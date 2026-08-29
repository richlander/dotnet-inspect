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
spelling, type declaration name, type expression, bound generic reference,
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

`CSharpDeclarationProvenanceTests.StructuredOutputContainsTheMetadataConfusionFixture`,
`CompatibilityOutputAndDiagnosticsContainTheMetadataConfusionFixture`, and the
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
`Compatibility` until the handoff lands.

A neighboring provenance case must keep a primitive and a same-spelled generic
parameter distinct:

```csharp
void Pair<@int>(int primitive, @int parameter);
```

The final example and its gate remain pending on the additional type provenance
tracked by #5076. Until that currency exists, CSharp reports the affected
declaration as `Compatibility` or `Unavailable` rather than claiming structured
fidelity.

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
  abbreviation, attributes, punctuation, and declaration modifiers; and
- a typed declaration context that says whether a type is a root or a child of
  one exact declaring-type identity.

These inputs remain owned by Metadata or the caller. The formatter validates
the declaration context against exact definition-name segments but does not
define how a larger source composer constructs its request tree. This document
states only the preconditions CSharp requires. It does not define how Metadata
decodes, validates, persists, or associates them.

All target contracts remain SRM-only, Roslyn-free, NativeAOT-friendly, and
usable from browser/Wasm consumers. Introducing a platform-specific dependency
would require a separately approved exception.

### Outputs

The target public result is a closed `CSharpDeclarationResult` with two arms:

- **Rendered** carries `CSharpDeclarationText`, any required exact whole
  namespace identities, a `Structured` or `Compatibility` mode, and
  diagnostics.
- **Unavailable** carries a stable reason and diagnostics, but no
  success-shaped empty declaration.

An exact whole namespace identity preserves an owner-issued namespace string;
it does not claim segment identity. Typed Metadata evidence or an explicitly
identified caller namespace may supply one. A qualified-name spelling derived
from type text cannot mint an identity or enter the result's import set; it
remains spelling evidence, or the declaration visibly degrades when the
selected abbreviation requires an exact namespace.

The required namespace set is ordinal, duplicate-free, and exact for the
selected qualification mode:

- `Qualified` returns an empty set because every reference remains qualified;
- `ShortWithUsings` returns every non-containing exact namespace, and only those
  namespaces, whose references were actually shortened; and
- `ContextualShort` returns the exact subset of caller-supplied, non-containing
  namespace identities actually relied upon for shortening; it does not invent
  a new import.

`CSharpDeclarationText` is contained presentation currency, not a string
identity. Its construction is restricted to the CSharp composer and means that
no artifact-controlled display control remains active. That containment
guarantee applies to both rendered modes.

`Structured` additionally means that every artifact-derived value reached the
output through a complete declared slot plan. `Compatibility` means that one or
more opaque fragments crossed explicitly contained compatibility boundaries;
it does not claim internal slot provenance. Neither mode means the declaration
compiles, corresponds to a body, or identifies a metadata row.

Diagnostics carry a closed `CSharpDeclarationDiagnosticReason` and optional
`InertString Detail` constructed under the shared `TextPolicy.Field` policy.
Raw artifact-controlled text cannot be interpolated into an ordinary diagnostic
string, and this design does not create another scalar policy.

Existing string-returning formatter methods may remain compatibility adapters
during migration. New consumers that require the structured guarantee consume
`CSharpDeclarationResult`; they do not infer success or mode from text.
`CSharpFormatter.FormatTypeUnit` is also a current compatibility surface; its
complete-unit result does not acquire the target guarantee from this document.
Issue #5142 owns its replacement or migration together with
`CSharpTypePrinter` aggregation.

### Adjacent owners

- **Metadata** supplies metadata facts and typed identities. CSharp does not
  mutate them or derive identity from their display text.
- **CSharpText** supplies model-free lexical operations. It may tokenize one
  declared type-expression or compatibility slot, but it does not decide which
  semantic slot a substring occupies. Issue #5134 owns the additional
  declaration-containment policy required by this target.
