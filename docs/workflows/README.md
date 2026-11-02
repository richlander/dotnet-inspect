# Workflow Scenario Format

> How to read and evaluate the scenario documents in this directory.

## Goal

This format is a readable and testable documentation system for useful,
evidence-backed workflows that accomplish important tasks. It fits within the
gap between unit tests and formal documentation: testable API-style
documentation for a tool, not proof that one command sequence is optimal.

Workflow docs that follow this format are equally:

- Readable
- Testable
- Descriptions of E2E scenarios

> "Dear Agent, please validate that the latest commit conforms to my unstated E2E scenario expectations. Thanks! I'm about to ship to prod."

A major problem in the era of coding agents is that it is very easy to generate tools that expose a lot of useful functionality, but not to validate E2E expectations. It's possible to have 500 tests and still regress a scenario. Unit tests are not intended to cover an E2E or every expectation that the tool maintainer or its users have.

The act of writing these workflows invariably finds bugs, unmet expectations, and missing features. This process can be agent-assisted, but requires significant human interaction to drive workflow definition and set expectations. That said, it is somewhat surprising that agents can also find E2E bugs if given the task of workflow definition/documentation.

After the workflow docs are in place, an agent can use the workflows as a smoke
test before shipping an update to prod. Record the selected files and scenarios;
package acquisition, source verification, and performance scenarios have
different prerequisites and costs.

These documents also provide tasks and judging criteria for testing how agents
use the tool. Keep that evaluation separate from executing a reference recipe.

## Two evaluation modes

| Mode | Input | What a pass establishes |
| --- | --- | --- |
| Scripted conformance | Preconditions, reference commands, queries, and assertions | The exact tool build completes the documented workflow and satisfies its assertions. |
| Agent task completion | Natural-language prompt, prepared user inputs, and access to the exact tool's shipped guidance and evidence | An agent can obtain and correctly interpret the required evidence without being given the reference recipe. |

Reference commands and assertions are judge-side material in agent mode. Do not
give the agent this document, expected findings, command captures, or an ordered
solution. Extract the prompt and identify its prepared inputs instead. The agent
may ask the installed tool for current skills, discovery, and explanation.
Accept equivalent successful approaches; matching a reference command sequence
is neither necessary nor sufficient.

Each expanded task must state the required outcomes and interpretation
boundaries alongside its reference checks. Grade correctness, completion,
material findings, and scope qualifications first. Report unavailable, failed,
partial, and complete-zero results separately. An unexecuted assertion or an
unsupported operation is not a negative finding.

Record the tool version, task/input identities, evaluated guidance, agent model,
any predeclared budget, actual calls, failures, recovery, and discovery path.
Keep conformance and agent results separate: a passing reference recipe does
not prove agent discoverability, and an agent's success does not validate
unexecuted reference variants.

