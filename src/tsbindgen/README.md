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

### Record shape discovery

A `[JSExport]` method's signature in this ABI style is always a plain
`string`/`Task<string>` — the real DTO type only appears inside the method
body, via a call such as `JsonSerializer.Serialize(dto, Context.Default.SomeDto)`.
Record shapes are therefore discovered from the assembly's
`JsonSerializerContext`-derived type: each `[JsonSerializable(typeof(T))]` on
that type compiles to a `JsonTypeInfo<T>`-typed property, which is readable
directly from metadata. This list is not a heuristic — System.Text.Json's
fast (non-reflection) serialization path requires every (de)serialized type to
be registered there, so it is exactly the set of shapes that can flow across
the `[JSExport]` boundary via this pattern.

Generated interfaces include properties with an accessible getter and
`[JsonInclude]` properties or fields accessible to the source-generated
context. Private, private-protected, and protected members remain excluded,
matching the source generator's `SYSLIB1038` boundary; internal, protected
internal, and public members are accessible. `[JsonIgnore(Condition = Never)]`
keeps a member in the shape; other ignore conditions exclude it because callers
cannot rely on its presence. Write-only properties remain excluded even when
annotated. The same wire-member rule drives transitive DTO discovery and
declaration emission so a discovered edge cannot become an orphaned or
incomplete TypeScript shape;
`DtsEmitterTests.Emit_IncludesJsonIncludedFieldsInParentInterface` and
`DtsEmitterTests.SourceGeneratedJson_OmitsInaccessibleJsonIncludedMembers`
plus `DtsEmitterTests.Emit_MatchesSourceGeneratedJsonIncludeAccessibility`
gate that shared-rule invariant against the real source generator, while
`DtsEmitterTests.Emit_IncludesPropertyWithJsonIgnoreNever` gates the explicit
`Never` exception.

### Drift detection

`DriftDetector` compares generated and checked-in declarations as the exact
ordered sequence of trimmed, non-blank lines. Reordered declarations, moved
members, missing lines, and extra structure all count as drift; blank-line and
indentation-only differences do not.

### Unmapped types

When `tsbindgen` cannot map a C# type to TypeScript, it still emits `unknown`
in the generated declaration so the partial output remains inspectable, but it
also prints a diagnostic to stderr for every unmapped occurrence and exits
non-zero. That keeps CI from treating a lossy projection as success-shaped
output.

A DTO whose serializer contexts declare conflicting property-naming policies
is emitted as `unknown`, without guessing a policy, and the diagnostic keeps
generation red until the wire contract is corrected. The same rule applies to
enum roots. Duplicate or malformed context-options rows are also unsupported
rather than resolved by metadata order. Non-default context options that change
serialized wire shape are unsupported until the object model projects their
semantics; formatting and read-only options remain accepted.
`JsonSourceGenerationOptionsAttributeTests` gates duplicate-row orders,
duplicate agreement, duplicate or malformed arguments within one row,
wire-shaping rejection, byte-backed `ReadCommentHandling` decoding, supported
peer options, and the ordinary single-row case.
`DtsEmitterTests.Emit_BlocksEnumWithUnsupportedContextOptions` gates the enum
path.

Type- or member-level custom `[JsonConverter]` attributes can replace the
entire inferred wire shape. Types using an unsupported converter are emitted
as diagnosed `unknown`; individual serialized members using one have an
`unknown` TypeScript type. Ignored members remain irrelevant. Exactly one
`JsonStringEnumConverter` on an enum remains supported, while duplicate,
malformed, or other converter metadata is not guessed.
`DtsEmitterTests.Emit_BlocksUnsupportedTypeAndMemberConverters`,
`Emit_AllowsExactlyOneSupportedStringEnumConverter`, and
`JsExportSurfaceBuilderTests.Extract_CapturesJsonConverterAndEnumWireNameFacts`
gate these boundaries against both direct models and compiled metadata.

For string-converted enums, `[JsonStringEnumMemberName]` supplies the emitted
wire value. Arbitrary values are safely escaped, equal wire values are
deduplicated in the TypeScript union, and duplicate or malformed attribute
rows stop generation before output.
`JsonPropertyNameAttributeTests.JsonStringEnumMemberName*` gates ordered
metadata evidence, while
`DtsEmitterTests.Emit_UsesEscapedDeduplicatedEnumWireNames` and
`Emit_RefusesMalformedOrDuplicateEnumWireNames` gate emission.

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
rather than the runtime's newer Unicode tables. Qualified CLR type identities
must match a discovered local identity exactly; an unrelated external type with
the same simple name becomes diagnosed `unknown`, not an alias. Empty
string-converted enums are rejected before output. Property keys use the
broader `IdentifierName` grammar, where reserved words remain valid and do not
require quoting;
`DtsEmitterTests.Emit_DoesNotQuoteReservedWordsUsedAsPropertyKeys` gates that
distinction.
`DtsEmitterTests.Emit_RefusesInvalidJsExportIdentifiers`,
`DtsEmitterTests.Emit_RefusesJsExportNameCollisions`, and
`DtsEmitterTests.Emit_RefusesGeneratedModuleBindingCollisions` plus
`DtsEmitterTests.Emit_RefusesParameterThatShadowsItsExportSlot` gate the export
path. `DtsEmitterTests.Emit_RefusesIdentifiersNewerThanPinnedTypeScriptUnicode`,
`TsTypeMapperTests.Map_QualifiedExternalTypeDoesNotAliasLocalRecord`, and
`DtsEmitterTests.Emit_RefusesEmptyStringConvertedEnumBeforeOutput` gate the
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
`Build_RejectsOnlyExportScopedBodyDiagnostics` tests gate those boundaries.
`Build_IgnoresLookalikeJsExportAttribute` gates exact
`System.Runtime.InteropServices.JavaScript.JSExport` identity. Artifact-derived
text in diagnostics is visually contained before it reaches stderr.
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
