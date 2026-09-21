# Navigation Scope-operation consumption

## Status and exact claim

[Inspection Subject Navigation](inspection-subject-navigation.md) is the one
architectural owner. This focused contract addresses the protected-consumption
boundary of issue
[#5584](https://github.com/richlander/dotnet-inspect/issues/5584):

> Navigation accepts one exact Scope-issued operation association before the
> participating effect, protects that transition from later explicit commands
> and stale work, and consumes only the correlated Scope settlement to publish
> the authorized complete Navigation outcome and release protection.

The executable model is under
[`models/navigation-scope-operation-consumption/`](models/navigation-scope-operation-consumption/).
It is bounded design evidence, not C# implementation conformance. The shared
Navigation producer boundary is implemented by
`NavigationTransitions.AcceptScopeOperation`, `EvaluateScopeOperation`, and
`CompleteScopeOperation`. `NavigationScopeOperations` now implements the real
source-retiring Type/Member coordinate replacement and retention producer.
CLI adoption and Browser/Wasm adoption remain **unverified** until their named
Release gates land.

The separate
[Navigation coordinate successor preparation](navigation-coordinate-successor-preparation.md)
contract reuses the retention policy to prepare a fresh Navigation lineage in
an already-populated successor Workspace without a Scope Replace association.
It does not change this protected same-Workspace transition. The portable
consumer retires this replacement path only after it adopts successor
construction in the next #6751 slice.

The adjacent
[Workspace Scope and Expansion](workspace-scope-and-expansion.md)
owner already issues the request/result association, complete terminal
snapshot, optional exact requested occurrence, and typed cancellation-control
result. This contract consumes those owner-issued values without changing
Scope admission, mutation, requested-occurrence, cancellation, or Artifact
publication policy.

## Demo

The shared C# composition is:

```csharp
WorkspaceScopeRequest request = workspace.IssueReplaceScopeRequest(
    currentScope.Revision,
    [destinationBinding],
    deadline,
    workspace.CreateScopePackageTarget(destinationBinding));

NavigationTransition accepted = NavigationTransitions.AcceptScopeOperation(
    currentNavigation,
    request.Association);
if (accepted.AdmissionRefusal is { } refusal)
    return refusal;
if (!NavigationTransitions.CanCommit(currentNavigation, accepted))
    return StaleAcceptance;
currentNavigation = accepted.State;

// A second command returns a typed refusal before new intent or work.
NavigationTransition later = NavigationTransitions.Begin(
    currentNavigation,
    anotherAction);
if (later.AdmissionRefusal is not null)
    ReportRefusal(later.AdmissionRefusal); // Does not abandon the accepted operation.

NavigationScopeEvaluationResult evaluated =
    await NavigationScopeOperations.EvaluateCoordinateReplacementAsync(
        workspace,
        accepted.ScopeWork!,
        request,
        sourceBinding,
        registry,
        availability,
        cancellationToken);
NavigationTransition completed = NavigationTransitions.CompleteScopeOperation(
    currentNavigation,
    accepted.ScopeWork!,
    evaluated);
if (NavigationTransitions.CanCommit(currentNavigation, completed))
    currentNavigation = completed.State;
```

The evaluator validates the accepted request association and original Scope
revision before submission. It admits the source Root query first, observes the
source inside that callback, submits the original request, and prepares the
exact requested destination occurrence from the returned Scope result. The
source binding, destination binding, borrowed realizations, availability
provider, and callback authority remain invocation-local.
An unchanged occurrence preserves its exact active subject, retained path, and
inspector request while rebuilding facts. A Package-only retained path is an
exact retention, not a fallback.

Before leaving admitted source access, the producer uses the correspondence
owner's `Detach()` projection. Its retention result carries Package descriptors,
`CoordinateLibraryPairingEvidence`, and `ApiCoordinateCorrespondenceEvidence`,
not the live matching results or Package observations. Navigation's own exact
destination initialization remains its existing detached protocol currency.
The operation result retains that native correspondence evidence. Its serialized
consumer outcome also carries the retention disposition, explanation, and
Library/Type/Member correspondence statuses; a host need not infer why a lower
context was truncated or turn a refused route into an absence verdict.

While that accepted transition is protected, a later subject, lens,
coordinate, restoration, or participating Scope command receives a synchronous
typed pre-admission refusal. The refusal does not issue another Navigation
intent, consume an action, publish result authority, acknowledge a receipt,
move focus or history, or start an external effect.

The motivating real replacement is
`Avalonia@11.3.14 -> 12.1.2/net8.0`.
`Avalonia.Data.MultiBinding` moves from `Avalonia.Markup` through a forwarder to
its defining `Avalonia.Base` Library. The correspondence producer supplies that
exact Type and defining-Library result; this boundary protects its association
with the matching membership settlement and publishes the prepared
defining-Library context. It does not model or reimplement forwarding.

The neighboring `System.Text.Json@10.0.0` case is an explicit duplicate Add.
Scope returns `NoEffect` with the same exact retained requested occurrence.
Navigation still applies that explicit activation intent, including when
Workspace was active. The same Add without activation intent does not select
the occurrence.

The eventual Browser consumer remains a thin completed-result boundary. This
TypeScript call site is a **mockup**, not an existing export or an additional
host state machine:

```typescript
const result: InspectionEnvelope<NavigationContent> =
    await commands.changePackageCoordinate(destination);
installNavigation(result.content);
renderShare(result.share);
renderDiagnostics(result.diagnostics);
```

The `Promise` represents waiting; the realized envelope retains Content, Share,
and diagnostics. Runtime Scope and Navigation identities stay behind the
shared command boundary rather than being serialized by this sketch.

## Boundary

### Inputs

Navigation consumes:

- the host's exact current `NavigationState` slot for one Workspace;
- one inert `WorkspaceScopeRequest.Association` before submission;
- optional Navigation-owned requested subject or retained-coordinate intent;
- the matching complete `WorkspaceScopeOperationResult`, including its
  complete current snapshot or historical-only unavailable evidence;
- the result's optional exact requested occurrence; and
- invocation-local prepared subject, defining-Library context, Registry, lens,
  and correspondence facts.

The Scope request, Package bindings, Workspace implementation, preparation
services, callbacks, tasks, and execution authority remain invocation-local.
Only resource-free identities, snapshots, results, and Navigation state may be
retained.

### Acceptance and protection

Issuance is neither Scope admission nor Navigation acceptance. The host first
computes Navigation acceptance from its current product state and commits the
transition only when that exact state object is still the current slot. Only
after that commit may it submit the matching Scope request.

There is at most one protected Navigation transition. Acceptance:

- binds the exact Workspace, Scope operation association, Navigation intent,
  and accepted current-state slot;
- invalidates older pre-effect Navigation work and effect authority;
- preserves the identities and FIFO order of queued maintenance and
  synchronization requests; and
- installs no speculative Scope snapshot, subject, lens, focus, history, or
  external effect.

A stale acceptance candidate is rejected at the current-slot commit. A later
explicit or participating Scope command is refused before ordinary admission.
That refusal is not an ordinary `Rejected` Navigation result and cannot itself
supersede the protected intent.

Older evaluation or authority may still return to the host, but it cannot
install facts or execute an effect during protection. Queued work resumes under
the ordinary Navigation state protocol only after correlated settlement
releases the barrier.

### Correlated settlement

Only the original `WorkspaceScopeOperationAssociation` may settle the
protected attempt. A result carrying another operation, including the
superseding operation named by a `Superseded` result, has no publication or
release authority for this transition.

For every arm other than `Unavailable`, Navigation consumes the result's
complete current Scope snapshot. It never reconstructs membership from the
requested effect and never preserves an old inventory merely because the
operation returned `NoEffect`, `Rejected`, `Failed`, `Cancelled`, or
`Superseded`.

`Unavailable` carries at most historical diagnostic evidence. Navigation does
not label its retained snapshot current, issue current-membership authority
from it, or present the old subject inventory as successfully reconciled.

`WorkspaceScopeCancellationResult.ObservedNoEffect` is only a control
observation. It cannot settle the mutation or release protection.
`WorkspaceScopeCancellationResult.Settled` may return the original correlated
mutation result and feed this same settlement boundary. Local cancellation
does not abandon an already submitted external effect or reopen the state to
stale completion.

### Complete Navigation outcome

For `Committed` and `NoEffect` with explicit activation intent, Navigation uses
the exact requested occurrence returned by Scope. Duplicate membership and a
previously active Workspace do not erase that intent. Membership alone does
not imply `Ready`; unavailable or failed preparation remains a typed
Navigation failure.

Without explicit activation, Navigation preserves a still-retained active
occurrence. Otherwise it may use only an exact successor authorized by the
Navigation-owned retained-coordinate input; absent that evidence it selects
Workspace. Scope owner policy never invents activation.

Subject, Library-context, Registry, or lens preparation runs against the
complete result snapshot. When a membership effect has committed but
preparation is unavailable or failed, the published Navigation result exposes
the new current membership and that failure boundary. It must not retain the
old successful inventory as though reconciliation completed.

The Avalonia path consumes the exact prepared `MultiBinding` counterpart and
its defining `Avalonia.Base` context. The active Type can therefore retain its
selected inspector while the non-active Library context moves. Exact
correspondence, availability, and lens results remain facts from their current
owners; this contract adds no matching or fallback algorithm.

### Publication and release

Settlement completion commits only against the protected current-state slot.
It uses Navigation's existing semantic revision, action generation, four-part
effect authority, consumer installation, and composite publication receipt.
There is no second receipt or acknowledgement scheme.

The complete result snapshot and outcome determine semantic change. Action
publication may advance generation independently. A consumer-visible effect
requires the current session, revision, protected intent, and effect epoch,
plus installation of that exact complete publication before acknowledgement.
Only that committed Navigation outcome releases protection.

## Executable model

`NavigationScopeOperationConsumption.tla` imports the Scope-owned
`WorkspaceScopeOperationHandoff` module through a named `Scope` instance with
explicit substitutions. The imported owner module defines the actual
Workspace/operation/kind/revision/target association, one-shot issue and
submission lifecycle, complete result construction, exact requested-occurrence
projection, superseder distinction, and typed cancellation-control transitions.
The Navigation model does not mint successful Scope results.

The finite profiles cover:

- committed replacement, explicit duplicate `NoEffect`, duplicate Pending,
  and replacement without activation;
- an exact Navigation-authorized successor and owner-policy Workspace fallback;
- `Rejected`, `Failed`, `Cancelled`, `Superseded`, and historical-only
  `Unavailable`;
- current inventory differing from Navigation's retained inventory even when
  this request returned `NoEffect` or a non-success settlement;
- membership commit followed by unavailable or failed Navigation preparation;
- exact forwarded Type publication with a changed defining-Library context;
- a later explicit and participating Scope refusal;
- current-slot movement before acceptance;
- an unrelated settled Scope operation; and
- local cancellation and both cancellation-control paths.

The five safety configurations are a disjoint partition by immutable profile;
their union is the full safety profile set. `Liveness.cfg` checks eventual
correlated settlement, protection release, and maintenance resumption under
weak fairness. Reachability configurations force each pathological path.
Committed mutations detect submit-before-acceptance, foreign settlement,
control-level release, stale work, stale effect execution, superseding refusal,
old-inventory retention, wrong requested-occurrence activation, and lost
forwarded Library context.

The model keeps structural matching, Registry classification, and ordinary
Navigation maintenance algorithms opaque. Its snapshots, subjects, lenses, and
defining-Library context are finite owner-supplied values. Bounded TLC success
establishes properties of this specification, not unbounded proof or
implementation conformance.

## Production implementation gates

The PR-fast Release gates are in `NavigationScopeOperationTests`.
Each method name below is prefixed with `ProtectedScope_`:

| Owned claim | Test method |
| --- | --- |
| Acceptance before effects; exact current-slot commit; later-command refusal; stale work/authority invalidation; queue identity retention | `AcceptanceCommitsExactCurrentSlotBeforeSubmission` |
| Original association and exact attempt govern settlement/release | `OnlyOriginalAssociationCanSettleAndRelease` |
| Every complete current settlement arm is consumed | `ConsumesEveryCompleteCurrentSettlementSnapshot` |
| Per-request `NoEffect` does not authorize retaining old inventory | `NoEffectConsumesNewerInventoryThanNavigation` |
| Historical unavailability persists through later admission and maintenance | `UnavailableRetainsHistoricalEvidenceWithoutCurrentAuthority` |
| Cancellation control cannot settle mutation; control failures remain visible | `CancellationControlCannotManufactureSettlement` |
| Explicit duplicate activation uses the exact requested occurrence; no-intent duplicates preserve Workspace selection | `ExplicitDuplicateUsesExactRequestedOccurrence`, `DuplicateHonorsActivationWithRetainedWorkspaceContext` |
| The retained request, not its effective fallback or availability, preserves inspector intent, including Workspace without an active occurrence | `RetainsUnavailableExactInspectorRequest`, `WorkspaceRetainsExactInspectorWithoutActiveOccurrence` |
| Fallback from a descendant recommends a Workspace inspector rather than carrying the descendant's request | `WorkspaceFallbackDoesNotCarryDescendantInspector` |
| Membership-preparation failure publishes current membership and typed failure | `MembershipPreparationFailurePublishesCurrentFailure` |
| Semantic revision, generation, effect authority, installation, and composite acknowledgement remain distinct | `ConsumesEveryCompleteCurrentSettlementSnapshot`, `MembershipPreparationFailurePublishesCurrentFailure` |
| Retained state/results erase invocation resources | `RetainedStateAndResultsDoNotRetainInvocationAuthority` |

These gates exercise product-issued Scope results rather than constructing
successful result arms in a test harness. The explicit duplicate gate uses the
pinned `System.Text.Json@10.0.0` archive. The producer retains only the exact
Scope association, detached Navigation basis, and resource-free outcome.

The coordinate-replacement Release gates are in
`NavigationCoordinateReplacementTests`; each method name is prefixed with
`ProtectedReplacement_`:

| Owned claim | Test method |
| --- | --- |
| The real Avalonia forwarded Type and constructor survive a source-retiring replacement with exact destination inventory, inspector request, actions, and no implicit source Package | `RetainsRealForwardedTypeAndMember` |
| A declared Avalonia extension Member remains exact despite its receiver-projected inventory row; active Member and ancestor Type retain their inspectors, and completion releases protection | `RetainsExtensionDeclarationDespiteReceiverProjection` |
| Member non-success falls back to the exact Type and does not transfer the Member inspector | `MemberNonSuccessFallsBackToExactType` |
| An active entry Library remains at its exact pair when the lower Type moves to another defining Library | `ActiveLibraryTruncatesForwardedLowerContext` |
| Active Workspace and Package ancestors retain their own subject and exact inspector while lower context follows the forwarded Type | `ActiveAncestorsKeepOwnInspectorAndForwardedContext` |
| Destination Registry unavailable/failed outcomes retain the destination-bound exact request without recommendation substitution | `ExactInspectorNonSuccessKeepsDestinationRequest` |
| An unchanged coordinate retains the exact active Type and inspector; a Package-only path remains exact beneath an active Package or Workspace | `UnchangedCoordinateRetainsExactTypeAndInspector`, `PackageOnlyContextIsAnExactPath` |
| A dangling forwarder stops before Member matching and retains native unbound-route evidence | `UnresolvedForwarderDoesNotUseMemberEvidence` |
| A resolved Type whose strict profile changed falls back to its paired Library without retaining lower context; exact Type/Member neighbors retain their inspector, and detached evidence remains readable after Workspace close | `StrictTypeCorrespondenceControlsMemberRetention` |
| Pre-admission cancellation still submits and consumes the original correlated Scope settlement | `PreCancelledRequestSettlesAndCanComplete` |
| Rejected Scope and retired-source preparation failures publish the complete current membership rather than stale successful content | `RejectedScopeUsesCompleteNewerSnapshot`, `RetiredSourceFailureStillSettlesCurrentMembership` |

`ApiCoordinateCorrespondenceQueryTests.RetiredSourceRoot_ReturnsTypedFailure`
keeps the public two-current-Root behavior unchanged.
`ArtifactRootPublicationTests.PackageArtifactRootPublication_RetirementStopsNewEntryAndDrainsLeases`
owns the Acquisition lifetime evidence: retirement rejects fresh entry while
an already admitted query can continue using its borrowed source image, and
release waits for query disposal. The Navigation producer uses that existing
admitted-query lifetime; it adds no lease contract.

The small strict-correspondence and dangling-forwarder cases are PR-fast.
The full Avalonia population cases measured 2.35-9.12 seconds per case in an
isolated Release xUnit report and are tagged `Speed=Slow`. Daily Deep Inspect's
unfiltered Queries suite owns their recurring coverage. The focused pre-merge
gate for this producer is the complete unfiltered
`NavigationCoordinateReplacementTests` class, including its slow cases:

```bash
dotnet run --project tests/DotnetInspector.Queries.Tests -c Release -- \
  --filter-class '*NavigationCoordinateReplacementTests'
```

## Adoption and rendering

The approved #7061 path under #5512 remains six capability steps:

1. Navigation retention policy.
2. Shared correspondence producers.
3. Protected Navigation adoption: this checked contract/model, shared producer
   boundary, source-retiring correspondence orchestration, and exact retained
   API integration are implemented.
4. CLI retained-result adoption in #5513.
5. Browser descriptors and coordinate controls in #5510.
6. Browser complete-result installation in #5511.

Scope prerequisite #7256 is complete. Existing realization and restoration
prerequisites remain separate. Browser-local Package and Library retention
decisions in #7014/#7040 retire only when Browser adoption preserves their
shipped behavior.

Navigation supplies typed state, descriptors, and outcomes. Completed host
command boundaries retain `InspectionEnvelope<TContent>`. CLI lowering remains
with Markout and structured formats. Browser/Wasm interactive HTML, CSS, focus,
and history remain host-owned. This contract adds no rendering domain, output
format, host state machine, generic scheduler, Scope policy, or Browser control
protocol.

## Non-claims

This contract does not define or implement:

- Scope request construction, validation, admission, mutation, cancellation,
  successor selection, or Artifact publication;
- Type, Member, forwarding, correspondence, Registry, or lens algorithms;
- TypeScript APIs, serialization of runtime identities, or retained services;
- Browser focus, history, accessibility, rendering, or operation control;
- CLI command behavior or output;
- complete Workspace restoration or realization cutover; or
- target-only Workspace-rooted graph work in #7301.
