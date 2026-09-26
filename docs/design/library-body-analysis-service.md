# Library body Analysis service

## Status

This document is the normative owner for stateless library-body Analysis
execution, tracked by
[#7553](https://github.com/richlander/dotnet-inspect/issues/7553).

The selective implementation-metric extension is tracked by
[#8450](https://github.com/richlander/dotnet-inspect/issues/8450) as the
Analysis-owned second step of
[#8445](https://github.com/richlander/dotnet-inspect/issues/8445).
It replaces complete-profile overproduction with an explicit metric-evidence
request, an owner-issued prerequisite plan, and an actual work-participation
receipt. The existing complete implementation profile remains a compatibility
projection during migration.

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

The group-wide call-census adoption uses the same focused execution boundary.
`AssemblyContextCallCensusQuery` acquires every available participant from one
admitted assembly group, while `CatalogCallGraphScope` owns exact member and
static-call correspondence, physical occurrence retention, canonical ordering,
unresolved physical occurrences, and graph diagnostics. Acquisition failures
remain Query evidence beside the positive census; no compatibility index
participates.

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

Parameterized implementation-metric evidence follows the same request
boundary as Resource Occurrence Analysis. It does not become another
`LibraryBodyAnalysisFeatures` bit. The request records exact metric evidence
separately from compatibility features so omission means "do not run", not
"produce the complete profile".

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

## Selective implementation metric Analysis

### Owner extension and exact claim

**Library Body Analysis Execution** additionally owns:

> Given one exact assembly input, one non-empty implementation-metric evidence
> request, one explicit body scope, and explicit work bounds, normalize only
> the semantic and execution prerequisites required by that evidence, execute
> each participating per-body stage at most once per physical body and each
> population stage once per execution, and publish detached typed evidence
> with authoritative requested/effective scope and actual work-participation
> receipts.

This extension owns selection and coordination, not the metric algorithms.
`MethodCallAnalysis`, `MethodSafetyAnalysis`,
`MethodImplementationProfileAnalysis`, `BodySignalAnalysis`, control-flow
construction, declared-source resolution, and their other focused owners keep
their existing factual definitions. The execution owner decides whether each
owner participates and arranges shared prerequisites.

The extension does not make `MethodImplementationProfile` the request model.
That type is one complete compatibility projection over finer owner-issued
evidence. It also does not make each profile property a separately scheduled
producer. Public evidence kinds align with useful consumer facts; executable
work stages align with meaningful avoidable cost.

### Conventional basis and deliberate boundary

The design transfers four existing repository precedents:

- parameterized Resource Occurrence requests prove that evidence whose meaning
  includes caller-supplied selection belongs beside, not inside, the coarse
  feature enum;
- `LibraryBodyAnalysisPlan` already distinguishes requested and effective
  physical scope while declared-source resolvers own authenticated expansion;
- focused result migrations preserve one shared execution without making
  `LibraryBodyAnalysisExecution` a semantic result bag; and
- lazy allocation fanout in `LibraryOptimizationAnalysisResult` proves that an
  existing complete result can defer an expensive enrichment until a consumer
  explicitly selects it.

The deliberate addition is an actual participation receipt. Existing plans and
feature flags describe permission and normalized intent, but cannot prove that
a stage started. Unlike a compiler pass manager, this design does not register
arbitrary producers or discover dependencies dynamically. The evidence and
work vocabularies are closed, reviewed Analysis contracts because their names
become observable cost and completeness claims.

### Request shape

`LibraryBodyAnalysisRequest` gains an optional, immutable
`ImplementationMetricAnalysisRequest`. The existing body token and type scope
remain on the outer request so one service invocation can share exact scope
with other selected Analysis producers.

Conceptually:

```text
LibraryBodyAnalysisRequest
  compatibility features
  requested body-token or type scope
  optional ImplementationMetricAnalysisRequest
    non-empty requested evidence kinds
    physical-body and encoded-IL work bounds

-> LibraryBodyAnalysisPlan
     requested metric evidence
     effective metric evidence
     effective metric work stages and prerequisite reasons
     requested physical scope
     expanded physical scope and attribution reasons

-> one LibraryBodyAnalysisExecution
     LibraryImplementationMetricAnalysisResult
       typed per-body evidence outcomes
       relationship evidence
       evidence and scope receipt
       actual work-participation receipt
```

A dedicated constructor such as `CreateImplementationMetrics` validates the
non-empty evidence set, known evidence kinds, positive bounds, and ordinary
body-scope invariants. It may also carry unrelated compatibility features when
a production consumer deliberately shares one execution. Analysis receives
the exact requested evidence set, not the MemberMetricsInspect authorization
ceiling that the Query owner has already resolved.

Convenience requests expand before plan construction to a named, versioned
evidence set. There is no implicit "all" when the evidence set is omitted or
empty. A `CompleteProfileV1` convenience names the evidence required by the
current `MethodImplementationProfile`; later evidence additions do not
silently increase its cost.

The temporary `ImplementationProfiles` compatibility feature has no caller
budget to preserve. While that flag exists, normalization uses a named
`LegacyUnbounded` metric budget and records the compatibility origin. Every
new `CreateImplementationMetrics` caller supplies finite positive bounds.
Removing the final compatibility caller removes `LegacyUnbounded`; it is not a
general public convenience.

The initial work bounds are:

- the maximum number of effective physical managed bodies; and
- the maximum total encoded IL bytes admitted for metric execution;
- the maximum number of physical bodies probed while authenticating
  declared-source and generated-body attribution; and
- the maximum encoded IL bytes decoded by those attribution probes.

Attribution-probe work is charged before each owner-required acquisition or
decode. Scope expansion then applies the physical result-body bound. Result
body size is charged after body acquisition and before metric-body decode or a
topic producer starts. Attribution exhaustion marks physical scope incomplete;
result-body exhaustion marks the deterministic metadata-ordered remainder
budget-exhausted. Both retain already completed evidence rather than returning
a successful empty result or retrying without bounds.

### Evidence vocabulary

The initial closed evidence vocabulary is:

| Evidence kind | Published fact | Minimum owned prerequisite |
| --- | --- | --- |
| Body size | Encoded IL byte count | Managed body acquisition |
| Instruction shape | Instruction and distinct opcode counts | Canonical method context |
| Control flow | Blocks, branches, switches, loops, and ordinary-flow complexity | Canonical method context |
| Exception regions | Catch, filter, finally, and fault counts | Managed body acquisition |
| Locals | Declared local count and decode completeness | Local-signature decode |
| Direct calls | Invocation and distinct-target counts | Canonical method context and metric call collection |
| Sibling overload relationships | Exact relationship rows and incoming/outgoing counts | Direct calls and family relationship projection |
| Allocation count | The current implementation-profile allocation count | Canonical context and allocation-signal collection |
| Allocation occurrences | Owner-issued allocation sites, shapes, escape, and multiplicity | Canonical context and allocation-occurrence collection |
| Throw count | The current implementation-profile throw count | Canonical context and body-signal collection |
| Unsafe presence | The current implementation-profile unsafe judgment | Selected declaration, local, opcode, and call safety evidence |
| Direct Reflection calls | The current implementation-profile Reflection count | Metric call collection and target classification |
| Async | The current implementation-profile async judgment | Declared-source and async-body attribution |

Evidence-kind names describe stable result facts. They do not expose source
class names or promise that every kind has an independently avoidable machine
instruction.

Allocation count does not select escape-classified `AllocationOccurrence`
rows. Allocation-occurrence evidence explicitly selects that existing focused
producer and can derive a count without also requesting the cheaper count
kind. `CompleteProfileV1` selects the current allocation count, not allocation
occurrences, because that preserves the existing profile meaning and cost.
Unsafe presence does not select all `UnsafetyOccurrence` rows. Those richer
safety rows retain their existing explicit request and owner-issued result
contract.

Declared-source and generated-body attribution are mandatory result
correspondence, not optional metric evidence. Every metric result identifies
the logical declared method and the physical evidence method when that
relationship is authenticated. Requesting the Async evidence kind controls
publication of the async metric; it does not let a caller omit the
correspondence required to interpret every other logical metric.

### Evidence and work prerequisite normalization

The plan preserves three different concepts:

```text
requested evidence
  -> semantic evidence prerequisites
  -> effective evidence
  -> executable work prerequisites
  -> effective work stages
```

A semantic evidence prerequisite is another public evidence result required to
make the requested result meaningful. Sibling overload relationships, for
example, add direct-call evidence to the effective set. The result records
that edge.

An executable work prerequisite is internal work required by an existing
owner contract. Direct calls currently consume the canonical
`MethodBodyAnalysisContext`; that context includes decoded instructions,
blocks, loop regions, exception regions, and decoded locals. Selecting direct
calls therefore participates in the canonical-context stage, but it does not
add Instruction shape, Control flow, Exception regions, or Locals to the
effective evidence set and does not publish those metric cells.

The initial work-stage vocabulary is:

- physical-scope and declared-source metadata attribution;
- declared-source body probing;
- managed body acquisition;
- local-signature decode;
- canonical method-context construction;
- metric direct-call collection and target classification;
- allocation-signal collection;
- allocation-occurrence collection;
- body-signal collection;
- safety collection; and
- sibling-relationship projection.

These are behavioral cost boundaries used by receipts and gates, not a generic
producer registry. New stages are additive only when a new claim needs to
distinguish avoidable work.

Body size and exception-region counts read the acquired managed body without
constructing `MethodBodyAnalysisContext`. Locals may decode the local signature
without constructing the instruction and control-flow context. Instruction
shape and every result whose current owner consumes
`MethodBodyAnalysisContext` share exactly one canonical context per physical
body.

Declared-source attribution may use its owner's separate body probe and
instruction decoder before the effective metric body population is known. That
work is neither canonical metric context construction nor free metadata
enumeration. It has its own budget, stage participation, tokens, charged bytes,
completion, and diagnostics.

Metric direct-call collection uses the narrow projection needed by the
effective metric evidence. Optional call value flow, optimization receiver
sources, local-throw qualification, allocation multiplicity, and unsafe-call
projection participate only when requested metric evidence or another
co-running Analysis feature needs them. In particular, sibling relationships
do not select allocation occurrence classification merely because the current
general `MethodEvidence` path supplies an allocation multiplicity callback.

No fine-grained metric request normalizes to the opaque
`LibraryBodyAnalysisFeatures.MethodEvidence` feature. The implementation may
share the same underlying context or call traversal with co-running features,
but it gates each optional topic projection by the union of the exact requests
that need it.

### Scope and attribution

The result distinguishes:

- caller-requested body tokens or full/type-filtered scope;
- directly admitted physical managed bodies;
- physical bodies added for authenticated async or lifted-source evidence;
- effective physical bodies after expansion and work bounds; and
- declared methods that have no managed physical body.

Existing `LibraryBodyDeclaredSourceResolver` and
`LibraryBodyAsyncSourceResolver` remain the owners of authenticated expansion
and attribution. Metric selection makes their scope expansion eligible even
when the coarse `MethodEvidence` feature is absent. The metric result retains
each expansion edge with its physical token, declared owner token when known,
and owner-issued reason.

If attribution probing exhausts its bound or fails before the owner can prove
scope closure, the result retains every authenticated edge found so far and
marks physical scope incomplete. It does not claim that omitted generated
bodies do not exist. `MemberMetricsInspect` may use the retained values for
non-authoritative decoration but cannot issue an authoritative Count, Top, or
complete-family relationship result from that scope.

The execution receipt proves exact Analysis scope; it does not prove that a
caller supplied a complete overload family. `MemberMetricsInspect` establishes
that population fact from its Member subject and `Overloads` receipt before
requesting sibling relationships, then verifies that the Analysis scope
receipt corresponds to that population.

### Execution and actual participation

`LibraryMethodAnalysisRunner` is split into conditionally entered stages while
retaining one metadata-ordered per-method lifecycle. A stage is created only
when at least one effective metric kind or co-running feature requires it.
Shared stages execute once and distribute their typed facts to the selected
topic owners.

The runner no longer treats `includeMethodEvidence` as permission to create
all method-evidence scaffolding. In particular:

- body-header evidence completes before canonical context construction;
- `MethodAllocationFacts` is not created for body size, exception regions,
  calls, or sibling relationships unless selected allocation or optimization
  work requires it;
- local safety inspection and unsafety occurrence collection run only for
  selected safety evidence or another explicit safety consumer;
- `BodySignalAnalysis` runs only for selected signal evidence or another
  explicit signal consumer;
- call collection runs only for selected call-derived evidence or another
  explicit call consumer; and
- complete-profile projection consumes already published metric evidence
  rather than triggering another body pass.

Recoverable failure is stage-local where its prerequisites allow it. A
successful body acquisition may publish body size and exception-region counts
even when instruction decoding later fails. A failed call classification does
not erase completed structural evidence. Every dependent evidence outcome
retains the same diagnostic or a typed prerequisite-failure reason.

An existing topic producer may publish a usable partial value without exposing
whether its internal recoverable path completed. Entry and exit instrumentation
cannot manufacture that missing evidence. Such a value is
`Incomplete(ProducerCompletionUnverified)` until its owning producer publishes
a typed completion or limitation outcome. In particular, body-signal and
allocation-occurrence evidence require focused owner amendments before
MemberMetricsInspect may treat them as authoritative.

Those owner amendments define only their producer-local completion boundary.
They do not move signal or allocation algorithms into Library Body Analysis
Execution. A recoverable failure inside one producer must retain its partial
typed value and owner-issued limitation without erasing independently completed
metric evidence.

Parallel scheduling may remain an implementation choice, but publication
order, scope accounting, work charging, and participation counts remain
metadata-stable.

### Participation and scope receipt

`LibraryImplementationMetricAnalysisResult` publishes an Analysis-issued
receipt containing:

- the requested and effective evidence sets;
- semantic and work prerequisite edges with owner-issued reasons;
- the requested, directly admitted, expanded, and effective physical scopes;
- scope-expansion edges and diagnostics;
- configured and consumed attribution-probe and metric-body work bounds;
- actual work-stage participation; and
- per-evidence available, incomplete, unavailable, failed, and
  budget-exhausted counts.

Actual participation is recorded at the execution point where a work stage
starts, not copied from `LibraryBodyAnalysisPlan`. Each participating stage
records the evidence or co-running feature causes, attempted physical-body
count, completed count, and unavailable or failed count. An effective stage
that has no eligible managed body remains visible as selected-but-not-started;
it is not falsely reported as participating.

The receipt separately states whether stage-participation instrumentation
covers every execution path used by the request. That fact does not claim that
the evidence population is complete: work-bound exhaustion, unavailable
evidence, failures, and per-evidence outcomes remain authoritative for
population completeness. A focused request can therefore have complete
stage participation while reporting budget-exhausted evidence.

The receipt therefore distinguishes:

- requested evidence from evidence added by semantic prerequisites;
- planned stages from stages that actually executed;
- shared stages from metric-specific topic projections; and
- absent work from work that ran but produced incomplete or failed evidence.

Tests and downstream operations use actual participation to prove minimum
work. `LibraryBodyAnalysisReceipt.Features` remains the compatibility feature
receipt and is insufficient for that claim.

### Focused result

`LibraryImplementationMetricAnalysisResult` is one closed, owner-typed result,
not an open result bag. It contains:

- the common execution receipt association;
- the implementation-metric participation and scope receipt;
- the declared-method roster needed for logical coverage;
- physical-body rows with logical and evidence identities;
- named optional evidence outcomes for the closed evidence vocabulary;
- exact sibling-overload relationships when requested; and
- Analysis diagnostics and typed limitations.

Each named evidence outcome is one of:

- available, with a complete typed value;
- incomplete, with a usable value and owner-issued reasons;
- unavailable, with a reason such as no managed body, scope exclusion, or
  unsatisfied prerequisite;
- failed, with the recoverable Analysis diagnostic; or
- budget exhausted, naming the exhausted bound.

Omission means not requested. It is never projected as zero, false, or an
available empty relationship set. The complete physical population remains
distinguishable from the declared logical population so bodyless methods and
generated bodies do not disappear.

`ProducerCompletionUnverified` is an incomplete reason, never an available
complete value. A downstream predicate, authoritative Count, semantic Top, or
complete-family result cannot treat it as complete evidence. A decoration may
display the value only while preserving its incomplete state.

Publishing an unrequested metric result remains constant-cost. The execution
may retain shared operation-local facts until all selected focused results are
constructed, then releases reader-bound state before returning.

### Complete-profile compatibility and retirement

During migration,
`LibraryBodyAnalysisFeatures.ImplementationProfiles` normalizes to the named
`CompleteProfileV1` metric request. The plan records that compatibility origin,
and the focused metric result remains the only body evidence source.

`LibraryImplementationProfileAnalysisResult` becomes an adapter over
`LibraryImplementationMetricAnalysisResult`. It preserves:

- every current `MethodImplementationProfile` field and meaning;
- exact overload relationships;
- logical and physical identities;
- incompleteness reasons and population coverage;
- generated-framework classification;
- current ordering and diagnostics; and
- existing CLI and Inspect Web exact-family output plus Library Metrics'
  `Complexity Explorer` and `Relationship Crossing` document.

The adapter performs no body acquisition, instruction decode, call scan,
safety scan, signal scan, or scope expansion. Compatibility parity is required
before any existing caller moves. Production callers then replace the feature
bit with `CreateImplementationMetrics(CompleteProfileV1, ...)`; the enum value
and compatibility normalization are removed after the final caller migrates.

The adapter preserves the current profile's `IsComplete` and existing
diagnostic behavior even when the focused result additionally marks a
producer's completion unverified. Newly surfaced producer limitations remain
additive focused-result evidence until the producer's owning design explicitly
approves a compatibility projection change.

The first selective caller is the host-neutral `MemberMetricsInspect`
operation from #8445. It requests the Query-derived evidence set directly and
consumes the focused metric result, not the complete-profile adapter.
Its initial compact capability needs Body size and Sibling overload
relationships. Capability discovery advertises each broader evidence kind only
after that kind's owner can issue the completion and limitation evidence
required by this contract.

### Pathological cases

The contract includes:

- a bodyless overload, which remains in the declared roster with unavailable
  body evidence and consumes no IL budget;
- a valid managed body whose instruction stream is malformed, which may retain
  completed size and exception-region evidence while context-dependent
  evidence fails;
- a lifted-source attribution probe that exhausts its separate body or byte
  bound, which retains authenticated mappings but marks physical scope
  incomplete;
- an async or lifted body added by authenticated scope expansion, whose
  physical token, declared owner, expansion reason, and charged work remain
  visible;
- an Async-only request, which performs attribution but does not construct the
  canonical method context;
- a sibling-relationship request over an incomplete family, which Analysis
  executes over its exact supplied scope but which MemberMetricsInspect rejects
  as non-authoritative using its population receipt;
- a bound exhausted after earlier metadata-ordered bodies completed, which
  preserves their evidence and marks every omitted body budget-exhausted;
- a signal or allocation producer that returns partial evidence after an
  internal recoverable failure, which remains usable but
  completion-unverified until its owner supplies a limitation;
- a selected stage with no eligible managed body, which is recorded as planned
  but not participating; and
- a shared execution where another feature requires broader work, whose stage
  receipt records both causes rather than attributing that work to metrics
  alone.

### Platform and trust boundary

Selective metric Analysis retains the existing SRM-only, inspected-code-inert,
NativeAOT-compatible execution boundary. It reads the caller's exact immutable
image, never loads or executes the inspected assembly, opens no network
resource, and returns no reader-bound state.

Body and IL bounds contain work induced by untrusted package or repository
content after the host has acquired the image. They do not claim to bound
image acquisition, metadata table enumeration required to establish exact
scope, or another explicitly co-running Analysis feature. Malformed metadata,
body decode, and resolver failures remain visible typed evidence; no broad
fallback converts them to zero metrics or a successful empty result.

### Production adoption for #8450

The counted implementation path is:

1. Add the validated parameterized request, closed evidence vocabulary,
   versioned `CompleteProfileV1` set, work bounds, and prerequisite plan.
2. Publish charged physical scope/source attribution, body size,
   exception-region evidence, and local-signature evidence without constructing
   the canonical method context.
3. Publish instruction-shape and control-flow evidence from one canonical
   context, then gate call, signal, safety, allocation, and relationship stages
   by their effective causes and publish actual participation.
4. Add producer-owned completion and limitation outcomes where current signal
   or allocation producers cannot prove whether partial evidence is complete.
5. Publish per-body typed outcomes and per-evidence coverage in
   `LibraryImplementationMetricAnalysisResult`.
6. Adapt the complete implementation profile from the focused result and
   migrate `AssemblyContextImplementationProfileFamilyQuery` and
   `AssemblyContextLibraryMetricsQuery`, preserving their existing CLI and
   Inspect Web consumers.
7. Retire the monolithic feature bit after remaining complete-profile callers
   move, then let #8445 adopt the selective result through
   `MemberMetricsInspect`.

Each slice is independently coherent and reaches an existing production
consumer or the next named host-neutral consumer. No unused generic execution
substrate lands ahead of adoption.

## Producer Planning adoption

This owner is the first adopter of [Producer Planning](producer-planning.md),
tracked by [#8568](https://github.com/richlander/dotnet-inspect/issues/8568).
The first slice adopts one producer, unsafe-evidence presence. Every other
producer keeps the fused execution described above until its own slice moves
it.

### Example

Library discovery asks whether System.Text.Json contains any unsafe evidence.
The work description names one producer, unsafe-evidence presence, with one
request: every method definition at declaration depth, plus body depth for
each definition that has a managed IL body, with an Exists terminal. The
reference execution visits definitions in metadata order. When the producer
publishes its first evidence, the Exists terminal is settled, and the
remaining work stops with the stopped outcome. If a definition's analysis is
incomplete before any evidence is found, the producer's outcome is failed with
that definition's diagnostic, and the answer is not reported as absent.

### Adoption decisions

1. **Where the contract lives.** Declarations, planning, work descriptions,
   outcomes, and receipts live in `ILInspector.Analysis` under
   `ILInspector.Analysis.Planning`. They name no Analysis-specific type, so
   they can move below both adopters when Research adopts.
2. **Unit and layers.** The unit is one method definition, in the same order
   as today's probe: type definitions in metadata order, then each type's
   methods. This slice defines two layers: the declaration layer (the
   definition's metadata, signature, and declaring type) and the body layer
   (the managed IL body and its local signature, when one exists). The body
   layer is the borrowed raw body, not decoded instructions, so a producer
   that stops at its first finding does not pay to decode the rest of the
   body. Decoded instructions and control-flow graphs become layers when a
   producer slice first requests them.
3. **Borrowed views.** A producer receives each layer as a snapshot-callback
   borrow through a scoped `readonly ref struct` view, so retaining the view
   is a compile error. A producer that did not request the body layer cannot
   obtain it.
4. **Interim executor.** Until level 2 exists
   ([#8577](https://github.com/richlander/dotnet-inspect/issues/8577)), this
   owner provides the serial reference executor for the method-definition
   source. It implements the reference passes exactly, stops at a settled
   Exists terminal, and records participation. It is not an optimization
   and makes no parallel or collapse claim.
5. **Producer algorithm.** Unsafe-evidence presence keeps the existing probe
   algorithm unchanged: the declaration check, the unsafe local-signature
   check, and the instruction scan with its call probe. It keeps the
   existing presence work budget and its bounds.
6. **Failure.** In metadata order, the first definition whose analysis is
   incomplete before any evidence is found fails the producer with that
   diagnostic. Evidence found first settles the terminal, and later
   definitions are not visited. This matches the retired probe.
7. **Retirement.** `LibraryBodyIndex.HasUnsafeEvidence` and the builder's
   presence loop are removed, and `UnsafeEvidencePresenceQuery` reads the
   producer's result. `DotnetInspector.Queries` then has no dependency on
   `LibraryBodyIndex`.
8. **Coexistence.** The fused execution for all other producers is unchanged
   and shares no mutable state with the planned execution.

### Gates

These gates land with the implementing slice and run in Release:

- **Description without bytes:** planning the presence request opens no
  image.
- **Whole-request rejection:** cycles, missing dependencies, upward-tier
  dependencies, and conflicting parameters are rejected with typed reasons,
  exercised with test declarations.
- **Equivalence:** the existing `UnsafeEvidencePresenceTests` cases produce
  the same answers and failures through the planned execution.
- **Early stop:** the receipt shows no definition visited after the first
  evidence.
- **Failure:** an incomplete definition before any evidence yields the failed
  outcome, not an absent answer.
- **Undeclared layer:** a producer that requested only the declaration layer
  cannot obtain the body layer.

This slice makes no claim about fusing several producers, parallel execution,
or request collapse. Those belong to later producer slices and to level 2.

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
| 6 | Exact-family and Library Metrics profiles, then MemberMetricsInspect | Selective `LibraryImplementationMetricAnalysisResult` plus the temporary complete-profile adapter |

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

The following slice shares that participant-analysis path with
`AssemblyContextCallCensusQuery`. Analysis publishes one generation-bound
graph-wide census over the focused results; Queries projects its exact members,
physical call occurrences, ordering keys, and graph diagnostics onto inert
assembly-context subjects while retaining typed acquisition failures.

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

The selective implementation-metric slice replaces that result's monolithic
producer input without changing its public complete-profile projection.
`AssemblyContextImplementationProfileFamilyQuery` is the compatibility
production witness because both the CLI and Inspect Web already consume its
completed envelope. `AssemblyContextLibraryMetricsQuery` is the full-library
witness: it continues to accept one `LibraryBodyAnalysisExecution` so Research
can compose the adapted profiles with the same execution's call graph for
`Complexity Explorer` and `Relationship Crossing`. `MemberMetricsInspect` is
the first caller that bypasses the complete adapter and proves evidence-level
work selection.

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

The selective demonstration uses the .NET 11 RC1
`System.Text.StringBuilder.AppendFormat` overload family.

The first request asks only for body size:

```text
requested evidence: BodySize
effective evidence: BodySize
participating work:
  physical scope/source metadata attribution
  managed body acquisition
conditional, separately charged work:
  source-attribution body probes
absent work:
  canonical method context
  direct calls and target classification
  allocation signals
  allocation occurrences
  body signals
  safety
  sibling relationships
```

It returns one logical correspondence for each selected overload, every
authenticated physical body and generated-body attribution, encoded IL size
where a managed body is available, and explicit bodyless or failed outcomes.

The decoration request adds exact sibling relationships:

```text
requested evidence: BodySize, SiblingOverloadRelationships
effective evidence: BodySize, DirectCalls,
                    SiblingOverloadRelationships
additional participating work:
  canonical method context
  metric direct-call collection and target classification
  sibling-relationship projection
still absent:
  allocation-signal, body-signal, and safety topic projections
```

The current complete request remains available as `CompleteProfileV1`. It must
produce the same 15 logical overloads, 16 physical profiles, profile values,
relationship rows, order, coverage, and diagnostics as the existing
exact-family result. Measurements compare all three requests over the same
immutable CoreLib image; the compact requests must show less actual producer
participation, and body-size-only must demonstrate lower median latency than
the complete profile. The measurement is reproducible design evidence, not a
fixed cross-machine timing threshold.

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

The selective implementation-metric migration additionally gates:

- request construction rejects empty evidence, unknown evidence, and
  non-positive bounds;
- body-size-only and exception-region-only requests do not construct
  `MethodBodyAnalysisContext`;
- locals-only requests do not decode selected metric bodies or build control
  flow; any owner-required attribution decode is separately charged and
  reported;
- Async-only requests perform source attribution without constructing the
  canonical method context;
- sibling relationships add direct-call evidence and execute one canonical
  context and one metric call traversal per admitted physical body;
- sibling relationships do not create allocation facts or run body-signal or
  safety topic projections;
- allocation-count-only requests do not collect allocation occurrences, while
  allocation-occurrence requests reuse the existing focused allocation result;
- several context-dependent metrics reuse one canonical context per physical
  body;
- generated and async scope expansion remains attributable when the coarse
  `MethodEvidence` feature is absent;
- attribution probes respect their separate body and byte bounds, and
  exhaustion marks physical scope incomplete without claiming closure;
- actual stage participation is recorded from execution and can differ from a
  selected-but-not-started stage;
- co-running feature work records its separate cause rather than appearing as
  metric-required work;
- malformed instruction decoding retains already completed header evidence and
  marks dependent evidence failed;
- an internal recoverable signal or allocation failure publishes a partial
  value with an owner-issued limitation, or remains
  completion-unverified until that owner contract lands;
- body and encoded-IL budget exhaustion retains prior evidence and marks
  omitted bodies explicitly;
- per-evidence coverage distinguishes not requested, bodyless, scope-excluded,
  failed, incomplete, and budget-exhausted outcomes;
- the complete-profile adapter performs no second body or topic-producer pass;
- `CompleteProfileV1` matches every existing profile field, relationship,
  order, coverage row, existing diagnostic, and logical/physical identity;
  new focused limitations do not silently alter compatibility output; and
- CLI and Inspect Web exact-family output plus the Library Metrics
  `Complexity Explorer` and `Relationship Crossing` document remain unchanged
  while their production queries move to the new request.

The compact-path performance probe records attribution probe bodies and bytes,
effective metric bodies, charged metric IL bytes, actual participating stages,
elapsed time, and allocated bytes for body-size-only,
body-size-plus-relationships, and `CompleteProfileV1` over the same
`StringBuilder.AppendFormat` image. CI gates semantic participation and parity;
the timing/allocation comparison remains reproducible non-CI evidence until
measurements justify a stable threshold.

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

All selective implementation-metric properties remain **unverified** in this
design-only slice.

## Non-claims

- No `AnalysisHouse`.
- No dependency-injection framework or service locator.
- No change to factual metric algorithms. Producer completion and limitation
  evidence is an additive owner contract where current partial paths are
  opaque.
- No asynchronous or concurrent producer execution contract beyond existing
  behavior.
- No path identity, file-stability, or persistent-cache claim.
- No requirement to decompose every result-local lookup or projection before
  the first section migrates.
- No redesign of `MethodInstructions`, `BlockGraph`, loop analysis, or the
  canonical `MethodBodyAnalysisContext`. A later split needs its own owner
  change and evidence.
- No promise that instruction shape avoids control-flow construction while the
  canonical context owns both.
- No generic metric algebra, producer dependency graph, type-keyed result bag,
  or reflection-based scheduling.
- No universal complexity, audit-risk, or overload-primary score.
- No change to Reflection or Unsafe Accessor classification semantics.
- No claim that output row limits reduce Analysis work when evidence semantics
  require the complete candidate population.
