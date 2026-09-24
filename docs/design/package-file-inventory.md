# Package file inventory

## Scope

This document owns the host-neutral package-file inventory and the semantic
selection and projection exposed by the CLI `Package files` section. It does
not own compile-asset selection, PackageHouse selected-slice measurements,
package content interpretation, or the `package --layout` lens.

`package --layout --tfm <TFM>` retains its existing layout-specific scope:
`lib/<TFM>` when present, otherwise `tools/<TFM>`. It does not adopt this
document's cross-root TFM predicate. The layout lens answers a scoped tree
question; `Package files --tfm <TFM>` answers the cross-root inventory question.

The production consumer is the `package` command. Issue #7630 adopted the
selection contract through ordinary section output and its existing path
projection. Issue #8484 incrementally moves that command family onto the shared
inspection infrastructure.

## Host-neutral inventory operation

The package-file inventory operation borrows one live
`PackageHouseSettlement.Acquired` for the duration of synchronous package-entry
enumeration. The acquired content must expose an entry manifest with declared
expanded lengths. The operation excludes packaging and restore plumbing, orders
the remaining paths ordinally, applies its package-file row QuerySpace, and
returns an `InspectionEnvelope<PackageFileInventoryDocument>`.

The CLI requests an acquire-only House settlement for this route. Retaining the
settlement must not manufacture a compile realization or disclose Package Info
measurements that the file-only command did not request.

The document owns detached path and declared-size values. It does not expose
package content, streams, filesystem paths, leases, generation identities, or
other live House state. A missing entry manifest, invalid declared length,
duplicate path, unresolved row query, or unavailable semantic window is a
visible unavailable or rejected document with an error diagnostic; it is not
an empty successful inventory.

The first production adoption supports the single-package, exact `Package
files` section without `--path`, a target-framework file predicate, discovery,
or `--print`. Its QuerySpace admits Head, Tail, and Window stages and the Rows
and Count terminals. Rows returns the selected detached entries; Count applies
the same semantic stages to the validated inventory cardinality without
retaining rows in the result. The CLI maps Rows into its existing section and
shape projections, and does not apply semantic row selection a second time.
Local archives, multi-package aggregation, package file-family sections,
filtered inventories, discovery, content, print, and layout modes remain on the
legacy producer until focused successor slices adopt their contracts.

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

## Root projection

`--roots` is a package-specific terminal shape projection over the selected
`Package files` rows. It requires exactly that section and is mutually exclusive
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

Contract tests use an independently built local package whose matching TFM
appears under `lib`, `ref`, nested `runtimes`, a custom root, and an `_._`
marker. Neighboring paths prove that TFM-like filenames, segment substrings, and
other frameworks do not match. A real `Microsoft.Data.SqlClient@6.1.0`
invocation demonstrates discovery across ordinary and nested roots without
root-specific selectors.
