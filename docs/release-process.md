# NuGet Release Process

This document defines manual publication for `TokenGuard.Core`, `TokenGuard.Extensions.OpenAI`, and `TokenGuard.Extensions.Anthropic` through GitHub Actions and NuGet Trusted Publishing.

## Release Model

- Each package owns its version in its `.csproj` file.
- Package versions do not need to match.
- One workflow run publishes one selected package.
- One existing Git tag identifies exact source commit.
- When one tag contains changes for multiple packages, run workflow once per changed package.
- Merges, pushes, and tag creation never publish automatically.

## One-Time Configuration

### GitHub

1. Create environment named `release` under repository settings.
2. Add required reviewer to `release` environment.
3. Add Actions repository variable `NUGET_USER` containing nuget.org username, not email address.

### NuGet.org

Create Trusted Publishing policy with:

- Repository owner: `svetstoykov`
- Repository: `TokenGuard`
- Workflow file: `publish.yml`
- Environment: `release`

No long-lived NuGet API key belongs in GitHub secrets.

## Prepare Release

1. Update `<Version>` and `<PackageReleaseNotes>` in package project being released.
2. Update `CHANGELOG.md` for package release.
3. Merge release changes.
4. Create and push Git tag pointing to exact commit to publish.
5. Confirm `.github/workflows/release-validation.yml` passed for tagged commit.

Package projects:

- `src/TokenGuard.Core/TokenGuard.Core.csproj`
- `src/TokenGuard.Extensions.OpenAI/TokenGuard.Extensions.OpenAI.csproj`
- `src/TokenGuard.Extensions.Anthropic/TokenGuard.Extensions.Anthropic.csproj`

## Publish Package

1. Open repository Actions page.
2. Select **Publish NuGet Package** workflow.
3. Select **Run workflow**.
4. Enter existing tag.
5. Select package to publish.
6. Start workflow.
7. Confirm validation job restores, builds, tests, packs, and uploads package artifacts.
8. Approve `release` environment deployment.
9. Confirm publication job completes.

Workflow publishes selected `.nupkg` and matching `.snupkg`. Package version comes from selected project file.

## Retry Behavior

Package versions on nuget.org remain immutable. Workflow uses duplicate-safe pushes, allowing same tag and package selection to be rerun after partial failure. Existing package artifacts are skipped; missing artifacts are pushed.

If Trusted Publishing login fails, verify exact owner, repository, workflow filename, environment, and `NUGET_USER` values against NuGet policy.

## Post-Publish Checks

1. Verify selected package version appears on nuget.org.
2. Verify symbol package finishes validation.
3. Verify package README and release notes render correctly.
4. If another package changed at same tag, run workflow again and select that package.
