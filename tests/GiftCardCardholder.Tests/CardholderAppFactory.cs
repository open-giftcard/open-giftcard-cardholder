using System.Net;
using System.Text.RegularExpressions;
using GiftCardCardholder.Tests.Fakes;
using GiftCardCardholder.Web;
using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GiftCardCardholder.Tests;

/// <summary>
/// Boots the real application with PostgreSQL and the backend replaced. Every
/// page, cookie, antiforgery, and security-header behaviour under test is the
/// production one.
/// </summary>
internal sealed partial class CardholderAppFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Data Protection needs its key path at registration time, before the host
    /// applies test configuration, so it comes from an environment variable. One
    /// shared directory per test run keeps parallel test classes from racing.
    /// </summary>
    private static readonly string KeyPath = InitializeKeyPath();

    /// <summary>
    /// The address TestServer will report as the browser's. A TEST-NET-3
    /// documentation address, so it is unmistakably not a real client.
    /// </summary>
    public const string ObservedClientAddress = "203.0.113.7";

    public StubBackendHandler Backend { get; } = new();

    public InMemoryCardholderSessionStore Store { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Never connected: the store is replaced below.
                ["ConnectionStrings:Cardholder"] =
                    "Host=localhost;Database=cardholder_unused;Username=unused",
                ["Backend:BaseUrl"] = "http://backend.invalid",
                ["CardholderSession:RequireSecureCookies"] = "false",
                ["CardholderSession:SessionCookieName"] = "cardholder-session",
                ["CardholderSession:ActivationCookieName"] = "cardholder-activation",
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICardholderSessionStore>();
            services.AddSingleton<ICardholderSessionStore>(Store);
            services.AddSingleton<IStartupFilter>(
                new ClientAddressStartupFilter(IPAddress.Parse(ObservedClientAddress)));
            services.AddHttpClient<BackendClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Backend);
        });
    }

    private static string InitializeKeyPath()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "cardholder-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        Environment.SetEnvironmentVariable("DataProtection__KeysPath", path);
        return path;
    }

    public HttpClient CreateBrowser() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    /// <summary>
    /// Pulls the antiforgery token out of a rendered form. Every POST in these
    /// tests goes through it, which means antiforgery protection itself is
    /// continuously exercised rather than bypassed.
    /// </summary>
    public static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenPattern().Match(html);
        Assert.True(match.Success, "The page did not render an antiforgery token.");
        return match.Groups[1].Value;
    }

    [GeneratedRegex(
        """name="__RequestVerificationToken"[^>]*value="([^"]+)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenPattern();
}
