using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Web.Pages;

/// <summary>
/// Moves the e-pin claim secret out of the address bar without consuming it.
/// Link-preview GETs therefore remain harmless.
/// </summary>
internal sealed class EpinModel(
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
            ActivationPurpose.PartnerEpin,
            cancellationToken);
        return RedirectToPage("/EpinClaim");
    }
}
