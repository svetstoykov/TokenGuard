# bat — Developer Workflow & Architecture Baseline

## Project Identity
- **Name**: `bat` — A cat(1) clone with syntax highlighting and Git integration
- **Version**: `0.26.1` (from `Cargo.toml`)
- **Edition**: Rust 2021
- **License**: MIT OR Apache-2.0
- **Repository**: https://github.com/sharkdp/bat

---

## 1. Build Manifest

**File:** `Cargo.toml`

This is a standard Cargo manifest with:
- Library crate (`src/lib.rs`) + binary target (`--bins`)
- Custom build script: `build = "build/main.rs"` — bakes syntaxes/themes into the binary at compile time via the `assets/create.sh` pipeline
- Release profile tuned for compact binaries: LTO enabled, codegen-units = 1, stripping

Key feature flags:
| Feature | Purpose |
|---------|---------|
| `application` (default) | Full `bat` CLI; pulls in bugreport, build-assets, minimal-application |
| `minimal-application` | Stripped-down app mode; includes clap, etcetera, paging, regex-onig, wild |
| `git` (default) | `gix` dependency for showing git modifications alongside file content |
| `paging` | Shell-pager support (minus pager library, grep-cli, shell-words) |
| `regex-onig` / `regex-fancy` | Mutually exclusive — pick one regex engine for syntax highlighting |
| `build-assets` | At-build-time parsing of `.sublime-syntax` and theme files into cached binary format |

---

## 2. Minimum Supported Rust Version (MSRV)

**Setting in `Cargo.toml`, line 14:**
```toml
rust-version = "1.88"
```

The README ("From source" section) independently states "Rust 1.79.0 or higher" — note this is slightly behind the manifest's declared 1.88, so `Cargo.toml` is the authoritative value. The comment immediately above the field reads:

> *You are free to bump MSRV as soon as a reason for bumping emerges.*

---

## 3. Running Tests

### Primary test command

```bash
cargo test
```

This runs both unit tests (inside `src/`) and integration tests (defined in `tests/integration_tests.rs`). Each test invokes the compiled `bat` binary through `assert_cmd` with varying arguments, asserting on stdout/stderr output.

### Syntax-highlighting regression tests

Located under `tests/syntax-tests/`:
- Helper script: `tests/syntax-tests/update.sh`
- Reference outputs live at `tests/syntax-tests/highlighted/`
- Before running them, ensure the built `bat` binary is on `$PATH`:
  ```bash
  export PATH="$PATH:$(pwd)/target/debug"
  ```

### Key dev-dependencies for testing
| Crate | Role |
|-------|------|
| `assert_cmd` | Spawn `bat` process and capture output |
| `predicates` | Fluent assertion predicates on cmd output |
| `serial_test` | Prevent concurrency races between parallel cargo-test invocations |
| `expect-test` | Snapshot-style expected-output assertions |
| `tempfile` | Sandbox files for safe integration testing |
| `nix` (Unix only) | Terminal manipulation helpers (termios) |

---

## 4. CI Pipeline

**Location:** `.github/workflows/CICD.yml`

The workflow covers at minimum:
- `cargo build --bins` — debug builds
- `cargo test` — full test suite
- Syntax/regression tests
- Changelog verification on PRs (auto-fails if required changelog entry is missing for behavioral changes)
- Releases are published manually from the `release-checklist.md` process

---

## 5. Configuration Model

### User config file
Generated with `bat --generate-config-file`. Location discovered via `bat --config-file`. Supports TOML-like list-of-args syntax (one flag per line). Example:
```toml
--theme="TwoDark"
--style="numbers,changes,header"
--italic-text=always
--map-syntax "*.ino:C++"
```

Environment variables: `BAT_THEME`, `BAT_STYLE`, `BAT_CONFIG_PATH`, `BAT_CONFIG_DIR`, `PAGER`, `BAT_PAGER`.

### System-wide config
Linux/macOS: `/etc/bat/config`
Windows: `C:\ProgramData\bat\config`

User-level config is appended on top of system-level config.

---

## 6. Core Source Structure

| Module | Responsibility |
|--------|----------------|
| `src/lib.rs` | Public API re-exports (`Printer`, `Input`, `OutputType`) |
| `src/controller.rs` | Central orchestrator routing input → page logic → output |
| `src/config.rs` | Builds `AppConfiguration` from args, env vars, and config file |
| `src/cli.rs` | Clap argument parser definition |
| `src/syntax_mapping/` | Language auto-detection, file-type association rules |
| `src/preprocess/` | LESSOPEN-style preprocessing hooks |
| `build/main.rs` | Build-script that compiles embedded syntaxes/themes and generates completions |

Highlighting engine: `syntect` (Sublime Text syntax/theme loader), using either `onig` (Oniguruma) or `fancy-regex` backends.

Git integration: `gix` for non-blocking repo state queries (showing modified/untracked markers in the gutter).

---

## 7. Installing from Source

```bash
# Debug build
cargo build --bins

# Release install
cargo install --path . --locked
```

Custom builds (with modified syntaxes/themes baked in):
```bash
bash assets/create.sh          # regenerates asset definitions
cargo install --path . --locked --force
```

Shell completions: `bat --completion <shell>`

---

## 8. Contributing Workflow

1. Open a feature request ticket before implementing new features
2. Update `CHANGELOG.md` (auto-verified by CI)
3. Add regression tests:
   - Functional → append to `tests/integration_tests.rs`
   - Theme/syntax → regenerate via `tests/syntax-tests/update.sh`
4. PR merges require maintainer approval

### Maintainers
- sharkdp
- eth-p
- keith-hall
- Enselic

---

## Summary

| Question | Answer |
|----------|--------|
| Build manifest file | **`Cargo.toml`** |
| MSRV declaration | **`rust-version = "1.88"`** (in `Cargo.toml`) |
| Test command | **`cargo test`** |
