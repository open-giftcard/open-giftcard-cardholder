using GiftCardCardholder.Web.Activation;
using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Display;
using GiftCardCardholder.Web.Localization;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GiftCardCardholder.Web.Pages;

internal sealed class SharesModel(
    CardholderSessionManager sessions,
    BackendClient backend,
    IStringLocalizer<SharedResource> text) : PageModel
{
    private const int PageSize = 20;
    private const string CancelKeyPrefix = "cardholder-share-cancel-";
    private const string StatusKey = "ShareStatusMessage";
    private const string ErrorKey = "ShareErrorMessage";

    private static readonly string[] Directions = ["Sent", "Received"];
    private static readonly string[] Kinds = ["ProtectedLink", "DirectInvitation"];
    private static readonly string[] States =
        ["Pending", "Claiming", "Claimed", "Cancelled", "Expired", "Locked"];

    public IReadOnlyList<OwnedGiftCard> ShareableCards { get; private set; } = [];

    public IReadOnlyList<GiftCardShare> Shares { get; private set; } = [];

    public string? NextCursor { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? ErrorMessage { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string Direction { get; set; } = "Sent";

    [BindProperty(SupportsGet = true)]
    public string? Kind { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? State { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Cursor { get; set; }

    [BindProperty]
    public Guid ShareId { get; set; }

    [BindProperty]
    public string? IdempotencyKey { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        StatusMessage = TempData[StatusKey] as string;
        ErrorMessage = TempData[ErrorKey] as string;
        NormalizeFilters();
        return await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            return RedirectToPage("/SignIn");
        }

        NormalizeFilters();
        try
        {
            await backend.CancelGiftCardShareAsync(
                session.AccessToken,
                ShareId,
                ResolveCancelKey(IdempotencyKey),
                cancellationToken);
            TempData[StatusKey] = text[SharingMessages.ShareCancelled].Value;
        }
        catch (BackendProblemException exception) when (exception.IsUnauthorized)
        {
            await sessions.SignOutLocallyAsync(HttpContext, cancellationToken);
            return RedirectToPage("/SignIn");
        }
        catch (BackendProblemException)
        {
            TempData[ErrorKey] = text[SharingMessages.CancelUnavailable].Value;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            TempData[ErrorKey] = text[ActivationMessages.TemporarilyUnavailable].Value;
        }

        return RedirectToPage("/Shares", new { Direction, Kind, State });
    }

    public static string NewCancelKey() => $"{CancelKeyPrefix}{Guid.NewGuid():N}";

    private async Task<IActionResult> LoadAsync(CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            return RedirectToPage("/SignIn");
        }

        ViewData["ShowSignOut"] = true;

        try
        {
            var cardsPage = await backend.GetMyGiftCardsAsync(
                session.AccessToken,
                limit: 50,
                cursor: null,
                cancellationToken);
            ShareableCards = cardsPage.Items
                .Where(c => string.Equals(c.LifecycleState, "Active", StringComparison.Ordinal) && c.AvailableBalance > 0)
                .ToList();
        }
        catch
        {
            // Non-critical: cards picker optional if loading fails
        }

        try
        {
            var page = await backend.GetMyGiftCardSharesAsync(
                session.AccessToken,
                PageSize,
                Cursor,
                Kind,
                State,
                Direction,
                cancellationToken);
            Shares = page.Items;
            NextCursor = page.NextCursor;
            return Page();
        }
        catch (BackendProblemException exception) when (exception.IsUnauthorized)
        {
            await sessions.SignOutLocallyAsync(HttpContext, cancellationToken);
            return RedirectToPage("/SignIn");
        }
        catch (Exception exception) when (
            exception is BackendProblemException or HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = text[ActivationMessages.TemporarilyUnavailable].Value;
            return Page();
        }
    }

    private void NormalizeFilters()
    {
        Direction = Directions.Contains(Direction, StringComparer.Ordinal)
            ? Direction
            : "Sent";
        Kind = Kinds.Contains(Kind, StringComparer.Ordinal) ? Kind : null;
        State = States.Contains(State, StringComparer.Ordinal) ? State : null;
    }

    private static string ResolveCancelKey(string? candidate) =>
        candidate is not null &&
        candidate.StartsWith(CancelKeyPrefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(candidate[CancelKeyPrefix.Length..], "N", out _)
            ? candidate
            : $"{CancelKeyPrefix}{Guid.NewGuid():N}";
}
