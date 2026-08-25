using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using SharpAgent.Domain.Profiles;
using SharpAgent.Infrastructure.Persistence;
using SharpAgent.Infrastructure.Setup;
using SharpAgent.Infrastructure.Tests.Support;
using Xunit;

namespace SharpAgent.Infrastructure.Tests.Persistence;

public sealed class OpenCodeGoCatalogSetupTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    [Fact]
    public void Environment_model_ids_map_only_to_the_approved_display_names()
    {
        var configuration = new DictionaryConfiguration(new Dictionary<string, string?>
        {
            [OpenCodeGoCatalogOptions.ProviderModelIdsEnvironmentVariable] = "model-ox, model-muse, model-mimo",
        });

        var options = OpenCodeGoCatalogOptions.FromConfiguration(configuration);

        Assert.Collection(
            options.Profiles,
            first => Assert.Equal(("Ox Alpha Free", "model-ox"), (first.DisplayName, first.ProviderModelId)),
            second => Assert.Equal(("Muse Spark 1.2 Contributor", "model-muse"), (second.DisplayName, second.ProviderModelId)),
            third => Assert.Equal(("MiMo-V2.5", "model-mimo"), (third.DisplayName, third.ProviderModelId)));
    }

    [Fact]
    public async Task Public_catalog_maps_only_allowlisted_models_without_a_secret_header()
    {
        var handler = new CatalogHandler(
            """{"object":"list","data":[{"id":"mimo-v2.5"},{"id":"unapproved-model"},{"id":"muse-spark-1.2-contributor"},{"id":"ox-alpha-free"}]}""");
        var client = new OpenCodeGoModelCatalogClient(new HttpClient(handler));

        var profiles = await client.FetchApprovedProfilesAsync("https://catalog.test/models");

        Assert.Collection(
            profiles,
            first =>
            {
                Assert.Equal("Ox Alpha Free", first.DisplayName);
                Assert.Equal("ox-alpha-free", first.ProviderModelId);
                Assert.Equal(EndpointKind.ChatCompletions, first.EndpointKind);
            },
            second =>
            {
                Assert.Equal("Muse Spark 1.2 Contributor", second.DisplayName);
                Assert.Equal("muse-spark-1.2-contributor", second.ProviderModelId);
                Assert.Equal(EndpointKind.Responses, second.EndpointKind);
            },
            third =>
            {
                Assert.Equal("MiMo-V2.5", third.DisplayName);
                Assert.Equal("mimo-v2.5", third.ProviderModelId);
                Assert.Equal(EndpointKind.ChatCompletions, third.EndpointKind);
            });

        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public void Single_profile_configuration_rejects_unapproved_display_names()
    {
        var configuration = new DictionaryConfiguration(new Dictionary<string, string?>
        {
            [OpenCodeGoCatalogOptions.SingleDisplayNameConfigurationKey] = "Unapproved model",
            [OpenCodeGoCatalogOptions.SingleProviderModelIdConfigurationKey] = "provider-id",
        });

        var options = OpenCodeGoCatalogOptions.FromConfiguration(configuration);

        Assert.Empty(options.Profiles);
    }

    [Fact]
    public async Task Seeder_persists_non_secret_profiles_idempotently()
    {
        await _database.InitializeAsync();
        var options = new OpenCodeGoCatalogOptions(
        [
            new OpenCodeGoProfileSeed("Ox Alpha Free", "provider-ox"),
        ])
        {
            RemoteCatalogEnabled = false,
        };
        var seeder = new OpenCodeGoCatalogSeeder(
            CreateFactory(),
            options,
            new OpenCodeGoModelCatalogClient(new HttpClient(new CatalogHandler("{}"))));

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        await using var context = _database.OpenContext();
        var profile = Assert.Single(await context.ModelProfiles.ToListAsync());
        Assert.Equal(ProviderKind.OpenCodeGo, profile.Provider);
        Assert.Equal("provider-ox", profile.ProviderModelId);
        Assert.Equal(OpenCodeGoCatalogOptions.SecretReference, profile.ConfigReference);
        Assert.True(profile.Enabled);
        Assert.True(profile.CanPlan());
        Assert.False(profile.CanExecute());
    }

    private DbContextFactoryStub CreateFactory() => new(
        new DbContextOptionsBuilder<SharpAgentDbContext>()
            .UseSqlite(_database.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options);

    public void Dispose() => _database.Dispose();

    private sealed class DictionaryConfiguration(IReadOnlyDictionary<string, string?> values) : IConfiguration
    {
        public string? this[string key]
        {
            get => values.TryGetValue(key, out var value) ? value : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => NoopChangeToken.Instance;

        public IConfigurationSection GetSection(string key) => new EmptyConfigurationSection(key, this[key]);
    }

    private sealed class EmptyConfigurationSection(string key, string? value) : IConfigurationSection
    {
        public string Key => key;

        public string Path => key;

        public string? Value
        {
            get => value;
            set => throw new NotSupportedException();
        }

        public string? this[string key]
        {
            get => null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => NoopChangeToken.Instance;

        public IConfigurationSection GetSection(string key) => new EmptyConfigurationSection(key, null);
    }

    private sealed class NoopChangeToken : IChangeToken
    {
        public static NoopChangeToken Instance { get; } = new();

        public bool HasChanged => false;

        public bool ActiveChangeCallbacks => false;

        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) =>
            NoopDisposable.Instance;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class CatalogHandler(string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
