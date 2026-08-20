using System.Net;

namespace GiftCardCardholder.Tests;

/// <summary>
/// Proves that JavaScript is a deployment choice rather than a build variant,
/// and that enabling it relaxes only the one CSP source it needs.
/// </summary>
public sealed class UiEnhancementTests
{
    [Fact]
    public async Task EnhancementsAreDisabledByDefault()
    {
        using var factory = new CardholderAppFactory();
        using var browser = factory.CreateBrowser();

        using var response = await browser.GetAsync(new Uri("/signin", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("/js/enhancements.js", body, StringComparison.Ordinal);
        Assert.Contains("data-enhancements=\"disabled\"", body, StringComparison.Ordinal);
        Assert.Contains("script-src 'none'", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OperatorCanEnableSameOriginEnhancements()
    {
        using var factory = new CardholderAppFactory(enableJavaScriptEnhancements: true);
        using var browser = factory.CreateBrowser();

        using var response = await browser.GetAsync(new Uri("/signin", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("type=\"module\"", body, StringComparison.Ordinal);
        Assert.Contains("/js/enhancements.js?v=", body, StringComparison.Ordinal);
        Assert.Contains("data-enhancements=\"enabled\"", body, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", policy, StringComparison.Ordinal);

        // The same server-rendered form remains the usable application.
        Assert.Contains("method=\"post\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"Identifier\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnhancementModuleIsAStaticSameOriginAsset()
    {
        using var factory = new CardholderAppFactory(enableJavaScriptEnhancements: true);
        using var browser = factory.CreateBrowser();

        using var response = await browser.GetAsync(
            new Uri("/js/enhancements.js", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("enhancements-active", body, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", body, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", body, StringComparison.Ordinal);
    }
}
