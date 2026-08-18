using System.Net;

namespace GiftCardCardholder.Web.Backend;

/// <summary>
/// A structured backend failure. The backend returns ProblemDetails carrying a
/// stable <c>code</c> extension; that code — never the raw response body — is
/// what this application branches on and maps to recipient-facing copy.
/// </summary>
internal sealed class BackendProblemException(
    HttpStatusCode statusCode,
    string code,
    string? detail,
    Guid? correlationId)
    : Exception($"Backend returned {(int)statusCode} ({code}).")
{
    /// <summary>Backend codes this application reacts to by name.</summary>
    internal static class Codes
    {
        /// <summary>A new recipient must choose a password before claiming.</summary>
        public const string PasswordRequired = "user.password.required";

        public const string PasswordInvalidLength = "user.password.invalid_length";

        public const string PasswordCommon = "user.password.common";

        /// <summary>
        /// Deliberately indistinguishable on the backend: a malformed token, an
        /// unknown invitation, and a wrong secret all produce this.
        /// </summary>
        public const string ClaimInvalid = "distribution.claim.invalid";

        public const string ClaimAlreadyCompleted = "distribution.claim.already_completed";

        public const string ClaimConcurrentConflict = "distribution.claim.concurrent_conflict";

        public const string ClaimLoginRequired = "distribution.claim.login_required";

        public const string RecipientIdentityDisabled = "recipient_identity.disabled";

        public const string RecipientIdentityConflict = "recipient_identity.concurrent_conflict";

        public const string ShareClaimAlreadyCompleted = "sharing.claim.already_completed";

        public const string ShareClaimInvalid = "sharing.claim.invalid";
    }

    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>Stable backend problem code, or an empty string when absent.</summary>
    public string Code { get; } = code;

    /// <summary>Backend-curated detail. Safe to log; not always safe to display.</summary>
    public string? Detail { get; } = detail;

    /// <summary>Correlation id for support, when the backend supplied one.</summary>
    public Guid? CorrelationId { get; } = correlationId;

    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;

    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;

    public bool IsConflict => StatusCode == HttpStatusCode.Conflict;

    public bool IsTooManyRequests => StatusCode == HttpStatusCode.TooManyRequests;
}
