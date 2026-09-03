# Member source presentation

This document owns how the `dotnet-inspect` CLI exposes one Research-issued
Finding census across member Facts and Annotated Source output. It is the
Finding-identity slice of
[issue #4718](https://github.com/richlander/dotnet-inspect/issues/4718), under
the end-to-end tracker
[issue #5515](https://github.com/richlander/dotnet-inspect/issues/5515).

The CLI consumes the receipt, instance keys, Facts rows, portable document,
and document-local fact sidecar owned by
[Research Finding census projection](research-finding-census-projection.md).
It does not reconstruct identity from text, offsets, row order, or document
fact ids.

## Contract

The CLI exposes Finding identity through two explicit member surfaces:

- **Facts** remains a row section. Body rows add `Census Receipt` and
  `Instance Key` columns. Member-header rows leave both columns absent because
  they are outside the body census.
- **Finding Census** is one indivisible correlation envelope. Selecting it
  requests Facts and the Annotated Source document from one Research member
  operation and emits the operation receipt, raw Research Facts, the portable
  document, and the document-local fact-to-instance sidecar.

`Finding Census` is explicit-only and requires one selected body-backed member.
It does not enter a verbosity preset or category. Requesting it authorizes the
same body projection and PDB-resolution capability as requesting Annotated
Source Document. Category and broad wildcard expansion omit it; a non-exact
selector that resolves only to Finding Census fails and requires its exact
section name.

The envelope uses this CLI-owned wire shape:

```json
{
  "fact_census_receipt": "6f9619ff-8b86-d011-b42d-00c04fc964ff",
  "facts": [
    {
      "member": "Example.Type::Method",
      "il_offset": 12,
      "csharp_line": 4,
      "anchor": "offset",
      "category": "Allocation",
      "id": "alloc.box",
      "detail": "boxed System.Int32",
      "conditionality": "Always",
      "instance_key": 1
    }
  ],
  "annotated_source_document": {
    "text": "..."
  },
  "source_fact_instances": [
    {
      "fact_id": 0,
      "instance_key": 1
    }
  ]
}
```

The receipt is the canonical `D` string supplied by
`FindingCensusReceipt.ToString()`. Instance keys, offsets, lines, and
document-local fact ids are JSON numbers. The envelope-level receipt scopes
every key, so rows inside the envelope do not repeat it.

`annotated_source_document` is serialized with the Decompiler-owned
`AnnotatedSourceDocumentJsonContext`. The CLI owns only the outer envelope and
does not rename, reshape, or independently serialize the nested document.
Exact singleton `Annotated Source Document --json` retains its existing
document-only shape.

### Format behavior

The Facts table retains its existing table semantics in Markdown, table, TSV,
and JSONL output. Its two identity columns are an intentional schema addition.

Finding Census is a non-tabular document payload:

- Markdown renders the indented envelope in a JSON code fence.
- Exact singleton `--json` renders the typed envelope directly; `--compact`
  selects compact JSON.
- table, TSV, JSONL, count, row-window, field, and column projections fail
  explicitly because they cannot preserve the indivisible correlation
  envelope.
- composing Finding Census with another exact section under `--json` fails
  rather than choosing one payload or changing the envelope root.

## Admission and failure

The CLI validates the lowered result before presenting it:

- the result receipt is non-default;
- each body Facts row carries that receipt and a positive unique key;
- each member-header row carries neither receipt nor key;
- each sidecar entry carries that receipt, a positive unique key, and a unique
  document-local fact id;
- every sidecar fact id names a body fact in the portable document;
- no member-header document fact has a sidecar entry; and
- the body key set in Facts equals the body key set in the sidecar.

A successful empty body census still emits its non-default receipt. It has no
keyed body rows and an empty sidecar; member-header rows may remain present and
unkeyed.

Missing output, a default or mismatched identity, an invalid association, or
Annotated Source document failure makes Finding Census fail atomically with a
nonzero exit and empty standard output. The CLI does not emit a partial
envelope or fall back to shape-derived matching.

## Composition boundaries

This owner defines:

- CLI section names, disclosure, columns, field names, and format behavior;
- the outer correlation-envelope shape;
- CLI admission of the lowered Research result; and
- CLI diagnostics for an unavailable envelope.

Adjacent owners remain separate:

- [Finding instance census](finding-instance-census.md) owns receipt and key
  construction and typed census validation;
- [Research Finding census projection](research-finding-census-projection.md)
  owns the one-operation Facts/document association and sidecar;
- [Member body substrate](member-body-substrate.md) owns the portable document
  and its serialization;
- [Output shapes](output-shapes.md) owns the Document-to-Scalar ladder; and
- Inspect Web transport and interaction remain
  [issues #5516](https://github.com/richlander/dotnet-inspect/issues/5516) and
  [#5517](https://github.com/richlander/dotnet-inspect/issues/5517).

The broader success, absence, failure, diagnostic, and legacy-null migration
tracked by #4718 is not defined by this Finding-identity slice.

## Evidence

Release gates in `src/dotnet-inspect.Tests` verify:

- one Finding Census invocation exposes one receipt across Facts and the
  Annotated Source sidecar;
- display-identical Findings retain separate instance keys;
- a successful empty body census retains its receipt;
- member-header facts remain unkeyed;
- Facts Markdown and TSV expose the added identity columns;
- Finding Census JSON preserves the Decompiler-owned nested document shape;
- invalid or unavailable associations fail without partial output; and
- exact singleton Annotated Source Document JSON remains unchanged.

The contract is a finite projection and validation of one immutable result.
It has no replacement, concurrency, or scheduling lifecycle, so executable
Release gates are the appropriate oracle; no TLA+ model is required.

## Non-claims

This design does not:

- mint, parse, or rehydrate Finding identity;
- define correspondence across executions or versions;
- add Finding identity to `AnnotatedSourceDocument`;
- define browser transport, selection, modal, history, or packet behavior;
- adopt member-header facts into the body census; or
- complete the remaining typed member-projection outcome work in #4718.
