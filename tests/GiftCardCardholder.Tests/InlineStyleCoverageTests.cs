using System.Text.RegularExpressions;

namespace GiftCardCardholder.Tests;

/// <summary>
/// Guards the one CSP rule that fails quietly.
///
/// The application serves <c>style-src 'self'</c>, so a browser discards every
/// <c>style</c> attribute. Nothing errors: the page renders, the declaration is
/// simply gone. The checkout countdown carried its position that way and so
/// restarted from sixty on every render while the real code expired underneath
/// it, and because reloading the page was separately broken, nobody could see
/// it happening.
///
/// A blocked script is noticed immediately. A blocked style is not, which is
/// why this is a test rather than a convention.
/// </summary>
public sealed partial class InlineStyleCoverageTests
{
    [Fact]
    public void NoViewCarriesAnInlineStyleAttribute()
    {
        var offenders = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var view in Views())
        {
            foreach (Match match in InlineStyle().Matches(File.ReadAllText(view)))
            {
                offenders.TryAdd(
                    $"{Path.GetFileName(view)}: {match.Value}",
                    view);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "style-src 'self' means the browser drops these and the page keeps "
            + "rendering, so the effect is lost with no error. Move the "
            + "declaration into app.css:\n  "
            + string.Join("\n  ", offenders.Keys));
    }

    private static IEnumerable<string> Views() =>
        Directory.EnumerateFiles(
            Path.Combine(WebProjectRoot(), "Pages"),
            "*.cshtml",
            SearchOption.AllDirectories);

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

    /// <summary>
    /// Matches a <c>style</c> attribute on an element, and deliberately not the
    /// <c>&lt;style&gt;</c> element or the word appearing inside a class name.
    /// </summary>
    [GeneratedRegex(@"\sstyle\s*=\s*""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex InlineStyle();
}