[Skill guidance](../../taste/skill-guidance.md#evaluate-the-discovery-premium)
owns the discovery-premium policy. These workflows supply representative tasks
and repeatable checks, not a requirement to run a new agent pilot for every
capability or documentation change. New tasks can expand capability coverage
without making an agent-efficiency claim.

The [secure supply-chain review](discovery/secure-supply-chain.md) is an expanded
example: one whole-task prompt, prepared project and snapshot inputs, six
judge-side outcomes, and executable reference checks with a completed-zero
control.

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
- **`expect`** — Substrings that must appear in the current assertion output.
- **`expect-not`** — Substrings that must NOT appear in the current assertion output.
- **`expect-error`** — Like `expect`, but command must exit nonzero.
- **`expect-stderr`** — Substrings that must appear in stderr.
- **`expect-not-stderr`** — Substrings that must NOT appear in stderr.
- **`query`** — Shell pipelines that independently extract values from stdout.
- **`perf`** — Latency and exit code constraints.

This structure makes scenarios simultaneously:

1. **Readable** — Markdown renders nicely in any viewer; goals and variants are scannable.
2. **Executable** — Code fences are unambiguous; automation can parse and run them.
3. **Evaluable** — The prompt and outcome criteria define the agent task; the
   reference commands and assertions remain with the judge.

Before evaluation, publish the exact revision under test, set
`DOTNET_INSPECT_WORKFLOW_BINARY` to that apphost and
`DOTNET_INSPECT_WORKFLOW_VERSION` to its `--version` output, and prepend the
apphost directory to `PATH`. Bare `dotnet-inspect` commands and workflows that
use `$INSPECT` must therefore resolve to the same executable unless a workflow
explicitly builds an alternate apphost required by its goal. Such an exception
must verify the same version and its required runtime flavor.

## Example

A complete scenario showing all the pieces together:

````markdown
## 1. Identify package source

> Goal: Determine whether a library is resolved from the platform or NuGet.

### 1a. Platform library

```prompt
Where does System.Text.Json come from — the platform or NuGet?
```

```usage
$ dotnet-inspect library System.Text.Json -v:q
# System.Text.Json.dll

Name: System.Text.Json | Version: 11.0.0-rc.1.26425.128 | TFM: .NETCoreApp,Version=v11.0 | Size: 2.7 MB | Source: Platform
```

```bash
dotnet-inspect library System.Text.Json -v:q
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
$ dotnet-inspect library System.Text.Json -v:q | grep -o 'Source: [A-Za-z]*'
Source: Platform
```

Note: This pipeline produces a text result that can be evaluated.

```query
grep -q 'Source: Platform' && echo true || echo false
```

```pipeline
$ dotnet-inspect library System.Text.Json -v:q | grep -q 'Source: Platform' && echo true || echo false
true
```

Note: This pipeline produces a boolean result. Only one of the pipelines need to be adopted.

### 1b. NuGet-only package

```prompt
Where does System.CommandLine come from?
```

```bash
dotnet-inspect package System.CommandLine@2.0.3 -v:q
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

A workflow starts at the repository root. Each `setup` or `bash` fence is one
shell program initialized with the working directory and exported environment
left by the preceding executable fence in that workflow. Changes made by
commands such as `cd` and `export` therefore persist to later fences, but shell
locals and functions do not. Commands may span physical lines with the usual
trailing `\` line continuation.

````markdown
```bash
dotnet-inspect System.Text.Json -v:q
```
````

### `expect` — content that must appear in the current assertion output

Each line is a substring that must appear in the current assertion output.
Before a `query`, that is the command's stdout. After a `query`, it is only the
derived query output. All lines must match.

````markdown
```expect
Source: Platform
## Library Info
```
````

### `expect-not` — content that must NOT appear

Each line is a substring that must **not** appear in the current assertion
output. Before a `query`, that is the command's combined stdout and stderr.
After a `query`, it is only the derived query output. Use
`expect-not-stderr` when stderr must remain absent regardless of query position.
If any line matches, the scenario fails.

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
package <package>
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

A shell pipeline applied to stdout. Used to isolate a specific value for
comparison. Useful for building dashboards or feeding results into other tools.

````markdown
```query
grep -o 'Source: [A-Za-z]*'
```
````

Query evaluation is sequential and position-sensitive:

1. Fold physical lines joined by a trailing `\` into one logical line.
2. Apply each nonempty logical line in the `query` fence independently to the
   original command stdout. A later query line does not consume an earlier
   query line's output.
3. Concatenate each query line's stdout, in source order, to form the derived
   query output.
4. `expect` and `expect-not` blocks before a `query` validate the original
   command output. Those after a `query` validate only the derived query output.
   Place original-output negative assertions before the first query.

This keeps multiline shell commands possible while making a multi-line query
an explicit list of independent projections rather than an accidental shell
script.

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

See the
[validating workflows reference](../../.github/skills/workflow-scenarios/validating-workflows.md)
for evaluation rules, preconditions, parallelization strategies, and the eval
pattern.

## Related skills

| Skill | What it covers |
| --- | --- |
| [Validating workflows](../../.github/skills/workflow-scenarios/validating-workflows.md) | How to run workflows solo or with agent teams, evaluation rules, the eval pattern |
| [Writing workflows](../../.github/skills/workflow-scenarios/writing-workflows.md) | How to author good workflow documents — conventions and best practices |
| [Performance testing](../../.github/skills/workflow-scenarios/performance-testing.md) | Using perf workflows as a pre-ship gate, profiling regressions |
| [Network observation](../../.github/skills/workflow-scenarios/network-guard.md) | Debug request-start logging and offline enforcement |
