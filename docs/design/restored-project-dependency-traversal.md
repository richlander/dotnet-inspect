# Restored project dependency traversal

This document owns the host-neutral restored-project traversal contract tracked
by [#5998](https://github.com/richlander/dotnet-inspect/issues/5998).

**Status:** implementation contract. The
`RestoredProjectDependencyTraversalQuery` in `DotnetInspector.Queries`
implements it. No CLI or browser host consumes it yet; adoption is
[#5994](https://github.com/richlander/dotnet-inspect/issues/5994).

## Owner and claim

**Restored Project Dependency Traversal Query** in `DotnetInspector.Queries`
owns:

> Project one exact restored target selection into typed root,
> project-reference, and package dependency relationships with completion
> sufficient for root-relative traversal.

This owner defines:

- the traversal node set — the explicit restored root, resolved project nodes,
  and resolved package nodes — and its root-relative distance relation;
- typed project-to-project and root-to-project relationships;
- admission of the facts owner's package relationships into that root-relative
  graph;
- depth admission, explicit depth boundaries, and the scope of a typed failure;
- traversal completion; and
- one traversal identity scoped to the facts owner's unchanged selection
  identity.

It does not own the facts it composes:

- [Restored Project Dependency Facts](restored-project-dependency-facts.md)
  owns assets admission, schema support, target selection, declaration groups,
  package-pruning evidence, resolved package coordinates, package-resolving
  graph edges, content provenance, selection identity, and `InertString`
  containment. This owner consumes those results and identities unchanged.
- `ProjectAssetsParser` in `DotnetInspector.Services` remains a **host
  locator**. It is not a parser for this owner, and locator provenance never
  enters the query.
- The [Dependency Inspection Command](dependency-inspection-command.md) owns
  CLI roots, gestures, sections, rendering, diagnostics, and exit status.
- [Package Dependency Traversal](package-dependency-traversal.md) owns the
  separate package-manifest traversal. A restored graph is authoritative
  evidence from restore; a manifest traversal is not. The two are never merged
  into one claim.

## Why this owner exists

`RestoredProjectDependencyFactsQuery` publishes resolved package nodes and
package-resolving edges. It walks project-reference branches to reach the
packages behind them, but it publishes no relationship for the project branch
itself: the root project and every nested project branch are pushed onto the
walk without an emitted edge.

For the ordinary shape `App -> ProjectB -> PackageC`, the published facts are
therefore exactly one edge, `ProjectB -> PackageC`, whose parent is an opaque
project identity with no incoming relationship. A consumer cannot compute the
distance of `PackageC` from `App`, cannot show that `ProjectB` is a project
reference of the root, and cannot distinguish "reached through one project
reference" from "reached through six".

`Traversal_RootRelativeDepthIsUnknowableFromFactsAlone` pins that gap. The
unified `depends` command in #5993 promises root-relative depth over a restored
project root, so this prerequisite must land before #5994 can claim it.

Two graphs that differ only in project-only topology currently share one facts
identity, and that identity contract is deliberately unchanged here. This owner
publishes its own topology identity instead of silently broadening the facts
owner's claim.

## Consumer and delivery plan

The first production consumer is the unified CLI `depends` adoption in
[#5994](https://github.com/richlander/dotnet-inspect/issues/5994), under the
end-to-end dependency tracker
[#5532](https://github.com/richlander/dotnet-inspect/issues/5532). The planned
second host is inspect-web Browser/Wasm through
[#5535](https://github.com/richlander/dotnet-inspect/issues/5535), which
consumes the same typed result across the managed engine boundary. This is
shared host-neutral substrate, so no single-consumer or single-host exception
applies.

The delivery sequence set by
[the command design](dependency-inspection-command.md) was #5993, #5765, #5996,
**#5998 (this owner)**, #3320, #5994, then #5995's `dependency-evidence`
removal. This document adds no step to that completed plan.

Total host steps to reach both planned hosts:

1. **#5998** — this query and its focused Release gates. *(this slice)*
2. **#3320** — the typed Markout graph and edge-row projection that both hosts
   lower through.
3. **#5994** — CLI `depends` supplies assets bytes, target request, and depth,
   then renders the typed result. The CLI keeps locator resolution and
   `.csproj`/directory/assets admission on its side of the boundary.
4. **#5535** — Browser/Wasm supplies the same bytes and request through the
   managed engine boundary and owns only its interactive DOM presentation.

No host step ports this algorithm into host code.

## Conventional baseline and deliberate divergence

The conventional restored-project reader — `dotnet list package`, NuGet's
`LockFileFormat`, and the MSBuild assets consumers — treats `project.assets.json`
as a graph whose root is the restoring project, walks
`projectFileDependencyGroups` into `targets`, and follows both `package` and
`project` typed nodes.

This owner keeps that baseline: same root, same entry set, same node typing,
same reachability.

The deliberate divergences are:

- **No local path is a node identity.** NuGet's readers use absolute
  `projectPath` and `msbuildProject` values. Those are untrusted authored text
  and local-environment evidence, so a project node's public identity remains
  the facts owner's opaque digest over its exact target-entry key, and the
  authored spelling travels only as `InertString`.
- **No version constraint is minted for a project relationship.** NuGet
  reconstructs project-reference versions from restore state this owner does
  not have. A project relationship is structural topology; package constraint
  evidence stays with the facts owner's package edges.
- **Depth is admission, not truncation of an unknown graph.** The whole
  selected graph is already materialized in the admitted bytes, so a depth
  boundary is an explicit statement about the answer, never a partial read.
- **Failures are scoped to the answer.** A failure beyond the requested depth
  does not make a bounded answer partial, because that evidence was never
  claimed.

## Contract shape

```text
exact project.assets.json UTF-8 bytes
  + optional exact target framework/runtime request
  + optional maximum root-relative depth
        |
        v
Restored Project Dependency Facts Query (one shared internal projection)
        |
        v
Restored Project Dependency Traversal Query
        |
        v
closed outcome
  - available traversal
    - the facts projection, unchanged
    - traversal identity (selection identity + topology digest)
    - nodes with minimum root-relative distance
    - project-resolving relationships with admitted distance
    - package-resolving relationships carrying the facts owner's exact edges
    - explicit depth boundaries
    - in-scope typed graph failures
    - stated completion
  - unavailable, retaining its facts
  - failed, retaining the owning phase's typed failure
```

## Inputs

One execution receives exact `project.assets.json` UTF-8 bytes, an optional
exact target request, and an optional maximum depth. It accepts no path,
filesystem, MSBuild, restore, cache, network, logger, or renderer capability.

Locator provenance is not an input. A `.csproj` locator, a project directory,
and a direct assets path are equivalent at this boundary once the host supplies
the same bytes and target request; only the host's locator provenance differs.
`Traversal_CsprojLocatorAndDirectAssetsBytesProduceOneTraversalIdentity` gates
that equivalence without moving locator ownership into the query layer.

`MaximumDepth` is the maximum admitted relationship distance from the root.
`0` admits the root node and no relationship; `null` admits the whole selected
graph. A negative request is rejected at construction.

## One shared projection

Target selection, schema support, assets admission, bounds, containment, and
the selected-target walk have exactly one implementation. The facts owner
exposes an internal projection that returns its published result together with
the project-relationship topology computed by the same walk; the traversal
query consumes that projection.

There is therefore no second JSON traversal, no second target-selection rule,
and no path by which the two owners can disagree about which target was
selected. `Traversal_TargetSelectionIsSharedWithTheFactsOwner` gates default,
explicit-framework, and framework-plus-runtime selection against the facts
owner's own answer.

The published facts API, its behavior, and its identity are unchanged by this
slice.

## Nodes and identity

The traversal reuses the facts owner's closed node union — the explicit
restored root, a resolved project node, or a resolved package node — as its
node currency. It mints no parallel node identity.

Each node carries its **minimum root-relative distance**: the fewest admitted
relationships between the root and that node. The root is always `0`. A project
node additionally carries its exact authored target-entry spelling as
`InertString`; every other variant carries none, and the invariant is enforced
at construction.

Traversal identity is the facts owner's unchanged `RestoredProjectSelectionIdentity`
plus a `TopologyDigest` over the admitted answer. The digest uses the same
length-prefixed, count-preceded canonical encoding the facts owner uses, over
node keys, relationship endpoints, admitted distances, package constraint and
role evidence, depth boundaries, in-scope failures, the requested depth, and
completion. Node keys are built only from canonical package coordinates and
opaque project digests, so no authored spelling, local path, JSON property
position, or rendered text reaches the digest.

Consequences that are gated:

- a project-only topology change moves the topology digest while the facts
  selection identity stays stable
  (`Traversal_ProjectOnlyTopologyChangeMovesTraversalIdentityNotSelectionIdentity`);
- a different depth request is a different answer and a different identity
  (`Traversal_DepthRequestChangesTraversalIdentity`);
- process culture changes neither identity nor result semantics
  (`Traversal_IdentityIsCultureInvariant`); and
- JSON property reordering changes neither identity nor result ordering
  (`Traversal_JsonPropertyOrderChangesNeitherTraversalIdentityNorOrdering`).

## Relationships

Two relationship families connect the node set.

A **project relationship** is the root or a graph node depending on a resolved
project node. It carries its parent, its project dependency, the dependency's
`InertString` spelling, and its admitted distance. Occurrences coalesce by
parent and dependency exactly as package edges do.

A **package relationship** carries the facts owner's exact
`RestoredProjectGraphEdge` instance. Edge identity, parent, dependency,
canonical constraint, source constraint spelling, direct/transitive role, and
root declaration association are preserved, never reminted.
`Traversal_PackageRelationshipRetainsTheExactFactsEdgeEvidence` gates that the
admitted relationship is the facts owner's own edge and not an equal copy.

A relationship's **distance** is one more than its parent's minimum distance. A
node reached from several parents keeps one minimum distance while every
distinct relationship survives; shared targets, diamonds, and cycles never
delete a relationship. A cycle terminates because each node expands once.

A package node with no admitted incoming relationship — for example one whose
root entry carried no usable constraint — is not a traversal node. Its typed
facts failure remains visible; the traversal never invents a root relationship
to make it reachable.

## Depth, boundaries, and failure scope

Depth admits already-materialized evidence and causes no acquisition,
filesystem access, restore, or additional parsing.

A relationship is admitted when its parent is reachable and the parent's
minimum distance is strictly less than the requested depth. A node whose
distance equals the requested depth and which has any outgoing evidence —
a relationship or a typed failure of its own expansion — is reported as an
explicit **depth boundary**, so a bounded answer never presents a bounded node
as an inspected leaf.

A typed graph failure belongs to the expansion of the node that produced it, so
it is in scope exactly when a relationship from that node would have been
admitted. An unbounded traversal answers over the whole selected graph and
therefore retains every occurrence, matching the facts owner's published
failures exactly
(`Traversal_UnboundedFailuresMatchThePublishedFactsGraphFailures`). Failures
that are not attributable to one node expansion — a configured-limit
exhaustion — are always in scope.

## Completion

Completion is stated, never inferred from an empty collection, and is validated
against the boundaries and failures it accompanies:

- `Complete`: every relationship the selected graph offers was admitted, with
  no in-scope failure.
- `DepthBounded`: the only unadmitted frontier is the request's explicit
  maximum depth.
- `Partial`: usable topology exists, but typed graph evidence inside the
  admitted depth is incomplete.

`Partial` takes precedence over `DepthBounded`: an in-scope failure is never
hidden behind a depth boundary.

An unavailable graph capability is `Unavailable`, not a complete empty
traversal, and it retains its facts so a consumer does not reproject them. A
failed document or a failed graph is `Failed` and retains the owning phase's
typed failure.

## Bounds and containment

Assets size, scalar length, declaration, node, and package-edge bounds are the
facts owner's and are unchanged. This owner adds one bound —
`MaxProjectRelationships` — on project-relationship occurrences observed while
walking one selected target. It is deliberately independent of the facts
owner's bounds so a project-dense document cannot change published facts
evidence; exceeding it makes the traversal `Partial` with the facts owner's
existing `ConfiguredLimitExceeded` reason while the facts graph stays complete.

Newly exposed project spellings are `InertString`, and project identity stays
an opaque digest.
`Traversal_HostileProjectSpellingRemainsInertBesideAnOpaqueIdentity` gates
both.

## Host and rendering boundaries

The query returns typed nodes, relationships, distances, boundaries, failures,
and completion. It has no Markout, DOM, command-line, console, or filesystem
dependency, and it performs no I/O.

The CLI decides whether a section requests traversal, supplies bytes, target,
and depth, composes this result with other dependency producers, lowers it
through Markout, and derives diagnostics and exit status. Browser/Wasm supplies
the same typed inputs through the managed engine boundary and owns only its
interactive presentation.

The implementation is SRM-free by construction, NativeAOT-friendly,
reflection-serialization-free, and compatible with a single-threaded
Browser/Wasm host: it is a synchronous, allocation-bounded walk over an
already-parsed document with no blocking wait, worker thread, or ambient
filesystem access. These properties inherit the repository and project
contracts; this slice adds no repository-wide absence scan.

## Pathological cases

### `App -> ProjectB -> PackageC`

```text
projectFileDependencyGroups: [ "ProjectB >= 1.0.0" ]
targets: ProjectB/1.0.0 (project) -> PackageC 1.0.0
         PackageC/1.0.0 (package, leaf)
```

| Depth | Nodes | Relationships | Boundaries | Completion |
| --- | --- | --- | --- | --- |
| `0` | root@0 | none | root | `DepthBounded` |
| `1` | root@0, ProjectB@1 | root → ProjectB @1 | ProjectB | `DepthBounded` |
| `2` | root@0, ProjectB@1, PackageC@2 | plus ProjectB → PackageC @2 | none | `Complete` |
| unbounded | same as `2` | same as `2` | none | `Complete` |

The facts owner alone reports only `ProjectB -> PackageC`, with no incoming
relationship for `ProjectB`.

### Project cycle

```text
root -> ProjectB -> ProjectC -> ProjectB
```

All three relationships survive, including the closing one; distances are `1`,
`2`, and `3`; nodes are `root@0`, `ProjectB@1`, `ProjectC@2`; and the walk
terminates.

### Project diamond

```text
root -> ProjectB -> ProjectD -> PackageE
root -> ProjectC -> ProjectD
```

Both `ProjectB -> ProjectD` and `ProjectC -> ProjectD` survive as distinct
relationships at distance `2`, `ProjectD` keeps one minimum distance of `2`,
and `PackageE` is at `3`.

### Failure beyond the boundary

```text
root -> ProjectB -> ProjectC -> (dependency with no selected-target node)
```

At depth `2`, the answer carries no failure and is `DepthBounded`, with
`ProjectC` reported as a boundary rather than a leaf. Unbounded, the same
document is `Partial` with the facts owner's `UnresolvedDependency`.

### Failure inside a bounded answer

A root entry that resolves to no node fails at distance `0`. At depth `1` that
failure is in scope, so the answer is `Partial` even though a depth boundary
also exists.

### Project-dense mesh

A mesh whose project-relationship occurrences exceed `MaxProjectRelationships`
is `Partial` with `ConfiguredLimitExceeded`, while the facts graph — which has
no package edge to bound — remains complete.

## Evidence

The implementation adds focused Release gates in
`tests/DotnetInspector.Queries.Tests/RestoredProjectDependencyTraversalQueryTests.cs`.
They use synthetic exact assets bytes that the product parses, plus the
existing `restored-project.dependency-facts` fixture, which already restores a
real project reference. No new checked-in fixture is required, and the harness
never constructs or repairs product evidence.

| Contract | Gate |
| --- | --- |
| Depth `1` admits the root project relationship and bounds the package relationship. | `Traversal_DepthOneAdmitsRootProjectRelationshipAndBoundsPackageRelationship` |
| Depth `2` admits the project-to-package relationship at distance `2`. | `Traversal_DepthTwoAdmitsProjectToPackageRelationshipAtDistanceTwo` |
| An unbounded traversal completes at a package leaf. | `Traversal_UnboundedTraversalCompletesAtPackageLeaf` |
| Depth `0` admits only the root and bounds every relationship. | `Traversal_DepthZeroAdmitsOnlyTheRootAndBoundsEveryRelationship` |
| Root-relative depth is unknowable from published facts alone. | `Traversal_RootRelativeDepthIsUnknowableFromFactsAlone` |
| A project cycle terminates and retains its closing relationship. | `Traversal_ProjectCycleTerminatesAndRetainsClosingRelationship` |
| A diamond retains both relationships and one minimum distance. | `Traversal_ProjectDiamondRetainsBothRelationshipsAtMinimumDistance` |
| Default, explicit-framework, and framework-plus-runtime selection are shared, not reimplemented. | `Traversal_TargetSelectionIsSharedWithTheFactsOwner` |
| `.csproj`, directory, and direct assets bytes produce one traversal identity. | `Traversal_CsprojLocatorAndDirectAssetsBytesProduceOneTraversalIdentity` |
| A really restored project reference becomes root-relative. | `Traversal_RestoredFixtureProjectReferenceIsRootRelative` |
| Package relationships retain the facts owner's exact edge evidence. | `Traversal_PackageRelationshipRetainsTheExactFactsEdgeEvidence` |
| Project-only topology moves traversal identity while selection identity stays stable. | `Traversal_ProjectOnlyTopologyChangeMovesTraversalIdentityNotSelectionIdentity` |
| JSON property order changes neither traversal identity nor ordering. | `Traversal_JsonPropertyOrderChangesNeitherTraversalIdentityNorOrdering` |
| A different depth request is a different traversal identity. | `Traversal_DepthRequestChangesTraversalIdentity` |
| Process culture changes neither traversal identity nor result semantics. | `Traversal_IdentityIsCultureInvariant` |
| Hostile project spellings stay inert beside an opaque identity. | `Traversal_HostileProjectSpellingRemainsInertBesideAnOpaqueIdentity` |
| A malformed document preserves the typed document failure. | `Traversal_MalformedDocumentPreservesTheTypedDocumentFailure` |
| An unsatisfied target request is unavailable and keeps its facts. | `Traversal_UnsatisfiedTargetRequestIsUnavailableAndKeepsItsFacts` |
| Ambiguous target identity preserves the typed graph failure. | `Traversal_AmbiguousTargetIdentityPreservesTheTypedGraphFailure` |
| A failure beyond the depth boundary does not poison a bounded answer. | `Traversal_FailureBeyondTheDepthBoundaryDoesNotPoisonTheBoundedAnswer` |
| Unbounded failures match the published facts graph failures exactly. | `Traversal_UnboundedFailuresMatchThePublishedFactsGraphFailures` |
| `Partial` takes precedence over `DepthBounded`. | `Traversal_PartialTakesPrecedenceOverTheDepthBoundary` |
| The project-relationship bound is partial without changing facts completion. | `Traversal_ProjectRelationshipBoundIsPartialWithoutChangingFactsCompletion` |
| A negative maximum depth is rejected. | `Traversal_NegativeMaximumDepthIsRejected` |

The facts owner keeps its existing admission, selection, containment, identity,
and completion gates in `RestoredProjectDependencyFactsQueryTests`. This slice
runs them unchanged as its no-regression evidence for the shared projection.

Run both suites with:

```bash
dotnet run --project tests/DotnetInspector.Queries.Tests -c Release
```

## TLA+ disposition

No TLA+ model is selected. The contract is a finite, deterministic,
single-threaded walk over one immutable already-parsed document, with no
concurrency, retry, replacement, or cross-operation join currency. The
pathological cases above cover its cycle, revisit, depth, failure-scope, and
completion properties directly.

If a later slice introduces incremental topology replacement or a concurrent
publication path, that new state machine requires a separate model decision.

## Non-goals

This owner does not define:

- MSBuild evaluation, restore, build, or project-file interpretation;
- assets admission, schema support, target selection, declaration groups,
  package-pruning evidence, or package coordinate and edge construction — all
  owned by the facts document;
- normalized package declaration evidence or its redefinition;
- remote package acquisition, nuspec traversal, package-source authorization,
  or cache policy;
- CLI grammar, admission, section visibility, row selection, rendering, or exit
  status; or
- filesystem locator resolution, which remains a host concern.
