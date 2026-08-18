using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Web.Pages;

/// <summary>
/// The recipient's own cards.
///
/// Every value shown here is read from the backend on each request. Balances
/// are Ledger-derived server-side; this page performs no arithmetic on money
/// and caches nothing, so it cannot show a stale or invented balance.
/// </summary>
internal sealed class CardsModel(
    CardholderSessionManager sessions,
    BackendClient backend,
    IStringLocalizer<SharedResource> text) : PageModel
{
    private const int PageSize = 20;

    public IReadOnlyList<OwnedGiftCard> Cards { get; private set; } = [];

    public string? ErrorMessage { get; private set; }

    public string? StatusMessage { get; private set; }

    /// <summary>
    /// Shown once, immediately after activation, so a recipient who has just
    /// chosen a password learns which contact that password belongs to.
    ///
    /// It is the exact contact rather than the masked one: the reader is
    /// authenticated as its owner by this point, so it is their own data and
    /// nothing new is disclosed. Masking would defeat the purpose, because the
    /// case that locks people out is a plus-alias whose distinguishing part is
    /// precisely what a mask hides.
    /// </summary>
    public string? SignInIdentifier { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        StatusMessage = TempData[ClaimCompletion.CardListStatusKey] as string;
        var activatedIdentifier =
            TempData[ClaimCompletion.ActivatedIdentifierKey] as string;
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            return RedirectToPage("/SignIn");
        }

        ViewData["ShowSignOut"] = true;

        try
        {
            if (activatedIdentifier is not null)
            {
                SignInIdentifier = await ResolveSignInIdentifierAsync(
                    session.AccessToken,
                    activatedIdentifier,
                    cancellationToken);
            }

            var page = await backend.GetMyGiftCardsAsync(
                session.AccessToken,
                PageSize,
                cursor: null,
                cancellationToken);
            Cards = page.Items;
            return Page();
        }
        catch (BackendProblemException exception) when (exception.IsUnauthorized)
        {
            // The token was refreshed proactively, so a 401 here means the
            // backend ended the session. Fail closed and ask for sign-in.
            await sessions.SignOutLocallyAsync(HttpContext, cancellationToken);
            return RedirectToPage("/SignIn");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = text[ActivationMessages.TemporarilyUnavailable].Value;
            return Page();
        }
    }

    /// <summary>
    /// Falls back to the masked identifier the claim already returned. Telling
    /// someone their username approximately is worth more than failing their
    /// card list because a secondary lookup did not answer.
    /// </summary>
    private async Task<string> ResolveSignInIdentifierAsync(
        string accessToken,
        string maskedIdentifier,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await backend.GetCurrentUserAsync(accessToken, cancellationToken);
            var contact = user.Email ?? user.PhoneNumber;
            return string.IsNullOrWhiteSpace(contact) ? maskedIdentifier : contact;
        }
        catch (Exception exception) when (
            exception is BackendProblemException or HttpRequestException
                or TaskCanceledException)
        {
            return maskedIdentifier;
        }
    }
}
