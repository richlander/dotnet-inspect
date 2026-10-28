# All-frameworks package discovery

## Status

Proposed under [#9833](https://github.com/richlander/dotnet-inspect/issues/9833),
part of [#9240](https://github.com/richlander/dotnet-inspect/issues/9240).
This document is the normative owner of `--tfm all`. It supersedes the
document-only rule from #3885 and the aggregate Table contract from #9904.

## Authority and exact claim

> `--tfm all` is package discovery in the manner of a package explorer. Only
> the `package` command accepts it. It consumes the package's archive entries,
> its package metadata, and the available-slice inventory that
> [compile selection](package-asset-selection-correspondence.md#authority-and-exact-claim)
> already issues. It selects no target framework and never reads an assembly
> image or a PDB.

Inspection across several frameworks has no single meaningful answer, so this
owner never selects one Library or one slice from several frameworks. A request
that needs one selected framework, or that reads an assembly or a PDB, fails
before acquisition instead.

## Admission

| Request with `--tfm all` | Result |
| --- | --- |
| `package <id>` default request, in every format and terminal | The discovery projection below. |
| `-S Files` | Every admitted entry with no Target predicate, under [Package file inventory](package-file-inventory.md#selected-entry-set). |
| `-S Dependencies` | Every dependency group from the package manifest. |
| `-S "Target Frameworks"` and other sections that read only archive entries or package metadata | Their ordinary results, without running the package-wide binary-signal scan. |
| `-S "Package Info"` | The section without every field that needs a selected framework or reads an assembly: `Selected TFM`, `Selected-TFM Status`, the selected-TFM folder, library count, and size measurements, `Ecosystem Dependencies`, `Ecosystem Dependency Status`, and `binary_signals` with every other member derived from the binary-signal scan. |
| `--library`, `-S "Dependency Hierarchy"`, and sections whose producers need a selected framework or read an assembly or a PDB, such as SourceLink, Signals, and Ecosystem Dependencies | Fails before acquisition and names the section or flag. |
| A category whose expansion contains a failing member | Fails; the category does not drop members. |
| `package query` | Fails before acquisition, because it evaluates assemblies. |
| `library`, `type`, `member`, `find`, `extensions`, `diff`, `depends`, `workspace`, or any other command with a package target | Fails before acquisition and names `package <id> --tfm all`. |
| Any command with a non-package target, such as `--platform`, a local file, or a project | Fails before acquisition and states that `--tfm all` applies only to `package`. |

The `--tfm all` default request returns a discovery projection that this owner
defines; it is not the Package children document of
[Package library scope](package-library-scope.md), whose population stays the
one selected compile projection. The projection is a Hierarchy under
[Section shapes](section-shapes.md#hierarchy):

- Each row is one candidate asset with `framework`, `role` (`ref`, `lib`, or
  tool), and package-relative `path` cells.
- Rows group under one framework context node per available slice, ordered by
  `TfmResolver` framework priority, highest first, then by framework name, as
  compile selection orders its inventory. Every candidate in a slice is a
  row, whatever markers the slice also has. An explicit empty-group marker is
  itself a row with role `ref` and path `ref/<tfm>/_._` in the inventory's
  framework spelling, so a marker-only framework stays visible in every
  format and in Count.
- A tool package groups its tool slices, as
  [Package Info tool measurements](package-info-tool-measurements.md) defines
  them, the same way. A tool slice without a Library is itself a row with role
  tool and its slice directory path, so it stays visible in every format.
- An empty compile and tool inventory returns the typed no-Library state with
  zero rows. A package without compile assets is not searched for other
  assemblies.

The Tree is its native presentation. Explicit `--tree`, JSON, the service
envelope, TSV, JSONL, and table output lower the same rows; Count counts them
and `--rows` windows them. Every row carries its own framework. A row whose
path is in that framework's selected compile projection, or a tool Library row,
is followed with `package <id> --tfm <framework> --library <path>`. Marker
rows, tool-slice rows without a Library, and candidates that a real `ref` asset
shadows or a co-located marker suppresses are listed for discovery only; no row
implies a `--library` path that its own framework's selection would reject.

The projection's producer sits beside the Package children producer and
declares its own QuerySpace capabilities. Inspect Web has no `--tfm all`
gesture, so the projection has no Browser/Wasm consumer.

Failures are ordinary visible errors with exit code 1. No route treats `all`
as a framework spelling or falls back to the highest framework.

## Non-claims

This owner does not change compile selection or its available-slice inventory;
it consumes the inventory and orders only its own projection. Which assets are
Libraries when a package has no compile assets stays with #9833 and [Package
library scope](package-library-scope.md); under `--tfm all` such a package
shows the empty state above. It does not change `--tfm <TFM>` for a single
framework.

## Adoption

The first CLI adoption slice admits `--tfm all` only on `package`: one shared
check fails every other command and non-package target, including
`package query`, before acquisition, and both `package --library` routes fail
before acquisition. It removes the IL-reading `library --tfm all` paths,
replaces their gates with rejection gates, and retires the `--tfm all`
descriptions in the documents that described them.

The remaining CLI behavior does not yet meet this contract. Observed on main:

- `package <id> --tfm all` shows only the selected framework's Library
  hierarchy; a tool package reports no applicable Library target.
- `-S @SourceLink` and `-S Signals` read assemblies and PDBs across every
  framework.
- `-S "Package Info"` keeps its selected-framework fields and Ecosystem
  Dependencies, identical to the request without `--tfm all`; the tool gate
  `PackageCommand_DeclaredToolUsesAggregateToolMeasurementsColdAndWarm`
  asserts those fields under `--tfm all`.
- `-S Dependencies` filters dependency groups by the literal `all` and returns
  no rows.
- Every package request except the default children projection runs the
  package-wide binary-signal scan (`PackageInspector.InspectAsync` with
  `scanBinarySignals`), opening every DLL and PDB, and Package Info JSON emits
  its `binary_signals` result.
- The default request's JSON, row formats, and Count report the selected
  framework's children document.
- `-S "Dependency Hierarchy"` fails with an internal exception.

The remaining adoption under #9833 updates that tool gate's selected-TFM
assertions, classifies every `package` section against this table, skips the
binary-signal scan under `--tfm all`, adds the discovery projection with its
QuerySpace producer capabilities for Count and row windows, and gates each row.
It also updates the default-request format statements in
[CLI reference](../cli-reference.md) and
[Primary subject views](primary-subject-views.md).
