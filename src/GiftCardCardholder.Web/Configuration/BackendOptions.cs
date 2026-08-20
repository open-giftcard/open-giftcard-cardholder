namespace GiftCardCardholder.Web.Configuration;

/// <summary>
/// Connection settings for the authoritative Open Giftcard API.
/// </summary>
public sealed class BackendOptions
{
    public const string SectionName = "Backend";

    /// <summary>
    /// Origin of the backend API, for example <c>http://localhost:5143</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Per-request timeout for backend calls.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
