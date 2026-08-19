using System.Globalization;
using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Display;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Web.Pages;

/// <summary>
/// Issuing a checkout code is a POST, but the code itself must be readable by a
/// GET for as long as it lives. It was previously only the body of the POST
/// response, so every ordinary navigation destroyed it: changing language or
/// theme, reloading, or pressing back all dropped the cardholder back onto
/// "generate a code" while their real code was still valid and, in the language
/// case, made the page impossible to ever see translated.
///
/// Issuance therefore stores the credential for its own sixty seconds and
/// redirects to a GET that renders it.
/// </summary>
internal sealed class PaymentModel(
    CardholderSessionManager sessions,
    ICardholderSessionStore store,
    BackendTokenProtector protector,
    BackendClient backend,
    TimeProvider clock,
    IStringLocalizer<SharedResource> text) : PageModel
{
    public IssuedPaymentToken? Credential { get; private set; }

    public string? QrCodeDataUri { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? FormattedNumericCode { get; private set; }

    /// <summary>
    /// The stylesheet class carrying how far into its sixty seconds the code
    /// already is.
    ///
    /// This cannot be an inline <c>animation-delay</c>: the application serves
    /// <c>style-src 'self'</c>, so the browser discards style attributes, and
    /// the offset that used to be written inline never reached the animation.
    /// Whole seconds are enough granularity for a sixty-second timer and let
    /// the stylesheet stay static.
    /// </summary>
    public string CountdownOffsetClass =>
        "countdown--o" + ((int)Math.Round(ElapsedSeconds))
            .ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// How long the credential has already been alive, in seconds.
    ///
    /// The ring and the seconds counter are both fixed 60-second CSS
    /// animations, so they would restart from full on every render. Offsetting
    /// them by the code's real age makes them show the time that is actually
    /// left rather than the time since the page was drawn.
    /// </summary>
    public double ElapsedSeconds =>
        Credential is null
            ? 0
            : Math.Clamp(
                (clock.GetUtcNow() - Credential.ExpiresAtUtc.AddSeconds(-60)).TotalSeconds,
                0,
                60);

    public async Task<IActionResult> OnGetAsync(
        Guid giftCardId,
        Guid? paymentTokenId,
        CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            return RedirectToPage("/SignIn");
        }

        ViewData["ShowSignOut"] = true;
        if (paymentTokenId is null)
        {
            return Page();
        }

        var stored = await store.FindPaymentCredentialAsync(
            paymentTokenId.Value,
            session.Id,
            giftCardId,
            clock.GetUtcNow(),
            cancellationToken);

        // An expired or unknown identifier is not an error worth explaining:
        // the code is simply gone, and the honest page to show is the one
        // offering a new one. Redirect so the dead identifier leaves the URL
        // and a reload cannot resurrect this branch.
        if (stored is null ||
            !protector.TryUnprotect(stored.ProtectedRawToken, out var rawToken) ||
            !protector.TryUnprotect(stored.ProtectedNumericCode, out var numericCode) ||
            !NumericPaymentCodeDisplay.TryFormat(numericCode, out var formattedCode))
        {
            return RedirectToPage("/Payment", new { giftCardId });
        }

        Credential = new IssuedPaymentToken(
            stored.PaymentTokenId,
            stored.GiftCardId,
            stored.GiftCardPublicReference,
            rawToken,
            numericCode,
            stored.IssuedAtUtc,
            stored.ExpiresAtUtc);
        FormattedNumericCode = formattedCode;
        QrCodeDataUri = PaymentQrCode.CreateDataUri(rawToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        Guid giftCardId,
        CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            return RedirectToPage("/SignIn");
        }

        ViewData["ShowSignOut"] = true;
        try
        {
            var credential = await backend.IssuePaymentTokenAsync(
                session.AccessToken,
                giftCardId,
                cancellationToken);
            if (!NumericPaymentCodeDisplay.TryFormat(credential.NumericCode, out _))
            {
                ErrorMessage = text[ActivationMessages.TemporarilyUnavailable].Value;
                return Page();
            }

            await store.CreatePaymentCredentialAsync(
                new StoredPaymentCredential(
                    credential.Id,
                    session.Id,
                    credential.GiftCardId,
                    credential.GiftCardPublicReference,
                    protector.Protect(credential.RawToken),
                    protector.Protect(credential.NumericCode),
                    credential.IssuedAtUtc,
                    credential.ExpiresAtUtc),
                clock.GetUtcNow(),
                cancellationToken);

            return RedirectToPage(
                "/Payment",
                new { giftCardId, paymentTokenId = credential.Id });
        }
        catch (BackendProblemException exception) when (exception.IsUnauthorized)
        {
            await sessions.SignOutLocallyAsync(HttpContext, cancellationToken);
            return RedirectToPage("/SignIn");
        }
        catch (BackendProblemException exception) when (exception.IsNotFound)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            ErrorMessage = text[CardMessages.NotFound].Value;
        }
        catch (BackendProblemException exception) when (exception.IsServerError)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ErrorMessage = text[PaymentMessages.ServerError].Value;
        }
        catch (BackendProblemException)
        {
            ErrorMessage = text[PaymentMessages.Unavailable].Value;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = text[ActivationMessages.TemporarilyUnavailable].Value;
        }

        return Page();
    }
}
