# dotnet-inspect browser prototype

This prototype explores a type-first, keyboard-driven browser experience for
`dotnet-inspect`. This branch is a **thin-engine rebuild on current `main`**, and
its organising rule is:

> **Every interaction that inspects an assembly runs inside a workspace, through
> a public product query that owns the session.** An operation with no such query
> is exported as explicitly unsupported and reported as a product API gap. It is
> never answered by opening a session, a metadata source, an analysis index, or a
> retained image descriptor.

The previous browser host was a single 4,103-line `Program.cs` that re-derived
package acquisition, target-framework ranking, symbol acquisition, and member
identity for itself, and opened assemblies wherever it needed one. It was not
carried forward.

`InspectWeb.Engine` remains the executable Browser/Wasm host and the owner of
all current exports and wire DTOs. `InspectWeb.Engine.Core` is its one-way,
implementation-only dependency for shared package/platform workspaces,
operation lifetimes, browser host policy, and typed internal results. Engine
maps those results to its wire DTOs; Core contains no `[JSExport]` method or
generated serializer context. `EngineCoreProject_HasOneWayOwnerReference`,
`EngineCoreAssembly_OwnsSharedWorkspaceState`, and
`EngineCoreAssembly_HasNoFacadeContracts` gate that boundary.

The rule is enforced by the compiler, not by a convention.
`engine/BannedSymbols.txt` bans `AssemblyInspectionSession`, `MetadataSource`,
`LibraryBodyIndex`, `AssemblyImageSnapshot`, raw metadata readers, descriptor
factories, and the group's image and retained-descriptor accessors in this
project, and `Directory.Build.targets` already escalates `RS0030` to an error
for every project.
`BrowserEngineLayeringTests` in `engine.Tests` pins that wiring and
resolves every complete banned documentation id, including generic arity and
parameter types — a renamed or malformed entry bans nothing and fails the gate.
It also bans opening a retained descriptor, minting one, or invoking
`AssemblyReader` in the host; descriptors may carry typed identity into a
product query, but package selection, identity decoding, descriptor creation,
and image content remain product-owned. A selected malformed entry receives an
artifact-neutral, role-unique identity only as a rejection carrier, so the
workspace returns its typed failure instead of silently shortening the
selected assembly set.

## How a workspace is opened

1. **Resolve an exact identity.** `PackageSourceCoordinateResolver` validates
   the package id. An exact pin bypasses discovery; an omitted version uses the
   NuGet Gallery search endpoint and accepts only the exact package ID's listed
   stable result. Neither path requests the NuGet.org v3 service index. The
   Browser adapter then selects one target framework — never "whatever the
   package happens to ship".
2. **Select and realize typed roles.** `PackagePayloadAcquisition` downloads and
   admits the package from the Gallery package CDN through the shared typed
   source, transport, and archive policy. The Gallery payload carries its
   advertised length into the Browser reservation policy before body
   materialization.
   `PackageAssemblyContextSelection` applies
   `PackageCompileAssetSelector`'s reference-group semantics around the
   implementation universe selected by `PackageAssetSelector`.
   `InspectionWorkspace.RealizePackageAssemblyContextRoles` decodes each
   healthy entry's real metadata identity, mints the descriptors, retains
   malformed/native/module entries as rejection carriers, applies the
   Browser-supplied admission policy, and creates the coordinated surface and
   implementation roles. It owns equivalent-identity rejection,
   reference-only surfaces, exact asset/participant associations, and exact
   surface-to-implementation correspondence. Browser retains transport,
   cache/deadline/lifetime policy, the 64 MB and 256-assembly limit values, and
   its coordinate/asset provenance adapter; it does not select package assets,
   decode identities, mint descriptors, or compose groups.
   The Browser adapter places one 30-second operation deadline around coordinate
   resolution and payload acquisition. The deadline token flows through the
   shared resolver, retry, response-body, archive-validation, and store paths;
   expiry is surfaced as a visible timeout instead of leaving the page behind
   an unbounded loading indicator.
   Gallery version enumeration joins the flat-container list with bounded
   SemVer2 registration metadata. Listed and unlisted versions remain visible
   to the version picker, while dependency wildcard and range selection uses
   listed versions only. A registration outage returns a typed partial list
   with unknown state, keeps the picker available, and makes range selection
   fail closed; exact dependency pins remain available.
   `GalleryEnumerationJoinsAuthoritativeListingState`,
   `GalleryExternalRegistrationPageIsValidatedAndRebased`,
   `GalleryExternalPagesUseBoundedConcurrency`,
   `GalleryRegistrationParserRetainsOnlyFlatCandidates`,
   `GalleryRegistrationAggregateByteLimitIsTypedPartialEnumeration`,
   `GalleryRegistrationDefaultAggregateCoversMeasuredMassTransitCanary`,
   `GalleryRegistrationDefaultBatchExceedsPerResponseLimit`,
   `GalleryRegistrationReservationWaitsForReturnedCapacity`,
   `GalleryRegistrationMaterializationBudgetReturnsFailedAttemptCapacity`,
   `GalleryLatePageDeadlineReturnsMaterializationCapacity`,
   `GalleryCleanupFailureReturnsMaterializationCapacity`,
   `GalleryRegistrationAggregateCountsFailedAttemptBytes`,
   `GalleryRegistrationLeafLimitIsTypedPartialEnumeration`,
   `GalleryRegistrationPageLimitIsTypedPartialEnumeration`,
   `GalleryRegistrationTraversalHonorsCallerCancellation`,
   `GalleryRegistrationTraversalUsesMonotonicDeadline`,
   `RegistrationResourceLimitsMapToResponseRejected`,
   `GalleryMalformedRegistrationIsTypedPartialEnumeration`,
   `GalleryCorruptEncodedRegistrationIsTypedPartialEnumeration`,
   `GalleryIncompleteRegistrationIsTypedPartialEnumeration`, and
   `GalleryFinalListingProjectionPreservesOperationTimeout` gate the source
   contract.
   `GalleryCallerCancellationDuringRegistrationRemainsCancellation` and
   `GalleryCallerCancellationOutranksConcurrentRegistrationFault` distinguish
   actual caller cancellation from optional-registration fallback.
   `DependencyRangeUsesAuthoritativeGalleryListingState`,
   `DependencyRangePreservesGalleryRegistrationTimeout`,
   `BrowserGalleryDeadlineLeavesTimeForSourceTimeout`, and
   `VersionPickerPreservesGalleryRegistrationTimeout` gate Browser
   consumption.
3. **Hand a role group to a query.** The participants open one
   `InspectionWorkspace` and one or two binding-consistent
   `AssemblyContextGroup` instances. `BrowserInspectionScope` exposes exactly
   two hand-offs — `Use(group => query(group))` and
   `UseParticipant(participant, (group, participant) => query(...))` — and no
   accessor for a session, an image, or a descriptor.

A workspace is **keyed by its complete exact coordinate set and reused**. The
package surface, a type projection, an annotated member, Integrations,
Opportunities, and a composite call-graph workspace over several packages all
reach the same open group rather than reacquiring every image.
`BrowserPackageWorkspace` keeps at most four scopes and disposes the least
recently used one on eviction, which is what returns its retained image bytes.
A scope carries a 64 MB aggregate retained-image budget. Two distinct
compile/implementation groups receive 32 MB each; a shared or reference-only
single group receives the full 64 MB. Before decoding any identity, the host
rejects a role whose declared expanded assembly total exceeds its group budget
or whose selected set exceeds 256 assemblies. Product realization enforces
those Browser-supplied values, keeping identity decoding itself inside the same
bound rather than relying on the later retained-snapshot check.
Failures after the role passes that preflight remain typed participant outcomes
beside healthy results.

`BrowserPlatformWorkspace` uses the same package cache, aggregate byte
accounting, operation deadline, and four-scope registry. The first selected
assembly for a target framework and platform family records the exact pack
version and producer. Later lazy selections re-acquire from that pin and
replace the old scope with one cumulative binding-consistent group, so runtime
and ASP.NET Core libraries never drift across versions or feeds and call
graphs can lazily acquire a selected target and see every resident platform
assembly. Surfaces and graph targets carry the pack membership recorded from
those acquired implementation archives from the product loader's
metadata-derived identities in its selected asset universe, so navigation does
not depend on archive filenames or on the optional static index knowing the
active framework. One graph expansion submits its complete missing assembly set
as one batch, so the cumulative workspace is rebuilt once under one package
operation deadline rather than once per assembly. Browser models qualify
assembly residency by platform pack, and one target cannot select the same
simple assembly name from both runtime and ASP.NET Core families; ambiguous
pack inference fails visibly rather than routing by first match. Reuse updates
both the shared scope LRU and its archive recency. Eviction severs the disposed
scope's loaded context, removes its scope reference, and removes it from the
registry, releasing the package content closures whose archives leave cache
accounting. Exact coordinates remain in a lightweight four-target LRU so an
evicted cumulative workspace can be re-acquired without version drift or lost
participants. Every archive is temporarily leased as soon as acquisition
returns it and until the cumulative candidate is registered or abandoned, so a
later family download cannot evict bytes that the unregistered candidate still
holds. Shared links carry the canonical `:Platform` group version and selected library
identity. An exact version bypasses discovery, keys retained Platform state
separately from floating acquisition, and follows every later assembly and
query operation; a different resident patch cannot satisfy it. A missing pin or
the Browser `latest` sentinel remains floating and uses version discovery.
`PlatformWorkspace_ExactVersionSkipsDiscoveryAndDoesNotReuseLatestState` and
`PlatformWorkspace_LatestSentinelUsesVersionDiscovery` gate those behaviors.
Initial member graphs use the same escaped definition identity
as subsequent graph descent. Platform graph loads and descents also carry the target's complete
assembly identity and reject an acquired root that is not binding-equivalent,
rather than applying a valid selector to a different assembly version or
public-key token. A selected Platform coordinate that matches multiple full
metadata identities fails typed rather than choosing by archive order. The
Platform workspace admits at most 256 realized assemblies and retains at most
64 MB of opened images.
`BrowserEngineBoundaryTests.PlatformWorkspace_PinsAndAccumulatesSelectedAssemblies`,
`PlatformWorkspace_BatchesCumulativeAssemblyExpansion`,
`PlatformWorkspace_RejectsOneNameAcrossPackFamilies`,
`PlatformWorkspace_UsesMetadataIdentityForPackMembership`,
`PlatformWorkspace_LeasesArchivesUntilCandidateRegistration`,
`PlatformWorkspace_ReuseTouchesTheSharedScopeLru`,
`PlatformWorkspace_EvictionRemovesRetainedTargetState`,
`PlatformWorkspace_CanceledQueueEntryPreservesSerialization`,
`PlatformWorkspace_RejectsInvalidSelectionsBeforeNetwork`, and
`PlatformWorkspace_RejectsAssemblyCountAboveBrowserBound` gate those host
contracts.

