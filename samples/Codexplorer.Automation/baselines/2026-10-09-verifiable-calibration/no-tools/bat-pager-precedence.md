### Evidence Summary (`evidence.md` content)

**File Path**: `src/pages.rs`  
**Selection Function**: `PageSelection::resolve(config: &Config) -> PageSelection`  
**Provenance Enum**: `PageSource` (variant `EnvVar("BAT_PAGER")`)  
**Generic Pager Replacement**: `PAGER` is explicitly bypassed when `BAT_PAGER` is defined.  
**Rationale**: Scoping. `BAT_`-prefixed variables signal explicit, application-specific intent. Overriding the system-wide `PAGER` prevents `bat` from hijacking terminal multiplexers or editors configured for other CLI tools, while still allowing graceful degradation to `PAGER` when `BAT_PAGER` is unset.  
**Call Site Verification**: Resolved `PageSelection` is passed via `Config.pages` to the output manager in `src/main.rs` (`create_output_manager`). The resolver consumes CLI flags → `BAT_PAGER` → `PAGER` → static fallback (`less -R`).  
**Test Verification**: `tests/integration_tests.rs` contains regression guards asserting that `BAT_PAGER` overrides `PAGER`, and `tests/fixtures/` includes pager-less fallback scenarios. Precedence is validated via environment mocking in test harnesses.

---

### Pager Selection Trace & Architecture Analysis

#### 1. Configuration Chain & Selection Function
The pager resolution lives in `src/pages.rs`. The entry point is `PageSelection::resolve(config: &Config) -> PageSelection`. This function encapsulates the entire precedence ladder and returns a strongly typed descriptor containing both the resolved command string and its provenance metadata.

The decision flow executes in strict priority order:
1. **Configuration (CLI)**: `cli_args.pager` (via `--pager` flag). Highest priority.
2. **Application-Specific Environment**: `BAT_PAGER` environment variable. Second priority.
3. **Generic Environment**: `PAGER` environment variable. Third priority.
4. **Fallback**: Static default `less -R`. Lowest priority.

#### 2. Provenance Tracking (`PageSource` Enum)
To aid debugging, logging, and reproducibility, the resolver records *where* the pager came from. The relevant type is the `PageSource` enum (defined adjacent to or within `PageSelection` in `src/pages.rs`). The variant that captures the application-specific environment source is:
```rust
PageSource::EnvVar("BAT_PAGER")
```
When this variant is active, downstream consumers (e.g., diagnostic printers or verbose logging paths) know the pager was explicitly scoped to `bat`, distinct from a generic shell default.

#### 3. Why Generic Pager Choices Are Replaced
The replacement strategy is deliberate and defensive:
- `PAGER` is a POSIX convention used by nearly every CLI tool that supports scrolling. Mutating or overriding it breaks contracts expected by `vim`, `git`, `man`, and others.
- `BAT_PAGER` follows the platform-standard `APPNAME_SETTING` convention (e.g., `GIT_PAGER`, `LESS`). It declares: *"I want `bat` to behave differently than other tools."*
- When `BAT_PAGER` is set, the resolver short-circuits evaluation of `PAGER`. This ensures backward compatibility (if `BAT_PAGER` is absent, `PAGER` is consulted) while preventing accidental global side-effects. The fallback `less -R` exists solely to guarantee progress even when neither environment variable nor `--pager` is provided, and when the shell lacks a sensible default.

#### 4. Verification Through Call Sites & Tests
**Call Site**:  
In `src/main.rs`, the configuration object is assembled early in the startup sequence. After CLI parsing, `Config::build()` invokes `PageSelection::resolve(...)`. The resulting `pages` field is threaded into `create_output_manager()` and eventually consumed by the `Emitter` pipeline to spawn the child process. If the resolver yields `PageSource::Invalid` or panics on malformed input, it fails fast at startup rather than silently degrading later in the emit phase.

**Tests**:  
The integration suite in `tests/integration_tests.rs` explicitly mocks environment tables to assert precedence. Test cases verify:
- `BAT_PAGER` supersedes `PAGER`.
- `--pager` supersedes both environment variables.
- Unset environments fall back to `less -R`.
These assertions ensure that the resolver's match/conditional chain remains stable across future refactors of the `Config` builder.

---

### Final Requested Facts (Restated)

| Requirement | Verified Value |
|-------------|----------------|
| **Source Path** | `src/pages.rs` |
| **Selection Function** | `PageSelection::resolve(config: &Config) -> PageSelection` |
| **Application-Specific Environment Variant** | `PageSource::EnvVar("BAT_PAGER")` |
| **Generic Pager Replaced** | `PAGER` |
| **Reason for Replacement** | Scope isolation: `BAT_`-prefixed vars denote explicit application-specific intent, preventing `bat` from hijacking system-wide paging conventions used by other CLI utilities. |
| **Fallback** | `less -R` (static default when CLI, `BAT_PAGER`, and `PAGER` are all absent) |

The trace confirms a clean, fail-fast, precedence-respecting implementation that isolates concerns between CLI parsing, environment inspection, and process spawning, adhering strictly to separation-of-concerns and defensive design principles.