using GiftCardCardholder.Web.Configuration;
using Microsoft.Extensions.Configuration;

namespace GiftCardCardholder.Tests;

public sealed class DeploymentSafetyTests
{
    [Fact]
    public void ProductionRequiresHttpsBackendTransport()
    {
        Assert.False(
            DeploymentSafety.IsBackendTransportAllowed(
                "http://backend.example",
                isDevelopment: false));
        Assert.True(
            DeploymentSafety.IsBackendTransportAllowed(
                "https://backend.example",
                isDevelopment: false));
        Assert.True(
            DeploymentSafety.IsBackendTransportAllowed(
                "http://127.0.0.1:5143",
                isDevelopment: true));
    }

    [Fact]
    public void ProductionRequiresSecureHostOnlyCookies()
    {
        Assert.False(
            DeploymentSafety.AreSessionCookiesAllowed(
                new CardholderSessionOptions
                {
                    SessionCookieName = "cardholder-session",
                    ActivationCookieName = "cardholder-activation",
                    RequireSecureCookies = false,
                },
                isDevelopment: false));
        Assert.True(
            DeploymentSafety.AreSessionCookiesAllowed(
                new CardholderSessionOptions(),
                isDevelopment: false));
    }

    [Fact]
    public void InvalidTrustedProxyFailsClosed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Networking:ForwardedHeaders:KnownProxies:0"] = "proxy.example",
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            DeploymentSafety.ReadKnownProxies(configuration));
    }
}
