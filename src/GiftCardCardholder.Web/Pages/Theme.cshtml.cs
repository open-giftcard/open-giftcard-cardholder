using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GiftCardCardholder.Web.Pages;

/// <summary>
/// Stores one presentation theme.
///
/// The application ships no JavaScript, so the choice cannot live in browser
/// storage and be applied on load. It is a cookie instead, read while rendering
/// and stamped onto the root element, which also means the correct theme is in
/// the first byte of HTML rather than a flash of the wrong one.
///
/// "system" is a real third option, not the absence of a choice: it clears the
/// stamp so the stylesheet's prefers-color-scheme query decides.
/// </summary>
internal sealed class ThemeModel(IHostEnvironment environment) : PageModel
{
    public const string CookieName = "giftcard_theme";

    private static readonly HashSet<string> Supported =
        new(StringComparer.Ordinal) { "light", "dark", "system" };

    public IActionResult OnGet() => NotFound();

    public IActionResult OnPost(string? theme, string? returnUrl)
    {
        if (theme is null || !Supported.Contains(theme))
        {
            return BadRequest();
        }

        if (string.Equals(theme, "system", StringComparison.Ordinal))
        {
            Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
        }
        else
        {
            Response.Cookies.Append(
                CookieName,
                theme,
                new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    MaxAge = TimeSpan.FromDays(365),
                    Path = "/",
                    SameSite = SameSiteMode.Lax,
                    Secure = !environment.IsDevelopment(),
                });
        }

        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToPage("/Cards");
    }
}
