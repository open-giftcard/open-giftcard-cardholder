# Cardholder Application Decisions

Durable decisions for the recipient application. Backend decisions live in the
`open-giftcard` repository; nothing here may contradict them. Where a backend
ADR governs, it is cited.

Backend hashes and candidate labels recorded before the public-history squash
are historical design evidence, not public Open Giftcard releases. Current
publishing rules live in `PUBLISHING.md`.

| ID | Decision | Status |
| --- | --- | --- |
| ADR-CARD-001 | Server-rendered Razor Pages with no JavaScript | Amended by ADR-CARD-016 |
| ADR-CARD-002 | The application is its own BFF | Accepted |
| ADR-CARD-003 | Hand-written backend client pinned by contract tests | Accepted |
| ADR-CARD-004 | Activation links are safe to open; claiming needs a POST | Accepted |
| ADR-CARD-005 | The claim probe determines whether a password is needed | Accepted |
| ADR-CARD-006 | No automatic sign-in after activation | Superseded by ADR-CARD-009 |
| ADR-CARD-007 | English is the initial language | Accepted |
| ADR-CARD-008 | A cardholder-owned session database | Accepted |
| ADR-CARD-009 | Sign in from the claim session when the backend issues one | Accepted |
| ADR-CARD-010 | Forward the observed client address on claim requests | Accepted |
| ADR-CARD-011 | Linked detail, authoritative timeline, and POST-only lifecycle | Accepted |
| ADR-CARD-012 | Server-owned locale and test-only browser automation | Accepted |
| ADR-CARD-013 | Fail-closed deployment boundary | Accepted; private tag record superseded |
| ADR-CARD-014 | Purpose-bound server-side sharing journeys and one-time credential display | Accepted |
| ADR-CARD-015 | Explicit POST checkout presentation without JavaScript | Accepted |
| ADR-CARD-016 | Optional presentation-only JavaScript enhancement | Accepted |

---

## ADR-CARD-016 — Optional Presentation-Only JavaScript Enhancement

**Status:** Accepted · **Date:** 2026-08-20

### Context

ADR-CARD-001 chose a complete server-rendered recipient journey to keep mobile
delivery small and the browser credential boundary narrow. Small interaction
improvements do not require a SPA, client-side business state, or weakening
that architectural rule.

### Decision

Keep every journey complete in server-rendered HTML and disable JavaScript by
default. Allow an operator to enable one same-origin ES module for presentation
transitions, disclosure-menu closing, duplicate-submit feedback, and small-screen
scroll positioning. The module may not fetch business data, store credentials,
issue payment tokens, decide authorization or money, or require inline/evaluated
script. Enabled CSP permits only `script-src 'self'`.

### Consequences

* Script-disabled browsers and operators retain the complete application.
* The enhancement is a deployment choice rather than a build fork.
* Security and journey tests cover both CSP modes.
* A future native app, SPA, service worker, or browser-side backend client would
  require a separate decision.

---

## ADR-CARD-015 — Explicit POST Checkout Presentation Without JavaScript

**Status:** Accepted · **Date:** 2026-08-05
**Backend:** IMPL-031 at `3a3d6a4a77336d60f6e0a1e97d8a73298b875ce9`

### Context

The backend now issues one opaque QR credential and one 12-digit numeric alias
for the same 60-second, single-use token. Automatic rotation would require
browser execution and could issue overlapping credentials, while GET issuance
would make refreshes and link previews perform a security-sensitive action.

### Decision

Keep checkout complete without JavaScript and retain `script-src 'none'` in the
default mode. An authenticated GET explains checkout and issues nothing. An
antiforgery-protected POST asks the backend for a credential, renders the opaque
value as a server-generated PNG QR data URI, groups the exact numeric value for
reading, and returns both once in the uncached response. A new credential always
requires another explicit POST. The optional enhancement module does not read,
issue, refresh, or store either presentation.

### Consequences

* Checkout has no JavaScript dependency and GET stays safe.
* The credential is held encrypted in the cardholder database only for its own
  sixty seconds, bound to the issuing session, so reload and language/theme
  changes do not issue overlapping codes. It never enters browser storage,
  TempData, or a URL.
* There is no browser-side authority or automatic rotation. A CSS-only timer
  blurs the QR at expiry and reveals an explicit antiforgery-protected renewal
  POST in its center. PostgreSQL/server time still owns expiry, and an expired
  presentation is generically refused at the POS.
* QRCoder is a presentation-only dependency and creates no authority or state.

