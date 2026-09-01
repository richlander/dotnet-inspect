# dotnet-inspect overview

`dotnet-inspect` is a CLI tool for inspecting .NET packages, platform libraries, local assemblies, public APIs, dependencies, SourceLink/symbol provenance, and version-to-version API changes.

It is built for both humans and agents. Markdown is the default output because headings, compact context rows, tables, and code fences are readable and easy for agents to quote. JSON, `--table`, and `--tsv` are available when structured automation or compact row output is more useful.

## Core architecture

The target [inspection space architecture](inspection-space.md) defines the
core: workspace contexts, typed query planning, acquisition and caching, shared
identity and provenance, owner-issued correspondence, and safe presentation
boundaries. Typed query-planning slices are implemented for library
metadata-image, direct-reference, assembly-context reference,
package dependency-group, loaded dependency-coordinate match,
extension-method, custom-attribute, manifest-resource, type-forwarder,
union-type, classified-method, audit-metadata, unsafe-evidence, top-leverage,
optimization-opportunity,
switch, SourceLink, Integrations, implementation relationships, type/member search,
extension reachability, API-comparison, Analysis body-signal comparison, and
Implementation comparison inspection, plus group-scoped
PDB-mapped-or-decompiled type/member source. The `diff` Changes, Analysis Diff,
and Implementation Diff sections consume producer-owned comparison results
over host-resolved surfaces, body indexes, and retained assembly content.
The library CLI, package `--all-libraries`, `extensions`, `implements`, and
`find` now host workspace-backed queries. Independent search fan-out remains
sequential and bounded to one retained participant at a time; group-scoped
Integrations and extension reachability retain compatible participants for
cross-assembly composition. The components below are the current hosts, shared
substrates, and inspection producers that will extend that space.

- `src/dotnet-inspect/` contains the CLI, command routing, parsers, options,
  output views, section descriptors, and inspectors. Its
  [Find type-search service](design/find-search-service.md) owns the
  CLI-scoped boundary from host-authorized candidate collection through typed
  exact, glob, namespace-prefix, partial, and miss classification; Metadata
  retains candidate facts and the command retains presentation.
- `src/DotnetInspector.Queries/` contains host-neutral typed query definitions,
  deterministic synchronous/asynchronous execution, prerequisite-aware cost,
  and content-shaped metadata, reference, package dependency-group,
  loaded dependency-coordinate match,
  extension-method, custom-attribute, manifest-resource, type-forwarder,
  union-type, classified-method, audit-metadata, unsafe-evidence, top-leverage,
  optimization-opportunity,
  SourceLink,
  implementation-relationship, type/member search, extension-reachability,
  API-comparison, progressive call-graph, and group-scoped source queries. The
  source query owns a Decompiler fallback over retained assembly content. The
  project has no Markout, console, or filesystem-path dependency.
- `src/DotnetInspector.ResearchQueries/` contains the optional Research-backed
  L1 query family. It composes switch metadata with AppContext IL evidence,
  compares already-acquired Analysis body indexes, and compares retained
  implementation assembly content, returning typed results without pulling
  Research into the core query assembly.
- `src/ILInspector.Metadata/` reads PE metadata and portable-PDB structure: named documents, checksums, sequence-point relationships/ranges, raw custom-debug-information blobs, API surfaces, method classification, authenticated [state-machine relationships](design/state-machine-relationship-index.md), and assembly details. `MetadataFindings` projects API and portable-PDB build-context observations onto the shared Finding spine while retaining compatibility classification through `ApiDiff`.
- `src/ILInspector.SourceLink/` sits above Metadata and SourceLinkFetch. It owns SourceLink map extraction, canonical document paths, URL decoration, provenance, high-level type/member/IL-offset resolution, source-document/member-source Findings, and SourceLink-aware debug audits.
- `src/SourceLinkFetch/` owns the dependency-free SourceLink map matcher and provenance grammar.
- `src/ILInspector.MetadataPrimitives/` is the dependency-free leaf for shared
  SRM mechanics and neutral name matching. `StringDistance` lives there so
  Metadata suggestion ranking does not acquire the Finding-backed Text layer;
  `MetadataNameMatching_DoesNotDependOnFindingBackedText` gates that boundary.
