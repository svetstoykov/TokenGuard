# NuGet Release Process

This document is the single authoritative path for publishing `TokenGuard.Core`,
`TokenGuard.Extensions.OpenAI`, and `TokenGuard.Extensions.Anthropic` to NuGet.

## Scope

- Publishes the three public NuGet packages from this repository.
- Uses the existing validation gate in `.github/workflows/release-validation.yml`.
- Assumes package functionality is already complete and release-ready.

## Prerequisites

Before starting a release:

1. Work from a **clean checkout** of the exact commit you will publish. Do not publish from a dirty
   working tree.
2. Use **.NET SDK 10.0+**.
3. Have a NuGet.org API key with push permission for all three package IDs.
4. Export the API key into your shell:

   ```bash
   export NUGET_API_KEY="<nuget-api-key>"
   ```

5. Ensure the release version is already committed in all three project files:
   - `src/TokenGuard.Core/TokenGuard.Core.csproj`
   - `src/TokenGuard.Extensions.OpenAI/TokenGuard.Extensions.OpenAI.csproj`
   - `src/TokenGuard.Extensions.Anthropic/TokenGuard.Extensions.Anthropic.csproj`

## Versioning Expectations

- Use one shared SemVer version for all three packages in each release.
- Keep `TokenGuard.Core`, `TokenGuard.Extensions.OpenAI`, and `TokenGuard.Extensions.Anthropic`
  on the same version number.
- Commit version changes before publishing. Do not override package versions only at pack time.
- Stable releases use versions without a prerelease suffix. If a prerelease is needed, use the same
  prerelease suffix on all three packages.

## Required Validation Gate

Publish only from a commit that already has a **passing** run of
`.github/workflows/release-validation.yml` on GitHub. That workflow is the required pre-release
gate and validates:

- `dotnet restore TokenGuard.sln --nologo`
- `dotnet build TokenGuard.sln --configuration Release --no-restore --nologo`
- `dotnet test TokenGuard.sln --configuration Release --no-build --nologo`
- `dotnet pack` for all three public packages

If the workflow is not green for the exact commit being published, stop and fix validation first.

## Release Procedure

### 1. Publish release metadata changes

Prepare and merge the release commit to `main` with the intended package version already set in all
three `.csproj` files.

Optional but recommended:

- update package release notes in the same release commit
- create an annotated Git tag such as `v1.2.3` on the validated commit after merge

### 2. Confirm the validated commit

Confirm the exact `main` commit or release tag you will publish already passed
`.github/workflows/release-validation.yml`.

### 3. Pack from that exact commit

From a clean checkout of the validated commit:

```bash
git checkout <validated-commit-or-tag>
rm -rf artifacts/release
dotnet restore TokenGuard.sln --nologo
dotnet build TokenGuard.sln --configuration Release --no-restore --nologo
dotnet test TokenGuard.sln --configuration Release --no-build --nologo
dotnet pack src/TokenGuard.Core/TokenGuard.Core.csproj --configuration Release --no-build --nologo --output artifacts/release
dotnet pack src/TokenGuard.Extensions.OpenAI/TokenGuard.Extensions.OpenAI.csproj --configuration Release --no-build --nologo --output artifacts/release
dotnet pack src/TokenGuard.Extensions.Anthropic/TokenGuard.Extensions.Anthropic.csproj --configuration Release --no-build --nologo --output artifacts/release
```

This local pack step must match the validated commit and must not introduce uncommitted tracked-file
changes.

### 4. Publish packages to NuGet

Publish in this order:

1. `TokenGuard.Core`
2. `TokenGuard.Extensions.OpenAI`
3. `TokenGuard.Extensions.Anthropic`

`TokenGuard.Core` goes first because both extension packages depend on it. After `TokenGuard.Core`
is published, the two extension packages can be pushed in either order, but this runbook uses the
order above every time.

Push both the primary package and its matching symbol package for each project:

```bash
dotnet nuget push artifacts/release/TokenGuard.Core.*.nupkg --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"
dotnet nuget push artifacts/release/TokenGuard.Core.*.snupkg --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"

dotnet nuget push artifacts/release/TokenGuard.Extensions.OpenAI.*.nupkg --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"
dotnet nuget push artifacts/release/TokenGuard.Extensions.OpenAI.*.snupkg --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"

dotnet nuget push artifacts/release/TokenGuard.Extensions.Anthropic.*.nupkg --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"
dotnet nuget push artifacts/release/TokenGuard.Extensions.Anthropic.*.snupkg --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"
```

## Symbols and Source Handling

- `src/Directory.Build.props` enables symbol package generation for public packages.
- Each `dotnet pack` command produces the primary `.nupkg` and a matching `.snupkg`.
- Release publication includes both artifacts for every package.
- No separate source-only package is published as part of this process.

## Post-Publish Checks

After pushing all six artifacts:

1. Verify all three package versions appear on NuGet.org.
2. Verify each package version has its matching symbol package accepted by NuGet.org.
3. Verify package READMEs render correctly on the NuGet package pages.
4. If you created a release tag, push that tag if it has not already been pushed.

## If Something Fails

- If validation fails, do not publish. Fix the repository, rerun validation, and publish a newly
  validated commit.
- If package push fails before all three packages are published, inspect which packages already
  exist on NuGet.org before retrying.
- Do not change code locally and publish without recommitting and rerunning the validation gate.
