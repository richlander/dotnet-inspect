# Shared metadata primitives

> **Map:** [Type, member, and API representation](design/type-member-api-representation.md)
> is the entry point for choosing a type, member, or API identity shape. This
> document owns the mechanical SRM boundary below those shapes.

## Decision summary

The June 2026 decision to stop after the first three MetadataPrimitives
migration steps is superseded.

Resume consolidation in `ILInspector.MetadataPrimitives`, but consolidate
**mechanics, not semantic models**:

- MetadataPrimitives owns bounded SRM traversal, signature-decode admission,
  exact metadata names and addresses, neutral structural keys, work budgets,
  and typed mechanical rejection.
- Metadata, Analysis, Decompiler, Instructions, and ILDiff retain their own
  semantic models, signature providers, projections, and failure policy.
- Analysis and Decompiler keep separate `TypeRef` types. They answer different
  questions and have continued to diverge in useful, owner-specific ways.
- Analysis and Decompiler should replace their local TypeSpec recursion and
  byte accounting with the shared `TypeSpecGuard`.
- Provider-facing wrappers remain local when they turn the same mechanical
  rejection into different owner-specific outcomes.
- Existing forwarded public identities in the `ILInspector.Metadata` namespace
  remain compatibility-pinned. A source census should make that legacy family
  explicit and require new neutral currencies to use
  `ILInspector.MetadataPrimitives`.

This document records the decision only. It does not authorize combining the
implementation slices or changing failure behavior without the focused
evidence described below.

## Why the previous stop decision expired

The old decision tested the proposed adoption against its first real Analysis
consumer. It correctly concluded that Analysis needed a semantic `TypeRef`, not
Metadata's display-oriented decoder, and that sharing the remaining
attribute-name walk would delete only about 15 stable lines. That
coupling-to-payoff result still holds.

What expired is the assumption that steps 4 and 5 were one all-or-nothing
choice. Analysis and Decompiler have since adopted several neutral mechanics,
and each now contains both the shared TypeSpec guard and an older local
TypeSpec policy. The new evidence is a concrete policy split below the semantic
models, not a reason to revisit the models themselves.

At `607bc830f`:

- Analysis has six project references, including direct references to both
  Metadata and MetadataPrimitives.
- Decompiler directly references Metadata, MetadataPrimitives, Instructions,
  ControlFlow, CSharp, Findings, ILDiff, Text, and CSharpText.
- Analysis and Decompiler already consume MetadataPrimitives-owned
  `MetadataRelationshipTraversal`, `SignatureBlobGuard`, and related rejection
  types. `StructuralCloneAnalysis` and `IrImporter` also call
  `TypeSpecGuard.TryEnter` directly.
- Both separately consume Metadata-owned `MetadataTypeDefinitionName` and
  `AssemblyReferenceIdentity`; those are product identity currencies, not
  evidence of MetadataPrimitives adoption.
- ILDiff is another direct MetadataPrimitives consumer for
  `MethodStructuralSignature` keys and bounded metadata mechanics.
- Nineteen product `ISignatureTypeProvider<,>` implementations exist across
  the repository. The number is not itself a defect: the SRM provider pattern
  is how each owner projects one signature walk into its own result.

The question is therefore no longer whether a shared dependency is worth
introducing. The dependency and partial adoption already exist. The current
question is which remaining mechanics have one repository-wide answer and
which differences are intentional policy.

## Current boundary

`ILInspector.MetadataPrimitives` is currently an SRM-only leaf with no project
references. That property is **not gated**; the first implementation slice must
add a project-closure gate before citing it as enforced.

```text
                     ILInspector.MetadataPrimitives
             (bounded SRM mechanics and neutral currencies)
                  /          |          |          \
          Metadata       Analysis   Decompiler   Instructions
                                                     |
                                                   ILDiff
```

The diagram shows ownership, not every direct project edge. ILDiff also
references MetadataPrimitives directly.

### Shared mechanical ownership

| Concern | MetadataPrimitives owns |
| --- | --- |
| Metadata relationships | Bounded TypeDef, TypeRef, ExportedType, and related handle walks; typed rejection |
| Signature admission | Structural blob prescan and cross-TypeSpec depth/byte budgets |
| Exact metadata names | Validated namespace plus root-to-leaf metadata-name segments |
| Physical addresses | MVID plus validated metadata table/token coordinates |
| Neutral structural identity | Bounded keys used for matching without display policy |
| Work budgets | Limits and typed exhaustion/rejection shared across consumers |
| Generic metadata context | Bounded generic parameter names and constraint flags |
| Neutral matching | Dependency-free name distance and similarity |

These mechanisms may expose handles, neutral values, typed results, or
disposable admission scopes. They must not select a consumer's display,
fallback, trust, correlation, or code-generation policy.

### Consumer ownership

