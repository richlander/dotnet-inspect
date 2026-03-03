# Validating Workflows

> How to run workflow scenario documents as tests — solo or with agent teams.

## What workflow docs are

Each `.md` file under `docs/workflows/` is a testable scenario document. It uses semantic code fences (`bash`, `expect`, `expect-not`, `expect-error`, `perf`) to define assertions against dotnet-inspect output. They serve as E2E smoke tests that catch regressions unit tests miss.

The [format spec](../../docs/workflows/README.md) defines the code fence types and document structure.

## Evaluation rules

1. **Report tool version first**: Run `dotnet-inspect --version` and include the output in your report. This establishes which build was tested.
2. Parse the `prompt` block as the input request (for eval systems, this is what the agent receives).
3. If a `setup` block exists, run it first to establish scenario state.
4. Run the `bash` block and capture stdout, stderr, and exit code.
5. For each `expect` line, check that it appears as a substring in stdout.
6. For each `expect-not` line, check that it does **not** appear in stdout or stderr.
7. For each `expect-error` line, check that exit code ≠ 0 and the line appears in stdout+stderr.
8. For each `expect-stderr` line, check that it appears as a substring in stderr.
9. For each `expect-not-stderr` line, check that it does **not** appear in stderr.
10. If a `query` block exists, pipe stdout through it and report the extracted value.
11. If a `perf` block exists, compare wall-clock time against `max_ms` and exit code against `exit_code`.

Commands are expected to exit 0 unless `expect-error` is used.

A scenario **passes** when all assertions hold. A variant passes independently — not every variant needs to pass for the goal to be considered covered, but failures should be investigated.

## Preconditions

Many workflows have a **Preconditions** section at the top (isolated sessions, cache priming, etc.). Always run these first.

General preconditions for all workflows:

- **NativeAOT build**: Performance numbers assume `./install.sh` has been run.
- **Warm cache**: Timing targets assume second+ invocation (OS and app caches warm).
- **Network**: Some commands require network access (e.g., `--latest-version`). Others are fully offline (e.g., `--version` with cached data).

## Running a single workflow

Parse the code fences and execute them in order:

1. Run the Preconditions section if present.
2. For each numbered goal/variant, run `setup` blocks, then `bash` blocks.
3. Check all `expect`, `expect-not`, `expect-error`, `expect-stderr`, `expect-not-stderr` assertions.
4. If `perf` blocks exist, check wall-clock time against `max_ms`.
5. Report pass/fail per scenario.

## Running all workflows

Workflow docs are independent — they can run in any order. To run the full suite:

1. List all `.md` files under `docs/workflows/` (excluding `README.md` and `eval/README.md`).
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
| Agent 3 | `advanced/`, `perf/` | Network guard, offline, performance |

## The eval pattern

Some workflows include `prompt` blocks — natural language requests that motivate the scenario. These map directly to agent evals.

### How it works

Each eval scenario uses the `prompt` → `bash` → `expect` pattern:

- **Input**: the `prompt` block — a natural language request the LLM receives
- **Expected tool call**: the `bash` block — the correct CLI invocation
- **Judge criteria**: the `expect` block — substrings that must appear in output

### Running an eval

1. Parse `prompt` blocks as eval inputs.
2. Give the LLM the prompt + tool description (from `dotnet-inspect -v:d`).
3. Check if the LLM produces a command matching or equivalent to the `bash` block.
4. Run the command and verify `expect` assertions against output.
5. Score: correct command + passing assertions = pass.

### Scoring dimensions

- **Command selection**: Did the LLM pick the right subcommand? (`type` vs `member` vs `find`)
- **Flag accuracy**: Did it use the right flags? (`--package`, `--platform`, `-v:q`)
- **Output interpretation**: Can it extract the answer from the output?

Eval scenarios are distributed across all workflow docs via `prompt` blocks. Files in `docs/workflows/eval/` are curated eval sets with high prompt density.

## Pre-ship checklist

Before shipping a new build:

1. Install with `./install.sh` (NativeAOT build).
2. Run all workflow scenarios.
3. Run [perf scenarios](performance-testing.md) and check latency targets.
4. Report version + pass/fail summary.
