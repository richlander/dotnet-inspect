# Package dependency call-graph operation

This document owns the PackageQueries composition that executes a completed
package dependency traversal and projects one dependency-aware member call
graph before transient Package Source and assembly-context resources close.

Implementation is tracked by
[#8076](https://github.com/richlander/dotnet-inspect/issues/8076). The broader
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
5. invoke the existing Queries-owned external-focused member call graph for
   one exact implementation MethodDef; and
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
last source step. Workspace publication and graph analysis retain the caller
cancellation token but do not unnecessarily pin the Package Source generation.
No source lease or active Package Source work escapes.

## Workspace publication

After every required edge has settled, the operation invokes
`PackageDependencyWorkspaceRouteQuery` exactly once with the complete
realization set.

A committed or no-effect Scope result permits package-role construction. Any
other Scope result is returned as a typed not-completed outcome with no graph.

The route result remains authoritative:

- a Package contribution identifies one final Workspace Package occurrence;
- a supplied-root route reuses its exact final occurrence;
- Platform delegation remains a Platform destination;
- a traversal boundary remains unavailable; and
- a PackageHouse no-contribution result remains unavailable.

The operation does not turn Platform or unavailable destinations into Package
bindings.

## Package implementation context

The graph package set contains:

- every traversal root binding; and
- every distinct contributed Package binding named by a completed Package
  route.

Supplied-root destinations are already present in the root set. Bindings are
deduplicated by their exact final Workspace Package occurrence and ordered by
that occurrence in the final Scope. When several traversal root slots map to
one occurrence, the selected focus root binding represents that occurrence.
Unrelated packages already present in the Scope do not enter this demand.

The operation prepares one
`PackageAssemblyContextCompletion` and one demand-local projection from those
exact bindings. The implementation role is the call-graph analysis universe
when it exists. Packages whose selected `lib/` assets serve both roles use the
shared surface role. Reference-only assets remain visible in detached route
evidence but cannot manufacture implementation bodies.

The exact focus must identify one implementation participant belonging to the
declared root occurrence. Missing, duplicate, cross-root, bodyless, or invalid
MethodDef identity is a visible failure before call-graph construction.

## Call-graph projection

A Queries-owned package-role call-graph query receives the demand-local
projection and exact focus. It:

1. locates the implementation participant by exact Package root slot and
   module version id;
2. validates the MethodDef and managed body;
3. creates `MemberCallGraphSession` over the existing implementation role;
4. invokes `CrossLibraryCalleeNeighborhood`; and
5. returns the existing `InspectionGraphDocument`.

CallGraph remains the owner of external boundaries and shortest connectors.
Queries remains the owner of exact assembly-generation classification and
Inspection Graph adaptation. PackageQueries neither reclassifies graph nodes
nor infers ownership from labels.

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
discarded diagnostic.

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

## Production path

The four slices are:

1. [single-edge realization](package-dependency-edge-realization.md);
2. [Workspace route composition](package-dependency-workspace-routes.md);
3. this shared source operation and dependency-aware call-graph service; and
4. CLI and Browser/Wasm adoption through one
   `InspectionEnvelope<TContent>`.

The fourth slice owns command syntax, browser interaction, host source
capabilities, output lowering, and final envelope diagnostics. Both hosts call
this operation rather than assembling dependency participants themselves.

## Required gates

Release gates cover:

1. all admitted edge executions are validated before the first source call;
2. several resolved edges execute serially in root-occurrence then edge order;
3. execution failure, caller cancellation, or source timeout prevents
   Workspace publication and graph construction;
4. Package contributions publish once while supplied-root, Platform, and
   unavailable routes preserve their typed outcomes;
5. root selection and the independent default or explicit traversal TFM
   remain distinguishable;
6. every traversal root binding identifies its exact retained generation and
   selection in the captured Workspace Scope;
7. only exact routed Package occurrences enter the package-role demand;
8. the exact root implementation MethodDef remains the graph seed;
9. a dependency boundary call enters the external-focused graph without
   retaining dependency-internal continuation;
10. route and graph evidence remain usable after source-operation and
   package-role cleanup;
11. cleanup failure cannot return a success-shaped graph; and
12. existing edge realization, Workspace route, package-role, and ordinary
    call-graph behavior remain unchanged.

## Non-claims

This composition does not:

- add CLI or Inspect Web behavior;
- change root package, asset, type, or member selection;
- change dependency traversal or its `net12.0` default;
- acquire an unbounded dependency closure;
- redefine PackageHouse realization or Platform pruning;
- add active Platform assemblies to the call-graph context;
- change external-focused topology;
- infer Package ownership from graph labels;
- retain live resource owners in completed results; or
- return `InspectionEnvelope<TContent>` before a production host consumes the
  operation.
