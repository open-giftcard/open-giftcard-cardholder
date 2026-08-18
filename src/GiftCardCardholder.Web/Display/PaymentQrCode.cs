using QRCoder;

namespace GiftCardCardholder.Web.Display;

/// <summary>
/// Converts the backend-issued opaque credential into presentation bytes. It
/// never parses, shortens, persists, or assigns meaning to the credential.
/// </summary>
internal static class PaymentQrCode
{
    private const string DataUriPrefix = "data:image/png;base64,";

    public static string CreateDataUri(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        var png = PngByteQRCodeHelper.GetQRCode(
            rawToken,
            QRCodeGenerator.ECCLevel.Q,
            8);
        return DataUriPrefix + Convert.ToBase64String(png);
    }
}
