# open-giftcard-cardholder

Gift Card smartphone-first cardholder application.

This is the app a person opens when their company sends them a digital gift
card: they follow the activation link, activate the card, and see its balance.

CARD-001 through CARD-006 deliver activation, sign-in by email or phone,
Ledger-derived owned-card balances/history/lifecycle, and recipient sharing.
An owner can create a protected link or direct email/phone invitation, review
sent/received state, cancel a pending share, claim into a separate child card,
and present a 60-second QR or grouped numeric payment code without handling a
UUID or backend token. English is the default; the
complete journey can be switched to Turkish.
CARD-007 adds reseller e-pin activation at `/epin`: link plus PIN can create a
new account or, after sign-in, attach to the buyer's existing exact identity.

Contributor documentation is being rewritten for this release and is not
published yet. Until it lands, this README is the authoritative guide.

The staging and production configuration contract is part of the documentation
still to be published.

## Architecture

- ASP.NET Core 10 Razor Pages, server-rendered, with **no JavaScript** and
  hand-written CSS — a page is a few kilobytes on mobile data.
- The application is its own Backend-for-Frontend: the browser receives HTML and
  one opaque session cookie, while backend access and refresh tokens stay
  server-side, encrypted with Data Protection.
- A cardholder-owned PostgreSQL database stores only sessions and short-lived
  activation contexts.
- The authoritative backend is the sibling `open-giftcard` repository, consumed
  through the pinned contract in `contracts/backend.openapi.json`. The exact
  backend commit and its SHA-256 are recorded in `contracts/README.md`.

The backend remains the only authority for authorization, tenancy, ownership,
and financial rules. Nothing in this repository decides any of them.

## Native Windows development

Node.js is not required to develop, build, test, or run the application. Install:

- .NET SDK 10
- PostgreSQL 18, including `psql`

Create the cardholder session database — it prompts for passwords and stores
none:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Setup-LocalDatabase.ps1
```

Start the sibling backend, pointing its activation links here:

```powershell
$env:Distribution__ClaimBaseUrl = "http://localhost:5180/activate"
$env:Networking__ForwardedHeaders__KnownProxies__0 = "127.0.0.1"
dotnet run --project src\GiftCardPlatform.Api
```

Then start this application in another window:

```powershell
$env:ConnectionStrings__Cardholder = "Host=localhost;Port=5432;Database=giftcard_cardholder;Username=giftcard_cardholder_app;Password=<yours>"
dotnet run --project src\GiftCardCardholder.Web
```

Open `http://localhost:5180`. The application creates its two tables on startup.
Set `Backend:BaseUrl` if the backend is not on `http://localhost:5143`.

Production must supply a durable, protected `DataProtection:KeysPath` and serve
over HTTPS so the `__Host-` prefixed session cookie is accepted; the Development
profile deliberately permits local HTTP.

Outside Development the application also requires an HTTPS backend URL and
secure `__Host-` names for both cookies. `/health` reports process liveness and
`/health/ready` verifies the cardholder-owned PostgreSQL tables.

## Verification

```powershell
dotnet build GiftCardCardholder.slnx --configuration Release
dotnet test GiftCardCardholder.slnx
```

The optional reproducible browser accessibility gate needs Node.js 24 and pnpm
11. It starts an anonymous-page-only development host and runs axe, keyboard,
320px, and 200% zoom checks in Firefox, Chromium, and mobile Chromium:

```powershell
cd browser-tests
pnpm install --frozen-lockfile
pnpm test
```

Run activation plus card detail/lifecycle and protected sharing claim, replay,
five-attempt lock, and cancellation against the exact pinned backend and
disposable PostgreSQL databases:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Run-CardholderE2E.ps1 `
  -BackendRepository ..\open-giftcard `
  -BackendEnvironmentFile ..\open-giftcard\.env
```

The runner refuses non-`giftcard_cardholder_e2e_*` database and role names and
cleans up its processes and PostgreSQL objects even when a check fails.

## Production build

```powershell
dotnet publish src\GiftCardCardholder.Web --configuration Release
```

Deploy as one origin. The backend stays bearer-only and needs no CORS
relaxation, because every backend call is made server-to-server.
