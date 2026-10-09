# `bat` Repository Baseline Survey

Status: **in progress (paused at runner request)**. All notes below are backed by
repository reads done in this session. No repository source, test, or config files
were modified. Artifacts are the only outputs.

## 1. Project identity

- Repository: `bat` (upstream `sharkdp/bat`) — a syntax-highlighting `cat` clone
  **and** a reusable Rust library.
- Library root: `src/lib.rs`, which exports the public API surface including
  `PrettyPrinter`, `Input`, `Syntax`, `SyntaxMapping`, `MappingTarget`,
  `WrappingMode`, and `PagingMode`.
- Binary root: `src/bin/bat/` (the `bat` CLI).

## 2. Documentation files (root)

- `README.md` — primary user + developer docs (see Development section, below).
- `CONTRIBUTING.md` (4238 bytes) — contribution workflow.
- `CHANGELOG.md` (65747 bytes) — sectioned release history; recurring section
  headings observed: `## Features`, `## Bugfixes`, `## Other`, `## Syntaxes`,
  `## Themes`, `## New themes`, `## Performance`, `## `bat` as a library`.
- `SECURITY.md` (129 bytes) — security policy stub.
- `NOTICE`, `LICENSE-APACHE`, `LICENSE-MIT` — licensing.
- `.gitmodules` (12794 bytes) — many vendored syntax/theme submodules.

### `doc/` directory (confirmed by `list_directory`)

Contents: `alternatives.md`, `assets.md`, `logo-header.svg`, `long-help.txt`,
`short-help.txt`, `release-checklist.md`, `sponsors.md`, and localized READMEs
`README-{ja,ko,ru,zh}.md`.

Note / discrepancy: `CONTRIBUTING.md` references `doc/` paths, and the real docs
directory is in fact `doc/`. (Earlier `file_tree` output presented README variants
in a way that was easy to misread; direct listing resolved it.)

## 3. Build and toolchain configuration

- `Cargo.toml` (3773 bytes) — declares **`rust-version = "1.88"`**.
- `Cargo.lock` — locked dependency graph; CI uses `--locked` extensively.
- `rustfmt.toml` (21 bytes) — single line comment `# Defaults are used` (i.e. no
  custom formatting rules; formatting is `rustfmt` defaults).
- `flake.nix` + `flake.lock` — Nix dev shell providing `cargo`.
- `.envrc` — contains `use flake`.
- `.cargo/config.toml` — sets `+crt-static` for MSVC targets (Windows static CRT).
- `.cargo/audit.toml` (65 bytes) — `cargo audit` configuration.
- Build script lives under `build/` (not a root `build.rs`), containing
  `application.rs`, `main.rs`, `syntax_mapping.rs`, `util.rs`. It generates static
  syntax-mapping data and, under the `application` feature, man pages/completions.
  The manifest references `cargo:rerun-if-changed=build/`.

### Known inconsistency to flag

- `README.md` states Rust **1.79.0** as the minimum supported version, while
  `Cargo.toml` declares `rust-version = "1.88"`. These disagree and should be
  reconciled (the release checklist derives MSRV from `cargo metadata`).

## 4. Source layout

`src/` modules (non-exhaustive): `controller`, `printer`, `style`, `theme`,
`paging`, `preprocessor`, plus `assets` and `syntax_mapping`.

- `src/assets/` — `assets_metadata.rs`, `build_assets.rs`, `build_assets/`,
  `lazy_theme_set.rs`, `serialized_syntax_set.rs`.
- `src/syntax_mapping/` — `builtins/`, `builtin.rs`, `ignored_suffixes.rs`.
- `src/syntax_mapping/builtins/` — OS-scoped subdirectories: `bsd-family`,
  `common`, `linux`, `macos`, `unix-family`, `windows`, plus `README.md` (4792
  bytes) documenting how to add mappings.
- `src/bin/bat/` — CLI implementation: `clap_app.rs` (37440 bytes, argument
  definitions), `main.rs`, `app.rs` (27018 bytes, application wiring), `assets.rs`,
  `config.rs`, `completions.rs`, `directories.rs`, `input.rs`.

## 5. Tests and quality scripts

`tests/` top level: `integration_tests.rs` (153394 bytes — the large primary suite),
`snapshot_tests.rs`, `test_pretty_printer.rs`, `assets.rs`, `github-actions.rs`,
`no_duplicate_extensions.rs`, `system_wide_config.rs`.

Subdirectories:

- `tests/benchmarks/` — `run-benchmarks.sh` (6976 bytes) with
  `highlighting-speed-src`, `many-small-files`, `startup-time-src`.
- `tests/examples/`, `tests/mocked-pagers/`, `tests/snapshots/`, `tests/tester/`,
  `tests/utils/`.
