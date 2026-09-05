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
  [CLI row-selection grammar](design/cli-row-selection.md) owns item, Window,
  Top, direction, rendered-line spelling, shorthand, capability, and typed
  operation-intent lowering at the L3 boundary. Its
  [search scope resolution](design/search-scope-resolution.md) owns default
  activation,
  explicit-source suppression, and named platform/package scope expansion. Its
  [Find type-search service](design/find-search-service.md) owns the
  CLI-scoped boundary from host-authorized candidate collection through typed
  exact, glob, namespace-prefix, partial, and miss classification; Metadata
  retains candidate facts and the command retains presentation.
  The target
  [dependency inspection command](design/dependency-inspection-command.md)
  owns asset admission, traversal intent, evidence disclosure, graph row
  currency, and retirement of the separate `dependency-evidence` command.
  The [package index cache](design/package-index-cache.md) separately owns
  whether a persistent filesystem-derived package result may replace cold
  inspection of one exact authorized retained payload.
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
  source query owns a Decompiler fallback over retained assembly content; the
  proposed
  [member source comparison query](design/member-source-comparison-query.md)
  owns an explicit two-endpoint attempt over one resolved member. The project
  has no Markout, console, or filesystem-path dependency.
- `src/DotnetInspector.ResearchQueries/` contains the optional Research-backed
  L1 query family. It composes switch metadata with AppContext IL evidence,
  compares already-acquired Analysis body indexes, and compares retained
  implementation assembly content, returning typed results without pulling
  Research into the core query assembly. Its target
  [workspace Research target composition](design/research-workspace-target-composition.md)
  joins a facade's Metadata forwarding outcome through the sealed
  Queries-to-Research population receipt to one already admitted terminal
  Research attempt.
- `src/ILInspector.Metadata/` reads PE metadata and portable-PDB structure: named documents, checksums, sequence-point relationships/ranges, raw custom-debug-information blobs, API surfaces, method classification, authenticated [state-machine relationships](design/state-machine-relationship-index.md), assembly details, and the sibling [ReadyToRun image projection](design/readytorun-image-projection.md) for PE-envelope discovery, headers, and section directories. `MetadataFindings` projects API and portable-PDB build-context observations onto the shared Finding spine while retaining compatibility classification through `ApiDiff`.
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
- `src/ILInspector.Findings/` contains the domain-free observation, inspection, matching, transition, comparison, complete analysis-diff, whole-census correlation, and exact-identity correlation contracts shared by product producers. The `timeline` command composes Metadata and Analysis producers over those same correlation contracts.
- `src/ILInspector.ILDiff/` owns IL body and assembly comparison over decoded
  instruction streams: canonicalization, alignment, Finding projection, typed
  failures, and producer-owned diff presentation.
- `src/ILInspector.Instructions/` is the shared IL decode + EH-aware basic-block substrate (one decoder the analyzer and decompiler converge onto); see [instruction substrate](design/instruction-substrate.md).
- `src/ILInspector.Text/` provides the reusable `TextFindings` API for exact, ordered line inspection and generic text comparison on the shared Finding spine.
- `src/DotnetInspector.Packages/` handles NuGet package extraction,
  package/source caches, feeds, symbol package acquisition, and version
  resolution.
- `src/DotnetInspector.Ecosystems/` is the static front-end application
  catalog. The [Package Set Registry](design/package-set-registry.md) reuses
  Packages-owned coordinate currency and validation while stable set identity,
  private shipped inventory, discovery, and lookup live here. The
  [Ecosystem Pack](design/ecosystem-packs.md) pattern supplies shipped pack
  metadata and product-demo source content from this assembly while Workspace
  Definitions retains demo records, resolution, run plans, and execution. Only
  the CLI and the managed inspect-web facade may consume this application
  assembly; reusable Queries, Packages, Services, Metadata, and browser Core do
  not reference it.
- `src/DotnetInspector.PackageQueries/` is the optional package-aware query
  companion. It consumes package realization proofs and package-neutral core
  queries without adding package identity or acquisition policy to those core
  query results. Its proposed, design-locked but not yet implemented
  [Package Query assembly-pattern
  evaluation](design/package-query-assembly-evaluation.md) owner defines
  bounded one-candidate primary-assembly evaluation and resource-free
  package-plus-selected-asset semantic evidence without realizing unrelated
  package assemblies.
- `src/DotnetInspector.Artifacts/` is the package- and Metadata-free contract
  floor for generation-scoped artifact identity, typed provenance and
  diagnostics, acquisition outcomes, and owner-issued guarded access.
