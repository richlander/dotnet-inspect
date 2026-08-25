# tsbindgen

`tsbindgen` generates TypeScript declarations from a .NET assembly's
`[JSExport]` wasm/JS interop surface, and can diff generated output against a
hand-written `.d.ts`/`.ts` file to detect drift (for CI gating).

It is a standalone tool, packaged like `mdi`, and does not add a
`dotnet-inspect` subcommand.

## Usage

```bash
# Print generated TypeScript declarations for an assembly's [JSExport] surface.
tsbindgen <path-to-assembly.dll>

# Compare generated output against a hand-written file; exits non-zero on drift.
tsbindgen <path-to-assembly.dll> --diff-against <path-to-hand-written.d.ts>

# Publish a JavaScript wrapper only after the checked-in declarations cover generated output.
tsbindgen <path-to-assembly.dll> --diff-against <path-to-hand-written.d.ts> --emit-js <path-to-wrapper.js>
```

## Design

`tsbindgen` consumes [`ILInspector.JsExportSurface`](../ILInspector.JsExportSurface),
a C#-faithful object model of an assembly's `[JSExport]` surface built on
`ILInspector.Metadata`'s `ApiSurfaceExtractor`. That OM performs no
target-language rewriting: a `Task<T>` return type is reported as `Task<T>`,
not unwrapped to a target-language "promise" concept.

All TypeScript-specific opinion — `Task<T>`/`ValueTask<T>` unwrapping to
`Promise<T>`, property naming based on the owning `JsonSerializerContext`'s
`[JsonSourceGenerationOptions(PropertyNamingPolicy = ...)]`, array/nullable
syntax, and `.d.ts` layout — lives entirely in this tool (`TsTypeMapper`,
`CamelCase`, `DtsEmitter`). A future binding-generation target besides
TypeScript would add its own "personality" layer here without needing to touch
the OM.

System.Text.Json serializes an exact CLR `byte[]` (`System.Byte[]`) DTO member
as one Base64 JSON string, so those JSON-wire declarations map to TypeScript
`string`. Direct `[JSExport]` parameters and returns instead retain JS
interop's numeric-array mapping; other byte-like arrays continue through
ordinary array mapping.
`TsTypeMapperTests.MapJsonWireType_MapsExactByteArraysToBase64Strings`,
`MapInteropType_PreservesByteArraysAsNumericArrays`,
`DtsEmitterTests.Emit_MapsDirectByteArrayExportAsInteropArray`,
`DtsEmitterTests.Emit_MapsByteArrayPropertiesToBase64StringsInDirectAndNestedDtos`,
and `SourceGeneratedJson_UsesBase64StringsForByteArrayProperties` gate that
wire contract against both generated declarations and the real source
generator.

### Record shape discovery

