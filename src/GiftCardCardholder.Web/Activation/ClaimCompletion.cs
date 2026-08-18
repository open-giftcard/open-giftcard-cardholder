using System.Net.Sockets;
using GiftCardCardholder.Web.Backend;
using GiftCardCardholder.Web.Sessions;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace GiftCardCardholder.Web.Activation;

/// <summary>
/// What happens after the backend accepts a claim.
///
/// Backend IMPL-019 returns a token pair only when the claim created the
/// recipient identity. That is exactly the case where this application knows
/// the recipient just proved control of the delivered contact and chose the
/// password, so it can open the session immediately. An existing account gets
/// no token pair — possession of one invitation must not authenticate an
/// account that may hold other cards — so that path still ends at sign-in.
/// </summary>
internal static class ClaimCompletion
{
    public const string CardsPage = "/Cards";

    public const string SignInPage = "/SignIn";

    /// <summary>Name of the TempData entry holding the masked contact.</summary>
    public const string ActivatedIdentifierKey = "ActivatedIdentifier";

    public const string CardListStatusKey = "CardListStatusMessage";

    /// <summary>
    /// Consumes the activation context and returns the page to redirect to.
    /// </summary>
    public static async Task<string> CompleteAsync(
        HttpContext httpContext,
        ClaimResult result,
        CardholderSessionManager sessions,
        ITempDataDictionary tempData,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(tempData);

        await sessions.EndActivationAsync(httpContext, cancellationToken);

        if (result.Session is not null)
        {
            // The token pair is consumed server-side and stored encrypted; the
            // browser only ever receives the opaque session cookie.
            await sessions.SignInAsync(
                httpContext,
                result.Session,
                result.OwnerUserId,
                cancellationToken);

            // This recipient just chose a password and was signed in without
            // ever being told which contact it belongs to. It costs nothing
            // today and locks them out the first time they return on another
            // device, so the card list says it once.
            tempData[ActivatedIdentifierKey] = result.MaskedLoginIdentifier;
            return CardsPage;
        }

        tempData[ActivatedIdentifierKey] = result.MaskedLoginIdentifier;
        return SignInPage;
    }

    public static async Task<string> CompleteDirectShareAsync(
        HttpContext httpContext,
        ClaimedDirectGiftCardShare result,
        CardholderSessionManager sessions,
        ITempDataDictionary tempData,
        string signedInMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(tempData);

        await sessions.EndActivationAsync(httpContext, cancellationToken);
        if (result.Session is not null)
        {
            await sessions.SignInAsync(
                httpContext,
                result.Session,
                result.OwnerUserId,
                cancellationToken);
            tempData[CardListStatusKey] = signedInMessage;
            return CardsPage;
        }

        tempData[ActivatedIdentifierKey] = result.MaskedLoginIdentifier;
        return SignInPage;
    }

    /// <summary>
    /// The browser address this application observed, normalized to IPv4 when
    /// the socket reported an IPv4-mapped IPv6 address. It comes from the
    /// connection, never from a request header.
    /// </summary>
    public static string? ResolveClientAddress(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var address = httpContext.Connection.RemoteIpAddress;
        if (address is null)
        {
            return null;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6 &&
            address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.ToString();
    }
}