- `src/DotnetInspector.Services/` contains shared services such as assembly-set
  and PDB acquisition, platform/package resolution, dependency resolution,
  signatures, SourceLink availability/integrity operations, source fetching,
  and nuspec parsing. It owns the accepted package/metadata XML structure
  defined by [nuspec structural compatibility](design/nuspec-structural-compatibility.md);
  Queries owns manifest identity, dependency validation, and resource policy.
  Its [package metadata persistence](design/package-metadata-persistence.md)
  contract defines when one authority-scoped, time-bounded metadata observation
  may replace a fresh metadata operation.
  `LocalRepoSourceAcquisition` owns [local repository source
  acquisition](design/local-repository-source-acquisition.md): when a
  caller-supplied Git clone may satisfy one PDB document request with verified
  bytes, or decline so acquisition can continue.
- `src/DotnetInspector.Core/` is the reference-free tool runtime kernel beneath
  Packages, Services, and the CLI: cache roots and eviction (`CoreCache`,
  `AsyncCache`), the single `HttpClientFactory` seam with offline and
  network-policy enforcement, network telemetry, and hardened XML/JSON readers.
- `src/ILInspector.Decompiler/` emits lowered C#, raw IL, and structural annotated IL from method bodies.
- `src/ILInspector.Research/` owns the offset-keyed fact overlay above Analysis
  and Decompiler: its registry orders fact producers, joins R1 analysis
  occurrences with R2 decompiler projections, and projects facts into the
  Annotated Source, annotated IL, and Facts views used by `member`.
  [Research Finding census projection](design/research-finding-census-projection.md)
  owns preservation of one producer-sealed body-fact receipt and its instance
  keys across those projections.
- `prototypes/annotated-source-viewer/` is the dependency-free browser consumer
  for `AnnotatedSourceDocument`: it derives lines from the canonical text buffer,
  resolves facts through targets to multi-span nodes, filters the stable node-kind
  vocabulary, and keeps unanchored facts visible without inventing coordinates.
- `prototypes/inspect-web/` is the browser/Wasm product host. Its
  [UI design](design/inspect-web-ui.md) composes the website's shared
  presentation language, reusable
  [SlideStrip](design/inspect-web-slide-strip.md), navigation rendering,
  navigation-result consumer, shell interaction, and page-level composition
  across six focused owners while individual components retain rendering,
  binding, and state-transition responsibilities.
- [Inspect Web Method Body Comparison](design/inspect-web-method-body-comparison.md)
  owns explicit same-assembly pair interaction, the managed feature projection,
  and typed Method Body Diff presentation. It consumes Queries comparison,
  existing member resolution, modal behavior, and operation lifetime without
  redefining those owners.
- `tools/DecompilerHarness/` owns ReturnToSender closure discovery,
  type-cluster planning, compile-back reference selection and closure, and
  generated-artifact admission and receipt-gated verdict composition. RTS
  specifies the required Metadata/CSharp request shape and consumes
  owner-issued artifact, fragment, and correspondence evidence;
  `ILInspector.CSharp`, `ILInspector.Decompiler`, and `ILInspector.ILDiff`
  retain ownership of producing that evidence.
- [Committed authored-corpus history](design/authored-corpus-history.md) owns
  admission of one complete EVIL benchmark artifact as a durable observation
  and validity of the ordered committed observation sequence. Benchmark
  production, methodology, ratchet comparison, and history-card rendering
  remain separate concerns.
- [Source-oracle candidate ledger](design/source-oracle-candidate-ledger.md)
  owns whether one candidate-discovery run can publish denominator-complete
  file verdicts and a deterministic next-enrollment ranking against one
  accepted baseline. PDB mapping, source acquisition, oracle evaluation,
  enrollment policy, and presentation remain separate concerns.
- [Repository xUnit test host](design/xunit-test-host.md) owns the repository's
  use of Microsoft Testing Platform for aggregate non-vacuity of xUnit test
  execution. MTP and xUnit retain runner semantics; suite owners retain
  argument expansion and any stronger per-selection evidence receipts.
- [Repository CI change plan](design/ci-change-plan.md) owns candidate
  provenance, exact changed-path interpretation, path and event routing
  implications, and one immutable validation plan with bounded scoped evidence.
  Workflow YAML transports and places selected validation, while jobs retain
  validation semantics, execution, and results.
- [`docs/design/ts-jsexport.md`](design/ts-jsexport.md) owns the `ts-jsexport`
  TypeScript facade projected at build time from an
  `ILInspector.JsExportSurface`, plus the producer context that selects a
  closed set of independent facade roots. The host-side tool consumes that
  evidence without entering the inspected application's browser dependency
  closure; only its dependency-free root-attribute contract may enter the
  producer graph. It emits one opinionated TypeScript module per root and
  leaves public module naming, compilation, composition, and publication to
  the consumer.
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
- [`docs/design/inspect-web-async-composition.md`](design/inspect-web-async-composition.md)
  owns the user-scenario ordering and typed handoffs across operation
  authority, worker runtime, generated facades, managed bridging, and
  feature-owned work without redefining those owners.
