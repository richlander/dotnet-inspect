# Workflow Scenario Format

> How to read and evaluate the scenario documents in this directory.

## Goal

This format is intended as a readable and testable documentation system that describes optimal workflows to accomplish important tasks (important enough to document). The format fits within a gap between unit tests and formal documentation. It's basically testable API-style documentation for a tool.

Workflow docs that follow this format are equally:

- Readable
- Testable
- Descriptions of E2E scenarios

> "Dear Agent, please validate that the latest commit conforms to my unstated E2E scenario expectations. Thanks! I'm about to ship to prod."

A major problem in the era of coding agents is that it is very easy to generate tools that expose a lot of useful functionality, but not to validate E2E expectations. It's possible to have 500 tests and still regress a scenario. Unit tests are not intended to cover an E2E or every expectation that the tool maintainer or its users have.

The act of writing these workflows invariably finds bugs, unmet expectations, and missing features. This process can be agent-assisted, but requires significant human interaction to drive workflow definition and set expectations. That said, it is somewhat surprising that agents can also find E2E bugs if given the task of workflow definition/documentation.

After the workflow docs are in place, an agent can use the workflows as a smoke test before shipping an update to prod. Depending on the size of the corpus, this will just take a few minutes. It's also easy to parallelize the task across sub-agents/teams/fleets if the workflows are described in multiple scenario-focussed files.

Some of these tools are intended to be used as part of an agent workflows, like an MCP or a tool that is referenced in a skill. Much of their purpose is making an agent more capable and/or efficient. It would be very useful to validate any hypothesis of agent improvement. The scenarios can be used by an eval/judge harness to validate efficacy.

## Front Matter

Each workflow document begins with YAML front matter for discoverability and agent queries.

```yaml
---
id: bare-name-routing
description: How bare names route to platform vs NuGet
commands: [type, package]
areas: [routing, resolution]
---
```

| Field | Purpose |
| ----- | ------- |
| `id` | Unique identifier for the workflow |
| `description` | Brief summary (one line) |
| `commands` | CLI commands exercised — agents can query "which workflows test the `type` command?" |
| `areas` | Code areas or components — agents can query "which workflows apply to my routing change?" |

This enables queries like: *"Before shipping, run all workflows where `areas` includes `routing`."*

## Structure

The format uses plain Markdown with semantic code fences. Each document describes **user goals** (H2 headings) that someone — human or agent — would accomplish with the tool. When there are multiple ways to achieve the same goal, each **variant** is an H3 under the goal.

```text
## Preconditions                      ← H2: optional setup section (must be first)
<bash blocks to establish state>

## 1. Goal name                       ← H2: category of task (numbered)
> Goal: description                   ← blockquote: why this matters

### 1a. Variant name                  ← H3: one way to do it (numbered)
```

The optional **Preconditions** section runs setup commands before any scenarios — clearing caches, downloading packages, or establishing known state. It must appear before numbered goals.

Goals and variants are numbered (1, 2, 3... and 1a, 1b, 2a...) to make them addressable in reports and discussions.

Within each goal or variant, code fences define the executable scenario:

- **`prompt`** — The natural language request an agent or user would make. Essential for eval systems; the H2/H3 headings are categories, not prompts.
- **`setup`** — Commands to run before this specific scenario (scenario-level, unlike file-level Preconditions).
- **`bash`** — The exact command to run.
- **`expect`** — Substrings that must appear in stdout.
- **`expect-not`** — Substrings that must NOT appear (stdout or stderr).
- **`expect-error`** — Like `expect`, but command must exit nonzero.
- **`expect-stderr`** — Substrings that must appear in stderr.
- **`expect-not-stderr`** — Substrings that must NOT appear in stderr.
- **`query`** — Shell pipeline to extract a specific value from stdout.
- **`perf`** — Latency and exit code constraints.

This structure makes scenarios simultaneously:

1. **Readable** — Markdown renders nicely in any viewer; goals and variants are scannable.
2. **Executable** — Code fences are unambiguous; automation can parse and run them.
3. **Evaluable** — The `prompt` + `expect` pattern maps directly to agent evals: give the prompt, check the output.

## Example

A complete scenario showing all the pieces together:

````markdown
## Identify package source

> Goal: Determine whether a library is resolved from the platform or NuGet.

### Platform library

```prompt
Where does System.Text.Json come from — the platform or NuGet?
```

