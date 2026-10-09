Based on the provided source excerpts, here is the technical trace of the pager selection architecture, prepared for `evidence.md`.

***

### Pager Selection Analysis

#### 1. Selection Path and Logic
**Source Path**: `src/pager.rs`  
**Selection Function**: `get_pager` (Line 93)

The pager resolution logic executes inside `get_pager` (Lines 97–102), adhering to a strict priority ladder:
1.  **Configuration**: Highest priority. Derived from the `config_pager` argument (e.g., CLI flag or configuration file). Records source as `PagerSource::Config` (Line 98).
2.  **Application-Specific Environment**: `BAT_PAGER` environment variable. Records source as `PagerSource::EnvVarBatPager` (Line 99).
3.  **Generic Environment**: `PAGER` environment variable. Records source as `PagerSource::EnvVarPager` (Line 100).
4.  **Fallback**: If none of the above exist, the system defaults to `"less"`. Records source as `PagerSource::Default` (Line 101).

#### 2. Application-Specific Environment Source
The enum variant recording the application-specific environment source is **`PagerSource::EnvVarBatPager`** (defined at Line 11).

#### 3. Replacement Logic for Generic Pagers
The function explicitly intercepts and replaces specific generic pagers. This logic is isolated to the `else` branch regarding explicit preferences (Lines 109–120):

*   **Trigger**: Replacement occurs if and only if `source == PagerSource::EnvVarPager` (Line 109).
*   **Targets**: The system detects `PagerKind::More`, `PagerKind::Most`, and `PagerKind::Bat`.
*   **Replacements**: These are unconditionally replaced with the `less` binary (with zero arguments).
*   **Reasoning**:
    *   `more` and `most`: These pagers do not support terminal coloring, which `bat` requires for syntax highlighting (Line 111).
    *   `bat`: Setting `PAGER=bat` causes infinite recursion because `bat` attempts to invoke itself as its own pager (Line 114).

**Safety Guarantee**: The code strictly enforces that these silent replacements never apply if the pager was explicitly defined via `BAT_PAGER` or the command line interface, preserving explicit user intent (Lines 115–116).

#### 4. Verification Status
*   **Implementation**: The behavior is structurally verified within the provided excerpts (Lines 109–127). The `use_less_instead` boolean acts as a gatekeeper, ensuring only specific legacy pagers (`more`, `most`) and self-referencing pagers (`bat`) are sanitized when originating from the generic system variable.
*   **Call Sites & Tests**: As the provided excerpts contain only `pager.rs`, direct observation of external call sites and unit tests is currently unavailable. However, the internal control flow ensures any module invoking `get_pager` receives the corrected `Pager` struct prior to execution.