- `src/CSharpText/` is a dependency-free leaf for model-free C# and XML-documentation textual grammars: primitive aliases, canonical member signatures, XML-documentation identity notation and comment extraction, FQN/member-selector normalization, operator notation, [type-declaration identifier admission](design/csharp-type-declaration-identifier-admission.md), identifier and keyword policy, expression-body recognition, member text layout, lexing, and conservative declaration/source-range recognition. It is not a parser and makes uncertainty explicit rather than guessing a span.
- `src/ILInspector.CSharp/` is the lightweight model-bound C# spelling and type-view layer over Metadata shapes. `CSharpFormatter` is the declaration-spelling seam; [declared-type self-name admission](design/csharp-declared-type-self-name.md) owns the proposed exact-name boundary shared by type, constructor, and finalizer heads. `CSharpTypePrinter` composes exact typed requests, including skeleton, full, stub, mixed-accessor, primary-constructor, and nested-type shapes, without taking a Decompiler or Research dependency.
- `src/ILInspector.Analysis/` indexes IL method-body evidence such as direct call sites, allocation and unsafety occurrences, method signals, and whole-assembly leverage without decompiling to C#. `AnalysisFindings` exposes reusable typed censuses and comparisons for allocations, call sites, unsafe operations, and unsafe declaration/body evidence.
- `src/ILInspector.Analysis.App/` is a temporary console harness for exercising Analysis queries until CLI wiring exists.
- `src/ILInspector.ControlFlow/` contains shared block-edge, dominance, and dataflow kernels used below Analysis and Decompiler without depending on either.
- `src/ILInspector.Findings/` contains the domain-free observation, inspection, matching, transition, comparison, whole-census correlation, and exact-identity correlation contracts shared by product producers. The `timeline` command composes Metadata and Analysis producers over those same correlation contracts.
- `src/ILInspector.ILDiff/` owns IL body and assembly comparison over decoded
  instruction streams: canonicalization, alignment, Finding projection, typed
  failures, and producer-owned diff presentation.
- `src/ILInspector.Instructions/` is the shared IL decode + EH-aware basic-block substrate (one decoder the analyzer and decompiler converge onto); see [instruction substrate](design/instruction-substrate.md).
- `src/ILInspector.Text/` provides the reusable `TextFindings` API for exact, ordered line inspection and generic text comparison on the shared Finding spine.
- `src/DotnetInspector.Packages/` handles NuGet package extraction, package/source caches, feeds, symbol package acquisition, and version resolution.
- `src/DotnetInspector.PackageQueries/` is the optional package-aware query
  companion. It consumes package realization proofs and package-neutral core
  queries without adding package identity or acquisition policy to those core
  query results.
- `src/DotnetInspector.Artifacts/` is the package- and Metadata-free contract
  floor for generation-scoped artifact identity, typed provenance and
  diagnostics, acquisition outcomes, and owner-issued guarded access.
- `src/DotnetInspector.Services/` contains shared services such as assembly-set
  and PDB acquisition, platform/package resolution, dependency resolution,
  signatures, SourceLink availability/integrity operations, source fetching,
  and nuspec parsing. It owns the accepted package/metadata XML structure
  defined by [nuspec structural compatibility](design/nuspec-structural-compatibility.md);
  Queries owns manifest identity, dependency validation, and resource policy.
- `src/DotnetInspector.Core/` is the reference-free tool runtime kernel beneath
  Packages, Services, and the CLI: cache roots and eviction (`CoreCache`,
  `AsyncCache`), the single `HttpClientFactory` seam with offline and
  network-policy enforcement, network telemetry, and hardened XML/JSON readers.
- `src/ILInspector.Decompiler/` emits lowered C#, raw IL, and structural annotated IL from method bodies.
- `src/ILInspector.Research/` owns the offset-keyed fact overlay above Analysis and Decompiler: its registry orders fact producers, joins R1 analysis occurrences with R2 decompiler projections, and projects facts into the Annotated Source, annotated IL, and Facts views used by `member`.
- `prototypes/annotated-source-viewer/` is the dependency-free browser consumer
  for `AnnotatedSourceDocument`: it derives lines from the canonical text buffer,
  resolves facts through targets to multi-span nodes, filters the stable node-kind
  vocabulary, and keeps unanchored facts visible without inventing coordinates.
- `prototypes/inspect-web/` is the browser/Wasm product host. Its
  [UI design](design/inspect-web-ui.md) owns the website's shared presentation
  and interaction language while individual components retain rendering,
  binding, and state-transition responsibilities.
- `tools/DecompilerHarness/` owns ReturnToSender closure discovery,
  type-cluster planning, compile-back reference selection and closure, and
  generated-artifact admission and receipt-gated verdict composition. RTS
  specifies the required Metadata/CSharp request shape and consumes
  owner-issued artifact, fragment, and correspondence evidence;
  `ILInspector.CSharp`, `ILInspector.Decompiler`, and `ILInspector.ILDiff`
  retain ownership of producing that evidence.
