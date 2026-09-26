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
are implemented in step 3. Analysis production paths adopt both owners in step
4. Decompiler physical import and EH structuring adopt both owners in step 5.
The remaining consumer migrations and retirement stay unverified until their
focused steps land.

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

Analysis's adopted per-method context is constructed from one Metadata-issued
`MethodBodyData`. It validates the physical method address before preserving
the catalog and decoding Instructions facts. Body signals, stable-getter
admission, and structural-clone EH admission consume the catalog directly.
Reaching definitions reuse the same `MethodInstructions`. ArrayPool exception
path policy consumes Instructions-issued location and handler identities while
retaining Analysis-owned conservative catch-interception policy; it does not
claim shared exceptional-search or unwind facts.

Raw byte/`ExceptionRegion` reaching-definitions overloads remain explicit
Layer 0 compatibility entry points. They cannot publish correlated facts and
are not used by the production `LibraryBodyAnalysisService` path. An
instruction-only lifted-owner scan decodes only instructions rather than
constructing an unused raw EH graph.

Decompiler's structured projection is an adapter, not a third general EH facts
API. A structured node can carry exact association to Instructions-issued
region and continuation identities; current ancestry can supply descendant
context within one projection build. The adapter does not become usable by
Analysis and does not move into Instructions.

For a body that declares EH, the adopted import path reads one closed Metadata
body result, decodes one correlated `MethodInstructions`, and pairs each flat
handler projection with the exact Instructions clause object for the same
Metadata clause identity.
EH structuring groups clauses by Instructions protected-region identity, uses
`LocationAt` for production protected/filter/handler membership, and validates
supported explicit normal edges with `NormalTransferAt`. Successful structured
nodes retain the exact protected-region or clause association. Missing,
ambiguous, or rejected correlated Instructions/catch evidence leaves the flat
representation intact and lowers fidelity visibly. An unavailable Metadata
body is a closed import failure. Metadata-backed production code never
substitutes the raw range algorithm; explicit synthetic Layer 0 inputs retain
that compatibility path.

`ProtectedRegionControlFlow` is the step-6 Decompiler policy adapter. For a
metadata-backed `Leave`, it requires an available `NormalTransferAt` result,
uses the owner-issued source and destination context to select raisable regions
actually left by the edge, and rejects a source inside a `finally` or `fault`
handler. It walks current IR ancestry only to locate structured constructs,
then requires their exact protected-region or handler associations to match the
Instructions source context; the bounded form additionally limits that walk to
constructs below the caller's candidate boundary. Equal ranges, node kinds, or
an association from a different body observation do not substitute for that
identity. Missing or stale production evidence declines and reports `DEC0017`;
detached or non-Metadata Layer 0 trees retain the explicit structural
compatibility path. `StructuringPass` carries the production function's
evidence owner into its detached validation/build clones, whose source offsets
and exact structured associations remain the projection currency; clone
detachment alone never selects Layer 0 compatibility.

The classic async inverse is the step-7 Decompiler consumer. Its raw
`BodyIndex` obtains protected and handler membership from `LocationAt` on the
execution method's exact `InstructionExceptionFlowFacts` observation. Its
planning index requires `TryCatch`, `CatchClause`, and `TryFinally` projections
to retain the exact clause and region associations issued by that observation,
then compares each provenance-bearing planning node with the shared imported
context at the same IL offset. Equal ranges from another body observation do
not correspond. Missing, unavailable, ambiguous, or re-paired production
evidence declines through the existing visible classic-inverse failure path.
The recipe, protocol roles, structured-ancestor accounting, and reconstruction
policy remain Decompiler-owned. Explicit non-Metadata Layer 0 requests retain
their range-based compatibility path.

The Decompiler composition boundary also requires the
`StateMachineRelationship`-selected `MoveNext` MethodDef to equal
`InstructionExceptionFlowFacts.Body.Method` whenever the production execution
body carries available exception-flow facts. Instructions-owned `GetClause`
and `GetRegion` resolve exact imported identities without consumer scans or
object-reference correspondence. Neither composition rule merges async and EH
semantics or moves Decompiler policy into either fact owner.

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
6. migrate `ProtectedRegionControlFlow` to shared normal-transfer facts and
   exact structured associations;
7. migrate classic async exception-context correspondence;
8. resume #6907 with shared exited-`finally` facts while keeping alias/write
   closure pass-owned; and
9. prove CLI and Browser/Wasm outcomes and retire remaining replaced paths.

Steps 2 through 8 are focused owner/adopter efforts; this design does not
authorize one implementation PR to sweep them. The current total is nine
steps. Changing that total requires updating #6965 and this map together.

Step 4 is implemented by the Metadata-backed `MethodBodyAnalysisContext`,
body-signal and admission consumers, shared reaching-definitions decode, and
the ArrayPool exception-path adapter. Step 5 is implemented by the
Metadata-backed Decompiler importer, correlated `MethodInstructions` handoff,
exact flat-to-structured clause association, and Instructions-backed EH
membership and normal-edge validation. Step 6 is implemented by
`ProtectedRegionControlFlow`'s shared normal-transfer query, Decompiler-owned
raisability policy, and exact bounded association check. Step 7 is implemented
by classic async's shared `LocationAt` queries, exact structured association
checks, and same-observation raw/planning join. Step 8 is implemented by
`InlineReturnLeaves` consuming each `Leave` transfer's ordered cleanup-handler
identities while retaining Decompiler-owned rewrite policy and conservative
managed-reference uncertainty. Step 9 remains.

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

Issue [#4178](https://github.com/richlander/dotnet-inspect/issues/4178)'s
return-timing consumer uses the shared exited-cleanup facts. Its conservative
managed-reference decline boundary is not an EH fact and stays with
Decompiler.

No TLA+ model is planned. The owners publish immutable, single-body,
single-threaded results without scheduling or distributed state. Focused
construction, correspondence, consumer, and product-host gates are the direct
evidence.

`ResourceLifecycleAnalysisTests` gates exact body/clause identity
preservation, refusal of uncorrelated body signals, compiler-produced nested
`finally` contexts, catch-all cleanup, typed-catch near misses, and nested
catch interception through the production assembly-analysis path. Existing
method signal, stable-getter, structural-clone, and reaching-definitions tests
gate their migrated consumers.

`DecompilerExceptionFactAdoptionTests` gates the same-body Instructions
handoff, exact flat and structured clause association, Metadata catch order,
the runtime `TextReader.Read(Span<char>)` cleanup identity, visible refusal of
missing or rejected evidence, closed Metadata body failure, and the explicit
synthetic compatibility path.
`ClassicInverseCoreExceptionTests` gates production catch/finally membership
through shared facts, visible missing-correlation refusal, exact protected,
catch, and finally associations, same-range foreign-body rejection, and the
same-observation raw/planning join. It also gates the exact
relationship-selected `MoveNext`/EH-body MethodDef join, rejection of a foreign
method's observation, and neighboring user-`finally` reconstruction. The full
`ClassicInverseCoreTests` fixture population gates unchanged recipe and
accounting behavior.

This composition does not add exceptional search/unwind semantics, a
cross-method exception graph, shared Analysis/Decompiler policy, or a new
rendering domain.
