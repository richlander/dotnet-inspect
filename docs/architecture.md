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
- Focused documents under [`docs/design/`](design/) own their component
  contracts.
- [CLI host architecture](cli-architecture.md) describes command-host
  composition without treating the CLI as the whole product.

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
| 1. Admit sources | Interpret explicit package, platform, project, local-file, or in-memory input and authorize any network or source-content work. | Host adapters, `DotnetInspector.Packages`, `DotnetInspector.Services` |
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
intent boundary and the first reusable L2 Rows execution boundary. Current L2
section pipelines remain in the same namespace inside the CLI project; their
broader migration is still incomplete.

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
| `DotnetInspector.Artifacts` | Contract floor | Source-neutral artifact identity, provenance, diagnostics, acquisition outcomes, and guarded content access. | [Artifact acquisition and workspaces](design/artifact-acquisition-and-workspaces.md) |
| `DotnetInspector.Core` | Runtime floor | Cache roots, cache publication, network policy, telemetry, and hardened readers. | [Inspection space architecture](inspection-space.md), [cache concurrency](design/cache-concurrency.md) |
| `DotnetInspector.Artifacts.Workspaces` | Workspace composition | Bounded immutable contribution composition and workspace-session lifetime, currently exercised by the package-free fixture canary. | [Artifact acquisition and workspaces](design/artifact-acquisition-and-workspaces.md) |
| `DotnetInspector.Artifacts.Local` | Source adapter canary | Snapshotting explicitly supplied local files into artifact contracts for the current local-acquisition canary. | [Artifact acquisition and workspaces](design/artifact-acquisition-and-workspaces.md) |
| `NuGetFetch` | Protocol adapter | NuGet feeds, downloads, authentication, and protocol behavior. | [NuGet authentication](design/nuget-authentication.md) |
| `DotnetInspector.Packages` | Package adapter | Package archives, package/source caches, extraction, and version acquisition. | [Version resolution](design/version-resolution.md) |
| `DotnetInspector.Services` | Shared services | Reusable acquisition and resolution services over explicit host policy. | The focused acquisition, package, platform, PDB, and source designs |

The artifact floor is intentionally package- and Metadata-free. Its contracts,
local adapter, and workspace session are implemented migration foundations, not
the universal CLI acquisition path. The current package-free fixture consumes
the canary; existing CLI paths still compose Packages, Services, `AssemblySet`,
and query workspaces while migration continues.

### Metadata, source, and text

| Region | Place in flow | Responsibility | Primary authority |
| ------ | ------------- | -------------- | ----------------- |
| `ILInspector.MetadataPrimitives` | Primitive floor | Dependency-free SRM mechanics and neutral metadata-name operations. | [Metadata primitives](metadata-primitives.md) |
| `CSharpText` | Text grammar floor | Model-free C# and XML-documentation grammars, names, signatures, and conservative text ranges. | [Inspection layers](design/inspection-layers.md) |
| `ILInspector.Metadata` | Metadata producer | PE and portable-PDB facts, API surfaces, typed metadata identities, and raw correlations. | [Assembly inspection query](design/assembly-inspection-query.md), focused Metadata designs |
| `SourceLinkFetch` | Map grammar | SourceLink map matching and provenance grammar. | [PDB acquisition](pdb-acquisition.md) |
| `ILInspector.SourceLink` | Source composer | SourceLink extraction, canonical paths, URL decoration, source correlation, and source Findings. | [PDB acquisition](pdb-acquisition.md), [source Finding producers](design/source-finding-producers.md) |
| `ILInspector.CSharp` | Typed projection | Model-bound C# spelling and typed type/member views. | [Type, member, and API representation](design/type-member-api-representation.md) |

Metadata owns metadata facts. SourceLink owns SourceLink interpretation.
CSharpText owns textual grammar, while ILInspector.CSharp owns spelling that
depends on typed models.

### Evidence and comparison engines

