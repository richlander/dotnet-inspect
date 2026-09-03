# Member source diff presentation

## Status and ownership

This document defines the `DotnetInspector.Presentation`-owned member source
diff projection for
[#5683](https://github.com/richlander/dotnet-inspect/issues/5683), within the
Inspect Web composition tracker
[#5528](https://github.com/richlander/dotnet-inspect/issues/5528) and the
structured-comparison tracker
[#5526](https://github.com/richlander/dotnet-inspect/issues/5526).

The normative claim is:

> Complete PDB and decompilation endpoint evidence for one member projects to
> one canonical standalone text pair, one `AnalysisDiff<string>`, one
> two-sided statistics summary, and one Markout `MappedTextDiff` shared by the
> CLI and browser hosts.

This is a focused L2 presentation design. It consumes:

- the two available endpoint attempts from
  [Member source comparison query](member-source-comparison-query.md), whose
  implementation is tracked by
  [#5690](https://github.com/richlander/dotnet-inspect/issues/5690);
- the producer-owned whole-member `MemberRenderResult.Text` contract;
- model-free declaration-trivia recognition from `CSharpText`;
- source-line correspondence from `TextFindings.CreateAnalysisDiff`;
- relation semantics from [Analysis diff](analysis-diff.md); and
- mapped text presentation from Markout.

It does not acquire source, resolve members, decompile assemblies, define
browser transport, or define browser interaction.

This effort uses the bounded first-adopter exception from
[Design scope and composition](../design-scope.md#stage-implementation-after-locking-the-design):
it defines the shared projection and adopts it in the existing CLI Source Diff
consumer. Later browser adoption remains separately owned by #5684 through
issue #5686.

## Why a canonical projection is required

The query's endpoints are intentionally not ready to compare as raw strings:

- verified PDB member source is a standalone source declaration; and
- `MemberRenderResult.Text` is the member's byte-identical segment in a
  whole-type listing, indented one type-body level.

The current CLI Source Diff does not consume `MemberRenderResult.Text`. It
constructs a separate CLI-owned member declaration from `DecompilerResult`,
chooses expression-body presentation independently, and applies different
signature wrapping. That path cannot be the shared endpoint because the
browser does not receive its typed inputs and reproducing it would move CLI
formatting policy into the query.

The shared projection therefore standardizes on the product-owned whole-member
render, removes producer-rendered declaration trivia that the PDB member slice
does not contain, and converts placement from type-body context to standalone
context. This deliberately changes CLI Source Diff hunks and statistics where
the old CLI projection chose different wrapping or expression-body layout. No
compatibility switch preserves the old comparison-only projection.

The separate CLI `Decompiled Source` section remains a CLI presentation. The
diff's After endpoint is labelled `Decompiled comparison`, not `Decompiled
Source`, so one output never claims that two different strings are the same
section content.

## Input boundary

The projection accepts only one `Available` PDB endpoint and one `Available`
decompiled endpoint from the same successful member source-comparison result.
It does not accept independently acquired strings.

The PDB endpoint contributes:

- its complete checksum-verified member text;
- repository and checksum provenance retained beside the presentation result;
  and
- query-owned unavailable-reason distinctions when the endpoint is absent.

The decompiled endpoint contributes one complete `MemberRenderResult`. Failed
or absent results expose no candidate text and cannot enter this projection.

Partial, unavailable, not-found, failed, and rejected query outcomes do not
become empty or one-sided diffs. Hosts present those typed outcomes without a
`MappedTextDiff`.

## Canonical endpoint text

### Before

Before text is the checksum-verified PDB member text emitted by the source
producer, unchanged character-for-character. That producer has already
dedented the selected declaration, normalized line endings to LF, and trimmed
the terminal newline. The presentation adapter performs no further trimming,
indentation, or line-ending transformation.

### After

After text is a standalone projection of complete
`MemberRenderResult.Text`. The product render guarantees a whole-type member
segment indented one type-body level and may include metadata attribute lines
before the signature. The projection:

1. removes exactly that one leading four-space level from every non-empty
   physical line;
2. uses `CSharpText` declaration recognition to identify the one member's
   signature start; and
3. removes only the leading attribute/trivia lines before that signature,
   matching the PDB member-slice boundary.

It does not:

- reflow or unwrap the signature;
- choose a different block or expression-body form;
- trim nested indentation or trailing whitespace;
- remove attributes from inside the declaration or body;
- use a host-owned C# parser or regenerate C#;
- add using directives from `MemberRenderResult.Namespaces`; or
- manufacture text for an incomplete render.

Removing one producer-guaranteed placement indent preserves the member's
relative indentation and every decompiler-owned spelling choice. Typed
declaration recognition prevents source attributes that live outside the PDB
slice from becoming fake additions. If a non-empty line lacks the required
prefix, or one exact signature start cannot be established, projection fails
visibly rather than guessing a source boundary.

`MemberRenderResult` has already normalized line endings to LF and trimmed the
terminal newline. Canonical After therefore has an absent final terminator, as
does canonical Before.

## Analysis and statistics

The adapter calls `TextFindings.CreateAnalysisDiff` exactly once over canonical
Before and After text.

Its statistics policy counts directly from the resulting relations:

- additions count After coordinates in Addition relations;
- removals count Before coordinates in Removal relations;
- changed populations retain separate Before and After cardinalities; and
- moved populations retain separate Before and After cardinalities.

Changed and moved are independent facets and may overlap. The adapter never
reconstructs statistics from Markout ranges and never collapses an N:M
correspondence into implied pairs.

An identical result is explicit: all six item counts are zero while the
complete analysis remains available.

## Markout lowering

The adapter lowers the same `AnalysisDiff<string>` through
`TextAnalysisDiffPresentation.CreateMappedTextDiff`.

The mapped Before and After line sequences are value- and order-identical to
the analysis endpoint sequences. Analysis coordinates therefore address the
same indexed lines exposed by `MappedTextDiff`; no host rebases or re-splits
them.

Only stable unchanged one-to-one correspondences become Markout anchors.
Every other relation becomes conventional removal and addition ranges.
Movement identity remains in the analysis and statistics even when the mapped
text presentation is intentionally lossy.

The mapped sequences record the absent final-line-terminator state of both
canonical producer texts. Generic line-ending equivalence and asymmetric
terminator behavior remain owned and gated by `TextFindings` and Markout; this
member-source projection does not claim endpoint states its producers cannot
emit.

## Result and host boundary

One successful presentation result retains:

- canonical Before and After text;
- endpoint provenance;
- the complete `AnalysisDiff<string>`;
- added, removed, changed-Before, changed-After, moved-Before, and moved-After
  counts; and
- the complete `MappedTextDiff`.

The shape is host-neutral and fully materialized. It retains no borrowed
metadata, decompiler IR, stream, reader, workspace lease, or browser state.

Hosts choose disclosure:

- the CLI uses statistics at normal verbosity and the mapped diff at detailed
  verbosity; and
- Inspect Web later transports the same typed information under #5684 and owns
  interaction under #5685 and #5686.

Neither host may substitute different endpoint text and retain the shared
analysis or statistics.

## CLI first adoption

The CLI Source Diff path becomes the first production caller. It consumes the
member source-comparison query and this presentation result instead of
constructing correspondence from `MemberCodeView` strings.

The implementation is ordered after #5690. The CLI does not adopt this adapter
until the query exposes the source-unavailability distinctions needed to
preserve its existing `too complex`, invalid coordinates, no declaration, and
acquisition-failure outcomes.

Within one CLI invocation, the comparison query's PDB endpoint is also the PDB
Source section evidence when that section is selected. The host does not repeat
equivalent source acquisition merely because PDB Source and Source Diff were
selected together.

Selecting Source Diff together with another decompiler section may run both the
whole-member comparison render and that section's distinct typed decompiler
artifact. This duplication is accepted because the artifacts have different
contracts and the comparison result retains no borrowed decompiler IR that
could safely substitute for them.

The CLI preserves:

- explicit Source Diff selection and current verbosity behavior;
- exact and line-ending-normalized checksum evidence;
- factual two-sided statistics;
- complete detailed Markout output;
- the distinct `Decompiled comparison` After label;
- explicit identical and unavailable outcomes; and
- structured table, TSV, and JSONL projections.

The intentional compatibility change is limited to the canonical decompiled
endpoint described above. Tests assert the new endpoint and resulting diff
rather than freezing the superseded CLI-only projection.

## Pathological demonstration

The contract fixture uses a complete product render with:

- a wrapped multi-line signature;
- leading metadata attributes excluded from the PDB member slice;
- nested block indentation;
- an expression that differs from PDB source;
- one moved line; and
- one two-line to three-line changed correspondence.

The fixture proves:

- exactly one type-body indentation level is removed from every non-empty After
  line;
- leading rendered attributes do not become attribute-only additions;
- wrapped signature and nested indentation remain otherwise unchanged;
- changed and moved counts retain separate Before and After cardinalities;
- mapped endpoint lines are index-identical to analysis endpoint lines; and
- Markout renders the complete lossy text mapping without erasing analytical
  movement.

## Gates

Release presentation tests prove:

- PDB text is character-for-character preserved as canonical Before text;
- one and only one producer-guaranteed type-body indent is removed from
  complete decompiled lines;
- leading rendered attributes outside the PDB member-slice boundary are
  excluded through `CSharpText` declaration recognition;
- inconsistent complete-result indentation fails visibly;
- ambiguous or missing signature boundaries fail visibly;
- incomplete endpoint evidence cannot produce a diff;
- unequal changed and moved populations retain two-sided counts;
- changed and moved overlap;
- identical inputs remain an explicit complete result;
- analysis and mapped endpoint sequences are index-identical;
- both mapped sequences retain the producer-issued absent-terminator state; and
- an N:M changed correspondence plus a moved relation proves statistics equal
  relation cardinalities even though Markout lowers them to removal/addition
  ranges.

Release CLI tests prove:

- the production Source Diff path calls the shared adapter;
- normal and detailed verbosity preserve their disclosure boundary;
- checksum provenance and unavailable outcomes remain visible;
- the PDB Source and Source Diff co-selection performs one equivalent PDB
  acquisition;
- the After header is `Decompiled comparison`, while the separate Decompiled
  Source section keeps its own label and content;
- non-text projections retain structured statistics; and
- a wrapped-signature fixture demonstrates the intentional replacement of the
  old CLI-only endpoint projection.

This typed same-result boundary deliberately adds the Queries, Decompiler,
Text, and CSharpText dependency graph to `DotnetInspector.Presentation`. Both
planned hosts already carry that graph, and accepting the query result directly
prevents hosts from pairing independently acquired endpoints.

Layering tests prove `DotnetInspector.Presentation` remains L2, consumes L1
query results, and is referenced by both the CLI and the planned Browser/Wasm
adapter without introducing a CLI or browser dependency.
