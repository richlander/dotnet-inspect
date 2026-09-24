# PackageHouse composition

## Status and approved scope

This document is the normative owner for `PackageHouse`, tracked by
[#6426](https://github.com/richlander/dotnet-inspect/issues/6426).

The user approved a broad package architecture with one higher-level package
concept above package input, transport, and policy. `Workspace` delegates
package acquisition and package-policy decisions to that concept instead of
reconstructing them. The accepted `House` convention from
[#6301](https://github.com/richlander/dotnet-inspect/issues/6301) names this
concept `PackageHouse`: a foundational clearing-house service that aggregates
candidates from multiple sources, applies policy, and settles one authorized
result.

This is a broad, explicitly approved composition design. It defines only the
House facade, request, settlement, result, receipts, and typed handoffs.
Adjacent owners retain their identities, algorithms, failures, and lifetimes.
Their adoption remains separately reviewed.

[#7423](https://github.com/richlander/dotnet-inspect/issues/7423) extends that
composition with package-slice policy. Its first focused slice defines only the
package-local compile inventory and selected projection that PackageHouse
retains. [#7431](https://github.com/richlander/dotnet-inspect/issues/7431)
implements the owner-issued selection evidence. Package Dependency Query
scope, size measurements, traversal targets, call graphs, Navigation, and host
adoption remain separate slices.

The first production adopter is shared package realization for
`Workspace`. The CLI and Inspect Web then consume the same House contract
through their package and Workspace paths.

The current host-neutral implementation floor is `PackageHouse` in
`DotnetInspector.Packages`. It executes exact, candidate-bound, and typed
selecting `Settle`, `Acquire`, and target-aware `Realize` requests by consuming
one Package Source Model-issued operation lease. `Realize` composes the
existing compile/runtime asset-selection owners and returns their
resource-free receipts and optional Library handoffs without opening selected
content. `PackageHouseDependencyInputAdapter` in
`DotnetInspector.PackageQueries` adopts normalized declaration or produced-
relationship evidence without copying its authorship or processing semantics.
`PackageHouseDependencyPruningQuery` preserves explicit non-evaluated states
or creates one PackageHouse-issued policy receipt, and candidate-bound House
execution applies that receipt before payload acquisition.
`PackageDependencyPruningInspection` in `DotnetInspector.Sections` now owns the
bounded host-neutral operation that resolves authorized declaration candidates,
applies that pruning query, and returns the ordered outcomes through
`InspectionEnvelope<PackageDependencyPruningInspectionResult>`. CLI `depends`
and Inspect Web package pruning both consume that operation while retaining
their own input scope, source and inventory authorization, completion policy,
and presentation.
`DesktopPackageSourceComposition` still owns desktop configuration,
credentials, transports, clients, stores, and disposal, but its
composition-owned exact and selecting payload operations, asynchronous pinned
candidate path, and candidate-manifest path now settle through PackageHouse.
The CLI's online `package Package@latest --versions` query and `@latest`
package opening consume the shared `PackageVersionSettlementInspection`
envelope, also used by Inspect Web exact/latest package opening. The shared inspection
projects House `Settle` evidence into a serialization-ready outcome; hosts
render or consume the selected coordinate rather than choosing a latest row.
The earlier desktop-only `SettleVersionAsync` bridge is retired. Desktop
composition now supplies only the configured House and source operation.
Requested `--verbose` source-fetch progress still flows to stderr through the
settlement's optional discovery callback.
Online ordinary `package Package --versions` queries consume a separate shared
`PackageVersionListingInspection` envelope over PackageHouse listing
settlement. The House result preserves authoritative or partial Package Source
discovery, while the inspection detaches version rows, source rows, typed
failures, and diagnostics for CLI projection. Online exact pinned queries also
consume this detached listing, request prerelease and unlisted evidence, and
apply their existing exact NuGet match over its rows. A matching pin remains
usable with visible partial-source diagnostics; a missing pin is not declared
absent when a configured authority failed, except when the failure concerns
listing state rather than version existence. A caller can request that pinned
single row with `Package@Version --versions -n 1`; valued
`package Package --version VERSION` instead selects the Package to inspect.
Raw listing may publish usable partial rows because it selects no coordinate;
source failures remain visible and cannot become authoritative absence.
Inspect Web's
`BrowserPackageVersionInventory` is the second host adopter under
[#7530](https://github.com/richlander/dotnet-inspect/issues/7530); it consumes
the same detached listing while retaining Browser-owned predecessor policy.
`System.Text.Json` is the motivating production package. The
`SourceScopedRoutingTests.LatestVersionSettlement_*` cases cover the detached
receipt, requested progress, and explicit prerelease boundary, while the
existing latest-settlement, source-failure, listing, rendering, and
`BrowserPackageVersionInventoryTests` cases preserve neighboring behavior.
This is payload-free adoption: offline behavior and package-content/Workspace
adoption remain separate slices.
`Realize`, target-aware dependency-edge realization, Workspace admission, live
Library construction, and broader host adoption remain later steps.
[#4653](https://github.com/richlander/dotnet-inspect/pull/4653) remains useful
design and implementation evidence; it is not the branch this architecture
extends. Its remaining intent is retired through the focused adoption steps in
[#6426](https://github.com/richlander/dotnet-inspect/issues/6426).

## Authority and exact claim

**PackageHouse Composition** owns:

> Given one typed package demand, one optional owner-issued target-selection
> context, one host-authorized package source and input plan, one closed
> package operation, and finite work, settle the demand to one typed package
> result that preserves the request, package identity and version evidence,
> source authority, pruning decision, payload and asset correspondence,
> dependency realization correspondence, provenance, completion, failures,
> and retained lifetime without reconstructing identity from strings.

The owner defines:

- the product-facing `PackageHouse` facade;
- the package-demand envelope;
- the package operation profile and common operation context;
- the distinction between settlement, acquisition, and realization;
- composition of owner-issued package source, version, pruning, payload,
  asset-selection, and dependency capabilities;
- package-local compile-selection policy and preservation of the complete
  owner-issued inventory beside its selected projection;
- the House result envelope and terminal outcome;
- the package decision, acquisition, and realization receipts;
- typed package-to-platform delegation;
- package-to-library unwrapping with retained provenance;
- target-aware dependency-edge realization handoff;
- the evidence `Workspace` receives before package admission; and
- the rule that product package processing does not recreate House decisions
  in hosts or Workspace composition.

It does not define:

- package-source identity, source classification, mapping, candidate
  aggregation, payload authorization, or source failure semantics;
- package input parsing or normalized dependency evidence;
- package ID, package version, version-range, or framework grammar;
- version ordering, latest, wildcard, range, or prerelease policy;
- platform/package pruning inventory or comparison;
- dependency candidate resolution or graph traversal;
- package archive admission, content generation, or cache publication;
- compile, runtime, resource, analyzer, or build asset selection;
- framework compatibility, compile-slice ranking, asset-role preference,
  empty-group interpretation, or managed-image classification;
- artifact identity, Workspace admission, revisions, leases, replacement, or
  participant lifetime;
- platform target settlement or realization;
- Metadata, source, analysis, decompilation, rendering, or presentation; or
- host source registration, credentials, network permission, cache capacity,
  cancellation gesture, or display policy.

Those owners issue typed facts and capabilities. PackageHouse composes them
and preserves their evidence.

## Why packages need a House

Package behavior is already split into focused owners:

```text
typed package input
  -> package dependency evidence
  -> version and pruning policy
  -> configured package authorities
  -> source clients and transport
  -> exact candidate and payload
  -> package content and selected assets
  -> Workspace admission
```

The split is correct. The missing concept is the product-facing owner that
keeps one request and its evidence associated across those boundaries.

Without that owner:

- the CLI creates desktop source composition directly;
- Browser/Wasm constructs source clients, acquisition, caching, and package
  workspaces through a separate host path;
- dependency traversal resolves exact package candidates without itself
  issuing target package realization;
- Workspace acquisition can receive a coordinate without the exact selection
  request that produced its intended asset universe;
- pruning and package-to-platform delegation risk becoming host composition;
  and
- package inspection and library unwrapping can collapse into whichever
  assembly path a caller happened to select.

PackageHouse does not eliminate the focused owners. It gives their results one
settlement boundary and gives hosts one package operation to invoke.

## Relationship to package input, transport, and policy

PackageHouse is above these roles by composition, not by ownership transfer.

| Role | Owner-issued input consumed by PackageHouse |
| --- | --- |
| Package input | Normalized declaration, relationship, authorship, processing, and completion evidence from [Package Dependency Evidence](package-dependency-evidence.md) and [#6266](https://github.com/richlander/dotnet-inspect/issues/6266) |
| Source authority | Configured authority, candidate, aggregate, manifest, payload, and failure outcomes from the [Package Source Model](package-source-model.md) |
| Transport | Protocol-independent `IPackageSourceClient` operations and result identity from [Browser Package Sources](browser-package-sources.md) and `NuGetFetch` |
| Version policy | Exact, latest, wildcard, range, listing, and prerelease decisions from [Version Resolution](version-resolution.md) |
| Pruning | Exact target-bound package supply decisions from [Platform Package Supply Policy](platform-package-supply-policy.md) |
| Dependency expansion | Exact candidate and immutable graph evidence from [Package Dependency Traversal](package-dependency-traversal.md) |
| Asset selection | Generation- and request-bound outcomes from [Package Asset-selection Correspondence](package-asset-selection-correspondence.md) |
| Artifact lifetime | Generation-scoped content, contributions, and sessions from [Artifact Acquisition and Workspaces](artifact-acquisition-and-workspaces.md) |

PackageHouse does not accept transport URLs, rendered dependency rows, asset
paths, assembly names, or package labels as substitutes for those typed
inputs.

## Package Source operation lease

PackageHouse consumes one already-issued `PackageSourceOperationLease` for each
operation. The Package Source Model issues that resource from its root
settlement lifetime over host-supplied configured-authority clients.
PackageHouse does not issue, rename, wrap, retain, or publish either lease.

The root settlement generation owns one package acquisition candidate-issuer
identity. Each operation lease owns one lower-owner operation context and
exercises that generation's candidate authority. PackageHouse owns the exact
operation lease throughout settlement, complete discovery, selection, and
payload acquisition, including every asynchronous suspension. It releases the
lease synchronously on every terminal result, invalid request or plan,
unsupported profile, cancellation, timeout, and exception. The lease settles:

- caller-pinned exact candidate authorization;
- complete version discovery across one explicit discovery contract and
  `PackageSourceAuthorization`;
- exact manifest acquisition through a candidate issued by its root
  generation; and
- exact payload acquisition through a candidate issued by its root generation,
  retaining the exact source-result identity and content generation.

The authorization value remains package-source-owner evidence. The source
client remains a caller-owned capability whose result identity must match both
the exact configured-authority association and the exact client that performed
the operation. PackageHouse neither discovers a broader authority set nor
reconstructs source identity from endpoints.

Releasing the operation lease ends its context and root registration.
Completed candidate, discovery, failure, timeout, receipt, and payload evidence
remains valid as data after release. Results and receipts retain no live source
lease authority. Release does not dispose source clients, authentication
contexts, transports, payload streams, package stores, artifact content, or
Workspace participants.

`DesktopPackageSourceComposition` owns its desktop capabilities and supplies
them to one Package Source Model-issued settlement lease for its lifetime.
For an operation whose lifetime the composition owns, it observes the exact
registered authorities and partial configuration failures, issues one
request-matched operation lease, and transfers that lease to a short-lived
PackageHouse executor. The executor is stateless between calls. Its desktop
projection preserves the exact lower-owner payload attempt details required by
the compatibility result, including not-found and reporting authorities,
without publishing those details as a second House receipt or retaining live
source authority.

An overload supplied with a caller-owned `NuGetOperationContext` remains an
explicit direct compatibility branch. The composition cannot transfer a
borrowed context into PackageHouse's consumed operation lease. The synchronous
`ResolvePinnedCandidate` compatibility method also remains direct. Its
asynchronous replacement uses House when no borrowed context is supplied and
preserves the direct branch for query operations that still share one external
context. Those branches retire with their callers rather than disguising
borrowed operation authority as House ownership.

Browser/Wasm and query adapters supply their host-created clients through the
same lease contract.

This is the adopted PackageHouse operation-ownership step 17b under
[#6544](https://github.com/richlander/dotnet-inspect/issues/6544). Library
ownership, Workspace admission, and host adoption remain separate focused
steps.

## Package demand

A House request begins with one typed package demand:

- one exact normalized package coordinate;
- one package ID plus an owner-issued version-selection constraint; or
- one already-resolved package edge retaining its declaration, candidate, and
  root-relative correspondence.

Package prefix, keyword search, catalog enumeration, and ecosystem population
planning remain discovery operations. They may produce package demands, but a
prefix or search row is not itself a House package identity.

An unresolved version demand retains its original constraint beside every
selection observation. Resolution issues one exact coordinate or a typed
non-success; it does not replace the request with a display version.

The initial resource-free contract floor in #6433 exposed only the
exact-coordinate demand. The selecting arm now consumes the owner-issued
`PackageVersionSelectionRequest` and
`PackageVersionResolutionReceipt` defined by
[Package Version Selection](version-resolution.md). A selected package
decision must use the receipt's exact candidate and coordinate; a typed
non-success can only stop package settlement without manufacturing either.
A `Prior` receipt, issued by the
[Package Version Service](package-version-service.md) when a retained prior
settlement is served, is a settled arm exactly like `Resolved`: the decision
retains its exact coordinate and pinned candidate, and every post-acquisition
terminal result, success or typed failure, preserves a `Prior` decision as it
preserves a `Resolved` one. A House constructed with a
`PackageVersionServicePlan` settles its latest-family selecting demands
(`LatestStable`, `LatestPrerelease`, `AlwaysLatest`) through the service and
records a prior served as `ServedPrior` on the plan's ledger for the host's
once-per-invocation disclosure; without one, and for `Wildcard` and `Range`,
it discovers. When acquisition
after a `Prior` decision ends in the not-found outcome, the House evicts the
prior through the service and appends a `Stage(Acquisition)` failure naming
the eviction; the result is still the ordinary `NotFound`. PackageHouse does
not interpret selector text or reproduce semantic version ordering.

The candidate-bound arm accepts one `PackageAcquisitionCandidate` already
issued by the supplied operation lease's root generation. A candidate may be
issued by an earlier operation from that same generation. Execution retains
that exact candidate and its reporting-authority correspondence rather than
reauthorizing its coordinate as a broader caller-pinned demand. Every
candidate authority must remain authorized by the House; another root
generation's candidate is rejected as foreign correspondence.

`PackageHouseDependencyInputAdapter` binds that lower demand to the exact
normalized root plus one declaration or produced relationship. It retains the
root's processing result without interpreting observation absence,
incompleteness, or authorship. This adoption is not the target-aware resolved-
edge realization owned by #6424: the supplied target remains the operation
target, and no traversal occurrence or originating target correspondence is
inferred.

## Version-listing settlement

[#7530](https://github.com/richlander/dotnet-inspect/issues/7530) adds one
resource-free PackageHouse operation for raw configured-source version
listing. This is not a `PackageHouseDemand`: a raw listing preserves a
population and may disclose partial evidence without selecting or authorizing
one exact coordinate.

`PackageHouseVersionListingRequest` retains one canonical package ID, one
`Settle` operation, the explicit prerelease and unlisted policies, and an
optional caller association. PackageHouse consumes one deadline-matched
`PackageSourceOperationLease`, applies the host-authorized source plan, and
requests version discovery with those exact policies and no source-side result
limit. It acquires no manifest, payload, store, Library, or Workspace
participant.

The closed result family preserves the exact request, completed discovery when
one exists, and operation-corresponding House failures:

- `Available` retains authoritative or partial discovery that can safely
  publish raw rows;
- `NotFound` requires authoritative discovery in which no configured authority
  observed the package;
- `Incomplete`, `Rejected`, `Unavailable`, and `Failed` preserve their existing
  Package Source terminal distinctions and publish no listing rows.

An authoritative package whose versions are all excluded by the requested
prerelease or listing policy is an available empty listing, not package
absence. Partial discovery may produce available rows because raw listing
chooses no coordinate, but every lower-owner failure remains attached and the
result cannot claim authoritative absence. A failed discovery never becomes an
available empty listing. Operation timeout remains terminal and caller
cancellation produces no result.

`PackageVersionListingInspection` projects the House result into
`InspectionEnvelope<PackageVersionListingOutcome>`. Available Content retains
the normalized request, authoritative-or-partial completeness, ordered
deduplicated version rows, and per-authority source rows. Typed non-success
retains inert reason text, operation timeout, and credential-safe authority
failures. Available partial Content retains the same typed authority failures
for host policy while excluding them from serialized Content; they also become
ordered diagnostics rather than disappearing or invalidating usable raw rows.

CLI `package Package --versions`, `--versions-with-feed`,
`--include-unlisted`, and Count are the first production adopter. Existing
human, JSON, JSONL, and TSV output remains a host projection. Explicit
`--envelope` publishes the complete detached listing Content. Count is a
terminal projection over the selected version or version/source collection:
ordinary `--count` and `--count --json` emit the scalar, while `--count
--envelope` makes the same scalar the Content of an
`InspectionEnvelope<int>`. Listing Content has no redundant Count property.

Inspect Web's `BrowserPackageVersionInventory` is the second production
adopter. `BrowserPackageWorkspace` supplies its existing built-in Gallery
authorization and bounded operation lease to the same inspection, then the
inventory consumes the detached listing Document while retaining Browser-owned
current-version insertion and previous-version presentation. The direct
Gallery version-result input is retired from that inventory path.

Online exact pinned CLI verification is the third production adopter. It
requests prerelease and unlisted rows, applies the existing exact NuGet
release-version match over detached Content, and uses typed partial authority
failures to distinguish unavailable version evidence from incomplete listing
state. The direct desktop version-discovery call is retired from that exact-
pinned path.

Pinned single-row CLI verification is the fourth production adopter. It
projects `Package@Version --versions -n 1` from authoritative or usable partial
Content while preserving prerelease and unlisted evidence. The explicit
`package Package --version VERSION` spelling selects the Package for ordinary
inspection and does not create a second listing lens. Latest selection, range
vectors and cells, offline queries, and payload acquisition remain outside
this listing operation.

## Version-population settlement

[#7115](https://github.com/richlander/dotnet-inspect/issues/7115) adds one
separate PackageHouse operation for settling a configured package-version
population. This is not a `PackageHouseDemand`: demands settle one exact
coordinate, while a population settles an immutable vector whose cells may
later become exact candidate-bound demands.

`PackageHouseVersionPopulationRequest` retains:

- one canonical `PackageVersionRange`;
- one `Settle`-profile `PackageHouseOperation`;
- the explicit prerelease- and unlisted-inclusion policies; and
- an optional opaque caller association.

PackageHouse consumes one request-deadline-matched
`PackageSourceOperationLease`, obtains the current source authorization for the
range package ID, and requests one
`CompleteVersionEnumeration` from Package Source. The request may select its
unlisted-inclusive form when a consumer must retain unlisted range endpoints
or rows. Settlement acquires no
package manifest, payload, store, composition, library, or Workspace
participant.

Every terminal result retains the exact request, the completed discovery when
one exists, and operation-corresponding House failures. The closed result
family is:

- `Available` for complete authoritative discovery whose listed population
  contains both range endpoints;
- `NotFound` when every required authority settles and no authority reports
  the package;
- `NoMatch` when the package exists but the population omits an endpoint;
- `Incomplete` when a required authority or listing result does not settle;
- `Rejected` when the request or returned evidence is unusable;
- `Unavailable` when a required configured-source capability cannot answer;
  and
- `Failed` for operation, timeout, transport, or other execution failure.

Caller cancellation remains cancellation. The operation ceiling has terminal
precedence and is retained as a House operation-timeout failure, together with
any lower-owner authority timeout that established it. A failure before
discovery does not manufacture an empty discovery result.

`Available` carries the direction-preserving `PackageVersionVector` produced
under [Package Version Selection](version-resolution.md). It also retains the
exact authoritative discovery that admitted the vector. `SelectCell` accepts
only the exact `PackageVersionAddress` object held by that vector; a
structurally equivalent address from another population is foreign. The cell's
candidate is issued by calling `SelectCandidate` on the retained discovery, so
its reporting-authority set and Package Source root-generation identity are
preserved.

A cell is resource-free and carries a fresh
`PackageHouseRequestAssociation`. Later realization creates an ordinary
candidate-bound House request and consumes a fresh operation lease from the
same still-live Package Source settlement root. Candidate-generation
validation rejects execution through another root. Current source
authorization is applied again at cell execution, so a source that is no
longer authorized cannot be recovered from retained population evidence.
Population discovery and every cell execution have independent request and
operation deadlines; an overall History budget belongs to the History
coordinator.

A cell may issue one prepared execution that freezes its exact candidate,
operation, target context, asset-selection kind, library-handoff mode, and
association into one `PackageHouseRequest`. A host executes that request
without rebuilding its demand. The preparation accepts a terminal settlement
only when House evidence retains the exact prepared request object, so reusing
the public association cannot substitute a neighboring demand. The
PackageQueries
[version-cell Metadata operation](package-version-cell-metadata-inspection.md)
is the first consumer.

`DesktopPackageSourceComposition` is the first production bridge. It resolves
the configured online source policy, transfers one operation lease into
population settlement, and later transfers a fresh lease into ordinary
candidate-bound cell execution. Neither the result nor a cell retains the
composition, lease, operation context, or an execution callback. Browser/Wasm
adoption uses the same host-neutral contract in a later slice.

The original #7115 slice did not migrate current API-range or top-level
`timeline` consumers. [#7410](https://github.com/richlander/dotnet-inspect/issues/7410)
later adopts only online `package Package@A..B --versions` listing as a direct
population consumer.
[Issue #7434](https://github.com/richlander/dotnet-inspect/issues/7434)
completes that leaf as
`InspectionEnvelope<PackageVersionPopulationOutcome>`. Its available Content
contains a detached `PackageVersionPopulationDocument` with the normalized
request, direction-preserving ordinal and selector addresses, listing state,
and ordered source rows. The closed House terminal family becomes typed
non-available Content with inert reason text, timeout state, and credential-safe
authority failures. Neighboring source failures on an available population are
ordered envelope diagnostics.

Count is a terminal projection over the settled population. Its request names
either the version or version/source cohort and applies the already-bound
semantic row selection before returning a typed Count result. Ordinary
`--count` projects that result as the existing scalar; `--count --envelope`
makes the same integer the Content of an `InspectionEnvelope<int>`. Without
Count, available population Content retains only the complete Document and no
redundant Count property. The CLI's ordinary version, feed, JSON, JSONL, and
TSV renderers remain projections over the shared Document. Neither the
inspection nor the command selects or executes population cells.

This adoption does not remove a command, add History coordination, migrate
range-address payload acquisition, or inspect process-global offline state.
Offline range discovery and extraction remain on their documented legacy path
until a host explicitly adopts an offline capability. The broader target
production consumer remains top-level Diff History under
[Diff History inspection](diff-history.md), with future subject sections backed
by that same operation rather than continued standalone `timeline` behavior;
the metadata-only package version Count is now the resource-free CLI consumer
described above.

## Package target context

A request may carry one package-owned target-selection context:

- canonical requested target framework when the operation is target-aware;
- optional runtime identifier;
- the selection mode that states whether the framework is exact or an
  owner-defined default;
- optional correspondence to the typed package input, restored target, or
  dependency edge that supplied the context; and
- optional correspondence to one exact `PlatformFamilyTarget` when another
  owner has already established that relationship.

The package target context is not `PlatformFamilyTarget`.

Package frameworks include .NET Standard, platform-qualified TFMs, and other
NuGet compatibility forms that are not platform target currency. Ordinary
package selection also does not require an exact platform family and version.
Platform correspondence remains an additional fact; equal framework text
cannot manufacture it.

Requested target framework and selected asset-folder framework remain
separate. Compatibility may select a lower applicable folder. The receipt
retains both.

## Package-local compile inventory and selected projection

A package-local compile realization asks one question about one acquired
package generation: which compile-time Library assets does this package offer
under this request? PackageHouse retains two distinct owner-issued views in the
answer:

- **compile inventory** contains every available target-framework slice and
  every compile candidate observed by the compile asset-selection owner,
  including an explicitly empty reference group; and
- **selected projection** contains the one applicable slice and zero, one, or
  many selected compile assets, or one typed non-success.

Complete means complete for compile selection within the exact acquired
package-content generation. It does not mean every package entry is a compile
candidate, turn package inventory into Workspace inventory, or authorize
opening selected content. The package remains the container that retains its
full content and acquisition evidence.

PackageHouse owns the policy supplied to the selector, not the compatibility
or asset-selection algorithm:

| Package-local request | PackageHouse selection policy |
| --- | --- |
| No requested target framework | `HighestAvailable` |
| Explicit requested target framework | `ExplicitTarget` |

`HighestAvailable` asks the owner to select its highest available compile
slice. An explicit target asks the owner to select the applicable compile
slice for that request. In both cases PackageHouse preserves the policy source,
the requested framework when present, the selected framework when one exists,
and the exact selector receipt. It does not choose a framework by sorting
strings, infer one from a host default, or rerun selection over rendered paths.

The selected projection preserves these distinct outcomes:

- selected with one or many compile assets;
- selected-empty because the applicable compile group explicitly contributes
  no compile assets;
- no compile slices;
- no applicable target-framework slice;
- invalid asset correspondence; and
- operation or owner failure.

Selected-empty is successful package realization with zero Library handoffs.
No slices and no applicable slice are `NoMatch`. Invalid compile
correspondence, including lower implementation ambiguity that the compile
owner normalizes as invalid, is `Rejected`. Failures do not become an empty
projection. PackageHouse preserves all completed acquisition and selection
evidence in every arm.

The compile asset-selection owner decides slice ranking, NuGet compatibility,
reference-versus-implementation preference, explicit empty-group meaning, and
compile-to-implementation pairing. Its result must retain available slices
even when the selected projection is empty or unsuccessful. PackageHouse does
not select a namesake or representative assembly: every selected asset remains
available for a later Library-focused consumer.

The selected-slice measurement projection joins only evidence from that same
acquisition generation and compile selection receipt. It reports:

- the retained compressed package archive length;
- the selected framework and ordered available compile frameworks;
- the ordered, distinct top-level package folders whose admitted entry paths
  contain the selected asset-folder framework as a directory segment;
- one uncompressed payload length for every selected compile asset; and
- the selected Library count and sum of those payload lengths.

The selected-framework folder inventory is package layout evidence, not an
additional asset selection. For example, `lib/net10.0/Foo.dll` contributes
`lib`, while `runtimes/linux-x64/lib/net10.0/Foo.dll` contributes `runtimes`.
An entry for another framework does not contribute, and an explicit `_._`
empty-group marker is not content.

Folder names are package-authored, sink-bound text. A host-neutral projection
carries each name as `InertString` under `TextPolicy.Field`; structured formats
retain the collection as an array rather than collapsing it into presentation
text. Available framework identities originate in the same package-authored
paths and use the same containment before crossing the Package Info inspection
boundary.

One selected compile asset represents one Library measurement. When the
selector supplies a distinct implementation counterpart, including a
RID-specific implementation, its package-entry length is the Library payload
length. Otherwise the selected compile asset supplies the length. The
projection never adds both the reference and implementation entry, substitutes
an unrelated runtime asset, chooses a representative assembly, or includes an
unselected framework slice.

Measured, selected-empty, no-slice, no-applicable-slice, invalid-selection,
House-failure, and unavailable-measurement outcomes remain distinct.
Selected-empty retains package and slice measurements with zero selected
Library entries; it is not collapsed with a missing or rejected selection.
When archive length is available, no-slice, no-applicable-slice, and invalid
selection outcomes retain the package-level measurements even though they
cannot produce selected-slice measurements.
The completed projection is resource-free and retains the acquisition and
selection receipts that establish its package generation and policy
correspondence.

The host-neutral Package Info inspection lowers that typed projection into one
`InspectionEnvelope<PackageInfoMeasurements>`. Its content carries the package
size, selected framework, ordered available-framework list, selected-framework
folder inventory, selected payload size, selected Library count, and typed
non-success state. The in-process content also retains the resource-free
measurement outcome so the acquisition generation and compile-selection
receipt remain available without retaining package content. Hosts consume
these fields rather than reselecting assets or deriving measurements from
extracted paths.

CLI configured-source Package Info acquisition requests the compile realization
as part of its existing package acquisition, so measurement does not download a
second archive. A direct local-file or offline legacy extraction has no
PackageHouse realization and must not manufacture one; it may report the
archive size already established by that input path, but it does not report
House-selected slice fields.

Inspect Web's ordinary package load requests one PackageHouse compile
realization after version settlement. It projects the same
`InspectionEnvelope<PackageInfoMeasurements>` and adapts that exact settlement
through `PackageHouseRootContributionAdapter` into the package Root used by the
Browser workspace. The Browser therefore neither downloads the archive again
nor repeats compile selection for its API surface. Its facade preserves
Content, Share, and diagnostics in a Browser-local wire contract; TypeScript
retains that envelope and renders Package Info without reconstructing
measurements from paths or choosing a representative assembly.

The first ordinary request constructs its workspace from that exact contributed
Root. A repeated ordinary request for the same source-scoped package generation
and selection request may join the retained workspace even when its new
PackageHouse realization carries a separately issued selection receipt. This
logical join is specific to ordinary request admission; a caller that directly
supplies a bound Root continues to join only the workspace retaining that exact
binding identity.

An operation that wants multiple framework slices issues separately associated
package-local selections and reports them as separate projections. It does not
merge incompatible slices into one selected universe. The coordinator for that
multi-selection operation owns its completion and ordering.

## Operation profiles

Each request authorizes one maximum package work profile:

| Profile | Authorized result |
| --- | --- |
| **Settle** | Exact coordinate, authority-bearing candidate, pruning decision, or typed non-success without package payload bytes |
| **Acquire** | Settled demand plus one authorized package payload and package-content generation |
| **Realize** | Acquired package plus one typed asset-selection result for the requested package target and role |

A profile is an upper work bound, not a promise that every lower stage runs.
Pruning may complete a target-aware request with a platform delegation before
payload acquisition. A cache hit may satisfy acquisition without transport.
`Realize` may validly produce zero selected libraries while preserving
package-shaped content and the asset-selection outcome.

Dependency graph construction remains query-owned. A traversal consumer can
issue `Settle` or `Realize` operations for admitted edges; PackageHouse does
not own graph scheduling, cycle termination, depth, or traversal work budgets.

The current PackageHouse execution floor supports `Settle`, `Acquire`, and
target-aware `Realize`.
One `PackageHouse` instance retains the host's package-source authorization and
an optional `PackagePayloadAcquisitionPlan`.

`PackageHouse.ExecuteAsync` accepts one request and consumes one
request-deadline-matched `PackageSourceOperationLease` by ordinary
resource-parameter ownership transfer. `PackageHouse.ExecuteStepAsync` instead
settles one awaited request without consuming the lease, so a caller-owned
source-operation composition can validate all candidates first and execute
several requests serially. The lease still permits only one active source step;
parallel House calls through one lease are invalid.

A candidate-bound dependency invocation may additionally carry the exact
PackageHouse-issued pruning receipt for that request. Caller cancellation and
the operation ceiling are carried only by the lease's Package Source-owned
context. House operation declarations accept only deadlines representable by
that lower-owner context.

`Realize` evaluates the acquired generation through the existing compile or
runtime selector, preserves its exact receipt, and optionally projects
resource-free `PackageHouseLibraryHandoff` values. Runtime realization
currently requires an exact requested framework because the runtime selector
owns no default-framework policy; a targetless or owner-default runtime request
is visibly rejected after acquisition rather than inventing a framework.
Compile realization retains the compile selector's owner-defined
nullable-framework behavior.

Execution returns the `PackageHouseSettlement` union. Both arms carry one
closed, immutable, resource-free `Result`. `ResourceFree` carries no live
payload. `Acquired` additionally carries one caller-owned `Payload` whose exact
coordinate, producer, origin, and content generation match the result's
acquisition receipt. Every terminal result after successful acquisition remains
an `Acquired` settlement, including selection no-match, ambiguity, rejection,
and operation timeout, so package shape and completed evidence are not lost.

## Pull-based acquired payload reads

An `Acquired` settlement may open one exact package entry as a cold, single-use
read stream. This is an optional House capability for hosts that can consume
bytes progressively. It is not part of the immutable House `Result`, an
`InspectionEnvelope<TContent>`, or a resource-free receipt.

Creating the stream binds it to the settlement's acquisition receipt and live
content generation but performs no payload work. A first non-empty read opens
the entry. Each sequential read produces no more expanded content than the
caller's buffer requests, so the caller controls backpressure. Length and
position are intentionally unavailable because probing stream metadata must
not start production. Cancellation observed before the first read also leaves
the stream cold.

Reading through end of stream completes declared-size and checksum validation.
A read may therefore surface decompression, size, or checksum failure. Early
disposal means abandonment rather than successful completion. The caller owns
and disposes the stream while the acquired payload generation remains live.

The Browser/Wasm package store still retains the complete admitted `.nupkg` in
memory, and a displayed document may ultimately remain resident in the pane.
The bounded path avoids a second complete expanded-entry `byte[]`; it does not
promise zero-copy acquisition. A CLI host can copy the same stream to stdout
or a file with one bounded transfer buffer. A Browser host can decode
progressively into its final resident representation.

This capability belongs only to `PackageHouseSettlement.Acquired`. The legacy
`PackageExtractor`, extracted-file, `PackageFileContent`, and Source paths gain
no adapter and retain their current behavior. Host adoption must begin from a
live House settlement rather than wrapping an already materialized `byte[]` or
legacy file read in a stream. Package content that does not implement the
internal House pull capability fails visibly; the House does not fall back to
the legacy eager entry-opening contract. Filesystem content additionally
requires a retained package archive: its declared entry size and CRC validate
the extracted-file stream, while archive-less content is visibly unsupported.

`PackageHouseExecutionTests.ExactPayloadRead_IsColdAndPullsFromTheHouseGeneration`
gates cold start, pre-read cancellation, receipt association, and progressive
copying.
`InMemoryPackageContentTests.BoundedPullOpen_StreamsWithoutAnEntrySizedAllocation`
gates the Browser/Wasm-relevant absence of an expanded-entry-sized allocation,
`BoundedPullOpen_StreamsRealSystemTextJsonEntry` preserves the motivating
nuget.org `System.Text.Json` package, and
`PackageArchiveValidatorTests.CheckedPullRead_RejectsContentBeyondTheDeclaredLength`
preserves lazy checked-read failure.

## Shared version-settlement inspection

For one exact or latest package demand, hosts consume one
`InspectionEnvelope<PackageVersionSettlementOutcome>` from
`PackageVersionSettlementInspection`, tracked by
[#7176](https://github.com/richlander/dotnet-inspect/issues/7176).
PackageHouse remains the normative settlement owner. This inspection adopts
the existing [Inspection Envelope](inspection-envelope.md) and
[Host-observable Content Kinds](host-observable-content-kinds.md) patterns
without redefining source authorization or version selection.

The question is which exact package version settles the demand, not which
assets are applicable. The input uses the existing package coordinate:
an omitted version requests fresh latest selection with explicit prerelease
policy; a present version is an exact pin. Framework and RID, if supplied,
remain request context, not asset-selection requirements. Exact settlement
authorizes the coordinate without claiming package existence or listing state.

The shared boundary preserves:

- a `Settled` outcome containing the normalized request, exact selected
  coordinate, requested prerelease policy, discovery freshness when discovery
  occurred, and selected version/source listing evidence;
- a `NotSettled` outcome retaining the native House terminal kind, reason,
  operation-timeout fact, and credential-safe source failure kind, message,
  and timeout kind;
- required Share for that same operation; and
- ordered cross-host diagnostics for source failures accompanying a valid
  settlement, without turning typed non-success into a warning or empty result.

Version-only settlement currently has no canonical Workspace Share projection.
Its Share is explicitly `NonProjectable` at
`package-version-settlement/share`; neither host substitutes an approximate
package URL. Broader browser Workspace sharing is a separate operation and is
not overwritten by this prerequisite's Share outcome.

The returned baseline is detached and serialization-ready. It carries neither
an acquisition candidate's authority nor live source resources. Typed source
and version policy remain with their lower owners; the shared projection does
not repeat selection. Caller cancellation produces no completed envelope.
Verbose progress remains a caller-supplied log, not an inspection diagnostic.

Hosts supply their configured House and a Package Source-owned operation
lease. The inspection consumes that operation; hosts still own clients and
the source root. A payload-free operation needs no Workspace.

Production adoption has three steps within this slice: the shared boundary,
CLI `package Package@latest --versions` queries, and Inspect Web exact/latest
package opening.
The CLI retains scalar/feed/listing presentation. Inspect Web retains the
same baseline through its richer package-opening composition and transport,
then continues existing payload acquisition and Workspace admission.
Browser search-based latest settlement and the CLI-only bridge retire with
these callers. Version inventories and dependency-range resolution do not
move into this inspection.

`System.Text.Json` is the real motivating package. The PR-fast
`PackageVersionSettlementInspectionTests` gate the detached result,
equivalent-plan envelope serialization, stable/preview and exact-pin
boundaries, typed refusal and absence, diagnostics, and cancellation.
CLI `SourceScopedRoutingTests` and `PackageVersionTests` preserve production
output and neighboring paths; browser adoption owns its managed and
TypeScript boundaries. `PackageQueryInspection` is analogous shared-envelope
composition evidence, not another policy owner.

## Host-authorized plan and operation

The host supplies:

- active package source declarations or explicit source capabilities;
- package-source mapping and authorization;
- any permitted package input capability;
- network and local-source permission;
- cache and payload limits;
- one owner-issued package source operation lease carrying caller cancellation
  and request/operation deadlines; and
- any policy generation required by the focused owners.

The plan is capability, not result. Registration does not prove a source
supports the requested operation, a package exists, a version is selectable,
or a payload is authorized.

The current execution floor binds stable host capabilities to the
`PackageHouse` instance. `PackagePayloadAcquisitionPlan` groups the
authority-and-producer-scoped store provider, payload limits, transfer policy,
payload diagnostics, and payload access: complete, or ranged, which only a
`Realize` operation may use because its selection bounds the read
([Ranged payload realization](package-source-model.md#ranged-payload-realization)). It carries no source lease, operation context,
payload, or release obligation and does not take ownership of stores returned
by its provider. The resource-owner-issued operation lease remains an explicit
consumed invocation input rather than a hidden field of a House-named plan.

One House operation consumes one shared operation identity and ceiling across
all selected authorities, source routes, compatibility requests, payload
consumption, and owner adapters. Request timeout may permit owner-authorized
fallback while time remains. Operation timeout is terminal. Caller
cancellation remains cancellation.

The detailed deadline and payload-stream lifetime contracts remain with
`NuGetFetch` and the Package Source Model.

## Settlement order and invariants

The House preserves this semantic order:

1. retain the typed demand and target context;
2. establish or consume version and source-authority evidence;
3. when target-bound pruning is comparable, obtain the pruning result before
   package payload acquisition;
4. either issue one typed platform delegation or retain package processing;
5. acquire only a source-authorized exact payload when the profile permits;
6. select assets only from the acquired package generation and retained target
   context when realization is requested; and
7. issue one terminal result retaining every completed decision and failure.

The order does not require one implementation method or serial execution of
independent read-only observations. It requires that no result claims a later
transition without the owner-issued evidence that authorizes it.

In particular:

- partial source evidence cannot select latest, wildcard, or range when an
  unreadable authority could change the answer;
- `Subsumed` can skip package payload acquisition, while every other pruning
  outcome preserves package processing or visible non-success;
- a discovered coordinate can be acquired only from an authority that
  reported it under the retained discovery contract;
- a caller-pinned coordinate may succeed from one eligible authority without
  proving peer authorities readable;
- an acquired payload does not imply a selected compile or runtime asset;
- selected assets must belong to the retained package content generation; and
- no failure becomes a success-shaped empty package, dependency set, or asset
  set.

## House result and receipts

This section applies the detached-value and receipt distinctions from
[Resource Ownership and Borrowing](resource-ownership-and-borrowing.md). The
[resource-owner type map](resource-owner-type-map.md) records the corresponding
implemented PackageHouse shapes. A House receipt preserves a completed
cross-owner join; it does not mint identity merely to distinguish one object
occurrence from another.

Every terminal House result retains:

- the original request and operation profile;
- target context and its owner correspondence;
- exact coordinate when one was settled;
- version and configured-authority evidence;
- source, producer, transport, and payload provenance when present;
- pruning result and package-processing decision when applicable;
- package content generation and payload lifetime when acquired;
- requested and selected framework/RID evidence when realized;
- selected package assets and their package-relative identities;
- dependency-edge correspondence when the request came from traversal;
- completion and every typed failure; and
- the exact request and its operation identity for request- and failure-scoped
  correspondence.

The result contains four separable receipts when the corresponding work ran:

- a **version resolution receipt** binds an unresolved request to complete
  configured-authority discovery and one exact candidate or typed non-success;
- a **package decision receipt** records coordinate settlement, authority,
  pruning, and the decision to acquire, delegate, or stop; and
- a **package acquisition receipt** binds the retained decision and candidate
  to the selected configured authority, source result and producer, payload
  origin, and package-content generation; and
- a **package realization receipt** directly binds that acquisition to one
  asset-selection-owner receipt and any library-focused handoffs.

Every terminal arm retains one common immutable evidence envelope containing
the exact request, every completed receipt, and every typed failure.
A later no-match, rejection, incompleteness, or failure does not rewrite or
discard an earlier package-retention decision or successful acquisition.
Recovered request-scoped failures may accompany `Settled`; operation timeout
is terminal and cannot. House failure adaptation retains an owner-issued
authority failure and associates it with the House operation; an owner
`PackageSourceTimeoutKind.Operation` remains terminal through that adaptation.
Operation timeout takes precedence over an otherwise successful selection:
`Failed` retains the completed acquisition and realization receipts, including
selected assets or an explicit empty compile group.

Consumers may retain the decision without retaining payload bytes. A disposed
payload or expired artifact session does not erase the historical decision
evidence, but it does make content access visibly unavailable.

The evidence envelope and package-to-library handoffs do not add synthetic
settlement or handoff identities. The exact request, decision, source
association, content generation, selector receipt, and selected asset already
provide the owner-issued correspondence required by the operation. A future
consumer that needs another durable cross-operation join must obtain it from
the owner of that new boundary rather than extending PackageHouse evidence
preemptively.

The operation identity associates request-scoped deadlines and failures. It is
not a claim that repeated execution of one reusable request has a separate
durable settlement-occurrence identity.

The implemented selecting-demand floor does not yet permit pruning or platform
delegation after version resolution. That composition remains in the pruning
adoption step; it cannot be inferred by attaching an exact coordinate to the
selecting request.

## Terminal outcomes

The closed result distinguishes:

- **Settled** -- the requested profile completed with package evidence;
- **Delegated** -- package pruning issued an authorized platform delegation
  and package payload acquisition was skipped;
- **Not found** -- every required authority established absence for the
  applicable contract;
- **No match** -- available evidence could not satisfy version, target, asset,
  or role selection;
- **Ambiguous** -- complete evidence admits more than one result and policy
  does not choose between them;
- **Rejected** -- request, authority, content, policy, or admission evidence is
  unusable;
- **Unavailable** -- a required capability or source is unavailable;
- **Incomplete** -- usable evidence exists but cannot authorize the requested
  settlement; and
- **Failed** -- operation work failed without a safe partial settlement.

Caller cancellation is thrown with the caller's token and is not a terminal
House result. Operation timeout is a typed terminal failure retaining its
deadline identity.

A `Settled` package may contain zero, one, or many selected libraries.
Package-shaped inspection remains available when its acquired content and
lifetime remain available.

For a completed realization, the asset-selection owner outcome determines the
House terminal family:

- selected runtime or compile assets produce `Settled`;
- an explicit empty compile group produces `Settled` with zero library
  handoffs;
- no assets or no matching target produce `NoMatch`;
- runtime ambiguity produces `Ambiguous`; and
- invalid runtime or implementation-asset evidence produces `Rejected`.

Every non-success mapping retains the completed acquisition and realization
receipts. Constructing a realization receipt is evidence that selection ran;
it does not by itself make the operation successful.

## Target-aware dependency-edge realization

[Package dependency edge
realization](package-dependency-edge-realization.md), tracked by
[#6424](https://github.com/richlander/dotnet-inspect/issues/6424), owns the
focused handoff from one resolved dependency edge to target package
realization. The host-neutral query now prepares an exact candidate-bound
PackageHouse compile execution under the traversal target, optionally carries
the exact Platform pruning receipt, and retains the completed settlement with
the root-relative edge occurrence.

For every edge that a traversal consumer chooses to realize, PackageHouse
retains:

- the source projection and normalized declaration identity;
- the exact candidate and authority correspondence;
- the root-relative edge occurrence when the graph owner supplies it;
- the package target context used for target realization; and
- the target package's requested-versus-selected asset evidence.

The association belongs to the edge occurrence or an owner-issued shared
request referenced by that occurrence. It does not belong solely to the
semantic package node: the same exact coordinate can be reached under
different target contexts.

The defining case is:

```text
Package A realization
  requested framework: net10.0
  selected package assets: net10.0
  dependency edge: B

PackageHouse realizes B for the retained edge context
  available folders: net10.0, net11.0
  requested framework: net10.0
  selected folder: net10.0
```

Calling B with no selection context and choosing its highest folder would
violate the House correspondence. Selecting `net11.0` would violate package
framework applicability. A compatible lower folder may be valid, but its
selected identity remains distinct from the `net10.0` request.

## Package pruning and platform delegation

The package owner invokes the focused platform package supply policy before
payload acquisition when the request has complete comparable target and
package version evidence.

`PackageHousePruningReceipt.Evaluate` is the PackageHouse-owned policy entry
point. It derives the normalized policy coordinate from an exact or
candidate-bound House demand, invokes `PlatformPrunePolicy`, and validates the
returned inventory, package, requested framework, runtime identifier, platform
family, and family-version correspondence. Callers cannot attach an
independently supplied `PlatformSupply` to package and target labels.

The normalized-input consumer in `DotnetInspector.PackageQueries` authorizes a
new evaluation only for one library-declared, pre-processing declaration whose
group is the root's exact selected group. The selection must itself be
`Selected`, and its requested framework must equal the exact PackageHouse
requested framework. The selected group's own framework may be a compatible
fallback or universal group and therefore is not required to equal the
request.

Applicability precedence is explicit:

1. application-authored declarations receive an explicit exemption;
2. unattributed authorship remains unattributed;
3. incomplete, unavailable, or failed processing remains non-evaluating;
4. complete runtime projection remains runtime evidence, not restore evidence;
5. complete prior package-pruning evaluation is not evaluated again;
6. complete processing without pruning observation remains not evidenced; and
7. only `NotApplicable` pre-processing evidence proceeds to declaration,
   selection, target, and inventory correspondence checks.

Raw declaration and relationship pruning remain distinct from traversal-edge
realization. Missing selection, selected-group mismatch, missing exact target,
requested-framework mismatch, missing platform target, and unavailable
inventory remain distinct typed target-unavailable results in that
raw-evidence path.

Only `Subsumed` authorizes delegation. The package decision receipt retains:

- the original package demand;
- resolved or unresolved version evidence;
- the exact platform target used for comparison;
- the complete pruning result and supplier evidence; and
- the fact that payload acquisition did not run.

The House returns a typed `PlatformDelegation` to application orchestration.
It does not call `PlatformHouse` directly. Orchestration may issue one ordinary
platform request and retains the association between the package decision and
platform settlement receipts.

`NotSubsumed`, `NotComparable`, unavailable, ambiguous, stale, or incomplete
pruning evidence cannot become delegation.

Receipt-aware execution currently accepts only candidate-bound dependency
demands. It verifies that the candidate belongs to the operation's root
generation and remains authorized by the current House before applying the
receipt. A `Subsumed` result returns resource-free delegation without requiring
or invoking payload acquisition. Every other policy result remains on the
package path with the exact pruning receipt retained; an `Acquire` operation
then requires the ordinary authority-scoped package store capability.

## Package-shaped and library-focused results

A package remains a first-class container subject. Its metadata, dependency
declarations, assets, content entries, source, and processing evidence are not
discarded merely because a consumer also wants assemblies.

Library-focused realization is explicit. It may select zero, one, or many
compile or implementation assets and unwrap each as a provenance-retaining
bare library.

Each unwrapped library retains:

- exact package coordinate and settlement receipt;
- package content generation and package-relative asset identity;
- requested and selected target context;
- compile, runtime, reference, or implementation role;
- producer and acquisition provenance;
- correspondence to any paired compile/implementation asset; and
- the content lease required to keep bytes readable.

The compile-handoff adopter is implemented in the separately compiled
`DotnetInspector.PackageHouse.Execution` project. It accepts one exact
`PackageHouseSettlement.Acquired` and one compile handoff issued by that
settlement. Reference identity binds the handoff to the settlement, and the
Package Source-issued content generation binds both to the live payload. A
handoff from another acquired settlement remains invalid even when both
payloads contain byte-identical assemblies.

The adopter materializes only:

- the selector-issued API assembly;
- its selector-corresponded implementation assembly, when distinct; and
- the exact API-side `.xml` entry with the same directory and base name, when
  present.

It publishes those contents into one bounded Artifact generation, consumes
Metadata-issued assembly projections, constructs the package source coordinate
and Library correspondence, and transfers one Artifact content child per
distinct Library content record into `LibraryContentOwner`. One exact `lib/`
asset serving both assembly roles transfers one child with both roles. A
missing compiled-XML entry is valid absence; a present over-budget entry is a
typed terminal result.

Completion transfers `LibraryContentOwner` and `ArtifactSetSession` as
separate caller-owned authorities. The Library owner must retire before the
Artifact session. Terminal receipts and per-content provenance retain no
payload, owner, lease, stream, opener, or callback. PackageHouse continues to
issue the resource-free handoff; the separately compiled adopter owns Artifact,
Metadata, and Library composition.

The House does not choose one assembly because its file name resembles the
package ID unless the asset-selection owner explicitly defines that role.
Shared Library inspection begins only after this handoff.

## Workspace boundary

`Workspace` owns registration, transactions, revisions, admission, order,
replacement, leases, and participant lifetime. It does not own package source,
version, pruning, payload, or asset-selection policy.

For package realization, Workspace orchestration:

1. forms or receives one typed package demand and target context;
2. supplies host-authorized capabilities and operation limits;
3. calls PackageHouse;
4. retains the House result and receipts;
5. admits only the returned package contribution and selected library
   contributions; and
6. reports or preserves every non-success without reconstructing a neighboring
   package result.

PackageHouse does not mutate Workspace. It returns an immutable result and
resource-free Library handoff while its acquired settlement retains the
separately caller-owned package payload. The PackageHouse execution adopter
can turn one exact compile handoff into separately transferred Library and
Artifact authorities for a later Workspace transaction. Rejected admission
disposes adopter-created resources under the Artifact owner contract; an
existing Workspace remains unchanged.

`DotnetInspector.PackageQueries` owns the narrow House-to-Root adapter.
`PackageHouseRootContributionAdapter` accepts the complete closed House
settlement rather than host-reconstructed payload and receipt parameters. An
acquired compile realization ending in `Settled`, `NoMatch`, or selection
`Rejected` whose complete package Root coordinate is representable produces
one `PackageHouseRootContribution` that pairs:

- the exact `PackageHouseResult`;
- the exact `PackageHouseRealizationReceipt.Compile`; and
- one Artifact Acquisition-issued `PackageRootBinding`.

The Queries-owned binding primitive validates the acquired package id and
content generation against the exact
`PackageCompileAssetSelectionReceipt`, then freezes
`receipt.Selection` directly. It does not call either package asset selector.
The `PackageRootBinding` remains House-agnostic and retains no House receipt;
the transient contribution is the cross-owner correspondence.

Resource-free settlements, runtime realizations, early acquisition or
selection failures, operation-timeout failures, and acquired coordinates
outside the existing package Root grammar return a typed no-contribution
outcome preserving the original House result. The package Root coordinate
records the Package Source-issued portable producer token, so configured HTTP
and local producers contribute without exposing their complete producer key.
The coordinate still cannot represent the broader Unicode package-id grammar
accepted by Package Source. Until
[#6967](https://github.com/richlander/dotnet-inspect/issues/6967) reconciles
the package-id grammars, the adapter reports `CoordinateNotRepresentable`
rather than throwing, guessing another identity, or weakening correspondence.

An exact package Root reacquisition request from
[#5837](https://github.com/richlander/dotnet-inspect/issues/5837) re-enters the
same House realization path. It retains the original selection request while
allowing a replacement physical generation.

## Host boundaries

The CLI and Inspect Web own:

- command or gesture interpretation;
- source registration and credential input;
- online, offline, and local capability selection;
- operation cancellation and host capacity;
- Workspace lifetime;
- section and row selection; and
- diagnostics and presentation.

They do not own package candidate completeness, version selection, pruning,
payload authorization, target-aware edge realization, or asset-selection
semantics.

Browser/Wasm can supply Gallery, configurable HTTP, in-memory, and supported
local capabilities without receiving a desktop constructor or filesystem
assumption. The CLI can supply installed configuration, package-source
mapping, credential-provider, filesystem, and cache capabilities. Equal
authorized inputs produce the same House package decision and realization
semantics.

This owner carries typed operation data but no rendering model. CLI output
uses Markout through its presentation owners. Inspect Web lowers the same
typed result through its Browser boundary and owns interactive rendering.

## Project and dependency boundaries

The logical House owner may span focused projects. A project name alone does
not define the owner.

The intended dependency shape is:

```text
NuGetFetch and package source capabilities
               |
               v
DotnetInspector.Packages
  identity, source, version, pruning, payload, asset selection
  PackageHouse request/result contracts and lower settlement
               |
               +----------------------+
               |                      |
               v                      v
DotnetInspector.Queries      DotnetInspector.PackageQueries
  evidence and traversal      package/query adapters and composition
               |                      |
               +----------+-----------+
                          |
                          v
              Workspace orchestration
                          |
                 +--------+--------+
                 |                 |
                 v                 v
           DotnetInspect.Cli  DotnetInspect.Web
```

Host-neutral PackageHouse source settlement remains inside
`DotnetInspector.Packages`. The normalized dependency-input adapter belongs in
`DotnetInspector.PackageQueries` because its positive responsibility is the
narrow handoff from Queries-owned evidence and dependency-candidate results to
Packages-owned House requests. That placement follows the existing
dependency-candidate and traversal adapter seam; it does not establish a home
for unrelated Packages-plus-Queries composition. The broader project
rationalization in
[#6432](https://github.com/richlander/dotnet-inspect/issues/6432) remains open.

`DotnetInspector.Queries` does not depend on a higher project merely to keep
its evidence and traversal algorithms usable. PackageHouse does not introduce
a generic Houses assembly. Exact moves from `DotnetInspector.Services` remain
tracked by [#6335](https://github.com/richlander/dotnet-inspect/issues/6335).

## Current implementation retirement

Retirement is staged:

1. introduce the House contract and host-neutral source capability
   construction;
2. move existing package operations behind the House without behavior change;
3. adopt Workspace, CLI, and Browser/Wasm one owner at a time;
4. remove direct host package-policy paths after their replacement is live;
5. remove the `DesktopPackageSourceComposition` identity; and
6. close or supersede #4653 after every carried requirement and unresolved
   finding is assigned to a landed focused slice or explicit residual issue.

No compatibility facade or obsolete CLI path is retained solely to preserve
the old architecture. A direct path remains only while it is the shipping path
for a supported scenario.

## Composition evidence

The Package Source Model's TLA+ model continues to own concurrent authority,
route fallback, association, completeness, and operation-timeout behavior.
PackageHouse consumes those checked outcomes rather than copying the source
state machine.

The initial House adds deterministic evidence-preserving orchestration. No new
TLA+ model is selected for the design slice. An implementation that introduces
concurrent pruning, settlement, acquisition, or realization transitions must
first define the new scheduling property and compose or extend the existing
model rather than relying on tests to explore races.

The execution gates use the real package identity
`Microsoft.Extensions.Logging@10.0.0` with deterministic source doubles. This
keeps the motivating package coordinate recognizable while proving settlement,
selection, source restriction, and payload correspondence without making the
Release suite depend on nuget.org availability.

The sole-facade composition boundary uses positive outcome canaries and design
review. Repository-wide absence of bypasses is **unverified**; no automated
source or compiled-graph scan is required. Each adoption proves its
representative product path and retires the direct path it replaces.

## Pathological cases

### Dependency offers a newer asset folder

Package A is realized for `net10.0` and reaches package B. B contains
`net10.0` and `net11.0` asset folders. The edge realization retains
`net10.0`, selects B's `net10.0` assets, and records requested and selected
frameworks separately.

### Same package coordinate, different target contexts

Two roots reach `Contoso.Json@4.0.0`, one for `net8.0` and one for `net10.0`.
The semantic coordinate can be shared, but the realization receipts and
selected asset universes remain distinct. A node cache keyed only by coordinate
cannot answer both.

### Partial sources cannot select latest

One configured authority reports `4.0.0`; another required authority times
out. The House may return partial discovery evidence, but it cannot settle
`latest`, a wildcard, or a range when the unread authority could change the
answer.

### Package is supplied by Platform

A target-bound `System.Text.Json` package demand is proven `Subsumed`. The
House skips package payload acquisition and returns a platform delegation plus
the complete package decision receipt. It does not infer
`System.Text.Json.dll` or call PlatformHouse directly.

### Package contains multiple libraries

One package realizes three selected compile assets. Package-shaped inspection
retains the container and all asset evidence. A library-focused request emits
three provenance-retaining library handoffs; the House does not silently pick
one by enumeration order.

### Package contains no applicable library

The payload is valid package content but the requested target has no applicable
compile group. The result preserves package inspection and returns typed asset
`No match`; it does not become package acquisition failure or a success-shaped
empty library.

### Browser and CLI use equivalent authorities

Browser Gallery and CLI NuGet.org capabilities report equivalent
authority-bearing package evidence under their owner contracts. PackageHouse
applies the same candidate, version, pruning, and realization semantics while
retaining their distinct producer and transport provenance.

### Released source operation retains evidence

A Package Source Model-issued operation lease issues an exact candidate and
settles a manifest failure. Its consumer releases the operation. The candidate
and failure retain their existing evidence semantics, but another candidate
resolution or manifest operation through that lease is rejected. The source
client remains alive because its host, not PackageHouse, owns it.

### Direct library bypasses PackageHouse

A user supplies one DLL path. It enters shared Library inspection with direct
provenance. It is not wrapped in a package request merely to create symmetry.

### Platform pack remains Platform-shaped

PlatformHouse acquires a targeting or runtime pack through a package-backed
source adapter. The pack ID and archive remain source coordinates for that
platform operation; no ordinary Package participant or PackageHouse result is
manufactured unless a separate explicit package demand exists.

## Analogous implementation evidence

Three existing designs bound this House:

- the Package Source Model already demonstrates package-owned aggregation over
  multiple typed source clients and exact authority association;
- PlatformHouse demonstrates a peer container/source facade that preserves
  target and source evidence while leaving lower algorithms with their owners;
  and
- artifact Workspace sessions demonstrate immutable contribution and lifetime
  handoff without moving admission policy into a source owner.

NuGet asset selection also demonstrates the central target-context distinction:
one requested target chooses one applicable package asset universe. PackageHouse
preserves that request across package boundaries; it does not copy NuGet's
restore graph or compatibility implementation.

The analogy is not authority. Packages remain independently published
containers selected through configured authorities. Platform is a coherent
family realization. Their Houses converge only at typed orchestration and the
provenance-retaining Library handoff.

## Adoption plan

[#6426](https://github.com/richlander/dotnet-inspect/issues/6426) owns eleven
counted production-adoption steps:

1. Lock this focused PackageHouse composition design and adoption/retirement
   plan.
2. Define the resource-free request, operation, result, receipt, and
   package-to-library/platform handoff contracts.
3. Generalize `DesktopPackageSourceComposition` to the Package Source
   Model-issued, host-neutral source-settlement lease over injected source
   capabilities tracked by #6477 and #6545.
4. Route version settlement, candidate authorization, manifest acquisition,
   and payload acquisition through the House.
5. Adopt normalized package input and processing evidence from #6266.
6. Apply package-owned pruning before payload acquisition and issue typed
   platform delegation.
7. Adopt target-aware dependency-edge realization from #6424.
8. Route package Root construction, durable reacquisition, multi-package
   realization, and participant lifetime through shared Workspace
   orchestration.
9. Adopt the House in CLI package operations and dependency expansion.
10. Adopt the same contracts in Inspect Web Browser/Wasm.
11. Move remaining package components from `DotnetInspector.Services`, remove
    direct host bypasses and the desktop composition identity, and close or
    supersede #4653.

Each step after this design is separately reviewed and leaves a usable
product. A direct path is retired only after its House replacement is live in
every supported host that uses it.

The compile-handoff Library materializer tracked by
[#7324](https://github.com/richlander/dotnet-inspect/issues/7324) implements the
PackageHouse adopter required by Library-ownership slice 4 and supplies the
live-Library prerequisite for step 8. Workspace and host adoption remain
separate work.

[#7423](https://github.com/richlander/dotnet-inspect/issues/7423) adds a
separate package-slice sequence:

1. lock the package-local compile inventory and selected-projection contract
   in this document;
2. expose the complete owner-issued selection evidence through #7431;
3. lock aggregate and explicit narrowing gestures in #7318;
4. adopt the result in Package/Library CLI, API/Type/Member, Find,
   Workspace/Navigation, and Inspect Web through #7430, #7429, #7433, #7432,
   and #7428; and
5. retire command-local package target, role, and representative-assembly
   policy as each replacement lands.

Package Dependency Query scope, size measurements, traversal targets, and call
graphs remain later #7423 sequences. This slice does not authorize or specify
them.

## Required gates

| Claim | Required Release evidence |
| --- | --- |
| Execution floor | Exact, candidate-bound, and typed selecting `Settle`, `Acquire`, and target-aware `Realize` operations produce closed House results. Realize composes selector-issued compile/runtime receipts and preserves acquired package shape for selected, explicit-empty, and typed non-success outcomes. |
| Request association | A result retains the exact demand, operation identity, and target context without reconstructing them from display values or adding a second settlement identity. |
| Normalized input adoption | One exact declaration or produced relationship, its root, authorship, processing result, candidate, target context, and House association remain linked without policy interpretation. |
| Terminal evidence | Every terminal arm retains the same immutable evidence envelope, completed receipts, and typed failures; direct and owner-adapted operation timeouts cannot produce success. |
| Source lease authority | One operation lease acts for one root-generation candidate issuer; candidate-bound demands preserve the issued authority subset, while candidates from another root generation and clients or results from another configured-authority association are rejected. |
| Source operation ownership | House execution releases its transferred operation after success, typed failure, invalid plan or deadline, unsupported profile, caller cancellation, operation timeout, and source exception; root settlement can then complete. |
| Source result independence | House results, decisions, candidates, evidence, receipts, and acquired payloads retain no operation lease or live source authority. |
| Source capability ownership | Releasing an operation or settling its root does not dispose caller-owned clients, stores, or retained payload content. |
| Source completeness | Partial authority evidence cannot settle latest, wildcard, range, or authoritative absence, and cannot reach package-store or payload work. |
| Version listing | Raw listing preserves authoritative or partial source evidence, publishes usable partial rows with failures disclosed, requires authoritative evidence for package absence, and never acquires payloads. |
| Version population | One complete metadata-only discovery serves multiple exact population cells without rediscovery; cells preserve reporting authorities, require their exact vector address, use fresh operations, and reject another Package Source root generation. |
| Source-operation stepping | `DependencyMemberCallGraphExecutesEdgesInTraversalOrder` proves that one caller-owned lease executes several House requests serially, while `DependencyMemberCallGraphRejectsForeignCandidatesBeforeSourceWork` proves complete candidate-generation validation before the first source step. Existing single-request gates continue to prove that `ExecuteAsync` consumes its lease. |
| Pruning order | `KnownPlatformPackageAcquireDelegatesBeforePayloadCapability` and `CandidateRealizeDelegatesBeforePayloadCapability` prove that `Subsumed` skips payload acquisition for either upper work profile; neighboring pruning states cannot issue platform delegation. |
| Pruning correspondence | Platform delegation consumes the policy-issued inventory/coordinate/supply receipt, compares the coordinate through the package owner's normalization, and matches the actual supplier family and exact target version. |
| Payload authority | Discovered payload comes only from a reporting authority; pinned payload follows the Package Source Model's eligible-authority rule; the acquisition receipt and live payload match source, producer, origin, and generation. |
| Selection correspondence | Realization consumes a selector-issued generation/request/outcome receipt matching the exact acquisition and target context. |
| Package-local policy | Owner-default compile realization records `HighestAvailable`; explicit-target realization records `ExplicitTarget`, the requested framework, and the separately selected framework. |
| Compile inventory | Selected, selected-empty, no-slice, no-applicable-slice, and invalid-selection outcomes retain every owner-issued available compile slice and candidate from the acquired generation through the final House result. |
| Projection cardinality | Zero, one, and many selected compile assets remain distinct valid projections; no path or package-name heuristic chooses a representative asset. |
| Selected-slice measurements | Multi-Library selection measures the retained archive and exactly one owner-paired payload entry per selected compile asset, including RID-specific implementation preference. Selected-empty, no-slice, no-applicable-slice, invalid-selection, House-failure, and unavailable entry-manifest outcomes remain typed and retain the acquisition and selection receipts. |
| Multi-slice isolation | A coordinator selecting multiple target frameworks receives separately associated projections and cannot merge their assets into one selected universe. |
| Selection completion | `ExactCompileRealizeBindsSelectionAndLibraryHandoff`, `ExactRuntimeRealizeAppliesExactRidOverlay`, `ExactCompileRealizePreservesExplicitEmptyGroup`, `ExactCompileRealizePreservesNoMatchWithPayload`, `RuntimeRealizeKeepsRequestedAndSelectedFrameworksDistinct`, `SameCoordinateWithTwoTargetsKeepsDistinctRealizations`, `NonSubsumedCandidateRealizeRetainsPruningAndSelection`, and `RuntimeOwnerDefaultRealizeIsVisiblyRejected` compose selector-issued outcomes through execution. Selector suites gate ambiguity and invalid-layout classification; `PackageHouseContractTests` gate their corresponding House terminal arms. |
| Selection timeout | `TimeoutAfterSelectionRetainsPayloadAndRealization` proves that operation timeout remains terminal after synchronous selection while retaining the caller-owned payload and completed acquisition and realization receipts. |
| Target-aware realization | A `net10.0` dependency with `net10.0` and `net11.0` folders selects `net10.0` and retains requested-versus-selected evidence. |
| Context separation | The same coordinate realized under two target contexts retains two realization receipts and cannot share one selected asset universe. |
| Package shape | Zero, one, and many selected library outcomes preserve package-shaped inspection and typed asset status. |
| Library materialization | `CompileHandoffMaterializesOwnedLibraryWithImplementationAndDocumentation` uses real `System.Text.Json` API, implementation, and compiled-XML package entries to produce one three-content Library backed by one Artifact generation. Focused neighboring gates bind the exact handoff and payload generation, reject byte-identical cross-settlement handoffs, transfer one content child for a `lib/` asset serving both assembly roles, preserve missing XML as valid absence, and keep missing assemblies, content and aggregate byte limits, malformed managed metadata, identity mismatch, cancellation, and retirement ordering visible. `MaterializationEvidenceContractsAreResourceFree` gates the detached evidence closure. |
| Workspace handoff | Admission consumes one immutable House realization; rejection leaves the prior Workspace unchanged and disposes rejected resources correctly. |
| Platform delegation | Package and platform receipts remain separately typed and associated by orchestration without a House-to-House call. |
| Host equivalence | CLI and Browser/Wasm canaries over equivalent owner-issued inputs observe the same House settlement semantics. |
| Retirement | Each adopting owner has a positive House-path canary before its direct path is removed. Repository-wide bypass absence remains unverified. |

Focused owner suites enforce their own algorithms. House tests compose public
owner outcomes; they do not manufacture package evidence or inspect private
test seams.

The Release gates for the desktop adoption are:

- `ConfiguredPayloadAcquisitionTests`, covering exact and selected acquisition,
  partial authorization, required-producer filtering, reporting-authority
  fallback, invalid selection, timeouts, cancellation, and composition
  settlement; `DesktopSettlementWaitsForPayloadBeforeReleasingClients`
  preserves synchronous post-disposal rejection for valid and invalid
  requests, and
  `AcquireSelected_TransportFallbackRetainsOriginalSourceSelection` keeps
  pre-acquisition source-set correspondence independent of payload-attempt
  failures, with
  `CandidateManifest_UsesHouseOwnedDesktopOperation` as the positive
  composition-owned candidate and manifest canary;
- `PackageHouseExecutionTests.ExactAcquireBindsLivePayloadToResourceFreeReceipt`
  and
  `PackageHouseExecutionTests.SelectingAcquireUsesOnlyAuthoritiesThatReportedSelection`,
  covering the compatibility details projected from exact House settlement;
  and
- `PackageHouseExecutionTests.CandidateManifestReleasesTransferredOperationWhenSourceThrows`
  and
  `PackageHouseExecutionTests.CandidateManifestRejectsForeignGenerationAndReleasesOperation`,
  covering candidate-manifest generation correspondence and terminal lease
  release.

## Demo

### Package-local selection remains plural

```text
Contoso.Tools@3.0.0 acquired generation C1
  ref/net8.0/Contoso.Tools.dll
  ref/net8.0/Contoso.Tools.Abstractions.dll
  ref/net10.0/Contoso.Tools.dll
  ref/net10.0/Contoso.Tools.Abstractions.dll

PackageHouse compile realization
  policy: HighestAvailable
  requested framework: absent
  available slices: net8.0, net10.0
  selected framework: net10.0
  selected assets:
    Contoso.Tools.dll
    Contoso.Tools.Abstractions.dll
```

The result keeps both available slices, selects one owner-ranked slice, and
retains both selected Libraries. PackageHouse does not choose the package-
named assembly as a representative.

### Evidence follows owner-issued correspondence

```text
System.Text.Json@10.0.0 request + operation identity
  -> decision retains the exact candidate
  -> acquisition adds authority, source, origin, and content generation
  -> realization adds the selector-issued receipt
  -> each library handoff adds one selected package asset
```

PackageHouse does not copy the candidate into the acquisition receipt, wrap
the selector receipt in a second asset-selection receipt, or mint settlement
and handoff identities that no owner consumes. Live payload and later Library
ownership remain separate from this resource-free evidence.

### Target context crosses a dependency edge

```text
Workspace package request
  package: A@1.0.0
  target: net10.0

PackageHouse
  settle A -> authorized A@1.0.0
  acquire A -> package generation A1
  select A assets -> lib/net10.0
  traverse selected dependency edge -> B
  settle B -> authorized B@2.0.0
  realize B with retained target net10.0

B package folders
  lib/net10.0
  lib/net11.0

result
  requested: net10.0
  selected: lib/net10.0
  edge and package realization receipts retained
```

### Package exits through platform delegation

```text
PackageHouse request
  package: System.Text.Json@10.0.0
  exact platform target: DotNetRuntime/net11.0/11.0.0

pruning: Subsumed
payload acquisition: skipped

PackageHouse result
  Delegated
  package decision receipt
  typed PlatformDelegation

application orchestration
  -> ordinary PlatformHouse request
  -> separately retained platform settlement receipt
```

### Package remains a container

```text
PackageHouse realization
  package: Contoso.Tools@3.0.0
  selected libraries:
    Contoso.Tools.dll
    Contoso.Tools.Abstractions.dll

package inspection
  retains nuspec, dependencies, content, assets, and source evidence

library-focused handoff
  emits two libraries with package provenance
```

## Non-claims

This design does not:

- define one universal package input format;
- make PackageHouse a parser, transport, cache, query, or Workspace bucket;
- change NuGet framework compatibility or restore behavior;
- define dependency graph traversal, version selection, pruning comparison, or
  asset-selection algorithms;
- define Package Query dependency scope, size measurements, traversal-target
  defaults, call-graph execution, aggregate Navigation, or host syntax;
- make package search, package prefixes, or ecosystem registration House
  identities;
- imply one assembly per package;
- infer package ownership from assembly names, paths, namespaces, or display
  text;
- turn `PlatformFamilyTarget` into package framework currency;
- call PlatformHouse directly or absorb platform settlement;
- make platform distribution packs ordinary Package participants;
- define CLI flags, Browser views, Markout schemas, or presentation;
- add a generic Houses project;
- require a repository-wide source or dependency-graph bypass scan; or
- claim #4653's unresolved findings are fixed until focused replacement slices
  land their corresponding behavior.
