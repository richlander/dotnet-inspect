# Annotated Source invocation destinations

Status: proposed design for
[#5411](https://github.com/richlander/dotnet-inspect/issues/5411).

**Owner:** Research composition of one physical Analysis call occurrence, one
Decompiler-issued C# invocation node, and one CallGraph-owned typed callee.

This design owns only the join that produces an Annotated Source invocation
destination. It consumes rather than redefines:

- physical direct-call evidence and IL offsets from Analysis;
- C# node identity, kind, spans, and IL provenance from the Decompiler;
- typed target identity and physical call-site lookup from
  [Call-graph projection](call-graph-projection.md);
- identity-currency selection from
  [Type, member, and API representation](type-member-api-representation.md);
- selection and named destination behavior from
  [Annotated Source viewer interaction](annotated-source-viewer-interaction.md);
  and
- modal dismissal, history, route availability, and destination focus from
  [Inspect Web UI](inspect-web-ui.md).

It does not add navigation data to the portable `AnnotatedSourceDocument`.
Destination resolution depends on the retained assembly context, so the result
belongs to the Research/browser envelope that accompanies one document.

## Contract

The actor is a user inspecting one exact method body from an already-authorized
assembly context. The input path is:

```text
selected MethodDef
  -> Analysis DirectCall occurrence
  -> CallGraphProjection physical call-site row
  -> Decompiler node provenance
  -> Annotated Source invocation destination
```

Each destination contains:

- one document-relative C# node id; and
- one `CallGraphNode` target, retaining the logical identity selected by the
  call-graph projection and the exact occurrence assembly identity retained by
  that physical call occurrence.

The join is available only when source-document projection is requested.
Requesting destination projection with no source document is invalid. A
successful projection with zero eligible rows is **available and empty**; it is
different from a capability that was not projected.

The browser envelope converts the target to its existing
`BrowserCallGraphTarget` wire currency. It must not reconstruct assembly, type,
member, signature, selector, or version identity from source or display text.
When exactly one loaded surface participant matches the target's assembly
identity, the browser envelope also carries that participant's asset id. This
coordinate-specific routing currency stays at the browser boundary rather than
entering the portable call-graph identity.

## Exact join

For every selected-method `DirectCall` occurrence:

1. Build or reuse the bounded depth-one callee projection for the selected
   method.
2. Resolve the occurrence through the projection's physical call-site lookup.
   The resolved target retains the physical occurrence's exact typed member and
   occurrence assembly identity after structural graph grouping. A missing or
   non-unique target contributes no destination.
3. Collect C# nodes whose product-issued IL provenance contains the occurrence
   offset.
4. Keep only structurally innermost candidates: a candidate is removed when a
   different candidate has spans wholly contained within it.
5. Publish only when exactly one remaining node is an
   `InvocationExpression`. The kind check happens after innermost selection, so
   a property getter inside an invocation argument cannot claim the enclosing
   invocation.
6. Group rows by node id and publish only when every row for that node carries
   one distinct combination of `CallGraphNode.Identity`, definition assembly
   identity, terminal resolution assembly identity, and occurrence assembly
   identity. ECMA-equivalent assembly spellings compare as one identity.

Nested invocations may therefore receive independent destinations. Repeated
call sites may target the same member while retaining different node ids.
Version-distinct references remain distinct because the join retains exact
assembly-reference identity rather than relying on structural member or display
shape. Artifact distinction remains CallGraph-owned and is retained when the
target carries distinct definition evidence; the join does not infer an
artifact from source text.

Indirect calls, object creation, operators, local/synthetic functions without
an exact call-graph target, missing provenance, overlapping non-nested
provenance, and ambiguous node-to-target groups remain selected source with no
destination. The projection never guesses a nearest call, parses C#, or chooses
one target from an ambiguous set.

## Consumer behavior

The modal viewer exposes **Member** and **Source** only after the mapped node is
primary. Selecting source remains inspection-only. The action payload carries
the product-issued destination row identity, not source text.

**Member** requests the resolved member Overview. **Source** requests that
member's Source section. The host may reject either route when the exact target
cannot support it in the current workspace; rejection is visible and never
falls back to the other destination. The modal dismisses when the action is
accepted. A failed transition retains Annotated Source and its history entry,
reports the error on that surface, and returns focus to **Explore**; it does not
reopen the modal. A successful transition commits the requested destination,
then the host owns history and focus there.

## Evidence

The implementation is gated by compiled-product fixtures for:

- local and external direct calls;
- nested invocations;
- property getters inside invocation arguments;
- repeated call sites;
- indirect invocation;
- absent projection and available-empty projection; and
- browser validation that rejects nonexistent, non-C#, non-invocation, or
  duplicate node mappings;
- exact surface-asset routing for dotted assembly names and collision-safe ids
  for graph-only type projection; and
- failed browser transitions retaining Annotated Source, visible error state,
  history, and focus.

The canonical demo selects
`JsonReaderHelper.UnescapeAndCompareBothInputs(...)` inside
`System.Text.Json.JsonElement.DeepEquals`, then opens its explicit **Member**
and **Source** destinations. A neighboring ordinary invocation exercises the
same path.
