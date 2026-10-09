### Factual Answers (Grounded in Provided Excerpts)

**Source File Path:** `src/theme.rs`  
**Function Name:** `default_theme(color_scheme: ColorScheme)` (lines 30–35)  
**Exact Built-in Default Theme Names:**
- Dark: `"Monokai Extended"`
- Light: `"Monokai Extended Light"`

---

### Selection Logic: Explicit Preference vs. Automatic Background Detection

The theme resolution flow is orchestrated by `ThemeOptions` (lines 45–56) and `ThemePreference` (lines 67–99), with detection delegated to `TerminalColorSchemeDetector` in the hidden `theme_impl` call (lines 24–26):

1. **Automatic Detection (Default Behavior):**  
   `ThemePreference::default()` resolves to `Auto(Default::default())` (line 81). When the preference is `Auto`, the runtime invokes `TerminalColorSchemeDetector` to probe the actual terminal background/brightness. Only after this probe does the system resolve whether to apply dark or light theming.

2. **Explicit Static Preference:**  
   Setting `BAT_THEME` (or CLI `--theme`) parses into `ThemePreference::Fixed`, `Dark`, or `Light` (lines 94–97). These three variants **skip color scheme probing entirely**. The chosen theme is applied unconditionally, regardless of the terminal's background.

3. **Scheme-Aware Overrides (Hybrid Mode):**  
   Even when `Auto` is active, `BAT_THEME_DARK` / `BAT_THEME_LIGHT` (or `--theme-dark` / `--theme-light`) populate `ThemeOptions.theme_dark` and `theme_light` (lines 51–55). During auto-detection, the resolver applies these user-supplied values first. If either is `None`, the resolver falls back to `default_theme()`, which returns the built-in `"Monokai Extended"` variants.

*Note on environment variable naming:* Lines 13–15 contain a copy-paste artifact where `BAT_THEME_DARK` and `BAT_THEME_LIGHT` are both assigned the literal string `"BAT_THEME"`. The doc comments (lines 12–15) and struct field documentation correctly describe their intended purpose as distinct environment variables and CLI flags.

---

### Proposed `evidence.md` Content

```markdown
# Theme Selection Logic Evidence

## Source Location
- **File:** `src/theme.rs`
- **Function:** `default_theme(color_scheme: ColorScheme)` (lines 30–35)
- **Entry Point:** `theme(options: ThemeOptions)` (lines 24–26) routes through `theme_impl` with `TerminalColorSchemeDetector`.

## Built-in Default Themes
- **Dark Scheme:** `"Monokai Extended"`
- **Light Scheme:** `"Monokai Extended Light"`

## Selection Logic: Explicit Preference vs. Automatic Detection
The resolution pipeline is defined by `ThemeOptions` (lines 45–56) and `ThemePreference` (lines 67–99).

- **Automatic Detection (Default):** `ThemePreference::default()` yields `Auto(Default::default())` (line 81). When `Auto`, `theme_impl` calls `TerminalColorSchemeDetector` to read the terminal's actual background color/brightness before deciding between dark and light variants.
- **Explicit Preference:** Setting `BAT_THEME` (`--theme`) parses to `Fixed`, `Dark`, or `Light` (lines 94–97). Any of these bypass `TerminalColorSchemeDetector` and force a static theme application, ignoring the terminal's background.
- **Per-Scheme Overrides:** `BAT_THEME_DARK` / `BAT_THEME_LIGHT` (`--theme-dark` / `--theme-light`) fill `ThemeOptions.theme_dark` and `theme_light`. During auto-resolution, these override the fallback chain before `default_theme()` is reached.
- **Fallback Chain:**
  1. Evaluate `ThemePreference`. If `Fixed`/`Dark`/`Light`, apply immediately.
  2. If `Auto`, run `TerminalColorSchemeDetector`.
  3. Apply resolved `theme_dark` or `theme_light` if populated.
  4. Otherwise, delegate to `default_theme(detected_color_scheme)` returning the Monokai variants.
```

---

### Final Verification Summary
- **Path:** `src/theme.rs`
- **Function:** `default_theme`
- **Dark Theme:** `"Monokai Extended"`
- **Light Theme:** `"Monokai Extended Light"`
- **Distinction:** `Auto` triggers `TerminalColorSchemeDetector` for background inference; `Fixed`/`Dark`/`Light` (set via `BAT_THEME`/`--theme`) statically override detection. `theme_dark`/`theme_light` (via `BAT_THEME_DARK`/`BAT_THEME_LIGHT` or `--theme-*`) provide explicit per-scheme overrides that short-circuit the built-in defaults before falling back to `default_theme()`.