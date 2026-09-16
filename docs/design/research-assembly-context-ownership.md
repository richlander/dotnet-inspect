# Research assembly-context ownership

## Owner and claim

This document owns `ResearchAssemblyContextCache` memoization in
`src/ILInspector.Research/ResearchFactRegistry.cs`.

> Research may share one `ResearchAssemblyContext` among producers that consume
> the exact same `LibraryBodyIndex` instance, but its memoization must not keep
> that index or context alive after the supplying owner releases them.

`LibraryBodyIndex` reference identity is the complete join currency. Research
does not infer continuity from an assembly path, module identity, content
shape, or equal analysis results. A different index receives a different
context even when both indexes describe equal bytes.

## Retention boundary

`ResearchAssemblyContextCache` uses a `ConditionalWeakTable` keyed by the exact
index instance. While an index is strongly reachable, repeated lookups may
reuse one context and its lazy assembly-wide projections. When the supplying
operation or Workspace releases that index, the table does not independently
keep either the index or its context alive.

The context retains its index so producers can read one immutable Analysis
observation. The weak table's ephemeron semantics permit the key and its value
to become collectible together even though the value refers back to the key.
This differs intentionally from an ordinary dictionary, whose key and value
would both remain process roots until explicit eviction.

This is owner-bounded memoization, not a persistent cache. It has no capacity
policy, filesystem identity, retry semantics, or cross-Workspace continuity.
`AnalysisIndexCache` separately owns how an index is obtained and how long that
owner retains it.

## Boundary case and evidence

`ResearchAssemblyContextCacheTests` is the Release gate:

- the same index instance returns the same context;
- distinct index instances over the same bytes return distinct contexts; and
- after the caller releases an index and context created outside
  `AnalysisIndexCache`, weak references to both become eligible for collection.

The final case prevents the gate from being satisfied by another process-wide
owner. Fact producers and member projections retain their existing results;
this change affects only how long their shared lazy context may be reused.

## Non-goals

- No change to Analysis index construction or `AnalysisIndexCache`.
- No cross-Workspace Research cache.
- No redesign of individual Research fact producers.
- No guarantee about collection timing while another owner still retains the
  index or context.
