# Library-body root paths

This document owns Analysis's bounded path evidence from caller-supplied exact
roots to exact destination methods in one already-built `LibraryBodyIndex`
(issue #6601).

## Claim and owner

`ILInspector.Analysis` owns one focused operation: given one exact local body
index, exact root and destination MethodDefs, and explicit search limits,
return deterministic shortest local call witnesses with their physical call
receipts and visible completion boundaries.

Analysis does not decide which methods are public roots or direct package-use
sites. Metadata owns the exact public MethodDef root inventory under
[#7391](https://github.com/richlander/dotnet-inspect/issues/7391). Queries will
compose those roots with pairwise call-use destinations under
[#7390](https://github.com/richlander/dotnet-inspect/issues/7390), and the CLI
and Browser/Wasm hosts will consume that shared composition under #6313.
Research Queries may also compose one exact selected MethodDef root with
Analysis-issued local-throw destinations for Annotated Source. That consumer
does not change this operation's path, identity, or completion contract.

## Motivating asset

The repository's own Presentation-to-Markout dependency supplies the motivating
shape at commit
[`ac9e9b4d2be08d4cdeeb851aafef3811bdcb35e1`](https://github.com/richlander/dotnet-inspect/tree/ac9e9b4d2be08d4cdeeb851aafef3811bdcb35e1):

- public
  [`MemberSourceDiffPresentationAdapter.Create`](https://github.com/richlander/dotnet-inspect/blob/ac9e9b4d2be08d4cdeeb851aafef3811bdcb35e1/src/DotnetInspector.Presentation/MemberSourceDiffPresentation.cs#L128)
  calls
  [`TextAnalysisDiffPresentation.CreateMappedTextDiff`](https://github.com/richlander/dotnet-inspect/blob/ac9e9b4d2be08d4cdeeb851aafef3811bdcb35e1/src/DotnetInspector.Presentation/TextAnalysisDiffPresentation.cs#L14);
- `CreateMappedTextDiff` calls private
  [`AddChange`](https://github.com/richlander/dotnet-inspect/blob/ac9e9b4d2be08d4cdeeb851aafef3811bdcb35e1/src/DotnetInspector.Presentation/TextAnalysisDiffPresentation.cs#L71)
  twice; and
- both destination methods directly construct Markout-owned diff values.

Pairwise evidence therefore finds private implementation methods at different
depths below one public capability. The caller path is the missing evidence
needed to explain which public surface reaches each direct-use site. The
compiled caller-graph fixture preserves this public-to-private shape, repeated
logical edge, and compiler-generated async variant without copying the product
implementation.

## Basis

Breadth-first search is the conventional shortest unweighted-path algorithm.
The existing `CallerLoopEvidenceAnalysis.FindNearest` uses breadth-first
frontiers for nearest call evidence, while
`CallGraphProjection.FindFocusCycles` uses breadth-first paths with independent
retained-witness and search-work limits. This operation follows that convention
but remains Analysis-owned and traverses the body index directly rather than a
rendering projection.

## Exact input

The operation accepts:

- one `LibraryBodyIndex`, which fixes the physical local participant;
- a non-empty set of root `MetadataMethodAddress` values;
- a non-empty set of destination `MetadataMethodAddress` values; and
- explicit maximum depth, search-node, searched-edge, and retained-path limits.

Each address must carry the index MVID and name a declared MethodDef in that
index. The live index supplies the participant-local boundary; the later
Queries composition pairs it with acquisition registration so duplicate
physical participants remain distinct. Duplicate addresses are one set member,
and input order has no semantic effect.

## Local call graph

Vertices are declared MethodDefs in the selected index. A directed edge exists
from caller to callee for an indexed `call`, `callvirt`, or `newobj` occurrence
whose typed operand resolves to a MethodDef in the same module. Calls to the
provider, framework, or any other participant do not enter the graph.

The logical caller is `DirectCall.Caller`, including authenticated attribution
from a compiler-generated body to its declared source method. Every logical
edge retains all of its `DirectCall` occurrences. Each occurrence keeps
`DirectCall.EvidenceMethod`, IL offset, and operand token as the physical
instruction coordinate. Repeated sites therefore remain separate receipts but
contribute one graph hop.

Synchronous iterator `MoveNext` bodies are not currently attributed to their
source method by `LibraryBodyIndex`. Their calls do not fabricate a path from
the iterator method; a typed generated-body boundary makes absence incomplete.

The operation does not infer reflection, delegates, dynamic dispatch, or
runtime virtual targets. A direct `callvirt` operand remains the exact static
edge recorded by Analysis; no override closure is added.

## Witness contract

For every reachable ordered root/destination pair, the result retains at most
one witness:

- the first method is the exact root;
- the last method is the exact destination;
- each step is one admitted logical edge and carries all physical receipts for
  that edge; and
- depth is the number of logical edges.

When the same exact MethodDef is both root and destination, the result contains
the zero-depth witness; it does not replace that identity relation with a
non-empty cycle.

Analysis searches callers in reverse from each destination. The retained
witness has minimum depth for its pair. Equal-length alternatives choose the
lexicographically smallest MethodDef-token sequence from root to destination.
This tie-break uses exact local metadata identity, never labels or formatted
signatures.

Destinations are searched in ascending MethodDef-token order, and roots are
reported in ascending MethodDef-token order within each destination. This
ordering also defines which witnesses fit when the retained-path limit is
reached.

## Bounds and completion

The already-materialized `LibraryBodyIndex` and its lazily cached local
adjacency indexing are prerequisites, not path-search work. The cache is
released by `LibraryBodyIndex.ReleaseCallGraphCaches`. The operation's receipt
counts:

- one search node for each admitted `(destination, method)` state;
- one searched edge for each logical incoming edge examined;
- one depth unit for each logical edge in a candidate witness; and
- one retained path for each returned root/destination witness.

The limits are independent:

- the depth limit applies separately to each destination search;
- node and edge limits apply across the full operation;
- the path limit bounds retained witnesses.

A depth boundary identifies the destination whose reverse search could continue
beyond the requested depth. Node or edge exhaustion stops remaining search.
Path exhaustion is reported only when an additional reachable pair is
observed, so exactly filling the capacity remains complete.

An index built with a caller-supplied method or type scope carries a partial
method-evidence boundary. Typed call operands that name the current module but
cannot resolve to one MethodDef carry a local-resolution boundary.
Unattributed generated execution bodies carry a generated-body boundary. This
includes unresolved lifted local-function and lambda bodies as well as
state-machine execution bodies. The input index's diagnostic count is also
retained as one bounded analysis boundary; exact diagnostics remain available
from the index. An excluded, failed, unresolved, or unattributed body can hide
an otherwise relevant caller edge even when that method was not reached
through the partial graph. Positive witnesses remain valid when any boundary
is present; only absence and exhaustiveness become incomplete.

The result also carries a typed receipt with requested endpoint counts, search
work, observed reachable pairs, and returned witnesses. The following Release
gates own the asserted behavior:

- `FindShortestPaths_UsesShortestStableLocalWitnesses`;
- `FindShortestPaths_PreservesPhysicalReceiptsAndSemanticCaller`;
- `FindShortestPaths_HonorsExactRootSetAndLocalParticipant`;
- `FindShortestPaths_ReportsIndependentLimits`; and
- `FindShortestPaths_RetainsPositiveEvidenceAcrossAnalysisBoundary`;
- `FindShortestPaths_UnattributedLiftedBodyMakesAbsenceIncomplete`; and
- `FindShortestPaths_RejectsInvalidRequests`.

## Non-claims

This operation does not:

- infer accessibility, public API roots, features, ecosystems, or package
  membership;
- cross the selected consumer library boundary;
- establish that a provider package can be removed or source-inlined;
- enumerate every equal shortest path for one root/destination pair;
- bridge synchronous iterator methods to their generated execution bodies;
- claim a negative result when any analysis or limit boundary is present; or
- define CLI, Browser/Wasm, Markout, or serialized output.

## Production adoption

The package-use end-to-end plan remains three steps:

1. this Analysis operation supplies exact bounded root-to-use-site witnesses;
2. issue #7390 composes Metadata-issued public roots, pairwise direct-use
   destinations, provider grouping, and one shared typed result; and
3. `graph cluster` and Browser/Wasm consume that same composition under
   #6313.

The later completed host boundary will use `InspectionEnvelope<TContent>`.
This Analysis result is an internal owner-issued input to that composition, not
a host result by itself.

Annotated Source is a second production consumer. It supplies the selected
physical MethodDef as the only root, supplies exact same-index MethodDefs with
proven local throws as destinations, and joins returned first-edge physical
receipts to the existing `call.edge` Findings. Research Queries owns that join,
its local-throw completeness state, and Browser/Wasm transport. Analysis still
does not claim exception propagation or runtime execution.
