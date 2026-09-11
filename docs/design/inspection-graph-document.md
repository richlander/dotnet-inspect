# Inspection graph document

How dotnet-inspect projects call, metadata, integration, Finding, and analysis
evidence into one typed graph that can move between members, types, assemblies,
and packages without turning every relationship into a call.

Related documents:

- [Call-graph projection](call-graph-projection.md) owns the current
  member-to-member call topology, identity, boundaries, and stable edge rows.
- [Call-graph characteristics](call-graph-characteristics.md) maps the current
  call-specific node fields and loop label into this document's descriptor
  model.
- [Inspection-graph modes](inspection-graph-modes.md) owns single-seed,
  peer-seed, and induced-set requests across member, type, assembly, and package
  subjects.
- [Member body substrate](member-body-substrate.md) owns
  `AnnotatedSourceDocument` and the fact-to-target join that motivates this
  design.
- [Inspection space](../inspection-space.md) owns workspace groups, query
  planning, acquisition lifetimes, currencies, and cross-producer joins.
- [Inspection layers](inspection-layers.md) owns the L1/L2/L3 consumer
  boundaries.
- [Output shapes](output-shapes.md) owns graph row semantics and sink
  projection.
- [Progressive disclosure](progressive-disclosure.md) owns selection,
  capabilities, and cost backpressure.
- [Section model](section-model.md) owns structural versus effective discovery,
  including producer budgets.
- [Graph signal annotations](graph-signal-annotations.md) describes the current
  node-only `--fields` mechanism this design generalizes.
- [IChatClient dual-lens demo](../workflows/discovery/aspire-ai-package-graph.md)
  supplies the locked type-to-package and package-to-type experience.

## Status

This document is the proposed parent architecture for the expanded graph
requirements that emerged from issue #4139 and the locked #4127 demo. The
call-specific #4139 migration is detailed in
[Call-graph characteristics](call-graph-characteristics.md). Requirements in
this document are design targets and are unverified until an implementation
slice names and adds its gates. Existing behavior is identified explicitly and
names its current owner or gate.

The design does not freeze a public CLR API, command spelling, or serialized
schema. It freezes the separation of identity, topology, physical occurrences,
characteristics, evidence, projection, and presentation that those surfaces
must preserve.

## Thesis

The most useful graph is not confined to one abstraction level.

A user should be able to start with a type, follow member-level evidence, and
land on the packages and integrations that matter. The inverse query should
start with packages and land on the types and APIs that explain their
relationship. Both views must retain the same evidence rather than reconstruct
relationships from labels.

The architecture is:

```text
producer-owned evidence
  call sites | metadata references | extensions | integrations | Findings
                                |
                                v
typed relationship occurrences over owner-issued subjects
                                |
                                v
inspection graph projection
  nodes | groups | logical edges | occurrences | characteristics | limits
                                |
                                v
viewer-selected lens
  member | type | assembly | package | mixed
                                |
                                v
Markout graph | browser graph | edge table | JSON
```

The current member call graph remains the authoritative source for call
topology. The expanded experience is an **inspection graph**: a shared
projection envelope in which calls remain calls and other producers contribute
their own typed relationships.

This distinction is semantic, not branding. A product surface may describe the
experience as an expanded call graph because call evidence is often its
backbone. The data contract must never describe a metadata reference,
integration opportunity, structural clone, or narrative link as a call.

## Goals

- Start from a member, type, assembly, or package and project onto any of those
  subject kinds when evidence supports the path.
- Show types and packages simultaneously as nodes, groups, or endpoints in one
  graph.
- Preserve member and physical-occurrence evidence when a view rolls up to
  types or packages.
- Carry typed relationship semantics and selectable characteristics on nodes,
  groups, logical edges, and physical occurrences.
- Compose existing and future Analysis, Metadata, Findings, and Research
  investments without moving their algorithms into graph rendering.
- Preserve lean defaults, explicit expensive work, typed failures, deterministic
  ordering, and browser/Wasm compatibility.
- Keep seeded and induced graph modes orthogonal to subject lens,
  characteristic selection, and output format.

## Non-goals

- A universal repository-wide identity, correspondence key, or evidence base
  type.
- Replacing `CallGraphProjection`, producer-owned Finding payloads, metadata
  queries, or integration queries with one graph IR.
- Treating package, type, or display text as a substitute for member or artifact
  identity.
- Inferring relationships across assembly context groups.
- Baking selected labels into L1 results or adding a formatter per sink.
- Making all Findings, analysis domains, source acquisition, or transitive
  traversal part of the default view.
- Path characteristics in the first delivery. Ordered paths remain typed
  witnesses, such as `CallGraphCycleWitness`, until a path-target contract is
  justified.
- Representing a narrative or synthetic relationship unless a producer owns
  evidence and semantics for it.

## Comparison with `AnnotatedSourceDocument`

`AnnotatedSourceDocument` is the design oracle for separating a carrier,
structure, observations, targeting, and presentation. The inspection graph
adopts that separation, not its text-specific shape.

### Where the designs overlap

| Concern | `AnnotatedSourceDocument` | Inspection graph |
| --- | --- | --- |
| Carrier | Canonical rendered `Text` | Typed subject and relationship topology |
| Structure | Nodes and regions over text | Nodes, groups, logical edges, and seed roles |
| Observation plane | Facts stated independently of text | Characteristics stated independently of labels |
| Targeting | `Fact -> Target -> Node` | `Characteristic -> GraphTarget` |
| Physical receipt | Fact source offset and targeted source structure | Producer-native relationship occurrence and evidence |
| Local joins | Contiguous document-local fact/node ids | Deterministic document-local node/group/edge/occurrence ids |
| Presentation | Carets, side comments, and filters derive from facts and targets | Labels, fields, tables, and interaction derive from typed relationships and characteristics |

Both designs preserve these rules:

1. Structure exists independently of observations. Most source nodes have no
   fact; a graph node or edge likewise need not have an optional
   characteristic.
2. An observation is stated as data and joined to a stable document target.
   Neither consumer reparses rendered text or labels to recover the join.
3. Display gestures are projections. A source fact does not contain a caret,
   and a graph characteristic does not contain Mermaid or Markdown text.
4. Document-local ids are joins only. They are deterministic within the
   document and carry no portable identity claim.
5. Producer vocabulary is typed and additive. Producers emit registered
   descriptor/target combinations; older consumers tolerate unknown additive
   kinds or descriptors.
