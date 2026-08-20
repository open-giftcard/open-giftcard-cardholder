# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

There is no released version and nothing has been deployed anywhere, so there
are no version headings yet. Everything below has landed on `main` since the
first public commit. The tags that predate the open-source cleanup are not
usable and are not listed.

## Unreleased

### Added

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
