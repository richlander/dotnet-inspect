# Inspect-web JSExport facade partitioning

Status: **implemented** for issue
[#4497](https://github.com/richlander/dotnet-inspect/issues/4497).
The [page-facing engine client](#page-facing-engine-client) is **implemented**
through the single-runtime production cutover:
[`engine-worker-client.ts`](../../inspect-web/src/engine-worker-client.ts)
binds every production managed call to one Worker epoch, and the page runtime
has no generated-facade import or managed-runtime fallback. The remaining
cross-runtime Source lifecycle work tracked by
[#5420](https://github.com/richlander/dotnet-inspect/issues/5420) is feature
lifecycle adoption, not runtime placement.

This is the owning document for the inspect-web production facade partition:
which existing browser-host exports belong together, how independently
generated modules attach to one Browser/Wasm runtime, and how the consumer
proves that the complete partition is deployed. It owns no package, metadata,
Analysis, source, call-graph, vocabulary, or workspace-query semantics.

[`ts-jsexport`](ts-jsexport.md) remains the owner of one rooted assembly to one
generated TypeScript source module and of compiler-declared context
orchestration across those independent modules. The
[inspection layers](inspection-layers.md) and their focused product documents
remain the owners of the typed operations and results that inspect-web adapts.
The [inspect-web README](../../inspect-web/README.md) owns the
implemented browser build and deployment procedure.

## Decision

Inspect-web replaces its former single `InspectWeb.Engine.dll` export surface
with seven independently generated facade modules:

| Facade | Managed assembly | Context artifact | Checked-in source | Responsibility |
| --- | --- | --- | --- | --- |
| `inspect-web-host` | `DotnetInspect.Web` | `DotnetInspect.Web.ts` | `DotnetInspect.Web/facades/inspect-web-host.ts` | Browser/Wasm lifecycle, host configuration, and build identity |
| `inspect-web-package` | `DotnetInspect.Web.Interop.Package` | `DotnetInspect.Web.Interop.Package.ts` | `DotnetInspect.Web/facades/inspect-web-package.ts` | Package and platform acquisition, package queries, and package content |
| `inspect-web-metadata` | `DotnetInspect.Web.Interop.Metadata` | `DotnetInspect.Web.Interop.Metadata.ts` | `DotnetInspect.Web/facades/inspect-web-metadata.ts` | API and metadata projection |
| `inspect-web-analysis` | `DotnetInspect.Web.Interop.Analysis` | `DotnetInspect.Web.Interop.Analysis.ts` | `DotnetInspect.Web/facades/inspect-web-analysis.ts` | Analysis, integration, opportunity, and performance results |
| `inspect-web-source` | `DotnetInspect.Web.Interop.Source` | `DotnetInspect.Web.Interop.Source.ts` | `DotnetInspect.Web/facades/inspect-web-source.ts` | Source and annotated-source projection |
| `inspect-web-call-graph` | `DotnetInspect.Web.Interop.CallGraph` | `DotnetInspect.Web.Interop.CallGraph.ts` | `DotnetInspect.Web/facades/inspect-web-call-graph.ts` | Package and platform call-graph expansion |
| `inspect-web-catalog` | `DotnetInspect.Web.Interop.Catalog` | `DotnetInspect.Web.Interop.Catalog.ts` | `DotnetInspect.Web/facades/inspect-web-catalog.ts` | Product vocabulary, home demos, and workspace-share transport |

`DotnetInspect.Web` declares the production set in compiled metadata:

```csharp
using TsJsExport;

[JsExportRoot(typeof(InspectionEngine))]
[JsExportRoot(typeof(PackageExports))]
[JsExportRoot(typeof(MetadataExports))]
[JsExportRoot(typeof(AnalysisExports))]
[JsExportRoot(typeof(SourceExports))]
[JsExportRoot(typeof(CallGraphExports))]
[JsExportRoot(typeof(CatalogExports))]
internal sealed class InspectWebJsExportContext;
```

The `JsExportRoot` declaration and the generator's context mode are one
mechanism. The attributes compile the closed facade recipe into CLR metadata;
`--context` names the exact recipe type for `ts-jsexport` to execute. Context
mode is not a TypeScript feature and adds no context concept to the emitted
modules.

Each type is an assembly anchor under the generator-owned root meaning; it does
not filter that assembly's export surface. The host already references every
capability export assembly, so the context adds no reverse or sibling
dependency. Attribute order is explanatory only.

Each module is generated from a different managed export assembly. The
Browser/Wasm application remains one host with one SDK runtime. A
consumer-owned coordinator calls the host facade's generated `createRuntime()`
exactly once, passes that same narrow runtime handle to every facade, and
initializes them serially. Only the host facade's generated `runEntryPoint()`
is used. Correctness does not depend on whether the selected SDK runtime
memoizes repeated creation.

The managed project graph has three roles:

```text
DotnetInspect.Web
  executable host and host exports;
  references every capability export assembly
        |
        +-- DotnetInspect.Web.Interop.Package
        +-- DotnetInspect.Web.Interop.Metadata
        +-- DotnetInspect.Web.Interop.Analysis
        +-- DotnetInspect.Web.Interop.Source
        +-- DotnetInspect.Web.Interop.CallGraph
        `-- DotnetInspect.Web.Interop.Catalog
                         |
                         v
              DotnetInspect.Web.Core
       shared workspace and host services;
             contains no [JSExport]
                     |
                     v
      owner-issued DotnetInspector.* and ILInspector.* APIs
```

`DotnetInspect.Web` remains the executable Browser/Wasm host, static-web asset
owner, and assembly read by `BuildIdentity`. `DotnetInspect.Web.Core` is a
non-exported implementation dependency for shared package/platform workspaces,
operation coordinators, host policy, and typed internal projections. Export
assemblies may reference `DotnetInspect.Web.Core` and the product projects
needed by their own capability. They do not reference sibling export
assemblies.

This choice preserves the implemented `ts-jsexport` contract. Generator-side
selection would require a new rule for selecting exports and pruning the
assembly-wide serializer vocabulary. That would be a normative change to the
generator owner as well as this consumer owner. The compiled context declares
the closed assembly set while every root retains the existing
one-assembly/one-module rule. Explicit search locations resolve only those
declared roots and never add a facade. Shared-runtime composition remains
gated by `eng/test-inspect-web-multi-facade-canary.sh`.

## Mock demo

After the seven assemblies build, one context invocation generates the complete
source set into a fresh scratch directory:

```text
ts-jsexport DotnetInspect.Web.dll
  --context DotnetInspect.Web.InspectWebJsExportContext
  --assembly-search-path <browser-output>
  --runtime-module ./runtime-loader.js
  --output <fresh-scratch>/facades

<fresh-scratch>/facades/
  DotnetInspect.Web.ts
  DotnetInspect.Web.Interop.Analysis.ts
  DotnetInspect.Web.Interop.CallGraph.ts
  DotnetInspect.Web.Interop.Catalog.ts
  DotnetInspect.Web.Interop.Metadata.ts
  DotnetInspect.Web.Interop.Package.ts
  DotnetInspect.Web.Interop.Source.ts
```

What to notice: the compiler-bound context, not a directory scan or handwritten
facade manifest, determines all seven outputs. Generation fails as one operation
before the destination exists if any declared root cannot resolve or emit. As a
neighboring case, `DotnetInspect.Web.Core.dll` may be present in the same search
directory but produces no facade because the context does not root it.

## Boundaries

### This owner is responsible for

- assigning every inspect-web `[JSExport]` operation to exactly one production
  facade;
- keeping each facade's managed exports and wire DTO closure cohesive;
- composing all generated modules over one browser runtime and one entry point;
- preserving visible failure when any required module cannot initialize;
- checking in, compiling, linting, publishing, and drift-checking every
  generated module and declaration; and
- certifying the complete facade set in both compiler-async and runtime-async
  Browser/Wasm deployments.

### Adjacent owners remain responsible for

- `ts-jsexport`: assembly inspection, authenticated runtime dispatch, wire
  contract projection, generated lifecycle behavior, and TypeScript emission;
- `DotnetInspect.Web.Core`: consumer-owned acquisition, workspace lifetimes,
  cancellation coordinators, and browser host policy;
- product queries and producers: package, metadata, Analysis, source,
  call-graph, and vocabulary semantics and typed results;
- the inspect-web UI: presentation, navigation, request authority, and visible
  error handling; and
- the worker protocol and managed-operation bridge: worker epochs, dynamic
  operation admission, progress callbacks, and cancellation messages.

### Non-claims

This partition does not:

- add export filtering, redefine context-root identity, or treat assembly search
  locations as facade membership;
- change any managed operation's parameters, JSON wire shape, failure
  semantics, query behavior, or progressive-disclosure policy;
- expose raw `ILInspector` or `DotnetInspector` objects to TypeScript;
- introduce lazy module loading, multiple runtimes, or a network protocol;
- define Worker hosting or lifecycle; the proposed client consumes the
  [Worker runtime](inspect-web-worker-runtime.md);
- make module names into product-layer authorities; or
- claim that managed project boundaries and product architecture layers are
  identical.

The export assemblies are L3 browser adapters. Their names describe the
capability they adapt, not ownership of the underlying product facts.

## Production surface inventory

The seven rooted export assemblies contain 57 `[JSExport]` methods.
The generated `initializeRuntime()` and `runEntryPoint()` functions are
generator-owned infrastructure and are not part of that count.

The inventory below is exhaustive. The compiled
`InspectWebJsExportContext` is the implementation source of truth for its
assembly membership.
`ProductionFacadeContext_DeclaresExactAssemblySet` gates equality between the
compiled root set and the seven managed assembly identities above.
`ProductionFacadePartition_AssignsEveryJsExportExactlyOnce` derives the actual
export set from those rooted assemblies and fails for an omitted, duplicated,
or unexpected assignment.

### Host facade: 3 exports

- `AsyncLoweringCanary`
- `BuildIdentity`
- `ConfigureHost`

The host assembly is the only facade whose `runEntryPoint()` the application
calls. `ConfigureHost` configures shared `DotnetInspect.Web.Core` policy before the
entry point starts application work. `AsyncLoweringCanary` remains the
deployment smoke's deterministic awaited operation.

### Package facade: 22 exports

- `ActivateWorkspacePackageOccurrence`
- `CancelPackageQuery`
- `ClearWorkspacePackageOccurrences`
- `GetPackageDocument`
- `ListGalleryDiscoveryCatalog`
- `ListPackageAssemblyQueryPatterns`
- `ListPackageQueryFacets`
- `LoadRuntimePack`
- `LoadRuntimePackAssembly`
- `MatchPackageDependencyCoordinate`
- `OpenPackageAssemblyQueryResult`
- `PackageCacheStats`
- `QueryMemberDocumentation`
- `QueryPackage`
- `QueryPackageDependencies`
- `QueryPackageVersions`
- `QueryWorkspacePackageOccurrences`
- `RequestPackageQueryMatches`
- `ResolvePackageDependencyVersion`
- `RunPackageAssemblyQuery`
- `RunPackageQuery`
- `SearchTypes`

This facade owns browser adaptation for package and platform acquisition,
ordered workspace package occurrences and their opaque activation actions,
package-query streaming, package-shipped documents, package dependency
coordinates, and the API surface initially loaded for a package or platform.
`SearchTypes` stays here because it ranks candidates from that loaded package
surface without opening another artifact. It does not transfer type-matching
semantics from the product query owner.

### Metadata facade: 8 exports

- `QueryGraphMemberSurface`
- `QueryPackageHeapEntries`
- `QueryPackageMetadata`
- `QueryPackageMetadataTable`
- `QueryPlatformHeapEntries`
- `QueryPlatformMetadata`
- `QueryPlatformMetadataTable`
- `QueryTypeProjection`

This facade adapts metadata images, tables, heaps, type projections, and the
member surface selected from graph navigation. It consumes package or platform
coordinates through `DotnetInspect.Web.Core`; it does not acquire artifacts
independently.

### Analysis facade: 8 exports

- `QueryCloneCandidates`
- `QueryMemberFacts`
- `QueryPackageIntegrations`
- `QueryPackageOpportunities`
- `QueryPackagePerformance`
- `QueryPlatformIntegrations`
- `QueryPlatformOpportunities`
- `QueryPlatformPerformance`

The explicitly unavailable platform-performance operation stays in this facade
so absence remains a visible capability result rather than a missing binding.
`QueryCloneCandidates` belongs here because it adapts the Workspace structural
Clone query and portable Presentation result without transferring candidate
ranking or coverage semantics into the browser host.
The module does not combine Analysis with call-graph topology; graph traversal
has its own facade and product owner.

### Source facade: 9 exports

- `CancelMethodBodyComparison`
- `CancelSourceQuery`
- `CancelTypeSourceQuery`
- `QueryMethodBodyComparison`
- `QueryMethodBodyComparisonTargets`
- `QueryMemberAnnotatedSource`
- `QueryMemberSource`
- `QueryTypeMemberSource`
- `QueryTypeSource`

The source cancellation coordinator remains shared consumer infrastructure, but
its public cancellation operations belong beside the work they cancel.
Annotated source stays with source because the returned document and its
viewer contract are the capability being requested; Analysis facts embedded in
that product document do not transfer ownership to this adapter.
Method-body comparison likewise projects native C#/IL evidence through the
shared query; its target inventory and keyed cancellation stay with that
managed feature in the same facade even though its contextual dialog is
retired.

### Call-graph facade: 2 exports

- `ExpandPlatformCallGraph`
- `QueryMemberCallGraph`

Both package and platform traversal return the same browser call-graph
contract. Graph-target member projection remains in the metadata facade because
it projects one API member after navigation rather than expanding topology.

### Catalog facade: 6 exports

- `DecodeWorkspaceShareState`
- `EncodeWorkspaceShareState`
- `ListHomeDemos`
- `ListVocabulary`
- `ResolveHomeDemo`
- `RunHomeDemo`

The catalog facade adapts product-owned static vocabulary and demo definitions
plus product-owned workspace-share transport. `RunHomeDemo` may call shared
package or Platform workspace services through `DotnetInspect.Web.Core`; it
does not call sibling facades or reuse their wire DTOs.

## Managed assembly contract

### Export isolation

Every `[JSExport]` method lives in exactly one export assembly.
`DotnetInspect.Web` owns the three host exports.
`DotnetInspect.Web.Core` contains no `[JSExport]` attribute, no generated
serializer context published as a facade contract, and no dependency on an
export assembly. Capability export assemblies do not reference each other.
These constraints keep the dependency graph acyclic and prevent one generated
facade from becoming a hidden aggregate of sibling operations.

`ProductionFacadeProjects_HaveAcyclicOwnerReferences` gates the project graph
and `ProductionFacadePartition_AssignsEveryJsExportExactlyOnce` gates the
export graph.

### Wire DTO ownership

Each export assembly owns:

1. the browser DTOs returned or accepted by its exports;
2. the source-generated `JsonSerializerContext` roots for those DTOs; and
3. projection from owner-issued product results into those DTOs.

An export does not serialize a DTO declared by `DotnetInspect.Web.Core` or a
sibling export assembly. Internal core models may be shared, but each facade
maps them to its own transport records. This keeps every assembly's
authenticated serializer vocabulary self-contained and avoids adding
referenced-assembly type resolution to the generator contract.

Some TypeScript declarations will be structurally equal across modules, such as
compile-library availability or package coordinates. They remain separate
module-local declarations. The TypeScript consumer may map them into an
application-owned model but may not establish equality by importing one
facade's DTO as another facade's owner.

`ProductionFacadeWireContexts_AreAssemblyLocal` gates that every JSON wire type
reached by an export is declared and source-generated in the same export
assembly. Existing serializer-to-completion authentication in
`ILInspector.JsExportSurface` continues to gate the wire claim itself.

### Shared implementation services

Shared browser state is not duplicated to match facade count. Package caches,
package/platform workspace acquisition, host proxy configuration, operation
coordinators, scope leases, and bounded browser policies have one
`DotnetInspect.Web.Core` implementation in the one runtime.

A helper moves to core only when at least two facade assemblies consume the
same typed browser-host operation. Product facts and classifications stay in
their product owners. Facade-specific formatting or DTO projection stays in
the facade that publishes it.

## Browser composition

All generated facades use the exact same runtime module specifier:

```text
./runtime-loader.js
```

Publication materializes stable `dotnet.js`, `dotnet.native.js`, and
`dotnet.runtime.js` modules as exact copies of the SDK's import-map-selected
fingerprinted modules. The loader imports the stable `dotnet.js`, so the
Worker and the SDK's internal dynamic imports use one module identity without
depending on the document import map. The coordinator is shared by the
implemented page host and the separate Worker diagnostic host; sharing its
source does not share a runtime between realms.

The consumer owns one coordinator:

```ts
import * as host from "/inspect-web-host.js";
import * as packageApi from "/inspect-web-package.js";
import * as metadata from "/inspect-web-metadata.js";
import * as analysis from "/inspect-web-analysis.js";
import * as source from "/inspect-web-source.js";
import * as callGraph from "/inspect-web-call-graph.js";
import * as catalog from "/inspect-web-catalog.js";

let readiness: Promise<void> | undefined;

async function initializeCore(): Promise<void> {
  const runtime = host.createRuntime();
  await host.initializeRuntime(runtime);
  await packageApi.initializeRuntime(runtime);
  await metadata.initializeRuntime(runtime);
  await analysis.initializeRuntime(runtime);
  await source.initializeRuntime(runtime);
  await callGraph.initializeRuntime(runtime);
  await catalog.initializeRuntime(runtime);
}

export function initializeFacades(): Promise<void> {
  readiness ??= initializeCore();
  return readiness;
}
```

The real coordinator also retains the first initialization failure so later
callers observe the same failure. The generated `JsExportRuntime` handle
exposes only the two SDK capabilities required by generated facades; the
coordinator neither returns that handle nor exposes a raw managed export
object.

Startup remains eager and ordered:

```ts
await initializeFacades();
host.configureHost(window.location.origin);
await host.runEntryPoint();

const packageSurface = await packageApi.queryPackage(
  "System.Text.Json",
  "10.0.0",
  "net10.0",
);
const metadataImage = await metadata.queryPackageMetadata(
  "System.Text.Json",
  "10.0.0",
  "net10.0",
);
```

The second call demonstrates a neighboring module over the same package
coordinate. It does not create another runtime, rerun the entry point, or route
metadata through the package facade.

No application operation is published as ready until all seven generated
facades initialize. A missing assembly export root, stale module, or failed
runtime acquisition rejects the shared readiness promise. The implementation
does not fall back to the monolithic module or expose a partially initialized
application.

The existing multi-facade canary gates the explicit composition behavior on
both shipped Browser/Wasm runtimes:

- the coordinator calls `createRuntime()` exactly once;
- every generated facade receives that same runtime promise;
- independently generated modules retain assembly-specific dispatch; and
- wrong roots, duplicate runtime modules, cross-routing, skipped initialization,
  and dropped managed invocation fail the gate.

The production gate adds all seven real assemblies and their actual operations;
the fixture canary remains because its intentionally colliding identities are a
stronger close negative than the production names.

## Page-facing engine client

This extension owns **consumer binding to the generated facade set
through one asynchronous client**. The production consumer is Inspect Web,
with Type Source as the first fully composed Worker feature in
[#5420](https://github.com/richlander/dotnet-inspect/issues/5420). It retains the
one-runtime and shared-core contracts above; it does not change the generated
facade ABI or create a new logical-operation or Worker-protocol owner.

### Composition contract

After cutover, the application's managed runtime resides in its dedicated
Worker. Every capability uses that runtime, including calls that have not yet
adopted the managed-operation bridge. Moving only Source while keeping a page
runtime for its neighbors would split the shared workspace and source budget
and is not a supported intermediate production state.

The page-facing client exposes consumer-required capabilities grouped by their
owning facade, not arbitrary managed member lookup or a new generated monolith.
Its bindings consume the generated parameter and result types; generated
functions still own managed names, overload selection, serialization, and
runtime dispatch. Bootstrap retains host configuration and entry-point
execution. The existing canary remains a diagnostic, not a UI capability.

Managed results and acknowledgments cross the client asynchronously, including
results currently returned synchronously by generated functions. A fulfilled
ordinary query preserves its generated result; managed rejection or Worker
boundary failure remains visible through the caller's error path. Posting a
command is not an acknowledgment of its effect. The client must allow controls
to be dispatched while a query awaits them rather than queueing every call
behind that query's completion.

The consumer retains one complete readiness barrier: all facades initialize
before managed dispatch is ready. Concurrent readiness callers share the
attempt and its failure. A failed Worker bootstrap remains failure; it does not
start a replacement runtime on the page. Static catalog results may be
materialized as page data after readiness. Mutable lookups and actions remain
asynchronous operations, not synchronous reads of an implicitly stale cache.

An operation already governed by
[operation authority](inspect-web-operation-authority.md) consumes a
Worker producer adapter with the same authority-issued identity. It is not
wrapped in another logical operation just to obtain a Promise-shaped facade.
Logical cancellation and the separate terminal/quiescence handoffs retain
their existing owner contracts. Ordinary Promise results do not acquire a new
claim of managed quiescence merely by crossing this client.

Callback-bearing calls need an explicit feature adapter, not a structured clone
of a page callback or `JSObject`. The adapter consumes the Worker owner's
validated event path and the feature's generated payloads. Package Query's
durable matches and failures require
[#5418's durable-event residual](https://github.com/richlander/dotnet-inspect/issues/5418)
before cutover; sending them as advisory progress would change their contract.
Its existing credit acknowledgment remains a real asynchronous result.

Worker-issued epoch identity and operation-authority identity keep their
separate meanings. A client bound to a closed epoch cannot silently send old
actions to a replacement epoch. The initial consumer recovery policy is a
visible page reload, not automatic in-place restoration of managed state.
The Worker owner's restart mechanism remains available to its own callers and
gates; adopting in-place application recovery needs a later consumer contract.
This avoids inventing a cache rehydration or action-rebinding protocol here.

UI owners decide how to await results while preserving navigation, user
activation, focus, and current-view publication. The client does not reproduce
managed codecs or ranking in JavaScript. Typed results still reach their
existing rendering owners; this extension adds no rendering or format-lowering
domain.

### Implemented production composition

The production composition root imports only
[`engine-worker-client.ts`](../../inspect-web/src/engine-worker-client.ts).
`createProductionEngineWorkerClient` creates one Worker host and binds startup,
ordinary, Type Source, and Package Query surfaces to its initial epoch. The
retained `buildIdentity` request is the complete page readiness barrier and is
also the result returned by the first `host.buildIdentity()` call. Worker
startup, protocol, and lifecycle failures remain visible through the existing
load-error path; production neither creates a page runtime nor retries by
directly importing a generated facade.

The Worker entry starts the shared managed runtime and installs the six
capability facade groups before readiness. Five startup reads retain their
closed specialized operations. Type Source and Package Query retain their
operation-authority adapters, exact caller-issued operation IDs, keyed
cancellation, and terminal/quiescence contracts. Package Query durable events
return through its generated event sink, and match credit counts only after the
exact asynchronous Worker and managed acknowledgment.

All other managed calls use the closed ordinary-operation catalog in
[`engine-worker-ordinary.ts`](../../inspect-web/src/engine-worker-ordinary.ts).
Its 49 entries are named at build time across Package (19), Metadata (8),
Analysis (7), Source (9), Call Graph (2), and Catalog (4). Callers cannot send a
module, facade, or member name. Arguments and results cross as inert JSON trees
only, bounded to 8,388,608 characters, 64 nesting levels, and 262,144
collection entries. The production projection for the immutable
`System.Text.Json@10.0.0/net10.0` package measures 3,843,729 JSON characters and
137,151 collection entries; both exceed the former 1,048,576-character and
65,536-entry bounds. The larger finite envelope admits that complete ordinary
package surface with approximately twice its observed capacity in each
dimension, and the published package-adoption gate acquires the same coordinate
through the normal product path as its durable pathological case. The reader
rejects accessors, symbols, prototype drift, sparse or extended arrays, cycles,
functions, `undefined`, and non-finite numbers rather than converting malformed
data into an empty or partial result.

Page consumers await formerly synchronous managed behavior. Workspace packet
encoding and decoding, demo resolution, Spotlight ranking, package-cache
statistics, application-scope selection, Workspace occurrence clearing, saved
Workspace capture and restoration, and navigation publication all retain their
own stale-result and transaction authority. Occurrence clear is a barrier for
following occurrence queries and activation. Initial Workspace publication
commits only after successful canonical URL encoding and ignores stale
navigation completion.

Share-copy preserves transient user activation by passing a Promise-backed
`Blob` to `ClipboardItem` before awaiting Worker packet encoding. A browser
without that capability receives a visible unsupported error; the page does
not make an unreliable post-`await` clipboard attempt. This is a page
interaction constraint, not a codec or Worker-protocol contract.

A client remains bound to the epoch that created it. Restart or disposal
rejects its held and active work and cannot retarget old controls or ordinary
calls into a replacement runtime. Production recovery remains reload-only.
This cutover does not claim in-place Workspace rehydration, physical
quiescence for ordinary Promise calls, or feature-specific lifecycle
completion beyond the adapters named above.

### Call-site migration inventory

This is migration evidence at `48d5436a2`, not a second export specification.
The [production inventory](#production-surface-inventory) and generated
declarations remain authoritative. Of its 50 managed exports, 48 are bound by
`loadEngineModule` in
[`dotnet-inspect.ts`](../../inspect-web/src/dotnet-inspect.ts).
Generated lifecycle functions are not included in these counts.

Website Gallery adoption (#6019) subsequently adds the synchronous startup
operation `listGalleryDiscoveryCatalog`. It participates in the same typed
catalog handoff; the historical counts in this migration snapshot exclude it.

| Current call class | Count | Generated operations | Consumer handoff to prepare |
| --- | ---: | --- | --- |
| Bootstrap/diagnostic only | 2 | `configureHost`, `asyncLoweringCanary` | Retain bootstrap/diagnostic ownership; do not expose a blanket facade proxy. |
| Synchronous startup data | 4 | `buildIdentity`, `listVocabulary`, `listHomeDemos`, `listPackageQueryFacets` | Await acquisition; supply typed catalog data to existing readers. |
| Synchronous computed results | 5 | `decodeWorkspaceShareState`, `encodeWorkspaceShareState`, `resolveHomeDemo`, `matchPackageDependencyCoordinate`, `searchTypes` | Await the real result in navigation/share, demo resolution, dependency matching, and Spotlight owners. |
| Synchronous stateful operations | 3 | `activateWorkspacePackageOccurrence`, `clearWorkspacePackageOccurrences`, `packageCacheStats` | Await activation/clear completion or a current stats result; retain the UI owner's ordering and invalidation. |
| Synchronous controls | 4 | `cancelPackageQuery`, `cancelSourceQuery`, `cancelTypeSourceQuery`, `requestPackageQueryMatches` | Preserve exact operation targeting and real acknowledgment; Package Query and Type Source are already keyed, while remaining singleton controls require focused adoption. Logical cancellation stays with its existing feature/operation authority. |
| Callback stream | 1 | `runPackageQuery` | Worker-local callback adapter, durable delivery, terminal ordering, and existing match-credit behavior. |
| Authority-governed Source | 1 | `queryTypeSource` | Direct Worker producer adapter; consume the existing keyed managed bridge and generated terminal DTO. |
| Other Promise-returning calls | 30 | Package: 9; Metadata: 8; Analysis: 7; Source: 3; Call graph: 2; Catalog: `runHomeDemo` | Preserve generated inputs, results, and failures through typed bindings; placement alone does not complete lifecycle adoption. |

The nonterminal, control, and query paths are one migration obligation, not
optional methods to omit from an initial client. In particular,
[`package-query-source.ts`](../../inspect-web/src/package-query-source.ts)
constructs a property-setter callback, and
[`package-query.ts`](../../inspect-web/src/package-query.ts) currently
updates credit only after a synchronous successful grant.
[`workspace-navigation.ts`](../../inspect-web/src/workspace-navigation.ts)
requires synchronous share encode/decode today. These consumers need focused
adoption before the production switch, not type assertions that pretend their
existing synchronous contracts already support a Worker.
In particular, share/copy must not assume that transient user activation
survives an awaited engine call; its navigation owner retains that interaction
constraint.

### Adoption and evidence

The first caller-adoption slice uses
[`engine-client.ts`](../../inspect-web/src/engine-client.ts) for
Promise-valued build identity, vocabulary, home demo, Package Query facet, and
Gallery discovery reads. Its three facade groups retain generated types;
`engine-facades.ts` still owns the existing single page runtime and readiness.
The application awaits each read in its existing startup error boundary:
build identity remains fatal, vocabulary and home demo failures remain
independent, and the two Package Query catalogs retain their shared failure
path. This is asynchronous caller preparation, not Worker execution or a
responsiveness claim.

`test/engine-client.test.ts` exercises deferred invocation, exact result and
failure forwarding, and independent neighboring reads;
`test/engine-facades.test.ts` retains the existing readiness/one-runtime
composition evidence. Both use the existing inspect-web Node test runner, and
the TypeScript gate checks the application's awaited DTO use.
Home-demo resolution also uses the catalog group. Its caller establishes the
existing navigation sequence before awaiting resolution, then carries that
sequence into workspace restoration or call-graph execution. A superseded
resolution cannot publish success or failure over newer navigation.
Source location, retry policy, and focus remain with the existing transactional
navigation path. `test/saved-workspace-navigation.test.ts` exercises that
production caller with delayed success/failure, a newer saved-workspace open,
and the call-graph handoff. Other computed callers, mutable/control calls,
and Worker activation remain outstanding.

The first Worker-only client slice binds exactly five startup reads:
`buildIdentity`, `listVocabulary`, `listHomeDemos`, `listPackageQueryFacets`,
and `listGalleryDiscoveryCatalog`. It consumes the corresponding `EngineClient`
subset in the separately published Worker client entry, not the production
page bootstrap. All calls share the existing full-facade and managed-reporter
readiness barrier. Concurrent calls, including repeated calls to one method,
have independent operation sessions and cannot supersede each other. An
individual managed rejection does not fail neighboring reads. Disposal and
epoch loss reject outstanding reads; a client cannot follow a replacement
epoch. Promise completion does not assert physical quiescence.

Each read has a closed Worker operation and consumes the generated function
and result type. Its JSON-representable result uses a transport JSON string,
limited to 1,048,576 UTF-16 code units before parsing and checked against the
generated DTO shape. This deliberate transport encoding bounds decoding by
text length without adding a recursive object-budget framework; it does not
replace generated managed serialization. Extra JSON properties and the
vocabulary's generated `unknown` values are preserved. Oversized results,
invalid shapes, managed exceptions, and Worker failures remain visible.

`test/engine-worker-startup.test.ts` gates generated-shaped result forwarding,
shared readiness, independent calls, rejection, disposal, and epoch binding
using the actual host, realm, and operation authority. The existing published
Worker gate compares all five client results with the actual generated facade
results in the same Worker. This is the first portion of milestone 4 below,
not production activation, Source adoption, or managed-work responsiveness.

Dependency-coordinate matching uses the package group without moving NuGet
selection into JavaScript. Dependency lists await results before enabling
open/load actions; graph construction awaits the same matcher before Mermaid
lowering; dependency navigation establishes its existing sequence before
matching or acquiring a package. List publication is tied to its current
container, framework selection, and coordinate snapshot. Graph requests use
their existing sequence from before matching and deduplicate the captured
input while pending, so duplicate render requests cannot cancel their own
in-flight diagram. Failure remains visible, including when an older graph is
retained during replacement. These are page-owned consumer mechanics, not a
new managed operation or persistent match cache.
`test/dependency-matching.test.ts` executes the production functions with
delayed results, replacement views, generated match outcomes, and failures;
it also covers awaited caller edges and the existing graph node bound.
The existing dependency-graph browser harness covers the async builder's
viewer integration. Its fixture remains a browser rendering gate, not evidence
of Worker execution.

The user-approved
[five-milestone plan](https://github.com/richlander/dotnet-inspect/issues/5420#issuecomment-5549528380)
is the production-host adoption and retirement path under #5418 and #5420:

1. Lock this consumer contract and migration inventory.
2. Prepare asynchronous consumer calls in focused slices, retaining the
   existing single page runtime.
3. Consume the separately owned durable Worker event prerequisite in #5418.
4. Prepare typed Worker bindings and the Source producer adapter in a
   Worker-only host, without activating them alongside the production runtime
   (**implemented**).
5. Switch production bootstrap and all required bindings together, retire the
   temporary page client/direct managed calls, and complete the Source demo
   (**implemented**).

Steps 2 and 3 may proceed independently under their owners. Step 5 waits for
all required paths; it includes the production demonstration rather than
deferring correctness to a later slice. This uses the approved inspect-web-only
Worker scope; it is not a CLI runtime migration. Feature-specific lifecycle
adoption may continue after placement, but direct page managed dispatch does
not.

The Type Source portion of milestone 4 registers the generated Source facade
in the Worker catalog and exposes its operation-authority-compatible producer
adapter from the page-facing Worker entry. The adapter projects the six
clone-safe Source fields, validates bounded Source and terminal DTOs, forwards
keyed cancellation, preserves expected versus unexpected failure, rejects
progress, and declares unbounded liveness.
`test/engine-worker-source.test.ts`, included in
`inspect-web-worker-protocol`, exercises the real host, realm, catalog, and
operation-authority path plus malformed boundary data. The published Firefox
Worker gate additionally returns decompiled Source from a deterministic local
package through the generated facade. The production `source-inspection.ts`
adapter and `dotnet-inspect.ts` dependencies remain unchanged.

The Package Query portion of milestone 4 composes the already landed durable
event, acknowledged control, and operation-keyed managed boundaries in a
Worker-only adapter. `engine-worker-package-query.ts` projects the existing
`QueryRequest` into one closed prefix-or-assembly input while preserving the
page authority's operation ID for Worker correlation and generated managed
admission. It rejects a callback `Completed` event, publishes the four
nonterminal generated event kinds as ordered durable events, and accepts
completion only from the versioned managed terminal result. Expected and
unexpected failures retain their classification and diagnostic; cancellation
retains its authoritative reason.

Callback rejection remains a managed bridge boundary failure. The generated
Promise rejects only after managed release, and the Worker runtime therefore
enters unexpected epoch draining rather than manufacturing a Package Query
terminal result. Operation-local containment applies to an invalid fulfilled
managed terminal DTO, whose validation maps it to an unexpected feature
failure while the realm remains usable.

The same adapter routes cancellation to the exact generated operation ID.
Positive match credit uses the Worker control channel and becomes granted only
after `Granted` returns the exact requested amount and the correlated Worker
acknowledgment reaches the page. `NotActive` remains `not-active`; malformed or
mismatched managed responses fail visibly. Settlement closes later control
admission while the Worker runtime retains the response obligation for a
control posted before settlement.

Request and event payloads allow at most 1,048,576 UTF-16 code units and 4,096
total collection entries. Error and diagnostic text allows 65,536 code units.
All DTO readers require closed own data properties and reject accessors. The
Worker bootstrap awaits both the generated Source and Package facades before
advertising readiness. `test/engine-worker-package-query.test.ts`, also in
`inspect-web-worker-protocol`, exercises the actual fake host, realm, catalog,
operation authority, durable-event path, and controlled adapter. Its cases
cover both request forms, exact identity, event order, all terminal mappings,
keyed cancellation, exact and inactive credit, overlap rejection, delayed
credit acknowledgment across settlement, terminal-callback rejection entering
Worker draining, malformed fulfilled results, payload bounds, and continued
realm health after an operation-local result failure.

The production cutover consumes this adapter without changing
`PackageQueryDataSource` generation policy, batching, rendering, the Worker
protocol, the managed bridge, or their TLA+ models. Package Query's production
path had four total steps:

1. Worker operation-addressed controls, completed through #6376 and #6385.
2. Operation-keyed managed controls, completed through #6390 and #6393.
3. The typed Worker adapter with durable events and acknowledged credit,
   completed through the Worker-only preparation slices.
4. Atomic activation of the single Worker runtime, retirement of direct page
   managed dispatch, and the #5816 responsiveness evidence, completed by
   [#6435](https://github.com/richlander/dotnet-inspect/issues/6435).

Milestone 5 supplies production bootstrap, all required neighboring bindings,
direct page-runtime retirement, and real-browser Source and Package Query
evidence. The published Worker runtime gate exercises startup reads, Type
Source, lifecycle loss, and visible bootstrap failure through the actual
client. The package-adoption gate now boots the same production client for
ordinary Package and Analysis calls and observes exactly one Worker. Its
production website scenario records a useful Package Query row, page input, a
two-frame render opportunity, render activity, and bounded timer delay before
the deliberately held query completes. The separate managed CPU isolation
gate retains the stronger compute-bound proof.

Worker protocol/lifecycle and durable ordering remain covered by their owner's
gates; managed lifetime remains covered by #5419. Consumer gates preserve
existing feature outcomes across the asynchronous boundary. Feature-specific
Source lifecycle evidence under #5420 may still distinguish logical
cancellation from physical release with a browser-native neighboring producer;
that remaining work does not reopen the single-runtime placement decision.
Existing models remain evidence for their owned components rather than proof
of consumer bindings.

### Comparative basis and mock demo

[Comlink](https://github.com/GoogleChromeLabs/comlink#api) demonstrates the
conventional asynchronous Worker-call boundary: even a synchronous remote
function produces a Promise, and callbacks need special handling rather than
ordinary cloning. Inspect Web deliberately uses its existing closed Worker
catalog and explicit feature adapters instead of adopting a general proxy
system. The [official .NET Worker evidence](inspect-web-worker-runtime.md#runtime-evidence)
supports runtime placement, not inspect-web's publication or lifetime policy.

Before, calling an asynchronous Source export still runs its managed work on
the page's runtime. The proposed composition is:

```text
page: existing Source view and operation authority
  -> typed Worker producer adapter, same operation identity
  -> owning generated Source facade
  -> one Worker runtime and shared managed workspace/source budget

neighbor: package and metadata queries -> that same Worker runtime
neighbor: browser-native fetch -> operation authority, without managed dispatch
failure: Worker bootstrap rejects -> visible failure, not a page runtime
```

This is a design mockup, not a responsiveness result. Source rendering and
focus remain with the existing view.

## TypeScript ownership

Each checked-in TypeScript source is a byte-identical copy of one canonical
context artifact and is the authoritative handoff for one managed export
assembly. Consumer-owned TypeScript compilation derives one `.d.ts` and one
browser JavaScript module from it:

```text
DotnetInspect.Web.Interop.Package.dll
        |
        v
DotnetInspect.Web/facades/inspect-web-package.ts
        |
        +-- src/facades/inspect-web-package.d.ts
        `-- DotnetInspect.Web/wwwroot/inspect-web-package.js
```

The generation command executes the compiled `JsExportRoot` recipe once using
one `ts-jsexport` binary, one runtime-module option, and explicit search
locations for the built assemblies. This execution path is the tool's context
mode. It emits all seven canonical artifacts into a destination that does not
exist. The consumer's exact table above maps each canonical artifact to its
public module and checked-in path without modifying the generated bytes. That
map cannot add or omit membership: its domain must equal the complete context
output set before TypeScript compilation begins.

A single `--check` command regenerates the context into fresh scratch space,
requires exact set equality with the seven canonical artifact names, then
compares every mapped source, declaration, and JavaScript artifact. It fails if
context resolution, generated membership, the consumer map, or any derived
artifact differs.

Application files import DTOs from their owning facade declaration. Runtime
composition imports JavaScript modules only in the coordinator. A small
authored bindings module may adapt generated names to existing application
callback interfaces, but it does not re-export all generated functions as a
new monolithic facade. The proposed page-facing client adapts invocation
placement through the existing Worker boundary; it does not reconstruct the
managed dispatch wrappers that generation owns.

Generated sources, declarations, JavaScript outputs, compiler programs, lint
targets, and generated-file relaxations remain exact inventories. The
toolchain gate fails for an unowned generated artifact or a source admitted
only through a broad directory glob.

## Async deployment contract

Facade partitioning changes the unit of deployment evidence from one assembly
to a closed assembly/module set. It does not change which methods are
compiler-async or runtime-async.

The compiler-async and runtime-async deployment jobs each record:

- the exact seven managed export assembly names and content digests;
- the exact seven generated TypeScript source and declaration digests;
- the exact seven published JavaScript filenames and content digests;
- the exact seven shipped WebCIL assembly names and content digests;
- per-assembly and total `[JSExport]`, compiler-async, and runtime-async counts;
- the sorted repository-relative project identities, their canonical SHA-256
  digest, and their count; and
- successful Browser/Wasm initialization of every facade plus the host canary
  result.

Before writing a receipt, each lane compiles every freshly generated TypeScript
source with the consumer's pinned compiler configuration and requires
byte-for-byte equality with the corresponding published JavaScript module. The
published filename-to-digest map therefore binds the wrappers the browser
imports to the managed assembly and authenticated source used by that lane.

Both jobs derive their expected assembly/module domain from the compiled
context and must report the same assembly names, generated source/declaration
digests, published JavaScript filename/digest map, total export count, sorted
project identities, and project-graph digest. The consumer mapping is accepted
only when its domain equals that context-issued set. The count remains a useful
summary but does not establish graph equality. Their lowering counts remain the
expected all-or-nothing inverse. A receipt for only `DotnetInspect.Web.dll` is
incomplete after partitioning even if its local counts are correct.

CoreCLR staging follows production promotion rather than every compiler-async
staging build. After production deploys successfully, the promotion workflow
calls the CoreCLR workflow with the exact validated product SHA, staging run
ID, and staged artifact ID that it promoted. The CoreCLR build checks out that
SHA and compares against that exact compiler-async artifact; it never resolves
a newer staging run independently. The promotion concurrency group serializes
the production and CoreCLR deployments, and the CoreCLR deployment does not
start before production succeeds.

The deployment smoke initializes every module, which acquires its exact
assembly export root and validates every expected runtime path, then invokes
`host.asyncLoweringCanary()`. It remains independent of network, package-cache,
server-API, and user-data state. Per-assembly lowering censuses prove that each
module was compiled in the expected mode; contract and WebCIL digests prove
that the censused assembly and generated binding are the deployed artifacts,
and compiled-JavaScript equality proves that each published wrapper implements
that binding.

The local production composition gate separately invokes representative real
operations from package, metadata, Analysis, source, call-graph, and catalog
facades using its bounded test inputs. It does not turn deployment
certification into a network integration test.

`InspectWebAsyncDeployment_ReceiptsCoverExactFacadeSet` gates module-set
completeness. `InspectWebAsyncDeployment_LoweringsPreserveFacadeContracts`
gates equal TypeScript contracts, published JavaScript filename/digest maps,
exact project identity and digest equality, and inverse lowering counts across
the paired deployments.

## Failure semantics

Partitioning must not turn failure into absence:

- generation fails when any export assembly or serializer contract is
  unsupported;
- startup fails when any required facade cannot initialize;
- a missing generated artifact fails drift and deployment checks;
- an explicitly unavailable managed capability remains an exported operation
  that rejects with its existing error;
- cancellation remains operation-specific and visible; and
- the UI continues to render operation failures through its existing
  authority and error paths.

The coordinator does not retry initialization with a second runtime, skip a
failed facade, or continue with a compatibility monolith.

## Alternatives

### Filter one assembly into several generated modules

Rejected for this effort. It would require `ts-jsexport` to own selection
syntax, exact export coverage, serializer-vocabulary pruning, shared DTO
declaration policy, and same-assembly multi-module runtime tests. Those are
generator contracts, not inspect-web consumer details. No current consumer
evidence requires that broader feature.

### Keep one generated module behind authored TypeScript barrels

Rejected. Barrels would rearrange imports while retaining one generated
runtime wrapper and one aggregate DTO vocabulary. They would not establish
independent drift, dispatch, ownership, or deployment evidence.

### Hand-write per-layer runtime wrappers

Rejected. It would restore the duplicate binding-name, overload, async, and
wire-type declarations that native facade generation retired.

### Load facade modules lazily

Deferred. Eager initialization preserves current startup and failure behavior.
Lazy loading would add partial-readiness, failure-recovery, and scheduling
contracts that issue #4497 does not need.

## Implementation sequence

The binding cutover is atomic. The current generated module acquires only
`DotnetInspect.Web`, validates all 48 managed paths during initialization, and
supplies the application's declarations and runtime calls. Moving an export
before replacing that module leaves a stale path; regenerating the monolith
after the move removes the operation before its consumer has migrated.

One cutover PR therefore:

1. introduces the six capability export assemblies and moves all 48 exports and
   their DTO closures to their final assemblies;
2. declares the seven roots in `InspectWebJsExportContext`, then generates the
   complete context once and compiles, verifies, lints, and drift-checks every
   mapped facade;
3. adds the single-flight coordinator and migrates every runtime call and DTO
   import;
4. expands Browser/Wasm composition and paired async deployment receipts to the
   exact production facade set; and
5. deletes the monolithic source, declaration, JavaScript module, and every
   compatibility import in the same change.

Preparatory PRs may precede the cutover only when the current monolithic facade
and deployment evidence remain complete. Extracting
`DotnetInspect.Web.Core`, adding reusable generation-loop infrastructure under
the current one-module configuration, or adding cutover-ready outcome tests are
valid examples. Moving a `[JSExport]`, changing its DTO assembly, publishing a
partial module set, or weakening a current gate is not preparation and belongs
in the atomic cutover.

Each preparatory PR and the cutover must remain independently buildable,
deployable, and demonstrable. There is no temporary aggregate adapter,
handwritten binding, partially initialized application, or compatibility
exception to the exact-one-owner rule.

## Acceptance

The partition is implemented when all of the following hold:

1. `ProductionFacadeContext_DeclaresExactAssemblySet` reads the compiled
   `InspectWebJsExportContext` and proves its root identities equal the seven
   expected managed assemblies.
2. `ProductionFacadePartition_AssignsEveryJsExportExactlyOnce` derives 51
   current exports across the seven expected assemblies with no omission or
   duplicate.
3. `ProductionFacadeProjects_HaveAcyclicOwnerReferences` proves the host,
   export-assembly, and core dependency direction.
4. `ProductionFacadeWireContexts_AreAssemblyLocal` proves every exported JSON
   wire closure is local to its export assembly.
5. The generation drift gate runs one context invocation, requires exact
   equality among compiled roots, canonical generated artifacts, and consumer
   mappings, and compares all seven TypeScript sources, declarations, and
   JavaScript modules.
6. TypeScript and Oxlint ownership tests cover each generated and authored
   composition file without admitting build-output directories.
7. The production Browser/Wasm composition gate initializes concurrent callers,
   passes one consumer-created runtime handle to every facade, observes one SDK
   creation and one live runtime, invokes every facade through its own
   assembly, and runs the entry point exactly once.
8. Existing multi-facade close negatives still fail for duplicate runtimes,
   wrong assembly roots, cross-routing, skipped initialization, and dropped
   managed invocation, while positive startup passes on both Mono and CoreCLR.
9. `InspectWebAsyncDeployment_ReceiptsCoverExactFacadeSet` and
   `InspectWebAsyncDeployment_LoweringsPreserveFacadeContracts` prove paired
   deployment completeness and parity.
10. Source, declaration, runtime-module, and compatibility searches find no
   surviving import or publication of `inspect-web-engine`.
11. The real browser demo loads a package, opens its metadata, source, Analysis,
    and call-graph views, runs a home demo, and round-trips a workspace share
    through the partitioned modules.
