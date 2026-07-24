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
but not both, and when both are supplied they must name the same member.

The projection owns everything a host must not re-invent in JavaScript:

- **Edge direction.** Caller-tree edges point child → parent (a caller flows
  *into* the target); callee-tree edges point parent → child (the target flows
  *out* to a callee).
- **Stable node identity.** Members are keyed on fully-qualified declaring type,
  name, method type arguments, and parameter types, so shared callees, cycles,
  and the target-as-both-caller-and-callee collapse to one node; overloads and
  distinct generic instantiations stay separate.
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
  break out of the label or the flowchart grammar.

## Consumers

The browser engine is handed this exact Mermaid document and only asks Mermaid
to convert it to SVG; it reconstructs no graph identity, direction, truncation,
cycles, or labels. The CLI keeps its existing per-section tree rendering for
now; the projection is the shared artifact a future combined `--mermaid`
call-graph view and the browser prototype adopt.

Coverage lives in
`src/ILInspector.Analysis.Tests/CallGraphMermaidTests.cs` (edge direction,
escaping, duplicates/cycles, external/already-shown/depth-limited/truncated
statuses, deterministic ids, loop annotations, and an exact combined
caller/target/callee document).
