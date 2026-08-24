using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace GiftCardCardholder.Web.Sessions;

internal static class CardholderDatabaseMigrator
{
    internal const string Switch = "--migrate";
    private const string MigrationId = "20260824_001_managed_sessions";
    private const string MigrationSql =
        """
        CREATE TABLE IF NOT EXISTS cardholder_sessions (
            id uuid PRIMARY KEY,
            cookie_hash text NOT NULL UNIQUE,
            user_id uuid NOT NULL,
            access_token text NOT NULL,
            refresh_token text NOT NULL,
            access_expires_at_utc timestamptz NOT NULL,
            refresh_expires_at_utc timestamptz NOT NULL,
            created_at_utc timestamptz NOT NULL,
            last_seen_at_utc timestamptz NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_cardholder_sessions_refresh_expiry
            ON cardholder_sessions (refresh_expires_at_utc);

        CREATE TABLE IF NOT EXISTS cardholder_activations (
            id uuid PRIMARY KEY,
            cookie_hash text NOT NULL UNIQUE,
            claim_token text NOT NULL,
            idempotency_key text NOT NULL,
            purpose text NOT NULL DEFAULT 'GiftCardDistribution',
            created_at_utc timestamptz NOT NULL,
            expires_at_utc timestamptz NOT NULL
        );

        ALTER TABLE cardholder_activations
            ADD COLUMN IF NOT EXISTS purpose text NOT NULL DEFAULT 'GiftCardDistribution';

        CREATE INDEX IF NOT EXISTS ix_cardholder_activations_expiry
            ON cardholder_activations (expires_at_utc);

        CREATE TABLE IF NOT EXISTS cardholder_payment_credentials (
            payment_token_id uuid PRIMARY KEY,
            session_id uuid NOT NULL,
            gift_card_id uuid NOT NULL,
            public_reference text NOT NULL,
            raw_token text NOT NULL,
            numeric_code text NOT NULL,
            issued_at_utc timestamptz NOT NULL,
            expires_at_utc timestamptz NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_cardholder_payment_credentials_expiry
            ON cardholder_payment_credentials (expires_at_utc);
        """;

    internal static bool IsRequested(IEnumerable<string> arguments) =>
        arguments.Contains(Switch, StringComparer.Ordinal);

    internal static async Task RunAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var migrationConnection = configuration.GetConnectionString("CardholderMigrations");
        if (string.IsNullOrWhiteSpace(migrationConnection))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:CardholderMigrations is required for {Switch}. " +
                "It must use the cardholder migration owner, never the runtime role.");
        }

        var runtimeConnection = configuration.GetConnectionString("Cardholder");
        if (string.IsNullOrWhiteSpace(runtimeConnection))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:Cardholder is required for {Switch} so the migrator " +
                "can grant the runtime role only its required table privileges.");
        }

        var runtimeRole = new NpgsqlConnectionStringBuilder(runtimeConnection).Username;
        if (string.IsNullOrWhiteSpace(runtimeRole))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Cardholder must name the runtime database user.");
        }

        var checksum = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(MigrationSql)));
        await using var dataSource = NpgsqlDataSource.Create(migrationConnection);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            "SELECT pg_advisory_xact_lock(hashtext('open-giftcard-cardholder-migrations'));",
            cancellationToken);
        await ExecuteAsync(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS cardholder_schema_migrations (
                migration_id text PRIMARY KEY,
                sha256 text NOT NULL,
                applied_at_utc timestamptz NOT NULL DEFAULT now()
            );
            """,
            cancellationToken);

        var recordedChecksum = await ReadChecksumAsync(
            connection,
            transaction,
            cancellationToken);
        if (recordedChecksum is not null &&
            !string.Equals(recordedChecksum, checksum, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cardholder migration {MigrationId} was changed after it was applied. " +
                $"Database has {recordedChecksum}; source has {checksum}.");
        }

        if (recordedChecksum is null)
        {
            await ExecuteAsync(
                connection,
                transaction,
                MigrationSql,
                cancellationToken);
            await RecordMigrationAsync(
                connection,
                transaction,
                checksum,
                cancellationToken);
        }

        await VerifyColumnsAsync(
            connection,
            transaction,
            "cardholder_sessions",
            [
                "id|uuid|NO",
                "cookie_hash|text|NO",
                "user_id|uuid|NO",
                "access_token|text|NO",
                "refresh_token|text|NO",
                "access_expires_at_utc|timestamptz|NO",
                "refresh_expires_at_utc|timestamptz|NO",
                "created_at_utc|timestamptz|NO",
                "last_seen_at_utc|timestamptz|NO",
            ],
            cancellationToken);
        await VerifyColumnsAsync(
            connection,
            transaction,
            "cardholder_activations",
            [
                "id|uuid|NO",
                "cookie_hash|text|NO",
                "claim_token|text|NO",
                "idempotency_key|text|NO",
                "purpose|text|NO",
                "created_at_utc|timestamptz|NO",
                "expires_at_utc|timestamptz|NO",
            ],
            cancellationToken);
        await VerifyColumnsAsync(
            connection,
            transaction,
            "cardholder_payment_credentials",
            [
                "payment_token_id|uuid|NO",
                "session_id|uuid|NO",
                "gift_card_id|uuid|NO",
                "public_reference|text|NO",
                "raw_token|text|NO",
                "numeric_code|text|NO",
                "issued_at_utc|timestamptz|NO",
                "expires_at_utc|timestamptz|NO",
            ],
            cancellationToken);

        var quotedRuntimeRole = new NpgsqlCommandBuilder().QuoteIdentifier(runtimeRole);
        await ExecuteAsync(
            connection,
            transaction,
            "REVOKE CREATE ON SCHEMA public FROM PUBLIC; " +
            $"GRANT USAGE ON SCHEMA public TO {quotedRuntimeRole}; " +
            "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE " +
            "cardholder_sessions, cardholder_activations, " +
            $"cardholder_payment_credentials TO {quotedRuntimeRole};",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Console.WriteLine($"cardholder migration {MigrationId} verified");
    }

    private static async Task VerifyColumnsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        IReadOnlyCollection<string> expected,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT column_name || '|' || udt_name || '|' || is_nullable
            FROM information_schema.columns
            WHERE table_schema = current_schema()
              AND table_name = $1
            ORDER BY ordinal_position;
            """;
        command.Parameters.AddWithValue(table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var actual = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            actual.Add(reader.GetString(0));
        }

        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Managed schema for {table} does not match this release. " +
                $"Expected [{string.Join(", ", expected)}], found " +
                $"[{string.Join(", ", actual)}].");
        }
    }

    private static async Task<string?> ReadChecksumAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT sha256 FROM cardholder_schema_migrations WHERE migration_id = $1;";
        command.Parameters.AddWithValue(MigrationId);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task RecordMigrationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string checksum,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO cardholder_schema_migrations (migration_id, sha256) VALUES ($1, $2);";
        command.Parameters.AddWithValue(MigrationId);
        command.Parameters.AddWithValue(checksum);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
