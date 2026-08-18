using System.Net;

namespace GiftCardCardholder.Tests;

/// <summary>
/// The theme has to survive without JavaScript, so the only thing that can
/// carry it is a cookie the server reads while rendering. These tests pin the
/// three states and the same hardening the language switch already has.
/// </summary>
public sealed class ThemeSwitchTests : IDisposable
{
    private readonly CardholderAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task DefaultIsFollowTheDeviceAndIsStampedOnTheDocument()
    {
        using var browser = factory.CreateBrowser();

        using var page = await browser.GetAsync("/signin");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("data-theme=\"system\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppearanceIsACollapsedMenuAndTheLanguageButtonCarriesAFlag()
    {
        using var browser = factory.CreateBrowser();

        using var page = await browser.GetAsync("/signin");
        var html = await page.Content.ReadAsStringAsync();

        // Collapsed: <details> with no open attribute, holding all three options.
        Assert.Contains("<details class=\"menu\">", html, StringComparison.Ordinal);
        Assert.Contains("name=\"theme\" value=\"light\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"theme\" value=\"dark\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"theme\" value=\"system\"", html, StringComparison.Ordinal);

        // The flag partial resolved rather than rendering nothing.
        Assert.Contains("class=\"flag\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public async Task ChoosingAThemeStampsItAndUsesAHardenedCookie(string theme)
    {
        using var browser = factory.CreateBrowser();
        using var post = await PostThemeAsync(browser, theme, "/signin?source=theme");

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal("/signin?source=theme", post.Headers.Location?.OriginalString);
        var cookie = string.Join(' ', post.Headers.GetValues("Set-Cookie"));
        Assert.Contains("giftcard_theme=", cookie, StringComparison.Ordinal);
        Assert.Contains("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Lax", cookie, StringComparison.OrdinalIgnoreCase);

        using var page = await browser.GetAsync("/signin");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains($"data-theme=\"{theme}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturningToTheDeviceSettingClearsTheChoice()
    {
        using var browser = factory.CreateBrowser();
        using (var choose = await PostThemeAsync(browser, "dark", "/signin"))
        {
            Assert.Equal(HttpStatusCode.Redirect, choose.StatusCode);
        }

        using var reset = await PostThemeAsync(browser, "system", "/signin");
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);

        using var page = await browser.GetAsync("/signin");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("data-theme=\"system\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedThemeIsRejectedWithoutSettingACookie()
    {
        using var browser = factory.CreateBrowser();
        using var response = await PostThemeAsync(browser, "midnight", "/signin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task ReturnUrlCannotRedirectOffOrigin()
    {
        using var browser = factory.CreateBrowser();
        using var response = await PostThemeAsync(
            browser,
            "dark",
            "https://attacker.example/steal");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/cards", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ThemeEndpointIsPostOnlyAndAntiforgeryProtected()
    {
        using var browser = factory.CreateBrowser();

        using var get = await browser.GetAsync("/theme");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["theme"] = "dark",
                ["returnUrl"] = "/signin",
            });
        using var post = await browser.PostAsync("/theme", content);
        Assert.Equal(HttpStatusCode.SeeOther, post.StatusCode);
        Assert.Equal("/session-expired", post.Headers.Location?.OriginalString);
    }

    private static async Task<HttpResponseMessage> PostThemeAsync(
        HttpClient browser,
        string theme,
        string returnUrl)
    {
        using var page = await browser.GetAsync("/signin");
        var token = CardholderAppFactory.ExtractAntiforgeryToken(
            await page.Content.ReadAsStringAsync());
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["theme"] = theme,
                ["returnUrl"] = returnUrl,
                ["__RequestVerificationToken"] = token,
            });
        return await browser.PostAsync("/theme", content);
    }
}
