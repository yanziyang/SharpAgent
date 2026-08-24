using System.Text.RegularExpressions;

namespace SharpAgent.Application.Security;

/// <summary>
/// Defense-in-depth redaction for free-text fields that could accidentally carry a
/// credential-shaped value. Matches high-confidence secret shapes and replaces them
/// with a fixed marker; the matched values are never echoed anywhere.
/// </summary>
public static partial class SecretRedactor
{
    [GeneratedRegex(
        "(sk-[A-Za-z0-9_-]{12,})|(ghp_[A-Za-z0-9]{20,})|(AKIA[0-9A-Z]{16})|(xox[baprs]-[A-Za-z0-9-]{10,})|(-----BEGIN[ A-Z]*PRIVATE KEY-----)|((?i:bearer)\\s+[A-Za-z0-9._~+/-]{18,}=*)")]
    private static partial Regex SecretShape();

    public const string Mask = "[redacted]";

    /// <summary>Returns the input with every secret-shaped value masked; null stays null.</summary>
    public static string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return SecretShape().Replace(text, Mask);
    }
}
