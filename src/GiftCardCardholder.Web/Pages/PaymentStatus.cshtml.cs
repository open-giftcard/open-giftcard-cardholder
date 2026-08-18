using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Display;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GiftCardCardholder.Web.Pages;

internal sealed class PaymentStatusModel(
    CardholderSessionManager sessions,
    BackendClient backend) : PageModel
{
    public string State { get; private set; } = "Unavailable";

    public string? DisplayAmount { get; private set; }

    public bool ShouldRefresh { get; private set; }

    public async Task OnGetAsync(
        Guid giftCardId,
        Guid paymentTokenId,
        CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            State = "SessionExpired";
            return;
        }

        try
        {
            var status = await backend.GetPaymentTokenStatusAsync(
                session.AccessToken,
                giftCardId,
                paymentTokenId,
                cancellationToken);
            State = status.State switch
            {
                "Pending" or "Active" or "Confirmed" or "Cancelled" or "Expired" =>
                    status.State,
                _ => "Unavailable",
            };
            ShouldRefresh = State is "Pending" or "Active";
            var amount = State == "Confirmed"
                ? status.ConfirmedAmount
                : status.Amount;
            if (amount is not null && !string.IsNullOrWhiteSpace(status.Currency))
            {
                DisplayAmount = MoneyFormatter.Format(amount.Value, status.Currency);
            }
        }
        catch (BackendProblemException exception) when (exception.IsUnauthorized)
        {
            await sessions.SignOutLocallyAsync(HttpContext, cancellationToken);
            State = "SessionExpired";
        }
        catch (BackendProblemException exception) when (exception.IsNotFound)
        {
            // A 404 means the backend cannot find this checkout, which is not
            // the same fact as "your code expired" — only the backend's own
            // Expired state says that. Reporting expiry here told cardholders
            // holding a perfectly valid code that it had died.
            State = "Unknown";
        }
        catch (Exception exception) when (
            exception is BackendProblemException or HttpRequestException or TaskCanceledException)
        {
            State = "Unavailable";
            ShouldRefresh = true;
        }
    }
}
