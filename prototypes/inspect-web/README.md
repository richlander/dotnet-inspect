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

The rule is enforced by the compiler, not by a convention.
`engine/BannedSymbols.txt` bans `AssemblyInspectionSession`, `MetadataSource`,
`LibraryBodyIndex`, `AssemblyImageSnapshot`, raw metadata readers, and the group's image and
retained-descriptor accessors in this project, and `Directory.Build.targets`
already escalates `RS0030` to an error for every project.
`BrowserEngineLayeringTests` in `engine.Tests` pins that wiring and
resolves every complete banned documentation id, including generic arity and
parameter types — a renamed or malformed entry bans nothing and fails the gate.
It also bans opening a retained descriptor or invoking `AssemblyReader` in the
host; descriptors may carry typed identity into a product query, but their image
content remains query-owned.
The narrow `acquisition` project decodes a healthy entry's real metadata identity;
the engine receives only that typed identity and cannot open a raw reader.
A selected malformed entry receives a path-derived identity only as a rejection
carrier, so the workspace returns its typed failure instead of silently
shortening the selected assembly set.

## How a workspace is opened

1. **Resolve an exact identity.** `PackageSourceCoordinateResolver` validates
   the package id. An exact pin bypasses discovery; an omitted version uses the
   NuGet Gallery search endpoint and accepts only the exact package ID's listed
   stable result. Neither path requests the NuGet.org v3 service index. The
   Browser adapter then selects one target framework — never "whatever the
   package happens to ship".
2. **Mint typed participants.** `PackagePayloadAcquisition` downloads and
   admits the package from the Gallery package CDN through the shared typed
   source, transport, and archive policy. The Gallery payload carries its
   advertised length into the Browser reservation policy before body
   materialization.
   `PackageCompileAssetSelector` adds reference-group semantics around the
   implementation universe selected by `PackageAssetSelector`, decodes each
   healthy entry's real metadata identity, and creates one
   `ResolvedAssemblyReference` per selected compile asset and, when the roles
   differ, per matching implementation asset. Malformed selected entries remain
   participants so queries report their rejection. Acquisition never inspects
   one.
   The Browser adapter places one 30-second operation deadline around coordinate
   resolution and payload acquisition. The deadline token flows through the
   shared resolver, retry, response-body, archive-validation, and store paths;
   expiry is surfaced as a visible timeout instead of leaving the page behind
   an unbounded loading indicator.
   Gallery version enumeration currently exposes raw flat-container versions
   with unknown listing state. The version picker may display that partial
   enumeration, but dependency wildcard and range selection fails closed until
   registration-backed listing state is implemented; exact dependency pins
   remain available.
3. **Hand the group to a query.** The participants open one `InspectionWorkspace`
   and one binding-consistent `AssemblyContextGroup`. `BrowserInspectionScope`
   exposes exactly two hand-offs — `Use(group => query(group))` and
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
or whose selected set exceeds 256 assemblies. This keeps acquisition itself
inside the same bound rather than relying on the later retained-snapshot check.
Failures after the role passes that preflight remain typed participant outcomes
beside healthy results.

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
distinction at the browser boundary. Constructed generic nodes recover assembly
identity from their definition. Synthetic array and function-pointer nodes remain visible but carry
no navigable definition identity. Accessor nodes resolve through their opaque
body selector even when the graph has no `MethodDef` token.

