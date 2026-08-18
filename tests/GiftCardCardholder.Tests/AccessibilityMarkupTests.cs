using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace GiftCardCardholder.Tests;

/// <summary>
/// Structural accessibility checks on the rendered HTML.
///
/// These fast structural checks complement the CARD-003 Playwright + axe gate.
/// They assert the subset that is decidable directly from server-rendered
/// markup: language, zoomable viewport, heading structure, labels, and skip
/// navigation.
/// </summary>
public sealed partial class AccessibilityMarkupTests : IDisposable
{
    private static readonly string TokenPairJson = $$"""
        {
          "accessToken": "access",
          "accessTokenExpiresAtUtc": "{{Iso(TimeSpan.FromMinutes(15))}}",
          "refreshToken": "refresh",
          "refreshTokenExpiresAtUtc": "{{Iso(TimeSpan.FromDays(30))}}"
        }
        """;

    private const string CurrentUserJson = """
        {"id":"0195c0de-0000-7000-8000-000000000009","email":"r@example.com",
         "phoneNumber":null,"status":"Active","contextType":"Identity"}
        """;

    private const string CardsJson = """
        {"items":[{"id":"0195c0de-0000-7000-8000-00000000000a","publicReference":"GC-ABCD-EFGH",
         "lifecycleState":"Active","fundedAmount":500.00,"balance":320.50,
         "reservedBalance":0.00,"availableBalance":320.50,"currency":"TRY",
         "validFromUtc":"2026-07-01T00:00:00+00:00","expiresAtUtc":"2027-07-01T00:00:00+00:00",
         "claimedAtUtc":"2026-07-29T10:00:00+00:00","issuedAtUtc":"2026-07-01T00:00:00+00:00"}],
         "limit":20,"nextCursor":null}
        """;

    private readonly CardholderAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    private static string Iso(TimeSpan fromNow) =>
        DateTimeOffset.UtcNow.Add(fromNow).ToString("O", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("/signin")]
    [InlineData("/activate?token=malformed")]
    public async Task PagesDeclareLanguageAndAZoomableViewport(string path)
    {
        using var browser = factory.CreateBrowser();

        using var response = await browser.GetAsync(new Uri(path, UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("<html lang=\"en\" ", html, StringComparison.Ordinal);
        Assert.Contains("width=device-width", html, StringComparison.Ordinal);

        // A maximum-scale or user-scalable=no viewport blocks pinch zoom, which
        // people with low vision rely on.
        Assert.DoesNotContain("user-scalable=no", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("maximum-scale", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/signin")]
    [InlineData("/activate?token=malformed")]
    public async Task PagesHaveExactlyOneFirstLevelHeading(string path)
    {
        using var browser = factory.CreateBrowser();

        using var response = await browser.GetAsync(new Uri(path, UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Single(HeadingPattern().Matches(html));
    }

    [Fact]
    public async Task EveryVisibleInputHasALabelBoundToIt()
    {
        using var browser = factory.CreateBrowser();

        using var response = await browser.GetAsync(new Uri("/signin", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        var labelled = LabelPattern().Matches(html)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var inputs = InputIdPattern().Matches(html)
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(inputs);
        foreach (var id in inputs)
        {
            Assert.True(labelled.Contains(id), $"Input '{id}' has no <label for> bound to it.");
        }
    }

    [Fact]
    public async Task ThereIsASkipLinkToTheMainContent()
    {
        using var browser = factory.CreateBrowser();

        using var response = await browser.GetAsync(new Uri("/signin", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("class=\"skip-link\" href=\"#main\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"main\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCardListExposesBalancesAsTextRatherThanColourAlone()
    {
        factory.Backend.Enqueue("auth/login", HttpStatusCode.OK, TokenPairJson);
        factory.Backend.Enqueue("me", HttpStatusCode.OK, CurrentUserJson);
        factory.Backend.Enqueue("gift-cards", HttpStatusCode.OK, CardsJson);

        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);

        using var response = await browser.GetAsync(new Uri("/cards", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        // State is written out, not conveyed only by the badge colour.
        Assert.Contains("320.50 TRY", html, StringComparison.Ordinal);
        Assert.Contains("Active", html, StringComparison.Ordinal);
        Assert.Single(HeadingPattern().Matches(html));
    }

    private static async Task SignInAsync(HttpClient browser)
    {
        using var page = await browser.GetAsync(new Uri("/signin", UriKind.Relative));
        var token = CardholderAppFactory.ExtractAntiforgeryToken(
            await page.Content.ReadAsStringAsync());

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Identifier"] = "r@example.com",
                ["Password"] = "a long enough passphrase",
                ["__RequestVerificationToken"] = token,
            });
        using var response = await browser.PostAsync(new Uri("/signin", UriKind.Relative), content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [GeneratedRegex("<h1[ >]", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex("""<label[^>]*\sfor="([^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPattern();

    [GeneratedRegex("""<input(?![^>]*type="hidden")[^>]*\sid="([^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex InputIdPattern();
}
