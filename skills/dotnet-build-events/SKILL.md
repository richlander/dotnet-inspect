---
name: dotnet-build-events
version: 0.2.0-provisional
description: Diagnose .NET build failures and warning debt with durable dotnet build event logs, SDK summary/type views, and dotnet-inspect build drill-down views.
---

# dotnet build event views

Use this skill when `dotnet build` can produce a managed build event log or an
EventLogId. The design split is:

- `dotnet build` captures facts once and prints a small baseline view.
- `dotnet-inspect build` repeatedly queries the persisted event log for
  drill-down, grouping, source context, and reports.

Do not parse raw build logs as the primary diagnostic source when an event log is
available.

## Current view set

The current `dotnet-inspect build` views are:

| View | Use |
| --- | --- |
| `Summary` | Build health and EventLogId handoff. |
| `Types` / `DiagnosticTypes` | Rank diagnostic classes before reading rows. |
| `Diagnostics` | Mixed-severity diagnostic rows. |
| `Errors` | Build-breaking diagnostic rows. |
| `Warnings` | Warning debt rows and before/after accounting. |
| `Projects` | Project ownership and failure/warning localization. |
| `Explain` | Cluster-level meaning and first-fix guidance. |
| `Details` | Rich source-context diagnostic cards. |
| `Graph`, `Targets`, `Tasks` | Build-structure/debugging escape hatches. |

There is no `Agent` view in the current dotnet-inspect workflow. Use SDK
`summary` and `types` as the basic build views, then use dotnet-inspect for
everything deeper.

## First build commands

Start with a small SDK-owned view:

```bash
dotnet build --view summary <project-or-sln> --event-log-stderr
```

For warning cleanup or repeated compile-error classes, start with the type
rollup:

```bash
dotnet build --view types <project-or-sln> --event-log-stderr
```

The SDK prints the JSONL path to stderr. Reuse that path; do not rerun the build
just to get another view over the same facts.

## Basic triage loop

Use the emitted log path with `dotnet-inspect build`:

```bash
dotnet-inspect build <log-or-id> -S Summary --tsv
dotnet-inspect build <log-or-id> -S Types --tsv
```

Then choose the narrowest useful follow-up:

```bash
# Exact rows for one error class
dotnet-inspect build <log-or-id> -S Errors --code CS1061 --tsv

# Exact rows for one warning class
dotnet-inspect build <log-or-id> -S Warnings --code CA1819 --tsv

# Mixed-severity rows
dotnet-inspect build <log-or-id> -S Diagnostics --code CS1061 --tsv

# Project ownership in multi-project builds
dotnet-inspect build <log-or-id> -S Projects --tsv

# Strategy before reading source cards
dotnet-inspect build <log-or-id> -S Explain --code CS0305 --markdown

# Source context before editing
dotnet-inspect build <log-or-id> -S Details --code CS1061 --markdown
```

Prefer TSV for work queues and accounting. Use Markdown only when source context
or strategy explanation is needed.

## Jellyfin warning-cleanup workflow

Use this workflow for Jellyfin warning cleanup or similar large-repo warning
debt tasks.

1. Work in an isolated Jellyfin worktree.
2. Use the source-built VMR `dotnet` for every build.
3. Override Jellyfin's SDK pin only in the isolated worktree if needed.
4. Use event views for before/after accounting.
5. Fix one warning code or project cluster at a time.
6. Rebuild between clusters and compare the new event log.

Recommended baseline:

```bash
dotnet build Jellyfin.sln \
  --no-restore \
  --no-incremental \
  --view types \
  --event-log-stderr \
  /p:UseSharedCompilation=false
```

Expected first follow-ups:

```bash
dotnet-inspect build <before-log> -S Types --tsv
dotnet-inspect build <before-log> -S Warnings --tsv
dotnet-inspect build <before-log> -S Projects --tsv
```

If one warning type dominates:

```bash
dotnet-inspect build <before-log> -S Warnings --code <CODE> --tsv
dotnet-inspect build <before-log> -S Details --code <CODE> --markdown
```

Use `Details` only after selecting a warning code or project cluster. Do not ask
for every rich card unless the task requires it.

## Compatibility constraints for warning cleanup

Do not suppress warnings unless explicitly authorized. Prefer safe
implementation-only fixes first.

Treat these as likely compatibility-affecting unless project evidence says
otherwise:

- public API signature changes
- public collection type changes
- property setter changes
- enum value changes
- serialization shape changes
- `CA1819`, `CA2227`, `CA1002`, and similar API-design warnings

If a warning is skipped for compatibility, leave it unsuppressed and report why.

## Short-circuits

| Situation | Do this |
| --- | --- |
| Build succeeds and warning cleanup was not requested | Stop after `Summary`. |
| Build has one or two errors | Skip `Types`; use `Errors --tsv`. |
| One diagnostic type dominates | Query that code directly. |
| You only need edit locations | Use `Errors`/`Warnings --tsv`; skip Markdown. |
| You need context before editing | Use `Details --code <CODE> --markdown`. |
| Multi-project warning debt | Use `Projects --tsv` before editing. |
| Toolchain/analyzer crash appears | Use `Explain`; do not edit reported target files as if they are app errors. |

## Final report requirements

For warning-cleanup tasks, end with:

- before warning counts by code
- after warning counts by code
- warning codes fixed
- warning codes skipped
- reason each skipped code was skipped
- final event-log path or EventLogId
- confirmation that raw build logs were not used as the primary accounting
  source
