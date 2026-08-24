# Backend OpenAPI Contract

`backend.openapi.json` was captured from the authoritative public backend:

- Repository: https://github.com/open-giftcard/open-giftcard
- Branch: `milestone/deployment-certified-rc`
- Commit: `56c73c689e41aea6073a73db2e575f8f479bc6c2`
- Endpoint: `/swagger/v1/swagger.json`
- SHA-256:
  `A5137EFD65B6CB9927419C26CA8BE0A1873C34FE47F4CC22B3C66BCA3549B6C1`

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
