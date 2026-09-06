# ReadyToRun browser projection

## Status and owner

This document owns Inspect Web's ReadyToRun presentation and metadata-root
selection. It is slice 4 of
[#5835](https://github.com/richlander/dotnet-inspect/issues/5835).

The browser consumes two host-neutral owners:

- [ReadyToRun image projection](readytorun-image-projection.md) supplies the
  validated PE-envelope role, advertisement, header, flag, section, and
  manifest facts; and
- [raw metadata-table projection](metadata-table-projection.md) supplies
  captured root-scoped image, table, row, reference, and heap operations with
  requested and canonical root identity.

This owner does not parse PE bytes, identify ReadyToRun sections from numeric
values, decode metadata, or infer root identity from display text.

## Claim

For one selected library, the managed facade projects:

- the independently available CLI and ReadyToRun manifest metadata roots;
- each root's requested and canonical identity, image overview, tables, and
  heaps;
- the validated ReadyToRun image overview and complete section directory; and
- root-specific and ReadyToRun-specific failures without suppressing healthy
  neighboring facts.

Package Metadata selects one available root for its image, table, and heap
summary. Metadata Explorer carries that same root in its state and supplies it
to every table-window and heap-listing request. A manifest selection never
falls back to CLI metadata.

The default remains the CLI root when it is available. If an image has no CLI
root but has a valid manifest root, Package Metadata selects the manifest
rather than presenting the artifact as metadata-free.

## Managed facade

The browser facade consumes public assembly-context queries. It does not open
an inspection session, retained image, `PEReader`, or `MetadataReader`.

One assembly response contains zero or more typed metadata-root overviews. A
root overview carries:

- requested root;
- canonical root when the producer assigns an image-relative identity;
- root RVA and size;
- whether a manifest request aliases CLI metadata;
- metadata format, image kind, manifest presence, heaps, and populated tables;
  and
- the containing image's PE and CLI header facts.

The ReadyToRun response carries role, advertisements, exact version, raw and
named flags, header locations, manifest relationship, and every section entry
with its numeric identity. TypeScript renders those fields but does not
reclassify the image or discover the manifest section.

The facade may perform the CLI-root, manifest-root, and ReadyToRun queries
independently against the same immutable workspace participant. This preserves
partial success: malformed ReadyToRun structure does not erase a readable CLI
root, and malformed CLI metadata does not erase an independently readable
manifest root.

## Package Metadata

Package Metadata keeps the existing full-height working surface and explicit
per-assembly **Explore** action. When more than one metadata root is available,
a compact Root selector chooses the displayed root. The selected root controls:

- format and root provenance;
- heap summaries and heap Explore actions;
- populated table groups and table Explore actions; and
- the root handed to the Metadata Explorer.

The ReadyToRun area is independent from root selection. It reports:

- `No` when the image has no canonical ReadyToRun advertisement;
- role, version, advertisements, flags, header extent, and manifest
  relationship when available;
- the validated section directory; or
- a visible typed failure when advertised structure is malformed.

An absent manifest does not create a disabled root or an error panel. A
malformed manifest is reported beside the healthy CLI root and is not offered
as an available root. When a manifest aliases CLI metadata, the selector keeps
the requested manifest identity visible while the root facts identify CLI as
the canonical root.

## Metadata Explorer

The explorer title identifies the requested root and, for an alias, its
canonical CLI identity. Its table directory, heap directory, row windows,
history, references, and heap listings all remain local to that root.

Changing the root closes any open explorer. This prevents table indices, row
ids, heap offsets, cached windows, or navigation history from crossing root
boundaries.

Every managed table-window and heap-listing export requires an explicit root
argument. The facade rejects unknown root spellings before running a query.

## Rendering strategy

This surface deliberately uses host-specific HTML rather than Markout. Package
Metadata and Metadata Explorer are interactive browser surfaces with root
selection, lazy table and heap loading, spatial navigation, focus history, and
responsive DOM composition. Their wire inputs remain typed, presentation-free
records; the browser does not reconstruct product semantics from formatted
text.

The CLI continues to use the Markout-backed
[ReadyToRun CLI projection](readytorun-cli-projection.md). Both hosts consume
the same Metadata and Queries-owned facts while retaining their distinct
interaction and rendering responsibilities.

## Failure and absence

- No ReadyToRun advertisement produces the explicit Package Metadata fact
  `ReadyToRun: No`.
- No section 112 omits the manifest root without reporting a failure.
- A malformed ReadyToRun advertisement or section directory produces a
  ReadyToRun failure while a healthy CLI root remains usable.
- A malformed manifest root produces a manifest-root failure while a healthy
  CLI root remains usable.
- A selected root's table or heap failure remains visible in Metadata Explorer
  and never retries against another root.
- Acquisition failure remains a library-level failure rather than successful
  emptiness.

## Gates

Release gates:

- `BrowserMetadataOperationsTests` covers managed projection of ReadyToRun
  facts, CLI and manifest root provenance, absence, partial failure, and
  selected-root table and heap operations.
- `metadata-viewer.test.ts` covers root selection, ReadyToRun overview and
  section rendering, missing and malformed manifest behavior, escaping, and
  explorer root disclosure.
- `metadata-inspection.test.ts` covers preservation of the selected root across
  table and heap requests.
- generated-facade checks keep the TypeScript declaration and runtime bridge
  aligned with the managed exports.

The existing Metadata and Queries suites remain the enforcing gates for
ReadyToRun discovery, root capture, alias identity, malformed-input handling,
and root-local table and heap semantics.

## Non-goals

This slice does not:

- decode ReadyToRun native methods, imports, fixups, GC information, or section
  payloads other than manifest metadata through the existing metadata owner;
- add TypeScript PE or metadata parsing;
- make ReadyToRun a separate persistent navigation subject or lens;
- persist metadata-root selection in Workspace packets or browser URLs; or
- change metadata projection budgets, table coverage, or heap completeness.
