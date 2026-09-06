# JSON union wire fixture

This assembly owns the native-union export inventory used by
`JsonUnionWireTests`. Its assembly boundary keeps unsupported read, converter,
case-mapping, and recursive-alias exports out of the canonical supported facade
fixture. Resolve it through `FixtureIds.JsExportUnions`.

The source-generated context covers scalar, DTO, generic, nested, collection,
and custom-converted unions, alongside an ordinary object and a raw string
export. The tests select an export from the extracted metadata while preserving
its compiler-generated runtime registration and serializer body evidence.

`ObjectUnion`, `NumberUnion`, and `GenericUnion<byte[]>` deliberately produce
`SYSLIB1227`: their
alternatives can be serialized, but the default read classifier is ambiguous.
Only that warning is exempted from warnings-as-errors in this fixture project;
it remains visible. Other source-generator warnings are not suppressed.

The runtime oracle uses SDK `11.0.100-preview.7.26381.103`, with System.Text.Json
source pinned to
[`e2c1e00b3d0f96afb892fb261d5921565b400246`](https://github.com/dotnet/dotnet/tree/e2c1e00b3d0f96afb892fb261d5921565b400246/src/runtime/src/libraries/System.Text.Json).
Generated union metadata selects the actual case and writes its own contract
inline; its null arm accounts for the default value. A nested scalar union's
number case and the ambiguous unions are negative read controls, not evidence
that all successfully written unions can be deserialized.

TypeScript lowering additionally compares closed generic byte-array and
dictionary arguments with their real JSON representations. The `T[]`
case-signature pair writes a Base64 string for bytes and an array for integers;
it cannot be represented by substituting an erased JSON type into a generic
TypeScript array. Recursive union aliases, unsupported case types, and
generic-parameter/module-binding name collisions have separate controls.