```usage
$ dotnet-inspect System.Text.Json -v:q
# System.Text.Json.dll

Name: System.Text.Json | Version: 9.0.0 | TFM: .NETCoreApp,Version=v9.0 | Size: 2.3 MB | Source: Platform | Modified: 2025-01-15
```

```bash
dotnet-inspect System.Text.Json -v:q
```

```expect
Source: Platform
```

```expect-not
Source: NuGet
```

```query
grep -o 'Source: [A-Za-z]*'
```

```pipeline
$ dotnet-inspect System.Text.Json -v:q | grep -o 'Source: [A-Za-z]*'
Source: Platform
```

Note: This pipeline produces a text result that can be evaluated.

```query
grep -q 'Source: Platform' && echo true || echo false
```

```pipeline
$ dotnet-inspect System.Text.Json -v:q | grep -q 'Source: Platform' && echo true || echo false
true
```

Note: This pipeline produces a boolean result. Only one of the pipelines need to be adopted.

### NuGet-only package

```prompt
Where does System.CommandLine come from?
```

```bash
dotnet-inspect System.CommandLine -v:q
```

```expect
Source: NuGet
```
````

## Code fence types

### `prompt` — the user/agent request

The natural language request that motivates this scenario. This is what a user would type or an agent would receive. Critical for eval systems — it's the input side of the input→output test.

````markdown
```prompt
What public types are in System.Text.Json?
```
````

### `setup` — scenario-level setup

Commands to run before this specific scenario. Unlike file-level Preconditions (which run once), `setup` runs immediately before the `bash` command in its scenario.

````markdown
```setup
dotnet-inspect cache clear
```
````

### `bash` — the command to run

The exact `dotnet-inspect` invocation. Run it as-is.

````markdown
```bash
dotnet-inspect System.Text.Json -v:q
```
````

### `expect` — content that must appear in stdout

Each line is a substring that must appear somewhere in the command's stdout. All lines must match.

````markdown
```expect
Source: Platform
## Library Info
```
````

### `expect-not` — content that must NOT appear

Each line is a substring that must **not** appear anywhere in stdout or stderr. If any line matches, the scenario fails.

````markdown
```expect-not
##
Tips:
```
````

### `expect-error` — content that must appear, with nonzero exit code

Like `expect`, but the command is expected to fail (exit code ≠ 0). Each line must appear in the combined stdout+stderr output.

````markdown
```expect-error
Version '99.99.99' of package 'System.CommandLine' not found.
```
````

### `expect-stderr` — content that must appear on stderr specifically

Each line is a substring that must appear in stderr. Used for tips and diagnostics that are written to stderr, not stdout.

````markdown
```expect-stderr
Tips:
```
````

### `expect-not-stderr` — content that must NOT appear on stderr

Each line is a substring that must **not** appear in stderr. Use when you need to assert absence of warnings or diagnostics without affecting stdout assertions.

````markdown
```expect-not-stderr
Warning:
Deprecated:
```
````

### `query` — extraction pipeline

A shell pipeline applied to stdout. Used to isolate a specific value for comparison. Useful for building dashboards or feeding results into other tools.

````markdown
```query
grep -o 'Source: [A-Za-z]*'
```
````

### `perf` — performance constraints

Key-value pairs defining latency and exit code targets. Used in performance-focused documents.

````markdown
```perf
max_ms: 25
exit_code: 1
```
````

| Key | Meaning |
| --- | ------- |
| `max_ms` | Maximum wall-clock time in milliseconds (warm, steady-state) |
| `exit_code` | Expected exit code (default: 0 if omitted) |

## Running and evaluating workflows

See the [validating workflows skill](../../skills/workflow-scenarios/validating-workflows.md) for evaluation rules, preconditions, parallelization strategies, and the eval pattern.

## Related skills

| Skill | What it covers |
| --- | --- |
| [Validating workflows](../../skills/workflow-scenarios/validating-workflows.md) | How to run workflows solo or with agent teams, evaluation rules, the eval pattern |
| [Writing workflows](../../skills/workflow-scenarios/writing-workflows.md) | How to author good workflow documents — conventions and best practices |
| [Performance testing](../../skills/workflow-scenarios/performance-testing.md) | Using perf workflows as a pre-ship gate, profiling regressions |
| [Network guard](../../skills/workflow-scenarios/network-guard.md) | DEBUG-only network guard, implementation details, offline mode |
