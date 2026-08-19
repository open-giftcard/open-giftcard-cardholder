namespace GiftCardCardholder.Web.Display;

internal static class PaymentMessages
{
    /// <summary>
    /// The card itself is why this failed, so pointing the recipient at its
    /// status is useful. Used for refusals the backend states deliberately.
    /// </summary>
    public const string Unavailable =
        "We could not create a payment code. Check the card's status and try again.";

    /// <summary>
    /// The server failed, and the card is very likely fine. Telling the
    /// recipient to check their card would send them looking for a fault they
    /// do not have, at a till, with a queue behind them.
    /// </summary>
    public const string ServerError =
        "Something went wrong on our side, so no payment code was created. " +
        "Your card has not been charged. Try again in a moment.";
}
