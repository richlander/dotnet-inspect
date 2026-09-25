# Package dependency call-graph operation

This document owns the PackageQueries composition that executes a completed
package dependency traversal and projects one dependency-aware member call
graph before transient Package Source and assembly-context resources close.

Implementation is tracked by
[#8076](https://github.com/richlander/dotnet-inspect/issues/8076) and
[#8309](https://github.com/richlander/dotnet-inspect/issues/8309).
[Member call-graph supply-chain focus](member-call-graph-supply-chain-focus.md)
composes a subtractive Package-interest baseline under
[#8334](https://github.com/richlander/dotnet-inspect/issues/8334). The broader
Workspace adoption remains
[#6638](https://github.com/richlander/dotnet-inspect/issues/6638).

## Claim and owner

`DotnetInspector.PackageQueries` owns one composition:

1. validate the complete set of exact edge-realization executions before
   starting Package Source work;
2. execute those edges deterministically and sequentially through one
   `PackageSourceOperationLease`;
3. invoke
   [package dependency Workspace routes](package-dependency-workspace-routes.md)
   once;
4. build one Workspace-owned package implementation context from the exact
   root and realized dependency bindings;
5. classify the graph's hub and external membership from the exact
   participant-to-Package associations, then invoke the existing
   Queries-owned external-focused member call graph for one exact
   implementation MethodDef; and
6. return detached route and graph evidence only after source and
   package-role cleanup has completed successfully.

[Package dependency traversal](package-dependency-traversal.md) owns graph
selection and bounds.
[Package dependency edge realization](package-dependency-edge-realization.md)
owns one candidate-bound PackageHouse request.
PackageHouse owns destination realization and Platform pruning.
Workspace routes own atomic Package contribution and destination lowering.
Queries owns package-role contexts, member call-graph analysis, external-focus
topology, and Inspection Graph lowering.

This composition does not redefine those contracts.

## Motivating scenario

An Inspect Web member call graph for
`Microsoft.Extensions.Http.Polly` should not require the user to independently
select compatible Polly packages.

The selected root asset and member remain exact. Dependency traversal uses its
independent target framework policy, `net12.0` by default, and PackageHouse
realizes the versions and assets authorized by that traversal. The completed
graph can therefore show calls into the dependency assemblies selected for the
root package rather than a manually assembled and potentially version-skewed
participant set.

The neighboring case is a dependency supplied by the active Platform. Its
route remains a Platform destination and no redundant Package Root or Package
assembly participant is fabricated.

## Request

One `PackageDependencyMemberCallGraphRequest` carries:

- one exact `InspectionWorkspace`;
- one captured `WorkspaceScopeSnapshot`;
- one completed `PackageDependencyTraversalOutcome`;
- one exact live `PackageRootBinding` per traversal root occurrence;
- one prepared `PackageDependencyEdgeRealizationExecution` for every admitted
  resolved-candidate edge occurrence;
- one exact root focus;
- finite external-focused call-graph depth and node bounds; and
- one finite Workspace deadline.

The focus is:

```text
root occurrence index
+ implementation module version id
+ MethodDef token
```

The module generation and MethodDef are the analysis identity. The root
occurrence proves that the focus belongs to the selected root package rather
than to a dependency with coincidentally matching metadata.

The request does not carry type or member display text. Hosts resolve their
selected subject before this operation and retain exact implementation
identity; the operation never repeats selection from labels.

Execution receives one `PackageHouse` and one caller-issued
`PackageSourceOperationLease`. The lease carries the caller cancellation,
request timeout, and operation ceiling. The composition does not discover
ambient sources, replace host authorization, or create a second source
operation.

## Complete preparation

Every admitted edge whose authority is `ResolvedCandidate` requires exactly
one prepared execution. No other edge may have one.

Before the first PackageHouse call, the operation validates:

- traversal reference identity;
- root occurrence and edge occurrence identity;
- exact resolved candidate identity;
- exact traversal target framework;
- shared Package Source generation and deadline compatibility;
- one-to-one coverage without duplicates; and
- deterministic root-occurrence then edge order.

Preparation failure therefore cannot leave a partially executed source
operation or a partially published Workspace Scope.

## Sequential source execution

`PackageSourceOperationLease` permits one active source step. The operation
executes prepared edges serially in root-occurrence then traversal-edge order.
It never fans PackageHouse calls out in parallel.

After every settled edge, the operation observes the source lease's caller
cancellation and operation deadline before starting the next edge. A thrown
acquisition, cancellation, or timeout failure stops execution. Later edges do
not run and Workspace publication does not begin.

The operation owns disposal of the supplied source lease immediately after the
last source step. Workspace publication, package-context preparation, and
graph analysis retain and observe the caller cancellation token but do not
unnecessarily pin the Package Source generation. Cancellation after
publication still releases every acquired package-role resource and cannot
produce completed graph output. No source lease or active Package Source work
escapes.

## Workspace publication

After every required edge has settled, the operation invokes
`PackageDependencyWorkspaceRouteQuery` exactly once with the complete
realization set.

A committed or no-effect Scope result permits package-role construction. Any
other Scope result is returned as a typed not-completed outcome with no graph.
When a no-effect publication reuses a logical Package occurrence from another
content generation, graph construction fails visibly unless the completed
route retains the exact binding for that final occurrence. It never silently
omits the routed Package from the implementation context.

The route result remains authoritative:

- a Package contribution identifies one final Workspace Package occurrence;
- a supplied-root route reuses its exact final occurrence;
- Platform delegation remains a Platform destination;
- a traversal boundary remains unavailable; and
- a PackageHouse no-contribution result remains unavailable.

The operation does not turn Platform or unavailable destinations into Package
bindings.

## Package implementation context

The graph package set always contains the focused root binding. For each
canonical package ID, it selects at most one exact final Workspace occurrence:
another traversal root precedes dependency routes; dependency routes select
the nearest root-relative occurrence, then the highest resolved version at
that distance. The Packages layer owns NuGet version-precedence comparison;
PackageQueries consumes that result without acquiring NuGet libraries. Scope
order remains the final binding order.

This graph-only coalescing prevents multiple routed versions of one package
from contributing duplicate assembly identities. Completed routes retain
every traversal candidate, exact realization, and final Workspace occurrence;
the operation does not rewrite traversal or route evidence. Supplied-root
destinations are already eligible through the root set. Unrelated packages
already present in the Scope do not enter this demand.

The operation prepares one
`PackageAssemblyContextCompletion` and one demand-local projection from those
exact bindings. The implementation role is the call-graph analysis universe
when it exists. Packages whose selected `lib/` assets serve both roles use the
shared surface role. Reference-only assets remain visible in detached route
evidence but cannot manufacture implementation bodies.

The exact focus must identify one implementation participant belonging to the
declared root occurrence. Missing, duplicate, cross-root, bodyless, or invalid
MethodDef identity is a visible failure before call-graph construction.

The same live participant mapping classifies external-focus membership. A graph
node uniquely owned by the focused root Package is a hub node, including nodes
from another implementation assembly in that Package. A node uniquely owned by
another admitted Package is external. Absent or ambiguous Package ownership is
unknown. Classification prefers the graph node's unambiguous resolved
definition assembly identity. When definition identity is unavailable, an exact
call-site assembly reference classifies the node only when the terminal
type-resolution identity names the same assembly. A conflicting facade and
terminal identity, simple assembly name, graph label, route order, or detached
display coordinate does not establish ownership.

## Call-graph projection

A Queries-owned package-role call-graph query receives the demand-local
projection and exact focus. It:

1. locates the implementation participant by exact Package root slot and
   module version id;
2. validates the MethodDef and managed body;
3. creates `MemberCallGraphSession` over the existing implementation role;
4. supplies package-scoped hub, external, and unknown membership from the live
   package-role participants;
5. invokes the existing seeded outgoing external-focus projection; and
6. returns the existing `InspectionGraphDocument`.

CallGraph remains the owner of external boundaries and shortest connectors.
Queries remains the owner of package-role membership classification and
Inspection Graph adaptation. The ordinary non-package session path retains its
assembly-generation focus. PackageQueries neither reclassifies detached graph
nodes nor infers ownership from labels.

Platform destinations do not fabricate package participants in this slice.
Calls whose definitions are outside the package implementation role retain the
existing typed unclassified-boundary behavior. A later Platform composition
may supply exact Platform participants without changing this operation's
Package route contract.

## Detached completion

The completed result contains:

- the detached traversal target policy and completion summary;
- one detached route summary for every admitted edge occurrence;
- the final Workspace Scope revision identity and Package occurrence
  descriptors needed to explain Package destinations; and
- one detached `InspectionGraphDocument`.

It does not expose:

- `PackageSourceOperationLease`;
- `PackageHouseSettlement`;
- `PackageRootBinding`;
- `PackageAssemblyContextCompletion`;
- `PackageAssemblyContextProjection`;
- `AssemblyContextGroup`; or
- `InspectionWorkspace`.

The route summary copies only resource-free owner-issued facts from the
Workspace route result. It never retains the route result's live realization
or settlement objects.

The operation returns success only after the projection has returned and the
package-role completion reports successful release of every owned group.
Cleanup failure is a visible operation failure, not a successful graph with a
discarded diagnostic. It remains authoritative when graph analysis also
throws, including caller cancellation; the graph-phase exception is rethrown
only after successful package-role cleanup.

## Failure algebra

Expected non-success outcomes are:

- Workspace route publication did not commit;
- focus does not identify one exact root implementation MethodDef; and
- package-role cleanup failed.

Programmer-contract violations, PackageHouse acquisition failures, package
implementation-context construction failures, caller cancellation, source
timeout, Workspace execution exceptions, and call-graph query exceptions
retain their existing exception semantics.

No non-success outcome contains an `InspectionGraphDocument`.

## Dependency demand

The graph walks the bodies of connector Packages and stops at boundary
Packages. [Member call-graph supply-chain
focus](member-call-graph-supply-chain-focus.md) classifies the focused root and
the selected baseline Packages as connectors, and every other known Package as
a highlighted boundary. A boundary call ends the graph at that call (gate 11).
A boundary Package therefore contributes only the definition identity and
Package ownership that classify its nodes. A connector Package contributes its
bodies too.

Measured on the CLI's real-network scenarios (2026-09-24). Every admitted edge
is realized with its implementation, and every participant's bodies are
analyzed:

| Root | Packages realized | Assemblies the graph's edges reach | Received | Wall time |
| --- | --- | --- | --- | --- |
| `OpenTelemetry` 1.18.0, `net10.0` | 16 | 4 and `System.Private.CoreLib` | 6.5 MB | 13.5 s |
| `Microsoft.Extensions.Http.Polly` 11.0.0-rc.1 | 10 | 2 and `System.Private.CoreLib` | 3.2 MB | 7.7 s |

Every dependency archive in both scenarios is under the
[size cut](package-cache-policy.md#size-first), so bytes are not the cost.
Realizing and analyzing implementation the graph never follows is the cost.

Two changes follow. Each keeps the exact root, the traversal, the route
contracts, ownership classification, baseline classification, and the
external-focused projection unchanged.

1. **Demand follows the baseline class.** A Package's baseline class is known
   before the graph is built: it is a function of the Package ID, the
   captured registration revision, and the baseline selection, not of the
   graph. Each admitted edge is realized with its class's demand:
   - a connector Package, meaning a baseline Package under the request's
     baseline, is realized with `SurfaceAndImplementation`, because the graph
     walks its bodies;
   - a boundary Package is realized with
     [`Surface`](package-read-demand.md#asset-demand), because the graph needs
     only its definitions and forwarders.

   The implementation role, which is the analysis universe, then holds the
   root's and the connectors' implementations only. Boundary participants
   join the surface role, where type resolution and ownership classification
   find them.
2. **Only reached Packages are realized.** Expansion runs in rounds:
   1. The first round builds the call tree over the root alone, within the
      request's depth and node bounds.
   2. The round collects the assembly references that the tree's calls use
      but that no realized participant defines.
   3. It then realizes the admitted edges whose Packages supply those
      assemblies, each with its class's demand.
   4. A realized connector extends the next round's tree. A boundary never
      does.

   A Package's supplied assemblies are read from the edge candidate's archive
   directory, through a [document demand](package-read-demand.md#document-demand)
   that names no entries, so only the root folder and the directory are read.
   Forwarders into another Package's assembly are references like any other.
   Expansion ends when a round finds no new Package, or when the depth or
   node bound ends the tree, and the graph is built over the realized set.

   An edge no round reaches is not realized. Its route summary says so, as
   `NotReached`. That is a resource-free fact of this operation, not a
   Workspace route outcome, and the detached result still carries one route
   summary per admitted edge. A referenced assembly that no admitted edge
   supplies remains unknown ownership, exactly as today.

The first change needs no new ordering. Complete preparation, one sequential
source execution, and one Workspace publication all stay as they are, with a
per-edge demand. The second changes execution and publication, not
preparation:
- every admitted edge's execution is still validated before the first source
  call;
- source execution becomes one sequential step per round, under the same
  lease; and
- Workspace publication happens once per round, with the round's
  contributions.

A cancellation, timeout, or failure in any round stops expansion with the
same outcomes as today.

## Production path

The four slices are:

1. [single-edge realization](package-dependency-edge-realization.md);
2. [Workspace route composition](package-dependency-workspace-routes.md);
3. this shared source operation and dependency-aware call-graph service; and
4. CLI and Browser/Wasm adoption through one
   `InspectionEnvelope<TContent>`, owned by
   [Package dependency member call-graph inspection](package-dependency-member-call-graph-inspection.md).

The fourth slice owns command syntax, browser interaction, host source
capabilities, output lowering, and final envelope diagnostics. Both hosts call
this operation rather than assembling dependency participants themselves.

## Required gates

Release gates cover:

1. all admitted edge executions are validated before the first source call;
2. several resolved edges execute serially in root-occurrence then edge order;
3. execution failure, caller cancellation, or source timeout prevents
   Workspace publication and graph construction, while cancellation after
   publication still releases package-role resources and prevents completed
   graph output;
4. Package contributions publish once while supplied-root, Platform, and
   unavailable routes preserve their typed outcomes;
5. root selection and the independent default or explicit traversal TFM
   remain distinguishable;
6. every traversal root binding identifies its exact retained generation and
   selection in the captured Workspace Scope;
7. only exact routed Package occurrences enter the package-role demand;
8. a reused logical Package occurrence from another generation fails visibly
   rather than being omitted from graph analysis;
9. the exact root implementation MethodDef remains the graph seed;
10. every uniquely owned focused-root Package node is a hub node while every
    uniquely owned dependency Package node is external;
11. a dependency boundary call enters the external-focused graph without
    retaining dependency-internal continuation, while an unknown endpoint
    remains visibly unclassified, including a facade reference whose forwarded
    definition ownership is incomplete;
12. route and graph evidence remain usable after source-operation and
   package-role cleanup;
13. cleanup failure cannot return a success-shaped graph and remains
    authoritative over simultaneous graph-phase cancellation; and
14. existing edge realization, Workspace route, package-role, and ordinary
    call-graph behavior remain unchanged;
15. boundary dependency edges are realized with `Surface` and connector edges
    with `SurfaceAndImplementation`, the implementation role holds only the
    root and connectors, and the graph's nodes, classification, and output
    equal those of all-implementation realization on the real `OpenTelemetry`
    and `Polly` scenarios under each baseline; and
16. an admitted edge that no expansion round reaches is not realized, its
    route summary is `NotReached`, and the graph's output is unchanged.

## Non-claims

This composition does not:

- add CLI or Inspect Web behavior;
- change root package, asset, type, or member selection;
- change dependency traversal or its `net12.0` default;
- acquire an unbounded dependency closure;
- follow calls into boundary Package bodies;
- redefine PackageHouse realization or Platform pruning;
- add active Platform assemblies to the call-graph context;
- change external-focused topology;
- add a host graph-policy selector;
- infer Package ownership from graph labels;
- retain live resource owners in completed results; or
- return `InspectionEnvelope<TContent>` before a production host consumes the
  operation.
