[CmdletBinding()]
param(
    [string]$BackendRepository,
    [string]$BackendEnvironmentFile,
    [switch]$KeepDatabases
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
if ([string]::IsNullOrWhiteSpace($BackendRepository)) {
    $BackendRepository = Join-Path $PSScriptRoot '..\..\open-giftcard'
}

$expectedBranch = 'main'
$expectedCommit = 'cfee9b1e17ab501e912d8aa8f84136d28e50dc6f'
$backendDatabase = 'giftcard_cardholder_e2e_backend'
$backendMigrator = 'giftcard_cardholder_e2e_migrator'
$backendApp = 'giftcard_cardholder_e2e_app'
$sessionDatabase = 'giftcard_cardholder_e2e_sessions'
$sessionApp = 'giftcard_cardholder_e2e_sessions_app'
$backendPort = 5145
$cardholderPort = 5184

$cardholderRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$backendRoot = (Resolve-Path $BackendRepository).Path
if ([string]::IsNullOrWhiteSpace($BackendEnvironmentFile)) {
    $BackendEnvironmentFile = Join-Path $backendRoot '.env'
}

$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$backendProject = Join-Path $backendRoot 'src\GiftCardPlatform.Api\GiftCardPlatform.Api.csproj'
$backendAssembly = Join-Path $backendRoot 'src\GiftCardPlatform.Api\bin\Debug\net10.0\GiftCardPlatform.Api.dll'
$cardholderProject = Join-Path $cardholderRoot 'src\GiftCardCardholder.Web\GiftCardCardholder.Web.csproj'
$cardholderAssembly = Join-Path $cardholderRoot 'src\GiftCardCardholder.Web\bin\Debug\net10.0\GiftCardCardholder.Web.dll'
$logRoot = Join-Path $cardholderRoot '.local\e2e\logs'
$keyRoot = Join-Path $cardholderRoot '.local\e2e\keys'

function Assert-DisposableName([string]$Value) {
    if (!$Value.StartsWith('giftcard_cardholder_e2e_', [StringComparison]::Ordinal)) {
        throw "Refusing to mutate non-E2E PostgreSQL object '$Value'."
    }
}

function Assert-LocalPortAvailable([int]$Port) {
    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        $Port
    )
    try {
        $listener.Start()
    }
    catch {
        throw "Local E2E port $Port is already in use."
    }
    finally {
        $listener.Stop()
    }
}

function Read-DotEnv([string]$Path) {
    $values = @{}
    foreach ($line in Get-Content $Path) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
            $values[$Matches[1]] = $Matches[2].Trim().Trim('"')
        }
    }
    return $values
}

function Invoke-Psql(
    [string]$Database,
    [string]$User,
    [string]$Password,
    [string]$Sql
) {
    $previousPassword = $env:PGPASSWORD
    try {
        $env:PGPASSWORD = $Password
        $Sql | & $psql `
            --host localhost `
            --port 5432 `
            --username $User `
            --dbname $Database `
            --no-psqlrc `
            --set ON_ERROR_STOP=1 `
            --quiet
        if ($LASTEXITCODE -ne 0) {
            throw "PostgreSQL command failed for database '$Database'."
        }
    }
    finally {
        $env:PGPASSWORD = $previousPassword
    }
}

function Invoke-PsqlScalar(
    [string]$Database,
    [string]$User,
    [string]$Password,
    [string]$Sql
) {
    $previousPassword = $env:PGPASSWORD
    try {
        $env:PGPASSWORD = $Password
        $result = & $psql `
            --host localhost `
            --port 5432 `
            --username $User `
            --dbname $Database `
            --no-psqlrc `
            --set ON_ERROR_STOP=1 `
            --tuples-only `
            --no-align `
            --command $Sql
        if ($LASTEXITCODE -ne 0) {
            throw "PostgreSQL scalar query failed for database '$Database'."
        }
        return ($result | Out-String).Trim()
    }
    finally {
        $env:PGPASSWORD = $previousPassword
    }
}

function Wait-Http([uri]$Uri, [System.Diagnostics.Process]$Process) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "Process $($Process.Id) exited before $Uri became ready."
        }

        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Uri -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "Timed out waiting for $Uri."
}

function Stop-OwnedProcess([System.Diagnostics.Process]$Process) {
    if ($null -ne $Process -and !$Process.HasExited) {
        Stop-Process -Id $Process.Id -Force
        $Process.WaitForExit(10000) | Out-Null
    }
}

function New-Browser([uri]$BaseAddress) {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $true
    $handler.CookieContainer = [System.Net.CookieContainer]::new()
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.BaseAddress = $BaseAddress
    return $client
}

function Send-BrowserRequest(
    [System.Net.Http.HttpClient]$Browser,
    [string]$Method,
    [string]$Uri,
    [hashtable]$Form = $null,
    [hashtable]$Headers = $null
) {
    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::new($Method),
        $Uri
    )
    try {
        if ($null -ne $Form) {
            $pairs = [System.Collections.Generic.List[
                System.Collections.Generic.KeyValuePair[string, string]
            ]]::new()
            foreach ($entry in $Form.GetEnumerator()) {
                $pairs.Add(
                    [System.Collections.Generic.KeyValuePair[string, string]]::new(
                        [string]$entry.Key,
                        [string]$entry.Value
                    )
                )
            }
            $request.Content = [System.Net.Http.FormUrlEncodedContent]::new($pairs)
        }
        if ($null -ne $Headers) {
            foreach ($entry in $Headers.GetEnumerator()) {
                $request.Headers.TryAddWithoutValidation(
                    [string]$entry.Key,
                    [string]$entry.Value
                ) | Out-Null
            }
        }

        $response = $Browser.SendAsync($request).GetAwaiter().GetResult()
        try {
            return [pscustomobject]@{
                Status = [int]$response.StatusCode
                Location = if ($null -eq $response.Headers.Location) {
                    $null
                }
                else {
                    $response.Headers.Location.OriginalString
                }
                Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            }
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $request.Dispose()
    }
}

