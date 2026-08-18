using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GiftCardCardholder.Web.Pages;

/// <summary>
/// Ends the session. POST only, so a link or prefetch cannot sign someone out,
/// and Razor Pages' automatic antiforgery validation covers the form.
/// </summary>
internal sealed class SignOutModel(CardholderSessionManager sessions) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Cards");

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await sessions.SignOutAsync(HttpContext, cancellationToken);
        return RedirectToPage("/SignIn");
    }
}
