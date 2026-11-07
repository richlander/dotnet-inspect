# Library symbol document prototype

This throwaway generator writes a complete
[Library symbol document](../../../../../docs/design/library-symbol-document.md) before
the product producer exists, so the
[format trial](../../../../../docs/design/library-symbol-document.md#workflow-trials)
can test the shape with agents while it is still cheap to change. It is not
the producer: slice 1 of [#11028](https://github.com/richlander/dotnet-inspect/issues/11028)
replaces it, and nothing in the product references it.

## Use

```bash
tools/ExplainReadingScenarios/usability/symbol-document-format/prototype/generate.sh <work-dir> [package-id] [version] [tfm]
```

The script downloads the package from nuget.org and its portable PDB from the
Microsoft symbol server, then runs two steps:

- `extract.cs` reads metadata, the XML documentation file, and the PDB with
  `System.Reflection.Metadata` and writes the records.
- `layout.py` derives every lookup from those records and writes one record
  per line.
- `excerpt.py` selects the design vectors' Types and Members from the
  extracted records before layout.

## Fidelity

For `System.Text.Json` 10.0.14 (`net10.0`) the output is 2,043,506 bytes,
SHA-256 `2ee8f40bb7aaa1fb43494f420757c0c2989524ec485f5aae6606a612af1f27f4`, and
two runs from fresh downloads are byte-identical. It holds 85 Types (one
forwarder) and 975 Members.

| Check | Result |
| --- | --- |
| Display signatures equal production dotnet-inspect 0.27 `type --json` | 301 of 301 compared Members across seven Types |
| Documentation IDs found in the package's XML documentation file | 1,054 of 1,060 records; the other six are implicit constructors with no entry |
| Methods taking exactly `System.IO.Stream` | 30 across four Types, equal to an independent metadata count |
| Members carrying `RequiresUnreferencedCode` | 54, equal to an independent metadata count, including three properties whose accessors carry it |
| Members with a source line | 885; the rest are 80 Members without an IL body and 10 implicit constructors the PDB does not map |
| Producers of `JsonDocument` (`produced_types`) | 13: `Parse` ×5, `ParseAsync`, `ParseValue`, `TryParseValue` (`out`), and `SerializeToDocument` ×5, with no wrapper such as `JsonConverter<JsonDocument>` |
| `required_overrides` | `JsonConverter<T>` requires `Read` and `Write`; `JsonConverterFactory` requires `CreateConverter` and `CanConvert`; `JsonConverter` requires 15, 13 of them inaccessible |
| `constructor_accessibility` | `JsonConverter` is `internal`, matching the CS1729 a derived class in another assembly gets; `JsonConverter<T>` is `protected_internal` and `JsonConverterFactory` `protected` |
| `overrides` | Set for 12 of 20 overriding Members; the other 8 override Members declared outside the Library, such as `Object.ToString` |
| Design vectors | `excerpt.py` selects the excerpt from this output, so every excerpt record is identical by construction |
| Lookups | Equal to their derivation from the records (catalog query 17) |

Known approximations:

- Name families and name words use a regular-expression word split, not
  the CSharpText identifier segmenter that the name-family summary owns.
- `overrides` and `required_overrides` follow the base chain only within the
  Library and match base Members by name, generic arity, and substituted
  parameter types.
- Canonical signatures follow `MemberCanonicalSignature`'s stated full-name
  grammar, which production 0.27 does not
  ([#11033](https://github.com/richlander/dotnet-inspect/issues/11033)).
- Attribute types omit compiler-emitted attributes in
  `System.Runtime.CompilerServices` and debugger attributes.
- Documentation summaries render `cref` targets as their simple names.