---

## ADR-CARD-001 — Server-rendered Razor Pages with no JavaScript

**Status:** Accepted · **Date:** 2026-07-29

### Context

The sibling portal is a React/Vite SPA behind an ASP.NET BFF. The recipient
application has a different audience and a different constraint: it is opened on
a phone, often on mobile data, sometimes at a checkout counter, by someone who
will use it a handful of times a year.

### Decision

Build the recipient application as a server-rendered ASP.NET Core Razor Pages
application with hand-written CSS and **no JavaScript at all**. The Bootstrap and
jQuery assets the project template produced were removed.

### Consequences

* A page is a few kilobytes rather than a bundle; first paint does not wait on
  script download, parse, and hydration.
* `script-src 'none'` is a truthful Content-Security-Policy directive rather than
  an aspiration, which removes a whole class of injection risk.
* Every flow works with scripting unavailable, and validation is server-side by
  necessity rather than by discipline.
* The toolchain is .NET only. This matters concretely: the development machine
  has no Node.js, npm, or pnpm installed.
* Anything genuinely interactive later — the Phase 4 rotating QR code is the
  obvious candidate — needs a deliberate, scoped exception to this decision, not
  an incremental slide into a client framework.

---

## ADR-CARD-002 — The application is its own BFF

**Status:** Accepted · **Date:** 2026-07-29
**Implements:** backend ADR-037

### Context

Backend ADR-037 requires production browser clients to sit behind a same-origin
BFF that keeps rotating refresh tokens server-side, and forbids putting them in
browser-reachable storage. The portal satisfies this with a separate BFF project
in front of an SPA.

### Decision

Because this application is server-rendered, the BFF and the UI are the same
process. The browser receives HTML and one opaque cookie. Backend access and
refresh tokens are held in the cardholder database, encrypted with ASP.NET Core
Data Protection, and attached to backend calls server-side.

### Consequences

* There is no client-side token handling to get wrong — the browser is never
  given a credential it could leak.
* Only a SHA-256 hash of the session cookie is stored, so a database snapshot
  cannot be replayed as a live session.
* Refresh is serialized per session. The backend treats a replayed refresh token
  as a compromise and revokes the family, so two concurrent requests refreshing
  at once would sign the recipient out.
* Data Protection keys must be persisted and shared across instances. Outside
  Development the application refuses to start without `DataProtection:KeysPath`,
  rather than silently using ephemeral keys that log everyone out on deploy.
  *Amended 2026-08-19:* this setting was named `DataProtection:KeyPath` until
  that date. The portal had always called the same thing
  `DataProtection:KeysPath`, so anyone writing one deployment template for both
  clients had to know that two applications with the same requirement spelled it
  differently. The cardholder was renamed to match. There is no fallback to the
  old name: outside Development a stale name now fails at startup naming the
  correct one, which is safer than starting with ephemeral keys.

---

## ADR-CARD-003 — Hand-written backend client pinned by contract tests

**Status:** Accepted · **Date:** 2026-07-29

### Context

The portal generates a client from `contracts/backend.openapi.json` with NSwag,
producing roughly 460,000 characters of generated code covering the whole API.
The cardholder application originally called a small recipient subset and now
adds only the Phase 3 sharing operations it presents.

### Decision

Hand-write a small typed client and bind only the fields the pages use. Pin the
same reviewed OpenAPI document, and add `BackendContractTests`, which assert
that every operation and field the client depends on still exists in it.

### Consequences

* The client is readable, and exactly what is sent to the backend is obvious at
  the call site — which matters for the claim probe.
* Backend drift fails the test suite instead of failing at runtime.
* The pinned document is byte-identical to the portal's snapshot, so both
  independent clients target one reviewed contract.
* If this application ever needs a large share of the API, revisit this.

---

## ADR-CARD-004 — Activation links are safe to open; claiming needs a POST

**Status:** Accepted · **Date:** 2026-07-29

### Context

A recipient receives an activation URL containing a single-use claim secret.
Mail clients, messaging apps, and link scanners routinely prefetch URLs to build
previews. A claim performed during a `GET` would let a preview consume the
invitation before the recipient ever opened it — the card would be claimed by
nobody, and the backend's single-use guarantee would work exactly as designed
against the person it is meant to protect.

### Decision

`GET /activate` never claims. It validates the token's shape, stores the secret
in a short-lived server-side activation context selected by its own opaque
cookie, and redirects to a clean URL. The claim happens only on an
antiforgery-protected `POST` from a button the recipient presses.

