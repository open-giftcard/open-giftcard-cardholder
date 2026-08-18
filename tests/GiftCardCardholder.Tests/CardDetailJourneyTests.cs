using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Display;

namespace GiftCardCardholder.Tests;

public sealed partial class CardDetailJourneyTests : IDisposable
{
    private const string AccessTokenValue = "card-detail-access-token";
    private const string RefreshTokenValue = "card-detail-refresh-token";
    private const string GiftCardId = "0195c0de-0000-7000-8000-000000000021";
    private const string FundingOrganizationId = "0195c0de-0000-7000-8000-000000000031";
    private const string IssuingOrganizationId = "0195c0de-0000-7000-8000-000000000032";
    private const string PublicReference = "DEMO-OWNED-0021";
    private const string LifecyclePath = "/api/v1/me/gift-cards/" + GiftCardId + "/lifecycle/";
    private const string DetailPath = "/api/v1/me/gift-cards/" + GiftCardId;
    private const string HistoryPath = DetailPath + "/history";

    private static string TokenPairJson => $$"""
        {
          "accessToken": "{{AccessTokenValue}}",
          "accessTokenExpiresAtUtc": "{{Iso(TimeSpan.FromMinutes(15))}}",
          "refreshToken": "{{RefreshTokenValue}}",
          "refreshTokenExpiresAtUtc": "{{Iso(TimeSpan.FromDays(30))}}"
        }
        """;

    private const string CurrentUserJson = """
        {
          "id": "0195c0de-0000-7000-8000-000000000009",
          "email": "recipient@example.com",
          "phoneNumber": null,
          "status": "Active",
          "contextType": "Identity"
        }
        """;

    private readonly CardholderAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task CardListLinksToBackendReturnedDetailWithoutAnIdentifierForm()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        factory.Backend.Enqueue("/api/v1/me/gift-cards", HttpStatusCode.OK, ListJson);

