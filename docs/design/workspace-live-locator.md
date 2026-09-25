# Workspace live locator

## Status and authority

Focused design under
[#6845](https://github.com/richlander/dotnet-inspect/issues/6845), following
the merged [reverse locator contract](reverse-type-declaration-locator.md)
in [#6853](https://github.com/richlander/dotnet-inspect/pull/6853).
The [explicit context projection](#implemented-explicit-context-projection)
and [resident context facade](#implemented-resident-context-facade) are
implemented for loader-issued declaration contexts. The
[reference-population adapter](#implemented-reference-population-admission)
adds package-backed reference realizations under
[#7077](https://github.com/richlander/dotnet-inspect/issues/7077).
The
[Package Scope admission adapter](#implemented-package-scope-declaration-admission)
is implemented under
[#7198](https://github.com/richlander/dotnet-inspect/issues/7198) for retained
Browser/Wasm adoption.
The [PlatformHouse population admission adapter](#implemented-platformhouse-population-admission)
is implemented for CLI Router adoption under
[#6850](https://github.com/richlander/dotnet-inspect/issues/6850).
Other population producers and host adoption remain pending. The interaction
model supplements, rather than certifies, the implementation.

**Workspace Live Locator**, within Workspace composition in
`DotnetInspector.Queries`, owns this claim:

> One active Workspace offers a locator that initializes on first demand,
> retains declaration inventories, follows append-only admitted population
> growth, and evaluates each request against its captured population revision.
> It returns the stateless locator's detached coordinate-plus-origin vectors
> and drains its work through existing Workspace ownership on close.

This focused responsibility does not change Artifact publication, logical
Scope membership, source realization, Metadata decoding, matching, binding,
or resource ownership. The operator-approved lifecycle is initialization,
**append-only growth**, and Workspace closure. Removal, replacement, changing
an existing occurrence's bytes, and refreshing an existing population in place
are not operations of this facade. This scope is not a claim about every
legacy Workspace API.

## Basis and immediate boundaries

The conventional design is a lazy, owner-resident materialized index with a
coherent snapshot-and-notification handoff. The useful distinction is between
live service authority and detached discovery evidence: consumers can reuse
the former without receiving it inside the latter.

| Role | Contract consumed |
| --- | --- |
| Result and matching owner | [Reverse Type-Declaration Locator](reverse-type-declaration-locator.md): exact finite populations, structured names, origin/context vectors, visibility, coverage and failures; the consumer chooses. |
| Population and admission owner | [Artifact acquisition and Workspaces](artifact-acquisition-and-workspaces.md): committed assembly occurrences, source correspondence, physical publication and Workspace lifetime. [Scope](workspace-scope-and-expansion.md) retains logical membership and its own revisions. |
| Resource owner | [Library Ownership and Borrowing](library-ownership-and-borrowing.md) and the existing group/session seams: retain under an owner, transfer async operation authority, borrow synchronously, return detached values. |
| Declaration producer | [Metadata inspection](assembly-inspection-query.md) and [#6848](https://github.com/richlander/dotnet-inspect/issues/6848): structured inventories, visibility and attributed decoding failures. |
| Retained-state boundary | [Stateless core services](stateless-core-services.md#pattern-ports-and-join-currencies): correctness-bearing indexes may live in an active realization; persistent storage is a different owner. |
| Design evidence | [Interaction model](models/workspace-live-locator/README.md): lazy activation, append/initialization races, revision-pinned answers, shared work, and owner-governed release. |
| Production adoption | [Delivery map](reverse-type-locator-adoption.md): cold query, common Sections, CLI Find-to-Type/Member, retained Browser/Wasm use, and old lookup retirement. |

Existing analogous implementations explain both reuse and boundaries.
`PlatformTypeCatalog` retains declarations but mixes discovery with routing
policy and uses a process-static path key. `AssemblyContextTypeInventoryQuery`
provides group-authorized per-participant execution but not the required
structured coordinate/context inventory. Neither is the new facade.
`InspectionWorkspace` retains admitted groups and coordinates their release.
Its dedicated declaration-context observation seam is not a notification API
for every kind of Workspace participant.

The live Library owner and operation-lease APIs are implemented. Adoption must
use that ownership seam rather than inventing a private lease protocol here.

## Population observation

The facade follows one Workspace's admitted, explicitly selected searchable
assembly population. Merely adding an inert ecosystem registration or naming
a package does not publish an assembly. Dependency-support assemblies remain
outside discovery unless the selected population includes them.

The Workspace-side observation seam supplies an immutable **population
receipt** associating:

- its issuing Workspace and exact committed realization/publication evidence;
- the selected population declaration and its upstream completion/gaps; and
- the finite ordered roster, with each occurrence's existing coordinate,
  origin, detached context, and authorized declaration access or typed failure.

The receipt's revision identifies that entire association, not an event
counter, package coordinate, path, binding version, or logical Scope revision.
It preserves the existing owners' distinct physical and logical currencies.
The live facade may compare receipt identity or consume a producer-certified
append relation; it cannot infer a suffix from an opaque version's arithmetic.
An immutable receipt and its roster remain meaningful after a newer receipt
is published.

Before activation, no declaration inventory work or locator subscription is
required. First demand joins one initialization. Registering observation and
capturing its baseline must be one coherent handoff: every concurrent append
is either in that baseline or recoverable through a pending revision
notification. Reading a snapshot and then installing an ordinary event
handler, with an unobserved interval between them, does not satisfy it.

Notifications are invalidations, not membership authority. They may coalesce
or repeat. On notification, the facade obtains an authoritative receipt and
reconciles the missing occurrences. A notification contains neither a borrowed
session nor permission to acquire new content. The handoff must schedule work
without running Metadata inspection or arbitrary callbacks inside publication
coordination. This is a dedicated Workspace observation seam, not a general
event bus.

Once activated, eligible additions schedule declaration maintenance without
another Find call. An append already in progress at activation cannot be
lost. Later unrelated publication can yield an unchanged roster; this causes
no rescan. An earlier unknown realization gap is not silently erased:
only later owner-issued completion evidence can settle it, in a new receipt.

The observation seam is new Workspace work in #6845. If current admission
APIs cannot supply its coherent committed receipt, that producer handoff is a
prerequisite, not something the locator can reconstruct from paths or events.

### Implemented explicit context projection

The first #6845 implementation supplies the cold query's population input.
`LoadDeclarationContextAsync` uses the
existing `WorkspaceContextLoader` acquisition path and issues a
`WorkspaceDeclarationContext`: one frozen request associated with either its
committed group and source correspondence or its upstream realization failure.
The request's order is reserved before acquisition awaits; completion timing
does not choose population order. Completion now also admits that explicitly
selected context into this Workspace's resident declaration population,
including a failed request's upstream gap.

`InspectionWorkspace.CaptureDeclarationPopulation` captures exactly the supplied
loader-issued contexts, ordered by that request order and then by the loader's
member order. It does not discover other Workspace groups, acquire content,
or read declarations. Repeated captures preserve occurrence identity; each
capture issues a new opaque association identity, even for an unchanged roster.
Equal logical coordinates in separate contexts remain separate observations.
An earlier capture is unchanged when a later context is loaded or selected.

`WorkspaceDeclarationPopulationReceipt` and its context/member receipts are
detached evidence. They retain the original request, realization gaps, Metadata
assembly identity, logical Library coordinate when available, and source-issued
realized coordinate and selection provenance. The latter retain the producer
and target/view information separately from the logical coordinate. Live group
access belongs to `WorkspaceDeclarationContext` and
`WorkspaceDeclarationPopulation`, not to their receipts.

Package and Platform loader members project their existing exact coordinates.
NuGet implementation-pack transport does not turn a Platform into a Package.
Only selected participants enter the roster, not the broader available-platform
catalog. Embedded members remain visible with `CoordinateUnavailable`; they
are not reclassified as local files. Raw groups, Artifact Root publication,
local/project producers, and other Library producers are not yet adapters into
this explicit projection. Their future adoption must preserve their own
source-issued correspondence.

`IsRealizationComplete` describes only the selected contexts' acquisition
outcomes. A failed context preserves its entire request and typed failures,
not an invented partial assembly roster or a complete empty population.
An explicit empty selection is realization-complete. Coordinate availability
and declaration inventory success are separate coverage dimensions for #6849.

`ReadDeclarations` reads one selected occurrence through the existing group's
scoped session borrow over retained immutable images. Each call returns
Metadata's detached inventory or rejection, or an explicit unavailable/access
failure. It does not retain the inventory or reacquire the source. Cancellation
is checked before and after the synchronous inspection and remains cancellation.
Closed/released ownership prevents new reads; returned inventories and receipts
remain usable. If acquisition commits a group before cancellation is observed
by the loader wrapper, that group remains under the existing Workspace owner.

Release gates are in `WorkspaceContextLoaderTests`, with the
`DeclarationPopulation_` prefix:

| Claim | Gate suffix |
| --- | --- |
| Stable occurrence/order and unchanged earlier receipts | `PreservesOriginsOccurrencesAndEarlierReceipts`, `RequestOrderAndSnapshotPrecedeAsyncRealization` |
| Source-domain and selected-participant preservation | `PlatformKeepsSourceDomainAndOnlySelectedAssemblies` |
| Upstream failures versus explicit empty selection | `PreservesWholeFailedRequestAlongsideHealthyMembers` |
| Unsupported coordinates, retained reads and detached post-close evidence | `EmbeddedOriginIsNotInventedAndReadsUseRetainedContent` |
| Selection/lifetime rejection and cancellation | `RejectsForeignDuplicateAndReleasedContexts` |
| Declaration rejection remains attributed after successful realization | `MetadataRejectionRemainsAttributedAfterRealization` |
| Real Package and Platform declaration choices | `RealJsonPackageAndPlatformKeepDistinctChoices` |

The real-asset gate uses `System.Text.Json@10.0.0` (`net10.0`) and
`Microsoft.NETCore.App.Runtime.linux-x64@10.0.10` through the actual loader,
selecting `System.Text.Json` in a separate Platform group. Both inventories
declare `System.Text.Json.JsonSerializer`. This is implementation-view evidence;
it does not substitute for the reference-view and resident-maintenance gates
below. The real-asset gate is `Speed=Slow`, retained in daily Deep Inspect's
unfiltered Queries suite; the small-fixture boundary cases remain PR-fast.

The [cold matcher](reverse-type-declaration-locator.md#implemented-cold-query)
and the resident facade below consume this projection. Other population
producers and CLI/Browser adoption remain on the
[delivery map](reverse-type-locator-adoption.md); neither prerequisite exposes
a new Find command.

## Implemented resident context facade

`InspectionWorkspace.GetDeclarationLocator(options)` returns the Workspace's
one `WorkspaceDeclarationLocator`. Getting it does not subscribe or inventory.
The first valid `ExecuteAsync` request activates observation; rejected requests
and already-cancelled callers do not activate it. The first getter fixes the
options, and a later getter cannot silently change the active service's limits.

The observed population consists of completed `LoadDeclarationContextAsync`
calls and explicit reference admissions on that Workspace. These operations
are searchable-context admission;
ordinary `LoadAsync`, raw groups, inert registrations, and Artifact Root/Scope
publication do not implicitly join it. Cold callers may still capture an exact
subset with `CaptureDeclarationPopulation`. The resident facade has one
append-only population, not a mutable selection or a Scope replacement policy.

First request admission atomically observes and captures that population.
Subsequent requests capture its current immutable association. An unchanged
population reuses its receipt identity; a completed context issues a new
association, even when it adds only an upstream failure. Notifications occur
outside publication coordination and reconcile the authoritative roster, not
event counts or ordinal suffixes. A context whose request began earlier can
finish later and sort before already inventoried contexts without relabeling
their occurrences.

Each request retains its own inventory tasks and population receipt. Append
maintenance can finish newer work without adding it to an earlier answer.
Matching, vector construction, ordering and coverage use the same query core
as the cold path. The internal prepared-outcome path changes its inventory
supplier, not its matching or source-selection contract.

The resident unit is an occurrence-associated immutable Metadata outcome.
Both inventories and Metadata's whole-image rejections are reusable; neither
coordinate equality nor identical bytes merge observations. Pending work is
single-flight. Automatic append maintenance schedules new occurrences without
another query and does not retry previous operational failures or bound stops.
A new valid query may retry those unfinished entries while reusing completed
Metadata evidence. The lower image owner may still return its retained failure;
retry is not permission to refresh its immutable content.

`WorkspaceDeclarationLocatorOptions` fixes two finite count limits:

- `MaxInventoryReadsPerAttempt`, default 256, limits newly scheduled whole-image
  read attempts for one request or append reconciliation. Joined work and reused
  outcomes do not consume that attempt's read allowance.
- `MaxRetainedInventories`, default 1,024, limits resident Metadata outcomes
  together with reservations for in-flight construction. No entry is evicted
  to admit another. Operational failures release their reservation.

These are local count bounds, not byte, time, intra-image, matching or output-row
bounds. Zero is an explicit no-work/no-retention choice. A stopped member stays
`NotEvaluated` with `Bound` identifying `ReadAttempts` or `RetainedInventories`;
healthy candidates remain visible with incomplete coverage. The result does
not misrepresent these limits as the cold query's per-call `maxInventoryReads`
window.

`InventoryReadCount` reports attempted scoped reads and
`RetainedInventoryCount` reports resident Metadata outcomes. `Maintenance`
exposes the currently scheduled worker's completion or fault without starting
or retrying work. These permit a host to observe background completion without
making another Find request. Work yields cooperatively between images, using
the existing Package completion pattern; it does not introduce a new scheduler
or platform exception.

Caller cancellation detaches its wait without cancelling shared maintenance.
Workspace close detaches observation, stops admission, cancels waiters and
drains the worker before terminal close. Actual image/session release still
belongs to the groups; no session is stored in this cache. Close clears the
resident outcomes. An unexpected maintenance exception permanently faults
`Maintenance` and subsequent valid requests until close; there is no automatic
worker recovery. Close still releases resources and then propagates that fault,
combining it with an independent exceptional group-close failure if necessary.
The ordinary close report continues to describe resource-release outcomes.

Release gates are in `WorkspaceContextLoaderTests`, prefixed `ResidentLocator_`:

| Claim | Gate suffix |
| --- | --- |
| Lazy activation, shared inventories, public/all reuse and cold equivalence | `IsLazyAndReusesInventoriesAcrossPatternsAndVisibility` |
| Initialization append, duplicate notifications, automatic maintenance and P1/P2 isolation | `AppendDuringInitializationMaintainsWithoutAnotherFindAndPinsReceipt` |
| In-progress acquisition at activation is not lost | `LoadStartedBeforeActivationIsObservedAfterCommit` |
| Growth is not inferred from ordinal arithmetic | `LateEarlierContextDoesNotTreatReceiptGrowthAsAnOrdinalSuffix` |
| A cancelled caller does not stop another caller or later maintenance | `CallerCancellationDoesNotCancelSharedOrLaterMaintenance` |
| Explicit bounds and retry without rescanning healthy entries | `BoundsStayVisibleAndExplicitRetryDoesNotRescanHealthyEntries` |
| Immutable rejection and upstream-gap coverage | `ImmutableRejectionsAreResidentAndUpstreamGapsRemainVisible` |
| Unsupported coordinates remain visible and unscanned | `UnsupportedCoordinateRemainsAnUnscannedObservation` |
| Close drains scheduled work and preserves detached answers | `CloseDrainsScheduledWorkAndPreservesDetachedAnswers` |
| Close waits for the actual borrowed access path | `CloseWaitsForActualBorrowedInventoryAccess` |
| Operational failures are retried rather than memoized as empty inventories | `OperationalRejectionIsRetriedWithoutRescanningHealthyInventory` |
| Fatal maintenance remains visible while close releases resources | `UnexpectedMaintenanceFailureStaysFaultedAndCloseStillReleases` |
| Invalid demand does not activate or reconfigure maintenance | `InvalidRequestsDoNotActivateOrChangeFixedLimits` |
| Real Package/Platform append and facade/definition choices | `RealJsonAppendMaintainsDistinctOriginsAndForwarderChoices` |

The 17 small-fixture cases are PR-fast. The real multi-assembly case is
`Speed=Slow`, retained by daily Deep Inspect's unfiltered Queries suite.
It uses the existing loader's **implementation-pack** Platform view:
`System.Text.Json@10.0.0` followed by selected
`Microsoft.NETCore.App.Runtime.linux-x64@10.0.10` assemblies. Reference-view
population projection is covered separately below. The dedicated
[package-backed Platform source](package-backed-platform-realization.md)
owns reference-pack realization; adopting its correspondence is not permission
to label reference-pack files as the legacy loader's implementation view.
Other producer adapters and the complete Browser handoff remain successor
work; the CLI Router's transferred reference-population adapter is documented
below.

## Implemented reference-population admission

`WorkspaceReferenceDeclarationLoader` consumes the
[package-backed Platform source](package-backed-platform-realization.md);
it does not add another reference-pack resolver. `LoadAsync` takes either an
externally established exact reference coordinate or an unchanged source-issued
target selection, the selected population, work bounds, and a Package Source
operation. `Admit` accepts an already-realized source value, including one
returned by the House adapter. Neither operation selects a version, ranks a
producer, or changes reference bytes into implementation-view evidence.

The adapter's claim is explicit admission into the existing searchable
population: a successful source roster becomes one retained group and one
completed declaration context; a source or image-retention failure becomes an
attributed failed context without a partial roster. Request order is reserved
before asynchronous source work. A successful exact-assembly demand is complete
for that demand, not for the entire reference pack.

The general receipt now uses closed `WorkspaceDeclarationRequest`,
`WorkspaceDeclarationOrigin`, and `WorkspaceDeclarationFailure` alternatives.
The context-loader alternatives preserve its existing input, realized
coordinates, and failures. The reference alternatives preserve the source's
exact family target and population, reference entry path, candidate kind,
discovery observations, safe authority display, serving source identity,
source-attempt generation, package-content generation, and preceding package
failures. These are observation evidence, not a new portable acquisition
coordinate. In particular, reference admission does not extend or relabel the
legacy implementation-pack `RealizedMemberCoordinate.Platform`.

The logical Library coordinate remains Platform plus Metadata assembly
identity. Equal coordinates, equal bytes, or equal authority display labels
do not identify an occurrence. Admitting the same realization twice, or equal
coordinates from different sources, creates separate contexts and occurrences.
`WorkspaceDeclarationContext.Group` carries live group access; its optional
`ContextLoadOutcome` carries the original context-loader result only. Detached
receipts and locator answers retain source evidence instead of this live access.

Both adapters use the existing immutable-image retention and closed-world
binding construction. Each reference group has its own retained-image budget
and lends its retained images to Metadata through the existing scoped session
path. A rejected image names its source and entry. Cancellation stays
cancellation. A group committed before cancellation remains Workspace-owned,
as on the context-loader path. Package Source owns acquisition and consumes
its operation; the Workspace adapter releases operations rejected before that
transfer. Workspace close prevents later publication and drains admitted
groups and locator maintenance, not separately caller-owned source acquisition.

No additional resident lifecycle is introduced. Explicit reference completion
uses the same coherent publication/notification path, including upstream gaps.
An active locator maintains appended occurrences without another Find.
Earlier captures and answers stay unchanged; cold and resident queries over
the same capture retain the same choices and coverage.

Release gates live in `WorkspaceReferenceDeclarationLoaderTests`:

| Claim | Gate |
| --- | --- |
| Distinct source observations and retained access after source settlement | `ExactReferencesFromDistinctProducersRemainDistinctAndOutliveSourceSettlement` |
| Source-issued selection, automatic append, pinned earlier results and cold equivalence | `DiscoveredReferenceAppendMaintainsResidentLocatorAndPreservesEarlierEvidence` |
| Upstream failure remains visible beside healthy evidence | `SourceFailureRemainsVisibleBesideHealthyReference` |
| Image bounds produce an attributed failure, not a partial roster | `ZeroRetainedImageBudgetRejectsReferenceContextAtomically` |
| Cancellation and closed admission release the operation | `CancellationAndClosedWorkspaceReleasePackageOperations` |
| Real Package/reference observations and acquisition-free Find reuse | `RealPackageReferenceAppendAddsSecondJsonSerializerChoiceWithoutFindNetwork` |

The real-package case pins `System.Text.Json@10.0.0` beside
`Microsoft.NETCore.App.Ref@10.0.10` and is retained as `Speed=Slow` in the
existing unfiltered daily Queries suite. Small-fixture cases are PR-fast.
The shared [Sections projection](output-shapes.md#reverse-type-declaration-locator-projection)
consumes the reference alternatives; CLI Find and TypeScript/Browser production
adoption are steps 7 and 8 of the
[delivery map](reverse-type-locator-adoption.md). CLI Find is implemented;
TypeScript/Browser, direct local/project, and other producer adapters remain
separate.

## Implemented PlatformHouse population admission

The CLI Router consumes the source-neutral completed handoff from
`PlatformHouseSelectedReferencePopulationExecutor`. It transfers the exact
`ArtifactSetSession` and ordered `LibraryContentOwner` batch atomically through
`InspectionWorkspace.AdmitLibraryBatchAsync`; after acceptance, the Workspace
alone owns retirement.

`WorkspacePlatformPopulationDeclarationAdmission` then joins the accepted
Library admission receipt to the exact
`PlatformPopulationRealizationValue` and
`PlatformPopulationRealizationReceipt`. It validates Workspace identity,
receipt availability, member/occurrence order, exact Library identity,
Platform coordinate and target family, managed assembly identity, and
source-contribution target correspondence before publishing one declaration
context. It neither reacquires nor copies image bytes.

The context reads declarations through
`InspectionWorkspace.IssueLibraryOperation`. Each scoped operation invokes the
existing `LibraryTypeDeclarationInventoryInspection` and returns detached
inventory plus exact MVID. Inventory bounds and failures remain explicit;
unsupported Windows Metadata remains unsupported. The declaration receipt
retains Platform family, target framework, version, source capability,
population role, and assembly identity as separate evidence.

CLI Router evaluates all full-Type, Type-prefix, and exact-namespace requests
through one `TypeDeclarationLocatorInspection` envelope, then closes the
Workspace before using the detached answer. Workspace close preserves
Library-before-session retirement and reports cleanup failures. The adapter is
not a new Platform source, target selector, or general admission of every
Library batch.

Release gates in `PlatformTypeLocatorRoutingTests` use the installed reference
population to prove exact Type/member identity and MVID after Workspace
closure, exact namespace routing, stable Platform evidence, and no package
source activation on an installed success. Production Router tests retain
ambiguity, generic arity, namesake namespace, explicit-source bypass, and
complete-miss behavior.

## Implemented Package Scope declaration admission

Inspect Web is the first consumer of an active Workspace whose Package Scope
grows while one resident declaration locator remains active. Scope publication
continues to mean logical membership only. The Browser host explicitly submits
one exact current `WorkspacePackageOccurrenceDescriptor` to the Queries-owned
admission adapter; neither Scope publication nor Artifact Root publication
implicitly makes an occurrence searchable.

The admission input joins four owner-issued currencies without replacing any
of them: Workspace identity, Scope occurrence identity, Artifact Root
correspondence, and physical Root generation. Under Artifact Root composition
coordination, admission validates that the same open Workspace still owns the
exact Scope occurrence and that the submitted Ready projection identifies the
current Root generation. Foreign, removed/root-only, Pending, Failed, stale,
and closing inputs produce typed admission failures. A point-in-time Scope
snapshot is evidence for the request, not authority to reopen a historical
Root.

A successful admission borrows the current Root's already-realized
reference-preferred surface role. It does not reacquire package content,
repeat asset selection, create another role realization, or choose an
implementation participant. One retained Root query lease pins that
exact generation. One declaration context preserves all selected surface
participants in Package asset order; each member retains its exact
asset-to-participant association, selected asset path and target framework,
Metadata assembly identity, Package coordinate, producer, and assembly
selection provenance. A Ready Root with no surface role becomes one attributed
unrealized context carrying the Package selection status rather than a
complete empty population.

Admission publishes the complete context atomically as one population addition
and notifies the resident locator only after leaving publication coordination.
The exact occurrence identity and Root generation form the idempotence key:
repeating that admission returns its existing result and neither republishes
the context nor rereads declarations. Equal Package coordinates in distinct
Scope occurrences remain distinct because coordinate equality never replaces
the Scope-issued occurrence identity.

The retained Root query lease is live access, not part of the detached receipt
or locator answer. It allows the existing synchronous scoped Metadata borrow
to remain lazy until locator demand. Once activated, resident maintenance
inventories each newly admitted member at most once and reuses prior outcomes
under the existing bounds. Earlier receipts and answers remain unchanged.
Removal and replacement policy is not introduced here; an already admitted
occurrence's exact generation remains pinned under the append-only locator
lifecycle.

Workspace close first prevents publication and asks locator maintenance to
stop. It returns every retained Package lease only after that maintenance
drains, then allows Artifact Root release to complete. Projection-return
failures join the existing artifact cleanup report. Detached receipts and
locator results remain usable after close. This ordering uses the existing
cooperative async lifecycle and introduces no thread, filesystem, assembly
loading, Browser-private cache, or process-static state.

The focused Release gates for #7198 cover:

| Claim | Gate |
| --- | --- |
| Exact current admission, Package/asset order and origin preservation | `PackageScopeDeclarationAdmission_PreservesExactOccurrenceAndSurfaceAssets` |
| Foreign, removed/root-only, Pending, Failed and stale typed outcomes | `PackageScopeDeclarationAdmission_RejectsInputsWithoutCurrentAuthority` |
| Repeated exact admission and distinct equal-coordinate occurrences | `PackageScopeDeclarationAdmission_IsIdempotentByOccurrenceAndGeneration` |
| Lazy first demand, append maintenance and unchanged prior evidence | `PackageScopeDeclarationAdmission_MaintainsResidentLocatorWithoutRescanning` |
| No-surface selection gap remains attributed | `PackageScopeDeclarationAdmission_PreservesTypedRealizationGap` |
| Close drains locator work before returning the Root lease | `PackageScopeDeclarationAdmission_CloseReturnsLeaseAfterMaintenance` |
| Real Package evidence and distinct source choices | `PackageScopeDeclarationAdmission_RealJsonPackagesRemainDistinctChoices` |

The synthetic boundary cases are PR-fast. The real-package gate uses
`System.Text.Json@10.0.0` and a second source occurrence defining
`System.Text.Json.JsonSerializer`; it is `Speed=Slow` and remains in daily
Deep Inspect's unfiltered Queries suite. Browser transport, selection and
Type/Member navigation remain step 8 rather than becoming Queries behavior.

## Resident inventories and shared work

The resident unit is a detached Metadata declaration inventory associated with
one exact admitted occurrence/content correspondence and the inventory
semantics that produced it. It is not a cached match list for one name.
Changing a pattern or exposing an explicitly admitted visibility view does
not require reopening already inventoried content. The producer must retain
the visibility evidence needed by the admitted requests.

There is one in-flight construction per reuse identity. Concurrent requests
and append maintenance join that work. Stable successful inventories remain
resident for the active Workspace, and adding content does not rescan them.
Different occurrences never collapse because coordinates, filenames, or
display labels match. Sharing an underlying content inventory is allowed only
with owner-issued exact-content correspondence; the separate observations and
their source/context choices remain in every result.

The Workspace owns resource retention. Its adapter retains the Library/group
owners needed by registered occurrences; index construction takes ordinary
operation authority and scoped session borrows. A borrowed
`AssemblyInspectionSession` must not be stored in the index or escape its
callback. Keeping an owner-backed image available avoids reacquisition;
keeping the detached inventory avoids decoding again. These are distinct
forms of reuse, neither a process-static cache nor a live result handle.

Maintenance inherits the Workspace's explicit work/resource bounds. Activating
the facade opts into local declaration maintenance for subsequent admitted
additions, not network access, ecosystem expansion, implementation-view
acquisition, or unbounded work. Pending, failed, and work-bound occurrences
stay represented; a retention or work bound cannot silently evict an
inventory, omit a new occurrence, or turn an incomplete roster into a miss.
No correctness claim depends on a particular cache eviction or retry policy.

## Request revision and completion

Every admitted request captures one authoritative population receipt at its
admission point, including a first request that starts initialization. It
waits for the required inventories for **that receipt**, or for their explicit
terminal non-success outcomes. It does not keep chasing the newest Workspace.
Admission also reconciles the captured receipt, so delayed notifications do
not force the request to wait for an event that already coalesced.

Matching consumes exactly the captured roster and uses the stateless query.
Inventories produced for later additions cannot enter the earlier answer.
The returned envelope carries the captured population evidence, not whatever
revision happens to be current when matching finishes. There is no promise
that a response is still the newest population when its consumer receives it.
A consumer wanting a later population makes another request.

A completely evaluated receipt can still contain failed inventories or
upstream gaps. Use the existing locator completion and per-member outcome
contract: zero candidates with incomplete coverage is not absence. A newer
healthy occurrence does not hide an older failed one. No scalar/singleton
policy is introduced; cold and resident queries over the same receipt and
same inventory evidence return the same ordered candidate vectors.

Only immutable Metadata outcomes may be reused as declaration evidence.
Operational failure, cancellation, or a work-bound stop is not a reusable
empty inventory. Such outcomes remain visible for the affected attempt;
a later admitted attempt may retry unfinished work under the same owner
bounds, without rescanning successful entries. There is no automatic
unbounded retry loop. An unexpected service-wide error faults the affected
operation visibly and cannot be wrapped as a successful partial vector.
Implementation must define its explicit recovery boundary before claiming
recovery from a faulted maintenance operation.

Caller cancellation detaches that caller and propagates as cancellation.
It does not cancel Workspace-owned shared inventory work or another caller.
If every caller leaves, an activated facade still maintains later additions
until Workspace close or an explicit visible maintenance failure/bound.
This is deliberate resident-service behavior, not a caller-token cache.

## Workspace close

Close makes new locator admissions unavailable and detaches observation.
Pending requests terminate through the ordinary cancellation/closing outcome;
none can restart maintenance or admit another occurrence. Already admitted
inventory work must finish or acknowledge the owner's cancellation and
relinquish its borrows/operation authority before dependent resources release.

The facade participates in Workspace drain; it is not another owner that
disposes shared Library/session resources independently. Existing group and
Library owners retain their release ordering and release-failure reporting.
Close releases resident inventories and observer references after admitted
work drains. A delayed notification cannot resurrect the facade.

Already returned coordinate-plus-origin vectors remain detached values after
close. They neither keep the Workspace alive nor permit session access.
Reopening a selected observation continues through source authorization and
exact-context validation in the existing owners.

## Demo: one Workspace, two observations

This is a target interaction, **not current CLI syntax or shipped behavior**.
It uses the pinned real declarations documented in the
[locator evidence](reverse-type-declaration-locator.md#real-asset-demo-and-evidence):
`System.Text.Json@10.0.0` (`net10.0`) and the Platform reference assembly from
`Microsoft.NETCore.App.Ref@10.0.10`.

```text
admit Package System.Text.Json@10.0.0 from nuget.org
  -> population P1; no locator inventory yet
Find System.Text.Json.JsonSerializer
  -> activate; inventory the admitted Package occurrence
  -> P1: [Package coordinate + nuget.org origin + net10.0 context]
admit Platform System.Text.Json from the .NET 10.0.10 reference population
  -> population P2; automatically inventory only the new occurrence
Find System.Text.Json.JsonSerializer
  -> P2: [Package observation, Platform observation]
consumer chooses Platform observation -> Type -> exact Member
close Workspace
  -> stop observing, drain reads, release through owners
  -> previously returned P1 and P2 vectors remain ordinary detached data
```

If P2 arrives while the first Find is running, that request still returns P1;
the next request captures P2. If the addition's inventory fails, the P2 answer
retains the Package candidate and the attributed failure, not complete
coverage. For a declaration-kind neighbor, the same pinned pack defines
`System.Object` in `System.Runtime` and forwards it from `netstandard`;
maintenance must preserve both observations rather than applying routing
precedence.

## Required evidence and delivery

The [model](models/workspace-live-locator/README.md) checks the protocol within
its stated finite bounds. Its exact verdicts are enforced by
`eng/tla-expected-exit-codes.txt`. It does not prove source correspondence,
Metadata decoding, resource retention in C#, or single-threaded Wasm behavior.

Before product support, #6845 requires Release gates over the actual facade:

| Observable property | Required implementation gate |
| --- | --- |
| Lazy/resident work | An admitted real Package causes no scan before first demand; repeated patterns and concurrent callers scan its successful inventory once. |
| Coherent growth | Pause initialization, append the pinned Platform occurrence, resume, and observe maintenance without a second query; duplicate/coalesced notifications cause no duplicate scan. |
| Exact request revision | Pause the first query, append and inventory another occurrence, and compare its P1 result and a subsequent P2 result against cold queries for those exact receipts. |
| Honest failure and bounds | An added rejected/malformed or work-bound occurrence remains attributed alongside healthy candidates; retry unfinished work without rescanning healthy entries. |
| Shared cancellation and close | Cancel one waiter while another proceeds; close during a borrowed inventory read, then verify release waits, no post-close work starts, and old results remain usable as detached data. |
| Production adoption | CLI Find-to-Type/Member and retained Browser/Wasm Find-after-append use the same facade and preserve selected origin/context; no thread-pool dependency is assumed. |

Metadata borrowing/visibility (#6848), exact population projection (#6845),
and the cold query (#6849) precede the resident facade's executable delivery.
Issue #6845 is closed after the explicit-context and resident deliveries; #7077
tracks the package-backed reference adapter. Other producer adapters remain
explicit step 4 successors, not implied by the closed tracker. Common Sections
(#6846) is implemented; CLI (#6844) and Browser (#6851) adopt the
live facade along the existing nine-step map. The one-shot CLI uses a short
Workspace lifetime rather than a separate index. Local/project coordinate
support remains #6847. Old Platform lookup retirement remains #6850.
