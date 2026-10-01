# Inspector.Graph components and levels

## Status, owner, and claim

Status: **design contract** for
[#9089](https://github.com/richlander/dotnet-inspect/issues/9089), a focused
successor under
[#8669](https://github.com/richlander/dotnet-inspect/issues/8669) and the
learn-the-codebase scenario in
[#8744](https://github.com/richlander/dotnet-inspect/issues/8744).

The **Inspector.Graph Components and Levels** owner defines this claim:

> Given one exact group-projection result and a source-document-bound
> selection of canonical source groups, Graph issues every strongly connected
> component of the induced selected-group topology, its acyclic condensation,
> and deterministic dependency levels. Every selected group belongs to
> exactly one component, and every condensation edge retains the exact
> canonical source edges that cross its component boundary.

[Inspector.Graph group
projection](inspector-graph-group-projection.md) owns group contraction,
projected nodes and edges, within-group contributions, canonical contributor
sets, and projection completion.
[Inspector.Graph execution](inspector-graph-execution.md) owns
closed-document execution, source-bound derived views, structural completion,
and work receipts. This document owns only directed component discovery,
condensation, and levelization over one already issued group projection.

Callers retain group and relationship meaning, the selected structural scope,
producer and domain completion, product cycle vocabulary, interpretation, and
presentation. Graph never decides that a group is internal, external,
namespace-shaped, defective, intended, layered, or tangled.

The contract is **unverified** until the Release gates under
[Required evidence](#required-evidence) land.

## Structural question

> Among these caller-selected projected groups, which groups are mutually
> reachable, which component boundaries remain after contraction, and how far
> is each component from a dependency sink?

This is a graph-shaped question. Answering it in Research would duplicate
strong-component discovery, condensation, contributor union, deterministic
identity, and level settlement. Answering it in Graph does not move product
meaning into Graph because the caller supplies the exact projection and the
canonical source groups admitted to the induced topology.

The first product consumer is Library Dependency Structure. Research selects
every internal namespace group and excludes external groups. It interprets
components of two or more namespace groups as namespace cycles and binds Graph
levels into its domain document. Graph sees only source group ids, projected
edge endpoints, and canonical source edge ids.

## Basis and boundaries

Component analysis is a closed derived-view operation:

```text
source-bound GraphGroupProjectionResult
  + source-document-bound selected source group ids
  -> induced selected-group topology
  -> complete strong-component membership
  -> acyclic component condensation
  -> sink-based component levels
  -> source-bound structural result and work receipt
```

The group projection remains the source structural view. Component analysis
does not re-run projection, copy caller payloads, inspect relationship values,
or create another `GraphDocument`.

The plan uses canonical source group ids rather than projection-local node ids.
This keeps its join currency valid for the source document named by the
projection receipt and lets one plan apply to a different relationship
projection over the same document. The result is always relative to the
projection supplied for that execution.

The component result carries canonical source group and source edge ids.
Result-local component and condensation-edge ids do not become portable
subject identity.

## Conventional basis

The design surveys analogous behavior as evidence, not authority:

| Design | Evidence adopted | Deliberate boundary |
| --- | --- | --- |
| [Boost Graph `strong_components`](https://www.boost.org/doc/libs/1_89_0/libs/graph/doc/strong_components.html) | Strong components are maximal mutually reachable vertex sets; every vertex maps to one integer component; expected work is `O(V + E)` | Boost's traversal-issued component labels are not observable identity here; Graph reissues deterministic ids from canonical source group identity and requires explicit-stack execution |
| [NetworkX condensation](https://networkx.org/documentation/stable/reference/algorithms/generated/networkx.algorithms.components.condensation.html) | One condensation node represents one SCC, membership and original-node mapping remain available, and the condensation is a DAG | Graph retains typed source-document ids and exact canonical edge contributors instead of dynamic attribute dictionaries |
| [NetworkX topological generations](https://networkx.org/documentation/stable/reference/algorithms/generated/networkx.algorithms.dag.topological_generations.html) | DAG generations are determined by longest-path dependency | Graph applies generation semantics to reversed condensation direction so dependency sinks are level 0, matching the Lakos rule already adopted by Library Dependency Structure |

The required divergence from common SCC APIs is deterministic component
identity. Traversal order, adjacency insertion order, hash-table enumeration,
or implementation-specific SCC discovery order cannot become a durable
component id.

The required divergence from generic condensation APIs is evidence retention.
A condensation edge is not only a pair of component ids; it retains the exact
canonical source edges that establish that boundary.

## Source-bound plan

The immutable plan declares:

- the exact source `GraphDocumentIdentity`; and
- the distinct canonical source group ids admitted to component analysis.

The group-id collection is snapshotted at plan construction. Default or
otherwise uninitialized collections, duplicate ids, and negative ids are
rejected.

Execution rejects:

- a plan bound to a different source document than the projection receipt;
- a selected source group absent from the supplied projection.

The adjacent group-projection owner already guarantees dense projected ids,
unique source groups, valid projected endpoints, complete source edge sets,
and deterministic row order. Component analysis consumes those guarantees; it
does not treat a trusted Graph result as hostile or repeat its validation.

An empty selected-group collection is valid and yields an empty result without
examining projected edges.

## Induced selected-group topology

Each selected canonical source group maps to its one projected node in the
supplied result. The admitted topology is induced:

- a projected edge participates when both endpoint source groups are selected;
- an edge with either endpoint outside the selection does not participate; and
- a within-group contribution never participates because group projection
  deliberately keeps it outside projected topology.

This rule lets Library Dependency Structure select internal namespace groups
while excluding external groups and every edge that crosses that boundary.
Graph does not discover or validate the product meaning of "internal."

All participating projected relationship kinds contribute to reachability.
Several unequal projected relationships may share endpoints. They represent
one structural arc for traversal but remain separate evidence contributors
when a condensation boundary is issued.

The selected group and edge populations are exact only relative to the
supplied projection and plan. Omitting a source group intentionally changes
the induced topology and can change components and levels.

## Strongly connected components

A strongly connected component is a maximal selected source-group set in
which every member reaches every other member through admitted projected
edges.

Every selected source group belongs to exactly one component. This includes:

- isolated groups;
- acyclic groups that form singleton components; and
- groups in reciprocal or longer directed cycles.

The projection contains no self-loop edges. Group-internal source edges remain
available in its within-group contribution population but cannot make a
singleton component a multi-group cycle.

Graph issues all singleton and multi-member components. It does not issue a
product `Cycle` classification. Library Dependency Structure applies its own
rule that a namespace cycle contains two or more namespace groups.

Each component carries:

- a dense zero-based component id;
- its complete ascending canonical source group ids; and
- its level after condensation settlement.

Component ids are ordered by the component's smallest source group id. Since
components are disjoint and non-empty, that minimum is unique.

## Membership mapping

The result also carries one explicit membership row per selected source group:

- canonical source group id; and
- result-local component id.

Membership rows are ordered by source group id. This mapping lets consumers
join source groups to component levels without rebuilding or interpreting the
SCC partition.

The member lists and membership rows must agree exactly:

```text
selected source group ids
  = disjoint union of component member ids
  = membership source group ids
```

This is deliberate structural redundancy. It preserves both discovery by
component and known-group lookup without asking each consumer to reconstruct
one from the other.

## Condensation

Each component becomes one condensation node whose id is the component id.
For every participating projected edge whose endpoints belong to different
components, one directed condensation edge connects those components.

A condensation edge is unique by:

1. source component id; and
2. target component id.

Several projected endpoint pairs and relationship values can contribute to
one condensation edge. The edge carries:

- a dense zero-based condensation edge id;
- source and target component ids; and
- the complete ascending distinct canonical source edge ids from every
  contributing projected edge.

Projection-local edge ids do not escape as durable contributor identity. The
canonical source edge ids remain valid in the exact source document named by
the result receipt.

An edge whose endpoints belong to one component does not become a
condensation self-loop. Its evidence remains visible in the supplied group
projection.

The condensation is acyclic by the SCC maximality contract. The implementation
gate checks this as an algorithmic correctness property; it is not a product
absence claim.

## Deterministic dependency levels

Levels follow the Lakos rule already adopted by Library Dependency Structure:

```text
level(component with no outgoing condensation edge) = 0

level(other component)
  = 1 + max(level(each directly depended-on target component))
```

Equivalently, a level is the longest directed path length from one condensation
component to any dependency sink.

Consequences:

- every member of one SCC shares one level;
- disconnected sinks are level 0;
- a component whose only outgoing projected edges cross outside the selected
  group scope is a sink in this induced topology;
- external edges do not affect Library Dependency Structure levels when the
  caller excludes external groups; and
- component ids do not imply level or topological order.

Levels are settled over the complete condensation before the result is issued.
No display filter, row limit, host order, or presentation selection can change
them.

## Deterministic identity and order

Observable order is:

- components by minimum canonical source group id;
- each component's members by canonical source group id;
- membership rows by canonical source group id;
- condensation edges by source component id then target component id; and
- canonical source edge contributor ids in ascending order.

Component and condensation-edge ids are assigned only after this structural
order is established.

Relationship payloads, display labels, `ToString()` output, hash codes,
projection input order, adjacency insertion order, DFS order, and SCC
discovery order never establish identity or output order.

## Completion and qualification

Component analysis is structurally exhausted when:

- every selected source group has one component membership;
- every admitted projected structural arc has participated in SCC discovery;
- every cross-component projected edge has contributed to one condensation
  edge;
- the condensation is settled; and
- every component has one level.

Structural exhaustion proves exactness only over the supplied projection and
selected source groups. It does not prove:

- that the source document exhausts a domain relationship;
- that the group projection selected every product-relevant relationship;
- that the selected group set includes every product-relevant group;
- that no domain cycle exists outside admitted evidence; or
- that a level expresses intended architecture.

The result does not copy, combine, suppress, or reinterpret source limits and
failures. Product composition retains the source document, projection, and
producer receipts when interpreting components or qualified absence.

An empty multi-member component population is therefore only a structural
fact about admitted evidence. A host states unqualified acyclicity only when
its product and producer owners establish the stronger completion claim.

## Work receipt

The operation issues a source-bound work receipt with exact counts sufficient
to verify component analysis:

- projected nodes examined;
- selected source groups admitted;
- projected edges examined;
- induced projected edges admitted;
- distinct directed structural arcs indexed;
- strongly connected components and membership rows issued;
- intra-component and cross-component projected edges admitted;
- condensation edges issued;
- distinct canonical source edge contributors retained on condensation edges;
- components whose levels were settled; and
- terminal settlement.

The receipt names the exact source `GraphDocumentIdentity`. It remains a
structural execution receipt, not a portable subject, producer, or product
completion receipt.

The induced-edge partition invariant is:

```text
induced projected edges
  = intra-component projected edges
  + cross-component projected edges
```

The implementation gate checks both counts and projected-edge membership.

## Stack safety, cost, and work reduction

The reference operation is iterative. It must not use process-stack recursion
for component discovery, condensation traversal, or level settlement.

For selected groups `V`, distinct induced structural arcs `A`, induced
projected edges `E`, and retained canonical condensation contributors `C`,
component discovery and levelization are linear:

```text
time: O(projected nodes + projected edges + V + A + E + C)
space: O(V + A + E + C)
```

The projected-node and projected-edge terms account for mapping canonical
source groups and inducing the selected topology. An empty selection avoids
the projected-edge scan.

The operation may maintain forward and reverse structural adjacency when that
keeps the iterative reference implementation simpler and more auditable.
Avoiding one linear index does not justify a substantially more complex
reference algorithm without measured evidence.

The operation does not:

- inspect source node, group, relationship, occurrence, or evidence payloads;
- inspect group-projection explanations or within-group contributions;
- copy source occurrence ids;
- recompute group projection;
- build transitive closure or enumerate paths;
- rank product rows; or
- construct presentation models.

An implementation may specialize a measured closed shape, but the reference
operation remains authoritative and the specialization must return equivalent
components, memberships, condensation edges, canonical contributors, levels,
completion, and receipts.

## Production adoption

This design is one Graph-owned slice in the learn-the-codebase path:

1. **Graph components:** this contract and its reference operation.
2. **Library Dependency Structure:** Research supplies its group projection
   and selects every internal namespace source group, then binds component
   membership and levels into its domain document.
3. **CLI:** a focused exact Library experience lowers the Research result
   through QuerySpace and Markout.
4. **Browser/Wasm:** a focused view consumes the same managed Research result.

The first implementation uses the independently compiled
`Inspector.Graph.Consumer` harness as its production host. Its application and
storage groups form an acyclic dependency with levels 1 and 0. The harness
exercises the ordinary public operation with application-owned payloads and no
dotnet-inspect dependency.

Library Dependency Structure is the first product caller. Its separately owned
Research adoption interprets multi-member internal namespace components as
cycles and combines component results with producer completion. Graph does not
define that product document in this slice.

FluentValidation 12.1.1 is the representative product asset for the Research
adoption. A synthetic deep chain and giant cycle are required because no real
package deterministically guarantees the depth needed to prove stack safety.

CLI and Browser/Wasm consume the same host-neutral Research document; neither
host recomputes components or levels.

## Required evidence

The implementation names these Release gates or equivalent focused gates:

1. `ComponentAnalysis_RequiresSourceBoundAdmittedGroups` rejects
   cross-document, duplicate, negative, absent, and uninitialized group
   selections.
2. `ComponentAnalysis_PartitionsEverySelectedGroupExactlyOnce` covers empty,
   isolated, disconnected, and mixed component populations and checks both
   member lists and membership rows.
3. `ComponentAnalysis_DerivesMaximalStrongComponents` compares reciprocal,
   longer-cycle, acyclic-tail, and multi-component results with an independent
   mutual-reachability oracle.
4. `ComponentAnalysis_CondensesToAcyclicContributorCompleteGraph` verifies
   unique component pairs, no self-loops, DAG structure, exact projected-edge
   partition, and complete canonical source-edge contributor unions across
   parallel relationship values.
5. `ComponentAnalysis_AssignsSinkBasedMaxDependencyLevels` checks every
   component against the sink/max-successor recurrence, including unequal path
   lengths and disconnected sinks.
6. `ComponentAnalysis_ExcludesCrossBoundaryAndWithinGroupEvidence` proves
   omitted source groups, external edges, and projection within-group
   contributions cannot change admitted components or levels.
7. `ComponentAnalysis_OrdersOnlyByCanonicalStructuralIdentity` varies plan
   order, relationship hash behavior, and discovery order while retaining
   byte-identical structural rows.
8. `ComponentAnalysis_DeepChainAndGiantCycleRemainIterative` exercises a
   stack-overflow-depth chain and cycle in the ordinary Release suite and
   measures the gate to classify its PR cost.
9. The direct-consumer fixture executes group projection through component
   levelization using ordinary caller-owned payloads and no dotnet-inspect
   dependency.
10. The Library Dependency Structure adoption over FluentValidation 12.1.1
    matches an independent Research-owned oracle for internal namespace SCCs,
    condensation, and levels.
11. NativeAOT evidence reports the Graph kernel and every adopted production
    terminal under the repository evidence contract.

The existing dependency-policy gate continues to enforce the BCL-only
`Inspector.Graph` project and assembly boundary. This design adds no new
dependency absence claim.

## Non-goals

- Defining a generic topology carrier before a second production source needs
  one.
- Running components directly over a canonical `GraphDocument`.
- Discovering or interpreting internal, external, namespace, assembly, Type,
  package, cluster, or ecosystem groups.
- Re-running group projection or combining its caller-owned payload planes.
- Defining a product `Cycle`, tangle, severity, quality, intended-layer, or
  architecture verdict.
- Strengthening projection, producer, domain, or product completion.
- Weakly connected components, communities, reachability, transitive closure,
  shortest paths, dominators, centrality, or path enumeration.
- QuerySpace predicates, row selection, Count, or source delegation.
- Research document construction.
- CLI grammar, Markout, Mermaid, JSON, or Browser rendering.
