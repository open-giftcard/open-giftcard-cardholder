using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GiftCardCardholder.Web.Backend;

namespace GiftCardCardholder.Tests;

public sealed class SharingJourneyTests : IDisposable
{
    private const string GiftCardId = "0195c0de-0000-7000-8000-000000000101";
    private const string ShareId = "0195c0de-0000-7000-8000-000000000102";
    private const string SenderId = "0195c0de-0000-7000-8000-000000000103";
    private const string RecipientId = "0195c0de-0000-7000-8000-000000000104";
    private const string ChildCardId = "0195c0de-0000-7000-8000-000000000105";
    private const string AccessToken = "sharing-access-token";
    private const string RefreshToken = "sharing-refresh-token";

    private static string TokenPairJson => $$"""
        {
          "accessToken":"{{AccessToken}}",
          "accessTokenExpiresAtUtc":"{{Iso(TimeSpan.FromMinutes(15))}}",
          "refreshToken":"{{RefreshToken}}",
          "refreshTokenExpiresAtUtc":"{{Iso(TimeSpan.FromDays(30))}}"
        }
        """;

    private const string CurrentUserJson = $$"""
        {"id":"{{RecipientId}}","email":"recipient@example.com","phoneNumber":null,
         "status":"Active","contextType":"Identity"}
        """;

    private const string DetailJson = $$"""
        {
          "id":"{{GiftCardId}}","publicReference":"GC-SOURCE-0101",
          "fundingOrganizationId":"0195c0de-0000-7000-8000-000000000110",
          "issuingOrganizationId":"0195c0de-0000-7000-8000-000000000111",
          "ownershipState":"IdentityOwned","lifecycleState":"Active",
          "fundedAmount":100.00,"balance":100.00,"reservedBalance":25.00,
          "availableBalance":75.00,"currency":"TRY",
          "validFromUtc":"2026-08-01T00:00:00Z","expiresAtUtc":"2027-08-01T00:00:00Z",
          "isTransferable":true,"isDivisible":true,
          "rootGiftCardId":"{{GiftCardId}}","generation":0,
          "distributionInvitationId":null,"distributedAtUtc":null,
          "claimedAtUtc":"2026-08-01T00:00:00Z","issuedAtUtc":"2026-08-01T00:00:00Z"
        }
        """;

    private const string ShareJson = $$"""
        {
          "id":"{{ShareId}}","kind":"ProtectedLink","sourceGiftCardId":"{{GiftCardId}}",
          "fundingOrganizationId":"0195c0de-0000-7000-8000-000000000110",
          "senderUserId":"{{SenderId}}","claimedByUserId":null,"childGiftCardId":null,
          "sourceGiftCardPublicReference":"GC-SOURCE-0101","childGiftCardPublicReference":null,
          "ledgerTransactionId":null,"amount":25.00,"currency":"TRY","state":"Pending",
          "failedPinAttempts":0,"recipientContactType":null,"maskedRecipientContact":null,
          "identityWasCreatedOnClaim":null,"expiresAtUtc":"2026-08-03T00:00:00Z",
          "createdAtUtc":"2026-08-02T00:00:00Z","claimedAtUtc":null,"closedAtUtc":null
        }
        """;

    private static string CreatedProtectedJson(string claimToken) => $$"""
        {"share":{{ShareJson}},"claimUrl":"http://localhost:5180/share/claim?token={{claimToken}}",
         "pin":"123456"}
        """;

    private const string SharePageJson = $$"""
        {"items":[{{ShareJson}}],"limit":20,"nextCursor":"opaque+share/=="}
        """;

    private const string CreatedDirectJson = $$"""
        {
          "share":{
            "id":"{{ShareId}}","kind":"DirectInvitation","sourceGiftCardId":"{{GiftCardId}}",
            "senderUserId":"{{SenderId}}","claimedByUserId":null,"childGiftCardId":null,
            "sourceGiftCardPublicReference":"GC-SOURCE-0101","childGiftCardPublicReference":null,
            "amount":20.00,"currency":"TRY","state":"Pending","failedPinAttempts":0,
            "recipientContactType":"Email","maskedRecipientContact":"p***@example.com",
            "identityWasCreatedOnClaim":null,"expiresAtUtc":"2026-08-03T00:00:00Z",
            "createdAtUtc":"2026-08-02T00:00:00Z"
          },
          "maskedRecipientContact":"p***@example.com","deliveryDispatchedThisRequest":true
        }
        """;

