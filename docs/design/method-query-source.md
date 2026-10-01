# Method Query Source

## Status

Focused design for
[#8577](https://github.com/richlander/dotnet-inspect/issues/8577), the
Method-definition source beneath
[Assembly Analysis Operation](assembly-analysis-operation.md). It defines one
source owner: the ordered physical MethodDef population, breadth selection,
requested depth, declared expansion, serial reference execution, and exact
source-work receipt.

The current `MethodDefinitionExecution` is a useful reference precursor. It
already visits MethodDefs in metadata order, applies the source gate, acquires
body and module-lookup work on demand, preserves Producer Planning outcomes,
and specializes eligible terminal kernels. It is not the target source:
explicit MethodDef scopes still require callers outside this boundary, body
depth is too coarse, generated-body and referenced-callee work are not
source requests, and its receipt does not prove which physical methods were
examined or acquired.

The first implementation slice routes the Assembly Analysis service through
this owner-issued source for the all-MethodDef unsafe-evidence `Exists`
request. The source owns resource-free planning, exact subject binding, serial
reference execution, source-receipt translation, and detached publication
while delegating producer work to the interim executor. The first-slice
Release gates are named in [Required evidence](#required-evidence). Sparse
breadth, generated-body expansion, body packets, exact source-failure
classification, and collapsed request groups remain **unverified**.

## Demo and pathological case

The production goal is a Member Metrics request for
`System.Text.StringBuilder.AppendFormat` that analyzes its 16 declared
overloads and one generated body without walking all 42,000-plus CoreLib
MethodDefs. The same source serves
`System.Text.Json.JsonDocument.Parse`, whose seven-body scope provides a
smaller real-package case.

The current legacy body service spends most of a scoped request on
whole-assembly identity construction, generated-body discovery,
state-machine mapping, and method-map construction. One body can therefore
cost roughly the same as 16 bodies. This source makes the selected physical
population and every widening relation explicit, so the receipt can prove
that sparse work stayed sparse. Timing remains observational evidence; exact
coverage in the receipt is the correctness gate.

Public API extraction used to find the selected Type is separate
whole-assembly work. Evidence reports it independently rather than crediting
or charging it to the Method source.

## Owner and exact claim

**Method Query Source** owns this exact claim:

> Given one admitted assembly image and one resolved Method-source execution
> group, the source visits exactly the physical MethodDefs selected by each
> request's direct breadth plus its declared expansion, in deterministic
> metadata order; acquires each terminal-demanded depth at most once per
> physical MethodDef in that group; separately accounts any bounded body probes
> required to determine generated breadth; publishes terminal results and
> completion equal to serial reference execution; records the actual
> definition, body, expansion, lookup, and per-request settlement work; and
> retains no decoded per-method working data after the last active visit for
> that method.

This owner defines:

- physical MethodDef identity and metadata order within one admitted module;
- direct breadth selection and the relations that may widen it;
- declaration, body, instruction, control-flow, call, and declared-source
  depth;
- the serial reference traversal and per-method working-data lifetime;
- source completion, source bounds, and exact source-work receipts;
- source-native terminal execution and fused per-request settlement; and
- equivalence obligations for sequential, cooperative, cached, or parallel
  executors.

This owner does not define:

- assembly acquisition, format admission, borrowing, or subject identity;
- producer identity, dependency closure, algorithms, results, failure
  containment, or aggregate participation;
- request-set association, grouping, collapse, or residual execution;
- metadata decoding semantics, method identity spelling, control-flow
  semantics, call resolution semantics, or declared-source authentication;
- Type or Member populations;
- public API extraction, selector resolution, presentation, or host policy;
- caching across operations; or
- a universal Analysis scheduler.

## Normative basis and adjacent owners

| Owner | Imported contract |
| --- | --- |
| [Open and closed queries](open-and-closed-queries.md) | Terminals, unit order, independent settlement, stopping, routing, and reference-equivalent lowering. |
| [Query Space Composition](query-space-composition.md) | Request-set grouping, terminal-specialized source plans, result association, source-native answers, and residuals. |
| [Producer Planning](producer-planning.md) | Closed producer descriptions, dependency-consistent visits and completions, failure containment, kernels, results, and `WorkReceipt`. |
| [Assembly Analysis Operation](assembly-analysis-operation.md) | Exact operation/access binding, stateless execution, detached publication, and preservation of source and producer outcomes. |
| [Source Delegation](source-delegation.md) | Accepted pushdown, completion evidence, no fallback after commitment, and substitution for reference execution. |
| [Session-owned format admission](assembly-inspection-query.md#session-owned-format-admission) | The admitted image and retained metadata reader; this source does not reclassify the format. |
| Metadata, Instructions, Control Flow, and Analysis evidence owners | The meaning and bounded construction of facts exposed at each requested depth. |

This design transfers no source meaning to Assembly Analysis or Producer
Planning. Assembly Analysis composes the source. Producer Planning describes
which producers and terminals consume it. Query Space decides which requests
share one execution group.

## Vocabulary

A **physical method** is one MethodDef row in the admitted module. Its
source-local identity is its MethodDef handle. Detached publications pair that
handle with the operation's exact subject identity; display text and decoded
signatures never establish identity.

A **declared owner** is a source method to which an authenticated generated
physical method is attributed. Attribution does not replace physical identity.
The source still visits and receipts the generated MethodDef that contains the
body.

**Breadth** selects physical methods. It consists of direct breadth and zero or
more declared expansion relations.

**Depth** is the greatest source-owned layer required for one physical method.
A deeper layer includes its prerequisites but does not imply a wider
population.

A **source plan** is resource-free and immutable. It binds resolved breadth,
depth, terminal-specialized producer work, work bounds, and receipt
requirements without opening the subject.

A **source execution group** is one QuerySpace-selected physical traversal
serving one or more source plans over the same owner-issued resource identity.
A singleton group is ordinary execution, not a special path.

A **method packet** is the source-owned, per-physical-method working set at the
deepest demanded depth. It is not a result, cache entry, or retained index.

## Breadth

### Direct breadth

Every request names one direct seed:

| Seed | Meaning | Selection work |
| --- | --- | --- |
| All definitions | Every MethodDef in the admitted module | The MethodDef table range |
| Exact methods | One immutable set of MethodDef handles | Direct handle access; no table scan |
| Exact types | Methods declared by one immutable set of TypeDef handles | Direct TypeDef method ranges |
| Metadata predicate | MethodDefs accepted by one owner-issued source-gate predicate | Declaration reads over the predicate's declared candidate range |

Exact-method and exact-type seeds are already resolved source coordinates.
Their construction may depend on an earlier selector operation, but source
planning neither replays that selector nor infers handles from display text.

A predicate declares both its candidate range and the declaration fields it
reads. `All definitions` with a predicate is not an exact-method request and
must receipt the definitions it examined. A sparse exact-handle request does
not become a whole-table predicate scan for implementation convenience.

An empty direct seed is an exact empty population. It is not a request to widen
to all methods.

### Declared expansion

Expansion is opt-in and typed. No consumer receives additional physical
methods merely because an implementation can discover them.

The initial relations are:

- **Generated execution bodies.** From an authenticated declared source,
  include its async or iterator state-machine execution method and its
  authenticated lifted local-function or lambda bodies. State-machine
  relationships may be available from targeted metadata. Lifted ownership may
  additionally require bounded instruction probes of the selected owner and
  recursively reached candidate bodies. Relationship owners define
  authentication; the Method source owns applying the requested relation to
  breadth and accounting the discovery work.
- **Same-image referenced bodies.** From decoded direct-call operands, include
  resolved same-image MethodDefs to an explicit maximum hop count and method
  count. This relation is absent unless requested. Token resolution alone does
  not widen breadth.

Generated-body expansion settles before terminal traversal so the complete
first wave can be ordered by MethodDef row. It has two discovery paths:

1. apply targeted metadata relationships, including authenticated
   state-machine execution methods; and
2. when lifted ownership requires body evidence, probe the selected owner
   bodies, follow only the call and function-pointer references that the
   relationship owner admits, and recursively probe reached candidates until
   the authenticated closure or a declared bound settles.

A generated-discovery probe is source work, not terminal producer execution.
It acquires the body and decodes only the instruction operands required by the
relationship owner. It publishes no producer fact, terminal result, canonical
analysis context, or row projection. Probe body count, IL bytes, relationship
nodes, failures, and resulting expansion origins are separately bounded and
receipted.

The source may reuse a probe's body or decoded instructions for terminal work
only when doing so preserves the packet-lifetime bound. The serial reference
does not retain every probed body until metadata-order traversal merely to
avoid a second read. A later terminal acquisition of the same MethodDef is
therefore distinct, visible work; the once-per-depth rule applies to terminal
packets, while the receipt reports expansion probes separately.

An implementation that scans every MethodDef to expand an exact-method seed
must receipt that whole-table work and does not satisfy the sparse-breadth
proportionality gate.

Referenced-body expansion is discovery-dependent. Serial reference execution
uses waves:

1. visit the current wave in metadata order;
2. collect newly resolved same-image MethodDefs without revisiting an existing
   physical method;
3. order the next wave by MethodDef row; and
4. stop at the declared hop or method bound.

One physical method appears once, at its earliest wave. Cycles do not create
new units. A bound reached before closure is typed incomplete source
completion, never successful exhaustion.

### Order and duplicate policy

Within one wave, physical methods are visited in ascending MethodDef row order.
Direct and generated populations form wave zero. A MethodDef reached through
several seeds or expansion relations remains one physical unit, while its
receipt retains every origin needed to explain why it was included.

Terminal position is defined by this order. `Exists` and `Head` therefore name
the same first matching unit regardless of request grouping or executor.

## Depth

Depth is expressed as source capabilities rather than one broad `Body` bit:

| Layer | Source-owned acquisition | Does not define |
| --- | --- | --- |
| Declaration | MethodDef, declaring TypeDef, attributes, flags, name comparison, and bounded signature-shape access | Display identity or body content |
| Identity | Detached inert method identity and anchor fields | Consumer ordering or presentation |
| Body | Managed body header, IL bytes, exception regions, and local-signature handle | Instruction meaning |
| Instructions | One bounded instruction decode | Analysis of those instructions |
| Control flow | The owner-issued CFG over the decoded instructions | Analysis-specific graph interpretation |
| Calls | Owner-issued classification and bounded resolution of call operands required by the request | Call-graph traversal or presentation |
| Declared source | Authenticated attribution from a generated physical body to its declared owner | Replacing the physical method's identity |

Each layer includes only its prerequisites. For example, `Calls` requires
`Instructions` and the declared shared lookup support, but it does not require
`Control flow` unless the request asks for both. `Identity` is projection work
and remains absent from Count or Exists plans that do not need it.

Shared lookup support, such as same-image token resolution or authenticated
state-machine relationships, is execution-scoped work rather than a fictitious
layer repeated for every method. A source plan declares the support it needs,
and the receipt records its actual construction and use separately.
Body-dependent lifted discovery is likewise source support, but it has its own
probe-body and probe-instruction accounting rather than being hidden inside
Body or Instructions terminal demand.

The current `MethodDefinitionLayers` maps into this vocabulary during
migration:

- its declaration facets remain Declaration demand;
- `IdentityText` becomes Identity;
- `Body` becomes Body or Instructions according to the producer's actual read;
  and
- `ModuleLookup` becomes declared shared lookup support.

No migration is complete while a producer declares a deeper layer than it
reads solely because the transitional bit is coarse.

## Planning

Planning is resource-free. It validates:

- every producer is valid for the Method source;
- direct breadth and expansion parameters are typed and bounded;
- each producer's declared depth covers every source view it can read;
- terminal-specific projection remains absent when the terminal does not need
  it;
- source-gate predicates declare only fields available before their decision;
- body-dependent generated expansion declares probe-body, IL-byte, and
  relationship-node bounds;
- referenced-body expansion declares hop and method bounds; and
- the closed Producer Planning description and source request have one exact
  association.

Planning does not enumerate metadata, authenticate a generated relationship,
decode a body, resolve a token, or estimate the result from a path.

An invariant owner-issued request may retain and reuse its complete source plan.
A changed breadth, expansion, depth, terminal, producer parameter, or request
set is planned anew. Plan reuse transfers no subject authority or result.

## Serial reference execution

The normative executor is deterministic and sequential:

1. Bind the exact source plan to the operation's admitted image access.
2. Resolve direct breadth and complete generated expansion through targeted
   metadata and, when required by the relationship owner, bounded
   body-instruction probes.
3. Create the metadata-ordered first wave.
4. Before each unit, remove requests and producers whose terminals have
   settled or whose prerequisites failed.
5. Acquire the current physical method only to the deepest layer still
   demanded by an active consumer.
6. Route one scoped method packet to active producers in
   dependency-consistent order. Several producers may read the same acquired
   layer; the source constructs it once.
7. Record each producer's participation through Producer Planning and each
   request's settlement through Query Space.
8. After the last active visit for the unit, release its body, decoded
   instructions, CFG, call classification, and other packet-local structures.
   Producer-owned facts retained by a closed work description are separate
   from the packet.
9. Complete eligible producers after their inputs complete.
10. Form the next referenced-body wave, if requested.
11. Stop before the next unit when every served request has settled; otherwise
    exhaust the declared breadth or publish typed incomplete completion.

Bodyless methods are physical methods. Declaration-only requests may consume
them. A request for a body layer records body absence as a source fact; it does
not fabricate an empty body or fail merely because the MethodDef is abstract,
runtime-implemented, P/Invoke, or otherwise bodiless.

## Fused traversal and terminal specialization

Query Space may place compatible requests in one execution group. The source
then acquires the union of their active depth for the current physical method
and routes the packet without merging the requests' meanings.

- `Exists` stops receiving work after its first selected match.
- `Count` keeps only cardinality unless its own predicate or residual requires
  other values.
- `Rows` performs its declared projection and publishes rows.
- A settled request keeps its result if later work for another request fails.
- Shared source failure affects every unsettled request that required the
  failed work.

A singleton uses the same terminal-specialized plan it would use outside a
request set. Fusing Rows with Exists does not charge Exists for identity
projection or units after settlement. The source receipt records shared
physical acquisition once and per-request settlement separately; Producer
Planning retains per-producer participation.

## Completion, failure, bounds, and cancellation

Source completion is one of:

- **Exhausted:** every unit in the declared breadth and expansion completed;
- **Satisfied:** the request's terminal settled before exhaustion;
- **Producer failed:** producer failure prevented that request from settling;
- **Source incomplete:** malformed or unavailable required source data, or a
  non-critical source bound, prevented exact completion;
- **Aborted:** a critical containment bound stopped the complete execution
  group; or
- **Cancelled:** the owning operation observed cancellation at a unit
  boundary before starting the next unit.

Malformed metadata, body, instruction, relationship, or lookup work remains
visible at the layer that required it. A request that did not demand the layer
does not fail because another request read it, unless both committed to one
covering source operation whose source failure makes their required
completion unavailable.

Producer failures follow Producer Planning containment. Source failures are
not producer failures and are never converted to an empty Rows result, zero
Count, false Exists, exhausted source, or planner decline.

Bounds are part of the source plan. At minimum they cover candidate
definitions examined, physical methods selected, generated expansions,
generated-discovery probe bodies and IL bytes, referenced-callee hops and
methods, terminal body bytes, instructions, CFG nodes and edges, and other
source-support work. A critical safety bound aborts under Producer Planning's
critical-failure contract. A consumer-selected completeness bound publishes
source incomplete.

Cancellation is optional operation policy. It is observed before source
binding and at physical-unit boundaries, never inside a producer visit or
decoder. A settled terminal remains settled; unsettled requests publish
cancelled completion.

## Exact source-work receipt

One detached `MethodSourceReceipt` records actual source work, not requested
work or a cost estimate. It contains:

- the exact request or execution-group association and subject identity;
- direct breadth, declared expansion, terminal, and demanded depth;
- completion for every served request and its settlement position, when any;
- exact MethodDef coverage for definitions examined, physical methods
  selected, generated-discovery bodies probed, terminal bodies acquired, and
  each deeper terminal layer acquired;
- generated and referenced expansion origins;
- generated-discovery probe bytes and relationship work;
- shared lookup-support construction and use;
- source bounds approached or reached; and
- executor identity.

Coverage uses MethodDef identity, not display text. Its representation may
compact contiguous MethodDef row ranges and sparse sorted runs, but membership
is exact. `All MethodDefs 1..N` is exact only with exhausted table traversal;
a stopped request carries the visited prefix or sparse coverage actually read.

The receipt distinguishes:

- a definition examined by a predicate from a physical method selected;
- a selected bodyless method from a body acquired;
- a body probed to authenticate generated breadth from a body acquired for
  terminal producer work;
- generated breadth from referenced-callee breadth;
- body acquisition from instruction, CFG, call, and attribution work; and
- shared source work from per-producer participation.

`WorkReceipt` remains Producer Planning's aggregate. It cannot substitute for
the source receipt, and the source receipt does not reinterpret producer
outcomes.

## Executor substitutions

The serial executor is available on every supported host, including
single-threaded Browser/Wasm. Cooperative, cached, precomputed, or parallel
executors are optional substitutions.

Every substitution must publish:

- the same physical population and unit order;
- the same terminal value, named unit, outcome, and completion;
- the same producer results under dependency and failure rules;
- truthful actual-work coverage and bounds; and
- no packet-local authority in detached results.

A parallel executor cannot publish whichever match finishes first. It commits
results in reference order. Speculative acquisition is actual work and must be
receipted; an executor that reads past the reference settlement point does not
satisfy the early-stop work property. Concurrency, incremental publication,
and cross-operation caches require their own scheduling, close, and lifetime
designs before adoption.

## Migration and production adoption

Migration is incremental:

1. Introduce the owner-issued source plan, execution, and receipt behind
   `AssemblyAnalysisService`. This slice is implemented.
2. Move unsafe-evidence presence from direct
   `MethodDefinitionExecution.Execute` to the source without changing its
   all-definitions `Exists` result. This slice is implemented.
3. Add exact-method and exact-type breadth with exact coverage receipts.
4. Move one sparse production body producer and its real CLI consumer,
   preserving its focused result rather than filtering a legacy aggregate.
5. Add authenticated generated-body expansion for that consumer.
6. Add referenced-body expansion only with a consumer that requires it.
7. Let the host-neutral request-set planner from #8574 group compatible
   requests, then adopt one CLI and one Browser/Wasm operation.
8. Move remaining producers and delete each superseded legacy scan and index
   when its final consumer moves.

Wrapping `LibraryBodyAnalysisBuilder.Build`, constructing every legacy result
and filtering afterward, or scanning every MethodDef to realize an exact
MethodDef seed is not adoption.

## Required evidence

The first implementation slice is gated in Release:

- `MethodQuerySource_PlanningDoesNotReadSubject`
- `MethodQuerySource_BindsExactPlanSubjectAndReceipt`
- `MethodQuerySource_SequentialReferenceMatchesInterimExecutor`
- `MethodQuerySource_ExistsStopsAtFirstSettledMethod`
- `MethodQuerySource_ProducerFailureDoesNotBecomeSuccessfulAbsence`
- `MethodQuerySource_ReleasedExecutionRetainsNoSubjectAuthority`

The following deeper-source gates remain **unverified**:

- `MethodQuerySource_ExactMethodBreadthVisitsOnlySelectedMethods`
- `MethodQuerySource_ExactTypeBreadthVisitsOnlyDeclaredMethods`
- `MethodQuerySource_GeneratedExpansionVisitsOnlyAuthenticatedBodies`
- `MethodQuerySource_GeneratedExpansionAccountsBodyDependentDiscovery`
- `MethodQuerySource_ReferencedExpansionIsBoundedDeduplicatedAndOrdered`
- `MethodQuerySource_DeepestTerminalLayerIsAcquiredOncePerMethod`
- `MethodQuerySource_ReceiptSeparatesExaminedSelectedAndAcquiredWork`
- `MethodQuerySource_SourceFailureDoesNotBecomeSuccessfulAbsence`

The sparse-breadth pathological gate uses the pinned System.Text.Json asset.
It compares one exact MethodDef, the seven `JsonDocument.Parse` MethodDefs,
and a wider Type scope. Receipt coverage must contain only direct methods,
authenticated generated bodies, and explicitly requested referenced bodies.
It must not contain unrelated MethodDefs merely because they share the
assembly.

`StringBuilder.AppendFormat` on the platform CoreLib supplies separate
NativeAOT evidence. For one body, 16 overloads plus declared generated bodies,
and the complete Type, report:

- source planning and first-use time;
- definitions examined and selected;
- generated and referenced expansions;
- generated-discovery probe bodies and IL bytes;
- terminal bodies, instructions, CFGs, calls, and attribution layers acquired;
- allocations and peak managed memory when available; and
- public API extraction separately from Method-source execution.

Every modernization slice reports exact NativeAOT base/head evidence for each
affected terminal. Exists reports settlement position and avoided later work.
Count reports avoided row projection and publication. Rows reports projection
and retained result work. Fewer traversals alone is not a performance result.

The per-method working-data lifetime is enforced by construction: packet views
are scoped, detached results contain no packet or source authority, and only
producer-declared facts may be retained. The Release detachment gate consumes
every published surface after source and assembly access are disposed. This is
partial behavioral coverage, not recursive proof over private object graphs.

## Platform and model assessment

The reference path is sequential, SRM-only, NativeAOT-friendly, Roslyn-free,
and compatible with single-threaded Browser/Wasm. It never loads inspected
assemblies.

Resource-free planning and serial execution are deterministic ordinary code.
The routing and settlement transition system remains owned and modeled by
[Open and closed queries](open-and-closed-queries.md). This design adds no
independent concurrent state machine. A future parallel, incremental, or
retained executor must model its scheduling and close interaction before
implementation.

## Non-claims

- No random-access PE transport requirement. Sparse metadata and body reads are
  a follow-on optimization; the first source may use the admitted immutable
  image while avoiding whole-image semantic walks.
- No claim that selector or public API work is sparse.
- No implicit generated-body or referenced-callee expansion.
- No whole call graph, recursive decompilation, or interprocedural fixed point.
- No cross-module body traversal.
- No universal Type, Member, or assembly row vocabulary.
- No Analysis-owned request collapse or terminal derivation.
- No producer-local cancellation.
- No required parallel executor.
- No cross-operation result, packet, reader, or source-plan cache.
- No compatibility guarantee for the legacy Library Body Analysis aggregate.
