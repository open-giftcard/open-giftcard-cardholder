namespace GiftCardCardholder.Web.Sessions;

internal enum ActivationPurpose
{
    GiftCardDistribution = 1,
    ProtectedShare = 2,
    DirectShare = 3,
    PartnerEpin = 4,
}

/// <summary>
/// A session as persisted: backend tokens are still encrypted here.
/// </summary>
internal sealed record StoredSession(
    Guid Id,
    Guid UserId,
    string ProtectedAccessToken,
    string ProtectedRefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset RefreshTokenExpiresAtUtc);

/// <summary>
/// A session in use: tokens are decrypted and must never be written to a
/// response, a log, a view, or a cookie.
/// </summary>
internal sealed record CardholderSession(
    Guid Id,
    Guid UserId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset RefreshTokenExpiresAtUtc);

/// <summary>
/// A pre-authentication activation context as persisted, holding the encrypted
/// claim token from the recipient's activation link.
/// </summary>
internal sealed record StoredActivation(
    Guid Id,
    string ProtectedClaimToken,
    string IdempotencyKey,
    ActivationPurpose Purpose,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// An issued checkout credential as persisted, so the QR page survives a
/// reload, a language change, or a theme change.
///
/// It exists only for the credential's own sixty seconds and is bound to the
/// session that asked for it. Both presentations are encrypted at rest for the
/// same reason backend tokens are: reading the database must not be enough to
/// pay with somebody's card.
/// </summary>
internal sealed record StoredPaymentCredential(
    Guid PaymentTokenId,
    Guid SessionId,
    Guid GiftCardId,
    string GiftCardPublicReference,
    string ProtectedRawToken,
    string ProtectedNumericCode,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// A decrypted activation context.
///
/// <see cref="ClaimToken"/> is the raw single-use secret from the recipient's
/// link. It stays server-side: it is never rendered into a page, placed in a
/// form field, logged, or echoed back to the browser.
/// </summary>
internal sealed record ActivationContext(
    Guid Id,
    string ClaimToken,
    string IdempotencyKey,
    ActivationPurpose Purpose,
    DateTimeOffset ExpiresAtUtc);