| Owner | Retains |
| --- | --- |
| Metadata | API models, declaration identities, degraded metadata facts, and API/display projections |
| Analysis | Evidence `TypeRef`, trust evidence, call/member matching, catalog correspondence, and incomplete-analysis policy |
| Decompiler | Pipeline `TypeRef`, code-generation facts, custom modifiers, function-pointer spelling inputs, and fidelity policy |
| Instructions | Decode, stack-shape, and instruction-substrate projections |
| ILDiff | Canonical IL operands, member/body alignment, diff failures, Findings, and presentation |

Consumer-owned providers should call shared admission and traversal mechanics,
then construct their native result. A neutral primitive must not return a
plausible `object`, an empty signature, or a display string on rejection unless
that value is itself an explicit typed result arm.

## Why `TypeRef` remains local

The Analysis and Decompiler models are not accidental copies of one canonical
type.

Analysis `TypeRef` carries evidence-facing concerns including:

- exact resolution provenance beside the structural shape;
- framework and protobuf trust evidence;
- catalog-correspondence payload for modifiers, function pointers, and array
  bounds;
- Analysis-specific incomplete and unsupported outcomes.

Decompiler `Pipeline.TypeRef` carries code-generation concerns including:

- value-type hints and inline-array facts;
- enclosing-type facts used by raising;
- function-pointer calling conventions, parameter ref kinds, and modifiers;
- fidelity-lowering unsupported shapes and printer inputs.

Those facts have different equality, lifetime, and failure semantics. A shared
`TypeRef` would either erase required evidence or become a union of unrelated
layer policy. The repository-wide representation map therefore remains
authoritative: type identity is a set of scoped currencies, not one universal
record.

The allowed sharing point is below both models:

1. admit the untrusted blob or relationship walk through shared bounds;
2. return neutral handles, exact names, structural coordinates, or typed
   rejection;
3. let the consuming provider construct its native model.

## Remaining convergence

### 1. Use one TypeSpec admission mechanism

Analysis and Decompiler currently duplicate:

- thread-static recursion depth;
- cumulative TypeSpec byte accounting;
- a local 1,024-byte per-TypeSpec limit;
- direct `SignatureBlobGuard` calls;
- cleanup of the active budget in `finally`.

MetadataPrimitives already owns `TypeSpecGuard`, with a 256-entry and
4,096-cumulative-byte contract plus the shared structural prescan. The local
decoders match those limits but add a 1,024-byte per-TypeSpec cap. They can
therefore only reject more input than the shared policy; they do not accept
anything the shared guard rejects.

The repository's existing shared contract intentionally allows one shallow
TypeSpec to consume the full 4,096-byte closure budget. Converging the semantic
decoders on that contract is therefore an explicit acceptance change:
well-formed, structurally bounded TypeSpecs from 1,025 through 4,096 bytes may
change from `Unsupported` to a decoded Analysis or Decompiler shape.

The first implementation slice should:

1. expose the existing typed rejection from `TypeSpecGuard.TryEnter` without
   adding a parallel guard;
2. route Analysis and Decompiler `GetTypeFromSpecification` through that
   disposable scope;
3. map rejection into each owner's existing unsupported/incomplete result;
4. remove the local counters and the separate 1,024-byte policy;
5. preserve each decoder's successful structural projection byte-for-byte
   outside the named 1,025–4,096-byte acceptance band.

`ProviderSignatureDecodeBoundaryTests` is the existing anti-ratchet gate for
top-level provider decodes and nested TypeSpec entry. The implementation slice
must update that gate so its accepted Analysis/Decompiler pattern is the shared
`TypeSpecGuard`, then mutation-prove that bypassing the guard in either decoder
fails. `TypeRefDecoderRecursionTests` in both owner suites must cover matching
close negative cases and the shared cumulative budget.

The evidence must include a shallow legal fixture inside the 1,025–4,096-byte
band, close negatives immediately above 4,096 bytes and above the structural
limits, and an allocation/output-budget canary for the newly admitted shape.
The PR must report the before/after result for both owners rather than describe
the change as behavior-neutral.

Until that slice lands, identical legal-input acceptance across the semantic
decoders is **not verified**.

### 2. Keep decode mechanics separate from failure policy

Metadata and Instructions have similarly named `GuardedProviderDecode`
adapters. Metadata returns values plus degraded state for row-level API
projection. Instructions and ILDiff use try-style results, typed diff failures,
or hash-derived unsupported identities. Decompiler's string composers throw at
their existing artifact-operation boundary, while Metadata's string producers
retain `SignatureDecodeResult<T>`.

Those outcomes are intentionally different. Do not move fallback construction
or throwing behavior into MetadataPrimitives merely to delete similarly shaped
methods. The shared owner is the prescan and TypeSpec admission mechanism;
the consuming owner decides what rejection means.

