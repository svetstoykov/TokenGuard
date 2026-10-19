# Contributing to TokenGuard

Thank you for considering a contribution. This page covers how to build and test the repository, and the branch and commit conventions a pull request is expected to follow.

For anything larger than a small fix, open an issue first and describe the change. A change to a public type or member is treated as a breaking-change question, so name the impact in the issue.

Do not report a security vulnerability in a public issue. Follow [SECURITY.md](SECURITY.md) instead.

## Prerequisites

- .NET SDK 10.0 or later. The packages target `net10.0` only.
- `bash` for the README snippet check.

CI runs on Linux only. macOS and Windows are expected to work and are not covered by CI.

## Build and test

Run these from the repository root:

```bash
dotnet restore TokenGuard.sln
dotnet build TokenGuard.sln --configuration Release --no-restore
dotnet test TokenGuard.sln --configuration Release --no-build
```

The build is expected to finish with zero warnings.

`TokenGuard.sln` holds the three packages under `src/`, the two Codexplorer sample projects under `samples/`, and three test projects:

| Project | Use for |
| --- | --- |
| `tests/TokenGuard.Tests` | Unit tests for token counting, strategies, models and provider adapters. |
| `tests/TokenGuard.IntegrationTests` | Message flow and strategy wiring across components, with deterministic fakes at the edges. |
| `tests/Codexplorer.Automation.Tests` | Tests for the benchmark automation in `samples/Codexplorer.Automation`. |

The test suites in the solution run offline and need no API key. The Codexplorer samples call a live LLM provider and read their API keys from the environment; see [samples/Codexplorer/README.md](samples/Codexplorer/README.md). Never put a key in code, tests, samples, logs or commits.

Tests use xUnit and FluentAssertions. Keep one behavior per test, assert on public outcomes, and add a regression test with every bug fix.

## README snippet check

`tests/TokenGuard.ReadmeSnippets` is outside the solution. It holds the quick-start code and the fragments shown in `README.md` and in the package readme files under `src/`. Run the check with:

```bash
./tests/TokenGuard.ReadmeSnippets/verify.sh
```

The script does three things:

1. Packs the three packages from the working tree into `artifacts/readme-snippets/feed`.
2. Builds `tests/TokenGuard.ReadmeSnippets` against those packed packages, using a private packages folder so a cached package of the same version is not picked up.
3. Checks that each tagged block in `Program.cs`, `AnthropicQuickStart.cs` and `OpenAIPackageReadme.cs` appears verbatim in the readme files that show it.

It builds the snippets and does not run them, so it needs no API key. It restores the other package references from nuget.org, so it needs network access. Its output goes to `artifacts/`, which is gitignored.

If you change a quick-start block, change it in the snippet source and in every readme that carries it, then run the script.

## Branches

Create a branch for your work. Name it with one of these prefixes and a short description of the work:

| Prefix | Use for |
| --- | --- |
| `feat/` or `feature/` | New functionality |
| `fix/` or `bugfix/` | A normal bug fix |
| `hotfix/` | An urgent production fix |
| `refactor/` | Restructuring without a behavior change |
| `chore/` | Maintenance and cleanup |
| `docs/` | Documentation only |
| `test/` | Tests only |
| `perf/` | Performance improvements |
| `build/` | Build system or dependencies |
| `ci/` | CI/CD pipelines |
| `style/` | Formatting |
| `release/` | Release preparation |
| `revert/` | Reverting an earlier change |
| `spike/` or `experiment/` | Exploratory work |

## Commits

Use a [Conventional Commit](https://www.conventionalcommits.org/) subject with an accurate type and an optional scope, then a blank line, then two or three bullet points that explain the outcome. Keep each commit cohesive.

```text
feat(core): add sliding-window compaction strategy

- Drop the oldest unpinned messages until the context fits the budget.
- Keep tool-call and tool-result pairs together when trimming.
```

## Pull requests

- Keep a pull request to one focused change.
- Flag any new package reference. TokenGuard ships as NuGet packages, so every reference becomes a dependency of its consumers.
- Add an entry under `Unreleased` in [CHANGELOG.md](CHANGELOG.md) for a change that users of the packages will notice.
- The `Release Validation` workflow in [.github/workflows/release-validation.yml](.github/workflows/release-validation.yml) runs on every pull request. It builds, tests, packs the packages, runs the snippet check, and fails if a tracked file changed during the build.

Releases are published by the maintainer; the process is in [docs/release-process.md](docs/release-process.md).

## License

Contributions are accepted under the [MIT License](LICENSE) that covers the repository.