- `tests/scripts/` — `find-slow-to-highlight-files.py` and `license-checks.sh`
  (latter enforces no GPL-licensed files are pulled in).
- `tests/syntax-tests/` — files: `compare_highlighted_versions.py` (2169),
  `create_highlighted_versions.py` (3512), `regression_test.sh` (360),
  `test_custom_assets.sh` (2158), `update.sh` (201),
  `BatTestCustomAssets.sublime-syntax` (616); directories `source/` and
  `highlighted/` holding paired inputs/expected-highlight outputs.

Asset generation script: `assets/create.sh` — initializes submodules, applies then
reverses patches, and runs `bat cache --build ...` to regenerate embedded syntaxes
and themes. Also `assets/completions/` holds completion templates such as
`assets/completions/bat.fish.in`.

## 6. CI/CD (`.github/`)

Workflows directory contains exactly two files:

- `CICD.yml` (20094 bytes) — the main multi-job pipeline. Jobs observed:
  - `all-jobs` — aggregator gate (`if: always()`, `needs:` the real jobs).
  - `crate_metadata` — extracts crate name/version and MSRV as job outputs.
  - `lint` — "Ensure code quality": `cargo fmt -- --check` plus Clippy
    (`dtolnay/rust-toolchain@stable` with `rustfmt,clippy`).
  - `min_version` — installs the MSRV toolchain from `crate_metadata` outputs and
    runs `cargo test --locked ${{ env.MSRV_FEATURES }}`.
  - `license_checks` — license checks (via `tests/scripts/license-checks.sh`).
  - `test_with_new_syntaxes_and_themes` — checks out with submodules, installs
    `bat`, runs `bash assets/create.sh`, reinstalls, then runs
    `cargo test --locked --release`, the `--ignored` assets tests,
    `tests/syntax-tests/regression_test.sh`, `bat --list-languages`,
    `bat --list-themes`, and `tests/syntax-tests/test_custom_assets.sh`.
  - `test_with_system_config` — sets `BAT_SYSTEM_CONFIG_PREFIX` and runs the
    `system_wide_config` tests with `--ignored`.
  - `documentation` — runs
    `cargo doc --locked --no-deps --document-private-items --all-features` and
    shows the man page.
  - `cargo-audit` — installs and runs `cargo audit`.
  - `build` — cross-platform build/test matrix (`${{ matrix.job.target }}` ×
    `${{ matrix.job.os }}`), with per-target install prerequisites, optional
    `cross` usage, `--locked` test runs, `bat --diagnostic`, and feature checks
    for `regex-onig`, `regex-onig,git`, `regex-onig,paging`,
    `regex-onig,git,paging`, and `minimal-application`. Produces tarballs and
    Debian packages; uploads artifacts; publishes archives/packages on release.
  - `winget` — publishes to Winget (`needs: build`).
- `require-changelog-for-PRs.yml` (1199 bytes) — named `Changelog`; enforces a
  `CHANGELOG.md` entry referencing the PR number and submitter; skips dependabot.

Other `.github/` assets: `ISSUE_TEMPLATE/` (`bug_report.md`, `feature_request.md`,
`question.md`, `syntax_request.md`), `dependabot.yml` (monthly updates for `cargo`,
`gitsubmodule`, `github-actions`; ignores `git2`), `FUNDING.yml`, and a Codecov
config file (listing vs. read path quirk noted; not yet fully read).

## 7. Contribution workflow (from `CONTRIBUTING.md` / docs)

- Every PR adding user-visible change must add a `CHANGELOG.md` entry formatted
  with `#<PR-number> <submitter>`, enforced in CI.
- Syntax-mapping contributions are documented in
  `src/syntax_mapping/builtins/README.md`.
- Asset and syntax-testing workflow documented in `doc/assets.md`.
- Release process in `doc/release-checklist.md`: bump version in `Cargo.toml`,
  derive MSRV via `cargo metadata ... rust_version`, update the localized READMEs
  and `CHANGELOG.md`, with dedicated sections for Syntaxes / Themes / Library;
  notes an auto-merge-dependabot workflow.

## 8. Open items / not yet verified

- Full read of `CICD.yml` beyond the grep-matched lines (matrix targets, env
  vars such as `MSRV_FEATURES`, packaging details).
- Exact contents of `SECURITY.md`, `.cargo/audit.toml`, and the Codecov config.
- Deeper inspection of core source modules (`controller`, `printer`, `style`,
  `theme`, `paging`, `preprocessor`) and `tests/tester/mod.rs`.
- Confirmation of README "Development" section instructions vs. the MSRV
  discrepancy noted in section 3.
