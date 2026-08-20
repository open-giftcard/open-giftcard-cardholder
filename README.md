# open-giftcard-cardholder

Open Giftcard's smartphone-first recipient application.

This is the app a person opens when their company sends them a digital gift
card: they follow the activation link, activate the card, and see its balance.

CARD-001 through CARD-006 deliver activation, sign-in by email or phone,
Ledger-derived owned-card balances/history/lifecycle, and recipient sharing.
An owner can create a protected link or direct email/phone invitation, review
sent/received state, cancel a pending share, claim into a separate child card,
and present a 60-second QR or grouped numeric payment code without handling a
UUID or backend token. English is the first and deterministic default language;
the complete journey can be switched to Turkish, and the language catalogue is
designed to accept additional complete translations without changing the menu.
CARD-007 adds reseller e-pin activation at `/epin`: link plus PIN can create a
new account or, after sign-in, attach to the buyer's existing exact identity.

The project is an open reference implementation, not a hosted card program or a
claim of production certification. Start with the
[architecture](docs/ARCHITECTURE.md), [decisions](docs/DECISIONS.md),
[deployment contract](docs/DEPLOYMENT.md),
[production-readiness matrix](docs/PRODUCTION_READINESS.md), and
[public publishing workflow](docs/PUBLISHING.md).

## Architecture

- ASP.NET Core 10 Razor Pages, server-rendered, with hand-written CSS — a page
  is a few kilobytes on mobile data. A deployment may opt into a small
  same-origin progressive-enhancement module; every journey remains complete
  without JavaScript.
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

### Optional JavaScript enhancements

JavaScript enhancements are disabled by default. An operator can enable them
without rebuilding:

```text
Ui__EnableJavaScriptEnhancements=true
```

Enabled mode adds smoother page and menu transitions, closes disclosure menus
on outside click or Escape, prevents accidental duplicate form submissions,
and keeps expanded card details in view on small screens. It does not fetch
business data or move activation secrets, payment credentials, backend tokens,
authorization, or financial decisions into the browser. The Content Security
Policy changes only from `script-src 'none'` to `script-src 'self'`; inline,
evaluated, and third-party scripts remain blocked.

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

Open `http://localhost:5180`. The application creates its three tables on startup.
Set `Backend:BaseUrl` if the backend is not on `http://localhost:5143`.

Production must supply a durable, protected `DataProtection:KeysPath` and serve
over HTTPS so the `__Host-` prefixed session cookie is accepted; the Development
profile deliberately permits local HTTP.

Outside Development the application also requires an HTTPS backend URL and
secure `__Host-` names for both cookies. `/health` reports process liveness and
`/health/ready` verifies the cardholder-owned PostgreSQL tables.

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for every required production
setting and [docs/PRODUCTION_READINESS.md](docs/PRODUCTION_READINESS.md) for the
line between implemented controls and operator responsibilities.

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
