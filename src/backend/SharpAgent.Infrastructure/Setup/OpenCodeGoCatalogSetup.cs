using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SharpAgent.Application.Providers;
using SharpAgent.Domain.Profiles;
using SharpAgent.Infrastructure.Persistence;

namespace SharpAgent.Infrastructure.Setup;

/// <summary>
/// Non-secret OpenCode Go profile configuration. Provider model identifiers are
/// supplied by the local operator; credentials remain in the referenced server
/// environment variable and are never persisted or returned to the browser.
/// </summary>
public sealed record OpenCodeGoCatalogOptions(
    IReadOnlyList<OpenCodeGoProfileSeed> Profiles)
{
    public const string ModelsEndpointConfigurationKey = "OpenCodeGo:ModelsEndpoint";
    public const string CatalogEnabledConfigurationKey = "OpenCodeGo:CatalogEnabled";
    public const string DefaultModelsEndpoint = "https://opencode.ai/zen/go/v1/models";

    public const string ProviderModelIdsEnvironmentVariable =
        "SHARPAGENT_OPENCODE_GO_PROVIDER_MODEL_IDS";

    public const string ProviderModelIdsConfigurationKey =
        "OpenCodeGo:ProviderModelIds";

    public const string SingleDisplayNameConfigurationKey =
        "OpenCodeGo:DisplayName";

    public const string SingleProviderModelIdConfigurationKey =
        "OpenCodeGo:ProviderModelId";

    public const string SecretReference = "SHARPAGENT_OPENCODE_GO_API_KEY";

    public string ModelsEndpoint { get; init; } = DefaultModelsEndpoint;

    public bool RemoteCatalogEnabled { get; init; } = true;

    public static OpenCodeGoCatalogOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var singleDisplayName = configuration[SingleDisplayNameConfigurationKey];
        var singleProviderModelId = configuration[SingleProviderModelIdConfigurationKey];
        if (!string.IsNullOrWhiteSpace(singleDisplayName)
            && !string.IsNullOrWhiteSpace(singleProviderModelId)
            && OpenCodeGoPlanAllowlist.IsAllowed(singleDisplayName))
        {
            return new([CreateProfileSeed(singleDisplayName, singleProviderModelId)]);
        }

        var configuredIds = configuration[ProviderModelIdsConfigurationKey]
            ?? configuration[ProviderModelIdsEnvironmentVariable];
        if (string.IsNullOrWhiteSpace(configuredIds))
        {
            return new([]);
        }

        var providerModelIds = configuredIds.Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var profiles = OpenCodeGoPlanAllowlist.ApprovedDisplayNames
            .Select((displayName, index) => index < providerModelIds.Length
                ? CreateProfileSeed(displayName, providerModelIds[index])
                : null)
            .Where(static profile => profile is not null)
            .Select(static profile => profile!)
            .ToArray();

        return new(profiles)
        {
            ModelsEndpoint = configuration[ModelsEndpointConfigurationKey]
                ?? DefaultModelsEndpoint,
            RemoteCatalogEnabled = !bool.TryParse(
                configuration[CatalogEnabledConfigurationKey],
                out var catalogEnabled)
                || catalogEnabled,
        };
    }

    private static OpenCodeGoProfileSeed CreateProfileSeed(string displayName, string providerModelId) =>
        new(
            displayName,
            providerModelId,
            OpenCodeGoPlanAllowlist.FindByProviderModelId(providerModelId)?.EndpointKind
                ?? EndpointKind.ChatCompletions);
}

public sealed record OpenCodeGoProfileSeed(
    string DisplayName,
    string ProviderModelId,
    EndpointKind EndpointKind = EndpointKind.ChatCompletions);

/// <summary>
/// Retrieves the public OpenCode Go model catalog. The endpoint contains model
/// identifiers only; no provider credential is required or sent.
/// </summary>
public sealed class OpenCodeGoModelCatalogClient(HttpClient httpClient)
{
    private const int MaxCatalogBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<OpenCodeGoProfileSeed>> FetchApprovedProfilesAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        try
        {
            using var response = await httpClient
                .GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentLength is > MaxCatalogBytes)
            {
                return [];
            }

            var payload = await response.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (payload.Length > MaxCatalogBytes)
            {
                return [];
            }

            var catalog = await JsonSerializer.DeserializeAsync<OpenCodeGoModelsResponse>(
                new MemoryStream(payload),
                JsonOptions,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (catalog?.Data is null)
            {
                return [];
            }

            var availableIds = catalog.Data
                .Select(static model => model.Id)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);

            return OpenCodeGoPlanAllowlist.ApprovedModels
                .Where(model => availableIds.Contains(model.ProviderModelId))
                .Select(static model => new OpenCodeGoProfileSeed(
                    model.DisplayName,
                    model.ProviderModelId,
                    model.EndpointKind))
                .ToArray();
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    private sealed record OpenCodeGoModelsResponse(IReadOnlyList<OpenCodeGoModel> Data);

    private sealed record OpenCodeGoModel(string Id);
}

/// <summary>
/// Seeds operator-configured OpenCode Go profiles without reading credentials or
/// making a provider call. Profiles remain Plan-eligible while unvalidated and
/// cannot enter Execute mode until a successful validation records capabilities.
/// </summary>
public sealed class OpenCodeGoCatalogSeeder(
    IDbContextFactory<SharpAgentDbContext> contextFactory,
    OpenCodeGoCatalogOptions options,
    OpenCodeGoModelCatalogClient catalogClient)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var profiles = options.Profiles;
        if (options.RemoteCatalogEnabled)
        {
            var discoveredProfiles = await catalogClient
                .FetchApprovedProfilesAsync(options.ModelsEndpoint, cancellationToken)
                .ConfigureAwait(false);
            if (discoveredProfiles.Count > 0)
            {
                profiles = discoveredProfiles;
            }
        }

        if (profiles.Count == 0)
        {
            return;
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var nowUtc = DateTimeOffset.UtcNow;
        var changed = false;
        foreach (var configured in profiles)
        {
            if (await context.ModelProfiles.AnyAsync(
                    profile => profile.Provider == ProviderKind.OpenCodeGo
                        && profile.DisplayName == configured.DisplayName,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                continue;
            }

            var profile = ModelProfile.Register(
                ProviderKind.OpenCodeGo,
                configured.DisplayName,
                configured.ProviderModelId,
                configured.EndpointKind,
                nowUtc,
                configReference: OpenCodeGoCatalogOptions.SecretReference);
            profile.Enable(nowUtc);
            await context.ModelProfiles.AddAsync(profile, cancellationToken).ConfigureAwait(false);
            changed = true;
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>Runs the non-secret OpenCode catalog seed after persistence startup.</summary>
public sealed class OpenCodeGoCatalogStartupService(OpenCodeGoCatalogSeeder seeder) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => seeder.SeedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
