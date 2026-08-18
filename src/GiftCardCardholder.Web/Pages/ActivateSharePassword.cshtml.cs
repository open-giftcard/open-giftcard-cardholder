using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Display;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Web.Pages;

internal sealed class ActivateSharePasswordModel(
    CardholderSessionManager sessions,
    BackendClient backend,
    IStringLocalizer<SharedResource> text) : PageModel
{
    public const int MinimumPasswordLength = 12;
    public const int MaximumPasswordLength = 128;

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? ConfirmPassword { get; set; }

    public bool HasActivation { get; private set; }

    public string? ErrorMessage { get; private set; }

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
        var localError = ValidatePassword(Password, ConfirmPassword);
        if (localError is not null)
        {
            ErrorMessage = text[localError, MinimumPasswordLength, MaximumPasswordLength].Value;
            return Page();
        }

        try
        {
            var result = await backend.ClaimDirectGiftCardShareAsync(
                activation.ClaimToken,
                Password,
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
            Password = null;
            ConfirmPassword = null;
        }
    }

    private static string? ValidatePassword(string? password, string? confirmPassword)
    {
        if (string.IsNullOrEmpty(password))
        {
            return ActivationMessages.EnterPassword;
        }

        var length = password.EnumerateRunes().Count();
        if (length < MinimumPasswordLength || length > MaximumPasswordLength)
        {
            return ActivationMessages.PasswordLength;
        }

        return string.Equals(password, confirmPassword, StringComparison.Ordinal)
            ? null
            : ActivationMessages.PasswordsMustMatch;
    }
}
