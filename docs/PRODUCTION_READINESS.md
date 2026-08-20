# Production Readiness

Open Giftcard is an open reference implementation. The cardholder source has
strong application-level controls, but this repository does not claim that a
public deployment has been certified for production.

Last reviewed: 2026-08-20.

| Area | Status | Boundary |
| --- | --- | --- |
| Server-rendered BFF and backend-token isolation | Implemented and tested | Browser receives an opaque cookie, never backend tokens. |
| Claim-secret cleanup and purpose-bound activation state | Implemented and tested | Claiming remains an antiforgery-protected POST. |
| Antiforgery, CSP, framing/referrer and `no-store` headers | Implemented and tested | Optional JS changes only `script-src` from `'none'` to `'self'`. |
| Secure cookies, HTTPS backend validation, durable-key requirement | Implemented fail-closed outside Development | Operator supplies trusted TLS and protected persistent storage. |
| PostgreSQL session/activation/payment store | Reference implementation | Runtime schema bootstrap still requires DDL privilege. |
| Liveness and database readiness endpoints | Implemented | Operator supplies monitoring and alerts. |
| Pinned public backend contract | Implemented and tested | Snapshot updates require explicit public commit review. |
| English/Turkish and automated accessibility checks | Implemented | Human assistive-technology and visual review is still required. |
| Multi-replica operation | Supported by shared DB/key design | Not certified in a public staging environment. |
| Backups, HA, restore, disaster recovery | Deployment responsibility | No repository artifact can prove an operator's controls. |
| Secrets, logs, metrics, alerting, incident response | Deployment responsibility | Must be provided by the hosting environment. |
| Notification delivery and retry/outbox | Backend/deployment responsibility | Must be verified before real recipient invitations. |
| Penetration test and staging certification | Not completed | Required before a production claim. |
| Coordinated public release | Not completed at last review | The public repositories had no synchronized release tag on the review date. |

## Explicitly unsupported boundaries

- Offline caching or local authorization, balance, or payment decisions.
- Service-worker or CDN caching of personalized recipient HTML.
- Backend access/refresh tokens in JavaScript or browser storage.
- Broad CORS access to the bearer-only backend.
- Automatic client-side credential issuance or rotation.
- A native mobile app, wallet pass, or push notification implementation in this
  repository today.

## Release gate

A production-ready claim requires all rows above to be either verified or
accepted by a named operator, the complete staging checklist in
[DEPLOYMENT.md](DEPLOYMENT.md), a reviewed three-repository commit triplet, and
a synchronized public tag created from the public histories. Passing CI alone
is source evidence, not deployment certification.
