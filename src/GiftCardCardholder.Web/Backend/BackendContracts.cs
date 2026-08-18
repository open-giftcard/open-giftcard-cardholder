namespace GiftCardCardholder.Web.Backend;

/// <summary>
/// The subset of the pinned backend contract this application binds to.
///
/// Only fields the cardholder experience actually needs are declared. Unknown
/// members are ignored during deserialization, so the backend may add fields
/// without breaking this client. <c>BackendContractTests</c> asserts every
/// member below still exists in <c>contracts/backend.openapi.json</c>.
/// </summary>
internal sealed record TokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);

internal sealed record LoginBody(string? Email, string? Password, string? PhoneNumber);

internal sealed record RefreshBody(string RefreshToken);

internal sealed record RevokeBody(string RefreshToken);

internal sealed record ClaimBody(string ClaimToken, string? Password, string? IdempotencyKey);

internal sealed record ClaimEpinBody(
    string ClaimToken,
    string Pin,
    string? ContactType,
    string? RecipientContact,
    string? Password,
    string IdempotencyKey);

internal sealed record CreateGiftCardShareBody(decimal Amount, string IdempotencyKey);

internal sealed record CreateDirectGiftCardShareBody(
    decimal Amount,
    string ContactType,
    string RecipientContact,
    string IdempotencyKey);

internal sealed record CancelGiftCardShareBody(string IdempotencyKey);

internal sealed record ClaimGiftCardShareBody(
    string ClaimToken,
    string Pin,
    string IdempotencyKey);

internal sealed record ClaimDirectGiftCardShareBody(
    string ClaimToken,
    string? Password,
    string IdempotencyKey);

internal sealed record ClaimedGiftCard(
    Guid Id,
    string PublicReference,
    string LifecycleState,
    decimal FundedAmount,
    string Currency,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Result of a claim.
///
/// <see cref="Session"/> is populated only when the claim created the recipient
/// identity (backend IMPL-019). An existing account claiming a card gets no
/// session, deliberately: possessing one invitation must not authenticate an
/// account that may already hold other cards.
/// </summary>
internal sealed record ClaimResult(
    Guid InvitationId,
    Guid OwnerUserId,
    bool IdentityWasCreated,
    string MaskedLoginIdentifier,
    TokenPair? Session,
    ClaimedGiftCard GiftCard,
    DateTimeOffset ClaimedAtUtc);

internal sealed record CurrentUser(
    Guid Id,
    string? Email,
    string? PhoneNumber,
    string Status,
    string ContextType);

internal sealed record OwnedGiftCard(
    Guid Id,
    string PublicReference,
    string LifecycleState,
    decimal FundedAmount,
    decimal Balance,
    decimal ReservedBalance,
    decimal AvailableBalance,
    string Currency,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset IssuedAtUtc);

internal sealed record OwnedGiftCardPage(
    IReadOnlyList<OwnedGiftCard> Items,
    int Limit,
    string? NextCursor);

internal sealed record OwnedGiftCardDetail(
    Guid Id,
    string PublicReference,
    Guid FundingOrganizationId,
    Guid IssuingOrganizationId,
    string OwnershipState,
    string LifecycleState,
    decimal FundedAmount,
    decimal Balance,
    decimal ReservedBalance,
    decimal AvailableBalance,
    string Currency,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc,
    bool IsTransferable,
    bool IsDivisible,
    Guid RootGiftCardId,
    int Generation,
    Guid? DistributionInvitationId,
    DateTimeOffset? DistributedAtUtc,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset IssuedAtUtc);

internal sealed record FinancialHistoryItem(
    string EventKey,
    string Category,
    string Operation,
    Guid EntityId,
    Guid? GiftCardId,
    string? GiftCardPublicReference,
    string? BusinessReference,
    decimal? Amount,
    string? Currency,
    string FinancialDirection,
    string? State,
    Guid? ActorUserId,
    DateTimeOffset OccurredAtUtc);

internal sealed record FinancialHistoryPage(
    IReadOnlyList<FinancialHistoryItem> Items,
    int Limit,
    string? NextCursor);

internal sealed record OwnGiftCardLifecycleBody(string IdempotencyKey);

internal sealed record IssuedPaymentToken(
    Guid Id,
    Guid GiftCardId,
    string GiftCardPublicReference,
    string RawToken,
    string NumericCode,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

internal sealed record PaymentTokenStatus(
    Guid Id,
    Guid GiftCardId,
    string State,
    Guid? PaymentProvisionId,
    decimal? Amount,
    string? Currency,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? SettledAtUtc,
    decimal? ConfirmedAmount);

internal sealed record GiftCardShare(
    Guid Id,
    string Kind,
    Guid SourceGiftCardId,
    Guid SenderUserId,
    Guid? ClaimedByUserId,
    Guid? ChildGiftCardId,
    string? SourceGiftCardPublicReference,
    string? ChildGiftCardPublicReference,
    decimal Amount,
    string Currency,
    string State,
    int FailedPinAttempts,
    string? RecipientContactType,
    string? MaskedRecipientContact,
    bool? IdentityWasCreatedOnClaim,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? ClosedAtUtc);

internal sealed record GiftCardSharePage(
    IReadOnlyList<GiftCardShare> Items,
    int Limit,
    string? NextCursor);

internal sealed record CreatedGiftCardShare(
    GiftCardShare Share,
    string ClaimUrl,
    string Pin);

internal sealed record CreatedDirectGiftCardShare(
    GiftCardShare Share,
    string MaskedRecipientContact,
    bool DeliveryDispatchedThisRequest);

internal sealed record SharedGiftCard(
    Guid Id,
    string PublicReference,
    string LifecycleState,
    decimal FundedAmount,
    string Currency,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc);

internal sealed record ClaimedGiftCardShare(
    GiftCardShare Share,
    SharedGiftCard ChildGiftCard);

internal sealed record ClaimedDirectGiftCardShare(
    GiftCardShare Share,
    Guid OwnerUserId,
    bool IdentityWasCreated,
    string MaskedLoginIdentifier,
    TokenPair? Session,
    SharedGiftCard ChildGiftCard);
