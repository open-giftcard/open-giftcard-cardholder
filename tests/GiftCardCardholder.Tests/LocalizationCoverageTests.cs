using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GiftCardCardholder.Tests;

/// <summary>
/// Guards the localisation boundary.
///
/// English is not stored anywhere: the neutral resource set is deliberately
/// almost empty and <c>Text["Sign in"]</c> falls back to its own key. That is a
/// good arrangement, but it has one failure mode, and the application has hit
/// it repeatedly: a new string ships, nobody adds the Turkish, and the page
/// silently renders English inside an otherwise Turkish interface. Nothing
/// fails, so nobody notices until a reader does.
///
/// These tests scan the views themselves rather than trusting a checklist.
/// </summary>
public sealed class LocalizationCoverageTests
{
    /// <summary>
    /// Values that reach <c>Text[...]</c> as a runtime expression rather than a
    /// literal, so no scan can discover them. They are backend contract values;
    /// a change here means the backend added a state and the client has not
    /// caught up.
    /// </summary>
    private static readonly string[] DynamicKeys =
    [
        // Gift card lifecycle
        "Active", "Suspended", "Cancelled", "Expired", "AwaitingClaim",
        // Share state
        "Pending", "Claiming", "Claimed", "Locked",
        // Share kind
        "ProtectedLink", "DirectInvitation",
    ];

    private static readonly Regex TextLiteral =
        new(@"Text\[\s*""((?:[^""\\]|\\.)*)""\s*\]", RegexOptions.Compiled);

    [Fact]
    public void EveryLiteralUsedInAViewHasATurkishTranslation()
    {
        var translated = TurkishKeys();
        var missing = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var view in Views())
        {
            foreach (Match match in TextLiteral.Matches(File.ReadAllText(view)))
            {
                var key = match.Groups[1].Value;
                if (!translated.Contains(key))
                {
                    missing.TryAdd(key, Path.GetFileName(view));
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "These strings would render in English inside the Turkish interface. "
            + "Add them to SharedResource.tr.resx:\n"
            + string.Join('\n', missing.Select(m => $"  \"{m.Key}\"  ({m.Value})")));
    }

    [Fact]
    public void EveryBackendStateReachingTheViewsHasATurkishTranslation()
    {
        var translated = TurkishKeys();
        var missing = DynamicKeys.Where(k => !translated.Contains(k)).ToArray();

        Assert.True(
            missing.Length == 0,
            "These backend values are used as translation keys and would render "
            + "raw:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// A key whose own text is not presentable English needs a neutral entry as
    /// well, because English resolves by falling back to the key. Backend enum
    /// names are the case that matters: "AwaitingClaim" is a fine identifier and
    /// a poor thing to show a cardholder.
    /// </summary>
    [Fact]
    public void BackendStatesThatAreNotPresentableEnglishHaveANeutralEntry()
    {
        var neutral = KeysIn(ResourcePath("SharedResource.resx"));

        var identifierLike = DynamicKeys
            .Where(k => Regex.IsMatch(k, "^[A-Z][a-z]+(?:[A-Z][a-z]+)+$"))
            .Where(k => !neutral.Contains(k))
            .ToArray();

        Assert.True(
            identifierLike.Length == 0,
            "These would show a cardholder a raw enum name in English. Add a "
            + "neutral entry:\n  " + string.Join("\n  ", identifierLike));
    }

    private static HashSet<string> TurkishKeys() =>
        KeysIn(ResourcePath("SharedResource.tr.resx"));

    private static HashSet<string> KeysIn(string path) =>
        XDocument.Load(path)
            .Root!
            .Elements("data")
            .Select(d => (string?)d.Attribute("name"))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

    private static string ResourcePath(string fileName) =>
        Path.Combine(
            WebProjectRoot(), "Resources", "Localization", fileName);

    private static IEnumerable<string> Views() =>
        Directory.EnumerateFiles(
            Path.Combine(WebProjectRoot(), "Pages"),
            "*.cshtml",
            SearchOption.AllDirectories);

    /// <summary>
    /// Walks up from the test binaries to the repository, which is the only way
    /// to reach the views: Razor pages are compiled into the web assembly and
    /// their source is not copied next to the tests.
    /// </summary>
    private static string WebProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "src", "GiftCardCardholder.Web");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/GiftCardCardholder.Web above "
            + AppContext.BaseDirectory);
    }
}
