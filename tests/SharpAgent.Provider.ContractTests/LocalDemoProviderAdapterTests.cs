using Microsoft.Extensions.AI;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Profiles;
using SharpAgent.Providers;
using AChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Xunit;

namespace SharpAgent.Provider.ContractTests;

public sealed class LocalDemoProviderAdapterTests
{
    [Fact]
    public async Task Local_demo_is_streaming_plan_only_and_never_requires_a_secret()
    {
        var adapter = new LocalDemoProviderAdapter();
        var profile = ModelProfile.Register(
            ProviderKind.Fake,
            "Offline demo (Plan only)",
            "sharpagent-local-demo",
            EndpointKind.None,
            DateTimeOffset.UtcNow,
            configReference: "local-demo");

        var validation = await adapter.ValidateAsync(
            profile,
            new SharpAgent.Application.Abstractions.ProviderSecretReference("unused"),
            CancellationToken.None);

        Assert.True(validation.Streaming);
        Assert.False(validation.ToolCalling);
        Assert.Equal(ProviderErrorCategory.None, validation.Error.Category);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in adapter
            .CreateChatClient(profile, new SharpAgent.Application.Abstractions.ProviderSecretReference("unused"))
            .GetStreamingResponseAsync(
                [new AChatMessage(ChatRole.User, "Try the local demo")],
                cancellationToken: CancellationToken.None))
        {
            updates.Add(update);
        }

        Assert.Contains(
            updates.SelectMany(static update => update.Contents),
            content => content is TextContent text && text.Text.Contains("Local demo plan complete", StringComparison.Ordinal));
    }
}
