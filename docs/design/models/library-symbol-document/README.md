# Library symbol document vectors

These vectors witness the [Library symbol document](../../library-symbol-document.md)
shape. The design states principles; this directory states the exact shape
through one example document and the questions it must answer.

## Contents

- `system-text-json.symbols.json` is an excerpt of the document for
  `System.Text.Json` 10.0.14 from nuget.org: eight Types, including the
  `IsExternalInit` forwarder and `JsonAttribute`, whose base is in another
  Library, and twelve Members chosen to cover methods, properties, generic
  methods, `ref` and `out` parameters, abstract Members without source lines,
  overrides inside and outside the Library, attributes, and exact, mentioned,
  and produced types. `counts` and the lookups describe the excerpt, not the
  whole Library.
- `queries/*.jq` is the query catalog. The first line of each file is the
  question; the rest is the `jq` program that answers it.
- `queries/*.expected` holds each recorded answer, true of the excerpt only.
- `invariants/*.jq` must print `true` for any document: lookups equal their
  derivation, no value is null and every record of a kind has the same
  fields, counts and outcome agree, and every status is in the vocabulary.
- `check.sh` checks that every member line is one whole JSON record, runs the
  invariants, and, for the excerpt, compares every catalog answer.

## Provenance

The excerpt is selected, not authored. The format-trial prototype in
`tools/ExplainReadingScenarios/usability/symbol-document-format/prototype`
generates the complete document from the package's
`lib/net10.0/System.Text.Json.dll`, its XML documentation file, and its
portable PDB from the Microsoft symbol server. Its `excerpt.py` keeps the eight
Types and twelve Members, and `layout.py` derives the excerpt's lookups and
counts, so every record is identical to the complete document's. Query 17
checks the lookup derivation.

The prototype's display signatures equal production dotnet-inspect 0.27 for
every Member compared. Its canonical signatures follow the
`MemberCanonicalSignature` full-name grammar (`System.String`, no nullable
annotations), which production 0.27 does not
([#11033](https://github.com/richlander/dotnet-inspect/issues/11033)). Its name
words use a regular-expression split rather than the CSharpText segmenter.

## Running

```bash
docs/design/models/library-symbol-document/check.sh
```

Pass a produced document as the first argument to check it against the grep
property and the invariants; the recorded answers describe only the excerpt.
Pass `""` and then `--record` to rewrite the excerpt's expectations after a
reviewed shape change.
