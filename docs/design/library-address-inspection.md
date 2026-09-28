# Library Address inspection

## Status and authority

This document is the focused owner for host-neutral physical-address
inspection over one exact realized Library. The work is tracked by
[#8688](https://github.com/richlander/dotnet-inspect/issues/8688) as the
second focused slice of
[#8672](https://github.com/richlander/dotnet-inspect/issues/8672).

The owning claim is:

> Library Address inspection executes one typed physical-address request over
> the implementation assembly of one exact realized Library. It consumes a
> matching transferred Library operation lease, composes owner-issued IL or
> metadata evidence, returns only detached results, and settles the lease
> exactly once.

This owner composes, but does not redefine:

- [IL coordinate workflows](il-coordinate-workflows.md) and
  `ILInspector.Research` for MethodDef-token plus IL-offset evidence;
- [Metadata table projection](metadata-table-projection.md) and
  `ILInspector.Metadata` for metadata-root and heap-address meaning;
- [Address child command](coordinate-child-command.md) for CLI grammar,
  exact/file modes, discovery, and presentation behavior; and
- `DotnetInspector.Libraries` for Library identity, implementation
  correspondence, retained companions, and lease ownership.

## Operation boundary

The request pairs one `LibraryReference` with portable address intent and
finite content limits. Execution requires a `LibraryOperationLease` issued for
that exact reference. A mismatch is a typed rejection. The operation owns the
transferred lease and disposes it on success, rejection, failure,
cancellation, and invalid-request paths.

Physical addresses target `LibraryReference.ImplementationAssembly`. The API
assembly may be a distinct reference contract and is never silently
substituted. A Library without implementation content receives a typed
`MissingImplementationAssembly` rejection.

The operation snapshots only the retained implementation bytes and, when
source evidence is requested, its one associated Portable PDB companion.
Assembly and PDB copies are bounded before allocation. More than one associated
Portable PDB is ambiguous and rejected. A supplied companion must pass
Metadata's identity correspondence check. Without a companion, an embedded
Portable PDB may contribute within the same limit.

The operation does not reopen an acquisition path, probe adjacent files, fetch
symbols, or perform network work. Those are upstream realization concerns.

## Portable intent

Exact IL intent carries a MethodDef token, non-negative IL offset, and
`ILOffsetProjectionCapabilities`. It carries no CLI section names.
`ILInspector.Research` remains authoritative for member, instruction,
exception, callsite, return-address, allocation, safety, cost, and source
evidence and for typed projection failures.

Exact heap intent carries `MetadataRootKind`, `HeapKind`, and the root-local
address. Metadata remains authoritative for root selection, root identity,
heap addressing, decoded `MetadataValue`, unsupported Windows Metadata, and
malformed-root behavior. An absent ReadyToRun manifest root never falls back to
the CLI root.

Population intent carries already-admitted records rather than a file path.
Each record is either one valid IL point or one retained malformed-input
observation. Construction snapshots the records, requires at least one, and
rejects more than 1,024. Execution preserves order and emits one row per
record. It does not apply CLI row selection.

File reading, lexical parsing, structural/effective discovery, section
selection, row windows, rendering, and browser-URL preference remain host
concerns.

## Outcomes

`LibraryAddressInspectionOperation.Execute` returns
`InspectionEnvelope<LibraryAddressInspectionOutcome>`.

- `Completed` contains an exact IL, exact heap, or wholly resolved population
  document.
- `Partial` contains a population document with at least one malformed or
  unresolved row while retaining every useful row in source order.
- An unresolved exact point remains typed content with an error diagnostic.
- Population documents preserve valid rows, malformed-input rows, and
  resolution-failure rows together. `IsComplete` is false when any row is not
  resolved.
- `Rejected` records authority or correspondence failures.
- `Incomplete` records assembly, companion-PDB, or embedded-PDB byte limits.
- `Failed` records content-access, format, inspection, or cleanup failures.
  Truncated, malformed, or unsupported companion-PDB content is a format
  failure; only a decoded Portable PDB whose identity does not match the
  implementation is a correspondence rejection.
  PE debug-directory and CodeView structural limits are inspection failures,
  not embedded-PDB limits.

Every result is detached from the Library, Artifact, PE, PDB, and stream
lifetimes. Until a complete portable Workspace scenario is supplied, Share is
`NonProjectable` at `library-address-inspection/share`.

## Real and pathological evidence

`System.Text.Json` 10.0.0 is the real package scenario. Its
`lib/net10.0/System.Text.Json.dll` implementation supplies exact IL and CLI
metadata-heap evidence.

Release tests cover:

- exact IL and heap results over the real implementation;
- distinct API and implementation contents;
- absent ReadyToRun manifest metadata without CLI-root fallback, malformed CLI
  metadata, and an out-of-range heap address that remains a malformed value;
- a retained Portable PDB contributing source evidence without path or
  network acquisition;
- lease/reference mismatch, missing implementation, and identity mismatch;
- mixed valid, malformed, and unresolved population rows in source order;
- the 1,024-record boundary and rejection of record 1,025;
- cancellation and invalid-request lease settlement; and
- detached content remaining readable after Library retirement.

PackageHouse composition, CLI and Inspect Web adoption, discovery projection,
and retirement of the legacy package-to-Library continuation remain later
slices of #8672.
