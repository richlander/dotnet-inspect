# JSON union wire fixture

This assembly owns the native-union export inventory used by
`JsonUnionWireTests`. Its assembly boundary keeps unsupported read, converter,
case-mapping, and recursive-alias exports out of the canonical supported facade
fixture. Resolve it through `FixtureIds.JsExportUnions`.

The source-generated context covers scalar, DTO, generic, nested, collection,
and custom-converted unions, alongside an ordinary object and a raw string
export. Scenario-adjacent shapes make each boundary recognizable:
`PackageReadResult<T>` is a payload-or-problem result,
`ItemsOrCount<T>` is a detail-or-summary response, and the inspection unions
distinguish arbitrary values from structural rows. The tests select an export
from the extracted metadata while preserving its compiler-generated runtime
registration and serializer body evidence.

The package-resolution, metric, arbitrary-inspection-value, and raw-inspection
result scenarios deliberately have overlapping JSON token shapes. Their local
`SYSLIB1227` suppressions document that write-only boundary: adding a
classifier would invent a read contract that the bridge neither consumes nor
supports, while changing the cases would remove the behavior under test. The
fixture project otherwise keeps warnings as errors. Avoidable ambiguity, such
as a byte payload competing with a string case, is represented instead as the
coherent `PackageReadResult<byte[]>` payload-or-problem scenario.

The runtime oracle uses SDK `11.0.100-preview.7.26381.103`, with System.Text.Json
source pinned to
[`e2c1e00b3d0f96afb892fb261d5921565b400246`](https://github.com/dotnet/dotnet/tree/e2c1e00b3d0f96afb892fb261d5921565b400246/src/runtime/src/libraries/System.Text.Json).
Generated union metadata selects the actual case and writes its own contract
inline; its null arm accounts for the default value. A nested scalar union's
number case and the ambiguous unions are negative read controls, not evidence
that all successfully written unions can be deserialized.

TypeScript lowering additionally compares closed generic byte-array and
dictionary arguments with their real JSON representations. The
`ItemsOrCount<T>` list case writes a Base64 string for bytes and an array for
integers; it cannot be represented by substituting an erased JSON type into a
generic TypeScript array. Recursive union aliases, unsupported case types, and
generic-parameter/module-binding name collisions have separate controls.
