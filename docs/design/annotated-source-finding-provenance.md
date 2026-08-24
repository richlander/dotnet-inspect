# Annotated Source Finding provenance

> **Map:** This document owns the browser-facing provenance contract for every
> descriptor that can reach an `AnnotatedSourceDocument`. It complements
> [Hidden-Fact Annotations](hidden-fact-annotations.md), which owns producer
> semantics, and [Finding Coordinates](finding-coordinates.md), which separates
> native provenance from correspondence.

Annotated Source shows a caller relationship where a producer observed it, and
shows evidence where that evidence physically exists. Those are often the same
member, but a callee descriptor deliberately crosses that boundary. A browser
peek must never turn the caller's relationship location into a plausible
substitute for missing remote evidence.

## Rules

- Distinguish the caller relationship location from the evidence subject.
  `source_offset` on a remote descriptor remains the caller's relationship
  coordinate; the remote payload retains the evidence member and physical body
  coordinates separately.
- Pair every IL offset with its physical method body. A numeric offset alone is
  not a cross-member coordinate.
- Acquire remote source only through producer-owned typed identities and
  coordinates, then map it only to product-issued `AnnotatedSourceNode` values
  through `AnnotatedSourceNode.Provenance.IlOffsets`. The browser does not parse
  source or infer a remote node.
- Show a visible source or correspondence failure rather than falling back to
  caller code for a remote descriptor.
- Create focused issues and tracker rows for decompiler gaps. The current
  blockers are **none**: `ThrowStatement`, `StackAllocationExpression`, and
  `IndirectInvocationExpression` provide the necessary remote mappings. The
  three `MemberProjection_CarriesCallee*SourceFor*Finding` tests gate those
  mappings, and
  `StackAllocSpanPassTests.CorelibSpanDirectStackalloc_RetainsLocallocProvenance`
  gates the raised Span form that real framework libraries use.

