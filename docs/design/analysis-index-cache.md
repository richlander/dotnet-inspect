# AnalysisIndexCache identity contract

Defines the caller-visible contract for `AnalysisIndexCache`: what a cache hit
is permitted to return, how long a returned `LibraryBodyIndex` may keep
describing whatever bytes were on disk when it was opened, and how that
relates to the assembly-image identity contract the rest of the repository
already relies on.

## Scope

Owner: `src/ILInspector.Research/AnalysisIndexCache.cs`. This document covers
only this class's two entry points, `ForPath` and `ForAssembly` -- their
cache-key definitions, eviction behavior, and image-identity guarantees. It
does not cover `ResearchAssemblyContextCache` (a downstream memoization layer
keyed on the `LibraryBodyIndex` instance this cache returns, and therefore
inheriting whichever identity guarantee this document establishes) or
`LibraryBodyIndex` itself.

This is a focused sub-contract of
[assembly image lifetime](assembly-image-lifetime.md), which owns the outer
rule this document must comply with: one assembly inspection session uses one
immutable image for its entire lifetime, and "a filesystem path may describe
where acquisition found the image; it is not permission to reopen a
potentially different file during the session." This document defines
`AnalysisIndexCache`'s complete semantic key and reuse policy, as that
document requires of every derived-result cache owner; it does not redefine
image lifetime, acquisition, or registration semantics themselves.

## Contract

Two lifetimes matter here and are easy to conflate, so this document names
both before using them, and what each one is *for*:

- **Process-lifetime** means the OS process itself: `s_pathIndexes` and
  `s_assemblyIndexes` are `static` fields with no `Dispose`, so an entry
  persists for as long as the process runs, regardless of how many separate
  inspections happen within it. It exists purely as a performance
  optimization keyed on a coordinate -- a path string or a registration
  reference (assembly-image-lifetime.md's "source coordinate": what
  acquisition request selected the artifact) -- so that a second lookup for
  the same coordinate reuses a previously built index instead of rebuilding
  one. No caller opts into this reuse or is aware it happened; it is an
  implementation detail of the two lookup functions, not a resource a caller
  requests.
- **Session** means `AssemblyInspectionSession`
  (`src/ILInspector.Metadata/AssemblyInspectionSession.cs`), a concrete,
  disposable type -- not an informal notion. Its lifetime is bounded by
  `Open(...)` and `Dispose()`, and assembly-image-lifetime.md's rule ("one
  assembly inspection session uses one immutable image for its entire
  lifetime") is scoped to exactly one such instance. Unlike the cache, a
  session exists for *intentional* sharing: a caller opens one specifically
  so that every metadata reader, method-body reader, and PDB/SourceLink
  correlation participating in that one inspection consumes the same
  retained image by construction, not by incidentally hitting the same
  cache key. Many sessions can open and close within a single process run,
  each independently bounded, each an explicit request rather than a
  side effect of a coordinate recurring.

- **The cache is process-lifetime, not session-scoped.** An entry in either
  store outlives any single `AssemblyInspectionSession` and can be reused by
  an unrelated later session in the same process -- the cache's actual scope
  is broader than the per-session scope its only real safety analogy uses,
  and nothing in its implementation narrows it to match.
- **`ForAssembly` keys by `AssemblyAcquisitionRegistration` reference
  identity**, not by path or content. A hit requires
  `ReferenceEquals(candidate.Registration, assembly.Registration)`, plus a
  compatible feature/method-token match. Because registration identity is the
  same identity the acquisition system uses to mean "one immutable image,"
  this key is compliant with the owning image-lifetime contract *as long as*
  a registration is never reused across two different physical images -- a
  guarantee this document assumes but does not itself establish or verify
  (that belongs to acquisition, per assembly-image-lifetime.md's
  boundaries).
- **`ForPath` keys by absolute path string alone** (`Path.GetFullPath`, plus
  the same feature/method-token match) and, on a miss, opens the file
  directly via `LibraryBodyIndex.Open(fullPath, ...)` -- it does not go
  through acquisition or registration at all. **This was found to violate
  the owning contract**: a `ForPath` cache hit returns whatever
  `LibraryBodyIndex` was built from the file's contents at some earlier,
  unbounded point in this process's lifetime, with no check that the file at
  that path still describes the same bytes. See "Path-keyed staleness"
  below.
- **Eviction is all-or-nothing per key space.** Both `s_pathIndexes` and
  `s_assemblyIndexes` are bounded lists capped at `MaxCachedIndexes` (8); a
  miss that would grow either list past that bound clears the *entire* list
  first, rather than evicting one entry (e.g. LRU). This is a capacity
  policy, not a correctness one: it affects hit rate, not what a hit is
  allowed to return.
- **Both key spaces are guarded by one lock (`s_indexLock`) shared between
  them.** All list scans, evictions, and insertions for both `ForPath` and
  `ForAssembly` happen while holding this lock, so concurrent callers never
  observe a torn list or race an insertion against a concurrent scan. This
  part of the contract was already sound: a coarse mutex around plain list
  operations has no interleaving to model beyond ordinary mutual exclusion.

