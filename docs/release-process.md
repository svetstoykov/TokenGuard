# NuGet Release Process

This document defines manual publication for `TokenGuard.Core`, `TokenGuard.Extensions.OpenAI`, `TokenGuard.Extensions.Anthropic`, and `TokenGuard.Extensions.AI` through GitHub Actions and NuGet Trusted Publishing.

## Release Model

- Each package owns its version in its `.csproj` file.
- Extension package versions do not need to match `TokenGuard.Core` version or each other.
- Each extension declares the `TokenGuard.Core` version it was built with as its minimum dependency. See [Minimum Core Version](#minimum-core-version).
- One workflow run publishes one selected package.
- One existing Git tag identifies exact source commit.
- When one tag contains changes for multiple packages, run workflow once per changed package.
- Merges, pushes, and tag creation never publish automatically.

## Minimum Core Version

`TokenGuard.Extensions.OpenAI`, `TokenGuard.Extensions.Anthropic` and `TokenGuard.Extensions.AI` use Core's public `ILlmSummarizer` contract.
Their packed dependency on Core is a minimum version, `x.y.z`, with no upper bound. Release validation checks that
all extensions declare the version of the Core package built from the same commit as that minimum.

Publish Core first, then the extensions that require it. An extension cannot be restored until its minimum Core
version is available on nuget.org. A consumer can use a newer compatible Core without waiting for an extension
release. A direct reference below an extension's minimum produces a NuGet downgrade warning or error.

For 1.3.0, bump and publish all four packages from one tag, Core first. The version bump and package release notes
belong to release preparation. Extensions from 1.2.0 retain their exact Core dependency; packages already published
on nuget.org are immutable.

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

1. Update `<Version>` and `<PackageReleaseNotes>` in package project being released. For 1.3.0, update all four package projects together.
2. Update `CHANGELOG.md` for package release.
3. Merge release changes.
4. Create and push Git tag pointing to exact commit to publish.
5. Confirm `.github/workflows/release-validation.yml` passed for tagged commit.

Release validation includes README snippet check. `tests/TokenGuard.ReadmeSnippets/verify.sh` packs `TokenGuard.Core`, `TokenGuard.Extensions.OpenAI`, `TokenGuard.Extensions.Anthropic` and `TokenGuard.Extensions.AI` from working tree, builds README's OpenAI, Anthropic and Microsoft.Extensions.AI quick starts against those packages instead of project references, and fails when quick start does not compile or when README stops matching compiled text. OpenAI quick start lives in `tests/TokenGuard.ReadmeSnippets/Program.cs` and is compared with `README.md` and `src/TokenGuard.Core/PackageReadme.md`. Anthropic quick start lives in `tests/TokenGuard.ReadmeSnippets/AnthropicQuickStart.cs`; its usings, setup and loop are compared with `README.md` and with `src/TokenGuard.Extensions.Anthropic/PackageReadme.md`. Fragments of OpenAI package readme live in `tests/TokenGuard.ReadmeSnippets/OpenAIPackageReadme.cs` and are compared with `src/TokenGuard.Extensions.OpenAI/PackageReadme.md`. Same check runs in validation job of publish workflow. When changing quick start, edit its source file and READMEs that carry it together, then run script locally.

The Microsoft.Extensions.AI quick start lives in `tests/TokenGuard.ReadmeSnippets/ChatClientQuickStart.cs`; its usings and loop are compared with `README.md` and `src/TokenGuard.Extensions.AI/PackageReadme.md`. The check compiles this example with only Core and the AI adapter imported.

Package projects:

- `src/TokenGuard.Core/TokenGuard.Core.csproj`
- `src/TokenGuard.Extensions.OpenAI/TokenGuard.Extensions.OpenAI.csproj`
- `src/TokenGuard.Extensions.Anthropic/TokenGuard.Extensions.Anthropic.csproj`
- `src/TokenGuard.Extensions.AI/TokenGuard.Extensions.AI.csproj`

## Publish Package

1. Open repository Actions page.
2. Select **Publish NuGet Package** workflow.
3. Select **Run workflow**.
4. Enter existing tag.
5. Select package to publish.
6. Start workflow.
7. Confirm validation job restores, builds, tests, packs, builds README snippets, and uploads package artifacts.
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
5. For a coordinated release, confirm all extensions are published with the released Core version as their minimum dependency.
