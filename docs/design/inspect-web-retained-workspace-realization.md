# Inspect Web Retained Workspace Realization

## Status

Accepted; production adoption is in progress.

The Browser realization host seam and retained activation transaction are
implemented. Workspace Definitions now supplies complete resource-free
restoration under
[#7027](https://github.com/richlander/dotnet-inspect/issues/7027), and
[#7028](https://github.com/richlander/dotnet-inspect/issues/7028) consumes its
format-2 and format-3 packet paths through the production Browser facade. The
format-1 Browser URL projection remains intentionally partial and cannot enter
this transaction. Producer migration and retirement of the compatibility
snapshot collection remain
[#7031](https://github.com/richlander/dotnet-inspect/issues/7031).
[Adoption and retirement](#adoption-and-retirement) records the remaining
production boundaries.

This document is the normative owner for how Inspect Web retains selectable
Workspace definitions, selects one definition, realizes it, and composes that
selection with browser history and presentation. It consumes:

- [Workspace Definitions](workspace-definitions.md) for portable definition
  lowering and complete restoration,
- [Artifact Acquisition and Workspaces](artifact-acquisition-and-workspaces.md)
  for candidate construction, atomic cutover, operation admission, predecessor
  drainage, and settlement,
- [Stateless Core Services](stateless-core-services.md) for the repository-wide
  one-active-realization rule, and
- [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) for
  browser-history classification, atomic Navigation-result installation,
  canonical location commitment, focus, announcement, acknowledgement, and
  abandonment ordering.

It does not redefine any of those contracts.

Tracked by [#6757](https://github.com/richlander/dotnet-inspect/issues/6757)
under the end-to-end adoption tracker
[#6749](https://github.com/richlander/dotnet-inspect/issues/6749).

## Exact claim

Inspect Web may retain zero or more resource-free Workspace definitions and
restoration records, but at most one selected Workspace realization may admit
new user operations.

Selecting an inactive definition constructs a fresh candidate realization and
atomically cuts over only after construction succeeds. The incumbent
definition, presentation, and realization remain selected when construction
fails, is cancelled, or is superseded. A predecessor may drain work after
cutover, but it is never selectable and cannot admit new work. Inspect Web does
not search dormant definitions, candidates, predecessors, or past realizations
for a compatible realization to revive.

The enforcing product gates are the Browser realization host's exclusive use
of `WorkspaceRealizationCoordinator` for construction, cutover, operation
admission, and settlement, plus the host's bounded aggregate realization
admission described in [Backpressure](#backpressure). Until every production
path listed in
[Adoption and retirement](#adoption-and-retirement) uses that gate, this claim
is `unverified` for Inspect Web as a whole.

## User purpose

Inspect Web should let a person keep several useful Workspace definitions in a
page session without keeping several package, metadata, analysis, source, or
Platform realization graphs alive. Selecting a prior definition should feel
like returning to the same named Workspace while still receiving a fresh,
exact realization of that definition.

This contract supports the unified Compare experience:

- Package owns Diff and Clone targets.
- Library and Type provide result inventories.
- Member is the first detail boundary.
- Diff, Clone, and Call Graph operations enter the same exact selected
  realization rather than finding whichever retained scope appears usable.

The familiar name and location are durable presentation identity. They are not
runtime authority.

## Basis

The conventional baseline is a resource-free collection of saved or recent
documents plus one active document session. A user may switch among document
definitions, but inactive entries do not retain open readers, sessions, caches,
or operation authority.

Inspect Web follows that baseline. Its deliberate strengthening is that
selection is an explicit construction-and-cutover transaction:

1. the current realization remains authoritative while the candidate is built,
2. the candidate cannot admit user operations before cutover,
3. cutover publishes one exact realization identity,
4. the predecessor drains already admitted work without remaining selectable,
   and
5. candidate or predecessor failure is visible rather than represented as an
   empty successful Workspace.

## Demo

The following example uses real package definitions but shows the intended
Browser behavior rather than a particular control layout.

1. Open `Humanizer.Core@2.14.1` and name the retained definition **Humanizer**.
2. Open `Newtonsoft.Json@13.0.3` and name the retained definition **Json.NET**.
3. Select **Humanizer** again.

The page keeps two retained definitions:

```text
Workspaces

  Humanizer   Humanizer.Core 2.14.1       Activating...
  Json.NET    Newtonsoft.Json 13.0.3      Active
```

While Humanizer is being constructed, Json.NET remains interactive. When the
candidate is ready, selection and presentation cut over together:

```text
Workspaces

  Humanizer   Humanizer.Core 2.14.1       Active
  Json.NET    Newtonsoft.Json 13.0.3
```

The second Humanizer activation has the same retained-definition identity and
a new realization identity. It does not revive the realization used before
Json.NET became active.

Neighboring failure case:

1. Json.NET is active.
2. The person selects a retained definition whose package is now unavailable
   from the configured source.
3. Candidate construction reports the acquisition failure.

Json.NET remains selected and interactive. The failed definition remains
available for retry or deletion, and the page does not present an empty
Workspace as though activation succeeded.

## Terms and owner-issued currencies

### Retained definition identity

A retained definition has one page-session-stable host identity. That identity
supports:

- list selection,
- labeling,
- deletion,
- browser-history references, and
- association with resource-free restoration state.

It does not identify a managed `Workspace`, `Scope`, package realization,
Platform target, reader, lease, or operation session.

Two retained entries may contain equivalent definition packets and still have
different retained-definition identities. Inspect Web does not merge entries
by package coordinates, labels, content generation, binding identity, or
apparent compatibility.

### Definition and restoration record

A retained record may contain only state that remains meaningful without a live
Workspace realization:

- the canonical Workspace packet or another complete restoration request,
- the stable retained-definition identity and user-visible label,
- the canonical browser location,
- detached Navigation input and focus identities that the Navigation owners
  define as resource-free,
- definition-derived coordinate count,
- saved-name association and opaque saved packet,
- insertion order, and
- detached activation failure or predecessor-settlement evidence.

The record must not contain or retain:

- an `InspectionWorkspace`,
- an admitted `Workspace.Scope`,
- package or Platform realization groups,
- metadata readers,
- source sessions,
- operation leases,
- query or analysis sessions,
- result models whose validity depends on a realization,
- retry closures capturing realization-owned state, or
- any other owner-issued live authority.

### Active selection

The active selection associates:

- one retained-definition identity,
- one exact `WorkspaceRealizationIdentity` issued by the realization
  coordinator,
- the complete definition snapshot captured by that realization, and
- presentation and Navigation state evaluated for that realization.

The association is explicit. Display labels, package coordinates, canonical
URLs, definition equality, and result text cannot be used to infer it.

Each successful cutover also issues a page-session publication ordinal.
TypeScript uses that ordinal only to order asynchronously delivered
installation results; it does not parse realization identities or use the
ordinal for operation admission. A lower ordinal cannot replace presentation
already installed from a higher ordinal.

### Activation intent

Every asynchronous selection request has host-owned intent identity. A
completion may affect selection only when it still belongs to the latest
applicable intent and the realization coordinator accepts the candidate for
cutover.

The host intent orders user choices. The coordinator's candidate and
realization identities govern runtime authority. Neither substitutes for the
other.

### Candidate, predecessor, and settlement record

A candidate and a predecessor are temporary transition states, not retained
Workspaces.

- A candidate is privately constructed through
  `WorkspaceRealizationConstructionLease`.
- A predecessor contains only already admitted work and drains through the
  coordinator.
- A settlement record is detached evidence of successful or failed retirement.
- A page observation carries the settlement identity and the exact retained
  definition and realization installation that issued it through both success
  and failure callbacks.

Only the coordinator decides when these states may begin, publish, drain, and
settle. Inspect Web may render their progress and failure but does not create a
parallel scope registry or authority system.

## State contract

At every observable Browser state:

1. zero or more retained definitions exist,
2. zero or one retained definition is selected,
3. zero or one coordinator realization admits new user operations,
4. a selected definition and active realization, when present, are explicitly
   associated,
5. candidates and predecessors are not selectable,
6. inactive definitions hold no live realization authority, and
7. presentation derived from a former realization is not installed as current
   state for a fresh realization.

A page with retained definitions may have no active realization during initial
load, after deleting the sole active definition, or after closing the
coordinator. That state is explicit and does not silently select another
definition.

## Selection and activation

### Selecting an inactive definition

Selection is asynchronous and transactional:

1. record the latest activation intent for the exact retained-definition
   identity,
2. lower the retained definition into its immutable `WorkspacePlan` and
   resource-free complete-restoration recipe,
3. reserve Browser aggregate-realization capacity,
4. ask `WorkspaceRealizationCoordinator` to begin a candidate from that exact
   plan,
5. continue restoration against the candidate's
   `WorkspaceRealizationConstructionLease.Workspace`,
6. complete construction with the exact definition snapshot,
7. cut over only if the intent is still current,
8. hand the authorized Navigation outcome to Inspect Web Navigation Consumer
   for installation, canonical location, history, focus, and announcement, and
9. observe predecessor settlement independently.

Definition lowering cannot require a live Workspace and cannot run through a
different temporary Workspace. The continuation receives the
coordinator-owned construction lease; neither Workspace Definitions nor the
Browser retains the lease's Workspace after release.

The incumbent remains selected and continues admitting operations through step
6. Cutover in step 7 transfers new-operation authority before incumbent
presentation is discarded.

Candidate construction may derive initial presentation in private, but that
presentation cannot become current before successful cutover.

### Selecting the active definition

Selecting the already active retained-definition identity is a no-op. Explicit
Reload is a distinct user intent that constructs a fresh realization of the
same definition.

This distinction prevents ordinary focus or history events from causing
unnecessary reconstruction while preserving a clear way to reacquire changing
local or network-backed inputs.

### Supersession

When a newer selection arrives:

- the newer retained-definition identity becomes the current intent,
- a late completion from the older intent cannot publish presentation or
  selection,
- any older candidate retires through coordinator settlement, and
- the incumbent remains active until a current candidate succeeds.

The host does not bypass the coordinator's candidate-settlement barrier to make
the latest request appear faster.

### Failure and cancellation

Candidate acquisition, lowering, restoration, completion, or cutover failure
leaves the incumbent definition, realization, Navigation state, URL, and
presentation selected.

The failure is attached to the attempted retained definition and is visible.
Cancellation is not reported as success. A retry creates a new intent and a
fresh candidate.

### Predecessor settlement failure

Successful cutover remains successful when predecessor retirement later fails.
The new realization stays selected and admits work. Inspect Web presents the
settlement failure as cleanup evidence and keeps the failed predecessor
non-selectable and charged according to the coordinator contract.

The host does not reactivate the predecessor, search it for a compatible scope,
or roll selection back after publication.

## Backpressure

Inspect Web admits at most **four charged Workspace realizations** initially,
preserving the Browser engine's existing aggregate live-scope bound while
separating it from the retained-definition count.

The charged count includes:

- the active realization,
- a construction candidate,
- every nonterminal draining candidate or predecessor, and
- every candidate or predecessor whose terminal settlement failed.

A settled successful realization record is resource-free and does not remain
charged.

The host reserves capacity before calling
`WorkspaceRealizationCoordinator.BeginCandidateAsync`. If all four slots are
charged by nonterminal drainage, the latest activation intent waits
asynchronously for settlement and remains supersedable or cancellable. If
terminal failed settlements consume the remaining capacity, activation fails
visibly with that cleanup evidence and leaves the incumbent selected.

No lock or blocked thread spans this wait. Retained-definition count is a
separate presentation policy and cannot be used as memory backpressure.

`BrowserWorkspaceRealizationHost` owns this aggregate admission. It consumes
the Queries-owned `WorkspaceRealizationSettlement.Succeeded` classification
rather than interpreting owner-specific cleanup reports. A newer Browser
attempt explicitly retires an unpublished coordinator candidate before
reserving its replacement charge, so failed cleanup cannot cause transient
over-admission.

## Entry points

All entry points that can replace the Browser Workspace use the same selection
transaction.

### Saved Workspace Open

Open creates a new page-session retained definition from the saved opaque
packet and requests activation of that definition. The saved record remains
resource-free. Existing retained definitions remain selectable, but their past
realizations do not remain live.

Opening the same saved name twice creates two retained-definition identities
unless the user explicitly selected the already retained entry. Packet equality
does not authorize host-level deduplication.

### Spotlight and package-query handoff

An external package result that is not represented by the active definition
creates or updates a retained definition and requests activation. The current
Workspace remains usable while package acquisition and realization proceed.

An operation whose target is already in the active realization enters that
realization instead of creating a replacement.

### Platform selection

Selecting a Platform target uses the same retained-definition and fresh
candidate path. A Platform catalog lookup may keep its own bounded transient
resource guard, but catalog capacity is not a collection of live user
Workspaces.

### Demos and shared links

Demo, share, and initial-location restoration produce a retained definition
before requesting activation. They do not receive a private alternate
realization path.

## Browser history

The retained-realization owner supplies resource-free selection intent to
Inspect Web Navigation Consumer:

- retained-definition identity,
- canonical location,
- detached Navigation focus and lens state, and
- any owner-issued definition identity required to detect stale restoration
  data.

Navigation Consumer remains the sole owner of browser-history push, replace,
adopt, and traversal realignment, plus atomic result installation and deferred
effects. This design constrains the realization input to that owner:

- Back or Forward within the currently active retained definition may apply a
  Navigation transition against that exact active realization.
- Back or Forward to another retained definition requests fresh activation.
  The current page stays interactive until cutover.
- History traversal does not revive a stored application snapshot or managed
  scope.

If a history entry refers to a retained definition that no longer exists, the
retained-realization owner returns that exact missing-selection outcome.
Navigation Consumer decides how its location, history, focus, announcement,
acknowledgement, and abandonment effects commit. Neither owner guesses a
replacement by label, URL, package coordinates, or definition equality.

## Presentation

The retained list presents definitions, not live Workspaces. Active,
activating, failed activation, and cleanup-failed are explicit statuses.

The current four-entry presentation bound may remain during migration as a
Browser UX policy, but it no longer represents four open scopes or four live
realizations. Changing that count is a separate presentation decision.

During activation:

- the incumbent result surface remains current,
- the target entry shows activation progress,
- controls that act on the incumbent remain associated with its exact
  realization,
- controls that cancel or inspect the candidate use the activation intent, and
- candidate-derived results are not mixed into incumbent presentation.

After cutover, the retained-realization owner invalidates predecessor-bound
result models before they can act on the new realization. Navigation Consumer
then installs the authorized Navigation result and owns the resulting location,
history, focus, and announcement effects.

## Deletion

Deleting an inactive retained definition removes only its resource-free host
record and related history references. It cannot trigger managed scope
retirement because no dormant realization belongs to the entry.

The delete control is disabled while that definition is the target of a
current or coordinator-pending activation. Selecting another definition
supersedes the activation; deletion becomes available after the displaced
candidate settles. This avoids a retained record disappearing while its exact
activation intent is still current.

Deleting the active definition follows one of two paths:

- With a successor, Inspect Web first activates the deterministic neighboring
  retained definition. It removes the old definition only as part of the
  successful selection commit. Failure preserves the old active definition and
  realization.
- Without a successor, Inspect Web removes the definition, closes the active
  coordinator realization, and enters the explicit no-Workspace state.

Deletion never revives a predecessor or searches for a compatible retained
scope.

## Operation admission

Every operation that reads realization-bound state must enter the exact active
realization through `WorkspaceRealizationCoordinator.EnterOperationAsync`.

This includes:

- package, assembly, Library, Type, and Member inspection,
- metadata and source operations,
- dependency queries,
- Diff, Clone, and Call Graph computation,
- Finding and occurrence actions, and
- Platform inspection.

An operation carries the admitted realization identity through its result
publication check. Results from a predecessor may finish, but the
retained-realization owner rejects their realization association before
Navigation Consumer or another presentation owner can install effects.

Exact target identity determines whether an operation belongs to the active
realization. Labels, coordinates, assembly names, Platform names, and content
similarity are not operation-admission keys.

## Package bytes and other shared resources

The one-realization rule does not require redownloading immutable package bytes
or discarding source-client infrastructure.

The following may remain shared when their owners permit it:

- package download single-flight state,
- immutable package content cache entries,
- source clients,
- HTTP transport,
- package metadata search results, and
- bounded transient Platform catalog resources.

Their leases must be held for the lifetime required by the active, candidate,
or draining realization that consumes them. A shared cache entry does not make
the consuming Workspace realization selectable or reusable.

## No compatibility search

Inspect Web must not select runtime authority by searching:

- retained package scopes,
- Platform targets,
- candidate or predecessor realizations,
- package-coordinate sets,
- content-generation matches,
- framework or binding matches,
- assembly labels,
- type or member display names, or
- result provenance text.

Similarity remains a product query input, such as Clone's similarly named
target scope. It is never a realization-selection mechanism.

## Adoption and retirement

The production adoption path has **seven implementation slices** after this
design lock. [#6757](https://github.com/richlander/dotnet-inspect/issues/6757)
tracks the sequence and [#6749](https://github.com/richlander/dotnet-inspect/issues/6749)
tracks the end-to-end architecture retirement.

1. **Browser realization host seam.** Add one managed Browser owner around
   `WorkspaceRealizationCoordinator`, with exact candidate construction,
   cutover, operation admission, predecessor settlement, the four-realization
   aggregate admission bound, and visible failure.
2. **Retained definitions and activation transaction.** Supply the TypeScript
   resource-free definition controller and consume the owner-issued complete
   restoration path for asynchronous selection, rollback presentation, exact
   managed realization association, and detached Navigation installation
   evidence. The transaction does not create a Browser-private restoration
   recipe or treat a currently projectable version-1 URL as a complete record.
   Existing producers remain on their compatibility snapshot path until slice
   3 migrates them; no migrated path may exchange a live application snapshot.
   Tracked by
   [#7028](https://github.com/richlander/dotnet-inspect/issues/7028).
3. **Fresh materialization producers.** Route saved Open, Spotlight external
   packages, package-query handoff, demos, shared links, and initial/history
   restoration through the one activation transaction. Tracked by
   [#7031](https://github.com/richlander/dotnet-inspect/issues/7031).
4. **Package and exact-subject operations.** Move package, assembly, Library,
   Type, Member, metadata, and source paths to exact active-realization
   admission, beginning with
   [#6818](https://github.com/richlander/dotnet-inspect/issues/6818).
   Spotlight's focused Package and package-origin Library adopter is
   [#7030](https://github.com/richlander/dotnet-inspect/issues/7030).
5. **Composite analysis operations.** Move Diff, Clone, Call Graph,
   type-dependency, Finding, and occurrence publication to exact admission and
   generation checks.
6. **Platform realization.** Route Platform selection and operations through
   the same host, preserving only catalog-specific transient bounds.
   Spotlight framework-Library realization remains direct and does not adopt
   a Platform action or destination; the former focused adopter
   [#7029](https://github.com/richlander/dotnet-inspect/issues/7029) is retired.
7. **Legacy registry retirement.** Decouple package-cache accounting and
   occurrence actions, then delete the managed multi-scope registry and every
   lookup, LRU, capacity, quarantine, and compatibility-search surface that
   exists to support multiple retained live realizations.

The sequence is independently landable only when each slice preserves the
current production behavior for paths not yet migrated and refuses mixed
authority within migrated paths. Final #6757 completion requires all seven
slices.

Slice 2 entered after Workspace Definitions gained complete restoration in
issue #7027. Its production facade accepts complete supported packet formats 2
and 3, constructs through `BrowserWorkspaceRealizationHost`, and returns
detached Navigation plus canonical projection and predecessor-settlement
evidence.
Scope supplies complete membership through its ordinary fresh-Workspace
operations; it does not require a restoration-only participant. The existing
snapshot path remains only as explicit compatibility state for the unmigrated
slice-3 producers; it is not an input or fallback for retained activation.

### Required retirement inventory

The final slice removes or replaces:

- TypeScript retention of `CanonicalWorkspaceRestoreSnapshot` as a dormant
  live application snapshot,
- synchronous `publishRetainedWorkspace` and
  `activateRetainedWorkspace` snapshot exchange,
- history-driven snapshot revival,
- `BrowserPackageWorkspace.Scopes`,
- `MaxOpenScopes` as a live-realization capacity,
- scope demand and published-key compatibility lookup,
- retained-scope LRU reuse and eviction after the four-realization Browser
  admission owner replaces their aggregate bound,
- quarantined retained-scope entries after terminal failed settlement remains
  charged by that replacement owner,
- `LeaseRetainedPackageScope`, `IsScopeRetained`, `TouchScope`, and
  realization-selection uses of `RemoveScopeAsync`,
- `BrowserPlatformWorkspace.Targets` as live Platform realizations,
- static live Workspace state in occurrence operations,
- method-body and other label-based retained-scope searches, and
- errors and UI text that describe four simultaneously live Workspaces.

Package acquisition, immutable content caching, source transport, and detached
saved definition records are not retired.

### Dependencies

- [#6750](https://github.com/richlander/dotnet-inspect/issues/6750) supplies
  definition lowering and fresh realization association.
- [#5525](https://github.com/richlander/dotnet-inspect/issues/5525) supplies
  portable Workspace and Package subjects.
- [#6112](https://github.com/richlander/dotnet-inspect/issues/6112) supplies
  the Navigation-owned canonical restoration participant.
- [#6113](https://github.com/richlander/dotnet-inspect/issues/6113) supplies
  retained Navigation results for Browser consumption.
- [#7027](https://github.com/richlander/dotnet-inspect/issues/7027) supplies
  the complete Workspace Definitions restoration transaction and non-install
  cleanup.
- [#7028](https://github.com/richlander/dotnet-inspect/issues/7028) consumes
  that result in the retained Browser activation transaction.
- [#6752](https://github.com/richlander/dotnet-inspect/issues/6752) supplies the
  realization coordinator.
- [#6756](https://github.com/richlander/dotnet-inspect/issues/6756) supplies
  exact Platform generation identity.
- [#6758](https://github.com/richlander/dotnet-inspect/issues/6758) supplies
  exact type-dependency realization authority.
- [#6751](https://github.com/richlander/dotnet-inspect/issues/6751) remains the
  owner for append-only Workspace Scope admission and is required before final
  retirement of package-removal compatibility paths.
- [#6818](https://github.com/richlander/dotnet-inspect/issues/6818) is the first
  exact type-inspection production adopter.
- [#5697](https://github.com/richlander/dotnet-inspect/issues/5697) continues
  to own Workspace viewer/editor expansion rather than runtime retention.

## Evidence

The focused model under
[`models/inspect-web-retained-workspace-realization/`](models/inspect-web-retained-workspace-realization/)
instantiates the owner-issued `WorkspaceRealizationCutover` model and rechecks
its safety properties in Browser composition. It checks:

- one exact active realization association,
- inactive definitions granting no active authority,
- fresh realization identity on reactivation,
- stale activation completion rejection,
- incumbent preservation on candidate failure,
- active-definition deletion only after successful successor activation,
- visible predecessor settlement failure, and
- aggregate realization capacity, and
- eventual predecessor settlement under the coordinator progress assumption.

The implementation slices must add Browser contract tests using product-owned
construction and operation paths. Tests must demonstrate:

- definition A, definition B, then definition A receiving distinct realization
  identities,
- a retained committed view that the version-1 URL cannot project restoring
  from complete resource-free owner-issued state rather than a stale URL or
  live application snapshot,
- candidate failure preserving B's selection and usable operation admission,
- a late A completion failing to replace a newer selection,
- predecessor operations finishing without republishing stale results,
- saved Open and history traversal using the same activation transaction,
- package and Platform paths admitting only the exact active realization, and
- complete removal of retained-scope compatibility search at final retirement.

The first-slice Release gates are:

- `CapacityWait_LatestAttemptStartsAfterPredecessorSettlement`;
- `FullCapacity_NewAttemptSupersedesCandidateBeforeReplacement`;
- `Close_SettlesCapacityWaitBeforePredecessorsDrain`;
- `CleanupFailuresStayChargedAndRejectLaterCandidate`;
- `CandidateRuntimeFailurePreservesActiveAdmissionAndCharge`;
- `CandidateBarrierWait_RemainsCancellableAndSupersedable`;
- `PreCancelledStart_DoesNotSupersedeCurrentCandidate`;
- `StaleCandidateHandle_CannotCutOverCurrentCandidate`;
- `FailedCandidateSupersession_DoesNotOverAdmitReplacement`; and
- `Close_SettlesCandidateBarrierWaitBeforeConstructionDrain`.

The second-slice Release gates add
`BrowserRetainedWorkspaceActivationTests` and
`retained-workspace-activation.test.ts`. They demonstrate A/B/A fresh
realization identity, active-selection no-effect, latest-intent supersession,
incumbent preservation, lower-ordinal response settlement observation,
strict format-1 rejection before candidate charge, predecessor drainage and
detached exactly-once settlement observation, a per-definition deletion
barrier that survives displacement and overlapping activation, visible
displaced cleanup failure, a sole-active deletion barrier that prevents
replacement admission before successful drainage, generation-bound active
deletion that cannot commit over newer selection, retention of resource-free
definitions added during sole-active drainage, deterministic active deletion,
and bounded resource-free retained definitions.
Generated-facade and ordinary Worker inventory gates keep the transaction
callable through the production Browser/Wasm boundary.

Existing package, Platform, Navigation, and analysis entry points remain on
their current paths until their counted adoption slices. The retained
activation transaction alone does not claim whole-product one-realization
enforcement.

Cross-platform support remains inherited from the coordinator and Browser
hosts. No new platform exception is introduced by this design.

## Non-goals

This design does not:

- change Workspace definition lowering or complete restoration,
- redefine `WorkspaceRealizationCoordinator`,
- permit multiple simultaneously selectable Workspaces,
- design simultaneous multi-Workspace tabs,
- define package or Platform cache policy,
- define Workspace Scope mutation,
- define Diff, Clone, or Call Graph query semantics,
- make similarly named Clone targets a realization-selection mechanism,
- promise stable local-file content across activations, or
- add support for Windows Metadata inputs.