- [`docs/design/engine-browser-async-event-stream.md`](design/engine-browser-async-event-stream.md)
  owns host-neutral, request-scoped engine event sequences: advisory progress,
  durable partial items and item failures, one semantic completion, and
  adapter-side pull, batching, and cancellation obligations before Browser
  publication.
- [`docs/design/custom-attribute-value-decoding.md`](design/custom-attribute-value-decoding.md)
  owns the safety contract for decoding custom-attribute values
  from untrusted metadata: the bounding, fail-closed, and fidelity invariants
  for a decoder this repository will own, the format's adversarial properties
  that force them, the two width-resolution paths, and the bound, charging, and
  refusal semantics. That contract is **prescriptive ahead of the
  implementation**: until #5288's slice 2 lands, `AttributeDecoder` still calls
  SRM's `CustomAttribute.DecodeValue` behind `CustomAttributeValueGuard`, and
  SRM becomes a test-time fidelity oracle only when it does.
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
- [Workspace scope and expansion](design/workspace-scope-and-expansion.md):
  committed logical Root membership and order, closed-by-default selective
  dependency expansion, scope revisions, logical limits, and complete
  scope-operation results.
- [Assembly image lifetime and MVID correctness](design/assembly-image-lifetime.md):
  the single-image inspection lifetime, source-specific cache scope, and
  non-cryptographic role of MVID-scoped metadata addresses.
- [Architecture](architecture.md): host-neutral composition,
  logical layers, project regions, currencies, and code-navigation map.
- [CLI host architecture](cli-architecture.md): command-host responsibilities,
  request lifetime, selection, and presentation composition.
- [CLI change classification and obsolete
  inputs](design/cli-change-classification.md): published surfaces, change
  disclosure, routing-collision analysis, invalid-input guards, and
  reservations.
- [Search scope resolution](design/search-scope-resolution.md): default
  activation, explicit-source suppression and composition, and named
  platform/package scope expansion for search commands.
- [Repository xUnit test host](design/xunit-test-host.md): MTP-owned aggregate
  non-vacuity for xUnit execution, with stronger per-selection evidence left
  to the suite that makes that claim.
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
- [Local repository source acquisition](design/local-repository-source-acquisition.md):
  local Git locator interpretation, checksum-backed byte admission, optional
  decline, and execution-limit evidence.
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
- [Inspect-web async composition](design/inspect-web-async-composition.md):
  scenario-level sequencing, owner-issued handoffs, browser/.NET/Rust semantic
  distinctions, gate ownership, and focused migration order.
- [Bounded metadata traversal](design/bounded-metadata-traversal.md): cycle, depth, count, text-budget, failure, and verification rules for artifact-derived metadata graphs.
- [Rendering model](design/rendering-model.md): output mode and verbosity design.
- [Progressive disclosure](design/progressive-disclosure.md): base/domain scope,
  discovery budgets, `-D`/`-S`, capabilities, and limiter behavior.
- [Item and line selection composition](design/item-and-line-limits.md):
  cross-component sequencing and typed handoffs for focused semantic
  selection, L2, source-execution, CLI, payload, and presentation designs.
- [Semantic row selection](design/semantic-row-selection.md): typed
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
- [Package Set Registry](design/package-set-registry.md): front-end-only static
  application identity, labels, purposes, order, exact lookup, and immutable
  ordered package-coordinate membership over the reusable package owner's
  coordinate validation.
- [Static Ecosystem Packs](design/ecosystem-packs.md): the proposed
  front-end-only application catalog, private source contribution shape, and
  static shipped-pack manifest that compose package-set identity, typed
  package-prefix requests, and opaque Integration-owned semantic-scanner
  bindings without making reusable infrastructure depend on the catalog.
- [Inspection subject navigation](design/inspection-subject-navigation.md):
  host-neutral Workspace, Package or non-package Root, Library, Type, and
  Member descriptors, availability, initial recommendations, transitions,
  reconciliation, and model-checked retained-session authority.
- [Inspect Web UI](design/inspect-web-ui.md): composition map for the website
  redesign, linking
  [presentation language](design/inspect-web-presentation-language.md),
  [SlideStrip](design/inspect-web-slide-strip.md),
  [navigation presentation](design/inspect-web-navigation-presentation.md),
  [navigation consumer](design/inspect-web-navigation-consumer.md),
  [shell interaction](design/inspect-web-shell-interaction.md), and
  [surface composition](design/inspect-web-surface-composition.md).
- [Annotated Source viewer interaction](design/annotated-source-viewer-interaction.md):
  viewer-local disclosure, actions, selection, annotations, media, Escape, and
  focus inside the embedded reader and modal viewer.