The browser payload retains the remote member target and the individual physical
coordinates; payload deduplication is tracked by [#4640]. Search/corpus currency
for this audit is tracked by [#4637], and any future decompiler mapping gap is
tracked through [#4643].

## Audited descriptor catalog

`AnnotatedSourceFindingProvenanceCatalogTests.DocumentedDescriptorCatalog_EqualsEveryReachableAnnotatedSourceProducer`
is the named set-equality gate for this table. Its test-owned typed audit set is
compared to exact descriptor ids exposed by both reachable
`ResearchFactRegistry` profiles, so an undocumented new descriptor and a stale
row both fail.
`ResearchFactRegistryTests.Registry_RejectsDescriptorsNotDeclaredByTheirProducer`
makes that producer declaration non-vacuous by rejecting any emitted descriptor
missing from the catalog. Markdown is not parsed as a source of truth.

| Descriptor | Producer | Evidence scope and retained typed data | Required node/provenance | Fidelity and browser behavior | Blocker / issue |
| --- | --- | --- | --- | --- | --- |
| `alloc.box` | `AllocationOccurrenceFactProducer` | Local: `AllocationOccurrence` physical method and IL offset | Product-issued local target; `IlOffsets` contains the occurrence offset | Full: local target/caret and local evidence code | None |
| `alloc.array` | `AllocationOccurrenceFactProducer` | Local: `AllocationOccurrence` physical method and IL offset | Product-issued local target; `IlOffsets` contains the occurrence offset | Full: local target/caret and local evidence code | None |
| `alloc.new` | `AllocationOccurrenceFactProducer` | Local: `AllocationOccurrence` physical method and IL offset | Product-issued local target; `IlOffsets` contains the occurrence offset | Full: local target/caret and local evidence code | None |
| `alloc.closure` | `AllocationOccurrenceFactProducer` | Local: `AllocationOccurrence` physical method and IL offset | Product-issued local target; `IlOffsets` contains the occurrence offset | Full: local target/caret and local evidence code | None |
| `alloc.statemachine` | `AllocationOccurrenceFactProducer` | Local: `AllocationOccurrence` physical method and IL offset | Product-issued local target; `IlOffsets` contains the occurrence offset | Full: local target/caret and local evidence code | None |
| `alloc.delegate` | `AllocationOccurrenceFactProducer` | Local: `AllocationOccurrence` physical method and IL offset | Product-issued local target; `IlOffsets` contains the occurrence offset | Full: local target/caret and local evidence code | None |
| `alloc.enumerator` | `AllocationOccurrenceFactProducer` | Local: `AllocationOccurrence` physical method and IL offset | Product-issued local target; `IlOffsets` contains the occurrence offset | Full: local target/caret and local evidence code | None |
| `unsafe.deref` | `UnsafetyOccurrenceFactProducer` | Local: `UnsafetyOccurrence` physical method and IL offset | Product-issued local target; `IlOffsets` contains the occurrence offset | Full: local target/caret and local evidence code | None |
| `unsafe.stackalloc` | `UnsafetyOccurrenceFactProducer` | Local: `UnsafetyOccurrence` physical method and `localloc` offset | Product-issued local target; `IlOffsets` contains `localloc` | Full: local target/caret and local evidence code | None |
| `unsafe.calli` | `UnsafetyOccurrenceFactProducer` | Local: `UnsafetyOccurrence` physical method and `calli` offset | Product-issued local target; `IlOffsets` contains `calli` | Full: local target/caret and local evidence code | None |
| `lifetime.ref-return` | `DecompilerLifetimeFactProducer` | Local: `Return` IR node and physical `ret` offset | Product-issued local target; `IlOffsets` contains `ret` | Full: local target/caret and local evidence code | None |
| `lifetime.stack-bound` | `DecompilerLifetimeFactProducer` | Local: stack-bound `NewObject` IR node and physical offset | Product-issued local target; `IlOffsets` contains the construction offset | Full: local target/caret and local evidence code | None |
| `lifetime.ref-struct-return` | `DecompilerLifetimeFactProducer` | Local: `Return` IR node and physical `ret` offset | Product-issued local target; `IlOffsets` contains `ret` | Full: local target/caret and local evidence code | None |
| `lifetime.pointer-return` | `DecompilerLifetimeFactProducer` | Local: `Return` IR node and physical `ret` offset | Product-issued local target; `IlOffsets` contains `ret` | Full: local target/caret and local evidence code | None |
| `lifetime.stack-escape` | `DecompilerLifetimeFactProducer` | Local: `Return` IR node and physical `ret` offset | Product-issued local target; `IlOffsets` contains `ret` | Full: local target/caret and local evidence code | None |
| `call.edge` | `DirectCallFactProducer` | Local: `DirectCall.EvidenceMethod` and call IL offset | Product-issued local target; `IlOffsets` contains the call offset | Full: local target/caret and local evidence code | None |
| `cost.method` | `MethodHeaderLeverageFactProducer` | Header aggregate: `MethodLeverage` has no singular body offset | No body node; intentionally unanchored header fact | Full as header/unanchored disclosure; no invented code line | None |
| `semantics.callee` | `CallSiteSemanticsFactProducer` | Remote full: resolved callee `MethodIdentity` plus `CallSiteEvidenceCoordinate` physical method, exception-construction offset, and kind | `ThrowStatement` whose `IlOffsets` contains the retained construction coordinate | Full: qualified callee member, copy/navigate actions, exact callee throw code; failure is visible | None |
| `safety.callee` | `CallSiteSemanticsFactProducer` | Remote full: resolved callee `MethodIdentity` plus `CallSiteEvidenceCoordinate` physical method, `localloc`/`calli` offset, and kind | `StackAllocationExpression` for `localloc`; `IndirectInvocationExpression` for `calli`; matching `IlOffsets` | Full: qualified callee member, copy/navigate actions, exact callee code; failure is visible, never caller fallback | [#4641] |
| `cost.callee` | `CallSiteCostFactProducer` | Remote partial/blocked: aggregate callee signals and caller relationship offset; no honest singular source coordinate | None: an aggregate cannot select one truthful line | Blocked: disclose the caller relationship only; do not fabricate a callee peek | [#4642] |

[#4637]: https://github.com/richlander/dotnet-inspect/issues/4637
[#4640]: https://github.com/richlander/dotnet-inspect/issues/4640
[#4641]: https://github.com/richlander/dotnet-inspect/issues/4641
[#4642]: https://github.com/richlander/dotnet-inspect/issues/4642
[#4643]: https://github.com/richlander/dotnet-inspect/issues/4643
