using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using GiftCardCardholder.Web.Backend;

namespace GiftCardCardholder.Tests;

/// <summary>
/// Covers the activation journey end to end against a scripted backend.
/// </summary>
public sealed class ActivationJourneyTests : IDisposable
{
    private const string ClaimPath = "gift-card-claims";

    private const string AccessTokenValue = "header.payload.signature-claim-access";
    private const string RefreshTokenValue = "opaque-refresh-token-from-claim";

    /// <summary>
    /// An existing account claiming a card: the backend returns no session, so
    /// the recipient is sent to sign in (backend IMPL-019).
    /// </summary>
    private const string ClaimSuccessJson = """
        {
          "invitationId": "0195c0de-0000-7000-8000-000000000001",
          "ownerUserId": "0195c0de-0000-7000-8000-000000000002",
          "identityWasCreated": false,
          "maskedLoginIdentifier": "a***@example.com",
          "session": null,
          "giftCard": {
            "id": "0195c0de-0000-7000-8000-000000000003",
            "publicReference": "GC-ABCD-EFGH",
            "lifecycleState": "Active",
            "fundedAmount": 500.00,
            "currency": "TRY",
            "validFromUtc": "2026-07-01T00:00:00+00:00",
            "expiresAtUtc": "2027-07-01T00:00:00+00:00"
          },
          "claimedAtUtc": "2026-07-29T10:00:00+00:00"
        }
        """;

    /// <summary>
    /// A claim that created the recipient identity: the backend returns a token
    /// pair, so the recipient goes straight to their card.
    /// </summary>
    private static string ClaimSuccessWithSessionJson => $$"""
        {
          "invitationId": "0195c0de-0000-7000-8000-000000000001",
          "ownerUserId": "0195c0de-0000-7000-8000-000000000002",
          "identityWasCreated": true,
          "maskedLoginIdentifier": "a***@example.com",
          "session": {
            "accessToken": "{{AccessTokenValue}}",
            "accessTokenExpiresAtUtc": "{{Iso(TimeSpan.FromMinutes(15))}}",
            "refreshToken": "{{RefreshTokenValue}}",
            "refreshTokenExpiresAtUtc": "{{Iso(TimeSpan.FromDays(30))}}"
          },
          "giftCard": {
            "id": "0195c0de-0000-7000-8000-000000000003",
            "publicReference": "GC-ABCD-EFGH",
            "lifecycleState": "Active",
            "fundedAmount": 500.00,
            "currency": "TRY",
            "validFromUtc": "2026-07-01T00:00:00+00:00",
            "expiresAtUtc": "2027-07-01T00:00:00+00:00"
          },
          "claimedAtUtc": "2026-07-29T10:00:00+00:00"
        }
        """;

    private static string Iso(TimeSpan fromNow) =>
        DateTimeOffset.UtcNow.Add(fromNow).ToString("O", CultureInfo.InvariantCulture);

    private readonly CardholderAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    private static string CreateClaimToken() =>
        $"{Guid.NewGuid():N}.{Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32))}";

    private bool ClaimWasAttempted() =>
        factory.Backend.Requests.Exists(request =>
            request.Method == "POST" && request.Path.EndsWith(ClaimPath, StringComparison.Ordinal));

