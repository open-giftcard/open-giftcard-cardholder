using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using GiftCardCardholder.Web.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Tests;

public sealed class LocalizationJourneyTests : IDisposable
{
    private const string TokenPairJson = """
        {
          "accessToken": "localized-access",
          "accessTokenExpiresAtUtc": "2030-01-01T00:00:00Z",
          "refreshToken": "localized-refresh",
          "refreshTokenExpiresAtUtc": "2030-02-01T00:00:00Z"
        }
        """;

    private const string CurrentUserJson = """
        {"id":"0195c0de-0000-7000-8000-000000000009","email":"r@example.com",
         "phoneNumber":null,"status":"Active","contextType":"Identity"}
        """;

    private const string CardsJson = """
        {"items":[{"id":"0195c0de-0000-7000-8000-00000000000a","publicReference":"GC-TR-0001",
         "lifecycleState":"Active","fundedAmount":1234.50,"balance":1234.50,
         "reservedBalance":0.00,"availableBalance":1234.50,"currency":"TRY",
         "validFromUtc":"2026-07-01T00:00:00Z","expiresAtUtc":"2027-07-01T00:00:00Z",
         "claimedAtUtc":"2026-07-02T00:00:00Z","issuedAtUtc":"2026-07-01T00:00:00Z"}],
         "limit":20,"nextCursor":null}
        """;

    private readonly CardholderAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public void TurkishResourceIsDiscoverableByTheApplicationLocalizer()
    {
        var localizer = factory.Services.GetRequiredService<IStringLocalizer<SharedResource>>();
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr");
            var result = localizer["Sign in"];

            Assert.False(
                result.ResourceNotFound,
                $"Resource search failed at '{result.SearchedLocation}'.");
            Assert.Equal("Giriş yap", result.Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public async Task EnglishIsDefaultEvenWhenBrowserRequestsTurkish()
    {
        using var browser = factory.CreateBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/signin");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("tr-TR"));

        using var response = await browser.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"en\" ", html, StringComparison.Ordinal);
        Assert.Contains("<h1>Sign in</h1>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1>Giriş yap</h1>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LanguagePostSwitchesToTurkishAndUsesHardenedCookie()
    {
        using var browser = factory.CreateBrowser();
        using var post = await PostLanguageAsync(browser, "tr", "/signin?source=language");

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal("/signin?source=language", post.Headers.Location?.OriginalString);
        var cookie = string.Join(' ', post.Headers.GetValues("Set-Cookie"));
        Assert.Contains(".AspNetCore.Culture=", cookie, StringComparison.Ordinal);
        Assert.Contains("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Lax", cookie, StringComparison.OrdinalIgnoreCase);

        using var page = await browser.GetAsync("/signin");
        var rawHtml = await page.Content.ReadAsStringAsync();
        var html = WebUtility.HtmlDecode(rawHtml);
        Assert.Contains("<html lang=\"tr\" ", rawHtml, StringComparison.Ordinal);
        Assert.Contains("<h1>Giriş yap</h1>", html, StringComparison.Ordinal);

        // The chip names the language in force, tagged as that language, and
        // says what pressing it does only through its accessible name. Labelling
        // a Turkish page "English" read as a claim that the page was English.
        Assert.Contains("<span lang=\"tr\" aria-hidden=\"true\">Türkçe</span>", html, StringComparison.Ordinal);
        Assert.Contains("English diline geç", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">English</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedCultureIsRejectedWithoutChangingTheDefault()
    {
        using var browser = factory.CreateBrowser();
        using var response = await PostLanguageAsync(browser, "de", "/signin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));

        using var page = await browser.GetAsync("/signin");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("<html lang=\"en\" ", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LanguageReturnUrlCannotRedirectOffOrigin()
    {
        using var browser = factory.CreateBrowser();
        using var response = await PostLanguageAsync(
            browser,
            "tr",
            "https://attacker.example/steal");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signin", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task LanguageEndpointIsPostOnlyAndAntiforgeryProtected()
    {
        using var browser = factory.CreateBrowser();

        using var get = await browser.GetAsync("/language");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["culture"] = "tr",
                ["returnUrl"] = "/signin",
            });
        using var post = await browser.PostAsync("/language", content);
        Assert.Equal(HttpStatusCode.SeeOther, post.StatusCode);
        Assert.Equal("/session-expired", post.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task TurkishValidationAndCardValuesAreLocalizedWithoutChangingAuthority()
    {
        using var browser = factory.CreateBrowser();
        using var language = await PostLanguageAsync(browser, "tr", "/signin");
        Assert.Equal(HttpStatusCode.Redirect, language.StatusCode);

        using var signInPage = await browser.GetAsync("/signin");
        var signInHtml = await signInPage.Content.ReadAsStringAsync();
        var token = CardholderAppFactory.ExtractAntiforgeryToken(signInHtml);

        using (var invalidContent = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Identifier"] = string.Empty,
                ["Password"] = string.Empty,
                ["__RequestVerificationToken"] = token,
            }))
        using (var invalid = await browser.PostAsync("/signin", invalidContent))
        {
            var invalidHtml = WebUtility.HtmlDecode(
                await invalid.Content.ReadAsStringAsync());
            Assert.Contains(
                "E-posta adresinizi veya telefon numaranızı ve parolanızı girin.",
                invalidHtml,
                StringComparison.Ordinal);
        }

        factory.Backend.Enqueue("auth/login", HttpStatusCode.OK, TokenPairJson);
        factory.Backend.Enqueue("me", HttpStatusCode.OK, CurrentUserJson);
        factory.Backend.Enqueue("gift-cards", HttpStatusCode.OK, CardsJson);
        using var freshPage = await browser.GetAsync("/signin");
        token = CardholderAppFactory.ExtractAntiforgeryToken(
            await freshPage.Content.ReadAsStringAsync());
        using var validContent = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Identifier"] = "r@example.com",
                ["Password"] = "a long enough passphrase",
                ["__RequestVerificationToken"] = token,
            });
        using var signIn = await browser.PostAsync("/signin", validContent);
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);

        using var cards = await browser.GetAsync("/cards");
        var cardsHtml = WebUtility.HtmlDecode(await cards.Content.ReadAsStringAsync());
        Assert.Contains("GC-TR-0001", cardsHtml, StringComparison.Ordinal);
        Assert.Contains("1.234,50 TRY", cardsHtml, StringComparison.Ordinal);
        Assert.Contains("Aktif", cardsHtml, StringComparison.Ordinal);
        Assert.Contains("1 Temmuz 2027", cardsHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("localized-access", cardsHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("localized-refresh", cardsHtml, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> PostLanguageAsync(
        HttpClient browser,
        string culture,
        string returnUrl)
    {
        using var page = await browser.GetAsync("/signin");
        var token = CardholderAppFactory.ExtractAntiforgeryToken(
            await page.Content.ReadAsStringAsync());
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["culture"] = culture,
                ["returnUrl"] = returnUrl,
                ["__RequestVerificationToken"] = token,
            });
        return await browser.PostAsync("/language", content);
    }
}
