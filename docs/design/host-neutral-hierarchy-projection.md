# Host-neutral hierarchy projection

## Status

This document is the normative design for **Host-neutral hierarchy
projection**. The substrate is tracked by
[#9458](https://github.com/richlander/dotnet-inspect/issues/9458).
Type Overview and shared presentation are the first adopters under
[#8430](https://github.com/richlander/dotnet-inspect/issues/8430). The CLI
adopts the compact profile; Inspect Web adoption remains separate.

The first shared presentation profile is the compact Type hierarchy:

```text
Type (FullSpelling)
  -> categories (Rows, Name)
    -> MemberGroups (Rows, Name)
      -> exact Members (Count)
```

It can be lowered as Tree or Mermaid without changing the semantic request.
An expanded Type-to-MemberGroup-to-exact-Member Rows profile waits for the
complete `TypeDocument`; this design does not manufacture exact-Member rows
from compact Counts.

## Owner and exact claim

Host-neutral hierarchy projection owns this exact claim:

> One hierarchy request independently selects an owner-typed topology, Rows
> or Count for each parent-to-child population, and Name or FullSpelling for
> each emitted node. Shared presentation independently selects the final
> format and synchronously lowers the owner-issued hierarchy to a destination
> without constructing a second retained tree.

This owner defines:

- the separation among topology, population terminal, node spelling, and
  presentation format;
- the recursive Rows-or-Count request shape used inside an owner-typed
  topology request;
- the synchronous push contract between an owner and shared presentation;
- child-scope nesting and exact last-sibling disclosure;
- sink callback lifetime, exception propagation, and non-retention; and
- the ownership boundaries among document/query owner, presentation owner,
  and transport host.

It does not define:

- any adopting owner's subjects, relations, membership, ordering, or
  population bindings;
- QuerySpace filtering, ordering, terminal, continuation, or execution
  semantics;
- one universal hierarchy Document or serialized hierarchy schema;
- asynchronous acquisition or progressive operation events;
- CLI grammar, Browser interaction, or destination lifetime; or
- unsupported topology, spelling, terminal, or format combinations for an
  adopting owner.

## Motivation and real asset

`System.Text.Json@10.0.0` provides the motivating production shape.
`System.Text.Json.JsonSerializer` has many logical Member families, including
`DeserializeAsync`, whose exact overloads can be counted in the compact Type
overview and enumerated by the complete Type document.

Tree and Mermaid presentation, plus eventual CLI and Browser consumers, need
the same identities and edges. If each host discovers Member families,
recounts overloads, or constructs its own hierarchy, topology and cost drift
by host. If rendering first copies the owner-issued hierarchy into Markout
`TreeNode` values, large hierarchies pay for a second retained graph before
the first output byte.

The shared pattern therefore keeps semantic work in the owner and format
lowering in shared presentation while preserving streaming delivery.

## Four independent request dimensions

### Typed topology

The adopting owner defines the legal subject relations and names the topology
with an owner-typed value. The generic substrate does not identify nodes with
strings or infer relationships from display text.

Examples include:

```text
Type -> Category -> MemberGroup -> exact Member
Type -> MemberGroup -> exact Member
```

The Type document family owner decides which topology is admitted by an
overview or complete document. A presentation format does not add, remove,
regroup, or reorder semantic nodes.

### Rows or Count

Every parent-to-child population closes independently as Rows or Count.

- **Rows** emits child nodes and may carry another child-population request.
- **Count** emits only the exact cardinality for that population and ends that
  branch.

Count does not require or imply Rows. Rows do not become Count by host-side
enumeration. The adopting owner maps each requested closing to its existing
typed population request and publishes the corresponding outcome.

### Name or FullSpelling

Every emitted node independently requests either:

- **Name**, the compact owner-issued name for that subject; or
- **FullSpelling**, the owner-issued complete spelling admitted by the
  selected document.

Spelling is data, not identity. A document rejects a spelling it cannot
produce rather than asking presentation to reconstruct it. In particular,
compact Type Overview MemberGroup rows admit Name; exact-Member
FullSpelling belongs to the complete Type document.

### Final format

Tree, Mermaid, Browser transport, and later formats are lowerings over the
same completed semantic plan. Format selection does not change topology,
terminal, spelling, filtering, or ordering.

Markout is a private implementation detail of shared presentation. A CLI or
Browser caller selects an admitted format or profile and supplies a
destination; it does not reference Markout, build `TreeNode`, format
owner-issued nodes, or discover hierarchy edges.

## Request boundary

The generic request vocabulary is recursive:

```text
HierarchyRequest<TTopology>
  Topology
  RootSpelling
  Children

HierarchyPopulationRequest
  Count
  Rows
    ChildSpelling
    Children?
```

The owner-typed topology gives each level semantic meaning. The recursive
population request only supplies the orthogonal terminal and spelling choices.
An invalid combination is rejected before acquisition or output begins.

The hierarchy request is carried in the owner-issued plan beside the typed
population requests that perform its work. A host selects it before execution,
the same way it selects an admitted Rows or Count request. No renderer may
silently widen the plan after execution.

## Ownership and composition

The three roles are:

- **Document/query owner:** defines subjects, legal topology, population
  membership and order, bindings, terminal outcomes, identities, and admitted
  spellings.
- **Shared presentation owner:** maps an admitted presentation profile to the
  owner request, formats owner-issued values, escapes syntax, assigns
  format-local node identifiers, and lowers to Tree, Mermaid, or another
  format.
- **Transport host:** selects a format/profile, supplies the destination and
  operation policy, and reports destination failure.

The composition is:

```text
host format/profile gesture
  -> shared presentation plan
  -> owner-issued hierarchy and population requests
  -> owner executes semantic work
  -> shared presentation streams one selected lowering
  -> host destination
```

The hierarchy push stream is not completed
`InspectionEnvelope<TContent>` Content and is not a universal wire schema.
The adopting operation still owns its completed Document, Outcome, Share, and
diagnostics. A Browser adoption may lower the same owner-issued hierarchy into
an owner-approved serializable transport, but callbacks and sinks do not cross
that boundary.

## Streaming sink contract

An adopting owner pushes its typed node union through
`IInspectionHierarchySink<TNode>`:

```csharp
sink.WriteNode(
    category,
    isLastSibling: true,
    children =>
    {
        children.WriteNode(firstMember, isLastSibling: false);
        children.WriteNode(lastMember, isLastSibling: true);
    });
```

The contract is synchronous:

- `WriteNode` receives one node in owner-issued sibling order.
- `isLastSibling` is exact within the current child scope.
- A non-null child callback writes only that node's direct children.
- The sink invokes a non-null child callback exactly once, synchronously,
  before `WriteNode` returns.
- The sink does not retain the node or callback after return.
- Callback nesting defines depth; no rendered indentation, syntax, or prefix
  crosses the boundary.
- A leaf supplies no child callback.
- Producer, formatter, destination, or cancellation exceptions propagate to
  the caller. A partial write does not become a successful receipt.

These rules permit incremental Tree prefixes, Mermaid node-and-edge emission,
or host-native disclosure without retaining a second hierarchy.

## Failure and resource rules

The owner and sink share one call stack. A callback cannot outlive its
`WriteNode` call, and a sink cannot continue a failed child scope as though the
hierarchy completed. Hosts that require atomic bytes buffer or stage output
under their destination owner; this pattern adds no universal buffering.

The request and sink remain NativeAOT- and Browser/Wasm-compatible. They use
typed values and delegates only and require no reflection, dynamic code,
thread affinity, retained reader, inspected assembly load, or host UI object.

## Type Overview first adoption

The compact Type Overview profile requests:

```text
topology: TypeCategoriesAndMemberGroups
root: FullSpelling
children: Rows(Name)          # categories
  children: Rows(Name)        # MemberGroups
    children: Count           # exact Members
```

The Type owner requires complete MemberGroup Rows with exact-Member Counts,
then pushes category and MemberGroup nodes in owner-issued order. It rejects
unavailable, partial, uncounted, or unsupported requests before presentation.

Shared presentation owns both initial lowerings:

- Tree writes the Type spelling and streams category and MemberGroup nodes
  through Markout without constructing `TreeNode`.
- Mermaid writes the same semantic nodes and edges with format-local stable
  identifiers and Mermaid escaping.

The shared presentation API accepts only Tree or Mermaid intent,
accessibility policy, the completed owner document, and the destination. The
CLI exact-Type hierarchy route adopts this profile without owning Markout,
node formatting, or hierarchy formation. Inspect Web may request either format or
a later Browser transport without duplicating hierarchy formation.

The expanded Mermaid example
`JsonSerializer -> DeserializeAsync -> exact DeserializeAsync overloads`
requires exact-Member Rows and FullSpelling from the complete `TypeDocument`.
That adoption changes the Type document request, not the Mermaid lowerer or
CLI hierarchy logic.

## Required evidence

The first adoption proves:

- topology, per-edge terminal, per-node spelling, and output format are
  independently represented;
- the Type owner rejects request shapes its compact document cannot satisfy;
- Tree and Mermaid lower the same owner-issued node sequence and identities;
- Count branches do not materialize exact-Member Rows;
- Tree streaming constructs no Markout `TreeNode`;
- the shared presentation path contains no transport-host routing or
  destination policy;
- canonical metadata arity is removed while noncanonical backticks remain;
- destination and formatter failures remain visible; and
- the implementation builds and runs on the existing NativeAOT- and
  Browser/Wasm-compatible product path.
