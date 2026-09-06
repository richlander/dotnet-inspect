# dotnet-inspect Documentation

dotnet-inspect is a CLI tool for exploring .NET libraries and NuGet packages. It's designed for both humans and LLMs—the structured markdown output is easy to read and easy to parse.

The tool answers questions like:

- What methods does `JsonSerializer` have?
- What changed between v9 and v10 of a package?
- Where does this type come from?
- Was this library built by Microsoft or rebuilt by my distro?

Unlike decompilers, dotnet-inspect focuses on the **public API surface**—the contracts you code against, not implementation details. It pulls from multiple sources (libraries, PDBs, symbol servers, NuGet metadata) to give you a complete picture.

## Quick Example

```bash
$ dotnet-inspect type JsonSerializer --package System.Text.Json --shape

# System.Text.Json.JsonSerializer (System.Text.Json 10.0.2)

System.Text.Json.JsonSerializer (System.Text.Json 10.0.2)
   ├─ string Serialize<TValue>(TValue value, JsonSerializerOptions? options = null)
   ├─ string Serialize(object? value, Type inputType, JsonSerializerOptions? options = null)
   ├─ void Serialize<TValue>(Stream utf8Json, TValue value, JsonSerializerOptions? options = null)
   └─ ...
```

## Documentation

### Current system docs

| Document | Need served |
| -------- | ----------- |
| [Inspection Space Architecture](inspection-space.md) | Target core workspace, query, acquisition, join, cache, and safety architecture organized around Rich, Fast, and Safe. |
| [Overview](overview.md) | Minimum system and architecture context for humans and agents. |
| [Architecture](architecture.md) | Current host-neutral composition, logical layers, project regions, currencies, and code-navigation map. |
| [CLI Host Architecture](cli-architecture.md) | CLI command-host responsibilities, request lifetime, selection, and presentation composition. |
| [Decompiler Architecture](decompiler-architecture.md) | Decompiler project boundaries, import/IR/pass/printer flow, host consumers, and testing/evidence infrastructure. |
| [CLI Change Classification and Obsolete Inputs](design/cli-change-classification.md) | Published CLI surfaces, observable change classification, disclosure, invalid-input guards, and routing reservations. |
| [Dependency Inspection Command](design/dependency-inspection-command.md) | Target unification of dependency graph traversal and normalized evidence under one asset-driven `depends` operation. |
| [Search Scope Resolution](design/search-scope-resolution.md) | Default activation, explicit-source suppression and composition, and named platform/package scope expansion for search commands. |
| [Typed Source Intent](design/search-scope-domain.md) | Immutable source declarations, bounded package-prefix requests, and pure search normalization ahead of staged host adoption. |
| [Repository xUnit Test Host](design/xunit-test-host.md) | Microsoft Testing Platform execution and aggregate non-vacuity for repository xUnit executables. |
| [Repository CI Change Plan](design/ci-change-plan.md) | Typed candidate provenance, exact changed-path interpretation, immutable CI validation selection, scoped evidence, and visible planner refusal. |
| [Repository Dependency Policy](dependency-policy.md) | Evaluated project and compiled assembly dependency rules, JSON policy semantics, and the Release CI gate. |
| [LLM Design](llm-design.md) | Current agent-facing output and workflow design. |
| [Progressive Disclosure](design/progressive-disclosure.md) | Current model for base/domain scope, discovery budgets, `-D`/`-S`, capabilities, counts, and row limits. |
| [Bare `-S` Default View](design/info-view.md) | Bullseye questions and section profiles for curated high-density default views. |
| [Platform Components](platform-components.md) | Accessing SDK libraries vs NuGet packages. |
| [NuGet Package Structure](nuget-package-structure.md) | Compile and implementation asset roles, `ref`/`lib`/RID-specific selection, and explicit empty compile groups. |
| [Private NuGet Feeds](private-feeds.md) | How to give the tool access to a private feed: installing a credential provider, unattended and CI setup, and the `nuget.config` fallback. |
| [Signals](assembly-audit.md) | Understanding Signals output and network scope flags. |
| [SourceLink Exposure](sourcelink-exposure.md) | Where SourceLink appears in package/library/type/member flows and how PDB/network costs are controlled. |
| [PDB Acquisition](pdb-acquisition.md) | How symbols and SourceLink are resolved. |
| [Local Repository Source Acquisition](design/local-repository-source-acquisition.md) | When caller-supplied Git clones may provide checksum-verified PDB source; local locator meaning, decline/fallback, and execution limits. |
| [Sample References](sample-references.md) | Extracting code samples from XML docs. |
| [Reading IR Dumps](decompiler-ir-dumps.md) | How maintainers read DecompilerHarness per-pass IR dumps to diagnose decompiled output. |
| [Decompiler Correctness Pipeline](decompiler-correctness-pipeline.md) | The staged gauntlet of decompiler checks, from entry gates to changed-method fidelity. |

