# Analysis index cache ownership

## Owner and claim

This document owns `AnalysisIndexCache` in
`src/ILInspector.Research/AnalysisIndexCache.cs`.

> One `AnalysisIndexCache` instance may reuse `LibraryBodyIndex` observations
> within the operation or exact Workspace realization that owns the cache, but
> `AnalysisIndexCache` itself retains no static index, path identity history,
> acquisition registration, or image snapshot across unrelated owners.

The cache is an explicit in-process owner, not a process-wide service. Current
Research entry points construct one cache for one request. A future Workspace
consumer may retain an instance for one exact realization, but must not share
it with a successor realization.

`LibraryBodyIndex` is immutable after construction. The ownership concern is
therefore retention and correspondence rather than mutation or terminal
cleanup: the cache controls how long an observed index remains reusable, while
the index itself has no release obligation.

## Exact reuse keys

The cache has two independently bounded stores.

### Path input

A path entry is keyed by:

- normalized absolute path;
- required Analysis features; and
- optional member token for member-scoped analysis.

A compatible assembly-scoped index may satisfy a narrower member request. A
member-scoped index cannot satisfy an assembly request or another member.

Before serving a hit, the cache confirms that the file length and last-write
time still match the values observed when the index was opened. A mismatch
fails visibly rather than allowing one owner to combine indexes from two path
generations. A fresh owner may open the new generation independently. Opening
is bracketed by observations of those values; a mismatch during the open also
fails visibly.

This fingerprint is a best-effort reuse check, not durable file identity.
Local files may change freely between operations. Because a new cache owner
has no history from a prior owner, `ForPath` makes no continuity claim across
operations or Workspace realizations.

### Acquired assembly input

An assembly entry is keyed by exact
`AssemblyAcquisitionRegistration` reference identity plus compatible Analysis
features and member scope. On first use, the cache opens and retains one
immutable `AssemblyImageSnapshot` for that registration. Every scoped index
for the registration is built from those exact bytes, including after index
entry eviction.

The cache consumes registration identity and does not infer equivalence from a
path, assembly name, module identifier, or equal bytes. If a path-backed
descriptor would open different bytes later, the current owner continues to
use its first snapshot; a new owner may observe the later generation.

## Lifetime and concurrency

All indexes and path fingerprints retained by `AnalysisIndexCache` are
instance fields. Releasing the cache removes every Analysis-cache root. There
is no static Analysis index store or static path-history store.

One lock protects the instance stores so an operation or realization may share
its cache across concurrent work without racing lookup, eviction, or
publication. The path-index and assembly-index lists remain bounded to eight
entries and use clear-all eviction. Owner-wide path fingerprints and acquired
assembly snapshots intentionally remain until the owner is released so index
eviction cannot erase generation identity. These policies do not transfer
identity or authority to another owner.

The returned index may outlive the cache when a caller retains it directly or
through `ResearchAssemblyContext`. That caller becomes responsible for the
remaining lifetime. The cache does not require `IDisposable` because it owns no
terminal resource; dropping its references is the complete release operation.

## Production composition

`ResearchViews` and `ILOffsetProjectionProducer` construct a fresh cache inside
one request. Each request may reuse one index across the facts or semantic
contexts it computes, but a later request starts with no inherited Analysis
state. The Decompiler annotation corpus creates one
`ResearchAssemblyContext` per assembly sweep and passes it to each member
projection, avoiding repeated whole-assembly Analysis while keeping that
retention inside the sweep.

Callers that already possess a `ResearchAssemblyContext` continue to supply it
directly. They do not reopen or rediscover its index through this cache.

`ResearchAssemblyContextCache` is a separate downstream memoization owner,
tracked by #6755. This slice proves only that `AnalysisIndexCache` adds no
process root. Until #6755 lands, the current Research memoizer can still retain
an index reached through the production projection path.

## Evidence

`ResearchFactRegistryTests` provides the Release gates:

- compatible requests through one cache owner reuse an index;
- distinct acquisition registrations remain distinct within one owner;
- one registration retains one immutable image across analysis scopes and
  index-entry eviction;
- two cache owners never share an index or path history;
- a changed path is rejected within one owner and a fresh owner can observe the
  new generation;
- owner-wide path generation remains fixed across analysis scopes and index
  eviction;
- releasing one cache owner leaves its retained index eligible for collection;
  and
- existing Research projections retain their expected facts.

The changed-path fixture uses two independently compiled repository assemblies
to prove that a reopened index describes the new bytes rather than merely
returning a different object.

The lifetime gate calls `AnalysisIndexCache` directly so it isolates this
owner's retention. End-to-end production collection remains explicitly
unverified until the downstream #6755 memoizer correction lands.

## Non-goals

- No persistent Analysis cache.
- No cross-operation or cross-Workspace continuity signal.
- No change to `LibraryBodyIndex` construction or Analysis semantics.
- No change to acquisition registration identity.
- No defense against a local file changing during one open beyond the existing
  bracketed fingerprint check.
- No deterministic collection timing while another caller retains the cache or
  returned index.
