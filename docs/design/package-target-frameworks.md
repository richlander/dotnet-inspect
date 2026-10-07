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

A lone Target Frameworks request reads folder names without binary signal
scanning, registry metadata, or other package inspection. Composed requests
use normal package inspection and the same folder collector. Semantic Head,
Tail, and Window operate on priority-ordered rows for either lone entrance;
Count observes the selected rows. Rendered-line clipping remains explicit.
This is a reference slice over the existing extracted package acquisition;
archive manifest pushdown is deferred rather than claimed here.

The retired standalone lens derived frameworks from DLL paths under the
preferred tools/ref/lib asset directory. Its bare lines and root JSON array
are replaced by the section's native TSV and projected JSON root. Unsupported
section formats and projections use section diagnostics. Library subject
selection still rejects the flag, as it rejects other package-section sugar.

## Evidence

Real-package parity uses System.Text.Json@10.0.12. Focused CLI gates compare
both entrances across native output, formats, windows, Count, projections,
composed sections, discovery, empty populations, and early invalid selection.
Boundary fixtures include empty framework folders, `tools/any`, mixed casing,
and ref-only paths. Release build, Markdown lint, and exact-head NativeAOT
before/after measurements accompany the production adoption.
