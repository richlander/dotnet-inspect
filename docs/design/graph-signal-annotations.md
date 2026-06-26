# Graph Signal Annotations

How analysis **signals** are projected onto call-graph nodes, and how the same
mechanism extends to exception-risk triage.

Related docs:

- [Output shapes](output-shapes.md) — the projection/shape model these fields plug into
- [Hidden-fact annotations](hidden-fact-annotations.md) — the per-method evidence model

## Two halves of a triage view

A useful triage graph needs both axes:

1. **Scale / leverage** — many callers, deep graph, calls in loops: the
   `fanin`/`fanout`/`depth`/`loop` cues already on `Call Graph`/`Caller Graph`.
2. **Kind of concern** — allocation, copy, unsafe, exception, reflection, I/O:
   the **signals** described here.

The high-value result is their intersection: a leveraged method that *also*
carries a costly or risky signal. Signals are projected the same way the perf
cues are — via `--fields` — so an agent requests only the fields a workflow needs
and the graph stays readable (signals never appear in the default view).

```bash
member Type Method:1 --library Product.dll -S "Call Graph" \
  --fields "Fanout,Fanin,Depth,Loop,Alloc,Copy,Unsafe"
```

```text
└─ BuildCallTree(int, int, int) (fanout 16, alloc 13)
   ├─ MethodKey(...) (copy 1, alloc 1)
   └─ <BuildCallTree>g__Build|9(...) (alloc 10)
```

## Current signals

All are carried on `CallTreePerf.Signals` (a `MethodSignals`) and rendered by
`FormatCallGraphAnnotation` (`ApiOutputFormatter`). The call-derived signals come
from `DirectCalls`/unsafe evidence; the IL-scan signals (`newarr`, throw, exception
regions) are folded in from the body scan during index build.

### Cost / kind-of-work

| Field | Means | Derivation |
| --- | --- | --- |
| `Alloc` / `Allocations` | heap allocations in the body | `newobj` call edges (`CallKind.NewObject`), `newarr` array allocations, plus `box` of value types |
| `Copy` / `Copies` | copy/materialize calls | callees in a curated set (`ToArray`, `ToList`, `CopyTo`, `GetSubArray`, `Substring`, `Concat`, `Join`) |
| `Unsafe` | method has unsafe evidence | any `UnsafeEvidence` for the method |
| `Reflection` | dynamic / metadata work | callees under `System.Reflection*`, `System.Linq.Expressions`, or `System.Activator` |

### Exception-risk

| Field | Means | Derivation |
| --- | --- | --- |
| `Throw` / `Throws` / `ThrowSites` | throw-site count in the body | `throw`/`rethrow` opcodes |
| `Exceptions` / `ExceptionTypes` / `ConstructedExceptions` | exception types constructed | distinct `*Exception` types created via `newobj` |
| `Catch` / `Catches` | the body handles | exception regions with a catch/filter handler |
| `Finally` / `Finallys` | the body has cleanup | `finally`/`fault` handler regions |

### Receipts

| Field | Means | Derivation |
| --- | --- | --- |
| `EvidenceIL` / `Evidence` / `IL` | IL offsets of the signal sites | sorted offsets of the signal-bearing instructions (`newobj`/`newarr`/`box`/`throw`/`ldftn`/reflection calls), capped for compactness |

A node renders a signal only when its count is non-zero (or, for `EvidenceIL`, when
offsets exist), so requesting `--fields Alloc` annotates only the allocating nodes.

With the exception fields projected, the existing `Caller Graph` answers
exception-reachability questions directly:

- *Where can exceptions originate?* — nodes with `Throw` (throw-site count) or
  `Exceptions` (the constructed exception types, e.g. `OperationCanceledException`).
- *Which public entry points reach throw-heavy paths?* — `Caller Graph` rooted at a
  throwing method, reading `root`/`entrypoint` classification.
- *Where is risk swallowed vs propagated?* — `Throw` without an enclosing `Catch`
  on the path.

### Cross-assembly Caller Graph

By default the `Caller Graph` reverse tree is single-assembly. Supplying a caller
scope (`--bin`/`--project`/`--caller-package`) extends it across those assemblies:
the graph is keyed by structural member identity (not assembly-local tokens), so a
dependency member surfaces the product entry points and callers that reach it.
Nodes from an assembly other than the selected member's own carry their source in
`CallTreePerf.Source`, rendered as `from <assembly>` and projectable via
`--fields Source`. This mirrors the `Callers` table's cross-assembly `Source`
column for the bounded reverse graph.

## Version-to-version analysis diff

The same per-method signals power `diff -S "Analysis Diff"`, which compares two
versions of an assembly and reports body-level signal deltas (allocations,
copies, reflection, throws/catches/finallys, unsafe, constructed-exception sets,
and optimization-opportunity shapes) per method. This is **complementary** to the
single-version `Top Leverage` / `Performance Triage` views: the diff finds
what *changed* between versions, while the single-version views find *longstanding*
cost that a diff is blind to (both sides are equally costly, so the delta is zero).

```bash
diff --package Newtonsoft.Json@12.0.3..13.0.3 -S "Analysis Diff" --changed
```

Rows are classified and ranked so the highest-value movements surface first:

- **In-place change vs added/removed.** A row is an *in-place* change only when the
  member is present in both versions. Added/removed members (`0 -> N` / `N -> 0`)
  are the dominant noise on a major-version bump and are pushed below in-place
  changes. `--changed` drops them entirely, leaving only true deltas.
- **Magnitude ranking.** Within in-place changes, rows sort by descending
  `|delta|`, so a `+5` allocation regression ranks above a `+1`.
- **Direction.** A positive delta on a cost signal is a *regression*; negative is an
  *improvement*. The summary splits the counts (`N regressions, M improvements,
  K added/removed`).
- **Loop-awareness (allocations).** An allocation row is annotated `in-loop` in the
  `Shape` column when the method allocates inside a loop, sourced from
  `MethodSignals.AllocInLoop` (a non-exception `newobj`/`newarr`/`box` in a loop
  region — independent of the hotspot threshold, so a single hot allocation still
  counts). The bit is read from the version that bears the cost: the new method for a
  regression, the old method for an improvement. In-loop allocations are
  repeated/hot, whereas one-time construction or error-path allocations are usually
  known-good, so this is the pay-dirt discriminator. It is a method-level signal
  (the method allocates somewhere in a loop), not a per-site attribution.

`--alloc-regressions` is a focus mode for the inherently file-able set: it keeps
only allocation *increases* on members present in both versions (an existing method
the maintainers made allocate more), drops every other signal and added/removed
member, and surfaces in-loop (hot) regressions first regardless of raw `|delta|`.
The summary counts the hot subset (`N allocation regressions, M in loop`). The flag
implies the Analysis Diff section, so it works without an explicit `-S`.

```bash
diff --package Newtonsoft.Json@12.0.3..13.0.3 --alloc-regressions
```

Machine output honors `--tsv` and `--jsonl` (the section serializes through the
projected-table writer, like every other tabular section), so an agent can consume
the deltas directly instead of parsing markdown.

## Growing the vocabulary

The model is deliberately small and grows by adding a field to `MethodSignals`
(call-derived in `MethodSignalAnalysis.Collect`, or IL-scan-derived via
`BodySignals`), a `FormatCallGraphAnnotation` case, and a schema field — never a new
output shape and never a change to the call-tree construction sites. Further
candidates: I/O calls, boxing, and dynamic (`callvirt` on `dynamic`) work.
