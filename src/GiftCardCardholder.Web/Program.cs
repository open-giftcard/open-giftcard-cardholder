using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Configuration;
using GiftCardCardholder.Web.Security;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GiftCardCardholder.Web;

/// <summary>
/// Composition root for the recipient application.
///
/// The application is server-rendered on purpose: the browser receives HTML and
/// an opaque session cookie and nothing else. There is no client-side token
/// handling to get wrong, and every page works with JavaScript unavailable.
/// </summary>
public partial class Program
{
    private static readonly string[] SupportedCultures = ["en", "tr"];

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var isDevelopment = builder.Environment.IsDevelopment();
        var knownProxyAddresses = DeploymentSafety.ReadKnownProxies(builder.Configuration);

        // The Windows default provider set includes Event Log. An unprivileged
        // native-development process may not write there, and a logging failure
        // must never replace an otherwise handled backend response.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        builder.Services
            .AddOptions<BackendOptions>()
            .Bind(builder.Configuration.GetSection(BackendOptions.SectionName))
            .Validate(
                options => DeploymentSafety.IsBackendTransportAllowed(
                    options.BaseUrl,
                    isDevelopment),
                "Backend:BaseUrl must be HTTPS outside Development; Development " +
                "may use an absolute HTTP URL.")
            .Validate(
                static options => options.TimeoutSeconds is > 0 and <= 120,
                "Backend:TimeoutSeconds must be between 1 and 120.")
            .ValidateOnStart();

        builder.Services
            .AddOptions<CardholderSessionOptions>()
            .Bind(builder.Configuration.GetSection(CardholderSessionOptions.SectionName))
            .Validate(
                static options => options.ActivationLifetimeMinutes is >= 5 and <= 240,
                "CardholderSession:ActivationLifetimeMinutes must be between 5 and 240.")
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.SessionCookieName) &&
                    !string.IsNullOrWhiteSpace(options.ActivationCookieName),
                "CardholderSession cookie names are required.")
            .Validate(
                options => DeploymentSafety.AreSessionCookiesAllowed(options, isDevelopment),
                "Outside Development both cardholder cookies must be secure __Host- cookies.")
            .ValidateOnStart();

        ConfigureDataProtection(builder);

        builder.Services.AddSingleton(TimeProvider.System);

        // Resolved from configuration lazily rather than read here: the host
        // adds further configuration sources during Build, and reading before
        // then would silently ignore them.
        builder.Services.AddSingleton(provider =>
        {
            var connectionString = provider
                .GetRequiredService<IConfiguration>()
                .GetConnectionString("Cardholder");
            return string.IsNullOrWhiteSpace(connectionString)
                ? throw new InvalidOperationException(
                    "ConnectionStrings:Cardholder is required. It must point at the " +
                    "cardholder-owned session database, never at the backend database.")
                : NpgsqlDataSource.Create(connectionString);
        });
        builder.Services.AddSingleton<ICardholderSessionStore, PostgreSqlCardholderSessionStore>();
        builder.Services.AddSingleton<BackendTokenProtector>();
        builder.Services.AddSingleton<SessionRefreshCoordinator>();
        builder.Services.AddScoped<CardholderSessionManager>();
        var skipSessionMaintenance =
            builder.Environment.IsDevelopment() &&
            builder.Configuration.GetValue<bool>("BrowserChecks:SkipSessionStoreMaintenance");
        if (!skipSessionMaintenance)
        {
            builder.Services.AddHostedService<SessionStoreMaintenanceService>();
        }

        builder.Services
            .AddHttpClient<BackendClient>(static (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<BackendOptions>>().Value;
                client.BaseAddress = new Uri($"{options.BaseUrl.TrimEnd('/')}/api/v1/");
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });

        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
        builder.Services
            .AddRazorPages()
            .AddMvcOptions(options =>
                options.Filters.Add<AntiforgeryFailureResultFilter>())
            .AddViewLocalization();
        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture("en");
            options.AddSupportedCultures(SupportedCultures);
            options.AddSupportedUICultures(SupportedCultures);

            // Locale is an explicit user choice. Ignore Accept-Language and
            // query strings so an unsupported or shared-device browser locale
            // cannot silently change the recipient journey.
            options.RequestCultureProviders = [new CookieRequestCultureProvider()];
        });

        if (knownProxyAddresses.Length > 0)
        {
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
                DeploymentSafety.ConfigureForwardedHeaders(options, knownProxyAddresses));
        }

        var app = builder.Build();

        if (knownProxyAddresses.Length > 0)
        {
            app.UseForwardedHeaders();
        }

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseStaticFiles();
        app.UseRequestLocalization();
        app.UseRouting();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/health/ready", async (
            ICardholderSessionStore store,
            CancellationToken cancellationToken) =>
                await store.IsReadyAsync(cancellationToken)
                    ? Results.Ok(new { status = "ready" })
                    : Results.Json(
                        new { status = "unavailable" },
                        statusCode: StatusCodes.Status503ServiceUnavailable));
        app.MapRazorPages();

        app.Run();
    }

    /// <summary>
    /// Session tokens are encrypted with Data Protection, so the keys must
    /// outlive a restart. Development keeps them under <c>.local</c>; every
    /// other environment must say where they live, because silently falling
    /// back to ephemeral keys would sign every recipient out on each deploy.
    /// </summary>
    private static void ConfigureDataProtection(WebApplicationBuilder builder)
    {
        var keysPath = builder.Configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath))
        {
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "DataProtection:KeysPath is required outside Development so session " +
                    "keys survive a restart and are shared across instances.");
            }

            keysPath = Path.Combine(builder.Environment.ContentRootPath, ".local", "keys");
        }

        Directory.CreateDirectory(keysPath);
        builder.Services
            .AddDataProtection()
            .SetApplicationName("GiftCardCardholder")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
    }
}
