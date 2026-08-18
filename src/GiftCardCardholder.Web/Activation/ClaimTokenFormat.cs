using System.Buffers.Text;

namespace GiftCardCardholder.Web.Activation;

/// <summary>
/// Shape validation for the claim token carried by an activation link.
///
/// This mirrors the backend's parsing rules so an obviously malformed link is
/// rejected without a network round trip and without consuming the claim
/// endpoint's rate limit. It is a convenience only: the backend remains the
/// sole authority on whether an invitation exists, is still claimable, and
/// matches the secret. Nothing here inspects or compares the secret itself.
/// </summary>
internal static class ClaimTokenFormat
{
    /// <summary>A GUID in "N" format precedes the separator.</summary>
    private const int InvitationIdLength = 32;

    /// <summary>Base64url length of the backend's 32-byte secret.</summary>
    private const int EncodedSecretLength = 43;

    public static bool TryParse(string? token, out Guid invitationId)
    {
        invitationId = Guid.Empty;
        var candidate = token?.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var separator = candidate.IndexOf('.', StringComparison.Ordinal);
        if (separator != InvitationIdLength ||
            !Guid.TryParseExact(candidate[..separator], "N", out invitationId))
        {
            invitationId = Guid.Empty;
            return false;
        }

        var encodedSecret = candidate[(separator + 1)..];
        if (encodedSecret.Length != EncodedSecretLength)
        {
            invitationId = Guid.Empty;
            return false;
        }

        Span<byte> secret = stackalloc byte[OpaqueSecretByteCount];
        if (!Base64Url.TryDecodeFromChars(encodedSecret, secret, out var written) ||
            written != OpaqueSecretByteCount)
        {
            invitationId = Guid.Empty;
            return false;
        }

        secret.Clear();
        return true;
    }

    private const int OpaqueSecretByteCount = 32;
}
