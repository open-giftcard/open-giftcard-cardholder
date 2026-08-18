using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Web.Pages;

/// <summary>
/// Password sign-in for a recipient, using the email address or phone number
/// their card was delivered to.
///
/// Signing in exchanges credentials for a backend token pair that is stored
/// server-side; the browser receives only the opaque session cookie.
/// </summary>
internal sealed class SignInModel(
    CardholderSessionManager sessions,
    BackendClient backend,
    IStringLocalizer<SharedResource> text) : PageModel
{
    [BindProperty]
    public string? Identifier { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>Masked contact shown after a successful activation.</summary>
    public string? ActivatedIdentifier { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ActivatedIdentifier = TempData[ClaimCompletion.ActivatedIdentifierKey] as string;

        // Already signed in? Skip the form.
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        return session is not null ? RedirectToSafeDestination() : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var identifier = Identifier?.Trim();
        if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = text[ActivationMessages.EnterCredentials].Value;
            return Page();
        }

        try
        {
            // The backend accepts exactly one identifier. An address is the only
            // thing that can contain '@', so it is a safe discriminator; the
            // backend still normalizes and validates whichever one it receives.
            var clientAddress = ClaimCompletion.ResolveClientAddress(HttpContext);
            var tokens = identifier.Contains('@', StringComparison.Ordinal)
                ? await backend.LoginWithEmailAsync(
                    identifier,
                    Password,
                    clientAddress,
                    cancellationToken)
                : await backend.LoginWithPhoneAsync(
                    identifier,
                    Password,
                    clientAddress,
                    cancellationToken);

            var user = await backend.GetCurrentUserAsync(tokens.AccessToken, cancellationToken);
            await sessions.SignInAsync(HttpContext, tokens, user.Id, cancellationToken);
            return RedirectToSafeDestination();
        }
        catch (BackendProblemException exception)
        {
            // The backend does not disclose whether an account exists, and
            // neither does this page.
            ErrorMessage = exception.IsTooManyRequests
                ? text[ActivationMessages.TooManyAttempts].Value
                : text[ActivationMessages.SignInFailed].Value;
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
        }
    }

    private IActionResult RedirectToSafeDestination() =>
        !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? LocalRedirect(ReturnUrl)
            : RedirectToPage("/Cards");
}
