# Security Policy

## Reporting a vulnerability

Use GitHub's private vulnerability reporting on this repository: **Security →
Report a vulnerability**. Please do not open a public issue for anything you
believe is exploitable.

There is no bounty and no formal response-time commitment.

## Supported versions

There is no released version yet. `main` is the only branch that receives fixes.

## Where the boundary is

This is the recipient-facing application. It decides nothing about
authorization, ownership, or money: the platform backend is the only authority
for all of them, and a finding in any of those belongs in the `open-giftcard`
repository.

This repository is responsible for the browser boundary, and it is the surface
an untrusted member of the public reaches first, arriving from an emailed
activation link.

**JavaScript is optional and same-origin only.** Pages are server-rendered and
the default Content Security Policy sets `script-src 'none'`. An operator may
enable the repository's progressive-enhancement module, which changes the
directive only to `script-src 'self'`; inline, evaluated, and third-party script
remain blocked. Tests cover both modes. The module handles presentation only
and receives no backend token, activation secret, or payment credential.

**The activation secret is kept out of the referrer.** A claim link carries a
secret in its URL, so every response sets `Referrer-Policy: no-referrer`. The
application also scrubs the secret out of the address and into its encrypted
server-side activation store rather than leaving it in browser history.

**Tokens never reach the browser.** The application is its own
Backend-for-Frontend: backend access and refresh tokens stay server-side,
encrypted with ASP.NET Data Protection, and the browser holds one opaque session
cookie whose value is 32 random bytes with only its SHA-256 stored.

**Cookies.** `HttpOnly`, `Secure`, and `__Host-` prefixed outside Development,
with lifetime capped at 400 days. Startup fails in a non-Development environment
if insecure cookies are configured. Where a `__Host-` prefix cannot be honoured
the prefix is dropped rather than emitting a cookie the browser would reject.

**Antiforgery.** Standard ASP.NET validation on every form post. A failure is
turned into a readable session-expired recovery page rather than a bare 400, so
a recipient who left a tab open is not shown a broken download.

**Payment credentials.** A displayed QR or numeric code is valid for 60 seconds,
is single use, and is never persisted client-side. The checkout view blurs it at
its real expiry and requires an explicit action to reissue.

## Known gaps

- **No staging certification and no penetration test.**
- **The user interface has not been reviewed on a screen by a second person.**
  Accessibility and contrast are asserted by automated checks and computed
  ratios rather than by inspection.

The full separation between implemented application controls, deployment
responsibilities, and unverified promotion work is tracked in
[docs/PRODUCTION_READINESS.md](docs/PRODUCTION_READINESS.md).

## Scope

In scope: session handling, the activation and share claim journeys, the payment
credential presentation, the CSP, and anything that could leak a claim secret or
a backend token to a browser or a third party.

Out of scope: authorization, tenant isolation, and financial correctness, which
are enforced by the backend and should be reported there.
