namespace GiftCardCardholder.Web.Configuration;

/// <summary>
/// Optional presentation enhancements for the server-rendered recipient app.
/// None of these settings may change authorization, financial behaviour, or
/// whether a journey works when JavaScript is unavailable.
/// </summary>
public sealed class CardholderUiOptions
{
    public const string SectionName = "Ui";

    /// <summary>
    /// Serves the same-origin progressive-enhancement module and permits
    /// same-origin scripts in Content Security Policy. Disabled by default.
    /// </summary>
    public bool EnableJavaScriptEnhancements { get; set; }
}
