# External-focused Inspection Graph composition

This document owns the Queries composition that applies the shared
[Inspection Graph focus projection](inspection-graph-focus-projection.md) to
one cross-library member call neighborhood.

The original composition was implemented by
[#7498](https://github.com/richlander/dotnet-inspect/issues/7498). Its transfer
to the shared focus owner and retirement of the parallel CallGraph model are
tracked by [#8444](https://github.com/richlander/dotnet-inspect/issues/8444);
the complete relationship experience remains
[#7451](https://github.com/richlander/dotnet-inspect/issues/7451).

## Claim and owner

`DotnetInspector.Queries` owns one composition:

1. classify an existing cross-library `CallGraphProjection` against the exact
   assembly generations retained by its `MemberCallGraphSession`;
2. adapt that already-bounded topology and every physical receipt into one
   `InspectionGraphDocument`; and
3. apply an outgoing seeded exit-frontier focus request with the selected
   member as its origin.

This composition is the default topology for
`MemberCallGraphSession.CrossLibraryCalleeNeighborhood`. Ordinary `Callees`,
`Callers`, and `CrossLibrary` views retain their existing topology.

CallGraph remains the owner of call traversal, logical rows, and physical
receipts. The shared Queries focus projection owns boundary and
shortest-connector selection. Workspace remains the owner of assembly-context
participants and acquisition identity. Inspection Graph remains the owner of
the shared graph document. This composition owns only their exact join and the
resulting default for this cross-library operation.

## Motivating scenario

The existing independently compiled caller/target fixtures contain:

```text
Caller.Entry.RunAcrossBoundary
  -> Target.Api.Forward
  -> Target.Api.Leaf
```

The first edge crosses from the selected assembly generation into another
assembly-context participant. The second edge is wholly inside that external
participant. An integration-style graph retains the first edge and omits the
second.

The neighboring local-connector case is:

```text
Caller.Entry.RunOuter
  -> Caller.Entry.Run
  -> Target.Api.Ping
```

The result retains both edges. The first is the shortest hub-local connector;
the second is the external boundary.

These fixture shapes gate the composition in CI. The OpenTelemetry 1.18.0
scenario in the CallGraph design remains the real-package evidence for the
user-visible value: 28 ordinary edges become seven boundaries or nine edges
with shortest local connectors.

## Exact assembly-generation classification

The session already owns the exact acquired assembly generations used to build
the catalog call graph. The composition uses the same generation currency:

```text
AssemblyReferenceIdentity + module version id
```

For each call-graph node:

- **hub** means its one unambiguous resolved definition generation equals the
  root assembly generation;
- **external** means that generation equals another successfully acquired
  participant generation in the same assembly-context group; and
- **unknown** means no unambiguous resolved definition generation matches
  either set.

Definition generation is derived from the node's typed definition assembly
identity and physical definition storage evidence. Labels, member spelling,
`CallGraphNodeKind`, assembly-name text, and unresolved resolution hints never
substitute for the exact join.

Assembly identity uses ECMA equivalence, including case-insensitive name,
culture, and public-key-token comparison and equivalent neutral-culture
spellings. Two registrations of the same exact assembly generation therefore
classify alike. Assemblies with matching names but different versions or module
version ids do not. An unresolved or outside-group endpoint remains unknown
even when the source traversal labels it external.

The focus node must classify as hub. Failure to establish that invariant is a
visible query error rather than an empty graph.

## Default request

`CrossLibraryCalleeNeighborhood` keeps its existing finite depth and node
bounds. After constructing that bounded source projection, Queries requests:

- outgoing call direction;
- seeded shortest connectors;
- the source focus node as the only seed;
- the exact root generation as hub; and
- every other successfully acquired participant generation as external.

The focus request selects the Call relationship, outgoing direction, and exit
frontier. It therefore retains every origin-reachable external exit and one
deterministic shortest inside-scope connector to it. It does not continue
through the outside participant.

Zero-depth or node-bound source graphs retain the primary seed and their
existing limits without inventing a boundary.

## Inspection Graph lowering

The source document contains the complete already-bounded call projection
before focus selection. The focused result assigns dense document-local node,
edge, and occurrence ids while preserving original CallGraph row and physical
call-site identities.

Each retained edge receives one additive `queries.focus-role`
characteristic:

- `exit`;
- `connector`; or
- `unclassified-boundary`.

The origin node receives the `focus` role. A row used by several paths remains
one connector; the source-relative CallGraph row and occurrences remain
singular. The external-call CLI maps the shared `exit` role to its existing
`boundary` output token, and Inspect Web maps it to the existing `boundary`
target kind, so user-visible output remains unchanged in both hosts.

Every unclassified boundary also receives a targeted
`queries.focus-scope-classification-incomplete` limit. This preserves the
scope-classification boundary in the shared document instead of dropping the
edge or converting unknown membership into absence. The existing CLI warning
token remains stable at its host lowering boundary.

Existing source facts remain independent:

- traversal incompleteness;
- node and depth bounds;
- physical-occurrence availability;
- catalog correspondence incompleteness; and
- body-analysis failure.

External classification does not strengthen or replace any of them.

## Identity and lifetime

Classification and lowering execute while the owning
`AssemblyContextGroup` retains its snapshots and registrations. The resulting
document remains session-bound whenever its member or occurrence identities
retain acquisition state.

The composition does not mint portable assembly identity, infer package
ownership, or preserve a live Workspace handle in the document. A later
package lens may group exact assembly subjects using its own owner-issued
mapping.

## Production path

This composition participates in the six-step shared focus path:

1. #8403 locked the shared projection contract.
2. #8444 transfers this composition to that owner and retires the CallGraph
   projection after parity.
3. Public API relationships and exposure decisions adopt the projection.
4. Package and assembly affiliation adopt it independently.
5. Typed signal decisions adopt it without moving producer semantics.
6. Integration corridors carry the common result to both hosts; #7595's
   existing CLI Graph path continues lowering this call result through
   Markout, and Inspect Web consumes the same host-neutral document.

The two hosts may present roles and incomplete classification differently.
Neither host reclassifies nodes or reselects topology.

## Required gates

Release gates cover:

1. direct root-to-external calls while omitting external-to-external
   continuation;
2. a multi-edge hub-local shortest connector followed by its boundary;
3. shared focus roles for origin, exit, connector, and unclassified rows;
4. outside-group endpoints retained as unclassified boundaries with a targeted
   completeness limit;
5. exact generation matching, including ECMA-equivalent identity spellings,
   rather than assembly-name or traversal-kind inference;
6. physical call-site receipts and existing traversal, node, depth,
   correspondence, and analysis diagnostics surviving filtering;
7. zero-depth and node-bound requests retaining only the primary seed and
   applicable limits; and
8. unchanged ordinary `Callees`, `Callers`, and `CrossLibrary` views.

`InspectionGraphFocusProjectionTests` owns reusable topology parity.
`MemberCallGraphSessionTests`, `PackageRoleMemberCallGraphQueryTests`, and
`ExternalCallGraphCommandTests` own this adopter's classification,
composition, and CLI-output parity.
`DependencyCallGraphDocument_ProjectsDetachedBrowserGraph` owns the Inspect Web
role-lowering parity.

## Non-claims

This composition does not:

- change Integration Census relationships or `IntegrationGraphProjection`;
- infer package ownership;
- add command syntax or browser interaction;
- traverse beyond the bounded source projection;
- include calls wholly inside an external participant;
- assign dependency strength from breadth, concentration, or call count; or
- compose restored-package provenance and call receipts.
