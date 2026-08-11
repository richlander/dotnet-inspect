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
`LibraryBodyIndex`, `AssemblyImageSnapshot`, and the group's image and
retained-descriptor accessors in this project, and `Directory.Build.targets`
already escalates `RS0030` to an error for every project.
`BrowserEngineLayeringTests` in `src/dotnet-inspect.Tests` pins that wiring,
checks that every banned identifier still resolves — a renamed entry bans
nothing — and pins the one deliberate exception: acquisition still decodes an
entry's real metadata identity, because the workspace refuses a participant
minted with a placeholder one.

## How a workspace is opened

1. **Resolve an exact identity.** A package id, a resolved version, and a target
   framework — never "whatever the package happens to ship".
2. **Mint typed participants.** Centralized acquisition downloads the package,
   selects compile assets with `PackageCompileAssetSelector`, decodes each
   entry's real metadata identity, and creates one `ResolvedAssemblyReference`
   per implementation assembly. Acquisition never inspects one.
3. **Hand the group to a query.** The participants open one `InspectionWorkspace`
   and one binding-consistent `AssemblyContextGroup`. `BrowserInspectionScope`
   exposes exactly two hand-offs — `Use(group => query(group))` and
   `UseParticipant(participant, (group, participant) => query(...))` — and no
   accessor for a session, an image, or a descriptor.

A workspace is **keyed by its complete exact coordinate set and reused**. The
package surface, a type projection, an annotated member, Integrations, and a
composite call-graph workspace over several packages all reach the same open
group rather than reacquiring every image. `BrowserPackageWorkspace` keeps at
most four scopes and disposes the least recently used one on eviction, which is
what returns its retained image bytes. A scope carries a 64 MB aggregate
retained-image budget; split compile/implementation roles receive 32 MB each.
Exhausting a group budget surfaces as a typed `ResourceBudget` rejection beside
the results rather than as a silently shorter list.