- **CSharpTypePrinter** composes declarations into complete source. Issue #5142
  owns unit, namespace/import, body, initializer, global-attribute, nesting,
  and aggregate outcome mechanics; this formatter contract supplies its
  declaration result.
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
| Type declaration name | Exact root-to-leaf metadata-name segments, introduced generic-parameter counts, and generic-parameter subplans | Validate per-segment arity ownership and the typed placement context, then compose a root's single name or an exact child leaf after parent-chain validation |
| Type expression | Model-bound type spelling plus all available type/generic-reference evidence | Render or lex within this slot; apply qualification, alias, and identifier policy without inspecting neighboring slots |
| Bound generic reference | Generic owner plus ordinal and raw declared name | Spell the bound declaration name as an identifier; equal text alone cannot construct this class |
| Closed syntax | Enum, Boolean, or other bounded choice owned by product code | Map to a fixed keyword, modifier, punctuation, or empty choice; artifact text cannot enter |
| Rendered fragment | Producer-issued C# expression or attribute fragment whose internal provenance is no longer available | Preserve its syntax and contain it under its fragment contract; never search it for declaration identifiers |
| Raw literal value | Artifact value that CSharp itself places in a C# literal | Escape according to the selected literal form before composition |
| Composite subplan | Ordered child slots for a parameter, accessor, constraint, or other repeated declaration structure | Validate and prepare every child before joining them with fixed syntax |
| Opaque compatibility | Historical declaration text with no complete slot map | Keep on the compatibility path; bounded model-free repair is allowed, but the result cannot be labeled `Structured` |

A bare `string` does not establish any of these classes. The declaration plan
records the class at construction so later code cannot silently promote an
opaque value by passing it to a different helper. Missing evidence is not a
value class: it selects a fixed omitted/default syntax, `Compatibility`, or
`Unavailable` according to the declaration form.

## Normative slot inventory

The implementation owns one data-driven catalog for this inventory. A slot may
be absent for declaration forms where it is inapplicable, but a new slot cannot
enter composition without a catalog entry and handler.

### Type slots

| Slot | Value class | Current source |
| --- | --- | --- |
| Type declaration name | Type declaration name | `ApiType.DefinitionName`, `IntroducedTypeParameterCounts`, and `TypeParameters` |
| Type declaration placement | Closed syntax | caller-issued root or exact parent identity; never inferred by comparing `Name`, `MetadataName`, or rendered text |
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
| Type-name qualification context | Composite subplan | caller-supplied namespace/import context and CSharp-owned shadowing sets |
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
| Member accessibility | Closed syntax | `ApiMember.Accessibility` |
| Constant modifier | Closed syntax | `ApiMember.IsConst` |
| Static member modifier | Closed syntax | `ApiMember.IsStatic` |
| Read-only member modifier | Closed syntax | `ApiMember.IsReadOnly` |
| Sealed member modifier | Closed syntax | `ApiMember.IsSealed` |
| Abstract member modifier | Closed syntax | `ApiMember.IsAbstract` |
| Override member modifier | Closed syntax | `ApiMember.IsOverride` |
| Virtual member modifier | Closed syntax | `ApiMember.IsVirtual` |
| Unsafe member modifier | Closed syntax | `ApiMember.IsUnsafe` |
| Async member modifier | Closed syntax | `ApiMember.IsAsync` |
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
| Finalizer spelling mode | Closed syntax | `SuppressFinalizerSpelling` body-fidelity choice |
| Synthesized-obsolete presence | Closed syntax | `ApiMember.IsObsolete` |
| Obsolete message | Raw literal value | `ApiMember.ObsoleteMessage` |

### Compatibility slots

Every compatibility path has an explicit opaque slot. A declaration cannot
become `Compatibility` merely because a structured handler declined it.