### Consequences

* Link previews and scanners cannot burn an invitation.
* The secret leaves the address bar immediately, so it is not shown on screen,
  and `Referrer-Policy: no-referrer` keeps it out of any outbound request.
* The secret is never rendered into the page or placed in a form field; the
  browser holds only the activation cookie.
* One extra redirect and one extra page on the journey. Worth it.

---

## ADR-CARD-005 — The claim probe determines whether a password is needed

**Status:** Accepted · **Date:** 2026-07-29

### Context

`POST /api/v1/gift-card-claims` requires a password only when the delivered
contact has no identity yet. A recipient whose email already has an account
claims without one, and their existing password is left untouched. The client
cannot know which case applies: the backend deliberately reveals nothing about
whether a contact exists until the invitation secret has been verified.

The alternative — always asking for a password — would be actively misleading,
since an existing account's password is ignored.

### Decision

Send the claim once with no password. A `400 user.password.required` identifies
a new recipient and routes to the create-password page. Success means an
existing account claimed the card.

### Consequences

* The probe is safe. Only a wrong *secret* increments the invitation's
  failed-attempt counter; a claim refused for a missing password rolls its
  transaction back and changes nothing.
* A new recipient costs two calls against the backend's claim rate limit,
  which defaults to ten per source IP per minute.
* The shared-source-address risk this decision originally raised was resolved by
  backend IMPL-019 and ADR-CARD-010: the backend now accepts one forwarded
  address from an allowlisted proxy, so the quota partitions per recipient
  again.

---

## ADR-CARD-006 — No automatic sign-in after activation

**Status:** Superseded by ADR-CARD-009 on 2026-07-29 · **Date:** 2026-07-29

### Context

Signing a recipient in straight after activation would be the smoothest journey.
It is not possible with the current contract: the claim response returns
`maskedLoginIdentifier` (`a***@example.com`) and no token pair, so the
application does not learn the address or number needed to call `auth/login`, and
cannot mint a session itself.

Asking the recipient to type their contact on the password page would enable it,
but the masked value is only known *after* the claim, so the field could not be
hinted — inviting a typo that strands them mid-journey.

### Decision

After a successful claim, redirect to sign-in and show the masked identifier the
backend returned, so the recipient knows exactly which address or number to use.

### Consequences

* One extra step at the end of activation, for both new and existing recipients.
* No guessing and no dead ends: the masked hint disambiguates which contact the
  card was sent to.
* Two backend changes would remove the step, and either is worth raising: return
  a token pair from a successful claim, or return the unmasked contact for an
  identity the same request just created.

**Outcome:** the first of those was raised and delivered as backend IMPL-019.
ADR-CARD-009 replaces this decision for new recipients; it still stands for an
existing account, which deliberately receives no session.

---

## ADR-CARD-009 — Sign in from the claim session when the backend issues one

**Status:** Accepted · **Date:** 2026-07-29
**Supersedes:** ADR-CARD-006 (for newly created identities only)
**Backend:** IMPL-019 at `52597cae46f77a9d1d5508392063da0fadf99bc7`

### Context

The claim response now carries an optional `session` token pair. The backend
populates it **only** when the claim created the recipient identity — the case
where the recipient just proved control of the delivered contact and chose the
password in the same request. An existing account claiming a card still gets
`null`, because possessing one invitation must not authenticate an account that
may already hold other cards.

### Decision

When `session` is present, consume the token pair server-side and open the
normal `HttpOnly` session immediately, landing the recipient on their card. When
it is null, keep the previous flow: end at sign-in showing the masked
identifier.

The password probe is unchanged. The probe carries no password, so it can never
create an identity and never returns a session; only the create-password call
can.

### Consequences

* A new recipient goes activation link → activate → choose password → card, with
  no sign-in step. That is the whole journey in three taps.
* The token pair is treated exactly like one from `auth/login`: encrypted at
  rest, never placed in a cookie, URL, log, or page.
* Both branches are covered by tests asserting the redirect target, that a
  session row exists only in the new-identity case, and that neither token value
  appears in any response.
* The two paths are a backend decision, not a client one. This application
  branches on what the response contains and infers nothing else.

---

## ADR-CARD-010 — Forward the observed client address on claim requests

**Status:** Accepted · **Date:** 2026-07-29
**Backend:** IMPL-019 — `Networking:ForwardedHeaders:KnownProxies`, forward limit 1

### Context

