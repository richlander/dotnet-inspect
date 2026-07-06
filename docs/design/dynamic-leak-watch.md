# Dynamic Leak-Watch

The retention axis of allocation analysis: how `runfaster leak-watch` separates a
managed leak from a collectible churn storm from native/committed growth, why the
static [Allocation Triage pre-filters](allocation-triage-prefilters.md) and the
`runfaster` allocation-tick join cannot do this on their own, and the investigation
that motivated it.

Related docs:

- [Allocation Triage pre-filters](allocation-triage-prefilters.md) — the static
  cost-shape side and what it can and cannot predict about realized cost.
- The static ArrayPool leak-triage in `src/ILInspector.Analysis/LeakTriage.cs` —
  the static shape that leak-watch is the dynamic complement to.

## The investigation

`ILInspector.Decompiler.Tests` drove resident memory to 10–16 GB on the CI host.
The same run was reproduced locally on macOS (24 GB) with
`dotnet-counters`/`dotnet-gcdump`/`vmmap`:

- Resident set climbed 4.5 GB → 11.3 GB while CPU sat at 400–650%.
- Live managed heap peaked at 4.9 GB; GC-committed tracked it at 5.4 GB (not an
  over-commit).
- `vmmap` attributed 6.1 GB / 25,472 regions to the GC heap (`VM_ALLOCATE`).
- Forcing a collection (via `dotnet-gcdump`) dropped the **live** heap to 561 MB —
  the bulk was collectible churn, not retention — but resident set stayed at
  7.3 GB. The retained roots were all Roslyn caches
  (`TextKeyedCache<SyntaxToken/Trivia>`, `GreenNode`, `Microsoft.CodeAnalysis`
  symbol tables).

**Root cause.** The suite has ~20 in-process `CSharpCompilation.Create` sites, each
rebuilding the ~185 trusted-platform `MetadataReference`s per compile, run in
parallel by the xUnit v3 runner. That drives Roslyn metadata churn → gen2
promotion → multi-GB GC-committed segments the runtime returns to the OS lazily.
The cheap fix is a shared, process-lifetime `MetadataReference` cache (Roslyn
`MetadataReference`/`AssemblyMetadata` are immutable and meant to be shared); an
isolated micro-benchmark of the exact pattern (400 parallel compiles) measured
**3,950 MB → 433 MB allocated** and **6–12.9 GB → ~0.27 GB peak working set**.

## Three growths that look alike

From the outside — a rising resident set — three very different causes are
indistinguishable:

| Verdict | Signature | Actionable cause |
| --- | --- | --- |
| `ManagedRetention` | live GC heap stays high at steady state, above baseline, and a collection does not return it | a genuine managed leak — capture a gcdump, inspect retained types |
| `ChurnStorm` | high allocation rate / GC pause, heap peaks then a collection returns it near baseline | allocation rate / parallelism, **not** a leak |
| `NativeOrCommittedGrowth` | working set stays far above the live heap (and the gap *grew*) | native allocation or GC-committed regions returned to the OS lazily |

The incident above is `NativeOrCommittedGrowth`: the working set held 10.5 GB,
2.3× the 4.57 GB live-heap peak, with the native gap widening ~1 GB over the
window — the growth is off the managed heap.

`leak-watch` consumes a `dotnet-counters` `System.Runtime` CSV (it does not spawn
tools — same "bring your diagnostic artifact" model as the rest of `runfaster`)
and classifies on the **steady-state** footprint (the last complete sample) versus
the baseline: `managedRetained = liveHeap − baseline`,
`nativeGap = workingSet − liveHeap`, and whether the native gap *grew*.

## Could static Performance Triage or the runfaster join have found this?

This is the discoverability question, and the answer sharpens where each layer's
signal actually lives.

### Static Allocation Triage — No

Static analysis owns **cost-shape**: the kind of an allocation, whether it sits on
a loop back-edge, whether it is cold-by-construction. It is correct and capable at
that (see the pre-filters doc). But a leak is not a cost-shape — it is a
**lifetime/retention** property: the same `new MetadataReference[]` is a
non-event when the references are shared and a multi-GB problem when they are
rebuilt-per-call and promoted. Static analysis cannot see call frequency or object
lifetime, so it cannot rank this, and should not pretend to.

### The runfaster allocation-tick join — Partial, and it over-flags

The `runfaster` join ranks hot allocators by bytes **allocated** and (for
non-inlined code) pins them to static sites by IL offset. On this workload it would
correctly rank the Roslyn allocators at the top — but bytes-allocated measures
**churn**, not **retention**. The forced-GC evidence shows the live heap is 561 MB
against 4.9 GB allocated: the join would flag a churn storm as if it were a leak.
An allocation-tick join has no retention axis.

### The missing piece — a retention/survivorship signal

`leak-watch` adds exactly that axis. It does not rank allocators; it answers "is
the footprint that is growing actually **retained**?" — the question the other two
layers structurally cannot.

### Per-type is viable here; per-site is not

This is the counterpoint to the survivorship work in #2260. That work found that a
*per-site* promotion ratio smears across shared types and produces catastrophic
false negatives on real leaks. But a *per-type* retention signal is sound and is
exactly what this incident needs: the Roslyn types dominate both the allocated and
the live sets, so a gcdump's alive-bytes-by-type join names the retained roots
without any per-site attribution. Leak-watch stays at the coarse process-level
counters axis; a gcdump is the per-type drill-down when its verdict is
`ManagedRetention`.

## Summary

Static analysis is correct and capable on cost-shape; the allocation-tick join
reliably narrows static signal to hot allocators; neither can separate churn from
retention. `leak-watch` is the small, dependency-free retention layer that closes
that gap, and it correctly classifies the motivating incident as native/committed
growth rather than a managed leak.