| Slot | Value class | Current source |
| --- | --- | --- |
| Legacy flattened type declaration name | Opaque compatibility | `ApiType.Name` when exact definition-name segments or arity ownership are unavailable |
| Opaque member declaration | Opaque compatibility | `ApiMember.Signature` when no complete structured signature can be formed |
| Combined explicit-interface name | Opaque compatibility | combined `ApiMember.Name` or `ApiSignature.MemberName` pending #5114 |
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
| Property | Property type, simple name, and accessor list |
| Property or indexer head with accessors omitted | The corresponding property/indexer head slots plus a closed omission choice; accessor child slots are deliberately absent |
| Indexer | Property type, closed `this` token, index parameters, and accessor list |
| Explicit-interface property or indexer | Separate qualifier, simple name or closed `this` token, parameters, and accessors; structured mode is pending #5114 |
| Event | Event type, simple name, and optional explicit-interface qualifier; structured explicit forms are pending #5114 |
| Field or constant | Field type and simple name |
| Unary, binary, conversion, or checked operator | Metadata operator kind mapped through a closed catalog, typed return/conversion target, and parameters |
| Delegate | Return type, exact type declaration name, typed root or parent/child placement, generic parameters, parameters, and constraints |
| Standalone accessor head | Accessor return attributes, accessor-specific accessibility, and closed accessor kind |
| Abbreviated member declaration | The selected member head plus a closed abbreviation choice; omitted parameter-name, default, and accessor child slots are not treated as consumed |
| Terminated member declaration | The selected member form plus a closed terminator choice |

This table covers the target per-declaration planner, including public
standalone accessor heads and deliberate declaration abbreviations. It does not
claim every legacy convenience method currently located on `CSharpFormatter`.
Compilation units, rendered imports, global attributes, enum-member and
fixed-buffer special cases, initializers, bodies, and member grouping remain
with #5142's complete-source composer.

## Composition rules

1. Build the declaration plan from known fields and explicit typed handoffs.
   The plan records each slot's semantic position and value class.
2. Validate the form before rendering. Missing structure selects
   `Compatibility` or `Unavailable`; it never manufactures a boundary.
3. Prepare every slot independently. Raw identities remain untouched in the
   source model.
