# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

`v1.0.0` is the current release and the first stable one. The `v0.9.x` tags
before it were the first this project ever published and made no stability
promise; see `VERSIONING.md` for what each number means and what it
deliberately does not. Local tags predating the open-source cleanup are not
usable, were never published, and are not listed.

## v1.0.0 - 2026-09-07

The first stable release. `VERSIONING.md` states what the number commits this
project to; in short, three promises now take effect.

**The HTTP API is stable within 1.x.** `/api/v1` will not break. A CI job diffs
the document a running instance serves against the accepted baseline and fails
on any change the policy forbids: a removed or renamed endpoint, request field
or response field, an optional request field made required, a narrowed accepted
value set, or a dropped status code. Additive change passes.

**Upgrading within 1.x is safe.** Migrations are forward-only, and CI applies
the previous release's migrations to a database populated through the
demonstration seed, then this build's over the same volume, and requires
readiness to answer without naming a module as behind. It then checks the
seeded value survived and that every ledger transaction still balances per
currency.

**An adopter can use it without forking the core.** One command brings up the
API, the operator portal and the cardholder application with a populated
tenant, and CI signs in through the portal with the credentials the README
publishes. Every architecture decision the source cites by number resolves to a
published document. Adding a notification or audit custody provider is
documented.

### Closed for this release

- The client contract direction is guarded in all three clients. The portal
  generates its backend client from the pinned contract at build time; the
  cardholder and POS assert their real serialised requests against the pinned
  schema in both directions, including its required fields.
- The full product is containerized. `docker compose -f docker-compose.yml -f
  docker-compose.full.yml up` brings up five more services alongside the API,
  and a CI job proves it from a clean checkout.
- The idempotency and retry contract is documented for integrators, including
  which conflicts are retryable and why the server does not retry them itself.

### Fixed

- The portal and cardholder pinned `rollForward: latestPatch`, so neither could
  build against a newer SDK feature band. That never showed in their own CI,
  which installs the exact pinned SDK, and appeared the first time the images
  were built. Both now match the backend and POS at `latestFeature`.
- The POS serialised-request check described itself as vacuous because the
  contract declared no required fields. It has been enforcing since the
  contract started declaring them; only the comment was wrong.

### Not in this release, by decision

Nothing has been deployed to a named environment, and `v1.0.0` deliberately
makes no deployment claim. That is `v0.5.0`, which remains open, and the gate
row asserting otherwise was removed with the reasoning recorded in
`VERSIONING.md`. SMS, managed audit custody and configurable branding remain
documented non-goals.

## v0.9.1 - 2026-08-31

### Fixed

- The release contract check reported a correctly tagged release as untagged,
  and failed CI on the `v0.9.0` commit in all four repositories. It looked for
  the tag only in the local working copy, and `actions/checkout` fetches a
  single commit with no tags, so the tag existed on the remote and not on the
  runner. It now looks locally first and falls back to `git ls-remote`; a tag
  found in either place passes, a tag found in neither still fails, and an
  unreachable remote warns rather than blocking an offline contributor.

  `v0.9.0` is left in history as what it was. It names a commit whose own CI
  does not pass, for a defect in the release tooling rather than in the
  platform, and `v0.9.1` is the corrected release.

## v0.9.0 - 2026-08-31

### Added

- A `Dockerfile`, so the cardholder can be brought up alongside the API with
  `docker compose -f docker-compose.yml -f docker-compose.full.yml up` from the
  backend repository. It mirrors the backend image, with no front-end build step
  because this application ships no JavaScript bundle by design. Not yet built:
  the machine this was written on has no Docker.

- Open Giftcard product identity throughout the rendered application and
  contributor documentation.
- An English-first language catalogue and multi-option language menu, replacing
  the hard-coded English/Turkish toggle so complete translations can be added
  without changing request validation or navigation structure.
- Deployment, production-readiness, and public publishing guidance that makes
  the reference-implementation boundary and unreleased status explicit.
- An operator-controlled, disabled-by-default JavaScript enhancement mode. The
  same-origin module adds presentation polish while the server-rendered HTML
  remains the complete application and the CSP stays strict in both modes.
- A security policy with a private reporting channel, and a contributor guide.
- CI fails when `contracts/README.md` declares a SHA-256 that is not the hash of
  the document beside it. This repository had exactly that: a recaptured
  snapshot with the previous hash left in place.
- Community health files: code of conduct, issue and pull request templates,
  and code owners.

### Fixed

- The pinned contract now names the public backend repository and the exact
  public commit verified to generate its recorded bytes.
- Contributor memory no longer treats private-era candidate tags as public
  Open Giftcard releases or describes the optional enhancement mode as absent.
- The header pushed the document to 345px inside a 320px viewport, producing a
  horizontal scrollbar at the narrowest supported phone width and at 200% zoom.
  The settings group now wraps and may shrink. The accessibility suite had been
  failing on every browser for some time before this.

### Changed

- `RELEASE_COMPATIBILITY.json` no longer names tags that do not exist. It
  declared release `v0.5.0-rc.1` and gave all four components that tag, and no
  repository has ever had a public tag. Schema version 2 adds a `development`
  channel for that state, and on a released channel now requires the tag it
  names to resolve locally. `scripts/Test-ReleaseContract.ps1` enforces both,
  and additionally rejects a byte order mark or CRLF line endings, so the file
  can be byte-identical in all four repositories. It had been CRLF in the
  backend and LF in the other three.
