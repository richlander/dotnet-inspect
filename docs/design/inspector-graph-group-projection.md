# Inspector.Graph group projection

## Status, owner, and claim

Status: **design contract** for
[#9051](https://github.com/richlander/dotnet-inspect/issues/9051), a focused
successor under
[#8669](https://github.com/richlander/dotnet-inspect/issues/8669) and the
learn-the-codebase scenario in
[#8744](https://github.com/richlander/dotnet-inspect/issues/8744).

The **Inspector.Graph Group Projection** owner defines this claim:

> Given one immutable Graph document, a complete caller-selected assignment
> from every canonical node to one of its admitted groups, and a typed
> relationship selection, Graph issues the exact contracted group topology.
> Every selected canonical edge is retained exactly once as either a
> cross-group projected-edge contribution or a within-group contribution, and
> every bounded edge explanation names canonical contributors with an exact
> remainder.

[Inspector.Graph library
boundary](inspector-graph-library-boundary.md) owns the immutable carrier,
groups, local identities, and structural validity.
[Inspector.Graph execution](inspector-graph-execution.md) owns closed-document
execution, source-bound derived views, structural completion, and work
receipts. This document owns only node-to-group contraction and contributor
explanation over one already populated document.

Callers retain subject and group meaning, relationship meaning, producer
completion, evidence interpretation, operation composition, and presentation.
Graph never decides that a group is a namespace, assembly, package, cluster,
or other domain concept.

The contract is **unverified** until the Release gates under
[Required evidence](#required-evidence) land.

## Structural question

> For one caller-selected grouping of this graph, which typed relationships
> cross groups, which remain inside a group, and which exact canonical edges
> and occurrences support each answer?

This is a graph-shaped question. Answering it in each producer would duplicate
contraction, relationship equality, local identity, occurrence de-duplication,
and contributor accounting. Answering it in Graph does not move domain meaning
into Graph because the caller supplies both the populated document and the
group assignment.

The first product consumer is Library Dependency Structure. Research supplies
Type-to-namespace and external-target assignments and interprets the result as
namespace dependency evidence. Graph sees only canonical node ids, group ids,
typed relationships, edges, and occurrences.

## Basis and boundaries

Group projection is a closed-document operation:

```text
immutable GraphDocument
  + source-bound node-to-group assignment
  + typed relationship selection
  -> projected group nodes
  -> cross-group projected edges
  -> within-group contributions
  -> optional bounded edge explanations
  -> source-bound structural result and work receipt
```

The source document remains canonical. Projection does not create a second
`GraphDocument` and does not copy its six caller-owned payload planes. A
generic contraction cannot combine arbitrary characteristics, limits,
failures, seeds, or occurrence evidence without interpreting caller semantics.
Instead, the result is a derived structural view that refers to canonical
groups, nodes, edges, and occurrences by local id and identifies the exact
source `GraphDocumentIdentity`.

This follows the derived-view contract in
[Inspector.Graph execution](inspector-graph-execution.md#canonical-structure-and-derived-views):
the result reshapes canonical structure, is unusable against another document,
and makes no stronger completion claim than its source and derivation.

The projected topology is suitable structural input for separately owned Graph
algorithms. This design does not define how strongly connected components,
condensation, levels, or another algorithm binds to that topology.

## Conventional basis

The design surveys analogous behavior as evidence, not authority:

| Design | Evidence adopted | Deliberate boundary |
| --- | --- | --- |
| [NetworkX quotient graph](https://networkx.org/documentation/stable/reference/algorithms/generated/networkx.algorithms.minors.quotient_graph.html) | Every source node belongs to exactly one partition block; result nodes represent blocks; a result edge exists when a source edge crosses the corresponding blocks | Graph uses caller-admitted group ids rather than arbitrary equivalence callbacks and retains exact source contributors instead of reducing evidence to an attribute dictionary |
| [igraph vertex contraction](https://r.igraph.org/reference/contract.html) | An explicit source-vertex-to-result-vertex mapping defines contraction | Graph does not leave parallel edges, loops, or payload combination to a later generic simplifier; typed relationship equality and exact contribution accounting are part of this operation |
| Boost Graph property-map model, already adopted by the execution owner | Algorithms consume structural ids and caller-associated values without taking ownership of domain meaning | The plan is immutable source-bound data rather than an executable domain callback |

The required divergence is evidence retention. Conventional quotient or
contraction APIs often combine numeric weights or caller-selected attributes.
That would be success-shaped data loss here: a product edge must still explain
which owner-issued finer edges and physical or derived occurrences support it.

## Source-bound plan

The immutable plan is bound to one source `GraphDocumentIdentity`. Execution
against another document is rejected even when its collection sizes happen to
match.

The plan declares:

- the exact source document identity;
- one assignment for every canonical node;
- the selected typed relationship values; and
- whether bounded explanations are required and, if so, the maximum
  contributor count per projected edge.

Relationship values use the source document's admitted relationship equality.
An empty relationship selection is valid. It produces projected nodes and no
edge contributions without scanning canonical edges.

The explanation bound is non-negative. A zero bound requests exact totals and
no contributor rows. Omitting the explanation request avoids contributor
ranking work; it does not weaken the complete projection.

## Complete node assignment

Every canonical node is assigned exactly once. The assigned group must be:

- one of that node's direct `GroupIds`; or
- an ancestor reached through the admitted acyclic group-parent chain of one
  of those direct groups.

The explicit assignment resolves documents in which a node belongs to several
independent group families. Graph does not infer which family or hierarchy
level the caller means.

The following plans are rejected before edge projection:

- a missing canonical node assignment;
- two assignments for one canonical node;
- a node or group id outside the source document;
- a group unrelated to the node's direct groups or their ancestors;
- a default or otherwise uninitialized assignment collection; or
- a plan bound to another document identity.

An empty document requires an empty assignment and yields an empty result.

Groups not selected by any assignment are absent from the result. A selected
group remains present when all of its member nodes are isolated or no
relationship is selected.

## Projected nodes

One projected node represents each distinct selected source group. It carries:

- a dense zero-based projected node id;
- the canonical source group id; and
- the complete ascending canonical node ids assigned to that group.

Projected nodes are ordered by canonical source group id. The result does not
copy the group's `TSubject`; the caller recovers it from the source document
whose identity the result names.

This separation keeps result-local topology and durable caller identity
distinct. A projected node id is valid only in this result. A source group id
is valid only in the named source document. Neither is a portable subject
identity.

## Edge partition

Execution examines only canonical edges whose relationship is selected. Each
selected edge maps its canonical source and target node ids through the
complete assignment.

The selected edge enters exactly one population:

- **cross-group contribution** when the assigned source and target groups
  differ; or
- **within-group contribution** when they are equal.

These populations are disjoint and their source edge ids partition the
selected canonical edge ids exactly. Group-internal evidence is never dropped
and is never emitted as a projected self-loop. Keeping it in a separately
named population prevents downstream cycle or level algorithms from treating
ordinary internal volume as a relationship between groups.

This fixed behavior deliberately differs from contraction APIs that retain
self-loops and ask callers to simplify afterward. A later caller that needs a
self-loop presentation can construct it from the exact within-group
contribution; the canonical evidence is already retained.

## Cross-group projected edges

A projected edge is unique by:

1. projected source node id;
2. projected target node id; and
3. typed relationship under the source document's relationship equality.

Each projected edge carries:

- a dense zero-based projected edge id;
- projected source and target node ids;
- the canonical typed relationship value from its first contributing source
  edge;
- complete ascending distinct canonical source edge ids; and
- complete ascending distinct canonical occurrence ids referenced by those
  source edges.

Multiple source edges with equal projected endpoints and equal relationships
become one projected edge. Parallel source relationships that are unequal
remain parallel projected edges.

Canonical occurrences may support more than one source edge. Occurrence ids
are therefore a set within each projected edge, not a globally additive edge
weight. The source-edge partition is exact globally; occurrence membership is
exact per contribution population. A result-wide distinct selected-occurrence
count is the union across those populations.

## Within-group contributions

A within-group contribution is unique by:

1. projected node id; and
2. typed relationship under the source document's relationship equality.

It carries the same complete canonical source edge and occurrence id sets as a
projected edge. These rows let callers report internal volume or establish
source-edge partition parity without putting self-loops into projected
topology.

Within-group rows are structural evidence, not Graph characteristics. Graph
does not label high or low internal volume, infer cohesion, or issue a quality
judgment.

## Deterministic identity and order

The source document already preserves caller-supplied canonical order. Group
projection uses that order rather than adding a relationship comparer that the
carrier does not own.

Observable order is:

- projected nodes by canonical source group id;
- projected edges by projected source node id, projected target node id, then
  the first contributing canonical edge id;
- within-group contributions by projected node id, then the first
  contributing canonical edge id; and
- source node, edge, and occurrence id collections in ascending canonical id
  order.

The first contributing source edge also supplies the projected relationship
value. All later contributors are equal under the document's admitted
relationship equality.

Display labels, `ToString()` output, hash-table enumeration, and payload object
identity never establish result identity or order.

## Bounded edge explanation

An explanation applies to one projected cross-group edge and ranks its
canonical source-edge contributors by:

1. descending canonical occurrence count on the source edge; then
2. ascending canonical source edge id.

Each retained contributor contains its canonical source edge id and occurrence
count. The caller uses the source document to recover finer endpoint subjects,
relationship identity, and occurrence evidence.

The explanation also carries:

- total source-edge contributor count;
- retained contributor count;
- omitted contributor count;
- total distinct occurrence count on the projected edge;
- distinct occurrence count covered by at least one retained contributor; and
- distinct occurrence count covered only by omitted contributors.

The last two occurrence counts partition the projected edge's distinct
occurrence set. An occurrence shared by retained and omitted source edges is
explained, not omitted.

The bound changes only the retained contributor list. It never changes the
projected edge, complete source id sets, totals, completion, or qualification.

Explanations are requested work. An execution that does not request them does
not rank contributors or build terminal-only explanation rows.

## Completion and qualification

Projection over an admitted immutable source document is structurally
exhausted when every canonical node assignment has been validated and every
selected canonical edge has entered exactly one contribution population.

Structural exhaustion proves only:

- the assignment covered the source document's canonical nodes;
- selected canonical edges were completely partitioned; and
- projected contributor sets and requested explanations are exact for that
  source document.

It does not prove that the source document exhausts a domain relationship.
The result does not copy, combine, suppress, or reinterpret source limits and
failures. Product composition retains the source document and applies its
producer-owned completion, limits, and failures when interpreting the
projection.

An empty projected edge set is therefore a qualified structural fact: no
selected cross-group edge exists in this source document under this
assignment. A host may state domain absence only when the adjacent product and
producer owners establish that stronger claim.

## Work receipt

The operation issues a source-bound work receipt with exact counts sufficient
to verify the projection:

- canonical nodes and assignments examined;
- selected source groups admitted;
- canonical edges examined;
- selected canonical edges admitted;
- cross-group and within-group source edges admitted;
- distinct selected canonical occurrences;
- projected edges and within-group rows issued;
- contributor rankings requested and issued; and
- terminal settlement.

The receipt names the exact source `GraphDocumentIdentity`. It does not become
a portable subject or producer receipt.

The edge partition invariant is:

```text
selected source edges
  = cross-group source edges + within-group source edges
```

The implementation gate checks both the counts and the disjoint union of
canonical source edge ids.

## Cost and work reduction

Complete contraction requires:

- one assignment validation for every canonical node;
- one pass over selected canonical edges;
- one contribution insertion per selected source edge; and
- occurrence-set union only for the contribution population that receives the
  edge.

The retained source-edge references are linear in selected source edges.
Retained occurrence references are linear in the distinct occurrence ids of
each projected or within-group contribution; sharing one canonical occurrence
across unrelated source edges may place that id in more than one contribution,
matching the source topology.

The operation does not:

- inspect or copy caller payloads;
- build adjacency, SCC, condensation, levels, or communities;
- construct presentation rows;
- rank contributors when explanations are not requested; or
- materialize a second canonical `GraphDocument`.

An implementation may specialize a measured closed shape, but the reference
operation remains authoritative and the specialization must return equivalent
ids, order, contributor sets, explanations, completion, and receipts.

## Production adoption

This design is the Graph-owned first slice in an owner-sized adoption path:

1. **Graph group projection:** this contract and its reference operation.
2. **Graph components:** a separate owner defines SCC, condensation, and
   deterministic levels over admitted structural topology.
3. **Library Dependency Structure:** Research supplies its owner-issued Type
   graph and grouping assignments, then binds the Graph results into its
   domain document.
4. **CLI:** a focused exact Library experience lowers the Research result
   through QuerySpace and Markout.
5. **Browser/Wasm:** a focused view consumes the same managed Research result.

The first implementation uses the independently compiled
`Inspector.Graph.Consumer` harness as its production host, exercising the
ordinary public operation with application-owned payloads and no
dotnet-inspect dependency. This is the repository's admitted test-harness
production-host pattern; it proves the substrate contract without combining
Graph and Research ownership in one PR.

Library Dependency Structure is the first product caller. Its focused adoption
must follow before the learn-the-codebase scenario claims group projection as a
shipped product experience. CLI and Browser/Wasm then consume that one
host-neutral Research result in their own focused slices.

FluentValidation 12.1.1 is the representative product asset. Its exact Type
dependency edges roll up to namespace and external groups, and its
highest-volume namespace edges exercise bounded finer-edge explanations.

The pathological scale case is a synthetic high-fan-in graph because no one
real package deterministically guarantees the required concentration shape.
It supplements rather than replaces the FluentValidation gate.

## Required evidence

The implementation names these Release gates or equivalent focused gates:

1. `GroupProjection_RequiresOneAdmittedAssignmentPerCanonicalNode` rejects
   missing, duplicate, unrelated, cross-document, and invalid-id assignments.
2. `GroupProjection_PartitionsEverySelectedSourceEdgeExactlyOnce` verifies the
   disjoint cross-group and within-group edge-id union.
3. `GroupProjection_UsesDocumentRelationshipEquality` proves equal
   relationships aggregate and unequal parallel relationships remain
   distinct.
4. `GroupProjection_RetainsDistinctCanonicalOccurrences` covers duplicate
   occurrence references within a projected edge and one occurrence shared by
   several source edges.
5. `GroupProjection_RetainsIsolatedAssignedGroups` covers empty relationship
   selection and isolated source nodes without scanning canonical edges.
6. `GroupProjection_OrdersOnlyByCanonicalStructuralIdentity` varies hash and
   display behavior while retaining byte-identical structural rows.
7. `GroupProjection_ExplainsBoundedContributorsWithExactRemainders` covers
   zero, smaller-than-population, equal, and larger explanation bounds plus
   occurrence overlap between retained and omitted contributors.
8. `GroupProjection_HighFanInRemainsIterativeAndBounded` exercises a
   pathological contributor population without recursion or unbounded
   explanation rows.
9. The direct-consumer fixture executes projection and explanation using
   ordinary caller-owned payloads and no dotnet-inspect dependency.
10. The Library Dependency Structure adoption over FluentValidation 12.1.1
    matches an independent Research-owned oracle for group edges, internal
    counts, contributor identities, and exact remainders.
11. NativeAOT evidence reports the Graph kernel and every adopted production
    terminal under the repository evidence contract.

The existing dependency-policy gate continues to enforce the BCL-only
`Inspector.Graph` project and assembly boundary. This design adds no new
dependency absence claim.

## Non-goals

- Discovering or choosing groups for callers.
- Defining namespace, assembly, package, Type, cluster, or ecosystem meaning.
- Copying or combining caller payloads into a second `GraphDocument`.
- Interpreting occurrence evidence or defining a universal edge weight.
- Strengthening producer completion or issuing domain absence claims.
- SCC, condensation, topological levels, traversal, reachability, or
  communities.
- QuerySpace predicates, row selection, Count, or source delegation.
- CLI grammar, Markout, JSON, or Browser rendering.
- Migrating every existing Graph consumer.
