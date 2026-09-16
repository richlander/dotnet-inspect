# Body Shape views

## Owned claim

The CLI presents the same selected Body Shapes evidence as either locatable
occurrences or an explicitly selected counted overview. Column projection does
not change the row unit.

The production consumers are `library`, `type`, and `member`.
[Delivery tracker #6186](https://github.com/richlander/dotnet-inspect/issues/6186)
owns the three-step adoption path. The user approved
this CLI presentation scope after reviewing repeated, indistinguishable
`Kind;Match` rows. Delivery comprises three steps: define this contract;
implement the three consumers and their output/discovery gates; publish the
demo and complete review.

## Basis and boundaries

[Progressive disclosure](progressive-disclosure.md) owns explicit selection
and expensive-work authorization. [Section-row shaping](section-row-shaping.md)
owns projection, row windows, and terminal Count.
[Output shapes](output-shapes.md) owns format lowering.
Decompiler's existing `BodyShapeSearchResult` supplies rendered-syntax
occurrences and failures; this document does not change that producer.

Counted text summaries follow the familiar
[`uniq -c`](https://www.gnu.org/software/coreutils/manual/html_node/uniq-invocation.html)
pattern, but group the whole selected evidence set rather than only adjacent
duplicates. Existing CLI member summaries likewise separate overview from
individual member detail. These are UX precedents, not semantic-equivalence
oracles.

Use the existing typed Markout view/section pipeline for all renderings.
Grouping is a CLI presentation projection shared by its three command
consumers, not a new inspection or decompilation API. Browser UI adoption and
instruction-origin mapping are separate work.

The library and API serializer contexts retain separate row-view types, as
the occurrence views already do: the current Markout generator emits a
type-info name per registered type and cannot register that type in both
contexts. Both views consume one shared grouping projection. Native library
JSON uses its existing typed JSON serializer and a raw summary model;
type/member document JSON retains its existing explicit rejection, with JSONL
available for rows.

## Two explicit sections

| Section | Row unit | Columns |
| --- | --- | --- |
| Body Shape Summary | One exact rendered `(Kind, Match)` group | Kind, Match, Count |
| Body Shapes | One rendered-syntax occurrence | Kind, Member, Token, Start Line, Start Column, End Line, End Column, Match |

Both sections are explicit-only and require the existing single exact
`--where "Kind=..."` predicate. Without `-S`, that predicate continues to select
`Body Shapes`. Select `Body Shape Summary` to request aggregation; no additional
mode flag is needed. Both may be selected in a document format.

Summary equality is ordinal equality of the producer's Kind and rendered
match text, before presentation escaping. It is not semantic equivalence:
`new object()` and `new()` remain separate. Identical matches in different
members, or at different extents in one member, contribute separately.
Group order is the order of each group's first occurrence.

The selected inspection and existing method predicates bound the input before
grouping. Each independent inspection retains its own groups. Row windows
then select summary groups without reducing their Count values.
`--count` counts the selected view's surviving rows: groups for the summary,
occurrences for `Body Shapes`. It never sums the summary's Count column.
Projection only removes columns, even when remaining cells are identical.
Existing restrictions on `--top` and `--order-by` remain in force.

Type `--member` filters narrow the evidence for both views before grouping.
This also corrects occurrence output that previously searched the whole type
despite that explicit member filter.

Member and Token identify the containing method. Start/End Line and Column
are one-based coordinates in that method's rendered C# body, not an original
source file or IL instruction. Drill down by selecting `Body Shapes` with its
member/token/extent columns; do not manufacture an IL offset from a text range.

Both views report the observed matches, preserve partial-search diagnostics,
and retain the existing explicit empty state. A failed inspection must not
become a zero-count summary. Structural and query discovery describe both
sections without running the producer; effective discovery uses the same
Kind requirement and evidence as occurrence output.

Discovery retains each host's existing gestures. Library supports structural
`-D "Body Shape Summary" --schema` and `--effective` inspection; type/member
target-bound `-D "Body Shape Summary"` is already effective. Member structural
discovery uses an explicit `--member Name:1` selector with `--schema`.
Type `--schema` remains the type-listing catalog; `type -Q "Body Shape Summary"`
describes the body query without acquisition.

## Demo

For `DotnetInspector.Fixtures.BodyShapeFixture`, select
`--where "Kind=ObjectCreationExpression" -S "Body Shape Summary"`
and project `--columns "Match;Count"`:

| Match | Count |
| --- | --- |
| `new object()` | 3 |
| `new()` | 1 |

Selecting `Body Shapes` instead retains all four occurrences. Its Member,
Token, and rendered-C# extent columns distinguish matches even when their
text is identical. Hiding those columns still leaves four rows.

## Gates

`BodyShapeSummaryTests` and `BodyShapeSummaryApiTests` run in the Release CLI
test executable. `Summary_GroupsExactTextAcrossAndWithinMembersInFirstOccurrenceOrder`
gates exact grouping; `SummaryRowWindow_SelectsGroupsWithoutTruncatingCounts`
and `SummaryCount_CountsSurvivingGroupsNotOccurrences` gate the row unit.
`ColumnProjection_PreservesViewCardinality` gates projection without regrouping.
`TypeSummary_AppliesMemberFilterBeforeGrouping` and
`LibrarySummary_FiltersMethodsBeforeGrouping` gate scope.
`FailedSearch_IsNotASuccessfulEmptySummary` gates failed-result propagation.
The same classes cover predicate authorization, empty output, discovery, and
native JSON/JSONL behavior. Existing Body Shapes tests continue to cover the
occurrence producer, location columns, and visible partial-search behavior.