6. One semantic observation is not duplicated merely because it has several
   visible hooks. `AnnotatedSourceDocument` uses one fact with several targets;
   a graph retains one physical occurrence when several walks observe it.

The useful shorthand remains "arcs are to graphs what carets are to annotated
source." Both are reader-visible hooks over typed data. In neither design is
the visible hook the semantic storage.

### Where the designs diverge

| Boundary | `AnnotatedSourceDocument` | Inspection graph |
| --- | --- | --- |
| Authoritative artifact | One exact rendered UTF-16 text buffer | Semantic topology; no canonical rendered graph string |
| Coordinate system | One absolute span currency over `Text` | Producer-native coordinates retained separately; no universal coordinate |
| Scope | One member and its C#/IL rendering | Many subjects and artifacts across one or more workspace groups |
| Node meaning | Rendered syntax or IL text structure | Owner-issued member, type, assembly, or package subject |
| Region/group meaning | Named syntactic parts over text; not fact targets | Typed subject containment or presentation grouping; valid characteristic targets |
| Primary semantics | Positive descriptive facts | Typed relationships, evidence occurrences, optional characteristics, limits, and failures |
| Join shape | Exactly one non-polymorphic fact-to-node target relation | Distinct typed joins for edge endpoints, occurrence support, characteristics, groups, and seeds |
| Completeness | Positive facts make no all-clear claim; an unanchored fact remains visible | Absence claims depend on traversal limits, producer failures, and completeness |
| Aggregation | One fact may target several source nodes | One logical edge may aggregate several distinct physical occurrences |
| Failure model | Malformed coordinates invalidate construction; projection failure is outside the document | Scoped acquisition and producer failures remain visible beside healthy graph evidence |
| Execution | Immutable projection of already-rendered member evidence | Workspace-planned producer demand with costs, capabilities, bounds, and sequential orchestration |

Those differences are constraints, not missing generalization:

- Graph nodes must not reuse `AnnotatedSourceNode`; source nodes identify
  rendered characters, while graph nodes identify semantic subjects.
- Graph targets must not reuse `AnnotatedSourceTarget`. The source document's
  one target shape is valuable precisely because every target is a text node;
  graph targets legitimately include node, group, edge, and occurrence.
- Graph evidence must not be normalized into UTF-16 spans or one invented
  coordinate. IL offsets, metadata rows, package coordinates, Findings, and
  comparison witnesses keep their producer-native currencies.
- Relationship kind is not the graph equivalent of a source fact. It is
  mandatory topology. Optional characteristics are the closer analogue to
  facts.
- Logical-edge aggregation must not copy source-fact deduplication blindly.
  Two C#/IL targets can describe one source observation, while two physical
  call sites remain two graph occurrences even when they share one edge.

### The composition boundary

The existing `AnnotatedMemberDocument` demonstrates how the models compose
without merging. Its call overlay keeps the source document unchanged:

```text
graph edge row -> physical occurrence -> fact -> target -> node -> spans -> text
```

The occurrence carries the stable graph edge row and an ordinary source fact
id. Placement continues through `AnnotatedSourceDocument.Targets`; the graph
does not add an edge-to-source-node join or duplicate source spans. The
`AnnotatedMemberDocument_ReusesCalleeLayerAndMapsEveryPhysicalCallSite` gate
pins the current focus-call-site form.

The expanded inspection graph should preserve that boundary. A graph
occurrence may cite an `AnnotatedSourceDocument` fact or other producer receipt
as evidence, but source placement remains source-owned. Conversely, an
annotated-source viewer does not need workspace traversal, package identity, or
graph aggregation merely because a fact also participates in a graph.

## Conceptual document

The following shape is conceptual. Names may change when concrete types land.

```text
InspectionGraphDocument
  Scope

  Nodes[]
    Id
    Subject
    Role
    GroupIds[]

  Groups[]
    Id
    Subject
    ParentId?

  Edges[]
    Id
    FromNodeId
    ToNodeId
    Relationship
    OccurrenceIds[]

  Occurrences[]
    Id
    Relationship
    SourceSubject
    TargetSubject
    Evidence
    DerivedFromOccurrenceIds[]

  Characteristics[]
    Descriptor
    Target
    Value
    Derivation

  Seeds[]
    Subject
    Target
    Role

  Limits[]
  Failures[]
```

The shape has five important properties:

1. Document-local ids are joins, not display values or portable subject
   identities.
2. Every node and group carries an owner-issued typed subject.
3. Every seed role binds an owner-issued subject to a node or group carrying
   that same subject; an induced-set document has no seed roles.
4. Every edge carries a typed relationship descriptor even when its renderer
   chooses not to print the descriptor.
5. Every occurrence bound to an edge has the same relationship as that edge,
   and its source and target project to the edge endpoints without reversing
   semantic direction.
6. Every logical edge can retain zero or more physical or derived occurrences.
7. Limits and failures remain separate from optional characteristics.

An edge with no occurrence is permitted only for an explicitly synthetic
relationship whose producer and derivation are part of the edge contract. It
must not look like observed call or metadata evidence. The first implementation
should require at least one occurrence for every non-synthetic edge.

The call adapter registers the observed `call` relationship with exact
member-to-member endpoint projection. Product-built call trees retain every
physical call site supporting each projected edge, so
`CallGraphInspectionGraphAdapter` emits physical `call.site` receipts plus
typed occurrence and edge characteristics. Evidence-free trees constructed by
external or synthetic callers retain the earlier `call.logical-edge` receipt
and edge-scoped `call.physical-occurrences-unavailable` limit rather than
fabricating a call site. A product edge with only a subset of its physical
receipts retains those receipts and the same limit, but omits aggregates that
would imply the subset was complete.
`CallAdapter_RetainsPhysicalSitesAndTypedAggregates`,
`CallAdapter_TreatsPartialPhysicalEvidenceAsIncomplete`,
`CallAdapter_PreservesAcquisitionDistinctReceipts`, and
`AnnotatedMemberDocument_ReusesCalleeLayerAndMapsEveryPhysicalCallSite`
gate the physical and compiler-produced paths.

## Subject identity

### Owner-issued currencies

A graph node does not introduce a universal subject key. Its `Subject` is a
discriminated reference to a currency owned by the domain that can answer the
identity question:

