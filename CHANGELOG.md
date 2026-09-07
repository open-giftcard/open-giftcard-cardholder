# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

`v0.9.1` is the current release, and `v0.9.0` an hour before it was the first
tag this project ever published. Neither makes a stability or deployment
promise; see `VERSIONING.md` for what the number means and what it deliberately
does not. Local tags predating the open-source cleanup are not usable, were
never published, and are not listed.

## Unreleased

### Removed

- `v1.0.0` was cut on 2026-09-07 and retracted the same day. A generalization
  and scope audit written shortly after the tag concluded that `/api/v1` should
  not be frozen yet, because decisions belonging to the original corporate-retail
  customer had become platform-wide invariants. The tag and the GitHub releases
  are deleted in all four repositories and `v0.9.1` is the current release again.
  `VERSIONING.md` records what happened and why. Nothing verified was reverted:
  the full-stack CI job, the SDK and container fixes, and the idempotency
  documentation all remain.

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
