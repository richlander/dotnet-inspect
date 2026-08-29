# C# structured declaration provenance

## Status and owner

This document defines the target declaration-slot contract for
`ILInspector.CSharp`. `CSharpFormatter` owns model-bound C# declaration
spelling; `CSharpDeclarationWriter` is its current implementation seam.

The target is **unverified** until the gates in
[Verification](#verification) exist. Current formatters mix structured
composition with compatibility text and do not yet return the result shape
defined here.

## Claim

A structured C# declaration is composed only from explicitly classified input
slots. Every slot preserves whether its value is:

- a raw metadata identifier;
- a model-bound type expression;
- a closed C# syntax choice;
- an already-rendered C# fragment; or
- opaque compatibility text.

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
CSharpText; it does not create a competing scalar policy. Structured punctuation
comes only from fixed product syntax. Rendered fragments must arrive under a
typed fragment contract or remain compatibility input.

`CSharpDeclarationProvenanceTests.StructuredOutputContainsTheAdversarialMetadataCorpus`
and the slot-collision gates in [Verification](#verification) are the planned
enforcement. The invariant is unverified until those gates exist.

Local repository actors and callers that deliberately bypass the public
formatter contract are outside this product threat model. Ordinary code review,
not stronger declaration types, governs those cases.

## Demo mockup

The implementation PR must turn this mockup into a real compiled or persisted
metadata demonstration. The examples below describe the target; they are not
current validation.

Given a generic owner whose raw type-parameter name is `class` and whose
explicit interface is `I<class>`, the structured declaration is:

```csharp
class C<@class> : I<@class>
{
    void I<@class>.Map();
}
```

The failed structured composition from #4656 was:

```csharp
void I<class>.Map();
```

A neighboring provenance case must keep a primitive and a same-spelled generic
parameter distinct:

```csharp
void Pair<@int>(int primitive, @int parameter);
```

What to notice:

- the explicit-interface qualifier is a type-expression slot, separate from
  the simple member-name slot;
- the primitive keyword remains syntax while the generic parameter is escaped
  as an identifier;
- default expressions and attribute arguments are not searched for either
  name; and
- the raw metadata names remain `class` and `int`.

The final neighboring example depends on the additional type provenance tracked
by #5076. Until that currency exists, CSharp reports the affected declaration as
compatibility or unavailable rather than claiming structured fidelity.

## Immediate boundaries

### Inputs

CSharp consumes Metadata-issued declaration facts:

- `ApiType`, including exact definition-name segments, generic parameters,
  bases, interfaces, attributes, and modifiers;
- `ApiMember`, including exact metadata name, kind, attributes, modifiers, and
  compatibility signature;
- `ApiSignature`, `ApiParameter`, `ApiAccessor`, `TypeParameter`, and typed
  type-reference or type-shape evidence; and
- caller options that select qualification, imports, namespace form,
  declaration abbreviation, and body-related modifiers.

These inputs remain owned by Metadata or the caller. This document states only
the preconditions CSharp requires. It does not define how Metadata decodes,
validates, persists, or associates them.

All target contracts remain SRM-only, Roslyn-free, NativeAOT-friendly, and
usable from browser/Wasm consumers. Introducing a platform-specific dependency
would require a separately approved exception.

### Outputs

The target public result is a closed `CSharpDeclarationResult` with two arms:

- **Rendered** carries `CSharpDeclarationText`, the required raw namespace
  identities, a `Structured` or `Compatibility` mode, and diagnostics.
- **Unavailable** carries a stable reason and diagnostics, but no
  success-shaped empty declaration.

`CSharpDeclarationText` is presentation currency, not a string identity. Its
construction is restricted to the CSharp composer and means that every
artifact-derived value reached the output through a declared slot handler.
It does not mean the declaration compiles, corresponds to a body, or identifies
a metadata row.

Existing string-returning formatter methods may remain compatibility adapters
during migration. New consumers that require the structured guarantee consume
`CSharpDeclarationResult`; they do not infer success or mode from text.

### Adjacent owners

- **Metadata** supplies metadata facts and typed identities. CSharp does not
  mutate them or derive identity from their display text.
- **CSharpText** supplies model-free lexical operations. It may tokenize one
  declared type-expression or compatibility slot, but it does not decide which
  semantic slot a substring occupies.
- **CLI and other hosts** choose where to display or serialize the result. They
  do not repeat CSharp preparation.
- **Decompiler and Research** may request or compose declarations but do not
  own declaration spelling.

## Slot value classes

Every catalog entry selects exactly one value class.

| Value class | Input meaning | CSharp treatment |
| --- | --- | --- |
| Raw identifier | Exact metadata name for one identifier position | Apply visual containment and C# identifier escaping once, at that position |
| Type expression | Model-bound type spelling plus all available type/generic-reference evidence | Render or lex within this slot; apply qualification, alias, and identifier policy without inspecting neighboring slots |
| Closed syntax | Enum, Boolean, or other bounded choice owned by product code | Map to a fixed keyword, modifier, punctuation, or empty choice; artifact text cannot enter |
| Rendered fragment | Producer-issued C# expression or attribute fragment whose internal provenance is no longer available | Preserve its syntax and contain it under its fragment contract; never search it for declaration identifiers |
| Raw literal value | Artifact value that CSharp itself places in a C# literal | Escape according to the selected literal form before composition |
| Opaque compatibility | Historical declaration text with no complete slot map | Keep on the compatibility path; bounded model-free repair is allowed, but the result cannot be labeled `Structured` |

A bare `string` does not establish any of these classes. The declaration plan
records the class at construction so later code cannot silently promote an
opaque value by passing it to a different helper.

## Normative slot inventory

The implementation owns one data-driven catalog for this inventory. A slot may
be absent for declaration forms where it is inapplicable, but a new slot cannot
enter composition without a catalog entry and handler.

### Compilation-unit and type slots

| Slot | Value class | Current source |
| --- | --- | --- |
| Namespace declaration and `using` segment | Raw identifier | `ApiType.Namespace`, formatter options, derived namespace identities |
| Type declaration name segment | Raw identifier | `ApiType.DefinitionName`, with legacy `Name` fallback |
| Type kind, accessibility, and modifiers | Closed syntax | `ApiType.Kind`, accessibility, and modifier flags |
| Type generic-parameter declaration | Raw identifier plus closed variance | `ApiType.TypeParameters` |
| Primary-constructor parameter | Structured parameter subplan | caller-supplied `ApiParameter` values |
| Enum underlying type | Closed primitive syntax or unavailable | `ApiType.EnumUnderlyingType` |
| Base type | Type expression | `ApiType.BaseType` and typed reference evidence |
| Implemented interface | Type expression | `ApiType.Interfaces` and available typed reference evidence |
| Type generic constraint | Closed special constraint or type expression | `TypeParameter.StructuredConstraints`; unstructured constraints are compatibility input |
| Type attribute | Rendered attribute fragment | `ApiType.Attributes` |

### Common member slots

| Slot | Value class | Current source |
| --- | --- | --- |
| Member attribute and return attribute | Rendered attribute fragment | `ApiMember.Attributes`, `ApiSignature.ReturnAttributes` |
| Accessibility and declaration modifier | Closed syntax | `ApiMember` flags and formatter options |
| Return, field, property, or event type | Type expression | `ApiSignature.ReturnType` or `ApiMember.ReturnType`, with typed evidence when available |
| Simple member name | Raw identifier | exact `ApiMember.Name` or a separately issued simple-name slot |
| Explicit-interface qualifier | Type expression | requires an owner-issued qualifier separate from the simple name |
| Method generic-parameter declaration | Raw identifier | `ApiSignature.TypeParameters` or an explicitly paired caller override |
| Method generic-parameter reference | Bound generic-parameter identity | type-expression evidence; equal rendered text is insufficient |
| Method generic constraint | Closed special constraint or type expression | `TypeParameter.StructuredConstraints`; inherited-restatement policy remains CSharp-owned |
| Parameter attribute | Rendered attribute fragment | `ApiParameter.Attributes` |
| Parameter modifier | Closed syntax | `ApiParameter.Modifier` |
| Parameter type | Type expression | `ApiParameter.Type` and typed evidence when available |
| Parameter name | Raw identifier | `ApiParameter.Name` |
| Parameter default | Rendered expression fragment | `ApiParameter.DefaultValueText` |
| Accessor attribute, accessibility, and kind | Rendered attribute fragment plus closed syntax | `ApiAccessor` |
| Obsolete message | Raw literal value | `ApiMember.ObsoleteMessage` |

### Declaration-form requirements

| Form | Additional required structure |
| --- | --- |
| Constructor, static constructor, finalizer | Exact declaring-type leaf name; punctuation and destructor marker are closed syntax |
| Ordinary or extension method | Return type, simple name, method generic parameters, parameters, and constraints; extension `this` is closed syntax |
| Explicit-interface method | Separate qualifier type expression and simple member name; a combined dotted string is compatibility input |
| Property | Property type, simple name, and accessor list |
| Indexer | Property type, closed `this` token, index parameters, and accessor list |
| Explicit-interface property or indexer | Separate qualifier, simple name or closed `this` token, parameters, and accessors |
| Event | Event type, simple name, and optional explicit-interface qualifier |
| Field or constant | Field type, simple name, and any literal initializer |
| Unary, binary, conversion, or checked operator | Metadata operator kind mapped through a closed catalog, typed return/conversion target, and parameters |
| Delegate | Return type, type name, generic parameters, parameters, and constraints |

This table covers the declaration forms CSharp currently emits. Body text,
initializer recovery, namespace selection, and member grouping are not new
declaration slots merely because `CSharpTypePrinter` composes them nearby.

## Composition rules

1. Build the declaration plan from known fields and explicit typed handoffs.
   The plan records each slot's semantic position and value class.
2. Validate the form before rendering. Missing structure selects
   `Compatibility` or `Unavailable`; it never manufactures a boundary.
3. Prepare every slot independently. Raw identities remain untouched in the
   source model.
4. Compose prepared values only with CSharp-owned fixed syntax and layout.
5. Issue `CSharpDeclarationText` only after the plan reports that every emitted
   artifact-derived value came through its registered handler.

The structured path therefore forbids:

- applying identifier substitutions to the complete declaration;
- finding the member or parameter list by searching completed output;
- splitting a combined explicit-interface name at a guessed dot;
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
`Compatibility`. This design does not specify the producer's extraction or
persistence mechanics. The implementation issue must record that prerequisite
as an independently owned Metadata handoff rather than adding text inference to
CSharp.

## Compatibility and migration

`ApiMember.Signature` remains useful for older serialized surfaces and
declaration forms whose structured facts are incomplete. Compatibility output
may retain the current bounded lexical repairs, but it has three restrictions:

- it is explicitly labeled `Compatibility`;
- it cannot be converted to `Structured` after rescanning; and
- consumers requiring compile-back or a structured containment claim reject it
  or surface the degradation.

Migration proceeds by declaration form, not by adding another whole-string
pass:

1. introduce the result, text currency, slot catalog, and compatibility mode;
2. route currently structured type, constructor, ordinary method, property,
   event, field, parameter, constraint, and accessor paths through plans;
3. add the owner-issued explicit-interface handoff before promoting those forms;
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
  complete expected handler set from the normative code catalog and fails for
  missing and stale handlers.
- `CSharpDeclarationProvenanceTests.StructuredCompositionDoesNotRescanFinalText`
  uses colliding identifiers inside a default literal, attribute argument,
  return type, qualifier, and member name to prove substitutions stay in their
  slots.
- `CSharpDeclarationProvenanceTests.ExplicitInterfaceGenericKeyword_IsEscapedWithoutChangingRawIdentity`
  exercises a real compiled or persisted metadata artifact and verifies both
  `I<@class>.Map` and unchanged raw names.
- `CSharpDeclarationProvenanceTests.PrimitiveAndSameNamedGenericRemainDistinct`
  is the #5076 gate and remains pending until that issue supplies provenance.
- `CSharpDeclarationProvenanceTests.EverySupportedDeclarationFormHasStructuredAndNeighborCases`
  covers every row of the form inventory, including constructors, properties,
  indexers, events, operators, bases, interfaces, and constraints.
- `CSharpDeclarationProvenanceTests.MissingStructureIsVisible`
  proves that each target failure selects `Compatibility` or `Unavailable`,
  never `Structured` or success-shaped empty output.
- `CSharpDeclarationProvenanceTests.StructuredOutputContainsTheAdversarialMetadataCorpus`
  runs the repository-owned metadata-confusion fixture through the public
  CSharp seam and verifies inert, single-declaration output.

The implementation PR must run these gates in Release and show the demo's
actual output beside a neighboring clean declaration. A synthetic unit case may
isolate a slot handler, but the explicit-interface claim requires real metadata
evidence.

## Non-claims

This owner does not define:

- Metadata decoding, identity, extraction, persistence, or safety budgets;
- API artifact JSON schema or preparation;
- CLI section selection, Markdown/JSON rendering, or Markout behavior;
- API-to-body, accessor, or call-graph correspondence;
- Decompiler body fidelity or compile-back admission;
- CSharpText's lexical grammar; or
- the missing primitive-versus-generic provenance tracked by #5076.

It also does not promise that every ECMA-335 declaration is representable in
C#. `Compatibility` and `Unavailable` are intentional outcomes where the input
does not support the structured claim.
