### Factual Answers from Excerpts

**Source path translating flags to detection policies:**  
`crates/core/flags/hiargs.rs` → `fn from_low_args` builds the dual `BinaryDetection` struct, branching on `low.binary` and `low.null_data` to assign `explicit` and `implicit` policies.

**Flag-model variant for treating binary data as text:**  
`BinaryMode::AsText` (defined in `crates/core/flags/lowargs.rs`). Alongside the `low.null_data` boolean, it forces the detection policy to `none()`.

**Searcher constructor that stops scanning at a binary byte (qualified):**  
`grep::searcher::BinaryDetection::quit(binary_byte: u8)` — instantiated in practice as `grep::searcher::BinaryDetection::quit(b'\x00')` for implicit/directory searches.

---

### Concept Explanations (Grounded in Excerpts)

* **NUL bytes (`\x00`):** Serve as the sentinel value for binary detection. Non-text files often contain long contiguous runs of NUL bytes. To avoid allocating impractically large lines when treating such files as line-oriented, ripgrep replaces detected NUL bytes with line terminators in conversion mode.
* **Explicit paths:** Files passed directly as CLI arguments. Ripgrep enforces a strict rule that explicitly given files must always be searched; therefore, the `quit` strategy is disabled for them. They fallback to `convert` (NUL replacement) or `none`, guaranteeing no mid-file termination or filtering.
* **Text mode:** Corresponds to `BinaryMode::AsText`. It maps directly to `BinaryDetection::none()`, completely disabling binary heuristics. All files are streamed as raw text regardless of content.
* **Null-data mode:** Triggered by the `--null-data` flag. Internally, `low.null_data` is evaluated identically to `AsText` (`matches!(low.binary, BinaryMode::AsText) || low.null_data`), routing to `none()` so that NUL bytes are ignored and do not trigger stop or replace behaviors.

---

### Proposed `evidence.md` Content

```markdown
# Binary Detection Policy Evidence (ripgrep 4649aa9)

## Translation Source
- **Path:** `crates/core/flags/hiargs.rs`
- **Symbol:** `BinaryDetection::from_low_args(&State, &LowArgs) -> BinaryDetection`
- **Behavior:** Constructs separate `explicit` and `implicit` `BinaryDetection` strategies. Branches on `low.binary` enum and `low.null_data` bool. Applies `convert(b'\x00')` for explicit paths, `quit(b'\x00')` for implicit paths, unless overridden to `none()`.

## Flag-Model Variant
- **Path:** `crates/core/flags/lowargs.rs`
- **Symbol:** `BinaryMode::AsText`
- **Behavior:** Disables binary detection entirely when matched alongside `low.null_data`. Routes policy construction to `BinaryDetection::none()`.

## Stop-on-Binary Constructor
- **Qualified Name:** `grep::searcher::BinaryDetection::quit(binary_byte: u8)`
- **Usage in Codebase:** `grep::searcher::BinaryDetection::quit(b'\x00')` applied to implicit/directory traversal scans. Immediately terminates the scan upon encountering the sentinel byte.

## Concept Summary
- **NUL Bytes (`\x00`):** Sentinel for binary classification. Replaced by line terminators in `convert` mode to prevent heap exhaustion from massive synthetic lines inherent in binary formats.
- **Explicit Paths:** Bypass the `quit` heuristic per project design philosophy ("an explicitly given file is always searched"). Forced into `convert` or `none` to guarantee complete ingestion or full match discovery.
- **Text Mode (`AsText`):** Absolute opt-out from binary heuristics. Equivalent to `none()`.
- **Null-Data Mode (`--null-data`):** Treated identically to `AsText` in policy resolution (`|| low.null_data`). Prevents NULs from acting as sentinel triggers, enabling NUL-delimited record searching.
```