# Open Giftcard Cardholder Deployment Contract

This repository is a reference implementation. This document describes the
configuration a deployer must supply; it is not evidence that a public staging
or production environment has passed these checks.

## Topology

```text
recipient --HTTPS--> ingress --HTTP/HTTPS--> Razor Pages BFF
                                         \--> HTTPS Open Giftcard API
cardholder BFF --> cardholder-owned PostgreSQL
cardholder BFF --> persistent Data Protection key volume
```

The browser receives server-rendered HTML and opaque `HttpOnly` cookies. The
backend tokens and activation contexts remain encrypted server-side. All
replicas must share the PostgreSQL database and Data Protection key ring.

## Required configuration

Supply configuration through the deployment platform, not a committed file:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:8080
AllowedHosts=card.<your-domain>

Backend__BaseUrl=https://api.<your-domain>
Backend__TimeoutSeconds=30
ConnectionStrings__Cardholder=Host=<host>;Port=5432;Database=<cardholder-db>;Username=<cardholder-role>;Password=<secret>;SSL Mode=Require
DataProtection__KeysPath=<absolute-persistent-key-volume>

CardholderSession__SessionCookieName=__Host-cardholder-session
CardholderSession__ActivationCookieName=__Host-cardholder-activation
CardholderSession__RequireSecureCookies=true
CardholderSession__ActivationLifetimeMinutes=30

# Optional, disabled by default
Ui__EnableJavaScriptEnhancements=false

# Only when TLS terminates at a reverse proxy
Networking__ForwardedHeaders__KnownProxies__0=<literal-immediate-proxy-ip>
```

Outside Development, startup fails when the backend URL is not HTTPS, either
cookie is not a secure `__Host-` cookie, the durable key path is absent, or a
configured proxy address is malformed. Development's HTTP cookies and local
`.local/keys` directory are not production settings.

The optional JavaScript setting serves one same-origin presentation module. It
does not change the routes, token boundary, backend authority, or requirement
that every journey work from the server-rendered HTML alone.

## PostgreSQL and schema ownership

Use a dedicated database and role. Never reuse a backend/portal database or
role, and never run the application as a PostgreSQL superuser.

At present the runtime initializes and evolves its own three tables:

- `cardholder_sessions`
- `cardholder_activations`
- `cardholder_payment_credentials`

The runtime role therefore needs the DDL and DML permissions required to create
and alter those tables in its own schema. This bootstrap approach is suitable
for a reference deployment but is a production-readiness limitation: a managed
migration step and a reduced-privilege runtime role should replace it before a
high-assurance deployment.

Back up the database and test restoration. A rollback must preserve both this
database and the Data Protection key ring or active sessions and activation
contexts may become unusable.

## TLS, proxy, and caching boundary

Terminate trusted TLS at the application or its immediate ingress. The ingress
must overwrite client-supplied forwarding headers. The application accepts one
forwarded hop only and only from literal addresses in `KnownProxies`; with no
entry it ignores forwarding headers.

Personalized responses carry `no-store`. Do not place a cache, service worker,
or CDN HTML cache in front of recipient pages. No CORS relaxation is required:
the BFF calls the bearer-only backend server-to-server.

## Coupled backend configuration

The backend must generate recipient links for this origin:

```text
Distribution__ClaimBaseUrl=https://card.<your-domain>/activate
Sharing__ClaimBaseUrl=https://card.<your-domain>/share/claim
Sharing__DirectClaimBaseUrl=https://card.<your-domain>/activate/share
Partners__ClaimBaseUrl=https://card.<your-domain>/epin
Networking__ForwardedHeaders__KnownProxies__<n>=<literal-cardholder-bff-ip>
```

The cardholder and backend proxy allowlists are separate trust boundaries.

## Health and observability

- `GET /health` is process liveness.
- `GET /health/ready` checks that all three cardholder tables are queryable and
  returns 503 without connection details when unavailable.
- Monitor backend readiness independently.

The application writes structured framework/application logs to standard
output. The operator remains responsible for collection, retention, alerting,
secret filtering, metrics, and incident response.

## Staging promotion checklist

Before exposing recipients:

1. Record the exact public commits of cardholder, backend, and portal and verify
   the pinned contract hash.
2. Verify TLS, HSTS, secure `__Host-` cookies, antiforgery, CSP, `no-store`, and
   both health endpoints.
3. Complete new/existing activation, sign-in, cards/history/lifecycle, all share
   paths, e-pin claim, payment presentation, sign-out, and English/Turkish
   switching against the deployed backend.
4. Restart every replica and verify sessions survive. Exercise multiple replicas
   to prove the database and key ring are shared.
5. Verify the ingress overwrites forwarded headers and rate limits distinguish
   observed recipient addresses.
6. Test database backup/restore and an application rollback without replacing
   the database or key ring.
7. Run keyboard, 320 px, 200% zoom, reduced-motion, Firefox, Chromium, and human
   screen-reader/visual review.
8. Confirm notification delivery, monitoring, alerting, secrets rotation, and
   incident ownership in the backend deployment.

Do not create a release tag merely because source tests pass. Follow
[PUBLISHING.md](PUBLISHING.md) and record incomplete gates in
[PRODUCTION_READINESS.md](PRODUCTION_READINESS.md).
