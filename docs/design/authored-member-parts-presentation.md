# Authored member parts presentation

## Owner and claim

Sections owns the shared presentation projection for
[#7718](https://github.com/richlander/dotnet-inspect/issues/7718).

> Present a completed SourceHouse member document through its issued part
> catalog, preserving the distinction between PDB locations and lexical
> ranges, and select only the original text named by those ranges.

[SourceHouse](source-house.md#authored-member-parts) supplies the verified
document, mapping, parts, and settlement. [CSharpText](member-text-parts.md)
owns the ranges. This projection changes neither acquisition nor lexical
association and does not strengthen physical-declaration provenance.

The shared catalog names `member`, `xml-docs`, `attributes`, `signature`, and
`body`. Absent optional parts are omitted. The complete member includes its
attached documentation and attributes. A selected part preserves each original
text fragment; multiple discontiguous fragments are joined with LF for display
or copying, without normalization within a fragment.

## Consumer lowering

This is the third and final planned delivery for #7718, following lexical
parts in #7726 and shared settlement in #7728. CLI and Browser use the same
completed `MemberSourceInspection` operation and this catalog.

The CLI's focused Source Locations JSON separates `member`, `document`, and
`pdb_span`. Default locations acquire no source text. Explicit `--source-parts`
adds lexical `parts`; `--print --part` selects one available part. Bare
`--print` retains whole-document meaning. The new member-source JSON omits
generic result-row/section bookkeeping; other print payloads are unchanged.
Display ranges use one-based inclusive lines, while selection uses native
UTF-16 spans. Discontiguous parts preserve their individual fragment ranges.

The CLI uses Markout for human-readable part catalogs and typed JSON for the
focused nested document. This is a deliberate JSON lowering rather than
flattening document/part structure into a table cell. Existing metadata-only
tables and their projections remain unchanged.

Discovery accepts Markdown, plaintext, or complete JSON; incompatible table,
scalar, graph, and row-window projections fail visibly. Part printing also
supports JSONL and a JSON array. `--row` chooses a member before part selection;
it does not choose a part. JSON retains the selected text characters. Rendered
CLI text uses the existing LF normalization and inert-text containment policy,
rather than promising a byte-for-byte file transfer.

Browser lowers the catalog into its existing Source viewer. Its wire spans
address the returned full-member text, mechanically rebased from the original
document. Selection and Copy consume the same selected text without another
acquisition. Decompiled fallback remains ordinary Source, with no authored
part catalog. This host deliberately reuses its escaped DOM code viewer rather
than Markout: the structured catalog reaches the viewer boundary, and selection
changes an existing code display and Copy action rather than generating a new
multi-format document. Type Source's existing flat wire contract is unchanged.

## Failure and evidence

Missing or ambiguous parts and failed source verification remain visible
unavailable outcomes, not empty text or decompiler substitutes. A consumer may
retain ordinary Source's explicit best-available policy, but cannot label a
fallback as an authored part.

The motivating asset is `richlander/dotnet-inspect` at
`bffd209a896d0380193e8d5f0f3a8beac3770d0f`,
`src/CSharpText.MemberSlicing/MemberTextSlicer.cs`, including its XML-documented
`ExtractMemberText` declaration. Focused Release CLI and Browser cases gate the
metadata-only default, shared completed acquisition, exact selection, absent
parts, checksum failure, and neighboring whole-document/decompiled behavior.
Frontend cases gate selected rendering, Copy, and stale-result handling.
