# Inspection space architecture

The core of `dotnet-inspect` is not its decompiler, analysis engine, metadata
scanner, or any other individual fact producer. The core is the **inspection
space**: the shared environment in which subjects are resolved, inspection work
is requested and governed, results retain identity and provenance, and different
kinds of evidence can be composed.

Analysis, decompilation, metadata projection, source inspection, package
inspection, and future producers extend that space. They are add-ins in the
architectural sense: statically linked, NativeAOT-friendly producers behind
shared contracts, not dynamically loaded plugins.

## Status

This document describes the target core architecture and the principles that
govern its migration. Library metadata, direct-reference, extension-method,
custom-attribute, manifest-resource, type-forwarder, union-type, switch,
SourceLink, PDB-mapped-or-decompiled type/member source, and API-comparison
inspection plus implementation-relationship and type/member search inspection
are the first typed-query canaries: commands and
section catalogs plan typed demand while queries remain independent of
acquisition and output. The `diff` Changes section consumes one API-comparison result over
host-resolved surfaces, retaining Metadata-owned Finding correspondence and
compatibility classification without coupling the query to endpoint acquisition
or output.
The library CLI and package
`--all-libraries` now use an ephemeral workspace for focused Integrations
demand. One binding-consistent assembly context group per binding universe
scans selected participants sequentially, preserves per-assembly identity,
provenance, and failures, and retains each available immutable snapshot for the
rest of that library inspection without reopening the source path. Package
`--all-libraries` partitions binding universes by package asset directory,
preserving non-`net*` framework and runtime contexts, and releases each
participant before advancing. For a remote package whose default selection
resolves one target framework, that grouped path now consumes the shared
artifact-backed package-role realization: the existing visible surface
selection remains the input to ordinary library inspection while Integration
queries use its exact body-bearing implementation participant when one exists.
A rejected implementation remains visible without erasing an available
surface, and asynchronous workspace close retains the artifact generation
until both participant groups settle. Ordinary presentation retains the
selected extraction file's timestamp rather than manufacturing a timestamp
from the artifact stream. The artifact-backed path requires the
binding's frozen surface role to exactly cover the command's visible selection
and to form exact assembly-identity correspondence; other package shapes
retain the legacy grouped workspace. Local archives and explicit `--tfm` modes
also keep that path because their visible selection can span tools or multiple
package layout roles. Progressive member call
graphs now run over the same group: they build Analysis indexes from retained
snapshots, keep one cross-assembly catalog generation for both traversal
directions, and remain independent of rendering. Group-scoped optimization
ranking also builds Analysis indexes from retained snapshots, resolves
cross-assembly metadata only to selected siblings under each participant's
binding policy, attributes bodies to public API owners, and returns one stable
product-owned order across the group. Seeded structural-clone retrieval binds
one exact seed participant and one explicit candidate participant, keeps both
retained snapshots alive for one same-image or cross-image Analysis call, and
returns the product result unchanged beside both subjects' identity and
provenance. Exact method analysis reads signals,
allocations, direct calls, unsafe evidence, exception regions, opportunities,
and diagnostics from one physical MethodDef body without exposing the snapshot
or Analysis index to its consumer. Analysis index execution remains sequential,
preserving the Browser/Wasm baseline. The `extensions`,
`implements`, and `find` commands also execute typed queries through ephemeral
workspaces. Ordinary search fan-out creates and disposes one-participant groups
sequentially; explicit extension reachability uses one binding-consistent group
so its name index, lazy member-edge traversal, and extension census observe the
same retained participant images. That group retains the workspace's bounded
image budget; participants rejected by acquisition or the budget remain visible
as extension and reachability warnings rather than silently shortening the
search. Other foundations include shared image and inspection session ownership,
catalog generations, `CoreCache`, typed provenance and resolution currencies,
and `InertString`; the remaining workspace model describes how those pieces
will be composed.

Patternless `find --package-prefix` is a package-space query rather than an
assembly workspace query. It streams bounded typed match, failure, and
completion events from source-owned search metadata and exact manifests.
Search supplies owners and candidate provenance; the manifest supplies authors
and declared dependency groups. A network-free `PackageManifestFactsQuery`
validates each bounded manifest and projects one immutable fact model that both
package-profile and package-content dependency queries consume. In addition to
the 1 MiB transport and 512 KiB decoded-document bounds, that projection admits
at most 32,768 UTF-16 code units per scalar value, 128 package types, 1,024
dependency groups, and 4,096 dependencies; a violation is an invalid-manifest
failure rather than a partial fact set. Root and metadata elements accept the
known nuspec namespaces whether the default namespace is declared on
`package` or, for legacy manifests, directly on `metadata`; other document
roots and namespaces are rejected. The query-bound `Packages` section owns
package/dependency row
expansion, schema, projection, and visible failure or truncation rows. It
applies an explicit row window before constructing rows and shares contained
package-level cells across dependency rows. The CLI registry materializes the
event stream once within query execution, while the public L1 streaming API
remains available to incremental hosts. The CLI owns acquisition authorization
and format selection. The profile does not acquire package archives or
assemblies, and hosts may present each L1 match incrementally before any later
package drill-in.

The Integration graph now accepts finite explicit-subject induced-set requests
over one realized context group. Workspace scope still decides which
participants may contribute. The request independently selects typed subjects,
relationship producers, and the both-endpoint subject-closure rule; it does not
create seeds or widen acquisition. Subject identities and workspace membership
are validated before the selected producers run. Exact package and assembly
identities are checked against the realized boundary; acquired types and
members are checked against definitions and structured extension-member
anchors in the retained participant image. A registration match alone is not
membership. The selected relationship set then drives one deterministic
registry plan regardless of input count, and the existing sequential group
executor remains the Browser/Wasm-compatible baseline.
`Execute_ExplicitSubjectCountDoesNotMultiplyProducerDemand` gates that planning
contract; `Execute_RejectsExplicitSubjectOutsideWorkspaceWithGuidance`,
`Execute_RejectsUndeclaredInScopeTypeBeforeProducerExecution`, and
`Execute_RejectsUndeclaredInScopeMemberBeforeProducerExecution` gate exact
membership; `Execute_ReportsMemberPreflightDecodeFailureBeforeProducers` gates
visible artifact failure, and
`Execute_ReportsTypeDeclarationRejectionBeforeProducers` gates typed metadata
rejection at the preflight boundary.

The desktop CLI now consumes that architecture through
`graph integrations`. Repeated package coordinates and one shared target
framework lower through `WorkspaceContextLoader` to exactly one group; the
realized package identities become the explicit induced subjects, and the
command passes the selected Integration relationship descriptors directly to
the query. The command does not invent a CLI graph IR, infer identity from
labels, or add direction/depth to induction. Markdown, tree, Mermaid, tabular,
and JSON output are presentation projections over the resulting
`InspectionGraphDocument`. Typed loader and graph failures remain visible, and
the sequential registry executor remains the command's execution policy.
`InspectionGraphCommandTests.ExecuteAsync_UsesExactPackageSetAndStructuredRequest`
gates this composition.

Mechanism-specific documents remain authoritative for the current behavior,
target design, and verification they own. In particular:

- [Assembly context group lifecycle model](models/assembly-context-group-lifecycle/README.md)
  checks the current callback admission, participant-local image opening,
  retained-image budget, disposal, and quiescent release protocol. Its bounded
  TLC evidence supplements rather than replaces the named Release tests.
- [Inspection layers](design/inspection-layers.md) owns host and query-layer
  boundaries.
- [Assembly inspection query model](design/assembly-inspection-query.md) owns
  the resolution-to-inspection currency.
- [Type, member, and API representation](design/type-member-api-representation.md)
  owns the map of lookup, shape, address, resolution, and correspondence
  currencies.
- [Finding coordinates](design/finding-coordinates.md) owns subject,
  correspondence, order, and provenance axes for Findings.
- [Member body substrate](design/member-body-substrate.md) owns body-local
  coordinates and bound versus portable source projections.
- [Type forwarding resolution](design/type-forwarding-resolution.md) owns
  catalog generations and definition correspondence.
- [Progressive disclosure](design/progressive-disclosure.md) owns current
  command backpressure and defaults.
- [Cache concurrency and publication](design/cache-concurrency.md) owns package
  cache coordination and atomic publication.
- [InertText](design/inert-text.md) owns treated-text semantics and gates.
- [Untrusted data threat model](design/untrusted-data-threat-model.md) owns
  security boundaries and priorities.

The complete end-to-end claim that every presentation path accepts only inert
artifact text remains **unverified**; the InertText document records the
remaining boundary enumeration.

