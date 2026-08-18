using System.Globalization;
using GiftCardCardholder.Web.Display;
using GiftCardCardholder.Web.Sessions;

namespace GiftCardCardholder.Tests;

public sealed class SessionPrimitiveTests
{
    [Fact]
    public void GeneratedCookieValuesAreUniqueAndWellShaped()
    {
        var values = Enumerable.Range(0, 200).Select(_ => OpaqueToken.Create()).ToArray();

        Assert.All(values, value => Assert.True(OpaqueToken.HasValidShape(value)));
        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void HashingIsStableAndDistinguishesValues()
    {
        var value = OpaqueToken.Create();

        Assert.Equal(OpaqueToken.Hash(value), OpaqueToken.Hash(value));
        Assert.NotEqual(OpaqueToken.Hash(value), OpaqueToken.Hash(OpaqueToken.Create()));
    }

    [Fact]
    public void HashDoesNotContainTheOriginalValue()
    {
        var value = OpaqueToken.Create();

        Assert.DoesNotContain(value, OpaqueToken.Hash(value), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("contains spaces and is definitely not base64url encoded!!")]
    public void MalformedCookieValuesAreRejected(string? value) =>
        Assert.False(OpaqueToken.HasValidShape(value));
}

public sealed class MoneyFormatterTests
{
    [Theory]
    [InlineData(0, "TRY", "0.00 TRY")]
    [InlineData(500, "TRY", "500.00 TRY")]
    [InlineData(1234.5, "TRY", "1,234.50 TRY")]
    [InlineData(0.05, "EUR", "0.05 EUR")]
    public void AmountsRenderWithTwoDecimalsAndCurrencyCode(
        decimal amount,
        string currency,
        string expected) =>
        Assert.Equal(
            expected,
            MoneyFormatter.Format(amount, currency, CultureInfo.GetCultureInfo("en-US")));

    [Fact]
    public void PrecisionIsPreservedExactly()
    {
        // Money must never round-trip through a floating-point type.
        var culture = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("0.10 TRY", MoneyFormatter.Format(0.1m, "TRY", culture));
        Assert.Equal(
            "99,999,999.99 TRY",
            MoneyFormatter.Format(99_999_999.99m, "TRY", culture));
    }

    [Fact]
    public void TurkishCultureChangesSeparatorsWithoutChangingTheAmountOrCurrency()
    {
        var result = MoneyFormatter.Format(
            1234.5m,
            "TRY",
            CultureInfo.GetCultureInfo("tr-TR"));

        Assert.Equal("1.234,50 TRY", result);
    }
}
