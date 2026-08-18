using System.Globalization;

namespace GiftCardCardholder.Web.Display;

/// <summary>
/// Renders money as an exact decimal plus its ISO currency code.
///
/// Values stay <see cref="decimal"/> end to end — they are never converted
/// through a floating-point type. The ISO code is shown rather than a symbol
/// because the backend is explicitly multi-currency and guessing a symbol from
/// a code would eventually be wrong.
/// </summary>
internal static class MoneyFormatter
{
    public static string Format(
        decimal amount,
        string currency,
        CultureInfo? culture = null) =>
        string.Create(
            culture ?? CultureInfo.CurrentCulture,
            $"{amount:N2} {currency}");
}
