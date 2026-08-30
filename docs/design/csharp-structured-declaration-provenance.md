# C# structured declaration provenance

## Status and owner

This document defines the target declaration-slot contract for
`ILInspector.CSharp`. `CSharpFormatter` owns model-bound C# declaration
spelling; `CSharpDeclarationWriter` is its current declaration seam.

The target is **unverified** until the gates in
[Verification](#verification) exist. Current formatters mix structured
composition with compatibility text and do not yet return the result shape
defined here.

## Claim

A structured C# declaration is composed only from explicitly classified input
slots. Every slot selects exactly one class from the closed
[slot value taxonomy](#slot-value-classes): raw identifier, qualified-name
spelling, namespace identity, type declaration name, type expression, bound
generic reference, type-binding evidence, declaration admission evidence,
closed syntax, rendered fragment, raw literal value, composite subplan, or
opaque compatibility.

The CSharp owner prepares each non-opaque slot according to that classification
before composition. It never recovers a slot boundary or provenance fact by
scanning the completed declaration. Raw Metadata models and identities remain
unchanged.

This is one CSharp-owned claim. Metadata continues to own extraction and its
`ApiType`, `ApiMember`, and `ApiSignature` facts. CSharpText continues to own
model-free lexical grammars and identifier policy.

## Why the current seam is insufficient

`ApiSignature` is not one provenance currency. For example:

- `MemberName` may combine an explicit-interface type expression with a member
  identifier;
- `ReturnType` and `ApiParameter.Type` are rendered type expressions;
- `ApiParameter.Name` and `TypeParameter.Name` are raw identifiers;
- `DefaultValueText` is an already-rendered expression;
- accessibilities, modifiers, accessor kinds, and special constraints are C#
  syntax choices; and
- `ApiMember.Signature` is an opaque compatibility declaration fragment.

The current adjacent handoffs also lack admission evidence required by this
target. Metadata classifies an `op_` prefix as an operator without retaining
one neutral candidate fact containing `SpecialName`, staticness, and complete
signature-shape evidence for CSharp to validate.
`ApiSignature.Accessors` lists only projected C# accessors, so the list does not
prove that metadata `raise`/`Other` associations were absent. The type printer
also supplies flattened declared-type names as binding context, and non-enum
literal fields expose `IsConst` without their Constant-row value. None of those
current shapes can admit `Representable`; the target requires the typed
evidence cataloged below. Metadata issue
[#5164](https://github.com/richlander/dotnet-inspect/issues/5164)
owns the missing operator and complete accessor-aggregate handoff; issue
[#5172](https://github.com/richlander/dotnet-inspect/issues/5172)
owns typed non-enum constant values. Fixed-buffer attributes are currently
decoded only through a reader-bound shell helper without an `ApiMember`
handoff; issue
[#5178](https://github.com/richlander/dotnet-inspect/issues/5178)
owns that source-field evidence.

The current writer sometimes composes those values structurally and sometimes
rescans the resulting string with generic-parameter, qualified-keyword, member,
or parameter-name substitutions. That can cover a missed slot accidentally,
but it cannot distinguish two equal strings with different provenance.

PR
[#4656](https://github.com/richlander/dotnet-inspect/pull/4656)
demonstrated both sides of the problem. Removing an unconditional
post-composition substitution left an explicit-interface qualifier unhandled:
an owner generic parameter named `class` rendered as `I<class>.Map` rather than
`I<@class>.Map`. Conversely,
[#5076](https://github.com/richlander/dotnet-inspect/issues/5076)
records that rendered `int` text cannot say whether it denotes
`System.Int32` or a generic parameter whose metadata name is `int`.

More text substitution cannot close both cases. The slot classification must
survive until the CSharp owner spells it.

## Threat boundary

The relevant actor is a package or assembly publisher who controls metadata
names, signatures, attributes, and literal values in an artifact acquired from
the internet. Those values enter through Metadata's `ApiType`/`ApiMember`
projection and cross the boundary when CSharp turns them into declaration text
that a terminal, Markdown renderer, JSON serializer, or source consumer may
show.

The containment invariant is:

> No artifact-controlled control, format, surrogate, line-separator, or
> paragraph-separator scalar reaches `CSharpDeclarationText` as an active
> display control, and every raw identifier or literal is made legal and
> visible according to its declared C# slot without changing the source model.

CSharp consumes the identifier and textual-containment policy owned by
CSharpText; it does not create a competing scalar policy. Current
`CSharpIdentifierCore` does not enforce the full scalar set above, so that part
of the target is pending on
[#5134](https://github.com/richlander/dotnet-inspect/issues/5134).
Structured punctuation comes only from fixed product syntax. Rendered fragments
must arrive under a typed fragment contract or remain compatibility input.

`CSharpDeclarationProvenanceTests.RepresentableOutputContainsTheMetadataConfusionFixture`,
`FallbackCompatibilityOutputAndDiagnosticsContainTheMetadataConfusionFixture`,
and the
slot-collision gates in [Verification](#verification) are the planned
enforcement. The invariant is unverified until those gates exist.

Local repository actors and callers that deliberately bypass the public
formatter contract are outside this product threat model. Ordinary code review,
not stronger declaration types, governs those cases.

## Demo mockup

The implementation PR must turn this CSharp-owned mockup into a real compiled
or persisted metadata demonstration. Given a method generic parameter whose raw
name is `class` and a parameter whose raw name is `event`, the structured
declaration is:

```csharp
void Map<@class>(int @event, string text = "class");
```

What to notice:

- the generic declaration and parameter name are identifier slots while the
  primitive and string literal keep their own meanings;
- the default expression is not searched for the generic-parameter name; and
- the raw metadata name remains `class`.

Two additional end-to-end examples are prerequisites rather than current
CSharp-only claims. After
[#5114](https://github.com/richlander/dotnet-inspect/issues/5114)
supplies a Metadata-owned qualifier/name handoff, an owner generic parameter
named `class` in an explicit-interface qualifier must render the member head as
`void I<@class>.Map();`.

The failed structured composition from #4656 was:

```csharp
void I<class>.Map();
```

That example and its gate remain pending on #5114; combined dotted text is
`FallbackRequired` with subordinate compatibility text until the handoff lands.

A neighboring provenance case must keep a primitive and a same-spelled generic
parameter distinct:

```csharp
void Pair<@int>(int primitive, @int parameter);
```

The final example and its gate remain pending on the additional type provenance
tracked by #5076. Until that currency exists, CSharp reports the affected
declaration as `FallbackRequired` rather than claiming representability.

## Immediate boundaries

### Inputs

CSharp consumes Metadata-issued declaration facts:

- `ApiType`, including exact definition-name segments, generic parameters,
  bases, interfaces, attributes, and modifiers;
- `ApiMember`, including exact metadata name, kind, attributes, modifiers, and
  compatibility signature;
- `ApiSignature`, `ApiParameter`, `ApiAccessor`, `TypeParameter`, and typed
  type-reference or type-shape evidence; and
- caller options that select qualification, import context, declaration
  abbreviation, attributes, punctuation, and declaration modifiers;
- body-owned facts that require declaration modifiers or finalizer spelling,
  but not body text;
- a typed declaration context that says whether a type is a root or a child of
  one exact declaring-type identity and carries exact type-binding evidence;
  and
- Metadata-issued operator and complete accessor-aggregate evidence required
  to admit those forms, pending #5164; and
- a Metadata-issued typed constant value required to admit non-enum constants,
  pending #5172; and
- Metadata-issued fixed-buffer source-field identity, element-type evidence,
  and length, pending #5178.

These inputs remain owned by Metadata or the caller. The formatter validates
the declaration context against exact definition-name segments but does not
define how a larger source composer constructs its request tree. This document
states only the preconditions CSharp requires. It does not define how Metadata
decodes, validates, persists, or associates them.

All target contracts remain SRM-only, Roslyn-free, NativeAOT-friendly, and
usable from browser/Wasm consumers. Introducing a platform-specific dependency
would require a separately approved exception.

### Outputs

The target public `CSharpDeclarationResult` preserves the four-arm
representability outcome required by Metadata's owning design:

- **Representable** carries `CSharpDeclarationText`, any required exact whole
  namespace identities, a `CSharpDeclarationReceipt`, and diagnostics.
- **FallbackRequired** carries a stable reason and the complete
  `ContainedTypeDeclaration` or `ContainedMemberDeclaration`. It may also carry
  subordinate `CSharpCompatibilityText`, its namespace requirements, and its
  opaque-boundary receipt for display, but that text is not authoritative C#.
- **Degraded** carries the signature status, bounded nonauthoritative evidence,
  and diagnostics, but no `CSharpDeclarationText` or metadata fallback.
- **Unavailable** carries the Metadata declaration failure and diagnostics, but
  no success-shaped empty declaration.

`CSharpCompatibilityText` is contained presentation currency for a legacy
declaration spelling. It does not claim C# representability and cannot replace
the complete contained fallback facts.

Any artifact-authored text inside bounded degradation evidence is an
`InertString` prepared under `TextPolicy.Field`; an ordinary string cannot carry
that evidence across the CSharp boundary.

`CSharpDeclarationReceipt` is an immutable public receipt for the exact
declaration plan. It contains one entry for every consumed slot occurrence,
including output-affecting closed choices whose selected value emits no text.
Each typed entry carries a closed `CSharpDeclarationSlotKind` and an immutable
semantic path made from closed role and ordinal segments, such as the path
represented by `parameter[1].name` or `accessor[0].kind`. The catalog maps its
kind to exactly one value class. An entry carries no raw artifact payload and
is never reconstructed from text or output offsets. Repeated slots therefore
remain independently observable.

The result constructor validates its arm against the receipt and catalog.
`Representable` requires the complete expected occurrence set for the selected
form plan and no opaque entry. Subordinate compatibility text on
`FallbackRequired` requires the complete expected occurrence set for its plan,
including every opaque boundary that replaces an unavailable structured
subtree, and at least one opaque entry. Text, outcome, and receipt cannot be
supplied as unrelated assertions.

An exact whole namespace identity preserves an owner-issued namespace string;
it does not claim segment identity. Typed Metadata evidence or an explicitly
identified caller namespace may supply one. A qualified-name spelling derived
from type text cannot mint an identity or enter the result's import set; it
remains spelling evidence, or the declaration visibly degrades when the
selected abbreviation requires an exact namespace.

The required namespace set is ordinal, duplicate-free, and exact for the
selected qualification mode:

- `Qualified` returns an empty set because every reference remains qualified,
  including synthesized attribute references that the compatibility path
  currently shortens;
- `ShortWithUsings` returns every non-containing exact namespace, and only those
  namespaces, whose references were actually shortened; and
- `ContextualShort` returns the exact subset of caller-supplied, non-containing
  namespace identities actually relied upon for shortening; it does not invent
  a new import.

`CSharpDeclarationText` is contained presentation currency, not a string
identity. Its construction is restricted to the CSharp composer and means that
no artifact-controlled display control remains active. The same containment
guarantee applies to subordinate `CSharpCompatibilityText`.

`Representable` additionally means that every artifact-derived value reached
the output through a complete declared slot plan. `FallbackRequired` preserves
the authoritative contained declaration even when optional compatibility text
crosses one or more explicitly contained opaque boundaries. Neither arm means
the declaration compiles, corresponds to a body, or identifies a metadata row.

Diagnostics carry a closed `CSharpDeclarationDiagnosticReason` and optional
`InertString Detail` constructed under the shared `TextPolicy.Field` policy.
Raw artifact-controlled text cannot be interpolated into an ordinary diagnostic
string, and this design does not create another scalar policy.

Existing string-returning formatter methods may remain compatibility adapters
during migration. New consumers that require the structured guarantee consume
`CSharpDeclarationResult`; they do not infer representability from text.
`CSharpFormatOptions.NamespacePolicy`,
`CSharpFormatter.FormatMemberUnit`, and `CSharpFormatter.FormatTypeUnit` are
current unit-composition compatibility surfaces; their namespace-wrapped
results do not acquire the target guarantee from this document. Issue #5142
owns their replacement or migration together with `CSharpTypePrinter`
aggregation.

### Adjacent owners

- **Metadata** supplies metadata facts and typed identities. CSharp does not
  mutate them or derive identity from their display text.
- **CSharpText** supplies model-free lexical operations. It may tokenize one
  declared type-expression or compatibility slot, but it does not decide which
  semantic slot a substring occupies. Issue #5134 owns the additional
  declaration-containment policy required by this target.
- **CSharpTypePrinter** composes declarations into complete source. Issue #5142
  owns unit, namespace/import, body, initializer, global-attribute, nesting,
  punctuation/placement, and aggregate outcome mechanics; this formatter
  contract supplies every declaration result, including enum-member and
  fixed-buffer heads.
- **CLI and other hosts** choose where to display or serialize the result. They
  do not repeat CSharp preparation.
- **Decompiler and Research** may request or compose declarations but do not
  own declaration spelling.

## Slot value classes

Every catalog entry selects exactly one value class. Composite declarations
contain child catalog entries; they do not combine classes in one entry.

| Value class | Input meaning | CSharp treatment |
| --- | --- | --- |
| Raw identifier | Exact metadata name for one identifier position | Apply visual containment and C# identifier escaping once, at that position |
| Qualified-name spelling | One metadata namespace or other qualified-name string whose dots have C# spelling semantics, not claimed metadata segment identity | Apply the CSharpText qualified-name grammar within the declared slot; reject or degrade empty, malformed, or unrepresentable components |
| Namespace identity | One exact whole owner-issued namespace string, without a segment-identity claim | Preserve the raw identity separately; use its contained qualified-name spelling only in the declared type-name context and never derive it from type display text |
| Type declaration name | Exact root-to-leaf metadata-name segments, introduced generic-parameter counts, and generic-parameter subplans | Validate per-segment arity ownership and the typed placement context, then compose a root's single name or an exact child leaf after parent-chain validation |
| Type expression | Model-bound type spelling plus all available type/generic-reference evidence | Render or lex within this slot; apply qualification, alias, and identifier policy without inspecting neighboring slots |
| Bound generic reference | Generic owner plus ordinal and raw declared name | Spell the bound declaration name as an identifier; equal text alone cannot construct this class |
| Type-binding evidence | Owner-issued exact type-definition identity plus its declared or imported binding context | Use only to decide binding and qualification; a full-name or display string cannot construct this class |
| Declaration admission evidence | Owner-issued identity, association, or shape evidence whose completeness lets CSharp decide one declaration form | Validate the typed evidence in CSharp before planning; a name, prefix, loose Boolean, or display spelling cannot stand in for it |
| Closed syntax | Enum, Boolean, or other bounded choice owned by product code | Map to a fixed keyword, modifier, punctuation, or empty choice; artifact text cannot enter |
| Rendered fragment | Producer-issued C# expression or attribute fragment whose internal provenance is no longer available | Preserve its syntax and contain it under its fragment contract; never search it for declaration identifiers |
| Raw literal value | Artifact value that CSharp itself places in a C# literal | Escape according to the selected literal form before composition |
| Composite subplan | Ordered child slots for a parameter, accessor, constraint, or other repeated declaration structure | Validate and prepare every child before joining them with fixed syntax |
| Opaque compatibility | Historical declaration text with no complete slot map | Keep on the fallback compatibility path; bounded model-free repair is allowed, but the result cannot be `Representable` |

A bare `string` does not establish any of these classes. The declaration plan
records the class at construction so later code cannot silently promote an
opaque value by passing it to a different helper. Missing evidence is not a
value class: it selects a fixed omitted/default syntax, `FallbackRequired`,
`Degraded`, or `Unavailable` according to the declaration form.

## Normative slot inventory

The implementation owns one data-driven catalog for this inventory. A slot may
be absent for declaration forms where it is inapplicable, but a new slot cannot
enter composition without a catalog entry and handler.

### Type slots

| Slot | Value class | Current source |
| --- | --- | --- |
| Type declaration name | Type declaration name | `ApiType.DefinitionName`, `IntroducedTypeParameterCounts`, and `TypeParameters` |
| Type declaration placement | Closed syntax | caller-issued root or exact parent identity; never inferred by comparing `Name`, `MetadataName`, or rendered text |
| Variance inclusion | Closed syntax | public `CSharpFormatter.FormatTypeName` `includeVariance` selector |
| Type kind | Closed syntax | `ApiType.Kind` |
| Type accessibility | Closed syntax | `ApiType.Accessibility` |
| Static type modifier | Closed syntax | `ApiType.IsStatic` |
| Abstract type modifier | Closed syntax | `ApiType.IsAbstract` |
| Sealed type modifier | Closed syntax | `ApiType.IsSealed` |
| Read-only type modifier | Closed syntax | `ApiType.IsReadOnly` |
| By-ref-like type modifier | Closed syntax | `ApiType.IsByRefLike` |
| Type generic-parameter name | Raw identifier | `ApiType.TypeParameters[].Name` |
| Type generic-parameter variance | Closed syntax | `ApiType.TypeParameters[].Variance` |
| Type-owned generic-parameter reference | Bound generic reference | owner/ordinal evidence within base, interface, constraint, and other type expressions |
| Primary-constructor parameter | Composite subplan | caller-supplied `ApiParameter` values |
| Enum underlying type | Type expression | `ApiType.EnumUnderlyingType`; absent/default `int` emits no clause |
| Base type | Type expression | `ApiType.BaseType` and typed reference evidence |
| Implemented interface | Type expression | `ApiType.Interfaces` and available typed reference evidence |
| Type special constraint | Closed syntax | non-type entries in `TypeParameter.StructuredConstraints` |
| Type constraint type | Type expression | type entries in `TypeParameter.StructuredConstraints` |
| Type attribute | Rendered fragment | `ApiType.Attributes` |

### Formatter option slots

Each output-affecting option is an independent slot. A broad "formatter
options" handler does not satisfy the catalog.

| Slot | Value class | Current source |
| --- | --- | --- |
| Type-name qualification mode | Closed syntax | `CSharpFormatOptions.TypeNamePolicy` |
| Containing namespace identity | Namespace identity | typed identity corresponding to `CSharpFormatOptions.ContainingNamespace` |
| Caller import namespace identity | Namespace identity | each typed identity corresponding to `CSharpFormatOptions.Usings` |
| Lexical shadowing identifier | Raw identifier | each `AdditionalShadowingNames` entry |
| Root-shadowing identifier | Raw identifier | each `AdditionalRootShadowingNames` entry |
| Unresolvable-root identifier | Raw identifier | each `AdditionalUnresolvableRootNames` entry |
| Declared-type binding | Type-binding evidence | exact definition identity and lexical context from the typed declaration context; `AdditionalDeclaredTypeFullNames` is compatibility input only |
| Imported declared-type binding | Type-binding evidence | exact definition identity and import context from the typed declaration context; `AdditionalImportedDeclaredTypeFullNames` is compatibility input only |
| Known namespace identity | Namespace identity | each typed identity corresponding to `AdditionalKnownNamespaces` |
| Signature abbreviation mode | Closed syntax | `CSharpFormatOptions.AbbreviateSignature` |
| Member terminator mode | Closed syntax | `CSharpFormatOptions.TerminateMemberDeclaration` |
| Forced `async` modifier | Closed syntax | `CSharpFormatOptions.ForceAsync` |
| Forced `unsafe` modifier | Closed syntax | `CSharpFormatOptions.ForceUnsafe` |
| Custom-attribute inclusion | Closed syntax | `CSharpFormatOptions.IncludeCustomAttributes` |
| Signature-attribute inclusion | Closed syntax | `CSharpFormatOptions.IncludeSignatureAttributes` |
| Synthesized-obsolete inclusion | Closed syntax | `CSharpFormatOptions.IncludeObsoleteAttribute` |
| Interface-modifier omission | Closed syntax | `CSharpFormatOptions.OmitInterfaceMemberModifiers` |
| Property-accessor omission | Closed syntax | `CSharpFormatOptions.OmitPropertyAccessors` |

### Common member slots

| Slot | Value class | Current source |
| --- | --- | --- |
| Member attribute | Rendered fragment | `ApiMember.Attributes` |
| Return attribute | Rendered fragment | `ApiSignature.ReturnAttributes` |
| Member declaration kind | Closed syntax | `ApiMember.Kind`, constructor name, and exact finalizer discriminator |
| Signature degradation status | Closed syntax | `ApiMember.SignatureDecodeStatus`; null means no degradation was reported, while `Degraded` refuses rendering |
| Operator admission and spelling | Declaration admission evidence | neutral Metadata-issued operator candidate containing exact method identity and name, `SpecialName`, staticness, and complete signature shape; CSharp validates form/arity and maps the admitted name through its bounded operator catalog, including checked and conversion variants |
| Indexer token selection | Closed syntax | owner-issued `ApiSignature.MemberName == "this[]"` sentinel after property-form validation; literal punctuation in a raw metadata name cannot select it |
| Complete accessor aggregate | Declaration admission evidence | Metadata-issued aggregate containing every associated semantic role and method identity; only complete get/set/init or add/remove aggregates admit representable C# accessor syntax |
| Member accessibility | Closed syntax | `ApiMember.Accessibility` |
| Constant modifier | Closed syntax | `ApiMember.IsConst` |
| Constant value kind | Closed syntax | typed Metadata constant kind pending #5172 |
| Constant value | Raw literal value | typed Metadata non-enum field constant pending #5172 |
| Static member modifier | Closed syntax | `ApiMember.IsStatic` |
| Read-only member modifier | Closed syntax | `ApiMember.IsReadOnly` |
| Sealed member modifier | Closed syntax | `ApiMember.IsSealed` |
| Abstract member modifier | Closed syntax | `ApiMember.IsAbstract` |
| Override member modifier | Closed syntax | `ApiMember.IsOverride` |
| Virtual member modifier | Closed syntax | `ApiMember.IsVirtual` |
| Unsafe member modifier | Closed syntax | `ApiMember.IsUnsafe` |
| Async member modifier | Closed syntax | `ApiMember.IsAsync` |
| Body-required unsafe modifier | Closed syntax | `CSharpMemberBody.RequiresUnsafeModifier` when the public body-aware declaration seam is used |
| Body-required async modifier | Closed syntax | `CSharpMemberBody.RequiresAsyncModifier` when the public body-aware declaration seam is used |
| Required-member modifier | Closed syntax | `ApiSignature.IsRequired` |
| Extension-receiver presence | Closed syntax | `ApiMember.IsExtension` |
| Return, field, property, or event type | Type expression | `ApiSignature.ReturnType` or `ApiMember.ReturnType`, with typed evidence when available |
| Simple member name | Raw identifier | exact `ApiMember.Name` or a separately issued simple-name slot |
| Explicit-interface qualifier | Type expression | requires an owner-issued qualifier separate from the simple name |
| Method generic-parameter declaration | Raw identifier | `ApiSignature.TypeParameters` or an explicitly paired caller override |
| Type- or method-owned generic-parameter reference | Bound generic reference | owner kind plus ordinal in member type-expression evidence; equal rendered text is insufficient |
| Method special constraint | Closed syntax | non-type entries in `TypeParameter.StructuredConstraints`; inherited-restatement policy remains CSharp-owned |
| Inherited method constraint restatement | Closed syntax | each method `TypeParameter.TypeKind`, mapped only to the closed `class`, `struct`, `default`, or omitted choices |
| Method constraint type | Type expression | type entries in `TypeParameter.StructuredConstraints` |
| Parameter | Composite subplan | ordered parameter child slots |
| Parameter attribute | Rendered fragment | `ApiParameter.Attributes` |
| Parameter modifier | Closed syntax | `ApiParameter.Modifier` |
| Parameter type | Type expression | `ApiParameter.Type` and typed evidence when available |
| Parameter name | Raw identifier | `ApiParameter.Name` |
| Parameter default presence | Closed syntax | `ApiParameter.HasDefault` |
| Parameter default | Rendered fragment | `ApiParameter.DefaultValueText` |
| Accessor | Composite subplan | ordered accessor child slots |
| Accessor return attribute | Rendered fragment | `ApiAccessor.ReturnAttributes` |
| Accessor accessibility | Closed syntax | `ApiAccessor.Accessibility` |
| Accessor kind | Closed syntax | `ApiAccessor.Kind` |
| Standalone accessor selection | Declaration admission evidence | typed caller selection bound to one exact child of the member's complete accessor aggregate; an arbitrary `string kind` cannot establish this slot |
| Finalizer spelling mode | Closed syntax | `SuppressFinalizerSpelling` body-fidelity choice |
| Synthesized-obsolete presence | Closed syntax | `ApiMember.IsObsolete` |
| Obsolete message | Raw literal value | `ApiMember.ObsoleteMessage` |
| Enum-member name | Raw identifier | exact enum field name |
| Fixed-buffer declaration kind | Closed syntax | validated fixed-buffer form rather than an ordinary field, pending #5178 |
| Fixed-buffer evidence | Declaration admission evidence | Metadata-issued source-field identity and decoded `FixedBufferAttribute` evidence, pending #5178 |
| Fixed-buffer element type | Type expression | typed element-type evidence from the validated fixed-buffer form, pending #5178 |
| Fixed-buffer name | Raw identifier | exact source-field name |
| Fixed-buffer length | Raw literal value | validated positive fixed-buffer length, pending #5178 |

### Compatibility slots

Every subordinate compatibility path has an explicit opaque slot. A declaration
cannot become `FallbackRequired` merely because a structured handler declined
it; the arm also requires the complete contained declaration.

| Slot | Value class | Current source |
| --- | --- | --- |
| Legacy flattened type declaration name | Opaque compatibility | `ApiType.Name` when exact definition-name segments or arity ownership are unavailable |
| Opaque member declaration | Opaque compatibility | `ApiMember.Signature` when no complete structured signature can be formed |
| Combined explicit-interface name | Opaque compatibility | combined `ApiMember.Name` or `ApiSignature.MemberName` pending #5114 |
| Display-derived type-binding context | Opaque compatibility | legacy declared/imported type full-name strings without exact definition identities |
| Unproven type-expression occurrence | Opaque compatibility | one exact base, interface, constraint, return, field, property, event, or parameter type spelling whose required binding or generic-owner provenance is unavailable, including #5076 |
| Unstructured type constraint | Opaque compatibility | a `TypeParameter.Constraints` entry without structured constraint evidence |
| Unstructured method constraint | Opaque compatibility | a method `TypeParameter.Constraints` entry without structured constraint evidence |

### Declaration-form requirements

| Form | Additional required structure |
| --- | --- |
| Class, struct, interface, record, or enum type | Exact type declaration name, typed root or parent/child placement, closed kind/modifiers, generic parameters, bases/interfaces, and constraints as applicable |
| Constructor | Exact declaring-type leaf name and parameters |
| Static constructor | Exact declaring-type leaf name and closed punctuation |
| Finalizer with destructor spelling | Exact declaring-type leaf name and closed destructor marker |
| Finalizer with destructor spelling suppressed | Closed body-fidelity selector plus structured `void Finalize()` method head |
| Ordinary or extension method | Return type, simple name, method generic parameters, parameters, and constraints; extension `this` is closed syntax |
| Explicit-interface method | Separate qualifier type expression and simple member name; pending #5114, a combined dotted string is compatibility input |
| Property | Property type, simple name, complete accessor aggregate, and accessor children; a result-contract outcome is pending #5164 |
| Property or indexer head with accessors omitted | The corresponding property/indexer head slots, complete accessor aggregate, and a closed omission choice; accessor child slots are deliberately absent and a result-contract outcome is pending #5164 |
| Indexer | Property type, closed `this` token, index parameters, complete accessor aggregate, and accessor children; a result-contract outcome is pending #5164 |
| Explicit-interface property or indexer | Separate qualifier, simple name or closed `this` token, parameters, complete accessor aggregate, and accessor children; a result-contract outcome is pending #5164, after which #5114 keeps combined explicit-interface text `FallbackRequired` until its separate handoff lands |
| Event | Event type, simple name, complete accessor aggregate, and optional explicit-interface qualifier; a result-contract outcome is pending #5164, after which explicit forms also require #5114 |
| Field | Field type and simple name |
| Non-enum constant | Field type, simple name, typed constant kind/value, and fixed initializer syntax; a result-contract outcome is pending #5172 |
| Enum member | Exact member name; initializer and trailing comma are separate composer children |
| Fixed-buffer field | Validated fixed-buffer evidence, closed `fixed` form, typed element type, exact field name, and validated length; a result-contract outcome is pending #5178 |
| Unary, binary, conversion, or checked operator | Neutral Metadata-issued operator candidate with exact identity, name, flags, and complete signature shape; CSharp validates `SpecialName`, staticness, form/arity, and its closed token catalog; a result-contract outcome is pending #5164 |
| Delegate | Return type, exact type declaration name, typed root or parent/child placement, generic parameters, parameters, and constraints |
| Standalone accessor head | Typed selection of one exact child in the complete aggregate, accessor return attributes, accessor-specific accessibility, and closed accessor kind; a result-contract outcome is pending #5164 |
| Abbreviated member declaration | The selected member head plus a closed abbreviation choice; omitted parameter-name, default, and accessor child slots are not treated as consumed |
| Terminated member declaration | The selected member form plus a closed terminator choice |

This table covers the target per-declaration planner, including public
standalone accessor heads, enum-member and fixed-buffer heads, and deliberate
declaration abbreviations. It does not claim every legacy convenience method
currently located on `CSharpFormatter`. Compilation units, rendered imports,
global attributes, initializers, bodies, enum values, punctuation/placement,
and member grouping remain with #5142's complete-source composer.

## Composition rules

1. Build the declaration plan from known fields and explicit typed handoffs.
   The plan records each slot's semantic position and value class.
2. Validate the form before rendering. Missing structure selects
   `FallbackRequired`, `Degraded`, or `Unavailable`; it never manufactures a
   boundary.
3. Prepare every slot independently. Raw identities remain untouched in the
   source model.
4. Compose prepared values only with CSharp-owned fixed syntax and layout.
5. Issue `CSharpDeclarationText` only after every emitted artifact-derived value
   has crossed either a structured slot handler or the contained compatibility
   boundary. Return `Representable` only when the complete form plan used
   non-opaque slots. Seal the same plan's occurrence receipt into the result; no
   separate pass may attest which slots were consumed.
The structured path therefore forbids:

- applying identifier substitutions to the complete declaration;
- finding the member or parameter list by searching completed output;
- splitting a combined explicit-interface name at a guessed dot;
- selecting a nested declaration leaf by comparing compatibility display
  strings;
- treating an equal string as proof of a generic-parameter reference;
- scanning default expressions or attribute arguments for declaration names;
  and
- reparsing final text to rebuild identity, correspondence, or provenance.

Slot-local model-free grammar is allowed. For example, CSharpText may lex a type
expression to distinguish identifier positions from `ref`, tuple punctuation,
or function-pointer syntax. That operation is bounded to the already-declared
type slot and does not assign provenance the input did not carry.

## Explicit-interface handoff

An explicit-interface declaration requires two independent values:

1. the qualifier as a type expression, including generic-argument evidence; and
2. the member as a simple raw identifier or the closed indexer token.

Current `ApiMember.Name` and `ApiSignature.MemberName` may contain both in one
dotted string. A last-dot split is not an owner-issued boundary: metadata names
may themselves contain punctuation, and equal text does not establish whether a
generic argument is a primitive, named type, or generic parameter.

Until an adjacent producer supplies the two values separately, these forms are
`FallbackRequired` and any legacy spelling remains subordinate compatibility
text. Metadata issue
[#5114](https://github.com/richlander/dotnet-inspect/issues/5114)
owns that prerequisite. This design does not specify its extraction or
persistence mechanics.

## Fallback and compatibility migration

`ApiMember.Signature` remains useful for older serialized surfaces and
declaration forms whose representability facts are incomplete. Subordinate
compatibility text may retain the current bounded lexical repairs, but it has
five restrictions:

- it appears only on `FallbackRequired`;
- it cannot replace or discard the complete contained declaration;
- its complete output and diagnostics satisfy the same display-containment
  invariant as representable output;
- it cannot be converted to `Representable` after rescanning; and
- consumers requiring compile-back or a structured containment claim reject it
  or surface the degradation.

Migration proceeds by declaration form, not by adding another whole-string
pass:

1. introduce the four result arms, text currencies, receipt, and slot catalog;
2. route currently structured type, constructor, ordinary method, field,
   enum-member, parameter, and constraint paths through plans;
3. keep pre-#5164 operator and accessor-bearing string methods as legacy
   adapters outside `CSharpDeclarationResult`: their current projections cannot
   supply either representability evidence or the complete contained facts
   required by `FallbackRequired`. Consume #5164's neutral operator candidate
   and complete accessor aggregate before routing operator, property, indexer,
   event, or standalone-accessor forms through the result contract;
4. keep non-enum constant string methods outside `CSharpDeclarationResult`
   until #5172 supplies the typed Constant-row value, then make CSharp own its
   literal spelling and fixed initializer syntax;
5. keep reader-bound fixed-buffer shell helpers outside
   `CSharpDeclarationResult` until #5178 supplies exact source-field,
   element-type, and length evidence;
6. consume #5114's owner-issued explicit-interface handoff before promoting
   those forms;
7. remove a compatibility repair only after its final form has representable
   coverage and close negative cases; and
8. address #5076 separately before claiming primitive/generic alias fidelity.

Existing caller-visible text remains unchanged for ordinary metadata except for
one explicit target correction: under `Qualified`, synthesized attribute
references such as `System.Obsolete` become qualified rather than relying on an
unreported namespace. All other changes require separate demonstration and
approval. Outcomes, receipts, and diagnostics add evidence; they do not
silently replace output with an empty string.

## Failure and degradation

Stable reasons include at least:

- missing structured signature;
- degraded metadata signature;
- missing explicit-interface boundary;
- unavailable generic-reference provenance;
- unavailable exact type-binding evidence;
- missing or invalid operator admission evidence;
- incomplete or unsupported accessor aggregate;
- invalid standalone accessor selection;
- unsupported declaration kind;
- invalid closed syntax value; and
- unsupported rendered fragment.

Neither a populated `ApiSignature` containing placeholder types nor
`ApiMember.Signature` may turn `SignatureDecodeStatus.Degraded` into
`Representable` or `FallbackRequired`. Null status carries no degradation
assertion, including for older persisted models, so the remaining structured or
opaque evidence decides the outcome normally.

An `op_` method without Metadata `SpecialName` evidence remains an ordinary
method when its ordinary method facts are representable; its name never selects
operator syntax. A `SpecialName` operator candidate that fails CSharp catalog,
staticness, arity, or conversion-shape validation selects `FallbackRequired`
rather than being relabeled as an ordinary method. An aggregate containing
`raise`, `Other`, an unbound semantic method, or a synthesized accessor is not a
complete representable C# accessor aggregate. It selects `FallbackRequired`
with the complete contained declaration and every associated semantic identity.
A well-formed typed standalone accessor selector that does not bind one exact
aggregate child likewise selects `FallbackRequired` when the contained
declaration is complete, or `Unavailable` only when Metadata reports an actual
declaration failure; an undefined selector enum remains a programmer error.
An inconsistent caller-issued nested declaration context is also a programmer
error. A well-formed nested request that CSharp cannot place as a standalone
declaration selects `FallbackRequired` with its complete contained type.

`SignatureDecodeStatus.Degraded` selects `Degraded` and preserves the bounded
nonauthoritative evidence allowed by the Metadata design. It never carries
`CSharpDeclarationText`, subordinate compatibility text, or a metadata
fallback. `Unavailable` is reserved for a Metadata declaration failure,
including a persisted compatibility input that carries such a failure.

Neither `FallbackRequired`, `Degraded`, nor `Unavailable` is an
exception-shaped success or an empty declaration. Programmer errors such as an
undefined enum remain argument errors. Artifact-caused incompleteness is a typed
outcome or diagnostic.

## Verification

The implementation must add these named gates; until then the corresponding
properties remain unverified:

- `CSharpDeclarationSlotCatalogTests.DeclaredSlotsAndHandlersAgree` derives the
  complete slot-to-value-class map, dependency tags, and active handler set from
  the normative code catalog. It fails for undefined classes, entries that mix
  value classes, missing or stale active handlers, and a handler that activates
  a dependency-deferred slot before its owner-issued evidence exists.
- `CSharpDeclarationSlotCatalogTests.PublicDeclarationInputsAndCatalogAgree`
  derives every output-affecting argument, option, typed context field, and
  body-to-declaration handoff from public methods that compose a declaration,
  declaration head, or cataloged declaration subplan. It requires each to map
  to a cataloged slot or an explicit non-composer exclusion and fails when a
  convenience adapter bypasses the target result. Slot-local lexical helpers,
  constructor-initializer fragments, `CSharpFormatOptions.NamespacePolicy`,
  `FormatMemberUnit`, and `FormatTypeUnit` unit composition are enumerated
  exclusions rather than silently absent inputs. Pre-#5164 operator and
  accessor-bearing branches are enumerated temporary legacy exclusions; their
  sibling representable forms still map through the result. Pre-#5172 non-enum
  constant and pre-#5178 fixed-buffer branches are likewise enumerated temporary
  exclusions.
- `CSharpDeclarationSlotCatalogTests.ResultArmsRequireExactConsumedSlotSet`
  derives the expected slots for every admitted form from that catalog, compares
  them with the slots actually consumed by the public composition path, and
  fails for missing, duplicate, stale, or bypassed handlers and occurrence
  paths. It also derives every dependency-deferred form and proves that its
  legacy adapter exposes no `CSharpDeclarationResult` arm before the dependency
  lands.
  It asserts the same exact set through the public
  `CSharpDeclarationReceipt`. `Representable` requires no opaque slot;
  subordinate compatibility text on `FallbackRequired` requires every used
  opaque boundary to appear in the receipt and at least one such boundary.
  Catalog/handler self-consistency alone cannot issue either arm.
- `CSharpDeclarationProvenanceTests.RepresentableCompositionDoesNotRescanFinalText`
  uses colliding identifiers inside a default literal, attribute argument,
  return type, qualifier, and member name to prove substitutions stay in their
  slots.
- `CSharpDeclarationProvenanceTests.ExplicitInterfaceGenericKeyword_IsEscapedWithoutChangingRawIdentity`
  remains pending on #5114, then exercises a real compiled or persisted metadata
  artifact and verifies both `I<@class>.Map` and unchanged raw names.
- `CSharpDeclarationProvenanceTests.PrimitiveAndSameNamedGenericRemainDistinct`
  is the #5076 gate and remains pending until that issue supplies provenance;
  it covers type- and method-owned references in bases, interfaces, constraints,
  fields, returns, and parameters.
- `CSharpDeclarationProvenanceTests.EveryDeclarationFormHasExpectedOutcomeAndNeighborCases`
  derives every row of the form inventory and verifies its expected
  `Representable`, `FallbackRequired`, `Degraded`, or `Unavailable` outcome,
  or its explicit dependency exclusion. Coverage includes every type kind and
  root/nested placement, constructors, properties, indexers, events, operators,
  fields, constants, both finalizer spellings, bases, interfaces, constraints,
  standalone accessor heads, enum-member and fixed-buffer heads,
  abbreviated/head-only declarations, and terminated declarations.
- `CSharpDeclarationProvenanceTests.ClosedSyntaxSelectorsHaveNeighborCoverage`
  varies each admitted formatter option and metadata discriminator
  independently,
  including required-member presence, inherited `TypeParameter.TypeKind`,
  parameter-default presence, extension-receiver presence,
  synthesized-obsolete presence and inclusion, operator and indexer selection,
  signature degradation, body-required and forced modifiers, abbreviation,
  accessor omission, standalone accessor selection, attribute inclusion,
  variance inclusion, enum-member and fixed-buffer form selection, and
  termination;
  each pair must change only its declared slot set and output syntax.
  Dependency-deferred operator/accessor, non-enum constant, and fixed-buffer
  selectors instead prove that no result-contract entry point accepts them
  before #5164, #5172, or #5178.
- `CSharpDeclarationProvenanceTests.OperatorAdmissionValidatesMetadataCandidate`
  remains pending on #5164, then
  uses otherwise identical operator names with and without `SpecialName`,
  staticness, valid arity, and valid conversion shape. Metadata supplies neutral
  candidate evidence; only CSharp validation can select operator syntax, and an
  `op_` prefix never can.
- `CSharpDeclarationProvenanceTests.AccessorAggregateMustBeCompleteAndRepresentable`
  remains pending on #5164, then
  pairs ordinary getter/setter and add/remove aggregates with metadata
  `raise`/`Other`, missing association identities, and synthesized fallback
  accessors. Unsupported but complete aggregates select `FallbackRequired`
  with every contained association; degraded or failed aggregates preserve
  their owning outcome rather than emitting representable property or event
  syntax.
- `CSharpDeclarationProvenanceTests.StandaloneAccessorSelectionBindsExactChild`
  remains pending on #5164, then
  proves that each typed selection resolves to one aggregate child and that an
  absent, mismatched, arbitrary, or undefined kind cannot emit an accessor
  head.
- `CSharpDeclarationProvenanceTests.NonEnumConstantRequiresTypedValue`
  remains pending on #5172, then pairs identical constant types/names with every
  supported typed value kind and malformed/missing rows. CSharp owns the literal
  slot and fixed initializer syntax; only complete Metadata evidence can enter
  the result contract.
- `CSharpDeclarationProvenanceTests.QualificationContextChildrenHaveIndependentReceipts`
  distinguishes containing namespace, caller import, every shadowing set,
  declared/imported type binding, and known namespace evidence. In particular,
  a containing namespace that shortens `N.T` and a caller import that shortens
  the same type produce different receipts and namespace-requirement sets.
- `CSharpDeclarationProvenanceTests.TypeBindingEvidenceDoesNotComeFromDisplayText`
  varies `ApiType.Name`, flattened full-name strings, and rendered type text
  while holding exact definition identities fixed, then varies only the exact
  identities. Only the latter may change declared/imported binding decisions.
- `CSharpDeclarationProvenanceTests.MissingTypeExpressionProvenanceIsOccurrenceScoped`
  uses equal unproven type spellings in base, interface, constraint, return, and
  parameter positions. Each becomes a distinct opaque occurrence in
  subordinate fallback compatibility text while the complete contained
  declaration remains independently set-equal; no occurrence can authorize
  another.
- `CSharpDeclarationProvenanceTests.NestedTypePlacementDoesNotComeFromDisplayText`
  varies only legacy `Name`/`MetadataName` spellings and proves that the typed
  declaration context selects an exact child leaf. An inconsistent
  caller-supplied parent identity, namespace, or segment depth is an argument
  error; a well-formed unsupported standalone nested request selects
  `FallbackRequired` with the complete contained type, and `Unavailable` occurs
  only for a supplied Metadata declaration failure.
- `CSharpDeclarationProvenanceTests.LegacyFlattenedTypeNameWithLiteralPunctuationIsNotRepresentable`
  proves that `ApiType.Name` cannot stand in for exact definition-name segments.
- `CSharpDeclarationProvenanceTests.TypeNameArityOwnershipIsRequiredForRepresentableNestedGenerics`
  exercises exact nested segments, per-segment parameter ownership, malformed
  arity, and the legacy fallback.
- `CSharpDeclarationProvenanceTests.NamespaceSpellingDoesNotClaimMetadataSegments`
  exercises keywords, empty components, repeated dots, and literal punctuation,
  preserving an owner-issued exact whole namespace while proving that a
  type-text-derived spelling cannot enter the declaration result's
  identity-bearing import set.
- `CSharpDeclarationProvenanceTests.RequiredNamespaceIdentitiesExactlyMatchShortenedReferences`
  covers zero, one, duplicate, missing, and stale namespace requirements under
  `Qualified`, `ShortWithUsings`, and `ContextualShort`, and compares the exact
  returned set with every reference the public declaration path shortened.
- `CSharpDeclarationProvenanceTests.QualifiedSynthesizedAttributesRemainQualified`
  demonstrates the intentional compatibility-path change from `[Obsolete]` to
  `[System.Obsolete]`, verifies an empty required-namespace set, and covers a
  neighboring explicitly imported attribute under the shortening modes.
- `CSharpDeclarationProvenanceTests.MissingStructureIsVisible`
  proves that each target failure selects `FallbackRequired`, `Degraded`, or
  `Unavailable`, never `Representable` or success-shaped empty output, and that
  fallback and degradation payloads retain the Metadata-owned facts.
- `CSharpDeclarationProvenanceTests.MetadataRepresentabilityOutcomeIsLossless`
  exercises all four source outcomes through the public CSharp boundary,
  asserts set equality for every contained fallback fact, preserves the exact
  degradation status and bounded evidence, and proves that only an actual
  Metadata declaration failure becomes `Unavailable`.
- `CSharpDeclarationProvenanceTests.DegradedSignaturePreservesEvidenceWithoutRendering`
  pairs null and `Degraded` status with otherwise identical populated
  structured and opaque signatures, proving that only the null neighbor may
  render and that degraded placeholders return `Degraded` with bounded
  evidence but no declaration or compatibility text.
- `CSharpDeclarationProvenanceTests.RepresentableOutputContainsTheMetadataConfusionFixture`
  remains pending on #5134's scalar policy, then
  resolves the immutable version named by
  `PackageFixtures.proj`'s `MetadataConfusionFixtureVersion` property and runs
  it through the public CSharp seam. The implementation must advance that
  property to a new fixture version whose manifest names keyword-generic and
  cross-slot literal-collision specimen IDs, and the gate asserts those IDs
  before checking inert, single-declaration output. #5114 adds
  explicit-interface specimens in its owner effort.
- `CSharpDeclarationProvenanceTests.FallbackCompatibilityOutputAndDiagnosticsContainTheMetadataConfusionFixture`
  remains pending on #5134, then forces opaque compatibility declarations and
  artifact-derived diagnostics, verifies that both are inert and line-safe,
  and asserts the complete contained fallback independently.
- `CSharpDeclarationProvenanceTests.DiagnosticsUseClosedReasonsAndInertDetails`
  proves independently of #5134 that every diagnostic reason is a closed enum,
  every optional detail is an `InertString` constructed under
  `TextPolicy.Field`, and no raw artifact name enters an ordinary message
  string.
- `CSharpDeclarationProvenanceTests.FinalizerSpellingModePreservesBodyFidelity`
  covers destructor syntax and the representable literal-`Finalize` alternative
  selected when body fidelity suppresses destructor reconstruction.
- `CSharpDeclarationProvenanceTests.BodyRequiredModifiersHaveIndependentReceipts`
  varies body-required and formatter-forced `async`/`unsafe` independently and
  proves each public body-aware declaration result records both selectors
  without treating body text as a declaration slot.
- `CSharpDeclarationProvenanceTests.EnumMemberHeadsAreFormatterOwned`
  proves exact enum-member names pass through declaration slots while enum
  initializers and separators remain separate composer children.
- `CSharpDeclarationProvenanceTests.FixedBufferHeadsAreFormatterOwned`
  remains pending on #5178, then proves fixed-buffer element types, names, and
  lengths pass through declaration slots while placement remains a separate
  composer concern.

The implementation PR must run the non-pending gates in Release and show the
CSharp-owned demo's actual output beside a neighboring clean declaration. A
synthetic unit case may isolate a slot handler, but the explicit-interface claim
remains pending until #5114 supplies real Metadata evidence, and the full
scalar-containment claim remains pending until #5134 supplies its CSharpText
contract.

## Non-claims

This owner does not define:

- Metadata decoding, identity, extraction, persistence, or safety budgets;
- API artifact JSON schema or preparation;
- CLI section selection, Markdown/JSON rendering, or Markout behavior;
- API-to-body, accessor, or call-graph correspondence;
- Decompiler body fidelity or compile-back admission;
- the Metadata operator/accessor evidence tracked by #5164;
- the Metadata non-enum constant-value evidence tracked by #5172;
- the Metadata fixed-buffer declaration evidence tracked by #5178;
- `CSharpTypePrinter` request-tree, compilation-unit, namespace/import, global
  attribute, initializer, body-fragment, or aggregate outcome mechanics tracked
  by #5142;
- CSharpText's lexical grammar; or
- the missing primitive-versus-generic provenance tracked by #5076.

The full scalar-containment policy is likewise not defined here; #5134 owns it.

It also does not promise that every ECMA-335 declaration is representable in
C#. `FallbackRequired`, `Degraded`, and `Unavailable` are intentional,
evidence-preserving outcomes where the input does not support authoritative
CSharp declaration text.
