# Host-neutral hierarchy projection

## Status

This document is the normative design for **Host-neutral hierarchy
projection**. The shared synchronous request and sink contract, CLI Markout
adapter, and first Type Tree adoption are implemented as focused slices. The
substrate is tracked by
[#9458](https://github.com/richlander/dotnet-inspect/issues/9458).
Type Overview is the first planned product adopter under
[#8430](https://github.com/richlander/dotnet-inspect/issues/8430), whose
counted adoption reaches both the CLI and Inspect Web.

## Owner and exact claim

Host-neutral hierarchy projection owns this exact claim:

> Given one owner-issued hierarchy request and an ordered typed hierarchy,
> synchronously push each node and its child scope to one host-provided sink
> without constructing a second retained tree. The owner retains hierarchy
> membership, sibling order, semantic row selection, and node meaning. The
> host retains text lowering, rendering, destination, and output-failure
> policy.

This owner defines:

- the explicit host-neutral request that selects hierarchy projection;
- the synchronous push contract between one owner and one host sink;
- child-scope nesting and exact last-sibling disclosure;
- sink callback lifetime, exception propagation, and non-retention; and
- the boundary between hierarchy formation and host rendering.

It does not define:

- any owner's node types, hierarchy membership, labels, or ordering;
- Rows, Count, filtering, row windows, continuation, or QuerySpace planning;
- one universal tree Document or serialized hierarchy schema;
- asynchronous acquisition or progressive operation events;
- Markout formatting, Browser interaction, or output destination policy; or
- whether one operation admits Tree, Rows, Count, or another projection.

## Motivation and real asset

`System.Text.Json@10.0.0` provides the motivating production shape. The compact
Type Overview for `System.Text.Json.JsonDocument` contains category nodes,
singleton Member-group leaves, and overload-family leaves such as
`Parse (5 overloads)`. The hierarchy is already represented by the
owner-issued Type and Member-group content. Copying that population into a
second `TreeNode` graph before rendering adds allocation and lets a host
accidentally regroup or reorder semantic rows.

The same issue appears in other bounded hierarchies, but this pattern does not
adopt them. Each owner chooses separately whether hierarchy projection is an
admitted terminal.

## Request boundary

`InspectionHierarchyRequest` is an explicit marker in an owner-issued plan. A
host selects it the same way it selects an admitted Rows or Count request:
before execution, without inferring semantic work from its renderer.

The marker carries no generic tree controls. Row selection, ordering,
continuation, nested Counts, and work bounds remain typed inputs of the
adopting owner. A Type owner may therefore compose one hierarchy request with
its own Member-group row window, while another owner may admit only its
complete hierarchy.

## Sink contract

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
- Callback nesting defines depth; no rendered indentation or prefix crosses
  the boundary.
- A leaf supplies no child callback.
- Producer, formatter, destination, or cancellation exceptions propagate to
  the caller. The hierarchy contract does not manufacture a successful
  receipt after a partial write.

These rules let a sink render incrementally while preserving enough sibling
state for line-art branches, structured nesting, or host-native disclosure.

## Host composition

```text
host gesture
  -> owner-issued plan with InspectionHierarchyRequest
  -> owner executes semantic work and pushes typed nodes
  -> host sink lowers node values
  -> host renderer and destination
```

The push stream is not completed `InspectionEnvelope<TContent>` Content and is
not serializable schema. The adopting operation still owns any completed
Result, Document, Outcome, Share, and diagnostics required by its contract.
The hierarchy is a requested host projection over that operation's semantic
work.

The first CLI adapter targets Markout's current incremental tree writer. It
formats owner-issued node values, maintains ancestor sibling state, and emits
rendered prefixes without retaining a `TreeNode` graph. The prefix is
CLI/Markout implementation detail and never enters the host-neutral contract.
A future structural Markout API may replace that adapter without changing
owners or hosts.

Browser/Wasm may implement a managed sink for a host-native tree or lower the
same owner-issued node values into transport owned by its adopting surface.
Callbacks and sink instances never cross a serialization boundary.

## Failure and resource rules

The owner and sink share one call stack. A callback cannot outlive its
`WriteNode` call, and a sink cannot continue a failed child scope as though the
hierarchy completed. Hosts that require atomic bytes buffer or stage output
under their existing destination owner; this pattern does not add universal
buffering.

The sink must remain NativeAOT- and Browser/Wasm-compatible. The contract uses
typed values and delegates only; it requires no reflection, dynamic code,
thread affinity, retained reader, or host UI object.

## Adoption

The counted production path has five steps:

1. Define and test the host-neutral request and synchronous sink contract.
2. Add a CLI Markout adapter that proves nested, sibling, leaf, and
   exception-unwind behavior without materializing `TreeNode`. Owner-specific
   node formatting retains text-containment responsibility.
3. Have Type Overview select `InspectionHierarchyRequest` explicitly and push
   its Type/category/Member-group node union from the owner while retaining
   Rows and Count as distinct admitted requests. Row-window adoption remains
   with its QuerySpace and Type document owners.
4. Have the CLI pass `--tree` or the native Tree default through that request
   and lower the pushed nodes through `MarkoutHierarchySink<TNode>` rather than
   constructing `TreeNode`.
5. Have the managed Inspect Web Type-document export execute the same Tree
   request into a Browser sink that lowers the owner-issued nodes to the
   serializable member-navigation transport consumed by
   `inspect-web/src/type-panel.ts`. That adoption retires TypeScript grouping
   of Type Overview rows; the Browser retains DOM interaction and rendering.

The Type CLI adoption implements steps 3 and 4 for the native Tree default and
explicit `--tree`: the plan carries `InspectionHierarchyRequest`, the Type
owner pushes category and compact Member-group nodes, and
`MarkoutHierarchySink<TNode>` prints them without a retained Markout
`TreeNode` graph. The first adoption requires complete Rows with nested exact
Member Counts; Count remains a distinct projection on its existing route.

Step 5 is the Inspect Web half of #8430's Type-document host adoption and must
name its focused implementation PR before execution.
