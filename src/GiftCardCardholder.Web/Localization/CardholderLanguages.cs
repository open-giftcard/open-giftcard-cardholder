namespace GiftCardCardholder.Web.Localization;

/// <summary>
/// The ordered presentation-language catalogue.
///
/// English is deliberately first and is the deterministic fallback. Adding a
/// language means adding one entry here and its complete resource file; the
/// request pipeline, allowlist, and menu all consume this same catalogue.
/// </summary>
internal static class CardholderLanguages
{
    public const string DefaultCultureName = "en";

    public static IReadOnlyList<CardholderLanguage> All { get; } =
    [
        new(DefaultCultureName, "English"),
        new("tr", "Türkçe"),
    ];

    public static string[] CultureNames { get; } =
        All.Select(language => language.CultureName).ToArray();

    public static CardholderLanguage Default => All[0];

    public static bool TryFind(string? cultureName, out CardholderLanguage language)
    {
        language = All.FirstOrDefault(
                candidate => string.Equals(
                    candidate.CultureName,
                    cultureName,
                    StringComparison.Ordinal))
            ?? Default;
        return string.Equals(language.CultureName, cultureName, StringComparison.Ordinal);
    }

    public static CardholderLanguage FindOrDefault(string? cultureName) =>
        TryFind(cultureName, out var language) ? language : Default;
}

internal sealed record CardholderLanguage(string CultureName, string NativeName);
