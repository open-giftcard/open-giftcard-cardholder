using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Display;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Web.Pages;

internal sealed class ActivateShareConfirmModel(
    CardholderSessionManager sessions,
    BackendClient backend,
    IStringLocalizer<SharedResource> text) : PageModel
{
    public bool HasActivation { get; private set; }

    public string? ErrorMessage { get; private set; }

    public bool SuggestSignIn { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var activation = await sessions.GetActivationAsync(
            HttpContext,
            ActivationPurpose.DirectShare,
            cancellationToken);
        if (activation is null)
        {
            ErrorMessage = text[SharingMessages.ClaimUnusable].Value;
            return Page();
        }

        HasActivation = true;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var activation = await sessions.GetActivationAsync(
            HttpContext,
            ActivationPurpose.DirectShare,
            cancellationToken);
        if (activation is null)
        {
            ErrorMessage = text[SharingMessages.ClaimUnusable].Value;
            return Page();
        }

        HasActivation = true;
        try
        {
            var result = await backend.ClaimDirectGiftCardShareAsync(
                activation.ClaimToken,
                password: null,
                activation.IdempotencyKey,
                ClaimCompletion.ResolveClientAddress(HttpContext),
                cancellationToken);
            var message = text[
                SharingMessages.ShareClaimed,
                result.ChildGiftCard.PublicReference].Value;
            var destination = await ClaimCompletion.CompleteDirectShareAsync(
                HttpContext,
                result,
                sessions,
                TempData,
                message,
                cancellationToken);
            return RedirectToPage(destination);
        }
        catch (BackendProblemException exception)
            when (exception.Code == BackendProblemException.Codes.PasswordRequired)
        {
            return RedirectToPage("/ActivateSharePassword");
        }
        catch (BackendProblemException exception)
        {
            ErrorMessage = text[SharingMessages.ForClaimFailure(exception)].Value;
            SuggestSignIn = exception.Code == BackendProblemException.Codes.ShareClaimAlreadyCompleted;
            return Page();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = text[ActivationMessages.TemporarilyUnavailable].Value;
            return Page();
        }
    }
}
