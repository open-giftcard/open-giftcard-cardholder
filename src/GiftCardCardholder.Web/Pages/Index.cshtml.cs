using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GiftCardCardholder.Web.Pages;

/// <summary>
/// Sends the recipient to their cards when a session exists, and to sign-in
/// otherwise. There is no marketing landing page — every visitor arrives either
/// from an activation link or to check a balance.
/// </summary>
internal sealed class IndexModel(CardholderSessionManager sessions) : PageModel
{
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        return session is not null
            ? RedirectToPage("/Cards")
            : RedirectToPage("/SignIn");
    }
}
