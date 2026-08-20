# Cardholder Architecture

## System boundary

The cardholder application and the backend are separate repositories. This
application consumes the backend only through the pinned OpenAPI contract. It
does not reproduce authorization, tenancy, ownership, or financial rules, and it
is never an authority for any of them.

```text
Browser  (complete server-rendered HTML; optional same-origin enhancements)
  | same-origin opaque session cookie + antiforgery
  v
GiftCardCardholder.Web   (Razor Pages; the UI and the BFF are one process)
  | Authorization: Bearer <access token held server-side>
  v
Open Giftcard API        (/api/v1, bearer only)

Web -> cardholder-owned PostgreSQL (sessions, activation contexts, and
                                      short-lived payment presentations)
```

Unlike the portal, there is no separate BFF project: a server-rendered
application already keeps every token on the server (ADR-CARD-002).

## Request flow

```text
Pages/*.cshtml.cs
  -> CardholderSessionManager     session, refresh, activation context
  -> BackendClient                typed calls over the pinned contract
       -> ICardholderSessionStore PostgreSQL
```

No page performs arithmetic on money, decides whether a card is usable, or infers
what a recipient is allowed to do. Those answers are read from the backend on
every request.

## Localization

ASP.NET Core request localization reads one ordered language catalogue. English
(`en`) is first and is the deterministic default; Turkish (`tr`) is the second
complete translation. Browser `Accept-Language` and query-string culture are
ignored. A same-origin antiforgery-protected POST writes a catalogue-allowlisted
culture to an `HttpOnly`, `SameSite=Lax` preference cookie and redirects only to
a local URL. The menu renders every catalogue entry, so adding a complete
resource file does not require redesigning a two-language control.

Razor pages and server-generated safe messages share one resource set. Dates and
decimal separators use the selected culture, while backend decimal values, ISO
currency codes, identifiers, event order, and lifecycle decisions remain
unchanged. Culture is never inferred from identity, organization, phone number,
or card currency.

## Journeys

### Activation

```text
GET  /activate?token=…    validate shape, store secret server-side, redirect
GET  /activate/confirm    explain, and offer one button
POST /activate/confirm    claim with no password
                            400 user.password.required -> /activate/password
                            200, session null          -> /signin  (existing account)
GET  /activate/password   create-password form
POST /activate/password   claim with password
                            200, session present       -> /cards   (signed in)
```

A `GET` never claims, because link previews prefetch URLs (ADR-CARD-004). The
password branch is decided by probing rather than guessing (ADR-CARD-005).

Reseller e-pins use a separate `/epin?token=…` entry and `/epin/claim` form but
the same encrypted, purpose-bound activation store. The form always requires
the separately delivered six-digit PIN. A signed-in buyer sends the current
server-held bearer token and the backend attaches only to that identity. An
anonymous buyer supplies email/phone plus password for a new identity; if it
already exists, the backend requires sign-in first. No GET consumes a claim,
and token, PIN, password, or bearer credential is rendered into HTML or browser
storage.

The claim response carries an optional `session` token pair (backend IMPL-019),
populated only when the claim created the recipient identity. That token pair is
consumed server-side and becomes the normal `HttpOnly` session, so a new
recipient never sees a sign-in form (ADR-CARD-009). An existing account
deliberately receives no session and still ends at sign-in with the masked
identifier, so possession of one invitation cannot authenticate an account that
may hold other cards.

Outbound claim and login requests set `X-Forwarded-For` to the address observed
on the browser connection, overwriting anything the browser sent, so the
backend's per-source quotas partition per recipient (ADR-CARD-010).

### Session

```text
GET  /signin    form (redirects to /cards when already signed in)
POST /signin    auth/login + me -> server-side session -> /cards
GET  /cards     me/gift-cards, Ledger-derived balances
POST /signout   backend revoke, delete session, clear cookie
```

### Card detail and lifecycle

```text
GET  /cards/{id}              exact-owner detail + combined history
GET  /cards/{id}?cursor=...   next backend-ordered history page
POST /cards/{id}?handler=Suspend
POST /cards/{id}?handler=Reactivate
                               antiforgery + per-form idempotency
                               -> redirect -> authoritative GET
```

Card identifiers reach the route only through an owned-card list response; the
recipient never pastes one. A missing and a non-owned card share the same 404
experience. History cursors remain opaque and are forwarded unchanged. The
application may choose which lifecycle form to display from the latest backend
state, but the backend alone decides ownership and transition validity. Command
responses are not converted into local state: after every accepted command the
redirect reloads exact-owner detail and history from the backend.

### Recipient sharing

```text
GET/POST /cards/{id}/share       exact-owner protected/direct creation
POST success, protected          show raw link + PIN once; never persist them
GET      /shares                 backend-filtered Sent/Received history
POST     /shares?handler=Cancel  cancel backend-returned pending share

GET  /share/claim?token=...      encrypt token server-side; clean redirect
POST /share/claim/confirm        signed-in recipient + six-digit PIN

GET  /activate/share?token=...   encrypt direct token server-side; clean redirect
POST /activate/share/confirm     passwordless probe
POST /activate/share/password    new identity only; consume returned session
```

