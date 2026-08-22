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

Generated interfaces include properties with an accessible getter, properties
whose non-public getter is opted in with `[JsonInclude]`, and non-static
`[JsonInclude]` fields. `[JsonIgnore(Condition = Never)]` keeps a member in the
shape; other ignore conditions exclude it because callers cannot rely on its
presence. Write-only properties remain excluded even when annotated. The same
wire-member rule drives transitive DTO discovery and declaration emission so a
discovered edge cannot become an orphaned or incomplete TypeScript shape;
`DtsEmitterTests.Emit_IncludesJsonIncludedFieldsInParentInterface` and
`DtsEmitterTests.Emit_UsesGetterAccessibilityForCompiledProperties` gate that
shared-rule invariant, while
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
generation red until the wire contract is corrected. Duplicate or malformed
context-options rows are also unsupported rather than resolved by metadata
order. `JsonSourceGenerationOptionsAttributeTests` gates both duplicate-row
orders, duplicate agreement, duplicate or malformed arguments within one row,
supported peer options, and the ordinary single-row case.

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
post-camel-case collisions are fatal. Valid Unicode TypeScript identifiers
remain supported, including TypeScript's measured continuation-only edge
points. Property keys use the broader `IdentifierName` grammar, where reserved
words remain valid and do not require quoting;
`DtsEmitterTests.Emit_DoesNotQuoteReservedWordsUsedAsPropertyKeys` gates that
distinction.
`DtsEmitterTests.Emit_RefusesInvalidJsExportIdentifiers`,
`DtsEmitterTests.Emit_RefusesJsExportNameCollisions`, and
`DtsEmitterTests.Emit_RefusesJsExportParameterNameCollisions` gate the export
path. Artifact-derived text in diagnostics is visually contained before it
reaches stderr. `DtsEmitterTests.Emit_AcceptsUnicodeTypeScriptIdentifiers`,
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
