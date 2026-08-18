using GiftCardCardholder.Web.Backend;

namespace GiftCardCardholder.Web.Display;

internal static class SharingMessages
{
    public const string CreateUnavailable =
        "This card cannot create that share right now. Its latest values are shown below.";

    public const string CredentialsAlreadyIssued =
        "That share was already created. For safety, its one-time link and PIN cannot be shown again.";

    public const string DirectInvitationSent =
        "The invitation was created for {0}. Its value is reserved until it is claimed or closed.";

    public const string ShareCancelled =
        "The share was cancelled. Its reserved value is available on the source card again.";

    public const string CancelUnavailable =
        "That share can no longer be cancelled. Its latest status is shown below.";

    public const string ClaimUnusable =
        "This share link or PIN is not valid, has already been used, is locked, or has expired.";

    public const string ShareClaimed =
        "The shared value is now on your gift card {0}.";

    public const string EnterAmount = "Enter an amount greater than zero.";

    public const string EnterRecipient =
        "Choose email or phone and enter the recipient's contact.";

    public const string EnterPin = "Enter the six-digit PIN sent separately from the link.";

    public static string ForCreateFailure(BackendProblemException exception) =>
        exception.Code == "sharing.credentials.already_issued"
            ? CredentialsAlreadyIssued
            : CreateUnavailable;

    public static string ForClaimFailure(BackendProblemException exception) =>
        exception.IsTooManyRequests
            ? GiftCardCardholder.Web.Activation.ActivationMessages.TooManyAttempts
            : ClaimUnusable;
}
