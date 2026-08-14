# Call graph characteristics

How the current member-centric call graph adapts to the characteristic plane in
the [inspection graph document](inspection-graph-document.md). This document is
call-specific: it does not define a second generic graph envelope and does not
turn integrations, metadata references, or opportunities into call
annotations.

Tracking: [#4139](https://github.com/richlander/dotnet-inspect/issues/4139).

The physical call-site retention and L1 occurrence/edge catalog under
[Current substrate](#current-substrate) are current. L2 discovery, selectors,
and occurrence-table/JSON bindings remain design targets until an
implementation names their gates below.

| Owns | Does not own |
| --- | --- |
| Mapping call topology, occurrences, node signals, and loop state into inspection-graph descriptors | The heterogeneous inspection-graph envelope |
| Call-specific aggregation and migration from label storage | Seed-centric versus ad hoc construction |
| Call node/edge/occurrence presentation bindings | Integration, metadata, opportunity, or package-ownership relationships |

Related:

- [Call-graph projection](call-graph-projection.md) owns the current call
  topology.
- [Inspection-graph modes](inspection-graph-modes.md) owns member, type,
  assembly, and package seeds plus peer-seed and induced-set requests.
- [Graph signal annotations](graph-signal-annotations.md) owns the current
  node `--fields` vocabulary.
- [Inspection layers](inspection-layers.md) owns L1/L2/L3 boundaries.
- [Section model](section-model.md) owns structural and effective discovery.

## Current substrate

| Current piece | Current contract | Migration role |
| --- | --- | --- |
| `CallGraphProjection.Nodes` | Member identity, boundary kind, `CallTreePerf`, and graph evidence | Inspection-graph member nodes |
| `CallGraphProjection.Edges` | One logical caller-to-callee row collapsed by `(From, To)`, with retained physical call-site ids and typed any-in-loop state | Edge with primary `call` relationship |
| `CallGraphProjection.CallSites` | Every physical `DirectCall` supporting a projected product edge, deduplicated across caller and callee walks | Document-wide occurrence plane |
| `CallGraphInspectionGraphAdapter` | Physical `call.site` receipts, direct occurrence values, and edge aggregates; evidence-free trees retain an explicit transitional limit | Current L1 call adapter |
| `AnnotatedCallGraphOccurrence` | Retained focus call site joined to an edge row and source fact | Partial occurrence adapter, not document-wide retention |
| `CallGraphSectionAdapter --fields` | Node signal selection and label projection | L2 bindings over semantic node descriptors |
| Markout `GraphEdge.Label` | Renderer slot | Projection target, never semantic storage |

`CallTreeNode.ParentEdgeCallSites` carries every physical `DirectCall`
supporting one tree edge. `CallGraphProjection` retains those sites behind
logical edges and deduplicates a site observed by both traversal directions.
The L1 adapter publishes call kind, IL offset, operand token, loop state, and
derived dispatch kind on occurrences, plus multiplicity, any-in-loop, distinct
call kinds, and distinct dispatch kinds on edges.

`AnnotatedCallGraphOccurrence` continues to carry the focus-member source join.
Evidence-free trees remain accepted for browser and synthetic callers; the
generic adapter preserves their logical row and explicit physical-evidence
limit rather than inventing a call site.

## Call topology is not a characteristic

The edge's primary `call` relationship is mandatory topology. Selecting or
hiding fields cannot change caller, callee, direction, identity, boundary
state, or completeness.

Call kind and dispatch kind are modalities of a call occurrence. They may be
selectable characteristics because the edge remains a call when those values
are omitted from presentation. Integration, extension, metadata-reference, and
opportunity are different relationship families. If one shares endpoints with
a call, it remains a separate logical edge with its own occurrences.

## Target call catalog

### Node descriptors

| Descriptor family | Existing source | Aggregation |
| --- | --- | --- |
| Scale | `CallTreePerf` fan-in, fan-out, and depth | Preserve each source unit; no generic numeric merge |
| Node loop context | `CallTreePerf.InLoop` | Preserve current `Loop`/`InLoop`/`Looping` node-field semantics |
| Body work | `MethodSignals` allocation, copy, unsafe, reflection, and exception facts | Descriptor-specific |
| Boundary context | node kind, source assembly, workspace group, package ownership | Direct or declared roll-up |
| Findings | producer-owned Finding references | No implicit severity merge |

The current field aliases and value meanings remain owned by
[Graph signal annotations](graph-signal-annotations.md). Migration registers
semantic descriptors and binds existing `--fields` names to them; it does not
silently rename fields or change defaults.

The node loop descriptor is distinct from physical call-site loop state and the
edge-level `any in loop` aggregate. Existing `--fields Loop` aliases continue
to bind `CallTreePerf.InLoop` and preserve their selected node-label output.
Migrating `CallGraphEdge.LoopLabel` cannot replace or derive that node field.

### Occurrence descriptors

| Descriptor | Meaning |
| --- | --- |
| Call kind | Opcode-level call shape already carried by `DirectCall` |
| IL offset | Physical coordinate scoped to the caller body |
| Operand token | Physical metadata operand scoped to the caller image |
| Loop state | Whether that physical site is in a loop |
| Dispatch kind | Derived direct, virtual, interface, delegate, or indirect modality |

Occurrence identity remains physical body plus call-site storage identity. A
caller and callee walk observing the same site must not create two
occurrences.

### Edge aggregates

| Descriptor | Policy |
| --- | --- |
| Call-site multiplicity | Count distinct retained call occurrences |
| Any in loop | `any` over occurrence loop state |
| Call kinds | Ordered distinct set |
| Dispatch kinds | Ordered distinct set |
| Cross-library or cross-package boundary | Derived from typed endpoint ownership within one assembly context group |

The former `LoopLabel` storage has migrated to `AnyCallInLoop` plus physical
occurrence evidence. The CLI continues to render the same `loop`/`loop call`
label from that typed state; no consumer parses the label to recover the value.
An evidence-free compatibility edge may retain its legacy analysis hint.

A call edge never crosses assembly context groups. An explicit cross-group
comparison produces a separately typed comparison relationship with
correspondence provenance; it is not a call boundary characteristic.

## Selection and discovery

The default call graph remains as lean as today. Optional body, provenance,
modality, and Finding fields require selection or an authored focused preset.
Correctness boundaries remain present whether or not a field is selected.

Structural discovery lists target kind, descriptor, aliases, value shape,
bound query, and declared aggregation support without running producers.
Explicit effective discovery may probe whether a field has data under the
producer's declared budget. Node, edge, and occurrence names must be qualified
when aliases collide.

Mermaid labels, tree suffixes, table columns, JSON properties, and browser
annotations are projections of the same selected typed values. Hosts may choose
different layouts, but they must not invent different semantic catalogs.

## Relationship to inspection-graph modes and mixed graphs

Every graph mode uses the same call adapter when calls are admitted. A member
seed enters directly. A type, assembly, or package seed admits owned members
through a typed request before contributing member call evidence. Peer-seed and
induced-set requests use the same rule. Mode does not choose characteristics.

Package and type ownership can appear as groups, endpoint roll-ups, or boundary
characteristics according to the selected inspection-graph lens. A package
endpoint remains a typed package subject backed by retained member
occurrences; it is not a member node relabeled with package text.

The locked `IChatClient` dual-lens graph composes call evidence with extension,
integration, ownership, metadata-reference, and opportunity relationships.
Only its actual caller-to-callee edges use this call-specific catalog.

## Delivery

1. Register semantic descriptors for current node fields and bind existing
   aliases without changing default output.
2. Retain every call occurrence behind every logical call edge. **Current.**
3. Move loop state out of label storage and add L1 occurrence and edge
   characteristics. **Current.** L2 selectors and structured output bindings
   remain.
4. Project package/group boundary descriptors from workspace-owned provenance.
5. Let the inspection graph compose call edges with other relation adapters.

## Required gates

- existing node fields preserve values, aliases, and disclosure;
- existing `Loop`/`InLoop`/`Looping` node fields preserve
  `CallTreePerf.InLoop` independently of edge loop aggregation;
- two call sites between the same members produce one call edge and two
  occurrences;
- loop presentation is unchanged after the typed-value migration;
- selecting no optional fields preserves topology, limits, and failures;
- structural discovery does not execute call or analysis producers;
- edge and occurrence rows retain separate count units;
- no call edge or call boundary characteristic joins assembly context groups;
- an explicit cross-group comparison remains a non-call relationship; and
- an integration or metadata-reference occurrence cannot attach directly to a
  call edge.

## Non-goals

- A member-only generic graph model.
- Treating relationship kind as optional label text.
- Attaching ecosystem section text to every call edge.
- Adding a second formatter per output sink.
- Freezing command spelling or a serialized schema in this design.
