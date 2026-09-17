# Realized package dependency context

## Status and authority

This document is the focused owner of the association between one realized
physical package participant and the package dependency evidence projected
from that participant's exact retained content. Design and adoption are tracked
by [#7401](https://github.com/richlander/dotnet-inspect/issues/7401).

The owner defines one host-neutral query, its detached result, and the
construction invariant that binds physical package selection to outgoing
dependency declarations. It does not own package acquisition, compile-asset
selection, manifest projection, dependency-group selection, dependency
normalization, traversal, destination realization, Workspace policy, or host
presentation.

Adjacent owners retain their authority:

- [Artifact acquisition and Workspaces](artifact-acquisition-and-workspaces.md)
  owns `PackageRootBinding`, its exact physical content generation, compile and
  implementation selection, and resource-free reacquisition request.
- `PackageDependencyGroupsQuery` owns bounded manifest access and
  target-framework dependency-group selection.
- [Package Dependency Evidence](package-dependency-evidence.md) owns normalized
  declarations, selected-group identity and status, completion, and failures.
- [Package Dependency Traversal](package-dependency-traversal.md) owns graph
  expansion and preservation of owner-issued source evidence.
- [Workspace default target framework](workspace-default-target-framework.md)
  owns the governing destination target when no authoritative target exists.
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
  implementation-selection target, runtime identifier, and whether compatible
  implementation selection governed the Root.
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
  RootRequest.UsesCompatibleImplementationSelection
```

A framework-neutral Root therefore uses the dependency-group owner's existing
no-request behavior. An exact Root asks the group owner the exact question. A
Root formed by compatible implementation selection asks the group owner its
compatible question against the original compile target.

The compile-asset and dependency-group owners may select different nearest
frameworks because a package may expose different asset and declaration-group
sets. That is valid retained evidence. The context proves common content and
selection intent, not selected-TFM equality.

The Workspace default never enters this source-group selection independently.
It may already be the `RootRequest.CompileTargetFramework` because it governed
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

Those states are Package Dependency Evidence, not context success or failure
inventions. `NoManifest` remains unavailable rather than becoming an empty
selected group. A manifest access, decode, identity, declaration, or
group-selection failure remains the adjacent owner's typed failure and cannot
produce `Available`.

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
later generation. Reacquiring `RootRequest` produces a new binding,
`ContentGeneration`, and `Selection`; the query must run again to issue a new
context.

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
that Root's exact selection intent and four declarations. Workspace default
`net11.0` may independently govern realization of each destination package.
Selecting Polly.Core's `net8.0` group would lose source evidence; using
`netstandard2.0` as the destination target would conflate source association
with destination policy.

For a Polly.Core Root realized compatibly from compile target `net11.0`, the
same query instead applies compatible dependency-group selection against
`net11.0`; its empty `net8.0` group remains a valid selected-empty outcome.
These are distinct contexts even though the package coordinate is equal.

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

| Property | Release gate |
| --- | --- |
| Construction projects dependency evidence from the binding's exact retained content and accepts no independently produced evidence root. | Query construction tests with two equal coordinates backed by distinct content generations. |
| Group selection receives the Root request's compile target and compatible-selection mode; selected asset and group frameworks may differ without losing either outcome. | Selector-spy table covering framework-neutral, exact, compatible, no-match, and differing-nearest-framework cases. |
| Polly.Core `netstandard2.0` retains its four declarations and direct assembly references; compatible `net11.0` realization retains the selected empty `net8.0` group as a separate context. | Pinned `Polly.Core@8.8.0` integration test plus equivalent deterministic fixtures. |
| Selected empty, no dependency groups, no matching framework, no manifest, and dependency-group failure remain distinct result arms. | Closed result-algebra table tests. |
| Equal coordinates under different content generations or selection identities cannot exchange contexts. | Same-coordinate cross-generation and independently repeated selection tests. |
| The detached result's public shape contains the Root request, exact opaque identities, and dependency evidence needed by consumers. | Public consumer construction/observation test plus post-Workspace-close evidence test. |
| Reacquisition issues a new context and does not transfer the old process-local association. | Reacquisition test across independent Workspaces and content generations. |
| Traversal preserves the complete context for shared nodes, revisits, cycles, selected-empty sources, and source failure. | Focused Package Traversal adoption gates. |

The design is specification-only. Every property is unverified until its named
Release gate lands.

## Production adoption

Issue #7401 is the end-to-end tracker. There are four capability steps:

1. Implement the host-neutral context query and its result algebra in
   `DotnetInspector.Queries`.
2. Have package realization issue the context while it holds the exact
   `PackageRootBinding`; do not add a second manifest acquisition path.
3. Have Package Dependency Traversal consume the context for realized source
   expansion and combine its declarations with #6424 destination realization.
4. Retain the shared result through CLI and Browser/Wasm call-graph
   experiences, using existing Markout and structured-output boundaries.

The shared query is not complete product behavior until both production hosts
consume the same association. Each adjacent owner adopts it in a focused
follow-up effort; this document does not redefine their internals.

## Non-goals

- Selecting package compile or implementation assets.
- Selecting dependency groups or changing NuGet compatibility.
- Choosing a Workspace default or destination target.
- Merging declarations from several dependency groups.
- Resolving dependency version ranges or acquiring destination packages.
- Defining traversal depth, budgets, scheduling, or graph identity.
- Retaining live package content or reopening authority.
- Adding a host-specific association or portable process identity.