The complete content-authorization claim is also target behavior. The current
package implementation may read source-blind global-folder content and derive
version candidates from content caches. The cache document records that
deviation under
[#3752](https://github.com/richlander/dotnet-inspect/issues/3752), with
provenance-matched payload work in
[#3767](https://github.com/richlander/dotnet-inspect/pull/3767).

## Goals: Rich, Fast, Safe

The inspection space is organized around three goals:

```text
                         Rich
                    deep · broad · joined
                       /           \
                      /             \
                  Fast ------------- Safe
             demand · reuse     typed · bounded
```

None is subordinate to another. A design that is rich but eagerly computes
everything is not fast. A cache that is fast but can return bytes from the wrong
producer is not safe. A safety transform that destroys identity or evidence is
not rich.

### Rich

Richness has three dimensions.

#### Deep data for each inspection type

An inspection type should expose the evidence needed to answer its question,
not only the first summary the CLI happens to render. Typed identities,
provenance, failures, and producer-native detail remain available for focused
queries and later composition.

Depth does not require a large default document. The result can be rich while a
host initially requests or renders a compact projection.

#### Many inspection types

The space admits many typed query/result pairs: package facts, API surfaces,
source provenance, integrations, dependencies, metadata, implementation facts,
performance evidence, decompiled source, and others.

The core does not gain one branch per type. A producer declares what it needs,
what it costs, what scope it runs over, and what typed result it returns. Adding
an inspection type extends the catalog without changing workspace semantics.

#### Shared foundations enable joins

The most valuable answers often cross producer boundaries:

- integrations across companion assemblies;
- package and assembly provenance;
- API identity joined with source or implementation evidence;
- analysis observations projected onto decompiled source;
- comparisons across framework or package-version contexts.

Those joins must use shared typed identity, coordinates, provenance, and context
boundaries. Display text is not identity, and a renderer is not a composition
layer. The shared foundation does not impose one universal identity. It gives
each domain enough context to issue the correspondence currency its joins
require.

### Fast

Fast does not mean parallel. The first intended execution target for the
workspace is a single-threaded Wasm host, and sequential execution remains a
supported, deterministic policy everywhere.

The architecture gets speed from avoiding work:

- **Demand-driven planning.** Run only the queries and prerequisites requested
  by the host.
- **Progressive acquisition.** Permit cheap, high-value results before
  expensive or exhaustive layers.
- **Shared lifetimes.** Open or materialize one artifact generation once, then
  reuse it through owner-controlled leases across the queries that consume it.
- **Frozen contexts.** Reuse binding and correspondence work inside an explicit
  catalog generation and the resolution and authorization policy snapshot that
  produced it; advance the generation rather than mutating its answers.
- **Semantic caching.** Key reusable results by every input that can change
  their meaning, including content, source authorization, options, and producer
  version where applicable.
- **Single-flight and atomic publication.** Share equivalent in-process work
  and never expose partially published persistent content.
- **Budgets.** Bound graph traversal, retained content, output, network work,
  and other input-amplified costs.

Concurrency is an executor policy layered on this model. A native host may run
independent assembly work concurrently, but the same plan must also run
sequentially with identical result ordering and failure semantics.

### Safe

The inspection space accepts artifacts as untrusted data, never as code or
authority.

- Inspected assemblies are parsed, not loaded.
- Resolution carries typed identity and provenance rather than handing
  inspection a bare path.
- Availability is not authorization. Cached or retained content is usable only
  when the current request authorizes its producer and coordinate.
- Network, source-content, and unbounded work require explicit capability and
  cost authorization.
- Malformed input, failed acquisition, and incomplete analysis remain visible
  as typed outcomes.
- Cache hits are valid only when the current request authorizes and identifies
  the stored result.
- Reader-local handles, catalog keys, and other bound currencies cannot outlive
  or be interpreted outside the owner scope that gives them meaning.
- Equality, path spelling, display text, and durable addresses do not substitute
  for owner-issued correspondence.
- Artifact-derived work is bounded so hostile input cannot silently turn a
  small request into unlimited CPU, memory, network, or output.
- Artifact text remains exact while it participates in identity and control
  flow, then crosses a structural presentation boundary as `InertString`.
  Format-specific escaping composes after that; it does not replace inertness.

Safety preserves evidence. `InertString` visually encodes rather than deleting
hostile text, and typed rejection refuses invalid input rather than repairing it
into a plausible answer.

## The inspection space

Conceptually, an inspection run combines three things:

```text
inspection space = inspection contexts × requested queries × execution policy
```

The product does not materialize that Cartesian product. The plan selects a
small demand-driven path through it. **Inspection context** is a conceptual
role, not a shared base type. Assembly-backed contexts come from assembly
context groups. Feed discovery, package metadata, and other operations that do
not inspect assemblies may use narrower source or artifact contexts without
creating a fake assembly group.

```text
CLI · Wasm · agent · service host
                |
                | typed requests
                v
       +----------------------+
       |   Inspection plan    |
       | scope · cost · caps  |
       | dependencies · budget|
       +----------+-----------+
                  |
                  v
       +----------------------+
       |  Inspection contexts |
       | assembly groups      |
       | source · artifact    |
       | identity · provenance|
       +----------+-----------+
                  |
          sequential baseline
          optional concurrency
                  |
        +---------+----------+
        |                    |
        v                    v
 metadata · packages   analysis · decompiler
 source · APIs         research · future producers
        |                    |
        +---------+----------+
                  |
                  v
       typed results · failures
       identity · provenance
                  |
                  v
       sections · shapes · formats
       inert text · structural escaping
```

### Workspace

Every invocation that inspects assemblies should use a workspace internally.
For the CLI it is normally ephemeral; a Wasm or service host may retain it and
run several query plans over the same acquired content.

An operation that does not inspect assemblies need not create an empty
workspace. It receives the narrow source, artifact, or request context declared
by its query. A discovery query may return typed inputs that an authorized later
stage uses to create a workspace; unresolved discovery terms do not masquerade
as a binding-consistent assembly group.

A workspace contains one or more **assembly context groups**. A group is one
binding-consistent universe: root assemblies, dependency assemblies, target
framework and runtime identity when known, and the resolution policy that chose
them. It also retains the acquisition provenance and policy inputs needed to
decide whether a query may use their content. Authorization remains a decision
for the current query plan, not a permanent property of the group.

[Workspace Scope and Expansion](design/workspace-scope-and-expansion.md) owns
the committed logical Root occurrences above those physical contexts,
closed-by-default selective dependency expansion, revision-bound scope edits,
and closure completeness. Artifact Acquisition retains realization, admission,
binding-context publication, query authorization, and physical lifetime.

Queries may cross assembly boundaries within a group. They must not infer a
relationship across groups. Multiple groups support comparisons such as two
package versions or framework contexts without mixing their bindings.

The first implemented group contract owns typed
`ResolvedAssemblyReference` participants paired with their binding-policy
snapshots. Every participant in a group must carry the same binding-policy
version identity. The group acquires each selected image lazily, validates it
against its descriptor, and retains one immutable, non-pooled byte snapshot
under a cumulative group budget reserved before snapshot allocation. Metadata
exposes that bounded immutable snapshot acquisition as a narrow public
capability; raw PE and metadata readers remain private, and Queries receives no
friend access to Metadata. Callback access receives a scoped stack-only image
view; direct access returns a stack-only read-only span result. Asynchronous
host work receives a descriptor whose opener retains that same immutable image,
so suspension does not reopen a mutable path. Disposing the group prevents new
access and releases its retained references after active callbacks complete,
but it never attempts to revoke or recycle an already returned span or retained
descriptor.
`InspectionWorkspaceTests` gates policy-version consistency, immutable snapshot
isolation, callback and span lifetimes, concurrent disposal, bounded retention,
per-participant single-flight acquisition, and typed acquisition failures.
When stream inspection is already propagating cancellation or a fatal failure,
owned-stream cleanup cannot replace that primary failure.
`AssemblyContextSourceQueryTests.SnapshotPrimaryFailure_IsNotMaskedByCleanupFailure`
gates the end-to-end member and type paths, while
`PdbContextDescriptorTests.DescriptorOpenPrimaryFailure_IsNotMaskedByCleanupFailure`
gates descriptor-backed metadata contexts, assembly images, and prefetched
sessions, and
`PdbContextDescriptorTests.PdbContextConstructionPrimaryFailure_ReleasesOwnedStream`
gates the post-reader context-construction boundary.
`InspectionWorkspaceTests.OwnedResources_AreDisposedBeforeSnapshots` gates the
derived-resource-before-snapshot disposal order.
`InspectionWorkspaceTests.AsyncParticipantRelease_PreservesOwnedResourceDisposalOrder`
gates that order when asynchronous host work releases its participant.
`InspectionWorkspaceTests.ConcurrentDisposal_AfterAsyncCallbackEnds_PreservesOwnedResourceDisposalOrder`
gates the disposal race after callback completion but before participant
release.
`InspectionWorkspaceTests.WorkspaceDisposal_ContinuesAfterAGroupFails` gates
all-group cleanup after an owned-resource failure, and
`InspectionWorkspaceTests.CallbackFailure_IsPreservedWhenDeferredDisposalAlsoFails`
gates preservation of an in-flight callback failure when deferred cleanup also
fails.

#### Workspace close and group release authority

`InspectionWorkspace` owns whether new assembly-context group construction may
begin, whether a completed group may enter the workspace registry, and when
workspace close is complete. It does not follow from that ownership that the
workspace directly disposes every group. A group has exactly one terminal
release completion, selected before construction begins:

| Registration kind | Terminal release authority | Workspace close behavior |
| --- | --- | --- |
| Direct | Workspace-owned release completion | Request release and await the group's quiescent terminal result |
| Coordinated | Adjacent owner-issued release completion | Close workspace participation and await that same completion; never call `AssemblyContextGroup.Dispose()` independently |

The coordinated form is the handoff used by package-role completion. The
package-role owner supplies its keyed release-completion cell and owns the
cleanup record it produces. Exact-request admission owns its request leases,
cache closure, and the decision that authorizes package-role release. The
workspace consumes only a narrow participation handle: it can close workspace
admission, route a late group into cleanup, and await the owner-issued terminal
completion. It does not inspect lease counts, reconstruct package-role group
ids, or reinterpret the cleanup result.

Group construction begins by atomically registering one opaque
`WorkspaceGroupAdmission` while the workspace is open. The admission records
the release kind and exact release completion before any potentially awaited
construction work. It is single-use and belongs to one workspace. Completion
has two outcomes:

- while the workspace remains open, the exact constructed group and release
  completion publish as one registry entry; or
- after close begins, the group never publishes or becomes available to a new
  query. It transfers directly into the recorded release path, and workspace
  close awaits that cleanup.

Failure or cancellation before a group exists completes the admission without
inventing a group cleanup result. Its primary outcome remains owned by the
constructing operation. If a group was returned, however, cleanup is a
workspace obligation even when the operation ignored cancellation. A caller
cannot abandon the admission ticket and leave an unregistered group outside
both the registry and cleanup.

Workspace close is one monotonic `Open` -> `Closing` -> `Closed` transition.
The first `CloseAsync` call closes new admission under the workspace gate,
captures every published registration and in-flight admission, and creates one
shared completion. Later close calls return the same eventual
`InspectionWorkspaceCloseReport`; close accepts no cancellation token after it
starts. Direct registrations request their workspace-owned release.
Coordinated registrations receive the workspace-close signal through their
owner-issued participation handle and retain existing lease-holder access until
that owner authorizes terminal release. Both forms close new group access
before releasing resources, and actual release remains subject to the existing
`AssemblyContextGroup` callback and owned-resource quiescence contract.

The adjacent owner may authorize coordinated release before workspace close,
such as when an explicit package-role session closes first. That transition
atomically removes the registration from active workspace use, prevents new
lease or query admission through it, and retains its historical registration
and terminal completion for the eventual workspace report. A later workspace
close observes the same completion; it neither reactivates nor releases the
group again.

Close awaits every admission that was in flight when closure began, every
late-result cleanup path, and every group release completion. One failed group
does not prevent another release from being requested or observed. The final
report retains one workspace registration identity and exact terminal release
result for every group that reached workspace ownership, in registration
order. That historical domain is immutable: owner-first release cannot remove
an entry, while failed or canceled construction that produced no group never
enters the domain and cannot acquire cleanup data. A coordinated entry retains
the adjacent owner's typed cleanup result; the workspace does not flatten it
into exception text or a second cleanup taxonomy. Expected cleanup failures are
data in the report. The report becomes available only after all entries are
terminal and is the same immutable instance returned by every close call.

Workspace construction selects its lifetime mode before any group admission.
The existing public construction path creates a synchronous-compatibility
workspace. It continues to accept the current synchronous direct and
package-role construction APIs. A coordinated registration in that mode must
provide a synchronous request-release adapter over the same owner-issued
completion retained by the package-role session; workspace disposal requests
that path exactly once and never independently disposes the group.

The synchronous compatibility path preserves the existing `IDisposable`
boundary, not the target complete-report contract. `Dispose()` closes new
workspace access and requests every direct or coordinated release before
returning, but it does not block for quiescent completion or return the eventual
report. Deferred release continues only through the already-owned group
callback and release-completion state machine; the adapter starts no task or
background work. Expected synchronous request failures retain the current
throwing compatibility behavior. New retained or shared hosts instead use an
explicit asynchronous construction path whose close is awaitable and reports
every terminal result.

On an asynchronous workspace, `DisposeAsync` awaits the same close completion
and exposes its report through the workspace rather than throwing expected
cleanup failures that could replace a primary exception from an `await using`
body. On a synchronous-compatibility workspace, `DisposeAsync` performs the
same release request as `Dispose()` so generic asynchronous disposal remains
compatible. Callers that need to branch on cleanup use `CloseAsync` and inspect
its returned report.

Lifetime-mode enforcement is fail-before-mutation. A
synchronous-compatibility workspace rejects construction that requires an
awaited admission or lacks a synchronous request-release adapter before that
construction begins. Calling synchronous `Dispose()` on an asynchronous
workspace throws `InvalidOperationException` before changing workspace state
and directs the caller to asynchronous close. The validity of `Dispose()`
therefore never depends on a race with later registration. Synchronous
disposal never blocks a thread on a task, starts fire-and-forget cleanup, or
leaves a half-closed workspace after rejecting the path. Its accepted
compatibility path records a durable release request before returning; it does
not launch an unobserved task or transfer progress to a background thread.

The state transitions are short synchronous updates under the workspace gate.
No gate is held across user or owner callbacks, group release, or an `await`.
Progress resumes through ordinary task continuations and requires neither
`Task.Run` nor a background thread, preserving the single-threaded
Browser/Wasm execution target.

[`InspectionWorkspaceClose.tla`](models/inspection-workspace-close/InspectionWorkspaceClose.tla)
models this workspace-level interaction. It covers direct and coordinated
release ownership, close racing construction, lease-draining authorization,
group quiescence, complete failure reporting, and eventual asynchronous close.
It treats package admission and coordinated release as adjacent abstract
completions whose contracts remain owned by
`docs/design/inspection-layers.md`. Its direct-group path instantiates the
exact-group request,
terminal receipt, and result lifecycle in
[`AssemblyContextGroupReleaseLifecycle.tla`](models/assembly-context-group-lifecycle/AssemblyContextGroupReleaseLifecycle.tla);
callback and resource internals remain in the detailed
[`AssemblyContextGroupLifecycle.tla`](models/assembly-context-group-lifecycle/AssemblyContextGroupLifecycle.tla)
model. The model checks the interaction contract. The Release gates below
enforce the shipped close mechanics; exact direct-receipt attribution remains
unverified by a fault-injection implementation gate.

The direct and coordinated workspace-close paths are implemented. The
parameterless constructor retains synchronous compatibility.
`CreateAsynchronous()` selects the awaited lifetime before admission,
`CloseAsync()` returns one shared `Task<InspectionWorkspaceCloseReport>`,
`DisposeAsync()` awaits that task, and `CloseReport` exposes the same immutable
report after completion. Each direct group has one release completion. An
asynchronous workspace captures that outcome as an
`InspectionWorkspaceDirectGroupCloseResult`; synchronous compatibility
continues to throw the same cleanup failure while requesting the same
group-owned release.

The direct implementation is enforced by these Release gates:

- `WorkspaceClose_RejectsAdmissionAndRoutesLateGroupToRelease` closes admission
  atomically, prevents a late construction result from publishing, and retains
  its cleanup result;
- `WorkspaceClose_NoGroupFailureSettlesAdmissionWithoutCleanupEntry` proves a
  construction failure after admission cannot strand close or invent a group
  cleanup record;
- `WorkspaceClose_AwaitsAllGroupCompletionsAndReportsEveryFailure` proves
  callback/group quiescence, attempt-all cleanup, stable ordering, and complete
  failure retention;
- `WorkspaceClose_ConcurrentCallersShareCompletionAndReportInstance` proves
  repeated and concurrent close calls join one task and receive the same
  immutable report object;
- `WorkspaceDispose_CompatibilityUsesSharedReleaseAuthority` proves
  asynchronous `Dispose()` rejection is fail-before-mutation and synchronous
  compatibility retains its throwing behavior through the group-owned release
  completion; and
- `WorkspaceClose_BrowserWasmUsesAwaitedProgressWithoutThreadBlocking` proves
  close rejects new work immediately, preserves an already-admitted callback,
  and reaches terminal close through awaited progress without a blocking wait
  or background-thread requirement.

Shareable package-role completion uses the coordinated path. It batch-registers
every planned physical group before awaited construction, pre-issues the exact
`PackageRoleGroupId` and terminal `PackageRoleCleanupReport` task, and closes
projection admission through a workspace-owned gate before owner release is
requested. The workspace never adds those groups to its direct-release set.
Package-role completion remains their sole physical release authority, while
`InspectionWorkspaceCoordinatedGroupCloseResult<PackageRoleGroupCleanupRecord>`
retains the exact keyed cleanup record without translating it.

The shareable completion operation requires `CreateAsynchronous()` because its
construction has awaited admission and it does not provide a synchronous
request-release adapter. The synchronous caller-owned
`CreatePackageAssemblyContextRoles` path remains unchanged.

The coordinated composition is enforced by these Release gates:

- `WorkspaceClose_DirectAndCoordinatedGroupsReleaseExactlyOnce`;
- `WorkspaceClose_ExistingCoordinatedLeaseRemainsUsableUntilOwnerRelease`; and
- `WorkspaceClose_OwnerFirstReleaseDeactivatesRegistrationAndRetainsReport`.

`WorkspaceClose_CoordinatedLateGroupsCommitHistoryBeforeOwnerRelease` proves
that close racing a separate-topology construction records both planned
admissions in registration order before dispatching their shared owner release,
returns no completion to the late caller, and retains both exact keyed cleanup
records.

This contract does not define package admission keys, cache policy, package
selection, role planning, participant projection, package cleanup-record shape,
artifact acquisition lifetime, query-specific participant release policy, or
the implementation of
[#4960](https://github.com/richlander/dotnet-inspect/issues/4960).

#### Retained package-realization caller

**Status:** no approved product caller.

The `inspect-web` prototype is the only current multi-operation consumer of
package roles. Its `BrowserPackageWorkspace` retains a bounded registry of
complete `BrowserInspectionScope` instances keyed by an exact
package-coordinate set; the prototype's README owns that retention and eviction
policy. Each scope owns one `InspectionWorkspace` and one package-role
realization. The registry returns the already-open scope for a later exact
request, so the workspace never receives a second independent package-role
demand and cannot exercise workspace-local exact-request admission.

Moving reuse below that boundary would be a product-topology migration, not a
narrow caller adoption. A Browser-session owner would need to adopt the landed
demand-projection and coordinated-release contracts across the prototype:
replace retained whole scopes with independently returned demand projections,
migrate every scope query to projection-safe access, attach package-archive
retention to the shared completion instead of one demand, and define awaited
session reset or shutdown so retained entries eventually close.

A one-request workspace beneath each existing registry key cannot receive a
second independent exact demand because the outer registry returns the retained
scope first. An Integrations-only workspace would duplicate the ordinary
realization solely to resubmit a demand that Integrations already answers from
the retained scope. Neither topology adds an independently useful product
lifetime.

The retained Browser platform path is not a package-role caller: each cumulative
rebuild creates a fresh `InspectionWorkspace` through `WorkspaceContextLoader`
instead of submitting repeated package-role demands. The CLI, the only shipped
host, remains operation-scoped. Therefore no existing component justifies the
lower-level retained cache, and
[#4960](https://github.com/richlander/dotnet-inspect/issues/4960) remains
deferred. A future caller proposal must establish its product lifetime first,
including explicit bounds for retained or in-flight exact requests, concurrent
physical operations, and aggregate retained-byte reservation, and must name one
real repeated exact-demand scenario plus one neighboring distinct-demand
scenario. Once that caller is approved, the admission implementation owns the
observable ready-reuse, non-hit, capacity-rejection, and terminal-cleanup gates
through that caller. Admission must not be implemented or a caller lifetime
manufactured solely to make those gates pass.

`WorkspaceContextLoader` now realizes package, platform, and embedded
coordinates without requiring a filesystem. A platform coordinate maps the
`runtime` or `aspnetcore` family to its product-owned implementation-pack
coordinate, selects the latest authorized version on the target framework's
major/minor release line unless exactly pinned, and mints pathless participants
with `PlatformAsset` provenance. A platform-qualified target such as
`net10.0-browser` uses its `net10.0` base release line, and one family cannot
mix versions or producers inside a group. Floating selection retains only the
authorized producers that reported the selected version. Every authorized
HTTP producer must first return an authoritative listing or prove the package
absent; a failed producer makes the floating result unavailable rather than
silently narrowing the candidate set. Local-folder and `file://` sources remain
outside this remote listing evidence set. The implementation-pack RID is `linux-x64`
because the assemblies are inspected as representative CoreCLR IL and never
executed; the workspace target RID remains the caller's independent binding
constraint. `WorkspaceContextLoaderTests.PlatformMember_ResolvesFrameworkMatchedVersionAndRealizesContentParticipants`
gates version selection, pathless platform provenance, and in-group platform
binding; `PlatformMember_PlatformQualifiedTargetUsesBaseReleaseLine` gates
qualified targets, `FloatingPlatformMember_AcquiresOnlyFromVersionReporters`
gates source correspondence, and
`FloatingPlatformMember_HttpSourceFailureIsUnavailable` with
`FloatingPlatformMember_AuthoritativeAbsenceDoesNotHideReporter` gates the
failure-versus-absence distinction. `InvalidPlatformCoordinate_UsesPlatformDiagnostic`
gates the platform-owned public diagnostic boundary while retaining package
detail in host logging, and
`RealizedPlatformCoordinate_ReacquiresRecordedProducer` gates exact
producer-bound transport.
`FloatingPlatformMember_MixedMalformedCriticalResourceIsUnavailable` prevents
a valid service-index sibling from masking a malformed critical resource, and
`PackageCoordinateResolverTests.FloatingCoordinate_SkipsNonHttpSource` gates
the same non-HTTP exclusion for floating package members.
Portable-PDB acquisition now follows the same content-shaped boundary:
`AcquiredPortablePdb` opens repeatable content from a host-supplied `IPdbStore`,
and `PdbAcquisitionService` can load it for a pathless
`ResolvedAssemblyReference`. That explicit-capability descriptor overload
requires the store and package-source authorization; the legacy desktop
descriptor overload remains path-bound. The filesystem store supplies the
compatibility path used by desktop decompiler callers; an in-memory store
supplies the same validated PDB bytes to browser/Wasm hosts, paired with the
same explicit `IPackageSourceAuthorization` used for package acquisition.
Stored Portable PDB content is keyed by GUID plus stamp, so the content
reference remains repeatable across otherwise-colliding symbol-server lookup
keys. `AssemblyContextSourceQuery` now consumes that seam for one selected group
participant. Type requests carry an exact `MetadataTypeDefinitionName`; member
requests add a `MemberAnchor` and MethodDef token, so target identity is never
recovered from display text. The query resolves that target against the
participant's retained immutable image, then takes a content-backed reference
for asynchronous source work without consuming the participant snapshot.
Checksum-verified PDB source wins when available. Otherwise the
Decompiler runs over the same content reference and the participant's frozen
`IAssemblyBindingPolicy`; a PDB-source integrity failure remains attached to a
successful decompiled result rather than being rewritten as absence. When both
producers are unavailable, the result carries both typed attempts instead of
empty text.

The query's moderated network work requires explicit symbol HTTP, `IPdbStore`,
`IPackageSourceAuthorization`, source-fetch, and source-content-store
capabilities. `InMemoryPdbStore` and `InMemorySourceContentStore` provide the
filesystem-free host shape. Absolute source paths recorded in a PDB are
disabled by default at this boundary and require an explicit opt-in.
`AssemblyContextSourceQueryTests.PathlessMember_AcquiresVerifiedPdbSource`,
`MissingPdbSource_FallsBackToDecompiler`,
`PdbSourceIntegrityFailure_IsPreservedBesideDecompiler`, and
`NeitherSourceAvailable_ReturnsTypedFailure` gate these claims.

`PackageIntegrationsWorkspaceTests.Create_PartitionsTfmsAndRetainsParticipantGeneration`
gates asynchronous host work over a retained descriptor without reopening its
source.
`LayeringTests.Metadata_FriendsOnlyTestAssemblies` gates the absence of
production Metadata friends.

`AssemblyContextIntegrationsQuery` is the first query over a complete group. It
visits participants in their immutable input order, borrows a narrow
`AssemblyInspectionSession` over each retained snapshot without reopening the
source, and returns the ecosystem and OpenTelemetry evidence owned by Metadata.
Each entry carries an opaque participant registration, assembly identity,
resolution provenance, and either materialized evidence or a typed failure.
Partial inspection is therefore explicit and meaningful. The query is
`Unbounded`: the group byte budget bounds retained content, but participant
count and metadata scanning work still require explicit demand. The baseline
executor remains sequential; cancellation-aware and concurrent group executors
are later policy work. Late malformed-metadata mapping and preflight
metadata-overflow isolation are gated by the package command tests named
below.
`AssemblyContextIntegrationsQueryTests.RegistryRun_ScansEveryParticipantInOrderAndReusesSnapshots`
and
`AssemblyContextIntegrationsQueryTests.Execute_CarriesAcquisitionFailureBesideLaterResults`
gate participant ordering, snapshot reuse, and general partial acquisition.
`AssemblyContextIntegrationsQueryTests.Execute_ReportsBudgetExhaustionAsIncompleteEntry`
gates the budget-limited case.

`AssemblyContextIntegrationOpportunitiesQuery` is the first dependent group
query. It declares the Integrations result as a typed prerequisite, derives the
set of already-present integrations from that result, and scans each available
participant's same retained snapshot for missing registration surfaces.
Rejected and failed prerequisite entries remain explicit in the dependent
result. The query's local cost is `NetworkFree`, while registry planning exposes
the `Unbounded` transitive cost of its Integrations prerequisite.
`AssemblyContextIntegrationsQueryTests.Execute_ComposesOpportunitiesFromTypedIntegrations`
and
`AssemblyContextIntegrationsQueryTests.RegistryRun_OpportunityQueryUsesOneImmutableSnapshot`
gate prerequisite composition, existing-integration suppression, and snapshot
reuse.

Package `--all-libraries` constructs one
`SourceRelativeAssemblyGroupBindingPolicy` per selected package asset directory
and passes that shared policy snapshot to every participant in that group.
`--tfm all` therefore creates separate groups for distinct framework, asset-kind,
and runtime directories rather than mixing binding universes. The host executes
the group query only for explicit Integrations demand, including demand
introduced by query prerequisites, correlates entries by acquisition
registration, and projects them into the existing per-library Finding model.
Query production and the asynchronous library pipeline consume one
participant's retained snapshot before the group releases it and advances, so
the group keeps its complete binding universe without retaining the package's
cumulative image bytes. The retained typed Integrations result prevents a
second scan. When Opportunities is selected, the host executes the typed
Integrations prerequisite and dependent Opportunities query inside that same
participant callback before release. Rejected or failed prerequisite
entries remain typed dependent outcomes, and a blank assembly identity remains
a compatibility skip. Direct `library` and package `--library` remain
single-assembly controls.
Ecosystem and OpenTelemetry evidence form one grouped query outcome, so
malformed participant metadata fails that grouped unit.
Remote package participants carry the coordinate selected by acquisition rather
than package-controlled nuspec identity. Local archives carry a valid,
normalized nuspec coordinate when one exists and local-archive provenance
otherwise. A grouped failure preserves successful rows but emits a warning for
the affected library and returns a nonzero incomplete result.
`PackageIntegrationsWorkspaceTests.Create_PartitionsTfmsAndRetainsParticipantGeneration`
and `Create_PartitionsNonNetFrameworkFolders` gate framework partitioning.
`Create_PartitionsSameFrameworkAcrossAssetContexts` gates package asset context
partitioning. Together they gate participant correlation, package provenance,
and same-generation host inspection.
`PackageIntegrationsWorkspaceTests.UseAssemblyAsync_ReleasesParticipantBeforeAdvancing`
gates streaming image release.
`PackageIntegrationsWorkspaceTests.OpportunityOnlyDemand_RequiresGroupedIntegrations`
and
`PackageIntegrationsWorkspaceTests.OpportunityDemand_UsesTheStreamingParticipantSnapshot`
and
`PackageIntegrationsWorkspaceTests.IntegrationRejection_SuppressesOpportunities`
gate prerequisite activation and failure-safe opportunity composition.
`PackageCommand_AllLibraries_BlankAssemblyNameSuppressesOpportunities` and
`LibraryCommand_BlankAssemblyNameSuppressesOpportunities` gate compatibility
skip suppression across grouped package and single-library hosts.
`PackageIntegrationsWorkspaceTests.LocalAcquisition_UsesOnlyValidNuspecCoordinates`
and `RemoteAcquisition_UsesResolvedCoordinate` gate acquisition provenance.
`InspectionGraphPackageBoundary` consumes those realized workspace members
without reopening their artifacts. It validates package identity and version
against package provenance while keeping the effective acquisition target
distinct from the selected physical asset target, retains acquisition-bound
assembly subjects, and projects one package subject as a structured group, a
package node, or both.
`WorkspaceContextLoaderTests.PackageBoundary_ProjectsLoadedPackageAsGroupAndNode`
gates the compiled package-acquisition path, and
`PackageBoundary_KeepsEffectiveTargetAcrossAssetFallback` gates the
effective/physical target distinction.
`InspectionGraphIntegrationsQuery` composes extensions, Integrations,
opportunities, references, and that package boundary over the same complete
loaded context. The registry baseline remains sequential: prerequisite queries
run in deterministic plan order, participant rows retain group order, and
composition never requires threads. Signature endpoints are resolved through
the participant's frozen binding policy and verified against the selected
retained image before entering the graph. Participant and endpoint failures
remain typed graph failures beside healthy evidence.
`InspectionGraphIntegrationsQueryTests.Execute_ProjectsLockedIChatClientEvidenceAcrossPackageGroups`
gates the compiled multi-assembly path, while
`Execute_DoesNotJoinAmbiguousMatchingAssemblyIdentities` gates the rule that
matching metadata identity or display text cannot replace acquisition
registration.
The query's `InspectionGraphModeRequest` now distinguishes seed intent from
that workspace scope. Its request-free overload explicitly produces a
workspace-participant induced set; type and assembly seeds bind exact nodes,
package seeds bind exact package groups in the detailed Integration lens, and
mixed peers remain equal. Mode binding happens after the same deterministic
full producer plan, so the existing mode-only overloads do not change
occurrence identity or semantic edge direction. Integration relationship
descriptors declare whether a seed may enter as a logical edge endpoint, an
original occurrence endpoint, or through strict typed ownership.
`Execute_DefaultsToWorkspaceInducedSetWithoutSeeds`,
`Execute_BindsTypeSeedToExactNode`,
`Execute_BindsAssemblySeedToExactNode`,
`Execute_BindsPackageSeedToDetailedLensGroup`,
`Execute_BindsPeerSeedsWithoutChoosingPrimary`, and
`PackageAndTypeModesShareSemanticIntegrationOccurrences` gate those claims.
`RelationshipCatalogsDeclareCurrentSeedAdmissions`,
`RelationshipDescriptor_ValidatesAndSnapshotsSeedAdmissions`, and
`AdmissionsMatchDeclaredEndpointDomains` gate the catalog declarations and
their endpoint constraints.

`InspectionGraphNeighborhoodRequest` now makes those declarations load-bearing
for single- and peer-seed Integration graphs. The request keeps mode, selected
relationships, semantic direction, and finite edge depth separate. Every seed
must have compatible directional admission before the registry runs. The
selected relationships request only extension, reference, Integration, or
opportunity queries they require; seed count does not multiply producer work.
The registry still expands typed prerequisites and runs them once in
registration order. Opportunity activates both extension and
Integration evidence because fulfillment suppression composes those results
before projecting opportunity edges; it does not activate unrelated reference
scans.

The completed evidence is projected sequentially into a dense bounded document.
Incoming traversal does not reverse stored semantic direction, original
occurrence receipts and identity survive id remapping, and failures from the
requested relationship producers and their required composition prerequisites
remain visible beside reached topology. Peer projection is a deterministic
multi-source union: every peer begins at depth zero, shared evidence is retained
once, and an admissible disconnected peer remains explicit rather than
disappearing.
`Execute_SelectedRelationshipsControlProducerDemand`,
`Execute_OpportunityNeighborhoodPreservesFulfillmentSuppression`,
`Execute_OpportunityNeighborhoodRetainsPrerequisiteFailures`,
`Execute_BoundsMixedRelationshipNeighborhoodByDepth`,
`Execute_PackageSeedExpandsThroughOwnedSourceSubjects`,
`Execute_OpportunitySourceTypeUsesOccurrenceAdmission`, and
`Execute_NeighborhoodRetainsSelectedProducerFailures` gate the planner,
ownership, receipt, and failure contracts.
`Execute_PeerNeighborhoodConnectsEqualSeeds`,
`Execute_ZeroDepthPeerNeighborhoodRetainsEverySeed`, and
`Execute_PeerNeighborhoodRetainsAdmissibleDisconnectedSeed` gate deterministic
multi-source projection without a primary focus.
`Execute_PeerCountDoesNotMultiplyProducerDemand` gates once-per-plan producer
execution independently of peer count.
`GroupedIntegrationsFailure_IsVisibleAndDeduplicated` gates diagnostic
composition and the shared nonzero completion status used after Markdown,
count, tabular, or JSON output.
`PackageCommand_AllLibraries_GroupedFailureSurvivesHostFailureAcrossOutputPaths`
gates that status independently of later host inspection across Markdown,
JSON, count, and tabular output.
`PackageCommand_AllLibraries_BlankAssemblyNameDoesNotAbortHealthyParticipants`
and
`InspectionAcquisitionPlanTests.PathFactories_BlankAssemblyName_ReturnNoDescriptor`
gate malformed participant isolation.
`PackageCommand_AllLibraries_MetadataOverflowPreservesHealthyOutput` gates
preflight decoder-failure isolation.
`PackageIntegrationsWorkspaceTests.ApplyAssemblyIntegrationsEntry_PopulatesFindings`
and `GroupedEvidence_SuppliesIntegrationPresence` gate the Finding projection
and duplicate-scan boundary.
`AssemblyContextIntegrationsQueryTests.Execute_CarriesBroadPresenceBeyondEvidenceRows`
gates preservation of presence flags that are broader than rendered evidence
rows. Existing
`PackageCommand_AllLibraries_*` tests gate Markdown and structured output
compatibility.

`MemberCallGraphSession` is the first group-owned derived Analysis resource.
It builds one scoped target index only for first paint, one full target index
when a caller-capable tier is requested, and one full index per distinct
cross-library image. The group owns its catalog lifetime and disposes the
catalog before releasing snapshots. Stream-only participants use the same path,
and projection is downstream of acquisition. Typed participant acquisition
failures remain visible as `MemberCallGraphAcquisitionException`.
`MemberCallGraphSessionTests` gates build and source-open counts,
stream-only operation, duplicate-image reuse, typed failures, projection reuse,
and group-owned disposal.
`MemberCallGraphSessionTests.WorkspaceDisposal_DisposesOwnedGraphBeforeSnapshots`
also gates disposal of the graph's catalog scope.
`MemberCallGraphSessionTests.MalformedMetadata_IsTypedAndCached` gates
malformed-image failure caching, and
`MemberCallGraphSessionTests.InvalidImageClassification_CoversMetadataDecoderExceptions`
gates the complete metadata-decoder exception classification.

`AssemblyContextOptimizationOpportunitiesQuery` is the first whole-assembly
Analysis ranking over a complete group. It opens one optimization-only body
index per available participant from the workspace-retained snapshot, uses the
participant's binding policy only for selected siblings in that group, and
releases call-graph caches before advancing. Analysis owns opportunity priority,
semantic loop classification, source-owner aggregation for lifted bodies, and
generated-framework suppression before aggregation and deterministic ordering;
the query joins analyzed body tokens to owning public API members and carries
their exact metadata type identity and stable member selector. Getter/setter or
add/remove evidence aggregates under that one public member while retaining
every contributing body token. Non-public counts, Analysis diagnostics,
metadata projection failures, and participant acquisition failures remain
beside the ranked result. Group execution remains sequential rather than
introducing a parallel-only contract.
`OptimizationOpportunityRankingTests` gate the product ranking policy, and
`AssemblyContextOptimizationOpportunitiesQueryTests` gate public-body
attribution, group ordering, binding-policy use, visible rejection, and query
cost.
`AssemblyContextResearchProjectionQueryTests.Projection_DoesNotAcquireAPolicySelectionOutsideTheGroup`
gates the shared resolver's group-containment boundary.

`AssemblyContextMethodAnalysisQuery` is the exact method-scoped Analysis seam.
It accepts one group participant and physical MethodDef token, opens an
optimization-capable body index over that participant's retained snapshot, and
returns the matching method identity, signals, allocation and unsafe
occurrences, physical call sites, unsafe declaration evidence, exception
regions, optimization opportunities, and Analysis diagnostics. Invalid and
bodyless tokens are typed participant failures rather than empty success. The
query does not aggregate a source method with async or lifted implementation
bodies; callers select each exact physical body explicitly. The query releases
derived call-graph caches before returning, and group execution remains
sequential for Browser/Wasm.
`AssemblyContextMethodAnalysisQueryTests` gate exact-token filtering, compiled
allocation/call/exception/opportunity evidence, visible invalid and bodyless
failures, and unbounded query cost.

`AssemblyContextStructuralCloneRetrievalQuery` is the first query that joins
two explicitly selected assembly participants while both immutable snapshots
remain borrowed. Its input names the seed and candidate groups and
participants, selects the seed by a MethodDef token or an exact structured type
plus `MemberAnchor`, and declares either one exact candidate type or an explicit
whole-assembly population. A-vs-A uses one reader only when both selections
refer to the same participant in the same group. Every other request uses
independent readers, including equal-MVID content acquired under separate
registrations, so reader-local identity is never inferred from module identity.

The query resolves only exact metadata identities, enumerates the full selected
population without query-side truncation, and dispatches one mutually exclusive
same-image or cross-image Analysis path. The exactly-once Analysis call count is
unverified beyond direct inspection. The returned
`StructuralCloneRetrievalResult` is not projected or reconstructed: ranks,
score components, method outcomes, blockers, receipts, MVID-scoped method
addresses, and the four product dispositions remain owned by Analysis.
Acquisition rejection, missing or ambiguous exact targets, and pre-retrieval
metadata failure are separate typed query outcomes. The query is `Unbounded`;
whole-assembly scope is explicit, and Analysis method, result, and
body-production limits remain the visible work controls. Exact selection still
uses Metadata-owned cumulative name, member-anchor, method-row, decode-failure,
and custom-attribute work ceilings, so malformed metadata fails visibly before
retrieval rather than multiplying per-row work. Each candidate type-name
attempt consumes structural-name work, and decode failures also count against
the decode-failure ceiling. Method projection is validated once per image
rather than at each projection site: the query admits a reader only after
confirming that no TypeDef method range reports a negative length and that
the ranges cover the MethodDef table exactly once, and seed and population
resolution accept only an image carrying that confirmation. Those two
requirements bound the underlying `MethodList` column jointly, which is why
neither is redundant: a negative length is what makes the starts
non-decreasing, and coverage is what forces the first non-null start to row 1
and holds every later start within one past the projected table. A null start
sits outside that chain: ECMA-335 II.22.37 permits it and the reader reports
its range as length zero rather than as the difference to the next start.
A repeated or out-of-range row, a `MethodPtr` table
that aliases one MethodDef row into two types, a descending range, and a
`MethodList` start past the table -- which SRM reports as an empty or
negative-length range rather than an error -- are all typed
metadata failures in the participant role that read the image, instead of
reaching Analysis as untyped argument errors, being reported as a member
ambiguity, or returning a success-shaped empty population. The check is a
single pass over the image's own tables and reads no raw table bytes; it is not
a claim that every malformed image is diagnosed before Analysis. It introduces
no network, source, Research, Finding, Decompiler, or presentation capability.
`AssemblyContextStructuralCloneRetrievalQueryTests` gates A-vs-A and A-vs-B
product-result preservation, type and whole-assembly population behavior,
exact-member, extension-member, and token selection, ambiguity, limit
separation, unsupported bodies, seed-before-candidate failure precedence,
malformed acquisition and metadata-neighbor isolation, and same-MVID
independent-reader handling. Its virtual-token, repeated-long-leaf,
repeated-long-unequal-leaf, repeated-malformed-leaf, near-limit-member-anchor,
repeated-container-attribute, and rejected-TypeSpec-attribute cases gate the
pre-retrieval work ceilings and visible metadata-failure boundary. Its
type-name decode-failure case gates the decode-failure ceiling, paired with a
below-ceiling case that proves isolated malformed neighbors remain tolerable.
Fifteen cases gate whole-image method ownership across the type-scoped,
whole-assembly same-image, whole-assembly cross-image, and member-seed paths,
covering duplicate, out-of-range, cross-type aliased, and silently empty
projections, a descending `MethodList` range, an uncovered `MethodPtr` row, and
metadata declaring no TypeDef rows. The descending cases pin the check on both
metadata shapes -- a `MethodPtr`-free image and a reordered `MethodPtr`
permutation -- and each is rejected at the module row, before any row is
projected. A further case pushes every start past the end of the projected
table so the earlier ranges report length zero and enumerate nothing, isolating
the one path that reaches the end of the derived bound with a negative final
range. Every fixture in this group pins its per-row range lengths, so the shape
it claims to exercise is gated rather than asserted in prose. A further case starts
the column past MethodDef row 1 while every range keeps a non-negative length,
so it is rejected by coverage alone and pins the half of the ordering proof the
range-length check cannot supply. A further case carries a null start *after* a
populated run, which ECMA-335 II.22.37 cannot express because each run is
delimited by the following start, so the negative length lands on the preceding
row. Those fifteen are all rejections; a
sixteenth case gates that a null
`MethodList`, which ECMA-335 permits and the runtime reader projects as an
empty run, is accepted rather than reported as malformed. A seventeenth gates
uniqueness of the exact seed member, which a rejected sibling leaves unproven,
and an eighteenth gates that matching a candidate leaf charges the
declaring-chain traversal it performs rather than only the names it compares;
that case pins its fixture's declaring depth, because a shallow fixture would
exhaust the same budget while leaving the traversal unexercised.

Other domain catalogs, query authorization, concurrent execution, and broader
command migration remain later slices.

Domain catalogs operate inside a group. A catalog may advance through
progressive generations as new candidates or binding roots are discovered while
the group itself remains alive. Each generation is scoped to the resolution and
authorization policy snapshot that produced it. A later query plan may reuse
the generation only when the domain owner verifies that the plan has a
compatible policy; otherwise it requires a separate generation or catalog.
Reauthorizing an image lease alone is insufficient because binding and
correspondence answers can themselves reveal a candidate the later plan may not
use.

A query execution attempt that consumes catalog-bound values runs against one
frozen generation. A domain may return a typed plan-expansion request when the
manifest lacks required work. The inspection coordinator quiesces consumers of
the predecessor, unions the request into the plan, asks the domain owner to
freeze a successor, and restarts the affected work. The successor does not
mutate the predecessor, but the owner may invalidate predecessor contexts,
tokens, and leases when it publishes the successor.

The smallest case remains cheap: one workspace, one group, one root assembly,
and one requested query.

### Inspection bundles and demos

A host build may include zero or more immutable **inspection bundles**. For an
assembly-backed scenario, the bundle may carry a portable workspace definition
from which the host creates an ordinary runtime workspace. It never contains a
serialized live workspace.

A bundle may contain:

- a stable bundle id and descriptive metadata;
- zero or more workspace definitions, each with one or more context-group
  definitions;
- embedded artifact content, typed acquisition locations, or domain-typed
  runtime input slots, with the identity, digest, and provenance evidence
  appropriate to their source;
- required producer capabilities; and
- optional named query-plan and view presets.

The optional workspace definition, query preset, and view or navigation preset
remain separate. A **demo scenario** names one composition of them. A
workspace-free scenario omits the workspace definition but still names the
embedded input, typed acquisition location, or domain-typed runtime input slot
used by its source- or artifact-scoped query. A discovery-first scenario may use
a typed query result to instantiate a workspace in a later authorized stage.
Several scenarios may reuse one workspace definition, and a host may inspect
the definition without running a preset or acquiring its inputs.

Product-resident home demos ship as a static id→factory registry
(`DotnetInspector.Queries.Definitions.ProductInspectionDemos`, smooth-markdown-table
`RendererRegistry` style); hosts resolve one demo via
`ProductInspectionDemos.ResolveHomeScenario`, which allocates only that demo's
peer records and requires a `ProductDemoSections` binding. Home demos are closed
presets over the open query/section product: the registry fixes inputs and names
**existing product section(s)** (`ProductDemoSections.ExpandRunSections` expands
Call Graph presets format-aware: Markdown keeps Call Graph + Callers;
table/tsv/jsonl keep Callers when the demo has caller scope so the re-add stays
one section, otherwise Call Graph so package-local entry points still emit rows;
mermaid keeps Call Graph; document JSON fails closed until graph projection
lands); the CLI host runs them through the normal type/member section pipelines
(`DemoScenarioRunner` → `TypeCommand` / `MemberCommand`) and returns those
sections in ordinary formats. Demos must not call past sections into ad hoc
inspection APIs; a capability that is not a product section is not a home demo
until the section exists. CLI argv, definition plans, and browser engine
operations (including a generated TypeScript binding of that engine surface)
must be encodings of the same preset—not parallel demo systems. Residual:
minted view-facet ids, `WorkspaceContextLoader` as the shared group-run owner,
and Call Graph structured-JSON projection (see
workspace-definitions). Detail:
[workspace-definitions.md — Product demos are closed section
presets](design/workspace-definitions.md#product-demos-are-closed-section-presets).

A bundle contains no live streams, `PEReader` instances, sessions, acquisition
registrations, candidate ids, catalog generations, join tokens, cached verdicts,
or authorization decisions. Loading a bundle materializes only immutable
definitions and presets. It performs no source discovery, artifact acquisition,
registration, image opening, or catalog construction.

For an assembly-backed input, the first authorized query plan that needs it asks
the normal acquisition owner to create its descriptor and registration lazily,
then asks the domain owner for a catalog under that plan's policy snapshot. A
workspace-free query asks its source or artifact owner only for the narrow
context its operation declares; no assembly registration, group, or catalog is
implied. A persistent host may retain a resulting workspace afterward under the
normal lifetime and budget rules.

The target
[artifact acquisition design](design/artifact-acquisition-and-workspaces.md)
names the owner-issued access for that first projection the admission lease.
Before its first adapter call, the workspace reserves the complete
multi-source plan against aggregate artifact-count, peak-acquisition-byte, and
retained-byte budgets that also include concurrent admissions and retained
generations. Before atomic publication, admission materializes every selected
logical artifact into retained immutable content, validates identity and every
budget dimension, and projects all required assembly participants. Equivalent
concurrent demands for the same context generation and admission-policy
snapshot join one workspace-owned operation and consume no second reservation,
including across Browser/Wasm awaited reentrancy. Cancellation-draining
operations accept no new join or late publication. Later opens use only
retained content; they perform no source acquisition, archive expansion,
participant minting, or catalog mutation. This reservation is distinct from
each adapter's transport/archive limits and the assembly group's image budget.
Later queries receive separate query leases and must reauthorize the retained
catalog generation before participant selection.

Hosts statically register the bundles they choose to ship. Excluded bundles and
their definitions and embedded artifact bytes do not enter the build. Included
bundles require no runtime plugin discovery or reflection loading. A
self-contained bundle can run without filesystem or network access; a bundle
that names package, platform, project, or local acquisition locations uses the
normal owner and capability gates for those sources. This keeps the model
compatible with trimming, NativeAOT, both online and offline Wasm demos, and
non-browser hosts.

Bundle inclusion is a build-time publication decision for every field, not only
embedded bytes. The publisher must be authorized to disclose its scenario
metadata, presets, package ids, source endpoints, paths, and artifact content to
every build recipient. Runtime scenario authorization cannot conceal
information already shipped in a Wasm or native binary.

Sensitive coordinates and sensitive acquisition locations remain outside the
bundle. The bundle declares a domain-typed runtime input slot that a host
supplies at runtime instead. The supplied value follows the same acquisition
owner and input, cost, and capability gates as an interactive request; an
unfilled or denied slot is a typed outcome. A slot is a domain-owned typed hole,
not a universal input envelope or a stored secret.

Private or otherwise non-redistributable content likewise uses an appropriately
protected runtime acquisition location rather than embedded bytes. A digest
provides integrity evidence, not confidentiality, disclosure authority, or
redistribution authority.

Build inclusion makes bundled bytes available. Selecting a scenario forms a
request for its declared inputs and capabilities; the host
authorizes that request under the same input, cost, and capability policy as any
other request. Selection does not bypass a network, source-content, exhaustive,
or other expensive-work gate. The bytes remain untrusted inspection data, are
parsed rather than loaded, retain bundled acquisition provenance, and cross the
same budgets and presentation boundaries as user-supplied content.

An inspection bundle contains no precomputed query results or producer,
correspondence, or authorization verdicts. A future build-time result-cache
feature would require its own semantic-key, producer-version, validation, and
publication contract; bundle inclusion does not imply one.

### Query plan

A host asks for typed inspections, not scanner names or output sections. Each
query declares:

| Property | Question answered |
| --- | --- |
| Scope | Does it run without a workspace, for one source or artifact, one assembly, one context group, or several groups? |
| Inputs | Which typed content and prior results does it consume? |
| Cost | Is the work bounded, network-bound, source-content-bound, or exhaustive? |
| Capabilities | What must the caller authorize? |
| Execution modes | May the producer render, run as an effectiveness probe, or both? |
| Dependencies | Which producer results must exist first? |
| Conditional successors | Which typed predecessor outcome selects each fallback path? |
| Lifetimes | Which acquired images, catalogs, or other bound resources must remain alive? |
| Correspondence | Which owner establishes relationships between the inputs? |
| Result | Which typed value or failure does it return? |

CLI sections and Wasm views lower their selections into this plan. They do not
own acquisition cost or producer dependencies.

The assembly-local string-keyed scanner predecessor has been retired.
`DotnetInspector.Queries` and its optional Research-backed companion now own
typed metadata, direct-reference,
assembly-context reference, package dependency-group, extension-method,
custom-attribute, manifest-resource, type-forwarder, union-type, switch,
SourceLink, API-comparison, and Analysis body-signal comparison plans. The
Analysis query
consumes old/new `LibraryBodyIndex` collections and returns
`ResearchComparison`; the diff CLI still owns lazy path-to-index acquisition as
a transitional adapter. Mutable CLI models, path-shaped residual inputs, and command-owned acquisition
remain migration boundaries rather than workspace contracts.

The registry executes synchronous and asynchronous queries in deterministic
prerequisite order. It passes each query's maximum transitive cost into the host
execution scope. A conditional successor is part of the closed graph before
execution and is selected only by its predecessor's typed outcome. Preflight
records authorization or denial for every successor; execution cannot add one.
A denied optional successor does not prevent an earlier branch from succeeding,
but selecting that successor produces its recorded typed denial. SourceLink
demonstrates the network boundary: a local-PDB read may finish without
acquisition, while a typed miss reaches a separately preflighted moderated PDB
acquisition successor. Availability and integrity declare unbounded work and
accept host-owned HTTP clients and an optional cache.

### Executor

Sequential topological execution defines the baseline. It works in
single-threaded Wasm, is easy to audit, and provides the reference ordering for
every other policy.

The host preflights common prerequisites, independent demand roots, and every
conditional successor. Each executable closure carries its granted
capabilities, execution mode, and probe policy; each unavailable closure
carries a typed request, capability, cost, mode, or policy denial. Discovery
can map an unavailable section root to a typed unknown while executing other
roots. Explicit render demand reports that denial as non-success. Plan-level
denial is reserved for mandatory common work that must complete before section
roots can be classified; a denied section root remains section-scoped even
when it is the sole demand.

A later executor may schedule independent nodes concurrently. Concurrency must
not alter:

- which work the plan authorizes;
- result and row ordering;
- acquisition or resource budgets;
- failure visibility;
- validity of bound currencies and frozen answers;
- assembly, group, or producer provenance.

It must also respect resource and generation barriers. Publishing a successor
must not silently invalidate a live consumer. The executor quiesces predecessor
consumers before an owner that invalidates on advance publishes the successor.
An owner may instead permit concurrent publication only when its leases retain
complete per-generation state until every consumer releases them. A query
cannot outlive a resource it borrowed. A producer may declare a resource safe
for concurrent consumers; otherwise the executor serializes access. The
sequential executor satisfies these rules without requiring threads.

Producers receive the narrow context named by their scope, not a mutable
workspace object. This keeps the workspace from becoming a god object and makes
cross-group access explicit.
[Analysis universe realization](design/analysis-universe-realization.md) owns
the equivalent narrow handoff when a validated analysis plan needs an ordered
finite population, one or more contexts, and provider-issued executable
capabilities.

## Core currencies

The core is defined more by the values crossing its boundaries than by project
names.

### Currency contracts

A **currency** is a value one owner accepts as authoritative for one operation.
It is not a repository-wide interchange type. Every currency has a contract:

| Property | Question |
| --- | --- |
| Authority | Which owner and operation may trust this value? |
| Scope | Is it valid for one reader, image, body, catalog generation, group, or comparison? |
| Lifetime | Which live owner or frozen generation must remain available? |
| Portability | May it cross a query, process, serialization, or persistence boundary? |
| Erasure | Which facts or capabilities were deliberately left behind? |
| Rebinding | Which owner can validate or bind it in another context? |
| Correspondence | Does equality have meaning, or must an owner compare or project it? |

These properties are independent. A durable address may be portable but unable
to prove that two artifacts correspond. An opaque catalog key may answer exact
correspondence but only while one generation remains alive. A portable source
line may survive serialization while its IL offset remains meaningful only
beside the physical body; its annotation extents use the coordinate plane of
the containing rendered stream.

Bound and portable forms are therefore a matrix, not a ladder. Projection from
a bound value into a portable value is explicit and names what authority it
loses. Rebinding is another owner operation with a typed failure, not an
implicit cast back to the original value.

Concrete types remain domain-owned. The core does not define a universal
`IJoinable`, generic anchor, bound-value wrapper, or portable-value envelope.
The architecture is the contract above and the ownership of each transition.

### Identity and provenance

The current implementation returns a descriptor such as
`ResolvedAssemblyReference`: identity, an opener for the selected content, and
structured resolution provenance. Inspection does not discard that information
into a bare path and later reconstruct it.

The target
[artifact acquisition design](design/artifact-acquisition-and-workspaces.md)
makes that descriptor source-neutral: artifact/acquisition identity plus
owner-guarded content access. Source adapters retain typed source-specific
provenance beside the workspace participant rather than extending a
Metadata-owned provenance hierarchy. Caller designation and other trust inputs
remain separate authorized workspace roles rather than provenance inferred from
paths or assembly names.

Identity, correspondence, provenance, and display remain separate. Joins use
the typed currencies; presentation chooses spelling afterward.

### Acquired generations and leases

An acquired assembly has several related but distinct scopes:

| Scope | Meaning |
| --- | --- |
| Acquisition registration | Repeated policy selections name the same canonical candidate chosen by one acquisition owner. |
| Image lifetime | Consumers read one opened byte generation through a format owner's session or lease. |
| Catalog generation | Binding and correspondence answers share one frozen candidate universe and policy snapshot. |

None implies another. Matching descriptor fields do not prove one registered
candidate. Sharing one `PEReader` does not establish definition
correspondence. Advancing a catalog generation does not require reopening every
image whose bytes remain valid.

The workspace coordinates these lifetimes without making format-specific
handles core currency. Metadata may own a `PEReader`; another producer may own
an immutable byte image or a parsed index. Queries receive narrow sessions,
views, or leases and do not reopen or dispose the underlying resource.

This is a correctness rule as well as a performance rule. Opening the same path
twice can observe two different files after a build, restore, or symlink change
and silently combine facts from different assemblies. Sharing the acquired
generation removes that assumption.

Reader-local handles and pointers remain inside the owning lifetime. Results
that outlive it materialize producer facts or carry a durable address that its
owner revalidates before dereference. A durable address is location evidence,
not artifact identity or correspondence proof.

### Authorized content

Content availability never grants authority to inspect it. A package payload,
source document, retained byte image, or cache entry is visible only when the
current request authorizes the producer and coordinate that supplied it.

Acquisition retains enough provenance and authorization evidence for the owner
to make that decision. A persistent workspace may retain bytes or parsed
resources, but a later query plan revalidates access under its own capabilities
and source policy before receiving a lease. It also reuses derived binding or
correspondence results only when their authorization scope is compatible. The
cache answers only after that decision; it does not introduce candidates or
widen authorization.

Fallback availability is a typed producer outcome, not authority. A local
cache miss may select a PDB-acquisition successor only when preflight recorded
that successor as authorized. A denied successor remains denied even if the
content later becomes available through another operation.

This is the acquisition analogue of other owner-issued safety currencies. The
acquisition owner authorizes content, a catalog authorizes correspondence, and
the presentation boundary produces `InertString`. None can be reconstructed by
inspecting the visible fields of an untyped value.

### Results and failures

Queries return typed results. Expected bad-input and acquisition failures are
typed outcomes with subject provenance, not empty collections shaped like
success. Unexpected producer defects remain fatal rather than being relabeled
as bad input.

Partial group results are valid only when their failures are carried beside
them and the result contract says partial inspection is meaningful.

A plan-expansion request is a typed orchestration outcome, not absence or an
empty result. The coordinator advances the owning domain's generation and
restarts affected work before presentation.

### `CoreCache`

`CoreCache` is shared infrastructure for category roots, path-safe hashed keys,
maintenance, and cache telemetry. It is a mechanism, not a semantic authority.

The cache owner for each result must still define:

- the complete semantic key;
- producer and source provenance;
- freshness and versioning;
- validation on read;
- publication and concurrency behavior;
- whether already-authorized network work may run after a miss.

A cache may make a correct query faster. It must not change which query was
asked or which producer's bytes the caller is authorized to inspect.

A persistent derived-result cache is an alternate entry into the pipeline
stage that produced it. A cache hit may skip an earlier gate only when all of
these are true:

- the gate establishes a stable property of the exact content or immutable
  producer evidence, not current-request authorization or lease liveness;
- the cache key is derived from the digest the owner computed over retained
  immutable content/evidence and names that content, not the snapshot instance
  or its generation;
- the entry's gate, producer, and cache publication consumed one such retained
  snapshot and its owner-computed identity;
- the entry was written only after the gate succeeded; and
- the category or payload records the complete gate-contract version.

When a result depends on several retained artifacts or external evidence, the
semantic key identifies every contributing content snapshot and every
provenance dimension that can change the derived result. An availability
Boolean is sufficient only when a declaration-derived closure proves the
cached payload is a function of that Boolean alone.

Root request and acquisition provenance participates by the same rule. Two
routes to the same retained bytes may share a cache entry only when every
producer and cached section/field predicate is route-independent; otherwise an
owner-issued typed route identity belongs in the semantic key. A resolved path
does not reconstruct whether the caller selected platform, package, direct
file, or another subject route.

The cache subject is immutable from lookup through cold production and
publication. Publication uses the exact evidence identities every producer
consumed; it cannot re-probe and file that result under post-production
evidence. An observed evidence-generation change either declines publication
or starts a later authorized operation that recomputes under the new subject.

Hashes taken before and after work over a separately reopened mutable path do
not establish this identity: the source may change from W to S and back to W
while the gate and producer consume S. The acquisition owner computes the
digest from retained immutable bytes and supplies those same bytes to the cold
path; neither the producer nor cache owner may reconstruct identity by reopening
the source. A later source replacement belongs to a later acquisition.

Current request, host, capability, and liveness policy is never certified by a
cache version and must be re-evaluated on every use. When a release introduces
or tightens stable admission, validation, failure, or projection semantics
after an existing cache lookup, the cache owner must either run that gate on
every hit or select a successor contract version before post-cutover lookup.
Extending only the new write path does not certify predecessor entries.

Every such cutover needs paired non-vacuity evidence: seed a predecessor entry
for content newly rejected by the gate and prove it cannot produce success,
then seed one for still-valid content and prove the cold path recomputes and
publishes a reusable successor entry. When the source can change, inject a
W-to-S-to-W replacement through the product acquisition seam, count source
opens, and prove no result derived from S can be published or read under W's
identity. This repository-wide cutover rule is unverified as a global
inventory; each adopting cache must name its owning gate. `MDP017` in
[member inspection planning and Metadata
projection](design/member-inspection-planning-and-metadata-projection.md) is the
worked gate for the library effective-catalog format-admission cutover.

### `InertString`

`InertString` is the presentation currency for untrusted artifact text. Its
construction applies a closed text policy and its type records that the value
crossed the containment boundary.

It belongs late in the pipeline. Metadata names, package ids, paths, and source
text stay exact while they participate in identity, matching, resolution, and
analysis. They become inert at the last shared structural boundary before
presentation, when the sink policy is known.

Structural escaping remains the renderer's responsibility. Inert text prevents
terminal control, visual reordering, and invisible agent-context payloads;
Markdown, JSON, TSV, and other writers separately escape their grammars.

## Joins

A join is an owned operation over typed operands in an explicit context:

```text
join = operands × context × correspondence authority
    -> relation | typed non-relation
```

The architecture does not require one relation type. It requires the operation
to preserve the distinctions that make its answer trustworthy.

### Join operands

A join operand conceptually combines four parts:

| Part | Role |
| --- | --- |
| Subject | The entity being discussed across producers or contexts. |
| Local binding | The exact candidate, member, body, or resource in the current context. |
| Native coordinates | Producer-owned locations such as a metadata row, IL offset, source extent, or stream position. |
| Payload and provenance | The evidence being related and the producer that supplied it. |

Those parts need not be duplicated on every leaf. Identity belongs at the
highest container that knows the subject; native coordinates stay on the
lowest producer that owns their semantics. A body-local fact may carry only an
IL offset while its enclosing result carries the member subject and assembly
binding. A portable structural span may depend on its containing text buffer for
the coordinate plane. Composition supplies the full operand without flattening it
into one key.

Member inspection is the worked pattern. A selector is a portable question, a
member anchor is a durable API-identity projection, a resolved target binds
that identity to one API surface and possible physical body, and a metadata
handle is exact only for one reader. Body evidence retains its native identity
and coordinates; Research owns the bridge when API and body vocabularies must
join. Projected members such as extension methods retain both the API target
and the physical body owner instead of collapsing them.

Source projection demonstrates the same pattern at another scale. An in-process
correlation may retain live annotation objects and IR relationships. Its
portable projection materializes annotation data and text spans so another
consumer can retain, filter, or render the relation without those live objects.
An instruction's IL offset remains scoped to its physical body, while structural
coordinates become absolute spans over the rendered text — the projection's own
canonical artifact — so a discontinuous construct names the same characters,
line breaks included, however the media were woven, and no coordinate depends on
a line identity the payload does not carry. The projection does not claim to
recover the original graph.

These examples are precedents, not core types. Their owning documents define
the exact currencies and conversions.

### Correspondence precedes composition

Equality is not correspondence. A path, display string, MVID, metadata token,
durable address, record equality, or matching payload fields can be useful
evidence without proving that two operands denote the same subject.

The domain owner establishes correspondence. Depending on the domain, its
closed result may distinguish:

- exact sameness and definite difference;
- ambiguity or duplicate-artifact indeterminacy;
- incomparable contexts or stale generations;
- exact and named soft-match tiers with match provenance;
- inability to decide because required evidence was unavailable.

A boolean result is insufficient when the domain admits those states. In
particular, indeterminate is neither false nor permission to fabricate a
match. A safe negative comes only from the authority that has enough evidence
to rule the relation out.

When repeated joins need hashing or indexing, the authority may project a
generation-scoped join token. Consumers do not derive one by normalizing
display strings or unpacking an opaque key. A portable address may be rebound
and revalidated in another context, but it does not become a correspondence
token by surviving the trip.

### Join scope

The required correspondence changes with scope:

| Scope | Rule |
| --- | --- |
| One live reader | Reader-local handles are exact only inside that reader. |
| One body | IL offsets are interpreted beside the physical member and body binding. |
| One rendered stream | Annotation extents are interpreted in that stream's coordinate plane. |
| One context group | Cross-assembly correspondence uses one frozen binding catalog generation. |
| Several groups | Each portable subject is bound independently; bound handles, keys, and tokens never cross the group boundary. |
| Several versions | Exact correspondence remains distinct from accepted soft correspondence; accepted soft matches retain tier and match provenance, and ambiguity is never promoted to a match. |

Cross-group comparison is explicit work, not an exception to group isolation.
It consumes portable projections or independently resolved subjects from each
group and produces a new relation. A value that is incomparable across catalogs
does not become comparable because both catalogs happen to be in one
workspace.

### Join execution

Joins must remain demand-driven and bounded. The query plan declares their
operand producers, correspondence owner, scope, capabilities, prerequisites,
and result. The executor requests the required frozen contexts from their
owners and retains their leases until the relation is complete.

An owner may provide indexes, blocking keys, or conservative prefilters to
avoid a Cartesian comparison. Such a filter may admit extra candidates, but it
must not produce a negative outside the evidence its domain contract
authorizes. Candidate generation and final correspondence remain separate
operations.

The result retains producer-native evidence, subject identity, local
coordinates, correspondence provenance, and scoped failures. It is a projection
of the relation, not a replacement for either operand. Research normally owns
cross-producer composition; a domain producer continues to own its own binding,
matching, and coordinate semantics. The workspace orchestrates both without
learning type names, member grammars, IL offsets, or source-span rules.

## Add-ins

An add-in owns domain facts and algorithms. It may be large and sophisticated,
but it integrates through the same core contracts.

Examples include:

- Metadata producing assembly and API facts.
- Analysis producing IL-body evidence and graph facts.
- Decompiler producing source-shaped projections.
- Research composing evidence owned by other producers.
- Package and source producers contributing acquisition and provenance facts.

An add-in does not:

- parse CLI arguments or choose output formats;
- invent a second acquisition or cache policy;
- infer identity from display strings;
- hide its cost or capabilities behind a section;
- bypass workspace grouping for cross-assembly work;
- send untreated artifact text directly to a sink.

“Add-in” does not imply runtime discovery, reflection loading, or an external
compatibility surface. Static registration is compatible with the role and
with NativeAOT.

## Architectural tests

A proposed core change should answer all three goals.

| Goal | Questions |
| --- | --- |
| Rich | Does it preserve producer-native depth, admit more inspection types, or improve typed joins? |
| Fast | Can it avoid unrequested work, share acquisition, cache safely, and run sequentially? |
| Safe | Are identity, provenance, capability, budgets, failures, and presentation containment explicit? |

Common false tradeoffs are rejected:

- “Rich” is not permission to collect everything eagerly.
- “Fast” is not permission to require threads or weaken cache identity.
- “Safe” is not permission to delete evidence, collapse failures to empty
  output, or encode identity before matching.

The inspection space is successful when a host can ask a deep question across
many kinds of evidence, pay only for the requested answer, and safely retain
enough identity and provenance to trust what was joined.
