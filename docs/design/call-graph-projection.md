# Call-graph projection

How typed call-graph facts become one deterministic, format-neutral node/edge
graph that any host — `dotnet-inspect` today, the browser-Wasm prototype next —
can render without re-deriving graph semantics (issues #3120, #3291, #3280).

Related docs:

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
- **Stable node identity.** Members are keyed with the Analysis layer's
  erased-identity convention (`GenericMemberIdentity`): the assembly-qualified
  `KeyFragment` of the *open* declaring type, the member name, the open parameter
  count, the erased/open parameter shape, and the open return type. This is the
  same key the builders compute, so the open definition side (caller tree, generic
  root) and the constructed call-site side (callee tree MethodSpec) agree. A
  generic method therefore keeps one identity across recursion and across distinct
  instantiations — they collapse onto one node with a self-loop rather than
  splitting into same-named twins. Following that convention, same-name generic
  overloads that differ only by arity coarsen together (the accepted coarsening
  `GenericMemberIdentity` documents), while overloads that differ by parameter
  types or by return type (C# conversion operators) and same-namespace/same-name
  types from *different assemblies* all stay separate. Shared callees, cycles, and
  the target-as-both-caller-and-callee collapse to one node.
- **Correspondence migration boundary.** The additive
  `CatalogMemberCorrespondencePlan` traverses an open signature once, exposes
  its distinct frozen-context requests, and can project a generation-scoped
  `CatalogMemberJoinKey` that includes member kind, canonical signature header,
  method generic arity, instance/static shape, and recursively resolved named
  types. The current builders and this projection do not consume that key yet;
  `CallerGraphKey`/`IdentityKey` remain the active behavior until graph
  storage, cache lifetime, and incomplete-edge evidence migrate together.
- **Deterministic ids/ordering.** The focus is id `0`; remaining ids are assigned
  in first-seen order over a caller depth-first walk, then a callee walk. Nodes
  are emitted in id order and edges in first-seen order, so the same input
  always yields an identical projection.
- **Cycles and duplicates.** The bounded tree marks re-encountered members
  `AlreadyShown`; the projection collapses them onto the existing node and still
  records the edge, so a cycle `A → B → A` is two edges between two nodes.
- **Boundary and external classification.** `External` callees carry
  `CallGraphNodeKind.External`; `DepthLimited` / `Truncated` nodes carry
  `Truncated`, meaning "more beyond here". An occurrence expanded elsewhere
  outranks a boundary occurrence of the same member, so a shared node is never
  misclassified as a dead end. How a host *shows* those kinds is the host's
  choice — the CLI groups external nodes and suffixes their labels; the browser
  styles them with a CSS class.
- **Loop-call annotations.** A call made inside a loop labels its edge (`loop`
  outbound, `loop call` inbound), read from the child node's loop flag.
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
separate concern (issue #3266). `dotnet-inspect` owns a
`ProgressiveMemberCallGraph` seam
(`src/dotnet-inspect/Inspectors/ProgressiveMemberCallGraph.cs`) that serves one
member's graph in three cumulative layers, cheapest first, so a host can paint
the outbound half immediately and fill in the expensive tiers as they land:

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
`MemberCallGraphView` (`Tier`, `CalleeRoot`, `CallerRoot`), so a host renders
them with its own per-section tree rendering *or* projects them with
`CallGraphProjection.Create(CallerRoot, CalleeRoot)` — "with or without mermaid."

**No duplicated work.** At most two target-assembly indexes are ever built — the
scoped single-body build and the full build — plus one build per cross-library
package, and each is built once and reused for callees, callers, and any
projection. The scoped build exists only for the progressive first paint: a
consumer that wants the whole graph calls `Callers()` or `CrossLibrary()`
directly and pays exactly one full build, with callees derived for free from it.
Once the full build lands it supersedes the scoped one, which is never rebuilt.
The `IndexBuildGuard`-collected seam tests assert these build counts through
`MethodBodyInspectionSession.OpenCountForTests`.

Drive it by pull (`Callees()` / `Callers()` / `CrossLibrary()`, or the lazy
`Tiers()` stream) or by push (`RunAsync` raising `LayerReady` per layer then
`Completed`). The push path is a thin wrapper over the same memoized pull core,
so the two never double the work. The forward cross-library expansion is the
callee mirror of `BuildCallerTree(scopes)`: `LibraryBodyIndex.BuildCallTree(token,
calleeScopes, …)` builds a structural forward map keyed by the same erased
identity the caller builder uses, tagging each boundary-crossing callee with its
source assembly.

## Consumers

`dotnet-inspect` renders one bidirectional `Call Graph` section from the
projection. `CallGraphSectionAdapter` lowers it to a Markout `Graph`, and Markout
picks the lowering the sink can express: a tree in Markdown and plain text, an
edge table under `--table`/`--tsv`/`--jsonl`, and a flowchart in Mermaid. The
adapter is the only place that knows call-graph vocabulary; the section is a
graph, not a pre-rendered tree, which is what lets one model serve every sink.

The browser engine consumes the same projection and generates its own Mermaid; it
reconstructs no graph identity, direction, truncation, cycles, or labels. The CLI
and the browser deliberately do not share a Mermaid generator — sharing the
*graph* is the point, not sharing the *format*.

Coverage lives in `src/ILInspector.Analysis.Tests/CallGraphProjectionTests.cs`
(edge direction and inversion, duplicates/cycles, node-kind precedence,
deterministic ids and ordering, loop annotations across collapse and inversion,
cross-assembly / generic-recursion-collapse / return-type identity behavior, the
bodiless-target combined view, and the two-different-unsupported-roots rejection)
and in `src/dotnet-inspect.Tests/MemberCallGraphSectionTests.cs` for the CLI
section, its lowerings, and its `--fields` projection.
