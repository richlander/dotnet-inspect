# Architecture

`dotnet-inspect` uses a layered architecture in which focused designs and
components own contracts. An owner defines the guarantees and typed affordances
that consumers may rely on, the requirements that implementations must uphold,
and the behavior the contract does not promise. Consumers compose owner-issued
identities, operations, and evidence without reconstructing stronger semantics
from implementation details.

This document maps the current implementation and its explicit migration
boundaries to the architecture owned by the rest of the documentation set. It
is a guide to composition, project boundaries, and code location; it is not an
umbrella specification.

Authority is intentionally distributed:

- [Overview](overview.md) names subsystem owners.
- [Inspection space architecture](inspection-space.md) owns the host-neutral
  workspace, query, acquisition, join, cache, and safety target.
- [Inspection layers](design/inspection-layers.md) owns the L1/L2/L3 consumer
  boundaries.
- [Library family boundaries](design/library-family-boundaries.md) owns the
  meaning of project and namespace roots independently from consumer layer and
  component role.
- Focused documents under [`docs/design/`](design/) own their component
  contracts.
- [CLI host architecture](cli-architecture.md) describes command-host
  composition without treating the CLI as the whole product.
- [Decompiler architecture](decompiler-architecture.md) maps the decompiler
  implementation, host integration, and testing infrastructure.

## Essential shape

`dotnet-inspect` is one inspection product with multiple hosts. The CLI is the
most complete host, but it is not the architectural center. The product is
converging incrementally on this host-neutral shape:

```text
Hosts
  CLI | browser/Wasm | focused tools and harnesses
                       |
                       v
Host composition and presentation
  source authorization | navigation | sections | rows | rendering
                       |
                       v
Typed inspection composition
  immutable query catalogs | section demand | request-local plans
                       |
                       v
Domain-owned evidence
  metadata | source | IL | analysis | C# | decompilation | diffs | Findings
                       |
                       v
Artifact and workspace foundations
  acquisition outcomes | guarded content | provenance | leases | caches
```

The arrows describe request composition, not a strict project-reference stack.
A logical layer may span projects, and dependency-free contract floors can be
referenced from several layers.

## Request composition

Current hosts perform the same broad responsibilities, although migration to
the source-neutral artifact and compiled-domain seams is incremental:

| Stage | Responsibility | Typical implementation |
| ----- | -------------- | ---------------------- |
| 1. Admit sources | Interpret explicit package, platform, project, local-file, or in-memory input and authorize any network or source-content work. | Host adapters, `NuGetFetch`, target `SourceFetch`, `DotnetInspector.Packages`, transitional `DotnetInspector.Services` |
| 2. Form a workspace | Retain content and binding-consistent participant contexts for the operation lifetime. | `AssemblySet`, query workspaces, assembly context groups; artifact-session canaries |
| 3. Resolve intent | Turn host gestures into typed subjects, lenses, sections, row plans, and capabilities. | CLI options and resolvers, section catalogs, output projections |
| 4. Plan producers | Lower direct section and host demand through an immutable typed-query catalog. | `InspectionQueryCatalog<TContext>`; Diff's compiled domain and lens |
| 5. Produce evidence | Execute only the selected producer closure over caller-owned contexts. | Metadata, SourceLink, Analysis, Decompiler, Research, package, and relationship queries |
| 6. Compose results | Preserve owner-issued identity, provenance, correspondence, Findings, and typed failure outcomes. | Query results and focused comparison or graph contracts |
| 7. Present | Project results into sections, rows, documents, or host-native interactions. | CLI views/output, inspect-web engine/UI, focused tools |

Hosts own operation lifetime and policy. Query and evidence owners do not
silently acquire a new source, infer identity from display text, or convert a
failed inspection into an empty success.

## Logical layers

The implementation uses the L1/L2/L3 ownership vocabulary defined by
[Inspection layers](design/inspection-layers.md):

| Layer | Owns | Does not own |
| ----- | ---- | ------------ |
| L1 typed queries | Typed requests and results, prerequisite closure, cost, execution order, and producer invocation. | Sections, command syntax, rendering, or ambient source discovery. |
| L2 inspection lenses | Section candidates, direct producer demand, schemas, output-row projection, and related selection metadata. | Producer algorithms, prerequisite semantics, or host acquisition policy. |
| L3 hosts | User gestures, source authorization, operation lifetime, navigation, command-specific demand, and presentation choice. | Metadata, Analysis, or other producer truth. |

Artifact contracts and domain engines sit below these consumer layers rather
than forming an additional host tier.

These are logical boundaries, not a claim that every layer is already a
separate reusable assembly. L1 is available through host-neutral projects.
`DotnetInspector.Sections` contains the typed unresolved selection-operation
intent boundary and the first reusable L2 Rows execution boundary. It also owns
the Dependency Query operation registration beside the existing
type-relationship row vocabulary and host-neutral Dependency content, avoiding
a parallel Source, Target, Kind, or Traversal implementation. Current L2 section
pipelines remain in the same namespace inside the CLI project; their broader
migration is still incomplete.

