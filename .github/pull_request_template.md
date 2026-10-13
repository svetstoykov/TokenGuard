## What this changes

A short description of the change and the reason for it. Link the issue if there is one.

## How it was checked

The commands you ran and what they reported.

## Checklist

- [ ] `dotnet build TokenGuard.sln --configuration Release` finishes with zero warnings
- [ ] `dotnet test TokenGuard.sln` passes
- [ ] `./tests/TokenGuard.ReadmeSnippets/verify.sh` passes, if a readme or a quick-start block changed
- [ ] New behavior has tests, and a bug fix has a regression test
- [ ] Public API changes and new package references are named above
- [ ] `CHANGELOG.md` has an entry under `Unreleased`, if users of the packages will notice the change
- [ ] No API keys or local environment files are included
