# Inspect-web JSExport facade partitioning

Status: **proposed** for issue
[#4497](https://github.com/richlander/dotnet-inspect/issues/4497).

This is the owning document for the inspect-web production facade partition:
which existing browser-host exports belong together, how independently
generated modules attach to one Browser/Wasm runtime, and how the consumer
proves that the complete partition is deployed. It owns no package, metadata,
Analysis, source, call-graph, vocabulary, or workspace-query semantics.

[`ts-jsexport`](ts-jsexport.md) remains the owner of one compiled assembly to
one generated TypeScript source module. The
[inspection layers](inspection-layers.md) and their focused product documents
remain the owners of the typed operations and results that inspect-web adapts.
The [inspect-web README](../../prototypes/inspect-web/README.md) owns the
implemented browser build and deployment procedure.

## Decision

Inspect-web will replace its one production `InspectWeb.Engine.dll` export
surface with seven independently generated facade modules:

| Facade | Managed assembly | Responsibility |
| --- | --- | --- |
| `inspect-web-host` | `InspectWeb.Engine` | Browser/Wasm lifecycle, host configuration, and build identity |
| `inspect-web-package` | `InspectWeb.Engine.PackageExports` | Package and platform acquisition, package queries, and package content |
| `inspect-web-metadata` | `InspectWeb.Engine.MetadataExports` | API and metadata projection |
| `inspect-web-analysis` | `InspectWeb.Engine.AnalysisExports` | Analysis, integration, opportunity, and performance results |
| `inspect-web-source` | `InspectWeb.Engine.SourceExports` | Source and annotated-source projection |
| `inspect-web-call-graph` | `InspectWeb.Engine.CallGraphExports` | Package and platform call-graph expansion |
| `inspect-web-catalog` | `InspectWeb.Engine.CatalogExports` | Product vocabulary, home demos, and workspace-share transport |

Each module is generated from a different managed export assembly. The
Browser/Wasm application remains one host with one SDK runtime. A
consumer-owned coordinator imports the generated modules from the same
`dotnet.js` module specifier and initializes them serially. Only the host
facade's generated `runEntryPoint()` is used.

The managed project graph has three roles:

```text
InspectWeb.Engine
  executable host and host exports;
  references every capability export assembly
        |
        +-- InspectWeb.Engine.PackageExports
        +-- InspectWeb.Engine.MetadataExports
        +-- InspectWeb.Engine.AnalysisExports
        +-- InspectWeb.Engine.SourceExports
        +-- InspectWeb.Engine.CallGraphExports
        `-- InspectWeb.Engine.CatalogExports
                         |
                         v
              InspectWeb.Engine.Core
       shared workspace and host services;
             contains no [JSExport]
                     |
                     v
      owner-issued DotnetInspector.* and ILInspector.* APIs
```

`InspectWeb.Engine` remains the executable Browser/Wasm host, static-web asset
owner, and assembly read by `BuildIdentity`. `InspectWeb.Engine.Core` is a
non-exported implementation dependency for shared package/platform workspaces,
operation coordinators, host policy, and typed internal projections. Export
assemblies may reference `InspectWeb.Engine.Core` and the product projects
needed by their own capability. They do not reference sibling export
assemblies.

This choice preserves the implemented `ts-jsexport` contract. Generator-side
selection would require a new rule for selecting exports and pruning the
assembly-wide serializer vocabulary. That would be a normative change to the
generator owner as well as this consumer owner. The assembly partition uses the
existing one-assembly/one-module rule and the shared-runtime composition already
gated by `eng/test-inspect-web-multi-facade-canary.sh`.

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
- `InspectWeb.Engine.Core`: consumer-owned acquisition, workspace lifetimes,
  cancellation coordinators, and browser host policy;
- product queries and producers: package, metadata, Analysis, source,
  call-graph, and vocabulary semantics and typed results;
- the inspect-web UI: presentation, navigation, request authority, and visible
  error handling; and
- the worker protocol and managed-operation bridge: worker epochs, dynamic
  operation admission, progress callbacks, and cancellation messages.

### Non-claims

This partition does not:

- add export filtering, module manifests, or multi-assembly input to
  `ts-jsexport`;
- change any managed operation's parameters, JSON wire shape, failure
  semantics, query behavior, or progressive-disclosure policy;
- expose raw `ILInspector` or `DotnetInspector` objects to TypeScript;
- introduce lazy module loading, multiple runtimes, workers, or a network
  protocol;
- make module names into product-layer authorities; or
- claim that managed project boundaries and product architecture layers are
  identical.

The export assemblies are L3 browser adapters. Their names describe the
capability they adapt, not ownership of the underlying product facts.

## Current surface and target inventory

The current engine contains 45 `[JSExport]` methods across seven source files.
The generated `initializeRuntime()` and `runEntryPoint()` functions are
generator-owned infrastructure and are not part of that count.

The target inventory below is exhaustive. The implementation gate
`ProductionFacadePartition_AssignsEveryJsExportExactlyOnce` will derive the
actual export set from the built assemblies and fail for an omitted, duplicated,
or unexpected assignment.

### Host facade: 3 exports

- `AsyncLoweringCanary`
- `BuildIdentity`
- `ConfigureHost`

The host assembly is the only facade whose `runEntryPoint()` the application
calls. `ConfigureHost` configures shared `InspectWeb.Engine.Core` policy before the
entry point starts application work. `AsyncLoweringCanary` remains the
deployment smoke's deterministic awaited operation.

### Package facade: 14 exports

- `CancelPackageQuery`
- `GetPackageDocument`
- `ListPackageQueryFacets`
- `LoadRuntimePack`
- `LoadRuntimePackAssembly`
- `MatchPackageDependencyCoordinate`
- `PackageCacheStats`
- `QueryMemberDocumentation`
- `QueryPackage`
- `QueryPackageDependencies`
- `QueryPackageVersions`
- `ResolvePackageDependencyVersion`
- `RunPackageQuery`
- `SearchTypes`

This facade owns browser adaptation for package and platform acquisition,
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
coordinates through `InspectWeb.Engine.Core`; it does not acquire artifacts
independently.

### Analysis facade: 7 exports

- `QueryMemberFacts`
- `QueryPackageIntegrations`
- `QueryPackageOpportunities`
- `QueryPackagePerformance`
- `QueryPlatformIntegrations`
- `QueryPlatformOpportunities`
- `QueryPlatformPerformance`

The explicitly unavailable platform-performance operation stays in this facade
so absence remains a visible capability result rather than a missing binding.
The module does not combine Analysis with call-graph topology; graph traversal
has its own facade and product owner.

### Source facade: 5 exports

- `CancelSourceQuery`
- `QueryMemberAnnotatedSource`
- `QueryMemberSource`
- `QueryTypeMemberSource`
- `QueryTypeSource`

The source cancellation coordinator remains shared consumer infrastructure, but
its public cancellation operation belongs beside the work it cancels.
Annotated source stays with source because the returned document and its
viewer contract are the capability being requested; Analysis facts embedded in
that product document do not transfer ownership to this adapter.

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
package/workspace services through `InspectWeb.Engine.Core`; it does not call the
package facade or reuse that facade's wire DTOs.

## Managed assembly contract

### Export isolation

Every `[JSExport]` method lives in exactly one export assembly.
`InspectWeb.Engine` owns the three host exports.
`InspectWeb.Engine.Core` contains no `[JSExport]` attribute, no generated
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

An export does not serialize a DTO declared by `InspectWeb.Engine.Core` or a
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
`InspectWeb.Engine.Core` implementation in the one runtime.

A helper moves to core only when at least two facade assemblies consume the
same typed browser-host operation. Product facts and classifications stay in
their product owners. Facade-specific formatting or DTO projection stays in
the facade that publishes it.

## Browser composition

All generated facades use the exact same runtime module specifier:

```text
./_framework/dotnet.js
```

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
  await host.initializeRuntime();
  await packageApi.initializeRuntime();
  await metadata.initializeRuntime();
  await analysis.initializeRuntime();
  await source.initializeRuntime();
  await callGraph.initializeRuntime();
  await catalog.initializeRuntime();
}

export function initializeFacades(): Promise<void> {
  readiness ??= initializeCore();
  return readiness;
}
```

The real coordinator also retains the first initialization failure so later
callers observe the same failure. It does not return a runtime or raw managed
export object.

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

The existing multi-facade canary gates the underlying SDK behavior:

- first initialization creates the runtime;
- later generated facades reuse the completed SDK runtime;
- independently generated modules retain assembly-specific dispatch; and
- wrong roots, duplicate runtime modules, cross-routing, skipped initialization,
  and dropped managed invocation fail the gate.

The production gate adds all seven real assemblies and their actual operations;
the fixture canary remains because its intentionally colliding identities are a
stronger close negative than the production names.

## TypeScript ownership

Each generated TypeScript source is the authoritative checked-in handoff for
one managed export assembly. Consumer-owned TypeScript compilation derives one
`.d.ts` and one browser JavaScript module from it:

```text
InspectWeb.Engine.PackageExports.dll
        |
        v
engine/facades/inspect-web-package.ts
        |
        +-- src/facades/inspect-web-package.d.ts
        `-- engine/wwwroot/inspect-web-package.js
```

The generation command runs once per export assembly using the same
`ts-jsexport` binary, resolved runtime-pack declaration, compiler configuration,
and runtime verifier. A single `--check` command regenerates all modules into
scratch space and compares every source, declaration, and JavaScript artifact.
It fails if the expected module set and the generated module set differ.

Application files import DTOs from their owning facade declaration. Runtime
composition imports JavaScript modules only in the coordinator. A small
authored bindings module may adapt generated names to existing application
callback interfaces, but it does not re-export all generated functions as a
new monolithic facade.

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
- the exact seven generated TypeScript contract digests;
- the exact seven shipped WebCIL assembly names and content digests;
- per-assembly and total `[JSExport]`, compiler-async, and runtime-async counts;
- the verified repository-project graph count; and
- successful Browser/Wasm initialization of every facade plus the host canary
  result.

Both jobs must report the same assembly names, generated-contract digests,
total export count, and repository-project graph. Their lowering counts remain
the expected all-or-nothing inverse. A receipt for only `InspectWeb.Engine.dll`
is incomplete after partitioning even if its local counts are correct.

The deployment smoke initializes every module, which acquires its exact
assembly export root and validates every expected runtime path, then invokes
`host.asyncLoweringCanary()`. It remains independent of network, package-cache,
server-API, and user-data state. Per-assembly lowering censuses prove that each
module was compiled in the expected mode; contract and WebCIL digests prove
that the censused assembly and generated binding are the deployed artifacts.

The local production composition gate separately invokes representative real
operations from package, metadata, Analysis, source, call-graph, and catalog
facades using its bounded test inputs. It does not turn deployment
certification into a network integration test.

`InspectWebAsyncDeployment_ReceiptsCoverExactFacadeSet` gates module-set
completeness. `InspectWebAsyncDeployment_LoweringsPreserveFacadeContracts`
gates equal TypeScript contracts and inverse lowering counts across the paired
deployments.

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

The implementation should land as a stack because later slices consume the
project and artifact boundaries established by earlier slices:

1. **Managed partition.** Introduce `InspectWeb.Engine.Core` and the seven export
   assemblies, move all 45 operations and their DTO closures, and gate exact
   assignment and the acyclic project graph.
2. **Generated artifacts.** Generate, compile, verify, lint, and drift-check
   each facade while retaining the monolithic application import as a temporary
   migration boundary.
3. **Consumer migration.** Add the single-flight coordinator, migrate runtime
   calls and DTO imports to their owners, and remove the monolithic facade.
4. **Deployment certification.** Expand Browser/Wasm execution and paired async
   deployment receipts to the exact production facade set, then remove
   temporary compatibility gates.

Every slice has one independently demonstrable claim. The monolithic generated
facade is deleted only in the slice that migrates every consumer and proves
there is no runtime or declaration import left.

## Acceptance

The partition is implemented when all of the following hold:

1. `ProductionFacadePartition_AssignsEveryJsExportExactlyOnce` derives 45
   current exports across the seven expected assemblies with no omission or
   duplicate.
2. `ProductionFacadeProjects_HaveAcyclicOwnerReferences` proves the host,
   export-assembly, and core dependency direction.
3. `ProductionFacadeWireContexts_AreAssemblyLocal` proves every exported JSON
   wire closure is local to its export assembly.
4. The generation drift gate compares the exact seven TypeScript sources,
   declarations, and JavaScript modules.
5. TypeScript and Oxlint ownership tests cover each generated and authored
   composition file without admitting build-output directories.
6. The production Browser/Wasm composition gate initializes concurrent callers,
   observes one live SDK runtime, invokes every facade through its own assembly,
   and runs the entry point exactly once.
7. Existing multi-facade close negatives still fail for duplicate runtimes,
   wrong assembly roots, cross-routing, skipped initialization, and dropped
   managed invocation.
8. `InspectWebAsyncDeployment_ReceiptsCoverExactFacadeSet` and
   `InspectWebAsyncDeployment_LoweringsPreserveFacadeContracts` prove paired
   deployment completeness and parity.
9. Source, declaration, runtime-module, and compatibility searches find no
   surviving import or publication of `inspect-web-engine`.
10. The real browser demo loads a package, opens its metadata, source, Analysis,
    and call-graph views, runs a home demo, and round-trips a workspace share
    through the partitioned modules.
