namespace GiftCardCardholder.Web.Display;

/// <summary>
/// Recipient-facing card-detail messages kept out of exception handlers so
/// backend problem details and identifiers are never accidentally rendered.
/// </summary>
internal static class CardMessages
{
    public const string NotFound =
        "We could not find that gift card in your account.";

    public const string HistoryUnavailable =
        "We could not load this activity page. Return to the latest activity and try again.";

    public const string ActionUnavailable =
        "That action is no longer available. The card's latest status is shown below.";

    public const string Suspended =
        "Your gift card is suspended. Its balance and history are still available.";

    public const string Reactivated =
        "Your gift card is active again.";
}
