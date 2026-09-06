# TypeScript facade fixture

This assembly is the canonical *supported* producer inspected by
`eng/test-ts-jsexport-typescript.sh`. Every export here must lower to a
publishable facade, because the harness generates and compiles the whole
assembly's TypeScript. Unsupported evidence — read classifiers, custom
converters, recursive aliases, unmapped case shapes — belongs in
`ILInspector.JsExportSurface.UnionFixtures`, whose assembly boundary keeps it
out of this facade.

`UnionSelections.cs` owns the native C# union inputs for the JSON union
lowering contract in
[`docs/design/ts-jsexport.md`](../../../docs/design/ts-jsexport.md):

- `WidgetSelection` — a DTO-or-string union whose default state is the null
  alternative.
- `FlagSelection` — a `bool?` value case beside a DTO case.
- `OutcomeSelection` — a nested union case beside a primitive case.
- `KindSelection` — a local enum case beside a string case.
- `CollectionSelection` — array and dictionary cases of a reference type. The
  array declares non-nullable entries and still writes a null entry, matching
  the conservative `T | null` entry lowering that signature-only case facts
  require. `SelectionEnvelope.Group` repeats that for a closed generic
  argument container.
- `Boxed<TValue>` — a generic union definition used only through closed
  instantiations (`Boxed<int>`, `Boxed<WidgetDto>`); the C# type parameter name
  is deliberately unlike the emitted TypeScript parameter name.
- `Wrapped<TValue>` — a direct type-parameter case closed over `byte[]`, whose
  JSON wire form is a Base64 string rather than an array. A parameter embedded
  in a case signature, such as `T[]`, stays unsupported and lives in
  `ILInspector.JsExportSurface.UnionFixtures`.
- `SelectionEnvelope` — union-valued members, arrays, and dictionaries.

Every union export writes with the source-generated
`UnionFixtureJsonContext`, so the fixture reaches serialization only. Reading a
union stays outside this fixture's contract.

`tests/ILInspector.JsExportSurface.Tests/Fixtures/ts-jsexport-runtime/union-payloads.cs`
runs the compiled exports and captures their real System.Text.Json output. The
harness feeds that output through the managed-runtime seam so the generated
facade, its compiled consumer, and the derived JavaScript are exercised against
producer-authored payloads rather than hand-written JSON shadows.