| Subject kind | Candidate owner-issued currency |
| --- | --- |
| Member | Analysis graph identity while bound; artifact member identity or a validated portable member projection when detached |
| Type | Metadata lookup/address or API identity appropriate to the query scope |
| Assembly | acquisition registration and assembly identity/provenance inside a group; a detached artifact projection outside it |
| Package | the realized package coordinate, including version, producer, framework, and RID where applicable |

The exact currency depends on scope and lifetime. The graph must retain that
scope rather than erase it into a string. The authoritative currency map
remains [Type, member, and API representation](type-member-api-representation.md).

Display equality never joins graph subjects. `IChatClient` in two assemblies,
two package versions, or two binding contexts may render alike and remain
distinct. A package id without version, acquisition producer, and effective
target is likewise not enough to identify a realized package participant.

### Bound and portable documents

A bound graph may refer to catalog-generation or workspace-lifetime currencies
while their owners remain alive. A portable graph must detach explicitly:

- exact artifact members retain artifact identity and a durable address;
- exact package subjects retain their realized package coordinate;
- unresolved or indeterminate occurrences remain document-local and retain
  their evidence;
- incomplete occurrences remain distinct;
- generation-scoped correspondence is removed rather than serialized as if it
  were durable.

This follows `CatalogCallGraphScope.Detach`. A portable projection loses
authority deliberately; reopening it does not recreate catalog correspondence.
If a subject kind has no safe portable projection, portability is a typed
failure or the graph remains session-bound. A display label is never the
fallback.

`InspectionGraphDocument.Scope` makes that lifetime claim explicit as
`SessionBound` or `Portable`; consumers do not infer it from subject display or
from which evidence happens to be present. Each owner-issued subject identity
declares whether it retains session authority, and portable construction
rejects any subject that does. The call adapter derives the scope from the
`GraphNodeIdentity` values and physical receipt identities the projection
actually used rather than accepting a caller assertion. Acquisition-aware call
receipts make the document session-bound even when their logical endpoint
subjects have portable detached identities.

The Integration Census adapter uses graph subject variants that wrap its exact
candidate-source, resolved-Type, and participant identities. Those variants
remain session-bound even when the participant carries a realized coordinate:
the admitted occurrence retains an
`IntegrationCandidateAttemptAddress`, whose binding-context identity has no
portable contract. This bridge preserves the Integration owner's currency
without inventing an acquisition registration or treating display text as
identity; it does not settle the deferred general portable Type-subject design.

### Nodes and groups

Package and type are both subject kinds and grouping lenses.

The same package subject may be:

- a node in a type-to-package package web;
- a group containing member or type nodes in a detailed call graph; or
- both in a composed document when the view needs a package endpoint and a
  package-owned expansion.

Node identity and group identity remain separate document-local roles even when
they reference the same domain subject. Renderers may lower a group as a
Mermaid subgraph, a table column, a badge, or nothing. Group membership is
structured data and must not be recovered from a node label.

`InspectionGraphPackageBoundary` implements this boundary over realized
workspace members. It joins an acquired assembly to its package through the
participant's opaque acquisition registration, validates the package identity
and version against package-asset provenance, and projects the resulting
package subject as an assembly group, a package node, or both. The realized
framework and RID remain the effective acquisition target; provenance retains
the selected physical asset target, which may differ after compatible fallback.
Assembly nodes retain acquisition-bound identity, so matching metadata
identities from two acquired artifacts do not collapse. A package-only lens
retains only portable realized package coordinates.
`WorkspaceContextLoaderTests.PackageBoundary_ProjectsLoadedPackageAsGroupAndNode`
gates the compiled package-acquisition path;
`WorkspaceContextLoaderTests.PackageBoundary_KeepsEffectiveTargetAcrossAssetFallback`
gates the effective/physical target distinction; and
`InspectionGraphPackageBoundaryTests.PackageGroupsLens_DoesNotCollapseMatchingAssemblyMetadata`
gates the close acquisition-identity case.

This describes the current implementation. Under the target
[artifact acquisition design](artifact-acquisition-and-workspaces.md), the
package adapter validates the coordinate, physical asset, producer, and content
before minting its realization and correspondence proof. Package graph
projection moves to an optional package-query companion and consumes that
proof; core assembly Queries no longer parse package versions or pattern match
Metadata-owned package provenance. The serialized `package` subject kind
remains part of the full host's graph contract, while a package-free host does
not reference the package projection implementation.

The platform adapter performs the equivalent validation and mints the platform
correspondence proof. Platform graph projection consumes that proof without
depending on the package companion or Metadata-owned platform provenance.

## Relationships and occurrences

### Relationship descriptors

A relationship descriptor defines:

- a stable id;
- semantic direction and endpoint roles;
- admitted edge source and target subject kinds;
- admitted original-occurrence source and target subject kinds;
- admitted seed subject kinds, entry mechanisms, and semantic endpoint roles;
- the producer that owns the relation;
- whether the relation is observed, derived, or synthetic;
- the occurrence evidence contract;
- the occurrence identity and deduplication contract;
- allowed aggregation and roll-up behavior.

Illustrative relationship families include:

| Relationship | Typical endpoints | Owner |
| --- | --- | --- |
| `call` | member -> member | CallGraph |
| `package.dependency` | realized package -> realized package | Packages |
| `package.contains-artifact` | realized package -> assembly | Packages |
| `metadata.contains` | assembly or type -> type or member | Metadata |
| `metadata.reference` | assembly, type, or member -> assembly, type, or member | Metadata |
| `api.extension` | extension member or provider -> extended type | Metadata |
| `api.implements` | type -> interface type | Metadata |
| `integration.observed` | API/type/package -> API/type/package | Metadata |
| `integration.composed` | API/type/package -> API/type/package | Research |
| `integration.opportunity` | consumer API/type/package -> candidate API/type/package | Metadata |
| `analysis.async-sibling-opportunity` | call site or member -> candidate member | Research |
| `implementation.structural-match` | member -> member | Analysis |

These ids are examples, not a frozen catalog. The first catalog slice must
choose ids, endpoint constraints, and exactly one owner per descriptor before
code depends on them. A Research relationship may reference a Metadata- or
Analysis-owned Finding as evidence without sharing ownership of the graph
relationship.

Relationship display titles, categories, field aliases, and default disclosure
belong to the L2 presentation binding, not this L1 semantic descriptor. L2 binds
to the descriptor through a typed catalog; it does not rediscover relationship
semantics from the stable id or a label.

Relationship direction is semantic and does not change with traversal
direction. A package-inward query may traverse incoming integration edges, but
the document still renders each edge in the direction defined by its
descriptor. A viewer may lay out the graph differently; it may not reverse the
claim.

