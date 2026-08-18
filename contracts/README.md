# Backend OpenAPI Contract

`backend.openapi.json` was captured from the authoritative sibling backend:

- Repository: the platform backend
- Branch: `chore/debrand-backend`
- Commit: `682c075203cd1bf9935865a969fa183fa5aab844`
- Endpoint: `/swagger/v1/swagger.json`
- SHA-256:
  `59B7B452E734A4411836342FDF4B0A24F20AD446D235C5F6BF4FA6E5DC2F6FE6`

This capture re-synchronizes the portal and cardholder onto **one** backend
document. The two had drifted apart: the portal was pinned at `3a03f70b` and the
cardholder at `cfee9b1e`, and the cardholder's recorded SHA-256 did not match the
file beside it. Both now carry the same bytes and the same recorded hash again.

It also adds the `partners` surface, which neither previous pin contained even
though the backend had been serving it and the cardholder had been calling the
e-pin claim route. The document is de-branded: its title is now
"Digital Corporate Gift Card Platform" and no operation summary names a retailer.

Update the snapshot only after reviewing backend contract changes at an
explicitly accepted backend commit. Never capture from a moving backend branch
without an explicit commit pin.

This repository does **not** generate a client from the document. The cardholder
app touches only its recipient activation, owned-card, lifecycle, sharing, and
e-pin claim subset, so it hand-writes a small typed client instead
(ADR-CARD-003). The pinned document remains the authority: `BackendContractTests`
asserts that every operation and response field the app binds to still exists in
it, so backend drift fails the build rather than surfacing at runtime.
