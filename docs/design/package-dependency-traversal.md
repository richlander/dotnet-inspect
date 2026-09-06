# Package dependency traversal

This document owns the host-neutral package-manifest traversal contract tracked
by [#5996](https://github.com/richlander/dotnet-inspect/issues/5996).

**Status:** design target. The current `DependencyResolutionService` remains a
lossy tree implementation until the focused delivery slices named here land.

## Owner and claim

**Package Dependency Traversal Query** in `DotnetInspector.Queries` owns:

> Starting from ordered exact package roots and their selected normalized
> declarations, follow source-authorized exact package candidates and return
> one immutable, depth-bounded directed graph with root-relative reachability,
> typed failures, and completion.

This owner defines:

- exact package-node and declaration-edge composition;
- source-relative manifest projection identity beneath semantic package nodes;
- unresolved declaration-boundary composition for roots that do not authorize
  recursive source work;
- per-root distance and edge-admission relations;
- cycle, revisit, and shared-node behavior;
- traversal depth and finite work-budget behavior;
- deterministic scheduling and result ordering;
- traversal-local failure and completion;
- preservation of owner-issued source and declaration evidence; and
- the reusable result consumed by CLI and Browser/Wasm hosts.

It does not own the facts or effects it composes:

- [Package Dependency Evidence](package-dependency-evidence.md) owns normalized
  declarations, selected dependency groups, declaration identity, source
  spellings, completion, and `InertString` containment.
- The exact dependency-candidate adapter tracked by
  [#5765](https://github.com/richlander/dotnet-inspect/issues/5765) owns the
  composition from one declaration through package-source authorization and
  NuGet version-range resolution to an owner-issued exact acquisition
  candidate.
- [Package Source Model](package-source-model.md) owns configured authority,
  source mapping, candidate completeness, exact payload authority,
  source-result association, authentication, deadlines, and typed source
  failures.
- `PackageManifestFactsQuery` owns bounded manifest decoding, package-identity
  validation, declaration validation, and `PackageManifestFailure`.
- `PackageDependencyGroupsQuery` owns target-framework group selection and the
  distinction among a selected group, no dependency groups, and no matching
  target framework.
- The [Dependency Inspection Command](dependency-inspection-command.md) owns
  CLI roots, traversal gestures, sections, graph-document composition, output
  formats, diagnostics, and exit status.

The traversal query consumes only owner-issued identities and results. It
must not select a version from package-source presentation rows, infer source
authority from a label or successful payload, parse a manifest independently,
or reconstruct declaration identity from display text.

## Consumer and delivery plan

The first production consumer is the unified CLI `depends` adoption in
[#5994](https://github.com/richlander/dotnet-inspect/issues/5994). The planned
second host is inspect-web Browser/Wasm through the dependency experience
tracked by [#5532](https://github.com/richlander/dotnet-inspect/issues/5532).
The browser consumes the same typed graph through its managed engine boundary;
it does not port this algorithm to TypeScript.

The focused delivery sequence is:

1. #5765 supplies the shared declaration-to-exact-candidate handoff. Its first
   Workspace consumer and this traversal consumer use one source-authorized
   version-resolution contract.
2. #5996 implements this query and its focused Release tests.
3. #5994 composes the query into `depends`.
4. The Browser/Wasm dependency experience consumes the same result after its
   acquisition path can provide the required candidate and manifest
   capabilities.

The design may land before #5765, but the traversal implementation must not
replace that prerequisite with an ad hoc resolver. In particular,
`PackageVersionDiscoveryResult.SourceListings` are presentation evidence, not
an authority-bearing acquisition receipt.

Before traversal implementation, #5765's owning design must make its core
candidate handoff reusable outside Workspace:

- the caller establishes whether one declaration is eligible for candidate
  resolution;
- Workspace keeps its exact-package and package-prefix scope checks outside
  that core handoff;
- traversal supplies its own recursive-expansion authorization;
- declarations for which
  `PackageDependencyVersionRange.GetExactVersion` returns a coordinate can use
  the Package Source Model's exact pinned-acquisition rule without first
  proving complete version enumeration;
- range or floating selection follows the Package Source Model rule that
  partial evidence cannot select when a missing authority could change the
  answer; and
- a selected candidate retains the reporting-authority correspondence needed
  for exact manifest acquisition.

The Workspace adapter may still expose an `outside` result for its own scope
policy. That Workspace-specific state is not part of the shared candidate
resolver consumed here.

Issue #5996 also supplies a Queries-owned manifest-bytes adapter. It projects
`PackageSourceManifest` through `PackageManifestFactsQuery`, the existing
facts-to-groups projection, and `PackageDependencyEvidenceQuery`; it never
downloads a `.nupkg` merely to read one already-acquired manifest.

## Question answered

For each admitted package root, the query answers:

> Which exact package coordinates or declared source boundaries are reachable
> through the selected manifest dependency groups, why does each directed edge
> exist, how far is each node from each root, and where did requested traversal
> stop?

The result is a manifest dependency graph. It is not a prediction of the flat
package graph that a project restore would choose.

## Conventional baseline and deliberate divergence

NuGet PackageReference restore applies graph-wide rules including lowest
applicable version, floating versions, direct-dependency-wins, and cousin
dependency reconciliation. It emits one resolved project graph in
`project.assets.json`.

This query has no project declaration set, lock file, central package
management state, or application-level override from which to reproduce that
contract. Pretending otherwise would make a package-manifest inspection look
more authoritative than its inputs permit.

The deliberate divergence is therefore:

- each normalized declaration edge selected for recursive expansion is
  resolved independently by #5765 against source-authorized evidence;
- the candidate owner applies NuGet's version-range and prerelease rules for
  that declaration;
- declarations classified as exact by
  `PackageDependencyVersionRange.GetExactVersion` follow the Package Source
  Model's pinned-acquisition rule, while range and floating declarations
  follow its candidate-completeness rule;
- two edges may resolve the same package ID to different exact versions, and
  both coordinates remain in the graph;
- a later edge never rewrites an earlier edge or prunes its descendants; and
- direct-dependency-wins, cousin reconciliation, lock-file selection, and
  project-level downgrade diagnostics are excluded.

When an authoritative restored project graph is available, the restored-
project owner supplies that graph instead. The CLI must not compare this
manifest traversal with a restore graph as though the two owners made the same
claim.

## Contract shape

```text
ordered admitted exact package roots
  + selected normalized declaration groups
  + typed framework-selection mode
  + per-root expansion authority
  + exact-candidate resolver capability from #5765
  + exact manifest acquisition and projection capability
  + optional maximum depth
  + finite traversal work budget
        |
        v
Package Dependency Traversal Query
        |
        v
immutable outcome
  - root occurrences
  - exact package nodes
  - source-relative manifest projections
  - unresolved declaration-boundary nodes
  - failed-resolution declaration nodes
  - work-budget declaration nodes
  - normalized declaration edges
  - per-root node distances
  - per-root projection distances
  - per-root admitted-edge relations
  - typed boundaries and failures
  - per-root completion and aggregate summary
```

The query owns one operation snapshot. A host may project several views from
that result without rerunning source work or changing graph identity.

## Immediate typed inputs

### Root occurrences

The request carries an ordered, finite set of package root occurrences. Each
occurrence references one admitted `PackageDependencyEvidenceRoot` whose
identity is `PackageDependencyEvidenceRootIdentity.Package`.

The root must already retain:

- an exact `PackageSourceCoordinate`;
- its package-manifest provenance;
- normalized declaration evidence and selection state; and
- a selection receipt constructed under the request's typed framework mode.

Two occurrences may name the same exact package coordinate. They remain
distinct roots with shared semantic node identity. Occurrence order is
request-local presentation and traversal order, not package identity.

A failed root without exact package identity is not a traversal input. The
host retains that failed attempt beside the query result in its enclosing
dependency document.

### Framework-selection mode

The request carries one typed framework-selection mode:

- `Exact`, with one validated canonical NuGet framework identity; or
- `ManifestDefault`, meaning each manifest uses the package dependency-group
  owner's explicit no-request selection policy.

The mode is structural request currency. It is formed before traversal and is
not reconstructed from
`PackageDependencyEvidenceSelection.RequestedFramework`, which remains inert
presentation evidence.

For `Exact`, the existing package evidence owner keeps its exact-or-universal
selection contract. It does not silently enable the legacy compatible-TFM
fallback. `NoMatchingTargetFramework` is therefore a complete selection
outcome for the exact question, but it remains visible and must not be
described as a package proven to have no dependency declarations.

For `ManifestDefault`, each node retains the framework group chosen by the
owner's no-request policy. The result explicitly describes a per-manifest
default traversal and does not claim one graph-wide target framework.

Every admitted root and every transitive manifest projection is constructed
under the same mode. The adapter supplies that association directly; the
query does not validate trusted in-process callers by comparing display text.

### Per-root expansion authority

Each root occurrence carries one typed expansion authority:

- `RecursiveSources`, allowing selected declarations to use #5765 and exact
  manifest acquisition within the request's source context; or
- `DirectDeclarationsOnly`, allowing only owner-issued direct declaration
  edges.

Remote package and local `.nupkg` roots may receive `RecursiveSources`.
Direct nuspec and package-prefix roots receive `DirectDeclarationsOnly`
because their admission gestures do not authorize recursive source work.

For a direct-only root, the query emits one declaration edge to an unresolved
declaration-boundary node and performs no candidate or manifest operation.
The boundary identity combines the root-bound source manifest projection,
owner-issued declaration identity, canonical package ID, and constraint; it is
not a guessed package coordinate.

### Exact candidate resolution

For each selected normalized declaration under `RecursiveSources`, the query
invokes the #5765 capability with:

- the source package's exact semantic identity;
- the complete owner-issued declaration identity and canonical constraint;
- the operation's package-source authorization context; and
- the shared operation context and cancellation token.

The shared capability returns an owner-issued exact candidate, a typed
resolution or authorization failure, or typed incomplete evidence. The
traversal query does not receive raw version strings and choose among them.

An exact candidate retains the correspondence and authority evidence required
for exact manifest acquisition. Candidate equality and acquisition authority
remain owned by #5765 and the Package Source Model.

### Exact manifest acquisition and projection

For a candidate that must be expanded, the query invokes a package-owned exact
manifest capability, then the Queries-owned manifest-bytes adapter. The result
retains the candidate association and returns one of:

- an admitted package evidence root for the same exact coordinate;
- a typed source, acquisition, manifest, or projection failure; or
- typed incomplete completion evidence.

The admitted root is produced through `PackageManifestFactsQuery`, the
facts-to-groups projection owned by `PackageDependencyGroupsQuery`, and
`PackageDependencyEvidenceQuery`. The traversal query never implements a
second nuspec parser or acquires a package archive for manifest-only work.

The candidate and returned root must correspond through owner-issued typed
identity. Artifact-authored package ID or version disagreement is a manifest
identity failure, not a second graph node.

### Traversal intent and work budget

The request carries:

- an optional positive maximum depth;
- a finite maximum number of expanded manifest projections;
- a finite maximum number of declarations submitted for candidate resolution;
  and
- one source operation context with its owner-issued deadline.

Depth is user intent. The work budget is a resource-safety limit. Exhausting
depth can produce successful `DepthBounded` completion; exhausting a work
budget produces typed partial completion because requested authorized evidence
was not established.

The manifest and source owners retain their own byte, page, response, and
deadline limits. This query does not enlarge them.

## Immutable result

### Root occurrence

Each result root retains:

- its request-local occurrence identity and order;
- its exact package semantic node;
- the original root evidence and provenance;
- its traversal completion; and
- its affected failures and boundaries.

The same semantic node may be both an explicit root and a transitive target.
That produces one node and several occurrence/reachability relations.

### Package node

A semantic package node is identified by exact canonical
`PackageSourceCoordinate`, never by package ID alone.

The node retains:

- exact package ID and version;
- every root or exact-candidate admission receipt for that coordinate;
- every source-relative manifest projection admitted for that coordinate; and
- aggregate root and edge incidence without collapsing those projections.

Source association is provenance, not node identity. Two versions of one
package ID are distinct nodes. Two valid paths to the same coordinate share
one node even when they require distinct source-relative projections.

### Manifest projection

A manifest projection identifies one authority-bearing observation of an exact
package coordinate. Its identity is owner-issued:

- a traversal-issued projection identity bound to one explicit root occurrence
  and its owner-issued provenance;
- a #5765 candidate correspondence plus exact manifest source result; or
- a proven correspondence that allows one of those observations to satisfy
  the other.

Each projection retains its exact source association, selected
dependency-group state, normalized declarations, and expansion state. Two
feeds may publish different bytes for the same coordinate, so non-equivalent
projections remain separate beneath the shared semantic package node. Their
outgoing declarations are never silently unioned into one source-independent
adjacency.

The current direct-nuspec evidence does not carry a typed correspondence
receipt. Repeated or same-coordinate direct nuspec roots therefore retain
distinct manifest projections. The traversal must not infer correspondence
from coordinate, display path, normalized declarations, object identity, or
byte equality.

An explicit root's supplied manifest evidence does not consume exact-manifest
acquisition budget. If the same coordinate is later reached through a
candidate, that evidence may replace source acquisition only when an
owner-issued correspondence proves the root provenance is valid for that
candidate. Coordinate or display equality alone is insufficient.

### Declaration-boundary node

A declaration-boundary node identifies a dependency target for which the
root's authority permits direct evidence but not exact candidate resolution.
It retains:

- source manifest projection identity;
- owner-issued declaration identity;
- canonical package ID and constraint;
- inert source spellings; and
- the root occurrences for which it is the authorized endpoint.

Boundary nodes are occurrence-bearing dependency identities, not exact
package coordinates. They never participate in cycle coalescing or recursive
expansion.

### Failed-resolution declaration node

A failed-resolution declaration node identifies a recursively authorized
dependency declaration for which #5765 did not issue an exact candidate. Its
identity combines:

- source manifest projection identity;
- owner-issued declaration identity; and
- canonical package ID and constraint.

It retains the complete typed #5765 no-match, authorization, incomplete, or
source-failure outcome and the affected root occurrences. It is not a guessed
package coordinate, does not participate in cycle coalescing, and has no
outgoing expansion.

### Work-budget declaration node

A work-budget declaration node identifies a recursively authorized normalized
declaration that was selected from an acquired manifest but could not be
submitted to #5765 because the declaration-resolution budget was exhausted.
Its identity combines:

- source manifest projection identity;
- owner-issued declaration identity; and
- canonical package ID and constraint.

It retains the exhausted budget kind, its configured limit, and the affected
root occurrences. It is not a guessed package coordinate, does not claim a
resolution attempt or source failure, and has no outgoing expansion.

### Declaration edge

One graph edge represents one normalized selected declaration. Its target is
one exact candidate, declaration-boundary node, failed-resolution declaration
node, or work-budget declaration node. Its semantic identity combines:

- source manifest projection identity and package coordinate;
- owner-issued selected group and declaration identity;
- target package coordinate and owner-issued candidate correspondence,
  declaration-boundary identity, failed-resolution declaration identity, or
  work-budget declaration identity.

The edge retains the complete normalized declaration, including its canonical
constraint and inert source spellings, plus the exact resolution and source
evidence supplied by #5765 when resolution was attempted. It also retains one
emission authority:

- `ResolvedCandidate`, produced by recursive source authorization; or
- `FailedResolution`, produced by recursive source authorization when #5765
  does not issue an exact candidate; or
- `WorkBudgetBoundary`, produced by recursive source authorization when the
  declaration-resolution budget cannot admit the #5765 attempt; or
- `DirectBoundary`, produced for one exact direct-only root occurrence.

Every distinct directed declaration edge remains present. Shared targets,
cycles, and revisits never justify deleting an edge. A recursively authorized
declaration that cannot produce an exact candidate retains its edge to a
failed-resolution declaration node and remains typed failure evidence. It does
not invent an exact candidate or reuse the direct-only boundary state. A
known declaration that cannot be submitted within the work budget retains its
edge to a work-budget declaration node without claiming a resolution attempt.
A direct-only declaration has a boundary edge by contract and is not a failed
candidate attempt.

A direct-only root and a recursive root may share the same exact source node.
The result can then contain both a boundary edge and a resolved edge for the
same normalized declaration, whether their manifest projections are distinct
or owner-issued correspondence coalesces them. Edge emission authority and
per-root admission keep the two authority contexts distinct; a host must not
union them into one root-independent tree.

### Root-relative reachability

The result carries:

- the minimum discovered edge distance for each root occurrence and reachable
  semantic node; and
- the minimum discovered edge distance for each root occurrence and reachable
  source-relative manifest projection; and
- for each graph edge and root occurrence, its admitted occurrence distance
  (`source projection distance + 1`) or the fact that it is not admitted.

An edge is admitted for root occurrence `R` only when both conditions hold:

1. its source manifest projection is reachable from `R` within the depth rule;
2. its emission authority matches `R`: `RecursiveSources` admits only
   `ResolvedCandidate`, `FailedResolution`, and `WorkBudgetBoundary` edges,
   while `DirectDeclarationsOnly` admits only `DirectBoundary` edges issued
   for `R` itself.

Projection correspondence can reuse manifest facts; it never transfers one
root occurrence's expansion authority to another.

This relation is part of the reusable result, not a CLI rendering repair.
Tree, Mermaid, table, JSON, Browser, and count projections must not each
reconstruct it differently.

### Failures and boundaries

A failure retains:

- the phase that failed: candidate resolution, exact manifest acquisition,
  manifest projection, or work budget;
- the source node and declaration or target candidate when applicable;
- the failed-resolution declaration node and edge when candidate resolution
  did not issue an exact target;
- owner-issued source, manifest, or evidence-query failure;
- affected root occurrences; and
- whether usable graph evidence remains.

A shared-node failure may affect several roots while remaining one failed
operation. Consumers can display the affected root set without duplicating the
source operation or changing failure identity.

Depth boundaries are not failures. They retain the endpoint node, affected
roots, requested depth, and the fact that outgoing manifest work was not
requested.

Source boundaries are not failures. They retain the direct declaration edge,
boundary node, affected roots, and the fact that recursive source work was not
authorized.

## Traversal semantics

### Direction and depth

Every edge points from a package to a package it declares as a dependency.
Depth counts edges from each explicit root:

- depth `0` is the root node;
- maximum depth `1` admits direct dependency edges;
- after the root-authority rule selects eligible edges, maximum depth `N`
  admits an edge from a source manifest projection whose distance from that
  root is less than `N`; the edge occurrence is at source distance plus one
  even when its semantic target node has a shorter minimum distance through
  another path; and
- omitted depth requests complete traversal within source authorization and
  finite producer budgets.

A node at the maximum depth remains an endpoint but its manifest is not
acquired solely to determine whether it has outgoing dependencies. If another
root reaches the same node at a shorter distance, that root may require and
authorize expansion.

The Release gate
`Traversal_DepthBoundSkipsEndpointManifestAcquisition` enforces this operation
bound.

### Selected declarations only

Each expanded node contributes declarations from its owner-issued selected
group only. The result retains the selection status so these cases remain
distinct:

- selected non-empty group;
- selected empty group;
- no dependency groups; and
- no matching target framework.

All normalized groups may remain available in package evidence, but traversal
does not merge declarations from non-selected groups. Every transitive
manifest uses the request's typed selection mode. `ManifestDefault` retains
each node's independently selected framework; it is never relabeled as an
exact-TFM graph.

### Per-declaration resolution

For a recursive root, the query schedules one candidate-resolution attempt
for each normalized declaration in the selected group. Resolution happens
before target manifest acquisition.

An exact result adds the target package node and resolved edge. A complete
no-match result, authorization failure, incomplete candidate set, or source
failure adds a failed-resolution declaration node and edge, retains the typed
failure, and schedules no target manifest acquisition. A declaration is exact
only when `PackageDependencyVersionRange.GetExactVersion` returns its canonical
version. For example, `[1.0.0]` is exact while bare `1.0.0` is a
minimum-inclusive range. Only the exact case can bypass complete peer version
enumeration under the Package Source Model's pinned-acquisition rule.

The query never substitutes `VersionRange.MinVersion`, treats an omitted
version as latest, or selects from partial source evidence.

For a direct-only root, the query schedules no candidate resolution and emits
its boundary edges in normalized declaration order.

### Shared nodes and expansion caching

An exact candidate's manifest and selected declarations are invariant within
one owner-issued candidate correspondence. The query single-flights equivalent
candidate correspondence, then reuses that immutable manifest projection for
every root that reaches it.

The traversal memo key is at least as narrow as the package acquisition
registry key. It includes the owner-issued candidate correspondence and the
request's exact acquisition context, so it never shares work across different
authorized-producer sets, cache roots, or acquisition policies.

Reuse does not imply global visitation. Root-relative distance and edge
admission are evaluated separately for every root occurrence and manifest
projection. When a root later reaches a shared projection at a shorter
distance, traversal reuses its adjacency but propagates that root through
every newly in-bound edge permitted by that root's expansion authority.

An explicit root's already-supplied projection is reused without acquisition
only under the owner-issued correspondence rule above. Otherwise the exact
candidate operation remains a distinct projection even when its coordinate
equals a root.

Two supplied roots or acquired candidates with the same exact coordinate may
therefore retain distinct source-relative projections. Equal package
coordinates coalesce the semantic node; only owner-issued correspondence
coalesces the manifest projection and its adjacency.

The Release gates
`Traversal_EquivalentCandidateAcquisitionIsSingleFlight` and
`Traversal_RootRelativeDepthDoesNotUseGlobalVisitedSet` enforce the two sides
of this contract.

### Cycles and revisits

For each root occurrence, a manifest projection is propagated only when first
discovered or when discovered at a shorter distance. Equal or longer revisits
retain their incoming edge but do not re-expand that projection for the root.

This rule terminates cycles in owner-issued projection identity while
preserving the closing edge. It also prevents one root's deeper visit from
suppressing another root's shallower expansion. Finite work budgets remain the
backstop when a source graph exposes an unexpectedly large sequence of
distinct projections or coordinates.

`Traversal_CycleRetainsClosingEdgeAndTerminates` and
`Traversal_SharedNodeRetainsAllParentEdges` are the Release gates.

### Finite work

The query charges manifest-projection expansion and declaration-resolution
budgets before starting the corresponding external work. It does not start
work that the remaining budget cannot admit.

Budget exhaustion retains the unprocessed frontier and affected roots as a
typed boundary, marks their completion partial, and performs no hidden retry.
A declaration selected from an already projected manifest remains a graph edge
to a work-budget declaration node when the remaining declaration-resolution
budget cannot admit its #5765 attempt. A target manifest whose exact candidate
was already resolved remains an unexpanded node frontier when the
manifest-projection budget cannot admit acquisition.
A line limit, row selector, renderer limit, or consumer cancellation is not a
traversal work budget.

Cancellation propagates. It does not publish a success-shaped partial result
or convert cancellation into a source failure.
`Traversal_CancellationDoesNotPublishOutcome` cancels after provisional graph
work begins and gates cancellation identity, absence of result publication,
and absence of source-failure conversion.

## Determinism

The request defines a distance-major stable work schedule:

1. breadth-first distance;
2. root occurrence order;
3. manifest projection admission order within that root and distance;
4. owner-issued selected-group declaration order.

Concurrent source operations may complete in any order, but their outcomes
return to their scheduled slots. Completion timing must not change node, edge,
failure, or reachability ordering.

Result nodes use first scheduled admission, with exact target coordinate as a
stable discriminator for candidates produced by one scheduled batch. Edges,
failures, and boundaries retain their originating schedule order.

Canonical package identity and NuGet version identity drive comparison.
Source labels, descriptions, error text, and other display strings never drive
ordering or joins.

`Traversal_SourceCompletionOrderDoesNotAffectResult` is the Release gate.

## Completion

Each root occurrence has one traversal completion:

- `Complete`: every declaration reachable within the requested boundary was
  resolved and every required manifest projection completed.
- `DepthBounded`: the only unexpanded frontier is caused by the explicit
  maximum depth.
- `SourceBounded`: one or more direct declaration boundaries remain because
  the root did not authorize recursive source work, with no failure inside the
  authorized direct boundary.
- `Partial`: usable graph evidence exists, but candidate, source, manifest,
  projection, or work-budget evidence inside the requested boundary is
  incomplete or failed.

No dependency groups, no matching target-framework group, and a selected
empty group are complete owner-issued selection states, not acquisition
failures. Their distinct statuses remain visible. In particular,
`NoMatchingTargetFramework` means the exact selection request found no exact
or universal group; it is not rendered as "this package has no dependency
declarations."

The result carries summary counts for `Complete`, `DepthBounded`,
`SourceBounded`, and `Partial`, plus `IsComplete` and `IsSuccessful`.
`IsComplete` requires every root to be `Complete`. `IsSuccessful` permits
`Complete`, `DepthBounded`, and `SourceBounded` roots only.

Root admission failures remain outside this query and are composed by the
enclosing dependency document. A mixture of failed root attempts and usable
traversal roots is partial at that document layer, not a claim that every root
failed.

An empty graph is complete only when every root's selected declaration state
establishes no applicable edges. Resolution failure, unavailable sources,
manifest failure, and budget exhaustion never become a dependency-free leaf.

## `InertString` and identity containment

Canonical package IDs, exact NuGet versions, normalized constraints, typed
group identities, and source-result identities are structural currency.

Artifact- or network-origin display evidence remains `InertString`, including:

- source package ID and version-constraint spellings;
- package descriptions and authors;
- configured source labels;
- source and manifest failure messages; and
- retained target-framework source spellings.

The traversal result preserves those values without converting them into raw
renderable strings. Hosts lower `InertString` only through their approved sink
boundaries. Display text never becomes package, node, edge, source, or root
identity.

`Traversal_InertTextRemainsInertThroughGraphResult` is the Release gate.

## Host and rendering boundaries

The query returns typed nodes, edges, reachability, failures, and completion.
It has no Markout, DOM, command-line, console, or filesystem-path dependency.

The CLI:

- decides whether selected sections request traversal;
- supplies roots, source context, target framework, depth, and budgets;
- composes package traversal with other dependency producers;
- lowers the result through Markout; and
- derives diagnostics and exit status from the complete document.

Browser/Wasm:

- supplies the same typed capabilities through the managed engine boundary;
- owns user gestures, operation lifetime, and interactive state; and
- renders typed graph data through its DOM path without duplicating traversal.

The reusable query must remain SRM-only where metadata is involved,
NativeAOT-friendly, free of inspected-assembly loading, and compatible with a
single-threaded Browser/Wasm host. It must not require blocking waits, ambient
filesystem access, reflection-based serialization, or worker threads.

These compatibility properties inherit the repository and project contracts.
The focused implementation gates exercise the managed Browser/Wasm projection
and do not add a redundant repository-wide absence scan.

## Pathological cases

### Root-relative revisit

Given maximum depth `2`:

```text
RootA@1 -> Shared@1
RootB@1 -> Bridge@1 -> Shared@1
Shared@1 -> RootB@1
```

The result must:

- admit `Shared@1 -> RootB@1` for `RootA`, where `Shared@1` is at depth `1`;
- not admit that outgoing edge for `RootB`, where `Shared@1` is at depth `2`;
- retain `Bridge@1 -> Shared@1`;
- retain the cycle-closing edge already admitted for `RootA`; and
- single-flight the source acquisition for one equivalent `Shared@1`
  candidate.

A global visited set keyed by package ID or coordinate fails this case. So
does a graph union without the root-edge admission relation.

### Same package ID, different versions

```text
RootA@1 -> Utility [1.0.0, 2.0.0)
RootB@1 -> Utility [2.0.0, 3.0.0)
```

When authoritative candidates select `Utility@1.5.0` and `Utility@2.4.0`, the
result contains two nodes and two edges. It does not unify them to one project
version, pick `VersionRange.MinVersion`, or discard one edge because the
package IDs match.

### Incomplete candidate evidence

When one eligible authority reports matching versions and another required
authority times out, #5765 cannot issue an authoritative range- or
floating-selected candidate when the missing authority could change the
answer. The traversal retains the declaration edge to a failed-resolution
declaration node, incomplete source evidence, affected roots, and partial
completion. It does not acquire the visible candidate or report a
dependency-free leaf. An owner-classified exact declaration remains governed
by the separate pinned-acquisition rule.

### Mixed expansion authority

When a direct nuspec root and a recursively authorized package root name the
same exact coordinate, owner-issued correspondence may allow their manifest
projection to be shared. The direct root still admits only its own
`DirectBoundary` edges and remains `SourceBounded`; the recursive root admits
only `ResolvedCandidate` edges and may traverse their target projections.
Neither root inherits the other's authority through shared node or projection
identity.

### Repeated direct root

When the same direct nuspec root is supplied twice, the two gestures remain
distinct root occurrences and share only semantic package-node identity. Until
an upstream owner issues typed manifest correspondence, they retain distinct
root-bound manifest projections, boundary nodes, and edge occurrences. Each
root admits only its own edge. Count and graph projections preserve both
supported gestures without guessing that their content observations
correspond.

## Evidence

The implementation adds focused Release gates for:

| Contract | Gate |
| --- | --- |
| Exact coordinates, not package IDs, define nodes. | `Traversal_SamePackageIdDifferentVersionsRemainDistinct` |
| Every normalized directed edge survives sharing and revisits. | `Traversal_SharedNodeRetainsAllParentEdges` |
| Root-relative depth is independent across roots. | `Traversal_RootRelativeDepthDoesNotUseGlobalVisitedSet` |
| Cycles retain their closing edge and terminate. | `Traversal_CycleRetainsClosingEdgeAndTerminates` |
| Endpoint manifests are not acquired beyond the depth request. | `Traversal_DepthBoundSkipsEndpointManifestAcquisition` |
| Equivalent candidate receipts do not repeat manifest acquisition. | `Traversal_EquivalentCandidateAcquisitionIsSingleFlight` |
| Root evidence replaces acquisition only with owner-issued correspondence. | `Traversal_RootEvidenceRequiresCandidateCorrespondenceForReuse` |
| Same-coordinate source projections retain distinct adjacency without changing node identity. | `Traversal_SourceRelativeProjectionPreservesDistinctContent` |
| Direct-only roots emit unresolved boundary edges without source work. | `Traversal_DirectOnlyRootsAreSourceBounded` |
| Failed recursive resolution retains the declaration edge without inventing an exact target. | `Traversal_FailedResolutionRetainsDeclarationEdge` |
| Per-root edge admission intersects depth with expansion authority. | `Traversal_EdgeAdmissionRespectsRootAuthority` |
| Repeated direct roots remain distinct without typed correspondence. | `Traversal_RepeatedDirectRootRequiresCorrespondenceToCoalesce` |
| Typed framework mode, never inert text, controls every group selection. | `Traversal_FrameworkModeIsStructuralCurrency` |
| Manifest-default traversal exercises the package-group owner's no-request query path. | `Traversal_ManifestDefaultUsesOwnerNoRequestSelection` |
| Exact selection retains no-match without compatible fallback. | `Traversal_ExactFrameworkNoMatchRemainsVisible` |
| Manifest-only expansion never downloads a package archive. | `Traversal_ManifestExpansionUsesManifestBytesOnly` |
| Candidate resolver incompleteness is preserved without reinterpretation. | `Traversal_CandidateResolverIncompleteOutcomeRemainsVisible` |
| Owner-classified exact declarations use pinned acquisition. | `Traversal_ExactDeclarationUsesPinnedAcquisition` |
| Bare minimum-inclusive versions are not misclassified as exact. | `Traversal_BareVersionRequiresCandidateResolution` |
| No matching version and source failure remain visible. | `Traversal_ResolutionFailureIsNotDependencyFreeLeaf` |
| Manifest acquisition or projection failure remains visible. | `Traversal_ManifestFailureIsNotDependencyFreeLeaf` |
| Work-budget exhaustion retains known declaration edges, the unprocessed frontier, and partial completion. | `Traversal_WorkBudgetRetainsUnprocessedFrontier` |
| Cancellation after provisional work propagates without publishing an outcome or source failure. | `Traversal_CancellationDoesNotPublishOutcome` |
| Source completion order cannot change result ordering. | `Traversal_SourceCompletionOrderDoesNotAffectResult` |
| Untrusted display evidence remains inert. | `Traversal_InertTextRemainsInertThroughGraphResult` |
| CLI and Browser/Wasm consume equivalent typed graph identity. | `Traversal_HostAdaptersPreserveEquivalentGraph` |

The source and candidate owners keep their existing authority, range-selection,
deadline, and exact-acquisition gates. Traversal tests use those typed outcomes;
they do not manufacture source authorization inside the harness.

## TLA+ disposition

No TLA+ model is selected for the initial implementation. The contract is a
finite deterministic traversal over immutable owner-issued outcomes, and the
pathological graph fixtures above directly cover its cycle, revisit, depth,
and completion properties.

If implementation introduces concurrent mutable publication, retry races, or
incremental graph replacement, that new state machine requires a separate
model decision before adoption. Async source completion alone does not justify
modeling because results are joined back to deterministic schedule slots.

## Non-goals

This owner does not define:

- project restore, direct-dependency-wins, cousin reconciliation, central
  package management, lock files, downgrade warnings, or RID asset selection;
- package-source configuration, mapping, authentication, transport, candidate
  aggregation, version selection, or exact payload authority;
- package discovery, package-prefix expansion, cache policy, or offline
  fallback;
- nuspec XML parsing or package-manifest validation;
- restored-project roots, project-reference edges, or
  `project.assets.json` traversal;
- CLI grammar, section visibility, row selection, graph rendering, or exit
  status; or
- one shared visual layout for terminal and browser hosts.

The current `DependencyNode` tree, global package-ID visited set,
`VersionRange.MinVersion` selection, and legacy manifest fetching are migration
inputs, not contracts to preserve.
