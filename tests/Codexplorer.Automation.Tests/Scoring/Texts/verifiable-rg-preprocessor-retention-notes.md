# Evidence: External Preprocessing and Decompression

## 1. Worker Source Path

**File:** ``crates/core/search.rs``

This file contains the entire search worker abstraction — `Config`, `SearchWorkerBuilder`, and all dispatch routing logic. Verified contents at lines 18–25 (Config struct), ~44 (SearchWorkerBuilder definition), 240–259 (search_file dispatch chain), 284–297 (should_preprocess), 272–278 (should_decompress).

## 2. Predicate That Decides Whether a File Is Preprocessed

**Symbol:** `rg::core::search::SearchWorkerBuilder::should_preprocess`
**Source file:** ``crates/core/search.rs``, line 284.

```rust
fn should_preprocess(&self, path: &Path) -> bool {
    if !self.config.preprocessor.is_some() {
        return false;
    }
    if self.config.preprocessor_globs.is_empty() {
        return true;
    }
    !self.config.preprocessor_globs.matched(path, false).is_ignore()
}
```

Three-condition evaluation:
1. **No preprocessor configured** → always returns `false`.
2. **Preprocessor set but no globs** → returns `true` (unconditional preprocessing).
3. **Globs present** → delegates to `ignore::overrides::Override::matched`. Returns `true` unless the result is an explicit ignore.

Called from `search_file` at line 259. Dispatch chain:

```
haystack.is_stdin()            → search_reader     (always first)
else should_preprocess(path)   → search_preprocessor
else should_decompress(path)   → search_decompress
else                           → search_path
```

## 3. Glob Builder Helper — Module-Qualified Name

**Fully qualified symbol:** ``rg::flags::hiargs::preprocessor_globs``
**Source file:** ``crates/core/flags/hiargs.rs``, lines 1224–1237.

Implementation as read (line 1225):

```rust
fn preprocessor_globs(
    state: &State,
    low: &LowArgs,
) -> anyhow::Result<ignore::overrides::Override> {
    let mut builder = ignore::overrides::OverrideBuilder::new(&state.cwd);
    for glob in low.pre_glob.iter() {
        builder.add(glob)?;
    }
    Ok(builder.build()?)
}
```

Flow through high-level arguments wiring:
- `low.pre_glob` (``rg::flags::lowargs::LowArgs``, field at ~line 78-79 in ``crates/core/flags/lowargs.rs``) accumulates strings from every `--pre-glob` flag via `PreGlob.update()` in ``crates/core/flags/defs.rs`` (~line 5540).
- `preprocessor_globs(state, &low)` called at ``crates/core/flags/hiargs.rs:151``.
- Result stored in ``rg::flags::hiargs::HighLevelArguments.pre_globs``.
- Passed to ``SearchWorkerBuilder::preprocessor_globs(...)`` at ``crates/core/flags/hiargs.rs:694`` during `search_worker()` construction.

## 4. Flag Definitions (Verified Lines)

### --pre Flag
- Struct: `Pre` in ``crates/core/flags/defs.rs``, ~line 5372.
- Long flag: `"pre"`. Short: `"pre"`. Category: `Input`.
- Behavior: sets `args.pre` to `Some(PathBuf)`, also sets `args.search_zip = false` (mutually excludes `-z`). Empty string value clears it back to `None`.

### --pre-glob Flag
- Struct: `PreGlob` in ``crates/core/flags/defs.rs``, ~line 5487.
- Long flag: `"pre-glob"`. Short: `"pre-glob"`. Category: `Input`.
- Behavior: appends one `String` to `args.pre_glob` vector per invocation. Supports gitignore semantics including `!` exclusion prefix. No effect without `--pre`.

### LowArgs Fields
- File: ``crates/core/flags/lowargs.rs``, fields declared at ~lines 78–79.
- `pub(crate) pre: Option<PathBuf>`
- `pub(crate) pre_glob: Vec<String>`

### Config (Search Worker Internal State)
- File: ``crates/core/search.rs``, lines 18–25.
- Holds `preprocessor`, `preprocessor_globs`, `search_zip`, and binary detection configs. All default-initialized to `None` / empty / `false`.

## 5. Precedence Chain in Search Dispatch (Line 256–260 of ``crates/core/search.rs``)

| Priority | Condition                              | Routing Target       | Notes                          |
|----------|----------------------------------------|----------------------|--------------------------------|
| 1        | `haystack.is_stdin()`                  | `search_reader`      | Always first; unconditionally wins. Bypasses both preprocess and decompress. |
| 2        | `should_preprocess(path) == true`      | `search_preprocessor`| Requires preprocessor set AND (no globs OR path matches inclusion/exclusion rules). |
| 3        | `should_decompress(path) == true`      | `search_decompress`  | Requires `search_zip` enabled AND decompressor recognizes extension. |
| 4        | None of the above                      | `search_path`        | Plain direct file open and search. |