Because a scope is reused, nothing here runs
`AssemblyContextIntegrationsQuery.ExecuteParticipantAsync` ([#3932]): its release
is terminal for the released participant, so a later whole-group query over the
same group would find that participant unavailable. Bounded retained bytes come
from scope eviction instead, which disposes a group rather than half-emptying it.
The banned-symbol list makes that a compile error rather than a comment.

A workspace may also span several package coordinates on purpose:
`MemberCallGraphSession` can only see callers in a sibling package when that
package is a participant of the *same* binding-consistent group, so the call
graph opens one workspace over every package the site currently has open.

[#3932]: https://github.com/richlander/dotnet-inspect/pull/3932

## Engine layout

| File | Owns |
| --- | --- |
| `engine/Program.cs` | the entry point, and nothing else |
| `engine/BannedSymbols.txt` | the compiler-enforced workspace rule |
| `engine/BrowserContracts.cs` | the transport records and their source-generated JSON context |
| `engine/BrowserPackageWorkspace.cs` | acquisition, the package cache, exact package/version/framework identity, participant minting, and the bounded workspace registry |
| `engine/BrowserInspectionScope.cs` | the `InspectionWorkspace` lifetime and its compile/implementation group hand-offs |
| `engine/BrowserSurfaceProjection.cs` | adapting typed query models into transport records |
| `engine/BrowserStyleOptions.cs` | resolving the client's style ids through `StyleOptionCatalog` |
| `engine/BrowserXmlDocumentation.cs` | reading one member's package-shipped XML documentation |
| `engine/BrowserInspectionEngine.cs` | the supported `[JSExport]` operations |
| `engine/BrowserUnsupportedOperations.cs` | the `[JSExport]` operations this engine refuses |

Inspected assemblies are read with System.Reflection.Metadata only, are never
written to a file, and are never loaded into the runtime. Browser/Wasm is
single-threaded, and both caches are written for that host: at most 12 packages
or 128 MB of package content, and at most four open workspaces, each evicting the
least recently used entry.

Acquisition is bounded before content enters either cache or workspace. A
version-index response may contain at most 1 MB, one downloaded nupkg at most
128 MB, one expanded assembly entry at most 64 MB, and one expanded Markdown or
XML entry at most 16 MB. `InMemoryPackageContent` checks a ZIP entry's declared
expanded length before allocation and verifies the observed expansion against
that declaration. `InMemoryPackageContentTests` gates both the pre-expansion
rejection and bounded stream reading. These are Browser-Wasm host limits, not a
product-wide archive-budget policy.

Each retained scope has an explicit compile role and implementation role. The
compile group uses the selector's reference-preferred assets for API and type
views; the implementation group uses matching `lib/` assets for bodies,
Integrations, and call graphs. Opportunities use the compile group because they
classify the package's reference-preferred public surface. Packages without
`ref/` assets share one group for both roles. When the roles differ, they split
the scope's 64 MB retained image budget rather than doubling it.

## Supported

| Operation | Workspace | Query that owns the session |
| --- | --- | --- |
| `QueryPackage` | one package/version/framework | `AssemblyContextApiSurfaceQuery.Execute(group, scope)` |
| `QueryTypeProjection` | one package/version/framework | `AssemblyContextTypeProjectionQuery.ExecuteParticipant(...)` |
| `QueryMemberAnnotatedSource` | one package/version/framework | `AssemblyContextMemberProjectionQuery.ExecuteParticipant(...)` |
| `QueryPackageIntegrations` | one package/version/framework | `AssemblyContextIntegrationsQuery.Execute(group)` |
| `QueryPackageOpportunities` | one package/version/framework | `AssemblyContextIntegrationOpportunitiesQuery.Execute(group, prerequisites)` |
| `QueryMemberCallGraph` | every open package coordinate, implementation group | `MemberCallGraphSession` |

`QueryPackage` is the site's default path. It runs against the product-selected
compile assets, so `ref/` assemblies remain authoritative when the package ships
them. It asks the API-surface query for the composed scope — the default consumer
surface plus the types only the include-all surface reaches — so a public type
keeps its public member list while non-public types remain reachable through the
accessibility filter. Every accessibility
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

[#3964]: https://github.com/richlander/dotnet-inspect/pull/3964

Two exports read **package content** without inspecting an assembly, so they open
no group: `GetPackageDocument` (the package's own Markdown manifest, path-checked
against that manifest) and `QueryMemberDocumentation` (the XML file shipped
beside a product-selected compile asset).

Three exports touch **no artifact at all** and say so in place: `SearchTypes`
(ranking names the client already holds, through `TypeMatcher`),
`PackageCacheStats`, and `ListStyleTiers`/`ListStyleOptions` (the
`StyleOptionCatalog`).

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

`QueryMemberCallGraph` projects `MemberCallGraphView` through
`ILInspector.CallGraph.CallGraphProjection` and renders Mermaid in the engine.
[`docs/design/call-graph-projection.md`](../../docs/design/call-graph-projection.md)
makes that split on purpose: the projection owns identity, direction, cycles,
and boundaries, and each front end spells them for itself.

## Unsupported

Each remaining gap is a missing public query that takes an `AssemblyContextGroup`
(or a group participant) and owns its own session, the way the six supported
queries do. Each export keeps the signature the browser bridge binds and throws a
`NotSupportedException` naming the gap, so the site reports the engine's refusal
rather than fixture results or success-shaped empty output.

| Unsupported export | Missing query |
| --- | --- |
| `QueryMemberSource`, `QueryTypeSource`, `QueryTypeMemberSource` | SourceLink and decompiled whole-member source over a group participant, plus symbol acquisition that yields group participants |
| `QueryMemberFacts` | method-scoped Analysis evidence over a group participant |
| `QueryPackageMetadata`, `QueryPackageMetadataTable`, `QueryPackageHeapEntries` | metadata image, table, and heap projections over a group (`MetadataImageQuery` binds to a host-opened session today) |
| `QueryPackageDependencies` | direct assembly references over a group (`AssemblyReferencesQuery` binds to a host-opened session today), plus a declared-dependency-group projection |
| `QueryPackagePerformance` | assembly-wide Analysis ranking over a group |
| every `QueryPlatform*`, `ExpandPlatformCallGraph`, `LoadRuntimePack`, `LoadRuntimePackAssembly` | runtime-pack acquisition that produces participants from content |

Two further gaps are about acquisition rather than inspection:

- `ResolvedAssemblyReference.CreateFromPathIfManaged` has **no content-shaped
  sibling**, so a filesystem-free acquisition owner must decode assembly identity
  itself before it can mint a participant the group will accept.
- The product's listed-version owners resolve NuGet.config and the on-disk
  content cache before answering, so a browser cannot use them; this engine reads
  the flat-container index for the product-owned nuget.org base address.

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

The .NET 11 preview Emscripten wrapper currently mishandles an SDK packs path
that contains whitespace. If that applies to the local SDK installation, pass
`EmscriptenSdkToolsPath` pointing to a no-whitespace link to the installed
Emscripten `tools` directory.

## Test

```bash
cd prototypes/inspect-web
npm test
```

`test/annotated-source-view.test.js` gates the annotated view helper against the
shared sample document: medium filtering, fact selection across media, multi-span
highlighting, unanchored facts, offset-to-node selection, and the refusal of an
invalid document.

There is no browser host test project, so the `[JSExport]` surface itself is
gated by compilation plus the banned-symbol rule. Four product test classes gate
the paths this engine depends on:

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

`BrowserEngineLayeringTests` in `src/dotnet-inspect.Tests` gates the layering
rule described above.

Pull requests that change the browser prototype, its shared annotated-source
viewer, product dependencies, or repository build inputs run the `inspect-web`
CI job. That job compiles the platform-index generator, publishes the Release
Wasm bundle, and runs both JavaScript suites.
`eng/test-ci-change-detection.cs` gates the path classification, and
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
- The Taste popover and Settings page render their groups, summaries, and
  ordering from `StyleOptionCatalog`; the browser does not restate that taxonomy.

## Deploy

`.github/workflows/deploy-inspect-web.yml` publishes the browser-Wasm project and
uploads the prebuilt `wwwroot` to an Azure Static Web App with Azure's own app
build disabled. It runs on every push to `main`; `workflow_dispatch` remains
available, but the deploy job itself requires `refs/heads/main`, so a manual run
cannot publish another ref. Its deployment credential is the
`AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB` GitHub Actions secret.

Two prerequisites live outside this repository and are **not** verified by
anything in it: the secret must be present, and the Static Web App resource's
production Branch setting must name the branch being deployed. Neither has been
confirmed from this branch, so treat a green workflow run — not this file — as
the evidence that a deployed site is current.

See [architecture-spike.md](architecture-spike.md) for the proposed .NET 11
browser engine and the NativeAOT decision.
