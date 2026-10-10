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
| `package <id>` default Library hierarchy | Every available slice in compile selection's inventory order, each with its `ref` and `lib` candidates by path and its explicit empty-group marker. A tool package shows every tool slice by path. |
| `-S Files` | Every admitted entry with no Target predicate, under [Package file inventory](package-file-inventory.md#selected-entry-set). |
| `-S Dependencies` | Every dependency group from the package manifest. |
| `-S "Target Frameworks"` and other manifest-only sections | Unchanged. |
| `-S "Package Info"` | The section without its selected-framework fields: `Selected TFM` and the selected-TFM folder, library count, and size measurements. |
| `--library`, `-S "Dependency Hierarchy"`, and sections whose producers read an assembly or a PDB, such as SourceLink, Signals, and Ecosystem Dependencies | Fails before acquisition and names the section or flag. |
| A category whose expansion contains a failing member | Fails; the category does not drop members. |
| `library`, `type`, `member`, `find`, `extensions`, `diff`, `depends`, or any other command with a package target | Fails before acquisition and names `package <id> --tfm all`. |

Failures are ordinary visible errors with exit code 1. No route treats `all`
as a framework spelling or falls back to the highest framework.

## Non-claims

This owner does not change compile selection, its slice order, or its
available-slice inventory; it consumes them. It does not define which assets
are Libraries when a package has no compile assets; #9833 and
[Package library scope](package-library-scope.md) own that. It does not change
`--tfm <TFM>` for a single framework.

## Adoption

The CLI does not yet meet this contract. Observed on main:

- `package <id> --tfm all` shows only the selected framework's Library
  hierarchy; a tool package reports no applicable Library target.
- `package <id> --library --tfm all`, `library [L] --package <id> --tfm all`,
  `find`, `extensions`, and `diff` read every DLL in the archive, including
  `build/` and `tools/` assets.
- `-S @SourceLink` and `-S Signals` read assemblies and PDBs across every
  framework.
- `-S Dependencies` filters dependency groups by the literal `all` and returns
  no rows.
- `-S "Dependency Hierarchy"` fails with an internal exception.
- `type --package <id> --tfm all` fails after acquisition.
- `library` and `type` recognize `all` at separate sites, and other commands
  accept it.

The adoption under #9833 adds one shared pre-acquisition admission check,
classifies every `package` section against this table, and gates each row. It
also retires the IL-reading `--tfm all` code paths and the current-mechanics
descriptions in [Integrations](integrations.md), [Inspection
space](../inspection-space.md), [Assembly inspection
query](assembly-inspection-query.md), and [CLI row
selection](cli-row-selection.md).
