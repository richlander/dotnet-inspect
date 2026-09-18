# External-focused call topology

This document owns CallGraph's focused projection of an existing member call
graph onto the calls that cross one explicitly classified hub boundary.
Implementation is tracked by
[#7470](https://github.com/richlander/dotnet-inspect/issues/7470); the broader
relationship experience remains
[#7451](https://github.com/richlander/dotnet-inspect/issues/7451).

## Claim and owner

`ILInspector.CallGraph` owns one host-neutral projection: given one existing
`CallGraphProjection`, explicit hub, external, and unknown node membership, and
an external-focus request, return the source call rows that answer the boundary
question without changing their member identity or physical evidence.

Boundary-only mode returns physical calls crossing between the hub and an
external node. Seeded-connector mode returns directionally reachable boundary
calls plus one deterministic shortest hub-local connector to each boundary.

The projection does not determine assembly, package, Workspace, or Integration
ownership. The caller supplies that classification from owner-issued identity.

## Motivating asset

The motivating asset is `OpenTelemetry` 1.18.0 under the restored `net11.0`
closure of `OpenTelemetry.Extensions.Hosting` 1.18.0. The focal member is:

```text
Microsoft.Extensions.DependencyInjection.
  ProviderBuilderServiceCollectionExtensions.
  AddOpenTelemetrySharedProviderBuilderServices(IServiceCollection)
```

The ordinary bounded graph contains 28 logical call edges. Seven cross an
assembly boundary. Boundary-only projection therefore removes 75% of the
edges. One relevant `OpenTelemetry.Api` call is reached through two local
edges:

```text
AddOpenTelemetrySharedProviderBuilderServices
  -> Sdk.get_SuppressInstrumentation
  -> SuppressInstrumentationScope.get_IsSuppressed
  -> OpenTelemetry.Api: RuntimeContextSlot<T>.Get
```

Boundary plus shortest connectors retains nine edges and removes 68% of the
ordinary graph. The full evidence, exact pair characteristics, and observed
IVT ingress are recorded in the
[#7451 evidence spike](https://github.com/richlander/dotnet-inspect/issues/7451#issuecomment-5721529695).

The deterministic caller-graph fixtures preserve the contract shapes in CI:
direct boundaries, local connectors, cycles, repeated physical calls, and
unresolved external endpoints. The NuGet scenario remains reproducible design
evidence rather than adding network acquisition to this projection's unit
gate.

## Basis

The conventional baseline separates a component boundary overview from
member-level contributing relationships and from path explanation:

- [Visual Studio Code Maps](https://github.com/MicrosoftDocs/visualstudio-docs/blob/9ec8e00baef5d1218d82afe9ebdafa8e9bc73ab2/docs/modeling/map-dependencies-across-your-solutions.md#L74-L124)
  groups dependencies at assembly level, then expands contributing links to
  the participating items and original relationships.
- [NDepend coupling and path graphs](https://www.ndepend.com/docs/visual-studio-dependency-graph#Coupling-Graph)
  separate detailed caller/callee coupling from a
  [minimal path](https://www.ndepend.com/docs/visual-studio-dependency-graph#Path-Graph)
  and from the distinct
  [all-paths operation](https://www.ndepend.com/docs/visual-studio-dependency-graph#All-Paths-Graph).
- [CodeQL path queries](https://github.com/github/codeql/blob/2304a1a416aea54a4d8b4f7ee98436e658563af7/docs/codeql/writing-codeql-queries/creating-path-queries.rst#L13-L23)
  bind source and sink endpoints to an explicit edge relation and can restrict
  the displayed graph through an explicit node set.
- [Visual Studio Call Hierarchy](https://github.com/MicrosoftDocs/visualstudio-docs/blob/main/docs/ide/call-hierarchy.md#L39-L58)
  retains the source locations contributing to one logical caller/callee
  relationship.

This projection follows those separations while being stricter about evidence:
the boundary view keeps original member call rows and every retained physical
IL receipt, and the connector view is explicitly shortest-path rather than
all-path reachability.

It deliberately does not use transitive reduction. Graphviz
[`tred`](https://github.com/rossbar/graphviz/blob/c50674b82582171aae78ea629f2d7c1891010101/cmd/tools/tred.1#L12-L23)
removes edges implied by longer paths and can be non-unique for cycles. That
operation may delete a genuine direct cross-boundary call and therefore does
not preserve this product's evidence question.

## Source and explicit membership

The source is one immutable `CallGraphProjection`. Its nodes, directed edges,
stable row numbers, and physical call-site receipts are authoritative. External
focus never rebuilds traversal or re-resolves a member.

The caller classifies source node ids into disjoint sets:

- **hub** nodes are members owned by the focal assembly or other focal unit;
- **external** nodes are known not to be owned by that hub; and
- unlisted nodes have **unknown** membership.

Classification is explicit because CallGraph does not own assembly-context,
package, or Workspace identity. A Queries consumer can classify an exact
assembly generation; another consumer can use a different owner-issued focal
unit without changing the topology algorithm. Names, labels, node kind, and
formatted assembly text never substitute for membership.

Every supplied node id must belong to the source projection. Hub and external
membership must not overlap. Boundary-only projection requires at least one hub
node. Seeded projection additionally requires at least one seed node, and every
seed must be a hub node.

## Boundary rows

A source edge is a boundary row when exactly one endpoint is a hub node and the
other is an external node.

Direction is semantic call direction:

- **outgoing** admits `hub -> external`;
- **incoming** admits `external -> hub`; and
- **both** admits either shape.

An edge between two hub nodes is local connector evidence. An edge between two
external nodes is outside the hub question. An edge with one hub endpoint and
one unknown endpoint is an unclassified boundary candidate: it is not positive
external evidence, but it remains visible so projection cannot turn incomplete
membership into a complete absence claim.

Boundary-only mode retains admitted boundary rows in source row order. It does
not retain unrelated local edges or external-to-external topology.

## Seeded shortest connectors

Seeded mode asks which boundary calls are connected to one or more selected hub
members through the directed hub-local graph.

For an outgoing boundary, the projection searches from the seeds to the
boundary edge's hub source. For an incoming boundary, it searches from the
boundary edge's hub target to the seeds. Only edges whose two endpoints are hub
nodes can be connector steps. Traversal stops at the boundary and never crosses
through an external or unknown node.

The boundary row remains the final outgoing step or first incoming step. A
boundary incident directly on a seed has a zero-edge local connector.
Disconnected positive external boundaries are outside the seeded question and
are omitted. A directionally relevant unknown boundary remains visible even
when it is disconnected because boundary classification is independent of
seed reachability; it has no connector in that case and prevents a complete
classification result.

The source graph is already finite and bounded. Breadth-first search supplies
minimum connector depth. Equal-length alternatives choose the
lexicographically smallest source row-number sequence. At most one connector
is retained for each boundary row, so the projection does not enumerate every
equal shortest path.

Rows shared by several connectors appear once. The final row set is reported
in source row order, independent of seed or membership input order.
A seeded result retains its seeds even when no boundary is reachable.

## Identity and physical evidence

External focus is a source-relative projection. It preserves:

- each retained `CallGraphNode` and `GraphNodeIdentity`;
- each retained `CallGraphRow` and original row number;
- each retained `CallGraphEdge` and call direction;
- every `CallGraphCallSite` referenced by a retained edge;
- repeated physical call sites and edge multiplicity;
- loop and dispatch evidence already attached to the source; and
- source traversal and analysis boundaries.

The result does not mint new logical node, edge, or occurrence identities.
Hosts and later Inspection Graph composition can therefore navigate from a
filtered transition back to the exact source row and receipts without
reconstructing correspondence.

Positive rows and unclassified boundary evidence remain distinct collections.
The result also exposes their ordered union so a consumer can render the
unknown candidates and their receipts without recovering nodes from the source
by label.

## Completion

Positive retained boundary rows and connectors remain valid when the source
graph or membership classification is incomplete.

The projection reports whether every directionally relevant edge incident on
the hub has a classified opposite endpoint. That membership statement is
separate from the source graph's traversal and analysis boundaries. Stopping
at a known external node is expected and does not by itself make boundary
classification incomplete; a depth-limited hub-local node may still limit the
source population.

The projection makes no new traversal-exhaustiveness claim. Consumers preserve
the source boundaries and combine them with the membership result according to
their own scoped absence question. Unknown membership is not silently treated
as external, local, or absent. External focus adds no success-shaped fallback
when request validation fails.

## Integration-style default and production path

The owner direction under #7451 is that integration-style call graphs use
external focus by default because their question is always about external
dependencies. This document defines the reusable call-topology projection; it
does not redefine Integration relationships or Workspace classification.

The production path is:

1. #7470 implements this CallGraph-owned projection and deterministic gates.
2. [#7498](https://github.com/richlander/dotnet-inspect/issues/7498)
   classifies exact Workspace participants and selects external focus for
   integration-style call graphs.
3. #7595: CLI Graph lowers the shared result through Markout.
4. Inspect Web consumes the same host-neutral result through its graph viewer.

The CLI and browser may render the projection differently. Neither host
reimplements boundary or shortest-connector selection.

## Required gates

Release gates cover:

1. outgoing and incoming boundary-only projection;
2. a boundary edge incident directly on the seed;
3. a shortest multi-edge local connector;
4. stable equal-length tie-breaking by source row sequence;
5. local cycles without repeated connector nodes;
6. disconnected boundary omission in seeded mode;
7. repeated physical sites remaining attached to one retained logical row;
8. unknown membership remaining visible and preventing complete absence;
9. a seed-disconnected unknown boundary remaining visible without a connector;
10. preservation of source traversal and analysis boundaries; and
11. request rejection for foreign, overlapping, or invalid membership.

## Non-claims

This projection does not:

- derive assembly, package, Workspace, or Integration ownership;
- invent package or assembly call edges;
- compute direct-use clusters, breadth, concentration, or strength;
- compose restored-package, acquisition, or call receipt chains;
- traverse beyond the source `CallGraphProjection`;
- infer reflection, delegates, dynamic dispatch, or runtime virtual targets;
- enumerate every equal shortest connector;
- define command spelling, output formatting, or browser interaction; or
- change ordinary call-graph defaults outside an adopting consumer.
