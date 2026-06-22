---
name: dotnet-build-events
version: 0.1.0-provisional
description: Use durable dotnet build event logs and dotnet-inspect build views to diagnose build failures and warning debt without reading noisy raw logs.
---

# dotnet build event views

Use this skill when a .NET build produces a managed build event log or EventLogId. The goal is to avoid parsing raw `dotnet build` text. `dotnet build` captures the facts once; `dotnet-inspect build` queries the persisted event log repeatedly.

This is a provisional target workflow. If a filter or `Details` view is not available in the current prototype, use the closest unfiltered TSV view and narrow the rows manually.

## First command

In the local test environment, `dotnet` is a PATH shim over the VMR SDK. It
auto-writes an event log, but you must still pick the build stdout view.

Start with a compact build view:

```bash
dotnet build --view summary <project-or-sln>
```

For warning or error-type triage, you can choose the type rollup directly:

```bash
dotnet build --view types <project-or-sln>
```

The shim prints the JSONL path to stderr and records a sidecar manifest. Use that
JSONL path in follow-up `dotnet-inspect-dev build` commands. If a build already
printed an event log path, do not rerun just to get the same facts.

## Triage workflow

Start with `Summary`:

```bash
dotnet-inspect build <log-or-id> -S Summary --tsv
```

If there are errors or warnings, get the diagnostic type rollup:

```bash
dotnet-inspect build <log-or-id> -S Types --tsv
```

If the top diagnostic looks like a known pattern, ask for an explanation:

```bash
dotnet-inspect build <log-or-id> -S Explain --code CS0305 --markdown
```

Investigate the highest-value code first:

```bash
dotnet-inspect build <log-or-id> -S Errors --code CS1061 --tsv
dotnet-inspect build <log-or-id> -S Warnings --code CA1819 --tsv
```

Use `Diagnostics` when you need a mixed severity view:

```bash
dotnet-inspect build <log-or-id> -S Diagnostics --code CS1061 --tsv
```

For multi-project builds, find the owner before editing:

```bash
dotnet-inspect build <log-or-id> -S Projects --tsv
```

Before making code changes, read the rich report or the closest available detail view:

```bash
dotnet-inspect build <log-or-id> -S Details --code CS1061 --markdown
```

Use `Explain` for cluster-level meaning and first-fix guidance. Use `Details` for source-context cards.

## Short-circuits

Do not run the full workflow when a smaller path answers the task.

| Situation | Do this |
| --- | --- |
| Build succeeded and the user did not ask about warnings | Stop after `Summary`. |
| Build succeeded and the user asked for warning cleanup | Run `dotnet build --view types`, then query `Warnings --code <top-code> --tsv`. |
| `Summary` shows only a few errors | Skip `Types`; run `Errors --tsv` to get the exact rows. |
| `Types` has one dominant code | Skip broad `Diagnostics`; query `Errors --code <code>` or `Warnings --code <code>`. |
| You only need locations for editing | Use TSV rows; skip Markdown/report. |
| You need fix context before editing | Go directly from `Types` to `Details --code <code> --markdown`. |
| Single-project build | Skip `Projects` unless ownership is unclear. |
| Multi-project build with many diagnostics | Use `Projects --tsv` before editing to find the owning project cluster. |
| Source files are unavailable or may be stale | Do not trust source-context views; use TSV diagnostics and read current files directly. |
| Warning cleanup requires before/after counts | Capture `Summary`, `Types`, and filtered `Warnings` before and after; skip `Details` unless editing needs context. |
| You already know the exact diagnostic selector or digest | Query that one diagnostic directly with `--diagnostic <id-or-digest>`. |

Good default loops:

```bash
# Small compile failure
dotnet build --view summary <project-or-sln>
dotnet-inspect build <log-or-id> -S Errors --tsv

# Repeated compile failure class
dotnet build --view types <project-or-sln>
dotnet-inspect build <log-or-id> -S Errors --code CS1061 --tsv

# Warning cleanup
dotnet build --view types <project-or-sln>
dotnet-inspect build <log-or-id> -S Warnings --code CA1819 --tsv
```

## View meanings