| Region | Place in flow | Responsibility | Primary authority |
| ------ | ------------- | -------------- | ----------------- |
| `ILInspector.Findings` | Result contracts | Domain-free observation, sealed-census identity, matching, transition, comparison, complete analysis-diff, and correlation contracts. | [Finding nomenclature](design/finding-nomenclature.md), [Finding instance census](design/finding-instance-census.md), [Analysis diff](design/analysis-diff.md), [Finding producers](design/finding-producers.md) |
| `ILInspector.Instructions` | Decode substrate | Shared instruction decoding and exception-region-aware basic blocks. | [Instruction substrate](design/instruction-substrate.md) |
| `ILInspector.ControlFlow` | Flow substrate | Shared control-flow, dominance, and dataflow kernels. | [Instruction substrate](design/instruction-substrate.md) |
| `ILInspector.Text` | Text producer | Exact ordered line inspection and generic text comparison on the Finding spine. | [Finding producers](design/finding-producers.md) |
| `ILInspector.Analysis` | IL evidence producer | SRM-based whole-assembly and targeted IL evidence, including calls, allocations, safety, leverage, and resource analysis. | Focused Analysis designs, [Finding adoption](design/finding-adoption.md) |
| `ILInspector.Decompiler` | IR producer | Per-method IR, structuring, typing, C# projection, and annotated IL. | [Decompiler correctness pipeline](decompiler-correctness-pipeline.md) |
| `ILInspector.ILDiff` | Comparison producer | Canonical IL-body and assembly comparison with typed failures and Finding projection. | [Implementation diff](design/implementation-diff.md) |
| `ILInspector.CallGraph` | Derived projection | Host-neutral projection of Analysis call trees into graph nodes, edges, cycles, and characteristics. | [Call graph projection](design/call-graph-projection.md) |
| `ILInspector.Research` | Cross-representation composer | Composition of producer-owned Analysis and Decompiler evidence into offset-keyed facts and implementation comparisons. | [IL coordinate workflows](design/il-coordinate-workflows.md), [Implementation diff](design/implementation-diff.md) |

Analysis and Decompiler intentionally answer different questions at different
representation altitudes. Research composes their evidence; neither engine
reaches through Research to redefine the other.

### Query and current lens composition

