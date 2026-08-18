using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace GiftCardCardholder.Web.Sessions;

/// <summary>
/// Opaque, high-entropy cookie values that select a server-side record.
///
/// The cookie itself carries no meaning and no claims — it is a lookup key. Only
/// a SHA-256 hash is stored, so a leaked database snapshot cannot be replayed as
/// a live session. A plain hash (rather than a password KDF) is appropriate
/// because the value is 256 random bits, not a guessable secret.
/// </summary>
internal static class OpaqueToken
{
    public const int ByteCount = 32;

    /// <summary>Base64url length of a <see cref="ByteCount"/>-byte value.</summary>
    private const int EncodedLength = 43;

    public static string Create() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteCount));

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// Cheap shape check so a malformed or truncated cookie is discarded before
    /// it reaches the database.
    /// </summary>
    public static bool HasValidShape(string? value) =>
        value is { Length: EncodedLength } &&
        value.All(static c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
