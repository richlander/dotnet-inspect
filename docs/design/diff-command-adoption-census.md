# Diff command adoption census

Status: **non-normative current-state index**.
[#7719](https://github.com/richlander/dotnet-inspect/issues/7719) establishes
the baseline required by
[#7703](https://github.com/richlander/dotnet-inspect/issues/7703) before Diff
routes move to shared operations and subject sections.

This document answers one adoption question: which current `diff` and
`timeline` entrances, carriers, acquisition paths, renderers, row units,
failure outcomes, and Release gates must a focused migration preserve, replace,
or explicitly retire?

The normative
[Diff operation and subject-section owner](command-transition-model.md#diff-operation-and-subject-section-adoption)
defines placement, equivalence, and production sequencing. The focused owners
linked below retain comparison, acquisition, History, output, envelope,
Workspace, House, and rendering semantics. This index does not redefine them.

## Census vocabulary

| State | Meaning |
| --- | --- |
| **Shared terminal** | A host-neutral operation returns an owner-issued `InspectionEnvelope<TContent>` used by more than one host or public transport. |
| **Command-owned route** | The CLI currently selects producers, composes results, or constructs the result carrier. It remains shipping behavior, not the target shared boundary. |
| **Compatibility bridge** | Shipping acquisition or ownership remains useful while a focused House, Workspace, or operation adoption is incomplete. |
| **Unverified** | No named Release gate establishes the stated current boundary. It is not a claim that the behavior is absent or incorrect. |

These labels are routing metadata. They do not replace the exact status,
residual gaps, or evidence in a focused owner.

## Source admission

Top-level `diff` admits exactly one endpoint-source route. Package and Platform
coordinates select sources; they do not change a Library comparison into a
Package or Platform subject.

| Entrance | Current admission | Endpoint population | Acquisition route |
| --- | --- | --- | --- |
| Package | `--package Package@old..new`, or the first positional value when no explicit source is present; optional `--tfm` | One or more Libraries per version | `AssemblySetResolver` uses `PackageExtractor`; direct PackageHouse and Workspace admission remain a **compatibility bridge** under #6638. |
| Platform | `--platform Library@old..new`; `--framework` defaults to `runtime` | One or more platform Libraries per version | `AssemblySetResolver` uses `PlatformResolver`; PlatformHouse Library transfer remains owned by the [PlatformHouse reference-processing design](platform-house-reference-processing.md). |
| Local Library | `--library old/path.dll..new/path.dll` | One Library per path | `AssemblySetResolver` opens the two direct paths. |
| Timeline package range | `timeline --package Package@A..B`, or positional package range; one required Type focus | One ordered package-version vector and zero or more explicitly selected payload cells | `PackageExtractor.OpenPackageRangeAsync` supplies discovery and payload reuse when online; `PackageVersionVector.ResolveAsync` supplies the vector fallback. |

`AssemblySet` owns temporary extraction cleanup for the three pairwise source
routes. Its current cleanup does not separately surface directory-deletion
failure. This is recorded behavior, not a cleanup contract introduced here.

## Operation and result routes

| Route | Dispatch boundary | Current result carrier | Status and disposition |
| --- | --- | --- | --- |
| Selected-Library API Changes | Exactly one acquired Library per endpoint; no Member filter, Analysis Diff, Implementation Diff, or Finding Transitions; no selected section other than Changes | `InspectionEnvelope<LibraryApiDiffOutcome>` from `LibraryApiDiffRunner` and `LibraryApiDiffInspection` | **Shared terminal.** One ephemeral `InspectionWorkspace` owns separate before/after `AssemblyContextGroup` values. Different logical Libraries are a typed rejection, not a legacy fallback. This is the first #7703 pairwise adoption substrate. |
| General API Changes | Multi-Library endpoints, Member filtering, mixed sections, or another route outside the selected-Library boundary | Command-owned API comparison and `DiffDocumentView` composition | **Command-owned route.** Preserve multi-Library population, Type/Member filter, classification, inspection-failure, and mixed-section behavior until a focused operation owns them. |
| Analysis Diff | `Analysis Diff`, including `--changed` and `--alloc-regressions` implications | `BodySignalComparisonQuery`, `AnalysisDiffRow`, and command-owned summaries | **Command-owned route.** Allocation-focused planning may omit unused Changes work, but the CLI still owns result composition and presentation. |
| Focused package Implementation Diff | Package source, Implementation Diff, exactly one Type and one Member, no PDB source, and one package-root assembly on each side | `WorkspaceImplementationComparisonRunner` over `WorkspaceImplementationComparisonQuery`; lowered into the existing Implementation Diff view | Workspace-backed shipping slice. Direct and forwarded targets run in one ephemeral Workspace with typed identity and closed-world binding evidence. Forwarded target acquisition still uses `PackageExtractor.ExtractPinnedPackageAsync`, a **compatibility bridge**. |
| General Implementation Diff | Any broader package, platform, local, untargeted, or PDB-source comparison outside the focused Workspace route | `ImplementationComparisonQuery`, `ImplementationDiffRow`, and specialized method-body or source evidence | **Command-owned route.** It retains existing behavior until focused operation slices cover its wider populations and evidence lanes. |
| Finding Transitions | Selected alone with the focus required by the Finding descriptor | Command-built `FindingTransitionRow` values; no query is declared in `DiffSections` | **Command-owned route.** API Type, Member, attribute, allocation, call-site, unsafety, C# line, and IL-op transitions retain missing, present, changed, removed, and failed evidence. |
| Timeline | Package range, one Type focus, optional Member focus, one Finding census, and selected Evaluations and/or Transitions | `TimelineDocumentView` with typed Evaluation and Transition rows | Shipping predecessor to shared History. `timeline` remains until the [History owner](diff-history.md#cli-cutover-dependency) and #7703 establish full population, evaluation, sparse/failure, Count, output, discovery, and Share parity. |

Current `--type` and `--member` values filter a Library comparison. They do not
establish exact Type or Member identity. Exact operation-first requests and
their subject sections must adopt the same owner-resolved identity together;
the [Diff adoption owner](command-transition-model.md#equivalent-requests)
defines that future boundary.

## Acquisition and lifetime routes

| Evidence lane | Current live ownership | Detached result or visible failure | Focused owner or task |
| --- | --- | --- | --- |
| Pairwise Package endpoint | `PackageExtractor`, `AssemblySet`, and endpoint metadata readers | API, Analysis, Implementation, or Finding carrier selected by the command; acquisition failure remains nonzero | [PackageHouse](package-house.md), [Workspace admission](artifact-acquisition-and-workspaces.md), #6638 |
| Pairwise Platform endpoint | `PlatformResolver` and `AssemblySet` | Same command-selected carrier and visible acquisition failure | [PlatformHouse reference processing](platform-house-reference-processing.md), #6301 |
| Pairwise local endpoint | Direct path-backed `AssemblySet` | Same command-selected carrier and visible invalid-image or query failure | [Assembly image lifetime](assembly-image-lifetime.md) |
| Selected-Library API | `LibraryApiDiffRunner` creates one `InspectionWorkspace` and two endpoint groups, then disposes them after producing detached Content | `LibraryApiDiffOutcome.Available`, `.Unavailable`, or `.Rejected`, endpoint summaries, ordered diagnostics, and `Share.NonProjectable` | [Library API Diff presentation](library-api-diff-presentation.md#shared-terminal-and-cli-consumer) |
| Focused package Implementation | `WorkspaceImplementationComparisonRunner` owns the bounded Workspace and observed root-to-terminal composition | Native forwarder Findings, endpoint identity, C#/IL evidence, typed composition or producer non-success, and cleanup evidence | [Targeted package implementation comparison](../cli-architecture.md#targeted-package-implementation-comparison), #4706 |
| Exact Member source pair | `MemberSourcePairInspection` executes inside an `InspectionWorkspace` | `InspectionEnvelope<AssemblyMemberSourcePairResult>` with typed available, unavailable, or failed source evidence | [PDB acquisition](../pdb-acquisition.md), #5526 |
| Broader PDB/source lane | SourceLink acquisition and `PdbSourceHouse.AcquireMemberAsync` | Command-owned PDB-source rows and visible absence, indexing, declaration, HTTP, ambiguity, and read failures | [PDB acquisition](../pdb-acquisition.md), #5526 |
| Timeline population | `PackageRangeExtraction`, `PackageVersionVector`, and selected package evaluations disposed by `TimelineCommand` | Per-cell present, missing, subject-absent, failed, or unevaluated evidence; one failed cell does not erase later cells | [Diff History](diff-history.md), #7229 |

The [resource-owner type map](resource-owner-type-map.md) remains the compact
current-state owner index. This census records only the Diff-specific route
through those owners.

## Rendering and transport routes

| Projection | Carrier and lowering | Row or line behavior | Envelope, Count, and failure |
| --- | --- | --- | --- |
| Envelope JSON | Native `InspectionEnvelope<LibraryApiDiffOutcome>` through `InspectionEnvelopeOutput`, `result_kind` `library-api-diff`, schema 1 | No display-row window; serializes the completed service value | Supported only by the selected-Library API terminal. Share is currently `NonProjectable` at `comparison/endpoints`. Unavailable and Rejected Content serialize before nonzero exit; acquisition failure fabricates no envelope. |
| Unprojected Content JSON | Native `LibraryApiDiffOutcome` through `LibraryApiDiffJsonContext` | No display-row window | Omits envelope framing but preserves typed Available, Unavailable, and Rejected cases. |
| Projected document JSON | CLI `DiffDocumentView`, including section arrays and inspection failures | The selected-Library display-JSON lowerer receives no `RowWindow`; positive row-window behavior is **unverified** | Not an envelope or Count projection. Non-success retains a reason and available failure rows before nonzero exit. |
| Markdown document | `DiffFullView` or command-owned `DiffDocumentView`; ordinary selected-Library output uses generated Markout while general multi-section output includes manual document composition | Selected-Library Markout receives `RowWindow`; exact grouped-list window behavior is **unverified** | No Diff Count integration. Empty selected-Library success is visibly “No API changes”; non-success remains visible. |
| Table, TSV, JSONL | `DiffTableView` for Type summaries or `DiffDetailedChangesView` for detailed Changes through `MarkoutSerializer` | Markout applies the semantic window to data rows while retaining headers; no Diff-owned rendered-line fallback is used | No Diff Count or envelope integration. Non-success emits an error rather than a success-shaped empty table. |
| Name-only | Direct ordered Type-name list from selected comparison subjects | The lowerer receives no `RowWindow`; name-only windowing is **unverified** | No Count or envelope integration; non-success emits the typed reason. |
| Source text diff | `MemberSourceDiffPresentationResult` lowered by `SourceTextDiffRenderer` to custom `SourceDiffOutput` | Fields plus mapped text lines; no semantic row sequence or rendered-line limiter | Specialized rendering boundary, not a Diff envelope or Count adapter. Typed unavailable and failed source evidence remains distinct. |
| Method-body diff | `MethodBodyDiffDocument` lowered by `MethodBodyDiffFormatter` and its Markout context | Semantic Producers, Endpoints, C#, IL, Diagnostics, and Cleanup sequences; optional writer-seam windowing | No Count or envelope adapter. Rejected, failed, cancelled, query, producer, endpoint, and cleanup failures remain explicit. |
| Timeline Markdown and structured formats | `TimelineDocumentView` with Evaluation and Transition semantic rows; typed JSON preserves the same selected identities | Head, Tail, and Window compose in argument order before Markdown, table, TSV, JSONL, typed JSON, or Count. Explicit Lines clips rendered output and cannot reduce authorized payload-cell acquisition. | Count observes post-selection rows per selected section. Multi-section Count is an ordered map. Failure, subject absence, missing, and unevaluated remain distinct. No public envelope is adopted. |

No Diff-owned tree model or tree lowerer is present in the current renderers.
The command-layer disposition of a tree request is **unverified**. `Inspection
Failures` is renderable in `DiffDocumentView` but is not a selectable entry in
the declared `DiffSections` schema.

## Section and row inventory

| Section | Producer or result | Semantic row | Current selection |
| --- | --- | --- | --- |
| Changes | `ApiComparisonQuery` or selected-Library `LibraryApiDiffDocument` | Detailed compatibility or unclassified API change; summary tables use one changed Type per row | Default Diff section and part of `@Diff` |
| Analysis Diff | `BodySignalComparisonQuery` | Member signal with old, new, delta, shape, and evidence | Explicit, implied by allocation-regression focus |
| Implementation Diff | `ImplementationComparisonQuery` or `WorkspaceImplementationComparisonQuery` | Member mechanism, difference, change, and evidence | Expensive and explicit |
| Finding Transitions | Command-owned Finding evaluation | Transition, Finding, target, old/new state, and detail | Expensive, exact-only, and selected alone |
| Inspection Failures | Endpoint and producer failure evidence | Operation, token, mechanism, kind, detail, subject, and dependency | Rendered evidence, not a selectable Diff section |
| Timeline Evaluations | Per-version Finding evaluation | One selected version cell and its completed state | Selected by default |
| Timeline Transitions | Correlation between selected evaluation cells | One adjacent or qualified sparse transition | Selected by default |

No inspected Diff renderer invokes `CountProjection`. Positive Diff Count
behavior is therefore **unverified**, rather than inherited from Timeline or
History.

## Release characterization gates

| Boundary | Named Release gates | Current evidence |
| --- | --- | --- |
| Selected-Library Content and presentation | `LibraryApiDiffPresentationTests`, `LibraryApiDiffInspectionTests`, `LibraryApiDiffJsonTests` | Complete and empty documents, path and memory inputs, typed truncation, logical-Library rejection, forwarded-constraint failures, serialization, and retention bounds |
| Selected-Library CLI and transport | `LibraryApiDiffCommandTests`, `LibraryApiDiffEnvelopeCommandTests` | Markdown and structured projections, filters, real `System.Text.Json` package evidence, Content JSON, envelope, compact JSON, modifier rejection, non-success, and no fabricated envelope |
| General Diff routes | `DiffCommandTests`, `CommandExecutionTests` | API, Analysis, Implementation, mixed sections, filters, Finding Transitions, package and local entrances, output composition, and command validation |
| Diff failure visibility | `CommandExecutionTests.Diff_InspectionFailures_AreNeverReportedAsCleanAcrossOutputModes` | Malformed local metadata must return failure across Markdown, document JSON, Finding, Analysis, table, TSV, JSONL, and name-only projections; tracked by #7010 |
| Source evidence | `SelectedSourceDiffTests`, `MemberSourceDiffPresentationTests` | Changed, unchanged, reordered, moved, absent, HTTP, deadline, ambiguity, indexing, read, and no-PDB cases |
| Focused Workspace Implementation | `WorkspaceImplementationComparisonRunnerTests`, `WorkspaceImplementationComparisonQueryTests`, `SectionPipelineTests.DiffCommand_AllocRegressionsRequestsAnalysisWithoutUnusedChanges` | Direct and forwarded targets, exact identity, closed-world composition, C#/IL evidence, typed non-success, peer-section incompleteness, and query-plan exclusion |
| Timeline semantics | `TimelineCommandTests` | Zero, sparse, and dense evaluation; exact Type/Member selection; Analysis Findings; nine-cell topology; failure distinctions; ordering; rows; formats; and Count |
| Timeline acquisition and lines | `ConfiguredPayloadAcquisitionTests.TimelineRange_OneDiscoveryAcquiresOnlyExplicitAddresses`, `TimelineRange_SemanticRowsComposeWithoutReducingExplicitAcquisition`, `TimelineRange_ExplicitLinesClipRenderedOutput`, `TimelineRange_LinesRejectDocumentJsonBeforeAcquisition`, `TimelineRange_ProbeReplayRetainsWorkingDirectoryAndSelectionPolicy` | One discovery, explicit-cell authorization, row/output separation, pre-acquisition rejection, and replay context |
| Browser selected-Library consumer | `BrowserLibraryApiDiffOperationTests` | Complete changed-Type inventory, canonical baseline serialization, empty and typed non-success, and whole-result bound rejection |

The named classes are suite boundaries, not evidence that every row above has
an isolated method. Focused owners retain the exact method inventory and
pathological cases.

## Unverified boundaries

The following current or migration-relevant boundaries have no named Release
gate in this census:

- a successful top-level Platform Diff, rather than the gated rejection of a
  multi-Library reference-pack endpoint by selected-Library transport;
- positive Diff semantic Head, Tail, Window, and Count behavior across each
  renderer;
- selected-Library name-only and display-JSON row-window behavior;
- grouped Markdown row-window behavior within Diff sections;
- the command-layer disposition of tree output;
- Timeline public envelope and Share behavior, which are not yet adopted;
- Timeline source-evidence behavior;
- Timeline structured or tabular output after real package acquisition; and
- a dedicated fixture-backed Timeline version evolution rather than
  test-local vectors and metadata.

These are inputs to focused #7703 slices. This index neither adds requirements
to their owners nor declares unsupported behavior supported.

## Maintenance

When a focused owner or shipping route changes:

1. Update the focused owner, implementation, and Release evidence first.
2. Update this index when dispatch, carrier, acquisition, renderer, row unit,
   envelope/Share status, failure disposition, or named gate changes.
3. Keep compatibility bridges until their focused retirement condition lands;
   do not infer retirement from a shared terminal existing on a neighboring
   route.
4. Mark a missing characterization `unverified`; do not manufacture a contract
   or transfer a gate from another route.
5. Keep top-level `diff` permanent and retain `timeline` until the normative
   adoption owner records complete History parity.

The census may close while executable adoption remains open. It is the
current-state baseline for those later slices, not their implementation
tracker.

## Validation

This document changes no product behavior and adds no independent semantic or
lifetime claim. The focused #7010 failure-visibility gate and Markdown
validation provide this slice's direct evidence; all other behavior remains
owned and gated by the linked focused designs and suites.

`CommandExecutionTests.Diff_InspectionFailures_AreNeverReportedAsCleanAcrossOutputModes`
passed in Release for the #7719 census candidate.

## Non-goals

- A universal Diff result type, renderer, or acquisition pipeline.
- New comparison, correspondence, History, Count, Share, or subject-identity
  semantics.
- Runtime changes or early advertisement of future subject sections.
- Retirement of `timeline`, a source route, an output mode, or a compatibility
  bridge.
- Treating Type or Member filters, display rows, or rendered text as exact
  subject identity.
- Duplicating every focused test method or owner algorithm.
