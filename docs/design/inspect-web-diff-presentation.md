# Inspect Web diff presentation

## Status and ownership

This document defines the Inspect Web-owned structured source-diff presentation
for [#5528](https://github.com/richlander/dotnet-inspect/issues/5528).

The normative claim is:

> Inspect Web presents product-produced source comparisons from typed
> `AnalysisDiff<string>` statistics and the shared Markout `MappedTextDiff`
> lowering without parsing rendered text or implementing source
> correspondence in the browser.

This is a focused browser-consumer design. It consumes:

- source acquisition and decompilation from `DotnetInspector.Queries`;
- line correspondence and classification from `ILInspector.Text`;
- host-neutral text-diff lowering from `DotnetInspector.Presentation`;
- `AnalysisDiff<T>` semantics from
  [Analysis diff](analysis-diff.md);
- multi-subject composition, where applicable, from
  [Comparison document](comparison-document.md);
- source provenance and action semantics from
  [Inspect Web presentation language](inspect-web-presentation-language.md);
- worker operation ownership from
  [Inspect Web worker runtime](inspect-web-worker-runtime.md); and
- page placement and responsive composition from
  [Inspect Web surface composition](inspect-web-surface-composition.md).

It does not redefine those contracts. In particular, the browser does not
match lines, classify movement, reconstruct counts from rendered ranges, or
infer subject identity from display text.

## User scenario

From one selected method, the user can compare checksum-verified PDB source with
dotnet-inspect's decompiled C# for the same MethodDef. The initial view answers:

- whether the two sources differ;
- how many lines were added, removed, changed, or moved;
- where each displayed change occurs; and
- which endpoint and provenance each pane represents.

The ordinary case is one selected member and one `AnalysisDiff<string>`. It
does not use `ComparisonDocument<T>` merely to claim multi-subject adoption.
Type-wide and cross-subject comparisons are a later composition slice and use
`ComparisonDocument<T>` when their producer has a genuine root comparison and
subject-transition graph.

## Product query

The browser needs a comparison query distinct from the existing source query.
`AssemblyContextSourceQuery.ExecuteMemberAsync` preserves the behavior-safe
Source default: it returns verified PDB source when available and decompiles
only as a fallback. A diff request explicitly pays for both endpoints.

The comparison query:

1. resolves one exact implementation member through the existing assembly
   context and binding-policy checks;
2. attempts SourceLink and checksum-verified PDB source;
3. independently produces decompiled C# for the same resolved MethodDef;
4. returns explicit endpoint availability and failure information;
5. when both endpoint texts are available, calls
   `TextFindings.CreateAnalysisDiff`; and
6. returns a presentation-neutral result containing endpoint evidence and the
   complete `AnalysisDiff<string>`.

The query owns acquisition order, retained-image lifetime, binding-policy
validation, and the association between the two endpoint texts. It must reuse
the existing source-query helpers rather than create a second SourceLink or
decompiler implementation.

The comparison query is moderated and gesture-triggered. Ordinary Source
requests remain unchanged and do not acquire or decompile a second endpoint.

## Browser transport

The JS export returns one closed browser DTO rooted in `BrowserJsonContext`.
The DTO contains:

- Before and After source descriptors with provider, provenance, optional
  producer-authorized browse URL, text, and final-line-terminator state;
- explicit comparison availability or failure;
- browser-consumer accounting with added and removed counts plus separate Before
  and After cardinalities for changed and moved populations;
- the complete `AnalysisDiff<string>` relation population projected as
  endpoint coordinates plus content and placement classifications; and
- the complete `MappedTextDiff` endpoint line sequences and changed ranges.

The browser-local DTO is an adaptation boundary, not a parallel analytical
model. It carries only closed JSON shapes that source-generated serialization
and `ts-jsexport` can project. At this non-L1 boundary, the adapter lowers the
query's analysis through
`TextAnalysisDiffPresentation.CreateMappedTextDiff`. The browser projection
owns the accounting policy and computes it directly from
`AnalysisDiff<string>` relations:

- additions and removals count their one-sided endpoint populations;
- changed counts retain separate Before and After cardinalities;
- moved counts retain separate Before and After cardinalities; and
- changed and moved overlap rather than form one exclusive partition.

Statistics are never reconstructed from mapped ranges. The mapped endpoint
line sequences are authoritative for rendered rows and relation coordinates.
Raw endpoint text remains available for copy and provenance operations; the
browser does not split it again to build the diff.

Only a product-issued browse URL may populate the browse field or enable an
Open action. A raw SourceLink resolved URL, fetch URL, or successful acquisition
does not establish browse authorization and is not exposed as an Open target.

The mapped ranges are the shared rendering shape. The browser may arrange
those ranges into interactive DOM for unified or side-by-side presentation,
but it must not change their correspondence or derive a different diff.
Movement remains available from the projected analytical relations even
though Markout conventionally lowers moved text to removal and addition
ranges.

## Operation and state

The worker operation is `query-member-source-diff`. Its arguments use the same
package, version, framework, assembly, type, member anchor, physical body token,
and decompiler-style identity as the existing member Source operation.

Source Diff has its own request identity and publication state. A late result
cannot publish after member, body, package, framework, or decompiler-style
selection changes. Cancellation participates in the existing authoritative
source-operation coordinator so Source, type Source, graph Source, and Source
Diff cannot publish competing results.

Failure remains visible. The worker envelope distinguishes transport or
execution failure from a successful typed result whose PDB or decompiled
endpoint is unavailable.

## Member surface

`Diff` is a body-dependent member section adjacent to `Source` and `Annotated
source`. Selecting it is the explicit expensive gesture.

The working-surface action region contains:

- a Unified / Side by side mode selector;
- Previous and Next change actions;
- a change position such as `2 of 7`; and
- endpoint-specific Open actions when an endpoint has a producer-authorized
  browse URL.

The content begins with a compact factual summary such as `+4 -2 changed 3 → 5
moved 1 → 1`, followed by the complete diff. Changed and moved are explicitly
labelled as overlapping facets. Identical inputs remain an explicit `No source
differences` result with both provenances; they do not render as an empty
success.

### Unified mode

Unified mode is the narrow-screen default and the initial mode unless a
session-local user choice exists. Each row exposes its endpoint kind and line
number without relying on color. Stable context, additions, removals, and
changed populations come from the mapped ranges.

### Side-by-side mode

Side-by-side mode aligns Before and After rows from the same mapped range.
One-sided populations leave an explicit empty peer cell. Horizontal scrolling
is local to each code pane; vertical change navigation remains shared.

At narrow widths the layout falls back to Unified rather than compressing two
unreadable panes. The user's side-by-side preference is retained and restored
when the viewport again admits it.

### Selection and navigation

Each mapped changed range defines one navigable change. Previous and Next use
that stable range order. Activating a change scrolls its first visible row into
view and moves focus to the diff region, not to a synthetic line control.

Text remains selectable across lines. Line-number and change-marker columns
are excluded from copied source text. Keyboard access does not depend on
pointer hover or color.

## Large inputs

The transport always preserves the complete diff. Rendering windows rows across
both stable and changed populations for large inputs, while every changed range
remains represented and reachable. A windowed renderer:

- keeps a bounded window around the active row and surrounding context mounted;
- exposes omitted stable spans as explicit expandable rows;
- represents omitted portions of a large changed range with explicit expandable
  rows while keeping the range address and endpoint cardinalities visible;
- lets change activation enter a bounded window at that range's first row and
  move through later windows without mounting the complete range;
- preserves canonical endpoint line numbers; and
- does not alter summary counts.

The first implementation may render complete ordinary member inputs while a
fixture proves the threshold at which windowing activates. Shipping an
unbounded renderer without that measured threshold is not sufficient for the
large-input requirement.

## Multi-subject composition

The member surface above is the first independently coherent slice.
Multi-subject presentation is a separate stacked slice after a producer emits
a real `ComparisonDocument<T>`.

That slice consumes the document's root comparison and subject transitions
directly. The browser may filter or navigate subjects, but it does not flatten
root and subject item spaces or aggregate counts across them.

The three-way extraction fixture from
[Comparison document](comparison-document.md#three-way-extraction) is the
pathological gate:

- the root `AnalysisDiff<PortableSourceRegion>` retains three independent
  Moved relations from one original method into three extracted methods;
- the subject projection remains one Diff plus three Additions;
- the mapped text projection may display removals and additions without
  claiming to preserve movement; and
- the browser exposes the root movement evidence separately from subject-local
  rendered ranges.

## Staged delivery

The implementation is delivered as focused stack slices:

1. product member source-comparison query, typed browser export, and worker
   operation;
2. member Diff section with unified and side-by-side rendering, change
   navigation, responsive fallback, and bounded large-input behavior; and
3. producer-backed multi-subject `ComparisonDocument<T>` adoption plus the
   three-way extraction fixture.

Each slice is independently correct. The first does not claim a user-visible
surface; the second does not claim multi-subject support; the third does not
replace the ordinary single-member path with an artificial composition.

## Gates

The delivery names the following Release gates:

- query tests prove both-endpoint success, identical text, PDB unavailable,
  decompilation unavailable, cancellation, and binding-policy invalidation;
- presentation tests prove the consumer accounting policy for one-sided,
  unequal changed, moved, and overlapping changed-plus-moved populations, and
  prove mapped ranges come from `TextAnalysisDiffPresentation`;
- browser boundary tests prove the closed DTO survives source-generated JSON
  and generated TypeScript without unknown members, and prove that a raw
  resolved or fetch URL does not enable Open;
- worker tests prove request identity, cancellation, stale-result suppression,
  and typed failure settlement;
- browser renderer tests prove all relation shapes, multi-change navigation,
  unified and side-by-side parity, responsive fallback, text selection, stable
  and changed-heavy bounded windows, and window expansion;
- the three-way extraction gate proves root movement and subject topology
  remain distinct; and
- the hosted workspace demo follows
  [Inspect Web demo hosting](../runbooks/inspect-web-demo-hosting.md) and shows
  more than one annotated difference.
