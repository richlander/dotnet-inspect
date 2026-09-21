# Evidence-backed implementation spine

## Status and authority

This is the focused Research-composition design for the implementation-spine
program under
[#7696](https://github.com/richlander/dotnet-inspect/issues/7696).
The user approved beginning the larger program with a specification-only PR on
2026-09-21.

**Implementation Spine Composition** is the single normative owner established
here. Its exact claim is:

> Given one sealed, exact compiled population; owner-issued call,
> correspondence, ownership, and structural observations; and explicit work
> and result bounds, select and explain a deterministic bounded set of
> high-support call-topology components and evidence-preserving connector
> paths. The completed result retains the exact graph occurrences, population
> and methodology receipts, selection observations, and every qualification
> needed to distinguish observed compiled structure from a quality judgment or
> runtime claim.

Research owns the selection methodology, interpretation boundary, and detached
selection result. Queries owns population sealing, Workspace and package
composition, operation execution, and projection through the existing
Inspection Graph. Analysis and Metadata retain their evidence and relationship
semantics. Hosts retain gestures and presentation.

This document does not redefine:

- Package Dependency Traversal or target-aware PackageHouse realization;
- Workspace admission, assembly groups, binding, or operation authority;
- call topology, physical call occurrences, caller or callee populations;
- interface implementation, subclass, override, or `MethodImpl` evidence;
- argument- or receiver-value analysis;
- ingress classification or completion;
- cyclomatic complexity or another implementation-profile measure;
- the Inspection Graph document, relationship catalog, or rendering model;
- Library Metrics report distributions;
- CLI or Browser report composition; or
- the reporting skill, which remains an orchestrator and analyst over
  tool-issued data.

Each missing prerequisite remains an independently owned adoption. This
document records their typed roles and current migration status without
specifying their internal algorithms.

## Product question

The operation answers:

> For this exact Library, Package, or restored Project population, which
> observed compiled call paths form the most load-bearing bounded
> implementation structure, why were they selected, what exact evidence
> supports every retained edge, and where is the answer incomplete?

The result is an **observed implementation spine**. "Spine" names a compact
visual and machine-readable projection, not a universal architectural metric.
One result may contain several disconnected spine components.

The first methodology does not claim:

- that selected code is the most important authored source;
- that omitted code is unimportant or unreachable at runtime;
- that high cyclomatic complexity is poor quality;
- that a static call is executed in production;
- that every virtual, delegate, reflection, or native transition was resolved;
  or
- that one scalar score measures implementation quality.

## Production experience

The target operation-first gestures are:

```console
dotnet-inspect graph spine --library ./Product.dll

dotnet-inspect graph spine \
  --library ./Product.dll \
  --library ./Product.Common.dll

dotnet-inspect graph spine \
  --package Microsoft.Azure.SignalR@1.33.1 \
  --tfm net8.0

dotnet-inspect graph spine --project ./src/Product.csproj
```

One `--library` selects an internal Library population. Repeated `--library`
inputs select one explicit multi-Library population, preserving exact
cross-Library calls and boundaries in the same spine operation.

Subject-first Library, Package, and Project reports may expose an authored
`Implementation Spine` Graph section over the already resolved subject. Both
entrances consume the same host-neutral operation under
[Operation Commands and Subject Sections](operation-command-and-subject-section-composition.md).

The CLI lowers the existing Inspection Graph through Markout to Markdown,
edge tables, structured JSON, tree views where the selected topology admits
one, and Mermaid. Inspect Web consumes the same typed operation through its
graph viewer and may use host-specific interaction without reconstructing
selection or evidence in TypeScript.

The planned reporting skill:

1. requests dense typed report artifacts from dotnet-inspect;
2. validates their exact subject and generation receipts;
3. may index or combine them mechanically, including with `jq --slurp`;
4. interprets the owner-issued observations; and
5. embeds the tool-issued spine visualization in the final report.

The skill does not rank methods, derive graph edges, compute paths, reconstruct
ownership, or turn presentation text into analysis data.

## Conventional basis and deliberate composition

NDepend combines .NET metrics, dependency queries, and interactive graphs.
CodeQL path queries preserve source-to-sink path evidence. Visual Studio
publishes conventional code metrics. These are useful analogous designs, but
their individual facilities do not define this contract.

The deliberate composition here is:

- exact compiled method and physical call-site evidence;
- package- and Library-generation ownership;
- deterministic bounded cross-Library traversal;
- explicit selection observations rather than an opaque score;
- typed incompleteness for dispatch, acquisition, and work limits;
- one host-neutral graph result for CLI and Browser rendering; and
- a product skill that analyzes tool-issued artifacts without becoming a
  second semantic implementation.

No external code or architecture is transferred.

## Imported owner contracts

| Evidence or behavior | Normative owner | Spine role |
| --- | --- | --- |
| Package roots, dependency edges, target policy, and traversal completion | [Package Dependency Traversal](package-dependency-traversal.md) | Supplies the bounded package population and exact root-relative paths eligible for realization |
| Target-aware destination realization and Platform pruning | [Package Dependency Edge Realization](package-dependency-edge-realization.md) | Supplies exact Package or Platform destinations without re-resolving dependency identity |
| Atomic Package destination admission and route outcomes | [Package Dependency Workspace Routes](package-dependency-workspace-routes.md) | Supplies exact Workspace Package occurrences, Platform destinations, unavailable edges, root-relative occurrence association, and terminal Scope evidence |
| Restored Project target selection, dependency facts, and root-relative traversal | [Restored Project Dependency Facts](restored-project-dependency-facts.md) and [Restored Project Dependency Traversal](restored-project-dependency-traversal.md) | Supplies the exact restored target identity, Project and Package relationships, traversal completion, and root-relative depth |
| Workspace participants, binding contexts, and graph focal length | [Workspace registration and call-graph focal length](workspace-registration-and-call-graph-scope.md) | Supplies one admitted operation population and finite acquisition/traversal authority |
| Stateless body execution and focused Analysis results | [Library Body Analysis Service](library-body-analysis-service.md) | Supplies receipt-associated call, leverage, profile, safety, allocation, and future value-flow evidence |
| Exact call topology and physical occurrences | [Call Graph Projection](call-graph-projection.md) and [Call Graph Characteristics](call-graph-characteristics.md) | Supplies member nodes, directed call edges, call-site receipts, modality, loop state, and completeness |
| Graph subjects, relationships, occurrences, characteristics, limits, and failures | [Inspection Graph Document](inspection-graph-document.md) | Carries the selected spine without adding a second graph envelope |
| Single-seed, peer-seed, induced-set, and bounded neighborhood semantics | [Inspection Graph Modes](inspection-graph-modes.md) | Supplies the request population and seed roles; spine is an authored Graph profile, not a new mode family |
| Implementation profiles and coverage | [Library Metrics report](library-structural-report.md) and Analysis | Supplies descriptive annotations; complexity does not select the first spine methodology |
| Research identity, admission, correspondence, and evidence composition | [Inspection Layers](inspection-layers.md) and focused Research owners | Supplies the owner-issued identities and detached composition boundary |

An input that lacks an owner-issued identity or completion state is not
admissible merely because its display text matches a graph subject.

## Scope and population

One request names exactly one sealed operation population:

- one explicit non-empty Library population whose entries each name one exact
  Library generation;
- one exact Package realization plus a target framework and bounded realized
  dependency population; or
- one restored Project target plus its owner-issued resolved dependency
  population.

Package and Project scope may contain several binding-consistent assembly
groups. A call edge exists only within one group. The result may contain
several qualified spine components, but it does not manufacture a call across
groups. A separately typed correspondence or dependency relationship may
explain why groups coexist; it does not become call evidence.

The population receipt preserves:

- the subject and source coordinates;
- target framework and runtime identifier when applicable;
- Workspace realization, definition snapshot, operation authority, Scope
  publication base, and terminal route-composition evidence;
- every admitted package and Library occurrence;
- assembly-group and catalog generations;
- traversal, acquisition, body-analysis, node, edge, occurrence, path, and
  result bounds; and
- failures and boundaries from every contributing owner.

For Project scope, restored dependency traversal does not by itself identify
the produced output Libraries or admit them to a Workspace. A separate focused
Project-output realization owner must associate the exact restored target with
its produced and resolved artifact occurrences before a Project spine request
is executable. This document imports that future result; it does not define
how MSBuild outputs are found or acquired.

The result does not infer package ownership from assembly names or paths.
Platform delegation remains Platform evidence and does not become package
payload evidence.

## Evidence planes

### Compiled call topology

Methodology version 1 selects over the exact static call plane already owned by
CallGraph:

- admitted physical `call`, `callvirt`, and `newobj` occurrences;
- their exact static operand definitions when correspondence succeeds; and
- compiler-generated physical bodies attributed to their Analysis-issued
  logical source owner while retaining the physical MethodDef and IL offset.

This is an exact claim about compiled operands and retained call sites, not an
exact runtime-dispatch claim. A virtual or interface `callvirt` keeps the
owner-issued dispatch disposition and reaches only its static slot in version
1. A bodiless slot or unresolved target is a visible boundary and cannot
silently connect to an implementation body.

Every retained call edge preserves its physical occurrences. Logical edge
aggregation, call kinds, loop state, dispatch disposition, exact-target state,
and evidence incompleteness remain owned by CallGraph and the Inspection Graph
adapter.

Signature-only references, dependency declarations, possible override targets,
and equal display names are not call edges.

### Deferred dispatch and ingress evidence

Methodology version 1 defines no receiver-population algebra, dispatch-candidate
relationship, runtime-target strengthening, or ingress taxonomy. It imports no
such evidence and makes no claim about possible implementations, callbacks, or
external entrypoints.

Those capabilities require separately approved focused owners before a later
spine methodology may import their issued types and completion states. Version
1 uses algorithmic compiled-graph source components for connector witnesses and
labels them as graph sources, never as external entrypoints.

### Structural annotations

Implementation profiles, body signals, Findings, cluster membership, and
Library Metrics report distributions may annotate selected graph targets
through the Inspection Graph characteristic plane.

The first selection methodology does not use cyclomatic complexity,
allocation count, exception regions, unsafe evidence, reflection calls, or a
structural-cohort label to choose the spine. Those values explain the selected
topology without turning implementation effort into importance or quality.

Topology clusters and structural-vector cohorts remain distinct:

- a topology cluster describes connected call evidence; and
- a structural cohort describes similar implementation measures or changes.

Neither identity substitutes for the other.

## Selection methodology

Every completed result carries a methodology identity. Methodology version 1
operates only on the compiled-call plane.

### Component graph

Research condenses exact-call strongly connected components into one directed
acyclic component graph. Condensation is an algorithmic step for recursive
topology; it is not the user-visible topology-cluster contract.

Each component preserves:

- every exact member identity;
- every internal physical call occurrence;
- incoming and outgoing component edges with their physical occurrences;
- Library and package ownership; and
- every contributing completeness qualification.

Only a component containing at least one admitted body-bearing method is
eligible for selection. Bodiless, unresolved, and external targets remain
visible boundaries and may terminate a retained connector.

An algorithmic graph source is a condensed component with no admitted exact
incoming component edge. It is a property of this sealed static graph, not a
claim that the component is public, externally callable, or a runtime
entrypoint.

### Selection observations

Research computes a vector, not a weighted scalar score:

| Observation | Meaning |
| --- | --- |
| `UpstreamReach` | Number of distinct exact member identities that can reach the component |
| `DownstreamReach` | Number of distinct exact member identities reachable from the component |
| `DirectCallerCount` | Number of distinct exact caller identities incident on the component |
| `CrossBoundaryReach` | Number of distinct Library or package boundaries reached through exact downstream call evidence |

These are positive lower-bound observations over the sealed compiled-call
plane. Added exact evidence can increase them and can change ordering, but it
does not invalidate an already retained positive path or call occurrence.
Counts retain their population, graph-generation, and completion state. A
missing participant, unresolved static target, or work boundary prevents a
complete absence or exclusivity claim but does not erase positive exact
evidence.

The request supplies a positive maximum component count admitted to selection
and a positive selected-component limit. SCC condensation and all four
selection observations must complete over the admitted component graph before
ordering.
Exceeding the component admission limit returns the call-census evidence with
a typed `SelectionUnavailable` outcome; it does not rank partial counts.

Components order deterministically by:

1. descending `UpstreamReach`;
2. descending `CrossBoundaryReach`;
3. descending `DownstreamReach`;
4. descending `DirectCallerCount`; and
5. canonical component key.

The graph-wide call-census prerequisite issues collision-free total ordering
keys for exact members and physical call occurrences. Library and Package
ownership inputs likewise issue collision-free total ordering keys for their
boundary identities. Research consumes those keys; it does not derive them
from labels, enumeration order, dense ids, or object hash codes.

The canonical component key is the lexicographically ordered sequence of its
exact member ordering keys. Sequence comparison is ordinal and
length-sensitive. It is bound to the call-census receipt.

The canonical method-edge key is the caller member key, callee member key, and
ordered physical call-occurrence ordering keys.

The canonical component-edge key is the ordered pair of endpoint component
keys followed by the ordered physical call-occurrence ordering keys
on that edge. These keys define all component and edge ordering below.

A boundary task key is `(kind, owner-key)`, where `Library` sorts before
`Package` and each owner-key uses its issuing owner's total order. This type tag
defines cross-kind ordering without merging the identities.

The canonical boundary-edge key is the boundary task key followed by the
canonical method-edge key of the physical call that crosses it. It totally
orders multiple crossings of the same boundary.

This ordering is a projection policy, not a universal importance score.
Every selected component retains the complete observation vector and the
methodology identity that selected it.

Strict dominance, cut vertices, and other negative or exclusivity-sensitive
observations are not part of methodology version 1. Any later admission needs
a separately approved methodology contract.

### Connector witnesses

The result retains a bounded union of compiled-call paths:

1. one deterministic shortest exact method path from any member of an
   algorithmic graph-source component to any member of each selected component;
2. direct exact method-call edges between selected components; and
3. one deterministic shortest exact method path from any member of each
   selected component to each Library or package boundary identity admitted by
   the request.

Connector search runs on the original exact method graph, not the condensed
component graph. Equal-length alternatives choose the lexicographically
smallest sequence of canonical method-edge keys. Shared nodes, edges, and
physical occurrences appear once in the projected graph.

The request declares these independent positive limits:

- maximum component count admitted to selection;
- maximum selected components;
- maximum connector depth per witness search;
- maximum searched method nodes across the connector operation;
- maximum searched method edges across the connector operation;
- maximum retained connector witnesses;
- maximum projected graph nodes; and
- maximum projected graph edges.

Connector depth is the number of canonical method edges in the completed
witness. For a boundary witness, that count includes the final physical call
edge that crosses the named Library or Package boundary.

Before connector work, selected components project atomically in selection
order. Members order by exact member identity and internal edges by physical
method-edge key. Projected-node capacity is checked first, then projected-edge
capacity. If either check fails, none of that component is projected,
`ProjectionLimited` names it and the winning limit, and all remaining
projection stops. The connector queue does not begin. The selection explanation
remains available.

Connector work has one total task queue:

1. `SourceWitness` tasks in selected-component order;
2. `DirectSelectedEdge` tasks in canonical method-edge order; and
3. `BoundaryWitness` tasks in the Cartesian order of selected component then
   owner-issued boundary identity.

Boundary tasks are created for every boundary identity admitted by the request,
not only boundaries already known to be reachable. `Unreachable` is therefore
a meaningful completed outcome.

Witness tasks use the ordered state transition:

```text
Unstarted -> DistanceSearch -> Reconstruct -> Retention -> Complete
```

A direct-selected-edge task skips to `Retention`.

`DistanceSearch` always traverses incoming method edges. A source task starts
with every member of its selected component at depth zero and seeks every member
of an algorithmic graph-source component at the first reachable source depth.
A boundary task starts with every terminal caller method and physical edge for
the named boundary at depth one, because the retained boundary crossing is
already one method edge, and seeks any member of its selected component.
Initial entries order by member key and boundary-edge key. When the depth limit
is zero, no boundary entry is admitted; the task records a depth frontier and
completes as `DepthLimited`.

Each search retains:

- FIFO queue entries `(member-key, depth)`;
- the exact distance assigned to every enqueued member;
- every charged edge that is an admissible forward successor toward the task
  destination;
- the current queue entry and next incoming-edge cursor;
- graph-source member goals found at the first goal depth;
- selected-member goals found at the first boundary-goal depth;
- terminal-member-to-boundary-edge candidates;
- whether an unexamined deeper frontier exists; and
- the global charged node and edge totals.

Distance is assigned when a member is first enqueued. Another charged edge to
that member at the same distance is retained as an additional admissible
successor but does not enqueue or charge the member again. A longer
discovery is ignored after its edge is charged. The same member or edge
examined by another task consumes work again.

The transition for one queue entry is:

1. Check global search-node capacity before dequeue. On success, charge one
   node and make the entry current.
2. Test the task goal. A source goal is recorded and is not expanded; after one
   source depth is found, entries at that depth complete and deeper entries are
   left on the frontier. A boundary goal is likewise recorded without expansion;
   after one selected-member depth is found, every entry at that depth completes
   and deeper entries are left on the frontier.
3. If the current depth equals the task depth limit, record an unexamined depth
   frontier and complete the entry without examining incoming edges.
4. Otherwise examine incoming edges in canonical method-edge order. Check
   global searched-edge capacity before each examination, then charge the edge
   and apply the distance rule above.

If the queue drains without a goal, an observed depth frontier produces
`DepthLimited`; otherwise the task is `Unreachable`. `DepthLimited` and
`Unreachable` complete only the current task and the total task queue
continues. Search-node or searched-edge exhaustion stops the whole queue.

`Reconstruct` consumes no new search work. For each source goal, it follows the
smallest admissible forward edge that decreases distance by one, then chooses
the minimum `(path-length, forward-edge-key-sequence, source-member-key)`.
A boundary task reconstructs from each reached selected member to one terminal
member in the same way, appends that terminal's smallest canonical boundary
edge, and chooses the minimum full forward edge sequence. This explicitly
minimizes forward path order; reverse traversal order cannot choose a different
equal-length witness.

`Retention` is atomic. A reconstructed witness checks capacity in this order:

1. retained connector witnesses;
2. newly introduced projected graph nodes; and
3. newly introduced projected graph edges.

If one check fails, none of that witness is retained. A direct-selected-edge
task atomically checks only projected-edge capacity because its endpoint
components were already projected. Exactly filling a capacity is complete
until another admissible task requires it.

Witness, projected-node, or projected-edge exhaustion stops the whole remaining
queue without discarding already retained selection observations or physical
evidence. Each task records `Retained`, `Unreachable`, `DepthLimited`,
`SearchNodeLimited`, `SearchEdgeLimited`, `WitnessLimited`, or
`ProjectionLimited`.

Every global stop retains one typed session-bound `ConnectorFrontier` with:

- stage: selected-component projection, distance search, reconstruction,
  witness retention, or direct-edge retention;
- current and next task keys;
- exact exhausted limit and all charged/retained counters;
- for search, queue entries with depths, distance and admissible-successor maps,
  current entry, next edge cursor, source and selected-member goals, and
  depth-frontier state;
- terminal-member-to-boundary-edge candidates for boundary search;
- for reconstruction, the candidate goals, current reconstruction cursor, and
  chosen edge prefix; and
- for retention, the fully reconstructed path and next capacity check.

Selected-component projection and direct-edge frontiers name their atomic
current component or edge. The frontier is sufficient to resume the same
generation without repeating or skipping charged work. Portable detachment may
replace it with a non-resumable boundary naming the same stage, current task,
exhausted limit, and omitted work; it never claims the remaining queue was
searched.

A selected component without a retained graph-source path remains visible and
qualified. A disconnected result contains several spine components rather
than inventing a connector. Methodology version 1 has no candidate-dispatch
overlay.

### Result shape

The detached Research result conceptually contains:

```text
ImplementationSpineSelection
  Methodology
  PopulationReceipt
  SelectedComponents[]
    CanonicalComponentKey
    ExactMembers[]
    SelectionObservations
    Qualifications[]
  ConnectorWitnesses[]
    GraphSourceComponentKey
    CallOccurrenceReferences[]
    BoundaryIdentity?
    Completion
  ConnectorFrontier?
  Limits[]
  Failures[]
```

Queries binds this selection to the same owner-issued subjects and
occurrences in an `InspectionGraphDocument`. The completed host-neutral
operation exposes an `InspectionEnvelope<TContent>` whose Content preserves
both the selection explanation and the graph projection. No host reparses
labels or recomputes the vector, SCCs, or paths.

`CallOccurrenceReference` is owner-issued and bound to the exact call-census
receipt and assembly-group/catalog generation. It is never a dense projection
id. While bound, it retains the acquisition-aware
`CallGraphCallSiteIdentity`. A portable report artifact requires the call
owner's explicit detachment to durable artifact-member identity plus the
physical evidence MethodDef, IL offset, and operand token. If any subject or
occurrence has no safe portable projection, detachment fails visibly or the
document remains `SessionBound`; labels are never substituted.

`InspectionGraphDocument.Scope` is authoritative. Structured output used for
cross-command report orchestration must be `Portable`, carry the spine
methodology and population receipts, and reject a join to artifacts with a
different subject, traversal, call-census, or methodology receipt.

## Service-oriented prerequisite inventory

The service migration is a prerequisite map, not part of the spine owner's
normative algorithm.

| Functionality | Current state | Spine relevance |
| --- | --- | --- |
| Library body execution | `LibraryBodyAnalysisService` publishes detached focused results | Reuse directly |
| Local caller/callee populations | `LibraryCallGraphAnalysisResult` owns trees and direct calls | Reuse directly |
| Cross-Library catalog correspondence | `CatalogCallGraphScope` accepts focused call-graph participants | Add a graph-wide detached call census without returning to `LibraryBodyIndex` |
| Method leverage | `LibraryLeverageAnalysisResult` is focused and detached | Use as an optional annotation or comparison input, not the spine definition |
| Implementation profiles | Focused Analysis result and Research comparison exist | Reuse for characteristics |
| Pairwise direct-use acquisition | `AssemblyPairCallUseQuery` still opens compatibility indexes | Migrate participant execution to focused call-graph results |
| Root paths | `LibraryBodyRootPathAnalysis` and `AssemblyPairClusterRootPathQuery` still consume a compatibility index | Move the path input to `LibraryCallGraphAnalysisResult` |
| Argument and receiver populations | Direct-call argument evidence exists, while result sinks, field stores/loads, and return flows remain index-only | Not required by methodology version 1; a separately approved dispatch design decides whether and how to consume a focused result |
| Member Research composition | `AssemblyContextMemberProjectionQuery` still creates `CompatibilityIndex` and `ResearchAssemblyContext` | Consume exact focused results and receipt-associated Research inputs |
| Body-signal comparison | `BodySignalComparisonInput` and parts of `ResearchDiff` remain index-shaped | Migrate only when the reporting or comparison consumer requires those facts |
| Implementation comparison | Complexity consumes focused profiles, while broader implementation inputs still retain body indexes | Preserve the existing Research control plane and replace index evidence owner by owner |

Static callable APIs are not migration failures by themselves. A stateless
operation with explicit inputs and detached outputs already conforms to the
service-oriented architecture. The migration target is removal of semantic
dependence on the compatibility aggregate, not replacing static methods with
retained service instances.

## Real-package evidence

The motivating package is
[`Microsoft.Azure.SignalR` 1.33.1](https://www.nuget.org/packages/Microsoft.Azure.SignalR/1.33.1)
for `net8.0`.

Its package payload contains:

- `Microsoft.Azure.SignalR.dll`; and
- `Microsoft.Azure.SignalR.Common.dll`.

`Microsoft.Azure.SignalR.Protocols` 1.33.1 is a separate direct package
dependency referenced by both package-owned Libraries.

A current repository CLI probe over the two package-owned Libraries found:

- 199 exact cross-Library call sites;
- 51 deterministic pairwise direct-use clusters;
- all observed pair calls directed from `Microsoft.Azure.SignalR` to
  `Microsoft.Azure.SignalR.Common`; and
- one dominant cluster with 62 call sites, 21 consumer methods, 27 provider
  methods, and 12 provider types.

The dominant cluster includes messaging, connection-management, and invocation
relationships around `ServiceLifetimeManager`, `ClientConnectionContext`,
`IServiceConnection`, and `IClientInvocationManager`. Its current public-root
path population is empty, while each Library has substantial Analysis-issued
root reach. The difference motivates a separately owned ingress study; this
design draws no ingress-classification or completeness conclusion from it.

The probe is reproducible design evidence:

```console
dotnet run --project src/DotnetInspect.Cli -c Release -- \
  graph libraries \
  --library ./Microsoft.Azure.SignalR.dll \
  --library ./Microsoft.Azure.SignalR.Common.dll \
  -S "Direct Use Clusters" --jsonl
```

The future package operation must acquire the same assets through the ordinary
Package and Workspace path rather than requiring manual extraction.

## Pathological evidence

Implementation must preserve these boundary cases:

1. An exact call graph contains recursion. SCC condensation preserves every
   physical occurrence and deterministic selection.
2. A large Library forms one broad exact component. The result reports the
   component and its support observations without calling it spaghetti code.
3. Several high-support components are disconnected. The result retains
   several spine components rather than a synthetic edge.
4. A selected package dependency is incompatible, pruned to Platform,
   unavailable, or work-bounded. Its owner-issued outcome remains visible and
   cannot become an empty graph.
5. An unresolved virtual or interface call is the only apparent connector
   between two implementation bodies. Methodology version 1 stops at the
   static slot boundary and keeps the bodies disconnected.
6. A generated async body carries the physical call site for a logical source
   method. The logical node receives the relationship while the occurrence
   retains the generated MethodDef and IL offset.
7. A search or projection bound stops connector retention after positive
   evidence was found. The partial graph, current task, BFS frontier, and exact
   exhausted limit remain visible.
8. In `A -> B -> C`, where `B -> C` crosses the named boundary, connector
   depth one admits the source witness `A -> B` but reports the boundary witness
   from `A` as `DepthLimited`; it never retains `A -> B -> C`.

The initial implementation fixtures should cover these shapes with small
independently compiled assemblies. `Microsoft.Azure.SignalR` remains the
real-package package-graph probe and should become pinned corpus evidence when
the ordinary package operation exists.

## Rendering

The typed result uses the existing Inspection Graph rendering strategy.
Markout remains the CLI lowering substrate:

- Markdown explains the methodology, population, selection observations,
  qualifications, and graph;
- table and JSON shapes expose selected components and exact edge rows;
- Mermaid groups nodes by Library and Package and distinguishes compiled calls
  from bodiless, external, unresolved, and work-bounded boundaries; and
- projected JSON retains machine-joinable subject and occurrence identities.

Solid edges represent retained compiled-call relationships with physical
occurrences. The call characteristic and boundary plane retain virtual,
interface, indirect, unresolved, external, and incomplete distinctions. A
renderer cannot use line style alone as the semantic distinction.

Inspect Web may provide interactive expansion, collapse, characteristic
selection, and navigation. It consumes the same typed selection and graph and
does not implement another ranking or connector algorithm.

## Adoption plan

The program has eleven counted production steps:

1. Lock this focused composition and methodology contract.
2. Migrate pairwise call-use acquisition and local root paths from
   `LibraryBodyIndex` to focused call-graph results.
3. Publish a graph-wide exact call census over an admitted assembly group,
   preserving physical occurrences and graph diagnostics.
4. Add call-census detachment that either issues a portable physical occurrence
   reference or fails visibly.
5. Publish the owner-issued package source-operation context that executes all
   admitted resolved edges, invokes Package Dependency Workspace Routes, and
   exposes detached dependency destinations to the shared dependency-aware
   call-graph service.
6. Compose restored Project dependency facts and traversal with a focused
   Project-output artifact realization and Workspace admission.
7. Implement the Research spine selection and detached result, then bind it to
   the Inspection Graph through the ResearchQueries companion.
8. Add the `graph spine` CLI operation for Library and Package subjects,
   subject-backed sections, Markout
   lowering, JSON artifacts, Mermaid demo, help, and relationship-skill
   guidance.
9. Add Project CLI execution after step 6 supplies exact output and dependency
   participants.
10. Add Inspect Web Library, Package, and Project spine visualization through
   the shared
   managed operation.
11. Add the layered reporting skill, with manually reproducible commands and
   automated typed-artifact orchestration for Library, Package, Dependency,
   and Project reports.

Steps 2 through 6 remain focused owner adoptions and may split further. This
composition does not authorize one implementation PR to change Analysis,
Metadata, Workspace, Research, Graph, CLI, Browser, and skills together.

The alternative architecture is direct skill-side orchestration over tables
and rendered graphs. It retires when step 10 consumes the owner-issued spine
operation; no compatibility obligation preserves skill-derived ranking,
joins, or paths.

## Required gates

Each implementation slice names its owner-specific Release gates. The complete
production path must eventually establish:

- a sealed population cannot mix Workspace realizations, definition snapshots,
  assembly groups, or catalog generations;
- every compiled-call spine edge retains at least one physical call occurrence;
- a static virtual or interface slot is never presented as a proven runtime
  implementation target;
- SCC condensation retains all internal and external exact occurrences;
- the selection vector and ordering are deterministic under input permutation;
- component admission exhaustion returns `SelectionUnavailable` rather than
  ordering partial observations;
- reach counts retain their exact population and graph-generation receipts and
  qualifications;
- equal shortest connector alternatives use the documented deterministic
  tie-break;
- source and boundary witnesses count every retained method edge against the
  same connector-depth limit, including the boundary-crossing edge;
- connector member, boundary, depth, search-node, search-edge, witness,
  projected-node, and projected-edge limits have the documented ordering and
  frontier outcomes;
- disconnected selected populations remain disconnected;
- positive evidence survives acquisition, Analysis, and result boundaries
  without creating a complete absence claim;
- a selection never joins by dense call-site id, display name, or input order;
- portable output either retains durable artifact and physical instruction
  identity or fails visibly;
- CLI and Browser consume one owner-issued selection and graph result;
- JSON artifact joins reject mismatched subject or generation receipts; and
- the reporting skill computes no semantic fact that the tool did not issue.

Properties without an implementation gate remain `unverified`.

## Non-claims

This design does not:

- define a universal code-quality, maintainability, or architecture score;
- infer authored design intent or feature names;
- claim runtime frequency, latency, or execution without a separately admitted
  runtime observation;
- close virtual dispatch over an open Workspace population;
- treat package dependencies as calls;
- rank packages by popularity, downloads, or ecosystem importance;
- require network work for an already admitted local Library request;
- make exhaustive package traversal or expensive Analysis a default operation;
- replace direct Calls, Callers, Top Leverage, Member Metrics, Library Metrics,
  Depends, Graph Libraries, or member Call Graph
  surfaces before their useful workflows have replacement parity; or
- add another graph envelope or host-specific semantic implementation.
