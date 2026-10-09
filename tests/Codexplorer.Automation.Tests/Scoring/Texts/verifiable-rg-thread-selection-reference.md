### Proposed `evidence.md` Content

```markdown
# Ripgrep Thread Configuration Trace

## Labeled Numeric Facts
automatic-thread-cap: 12
sorted-thread-count: 1

## Branch Analysis & Flow (`crates/core/flags/hiargs.rs`, lines 167-173, 847-849)
The search thread count is resolved in a strict priority hierarchy before being exposed via the `threads()` accessor.

1. **Sorted Search & Single-File Search** (`sorted-thread-count: 1`)
   - Conditions: `low.sort.is_some()` OR `paths.is_one_file`
   - Mechanism: Short-circuits to `1` immediately.
   - Rationale: Parallel directory walking violates lexical sort order guarantees, and searching a single file yields zero parallelization overhead. Both modes statically disable concurrency.

2. **Explicit User Override**
   - Condition: `if let Some(threads) = low.threads`
   - Mechanism: Direct assignment from CLI flag / env var.
   - Rationale: User intent supersedes heuristics. No clamping or adjustment is applied post-parse.

3. **Automatic Selection** (`automatic-thread-cap: 12`)
   - Condition: Fallback when neither sorting/single-file is active, and no explicit override exists.
   - Mechanism: `std::thread::available_parallelism().map_or(1, |n| n.get()).min(12)`
   - Rationale: Queries the operating system for logical CPU count, falls back to `1` if the API is unavailable, and enforces a hard ceiling of `12`. This prevents resource thrashing and excessive memory footprint on servers with high core counts during bulk traversal. The resolved integer is stored in `self.threads` and retrieved via `pub(crate) fn threads(&self) -> usize` for downstream injection into the parallel walk/search builder.
```

---

### Final Reference Answer

**Source Path:** `crates/core/flags/hiargs.rs` (thread resolution at lines 167–173; accessor at 847–849)  
**Maximum Automatic Thread Count:** `12`  
**Thread Count Forced by Sorted Search:** `1`  

**Labeled Numeric Facts:**  
`automatic-thread-cap: 12`  
`sorted-thread-count: 1`  

**Distinctions & Construction Trace:**
- **Automatic Selection:** Triggered only when `low.sort` is `None`, `paths.is_one_file` is `false`, and `low.threads` is `None`. Falls back to `std::thread::available_parallelism()`, applies a `min(12)` cap to bound memory/CPU contention during large-scale directory walks, and defaults to `1` if the OS API fails.
- **Explicit User Count:** Matches `if let Some(threads) = low.threads`. Takes absolute precedence over heuristics and mode restrictions once parsed, passing straight through to the internal `State` struct.
- **Single-File Search:** Detected via `paths.is_one_file`. Forces `1` thread instantly, as serializing a single file stream across workers introduces synchronization overhead with zero throughput gain.
- **Sorted Search:** Detected via `low.sort.is_some()`. Forces `1` thread because maintaining deterministic, lexicographically ordered output across parallelized `ignore` crate walkers is non-trivial and disabled by design. Parallel results would require a secondary merge step not implemented in this pipeline.
- **Parser-to-Walker Bridge:** The computed `threads` integer is stored in `State` and surfaced via `self.threads()`. Downstream consumers (the `grep_search` runner and `WalkParallel` configuration) read this accessor to spawn exactly that many worker handles, completing the flag → count → execution mapping without intermediate mutable state or lazy evaluation.