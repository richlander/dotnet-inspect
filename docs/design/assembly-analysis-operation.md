# Assembly Analysis Operation

## Status

This is the target composition contract for
[#8965](https://github.com/richlander/dotnet-inspect/issues/8965), the focused
Library Body Analysis producer-hub retirement effort beneath
[#8568](https://github.com/richlander/dotnet-inspect/issues/8568).

The operator approved this House-shaped, session-aware composition: a
resource-free operation describes the work, a stateless service executes it
against explicit owner-issued access, and the returned execution retains only
detached evidence. Unsafe-evidence presence is the first production adoption.
It binds one single-producer Method request to stack-only session access,
executes the owner-issued Method source, and publishes separate Method-source
and Producer Planning receipts with the focused producer result. The first
serial Method source delegates producer work to the existing sequential
reference executor while owning source planning, binding, and receipt
translation. The eight gates under
[Required evidence](#required-evidence) verify the operation boundary in
Release; the Method source's gates are listed in its owning design.
The request-set operation now binds one immutable QuerySpace plan and publishes
every Method request in request order, plus physical group work recorded once.
Method Classification is its first production caller: the session-backed query
uses request-set access while the PEReader overload remains the direct
reference.
Other source kinds, cancellation, residual request satisfaction, and mixed-
hub composition remain **unverified**. The first exact-member
producers are designed under
[Exact-member scoped producers](#exact-member-scoped-producers).

## Authority and exact claim

**Assembly Analysis Operation** owns this exact claim:

> Given one resource-free `AssemblyAnalysisOperation`, one exact owner-issued
> operation access to an assembly, and the owner-issued source operations that
> access makes available after their owning admission has settled,
> `AssemblyAnalysisService` binds the operation's closed Producer Planning
> description to those sources exactly once and publishes either a detached
> `AssemblyAnalysisExecution` or an operation-level non-success, without
> retaining subject authority or behavior-bearing state.

This owner defines:

- the structural relationship among `AssemblyAnalysisOperation`,
  `AssemblyAnalysisService`, and `AssemblyAnalysisExecution`;
- the one-invocation binding of a closed producer description to exact
  assembly access and owner-issued sources;
- the preservation of operation, subject, source-work, and producer-result
  associations across that invocation;
- the coarse operation boundary at which optional cancellation is observed;
- the sequential reference composition required on single-threaded
  Browser/Wasm; and
- the exact-member producer rule used while the current producer hub is
  drained.

This owner does not define:

- assembly acquisition, identity, admission, borrowing, or release;
- image-format classification or `MetadataReader` construction;
- Type, Member, or Method population, depth, traversal, batching, expansion,
  collapse, pushdown, or continuation;
- producer identity, dependencies, algorithms, evidence, results, or
  participation semantics;
- query-operation registration, portable intent, row selection, or
  presentation;
- metric meaning, roll-up, or the composition of a compatibility profile; or
- host lifetime, caching, persistent state, or cross-operation reuse.

## Conventional basis and adjacent owners

The contract adopts established repository patterns rather than introducing a
new planner or resource owner:

| Owner | Imported contract |
| --- | --- |
| [Stateless Core Services](stateless-core-services.md) | Explicit operation input, stateless service execution, detached output, and no hidden semantic state port. |
| [Analysis Universe Realization](analysis-universe-realization.md) | Exact plan-to-provider correspondence, operation-scoped capability access, owner-issued lifetimes, and failure-preserving release. |
| [Assembly image lifetime](assembly-image-lifetime.md) and [Resource ownership and borrowing](resource-ownership-and-borrowing.md) | Assembly ownership, synchronous borrowing, owned asynchronous work, and settlement obligations. |
| [Session-owned format admission](assembly-inspection-query.md#session-owned-format-admission) | One-time session-backed format classification, retained `MetadataReader` construction, and visible no-metadata, unsupported-format, and malformed-reader outcomes. |
| [Producer Planning](producer-planning.md) | Closed producer descriptions, dependencies, typed outcomes, declaration-keyed results, and participation receipts. |
| QuerySpace owners, including [Query Space Composition](query-space-composition.md) and [Source Delegation](source-delegation.md) | Source requests, collapse, traversal, terminals, delegated execution, completion, and reference equivalence. |
| [Evidence and Metric Coordination](evidence-metric-coordination.md) | Selective metric requirements, shared evidence, roll-up, and actual-work accounting. |
| Analysis evidence owners | Producer algorithms, evidence meaning, focused result types, and typed limitations. |

`AssemblyAnalysisOperation` is an internal Analysis execution operation. It is
not a query-capable operation definition from
[Query Operation Infrastructure](query-operation-infrastructure.md), a
portable host intent, a capability registration, or another analysis-universe
request.

The service role is analogous to the stateless services used behind Houses,
but there is no `AnalysisHouse`. The assembly session or analysis-universe
provider retains physical ownership. The service receives only the access
issued for one invocation.

## Contract shape

```text
consumer-owned Analysis demand
  -> Producer Planning closes declarations and resource requests
  -> AssemblyAnalysisOperation
       resource-free
       exact producer work description
       exact owner-issued source requests
       exact work bounds and terminal requirements
  + session or universe owner settles admission
  + owner-issued operation access to that exact assembly
  -> AssemblyAnalysisService.Execute(...)
       bind each request to the access's AssemblyAnalysisSource role
       execute the QuerySpace reference plan
       visit producers according to their closed description
       preserve source completion, producer outcomes, and diagnostics
  -> AssemblyAnalysisExecution
       exact operation and subject association
       owner-issued source-work receipts
       declaration-keyed producer outcomes and focused results
       no live access
  -> adopting query passes focused results to its consumers
```

The conceptual `Execute` spelling does not freeze a CLR signature. A
synchronous implementation may consume a scoped assembly borrow. Work that
crosses `await` must instead retain owner-issued operation access; a borrow
cannot cross suspension.

### `AssemblyAnalysisOperation`

`AssemblyAnalysisOperation` is closed, immutable, and resource-free. It
retains the exact Producer Planning work description and owner-issued source
requests that execution must honor. Its construction does not open an image,
enumerate assembly metadata, resolve a reference, acquire a reader, or invoke
a producer.

The operation does not merge producer requests. Each declaration contributes
its own QuerySpace request. QuerySpace either keeps them separate or returns
one collapsed source plan plus consumer residuals under
[#8574](https://github.com/richlander/dotnet-inspect/issues/8574).

The direct reference Method request accepts exactly one planned producer. A
request-set operation instead retains each complete QuerySpace association and
lets Method Query Source place compatible all-definition requests into
terminal-specialized lanes within one physical execution group. It never asks
Producer Planning to merge distinct closings or derives one terminal result
from another.

An operation may compose Type, Member, and Method source requests. The service
name is therefore assembly-wide rather than method-specific. Each source owner
retains its row vocabulary, population predicates, depth, expansion,
terminal, completion, and failure contracts.

### `AssemblyAnalysisService`

`AssemblyAnalysisService` is a reusable stateless executor. One invocation
binds one exact operation to one exact assembly access. It may coordinate
request-local structures required by the owner-issued plans, but none becomes
a hidden input to a later invocation.

The service:

- does not reopen an assembly by path or reconstruct access from display
  identity;
- does not classify the image format or construct a `MetadataReader` for
  session-backed execution;
- does not select producers, infer prerequisites, or expand compatibility
  profiles;
- does not enumerate the assembly outside an owner-issued source plan;
- does not implement an Analysis-specific request-union algorithm;
- does not reinterpret a source or producer failure; and
- does not require concurrency.

The sequential executor is normative. A later executor may reorder, batch, or
run work concurrently only when the relevant source and producer owners permit
it and the result is equivalent to the sequential reference execution.

### `AssemblyAnalysisExecution`

`AssemblyAnalysisExecution` is an operation-scoped publication, not a broad
semantic result. It preserves:

- exact association with the operation and subject execution;
- every owner-issued source-work and completion receipt;
- declaration-keyed producer outcomes and focused result values;
- shared diagnostics whose owner permits common publication; and
- typed incompleteness and failure evidence.

Consumers request a result through its producer declaration and then pass the
focused result value to the owning query or section. The execution is not a
runtime-type bag, string-keyed registry, compatibility profile, or substitute
for the focused result type.

The publication retains no session, access lease, borrow, PE reader, Metadata
reader, stream, resolver, source callback, or mutable producer state.

### `AssemblyAnalysisSource`

`AssemblyAnalysisSource` names the internal composition role through which one
exact assembly access exposes owner-issued Analysis sources. It is a source
family, not one universal row space.

For a session-backed source, Assembly Inspection has already classified the
image and published its retained `MetadataReader` before the service receives
access. The source consumes that admitted reader; it does not repeat general
format admission or reader construction. A no-metadata outcome, unsupported
Windows Metadata, or malformed reader construction settles under the
session-owned admission contract before an Analysis producer runs.

The method-definition member of that family is owned by
[Method Query Source](method-query-source.md). It defines method population,
declaration/body depth, generated-body expansion, referenced-callee work,
traversal, early stop, executor equivalence, and exact source-work receipts.
Type and Member sources remain with their respective owners and may use
different row vocabularies or terminals.

### Reference binding access

Some producers answer a question about an assembly by consulting metadata of
the assemblies it references; async-sibling analysis resolves a synchronous
callee's declaring type, and a `FooAsync` candidate on it, in another
assembly. Reference binding is therefore an owner-issued capability of the
operation access, in the same role as the subject's image. It is not an
ambient resolver, a path probe, or acquisition performed by a producer.

`AssemblyInspectionOperationAccess` may carry one
`AssemblyReferenceBindingAccess`: the subject's own
`ResolvedAssemblyReference` and the `IAssemblyBindingPolicy` the issuing owner
selected for that subject. The issuer is the
[Workspace-backed universe provider](analysis-universe-realization.md) or an
equivalent owner. Selection follows
[Structured type-forwarding resolution](type-forwarding-resolution.md), and
any acquisition happens before execution under the
[Assembly Reference Resolution Ladder](assembly-reference-resolution-ladder.md)
that produced the Workspace generation. The binding is read-only for one
synchronous invocation; a producer never acquires, replaces a generation, or
mutates a policy.

A producer declares that it needs the capability through the
`ReferenceBinding` layer of Producer Planning (see
[Method Query Source](method-query-source.md#reference-binding)). When any
planned producer declares it and the access carries none, the service rejects
the operation with `ReferenceBindingUnavailable` before a producer runs. A
producer never silently degrades to same-assembly evidence, because absence of
a finding would then be indistinguishable from an unresolved reference.

Within an invocation, a reference that the policy cannot select, selects
ambiguously, or cannot read is a typed, per-body producer diagnostic. It never
becomes a negative finding. A policy snapshot that changes during the
invocation fails the execution, as it does for the existing binding-policy
resolver.

A request-set operation carries the binding access to every lane, so a plan
that declares the `ReferenceBinding` layer runs when the access is present. It
is rejected with `ReferenceBindingUnavailable` before a producer runs only when
the access is absent.

The detached execution retains no binding access, policy, resolved
assembly, or referenced reader.

## Planning and execution boundaries

Operation construction succeeds only after Producer Planning closes and
validates the producer set. Content is not read to repair an incomplete
description. Failure to form the description is a planning rejection, not an
empty execution.

Operation access is acquired separately. A denied, unavailable, foreign, or
mismatched access rejects execution before a producer runs. Session-backed
format admission likewise settles before source or producer execution. The
service does not widen the universe, reacquire by path, reconstruct a reader,
or substitute equal-looking content.

After execution starts, QuerySpace owns source progress and completion while
Producer Planning owns producer progress and outcomes. A source-delegation
attempt that has committed follows the source-delegation outcome; the service
does not silently rerun the reference path.

Every result and receipt retains the same exact invocation association.
Matching display names, paths, assembly identities, tokens from another
image, or equal-looking operation values do not establish correspondence.

## Migration from Library Body Analysis Execution

The current
[Library Body Analysis Execution](library-body-analysis-service.md) is the
donor and compatibility boundary during migration. It remains callable for
unmigrated consumers, but no new producer coordination belongs in its closed
feature, plan, runner, aggregate, or compatibility-index hubs.

Migration uses a strangler:

1. Prove the method source and service binding with the existing narrow unsafe
   presence production path.
2. Land the minimum general request collapse from #8574 needed to compose
   several producer requests.
3. Leave unmoved producers on the existing hub; no aggregate remainder
   producer is introduced.
4. Move one cohesive producer owner and at least one production consumer per
   slice.
5. Delete each superseded feature, plan, runner, aggregate, and compatibility
   projection when its final consumer moves.
6. Delete the current producer hub after the final producer and consumer
   move.

A migrated producer computes its focused result from its declaration and
visits. Filtering a broad legacy aggregate after construction does not count
as migration.

### Exact-member scoped producers

The first hub-draining slices are designed here and are **unverified** until
they land. The whole-assembly `LibraryBodyAnalysisResult` is not reproducible
from source-scoped units: its declaration counts, unsafe-mode counts,
call-resolution map, declared-source publication, and exception classification
all span every MethodDef and TypeDef. No aggregate "remainder" producer is
declared. Each slice instead peels one focused result for one real consumer,
using exact-member units from the owner-issued Method source and targeted,
receipted lookups for anything outside the selection. A lookup that would need
an unrelated body or would enumerate a producer population is a second planner
and is rejected. An owner-declared, bounded metadata correspondence index is
shared lookup support, not producer breadth; the source receipts every row and
byte it examines.

Normative claim, owned here with call-site population and identity owned by
[#8945](https://github.com/richlander/dotnet-inspect/issues/8945):

> For an owner-issued exact MethodDef target, Method-source producers publish
> the focused declaration, direct-call, and selected-method safety facts the
> consumer reads today. Body work is limited to the selected method; metadata
> correspondence is limited to declared, bounded shared lookups; and the source
> receipts every unit, lookup row, byte, and body it touches.

An `ExactMethodTarget` associates the operation subject with the caller's raw
metadata token. Source admission checks the token kind and row against the
retained reader without scanning a table. An invalid target settles as typed
`InvalidToken` with no producer visit; a valid target becomes the exact
MethodDef breadth and then distinguishes `Bodiless` from `Body`. The resulting
`EvidenceMethod` remains associated with the subject and source receipt; a
naked token is never a cross-module identity.

**Producers.**

| Producer | Fact and result | Support |
| --- | --- | --- |
| Exact-target declaration | For an admitted MethodDef, its detached identity, signature, and body presence. Combined with source admission, it distinguishes an invalid token from a bodiless method and replaces the `callGraph.DeclaredMethods` read. | Declaration and Identity layers. Identity is charged through the source gate's bounded identity decoder. |
| Selected-method safety | Declaration, local, and instruction safety for the selected method: its non-call `UnsafeEvidence` and `UnsafetyOccurrence` values. Bodiless methods retain declaration evidence and have no body evidence. | Declaration, Instructions, and Calls layers for the selected method only. |
| Direct calls | The method's call sites with the #8945 identity tiers (Count; exact declaring Type; exact Type.Member.Overload; full signature), consuming the one physical `EvidenceMethod`. | Calls layer, plus `ReferenceBinding` when a tier needs cross-assembly identity. |
| Call safety | The unsafe mode of each same-image target the selected method calls, plus the normalized unsafe-call evidence. Cross-assembly callees keep the legacy outcome, an unset unsafe mode; resolving external contracts is a later, separately gated behavior change. | `SameImageCallTargetLookup`, below. No target body is acquired. |

The declarations publish one host-neutral focused result containing the token
outcome, method identity, direct calls, unsafety occurrences, unsafe evidence,
and producer diagnostics. Call safety consumes the direct-call result through
its declared dependency, replaces only unsafe-call evidence, and combines that
with the selected-method producer's non-call evidence. It never reconstructs a
whole-assembly call-resolution map.

`SameImageCallTargetLookup` is execution-scoped Method-source support. It
preserves every current-module operand shape admitted by the existing
resolver:

- a MethodSpec first peels to its underlying MethodDef or `MemberRef`, then
  follows the matching path below;
- a MethodDef operand reads that exact declaration;
- a `MemberRef` parented by a MethodDef reads that exact declaration, preserving
  its vararg call-site signature;
- a `MemberRef` parented by a TypeDef starts from that exact type and examines
  its method range;
- a `MemberRef` parented by a constructed TypeSpec decodes the generic
  definition, verifies constructed arity, and resolves the target method; and
- a local TypeRef, including a module or assembly alias authenticated as the
  current image, uses type-name correspondence.

Only the final two paths lazily build the bounded type-name index over the
TypeDef table; all type-based paths examine only the matched TypeDef's method
range for signature correspondence. Construction is shared across named
targets and charges every TypeDef row, candidate MethodDef row, and decoded
correspondence byte to the source receipt. Ambiguous, malformed, or
budget-exhausted correspondence retains the existing visible failure; it never
becomes an unset mode or successful empty result.

**Body acquisition.** Producers read the body from the source's packet; none
calls `GetMethodBody`, `RequireMethodBody`, or `MethodBodySource.Read`. A
call-target lookup reads declarations and signatures only and acquires no body.
Producers left on the old boundary may still acquire bodies independently;
their work is not attributed to the migrated source.

**Admission.** These producers serve exact-MethodDef requests only. Type-wide,
predicate-scoped, and unscoped requests stay on `LibraryBodyAnalysisService`
until a later slice gives their population an owner. The service never falls
back silently, and a request outside admission is rejected with a typed
planning rejection.

**First adopter.** `AssemblyContextMethodAnalysisQuery` is Workspace-backed,
uses one exact method token, and already receives its resolver from the
universe provider (`AssemblyContextAnalysisSource.Resolver`), so the owner that
issues the [reference binding](#reference-binding-access) exists. It reads
declaration, `MethodSignals`, direct calls, safety, allocations, optimization
opportunities, and diagnostics for that one method. This slice moves the
declaration, direct-call, selected-method safety, and call-safety facts.
Allocations, optimization opportunities, and final `MethodSignals` composition
stay on the old boundary until their producers move; the query composes both
paths without wrapping `Build`. The slice therefore claims proportional
migrated work, not proportional total query work while the old path remains.
Issuing the `AssemblyReferenceBindingAccess` from the provider is part of this
slice.

`ILOffsetQuery` follows once an owner can issue operation access and a
reference binding over its prefetched `PdbContext` image without reopening by
path. Until then it stays on the old boundary.

**Stage participation.** Migrated producers do not publish the legacy
`LibraryBodyAnalysisStageParticipation` receipt. The Method source receipt and
Producer Planning `WorkReceipt` describe their work. A consumer that requires
the legacy receipt (CLI `--trace`) stays on the old boundary until it moves.

**Shrink rule.** Each slice moves one real consumer, publishes its existing
focused result, and updates the live drain map in #8965. A hub branch, request
parameter, or feature is deleted when its final consumer moves, not merely
when the first focused adopter lands. The hub is deleted when its last field
leaves.

`CompleteProfileV1` remains a compatibility composition of independently
owned metrics and relationships. It is not one producer. Metric selection and
shared prerequisites follow Evidence and Metric Coordination. Direct-call and
overload-relationship work adopts the method-targeted population and identity
owned by [#8945](https://github.com/richlander/dotnet-inspect/issues/8945)
rather than creating another scanner.

Public API extraction in the implementation-profile family query is separate
whole-assembly work. Its cost and completion are measured independently; body
source proportionality does not claim to remove it.

## Cancellation boundary

Cancellation is optional operation policy. When an adopting operation exposes
it, `AssemblyAnalysisService` observes it only at coarse orchestration
boundaries such as before access binding, between major source-plan phases,
and before terminal publication.

A cancellation token does not enter a producer declaration, `Visit`,
`Complete`, per-unit source callback, decoder, resolver, or producer-local
loop. A visit already in progress completes under its ordinary work bounds;
the orchestrator declines to schedule later phases after observing
cancellation.

Cancellation is one top-level operation non-success. It does not fail each
producer independently, mint a successful `AssemblyAnalysisExecution`, or
reinterpret already bounded source work. QuerySpace terminals, early
satisfaction, and explicit work limits remain their owners' completion
semantics rather than cancellation mechanisms.

An operation need not expose cancellation merely because the service can
compose it. Diff, Graph, Depends, or another outer operation may own the
coarser cancellation gesture and choose the boundaries at which it stops
starting more Analysis work.

## Failure and work evidence

The operation keeps four non-success stages distinct:

| Stage | Required outcome |
| --- | --- |
| Operation formation | One typed planning rejection; no access acquisition or producer work. |
| Admission, access, and source binding | The owner-issued admission, access, or binding non-success; no producer work. |
| Orchestrator cancellation | One top-level cancellation outcome observed at a coarse operation boundary; no producer-local cancellation outcome. |
| Source and producer execution | Owner-issued per-source and per-producer outcomes associated with the exact operation. |

Malformed bodies, unavailable evidence, work bounds, early terminal
satisfaction, and operation cancellation remain visible in the owning outcome
and receipt. Missing work cannot become an empty successful result, a zero
count, a negative Finding, or an absence proof.

The exact source-work receipt is owned by #8577. Aggregate producer
participation remains owned by Producer Planning. This composition preserves
both and does not pretend that the current aggregate `WorkReceipt` proves
which definitions, bodies, expansions, or referenced callees were read.

## Platform and model assessment

The baseline execution is deterministic and sequential. It uses no threads,
dynamic loading, Roslyn, reflection over the inspected assembly, or
host-specific filesystem requirement. Browser/Wasm remains a supported
production host.

This contract adds no mutable publication protocol, scheduler state machine,
concurrent close interaction, or new lifetime owner. Existing resource,
universe-realization, QuerySpace, source-delegation, and Producer Planning
contracts own those behaviors. A TLA+ model would duplicate them without
checking a new transition system.

If an adoption adds incremental publication, concurrent mutation, a new close
protocol, or retained cross-operation state, stop and model that focused
interaction before implementation.

## Required evidence

The first implementation adoption supplies these Release gates:

- `AssemblyAnalysisOperation_PlanningDoesNotOpenSubject`
- `AssemblyAnalysisService_BindsExactOperationAndSubject`
- `AssemblyAnalysisService_RejectsMismatchedOperationAccess`
- `AssemblyAnalysisService_PreservesSourceFailureAndCompletion`
- `AssemblyAnalysisService_PreservesProducerOutcomes`
- `AssemblyAnalysisExecution_ContainsNoLiveSubjectAuthority`
- `AssemblyAnalysisService_SequentialReferenceMatchesInterimExecutor`
- `AssemblyAnalysisOperation_PreservesOwnerIssuedSourceKinds`

The reference-binding capability is verified only by these Release gates,
which are **unverified** until its first adopter lands:

- `AssemblyAnalysisService_RejectsMissingReferenceBinding`
- `AssemblyAnalysisService_PassesReferenceBindingToDeclaringProducers`
- `AssemblyAnalysisExecution_ContainsNoReferenceBindingAuthority`

Those gates now run with the unsafe-evidence production adoption. The operator
selected partial behavioral coverage for
`AssemblyAnalysisExecution_ContainsNoLiveSubjectAuthority`: it disposes the
borrowed session and owning `PdbContext`, then consumes the published operation
association, subject identity, source receipt, producer outcome, focused
result, and `WorkReceipt`. This proves detachment for every surface published
by this adopter. It deliberately does not recursively inspect private object
graphs and is not a universal absence proof for future execution shapes.

`AssemblyAnalysisService_PreservesSourceFailureAndCompletion` covers the first
source boundary: session-owned no-metadata admission rejects before producer
work, while accepted execution publishes the Method source's settled,
exhausted, producer-failed, or aborted completion. Later source kinds and
mid-source delegation failures add their owner-issued outcomes and gates when
they adopt the service.

The first exact-member slice supplies
`AssemblyAnalysisService_MixedLegacyAndMigratedProducersUseOneSourcePlan` and
these gates:

- `ExactMember_DeclarationDistinguishesInvalidFromBodilessToken` asserts the
  typed outcome for a valid body, a bodiless method, and an invalid token, and
  proves that invalid-target admission examines no MethodDef row. For valid
  targets it compares the published detached identity with legacy and asserts
  Identity-layer participation and bounded identity work;
- `ExactMember_SelectedMethodSafetyMatchesLegacy` covers declaration evidence,
  pointer and pinned locals, indirect operations, and non-call unsafe evidence,
  and proves that only the selected body is acquired;
- `ExactMember_CallSafetyResolvesOnlyNamedCallees` asserts that a selected
  caller with an unsafe callee outside the selection reports the legacy safety
  result for a MethodDef/MethodSpec target and `MemberRef` parents of MethodDef,
  TypeDef, constructed TypeSpec, local TypeRef, module-alias TypeRef, and
  assembly-alias TypeRef. The receipt shows which paths avoid the TypeDef
  index, lazy index construction for correspondence paths, only the matched
  TypeDef's MethodDef range, zero target-body acquisitions, and an unset mode
  for a cross-assembly callee exactly as legacy does;
- `ExactMember_AccountsForSelectedAndCorrespondenceWork` counts every
  definition, body, lookup row, and correspondence byte in the migrated source
  plan, including completion. Producer visits and body work cover only the
  selected method; any wider metadata work is bounded declared lookup support
  and appears separately in the source receipt. The gate does not measure
  producers that remain on the old boundary, whose `Build` still enumerates
  the assembly until they move; and
- `ExactMember_RejectsTypeUnscopedOrPredicateRequest` asserts the typed
  planning rejection and that no fallback executes.

The first adopter's validation compares `AssemblyContextMethodAnalysisQuery`'s
complete public outcome (`MethodSignals`, direct calls, allocations, safety,
optimization opportunities, diagnostics, and exception regions), including
invalid and bodiless tokens and same-image aliases, with the old boundary, not
only the focused result. It also proves that the retained `MethodSignals`,
allocation, and optimization values came from the old boundary during this
mixed slice rather than being silently omitted or reattributed.

The positive session path inherits the Release gates owned by
[session-owned format admission](assembly-inspection-query.md#session-owned-format-admission).
This adoption does not add an automated composition-absence gate for repeated
admission and must not describe positive session-path tests as that proof.

An implementation slice that exposes cancellation supplies
`AssemblyAnalysisService_CancellationStopsAtOrchestratorBoundary`. A slice
that does not expose cancellation has no cancellation gate.

The first production slice also keeps the existing CLI and Browser/Wasm
consumer canaries green. A test harness alone is not adoption.

Every implementation-modernization slice records exact base and head
NativeAOT command evidence under
[NativeAOT before/after for modernization](../evidence-and-validation.md#nativeaot-beforeafter-for-modernization).
The pathological family case separately reports public-API extraction and
method-source work so one cost cannot hide the other.

## Non-claims

- No `AnalysisHouse`, universal service interface, service locator, or
  dependency-injection requirement.
- No new acquisition, cache, session, realization, or persistent-state owner.
- No acquisition, binding-policy mutation, or generation replacement during
  producer execution; reference binding is consumed, never driven.
- No universal assembly, Type, Member, or Method source vocabulary.
- No Analysis-owned request collapse, source optimizer, or scheduler.
- No broad producer result, metric bundle, or type-keyed result bag.
- No `CompleteProfileV1` producer.
- No direct-call identity or scanner outside #8945.
- No producer-local, per-unit, or per-instruction cancellation.
- No requirement that every Analysis operation expose cancellation.
- No replacement for Query Operation Infrastructure or Inspection Operation
  Composition.
- No parallel-execution requirement.
- No claim that the remaining Library Body Analysis hubs are already retired.
