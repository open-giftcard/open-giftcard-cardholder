using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Display;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Web.Pages;

internal sealed class ShareCreateModel(
    CardholderSessionManager sessions,
    BackendClient backend,
    IStringLocalizer<SharedResource> text) : PageModel
{
    private const string ProtectedKeyPrefix = "cardholder-share-link-";
    private const string DirectKeyPrefix = "cardholder-share-direct-";

    public OwnedGiftCardDetail? GiftCard { get; private set; }

    public CreatedGiftCardShare? ProtectedResult { get; private set; }

    public CreatedDirectGiftCardShare? DirectResult { get; private set; }

    public string? ErrorMessage { get; private set; }

    [BindProperty]
    public decimal? ProtectedAmount { get; set; }

    [BindProperty]
    public string? ProtectedIdempotencyKey { get; set; }

    [BindProperty]
    public decimal? DirectAmount { get; set; }

    [BindProperty]
    public string? RecipientContactType { get; set; }

    [BindProperty]
    public string? RecipientContact { get; set; }

    [BindProperty]
    public string? DirectIdempotencyKey { get; set; }

    public async Task<IActionResult> OnGetAsync(
        Guid giftCardId,
        CancellationToken cancellationToken)
    {
        NewFormKeys();
        return await LoadAsync(giftCardId, cancellationToken);
    }

    public async Task<IActionResult> OnPostProtectedAsync(
        Guid giftCardId,
        CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            return RedirectToPage("/SignIn");
        }

        ViewData["ShowSignOut"] = true;
        ProtectedIdempotencyKey = ResolveKey(ProtectedIdempotencyKey, ProtectedKeyPrefix);
        if (ProtectedAmount is null or <= 0)
        {
            ErrorMessage = text[SharingMessages.EnterAmount].Value;
            DirectIdempotencyKey = ResolveKey(DirectIdempotencyKey, DirectKeyPrefix);
            return await LoadWithSessionAsync(session, giftCardId, cancellationToken);
        }

        try
        {
            ProtectedResult = await backend.CreateGiftCardShareAsync(
                session.AccessToken,
                giftCardId,
                ProtectedAmount.Value,
                ProtectedIdempotencyKey,
                cancellationToken);
        }
        catch (BackendProblemException exception) when (exception.IsUnauthorized)
        {
            await sessions.SignOutLocallyAsync(HttpContext, cancellationToken);
            return RedirectToPage("/SignIn");
        }
        catch (BackendProblemException exception)
        {
            ErrorMessage = text[SharingMessages.ForCreateFailure(exception)].Value;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = text[ActivationMessages.TemporarilyUnavailable].Value;
        }

        if (ProtectedResult is not null)
        {
            ProtectedIdempotencyKey = NewKey(ProtectedKeyPrefix);
        }

        DirectIdempotencyKey = ResolveKey(DirectIdempotencyKey, DirectKeyPrefix);
        await TryLoadCardAsync(session, giftCardId, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDirectAsync(
        Guid giftCardId,
        CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            return RedirectToPage("/SignIn");
        }

        ViewData["ShowSignOut"] = true;
        DirectIdempotencyKey = ResolveKey(DirectIdempotencyKey, DirectKeyPrefix);
        var contactType = RecipientContactType?.Trim();
        var contact = RecipientContact?.Trim();
        if (DirectAmount is null or <= 0)
        {
            ErrorMessage = text[SharingMessages.EnterAmount].Value;
        }
        else if ((contactType is not "Email" and not "Phone") || string.IsNullOrEmpty(contact))
        {
            ErrorMessage = text[SharingMessages.EnterRecipient].Value;
        }
        else
        {
            try
            {
                DirectResult = await backend.CreateDirectGiftCardShareAsync(
                    session.AccessToken,
                    giftCardId,
                    DirectAmount.Value,
                    contactType,
                    contact,
                    DirectIdempotencyKey,
                    cancellationToken);
            }
            catch (BackendProblemException exception) when (exception.IsUnauthorized)
            {
                await sessions.SignOutLocallyAsync(HttpContext, cancellationToken);
                return RedirectToPage("/SignIn");
            }
            catch (BackendProblemException exception)
            {
                ErrorMessage = text[SharingMessages.ForCreateFailure(exception)].Value;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException)
            {
                ErrorMessage = text[ActivationMessages.TemporarilyUnavailable].Value;
            }
        }

        if (DirectResult is not null)
        {
            RecipientContact = null;
            ModelState.Remove(nameof(RecipientContact));
            DirectIdempotencyKey = NewKey(DirectKeyPrefix);
        }

        ProtectedIdempotencyKey = ResolveKey(ProtectedIdempotencyKey, ProtectedKeyPrefix);
        await TryLoadCardAsync(session, giftCardId, cancellationToken);
        return Page();
    }

    private async Task<IActionResult> LoadAsync(Guid giftCardId, CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            return RedirectToPage("/SignIn");
        }

        ViewData["ShowSignOut"] = true;
        return await LoadWithSessionAsync(session, giftCardId, cancellationToken);
    }

    private async Task<IActionResult> LoadWithSessionAsync(
        CardholderSession session,
        Guid giftCardId,
        CancellationToken cancellationToken)
    {
        try
        {
            GiftCard = await backend.GetMyGiftCardAsync(
                session.AccessToken,
                giftCardId,
                cancellationToken);
            return Page();
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
            return Page();
        }
        catch (Exception exception) when (
            exception is BackendProblemException or HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = text[ActivationMessages.TemporarilyUnavailable].Value;
            return Page();
        }
    }

    private async Task TryLoadCardAsync(
        CardholderSession session,
        Guid giftCardId,
        CancellationToken cancellationToken)
    {
        try
        {
            GiftCard = await backend.GetMyGiftCardAsync(
                session.AccessToken,
                giftCardId,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is BackendProblemException or HttpRequestException or TaskCanceledException)
        {
            // Never replace a one-time successful credential response with a
            // secondary detail-read failure. The no-store result must remain visible.
        }
    }

    private void NewFormKeys()
    {
        ProtectedIdempotencyKey = NewKey(ProtectedKeyPrefix);
        DirectIdempotencyKey = NewKey(DirectKeyPrefix);
    }

    private static string NewKey(string prefix) => $"{prefix}{Guid.NewGuid():N}";

    private static string ResolveKey(string? candidate, string prefix) =>
        candidate is not null &&
        candidate.StartsWith(prefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(candidate[prefix.Length..], "N", out _)
            ? candidate
            : NewKey(prefix);
}
