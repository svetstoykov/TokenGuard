---
name: xml-doc
description: >
  Write and review C# XML documentation comments for public members.
  Use this skill whenever the user asks to write, add, review, fix, or improve
  XML doc comments, or when generating code that requires documentation.
  Trigger on phrases like "write the docs", "add XML docs", "document this",
  "review the comments", "fix the summary", or any request to produce or critique
  <summary>, <remarks>, <param>, <returns>, or <exception> blocks.
  Also trigger when code is produced and documentation is part of the task.
---

# XML Documentation

Canonical shared guidance also lives at `ai/skills/xml-doc.md`. Keep this Kilo skill aligned with that file so non-Kilo agents can follow the same behavior.

A decision-making and writing guide for C# XML documentation comments. All rules are
derived from the patterns established in the TokenGuard codebase — primarily
`ConversationContext`, `ContextMessage`, and the compaction model types.

---

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

---

## Summary Rules

The `<summary>` answers "what does this do?" in one sentence.

- Start with a verb for methods and constructors: *Appends*, *Records*, *Builds*, *Returns*
- Start with "Gets" or "Gets or sets" for properties
- Start with "Represents" or "Defines" for types and records
- Do not repeat the member name
- Do not explain why or how — that belongs in `<remarks>`
- End with a period
- No line breaks inside `<summary>`

```csharp
// Bad — describes how, not what; repeats the type name
/// <summary>
/// ConversationContext manages the token budget and calls the compaction strategy when needed.
/// </summary>

// Good — one sentence, "what", ends with period
/// <summary>
/// Represents the state of one LLM conversation.
/// </summary>
```

---

## Remarks Rules

The `<remarks>` answers "why does this exist and how does it behave?"

- Use `<para>` to separate distinct ideas. Each para covers one concern.
- First para: role in the system or high-level behavioral contract.
- Subsequent paras: side effects, ordering constraints, preservation invariants, cross-cutting concerns.
- Reference related types and members with `<see cref="..."/>`.
- Reference parameter names inline with `<paramref name="..."/>`.
- Do not re-state the summary — start from the summary as given and go deeper.
- Omit `<remarks>` from simple, self-evident members (e.g., a `Count` property on a list wrapper).

```csharp
// Pattern: first para sets context, later paras address specific behavioral contracts

/// <remarks>
/// <para>
/// A <see cref="ConversationContext"/> acts as the central state container for an agent loop or
/// chat session. Messages are added to the history as user input, model responses, and tool
/// results occur, and <see cref="PrepareAsync(CancellationToken)"/> returns the message list
/// that should be sent to the provider for the next request.
/// </para>
/// <para>
/// The context keeps the original recorded history intact. When the conversation is still within
/// budget, <see cref="PrepareAsync(CancellationToken)"/> returns that history directly. When the
/// configured compaction trigger is reached, the context delegates to the configured
/// <see cref="ICompactionStrategy"/> to produce a smaller request payload while preserving the
/// overall conversation flow.
/// </para>
/// </remarks>
```

---

## Param Rules

- Describe what the parameter *is* and any constraints on its value.
- When a null or whitespace input is rejected, say so: "Cannot be null or whitespace."
- When optional, state what happens when absent (e.g., "When <see langword="null"/>, no notifications are emitted.").
- Keep param descriptions to one or two sentences. Longer context belongs in `<remarks>`.

```csharp
// Bad — restates the type, adds nothing
/// <param name="counter">An ITokenCounter.</param>

// Good — states what it does and why it matters
/// <param name="counter">
/// Counts tokens for individual messages. This should match the target provider as closely
/// as possible so compaction decisions are based on realistic estimates.
/// </param>
```

---

## Returns Rules

- State what the value *represents*, not just its type.
- For tasks that resolve to a list or result object, describe what the resolved value contains.
- For booleans, describe what `true` and `false` mean in this context.

```csharp
// Bad
/// <returns>A task.</returns>

// Good
/// <returns>
/// A task that resolves to the full history when it fits within the configured budget, or to a
/// compacted message list when compaction is required.
/// </returns>
```

---

## Exception Rules

- Use `cref` to name the exception type exactly.
- Describe the condition that causes it, not just that it "may be thrown."
- One `<exception>` element per exception type.
- `ArgumentNullException` and `ArgumentException` have slightly different triggers — be precise.

```csharp
/// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or whitespace.</exception>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
/// <exception cref="ObjectDisposedException">Thrown when the context has been disposed.</exception>
```

---

## Cross-Reference Rules

Use `<see cref="..."/>` whenever precision matters more than prose flow.

- Reference types that the member depends on, delegates to, or affects.
- Reference the method or property the caller should call next ("await it immediately before every provider request").
- Use `<see langword="null"/>`, `<see langword="true"/>`, `<see langword="false"/>` instead of backticks for language keywords.
- Prefer `<see cref="Method(ParamType)"/>` with a disambiguating signature when the type has overloads.

```csharp
// Reference a method that the reader will likely call next
/// When the trigger is reached, the context delegates to the configured
/// <see cref="ICompactionStrategy"/> to produce a smaller request payload.

// Use langword for null, not backticks
/// When <see langword="null"/>, no compaction notifications are emitted.
```

---

## Disposal and Thread Safety

When a type implements `IDisposable`, document the disposal contract.

- State what is released on `Dispose`.
- State that all public members throw `ObjectDisposedException` after disposal.
- State the intended scope or lifetime (per-request, per-session, singleton warning).

```csharp
/// <remarks>
/// After disposal, all public members throw <see cref="ObjectDisposedException"/>. A
/// <see cref="ConversationContext"/> should be scoped to a single conversation and disposed
/// when that conversation ends. Registering it as a singleton will cause the history to grow
/// for the lifetime of the process and will not be released until the process exits.
/// </remarks>
```

---

## Common Smells and Fixes

| Smell | Fix |
|---|---|
| Summary repeats the member name verbatim | Rewrite to describe what it does, starting with a verb |
| Summary describes implementation detail ("calls the strategy and returns...") | Describe observable behavior, not internal steps |
| `<remarks>` is one long paragraph | Split on logical breaks using `<para>` |
| Parameter doc says "The value" or restates the type | Describe what the value *means* and any constraints |
| No `<exception>` for a method that explicitly throws | Add one per thrown type with a condition clause |
| `<see>` used for types outside this assembly with no context | Either omit or add a brief inline description of why the reader should care |
| Optional param documented without stating the null/absent behavior | Add "When `null`, ..." or "When omitted, ..." |
| `<remarks>` duplicates the `<summary>` | Remove the duplicate; `<remarks>` should deepen, not restate |
| Boolean return with no explanation of what true/false mean | Add a `<returns>` that names the true and false conditions |

---

## Checklist Before Submitting Documentation

- [ ] Every public member has a `<summary>`
- [ ] Summary starts with the right verb or noun pattern and ends with a period
- [ ] `<remarks>` present on any member where behavior, constraints, or context are non-obvious
- [ ] `<remarks>` uses `<para>` whenever more than one idea is present
- [ ] Every parameter has a `<param>` that describes meaning and constraints
- [ ] Every non-void return has `<returns>` that describes what the value represents
- [ ] Every explicitly thrown exception has `<exception>` with a condition clause
- [ ] Types and members referenced inline use `<see cref="..."/>` not backticks
- [ ] `null`, `true`, `false` use `<see langword="..."/>` not backticks
- [ ] Disposal contract documented on `IDisposable` types
- [ ] No documentation duplicates the summary content inside remarks
