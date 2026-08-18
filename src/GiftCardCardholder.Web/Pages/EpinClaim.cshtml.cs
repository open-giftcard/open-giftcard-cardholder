using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Web.Pages;

internal sealed class EpinClaimModel(
    CardholderSessionManager sessions,
    BackendClient backend,
    IStringLocalizer<SharedResource> text) : PageModel
{
    public const int MinimumPasswordLength = 12;
    public const int MaximumPasswordLength = 128;

    [BindProperty]
    public string? Pin { get; set; }

    [BindProperty]
    public string ContactType { get; set; } = "Email";

    [BindProperty]
    public string? RecipientContact { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? ConfirmPassword { get; set; }

    public bool HasActivation { get; private set; }
    public bool IsSignedIn { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool SuggestSignIn { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        HasActivation = await HasActivationAsync(cancellationToken);
        IsSignedIn = await sessions.GetAsync(HttpContext, cancellationToken) is not null;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var activation = await sessions.GetActivationAsync(
            HttpContext,
            ActivationPurpose.PartnerEpin,
            cancellationToken);
        if (activation is null)
        {
            ErrorMessage = text[ActivationMessages.LinkUnusable].Value;
            return Page();
        }

        HasActivation = true;
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        IsSignedIn = session is not null;
        var localError = ValidateInput(session is not null);
        if (localError is not null)
        {
            ErrorMessage = text[localError, MinimumPasswordLength, MaximumPasswordLength].Value;
            ClearSecrets();
            return Page();
        }

        try
        {
            var result = await backend.ClaimEpinAsync(
                activation.ClaimToken,
                Pin!.Trim(),
                session is null ? ContactType : null,
                session is null ? RecipientContact?.Trim() : null,
                session is null ? Password : null,
                activation.IdempotencyKey,
                session?.AccessToken,
                ClaimCompletion.ResolveClientAddress(HttpContext),
                cancellationToken);

            if (session is not null)
            {
                await sessions.EndActivationAsync(HttpContext, cancellationToken);
                TempData[ClaimCompletion.CardListStatusKey] =
                    text["Your e-pin was added to your account."].Value;
                return RedirectToPage("/Cards");
            }

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
            SuggestSignIn = exception.Code == BackendProblemException.Codes.ClaimLoginRequired;
            ErrorMessage = SuggestSignIn
                ? text["That account already exists. Sign in first, then enter the e-pin again."].Value
                : text[ActivationMessages.ForClaimFailure(exception).Message,
                    MinimumPasswordLength,
                    MaximumPasswordLength].Value;
            return Page();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = text[ActivationMessages.TemporarilyUnavailable].Value;
            return Page();
        }
        finally
        {
            ClearSecrets();
        }
    }

    private async Task<bool> HasActivationAsync(CancellationToken cancellationToken) =>
        await sessions.GetActivationAsync(
            HttpContext,
            ActivationPurpose.PartnerEpin,
            cancellationToken) is not null;

    private string? ValidateInput(bool signedIn)
    {
        if (Pin?.Trim() is not { Length: 6 } pin || pin.Any(character => !char.IsAsciiDigit(character)))
        {
            return "Enter the six-digit PIN supplied by the reseller.";
        }

        if (signedIn)
        {
            return null;
        }

        if (ContactType is not "Email" and not "Phone" || string.IsNullOrWhiteSpace(RecipientContact))
        {
            return "Enter an email address or phone number for your new account.";
        }

        if (string.IsNullOrEmpty(Password))
        {
            return ActivationMessages.EnterPassword;
        }

        var passwordLength = Password.EnumerateRunes().Count();
        if (passwordLength < MinimumPasswordLength || passwordLength > MaximumPasswordLength)
        {
            return ActivationMessages.PasswordLength;
        }

        return string.Equals(Password, ConfirmPassword, StringComparison.Ordinal)
            ? null
            : ActivationMessages.PasswordsMustMatch;
    }

    private void ClearSecrets()
    {
        Pin = null;
        Password = null;
        ConfirmPassword = null;
    }
}
