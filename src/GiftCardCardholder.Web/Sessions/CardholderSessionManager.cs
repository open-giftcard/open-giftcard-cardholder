using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Configuration;
using Microsoft.Extensions.Options;

namespace GiftCardCardholder.Web.Sessions;

/// <summary>
/// Owns the browser-facing session and the backend token lifecycle.
///
/// The browser only ever receives an opaque cookie. Access and refresh tokens
/// live in the cardholder database, encrypted, and are attached to backend
/// calls server-side. This is the ADR-037 boundary: a refresh token never
/// enters JavaScript-reachable storage.
/// </summary>
internal sealed partial class CardholderSessionManager(
    ICardholderSessionStore store,
    BackendTokenProtector protector,
    BackendClient backend,
    SessionRefreshCoordinator refreshCoordinator,
    IOptions<CardholderSessionOptions> options,
    TimeProvider timeProvider,
    ILogger<CardholderSessionManager> logger)
{
    /// <summary>
    /// Refresh this far ahead of expiry so a request already in flight does not
    /// arrive at the backend with a just-expired token.
    /// </summary>
    private static readonly TimeSpan RefreshLeadTime = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Browsers cap persistent cookies at 400 days, and a Max-Age beyond
    /// roughly 68 years overflows some clients' parsers outright. Capping keeps
    /// the header well-formed no matter what expiry the backend reports.
    /// </summary>
    private static readonly TimeSpan MaximumCookieLifetime = TimeSpan.FromDays(400);

    private readonly CardholderSessionOptions settings = options.Value;

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Cardholder session ended. Reason={Reason}")]
    private static partial void LogSessionEnded(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Warning,
        Message = "Backend revoke failed while signing out; the local session was still removed.")]
    private static partial void LogRevokeFailed(ILogger logger, Exception exception);

    public string SessionCookieName =>
        ResolveCookieName(settings.SessionCookieName);

    public string ActivationCookieName =>
        ResolveCookieName(settings.ActivationCookieName);

    /// <summary>
    /// Creates a session from a freshly issued backend token pair and sets the
    /// opaque cookie.
    /// </summary>
    public async Task SignInAsync(
        HttpContext httpContext,
        TokenPair tokens,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(tokens);

        var now = timeProvider.GetUtcNow();
        var cookieValue = OpaqueToken.Create();
        var session = new StoredSession(
            Guid.CreateVersion7(),
            userId,
            protector.Protect(tokens.AccessToken),
            protector.Protect(tokens.RefreshToken),
            tokens.AccessTokenExpiresAtUtc,
            tokens.RefreshTokenExpiresAtUtc);

        await store.CreateSessionAsync(
            session,
            OpaqueToken.Hash(cookieValue),
            now,
            cancellationToken);

        httpContext.Response.Cookies.Append(
            SessionCookieName,
            cookieValue,
            BuildCookieOptions(tokens.RefreshTokenExpiresAtUtc - now));
    }

    /// <summary>
    /// Returns the current session, refreshing the backend token pair when it
    /// is close to expiry. Returns null when there is no usable session, in
    /// which case the caller must send the recipient to sign in.
    /// </summary>
    public async Task<CardholderSession?> GetAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var cookieValue = httpContext.Request.Cookies[SessionCookieName];
        if (!OpaqueToken.HasValidShape(cookieValue))
        {
            return null;
        }

        var cookieHash = OpaqueToken.Hash(cookieValue!);
        var now = timeProvider.GetUtcNow();
        var stored = await store.FindSessionAsync(cookieHash, now, cancellationToken);
        if (stored is null)
        {
            ClearCookie(httpContext, SessionCookieName);
            return null;
        }

        if (!protector.TryUnprotect(stored.ProtectedAccessToken, out var accessToken) ||
            !protector.TryUnprotect(stored.ProtectedRefreshToken, out var refreshToken))
        {
            await EndSessionAsync(httpContext, cookieHash, "unprotect_failed", cancellationToken);
            return null;
        }

        var session = new CardholderSession(
            stored.Id,
            stored.UserId,
            accessToken,
            refreshToken,
            stored.AccessTokenExpiresAtUtc,
            stored.RefreshTokenExpiresAtUtc);

        if (session.AccessTokenExpiresAtUtc - now > RefreshLeadTime)
        {
            return session;
        }

        return await refreshCoordinator.RunAsync(
            session.Id,
            async () =>
            {
                // Another request may have refreshed while this one waited.
                var latest = await store.FindSessionAsync(
                    cookieHash,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                if (latest is null)
                {
                    ClearCookie(httpContext, SessionCookieName);
                    return null;
                }

                if (latest.AccessTokenExpiresAtUtc - timeProvider.GetUtcNow() > RefreshLeadTime &&
                    protector.TryUnprotect(latest.ProtectedAccessToken, out var freshAccess) &&
                    protector.TryUnprotect(latest.ProtectedRefreshToken, out var freshRefresh))
                {
                    return new CardholderSession(
                        latest.Id,
                        latest.UserId,
                        freshAccess,
                        freshRefresh,
                        latest.AccessTokenExpiresAtUtc,
                        latest.RefreshTokenExpiresAtUtc);
                }

                return await RefreshAsync(httpContext, latest, cookieHash, cancellationToken);
            },
            cancellationToken);
    }

    public async Task SignOutAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var cookieValue = httpContext.Request.Cookies[SessionCookieName];
        if (!OpaqueToken.HasValidShape(cookieValue))
        {
            ClearCookie(httpContext, SessionCookieName);
            return;
        }

        var cookieHash = OpaqueToken.Hash(cookieValue!);
        var stored = await store.FindSessionAsync(
            cookieHash,
            timeProvider.GetUtcNow(),
            cancellationToken);

        if (stored is not null &&
            protector.TryUnprotect(stored.ProtectedRefreshToken, out var refreshToken))
        {
            try
            {
                await backend.RevokeAsync(refreshToken, cancellationToken);
            }
            catch (BackendProblemException exception)
            {
                // Revoke is possession-based and idempotent; a failure here must
                // not strand the recipient in a session they asked to end.
                LogRevokeFailed(logger, exception);
            }
        }

        await EndSessionAsync(httpContext, cookieHash, "signed_out", cancellationToken);
    }

    /// <summary>
    /// Drops the local session without calling the backend. Used when the
    /// backend has already rejected the access token: the credential is dead,
    /// so a revoke call would add a round trip and tell us nothing.
    /// </summary>
    public async Task SignOutLocallyAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var cookieValue = httpContext.Request.Cookies[SessionCookieName];
        if (!OpaqueToken.HasValidShape(cookieValue))
        {
            ClearCookie(httpContext, SessionCookieName);
            return;
        }

        await EndSessionAsync(
            httpContext,
            OpaqueToken.Hash(cookieValue!),
            "backend_rejected",
            cancellationToken);
    }

    /// <summary>
    /// Stores the raw claim token from an activation link server-side and
    /// returns the cookie that selects it, so the secret can be removed from
    /// the address bar immediately.
    /// </summary>
    public async Task StartActivationAsync(
        HttpContext httpContext,
        string claimToken,
        ActivationPurpose purpose,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var now = timeProvider.GetUtcNow();
        var lifetime = TimeSpan.FromMinutes(settings.ActivationLifetimeMinutes);
        var cookieValue = OpaqueToken.Create();
        var activation = new StoredActivation(
            Guid.CreateVersion7(),
            protector.Protect(claimToken),
            Guid.NewGuid().ToString("N"),
            purpose,
            now + lifetime);

        await store.CreateActivationAsync(
            activation,
            OpaqueToken.Hash(cookieValue),
            now,
            cancellationToken);

        httpContext.Response.Cookies.Append(
            ActivationCookieName,
            cookieValue,
            BuildCookieOptions(lifetime));
    }

    public async Task<ActivationContext?> GetActivationAsync(
        HttpContext httpContext,
        ActivationPurpose expectedPurpose,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var cookieValue = httpContext.Request.Cookies[ActivationCookieName];
        if (!OpaqueToken.HasValidShape(cookieValue))
        {
            return null;
        }

        var stored = await store.FindActivationAsync(
            OpaqueToken.Hash(cookieValue!),
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (stored is null)
        {
            ClearCookie(httpContext, ActivationCookieName);
            return null;
        }

        if (stored.Purpose != expectedPurpose)
        {
            await EndActivationAsync(httpContext, cancellationToken);
            return null;
        }

        if (!protector.TryUnprotect(stored.ProtectedClaimToken, out var claimToken))
        {
            await EndActivationAsync(httpContext, cancellationToken);
            return null;
        }

        return new ActivationContext(
            stored.Id,
            claimToken,
            stored.IdempotencyKey,
            stored.Purpose,
            stored.ExpiresAtUtc);
    }

    public async Task EndActivationAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var cookieValue = httpContext.Request.Cookies[ActivationCookieName];
        if (OpaqueToken.HasValidShape(cookieValue))
        {
            await store.DeleteActivationAsync(OpaqueToken.Hash(cookieValue!), cancellationToken);
        }

        ClearCookie(httpContext, ActivationCookieName);
    }

    private async Task<CardholderSession?> RefreshAsync(
        HttpContext httpContext,
        StoredSession stored,
        string cookieHash,
        CancellationToken cancellationToken)
    {
        if (!protector.TryUnprotect(stored.ProtectedRefreshToken, out var refreshToken))
        {
            await EndSessionAsync(httpContext, cookieHash, "unprotect_failed", cancellationToken);
            return null;
        }

        TokenPair refreshed;
        try
        {
            refreshed = await backend.RefreshAsync(refreshToken, cancellationToken);
        }
        catch (BackendProblemException exception) when (exception.IsUnauthorized)
        {
            // The backend revoked the family, or the token was already used.
            // Failing closed is the only safe response.
            await EndSessionAsync(httpContext, cookieHash, "refresh_rejected", cancellationToken);
            return null;
        }

        var updated = stored with
        {
            ProtectedAccessToken = protector.Protect(refreshed.AccessToken),
            ProtectedRefreshToken = protector.Protect(refreshed.RefreshToken),
            AccessTokenExpiresAtUtc = refreshed.AccessTokenExpiresAtUtc,
            RefreshTokenExpiresAtUtc = refreshed.RefreshTokenExpiresAtUtc,
        };

        try
        {
            await store.UpdateSessionTokensAsync(
                updated,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The rotation already consumed the old refresh token, so a session
            // we cannot persist is dead. End it rather than leave the recipient
            // holding a cookie whose stored token no longer works.
            await EndSessionAsync(httpContext, cookieHash, "persist_failed", cancellationToken);
            throw;
        }

        return new CardholderSession(
            updated.Id,
            updated.UserId,
            refreshed.AccessToken,
            refreshed.RefreshToken,
            refreshed.AccessTokenExpiresAtUtc,
            refreshed.RefreshTokenExpiresAtUtc);
    }

    private async Task EndSessionAsync(
        HttpContext httpContext,
        string cookieHash,
        string reason,
        CancellationToken cancellationToken)
    {
        await store.DeleteSessionAsync(cookieHash, cancellationToken);
        ClearCookie(httpContext, SessionCookieName);
        LogSessionEnded(logger, reason);
    }

    private void ClearCookie(HttpContext httpContext, string name)
    {
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        httpContext.Response.Cookies.Delete(name, BuildCookieOptions(TimeSpan.Zero));
    }

    private CookieOptions BuildCookieOptions(TimeSpan maxAge) =>
        new()
        {
            HttpOnly = true,
            Secure = settings.RequireSecureCookies,
            // Lax keeps the cookie usable when the recipient arrives from an
            // email or messaging app, while still refusing cross-site posts.
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
            MaxAge = maxAge > TimeSpan.Zero
                ? (maxAge < MaximumCookieLifetime ? maxAge : MaximumCookieLifetime)
                : null,
        };

    /// <summary>
    /// The <c>__Host-</c> prefix is only legal on a Secure cookie. The
    /// Development profile may run over plain HTTP on a phone, so the prefix is
    /// dropped rather than silently issuing a cookie the browser will reject.
    /// </summary>
    private string ResolveCookieName(string configured) =>
        settings.RequireSecureCookies || !configured.StartsWith("__Host-", StringComparison.Ordinal)
            ? configured
            : configured["__Host-".Length..];
}
