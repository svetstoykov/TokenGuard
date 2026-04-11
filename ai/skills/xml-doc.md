# XML Documentation

Write and review C# XML documentation comments for all public members. Use this guidance whenever the user asks to write, add, review, fix, or improve XML doc comments, or when generating code that requires documentation. Trigger on phrases like "write the docs", "add XML docs", "document this", "review the comments", "fix the summary", or any request to produce or critique `<summary>`, `<remarks>`, `<param>`, `<returns>`, or `<exception>` blocks. Also trigger when code is produced and documentation is part of the task.

The canonical detailed reference for this skill is `.kilo/skills/xml-doc/SKILL.md`. When working in agents that do not natively load Kilo skills, follow that file directly.

## Tag Taxonomy

| Tag | Purpose | Required on |
|---|---|---|
| `<summary>` | One-sentence "what" | Every public member |
| `<remarks>` | "Why" and "how"; architectural context, side effects, constraints | Classes, complex methods, non-obvious properties |
| `<param name="...">` | What the parameter represents and any constraints | Every public method/constructor param |
| `<returns>` | What the return value represents | Non-void public methods |
| `<exception cref="...">` | Thrown when what condition is met | Every explicitly thrown exception |
| `<see cref="...">` | Cross-reference to a type, member, or keyword | Inline wherever precision requires it |
| `<paramref name="...">` | Inline reference to a parameter by name | Inside `<param>`, `<remarks>`, or `<exception>` prose |
| `<para>` | Separates distinct ideas inside `<remarks>` | Whenever `<remarks>` contains more than one thought |

## Core Rules

**Summary** — answers "what does this do?" in one sentence.
- Verbs for methods/constructors: *Appends*, *Records*, *Builds*, *Returns*
- "Gets" or "Gets or sets" for properties
- "Represents" or "Defines" for types and records
- No "how", no repetition of member name, ends with a period

**Remarks** — answers "why does this exist and how does it behave?"
- Use `<para>` per distinct idea
- First para: role in the system or high-level behavioral contract
- Subsequent paras: side effects, ordering constraints, preservation invariants
- Reference related types with `<see cref="..."/>`, parameters with `<paramref name="..."/>`
- Omit on simple, self-evident members

**Params** — what the value *is* and any constraints (null rejection, optional behavior).

**Returns** — what the resolved value *represents*, not just its type.

**Exceptions** — one element per type, with the condition that triggers it.

**Cross-references** — `<see cref="..."/>` for types and members, `<see langword="null"/>` (not backticks) for language keywords.

## Reference

Full tag rules, examples, disposal contract guidance, and a checklist: `.kilo/skills/xml-doc/SKILL.md`
