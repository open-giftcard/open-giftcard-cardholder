# Contributing

Thanks for looking. Open an issue before a large change so we can agree on the
shape; small fixes can go straight to a pull request.

## What this repository decides

Almost nothing. The backend is the only authority for authorization, ownership,
and money. If your change is about who may do what, or about a balance, it
belongs in the `open-giftcard` repository.

What lives here is the recipient's experience, and it is the surface an
untrusted member of the public reaches first, arriving from an emailed link on a
phone.

## The two constraints that shape everything

**No JavaScript.** Pages are server-rendered and the Content Security Policy
sets `script-src 'none'`. A browser check asserts the page contains zero
`<script>` elements, so this cannot be relaxed by accident. If a feature seems
to need script, it needs a different design: the theme switch is a form post,
and the disclosure menus are `<details>`.

**It has to work on a small screen at high zoom.** The browser suite renders at
320px with 200% zoom and fails on any horizontal overflow. That check earns its
keep: it caught a header row that could neither fit nor wrap.

## Getting a working copy

You need .NET 10 and PostgreSQL. Node is needed only for the optional browser
checks.

```bash
powershell -File scripts/Setup-LocalDatabase.ps1
$env:ConnectionStrings__Cardholder="..."
dotnet run --project src/GiftCardCardholder.Web
```

The application keeps its own database for sessions and short-lived activation
contexts, and creates its tables on startup. It needs a running backend.

## Running the tests

```bash
dotnet test GiftCardCardholder.slnx -c Release
```

The browser checks are self-contained and do not need the backend: the harness
starts the app pointed at deliberately unreachable dependencies so only
anonymous pages are exercised.

```bash
cd browser-tests
pnpm install --frozen-lockfile
pnpm exec playwright install firefox chromium
pnpm exec playwright test
```

Run them for any change to markup or CSS. The .NET suite will not catch a layout
regression.

## Common changes

### User-facing text

English strings are also the `.resx` keys. Changing a sentence changes its key,
so `Resources/Localization/SharedResource.tr.resx` must be updated in the same
commit or the Turkish translation silently falls back to English.
`LocalizationCoverageTests` guards this.

Write for someone who did not ask for this card and does not know the product.
Say what will happen to them, not what the system requires.

### Anything touching the claim journey

Read [SECURITY.md](SECURITY.md) first. A claim link carries a secret in its URL,
which is why every response sets `Referrer-Policy: no-referrer` and why the
application scrubs the secret out of the address into its encrypted server-side
store. Do not add anything that could put that secret in a log, a referrer, or
browser history.

### Adding a backend call

This repository hand-writes its client rather than generating one. Add the call
to `Backend/BackendClient.cs`, and add the operation and the fields you bind to
`BackendContractTests`, so backend drift fails the build instead of surfacing at
runtime.

To bind an endpoint the pinned contract does not contain, the contract must be
recaptured at an agreed backend commit, updating the recorded commit and hash in
`contracts/README.md` together. CI fails when the declared hash is not the hash
of the file beside it.

### CSS

Colour lives in `:root` custom properties in `wwwroot/css/app.css`, so a
deployment can override the palette with one appended stylesheet. The card-face
gradient records its contrast ratios in a comment; keep them accurate if you
change the colours. Brand indigo measures 2.34:1 on white and is deliberately
refused as a text colour.

## What a good pull request looks like

Behavioural changes carry tests, and markup or CSS changes carry a browser-suite
run. The build treats warnings as errors.

Report results honestly, including anything you could not run.

## Security

Do not open a public issue for a suspected vulnerability. See
[SECURITY.md](SECURITY.md).

## Licence

Contributions are accepted under the Apache License 2.0.
