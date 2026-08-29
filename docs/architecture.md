# Implementation architecture

This document maps the current implementation to the architecture owned by the
rest of the documentation set. It is a guide to composition, project
boundaries, and code location; it is not an umbrella specification.

Authority is intentionally distributed:

- [Overview](overview.md) names subsystem owners.
- [Inspection space architecture](inspection-space.md) owns the host-neutral
  workspace, query, acquisition, join, cache, and safety target.
- [Inspection layers](design/inspection-layers.md) owns the L1/L2/L3 consumer
  boundaries.
- Focused documents under [`docs/design/`](design/) own their component
  contracts.
- [CLI host architecture](cli-architecture.md) describes command-host
  composition without treating the CLI as the whole product.

When this map disagrees with an owning design or current tests, the owner and
tests win.

## Essential shape

`dotnet-inspect` is one inspection product with multiple hosts. The CLI is the
most complete host, but it is not the architectural center.

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
  immutable query domains | section lenses | request-local plans
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

A normal inspection follows the same broad path in every host:

| Stage | Responsibility | Typical implementation |
| ----- | -------------- | ---------------------- |
| 1. Admit sources | Interpret explicit package, platform, project, local-file, or in-memory input and authorize any network or source-content work. | Host adapters, `DotnetInspector.Packages`, `DotnetInspector.Services`, `DotnetInspector.Artifacts.*` |
| 2. Form a workspace | Retain immutable content and binding-consistent participant contexts for the operation lifetime. | `ArtifactSetSession`, query workspaces, assembly context groups |
| 3. Resolve intent | Turn host gestures into typed subjects, lenses, sections, row plans, and capabilities. | CLI options and resolvers, view-facet and navigation contracts, section catalogs |
| 4. Plan producers | Lower direct section and host demand through an immutable typed-query domain. | `CompiledInspectionDomain<TContext>`, `InspectionQueryCatalog<TContext>` |
| 5. Produce evidence | Execute only the selected producer closure over caller-owned contexts. | Metadata, SourceLink, Analysis, Decompiler, Research, package, and relationship queries |
| 6. Compose results | Preserve owner-issued identity, provenance, correspondence, Findings, and typed failure outcomes. | Query results and focused comparison or graph contracts |
| 7. Present | Project results into sections, rows, documents, or host-native interactions. | CLI views/output, inspect-web engine/UI, focused tools |

Hosts own operation lifetime and policy. Query and evidence owners do not
silently acquire a new source, infer identity from display text, or convert a
failed inspection into an empty success.

## Logical layers

The implementation uses the L1/L2/L3 vocabulary defined by
[Inspection layers](design/inspection-layers.md):

| Layer | Owns | Does not own |
| ----- | ---- | ------------ |
| L1 typed queries | Typed requests and results, prerequisite closure, cost, execution order, and producer invocation. | Sections, command syntax, rendering, or ambient source discovery. |
| L2 inspection lenses | Section candidates, direct producer demand, schemas, output-row projection, and related selection metadata. | Producer algorithms, prerequisite semantics, or host acquisition policy. |
| L3 hosts | User gestures, source authorization, operation lifetime, navigation, command-specific demand, and presentation choice. | Metadata, Analysis, or other producer truth. |

`DotnetInspector.RowSelection` is an orthogonal dependency-free utility.
Artifact contracts and domain engines sit below these consumer layers rather
than forming an additional host tier.

