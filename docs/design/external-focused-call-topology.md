# External-focused call topology

This document records the compatibility baseline for projecting an existing
member call graph onto calls that cross one explicitly classified boundary.
The original CallGraph implementation was delivered by
[#7470](https://github.com/richlander/dotnet-inspect/issues/7470). Its
ownership transfer to the shared Inspection Graph focus projection and
retirement are tracked by
[#8444](https://github.com/richlander/dotnet-inspect/issues/8444); the broader
relationship experience remains
[#7451](https://github.com/richlander/dotnet-inspect/issues/7451).

## Claim and owner

`DotnetInspector.Queries` owns the shared host-neutral projection: given one
Inspection Graph document adapted from an existing `CallGraphProjection`,
explicit inside, outside, and unknown scope decisions, and an exit-frontier
request, return the source call evidence that answers the boundary question
without changing member identity or physical evidence.

Induced exit-frontier mode returns physical calls crossing between inside and
outside subjects. Seeded mode returns directionally reachable exits plus one
deterministic shortest inside-only connector to each exit.

CallGraph continues to own traversal, logical rows, and physical receipts. The
projection does not determine assembly, package, Workspace, or Integration
ownership. The caller supplies scope decisions from owner-issued identity.

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

The source is one immutable `InspectionGraphDocument` adapted from an already
bounded `CallGraphProjection`. Its member subjects, directed Call
relationships, logical-row evidence, and physical call-site receipts are
authoritative. Focus projection never rebuilds traversal or re-resolves a
member.

The caller supplies one decision per classified subject:

- **inside** subjects are members owned by the focal assembly or other focal
  unit;
- **outside** subjects are known not to be owned by that scope; and
- explicit unknown or omitted subjects have **unknown** membership.

Classification is explicit because CallGraph does not own assembly-context,
package, or Workspace identity. A Queries consumer can classify an exact
assembly generation; another consumer can use a different owner-issued focal
unit without changing the topology algorithm. Names, labels, node kind, and
formatted assembly text never substitute for membership.

Every supplied subject must belong to a source node, and one subject cannot
receive conflicting decisions. Focus projection requires at least one inside
subject. A seeded request additionally requires every bound origin to have an
explicit inside decision. An induced request has no origins and retains every
classified exit in its input closure.

## Boundary rows

A source edge is an exit when exactly one endpoint is inside scope and the
other is outside.

Direction is semantic call direction:

- **outgoing** admits `inside -> outside`;
- **incoming** admits `outside -> inside`; and
- **both** admits either shape.

An edge between two inside nodes is local connector evidence. An edge between
two outside nodes is outside the scope question. An edge with one inside
endpoint and one unknown endpoint is an unclassified boundary candidate: it is
not positive external evidence, but it remains visible so projection cannot
turn incomplete membership into a complete absence claim.

Induced mode retains admitted exit rows in source row order. It does not
retain unrelated local edges or outside-to-outside topology.

## Seeded shortest connectors

Seeded mode asks which boundary calls are connected to one or more selected
inside-scope members through the directed inside-only graph.

For an outgoing boundary, the projection searches from the seeds to the
boundary edge's inside source. For an incoming boundary, it searches from the
boundary edge's inside target to the seeds. Only edges whose two endpoints are
inside can be connector steps. Traversal stops at the boundary and never
crosses through an outside or unknown node.

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

- each retained member subject and `GraphNodeIdentity`;
- the Call relationship and its semantic direction;
- original logical-row evidence;
- every physical call-site occurrence referenced by a retained edge;
- repeated physical call sites and edge multiplicity;
- loop and dispatch evidence already attached to the source; and
- source traversal and analysis boundaries.

The result may assign new dense document-local ids, but it does not replace
owner-issued subject, relationship, or occurrence identity. Hosts can
therefore navigate from a filtered transition back to the exact source row and
receipts without reconstructing correspondence.

Positive exits and unclassified boundary evidence retain distinct additive
roles. Each unclassified edge also carries a targeted scope-classification
limit so a consumer can render unknown candidates and their receipts without
recovering nodes from source labels.

## Completion

Positive retained boundary rows and connectors remain valid when the source
graph or membership classification is incomplete.

The absence of a targeted scope-classification limit reports that every
directionally relevant edge incident on the inside scope has a classified
opposite endpoint. That membership statement is separate from the source
graph's traversal and analysis boundaries. Stopping at a known outside node is
expected and does not by itself make boundary classification incomplete; a
depth-limited inside node may still limit the source population.

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

1. #7470 established the call-specific compatibility behavior.
2. [#7498](https://github.com/richlander/dotnet-inspect/issues/7498)
   classified exact Workspace participants and composed the first
   Inspection Graph result.
3. #8444 moves topology selection to the shared focus projection and retires
   the call-specific implementation after parity.
4. #7595's CLI Graph path continues lowering the shared result through
   Markout; Inspect Web consumes the same host-neutral result through its graph
   viewer.

The CLI and browser may render the projection differently. Neither host
reimplements boundary or shortest-connector selection.

## Required gates

Release gates cover:

1. outgoing and incoming induced-frontier projection;
2. a boundary edge incident directly on the seed;
3. a shortest multi-edge local connector;
4. stable equal-length tie-breaking by source row sequence;
5. local cycles without repeated connector nodes;
6. disconnected boundary omission in seeded mode;
7. repeated physical sites remaining attached to one retained logical row;
8. unknown membership remaining visible and preventing complete absence;
9. a seed-disconnected unknown boundary remaining visible without a connector;
10. preservation of source traversal and analysis boundaries; and
11. request rejection for foreign, conflicting, or invalid membership while
    omitted classification remains explicit unknown.

## Non-claims

This projection does not:

- derive assembly, package, Workspace, or Integration ownership;
- invent package or assembly call edges;
- compute direct-use clusters, breadth, concentration, or strength;
- compose restored-package, acquisition, or call receipt chains;
- traverse beyond the source `InspectionGraphDocument`;
- infer reflection, delegates, dynamic dispatch, or runtime virtual targets;
- enumerate every equal shortest connector;
- define command spelling, output formatting, or browser interaction; or
- change ordinary call-graph defaults outside an adopting consumer.