### Logical edges

Logical edge identity includes at least:

```text
source subject + target subject + relationship descriptor
```

A producer may add a typed discriminator when two distinct logical
relationships of the same kind connect the same subjects. That discriminator
is producer-owned evidence, not a label.

This extends the current call projection, which collapses by `(From, To)`.
Different relation families between the same nodes must never collapse. A call
and a metadata reference may share endpoints and remain two logical edges.

### Physical and derived occurrences

An occurrence is the receipt for one contribution to a logical edge.

Examples:

- one IL call site with caller body, IL offset, operand token, call kind, and
  loop/dispatch evidence;
- one `MemberRef` or `TypeRef` metadata row;
- one extension method whose first parameter names the extended type;
- one integration observation or opportunity Finding;
- one exact structural-comparison witness.

Occurrences retain producer-native coordinates and provenance. They do not
invent a common coordinate system. A call-site IL offset stays scoped to its
physical body; a metadata row stays scoped to its image; a Finding retains its
descriptor, key, subject, and payload.

An occurrence also retains the original producer-owned source and target
subjects. The logical edge's node ids are the selected view endpoints; they may
be aggregate type or package nodes. Occurrence endpoints remain the member,
type, assembly, or package subjects that supplied the evidence. Several member
pairs may therefore collapse onto one type-to-package edge without losing which
members established it.

An edge may bind only occurrences whose `Relationship` equals the edge
relationship. The relationship descriptor owns a typed endpoint-projection
rule: the occurrence source must equal or project to the `FromNodeId` subject,
and the occurrence target must equal or project to the `ToNodeId` subject.
Traversal direction never changes that support relation. A package-inward lens
may walk an incoming edge, but it cannot bind a source occurrence to the edge's
target.

Edge and occurrence endpoint domains are distinct descriptor constraints. A
type-to-package edge can therefore require type and package view endpoints
while admitting only member-to-member original occurrences. The projection
rule receives the typed occurrence, including its producer evidence, and the
semantically directed selected endpoint. It does not recover package ownership
from labels or capture an unvalidated per-document side map.

Seed admission is also descriptor-owned and separate from endpoint projection.
A seed may enter a relationship as an exact logical `EdgeEndpoint`, as an
original `OccurrenceEndpoint` retained behind a rolled-up edge, or through
typed `OwnedSubjects`. Every admission names semantic source or target;
incoming traversal never changes that role. Callers can query all matching
admissions without collapsing their entry kind or role. Direct edge and
occurrence admissions must use a subject kind declared by the corresponding
descriptor endpoint domain. An owned-subject admission must name a strict
typed owner of a kind in that semantic endpoint domain; it authorizes later
expansion but does not change the logical edge's subject kind or direction.

The single-seed neighborhood requires at least one selected relationship and
the seed kind and semantic direction must be admitted by at least one of them.
Integration catalog validation occurs before producer execution and fails with
the selected relationship id and typed guidance.

Explicit induced-set requests carry no seeds and do not use the directed seed
admission gate. Their `BothEndpointsWithinSubjectClosure` rule evaluates each
physical occurrence independently: both semantic roles must be an exact
logical endpoint, an exact original occurrence endpoint, or strictly owned by
one of the finite typed input subjects. This preserves roll-up receipts without
turning induction into a containment traversal. Integration catalog membership
is still validated before any producer runs.
`RelationshipDescriptor_ValidatesAndSnapshotsSeedAdmissions`,
`AdmissionsMatchDeclaredEndpointDomains`, and
`RelationshipCatalogsDeclareCurrentSeedAdmissions` gate the implemented
descriptor contracts. `InducedSetRequest_ValidatesAndSnapshotsExplicitInputs`
and `Execute_ExplicitInducedSetRejectsUnsupportedRelationshipFirst` gate the
separate induced-set contracts.

Each relationship descriptor owns an occurrence-identity projection within one
document. Projection deduplicates repeated observations by that key before
assigning deterministic document-local occurrence ids. For `call`, the key is
the physical body and call-site storage identity: artifact or acquisition
identity as appropriate to the document lifetime, evidence-method token, IL
offset, and operand token. `DirectCall.Caller` separately retains declared
source attribution. Observing that call site from caller and callee walks cannot
create two occurrences. Two distinct IL offsets remain two occurrences even
when every other field and the logical edge are equal.

Producers perform that projection and deduplication before document
construction. `InspectionGraphDocument` independently rejects duplicate
projected identities, so a producer cannot silently publish two receipts for
one occurrence.

Composition does not weaken the relationship invariant. A composed edge gets a
derived occurrence of the composed relationship, with its own source and target
subjects and explicit `DerivedFromOccurrenceIds` receipts for the native
evidence. It must not directly attach a call, metadata-reference, or extension
occurrence to an `integration.composed` edge.

`CallGraphProjection.CallSites` is the document-wide seam: every product-built
tree edge retains its physical `DirectCall` receipts, and projection
deduplicates the same call site when caller and callee walks both observe it.
Each receipt retains IL offset, operand token, call kind, loop state, and a
descriptive dispatch classification. Its opaque typed identity retains the
caller acquisition when catalog evidence is complete, so two acquired artifacts
with identical MVIDs and call coordinates remain distinct occurrences while
both producer deduplication and document validation use the same currency.
`AnnotatedCallGraphOccurrence` remains the source-overlay seam for focus-member
facts; it maps physical coordinates from the selected member's own evidence
body to stable edge rows and source facts. Receipts from async or lifted
evidence bodies remain graph occurrences, but do not borrow the declared
kickoff body's source anchors.

## Characteristics

A characteristic is a typed description attached to a graph target. It is not
topology, identity, completeness, or preformatted label text.

### Semantic descriptor contract

An L1 characteristic descriptor defines:

- stable id;
- admitted targets: node, group, logical edge, or occurrence;
- typed value shape;
- producer and typed query prerequisites;
- direct versus derived meaning;
- aggregation policy for occurrence-to-edge and subject roll-up.

The prerequisite query definition owns cost and capabilities. The descriptor
does not repeat them.

### Presentation binding

L2 binds a semantic descriptor into the section schema. That binding owns:

- display title and topical category;
- node, group, edge, or occurrence field name and aliases;
- default disclosure and authored focused presets;
- target-specific schema metadata; and
- label-composition policy for graph renderers.

