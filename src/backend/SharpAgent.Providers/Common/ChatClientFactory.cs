using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Profiles;

using System.ClientModel;
namespace SharpAgent.Providers.Common;

/// <summary>
/// Builds the provider-neutral chat client for OpenAI-compatible chat.completions
/// endpoints. The key is resolved server-side and never leaves the process; the
/// returned client is consumed only by the agent runtime (FR-055, design 7.1).
/// </summary>
public static class ChatClientFactory
{
    public static IChatClient Create(
        string baseUrl,
        ModelProfile profile,
        ProviderSecretReference secretReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(secretReference);

        if (profile.EndpointKind != EndpointKind.ChatCompletions)
        {
            throw new InvalidOperationException(
                $"Endpoint style '{profile.EndpointKind}' is not supported for chat clients yet.");
        }

        var key = Environment.GetEnvironmentVariable(secretReference.EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"Provider secret '{secretReference.EnvironmentVariableName}' is not configured on this server.");
        }

        var options = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
        var chatClient = new ChatClient(profile.ProviderModelId, new ApiKeyCredential(key), options);
        return chatClient.AsIChatClient();
    }
}