A `[JSExport]` method's signature in this ABI style is always a plain
`string`/`Task<string>` — the real DTO type only appears inside the method
body, via a call such as `JsonSerializer.Serialize(dto, Context.Default.SomeDto)`.
Record shapes are therefore discovered from the assembly's authentic,
framework-signed `JsonSerializerContext`-derived type: each
`[JsonSerializable(typeof(T))]` on that type compiles to a property whose
`JsonTypeInfo<T>` definition is likewise authenticated to System.Text.Json.
The context must also carry the authentic
`[GeneratedCode("System.Text.Json.SourceGeneration", ...)]` marker emitted by
the STJ generator; a handwritten context cannot inherit generated-contract
trust from a matching attribute, getter name, and signature alone.
The property is accepted only when its metadata property identity is the
row's `TypeInfoPropertyName`, or STJ's structured default generated name when
that argument is absent, its `T` identity matches the authenticated row, and
it is an instance, parameterless, getter-only property. An indexed or
otherwise user-shaped same-name sibling is reached failure evidence, not a
generated registration.
An unrelated same-`T` handwritten `JsonTypeInfo<T>` property on the same
partial context is not a registration.
The root and property argument are compared as one structured shape: primitive
codes, array rank, named/generic identities, and every generic argument remain
distinct. This avoids special string or name fallback paths for `int`, `int[]`,
`byte[]`, and closed generic roots. A serialized primitive is intrinsic only
when its defining assembly is a platform-signed core contract assembly
(`System.Private.CoreLib`, `System.Runtime`, `mscorlib`, or `netstandard`);
another platform-signed assembly cannot alias `System.Int32` or another
primitive. `System.Decimal` remains a named core-contract value type because
ECMA-335 has no decimal primitive element code; this keeps its serialized root
shape equal to the generated `JsonTypeInfo<decimal>` signature.
`JsonWireContractResolverTests.Build_ResolvesRegisteredPrimitiveAndArrayRoots`
and `DtsEmitterTests.Emit_ParsesDecimalWireRootResults` gate scalar and array
decimal wrappers through the real source-generated context. For a default name
STJ uses the leaf
metadata segment (plus generic arguments and array suffixes), so a nested
`Outer.Leaf` root is `Leaf`, not `OuterLeaf`. A nested/top-level leaf collision
is retained as ambiguous evidence and stops generation only when its context
property reaches an export; unrelated unsupported roots do not poison another
context.
Vector roots append `Array`; multidimensional rank-*N* roots append
`Array{N}D` recursively, so `int[][,]` is `Int32Array2DArray` and `int[,][]`
is `Int32ArrayArray2D`. The root/property comparison treats only omitted
bounds and explicit default zero lower bounds as equivalent, because the
serialized attribute grammar cannot encode them; rank and recursive element
shape remain required. Multidimensional roots are still unsupported because
System.Text.Json does not serialize them at runtime:
they are retained as getter-scoped failure evidence, while an unrelated vector
root remains usable.
An authentic malformed root whose default property name cannot be recovered is
retained against otherwise-unmatched, trusted `JsonTypeInfo<T>` getters in that
same context. It therefore fails only when body evidence reaches such a getter
instead of disappearing as an absent registration or poisoning unrelated
contexts. Even a fully undecodable authentic row retains one unsupported
placeholder root, so malformed row counts cannot globally poison unrelated
contexts before getter reachability is known.
These checks use assembly-scoped, structured metadata identity rather than
matching flattened names as text; a nested type cannot alias an expected
top-level System.Text.Json definition.
Framework attribute constructor parameters must likewise resolve to their
actual core-contract or System.Text.Json defining assembly, and a generic enum
converter's target must match the enum's structured namespace and nesting.
`JsExportSurfaceBuilderTests.Build_DoesNotDiscoverHandwrittenContextProperties`
gates registration correspondence, while
`Build_DoesNotTrustNestedSerializerContextIdentity` and
`Extract_CapturesStructuredSerializerContextBaseIdentity` gate the structured
authentication and extraction path.
`JsonWireContractResolverTests.Build_AuthenticatesOnlyGeneratedCustomNamedContextProperty`
and `JsExportSurfaceBuilderTests.Build_DefersUnreachedAmbiguousAndRejectsMalformedGeneratedPropertyIdentities`
gate the custom-property-name boundary.
`JsonWireContractResolverTests.Build_ResolvesRegisteredString` and
`Build_ResolvesRegisteredStringArrayAfterAwait` gate the compiler-produced
intrinsic paths. This list is not a heuristic — System.Text.Json's fast
(non-reflection)
serialization path requires every (de)serialized type to be registered there,
so it is exactly the set of shapes that can flow across the `[JSExport]`
boundary via this pattern.
`JsExportSurfaceBuilderTests.Build_AuthenticatesNestedSerializerRootUsingLeafPropertyName`,
`Build_RejectsNestedAndTopLevelSerializerRootCollisionWhenReached`,
`Extract_PreservesClosedGenericAndPrimitiveSerializerRootShapes`, and
`JsonWireContractResolverTests.Build_ResolvesRegisteredPrimitiveAndArrayRoots`
gate the generated-name and exact-shape boundaries.
`JsExportSurfaceBuilderTests.Extract_RecordsMultidimensionalRootEvidenceAndSourceGeneratorNames`,
`Build_RejectsReachedMultidimensionalSerializerRoot`,
`Build_DoesNotNormalizeNonDefaultMultidimensionalArrayBounds`, and
`SourceGeneratedJson_MultidimensionalRootRemainsUnsupportedAtRuntime` gate the
array-name, reached-failure, and runtime-boundary contracts.
`JsonSerializableAttributeTests.ReadJsonSerializableRoots_DoesNotAliasBogusPrimitiveAssembly`
and
`ReadJsonSerializableRoots_RetainsFullyMalformedAuthenticRow` plus
`JsExportSurfaceBuilderTests.Build_BindsUnnamedMalformedRootToReachedTrustedGetter`
gate primitive provenance, malformed-row retention, and unnamed malformed-root
reachability. `Build_RejectsReachedHandwrittenSerializerContextGetter` gates
the source-generator marker boundary against a compiled handwritten context
carrying a different authentic `GeneratedCode` marker.

