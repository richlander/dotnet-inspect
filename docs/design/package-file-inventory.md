# Package file inventory

## Scope

This document owns the host-neutral package-file inventory and the semantic
selection and projection exposed by the CLI `Files` section. It does
not own compile-asset selection, PackageHouse selected-slice measurements,
package content interpretation.

The CLI `--files` shortcut selects `Files` through the ordinary section
pipeline. It replaces the separate `--layout` lens. Every projection retains
package-relative path identity; compact directory labels are presentation.

The motivating real asset is `System.Text.Json@10.0.12`: its
`buildTransitive/net8.0/System.Text.Json.targets` and
`lib/net8.0/System.Text.Json.dll` demonstrate that framework selection and
root selection are independent. The supported claim is that selecting Root
`lib` and Target `net8.0` returns only matching lib files, identically
through flags and typed predicates. Durable local archive controls cover
other roots, nested directories, framework-like filenames, and empty matches.

The production consumer is the `package` command. The unified Files
shortcut, predicate vocabulary, and hierarchy adoption are tracked by
[#9634](https://github.com/richlander/dotnet-inspect/issues/9634), one
independently mergeable CLI adoption approved by the user on 2026-10-07.
The shared vocabulary and hierarchy sink remain Browser/Wasm-compatible;
this slice introduces no browser interaction. Issue #7630 adopted the
selection contract through ordinary section output and its existing path
projection. Issue #8484 incrementally moves that command family onto the shared
inspection infrastructure.

## Host-neutral inventory operation

The package-file inventory operation borrows one live
`PackageHouseSettlement.Acquired` for the duration of synchronous package-entry
scanning. The acquired content must expose a pull-based entry scanner with
declared expanded lengths. The operation excludes packaging and restore
plumbing, applies its package-file row QuerySpace, and returns an
`InspectionEnvelope<PackageFileInventoryDocument>`.

The CLI recognizes this route before legacy extraction or package inspection.
Its desktop host composition acquires the package directly through
PackageHouse with an authority-scoped store, executes the inventory while the
settlement is live, and then releases the store and settlement. The route does
not create a `PackageExtractionResult`, parse the nuspec body, construct the
legacy package document, inspect registry metadata or signatures, request a
compile realization, or disclose Package Info measurements that the file-only
command did not request. A possible .NET tool wrapper retains legacy redirect
handling rather than listing the wrapper as the requested package.

The document owns detached path and declared-size values. It does not expose
package content, streams, filesystem paths, leases, generation identities, or
other live House state. Package-wide agent-documentation presence is computed
from the complete validated inventory before row selection, so a selected
window cannot change it. A missing entry manifest, invalid declared length,
duplicate path, unresolved row query, or unavailable semantic window is a
visible unavailable or rejected document with an error diagnostic; it is not
an empty successful inventory.

The first production adoption supports the single-package, exact `Package
files` section without `--path`, discovery,
or `--print`. Its QuerySpace admits Head, Tail, and Window stages and the Rows
and Count terminals. Rows constructs and ordinally sorts detached entries
before selection. Count advances the entry scanner while validating paths, applying predicates, and
deriving package-wide facts, then applies the same semantic stages to the
validated cardinality; it does not sort, retain, or transport detached rows. Filtered Count
evaluates one transient typed entry at a time. The CLI maps Rows into its existing section and shape
projections, clears the rendered-row window after QuerySpace applies it, and
does not apply semantic row selection a second time. Whole-document JSON
remains legacy because its existing contract includes unrelated package
metadata; JSON shape projections and JSONL file rows use the inventory route.
Pre-resolved Workspace packages, local archives, offline acquisition,
multi-package aggregation, package file-family sections, path-filtered inventories,
discovery, content, print, raw, tree, envelope, tool-wrapper redirects, and
other unsupported modes remain on the legacy producer until focused successor slices
adopt their contracts.

## Selected entry set

The inventory begins with every admitted package entry after packaging and
restore plumbing is excluded. Entries retain deterministic ordinal package-path
order.

`--path` selectors and `--tfm <TFM>` are independent predicates over that same
inventory. When both are present, an entry must satisfy both. `--tfm` matches
the requested value case-insensitively as one complete directory segment at any
depth; it does not match a filename, a segment substring, or only known NuGet
roots. `--tfm all` retains the existing unfiltered package behavior.

The target-framework value is validated before package acquisition through the
package target-context validation used by other `--tfm` consumers.

The semantic entry set is shared by Markdown, table, TSV, JSON, JSONL, Count,
row selection, `--paths`, and package-content selection. A host must not rerun
the filter against rendered text.

An `_._` marker is an admitted package entry and remains visible when its path
matches. File inventory reports package layout rather than compile content, so
neither full-path selection nor root projection hides it.

## Typed file predicates and hierarchy

The file owner declares `Path`, `Name`, `Directory`, `Root`, and
`Target` in its shared QuerySpace row vocabulary. Path is the complete
package-relative identity, Name is the final segment, Directory is the exact
parent path, and Root is the first directory segment (empty for root files).
Those four text fields admit case-insensitive `=` and `!=` with `*` and
`?` wildcards. Target admits exact case-insensitive complete directory
segment matching, preserving the existing file TFM predicate; it does not
infer compile assets or match filenames.

Repeatable `--where` terms compose with AND, before Head, Tail, Window,
Count, and projection. `--lib` and `--tools` lower to Root equality;
`--tfm` lowers to Target equality for Files. `--tfm all` adds no predicate.
Root flags require exactly Files for one package and cannot be combined.
Unknown fields, unsupported operators, and incompatible sections fail before
acquisition. A valid filter with no matches succeeds with zero rows.

Directory nodes are context for the selected file rows under
[Section shapes](section-shapes.md#hierarchy). Predicates match typed ancestry
on files, never rendered directory labels. Tree, flat rows, and Count see
the same selected population. The shared hierarchy sink owns streaming
presentation; directories do not become result rows.

## Root projection

`--roots` is a package-specific terminal shape projection over the selected
`Files` rows. It requires exactly that section and is mutually exclusive
with `--value`, `--urls`, `--paths`, `--print`, and `--count`.

Each selected entry beneath a top-level package folder contributes that folder's
first path segment. A root-level file has no folder root and does not contribute
a value. The projection returns the first spelling of each folder root in
deterministic selected-entry order, with case-insensitive de-duplication. This
is a matching-entry root inventory, not a content-bearing-root claim: a root
represented only by a matching `_._` entry remains present.

Plain output emits one root per line. JSON and JSONL use the existing structured
shape-projection rows, retaining row number, section identity, value, and path.
`--row` may select one projected root. The projection does not create synthetic
`PackageFile` rows or invent file sizes.

## Boundary evidence

`PackageFileInventoryInspectionTests` enforces predicate/window/Count
equivalence and package-wide facts through the borrowed archive operation.
`Package_FileQueries_WrappersAndPredicatesSelectSameRows` enforces CLI
shortcut and predicate parity, directory identity, empty matches, and
composed sections. `PackageFileHierarchyPresentationTests` enforces typed
file/context identity, last-sibling disclosure, and destination failure.
The hostile-file-name CLI gate checks containment through the shared sink.


The production-route gates use a package whose `AGENTS.md` entry lies outside
the selected row window and an admitted archive with a malformed nuspec body.
They require package-wide agent-documentation state to remain true, require a
non-first row window to render once, and require file inventory to complete
without entering legacy nuspec/package inspection. A scanner-only Count fixture
requires the operation to pull and dispose the manifest scanner, preserve
package-wide facts, return cardinality, and transport no detached rows.

Contract tests use an independently built local package whose matching TFM
appears under `lib`, `ref`, nested `runtimes`, a custom root, and an `_._`
marker. Neighboring paths prove that TFM-like filenames, segment substrings, and
other frameworks do not match. A real `Microsoft.Data.SqlClient@6.1.0`
invocation demonstrates discovery across ordinary and nested roots without
root-specific selectors.