| View | Use it for | Default columns |
| --- | --- | --- |
| `Summary` | Build health and EventLogId handoff. | `Succeeded Projects Failed Errors Warnings EventLogId` |
| `Types` | Ranking diagnostic classes before reading individual rows. | `Severity Code Count Description` |
| `Explain` | Cluster-level meaning and first-fix guidance. | Markdown docs keyed by diagnostic code/cluster. |
| `Diagnostics` | Mixed-severity diagnostic rows and explicit severity filtering. | `Severity Code Project File Line Column Message` |
| `Errors` | Build-breaking diagnostics. | `Code Project File Line Column Message` |
| `Warnings` | Warning debt and before/after cleanup reports. | `Code Project File Line Column Message` |
| `Projects` | Project ownership and failure localization. | `Project Errors Warnings Succeeded TargetFramework` |
| `Details` | Markdown handoff before editing. | Rich diagnostic cards with source windows, selectors, digests, and anchors. |

`Errors` and `Warnings` are filtered projections over the `Diagnostics` model. They omit `Severity` because the selected view already implies it. If you need severity in the output, use `Diagnostics --severity error` or explicit column projection.

## Rich diagnostics

Rich diagnostic output is intentionally bulky. Always narrow it first.

Preferred path:

```bash
dotnet-inspect build <log-or-id> -S Types --tsv
dotnet-inspect build <log-or-id> -S Details --code CS1061 --markdown
```

Use these controls:

| Control | Use |
| --- | --- |
| `--code <CODE>` | Render one diagnostic class after looking at `Types`. |
| `--project <PATTERN>` | Focus a multi-project build. |
| `--file <PATTERN>` | Focus one source file. |
| default | Render one rich diagnostic card plus the compact index. |
| `--cards N` | Render the first N rich diagnostic cards when one card is not enough. |
| `--tail-cards N` | Render the last N rich diagnostic cards when later rows may differ. |
| `--diagnostic <ID>` | Render one card by selector, such as `E7` or `W3`. |
| `--diagnostic <DIGEST>` | Render one card by stable digest, such as `CS1061:7f3a2c`. |

Use TSV first when you only need edit locations. Rich Markdown should include a compact index with diagnostic `Id`, `Digest`, `Section`, and `Lines`, then only the requested or limited source-context cards.

Clang and Swift are good models for source snippets, carets, notes, and fix-it-style hints. Go is a good model for durable JSONL events. dotnet-inspect should combine those: queryable event data first, limited rich source cards only when needed.

## Warning cleanup with compatibility constraints

For prompts like "fix all warnings but skip compatibility-affecting fixes", use
the event views as the accounting source and be conservative with public API
changes.

Workflow:

```bash
dotnet build --view types <project-or-sln>
dotnet-inspect build <log-or-id> -S Warnings --tsv
dotnet-inspect build <log-or-id> -S Projects --tsv
```

Then fix warnings by code/project cluster. Rebuild and compare before/after:

```bash
dotnet build --view types <project-or-sln>
dotnet-inspect build <new-log-or-id> -S Types --tsv
dotnet-inspect build <new-log-or-id> -S Warnings --tsv
```

Rules:

- Do not suppress warnings unless the user explicitly asks for suppressions.
- Skip fixes that change public API shape, serialization shape, or compatibility contracts.
- Treat warnings like `CA1819`, `CA2227`, collection-type changes, property setter changes, enum value changes, and public signature changes as likely compatibility-affecting unless the repo clearly permits them.
- Prefer private/internal implementation fixes first.
- Report skipped warning counts by code and why they were skipped.

## Rules for agents

- Do not use raw build logs as the first diagnostic source when an event log is available.
- Do not use the harness-only `capture-build-event-log` as the normal path; use `dotnet build --view ...` so the build/view choice is part of the workflow being tested.
- Do not use a mixed-row shortcut view; prefer `Summary` -> `Types` -> filtered `Diagnostics`.
- Do not start with `Targets`, `Tasks`, or `Graph` for normal compile failures. Use them only for build-structure, target ordering, or performance investigations.
- Use `--tsv` for compact machine-readable triage and Markdown only when you need source context or a handoff report.
- Filter before reading many rows: use `--code`, `--severity`, `--project`, or `--file` when available.
- Use `Explain` when `Types` shows a repeated known pattern and you need fix strategy before reading source cards.
- Limit rich diagnostic cards with `--cards` or `--tail-cards`; do not ask for every card unless the task requires it.
- Prefer `--diagnostic <id-or-digest>` for follow-up on one rich diagnostic.
- Treat source context as an enrichment from the current workspace, not as raw event-log truth. If source freshness is uncertain, say so.
- For warning cleanup, capture before/after `Summary`, `Types`, and `Warnings` outputs.
- Fix one diagnostic code or project cluster at a time, then rebuild and compare the new event log.
