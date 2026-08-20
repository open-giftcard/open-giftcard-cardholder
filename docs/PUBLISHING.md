# Public Publishing Workflow

## Repository identity

The canonical public repository is:

`https://github.com/open-giftcard/open-giftcard-cardholder`

The public repository was intentionally started from a reviewed, squashed
commit. Some older development working copies retain the full pre-public
history, so their `main` and public `main` have unrelated histories. Private-era
tags in those copies are historical development markers; they are not Open
Giftcard public releases.

## Remote policy for a full-history working copy

- `origin` is the canonical public Open Giftcard repository.
- `legacy` preserves the former development remote.
- Do not set this full-history `main` branch to track `origin/main`.
- Never force-push, merge unrelated histories, or push `--tags` to `origin`.

A normal clone of the public repository needs only its usual `origin` remote.

## Publishing a change

Use a clean clone of the public repository. Transfer the reviewed patch (or
cherry-pick a commit only when its parentage and contents are understood), run
all gates there, and open a normal pull request against public `main`.

Before publishing:

1. Review the diff for private names, hosts, credentials, generated artifacts,
   and historical release claims.
2. Verify `contracts/README.md` names a public backend commit and that the
   recorded SHA-256 matches `backend.openapi.json`.
3. Run formatting, Release build/tests, and the browser gate when UI changed.
4. Keep `CHANGELOG.md` under `Unreleased` until a coordinated public release is
   actually created.

## Releases

Create no standalone cardholder tag. A release must use one semantic version
across the public cardholder, backend, and portal repositories and record the
reviewed commit triplet. Deployment certification is separate from tagging and
must follow [DEPLOYMENT.md](DEPLOYMENT.md) and
[PRODUCTION_READINESS.md](PRODUCTION_READINESS.md).