Serialized generic root arguments use a strict structural grammar. Leading,
doubled, and trailing delimiters are unsupported, and the sum of canonical
metadata-name arities must equal the parsed argument count. This preserves
assembly-qualified nested generic roots without treating malformed metadata as
the same `ApiTypeShape` as a valid registration.
`JsonSerializableAttributeTests.ReadJsonSerializableRoots_ParsesAssemblyQualifiedNestedGenerics` and
`ReadJsonSerializableRoots_RejectsMalformedGenericDelimitersAndArity` are the
gates.

### Source-generation direction

A generated `JsonTypeInfo<T>` property does not itself prove deserialize
support. tsbindgen retains the effective
`JsonSourceGenerationMode`: a root set to `Default` inherits its context's
mode, `Serialization` authenticates only serialization, and `Metadata`
authenticates both directions. Consequently a serialization-only property can
resolve a return envelope but never a `Deserialize` parameter. The compiled
`JsonWireContractResolverTests.Build_UsesEffectiveSourceGenerationModesForWireDirections`
and
`SourceGeneratedJson_SerializationOnlyRootRejectsDeserializeAndPreservesSerializeShape`
gates cover the override/default rule and STJ's runtime failure oracle.

Generated interfaces follow accessor participation by direction: serialization
requires an accessible getter, while deserialization requires an accessible
setter. A public setter with a private or absent getter therefore remains an
input member, while a private setter is not promised as accepted input merely
because its getter is public. System.Text.Json can also bind a getter-only
property, or a property whose setter does not participate, through a matching
constructor parameter. Constructor correspondence is not yet projected, so a
reached deserialize contract containing a getter-only candidate, or a
non-participating setter with a matching constructor parameter, fails visibly
instead of emitting an empty or partial interface. `[JsonInclude]`
properties or fields must remain
accessible to the source-generated context: private, private-protected, and
protected members are excluded, while internal, protected internal, and public
members are accessible. Indexed properties are also excluded because
System.Text.Json does not include indexers in object contracts; extracted
surfaces persist the property index-parameter count, while older or
hand-composed surfaces without equivalent signature evidence fail closed. The
same wire-member rule drives transitive DTO
discovery and declaration emission so a discovered edge cannot become an
orphaned or incomplete TypeScript shape;
`DtsEmitterTests.Emit_UsesSetterAccessibilityForDeserializeDeclarations` and
`SourceGeneratedJson_UsesSetterAccessibilityForDeserialization` gate the
directional accessor contract against the real source generator, while
`Emit_BlocksUnmodeledConstructorBoundDeserialization` and
`Emit_BlocksConstructorBindingWithPrivateSetter` gate the fail-visible
constructor-binding boundary against real generated contexts.
`DtsEmitterTests.Emit_IncludesJsonIncludedFieldsInParentInterface` and
`DtsEmitterTests.SourceGeneratedJson_OmitsInaccessibleJsonIncludedMembers`
plus `DtsEmitterTests.Emit_MatchesSourceGeneratedJsonIncludeAccessibility`
gate that shared-rule invariant against the real source generator.
`JsonWireMemberRulesTests.ExtractedCompilerIndexerIsExcludedFromJsonContract`
and
`JsExportSurfaceBuilderTests.Build_WidgetDtoSerializesFourPropertiesAndExcludesIndexer`
gate indexer extraction and projection.

### Directional `[JsonIgnore]`

`[JsonIgnore]` is not one fact. `WhenWriting` removes a member from what is
serialized while leaving it in what is deserialized, and `WhenReading` does the
reverse, so collapsing every non-`Never` condition into total exclusion drops
members that really are on the wire. Conditions are therefore preserved from
metadata as `JsonWireIgnoreCondition` and applied per direction:

| Condition | Serialize | Deserialize |
| --- | --- | --- |
| absent, `Never` | present | present |
| `Always`, `WhenWritingDefault`, `WhenWritingNull` | absent | absent |
| `WhenWriting` | absent | present |
| `WhenReading` | present | absent |

`WhenWritingDefault` and `WhenWritingNull` depend on the runtime value rather
than the declaration, so a static projection still cannot promise the member in
either direction and keeps them excluded.

