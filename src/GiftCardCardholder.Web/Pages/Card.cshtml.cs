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
/// Exact-owner card detail, combined authoritative history, and the two
/// lifecycle actions the backend permits a cardholder to request.
/// </summary>
internal sealed class CardModel(
    CardholderSessionManager sessions,
    BackendClient backend,
    IStringLocalizer<SharedResource> text) : PageModel
{
    private const int HistoryPageSize = 10;
    private const string LifecycleKeyPrefix = "cardholder-lifecycle-";
    private const string StatusMessageKey = "CardStatusMessage";
    private const string ActionErrorKey = "CardActionError";

    public OwnedGiftCardDetail? GiftCard { get; private set; }

    public IReadOnlyList<FinancialHistoryItem> History { get; private set; } = [];

    public string? NextCursor { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? HistoryErrorMessage { get; private set; }

    [BindProperty]
    public string? IdempotencyKey { get; set; }

    public bool CanSuspend =>
        string.Equals(GiftCard?.LifecycleState, "Active", StringComparison.Ordinal);

    public bool CanReactivate =>
        string.Equals(GiftCard?.LifecycleState, "Suspended", StringComparison.Ordinal);

    public async Task<IActionResult> OnGetAsync(
        Guid giftCardId,
        string? cursor,
        CancellationToken cancellationToken)
    {
        StatusMessage = TempData[StatusMessageKey] as string;
        ErrorMessage = TempData[ActionErrorKey] as string;
        return await LoadAsync(giftCardId, cursor, cancellationToken);
    }

    public Task<IActionResult> OnPostSuspendAsync(
        Guid giftCardId,
        CancellationToken cancellationToken) =>
        ExecuteLifecycleAsync(giftCardId, suspend: true, cancellationToken);

    public Task<IActionResult> OnPostReactivateAsync(
        Guid giftCardId,
        CancellationToken cancellationToken) =>
        ExecuteLifecycleAsync(giftCardId, suspend: false, cancellationToken);

    private async Task<IActionResult> LoadAsync(
        Guid giftCardId,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            return RedirectToPage("/SignIn");
        }

        ViewData["ShowSignOut"] = true;
        IdempotencyKey = NewLifecycleKey();

        try
        {
            GiftCard = await backend.GetMyGiftCardAsync(
                session.AccessToken,
                giftCardId,
                cancellationToken);
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

        try
        {
            var history = await backend.GetMyGiftCardHistoryAsync(
                session.AccessToken,
                giftCardId,
                HistoryPageSize,
                cursor,
                cancellationToken);
            History = history.Items;
            NextCursor = history.NextCursor;
        }
        catch (BackendProblemException exception) when (exception.IsUnauthorized)
        {
            await sessions.SignOutLocallyAsync(HttpContext, cancellationToken);
            return RedirectToPage("/SignIn");
        }
        catch (Exception exception) when (
            exception is BackendProblemException or HttpRequestException or TaskCanceledException)
        {
            HistoryErrorMessage = text[CardMessages.HistoryUnavailable].Value;
        }

        return Page();
    }

    private async Task<IActionResult> ExecuteLifecycleAsync(
        Guid giftCardId,
        bool suspend,
        CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            return RedirectToPage("/SignIn");
        }

        var idempotencyKey = ResolveLifecycleKey(IdempotencyKey);
        try
        {
            if (suspend)
            {
                await backend.SuspendMyGiftCardAsync(
                    session.AccessToken,
                    giftCardId,
                    idempotencyKey,
                    cancellationToken);
                TempData[StatusMessageKey] = text[CardMessages.Suspended].Value;
            }
            else
            {
                await backend.ReactivateMyGiftCardAsync(
                    session.AccessToken,
                    giftCardId,
                    idempotencyKey,
                    cancellationToken);
                TempData[StatusMessageKey] = text[CardMessages.Reactivated].Value;
            }
        }
        catch (BackendProblemException exception) when (exception.IsUnauthorized)
        {
            await sessions.SignOutLocallyAsync(HttpContext, cancellationToken);
            return RedirectToPage("/SignIn");
        }
        catch (BackendProblemException exception) when (exception.IsNotFound)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            ViewData["ShowSignOut"] = true;
            ErrorMessage = text[CardMessages.NotFound].Value;
            IdempotencyKey = NewLifecycleKey();
            return Page();
        }
        catch (BackendProblemException exception) when (exception.IsConflict)
        {
            TempData[ActionErrorKey] = text[CardMessages.ActionUnavailable].Value;
        }
        catch (Exception exception) when (
            exception is BackendProblemException or HttpRequestException or TaskCanceledException)
        {
            TempData[ActionErrorKey] = text[ActivationMessages.TemporarilyUnavailable].Value;
        }

        return RedirectToPage("/Card", new { giftCardId });
    }

    private static string NewLifecycleKey() =>
        $"{LifecycleKeyPrefix}{Guid.NewGuid():N}";

    private static string ResolveLifecycleKey(string? candidate)
    {
        if (candidate is not null &&
            candidate.StartsWith(LifecycleKeyPrefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(candidate[LifecycleKeyPrefix.Length..], "N", out _))
        {
            return candidate;
        }

        return NewLifecycleKey();
    }
}
