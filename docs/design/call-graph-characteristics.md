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
| Mapping call topology, occurrences, node signals, loop state, and selected implementation-profile evidence into inspection-graph descriptors and joins | The heterogeneous inspection-graph envelope |
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
| `CallGraphProjection.Edges` | One logical caller-to-callee row collapsed by `(From, To)`, with retained physical call-site ids, typed any-in-loop state, and explicit physical-occurrence incompleteness | Edge with primary `call` relationship |
| `CallGraphProjection.CallSites` | Every physical `DirectCall` supporting a projected product edge, deduplicated across caller and callee walks | Document-wide occurrence plane |
| `CallGraphInspectionGraphAdapter` | Physical `call.site` receipts, direct occurrence values, and complete-evidence edge aggregates; fully or partially evidence-free edges retain an explicit transitional limit | Current L1 call adapter |
| `CallGraphImplementationDocument` | Unchanged inspection graph plus one Analysis receipt, raw implementation profiles and overload relationships, profile coverage, diagnostics, and explicit document-local joins | Current opt-in implementation-evidence plane |
| `CallGraphAsyncSiblingDocument` | Ordinary call topology plus separate Analysis-owned async-sibling relationship occurrences, raw opportunities, shared receipt and coverage, diagnostics, and explicit document-local joins | Current opt-in semantic-relationship plane |
| `AnnotatedCallGraphOccurrence` | Retained focus call site joined to an edge row and source fact | Partial occurrence adapter, not document-wide retention |
| `CallGraphSectionAdapter --fields` | Node signal selection and label projection | L2 bindings over semantic node descriptors |
| Markout `GraphEdge.Label` | Renderer slot | Projection target, never semantic storage |

`CallTreeNode.ParentEdgeCallSites` carries every physical `DirectCall`
supporting one tree edge. `CallGraphProjection` retains those sites behind
logical edges and deduplicates a site observed by both traversal directions.
The L1 adapter publishes call kind, IL offset, operand token, loop state, and
derived dispatch kind on occurrences, plus multiplicity, any-in-loop, distinct
call kinds, and distinct dispatch kinds on edges whose physical occurrence set
is complete. A detached-scope identity conflict retains each physical site
once, marks every later affected edge incomplete, and suppresses aggregates
over the resulting strict subset.

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

## Implementation profile plane

`CallGraphImplementationDocumentAdapter` composes an already-produced
`CallGraphProjection` and
`LibraryImplementationProfileAnalysisResult`. It does not request Analysis
features or choose evidence scope. The resulting
`CallGraphImplementationDocument` retains:

- the unchanged `InspectionGraphDocument` as authoritative topology;
- the exact Analysis receipt, population coverage, and diagnostics;
- every raw `MethodImplementationProfile` and
  `OverloadCallRelationship`; and
- one ordered document-local join for every retained profile and overload
  relationship.

A profile join independently maps its logical declared owner and physical
evidence method through `CallGraphProjection.FindNode`. Found, not-projected,
and ambiguous outcomes remain distinct. Several physical profiles can
therefore join one logical graph node without being summed, maximized, or
replaced by a representative body. An evidence method that has no graph node
remains retained with an explicit not-projected join.

An overload-relationship join first maps its exact caller and callee. It then
maps to their existing ordinary `call` edge and, when retained, the exact
physical call occurrence identified by evidence method, IL offset, and call
kind. It never creates another edge or occurrence. Missing physical occurrence
evidence does not erase an otherwise found logical edge.

`Create_RetainsPopulationAndJoinsExistingOverloadOccurrence` gates unchanged
topology, raw population retention, and edge/occurrence reuse.
`Create_PreservesPhysicalProfilesBehindOneLogicalAsyncNode` gates the
logical-owner/physical-body distinction.
`Create_SystemTextJsonRetainsEverySelectedSerializeProfile` gates the pinned
15-overload `System.Text.Json` corpus and scoped-coverage preservation.
`Create_RejectsUnrequestedImplementationProfiles` gates explicit selection.

This slice is intentionally construction-only. Issue #8244 owns request
selection, scope, and execution reuse; #8243 owns the overload-family graph
mode; #6980 owns envelope and transport adoption.

## Async-sibling relationship plane

`CallGraphAsyncSiblingDocumentAdapter` composes an already-produced
`CallGraphProjection` and `LibraryOptimizationAnalysisResult`. The ordinary
sync call remains a `call` edge and physical call occurrence. A successfully
joined `sync-call-in-async` opportunity adds a separate
`analysis.async-sibling-opportunity` edge from the authenticated async source
to the callable candidate, with one derived occurrence citing that exact call
occurrence.

