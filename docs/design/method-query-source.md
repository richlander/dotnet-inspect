# Method Query Source

## Status

Focused design for
[#8577](https://github.com/richlander/dotnet-inspect/issues/8577), the
Method-definition source beneath
[Assembly Analysis Operation](assembly-analysis-operation.md). It defines one
source owner: the ordered physical MethodDef population, breadth selection,
requested depth, declared expansion, serial reference execution, and exact
source-work receipt.

The current source delegates producer work to
`MethodDefinitionExecution`. It supports all-MethodDef, exact-MethodDef, and
exact-TypeDef breadth in metadata order, applies the source gate, acquires body
and module-lookup work on demand, preserves Producer Planning outcomes, and
specializes eligible all-definition terminal kernels. Exact seeds are
normalized without subject access. Their execution uses direct MethodDef
access or direct TypeDef method ranges rather than a table scan.

The implemented slices route the Assembly Analysis service through this
owner-issued source for unsafe-evidence requests and publish compact exact
MethodDef coverage for definitions examined, physical methods selected, and
terminal bodies attempted and acquired, plus module-lookup use. Type
`Unsafe Members` effective discovery is the first sparse production consumer:
it closes the unsafe-evidence producer with `Exists` over the selected TypeDef
instead of rendering the ordinary whole-scope section. The source owns
resource-free planning, exact subject binding, serial reference execution,
source-receipt translation, and detached publication.

Exact-member `Calls --count` is the first body-producing CLI consumer. Direct
invocation Count and Calls-row Count each use one Body-only producer over exact
MethodDef breadth plus authenticated generated execution bodies. Their scalar
path decodes call opcodes without module lookup, target resolution, signature
enrichment, canonical method context, or `DirectCall` row construction. The
two superseded `LibraryBodyAnalysis` Count request branches are deleted.

Compatible all-definition request sets now partition into physical
instruction-source cost classes, then execute one MethodDef traversal per
class with independent terminal-specialized lanes. Forward-only shallow lanes
share one no-retention stream; retained or selective-detail lanes share one
lazy retained sequence. Each lane retains its own gate, optional module lookup,
traversal ordinal, Producer Planning work, completion, exact source coverage,
and terminal body and encoded-IL bounds.
Bound exhaustion is source-incomplete for that lane and its receipt preserves
the admitted work; another shared lane continues under its own limits. A
settled lane keeps its result when a later lane aborts or encounters source
failure, and source failure reaches only active lanes whose type scope required
the failed enumeration. Sparse breadth and incompatible requests remain
separate groups. Exact-method and exact-type requests may opt into bounded
generated execution-body expansion, and an all-definition request may declare
the [declared-source relation](#declared-source-relation), which settles at
source completion in its own lane. The source uses targeted nested metadata
for state-machine bodies and the existing lifted ownership authority for
bounded body-dependent discovery; its receipt separates candidate definitions,
probe bodies and bytes, relationship nodes, and
authenticated origins from terminal work. Declaring producers read the
subject's public MethodDef roots and reference-assembly status at completion
through [image exposure support](#image-exposure-support), receipted apart from
breadth. Metadata-predicate breadth, body
packets, and collapsed sparse request groups remain **unverified**.

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

## Name and scope

The source is named for methods, not members, because only methods have the
two properties it is built on: a physical row identity and a body.

- A MethodDef row is the unit of identity, order, and receipt. Per-method
  depth hangs off one MethodDef. Shared lookup support, such as call targets,
  is execution-scoped and receipted per group and request, with the
  MethodDefs that queried it.
- Properties and events have no IL of their own. Their accessors are
  MethodDefs, which this source already visits. The source can supply the
  accessor association; reporting an accessor's facts on its property is
  attribution, owned by the consumer.
- Fields have no body. Field facts come from metadata rows and, for
  initialized data, PE section data. That is a different population with a
  different cost model.
- For this source, types are breadth, not population. Exact-TypeDef breadth
  selects that type's methods.

"Member" is the product vocabulary. Unsafe Members, Member Metrics, and
member-scoped commands attribute method facts back to members. A `Member`
prefix here would claim coverage this source does not have. It would also
take the name a future source that does enumerate members would need. As
[`AssemblyAnalysisSource`](assembly-analysis-operation.md#assemblyanalysissource)
states, Type and Member sources belong to their own owners.

The code uses the ECMA-335 and `System.Reflection.Metadata` name for the same
population, `MethodDefinition`. The implementation is mainly in
`src/ILInspector.Analysis/Planning/`:

| Code | Role |
| --- | --- |
| `MethodDefinitionSource*` types in `MethodQuerySource.cs` | Breadth, requests, request-set plans, coverage, and receipts |
| `MethodQuerySource` (internal) in `MethodQuerySource.cs` | Entry point |
| `MethodDefinitionExecution.cs` | Traversal and on-demand layer acquisition |
| `MethodDefinitionLayers` and `MethodDefinitionView` in `MethodDefinitionProducers.cs` | Declared layers and the per-method view producers read through |
| `MethodDefinitionGeneratedExpansion.cs` | Generated-expansion bounds and coverage; discovery and authentication delegate to the relationship owners' resolvers |
| `MethodRowGate.cs`, `MethodDefinitionPredicates.cs`, and `MethodDefinitionTerminalWorkBudget.cs` | Source gate, predicates, and terminal work bounds |
| `MemorySafetyContractSupport.cs` | Memory-safety contract and accessor association support |
| `SameModuleCallTargetSupport.cs` | `CallTargets` support |
| `MethodCallMetadataSupport.cs` | `Calls`, `LocalSignature`, and field-operand support |
| `MethodAnalysisSupport.cs` | Allocation and optimization's shared reader-backed judgments and support-work receipt |

Where this source sits among Query Space, Producer Planning, and Assembly
Analysis Operation is described in
[Analysis stack](assembly-analysis-operation.md#analysis-stack).

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

One source request names exactly one requested producer. Its closed Producer
Planning description also carries that producer's dependency closure, so
dependency-consistent visits, completion, failure containment, and
`WorkReceipt` remain intact. Another independently requested producer belongs
to another request-set association rather than hitchhiking in that request.

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
  include its async or iterator state-machine methods in the requested
  [roles](#declared-source-relation), always including the execution method,
  and its authenticated lifted local-function or lambda bodies. State-machine
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

Generated-body discovery runs on `DeclaredSourceAttribution`, which composes
`DeclaredSourceResolver`, `StateMachineSourceResolver`, and
`LiftedSourceOwnerResolver`, with the metadata resolvers and classifiers they
read, from the subject's metadata and PE readers alone. The three resolvers
reference no Library Body Analysis plan, result, or runner; Library Body
Analysis applies them to its plans through
`LibraryBodyDeclaredSourcePublication`, which is deleted with it. The metadata
resolvers and classifiers still carry `LibraryBody` names and move under
[#9959](https://github.com/richlander/dotnet-inspect/issues/9959). The source constructs it only when a request declares
expansion and builds no `LibraryBodyAnalysisBuilder` for it. When a producer
also declares the module lookup, that lookup's builder is given the same
attribution, so expansion and lookup share one set of resolver caches. Library
Body Analysis composes its own attribution the same way, so authentication has
one implementation.

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

### Declared-source relation

Status: exact breadth implements roles, nested-type reach, unauthenticated
candidates, and retained-prefix bounds. All-definition breadth implements the
reverse lowering described below, with its own operand scan of each acquired
body.

- `IncludeGeneratedExecutionBodies` takes the requested
  `MethodDefinitionGeneratedBodyRoles`, defaulting to Execution, and
  `includeUnauthenticatedCandidates`.
- `MethodDefinitionGeneratedExpansionCoverage` publishes each outcome:
  `Origins` in the requested roles, `AttributedOutsideRoles`, and
  `UnauthenticatedCandidates`.
- A nested type counts as compiler-generated when it carries
  `CompilerGeneratedAttribute`.
- A generated-named lifted body declared directly in breadth that no owner
  reaches is also an unauthenticated candidate.
- A visiting producer reads its unit's outcome through
  `MethodDefinitionView.DeclaredSource`. The outcome is `Generated` with the
  unit's origin, `Unauthenticated`, or `Direct` when the unit entered breadth
  directly and the relation published no generated outcome for it. A body
  that origins name with two different owners is `Unauthenticated`, because
  neither owner is unique. Reading an outcome does no source work; the
  relation's work and outcomes stay in the expansion receipt. A breadth that
  declares no expansion carries no relation, and reading it throws
  `ProducerContractException`.
- Over all-definition breadth, `IncludeGeneratedExecutionBodies` declares the
  relation and leaves breadth unchanged. It takes no unauthenticated
  candidates, because every body is already visited. Its default bounds,
  `MethodDefinitionGeneratedExpansionLimits.AllDefinitions`, are sized to the
  metadata tables. A producer reads each unit's outcome at completion through
  `MethodDefinitionCompletionView.DeclaredSourceOf`; reading it during a visit
  throws `ProducerContractException`.
- A request that declares the all-definition relation keeps its own lane,
  because a shared traversal carries no relation. Its module lookup composes
  its own attribution, so no unit visit charges the pending relation's work.

A reached bound keeps the retained prefix, as
[Completion, failure, bounds, and cancellation](#completion-failure-bounds-and-cancellation)
describes.

The declared-source relation maps declared members and the physical bodies
that implement them, in both directions. Exact breadth asks the forward
question: which bodies implement this member? All-definition breadth asks the
reverse question: which declared member does this body belong to? The answers
are one relation, and the attribution owners define what authenticates each
edge. This section owns how the source applies it to breadth, what each
direction costs, and what it publishes.

An edge has a kind, which fixes the depth needed to authenticate it:

| Edge | Authenticated by | Depth |
| --- | --- | --- |
| State machine | The source's state-machine attribute names a type that the targeted claim check makes unique to that source | Declaration only |
| Lifted | An owner body, or a body that owner owns, references the lifted method through `ldftn`, `ldvirtftn`, `call`, or `callvirt` | Selected call and function-pointer operands |

A request that needs only state-machine edges does not pay for lifted
probes. A compiler-generated name may select the candidate group a reference
is checked against, but a name alone never authenticates either edge.

Every method declared on an authenticated state-machine type belongs to the
declared source and carries one role, decided from metadata alone:

| Role | Members | Example |
| --- | --- | --- |
| Execution | The resolved `MoveNext` | `<Foo>d__3.MoveNext` |
| Scaffolding | Instance constructors, and every other body of a MethodImpl row on that type | `GetEnumerator`, `get_Current`, `Dispose`, `Reset`, `SetStateMachine` |
| User helper | Every other method on that type | `<>m__Finally1`, which holds an iterator's `finally` code |

Lifted local-function and lambda bodies have the Execution role. Roles do not
change ownership. They let a consumer choose breadth: generated-body expansion
names the roles it includes. Execution is always included. A consumer that
measures written code can include user helpers and leave out scaffolding;
whether member call counts do so is that consumer's own change. A consumer
that claims completeness over everything
compiled for a member, such as the unsafe member census, includes every
role. A method of an authenticated type outside the requested roles is still
attributed, so it is neither unexamined nor unauthenticated.

For each generated-shaped MethodDef in breadth, the relation publishes one
outcome:

- an origin `(Method, DeclaredOwner, Kind, Role)`, where `DeclaredOwner` is
  the ultimate declared member, so a lambda inside an iterator's lambda is
  owned by the iterator method; or
- a typed unauthenticated outcome naming that body.

Over exact-type breadth, the relation also covers every method of a
compiler-generated type reached from a selected type through a chain of
compiler-generated nested types, such as an async lambda's state machine
nested in a display class. The chain stops at the first nested type that is
not compiler-generated, because that type's members own their own generated
bodies, and a request that wants them selects that type. Each such method is
an origin in a requested role, an attributed method outside those roles, or
an unauthenticated candidate. Reaching those nested types is targeted
NestedClass work, bounded by the relationship-node limit and receipted with
the expansion. Closure display-class constructors and the static-lambda cache
type `<>c` are typical unauthenticated candidates, since no edge kind names
their owner. Expansion may also request its unauthenticated candidates. They
then join breadth and are visited without an owner, as all-definition breadth
visits every body, so a consumer can show that they lack evidence instead of
treating them as unexamined.

The two directions are lowered differently, and neither lowering widens the
other's cost class:

- **Exact breadth: sparse forward walk.** The relation settles before terminal
  traversal from targeted metadata and bounded probes of the selected owners
  and the bodies they reach, as the [expansion](#declared-expansion) does
  today. The source builds no module-wide index for an exact request.
- **All-definition breadth: reverse lowering fused into traversal.** Every
  body is already in breadth, so the relation adds and removes no breadth.
  State-machine edges come from one declaration-depth scan of state-machine
  attributes. Lifted edges come from the call and function-pointer operands
  recorded during the same forward traversal, and the owner closure settles
  at source completion. The settled closure is the sparse forward walk from
  every user type's methods, where a user type is neither compiler-generated
  nor nested in one, reading the recorded operands instead of probing bodies.
  A body a producer already acquired is not read again. A body no producer
  acquired, as when the producers need no instructions, is read by the
  relation itself. The receipt records both as relation work:
  `TraversalReferenceBodies`, `TraversalReferenceBodiesAcquired`, and
  `TraversalReferenceEncodedIlBytes`. Settling probes only bodies traversal
  did not record, such as those after an early stop. The relation still scans
  operands itself. Sharing a producer's instruction decode, so that a
  producer that already decodes instructions adds no second operand pass, is
  **unverified**.

Over all-definition breadth the relation is therefore a source-completion
fact. A producer reads it in `Complete`, not during a unit visit. A producer
whose visits stop early at a settled terminal still reads the whole-module
relation: settling probes the bodies traversal left unvisited, and a bound
or failure at settle reaches that producer too, so it completes
`SourceBounded` or fails with the source. Over exact breadth the relation is
also available during the visit.

Both lowerings publish the same origins for the same declared owners. That
equivalence is a required gate, the first gate of
[#9864](https://github.com/richlander/dotnet-inspect/issues/9864):
`AllDefinitionRelationEqualsTheExactTypeUnion` compares the all-definition
origins, attributed methods outside the requested roles, and unauthenticated
candidates with the union of the exact-type expansions over every user type.
It runs over the unsafe fixtures, the Analysis test assembly, the iterator
fixture, and System.Text.Json. `AllDefinitionRelationReadsTraversalOperandsWithoutProbes`,
`AllDefinitionRelationScansTheBodyAProducerAcquired`,
`AllDefinitionRelationIsNotReadableDuringAVisit`,
`AllDefinitionRelationBoundReachesAProducerThatStoppedEarly`,
`AllDefinitionRelationDeclaresNoCandidatesAndModuleBounds`, and
`Plan_KeepsAllDefinitionRelationRequestsInTheirOwnLanes` gate the receipts,
the completion-only read, a bound reaching an early-stopped producer, the
declaration, and the lane rule. A host that retains a session may keep the
reverse relation as a typed prepared resource only when measured repeat
requests amortize its preparation.

A reached relation bound publishes typed source incompleteness with the
retained prefix of breadth and origins, rather than failing the source with
empty breadth. Each consumer decides what that prefix proves. For example,
unsafe evidence presence keeps its answer semantics and visible failures,
while the census keeps the prefix's findings and reports the census
incomplete.

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
| Semantic identity | The detached structural identity of the MethodDef, decoded metadata rather than text | Its caller-unsafe contract, display spelling, or cross-module identity |
| Body | Managed body header, IL bytes, exception regions, and local-signature handle | Instruction meaning |
| Instructions | One bounded instruction decode | Analysis of those instructions |
| Control flow | The owner-issued CFG over the decoded instructions | Analysis-specific graph interpretation |
| Local signature | Owner-issued local-variable types, declared count, and typed incomplete reason | Call operands or call targets |
| Calls | Owner-issued classification and bounded resolution of call operands required by the request | Call-graph traversal or presentation |
| Declared source | Authenticated attribution from a generated physical body to its declared owner | Replacing the physical method's identity |

Each layer includes only its prerequisites. For example, `Calls` requires
`Instructions` and the declared shared lookup support, but it does not require
`Control flow` unless the request asks for both. `Identity` is projection work
and remains absent from Count or Exists plans that do not need it.

Instruction demand has two independent source-owned facets:

| Facet | Ordered values | Meaning |
| --- | --- | --- |
| Access | `ForwardOnly < RetainedPrefix` | Whether all consumers can observe one advancing stream, or any consumer needs replay, independent progress, or IL-offset lookup. |
| Detail | `OpcodeAndExtent < SelectiveOperands` | Whether consumers need only shallow instruction identity, or may resolve selected operands and branch targets. |

A producer declares its minimum semantic demand, never a physical reader type.
The request-set planner first partitions requirements by selected physical
instruction-source kind. Within one execution group, the source joins Access
and Detail independently across active producers. A no-retention stream
currently satisfies `ForwardOnly + OpcodeAndExtent`; retained-prefix demand
selects one lazy shallow retained sequence, while selective-detail demand
selects its resolved-detail mode. A retained consumer therefore cannot promote
a forward-only lane into the retained cost class. Adding a producer can only
preserve or increase the demand within its compatible group, and producer
order cannot change the selected source.

The Method-body demand scorecard carries the reference implementation and real
classifier gates for this rule. Production declarations and plans now use the
same Access and Detail vocabulary rather than copying the scorecard planner.
[Query Space Producer Capabilities](query-space-producer-capabilities.md) owns
the reusable requirement, provision, result-coverage, and producer-plan
contract shared with Type and Member declaration planning. This Method owner
retains the Access and Detail meanings, their pointwise join, and physical
source choices.

Method request-set planning preserves each lane's minimum semantic demand,
separates lanes whose plans select different physical source kinds, and forms a
joined plan within each resulting group. Compatible
`ForwardOnly + OpcodeAndExtent` requests use one fused no-retention stream.
Compatible retained-prefix or selective-detail requests use one packet-local
`InstructionSequence`; independent cursors share its scan frontier. A joined
`SelectiveOperands` group selects resolved-detail retention before any lane
runs, so producer order cannot cause an earlier shallow lane to force a second
decode. A producer with that declared demand may explicitly materialize the
completed sequence as canonical `MethodInstructions`; no other retained lane
implicitly completes the source. Source
receipts distinguish no-retention sources, retained sources, and instruction
participation. A request receipt records a retained source opening only for the
lane that actually created that packet-local sequence; the physical group
receipt records the shared retained frontier actually scanned. The exact-member
call-count producer is the first production adopter.

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

### Reference binding

`ReferenceBinding` is a declared layer, beside `ModuleLookup`, for producers
that resolve declarations in assemblies the subject references. It implies
`ModuleLookup`. The source hands the lookup the owner-issued reference-binding
access defined by
[Assembly Analysis Operation](assembly-analysis-operation.md#reference-binding-access).
The source does not choose a policy, acquire an assembly, or fall back to
same-assembly resolution. A plan whose producer declares the layer is rejected
by the operation when the access lacks it. A request-set operation carries the
same access to every lane, shared or single, so grouped lanes that declare the
layer share one binding. Unresolved, ambiguous, or unreadable
references are per-body producer diagnostics, never negative findings.
Referenced-body traversal remains outside this layer; it resolves declarations
only.

### Memory-safety contract support

`MemorySafetyContracts` is declared shared lookup support for producers that
need the caller contract of a same-image member, tracked by
[#9831](https://github.com/richlander/dotnet-inspect/issues/9831). It does not
imply `ModuleLookup`. It builds no `LibraryBodyAnalysisBuilder`, acquires no
body, and does not widen breadth. A producer that did not declare it cannot
obtain it.

The execution constructs one `MemorySafetyContractLookup` over the subject's
metadata reader at the first access by a declaring producer. Lanes of one
shared execution group use the same lookup. The lookup is a thin projection of
`MemorySafetyMetadataIndex`. Contract semantics, integrity validation, and the
budgets in `MetadataSafetyPolicy` stay with that index. The lookup adds no
correctness logic.

The lookup publishes:

- module rules, with their typed availability or failure;
- the direct or associated contract of a same-image MethodDef, FieldDef,
  PropertyDef, or EventDef, as the index's `GetMemberContract` decides it; and
- for a MethodDef, its accessor association. The association is none, the
  PropertyDef or EventDef with the accessor's role, ambiguous, or unavailable
  with the index's association failure.

Cross-assembly members are outside this support. Call-target identity comes
from its owners, #8700 and #8945; this support resolves no operand.

The source receipt carries `MemorySafetyContractCoverage`. Construction work is
the CustomAttribute ordering scan, the TypeDef, MethodDef, NestedClass,
Property, and Event integrity scan, the Property, Event, and MethodSemantics
association projection, and the module CustomAttribute rows. Query work is the
member CustomAttribute rows read. Both report the name characters admitted by
the name budget and the signature bytes of enumerated decode steps. Each step
adds the full length of its blob each time it runs: a member signature
pointer decode and every TypeSpec it reaches, an accessor-shape signature
read, and, for each attribute row whose type matched, its constructor
signature before validation and then its value blob. Attribute type-name
resolution, including a generic attribute's TypeSpec parent, is reported only
as name characters, which the name budget bounds. Coverage also records the MethodDefs whose visits queried
the lookup and the number of queries.

The physical group receipt records construction once and all query work. A
request receipt records construction only for the lane whose access created
the lookup, and only that lane's queries. Construction is absent when no
producer declares the support or no declaring producer queries it.

### Same-module call-target support

`CallTargets` is declared shared lookup support for producers that need the
typed target of a call operand, tracked by
[#8700](https://github.com/richlander/dotnet-inspect/issues/8700). It does not
imply `ModuleLookup`. It builds no `LibraryBodyAnalysisBuilder`, acquires no
body, and does not widen breadth. A producer that did not declare it cannot
obtain it.

The target is the one
[Direct-call target resolution](library-body-analysis-service.md#direct-call-target-resolution)
defines: a current-module MethodDef, an external declaring type's exact
reference origin, a runtime-provided array member, or a typed unresolved
reason. One resolver owns that judgment. Library Body Analysis's declared-method
map and this support are two sources of candidate methods for it, so neither
defines another resolver. For each physical call site,
`MethodQuerySource_CallTargetsMatchLibraryBodyAnalysis` gates that both issue
the same target. This support is the declared shared lookup that the `Calls`
layer ([#9868](https://github.com/richlander/dotnet-inspect/issues/9868))
requires to carry a target for each call site.

The execution constructs one `SameModuleCallTargetLookup` over the subject's
metadata reader at the first query by a declaring producer. Lanes of one shared
execution group use the same lookup. A query names the call kind and operand
token in the visited body:

- `calli` is unresolved as indirect and reads no metadata.
- An operand that peels, through a MethodSpec or a MemberRef on a MethodDef
  parent, to a same-image MethodDef binds by token. The lookup reads that
  method's row for its declaring type, the operand's signature-depth guard,
  and, when the call site passes its checks, the method's signature header to
  check the generic declaration. It decodes no signature.
- Any other operand's member reference is decoded in the caller's generic
  scope. A binding by signature reads the module's TypeDef index, which the
  lookup builds at the first query that needs it. It then decodes the
  signatures of the matched type's methods with the referenced name, once per
  type and name.
- A MethodSpec's instantiation does not affect the target and is not decoded.
- Metadata that cannot be read throws and fails the visited body. A
  generic-declaration header that cannot be read is an invalid declaration, as
  Library Body Analysis judges it.

On hand-authored metadata the two sources can still differ, and each outcome
stays contained. Library Body Analysis fails a body whose MethodSpec
instantiation is invalid for its target, and it omits from its declared
methods a MethodDef whose identity it could not build. The lookup resolves the
MethodSpec's target and supplies every MethodDef row.

The source receipt carries `SameModuleCallTargetCoverage`. Construction reads
the module name and assembly identity and records no work. Query work counts
three things:

- the TypeDef rows read to build the index;
- the MethodDef rows read as caller-scope probes, definition probes, and
  candidates; and
- signature bytes, as the full blob length of each enumerated step each time
  it runs. The steps are the depth guard of a token-bound operand's signature,
  the member reference signature and its TypeSpec parent, and each candidate
  MethodDef signature.

The index and candidate caches last for the execution, so their work is
charged to the query that first needed them. The lookup also remembers the
last caller's scope, so consecutive queries from one body, including those of
another lane visiting it in the same shared pass, read the caller's row once. MemberRef and MethodSpec rows that
peel an operand are not reported; each query reads at most one of each.
Coverage also records the MethodDefs whose visits queried the lookup and the
number of queries. The physical group receipt and each request receipt
attribute construction and queries as memory-safety contract support does.

### Local-signature support

`LocalSignature` is the narrow per-method depth for a producer that needs the
decoded local-variable signature but no call operand. It implies `Body` but not
`Calls`, `CallTargets`, control flow, or `ModuleLookup`. The execution-scoped
`MethodCallMetadataSupport` owns the decode and cache, so compatible lanes
reuse the same packet while each producer receipts its own logical
`LocalSignature` read. `ExactMethodAllocationProducer` is the first production
adopter: it combines this depth with retained selective instructions,
`SemanticIdentity`, memory-safety contracts, and `MethodAnalysis` support to
construct one canonical method context without paying for call-target
resolution.

### Calls support

`Calls` is per-method depth for producers that need IL-ordered call sites with
resolved operand identity, tracked by
[#9868](https://github.com/richlander/dotnet-inspect/issues/9868). It implies
`Body` and `CallTargets` but not `ModuleLookup`, control flow, or wider breadth.
A producer declares the retained-prefix, selective-operand
`ResolvedCalls` analyzer capability and passes that owner-issued instruction
sequence to `GetCalls`; the support does not open or decode another
instruction stream. A producer that did not declare `Calls` cannot access call or field-operand
facts. Local-signature access requires either `LocalSignature` or the wider
`Calls` depth.

Each `MethodCallSite` carries the IL offset, return address, operand token,
call kind, full `MemberRef` callee identity, and typed `MethodCallTarget`.
Call-site classification includes `call`, `callvirt`, `newobj`, `ldftn`,
`ldvirtftn`, and `calli`. Direct call operands accept MethodDef, MemberRef, and
MethodSpec handles. A `calli` accepts a StandaloneSig handle and publishes its
function-pointer signature as the callee identity. An operand of another
handle kind, unreadable metadata, or incomplete instruction decode throws and
fails that producer's visited body; it never becomes an absent call.

The same declared support exposes two facts only when the producer asks:

- local variables as their decoded `TypeRef` values, declared count, and a
  typed incomplete reason when guarded decoding cannot safely complete; and
- a field operand's declaring type, name, field type, optional current-module
  FieldDef token, and RVA flag, or a typed unresolved reason for a
  non-field operand shape.

One execution-scoped lookup owns signature decoding. The packet caches Calls,
the local signature, and each distinct field token for one physical MethodDef.
Lanes in one shared execution group reuse those packet facts. The physical
receipt charges each decode once; each request receipt receives the shared
decode work once and retains its own logical read counts. Work records call
sites, method and field operands, local signatures, generic-parameter rows,
and signature bytes. `CallTargets` separately receipts target-resolution work
for every site, including replay to a lane that reads cached Calls.

Shallow call-count producers do not declare `Calls`. They retain no
instructions, decode no call operand or external signature, construct no
member identity or target, and leave both Calls and CallTargets coverage empty.
Library Body Analysis uses the same local-signature decoder during migration,
but new producers do not use its lookup or aggregate result.

The first adopter is the
[unsafe-member census](unsafe-member-findings.md#execution) at Type and
Library scope. It has no host consumer yet, so this capability makes no production performance
claim.

### Method analysis support

`MethodAnalysis` is declared, execution-scoped support for the reader-backed
judgments shared by allocation and optimization analysis, tracked by
[#10307](https://github.com/richlander/dotnet-inspect/issues/10307). Given one
admitted module and a producer that explicitly declares the support, Method
Query Source constructs at most one `MethodAnalysisLookup` in that execution
group, issues a narrow per-method resolver over the producer-supplied method
identity and instructions, and receipts the support work performed while that
resolver is in scope.

The support does not imply `Body`, `Calls`, `SemanticIdentity`, control flow,
or `ModuleLookup`. A producer declares each current-method prerequisite
separately and passes the owner-issued identity and instructions to
`BeginMethodAnalysis`. The support therefore neither acquires the current
method's body nor widens terminal MethodDef breadth. Exact-method and
exact-type requests retain their sparse source plans. Compatible
all-definition lanes in one shared execution group use one lookup and its
caches; source plans that Query Space keeps in separate groups do not acquire a
cross-group cache.

The lookup composes the existing Analysis authorities rather than defining a
second judgment:

- method and type token resolution in the caller's generic scope;
- signature equivalence and same-image reference correspondence;
- heap, non-heap, delegate-construction, and allocating-box classification;
- generic-parameter, stable-receiver, and async-state-machine classification;
  and
- same-image reference-type and field-owner resolution.

The issued resolver also exposes the existing per-method reaching-definitions
adapter required by allocation escape analysis. `ReachingDefinitions` remains
the semantic owner. The support does not precompute reaching definitions:
the adapter runs only when the producer asks, over the instructions already
issued for that method. Allocation discovery, escape classification,
optimization-opportunity semantics, canonical context construction, and
result enrichment likewise remain with their existing Analysis owners.

Support costs may reach beyond the current MethodDef without widening its
terminal breadth:

- async-state-machine classification lazily scans the module's TypeDef rows
  once per lookup; and
- stable-receiver classification may read a same-image getter body, cached by
  getter MethodDef;
- exact declared-source identity reuses the execution's receipted
  memory-safety contract index rather than constructing the legacy
  unreceipted index, resolves state-machine ownership from the selected
  execution type and its targeted source candidates rather than constructing
  the whole-module state-machine certificate, and receipts generated-provenance
  TypeDef, attribute, and enclosing-relationship reads; and
- exact local-allocation exception classification follows the operand's
  TypeDef or TypeSpec definition and its bounded base chain rather than
  constructing a module-wide type-name index.

Those reads are supporting probes, not terminal methods. They do not add
MethodDefs to the producer's visited population or bodies to its terminal body
coverage.

`MethodAnalysisSupportCoverage` records whether the lookup was constructed,
the number and exact MethodDefs of scoped queries, and
`MethodAnalysisSupportWork`:

- method references resolved and their signature work;
- generic-constraint and stable-receiver queries;
- available stable-getter bodies read and their encoded IL bytes; and
- TypeDef rows scanned for async-state-machine classification;
- declared-source targeted candidates and relationships, generated-provenance
  TypeDef and attribute rows, state-machine interface implementations, and
  contract construction and query work;
- local-exception queries, targeted TypeDef and relationship rows, and
  TypeSpec signature bytes; and
- optimization-suppression queries, method and enclosing-type attribute rows,
  enclosing TypeDef rows, and declaring-type relationships.

Construction is lazy. Declaring but not querying the support constructs
nothing. Disposing the query scope records all support work performed by the
allocation and optimization algorithms that shared its resolver, including
work completed before a recoverable algorithm failure unwinds the visit. The
physical group receipt records each cache miss once. Each lane records its own
query and only the incremental physical support work it caused.

Unreadable metadata follows the existing resolver and producer-failure
contracts. The source neither converts malformed evidence into a successful
empty answer nor falls back to `LibraryBodyAnalysisBuilder`.

During migration, `DeclaredSourceAttribution` composes this same
`MethodAnalysisLookup`, and `LibraryBodyAnalysisBuilder` consumes its existing
resolver and classifiers through that composition. This preserves the donor's
current allocation and optimization behavior without copying the private
resolver. [#10285](https://github.com/richlander/dotnet-inspect/issues/10285)
adds dependent exact allocation and optimization producers over this support.
The allocation owner constructs one canonical context and
`MethodAllocationFacts`; optimization reads those same-unit facts and cannot
rebuild body, control-flow, allocation-discovery, or escape work. The shared
CLI and Browser/Wasm method-analysis consumer now sources exact allocations
and intrinsic opportunities from that operation, applies the existing final
publication enrichment with retained direct-call provenance, declared-owner
suppression, and exception classification. Public direct calls and final
`MethodSignals` come from the same exact producer bundle; the query executes
no Library Body Analysis donor. A recoverable allocation-operand failure
retains the classified prefix and publishes its exact stopping offset and
operation; optimization consumes that same partial fact rather than rebuilding
or replacing it with successful-empty evidence. Optimization suppression uses
the same method-analysis support rather than reading metadata from the visit:
method and enclosing-type generated-code attributes, bounded enclosing TypeDefs,
and declaring-type relationships are separately reported even when suppression
returns before intrinsic opportunity collection.
Its performance case is
`System.Text.Json@10.0.0`
`System.Text.Json.Schema.JsonSchemaExporter.MapJsonSchemaCore(...)`, whose
allocation-hotspot result reports 20 `newobj`, `newarr`, or `box`
sites in a loop.

The counted retirement sequence is:

1. #10307 owns this declared support, legacy composition, and receipts.
2. #10285 moves the shared CLI and Browser/Wasm exact allocation and
   optimization operation onto Method Query Source.
3. #8965 and #9959 drain the remaining donor consumers and delete the
   superseded resolver and Library Body Analysis infrastructure.

### Semantic identity

`SemanticIdentity` is per-method depth for producers that need a MethodDef's
structural identity, tracked by
[#9830](https://github.com/richlander/dotnet-inspect/issues/9830). It is the
identity a finding key or a correspondence is built from, not identity text.
`IdentityText` keeps its budgeted `MethodRowIdentity` text and anchors, and
neither layer reinterprets the other, so declaring `SemanticIdentity` does not
arm the identity-text budget. `SemanticIdentity` acquires no body, does not
imply `ModuleLookup`, and does not widen breadth. A producer that did not
declare it cannot obtain it. It runs only on units the source has already
admitted: admitting a raw exact-target token, including an invalid one, is the
exact-member consumer's work under
[#9832](https://github.com/richlander/dotnet-inspect/issues/9832).

`MethodSemanticIdentity` carries every structural field of the current
`MethodIdentity` except its caller-unsafe contract:

- the module's assembly name and MVID;
- the declaring `TypeRef`;
- the method's name, token, and static and extension flags;
- its parameter and return `TypeRef` values;
- its generic arity and parameter names;
- its signature header and vararg required-parameter count; and
- whether its generic declaration is invalid and whether virtual dispatch can
  reach an override.

The token is valid only with the operation's subject. Like every publication of
this source, the identity is paired with the exact subject identity and is not
a cross-module key. `ToMethodIdentity` composes it with a caller-unsafe mode
into the current `MethodIdentity`. That mode comes from
[Memory-safety contract support](#memory-safety-contract-support), which the
producer declares separately, so the dependency stays visible in its
declaration and receipts.

One reader owns the decode. Library Body Analysis builds its `MethodIdentity`
through the same `MethodSemanticIdentityReader`, so the two cannot diverge.
`MethodQuerySource_SemanticIdentityMatchesLibraryBodyAnalysis` gates that the
source issues the analysis's identity for every MethodDef the analysis
publishes, and a typed malformed outcome for every other.

The result is typed:

- **Decoded:** the identity, decoded completely.
- **SignatureBoundExceeded:** the method signature exceeds the safe decoding
  limits of the signature-depth guard. The identity is still published, with
  no parameter types, an unsupported return type, and a zero header, exactly
  as the current `MethodIdentity` degrades.
- **Malformed:** the metadata cannot be read. No identity is published, and the
  producer decides whether that fails the unit.

The decode is bounded per method by the signature-depth guard and by the type
decoder's relationship-node and type-specification limits. A bound that is
exceeded degrades the affected type to an unsupported `TypeRef`, as the current
identity does; it never reads further.

The physical source decodes each MethodDef at most once per execution group;
every lane that reads it shares that decode. The source receipt carries
`MethodSemanticIdentityCoverage`:

- whether the execution read the module's assembly name and MVID, which it does
  once;
- the decode work, which counts:
  - the method signature bytes, read once by the depth guard;
  - the type decoder's structural work for the declaring type and every
    signature type: one unit per type node, plus the bytes of each name,
    assembly-reference, and type-specification blob it reads;
  - the GenericParam rows read for the generic scope and the
    generic-declaration check;
  - the CustomAttribute rows read by the extension-method check; and
  - the bytes of the method's name and of its own and its declaring type's
    generic parameter names;
- the MethodDefs whose identity was decoded or read; and
- the count of each outcome.

The physical group receipt charges each decode once. Each request receipt
records the identities that request read, with the work they required, so a
request that shares another lane's decode still shows what its answer cost.

### Image exposure support

`PublicMethodRoots` and `ReferenceAssemblyStatus` are declared,
execution-scoped support for producers whose answer depends on facts about the
whole image rather than about one MethodDef, tracked by
[#9865](https://github.com/richlander/dotnet-inspect/issues/9865). The unsafe
member census needs both. Exposure comes from the
[public MethodDef root inventory](public-method-root-inventory.md), and a
reference assembly makes the census incomplete under the
[reference-assembly rule](library-enablements.md#reference-assemblies).

A producer reads them only at completion, through its completion view. They
acquire no body, do not imply `ModuleLookup`, and do not widen breadth. A
producer that did not declare one cannot obtain it. Semantics stay with their
owners. This support only issues and receipts them:

- **Public MethodDef roots** come from `PublicMethodRootInventoryReader.Read`,
  with the image's own TypeDef and MethodDef row counts as limits. The result is the inventory, complete or
  bounded with its retained prefix and boundary, or a typed failure when the
  metadata is malformed. Under image-sized limits a well-formed image is always
  complete; the bounded shape is preserved, not produced.
- **Reference-assembly status** comes from
  `LibraryEnablementFacts.ClassifyReferenceAssembly` as `Implementation`,
  `Reference`, or `Undecidable`. A malformed assembly row is `Undecidable`,
  exactly as the census classifies it today.

Each fact is computed at most once per execution group, at the first read by a
declaring producer, and every lane that reads it shares that result. Both
results are detached: they hold tokens, the module version ID, and counts, never
a reader. An image without metadata publishes a roots failure and an
`Undecidable` status without reading anything.

The source receipt carries `PublicMethodRootCoverage` and
`ReferenceAssemblyStatusCoverage`, separate from MethodDef breadth:

- roots: whether this receipt's lane computed the inventory, its
  `PublicMethodRootInventoryReceipt` (the TypeDefs and MethodDefs visited and
  the roots retained), whether it failed, and the number of reads; and
- status: whether this receipt's lane computed it, the assembly CustomAttribute
  rows it read, and the number of reads.

The physical group receipt records each computation once and every read. A
request receipt records a computation only for the lane whose read created it,
and only that lane's reads. Coverage is empty when no producer declares the
support or no declaring producer reads it. A failed root inventory has no
receipt, because the reader publishes none for a malformed image.

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

A reached generated-expansion bound keeps the retained prefix instead of
failing the source with empty breadth:

- Breadth is the direct methods plus every body the relation recorded
  within the limits. The work totals, `ProbeEncodedIlBytes` and
  `RelationshipNodes`, include the attempt that reached the bound, because
  that work was done. The handle sets `CandidateDefinitionsExamined` and
  `ProbeBodiesAttempted` record only the handles admitted within the limit,
  because the bound check runs before that work. An outcome is recorded only
  after its bound check, so every origin's body is in breadth.
- The expansion coverage names the bound in `ReachedLimit` and
  `ReachedAtToken`. Completion is source incomplete, and the source failure
  carries the bound's message.
- Producers still visit the prefix and complete. Their outcome is
  `SourceBounded`, which is not `HasValue`, so only a consumer that decides
  what the prefix proves reads the value. A producer that stopped early is
  also `SourceBounded`, because it answered only over the prefix.
- During expansion a bound is not a recoverable method failure, so no
  resolver handler turns it into a rejected or unauthenticated outcome. A
  bound that is raised and caught anyway still bounds the relation.
- A unit the relation had not settled when the bound was reached stays
  `Direct`. `IsBounded` on the coverage says the relation is incomplete.
- Once expansion settles, a module lookup that shares its attribution can
  still charge its work, and a resolver cache that stored the bound during
  expansion can replay it. A new bound reached then, and a replayed one, are
  each that unit's recoverable failure.

Terminal body admission retains normalized ordered ranges during forward
traversal rather than one identity per MethodDef. A multipass request may
revisit an earlier MethodDef, so admission materializes unordered membership
only at that transition to preserve distinct-MethodDef body and byte
accounting.

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
  selected, generated-discovery bodies probed, terminal bodies attempted and
  acquired, module-lookup use, and each deeper terminal layer acquired;
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

Library Metrics adopts an exact structural request. One shallow decoded
instruction array supplies instruction cardinality, normal-flow branch shape,
backward-loop ranges, and allocation opcodes; direct invocation resolution
consumes that same bounded per-method evidence without constructing the
canonical `BlockGraph` or `ExceptionFlowTopology`. The physical request
therefore omits local-signature and canonical-context facts entirely. Async
disposition retains the generated-type provenance metadata needed to suppress
runtime-async methods in generated types without requesting complete
MethodEvidence. The direct CLI route and the host-neutral AssemblyContext query
used by package and platform Browser/Wasm exports issue the same request.
Research consumes a focused structural result rather than reconstructing or
publishing complete implementation profiles.

The distinction is measured, not aesthetic. A rejected retained
complete-profile packet removed traversals but improved the exact NativeAOT
CoreLib Library Metrics command median by only 1.264%, below its 5% admission
gate. The packet implementation is not retained. The structural route exists
because QuerySpace must select the minimum physical fact family for the
terminal, not merely share a cheaper implementation of an overbroad
compatibility result.

## Migration and production adoption

Migration is incremental:

1. Introduce the owner-issued source plan, execution, and receipt behind
   `AssemblyAnalysisService`. This slice is implemented.
2. Move unsafe-evidence presence from direct
   `MethodDefinitionExecution.Execute` to the source without changing its
   all-definitions `Exists` result. This slice is implemented.
3. Add exact-method and exact-type breadth with exact coverage receipts. This
   slice is implemented.
4. Move one sparse production body producer and its real CLI consumer,
   preserving its focused result rather than filtering a legacy aggregate.
   Type `Unsafe Members` effective discovery implements this step with exact
   TypeDef breadth.
5. Add authenticated generated-body expansion for that consumer. This slice is
   implemented: exact-Type `Unsafe Members` includes authenticated state-machine
   and lifted execution bodies under finite candidate, generated-method, probe,
   IL-byte, and relationship bounds. Scoped lifted-owner evidence settles once
   per owner group, and state-machine authentication remains targeted.
   State-machine claimant attributes settle once per source declaring-Type
   chain. Nested TypeDef indexing, lifted declaring-chain authentication, and
   every examined state-machine InterfaceImpl and MethodImpl row are included
   in relationship-node work. Both targeted paths retain their acquired
   evidence per TypeDef, so sibling sources reuse it without hiding the work
   charged by the first lookup.
6. Add request-owned terminal body and encoded-IL bounds before migrating a
   body-producing consumer. This slice is implemented with exact per-lane
   receipts and typed source-incomplete exhaustion. Exact-member direct-call
   and Calls-row Count now implement its first body-producing CLI adoption.
7. Add referenced-body expansion only with a consumer that requires it.
8. Let the host-neutral request-set planner from #8574 group compatible
   requests. Method Classification implements the first mixed-terminal CLI
   operation, and exact-member Calls Count implements a direct body-producer
   CLI operation. Grouped body-producer and Browser/Wasm operations remain.
9. Add the `ReferenceBinding` layer with its first producer, async-sibling
   opportunities. The Method source hands the operation's reference-binding
   access to the lookup and charges no work for it beyond the producer's own
   declared lookups. The request-set operation carries the access to grouped
   lanes, and the producer closes Rows, Count, and Exists as independent
   QuerySpace requests through `SyncCallsInAsyncQuery`. This slice is
   **unverified**; legacy callers stay until their consumers move.
10. Add bounded semantic MethodDef identity (#9830) and receipted
    memory-safety contract support (#9831) as independent source capabilities.
    [Memory-safety contract support](#memory-safety-contract-support) is
    implemented; its consumers adopt it in their own slices.
    [Same-module call-target support](#same-module-call-target-support) (#8700)
    and [Semantic identity](#semantic-identity) (#9830) are implemented on the
    same terms.
11. The first exact-member consumer moved in #9832 through
    [exact-member scoped producers](assembly-analysis-operation.md#exact-member-scoped-producers).
    `ExactMethodAnalysisProducer` consumes the exact MethodDef population,
    semantic identity, memory-safety contracts, Calls, and resolved-call body
    analyzer owned by the source; it defines no second population or call
    resolver. `AssemblyContextMethodAnalysisQuery` consumes that focused result
    for identity, safety, diagnostics, and exception regions. When call
    decoding fails recoverably, Calls retains the successfully resolved prefix
    plus a typed stopping failure so independent evidence remains publishable.
12. #10285 adds `LocalSignature` depth and peels exact allocation and
    optimization into dependent producers. The allocation producer owns one
    context and allocation-facts instance; optimization consumes that exact
    fact. `AssemblyContextMethodAnalysisQuery` composes the focused bundle in
    one exact source execution. Calls now owns complete MethodSpec
    definition-token provenance; the bundle publishes direct calls,
    multiplicity, and final signals from the same context and allocation
    facts. For a selected generated body, exact method-analysis support
    authenticates the ultimate declared owner without widening terminal
    breadth or building a module-wide lifted-method census. The support receipt
    separately reports its finite owner-type candidates, closure relationships,
    and attribution IL probes; caller attribution and owner suppression consume
    that answer. Final opportunity enrichment consumes only the exact evidence,
    and the query has no `LibraryBodyAnalysisService` execution.
13. Move remaining producers and delete each superseded legacy scan and index
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
- `MethodQuerySource_ExecutesFocusedProducerDependencyClosure`
- `MethodQuerySource_RejectsUnrelatedRequestedProducer`
- `MethodQuerySource_ReleasedExecutionRetainsNoSubjectAuthority`
- `Execute_FailedBodyReadIsNotReportedAsAcquired`

The exact-breadth slice is gated in Release:

- `MethodQuerySource_NormalizesExactBreadthWithoutReadingSubject`
- `MethodQuerySource_EmptyExactSeedsRemainEmptyPopulations`
- `MethodQuerySource_ExactMethodBreadthVisitsOnlySelectedMethods`
- `MethodQuerySource_ExactTypeBreadthVisitsOnlyDeclaredMethods`
- `MethodQuerySource_ReceiptSeparatesExaminedSelectedAndAcquiredWork`
- `MethodQuerySource_ExactExistsPublishesVisitedSparsePrefix`

Terminal work bounds are gated in Release:

- `Execute_TerminalBodyBoundIsLaneLocal`
- `Execute_TerminalBodyBoundInCountKernelIsSourceIncomplete`
- `Execute_TerminalBodyBoundThroughBodyUseProducerIsSourceIncomplete`
- `Execute_TerminalEncodedIlByteBoundPublishesPartialWork`
- `TerminalWorkBudget_OrderedAdmissionsUseCompactRetention`
- `TerminalWorkBudget_MultipassRevisitIsNotChargedTwice`

Exact-member scalar Count adoption is gated in Release:

- `Execute_CountsExactLogicalMemberWithoutRows`
- `Execute_SumsGeneratedPhysicalBodiesForLogicalMember`
- `Execute_BodilessMethodCompletesWithZero`
- `Execute_CallSiteCountIncludesFunctionLoads`
- `Execute_AttributionExhaustionWithholdsCallSiteCount`
- `Execute_TerminalBodyExhaustionWithholdsCount`
- `MethodCallCountProducer_DoesNotResolveMalformedTarget`
- `MethodCallCountProducer_DiscoveryFailureIsVisible`
- `MethodCallCountProducer_DoesNotDecodeDamagedSignature`

The first production adoption is gated in Release:

- `ExactTypeBreadthReceiptsOnlyTheSelectedType`
- `ExactTypeBreadthIncludesGeneratedUnsafeBodies`
- `TypeAuditEffectiveDiscovery_ListsUnsafeMembers`
- `TypeAuditEffectiveDiscovery_ListsGeneratedUnsafeMembers`
- `TypeAuditEffectiveDiscovery_OmitsUnsafeMembersForSafeType`
- `TypeCommand_UnsafeMembersDiscovery_DoesNotBuildIndex`

Generated expansion is gated in Release:

- `MethodQuerySource_GeneratedExpansionVisitsOnlyAuthenticatedBodies`
- `MethodQuerySource_GeneratedExpansionAccountsBodyDependentDiscovery`
- `MethodQuerySource_GeneratedExpansionAvoidsAssemblyStateMachineIndex`
- `MethodQuerySource_GeneratedExpansionAvoidsLegacyLiftedDeclaringChainWalk`
- `MethodQuerySource_GeneratedExpansionSettlesSiblingLiftedEvidenceOnce`
- `MethodQuerySource_GeneratedExpansionBoundsLiftedDeclaringTypeTraversal`
- `MethodQuerySource_GeneratedExpansionBoundsNestedTypeTraversal`
- `MethodQuerySource_GeneratedExpansionBoundsTargetedStateMachineLookup`
- `MethodQuerySource_GeneratedExpansionSettlesTargetedStateMachineClaimsOnce`
- `MethodQuerySource_GeneratedExpansionBoundsStateMachineInterfaceTraversal`
- `MethodQuerySource_GeneratedExpansionBoundsStateMachineMethodImplementationTraversal`
- `MethodQuerySource_GeneratedExpansionReusesTargetedStateMachineLookup`
- `MethodQuerySource_GeneratedExpansionBoundPublishesSourceIncomplete`
- `MethodQuerySource_GeneratedExpansionIlByteBoundRetainsAttemptedWork`

Retained-prefix bounds are gated in Release:

- the two bound tests above assert the selected type's methods in breadth,
  the typed bound, the `SourceBounded` outcome, and that presence still
  reports incomplete;
- `DeclaredSourceRelationBoundIsAPrefixOfTheSettledRelation` sweeps each
  bound dimension and requires every kept outcome to be one the settled
  relation publishes, with every origin's body in breadth;
- `TypeScopeExpansionBoundKeepsTheRetainedPrefix` sweeps one bound across all
  dimensions and requires an available census whose evidence is a subset of
  the unbounded census, with the attribution-bound limitation; and
- `ExpansionBoundIsNotRecoverableAndAlwaysBoundsTheRelation`.

The instruction-demand planner and first production Method-source adoption are
gated in Release:

- `MethodBodyAnalyzerPlanner_SelectsSourceFromCombinedDemand`
- `MethodBodyAnalyzerPlanner_JoinIsOrderIndependent`
- `MethodBodyAnalyzerPlanner_PreservesRealClassifierResults`
- `Plan_SeparatesInstructionSourceKindsWithoutWideningLanes`
- `Execute_FusesNoRetentionInstructionSourceAcrossLanes`
- `Execute_RejectsScopeGuardOnPendingFusedInstructionFact`
- `Execute_SeparatesNoRetentionAndRetainedInstructionSources`
- `Execute_AttributesRetainedSourceOpeningToOpeningLane`
- `Execute_RetainedInstructionFailureReceiptsCompletedPrefix`
- `Execute_CallCountBodyBoundDoesNotOpenUnacquiredInstructionSource`
- `MethodCallCountProducer_DoesNotResolveMalformedTarget`
- `MethodCallCountProducer_DiscoveryFailureIsVisible`

Memory-safety contract support is gated in Release:

- `MemorySafetyMetadataIndex_ReceiptedMatchesUnreceiptedContracts`
- `MemorySafetyMetadataIndex_PublishesAccessorAssociationAndRole`
- `MemorySafetyMetadataIndex_ReceiptsConstructionAndQueryWork`
- `MethodQuerySource_MemorySafetyContractsAbsentWhenUndeclared`
- `MethodQuerySource_MemorySafetyContractsRequireDeclaration`
- `MethodQuerySource_MemorySafetyContractsReceiptExactMethodWork`
- `MethodQuerySource_MemorySafetyContractsSharedOnceAcrossLanes`

Same-module call-target support is gated in Release:

- `MethodQuerySource_CallTargetsMatchLibraryBodyAnalysis`, over the
  callee-resolution fixture, the Analysis test assembly,
  System.Collections.Immutable, and System.Linq
- the `SameModuleTarget_*` synthetic cases, which assert the same parity for
  unmatched, ambiguous, unsupported, malformed, and single-definition member
  references
- `MethodQuerySource_CallTargetsAbsentWhenUndeclared`
- `MethodQuerySource_CallTargetsRequireDeclaration`
- `MethodQuerySource_CallTargetsReceiptExactMethodWork`
- `MethodQuerySource_CallTargetsBindDefinitionTokensWithoutTypeIndex`
- `MethodQuerySource_CallTargetsSharedOnceAcrossLanes`

Calls support is gated in Release:

- `MethodQuerySource_CallsMatchLibraryBodyAnalysis`, over the call-target
  parity subjects
- `MethodQuerySource_CallsAbsentWhenUndeclared`
- `MethodQuerySource_CallsRequireDeclaration`
- `MethodQuerySource_CallsReceiptExactMethodWork`
- `MethodQuerySource_ClosedGenericFieldTypeIsInstantiated`
- `MethodQuerySource_CalleeIdentityPreservesPlatformContractHit`
- `MethodQuerySource_CallsAreDecodedOnceAcrossLanes`
- `MethodQuerySource_FieldOperandsAreDecodedOnceAcrossLanes`
- `MethodQuerySource_MalformedCallOperandFailsBody`
- `MethodQuerySource_FieldMemberReferenceCallFailsBody`
- `MethodQuerySource_MalformedCalliSignatureFailsBody`
- `MethodQuerySource_RepeatedCachedReadsRemainLogicalReads`
- `MethodCallCountProducer_DoesNotResolveMalformedTarget`
- `MethodCallCountProducer_DoesNotDecodeDamagedSignature`

Semantic identity is gated in Release:

- `MethodQuerySource_SemanticIdentityMatchesLibraryBodyAnalysis`, over the same
  four subjects, for every MethodDef
- `MethodQuerySource_SemanticIdentityParityCoversDeclarationShapes`: the
  subjects include extension, generic, nested-type, bodiless, and
  virtual-dispatch methods
- the `SameModuleTarget_*` synthetic cases, which assert the same parity, plus
  `SemanticIdentity_VarargDefinitionKeepsRequiredParameterCount`,
  `SemanticIdentity_SignaturePastDecodeDepthIsBoundedNotMalformed`, and
  `SemanticIdentity_UnreadableSignatureIsTypedMalformed`
- `MethodQuerySource_SemanticIdentityAbsentWhenUndeclared`
- `MethodQuerySource_SemanticIdentityRequiresDeclaration`
- `MethodQuerySource_SemanticIdentityReceiptsExactMethodWork`
- `MethodQuerySource_SemanticIdentityOfBodilessMethodAcquiresNoBody`
- `MethodQuerySource_SemanticIdentityDecodedOnceAcrossLanes`

The following deeper-source gates remain **unverified**:

- `MethodQuerySource_ReferencedExpansionIsBoundedDeduplicatedAndOrdered`
- `MethodQuerySource_DeepestTerminalLayerIsAcquiredOncePerMethod`
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
independent concurrent state machine. The Library Metrics structural route uses
the existing scheduler and has no early-settling terminal. A future parallel,
incremental, or retained executor must model its scheduling and close
interaction before implementation.

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