The semantic descriptor remains useful to an L1-only browser consumer without
bringing Markout, section categories, CLI field names, or disclosure policy
downward. Conversely, L2 never reruns the producer or redefines aggregation.

Illustrative L2 presentation layers include:

| Layer | Target | Examples |
| --- | --- | --- |
| Scale | node | fan-in, fan-out, depth |
| Body work | node or member occurrence | allocation, copy, throw, unsafe |
| Call modality | occurrence and edge roll-up | loop, call kind, dispatch kind, conditional region |
| Boundary | node, group, or edge | source group, package crossing |
| API | edge or occurrence | extension member, adapter member |
| Integration | edge or occurrence | Aspire, AI, OpenTelemetry |
| Finding | any supported target | descriptor/key reference, priority, confidence |

Both catalogs are typed. Neither is a dictionary of display strings. Additive
semantic descriptor ids may be rendered by older consumers as unknown fields,
but producers emit only registered descriptors and valid target/value
combinations. L2 field bindings are separately discoverable presentation
contracts.

### Direct and rolled-up values

Every characteristic states whether it is:

- **direct** evidence about its target;
- **aggregated** from occurrences;
- **rolled up** from contained subjects; or
- **derived** by a named cross-producer composition.

The distinction reaches structured output. A package-level allocation count
must not look like a package directly allocated memory when it is the sum or
set of member observations.

Aggregation is descriptor-owned. Supported policies may include:

- `any` or `all` for flags;
- count of distinct occurrences;
- count of distinct subjects;
- sum or maximum for compatible numeric units;
- ordered or distinct set;
- strongest typed disposition; and
- no roll-up.

There is no generic "merge non-empty values" rule. Fan-in counts members while
fan-out currently counts call sites; allocation counts and Finding priority
have different units again. A descriptor that does not define a sound roll-up
cannot appear on an aggregate type or package node.

### Calls as the first occurrence-backed catalog

The first edge-characteristic slice uses call evidence. The current projection
and focus overlay expose the required distinction:

- logical edge: caller member -> callee member;
- physical occurrence: one retained call site with call kind, IL offset,
  operand token, loop state, and derived dispatch kind;
- edge aggregates: call-site multiplicity, any-in-loop, distinct call kinds,
  and distinct dispatch kinds when physical occurrence evidence is complete;
  and
- focus source occurrence: the existing source-fact overlay over those physical
  coordinates.

Loop is stored as typed occurrence evidence and an edge aggregate, not as
`CallGraphEdge` label text. The CLI derives its existing `loop`/`loop call`
label from the typed edge state. Evidence-free compatibility trees may retain
their legacy analysis hint, but product-built physical edges do not depend on
it. The edge's primary `call` relationship remains structured and
non-optional.

Descriptive dispatch kind and correctness-bearing dispatch completeness are
separate. A selected characteristic may distinguish direct, virtual, interface,
delegate, or indirect modality. An unresolved runtime target remains an
always-present occurrence disposition and traversal limit whether or not that
characteristic is selected.

The current descriptive classifier distinguishes direct, virtual, function
pointer, virtual function pointer, and indirect sites from retained
`DirectCall` evidence. Interface and delegate refinements remain deferred until
their owning Analysis producer supplies typed evidence; display-name heuristics
are not substituted.

## Correctness and completeness

Some graph state is necessary to interpret absence and must never depend on a
selected characteristic:

- focus and seed roles;
- external, bodiless, truncated, and depth-limited boundaries;
- unresolved virtual or indirect dispatch;
- incomplete or indeterminate correspondence;
- analysis and acquisition failures;
- node, depth, path, occurrence, and byte budgets; and
- whether a query may support an absence claim.

These belong to topology, occurrence disposition, limits, or failures.
Renderers may choose compact chrome, but structured output and focused human
output must preserve them. Selecting no characteristics cannot transform an
incomplete graph into a complete-looking graph.

Positive evidence survives unrelated limits. Empty output supports absence
only when every producer and traversal needed by the request reports complete.
This follows the current call-cycle contract in
`AnnotatedCallGraphCycleInspection`.

Partial workspace results remain useful when every rejected or failed
participant stays beside the healthy results. The graph projection must either
carry those scoped failures or fail the graph operation. It must not omit a
participant and return a success-shaped smaller graph.

## Projection and traversal

Five axes remain separate:

| Axis | Question |
| --- | --- |
| Workspace scope | Which binding-consistent participants may contribute? |
| Mode | One privileged seed, several peer seeds, or an induced input set? |
| Relationship set | Which typed relationships may be traversed or composed? |
| Subject lens | At which subject kinds are nodes retained, grouped, or rolled up? |
| Characteristics | Which optional descriptive layers are produced and rendered? |

Output format is a sixth, presentation-only axis.

The axes are independently selectable, not a promise that their Cartesian
product is valid. Relationship descriptors constrain admitted subject kinds,
available roll-ups, required producers, and supported lenses. Discovery exposes
only valid combinations, and an unsupported combination fails with guidance
rather than dropping an axis.

### Seeded and induced modes

[Inspection-graph modes](inspection-graph-modes.md) and issue #4133 own mode
semantics:

- single-seed mode asks what surrounds one member, type, assembly, or realized
  package;
- peer-seed mode asks how several named anchors connect; and
- induced-set mode asks what admitted graph an input set contains without
  inventing a focus.

A seed is not merely workspace scope. A package seed may remain the focus
package endpoint in a package lens, while a call contribution expands through
typed package-owned members and retains member-to-member call semantics.

No mode chooses a subject lens, relationship set, or characteristic selection
automatically. The same relationship, identity, direction, limit, and failure
contracts apply in every mode.

`InspectionGraphNeighborhoodRequest` is the first composed request over these
orthogonal axes. It currently requires one seed or two or more equal peer
seeds, one or more typed relationship descriptors, semantic traversal
direction, and a finite maximum edge depth.
The resulting document retains both its `ModeRequest` and
`NeighborhoodRequest`; a consumer never has to infer selection or bounds from
the surviving topology.

`InspectionGraphInducedSetRequest` is the corresponding composed request for
explicit induction. It retains one or more distinct typed subjects, one or more
distinct relationship descriptors, and its admission rule. The resulting
document retains both its `ModeRequest` and `InducedSetRequest`, contains no
seed bindings, keeps every explicit input as a node or group, and records the
finite input count as `queries.induced-subject-bound`.
Construction revalidates that every retained occurrence is admitted on both
semantic roles and requires exactly one global subject-bound diagnostic whose
count equals the request. `Document_RetainsExplicitInducedSetRequestWithoutSeeds`
and `Document_RejectsExplicitOccurrenceOutsideSubjectClosure` gate those
envelope invariants.

