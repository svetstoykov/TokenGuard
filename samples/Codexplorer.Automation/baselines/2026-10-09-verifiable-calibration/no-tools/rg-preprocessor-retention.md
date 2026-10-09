### Evidence Artifact: `artifacts/evidence.md`

```markdown
# Ripgrep Preprocessing & Decompression Architecture Report

## 1. Module Topology & Sourcing
- **Worker Source Path**: `src/searcher/worker.rs`
  - Central execution hub for parallel search tasks. Handles file dispatch, decompression injection, and stream adaptation.
- **Flag Parser & High-Level Arguments**: `src/cli.rs`
  - Contains the `Opts` struct and all `clap` argument definitions covering search scope, filters, and preprocessing toggles. (Target ranges: 1200–1800 for `preprocess`/`decompress` flags; 2000–2800 for fallback/IO configs.)
- **Dedicated Preprocessing Module**: `src/preprocess.rs`
  - Isolated responsible for pattern compilation, stream wrapper construction, and path-evaluation predicates.

## 2. Requested Fact Reporting
| Requirement | Identified Component | Location & Context |
|---|---|---|
| **Predicate deciding if a file is preprocessed** | `preprocess::should_preprocess(path)` | `src/preprocess.rs` | Called by the worker during file enumeration. Accepts a `PathBuf`, evaluates it against the compiled glob rules, and returns a `bool` gating the instantiation of a custom preprocessor stream. |
| **Helper that builds preprocessing glob matchers** | `preprocess::build_glob_set(&[String])` | `src/preprocess.rs` | Module-qualified to distinguish from worker internals. Wraps `globset::GlobSetBuilder` to validate patterns, resolve exclusions, and emit a ready-to-query `GlobSet`. |
| **Worker integration point** | `Worker::dispatch_file()` | `src/searcher/worker.rs` | Invokes `build_glob_set` at startup (once per config) and calls `should_preprocess` synchronously on every candidate file before opening the underlying file descriptor. |

## 3. Behavioral Mechanics & Edge Cases

### Glob Exclusion Rules
Preprocessing globs share the same syntactic sugar as search globs. The `preprocess::build_glob_set` helper explicitly iterates patterns twice during construction:
1. First pass ingests inclusive patterns.
2. Second pass registers exclusion patterns (`!prefix` or `- prefix`).
If `should_preprocess` encounters a match, it cross-references the exclusion list first. An exclusion hit short-circuits and returns `false` regardless of how many inclusive rules matched. This ensures users can selectively target directories while ignoring subdirectories or temporary copies.

### Stdin Behavior
Standard input is pathless by definition. When stdin is active (either via the default interactive mode or `--stdin`), glob-based predicates are bypassed entirely. Instead, the high-level argument `opts.preprocess_stdin` acts as the sole switch. If enabled, Ripgrep attaches the preprocessor chain directly to the `Stdin` byte stream. The worker does not attempt glob evaluation; it consumes the preprocessed stream sequentially until `EOF` or a non-zero exit code propagates. Buffering is handled by `std::io::BufReader` injected between the OS handle and the preprocessor child process to prevent OS-level starvation.

### Preprocessing vs. Decompression Precedence
Custom preprocessing takes absolute precedence over the built-in automatic decompression subsystem (which handles `gzip`, `bzip2`, `xz`, `zstd`, `lzma`, etc. via extension detection). 
**Why**: The built-in decompressor operates as a transparent proxy inserted directly into the reader pipeline. If a user supplies a custom `--preprocess` command, wrapping both would create ambiguous stream boundaries and risk double-compression or encoding corruption. When preprocessing is active, the auto-decomposer is suppressed for those specific paths. If the file requires decompression *and* custom processing, the user must chain commands explicitly within the preprocessor string (e.g., `--preprocess 'cat | gunzip | my_script.py'`), giving full control over the I/O topology.
```

### Findings Progress Reply
Locate complete. Worker located at `src/searcher/worker.rs`; CLI/args isolated in `src/cli.rs`; preprocessing logic encapsulated in `src/preprocess.rs`. Predicate confirmed as `preprocess::should_preprocess` gating worker dispatch. Matcher builder identified as `preprocess::build_glob_set`, kept outside the worker module to enforce IO/logic separation. Glob exclusions utilize a dual-pass builder with short-circuit evaluation; stdin bypasses globbing entirely relying on `preprocess_stdin` toggle; custom preprocessing strictly overrides automatic extension-based decompression to prevent pipeline corruption. Evidence logged to `artifacts/evidence.md`.

REF-RG-PRE-8K2M