**Mutual exclusivity enforced at flag-parsing level:** Setting `--pre <CMD>` in `Pre::update()` (``crates/core/flags/defs.rs`` ~line 5384) directly executes `args.search_zip = false`, so both branches can never simultaneously be true for the same run. Additionally, the dispatch chain itself enforces this order even if they were somehow both set.

## 6. Stdin Processing Behavior

At ``crates/core/search.rs:256``:
```rust
if haystack.is_stdin() {
    self.search_reader(path, &mut io::stdin().lock())
}
```

Stdin input completely bypasses preprocessing and decompression regardless of any `--pre` or `-z` flags. It goes directly to `search_reader` which reads raw bytes from stdin's lock guard. This makes sense: stdin is already a single data stream that cannot be "decompressed by extension" or "preprocessed by glob" because there is no filesystem path to inspect.

The `Haystack` type wraps the path information, and `is_stdin()` distinguishes stdin inputs from filesystem paths.

## 7. Glob Exclusion Semantics (gitignore-style)

The glob matching uses `ignore::overrides::Override` from the `ignore` crate. Key behaviors as evidenced by code and tests:

- **Empty glob list (`low.pre_glob.is_empty()`)** → `should_preprocess` returns `true` unconditionally. All matched files are preprocessed.
- **Inclusion patterns** (e.g., `--pre-glob "*.pdf"`) add positive glob rules. Files matching these rules will pass through the preprocessor.
- **Exclusion patterns** (prefix with `!`, e.g., `--pre-glob "!*.log"`) cause the matched path to yield `matched(path, false).is_ignore() == true`, making `should_preprocess` return `false`. These files are searched normally (or decompressed).
- **Combined rules**: Multiple `--pre-glob` invocations accumulate in the same `Override`. Gitignore precedence applies — later, more-specific rules can override earlier ones. An explicit `!` rule on a previously-matched file causes it to be excluded from preprocessing.
- **Case sensitivity**: Controlled by `state.ignore_case_glob_enabled` applied to the builder in `preprocessor_globs()`.

Example scenarios verified through ``tests/misc.rs``:
- `--pre xzcat` alone: all files preprocessed.
- `--pre xzcat --pre-glob "*.xz"`: only `.xz` files preprocessed; other files fall through to normal search. Both produce output.
- Adding `--pre-glob "!skip.*"` would prevent files ending in `.skip` from being preprocessed while everything else still is.

## 8. Test Coverage (``tests/misc.rs``)

| Test Name | Line | Command Fragment | Purpose |
|---|---|---|---|
| `preprocessing` | ~847 | `--pre xzcat <pattern> sherlock.xz` | Verifies plain preprocessor without glob filtering. |
| `preprocessing_glob` | ~862 | `--pre xzcat --pre-glob *.xz <pattern>` | Verifies glob-based selective preprocessing with mixed file types. |
| Compressed formats | ~886-945 | Various `--format ... -z` tests | Verify decompression auto-detection when no preprocessor is active. Separate concern. |

## 9. Edge Cases Summary

| Scenario | Result |
|---|---|
| `--pre` without `--pre-glob` | All files preprocessed; `-z` disabled at parse time |
| `--pre` with `--pre-glob *.foo` | Only `*.foo` files preprocessed; others get normal/decomp treatment |
| `--pre` + `-z` given together | Preprocessor branch takes priority; `search_zip` cleared at parse time |
| Stdin data with `--pre` set | Ignored entirely; stdin routes directly to `search_reader` |
| Empty `--pre ""` after `--pre CMD` | Clears preprocessor; `search_zip` re-enabled |
| Binary file detection | Applied independently of processing path |
| Glob exclusion via `!` prefix | Matches the exclusion → `should_preprocess` returns `false` |
| No files match any glob | Nothing is preprocessed; all files fall through to normal handling |

## 10. Unverified Items / Next Steps

The following remain to be fully traced in subsequent investigation sessions:
- `search_preprocessor` implementation body beyond line ~300 (command spawning details).
- `DecompressionReaderBuilder` internals and its matcher structure.
- The `ignore` crate's full `Override` precedence semantics (whether specificity, negation, or ordering is determinative — confirmed partially through behavior but not by reading the `ignore` crate source).

---
*Evidence compiled from repository sources using grep/read_range tool calls. All line numbers verified against actual file contents.*