        using var response = await browser.GetAsync(new Uri("/cards", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"href=\"/cards/{GiftCardId}\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"giftCardId\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetailRendersAuthoritativeBalanceAndHistoryWithoutInternalOwnerIdentifiers()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        EnqueueDetailAndHistory("Active");

        using var response = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(PublicReference, body, StringComparison.Ordinal);
        Assert.Contains("73.25 TRY", body, StringComparison.Ordinal);
        Assert.Contains("Card loaded", body, StringComparison.Ordinal);
        Assert.Contains("Card suspended", body, StringComparison.Ordinal);
        Assert.Contains("Suspend card", body, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(body, "<h1[ >]", RegexOptions.IgnoreCase));
        Assert.Contains("aria-labelledby=\"card-controls-heading\"", body, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"activity-heading\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Reactivate card", body, StringComparison.Ordinal);
        Assert.DoesNotContain(">Cancel", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FundingOrganizationId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(IssuingOrganizationId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessTokenValue, body, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshTokenValue, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpaqueHistoryCursorIsEncodedAndForwardedUnchanged()
    {
        const string cursor = "opaque+cursor/==";
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        EnqueueDetailAndHistory("Active", cursor);

        using var first = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}", UriKind.Relative));
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.Contains("cursor=opaque%2Bcursor%2F%3D%3D", firstBody, StringComparison.Ordinal);

        EnqueueDetailAndHistory("Active");
        using var second = await browser.GetAsync(
            new Uri(
                $"/cards/{GiftCardId}?cursor=opaque%2Bcursor%2F%3D%3D",
                UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var historyRequest = factory.Backend.Requests.Last(request =>
            request.Path.EndsWith(HistoryPath, StringComparison.Ordinal));
        Assert.EndsWith(
            "?limit=10&cursor=opaque%2Bcursor%2F%3D%3D",
            historyRequest.PathAndQuery,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingAndNonOwnedCardsShareOneSafeNotFoundExperience()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        factory.Backend.EnqueueProblem(DetailPath, HttpStatusCode.NotFound, "gift_card.not_found");

        using var response = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(CardMessages.NotFound, body, StringComparison.Ordinal);
        Assert.DoesNotContain("gift_card.not_found", body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            factory.Backend.Requests,
            request => request.Path.EndsWith(HistoryPath, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Active", "Suspend", "suspend")]
    [InlineData("Suspended", "Reactivate", "reactivate")]
    public async Task LifecycleActionsAreAntiforgeryProtectedIdempotentBackendPosts(
        string state,
        string handler,
        string backendAction)
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        EnqueueDetailAndHistory(state);
        using var page = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}", UriKind.Relative));
        var html = await page.Content.ReadAsStringAsync();
        var antiforgery = CardholderAppFactory.ExtractAntiforgeryToken(html);
        var idempotencyKey = ExtractIdempotencyKey(html);
        factory.Backend.Enqueue(
            LifecyclePath + backendAction,
            HttpStatusCode.OK,
            LifecycleResultJson(state));

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["IdempotencyKey"] = idempotencyKey,
                ["__RequestVerificationToken"] = antiforgery,
            });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/cards/{GiftCardId}?handler={handler}", UriKind.Relative))
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "browser-forged");

        using var response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/cards/{GiftCardId}", response.Headers.Location?.OriginalString);
        var lifecycle = factory.Backend.Requests.Single(recorded =>
            recorded.Path.EndsWith(LifecyclePath + backendAction, StringComparison.Ordinal));
        Assert.Contains(
            $"\"idempotencyKey\":\"{idempotencyKey}\"",
            lifecycle.Body,
            StringComparison.Ordinal);
        Assert.Equal($"Bearer {AccessTokenValue}", lifecycle.Header("Authorization"));
        Assert.DoesNotContain("browser-forged", lifecycle.Header("Authorization")!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LifecyclePostWithoutAntiforgeryNeverReachesTheBackend()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        var callsBefore = factory.Backend.Requests.Count;
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["IdempotencyKey"] = "cardholder-lifecycle-0123456789abcdef0123456789abcdef",
            });

        using var response = await browser.PostAsync(
            new Uri($"/cards/{GiftCardId}?handler=Suspend", UriKind.Relative),
            content);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/session-expired", response.Headers.Location?.OriginalString);
        Assert.Equal(callsBefore, factory.Backend.Requests.Count);
    }

    [Fact]
    public async Task LifecycleConflictReloadsTheBackendStateWithSafeCopy()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        EnqueueDetailAndHistory("Active");
        using var page = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}", UriKind.Relative));
        var html = await page.Content.ReadAsStringAsync();
        factory.Backend.EnqueueProblem(
            LifecyclePath + "suspend",
            HttpStatusCode.Conflict,
            "gift_card.lifecycle.invalid_transition");

        using var post = await PostLifecycleAsync(browser, "Suspend", html);
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        EnqueueDetailAndHistory("Suspended");
        using var refreshed = await browser.GetAsync(post.Headers.Location);
        var refreshedBody = WebUtility.HtmlDecode(
            await refreshed.Content.ReadAsStringAsync());

        Assert.Contains(CardMessages.ActionUnavailable, refreshedBody, StringComparison.Ordinal);
        Assert.Contains("Reactivate card", refreshedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("gift_card.lifecycle.invalid_transition", refreshedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackendUnauthorizedDetailFailsClosedToSignIn()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        factory.Backend.EnqueueProblem(DetailPath, HttpStatusCode.Unauthorized, "session.invalid");

        using var response = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signin", response.Headers.Location?.OriginalString);
        Assert.Equal(0, factory.Store.SessionCount);
    }

    [Fact]
    public async Task EmptyHistoryKeepsTheDetailUsable()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        factory.Backend.Enqueue(DetailPath, HttpStatusCode.OK, DetailJson("Active"));
        factory.Backend.Enqueue(HistoryPath, HttpStatusCode.OK, EmptyHistoryJson);

        using var response = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(PublicReference, body, StringComparison.Ordinal);
        Assert.Contains("There is no activity to show yet", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetailServiceFailureShowsOnlySafeRecipientCopy()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        factory.Backend.EnqueueProblem(
            DetailPath,
            HttpStatusCode.ServiceUnavailable,
            "database.internal_failure");

        using var response = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}", UriKind.Relative));
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ActivationMessages.TemporarilyUnavailable, body, StringComparison.Ordinal);
        Assert.DoesNotContain("database.internal_failure", body, StringComparison.Ordinal);
        Assert.DoesNotContain(PublicReference, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryServiceFailureKeepsDetailUsableWithSafeCopy()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        factory.Backend.Enqueue(DetailPath, HttpStatusCode.OK, DetailJson("Active"));
        factory.Backend.EnqueueProblem(
            HistoryPath,
            HttpStatusCode.ServiceUnavailable,
            "ledger.internal_failure");

        using var response = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}", UriKind.Relative));
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(PublicReference, body, StringComparison.Ordinal);
        Assert.Contains(CardMessages.HistoryUnavailable, body, StringComparison.Ordinal);
        Assert.DoesNotContain("ledger.internal_failure", body, StringComparison.Ordinal);
    }

    private async Task SignInAsync(HttpClient browser)
    {
        factory.Backend.Enqueue("auth/login", HttpStatusCode.OK, TokenPairJson);
        factory.Backend.Enqueue("me", HttpStatusCode.OK, CurrentUserJson);
        using var page = await browser.GetAsync(new Uri("/signin", UriKind.Relative));
        var token = CardholderAppFactory.ExtractAntiforgeryToken(
            await page.Content.ReadAsStringAsync());
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Identifier"] = "recipient@example.com",
                ["Password"] = "a long enough passphrase",
                ["__RequestVerificationToken"] = token,
            });
        using var response = await browser.PostAsync(new Uri("/signin", UriKind.Relative), content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private void EnqueueDetailAndHistory(string state, string? nextCursor = null)
    {
        factory.Backend.Enqueue(DetailPath, HttpStatusCode.OK, DetailJson(state));
        factory.Backend.Enqueue(HistoryPath, HttpStatusCode.OK, HistoryJson(nextCursor));
    }

    private static async Task<HttpResponseMessage> PostLifecycleAsync(
        HttpClient browser,
        string handler,
        string html)
    {
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["IdempotencyKey"] = ExtractIdempotencyKey(html),
                ["__RequestVerificationToken"] =
                    CardholderAppFactory.ExtractAntiforgeryToken(html),
            });
        return await browser.PostAsync(
            new Uri($"/cards/{GiftCardId}?handler={handler}", UriKind.Relative),
            content);
    }

    private static string ExtractIdempotencyKey(string html)
    {
        var match = IdempotencyKeyPattern().Match(html);
        Assert.True(match.Success, "The lifecycle form did not render an idempotency key.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string Iso(TimeSpan fromNow) =>
        DateTimeOffset.UtcNow.Add(fromNow).ToString("O", CultureInfo.InvariantCulture);

    private static string DetailJson(string state) => $$"""
        {
          "id": "{{GiftCardId}}",
          "publicReference": "{{PublicReference}}",
          "fundingOrganizationId": "{{FundingOrganizationId}}",
          "issuingOrganizationId": "{{IssuingOrganizationId}}",
          "ownershipState": "IdentityOwned",
          "lifecycleState": "{{state}}",
          "fundedAmount": 100.00,
          "balance": 73.25,
          "reservedBalance": 0.00,
          "availableBalance": 73.25,
          "currency": "TRY",
          "validFromUtc": "2026-07-01T00:00:00Z",
          "expiresAtUtc": "2027-07-01T00:00:00Z",
          "isTransferable": false,
          "isDivisible": false,
          "rootGiftCardId": "{{GiftCardId}}",
          "generation": 0,
          "distributionInvitationId": "0195c0de-0000-7000-8000-000000000041",
          "distributedAtUtc": "2026-07-02T10:00:00Z",
          "claimedAtUtc": "2026-07-02T10:05:00Z",
          "issuedAtUtc": "2026-07-01T09:00:00Z"
        }
        """;

    private static string HistoryJson(string? nextCursor) => $$"""
        {
          "items": [
            {
              "eventKey": "lifecycle:2",
              "category": "Lifecycle",
              "operation": "Suspend",
              "entityId": "0195c0de-0000-7000-8000-000000000052",
              "giftCardId": "{{GiftCardId}}",
              "giftCardPublicReference": "{{PublicReference}}",
              "businessReference": "Cardholder self-service suspension.",
              "amount": null,
              "currency": "TRY",
              "financialDirection": "None",
              "state": "Suspended",
              "actorUserId": "0195c0de-0000-7000-8000-000000000009",
              "occurredAtUtc": "2026-07-03T11:00:00Z"
            },
            {
              "eventKey": "ledger:1",
              "category": "Ledger",
              "operation": "gift_card.issuance",
              "entityId": "0195c0de-0000-7000-8000-000000000051",
              "giftCardId": "{{GiftCardId}}",
              "giftCardPublicReference": "{{PublicReference}}",
              "businessReference": "AWARD-0021",
              "amount": 100.00,
              "currency": "TRY",
              "financialDirection": "Credit",
              "state": "Active",
              "actorUserId": "0195c0de-0000-7000-8000-000000000008",
              "occurredAtUtc": "2026-07-01T09:00:00Z"
            }
          ],
          "limit": 10,
          "nextCursor": {{(nextCursor is null ? "null" : $"\"{nextCursor}\"")}}
        }
        """;

    private static string LifecycleResultJson(string previousState) => $$"""
        {
          "event": {
            "id": "0195c0de-0000-7000-8000-000000000061",
            "giftCardId": "{{GiftCardId}}",
            "action": "Suspend",
            "previousState": "{{previousState}}",
            "newState": "Suspended",
            "occurredAtUtc": "2026-07-04T12:00:00Z"
          }
        }
        """;

    private const string ListJson = $$"""
        {
          "items": [{
            "id": "{{GiftCardId}}",
            "publicReference": "{{PublicReference}}",
            "lifecycleState": "Active",
            "fundedAmount": 100.00,
            "balance": 73.25,
            "reservedBalance": 0.00,
            "availableBalance": 73.25,
            "currency": "TRY",
            "validFromUtc": "2026-07-01T00:00:00Z",
            "expiresAtUtc": "2027-07-01T00:00:00Z",
            "claimedAtUtc": "2026-07-02T10:05:00Z",
            "issuedAtUtc": "2026-07-01T09:00:00Z"
          }],
          "limit": 20,
          "nextCursor": null
        }
        """;

    private const string EmptyHistoryJson = """
        {"items": [], "limit": 10, "nextCursor": null}
        """;

    [GeneratedRegex(
        @"name=""IdempotencyKey""[^>]*value=""([^""]+)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex IdempotencyKeyPattern();
}
