using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GiftCardCardholder.Web.Pages;

/// <summary>
/// Entry point for an activation link.
///
/// This handler deliberately does not claim anything. Mail and messaging
/// clients prefetch links to build previews, and a GET that claimed would let a
/// preview consume a recipient's single-use invitation before they ever opened
/// it. Instead the secret is moved into a server-side activation context and
/// the request is redirected to a clean URL, which also gets the secret out of
/// the address bar. The claim happens only when the recipient presses a button.
/// </summary>
using Microsoft.Extensions.Localization;

internal sealed class ActivateModel(
    CardholderSessionManager sessions,
    IStringLocalizer<SharedResource> text) : PageModel
{
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? token, CancellationToken cancellationToken)
    {
        if (!ClaimTokenFormat.TryParse(token, out _))
        {
            ErrorMessage = text[ActivationMessages.LinkUnusable].Value;
            return Page();
        }

        await sessions.StartActivationAsync(
            HttpContext,
            token!.Trim(),
            ActivationPurpose.GiftCardDistribution,
            cancellationToken);
        return RedirectToPage("/ActivateConfirm");
    }
}
