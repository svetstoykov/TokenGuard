# Baseline survey: `bat` (sharkdp/bat)

Living notes captured from a read-only reconnaissance of the cloned repository.
Scope so far: project layout, build configuration, contributor/developer workflow, tests, CI.
Nothing in the repository was modified; all notes live in this artifacts folder.

## 1. What the project is

`bat` is a `cat` clone with syntax highlighting and Git integration (Rust).
It ships as both a CLI application and a library (`bat as a library` is an explicit
release-checklist section), and the crate is consumed downstream via the `pretty_printer`
/ `PrettyPrinter` API.

## 2. Top-level layout

```
.cargo/          toolchain-level cargo config
.github/         CI workflows, dependabot, issue templates
assets/          syntax/theme assets + build-assets.sh driver
build/           Cargo build script and its helper modules
diagnostics/     diagnostics support files
doc/             end-user and maintainer docs, translated READMEs
examples/        example programs
src/             crate source (library + `bat` binary)
tests/           integration + snapshot tests and helpers
Cargo.toml       manifest (src/Cargo.lock also present)
CHANGELOG.md     very large; enforced per-PR (see §6)
CONTRIBUTING.md  contributor policy
README.md        install, customization, development sections
rustfmt.toml     "Defaults are used" (no custom formatting rules)
flake.nix/.lock  Nix dev shell
SECURITY.md, NOTICE, LICENSE-APACHE, LICENSE-MIT
```

## 3. Build configuration

`Cargo.toml`:
- Feature gate `application` drives whether the CLI binary is built versus library-only use.
  CI references a narrower `minimal-application` feature for the MSRV job.
- MSRV is *not* hardcoded in docs; it is read from crate metadata
  (`packages[0].rust_version`) by the release checklist and by CI.

`build/main.rs` (build script):
- Always calls `syntax_mapping::build_static_mappings()`.
- Conditionally calls `application::gen_man_and_comp()` under the `application` feature to
  generate the manpage and shell completions.
- Emits `cargo:rerun-if-changed=build/`.

`build/application.rs`:
- Generates manpage + shell completions from templates under `assets/manual/` and
  `assets/completions/`.
- Uses env vars `PROJECT_NAME`, `PROJECT_EXECUTABLE`, `CARGO_PKG_VERSION`; writes
  generated completion scripts and exposes e.g. `BAT_GENERATED_COMPLETION_PS1` / `_ZSH`.

`build/syntax_mapping.rs` (largest build helper, ~11 KB):
- Reads `*.toml` mapping definition files from `src/syntax_mapping/builtins/` and codegens a
  static table `BUILTIN_MAPPINGS: [(Lazy<Option<GlobMatcher>>, MappingTarget); N]`, written to
  `$OUT_DIR/codegen_static_syntax_mappings.rs` and later `include!`d.
- Platform filtering is compile-time via build-script `cfg`: subdirs `common`, `unix-family`,
  `bsd-family`, `linux`, `macos`, `windows` are selected based on the host/target.
  `common` is always included.
- Glob patterns support `${ENV_VAR}` interpolation; the parser hard-errors on stray `$`
  (deliberately strict — documented in a comment) and rejects empty matchers.
- `case_sensitive` defaults to **insensitive** (`DEFAULT_CASE = Case::Insensitive`) for
  backwards compatibility; brace expansion `{}` was permitted as of 2024-02-20.