    [Fact]
    public async Task MalformedActivationLinkIsRefusedWithoutCallingTheBackend()
    {
        using var browser = factory.CreateBrowser();

        using var response = await browser.GetAsync(
            new Uri("/activate?token=not-a-real-token", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join(' ', values)
            : string.Empty;
        Assert.DoesNotContain("cardholder-activation", setCookies, StringComparison.Ordinal);
        Assert.DoesNotContain("cardholder-session", setCookies, StringComparison.Ordinal);
        Assert.False(ClaimWasAttempted());
    }

    [Fact]
    public async Task OpeningAnActivationLinkMovesTheSecretOutOfTheUrl()
    {
        using var browser = factory.CreateBrowser();
        var token = CreateClaimToken();

        using var response = await browser.GetAsync(
            new Uri($"/activate?token={token}", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/activate/confirm", response.Headers.Location?.OriginalString);
        Assert.Equal(1, factory.Store.ActivationCount);

        // The secret is held server-side; it must not come back to the browser.
        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            token,
            string.Join(' ', response.Headers.GetValues("Set-Cookie")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ViewingTheConfirmationPageNeverClaimsTheCard()
    {
        // Mail and chat clients prefetch links to build previews. If a GET
        // claimed, a preview would burn the recipient's single-use invitation.
        using var browser = factory.CreateBrowser();
        await browser.GetAsync(new Uri($"/activate?token={CreateClaimToken()}", UriKind.Relative));

        using var response = await browser.GetAsync(new Uri("/activate/confirm", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(ClaimWasAttempted());
    }

    [Fact]
    public async Task ANewRecipientIsSentToCreateAPassword()
    {
        factory.Backend.EnqueueProblem(
            ClaimPath,
            HttpStatusCode.BadRequest,
            BackendProblemException.Codes.PasswordRequired);

        using var browser = factory.CreateBrowser();
        await browser.GetAsync(new Uri($"/activate?token={CreateClaimToken()}", UriKind.Relative));

        using var response = await PostFormAsync(browser, "/activate/confirm", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/activate/password", response.Headers.Location?.OriginalString);

        // The probe deliberately carries no password.
        var probe = factory.Backend.Requests.Find(request =>
            request.Path.EndsWith(ClaimPath, StringComparison.Ordinal));
        Assert.NotNull(probe);
        Assert.Contains("\"password\":null", probe!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExistingAccountClaimsWithoutAPasswordAndIsSentToSignIn()
    {
        factory.Backend.Enqueue(ClaimPath, HttpStatusCode.OK, ClaimSuccessJson);

        using var browser = factory.CreateBrowser();
        await browser.GetAsync(new Uri($"/activate?token={CreateClaimToken()}", UriKind.Relative));

        using var response = await PostFormAsync(browser, "/activate/confirm", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signin", response.Headers.Location?.OriginalString);

        // The activation context is consumed once the card is claimed.
        Assert.Equal(0, factory.Store.ActivationCount);
    }

    [Fact]
    public async Task ANewRecipientIsSignedInImmediatelyAfterSettingAPassword()
    {
        factory.Backend.EnqueueProblem(
            ClaimPath,
            HttpStatusCode.BadRequest,
            BackendProblemException.Codes.PasswordRequired);
        factory.Backend.Enqueue(ClaimPath, HttpStatusCode.OK, ClaimSuccessWithSessionJson);

        using var browser = factory.CreateBrowser();
        await browser.GetAsync(new Uri($"/activate?token={CreateClaimToken()}", UriKind.Relative));
        await PostFormAsync(browser, "/activate/confirm", []);

        using var response = await CompletePasswordAsync(browser);
        var setCookies = string.Join(' ', response.Headers.GetValues("Set-Cookie"));

        // The claim returned a token pair, so activation ends on the card
        // itself rather than at a sign-in form.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/cards", response.Headers.Location?.OriginalString);
        Assert.Equal(1, factory.Store.SessionCount);
        Assert.Equal(0, factory.Store.ActivationCount);

        Assert.Contains("cardholder-session=", setCookies, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookies, StringComparison.OrdinalIgnoreCase);

        // The backend credentials must not reach the browser in any form.
        Assert.DoesNotContain(AccessTokenValue, setCookies, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshTokenValue, setCookies, StringComparison.Ordinal);
        Assert.DoesNotContain(
            AccessTokenValue,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewRecipientIsToldWhichContactTheirPasswordBelongsTo()
    {
        factory.Backend.EnqueueProblem(
            ClaimPath,
            HttpStatusCode.BadRequest,
            BackendProblemException.Codes.PasswordRequired);
        factory.Backend.Enqueue(ClaimPath, HttpStatusCode.OK, ClaimSuccessWithSessionJson);
        // Their own record, read as themselves, so the exact contact is
        // available and the plus-alias a mask would hide survives.
        factory.Backend.Enqueue(
            "me",
            HttpStatusCode.OK,
            """
            {"id":"0195c0de-0000-7000-8000-000000000002",
             "email":"ayse+demo@example.com","phoneNumber":null,
             "status":"Active","contextType":"Identity"}
            """);
        factory.Backend.Enqueue(
            "/api/v1/me/gift-cards",
            HttpStatusCode.OK,
            """{"items":[],"limit":20,"nextCursor":null}""");

        using var browser = factory.CreateBrowser();
        await browser.GetAsync(new Uri($"/activate?token={CreateClaimToken()}", UriKind.Relative));
        await PostFormAsync(browser, "/activate/confirm", []);
        using var activated = await CompletePasswordAsync(browser);

        using var cards = await browser.GetAsync(activated.Headers.Location);
        var html = WebUtility.HtmlDecode(await cards.Content.ReadAsStringAsync());

        Assert.Contains("Next time, sign in with", html, StringComparison.Ordinal);
        Assert.Contains("ayse+demo@example.com", html, StringComparison.Ordinal);

        // Said once. A permanent banner would be noise on every later visit.
        using var again = await browser.GetAsync(new Uri("/cards", UriKind.Relative));
        var repeat = WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Next time, sign in with", repeat, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExistingAccountIsNotSignedInByClaimingACard()
    {
        // Possessing one invitation must not authenticate an account that may
        // already hold other cards, so the backend returns no session here.
        factory.Backend.Enqueue(ClaimPath, HttpStatusCode.OK, ClaimSuccessJson);

        using var browser = factory.CreateBrowser();
        await browser.GetAsync(new Uri($"/activate?token={CreateClaimToken()}", UriKind.Relative));

        using var response = await PostFormAsync(browser, "/activate/confirm", []);

        Assert.Equal("/signin", response.Headers.Location?.OriginalString);
        Assert.Equal(0, factory.Store.SessionCount);
    }

    [Fact]
    public async Task ClaimRequestsCarryTheClientAddressThisApplicationObserved()
    {
        factory.Backend.EnqueueProblem(
            ClaimPath,
            HttpStatusCode.BadRequest,
            BackendProblemException.Codes.PasswordRequired);

        using var browser = factory.CreateBrowser();
        await browser.GetAsync(new Uri($"/activate?token={CreateClaimToken()}", UriKind.Relative));
        await PostFormAsync(browser, "/activate/confirm", []);

        var claim = factory.Backend.Requests.Find(request =>
            request.Path.EndsWith(ClaimPath, StringComparison.Ordinal));

        // Without this the backend's per-source claim quota would partition on
        // this server's address and be shared by every recipient.
        Assert.NotNull(claim);
        Assert.Equal(CardholderAppFactory.ObservedClientAddress, claim!.Header("X-Forwarded-For"));
    }

    [Fact]
    public async Task ABrowserSuppliedForwardingHeaderIsNeverRelayed()
    {
        factory.Backend.EnqueueProblem(
            ClaimPath,
            HttpStatusCode.BadRequest,
            BackendProblemException.Codes.PasswordRequired);

        using var browser = factory.CreateBrowser();
        await browser.GetAsync(new Uri($"/activate?token={CreateClaimToken()}", UriKind.Relative));

        // A caller who could choose the forwarded address could choose which
        // rate-limit partition to consume, or exhaust someone else's.
        using var page = await browser.GetAsync(new Uri("/activate/confirm", UriKind.Relative));
        var token = CardholderAppFactory.ExtractAntiforgeryToken(
            await page.Content.ReadAsStringAsync());

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["__RequestVerificationToken"] = token,
            });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/activate/confirm", UriKind.Relative))
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.99");
        using var response = await browser.SendAsync(request);

        var claim = factory.Backend.Requests.Find(item =>
            item.Path.EndsWith(ClaimPath, StringComparison.Ordinal));
        Assert.NotNull(claim);

        var forwarded = claim!.Header("X-Forwarded-For");
        Assert.Equal(CardholderAppFactory.ObservedClientAddress, forwarded);
        Assert.DoesNotContain("198.51.100.99", forwarded!, StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> CompletePasswordAsync(HttpClient browser) =>
        PostFormAsync(
            browser,
            "/activate/password",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Password"] = "a long enough passphrase",
                ["ConfirmPassword"] = "a long enough passphrase",
            });

    [Fact]
    public async Task AShortPasswordIsRefusedBeforeReachingTheBackend()
    {
        factory.Backend.EnqueueProblem(
            ClaimPath,
            HttpStatusCode.BadRequest,
            BackendProblemException.Codes.PasswordRequired);

        using var browser = factory.CreateBrowser();
        await browser.GetAsync(new Uri($"/activate?token={CreateClaimToken()}", UriKind.Relative));
        await PostFormAsync(browser, "/activate/confirm", []);
        var callsBefore = factory.Backend.Requests.Count;

        using var response = await PostFormAsync(
            browser,
            "/activate/password",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Password"] = "short",
                ["ConfirmPassword"] = "short",
            });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("between 12 and 128 characters", body, StringComparison.Ordinal);
        Assert.Equal(callsBefore, factory.Backend.Requests.Count);
    }

    [Fact]
    public async Task MismatchedPasswordsAreRefused()
    {
        factory.Backend.EnqueueProblem(
            ClaimPath,
            HttpStatusCode.BadRequest,
            BackendProblemException.Codes.PasswordRequired);

        using var browser = factory.CreateBrowser();
        await browser.GetAsync(new Uri($"/activate?token={CreateClaimToken()}", UriKind.Relative));
        await PostFormAsync(browser, "/activate/confirm", []);

        using var response = await PostFormAsync(
            browser,
            "/activate/password",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Password"] = "a long enough passphrase",
                ["ConfirmPassword"] = "a different long passphrase",
            });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Both passwords must match", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAlreadyClaimedInvitationPointsTheRecipientAtSignIn()
    {
        factory.Backend.EnqueueProblem(
            ClaimPath,
            HttpStatusCode.Conflict,
            BackendProblemException.Codes.ClaimAlreadyCompleted);

        using var browser = factory.CreateBrowser();
        await browser.GetAsync(new Uri($"/activate?token={CreateClaimToken()}", UriKind.Relative));

        using var response = await PostFormAsync(browser, "/activate/confirm", []);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already been activated", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownInvitationGivesNothingAway()
    {
        factory.Backend.EnqueueProblem(
            ClaimPath,
            HttpStatusCode.Unauthorized,
            BackendProblemException.Codes.ClaimInvalid);

        using var browser = factory.CreateBrowser();
        await browser.GetAsync(new Uri($"/activate?token={CreateClaimToken()}", UriKind.Relative));

        using var response = await PostFormAsync(browser, "/activate/confirm", []);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(
            "not valid, has already been used, or has expired",
            body,
            StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient browser,
        string path,
        Dictionary<string, string> fields)
    {
        using var page = await browser.GetAsync(new Uri(path, UriKind.Relative));
        var token = CardholderAppFactory.ExtractAntiforgeryToken(
            await page.Content.ReadAsStringAsync());

        var form = new Dictionary<string, string>(fields, StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = token,
        };
        using var content = new FormUrlEncodedContent(form);
        return await browser.PostAsync(new Uri(path, UriKind.Relative), content);
    }
}
