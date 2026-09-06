# ReadyToRun CLI projection

## Status and owner

This document owns the `library` command's ReadyToRun presentation and explicit
metadata-root selection. It is tracked by
[#6066](https://github.com/richlander/dotnet-inspect/issues/6066), slice 3 of
[#5835](https://github.com/richlander/dotnet-inspect/issues/5835).

The CLI consumes two host-neutral owners:

- [ReadyToRun image projection](readytorun-image-projection.md) supplies
  validated PE-envelope facts; and
- [metadata table projection](metadata-table-projection.md) supplies captured,
  root-scoped ECMA-335 image, table, row, reference, and heap operations.

This owner does not parse PE bytes, reinterpret ReadyToRun sections, decode
native code, or redefine metadata-root identity.

## Claim

The `library` command exposes two independent explicit gestures:

1. `-S @ReadyToRun` selects the `ReadyToRun: Image` and
   `ReadyToRun: Sections` views of the validated ReadyToRun envelope.
2. `--metadata-root cli|r2r-manifest` selects the root used by every existing
   `@Metadata` image, table, and heap operation.

The default metadata root remains `cli`. Selecting `r2r-manifest` never falls
back to CLI metadata: an absent manifest is an error, and malformed ReadyToRun
or metadata-root structure remains a visible typed-query failure.

The Release gates are the ReadyToRun and metadata-lens cases in
`dotnet-inspect.Tests`. They cover explicit disclosure, image and section
facts, selected-root provenance, table and heap operations from the selected
root, missing- and malformed-root failure, and unchanged default suppression.

## User gestures

`@ReadyToRun` is a domain category, not part of the library base view. Its
members are:

- `ReadyToRun: Image`, a fixed one-row summary containing role, discovery
  evidence, exact version, raw-preserving flags, header location and size, and
  manifest relationship; and
- `ReadyToRun: Sections`, one row per validated section-directory entry,
  including numeric type identity, RVA, size, and CLI-metadata aliasing.

Both sections are explicit-only and network-free. No verbosity level renders
them automatically. The category is listed by discovery, while its member
sections remain behind the category door.

`--metadata-root` is a subject selector before the output-shape ladder. It
does not create another metadata section family or encode a root into section
names. In render mode, a non-default root requires selection of `@Metadata` or
at least one `Metadata: ...` section. In discovery mode it requires effective
metadata discovery and an input artifact, because structural discovery has no
image whose roots can be selected.

The accepted spellings are:

```text
--metadata-root cli
--metadata-root r2r-manifest
```

The selected root applies uniformly to `Metadata: Image`, table sections, heap
listings, and the coordinate-scoped `Metadata: Heap` section. Tokens, row ids,
handles, and heap offsets are therefore always interpreted in one root.

## Root provenance

The metadata image section adds root facts when the selected root has a PE/RVA
identity:

- requested root;
- canonical root;
- root RVA;
- root size; and
- for an R2R manifest request, whether it is a separate root or aliases CLI
  metadata.

Requested provenance and canonical identity remain separate. An exact
manifest/CLI alias therefore reports `ReadyToRun manifest` as the request and
`CLI` as the canonical root instead of presenting duplicate physical roots.

COFF-only metadata keeps its existing source-less projection. Because the
metadata owner deliberately assigns it no synthetic PE/RVA root identity, the
CLI does not invent root coordinates for that case.

## Typed query and rendering composition

`ReadyToRunImageQuery` returns available, absent, or failed outcomes from one
shared `AssemblyInspectionSession`. The available result is retained on
`LibraryInspection` and lowered by `LibraryInspectionView` into static Markout
row types. Markdown, table, TSV, JSONL, row-window, column, and count behavior
therefore use the ordinary section pipeline.

`MetadataImageQuery` accepts the requested root. For a PE CLI root or an R2R
manifest root, its available result retains the captured
`MetadataRootInspection` alongside the overview. Metadata tables and heaps use
that same captured root rather than reopening the artifact or selecting a root
again. COFF-only CLI metadata retains the established source-less path.

The existing metadata renderer remains the format-lowering owner for dynamic
ECMA-335 table schemas. It accepts optional root provenance for
`Metadata: Image`; the standalone `mdi` shape and callers without a selected
root remain unchanged.

## Failure and absence

- No ReadyToRun advertisement makes `@ReadyToRun` inapplicable.
- A malformed advertisement or section directory is a failed ReadyToRun query,
  not an absent result.
- No section 112 after an explicit `r2r-manifest` request is a command error.
- A malformed manifest is a failed metadata query and never falls back to the
  CLI root.
- A heap coordinate is resolved against the selected root; an invalid address
  remains a caller-input error.

Unrelated library sections remain independently inspectable when ReadyToRun
inspection fails. Selecting the failed ReadyToRun or metadata section produces
a nonzero exit through the existing selected-inspection-failure path.

## Non-goals

This slice does not:

- decode ReadyToRun native methods, imports, fixups, GC information, or section
  payloads other than manifest metadata through the existing metadata owner;
- add ReadyToRun facts to default library verbosity;
- add root-qualified duplicates of metadata sections;
- change raw metadata projection budgets or failure shapes; or
- expose the capability in Inspect Web. Browser/Wasm adoption remains #5835
  slice 4.
