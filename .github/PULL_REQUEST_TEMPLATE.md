## What this changes

Describe the behaviour before and after. If this fixes something, say what made
it wrong rather than only what the fix does.

## How it was verified

State what you ran and what it said. If you did not run something, say that
instead of leaving it implied.

- [ ] `dotnet format --verify-no-changes`, build, and `dotnet test`
- [ ] The Firefox and Chromium accessibility checks
- [ ] Opened the affected pages in a browser
- [ ] Not applicable, because:

## If it touches a page

- [ ] It works with JavaScript disabled. This application is server-rendered on
      purpose and a recipient may be on anything.
- [ ] No horizontal scrollbar at a 320px viewport or at 200% zoom.

## If it touches the backend contract

- [ ] `bash scripts/verify-contract-pin.sh` passes. A recaptured snapshot needs
      its recorded hash updated in the same commit.

## Anything a reviewer should look at first

Point at the part you are least sure about.
