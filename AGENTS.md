# TokenGuard Repository Instructions

This file is an index. The rules live in `docs/ai-rules/` as bounded context files so that only the relevant ones are loaded for a given task.

`docs/ai-rules/` and `docs/superpowers/` are local and gitignored. Never add, stage, or commit anything under them. Read the rule files from disk when they are present; a published clone does not contain them. The rest of `docs/` is public library documentation and is versioned normally.

## Always read

Read these two files before doing anything else in a session. They are short and always apply.

| File | Contents |
| --- | --- |
| [`docs/ai-rules/core-principles.md`](docs/ai-rules/core-principles.md) | Role, product and technology stack, repository structure, design priority order, code organization rules. |
| [`docs/ai-rules/workflow-and-boundaries.md`](docs/ai-rules/workflow-and-boundaries.md) | Consult-first collaboration, scope boundaries, reply and reference style, Superpowers skill approval, trust boundaries, Git workflow and commit format. |

## Read when the trigger applies

Read a file before starting work that matches its trigger. When several triggers apply, read all of them.

| Trigger | File |
| --- | --- |
| Writing or changing C# under `src`, `tests`, or `samples` | [`docs/ai-rules/csharp-guidelines.md`](docs/ai-rules/csharp-guidelines.md) |
| Writing or reviewing C# XML documentation comments | [`docs/ai-rules/csharp-xml-documentation.md`](docs/ai-rules/csharp-xml-documentation.md) |
| Adding or changing tests, or claiming a change is complete | [`docs/ai-rules/testing.md`](docs/ai-rules/testing.md) |

`samples/Codexplorer` carries its own `AGENTS.md`; it applies to work inside that project.

## On-demand commands

These are not rules. Run one only when the user asks for it or invokes it by name. Each lives as a skill in `.claude/skills/`, mirrored unchanged in `.agents/skills/`; both copies are versioned and must stay identical.

| Command | Use for | File |
| --- | --- | --- |
| `audit-token-economy` | Auditing a Codexplorer session transcript for token economy and compaction invariants, and tracing violations to source | [`.claude/skills/audit-token-economy/SKILL.md`](.claude/skills/audit-token-economy/SKILL.md) |

## Maintaining these rules

- Add a durable engineering rule to the file whose trigger already covers it. Create a new bounded file only when a genuinely new area appears, and add its trigger row here in the same change.
- Put a procedure that runs only on request in a skill under `.claude/skills/` with its `.agents/skills/` mirror, and list it under on-demand commands, not in `docs/ai-rules/`.
- Keep detailed and frequently changing requirements in `.specs/token-guard-spec.md` or task files, not in `docs/ai-rules/`.
- Edits under `docs/ai-rules/` stay on this machine. They are never part of a commit.
