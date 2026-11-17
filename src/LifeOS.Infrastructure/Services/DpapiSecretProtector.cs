using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using LifeOS.Application.Abstractions;

namespace LifeOS.Infrastructure.Services;

/// <summary>
/// Protects sensitive setting values (API keys) at rest using Windows DPAPI scoped to the current
/// user. Values are prefixed so already-protected data is never double-encrypted.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    private const string Prefix = "dpapi:v1:";

    // Extra entropy binds the ciphertext to this application, so another app running as the same
    // user cannot decrypt it even if it reads the database.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LifeOS.Settings.v1");

    public string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return plainText;
        }

        if (IsProtected(plainText))
        {
            return plainText;
        }

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipher = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(cipher);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return protectedText;
        }

        if (!IsProtected(protectedText))
        {
            // Legacy or plaintext value: return as-is rather than throwing, so a partially
            // migrated database still starts.
            return protectedText;
        }

        var payload = protectedText[Prefix.Length..];
        var cipher = Convert.FromBase64String(payload);
        var plainBytes = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }

    public bool IsProtected(string value)
        => !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);
}
