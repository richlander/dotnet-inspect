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

### Inert text

A DTO property whose C# type is `InertText.InertString` generates as the opaque
TypeScript string brand `InertString`, while an ordinary `System.String`
property remains `string`. The brand preserves the compiler-visible fact that
the C# producer applied an inert-text policy without introducing a JavaScript
runtime wrapper. JSON still carries the encoded value as a string, so normal
JSON parsing and string-oriented browser APIs continue to work.

The brand deliberately does not name `Field` versus `Prose`: the C# currency
type records that some policy was applied, not which one, because composition
may tighten a value under a different policy. It also does not preserve
`Forms`, `Concerns`, or `IsTruncated`; those facts are not recoverable from the
string-valued wire representation. A contract that needs them must expose a
separate typed envelope rather than implying that the brand carries metadata it
does not.

`InertString` is not HTML-safe text. A browser rendering boundary must still
apply its sink-specific HTML, attribute, URL, or DOM-text handling.

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