A declared type's directions come from how exports use it: a resolved return
wire type marks a type serialize-only, a resolved parameter wire type marks it
deserialize-only, and both mark it bidirectional. A bidirectional type with a
direction-sensitive member has no single interface, so it is emitted as
diagnosed `unknown` rather than guessing one direction's shape. This split
includes accessor participation as well as directional `[JsonIgnore]`. Without body
evidence no direction can be attributed and every type is read as
bidirectional. Discovery walks the direction-independent member union so every
possible referenced declaration exists, while direction propagation follows
only members present in the active direction. Types discovered solely through
an inactive edge retain an explicit `None` direction and are not emitted. A
deserialize-only edge therefore cannot falsely make a type bidirectional or
publish an unreferenced failing declaration through a member absent from
serialization.

`JsonPropertyNameAttributeTests.JsonIgnoreConditionValuesMatchSystemTextJson`
pins the condition values to System.Text.Json's own enum and
`DirectionalJsonIgnoreConditionsAreDecodedFromCompiledMetadata` gates decoding
from compiled metadata. The serialized enum type must itself carry authentic
platform-signed System.Text.Json assembly provenance;
`JsonIgnoreConditionFromUntrustedEnumAssemblyIsMalformed` gates that boundary.
`JsonWireMemberRulesTests.DirectionalIgnoreConditionsSelectDirections` gates
the table above,
`JsExportSurfaceBuilderTests.Build_RecordsSerializeOnlyDirectionForReturnOnlyDto`
gates direction attribution, and
`DtsEmitterTests.Emit_PreservesWhenReadingMemberInSerializeOnlyDeclaration`,
`Emit_PreservesWhenWritingMemberInDeserializeOnlyDeclaration`,
`Emit_PropagatesOnlyMembersPresentInTheActiveDirection`,
`JsExportSurfaceBuilderTests.Build_RecordsInactiveDiscoveredTypeAsNone`,
`Emit_BlocksBidirectionalTypeWithDirectionSensitiveMember`,
`Emit_BlocksDirectionSensitiveTypeWithoutBodyEvidence`, and
`Emit_DoesNotOrphanTypesReachedOnlyThroughDirectionalMembers` gate emission
against the compiled, source-generator-backed fixtures.
`DtsEmitterTests.Emit_IncludesPropertyWithJsonIgnoreNever` continues to gate
the explicit `Never` exception.

Inherited class members and the wire semantics of `[JsonNumberHandling]`,
`[JsonPolymorphic]`, `[JsonDerivedType]`, and `[JsonExtensionData]` are not yet
projected. Affected records or members therefore become diagnosed `unknown`
rather than a partial interface. These attributes are recognized only with
their framework-signed identity.
`DtsEmitterTests.Emit_BlocksUnsupportedWireShapingContracts` gates the compiled
inheritance, number-handling, polymorphism, and extension-data cases.

The JSON envelope for each export comes only from an authentic, platform-signed
`System.Text.Json.JsonSerializer` call whose `JsonTypeInfo<T>` parameter is
likewise authenticated. A serialization contributes the return envelope only
when Analysis proves its string result reaches the method return or an
authenticated async-builder result sink; stream overloads, discarded results,
and unresolved flows do not contribute.
`JsonWireContractResolverTests.SerializerIdentityRequiresSignedSystemTextJsonAssembly`,
`Build_DoesNotTreatStreamSerializationAsReturnEnvelope`, and
`Build_DoesNotTreatDiscardedSerializationAsReturnEnvelope` plus
`MethodCallAnalysisTests.ClassifiesReturnedAndDiscardedCallResults` and
`ClassifiesSingleLocalUseAsCallArgument` gate these boundaries.

### Drift detection

`DriftDetector` compares generated and checked-in declarations as the exact
ordered sequence of trimmed, non-blank lines. Reordered declarations, moved
members, missing lines, and extra structure all count as drift; blank-line and
indentation-only differences do not.

When `--emit-js` and `--diff-against` are both present, tsbindgen validates the
declaration input and drift before creating or overwriting the JavaScript
destination. A missing or stale declaration file therefore leaves a prior
wrapper untouched rather than publishing a success-shaped partial result.
`TsBindGenCommandTests.Invoke_WithMissingDiffAgainst_DoesNotOverwriteExistingJavaScript`,
`Invoke_WithStaleDiffAgainst_DoesNotOverwriteExistingJavaScript`, and
`Invoke_WithCoveredDiffAgainst_PublishesJavaScript` gate that publication order.

### Unmapped types

When `tsbindgen` cannot map a C# type to TypeScript, it still emits `unknown`
in the generated declaration so the partial output remains inspectable, but it
also prints a diagnostic to stderr for every unmapped occurrence and exits
non-zero. That keeps CI from treating a lossy projection as success-shaped
output. JavaScript module output is suppressed whenever those mapping
diagnostics exist, so a lossy declaration cannot be paired with a
success-shaped wrapper artifact.
`TsBindGenCommandTests.Invoke_WithDiagnostics_DoesNotAttemptInvalidEmitJsPath`
gates that output boundary.

