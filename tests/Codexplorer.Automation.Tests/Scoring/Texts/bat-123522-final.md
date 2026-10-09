Live work stopped. Here's the close-out.

## Concrete progress

**Repository identified:** `bat` (sharkdp/bat) — a `cat` clone with syntax highlighting, Rust, dual CLI/library crate.

**Evidence gathered (read-only):**
- **Top-level layout** — `.cargo/`, `.github/`, `assets/`, `build/`, `diagnostics/`, `doc/`, `examples/`, `src/`, `tests/`, plus `Cargo.toml`, `Cargo.lock`, `CHANGELOG.md`, `CONTRIBUTING.md`, `README.md`, `rustfmt.toml`, `SECURITY.md`, `NOTICE`, licenses, `flake.nix`.
- **Build machinery** — `build/main.rs` orchestrates; `build/application.rs` generates manpage + completions; `build/syntax_mapping.rs` (read in depth) codegens `BUILTIN_MAPPINGS` from platform-scoped TOML files in `src/syntax_mapping/builtins/`, with strict glob parsing (`${ENV}` interpolation, case-insensitive default, duplicate-matcher hard error, brace expansion since 2024-02-20).
- **Toolchain config** — `.cargo/config.toml` forces `+crt-static` for Windows MSVC targets; `.cargo/audit.toml` present.
- **Source layout** — root modules (`printer.rs`, `vscreen.rs`, `assets.rs`, `controller.rs`, …) plus `src/bin/bat/` (with file sizes for all 8 files) and `src/syntax_mapping/`, `src/assets/`.
- **Tests** — integration (~150 KB), snapshot, assets, github-actions, system-wide-config, pretty-printer, plus `syntax-tests/`, `benchmarks/`, `scripts/`, `tester/`, `utils/`.
- **CI** — full job inventory for `CICD.yml`: `all-jobs`, `crate_metadata`, `lint`, `min_version`, `license_checks`, `test_with_new_syntaxes_and_themes`, `test_with_system_config`, `documentation`, `cargo-audit`, `build` (matrix + release publish), `winget`; global env `CICD_INTERMEDIATES_DIR` and `MSRV_FEATURES`.
- **Changelog enforcement** — `require-changelog-for-PRs.yml` greps added `CHANGELOG.md` lines for PR number + submitter, skipping dependabot.
- **Licensing guard** — `tests/scripts/license-checks.sh` fails on any "General Public License" match (submodules included) with three documented exclusions.
- **Contributor workflow & release process** — recursive clone, `cargo build --bins`, `cargo test`, `bat` on `PATH`; release checklist MSRV extraction via `cargo metadata | jq`.

**Artifact written:** `baseline.md` — a single consolidated, sectioned summary (project, layout, build config, source, tests, contributor workflow, CI, docs/release, open questions).

## Unfinished work / blockers
- None blocking. The remaining items are simply *not yet read*, not blocked:
  - Exact `rust-version` value and the definitions of `minimal-application` / `bugreport` / `build-assets` features in `Cargo.toml`.
  - `src/lib.rs` public API surface.
  - `diagnostics/` contents.
  - The `build` job's full target matrix (≈lines 156–200 of `CICD.yml`).
- Tooling quirk worth flagging for any continuation: several `grep` calls returned repo-wide matches despite a `pathGlob`/`path` argument, and large-file reads were intermittently dropped from context and had to be re-fetched. Prefer narrow `read_range` calls and expect to re-verify.

## Next recommended steps
1. Read `Cargo.toml` fully to capture `rust-version`, feature graph (`application`, `minimal-application`, `bugreport`, `build-assets`), and dependency layout.
2. Enumerate the `build` job matrix and release-artifact steps in `CICD.yml` (~156–460).
3. Skim `src/lib.rs` for the public library surface and `diagnostics/` contents.
4. Append each to `baseline.md` as new sections when work resumes.

Stopping here as instructed.