using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Web.Pages;

/// <summary>
/// Collects a password for a recipient who has no account yet, then claims.
///
/// The password is used for this one request and is never stored, logged, or
/// placed in TempData. The recipient's contact is not asked for and not sent:
/// the backend takes it from the invitation, so user input cannot influence who
/// ends up owning the card.
/// </summary>
internal sealed class ActivatePasswordModel(
    CardholderSessionManager sessions,
    BackendClient backend,
    IStringLocalizer<SharedResource> text) : PageModel
{
    /// <summary>Mirrors the backend policy so obvious mistakes are caught locally.</summary>
    public const int MinimumPasswordLength = 12;

    public const int MaximumPasswordLength = 128;

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? ConfirmPassword { get; set; }

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

        var localError = ValidatePassword(Password, ConfirmPassword);
        if (localError is not null)
        {
            ErrorMessage = text[
                localError,
                MinimumPasswordLength,
                MaximumPasswordLength].Value;
            return Page();
        }

        try
        {
            var result = await backend.ClaimAsync(
                activation.ClaimToken,
                Password,
                activation.IdempotencyKey,
                ClaimCompletion.ResolveClientAddress(HttpContext),
                cancellationToken);

            // A claim that created the identity returns a token pair, so the
            // recipient goes straight to their card instead of signing in again.
            var destination = await ClaimCompletion.CompleteAsync(
                HttpContext,
                result,
                sessions,
                TempData,
                cancellationToken);
            return RedirectToPage(destination);
        }
        catch (BackendProblemException exception)
        {
            var failure = ActivationMessages.ForClaimFailure(exception);
            ErrorMessage = text[
                failure.Message,
                MinimumPasswordLength,
                MaximumPasswordLength].Value;
            SuggestSignIn = failure.SuggestSignIn;
            return Page();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = text[ActivationMessages.TemporarilyUnavailable].Value;
            return Page();
        }
        finally
        {
            // Do not let the entered password survive into the rendered page.
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

        // The backend counts Unicode runes, not UTF-16 units, so emoji and
        // accented characters are measured the same way here.
        var length = password.EnumerateRunes().Count();
        if (length < MinimumPasswordLength || length > MaximumPasswordLength)
        {
            return ActivationMessages.PasswordLength;
        }

        return !string.Equals(password, confirmPassword, StringComparison.Ordinal)
            ? ActivationMessages.PasswordsMustMatch
            : null;
    }
}
