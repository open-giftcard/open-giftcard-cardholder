using GiftCardCardholder.Web.Backend;

namespace GiftCardCardholder.Web.Activation;

/// <summary>
/// Turns backend problem codes into recipient-facing copy.
///
/// Two rules govern every message here. First, nothing reveals whether an
/// invitation exists — the backend deliberately returns one code for a
/// malformed token, an unknown invitation, and a wrong secret, and the wording
/// preserves that. Second, no message contains a backend identifier, a
/// contact value, or a raw detail string.
/// </summary>
internal static class ActivationMessages
{
    public const string LinkUnusable =
        "This activation link is not valid, has already been used, or has expired. " +
        "Please ask the company that sent your gift card for a new link.";

    public const string AlreadyActivated =
        "This gift card has already been activated. Sign in to see it.";

    public const string IdentityDisabled =
        "This account is not available. Please contact support.";

    public const string TooManyAttempts =
        "Too many attempts. Please wait a minute and try again.";

    public const string TemporarilyUnavailable =
        "We could not reach the gift card service. Please try again in a moment.";

    public const string SignInFailed =
        "We could not sign you in with those details. Check them and try again.";

    public const string TryAgain =
        "That took longer than expected. Please try again.";

    public const string PasswordLength =
        "Your password must be between {0} and {1} characters.";

    public const string PasswordCommon =
        "Please choose a less common password or passphrase.";

    public const string EnterPassword = "Enter a password.";

    public const string PasswordsMustMatch = "Both passwords must match.";

    public const string EnterCredentials =
        "Enter your email address or phone number and your password.";

    /// <summary>
    /// Maps a claim failure to copy, and says whether the recipient should be
    /// pointed at sign-in rather than asked to retry activation.
    /// </summary>
    public static (string Message, bool SuggestSignIn) ForClaimFailure(
        BackendProblemException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.Code switch
        {
            BackendProblemException.Codes.ClaimAlreadyCompleted =>
                (AlreadyActivated, true),
            BackendProblemException.Codes.RecipientIdentityDisabled =>
                (IdentityDisabled, false),
            BackendProblemException.Codes.ClaimConcurrentConflict or
            BackendProblemException.Codes.RecipientIdentityConflict =>
                (TryAgain, false),
            BackendProblemException.Codes.PasswordInvalidLength =>
                (PasswordLength, false),
            BackendProblemException.Codes.PasswordCommon =>
                (PasswordCommon, false),
            _ when exception.IsTooManyRequests => (TooManyAttempts, false),
            _ => (LinkUnusable, false),
        };
    }
}
