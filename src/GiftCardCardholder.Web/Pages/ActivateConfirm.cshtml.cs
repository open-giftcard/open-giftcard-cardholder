using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Web.Pages;

/// <summary>
/// Confirms activation, then performs the claim probe.
///
/// The backend requires a password only when the delivered contact has no
/// identity yet, and a client cannot know which case it is in. So the first
/// claim is sent without a password: a <c>user.password.required</c> refusal
/// identifies a new recipient, and success means an existing account claimed
/// the card. The probe is safe to send — a missing password is refused before
/// any state changes, and only a wrong <em>secret</em> counts against the
/// invitation's attempt limit.
/// </summary>
internal sealed class ActivateConfirmModel(
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
            ActivationPurpose.GiftCardDistribution,
            cancellationToken);
        if (activation is null)
        {
            ErrorMessage = text[ActivationMessages.LinkUnusable].Value;
            return Page();
        }

        HasActivation = true;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var activation = await sessions.GetActivationAsync(
            HttpContext,
            ActivationPurpose.GiftCardDistribution,
            cancellationToken);
        if (activation is null)
        {
            ErrorMessage = text[ActivationMessages.LinkUnusable].Value;
            return Page();
        }

        HasActivation = true;

        try
        {
            var result = await backend.ClaimAsync(
                activation.ClaimToken,
                password: null,
                activation.IdempotencyKey,
                ClaimCompletion.ResolveClientAddress(HttpContext),
                cancellationToken);

            // No password was needed, so the delivered contact already had an
            // account and the card is now theirs. That path returns no session
            // by design, so this normally redirects to sign-in.
            var destination = await ClaimCompletion.CompleteAsync(
                HttpContext,
                result,
                sessions,
                TempData,
                cancellationToken);
            return RedirectToPage(destination);
        }
        catch (BackendProblemException exception)
            when (exception.Code == BackendProblemException.Codes.PasswordRequired)
        {
            return RedirectToPage("/ActivatePassword");
        }
        catch (BackendProblemException exception)
        {
            var failure = ActivationMessages.ForClaimFailure(exception);
            ErrorMessage = text[
                failure.Message,
                ActivatePasswordModel.MinimumPasswordLength,
                ActivatePasswordModel.MaximumPasswordLength].Value;
            SuggestSignIn = failure.SuggestSignIn;
            return Page();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = text[ActivationMessages.TemporarilyUnavailable].Value;
            return Page();
        }
    }
}
