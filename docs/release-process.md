# NuGet Release Process

This document defines manual publication for `TokenGuard.Core`, `TokenGuard.Extensions.OpenAI`, and `TokenGuard.Extensions.Anthropic` through GitHub Actions and NuGet Trusted Publishing.

## Release Model

- Each package owns its version in its `.csproj` file.
- Extension package versions do not need to match `TokenGuard.Core` version or each other.
- Each extension package depends on exactly one `TokenGuard.Core` version: version in `TokenGuard.Core.csproj` at tagged commit. See [Core Version Pin](#core-version-pin).
- One workflow run publishes one selected package.
- One existing Git tag identifies exact source commit.
- When one tag contains changes for multiple packages, run workflow once per changed package.
- Merges, pushes, and tag creation never publish automatically.

## Core Version Pin

`TokenGuard.Extensions.OpenAI` and `TokenGuard.Extensions.Anthropic` use internal members of `TokenGuard.Core`. Those members carry no compatibility promise between Core versions, so each packed extension declares exact Core dependency, `[x.y.z]`, instead of minimum version. `src/Directory.Build.targets` sets it during pack. NuGet then reports mismatched pair at restore; without pin, mismatch surfaces only at run time as `MissingMethodException` or `TypeLoadException`.

What NuGet reports depends on consumer project:

- Two extensions pinned to different Core versions: restore fails with `NU1107` version conflict.
- Extension plus direct `TokenGuard.Core` reference at another version: restore succeeds with `NU1608` warning and resolves direct version. Pin does not block this pair; it fails at run time unless consumer treats warning as error. Package READMEs tell consumers to reference Core directly, so this is common case.

Consequences for release:

- Every `TokenGuard.Core` release needs release of both extension packages from same tag, including Core patch release that changes no extension code. Bump extension `<Version>` in same change that bumps Core.
- Until matching extension ships, extension consumer has no supported way to move to new Core version. Core-only release therefore strands extension consumers on previous Core.
- Extension `<Version>` left unchanged after Core bump packs same extension version with new pin. nuget.org already holds that version, and duplicate-safe push skips it without error.
- Publish `TokenGuard.Core` first, then each extension. Extension published before its Core version exists on nuget.org cannot be restored.
- Extension-only release stays possible: it pins Core version already in `TokenGuard.Core.csproj`, which must already be published.
- Extension versions published before pin (`1.0.1`, `1.1.0`) keep minimum-version dependency and stay exposed to mismatch.

Release validation checks packed `.nuspec` of both extensions for `[x.y.z]` range equal to packed Core version.

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

1. Update `<Version>` and `<PackageReleaseNotes>` in package project being released. When `TokenGuard.Core` version changes, do same for both extension projects.
2. Update `CHANGELOG.md` for package release.
3. Merge release changes.
4. Create and push Git tag pointing to exact commit to publish.
5. Confirm `.github/workflows/release-validation.yml` passed for tagged commit.

Release validation includes README snippet check. `tests/TokenGuard.ReadmeSnippets/verify.sh` packs `TokenGuard.Core` and `TokenGuard.Extensions.OpenAI` from working tree, builds README's OpenAI quick start against those packages instead of project references, and fails when quick start does not compile or when `README.md` or `src/TokenGuard.Core/PackageReadme.md` stops matching compiled text in `tests/TokenGuard.ReadmeSnippets/Program.cs`. Same check runs in validation job of publish workflow. When changing quick start, edit `Program.cs` and both READMEs together, then run script locally.

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
5. After `TokenGuard.Core` release, confirm both extension packages are published with dependency on that exact Core version.
