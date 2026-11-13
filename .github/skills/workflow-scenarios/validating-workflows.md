# Validating Workflows

> How to run workflow scenario documents as tests — solo or with agent teams.

## What workflow docs are

Each `.md` file under `docs/workflows/` is a testable scenario document. It uses semantic code fences (`bash`, `expect`, `expect-not`, `expect-error`, `perf`) to define assertions against dotnet-inspect output. They serve as E2E smoke tests that catch regressions unit tests miss.

The [format spec](../../../docs/workflows/README.md) defines the code fence
types and document structure.

## Evaluation rules

1. **Report tool version first**: Run `dotnet-inspect --version` and include the output in your report. This establishes which build was tested.
2. Parse the `prompt` block as the input request (for eval systems, this is what the agent receives).
3. If a `setup` block exists, run it first to establish scenario state.
4. Run the `bash` block as one shell program and capture stdout, stderr, and exit
   code. Start each workflow at the repository root. Initialize each executable
   fence with the working directory and exported environment left by the
   preceding fence; shell locals and functions do not persist. A trailing `\`
   keeps multiline commands possible.
5. Process assertion and query fences in document order. Before a `query`,
   `expect` and `expect-not` validate the original command output.
6. For a `query`, first fold trailing-`\` line continuations. Apply each
   remaining nonempty logical line independently to the original command
   stdout, then concatenate the results in source order. Query lines do not
   pipe into one another.
7. After a `query`, `expect` and `expect-not` validate that derived query
   output only. Place original stdout/stderr negative assertions before the
   first query. A later query replaces the derived output used by following
   assertions.
8. For each `expect-error` line, check that exit code ≠ 0 and the line appears
   in stdout+stderr.
9. For each `expect-stderr` line, check that it appears as a substring in
   stderr.
10. For each `expect-not-stderr` line, check that it does **not** appear in
    stderr.
11. If a `perf` block exists, compare wall-clock time against `max_ms` and exit
    code against `exit_code`.

Commands are expected to exit 0 unless `expect-error` is used.

A scenario **passes** when its command and all assertions hold. Report each
variant independently; a passing variant does not erase another variant's
failure. Stop dependent scenarios when preparation fails and report them as
blocked, not passed or complete-zero.

## Preconditions

Many workflows have a **Preconditions** section at the top (isolated sessions, cache priming, etc.). Always run these first.

General preconditions for all workflows:

- **Exact NativeAOT build**: Set `DOTNET_INSPECT_WORKFLOW_BINARY` to the
  published apphost for the exact revision under test,
  `DOTNET_INSPECT_WORKFLOW_VERSION` to its `--version` output, and prepend the
  apphost directory to `PATH`. Verify that bare `dotnet-inspect`, `$INSPECT`
  workflows, and the recorded version all identify that same NativeAOT apphost:

  ```bash
  set -e -o pipefail
  : "${DOTNET_INSPECT_WORKFLOW_BINARY:?set the exact published apphost path}"
  : "${DOTNET_INSPECT_WORKFLOW_VERSION:?set the expected --version output}"
  test -x "$DOTNET_INSPECT_WORKFLOW_BINARY"
  export PATH="$(dirname "$DOTNET_INSPECT_WORKFLOW_BINARY"):$PATH"
  test "$(command -v dotnet-inspect)" = "$DOTNET_INSPECT_WORKFLOW_BINARY"
  test "$(dotnet-inspect --version)" = "$DOTNET_INSPECT_WORKFLOW_VERSION"
  dotnet-inspect --flavor | grep -q '^NativeAOT;'
  ```

  A workflow may explicitly build an alternate apphost when that runtime or
  configuration is the behavior under test. It must verify the same
  `DOTNET_INSPECT_WORKFLOW_VERSION` and its required flavor. The Debug-only
  network-observation workflow is the current exception.

- **Warm cache**: Timing targets assume second+ invocation (OS and app caches warm).
- **Network**: Some commands require network access (for example,
  `package Package --versions -n 1`). Others are fully offline (for example,
  exact Package inspection from a cached payload).

## Running a single workflow

Parse the code fences and execute them in order:

1. Run the Preconditions section if present.
2. For each numbered goal/variant, run `setup` blocks, then `bash` blocks.
3. Process assertions and queries in fence order, using original or derived
   output according to the sequential rule above.
4. Check `expect-error`, stderr assertions, and any `perf` constraints.
5. Report pass/fail per scenario.

## Running all workflows

Workflow docs are independent — they can run in any order. To run the full suite:

1. List scenario `.md` files under `docs/workflows/`, excluding every
   `README.md`. Distinguish executable conformance scenarios from narrative
   demos and explicitly future/illustrative command shapes.
2. Run each file's scenarios sequentially within the file.
3. Report pass/fail per file with scenario-level detail on failures.

## Parallelizing across agent teams

Workflows are designed for parallel execution. Split the work by:

- **By file** — each agent gets one or more workflow files. No shared state between files (each uses isolated sessions).
- **By area tag** — front matter has an `areas` field. Assign agents by area (e.g., one agent runs all `routing` workflows, another runs `performance`).
- **By directory** — `getting-started/`, `core/`, `discovery/`, `advanced/`, `perf/`, `output/` are natural team boundaries.

### Example: three-agent split

| Agent | Files | Focus |
| --- | --- | --- |
| Agent 1 | `getting-started/`, `output/` | Basics and output formats |
| Agent 2 | `core/`, `discovery/` | Core inspection and API discovery |
| Agent 3 | `advanced/`, `perf/` | Network observation, offline, performance |

## Agent task completion

This is separate from scripted conformance. Some workflows include `prompt`
blocks with natural-language tasks. The workflow's reference commands,
assertions, and expected findings remain judge-side material.

### Preparing a run

1. Validate the reference path against the exact tool build first. Prepare the
   project, dependency snapshot, or other user inputs without giving the agent
   this workflow document or reference results.
2. Freeze and record task inputs and tool identity. Record the agent model,
   access to current embedded skills, and any call or time budget before
   starting. Do not silently substitute another build or production version.
3. Give the agent only the selected prompt, prepared input locations, and tool
   acquisition/invocation information. Have it begin with the current embedded
   root skill; it may choose focused skills and installed discovery/evidence.
4. Capture actual guidance, discovery, execution, failed calls, recovery, and
   the final answer. Do not coach the reader with missing syntax or expected
   findings, replace it, or rerun it while presenting the result as one trial.
5. Grade the required outcomes and interpretation boundaries, accepting
   equivalent successful command choices. Preserve setup failures and budget
   exhaustion separately from tool results.

### Judging and reporting

Report correctness and completion per required outcome, including material
findings, endpoint/asset scope, and honest unavailable or partial results.
A command that resembles the reference is not sufficient; the answer must
follow from completed evidence. Conversely, another valid discovery or
execution path is not a failure.

Use captured tool calls to identify how any omitted mechanics were obtained:
installed explanation, another discovery surface, an existing recipe, or
validation-error recovery. Successful output alone does not establish an
explain-mediated handoff.

Report call counts and failures, counting guidance and recovery consistently.
Do not infer efficiency from a smaller guide, output byte count, equal capped
call counts, or success against a production version lacking the capability.
For a discovery-premium comparison, keep the same supported tool build and
task, and follow the acceptance and representative-pilot policy in
[skill guidance](../../../taste/skill-guidance.md#evaluate-the-discovery-premium).
A whole-bundle comparison against published production answers a different
question and must retain that product's real capabilities and limitations.

Keep a receipt identifying the tested revision, selected scenarios, conformance
results, agent outcomes if run, and unresolved limitations. Do not imply that
the unselected corpus or an unrun agent mode passed.

## Pre-ship checklist

Before shipping a new build:

1. Publish the exact revision as NativeAOT and set
   `DOTNET_INSPECT_WORKFLOW_BINARY` and `DOTNET_INSPECT_WORKFLOW_VERSION`.
   Clean the product's intermediate artifacts before publishing, remove the
   prior publish directory, prepend the new apphost directory to `PATH`, and
   verify the apphost as described above.
2. Run all scripted conformance workflow scenarios. This does not require an
   agent-completion pilot for every scenario.
3. Run [perf scenarios](performance-testing.md) and check latency targets.
4. Report version and conformance pass/fail summary. Report any separately run
   agent tasks and their outcomes without implying the unrun agent corpus passed.
