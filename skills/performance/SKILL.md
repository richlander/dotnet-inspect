---
name: dotnet-inspect-performance
version: 0.1.0
description: Find high-leverage .NET methods with allocation and repetition evidence, trace candidate costs through calls and IL, and select focused profiling or benchmark follow-ups rather than claiming measured wins.
---

# dotnet-inspect: Find .NET Performance Optimization Candidates

Use this skill to find Members worth investigating, not to infer measured wins.
Run `dnx dotnet-inspect -y -- <command>`. This static analysis is experimental;
use installed discovery for current shapes, fields and query rules.

## Start with leverage and actionable candidates

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "Top Leverage"
dnx dotnet-inspect -y -- library MyLib.dll -S "Performance:*" \
  --where "Priority>=high" --top 20 --tsv
dnx dotnet-inspect -y -- library -D @Performance
dnx dotnet-inspect -y -- library -Q "Performance: Arrays" --json
```

Top Leverage ranks call-graph reach, not execution frequency. Intersect its
Members with performance candidates to choose a focused drill. A large
call-graph footprint can matter even when a local cost is small.

Library triage has kind-scoped sections; Type/Member scope uses
`Performance Triage`. Use `--all` for non-public Type/Member targets.
`--top` selects a curated ranked prefix, while `-n`/`--rows` shape output;
they are not interchangeable. `@Performance` includes heterogeneous sections,
so use concrete sections or `Performance:*` for compatible row output.

## Keep priority, confidence and runtime cost separate

- **Priority** is static actionability, not measured hotness.
- **Confidence** describes evidence/rewrite certainty, not importance.
- A potential allocation is not guaranteed bytes or frequency.
- Repetition in a local or caller loop does not prove that the loop executes
  often, and recursive structure does not prove realized depth.
- Async state-machine allocation can be amortized; it is not automatically
  an avoidable per-item cost.

Focus first on IL-visible costs such as boxing, arrays, delegates and
materialization. Do not prescribe rewrites for work the JIT may remove
without runtime evidence. A scan-in-loop candidate needs evidence that the
scanned population grows; a small-array candidate needs escape/lifetime
evidence before recommending stack allocation.

For an unfamiliar shape, use `explain "<question>"` and follow its returned
resource and binding. Use `-Q` before constructing predicates or field orders;
displayed fields do not automatically support either.

## Drill one exact Member

```bash
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll \
  -S "Performance Triage" --json
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll \
  -S "Call Graph" --fields "Alloc,Loop" --tree
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll \
  -S "Annotated Source,IL" --offline
```

Choose a copyable stable selector from the inventory rather than inferring
one from a formatted label. Retain Candidate, Finding, provenance and physical
Member/IL coordinates. Exact, aggregate and unmatched provenance are different
claims: an aggregate candidate is not an exact allocation occurrence.
Generated async bodies may own the physical IL receipt even when the displayed
Member is the source-facing method.

Follow calls toward public entrypoints and callers, then inspect the local
operation. A function load or callback registration does not establish
invocation. Source or IL explains the candidate; a representative benchmark,
allocation trace or profiler establishes runtime cost and rewrite benefit.

`Call Graph` machine rows are available through `--jsonl`/`--tsv`; a separate
Member Share URL opens its public API Overview, not this performance lens.

## Broader investigations

| Question | Entrance |
| --- | --- |
| How much implementation is concentrated in a few Members? | `library MyLib.dll -S "Library Metrics"` |
| Where do allocations fan out through construction paths? | `library MyLib.dll --triage-shape allocation-fanout --top 20 --tsv` |
| Which costs repeat through caller loops? | `library MyLib.dll -S "Performance:*" --where "CallerLoop=direct" --json` |
| Does pooled-resource cleanup deserve attention? | `library MyLib.dll -S "Resource Triage" --jsonl` |
| When did one allocation appear? | `diff --history --package MyLib@old..new -t MyType -m Method --finding analysis.allocation --at first --at last` |

Pool churn on exception paths is a hardening/profiling candidate, not a proven
permanent leak or memory-corruption finding. Resource Triage has no positive
completion receipt: describe pooled-resource coverage as unverified, even
when rows are present. Load `skill correctness` for ownership interpretation.

History checkpoints locate a window, not exact onset. Confirm the adjacent
pair with `diff --package MyLib@old..new -t MyType -m Method --finding
analysis.allocation`; a native Added occurrence establishes introduction,
unlike an aggregate count delta. Load `skill compatibility` for the search
policy and boundary interpretation.

Stop when each pursued candidate is confirmed, dismissed or explicitly open.
Report static observations separately from measured cost, proposed rewrites
and unverified coverage.
