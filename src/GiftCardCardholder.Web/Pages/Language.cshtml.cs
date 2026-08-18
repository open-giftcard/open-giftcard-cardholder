using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GiftCardCardholder.Web.Pages;

/// <summary>
/// Stores one allowlisted presentation culture. It never infers locale from
/// identity, tenant, phone number, or currency.
/// </summary>
internal sealed class LanguageModel(IHostEnvironment environment) : PageModel
{
    private static readonly HashSet<string> SupportedCultures =
        new(StringComparer.Ordinal) { "en", "tr" };

    public IActionResult OnGet() => NotFound();

    public IActionResult OnPost(string? culture, string? returnUrl)
    {
        if (culture is null || !SupportedCultures.Contains(culture))
        {
            return BadRequest();
        }

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = TimeSpan.FromDays(365),
                Path = "/",
                SameSite = SameSiteMode.Lax,
                Secure = !environment.IsDevelopment(),
            });

        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToPage("/SignIn");
    }
}
