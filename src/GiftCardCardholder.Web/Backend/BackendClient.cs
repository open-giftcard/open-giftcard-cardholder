using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GiftCardCardholder.Web.Backend;

/// <summary>
/// Stateless typed client over the pinned <c>/api/v1</c> contract.
///
/// It takes a bearer token explicitly rather than reading one from ambient
/// state, so the session layer stays the single owner of token lifecycle and
/// this type can be tested without a session. Nothing here writes a token,
/// password, or claim secret to a log.
/// </summary>
internal sealed partial class BackendClient(HttpClient httpClient, ILogger<BackendClient> logger)
{
    private const string ForwardedForHeader = "X-Forwarded-For";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Warning,
        Message = "Backend call failed. Status={Status} Code={Code} CorrelationId={CorrelationId}")]
    private static partial void LogBackendProblem(
        ILogger logger,
        int status,
        string code,
        Guid? correlationId);

    public Task<TokenPair> LoginWithEmailAsync(
        string email,
        string password,
        string? clientIpAddress,
        CancellationToken cancellationToken) =>
        PostAsync<LoginBody, TokenPair>(
            "auth/login",
            new LoginBody(email, password, null),
            accessToken: null,
            clientIpAddress,
            cancellationToken);

    public Task<TokenPair> LoginWithPhoneAsync(
        string phoneNumber,
        string password,
        string? clientIpAddress,
        CancellationToken cancellationToken) =>
        PostAsync<LoginBody, TokenPair>(
            "auth/login",
            new LoginBody(null, password, phoneNumber),
            accessToken: null,
            clientIpAddress,
            cancellationToken);

    public Task<TokenPair> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        PostAsync<RefreshBody, TokenPair>(
            "auth/refresh",
            new RefreshBody(refreshToken),
            accessToken: null,
            clientIpAddress: null,
            cancellationToken);

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/revoke")
        {
            Content = JsonContent.Create(new RevokeBody(refreshToken), options: JsonOptions),
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToProblemAsync(response, cancellationToken);
        }
    }

    /// <summary>
    /// Claims a delivered card. <paramref name="password"/> is omitted on the
    /// probe call: the backend requires one only when the verified contact has
    /// no identity yet, and a probe that is refused for a missing password
    /// rolls back without touching the invitation's failed-attempt counter.
    ///
    /// <paramref name="clientIpAddress"/> is the address this application
    /// observed on the browser connection. It is written to
    /// <c>X-Forwarded-For</c> so the backend's per-source claim quota partitions
    /// by recipient instead of collapsing onto this server's single address.
    /// </summary>
    public async Task<ClaimResult> ClaimAsync(
        string claimToken,
        string? password,
        string idempotencyKey,
        string? clientIpAddress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "gift-card-claims")
        {
            Content = JsonContent.Create(
                new ClaimBody(claimToken, password, idempotencyKey),
                options: JsonOptions),
        };

        // Set, never append. Any X-Forwarded-For the browser supplied is
        // discarded: relaying it would let a caller choose which rate-limit
        // partition to consume. The backend independently refuses the header
        // unless this application's own address is in its known-proxy list.
        SetForwardedFor(request, clientIpAddress);

        return await SendAsync<ClaimResult>(request, cancellationToken);
    }

    /// <summary>
    /// Claims a reseller e-pin. A signed-in caller supplies an access token and
    /// attaches the card to that exact identity. An anonymous caller supplies
    /// a new email/phone identity and password. The raw PIN and claim secret
    /// exist only in this server-to-server request.
    /// </summary>
    public async Task<ClaimResult> ClaimEpinAsync(
        string claimToken,
        string pin,
        string? contactType,
        string? recipientContact,
        string? password,
        string idempotencyKey,
        string? accessToken,
        string? clientIpAddress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "gift-card-claims")
        {
            Content = JsonContent.Create(
                new ClaimEpinBody(
                    claimToken,
                    pin,
                    contactType,
                    recipientContact,
                    password,
                    idempotencyKey),
                options: JsonOptions),
        };
        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        SetForwardedFor(request, clientIpAddress);
        return await SendAsync<ClaimResult>(request, cancellationToken);
    }

    public Task<CurrentUser> GetCurrentUserAsync(
        string accessToken,
        CancellationToken cancellationToken) =>
        GetAsync<CurrentUser>("me", accessToken, cancellationToken);

    public Task<OwnedGiftCardPage> GetMyGiftCardsAsync(
        string accessToken,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var path = $"me/gift-cards?limit={limit.ToString(CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrEmpty(cursor))
        {
            path += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        return GetAsync<OwnedGiftCardPage>(path, accessToken, cancellationToken);
    }

    public Task<OwnedGiftCardDetail> GetMyGiftCardAsync(
        string accessToken,
        Guid giftCardId,
        CancellationToken cancellationToken) =>
        GetAsync<OwnedGiftCardDetail>(
            $"me/gift-cards/{giftCardId:D}",
            accessToken,
            cancellationToken);

    public Task<CreatedGiftCardShare> CreateGiftCardShareAsync(
        string accessToken,
        Guid giftCardId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        PostAsync<CreateGiftCardShareBody, CreatedGiftCardShare>(
            $"me/gift-cards/{giftCardId:D}/shares",
            new CreateGiftCardShareBody(amount, idempotencyKey),
            accessToken,
            clientIpAddress: null,
            cancellationToken);

    public Task<CreatedDirectGiftCardShare> CreateDirectGiftCardShareAsync(
        string accessToken,
        Guid giftCardId,
        decimal amount,
        string contactType,
        string recipientContact,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        PostAsync<CreateDirectGiftCardShareBody, CreatedDirectGiftCardShare>(
            $"me/gift-cards/{giftCardId:D}/share-invitations",
            new CreateDirectGiftCardShareBody(
                amount,
                contactType,
                recipientContact,
                idempotencyKey),
            accessToken,
            clientIpAddress: null,
            cancellationToken);

    public Task<GiftCardSharePage> GetMyGiftCardSharesAsync(
        string accessToken,
        int limit,
        string? cursor,
        string? kind,
        string? state,
        string? direction,
        CancellationToken cancellationToken)
    {
        var path = $"me/shares?limit={limit.ToString(CultureInfo.InvariantCulture)}";
        path = AppendQuery(path, "cursor", cursor);
        path = AppendQuery(path, "kind", kind);
        path = AppendQuery(path, "state", state);
        path = AppendQuery(path, "direction", direction);
        return GetAsync<GiftCardSharePage>(path, accessToken, cancellationToken);
    }

    public Task<GiftCardShare> CancelGiftCardShareAsync(
        string accessToken,
        Guid shareId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        PostAsync<CancelGiftCardShareBody, GiftCardShare>(
            $"me/shares/{shareId:D}/cancel",
            new CancelGiftCardShareBody(idempotencyKey),
            accessToken,
            clientIpAddress: null,
            cancellationToken);

    public Task<ClaimedGiftCardShare> ClaimGiftCardShareAsync(
        string accessToken,
        string claimToken,
        string pin,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        PostAsync<ClaimGiftCardShareBody, ClaimedGiftCardShare>(
            "share-claims",
            new ClaimGiftCardShareBody(claimToken, pin, idempotencyKey),
            accessToken,
            clientIpAddress: null,
            cancellationToken);

    public Task<ClaimedDirectGiftCardShare> ClaimDirectGiftCardShareAsync(
        string claimToken,
        string? password,
        string idempotencyKey,
        string? clientIpAddress,
        CancellationToken cancellationToken) =>
        PostAsync<ClaimDirectGiftCardShareBody, ClaimedDirectGiftCardShare>(
            "share-invitation-claims",
            new ClaimDirectGiftCardShareBody(claimToken, password, idempotencyKey),
            accessToken: null,
            clientIpAddress,
            cancellationToken);

    public Task<FinancialHistoryPage> GetMyGiftCardHistoryAsync(
        string accessToken,
        Guid giftCardId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var path = $"me/gift-cards/{giftCardId:D}/history" +
            $"?limit={limit.ToString(CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrEmpty(cursor))
        {
            path += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        return GetAsync<FinancialHistoryPage>(path, accessToken, cancellationToken);
    }

    public Task SuspendMyGiftCardAsync(
        string accessToken,
        Guid giftCardId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        PostOwnedLifecycleAsync(
            accessToken,
            giftCardId,
            "suspend",
            idempotencyKey,
            cancellationToken);

    public Task ReactivateMyGiftCardAsync(
        string accessToken,
        Guid giftCardId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        PostOwnedLifecycleAsync(
            accessToken,
            giftCardId,
            "reactivate",
            idempotencyKey,
            cancellationToken);

    public async Task<IssuedPaymentToken> IssuePaymentTokenAsync(
        string accessToken,
        Guid giftCardId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"me/gift-cards/{giftCardId:D}/payment-tokens");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendAsync<IssuedPaymentToken>(request, cancellationToken);
    }

    public Task<PaymentTokenStatus> GetPaymentTokenStatusAsync(
        string accessToken,
        Guid giftCardId,
        Guid paymentTokenId,
        CancellationToken cancellationToken) =>
        GetAsync<PaymentTokenStatus>(
            $"me/gift-cards/{giftCardId:D}/payment-tokens/{paymentTokenId:D}",
            accessToken,
            cancellationToken);

    private async Task PostOwnedLifecycleAsync(
        string accessToken,
        Guid giftCardId,
        string action,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"me/gift-cards/{giftCardId:D}/lifecycle/{action}")
        {
            Content = JsonContent.Create(
                new OwnGiftCardLifecycleBody(idempotencyKey),
                options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToProblemAsync(response, cancellationToken);
        }

        // The command response is intentionally not bound into a competing
        // lifecycle model. The page reloads exact-owner detail and history
        // from the backend after the redirect and renders that authoritative
        // state instead.
    }

    private async Task<TResponse> GetAsync<TResponse>(
        string path,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendAsync<TResponse>(request, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        string? accessToken,
        string? clientIpAddress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        SetForwardedFor(request, clientIpAddress);

        return await SendAsync<TResponse>(request, cancellationToken);
    }

    private static void SetForwardedFor(
        HttpRequestMessage request,
        string? clientIpAddress)
    {
        if (string.IsNullOrEmpty(clientIpAddress))
        {
            return;
        }

        // Set, never append or copy. The caller supplies only the address
        // observed on the browser connection, not an incoming request header.
        request.Headers.Remove(ForwardedForHeader);
        request.Headers.TryAddWithoutValidation(ForwardedForHeader, clientIpAddress);
    }

    private static string AppendQuery(string path, string name, string? value) =>
        string.IsNullOrEmpty(value)
            ? path
            : $"{path}&{name}={Uri.EscapeDataString(value)}";

    private async Task<TResponse> SendAsync<TResponse>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToProblemAsync(response, cancellationToken);
        }

        var payload = await response.Content
            .ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
        return payload ?? throw new BackendProblemException(
            response.StatusCode,
            "backend.empty_response",
            "The backend returned an empty response.",
            correlationId: null);
    }

    private async Task<BackendProblemException> ToProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var code = string.Empty;
        string? detail = null;
        Guid? correlationId = null;

        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("code", out var codeElement) &&
                codeElement.ValueKind == JsonValueKind.String)
            {
                code = codeElement.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("detail", out var detailElement) &&
                detailElement.ValueKind == JsonValueKind.String)
            {
                detail = detailElement.GetString();
            }

            if (root.TryGetProperty("correlationId", out var correlationElement) &&
                correlationElement.TryGetGuid(out var parsed))
            {
                correlationId = parsed;
            }
        }
        catch (JsonException)
        {
            // A non-ProblemDetails body (a proxy error page, for example) still
            // has to fail closed rather than surface raw upstream content.
            code = "backend.unreadable_response";
        }

        LogBackendProblem(logger, (int)response.StatusCode, code, correlationId);
        return new BackendProblemException(response.StatusCode, code, detail, correlationId);
    }
}
