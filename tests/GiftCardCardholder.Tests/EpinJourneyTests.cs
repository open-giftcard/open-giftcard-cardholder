using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;

namespace GiftCardCardholder.Tests;

public sealed class EpinJourneyTests : IDisposable
{
    private const string ClaimPath = "gift-card-claims";
    private readonly CardholderAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task OpeningEpinMovesSecretServerSideAndGetNeverClaims()
    {
        using var browser = factory.CreateBrowser();
        var token = CreateClaimToken();

        using var opened = await browser.GetAsync(new Uri($"/epin?token={token}", UriKind.Relative));
        var body = await opened.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Redirect, opened.StatusCode);
        Assert.Equal("/epin/claim", opened.Headers.Location?.OriginalString);
        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
        Assert.DoesNotContain(token, string.Join(' ', opened.Headers.GetValues("Set-Cookie")), StringComparison.Ordinal);
        Assert.Empty(factory.Backend.Requests);

        using var form = await browser.GetAsync(opened.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Empty(factory.Backend.Requests);
    }

    [Fact]
    public async Task NewBuyerSuppliesPinAndContactAndReceivesOpaqueSessionOnly()
    {
        factory.Backend.Enqueue(ClaimPath, HttpStatusCode.OK, SuccessWithSessionJson());
        using var browser = factory.CreateBrowser();
        var token = CreateClaimToken();
        await browser.GetAsync(new Uri($"/epin?token={token}", UriKind.Relative));
        using var form = await browser.GetAsync(new Uri("/epin/claim", UriKind.Relative));
        var antiforgery = CardholderAppFactory.ExtractAntiforgeryToken(
            await form.Content.ReadAsStringAsync());

        using var response = await browser.PostAsync(
            new Uri("/epin/claim", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiforgery,
                ["Pin"] = "123456",
                ["ContactType"] = "Email",
                ["RecipientContact"] = "buyer@example.com",
                ["Password"] = "safe-passphrase-2026",
                ["ConfirmPassword"] = "safe-passphrase-2026",
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/cards", response.Headers.Location?.OriginalString);
        Assert.Equal(1, factory.Store.SessionCount);
        Assert.Equal(0, factory.Store.ActivationCount);

        var claim = Assert.Single(factory.Backend.Requests);
        Assert.Contains($"\"claimToken\":\"{token}\"", claim.Body, StringComparison.Ordinal);
        Assert.Contains("\"pin\":\"123456\"", claim.Body, StringComparison.Ordinal);
        Assert.Contains("\"contactType\":\"Email\"", claim.Body, StringComparison.Ordinal);
        Assert.Contains("\"recipientContact\":\"buyer@example.com\"", claim.Body, StringComparison.Ordinal);

        var returned = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(token, returned, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", returned, StringComparison.Ordinal);
        Assert.DoesNotContain("safe-passphrase-2026", returned, StringComparison.Ordinal);
    }

    private static string CreateClaimToken() =>
        $"{Guid.NewGuid():N}.{Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32))}";

    private static string SuccessWithSessionJson() => $$"""
        {
          "invitationId":"0195c0de-0000-7000-8000-000000000001",
          "ownerUserId":"0195c0de-0000-7000-8000-000000000002",
          "identityWasCreated":true,
          "maskedLoginIdentifier":"b***@example.com",
          "session":{
            "accessToken":"header.payload.signature",
            "accessTokenExpiresAtUtc":"{{DateTimeOffset.UtcNow.AddMinutes(15).ToString("O", CultureInfo.InvariantCulture)}}",
            "refreshToken":"opaque-refresh",
            "refreshTokenExpiresAtUtc":"{{DateTimeOffset.UtcNow.AddDays(30).ToString("O", CultureInfo.InvariantCulture)}}"
          },
          "giftCard":{
            "id":"0195c0de-0000-7000-8000-000000000003",
            "publicReference":"GC-EPIN-0001",
            "lifecycleState":"Active","fundedAmount":100.00,"currency":"TRY",
            "validFromUtc":"2026-08-18T00:00:00+00:00",
            "expiresAtUtc":"2027-08-18T00:00:00+00:00"
          },
          "claimedAtUtc":"2026-08-18T10:00:00+00:00"
        }
        """;
}