Relationship producers may retain a stricter typed breadth budget alongside
that shared request. The bounded call neighborhood records
`call.traversal-node-bound` in addition to
`queries.neighborhood-depth-bound`, because Analysis enforces its node budget
while building the cross-library callee tree. Hitting either bound remains
visible through call traversal incompleteness; it does not erase the member
seed or its physical evidence. Nonzero catalog correspondence counts likewise
remain a typed `call.correspondence-incomplete` limit rather than a
success-shaped empty graph. `CrossLibraryCalleeNeighborhood_*` gates these
call-specific compositions.

The Integration implementation validates catalog membership before producer
execution. Its relationship set drives the deterministic query-registry plan,
including opportunity's Integration and extension prerequisites for
fulfillment reconciliation. Projection begins through the selected
descriptor's exact edge, original-occurrence, or typed owned-subject admission
for each seed, then walks logical endpoints for the remaining hops. Incoming
traversal changes which endpoint is followed, never the stored edge or
occurrence direction. Peer projection is one deterministic multi-source walk:
every peer begins at depth zero, reached topology is the union of their bounded
neighborhoods, and shared edges and occurrences retain one identity. It does
not fabricate a primary seed or require every peer to be connected.

Explicit-set projection instead tests both semantic endpoint closures for each
selected physical occurrence. It does not walk from an admitted endpoint.
Logical edges survive when they retain at least one admitted receipt, and their
occurrence lists are rebuilt from only those receipts. If that filters a
multi-occurrence edge, characteristics that directly described the unfiltered
edge are omitted rather than becoming success-shaped partial aggregates.

Acquisition-bound member subjects retain their structured
`MetadataTypeDefinitionName` declaring type beside the member anchor. Type-to-
member ownership compares that typed identity, not the anchor's rendered
generic spelling. `ProjectionOwnership_UsesStructuredGenericDeclaringType`
gates the generic-type case.

Projection assigns new dense document-local ids while retaining semantic
subjects, relationship descriptors, occurrence evidence and occurrence
identity. Failures from requested relationship producers and their required
composition prerequisites remain visible even when their target is outside
healthy reached topology, subject to producer-specific admission policies.
Explicit Integration induced sets apply the out-of-context `BindingMissing`
policy owned by [Integrations](integrations.md) before failure-target retention.
A typed `queries.neighborhood-depth-bound` limit records the requested bound,
including depth zero. An admissible owner-issued seed remains bound even when
selected producers emit no relationship evidence.
For peer requests, the same bound is targeted at every equal seed so no peer's
completeness is inferred from another's topology.
`Execute_BoundsMixedRelationshipNeighborhoodByDepth`,
`Execute_ZeroDepthRetainsSeedWithoutEdges`,
`Execute_ZeroDepthRetainsAdmissibleSeedWithoutSelectedEvidence`,
`Execute_PeerNeighborhoodConnectsEqualSeeds`,
`Execute_ZeroDepthPeerNeighborhoodRetainsEverySeed`,
`Execute_PeerNeighborhoodRetainsAdmissibleDisconnectedSeed`,
`Execute_OpportunityNeighborhoodPreservesFulfillmentSuppression`,
`Execute_NeighborhoodRetainsSelectedProducerFailures`,
`Execute_OpportunityNeighborhoodRetainsPrerequisiteFailures`, and
`Execute_RejectsForeignRelationshipBeforeProducerExecution` gate these
contracts.
`Execute_ExplicitPackageSetInducesOnlyInternalEvidence`,
`Execute_ExplicitInducedSetRequiresBothEndpointClosures`,
`Execute_ModeOnlyExplicitSubjectsRejectsBeforeProducers`,
`Execute_RejectsExplicitSubjectOutsideWorkspaceWithGuidance`,
`Execute_RejectsUndeclaredInScopeTypeBeforeProducerExecution`,
`Execute_RejectsUndeclaredInScopeMemberBeforeProducerExecution`,
`Execute_ReportsMemberPreflightDecodeFailureBeforeProducers`,
`Execute_ReportsTypeDeclarationRejectionBeforeProducers`,
`Execute_ExplicitInducedSetRetainsIsolatedInput`,
`Execute_ExplicitInducedSetRetainsDeclaredMemberInput`,
`Execute_ExplicitInducedSetRetainsOnlyInClosureFailures`, and
`Execute_ExplicitSubjectCountDoesNotMultiplyProducerDemand` gate explicit-set
projection.

### Type outward

A type-outward query may:

1. resolve the selected type in one workspace group;
2. select members or metadata relationships associated with that type;
3. compose extension, call, and integration evidence as requested;
4. retain physical member/metadata/Finding occurrences;
5. roll terminal subjects up to realized packages; and
6. render the type and package subjects together.

For the locked `IChatClient` demo, the visible result may be:

```text
focus type: IChatClient

OpenAI SDK
  -> IChatClient
       api: AsIChatClient
       integration: AI
       package: Microsoft.Extensions.AI.OpenAI
```

The query reaches the package by traversing the evidence-backed adapter and
ownership relationships. It is not a fabricated call from the interface to the
package.

### Package inward

A package-inward query starts with realized package subjects, traverses the
same admitted relationships in either query direction, and retains the types
or APIs that explain the package connection.

Reversing the query does not mint reverse evidence. It changes root selection,
traversal, and roll-up:

```text
focus package: Microsoft.Extensions.AI.OpenAI

OpenAI SDK
  -> IChatClient
       api: AsIChatClient
       integration: AI
       package: Microsoft.Extensions.AI.OpenAI
```

Whether the semantic edge points type-to-provider or provider-to-type is owned
by the chosen relationship descriptor. Both views refer to the same
occurrence, and a renderer may orient layout around the selected focus without
changing that descriptor.

### Mixed lenses

A mixed graph can retain member nodes where call detail matters, type nodes
where APIs converge, and package nodes where ecosystem ownership matters.
Roll-up is not all-or-nothing. A projection may keep the focused member path
expanded while collapsing distant terminal subjects to packages.

Every collapse retains each occurrence's original endpoint subjects and
evidence. Expanding a collapsed node is a new bounded projection over retained
or reacquired evidence, not string parsing.

## Applying in-flight analysis investments

