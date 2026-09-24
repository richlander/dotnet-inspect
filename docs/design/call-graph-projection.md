# Call-graph projection

How typed call-graph facts become one deterministic, format-neutral node/edge
graph that any host — `dotnet-inspect` today, the browser-Wasm prototype next —
can render without re-deriving graph semantics (issues #3120, #3291, #3280).

Related docs:

- [Library-body root paths](library-body-root-paths.md) — bounded shortest
  local witnesses from caller-supplied exact roots to destination MethodDefs
- [Inspection graph document](inspection-graph-document.md) — the typed
  multi-subject envelope that composes call topology with package, integration,
  Finding, and other relationship evidence
- [Call-graph characteristics](call-graph-characteristics.md) — the
  call-specific adapter from current nodes, edges, occurrences, and signals
- [External-focused call topology](external-focused-call-topology.md) —
  boundary-only and seeded shortest-connector views over an existing
  projection
- [Inspection-graph modes](inspection-graph-modes.md) — member, type, assembly,
  and package seeds plus peer-seed and induced-set requests
- [Graph signal annotations](graph-signal-annotations.md) — the per-node
  perf/kind-of-work cues the CLI projects onto the same call trees
- [Output shapes](output-shapes.md) — the projection/shape model the CLI uses

## Layering

```text
ILInspector.Analysis            typed CallTreeNode facts + bounded traversal
        ↓ CallTreeNode roots
ILInspector.CallGraph           host-neutral projection → nodes + edges
        ↓ CallGraphProjection (no format vocabulary)
dotnet-inspect        Browser Wasm
  ↓ CallGraphSectionAdapter        ↓ its own renderer
Markout Graph → tree | edge table | Mermaid
```

`ILInspector.Analysis` stays presentation-free: it owns the graph evidence and
the bounded traversal (`LibraryCallGraphAnalysisResult.BuildCallerTree` /
`BuildCallTree`) plus exact local root-to-destination witnesses over the same
focused result (`LibraryBodyRootPathAnalysis.FindShortestPaths`). For one fixed
catalog population, `CatalogCallGraphScope.Census` additionally publishes every
exact declared member and resolved physical `call`, `callvirt`, and `newobj`
occurrence without traversal bounds. The census retains its catalog generation,
physical evidence, unresolved physical occurrences, graph diagnostics, and
owner-issued total ordering keys; it is session-bound until an explicit
detachment operation issues durable occurrence references.
`ILInspector.CallGraph` turns `CallTreeNode` roots into a deterministic
node/edge set. It knows nothing about Mermaid, Markdown, tables, or any other
format, takes no dependency on Markout, the CLI, or inspected-assembly loading,
and stays SRM-only, NativeAOT-friendly, and browser-Wasm compatible.

Each host owns its own rendering. `dotnet-inspect` lowers the projection to a
Markout `Graph` in `CallGraphSectionAdapter`, which is where all call-graph
vocabulary — member spelling, `(external)`, `…`, and the `--fields` cue
annotations — lives; Markout then chooses a lowering per sink. The browser
prototype generates its own Mermaid from the same projection. The two renderings
need not agree, and neither layer below the host knows a format exists.

## What the projection owns

`CallGraphProjection.Create(callerRoot, calleeRoot)` produces one node/edge graph
centered on the selected overload:

```text
callers -> selected overload -> callees
```

Both roots are the selected overload. `callerRoot` is the reverse tree (its
children are inbound callers); `calleeRoot` is the outbound tree (its children
are callees). Either may be null (e.g. the browser's first caller-only view),
but not both, and when both are supplied they must name the same member. A
bodiless target (abstract / interface / extern) is the one exception the builders
resolve asymmetrically: `BuildCallerTree` recovers the real member from an
inbound call operand while `BuildCallTree` has no body and yields an `Unsupported`
placeholder root. The projection treats an `Unsupported` placeholder as *unknown
identity* — it never contradicts a resolved member — and centers the graph on the
resolved member, so the combined view still renders instead of throwing. Two
*different* `Unsupported` placeholder roots (each naming a different unresolved
token) are still rejected: the wildcard applies only to a placeholder paired with
a resolved member, never to two contradictory unknowns.

The projection owns everything a host must not re-invent in JavaScript:

- **Edge direction.** Caller-tree edges point child → parent (a caller flows
  *into* the target); callee-tree edges point parent → child (the target flows
  *out* to a callee).
- **Stable node identity.** A catalog-scoped tree carries
  `GraphNodeEvidence`: total physical storage identity plus the optional
  generation-scoped `CatalogMemberJoinKey` issued for its open signature.
  Exact and indeterminate issued keys are the logical graph identity;
  incomplete projections retain their unique storage identity and therefore
  cannot fabricate a join. Definitions and call sites remain separate physical
  occurrences even when correspondence collapses them onto one logical node.
  A host that must release a catalog before projection detaches the tree through
  `CatalogCallGraphScope.Detach`: exact occurrences with one definition retain a
  stable assembly-identity/MVID/MethodDef identity; unresolved or indeterminate
  joins receive a scope-local detached identity; and incomplete occurrences
  retain their unique physical storage identity. Generation-scoped
  correspondence is removed. Detached trees from separate scopes can therefore
  join the same exact physical definition without collapsing different versions
  or artifacts, while repeated unresolved occurrences remain joined within
  their original scope. `CallGraphNode.Identity` exposes the exact identity the
  projection used; adapters retain that currency rather than reconstructing it
  from `MemberRef`. These boundaries are gated by
  `DetachedVersionSkewedDefinitionsRemainDistinct`,
  `DetachedRepeatedExternalOccurrencesStayJoined`, and
  `DetachedArtifactIdentityIgnoresAcquisitionRegistration`.
  Generic recursion, constructed `MethodSpec` calls, varargs, modifiers,
  function pointers, instance/static shape, member kind, generic arity,
  parameters, and return types follow `CatalogMemberCorrespondencePlan`.
  Optional vararg arguments are not part of the member identity.

  Synthetic and same-assembly trees predating catalog evidence remain accepted.
  For those inputs the projection uses Analysis's typed structural fallback for
  the *entire* projection. It never mixes catalog and structural identities in
  one result. Shared callees, cycles, and the target as both caller and callee
  collapse to one node in either domain.
  The fallback's opaque member selector preserves ECMA-335 array kind:
  vector `T[]` and rank-one non-SZ `T[*]` are distinct at every nested
  position. The Metadata API producer emits the Analysis-owned structural
  payload whenever a non-SZ array requires it, and the Analysis `MemberRef`
  producer emits the byte-identical payload from `TypeRef`; normalized display
  spelling remains compatibility-only and does not erase exact array kind.
  Exact metadata-name segments containing literal array brackets use the same
  escaping on both producer paths, and the Metadata API producer emits that
  structural payload through bare names, arrays, and generic containers. Such
  names therefore cannot alias an actual array wrapper.
  `CallGraphArrayKindIdentityTests.Resolve_PreservesLiteralArrayNamesAcrossTypeShapes`
  gates those cases.
  `CallGraphArrayKindIdentityTests.Resolve_PreservesArrayKindForEventAccessors`
  gates the same producer agreement, distinctness, and tokenless resolution for
  event add/remove bodies in a CLR-loadable image.
  General exact-name identity, namespace-versus-nested boundaries, pinned-name
  compatibility, contextual generic-name shadowing, multidimensional arrays in
  display-parsed generic arguments, and primitive `TypedReference` identity are
  outside this array-kind claim and tracked by #5374, #5375, and #5376.
  An older surface without that structural payload cannot claim a structural
  match for `T[*]`, but an exact MethodDef-token candidate may still recover
  the body when no structural candidate matches.

  `CallGraphArrayKindIdentityTests.Resolve_PreservesArrayKindAcrossExtractedApiAndMemberRefSelectors`
  gates producer agreement for vectors, non-SZ arrays, nested generics,
  pointers, by-reference types, tuples, method generic parameters, custom
  modifiers, and return types, plus resolution with and without an exact-token
  candidate.
  `CallGraphCorrespondenceFlowTests.CorrespondenceFlow_MatchesRecordedStageExpectations`
  records the exact Metadata input, API selector, independently decoded
  `MemberRef` selector, tokenless resolution, and JSON-restored exact-token
  recovery for vector, rank-one non-SZ, rank-two, and nested non-SZ specimens.
- **Physical evidence.** Every projected node retains the distinct
  `GraphNodeEvidence` carried by the tree occurrences that collapsed into it.
  A catalog-resolved node also carries the exact defining assembly identity
  when every definition site agrees; call-site storage remains attributed to
  the caller and is not repurposed as defining evidence.
  Separately, the node carries the terminal assembly identity observed while
  resolving its declaring type. An unresolved terminal is an acquisition hint,
  never a definition claim, and conflicting observations are withheld. The
  Browser platform graph uses that hint only for nodes in its bounded
  projection, only when an already-authorized platform pack supplies the
  assembly, then rebuilds against the expanded participant-only workspace
  before transport. It never substitutes host filesystem probing.
  `CalleeTreeCarriesResolvedDefinitionAssemblyIdentity` and
  `ConflictingDefinitionAndResolutionAssembliesAreWithheld` gate the two typed
  identities and conflict handling.
  `PlatformCallGraph_ResolvesDefinitionsBehindFacadesWithoutHostProbing` gates
  the Browser no-resolver consumer boundary.
  Every product-built tree child also retains all `DirectCall` receipts for its
  parent edge and the acquisition-aware definition storage of their caller.
  `CallGraphProjection.CallSites` deduplicates catalog receipts by that caller
  definition, IL offset, and operand token when caller and callee walks observe
  the same physical site. Assembly-local and synthetic trees use their
  structural caller identity. A projection uses the acquisition-aware domain
  only when every physical edge supplies caller-definition storage; otherwise
  it uses structural receipt identity throughout, so mixed catalog and
  evidence-free input cannot duplicate one receipt by changing domains between
  observations.
  Detached logical caller identity is deliberately excluded because
  independent direction scopes can assign different identities to the same
  physical caller. Acquisition identity remains included because distinct
  artifacts can share assembly name, MVID, tokens, and offsets. Each logical
  edge retains the resulting dense call-site ids. The catalog scope retains the
  complete physical store independently. A
  receipt observed through independently detached direction scopes can map to
  different logical callers or targets when those scopes cannot reconcile the
  same catalog identity. The first deterministic edge retains the one physical
  receipt; every later edge marks its physical occurrence set unavailable
  rather than duplicating the occurrence or failing the graph. Such an edge can
  still retain other nonconflicting sites. Its typed loop state includes the
  fallback observation, and the generic adapter emits an unavailable-evidence
  limit and omits edge aggregates that would otherwise look complete.
  `ConflictingDetachedTargetsKeepOnePhysicalReceipt` and
  `ConflictingDetachedCallersKeepOnePhysicalReceipt`,
  `PartiallyConflictingEdgeDisclosesMissingLoopedReceipt`,
  `SameMvidSitesFromDistinctArtifactsRemainDistinct`,
  `DetachedCatalogDirectionsDeduplicatePhysicalReceipts`,
  `MixedEvidenceProjectionUsesOneReceiptIdentityDomain`, and
  `CallGraph_IndependentScopeIdentityConflictRemainsUsable` gate that behavior.
  Exact row lookup consults these retained receipts before structural fallback;
  `FindCalleeRowUsesRetainedNonRepresentativeCallSite` gates repeated sites
  whose node evidence carries only a representative occurrence. A
  call-site storage key identifies one physical operand occurrence (source
  registration, MVID, evidence-method token, IL offset, and operand token);
  `DirectCall.Caller` may name the declared source method while
  `DirectCall.EvidenceMethod` names that physical body. The key is evidence,
  never a logical node count or a cycle key.
- **Deterministic ids/ordering.** The focus is id `0`; remaining ids are assigned
  in first-seen order over a caller depth-first walk, then a callee walk. Nodes
  are emitted in id order and edges in first-seen order, so the same input
  always yields an identical projection. Both the cheap assembly-local caller
  tree and the catalog caller tree order inbound edges by assembly name,
  qualified member identity, physical definition identity, and call-site
  offset. Requesting an otherwise noncontributing catalog scope therefore does
  not change sibling order, revisit placement, or which nodes fit in a bounded
  traversal.
- **Stable rows.** A call graph answers "what calls what", so its row unit is a
  directed edge. `Rows` numbers those edges from one in deterministic edge order
  and retains those numbers when a host filters them. Counts and row windows
  therefore bind to the projection rather than to a rendered tree's node lines.
  `FindFocusCalleeRow` maps a physical call occurrence from the selected member
  to that stable logical edge row. Exact catalog call-site storage wins; the
  assembly-local fallback uses the same typed structural identity as projection.
  `FindFocusCalleeTarget` returns an occurrence view of that row's target: it
  keeps the logical node id and identity while restoring the physical call's
  typed member and exact encoded assembly scope. A definition identity from a
  collapsed node is withheld when the projection has no unambiguous terminal
  resolution and it conflicts with the occurrence scope. The occurrence view
  is not inserted into `Nodes`.
  `FindCalleeTargetRestoresVersionDistinctOccurrenceIdentity` gates these
  properties.
  `FindNode` likewise maps a `MethodIdentity` to a node, preferring exact
  definition evidence, including the exact definition that supplied body facts
  for a detached call-site occurrence, before the typed structural fallback
  used by evidence-free projections. It never structurally crosses versioned
  catalog evidence. Hosts use that method to join non-topological annotations
  without reconstructing member identity from labels. Missing and ambiguous
  mappings remain distinct outcomes. `FindNodePrefersExactDefinitionEvidence`,
  `FindNodeUsesRetainedCallSiteDefinitionEvidence`,
  `FindNodeUsesTypedStructuralFallback`, and
  `FindNodeDoesNotCrossVersionedEvidence` gate that contract.
- **Cycles and duplicates.** The bounded tree marks re-encountered members
  `AlreadyShown`; the projection collapses them onto the existing node and still
  records the edge, so a cycle `A → B → A` is two edges between two nodes.
  `FindFocusCycles` derives simple cycles that start and end at the selected
  member from those existing rows; it never reopens an image or rebuilds a
  traversal. Witnesses are ordered shortest first and then by stable edge-row
  sequence. `MaxWitnesses` bounds retained results and `MaxPaths` bounds the
  breadth-first search itself, so dense projections cannot turn witness
  discovery into unbounded path enumeration. `FocusCyclesAreShortestThenStableEdgeRowOrder`,
  `FocusCycleSearchReportsIndependentCostLimits`, and
  `FocusCycleSearchDoesNotRepeatNodesWithinAWitness` gate those properties,
  including the equal-length ordering tie-break.
- **Boundary and external classification.** `External` callees carry
  `CallGraphNodeKind.External`; `DepthLimited`, `Truncated`, `Bodiless`, and
  `AnalysisIncomplete` nodes carry `Truncated`, meaning "more beyond here".
  `Bodiless` means the resolved definition has no IL body and static operand
  traversal cannot rule out runtime dispatch or an external implementation.
  `AnalysisIncomplete` retains the method's typed `AnalysisDiagnostic`; partial
  calls found before the recoverable failure remain positive evidence. If the
  same node also exhausts the node budget, `Truncated` remains its status while
  the diagnostic remains attached; budget handling and analysis-failure
  disclosure therefore stay independent. An
  occurrence expanded elsewhere outranks a boundary occurrence of the same
  member, so a shared node is never misclassified as a dead end. How a host
  *shows* those kinds is the host's choice — the CLI groups external nodes and
  suffixes their labels; the browser styles them with a CSS class.
- **Directional traversal completeness.**
  `HasUnexploredTraversalBoundary` is separate from the merged display kind.
  Only the outbound callee traversal can prove absence: its edges come from
  each reached method's own body, while a caller-tree `Leaf` means only "no
  callers in this indexed scope." Within the outbound direction, an expanded
  occurrence satisfies boundary duplicates of the same typed graph identity.
  `AlreadyShown` defers to that primary occurrence and cannot override a
  `Truncated` primary. A bodiless definition and a recoverable body-analysis
  failure are always outbound boundaries; the latter is also exposed
  independently as `HasAnalysisFailureBoundary`. A `callvirt` or `ldvirtftn`
  occurrence whose static operand is virtual, non-final, and declared on an
  unsealed type is also an outbound boundary: runtime dispatch can select an
  override that the static operand tree does not contain. This fact belongs to
  the occurrence rather than the collapsed member identity, so a direct
  occurrence of the same member cannot mask it. Assembly-local and catalog tree
  lowering OR the fact across physical call sites before selecting one
  representative edge for a collapsed callee, including when loop evidence
  selects a direct-call representative. Repeated physical sites therefore
  retain their true fan-out without consuming the bounded node budget more than
  once. Nonvirtual methods and final overrides remain complete; ordinary
  nonvirtual instance calls emitted as `callvirt` do not acquire a false
  boundary.
  `CycleCompletenessCollapsesBoundariesWithinOneDirection`,
  `CallerLeafDoesNotHideAnOutboundTraversalBoundary`,
  `AlreadyShownDoesNotHideATruncatedPrimaryOccurrence`,
  `BodilessCalleeKeepsAnEmptyCycleCensusIncomplete`, and
  `BodyAnalysisFailureRemainsAnExplicitTraversalBoundary`,
  `UnresolvedVirtualDispatchKeepsAnEmptyCycleCensusIncomplete`, and
  `CycleWitnessSurvivesUnresolvedVirtualDispatch` gate the projection
  distinctions. `BuildCallTree_ClassifiesSameAssemblyBodilessCallee`,
  `BuildCallTrees_MarkOnlyOpenVirtualDispatchAsUnresolved`,
  `CallTrees_PreserveDispatchAcrossCalleeCollapse`, and
  `BuildCallTree_PreservesRecoverableBodyAnalysisFailure` gate the
  Analysis-to-tree wiring for both assembly-local and catalog traversals,
  including the diagnostic-plus-budget precedence.
- **Loop-call annotations.** A call made inside a loop sets typed
  `AnyCallInLoop` edge state, aggregated from retained call sites. The host
  derives `loop` outbound or `loop call` inbound from that state and the edge's
  first traversal origin. Evidence-free compatibility trees fall back to the
  child node's loop flag and legacy hint.
- **Per-node analysis facts.** `CallTreePerf` (fanout, fanin, depth, loop, source
  assembly, and the `MethodSignals` cost/exception cues) travels on the node, so a
  host can project any subset without re-walking the tree. Perf is analysis data,
  not presentation.

  Both walks observe the same member, but neither observes all of it: a caller
  tree indexes the caller scope and reports fan-in, the root classification and
  cross-assembly source while hard-coding fan-out to `0`; a callee tree indexes
  the callee scope and reports fan-out but never classifies a root. Merging the
  two observations therefore happens field by field, keeping the side that
  actually measured each fact.

  Merging is only sound when both sides measure the same quantity, so the units
  are pinned in `LibraryBodyIndex` rather than reconciled here:

  - **Fan-in counts distinct callers, never call sites.** It is a leverage cue —
    "how many members depend on this one" — and the reverse graph draws one edge
    per distinct caller, so the annotation has to agree with the picture it
    annotates. A caller that invokes the target three times contributes `1`.
  - **Fan-out counts call sites**, because outbound cost is per site.
  - **Depth is the bounded subtree height rooted at the node**, so on the caller
    side it measures upstream reach and on the callee side downstream reach.

  Given matching units, degrees and depth are lower bounds over whichever scope
  set that walk indexed, so the larger observation is the better-informed one:
  the merge takes the maximum, and a direction that never measures a quantity
  reports `0` and can never pin it. For depth this publishes the taller of the
  two subtrees rooted at the member — its widest reach in the bounded graph,
  in whichever direction that reach runs.

### Equal-root outbound projection

`CallGraphProjection.FromCallees(calleeRoots, maxNodes)` projects two or more
ordered outbound roots without choosing a primary focus. `RootNodeIds` retains
their caller-supplied order as the leading projection nodes. `Focus` remains
the single-root convenience and fails explicitly for an equal-root projection
rather than silently selecting the first root.

All roots come from one Analysis evidence generation and use the same identity
domain. The projection registers every root before expanding any downstream
tree, so the combined node budget cannot discard a requested root. The budget
must therefore be at least the root count. Downstream nodes retain the existing
identity collapse, edge direction, occurrence deduplication, node-kind
precedence, and deterministic first-seen ordering. Shared helpers appear once;
disconnected roots remain present. When the combined node budget stops
expansion, `HasUnexploredTraversalBoundary` remains true.
Traversal completeness is likewise projection-wide: one complete expansion of
an identity satisfies a depth-limited or already-shown occurrence reached from
another equal root.

Analysis and the consuming query continue to own traversal depth. Each supplied
tree retains its Analysis-issued depth, external, unresolved-dispatch, and
body-failure boundaries; projection combines those boundaries without claiming
that absence beyond any root is complete. The overload-family mode tracked by
issue #8243 supplies the common requested depth and binds `RootNodeIds` as equal
peer seeds. Structural entry and convergence interpretation remains outside
projection and is tracked by issue #8258.

`EqualRootsPrecedeOneSharedNeighborhood`,
`EqualRootNodeBoundRetainsEveryRootAndDisclosesBoundary`, and
`EqualRootsCombineTraversalAndAnalysisBoundaries` gate shared topology, the
combined bound, root ordering, occurrence reuse, and failure preservation.
`EqualRootsRetainDisconnectedNeighborhoods` and
`EqualRootCycleIsCompleteWithoutChoosingFocus` gate the pathological graph
shapes. `EqualRootExpansionCompletesBoundaryDuplicate` gates completeness
composition across roots. The pinned `System.Text.Json` gate
`MultiRootProjection_SystemTextJsonRetainsEverySerializeRootAndSharedHelper`
retains all 15 public `JsonSerializer.Serialize` roots and a downstream helper
reached from more than one root.

### Lowering a bidirectional graph to a tree

The projection is a graph, and a graph containing a cycle through the focus
cannot be drawn as a tree without breaking it somewhere. Markout's tree lowering
roots at the focus, follows outbound edges, and appends anything still unvisited
as an additional root; a member that is both an inbound caller and an outbound
callee is therefore printed under whichever side reached it first, with the
other side pointing at it through a `↩` revisit leaf.

No node and no edge is lost — the revisit leaf carries the edge — but a caller
chain that re-enters through the callee side reads as two fragments rather than
one path. That is a property of tree lowering, not of the projection, and the
edge-table lowering (`--table`, `--tsv`, `--jsonl`) shows every edge in one
place for readers who need it. Splitting the model back into two graphs to make
the tree prettier would reintroduce exactly the duplicated walk this design
removes, so the projection stays single and the lowering stays lossy-by-shape.

Escaping is *not* the projection's job. Labels are member spellings; making them
safe for a given output grammar belongs to the renderer that knows the grammar
(Markout's `MermaidFormatter` for the CLI).

## Progressive acquisition

The projection needs `CallTreeNode` roots; how a host *acquires* them is a
separate concern (issue #3266). `DotnetInspector.Queries` owns a
`MemberCallGraphSession` seam over one `AssemblyContextGroup`
(`src/DotnetInspector.Queries/MemberCallGraphSession.cs`). It consumes
typed assembly descriptors and workspace-owned immutable snapshots rather than
filesystem paths, and serves one member's graph in three cumulative layers,
cheapest first, so a host can paint the outbound half immediately and fill in
the expensive tiers as they land:

`Session` names the stateful memoization and lifetime boundary. Progressive
acquisition is a capability of that session, not a separate call-graph kind.

1. **`Callees`** — a scoped single-body build that decodes only the selected
   member. The callee tree is bounded at depth 1 (immediate callees); there is
   no caller tree yet.
2. **`Callers`** — a full decode of the member's own assembly, adding the
   intra-library caller tree and deepening the callee tree to the configured
   depth. Expansion stops at the assembly edge.
3. **`CrossLibrary`** — decodes the in-scope packages so *both* the caller tree
   and the callee tree can cross a library boundary up to `depth`.

The layer names name the tier that was unlocked, not a direction: at `depth > 1`
the `CrossLibrary` layer lets a caller chain *and* a callee chain each cross a
package boundary. The seam yields presentation-free `CallTreeNode` roots as a
`MemberCallGraphView` (`Tier`, focus MVID/token, `CalleeRoot`, `CallerRoot`,
`FocusCallSites`, `Diagnostics`). `FocusCallSites` retains every physical
outbound operand occurrence in the selected member's own IL body from the same
scoped or full index that produced the roots. Calls attributed from generated
evidence bodies remain receipts on the logical graph edge, but are not attached
to the declared kickoff body's source or IL offsets. The tree carries physical
receipts on every retained edge, not only the focus edge. A host renders the
roots directly or projects
them with
`CallGraphProjection.Create(CallerRoot, CalleeRoot)` — "with or without mermaid."
`Diagnostics` is a stable count summary of incomplete correspondence and exact
bindings to a different identity of the primary assembly, distilled before any
temporary catalog scope is released. A host can therefore disclose those
boundaries without retaining generation-bound graph evidence or rebuilding the
graph. The CLI's direction-specific scopes also detach their trees before
release, preserving physical evidence and safe exact or scope-local identity
while dropping generation-scoped correspondence. The exact binding remains
exact graph identity; the diagnostic does not join one assembly version to
another. If either direction is scoped, the CLI builds the other direction in
a target-only catalog rather than mixing a detached tree with an evidence-free
local tree; `CallGraph_KeepsVersionSkewedCallersWhenCalleesAreUnscoped` gates
that projection never falls back to structural identity and collapses versions.

Caller-scope admission distinguishes result cardinality from completeness
before graph construction. When a caller's exact assembly reference binds to a
different definition than the inspected target, that caller is conclusively
excluded from the target's graph. If no caller remains, the complete empty graph
is a canonical successful value, analogous to `string.Empty`: zero edges is
evidence, not absence of evidence. `Complete(empty)` is therefore distinct from
`Incomplete(partial)`. An unavailable or ambiguous correspondence retains the
candidate as indeterminate and makes the affected result incomplete; exact
exclusion does not. This does not change the separate case where an admitted
graph contains an exact binding to a different identity of the primary
assembly, which remains represented by `BindingIdentityConflictCount`.
`CallerScopes_ExactReferencedVersionExcludesDifferentTarget` gates the
Analysis-owned admission result, and
`Member_CallGraph_VersionSkewedCallerScopeIsCompleteAndEmpty` gates the CLI
presentation without an incompleteness warning.

`CrossLibraryCalleeNeighborhood` exposes the existing cross-library callee
traversal as a call-only L1 inspection-graph neighborhood. Its request carries
non-negative maximum edge depth and a positive call-node budget. The returned
document retains one member seed, outgoing caller-to-callee direction, the
generic neighborhood depth bound, the call-specific node bound, every retained
physical call-site receipt, and any incomplete catalog correspondence. Depth
zero retains only the seed. A missing in-scope definition remains an external
boundary; an acquired definition may continue transitively across an assembly
boundary. This surface does not add another resolver or traversal: it invokes
`CatalogCallGraphScope.BuildCallTree`, projects the callee tree once, and applies
the shared dense neighborhood projection. The
`CrossLibraryCalleeNeighborhood_*` tests gate cross-boundary continuation,
depth-zero and finite-depth behavior, node bounds, external placeholders,
physical receipts, and correspondence disclosure.

**No duplicated work.** At most two target-assembly indexes are ever built — the
scoped single-body build and the full build — plus one build per cross-library
package, and each is built once and reused for callees, callers, and any
projection. The scoped build exists only for the progressive first paint: a
consumer that wants the whole graph calls `Callers()` or `CrossLibrary()`
directly and pays exactly one full build, with callees derived for free from it.
Once the full build lands it supersedes the scoped one, which is never rebuilt.
The full build releases the scoped index, while both builds read the same
workspace snapshot; each participant source is opened at most once. Participants
with the same assembly identity and MVID share one full index even when the
group contains multiple acquisition descriptors for that image.
For the cross-library tier, `CatalogCallGraphScope` then plans each distinct
source signature once, unions all type-resolution requests, freezes one catalog
generation, projects every plan once, and stores physical definitions, call
sites, and edges once. Both traversal directions and every later
`CallGraphProjection` reuse that storage; Mermaid does not trigger a second
walk or acquisition. The owning `AssemblyContextGroup` disposes the graph
generation and catalog before releasing its retained snapshots; explicit graph
disposal can release them earlier. A required participant failure raises
`MemberCallGraphAcquisitionException` with typed acquisition failures rather
than returning a success-shaped partial graph.
`MemberCallGraphSessionTests` asserts index build and source-open counts,
stream-only input, duplicate-image reuse, typed failures, projection reuse, and
group-owned release, including disposal of the catalog scope.
`CatalogCallGraphScopeTests` pins the single-generation,
single-policy-evaluation, shared-storage, duplicate-artifact, and
incomplete-evidence contracts.

`DotnetInspector.ResearchQueries.AnnotatedMemberDocumentQuery` is the first
non-rendering consumer of this progressive seam. It accepts an already-acquired
view and an already-open `MetadataSource`, projects the graph once, and returns
portable source plus an `AnnotatedCallGraphOverlay`. Each overlay occurrence
names both its stable edge row and its `call.edge` fact id. Two calls at
different IL offsets therefore remain two physical occurrences and two source
facts even when they share one logical edge row. A node budget limits both the
projection and the supplied relationship facts; omission under a
`DepthLimited` or `Truncated` focus is not reported as a mapping failure.
Caller-only views are rejected because they contain no outbound topology to
which body-local call sites could map.

The overlay also carries an `AnnotatedCallGraphCycleInspection`. Each observed
focus cycle is a `Finding<CallGraphCycleWitness>` whose payload is the ordered
stable edge-row path. Its `FindingKey` comes from the typed member-identity path,
not those projection-local row numbers, so an unrelated earlier edge does not
rename the observation; `CycleFindingIdentityDoesNotDependOnEdgeRowNumbers`
gates that separation. Operation completeness remains separate from the durable
Finding: `TraversalBoundary`, `AnalysisFailure`,
`IncompleteCorrespondence`, `WitnessBudget`, and `PathBudget` are independent
flags. A positive cycle therefore remains valid
when unrelated work was bounded, while an empty bounded census means only "not
observed in this tier and budget," never "not recursive." The projection retains
directional traversal completeness separately from display node kinds, so a
depth-limited occurrence does not make the census incomplete when that same
logical node was expanded elsewhere in the same direction.
`CycleFindingSurvivesUnrelatedGraphAndCorrespondenceLimits`,
`CycleFindingSurvivesAnExplicitBodyAnalysisFailure`, and
`AnnotatedMemberDocument_HonorsACalleeNodeBudget` gate the positive and empty
bounded cases.

An opt-in Resource Occurrence request adds generic ownership paths to that same
overlay without changing graph acquisition. Analysis computes compact detached
per-method summaries while each selected body is already decoded: owner-issued
acquisition obligations, resource-neutral incoming-parameter flows, physical
forwarding calls, typed releases, field stores, and returns to the caller. It
reuses the body's existing `MethodInstructions` for reaching definitions and
retains no IL, blocks, or dataflow state.
`MemberCallGraphView.ResourceOwnershipSummaries` carries those summaries from
the same focused results that produced the current graph tier; Research joins
forwarding calls to stable edge rows and performs no body, graph, effect, or
source acquisition.

The first body-scoped tier can therefore expose an acquisition and its
forwarding edge before the callee body is available. A later full tier
supersedes the scoped result and completes the path from already-retained
summaries. Terminal `Finding<ResourceOwnershipPathWitness>` payloads distinguish
`Released`, `Stored`, and `ReturnedToCaller`; retain the selected resource kind;
and retain every physical forwarding coordinate even when repeated call sites
collapse onto one logical edge row. Finding identity uses the acquisition,
resource kind, physical coordinates, and typed sink identity, not labels or row
numbers alone.

The production annotated-document consumer selects the ArrayPool resource kind
as Research query policy. Analysis does not encode ArrayPool identity in the
generic summary. The earlier `ArrayPoolOwnershipPathFindings` path remains
independently executable as a fidelity oracle; it is not an input, adapter, or
fallback for generic composition.

Ownership completeness remains separate from positive Findings:
`NotRequested`, `TraversalBoundary`, `IncompleteCorrespondence`,
`BodyUnavailable`, `AnalysisFailure`, `WitnessBudget`, and `PathBudget` are
independent flags. Address-taken, local-alias, unsupported-stack, unresolved,
or failed body evidence is incomplete rather than a safe outcome. A catalog
node without a matching physical definition never borrows a structurally
similar body from another image. The feature is not in `MemberCallGraphOptions`' default producer set, so callers
pay the reaching-definitions and retained-summary cost only when they provide
admitted `ResourceEffects`.

`AnnotatedOwnershipProgressesWithoutReacquiringGraphWork` gates scoped-to-full
progression and unchanged build/source-open counts.
`AnnotatedOwnershipComposesTypedTerminalPaths` gates multi-hop, instance, and
constructor forwarding; `OwnershipWitnessBudgetPreservesPhysicalCallIdentity`
and `OwnershipPathBudgetLeavesForwardedPathIncomplete` gate the two budgets.
`AddressTakenRentIsRetainedAsIncomplete` and
`OwnershipForwardedToABodilessCalleeIsIncomplete` gate the close negative
cases. `IndirectCallShapesAreRetainedAsIncomplete` and
`OwnershipIndirectCallShapesDoNotProduceSafeFindings` gate that `ldftn`,
`ldvirtftn`, and `calli` remain unsupported/incomplete rather than entering the
direct-call stack model.
`GenericOwnershipKeepsResourceKindsDistinctOnOneGraph` gates two obligations
with different resource kinds over one collapsed graph edge, while
`OwnershipPositiveWitnessSurvivesAnIncompleteSiblingPath` gates path-local
completeness.

The query declares no graph or Analysis acquisition.
`AnnotatedMemberDocument_ReusesCalleeLayerAndMapsEveryPhysicalCallSite` test
gates graph-session reuse by asserting unchanged session build/source-open
counts and the two-occurrences/one-edge shape.
`AnnotatedMemberDocument_ReportsOneCycleForRepeatedRecursiveCalls` extends that
gate to the cycle projection: two physical recursive calls retain two source
occurrences, share one logical edge, produce one cycle Finding, and leave the
session build/source-open counts unchanged.
`AnnotatedMemberDocument_ReportsAMutualCycleAtTheCallerTier` proves the same
composition over a compiler-produced two-method cycle without another target
index or source open. `ExhaustedTraversalProducesACompleteEmptyCycleCensus`
gates the only empty result that supports an absence claim.
`Registry_UnionsProducerAnalysisRequirementsBeforeAcquisition` pins the
call-only Research profile to `ResearchFactRequirements.None`, and
`RequirementsNone_DoesNotResolveAnAssemblyContext` is the non-vacuity gate that
proves such a profile bypasses Research's Analysis-context resolver.

The Inspect Web member Finding census adopts the same body-local relationship
dimension through
`ResearchFactRegistry.MemberCensusWithCallRelationships`. The source operation
requires an exact MethodDef token, obtains physical calls from its retained
`LibraryBodyIndex`, and builds one depth-one callee projection that also
supplies invocation destinations. The resulting `call.edge` Findings share the
ordinary census receipt and source targets while remaining outside the default
annotation set; **All** reveals them without changing first paint. The Browser
contract also transports one typed sidecar row per Finding with the caller
MVID and MethodDef token, IL offset, operand token, call kind, loop state,
stable edge row, and occurrence-specific graph target. Browser validation
requires exact coverage between those rows and the document's `call.edge`
Findings; it never recovers identity from labels or source text.

The Annotated Source modal consumes that sidecar as a **Relationships**
projection with **Table** and **Diagram** presentations. **Table** is the
fresh-session default. It renders one row per physical call occurrence, so
repeated source calls that share one stable logical edge remain separate rows.
Each row exposes the typed call kind, loop state, stable edge row, the exact
`call.edge` Finding opener, and explicitly named **Member** and **Source**
actions over the typed target. Coordinate disclosure adds the method-relative
IL offset.

**Diagram** is an opt-in Browser lowering over those same validated rows. It
renders the current body as the root and one visual edge per producer-issued
stable edge row. Repeated physical occurrences may therefore share one visual
edge, but the Browser retains every exact `factId`, discloses the occurrence
count, and provides an explicit path back to the physical table rows. One
stable edge may contain occurrence-specific typed targets with different
assembly identities; the grouped edge therefore retains every distinct typed
target and representative relationship index rather than choosing one.
The diagram's companion entries expose explicitly named **Member** and
**Source** actions for each retained target; SVG text and Mermaid node
identifiers are presentation only and never become identity. Diagram
activation performs no Call Graph query, Analysis acquisition, target-index
build, or source open.

Both presentations are independent of annotation membership: they are
available whenever the relationship capability is available, while **All**
remains the only way to draw relationship annotations at source locations.
Available-empty means only that no direct relationships were projected for
that exact body; unavailable retains its typed capability reason. The browser
does not reacquire a graph, silently navigate from a visual edge or node,
discard physical occurrences, or reconstruct identity from presentation.

The Finding-census operation can also request focus-cycle inspection. It keeps
the complete depth-one focus neighborhood required by the relationship
contract, enriches that root with a depth-three, 25-node same-assembly
caller/callee expansion, and lowers the result through one projection. Each
Browser cycle retains the durable Finding key and ordinal, ordered stable edge
rows, every physical `call.edge` fact id for its first logical edge, and one
typed graph target per edge. The viewer attaches those witnesses to the
existing opt-in relationship detail; it does not mint another source fact or
infer a path from labels. Completeness remains independent through
`TraversalBoundary`, `AnalysisFailure`, `IncompleteCorrespondence`,
`WitnessBudget`, and `PathBudget`. Positive direct and mutual recursion
witnesses remain valid under any limit; only an empty complete census supports
absence within the projected same-assembly focus graph. A logical cycle whose
first edge exists only in an attributed generated body has no declared-body
source fact to anchor; the source projection omits that witness and reports
`IncompleteCorrespondence` instead of failing the enclosing Finding census.

The same operation can classify exact physical relationships that synchronously
observe `Task` completion. Analysis authenticates only framework
`Task.Wait(...)`, `Task<T>.Result`, and task-awaiter `GetResult()` members and
matches their complete ordinary instance signatures, including exact parameter
and return types, non-generic method arity, and default calling convention.
Fixed framework signature types require trusted framework identity; generic
result members preserve and match the open declaring-type parameter. Fixed
types also retain their exact ECMA-335 primitive or value-type discriminator,
so a same-name `CLASS`/`VALUETYPE` mismatch is not authenticated. It
returns a typed operation kind; Research joins each positive observation to the
existing physical `call.edge` fact. The classification needs no graph expansion,
body reopening, ownership result, or inferred source text. It deliberately does
not classify custom awaiters or `ValueTask`, and an empty result makes no claim
that the member is free of blocking behavior. The viewer describes the proven
structure as a synchronous completion operation that *may* block when the task
is incomplete; actual blocking, duration, frequency, deadlock, and completion
state require runtime or stronger flow evidence and are not asserted.
[VSTHRD002](https://microsoft.github.io/vs-threading/analyzers/VSTHRD002.html)
and
[CA1849](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1849)
recognize the same three Task structures as analyzer inputs. This projection
uses that established structural boundary but deliberately reports evidence
rather than a diagnostic: it does not apply source-level completion guards,
async-context policy, or code-fix advice.

Annotated Source can also expose the complementary asynchronous structure for a
successfully reconstructed classic await. The Decompiler, not Analysis, owns
this evidence because
[the classic async inverse](classic-async-reconstruction.md) already requires a
closed proof that the exact `IsCompleted` test has an inline edge to the matching
`GetResult`, a suspension-registration edge, and a correlated resume edge to the
same continuation before it may mark the reconstructed `AwaitExpression`.
Re-running a second, weaker recognizer over Analysis IL would duplicate that
proof and could disagree with the source reconstruction the viewer actually
displays. Method-level async classification is likewise too coarse because one
rendered body can contain other await syntax that does not carry this proof.
Research therefore emits one typed sidecar row only for each exact printed
`AwaitExpression` retaining the classic inverse's completion-path marker.
If marked and unmarked contributors collapse to one rendered node, projection
fails closed and emits no row for that node.
Browser validation requires every row to name an existing C#
`AwaitExpression` node and rejects duplicates; no identity is recovered from
method classification or source text. Selecting the await explains the
inline-completion and suspension/resume paths and states
that this is compiled structure only. It does not claim which path ran,
successful completion, path frequency or duration, scheduler or thread
selection, `ExecutionContext` behavior, or allocation behavior. Runtime-async,
async iterators, and declined classic lowerings fail closed with no observation.
The sidecar remains outside default annotations and adds no source Finding.

Annotated Source also preserves Analysis-owned exception-path classification
for exact allocation Findings. Research reads the typed
`AllocationOccurrence`, not its formatted `path=...` detail, and joins each
positive observation to the existing source document `FactId`. A thrown-value
observation requires `Escape == ThrowPath`; an exception-handler observation
requires `PathContext == ErrorPath` after excluding thrown values. Browser
validation requires a unique body allocation fact for every observation. The
viewer adds structured detail to the selected allocation Finding without
minting another Finding, chip, or default annotation.

The presentation says either that the allocation constructs the thrown value
or that it occurs in a catch, filter, or fault handler, followed by an explicit
compiled-structure-only disclaimer. It does not claim that an exception
occurred, that a handler ran, path frequency, rarity, latency, or runtime
allocation count. Ordinary branch and switch-arm allocations are close
negatives: the current Analysis evidence proves conditional placement but not
that the branch is semantically a fallback. The motivating real shape is the
`System.MemoryExtensions` throw helper documented in
[caret stacking](caret-stacking.md), where the exception allocation already
carries `path=error-path` and `escape=throw-path`; the typed sidecar removes the
browser's need to parse that presentation string.

Annotated Source can additionally compose a bounded positive path from the
selected exact MethodDef to a same-module MethodDef containing an
Analysis-proven local `throw new`. Research Queries supplies the selected
MethodDef as the only
[library-body root](library-body-root-paths.md), supplies exact local
destinations whose `MethodLocalThrowEvidence` contains at least one known
site, and requests deterministic shortest witnesses with fixed depth, node,
edge, and retained-path limits. The operation reuses the member projection's
one `LibraryBodyIndex`; it performs no second body acquisition, source open, or
graph build.

Each projected witness retains every physical `call.edge` fact for the
Analysis-admitted `call`, `callvirt`, and `newobj` occurrences on its first
logical edge, one typed call-graph target for every path step, and every known
terminal local-throw site with its exception `TypeRef`, qualified TypeDef
address, construction offset, constructor token, and physical `throw` offset.
Function-pointer loads and indirect calls may share the relationship
projection's stable edge row, but they neither begin the proven direct-call
path nor enter its physical receipt set.
The first-edge fact ids, not a rendered path or member label, attach the
witness to source. Repeated physical calls therefore share the same logical
shortest path without losing their separate source occurrences. A path that
cannot map its first edge or one of its typed methods to the retained
projection is omitted with an explicit correspondence boundary.

The root-path operation retains at most one deterministic shortest witness for
each root/destination pair. Equal-length alternatives are therefore not a
per-first-edge census. When the operation retains a witness elsewhere but none
begins with the selected relationship, Finding detail says only that no
retained deterministic shortest witness begins there; it does not claim that
no path through that relationship exists.

Research supplies only Analysis-proven local-throw destinations. When none are
available, it does not run a synthetic root/destination search: the receipt
reports zero destinations and zero search work while independent Analysis,
traversal, local-throw, and correspondence boundaries still constrain empty
use.

Completeness combines the Analysis root-path boundaries with local-throw
coverage. Positive witnesses remain valid when either operation is incomplete.
An inspected unresolved throw site, a relevant unavailable body, exhausted
search work, unattributed generated execution, unresolved local call, Analysis
diagnostic, or failed source correspondence prevents an empty result from
becoming an absence claim. A method with no managed body is not a hidden local
IL throw site; runtime override dispatch remains outside the direct-call
contract. The viewer may state only that no path was observed within the
available bounded evidence.

The detail wording is deliberately not exception propagation. It says that the
selected root has a bounded static direct-call path to a method containing a
proven local construction-fed `throw`. It does not claim that the root or any
intermediate method throws at runtime, that the terminal throw is reached or
escapes its method, that an exception propagates to the root, or that a caller
catch, filter, or fault intercepts it. It also makes no frequency, latency, or
runtime-path claim. Calls, catch declarations, `rethrow`, exception
construction without a consuming `throw`, unresolved throw operands, and
same-looking foreign-module identities do not create positive witnesses.

The motivating real asset is CoreLib's
`ArgumentNullException.ThrowIfNull(object, string)`, which has no local throw
but directly calls the internal `ArgumentNullException.Throw(string)` body
that Analysis proves constructs and throws `ArgumentNullException`. The
composition preserves that distinction: the path is positive, the selected
root remains locally clean, and no propagation statement is synthesized.

`MemberProjection_ComposesCallRelationshipsWithTheFindingCensus` gates the
single operation shape, and
`MemberFindingCensus_ProjectsExactCalleeEvidenceSource` gates production
Browser/Wasm transport alongside existing callee evidence.
`MemberProjection_ProjectsRepeatedDirectRecursionAsOneCycle`,
`MemberProjection_ProjectsMutualRecursionAsAnOrderedCycle`, and
`MemberProjection_OmitsGeneratedBodyCycleWithoutFailingSourceCensus`, and
`MemberFindingCensus_ProjectsExactMutualCycleWitness` gate the cycle identity,
physical anchoring, ordered typed path, and production Browser/Wasm transport.
This adoption does not transport ownership witnesses or reuse the separately
requested full member Call Graph surface.
`MemberProjection_ProjectsSynchronousTaskCompletionOperations` and
`MemberFindingCensus_ProjectsSynchronousTaskCompletionOperation` gate the
framework identity, physical `call.edge` join, Browser/Wasm transport, and
non-default detail. Compiler-generated async-body calls remain outside this
declared-body source correspondence; their absence is not presented as a clean
result. `MemberProjection_ProjectsClassicAwaitCompletionPaths` and
`MemberFindingCensus_ProjectsClassicAwaitCompletionPaths` gate the authenticated
classic kickoff, exact Decompiler-issued await-node binding, Browser/Wasm
transport, and bounded no-runtime-claim presentation.
`MemberProjection_ProjectsAllocationExceptionPaths` and
`MemberFindingCensus_ProjectsAllocationExceptionPath` gate the typed allocation
payload, exact source-Finding join, thrown-value versus handler distinction,
Browser/Wasm transport, and branch-only close negative.
`MemberProjection_ProjectsBoundedLocalThrowPaths` gates the CoreLib
`ThrowIfNull` path, root-local negative, terminal type and offsets, exact
first-edge physical receipts, deterministic shortest path, and visible
Analysis/local-throw/correspondence boundaries.
`MemberFindingCensus_ProjectsBoundedLocalThrowPath` gates Browser/Wasm
transport, typed path targets and exception identity, static-evidence wording,
and unchanged Analysis-build/source-open counts.

Drive it by pull (`Callees()` / `Callers()` / `CrossLibrary()`, or the lazy
`Tiers()` stream) or by push (`RunAsync` raising `LayerReady` per layer then
`Completed`). The push path is a thin wrapper over the same memoized pull core,
so the two never double the work. The forward and reverse cross-library
expansions are two queries over the same `CatalogCallGraphScope`; neither builds
a direction-specific identity map.

## Consumers

`dotnet-inspect` renders one bidirectional `Call Graph` section from the
projection. `CallGraphSectionAdapter` lowers it to one Markout `Graph`, and
Markout picks the lowering the sink can express: an edge table in Markdown by
default, a standalone tree under `--tree`, an edge table under
`--table`/`--tsv`/`--jsonl`, a standalone diagram under `--mermaid`, or a
fenced diagram under `--markdown --mermaid`. The adapter is the only place that
knows call-graph vocabulary; the section is a graph, not a pre-rendered tree or
diagram, which is what lets one model serve every sink. `--count` reports the
projection's edge-row count. `--rows` selects those same stable edge rows before
tree/diagram lowering and at the table writer boundary for tabular output, so
changing the final rendering does not change the addressed relationships.

The browser engine consumes the same projection and generates its own Mermaid; it
reconstructs no graph identity, direction, truncation, cycles, or labels. The CLI
and the browser deliberately do not share a Mermaid generator — sharing the
*graph* is the point, not sharing the *format*.
Browser navigation transports the projected display spelling separately from
the exact metadata type name; the latter preserves nested `+` delimiters and
generic arity and is the only spelling used to resolve a graph target.
Constructed generic declaring types recover that name and assembly from their
definition. Synthetic array and function-pointer declaring shapes remain
renderable but intentionally carry no navigable definition identity. Property
and event accessors resolve through their opaque body selector when no physical
method token survives projection.

Coverage lives in `tests/ILInspector.Analysis.Tests/CallGraphProjectionTests.cs`
(edge direction and inversion, duplicates/cycles, node-kind precedence,
deterministic ids and ordering, loop annotations across collapse and inversion,
cross-assembly / generic-recursion-collapse / return-type identity behavior, the
bodiless-target combined view, and the two-different-unsupported-roots rejection)
and in `tests/DotnetInspector.Queries.Tests/MemberCallGraphSessionTests.cs` for
progressive acquisition and bounded cross-library callee neighborhoods.
`tests/DotnetInspect.Cli.Tests/MemberCallGraphSectionTests.cs` covers the CLI section,
its lowerings, and its `--fields` projection.
