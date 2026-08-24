using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Profiles;
using SharpAgent.Provider.ContractTests.Support;
using SharpAgent.Providers;
using SharpAgent.Providers.Common;
using Xunit;

namespace SharpAgent.Provider.ContractTests;

/// <summary>
/// Adapter dispatch contracts: each adapter forwards to its configured endpoint,
/// honors server-side base-URL overrides, exposes its provider kind, and the
/// registry + DI composition resolve every kind.
/// </summary>
public sealed class AdapterDispatchTests : IDisposable
{
    private const string TestSecretVariable = "SHARPAGENT_TEST_PROVIDER_KEY";

    private readonly Dictionary<string, string?> _original = new()
    {
        [DeepSeekAdapter.BaseUrlVariable] = Environment.GetEnvironmentVariable(DeepSeekAdapter.BaseUrlVariable),
        [OpenRouterAdapter.BaseUrlVariable] = Environment.GetEnvironmentVariable(OpenRouterAdapter.BaseUrlVariable),
        [OpenCodeGoAdapter.BaseUrlVariable] = Environment.GetEnvironmentVariable(OpenCodeGoAdapter.BaseUrlVariable),
    };

    public AdapterDispatchTests()
    {
        Environment.SetEnvironmentVariable(TestSecretVariable, "test-key-value");
        foreach (var name in _original.Keys)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _original)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static ModelProfile Profile(ProviderKind provider, string displayName) =>
        ModelProfile.Register(provider, displayName, "model-id", EndpointKind.ChatCompletions, DateTimeOffset.UtcNow);

    private static ProviderValidationRunner Runner(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) });

    private const string SuccessSse =
        "data: {\"choices\":[{\"delta\":{\"content\":\"validation-ok\"},\"finish_reason\":null}]}\n\n" +
        "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":3}}\n\n" +
        "data: [DONE]\n\n";

    [Fact]
    public async Task DeepSeek_adapter_uses_its_default_endpoint()
    {
        var handler = StubHttpMessageHandler.Sse(SuccessSse);
        var adapter = new DeepSeekAdapter(Runner(handler));

        Assert.Equal(ProviderKind.DeepSeek, adapter.Provider);
        var result = await adapter.ValidateAsync(
            Profile(ProviderKind.DeepSeek, "DeepSeek Coder"),
            new ProviderSecretReference(TestSecretVariable),
            CancellationToken.None);

        Assert.Equal(ProviderErrorCategory.None, result.Error.Category);
        Assert.Equal("https://api.deepseek.com/chat/completions", Assert.Single(handler.Requests).Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task OpenRouter_adapter_uses_its_default_endpoint()
    {
        var handler = StubHttpMessageHandler.Sse(SuccessSse);
        var adapter = new OpenRouterAdapter(Runner(handler));

        Assert.Equal(ProviderKind.OpenRouter, adapter.Provider);
        var result = await adapter.ValidateAsync(
            Profile(ProviderKind.OpenRouter, "Router Model"),
            new ProviderSecretReference(TestSecretVariable),
            CancellationToken.None);

        Assert.Equal(ProviderErrorCategory.None, result.Error.Category);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", Assert.Single(handler.Requests).Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Server_side_base_url_overrides_apply()
    {
        Environment.SetEnvironmentVariable(OpenRouterAdapter.BaseUrlVariable, "https://internal.router.test");
        var handler = StubHttpMessageHandler.Sse(SuccessSse);
        var adapter = new OpenRouterAdapter(Runner(handler));

        await adapter.ValidateAsync(
            Profile(ProviderKind.OpenRouter, "Router Model"),
            new ProviderSecretReference(TestSecretVariable),
            CancellationToken.None);

        Assert.Equal(
            "https://internal.router.test/chat/completions",
            Assert.Single(handler.Requests).Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task OpenCode_Go_adapter_exposes_its_provider_kind_and_uses_its_default_endpoint()
    {
        var handler = StubHttpMessageHandler.Sse(SuccessSse);
        var adapter = new OpenCodeGoAdapter(Runner(handler));

        Assert.Equal(ProviderKind.OpenCodeGo, adapter.Provider);
        var result = await adapter.ValidateAsync(
            Profile(ProviderKind.OpenCodeGo, "Ox Alpha Free"),
            new ProviderSecretReference(TestSecretVariable),
            CancellationToken.None);

        Assert.Equal(ProviderErrorCategory.None, result.Error.Category);
        Assert.Equal(
            "https://api.opencode.go/v1/chat/completions",
            Assert.Single(handler.Requests).Request.RequestUri!.ToString());
    }

    [Fact]
    public void Registry_resolves_registered_kinds_and_returns_null_otherwise()
    {
        var registry = new ProviderAdapterRegistry(
        [
            new OpenCodeGoAdapter(Runner(StubHttpMessageHandler.Sse(SuccessSse))),
            new DeepSeekAdapter(Runner(StubHttpMessageHandler.Sse(SuccessSse))),
            new OpenRouterAdapter(Runner(StubHttpMessageHandler.Sse(SuccessSse))),
        ]);

        Assert.Equal(ProviderKind.OpenCodeGo, registry.Find(ProviderKind.OpenCodeGo)!.Provider);
        Assert.Equal(ProviderKind.DeepSeek, registry.Find(ProviderKind.DeepSeek)!.Provider);
        Assert.Equal(ProviderKind.OpenRouter, registry.Find(ProviderKind.OpenRouter)!.Provider);
        Assert.Null(registry.Find(ProviderKind.Fake));
    }

    [Fact]
    public void AddProviderAdapters_registers_every_adapter_and_the_registry()
    {
        var services = new ServiceCollection();
        services.AddProviderAdapters();

        Assert.Equal(3, services.Count(static descriptor => descriptor.ServiceType == typeof(IModelProviderAdapter)));
        Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IProviderAdapterRegistry));
        Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(ProviderValidationRunner));
        Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(HttpClient));
        Assert.Equal(ServiceLifetime.Singleton, services.Single(static descriptor => descriptor.ServiceType == typeof(IProviderAdapterRegistry)).Lifetime);
    }
}
