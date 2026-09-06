# Member signature shape and transport

This is the focused owner of the existing `MemberSignatureShape` projection,
correspondence, and `mss1` transport contract. It takes that one responsibility
from [Type, member, and API representation](type-member-api-representation.md),
which remains the currency map and owner of its other contracts. CSharpText
owns the neutral model and textual operations; Metadata supplies the SRM
projection. This document does not change either implementation.

## Purpose and limits

The question is: **within the caller's relevant same-name candidate group,
does the available source/metadata shape distinguish one candidate?**
It is not: "are these complete signatures, definitions, or bodies identical?"

The concrete consumer is DecompilerHarness ReturnToSender's non-authoritative
source-corpus lookup, including cases where declaration order differs from
metadata order. Its [lookup and attribution contract](../decompiler-correctness-pipeline.md)
owns fallback and fault attribution. A shape result cannot supply the missing
physical identity or source provenance.

The historical motivation is [#3843](https://github.com/richlander/dotnet-inspect/issues/3843):
the harness's lossy `SignatureIdentity` string overstated its authority.
[#4190](https://github.com/richlander/dotnet-inspect/pull/4190) replaced it with
shared typed evidence and explicit refusal. This is a repository-designed
projection, not a standardized signature format. Analogies are not claims of
historical derivation.

The benefit is one comparison model for two producers without requiring a
bound source compilation. The cost is deliberately incomplete discrimination:
some different declarations have equal shapes, and some useful matches cannot
be established. The supported consumer accepts those limits because this is
lookup evidence, not the authority for an identity-sensitive decision.

[Issue #6072](https://github.com/richlander/dotnet-inspect/issues/6072) is the
one-step documentation adoption tracker: establish this focused contract and
update its references. Existing harness adoption is unchanged. This slice adds
no CLI/browser path, renderer, dependency, or architecture migration.

## Alternatives and decision

The baseline is a typed comparison model with an explicit projection policy,
separate from serialization and identity. Established representations help
justify the policy but answer different questions:

| Alternative or analogue | Relevant evidence | Decision for this consumer |
| --- | --- | --- |
| [C# signatures and overloading, section 7.6][csharp-signatures] | Ordinary return types and parameter/generic names are excluded; generic positions and parameter modes matter; conversions use source and target types. | Supports several erasures, not the entire scheme. The language's signature includes the member name and bound type relationships that this caller-scoped lexical shape does not supply. |
| [C# reference-parameter rules][reference-parameters] | `ref`, `out`, `in`, and `ref readonly` cannot be the sole overload distinction in one type; value versus reference passing can distinguish overloads. | Justifies common by-reference evidence for this lookup. These modes still differ for other purposes, including overriding; collapsing them is not general signature equality. |
| [ECMA-335, sixth edition, II.23.2][ecma-signatures] | Binary signatures encode return types, generic positions, `BYREF`, and custom modifiers. Type references can depend on the metadata context. | Correct input evidence for the Metadata adapter, not a model-free source comparison format or standalone member identity. Raw blob equality cannot replace this projection. |
| [C# XML documentation IDs, informative Annex D][documentation-ids] | Uses positional generics, `@` for `in`/`out`/`ref`, and `~ReturnType` for conversions; also carries member/containing-type names. | Closest spelling analogy. Reusing the ID as the comparison currency would still need lexical admission, caller scope, explicit ambiguity/refusal, and the chosen type distinctions. We keep the typed model rather than make a documentation-link string its authority. |
| [Roslyn `SymbolKey` source][symbol-key] | Creates keys from symbols and resolves against a `Compilation`; warns about cross-version persistence and direct string comparison. It can resolve to multiple symbols. | Appropriate comparison for compiler-backed work, not the existing model-free production layer. Adopting it would require compiler infrastructure and a different consumer contract. No claim about its general AOT feasibility is needed. |
| [Typed-model serialization using System.Text.Json][json-source-generation] | Source-generated serialization supports avoiding reflection-based serialization in NativeAOT applications. | A credible alternative transport for this same already-projected model; not an alternative to defining the model or its loss policy. It must have an explicit schema, resource limits, and migration, with a canonical profile only if canonical text equality is required. |

The strongest justification is for the **typed shape and explicit outcomes**.
It exposes what can be compared, keeps source and Metadata producers aligned,
and makes ambiguity and refusal observable without claiming identity.

The justification for **this exact custom codec** is narrower: it is the
existing deterministic, version-marked, bounded, self-delimiting text carrier
for those nodes. Its explicit counts and tags preserve their structural
distinctions. Retaining it avoids an otherwise unnecessary transport migration
while documenting its existing behavior. That is a maintenance tradeoff, not
proof that it is the best possible encoding.

JSON could faithfully serialize the same lossy shape; neither JSON nor `mss1`
needs to erase anything further. Matching can use the deserialized typed model
rather than raw JSON string equality. A replacement would need deliberate
polymorphic-node and collection mapping, but this document does not claim
ordinary serialization is incompatible or that the current types are already
a tested drop-in JSON schema. No measured size, speed, allocation, or
maintenance-cost superiority over ordinary serialization is established.
The custom reader/writer and version policy remain carrying costs.

Thus the scheme is defensible for its named, non-authoritative consumer.
Reuse for a new purpose must justify that purpose's projection and ambiguity
policy; a convenient existing string is not sufficient justification.

[csharp-signatures]: https://github.com/dotnet/csharpstandard/blob/1397ed398812d5bbc11018ff7af613f9d73af2d0/standard/basic-concepts.md#76-signatures-and-overloading
[reference-parameters]: https://github.com/dotnet/docs/blob/156931bb4ec1e81b028c76ea983553f2e9778bdd/docs/csharp/language-reference/keywords/method-parameters.md
[ecma-signatures]: https://www.ecma-international.org/wp-content/uploads/ECMA-335_6th_edition_june_2012.pdf
[documentation-ids]: https://github.com/dotnet/csharpstandard/blob/1397ed398812d5bbc11018ff7af613f9d73af2d0/standard/documentation-comments.md#d4-processing-the-documentation-file
[symbol-key]: https://github.com/dotnet/roslyn/blob/469f6d9cf08b3209f11459119a9fd3afafc65494/src/Workspaces/SharedUtilitiesAndExtensions/Compiler/Core/SymbolKey/SymbolKey.cs
[json-source-generation]: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation

## Correspondence contract

The caller supplies the declaration kind and generic-parameter context needed
by the source adapter, and scopes comparison to the relevant declaring-type
context and member-name group. Names, containing-member identity, assembly
binding, accessibility, and static/instance status are not keys in this shape.
It is not a general CLR overload resolver.

Supply the **complete relevant candidate set**, including candidates whose
shapes are unavailable. Do not drop an uncertain sibling, deduplicate equal
shapes, or search globally by transport string. Changing the candidate set can
change the result without changing any signature.

| Available evidence | Result |
| --- | --- |
| Target or any candidate unavailable, including a recursively unresolved named-type shape | `Unavailable`, with a reason |
| All available; exactly one structurally equal candidate | `Unique`, identifying that candidate under this projection only |
| All available; multiple structurally equal candidates | `Ambiguous`, retaining every matching candidate |
| All available; no structurally equal candidate, including an empty group | `Unavailable`, with a reason |

Equality is structural and ordered; string components are case-sensitive.
`Unique` does not prove complete signature equality, semantic compatibility,
named-type binding, or that source belongs to a MethodDef. `Unavailable` is not
proof that no corresponding declaration exists. A caller requiring stronger
authority must use the appropriate identity/provenance owner, not strengthen
the meaning of this result. Any ordinal fallback is the consumer's separate
policy, not a success returned by the shape matcher.

The matcher contract is exercised by
`Matcher_PreservesUniqueAndAmbiguousOutcomes`,
`Matcher_DoesNotReturnUniqueWhenAnotherCandidateIsUnavailable`, and
`Matcher_DottedGlobalTypeBoundaryCannotProduceFalseUnique`.

## Projection policy and rationale

Projection loses information; encoding the resulting shape does not perform
another semantic normalization. The policy is purpose-specific, not a claim
that the erased facts are unimportant elsewhere.

| Fact | Policy | Rationale and limit |
| --- | --- | --- |
| Method generic arity; containing-type versus method generic positions | Preserve arity, owner kind, and position; erase parameter names | Renaming a generic parameter need not prevent correspondence. Constraints are not an identity component here; known value-type constraints can inform nullable projection. |
| Parameter order, type structure, and value versus managed-reference passing | Preserve | These distinguish the supported overload alternatives. A reference-type value such as `int[]` is not a by-reference parameter. |
| Parameter names, defaults, attributes, and `this`/`params`/`scoped` spelling | Not represented | This is not named-argument binding, optional-argument applicability, extension lookup, or a lifetime contract. |
| `ref`, `out`, `in`, and parameter `ref readonly` | Collapse to `ByReference` | Common managed-reference evidence is sufficient for this limited comparison. C# disallows these as the sole overload distinction in one type; direction and readonly semantics are not established. |
| Primitive aliases and `dynamic` | Normalize aliases to CLR names; `dynamic` to `System.Object` | Align source spelling with metadata representation, not dynamic-binding behavior. |
| Ordinary method/property returns, including ordinary ref returns | Not represented | Not used to discriminate the supported C# same-name parameter alternatives; return compatibility is not established. |
| Conversion return type | Preserve | The target type distinguishes conversion candidates. The caller still separates operator names; implicit/explicit/checked identity is not in the shape. |
| Array kind, rank, nesting, and element shape | Preserve | A vector, rank-one non-SZ array, and differing nested ranks are different shapes. Ordinary C# array syntax cannot express the rank-one non-SZ case. |
| Specified metadata array sizes or nonzero lower bounds | Refuse | There is no matching C# rank-only spelling. Explicit zero lower bounds are accepted but not retained. |
| Tuple element types and order | Preserve; normalize supported `System.ValueTuple` rest chains | Align tuple syntax with its metadata structure. Element names are erased; names are not positional type identity. |
| Nullable value types | Preserve a nullable wrapper | `T` and `Nullable<T>` are different parameter types. `T?` needs known value-type evidence for generic parameters. |
| Known reference nullability (`object?`, `string?`, arrays) | Erase annotation | Not a distinct runtime parameter type. Nullable syntax on an unresolved or unconstrained type is unavailable, not guessed. |
| Named types | Preserve the projected namespace, nested segments, arities, and arguments, but not assembly identity | The lexical adapter accepts primitive/generic forms and supported `global::` names, not using/alias resolution. Syntax can still lack namespace-versus-nesting information; complete candidate scope does not turn lexical spelling into bound identity. |
| Pointers and supported function pointers | Preserve recursive types and represented calling convention | Supported structural differences matter. Function-pointer return types remain part of the pointer type even in ordinary method parameters. Unsupported header/convention combinations are unavailable. |
| Decodable custom modifiers | Erase modifier identity and required/optional distinction | The shape is not CLR signature equivalence. A modifier must decode successfully before erasure; an unavailable modifier cannot justify a match. |

For the non-virtual static parameter fixtures, the underlying by-reference
MethodDef signature blobs are identical across the direction/readonly forms.
Those examples do not demonstrate distinct metadata custom-modifier signatures.
Modifier handling has its own evidence.

### Demo: distinguish overloads, retain ambiguity

Within the compiled `Ref` group:

| Declaration | Canonical shape transport |
| --- | --- |
| `Ref(int value)` | `mss1:0(1:vp12:System.Int32)n` |
| `Ref(ref int value)` | `mss1:0(1:rp12:System.Int32)n` |

The `v`/`r` distinction survives both producers and transport. In a different
scenario, alternative source declarations `Ref(ref int)` and `Ref(out int)`
both project to the second string. These are not one legal C# overload set.
Given both as candidates, the result is `Ambiguous`, not a merged candidate.
Given only the opposite-passing candidate, it is `Unavailable`. A single equal
candidate is only unique under this projection.

`ParameterPassingSignatureShapeFlowTests` supplies compiler-produced methods,
independent expected MethodDef handles, literal shapes/transports, and those
outcomes. `MemberSignatureShapeFlowTests` covers array/generic/tuple distinctions
and erasures. `ConversionSignatureShapeFlowTests` covers conversion returns,
ordinary-return erasure, operator-name scoping, and the current source
adapter's visible refusal of checked-conversion headers.

### Producer admission boundaries

These are the existing projection boundaries, not a replacement C# compiler or
metadata validator. Metadata decoding and type-name rules remain owned by
their respective Metadata services.

Metadata projection refuses noncanonical generic headers, disagreement with
the MethodDef's owned contiguous GenericParam rows, and disagreement between
declaring-type name arities and cumulative owned rows. Metadata arity suffixes
must be nonzero canonical ASCII decimals; positional generic references must
fit their validated context. Function-pointer instance, explicit-this, generic,
or vararg headers are unavailable. Source headers containing directives are
unavailable; a directive only in the body does not hide the header. Projection
failure returns `Unavailable` with a reason, not an empty available shape.
The source recognizer does not establish that an accepted declaration is a
legal compilable C# program.

Relevant gates in `MetadataMemberSignatureShapeTests` include
`MetadataAdapter_RefusesGenericHeaderWithoutOwnedRows`,
`MetadataAdapter_RefusesNonContiguousGenericParameterRows`,
`MetadataAdapter_RefusesZeroArityGenericHeader`,
`MetadataAdapter_RefusesMethodGenericPositionOutsideHeaderArity`,
`MetadataAdapter_RefusesMissingDeclaringTypeGenericRows`,
`MetadataAdapter_AllowsCumulativeNestedTypeGenericRows`,
`MetadataAdapter_RefusesNoncanonicalTypeReferenceArity`,
`MetadataAdapter_RefusesUnrepresentableFunctionPointerHeaders`,
`MetadataAdapter_RefusesMultidimensionalArrayBounds`,
`MetadataAdapter_AllowsExplicitZeroArrayLowerBound`, and
`MetadataAdapter_RefusesUnavailableErasedModifier`.

Source gates in `MemberSignatureShapeTests` include
`SourceShape_RefusesNonGlobalNamedTypes`,
`SourceShape_RefusesSemanticallyUnresolvedNullableTypes`,
`SourceShape_RefusesSupplementalFunctionPointerConvention`, and
`SourceShape_DirectiveInHeaderIsUnavailable`.

## Canonical `mss1` transport

The encoding belongs to this repository. It is not an ECMA-335 signature blob,
XML documentation ID, member anchor, or source declaration. It carries a
shape, not its origin, candidate scope, availability reason, or authority.

### Grammar

Quoted terminals are literal and case-sensitive. `N` is an ASCII decimal
integer in `0..2147483647`, written without a sign or leading zeroes except
`0`. `text` is `N ":"` followed by exactly `N` UTF-16 code units. A text
payload is raw .NET string content: it has no escaping or Unicode
normalization, and may contain punctuation that is structural elsewhere.
The grammar does not choose a file/JSON/byte-stream encoding for an outer host.

In the following notation, `items(k, X)` means exactly `k` consecutive `X`
values, with no separators beyond those in `X`. Named counts are occurrences
of `N`; their names are explanatory, not encoded.

```text
shape     = "mss1:" generic-arity "(" count ":" items(count, parameter) ")" result
parameter = ("v" | "r") type
result    = "n" | "y" type
text      = length ":" [exactly length UTF-16 code units]

type      = "p" text
          | ("t" | "m") position ";"
          | "n" text segment-count ":" items(segment-count, segment)
          | "x" text argument-count ":" items(argument-count, type)
          | ("z" | "a") rank ":" type
          | ("*" | "&" | "?") type
          | "u" element-count ":" items(element-count, type)
          | "f" text type parameter-count ":" items(parameter-count, type)
segment   = text arity ":" argument-count ":" items(argument-count, type)
```

| Tag | Meaning |
| --- | --- |
| `v`, `r` | Top-level parameter value or by-reference passing |
| Result `n`, `y` | No conversion-return shape, or one following type |
| `p` | Primitive-name payload |
| `t`, `m` | Containing-type or method generic-parameter position, zero-based |
| Type `n` | Namespace payload followed by ordered named-type segments |
| `x` | Unresolved legacy-name shape and arguments; never eligible for correspondence, even inside canonical transport |
| `z`, `a` | SZ array or non-SZ array, with rank and element type |
| `*`, `&`, `?` | Pointer, by-reference type, or nullable-value wrapper |
| `u` | Ordered tuple element types |
| `f` | Calling-convention payload, return type, then ordered parameter types |

`r` and `&` are not interchangeable. A method's by-reference parameter is
`r` plus its element type; `&` is a recursive type node, for example inside a
function-pointer type.

No whitespace or trailing content is permitted outside counted text payloads.
The outer prefix, punctuation, integer spelling, counts, type ordering, and
payloads all participate in canonical equality. Decoding canonical input must
consume the entire string and re-encoding must reproduce it exactly.

The codec requires nonempty primitive, segment, unresolved-name, and
calling-convention payloads; the namespace may be empty. Named types need at
least one segment, tuples at least two elements, and array ranks must be
positive (`z` requires rank one). These are structural transport conditions:
the codec does not bind names, validate a primitive-name vocabulary, enforce
generic positions against a declaration, or require segment arity to equal
the number of supplied arguments. Producer admission is a separate boundary.

### Limits and failure

The current codec admits at most 65,536 UTF-16 code units including `mss1:`,
4,096 recursive type nodes, and type depth 128 (each root parameter or
conversion-return type starts at one). It also admits at most 4,096 aggregate
collection slots: member parameters, named segments, named/unresolved type
arguments, tuple elements, and function-pointer parameters all contribute.
These are cumulative limits, not independent allowances for each collection.

Encode rejects invalid or over-budget supplied shapes with an argument
exception; decode reports malformed, noncanonical, or over-budget text as
`Unavailable`. Successful decoding alone does not establish correspondence
eligibility: an `x` node remains unresolved.

`Codec_RoundTripsCanonicalTextAndNormalizesLegacyInput`,
`Codec_MalformedCanonicalTextIsUnavailable`, and
`Codec_IsInjectiveForMixedArrayRanks` exercise transport fidelity and refusal.
`Codec_RejectsCollectionAmplificationBeforeAllocatingIt`,
`Codec_RejectsOversizedOutputBeforeGrowingTheBuilder`, and
`Codec_RejectsUndefinedEnumValues` cover the named resource/shape boundaries.
These are existing Release gates in
[`MemberSignatureShapeTests`](../../tests/CSharpText.Tests/MemberSignatureShapeTests.cs),
not a claim of exhaustive enumeration of the grammar.

Source admission additionally limits declaration text to 256 Ki UTF-16 code
units and parameter lists to 1,024 entries, and applies the transport depth
and final encoding limits. Parenthesis correspondence takes a bounded linear
pass, gated by
`SourceShape_NestedParameterListCandidatesStayWithinLinearTime`.
Metadata projection charges one cumulative work budget, including modifier
subtrees later erased and legacy generic names:
`MetadataAdapter_RefusesErasedModifierAmplificationBeforeLargeAllocation` and
`LegacyCompatibility_RefusesGenericNameAmplificationBeforeLargeAllocation`
are the gates. The numeric Metadata acquisition/decoding budgets are not
redefined by the wire grammar.

### Legacy and evolution

`Decode` also recognizes the existing unprefixed legacy grammar. `Normalize`
can encode its resulting shape, but normalization does not authorize an
upgrade from legacy evidence to candidate-selection evidence. Consumers must
preserve that origin distinction. Legacy text may validate an independently
selected exact-token record; it must not select candidates. The current
consumer enforces the `mss1:` prefix before correspondence.

`Codec_NormalizesLegacyNamedTypesWithoutTreatingThemAsExact`,
`CompileBackTargets_LegacySignatureCannotOverrideOrdinal`, and
`SourceSignatureCorrespondence_RejectsLegacyCandidateSelection` cover that
boundary. Unknown prefixes have no defined future-version dispatch contract;
they must not be treated as a supported newer schema.

For maintenance, changing the interpretation or canonical encoding of an
existing `mss1` string requires a distinct version rather than silently
reusing this prefix. Adding support for another source/metadata input need
not change the wire version if existing node meanings and encodings remain
unchanged. A projection correction can nevertheless change a persisted
lookup's emitted shape: its change must state whether records are regenerated,
revalidated, or deliberately no longer accepted. Wire-version equality is
not evidence that producer behavior or provenance stayed the same.

This policy does not introduce a newer version, an automatic migration,
indefinite legacy compatibility, or a cross-version compatibility promise.
