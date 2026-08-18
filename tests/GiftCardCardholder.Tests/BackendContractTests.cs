using System.Text.Json;

namespace GiftCardCardholder.Tests;

/// <summary>
/// Pins this client to the reviewed backend contract.
///
/// The app hand-writes its client rather than generating one, so nothing would
/// otherwise notice if the backend renamed a field the pages depend on. These
/// assertions turn that into a build failure the next time the pinned document
/// is refreshed.
/// </summary>
public sealed class BackendContractTests
{
    private static readonly JsonDocument Contract = JsonDocument.Parse(
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "contracts", "backend.openapi.json")));

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/auth/refresh")]
    [InlineData("/api/v1/auth/revoke")]
    [InlineData("/api/v1/gift-card-claims")]
    [InlineData("/api/v1/me")]
    [InlineData("/api/v1/me/gift-cards")]
    [InlineData("/api/v1/me/gift-cards/{giftCardId}")]
    [InlineData("/api/v1/me/gift-cards/{giftCardId}/history")]
    [InlineData("/api/v1/me/gift-cards/{giftCardId}/lifecycle/suspend")]
    [InlineData("/api/v1/me/gift-cards/{giftCardId}/lifecycle/reactivate")]
    [InlineData("/api/v1/me/gift-cards/{giftCardId}/payment-tokens")]
    [InlineData("/api/v1/me/gift-cards/{giftCardId}/payment-tokens/{paymentTokenId}")]
    [InlineData("/api/v1/me/gift-cards/{giftCardId}/shares")]
    [InlineData("/api/v1/me/gift-cards/{giftCardId}/share-invitations")]
    [InlineData("/api/v1/me/shares")]
    [InlineData("/api/v1/me/shares/{shareId}/cancel")]
    [InlineData("/api/v1/share-claims")]
    [InlineData("/api/v1/share-invitation-claims")]
    public void OperationsThisClientCallsExist(string path)
    {
        var paths = Contract.RootElement.GetProperty("paths");

        Assert.True(
            paths.TryGetProperty(path, out _),
            $"The pinned backend contract no longer exposes {path}.");
    }

    [Theory]
    [InlineData("LoginApiRequest", "email,password,phoneNumber")]
    [InlineData("TokenPairApiResponse", "accessToken,accessTokenExpiresAtUtc,refreshToken,refreshTokenExpiresAtUtc")]
    [InlineData("ClaimGiftCardApiRequest", "claimToken,password,idempotencyKey")]
    [InlineData("GiftCardClaimApiResponse", "invitationId,ownerUserId,identityWasCreated,maskedLoginIdentifier,session,giftCard,claimedAtUtc")]
    [InlineData("CurrentUserApiResponse", "id,email,phoneNumber,status,contextType")]
    [InlineData("OwnedGiftCardSummary", "id,publicReference,lifecycleState,fundedAmount,balance,reservedBalance,availableBalance,currency,validFromUtc,expiresAtUtc,claimedAtUtc,issuedAtUtc")]
    [InlineData("OwnedGiftCardPage", "items,limit,nextCursor")]
    [InlineData("OwnedGiftCardDetail", "id,publicReference,fundingOrganizationId,issuingOrganizationId,ownershipState,lifecycleState,fundedAmount,balance,reservedBalance,availableBalance,currency,validFromUtc,expiresAtUtc,isTransferable,isDivisible,rootGiftCardId,generation,distributionInvitationId,distributedAtUtc,claimedAtUtc,issuedAtUtc")]
    [InlineData("FinancialHistoryItem", "eventKey,category,operation,entityId,giftCardId,giftCardPublicReference,businessReference,amount,currency,financialDirection,state,actorUserId,occurredAtUtc")]
    [InlineData("FinancialHistoryPage", "items,limit,nextCursor")]
    [InlineData("OwnGiftCardLifecycleCommandApiRequest", "idempotencyKey")]
    [InlineData("IssuedPaymentTokenResult", "id,giftCardId,giftCardPublicReference,rawToken,numericCode,issuedAtUtc,expiresAtUtc")]
    [InlineData("PaymentTokenStatusResult", "id,giftCardId,state,paymentProvisionId,amount,currency,expiresAtUtc,settledAtUtc,confirmedAmount")]
    [InlineData("CreateGiftCardShareApiRequest", "amount,idempotencyKey")]
    [InlineData("CreateDirectGiftCardShareApiRequest", "amount,contactType,recipientContact,idempotencyKey")]
    [InlineData("CancelGiftCardShareApiRequest", "idempotencyKey")]
    [InlineData("ClaimGiftCardShareApiRequest", "claimToken,pin,idempotencyKey")]
    [InlineData("ClaimDirectGiftCardShareApiRequest", "claimToken,password,idempotencyKey")]
    [InlineData("GiftCardShareResult", "id,kind,sourceGiftCardId,senderUserId,claimedByUserId,childGiftCardId,sourceGiftCardPublicReference,childGiftCardPublicReference,amount,currency,state,failedPinAttempts,recipientContactType,maskedRecipientContact,identityWasCreatedOnClaim,expiresAtUtc,createdAtUtc,claimedAtUtc,closedAtUtc")]
    [InlineData("GiftCardSharePage", "items,limit,nextCursor")]
    [InlineData("CreatedGiftCardShareResult", "share,claimUrl,pin")]
    [InlineData("CreatedDirectGiftCardShareResult", "share,maskedRecipientContact,deliveryDispatchedThisRequest")]
    [InlineData("ClaimedGiftCardShareResult", "share,childGiftCard")]
    [InlineData("ClaimedDirectGiftCardShareResult", "share,ownerUserId,identityWasCreated,maskedLoginIdentifier,session,childGiftCard")]
    [InlineData("DirectGiftCardShareClaimSessionResult", "accessToken,accessTokenExpiresAtUtc,refreshToken,refreshTokenExpiresAtUtc")]
    [InlineData("GiftCardResult", "id,publicReference,lifecycleState,fundedAmount,currency,validFromUtc,expiresAtUtc")]
    public void FieldsThisClientBindsToExist(string schemaName, string expectedFields)
    {
        var schema = Contract.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName)
            .GetProperty("properties");

        foreach (var field in expectedFields.Split(','))
        {
            Assert.True(
                schema.TryGetProperty(field, out _),
                $"The pinned backend contract no longer exposes {schemaName}.{field}.");
        }
    }
}
