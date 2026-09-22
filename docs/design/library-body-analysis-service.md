# Library body Analysis service

## Status

This document is the normative owner for stateless library-body Analysis
execution, tracked by
[#7553](https://github.com/richlander/dotnet-inspect/issues/7553).

The initial adoption moved immutable-image execution behind
`LibraryBodyAnalysisService`, moved the Workspace-backed method and
optimization-opportunity queries onto that boundary, and moved
`AssemblyPairClusterRootPathQuery` onto the same immutable-image boundary.
Those consumers established the service but retained `LibraryBodyIndex` as its
only public result shape.

The first typed-result migration breaks that compatibility pattern.
`LibraryBodyAnalysisExecution` associates one receipt with independently named
`LibrarySafetyAnalysisResult` and
`LibraryImplementationProfileAnalysisResult` values. The second adds
`LibraryOptimizationAnalysisResult`, which owns completed optimization
opportunities, opt-in lazy allocation fanout, and generated-framework type
identities. The Library Unsafe Evidence, Member Metrics, and
Optimization Opportunities queries consume those focused types directly. One
service invocation may still coordinate several producers over one body
acquisition; that does not make their answers one semantic type.

The third migration adds `LibraryLeverageAnalysisResult` for whole-library
ranking and `LibraryCallGraphAnalysisResult` for detached call evidence, local
graph derivation, and catalog participation. Library Top Leverage and member
call-graph composition consume those focused types. The compatibility index
delegates its leverage and local graph members to the same results; it no
longer owns a second implementation.

The next call-graph adoption moves pairwise direct-use acquisition and bounded
local root-path analysis to `LibraryCallGraphAnalysisResult`. Pairwise queries
execute Analysis once per participant and consume the focused result directly;
root-path analysis accepts that result rather than requiring the compatibility
aggregate. Existing pair completion, path limits, diagnostics, and output
remain unchanged.

The first Research adoption publishes `LibraryAllocationAnalysisResult`,
extends `LibrarySafetyAnalysisResult` with its producer-owned occurrence map,
and lets `LibraryCallGraphAnalysisResult` publish its detached method signals.
`MemberProjectionProducer` receives those results and
`LibraryLeverageAnalysisResult` through one member-projection-specific input.
The input preserves their shared execution receipt; it is not a general result
bag. CLI and Workspace/L1 composition own the single Analysis execution and
pass the focused values into Research.

The second Research adoption moves IL-offset allocation, safety, and cost
composition off `LibraryBodyIndex`. `ILOffsetAnalysisInput` joins the three
focused results from one receipt. CLI coordinate composition executes Analysis
once over the already-prefetched authoritative image, scopes the request to
the selected physical MethodDef tokens, and reuses the input across coordinate
file rows. The Research producer validates source-module correspondence and
result participation without reopening Analysis.

The CLI session adoption moves both path and prefetched-image execution in
`MethodBodyInspectionSession` onto the service. The session continues to own
command-selected feature and body-scope policy, resolver binding policy, source
attribution, and reuse of one execution across requested sections. Migrated
sections consume focused results from that execution; unmigrated sections
request its lazy compatibility index.

`LibraryBodyIndex.Open*` remains a temporary compatibility facade for
unmigrated consumers. `LibraryBodyIndex` itself is also a temporary aggregate
for those consumers, not the destination for new producer evidence or query
algorithms. Each later implementation slice moves at least one production
consumer to the service and an owner-issued result type, then removes the
corresponding index dependency.

## Authority and exact claim

**Library Body Analysis Execution** owns:

> Given one exact assembly input, one explicit Analysis request, and any
> owner-issued reference resolver required by that request, execute the
> selected library-body producers once and publish their detached,
> owner-typed results without retaining behavior-bearing state between
> invocations or requiring consumers to depend on `LibraryBodyIndex`.

The owner defines:

- the immutable requested feature and body-scope input;
- feature-prerequisite and scope normalization;
- path and caller-supplied immutable-image execution entry points;
- PE and Metadata reader lifetime during execution;
- selected producer coordination; and
- construction and publication of focused detached Analysis results.

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
       -> construct owner-typed detached results
       -> publish LibraryBodyAnalysisExecution
          -> one receipt for shared identity, coverage, and diagnostics
          -> explicitly named focused result values
  -> adopting query or section consumes only its focused result
  -> LibraryBodyIndex compatibility adapter serves unmigrated consumers
```

`LibraryBodyAnalysisService` is a static callable boundary, not a global
service. It has no mutable static state, ambient resolver, retained reader,
service provider, or coordinator identity.

`LibraryBodyAnalysisRequest` snapshots an explicit token scope. Its optional
type predicate remains caller-supplied behavior for the invocation. The
normalized internal plan may expand prerequisite features or evidence scope.
The execution publication records the effective features, shared diagnostics,
and whether it covers the full method-evidence population.

The publication may aggregate several explicitly named result values so one
command can reuse one acquisition. It is not a universal result algebra,
producer registry, type-keyed bag, or semantic facade. Producer result types
remain independently named and owned; section and query APIs accept those
focused types rather than the aggregate publication or `LibraryBodyIndex`.

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

Each producer result is detached evidence. It retains only the identity,
coverage, facts, diagnostics, and typed limitations required by its owning
claim. It owns no PE reader, Metadata reader, resolver, stream, Workspace
lease, or service instance.

Publishing an unrequested focused result must remain constant-cost over its
already-produced input references. Result-local derived arrays are constructed
only when a consumer accesses a result whose producer participated.
Implementation-profile, call-graph, leverage, and optimization results share
one lazy physical-call projection, method-signal derivation, declared-method
map, and generated-framework classification internally. This keeps the result
types semantically separate without repeating retained evidence or
whole-library classification.

`LibraryImplementationProfileAnalysisResult` publishes an Analysis-issued
implementation-profile population coverage receipt beside its profile rows.
The receipt records whether profiles were requested, whether the execution
covered the full method-evidence population, the declared methods, physical
managed bodies, profiled physical bodies, unavailable physical bodies with
their Analysis-owned reasons, and the shared diagnostics. Consumers that need a
whole-library population use this receipt directly rather than inferring
coverage from missing rows, display labels, or profile order.

Common execution identity, coverage, and diagnostics may be published once in
an execution receipt. A focused result refers to that common evidence through
an explicit typed association; it does not infer correspondence from a path,
display name, equal content, or neighboring result.

The existing internal `MethodBodyAnalysisResult`, `SafetyAnalysisResult`,
`AllocationAnalysisResult`, `OptimizationAnalysisResult`,
`ResourceLifecycleAnalysisResult`, and `OwnershipFlowAnalysisResult` establish
the decomposition direction, not final public API approval. A result becomes
public only when its first production consumer fixes the smallest useful
shape. New Resource Occurrence Analysis publishes a distinct
`LibraryResourceOccurrenceAnalysisResult` containing root-bound
`ResourceOccurrenceAnalysisResult` method evidence. Its explicit admitted
effect set is carried by the request rather than by an unparameterized feature
bit. It does not add another property or projection method to
`LibraryBodyIndex`.

Resource Lifecycle Analysis follows the same parameterized boundary. A
`CreateResourceLifecycle(admission)` request selects occurrence prerequisites
and publishes a distinct `LibraryResourceLifecycleAnalysisResult`; an
occurrence-only request does not select lifecycle work. The lifecycle producer
composes occurrence evidence with same-execution control-flow and exception
facts before those operation-local facts are discarded.

During migration, `LibraryBodyIndex` may adapt the execution receipt and
focused results for unmigrated consumers. Adapter-only lazy indexes may remain
until their focused owner and consumer move. The adapter must not become the
input required by a newly migrated query.

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

The library section system is the first deliberate result-type consumer. It
currently maps its complete selected-query set to one feature union, lazily
builds one `LibraryBodyIndex`, and passes that index to five unrelated typed
queries. The migration keeps the useful single execution and removes the
shared semantic input.

| Sequence | Production consumer | Focused Analysis result |
| --- | --- | --- |
| 1 | Library Unsafe Evidence and Member Metrics sections | Safety evidence and implementation-profile results shaped from the existing internal producer outputs |
| 2 | Library Optimization Opportunities section | `LibraryOptimizationAnalysisResult`, with completed opportunities, lazy allocation fanout, and generated-framework identities |
| 3 | Library Top Leverage and member call-graph composition | `LibraryLeverageAnalysisResult` for ranking and `LibraryCallGraphAnalysisResult` for detached local/catalog graph evidence |
| 4 | Library Resource Triage section under #6731 | `LibraryResourceLifecycleAnalysisResult`, consuming `ResourceOccurrenceAnalysisResult` from #6730 |
| 5 | API/member sections, Timeline, Research, JavaScript export, and remaining CLI adapters | Bespoke owner results selected by each consumer; no mechanical aggregate substitution |

Every slice:

1. defines or narrows one owner-issued Analysis result;
2. makes `LibraryBodyAnalysisService` publish it from the shared execution;
3. changes at least one production section or query to consume that type;
4. lets `InspectionQueryContext` share the execution receipt without exposing
   `LibraryBodyIndex` to the migrated section;
5. removes the superseded index member, projection, or compatibility call when
   no remaining consumer needs it; and
6. gates unchanged section output, diagnostics, cost declaration, and
   single-acquisition behavior.

The first implementation slice migrated both Unsafe Evidence and
Member Metrics because they already projected cohesive internal
result families and exercised the library section system directly. The second
slice moves optimization completion from the index into
`LibraryOptimizationAnalysisResult`. That result retains the exact
allocation, call, declared-method, suppression, exception-type, and module-name
inputs needed to complete opportunities. It publishes completed detached rows
rather than exposing those inputs to the query. Allocation fanout remains lazy
until the query's existing opt-in selects it. The compatibility index delegates
its optimization members to the same focused result instead of maintaining a
second implementation.

The third slice moves leverage ranking and local call-tree derivation from
`LibraryBodyIndex` into their focused results. Catalog participants carry a
`LibraryCallGraphAnalysisResult`, preserving the result receipt as the
physical-artifact identity and evidence boundary. Member graph sessions retain
focused call-graph and optimization results for graph construction and
optional annotations. The existing `CallTreeNode`, `CallGraphProjection`, and
Markout lowering remain the structured and rendered output path; the slice
changes evidence ownership, not output shape or host rendering.

The next call-graph slice moves `AssemblyPairCallUseQuery` participant
acquisition and `LibraryBodyRootPathAnalysis` onto
`LibraryCallGraphAnalysisResult`. `AssemblyPairClusterRootPathQuery` executes
the service over its retained participant image and passes the focused result
to the bounded path operation. The wider member Research projection retains
its compatibility index for unrelated evidence but passes its associated
call-graph result to root-path analysis.

The first sequence-5 slice moves member Research fact production from
`LibraryBodyIndex` and `ResearchAssemblyContext` to four exact focused results:
allocation occurrences, safety evidence and occurrences, call evidence and
signals, and leverage. `MemberProjectionAnalysisInput` validates that all four
carry the same receipt and provides only the member-projection joins over those
results. Path-backed compatibility production and immutable-image L1
production each execute Analysis once; only the L1 query retains a
compatibility index for its separate callee-evidence composition.

The next sequence-5 slice moves `ILOffsetProjectionProducer` to allocation,
safety, and call-graph results from one exact receipt. CLI single-coordinate
execution prepares one token-scoped input; coordinate-file execution prepares
one input for the union of selected physical MethodDef tokens. This preserves
the existing point-fact output and typed failure boundary while removing both
`AnalysisIndexCache` and `LibraryBodyIndex` from IL-offset production.

The implementation-profile population slice adds the coverage receipt required
by Library Metrics without changing existing Member Metrics rows. It preserves
Analysis ownership of declared-method enumeration, managed-body enumeration,
body-scope qualification, recoverable diagnostics, and unavailable-body
classification so Research can later decide whether a whole-library report is
available.

The pathological graph cases remain explicit: bodiless declarations may still
be selected as roots, async and lifted calls retain physical evidence
coordinates while ranking declared sources, same-module `ModuleRef` calls
resolve locally, exact referenced versions constrain catalog edges, and cache
release must preserve answers while retaining evidence-domain derivations.
Their existing Release gates remain authoritative, supplemented by focused
result parity, catalog, and cache-boundary gates.

Resource Triage follows issues #6730 and #6731 so the new ownership path reaches
a section without returning through the old index shape.

Removal of `LibraryBodyIndex.Open*` follows its final acquisition consumer.
Removal or narrowing of `LibraryBodyIndex` itself follows its final semantic
consumer. No service substrate, producer registry, universal request, generic
result, or type-keyed result bag lands for hypothetical later adoption.

## Demo

The Optimization Opportunities production query changes from accepting the
compatibility index:

```csharp
OptimizationOpportunitiesResult result =
    OptimizationOpportunitiesQuery.Execute(
        context.BodyIndex(),
        includeAllocationFanout);
```

to accepting the owner-issued focused result from the shared execution:

```csharp
LibraryOptimizationAnalysisResult optimization =
    context.BodyAnalysis().Optimization;
OptimizationOpportunitiesResult result =
    OptimizationOpportunitiesQuery.Execute(
        optimization,
        includeAllocationFanout);
```

Other migrated sections use the same pattern:

```csharp
LibrarySafetyAnalysisResult safety = context.BodyAnalysis().Safety;
UnsafeEvidenceResult result = UnsafeEvidenceQuery.Execute(safety);
```

Top Leverage and member graph composition now use separate focused results:

```csharp
TopLeverageResult leverage =
    TopLeverageQuery.Execute(context.BodyAnalysis().Leverage);

LibraryCallGraphAnalysisResult graph =
    memberSession.AnalysisExecution.CallGraph;
CallTreeNode callees = graph.BuildCallTree(methodToken);
```

Inspect Web continues to receive owner-typed query exports. The browser host
remains compiler-banned from calling the Analysis service directly; Workspace
queries own service execution and project its evidence.
The cluster root-path query follows the same request/service shape for
`MethodEvidence` and passes the resulting `LibraryCallGraphAnalysisResult`
directly to bounded path analysis; its existing section and CLI continue to own
composition and presentation. `MethodBodyInspectionSession` similarly
translates command capability and scope policy into one request, delegates path
or prefetched-image execution to the service, retains the returned execution
for the command, and creates its detached compatibility index only when an
unmigrated consumer requests it.

## Evidence

The initial Release gates are:

- `LibraryBodyAnalysisService_ConsumesImageWithoutReopeningSourceName` for
  equivalent path and immutable-image evidence, including resolver-backed root
  snapshot construction and allocation fanout, without consuming the caller's
  image or treating its source name as a path;
- `LibraryBodyAnalysisService_ImageRequestHonorsBodyScope` for explicit
  request scope and unchanged prerequisite normalization;
- existing `AssemblyContextMethodAnalysisQueryTests` for exact physical
  method evidence, visible participant failures, resolver use, and
  cancellation;
- existing `AssemblyContextOptimizationOpportunitiesQueryTests` for ranking,
  public-member attribution, participant isolation, resolver use, and visible
  failure; and
- existing `AssemblyPairCallUseQueryTests` cluster root-path cases for public
  root composition, exact path witnesses, completion boundaries, owner
  diagnostics, and stale-selection rejection;
- existing `MethodBodyInspectionSessionTests` for path execution, requested
  features, body scope, source attribution, and cross-assembly composition;
- existing `IndexBuildInvariantTests` for one Analysis execution per command,
  plus
  `PackageIntegrationsWorkspaceTests.Create_PartitionsTfmsAndRetainsParticipantGeneration`
  for prefetched-image execution over a retained package participant;
- `BrowserEngineLayeringTests.BanListForbidsEverySessionAndImageDoor` and
  `EveryPublicPathMethodOwnerIsBannedOrApprovedNonInspectionSurface` for the
  query-owned Browser/Wasm boundary; and
- the normal build for the Inspect Web consumers of both adopted queries.

No source-code absence gate enforces service usage. Consumer migration is
established by the production call sites and focused behavior gates.

Each result-type migration additionally gates:

- the migrated section no longer accepts or acquires `LibraryBodyIndex`;
- selecting several migrated sections performs one service execution;
- selecting one migrated section does not run unrelated producers;
- the focused result preserves positive evidence and typed incompleteness;
- section output and diagnostics remain unchanged unless that slice names a
  separately reviewed correction; and
- no public result type lands without its adopting production section or
  query.

The typed migrations are gated by
`LibraryBodyAnalysisExecutionTests`,
`ImplementationProfilesQueryTests`,
`OptimizationOpportunitiesQueryTests`,
`UnsafeEvidenceQuery_RecordsFocusedAnalysisWithoutBodyIndex`,
`OptimizationOpportunitiesQuery_UsesFocusedBodyAnalysis`,
`OptimizationOpportunitiesQuery_AllocationFanoutRemainsOptIn`,
`ImplementationProfilesQuery_RunsOnlyItsFocusedProducers`, and
`MigratedAnalysisQueries_ShareExecutionWithoutBodyIndex`. The leverage and
call-graph migration adds
`CompatibilityIndex_DelegatesCallGraphAndLeverageResults`,
`ReleaseMethods_DropExactlyTheCachesTheyDocument`,
`TopLeverageQuery_RecordsFocusedAnalysisWithoutBodyIndex`,
`TopLeverageQuery_MissingProducerRemainsTyped`,
`MemberCallGraphSessionTests`, and the catalog call-graph and definition
resolution suites.

## Non-claims

- No `AnalysisHouse`.
- No dependency-injection framework or service locator.
- No change to Analysis algorithms or evidence.
- No asynchronous or concurrent producer execution contract beyond existing
  behavior.
- No path identity, file-stability, or persistent-cache claim.
- No requirement to decompose every result-local lookup or projection before
  the first section migrates.
