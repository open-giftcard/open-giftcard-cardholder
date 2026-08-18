using Microsoft.AspNetCore.DataProtection;

namespace GiftCardCardholder.Web.Sessions;

/// <summary>
/// Encrypts backend access and refresh tokens at rest.
///
/// The session row is only a lookup target; the credential inside it is
/// protected independently, so reading the database is not enough to act as a
/// recipient. Keys must be persisted and protected by the deployment platform
/// in production — the Development profile keeps them under <c>.local</c>.
/// </summary>
internal sealed class BackendTokenProtector
{
    private const string Purpose = "GiftCardCardholder.Web.BackendTokens.v1";

    private readonly IDataProtector protector;

    public BackendTokenProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string value) => protector.Protect(value);

    /// <summary>
    /// Returns false when the payload cannot be decrypted — for example after a
    /// key rotation that dropped the old key. The caller treats that as a dead
    /// session and asks the recipient to sign in again, which is safer than
    /// throwing on every request.
    /// </summary>
    public bool TryUnprotect(string protectedValue, out string value)
    {
        try
        {
            value = protector.Unprotect(protectedValue);
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            value = string.Empty;
            return false;
        }
    }
}
