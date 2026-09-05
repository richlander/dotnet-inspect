# C# memory-safety declaration spelling

## Status and ownership

This is the focused declaration contract for
[#5257](https://github.com/richlander/dotnet-inspect/issues/5257), owned solely
by `ILInspector.CSharp`. It is a design prerequisite, not a claim that the
current printer implements model-aware spelling.

**Claim:** a rendered declaration preserves the supplied caller contract
under the selected C# language semantics, independently of structural pointer
shape and body-context requirements. When those inputs cannot support that
claim, the declaration is visibly unavailable rather than plausibly spelled.

[Memory-safety models and evidence](memory-safety-models.md) supplies the
v1/v2 vocabulary and language distinctions. Metadata owns
[API memory-safety facts](type-member-api-representation.md#api-memory-safety-facts)
and their unavailable states. Decompiler owns reconstructed bodies and the
primary-constructor fallback under
[memory-safety rendering modes](memory-safety-modes.md). This document consumes
those boundaries; it does not redefine their evidence or reconstruction.

## Purpose and design basis

The consumer is the shared C# declaration producer used by API views and
reconstructed source. A Boolean cannot distinguish a v2 pointer-bearing safe
boundary from a pointerless caller-unsafe member. Treating either as the other
changes the obligation displayed to callers.

The conventional basis is typed compiler facts followed by a language-specific
spelling decision. The additional policy is justified by that concrete
correctness requirement, not by a new general result framework.

The accepted
[unsafe-evolution proposal][unsafe-evolution] supplies the language baseline:
pointer syntax and caller-contract semantics have independent feature gates.
Compiler-produced fixtures and Roslyn's tests are the comparison oracle for
legal declaration forms. Their emitted contracts are evidence; neither
attribute display text nor an analogous decompiler's spelling is authority.
No implementation code is transferred.

## Inputs and result

The spelling decision consumes the declaration's Metadata-issued contract,
its independent signature-pointer evidence, the selected output language
semantics, and the declaration shape actually being rendered. Layout and
backing-storage evidence apply to the declaring type, not a projected receiver.
Accessor evidence remains associated with its exact MethodDef in its module.

The binary rules model and output language capability are separate inputs.
An unmarked binary does not prove that its source used an older language.
Replay preserves the recognized binary contract; selecting a language does
not implicitly opt the output into another module rules model. Migration
simulation is outside this contract.

The observable result is no safety modifier, `safe`, `unsafe`, or an explicit
unavailable result identifying the declaration and missing or incompatible
evidence. This is a C# spelling decision, not another caller-contract resolver.
The existing typed print outcome and diagnostic mechanisms should carry it.

Unsupported, malformed, conflicting, or unavailable module evidence cannot
authorize model-aware replay. This is deliberately narrower than a compiler's
compatibility inference for an unrecognized marker: a plausible declaration is
not a faithful interpretation of an unknown rules model.

Older serialized or hand-composed surfaces without the new facts remain
eligible for the existing compatibility view, not for a model-aware result.
The compatibility path must remain distinguishable; it must not turn a missing
fact into `None`, a pointer-free signature, or proven absent storage.

## Declaration obligations

### Caller contract and pointer syntax

| Input and selected semantics | Required outcome |
| --- | --- |
| v2 explicit caller contract on a declaration that permits it | `unsafe`, even when the signature contains no pointer |
| v2 `None` with a pointer-bearing signature | No `unsafe` merely because of the pointer |
| v1 pointer-bearing signature with language semantics requiring a lexical context | A sufficient declaration-level `unsafe` context |
| v1 pointer-bearing signature with relaxed pointer syntax | Pointer syntax alone does not require the modifier; signature-based caller propagation remains unchanged |
| An unavailable input needed to choose the modifier | Visible unavailability, not a negative fact |

A declaration may need additional lexical context under legacy language rules
because of a product-supplied body requirement. Under v2, that requirement
cannot be satisfied by adding a member modifier: it would change the caller
contract without establishing the required body context. Body wrapping and the
constructor-initializer exception remain Decompiler responsibilities.

Under v2, do not put `unsafe` on a type, delegate declaration, static
constructor, or destructor. A body requirement cannot override that language
restriction. Inputs requiring an unrepresentable caller contract are
unavailable; suppressing the contract is not a repair.

### Required `safe` spelling

Under v2, a non-propagating declaration must spell `safe` when its emitted form
requires an explicit choice. This covers extern declarations and instance
fields or field-backed declarations in explicit- or extended-layout types.
Static storage does not acquire that layout-based requirement.

The extern decision needs an affirmative declaration-shape fact. An absent
managed-body RVA is insufficient: abstract, runtime-provided, reference-assembly,
and reconstructed stub shapes do not all mean the same emitted declaration.
CSharp does not infer this fact from a displayed attribute or signature.
The missing Metadata implementation-fact projection is tracked by
[#5940](https://github.com/richlander/dotnet-inspect/issues/5940); this design
does not specify that projection's construction.

Backing associations are conventions, not recovered source. Unknown or
ambiguous association evidence is not proof that a property or event has no
instance storage. The selected source shape also matters: an emitted
auto-property can introduce storage even when the source body was custom.
Where the selected shape and retained evidence cannot establish a valid
modifier placement, report unavailability rather than invent storage absence.
No obligation is imposed on Metadata to decide C# spelling.

`safe` is derived spelling, not a recovered source keyword or a new binary
fact. Its emission does not claim that a body is memory-safe.

### Properties and events

Preserve the observable contracts of the property's individual accessors.
Honor the language's placement rules: safety modifiers may appear on the
property or its accessors, not both; a common modifier on every accessor is
spelled on the property. Retain differing accessor contracts where the language
can represent them. An incompatible owner/accessor combination is unavailable,
not normalized into a different contract.

Event accessors cannot independently carry these modifiers. An event whose
retained accessor contracts cannot be represented by its declaration is
unavailable. Do not discard an accessor's evidence to make it printable.

### Reconstructed primary constructors

Consume the source shape selected by the Decompiler owner. When that owner
supplies an explicit field and ordinary constructor instead of a primary
constructor, preserve both and apply declaration spelling to the field and
constructor separately. Never place `safe` or `unsafe` on a primary-constructor
parameter as a substitute for a storage declaration.

CSharp does not choose which stores to remove or undo a primary-constructor
raise. The product-owned fallback prerequisite remains in #5255. Changing only
a compile-back planner cannot establish that production reconstruction
satisfies it.

## Rendering and adoption

The information remains typed through the shared CSharp declaration boundary.
CSharp owns source-language lowering; Markout remains the presentation
substrate for views that embed those declarations. Neither CLI nor browser
code re-derives the modifier from the old Boolean or rendered text.

The end-to-end tracker is
[#5226](https://github.com/richlander/dotnet-inspect/pull/5226). Its production
declaration path has **three stages**:

1. Metadata publishes independent facts in #5253, completed by #5915.
2. The shared CSharp producer adopts them in #5257, after the required
   product-owned #5255 fallback and #5940 declaration inputs are available.
3. #5257 exercises that producer through CLI and browser/Wasm declaration
   surfaces, including their filtered and selected views.

This design is the contract prerequisite within stage 2, not an additional
host implementation or a completed adoption stage. Implementation must record
any missing owner-issued declaration-shape input as a focused prerequisite
rather than derive it from display text or broaden this owner's design.
Production source composers must also route their declaration portions through
this shared policy; that wiring does not transfer body reconstruction or
storage-selection ownership to CSharp.

Retirement is consumer-specific: stage 2 removes `IsUnsafe` as the authority
for model-aware CSharp spelling, and stage 3 retires any host-local substitute
on those surfaces. Existing filtering, diff, JS-export, and Research policies
remain with their owners and retain their existing behavior until separately
migrated. Compatibility rendering of older inputs remains explicitly limited
as described above. No platform or single-host exception is requested.

## Evidence required before implementation is supported

The behavior in this document is **unverified** until the implementation gates
land. Existing `ApiMemorySafetyFactsTests` prove the input facts, not these
spelling decisions. Existing CSharp tests prove current behavior, not this
proposed adoption.

Use the existing Release CSharp test executable for declaration and whole-type
outcomes. Compile product-produced artifacts with the selected language and
rules configuration to establish legality; do not have the harness repair
their source. Re-extract caller contracts where legality alone cannot detect
a changed obligation. The bounded case family is:

- Pointer-free explicit v2 contracts and pointer-bearing v2 `None`, alongside
  legacy pointer declarations under both pointer-syntax modes.
- Required `safe` for extern and instance-storage declarations, with static
  storage and ordinary custom declarations as neighboring cases.
- Uniform and differing property-accessor contracts, event-accessor
  limitations, and unsupported declaration positions.
- Unavailable rules or necessary signature/storage evidence, and the explicit
  older-input compatibility boundary.
- A product-produced primary-constructor fallback preserving the field and
  ordinary constructor, with an ordinary primary constructor unchanged.
- The same facts and spelling through the actual CLI and browser/Wasm
  declaration consumers; selection must not drop facts before rendering.

These are outcome gates, not hostile-caller or source-policing tests. Each
implementation slice names its concrete tests and the cases it supports.

[unsafe-evolution]: https://github.com/dotnet/csharplang/blob/f445f642755a28631b7e37db01f6373c437159c3/proposals/unsafe-evolution.md
