### Proposed `evidence.md` Content

```markdown
# Ripgrep Text-Encoding Label Parsing Evidence

## 1. Flag Definition & Parsing File
- **Path**: `crates/core/flags/defs.rs`
- **Role**: Defines the `Encoding` struct implementing the `Flag` trait. The `update` method parses the `--encoding`/`-E` argument values and maps the string literal `"none"` directly to the `EncodingMode::Disabled` variant.

## 2. Qualified Enum Variant Used to Disable Decoding
- **Path**: `crates/core/flags/lowargs.rs`
- **Symbol**: `EncodingMode::Disabled`
- **Definition**: Documented as the mode that uses "no explicit encoding and disable[s] all BOM sniffing. This will always result in searching the raw bytes, regardless of their true encoding."

## 3. Integration Test Name Verifying Ignored Encoding
- **Path**: `tests/feature.rs`
- **Name**: `f1207_ignore_encoding`
- **Verification**: Confirms that `--encoding none` forces raw-byte inspection. The test creates a file containing a UTF-16LE BOM followed by bytes, runs a search for a null byte (`\x00`), and asserts successful raw-matching output instead of decoder failures or skipped bytes.

## 4. Automatic Detection vs. Disabled Decoding
- **Automatic Detection (`EncodingMode::Auto`)**:
  - Default behavior. Restricts automatic discovery strictly to BOM sniffing for UTF-8 or UTF-16. Files without a BOM are searched using the searcher's default fallback without active transcode enforcement.
  - Implementation: `hiargs.rs` (line 729) leaves the searcher builder unmodified for `Auto`, preserving standard library defaults for BOM sniffing.
- **Disabled Decoding (`EncodingMode::Disabled`)**:
  - Explicitly overrides all textual assumptions. Not only does it refrain from forcing a specific encoding, it actively suppresses BOM sniffing via `builder.bom_sniffing(false)` (`hiargs.rs` lines 733–734).
  - Result: The search engine receives pure byte streams. No transcoding, no invalid-sequence replacement (``), and no BOM stripping occurs. This is required for binary/pattern-search scenarios where any character-level interpretation corrupts match semantics.
```

---

### Final Verified Answers

| Requested Element | Verified Value |
|-------------------|----------------|
| **File defining encoding flag parsing** | `crates/core/flags/defs.rs` |
| **Qualified enum variant for disabling decoding** | `EncodingMode::Disabled` |
| **Integration test name verifying ignored encoding** | `f1207_ignore_encoding` |
| **Difference: Auto vs Disabled** | `Auto` relies exclusively on BOM sniffing and leaves the searcher at its default configuration. `Disabled` actively calls `bom_sniffing(false)` on the searcher builder, completely bypassing transcoding and yielding raw bytes for the regex engine to process. |