function Get-AntiforgeryToken([string]$Html) {
    $match = [regex]::Match(
        $Html,
        'name="__RequestVerificationToken"[^>]*value="([^"]+)"',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )
    if (!$match.Success) {
        throw 'The page did not render an antiforgery token.'
    }
    return [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
}

function Get-HiddenInputValue([string]$Html, [string]$Name) {
    $escapedName = [regex]::Escape($Name)
    $match = [regex]::Match(
        $Html,
        'name="' + $escapedName + '"[^>]*value="([^"]+)"',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )
    if (!$match.Success) {
        throw "The page did not render the hidden '$Name' value."
    }
    return [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
}

function Get-ElementValueById([string]$Html, [string]$Id) {
    $escapedId = [regex]::Escape($Id)
    $textarea = [regex]::Match(
        $Html,
        '<textarea[^>]*id="' + $escapedId + '"[^>]*>(.*?)</textarea>',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
            [System.Text.RegularExpressions.RegexOptions]::Singleline
    )
    if ($textarea.Success) {
        return [System.Net.WebUtility]::HtmlDecode($textarea.Groups[1].Value.Trim())
    }

    $input = [regex]::Match(
        $Html,
        '<input[^>]*id="' + $escapedId + '"[^>]*value="([^"]*)"',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )
    if ($input.Success) {
        return [System.Net.WebUtility]::HtmlDecode($input.Groups[1].Value)
    }

    throw "The page did not render a value for element '$Id'."
}

function Get-DefinitionAmount([string]$Html, [string]$Term) {
    # The card detail page renders posted, reserved, and available value as a
    # <dt>/<dd> pair. Reading them back is how the E2E proves the recipient sees
    # backend-authored value components rather than anything computed here.
    $pattern = "<dt>$([regex]::Escape($Term))</dt>\s*<dd>([^<]+)</dd>"
    $match = [regex]::Match($Html, $pattern)
    if (!$match.Success) {
        throw "Card detail did not render a '$Term' value."
    }

    $text = $match.Groups[1].Value.Trim()
    $numeric = ($text -split '\s+')[0]
    return [decimal]::Parse(
        $numeric,
        [Globalization.NumberStyles]::Number,
        [Globalization.CultureInfo]::GetCultureInfo('en-US')
    )
}

function Get-CardReference([string]$Html) {
    $match = [regex]::Match($Html, '<p class="card-reference">([^<]+)</p>')
    if (!$match.Success) {
        throw 'Card detail did not render a public card reference.'
    }

    return $match.Groups[1].Value.Trim()
}

function Send-BrowserForm(
    [System.Net.Http.HttpClient]$Browser,
    [string]$Path,
    [hashtable]$Fields,
    [hashtable]$Headers = $null
) {
    $page = Send-BrowserRequest $Browser 'GET' $Path
    if ($page.Status -ne 200) {
        throw "Expected GET $Path to return 200; received $($page.Status)."
    }
    $form = @{}
    foreach ($entry in $Fields.GetEnumerator()) {
        $form[$entry.Key] = $entry.Value
    }
    $form['__RequestVerificationToken'] = Get-AntiforgeryToken $page.Body
    return Send-BrowserRequest $Browser 'POST' $Path $form $Headers
}

function Assert-Redirect($Response, [string]$ExpectedLocation) {
    if ($Response.Status -ne 302 -or $Response.Location -ne $ExpectedLocation) {
        throw "Expected redirect to '$ExpectedLocation'; received status $($Response.Status) and location '$($Response.Location)'."
    }
}

foreach ($name in @(
    $backendDatabase,
    $backendMigrator,
    $backendApp,
    $sessionDatabase,
    $sessionApp
)) {
    Assert-DisposableName $name
}
foreach ($port in @($backendPort, $cardholderPort)) {
    Assert-LocalPortAvailable $port
}

if (!(Test-Path $psql)) {
    throw "PostgreSQL 18 psql was not found at '$psql'."
}
if (!(Get-Command dotnet-ef -ErrorAction SilentlyContinue)) {
    throw 'The dotnet-ef global tool is required for the guarded E2E run.'
}

$actualBranch = (& git -C $backendRoot branch --show-current).Trim()
$actualCommit = (& git -C $backendRoot rev-parse HEAD).Trim()
if ($actualBranch -ne $expectedBranch -or $actualCommit -ne $expectedCommit) {
    throw "Backend must be $expectedBranch at $expectedCommit. Found $actualBranch at $actualCommit."
}
$sourceChanges = & git -C $backendRoot status --short -- src
if ($sourceChanges) {
    throw 'Backend source files are modified; refusing to run against an unpinned implementation.'
}

$backendEnvironment = Read-DotEnv $BackendEnvironmentFile
$administrator = $backendEnvironment['POSTGRES_SUPERUSER']
$administratorPassword = $backendEnvironment['POSTGRES_SUPERUSER_PASSWORD']
if ([string]::IsNullOrWhiteSpace($administrator) -or
    [string]::IsNullOrWhiteSpace($administratorPassword)) {
    throw 'The backend .env must provide the PostgreSQL superuser credentials.'
}

$backendPassword = [Guid]::NewGuid().ToString('N')
$migratorPassword = [Guid]::NewGuid().ToString('N')
$sessionPassword = [Guid]::NewGuid().ToString('N')
$jwtKey = "{0}{1}" -f [Guid]::NewGuid().ToString('N'), [Guid]::NewGuid().ToString('N')
$bootstrapSecret = "{0}{1}" -f [Guid]::NewGuid().ToString('N'), [Guid]::NewGuid().ToString('N')
$platformEmail = 'cardholder.platform.e2e@example.test'
$platformPassword = 'Cardholder-Platform-E2E-only-2026!'
$organizationEmail = 'cardholder.organization.e2e@example.test'
$organizationPassword = 'Cardholder-Organization-E2E-only-2026!'
$newRecipientEmail = 'cardholder.new-recipient.e2e@example.test'
$newRecipientPassword = 'Cardholder-New-Recipient-E2E-2026!'
$existingRecipientEmail = 'cardholder.existing-recipient.e2e@example.test'
$existingRecipientPassword = 'Cardholder-Existing-Recipient-E2E-2026!'
$directNewRecipientEmail = 'cardholder.direct-new-recipient.e2e@example.test'
$directNewRecipientPassword = 'Cardholder-Direct-New-Recipient-E2E-2026!'
$directExistingRecipientEmail = 'cardholder.direct-existing-recipient.e2e@example.test'
$directExistingRecipientPassword = 'Cardholder-Direct-Existing-Recipient-E2E-2026!'

$backendProcess = $null
$cardholderProcess = $null
$newBrowser = $null
$existingBrowser = $null
$directNewBrowser = $null
$directExistingBrowser = $null
New-Item -ItemType Directory -Force -Path $logRoot, $keyRoot | Out-Null

try {
    $dropSql = @"
SELECT pg_terminate_backend(pid)
FROM pg_stat_activity
WHERE datname IN ('$backendDatabase', '$sessionDatabase')
  AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS $backendDatabase;
DROP DATABASE IF EXISTS $sessionDatabase;
DROP ROLE IF EXISTS $backendApp;
DROP ROLE IF EXISTS $backendMigrator;
DROP ROLE IF EXISTS $sessionApp;
"@
    Invoke-Psql 'postgres' $administrator $administratorPassword $dropSql

    $createSql = @"
CREATE ROLE $backendMigrator LOGIN PASSWORD '$migratorPassword'
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
CREATE ROLE $backendApp LOGIN PASSWORD '$backendPassword'
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
CREATE ROLE $sessionApp LOGIN PASSWORD '$sessionPassword'
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
CREATE DATABASE $backendDatabase OWNER $backendMigrator;
CREATE DATABASE $sessionDatabase OWNER $sessionApp;
"@
    Invoke-Psql 'postgres' $administrator $administratorPassword $createSql

    $databaseSql = @"
CREATE EXTENSION IF NOT EXISTS ltree;
CREATE SCHEMA organizations AUTHORIZATION $backendMigrator;
CREATE SCHEMA audit AUTHORIZATION $backendMigrator;
CREATE SCHEMA identity AUTHORIZATION $backendMigrator;
CREATE SCHEMA "authorization" AUTHORIZATION $backendMigrator;
CREATE SCHEMA ledger AUTHORIZATION $backendMigrator;
CREATE SCHEMA corporate_credits AUTHORIZATION $backendMigrator;
CREATE SCHEMA gift_cards AUTHORIZATION $backendMigrator;
CREATE SCHEMA distribution AUTHORIZATION $backendMigrator;
CREATE SCHEMA sharing AUTHORIZATION $backendMigrator;
CREATE SCHEMA payments AUTHORIZATION $backendMigrator;
GRANT CONNECT ON DATABASE $backendDatabase TO $backendMigrator, $backendApp;
GRANT USAGE ON SCHEMA organizations, audit, identity, "authorization",
    ledger, corporate_credits, gift_cards, distribution, sharing, payments TO $backendApp;
REVOKE CREATE ON SCHEMA organizations, audit, identity, "authorization",
    ledger, corporate_credits, gift_cards, distribution, sharing, payments FROM $backendApp;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
ALTER DEFAULT PRIVILEGES FOR ROLE $backendMigrator IN SCHEMA organizations
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO $backendApp;
ALTER DEFAULT PRIVILEGES FOR ROLE $backendMigrator IN SCHEMA "authorization"
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO $backendApp;
ALTER DEFAULT PRIVILEGES FOR ROLE $backendMigrator IN SCHEMA identity
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO $backendApp;
ALTER DEFAULT PRIVILEGES FOR ROLE $backendMigrator IN SCHEMA audit
    GRANT SELECT, INSERT ON TABLES TO $backendApp;
ALTER DEFAULT PRIVILEGES FOR ROLE $backendMigrator IN SCHEMA ledger
    GRANT SELECT, INSERT ON TABLES TO $backendApp;
ALTER DEFAULT PRIVILEGES FOR ROLE $backendMigrator IN SCHEMA corporate_credits
    GRANT SELECT, INSERT ON TABLES TO $backendApp;
ALTER DEFAULT PRIVILEGES FOR ROLE $backendMigrator IN SCHEMA gift_cards
    GRANT SELECT, INSERT, UPDATE ON TABLES TO $backendApp;
ALTER DEFAULT PRIVILEGES FOR ROLE $backendMigrator IN SCHEMA distribution
    GRANT SELECT, INSERT, UPDATE ON TABLES TO $backendApp;
ALTER DEFAULT PRIVILEGES FOR ROLE $backendMigrator IN SCHEMA sharing
    GRANT SELECT, INSERT, UPDATE ON TABLES TO $backendApp;
ALTER DEFAULT PRIVILEGES FOR ROLE $backendMigrator IN SCHEMA payments
    GRANT SELECT, INSERT, UPDATE ON TABLES TO $backendApp;
"@
    Invoke-Psql $backendDatabase $administrator $administratorPassword $databaseSql

    & dotnet restore $backendProject
    if ($LASTEXITCODE -ne 0) { throw 'Pinned backend restore failed.' }
    & dotnet restore $cardholderProject
    if ($LASTEXITCODE -ne 0) { throw 'Cardholder restore failed.' }
    & dotnet build $backendProject --configuration Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Pinned backend build failed.' }
    & dotnet build $cardholderProject --configuration Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Cardholder build failed.' }

    $env:GIFTCARD_MIGRATIONS_CONNECTION =
        "Host=localhost;Port=5432;Database=$backendDatabase;Username=$backendMigrator;Password=$migratorPassword"
    $migrationContexts = @(
        @('GiftCardPlatform.Modules.Organizations', 'OrganizationsDbContext'),
        @('GiftCardPlatform.Modules.Audit', 'AuditDbContext'),
        @('GiftCardPlatform.Modules.Authorization', 'AuthorizationDbContext'),
        @('GiftCardPlatform.Modules.Identity', 'IdentityDbContext'),
        @('GiftCardPlatform.Modules.Ledger', 'LedgerDbContext'),
        @('GiftCardPlatform.Modules.CorporateCredits', 'CorporateCreditsDbContext'),
        @('GiftCardPlatform.Modules.GiftCards', 'GiftCardsDbContext'),
        @('GiftCardPlatform.Modules.Distribution', 'DistributionDbContext'),
        @('GiftCardPlatform.Modules.Sharing', 'SharingDbContext'),
        @('GiftCardPlatform.Modules.Payments', 'PaymentsDbContext')
    )
    foreach ($migration in $migrationContexts) {
        & dotnet-ef database update `
            --project (Join-Path $backendRoot "src\$($migration[0])") `
            --startup-project (Join-Path $backendRoot 'src\GiftCardPlatform.Api') `
            --context $migration[1] `
            --no-build
        if ($LASTEXITCODE -ne 0) {
            throw "Migration failed for $($migration[1])."
        }
    }

    $env:ConnectionStrings__Default =
        "Host=localhost;Port=5432;Database=$backendDatabase;Username=$backendApp;Password=$backendPassword"
    $env:Authentication__Jwt__SigningKey = $jwtKey
    $env:Authentication__LoginRateLimit__PermitLimit = '20'
    $env:Networking__ForwardedHeaders__KnownProxies__0 = '127.0.0.1'
    $env:Bootstrap__PlatformAdministrator__Secret = $bootstrapSecret
    $env:Distribution__ClaimBaseUrl = "http://127.0.0.1:$cardholderPort/activate"
    $env:Sharing__ClaimBaseUrl = "http://127.0.0.1:$cardholderPort/share/claim"
    $env:Sharing__DirectClaimBaseUrl = "http://127.0.0.1:$cardholderPort/activate/share"
    # The share TTL is fixed at 24 hours and cannot be shortened (ADR-016), so the
    # expiry scenario ages a share instead. Only the sweep cadence is tightened.
    $env:Sharing__ExpirationPollIntervalSeconds = '5'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$backendPort"
    $backendProcess = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList @($backendAssembly) `
        -WorkingDirectory (Split-Path $backendProject) `
        -WindowStyle Hidden `
        -PassThru `
        -RedirectStandardOutput (Join-Path $logRoot 'backend.out.log') `
        -RedirectStandardError (Join-Path $logRoot 'backend.err.log')
    Wait-Http "http://127.0.0.1:$backendPort/health/ready" $backendProcess

    $api = "http://127.0.0.1:$backendPort/api/v1"
    Invoke-RestMethod `
        -Method Post `
        -Uri "$api/bootstrap/platform-administrator" `
        -Headers @{ 'X-Platform-Bootstrap-Secret' = $bootstrapSecret } `
        -ContentType 'application/json' `
        -Body (@{ email = $platformEmail; password = $platformPassword } | ConvertTo-Json) |
        Out-Null
    $platformLogin = Invoke-RestMethod `
        -Method Post `
        -Uri "$api/auth/login" `
        -ContentType 'application/json' `
        -Body (@{ email = $platformEmail; password = $platformPassword } | ConvertTo-Json)
    $platformAuthorization = @{ Authorization = "Bearer $($platformLogin.accessToken)" }
    $organization = Invoke-RestMethod `
        -Method Post `
        -Uri "$api/organizations" `
        -Headers $platformAuthorization `
        -ContentType 'application/json' `
        -Body (@{ name = 'Cardholder E2E'; code = 'CARDHOLDER-E2E' } | ConvertTo-Json)
    Invoke-RestMethod `
        -Method Post `
        -Uri "$api/corporate-credits/allocations" `
        -Headers $platformAuthorization `
        -ContentType 'application/json' `
        -Body (@{
            organizationId = $organization.id
            amount = 250
            currency = 'TRY'
            businessReference = 'CARDHOLDER-E2E-FUND'
            idempotencyKey = 'cardholder-e2e-fund-v1'
        } | ConvertTo-Json) | Out-Null
    $organizationUser = Invoke-RestMethod `
        -Method Post `
        -Uri "$api/users" `
        -Headers $platformAuthorization `
        -ContentType 'application/json' `
        -Body (@{ email = $organizationEmail; password = $organizationPassword } | ConvertTo-Json)
    Invoke-RestMethod `
        -Method Post `
        -Uri "$api/users" `
        -Headers $platformAuthorization `
        -ContentType 'application/json' `
        -Body (@{ email = $existingRecipientEmail; password = $existingRecipientPassword } | ConvertTo-Json) |
        Out-Null
    Invoke-RestMethod `
        -Method Post `
        -Uri "$api/users" `
        -Headers $platformAuthorization `
        -ContentType 'application/json' `
        -Body (@{
            email = $directExistingRecipientEmail
            password = $directExistingRecipientPassword
        } | ConvertTo-Json) |
        Out-Null
    Invoke-RestMethod `
        -Method Post `
        -Uri "$api/organizations/$($organization.id)/initial-administrator" `
        -Headers $platformAuthorization `
        -ContentType 'application/json' `
        -Body (@{ userId = $organizationUser.id } | ConvertTo-Json) | Out-Null
    $organizationLogin = Invoke-RestMethod `
        -Method Post `
        -Uri "$api/auth/login" `
        -ContentType 'application/json' `
        -Body (@{ email = $organizationEmail; password = $organizationPassword } | ConvertTo-Json)
    $organizationAuthorization = @{
        Authorization = "Bearer $($organizationLogin.accessToken)"
        'X-Organization-Id' = $organization.id
    }
    $expiresAt = [DateTimeOffset]::UtcNow.AddYears(1).ToString('O')
    $batch = Invoke-RestMethod `
        -Method Post `
        -Uri "$api/organizations/$($organization.id)/gift-card-batches/" `
        -Headers $organizationAuthorization `
        -ContentType 'application/json' `
        -Body (@{
            batchReference = 'CARDHOLDER-E2E-BATCH'
            idempotencyKey = 'cardholder-e2e-batch-v1'
            items = @(
                @{
                    itemReference = 'NEW-RECIPIENT'
                    amount = 100
                    currency = 'TRY'
                    validFromUtc = $null
                    expiresAtUtc = $expiresAt
                    isTransferable = $true
                    isDivisible = $true
                    contactType = 'Email'
                    recipientContact = $newRecipientEmail
                },
                @{
                    itemReference = 'EXISTING-RECIPIENT'
                    amount = 100
                    currency = 'TRY'
                    validFromUtc = $null
                    expiresAtUtc = $expiresAt
                    isTransferable = $true
                    isDivisible = $true
                    contactType = 'Email'
                    recipientContact = $existingRecipientEmail
                }
            )
        } | ConvertTo-Json -Depth 8)
    $newItem = $batch.items | Where-Object itemReference -eq 'NEW-RECIPIENT'
    $existingItem = $batch.items | Where-Object itemReference -eq 'EXISTING-RECIPIENT'
    $newDelivery = Invoke-RestMethod `
        -Method Get `
        -Uri "$api/development/organizations/$($organization.id)/claim-deliveries/$($newItem.invitationId)" `
        -Headers $organizationAuthorization
    $existingDelivery = Invoke-RestMethod `
        -Method Get `
        -Uri "$api/development/organizations/$($organization.id)/claim-deliveries/$($existingItem.invitationId)" `
        -Headers $organizationAuthorization

    $env:ConnectionStrings__Cardholder =
        "Host=localhost;Port=5432;Database=$sessionDatabase;Username=$sessionApp;Password=$sessionPassword"
    $env:Backend__BaseUrl = "http://127.0.0.1:$backendPort"
    $env:DataProtection__KeyPath = $keyRoot
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$cardholderPort"
    $cardholderProcess = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList @($cardholderAssembly) `
        -WorkingDirectory (Split-Path $cardholderProject) `
        -WindowStyle Hidden `
        -PassThru `
        -RedirectStandardOutput (Join-Path $logRoot 'cardholder.out.log') `
        -RedirectStandardError (Join-Path $logRoot 'cardholder.err.log')
    Wait-Http "http://127.0.0.1:$cardholderPort/signin" $cardholderProcess

    $newBrowser = New-Browser "http://127.0.0.1:$cardholderPort/"
    $arrival = Send-BrowserRequest $newBrowser 'GET' $newDelivery.claimUrl
    Assert-Redirect $arrival '/activate/confirm'
    if ($arrival.Location.IndexOf(
        'token=',
        [StringComparison]::OrdinalIgnoreCase
    ) -ge 0) {
        throw 'The activation secret remained in the redirect URL.'
    }
    $probe = Send-BrowserForm $newBrowser '/activate/confirm' @{}
    Assert-Redirect $probe '/activate/password'
    $passwordClaim = Send-BrowserForm $newBrowser '/activate/password' @{
        Password = $newRecipientPassword
        ConfirmPassword = $newRecipientPassword
    }
    Assert-Redirect $passwordClaim '/cards'
    $newCards = Send-BrowserRequest $newBrowser 'GET' '/cards'
    if ($newCards.Status -ne 200 -or
        $newCards.Body.IndexOf(
            $newItem.giftCardPublicReference,
            [StringComparison]::Ordinal
        ) -lt 0 -or
        $newCards.Body.IndexOf('100.00 TRY', [StringComparison]::Ordinal) -lt 0) {
        throw 'The new-recipient journey did not land on the real 100 TRY card.'
    }
    if ($newCards.Body.IndexOf(
        'accessToken',
        [StringComparison]::OrdinalIgnoreCase
    ) -ge 0 -or
        $newCards.Body.IndexOf(
            'refreshToken',
            [StringComparison]::OrdinalIgnoreCase
        ) -ge 0) {
        throw 'A backend token name appeared in the cardholder page.'
    }

    $existingBrowser = New-Browser "http://127.0.0.1:$cardholderPort/"
    $existingArrival = Send-BrowserRequest $existingBrowser 'GET' $existingDelivery.claimUrl
    Assert-Redirect $existingArrival '/activate/confirm'
    $existingClaim = Send-BrowserForm $existingBrowser '/activate/confirm' @{}
    Assert-Redirect $existingClaim '/signin'
    $signInPage = Send-BrowserRequest $existingBrowser 'GET' '/signin'
    if ($signInPage.Body.IndexOf(
        'Your gift card is ready.',
        [StringComparison]::Ordinal
    ) -lt 0) {
        throw 'The existing-account branch did not show the safe sign-in handoff.'
    }
    $existingSignIn = Send-BrowserForm $existingBrowser '/signin' @{
        Identifier = $existingRecipientEmail
        Password = $existingRecipientPassword
    } @{ 'X-Forwarded-For' = '198.51.100.99' }
    Assert-Redirect $existingSignIn '/cards'
    $existingCards = Send-BrowserRequest $existingBrowser 'GET' '/cards'
    if ($existingCards.Status -ne 200 -or
        $existingCards.Body.IndexOf(
            $existingItem.giftCardPublicReference,
            [StringComparison]::Ordinal
        ) -lt 0 -or
        $existingCards.Body.IndexOf('100.00 TRY', [StringComparison]::Ordinal) -lt 0) {
        throw 'The existing-account journey did not land on the real 100 TRY card.'
    }

    $detailPath = "/cards/$($existingItem.giftCardId)"
    $detail = Send-BrowserRequest $existingBrowser 'GET' $detailPath
    if ($detail.Status -ne 200 -or
        $detail.Body.IndexOf(
            $existingItem.giftCardPublicReference,
            [StringComparison]::Ordinal
        ) -lt 0 -or
        $detail.Body.IndexOf('100.00 TRY', [StringComparison]::Ordinal) -lt 0 -or
        $detail.Body.IndexOf('Card loaded', [StringComparison]::Ordinal) -lt 0 -or
        $detail.Body.IndexOf('Suspend card', [StringComparison]::Ordinal) -lt 0) {
        throw 'CARD-002 detail did not render the backend card, balance, history, and active control.'
    }

    $suspend = Send-BrowserRequest `
        $existingBrowser `
        'POST' `
        "$detailPath`?handler=Suspend" `
        @{
            IdempotencyKey = Get-HiddenInputValue $detail.Body 'IdempotencyKey'
            __RequestVerificationToken = Get-AntiforgeryToken $detail.Body
        }
    Assert-Redirect $suspend $detailPath
    $suspended = Send-BrowserRequest $existingBrowser 'GET' $detailPath
    if ($suspended.Status -ne 200 -or
        $suspended.Body.IndexOf('Suspended', [StringComparison]::Ordinal) -lt 0 -or
        $suspended.Body.IndexOf('Reactivate card', [StringComparison]::Ordinal) -lt 0 -or
        $suspended.Body.IndexOf('Card suspended', [StringComparison]::Ordinal) -lt 0) {
        throw 'CARD-002 suspend did not reload the backend state and lifecycle history.'
    }

    $reactivate = Send-BrowserRequest `
        $existingBrowser `
        'POST' `
        "$detailPath`?handler=Reactivate" `
        @{
            IdempotencyKey = Get-HiddenInputValue $suspended.Body 'IdempotencyKey'
            __RequestVerificationToken = Get-AntiforgeryToken $suspended.Body
        }
    Assert-Redirect $reactivate $detailPath
    $reactivated = Send-BrowserRequest $existingBrowser 'GET' $detailPath
    if ($reactivated.Status -ne 200 -or
        $reactivated.Body.IndexOf('Active', [StringComparison]::Ordinal) -lt 0 -or
        $reactivated.Body.IndexOf('Suspend card', [StringComparison]::Ordinal) -lt 0 -or
        $reactivated.Body.IndexOf('Card reactivated', [StringComparison]::Ordinal) -lt 0) {
        throw 'CARD-002 reactivate did not reload the backend state and lifecycle history.'
    }

    $paymentPath = "$detailPath/pay"
    $paymentPage = Send-BrowserRequest $existingBrowser 'GET' $paymentPath
    if ($paymentPage.Status -ne 200 -or
        $paymentPage.Body.IndexOf('Generate payment code', [StringComparison]::Ordinal) -lt 0 -or
        $paymentPage.Body.IndexOf('data:image/png;base64,', [StringComparison]::Ordinal) -ge 0) {
        throw 'CARD-006 safe GET did not show the explicit generation step.'
    }
    $paymentResult = Send-BrowserRequest `
        $existingBrowser `
        'POST' `
        $paymentPath `
        @{ __RequestVerificationToken = Get-AntiforgeryToken $paymentPage.Body }
    $numericPaymentCode = [regex]::Match(
        $paymentResult.Body,
        '<output[^>]*id="payment-number"[^>]*>([0-9]{4} [0-9]{4} [0-9]{4})</output>',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )
    if ($paymentResult.Status -ne 200 -or
        $paymentResult.Body.IndexOf('data:image/png;base64,', [StringComparison]::Ordinal) -lt 0 -or
        !$numericPaymentCode.Success -or
        $paymentResult.Body.IndexOf('rawToken', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $paymentResult.Body.IndexOf('accessToken', [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw 'CARD-006 did not render the real one-time QR/numeric credential safely.'
    }

    $sharePath = "$detailPath/share"
    $sharePage = Send-BrowserRequest $existingBrowser 'GET' $sharePath
    if ($sharePage.Status -ne 200 -or
        $sharePage.Body.IndexOf('75.00 TRY', [StringComparison]::Ordinal) -lt 0 -and
        $sharePage.Body.IndexOf('100.00 TRY', [StringComparison]::Ordinal) -lt 0) {
        throw 'CARD-004 share creation did not render backend-authored available value.'
    }
    $protectedCreate = Send-BrowserRequest `
        $existingBrowser `
        'POST' `
        "$sharePath`?handler=Protected" `
        @{
            ProtectedAmount = '20.00'
            ProtectedIdempotencyKey = Get-HiddenInputValue $sharePage.Body 'ProtectedIdempotencyKey'
            __RequestVerificationToken = Get-AntiforgeryToken $sharePage.Body
        }
    if ($protectedCreate.Status -ne 200) {
        throw "Protected share creation returned $($protectedCreate.Status)."
    }
    $protectedClaimUrl = Get-ElementValueById $protectedCreate.Body 'claim-url'
    $protectedPin = Get-ElementValueById $protectedCreate.Body 'share-pin'
    if ($protectedCreate.Body.IndexOf('These values are shown once.', [StringComparison]::Ordinal) -lt 0 -or
        $protectedPin.Length -ne 6) {
        throw 'Protected share creation did not present the one-time link and separate PIN.'
    }

    # Phase 3 exit: reserved and available value. Creating a share reserves
    # immediately (ADR-015) without posting to the Ledger, so the source card
    # must now show the reservation and a correspondingly reduced available
    # figure while its posted balance is unchanged.
    $sourceReference = Get-CardReference $reactivated.Body
    $reservedDetail = Send-BrowserRequest $existingBrowser 'GET' $detailPath
    if ($reservedDetail.Status -ne 200) {
        throw "Card detail returned $($reservedDetail.Status) while a share was pending."
    }
    $postedValue = Get-DefinitionAmount $reservedDetail.Body 'Posted balance'
    $reservedValue = Get-DefinitionAmount $reservedDetail.Body 'Reserved'
    $availableValue = Get-DefinitionAmount $reservedDetail.Body 'Available to use or share'
    if ($reservedValue -lt 20.00) {
        throw "A pending 20.00 share did not reserve value; reserved shows $reservedValue."
    }
    if (($postedValue - $reservedValue) -ne $availableValue) {
        throw "Backend value components are incoherent: posted $postedValue minus reserved $reservedValue is not available $availableValue."
    }

    $protectedArrival = Send-BrowserRequest $newBrowser 'GET' $protectedClaimUrl
    Assert-Redirect $protectedArrival '/share/claim/confirm'
    $protectedClaim = Send-BrowserForm $newBrowser '/share/claim/confirm' @{ Pin = $protectedPin }
    Assert-Redirect $protectedClaim '/shares?Direction=Received&State=Claimed'
    $receivedShares = Send-BrowserRequest $newBrowser 'GET' '/shares?Direction=Received&State=Claimed'
    if ($receivedShares.Status -ne 200 -or
        $receivedShares.Body.IndexOf('20.00 TRY', [StringComparison]::Ordinal) -lt 0 -or
        $receivedShares.Body.IndexOf('Claimed', [StringComparison]::Ordinal) -lt 0) {
        throw 'Protected share claim did not appear in backend-authored received history.'
    }

    # Phase 3 exit: child lineage. A successful claim creates a separately owned
    # child card rather than transferring the source, so the recipient must now
    # hold a card carrying the shared amount whose public reference is not the
    # sender's card.
    $recipientCards = Send-BrowserRequest $newBrowser 'GET' '/cards'
    if ($recipientCards.Status -ne 200 -or
        $recipientCards.Body.IndexOf('20.00 TRY', [StringComparison]::Ordinal) -lt 0) {
        throw 'The claimed share did not produce a recipient-owned child card.'
    }
    if ($recipientCards.Body.IndexOf($sourceReference, [StringComparison]::Ordinal) -ge 0) {
        throw "The recipient can see the sender's source card $sourceReference; the child must be a separate card."
    }

    # Phase 3 exit: transfer posts only at claim (ADR-015). The source card's
    # posted balance drops by the shared amount and its reservation is released,
    # so available must equal the new posted value.
    $afterClaimDetail = Send-BrowserRequest $existingBrowser 'GET' $detailPath
    $postedAfterClaim = Get-DefinitionAmount $afterClaimDetail.Body 'Posted balance'
    $reservedAfterClaim = Get-DefinitionAmount $afterClaimDetail.Body 'Reserved'
    $availableAfterClaim = Get-DefinitionAmount $afterClaimDetail.Body 'Available to use or share'
    if ($postedAfterClaim -ne ($postedValue - 20.00)) {
        throw "Claim did not post the transfer; posted moved from $postedValue to $postedAfterClaim."
    }
    if ($reservedAfterClaim -ne ($reservedValue - 20.00)) {
        throw "Claim did not release the reservation; reserved moved from $reservedValue to $reservedAfterClaim."
    }
    if (($postedAfterClaim - $reservedAfterClaim) -ne $availableAfterClaim) {
        throw 'Backend value components are incoherent after claim.'
    }

    $replayArrival = Send-BrowserRequest $newBrowser 'GET' $protectedClaimUrl
    Assert-Redirect $replayArrival '/share/claim/confirm'
    $replay = Send-BrowserForm $newBrowser '/share/claim/confirm' @{ Pin = $protectedPin }
    if ($replay.Status -ne 200 -or
        $replay.Body.IndexOf('already been used', [StringComparison]::Ordinal) -lt 0) {
        throw 'Protected share replay did not fail safely.'
    }

    $lockPage = Send-BrowserRequest $existingBrowser 'GET' $sharePath
    $lockCreate = Send-BrowserRequest `
        $existingBrowser `
        'POST' `
        "$sharePath`?handler=Protected" `
        @{
            ProtectedAmount = '5.00'
            ProtectedIdempotencyKey = Get-HiddenInputValue $lockPage.Body 'ProtectedIdempotencyKey'
            __RequestVerificationToken = Get-AntiforgeryToken $lockPage.Body
        }
    $lockUrl = Get-ElementValueById $lockCreate.Body 'claim-url'
    $lockArrival = Send-BrowserRequest $newBrowser 'GET' $lockUrl
    Assert-Redirect $lockArrival '/share/claim/confirm'
    foreach ($attempt in 1..5) {
        $wrongPin = Send-BrowserForm $newBrowser '/share/claim/confirm' @{ Pin = '000000' }
        if ($wrongPin.Status -ne 200 -or
            $wrongPin.Body.IndexOf('not valid', [StringComparison]::Ordinal) -lt 0) {
            throw "Wrong-PIN attempt $attempt did not fail with safe copy."
        }
    }
    $lockedShares = Send-BrowserRequest $existingBrowser 'GET' '/shares?Direction=Sent&State=Locked'
    if ($lockedShares.Status -ne 200 -or
        $lockedShares.Body.IndexOf('Locked', [StringComparison]::Ordinal) -lt 0) {
        throw 'The fifth wrong PIN did not produce backend-authored Locked history.'
    }

    $cancelPage = Send-BrowserRequest $existingBrowser 'GET' $sharePath
    $cancelCreate = Send-BrowserRequest `
        $existingBrowser `
        'POST' `
        "$sharePath`?handler=Protected" `
        @{
            ProtectedAmount = '5.00'
            ProtectedIdempotencyKey = Get-HiddenInputValue $cancelPage.Body 'ProtectedIdempotencyKey'
            __RequestVerificationToken = Get-AntiforgeryToken $cancelPage.Body
        }
    if ($cancelCreate.Status -ne 200) {
        throw 'The cancellable protected share was not created.'
    }
    $pendingShares = Send-BrowserRequest $existingBrowser 'GET' '/shares?Direction=Sent&State=Pending'
    $cancel = Send-BrowserRequest `
        $existingBrowser `
        'POST' `
        '/shares?handler=Cancel' `
        @{
            ShareId = Get-HiddenInputValue $pendingShares.Body 'ShareId'
            IdempotencyKey = Get-HiddenInputValue $pendingShares.Body 'IdempotencyKey'
            Direction = 'Sent'
            State = 'Pending'
            __RequestVerificationToken = Get-AntiforgeryToken $pendingShares.Body
        }
    Assert-Redirect $cancel '/shares?Direction=Sent&State=Pending'
    $cancelledShares = Send-BrowserRequest $existingBrowser 'GET' '/shares?Direction=Sent&State=Cancelled'
    if ($cancelledShares.Status -ne 200 -or
        $cancelledShares.Body.IndexOf('Cancelled', [StringComparison]::Ordinal) -lt 0) {
        throw 'Sender cancellation did not appear in backend-authored history.'
    }

    # Phase 3 exit: share expiry releases the reservation without posting value.
    # ADR-016 fixes the TTL at exactly 24 hours and the Sharing module validates
    # that, so expiry cannot be reached by configuration inside a test run. The
    # harness instead ages one pending share in the disposable database - moving
    # created and expires by the same amount, so the 24-hour TTL invariant and
    # its check constraint both still hold - and then lets the backend's own
    # ShareExpirationWorker sweep it. Ageing needs the share-identity trigger off
    # for exactly that one statement; it is restored immediately, so the rest of
    # the run still exercises it.
    $ledgerTransactionsBeforeExpiry = Invoke-PsqlScalar `
        $backendDatabase `
        $administrator `
        $administratorPassword `
        'SELECT count(*) FROM ledger.transactions;'
    $expiryBaseline = Send-BrowserRequest $existingBrowser 'GET' $detailPath
    $expiryBaselinePosted = Get-DefinitionAmount $expiryBaseline.Body 'Posted balance'
    $expiryBaselineReserved = Get-DefinitionAmount $expiryBaseline.Body 'Reserved'

    $expiryPage = Send-BrowserRequest $existingBrowser 'GET' $sharePath
    $expiryCreate = Send-BrowserRequest `
        $existingBrowser `
        'POST' `
        "$sharePath`?handler=Protected" `
        @{
            ProtectedAmount = '5.00'
            ProtectedIdempotencyKey = Get-HiddenInputValue $expiryPage.Body 'ProtectedIdempotencyKey'
            __RequestVerificationToken = Get-AntiforgeryToken $expiryPage.Body
        }
    if ($expiryCreate.Status -ne 200) {
        throw 'The expiring protected share was not created.'
    }
    $expiryPending = Send-BrowserRequest $existingBrowser 'GET' '/shares?Direction=Sent&State=Pending'
    $expiryShareId = Get-HiddenInputValue $expiryPending.Body 'ShareId'
    $null = [Guid]::Parse($expiryShareId)
    $pendingExpiryDetail = Send-BrowserRequest $existingBrowser 'GET' $detailPath
    $reservedWhilePending = Get-DefinitionAmount $pendingExpiryDetail.Body 'Reserved'
    if (($reservedWhilePending - $expiryBaselineReserved) -ne 5.00) {
        throw "The share awaiting expiry did not reserve 5.00; reserved moved from $expiryBaselineReserved to $reservedWhilePending."
    }

    $ageShareSql = @"
ALTER TABLE sharing.shares DISABLE TRIGGER sharing_share_identity_immutable;
UPDATE sharing.shares
   SET created_at_utc = created_at_utc - interval '48 hours',
       expires_at_utc = expires_at_utc - interval '48 hours'
 WHERE id = '$expiryShareId';
ALTER TABLE sharing.shares ENABLE TRIGGER sharing_share_identity_immutable;
"@
    Invoke-Psql $backendDatabase $administrator $administratorPassword $ageShareSql

    $expiryDeadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    $expiredState = ''
    while ([DateTimeOffset]::UtcNow -lt $expiryDeadline) {
        $expiredState = Invoke-PsqlScalar `
            $backendDatabase `
            $administrator `
            $administratorPassword `
            "SELECT state FROM sharing.shares WHERE id = '$expiryShareId';"
        if ($expiredState -eq 'Expired') {
            break
        }
        Start-Sleep -Seconds 2
    }
    if ($expiredState -ne 'Expired') {
        throw "ShareExpirationWorker did not expire the aged share within 90 seconds; its state is '$expiredState'."
    }

    $expiredShares = Send-BrowserRequest $existingBrowser 'GET' '/shares?Direction=Sent&State=Expired'
    if ($expiredShares.Status -ne 200 -or
        $expiredShares.Body.IndexOf('Expired', [StringComparison]::Ordinal) -lt 0 -or
        $expiredShares.Body.IndexOf('5.00 TRY', [StringComparison]::Ordinal) -lt 0) {
        throw 'Worker-driven expiry did not appear in backend-authored sent history.'
    }
    $releasedDetail = Send-BrowserRequest $existingBrowser 'GET' $detailPath
    $postedAfterExpiry = Get-DefinitionAmount $releasedDetail.Body 'Posted balance'
    $reservedAfterExpiry = Get-DefinitionAmount $releasedDetail.Body 'Reserved'
    if ($reservedAfterExpiry -ne $expiryBaselineReserved) {
        throw "Expiry did not release the reservation; reserved is $reservedAfterExpiry, expected $expiryBaselineReserved."
    }
    if ($postedAfterExpiry -ne $expiryBaselinePosted) {
        throw "Expiry moved posted value; posted is $postedAfterExpiry, expected $expiryBaselinePosted."
    }
    $ledgerTransactionsAfterExpiry = Invoke-PsqlScalar `
        $backendDatabase `
        $administrator `
        $administratorPassword `
        'SELECT count(*) FROM ledger.transactions;'
    if ($ledgerTransactionsAfterExpiry -ne $ledgerTransactionsBeforeExpiry) {
        throw "Share expiry posted to the Ledger; transaction count moved from $ledgerTransactionsBeforeExpiry to $ledgerTransactionsAfterExpiry."
    }

    $senderLogin = Invoke-RestMethod `
        -Method Post `
        -Uri "$api/auth/login" `
        -ContentType 'application/json' `
        -Body (@{
            email = $existingRecipientEmail
            password = $existingRecipientPassword
        } | ConvertTo-Json)
    $senderAuthorization = @{ Authorization = "Bearer $($senderLogin.accessToken)" }

    $directNewPage = Send-BrowserRequest $existingBrowser 'GET' $sharePath
    $directNewCreate = Send-BrowserRequest `
        $existingBrowser `
        'POST' `
        "$sharePath`?handler=Direct" `
        @{
            DirectAmount = '10.00'
            RecipientContactType = 'Email'
            RecipientContact = $directNewRecipientEmail
            DirectIdempotencyKey = Get-HiddenInputValue $directNewPage.Body 'DirectIdempotencyKey'
            __RequestVerificationToken = Get-AntiforgeryToken $directNewPage.Body
        }
    if ($directNewCreate.Status -ne 200 -or
        $directNewCreate.Body.IndexOf('The invitation was created for', [StringComparison]::Ordinal) -lt 0 -or
        $directNewCreate.Body.IndexOf($directNewRecipientEmail, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw 'The new-recipient direct invitation did not render only the masked contact.'
    }
    $directNewPending = Send-BrowserRequest `
        $existingBrowser `
        'GET' `
        '/shares?Direction=Sent&Kind=DirectInvitation&State=Pending'
    $directNewShareId = Get-HiddenInputValue $directNewPending.Body 'ShareId'
    $directNewDelivery = Invoke-RestMethod `
        -Method Get `
        -Uri "$api/me/shares/$directNewShareId/development-delivery" `
        -Headers $senderAuthorization

    $directNewBrowser = New-Browser "http://127.0.0.1:$cardholderPort/"
    $directNewArrival = Send-BrowserRequest $directNewBrowser 'GET' $directNewDelivery.claimUrl
    Assert-Redirect $directNewArrival '/activate/share/confirm'
    $directNewProbe = Send-BrowserForm $directNewBrowser '/activate/share/confirm' @{}
    Assert-Redirect $directNewProbe '/activate/share/password'
    $directNewClaim = Send-BrowserForm $directNewBrowser '/activate/share/password' @{
        Password = $directNewRecipientPassword
        ConfirmPassword = $directNewRecipientPassword
    }
    Assert-Redirect $directNewClaim '/cards'
    $directNewCards = Send-BrowserRequest $directNewBrowser 'GET' '/cards'
    if ($directNewCards.Status -ne 200 -or
        $directNewCards.Body.IndexOf('10.00 TRY', [StringComparison]::Ordinal) -lt 0 -or
        $directNewCards.Body.IndexOf('The shared value is now on your gift card', [StringComparison]::Ordinal) -lt 0) {
        throw 'The new-identity direct invitation did not create a session and land on its card.'
    }

    $directExistingPage = Send-BrowserRequest $existingBrowser 'GET' $sharePath
    $directExistingCreate = Send-BrowserRequest `
        $existingBrowser `
        'POST' `
        "$sharePath`?handler=Direct" `
        @{
            DirectAmount = '10.00'
            RecipientContactType = 'Email'
            RecipientContact = $directExistingRecipientEmail
            DirectIdempotencyKey = Get-HiddenInputValue $directExistingPage.Body 'DirectIdempotencyKey'
            __RequestVerificationToken = Get-AntiforgeryToken $directExistingPage.Body
        }
    if ($directExistingCreate.Status -ne 200 -or
        $directExistingCreate.Body.IndexOf($directExistingRecipientEmail, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw 'The existing-recipient direct invitation exposed the raw contact.'
    }
    $directExistingPending = Send-BrowserRequest `
        $existingBrowser `
        'GET' `
        '/shares?Direction=Sent&Kind=DirectInvitation&State=Pending'
    $directExistingShareId = Get-HiddenInputValue $directExistingPending.Body 'ShareId'
    $directExistingDelivery = Invoke-RestMethod `
        -Method Get `
        -Uri "$api/me/shares/$directExistingShareId/development-delivery" `
        -Headers $senderAuthorization

    $directExistingBrowser = New-Browser "http://127.0.0.1:$cardholderPort/"
    $directExistingArrival = Send-BrowserRequest `
        $directExistingBrowser `
        'GET' `
        $directExistingDelivery.claimUrl
    Assert-Redirect $directExistingArrival '/activate/share/confirm'
    $directExistingClaim = Send-BrowserForm $directExistingBrowser '/activate/share/confirm' @{}
    Assert-Redirect $directExistingClaim '/signin'
    $directExistingSignInPage = Send-BrowserRequest $directExistingBrowser 'GET' '/signin'
    if ($directExistingSignInPage.Body.IndexOf('Your gift card is ready.', [StringComparison]::Ordinal) -lt 0 -or
        $directExistingSignInPage.Body.IndexOf($directExistingRecipientEmail, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw 'The existing-account direct claim did not preserve the masked sign-in handoff.'
    }
    $directExistingSignIn = Send-BrowserForm $directExistingBrowser '/signin' @{
        Identifier = $directExistingRecipientEmail
        Password = $directExistingRecipientPassword
    }
    Assert-Redirect $directExistingSignIn '/cards'
    $directExistingCards = Send-BrowserRequest $directExistingBrowser 'GET' '/cards'
    if ($directExistingCards.Status -ne 200 -or
        $directExistingCards.Body.IndexOf('10.00 TRY', [StringComparison]::Ordinal) -lt 0) {
        throw 'The existing-account direct claim was not visible after normal sign-in.'
    }

    $sessionCount = Invoke-PsqlScalar `
        $sessionDatabase `
        $sessionApp `
        $sessionPassword `
        'SELECT count(*) FROM cardholder_sessions;'
    if ($sessionCount -ne '4') {
        throw "Expected four server-side cardholder sessions; found '$sessionCount'."
    }
    Write-Output 'CARD-001 activation, CARD-002 lifecycle, CARD-004/005 sharing, and CARD-006 payment presentation E2E passed against the real backend.'
}
finally {
    if ($null -ne $newBrowser) { $newBrowser.Dispose() }
    if ($null -ne $existingBrowser) { $existingBrowser.Dispose() }
    if ($null -ne $directNewBrowser) { $directNewBrowser.Dispose() }
    if ($null -ne $directExistingBrowser) { $directExistingBrowser.Dispose() }
    Stop-OwnedProcess $cardholderProcess
    Stop-OwnedProcess $backendProcess

    if (!$KeepDatabases -and
        ![string]::IsNullOrWhiteSpace($administrator) -and
        ![string]::IsNullOrWhiteSpace($administratorPassword)) {
        $cleanupSql = @"
SELECT pg_terminate_backend(pid)
FROM pg_stat_activity
WHERE datname IN ('$backendDatabase', '$sessionDatabase')
  AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS $backendDatabase;
DROP DATABASE IF EXISTS $sessionDatabase;
DROP ROLE IF EXISTS $backendApp;
DROP ROLE IF EXISTS $backendMigrator;
DROP ROLE IF EXISTS $sessionApp;
"@
        Invoke-Psql 'postgres' $administrator $administratorPassword $cleanupSql
    }
}