Because a scope is reused, nothing here runs the terminal participant-streaming
forms of `AssemblyContextIntegrationsQuery` or
`AssemblyContextIntegrationOpportunitiesQuery` ([#3932]): their release is
terminal for the released participant, so a later whole-group query over the
same group would find that participant unavailable. Bounded retained bytes come
from scope eviction instead, which disposes a group rather than half-emptying
it. The banned-symbol list makes that a compile error rather than a comment.

A workspace may also span several package coordinates on purpose:
`MemberCallGraphSession` can only see callers in a sibling package when that
package is a participant of the *same* binding-consistent group, so the call
graph opens one workspace over every package the site currently has open.
Coordinates are temporarily leased while that composite scope is assembled, so
acquiring a later package cannot evict an earlier archive and leave it alive
outside cache accounting.
Call-graph targets carry a display spelling, the exact escaped structured type
identity (`typeDefinitionId`), and the flattened metadata spelling
(`typeMetadataId`); navigation uses an identity rather than the display name, so
nested and generic type names retain `+` and arity. The flattened spelling cannot
tell a nested `Outer+Inner` from a type whose own metadata name contains a
literal `+`, so the product publishes it only where it names exactly one type and
withholds it otherwise — `CallGraphMemberResolver.UnambiguousMetadataIdentity`
owns that decision, and `DefinitionIdentity` is the injective identity the same
resolver matches on the other side.
`CallGraphTargets_DistinguishNestedFromLiteralPlusDeclaringTypes` gates the
distinction at the browser boundary. A graph click on a non-public member of a
public type lazily projects that exact member through the same product resolver
and opens the ordinary member page; the shared URL retains the opaque target so
refresh does not fall back to a source modal. `graph-only members open through
the typed member surface` and `graph-only member targets round-trip through
shared URLs` gate that path. Projected non-public rows remain separately labeled
as graph-discovered implementation members rather than entering the Public API
count. Constructed generic nodes recover assembly identity from their
definition. Synthetic array and function-pointer nodes remain visible but carry
no navigable definition identity. Accessor nodes resolve through their opaque
body selector even when the graph has no `MethodDef` token. That exact body
enables Call graph, Annotated source, and Facts; whole-member Source remains
hidden because its product query intentionally rejects accessor bodies.
The call-graph legend explains the independent border vocabulary: solid nodes
receive no platform lookup, while dashed nodes are unresolved external
assemblies that receive a .NET platform lookup on click.

[#3932]: https://github.com/richlander/dotnet-inspect/pull/3932

## Engine layout

| File | Owns |
| --- | --- |
| `engine/Program.cs` | the entry point, and nothing else |
| `engine/BannedSymbols.txt` | the compiler-enforced workspace rule |
| `engine/BrowserContracts.cs` | the transport records and their source-generated JSON context |
| `engine/BrowserWorkspaceShareOperations.cs` | typed Browser adaptation over the product-owned workspace packet codec and transposer |
| `engine/BrowserPackageWorkspace.cs` | the Browser adapter over shared package acquisition, the session cache/capacity policy, and the bounded workspace registry |
| `engine/BrowserPlatformWorkspace.cs` | content-backed platform acquisition, exact family pins, cumulative group replacement, and shared package/workspace accounting |
| `engine/BrowserApiSurfacePolicy.cs` | the explicit participant/type/member bounds every API-surface projection runs under |
| `engine/BrowserInspectionScope.cs` | Browser coordinate/asset provenance over product-realized surface/implementation roles, the `InspectionWorkspace` lifetime, and query hand-offs |
| `engine/BrowserSurfaceProjection.cs` | adapting typed query models into transport records |
| `engine/BrowserStyleOptions.cs` | resolving the client's style ids through `StyleOptionCatalog` |
| `engine/BrowserXmlDocumentation.cs` | reading one member's package-shipped XML documentation |
| `engine/InspectionEngine.cs` | the supported `[JSExport]` operations |
| `engine/BrowserPlatformOperations.cs` | the supported Platform acquisition, Integrations, Opportunities, and call-graph exports |
| `engine/BrowserSourceOperations.cs` | pathless PDB-mapped-or-decompiled type/member source and Browser source capabilities |
| `engine/BrowserUnsupportedOperations.cs` | the `[JSExport]` operations this engine refuses |

Inspected assemblies are read with System.Reflection.Metadata only, are never
written to a file, and are never loaded into the runtime. Browser/Wasm is
single-threaded, and both caches are written for that host: at most 12 packages
or 128 MB of package content in aggregate, including nupkg arrays retained by
open scopes, and at most four open workspaces. Evicting a package first disposes
every scope that retains it, so cache eviction actually releases the archive
bytes instead of removing only the cache's reference. The client retains at
most 12 package models as well, and rejects a shared workspace with more than
12 tuples or 65,536 encoded characters before it starts package acquisition.
The JavaScript `shared workspaces are bounded before package loading` and
`workspace package models retain the active and newest coordinates within the
limit` cases gate those client boundaries. A nupkg response must
declare its content length. The cache reserves that length and evicts enough
unleased content before allocating the response array; reservations participate
in the same 12-package/128 MB aggregate while the download is in flight.

A coordinate is validated before it can key the cache or reach the network.
`PackageCoordinateResolver` owns the same bounded ASCII package-id grammar and
canonical exact-version grammar used by workspace contexts. Floating versions
use its listing-aware shared policy, with persistent candidate caching disabled
for the filesystem-free host. `PackageResourceUrl` composes every feed-declared
flat-container resource without losing a signed base query or allowing a
coordinate to rewrite the resource path.
`PackageCoordinateResolverTests.Coordinate_RejectsAPackageIdOutsideTheGrammar`,
`ListVersions_UsesAuthorizedSourcesWithoutPersistentCaching`, and
`BrowserEngineBoundaryTests.PackageCoordinates_AreRejectedBeforeAnyCacheOrNetworkAccess`,
`BrowserEngineBoundaryTests.PackageResolution_StallBecomesVisibleOperationTimeout`,
`BrowserEngineBoundaryTests.PackageAcquisition_StallBecomesVisibleOperationTimeout`,
`BrowserEngineBoundaryTests.PackageAcquisition_SharedStallIsAVisibleTimeoutForEveryCaller`,
`BrowserEngineBoundaryTests.PackageAcquisition_ExpiredDeadlineCannotPublishReservedContent`,
`BrowserEngineBoundaryTests.PackageOperation_LateFailureBecomesVisibleTimeout`,
and
`BrowserEngineBoundaryTests.PackageOperation_LateCallerCancellationRemainsCancellation`
gate these boundaries.

Acquisition is bounded before content enters either cache or workspace. A
version-index response uses the shared 16 MB text-response limit, one downloaded
nupkg may contain at most 128 MB, the aggregate declared archive expansion is
limited to 512 MB, one expanded assembly entry to 64 MB, and one expanded
Markdown or XML-documentation entry to 16 MB. A package manifest is separately
limited to 1 MB of input and 512K decoded XML characters by
`ManifestBounds_AreEnforcedForEveryPackageStore` and
`ExecuteAsync_EnforcesDecodedCharacterLimit`. A nupkg may contain at most 4,096
entries.
`PackageArchiveValidator` applies those Browser-supplied limits while retaining
the shared path, CRC, compression, directory, and expansion admission rules. It
scans the highest-offset end record and central directory without allocating
entry objects before `ZipArchive` can materialize them.
`InMemoryPackageContent` checks a ZIP entry's declared expanded length before
allocation and verifies the observed expansion against that declaration.
`PackageArchiveValidatorTests`, `InMemoryPackageContentTests`, and
`BrowserEngineBoundaryTests.PackageArchiveEntryFlood_IsRejectedBeforeArchiveEnumeration`
gate the shared rules and the Browser limit. XML documentation text is streamed
through the shared `CSharpText.XmlDocText` grammar and rejects nesting beyond
its product-owned depth limit. Package Markdown is rendered through a narrow
text-only element allow list with styling and resource-loading attributes
removed.
`XmlDocTextTests.GetNodeTextWithRefs_AcceptsTheDepthLimitAndRejectsTheNextElement`,
`BrowserEngineBoundaryTests.XmlDocumentation_AcceptsTheDepthLimitAndRejectsTheNextElement`, and
the JavaScript `package Markdown has no styling or resource-loading authority`
case gate those boundaries.

Each retained scope has an explicit compile role and implementation role. The
compile group uses the selector's reference-preferred assets for API and type
views; the implementation group uses matching `lib/` assets for bodies,
Integrations, call graphs, and whole-assembly performance analysis.
Opportunities use the compile group because they classify the package's
reference-preferred public surface. Packages without `ref/` assets share one
group for both roles. When both roles exist and differ, they split the scope's
64 MB retained image budget rather than doubling it. A reference-only package
has one group and uses the full budget; performance analysis falls back to that
surface group when no implementation participant exists.

## Supported

| Operation | Workspace | Query that owns the session |
| --- | --- | --- |
| `QueryPackage` | one package/version/framework | `AssemblyContextApiSurfaceQuery.ExecuteBounded(group, scope, limits, participants)` |
| `QueryTypeProjection` | one package/version/framework | `AssemblyContextTypeProjectionQuery.ExecuteParticipant(...)` |
| `QueryMemberAnnotatedSource` | one package/version/framework | `AssemblyContextMemberProjectionQuery.ExecuteParticipant(...)` |
| `QueryMemberSource`, `QueryTypeSource`, `QueryTypeMemberSource` | one package/version/framework | `AssemblyContextSourceQuery.ExecuteMemberAsync(...)` / `ExecuteTypeAsync(...)` |
| `QueryPackageDependencies` | one package/version/framework | `PackageDependencyGroupsQuery.ExecuteAsync(content, ...)` and `AssemblyContextReferencesQuery.ExecuteParticipant(...)` |
| `QueryPackageIntegrations` | one package/version/framework | `PackageWorkspaceIntegrationsQuery.Execute(realization)` |
| `QueryPackageOpportunities` | one package/version/framework | `AssemblyContextIntegrationOpportunitiesQuery.Execute(group, prerequisites)` |
| `QueryPackagePerformance` | one package/version/framework, implementation group | `AssemblyContextOptimizationOpportunitiesQuery.Execute(group)` |
| `QueryMemberCallGraph` | every open package coordinate, implementation group | `MemberCallGraphSession` |
| `LoadRuntimePack`, `LoadRuntimePackAssembly` | selected platform assemblies accumulated per target framework | `AssemblyContextApiSurfaceQuery.ExecuteBounded(group, scope, limits, participants)` |
| `QueryPlatformIntegrations` | one selected participant in the cumulative platform group | `AssemblyContextIntegrationsQuery.ExecuteParticipant(...)` |
| `QueryPlatformOpportunities` | one selected participant in the cumulative platform group | `AssemblyContextIntegrationOpportunitiesQuery.ExecuteParticipant(...)` |
| `ExpandPlatformCallGraph` | lazily acquired target in the cumulative runtime and ASP.NET Core platform group | `MemberCallGraphSession` |

`QueryPackage` is the site's default path. It runs against the product-selected
compile assets, so `ref/` assemblies remain authoritative when the package ships
them. It asks the API-surface query for the composed scope — the default consumer
surface plus non-public types — so a public type keeps its public member list
while non-public types remain reachable through the accessibility filter. Public
types hidden by the extractor stay hidden rather than re-entering the default
bucket with private members. That scope is one extraction inside
`ApiSurfaceExtractor`, not two composed in the query layer, so a package load
materializes each image's surface once.

A package load is not an explicit request for unbounded work, so it never
invokes the whole-group or participant entry points, which are declared
`InspectionCost.Unbounded`. `BannedSymbols.txt` makes that a compile error here.
`QueryPackage` calls `ExecuteBounded` with `BrowserApiSurfacePolicy.Limits` and
selects only the requested coordinate's participants, so another open package's
surface is never materialized to be discarded. If a projection reaches a bound it
stops at a whole participant — no type is returned with a shortened member list —
and the response's `inspectionError` names the bound, what was projected, and how
many assemblies were not. Types, members, retained metadata-row failures, type
forwarders, and total inspected metadata rows each have an explicit ceiling.
`BrowserEngineBoundaryTests.ApiSurfaceProjection_IsBoundedAndReportsTruncation`
gates the bound and its non-vacuity; the truncation contract itself is gated by
`AssemblyContextApiSurfaceQueryTests`. Every accessibility
bucket's id, label, order, default, and count comes from the query's own
`ApiAccessibilityBucket` values; the browser classifies nothing and orders no
label. Member identity is likewise product-owned: the stable selector, digest,
and canonical signature come from `ApiMemberIdentity`, and the call-graph
selector and accessor body selectors from `CallGraphMemberResolver`. A
participant the workspace could not project is named in `inspectionError` beside
the results rather than dropped. Inspection failures from an otherwise available
partial API surface are summarized there too, so healthy rows remain usable
without presenting an incomplete surface as complete. The site folds that field
into its query notice.

`QueryTypeProjection` and `QueryMemberAnnotatedSource` run over one group
participant. The Research queries own the `MetadataSource` and the whole-assembly
`LibraryBodyIndex` themselves, take no filesystem path, and resolve references
through the participant's own binding policy rather than by matching simple
names. Type projection stays on the compile participant. Annotated source moves
to its matching implementation participant and asks `CallGraphMemberResolver`
to validate the surface's `MethodDef` token or remap it by the opaque structural
selector when `ref/` and `lib/` row numbers differ. It then returns the product's portable
`AnnotatedSourceDocument` serialized by its owning
`AnnotatedSourceDocumentJsonContext` — the same artifact the CLI writes and the
[#3964] viewer validates — inside an envelope carrying provenance and, when the
whole-assembly fact context could not be built, a visible `contextLimitation` so
a short fact list is never read as an honest absence of facts. Printer options
are resolved from `StyleOptionCatalog`; an id the catalog does not know is a
visible failure, not a silently ignored selection.

The three source exports resolve the exact structured type identity and opaque
member body selector against the implementation participant before calling
`AssemblyContextSourceQuery`. The query tries checksum-verified PDB source
through Browser HTTP and explicit nuget.org authorization, then falls back to
pathless decompilation under the workspace binding policy. Symbol-package
responses are capped at 24 MiB, expanded PDBs at 8 MiB, and archives at 2,048
entries before either response or expanded content is copied into the
request-scoped store. Candidate PDB expansion across one symbol package is
capped at 24 MiB, checks cancellation between decompression chunks, and rejects
all ZIP64 sentinels in the end-of-central-directory record before `ZipArchive`
enumeration. Because that record does not carry the per-entry ZIP64 extra field
that supplies `ZipArchiveEntry.Length`, a negative declared PDB length — which
would clear every ceiling and then narrow to a large allocation — is rejected at
the allocation site as well. The store independently
caps all retained PDB bytes at 24 MiB. SourceLink requests are authorized before
dispatch for HTTPS URLs on GitHub, Azure DevOps, GitLab, and Bitbucket source
hosts, and the Browser transport refuses redirects; unsupported hosts visibly
fall back to decompilation.

MSDL's redirect omits CORS headers, so the Browser host rewrites only the exact
MSDL symbol-request shape to the current site's absolute `/api/msdl/...` URL.
Each Free Static Web App deploys the small anonymous managed Function in
`msdl-proxy`; the fixed upstream host and independent path-segment validator
keep it from becoming a caller-directed proxy. The function enforces the same
8 MiB portable-PDB ceiling as the Browser consumer.

Source operations are exclusive across the Browser process: a new request
cancels the previous request, and leaving every source view cancels hidden work.
The operation holds its workspace and package archives until its fresh bounded
PDB and source stores are released, so concurrent or evicted requests cannot
multiply those request-local budgets. This lifetime is gated by
`SourceOperations_AreExclusiveAndSuperseding` and
`ActiveScopeLease_PreventsWorkspaceAndPackageEviction`. Cancellation also
releases a caller waiting on shared package acquisition without canceling that
bounded cache operation for other consumers; `CancelledWait_ReleasesSharedPackageAcquisition`
gates that separation. Source lookup therefore adds no ambient filesystem
dependency or unbounded retained cache. Typed rejection and unavailable
outcomes become visible failures; only an `Available` result crosses the
bridge. Decompiled results disclose why the PDB-source attempt was unavailable.
`BrowserEngineBoundaryTests.DecompiledSources_CarryPdbAttemptLimitation` gates
that adapter wiring.
Reference-only type source is refused rather than presented as a body-free
decompilation. Printer options apply to decompiled fallback and never rewrite
PDB source. Whole-member source remains MethodDef-scoped: a
call-graph accessor body reports that limitation rather than returning its owner
property or the whole type as a success-shaped substitute, and bodiless API
groups do not offer a Source section.
`BrowserEngineBoundaryTests.SourceContexts_UseFreshMemoryOnlyPdbStores`,
`BrowserEngineBoundaryTests.SourceFetchPolicy_AuthorizesBeforeDispatch`,
`BrowserEngineBoundaryTests.TypeSourceParticipant_RefusesReferenceOnlyAssembly`,
`SnupkgPdbReaderTests.ExtractPortablePdb_EntryLimitRejectsArchiveBeforeExpansion`,
`SnupkgPdbReaderTests.ExtractPortablePdb_EveryZip64SentinelIsRejected`,
`SnupkgPdbReaderTests.ExtractPortablePdb_AggregateExpansionRejectsRepeatedCandidates`,
`SymbolPackageDownloaderTests.AcquirePdbAsync_LimitedHostRejectsOversizedSymbolPackage`,
`SymbolPackageDownloaderTests.AcquirePdbAsync_LimitedHostRejectsOversizedMsdlBeforeStore`,
`AssemblyContextSourceQueryTests.DecompilerFallback_AppliesRequestPrinterOptions`,
and the JavaScript `source requests carry exact type and member identities`,
`member request identity distinguishes colliding type queries`, `annotated
source request identity includes the selected body`, and `call graph source
identity prefers the structured type definition` cases gate these boundaries.

[#3964]: https://github.com/richlander/dotnet-inspect/pull/3964

Two exports read **package content** without inspecting an assembly, so they open
no group: `GetPackageDocument` (the package's own Markdown manifest, path-checked
against that manifest) and `QueryMemberDocumentation` (the XML file shipped
beside a product-selected compile asset).

Three exports touch **no artifact at all** and say so in place: `SearchTypes`
(ranking names the client already holds, through `TypeMatcher`),
`PackageCacheStats`, and `ListVocabulary` (the shared product-owned vocabulary
catalog).

`QueryPackageIntegrations` groups the query's own
`EcosystemIntegrationSignalInfo` values by the integration name the scanner
assigned. That is presentation grouping; no signal, category, or count is
composed here, and a participant the group could not acquire is reported beside
the results rather than dropped. Packages with a partial `ref/`/`lib/` pairing
scan implementation participants first, then scan each reference-only surface
participant through the non-terminal participant query. The reusable group
remains intact; `ExecuteParticipant_DoesNotReleaseTheReusableGroup` gates that
lifetime contract.

`QueryPackageOpportunities` asks the query registry for the typed Opportunities
query, which runs its declared Integrations prerequisite over the same retained
surface group. The product owns opportunity classification, existing-integration
suppression, and participant failures. The browser only deduplicates identical
rows and groups them by the returned integration name.

`QueryPackagePerformance` runs the product's group-scoped optimization query
over implementation participants, falling back to the surface group only for a
reference-only package. Analysis owns opportunity priority, semantic loop
classification, generated-framework suppression, member aggregation, and
deterministic order. The query owns body-index lifetime, binding-contained
sibling resolution, public API attribution, and typed failures. Lifted evidence
aggregates under its source owner. Exact metadata type identity and stable member
selectors bridge implementation evidence to the exact rendered
reference-preferred surface without treating MethodDef tokens as cross-image
identities. The browser removes rows absent from that surface, emits the surface
assembly identity, and applies its 200-member display bound afterward while
preserving product order. Extra implementation-only assemblies are omitted
rather than making the lens fail, and any bounded-surface notice remains visible
beside the Analysis result. A 201st navigable ranked member produces a visible
truncation notice instead of making the top 200 look complete. Accessor evidence
is aggregated under its owning property or event with every body token retained.
Rows open the supported member Overview. Non-public opportunities remain visible
in the aggregate count.

`QueryMemberFacts` resolves the selected reference-preferred member to its exact
implementation body through the product's opaque member correspondence, then
invokes `AssemblyContextMethodAnalysisQuery` for that participant and physical
MethodDef token. The query owns retained-image, metadata-context, and Analysis
index lifetime. The browser only formats signals, allocation and call
occurrences, unsafe evidence, exception regions, opportunities, and visible
diagnostics. Allocation occurrences retain the product's heap-counting
discriminator, and safety rows use the product's deduplicated semantic
projection. Call rows use qualified type spelling and retain constructed
generic method type arguments. Selected graph-only accessor bodies use their
body selector and token, and ref/lib MethodDef row numbers are validated rather
than treated as cross-image identities. Surface selections use structural
correspondence without offering their reference-image token as an implementation
fallback; only a graph-only member surface returned by the product authorizes
fallback, using the graph response's exact selected body name, selector, and
implementation MethodDef token rather than fields restored from a shared
target. Owning property and event surfaces retain that selected accessor
separately from navigation state. The selected body coordinates overlay rather
than replace the full graph target, preserving its assembly and type identity
for history restoration; `selected graph bodies preserve the full navigation
identity` gates that round trip.
`BrowserEngineBoundaryTests.MemberFacts_DistinguishesSurfaceAndBodyTokenResolution`
gates token and accessor provenance, heap classification, unsafe-operation
deduplication, and constructed generic call identity. The frontend retains at
most one in-flight Facts Analysis request per member signature and lets a
returning selection reattach to that work; `same member facts request does not
duplicate in-flight analysis` and `returning to in-flight member facts reuses
work and owns publication` gate that single-threaded Browser/Wasm protection.
`graph-only implementation bodies select, switch, and clear` gates the mutable
application projection that authorizes accessor fallback and removes that
authorization when the selected target no longer matches a product body.

`QueryPackageDependencies` asks the package-content query for every dependency
group in manifest order and an exact-framework selection outcome. A missing
exact group remains visible while the UI permits inspecting the groups that were
actually declared. The dependency list and graph both follow that explicit UI
selection for the active package; other open packages use their product-selected
groups. The selected compile participant's direct references come from the
assembly-context query; the browser neither parses the nuspec nor opens an
assembly session.
For open-package navigation, JavaScript supplies the loaded coordinates and
their typed package-versus-platform provenance to
`PackageDependencyCoordinateMatchQuery`. The product returns `NoMatch`,
`Unique`, or `Ambiguous` using NuGet identity and range semantics; JavaScript
only activates the opaque key from a unique result. Product-query tests gate
the semantic outcomes, and
`dependency candidates carry typed package provenance to the product engine`
gates the Browser transport.

`QueryMemberCallGraph` projects `MemberCallGraphView` through
`ILInspector.CallGraph.CallGraphProjection` and renders Mermaid in the engine.
[`docs/design/call-graph-projection.md`](../../docs/design/call-graph-projection.md)
makes that split on purpose: the projection owns identity, direction, cycles,
and boundaries, and each front end spells them for itself. The Mermaid renderer
HTML-encodes delimiters and visibly encodes control, line-separator, Unicode
format, and unpaired-surrogate characters before artifact labels enter the
grammar. The engine's `CallGraphMermaid_ContainsArtifactLabels` and JavaScript's
`type graph rendering contains artifact labels` and
`dependency graph rendering contains artifact labels` gate the final renderers'
containment while preserving ordinary Unicode scalar text. Call-graph
navigation receives typed
targets for every projected node and uses the transport's normalized lowercase
node kind rather than inferring identity from SVG text.
Package participants never satisfy platform-scoped bindings. Incomplete node,
edge, and binding-identity diagnostics from `MemberCallGraphView` cross the
transport and remain visible beside a partial graph. A projected target is
navigable only when exactly one loaded package coordinate matches it; portable
graph focus remains deferred to [#4054].
`WorkspaceBinding_RejectsPackageParticipantsForPlatformScope`,
`CallGraphDiagnostics_PreserveIncompleteProductEvidence`, and the JavaScript
ambiguity and diagnostic cases gate these host behaviors.

[#4054]: https://github.com/richlander/dotnet-inspect/issues/4054

## Unsupported

Each remaining gap is a missing public query that owns its own group session.
Each export keeps the signature the browser bridge binds and throws a
`NotSupportedException` naming the gap, so the site reports the engine's refusal
rather than fixture results or success-shaped empty output.

| Unsupported export | Missing product query |
| --- | --- |
| `QueryPlatformPerformance` | assembly-wide Analysis ranking over a platform group |

Package and Platform Metadata use
`AssemblyContextMetadataImageQuery`, `AssemblyContextMetadataTableQuery`, and
`AssemblyContextMetadataHeapQuery`. The host selects a workspace participant;
the product query owns session access and returns typed availability, rejection,
or failure. Table windows and heap listings retain their bounds, coverage, and
truncation instead of presenting partial data as complete.

Package-backed type Metadata/Source and member Source/Annotated Source exports
do not accept platform coordinates. The Platform UI therefore withholds those
type lenses and member sections rather than routing `Microsoft.NETCore.App`
through NuGet package acquisition. Platform call graphs remain available;
method Facts remains package-backed.

`ResolvedAssemblyReference.CreateFromStreamIfManaged` owns pathless identity
decoding, so Browser acquisition does not reconstruct assembly identity.

Platform workspace acquisition and the supported adapters are tracked by
[#4401].

Each gap has a tracking issue; the pull request that introduced this rebuild
lists them.

[#4401]: https://github.com/richlander/dotnet-inspect/issues/4401

## Annotated source

`src/annotated-source-view.ts` and its tests are the browser half of the [#3964]
portable `AnnotatedSourceDocument` contract, and `QueryMemberAnnotatedSource` now
feeds it a real document.

[Annotated Source viewer interaction](../../docs/design/annotated-source-viewer-interaction.md)
owns disclosure, actions, selection, annotations, media, Escape, and focus
inside the embedded reader and modal viewer. The shared
[Inspect Web Shell Interaction](../../docs/design/inspect-web-shell-interaction.md)
design continues to own modal composition, while
[Inspect Web Navigation Consumer](../../docs/design/inspect-web-navigation-consumer.md)
owns browser-history behavior and destination focus.

The viewer reuses the owner's module rather than copying it.
`prototypes/annotated-source-viewer/src/document-model.js` owns validation,
UTF-16 coordinates, line derivation, segmentation, and the fact → target → node →
span walk. `src/document-model.ts` provides typed aliases over that owner for
Vite and the tests; Vite bundles the shared implementation into the deployable
browser artifact without copying its logic.

The inline working surface shows complete product-issued, C#-highlighted source
with the catalog's default Finding annotations and Finding detail. The
page-owned contextual bar supplies source-only **Copy** and **Explore**, while
compact product provenance follows the source instead of introducing a second
reader header. Each **Explore** activation creates a fresh full-bleed modal
session with C# visible, IL and UTF-16 ranges hidden, default annotations
active, and no transferred detail. The modal adds catalog-driven annotation
and medium controls, product-issued structure, deterministic
invocation-preferred source hit testing, node selection, and one persistent
inspector action for every Finding, including unanchored Findings. Annotation
rows preserve the product-issued source prefix as layout geometry, so each
CodeLens-like row appears immediately
before its target line and begins at the anchored span without flattening the
language's visible indentation. Dismissal destroys modal-local presentation
and annotation state while retaining only an eligible embedded Finding primary.

`src/annotated-source-session.ts` owns the viewer-local state transitions;
`src/annotated-source.ts` owns markup, browser hit testing, native drag
selection protection, detail, and focus-target identities; and
`dotnet-inspect.ts` composes the modal with shell history, inert background,
focus restoration, and layered Escape. A payload the portable model rejects
remains a visible failure rather than rendering success-shaped empty output.

## Run

The frontend requires Node.js 24 or later; `npm ci` enforces that requirement.
Install the experimental browser workload selected by the repository SDK:

```bash
dotnet workload install wasm-experimental
cd prototypes/inspect-web
npm ci
npm run build
cd engine
dotnet run -c Release
```

Open `http://127.0.0.1:5198`. Create a deployable static bundle with
`npm run build && dotnet publish -c Release` from the same directories shown
above. The TypeScript check is part of both `npm run build` and `npm test`.
Remote addresses require HTTPS because the .NET loader uses secure-context
browser APIs. For private cross-machine demos, follow
[`docs/runbooks/inspect-web-demo-hosting.md`](../../docs/runbooks/inspect-web-demo-hosting.md);
the preferred SSH-forwarding pattern preserves a browser-loopback URL without
exposing an application port.

On a bare visit, `dotnet-inspect.ts` waits for the home page's first contentful
paint before dynamically importing the seven production facade modules. Search
and demo controls remain inert behind a loading indicator until every facade is
ready; package and shared-workspace deep links retain the full loading
interstitial. The `bare home paints before wasm engine download` JavaScript test
gates this startup boundary.

`eng/generate-inspect-web-engine-facade.sh` executes the engine's compiled
`JsExportRoot` recipe once to generate the seven canonical context artifacts.
This execution path is `ts-jsexport` context mode: the attributes declare the
closed assembly set and `--context` names that declaration for the generator.
It is one mechanism, not a TypeScript feature, and the generated modules
contain no context construct. The script requires the context output to equal
the exact seven-entry consumer map, proves each context artifact equals direct
generation for its rooted assembly, and copies those bytes unchanged into
`engine/facades/`.

Those native TypeScript files are the authoritative checked-in handoff. The
script compiles all seven in one exact program against the SDK-owned
`dotnet.d.ts` from the Browser/Wasm runtime pack selected for the engine build,
with LF compiler output on every host. The derived declarations live in
`src/facades/` and the published modules in `engine/wwwroot/`; `--check`
compares all 21 artifacts and rejects extra or missing files. The SDK
declaration is a compile-time input copied only into a temporary workspace and
is never published.

`src/engine-facades.ts` owns runtime composition. Concurrent callers share one
retained readiness promise. It calls the host module's `createRuntime()` once,
then passes that same narrow runtime handle while the seven generated modules
initialize serially. Only the host facade configures browser policy and runs
the entry point; application calls bind directly to their owning generated
module rather than a compatibility monolith. After publish,
`verify-published-engine-facades.ts` runs the real Browser/Wasm artifact through
the same worker-safe path, observes the SDK call without memoizing it, proves
one creation, one runtime, and one entry point, invokes every facade through its
own assembly, and exercises build identity plus `asyncLoweringCanary()`, a
genuinely awaited operation with a fixed typed result and no network,
package-cache, server-API, or user-data dependency.

The purpose-built `multi-facade-canary` proves that this lifecycle composes
across independently generated modules. Its Alpha and Beta assemblies
deliberately use the same namespace, declaring-type names, method names,
overload shapes, record name, and enum name. Each checked-in facade is generated
from only its own assembly and acquires only that assembly's export root. A
consumer-owned single-flight coordinator serializes first initialization:
Alpha initializes before Beta, while concurrent readiness callers share that
one sequence. The coordinator creates one narrow runtime handle and passes it
to both generated modules; it does not rely on repeated SDK creation being
idempotent.

`eng/generate-inspect-web-multi-facade-canary.sh --check` gates independent
generation and drift for both facades. The
`eng/test-inspect-web-multi-facade-canary.sh` Browser/Wasm gate publishes both
assemblies into one runtime under both Mono and CoreCLR, requests readiness
concurrently, and then invokes both facades. It requires exactly one SDK
creation and one live runtime, assembly-distinct results through both declaring
types and exact overload keys, a genuinely awaited operation from each
assembly, independent record and enum declarations, and the managed operation
bridge's authenticated nonterminal callback lifecycle. The bridge case carries
Progress, Item, and ItemFailure events in producer order before the terminal
result, rejects callback failure, and prevents later invocation through a
retained sink. Its negative cases prove that the gate fails for a dropped
managed event callback, wrong assembly root, separately loaded runtime module,
both operational paths routed through one facade, uninitialized second facade,
or dropped managed invocation. This canary does not split the production engine
binding or expose raw `ILInspector` APIs; that production partition remains
[#4497].

[#4497]: https://github.com/richlander/dotnet-inspect/issues/4497

The home page identifies the browser stack below its search surface and links
to the client-rendered `/credits` route. `src/credits-panel.ts` owns that page's
markup, route recognition, and rendered control bindings. Azure Static Web Apps
rewrites direct Credits requests to the non-cacheable `index.html`; the
document's root base keeps SDK-generated framework imports valid on both
`/credits` and `/credits/`. `scripts/verify-site-artifact.ts` gates that base
and its ordering in the Vite bundle and SDK-published site. Other application
routes use the navigation fallback, while API, asset, and framework requests
remain excluded.

Search also exposes a `Package query` action that opens the routed `/query`
surface. It runs the product-issued nuspec-only facet catalog against
nuget.org, streams rows and visible partial failures from Browser Wasm, and
hands an exact result coordinate to the normal Workspace package-opening path.
The route keeps request and result state in the current session rather than in
the URL; a direct load starts with an empty prefix.

The .NET 11 preview Emscripten wrapper currently mishandles an SDK packs path
that contains whitespace. If that applies to the local SDK installation, pass
`EmscriptenSdkToolsPath` pointing to a no-whitespace link to the installed
Emscripten `tools` directory.

## Static analysis

Run both frontend analysis gates locally with:

```bash
cd prototypes/inspect-web
npm run typecheck
npm run analyze
```

The TypeScript gate checks product and test projects with `strict`,
`exactOptionalPropertyTypes`, and `noImplicitReturns`. `npm run analyze` then
runs Oxlint with its tsgolint backend against the same TypeScript 7.0.2
toolchain used to build the product. Correctness and suspicious diagnostics,
type-aware promise and unsafe-operation checks, warnings, and unused
suppression directives all fail the gate. Browser/background promises must
either preserve sequencing or surface unexpected rejection visibly. The exact
`node:test` `test` call is the only configured safe promise-returning call
because the test runner owns and observes that returned promise.

Closed workspace scopes, type and package lenses, member sections, and
Spotlight scopes are literal unions derived from their UI catalogs. DOM and URL
tokens are decoded before they reach typed state or actions; the scope-bar and
workspace-navigation tests gate rejection of unknown values.

Oxlint checks all seven compiler-derived production facade artifact triples and
both multi-facade canary source modules as consumer contracts. The
`src/facades/*.d.ts` declarations receive the TypeScript rules, while the exact
seven `engine/wwwroot/inspect-web-*.js` modules receive the JavaScript
correctness and suspicious rules described below. The checked-in production and
canary TypeScript facades are compiled separately against the exact SDK-owned
`dotnet.d.ts`; the canary gate compiles its authored coordinator and exercise
modules in that same program. TypeScript compilation and the generated facade
drift gates provide independent source and declaration coverage. The toolchain
test pins every separately compiled and derived lint input so a generator
change cannot silently leave analysis coverage. The configuration disables
four non-correctness rules: underscore spelling, function relocation, listener
API preference, and `Array.prototype.sort`. Those rules prescribe
naming/layout churn or, for sorting, the ES2023 `toSorted` API while this
project targets ES2022. Those four, plus the generated-facade overrides, are
the *complete* set of disabled rules. The compiler-derived JavaScript disables
the five unsafe-operation rules and the catch-callback annotation rule that
JavaScript cannot satisfy. The authoritative generated TypeScript facades
disable those unsafe-operation rules, unsafe type-assertion analysis for
authenticated JSON envelopes, and the redundant constituent diagnostic that
lacks the temporary SDK declaration used by their separate compiler gates. A
toolchain test pins these against Oxlint's resolved configuration, so another
disable — written at the top level or inside an `overrides` entry — fails
rather than passing quietly. Turning a rule off is not the only way to lose it,
so options, plugin settings and the global environment are pinned beside the
severities; those are described below.

Existing JavaScript tests and verification scripts remain covered by Oxlint's
correctness and suspicious rules, but not by its unsafe-operation type rules:
adding a shadow type model or migrating those files to TypeScript is outside
this analysis change. Their dependency and reachability graph remains covered
by Knip.

Oxlint's `plugins` list enables the `import`, `jsdoc`, and `promise` plugins on
top of the ones it turns on by default. That key *replaces* the defaults rather
than adding to them, so `typescript`, `unicorn`, and `oxc` are re-declared
explicitly; dropping one would retire a whole family of rules with every
command green. The core `eslint` rules are not affected by that key — they stay
enabled whether or not the list names them — so the list does not mention them.
Each added plugin was measured against this project at the same correctness and
suspicious categories before being enabled, and the toolchain tests read
Oxlint's own `--print-config` output rather than the config file. That output is
what Oxlint resolved, so a plugin that enables nothing at these categories, a
narrowed category set, a named rule switched off, or an `overrides` entry that
replaces the plugin list for product sources all fail there instead of reading
as an adoption. Overrides need reading separately because Oxlint keeps them as
their own array rather than folding them into the top-level rules.
That resolved read pins rule *options* as one set alongside the severities: a
rule left enabled but given options that exempt the code it was enabled for
reports nothing while every severity reads exactly as before. The `node:test`
`test` call is the only option-borne exemption in the project, so any second one
fails there. Plugin *settings* are pinned the same way and for the same reason,
except that they reach a whole family at once — `settings.jsdoc.ignorePrivate`
exempts every `@private` symbol from every jsdoc rule without touching a
severity. The settings assertion is differential: it compares this project's
resolved settings against Oxlint's resolution of an empty config, so the claim
is literally "this project changes no setting" and an Oxlint release that adds a
plugin's settings block does not churn it.

Severities, options and settings describe what the rules do; the *environment*
decides what they can see, and a rule that sees nothing reports nothing.
`eslint/no-global-assign` fires only on a name the configuration calls a
read-only global, so it can be silenced two ways without touching a severity:
re-declare the name as writable through `globals`, or drop the `env` that
supplied it — deleting `browser` silences an assignment to `document` exactly
as `globals: { document: "writable" }` does. Both keys are pinned, at the top
level and inside every `overrides` entry, as one map: this project declares no
`globals` at all, and its environments are `browser` plus `es2022` at the top
level and Node for scripts, tests and the Vite config.

`promise/always-return` is enabled and pinned by name: the two side-effect
continuations handed to `observeAsync` return explicitly, because their promises
are consumed rather than terminal and so fall outside the upstream
`ignoreLastCallback` option.

Enabling a rule is not the same as running it at its use sites, because an
inline `oxlint-disable` directive switches it off exactly where it would have
reported, and a *used* directive is invisible to `reportUnusedDisableDirectives`
as well as to every severity, category, option and override read above. So the
directive scan pins the set of rules this project suppresses inline at all —
today `typescript/no-unsafe-type-assertion` and
`typescript/no-unnecessary-type-parameters`. Pinning rules rather than sites
means an additional type-assertion suppression in a test does not churn the
list, while suppressing a newly adopted rule fails. A directive naming no rule
switches off everything and is rejected outright.

`npm run lint` also runs [html-validate](https://html-validate.org) over
`**/*.{html,htm,xhtml}` with `--config .htmlvalidate.json`. Documents are
otherwise invisible to every other gate here: the compiler builds a program out
of `.ts` files and Oxlint is handed a list of source paths, so nothing read
`index.html` at all before this. The committed `.htmlvalidate.json` extends the
`standard`, `document`, and `a11y` presets, which bring validity, element
conformance, document structure, and WCAG rules. It sets `root: true` so
configuration outside the project cannot merge into it, and makes one option
change: `require-sri` uses `target: "crossorigin"`, so third-party bytes must
carry a digest while same-origin files Vite emits are not asked for one.
`.htmlvalidateignore` names only generated output — `/dist`, `node_modules`,
`bin` and `obj`. Only `dist` is anchored: it is generated at the project root
only, so an unanchored entry would also exclude an authored `src/dist`. The
other three match at any depth, which makes the ignore file deliberately
*broader* than the project inventory's own pruning — the inventory exempts
anything under `public/`, `src/`, `test/` and `scripts/` outright, and prunes
`bin` and `obj` only beside a `.csproj`. The two are not equivalent and are not
meant to be. What makes the comparison below valid is containment in the safe
direction: every directory the inventory prunes is also ignored, so no owned
document is measured against a file the linter refused to open. Where the two
do diverge — an authored `src/bin/probe.html`, say — the set comparison fails
loudly rather than passing quietly. The `bin` and `obj` entries matter only once
the engine project has been built, which is why they went unnoticed locally and
surfaced on CI: without them html-validate was linting `engine/bin/**` and
`engine/obj/**` — MSBuild static-web-asset placeholders and copied `wwwroot`
output that no one authored and no one can fix.

Eight toolchain tests hold that wiring honest. They pin the preset list, the
`root: true` setting, and the *whole* `rules` object — `require-sri` is the only
entry, so a second rule relaxed beside it fails rather than slipping past an
assertion aimed at one key. They also pin the file's whole *key set*, because
rules are not the only way the presets get weaker: an `elements` entry changes
the HTML metadata the stock rules check against, so a rule can stay on and
simply have nothing left to say about an element. They require the lint glob to
reach a document of each covered extension, both nested and under `src/dist`;
require the committed configuration to reject a specimen *by the name of the
rule that must reject it* (`close-order`, `element-required-attributes`,
`wcag/h37`, `require-sri`, `attribute-allowed-values`); and require every
document the project owns to sit outside the ignore file.

The rest close the gap between "the linter ran" and "the linter saw this file".
One states the property directly, in two passes with different jobs. Both run
html-validate with `--dump-source` under the same `--config` the lint uses and
read the `Source` header printed ahead of each processed document.

The per-document pass is the authoritative answer to "was this document
examined". It hands html-validate each owned path *on its own* and requires the
first header back to name that same document. Asking one file at a time is what
makes the answer trustworthy, because `--dump-source` prints a document's full
text after its header: in a combined stream a header spelled inside a body is
indistinguishable from a real one, so a document excluded by an ignore file can
be vouched for by markup that *was* read. Handed a single path, the only text
that can reach stdout is that file's own, and an excluded path prints `No files
matching patterns` and nothing else. Comparing the header's path rather than
merely observing that one exists closes a further channel — html-validate
expands its path arguments as globs, so a document named with glob
metacharacters resolves onto a different file, and identity catches that without
enumerating which characters are dangerous.

The whole-glob pass runs over the lint glob and compares the header set to the
set the project inventory reports. Forgery cannot hide anything from it: writing
a header into a body only *adds* a name, and a name that is not owned fails as
an extra. So it is the reliable direction for "the linter read something this
project does not own" — a stray document under a generated directory, say —
while the per-document pass owns the direction a body could otherwise lie about.
Between them a document the linter never saw fails whatever the cause, and the
`--formatter=json` output cannot substitute for either, because it lists only
files that had problems.

Three more name specific causes, which makes a failure diagnosable rather than
merely true. html-validate resolves configuration and exclusions per directory:
a descendant `.htmlvalidate.json` replaces the committed rules for its own
subtree and a descendant `.htmlvalidateignore` drops documents outright, so a
walk requires the tree to hold exactly one of each, at the root. `**` does not
descend into dotted directories, so no authored document may sit under one. And
document extensions must be lowercase, because Node matches glob patterns
case-insensitively on macOS and Windows and case-sensitively everywhere else — a
`probe.HTML` would be linted on a developer's Mac and skipped on the Ubuntu
runners that gate merges and deploy the site.

Those three walks descend from the project root, so none of them can see an
ancestor. html-validate looks for `.htmlvalidateignore` by walking *upward* from
each document, and `root: true` stops configuration merging without stopping
ignore discovery, so a file one directory above this project can exclude an
authored document. That is precisely the case the `--dump-source` passes catch
and a walk structurally cannot, which is why the property is asserted directly
rather than by enumerating one more placement.

The `<link rel="preload" id="webassembly">` element in `index.html` carries a
scoped `html-validate-disable-next` directive for `element-required-attributes`.
It is a genuinely incomplete element on purpose: the .NET Wasm publish step
rewrites it to inject the runtime `href`, and three workflows plus
`PromotionWorkflowContract.cs` pin it by id. The directive names that one rule
on that one element.

That directive is the whole suppression budget, and the last toolchain test pins
it as such. A directive is written in the document rather than in a config file,
so none of the reads above can see one, and `no-unused-disable` cannot help when
the suppression is genuinely used: widening this one from `disable-next` to a
file-wide `disable` silences the rule for every element below it, and a second
directive next to a fresh violation is equally invisible. So the test
inventories every directive in every authored document and pins the set,
including the action — a different rule, a second entry, or a wider action all
fail.

CSS is not linted. Adopting Stylelint is tracked separately.

### Protections the linters cannot provide

Everything above reads source text at build time. Two properties matter here that
no amount of reading source text can establish.

The first is what a browser is allowed to do with the page once it ships.

`staticwebapp.config.json` sets four response headers globally —
`X-Content-Type-Options: nosniff`, which stops content-type guessing on the JSON,
TSV and wasm this site serves, plus `Referrer-Policy`, `X-Frame-Options` and
`Strict-Transport-Security`. Azure Static Web Apps returns the union of
`globalHeaders` and a matching route's `headers`, with the route winning per key,
so a route that names one of these keys replaces the global value for its own
paths and the file still reads as though the protection is global. A toolchain
test pins the header set and requires the route keys to stay disjoint from it,
compared case-insensitively because HTTP header names are — a route spelling
`x-frame-options` overrides a global `X-Frame-Options` on the wire. It also
rejects any route using `redirect`: Azure omits `globalHeaders` entirely on
redirect responses ([Azure/static-web-apps#739][swa-739], open since 2022), so
such a route names none of the four keys and still answers without them.
Together those two properties are what make "every static response" true rather
than merely intended. CI re-checks the headers in the published artifact,
because the site is deployed from that copy rather than from the source file.

[swa-739]: https://github.com/Azure/static-web-apps/issues/739

The word "static" there is a real boundary, not hedging. Azure Static Web Apps
does not apply `globalHeaders` to responses produced by the managed functions
under `/api/*`, which carry whatever headers the function sets for itself. The
MSDL proxy is such a function, so these four headers do not cover its responses.
Giving the proxy its own headers is tracked in #5119.

The second property is whether the third-party digests in `index.html` still
describe what the CDN serves. `require-sri` enforces that a cross-origin
subresource *carries* a digest, and that is the whole of what a linter can see;
whether the digest is still current lives on the network and changes without any
commit here. `scripts/check-sri-freshness.ts` re-fetches each pinned URL and
compares hashes, reading the pins out of the document so they cannot drift from
what the site actually loads.

Be precise about what that buys, because it is not a security control. SRI is
the security control and the browser enforces it: a stale pin means the browser
*refuses* the bytes, so nothing unexpected runs. What goes wrong is that the
subresource silently disappears — on this site, syntax highlighting stops
working — with nothing to say why. This is a maintenance signal, and it is
scheduled weekly rather than gating pull requests, because reaching jsDelivr is
required and an outage there is not a defect in somebody's change.

It reads the document with html-validate's own parser — the same parser that
lints the file — rather than with a pattern, so the two cannot disagree about
what the markup contains. It resolves URLs the way a browser does and follows
the SRI metadata grammar, so valid markup does not produce false drift. It also
separates the two ways it can fail: a stale pin is fixed by re-pinning, an
unreachable CDN is not a pin problem at all, and filing the second under the
first sends somebody looking for drift that is not there.

The check that matters most is the cheapest one. Finding *no* pinned
subresources exits as inconclusive rather than as success, because this script
exists to check them and finding none means the markup shape changed underneath
it. Without that, every later refactor of `index.html` would quietly turn the
weekly run into a green light for nothing.

Both of those checks read markup, and that is also their limit. `require-sri`
and the freshness check each look at `<script>` and `<link>` elements, so a
library loaded by `import("https://cdn.example/lib.js")` is invisible to both --
a dynamic import is not markup. Three runtime libraries used to load exactly
that way: mermaid, marked, and DOMPurify. They carried no digest, and both
checks reported clean, because neither could see them. That is a worse failure
than a missing pin: the report says the CDN surface is fully covered while a
third of it is unexamined, and the unexamined third included the sanitizer.

The fix is not a third check that knows about dynamic imports. It is removing
the condition those checks were trying to describe. The three libraries are
ordinary npm dependencies, and Vite bundles them into same-origin chunks that
load on demand, so no CDN sits in the runtime path for them at all. Lazy
loading survives, and mermaid actually splits further: its per-diagram-type
chunks are only fetched for the diagram kinds a page renders.

Moving them into the lockfile is what makes them auditable. A version in a CDN
URL is checked against nothing; a version in `package-lock.json` is checked
against the advisory database. Auditing the three versions those CDN URLs
pinned reports two vulnerable packages carrying 24 advisories between them --
19 against that DOMPurify build, several of which defeat sanitization outright,
and 5 against that mermaid build. The code comment asserting that DOMPurify
makes package Markdown safe had been resting on that build. None of it was
hidden; nothing was in a position to look.

Auditing that lockfile is what makes the difference visible, and `npm audit` is
still the way to run it by hand. Three review rounds went into the one CI line
that used to run it, and each found the same shape of mistake: a flag, or the
absence of one, meaning something narrower than it appeared to. That analysis is
worth keeping even though the line is gone.

`--audit-level` reads as a severity filter over advisories. npm applies it to
*packages*, bucketing each by the highest severity affecting it. Of the 24
advisories above, 19 are moderate and 5 are low, and both packages bucket as
moderate -- so `--audit-level=high` returns success on all 24, including all 19
sanitizer bypasses.

Omitting the flag does not remove the filter. npm falls back to `low`
(`options.auditLevel || 'low'` in `npm-audit-report`), which still passes a
package whose advisories are all `info`. `info` is the only setting that fails
on any advisory, and it has to be asked for.

`--omit=dev` is absent for a different reason: it filters by where a package is
declared, not by whether its code reaches a browser. Vite is a devDependency and
its `__vite__mapDeps` helper is in the shipped bundle, so that split was never
the boundary it resembled.

That check no longer runs in CI. `npm audit` needs npm's advisories endpoint,
and it exits non-zero both when it finds an advisory and when it cannot reach
that endpoint, so the merge gate could not tell a vulnerable dependency from an
npm outage. On 2026-09-04 the endpoint returned 503s and timeouts for over two
hours and turned `ci-required` red on unrelated pull requests; a gate that
blocks merges on a third party's uptime is not measuring this repository.

What watches the lockfile now is Dependabot: the same 168 packages against the
same advisory database, with vulnerability alerts enabled and a weekly npm
update schedule for `/prototypes/inspect-web`. The honest difference is timing.
`npm audit` blocked the merge that introduced an advisory; Dependabot reports
one after it lands and opens a security update. That is weaker, and it is what
lets the sanitization comment name a gate that is monitoring rather than
enforcement. Run `npm audit --audit-level=info` locally to get the old answer on
demand.

What remains on a CDN is the three Prism scripts in `index.html`. Those are
markup, they carry digests, and the freshness check reads them -- so the
coverage claim and the actual surface now describe the same set.

A Content-Security-Policy is still outstanding, because the generated
`<script type="importmap">` needs a per-build hash before `script-src` can be
strict.

Knip checks authored source, every TypeScript and JavaScript test, and
build/verification scripts for unused files, exports, and dependencies.
`knip.json` excludes the exact seven generated
`engine/wwwroot/inspect-web-*.js` publish artifacts: they import
`./_framework/dotnet.js`, which exists only after Wasm publish. The exclusions
are specific to Knip reachability; Oxlint still checks every generated module.
It also ignores `type-fest`, which nothing here imports:
mermaid's shipped `.d.ts` files import it while declaring it only in mermaid's
own `devDependencies`, so a consumer has to supply it for `tsc` to resolve
mermaid's types. It is pinned to the range mermaid builds against.

The tsgolint semantic backend publishes native binaries for x64 and arm64 hosts
running macOS, Linux (glibc or musl), or Windows. Those are the supported
development-analysis hosts; `npm run lint` fails before launching the analyzer
on other operating-system or architecture combinations, including Linux
ppc64le and s390x. Oxlint itself supports additional hosts, but type-aware
analysis is the limiting capability. This approved development-tool exception
does not affect the browser/Wasm product runtime or artifact. The host-matrix
unit test derives the supported intersection from the locked Oxlint and
tsgolint package metadata, requires both Linux libc variants, and pins the npm
preflight wiring; `npm run analyze` on macOS arm64 and the Linux x64 CI host
gates the supported paths.

`noUncheckedIndexedAccess` is enabled in the shared configuration and therefore
applies to both product and test projects. Indexed and lookup values are checked
at their use sites, with malformed decoded coordinates ignored and missing
decoded assembly descriptors reported through the existing visible failure
paths. Close-negative tests cover those boundaries rather than relying on
non-null assertions.

The scope, lens, and member-section vocabularies are closed union types derived
from the catalogs that render them. `data.ts` and `spotlight.ts` own those
catalogs; narrower typed subsets such as the platform library picker may repeat
only the values that surface exposes, with assignment back to catalog-derived
state checked by TypeScript. Values arriving from `dataset` attributes, URL
query parameters, hashes, and share packets are admitted through `isTypeLens`,
`isPackageLens`, `isMemberSection`, `isWorkspaceScope`, and `availableScope`;
an unrecognized value is rejected at that boundary rather than cast into typed
state.

Because those catalogs also render the choices a user can pick, adding an entry
widens the union *and* immediately offers the new value. Every dispatch named by
the exhaustiveness gate therefore ends in `assertNever`, so adding a catalog
entry fails compilation until each such dispatch says what the new value does.
The gate derives its vocabulary roster from every exported type alias in
`data.ts` and `spotlight.ts` that queries a catalog; it is not tied to one
formatting or indexing shape.

Nothing at runtime can observe that property — an unhandled value would simply
take whichever branch the consumer fell through to — so the gate is the compiler, and
`widening a UI vocabulary catalog fails compilation until every consumer handles it`
in `test/vocabulary-exhaustiveness.test.ts` is that gate. It widens each catalog
in a throwaway copy of the real TypeScript source graph and asserts `tsc`
reports the expected `assertNever` location in every named dispatch, with no
unrelated diagnostic. Deleting any one exhaustive dispatch turns it red.
`packageLensBody` is exhaustive too: an unwired package lens used to render a
placeholder that was indistinguishable from an empty lens, but now fails
compilation until its behavior is explicit.

## Test

```bash
cd prototypes/inspect-web
npm ci
npm run analyze
npm run build
npm test
npx playwright install firefox
npm run test:browser
cd ../..
dotnet run --project prototypes/inspect-web/engine.Tests -c Release
```

`BrowserEngineBoundaryTests` gates the browser host's aggregate archive budget,
central-directory entry limit before archive enumeration, role preflight before identity decoding, malformed selected-participant
visibility, reference-only retained-image budget, duplicate XML parameter
handling, Mermaid label containment, and complete call-graph navigation targets.
The frontend tests gate the Annotated Source session/action matrix against the
shared sample document and keep Spotlight candidate/cache identity
coordinate-complete. The Playwright Firefox gate exercises real pointer
coordinates, invocation-preferred hit testing, keyboard activation, native
drag selection, focus trapping and restoration, layered Escape, pointer
**Close**, backdrop dismissal, and source-only copy.
`call graph diagnostics distinguish failures from expected bounds` gates that
catalog and body-analysis failures remain visible while an expected finite
traversal boundary does not become a global error.

The shared product paths are gated by:

- `AssemblyContextApiSurfaceQueryTests` in `src/DotnetInspector.Queries.Tests`
  gates the surface query: the public and composed scopes, the accessibility
  buckets' ordering, default, and counts, participant rejection in group order,
  snapshot reuse across runs, and preserved `ApiSurface` inspection failures.
- `AssemblyContextResearchProjectionQueryTests` in the same project gates the
  Research queries: pathless projection from workspace-owned content, the
  annotated document and its whole-assembly facts, exact `MethodDef` addressing,
  binding-policy resolution, and typed participant failure.
- `ContentShapedWorkspaceParticipantTests` gates content-shaped acquisition: a
  participant minted from in-memory content is acquired by the group, and one
  minted with a placeholder identity is rejected — which is why acquisition must
  decode identity first.
- `ContentShapedMemberProjectionTests` in `src/ILInspector.Research.Tests` gates
  the two product seams the Research queries stand on: projecting a member from a
  path-less, stream-backed assembly reference, and supplying the whole-assembly
  analysis context that path-keyed resolution cannot provide.

`BrowserEngineLayeringTests` in `engine.Tests` gates the layering rule described
above on every browser-engine CI run.

Pull requests that change the browser prototype, its shared annotated-source
viewer, product dependencies, or repository build inputs run the `inspect-web`
CI job. That job installs the locked Node dependencies, checks and bundles the
TypeScript/JavaScript frontend, rejects unused authored files, exports, and
dependencies, compiles the platform-index generator, publishes the Release Wasm
bundle, runs the browser-engine tests, and runs both frontend test suites.
The `eng/CiChangeDetection` gate, invoked through
`eng/test-ci-change-detection.cs`, gates the path classification, and
`ci-required` includes the job's result.

## Interaction model

Package tabs and the framework selector are workspace identity, not display
state: changing either resolves a different workspace. Lenses this engine does
not answer report the engine's failure rather than fixture results.

`src/workspace-navigation.ts` owns the in-memory view history, monotonic
navigation generation, URL routing and building, typed adaptation to the
generated workspace-state bridge, the browser-history port, and the single delegated click listener
that intercepts same-origin in-app anchor clicks
(`bindWorkspaceLinkNavigation`/`shouldInterceptLinkClick`) — a modified click,
`target`-scoped link, `download` link, or cross-origin href keeps native
browser behavior. Initial routing records the opaque `w=` value without decoding
it, which preserves the bare-home paint before WebAssembly while allowing shared
workspace resolution to run only after the engine is ready.
`BrowserWorkspaceShareOperations` then routes decode through
`WorkspaceSharePacketCodec` and `WorkspaceSharePacketTransposer`; encoding
reverses the same path. TypeScript neither understands compact packet fields nor
owns base64url. Packet-local tabs, independent navigation focus and selected
binding context, portable member anchors/signatures, section, and sorted library
scope cross the generated bridge as long-form records. The selected context,
not every open tab, bounds package Call Graph expansion. Browser-created Call
Graph state composes only package tabs with the active tab's framework and RID;
incompatible tabs remain separate contexts. Product-run Call Graph demos install
their exact executed package order as the selected context, and expanded queries
send that complete ordered context to the product engine.

Browser activation supports package tabs and one exact or floating `:Platform`
group tab without RIDs. Canonical activation is atomic: any unavailable tab,
library, type, member, applicable member section, or many-to-one or
coordinate-changing tab resolution leaves the original URL intact, retains the
prior workbench when one exists, and reports the failed restore rather than
activating a partial workspace.
Unsupported groups, multiple Platform tabs, runtime identifiers,
multiple selected libraries, unknown lenses or sections, package-root facets,
unresolved graph targets,
ambiguous overloads, graph-discovered members, accessor-specific bodies, and
members without a portable product identity fail visibly rather than producing
lossy state. These boundaries are gated by `canonical tabs must remain distinct
and ordered after resolution`, `missing Platform reacquisition retains only an
aligned canonical pin`, and `canonical restoration is atomic and history adopts
the active packet basis`. A present `w=` remains authoritative even when
decoding or Browser adaptation rejects it; the visible package label is never a
fallback workspace, and malformed courtesy paths cannot preempt packet handling.
Failed packet URLs survive automatic nested renders until the projected
workspace changes. Successful packet activation discards any prior graph-source
modal, and explicit version or framework changes discard a floating packet basis
before URL capture only after acquisition succeeds. A failed Platform switch
retains its resident workspace. A selected Call Graph context containing a
Platform participant fails visibly because the Browser transport realizes only
package participants. `an empty workspace parameter remains authoritative`,
`authoritative packets bypass malformed courtesy paths`, `failed URL retention
survives automatic renders until navigation changes`, `Browser Call Graph
contexts reject Platform participants`, `explicit coordinate changes discard a
floating canonical basis`, and `canonical commit clears a settled graph source
without rendering` gate those boundaries.
Malformed percent-encoding in an ordinary package or version courtesy path
produces a typed route failure rather than escaping `decodeURIComponent`.
Boot and navigation without a resident workspace render that failure in the
error shell without offering an ineffective retry, and explicit Home navigation
clears the route error and its failed-URL hold. A resident route failure owns
its notice and URL hold in one discriminated state record; projection changes
retire both before presentation, while ordinary query notices and Retry actions
remain independent. A valid route, explicit Home or Credits navigation, and
both dismiss surfaces share its cleanup path; Home dismissal also replaces the
failed history entry with `/`. Other dismissals and projection changes replace
the failed entry with a guaranteed package-root recovery URL; if history
replacement is blocked, the owned failure and notice remain visible. Canonical
workspace Retry actions restore their own failed URL before retrying rather
than adopting an ambient route. Retry proceeds without history mutation when
that URL is already current, but does not run when a required restoration is
blocked. Parsed restore paths and final-package closure likewise stop before
replacing an unrecovered route failure or releasing its workspace. In-app and
history navigation with a resident workspace retain it and report the failed
route as a notice. `last package close recovers a route before releasing the
workspace`, `failed URL state is retained and retired atomically`,
`workspace retry restores its owned URL before running`, `route failure
recovery owns malformed URL replacement`, `malformed courtesy package routes
become typed failures`, `valid courtesy package routes continue to decode
normally`, and `malformed package routes use the contained restore failure path`
gate those boundaries.
`canonical transitions cancel visible source work before snapshot` and
`canonical transitions settle annotated source before snapshot` specifically
gate source-request settlement. Filters and browse presentation stay
session-local. A package-root view drops stale `w=` state and uses the ordinary
Browser package route for address-bar synchronization and explicit Share until
product facet ids exist. Other transient,
non-projectable views retain the last valid canonical URL; explicit Share
reports the refusal.
`test/workspace-navigation.test.ts` gates that the route preflight cannot decode
the packet and that later resolution invokes exactly one decoder.
`dotnet-inspect.ts` remains the sole mutable
application-state owner: it supplies typed snapshots and explicit transition
callbacks, calls the one link-navigation binder instead of adding its own
click listener, and registers application-level gestures and context
predicates with the shared keybinding dispatcher.

`src/keybinding-registry.ts` is a dependency-free general component with no
inspect-web imports. A registration declares keys, exact modifiers by default,
priority, an optional event-path scope and context predicate, and a handler
whose Boolean result distinguishes "matched" from "handled". The registry
orders active matches by priority and nearest event-path scope, stops at the
first handled result, and centrally applies `preventDefault()`. A false result
falls through to the next candidate. Equal-precedence matches produce a
structured conflict callback while retaining deterministic registration order.
Event-scoped registrations live in a `WeakMap`; registration also returns an
explicit disposer.

`src/workbench-keybindings.ts` is the inspect-web adapter: it defines the
workspace, element, and modal priority policy and reports conflicts. Local
owners including Spotlight, type/member filters, package tabs, and graph
interactions register their gestures against the rendered element. The
composition root registers workspace and modal gestures, then attaches the
registry's only raw `keydown` listener to `document`. Alt+←/→ and Shift+←/→
drive `navBack()`/`navForward()`; element-scoped gestures arbitrate in the same
dispatcher instead of relying on bubbling order or `defaultPrevented`
cooperation. Shift+←/→ remain gated by the shared typing check so they never
steal native text selection inside an input or filter field; Shift+↑/↓ stay
unclaimed globally.

`test/keybinding-registry.test.ts` gates precedence, scoped arbitration,
handled fallthrough, exact modifiers, conflict reporting, disposal, and the
original stack-navigation collision. `test/spotlight-identity.test.js` gates
the single-listener wiring and the complete workbench priority order.
`test/workspace-navigation.test.ts` gates
history traversal, stale-entry removal, navigation cancellation, generated
codec delegation, canonical topology and identity adaptation, visible typed
failures, sandboxed history errors, and the link-interception rule.

`src/package-acquisition.ts` owns NuGet and runtime-pack engine invocation,
surface-to-workspace-model projection, serialized runtime-pack loading, and
stale-result checks at the publication boundary. `dotnet-inspect.ts` supplies
the engine and state ports and retains mutable loading/error state, package
activation, workspace restoration, notices, retries, and rendering.
The generated engine declarations expose JSON-wire values as readonly
snapshots. Package acquisition therefore creates explicit application-owned
package, type, member, and parameter models only for the paths the client
mutates: runtime package aggregation, graph-member retention, and documentation
hydration. Immutable assembly, accessibility, document, and exception values
remain shared where their identity is useful; their containing application
collections are copied before mutation. The
`generatedPackageSurfaceRejectsMutation` and
`generatedMemberSurfaceRejectsMutation` TypeScript canaries keep direct wire
mutation red, while `package projection copies only application-owned mutable
collections` and `documentation hydration mutates only the application
projection` gate the copy boundary and wire-object isolation.
`test/package-acquisition.test.ts` gates package projection, publication
ordering, runtime request serialization and merging, cancellation after queued
or in-flight work, request-local failure reporting, replacement-slot
preservation, retry after failure, and resident-pack reuse;
exact Platform pins additionally gate version-aware resident reuse and engine
invocation;
`test/spotlight-identity.test.js` gates provenance, failure adaptation, and
composition-root wiring.

`src/package-inspection.ts` owns the async request lifecycle for Dependencies,
Integrations, Opportunities, Analysis, and package-level Metadata, including
workspace dependency-cache population and stale-result suppression.
`dotnet-inspect.ts` supplies the typed state, engine, platform-routing,
diagnostics, and rendering ports while retaining lens selection and
presentation. `test/package-inspection.test.ts` gates complete cache identity,
resident-package checks, visible partial/failure results, package/platform
routing, stale publication, and explicit Platform library scope;
`test/spotlight-identity.test.js` gates the composition-root wiring.

`src/source-inspection.ts` owns the mutually exclusive member, type, and
call-graph source request lifecycle: shared cancellation, generation and
per-surface identity checks, loading/error/result transitions, graph-modal
open/close state, and focus-preserving completion. `dotnet-inspect.ts`
validates the active selection, builds typed engine requests, supplies mutable
state and rendering ports, and retains source presentation.
`test/source-inspection.test.ts` gates hidden cancellation, stale member
selection, visible failure, hidden type completion, graph close/cancellation,
and graph failure; `test/spotlight-identity.test.js` gates engine and
composition-root wiring.

`src/member-detail-inspection.ts` owns member XML-documentation, annotated
source, and Facts request lifecycles: cache and request identity, current-member
publication, loading/error/result transitions, annotated selection reset,
runtime documentation suppression, and focus-preserving completion.
`dotnet-inspect.ts` validates the selected overload, constructs exact engine
requests, and retains mutable state, rendering, and annotated-source
interaction handlers. `test/member-detail-inspection.test.ts` gates current and
stale completion, cached failures, runtime documentation, exact request
coordinates, cross-surface invalidation, and focus restoration;
`test/spotlight-identity.test.js` gates composition-root wiring.

`src/call-graph-inspection.ts` owns member call-graph request coordination:
fast local publication followed by full-workspace expansion, runtime-member
platform routing, sequence/current/key stale suppression, visible partial and
terminal failures, and the platform drill stack. `dotnet-inspect.ts` validates
the selected overload, constructs exact engine requests, and supplies paint,
rendering, DOM patching, package-stat, and platform-navigation ports.
`test/call-graph-inspection.test.ts` gates cache reuse, progressive ordering,
workspace and runtime routing, partial failures, stale completion, drill
navigation, and focus restoration; `test/spotlight-identity.test.js` gates
composition-root wiring.

`src/metadata-inspection.ts` owns the type-metadata request lifecycle and the
Metadata Explorer's table-window and heap-listing requests, including cache
identity, package/platform routing, stale-explorer suppression, visible
loading/failure state, and focus-aware completion. `dotnet-inspect.ts` supplies
typed state, engine, rendering, and scroll ports while retaining selection
validation, explorer focus/history navigation, and DOM effects.
`test/metadata-inspection.test.ts` gates cached and stale type completions,
focus preservation, explorer routing, window identity, failure publication,
and focused scrolling; `test/spotlight-identity.test.js` gates composition-root
wiring.

`src/spotlight.ts` owns the modal workbench search, embedded home search,
scope/result rendering, selection, and keyboard interaction.
`src/spotlight-package-search.ts` owns debounced NuGet discovery, generation
cancellation, result publication, and reset state.
`src/command-bar.ts` supplies its typed Commands-scope grammar and results;
`dotnet-inspect.ts` retains command effects, the NuGet query endpoint, package
navigation, and acquisition so the components do not acquire engine or
workspace authority. `test/spotlight.test.ts` and
`test/command-bar.test.ts` gate both presentation modes, scope ownership,
completion and replacement behavior, bounded results, command metadata, and
escaping; `test/spotlight-package-search.test.ts` gates debounce, scope and
query eligibility, cancellation, stale suppression, failure settlement, and
mounted-result refresh.

`src/catalog-requests.ts` owns .NET release and package-version catalog
lifecycles: cache and loading state, request deduplication, version ordering,
package-residency guards, and selector-update dispatch. `dotnet-inspect.ts`
retains the .NET release endpoint, engine version query, option rendering, DOM
repainting, and version switching. `test/catalog-requests.test.ts` gates cache
reuse, in-flight deduplication, sorting, current Platform refresh, package
removal, and both silent transient-failure paths; the composition-root gate
checks that network and DOM authority remain outside the coordinator.

The typed `src/status-bar.ts` component renders both the full-width workspace
data bar and the home readiness bar and owns their rendered toggle binding.
The workspace bar occupies the bottom row formerly used by the persistent
command prompt, giving the bar the full viewport width. By default the bar
shows a compact, single-line summary in
priority order: app version/commit, package provenance, build date, and a
one-line performance summary. A dedicated toggle button at the end of the bar
(so it never overlaps the commit link) expands and collapses the view,
adding the full diagnostics breakdown (download/startup/precompute/total),
package cache stats, active assembly, framework, and the "public API
surface" label. Expansion state lives in `state.statusBarExpanded` and
applies to both the workspace and home bars.
Package source, assembly, and framework are shown only in a workspace.
Current browser acquisition distinguishes NuGet.org from the .NET platform;
the typed model also reserves local-file and custom-feed provenance for
future acquisition paths. Missing or malformed provenance is shown as
`Unknown` rather than omitted so acquisition failures stay diagnosable.
Symbol/PDB acquisition status is not yet surfaced here — no backend contract
reports it today — and is a tracked fast-follow.

`src/type-panel.ts` owns the type selector (the "PUBLIC TYPES" / "MEMBERS" nav
pane), its rendered DOM control bindings (including member filters,
composition jumps, member navigation, and member/type copy controls), and the
type viewer (the type heading, metadata, and source sections shown for the
"type" scope).
`dotnet-inspect.ts` still owns the type index, filtering, member grouping, and
navigation state transitions, and supplies them through typed callbacks; the
shared text helpers used well beyond the type panel (`kindIcon`, `shortKind`,
`typeDisplayName`, `highlight`, `highlightCSharp`, `factRows`,
`relatedTypeChip`) stay in `dotnet-inspect.ts` and are injected the same way.
`test/type-panel.test.ts` gates every rendered control binding, type-filter
keyboard behavior, namespace grouping and selection in the type list,
active-group and overload selection in the member list, the type heading's
package/library fields, the metadata- and source-signature cache keys, and the
metadata/source panels' loading, error, and loaded states.

`src/package-bar.ts` owns the package tab strip (including the always-present
Platform tab), the open-package query form, package framework/version controls,
and their keyboard/mouse/wheel interaction. `dotnet-inspect.ts` supplies the
workspace effects — selecting, closing, opening, or changing a package or the
runtime pack — so the component acquires no engine or workspace authority.
`test/package-bar.test.ts` gates tab markup, active/close state, escaping,
open-package query parsing, and package selection dispatch.

`src/package-view.ts` owns package-level dependency, overview, type-graph, and
performance navigation bindings. `dotnet-inspect.ts` still owns package and
filter state, in-place dependency updates, navigation effects, and member
inspection effects behind typed callbacks. `test/package-view.test.ts` gates
dataset decoding, missing values, replacement dependency-list binding, inactive
surfaces, and no eager dispatch.

`src/library-controls.ts` owns library/accessibility filters, the primary
Platform library selector, and the lens-scoped Platform library selectors.
`dotnet-inspect.ts` still owns filter mutation, runtime-pack acquisition,
generation checks, visible retry state, and lens reload effects behind typed
callbacks. `test/library-controls.test.ts` gates selector mapping, pack
provenance/defaults, empty selections, inactive surfaces, and no eager
dispatch.

`src/shell-controls.ts` owns the rendered workbench chrome, home demo/theme
controls, and load-error retry/query/detail bindings. It reuses the package
query grammar from `package-bar.ts`; `dotnet-inspect.ts` still owns notice and
package state, navigation/history, sharing, theme effects, demo orchestration,
retry selection, and package loading behind typed callbacks.
`test/shell-controls.test.ts` gates every selector, valid and invalid home demo
identities, replacement-package parsing, local error-detail state, inactive
surfaces, and no eager dispatch.

`src/graph-interactions.ts` owns graph-back, pan/zoom, pointer, keyboard, zoom
button, and rendered Mermaid node bindings for type, dependency, and call
graphs. `dotnet-inspect.ts` still owns typed graph-target resolution, package
and member navigation, platform descent, graph rendering, and stale-render
suppression behind callback resolvers. `test/graph-interactions.test.ts` gates
stable Mermaid node identity decoding, navigable and informational nodes,
drag-click suppression, every pan/zoom input, inactive surfaces, and no eager
dispatch.

`src/settings-panel.ts` owns the Settings page and the decompiler "taste"
popover it shares its style catalog with, including each surface's rendered DOM
bindings and the home/workbench controls that open them. `dotnet-inspect.ts`
still owns `state`, localStorage persistence, and the
theme/taste/open/close effects, supplying those actions through typed callbacks.
`test/settings-panel.test.ts` gates the mutually exclusive Settings and popover
binding shapes, input validation, the style catalog's tier grouping,
byte-divergent badges and checked state, the taste popover's active/default
states, and the Settings page's theme, close, and active-style-count states.

`src/scope-bar.ts` owns the scope switcher and lens strip (the segmented
Package/Types/Member control and the buttons beside it for the active scope's
lenses or member sections), including their rendered DOM bindings.
`dotnet-inspect.ts` still owns the current scope, the package/type/member lens
definitions, and each navigation state transition, supplying those effects
through typed callbacks. `test/scope-bar.test.ts` gates each mutually exclusive
binding shape, the active scope segment, active lens/section marking,
keyboard-shortcut indices, and label escaping.

`src/metadata-viewer.ts` owns the Metadata lens (the image-level summary of each
assembly — format stamp, heap sizes, ECMA-335 table row counts, and PE/CLI
headers) and the Metadata Explorer (the spatial table/heap drill-down laid over
it), including the explorer's rendered DOM bindings. Both describe the metadata
image rather than the API surface within it, so they share one module the way
`type-panel.ts` combines the type selector and the type viewer.
`package-inspection.ts` coordinates the package-level image request, while
`metadata-inspection.ts` coordinates type metadata and the explorer's
table-window and heap-listing requests. `dotnet-inspect.ts` still owns `state`,
the explorer's focus/history stack, lazy `IntersectionObserver` hydration,
resize coordination, and global gesture effects, supplying those effects
through typed callbacks and registry declarations; the shared helpers used
well beyond these views
(`escapeHtml`, `fmtBytes`,
`platformLensPicker`, `scopedPlatformLibrary`, `packageScopeSignature`) stay
in `dotnet-inspect.ts` and are injected the same way.
`test/metadata-viewer.test.ts` gates the lens's picker, loading, failure,
stale-scope, partial-read, and empty-image states and its heap/table ordering;
the Metadata-lens table/heap entry controls, the explorer's mutually exclusive
overview/focus binding shapes, chips, history-button enablement, overview
versus focus lightbox, lazy-load hooks, pager bounds, row highlight and
selection, ref->def jump targets, cell escaping, heap addressing and coverage
notes, and the row inspector.

`src/doc-viewer.ts` owns the package document modal (the Markdown reader
opened from a package's documents list) and that list's markup, including its
open, close, and bare-backdrop bindings. `src/document-inspection.ts` owns its
sequence-guarded async load/close lifecycle, visible failure, and frontmatter
projection.
`dotnet-inspect.ts` validates the selected package document and supplies the
engine, sanitized Markdown-rendering, state, and render ports.
`test/doc-viewer.test.ts` gates the closed/no-document fallback, loading and
error presentation, the
frontmatter card's presence and fields, and title/subtitle/frontmatter-name
escaping, package-document list output, open dispatch, and button/backdrop
close dispatch;
`test/document-inspection.test.ts` gates exact request coordinates,
frontmatter projection, stale-stage suppression, visible failures, and close
invalidation (the rendered document body is trusted, pre-sanitized Markdown
HTML and is not escaped).

`src/graph-source.ts` owns the member source modal (the code viewer opened
from a call graph node), including its rendered close and bare-backdrop
bindings.
`source-inspection.ts` owns its sequence-guarded async lifecycle;
`dotnet-inspect.ts` supplies `state`, the typed engine port, and the
`highlightCSharp` Prism wrapper, and passes each computed slice explicitly.
`test/graph-source.test.ts` gates the loading state, the
original-versus-decompiled provenance labels, the open-source link's presence
only when a `url` is provided, the error state's fallback message, and title
escaping in both the header and loading status, plus button/backdrop close
dispatch.

`src/annotated-source.ts` owns the annotated source result (the
fact-annotated C#/IL dual view shown for a member overload), including its
rendered copy, medium, fact, source-offset, and clear-selection bindings; it
composes `annotated-source-view.ts`'s `buildAnnotatedView` projection into
markup. `member-detail-inspection.ts` owns the sequence-guarded async load
lifecycle; `dotnet-inspect.ts` still owns `state`, document interpretation,
copy/render effects, and the selection transitions, supplying them through
typed callbacks.
`test/annotated-source.test.ts` gates the rejected-document fallback, the
medium toggles and hidden-line count, the context-limitation notice, anchored
versus unanchored fact rendering, selection state, binding dispatch and
malformed dataset behavior, and source-text escaping.

`src/package-opportunities.ts` owns the package/platform "Integration
opportunities" lens (the ecosystem auth/cloud/config/database/AI-client
integration suggestions for a package or platform library), including its
rendered DOM bindings, opportunity-row API-name splitting, package-chip
detection, and "look for" chip rendering. `dotnet-inspect.ts` still owns
`state`, target resolution, navigation effects, and the platform library
picker, `package-inspection.ts` owns the scan-scope-keyed async load lifecycle,
and the root supplies behavior through typed callbacks.
`test/package-opportunities.test.ts` gates the platform pick-a-library prompt,
the scanning/loading/error states (fresh versus stale scope), the
no-opportunities and inspection-error banners, the category summary counts,
API name splitting, package-chip versus plain-text kind rendering, look-for
chip/wildcard/empty rendering, binding dispatch and empty-value behavior, and
text escaping.

- `Cmd/Ctrl+K` opens Spotlight in the Commands scope.
- `Cmd/Ctrl+P` opens Spotlight in the All scope.
- `Cmd/Ctrl+F` or `/` focuses the type filter.
- Arrow keys select a Spotlight result, `Tab` cycles scopes, and `Enter`
  completes or runs a command.
- Arrow keys or `j`/`k` navigate the type index.
- Number keys switch the active scope's lenses when an input is not focused.
- `share` copies the package, version, framework, type, and lens selection.
- The Taste popover and Settings page consume the same `C# Style Tiers` and
  `C# Style Choices` vocabulary sections as the CLI; the browser does not
  restate their taxonomy.

## Deploy

`.github/workflows/deploy-inspect-web.yml` publishes every `main` commit,
archives the resulting `wwwroot` and prebuilt managed API as the run-scoped
`inspect-web-site` GitHub artifact, then uses a fresh environment-gated job to
download that artifact by ID with digest mismatch configured as an error and
deploy it to the public staging site at `https://dotnet-inspect.ca`. The upload
includes the managed API's hidden `.azurefunctions` dependencies and overwrites
the same-name artifact on a rerun, so a cancelled attempt can be retried without
leaving multiple artifacts that promotion rejects.
`PromotionWorkflowContract` gates both properties. The post-download gate
requires the extension loader before deployment. Candidate build code never
runs in the staging deployment job. The separate
`inspect-web-staging` GitHub environment accepts only `main` and holds a
deployment token scoped to the staging Azure Static Web App.

Successful main-push completion of `.github/workflows/deploy-inspect-web.yml`
triggers `.github/workflows/deploy-inspect-web-coreclr.yml`, which checks out
that run's exact head and downloads its exact `inspect-web-site` artifact before
publishing the same commit to the isolated comparison site at
`https://coreclr.dotnet-inspect.ca`. It uses a third Azure Static Web App, the
main-only `inspect-web-coreclr-staging` environment, a distinct deployment
token, and the non-promotable `inspect-web-coreclr-site` artifact. The site is
interpreter-only while the .NET 11 Preview 7 SDK lacks the packaged headers and
Emscripten cache wiring needed for CoreCLR native relinking. The workflow pins
the same proven preview SDK as Mono staging, enables `runtime-async=on` across
this application graph, and applies the `UseMonoRuntime=false`,
`WasmBuildNative=false`,
`WasmNestedPublishAppDependsOn=`, and `WasmEnableExceptionHandling=true`
overrides. This exercises runtime async only in the CoreCLR comparison
deployment; Mono staging and ordinary non-AOT builds retain classic async
lowering. The workflow verifies the CoreCLR-specific `GetDotNetRuntimeHeap`
hook before and after artifact transfer. Before the CoreCLR artifact crosses
the upload/deploy boundary, the workflow compares its schema-5 runtime receipt
with the triggering Mono run's schema-5 compiler receipt.

Both deployment builds import `InspectWebAsyncLoweringReceipt.targets`. Every
project that reaches `CoreCompile` fails unless its exact `Features` property
selects the deployment's expected lowering, then emits a project-path receipt.
`verify-async-project-graph.ts` requires those receipts to equal the evaluated
transitive repository project graph rooted at `InspectWeb.Engine.csproj`;
framework/runtime-pack binaries, the separately published MSDL server API, and
unrelated repository projects are outside that set.

Both builds then run `verify-inspect-web-async-deployment.sh` immediately after
their clean engine publish. The gate derives the seven export assemblies from
the compiled `InspectWebJsExportContext`, enumerates every public async export
as compiler async for Mono and runtime async for CoreCLR, and requires the
entire census to use the expected physical lowering. These are the exact
pre-link assemblies that retain the compiler-generated runtime wrappers
authenticated by `ts-jsexport`; the linker removes those wrappers from its
intermediate assemblies before packaging the shipped WebCIL.

The gate regenerates all seven declarations with
`generate-inspect-web-engine-facade.sh --contract`, compiles every generated
source with the pinned consumer program, and requires the declarations and
JavaScript bytes to equal the checked-in and published artifacts. It then
initializes all seven facades through the published Browser/Wasm runtime and
invokes the host's `AsyncLoweringCanary`. The verifier carries the same
authoritative product `VersionPrefix` used by the deployment build into
`ts-jsexport`, so the compiled context and generator authenticate the same exact
`TsJsExport.Contracts` assembly identity.

The schema-5 receipt preserves each facade's assembly, generated source,
declaration, published JavaScript, and shipped WebCIL identity and digest beside
its export and lowering counts. It also records the exact sorted repository
project identities, their count and digest, and the successful initialization
and canary outcome. Paired receipt validation requires both deployments to
describe the same facade and project domains and treats only the inverse
compiler-async/runtime-async counts as mode-specific. Build, staging, and
production checks recompute every transferred digest without executing
candidate code in an environment-gated deployment job.
`PromotionWorkflowContract` gates both expected-lowering properties, exact
facade domains, both browser invocations, graph receipts, and post-transfer
evidence checks with close mutations.

`.github/workflows/promote-inspect-web.yml` intentionally promotes one
successful staging run to production at `https://dotnet-inspect.net`. The
operator supplies the staging run ID and types `promote`; the workflow verifies
that the run was a successful `main` build through the staging workflow, that
its `Publish staging` job succeeded, and that it produced one unexpired,
nonempty `inspect-web-site` artifact. Main-push staging is the default. An
operator-dispatched staging run is accepted only when the promotion dispatch
explicitly enables `allow_manual_staging`; the validator rejects it otherwise.
After production approval the workflow revalidates that same override, run
attempt, commit, artifact identity, and digest, downloads the exact artifact ID
with digest mismatch configured as an error, and deploys the archived staging
files. `validate-inspect-web-promotion.cs --self-test`, run by inspect-web CI,
gates the default rejection, explicit exception, and other close negative cases;
the CI change-detection workflow contract gate keeps all deployment jobs free
of candidate code, closes the CoreCLR runtime and credential contract, keeps
production revalidation on the trusted dispatch revision, and orders each
artifact download before only verification and deployment.

Production promotion uses the distinct `inspect-web-production-promotion`
environment and `AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB_PRODUCTION`
secret. Legacy deployment workflows reference neither name. During cutover,
cancel outstanding legacy deployment runs, reset the production Static Web App
deployment token, put the replacement token only in the promotion environment,
and delete both the old `inspect-web-production` environment secret and the
repository-scoped token. Token rotation invalidates credentials already copied
into queued parent-era jobs; deleting both old secret locations makes later
reruns fail closed.

All three deployment workflows pin the Azure deployment action to an exact
commit and pin their checkout, SDK setup, and artifact actions to exact
commits. The workflow contract gate enforces those references. Azure's pinned
action still pulls Microsoft's `staticappsclient:stable` image; that
vendor-controlled deployment dependency is not immutable and remains inside
the Azure trust boundary. All three workflows disable Azure's own app and API
builds and require the published artifact to contain
`staticwebapp.config.json`, `host.json`, `functions.metadata`, and
`worker.config.json`.
Trusted build and deployment steps also verify that Vite preserved the authored
.NET placeholders, that every file in Vite's generated manifest exists and is
loaded by the index where required, that the SDK injected a mapping to the
fingerprinted `dotnet.js`, and that the import map precedes the Vite module
entry. That configuration serves `/` and `/index.html` with `Cache-Control:
no-cache, no-store, must-revalidate`, so an Azure edge cannot retain an old
browser boot graph after its fingerprinted Wasm assets rotate.
`BrowserStaticWebAppConfigTests.RootDocumentsAreNotCachedAndConfigIsPublished`
gates the header contract and publish wiring. The staging publish step embeds
the CLI's authoritative `VersionPrefix`, exact source SHA, and UTC build
timestamp. The home and workspace status bars show that version, link the
short commit to GitHub, and disclose the binary build time.
`BuildIdentity_UsesVersionedRepositoryProvenance` and
`ready status shows versioned linked build provenance` gate the engine and UI
halves.

The Azure resources, custom-domain assignments, GitHub environments, branch
restrictions, required production reviewer, and environment-scoped deployment
tokens live outside this repository and are **not** verified by anything in it.
Treat successful staging and promotion runs, not this file, as evidence that
the corresponding deployed site is current. Both staging domains are public
infrastructure and are not confidentiality boundaries.

See [architecture-spike.md](architecture-spike.md) for the proposed .NET 11
browser engine and the NativeAOT decision.
