## Final Findings Summary

### Verified Deliverables

**1. Worker Source Path (dispatch logic)**
- File: ``crates/core/search.rs``
- Method: `SearchWorkerBuilder::search_file` at lines 246–260 performs the dispatch chain.
- The method evaluates four branches in strict priority order via if-else, routing each haystack to one of `search_reader`, `search_preprocessor`, `search_decompress`, or `search_path`.

**2. Preprocessing Predicate — Symbol and Line**
- **Fully qualified symbol:** ``rg::core::search::SearchWorkerBuilder::should_preprocess``
- **File:** ``crates/core/search.rs``, line 284.
- **Logic:** Returns `false` if no preprocessor configured; returns `true` if globs list is empty (unconditional); otherwise evaluates `self.config.preprocessor_globs.matched(path, false).is_ignore()` — returns `true` unless the path explicitly matches an exclusion rule. Called from line 259 inside `search_file`.

**3. Glob Builder Helper — Module-Qualified Name**
- **Fully qualified symbol:** ``rg::flags::hiargs::preprocessor_globs``
- **File:** ``crates/core/flags/hiargs.rs``, line 1225.
- Signature: `fn preprocessor_globs(state: &State, low: &LowArgs) -> anyhow::Result<ignore::overrides::Override>`
- Accumulates strings from `low.pre_glob` into an `ignore::overrides::OverrideBuilder`, applies case-sensitivity from `state.ignore_case_glob_enabled`, and builds. Wiring: result stored in ``HighLevelArguments.pre_globs`` (set at line 151), passed to `SearchWorkerBuilder` at line 694.

**4. Glob Exclusion Semantics**
- Uses `ignore::overrides::Override` from the `ignore` crate with gitignore-style rules. Inclusion patterns (e.g., `*.pdf`) cause `matched(...).is_ignore() == false`, allowing preprocessing. Exclusion patterns prefixed with `!` cause `matched(...).is_ignore() == true`, skipping preprocessing. An empty glob list (`pre_glob.is_empty()`) triggers unconditional preprocessing. Multiple globs accumulate in the same Override with gitignore precedence semantics.

**5. Stdin Processing Behavior**
- Stdin input routes unconditionally to `search_reader` at ``crates/core/search.rs:256``. It bypasses both preprocessing and decompression regardless of any `--pre` or `-z` flags set. This occurs because stdin has no filesystem path to inspect for extension-based decompression or glob matching.

**6. Precedence Order**
- Priority 1 → `haystack.is_stdin()` → `search_reader` (always first)
- Priority 2 → `should_preprocess(path)` → `search_preprocessor`
- Priority 3 → `should_decompress(path)` → `search_decompress`
- Priority 4 → Fallback → `search_path`
- Mutual exclusivity enforced at parse time: `--pre <CMD>` sets `args.search_zip = false` in ``crates/core/flags/defs.rs:Pre::update`` (~line 5384). Even without this, the dispatch chain enforces preprocess-first ordering.

---

### Unfinished Work

- Reading the full body of `search_preprocessor` beyond the initial command-spawning block (lines ~300+) to fully document how stdin/files are wired to the preprocessor process.
- Examining the `DecompressionReaderBuilder` implementation and its matcher structure in detail.
- Tracing the `ignore` crate's `Override` source to document exact precedence rules for conflicting inclusion/exclusion patterns (gitignore-specific behavior not visible from ripgrep code alone).

### Blockers

None identified. All critical paths are accessible within the repository.

### Next Recommended Steps

1. Follow up on the three items listed under "Unfinished Work" if deeper documentation is needed.
2. Verify edge cases around `--pre-glob !` negation interactions with multiple accumulated globs by reviewing more test scenarios.
3. Confirm that `search_zip` re-enabling after clearing `--pre ""` works correctly by reading the `Pre::update` implementation fully.

REF-RG-PRE-8K2M