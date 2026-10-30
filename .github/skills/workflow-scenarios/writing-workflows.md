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
| `expect` | Substrings that must appear in the current assertion output |
| `expect-not` | Substrings that must NOT appear in the current assertion output |
| `expect-error` | Like expect, but command must exit nonzero |
| `expect-stderr` | Substrings that must appear in stderr |
| `expect-not-stderr` | Substrings that must NOT appear in stderr |
| `setup` | Commands to run before this specific scenario |
| `prompt` | Natural language request for eval systems |
| `query` | Shell pipeline to extract a value from stdout |
| `perf` | Latency (`max_ms`) and exit code constraints |

Before the first `query`, the current assertion output is the command output.
After a `query`, it is the derived query output. Put command-output negative
assertions before the first query.

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
Network traffic [
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

Include `prompt` blocks when the scenario maps to a natural language question an
agent might receive. The prompt describes the user's goal and available inputs,
not the solution's flags, selectors, or ordered commands.

```markdown
```prompt
What public types are in System.Text.Json?
`` `

`` `bash
dotnet-inspect type System.Text.Json -v:q
`` `
```

Not every scenario needs a prompt — only add them where there's a clear natural language question.

For an expanded end-to-end task, use one prompt for the whole investigation
rather than coaching the agent with each reference substep. Keep its input
project, snapshot, or other prepared assets separate from this workflow
document. Record required outcomes and interpretation boundaries as judge-side
criteria. Include a positive case and a meaningful completed-zero or other
negative control where the task needs to distinguish absence from failure.

Reference commands and exact predicates belong in conformance fences, not in
the agent input. A reference recipe is one known-good path, not an optimality
claim or a command-matching rubric. Follow the
[two-mode contract](../../../docs/workflows/README.md#two-evaluation-modes).

## Conventions

- One workflow file per logical area (don't mix package inspection with member lookup).
- Use bare `dotnet-inspect` for ordinary commands. A workflow may bind
  `$INSPECT` to `$DOTNET_INSPECT_WORKFLOW_BINARY` when repeated invocation or
  timing needs an exact path, or to an explicitly built alternate apphost when
  that runtime is the behavior under test. Verify the selected apphost's
  version and required runtime flavor.
- Use version pins (`@2.0.2`) for NuGet packages to ensure reproducible results.
- Freeze both endpoints and the observed date when comparing current
  dependencies with an upstream snapshot. Do not silently re-resolve `latest`
  during a repeat run or describe the frozen endpoint as today's latest.
- Make formatting intent explicit when asserting Markdown headers or tables;
  default section output may be a tree or another representation.
- Use `text` fences for placeholders and illustrative command shapes. Every
  `bash` fence must run against the workflow's actual prepared inputs.
- Keep expect blocks minimal — assert structure, not decoration.
- Use blockquote (`> Goal:`) descriptions on every H2 to explain why the goal matters.

## Reference

- [Full format specification](../../../docs/workflows/README.md) — complete
  format documentation with examples
