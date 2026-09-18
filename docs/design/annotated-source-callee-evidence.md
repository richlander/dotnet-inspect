# Annotated Source callee evidence

**Owner:** Annotated Source callee-evidence composition.

## Claim

Annotated Source keeps a callee Finding attached to the caller invocation while
showing instruction evidence only at the exact product-issued source node in
the physical callee method. The browser transport serializes each physical
callee document once and bounds the aggregate document payload without hiding
the affected Finding.

The composition joins three owner-issued currencies:

- the Research Finding census receipt and instance key identify the exact
  caller-side Finding;
- `ResearchEvidenceLocation` identifies the physical callee method and IL
  instruction; and
- `AnnotatedSourceNode.Provenance.IlOffsets` identifies the corresponding
  source node in the callee document.

No display string, descriptor, caller source offset, or array position is an
identity or correspondence key.

## Motivation

The production case is
[`System.Text.Json.JsonElement.DeepEquals`](https://github.com/dotnet/runtime/blob/8970fe8a2fb3301d03a9de8e75892fb31b49291d/src/libraries/System.Text.Json/src/System/Text/Json/Document/JsonElement.cs#L1258-L1291),
which calls
[`JsonReaderHelper.UnescapeAndCompareBothInputs`](https://github.com/dotnet/runtime/blob/8970fe8a2fb3301d03a9de8e75892fb31b49291d/src/libraries/System.Text.Json/src/System/Text/Json/Reader/JsonReaderHelper.Unescaping.cs#L162-L204).
The relationship is visible at the caller invocation, while the unsafe
implementation evidence is the callee's `stackalloc` source. Showing the caller
line as the unsafe evidence would be false.

The closed #4448 prototype demonstrated the intended interaction and real
Firefox rendering. This design retains that evidence but replaces its
descriptor-plus-offset association with the subsequently introduced
producer-issued Finding identity.

## Contract

The assembly-context member projection supports callee evidence only when the
same operation requests Facts rows and an Annotated Source document. For each
instruction-level `semantics.callee` or `safety.callee` row, it:

1. retains the row's non-default Finding instance key;
2. admits every evidence location against the complete Research-issued callee
   `MethodIdentity`;
3. projects that exact MethodDef as a separate `AnnotatedSourceDocument`; and
4. maps each IL offset to exactly one C# node of the required kind.

| Evidence | Required C# node |
| --- | --- |
| exception construction | `ObjectCreationExpression` |
| `localloc` | `StackAllocationExpression` |
| `calli` | `IndirectInvocationExpression` |

The node's product-issued IL provenance must contain the evidence offset. Zero
or multiple matching nodes is a correspondence failure, not a partial success.
The Source facade and TypeScript viewer each repeat that correspondence check
against the serialized document and require the transported node-id set to
equal the exact matches. This prevents serialization or adapter drift from
turning a Research-proven location into a different displayed source node.
The wire sequence is the ascending distinct representation of that set; it
does not inherit evidence-coordinate or source-emission order.
An unavailable row that retains a diagnostic document is valid only while the
same recomputation still reports zero or multiple matching nodes.
An instruction-unavailable Finding, callee document failure, or correspondence
failure retains the callee member target and a typed visible reason but no
evidence node ids.

The browser transport carries both the document-local fact id and the
producer-issued instance key. The Finding census adapter verifies that this
pair is exactly the pair in the operation's source sidecar. The receipt at the
census root scopes the key. The browser repeats structural validation before
rendering.

Finding detail labels the caller nodes as relationship targets and the separate
snippet as callee evidence. The callee member has the same typed **Member** and
**Source** navigation actions as invocation destinations. Coordinate
disclosure shows the evidence kind and method-relative IL offset. Missing
evidence is visible and never falls back to caller source.

The typed navigation target is constructed from the same admitted complete
callee `MethodIdentity`; it is the wire projection of that identity, not a
second correspondence key. The Source facade requires a complete MethodDef
target, including its product-issued selector, before transport. The browser
consumes only the same-process facade output, so this slice does not duplicate
the callee identity solely to compare one trusted projection with another.

### Shared document table and budget

The Source facade groups projected callee documents by the complete
Research-issued `MethodIdentity`, including assembly name, module version id,
declaring type, signature, and MethodDef token. Display member text, navigation
text, document contents, and caller Finding identity are not deduplication
keys. Two Findings for the same physical method share one operation-local
integer `documentId`; two physically distinct methods remain distinct even
when their display text and source text match.

`annotatedSource.findingEvidenceDocuments` carries each admitted compact
`AnnotatedSourceDocument` once. Each `findingEvidence` row retains its own
fact id, instance key, member target, coordinates, node ids, and unavailable
reason, and carries only its optional `documentId`. First occurrence of each
complete method identity defines deterministic table order. Repeated
projections for one identity must serialize to the same document or the
managed operation fails visibly. The managed and TypeScript adapters require
unique non-negative document ids, valid references, and no unreferenced table
entries.

The table admits at most 8,388,608 compact JSON characters across unique
callee documents. This reserves half of the ordinary worker's 16,777,216
character JSON ceiling for the caller document, Finding rows, typed targets,
and envelope overhead. Documents are considered in deterministic first-use
order. A document that would exceed the aggregate allowance is not entered in
the table. Every referencing row retains its Finding identity, target, and
coordinates, clears its node ids and document id, and carries a visible budget
reason in addition to any pre-existing unavailable reason. Later smaller
documents may still be admitted. The ordinary worker remains the owner of the
final whole-message limit.

## Boundaries

This slice covers instruction-level `semantics.callee` and `safety.callee`.
Method-level aggregate `cost.callee` evidence remains #4642 because it has no
truthful singular source node.

Research owns the evidence subject and locations. Decompiler owns document
construction, node kinds, and IL provenance. The assembly-context query owns
the typed join. The Source facade owns the census wire association. The viewer
owns validation, disclosure, and navigation presentation.

## Gates

Release tests cover:

- compiled `stackalloc`, `calli`, and exception-construction callees;
- complete method-identity admission rather than token-only matching;
- zero and ambiguous node correspondence as visible failure;
- unavailable instruction and source-document outcomes;
- exact fact-id and instance-key transport association;
- browser rejection of malformed callee documents, node ids, and identities;
- complete-method-identity deduplication rather than display-text aliasing;
- one shared serialized document across repeated Finding rows;
- visible aggregate document-budget exhaustion;
- bounded stress showing that the document portion grows with unique physical
  callees rather than repeated call sites; and
- detail rendering, coordinate disclosure, and typed Member/Source actions.

The real `System.Text.Json` scenario is the production demonstration.

## Non-claims

- No change to Finding production or caller anchoring.
- No browser inference from C# text, display signatures, or descriptors.
- No cross-assembly callee acquisition beyond the Research assembly context.
- No aggregate-cost source line.
- No replacement of the ordinary worker's whole-message admission limit.
