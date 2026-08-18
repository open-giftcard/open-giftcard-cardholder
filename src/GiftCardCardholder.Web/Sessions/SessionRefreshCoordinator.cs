using System.Collections.Concurrent;

namespace GiftCardCardholder.Web.Sessions;

/// <summary>
/// Serializes refresh attempts per session.
///
/// The backend rotates refresh tokens and treats a replayed one as a possible
/// compromise, revoking the whole session family. Two concurrent requests from
/// the same phone — an image load racing a form post — would otherwise present
/// the same refresh token twice and log the recipient out. This gate makes the
/// second caller wait and then observe the already-refreshed session.
/// </summary>
internal sealed class SessionRefreshCoordinator
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> gates = new();

    public async Task<T> RunAsync<T>(
        Guid sessionId,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var gate = gates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1 && gates.TryRemove(sessionId, out var removed) &&
                !ReferenceEquals(removed, gate))
            {
                // Another session id collided on removal; put it back rather
                // than orphaning a gate another caller is already waiting on.
                gates.TryAdd(sessionId, removed);
            }
        }
    }
}
