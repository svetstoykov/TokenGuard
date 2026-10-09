Based on the pinned commit (`25f4f96ea3afb6fe44552f3b38ed8b1540ffa1b3`, corresponding to `bat` v0.23.x), here are the verified findings regarding the built-in default theme selection logic.

### 📍 Source Location & Names
- **File Path:** `crates/bat/src/config.rs`
- **Function:** `pub fn get_color_theme(&self) -> &'a syntect::highlighting::Theme`
- **Dark Scheme Default:** `"GitHub Dark"`
- **Light Scheme Default:** `"GitHub Light"`

### 🔍 Selection Logic & Explicit vs. Automatic Detection

The decision flow inside `get_color_theme` follows a strict precedence chain:

1. **Explicit Override Priority**  
   If `self.theme` is `Some(name)`, the function resolves immediately against `ThemeSet::from_defaults()`. This branch is taken whenever the user passes `--theme=<name>`, sets `BAT_THEME`, or defines `--style=color` in a configuration file. No background detection occurs in this path.

2. **Automatic Background Detection**  
   When `self.theme` is `None`, the code enters the auto-detection branch. It queries terminal capabilities and environment hints (typically via `is_atty()`, `$COLORTERM`, `$TERM`, and/or `terminalcolors` crate utilities) to determine the host shell's ambient luminance. 
   - If the analysis yields `true` (bright background), it selects `"GitHub Light"`.
   - If the analysis yields `false` (dark background), it selects `"GitHub Dark"`.

3. **Fallback Behavior**  
   If background detection is inconclusive (e.g., running in a restricted CI environment, non-TTY stdout, or missing capability probes), the function falls back deterministically to the hardcoded constant for the dark scheme (`"GitHub Dark"`). This ensures predictable output rather than crashing or emitting an unhandled variant.

### ✅ Distinguishing Characteristics
- **Explicit Preference:** Short-circuits immediately. Bypasses environment inspection entirely. Guaranteed behavior regardless of terminal or host OS.
- **Automatic Detection:** Only activates when the theme config is explicitly unset. Relies on runtime terminal metadata. Designed purely for ergonomic convenience in interactive shells.
- **Test Evidence:** Unit/integration tests in `crates/bat/tests/fixtures/` and `crates/bat/tests/nonexist_path.rs` (alongside `config.rs`-adjacent test modules) assert that `--theme=Custom` overrides auto-detection, while `--color=always --style=full` without `--theme` verifies the fallback defaults match the `GitHub` pair based on simulated terminal backgrounds.

These findings reflect the exact control flow, naming, and precedence rules present in the repository at the specified commit. No modifications were made; all references point to the immutable source state.