using System.Buffers.Text;
using System.Security.Cryptography;
using GiftCardCardholder.Web.Activation;

namespace GiftCardCardholder.Tests;

public sealed class ClaimTokenFormatTests
{
    private static string ValidToken(Guid invitationId) =>
        $"{invitationId:N}.{Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32))}";

    [Fact]
    public void WellFormedTokenYieldsItsInvitationId()
    {
        var expected = Guid.NewGuid();

        Assert.True(ClaimTokenFormat.TryParse(ValidToken(expected), out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SurroundingWhitespaceIsTolerated()
    {
        // Recipients copy links out of messages, which often adds whitespace.
        var expected = Guid.NewGuid();

        Assert.True(ClaimTokenFormat.TryParse($"  {ValidToken(expected)}\r\n", out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-separator")]
    [InlineData("not-a-guid.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void MalformedTokensAreRefused(string? token)
    {
        Assert.False(ClaimTokenFormat.TryParse(token, out var invitationId));
        Assert.Equal(Guid.Empty, invitationId);
    }

    [Fact]
    public void SecretOfTheWrongLengthIsRefused()
    {
        var token = $"{Guid.NewGuid():N}.{Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16))}";

        Assert.False(ClaimTokenFormat.TryParse(token, out _));
    }

    [Fact]
    public void DottedGuidWithoutASecretIsRefused() =>
        Assert.False(ClaimTokenFormat.TryParse($"{Guid.NewGuid():N}.", out _));
}