## Path-keyed staleness

The most significant finding of this effort: **`ForPath` can return an index
that no longer matches the file at its cached path**, in either of two ways.

1. **A stale hit.** Caller A opens path `P`, gets an index cached under `P`.
   The file at `P` is later replaced (rebuilt, redeployed, or overwritten by
   any process) without the path changing. Caller B, in the same process,
   requests `P` again with a compatible feature/token match and receives
   Caller A's index -- built from bytes that no longer exist at `P` -- with
   no error, no staleness signal, and no re-open.
2. **A stale reopen.** The same scenario, but the entry was evicted (list
   full) before Caller B's request. `ForPath` reopens `P` directly with no
   identity check against what Caller A originally observed; if the file has
   changed further since Caller A's open, the newly cached index still
   carries no record of *which* generation of the file it now describes.

Both are direct instances of the pattern assembly-image-lifetime.md already
names as unsound: "Comparing a file before and after separate opens is not
equivalent: a moving path can change from A to B and back to A between
observations." `ForPath` never compares at all -- it has no fingerprint to
compare against.

Today's only caller of `ForPath` (`ResearchViews.ResolveAssemblyContext`, via
`ILOffsetProjectionProducer.TryOpenAnalysisIndex` when no
`ResolvedAssemblyReference` is available) reaches it from one-shot CLI
command invocations, where the practical exposure window is one process run
-- but the class itself is process-global library infrastructure with no
session boundary of its own, so nothing in its implementation limits this
exposure to that usage. A future long-lived host (a server, a watch mode, an
MCP surface) that reuses `ILInspector.Research` inherits this gap
unconditionally, and the *within-one-run* exposure is not zero either: a
build or deploy that replaces the inspected file mid-run, or a run that
inspects the same path across multiple distinct commands, can already
observe it today.

`ForAssembly` does not share this exposure for the same reason `ForPath`
does: its key is registration identity, which acquisition already binds to
one immutable image. Only one caller
(`ILOffsetProjectionProducer.TryOpenAnalysisIndex`) additionally
cross-checks the returned index's module version ID against an independently
obtained one from `PdbContext`, and would surface a mismatch as a visible
`InvalidOperationException` -- but that check is caller-side, does not
apply to any other consumer of `ForAssembly`, and is not part of this
cache's own contract.

**Fixed:** `ForPath` now records a lightweight file-identity fingerprint
(`FileInfo.Length` and `LastWriteTimeUtc`, the same fields
`LocalArtifactSource.cs` already uses for exactly this purpose). A cache hit
re-observes the file's current fingerprint and, on any mismatch, evicts the
stale entry and reopens the path as a fresh entry instead of returning the
mismatched index -- this closes the gap where a hit or a post-eviction
reopen returns a *known-stale* result with no check at all.

A first version of this fix captured the fingerprint only once, immediately
after `LibraryBodyIndex.Open` returned. Round-1 review (both seats,
independently) found this insufficient: `Open` reads and closes its own file
handle internally, so a replacement landing in the narrow window between
`Open` returning and the fingerprint observation would be recorded as the
fingerprint for an index that was actually built from the *previous*
generation's bytes -- silently caching a mismatch that looks verified but
isn't. The fix now **brackets** the open with a fingerprint taken
immediately before and immediately after it, and only caches the result when
both observations agree; a mismatch (or either observation failing, e.g. the
file disappearing mid-open) leaves the freshly built index uncached but
returns it to the caller, so the next request re-opens and
re-verifies from scratch rather than trusting an identity this open couldn't
confirm.

This narrows the exposure window from "unbounded, for as long as the
process retains the cache entry" to "the duration of one `Open` call," but
does not eliminate it: a replacement landing inside that narrower window is
still possible in principle, and closing it completely would require
deriving the fingerprint from the exact bytes read (as `LocalArtifactSource`
does, by reading the fingerprint off the same stream handle used for the
content) rather than from two separate path-based observations. That
approach was deliberately not taken here: `LibraryBodyIndex.Open` chooses
between a fully-materialized read and a lazy, seek-based read depending on
whether the request is scoped to one member (see its own comments on
`PEStreamOptions.PrefetchEntireImage` vs. `Default`), and forcing every
`ForPath` call through a byte-materializing entry point would silently
undo that scoped/lazy performance choice -- a different owner's contract
(`ILInspector.Analysis`'s I/O strategy), not this cache's to redefine. The
residual open-duration race is therefore **unverified**: no gate proves it
closed, and it is called out here rather than left implicit.

## Non-claims

This document does not define or change:

- `ForAssembly`'s registration-identity contract itself (owned by
  assembly-image-lifetime.md and acquisition);
- `ResearchAssemblyContextCache`'s memoization contract;
- the eviction *policy* (clear-all vs. LRU) -- a capacity/performance
  concern, not a correctness one addressed here;
- a guarantee that the fingerprint check detects every possible file
  mutation (see the known limit above); or
- any session-scoping mechanism for the cache as a whole -- the cache
  remains process-lifetime; only the staleness-detection gap for `ForPath`
  is closed.
