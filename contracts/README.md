# Backend OpenAPI Contract

`backend.openapi.json` was captured from the authoritative public backend:

- Repository: https://github.com/open-giftcard/open-giftcard
- Branch: `milestone/deployment-certified-rc`
- Commit: `90c7dd2a17a6eb4ea686fc344de25f3a1a155d12`
- Endpoint: `/swagger/v1/swagger.json`
- SHA-256:
  `EC8051EBC7F65007DB8BBA6BDF1B84FAA3CDBA16456A96506E1BE562C3C3827C`

That public commit was rebuilt and its served OpenAPI document was verified to
have exactly the SHA-256 recorded above. Later backend changes do not silently
move this pin: updating the snapshot requires an explicit review and a new
public commit reference.

The document includes the `partners` surface used by the e-pin claim route. Its
API title is generic and no operation summary names a retailer.

Update the snapshot only after reviewing backend contract changes at an
explicitly accepted backend commit. Never capture from a moving backend branch
without an explicit commit pin.

This repository does **not** generate a client from the document. The cardholder
app touches only its recipient activation, owned-card, lifecycle, sharing, and
e-pin claim subset, so it hand-writes a small typed client instead
(ADR-CARD-003). The pinned document remains the authority: `BackendContractTests`
asserts that every operation and response field the app binds to still exists in
it, so backend drift fails the build rather than surfacing at runtime.