- [Annotated Source invocation destinations](design/annotated-source-invocation-destinations.md):
  Research composition of physical direct calls, Decompiler-issued invocation
  nodes, and CallGraph-owned typed targets.
- [Analysis UX scopes](design/analysis-ux-scopes.md): shared analysis vocabulary across offset, member, type, and library scopes.
- [Memory-safety models and evidence](design/memory-safety-models.md):
  v1/v2 vocabulary and composition of project policy, binary contracts,
  implementation evidence, and provenance.
- [IL coordinate workflows](design/il-coordinate-workflows.md): prototype workflows for explaining sparse runtime coordinates from debugger, profiler, or analyzer artifacts.
- [IL Diff canonicalization](design/il-diff-canonicalization.md): current `CanonicalIlOperation` guarantees, boundaries, and extension points.
- [Finding nomenclature](design/finding-nomenclature.md): observation/change semantics, operation outcomes, and Research composition boundaries.
- [Finding producer design](design/finding-producers.md): how to choose owners, payloads, identities, result shapes, and matching modes.
- [Finding coordinates](design/finding-coordinates.md): separation of subject identity, correspondence, optional producer order, and typed provenance.
- [Finding instance census](design/finding-instance-census.md): producer-issued
  receipt and per-instance keys for one sealed execution census, with
  bijection and exact-association validation.
- [Research Finding census projection](design/research-finding-census-projection.md):
  Research preservation of one body-fact census through Facts and Annotated
  Source.
- [Finding value semantics](design/finding-value-equality.md): .NET equality
  and hashing for Finding-owned structural values, ordered collections,
  identity sets, union cases, and reference-identity operation objects.
- [Finding adoption](design/finding-adoption.md): consumer migration, failure visibility, native-case presentation, and quality-gate rules.
- [Source Finding producers](design/source-finding-producers.md): portable-PDB source/build-context inputs, outputs, identities, and migration boundaries.
- [Member source diff presentation](design/member-source-diff-presentation.md):
  canonical placement-aligned endpoint text, source-line analysis and statistics,
  Markout mapped-text lowering, and the CLI Source Diff first adoption.
- [Implementation Diff](design/implementation-diff.md): product C# + IL/body diff projection shared by the opt-in `diff` section, RTS, and harnesses.
- [C# assembly round-trip testing](design/csharp-member-recompilation.md): proposed tools-only `cluster`/`all` artifact compilation and layered IL/C# comparison.
- [Fixture governance](fixture-governance.md): fixture catalog, project-boundary, and semantic-axis rules.
- [Integrations](design/integrations.md): library ecosystem integration roll-ups and focused API currency; its [scanner binding](design/integration-scanner-binding.md) separates decoded observations from application-authored interpretation.
- [Section model](design/section-model.md): section selection and query behavior.
- [Capability section registry spike](design/capability-section-registry-spike.md): measured static lambda-table and precompiled-plan pilot layered on `SectionPipeline`.
- [Hidden-fact annotations](design/hidden-fact-annotations.md): offset-keyed fact overlay semantics, validation, and projections.
- [Caret stacking](design/caret-stacking.md): `--focus` caret display model — one numbered caret per fact extent, with the fact texts listed below.
- [Member Index](design/member-index.md): overload selector and digest contract.
- [Member target resolution](design/member-target-resolution.md): typed member selector, anchor, and body-target resolution.
- [Member ordering](design/member-order.md): canonical type/member section order and member-kind mapping.
- [Local package source identity](design/local-package-source-identity.md):
  config- and command-relative path resolution, path and `file://`
  canonicalization, host path equality, and the local identity consumed by
  mapping and cache authorization.
- [Local folder package source](design/local-folder-package-source.md):
  recognized general folder-feed layouts, capability semantics, bounded
  filesystem and archive observation, source outcomes, mutation handling, and
  caller-owned local payload streams.
- [Package source model](design/package-source-model.md): source eligibility,
  mapping, authority composition, source-bound caches, selection, and
  enrichment.
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
- [NuGet API selection](design/nuget.md#scenario-selection): scenario-to-resource
  decision guidance and evidence, including API combinations and first/last
  requested-result costs. This is not a new runtime selector; source, query,
  and host contracts remain with their focused owners.
- [NuGet Gallery discovery](design/nuget-gallery-discovery.md): proposed
  NuGetFetch-owned termless/type-filtered discovery, source ordering,
  search-selector catalog, typed metadata observations, and Gallery-specific
  row-delegation evidence. Row meaning, generic source contracts, and host
  adoption remain with their focused owners.
- [Version resolution](design/version-resolution.md): package/platform version and cache behavior.
- [Cache concurrency and publication](design/cache-concurrency.md): process-local single-flight, atomic publication, dependency overlap, and filesystem guarantees.
- [Skill guidance taste](../taste/skill-guidance.md): how to maintain the embedded agent skill.
