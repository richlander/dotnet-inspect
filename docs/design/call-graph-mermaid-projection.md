# Call-graph Mermaid projection

How typed call-graph facts become one deterministic Mermaid document that any
host — `dotnet-inspect` today, the browser-Wasm prototype next — can render
without re-deriving graph semantics (issue #3120).

Related docs:

- [Graph signal annotations](graph-signal-annotations.md) — the per-node
  perf/kind-of-work cues the CLI projects onto the same call trees
- [Output shapes](output-shapes.md) — the projection/shape model the CLI uses

## Layering

```text
ILInspector.Analysis            typed CallTreeNode facts + bounded traversal
        ↓ CallTreeNode roots
ILInspector.CallGraph           host-neutral projection → Mermaid document
        ↓ deterministic flowchart text
dotnet-inspect        Browser Wasm
```

`ILInspector.Analysis` stays presentation-free: it owns the graph evidence and
the bounded traversal (`LibraryBodyIndex.BuildCallerTree` /
`BuildCallTree`) but knows nothing about Mermaid. `ILInspector.CallGraph` is a
focused new project that turns those `CallTreeNode` roots into text. It takes no
dependency on Markout, the CLI, or inspected-assembly loading and stays SRM-only,
NativeAOT-friendly, and browser-Wasm compatible.

## What the projection owns

`CallGraphMermaid.Render(callerRoot, calleeRoot)` produces one `flowchart LR`
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
- **Deterministic ids/ordering.** The target is `n0`; remaining ids are assigned
  in first-seen order over a caller depth-first walk, then a callee walk. Nodes
  are declared in id order and edges in first-seen order, so the same input
  always yields byte-identical output.
- **Cycles and duplicates.** The bounded tree marks re-encountered members
  `AlreadyShown`; the projection collapses them onto the existing node and still
  draws the edge, so a cycle `A → B → A` renders as two edges between two nodes.
- **Boundary and external styling.** `External` callees get a dashed `external`
  class; `DepthLimited` / `Truncated` nodes get a `truncated` class marking
  "more beyond here". An occurrence expanded elsewhere outranks a boundary
  occurrence of the same member, so a shared node is never mislabelled a dead
  end. `classDef` blocks are emitted only for classes actually used.
- **Loop-call annotations.** A call made inside a loop labels its edge (`-->|loop|`
  outbound, `-->|loop call|` inbound), read from the child node's loop flag.
- **Mermaid-safe escaping.** Hostile or unusual member names (quotes, angle
  brackets, pipes, `#`) are escaped with Mermaid entity codes so they cannot
  break out of the label or the flowchart grammar. Edge labels are unquoted, so
  they additionally entity-encode the structural delimiters (`()[]{}`) that would
  otherwise corrupt an edge label.

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
`CallGraphMermaid.Render(CallerRoot, CalleeRoot)` — "with or without mermaid."

**No duplicated work.** At most two target-assembly indexes are ever built — the
scoped single-body build and the full build — plus one build per cross-library
package, and each is built once and reused for callees, callers, and any Mermaid
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

The browser engine is handed this exact Mermaid document and only asks Mermaid
to convert it to SVG; it reconstructs no graph identity, direction, truncation,
cycles, or labels. The CLI keeps its existing per-section tree rendering for
now; the projection is the shared artifact a future combined `--mermaid`
call-graph view and the browser prototype adopt.

Coverage lives in
`src/ILInspector.Analysis.Tests/CallGraphMermaidTests.cs` (edge direction,
escaping, edge-label structural encoding, duplicates/cycles,
external/already-shown/depth-limited/truncated statuses, deterministic ids, loop
annotations, cross-assembly / generic-recursion-collapse / return-type identity
behavior, the bodiless-target combined view, the two-different-unsupported-roots
rejection, and an exact combined caller/target/callee document).