    private const string ClaimedProtectedJson = $$"""
        {
          "share":{
            "id":"{{ShareId}}","kind":"ProtectedLink","sourceGiftCardId":"{{GiftCardId}}",
            "senderUserId":"{{SenderId}}","claimedByUserId":"{{RecipientId}}",
            "childGiftCardId":"{{ChildCardId}}","sourceGiftCardPublicReference":null,
            "childGiftCardPublicReference":"GC-CHILD-0105","amount":25.00,"currency":"TRY",
            "state":"Claimed","failedPinAttempts":0,"expiresAtUtc":"2026-08-03T00:00:00Z",
            "createdAtUtc":"2026-08-02T00:00:00Z","claimedAtUtc":"2026-08-02T01:00:00Z"
          },
          "childGiftCard":{"id":"{{ChildCardId}}","publicReference":"GC-CHILD-0105",
            "lifecycleState":"Active","fundedAmount":25.00,"currency":"TRY",
            "validFromUtc":"2026-08-02T01:00:00Z","expiresAtUtc":"2027-08-01T00:00:00Z"}
        }
        """;

    private static string DirectClaimWithSessionJson => $$"""
        {
          "share":{
            "id":"{{ShareId}}","kind":"DirectInvitation","sourceGiftCardId":"{{GiftCardId}}",
            "senderUserId":"{{SenderId}}","claimedByUserId":"{{RecipientId}}",
            "childGiftCardId":"{{ChildCardId}}","amount":20.00,"currency":"TRY",
            "state":"Claimed","failedPinAttempts":0,"recipientContactType":"Email",
            "maskedRecipientContact":"r***@example.com","identityWasCreatedOnClaim":true,
            "expiresAtUtc":"2026-08-03T00:00:00Z","createdAtUtc":"2026-08-02T00:00:00Z"
          },
          "ownerUserId":"{{RecipientId}}","identityWasCreated":true,
          "maskedLoginIdentifier":"r***@example.com","session":{{TokenPairJson}},
          "childGiftCard":{"id":"{{ChildCardId}}","publicReference":"GC-CHILD-0105",
            "lifecycleState":"Active","fundedAmount":20.00,"currency":"TRY",
            "validFromUtc":"2026-08-02T01:00:00Z","expiresAtUtc":"2027-08-01T00:00:00Z"}
        }
        """;

    private const string DirectClaimExistingJson = $$"""
        {
          "share":{
            "id":"{{ShareId}}","kind":"DirectInvitation","sourceGiftCardId":"{{GiftCardId}}",
            "senderUserId":"{{SenderId}}","claimedByUserId":"{{RecipientId}}",
            "childGiftCardId":"{{ChildCardId}}","amount":20.00,"currency":"TRY",
            "state":"Claimed","failedPinAttempts":0,"recipientContactType":"Email",
            "maskedRecipientContact":"r***@example.com","identityWasCreatedOnClaim":false,
            "expiresAtUtc":"2026-08-03T00:00:00Z","createdAtUtc":"2026-08-02T00:00:00Z"
          },
          "ownerUserId":"{{RecipientId}}","identityWasCreated":false,
          "maskedLoginIdentifier":"r***@example.com","session":null,
          "childGiftCard":{"id":"{{ChildCardId}}","publicReference":"GC-CHILD-0105",
            "lifecycleState":"Active","fundedAmount":20.00,"currency":"TRY",
            "validFromUtc":"2026-08-02T01:00:00Z","expiresAtUtc":"2027-08-01T00:00:00Z"}
        }
        """;

