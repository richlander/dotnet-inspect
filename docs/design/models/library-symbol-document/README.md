# Library symbol document vectors

These vectors witness the [Library symbol document](../../library-symbol-document.md)
shape. The design states principles; this directory states the exact shape
through one example document and the questions it must answer.

## Contents

- `system-text-json.symbols.json` is an excerpt of the document for
  `System.Text.Json` 10.0.14 from nuget.org: six Types, including the
  `IsExternalInit` forwarder, and eight Members chosen to cover methods, a
  property, generic methods, a `ref` parameter, abstract Members without
  source lines, attributes, and exact-versus-mentioned return types. `counts`
  describes the excerpt, not the whole Library.
- `queries/*.jq` is the query catalog. The first line of each file is the
  question; the rest is the `jq` program that answers it.
- `queries/*.expected` holds each recorded answer.
- `check.sh` runs every query and compares its answer, and checks that a
  `grep` match is one whole JSON record.

## Provenance

Names, signatures, documentation IDs, summaries, metadata tokens, the MVID,
and the forwarder target come from the package's
`lib/net10.0/System.Text.Json.dll`, read with production dotnet-inspect and
`System.Reflection.Metadata` on 2026-11-06. Source URLs are the package's
SourceLink mapping at commit `0427b9abbead3fa64c6655eb4290c537397131fa` of
`dotnet/dotnet`. Source lines are the first statement of each body in those
files; the producer's value is the member Source Locations section's, and
slice 1 replaces any line that differs. Canonical signatures follow the
`MemberCanonicalSignature` full-name grammar (`System.String`, no nullable
annotations); production 0.27 `type --json` emits `string` and `?` instead,
tracked by [#11033](https://github.com/richlander/dotnet-inspect/issues/11033). Lookups were derived from the records,
and query 17 checks that derivation.

Every Type and Member record in the excerpt is field-for-field identical to
the corresponding record in the complete prototype document that
`tools/ExplainReadingScenarios/usability/symbol-document-format/prototype` generates from the same package, PDB,
and documentation file.

## Running

```bash
docs/design/models/library-symbol-document/check.sh
```

Pass a produced document as the first argument to run the catalog against it.
Pass `--record` as the second argument to rewrite the expectations after a
reviewed shape change.
