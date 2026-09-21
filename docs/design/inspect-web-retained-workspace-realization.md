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
this transaction. The uncovered-Spotlight Package managed adoption boundary
now composes a captured schema-version-3 Definitions request with the real
retained owner, including unpublished completion, source-basis revalidation,
synchronous cutover, and one-shot non-posting settlement. Its final
interaction binding, the other producer migrations, and retirement of the
compatibility snapshot collection remain
[#7031](https://github.com/richlander/dotnet-inspect/issues/7031).
[Adoption and retirement](#adoption-and-retirement) records the remaining
production boundaries.

The consumer-accepted completion contract in
[Selection and activation](#selection-and-activation) is the S4
[#7706](https://github.com/richlander/dotnet-inspect/issues/7706) target.
The managed owner, generated Catalog facade, ordinary Worker transport, and
TypeScript controller implement its preparation, acceptance, cutover, and
matching-completion handshake. Final production composition through every
migrated producer remains S6
[#7709](https://github.com/richlander/dotnet-inspect/issues/7709).

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
  browser-history classification, atomic Navigation-result posting,
  canonical location commitment, focus, announcement, acknowledgement, and
  abandonment ordering.

It does not redefine any of those contracts.

Tracked by [#6757](https://github.com/richlander/dotnet-inspect/issues/6757)
under the end-to-end adoption tracker
[#6749](https://github.com/richlander/dotnet-inspect/issues/6749).

## Exact claim

Inspect Web may retain zero or more resource-free Workspace definitions and
restoration records, but at most one selected Workspace realization may admit
new user operations. Each retained Workspace may contain multiple packages and
retain one package or Navigation focus independently of whether that Workspace
is active. An inactive Workspace's retained package selection is inert
restoration state, not live package authority.

Selecting an inactive definition constructs a fresh candidate realization and
atomically cuts over only after construction succeeds and the current consumer
accepts that exact candidate. The incumbent definition, presentation, and
realization remain selected when construction
fails, is cancelled, or is superseded. A predecessor may drain work after
cutover, but it is never selectable and cannot admit new work. Inspect Web does
not search dormant definitions, candidates, predecessors, or past realizations
for a compatible realization to revive.

The enforcing product gates are the Browser realization host's exclusive use
of `WorkspaceReplacementCoordinator` for construction, cutover, operation
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

The following example shows the intended Browser behavior rather than a
particular control layout.

1. Retain **Workspace 1** with `FooPackage` and `BarPackage`, selecting
   `BarPackage`.
2. Retain **Workspace 2** with `BazPackage` and `BarPackage`, selecting
   `BazPackage`.
3. Activate **Workspace 1**.

The page keeps two retained Workspace definitions:

```text
Workspaces

  Workspace 1  Activating...
    FooPackage
    BarPackage  Selected
  Workspace 2  Active
    BazPackage  Selected
    BarPackage
```

`Selected` within Workspace 1 is retained restoration state while Workspace 2
is active; it does not authorize operations against Workspace 1. While
Workspace 1 is being constructed, Workspace 2 remains interactive. When the
candidate is ready, Workspace activation and presentation cut over together:

```text
Workspaces

  Workspace 1  Active
    FooPackage
    BarPackage  Selected
  Workspace 2
    BazPackage  Selected
    BarPackage
```

Activating Workspace 2 and then Workspace 1 again restores each complete
package set and its own retained selected package. The second Workspace 1
activation has the same retained-definition identity and a new realization
identity. It does not revive Workspace 1's earlier realization.

Neighboring failure case:

1. Workspace 2 is active.
2. The person selects a retained definition whose package is now unavailable
   from the configured source.
3. Candidate construction reports the acquisition failure.

Workspace 2 remains selected and interactive. The failed definition remains
available for retry or deletion, and the page does not present an empty
Workspace as though activation succeeded.

### Consumer completion (S4 target)

This is an intended Browser-host scenario, not a claim that the current
one-step facade implements acceptance. Use two retained definitions of the
real `System.Text.Json@9.0.4` Package, whose detached presentation is already
exercised by the Browser activation tests:

```text
A active; B preparing       A still admits inspection.
B ready; consumer rejects   A remains active; B settles unpublished.
B ready; consumer accepts   Exact B may cut over.
B cut over; posting pending C waits or receives an explicit busy result.
B posting completes/fails   B remains active; failure is visible if present.
                            C may proceed; A's drainage is independent.
```

The neighboring deletion case starts with only A active. After accepted
deletion removes A's authority, a legacy Open arrives while A drains. It may
wait or be explicitly rejected, but it cannot capture A as a rollback target.
Deletion finishes with no Workspace and its cleanup outcome visible. A later
failed Open cannot restore A's retired presentation or managed association.

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

### Retained package selection

Each retained Workspace definition may carry one owner-issued package or
Navigation focus among its complete package membership. This is separate from
the Browser's selected Workspace identity.

For the active Workspace, complete restoration evaluates that focus into the
Navigation result and exact active package subject that the Browser posts
after cutover. For an inactive Workspace, the same focus is only resource-free
restoration input. It cannot admit package operations, identify a live Scope,
or imply that the Workspace has a dormant realization.

### Active selection

The active selection associates:

- one retained-definition identity,
- one exact `InspectionWorkspaceIdentity` issued by the realization
  coordinator and represented to the Browser by a host-issued realization ID,
- the complete definition snapshot captured by that realization, and
- presentation and Navigation state evaluated for that realization.

The association is explicit. Display labels, package coordinates, canonical
URLs, definition equality, and result text cannot be used to infer it.

Each successful cutover also issues a page-session publication ordinal.
TypeScript uses that ordinal only to order asynchronously delivered
postings; it does not parse realization identities or use the
ordinal for operation admission. A lower ordinal cannot replace presentation
already posted from a higher ordinal.

The initial Navigation result contributes the third currency in the
cross-runtime posting join: its opaque effect authority. The Browser may
post and settle retained presentation only when realization identity,
publication ordinal, and effect authority all belong to the same successful
publication. Definition identity and package coordinates remain presentation
facts, not substitutes for that tuple.

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
- A prepared activation carries one owner-issued opaque receipt that associates
  the exact candidate, activation intent, detached presentation, and eventual
  consumer completion. Caller-chosen text is never that association.
- A predecessor contains only already admitted work and drains through the
  coordinator.
- A settlement record is detached evidence of successful or failed retirement.
- A page observation carries the settlement identity and the exact retained
  definition and realization posting associated with it through both success
  and failure callbacks.

Only the coordinator decides when these states may begin, publish, drain, and
settle. Inspect Web may render their progress and failure but does not create a
parallel scope registry or authority system.

The retained-realization owner also owns the host transaction around the
coordinator transition. Preparation offers detached candidate evidence to the
consumer while the incumbent remains authoritative. Consumer acceptance is the
last cancellable point. After the matching receipt begins cutover or
deactivation, cancellation and supersession cannot revive retired authority.
The transaction remains open until the same consumer reports completion or
failure. Completion failure is detached visible evidence; it does not roll
back managed authority.

## State contract

At every observable Browser state:

1. zero or more retained definitions exist,
2. zero or one retained definition is selected,
3. zero or one coordinator realization admits new user operations,
4. a selected definition and active realization, when present, are explicitly
   associated,
5. each definition may retain one package or Navigation focus, but only the
   active realization's posted selection grants package operation
   authority,
6. candidates and predecessors are not selectable,
7. inactive definitions hold no live realization authority, and
8. presentation derived from a former realization is not posted as current
   state for a fresh realization.

A page with retained definitions may have no active realization during initial
load, after deleting the sole active definition, or after closing the
coordinator. That state is explicit and does not silently select another
definition.

## Selection and activation

### Selecting an inactive definition

Selection has a reversible preparation and an accepted completion:

- **Preparation:** the latest intent identifies the exact retained definition.
  Its immutable plan and complete-restoration recipe construct one private
  coordinator candidate under the existing admission and construction rules.
  The incumbent remains selected and usable. A ready candidate is not permission
  to cut over.
- **Acceptance:** the consumer accepts only the still-current prepared candidate
  and its captured incumbent association. Acceptance binds one owner-issued
  transition identity to that preparation and commits the consumer to finish
  the resulting operation. An older preparation or acceptance cannot retire a
  newer incumbent.
- **Completion:** the accepted transition owns its cutover result and the
  matching consumer completion. Navigation Consumer posts the returned
  Navigation result and completes or abandons its effects under that owner's
  rules. A later Workspace-changing request cannot take this completion
  obligation away.

Definition lowering cannot require a live Workspace and cannot run through a
different temporary Workspace. The continuation receives the
coordinator-owned construction lease; neither Workspace Definitions nor the
Browser retains the lease's Workspace after release.

Cutover transfers new-operation authority before incumbent presentation is
discarded. Successful publication retains the exact realization identity,
publication ordinal, initial Navigation result and detached presentation
already required by the posting handoff. The transition identity correlates
the completion obligation; it does not replace that posting tuple or authorize
Navigation effects.

Candidate construction may derive initial presentation in private, but that
presentation cannot become current before successful cutover. The consumer may
inspect only the detached prepared presentation before acceptance; it receives
no realization or operation authority from preparation.

An activation receipt is single-use. Commit accepts only the current prepared
receipt. Cancellation accepts only a current receipt that has not begun commit.
Completion accepts only the committed receipt and closes the host transaction
exactly once. An unavailable, stale, duplicated, or wrong-phase receipt fails
visibly and cannot act on another candidate.

Complete restoration projects the package presentation only while the exact
Root bindings and Navigation package evaluations are available. Queries owns
the correlation between Navigation package order, opaque package subject
identity, Root request, Root binding, and evaluation. The callback exposes a
validated ordered projection rather than raw dictionaries or the live
Workspace. Browser code must detach its package surfaces before returning.

The successor `BrowserNavigationStateSlot` is created only after successful
cutover. A candidate that never reaches cutover therefore creates no live
Navigation slot requiring retirement. The predecessor slot is invalidated
before the successor presentation can post, even when cancellation callbacks
make retirement report cleanup failure.

### Acceptance and completion ownership

The same owner admits managed selection, active deletion, and replacement of a
managed incumbent by a compatibility successor. These are operation kinds of
one completion contract, not entry-point-specific barriers. Inactive-definition
edits and ordinary reads against the active realization do not acquire a
Workspace-changing completion obligation.

Before acceptance, rejection, cancellation or supersession retires only the
unpublished candidate and preserves the incumbent. A replacement intent may
supersede preparation, but it still obeys coordinator settlement and capacity
admission.

Acceptance ends ordinary cancellation of that transition. From acceptance
until matching completion, another Workspace-changing request may wait or be
explicitly rejected; it cannot supersede the accepted operation, start
retirement on its behalf, or publish a competing selection. History intent
classification remains Navigation Consumer's responsibility: waiting for this
owner is not permission to write history.

Release requires a terminal managed outcome and completion by the matching
consumer, including visible failure when posting or effects fail. If no cutover
occurred, unpublished-candidate settlement is also required. After successful
replacement, predecessor settlement remains independently observable through
its existing exact association; waiting for that predecessor is not a new
prerequisite for using the successor.

The owner distinguishes failure before cutover from failure after authority
transferred. A failed or missing asynchronous response is not evidence that
cutover did not occur. The consumer must preserve the accepted transition's
ownership until its outcome is known, or surface an unavailable state if the
runtime can no longer report it. It must not infer rollback permission from a
transport exception or a previously captured presentation.

Once the matching completion succeeds or visibly fails, the owner may admit
the next Workspace-changing request. An old completion cannot release a newer
operation. This is ordinary cross-runtime correlation, not a capability that
permits callers to revive or inspect retired realizations.

### Selecting the active definition

Selecting the already active retained-definition identity is a no-op. Explicit
Reload is a distinct user intent that constructs a fresh realization of the
same definition.

Reselection does not release another accepted transition or repost its initial
Navigation effects; it follows the same pending-completion admission rule.

This distinction prevents ordinary focus or history events from causing
unnecessary reconstruction while preserving a clear way to reacquire changing
local or network-backed inputs.

### Supersession

When a newer selection arrives during reversible preparation:

- the newer retained-definition identity becomes the current intent,
- a late completion from the older intent cannot publish presentation or
  selection,
- any older candidate retires through coordinator settlement, and
- the incumbent remains active until a current candidate succeeds.

The host does not bypass the coordinator's candidate-settlement barrier to make
the latest request appear faster. After acceptance, the newer request instead
follows [Acceptance and completion ownership](#acceptance-and-completion-ownership).

### Failure and cancellation

Candidate acquisition, lowering, restoration, construction completion, or
rejected cutover leaves the incumbent definition, realization, Navigation state
and presentation selected. Navigation Consumer owns any accompanying history
disposition; preserving the incumbent is not permission to overwrite a newer
traversal.

The failure is attached to the attempted retained definition and is visible.
Cancellation before acceptance is not reported as success. A retry creates a
new intent and a fresh candidate. Cancellation after acceptance does not undo
the accepted transition or end its completion obligation.

Consumer rejection or failure before acceptance cancels and settles the
candidate without cutover. Each unknown cancellation transport outcome leaves
its candidate unsettled and keeps its own receipt; activation and deletion
remain blocked until every such receipt is retried to a confirmed terminal
result or the owning Worker is closed. Failure after commit begins has a
different boundary. If cutover,
Browser presentation posting, recording that posting, or later required
consumer completion fails, the transition remains owned through the matching
completion report. After successful cutover, the new managed realization
remains active, its exact Navigation authority is abandoned when posting cannot
complete, and the failure is visible. The host cannot restore the predecessor
because managed operation authority has already transferred.

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
`WorkspaceReplacementCoordinator.BeginCandidateAsync`. If all four slots are
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
The rendered result captures the exact resource-free schema-version-3
Definitions request and curated registration plan. Scanner-bearing ecosystem
registrations can make that request nonprojectable to a canonical packet; the
retained record keeps the exact Definitions request and projection evidence
rather than dropping registrations or manufacturing a private packet.

An operation whose target is already in the active realization enters that
realization instead of creating a replacement.

Spotlight's current-subject adopter holds that exact admitted realization
through ordinary Navigation or the complete Package membership-and-focus
operation. Admission failure remains typed separately, and covered Package
failure does not enter fresh-Workspace restoration.

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
adopt, and traversal realignment, plus atomic result posting and deferred
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

The retained list presents Workspace definitions, their resource-free package
membership and retained package focus, not live dormant Workspaces. Active,
activating, failed activation, and cleanup-failed are explicit Workspace
statuses. Package selection is nested within its owning Workspace and is not
rendered as a peer Workspace row.

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
then posts the authorized Navigation result and owns the resulting location,
history, focus, and announcement effects.

### Complete candidate presentation

A posting describes every Package and Platform coordinate in the
completed definition, independently of current focus. Presentation preserves
owner-issued Navigation IDs, context membership and order, requested framework
and RID, and the candidate's selected libraries and bounded API inventory.
Requested framework and selected compile-asset framework remain distinct.
Package and Platform rows have separate types; a Platform is not a Package
with a fabricated Package subject.

Browser projection consumes the opt-in
[complete-restoration inventory](workspace-definitions.md#complete-restoration-inventory).
Detached values remain readable after the realization closes. Library counts,
type rows, truncation and inspection-failure disclosure come from the same
captured API outcome, not a second metadata inspection or a lookup by display
name in the legacy Browser cache. Package framework choices and documents use
the captured selection and manifest. The existing bounded Package icon query
reads the exact retained Root during completion; only its detached image
payload survives projection.

The packet activation Catalog facade publishes typed definition topology and
compact Package and Platform inventories through its generated TypeScript
contract and ordinary Worker transport. Each row retains its exact identity,
context association, selected compile framework, captured Library/Type/Member
and document counts, and inspection-notice presence. Full surfaces, including
icons, remain resident in managed memory and are retrieved one row at a time;
activation and no-effect responses do not repeat every row's API or image
payload. Topology comes from the completed
canonical packet paired with resolved owner-issued IDs, including inactive,
Platform-only and registration-only definitions. Typed registrations retain
their order, package prefixes, exact Package/Platform library coordinates and
assembly identities, and ecosystem contributions; callers need not reparse the
packet to recover those facts. This host-specific JSON
projection is posting state, not a rendered report. Internal definition
activation may still succeed when no canonical packet can represent it.

The complete initial Navigation result and its authority are consumed unchanged
from the Navigation owner. Compact presentation does not page, truncate, or
redefine that independently owned result; the delivery claim here removes
aggregate Package/Platform surface growth, not every possible Navigation
payload limit.

A Package- or Platform-row request is admitted only against the matching active retained
definition, realization and Navigation ID under the existing operation
admission protocol. Stale definition or realization identity is superseded;
a missing row or the wrong row kind is explicitly unavailable. Admission returns detached
presentation, does not change Navigation focus, and does not transfer a live
lease or authorize a later Worker response to commit Browser state.

Detail delivery pages a row's Types explicitly: each response carries its Type
offset, total captured Type count, and next offset (or terminal null). Header
metadata and counts still describe the captured row; the surface's Type array
contains only the identified page. Every page request re-enters the exact
definition, realization and row, so a continuation cannot switch silently to
a replacement realization. Requests start at zero and follow returned offsets.

Detail delivery preserves the ordinary Worker's existing JSON-character and
collection-entry limits. Pages contain at most 100 Types and shrink to fit;
Types are indivisible. An oversized single Type or header returns an explicit
unavailable result, not a truncated successful Type or an undeliverable response.
The initial compact row remains available, and a failed detail read does not
replace or retire the active realization. The same transport accounting is
used by the existing Library API diff boundary.

Recovery slice S3 (#7708) supplies this callable presentation and admission
capability. Activation/deletion completion remains S4 (#7706), Browser
history/async intent ownership remains S5 (#7705), and complete Save/Open and
final UI installation remain S6 (#7709). Coordinate replacement and restored
project RID-selection policy are not part of this slice.

Release gates are
`BrowserRetainedWorkspaceActivationTests` (real `System.Text.Json@9.0.4`
inventory, pinned Platform/mixed contexts, registration-only topology,
post-close presentation, Catalog serialization and stale-row admission) and
`BrowserSpotlightRetainedWorkspaceActivationTests` (neighboring nonprojectable
definition activation). Ordinary Worker forwarding is covered by
`engine-worker-ordinary.test.ts`; generated-facade compilation and authored
TypeScript typechecking enforce the transport shape.
The large-icon delivery case derives twelve distinctly named test Packages
from the real `System.Text.Json@9.0.4` archive, removes its now-invalid signature,
and extends its PNG with a valid ancillary text chunk to the admitted 1 MiB
limit. These are test mutations, not claims about the published Package.
It checks the whole product-generated activation and no-effect responses,
including Navigation, and obtains the full icon only through row admission.
The production-bounds .NET 10 Platform case reads every Type page and compares
the ordered result with the resident captured inventory.
The mixed-context and mixed Catalog cases are marked `Speed=Slow` from isolated
timings; the latter projects the production Platform bounds. The unfiltered
Browser-engine CI job and focused Release activation gate retain these cases.

## Deletion

Deleting an inactive retained definition removes only its resource-free host
record and related history references. It cannot trigger managed scope
retirement because no dormant realization belongs to the entry.

The delete control is disabled while that definition is the target of a
current or coordinator-pending activation. Selecting another definition may
supersede a not-yet-accepted activation; deletion becomes available after the
displaced candidate settles. Accepted transitions instead retain completion
ownership. This avoids a retained record disappearing while its exact
activation intent is still current.

Deleting the active definition follows one of two paths:

- With a successor, Inspect Web first activates the deterministic neighboring
  retained definition through the same prepare, accept, cutover, and matching
  consumer-completion transaction. It removes the old definition as part of
  successful managed cutover. Rejection before authority transfers preserves
  the old active definition and realization; later consumer failure cannot
  restore either.
- Without a successor, Inspect Web removes the definition, closes the active
  coordinator realization, and enters the explicit no-Workspace state. The
  owner issues one deactivation receipt and keeps the transition barrier through
  managed settlement and matching consumer completion of the no-Workspace or
  compatibility-successor presentation. A terminal cleanup or consumer
  completion failure remains visible but cannot preserve active presentation or
  selection after managed authority has been removed. A rejection before
  deactivation begins preserves the incumbent.

Deletion never revives a predecessor or searches for a compatible retained
scope.

Active deletion uses the same accepted completion ownership as selection.
For sole-active deletion, removal of managed authority is irreversible before
drainage completes. The operation remains owned through settlement and the
consumer's matching no-Workspace completion, including visible cleanup failure.
An Open or selection arriving during that interval cannot capture the retired
incumbent as a rollback target or release the deletion's completion obligation.

A compatibility successor does not acquire a managed realization identity.
Replacement first requires current acceptance of the exact incumbent being
retired and remains owned until that retirement and the compatibility
consumer's completion are known. Failure before retirement preserves the
incumbent; failure afterward leaves explicit unavailable or no-Workspace
presentation, not a revived managed snapshot. S6 supplies the compatibility
producer and presentation adapter; this owner does not define legacy packet
interpretation or saved-storage behavior.

## Operation admission

Every operation that reads realization-bound state must enter the exact active
realization through `WorkspaceReplacementCoordinator.EnterOperationAsync`.

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
Navigation Consumer or another presentation owner can post effects.

`BrowserSpotlightRetainedCurrentActivation` is the focused Spotlight adopter:
it admits the captured retained-definition identity once and holds the returned
lease through the complete current-subject operation.

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
   `WorkspaceReplacementCoordinator`, with exact candidate construction,
   cutover, operation admission, predecessor settlement, the four-realization
   aggregate admission bound, and visible failure.
2. **Retained definitions and activation transaction.** Supply the TypeScript
   resource-free definition controller and consume the owner-issued complete
   restoration path for asynchronous selection, rollback presentation, exact
   managed realization association, and detached Navigation posting
   evidence. The transaction does not create a Browser-private restoration
   recipe and rejects version-1 links. Existing producers remain on their
   temporary snapshot path until slice 3 migrates them; no migrated path may
   exchange a live application snapshot, and no compatibility lowering may be
   added for an unshipped Browser format.
   Tracked by
   [#7028](https://github.com/richlander/dotnet-inspect/issues/7028).
3. **Fresh materialization producers.** Route saved Open, Spotlight external
   packages, package-query handoff, demos, shared links, and initial/history
   restoration through the one activation transaction. Tracked by
   [#7031](https://github.com/richlander/dotnet-inspect/issues/7031).
   The uncovered-Spotlight Package managed composition is implemented: an
   owner-issued captured Definitions request can reach the real retained
   owner, and only the still-current source basis may publish the complete
   candidate. Final interaction binding remains #6686; the other listed
   producers remain on the compatibility path.
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
snapshot path is temporary unmigrated implementation state for slice-3
producers, not compatibility state and not an input or fallback for retained
activation.

Within slice 3, the #7516 recovery has **seven implementation steps**: S1
complete root capture (#7707), S2 pinned Platform restoration (#7710), the
detached restoration-inventory prerequisite (#7776), S3 exact Browser
presentation (#7708), S4 accepted completion (#7706), S5 history/intent
ownership (#7705), and S6 production Save/Open adoption (#7709). The first four
have merged. S4 begins with this focused contract/model lock and then supplies
the callable managed, generated-facade and controller handshake; S6 applies it
to all migrated producers and retires their snapshot exchange. The contract
PR is not another implementation step or evidence that S4 is complete.

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
  the complete Workspace Definitions restoration transaction and non-posting
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

The S4 completion model under
[`models/inspect-web-retained-workspace-completion/`](models/inspect-web-retained-workspace-completion/)
checks current acceptance, exact completion ownership, non-revival after
cutover/deactivation, and the independence of predecessor settlement. Its
negative controls represent F01, F05, F06 and F17 from the superseded #7516.
The model consumes coordinator-issued candidate/realization identity; it does
not redefine coordinator transitions or Navigation history/effect policy.
Model results are bounded design evidence only. Implementation enforcement
remains `unverified` until S4 supplies managed Release preparation/commit/
completion/settlement gates, controller outcome cases, generated-facade gates,
and focused Browser/Wasm handshake evidence. S6 must exercise the contract
through every migrated production entry point.

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

- multi-package definition A, multi-package definition B, then definition A
  receiving distinct realization identities while each activation restores
  the exact package set and retained selected package,
- a retained committed view that the version-1 URL cannot project restoring
  from complete resource-free owner-issued state rather than a stale URL or
  live application snapshot,
- an uncovered Spotlight Package restoring from its exact captured
  schema-version-3 Definitions request with the Ecosystems-owned curated
  registrations, including the nonprojectable result,
- source registration movement after complete restoration taking the
  one-shot non-posting path and preserving the incumbent,
- candidate failure preserving B's selection and usable operation admission,
- a late A completion failing to replace a newer selection,
- predecessor operations finishing without republishing stale results,
- saved Open and history traversal using the same activation transaction,
- package and Platform paths admitting only the exact active realization, and
- complete removal of retained-scope compatibility search at final retirement.

The existing retained-realization and Spotlight models continue to compose the
unpublished candidate, final coordinator authority check, publication, and
non-posting transitions. They do not establish the host's consumer-acceptance
and completion handshake. The owner-issued receipt and its pre-acceptance
cancellation versus post-acceptance completion boundary are gated directly by
the managed and TypeScript product tests below rather than represented as a
second coordinator model.

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
realization identity with exact multi-package membership and retained package
focus restoration, active-selection no-effect, latest-intent supersession,
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

The consumer-accepted transaction gates add
`PreparedCandidateRequiresConsumerCommitBeforeCutover`,
`RejectedPreparedCandidatePreservesIncumbent`,
`CommitCompletionKeepsTransitionOwned`,
`CompletionFailureCannotRestorePredecessor`,
`SoleActiveDeactivationRequiresMatchingConsumerCompletion`,
`consumer rejection cancels before cutover`,
`commit barrier remains held through consumer completion`, and
`sole-active deletion cannot revive retired authority`. They exercise the
owner-issued receipt through the managed owner, generated Catalog facade, and
TypeScript controller. Saved Open, browser-history policy, and compatibility
entry-point composition remain later #7705/#7709 gates.

Existing package, Platform, Navigation, and analysis entry points remain on
their current paths until their counted adoption slices. The retained
activation transaction alone does not claim whole-product one-realization
enforcement.

Cross-platform support remains inherited from the coordinator and Browser
hosts. No new platform exception is introduced by this design.

## Non-goals

This design does not:

- change Workspace definition lowering or complete restoration,
- redefine `WorkspaceReplacementCoordinator`,
- permit multiple simultaneously selectable Workspaces,
- design simultaneous multi-Workspace tabs,
- define package or Platform cache policy,
- define Workspace Scope mutation,
- define Diff, Clone, or Call Graph query semantics,
- make similarly named Clone targets a realization-selection mechanism,
- promise stable local-file content across activations, or
- add support for Windows Metadata inputs.
