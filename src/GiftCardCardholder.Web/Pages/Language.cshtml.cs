using GiftCardCardholder.Web.Localization;
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
    public IActionResult OnGet() => NotFound();

    public IActionResult OnPost(string? culture, string? returnUrl)
    {
        if (!CardholderLanguages.TryFind(culture, out var language))
        {
            return BadRequest();
        }

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(
                new RequestCulture(language.CultureName)),
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
