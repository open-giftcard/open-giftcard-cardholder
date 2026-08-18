namespace GiftCardCardholder.Web.Display;

internal static class NumericPaymentCodeDisplay
{
    public static bool TryFormat(string? code, out string formatted)
    {
        formatted = string.Empty;
        if (code is null || code.Length != 12 ||
            code.Any(character => character is < '0' or > '9'))
        {
            return false;
        }

        formatted = string.Join(' ', code[..4], code[4..8], code[8..]);
        return true;
    }
}
