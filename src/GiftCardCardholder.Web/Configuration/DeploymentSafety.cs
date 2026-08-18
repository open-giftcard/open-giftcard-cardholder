using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace GiftCardCardholder.Web.Configuration;

internal static class DeploymentSafety
{
    private const string KnownProxiesSection =
        "Networking:ForwardedHeaders:KnownProxies";

    public static IPAddress[] ReadKnownProxies(IConfiguration configuration) =>
        configuration
            .GetSection(KnownProxiesSection)
            .GetChildren()
            .Select(item => item.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value =>
                IPAddress.TryParse(value, out var address)
                    ? address
                    : throw new InvalidOperationException(
                        $"{KnownProxiesSection} contains invalid IP address '{value}'."))
            .Distinct()
            .ToArray();

    public static void ConfigureForwardedHeaders(
        ForwardedHeadersOptions options,
        IReadOnlyCollection<IPAddress> knownProxies)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(knownProxies);
        if (knownProxies.Count == 0)
        {
            throw new InvalidOperationException(
                "Forwarded-header middleware must not be enabled without a trusted proxy.");
        }

        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var address in knownProxies)
        {
            options.KnownProxies.Add(address);
        }
    }

    public static bool IsBackendTransportAllowed(string baseUrl, bool isDevelopment) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps ||
         (isDevelopment && uri.Scheme == Uri.UriSchemeHttp));

    public static bool AreSessionCookiesAllowed(
        CardholderSessionOptions options,
        bool isDevelopment) =>
        isDevelopment ||
        (options.RequireSecureCookies &&
         options.SessionCookieName.StartsWith("__Host-", StringComparison.Ordinal) &&
         options.ActivationCookieName.StartsWith("__Host-", StringComparison.Ordinal));
}