The backend rate-limits claims per source address. Every request from this
application arrives from one server address, so without forwarding, all
recipients would share a single ten-per-minute partition — the risk recorded
under ADR-CARD-005.

IMPL-019 lets the backend accept exactly one `X-Forwarded-For` address, and only
when the immediate peer's literal address is allowlisted.

### Decision

On outbound claim requests, **set** `X-Forwarded-For` to the address this
application observed on the browser connection. Never copy, append to, or relay
an incoming value.

### Consequences

* The claim quota partitions per recipient again.
* A browser-supplied forwarding header is discarded, so a caller cannot choose
  which partition to consume or exhaust someone else's. A test asserts this.
* The address comes from `Connection.RemoteIpAddress`, normalized from
  IPv4-mapped IPv6. If this application is itself deployed behind a proxy, it
  must be configured to derive that connection address correctly, or it will
  forward its own upstream's address.
* Deployment requires this application's address in the backend's known-proxy
  list. Without it the backend ignores the header and fails safe to the direct
  remote address — degraded partitioning, never a trust escalation.
* The same rule applies to `auth/login`, whose quota is also source-address
  based. CARD-001 now forwards the observed address for both claim and login;
  forged browser values are covered by regression tests in both journeys.

---

## ADR-CARD-007 — English is the initial language

**Status:** Accepted · **Date:** 2026-07-29

### Decision

Ship English copy first, matching the portal's stance and the backend
documentation.

### Consequences

* Recipients in the original deployment were customers and employees in Turkey, so Turkish is expected
  before any real pilot. Treat this as a deliberate deferral, not a conclusion.
* All user-facing strings live in `.cshtml` files and `ActivationMessages`.
  Introducing resource files later is mechanical; leaving copy scattered through
  page models would not be, which is why messages are centralized now.

---

## ADR-CARD-008 — A cardholder-owned session database

**Status:** Accepted · **Date:** 2026-07-29

### Decision

Sessions live in a PostgreSQL database owned by this application, with its own
non-superuser login. It is never the backend's database and never the portal's.

### Consequences

* This application stores no business, financial, ownership, or authorization
  state — only what is required to keep backend tokens out of the browser. It
  can be dropped and recreated at the cost of signing everyone in again.
* It cannot read or corrupt backend data, and the backend's Row-Level Security
  boundary is unaffected by anything here.
* Two tables, created on startup: `cardholder_sessions` and
  `cardholder_activations`. Expired rows are swept periodically.

---

## ADR-CARD-011 — Linked detail, authoritative timeline, and POST-only lifecycle

**Status:** Accepted · **Date:** 2026-08-01
**Backend:** `main` at `4b204c4034d9140b0fc2813fca135d77cce89780`

### Context

The converged backend exposes three exact-owner capabilities required by the
next recipient journey: card detail with a Ledger-derived balance, a combined
financial/distribution/lifecycle timeline, and suspend/reactivate commands.
Backend ownership RLS deliberately answers a missing card and somebody else's
card the same way.

The application must make those capabilities easy to use without turning a
backend UUID into something the recipient enters, copying lifecycle rules, or
building a second balance/history authority.

### Decision

Cards link from the owned-card list to `/cards/{giftCardId}`. The identifier is
selected from a backend response and never entered by the recipient. The detail
page reads both the exact-owner detail endpoint and the combined history
endpoint on every request. It forwards the backend's opaque cursor unchanged
and renders the returned ordering without merging or recalculating events.

Suspend and reactivate use separate same-origin POST handlers with automatic
antiforgery. Each rendered form carries a random idempotency key; the value is
not a credential or authority and the backend still validates ownership and the
transition. After any result, the page reloads the backend state. No optimistic
lifecycle state is stored locally.

### Consequences

* A recipient never pastes a UUID and cannot select a card outside the
  backend-returned list through normal navigation.
* Direct URL tampering still reaches the backend exact-owner boundary and gets
  the same generic 404 as a missing card; the UI never tries to decide why.
* The page may show the action suggested by the latest returned lifecycle
  state, but that is navigation only. A stale or forged POST is accepted or
  refused solely by the backend.
* Cancel, expire, value return, and all organization/platform commands remain
  absent from the cardholder client.
* The cardholder database remains session-only. It gains no card, balance,
  history, ownership, or lifecycle table.

---

## ADR-CARD-012 — Server-owned locale and test-only browser automation

**Status:** Accepted · **Date:** 2026-08-01

### Context

