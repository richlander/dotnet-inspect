# Exception facts composition

This map composes two focused owners:

- [Metadata exception-region facts](metadata-exception-region-facts.md) own the
  physical method-body clause catalog; and
- [Instruction exception-flow facts](instruction-exception-flow-facts.md) own
  the decoded-IL topology and normal-transfer interpretation of that catalog.

It records their handoff and the peer Analysis and Decompiler adoption plan.
It does not redefine either owner's contract or any consumer's internal policy.

## Status and correction

This is the corrected architecture under
[#6965](https://github.com/richlander/dotnet-inspect/issues/6965). PR
[#6966](https://github.com/richlander/dotnet-inspect/pull/6966) previously
placed the whole fact contract under Decompiler. The operator
[approved replacing that scope](https://github.com/richlander/dotnet-inspect/issues/6965#issuecomment-5672005366)
with composed Metadata and Instructions owners and Analysis/Decompiler peer
consumption. That approval covers this two-owner composition design, not a
combined implementation PR.

Metadata's body and physical clause types, closed result, and current-surface
migration are implemented in step 2. Instructions topology, location, and
normal-transfer facts plus the `MethodInstructions` / `BlockGraph` migration
are implemented in step 3. Peer-consumer adoption and remaining retirement
stay unverified until their focused steps land.

## Handoff

```text
Metadata
  Method-body evidence identity
  Copied IL + complete exception-clause catalog
                         |
                         | exact owner-issued body/clause currency
                         v
Instructions
  Decoded instructions + validated EH topology
  Location contexts + normal-transfer facts
                 /                         \
                v                           v
Analysis                                    Decompiler
physical inventory from Metadata            physical import from Metadata
path questions from Instructions             flow questions from Instructions
consumer-owned findings/policy                consumer-owned IR mapping/raises
```

The method-body evidence identity is the join currency. Metadata issues it
once with copied IL and the complete clause catalog. Instructions preserves it
while issuing region and continuation identities. A consumer cannot join a
catalog from one body observation to IL or flow facts from another.

The dependency shape remains:

```text
Metadata --------> MetadataPrimitives <-------- Instructions
                                                 |
                                      Analysis --+-- Decompiler
```

The detached handoff types may live in `MetadataPrimitives`, as
`MethodBodyData` already does. Instructions does not depend on the Metadata
assembly, and Metadata does not depend on Instructions.

## Peer consumption

Analysis and Decompiler independently choose the lowest sufficient altitude:

| Question | Analysis | Decompiler |
| --- | --- | --- |
| Does this readable method body declare EH clauses? | Consume Metadata catalog presence directly for candidate selection and signals. | Consume Metadata catalog presence for import and early pass admission. |
| What clauses, kinds, ranges, and catch types were declared? | Consume Metadata for inventory, counts, physical containment, and Finding evidence. | Consume Metadata for imported clause and catch-type identity. |
| Which validated regions contain this instruction or block? | Consume Instructions for path-sensitive analysis. | Consume Instructions for raw-IR membership and structuring input. |
| Which `finally` handlers execute before this normal continuation? | Consume Instructions for cleanup/resource-path questions. | Consume Instructions for protected-region and return-timing questions. |
| What does the evidence mean for the product? | Analysis owns body signals, leak/resource conclusions, and Findings. | Decompiler owns structured-node correspondence, raises, alias/write proofs, fidelity, and diagnostics. |

Analysis does not route through Decompiler, and Decompiler does not route
through Analysis. Neither reconstructs shared clause or normal-transfer
identity from offsets, display rows, or handler names.

Decompiler's structured projection is an adapter, not a third general EH facts
API. A structured node can carry exact association to Instructions-issued
region and continuation identities; current ancestry can supply descendant
context within one projection build. The adapter does not become usable by
Analysis and does not move into Instructions.

## Nine-step adoption plan

Tracker #6965 owns this complete sequence:

1. lock the corrected Metadata, Instructions, and composition designs;
2. implement Metadata's closed method-body/exception-region result and migrate
   its existing region/context surfaces;
3. implement Instructions flow facts and migrate `MethodInstructions` /
   `BlockGraph`, retiring duplicate normalized-region and crossed-`finally`
   logic;
4. adopt Metadata and Instructions facts in Analysis, retiring independent
   clause-order/range interpretation where the shared contracts apply;
5. adopt the same facts in Decompiler import and EH structuring, adding exact
   structured-IR association;
6. migrate `ProtectedRegionControlFlow`;
7. migrate classic async exception-context correspondence;
8. resume #6907 with shared exited-`finally` facts while keeping alias/write
   closure pass-owned; and
9. prove CLI and Browser/Wasm outcomes and retire remaining replaced paths.

Steps 2 through 8 are focused owner/adopter efforts; this design does not
authorize one implementation PR to sweep them. The current total is nine
steps. Changing that total requires updating #6965 and this map together.

## Production-host path

The architecture reaches both hosts through existing product paths:

- Metadata's existing **Exception Regions** section can render the owner-issued
  catalog through Markout in CLI and browser views without a new shape.
- Analysis Findings and Signals can consume the same Metadata catalog and
  Instructions flow facts before their existing shared projections.
- CLI decompiled source reaches `MemberBodyProducer`.
- Browser/Wasm source reaches the same producer through
  `AssemblyContextSourceQuery`; the end-to-end gate must force decompiler
  fallback and traverse the generated TypeScript `queryMemberSource` facade and
  Wasm/worker boundary.

No host owns EH interpretation. No new default section is implied. Markout
remains the existing lowering owner for Metadata and Analysis structured
output; decompiled C# continues through the Decompiler printer.

## Evidence and scope

The production motivation is the pinned runtime
[`TextReader.Read(Span<char>)`](https://github.com/dotnet/runtime/blob/f9b470a5ae7dccd67a1d3fb21aea39c3c8410c7c/src/libraries/System.Private.CoreLib/src/System/IO/TextReader.cs#L96-L114)
shape. Metadata can report its declared `finally`; Instructions can report the
normal return continuation and cleanup order; Analysis and Decompiler can
interpret those facts without sharing policy.

PR [#6907](https://github.com/richlander/dotnet-inspect/pull/6907) remains
paused. Its accepted indirect-store alias finding is not an EH fact and stays
with Decompiler's return-timing consumer.

No TLA+ model is planned. The owners publish immutable, single-body,
single-threaded results without scheduling or distributed state. Focused
construction, correspondence, consumer, and product-host gates are the direct
evidence.

This composition does not add exceptional search/unwind semantics, a
cross-method exception graph, shared Analysis/Decompiler policy, or a new
rendering domain.