The document retains every raw optimization opportunity, the Analysis receipt,
method-evidence coverage and diagnostics, plus explicit joins for source,
candidate, observed call, and emitted relationship. Missing candidate nodes or
physical call receipts do not admit new nodes or fabricate occurrences. The
relationship adapter consumes typed `AsyncSiblingOpportunityEvidence`; it does
not parse evidence or fix text and does not run sibling selection.

The contract and named gates live under
[Async-sibling opportunity composition](inspection-graph-document.md#async-sibling-opportunity-composition).
This L1 slice adds no presentation binding or default disclosure.

## Overload-family structural plane

`OverloadFamilyCallGraphStructuralQuery` composes the bounded equal-peer graph
issued by `OverloadFamilyCallGraphQuery` into one
`OverloadFamilyCallGraphStructuralDocument`. The document retains that graph
unchanged as authoritative topology and adds deterministic document-local
components and node facts. It does not select a primary, canonical, core, or
hot overload.

The family is the ordered peer-seed set already resolved by the overload-family
query. Direct call edges between family members form the family delegation
graph. Its strongly connected components are ordered by their earliest family
seed, and a component with no incoming edge from another family component is a
family-entry component. Recursive siblings therefore remain one entry
component rather than acquiring an arbitrary root from traversal order.

Each entry component is one origin. Reachability starts from every member of
that component and follows retained call topology. Every admitted node records
the ordered entry-component origins that reach it, and every family component
records the union of its members' origins. A family node with multiple origins
is a family-convergence point; a non-family node with multiple origins is an
implementation-convergence point. Several entries, several convergence
points, disconnected family members, no convergence point, and a family seed
that is also a convergence point are all valid.

For an instance-constructor family, a direct same-family edge contributes to
delegation only when at least one retained physical occurrence has
`CallKind.Call`. A same-type `CallKind.NewObject` edge remains in the
authoritative graph and in ordinary downstream topology but does not connect
family components or carry one constructor entry's origin into another
constructor. Base constructors have another declaring type, and `.cctor` has
another metadata name, so neither is a family member.

Structural confidence is complete only when the retained graph has no
unexplored traversal, unavailable physical occurrence, incomplete
correspondence, or recoverable Analysis-failure diagnostic. The declared depth
and node budgets do not by themselves downgrade confidence; the existing
Call Graph diagnostic does so when a budget, unresolved call, unavailable body,
or failure leaves an unexplored boundary. An incomplete document still retains
observed components, origins, and convergence as graph-relative candidates,
but it cannot support a complete entry or convergence absence claim.

Implementation-profile coverage remains a separate supporting plane. When a
consumer composes these facts with `CallGraphImplementationDocument`, its raw
profiles, coverage, and diagnostics remain visible and never determine or
upgrade structural status.

`Derive_DirectSiblingChainRetainsTransitiveEntryOrigin`,
`Derive_TwoEntriesConvergeOnFamilyOverload`,
`Derive_PublicEntriesConvergeOnlyOnPrivateHelper`,
`Derive_RecursiveFamilyCollapsesIntoOneEntryComponent`, and
`Derive_ConstructorNewObjectDoesNotCreateDelegation` gate the pathological
component, reachability, convergence, cycle, and constructor cases.
`Derive_IncompleteTraversalProducesCandidateFacts` gates the absence boundary.
`Derive_SystemTextJsonRetainsEntriesAndSharedHelperConvergence` gates the
pinned 15-overload `System.Text.Json` corpus.

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
An evidence-free compatibility edge may retain its legacy analysis hint. A
partially degraded edge preserves typed fallback loop state but not the legacy
text hint.

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
4. Correlate selected implementation profiles and overload relationships with
   graph nodes, edges, and occurrences without changing topology. **Current.**
5. Project package/group boundary descriptors from workspace-owned provenance.
6. Let the inspection graph compose call edges with other relation adapters.

## Required gates

- existing node fields preserve values, aliases, and disclosure;
- existing `Loop`/`InLoop`/`Looping` node fields preserve
  `CallTreePerf.InLoop` independently of edge loop aggregation;
- two call sites between the same members produce one call edge and two
  occurrences;
- implementation profiles retain separate logical-owner and physical-body
  joins;
- an overload relationship reuses its existing call edge and occurrence;
- incomplete scoped profile coverage remains visible beside healthy graph
  evidence;
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
