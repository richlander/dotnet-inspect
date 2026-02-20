# Writing Workflows

> How to author good workflow scenario documents for this repo.

## Why workflows matter

Workflow docs fill the gap between unit tests and formal documentation. They are testable E2E scenario descriptions that catch regressions, validate expectations, and document how the tool should behave. Writing a workflow invariably finds bugs, unmet expectations, and missing features.

## File structure

Every workflow starts with YAML front matter:

```yaml
---
id: my-workflow
description: One-line summary of what this validates
commands: [type, member]
areas: [routing, resolution]
---
```

The `commands` and `areas` fields enable queries like "run all workflows that test the `member` command" or "run all `routing` workflows before shipping."

## Goal and variant pattern

Organize scenarios as numbered **goals** (H2) with optional **variants** (H3):

```markdown
## 1. Goal name

> Goal: Why this matters — what user need does this validate?

### 1a. First variant

### 1b. Second variant
```

- Goals describe a category of task (e.g., "Identify package source").
- Variants are different ways to achieve the same goal (e.g., platform library vs NuGet package).
- Number everything (1, 2, 3... and 1a, 1b, 2a...) so they're addressable in reports.

## Code fence types

| Fence | Purpose |
| --- | --- |
| `bash` | The exact command to run |
| `expect` | Substrings that must appear in stdout (all must match) |
| `expect-not` | Substrings that must NOT appear in stdout or stderr |
| `expect-error` | Like expect, but command must exit nonzero |
| `expect-stderr` | Substrings that must appear in stderr |
| `expect-not-stderr` | Substrings that must NOT appear in stderr |
| `setup` | Commands to run before this specific scenario |
| `prompt` | Natural language request for eval systems |
| `query` | Shell pipeline to extract a value from stdout |
| `perf` | Latency (`max_ms`) and exit code constraints |

## Writing good expect blocks

### Be specific enough to catch regressions

```markdown
```expect
Source: Platform
## Library Info
`` `
```

### Use expect-not to assert absence

```markdown
```expect-not
Source: NuGet
Network guard violation
`` `
```

### Don't over-specify

Assert the important structural elements — section headers, key fields, source type. Don't assert exact counts or formatting that changes frequently. If a count matters, use a `query` pipeline to extract it.

## Preconditions section

If the workflow needs setup (isolated session, cache priming, downloads), put it in a **Preconditions** H2 at the top, before any numbered goals:

```markdown
## Preconditions

`` `bash
export DOTNET_INSPECT_ISOLATED=my-workflow
`` `

`` `bash
dotnet-inspect cache clear
`` `
```

## Adding prompts for eval

Include `prompt` blocks when the scenario maps to a natural language question an agent might receive. This enables eval scoring:

```markdown
```prompt
What public types are in System.Text.Json?
`` `

`` `bash
dotnet-inspect type System.Text.Json -v:q
`` `
```

Not every scenario needs a prompt — only add them where there's a clear natural language question.

## Conventions

- One workflow file per logical area (don't mix package inspection with member lookup).
- Use `dotnet-inspect` (not `$INSPECT` or aliases) unless testing the DEBUG apphost specifically.
- Use version pins (`@2.0.2`) for NuGet packages to ensure reproducible results.
- Keep expect blocks minimal — assert structure, not decoration.
- Use blockquote (`> Goal:`) descriptions on every H2 to explain why the goal matters.

## Reference

- [Full format specification](../../docs/workflows/README.md) — complete format documentation with examples