4. Compose prepared values only with CSharp-owned fixed syntax and layout.
5. Issue `CSharpDeclarationText` only after every emitted artifact-derived value
   has crossed either a structured slot handler or the contained compatibility
   boundary. Label the result `Structured` only when the complete form plan used
   structured slots.
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
`Compatibility`. Metadata issue
[#5114](https://github.com/richlander/dotnet-inspect/issues/5114)
owns that prerequisite. This design does not specify its extraction or
persistence mechanics.

## Compatibility and migration

`ApiMember.Signature` remains useful for older serialized surfaces and
declaration forms whose structured facts are incomplete. Compatibility output
may retain the current bounded lexical repairs, but it has four restrictions:

- it is explicitly labeled `Compatibility`;
- its complete output and diagnostics satisfy the same display-containment
  invariant as structured output;
- it cannot be converted to `Structured` after rescanning; and
- consumers requiring compile-back or a structured containment claim reject it
  or surface the degradation.

Migration proceeds by declaration form, not by adding another whole-string
pass:

1. introduce the result, text currency, slot catalog, and compatibility mode;
2. route currently structured type, constructor, ordinary method, property,
   event, field, parameter, constraint, and accessor paths through plans;
3. consume #5114's owner-issued explicit-interface handoff before promoting
   those forms;
4. remove a compatibility repair only after its final form has structured
   coverage and close negative cases; and
5. address #5076 separately before claiming primitive/generic alias fidelity.

Existing caller-visible text remains unchanged for ordinary metadata unless a
change is explicitly demonstrated and approved. Mode and diagnostics add
evidence; they do not silently replace output with an empty string.

## Failure and degradation

Stable reasons include at least:

- missing structured signature;
- degraded metadata signature;
- missing explicit-interface boundary;
- unavailable generic-reference provenance;
- unsupported declaration kind;
- invalid closed syntax value; and
- unsupported rendered fragment.

An `Unavailable` result is not an exception-shaped success and not an empty
declaration. Programmer errors such as an undefined enum remain argument
errors. Artifact-caused incompleteness is a typed result or diagnostic.

## Verification

The implementation must add these named gates; until then the corresponding
properties remain unverified:

- `CSharpDeclarationSlotCatalogTests.DeclaredSlotsAndHandlersAgree` derives the
  complete slot-to-value-class map and handler set from the normative code
  catalog and fails for undefined classes, entries that mix value classes, and
  missing or stale slots and handlers.
- `CSharpDeclarationSlotCatalogTests.RenderedModeRequiresExactConsumedSlotSet`
  derives the expected slots for every form from that catalog, compares them
  with the slots actually consumed by the public composition path, and fails
  for missing, duplicate, stale, or bypassed handlers. `Structured` requires no
  opaque slot; `Compatibility` requires every used opaque boundary to appear in
  the receipt and at least one such boundary. Catalog/handler self-consistency
  alone cannot issue either rendered mode.
- `CSharpDeclarationProvenanceTests.StructuredCompositionDoesNotRescanFinalText`
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
- `CSharpDeclarationProvenanceTests.EveryDeclarationFormHasExpectedModeAndNeighborCases`
  derives every row of the form inventory and verifies its expected
  `Structured`, `Compatibility`, or `Unavailable` outcome, including
  every type kind and root/nested placement, constructors, properties, indexers,
  events, operators, fields, both finalizer spellings, bases, interfaces,
  constraints, standalone accessor heads, abbreviated/head-only declarations,
  and terminated declarations.
- `CSharpDeclarationProvenanceTests.ClosedSyntaxSelectorsHaveNeighborCoverage`
  varies each formatter option and metadata discriminator independently,
  including required-member presence, inherited `TypeParameter.TypeKind`,
  parameter-default presence, extension-receiver presence,
  synthesized-obsolete presence and inclusion, abbreviation, accessor
  omission, attribute inclusion, forced modifiers, and termination;
  each pair must change only its declared slot set and output syntax.
- `CSharpDeclarationProvenanceTests.NestedTypePlacementDoesNotComeFromDisplayText`
  varies only legacy `Name`/`MetadataName` spellings and proves that the typed
  declaration context selects an exact child leaf while mismatched parent
  identity, namespace, or segment depth and an unsupported standalone nested
  request return `Unavailable`.
- `CSharpDeclarationProvenanceTests.LegacyFlattenedTypeNameWithLiteralPunctuationIsNotStructured`
  proves that `ApiType.Name` cannot stand in for exact definition-name segments.
- `CSharpDeclarationProvenanceTests.TypeNameArityOwnershipIsRequiredForStructuredNestedGenerics`
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
- `CSharpDeclarationProvenanceTests.MissingStructureIsVisible`
  proves that each target failure selects `Compatibility` or `Unavailable`,
  never `Structured` or success-shaped empty output.
- `CSharpDeclarationProvenanceTests.StructuredOutputContainsTheMetadataConfusionFixture`
  remains pending on #5134's scalar policy, then
  resolves the immutable version named by
  `PackageFixtures.proj`'s `MetadataConfusionFixtureVersion` property and runs
  it through the public CSharp seam. The implementation must advance that
  property to a new fixture version whose manifest names keyword-generic and
  cross-slot literal-collision specimen IDs, and the gate asserts those IDs
  before checking inert, single-declaration output. #5114 adds
  explicit-interface specimens in its owner effort.
- `CSharpDeclarationProvenanceTests.CompatibilityOutputAndDiagnosticsContainTheMetadataConfusionFixture`
  remains pending on #5134, then forces opaque compatibility declarations and
  artifact-derived diagnostics and verifies that both are inert and line-safe.
- `CSharpDeclarationProvenanceTests.DiagnosticsUseClosedReasonsAndInertDetails`
  proves independently of #5134 that every diagnostic reason is a closed enum,
  every optional detail is an `InertString` constructed under
  `TextPolicy.Field`, and no raw artifact name enters an ordinary message
  string.
- `CSharpDeclarationProvenanceTests.FinalizerSpellingModePreservesBodyFidelity`
  covers destructor syntax and the structured literal-`Finalize` alternative
  selected when body fidelity suppresses destructor reconstruction.

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
- `CSharpTypePrinter` request-tree, compilation-unit, namespace/import, global
  attribute, initializer, body-fragment, or aggregate outcome mechanics tracked
  by #5142;
- CSharpText's lexical grammar; or
- the missing primitive-versus-generic provenance tracked by #5076.

The full scalar-containment policy is likewise not defined here; #5134 owns it.

It also does not promise that every ECMA-335 declaration is representable in
C#. `Compatibility` and `Unavailable` are intentional outcomes where the input
does not support the structured claim.
