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

- each comparison producer keeps its own design, such as
  [Fast Diff](fast-diff.md), [Implementation diff](implementation-diff.md) and
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
| Iterating a version population | `DiffHistoryMethodology`, CLI only, evaluating each version on its own and lowered by the CLI host (`DiffHistoryOutput`) |

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

## Layers

| Layer | Owns | Assemblies |
| --- | --- | --- |
| **1. Comparison kernels** | Pairwise producers over two images or bodies, and the infrastructure they share: correspondence of Types and Members across two images, the axis state vocabulary (`Changed`, `Unchanged`, `Indeterminate`, `NotCompared`) and how states combine across steps, and producer-internal slicing that yields under a work bound | `ILInspector.Metadata`, `ILInspector.ILDiff`, `ILInspector.Research`, `ILInspector.Decompiler` |
| **2. Selection, acquisition and queries** | Version selection (exact pairs, ranges, populations, version windows), PackageHouse cells, the package endpoint scope, Platform realization, the package Root and Workspace, assembly-context queries over participants, and compile-asset correspondence across versions | `DotnetInspector.Packages`, `DotnetInspector.PackageHouse.*`, `DotnetInspector.Queries`, `DotnetInspector.PackageQueries`, `DotnetInspector.PlatformHouse.*`, `DotnetInspector.PlatformQueries` |
| **3. Results and presentation** | Content owners' result contracts, such as the per-analysis outcomes and Library API Diff outcomes, Research-backed queries, and lowering to portable documents, JSON and Markout | `DotnetInspector.Sections`, `DotnetInspector.ResearchQueries`, the result contracts in `DotnetInspector.ResearchSections`, `DotnetInspector.Presentation` |
| **4. Diff system** | The diff request, the diff operation, its two execution modes, Library pairing, analysis and population selection, and assembling each request's result from layer 3 contracts | `DotnetInspector.Diff` (new), absorbing orchestration now in `DotnetInspector.PackageQueries`, `DotnetInspector.ResearchQueries`, `DotnetInspector.ResearchSections`, `DotnetInspector.Presentation` and both hosts |
| **5. Hosts** | Parsing and routing (CLI); scheduling, caching per page, and rendering (browser) | `DotnetInspect.Cli`, `DotnetInspect.Web*`, `inspect-web` |

Each layer depends only on layers above it in the table, so the diff system
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
- Type and Member correspondence across two images, such as Fast Diff's
  fingerprints and `MemberAnchor`;