The L1/L2 binding is owned by
[Compiled inspection domain composition](design/section-pipeline.md#compiled-inspection-domain-composition).
One immutable producer domain can serve multiple immutable section lenses while
each request supplies its own context and cancellation.

## Implementation regions

### Artifact and runtime foundations

| Region | Responsibility | Primary authority |
| ------ | -------------- | ----------------- |
| `DotnetInspector.Artifacts` | Source-neutral artifact identity, provenance, diagnostics, acquisition outcomes, and guarded content access. | [Artifact acquisition and workspaces](design/artifact-acquisition-and-workspaces.md) |
| `DotnetInspector.Artifacts.Workspaces` | Bounded immutable contribution composition and workspace-session lifetime. | [Artifact acquisition and workspaces](design/artifact-acquisition-and-workspaces.md) |
| `DotnetInspector.Artifacts.Local` | Snapshotting explicitly supplied local files into artifact contracts. | [Artifact acquisition and workspaces](design/artifact-acquisition-and-workspaces.md) |
| `DotnetInspector.Core` | Tool runtime kernel: cache roots, cache publication, network policy, telemetry, and hardened readers. | [Inspection space architecture](inspection-space.md), [cache concurrency](design/cache-concurrency.md) |
| `DotnetInspector.Packages`, `NuGetFetch` | Package archives, feeds, package/source caches, and version acquisition. | [Version resolution](design/version-resolution.md), [NuGet authentication](design/nuget-authentication.md) |
| `DotnetInspector.Services` | Reusable acquisition and resolution services over explicit host policy. | The focused acquisition, package, platform, PDB, and source designs |

The artifact floor is intentionally package- and Metadata-free. Higher
acquisition adapters produce its contracts; consumers receive guarded content
instead of re-opening a path by convention.

### Metadata, source, and text

| Region | Responsibility | Primary authority |
| ------ | -------------- | ----------------- |
| `ILInspector.MetadataPrimitives` | Dependency-free SRM mechanics and neutral metadata-name operations. | [Metadata primitives](metadata-primitives.md) |
| `ILInspector.Metadata` | PE and portable-PDB facts, API surfaces, typed metadata identities, and raw correlations. | [Assembly inspection query](design/assembly-inspection-query.md), focused Metadata designs |
| `SourceLinkFetch` | SourceLink map matching and provenance grammar. | [PDB acquisition](pdb-acquisition.md) |
| `ILInspector.SourceLink` | SourceLink extraction, canonical paths, URL decoration, source correlation, and source Findings. | [PDB acquisition](pdb-acquisition.md), [source Finding producers](design/source-finding-producers.md) |
| `CSharpText` | Model-free C# and XML-documentation grammars, names, signatures, and conservative text ranges. | [Inspection layers](design/inspection-layers.md) |
| `ILInspector.CSharp` | Model-bound C# spelling and typed type/member views. | [Type, member, and API representation](design/type-member-api-representation.md) |

Metadata owns metadata facts. SourceLink owns SourceLink interpretation.
CSharpText owns textual grammar, while ILInspector.CSharp owns spelling that
depends on typed models.

### Method-body and comparison engines

| Region | Responsibility | Primary authority |
| ------ | -------------- | ----------------- |
| `ILInspector.Instructions` | Shared instruction decoding and exception-region-aware basic blocks. | [Instruction substrate](design/instruction-substrate.md) |
| `ILInspector.ControlFlow` | Shared control-flow, dominance, and dataflow kernels. | [Instruction substrate](design/instruction-substrate.md) |
| `ILInspector.Analysis` | SRM-based whole-assembly and targeted IL evidence, including calls, allocations, safety, leverage, and resource analysis. | Focused Analysis designs, [Finding adoption](design/finding-adoption.md) |
| `ILInspector.Decompiler` | Per-method IR, structuring, typing, C# projection, and annotated IL. | [Decompiler correctness pipeline](decompiler-correctness-pipeline.md) |
| `ILInspector.ILDiff` | Canonical IL-body and assembly comparison with typed failures and Finding projection. | [Implementation diff](design/implementation-diff.md) |
| `ILInspector.CallGraph` | Host-neutral projection of Analysis call trees into graph nodes, edges, cycles, and characteristics. | [Call graph projection](design/call-graph-projection.md) |
| `ILInspector.Research` | Composition of producer-owned Analysis and Decompiler evidence into offset-keyed facts and implementation comparisons. | [Finding adoption](design/finding-adoption.md) |
| `ILInspector.Findings` | Domain-free observation, census, matching, transition, comparison, and correlation contracts. | [Finding nomenclature](design/finding-nomenclature.md), [Finding producers](design/finding-producers.md) |
| `ILInspector.Text` | Exact ordered line inspection and generic text comparison on the Finding spine. | [Finding producers](design/finding-producers.md) |

Analysis and Decompiler intentionally answer different questions at different
representation altitudes. Research composes their evidence; neither engine
reaches through Research to redefine the other.

### Query and lens composition

| Region | Responsibility | Primary authority |
| ------ | -------------- | ----------------- |
| `DotnetInspector.Queries` | Core L1 query definitions, immutable catalogs, workspaces, execution plans, and typed results. | [Inspection layers](design/inspection-layers.md), [inspection space](inspection-space.md) |
| `DotnetInspector.ResearchQueries` | Optional Research-backed L1 queries without pulling Research into the core query assembly. | [Inspection layers](design/inspection-layers.md) |
| `DotnetInspector.PackageQueries` | Package-aware composition over package-neutral queries and realization proofs. | [Package query CLI](design/package-query-cli.md) |
| `DotnetInspector.Vocabulary` | Shared static catalogs for legal rich-query values across hosts. | [Query vocabulary](design/vocabulary.md) |
| `DotnetInspector.Sections` | Current L2 section pipelines, immutable catalogs, schemas, and compiled query-domain lenses. | [Section model](design/section-model.md), [section pipeline](design/section-pipeline.md) |
| `DotnetInspector.RowSelection` | Ordered row-selection stages shared without a host dependency. | [Semantic row selection](design/semantic-row-selection.md) |

Queries accept content-shaped or context-shaped inputs. They do not choose a
renderer, parse command lines, or use display strings as identity.

### Hosts and tools

| Host | Role | Primary guide |
| ---- | ---- | ------------- |
| `src/dotnet-inspect` | Complete command-line host, including source resolution, command orchestration, section selection, output models, and rendering. | [CLI host architecture](cli-architecture.md) |
| `prototypes/inspect-web` | Browser/Wasm host and product UI over reusable engine contracts. | [Inspect Web UI](design/inspect-web-ui.md) |
| `tools/DecompilerHarness` | Decompiler correctness, compile-back, corpus, and independent-oracle orchestration. | [Decompiler correctness pipeline](decompiler-correctness-pipeline.md) |
| Focused apps and fixtures | Narrow executable consumers that prove a reusable boundary without becoming product owners. | Their local README or owning design |

Harnesses and fixtures may prove product behavior, but they do not manufacture
or repair the product evidence they measure.

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
- section, schema, row, output-shape, view-facet, and navigation identities;
- inert presentation text at the untrusted-data boundary.

The owning documents define construction and failure semantics. Adjacent
components consume those values without recreating their validation or
inferring them from formatted text.

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
| Workspace, acquisition, cache, or source policy | [Inspection space](inspection-space.md), [artifact acquisition](design/artifact-acquisition-and-workspaces.md) | `DotnetInspector.Artifacts*`, `DotnetInspector.Core`, `DotnetInspector.Packages`, `DotnetInspector.Services` |
| Query planning or execution | [Inspection layers](design/inspection-layers.md) | `DotnetInspector.Queries`, optional query companions |
| Sections, discovery, or selection | [Progressive disclosure](design/progressive-disclosure.md), [section model](design/section-model.md) | `src/dotnet-inspect/Sections`, `DotnetInspector.RowSelection` |
| Metadata, API, type, or member facts | [Assembly inspection query](design/assembly-inspection-query.md), [representation](design/type-member-api-representation.md) | `ILInspector.Metadata*`, `ILInspector.CSharp`, `CSharpText` |
| Source and PDB behavior | [PDB acquisition](pdb-acquisition.md) | `ILInspector.Metadata`, `ILInspector.SourceLink`, `SourceLinkFetch`, Services |
| IL analysis, graphs, or Findings | [Finding adoption](design/finding-adoption.md), relevant focused Analysis or graph design | `ILInspector.Instructions`, `ILInspector.ControlFlow`, `ILInspector.Analysis`, `ILInspector.CallGraph`, `ILInspector.Findings` |
| Decompilation or implementation comparison | [Decompiler correctness](decompiler-correctness-pipeline.md), [implementation diff](design/implementation-diff.md) | `ILInspector.Decompiler`, `ILInspector.ILDiff`, `ILInspector.Research` |
| CLI command or output behavior | [CLI host architecture](cli-architecture.md), [progressive disclosure](design/progressive-disclosure.md), [output shapes](design/output-shapes.md) | `src/dotnet-inspect` |
| Browser interaction | [Inspect Web UI](design/inspect-web-ui.md) | `prototypes/inspect-web` |

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
