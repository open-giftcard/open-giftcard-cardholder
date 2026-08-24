<#
.SYNOPSIS
    Creates the local cardholder session database and split non-superuser roles.

.DESCRIPTION
    The cardholder application owns a small database used only for browser
    sessions and activation contexts. It must never be the backend's database
    and never the portal's (ADR-CARD-008).

    You are prompted for both passwords, and neither is written to a file, a
    log, or the shell history. Re-running is safe: existing objects are left
    alone.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Setup-LocalDatabase.ps1
#>
[CmdletBinding()]
param(
    [string]$PostgresHost = 'localhost',
    [int]$Port = 5432,
    [string]$AdminUser = 'postgres',
    [string]$Database = 'giftcard_cardholder',
    [string]$Role = 'giftcard_cardholder_app',
    [string]$MigrationRole = 'giftcard_cardholder_migrator'
)

$ErrorActionPreference = 'Stop'

$psql = Get-Command psql -ErrorAction SilentlyContinue
if (-not $psql) {
    $fallback = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
    if (-not (Test-Path $fallback)) {
        throw "psql was not found. Install the PostgreSQL client tools or add psql to PATH."
    }
    $psql = $fallback
}
else {
    $psql = $psql.Source
}

Write-Host "Creating database '$Database' with '$MigrationRole' and '$Role' on ${PostgresHost}:${Port}." -ForegroundColor Cyan

$rolePassword = Read-Host -Prompt "Choose a password for the new '$Role' login" -AsSecureString
$rolePlain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($rolePassword))
if ([string]::IsNullOrWhiteSpace($rolePlain)) {
    throw "A password for '$Role' is required."
}
$migrationPassword = Read-Host -Prompt "Choose a password for the new '$MigrationRole' login" -AsSecureString
$migrationPlain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($migrationPassword))
if ([string]::IsNullOrWhiteSpace($migrationPlain)) {
    throw "A password for '$MigrationRole' is required."
}

# Passed through a parameterised psql variable and quoted server-side, so a
# password containing quotes cannot break out of the statement.
$sql = @"
select format('create role %I login password %L nosuperuser nocreatedb nocreaterole nobypassrls',
              :'role_name', :'role_password')
where not exists (select 1 from pg_roles where rolname = :'role_name') \gexec

select format('create role %I login password %L nosuperuser nocreatedb nocreaterole nobypassrls',
              :'migration_role_name', :'migration_role_password')
where not exists (select 1 from pg_roles where rolname = :'migration_role_name') \gexec

select format('create database %I owner %I', :'db_name', :'migration_role_name')
where not exists (select 1 from pg_database where datname = :'db_name') \gexec

select format('alter database %I owner to %I', :'db_name', :'migration_role_name') \gexec

select format('grant connect on database %I to %I, %I',
              :'db_name', :'migration_role_name', :'role_name') \gexec
"@

$adminPassword = Read-Host -Prompt "Password for the '$AdminUser' PostgreSQL administrator" -AsSecureString
$env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($adminPassword))

try {
    $sql | & $psql `
        --host $PostgresHost --port $Port --username $AdminUser --dbname postgres `
        --no-password --quiet `
        --variable "role_name=$Role" `
        --variable "role_password=$rolePlain" `
        --variable "migration_role_name=$MigrationRole" `
        --variable "migration_role_password=$migrationPlain" `
        --variable "db_name=$Database"
    if ($LASTEXITCODE -ne 0) {
        throw "psql exited with code $LASTEXITCODE."
    }

    $schemaSql = @"
select format('alter table %I owner to %I', table_name, :'migration_role_name')
from (values
    ('cardholder_sessions'),
    ('cardholder_activations'),
    ('cardholder_payment_credentials'),
    ('cardholder_schema_migrations')) as existing(table_name)
where to_regclass(existing.table_name) is not null \gexec

revoke create on schema public from public;
select format('grant usage on schema public to %I', :'role_name') \gexec
"@
    $schemaSql | & $psql `
        --host $PostgresHost --port $Port --username $AdminUser --dbname $Database `
        --no-password --quiet `
        --variable "role_name=$Role" `
        --variable "migration_role_name=$MigrationRole"
    if ($LASTEXITCODE -ne 0) {
        throw "psql schema ownership update exited with code $LASTEXITCODE."
    }
}
finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    $rolePlain = $null
    $migrationPlain = $null
}

Write-Host ""
Write-Host "Done. Start the application with:" -ForegroundColor Green
Write-Host ""
Write-Host "  `$env:ConnectionStrings__Cardholder = `"Host=$PostgresHost;Port=$Port;Database=$Database;Username=$Role;Password=<the password you just chose>`""
Write-Host "  `$env:ConnectionStrings__CardholderMigrations = `"Host=$PostgresHost;Port=$Port;Database=$Database;Username=$MigrationRole;Password=<the migration password you just chose>`""
Write-Host "  dotnet run --project src\GiftCardCardholder.Web -- --migrate"
Write-Host "  `$env:ConnectionStrings__CardholderMigrations = `$null"
Write-Host "  dotnet run --project src\GiftCardCardholder.Web"
Write-Host ""
Write-Host "The migration command creates or verifies the managed schema. Normal startup performs no DDL."