| Region | Place in flow | Responsibility | Primary authority |
| ------ | ------------- | -------------- | ----------------- |
| `DotnetInspector.Vocabulary` | Cross-host catalog | Shared static catalogs for legal rich-query values across hosts. | [Query vocabulary](design/vocabulary.md) |
| `DotnetInspector.RowSelection` | Shared row-selection contract | Typed `Head`, `Tail`, `Window`, and `Top` declarations plus complete-sequence generic reference evaluation. | [Semantic row selection](design/semantic-row-selection.md) |
| `DotnetInspector.Sections` | Shared L2 contracts | Typed unresolved row-selection intent plus binding of already-resolved section-row cohorts to semantic selection and L2 result identities. | [L2 section-row shaping](design/section-row-shaping.md) |
| `DotnetInspector.Presentation` | Shared presentation composition | Host-neutral lowering from typed inspection and comparison contracts into Markout presentation shapes. Member source diff projection deliberately consumes the Queries, Decompiler, Text, Metadata, MetadataPrimitives, and CSharpText graph so hosts cannot pair independently acquired endpoints or infer constructor context from display text. | [Analysis diff](design/analysis-diff.md), [Comparison document](design/comparison-document.md), [Member source diff presentation](design/member-source-diff-presentation.md) |
| `DotnetInspector.Queries` | Core L1 | Typed query definitions, immutable catalogs, workspaces, execution plans, and typed results. | [Inspection layers](design/inspection-layers.md), [inspection space](inspection-space.md) |
| `DotnetInspector.ResearchQueries` | Optional L1 companion | Research-backed queries without pulling Research into the core query assembly. | [Inspection layers](design/inspection-layers.md) |
| `DotnetInspector.PackageQueries` | Optional L1 companion | Package-aware composition over package-neutral queries and realization proofs. | [Package Root realization](design/artifact-acquisition-and-workspaces.md#package-root-realization) |

Queries accept content-shaped or context-shaped inputs. They do not choose a
renderer, parse command lines, or use display strings as identity.

The reusable `DotnetInspector.Sections` project currently contains the
unresolved selection-operation intent and one-cohort Rows execution
boundaries. Existing L2 section pipelines, immutable catalogs, schemas, and
compiled lenses remain under
`src/dotnet-inspect/Sections` in the CLI assembly. The
[Section model](design/section-model.md) and
[section pipeline](design/section-pipeline.md) own those contracts;
[Inspection layers](design/inspection-layers.md) owns their target reusable L2
boundary. The browser host currently consumes L1 query projects without
referencing the CLI assembly.

### Hosts and tools

| Host | Place in flow | Role | Primary guide |
| ---- | ------------- | ---- | ------------- |
| `src/DotnetInspector.Ecosystems` | Application catalog | Static package-set identity and membership today; target ecosystem-pack metadata and product-demo sources shared by the product front ends without entering reusable infrastructure. | [Package Set Registry](design/package-set-registry.md), [Static Ecosystem Packs](design/ecosystem-packs.md), [Workspace Definitions](design/workspace-definitions.md#product-demos-are-closed-section-presets) |
| `src/dotnet-inspect` | Product host | Complete command-line host, including source resolution, command orchestration, section selection, output models, and rendering. | [CLI host architecture](cli-architecture.md) |
| `prototypes/inspect-web` | Product host | Browser/Wasm host and product UI over reusable engine and focused UI-control contracts. | [Inspect Web UI](design/inspect-web-ui.md) composition map, [SlideStrip](design/inspect-web-slide-strip.md) reusable control, [operation authority](design/inspect-web-operation-authority.md) |
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

Within the CLI host, `PackageIndexCache` is a focused derived-result owner. Its
[package index cache](design/package-index-cache.md) contract defines when a
persistent filesystem-derived package projection may replace cold inspection;
`CoreCache` remains only its storage mechanism.

Within `DotnetInspector.Services`, package-metadata persistence is a focused
observation-reuse owner. Its
[package metadata persistence](design/package-metadata-persistence.md)
contract defines when a complete, authority-scoped present or absent
observation may replace a fresh metadata operation; `MetadataFieldCache` and
`CoreCache` remain encoding and storage mechanisms.

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
| Workspace share packets | `WorkspaceSharePacketCodec` in `DotnetInspector.Queries`; [workspace definitions](design/workspace-definitions.md#the-url-share-packet) owns the versioned projection. | A bounded canonical base64url/JSON projection of acquisition coordinates, binding contexts, navigation focus, and optional initial view state. | Acquired artifacts, a serialized live workspace, credentials, or query results. |
| Nuspec XML | `DotnetInspector.Services.NuspecParser` over the shared `HardenedXml` boundary; [nuspec structural compatibility](design/nuspec-structural-compatibility.md) owns accepted document shapes. | Untrusted package-manifest structure projected into `NuspecData`, then validated by consuming package queries. | Authoritative package coordinates or acquisition provenance merely because the manifest declares them. |

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
| Sections, discovery, or selection | [Progressive disclosure](design/progressive-disclosure.md), [section model](design/section-model.md), [semantic row selection](design/semantic-row-selection.md) | `DotnetInspector.RowSelection`, `DotnetInspector.Sections`, `src/dotnet-inspect/Sections`, `src/dotnet-inspect/Output` |
| Metadata, API, type, or member facts | [Assembly inspection query](design/assembly-inspection-query.md), [representation](design/type-member-api-representation.md) | `ILInspector.Metadata*`, `ILInspector.CSharp`, `CSharpText` |
| Portable identities or interchange formats | [Inspection space currencies](inspection-space.md#core-currencies), [workspace definitions](design/workspace-definitions.md), [nuspec compatibility](design/nuspec-structural-compatibility.md) | `CSharpText.XmlDocumentationNotation`, `DotnetInspector.Queries.Definitions.WorkspaceSharePacket*`, `DotnetInspector.Services.NuspecParser` |
| Source and PDB behavior | [PDB acquisition](pdb-acquisition.md) | `ILInspector.Metadata`, `ILInspector.SourceLink`, `SourceLinkFetch`, Services |
| IL analysis, graphs, or Findings | [Finding adoption](design/finding-adoption.md), relevant focused Analysis or graph design | `ILInspector.Instructions`, `ILInspector.ControlFlow`, `ILInspector.Analysis`, `ILInspector.CallGraph`, `ILInspector.Findings` |
| Decompilation or implementation comparison | [Decompiler correctness](decompiler-correctness-pipeline.md), [implementation diff](design/implementation-diff.md) | `ILInspector.Decompiler`, `ILInspector.ILDiff`, `ILInspector.Research` |
| CLI command or output behavior | [CLI host architecture](cli-architecture.md), [progressive disclosure](design/progressive-disclosure.md), [output shapes](design/output-shapes.md) | `src/dotnet-inspect` |
| Browser interaction | [Inspect Web UI](design/inspect-web-ui.md) composition map; see [navigation presentation](design/inspect-web-navigation-presentation.md), [navigation consumer](design/inspect-web-navigation-consumer.md), [shell interaction](design/inspect-web-shell-interaction.md), and [surface composition](design/inspect-web-surface-composition.md) | `prototypes/inspect-web` |

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