English was accepted as the first/default language, and Turkish is the first
additional complete translation. More languages should not require changing a
two-language control. The application must keep working without JavaScript.
Structural HTML assertions alone cannot prove behavior in the accepted Firefox
and Chromium targets or run an accessibility engine.

### Decision

ASP.NET Core request localization consumes one ordered catalogue, with English
first and as the deterministic default. Turkish is currently the next complete
entry. A same-origin POST protected by antiforgery writes the standard culture
cookie after catalogue allowlist validation; its return URL must be local. The
menu renders every entry. Culture changes presentation only and never derives
from a tenant, identity, phone number, or currency.

The runtime remains server-rendered with `script-src 'none'` by default. Playwright and axe
are added only under browser tests and run against Firefox, Chromium, and mobile
Chromium. They are verification tools, not application dependencies, and are
excluded from production output.

### Consequences

* English behavior is stable for recipients with no cookie or an unsupported
  browser preference; Turkish is an explicit, reversible choice.
* Dates and decimal separators follow the selected locale, while the backend's
  exact decimal value and ISO currency code remain unchanged.
* The browser receives no new credential-bearing or executable application
  state. The culture cookie is non-sensitive and restricted to supported names.
* Normal application development, build, test, and deployment remain .NET-only.
  Running the reproducible browser accessibility gate additionally needs the
  documented test-only Playwright toolchain.

---

## ADR-CARD-013 — Fail-Closed Deployment Boundary

**Status:** Accepted · **Date:** 2026-08-01

### Context

The recipient application already requires durable Data Protection keys outside
Development, but a non-Development operator could still configure HTTP backend
transport or insecure cookies. Its forwarded client address also needs an
explicit ingress trust boundary before it is safe behind a load balancer.

### Decision

Require HTTPS backend transport, secure `__Host-` cookie names, and the durable
key path outside Development. Process one forwarded address/proto hop only from
literal allowlisted proxy addresses and trust none by default. Keep liveness
process-only; readiness checks only the cardholder-owned session database.

The original private development repositories also used a synchronized
`v0.2.0-rc.1` marker. That marker is historical evidence only and is not a
public Open Giftcard release. Public release governance is now defined by
`docs/PUBLISHING.md`; it never imports the private tags or unrelated history.

### Consequences

* Unsafe production-shaped configuration fails before serving recipients.
* Ingress addresses, public origins, certificates, key storage, PostgreSQL
  endpoints, and secrets remain explicit deployment inputs.
* The browser session, HTML-first runtime, localization, and backend authority
  boundaries do not change.

---

## ADR-CARD-014 — Purpose-Bound Server-Side Sharing Journeys and One-Time Credential Display

**Status:** Accepted · **Date:** 2026-08-02
**Backend:** Phase 3 `main` at `7c9cf169bc5ebe5e3e25025b19f55c47443113f9`

### Context

Phase 3 exposes two deliberately different recipient paths. A protected link
has a one-time secret plus a six-digit PIN and may be claimed only by an
authenticated different recipient. A direct invitation is bound to a verified
email or phone contact and may create the minimum recipient identity. Protected
share credentials cannot be recovered after creation, and link-preview GETs
must never perform a claim.

### Decision

Keep both journeys server-rendered and complete without JavaScript. Protected-link creation
renders the raw link and PIN only in the successful antiforgery-protected POST
response, which is already covered by the application's `no-store` boundary.
The app does not persist those values to implement a redirect or copy button.

When either kind of claim link arrives, validate only its opaque-token shape,
encrypt it into the cardholder-owned activation store with an explicit purpose,
and immediately redirect to a clean URL. Every later page loads only a context
whose purpose matches that route. Generic claim requires an existing server-side
session and a PIN POST. Direct claim reuses the missing-password probe and
new-identity-only session behavior already established for CARD-001.

History filters and card value components are passed to and read from the
backend. The client performs no balance subtraction and does not infer whether
a share is eligible, claimable, cancellable, expired, or locked.

### Consequences

* Link scanners and GET refreshes cannot claim or increment PIN attempts.
* A distribution, protected-share, or direct-share token cannot accidentally
  cross into another client endpoint even though all contexts share the small
  cardholder-owned encrypted store.
* The one-time result cannot support an automatic JavaScript copy button. The
  page instead provides selectable values and clear instructions to send the
  link and PIN separately.
* Refreshing the successful creation POST may produce the backend's safe
  `sharing.credentials.already_issued` conflict; the UI explains that the raw
  credentials cannot be shown again.
* The backend remains the only authority for money, ownership, identity,
  protection attempts, reservation release, child lineage, and state.
