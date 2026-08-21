# ILInspector.JsExportSurface

`ILInspector.JsExportSurface` is a C#-faithful object model of an assembly's
`[JSExport]` wasm/JS interop surface, projected from `ILInspector.Metadata`'s
`ApiSurface`/`ApiSurfaceExtractor`.

`JsExportSurfaceBuilder.Build` discovers:

- **Functions** — every `[JSExport]`-attributed static member, with its
  declaring type, parameters, and return type reported unmodified (a
  `Task<string>` is reported as `Task<string>`, never unwrapped to a
  target-language concept such as `Promise<T>`).
- **Records** — the transitive closure of record shapes reachable from the
  assembly's `JsonSerializerContext`-derived type's `[JsonSerializable(typeof(T))]`
  roots, since `[JSExport]` method signatures alone don't reveal the DTO shapes
  serialized inside their bodies. See the `<remarks>` on
  `JsExportSurfaceBuilder` for the full rationale.

This library intentionally stays free of any target-language opinion (naming
policy, `Promise` unwrapping, `.d.ts` syntax); that "personality" belongs to a
consumer such as [`tsbindgen`](../tsbindgen).

Run its test suite in Release:

```bash
dotnet run --project tests/ILInspector.JsExportSurface.Tests -c Release
```

Tests validate this library and `tsbindgen` together against
`ILInspector.JsExportSurface.Fixtures`, a small purpose-built `[JSExport]`
surface used only as a regression fixture.
