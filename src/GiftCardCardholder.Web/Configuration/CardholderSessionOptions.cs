namespace GiftCardCardholder.Web.Configuration;

/// <summary>
/// Cookie and lifetime settings for the server-side cardholder session.
/// Backend access and refresh tokens never leave the server, so these settings
/// govern only the opaque cookies that select a server-side record.
/// </summary>
public sealed class CardholderSessionOptions
{
    public const string SectionName = "CardholderSession";

    /// <summary>
    /// Name of the authenticated session cookie. Production uses the
    /// <c>__Host-</c> prefix, which requires Secure, path <c>/</c>, and no
    /// Domain attribute.
    /// </summary>
    public string SessionCookieName { get; set; } = "__Host-cardholder-session";

    /// <summary>
    /// Name of the short-lived pre-authentication activation cookie.
    /// </summary>
    public string ActivationCookieName { get; set; } = "__Host-cardholder-activation";

    /// <summary>
    /// When false, cookies are issued without the Secure attribute so the
    /// Development profile can run over plain HTTP on a phone or emulator.
    /// It must remain true everywhere else.
    /// </summary>
    public bool RequireSecureCookies { get; set; } = true;

    /// <summary>
    /// How long an activation context survives after the recipient opens their
    /// link. Independent of the backend claim-token lifetime, which the backend
    /// alone enforces.
    /// </summary>
    public int ActivationLifetimeMinutes { get; set; } = 30;
}