A DTO whose serializer contexts declare conflicting property-naming policies
is emitted as `unknown`, without guessing a policy, and the diagnostic keeps
generation red until the wire contract is corrected. The same rule applies to
enum roots. Supported snake-case and kebab-case policies delegate to
System.Text.Json's own Unicode-aware implementations;
`JsonNamingPoliciesTests.PoliciesMatchSystemTextJson` gates that wire-name
equivalence. Duplicate or malformed context-options rows are also unsupported
rather than resolved by metadata order. Non-default context options that change
serialized wire shape are unsupported until the object model projects their
semantics; formatting and read-only options remain accepted.
The authenticated `JsonSerializerDefaults.General` constructor value is also
accepted because it selects those default semantics; `Web` remains
unsupported. A generated `JsonTypeInfo<T>` getter is trusted only when its
receiver flows from the same context's generated `Default` property. A custom
context instance can carry runtime `JsonSerializerOptions` that differ from
the attribute, so unproven receivers fail before declarations or wrappers are
published.
Enum-valued options are decoded only when their serialized enum name carries a
complete platform-signed System.Text.Json assembly identity.
`JsonSourceGenerationOptionsAttributeTests` gates duplicate-row orders,
duplicate agreement, duplicate or malformed arguments within one row,
wire-shaping rejection, signed and unsigned byte-backed
`ReadCommentHandling` decoding, supported peer options, and the ordinary
single-row case.
`JsonSourceGenerationOptionsAttributeTests.JsonSerializerDefaultsConstructorAcceptsOnlyGeneral`
and
`JsExportSurfaceBuilderTests.Build_RejectsCustomSerializerContextInstanceReceiver`
gate the constructor and runtime-receiver boundaries.
`DtsEmitterTests.Emit_BlocksEnumWithUnsupportedContextOptions` gates the enum
path.
For scalar generated roots, unsupported context options are retained against
the exact `JsonTypeInfo<T>` getter. A scalar root has no DTO declaration to
carry the policy, so generation fails before declarations or a wrapper can be
published only when the getter actually reaches an export; an unused
unsupported scalar context does not poison a supported sibling.
`JsExportSurfaceBuilderTests.Build_RejectsReachedUnsupportedScalarContextOptions`,
`Build_IgnoresUnusedUnsupportedScalarContextAndResolvesVectorSibling`, and
`TsBindGenCommandTests.Invoke_UnsupportedScalarContextOptionsFailsBeforeDeclarationOrWrapperPublication`
are the gates.

Wire-shaping framework attributes are trusted only with their platform-signed
assembly identity and expected constructor/value shape. This applies to
`[Flags]`, `[JsonInclude]`, `[JsonIgnore]`, `[JsonPropertyName]`,
`[JsonStringEnumMemberName]`, and `[JsonSourceGenerationOptions]`; a same-name
attribute from another assembly cannot alter the projected contract.
`JsonPropertyNameAttributeTests.SameNameAttributeFromUntrustedAssemblyIsIgnored`
and
`JsonPropertyNameAttributeTests.AuthenticAttributeWithMalformedConstructorProducesRowMarker`
plus
`JsonPropertyNameAttributeTests.LocallyDefinedFrameworkNamedAttributeInModuleIsUnauthenticated`
and
`JsonSourceGenerationOptionsAttributeTests.SameNameOptionsAttributeFromUntrustedAssemblyIsIgnored`
gate the cross-assembly and manifest-less-module boundaries.

Attribute identity is compared structurally, as a namespace plus root-to-leaf
metadata name segments, never as flattened display text. A flattened nested
`TypeRef` chain spells itself exactly like a genuine top-level framework
attribute and can still resolve through the authentic signed `AssemblyRef`, so
display text alone cannot separate an impostor from the real attribute. This
applies to every authenticated framework attribute, including `[JSExport]` and
the System.Text.Json attributes above.
`JsonPropertyNameAttributeTests.NestedAttributeIdentityCannotAliasTopLevelFrameworkAttribute`
and `NestedIdentityCannotAliasTopLevelJsExportAttribute` gate the impostor and
the matching genuine positives; both first assert that the flattened spelling
really does alias, so the gate cannot pass vacuously.
`JsonPropertyNameAttributeTests.TopLevelAttributeIdentityStillAuthenticates`
gates the genuine top-level case.

