using Npgsql;

namespace GiftCardCardholder.Web.Sessions;

/// <summary>
/// PostgreSQL-backed session storage.
///
/// Sessions are looked up by cookie hash only; the raw cookie is never stored.
/// Backend tokens arrive already encrypted by
/// <see cref="BackendTokenProtector"/> and this type never decrypts them, so a
/// store-level bug cannot leak a usable credential.
/// </summary>
internal sealed class PostgreSqlCardholderSessionStore(NpgsqlDataSource dataSource)
    : ICardholderSessionStore
{
    private const string Schema = """
        create table if not exists cardholder_sessions (
            id uuid primary key,
            cookie_hash text not null unique,
            user_id uuid not null,
            access_token text not null,
            refresh_token text not null,
            access_expires_at_utc timestamptz not null,
            refresh_expires_at_utc timestamptz not null,
            created_at_utc timestamptz not null,
            last_seen_at_utc timestamptz not null
        );

        create index if not exists ix_cardholder_sessions_refresh_expiry
            on cardholder_sessions (refresh_expires_at_utc);

        create table if not exists cardholder_activations (
            id uuid primary key,
            cookie_hash text not null unique,
            claim_token text not null,
            idempotency_key text not null,
            purpose text not null default 'GiftCardDistribution',
            created_at_utc timestamptz not null,
            expires_at_utc timestamptz not null
        );

        alter table cardholder_activations
            add column if not exists purpose text not null default 'GiftCardDistribution';

        create index if not exists ix_cardholder_activations_expiry
            on cardholder_activations (expires_at_utc);

        create table if not exists cardholder_payment_credentials (
            payment_token_id uuid primary key,
            session_id uuid not null,
            gift_card_id uuid not null,
            public_reference text not null,
            raw_token text not null,
            numeric_code text not null,
            issued_at_utc timestamptz not null,
            expires_at_utc timestamptz not null
        );

        create index if not exists ix_cardholder_payment_credentials_expiry
            on cardholder_payment_credentials (expires_at_utc);
        """;

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand(
                """
                select 1 from cardholder_sessions limit 0;
                select 1 from cardholder_activations limit 0;
                select 1 from cardholder_payment_credentials limit 0;
                """);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(Schema);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CreateSessionAsync(
        StoredSession session,
        string cookieHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            insert into cardholder_sessions (
                id, cookie_hash, user_id, access_token, refresh_token,
                access_expires_at_utc, refresh_expires_at_utc,
                created_at_utc, last_seen_at_utc)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $8);
            """);
        command.Parameters.AddWithValue(session.Id);
        command.Parameters.AddWithValue(cookieHash);
        command.Parameters.AddWithValue(session.UserId);
        command.Parameters.AddWithValue(session.ProtectedAccessToken);
        command.Parameters.AddWithValue(session.ProtectedRefreshToken);
        command.Parameters.AddWithValue(session.AccessTokenExpiresAtUtc);
        command.Parameters.AddWithValue(session.RefreshTokenExpiresAtUtc);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredSession?> FindSessionAsync(
        string cookieHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            update cardholder_sessions
               set last_seen_at_utc = $2
             where cookie_hash = $1
               and refresh_expires_at_utc > $2
            returning id, user_id, access_token, refresh_token,
                      access_expires_at_utc, refresh_expires_at_utc;
            """);
        command.Parameters.AddWithValue(cookieHash);
        command.Parameters.AddWithValue(now);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredSession(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5));
    }

    public async Task UpdateSessionTokensAsync(
        StoredSession session,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            update cardholder_sessions
               set access_token = $2,
                   refresh_token = $3,
                   access_expires_at_utc = $4,
                   refresh_expires_at_utc = $5,
                   last_seen_at_utc = $6
             where id = $1;
            """);
        command.Parameters.AddWithValue(session.Id);
        command.Parameters.AddWithValue(session.ProtectedAccessToken);
        command.Parameters.AddWithValue(session.ProtectedRefreshToken);
        command.Parameters.AddWithValue(session.AccessTokenExpiresAtUtc);
        command.Parameters.AddWithValue(session.RefreshTokenExpiresAtUtc);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteSessionAsync(string cookieHash, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "delete from cardholder_sessions where cookie_hash = $1;");
        command.Parameters.AddWithValue(cookieHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CreateActivationAsync(
        StoredActivation activation,
        string cookieHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            insert into cardholder_activations (
                id, cookie_hash, claim_token, idempotency_key, purpose,
                created_at_utc, expires_at_utc)
            values ($1, $2, $3, $4, $5, $6, $7);
            """);
        command.Parameters.AddWithValue(activation.Id);
        command.Parameters.AddWithValue(cookieHash);
        command.Parameters.AddWithValue(activation.ProtectedClaimToken);
        command.Parameters.AddWithValue(activation.IdempotencyKey);
        command.Parameters.AddWithValue(activation.Purpose.ToString());
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(activation.ExpiresAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredActivation?> FindActivationAsync(
        string cookieHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            select id, claim_token, idempotency_key, purpose, expires_at_utc
              from cardholder_activations
             where cookie_hash = $1
               and expires_at_utc > $2;
            """);
        command.Parameters.AddWithValue(cookieHash);
        command.Parameters.AddWithValue(now);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredActivation(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            Enum.Parse<ActivationPurpose>(reader.GetString(3), ignoreCase: false),
            reader.GetFieldValue<DateTimeOffset>(4));
    }

    public async Task DeleteActivationAsync(string cookieHash, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "delete from cardholder_activations where cookie_hash = $1;");
        command.Parameters.AddWithValue(cookieHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CreatePaymentCredentialAsync(
        StoredPaymentCredential credential,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Issuance is idempotent at the backend, so a replayed identifier must
        // refresh the row rather than collide.
        await using var command = dataSource.CreateCommand(
            """
            insert into cardholder_payment_credentials (
                payment_token_id, session_id, gift_card_id, public_reference,
                raw_token, numeric_code, issued_at_utc, expires_at_utc)
            values ($1, $2, $3, $4, $5, $6, $7, $8)
            on conflict (payment_token_id) do update
                set session_id = excluded.session_id,
                    gift_card_id = excluded.gift_card_id,
                    public_reference = excluded.public_reference,
                    raw_token = excluded.raw_token,
                    numeric_code = excluded.numeric_code,
                    issued_at_utc = excluded.issued_at_utc,
                    expires_at_utc = excluded.expires_at_utc;
            """);
        command.Parameters.AddWithValue(credential.PaymentTokenId);
        command.Parameters.AddWithValue(credential.SessionId);
        command.Parameters.AddWithValue(credential.GiftCardId);
        command.Parameters.AddWithValue(credential.GiftCardPublicReference);
        command.Parameters.AddWithValue(credential.ProtectedRawToken);
        command.Parameters.AddWithValue(credential.ProtectedNumericCode);
        command.Parameters.AddWithValue(credential.IssuedAtUtc);
        command.Parameters.AddWithValue(credential.ExpiresAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredPaymentCredential?> FindPaymentCredentialAsync(
        Guid paymentTokenId,
        Guid sessionId,
        Guid giftCardId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            select payment_token_id, session_id, gift_card_id, public_reference,
                   raw_token, numeric_code, issued_at_utc, expires_at_utc
              from cardholder_payment_credentials
             where payment_token_id = $1
               and session_id = $2
               and gift_card_id = $3
               and expires_at_utc > $4;
            """);
        command.Parameters.AddWithValue(paymentTokenId);
        command.Parameters.AddWithValue(sessionId);
        command.Parameters.AddWithValue(giftCardId);
        command.Parameters.AddWithValue(now);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredPaymentCredential(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7));
    }

    public async Task<int> DeleteExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            with removed_sessions as (
                delete from cardholder_sessions
                 where refresh_expires_at_utc <= $1
                returning 1
            ),
            removed_activations as (
                delete from cardholder_activations
                 where expires_at_utc <= $1
                returning 1
            ),
            removed_payment_credentials as (
                delete from cardholder_payment_credentials
                 where expires_at_utc <= $1
                returning 1
            )
            select (select count(*) from removed_sessions)
                 + (select count(*) from removed_activations)
                 + (select count(*) from removed_payment_credentials);
            """);
        command.Parameters.AddWithValue(now);
        var removed = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(removed, System.Globalization.CultureInfo.InvariantCulture);
    }
}
