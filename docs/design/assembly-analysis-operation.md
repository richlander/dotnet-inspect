# Assembly Analysis Operation

## Status

This is the proposed target composition contract for
[#8965](https://github.com/richlander/dotnet-inspect/issues/8965), the focused
Library Body Analysis producer-hub retirement effort beneath
[#8568](https://github.com/richlander/dotnet-inspect/issues/8568).

The operator approved this House-shaped, session-aware composition: a
resource-free operation describes the work, a stateless service executes it
against explicit owner-issued access, and the returned execution retains only
detached evidence. The first implementation adoption and every property under
[Required evidence](#required-evidence) remain **unverified** until their
named Release gates land.

## Authority and exact claim

**Assembly Analysis Operation** owns this exact claim:

> Given one resource-free `AssemblyAnalysisOperation`, one exact owner-issued
> operation access to an assembly, and the owner-issued source operations that
> access makes available, `AssemblyAnalysisService` binds the operation's
> closed Producer Planning description to those sources exactly once and
> publishes a detached `AssemblyAnalysisExecution`, preserving source and
> producer outcomes without retaining subject authority or behavior-bearing
> state.

This owner defines:

- the structural relationship among `AssemblyAnalysisOperation`,
  `AssemblyAnalysisService`, and `AssemblyAnalysisExecution`;
- the one-invocation binding of a closed producer description to exact
  assembly access and owner-issued sources;
- the preservation of operation, subject, source-work, and producer-result
  associations across that invocation;
- the sequential reference composition required on single-threaded
  Browser/Wasm; and
- the temporary legacy-remainder rule used while the current producer hub is
  drained.

This owner does not define:

- assembly acquisition, identity, admission, borrowing, or release;
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
  + owner-issued operation access to one assembly
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
- typed cancellation, incompleteness, and failure evidence.

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

The method-definition member of that family is owned by
[#8577](https://github.com/richlander/dotnet-inspect/issues/8577). That effort
defines method population, declaration/body depth, generated-body expansion,
referenced-callee work, traversal, early stop, executor equivalence, and exact
source-work receipts. Type and Member sources remain with their respective
owners and may use different row vocabularies or terminals.

## Planning and execution boundaries

Operation construction succeeds only after Producer Planning closes and
validates the producer set. Content is not read to repair an incomplete
description. Failure to form the description is a planning rejection, not an
empty execution.

Operation access is acquired separately. A denied, unavailable, foreign, or
mismatched access rejects execution before a producer runs. The service does
not widen the universe, reacquire by path, or substitute equal-looking
content.

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
3. Introduce one temporary legacy-remainder declaration for producers that
   have not moved.
4. Move one cohesive producer owner and at least one production consumer per
   slice.
5. Delete each superseded feature, plan, runner, aggregate, and compatibility
   projection when its final consumer moves.
6. Delete the legacy remainder and the current producer hub after the final
   producer and consumer move.

The legacy-remainder declaration is constrained:

- it receives units from the owner-issued QuerySpace source;
- it never enumerates an assembly, opens content, or calls
  `LibraryBodyAnalysisBuilder.Build`;
- it publishes only fields still required by unmigrated consumers; and
- its request and result shape shrink in every migration that consumes part
  of it.

A migrated producer computes its focused result from its declaration and
visits. Filtering a broad legacy aggregate after construction does not count
as migration.

`CompleteProfileV1` remains a compatibility composition of independently
owned metrics and relationships. It is not one producer. Metric selection and
shared prerequisites follow Evidence and Metric Coordination. Direct-call and
overload-relationship work adopts the method-targeted population and identity
owned by [#8945](https://github.com/richlander/dotnet-inspect/issues/8945)
rather than creating another scanner.

Public API extraction in the implementation-profile family query is separate
whole-assembly work. Its cost and completion are measured independently; body
source proportionality does not claim to remove it.

## Failure, cancellation, and work evidence

The execution keeps three failure stages distinct:

| Stage | Required outcome |
| --- | --- |
| Operation formation | One typed planning rejection; no access acquisition or producer work. |
| Access and source binding | One typed access or binding rejection; no producer work. |
| Source and producer execution | Owner-issued per-source and per-producer outcomes associated with the exact operation. |

Malformed bodies, unavailable evidence, work bounds, early terminal
satisfaction, and cancellation remain visible in the owning outcome and
receipt. Missing work cannot become an empty successful result, a zero count,
a negative Finding, or an absence proof.

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

The slice that introduces the legacy-remainder declaration supplies
`AssemblyAnalysisService_MixedLegacyAndMigratedProducersUseOneSourcePlan`.

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
- No universal assembly, Type, Member, or Method source vocabulary.
- No Analysis-owned request collapse, source optimizer, or scheduler.
- No broad producer result, metric bundle, or type-keyed result bag.
- No `CompleteProfileV1` producer.
- No direct-call identity or scanner outside #8945.
- No replacement for Query Operation Infrastructure or Inspection Operation
  Composition.
- No parallel-execution requirement.
- No claim that Library Body Analysis hubs or `LibraryBodyIndex` are already
  retired.
