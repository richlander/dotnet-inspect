# External-focused Inspection Graph composition

This document owns the Queries composition that applies
[external-focused call topology](external-focused-call-topology.md) to one
cross-library member call neighborhood and lowers the result into the shared
[Inspection Graph document](inspection-graph-document.md).

Implementation is tracked by
[#7498](https://github.com/richlander/dotnet-inspect/issues/7498); the complete
relationship experience remains
[#7451](https://github.com/richlander/dotnet-inspect/issues/7451).

## Claim and owner

`DotnetInspector.Queries` owns one composition:

1. classify an existing cross-library `CallGraphProjection` against the exact
   assembly generations retained by its `MemberCallGraphSession`;
2. apply outgoing seeded external focus with the selected member as the seed;
   and
3. adapt every retained positive or unclassified evidence row into one
   `InspectionGraphDocument`.

This composition is the default topology for
`MemberCallGraphSession.CrossLibraryCalleeNeighborhood`. Ordinary `Callees`,
`Callers`, and `CrossLibrary` views retain their existing topology.

CallGraph remains the owner of boundary and shortest-connector selection.
Workspace remains the owner of assembly-context participants and acquisition
identity. Inspection Graph remains the owner of the shared graph document.
Queries owns only their exact join and the resulting default for this
cross-library operation.

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

Two registrations of the same exact assembly generation classify alike.
Assemblies with matching names but different versions or module version ids do
not. An unresolved or outside-group endpoint remains unknown even when the
source traversal labels it external.

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

The projection therefore retains every seed-reachable external boundary and
one deterministic shortest hub-local connector to it. It does not continue
through the external participant.

Zero-depth or node-bound source graphs retain the primary seed and their
existing limits without inventing a boundary.

## Inspection Graph lowering

The adapted document contains every row in
`ExternalFocusedCallGraphProjection.EvidenceRows`. Document-local node, edge,
and occurrence ids are dense, while occurrence evidence retains the original
CallGraph row or physical call-site identities.

Each retained edge receives one derived
`queries.call.external-focus-role` characteristic:

- `boundary`;
- `connector`; or
- `unclassified-boundary`.

A row used by positive and unclassified paths is still one `connector`; the
source-relative CallGraph row and occurrences remain singular.

Every unclassified boundary also receives a targeted
`queries.call.external-boundary-classification-incomplete` limit. This
preserves
the CallGraph result's classification boundary in the shared document instead
of dropping the edge or converting unknown membership into absence.

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

The four-step path under #7451 is:

1. #7470: CallGraph-owned boundary and connector projection — complete.
2. #7498: this Queries/Inspection Graph composition.
3. CLI Graph lowers the shared document through Markout.
4. Inspect Web consumes the same host-neutral document.

The two hosts may present roles and incomplete classification differently.
Neither host reclassifies nodes or reselects topology.

## Required gates

Release gates cover:

1. direct root-to-external calls while omitting external-to-external
   continuation;
2. a multi-edge hub-local shortest connector followed by its boundary;
3. edge role characteristics for boundary and connector rows;
4. outside-group endpoints retained as unclassified boundaries with a targeted
   completeness limit;
5. exact generation matching rather than assembly-name or traversal-kind
   inference;
6. physical call-site receipts and existing traversal, node, depth,
   correspondence, and analysis diagnostics surviving filtering;
7. zero-depth and node-bound requests retaining only the primary seed and
   applicable limits; and
8. unchanged ordinary `Callees`, `Callers`, and `CrossLibrary` views.

## Non-claims

This composition does not:

- change Integration Census relationships or `IntegrationGraphProjection`;
- infer package ownership;
- add command syntax or browser interaction;
- traverse beyond the bounded source projection;
- include calls wholly inside an external participant;
- assign dependency strength from breadth, concentration, or call count; or
- compose restored-package provenance and call receipts.
