using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharpAgent.Application.Tools;

/// <summary>
/// Immutable action fingerprint binding one approval to exactly one proposal
/// (FR-045): run, workspace identity, action, resolved targets, command/patch
/// content and the operator policy document all feed the hash.
/// </summary>
public static class ActionFingerprint
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Compute(
        ToolProposal proposal,
        IReadOnlyList<ResolvedTarget> targets,
        string workspaceCanonicalRoot,
        string policyRulesJson,
        string? patchContentHash)
    {
        var payload = new
        {
            schema = 1,
            proposal.SessionId,
            proposal.RunId,
            proposal.WorkspaceId,
            workspaceCanonicalRoot,
            action = proposal.Action.ToString(),
            commandName = proposal.CommandName,
            arguments = proposal.Arguments ?? [],
            changeSetId = proposal.ChangeSetId,
            relativePath = proposal.RelativePath,
            searchQuery = proposal.SearchQuery,
            targets = targets.Select(static t => new { t.AbsolutePath, t.RelativePath }).ToArray(),
            patchContentHash = patchContentHash ?? string.Empty,
            policyRulesSha256 = Sha256Hex(policyRulesJson),
        };

        return Sha256Hex(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    public static string Sha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }
}
