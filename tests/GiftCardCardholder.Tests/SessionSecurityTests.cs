using System.Globalization;
using System.Net;
using GiftCardCardholder.Web.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace GiftCardCardholder.Tests;

/// <summary>
/// Guards the properties that make this a safe place to hold a backend session:
/// tokens stay on the server, cookies are not script-readable, and unsafe
/// requests need an antiforgery token.
/// </summary>
public sealed class SessionSecurityTests : IDisposable
{
    private const string AccessTokenValue = "header.payload.signature-access";
    private const string RefreshTokenValue = "opaque-refresh-token-value";

    /// <summary>
    /// Lifetimes mirror the real backend's 15-minute access and 30-day refresh
    /// tokens, and are relative to now so the fixture cannot go stale.
    /// </summary>
    private static string TokenPairJson => $$"""
        {
          "accessToken": "{{AccessTokenValue}}",
          "accessTokenExpiresAtUtc": "{{Iso(TimeSpan.FromMinutes(15))}}",
          "refreshToken": "{{RefreshTokenValue}}",
          "refreshTokenExpiresAtUtc": "{{Iso(TimeSpan.FromDays(30))}}"
        }
        """;

    private static string Iso(TimeSpan fromNow) =>
        DateTimeOffset.UtcNow.Add(fromNow).ToString("O", CultureInfo.InvariantCulture);

    private const string CurrentUserJson = """
        {
          "id": "0195c0de-0000-7000-8000-000000000009",
          "email": "recipient@example.com",
          "phoneNumber": null,
          "status": "Active",
          "contextType": "Identity"
        }
        """;

    private const string EmptyCardsJson = """
        {"items": [], "limit": 20, "nextCursor": null}
        """;

    private readonly CardholderAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task SigningInStoresBackendTokensServerSideOnly()
    {
        factory.Backend.Enqueue("auth/login", HttpStatusCode.OK, TokenPairJson);
        factory.Backend.Enqueue("me", HttpStatusCode.OK, CurrentUserJson);

        using var browser = factory.CreateBrowser();
        using var response = await SignInAsync(browser);
        var setCookies = string.Join(' ', response.Headers.GetValues("Set-Cookie"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/cards", response.Headers.Location?.OriginalString);
        Assert.Equal(1, factory.Store.SessionCount);

        Assert.Contains("cardholder-session=", setCookies, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookies, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookies, StringComparison.OrdinalIgnoreCase);

        // The credentials the backend issued must never reach the browser.
        Assert.DoesNotContain(AccessTokenValue, setCookies, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshTokenValue, setCookies, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASignedInRecipientSeesCardsWithoutAnyTokenInThePage()
    {
        factory.Backend.Enqueue("auth/login", HttpStatusCode.OK, TokenPairJson);
        factory.Backend.Enqueue("me", HttpStatusCode.OK, CurrentUserJson);
        factory.Backend.Enqueue("gift-cards", HttpStatusCode.OK, EmptyCardsJson);

        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);

        using var response = await browser.GetAsync(new Uri("/cards", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(AccessTokenValue, body, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshTokenValue, body, StringComparison.Ordinal);
        Assert.Contains("do not have any gift cards yet", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CardsRequireASession()
    {
        using var browser = factory.CreateBrowser();

        using var response = await browser.GetAsync(new Uri("/cards", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signin", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SignOutCannotHappenThroughALink()
    {
        factory.Backend.Enqueue("auth/login", HttpStatusCode.OK, TokenPairJson);
        factory.Backend.Enqueue("me", HttpStatusCode.OK, CurrentUserJson);

        using var browser = factory.CreateBrowser();
        await SignInAsync(browser);

        using var response = await browser.GetAsync(new Uri("/signout", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/cards", response.Headers.Location?.OriginalString);
        Assert.Equal(1, factory.Store.SessionCount);
    }

    [Fact]
    public async Task APostWithoutAnAntiforgeryTokenIsRejected()
    {
        using var browser = factory.CreateBrowser();
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Identifier"] = "recipient@example.com",
                ["Password"] = "a long enough passphrase",
            });

        using var response = await browser.PostAsync(new Uri("/signin", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/session-expired", response.Headers.Location?.OriginalString);
        Assert.Empty(factory.Backend.Requests);
    }

    [Fact]
    public async Task FailedSignInDoesNotRevealWhetherTheAccountExists()
    {
        factory.Backend.EnqueueProblem(
            "auth/login",
            HttpStatusCode.Unauthorized,
            "identity.login.invalid");

        using var browser = factory.CreateBrowser();
        using var response = await SignInAsync(browser);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("could not sign you in with those details", body, StringComparison.Ordinal);
        Assert.Equal(0, factory.Store.SessionCount);
    }

    [Fact]
    public async Task SignInForwardsOnlyTheClientAddressObservedByTheApplication()
    {
        factory.Backend.EnqueueProblem(
            "auth/login",
            HttpStatusCode.Unauthorized,
            "identity.login.invalid");

        using var browser = factory.CreateBrowser();
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
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/signin", UriKind.Relative))
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.99");

        using var response = await browser.SendAsync(request);

        var login = Assert.Single(factory.Backend.Requests);
        var forwarded = login.Header("X-Forwarded-For");
        Assert.Equal(CardholderAppFactory.ObservedClientAddress, forwarded);
        Assert.DoesNotContain("198.51.100.99", forwarded!, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedIngressConfigurationAllowsExactlyOneForwardedHop()
    {
        var options = new ForwardedHeadersOptions();
        DeploymentSafety.ConfigureForwardedHeaders(
            options,
            [IPAddress.Parse(CardholderAppFactory.ObservedClientAddress)]);

        Assert.Equal(1, options.ForwardLimit);
        Assert.Contains(
            IPAddress.Parse(CardholderAppFactory.ObservedClientAddress),
            options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
    }

    [Fact]
    public async Task HealthSeparatesLivenessFromSessionStoreReadiness()
    {
        using var browser = factory.CreateBrowser();

        using var live = await browser.GetAsync(new Uri("/health", UriKind.Relative));
        using var ready = await browser.GetAsync(new Uri("/health/ready", UriKind.Relative));
        factory.Store.IsReady = false;
        using var unavailable = await browser.GetAsync(
            new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain(
            "connection",
            await unavailable.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponsesCarryStrictSecurityHeaders()
    {
        using var browser = factory.CreateBrowser();

        using var response = await browser.GetAsync(new Uri("/signin", UriKind.Relative));

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient browser)
    {
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
        return await browser.PostAsync(new Uri("/signin", UriKind.Relative), content);
    }
}
