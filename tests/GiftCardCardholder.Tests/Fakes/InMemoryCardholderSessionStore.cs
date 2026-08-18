using System.Collections.Concurrent;
using GiftCardCardholder.Web.Sessions;

namespace GiftCardCardholder.Tests.Fakes;

/// <summary>
/// Replaces PostgreSQL so the journey tests exercise real page, cookie, and
/// session behaviour without needing a database. Storage semantics that matter
/// to those tests — lookup by cookie hash, expiry, deletion — are preserved.
/// </summary>
internal sealed class InMemoryCardholderSessionStore : ICardholderSessionStore
{
    private readonly ConcurrentDictionary<string, StoredSession> sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StoredActivation> activations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, StoredPaymentCredential> paymentCredentials = new();

    public int SessionCount => sessions.Count;

    public int ActivationCount => activations.Count;

    public bool IsReady { get; set; } = true;

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken) =>
        Task.FromResult(IsReady);

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CreateSessionAsync(
        StoredSession session,
        string cookieHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        sessions[cookieHash] = session;
        return Task.CompletedTask;
    }

    public Task<StoredSession?> FindSessionAsync(
        string cookieHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(cookieHash, out var session))
        {
            return Task.FromResult<StoredSession?>(null);
        }

        return Task.FromResult<StoredSession?>(
            session.RefreshTokenExpiresAtUtc > now ? session : null);
    }

    public Task UpdateSessionTokensAsync(
        StoredSession session,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var (hash, existing) in sessions)
        {
            if (existing.Id == session.Id)
            {
                sessions[hash] = session;
                break;
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteSessionAsync(string cookieHash, CancellationToken cancellationToken)
    {
        sessions.TryRemove(cookieHash, out _);
        return Task.CompletedTask;
    }

    public Task CreateActivationAsync(
        StoredActivation activation,
        string cookieHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        activations[cookieHash] = activation;
        return Task.CompletedTask;
    }

    public Task<StoredActivation?> FindActivationAsync(
        string cookieHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!activations.TryGetValue(cookieHash, out var activation))
        {
            return Task.FromResult<StoredActivation?>(null);
        }

        return Task.FromResult<StoredActivation?>(
            activation.ExpiresAtUtc > now ? activation : null);
    }

    public Task DeleteActivationAsync(string cookieHash, CancellationToken cancellationToken)
    {
        activations.TryRemove(cookieHash, out _);
        return Task.CompletedTask;
    }

    public Task CreatePaymentCredentialAsync(
        StoredPaymentCredential credential,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        paymentCredentials[credential.PaymentTokenId] = credential;
        return Task.CompletedTask;
    }

    public Task<StoredPaymentCredential?> FindPaymentCredentialAsync(
        Guid paymentTokenId,
        Guid sessionId,
        Guid giftCardId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!paymentCredentials.TryGetValue(paymentTokenId, out var credential))
        {
            return Task.FromResult<StoredPaymentCredential?>(null);
        }

        // The real store binds on all three, so the fake must too, or a test
        // could not tell a leak across sessions or cards from a pass.
        var matches = credential.SessionId == sessionId &&
            credential.GiftCardId == giftCardId &&
            credential.ExpiresAtUtc > now;
        return Task.FromResult(matches ? credential : null);
    }

    public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