The create page displays posted, reserved, and available values exactly as the
backend returns them. It never subtracts a reservation locally. Protected raw
credentials exist only in the successful POST response, covered by `no-store`;
there is deliberately no redirect cache or JavaScript copy state from which
they could be recovered. Direct creation renders only the backend-masked
contact.

All incoming claim tokens share the encrypted activation table but carry an
explicit Distribution, ProtectedShare, or DirectShare purpose. Loading a token
from the wrong route deletes the context and fails closed. Generic claim also
requires the normal server-side authenticated session; sign-in accepts only a
same-origin local return URL so it can resume the clean confirmation route.
The backend alone records PIN attempts, lock/expiry, claim/cancel validity,
reservation release, transfer, and child lineage.

### Checkout presentation

```text
GET  /cards/{id}/pay   authenticated explanation; issues nothing
POST /cards/{id}/pay   antiforgery + server-side bearer
                         -> backend exact-owner payment-token issuance
                         -> QR PNG data URI + grouped numeric code shown once
```

The POST response is already `no-store`. The raw presentations are encrypted
with Data Protection in the cardholder database only for their 60-second
lifetime and bound to the issuing session; they never enter a URL, TempData,
logs, analytics, or browser storage. QRCoder turns the opaque token into PNG
bytes without interpreting it; the numeric code is accepted for display only
when it is exactly 12 ASCII digits. The app does not issue credentials on GET
or a timer, and it does not decide card eligibility, expiry, replay, ownership,
available value, or payment effects.

## Session model

* The browser holds one opaque 256-bit cookie value; only its SHA-256 hash is
  stored.
* Backend access and refresh tokens are encrypted with Data Protection before
  they reach the database.
* The access token is refreshed 60 seconds before expiry, serialized per session
  so a rotation is never attempted twice concurrently.
* A refresh the backend rejects, an undecryptable payload, and a failed write
  after a consumed rotation all end the session rather than leaving a recipient
  holding a cookie that cannot work.
* Cookies are `HttpOnly`, `SameSite=Lax`, path `/`, and `Secure` with the
  `__Host-` prefix outside Development. `Lax` is required: recipients arrive by
  following a link from an email or messaging app.

## Security posture

* Razor Pages validates antiforgery tokens on every POST automatically.
* `Content-Security-Policy` sets `script-src 'none'` by default and
  `frame-ancestors 'none'`. When an operator enables the presentation-only
  enhancement module, `script-src` becomes `'self'`; inline, evaluated, and
  third-party scripts remain blocked.
* `Referrer-Policy: no-referrer` keeps an activation secret in a URL from
  leaking outward.
* Every response is `no-store`: each page is recipient-specific.
* Recipient-facing errors are mapped from backend problem codes and never
  disclose whether an invitation or account exists.
* Passwords and claim secrets are never logged, persisted by this application,
  placed in TempData, or rendered into a page.
* The culture cookie contains only `en` or `tr`; it is not a session or an
  authorization input.
* Distribution and both sharing claim contexts are encrypted and purpose-bound;
  a GET never claims or increments a protection attempt.

## Browser verification boundary

The deployed application is complete without JavaScript. An operator may enable
the small same-origin module for disclosure-menu behavior, duplicate-submit
feedback, and presentation transitions; it fetches no business data and holds
no credentials. Playwright and axe under `browser-tests/` launch Firefox,
Chromium, and mobile Chromium. Authenticated forms and secret routing are
exercised through the real Razor pipeline by .NET journey tests and the guarded
disposable-PostgreSQL E2E runner. A development-only setting skips the session
maintenance worker for the disposable browser host; production ignores it.

## Deployment

One process serving HTML and calling the backend. It requires a PostgreSQL
database of its own and a persisted Data Protection key location. The backend
stays bearer-only and needs no CORS relaxation, because every backend call is
server-to-server.

Outside Development the process fails startup unless backend transport is
HTTPS, both cookies are secure `__Host-` cookies, and the key path is explicit.
It processes one forwarded address/proto hop only from a literal allowlisted
ingress; when no proxy is configured, forwarded-header middleware is absent.
`/health` is process liveness and `/health/ready` checks all three tables.
See `docs/DEPLOYMENT.md` for the exact configuration and promotion checklist.

`Distribution:ClaimBaseUrl` on the backend must point at this application's
`/activate` route so activation links resolve here, and this application's
address must appear in the backend's
`Networking:ForwardedHeaders:KnownProxies` list for per-recipient claim
quotas. Without the allowlist entry the backend ignores the forwarded address
and falls back to the direct remote address — weaker partitioning, never a
trust escalation.