Authentic but unreadable `[JsonIgnore]` and `[JsonInclude]` metadata is visible
unsupported evidence, not absence. Such a row is preserved with the same
malformed-row marker convention as `[JsonPropertyName]`, and generation stops
with a token-only diagnostic rather than emitting a success-shaped declaration
from metadata that could not be decoded. Duplicate `[JsonIgnore]` rows are
equally fatal. A same-name attribute from an untrusted assembly is still
ignored outright, because it never claimed the framework's meaning.
`JsonPropertyNameAttributeTests.MalformedAuthenticJsonIgnoreIsUnsupportedEvidence`,
`MalformedAuthenticJsonIncludeIsUnsupportedEvidence`,
`UntrustedJsonIgnoreAttributeIsIgnoredRatherThanMalformed`, and
`UntrustedJsonIncludeAttributeIsIgnoredRatherThanMalformed` gate the metadata
side, while `DtsEmitterTests.Emit_RefusesMalformedOrDuplicateJsonIgnoreRows`,
`Emit_RefusesMalformedJsonIncludeRows`, and
`Emit_StopsGenerationForPatchedMalformedJsonIgnoreAttribute` gate end-to-end
generation, the last against a patched real compiled assembly.

Type- or member-level custom `[JsonConverter]` attributes can replace the
entire inferred wire shape. Types using an unsupported converter are emitted
as diagnosed `unknown`; individual serialized members using one have an
`unknown` TypeScript type. Ignored members remain irrelevant. Exactly one framework-signed `JsonStringEnumConverter` on an enum remains
supported; its constructor and complete value blob must have the expected
shape, and its generic argument, when present, must identify that exact enum by
complete ECMA assembly identity and qualified type name. Duplicate, malformed,
mismatched, spoofed, or other converter metadata is not guessed.
Once a type-level converter makes the CLR member shape irrelevant, well-formed
member wire names cannot turn that diagnosed `unknown` into a fatal result;
malformed attribute rows remain fatal.
`DtsEmitterTests.Emit_BlocksUnsupportedTypeAndMemberConverters`,
`Emit_AllowsExactlyOneSupportedStringEnumConverter`, and
`Emit_ConverterControlledTypeIgnoresResolvedMemberNames` plus
`JsExportSurfaceBuilderTests.Extract_CapturesJsonConverterAndEnumWireNameFacts`
and `Extract_RejectsStringEnumConverterForAnotherEnum` gate these boundaries
against both direct models and compiled metadata.

For string-converted enums, `[JsonStringEnumMemberName]` supplies the emitted
string wire value. The current metadata model does not prove a converter was
configured with `allowIntegerValues: false`, so declarations also admit the
default System.Text.Json numeric fallback: regular enums are a string-literal
union plus `number`, while flags enums are `string | number`. Arbitrary values
are safely escaped, equal wire values are deduplicated in the TypeScript union,
and duplicate or malformed attribute rows stop generation before output.
The ordered nullable row evidence is persisted in production API JSON, and the
single resolved wire name is derived from that evidence after a round trip
rather than falling back to the CLR field name.
`JsonPropertyNameAttributeTests.JsonStringEnumMemberName*` gates ordered
metadata evidence, while
`ApiOutputFormatterTests.ApiTypeJson_RoundTripsEnumWireNameEvidence` gates the
production JSON contract and
`DtsEmitterTests.Emit_ProjectsStringConvertedEnumAsStringLiteralAndNumberUnion`,
`Emit_ProjectsStringConvertedFlagsEnumAsStringAndNumber`,
`SourceGeneratedJson_StringEnumConverterAllowsUndefinedNumericValues`,
`Emit_UsesEscapedDeduplicatedEnumWireNames`, and
`Emit_RefusesMalformedOrDuplicateEnumWireNames` gate emission and the real STJ
oracle.

A control character in `[JsonPropertyName]` is a harder boundary: generation
stops without emitting declarations, and reports only a safe metadata location
without echoing the unsafe wire name. This validation covers properties,
fields, enum members, and field-targeted attributes on auto-properties,
including members otherwise excluded from serialization. Duplicate or malformed
`[JsonPropertyName]` metadata, control-bearing resolved member names, and
colliding resolved JSON names are rejected the same way.
`JsonPropertyNameAttributeTests.UnexpectedNamedArgumentProducesMalformedRowMarker`
gates semantic row validation before the emitter sees the contract.
`DtsEmitterTests.Emit_RefusesControlCharactersInResolvedMemberNames` and
`DtsEmitterTests.Emit_RefusesDuplicateResolvedMemberNames` gate those resolved
name boundaries.

