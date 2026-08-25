using System.Net;
using System.Text.Json;
using GiftCardCardholder.Web.Backend;
using Microsoft.Extensions.Logging.Abstractions;

namespace GiftCardCardholder.Tests;

/// <summary>
/// Checks the bytes this app actually puts on the wire against the pinned
/// contract, rather than a hand-written list of what it is believed to send.
///
/// <para>
/// <see cref="BackendContractTests"/> asserts that routes and fields named in
/// its own <c>InlineData</c> exist in the document. Those lists are written by
/// hand, so they catch the backend moving away from this client but not this
/// client falling behind the backend. The POS client sent no idempotency key
/// for four days after the backend began requiring one, and assertions of that
/// shape passed the whole time, because the only thing they check is that the
/// field exists somewhere in the contract.
/// </para>
///
/// <para>
/// These tests drive the real <see cref="BackendClient"/> through a capturing
/// handler and read the body it produced. Nothing is transcribed, so a field
/// this client stops sending, or sends under a new name, fails here. Since
/// backend commit a8a506a the document declares which fields are required, so
/// the required check below is enforcing rather than decorative.
/// </para>
/// </summary>
public sealed class SerialisedRequestContractTests
{
    private static readonly JsonDocument Contract = JsonDocument.Parse(
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "contracts", "backend.openapi.json")));

    public static TheoryData<string, string> IdempotentOperations() => new()
    {
        { "create share", "/api/v1/me/gift-cards/{giftCardId}/shares" },
        { "create direct share", "/api/v1/me/gift-cards/{giftCardId}/share-invitations" },
        { "cancel share", "/api/v1/me/shares/{shareId}/cancel" },
        { "claim share", "/api/v1/share-claims" },
        { "claim direct share", "/api/v1/share-invitation-claims" },
        { "suspend card", "/api/v1/me/gift-cards/{giftCardId}/lifecycle/suspend" },
        { "reactivate card", "/api/v1/me/gift-cards/{giftCardId}/lifecycle/reactivate" },
    };

    [Theory]
    [MemberData(nameof(IdempotentOperations))]
    public async Task EveryIdempotentCallMatchesTheContractForItsOperation(
        string operation,
        string path)
    {
        var body = await CaptureAsync(operation);

        AssertBodyMatchesSchema(body, path, operation);
    }

    /// <summary>
    /// The idempotency key is the field whose absence caused a live defect in
    /// the sibling POS client, and the contract now declares it required on
    /// every one of these operations.
    /// </summary>
    [Theory]
    [MemberData(nameof(IdempotentOperations))]
    public async Task EveryIdempotentCallSendsANonEmptyIdempotencyKey(
        string operation,
        string path)
    {
        _ = path;
        var body = await CaptureAsync(operation);

        Assert.True(
            body.TryGetProperty("idempotencyKey", out var key),
            $"The '{operation}' call sends no idempotency key, so a retry will be " +
            "refused instead of returning the original outcome.");
        Assert.False(
            string.IsNullOrWhiteSpace(key.GetString()),
            $"The '{operation}' call sends an empty idempotency key.");
    }

    private static void AssertBodyMatchesSchema(JsonElement body, string path, string operation)
    {
        var schema = RequestSchema(path);
        var declared = schema.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal)
            : [];

        foreach (var sent in body.EnumerateObject())
        {
            Assert.True(
                declared.Contains(sent.Name),
                $"The '{operation}' call sends '{sent.Name}', which the pinned contract " +
                $"does not declare on {path}.");
        }

        if (!schema.TryGetProperty("required", out var required))
        {
            return;
        }

        foreach (var name in required.EnumerateArray().Select(item => item.GetString()))
        {
            Assert.True(
                body.TryGetProperty(name!, out _),
                $"The contract requires '{name}' on {path}, and the '{operation}' call " +
                "does not send it.");
        }
    }

    private static JsonElement RequestSchema(string path)
    {
        var schema = Contract.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        return Contract.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(reference.GetString()!.Split('/')[^1]);
    }

    private static async Task<JsonElement> CaptureAsync(string operation)
    {
        using var handler = new CapturingHandler();
        using var http = new HttpClient(handler)
        {
            // Program.cs appends this prefix, and the relative routes in
            // BackendClient are written against it.
            BaseAddress = new Uri("https://backend.example/api/v1/"),
        };
        var client = new BackendClient(http, NullLogger<BackendClient>.Instance);
        var card = Guid.NewGuid();
        var share = Guid.NewGuid();
        const string Token = "access-token";
        const string Key = "idem-key-1";

        try
        {
            Task call = operation switch
            {
                "create share" => client.CreateGiftCardShareAsync(
                    Token, card, 10m, Key, CancellationToken.None),
                "create direct share" => client.CreateDirectGiftCardShareAsync(
                    Token, card, 10m, "Email", "someone@example.test", Key, CancellationToken.None),
                "cancel share" => client.CancelGiftCardShareAsync(
                    Token, share, Key, CancellationToken.None),
                "claim share" => client.ClaimGiftCardShareAsync(
                    Token, "claim-token", "1234", Key, CancellationToken.None),
                "claim direct share" => client.ClaimDirectGiftCardShareAsync(
                    "claim-token", "Password1!", Key, null, CancellationToken.None),
                "suspend card" => client.SuspendMyGiftCardAsync(
                    Token, card, Key, CancellationToken.None),
                "reactivate card" => client.ReactivateMyGiftCardAsync(
                    Token, card, Key, CancellationToken.None),
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
            };

            await call;
        }
        catch (Exception)
        {
            // The stub answers with an empty object, so deserialising the result
            // may fail. The request was already captured by then, which is the
            // only thing under test here.
        }

        Assert.NotNull(handler.LastBody);
        return JsonDocument.Parse(handler.LastBody!).RootElement.Clone();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
