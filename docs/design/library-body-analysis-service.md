# Library body Analysis service

## Status

This document is the normative owner for stateless library-body Analysis
execution, tracked by
[#7553](https://github.com/richlander/dotnet-inspect/issues/7553).

The initial adoption moves immutable-image execution behind
`LibraryBodyAnalysisService` and moves the Workspace-backed method and
optimization-opportunity queries onto that boundary. Those queries are
production inputs to Inspect Web Analysis exports; method analysis also feeds
the shared matched-member query.

`LibraryBodyIndex.Open*` remains a temporary compatibility facade for
unmigrated consumers. Each later implementation slice moves at least one
production consumer and retires its corresponding compatibility call.

## Authority and exact claim

**Library Body Analysis Execution** owns:

> Given one exact assembly input, one explicit Analysis request, and any
> owner-issued reference resolver required by that request, execute the
> selected library-body producers once and return detached
> `LibraryBodyIndex` evidence without retaining behavior-bearing state between
> invocations.

The owner defines:

- the immutable requested feature and body-scope input;
- feature-prerequisite and scope normalization;
- path and caller-supplied immutable-image execution entry points;
- PE and Metadata reader lifetime during execution;
- selected producer coordination; and
- construction of the detached Analysis result.

It does not define:

- Workspace acquisition, admission, snapshots, or lifetime;
- assembly-reference binding policy or resolution outcomes;
- query cost, capability, or participant failure projection;
- Analysis producer algorithms or evidence semantics;
- Research joins, call-graph projection, Findings, or presentation; or
- cross-operation caching or persistent Analysis state.

## Operation shape

```text
exact path or caller-owned immutable image
  + LibraryBodyAnalysisRequest
  + optional IAssemblyReferenceResolver
  -> LibraryBodyAnalysisService
       -> normalize producer prerequisites and scope
       -> open operation-local PE and Metadata readers
       -> execute the selected Analysis producers
       -> construct detached evidence
  -> LibraryBodyIndex
```

`LibraryBodyAnalysisService` is a static callable boundary, not a global
service. It has no mutable static state, ambient resolver, retained reader,
service provider, or coordinator identity.

`LibraryBodyAnalysisRequest` snapshots an explicit token scope. Its optional
type predicate remains caller-supplied behavior for the invocation. The
normalized internal plan may expand prerequisite features or evidence scope;
the returned index records the effective features and whether it covers the
full method-evidence population.

## Input ownership

The immutable-image entry point borrows an `ImmutableArray<byte>` for the
duration of synchronous execution. It does not open the supplied display name
as a path. When resolution-aware Analysis requires a root snapshot, the
service creates that root from the supplied bytes.

The path entry point is a desktop compatibility operation. It owns its stream
and reader for the invocation and may retain an immutable root snapshot while
producer execution resolves references. Path text does not establish
cross-operation content identity.

The optional resolver remains owner-issued. Analysis uses it only for features
whose existing producer contract requires reference resolution. The service
does not retain it after returning.

## Result boundary

`LibraryBodyIndex` is detached evidence. It retains image-derived module
identity, effective features and scope, producer evidence, diagnostics, and
result-local derived indexes. It owns no PE reader, Metadata reader, resolver,
stream, Workspace lease, or service instance.

This first adoption preserves the existing exception boundary. Invalid
requests fail during request construction; invalid images and producer
failures remain visible to the calling query, whose existing participant
outcome owns failure projection. A typed Analysis execution outcome should
land only with a production consumer that can preserve its distinctions
end-to-end; this design does not add an unused result algebra ahead of that
consumer.

Cancellation also remains with the current consumer contracts. Workspace
queries check caller cancellation around synchronous execution, and an
owner-issued resolver may preserve its own cancellation behavior. Adding
cooperative cancellation inside CPU producers requires a focused Analysis
execution change with producer-owned evidence.

## Consumer-led adoption

Each implementation slice is organized by a production consumer:

1. Immutable-image service execution plus Workspace method and optimization
   queries.
2. The next Workspace, Research, or CLI consumer plus only the additional
   entry point or result responsibility it needs.
3. Removal of `LibraryBodyIndex.Open*` when its final production consumer
   moves.
4. Independent evaluation of result lookup, leverage, and call-graph methods;
   each moves only with a focused owner and adopting consumer.

No service substrate, producer registry, universal request, or generic result
lands for hypothetical later adoption.

## Demo

The production query changes from evidence constructing itself:

```csharp
LibraryBodyIndex index = LibraryBodyIndex.OpenFromPrefetchedImage(
    sourceName,
    snapshot.Content,
    LibraryBodyAnalysisFeatures.OptimizationOpportunities,
    resolver);
```

to explicit execution:

```csharp
LibraryBodyAnalysisRequest request =
    LibraryBodyAnalysisRequest.Create(
        LibraryBodyAnalysisFeatures.OptimizationOpportunities);
LibraryBodyIndex index =
    LibraryBodyAnalysisService.AnalyzeImage(
        sourceName,
        snapshot.Content,
        request,
        resolver);
```

Inspect Web continues to consume the same method evidence and ranked
optimization opportunities through its existing query exports. The change is
architectural: the result no longer acts as the production service. The
browser host remains compiler-banned from calling the Analysis service
directly; Workspace queries own service execution and project its evidence.

## Evidence

The initial Release gates are:

- `LibraryBodyAnalysisService_ConsumesImageWithoutReopeningSourceName` for
  equivalent path and immutable-image evidence, including allocation fanout,
  without consuming the caller's image or treating its source name as a path;
- `LibraryBodyAnalysisService_ImageRequestHonorsBodyScope` for explicit
  request scope and unchanged prerequisite normalization;
- existing `AssemblyContextMethodAnalysisQueryTests` for exact physical
  method evidence, visible participant failures, resolver use, and
  cancellation;
- existing `AssemblyContextOptimizationOpportunitiesQueryTests` for ranking,
  public-member attribution, participant isolation, resolver use, and visible
  failure; and
- `BrowserEngineLayeringTests.BanListForbidsEverySessionAndImageDoor` and
  `EveryPublicPathMethodOwnerIsBannedOrApprovedNonInspectionSurface` for the
  query-owned Browser/Wasm boundary; and
- the normal build for the Inspect Web consumers of both adopted queries.

No source-code absence gate enforces service usage. Consumer migration is
established by the production call sites and focused behavior gates.

## Non-claims

- No `AnalysisHouse`.
- No dependency-injection framework or service locator.
- No change to Analysis algorithms or evidence.
- No asynchronous or concurrent producer execution contract beyond existing
  behavior.
- No path identity, file-stability, or persistent-cache claim.
- No decomposition of result-local lookup or projection methods in this
  slice.
