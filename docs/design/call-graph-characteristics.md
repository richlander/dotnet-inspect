# Call graph characteristics

How the current member-centric call graph adapts to the characteristic plane in
the [inspection graph document](inspection-graph-document.md). This document is
call-specific: it does not define a second generic graph envelope and does not
turn integrations, metadata references, or opportunities into call
annotations.

Tracking: [#4139](https://github.com/richlander/dotnet-inspect/issues/4139).

Only the behavior under [Current substrate](#current-substrate) is current.
The descriptor migration and catalogs are design targets and remain unverified
until an implementation names the gates below.

| Owns | Does not own |
| --- | --- |
| Mapping call topology, occurrences, node signals, and loop state into inspection-graph descriptors | The heterogeneous inspection-graph envelope |
| Call-specific aggregation and migration from label storage | Seed-centric versus ad hoc construction |
| Call node/edge/occurrence presentation bindings | Integration, metadata, opportunity, or package-ownership relationships |

Related:

- [Call-graph projection](call-graph-projection.md) owns the current call
  topology.
- [Call-graph modes](call-graph-modes.md) owns seed-centric and ad hoc call
  construction.
- [Graph signal annotations](graph-signal-annotations.md) owns the current
  node `--fields` vocabulary.
- [Inspection layers](inspection-layers.md) owns L1/L2/L3 boundaries.
- [Section model](section-model.md) owns structural and effective discovery.

## Current substrate

| Current piece | Current contract | Migration role |
| --- | --- | --- |
| `CallGraphProjection.Nodes` | Member identity, boundary kind, `CallTreePerf`, and graph evidence | Inspection-graph member nodes |
| `CallGraphProjection.Edges` | One logical caller-to-callee row collapsed by `(From, To)` | Edge with primary `call` relationship |
| `CallGraphEdge.LoopLabel` | Any collapsed site was in a loop, stored as display text | Legacy source for an aggregated loop descriptor |
| `AnnotatedCallGraphOccurrence` | Retained focus call site joined to an edge row and source fact | Partial occurrence adapter, not document-wide retention |
| `CallGraphSectionAdapter --fields` | Node signal selection and label projection | L2 bindings over semantic node descriptors |
| Markout `GraphEdge.Label` | Renderer slot | Projection target, never semantic storage |

`AnnotatedCallGraphOccurrence` currently carries module, caller token, IL
offset, operand token, `CallKind`, and loop state for focus call sites. It does
not retain occurrences for every projected edge and does not carry dispatch
kind. The first implementation slice must fill those gaps rather than describe
them as current behavior.

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
| Body work | `MethodSignals` allocation, copy, unsafe, reflection, and exception facts | Descriptor-specific |
| Boundary context | node kind, source assembly, workspace group, package ownership | Direct or declared roll-up |
| Findings | producer-owned Finding references | No implicit severity merge |

The current field aliases and value meanings remain owned by
[Graph signal annotations](graph-signal-annotations.md). Migration registers
semantic descriptors and binds existing `--fields` names to them; it does not
silently rename fields or change defaults.

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
| Cross-group or cross-package boundary | Derived from typed endpoint ownership |

`LoopLabel` migrates to the `any in loop` aggregate. Hosts may continue to
render the same label, but no consumer may parse that label to recover the
value.

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

## Relationship to call modes and mixed graphs

Seed-centric and ad hoc modes use the same call adapter. Mode chooses which
member call evidence enters the graph; it does not choose characteristics.

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
2. Retain every call occurrence behind every logical call edge.
3. Move loop state out of label storage and add occurrence and edge fields.
4. Project package/group boundary descriptors from workspace-owned provenance.
5. Let the inspection graph compose call edges with other relation adapters.

## Required gates

- existing node fields preserve values, aliases, and disclosure;
- two call sites between the same members produce one call edge and two
  occurrences;
- loop presentation is unchanged after the typed-value migration;
- selecting no optional fields preserves topology, limits, and failures;
- structural discovery does not execute call or analysis producers;
- edge and occurrence rows retain separate count units; and
- an integration or metadata-reference occurrence cannot attach directly to a
  call edge.

## Non-goals

- A member-only generic graph model.
- Treating relationship kind as optional label text.
- Attaching ecosystem section text to every call edge.
- Adding a second formatter per output sink.
- Freezing command spelling or a serialized schema in this design.
