using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Display;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Web.Pages;

internal sealed class ShareClaimConfirmModel(
    CardholderSessionManager sessions,
    BackendClient backend,
    IStringLocalizer<SharedResource> text) : PageModel
{
    public bool HasActivation { get; private set; }

    public bool NeedsSignIn { get; private set; }

    public string? ErrorMessage { get; private set; }

    [BindProperty]
    public string? Pin { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var activation = await sessions.GetActivationAsync(
            HttpContext,
            ActivationPurpose.ProtectedShare,
            cancellationToken);
        if (activation is null)
        {
            ErrorMessage = text[SharingMessages.ClaimUnusable].Value;
            return Page();
        }

        HasActivation = true;
        NeedsSignIn = await sessions.GetAsync(HttpContext, cancellationToken) is null;
        if (!NeedsSignIn)
        {
            ViewData["ShowSignOut"] = true;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var activation = await sessions.GetActivationAsync(
            HttpContext,
            ActivationPurpose.ProtectedShare,
            cancellationToken);
        if (activation is null)
        {
            ErrorMessage = text[SharingMessages.ClaimUnusable].Value;
            return Page();
        }

        HasActivation = true;
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            NeedsSignIn = true;
            return Page();
        }

        ViewData["ShowSignOut"] = true;
        var pin = Pin?.Trim();
        if (pin is null || pin.Length != 6 || pin.Any(character => !char.IsAsciiDigit(character)))
        {
            ErrorMessage = text[SharingMessages.EnterPin].Value;
            ClearSubmittedPin();
            return Page();
        }

        try
        {
            var claimed = await backend.ClaimGiftCardShareAsync(
                session.AccessToken,
                activation.ClaimToken,
                pin,
                activation.IdempotencyKey,
                cancellationToken);
            await sessions.EndActivationAsync(HttpContext, cancellationToken);
            TempData["ShareStatusMessage"] = text[
                SharingMessages.ShareClaimed,
                claimed.ChildGiftCard.PublicReference].Value;
            return RedirectToPage("/Shares", new { Direction = "Received", State = "Claimed" });
        }
        catch (BackendProblemException exception) when (
            exception.IsUnauthorized &&
            exception.Code != BackendProblemException.Codes.ShareClaimInvalid)
        {
            await sessions.SignOutLocallyAsync(HttpContext, cancellationToken);
            NeedsSignIn = true;
            return Page();
        }
        catch (BackendProblemException exception)
        {
            ErrorMessage = text[SharingMessages.ForClaimFailure(exception)].Value;
            return Page();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = text[ActivationMessages.TemporarilyUnavailable].Value;
            return Page();
        }
        finally
        {
            ClearSubmittedPin();
        }
    }

    /// <summary>
    /// Drops the submitted PIN from the model <em>and</em> from ModelState.
    ///
    /// Clearing the property alone is not enough: the input tag helper renders
    /// its value from ModelState in preference to the model, so a refused PIN
    /// would reappear in the page source — and from there in browser history
    /// and any proxy that logs response bodies. The PIN is a secret the sender
    /// delivered out of band, so it must not survive a failed attempt.
    /// </summary>
    private void ClearSubmittedPin()
    {
        Pin = null;
        ModelState.Remove(nameof(Pin));
    }
}