Generation also stops when nested types, TypeScript reserved/predefined type
names, tsbindgen's own `Promise`/`Record` vocabulary, or multiple CLR types
would produce an illegal or ambiguous declaration name rather than inventing a
disambiguation scheme. Export function, declaring-type, and parameter names are
validated before either declarations or JavaScript wrappers are emitted, and
strict-mode names and collisions with the wrapper's generated `dotnet`,
`<name>Export`, and `result` bindings are fatal. Valid Unicode TypeScript
identifiers remain supported, including TypeScript's measured continuation-only
edge points. Identifier acceptance is pinned to the TypeScript 7.0.2 scanner
rather than the runtime's newer Unicode tables. Qualified CLR type identities must match a discovered local identity by
complete ECMA assembly identity and structured metadata definition name; the
structure distinguishes a top-level `N.A.B` from nested `N.A+B` even when a
display projection is identical. An unrelated external type with the same
assembly simple name, simple type name, or namespace-qualified name becomes
diagnosed `unknown`, not an alias. Built-in mappings such as
`Task<T>`, `Dictionary<TKey, TValue>`, primitives, and `JsonElement` likewise
require both a platform signature and an allowed defining or contract assembly
for that exact mapping plus the expected top-level
`MetadataTypeDefinitionName`; same-name external types, nested lookalikes, and
references that merely claim a platform token become diagnosed `unknown`.
`DtsEmitterTests.Emit_DoesNotApplyDictionarySemanticsToLookalikeType` and
`Emit_DoesNotApplyTaskSemanticsToLookalikeType` plus
`Emit_DoesNotTrustClaimedPlatformTokenFromWrongAssembly` gate this framework
boundary;
`Emit_NestedIdentityCannotAliasNamespaceQualifiedType` gates structured type
identity, and
`Emit_DoesNotMapExtractedNestedFrameworkByteOrTaskLookalikes` gates the
extracted nested Byte/Task impostors.
Empty string-converted enums
are rejected before output. Property keys use the broader `IdentifierName` grammar,
where reserved words remain valid and do not require quoting;
`DtsEmitterTests.Emit_DoesNotQuoteReservedWordsUsedAsPropertyKeys` gates that
distinction.
`DtsEmitterTests.Emit_RefusesInvalidJsExportIdentifiers`,
`DtsEmitterTests.Emit_RefusesJsExportNameCollisions`, and
`DtsEmitterTests.Emit_RefusesGeneratedModuleBindingCollisions` plus
`DtsEmitterTests.Emit_RefusesParameterThatShadowsItsExportSlot` gate the export
path. `DtsEmitterTests.Emit_RefusesIdentifiersNewerThanPinnedTypeScriptUnicode`,
`TsTypeMapperTests.Map_QualifiedExternalTypeDoesNotAliasLocalRecord`, and
`DtsEmitterTests.Emit_ExternalEnvelopeCannotAliasLocalQualifiedType` plus
`Emit_ExternalSignatureTypesCannotAliasLocalQualifiedType`,
`JsExportSurfaceBuilderTests.Build_DoesNotAliasExternalContextRootToLocalType`
and `Build_DoesNotTrustLookalikeSerializerContextTypes`
and `DtsEmitterTests.Emit_RefusesEmptyStringConvertedEnumBeforeOutput` gate the
remaining declaration boundaries. Incomplete metadata extraction and unsafe,
signature-less, or degraded JS-export/wire signatures stop before declaration
or file output and report only token-based locations; incomplete extraction is
rejected before body analysis begins. A recoverable body-analysis diagnostic
for a JS export, including its compiler-generated async implementation, is also
fatal because its JSON envelope evidence may be incomplete; diagnostics for
unrelated methods remain irrelevant.
`TsBindGenCommandTests.Invoke_IncompleteExtractionFailsWithoutOutput` and
`JsExportSurfaceBuilderTests.Build_InvalidExportUsesContainedFailure` plus the
`Build_RejectsDegraded*` and
`Build_RejectsOnlyExportScopedBodyDiagnostics` plus
`Build_MalformedContextUsesContainedTokenLocation` gate those boundaries.
`JsonWireContractResolverTests.Build_RejectsRealAsyncStateMachineAnalysisFailure`
corrupts a compiled `MoveNext` body and gates production source-method
attribution. `Build_IgnoresLookalikeJsExportAttribute` and
`Extract_DoesNotTrustSameNameJsExportFromAnotherAssembly` plus
`Extract_DoesNotTrustMalformedAuthenticJsExportRows` gate the exact,
framework-signed `System.Runtime.InteropServices.JavaScript.JSExport`
constructor and value contract. Authentic JSExport rows retain a count and
malformed marker through the `ApiMember` JSON contract: malformed-only,
valid-plus-malformed, and duplicate-valid evidence stop before declaration or
wrapper emission, while untrusted lookalikes remain absent.
MethodDefs deliberately filtered from the declarable API inventory retain the
same evidence separately on their containing type, so an attributed accessor
or compiler-generated local function cannot disappear as absence. Rows inside
a wholly filtered compiler-generated type are retained at surface scope for
the same reason. Method generic arity and exact MethodDef body presence are
likewise persisted: the runtime generator emits no generic, `abstract`, or
`extern` method wrapper, so those `[JSExport]` shapes are rejected before
publication.
Metadata also retains only an exact `__Wrapper_<name>_<digits>` MethodDef
backed by an authentic SDK `DynamicDependency` registration as a candidate,
never treating that declaration fact as body provenance. Analysis
authenticates the generated wrapper-to-local-stub-to-exact-export MethodDef
call chain before tsbindgen emits declarations or JavaScript. A prefix sibling
and a handwritten candidate therefore both fail before publication.
`JsExportSurfaceBuilderTests.Extract_RetainsMalformedAuthenticJsExportRowsAsFailureEvidence`,
`Extract_RejectsDuplicateOrMixedAuthenticJsExportRows`, and
`ApiOutputFormatterTests.ApiTypeJson_RoundTripsRuntimeJsExportFailureEvidence`
are the gates.
The runtime generator publishes ordinary method declarations, not operators or
other `ApiMember.Kind` values. An authentic non-method `[JSExport]` therefore
fails before declarations or wrappers are emitted.
`JsExportSurfaceBuilderTests.Build_RejectsAuthenticJsExportOperatorBeforePublication`,
`Build_RejectsGenericJsExportWithoutRuntimeWrapper`,
`Build_RejectsBodylessJsExportsWithoutRuntimeWrappers`,
`Build_RejectsHandwrittenRuntimeWrapperCandidate`,
`Build_DoesNotCreditPrefixSiblingWrapper`,
`Build_RejectsIndexedGetterWithGeneratedRootName`,
`Extract_RetainsFilteredJsExportMethodDefsAsFailureEvidence`,
`Extract_RetainsFilteredJsExportRowsFromCompilerGeneratedTypes`,
`SourceGeneratedJsExport_EmitsOnlyOrdinaryMethodWrappers`, and
`TsBindGenCommandTests.Invoke_JsExportOperatorFailsBeforeDeclarationOrWrapperPublication`
plus `Invoke_FilteredGeneratedTypeExportFailsBeforePublication` are the gates.
`ApiOutputFormatterTests.ApiTypeJson_RoundTripsRuntimeJsExportFailureEvidence`
and `ApiSurfaceJson_RoundTripsSurfaceScopedJsExportFailureEvidence` gate the
persistent MethodDef and surface-level evidence.
`Extract_ChargesSerializedConverterTypeNameBeforeDecode` gates bounded
materialization accounting for converter-controlled serialized type names.
Property and field metadata tokens identify the precise offending row in fatal
messages. Artifact-derived text in diagnostics is visually contained before it
reaches stderr.
`DtsEmitterTests.Emit_AcceptsUnicodeTypeScriptIdentifiers`,
`DtsEmitterTests.Emit_RefusesUnicodePatternSyntaxAsIdentifierStart`,
`DtsEmitterTests.Emit_RefusesForbiddenTypeDeclarationNames`,
`DtsEmitterTests.Emit_DoesNotEchoRejectedTypeNames`, and
`TsBindGenDiagnosticsTests.ReportUnmappedType_ContainsArtifactText` gate those
identifier and diagnostic properties.

## Testing

Run the test suite in Release:

```bash
dotnet run --project tests/ILInspector.JsExportSurface.Tests -c Release
```

Tests validate both `ILInspector.JsExportSurface` and `tsbindgen` against
`ILInspector.JsExportSurface.Fixtures`, a small purpose-built `[JSExport]`
surface (not a real product surface) covering nested records, arrays,
nullables, JSON naming-policy variance, and sync/async/non-generic-`Task`
exports.