The graph does not absorb analysis algorithms. It gives their typed results a
shared carrier. The PRs in this section are open and are not part of the current
product head. The examples describe how their proposed contracts could be
adopted if they land; each owning PR remains authoritative for its final
behavior.

### Async sibling calls (#4091)

PR #4091 proposes `sync-call-in-async` analysis with exact
`analysis.call-site` Finding provenance plus a signature-compatible async
sibling candidate. If that contract lands, Research can project it as a
specifically named `analysis.async-sibling-opportunity` relationship:

- the observed synchronous call remains a `call`;
- the candidate relationship remains an opportunity;
- the physical call site and Finding key remain attached;
- type/package lenses may roll the opportunity up when the descriptor permits.

The graph must not present the async sibling as a call that already occurred.

### Static performance triage (#4121)

PR #4121 proposes separate priority and confidence values for Performance
Triage. If that contract lands, it can contribute characteristics and Finding
references over members and call occurrences. Static evidence remains static
evidence: rolling a concern up to a type or package does not imply runtime heat,
bytes, or impact.

### Structural clone comparison (#4114)

PR #4114 proposes an exact structural comparison between members in one
retained image. If that contract lands, it can contribute a typed
`implementation.structural-match` relationship retaining its disposition,
correspondence, and witness. A future comparison that establishes the same
relation across artifacts could support a package-to-package clone view as a
roll-up over those member relationships, not package equality or provenance
inference.

### Existing Integrations and touchpoints