- [Repository xUnit test host](design/xunit-test-host.md) owns explicit test
  selection non-vacuity for the argument vector handed to xUnit after any
  suite-owned expansion. xUnit retains command-line parsing, discovery,
  filtering, execution, reporting, and Microsoft Testing Platform protocol
  behavior.
- [`docs/design/ts-jsexport.md`](design/ts-jsexport.md) owns the `ts-jsexport`
  TypeScript facade projected at build time from an
  `ILInspector.JsExportSurface`. The host-side tool consumes that evidence
  without entering the inspected application's browser dependency closure,
  emits one opinionated TypeScript module, and leaves compilation and
  publication to the consumer.
- [`docs/design/inspect-web-jsexport-partitioning.md`](design/inspect-web-jsexport-partitioning.md)
  owns the inspect-web production facade partition: exact assignment of
  browser-host exports to generated capability modules, one-runtime
  initialization and entry-point composition, module-local wire DTO ownership,
  generated-artifact coverage, and complete multi-module deployment evidence.
  Product-layer documents retain ownership of the operations and facts those
  L3 adapters expose.
- [`docs/design/inspect-web-managed-operation-bridge.md`](design/inspect-web-managed-operation-bridge.md)
  owns dynamic managed-operation admission, keyed cooperative cancellation,
  first-reason fidelity, operation-scoped progress callback release, typed
  terminal envelopes, shared-waiter detachment, and epoch-work lease identity
  at the inspect-web worker-to-managed boundary.
- [`docs/design/inspect-web-worker-runtime.md`](design/inspect-web-worker-runtime.md)
  owns the long-lived inspect-web Web Worker epoch, bootstrap readiness, held
  starts, closed protocol, replay validation, liveness accounting, draining,
  hard termination, and worker-realm release.
- [`docs/design/custom-attribute-value-decoding.md`](design/custom-attribute-value-decoding.md)
  owns the safety contract for decoding custom-attribute values
  from untrusted metadata: the alignment, bounding, and guard-work invariants
  relating `CustomAttributeValueGuard` to SRM's decode, the two
  width-resolution paths and the distinct mechanism each uses to stay in
  agreement, and the bound, charging, and refusal semantics.
  `SignatureBlobGuard` retains its structural signature bounds.

## Engineering guidance

[AGENTS.md](../AGENTS.md) is the source of truth for repository-wide
engineering and workflow rules. This document describes subsystem ownership;
use the task map in `AGENTS.md` to find the focused guidance for a change.

## Important systems

- [Inspection space architecture](inspection-space.md): the target Rich, Fast, and Safe core that will be shared by hosts and inspection producers.
- [Artifact acquisition and workspace composition](design/artifact-acquisition-and-workspaces.md):
  the target separation between storage, source adapters, multi-source
  workspace lifetimes, packages, and assembly inspection.
- [Assembly image lifetime and MVID correctness](design/assembly-image-lifetime.md):
  the single-image inspection lifetime, source-specific cache scope, and
  non-cryptographic role of MVID-scoped metadata addresses.
- [Architecture](architecture.md): host-neutral composition,
  logical layers, project regions, currencies, and code-navigation map.
- [CLI host architecture](cli-architecture.md): command-host responsibilities,
  request lifetime, selection, and presentation composition.
- [Repository xUnit test host](design/xunit-test-host.md): semantic
  non-vacuity for explicit test selections after suite-owned argument
  expansion, while preserving xUnit-owned discovery, execution, reporting, and
  server dispatch.
- [Find type-search service](design/find-search-service.md): CLI-scoped
  candidate collection, classification precedence, source ordering, limits,
  failure visibility, and typed result boundary for `find`.
