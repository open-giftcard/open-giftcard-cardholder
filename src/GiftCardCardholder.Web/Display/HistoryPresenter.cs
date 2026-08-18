using GiftCardCardholder.Web.Backend;

namespace GiftCardCardholder.Web.Display;

/// <summary>
/// Converts authoritative history classifications into presentation copy.
/// It never changes amounts, ordering, state, or financial direction.
/// </summary>
internal static class HistoryPresenter
{
    public static string Title(FinancialHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return (item.Category, item.Operation) switch
        {
            ("Ledger", "gift_card.issuance") => "Card loaded",
            ("Distribution", "Distributed") => "Card sent",
            ("Distribution", "Claimed") => "Card activated",
            ("Lifecycle", "Suspend") => "Card suspended",
            ("Lifecycle", "Reactivate") => "Card reactivated",
            ("Lifecycle", "Cancel") => "Card cancelled",
            ("Lifecycle", "Expire") => "Card expired",
            ("Ledger", _) => "Balance activity",
            ("Distribution", _) => "Delivery activity",
            ("Lifecycle", _) => "Card status updated",
            _ => "Card activity",
        };
    }

    public static string? Amount(FinancialHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Amount is not null && !string.IsNullOrWhiteSpace(item.Currency)
            ? MoneyFormatter.Format(item.Amount.Value, item.Currency)
            : null;
    }
}