Workspace Integrations (#3629) and reference touchpoints (#3630) remain separate
queries and evidence producers. Their results can share the graph envelope and
participate in a composed view without being reimplemented as call traversal.

Cross-library call resolution (#3632) improves the depth and completeness of
`call` relationships. It does not resolve metadata, integration, or opportunity
relationships, and those relationships do not cover for a missing call body.

## Query planning and execution

The inspection workspace orchestrates graph production:

```text
inspection space = contexts x requested queries x execution policy
```

Graph selection lowers into ordinary typed query demand. A requested L2 field
binding selects its L1 semantic descriptor and producer prerequisite; that
query definition declares its maximum transitive cost and capabilities.
Selecting `edge.loop` may reuse already-acquired call occurrences; selecting
structural matches or performance Findings may require additional Analysis
features. Unselected layers do not execute merely because a renderer could show
them.

The baseline remains deterministic sequential execution. The graph contract
requires no threads, tasks, shared mutation, or concurrent collection and must
work in single-threaded browser/Wasm. A future executor may run independent
producers concurrently, but it must deliver the same ordered typed inputs to
the projection and produce the same document.

Queries may compose relationships within one assembly context group. A
cross-group comparison is an explicit query that binds each side independently
and produces a new typed relation with correspondence provenance. No graph
builder infers a relation because two subjects in different groups have equal
fields or labels.

## Layer ownership

| Layer | Responsibility |
| --- | --- |
| Packages / Services | Realized package identity, dependency edges, acquisition provenance, and package-to-artifact ownership |
| Metadata | Metadata references, extension/implementation facts, integration observations, metadata-native identities and coordinates |
| Analysis | Call occurrences, IL/body characteristics, performance evidence, structural comparisons |
| CallGraph | Member call topology, logical call-edge collapse, call boundaries, cycles, deterministic call rows |
| Findings | Domain-free observation identity, inspection outcomes, and correspondence contracts |
| Research / ResearchQueries | Cross-producer joins, opportunity/overlay composition, evidence-preserving roll-ups |
| Queries (L1) | Workspace lifetime, demand, costs, typed graph request/result, sequential orchestration |
| Sections (L2) | Discoverable graph section and node/edge/occurrence field schemas |
| CLI/browser (L3/host) | Command and lens selection, spelling, interaction, output format |
| Markout | Generic graph lowering and grammar-safe rendering only |

`ILInspector.CallGraph` remains call-specific. The inspection graph envelope
belongs above producer-specific projections, normally in L1 or its optional
Research companion. It must not force Metadata to depend on Analysis or make
CallGraph understand packages, integrations, Findings, or output formats.

## Output contract

### Structured first

The stored contract is nodes, groups, typed edges, occurrences,
characteristics, limits, and failures. Mermaid edge labels and node text are
projections of that contract.

A renderer may combine selected values:

```text
AsIChatClient | Integration: AI
calls | loop | package: Microsoft.Extensions.AI.OpenAI
```

No consumer may parse those strings to recover relationship kind, package
identity, Finding identity, or occurrence count.

### Rows and counts

The default graph row remains one logical directed edge, matching
[Output shapes](output-shapes.md). Filtering retains stable edge row numbers.
`--count` counts logical relationships, not rendered nodes or physical
occurrences.

An occurrence-detail lens may expose one row per occurrence. It is a distinct
declared shape and count unit; selecting occurrence fields must not silently
change an edge table into an occurrence table.

When that lens lands, [Output shapes](output-shapes.md) must be updated in the
same change so it remains the authoritative owner of graph row and count units.

### Discovery and field selection

Node, group, edge, and occurrence characteristics require subject-qualified
discovery. Structural field discovery must say:

- target kind;
- descriptor id and aliases;
- value shape;
- the bound producer query whose L1 definition declares cost and capabilities;
- and the descriptor's declared aggregation support for the requested lens.

Structural discovery does not run the bound producer and does not claim that
the field has data. Effective field discovery, requested explicitly through
`--effective` or an equivalent host gesture, may additionally report whether
the field has data for the effective target. It spends the bound producer's
declared probe budget and follows the cache rules in
[Section model](section-model.md). L2 does not establish effectiveness on the
query's behalf.

The command syntax may use separate selectors such as `--edge-fields`, or a
unified qualified grammar such as `edge.kind,node.alloc`. The syntax is not
settled here. An unqualified collision must fail with guidance rather than
selecting one target kind.

### Progressive disclosure

- Topology, primary relationship kind, focus, and correctness boundaries are
  always part of the typed graph.
- Pure call-graph renderers may omit the repeated word `calls` from every edge
  label because the section supplies that context.
- Mixed-relation graphs must disclose relation kind wherever omission would
  make two semantics indistinguishable.
- Optional body, performance, integration, Finding, and provenance
  characteristics require selection or an authored focused preset.
- Network, source content, exhaustive traversal, and expensive analysis remain
  explicitly capability-gated.

### Presentation safety

Subjects and evidence retain exact producer text through identity, matching,
and analysis. They become inert at the last shared structural boundary before
presentation. Markout or the host renderer then escapes Markdown, Mermaid,
JSON, TSV, HTML, and other output grammars. L1 never stores grammar-ready edge
labels from untrusted artifact text.

## Locked-demo interpretation

The merged #4127 owner document locks one `IChatClient` dual-lens demo:

| Subject | Topology and evidence |
| --- | --- |
| `IChatClient` | type focus and hub identity from `Microsoft.Extensions.AI.Abstractions` |
| OpenAI and Bedrock | `AsIChatClient` adapter/integration occurrences, provider SDK subjects, and package ownership |
| Azure OpenAI | metadata reference to OpenAI plus an explicit MEAI integration opportunity, never a fabricated adapter call |

The type-outward and package-inward readings share those occurrences and keep
their semantic edge directions. The pinned packages and normative target
Mermaid in
[the locked demo](../workflows/discovery/aspire-ai-package-graph.md) are the
acceptance owner.

`InspectionGraphIntegrationsQuery` implements the L1 locked-demo projection.
It contributes `api.extension` from each `AsIChatClient` member to its exact
acquired SDK receiver type, `integration.observed` from that same member to the
exact acquired `IChatClient`, Azure's direct `metadata.reference` to OpenAI,
and Azure's explicit `integration.opportunity` to `IChatClient`. Package groups
come from `InspectionGraphPackageBoundary`; no package or endpoint join parses
a label.

The composer reconciles raw per-assembly opportunities with observed adapters
elsewhere in the same context. The same acquired SDK type is fulfilled only
when one exact member supplies both its extension and integration occurrences.
This suppresses the OpenAI and Bedrock raw gaps without suppressing Azure or a
same-spelled type from another acquisition.
`InspectionGraphIntegrationsQueryTests.Execute_ProjectsLockedIChatClientEvidenceAcrossPackageGroups`,
`PackageAndTypeModesShareSemanticIntegrationOccurrences`, and
`Execute_DoesNotJoinAmbiguousMatchingAssemblyIdentities` gate those claims.

Aspire hosting package webs, an `AddOpenAI` seed graph, and a two-plane AppHost
and application view remain related but unlocked demos. They may later use the
same envelope, but they are not acceptance fixtures for this design.

## Delivery slices

Each slice should be independently landable and name which graph mode and
subject lenses it advances.

1. **Document and descriptor catalog.** Add the typed envelope, target kinds,
   relationship descriptors, characteristic descriptors, limits, and failures.
   Adapt the existing member call graph without changing default output.
2. **Call occurrences and edge characteristics.** Retain every call site behind
   logical call edges; move loop out of label storage; add call kind, dispatch,
   and multiplicity to the structured L1 projection. L2 selector and
   occurrence-table/JSON bindings remain a separately gated presentation step;
   they must not encode these values back into edge labels.
3. **Typed groups and package boundary.** Project assembly/package ownership
   from workspace provenance and realized coordinates; render the same package
   as a group or node without string-derived identity. Implemented by
   `InspectionGraphPackageBoundary`; presentation lowering and integration
   composition remain later slices.
4. **Type/package integration projection.** Compose extension and Integrations
   evidence into the locked `IChatClient` type-outward and package-inward views
   demonstrated by #4127. Implemented at L1 by
   `InspectionGraphIntegrationsQuery`; seed selection and presentation lowering
   remain later slices.
5. **Findings and analysis adoption.** Add explicit adapters for #4091, #4121,
   and #4114 after their contracts land, without changing their native producer
   semantics.
6. **Seed and set composition.** `InspectionGraphModeRequest` now makes
   single-seed, peer-seed, and induced-set intent explicit.
   `CallGraphInspectionGraphAdapter` preserves the current member seed;
   `InspectionGraphPackageBoundary` and `InspectionGraphIntegrationsQuery`
   bind type, assembly, and package subjects to exact nodes or groups; peers
   retain equal roles; and request-free workspace projection declares its
   workspace-participant induced-set rule. Relationship descriptors now own
   direct edge, original occurrence, and owned-subject seed admission, and
   preserve each admission's semantic role for request planning.
   `InspectionGraphNeighborhoodRequest` now drives finite single-seed
   Integration traversal and producer selection. Peer connecting
   neighborhoods, explicit-subject induced sets, and presentation lowering
   remain.

## Required implementation gates

Implementation is not complete until focused gates prove:

- two physical calls between the same members produce one logical call edge and
  two retained occurrences;
- several original member endpoint pairs can collapse onto one type/package
  edge while every occurrence retains its original source and target subjects;
- a call and another relationship between the same subjects remain distinct
  edges;
- an edge rejects an occurrence with a different relationship, a reversed
  endpoint projection, or an endpoint that does not project to its node;
- a composed relationship uses a derived occurrence that cites native
  occurrence receipts rather than attaching foreign-relation occurrences;
- type-to-package and package-to-type projections retain the same evidence
  occurrence and semantic relationship direction;
- two versions or acquisition contexts of the same package never join by
  display name;
- aggregate characteristics disclose their derivation and reject unsupported
  roll-ups;
- selecting no optional characteristics preserves focus, boundaries, limits,
  and failures;
- a failed workspace participant remains visible beside healthy graph evidence;
- field selection controls producer demand and an unselected expensive producer
  does not execute;
- structural field discovery does not execute bound producers, while explicit
  effective discovery observes their declared probe budget;
- sequential and any future concurrent executor produce structurally identical
  deterministic documents;
- edge rows and occurrence rows retain their distinct count and filtering
  units;
- Markdown, Mermaid, table, JSON, and browser consumers derive from the same
  typed document without reparsing labels; and
- the locked `IChatClient` pin set preserves OpenAI and Bedrock adapter
  occurrences, the Azure-to-OpenAI metadata reference, and the Azure MEAI
  opportunity without fabricating a call edge.

## Deferred decisions

These choices do not block the architecture:

- command entry point (`graph`, an existing command lens, or both);
- exact node/edge/occurrence selector syntax;
- concrete project and CLR type names for the L1 envelope;
- the initial stable descriptor-id vocabulary;
- whether the first package-web edge's primary relationship is a dedicated
  integration relation or an extension relation carrying integration
  characteristics;
- portable type-subject representation beyond the currencies already owned by
  Metadata; and
- path-target characteristics beyond existing typed witness payloads.

Each decision must preserve the contracts above. In particular, command syntax
cannot repair an untyped model, and a convenient Mermaid label cannot become
relationship identity.
