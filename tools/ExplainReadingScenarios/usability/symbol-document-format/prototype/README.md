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

## Fidelity

For `System.Text.Json` 10.0.14 (`net10.0`) the output is 1,787,789 bytes,
SHA-256 `f0ee4d5a55c214dc83db6beb66425976ce7d769abde8082ffe4580d148df19e0`, and
two runs from fresh downloads are byte-identical. It holds 85 Types (one
forwarder) and 975 Members.

| Check | Result |
| --- | --- |
| Display signatures equal production dotnet-inspect 0.27 `type --json` | 301 of 301 compared Members across seven Types |
| Documentation IDs found in the package's XML documentation file | 1,054 of 1,060 records; the other six are implicit constructors with no entry |
| Methods taking exactly `System.IO.Stream` | 30 across four Types, equal to an independent metadata count |
| Members carrying `RequiresUnreferencedCode` | 54, equal to an independent metadata count, including three properties whose accessors carry it |
| Members with a source line | 885; the rest are 80 Members without an IL body and 10 implicit constructors the PDB does not map |
| Design vectors | Every excerpt record is identical to its record here |
| Lookups | Equal to their derivation from the records (catalog query 17) |

Known approximations:

- Name families use a regular-expression word split, not the CSharpText
  identifier segmenter that the name-family summary owns.
- Canonical signatures follow `MemberCanonicalSignature`'s stated full-name
  grammar, which production 0.27 does not
  ([#11033](https://github.com/richlander/dotnet-inspect/issues/11033)).
- Attribute types omit compiler-emitted attributes in
  `System.Runtime.CompilerServices` and debugger attributes.
- Documentation summaries render `cref` targets as their simple names.
