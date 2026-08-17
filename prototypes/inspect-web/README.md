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

1. **Resolve an exact identity.** `PackageCoordinateResolver` validates the
   package id and resolves an omitted version through the authorized nuget.org
   source without a filesystem cache. The Browser adapter then selects one
   target framework — never "whatever the package happens to ship".
2. **Mint typed participants.** `PackagePayloadAcquisition` downloads and
   admits the package through the shared source, transport, and archive policy.
   `PackageCompileAssetSelector` adds reference-group semantics around the
   implementation universe selected by `PackageAssetSelector`, decodes each
   healthy entry's real metadata identity, and creates one
   `ResolvedAssemblyReference` per selected compile asset and, when the roles
   differ, per matching implementation asset. Malformed selected entries remain
   participants so queries report their rejection. Acquisition never inspects
   one.
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
| `engine/BrowserInspectionEngine.cs` | the supported `[JSExport]` operations |
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
`BrowserEngineBoundaryTests.PackageCoordinates_AreRejectedBeforeAnyCacheOrNetworkAccess`
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
all ZIP64 sentinels before `ZipArchive` enumeration. The store independently
caps all retained PDB bytes at 24 MiB. SourceLink requests are authorized before
dispatch for HTTPS URLs on GitHub, Azure DevOps, GitLab, and Bitbucket source
hosts, and the Browser transport refuses redirects; unsupported hosts visibly
fall back to decompilation.

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
and the JavaScript `source requests carry exact type and member identities` and
`call graph source identity prefers the structured type definition` cases gate
these boundaries.

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
HTML-encodes delimiters and visibly encodes control and line-separator
characters before artifact labels enter the grammar. The type-relationship
renderer applies the same containment. Call-graph navigation receives typed
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
| every `QueryPlatform*`, `ExpandPlatformCallGraph`, `LoadRuntimePack`, `LoadRuntimePackAssembly` | runtime-pack acquisition that produces participants from content |

One further gap is about acquisition rather than inspection:

- `ResolvedAssemblyReference.CreateFromPathIfManaged` has **no content-shaped
  sibling**, so a filesystem-free acquisition owner must decode assembly identity
  itself before it can mint a participant the group will accept.

Each gap has a tracking issue; the pull request that introduced this rebuild
lists them.

## Annotated source

`src/annotated-source-view.js` and its tests are the browser half of the [#3964]
portable `AnnotatedSourceDocument` contract, and `QueryMemberAnnotatedSource` now
feeds it a real document.

The viewer reuses the owner's module rather than copying it.
`prototypes/annotated-source-viewer/src/document-model.js` owns validation,
UTF-16 coordinates, line derivation, segmentation, and the fact → target → node →
span walk; the engine project links that exact file into
`wwwroot/src/document-model.js`. `src/document-model.js` here is a re-export the
repository tree and the Node tests resolve, and it holds no logic. On top of it
the view module adds only selection state: canonical lines, C#/IL medium toggles
that hide lines without rebasing a coordinate, fact selection that highlights
every targeted node across both media without selecting the text between one
node's separated spans, click-to-tightest-node, explicitly unanchored facts, and
a copy action that copies `document.text` so the copied artifact is source and
never annotations. A payload the model rejects is reported as rejected, not
rendered.

## Run

Install the experimental browser workload selected by the repository SDK:

```bash
dotnet workload install wasm-experimental
cd prototypes/inspect-web/engine
dotnet run -c Release
```

Open `http://127.0.0.1:5198`. Create a deployable static bundle with
`dotnet publish -c Release`. Remote addresses require HTTPS because the .NET
loader uses secure-context browser APIs.

On a bare visit, `app.js` waits for the home page's first contentful paint
before dynamically importing `engine.js`. Search and demo controls remain
inert behind a loading indicator until the Wasm engine is ready; package and
shared-workspace deep links retain the full loading interstitial. The
`bare home paints before wasm engine download` JavaScript test gates this
startup boundary.

The .NET 11 preview Emscripten wrapper currently mishandles an SDK packs path
that contains whitespace. If that applies to the local SDK installation, pass
`EmscriptenSdkToolsPath` pointing to a no-whitespace link to the installed
Emscripten `tools` directory.

## Test

```bash
dotnet run --project prototypes/inspect-web/engine.Tests -c Release
cd prototypes/inspect-web
npm test
```

`BrowserEngineBoundaryTests` gates the browser host's aggregate archive budget,
central-directory entry limit before archive enumeration, role preflight before identity decoding, malformed selected-participant
visibility, reference-only retained-image budget, duplicate XML parameter
handling, Mermaid label containment, and complete call-graph navigation targets.
The JavaScript tests gate the annotated view helper against the shared sample
document and keep Spotlight candidate/cache identity coordinate-complete.

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
CI job. That job compiles the platform-index generator, publishes the Release
Wasm bundle, runs the browser-engine tests, and runs both JavaScript suites.
The `eng/CiChangeDetection` gate, invoked through
`eng/test-ci-change-detection.cs`, gates the path classification, and
`ci-required` includes the job's result.

## Interaction model

Package tabs and the framework selector are workspace identity, not display
state: changing either resolves a different workspace. Lenses this engine does
not answer report the engine's failure rather than fixture results.

- `Cmd/Ctrl+K` focuses the persistent command prompt.
- `Cmd/Ctrl+F` or `/` focuses the type filter.
- Arrow keys select a completion, `Tab` accepts it, and `Enter` runs it.
- Arrow keys or `j`/`k` navigate the type index.
- Number keys switch the active scope's lenses when an input is not focused.
- `share` copies the package, version, framework, type, and lens selection.
- The Taste popover and Settings page consume the same `C# Style Tiers` and
  `C# Style Choices` vocabulary sections as the CLI; the browser does not
  restate their taxonomy.

## Deploy

`.github/workflows/deploy-inspect-web.yml` publishes the browser-Wasm project and
uploads the prebuilt `wwwroot` to an Azure Static Web App with Azure's own app
build disabled. It runs on every push to `main`; `workflow_dispatch` remains
available, but the deploy job itself requires `refs/heads/main`, so a manual run
cannot publish another ref. Its deployment credential is the
`AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB` GitHub Actions secret.
The publish step embeds the CLI's authoritative `VersionPrefix`, the exact
`GITHUB_SHA`, and a UTC build timestamp in the engine. The home and workspace
status bars show that version, link the short commit to GitHub, and disclose the
binary build time. `BuildIdentity_UsesVersionedRepositoryProvenance` and
`ready status shows versioned linked build provenance` gate the engine and UI
halves.

Two prerequisites live outside this repository and are **not** verified by
anything in it: the secret must be present, and the Static Web App resource's
production Branch setting must name the branch being deployed. Neither has been
confirmed from this branch, so treat a green workflow run — not this file — as
the evidence that a deployed site is current.

See [architecture-spike.md](architecture-spike.md) for the proposed .NET 11
browser engine and the NativeAOT decision.
