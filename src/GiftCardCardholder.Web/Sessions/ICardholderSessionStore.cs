namespace GiftCardCardholder.Web.Sessions;

/// <summary>
/// Durable storage for cardholder sessions and pre-authentication activation
/// contexts. This store is owned by the cardholder application and is a
/// separate database from the backend's — it holds no business, financial, or
/// authorization state, only what is needed to keep backend tokens out of the
/// browser.
/// </summary>
internal interface ICardholderSessionStore
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);

    Task InitializeAsync(CancellationToken cancellationToken);

    Task CreateSessionAsync(
        StoredSession session,
        string cookieHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<StoredSession?> FindSessionAsync(
        string cookieHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task UpdateSessionTokensAsync(
        StoredSession session,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task DeleteSessionAsync(string cookieHash, CancellationToken cancellationToken);

    Task CreateActivationAsync(
        StoredActivation activation,
        string cookieHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<StoredActivation?> FindActivationAsync(
        string cookieHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task DeleteActivationAsync(string cookieHash, CancellationToken cancellationToken);

    Task CreatePaymentCredentialAsync(
        StoredPaymentCredential credential,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the credential only to the exact session, card, and identifier
    /// it was issued for, and only while it is still live.
    /// </summary>
    Task<StoredPaymentCredential?> FindPaymentCredentialAsync(
        Guid paymentTokenId,
        Guid sessionId,
        Guid giftCardId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Removes rows whose usefulness has already expired.</summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