### Contributor docs

| Document | Need served |
| -------- | ----------- |
| [Style Guide](design/style-guide.md) | Output formatting conventions. |
| [Output Shapes](design/output-shapes.md) | The Document → Table → Vector → Scalar shape ladder, how Markout produces it, and how the output flags select a shape. |
| [Uncertified Scan Results](design/uncertified-scan-results.md) | How a command reports a multi-candidate scan that lost a candidate: exclusions named first, uncertainty carried beside the outcome, exit code `3`. |
| [Semantic Row Selection](design/semantic-row-selection.md) | Typed ordered Head, Tail, Window, and Top stages over complete logical sequences. |
| [CLI Row-Selection Grammar](design/cli-row-selection.md) | L3 item, Window, Top, direction, line-unit, shorthand, capability, and lowering rules for command-by-command adoption. |
| [Source Delegation](design/source-delegation.md) | Delegated source execution: the effect protocol, closed result algebra, completion-evidence bases, and equivalence gates for row handoff and exact upstream Count. |
| [Package Dependency Evidence](design/package-dependency-evidence.md) | Normalized declared dependency evidence, additive resolution and owner observations, cross-input equivalence, completion, and `InertString` query-result containment. |
| [Package Dependency Traversal](design/package-dependency-traversal.md) | Source-authorized exact package-manifest traversal with typed graph identity, root-relative depth, failures, completion, and shared CLI/Browser consumption. |
| [Dependency Evidence CLI](design/dependency-evidence-cli.md) | CLI command, input binding, sections, count exactness, Markout and JSON lowering, diagnostics, and routing for normalized dependency evidence. |
| [Restored Project Dependency Facts](design/restored-project-dependency-facts.md) | Host-neutral `project.assets.json` declarations, resolved package graph, typed identity, completion, failure, and containment. |
| [Projected JSON Output](design/projected-json.md) | Typed versus lowered JSON, section-scoped projection, representability, atomic output, and adoption gates. |
| [Inspection Graph Document](design/inspection-graph-document.md) | Typed multi-subject graph envelope for calls, metadata, integrations, Findings, occurrences, characteristics, and package/type lenses. |
| [Custom-Attribute Value Decoding](design/custom-attribute-value-decoding.md) | The bounding, fail-closed, and fidelity invariants for a custom-attribute decoder this repository will own, the format's adversarial properties, the two width-resolution paths, bounds, charging, and refusal semantics. Prescriptive ahead of the decoder (#5288 slice 2); SRM is still the production decoder today and becomes a test-time oracle when that lands. Nine known gaps recorded against the contract. |
| [Bounded Metadata Signature Decoding](design/metadata-signature-decoding.md) | Design-only, unverified node, materialization, and work-ledger bounds for decoding one artifact-authored metadata signature. |
| [Inspection Graph Modes](design/inspection-graph-modes.md) | Single-seed, peer-seed, and induced-set requests over member, type, assembly, and package subjects. |
| [Call Graph Characteristics](design/call-graph-characteristics.md) | Mapping current call nodes, edges, occurrences, signals, and loop state into the inspection-graph descriptor model. |
| [Graph Signal Annotations](design/graph-signal-annotations.md) | Projecting analysis signals (alloc/copy/unsafe, and exception-risk follow-ups) onto call-graph nodes via `--fields`. |
| [Allocation Triage Pre-Filters](design/allocation-triage-prefilters.md) | Which allocation candidates Performance Triage surfaces, why the pre-filters prune cold-by-construction shapes, and what realized cost the static side cannot predict. |
| [Finding Nomenclature](design/finding-nomenclature.md) | Canonical observation/change vocabulary, arity ladder, operation outcomes, and Research composition boundary. |
| [Finding Producer Design](design/finding-producers.md) | Choosing producer ownership, payloads, identities, result shapes, matching modes, and higher-rung boundaries. |
| [Finding Instance Census](design/finding-instance-census.md) | Producer-issued receipt and per-instance keys for one sealed Finding census, including exact-association validation. |
| [Research Finding Census Projection](design/research-finding-census-projection.md) | Preserving one producer-sealed body-fact census through Facts and Annotated Source without shape-derived identity. |
| [Member Source Presentation](design/member-source-presentation.md) | CLI presentation of one Research-issued Finding census across member Facts and Annotated Source output. |
| [Finding Value Semantics](design/finding-value-equality.md) | Equality and hashing for Finding-owned structural values, ordered collections, identity sets, union cases, and operation objects. |
| [Analysis Diff Format](design/analysis-diff.md) | Complete immutable two-version item sequences and exhaustive producer-issued N:M relations for shared CLI and browser/Wasm analysis. |
| [Comparison Document](design/comparison-document.md) | Portable root and subject composition for shared CLI/browser diffs and clone payloads, including referenced rename/move descriptions. |
| [Performance Analysis Baselines](analysis-baselines.md) | Internal baselines of what each analysis type finds over a fixed corpus, with effectiveness ratings for the one-stop-shop Performance Analysis view. |
| [Dynamic Leak-Watch](design/dynamic-leak-watch.md) | The retention axis: how `runfaster leak-watch` separates a managed leak from a churn storm from native/committed growth, and why static triage and the allocation-tick join cannot. |
| [Rendering Model](design/rendering-model.md) | Historical/current rendering model notes; prefer [Progressive Disclosure](design/progressive-disclosure.md) for current agent-facing behavior. |
| [Section Model](design/section-model.md) | Section selection design notes; use with [Progressive Disclosure](design/progressive-disclosure.md). |
| [View-Facet Registry](design/view-facet-registry.md) | View-facet identity and discovery: how facets are registered and looked up across CLI and browser hosts. |
| [Package Set Registry](design/package-set-registry.md) | Front-end-only static application identities, descriptors, and package membership over reusable package-coordinate validation. |
| [Static Ecosystem Packs](design/ecosystem-packs.md) | Front-end-only application catalog of private static ecosystem registrations composing discovery metadata with optional package-set, prefix-request, and opaque Integration scanner bindings. |
| [Integration Scanner Binding](design/integration-scanner-binding.md) | Integration-owned static scanner handoff over immutable decoded observations, preserving evidence and owner-controlled execution; catalog and host adoption remain staged. |
| [Workspace Scope and Expansion](design/workspace-scope-and-expansion.md) | Committed logical Root membership and order, closed-by-default selective dependency expansion, revision-bound edits, and complete scope-operation results. |
| [Approved Lazy Traversal](design/approved-lazy-traversal.md) | Proposed, operator-approved cross-owner experience: distinct subjects and traversal permissions, prefix/ecosystem knowledge, lazy demand, Browser defaults, and bounded prefix-only operations. |
| [Schema Query](design/schema-query.md) | `-D`/`-S` schema/query implementation notes. |
| [Query Vocabulary](design/vocabulary.md) | Shared static catalogs for legal query values across CLI and browser hosts. |
| [Hidden-Fact Annotations](design/hidden-fact-annotations.md) | Allocation/unsafety/lifetime annotation model and the static IL pair-agreement oracle strategy. |
| [Annotated Source Viewer Interaction](design/annotated-source-viewer-interaction.md) | Embedded-reader and modal-viewer disclosure, actions, selection, annotations, media, Escape, and focus behavior. |
| [Annotated Source Invocation Destinations](design/annotated-source-invocation-destinations.md) | Research composition of physical calls, Decompiler invocation provenance, and CallGraph-owned typed targets. |
| [Caret Stacking](design/caret-stacking.md) | `--focus` display model: one caret per fact extent, packed onto as few rows as fit, with the numbered fact texts listed below. |
| [Decompiler Inspection & Oracle](design/decompiler-inspection-oracle.md) | Unifies single-method inspection (dump/stages) with the corpus-wide fidelity check oracle; product-vs-tool scoping. |
| [Decompiler Name and Symbol Preservation](design/decompiler-symbol-preservation.md) | Artifact-backed identifier preservation, authenticated generated-name recovery, honest synthesis, tracked gaps, and irrecoverable source spellings, each with a fixture probe. |
| [ReturnToSender: Fact-Planned Compile-Back Harness](design/fact-planned-compile-back-harness.md) | Spec for a fresh tools-side compile-back harness with fact-planned TypeProducer/TypePrinter shells. |
| [Memory-Safety Models and Evidence](design/memory-safety-models.md) | v1/v2 vocabulary and composition of project policy, binary contracts, implementation evidence, and provenance. |
| [Method Body Inspection](design/method-body-inspection.md) | Target service seam for shared `member` and `library --il-offset` method-body facts and coordinate inspection. |
| [Member Body Substrate](design/member-body-substrate.md) | One base for skeleton/full/merged/diff body rendering: `ApiType` shape, `MemberAnchor` address, one scope, and `MemberBody`'s scalar (whole-body) and vector (offset-keyed) shapes. |
| [NuGet API Selection](design/nuget.md) | Scenario-to-API decisions, endpoint roles, first/last-result performance evidence, and current versus proposed adoption. |
| [NuGet Gallery Discovery](design/nuget-gallery-discovery.md) | Proposed NuGetFetch termless/type-filtered Gallery search, source orders, search-facet discovery, and bounded row-source delegation, with CLI/browser adoption tracked separately. |
| [NuGet Feed Authentication](design/nuget-authentication.md) | How feeds are authenticated: `nuget.config` credentials, credential provider discovery and the 401-driven plugin protocol, source-scoped plugin credential isolation, supported credential forms, and hermetic/live test tiers. See [Private NuGet Feeds](private-feeds.md) for setup instructions. |
| [Local Package Source Identity](design/local-package-source-identity.md) | Canonical config- and command-relative path identity shared by local source consumers. |
| [Local Folder Package Source](design/local-folder-package-source.md) | General V2/V3 folder-feed recognition, independent capabilities, bounded filesystem and archive observation, typed failures, and payload lifetime. |
| [Package Source Model](design/package-source-model.md) | Configured package authority, mapping, source-result adoption, aggregation, selection, and cache authorization. |
| [Package Payload Capacity](design/package-payload-capacity.md) | Awaited host-capacity reservation before response materialization, cancellation, and publication handoff. |
| [Version Resolution](design/version-resolution.md) | Package/platform version and cache behavior. |
| [Cache concurrency and publication](design/cache-concurrency.md) | Process-local single-flight, cross-process atomic publication, dependency overlap, and filesystem guarantees. |
| [Package Index Cache](design/package-index-cache.md) | Persistent filesystem-derived package inspection identity, completeness, freshness, validation, and reuse. |
| [Package Metadata Persistence](design/package-metadata-persistence.md) | Authority-scoped, time-bounded present and absent metadata observations, production completion, field-state preservation, and reuse. |
| [Assembly Inspection Query Model](design/assembly-inspection-query.md) | Target boundary where the CLI forms a query and the metadata/service layer resolves, opens, and returns the typed inspection result (why the CLI should not hold a `PEReader`). |
| [ReadyToRun Image Projection](design/readytorun-image-projection.md) | PE managed-native and `RTR_HEADER` discovery, validated R2R headers and section directories, manifest-metadata extent identification, bounds, and failure behavior. |
| [ReadyToRun CLI Projection](design/readytorun-cli-projection.md) | Explicit `@ReadyToRun` library sections and root-consistent `--metadata-root` selection for the existing `@Metadata` lens. |
| [Package Query Assembly-Pattern Evaluation](design/package-query-assembly-evaluation.md) | Proposed, design-locked, not-yet-implemented bounded one-candidate primary-assembly selection, semantic confirmation, resource-free evidence, and candidate-scoped release for shared CLI and Browser Package Query consumers. |
| [Find Type-Search Service](design/find-search-service.md) | CLI-scoped candidate collection and exact, glob, namespace-prefix, partial, and miss classification into typed results. |
| [Skill Guidance Taste](../taste/skill-guidance.md) | Good and bad examples for maintaining the embedded skill. |
| [Inspection Layers](design/inspection-layers.md) | Layering and consumer-boundary rules between Metadata, Analysis, CSharpText, CSharp, Research, and the CLI. |
| [Metadata Semantic Substrates](design/metadata-semantic-substrates.md) | Admission, typed outcomes, identity, evidence, bounds, and consumer boundaries for shared metadata-derived meaning. |
| [Workspace Research Target Composition](design/research-workspace-target-composition.md) | Queries-owned association from a workspace facade through Metadata forwarding evidence and the Queries-to-Research population receipt to one exact Research target attempt. |
| [Direct-member Comparison](design/direct-member-comparison.md) | Queries-owned designated local C#/IL comparison, explicit Research/publication prerequisites, and production adoption and legacy-retirement ledger. |
| [Local Comparison Publication](design/local-comparison-publication.md) | Queries-owned result association and terminal evidence for the first borrowed-input, two-host method-comparison route. |
| [Inspect Web Method Body Comparison](design/inspect-web-method-body-comparison.md) | Explicit same-assembly pair interaction, managed feature projection, and typed Browser Method Body Diff presentation. |
| [Analysis Universe Realization](design/analysis-universe-realization.md) | Operation-scoped binding from one exact finite analysis universe and validated plan to owner-issued executable capabilities, deterministic access, retained lifetimes, and visible failure. |
| [Artifact Acquisition and Workspaces](design/artifact-acquisition-and-workspaces.md) | How artifacts are acquired and composed into an inspection workspace. |
| [Inspect-web Managed Operation Bridge](design/inspect-web-managed-operation-bridge.md) | Dynamic managed-operation admission, keyed cancellation, progress callback release, typed outcomes, shared-waiter detachment, and epoch-work leases. |
| [Inspect-web Worker Runtime](design/inspect-web-worker-runtime.md) | Long-lived worker epochs, bootstrap readiness, held starts, closed protocol and replay validation, liveness, draining, restart, and hard realm release. |
| [Inspect-web Async Composition](design/inspect-web-async-composition.md) | Cross-owner scenarios, typed handoff order, runtime-semantics comparison, gate ownership, and focused migration dependencies. |
| [Engine-to-browser Async Event Streams](design/engine-browser-async-event-stream.md) | Host-neutral ordered progress, durable partial outcomes, one semantic completion, adapter backpressure, batching, and cancellation for engine streams consumed by Browser hosts. |
| [Inspect Web UI](design/inspect-web-ui.md) | Composition map for the website redesign: redesign summary, product dependencies, document map, cross-document relationships, and reference-product boundary. |
| [Inspect Web Presentation Language](design/inspect-web-presentation-language.md) | Reusable visual and accessibility language: selector-control states, progressive filter disclosure, shared subject-heading rules, and compact source-provenance presentation. |
| [Member Source Comparison Query](design/member-source-comparison-query.md) | Presentation-neutral two-endpoint member source acquisition, partial availability, cancellation, and binding-policy consistency. |
| [Selected Member Source Pair Query](design/member-source-pair-query.md) | Queries-owned authored-source comparison across two retained images, independent of local C#/IL changes, with explicit endpoint outcomes. |
| [Member Source Diff Presentation](design/member-source-diff-presentation.md) | Canonical placement-aligned endpoint projection, AnalysisDiff statistics, Markout lowering, and first adoption by CLI Source Diff. |
| [Inspect Web Source-diff Transport](design/inspect-web-source-diff-transport.md) | Proposed member source-diff feature payload admission, complete typed codec, bounded transfer, and adoption of existing worker liveness and cancellation. |
| [Inspect Web SlideStrip](design/inspect-web-slide-strip.md) | Reusable single-region ordered-item control with Label, optional Short Label and Icon, derived Index, whole-strip modes, contiguous windows, edge disclosure, and focus preservation. |
| [Inspect Web Navigation Presentation](design/inspect-web-navigation-presentation.md) | Rendering and interacting with product-issued coordinate, workspace, subject, hierarchy, Library, lens, and activation descriptors. |
| [Inspect Web Saved Workspaces](design/inspect-web-saved-workspaces.md) | Named browser-local Save/Open/Forget using canonical packets and the existing one-live-Workspace restoration path. |
| [Inspect Web Workspace Editing](design/inspect-web-workspace-editing.md) | Proposed Browser editor eligibility, explicit Save/Cancel, Inspect terminology, and dirty in-app navigation decisions; owner-backed save completion remains prerequisite work. |
| [Inspect Web Workspace Add package](design/inspect-web-workspace-add-package.md) | Focused package-search picker appending a resolved coordinate without replacing or evicting current members. |
| [Inspect Web Navigation Consumer](design/inspect-web-navigation-consumer.md) | Browser-side navigation-result consumer model: canonical location, browser history, transition lifecycle, effect authority, synchronization debt, and renderer/destination lifetimes. |
| [Inspect Web Shell Interaction](design/inspect-web-shell-interaction.md) | Persistent shell and shared transient/routed surface interaction: shell actions, menu/modal semantics, Spotlight Search, Open, Settings entry, and the command palette. |
| [Inspect Web Surface Composition](design/inspect-web-surface-composition.md) | Browser host page-level composition and placement: working surfaces, Unified Settings, package-source presentation, responsive composition, and the data bar and Diagnostics. |
| [Platform Composition and Overlays](design/platform-composition-and-overlays.md) | Platform library composition, overlays, and core-library entitlement. |
| [Type, Member, and API Representation](design/type-member-api-representation.md) | Canonical type, member, and API identity model. |
| [Member Signature Shape and Transport](design/member-signature-shape.md) | Non-authoritative signature correspondence: loss-policy rationale, caller obligations, alternatives, canonical `mss1` grammar, and evolution. |
| [C# Type-Declaration Identifier Admission](design/csharp-type-declaration-identifier-admission.md) | Compiler-characterized model-free admission from exact identity text to a legal C# declared-type identifier spelling or typed refusal. |
| [C# Declared-Type Self-Name Admission](design/csharp-declared-type-self-name.md) | Proposed typed admission from one exact Metadata leaf to the identifier shared by a type header, constructors, and finalizers. |
| [C# Memory-Safety Declaration Spelling](design/csharp-memory-safety-spelling.md) | Proposed CSharp-owned spelling of caller contracts independently from pointer syntax and body-context requirements. |
| [Source Finding Producers](design/source-finding-producers.md) | How source-derived Findings are produced. |
| [Untrusted Data Threat Model](design/untrusted-data-threat-model.md) | Trust boundaries, existing controls, and the security-scope rationale for untrusted internet-origin data. |
| [Finding Adoption](design/finding-adoption.md) | How Analysis, Findings, and Research compose. |
| [Call Graph Projection](design/call-graph-projection.md) | Projecting the inspection graph into a call graph. |
| [Instruction Substrate](design/instruction-substrate.md) | Shared IL/control-flow substrate consumed by Analysis and the Decompiler. |
| [TypeScript Facades for `[JSExport]`](design/ts-jsexport.md) | Generating TypeScript facades for JSExport members. |
| [Classic Async Request Adapter](design/classic-async-request-adapter.md) | Carries exact Metadata relationship evidence and owner failures into the Decompiler classic-inverse boundary. |
| [Classic Async Inverse Core](design/classic-async-reconstruction.md) | Proof-carrying reconstruction of authenticated classic async requests. |
| [Committed Authored-Corpus History](design/authored-corpus-history.md) | Admission, ordered observation addressing, sequence validity, provenance, compatibility, and consumer trust for the committed EVIL benchmark history. |
| [Source-Oracle Candidate Ledger](design/source-oracle-candidate-ledger.md) | Denominator-complete candidate-file verdicts, accepted baseline evidence, deterministic next-enrollment ranking, provenance disclosure, and archive limits. |
| [Decompiler Raise Discipline](decompiler-raise-discipline.md) | Rules for raising IL into decompiler structures.  |

### Contributor workflow and process docs

| Document | Need served |
| -------- | ----------- |
| [Development Practices](development-practices.md) | How convention, design, pathological fixtures, analogous implementations, narrow slices, agent-current compatibility, demos, and review work together. |
| [Design Scope and Composition](../docs/design-scope.md) | Full mechanics for one-owner-per-design, broad-design gating, TLA+ modeling, and over-broad-design recovery. |
| [Evidence and Validation](evidence-and-validation.md) | Matching evidence to claims, the style-oracle consultation procedure, and the harness/product boundary. |
| [Fixture Governance](fixture-governance.md) | Placement, project-boundary axes, catalog metadata, consumer rules, and expectation ownership for compiled fixtures and test-local samples. |
| [Round Orchestration](round-orchestration.md) | Running an adversarial review round: status discovery, dispatch, reconciliation, carry-forward, and block boundaries. |
| [Agent Model Mapping](agent-models.md) | Contributor-guidance model names, exact dispatch IDs, and runtime availability resolution. |
| [GitHub Status Queries](github-status-queries.md) | Querying PR mergeability and CI status without wasting API quota. |
| [GitHub API Operations](github-api-operations.md) | Correct `gh api` usage for PR/issue metadata changes. |
| [Stacked PRs](stacked-prs.md) | Mechanics for stacking multiple PRs for a multi-slice issue. |
| [Agent Session State](agent-session-state.md) | Session themes, post-merge handoff, tmux identity, pane activity, and state publishing mechanics. |
| [Local Development Environment](dev-environment.md) | NuGet source overrides and file-based throwaway probes. |
| [Release Workflow](release-workflow.md) | Coordinated package-and-site release process. |
| [Markout Co-development](markout-co-development.md) | The (rare) peer-checkout workflow for changes spanning Markout and this repo. |
| [Inspect-web Demo Hosting](runbooks/inspect-web-demo-hosting.md) | Hosting a network-accessible inspect-web demo. |
| [Installing TLA+ and Java](runbooks/tla-plus-setup.md) | Installing and pinning the TLA+ tools and Java. |
| [TLA+ Methodology](tla-plus-methodology.md) | TLA+ modeling methodology and curated examples. |
| [IL Round-trip Tests](../tests/DotnetInspector.ILRoundtrip.Tests/README.md) | Dependency restore and fast/full test commands for the IL round-trip suite. |

The canonical [`adversarial-review-prompt.md`](adversarial-review-prompt.md) is
the directly usable fixed prefix for every non-trivial reviewer prompt and
carries the repository trust model and finding-admission contract.

PR templates live under `docs/templates/`: `decompiler-pr.md` (raising,
structuring, validity, fidelity, or corpus behavior) and
`decompiler-compile-back-harness-pr.md` (harness-only compile-back, fidelity
skeleton, or ReturnToSender coverage with no product-output change). The optional
[`adversarial-review-prompt.md`](templates/adversarial-review-prompt.md)
template provides the full fill-in form.

### Design history and backlog

Some files under `docs/design/` and `docs/backlog*.md` were written during
ideation. They are useful design history, but may not describe current
behavior. When current behavior matters, start with Overview, the Architecture
map, the relevant host architecture guide, Progressive Disclosure, the embedded
skill, and tests.

## Getting Started

```bash
# Install and run with dnx (like npx)
dnx dotnet-inspect -y -- --help

# Or install globally
dotnet tool install -g dotnet-inspect
dotnet-inspect --help
```