    private readonly CardholderAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task ProtectedLinkGetMovesSecretServerSideAndNeverClaims()
    {
        using var browser = factory.CreateBrowser();
        var token = CreateClaimToken();

        using var intake = await browser.GetAsync($"/share/claim?token={token}");
        Assert.Equal(HttpStatusCode.Redirect, intake.StatusCode);
        Assert.Equal("/share/claim/confirm", intake.Headers.Location?.OriginalString);
        Assert.Equal(1, factory.Store.ActivationCount);
        Assert.DoesNotContain(token, await intake.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var confirmation = await browser.GetAsync("/share/claim/confirm");
        var html = await confirmation.Content.ReadAsStringAsync();
        Assert.Contains("Sign in first", html, StringComparison.Ordinal);
        Assert.Empty(factory.Backend.Requests);
    }

    [Fact]
    public async Task ProtectedClaimResumesAfterSignInAndSendsPinOnlyOnPost()
    {
        using var browser = factory.CreateBrowser();
        var claimToken = CreateClaimToken();
        await browser.GetAsync($"/share/claim?token={claimToken}");
        await SignInAsync(browser, "/share/claim/confirm");
        factory.Backend.Enqueue("share-claims", HttpStatusCode.OK, ClaimedProtectedJson);

        using var claim = await PostFormAsync(
            browser,
            "/share/claim/confirm",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Pin"] = "123456" });

        Assert.Equal(HttpStatusCode.Redirect, claim.StatusCode);
        Assert.Equal("/shares?Direction=Received&State=Claimed", claim.Headers.Location?.OriginalString);
        Assert.Equal(0, factory.Store.ActivationCount);
        var backendClaim = factory.Backend.Requests.Last(request =>
            request.Path.EndsWith("share-claims", StringComparison.Ordinal));
        Assert.Contains(claimToken, backendClaim.Body, StringComparison.Ordinal);
        Assert.Contains("123456", backendClaim.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(claimToken, await claim.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProtectedCreationShowsOneTimeCredentialsAndBackendValueComponents()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        var claimToken = CreateClaimToken();
        factory.Backend.Enqueue($"gift-cards/{GiftCardId}", HttpStatusCode.OK, DetailJson);
        factory.Backend.Enqueue($"gift-cards/{GiftCardId}/shares", HttpStatusCode.Created, CreatedProtectedJson(claimToken));
        factory.Backend.Enqueue($"gift-cards/{GiftCardId}", HttpStatusCode.OK, DetailJson);

        using var result = await PostFormAsync(
            browser,
            $"/cards/{GiftCardId}/share?handler=Protected",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ProtectedAmount"] = "25.00",
                ["ProtectedIdempotencyKey"] = $"cardholder-share-link-{Guid.NewGuid():N}",
            },
            getPath: $"/cards/{GiftCardId}/share");
        var html = WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains(claimToken, html, StringComparison.Ordinal);
        Assert.Contains("123456", html, StringComparison.Ordinal);
        Assert.Contains("100.00 TRY", html, StringComparison.Ordinal);
        Assert.Contains("25.00 TRY", html, StringComparison.Ordinal);
        Assert.Contains("75.00 TRY", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "<h1[ >]", RegexOptions.IgnoreCase));
        Assert.Contains("for=\"ProtectedAmount\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"DirectAmount\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"RecipientContact\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessToken, html, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshToken, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachShareOptionLeadsWithWhatHappensToTheRecipient()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        factory.Backend.Enqueue($"gift-cards/{GiftCardId}", HttpStatusCode.OK, DetailJson);

        using var page = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}/share", UriKind.Relative));
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());

        // The sender is choosing between two outcomes for the other person, so
        // each option states the outcome first. Naming only the precondition
        // led senders to pick the link for someone with no account and read the
        // resulting sign-in requirement as a fault.
        Assert.Contains("They will need to sign in.", html, StringComparison.Ordinal);
        Assert.Contains(
            "They can set up an account from the message.",
            html,
            StringComparison.Ordinal);

        var signIn = html.IndexOf("They will need to sign in.", StringComparison.Ordinal);
        var setUp = html.IndexOf(
            "They can set up an account from the message.",
            StringComparison.Ordinal);
        Assert.True(
            signIn < html.IndexOf("Create protected link", StringComparison.Ordinal),
            "The consequence must be read before the button that commits to it.");
        Assert.True(
            setUp < html.IndexOf("Send invitation", StringComparison.Ordinal),
            "The consequence must be read before the button that commits to it.");

        // The security boundary is deliberate and the copy must not imply the
        // link can create an account for someone.
        Assert.DoesNotContain(
            "For someone who already has a",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProtectedCreationRetainsIdempotencyKeyAfterBackendFailure()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        var idempotencyKey = $"cardholder-share-link-{Guid.NewGuid():N}";
        factory.Backend.Enqueue($"gift-cards/{GiftCardId}", HttpStatusCode.OK, DetailJson);
        factory.Backend.EnqueueProblem(
            $"gift-cards/{GiftCardId}/shares",
            HttpStatusCode.ServiceUnavailable,
            "service.unavailable");
        factory.Backend.Enqueue($"gift-cards/{GiftCardId}", HttpStatusCode.OK, DetailJson);

        using var result = await PostFormAsync(
            browser,
            $"/cards/{GiftCardId}/share?handler=Protected",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ProtectedAmount"] = "25.00",
                ["ProtectedIdempotencyKey"] = idempotencyKey,
            },
            getPath: $"/cards/{GiftCardId}/share");
        var html = WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains($"value=\"{idempotencyKey}\"", html, StringComparison.Ordinal);
        var request = factory.Backend.Requests.Last(item =>
            item.Path.EndsWith("/shares", StringComparison.Ordinal));
        Assert.Contains(idempotencyKey, request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongPinPreservesSignedInSessionAndActivationForABoundedRetry()
    {
        using var browser = factory.CreateBrowser();
        await browser.GetAsync($"/share/claim?token={CreateClaimToken()}");
        await SignInAsync(browser, "/share/claim/confirm");
        factory.Backend.EnqueueProblem(
            "share-claims",
            HttpStatusCode.Unauthorized,
            BackendProblemException.Codes.ShareClaimInvalid);

        using var result = await PostFormAsync(
            browser,
            "/share/claim/confirm",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Pin"] = "000000" });
        var html = await result.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("not valid", html, StringComparison.Ordinal);
        Assert.Equal(1, factory.Store.SessionCount);
        Assert.Equal(1, factory.Store.ActivationCount);
    }

    [Fact]
    public async Task ProtectedClaimPinPostRequiresAnAntiforgeryToken()
    {
        using var browser = factory.CreateBrowser();
        await browser.GetAsync($"/share/claim?token={CreateClaimToken()}");
        await SignInAsync(browser, "/share/claim/confirm");
        factory.Backend.Enqueue("share-claims", HttpStatusCode.OK, ClaimedProtectedJson);
        var callsBeforePost = factory.Backend.Requests.Count;

        // Everything about this request is valid except the antiforgery token,
        // so a rejection can only be attributed to CSRF protection. Without it,
        // another origin could spend a share the recipient never chose to claim.
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Pin"] = "123456" });
        using var result = await browser.PostAsync("/share/claim/confirm", content);

        Assert.Equal(HttpStatusCode.SeeOther, result.StatusCode);
        Assert.Equal("/session-expired", result.Headers.Location?.OriginalString);
        Assert.Equal(callsBeforePost, factory.Backend.Requests.Count);
        Assert.Equal(1, factory.Store.ActivationCount);
        Assert.Equal(1, factory.Store.SessionCount);
    }

    [Fact]
    public async Task WrongPinsAndALockedShareAreIndistinguishableToTheRecipient()
    {
        using var browser = factory.CreateBrowser();
        await browser.GetAsync($"/share/claim?token={CreateClaimToken()}");
        await SignInAsync(browser, "/share/claim/confirm");

        // Two refused attempts, then whatever the backend reports once its
        // attempt bound locks the share. The client maps every claim failure to
        // one message on purpose: distinguishing "wrong PIN" from "locked"
        // would tell an attacker whether the PIN space is still worth probing.
        factory.Backend.EnqueueProblem(
            "share-claims",
            HttpStatusCode.Unauthorized,
            BackendProblemException.Codes.ShareClaimInvalid);
        factory.Backend.EnqueueProblem(
            "share-claims",
            HttpStatusCode.Unauthorized,
            BackendProblemException.Codes.ShareClaimInvalid);
        factory.Backend.EnqueueProblem("share-claims", HttpStatusCode.Conflict, "sharing.claim.locked");

        var rendered = new List<string>();
        foreach (var pin in new[] { "000000", "111111", "222222" })
        {
            using var attempt = await PostFormAsync(
                browser,
                "/share/claim/confirm",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Pin"] = pin });
            Assert.Equal(HttpStatusCode.OK, attempt.StatusCode);
            rendered.Add(await attempt.Content.ReadAsStringAsync());
        }

        // The safe message deliberately enumerates every possibility at once —
        // "not valid, has already been used, is locked, or has expired" — so
        // that it cannot say which one applies. All three refusals, including
        // the unrecognized lock code, must produce that identical sentence.
        Assert.All(rendered, html => Assert.Contains(
            "is not valid, has already been used, is locked, or has expired",
            html,
            StringComparison.Ordinal));

        // An unrecognized backend code must fall through to that copy rather
        // than surfacing a raw detail the recipient could read state from.
        Assert.All(rendered, html =>
            Assert.DoesNotContain("sharing.claim", html, StringComparison.Ordinal));

        // A refused claim is not an authentication failure: the recipient stays
        // signed in and the activation context survives for a further attempt.
        Assert.Equal(1, factory.Store.SessionCount);
        Assert.Equal(1, factory.Store.ActivationCount);
    }

    [Fact]
    public async Task ARefusedPinIsNeverEchoedBackIntoThePage()
    {
        using var browser = factory.CreateBrowser();
        await browser.GetAsync($"/share/claim?token={CreateClaimToken()}");
        await SignInAsync(browser, "/share/claim/confirm");
        factory.Backend.EnqueueProblem(
            "share-claims",
            HttpStatusCode.Unauthorized,
            BackendProblemException.Codes.ShareClaimInvalid);

        using var result = await PostFormAsync(
            browser,
            "/share/claim/confirm",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Pin"] = "246813" });
        var html = await result.Content.ReadAsStringAsync();

        // The PIN is delivered out of band and is a shared secret. Re-rendering
        // it would put it in the page source, browser history, and any proxy
        // that logs bodies.
        Assert.DoesNotContain("246813", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedSharePasswordIsNeverEchoedBackIntoThePage()
    {
        // The PIN field had exactly this defect, and the direct-invitation
        // password form binds the same way, so the sibling case is pinned here
        // rather than assumed from tag-helper behaviour.
        using var browser = factory.CreateBrowser();
        await browser.GetAsync($"/activate/share?token={CreateClaimToken()}");
        factory.Backend.EnqueueProblem(
            "share-invitation-claims",
            HttpStatusCode.BadRequest,
            BackendProblemException.Codes.PasswordRequired);
        await PostFormAsync(browser, "/activate/share/confirm", []);

        const string chosen = "a long enough passphrase";
        using var mismatch = await PostFormAsync(
            browser,
            "/activate/share/password",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Password"] = chosen,
                ["ConfirmPassword"] = "a different long passphrase",
            });
        var html = await mismatch.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, mismatch.StatusCode);
        Assert.DoesNotContain(chosen, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShareHistoryForwardsFiltersAndOpaqueCursorAndCanCancelReturnedShare()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        factory.Backend.Enqueue("me/shares", HttpStatusCode.OK, SharePageJson);

        using var page = await browser.GetAsync(
            "/shares?Direction=Sent&Kind=ProtectedLink&State=Pending");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("GC-SOURCE-0101", html, StringComparison.Ordinal);
        Assert.Contains("Cursor=opaque%2Bshare%2F%3D%3D", html, StringComparison.Ordinal);
        var list = factory.Backend.Requests.Last(request => request.Path.EndsWith("me/shares", StringComparison.Ordinal));
        Assert.Contains("kind=ProtectedLink", list.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("state=Pending", list.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("direction=Sent", list.PathAndQuery, StringComparison.Ordinal);

        var antiforgery = CardholderAppFactory.ExtractAntiforgeryToken(html);
        factory.Backend.Enqueue($"shares/{ShareId}/cancel", HttpStatusCode.OK, ShareJson);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ShareId"] = ShareId,
            ["IdempotencyKey"] = $"cardholder-share-cancel-{Guid.NewGuid():N}",
            ["Direction"] = "Sent",
            ["Kind"] = "ProtectedLink",
            ["State"] = "Pending",
            ["__RequestVerificationToken"] = antiforgery,
        });
        using var cancelled = await browser.PostAsync("/shares?handler=Cancel", content);
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        Assert.Equal("/shares?Direction=Sent&Kind=ProtectedLink&State=Pending", cancelled.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task DirectCreationSendsContactOnlyServerToServerAndRendersMaskedValue()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        factory.Backend.Enqueue($"gift-cards/{GiftCardId}", HttpStatusCode.OK, DetailJson);
        factory.Backend.Enqueue(
            $"gift-cards/{GiftCardId}/share-invitations",
            HttpStatusCode.Created,
            CreatedDirectJson);
        factory.Backend.Enqueue($"gift-cards/{GiftCardId}", HttpStatusCode.OK, DetailJson);

        using var result = await PostFormAsync(
            browser,
            $"/cards/{GiftCardId}/share?handler=Direct",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DirectAmount"] = "20.00",
                ["RecipientContactType"] = "Email",
                ["RecipientContact"] = "private.recipient@example.com",
                ["DirectIdempotencyKey"] = $"cardholder-share-direct-{Guid.NewGuid():N}",
            },
            getPath: $"/cards/{GiftCardId}/share");
        var html = await result.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("p***@example.com", html, StringComparison.Ordinal);
        Assert.DoesNotContain("private.recipient@example.com", html, StringComparison.Ordinal);
        var request = factory.Backend.Requests.Last(item =>
            item.Path.EndsWith("share-invitations", StringComparison.Ordinal));
        Assert.Contains("private.recipient@example.com", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"contactType\":\"Email\"", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectCreationSupportsPhoneWithoutRenderingTheRawNumber()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        const string rawPhone = "+905551112233";
        const string maskedPhone = "+90 *** ** 2233";
        var phoneResponse = CreatedDirectJson
            .Replace("\"recipientContactType\":\"Email\"", "\"recipientContactType\":\"Phone\"", StringComparison.Ordinal)
            .Replace("p***@example.com", maskedPhone, StringComparison.Ordinal);
        factory.Backend.Enqueue($"gift-cards/{GiftCardId}", HttpStatusCode.OK, DetailJson);
        factory.Backend.Enqueue(
            $"gift-cards/{GiftCardId}/share-invitations",
            HttpStatusCode.Created,
            phoneResponse);
        factory.Backend.Enqueue($"gift-cards/{GiftCardId}", HttpStatusCode.OK, DetailJson);

        using var result = await PostFormAsync(
            browser,
            $"/cards/{GiftCardId}/share?handler=Direct",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DirectAmount"] = "20.00",
                ["RecipientContactType"] = "Phone",
                ["RecipientContact"] = rawPhone,
                ["DirectIdempotencyKey"] = $"cardholder-share-direct-{Guid.NewGuid():N}",
            },
            getPath: $"/cards/{GiftCardId}/share");
        var html = await result.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains(maskedPhone, WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.DoesNotContain(rawPhone, html, StringComparison.Ordinal);
        var request = factory.Backend.Requests.Last(item =>
            item.Path.EndsWith("share-invitations", StringComparison.Ordinal));
        Assert.NotNull(request.Body);
        using var requestBody = JsonDocument.Parse(request.Body);
        Assert.Equal(rawPhone, requestBody.RootElement.GetProperty("recipientContact").GetString());
        Assert.Equal("Phone", requestBody.RootElement.GetProperty("contactType").GetString());
    }

    [Fact]
    public async Task DirectInvitationProbeCreatesPasswordThenConsumesSessionServerSide()
    {
        using var browser = factory.CreateBrowser();
        var claimToken = CreateClaimToken();
        await browser.GetAsync($"/activate/share?token={claimToken}");
        factory.Backend.EnqueueProblem(
            "share-invitation-claims",
            HttpStatusCode.BadRequest,
            BackendProblemException.Codes.PasswordRequired);

        using var probe = await PostFormAsync(browser, "/activate/share/confirm", []);
        Assert.Equal("/activate/share/password", probe.Headers.Location?.OriginalString);
        var probeRequest = factory.Backend.Requests.Last(request =>
            request.Path.EndsWith("share-invitation-claims", StringComparison.Ordinal));
        Assert.Contains("\"password\":null", probeRequest.Body, StringComparison.Ordinal);
        Assert.Equal(CardholderAppFactory.ObservedClientAddress, probeRequest.Header("X-Forwarded-For"));

        factory.Backend.Enqueue("share-invitation-claims", HttpStatusCode.OK, DirectClaimWithSessionJson);
        using var completion = await PostFormAsync(
            browser,
            "/activate/share/password",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Password"] = "a long enough passphrase",
                ["ConfirmPassword"] = "a long enough passphrase",
            });
        var cookies = string.Join(' ', completion.Headers.GetValues("Set-Cookie"));
        Assert.Equal("/cards", completion.Headers.Location?.OriginalString);
        Assert.Equal(1, factory.Store.SessionCount);
        Assert.Equal(0, factory.Store.ActivationCount);
        Assert.DoesNotContain(AccessToken, cookies, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshToken, cookies, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingRecipientDirectInvitationClaimsWithoutPasswordButDoesNotAuthenticate()
    {
        using var browser = factory.CreateBrowser();
        await browser.GetAsync($"/activate/share?token={CreateClaimToken()}");
        factory.Backend.Enqueue(
            "share-invitation-claims",
            HttpStatusCode.OK,
            DirectClaimExistingJson);

        using var result = await PostFormAsync(browser, "/activate/share/confirm", []);
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal("/signin", result.Headers.Location?.OriginalString);
        Assert.Equal(0, factory.Store.SessionCount);
        Assert.Equal(0, factory.Store.ActivationCount);

        using var signIn = await browser.GetAsync("/signin");
        var html = await signIn.Content.ReadAsStringAsync();
        Assert.Contains("r***@example.com", html, StringComparison.Ordinal);
        var claim = factory.Backend.Requests.Last(request =>
            request.Path.EndsWith("share-invitation-claims", StringComparison.Ordinal));
        Assert.Contains("\"password\":null", claim.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayedProtectedClaimFailsSafelyWithoutEndingTheSession()
    {
        using var browser = factory.CreateBrowser();
        await browser.GetAsync($"/share/claim?token={CreateClaimToken()}");
        await SignInAsync(browser, "/share/claim/confirm");
        factory.Backend.EnqueueProblem(
            "share-claims",
            HttpStatusCode.Conflict,
            BackendProblemException.Codes.ShareClaimAlreadyCompleted);

        using var result = await PostFormAsync(
            browser,
            "/share/claim/confirm",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Pin"] = "123456" });
        var html = await result.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("already been used", html, StringComparison.Ordinal);
        Assert.Equal(1, factory.Store.SessionCount);
    }

    [Fact]
    public async Task ActivationPurposeCannotCrossIntoAnotherClaimEndpoint()
    {
        using var browser = factory.CreateBrowser();
        await browser.GetAsync($"/activate?token={CreateClaimToken()}");
        Assert.Equal(1, factory.Store.ActivationCount);

        using var wrongRoute = await browser.GetAsync("/activate/share/confirm");
        var html = await wrongRoute.Content.ReadAsStringAsync();
        Assert.Contains("not valid", html, StringComparison.Ordinal);
        Assert.Equal(0, factory.Store.ActivationCount);
        Assert.Empty(factory.Backend.Requests);
    }

    private static string CreateClaimToken() =>
        $"{Guid.NewGuid():N}.{Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32))}";

    private static string Iso(TimeSpan fromNow) =>
        DateTimeOffset.UtcNow.Add(fromNow).ToString("O", CultureInfo.InvariantCulture);

    private async Task SignInAsync(HttpClient browser, string? returnUrl = null)
    {
        factory.Backend.Enqueue("auth/login", HttpStatusCode.OK, TokenPairJson);
        factory.Backend.Enqueue("me", HttpStatusCode.OK, CurrentUserJson);
        var path = returnUrl is null ? "/signin" : $"/signin?ReturnUrl={Uri.EscapeDataString(returnUrl)}";
        using var signedIn = await PostFormAsync(
            browser,
            path,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Identifier"] = "recipient@example.com",
                ["Password"] = "a long enough passphrase",
                ["ReturnUrl"] = returnUrl ?? string.Empty,
            });
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient browser,
        string postPath,
        Dictionary<string, string> fields,
        string? getPath = null)
    {
        using var page = await browser.GetAsync(getPath ?? postPath);
        var antiforgery = CardholderAppFactory.ExtractAntiforgeryToken(
            await page.Content.ReadAsStringAsync());
        var form = new Dictionary<string, string>(fields, StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = antiforgery,
        };
        using var content = new FormUrlEncodedContent(form);
        return await browser.PostAsync(postPath, content);
    }
}