- the axis state vocabulary and its combination rule, the one
  [Library-scope History](diff-history.md#library-scope-history-over-fast-diff)
  uses for window states; and
- producer slicing, which [Fast Diff](fast-diff.md#steps) calls steps.

A mechanism that needs a package, a version, a listing or a cell belongs in
layer 2 or 3. New reusable comparison infrastructure lands in layer 1 first,
and layer 3 composes it.

### Vocabulary

A **step** is one comparison between two consecutive points of a diff
request. A producer's unit of bounded work is a **slice**. [Fast
Diff](fast-diff.md#steps) renames its steps to slices in the adoption step
that moves its browser driver, so that "step" means one thing.

## Host-neutral API

The diff system exposes one request and one operation. Results keep their
content owners' terminals.

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
| An exact package pin, including a request's pinned current version | The [package endpoint scope](package-endpoint-scope.md) |
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
| **Paired steps** | For each step, the operation holds both cells open and runs each pairwise producer on the step's two images. | Fast Diff, API diff, implementation diff, string literals, source pairs |
| **Per-point census** | The operation evaluates each point on its own, then compares adjacent results. Each point resolves the focus within that version, with History's absent and ambiguous outcomes. | Today's Diff History Finding producers |

An exact pair is a request with two points and one step. Diff History's
existing methodology, including adaptive bisection, surveys and next actions,
becomes the per-point mode. Library-scope History over Fast Diff is the paired
mode over a version window. Sharing across requests, such as a host that asks for both a Supply chain
and an API evolution window, goes through a
[request set](query-space-composition.md#request-sets-and-source-plan-collapse).
The operation's set entry accepts a request set that Query Space composition
formed, and shares only what that composition admits, including its cost-class
rule. The operation never shares work between requests on its own.

A step's failure keeps its typed outcome, and
[History's rule](diff-history.md#window-states) that a failed step is never
read as `Unchanged` applies in both modes.

### Result

The operation returns each request's result in its content owner's terminal,
in the shared inspection envelope:

- a Diff History request returns `InspectionEnvelope<DiffHistoryOutcome>`,
  including its Count component, as [Diff History](diff-history.md) owns it;
- Library-scope History over Fast Diff returns
  `InspectionEnvelope<DiffHistoryOutcome>` whose Document carries the window,
  as [Diff History](diff-history.md#library-scope-history-over-fast-diff) owns
  it; and
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
| 2 | Create `DotnetInspector.Diff` with `DiffRequest` and `DiffOperation`. Move Diff History's orchestration into it as the per-point mode, returning `DiffHistoryOutcome` unchanged. `diff --history` calls it. History's lowering moves from the CLI host into Presentation. | `DiffHistoryInspector`, `DiffHistoryMethodology` and History request types in `PackageQueries`; `DiffHistoryInspection` in `DotnetInspector.Sections`; `DiffHistoryOutput` and `DiffHistoryJsonOutput` in the CLI |
| 3 | Every point opens through its kind's seam, in both hosts, finishing [package endpoint scope](package-endpoint-scope.md#adoption) adoption. The browser gains the endpoint scope and a population cell executor; both hosts share one Root and Workspace composition behind the two seams. Prerequisites: the [package cache policy](package-cache-policy.md) decision that endpoint scope adoption names, and Platform endpoints from Platform realization. | `ApiSurfaceEndpointResolver` diff routes; `PackageEndpointDiffSession`; the browser's per-export `OpenScopeAsync` pairs; the duplicate Root and Workspace composition in the CLI's cell executor |
| 4 | Add the paired-steps mode and version-window requests. Run Fast Diff as a paired producer, with its slicing driven by the operation. Rename Fast Diff steps to slices. | The browser's own Fast Diff slice loop in `LibraryFastDiffExports` |
| 5 | One Library correspondence query for paired-mode steps, owned by [compile-asset correspondence](package-asset-selection-correspondence.md#comparison-counterparts). Per-point focus resolution stays History's rule. | `LibraryApiDiffRunner`'s `.Single()` pairing; per-export `FindComparisonAsset` calls |
| 6 | Move the analysis-set runner and its single input builder into the operation. Single-Library API Diff has one terminal. | `RunAnalysisSet`'s separate input builder; `LibraryApiDiffRunner` and `LibraryApiDiffInspection` as a separate terminal |
| 7 | Member body content uses one selected-population request | `MemberBodyDiffInspection` as a browser-only path; CLI Implementation Diff's separate population |
| 8 | Browser exports become thin adapters over the operation; the background cue requests window policies | TypeScript version choice (`resolveEffectiveDiffTarget` choosing versions); per-export pairing and aggregation code; the whole-Library API Diff behind the one-Type cue once Fast Diff's Type level lands |
| 9 | CLI `diff` routes, including Platform and local-file diff, collapse onto the operation; the three command-owned routes are owned here | Command-owned routes in the [adoption census](diff-command-adoption-census.md) |
| 10 | Align documents: Fast Diff's adoption baselines, the census's analysis-set status, and each focused owner's orchestration claims | Stale claims |

[#9920](https://github.com/richlander/dotnet-inspect/issues/9920)'s remaining
slices consume this system: its Library cue over a window needs steps 4 and 8,
its Type compare rows need step 4 and Fast Diff's Type level, its compare
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
- no host assembly references a producer directly for a comparison; and
- implementation slices carry exact-head NativeAOT numbers, compared with the
  path they retire.

## Non-claims

- This design does not change any producer's comparison semantics, any
  History semantics, or any presentation.
- It does not merge the producers into one engine; producers stay separate in
  layer 1 and share infrastructure there.
- It does not add persistent caching; that remains
  [#9793](https://github.com/richlander/dotnet-inspect/issues/9793).
- Clone search, `api --match` and direct member comparison are not diff
  requests. They reuse layer 1 infrastructure and are outside this system.