The reusable L1/L2 binding is owned by
[Compiled inspection domain composition](design/section-pipeline.md#compiled-inspection-domain-composition).
One immutable producer domain can serve multiple immutable section lenses while
each request supplies its own context and cancellation. Diff is the current
production canary; other command families still use their existing query and
section catalog composition.

## Implementation regions

The tables follow composition order: shared contracts and substrates first,
then primary producers, then derived projections, composers, and hosts.
Parallel siblings are grouped by their place in that flow rather than sorted
alphabetically.

### Artifact and runtime foundations

| Region | Place in flow | Responsibility | Primary authority |
| ------ | ------------- | -------------- | ----------------- |
| Stateless core composition (target) | Cross-owner service boundary | Target association among resource-free Workspace definitions, one selected active realization, explicit operation authority, detached terminal results, and an optional persistent-cache port. | [Stateless core services](design/stateless-core-services.md), [#6749](https://github.com/richlander/dotnet-inspect/issues/6749) |
| `Inspector.Artifacts` | Contract floor | Source-neutral artifact identity, provenance, diagnostics, acquisition outcomes, resource-classified access authority, and scoped content borrowing. | [Artifact acquisition and workspaces](design/artifact-acquisition-and-workspaces.md), [Artifact ownership and borrowing](design/artifact-ownership-and-borrowing.md), [library family boundaries](design/library-family-boundaries.md) |
| `DotnetInspector.Cache` | Cache mechanism | `PersistentCache` roots, hashed keys, maintenance, atomic file publication, and `CacheTelemetry`. Its dependencies are limited to the platform, `InertText`, and `DotnetInspector.Networking`. | [Inspection space architecture](inspection-space.md#persistentcache), [cache concurrency](design/cache-concurrency.md), [#6671](https://github.com/richlander/dotnet-inspect/issues/6671) |
| `Inspector.Resources` | Ownership protocol floor | Dependency-free current-C# declaration carriers plus the serializer-neutral synchronous snapshot callback and ref-like view shared by ownership-protected values and Analysis. The common model covers exclusive mutation, terminal obligations, borrows, and detached immutable results; the non-normative type map records each owner's current adoption without redefining it. | [Resource ownership and borrowing](design/resource-ownership-and-borrowing.md), [resource-owner type map](design/resource-owner-type-map.md), [Resource Effect Language](design/resource-effect-language.md), [#6544](https://github.com/richlander/dotnet-inspect/issues/6544) |
| `Inspector.Artifacts.Workspaces` | Workspace composition | Bounded immutable contribution composition, resource-free content references, transferable retained-content children, and asynchronously settled workspace-session lifetime, currently exercised by the package-free fixture canary. | [Artifact acquisition and workspaces](design/artifact-acquisition-and-workspaces.md), [Artifact ownership and borrowing](design/artifact-ownership-and-borrowing.md), [library family boundaries](design/library-family-boundaries.md) |
| `Inspector.Artifacts.Local` | Source adapter canary | Snapshotting explicitly supplied local files into artifact contracts for the current local-acquisition canary. | [Artifact acquisition and workspaces](design/artifact-acquisition-and-workspaces.md), [library family boundaries](design/library-family-boundaries.md) |
| `DotnetInspector.DependencyManifests` | SDK application format processing | Bounded byte-to-model interpretation of exact application runtime and compilation targets, managed asset coordinates, and declared library location metadata. | [Application dependency manifest format](design/application-dependency-manifest-format.md), [#6199](https://github.com/richlander/dotnet-inspect/issues/6199) |
| `DotnetInspector.Libraries` | Managed-Library resource owner | Resource-free realized-Library, content-role, Artifact-registration, and companion-correspondence references; aggregate Artifact content-child ownership; owner-issued operation leases; synchronous scoped single/pair borrowing; and asynchronous aggregate retirement. PackageHouse compile-handoff, PlatformHouse exact one-Library, and SourceHouse shared member-pair adoption are implemented; Workspace, DocumentationHouse host, and broader source/platform adoption remain staged. | [Library ownership and borrowing](design/library-ownership-and-borrowing.md), [resource ownership and borrowing](design/resource-ownership-and-borrowing.md), [selected member source pairs](design/member-source-pair-query.md), [#6621](https://github.com/richlander/dotnet-inspect/issues/6621), [#7033](https://github.com/richlander/dotnet-inspect/issues/7033) |
| `DotnetInspector.EcosystemLoading` | Ecosystem population service contract | Host-neutral canonical loader identity, typed static binding, exact Workspace-registration correspondence and selection, single-use requests, closed terminal outcomes and receipts, completed-child evidence, and one-shot authority transfer or retirement. Platform population children retain exact PlatformHouse request and receipt evidence, map the terminal outcomes issued by current population producers, map focus and binding-support roles, and transfer the adjacent Artifact session independently from Library owners. Workspace admission, Navigation, envelopes, and host adoption remain staged. | [Ecosystem population loading](design/ecosystem-population-loading.md), [#7317](https://github.com/richlander/dotnet-inspect/issues/7317) |
| `NetworkAccess` | Network destination admission | Shared configured-origin and public-address connection policy for independently owned desktop transports. | [Library family boundaries](design/library-family-boundaries.md), [untrusted-data threat model](design/untrusted-data-threat-model.md) |
| `UntrustedDocuments` | Untrusted document parsing | Duplicate-rejecting JSON and DTD-prohibiting XML entry points with optional decoded-character limits. It depends only on the platform; consumers own schema and semantic policy. | [Library family boundaries](design/library-family-boundaries.md), [untrusted-data threat model](design/untrusted-data-threat-model.md), [#6770](https://github.com/richlander/dotnet-inspect/issues/6770) |
| `DotnetInspector.Networking` | Product HTTP composition | Cross-host HTTP clients, offline and credential-free composition, request currency and breadcrumbs, network traffic policy, observations, and diagnostics. | [Library family boundaries](design/library-family-boundaries.md), [untrusted-data threat model](design/untrusted-data-threat-model.md), [#6572](https://github.com/richlander/dotnet-inspect/issues/6572) |
| `NuGetFetch` | Protocol adapter | NuGet feeds, downloads, authentication, and protocol behavior. | [NuGet authentication](design/nuget-authentication.md) |
| Target `SourceFetch` | Transport adapter | Bounded host-authorized source-byte retrieval, caller-owned validation, redirect handling without a provenance claim, content-store integration, and typed transport outcomes. | [SourceFetch evidence admission](design/source-fetch.md), [PDB acquisition](pdb-acquisition.md), [library family boundaries](design/library-family-boundaries.md), [#6335](https://github.com/richlander/dotnet-inspect/issues/6335) |
| `DotnetInspector.Packages` | Package domain | Package identity, configured-source authority, resource-free version-selection requests and receipts, pruning policy, payload acquisition, archive content, asset selection, and the host-neutral PackageHouse exact/selecting settlement executor. | [PackageHouse composition](design/package-house.md), [package source model](design/package-source-model.md), [version resolution](design/version-resolution.md) |
| `DotnetInspector.PackageHouse.Execution` | Package service execution | Bounded materialization of one exact acquired compile handoff into an Artifact-backed Library. It validates handoff and payload-generation correspondence, publishes selector-issued API/implementation assemblies and optional API-side compiled XML, consumes Metadata projections, and returns Library and Artifact authorities separately. Workspace and host adoption remain staged. | [PackageHouse composition](design/package-house.md), [Library ownership and borrowing](design/library-ownership-and-borrowing.md), [#7324](https://github.com/richlander/dotnet-inspect/issues/7324) |
| `PackageHouse` composition | Package service | Sole host-neutral package settlement facade. The current floor executes typed exact/selecting settlement and acquisition through source-owner-issued leases and issues resource-free selected-Library handoffs; the separate execution adapter materializes one exact compile handoff. Later adoption adds dependency-edge correspondence and provenance-retaining Workspace and host handoffs. | [PackageHouse composition](design/package-house.md), [#6426](https://github.com/richlander/dotnet-inspect/issues/6426) |
| `DotnetInspector.Platforms` contract floor | Platform identity | Package-neutral logical family and exact family-target currency shared by declarations, source composition, pruning, workspaces, CLI, and Browser/Wasm. | [Platform target currency](design/platform-target-currency.md), [#6361](https://github.com/richlander/dotnet-inspect/issues/6361), [#6378](https://github.com/richlander/dotnet-inspect/issues/6378) |
| `DotnetInspector.Platforms.Formats` | Platform file formats | Host-neutral bounded interpretation of shared-framework runtime configurations and dependency manifests from immutable bytes. It owns no paths, source policy, or framework graph resolution. | [Platform manifest formats](design/platform-manifest-formats.md), [#5139](https://github.com/richlander/dotnet-inspect/issues/5139) |
| `DotnetInspector.PlatformHouse` contract seam | Platform service contract | Host-neutral target demands, operations, source authorization plans, contributions, terminal outcomes, and resource-free settlement receipts. Source adapters and product adoption remain separately staged. | [PlatformHouse realization and reference processing](design/platform-house-reference-processing.md), [#6301](https://github.com/richlander/dotnet-inspect/issues/6301) |
| `DotnetInspector.PlatformHouse.Execution` | Platform service execution | Exact-target, one-Library validation and atomic shared-Library owner/reference handoff over selected resource-free source evidence and separately transferred Artifact content children, including explicitly requested API-associated compiled-XML companions, plus typed source-neutral bounded Artifact publication, Metadata projection, cleanup, and assembly-reference terminal projection contracts consumed without friend access. Complete populations transfer ordered all-or-nothing sets of ordinary Library owners for reference-only, implementation-only, or paired reference/implementation views. Every completed population member retains its exact family target and focus or binding-support role beside the aligned value, receipt, Library reference, and owner. Paired populations preserve a reference-first lossless union and join only exact managed identities with matching population attribution; implementation-only members receive both Library content roles under population declaration-surface evidence. The non-owning global assembly-reference operation executes captured `Precedence`, `Fallback`, or `Aggregation` policy over ephemeral source-prepared attempts, opens only the selected snapshot, constructs and borrows a temporary Library, returns Metadata's detached binding decision, and settles and retires every Library and Artifact authority before completion. The versionless runtime executors lazily select one exact target, route any source-issued association only through its explicitly related realization capability, and apply bounded multi-source policy to either one requested Library or one authoritative complete reference population. Both retain the original family-default demand, resource-free discovery and realization settlements, and cumulative finite work; population completion transfers every source-ordered Library owner beside a separate Artifact session. Target-bound type indexing, source-relative routes, and broader product adoption remain staged. | [PlatformHouse realization and reference processing](design/platform-house-reference-processing.md), [#7237](https://github.com/richlander/dotnet-inspect/issues/7237), [#7322](https://github.com/richlander/dotnet-inspect/issues/7322), [#7339](https://github.com/richlander/dotnet-inspect/issues/7339), [#7354](https://github.com/richlander/dotnet-inspect/issues/7354), [#7545](https://github.com/richlander/dotnet-inspect/issues/7545), [#7586](https://github.com/richlander/dotnet-inspect/issues/7586), [#7626](https://github.com/richlander/dotnet-inspect/issues/7626), [#7652](https://github.com/richlander/dotnet-inspect/issues/7652), [#7683](https://github.com/richlander/dotnet-inspect/issues/7683), [#7697](https://github.com/richlander/dotnet-inspect/issues/7697), [#7778](https://github.com/richlander/dotnet-inspect/issues/7778), [#7825](https://github.com/richlander/dotnet-inspect/issues/7825), [#7892](https://github.com/richlander/dotnet-inspect/issues/7892) |
| `DotnetInspector.PlatformHouse.Execution.Installed` | Installed platform execution | Desktop composition that selects successful installed one-assembly source snapshots and provenance, including an explicitly requested reference compiled-XML companion, for the shared bounded Artifact and one-Library handoff, and materializes authoritative installed reference, manifest-defined implementation, or paired populations into source-ordered Library owners plus a separate Artifact session. Installed source realization explicitly attributes recognized runtime and ASP.NET Core frameworks; population materialization preserves that evidence and rejects unclassified framework members. It adapts complete installed target inventories to source-neutral callback-scoped candidate associations and provides lazy selected-target one-Library and complete-reference-population capabilities without receiving package associations. Paired populations retain each source's independent membership and provenance while PlatformHouse owns exact managed-identity and population-attribution correspondence. Host wiring remains staged. | [PlatformHouse realization and reference processing](design/platform-house-reference-processing.md), [#7269](https://github.com/richlander/dotnet-inspect/issues/7269), [#7322](https://github.com/richlander/dotnet-inspect/issues/7322), [#7339](https://github.com/richlander/dotnet-inspect/issues/7339), [#7354](https://github.com/richlander/dotnet-inspect/issues/7354), [#7586](https://github.com/richlander/dotnet-inspect/issues/7586), [#7683](https://github.com/richlander/dotnet-inspect/issues/7683), [#7697](https://github.com/richlander/dotnet-inspect/issues/7697), [#7778](https://github.com/richlander/dotnet-inspect/issues/7778), [#7825](https://github.com/richlander/dotnet-inspect/issues/7825), [#7892](https://github.com/richlander/dotnet-inspect/issues/7892) |
| `DotnetInspector.PlatformHouse.Execution.Packages` | Package-backed platform execution | Host-neutral composition that selects successful package-backed reference/runtime snapshots and exact package authority, producer, content-generation, origin, coordinate, and digest provenance for the shared bounded Artifact and Library handoff, including an explicitly requested reference compiled-XML companion. One-Library and reference-only, implementation-only, or paired complete-population results return Library and Artifact authorities separately. Package-backed population materialization preserves source-issued framework family and version as exact focus or binding-support membership. It adapts complete package-backed target inventories to callback-scoped associations retaining the exact discovery/candidate pair and provides lazy selected-target one-Library and complete-reference-population realization that sends that pair only to the matching reference capability; external selected targets use an exact package coordinate, and implementation receives only the frozen target plus explicit RID. Single-view populations preserve source order; implementation-only members receive both Library content roles under Platform-owned declaration-surface evidence; paired populations preserve independently authoritative membership as an exact-identity and matching-attribution reference-first lossless union. Every population retains detached Package Source lifetime. Host wiring remains staged. | [PlatformHouse realization and reference processing](design/platform-house-reference-processing.md), [package-backed Platform realization](design/package-backed-platform-realization.md), [#7304](https://github.com/richlander/dotnet-inspect/issues/7304), [#7383](https://github.com/richlander/dotnet-inspect/issues/7383), [#7395](https://github.com/richlander/dotnet-inspect/issues/7395), [#7457](https://github.com/richlander/dotnet-inspect/issues/7457), [#7626](https://github.com/richlander/dotnet-inspect/issues/7626), [#7652](https://github.com/richlander/dotnet-inspect/issues/7652), [#7697](https://github.com/richlander/dotnet-inspect/issues/7697), [#7778](https://github.com/richlander/dotnet-inspect/issues/7778), [#7825](https://github.com/richlander/dotnet-inspect/issues/7825), [#7892](https://github.com/richlander/dotnet-inspect/issues/7892) |
| `DotnetInspector.Platforms.Installed` | Installed platform source | Package-free explicit-hive target discovery plus bounded immutable reference-pack and manifest-defined implementation realization over installed-source coordinates. Explicit one-Library demand may also snapshot the exact same-basename compiled-XML companion without making it assembly-population membership. It owns location, acquisition, graph resolution, and source-specific projection, but does not choose a hive, source policy, target version, or documentation semantics. | [Installed reference-pack realization](design/installed-reference-pack-realization.md), [platform composition and overlays](design/platform-composition-and-overlays.md#installed-implementation-platform-realization), [#6012](https://github.com/richlander/dotnet-inspect/issues/6012) |
| `DotnetInspector.PlatformHouse.Installed` | Platform source integration | Desktop adapter that maps authorized PlatformHouse requests and exact targets to installed reference and implementation sources, including family-default preferred discovery across all installed framework bands and the one-assembly population implied by assembly-reference binding, then returns live source values beside resource-free House contributions. | [Installed reference-pack realization](design/installed-reference-pack-realization.md), [PlatformHouse realization and reference processing](design/platform-house-reference-processing.md) |
| `DotnetInspector.Platforms.Packages` | Package-backed platform source | Host-neutral authorized target discovery plus immutable reference-pack and RID-specific, manifest-defined runtime-pack realization through Package Source operation leases, preserving candidate authority, producer, content generation, support-closure, and digest evidence. Explicit one-Library demand may snapshot the exact same-basename compiled-XML companion from retained package content without making it assembly-population membership or settling documentation. | [Package-backed Platform realization](design/package-backed-platform-realization.md) |
| `DotnetInspector.PlatformHouse.Packages` | Platform source integration | Thin discovery/reference/implementation adapter preserving exact House request and source-selection association while keeping live source values outside resource-free contributions, including family-default fallback discovery in its exact framework band and the one-assembly population implied by assembly-reference binding. | [Package-backed Platform realization](design/package-backed-platform-realization.md), [PlatformHouse realization and reference processing](design/platform-house-reference-processing.md) |
| Target `PlatformHouse` composition | Platform service | Sole host-neutral platform target/version settlement, realization, and reference-processing facade over authorized capabilities, with shared Library owner/reference handoff and Metadata-owned forwarding. Package pruning remains upstream in the package domain; documentation settlement is downstream in DocumentationHouse. | [PlatformHouse realization and reference processing](design/platform-house-reference-processing.md), [#6301](https://github.com/richlander/dotnet-inspect/issues/6301) |
| `DotnetInspector.Services` (transitional) | Shared services | Reusable acquisition and resolution services over explicit host policy pending decomposition into subject owners. | The focused acquisition, package, platform, PDB, and source designs; [#6335](https://github.com/richlander/dotnet-inspect/issues/6335) |

Within Services, `LocalRepoSourceAcquisition` owns [local repository source
acquisition](design/local-repository-source-acquisition.md): checksum-backed
substitution of Git blob bytes for one PDB source request. PDB acquisition
retains the surrounding source-selection, checksum, and fallback policy.
The existing `SourceFetch` implementation targets the independent transport
root rather than the PDB owner: it retrieves authorized source bytes, while
`PdbSourceHouse` decides when and how those bytes satisfy a PDB document for
broader enrichment. Shared [type acquisition](design/type-source-acquisition.md),
[member acquisition](design/member-source-acquisition.md),
and [selected member-pair queries](design/member-source-pair-query.md) use
`SourceHouse` authored settlement through the Library adapter. The target [SourceHouse
composition](design/source-house.md) also consumes content-backed decompiler
services; migration and retirement of the remaining source composition are
tracked by #6512.

The artifact floor is intentionally package- and Metadata-free. Its contracts,
local adapter, and workspace session are implemented migration foundations, not
the universal CLI acquisition path. The current package-free fixture consumes
the canary; existing CLI paths still compose Packages, Services, `AssemblySet`,
and query workspaces while migration continues.

### Metadata, source, and text

| Region | Place in flow | Responsibility | Primary authority |
| ------ | ------------- | -------------- | ----------------- |
| `ILInspector.MetadataPrimitives` | Primitive floor | Dependency-free SRM mechanics and neutral metadata-name operations. | [Metadata primitives](metadata-primitives.md) |
| `CSharpText` | Text grammar floor | Model-free C# and XML-documentation grammars, names, signatures, conservative text ranges, and bounded parsed documentation attached to one exact physical declaration span. | [Inspection layers](design/inspection-layers.md), [C# declaration-attached authored documentation](design/csharp-authored-documentation.md) |
| `CSharpText.MemberSlicing` | Member-text processor | Conservative selection of one complete C# member declaration from caller-supplied line evidence, using only public `CSharpText` contracts. | [Library family boundaries](design/library-family-boundaries.md), [untrusted-data threat model](design/untrusted-data-threat-model.md) |
| `ILInspector.Metadata` | Metadata producer | PE and portable-PDB facts, ReadyToRun image envelopes, API surfaces, typed metadata identities, raw correlations, and resource-free terminal projections of frozen assembly-binding decisions. | [Assembly inspection query](design/assembly-inspection-query.md), [structured type-forwarding resolution](design/type-forwarding-resolution.md), [ReadyToRun image projection](design/readytorun-image-projection.md), focused Metadata designs |
| `DotnetInspector.LibraryMetadata` | Library/Metadata composer | Bounded API-surface and complete type-declaration inventory extraction inside exact Library API-content snapshots, with resource-free correspondence between detached Metadata evidence and realized content. | [Library-Metadata correspondence](design/library-metadata-correspondence.md), [#7238](https://github.com/richlander/dotnet-inspect/issues/7238), [#7932](https://github.com/richlander/dotnet-inspect/issues/7932) |
| `ILInspector.SourceLink` | Source interpreter and composer | SourceLink map matching, provenance grammar, extraction, canonical paths, URL decoration, homogeneous type-document mappings, source Findings, and supplied-source checksum verification and decoding. Default type-document selection belongs to SourceHouse's shared consumer policy. | [PDB acquisition](pdb-acquisition.md), [source Finding producers](design/source-finding-producers.md) |
| `DotnetInspector.SourceHouse` | Source settlement | Authored-only settlement and exact member/type decompilation over transferred Library operation leases, detached selected-image and PDB evidence, finite work, native producer attempts, and resource-free receipts. An attestation-backed source capability may additionally issue closed physical-declaration correspondence from typed build evidence while the runtime remains SRM-only and Roslyn-free. Type authored targets default to the primary document and may select an exact mapped partial document; explicit document requests never become decompilation requests. Shared member Source/comparison and member pairs adopt it through `MemberSourceInspection` and `MemberSourcePairInspection`; ordinary shared type fallback and Browser Type Source use `TypeSourceInspection`. Upstream external-PDB acquisition, full House-owned fallback policy, CLI whole-type decompilation, and DocumentationHouse authored adoption remain staged. | [SourceHouse composition](design/source-house.md), [physical-declaration correspondence](design/source-house-physical-declaration-correspondence.md), [shared member acquisition](design/member-source-acquisition.md), [shared type acquisition](design/type-source-acquisition.md), [selected member source pairs](design/member-source-pair-query.md), [#7356](https://github.com/richlander/dotnet-inspect/issues/7356), [#7497](https://github.com/richlander/dotnet-inspect/issues/7497), [#7522](https://github.com/richlander/dotnet-inspect/issues/7522), [#7859](https://github.com/richlander/dotnet-inspect/issues/7859), [#7953](https://github.com/richlander/dotnet-inspect/issues/7953) |
| `SourceBuildAttestation` | Build-time SourceHouse evidence issuer | Non-packable Roslyn-enabled direct-emission library that accepts exact source bytes and compiler references, mints build-scoped physical-input identities, correlates supported declaration symbols to unique emitted TypeDef or MethodDef XML identities, and supplies detached typed evidence plus an attestation-backed SourceHouse source capability. It is outside the SRM-only product runtime graph. | [Physical-declaration correspondence](design/source-house-physical-declaration-correspondence.md), [#7859](https://github.com/richlander/dotnet-inspect/issues/7859) |
| Target `SourceHouse` composition | Source service | Content-first settlement of SourceLink-authored or C#-decompiled source for one exact target, with independent consumer-selected source and PDB policy and retained producer evidence. | [SourceHouse composition](design/source-house.md), [#6512](https://github.com/richlander/dotnet-inspect/issues/6512) |
| `DotnetInspector.DocumentationHouse.Contracts` | Documentation service contract | Source-neutral subjects grounded in owner-issued Library-Metadata correspondence, compiled-XML contributions, finite operation plans, typed attempts and outcomes, work evidence, and resource-free receipts. | [DocumentationHouse composition](design/documentation-house.md), [Library-Metadata correspondence](design/library-metadata-correspondence.md), [#6579](https://github.com/richlander/dotnet-inspect/issues/6579) |
| `DotnetInspector.DocumentationHouse` | Documentation service | Compiled-XML settlement over one transferred Library operation lease, bounded detached XML bytes, and CSharpText exact-ID reading. Multi-subject execution scans each selected companion once while retaining only requested IDs. Package, direct-Library, and Platform adapters plus Queries composition, CLI package/direct-Library adoption, and Browser package adoption are implemented; Platform host and authored-source adoption remain staged. | [DocumentationHouse composition](design/documentation-house.md), [Library ownership and borrowing](design/library-ownership-and-borrowing.md), [#6579](https://github.com/richlander/dotnet-inspect/issues/6579) |
| `DotnetInspector.DocumentationHouse.Direct` | Direct-Library documentation integration | Maps one exact direct Artifact-backed Library to source-neutral compiled-XML candidates, or unavailable evidence when that realized Library admits no XML companion. It retains no Library lifetime authority. | [DocumentationHouse composition](design/documentation-house.md), [Library ownership and borrowing](design/library-ownership-and-borrowing.md), [#6579](https://github.com/richlander/dotnet-inspect/issues/6579) |
| `DotnetInspector.DocumentationHouse.Packages` | Package documentation integration | Binds one exact PackageHouse Library materialization receipt to source-neutral candidate or authoritative-absence compiled-XML evidence. It retains no PackageHouse payload or Library lifetime authority. | [DocumentationHouse composition](design/documentation-house.md), [PackageHouse composition](design/package-house.md), [#6579](https://github.com/richlander/dotnet-inspect/issues/6579) |
| `DotnetInspector.DocumentationHouse.Platform` | Platform documentation integration | Binds one exact PlatformHouse Library realization receipt to source-neutral candidate, authoritative-absence, or unavailable compiled-XML evidence. Installed and package-backed realization produce the same API-associated Library companion shape; the adapter retains no Platform source value, Artifact authority, or Library lifetime authority. | [DocumentationHouse composition](design/documentation-house.md), [PlatformHouse realization and reference processing](design/platform-house-reference-processing.md), [#6579](https://github.com/richlander/dotnet-inspect/issues/6579) |
| `ILInspector.CSharp` | Typed projection | Model-bound C# spelling and typed type/member views. | [Type, member, and API representation](design/type-member-api-representation.md) |

Metadata owns metadata facts. SourceLink owns SourceLink interpretation.
CSharpText owns textual grammar, while ILInspector.CSharp owns spelling that
depends on typed models.

[C# memory-safety declaration spelling](design/csharp-memory-safety-spelling.md)
owns the CSharp policy for consuming independent caller-contract, pointer,
declaration-shape, and layout facts. The opt-in method/field implementation
includes explicit-layout source lowering; compatibility remains the default.
Other declaration forms and production-host adoption remain pending. Metadata
interpretation and Decompiler reconstruction stay with their respective owners.

### Evidence and comparison engines

| Region | Place in flow | Responsibility | Primary authority |
| ------ | ------------- | -------------- | ----------------- |
| `Inspector.Findings` | Result contracts | Domain-free observation, sealed-census identity, matching, transition, comparison, complete analysis-diff, and correlation contracts. | [Finding nomenclature](design/finding-nomenclature.md), [Finding instance census](design/finding-instance-census.md), [Analysis diff](design/analysis-diff.md), [Finding producers](design/finding-producers.md), [library family boundaries](design/library-family-boundaries.md) |
| `ILInspector.Instructions` | Decode substrate | Shared instruction decoding and exception-region-aware basic blocks. | [Instruction substrate](design/instruction-substrate.md) |
| `ILInspector.ControlFlow` | Flow substrate | Shared control-flow, dominance, and dataflow kernels. | [Instruction substrate](design/instruction-substrate.md) |
| `Inspector.Text` | Text producer | Exact ordered line inspection, generic text comparison, and deterministic LF construction. | [Finding producers](design/finding-producers.md), [library family boundaries](design/library-family-boundaries.md) |
| `ILInspector.Analysis` | IL evidence producer | SRM-based whole-assembly and targeted IL evidence, including calls, allocations, safety, leverage, current ArrayPool resource evidence, bounded admission of portable resource-effect declarations, and generation-bound resolution of those declarations to exact metadata occurrences. | [Resource Effect Language](design/resource-effect-language.md), [Resolved Resource Effects](design/resolved-resource-effects.md), focused Analysis designs, [Finding adoption](design/finding-adoption.md) |
| `ILInspector.Decompiler` | IR producer | Per-method IR, structuring, typing, C# projection, and annotated IL. | [Decompiler correctness pipeline](decompiler-correctness-pipeline.md) |
| `ILInspector.ILDiff` | Comparison producer | Canonical IL-body and assembly comparison with typed failures and Finding projection. | [Implementation diff](design/implementation-diff.md) |
| `ILInspector.CallGraph` | Derived projection | Host-neutral projection of Analysis call trees into graph nodes, edges, cycles, and characteristics. | [Call graph projection](design/call-graph-projection.md) |
| `ILInspector.Research` | Cross-representation composer | Composition of producer-owned Analysis and Decompiler evidence into method-qualified facts, source relationships, and implementation comparisons. | [Research evidence locations](design/research-evidence-locations.md), [IL coordinate workflows](design/il-coordinate-workflows.md), [Implementation diff](design/implementation-diff.md) |

Analysis and Decompiler intentionally answer different questions at different
representation altitudes. Research composes their evidence; neither engine
reaches through Research to redefine the other.

### Query and current lens composition

| Region | Place in flow | Responsibility | Primary authority |
| ------ | ------------- | -------------- | ----------------- |
| `DotnetInspector.Vocabulary` | Cross-host catalog | Shared static catalogs for legal rich-query values across hosts. | [Query vocabulary](design/vocabulary.md) |
| `DotnetInspector.QueryEngine` | Transitional dependency-free physical carrier; target `QuerySpace` | Executable query-operation registration and effective capability projection; portable query intent and codec; generic row vocabularies and execution; and typed `Head`, `Tail`, `Window`, and `Top` selection. [QuerySpace Library Boundary](design/query-space-library.md) owns the target independent package, API, dependency, and lifetime composition; the focused semantic designs retain authority during and after migration. | [QuerySpace library boundary](design/query-space-library.md), [Query operation infrastructure](design/query-operation-infrastructure.md), [portable query intent](design/portable-query-intent.md), [portable query payload](design/portable-query-payload.md), [row query and ordering](design/row-query-order.md), [semantic row selection](design/semantic-row-selection.md), [L2 section-row shaping](design/section-row-shaping.md) |
| `DotnetInspector.SourceSelection` | Shared source-intent contract | Immutable source declarations, exact package/Platform Library coordinates, bounded package-prefix intent, and pure reference search normalization; host adoption is staged. | [Typed source intent](design/search-scope-domain.md), [exact Library source coordinate](design/exact-library-source-coordinate.md) |
| `DotnetInspector.SourceDelegation` | Shared source-execution contract | Typed candidate planning, linear acceptance/execution, and completion-bound row or Count outcomes; source and host adoption is staged. | [Source delegation](design/source-delegation.md) |
| `DotnetInspector.Sections` | Shared L2 contracts | Typed unresolved row-selection intent, binding of already-resolved section-row cohorts to semantic selection and L2 result identities, the immutable structural Discovery Document and semantic output-mode vocabulary, the executable Dependency Query operation and its type-relationship and rooted-hierarchy routes, host-neutral dependency Content and Evidence projections, and the cross-host completed-inspection envelope with portable-share and contained-diagnostic outcomes. | [Schema query](design/schema-query.md), [L2 section-row shaping](design/section-row-shaping.md), [Query operation infrastructure](design/query-operation-infrastructure.md), [Dependency inspection](design/dependency-inspection-command.md), [Library family boundaries](design/library-family-boundaries.md), [#6801](https://github.com/richlander/dotnet-inspect/issues/6801) |
| `DotnetInspector.Presentation` | Shared presentation composition | Host-neutral lowering from typed inspection and comparison contracts into portable documents, source-generated structured JSON, and Markout presentation shapes. Member source diff projection deliberately consumes the Queries, Decompiler, Text, Metadata, MetadataPrimitives, and CSharpText graph so hosts cannot pair independently acquired endpoints or infer constructor context from display text. | [Analysis diff](design/analysis-diff.md), [Comparison document](design/comparison-document.md), [Member source diff presentation](design/member-source-diff-presentation.md), [Ecosystem change report](design/ecosystem-change-report.md) |
| `DotnetInspector.Queries` | Core L1 | Typed query definitions, immutable catalogs and lower ecosystem declarations, neutral empty workspaces, execution plans, and typed results. Package Query owns its executable Query Operation definition, effective route, registered-term projection, portable intent resolution, package-grain plan, and results here; CLI and Browser hosts consume that route without restating its capability inventory. Library Query owns the corresponding Library-grain operation, direct-reference qualification, candidate-bound execution, result, failure, and completion contracts over an explicit assembly-set population. Graph Libraries likewise owns its executable Cluster vocabulary, command and row-set routes, portable plan, and host-neutral cluster selection here; CLI query sections derive discovery and lowering from those routes. Realized Package Dependency Context projects one live Package Root binding's exact retained content and frozen package-local selection intent into a detached association with normalized dependency evidence; Package Dependency Traversal retains that complete context as a typed root source and retains one separate traversal target for compatible candidate-manifest selection. Raw declaration edges remain evidence; a Workspace operation with the .NET Runtime Ecosystem composes Platform subsumption before admitting package routes. Compiled-documentation composition transfers an already-authorized Library operation to DocumentationHouse, retains each exact detached outcome in process, and owns a bounded discriminated outcome as the portable JSON contract; its multi-subject form preserves one outcome per exact subject. The live declaration locator admits explicit context-loader and package-backed reference populations under existing group ownership. Product curation does not enter this layer. | [Inspection layers](design/inspection-layers.md), [Query operation infrastructure](design/query-operation-infrastructure.md), [The package query CLI](design/package-query-cli.md), [Library Query](design/library-query.md), [Pairwise Library Direct-Use Clustering](design/pairwise-library-direct-use-clusters.md), [Realized package dependency context](design/realized-package-dependency-context.md), [Package dependency traversal](design/package-dependency-traversal.md), [DocumentationHouse composition](design/documentation-house.md), [inspection space](inspection-space.md), [ecosystem registration handoff](design/workspace-ecosystem-registration-handoff.md), [Workspace live locator](design/workspace-live-locator.md) |
| `DotnetInspector.ResearchQueries` | Optional L1 companion | Research-backed queries without pulling Research into the core query assembly. | [Inspection layers](design/inspection-layers.md) |
| `DotnetInspector.PackageQueries` | Optional L1 companion | Package-aware composition over package-neutral queries and realization proofs, including one-shot PackageHouse Library materialization and single- or multi-subject compiled-documentation settlement with complete resource retirement before portable publication. | [Package Root realization](design/artifact-acquisition-and-workspaces.md#package-root-realization), [Package version-cell Metadata inspection](design/package-version-cell-metadata-inspection.md), [DocumentationHouse composition](design/documentation-house.md) |

Queries accept content-shaped or context-shaped inputs. They do not choose a
renderer, parse command lines, or use display strings as identity.

The reusable `DotnetInspector.Sections` project currently contains the
unresolved selection-operation intent and one-cohort Rows execution
boundaries, the immutable structural Discovery Document, plus dependency
inspection Content, complete package-evidence projection, and the semantic
dependency graph consumed by the CLI. Existing L2 section pipelines, immutable
catalog declarations, Markout schemas, and compiled lenses remain under
`src/DotnetInspect.Cli/Sections` in the CLI assembly. The
[Section model](design/section-model.md) and
[section pipeline](design/section-pipeline.md) own those contracts;
[Inspection layers](design/inspection-layers.md) owns their target reusable L2
boundary. The CLI constructs the first Library Discovery Document from those
owner-issued declarations. The browser host references the shared Sections
assembly but does not construct or present structural discovery until its
focused #7814 adoption.

### Hosts and tools

| Host | Place in flow | Role | Primary guide |
| ---- | ------------- | ---- | ------------- |
| `src/DotnetInspector.Ecosystems` | Application catalog | Static package-set identity and membership, ecosystem-pack metadata, the separately authored Package/assembly dependency-recognition profile, direct-observation classification and Package/Library recognition outcomes, and Package recognition composition over one exact PackageHouse compile realization. It also owns lazy product-demo sources, opaque special-population loader bindings, exact lower registration projections and loader correspondence selection, platform/all-known resource-free WorkspacePlan factories, and nominal `.NET Runtime` and ASP.NET Core loader inputs. The product loaders retain their exact static Platform family declarations, invoke one host-authorized typed PlatformHouse population capability, and reject a returned request for another family before authority transfer; they do not own target or source selection. Ecosystem registrations aggregate ordinary package and special platform populations; Ecosystem Population Loading owns common orchestration and authority handoff, while independent PackageHouse and PlatformHouse boundaries own domain execution and settlement. Package-backed and installed population materializers expose the common source-neutral handoff outcome directly, and a validated public factory supports other host-authorized materializers. Callers construct live InspectionWorkspace owners explicitly. | [Package Set Registry](design/package-set-registry.md), [Static Ecosystem Packs](design/ecosystem-packs.md), [Ecosystem Dependency Recognition](design/ecosystem-dependency-recognition.md), [Workspace Ecosystem Registration Handoff](design/workspace-ecosystem-registration-handoff.md), [Ecosystem Population Loading](design/ecosystem-population-loading.md) |
| `src/DotnetInspect.Cli` | Product host | Complete command-line host, including source resolution, command orchestration, section selection, output models, and rendering. | [CLI host architecture](cli-architecture.md) |
| `inspect-web/` | Product host | Top-level Browser/Wasm product workspace containing the UI, one-active-realization host seam, focused managed facades, tests, canaries, and deployment tooling. | [Inspect Web UI](design/inspect-web-ui.md) composition map, [retained Workspace realization](design/inspect-web-retained-workspace-realization.md), [navigation presentation](design/inspect-web-navigation-presentation.md), [operation authority](design/inspect-web-operation-authority.md) |
| `tools/DecompilerHarness` | Correctness harness | Decompiler correctness, compile-back, corpus, and independent-oracle orchestration. | [Decompiler correctness pipeline](decompiler-correctness-pipeline.md) |
| Focused apps and fixtures | Boundary canary | Narrow executable consumers that prove a reusable boundary without becoming product owners. | Their local README or owning design |

Harnesses and fixtures may prove product behavior, but they do not manufacture
or repair the product evidence they measure.

Within `tools/DecompilerHarness`, `AuthoredCorpusHistoryStore` is the focused
owner for admitting complete EVIL benchmark artifacts as durable observations
and validating the ordered committed sequence. Its
[committed authored-corpus history](design/authored-corpus-history.md) contract
separates persistence evidence from benchmark production, methodology,
ratchet comparison, and history-card rendering.

`SourceOracleCandidateLedger` is the focused harness owner for complete
candidate-file accounting and deterministic next-enrollment ranking over one
accepted source-oracle baseline. Its
[candidate-ledger contract](design/source-oracle-candidate-ledger.md) consumes
PDB mapping, acquisition, evaluation, syntax-inventory, and provenance evidence
without taking ownership of those producers or of manifest enrollment.

Within the CLI host, `PackageIndexCache` is a focused derived-result owner. Its
[package index cache](design/package-index-cache.md) contract defines when a
persistent filesystem-derived package projection may replace cold inspection;
`PersistentCache` remains only its storage mechanism.

Within `DotnetInspector.Services`, package-metadata persistence is a focused
observation-reuse owner. Its
[package metadata persistence](design/package-metadata-persistence.md)
contract defines when a complete, authority-scoped present or absent
observation may replace a fresh metadata operation; `MetadataFieldCache` and
`PersistentCache` remain encoding and storage mechanisms.

## Core currencies

The architecture composes typed currencies rather than strings or
presentation rows:

- artifact identity, generation, provenance, diagnostics, and guarded content;
- workspace participants, binding context, leases, and request-owned contexts;
- typed query definitions, plans, results, costs, and failures;
- type, member, API, metadata, and instruction identities;
- owner-issued correspondence between versions, representations, or
  participants;
- `Finding<T>`, censuses, comparisons, transitions, and correlation results;
- section, schema, row, and output-shape identities;
- inert presentation text at the untrusted-data boundary.

The owning documents define construction and failure semantics. Adjacent
components consume those values without recreating their validation or
inferring them from formatted text.

### Portable and bound currencies

Portability is one axis of a currency, not a synonym for durability,
correspondence, or displayability:

| Form | Examples | Boundary rule |
| ---- | -------- | ------------- |
| Bound or non-portable | Live readers, IR nodes, query contexts, leases, body-scoped offsets, and generation-scoped catalog keys | Meaning depends on one live image, body, request, workspace, or catalog generation. These values do not cross that boundary. |
| Portable | Artifact coordinates and digests, XML documentation API identifiers, persisted member projections, workspace definition records, and materialized source/text spans | The owner defines enough stable data for the value to cross a query, process, serialization, or persistence boundary. Portability does not make equality prove correspondence. |

Projection from bound to portable is explicit and records what authority or
precision was erased. Rebinding a portable value is another owner operation
with validation and a typed failure; it is not a cast back to the live value.
The full scope/lifetime/portability/correspondence matrix is owned by
[Inspection space](inspection-space.md#core-currencies).

This use of *portable* describes an architectural boundary. It is independent
of format names such as Portable PDB.

### Interchange formats

Interchange is a separate axis: it defines an external or cross-host syntax
from which an owner constructs typed currencies. A value can be portable
without having a standardized interchange syntax, and accepted interchange
text is not automatically trusted identity.

| Format | Owner and typed boundary | Carries | Does not carry |
| ------ | ------------------------ | ------- | -------------- |
| XML documentation API identifiers | `CSharpText.XmlDocumentationNotation` produces `XmlDocMemberIdentity`; [type/member/API representation](design/type-member-api-representation.md) owns its role among identity projections. | Portable `T:`, `M:`, and related lookup notation with the XML documentation signature grammar. | A live metadata binding, Member Index identity, or proof that two members correspond. |
| Workspace share packets | `WorkspaceSharePacketCodec` and `WorkspaceSharePacketTransposer` in `DotnetInspector.Queries`; [workspace definitions](design/workspace-definitions.md#the-url-share-packet) owns the versioned projection. | Bounded canonical base64url/JSON formats: immutable format 1; query-free format 2 with nullable Workspace/Package focus and complete per-coordinate committed view state; format 3 with the ordered portable Workspace registration vector, including registration-only definitions; and explicit format 4 with active Library/Type/Member selection independent of deeper retained context. Existing producers still default to format 3. | Acquired artifacts, a serialized live workspace, credentials, query results, executable Ecosystem scanners, or query-bearing format-2/3/4 state before #6971. |
| Nuspec XML | `DotnetInspector.Services.NuspecParser` over the shared `UntrustedDocuments.HardenedXml` boundary; [nuspec structural compatibility](design/nuspec-structural-compatibility.md) owns accepted document shapes. | Untrusted package-manifest structure projected into `NuspecData`, then validated by consuming package queries. | Authoritative package coordinates or acquisition provenance merely because the manifest declares them. |

## Representation-specific identities

The codebase deliberately has more than one type or member representation.
Metadata API shapes, Analysis `TypeRef` values, Decompiler IR types, C# display
shapes, selectors, and navigation subjects retain different structure and
erasure policies.

This is not accidental duplication. Shared mechanics may move into neutral
primitives, but one representation must not become a universal identity merely
because several displays look alike. The authoritative currency map and
correspondence rules live in
[Type, member, and API representation](design/type-member-api-representation.md).

## Dependency direction

Repository-wide constraints in [`AGENTS.md`](../AGENTS.md) and focused designs
are binding. The implementation map highlights the consequences:

- product inspection remains SRM-based and does not load inspected
  assemblies;
- reusable paths remain NativeAOT-friendly and target Browser/Wasm
  compatibility;
- L1 queries do not depend on CLI presentation;
- Metadata, Analysis, CSharpText, CSharp, Decompiler, Research, and the CLI
  retain their named ownership boundaries;
- network, source-content, and unbounded work require explicit host
  authorization;
- typed failures remain visible rather than becoming empty success;
- presentation is downstream of typed identity, provenance, and
  correspondence.

Focused documents name the Release gates for their safety, soundness, or
faithfulness claims. This map does not duplicate those evolving gate lists.

## Finding the implementation

| Change area | Start with | Then inspect |
| ----------- | ---------- | ------------ |
| Workspace, acquisition, cache, network, or source policy | [Inspection space](inspection-space.md), [artifact acquisition](design/artifact-acquisition-and-workspaces.md) | `Inspector.Artifacts*`, `DotnetInspector.Cache`, `DotnetInspector.Networking`, `DotnetInspector.Packages`, `DotnetInspector.Services` |
| Query planning or execution | [Inspection layers](design/inspection-layers.md) | `DotnetInspector.Queries`, optional query companions |
| Sections, discovery, or selection | [Progressive disclosure](design/progressive-disclosure.md), [section model](design/section-model.md), [semantic row selection](design/semantic-row-selection.md) | Transitional `DotnetInspector.QueryEngine`, target `QuerySpace`, `DotnetInspector.Sections`, `src/DotnetInspect.Cli/Sections`, `src/DotnetInspect.Cli/Output` |
| Metadata, API, type, or member facts | [Assembly inspection query](design/assembly-inspection-query.md), [representation](design/type-member-api-representation.md) | `ILInspector.Metadata*`, `ILInspector.CSharp`, `CSharpText` |
| Portable identities or interchange formats | [Inspection space currencies](inspection-space.md#core-currencies), [workspace definitions](design/workspace-definitions.md), [nuspec compatibility](design/nuspec-structural-compatibility.md) | `CSharpText.XmlDocumentationNotation`, `DotnetInspector.Queries.Definitions.WorkspaceSharePacket*`, `DotnetInspector.Services.NuspecParser` |
| Source and PDB behavior | [PDB acquisition](pdb-acquisition.md) | `ILInspector.Metadata`, `ILInspector.SourceLink`, Services |
| IL analysis, graphs, or Findings | [Finding adoption](design/finding-adoption.md), relevant focused Analysis or graph design | `ILInspector.Instructions`, `ILInspector.ControlFlow`, `ILInspector.Analysis`, `ILInspector.CallGraph`, `Inspector.Findings` |
| Decompilation or implementation comparison | [Decompiler architecture](decompiler-architecture.md), [decompiler correctness](decompiler-correctness-pipeline.md), [implementation diff](design/implementation-diff.md) | `ILInspector.Decompiler`, `ILInspector.ILDiff`, `ILInspector.Research` |
| CLI command or output behavior | [CLI host architecture](cli-architecture.md), [progressive disclosure](design/progressive-disclosure.md), [output shapes](design/output-shapes.md) | `src/DotnetInspect.Cli` |
| Browser interaction | [Inspect Web UI](design/inspect-web-ui.md) composition map; see [navigation presentation](design/inspect-web-navigation-presentation.md), [navigation consumer](design/inspect-web-navigation-consumer.md), [shell interaction](design/inspect-web-shell-interaction.md), and [surface composition](design/inspect-web-surface-composition.md) | `inspect-web/` |

Use [the documentation index](README.md) when the focused owner is not obvious.

## Non-claims

This document does not:

- define command syntax or enumerate current options;
- restate every project, query, section, producer, or test;
- replace focused design contracts with one end-to-end specification;
- assign ownership based only on project names or directory placement;
- describe design-history documents as implemented behavior; or
- make merge, compatibility, safety, or fidelity claims without their owning
  evidence.
