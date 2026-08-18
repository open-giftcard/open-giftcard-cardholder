using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Display;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Web.Pages;

internal sealed class ShareClaimModel(
    CardholderSessionManager sessions,
    IStringLocalizer<SharedResource> text) : PageModel
{
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? token, CancellationToken cancellationToken)
    {
        if (!ClaimTokenFormat.TryParse(token, out _))
        {
            ErrorMessage = text[SharingMessages.ClaimUnusable].Value;
            return Page();
        }

        await sessions.StartActivationAsync(
            HttpContext,
            token!.Trim(),
            ActivationPurpose.ProtectedShare,
            cancellationToken);
        return RedirectToPage("/ShareClaimConfirm");
    }
}
