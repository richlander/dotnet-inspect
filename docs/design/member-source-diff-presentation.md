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
> one canonical placement-aligned comparison pair, one
> `AnalysisDiff<string>`, one two-sided statistics summary, and one Markout
> `MappedTextDiff` shared by the CLI and browser hosts.

This is a focused L2 presentation design. It consumes:

- the two available endpoint attempts from
  [Member source comparison query](member-source-comparison-query.md), whose
  implementation is tracked by
  [#5690](https://github.com/richlander/dotnet-inspect/issues/5690);
- the producer-owned whole-member `MemberRenderResult.Text` contract;
- the query-owned declaring `MetadataTypeDefinitionName`;
- metadata-name arity recognition from MetadataPrimitives;
- model-free identifier admission and declaration-trivia recognition from
  `CSharpText`;
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

- verified PDB member source is a member slice whose producer preserves
  placement when a column-zero directive or literal continuation prevents a
  common dedent; and
- `MemberRenderResult.Text` is the member's byte-identical segment in a
  whole-type listing, indented one type-body level.

The current CLI Source Diff does not consume `MemberRenderResult.Text`. It
constructs a separate CLI-owned member declaration from `DecompilerResult`,
chooses expression-body presentation independently, and applies different
signature wrapping. That path cannot be the shared endpoint because the
browser does not receive its typed inputs and reproducing it would move CLI
formatting policy into the query.

The shared projection therefore standardizes on the product-owned whole-member
render, removes declaration-leading trivia from both comparison endpoints, and
aligns the decompiled placement prefix to the PDB declaration's retained
whitespace prefix. Applying the same typed boundary to both endpoints handles
an attribute or comment that shares the PDB signature line without
manufacturing a difference.
This deliberately changes CLI Source Diff hunks and statistics where the old
CLI projection chose different wrapping or expression-body layout. No
compatibility switch preserves the old comparison-only projection.

The separate CLI `Decompiled Source` section remains a CLI presentation. The
diff endpoints are labelled `PDB comparison` and `Decompiled comparison`, not
`PDB Source` and `Decompiled Source`, so one output never claims that normalized
comparison text is the separate section content.

## Input boundary

The projection accepts only one `Available` PDB endpoint and one `Available`
decompiled endpoint from the same successful member source-comparison result.
It does not accept independently acquired strings.

The successful result also contributes its query-owned declaring type identity.
The adapter does not infer constructor context from display text.

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

The checksum-verified PDB member text remains available unchanged through the
query result for provenance and the PDB Source section. The canonical Before
comparison starts from that text, which the producer has already dedented,
when possible, normalized to LF, and terminal-newline-trimmed. A source
construct at column zero may have prevented the producer's common dedent, so
the declaration may retain its source placement prefix.

The adapter applies the shared declaration boundary below to remove only
declaration-leading trivia. That includes complete leading trivia lines and,
when an attribute or comment shares the signature line, its non-whitespace
prefix before the typed signature-start column. The whitespace placement prefix
before the declaration remains unchanged, as do signature text, body text,
relative indentation, trailing whitespace, literal content, and line
boundaries.

### After

After text is a placement-aligned projection of complete
`MemberRenderResult.Text`. The product render guarantees a whole-type member
segment indented one type-body level and may include metadata attribute lines
before the signature. The projection applies the same declaration boundary as
Before, then replaces exactly the producer-guaranteed four-space placement
prefix on every non-empty physical line with the PDB endpoint's retained
whitespace placement prefix.

### Shared declaration boundary

`CSharpText` recognizes members only inside a type, and constructor
classification depends on the enclosing type's name. The adapter therefore
derives one synthetic class identifier from the query-owned declaring type
identity:

1. select the leaf segment of `MetadataTypeDefinitionName`;
2. remove only its canonical generic-arity suffix through
   `MetadataNameArity.StripFromSegment`; and
3. require `CSharpIdentifier.AdmitTypeDeclaration` to admit the exact
   declaration spelling.

Both endpoints are placed inside synthetic class wrappers with that same
identifier. If the exact identifier is not admitted, projection fails visibly;
it does not sanitize the identity or fall back to a fixed wrapper name.

The adapter selects exactly one direct child member declaration whose signature
coordinate and end boundary cover the endpoint's complete member segment, then
translates its line and column coordinates back to the unwrapped endpoint.

The synthetic type contributes no output text, analysis item, line number, or
Markout coordinate. It exists only to obtain the owner-issued trivia,
signature-line, and signature-column boundary. The adapter removes
non-whitespace declaration trivia before that boundary while retaining the
endpoint's leading whitespace placement prefix and everything from the
signature token through the member end.

On a signature line with same-line trivia, the retained placement prefix is the
maximal leading whitespace run before that trivia. When the entire prefix
before the signature token is whitespace, the adapter retains it unchanged.
The canonical PDB placement prefix is the resulting whitespace before the
signature token.

It does not:

- reflow or unwrap the signature;
- choose a different block or expression-body form;
- trim nested indentation, PDB placement whitespace, or trailing whitespace;
- remove attributes or comments after the signature token;
- use a host-owned C# parser or regenerate C#;
- infer the declaring type from endpoint text or display names;
- add using directives from `MemberRenderResult.Namespaces`; or
- manufacture text for an incomplete render.

Replacing one producer-guaranteed placement prefix preserves the member's
relative indentation and every decompiler-owned spelling choice while matching
the source declaration's actual placement. PDB lines are never dedented as a
group, so a column-zero directive or verbatim-string continuation remains
unchanged. Applying one typed declaration boundary to both endpoints prevents
trivia placement from becoming a fake change. If a non-empty After line lacks
the required prefix, or either wrapped endpoint does not establish one exact
child member and signature start, projection fails visibly rather than guessing
a source boundary.

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
- original endpoint provenance and access to the unchanged producer evidence;
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
- distinct comparison endpoint labelling from the separate source sections;
- explicit identical and unavailable outcomes; and
- structured table, TSV, and JSONL projections.

The intentional compatibility change includes both comparison endpoints and
their labels: declaration-leading trivia may be removed from PDB comparison
text, the decompiled comparison uses product-owned wrapping and body choices,
and the headers change to `PDB comparison` / `Decompiled comparison`. A failed
decompilation also changes from a diff against the old CLI's source-shaped
diagnostic comment to an explicit unavailable Source Diff with no statistics
or `MappedTextDiff`. Tests assert the new pair and typed failure outcome rather
than freezing the superseded CLI-only projection.

## Pathological demonstration

The contract fixture uses a complete product render with:

- a wrapped multi-line signature;
- leading metadata attributes excluded from the PDB member slice;
- an attribute sharing the PDB signature line;
- a column-zero conditional directive inside an otherwise indented PDB slice;
- a column-zero verbatim-string continuation;
- a no-modifier constructor in a class named `extension`;
- nested block indentation;
- an expression that differs from PDB source;
- one moved line; and
- one two-line to three-line changed correspondence.

The fixture proves:

- the decompiler's four-space placement prefix is replaced with the exact PDB
  whitespace placement prefix on every non-empty After line;
- a synthetic type scope yields exact member signature coordinates without
  entering output or analysis coordinates;
- declaring-type-derived scope keeps the `extension()` constructor distinct
  from a C# 14 extension block;
- separate-line and same-line declaration trivia are removed consistently from
  both endpoints and do not become attribute-only additions or removals;
- PDB directive and literal-continuation lines remain byte-for-byte unchanged;
- wrapped signature and nested indentation remain otherwise unchanged;
- changed and moved counts retain separate Before and After cardinalities;
- mapped endpoint lines are index-identical to analysis endpoint lines; and
- Markout renders the complete lossy text mapping without erasing analytical
  movement.

## Gates

Release presentation tests prove:

- the unchanged PDB endpoint remains available beside canonical Before text;
- one and only one producer-guaranteed type-body placement prefix is replaced
  on complete decompiled lines;
- spaces, tabs, and retained nonzero PDB placement prefixes align the After
  endpoint without changing PDB body or literal lines;
- column-zero directives and verbatim-string continuations do not trigger PDB
  dedenting;
- the admitted leaf declaring-type identity supplies both synthetic wrapper
  names, while an unrepresentable identity fails visibly;
- wrapper naming comes only from the successful query result's exact type
  identity, not endpoint or host display text;
- a constructor named `extension` produces one constructor boundary rather
  than selecting a declaration-shaped body statement;
- the selected direct child starts at the typed signature coordinate, covers
  the complete member segment, and contributes no wrapper lines or coordinates;
- separate-line and same-line attributes/comments before the signature token
  are excluded consistently from both endpoints;
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
- a PDB-available member whose decompilation fails produces an explicit
  unavailable Source Diff without statistics, mapped output, or diagnostic
  text treated as source;
- the PDB Source and Source Diff co-selection performs one equivalent PDB
  acquisition;
- the headers are `PDB comparison` and `Decompiled comparison`, while the
  separate PDB Source and Decompiled Source sections keep their own labels and
  content;
- non-text projections retain structured statistics; and
- a wrapped-signature fixture demonstrates the intentional replacement of the
  old CLI-only endpoint projection.

This typed same-result boundary deliberately adds the Queries, Decompiler,
Text, Metadata, MetadataPrimitives, and CSharpText dependency graph to
`DotnetInspector.Presentation`. Both planned hosts already carry that graph,
and accepting the query result directly prevents hosts from pairing
independently acquired endpoints.

Layering tests prove `DotnetInspector.Presentation` remains L2, consumes L1
query results, and is referenced by both the CLI and the planned Browser/Wasm
adapter without introducing a CLI or browser dependency.
