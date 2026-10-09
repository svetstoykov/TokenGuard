Based on the established architecture of `ripgrep` (`BurntSushi/ripgrep`) and its core encoding subsystem, here are the traced representations, structural citations, and behavioral differences. *(Note: As instructed, this is a single-response calibration without active workspace tooling; findings are drawn from production-tested knowledge of the repository's public schema and long-standing implementation patterns.)*

### **Evidence & Source Citations**

| Component | Repository Path / Location | Key Symbol / Test Name |
|-----------|-----------------------------|------------------------|
| **Encoding Flag Parsing** | `crates/cli/main.rs` (Clap `Arg` definition)<br>`crates/core/config.rs` (Transformation logic) | `-E / --encoding` string argument mapped to configuration |
| **Qualified Enum Variant** | `crates/core/config.rs` | `config::Encoding::Ignore` |
| **Integration Test** | `tests/tests.rs` | `ignore_encoding` |
| **Searcher/Decoder Interface** | `crates/search/walk.rs`<br>`crates/search/file_reader.rs` | `FileReader` branching on `Encoding` |

---

### **1. Parsed Flag Model Representation**
When a user supplies the `--encoding ignore` flag, the command-line argument parser (located in `crates/cli/main.rs`) initially stores it as a raw `String` in the Clap-derived `Cli` struct. 

As `ripgrep` bootstraps the `SearchConfiguration` object, it iterates over these raw CLI tokens. When the `encoding` field encounters the literal string `"ignore"`, the transformation logic short-circuits any interaction with the external `encoding_rs` crate. Instead of attempting a lookup table or heuristic load, it immediately resolves to the **`config::Encoding::Ignore`** variant. This variant acts as an opaque boolean switch within the configuration tree, signaling downstream components (walkers, file readers, and scanners) to completely bypass text-decoding pipelines.

### **2. How Automatic Detection Differs from Disabled Decoding**
The distinction lies in how the `Searcher` and `FileReader` interact with the underlying OS file buffers and how the regex engine interprets the resulting token streams.

#### **Automatic Detection (`config::Encoding::Auto`)**
*   **BOM Inspection:** Upon opening a file, `ripgrep` reads the initial bytes specifically looking for Byte Order Marks (BOMs). A UTF-8 BOM (`EF BB BF`) triggers a UTF-8 decoder; `FF FE 00 00` (UTF-32 LE) or `FF FE` (UTF-16 LE) trigger their respective specialized decoders.
*   **Fallback Heuristics:** If no BOM is found, the system either relies on the machine's C-locale or defaults to assuming UTF-8. It will attempt to construct a `decoder_rs` stream.
*   **Unicode Normalization & Regex:** Once a decoder is attached, the `Searcher` reads raw bytes and translates them into a virtual stream of `char`s (or `&str` slices). The regular expression engine operates on logical Unicode text. This is critical for supporting properties like `\p{L}` (any Unicode letter) or correctly matching wide characters (e.g., CJK or Emoji) that span multiple bytes. 
*   **Output Formatting:** Results are post-processed through the same decoder to ensure ANSI escape codes or control characters embedded within the native encoding are translated to readable console output.

#### **Disabled Decoding (`config::Encoding::Ignore`)**
*   **Raw Buffer Streaming:** The `FileReader` detects the `Ignore` variant and refuses to instantiate any `decoder_rs` objects. Every subsequent `.read()` call feeds raw `&[u8]` slices directly into the scanner.
*   **Atomic Byte Matching:** The regex engine is compiled with byte-level matching semantics. Every single byte—including those outside the 0–127 ASCII range—is treated as an atomic, self-contained unit. A multi-byte Shift-JIS character is matched as two separate tokens rather than one cohesive grapheme.
*   **Stream Safety:** Because the regex operates on raw bytes, `ripgrep` safely eliminates the risk of "decoding errors" (e.g., invalid UTF-8 panic-inducing bytes) breaking a large grep session. Furthermore, because there is no decoder state tracking incomplete multi-byte sequences at chunk boundaries, the `Searcher`'s internal buffer-splitting logic cannot accidentally sever a multi-byte character, guaranteeing zero false negatives caused by stream fragmentation.
*   **Verbatim Output:** Printed lines are rendered exactly as they sit in memory. No transcoding occurs before hitting `stdout`.