`StringSignatureDecodeBoundaryTests` currently scans Metadata and
MetadataPrimitives only. Decompiler's requirement that string-producing
decodes pass through its `GuardedSignatureText` and `GetValueOrThrow` policy is
therefore **not verified**. The first implementation slice must extend the gate
to Decompiler and mutation-prove that a new prescanned direct use still fails
when it bypasses the owner gateway.

A generic decode helper belongs in MetadataPrimitives only if at least three
consumers need the same typed outcome contract. Line-count reduction alone is
not sufficient.

### 3. Distinguish ILDiff's two signature projections

ILDiff contains two `SignatureIdentityProvider` classes, but they answer
different questions:

- assembly/member pairing requires exact declaring-type identity and rejects a
  method from the correlation map when identity construction fails;
- operand canonicalization applies diff normalization, compiler-generated
  correspondence, and unsupported-signature identities for row evidence.

Do not merge them into one provider or move their string policy downward.
A focused ILDiff cleanup may rename them to make the two projections obvious
and may share genuinely byte-identical primitive spelling helpers, but it must
preserve their distinct failure and normalization contracts.

### 4. Make the namespace split deliberate

Most files in `ILInspector.MetadataPrimitives` still declare
`namespace ILInspector.Metadata`, while newer neutral currencies use
`ILInspector.MetadataPrimitives`. The mismatch began as transitional, but some
old full type names are now explicit compatibility contracts:

- `ILInspector.Metadata` forwards
  `ILInspector.Metadata.SignatureBlobGuard` and
  `ILInspector.Metadata.MethodStructuralSignature` to the primitives assembly;
- `SignatureDecoderSafetyTests.SignatureBlobGuard_OldAssemblyIdentity_IsForwarded`
  gates the former identity;
- a CLR type forwarder cannot preserve an old full type name while changing its
  namespace.

A blanket namespace move would therefore be a breaking identity change, not a
mechanical cleanup. The dedicated namespace slice should instead:

1. inventory every public primitive as compatibility-pinned legacy namespace
   or owner-native `ILInspector.MetadataPrimitives`;
2. retain the old namespace for forwarded identities unless a separate
   decision explicitly retires the compatibility gate;
3. require new neutral currencies to use the owner-native namespace;
4. add a source/forwarder census that fails on an unclassified public type or a
   stale forwarding contract.

This makes the mixed namespace deliberate without adding duplicate wrapper
types. The classification property is currently **not gated**.

## Existing safety enforcement

The current boundary is protected by:

- `ProviderSignatureDecodeBoundaryTests` for guarded provider decodes and
  bounded nested TypeSpec re-entry;
- `StringSignatureDecodeBoundaryTests` for Metadata and MetadataPrimitives
  string-producing signature paths; Decompiler policy is not yet covered;
- `MetadataRelationshipTraversalTests` for bounded relationship mechanics;
- `SignatureBlobGuardTests` and `SignatureDecoderSafetyTests` for malformed and
  adversarial signature shapes;
- `LayeringTests.MetadataNameMatching_DoesNotDependOnFindingBackedText` for the
  MetadataPrimitives owner of neutral name matching.

No current gate enforces that MetadataPrimitives has zero project references.

An implementation that changes a stated safety or ownership property must
extend the owning gate rather than relying on a green broad suite.

## Sequencing

Keep the work in independently reviewable slices:

1. **TypeSpec admission and boundary gates** — add the leaf and Decompiler
   string-gateway gates, converge Analysis and Decompiler on the shared guard,
   and evidence the explicit 1,025–4,096-byte acceptance change.
2. **Namespace ownership classification** — inventory legacy forwarded
   identities and owner-native types, then add the source/forwarder census.
3. **Optional local clarity** — rename ILDiff's two providers if the names
   continue to obscure their distinct projections.

Do not combine these slices with a `TypeRef` redesign, provider-policy rewrite,
or rendering change. Each slice must preserve product output and failure
visibility except for the TypeSpec acceptance change explicitly owned by slice
1.

## Non-goals

- A repository-wide `TypeRef`.
- One canonical type spelling.
- Moving rendering, trust, analysis, correlation, or fidelity policy into
  MetadataPrimitives.
- Hiding mechanical rejection behind empty or plausible values.
- Adding a dependency from Analysis to Decompiler or from Decompiler to
  Analysis.
- Deduplicating provider classes solely because they implement the same SRM
  interface.
- A blanket namespace rename that breaks existing forwarded full type names.

## Superseded decision

The June 2026 "stop after step 3" decision correctly rejected a unified
display-oriented `TypeRef` and declined to share a two-consumer
attribute-name walk whose payoff was only about 15 lines. It treated all
further adoption as one choice.

The durable part remains: semantic models stay local and consumers pull only
demonstrably shared primitives downward. The superseded part is the prohibition
on steps 4 and 5. Analysis and Decompiler have already adopted shared
relationship and identity mechanics; they should complete that convergence
where one bounded SRM mechanism has one correct answer.