- [Inspection layers](design/inspection-layers.md): layer split for multiple consumers, vocabulary, and seam rules.
- [Compiled inspection domain composition](design/section-pipeline.md#compiled-inspection-domain-composition):
  L1/L2 binding from one immutable typed-query domain to reusable compiled
  section lenses and caller-owned execution contexts.
- [Analysis surfaces and universes](design/analysis-surfaces-and-universes.md):
  host-neutral request topology separating report surface, finite evidence
  universe, targeted/census mode, capability introspection, and result
  projection without owning producer semantics or presentation.
- [Analysis universe realization](design/analysis-universe-realization.md):
  operation-scoped binding from one exact finite universe description and
  validated plan to owner-issued executable capabilities, deterministic
  population and context access, retained lifetimes, and visible failure.
- [`ts-jsexport` TypeScript facade generation](design/ts-jsexport.md): ownership,
  type views, compiler handoff, related generator categories, and migration from
  direct JavaScript plus declaration emission.
- [Member inspection planning and metadata projection](design/member-inspection-planning-and-metadata-projection.md):
  proposed separation of type/member intent, section resolution, producer
  authorization, shared declaration validation, and C# representability.
- [Inspection graph document](design/inspection-graph-document.md): typed
  multi-subject graph projection for calls, metadata relationships,
  integrations, Findings, characteristics, and package/type lenses.
- [Inspection graph modes](design/inspection-graph-modes.md): single-seed,
  peer-seed, and induced-set requests over member, type, assembly, and package
  subjects.
- [Call graph characteristics](design/call-graph-characteristics.md):
  call-specific mapping from current topology, signals, loop state, and
  physical occurrences into the inspection-graph descriptor model.
- [Type, member, and API representation](design/type-member-api-representation.md): authoritative currency map for lookup, shape, identity, correspondence, location, selectors, and display.
- [State-machine relationship index](design/state-machine-relationship-index.md):
  Metadata-owned kickoff, state-machine type, implementation-method, and typed
  structural-failure relationships shared by higher layers.
- [Structured type-forwarding resolution](design/type-forwarding-resolution.md): typed reference-to-definition resolution, forwarding evidence, binding policy, outcomes, and consumer migration.
- [Signals](assembly-audit.md): package/library signal semantics and network scope.
- [PDB acquisition](pdb-acquisition.md): symbols and SourceLink acquisition.
- [Untrusted data threat model](design/untrusted-data-threat-model.md): trust boundaries and security rules for inspected artifacts, network input, caches, output paths, and rendering.
- [Inspect-web TypeScript semantic facts](design/inspect-web-typescript-semantic-facts.md):
  one pinned TypeScript project snapshot exposed through repository-owned
  semantic handles, queries, and explicit failure results.
- [Inspect-web operation authority](design/inspect-web-operation-authority.md):
  page-wide operation identity plus per-view logical outcome, cancellation,
  publication authority, disposal, and producer quiescence.
- [Inspect-web managed operation bridge](design/inspect-web-managed-operation-bridge.md):
  dynamic active-operation admission, keyed cancellation-token signaling,
  progress callback release, typed managed outcomes, shared-waiter detachment,
  and epoch-work lease handoff.
- [Inspect-web worker runtime](design/inspect-web-worker-runtime.md):
  worker epochs, bootstrap readiness, held operation dispatch, closed protocol
  validation, liveness and replay accounting, draining, restart, and hard
  worker-realm release.
- [Bounded metadata traversal](design/bounded-metadata-traversal.md): cycle, depth, count, text-budget, failure, and verification rules for artifact-derived metadata graphs.
- [Rendering model](design/rendering-model.md): output mode and verbosity design.
- [Progressive disclosure](design/progressive-disclosure.md): base/domain scope,
  discovery budgets, `-D`/`-S`, capabilities, and limiter behavior.
- [Item and line selection composition](design/item-and-line-limits.md):
  cross-component sequencing and typed handoffs for focused semantic
  selection, L2, source-execution, CLI, payload, and presentation designs.
- [Semantic row selection](design/semantic-row-selection.md): dependency-free
  ordered-stage, strict-window, reindexing, and all-or-failure sequence
  component.
- [Command transitions](design/command-transition-model.md): when source, focus, operation arity, lens, traversal, or rendering changes should switch commands versus stay within one command.
- [Row query and ordering](design/row-query-order.md): typed predicate and
  order resolution, baseline ordering, and per-`Top` ranking identities.
- [Section-row shaping](design/section-row-shaping.md): typed declared-row-set
  binding, projection roles, terminal Count, and result binding.
- [Source delegation](design/source-delegation.md): delegated source
  execution — the effect protocol, result algebra, completion-evidence
  binding, and exact upstream Count acceptance.
- [Product vocabulary](design/vocabulary.md): sectioned, host-neutral legal query values shared by CLI and browser/WASM.
- [View Facet Registry](design/view-facet-registry.md): stable product-owned
  inspection-facet identities, labels, order, structural applicability,
  discovery, and typed resolution outcomes.
- [Inspection subject navigation](design/inspection-subject-navigation.md):
  host-neutral root, Library, Type, and Member descriptors, availability,
  initial recommendations, transitions, reconciliation, and model-checked
  retained-session authority.
- [Inspect Web UI](design/inspect-web-ui.md): shared website control states,
  interaction grammar, and visual composition rules.
- [Annotated Source viewer interaction](design/annotated-source-viewer-interaction.md):
  viewer-local disclosure, actions, selection, annotations, media, Escape, and
  focus inside the embedded reader and modal viewer.
- [Analysis UX scopes](design/analysis-ux-scopes.md): shared analysis vocabulary across offset, member, type, and library scopes.
- [Memory-safety models and evidence](design/memory-safety-models.md):
  v1/v2 vocabulary and composition of project policy, binary contracts,
  implementation evidence, and provenance.
- [IL coordinate workflows](design/il-coordinate-workflows.md): prototype workflows for explaining sparse runtime coordinates from debugger, profiler, or analyzer artifacts.
- [IL Diff canonicalization](design/il-diff-canonicalization.md): current `CanonicalIlOperation` guarantees, boundaries, and extension points.
- [Finding nomenclature](design/finding-nomenclature.md): observation/change semantics, operation outcomes, and Research composition boundaries.
- [Finding producer design](design/finding-producers.md): how to choose owners, payloads, identities, result shapes, and matching modes.
- [Finding coordinates](design/finding-coordinates.md): separation of subject identity, correspondence, optional producer order, and typed provenance.
- [Finding value semantics](design/finding-value-equality.md): .NET equality
  and hashing for Finding-owned structural values, ordered collections,
  identity sets, union cases, and reference-identity operation objects.
- [Finding adoption](design/finding-adoption.md): consumer migration, failure visibility, native-case presentation, and quality-gate rules.
- [Source Finding producers](design/source-finding-producers.md): portable-PDB source/build-context inputs, outputs, identities, and migration boundaries.
- [Implementation Diff](design/implementation-diff.md): product C# + IL/body diff projection shared by the opt-in `diff` section, RTS, and harnesses.
- [C# assembly round-trip testing](design/csharp-member-recompilation.md): proposed tools-only `cluster`/`all` artifact compilation and layered IL/C# comparison.
- [Fixture governance](fixture-governance.md): fixture catalog, project-boundary, and semantic-axis rules.
- [Integrations](design/integrations.md): library ecosystem integration roll-ups and focused API currency.
- [Section model](design/section-model.md): section selection and query behavior.
- [Capability section registry spike](design/capability-section-registry-spike.md): measured static lambda-table and precompiled-plan pilot layered on `SectionPipeline`.
- [Hidden-fact annotations](design/hidden-fact-annotations.md): offset-keyed fact overlay semantics, validation, and projections.
- [Caret stacking](design/caret-stacking.md): `--focus` caret display model — one numbered caret per fact extent, with the fact texts listed below.
- [Member Index](design/member-index.md): overload selector and digest contract.
- [Member target resolution](design/member-target-resolution.md): typed member selector, anchor, and body-target resolution.
- [Member ordering](design/member-order.md): canonical type/member section order and member-kind mapping.
- [Package source model](design/package-source-model.md): source eligibility,
  mapping, local stores, source-bound caches, selection, and enrichment.
- [Package dependency evidence](design/package-dependency-evidence.md):
  normalized declared dependency observations across typed package-manifest
  and restored-project inputs, additive resolution and owner evidence,
  cross-input equivalence, completion, and query-result `InertString`
  containment.
- [NuGetFetch source-result identity](design/browser-package-sources.md#nugetfetch-typed-source-result-identity):
  credential-free producer provenance, caller association, transport evidence,
  factory-bound result propagation, and safe retained failures. It consumes
  normalized endpoint and path-redaction handoffs; package composition, cache
  authority, presentation, and post-return stream failures remain with their
  focused owners.
- [NuGetFetch operation deadlines](design/browser-package-sources.md#timeout-ownership):
  one reusable monotonic operation context, nested request deadlines, typed
  timeout identity, and source-safe post-return stream failures. It consumes
  source-result identity; source eligibility, failover policy, cache behavior,
  and presentation remain with their focused owners.
- [Version resolution](design/version-resolution.md): package/platform version and cache behavior.
- [Cache concurrency and publication](design/cache-concurrency.md): process-local single-flight, atomic publication, dependency overlap, and filesystem guarantees.
- [Skill guidance taste](../taste/skill-guidance.md): how to maintain the embedded agent skill.