[#3932]: https://github.com/richlander/dotnet-inspect/pull/3932

## Engine layout

| File | Owns |
| --- | --- |
| `acquisition/BrowserAssemblyIdentityDecoder.cs` | the isolated raw metadata read needed to mint an exact participant identity |
| `engine/Program.cs` | the entry point, and nothing else |
| `engine/BannedSymbols.txt` | the compiler-enforced workspace rule |
| `engine/BrowserContracts.cs` | the transport records and their source-generated JSON context |
| `engine/BrowserPackageWorkspace.cs` | the Browser adapter over shared package acquisition, the session cache/capacity policy, reference-role selection, participant minting, and the bounded workspace registry |
| `engine/BrowserApiSurfacePolicy.cs` | the explicit participant/type/member bounds every API-surface projection runs under |
| `engine/BrowserInspectionScope.cs` | the `InspectionWorkspace` lifetime and its compile/implementation group hand-offs |
| `engine/BrowserSurfaceProjection.cs` | adapting typed query models into transport records |
| `engine/BrowserStyleOptions.cs` | resolving the client's style ids through `StyleOptionCatalog` |
| `engine/BrowserXmlDocumentation.cs` | reading one member's package-shipped XML documentation |
| `engine/InspectionEngine.cs` | the supported `[JSExport]` operations |
| `engine/BrowserSourceOperations.cs` | pathless authored-or-decompiled type/member source and Browser source capabilities |
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
Integrations, and call graphs. Opportunities use the compile group because they
classify the package's reference-preferred public surface. Packages without
`ref/` assets share one group for both roles. When both roles exist and differ, they split the scope's 64 MB retained image
budget rather than doubling it. A reference-only package has one group and uses
the full budget.

## Supported

| Operation | Workspace | Query that owns the session |
| --- | --- | --- |
| `QueryPackage` | one package/version/framework | `AssemblyContextApiSurfaceQuery.ExecuteBounded(group, scope, limits, participants)` |
| `QueryTypeProjection` | one package/version/framework | `AssemblyContextTypeProjectionQuery.ExecuteParticipant(...)` |
| `QueryMemberAnnotatedSource` | one package/version/framework | `AssemblyContextMemberProjectionQuery.ExecuteParticipant(...)` |
| `QueryMemberSource`, `QueryTypeSource`, `QueryTypeMemberSource` | one package/version/framework | `AssemblyContextSourceQuery.ExecuteMemberAsync(...)` / `ExecuteTypeAsync(...)` |
| `QueryPackageDependencies` | one package/version/framework | `PackageDependencyGroupsQuery.ExecuteAsync(content, ...)` and `AssemblyContextReferencesQuery.ExecuteParticipant(...)` |
| `QueryPackageIntegrations` | one package/version/framework | `AssemblyContextIntegrationsQuery.Execute(group)` |
| `QueryPackageOpportunities` | one package/version/framework | `AssemblyContextIntegrationOpportunitiesQuery.Execute(group, prerequisites)` |
| `QueryMemberCallGraph` | every open package coordinate, implementation group | `MemberCallGraphSession` |

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
`AssemblyContextSourceQuery`. The query tries checksum-verified authored source
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
bridge. Decompiled results disclose why the authored attempt was unavailable.
Reference-only type source is refused rather than presented as a body-free
decompilation. Printer options apply to decompiled fallback and never rewrite
authored source. Whole-member source remains MethodDef-scoped: a
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
| `QueryMemberFacts` | method-scoped Analysis evidence over a group participant |
| `QueryPackageMetadata`, `QueryPackageMetadataTable`, `QueryPackageHeapEntries` | metadata image, table, and heap projections over a group (`MetadataImageQuery` binds to a host-opened session today) |
| `QueryPackagePerformance` | assembly-wide Analysis ranking over a group |
| every `QueryPlatform*`, `ExpandPlatformCallGraph`, `LoadRuntimePack`, `LoadRuntimePackAssembly` | `WorkspaceContextLoader` now produces runtime-pack participants from content; the Browser host still needs platform scope caching, typed-result adaptation, and the missing group-scoped metadata/performance queries named above |

`ResolvedAssemblyReference.CreateFromStreamIfManaged` owns pathless identity
decoding, so Browser acquisition does not reconstruct assembly identity.

Each gap has a tracking issue; the pull request that introduced this rebuild
lists them.

## Annotated source

`src/annotated-source-view.ts` and its tests are the browser half of the [#3964]
portable `AnnotatedSourceDocument` contract, and `QueryMemberAnnotatedSource` now
feeds it a real document.

The viewer reuses the owner's module rather than copying it.
`prototypes/annotated-source-viewer/src/document-model.js` owns validation,
UTF-16 coordinates, line derivation, segmentation, and the fact → target → node →
span walk. `src/document-model.ts` provides typed aliases over that owner for
Vite and the Node tests; Vite bundles the shared implementation into the
deployable browser artifact without copying its logic. On top of it the typed
view module adds only selection state:
canonical lines, C#/IL medium toggles that hide lines without rebasing a
coordinate, fact selection that highlights every targeted node across both
media without selecting the text between one node's separated spans,
click-to-tightest-node, explicitly unanchored facts, and a copy action that
copies `document.text` so the copied artifact is source and never annotations.
A payload the model rejects is reported as rejected, not rendered.

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
browser APIs.

On a bare visit, `dotnet-inspect.ts` waits for the home page's first contentful paint
before dynamically importing `inspect-web-engine.js`. Search and demo controls
remain inert behind a loading indicator until the Wasm engine is ready; package
and shared-workspace deep links retain the full loading interstitial. The `bare
home paints before wasm engine download` JavaScript test gates this startup
boundary.

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

Oxlint checks both checked-in tsbindgen outputs as consumer contracts:
`src/inspect-web-engine.d.ts` receives the TypeScript rules, while
`engine/wwwroot/inspect-web-engine.js` receives the JavaScript correctness and
suspicious rules described below. TypeScript compilation and the generated
surface drift gate provide independent declaration coverage. The toolchain
test pins both generated lint inputs so a generator change cannot silently
leave analysis coverage. The configuration disables four non-correctness
rules: underscore spelling, function relocation, listener API preference, and
`Array.prototype.sort`. Those rules prescribe naming/layout churn or, for
sorting, the ES2023 `toSorted` API while this project targets ES2022.

Existing JavaScript tests and verification scripts remain covered by Oxlint's
correctness and suspicious rules, but not by its unsafe-operation type rules:
adding a shadow type model or migrating those files to TypeScript is outside
this analysis change. Their dependency and reachability graph remains covered
by Knip.

Knip checks authored source, every TypeScript and JavaScript test, and
build/verification scripts for unused files, exports, and dependencies.
`knip.json` excludes only `engine/wwwroot/inspect-web-engine.js`: that generated
publish artifact imports `./_framework/dotnet.js`, which exists only after Wasm
publish. The exclusion is specific to Knip reachability; Oxlint still checks
the generated module.

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

`noUncheckedIndexedAccess` is not enabled yet. A TypeScript 7.0.2 migration
probe reports 77 findings across 15 files: 65 in nine product files and 12 in
six test files. [Issue #4549](https://github.com/richlander/dotnet-inspect/issues/4549)
records the probe commands, file distribution, and migration discipline; the
set should be migrated with close negative tests for indexing behavior rather
than hidden behind assertions.

## Test

```bash
cd prototypes/inspect-web
npm ci
npm run analyze
npm run build
npm test
cd ../..
dotnet run --project prototypes/inspect-web/engine.Tests -c Release
```

`BrowserEngineBoundaryTests` gates the browser host's aggregate archive budget,
central-directory entry limit before archive enumeration, role preflight before identity decoding, malformed selected-participant
visibility, reference-only retained-image budget, duplicate XML parameter
handling, Mermaid label containment, and complete call-graph navigation targets.
The frontend tests gate the annotated view helper against the shared sample
document and keep Spotlight candidate/cache identity coordinate-complete.
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
navigation generation, share-packet encoding and decoding, URL parsing and
building, and the browser-history port. `dotnet-inspect.ts` remains the sole
mutable application-state owner: it supplies typed snapshots and explicit
transition callbacks, and retains asynchronous workspace restoration and DOM
event binding. `test/workspace-navigation.test.ts` gates history traversal,
stale-entry removal, navigation cancellation, rich and legacy URL
compatibility, visible invalid-state failures, and sandboxed history errors;
`test/spotlight-identity.test.js` gates the composition-root wiring.

`src/package-acquisition.ts` owns NuGet and runtime-pack engine invocation,
surface-to-workspace-model projection, serialized runtime-pack loading, and
stale-result checks at the publication boundary. `dotnet-inspect.ts` supplies
the engine and state ports and retains mutable loading/error state, package
activation, workspace restoration, notices, retries, and rendering.
`test/package-acquisition.test.ts` gates package projection, publication
ordering, runtime request serialization and merging, cancellation after queued
or in-flight work, request-local failure reporting, replacement-slot
preservation, retry after failure, and resident-pack reuse;
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
resize coordination, and the global keydown handler, supplying those effects
through typed callbacks; the shared helpers used well beyond these views
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
includes the managed API's hidden `.azurefunctions` dependencies, and the
post-download gate requires its extension loader before deployment. Candidate
build code never runs in the staging deployment job. The separate
`inspect-web-staging` GitHub environment accepts only `main` and holds a
deployment token scoped to the staging Azure Static Web App.

`.github/workflows/deploy-inspect-web-coreclr.yml` publishes the same `main`
commit to the isolated comparison site at
`https://coreclr.dotnet-inspect.ca`. It uses a third Azure Static Web App, the
main-only `inspect-web-coreclr-staging` environment, a distinct deployment
token, and the non-promotable `inspect-web-coreclr-site` artifact. The site is
interpreter-only while the .NET 11 Preview 7 SDK lacks the packaged headers and
Emscripten cache wiring needed for CoreCLR native relinking. The workflow pins
the proven preview SDK, enables `runtime-async=on` across this application
graph, and applies the `UseMonoRuntime=false`, `WasmBuildNative=false`,
`WasmNestedPublishAppDependsOn=`, and `WasmEnableExceptionHandling=true`
overrides. This exercises runtime async only in the CoreCLR comparison
deployment; Mono staging and ordinary non-AOT builds retain classic async
lowering. The workflow verifies the CoreCLR-specific `GetDotNetRuntimeHeap`
hook before and after artifact transfer.

`.github/workflows/promote-inspect-web.yml` intentionally promotes one
successful staging run to production at `https://dotnet-inspect.net`. The
operator supplies the staging run ID and types `promote`; the workflow verifies
that the run was a successful `main` push through the staging workflow, that
its `Publish staging` job succeeded, and that it produced one unexpired,
nonempty `inspect-web-site` artifact. After production approval it revalidates
the run attempt, commit, artifact identity, and digest, downloads the exact
artifact ID with digest mismatch configured as an error, and deploys the
archived staging files. `validate-inspect-web-promotion.cs --self-test`, run
by inspect-web CI, gates the evidence discriminator and close negative cases;
the CI change-detection workflow contract gate keeps all deployment jobs free
of candidate code, closes the CoreCLR runtime and credential contract, keeps
production revalidation on the trusted dispatch revision, and orders each
artifact download before only verification and deployment. Manual staging runs
remain useful for recovery but are deliberately not promotable.

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