- Duplicate matchers across files are a hard build error (`bail!("Rules with duplicate matchers found")`).
- Uses `syn` + `prettyplease` to parse and pretty-print the generated token stream.
- Handles a known `cargo vendor` bug (rust-lang/cargo#15080) by skipping missing builtin dirs.

`build/util.rs`: provides `render_template`.

`.cargo/config.toml`:
- Forces `-C target-feature=+crt-static` for Windows MSVC targets (x86_64, i686, aarch64) so
  released Windows binaries are statically linked against the CRT.
`.cargo/audit.toml` also present (cargo-audit configuration).

## 4. Source layout (`src/`)

Root modules (library surface):
- `assets.rs` (~31 KB) — syntax/theme asset loading.
- `printer.rs` (~37 KB) and `vscreen.rs` (~36 KB) — output rendering pipeline.
- `controller.rs`, `config.rs`, `theme.rs`, `line_range.rs`, `pretty_printer.rs`,
  `lib.rs`, `macros.rs`, `paging.rs`, `wrapping.rs`.

Subdirectories:
- `src/bin/bat/` — the CLI binary: `main.rs` (~17 KB), `app.rs` (~27 KB),
  `clap_app.rs` (~37 KB, argument definition), `config.rs`, `input.rs`, `directories.rs`,
  `assets.rs`, `completions.rs`.
- `src/syntax_mapping/` — `builtin.rs`, `ignored_suffixes.rs`, and `builtins/`
  (the TOML mapping definitions consumed by the build script).
- `src/assets/` — `build_assets/`, `assets_metadata.rs`, `build_assets.rs`,
  `lazy_theme_set.rs`, `serialized_syntax_set.rs` (compile-time asset serialization).

## 5. Tests

- `tests/integration_tests.rs` is the primary suite (~150 KB).
- `tests/snapshot_tests.rs` for snapshot-based output verification, backed by `tests/snapshots/`.
- `tests/assets.rs`, `tests/test_pretty_printer.rs`, `tests/system_wide_config.rs`.
- `tests/no_duplicate_extensions.rs` — enforces asset uniqueness (complements the build-script
  duplicate-matcher check).
- `tests/github-actions.rs` — test-side assertions about CI-related behaviour.
- Helpers: `tests/tester/`, `tests/utils/` (`command.rs`, `mocked_pagers.rs`), `tests/mocked-pagers/`.
- `tests/syntax-tests/` — syntax verification harness: `source/`, `highlighted/` golden files,
  `BatTestCustomAssets.sublime-syntax`, `create_highlighted_versions.py`,
  `compare_highlighted_versions.py`, `update.sh`, `regression_test.sh`, `test_custom_assets.sh`.
- `tests/benchmarks/` — `run-benchmarks.sh`, `highlighting-speed-src/`, `startup-time-src/`,
  `many-small-files/`.
- `tests/examples/` — `cache_source/`, `git/`, `regression_tests/` fixtures.
- `tests/scripts/` — `find-slow-to-highlight-files.py`, `license-checks.sh`.
- Tests require a `bat` executable on `PATH` (e.g. `target/debug/release`); CONTRIBUTING
  documents exporting the debug dir or `cargo install --path . --locked`.

## 6. Contributor workflow

- Clone must be **recursive** (`git clone --recursive`) — syntax/theme assets are submodules.
- Build: `cargo build --bins` (README §Development, ~line 890).
- Test: `cargo test` — runs unit + integration tests (README line ~894).
- CHANGELOG policy: every PR is expected to add a `CHANGELOG.md` entry referencing the PR
  number and author; some changes are exempt.
- Syntax/theme/syntax-test contributions are directed to `doc/assets.md` and
  `src/syntax_mapping/builtins/README.md`.
- Local formatting is governed by `rustfmt.toml`, which is simply "Defaults are used" —
  i.e. stock `cargo fmt`.

## 7. CI and automation

`.github/workflows/CICD.yml` (~20 KB) — main pipeline. Triggers: `workflow_dispatch`,
`pull_request`, `push` (branch-restricted).
Global env: `CICD_INTERMEDIATES_DIR: "_cicd-intermediates"` and
`MSRV_FEATURES: --no-default-features --features minimal-application,bugreport,build-assets`.

Jobs:
- `all-jobs` — aggregate gate (`if: always()`, `needs:` the rest) used for branch protection.
- `crate_metadata` — extracts crate info (name/version/MSRV) as job outputs feeding others.
- `lint` — "Ensure code quality"; installs `rustfmt,clippy` and runs `cargo fmt -- --check`
  (plus clippy).
- `min_version` — "Minimum supported rust version"; depends on `crate_metadata`, installs the
  MSRV toolchain via `dtolnay/rust-toolchain@master` and builds with `MSRV_FEATURES`.
- `license_checks` — runs `tests/scripts/license-checks.sh`; fails the build if any file
  (including submodules) mentions "General Public License", i.e. blocks GPL-incompatible assets.
  The script whitelists itself plus two known false positives (a Matlab `Octave-function.sublime-snippet`
  and the JSP `LICENSE.md`).
- `test_with_new_syntaxes_and_themes` — rebuilds assets and runs the suite against updated syntax/theme sets.
- `test_with_system_config` — prepares env vars and runs tests exercising system-wide config.
- `documentation` — builds docs and checks documentation.
- `cargo-audit` — installs `cargo-audit --locked` and runs `cargo audit`.
- `build` — matrix job `${{ matrix.job.target }} (${{ matrix.job.os }})`; produces tarballs and
  Debian packages, uploads them, and publishes via `softprops/action-gh-release@v2` only when
  `steps.is-release.outputs.IS_RELEASE`.
- `winget` — "Publish to Winget", `needs: build`, uses pinned
  `vedantmgoyal9/winget-releaser@a8fff44b...`.

`.github/workflows/require-changelog-for-PRs.yml`:
- `on: pull_request`; job `check-changelog` on `ubuntu-latest`.
- Skips dependabot PRs (`if: github.actor != 'dependabot[bot]'`).
- Resolves the PR submitter login, then greps **added** `CHANGELOG.md` lines for a pattern
  matching `#${PR_NUMBER}` and the submitter — mechanizing the CONTRIBUTING changelog rule.

`.github/dependabot.yml`: monthly cargo / git-submodule / github-actions updates at 04:00
Europe/Berlin; explicitly ignores `git2` 0.13.17.

Also present: `.codecov.yml`, `FUNDING.yml`, and `ISSUE_TEMPLATE/`
(bug_report, feature_request, question, syntax_request).

## 8. Docs and release process

`doc/`:
- `assets.md` — how to add syntaxes/themes/syntax tests (heavily referenced by CONTRIBUTING).
- `release-checklist.md` — maintainer release runbook.
- `alternatives.md`, `sponsors.md`, `long-help.txt`, `short-help.txt`.
- Translated READMEs: `README-ja.md`, `README-ko.md`, `README-ru.md`, `README-zh.md`.

Release checklist highlights:
1. Bump version in `Cargo.toml`, then `cargo build` to refresh `Cargo.lock`.
2. Read MSRV with
   `cargo metadata --no-deps --format-version 1 | jq -r '.packages[0].rust_version'`.
3. Propagate version + MSRV into `README.md` and all translated READMEs.
4. Update `CHANGELOG.md`; open a fresh "unreleased" section.
5. Run the checklist's per-area sections: Syntaxes, Themes, "bat as a library".

`assets/build-assets.sh` (`set -euo pipefail`): updates submodules and patch files, then runs
`bat cache --build --blank --acknowledgements --source="$ASSET_DIR" --target="$ASSET_DIR"`
to regenerate serialized assets.

## 9. Open questions / not yet verified

- Exact full list of `MSRV_FEATURES` implications (`minimal-application`, `bugreport`,
  `build-assets` feature definitions) — features not yet read from `Cargo.toml` line-by-line.
- Full `rust-version` value (MSRV number) not yet read.
- `src/lib.rs` public API surface not yet enumerated in detail.
- `diagnostics/` directory contents not yet inspected.
- The `build` job matrix targets list (lines ~156–200 of `CICD.yml`) not yet enumerated.
