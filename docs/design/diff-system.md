# Diff system

## Status and owner

This document owns the **diff system**: one vertical path, from a host's diff
request to a completed diff result. It owns the system's layers, its
host-neutral request and operation APIs, the placement rule for
reusable comparison infrastructure, and the adoption and retirement sequence
that moves both hosts onto it. It is tracked by
[#9964](https://github.com/richlander/dotnet-inspect/issues/9964) under
[#9716](https://github.com/richlander/dotnet-inspect/issues/9716).

Focused owners keep their contracts:

- each comparison producer keeps its own design, such as [Finding Diff
  Any](finding-diff-any.md), [Implementation diff](implementation-diff.md) and
  [IL diff canonicalization](il-diff-canonicalization.md);
- [Diff History](diff-history.md) keeps History's semantics and its terminal:
  what a transition establishes, window states, Count and Share, and
  `InspectionEnvelope<DiffHistoryOutcome>`;
- [version resolution](version-resolution.md) keeps version selection,
  including the [version window
  projection](version-resolution.md#version-window-projection);
- [command transition model](command-transition-model.md) keeps the analysis
  catalog, analysis selection and the per-analysis outcome contract;
- [package endpoint scope](package-endpoint-scope.md) keeps how one exact
  package version opens as an inspection scope, and
  [PackageHouse](package-house.md#version-population-settlement) keeps how a
  population address opens as a cell;
- [package-backed Platform realization](package-backed-platform-realization.md)
  keeps how a Platform endpoint opens; and
- presentation owners such as [Comparison document](comparison-document.md)
  and [Library API diff presentation](library-api-diff-presentation.md) keep
  lowering.

This document decides where each of those runs and who calls it. It owns no
result shape: each request returns its content owner's terminal. Where an
existing document places orchestration in a host, this document supersedes
only that placement, and the adoption step that moves it amends that
document.

## Why

A survey of `main` in October 2026 found diff orchestration at several layers,
duplicated per host. Only the pairwise producers and the analysis-set runner
(`DiffAnalysisInspection`) are shared by the CLI and the browser.

| Decision | Where it is made today |
| --- | --- |
| Which versions to compare | Browser TypeScript (`resolveEffectiveDiffTarget` in `package-comparison-targets.ts`); the CLI's literal `A..B` (`DiffCommand`); CLI History (`DiffHistoryEvaluationPlan`) |
| Which Libraries correspond | `PackageCompileAssetSelector.FindComparisonAsset` (browser exports); `LibraryApiDiffRunner` (`.Single()` per side); History's Type matching across all of a cell's assemblies; `CoordinateLibraryPairingQuery` |
| How endpoints are acquired | Browser `BrowserPackageWorkspace.OpenScopeAsync`; CLI `ApiSurfaceEndpointResolver`; CLI `PackageEndpointDiffSession`; History `PackageHouseVersionPopulation.SelectCell` |
| Which analyses run and how results combine | `DiffAnalysisInspection` (shared), with inputs built separately by the CLI (`RunAnalysisSet`) and the browser (`DiffAnalysisLibraryInspection`) |
| Single-Library API Diff terminal | Browser: the analysis set; CLI: `LibraryApiDiffRunner` and `LibraryApiDiffInspection` |
| Which Member bodies to diff | Browser: `MemberBodyDiffInspection`; CLI: `ImplementationComparisonQuery` |
| Iterating a version population | `DiffHistoryMethodology`, CLI only, evaluating each version on its own and lowered by the CLI host (`DiffHistoryOutput`); its orchestration now runs in `DiffOperation` |

Three CLI `diff` routes, general API changes, Analysis Diff and general
Implementation Diff, are recorded as command-owned routes in the
[adoption census](diff-command-adoption-census.md), with no focused owner.
Every row above is a place where the two hosts can disagree about the same
comparison.

## Claim

> **Claim.** Every diff in the product, in either host, runs through one
> host-neutral operation. A host builds a typed diff request, calls the
> operation, and presents or schedules the completed result. The operation
> alone resolves versions, acquires endpoints, pairs Libraries, selects
> analyses and populations, runs producers, and combines their results.
> Comparison infrastructure that does not depend on packages, versions or
> hosts lives in the `ILInspector` layer, where any scenario can reuse it.

## Scenarios

The scarce resource is time. The goal is a diff, and findings in general,
cheap enough to be always available: the way IntelliSense made every Type and
member list available on a partial name or a trailing dot, and the way the
`.Add*` queries carry that one step further. For diff that means opening a
package and seeing at once what changed, broadly or for one member, and asking
of one member when it last changed. The Exists case carries that promise; the
exhaustive case explains what Exists found.

Five scenarios are foundational, and each is one composition of three
primitives: rows are paired across images by their owner's correspondence
(Finding Diff Any's [identity](finding-diff-any.md#identity) for Types and
the members it reads, [API qualified anchor](api-qualified-anchor.md) for a
member named across versions); each paired row is decided by **Exists**
(lockstep that stops at the row's first difference) or read in full by
**Exhaustive** (the complete diff of the row); and row decisions fold up to
the requested scope. The axes, API and Body, are which row kinds are read.
The rungs are [Diff capabilities](diff-capabilities.md)'.

| Scenario | Pairing | Per-row decision and fold | Rungs |
| --- | --- | --- | --- |
| **Member**: one member, A against A' | One member by its anchor | Exists, or Exhaustive, on API and/or Body; no fold: the single-atom diff | 2 for one row, 4 |
| **Type**: one Type, A against A' | The Type's members and its declaration facts | Type Exists, stopped at the Type's first difference; every row's Exists state; or Exhaustive per row | 1, 2, 3 |
| **Assembly**: A against A' | Every Type, then every member | Type Exists for every Type; or Exhaustive per Type, per Member, or both, folded to Type states and the Library state: the full offline assembly diff | 1 to 4 |
| **Batch**: A against several versions | One image paired with each of the others, a star | Exists per row, folded to Type states per version pair, such as the current major's previous patch and the last major's patch against A | 1 and 2, repeated |
| **Bisect**: one member across a version sequence | One member by its anchor in each version | Exists per row, folded to every version interval across which the member differs, on API and/or Body, with no assumption that it changed once | 2 for one row, repeated |

Finding Diff Any is the assembly scenario, hard-wired: assembly-wide pairing,
Type-scope folds, and a private body reader, the walk, that decides a paired
method's Body without a cursor. Product hosts reach the Type scenario by
filtering its assembly result, and its one Type-scoped entry, `Explain`,
returns first-difference text, not states. It reaches the member scenario
only through internal gate hooks; the member Body diff lives in IL diff's
endpoints, and a member's API today is read as a filter over the surface-wide
API comparison. [Diff History](diff-history.md) composes the chain form, each
consecutive pair of a version population, in two ways: Library-scope History
over Finding Diff Any at explicit points, and Type- or Member-focused History
over Finding producers, such as the API member and Analysis Findings, whose
adaptive probing bisects one Type's or one Member's Findings by inspecting
each point it probes, with no assumption that the focus changed once. Batch
as a star,
and bisect over Exists on Finding Diff Any's axes, are not composed. Finding
Diff Any is therefore one composition, not the solution.

What is foundational is what every row of the table uses: the cursors and
`FindingExists` of [Finding census](finding-census.md), which owns the
reading of one row on one kind including when it is `Indeterminate`; the
exhaustive comparison of each kind; and the vocabulary Finding Diff Any owns
as contract rather than mechanism: the identity rule, which rows count toward
which axis, and how a Type's rows and their undecided readings fold. Finding
census step 4 makes Finding Diff Any the owner of Exists at Library, Type, and
Member scope, which is the point at which it becomes a thin composition of
those primitives. The walk is a provision of that owner, kept while it is
measurably cheaper than lockstep over the cursors and deleted when lockstep
matches it, under Finding census step 2b; it is justified by time alone,
never by a second definition of change.

Three consequences follow for what Finding Diff Any does not compose:

- **Batch** wants one census side per image reused across the batch's
  comparisons, so an image's Type-level keys are spelled once. The Symbol Key
  interning table is scoped to one comparison today and a side releases it
  when it completes, so batch needs the table scoped to the batch, recorded
  on [#10533](https://github.com/richlander/dotnet-inspect/issues/10533).
- **Bisect over Exists** is where per-row Exists pays most: it reads one
  member per version and stops at that member's first difference, so its cost
  is one member times the versions probed, never a whole comparison per
  point. Its Body axis needs the declaration cursor of Finding census step 3
  beside the IL cursor, since a member's Body facts include declaration facts,
  and its API axis needs a per-member reading under Finding Diff Any's
  API-axis equality, which its [Member level](finding-diff-any.md#member)
  defines and no owner provides yet.
- **The Type scenario at rung 3** is composed by
  [Annotated Type Map](annotated-type-map.md), which reads the per-Member
  exhaustive diffs under one Type; Finding Diff Any does not compose it.

## Layers

| Layer | Owns | Assemblies |
| --- | --- | --- |
| **1. Comparison kernels** | Pairwise producers over two images or bodies, and the infrastructure they share: correspondence of Types and Members across two images, the axis state vocabulary (`Changed`, `Unchanged`, `Indeterminate`, `NotCompared`) and how states combine across steps, and producer-internal slicing that yields under a work bound | `Inspector.Findings`, `Inspector.Text`, `ILInspector.MetadataPrimitives`, `ILInspector.Metadata`, `ILInspector.Analysis`, `ILInspector.ILDiff`, `ILInspector.Research`, `ILInspector.Decompiler` |
| **2. Selection, acquisition and queries** | Version selection (exact pairs, ranges, populations, version windows), PackageHouse cells, the package endpoint scope, Platform realization, the package Root and Workspace, assembly-context queries over participants, and compile-asset correspondence across versions | `DotnetInspector.Packages`, `DotnetInspector.PackageHouse.*`, `DotnetInspector.Queries`, `DotnetInspector.PackageQueries`, `DotnetInspector.PlatformHouse.*`, `DotnetInspector.PlatformQueries` |
| **3. Results and presentation** | Content owners' result contracts, such as the per-analysis outcomes and Library API Diff outcomes, Research-backed queries, and lowering to portable documents, JSON and Markout | `DotnetInspector.Sections`, `DotnetInspector.ResearchQueries`, the result contracts in `DotnetInspector.ResearchSections`, `DotnetInspector.Presentation` |
| **4. Diff system** | The diff request, the diff operation, its two execution modes, Library pairing, analysis and population selection, and assembling each request's result from layer 3 contracts | `DotnetInspector.Diff` (new), absorbing orchestration now in `DotnetInspector.PackageQueries`, `DotnetInspector.ResearchQueries`, `DotnetInspector.ResearchSections`, `DotnetInspector.Presentation` and both hosts |
| **5. Hosts** | Parsing and routing (CLI); scheduling, caching per page, and rendering (browser) | `DotnetInspect.Cli`, `DotnetInspect.Web*`, `inspect-web` |

Each layer depends only on its own layer or layers above it in the table, so
the diff system
references result contracts and Presentation, and Presentation never
references the diff system. A result contract may live in a lower layer:
History's `DiffHistoryOutcome` and Document stay in
`DotnetInspector.PackageQueries`, where Presentation can lower them. This
matches today's references:
`DotnetInspector.PackageQueries` references `DotnetInspector.Queries`,
`DotnetInspector.Sections` references both, `DotnetInspector.Presentation`
references `DotnetInspector.Queries` and `DotnetInspector.Sections`, and
`DotnetInspector.ResearchSections` references `DotnetInspector.Presentation`. Presentation lowers results and never
orchestrates. Hosts reference the diff system and Presentation, and nothing in
a host chooses versions, pairs Libraries, acquires endpoints for a comparison,
or combines comparison results.

### Placement rule for reusable infrastructure

A comparison mechanism belongs in layer 1 when it takes images, bodies or
metadata and knows nothing about packages, versions or hosts. Such mechanisms
serve several scenarios: diff, Diff History, clone search, `api --match` and
direct member comparison. Examples:

- the pairwise producers themselves;
- Type and Member correspondence across two images, such as Finding Diff Any's
  fingerprints and `MemberAnchor`;
- the axis state vocabulary and its combination rule, the one [Library-scope
  History](diff-history.md#library-scope-history-over-finding-diff-any) uses for
  window states; and
- producer slicing, which [Finding Diff Any](finding-diff-any.md#steps) calls
  steps.

A mechanism that needs a package, a version, a listing or a cell belongs in
layer 2 or 3. New reusable comparison infrastructure lands in layer 1 first,
and layer 3 composes it.

### Vocabulary

A **step** is one comparison between two consecutive points of a diff request. A
producer's unit of bounded work is a **slice**. [Finding Diff
Any](finding-diff-any.md#steps) renames its steps to slices in the adoption step
that moves its browser driver, so that "step" means one thing.

## Data structures

The layer 1 data structures are the base of the system: they define which
kinds of differencing it supports. Each is owned by its focused design; this
section names them as one set, states the shared vocabulary, and records where
the set diverges today.

### The Finding ladder

The base is the Finding ladder that
[Finding nomenclature](finding-nomenclature.md#information-types-and-operation-outcomes)
owns. Its information types are ordered by how many versions they span, and
each rung has the operation that produces it:

| Versions | Information type | Operation and outcome | Question |
| --- | --- | --- | --- |
| One | `Finding<T>`: one observation with a typed payload | **Inspect**: `FindingInspection<T>`, which is Complete, Absent or Failed | What does this version contain? |
| Two | `PairFinding<T>`: one classified transition, Present, Changed, Added or Removed, with a Moved facet and match provenance | **Compare**: `FindingComparison<T>` over two inspections | How does one observation relate to its counterpart? |
| Two | `AnalysisDiff<T>`: complete Before and After sequences partitioned into additions, removals and N:M correspondences | None: complete information a producer issues, derived from a completed Compare or built from a native differ, as [Analysis diff](analysis-diff.md#purpose) owns | How are all items in two sequences related? |
| More than two | `CorrelatedFinding<T>`: one exact identity's occurrences, labelled with their evaluated versions | **Correlate**: `FindingCensusCorrelation<T>`, then `FindingCorrelation<T>` per identity, which is Present, Missing, SubjectAbsent, NoApplicableInput or Failed | Where across versions is this identity? |

`ComparisonDocument<T>` is not a rung: it composes a root and subjects around
any payload, and adds subject-level addition, deletion, rename and move.
Markout's `MappedTextDiff` is the presentation format an `AnalysisDiff<T>`
lowers to, and `AnnotatedSourceDocument` carries annotations on one rendered
artifact, as [Analysis diff](analysis-diff.md#relationship-to-adjacent-formats)
describes.

The ladder maps onto the diff system's requests:

- **Every point is an Inspect** for a Finding producer: a version's
  observations are its census. A non-Finding producer, such as Finding Diff Any,
  evaluates pairs only.
- **Change is Compare.** An exact pair is one Compare. A window, a History
  population and an adaptive bisection are sequences of Compares between
  adjacent evaluated points, which History calls transitions; bisection chooses
  its next point from them. Library-scope History over Finding Diff Any is the
  same shape: each step is one comparison, and its window states derive from the
  step states.
- **Identity across versions is Correlate.** "Where is this identity, and
  when did it appear or disappear?" is a presence question over sparse
  evaluated points; unevaluated versions are never manufactured as Missing.
  Correlate records presence; it does not detect a changed payload, which is a
  Compare.

A producer's native rows are payloads on the ladder: a `Finding<T>` payload,
or an `AnalysisDiff<T>` item. A producer whose results are not Findings can
take part in Compare through its own result, but not in Correlate.

### Payloads in use

The ladder's types are generic, so the system is easiest to see through its
payloads. These are the main `T` types in production on `main` in October
2026:

| Family | Typical `T` | Rungs used | Producers |
| --- | --- | --- | --- |
| API surface | `ApiTypeHandle`, `ApiMemberHandle`, `ApiAttributeHandle` | Inspect, Compare, census Correlate, History transitions; exact-identity Correlate for `ApiMemberHandle` | Metadata API Findings |
| Body text | `CanonicalIlOperation`, `CSharpCanonicalLine` | Inspect, Compare | IL and C# Findings, the Finding half of Implementation Diff |
| Text lines | `string` | Inspect, Compare, `AnalysisDiff<string>` | `TextFindings`: member source diff, the authored source pair, the annotated source diff |
| Source | `SourceDocumentObservation`, `MemberSourceObservation` | Inspect, Compare | SourceLink and source Findings |
| Body semantics in History | `AllocationOccurrence`, `DirectCall`, `UnsafetyOccurrence` | Inspect, Compare, census Correlate, History transitions | Analysis producers that `diff --history` accepts |
| Other body semantics | `UnsafeEvidence`, `StringLiteralUseOccurrence`, `SwitchInfo` compare; `StringMaterializationOccurrence`, `ResourceLifecycleOccurrence`, `ResourceOwnershipPathWitness`, `CallGraphCycleWitness` and `UnsafeMemberFinding` are inspected only; `UnsafeMemberFinding` has no host caller yet ([unsafe member Findings](unsafe-member-findings.md)) | Inspect, and Compare where noted | Analysis producers in the [analysis catalog](command-transition-model.md#analysis-selection) |
| Assembly metadata | `AssemblyReference`, `AssemblyAttributeInfo`, `TypeForwarderInfo`, `ManifestResourceInfo`, `CompilationOptionInfo`, `CompilationReferenceInfo`, `UnionTypeInfo`, `ExtensionMemberObservation`, `ClassifiedMethodObservation` compare; `AssemblySurfaceClassification` is inspected only | Inspect, and Compare where noted, mostly in identity-set mode | Metadata Findings |
| Ecosystem signals | `OpenTelemetrySignalInfo`, `EcosystemIntegrationSignalInfo` | Inspect, Compare | Ecosystem producers |
| Decompiler | `DecompilerFidelityCause` | Inspect, Compare | Decompiler fidelity |
| Annotations | `IAnnotation` | Inspect | Annotated source facts |

The other generic types have few payloads in production:

| Type | Payload | Use |
| --- | --- | --- |
| `AnalysisDiff<T>` | `string` | Text lines |
| `ComparisonDocument<T>` | `LibraryApiTypeDiff` | Library API Diff |
| `FindingCensusCorrelation<T>` | `ApiTypeHandle`, `ApiMemberHandle`, `ApiAttributeHandle`, `AllocationOccurrence`, `DirectCall`, `UnsafetyOccurrence` | History's census over evaluated points |
| `CorrelatedFinding<T>`, `FindingCorrelation<T>` | `ApiMemberHandle` | History's exact-member track |

"In production" means a shipped library API: several metadata Compare
operations, such as switches, union Types and extension members, have no host
caller yet beyond the analysis catalog. So Compare carries most diff content
today, across about thirty payloads, while the other rungs serve narrow domains:
`AnalysisDiff` is text only, `ComparisonDocument` is API only, census Correlate
covers History's six payloads, and exact-identity Correlate is API members only.
The designed `ComparisonDocument<AnalysisDiff<TItem>>` and
`ComparisonDocument<MappedTextDiff>` forms have no production instantiation.
Finding Diff Any's `FindingDiffAnyState`, `ApiDiffAnalyzer`'s `ApiChange`, and
the IL and C# body rows have no `T` at all; they are the producers that the next
table marks as off the ladder.

### Producers

| Kind | Structure | Granularity | On the ladder | Owner |
| --- | --- | --- | --- | --- |
| **Ordered sequence alignment** | `FindingMatcher` (an LCS core, then a scored move pass) producing `FindingComparison<T>` and `PairFinding<T>` | Any ordered stream of keyed observations: text lines, IL Findings, C# Findings, string literal uses | Compare | [Finding nomenclature](finding-nomenclature.md) |
| **Identity-set correspondence** | `FindingMatcher` in its identity-set mode, a multiset bijection with no order or moves | Unordered facts, such as metadata Findings | Compare | [Finding nomenclature](finding-nomenclature.md) |
| **Sequence relations** | `AnalysisDiff<T>`: a complete partition of two sequences into additions, removals and correspondences, where a correspondence may be N:M | Any sequence a producer has aligned | Two-version relation | [Analysis diff](analysis-diff.md#correspondence-facets) |
| **Text characterization** | `TextDiffCharacterization` (`TextRegion`, `TextChange`, `TextMove`) over an `AnalysisDiff<string>` | Lines, with whitespace-only and moved changes | Facts over an `AnalysisDiff<string>` | [Text whitespace](text-whitespace-characterization.md), [text moves](text-move-characterization.md) |
| **Canonical IL comparison** | `IlBodyDiff` rows over `CanonicalIlOperation` | One method body | No: native rows; IL Findings take the ladder separately | [IL diff canonicalization](il-diff-canonicalization.md) |
| **C# body comparison** | `CSharpBodyDiff` line rows; `CSharpStructuralComparison` node matches within one body | One method body | No: native rows; C# Findings take the ladder separately | [Implementation diff](implementation-diff.md#structural-body-comparison) |
| **API surface comparison** | `ApiDiffAnalyzer` `ApiChange` with `ChangeKind` and `ChangeClassification` | API Type or member | No: `ApiChange` rows; API member Findings, which History correlates, take the ladder separately | [Library API diff presentation](library-api-diff-presentation.md) |
| **Axis summary** | `FindingDiffAnyState` per Type and axis | Type, with Member and residual partitions designed | No: per-pair states | [Finding Diff Any](finding-diff-any.md#levels) |
| **Subject composition** | `ComparisonSubjectChange`: Diff, Addition, Deletion, Rename, Move, RenameAndMove | A root or subject in a composed comparison | Composition, not a rung | [Comparison document](comparison-document.md#subject-changes) |

Correspondence across two images uses identity structures that also live in
layer 1: `MemberAnchor` and its fingerprint, `MethodBodyIdentity`, Finding Diff
Any's `SymbolKey`, the `ResearchTargetCorrespondence` family, and
`ApiQualifiedAnchor`, which claims no diff adoption yet.

### Change vocabulary

The canonical vocabulary follows [Finding](finding-nomenclature.md)
transitions, with five states and two facets:

| Canonical | Meaning | Native spellings today |
| --- | --- | --- |
| **Added** | Only After has it | Addition (`AnalysisDiff`, `ComparisonSubjectChange`), Add (`IlDiffKind`, `CSharpDiffKind`), `TypeAdded`, `MemberAdded` |
| **Removed** | Only Before has it | Removal, Deletion, Remove, `TypeRemoved`, `MemberRemoved` |
| **Changed** | Corresponding items differ | `Changed` in most; IL has no changed kind and emits a removal and an addition in one hunk |
| **Unchanged** | Corresponding items are equal | Present (`PairKind`), Unchanged, Context, Exact |
| **Undetermined** | The comparison could not decide | `Indeterminate` and `NotCompared` (`FindingDiffAnyState`, History window states), failed comparisons |
| **Moved** (placement facet) | Corresponding items changed position | `FindingDifferenceKind.Moved`, `AnalysisDiffPlacementKind.Moved`, `TextMove`, `CSharpStructuralChangeKind.Moved` |
| **Renamed** (identity facet) | Corresponding items changed name | `ComparisonSubjectChange.Rename`, with no producer |

`ComparisonSubjectChange.Diff` means the subject corresponds, with no
composition-level change; as [Comparison
document](comparison-document.md#subject-changes) owns, its payload decides
whether it is Changed or Unchanged. Undetermined is carried as itself and is
never projected into Changed or Unchanged, which keeps
[History's rule](diff-history.md#window-states) that an undecided or failed
comparison is never read as unchanged.

Producers keep their native rows, which carry facts the canonical vocabulary
does not, such as an IL offset or a breaking classification. Every document
projects its rows onto the canonical vocabulary at its boundary, so a consumer
can count, filter and render additions, removals, changes and moves the same
way for every producer.

### What the set does not support yet

- **Moves and renames across members or Types.** No producer emits
  `ComparisonSubjectChange.Move` or `Rename`; Finding Diff Any keys Types by
  name, so a moved Type is one removal and one addition. Clone-assisted recovery
  is future work in [structural clone search
  scope](structural-clone-search-scope.md#future-clone-assisted-diff).
- **Moves inside IL and C# line diffs.** Only Findings, text and C# structure
  detect moves.
- **Correlation for non-Finding producers.** Finding Diff Any,
  `ApiDiffAnalyzer`, and the IL and C# body diffs return native rows rather than
  Findings, so their results cannot be correlated across versions; only their
  Finding projections can.
- **One alignment kernel.** Three product LCS implementations exist:
  `FindingMatcher`, and private ones in `IlBodyDiff` and `CSharpBodyDiff`. A
  fourth, `DiffHunks` in the decompiler pipeline tool, is developer tooling. The two body
  representations are compared twice on purpose, under
  [implementation diff's dual representation](implementation-diff.md#deliberate-dual-representation-decision);
  that decision needs two representations, not two algorithms.

## Diff documents

A diff document is a completed, transportable result. It carries two kinds of
information, kept apart:

- **Difference information:** what was added, removed, changed or moved, in
  the canonical vocabulary, at the document's granularity; and
- **Annotations:** typed facts about either side or about a change, such as
  Finding evidence, signals, source facts, or endpoint issues, each targeting
  a coordinate in the difference information.

| Document | Difference information | Annotations | Owner |
| --- | --- | --- | --- |
| `ComparisonDocument<T>` | A root and ordered subjects, each with a subject change and an owner-typed payload | None of its own; the payload carries them | [Comparison document](comparison-document.md#structured-form) |
| `LibraryApiDiffDocument` | A `ComparisonDocument<LibraryApiTypeDiff>`: Type pair kinds, compatibility changes, member relations | Endpoint issues: truncation, rejection, degraded signatures | [Library API diff presentation](library-api-diff-presentation.md#outcome-cases-and-complete-document) |
| `DiffAnalysisDocument` | Per-analysis outcomes, summary counts, transitions, API changes | Finding identities | [Command transition model](command-transition-model.md#content-and-failure) |
| `ImplementationDiffDocument` | Per-member evidence with native IL and C# rows, IL Finding transitions, complexity changes, coverage | Signal, shape, magnitude and in-loop facts on evidence | [Implementation diff](implementation-diff.md#exact-pair-document-and-inspection-envelope) |
| `MemberSourceDiffPresentation` and the authored source pair | An `AnalysisDiff<string>` and a `MappedTextDiff` | Change labels for whitespace and moves (member source diff only) | [Member source diff presentation](member-source-diff-presentation.md), [member source pair](member-source-pair-query.md) |
| `AnnotatedSourceDiffDocument` | Per medium (C# and IL), an `AnalysisDiff<string>` with its characterization and a line map | Two whole `AnnotatedSourceDocument`s, whose facts and targets annotate each side | [Annotated source diff document](annotated-source-diff-document.md) |
| The declaration documents | One row's declaration facts per side and a `FindingComparison<DeclarationFact>` pairing them | None; each fact carries its axis and certainty | [Declaration view](declaration-view.md) |
| `DiffHistoryOutcome` | Per-version evaluations and `DiffHistoryTransition<T>` between points | Probe receipts and Count | [Diff History](diff-history.md#shared-temporal-outcome-and-document) |

### The annotated source document

The annotated source diff document is where difference information and
annotations meet. It places a text diff beside each side's
`AnnotatedSourceDocument`, and joins them through typed coordinates: a fact
targets a node, the node a span, the span a line through the line map, and the
line a relation in the text diff. That chain is the general rule for every
annotation on a diff: an annotation targets a coordinate the document owns,
never a rendered line number.

Its [fact comparison](annotated-source-diff-document.md#fact-comparison), which
compares the facts themselves across versions, is designed but not
implemented, so today each side's facts are annotations on that side only. The
document is also the target for Implementation Diff's C# lane. Its IL medium is
a text diff of printed IL; `IlBodyDiff` stays Implementation Diff's IL lane.

### Rules

- A document exposes the canonical vocabulary for its difference information,
  whatever its producers' native rows.
- A multi-subject document composes its subjects through
  `ComparisonDocument<T>`, so subject-level addition, deletion, rename and
  move have one representation. Library API Diff is the only adopter today.
- Annotations are typed and target document coordinates. A transport carries
  every annotation and change label its document holds; a projection that
  drops one says so in its owner's design.
- Each document has one serialization owner. History's JSON moves from the
  CLI host into Presentation in adoption step 2b.

## Markout

[Markout](../markout-co-development.md) is the rendering substrate for diffs.
It never computes a difference; it validates and presents a caller-issued
mapping:

| Construct | Role |
| --- | --- |
| `MappedTextDiff`, `TextDiffSequence`, `TextDiffChange`, `TextDiffRange` | Two sides and their changes, each an addition, removal or replacement |
| `TextDiffInnerMapping`, `TextDiffSpan` | Intraline correspondence |
| `TextDiffChangeLabel` | A label written after the hunk header, with emphasis, whitespace display and a related change; differently labelled changes never share a hunk |
| `TextDiffAnnotation` (`ForChange`, `ForLine`, `ForSpan`) | An annotation on a change, a line or a span, on either side, with a severity |
| `MappedTextDiffLowering`, `TextDiffDisplayLine` | Format-neutral display lines: context, removal, addition, omission, annotation |
| `MarkoutWriter.WriteTextDiff`, `ITextDiffFormatter`, `TextDiffContextLines` | Writing to Markdown, plain text, Unicode or a structured table |

Structured documents, such as Library API Diff, Analysis Diff, Implementation
Diff and History, lower to Markout sections and tables.

Presentation owns the lowerings from documents to Markout:
`TextAnalysisDiffPresentation.CreateMappedTextDiff` maps an `AnalysisDiff` to a
`MappedTextDiff`, and `CreateLabeledMappedTextDiff` adds characterized
whitespace and move labels. The rules:

- **Every text diff lowers through `MappedTextDiff`.** Today the IL and C# body
  diffs bypass it: `IlDiffPrinter` and `CSharpDiffPrinter` format their own
  hunk strings, which the CLI places in table cells.
- **Every annotation on a text diff lowers to `TextDiffAnnotation`.** No
  producer constructs one today, so annotated-source facts never reach a
  rendering.
- **The browser renders the same mapping.** It receives `MappedTextDiff` data
  through the [source diff transport](inspect-web-source-diff-transport.md) and
  renders HTML from it, so both hosts present one mapping. The transport
  carries annotations, though no producer issues one yet, and drops change
  labels, which it must carry.

## Rendering models

Two text rendering models, both lowered from one `MappedTextDiff`:

| Model | Shape | Hosts |
| --- | --- | --- |
| **GNU diff** | Unified hunks with `---`, `+++` and `@@` headers, context lines, change labels after the header, and the no-newline marker; Markout's Markdown and plain-text formatters | CLI and Markdown output. The browser shows the same hunks in unified or side-by-side layout. |
| **Annotated** | The same display lines with annotation lines interleaved: each `TextDiffAnnotation` appears under the line, span or change it targets, with its severity | CLI through `WriteTextDiff`, whose display lines include annotation lines; browser through its diff viewer |

Side-by-side is a browser layout of either model, not a separate model; Markout
has no side-by-side lowering, and
[the diff viewer](inspect-web-diff-viewer-interaction.md#row-model) owns that
layout. Structured documents render as sections and tables, which are not
text diff models.

Today only GNU diff is complete, and only for member source diffs. The IL and
C# body printers write pseudo-unified hunk rows without headers; the annotated
model has no implementation in either host, and
`CSharpStructuralDiffPrinter.RenderAnnotatedBody` is called only from tests;
and the CLI prints the annotated source diff document only as JSON.

## Host-neutral API

The diff system exposes one request and one operation, with a set entry for
requests that a Query Space request set composes. Results keep their content
owners' terminals.

### Request

A `DiffRequest` names:

- **Subject:** a Package with a framework, a Library, a Type, or a Member.
  Identity uses the existing portable currencies.
- **Endpoint kind:** package coordinates, Platform coordinates, or local
  files. Version windows and History populations apply to package endpoints;
  Platform and local-file requests name an exact pair.
- **Versions:** exactly one of
  - an exact pair, Before and After;
  - a version window, a `PackageVersionWindowPolicy` and depth, projected by
    [version resolution](version-resolution.md#version-window-projection);
  - explicit points; or
  - a Diff History population with an evaluation plan.

  The current version is always the caller's pinned exact coordinate.
- **Content:** the analyses to run, from the
  [analysis catalog](command-transition-model.md#analysis-selection), and the
  axes per step. Each step names its axes; for example, a Supply chain window
  asks for API and Body on every step.
- **Population:** for Member-level content, the explicit selected population,
  such as public body-backed Members paired by API relation.
- **Bounds:** evaluation limits and cancellation.

The request is typed and resource-free. Hosts never parse version text into
it beyond the exact coordinates and policies the existing selection owners
already define.

### Operation

`DiffOperation.ExecuteAsync(request, acquisition, cancellationToken)` runs
every diff. The host supplies its acquisition capabilities: its PackageHouse,
its source operation lease and settlement root, and its population cell
executor. Acquisition authority therefore stays with the host, while every
decision stays in the operation. Each point opens through the existing seam
for its kind, and each seam keeps its owner:

| Point | Opens through |
| --- | --- |
| An exact package pin, including a request's pinned current version, or a package endpoint that the House selects as `package` does | The [package endpoint scope](package-endpoint-scope.md) |
| A population address, such as a window or History point other than the current version | Its prepared cell execution, issued through the retained discovery's `SelectCandidate`, as [PackageHouse](package-house.md#version-population-settlement) and the [version window projection](version-resolution.md#version-window-projection) require |
| A Platform endpoint | [Package-backed Platform realization](package-backed-platform-realization.md) |
| A local file | A direct load, with no scope |

The operation:

1. resolves the request's versions into a population and its initial points;
2. opens each point through its seam, reusing an opened point that two steps
   of the request share;
3. in paired mode, pairs Libraries across each step through one
   correspondence query; a request that names a compile asset pins it on the
   current point, and the query finds its counterpart on every other point;
4. runs the content in one of two execution modes, where the per-point mode
   may choose further points from observed results, as History's adaptive
   methodology does; and
5. assembles the result.

The two execution modes:

| Mode | Shape | Producers |
| --- | --- | --- |
| **Paired steps** | For each step, the operation holds both cells open and runs each pairwise producer on the step's two images. | Finding Diff Any, API diff, implementation diff, string literals, source pairs |
| **Per-point census** | The operation evaluates each point on its own, then compares adjacent results. Each point resolves the focus within that version, with History's absent and ambiguous outcomes. | Today's Diff History Finding producers |

The modes are how a step reads its images. How far it reads is the other
axis, **Exists** to the first difference or **Exhaustive** to the end, which
[Finding census](finding-census.md) owns per row over one streamed census, so
that a paired step and a per-point census decide one equality where both
decide, with a per-point `Failed` read as `Indeterminate`.

An exact pair is a request with two points and one step. Diff History's existing
methodology, including adaptive bisection, surveys and next actions, becomes the
per-point mode. Library-scope History over Finding Diff Any is the paired mode
over a version window.

Sharing across requests, such as a host that asks for both a Supply chain and
an API evolution window, goes through a
[request set](query-space-composition.md#request-sets-and-source-plan-collapse).
`DiffOperation.ExecuteSetAsync(requestSet, acquisition, cancellationToken)`
accepts a request set that Query Space composition formed, with each
`DiffRequest` as one association, and shares only what that composition
admits, including its cost-class rule. Adoption step 4 adds it with the first
consumer that asks for two windows; until then, requests run separately. The
operation never shares work between requests on its own.

A step's failure keeps its typed outcome, and
[History's rule](diff-history.md#window-states) that a failed step is never
read as `Unchanged` applies in both modes.

### Result

The operation returns each request's result in its content owner's terminal,
in the shared inspection envelope:

- a Diff History request returns `InspectionEnvelope<DiffHistoryOutcome>`,
  including its Count component, as [Diff History](diff-history.md) owns it;
- Library-scope History over Finding Diff Any returns
  `InspectionEnvelope<DiffHistoryOutcome>` whose Document carries the window, as
  [Diff History](diff-history.md#library-scope-history-over-finding-diff-any)
  owns it; and
- a pair or window request over the analysis catalog returns the per-analysis
  outcomes that the [command transition
  model](command-transition-model.md#content-and-failure) owns, one set per
  step.

One operation serves every request, but it adds no universal result type. A
new kind of content brings its owner's terminal. Presentation owners lower
each result. A host that needs only part of a result, such as the browser's
Type cue, asks for that content in the request rather than filtering a larger
one.

## Browser and CLI

Both hosts call the same operation:

- **CLI.** `diff`, `diff --history` and `diff --analysis` build a
  `DiffRequest` from their arguments and call the operation. The CLI keeps
  parsing, routing and output, and owns no comparison decisions.
- **Browser.** Each managed export becomes a thin adapter that builds a
  `DiffRequest` and calls the operation. The page keeps its background queue,
  its held analyses and their keys, relevance and rendering. It asks for a
  window policy or an exact pair, and never chooses a version in TypeScript.

The browser's foreground-first and structural-bound rules are unchanged.
Producer slicing still yields the Worker, now driven by the operation rather
than by each export.

## Adoption and retirement

Each step is independently mergeable, names its owner, and moves both hosts or
states why one host has no consumer yet. The order puts the shared spine
first, so later steps retire duplicates rather than add adapters.

| Step | Change | Retires |
| --- | --- | --- |
| 1 | This design | None |
| 2 | Create `DotnetInspector.Diff` with `DiffRequest` and `DiffOperation`. Move Diff History's orchestration into it as the per-point mode, returning `DiffHistoryOutcome` unchanged. `diff --history` calls it. Done: `DiffOperation.ExecuteAsync` and `ExecuteWithEvidenceAsync` take a `DiffHistoryRequest` and a `DiffAcquisition`, and the engines are internal to the diff system. Per-version cell evaluators stay in `PackageQueries` until step 3. | `DiffHistoryInspector`, `DiffHistoryMethodology` and History request types in `PackageQueries`; `DiffHistoryInspection` in `DotnetInspector.Sections` |
| 2b | History's lowering moves from the CLI host into Presentation, after `FindingTargetFormatter` moves below Presentation; next-action command text stays in the CLI. Done: `DiffHistoryPresentation.Project` builds the Markout view and takes the host's `DiffHistoryNextActionFormatter`; `DiffHistoryJsonOutput` and its context serialize the outcome; Presentation admits `NuGet.Versioning`, which package-version contracts expose. | `DiffHistoryJsonOutput`, the view models and the projection in the CLI; the CLI keeps writing, section selection, row selection and command text |
| 3 | Every point opens through its kind's seam, in both hosts, finishing [package endpoint scope](package-endpoint-scope.md#adoption) adoption. The browser gains the endpoint scope and a population cell executor; both hosts share one Root and Workspace composition behind the two seams. Prerequisites: the [package cache policy](package-cache-policy.md) decision that endpoint scope adoption names, and Platform endpoints from Platform realization. | `ApiSurfaceEndpointResolver` diff routes; `PackageEndpointDiffSession`; the browser's per-export `OpenScopeAsync` pairs; the duplicate Root and Workspace composition in the CLI's cell executor |
| 4 | Add the paired-steps mode, version-window requests and the request-set entry. Run Finding Diff Any as a paired producer, with its slicing driven by the operation. Rename Finding Diff Any steps to slices. | The browser's own Finding Diff Any slice loop in `LibraryFindingDiffAnyExports` |
| 5 | One Library correspondence query for paired-mode steps, owned by [compile-asset correspondence](package-asset-selection-correspondence.md#comparison-counterparts). Per-point focus resolution stays History's rule. | `LibraryApiDiffRunner`'s `.Single()` pairing; per-export `FindComparisonAsset` calls |
| 6 | Move the analysis-set runner and its single input builder into the operation. Single-Library API Diff has one terminal. | `RunAnalysisSet`'s separate input builder; `LibraryApiDiffRunner` and `LibraryApiDiffInspection` as a separate terminal |
| 7 | Member body content uses one selected-population request | `MemberBodyDiffInspection` as a browser-only path; CLI Implementation Diff's separate population |
| 8 | Browser exports become thin adapters over the operation; the background cue requests window policies | TypeScript version choice (`resolveEffectiveDiffTarget` choosing versions); per-export pairing and aggregation code; the whole-Library API Diff behind the one-Type cue once Finding Diff Any's Type level lands |
| 9 | CLI `diff` routes, including Platform and local-file diff, collapse onto the operation; the three command-owned routes are owned here | Command-owned routes in the [adoption census](diff-command-adoption-census.md) |
| 10 | Align documents: Finding Diff Any's adoption baselines, the census's analysis-set status, and each focused owner's orchestration claims | Stale claims |
| 11 | Every diff document exposes the canonical change vocabulary at its boundary, and the browser projections carry it | Per-host vocabulary mapping, such as the browser's three-state Library API Diff Type state |
| 12 | IL and C# body diffs lower through `MappedTextDiff` and render in the GNU diff model in both hosts | `IlDiffPrinter` and `CSharpDiffPrinter` hunk strings in CLI table cells |
| 13 | The [annotated source diff document](annotated-source-diff-document.md#adoption) completes its fact comparison and owns the lowering of its facts to `TextDiffAnnotation`, amending its non-claim and Hosts table; Presentation runs that lowering; both hosts render the annotated model; the source diff transport carries change labels | JSON-only CLI output for the annotated source diff; `CSharpStructuralDiffPrinter.RenderAnnotatedBody` as a test-only renderer; label-dropping projections |
| 14 | `IlBodyDiff` and `CSharpBodyDiff` align through the shared alignment kernel, keeping their two representations and cross-check | The two private LCS implementations |

[#9920](https://github.com/richlander/dotnet-inspect/issues/9920)'s remaining
slices consume this system: its Library cue over a window needs steps 4 and 8,
its Type compare rows need step 4 and Finding Diff Any's Type level, its compare
controls need step 8, and its CLI `diff --history` at Library scope needs steps
2 and 4. Its pinned current version opens through the endpoint scope, and its
other points through their cells, from step 3. The version window projection
([#9954](https://github.com/richlander/dotnet-inspect/pull/9954)) is version
selection and stays in layer 2.

## Gates

Each adoption step carries its own gates. The system's invariants, each gated
by the step that establishes it:

- the same `DiffRequest` produces the same result from the CLI and from the
  browser's managed host;
- an exact-pair request and the corresponding one-step window produce the same
  step result;
- a point that two steps of one request share opens once, and two requests
  share a point only as their Query Space request set admits;
- a population address opens only through its candidate-bound cell, and only
  the pinned current version opens through its exact pin;
- a Diff History request returns the same `DiffHistoryOutcome`, including
  Count, before and after step 2;
- a failed step is never combined as `Unchanged`;
- no host assembly references a producer directly for a comparison;
- every diff document reports additions, removals, changes, moves and
  undetermined comparisons in the canonical vocabulary, and never reports an
  undetermined comparison as unchanged;
- every text diff a host renders is a `MappedTextDiff`, and every annotation
  on it a `TextDiffAnnotation` targeting a document coordinate; and
- implementation slices carry exact-head NativeAOT numbers, compared with the
  path they retire.

## Non-claims

- This design does not change any producer's comparison semantics or any
  History semantics. Presentation changes only through adoption steps 11–13.
- It does not merge the producers into one engine; producers stay separate in
  layer 1 and share infrastructure there.
- It does not add persistent caching; that remains
  [#9793](https://github.com/richlander/dotnet-inspect/issues/9793).
- Clone search, `api --match` and direct member comparison are not diff
  requests. They reuse layer 1 infrastructure and are outside this system.
