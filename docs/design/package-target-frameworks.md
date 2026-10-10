# Package target-framework section

## Authority and claim

`--tfms` is a literal shortcut for `-S "Target Frameworks"`. Both entrances
select the existing Table section, with the same folder-derived population,
TFM-priority ordering, row windows, Count, discovery, and format lowering.
A lone section uses native TSV; explicit Markdown, JSON, and projections follow
[Section Shapes](section-shapes.md) and [Projected JSON](projected-json.md).
The flag can compose with other package sections.

The section includes immediate `lib/` and `tools/` framework folder names,
excluding `tools/any`, de-duplicated case-insensitively. It does not infer
framework support from dependencies or combine compile asset groups. Ref-only
or runtime-only folders are outside this existing section's population.

## Motivation and adoption

System.Text.Json@10.0.12, published on nuget.org, exposes five `lib/` framework
folders. Its old `--tfms` lens and Target Frameworks section listed those same
values through independent renderers and incompatible JSON shapes. The
section is the canonical result; retiring the lens removes that discontinuity.
The CLI-focused continuation was approved by the user on 2026-10-07 after the
wrapper audit merged. One production adoption slice makes the flag select the
existing section and retires the standalone renderer. This changes a CLI
gesture over an existing section, not the browser's section capabilities.

## Execution and compatibility

A lone Target Frameworks table or Count request reads folder names without
binary signal scanning, registry metadata, signature verification, package-info
measurements, or full file inventory. Local packages retain nuspec identity;
Markdown reads nuspec identity for its title. Typed JSON uses normal package
inspection to preserve its full package-object contract. Composed requests use
normal inspection and the same folder collector. Semantic Head,
Tail, and Window operate on priority-ordered rows for either lone entrance;
Count observes the selected rows. Rendered-line clipping remains explicit.
Online native Rows and Count consume optional directory evidence from a
package-wide PackageHouse File List query. Exact pins and version-selected
requests use the existing source authorization and version settlement.
Directory-only acquisition follows the shared
[package cache policy](package-cache-policy.md): archives at or under its size
cut are acquired complete; larger archives use supported ranged access without
selecting entry bodies or extracting the package. This command does not override
the shared size cut. File paths and explicit empty
directory entries both contribute logical directory facts. The normal package
collector and archive collector share ordering and folder projection.

Local/offline, forced-refresh, Markdown, projected output, typed JSON, composed
sections, and tool-wrapper redirection retain their established inspection
paths. Sources that require complete transfer retain that supported fallback;
missing retained directory evidence also uses established inspection.

This remains a reference row-shaping slice: QuerySpace owns semantic selection
after the validated directory is admitted. The complete central directory must
be validated before deriving the population. The ranged directory path needs
no archive-body expansion.

The retired standalone lens derived frameworks from DLL paths under the
preferred tools/ref/lib asset directory. Its bare lines and root JSON array
are replaced by native TSV and the section's established JSON contracts:
plain `--json` preserves the typed package object, while field or column
projections follow Projected JSON. Unsupported
section formats and projections use section diagnostics. Library subject
selection still rejects the flag, as it rejects other package-section sugar.

## Evidence

Real-package parity uses System.Text.Json@10.0.12. Focused CLI gates compare
both entrances across native output, formats, windows, Count, projections,
composed sections, discovery, empty populations, and early invalid selection.
The real-package gate also requires typed JSON to retain Microsoft authorship,
manifest version, package size, and all five framework folders. A renamed local
archive gate requires nuspec identity in JSON and Markdown.
Boundary fixtures include empty framework folders, `tools/any`, mixed casing,
and ref-only paths. Release build, Markdown lint, and exact-head NativeAOT
before/after measurements accompany the production adoption.

## Archive-backed adoption

Issue #9829 tracks one CLI adoption slice over the existing PackageHouse content
query. The user directed this CLI-facing modernization on 2026-10-09 after the
wrapper migration. File List retains its existing file-entry contract and may
also request package-wide logical directories from the same admitted archive
generation. The archive owner constructs those facts; hosts do not parse ZIP
structures or rescan a second archive inventory. Browser-compatible in-memory,
ranged, and desktop filesystem storage preserve the same directory evidence;
this slice adds no browser UI.

System.Text.Json@10.0.12 motivates avoiding acquisition of its unrelated
payload to list its five framework folders. Shared PackageHouse gates cover
explicit empty folders across ranged, complete, and cached content, including
in-memory and filesystem stores. CLI parity, selected versions, strict windows,
typed JSON facts, and exact NativeAOT end-to-end Rows/Count measurements
complete the adoption evidence.
