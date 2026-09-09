# C# memory-safety declaration spelling

## Status and ownership

This is the focused declaration contract for
[#5257](https://github.com/richlander/dotnet-inspect/issues/5257), owned solely
by `ILInspector.CSharp`. The opt-in method/field implementation is tracked by
[#6105](https://github.com/richlander/dotnet-inspect/issues/6105) and consumes
the layout facts delivered by
[#6144](https://github.com/richlander/dotnet-inspect/issues/6144).
Compatibility remains the default. The remaining declaration forms and
production-host adoption remain pending.

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
those boundaries, including
[API layout facts](type-member-api-representation.md#api-layout-facts); it does
not redefine their evidence or reconstruction.

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

A composed C# source artifact has one module rules model at compilation time.
One batch, including its nested types, therefore cannot preserve a mixture of
legacy and updated input contracts. Model-aware batch printing refuses that
mixture atomically rather than produce source whose successful compilation
silently changes one side's caller obligations.

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
The Metadata implementation-fact projection tracked by
[#5940](https://github.com/richlander/dotnet-inspect/issues/5940) landed in #5972.
Those facts retain flags and body-RVA presence, not a C# extern decision; this
design does not specify their construction.

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

### Method and field slice

`CSharpFormatOptions.MemorySafetyLanguage` and
`CSharpTypePrintOptions.MemorySafetyLanguage` explicitly select model-aware
spelling. Their null default retains the existing compatibility view.
`Legacy` requires a lexical pointer context; `RelaxedPointerSyntax` removes
that requirement; `UpdatedCallerContracts` additionally supports the v2
declaration forms. These are language capabilities, not requests to change
the inspected module's rules. Compilation must select the corresponding
language and preserve the recognized module model separately.

The first slice supports methods, ordinary constructors, and fields. An
explicit `CSharpBodyPolicy.Extern`, or `CSharpFormatOptions.IsExtern` for a
single declaration, selects the no-body extern form. A skeleton, abstract
member, or reconstructed stub is not automatically extern. Raw MethodDef
implementation facts remain available to source-shape producers; this policy
does not substitute an RVA test for their affirmative choice.

An opt-in whole-type batch returns `CSharpTypePrintOutcome.NotRendered` with
`MemorySafetyFailures` when necessary evidence or a supported declaration
form is unavailable. The existing self-name failures remain independent;
neither failure category exposes partial source. String-returning formatter
entry points report the same refusal through `NotSupportedException`.

Properties, events, accessors, delegates, enums, and primary-constructor
syntax remain explicitly unavailable in this opt-in slice. A caller can
select the supported members or supply the product-selected explicit-field
and ordinary-constructor shape. The printer does not silently drop an
unsupported selected member. This slice proves safety-modifier spelling and
caller-contract preservation, not body reconstruction or general layout
reconstruction.

Method-like admission requires the Metadata-owned
`ApiMember.MethodSemantics` fact. A positive `None` permits the ordinary
method, constructor, finalizer, extension, or explicit-interface method path.
Any property or event MethodSemantics role is an unsupported accessor and
refuses the complete output. Null is unavailable evidence rather than proof of
an ordinary method. This distinction follows the MethodSemantics relationship
and does not infer accessor shape from a qualified MethodDef name,
`SpecialName`, or an empty declaration-accessor collection.

An attached extension method is rendered as its defining static declaration,
not as a declaration of the receiver type that carries the projection. Direct
member formatting therefore applies the receiver's module rules and exact
member association but does not apply whole-type enum, delegate, primary-
constructor, or layout admission to that receiver. The extension declaration's
own method form still controls ordinary and affirmative extern policy, including
an extern extension projected onto an interface receiver. Whole-type printing
of an enum or delegate remains unavailable independently of a projected
extension member. A type-unit or whole-type request also retains its actual
enclosing-type restrictions; it does not relocate an attached extension into
the defining static class. Any attached extension selected through those paths
is unavailable regardless of receiver kind or body policy; standalone member
formatting remains the supported projection path.

`safe` is illegal on an ordinary field when its enclosing layout is not
emitted (CS9388), so the supported updated-rules explicit-layout path emits a
`StructLayoutAttribute(LayoutKind.Explicit, ...)` on the type and a
`FieldOffsetAttribute` on every selected instance field. This includes fields
whose caller contract spells `unsafe`; static fields do not receive an offset.
Zero is a valid offset. Positive size and packing observations are emitted as
named arguments; zero retains the attribute defaults. Packing values that C#
cannot represent are unavailable rather than emitted as invalid source.

The printer admits that source only when the layout MVID and TypeDef token match
the declaring type, and each selected instance field's MVID, declaring TypeDef
token, FieldDef token, and usable offset match its declaration. The type and
member metadata tokens are therefore retained through the rendering snapshot
alongside the layout facts. Missing or mismatched evidence refuses the complete
batch before source publication.

These attributes are required semantic spelling, not optional custom-attribute
display. Suppressing ordinary custom attributes does not suppress them.
Collision-proof `global::System.Runtime.InteropServices` names keep their
binding independent of the inspected source's namespace and type names.
Individual formatter entry points apply the same type or field decision using
the supplied declaring-type evidence; the whole-type printer is the supported
compilation-unit proof.

Extended layout still requires a `safe` field decision under the language
model, but the current evidence does not establish a corresponding C# layout
attribute form. Model-aware extended-layout output is therefore visibly
unavailable. Sequential and automatic layouts do not infer offsets or enter
this narrow replay path. Legacy binaries also remain outside this layout
lowering path: selecting a newer output-language capability does not change
their module rules or authorize updated-rules derived attributes.

### Production adoption

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

The method/field slice is part of stage 2, not an additional host implementation
or a completed adoption stage. Implementation must record
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

The implemented method/field slice is gated by
`CSharpMemorySafetySpellingTests`, covering declaration decisions, exact
evidence association, explicit-layout lowering, mixed-rules batch refusal,
and atomic type outcomes.
`CSharpMemorySafetySpellingCompileTests` compiles product-produced artifacts
unchanged, then re-extracts their caller contracts. Its explicit-layout case
also re-extracts the size, packing, and zero/nonzero field offsets. The compiler
gate uses the existing Decompiler test executable and its Roslyn reference
infrastructure; the decision gate uses the CSharp executable. The Dynamic
fixture census retains the compiler site explicitly.

Ordinary CI runs the complete CSharp test executable and the existing fast
Decompiler lane. Neither gate establishes the deferred declaration forms,
primary-constructor fallback, or production-host adoption; those portions of
this document remain **unverified**.

Existing `ApiMemorySafetyFactsTests` prove the input facts, not the spelling
decisions.

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
