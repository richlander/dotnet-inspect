# Inspection graph document

How dotnet-inspect projects call, metadata, integration, Finding, and analysis
evidence into one typed graph that can move between members, types, assemblies,
and packages without turning every relationship into a call.

Related documents:

- [Call-graph projection](call-graph-projection.md) owns the current
  member-to-member call topology, identity, boundaries, and stable edge rows.
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
- [Graph signal annotations](graph-signal-annotations.md) describes the current
  node-only `--fields` mechanism this design generalizes.
- [Aspire and AI target demos](https://github.com/richlander/dotnet-inspect/pull/4127)
  supply the motivating type-to-package and package-to-type experiences.

## Status

This document is a proposed architecture for issue #4139. Requirements in this
document are design targets and are unverified until an implementation slice
names and adds its gates. Existing behavior is identified explicitly and names
its current owner or gate.

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
- Keep seed-centric and ad hoc graph modes orthogonal to subject lens,
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
- Representing the narrative `provisions resources for` edge from the target
  demos unless a producer owns evidence and semantics for it.

## Carrier and overlays

`AnnotatedSourceDocument` is a text carrier plus structural and fact overlays:

```text
fact -> target -> node -> spans -> text
```

The graph-shaped analogue is:

```text
characteristic -> target -> node | group | edge | occurrence
                                      |
                                      v
                            subject and evidence
```

The topology is the carrier. Nodes and logical edges are stable targets within
one document. Occurrences retain the physical evidence that supported a
logical edge. Characteristics describe those targets without becoming their
identity or being flattened into their display labels.

This refines the useful shorthand "arcs are to graphs what carets are to
annotated source." The visible arc is the reader's hook. The semantic analogue
of the source target is the stable edge or occurrence target behind that arc.
That distinction matters when two physical call sites collapse onto one
logical edge: there is one visible arc but two evidence-bearing occurrences.

## Conceptual document

The following shape is conceptual. Names may change when concrete types land.

```text
InspectionGraphDocument
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
    Evidence

  Characteristics[]
    Descriptor
    Target
    Value
    Derivation

  FocusNodeIds[]
  Limits[]
  Failures[]
```

The shape has five important properties:

1. Document-local ids are joins, not display values or portable subject
   identities.
2. Every node and group carries an owner-issued typed subject.
3. Every edge carries a typed relationship descriptor even when its renderer
   chooses not to print the descriptor.
4. Every logical edge can retain zero or more physical or derived occurrences.
5. Limits and failures remain separate from optional characteristics.

An edge with no occurrence is permitted only for an explicitly synthetic
relationship whose producer and derivation are part of the edge contract. It
must not look like observed call or metadata evidence. The first implementation
should require at least one occurrence for every non-synthetic edge.

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

## Relationships and occurrences

### Relationship descriptors

A relationship descriptor defines:

- a stable id;
- semantic direction and endpoint roles;
- admitted source and target subject kinds;
- the producer that owns the relation;
- whether the relation is observed, derived, or synthetic;
- the occurrence evidence contract;
- the occurrence identity and deduplication contract;
- allowed aggregation and roll-up behavior; and
- its default disclosure policy.

Illustrative relationship families include:

| Relationship | Typical endpoints | Owner |
| --- | --- | --- |
| `call` | member -> member | CallGraph |
| `metadata.reference` | assembly, type, or member -> assembly, type, or member | Metadata |
| `api.extension` | extension member or provider -> extended type | Metadata |
| `api.implements` | type -> interface type | Metadata |
| `integration.observed` | API/type/package -> API/type/package | Metadata |
| `integration.composed` | API/type/package -> API/type/package | Research |
| `integration.opportunity` | consumer API/type/package -> candidate API/type/package | Research |
| `analysis.async-sibling-opportunity` | call site or member -> candidate member | Research |
| `implementation.structural-match` | member -> member | Analysis |

These ids are examples, not a frozen catalog. The first catalog slice must
choose ids, endpoint constraints, and exactly one owner per descriptor before
code depends on them. A Research relationship may reference a Metadata- or
Analysis-owned Finding as evidence without sharing ownership of the graph
relationship.

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

Each relationship descriptor owns an occurrence-identity projection within one
document. Projection deduplicates repeated observations by that key before
assigning deterministic document-local occurrence ids. For `call`, the key is
the physical body and call-site storage identity: artifact or acquisition
identity as appropriate to the document lifetime, caller token, IL offset, and
operand token. Observing that call site from caller and callee walks cannot
create two occurrences. Two distinct IL offsets remain two occurrences even
when every other field and the logical edge are equal.

`AnnotatedCallGraphOccurrence` is the current worked example: two call sites
can share one stable edge row while retaining two IL offsets, operand tokens,
call kinds, loop flags, and source facts. The
`AnnotatedMemberDocument_ReusesCalleeLayerAndMapsEveryPhysicalCallSite` gate
pins that behavior.

## Characteristics

A characteristic is a typed description attached to a graph target. It is not
topology, identity, completeness, or preformatted label text.

### Descriptor contract

A characteristic descriptor defines:

- stable id and display title;
- layer or category;
- admitted targets: node, group, logical edge, or occurrence;
- typed value shape;
- producer and query prerequisites;
- direct versus derived meaning;
- aggregation policy for occurrence-to-edge and subject roll-up;
- default disclosure and cost; and
- structured-output field name.

Illustrative layers include:

| Layer | Target | Examples |
| --- | --- | --- |
| Scale | node | fan-in, fan-out, depth |
| Body work | node or member occurrence | allocation, copy, throw, unsafe |
| Call modality | occurrence and edge roll-up | loop, call kind, dispatch kind, conditional region |
| Boundary | node, group, or edge | source group, package crossing |
| API | edge or occurrence | extension member, adapter member |
| Integration | edge or occurrence | Aspire, AI, OpenTelemetry |
| Finding | any supported target | descriptor/key reference, priority, confidence |

The catalog is typed. It must not be a dictionary of display strings. Additive
descriptor ids may be rendered by older consumers as unknown fields, but
producers emit only registered descriptors and valid target/value combinations.

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

The first edge-characteristic slice should use call evidence because the
current graph already exposes the required distinction:

- logical edge: caller member -> callee member;
- physical occurrences: every retained call site;
- direct occurrence values: call kind, IL offset, loop state, dispatch kind;
- edge aggregates: call-site multiplicity, any-in-loop, distinct call kinds,
  and distinct dispatch kinds.

Loop is currently merged directly into `CallGraphEdge.LoopLabel`. Moving it
into the characteristic plane must preserve behavior while eliminating the
label as storage. The edge's primary `call` relationship remains structured
and non-optional.

Descriptive dispatch kind and correctness-bearing dispatch completeness are
separate. A selected characteristic may distinguish direct, virtual, interface,
delegate, or indirect modality. An unresolved runtime target remains an
always-present occurrence disposition and traversal limit whether or not that
characteristic is selected.

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

### Seed-centric and ad hoc modes

Issue #4133 owns mode semantics:

- seed-centric mode asks what surrounds one member or type;
- ad hoc mode asks what graph a set of inputs induces.

Neither mode chooses a package or type lens automatically. Neither mode
authorizes all characteristics. The same relationship and identity contracts
apply in both.

### Type outward

A type-outward query may:

1. resolve the selected type in one workspace group;
2. select members or metadata relationships associated with that type;
3. compose extension, call, and integration evidence as requested;
4. retain physical member/metadata/Finding occurrences;
5. roll terminal subjects up to realized packages; and
6. render the type and package subjects together.

For the Aspire hosting demo, the visible result may be:

```text
IDistributedApplicationBuilder
  -> Aspire.Hosting.OpenAI
       api: AddOpenAI
       integration: Aspire
```

The package edge is derived from extension/integration evidence. It is not a
fabricated call from the interface to the package.

### Package inward

A package-inward query starts with realized package subjects, traverses the
same admitted relationships in either query direction, and retains the types
or APIs that explain the package connection.

Reversing the query does not mint reverse evidence. It changes root selection,
traversal, and roll-up:

```text
focus package: Aspire.Hosting.OpenAI

IDistributedApplicationBuilder
  -> Aspire.Hosting.OpenAI
       api: AddOpenAI
       integration: Aspire
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

Every collapse retains its source subject and occurrence set. Expanding a
collapsed node is a new bounded projection over retained or reacquired evidence,
not string parsing.

## Applying analysis investments

The graph does not absorb analysis algorithms. It gives their typed results a
shared carrier.

### Async sibling calls (#4091)

The `sync-call-in-async` analysis produces exact `analysis.call-site` Finding
provenance plus a signature-compatible async sibling candidate. Research can
project that as a specifically named
`analysis.async-sibling-opportunity` relationship:

- the observed synchronous call remains a `call`;
- the candidate relationship remains an opportunity;
- the physical call site and Finding key remain attached;
- type/package lenses may roll the opportunity up when the descriptor permits.

The graph must not present the async sibling as a call that already occurred.

### Static performance triage (#4121)

Performance triage contributes characteristics and Finding references over
members and call occurrences. Priority and confidence remain distinct typed
values. Static evidence remains static evidence: rolling a concern up to a type
or package does not imply runtime heat, bytes, or impact.

### Structural clone comparison (#4114)

An exact structural comparison contributes a typed
`implementation.structural-match` relationship between member subjects,
retaining its disposition, correspondence, and witness. The #4114 slice
compares members in one retained image. A future comparison that establishes
the same relation across artifacts could support a package-to-package clone
view as a roll-up over those member relationships, not package equality or
provenance inference.

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

Graph selection lowers into ordinary typed query demand. A requested
characteristic declares its producer prerequisites and maximum transitive cost.
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
AddOpenAI | Integration: Aspire
calls | loop | package: Aspire.Hosting
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
discovery. A field catalog must say:

- target kind;
- descriptor id and aliases;
- value shape;
- producer cost and capabilities;
- aggregation availability for the active lens; and
- whether the field has data in the effective target.

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

## Target-demo interpretation

The #4127 demos exercise one envelope with different producer mixes:

| Demo | Topology and evidence |
| --- | --- |
| Aspire hosting package web | type/package subjects; extension and integration occurrences |
| `AddOpenAI` seed graph | member call topology with package groups and call characteristics |
| Multi-provider `IChatClient` | adapter/integration relationships, call evidence where present, reference evidence, and explicit opportunities |
| Two-plane AppHost/app view | composition of the preceding graphs; narrative edges excluded until modeled |

The demos may share one renderer and one document envelope. They do not share
one relationship kind or pretend that every endpoint is a member.

## Delivery slices

Each slice should be independently landable and name which graph mode and
subject lenses it advances.

1. **Document and descriptor catalog.** Add the typed envelope, target kinds,
   relationship descriptors, characteristic descriptors, limits, and failures.
   Adapt the existing member call graph without changing default output.
2. **Call occurrences and edge characteristics.** Retain every call site behind
   logical call edges; move loop out of label storage; add call kind, dispatch,
   and multiplicity with structured table/JSON projection.
3. **Typed groups and package boundary.** Project assembly/package ownership
   from workspace provenance and realized coordinates; render the same package
   as a group or node without string-derived identity.
4. **Type/package integration projection.** Compose extension and Integrations
   evidence into the type-outward and package-inward views demonstrated by
   #4127.
5. **Findings and analysis adoption.** Add explicit adapters for #4091, #4121,
   and #4114 without changing their native producer semantics.
6. **Ad hoc composition.** Apply the same envelope to #4133 multi-input mode;
   preserve seed-centric defaults and bounds.

## Required implementation gates

Implementation is not complete until focused gates prove:

- two physical calls between the same members produce one logical call edge and
  two retained occurrences;
- a call and another relationship between the same subjects remain distinct
  edges;
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
- sequential and any future concurrent executor produce structurally identical
  deterministic documents;
- edge rows and occurrence rows retain their distinct count and filtering
  units;
- Markdown, Mermaid, table, JSON, and browser consumers derive from the same
  typed document without reparsing labels; and
- the Aspire package-web and multi-provider fixtures contain no fabricated
  call edge.

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
