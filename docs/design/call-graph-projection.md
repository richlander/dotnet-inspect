# Call-graph projection

How typed call-graph facts become one deterministic, format-neutral node/edge
graph that any host — `dotnet-inspect` today, the browser-Wasm prototype next —
can render without re-deriving graph semantics (issues #3120, #3291, #3280).

Related docs:

- [Inspection graph document](inspection-graph-document.md) — the typed
  multi-subject envelope that composes call topology with package, integration,
  Finding, and other relationship evidence
- [Call-graph characteristics](call-graph-characteristics.md) — the
  call-specific adapter from current nodes, edges, occurrences, and signals
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
the bounded traversal (`LibraryBodyIndex.BuildCallerTree` /
`BuildCallTree`). `ILInspector.CallGraph` turns those `CallTreeNode` roots into a
deterministic node/edge set. It knows nothing about Mermaid, Markdown, tables, or
any other format, takes no dependency on Markout, the CLI, or inspected-assembly
loading, and stays SRM-only, NativeAOT-friendly, and browser-Wasm compatible.

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

An opt-in `OwnershipFlow` body producer adds `ArrayPool<T>` ownership paths to
that same overlay without changing graph acquisition. Analysis computes compact
per-method summaries while each selected body is already decoded: rent origins,
array-parameter effects, physical forwarding calls, pool returns, field stores,
and array returns to the caller. It reuses the body's existing
`MethodInstructions` for reaching definitions and retains no IL, blocks, or
dataflow state. A calls/signature discriminator skips reaching definitions for
bodies with neither a rent nor an array parameter.
`MemberCallGraphView.OwnershipEvidence` carries those summaries from the same
indexes that produced the current graph tier; Research joins forwarding calls
to stable edge rows and performs no body, graph, or source acquisition.

The first body-scoped tier can therefore expose a rent and its forwarding edge
before the callee body is available. A later full tier supersedes the scoped
index and completes the path from already-retained summaries. Terminal
`Finding<ArrayPoolOwnershipPathWitness>` payloads distinguish `ReturnedToPool`,
`Stored`, and `ReturnedToCaller`, and retain every physical forwarding
coordinate even when repeated call sites collapse onto one logical edge row.
Finding identity uses those physical coordinates plus the typed sink identity,
not labels or row numbers alone.

Ownership completeness remains separate from positive Findings:
`NotRequested`, `TraversalBoundary`, `IncompleteCorrespondence`,
`BodyUnavailable`, `AnalysisFailure`, `WitnessBudget`, and `PathBudget` are
independent flags. Address-taken, local-alias, unsupported-stack, unresolved,
or failed body evidence is incomplete rather than a safe outcome. A catalog
node without a matching physical definition never borrows a structurally
similar body from another image. The feature is not in
`MemberCallGraphOptions`' default producer set, so callers pay the reaching-
definitions and retained-summary cost only when they request ownership flow.

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

Coverage lives in `src/ILInspector.Analysis.Tests/CallGraphProjectionTests.cs`
(edge direction and inversion, duplicates/cycles, node-kind precedence,
deterministic ids and ordering, loop annotations across collapse and inversion,
cross-assembly / generic-recursion-collapse / return-type identity behavior, the
bodiless-target combined view, and the two-different-unsupported-roots rejection)
and in `src/DotnetInspector.Queries.Tests/MemberCallGraphSessionTests.cs` for
progressive acquisition and bounded cross-library callee neighborhoods.
`src/dotnet-inspect.Tests/MemberCallGraphSectionTests.cs` covers the CLI section,
its lowerings, and its `--fields` projection.
