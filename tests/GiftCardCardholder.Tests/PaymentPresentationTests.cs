using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Display;

namespace GiftCardCardholder.Tests;

public sealed partial class PaymentPresentationTests : IDisposable
{
    private const string AccessToken = "payment-access-token";
    private const string RefreshToken = "payment-refresh-token";
    private const string GiftCardId = "0195c0de-0000-7000-8000-000000000071";
    private const string PublicReference = "DEMO-PAY-0071";
    private const string RawToken =
        "0195c0de000070008000000000000081.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string NumericCode = "123456789012";
    private const string PaymentTokenId = "0195c0de-0000-7000-8000-000000000081";
    private const string PaymentPath =
        "/api/v1/me/gift-cards/" + GiftCardId + "/payment-tokens";

    private readonly CardholderAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task SignedInRecipientGeneratesOneNoStoreQrAndNumericPresentation()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        using var page = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative));
        var html = await page.Content.ReadAsStringAsync();
        var antiforgery = CardholderAppFactory.ExtractAntiforgeryToken(html);
        factory.Backend.Enqueue(PaymentPath, HttpStatusCode.Created, PaymentTokenJson);

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["__RequestVerificationToken"] = antiforgery,
            });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative))
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "browser-forged");

        using var issued = await browser.SendAsync(request);

        // Issuing redirects, so the live code is reachable by a GET and every
        // ordinary navigation keeps it.
        Assert.Equal(HttpStatusCode.Redirect, issued.StatusCode);
        var credentialUrl = issued.Headers.Location!;
        Assert.Equal(
            $"/cards/{GiftCardId}/pay/{PaymentTokenId}",
            credentialUrl.OriginalString,
            ignoreCase: true);

        using var response = await browser.GetAsync(credentialUrl);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Contains(PublicReference, body, StringComparison.Ordinal);
        Assert.Contains("1234 5678 9012", body, StringComparison.Ordinal);
        Assert.Contains("src=\"data:image/png;base64,", body, StringComparison.Ordinal);
        Assert.Contains("12-digit payment code", body, StringComparison.Ordinal);
        Assert.Contains(
            $"/cards/{GiftCardId}/pay/status?paymentTokenId={PaymentTokenId}",
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
        Assert.Single(HeadingPattern().Matches(body));
        Assert.DoesNotContain(RawToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshToken, body, StringComparison.Ordinal);

        var backendRequest = factory.Backend.Requests.Single(recorded =>
            recorded.Path.EndsWith(PaymentPath, StringComparison.Ordinal));
        Assert.Equal("POST", backendRequest.Method);
        Assert.Equal($"Bearer {AccessToken}", backendRequest.Header("Authorization"));
        Assert.Null(backendRequest.Body);
    }

    [Fact]
    public async Task PaymentGenerationWithoutAntiforgeryNeverReachesBackend()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        var before = factory.Backend.Requests.Count;

        using var response = await browser.PostAsync(
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative),
            new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/session-expired", response.Headers.Location?.OriginalString);
        Assert.Equal(before, factory.Backend.Requests.Count);

        using var recovery = await browser.GetAsync(response.Headers.Location);
        var body = await recovery.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        Assert.Equal("text/html", recovery.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Your session expired", body, StringComparison.Ordinal);
        Assert.Contains("Start again", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckoutStatusRefreshesUntilItShowsTheConfirmedAmount()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        var backendPath = PaymentPath + "/" + PaymentTokenId;
        factory.Backend.Enqueue(
            backendPath,
            HttpStatusCode.OK,
            PaymentStatusJson("Pending", amount: null, confirmedAmount: null));
        factory.Backend.Enqueue(
            backendPath,
            HttpStatusCode.OK,
            PaymentStatusJson("Confirmed", amount: 30m, confirmedAmount: 24m));
        var pagePath = $"/cards/{GiftCardId}/pay/status?paymentTokenId={PaymentTokenId}";

        using var pending = await browser.GetAsync(new Uri(pagePath, UriKind.Relative));
        var pendingBody = await pending.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        Assert.Contains("http-equiv=\"refresh\"", pendingBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Waiting for the cashier", pendingBody, StringComparison.Ordinal);
        Assert.Equal("SAMEORIGIN", pending.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains(
            "frame-ancestors 'self'",
            pending.Headers.GetValues("Content-Security-Policy").Single(),
            StringComparison.Ordinal);

        using var confirmed = await browser.GetAsync(new Uri(pagePath, UriKind.Relative));
        var confirmedBody = await confirmed.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Contains("Payment complete", confirmedBody, StringComparison.Ordinal);
        Assert.Contains("24.00 TRY", confirmedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("http-equiv=\"refresh\"", confirmedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RawToken, pendingBody + confirmedBody, StringComparison.Ordinal);
        Assert.DoesNotContain(NumericCode, pendingBody + confirmedBody, StringComparison.Ordinal);

        Assert.All(
            factory.Backend.Requests.Where(request =>
                request.Path.EndsWith(backendPath, StringComparison.Ordinal)),
            request => Assert.Equal($"Bearer {AccessToken}", request.Header("Authorization")));
    }

    [Theory]
    [InlineData("Cancelled", "Payment not completed")]
    [InlineData("Expired", "Payment code expired")]
    public async Task TerminalCheckoutFailuresExplainWhatHappenedWithoutRefreshing(
        string state,
        string expectedHeading)
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        factory.Backend.Enqueue(
            PaymentPath + "/" + PaymentTokenId,
            HttpStatusCode.OK,
            PaymentStatusJson(state, amount: 30m, confirmedAmount: null));

        using var response = await browser.GetAsync(new Uri(
            $"/cards/{GiftCardId}/pay/status?paymentTokenId={PaymentTokenId}",
            UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedHeading, body, StringComparison.Ordinal);
        Assert.DoesNotContain("http-equiv=\"refresh\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckoutCodeCountsDownInSecondsBelowTheQrWithoutScript()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        using var page = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative));
        var antiforgery = CardholderAppFactory.ExtractAntiforgeryToken(
            await page.Content.ReadAsStringAsync());
        factory.Backend.Enqueue(PaymentPath, HttpStatusCode.Created, PaymentTokenJson);

        using var issued = await browser.PostAsync(
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative),
            new FormUrlEncodedContent(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["__RequestVerificationToken"] = antiforgery,
                }));
        using var response = await browser.GetAsync(issued.Headers.Location);
        var body = await response.Content.ReadAsStringAsync();

        // Every second is present, so the counter is real rather than an icon
        // standing in for one.
        Assert.Contains("countdown__strip", body, StringComparison.Ordinal);
        Assert.Contains("payment-code-stage", body, StringComparison.Ordinal);
        Assert.Contains("payment-code-renew", body, StringComparison.Ordinal);
        Assert.Contains("Payment code expired", body, StringComparison.Ordinal);
        Assert.Contains("Generate new payment code", body, StringComparison.Ordinal);
        Assert.Contains(
            $"action=\"/cards/{GiftCardId}/pay\"",
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">60<small>s</small>", body, StringComparison.Ordinal);
        Assert.Contains(">1<small>s</small>", body, StringComparison.Ordinal);
        Assert.Contains(">0<small>s</small>", body, StringComparison.Ordinal);
        Assert.DoesNotContain("&#9203;", body, StringComparison.Ordinal);

        // The default HTML-only deployment drives it entirely with CSS.
        Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);

        // The offset is a class on the shared parent, so the ring and the
        // number cannot disagree. It must not be an inline style: this
        // application serves style-src 'self', which discards those.
        //
        // A freshly issued code is barely any seconds old, but not reliably
        // zero of them: under a loaded machine the render can land a second
        // after issuance. Asserting the exact class made this fail only in a
        // full run, which is the worst way for a test to be wrong.
        var offset = OffsetPattern().Match(body);
        Assert.True(offset.Success, "The countdown carried no offset class.");
        Assert.InRange(
            int.Parse(offset.Groups[1].Value, CultureInfo.InvariantCulture), 0, 2);
        Assert.DoesNotContain("animation-delay", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" style=", body, StringComparison.Ordinal);

        using var stylesheet = await browser.GetAsync(
            new Uri("/css/app.css", UriKind.Relative));
        var css = await stylesheet.Content.ReadAsStringAsync();
        Assert.Contains("@keyframes payment-qr-expire", css, StringComparison.Ordinal);
        Assert.Contains("filter: blur", css, StringComparison.Ordinal);
        Assert.Contains("@keyframes payment-code-renew", css, StringComparison.Ordinal);
        Assert.Contains("visibility: visible", css, StringComparison.Ordinal);

        Assert.True(
            body.IndexOf("payment-qr", StringComparison.Ordinal)
                < body.IndexOf("countdown__ring-container", StringComparison.Ordinal),
            "The countdown belongs below the QR code it applies to.");
    }

    [Fact]
    public async Task ExpiryOverlayPostsAReplacementCodeFromTheSamePage()
    {
        const string replacementTokenId =
            "0195c0de-0000-7000-8000-000000000082";
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        var credentialUrl = await IssueCodeAsync(browser);
        using var livePage = await browser.GetAsync(credentialUrl);
        var body = await livePage.Content.ReadAsStringAsync();
        var antiforgery = CardholderAppFactory.ExtractAntiforgeryToken(body);
        factory.Backend.Enqueue(
            PaymentPath,
            HttpStatusCode.Created,
            PaymentTokenJson.Replace(
                PaymentTokenId,
                replacementTokenId,
                StringComparison.Ordinal));

        using var renewed = await browser.PostAsync(
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative),
            new FormUrlEncodedContent(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["__RequestVerificationToken"] = antiforgery,
                }));

        Assert.Equal(HttpStatusCode.Redirect, renewed.StatusCode);
        Assert.Equal(
            $"/cards/{GiftCardId}/pay/{replacementTokenId}",
            renewed.Headers.Location?.OriginalString,
            ignoreCase: true);
        Assert.Equal(
            2,
            factory.Backend.Requests.Count(request =>
                request.Path.EndsWith(PaymentPath, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ALiveCheckoutCodeSurvivesReloadAndSwitchingLanguage()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        var credentialUrl = await IssueCodeAsync(browser);

        // Reload: the same code, not an offer to generate another.
        using var reloaded = await browser.GetAsync(credentialUrl);
        var reloadedBody = await reloaded.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, reloaded.StatusCode);
        Assert.Contains("1234 5678 9012", reloadedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Generate payment code", reloadedBody, StringComparison.Ordinal);

        // Switching language returns to the code, translated. This is the
        // reported bug: it used to land on "generate a code", which is also
        // why the page could never be seen in Turkish.
        using var switched = await browser.PostAsync(
            new Uri("/language", UriKind.Relative),
            new FormUrlEncodedContent(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["culture"] = "tr",
                    ["returnUrl"] = credentialUrl.OriginalString,
                    ["__RequestVerificationToken"] =
                        CardholderAppFactory.ExtractAntiforgeryToken(reloadedBody),
                }));
        Assert.Equal(credentialUrl.OriginalString, switched.Headers.Location?.OriginalString);

        using var turkish = await browser.GetAsync(switched.Headers.Location);
        var turkishBody = await turkish.Content.ReadAsStringAsync();
        Assert.Contains("<html lang=\"tr\"", turkishBody, StringComparison.Ordinal);
        Assert.Contains("1234 5678 9012", turkishBody, StringComparison.Ordinal);
        Assert.Contains("Kasa kodunuz", turkishBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Your checkout code", turkishBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReloadingShowsTheTimeActuallyLeftRatherThanRestartingAtSixty()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        using var page = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative));
        var antiforgery = CardholderAppFactory.ExtractAntiforgeryToken(
            await page.Content.ReadAsStringAsync());

        // A code with twenty seconds left is forty seconds old.
        factory.Backend.Enqueue(
            PaymentPath,
            HttpStatusCode.Created,
            AgedPaymentTokenJson(TimeSpan.FromSeconds(20)));
        using var issued = await browser.PostAsync(
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative),
            new FormUrlEncodedContent(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["__RequestVerificationToken"] = antiforgery,
                }));

        using var response = await browser.GetAsync(issued.Headers.Location);
        var body = await response.Content.ReadAsStringAsync();

        var offset = OffsetPattern().Match(body);
        Assert.True(offset.Success, "The countdown carried no offset class.");
        var elapsed = int.Parse(offset.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.InRange(elapsed, 39, 41);
    }

    [Fact]
    public async Task AnotherRecipientsSessionCannotRenderTheCode()
    {
        using var owner = factory.CreateBrowser();
        await SignInAsync(owner);
        var credentialUrl = await IssueCodeAsync(owner);

        using var stranger = factory.CreateBrowser();
        await SignInAsync(stranger);
        using var response = await stranger.GetAsync(credentialUrl);

        // The credential is bound to the session that asked for it, so knowing
        // the identifier is not enough to display someone else's code.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            $"/cards/{GiftCardId}/pay",
            response.Headers.Location?.OriginalString,
            ignoreCase: true);
    }

    private async Task<Uri> IssueCodeAsync(HttpClient browser)
    {
        using var page = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative));
        var antiforgery = CardholderAppFactory.ExtractAntiforgeryToken(
            await page.Content.ReadAsStringAsync());
        factory.Backend.Enqueue(PaymentPath, HttpStatusCode.Created, PaymentTokenJson);

        using var issued = await browser.PostAsync(
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative),
            new FormUrlEncodedContent(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["__RequestVerificationToken"] = antiforgery,
                }));
        Assert.Equal(HttpStatusCode.Redirect, issued.StatusCode);
        return issued.Headers.Location!;
    }

    [Fact]
    public async Task UnreadableCheckoutIsNotReportedAsAnExpiredCode()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        factory.Backend.Enqueue(
            PaymentPath + "/" + PaymentTokenId,
            HttpStatusCode.NotFound,
            """{"type":"about:blank","title":"Not Found","status":404}""");

        using var response = await browser.GetAsync(new Uri(
            $"/cards/{GiftCardId}/pay/status?paymentTokenId={PaymentTokenId}",
            UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Checkout status unavailable", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Payment code expired", body, StringComparison.Ordinal);
        Assert.DoesNotContain("was not completed", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmedCheckoutShowsAMarkAndNotOnlyWords()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        factory.Backend.Enqueue(
            PaymentPath + "/" + PaymentTokenId,
            HttpStatusCode.OK,
            PaymentStatusJson("Confirmed", amount: 30m, confirmedAmount: 30m));

        using var response = await browser.GetAsync(new Uri(
            $"/cards/{GiftCardId}/pay/status?paymentTokenId={PaymentTokenId}",
            UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("payment-status__mark-tick", body, StringComparison.Ordinal);
        Assert.Contains("Payment complete", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnonymousPaymentPageRedirectsToSignInWithoutBackendCall()
    {
        using var browser = factory.CreateBrowser();

        using var response = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signin", response.Headers.Location?.OriginalString);
        Assert.Empty(factory.Backend.Requests);
    }

    [Fact]
    public async Task MalformedBackendNumericCodeFailsClosedWithoutRenderingCredentials()
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        using var page = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative));
        var antiforgery = CardholderAppFactory.ExtractAntiforgeryToken(
            await page.Content.ReadAsStringAsync());
        factory.Backend.Enqueue(
            PaymentPath,
            HttpStatusCode.Created,
            PaymentTokenJson.Replace(NumericCode, "1234", StringComparison.Ordinal));

        using var response = await browser.PostAsync(
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative),
            new FormUrlEncodedContent(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["__RequestVerificationToken"] = antiforgery,
                }));
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ActivationMessages.TemporarilyUnavailable, body, StringComparison.Ordinal);
        Assert.DoesNotContain(RawToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image/png", body, StringComparison.Ordinal);
    }

    [Fact]
    public void QrRendererProducesPngDataWithoutEmbeddingCredentialAsText()
    {
        var dataUri = PaymentQrCode.CreateDataUri(RawToken);
        var png = Convert.FromBase64String(dataUri["data:image/png;base64,".Length..]);

        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
        Assert.DoesNotContain(RawToken, dataUri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("123456789012", "1234 5678 9012")]
    [InlineData("1234", null)]
    [InlineData("１２３４５６７８９０１２", null)]
    public void NumericDisplayAcceptsOnlyTwelveAsciiDigits(string value, string? expected)
    {
        var accepted = NumericPaymentCodeDisplay.TryFormat(value, out var formatted);

        Assert.Equal(expected is not null, accepted);
        Assert.Equal(expected ?? string.Empty, formatted);
    }

    private async Task SignInAsync(HttpClient browser)
    {
        factory.Backend.Enqueue("auth/login", HttpStatusCode.OK, TokenPairJson);
        factory.Backend.Enqueue("me", HttpStatusCode.OK, CurrentUserJson);
        using var page = await browser.GetAsync(new Uri("/signin", UriKind.Relative));
        var antiforgery = CardholderAppFactory.ExtractAntiforgeryToken(
            await page.Content.ReadAsStringAsync());
        using var response = await browser.PostAsync(
            new Uri("/signin", UriKind.Relative),
            new FormUrlEncodedContent(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Identifier"] = "recipient@example.com",
                    ["Password"] = "a long enough passphrase",
                    ["__RequestVerificationToken"] = antiforgery,
                }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static string Iso(TimeSpan fromNow) =>
        DateTimeOffset.UtcNow.Add(fromNow).ToString("O", CultureInfo.InvariantCulture);

    private static string TokenPairJson => $$"""
        {
          "accessToken": "{{AccessToken}}",
          "accessTokenExpiresAtUtc": "{{Iso(TimeSpan.FromMinutes(15))}}",
          "refreshToken": "{{RefreshToken}}",
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

    private static string PaymentTokenJson => $$"""
        {
          "id": "{{PaymentTokenId}}",
          "giftCardId": "{{GiftCardId}}",
          "giftCardPublicReference": "{{PublicReference}}",
          "rawToken": "{{RawToken}}",
          "numericCode": "{{NumericCode}}",
          "issuedAtUtc": "{{Iso(TimeSpan.Zero)}}",
          "expiresAtUtc": "{{Iso(TimeSpan.FromSeconds(60))}}"
        }
        """;

    private static string AgedPaymentTokenJson(TimeSpan remaining) => $$"""
        {
          "id": "{{PaymentTokenId}}",
          "giftCardId": "{{GiftCardId}}",
          "giftCardPublicReference": "{{PublicReference}}",
          "rawToken": "{{RawToken}}",
          "numericCode": "{{NumericCode}}",
          "issuedAtUtc": "{{Iso(remaining - TimeSpan.FromSeconds(60))}}",
          "expiresAtUtc": "{{Iso(remaining)}}"
        }
        """;

    private static string PaymentStatusJson(
        string state,
        decimal? amount,
        decimal? confirmedAmount) => $$"""
        {
          "id": "{{PaymentTokenId}}",
          "giftCardId": "{{GiftCardId}}",
          "state": "{{state}}",
          "paymentProvisionId": "0195c0de-0000-7000-8000-000000000091",
          "amount": {{(amount?.ToString(CultureInfo.InvariantCulture) ?? "null")}},
          "currency": "TRY",
          "expiresAtUtc": "{{Iso(TimeSpan.FromMinutes(2))}}",
          "settledAtUtc": null,
          "confirmedAmount": {{(confirmedAmount?.ToString(CultureInfo.InvariantCulture) ?? "null")}}
        }
        """;

    [GeneratedRegex("<h1[ >]", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"countdown--o([0-9]+)")]
    private static partial Regex OffsetPattern();

    // A backend fault and a deliberate refusal are different events and must not
    // read the same. Telling a recipient to check their card when the server
    // threw sends them hunting for a fault they do not have, at a till.
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.Conflict, false)]
    public async Task BackendFaultAndRefusalAreReportedDifferently(
        HttpStatusCode status,
        bool expectServerFaultWording)
    {
        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);
        using var page = await browser.GetAsync(
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative));
        var html = await page.Content.ReadAsStringAsync();
        var antiforgery = CardholderAppFactory.ExtractAntiforgeryToken(html);
        factory.Backend.EnqueueProblem(PaymentPath, status, "payment.token.unavailable");

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["__RequestVerificationToken"] = antiforgery,
            });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/cards/{GiftCardId}/pay", UriKind.Relative))
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "browser-forged");

        using var response = await browser.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (expectServerFaultWording)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("went wrong on our side", body, StringComparison.Ordinal);
            Assert.Contains("has not been charged", body, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Check the card&#x27;s status", body, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Check the card&#x27;s status", body, StringComparison.Ordinal);
            Assert.DoesNotContain("went wrong on our side", body, StringComparison.Ordinal);
        }
    }
}
