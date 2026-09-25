# Realized package dependency context

## Status and authority

This document is the focused owner of the association between one realized
physical package participant and the package dependency evidence projected
from that participant's exact retained content. Design and adoption are tracked
by [#7401](https://github.com/richlander/dotnet-inspect/issues/7401).

This is a legacy-path contract. New package consumers use
[PackageHouse](package-house.md), and the PackageHouse adoption and retirement
tracker [#6426](https://github.com/richlander/dotnet-inspect/issues/6426)
moves existing CLI, Browser/Wasm, Workspace, and dependency consumers to that
facade. Do not add new evidence or consumers here.

The owner defines one host-neutral query, its detached result, and the
construction invariant that binds physical package selection to outgoing
dependency declarations. `RealizedPackageDependencyContextQuery` implements
that boundary in `DotnetInspector.Queries`. It does not own package acquisition,
compile-asset selection, manifest projection, dependency-group selection,
dependency normalization, traversal, destination realization, Workspace
policy, or host presentation.

Adjacent owners retain their authority:

- [Artifact acquisition and Workspaces](artifact-acquisition-and-workspaces.md)
  owns `PackageRootBinding`, its exact physical content generation, compile and
  implementation selection, compatible target-selection authorization and
  outcome, and resource-free reacquisition request. [#7230](https://github.com/richlander/dotnet-inspect/issues/7230)
  stages retention of authorization independently from whether compatible
  fallback selected the implementation.
- `PackageDependencyGroupsQuery` owns bounded manifest access and
  target-framework dependency-group selection.
- [Package Dependency Evidence](package-dependency-evidence.md) owns normalized
  declarations, selected-group identity and status, completion, and failures.
- [Package Dependency Traversal](package-dependency-traversal.md) owns graph
  expansion and preservation of owner-issued source evidence.
- [Traversal target-framework policy](traversal-target-framework-policy.md)
  owns the governing destination target for a traversal.
- [Platform/package pruning](platform-package-pruning.md) owns exact
  target-relative package subsumption; call-graph composition decides whether
  a declaration uses a Platform or package route.
- [Target-framework selection across dependency
  realization](https://github.com/richlander/dotnet-inspect/issues/6424) owns
  the handoff from an admitted dependency edge to destination Package Root
  realization.

## Claim

> Dependency declarations participate in realized-package traversal only
> through one owner-issued context projected from the exact retained content
> and selection intent of that physical package participant.

The context prevents two individually valid but unrelated facts from being
presented as one package variant. Equal package coordinates, framework text,
asset paths, or independently repeated selection outcomes do not establish the
association.

## Input and construction

The host-neutral query is:

```text
RealizedPackageDependencyContextQuery
  Input: PackageRootBinding
  Output: RealizedPackageDependencyContextResult
```

The input is one live acquisition-owned binding. Callers do not supply a
separately projected dependency-evidence root for the query to pair.

The owner derives one resource-free subject from the binding:

```text
RealizedPackageDependencySubject
  RootRequest: PackageRootReacquisitionRequest
  ContentGeneration: PackageContentGenerationIdentity
  Selection: PackageRootSelectionIdentity
```

The exact binding supplies all construction authority:

- `RootRequest` retains the producer-pinned coordinate, compile target,
  implementation-selection target, runtime identifier, compatible
  target-selection authorization, and whether compatible implementation
  selection governed the Root.
- `ContentGeneration` identifies the exact retained payload generation.
- `Selection` identifies the exact compile-asset selection over that
  generation.

The query projects the package manifest from that same retained content through
`PackageDependencyGroupsQuery`, then projects its available result through
`PackageDependencyEvidenceQuery`. No caller-provided coordinate, TFM, manifest,
group, or evidence root participates in construction.

## Shared selection intent

Package compile assets and manifest dependency groups remain independent
owner-issued selections over different candidate sets. This owner gives them
the same consumer intent without requiring equal selected frameworks:

```text
DependencyGroupRequest.Target =
  RootRequest.CompileTargetFramework

DependencyGroupRequest.AllowCompatibleFallback =
  RootRequest.AllowsCompatibleTargetSelection
```

A framework-neutral Root therefore uses the dependency-group owner's existing
no-request behavior. An exact-only Root asks the group owner the exact
question. A Root whose caller authorized compatible target selection asks the
group owner its compatible question against the original compile target,
whether compile-asset selection used an exact asset or compatible fallback.

`RootRequest.UsesCompatibleImplementationSelection` remains observed Root
selection evidence. It does not express caller authorization and does not
control dependency-group fallback. #7230 must issue and preserve
`AllowsCompatibleTargetSelection` through acquisition and reacquisition before
this query can implement the mapping.

The compile-asset and dependency-group owners may select different nearest
frameworks because a package may expose different asset and declaration-group
sets. That is valid retained evidence. The context proves common content and
selection intent, not selected-TFM equality.

The traversal target never enters this source-group selection independently. It
may match the `RootRequest.CompileTargetFramework` because that target governed
realization of this participant. For an explicitly selected hub, the Root's own
selection intent remains authoritative for its source declarations. Later
destination selection is a separate #6424 handoff.

## Result algebra

```text
RealizedPackageDependencyContextResult
  Available(Context)
  Unavailable(Subject, NoManifest)
  Failed(Subject, DependencyGroupFailure)

RealizedPackageDependencyContext
  Subject: RealizedPackageDependencySubject
  Evidence: PackageDependencyEvidenceRoot
```

`Available` includes every complete group-selection status:

- selected non-empty group;
- selected empty group;
- no dependency groups; and
- no matching target framework.

`Available` also retains complete or incomplete
`PackageDependencyEvidenceRoot` values. When Package Dependency Evidence
preserves surviving declarations alongside typed declaration failures, the
context carries that incomplete root without discarding either side.

These states are adjacent-owner evidence, not context success or failure
inventions. `NoManifest` remains unavailable rather than becoming an empty
selected group. A `PackageDependencyGroupsResult.Failed` that prevents
evidence-root construction remains `Failed`. Once Package Dependency Evidence
constructs a root, this owner does not reinterpret its completion or internal
failures as context failure.

Cancellation follows the existing query cancellation contract and is not
rewritten as package evidence.

## Association and identity

The `RealizedPackageDependencyContext` value is the association. It retains:

- the acquisition owner's exact reacquisition request;
- the process-local content-generation and selection identities; and
- Package Dependency Evidence's exact package root, selected-group occurrence,
  declarations, completion, and failures.

The package coordinate appears in both owner-issued sides and must agree during
construction, but coordinate equality alone is not correspondence. The content
generation and selection identities prevent another binding for the same
coordinate from claiming the context.

Traversal consumes the context as one source. It may retain the subject with
declaration and edge evidence, but it does not replace the context with a
package ID, selected framework, or graph-node identity.

## Lifetime and reacquisition

The query executes while the binding supplies current retained-content access.
Its result retains the resource-free `RootRequest`, identities, and normalized
evidence rather than the binding, package content, stream, Workspace,
acquisition client, callback, or reopening authority.

The detached result remains valid historical evidence after the binding or
Workspace closes. It cannot reopen content or establish correspondence to a
later observation. Reacquiring `RootRequest` produces a new binding,
`Selection`, and context. `ContentGeneration` remains equal when the
acquisition owner proves reuse of the same retained immutable snapshot and
changes when replacement content is acquired. Either path requires the query
to run again; generation equality alone does not transfer the old context.

Encoding `RootRequest` for another host does not serialize the process-local
association. The destination host reacquires the Root and independently
projects its own context. Equal logical requests across hosts remain separate
physical observations.

## Motivating real asset

`Polly.Core@8.8.0` exposes the boundary:

- its `netstandard2.0` dependency group declares
  `Microsoft.Bcl.AsyncInterfaces`, `Microsoft.Bcl.TimeProvider`,
  `System.ComponentModel.Annotations`, and
  `System.Threading.Tasks.Extensions`;
- its `netstandard2.0` assembly directly references all four; and
- its `net8.0` dependency group is empty.

For an explicitly selected `netstandard2.0` Polly.Core hub, the context retains
that Root's exact selection intent and four declarations. The traversal
`ProductDefault(net12.0)` may independently govern realization of each
destination package.
Selecting Polly.Core's `net8.0` group would lose source evidence; using
`netstandard2.0` as the destination target would conflate source association
with destination policy.

For a Polly.Core Root realized compatibly from compile target `net12.0`, the
same query instead applies compatible dependency-group selection against
`net12.0`; its empty `net8.0` group remains a valid selected-empty outcome.
These are distinct contexts even though the package coordinate is equal.

A neighboring package may have an exact compile asset for the requested target
but only a compatible dependency group. Compatible authorization must survive
that exact asset selection so the group owner can select the valid compatible
declarations. Whether asset fallback happened cannot substitute for the
authorization.

Observed with production `dotnet-inspect` 0.25.0:

```console
dotnet-inspect dependency-evidence \
  --package Polly.Core@8.8.0 --tfm netstandard2.0 --tsv
dotnet-inspect package Polly.Core@8.8.0 \
  --library --tfm netstandard2.0 -S References --json
```

A pinned real-package gate preserves the behavior, with deterministic fixtures
covering identity and failure boundaries without requiring live NuGet access.

## Conventional basis and divergence

NuGet restore associates one target-specific package library with the
dependency declarations selected for the same restore target. The context
preserves that conventional association when inspection keeps physical package
selection and dependency evidence in separately owned models.

The inspection-specific divergence is detached evidence. A real restore graph
normally remains attached to one restore result; dotnet-inspect may close the
physical Workspace while retaining the exact historical association for
reporting. The detached context grants no acquisition or reopening authority.

The subject shape follows existing package evaluation practice:
`PackageAssemblyEvaluationSubject` already retains a Root reacquisition
request, content generation, and selection identity so later evidence cannot
silently move to another package occurrence.

## Required gates

| Property | Release gate or status |
| --- | --- |
| Construction projects dependency evidence from the binding's exact retained content and accepts no independently produced evidence root. | `ExecuteAsync_DoesNotExchangeEqualCoordinateContexts`. |
| Group selection receives the Root request's compile target and compatible-selection authorization, independently from whether asset fallback was used; selected asset and group frameworks may differ without losing either outcome. | `ExecuteAsync_UsesFrozenRootSelectionIntent`. |
| Polly.Core `netstandard2.0` retains its four declarations and direct assembly references; compatible `net12.0` realization retains the selected empty `net8.0` group as a separate context. | `PollyCore_RetainsSourceDeclarationsAndCompatibleEmptyGroup` plus deterministic selection cases. |
| Selected empty, no dependency groups, no matching framework, no manifest, and dependency-group failure remain distinct result arms. | `ExecuteAsync_PreservesClosedResultAlgebra`. |
| A selected group containing one surviving declaration and one conflicting declaration produces an available incomplete context that retains both the usable edge and typed declaration failure. | `ExecuteAsync_RetainsIncompleteSelectedEvidence`; `Traversal_RealizedIncompleteContextRetainsSurvivingEdgeAndFailure`. |
| Equal coordinates under different content generations or selection identities cannot exchange contexts. | `ExecuteAsync_DoesNotExchangeEqualCoordinateContexts`. |
| The detached result's public shape contains the Root request, exact opaque identities, and dependency evidence needed by consumers. | `ExecuteAsync_ExternalConsumerObservesDetachedPublicShape`; post-Workspace-close observation remains unverified. |
| Reissuing the query produces a new selection and context; same retained content preserves generation identity while replacement content changes it. | `ExecuteAsync_ReissuesContextForSameAndReplacementGenerations`; independent-Workspace reacquisition remains unverified. |
| Traversal preserves the complete context for shared nodes, revisits, cycles, and selected-empty sources. | `Traversal_EqualCoordinateRealizedContextsRemainDistinct`; `Traversal_RootRelativeDepthDoesNotUseGlobalVisitedSet`; `Traversal_CycleRetainsClosingEdgeAndTerminates`; `Traversal_RealizedPollyContextsPreservePackageSelectionUnderDefaultTarget`. |

## Legacy adoption and retirement

Issue #7401 delivered this context for the legacy Root and traversal path.
PackageHouse tracker #6426 now owns migration of package Root construction,
dependency realization, shared Workspace orchestration, CLI, and Browser/Wasm
to House requests, results, and receipts. The component-placement inventory
and final `DotnetInspector.Queries`/`DotnetInspector.PackageQueries`
disposition remain tracked by
[#6432](https://github.com/richlander/dotnet-inspect/issues/6432).

During migration, existing consumers may continue to read this context, but
they receive no new framework-reference, platform-route, or House evidence.
Each consumer retires this dependency when its PackageHouse replacement lands;
no adapter reconstructs the context from equal House display data.

## Non-goals

- Selecting package compile or implementation assets.
- Defining Artifact Acquisition's representation of compatible-selection
  authorization or outcome.
- Selecting dependency groups or changing NuGet compatibility.
- Choosing a traversal target or realizing a destination Root.
- Merging declarations from several dependency groups.
- Resolving dependency version ranges or acquiring destination packages.
- Defining traversal depth, budgets, scheduling, or graph identity.
- Retaining live package content or reopening authority.
- Adding a host-specific association or portable process identity.
