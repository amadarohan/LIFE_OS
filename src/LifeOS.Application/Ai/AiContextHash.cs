using System.Security.Cryptography;
using System.Text;

namespace LifeOS.Application.Ai;

/// <summary>
/// Single source of truth for <c>AiAction.ContextHash</c>. The hash covers the proposal payload
/// plus an optional fingerprint of the live state the proposal depends on, so execution can detect
/// that the world moved on between proposal and approval.
/// </summary>
/// <remarks>
/// A create-type proposal has no prior state to go stale, so its fingerprint is empty and the hash
/// is a pure function of the payload. An update-type proposal includes the target row's
/// <c>RowVersion</c>, which changes on any write — that is what makes the recheck meaningful.
/// </remarks>
public static class AiContextHash
{
    public static string Compute(string payloadJson, string? liveFingerprint = null)
    {
        var material = string.IsNullOrEmpty(liveFingerprint)
            ? payloadJson
            : payloadJson + "\n" + liveFingerprint;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash);
    }
}
