# Workspace live locator

## Status and authority

Focused design under
[#6845](https://github.com/richlander/dotnet-inspect/issues/6845), following
the merged [reverse locator contract](reverse-type-declaration-locator.md)
in [#6853](https://github.com/richlander/dotnet-inspect/pull/6853).
The [explicit context projection](#implemented-explicit-context-projection)
is implemented. The live facade, snapshot/subscription handoff, and resident
implementation gates below are **not implemented**. The interaction model is
design evidence only.

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
`InspectionWorkspace` already retains admitted groups and coordinates their
release; its current implementation has no participant-notification API.

The live Library owner/operation-lease APIs are still design-only. Adoption
must use the then-implemented ownership seam or land its prerequisite; a
design reference is not permission to invent a private lease protocol here.

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

The first #6845 implementation supplies the cold query's population input,
not Workspace-wide observation. `LoadDeclarationContextAsync` uses the
existing `WorkspaceContextLoader` acquisition path and issues a
`WorkspaceDeclarationContext`: one frozen request associated with either its
committed group and source correspondence or its upstream realization failure.
The request's order is reserved before acquisition awaits; completion timing
does not choose population order.

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

Global population observation, first-demand caching, append maintenance, the
cold matcher, and CLI/Browser adoption remain pending along the
[delivery map](reverse-type-locator-adoption.md). This slice does not complete
issue #6845 or expose a new Find command.

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
The same #6845 issue remains open for that implementation after this design
lands. Common Sections (#6846), CLI (#6844), and Browser (#6851) adopt the
live facade along the existing nine-step map; the one-shot CLI uses a short
Workspace lifetime rather than a separate index. Local/project coordinate
support remains #6847. Old Platform lookup retirement remains #6850.
