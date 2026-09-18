# Annotated Source C# Projection

## Status

This design owns one bounded Decompiler operation: projecting one
`AnnotatedSourceDocument` onto its C# plane while preserving every retained
document plane and returning explicit original-to-projected C# node identity.

It transfers only that projection contract from
[Implementation Diff](implementation-diff.md). Correspondence between
documents, structural comparison, rendering, Research composition, and
host-specific presentation remain with their existing owners.

## Exact claim

`CSharpAnnotatedSourceProjection.Create` returns:

- one validated C#-only `AnnotatedSourceDocument`; and
- one immutable map from every retained source-document C# node id to its
  projected-document node id.

The projection deletes text only when IL nodes prove complete ownership of the
line content. It rebases retained absolute spans in UTF-16 code units, preserves
the original text and terminator code units in every retained segment, and
fails visibly rather than clipping C# structure across deleted text.

This is a one-document transformation. The node map records identity created by
that transformation; it does not create identity between two documents.

## Design basis

`AnnotatedSourceDocument` is the normative owner of the text buffer, absolute
UTF-16 spans, node and fact id rules, targets, regions, node provenance, and
physical method source. This projection consumes those types without
redefining them.

The implementation replaces the internal comparison helper introduced in #4254
and incorporates the useful complete-line and fact-retention behavior
demonstrated by the superseded #4050 prototype. Neither earlier implementation
is authoritative: this document narrows and completes the product contract
required by #4097.

Roslyn's text model is supporting evidence for the coordinate choice only:
.NET and JavaScript strings both index decoded UTF-16 code units. The product
does not add a Roslyn dependency or parse projected text.

## Text ownership and removal

The input text is split into content plus its exact existing line terminator:
LF, CRLF, CR, or no terminator. Projection never normalizes those code units.

IL nodes establish removal by the union of their spans on each line:

1. Every IL span must select line content only. Selecting any terminator or
   crossing outside content is invalid.
2. A line touched by IL nodes is removable only when their union covers every
   content code unit from the first through the last.
3. Gaps are partial or mixed ownership and fail the projection.
4. Overlap among IL spans is allowed; ownership is established by their union,
   not by one privileged node.
5. Text with no IL-node coverage is retained. The projection never infers IL
   from labels, prefixes such as `IL_`, indentation, or text shape.

Each retained line contributes its complete original segment, including its
terminator when present. Consequently, removing a final IL line preserves the
preceding retained line's original terminator. This is deliberate continuity
with the existing structural-diff artifact and keeps projection a deletion of
proved IL segments rather than a newline-rewriting pass.

An empty C# plane is valid when complete IL ownership removes every line.

## Retained structure

Nodes whose `Medium` is `CSharp` are retained in source list order and
renumbered contiguously from zero. Their kind and
`AnnotatedSourceNodeProvenance` are preserved exactly. Their spans are
intersected with retained text segments and rebased into the projected buffer;
adjacent projected pieces coalesce deterministically.

The immutable original-to-projected node map is total and one-to-one over those
retained nodes. IL nodes have no entry. Consumers use this map when an
owner-issued source-document node identity must be carried through the
projection; they must not recover identity from text, spans, regions, order,
ordinals, labels, or equal document-local ids.

Every region is retained in source list order with its role unchanged and its
spans rebased by the same rule.

A retained C# node or region that overlaps deleted IL-owned text is invalid.
The projection throws instead of clipping that structure into a different
syntactic claim.

## Facts and targets

Facts have no text coordinates, so projection treats their targets as the
retention boundary:

- an unanchored fact remains unanchored, including member-header facts;
- a targeted fact with at least one retained C# target remains, with only its
  retained targets;
- a fact targeted only to removed IL nodes is dropped;
- a fact targeted to both C# and IL nodes remains once, with its C# targets;
  and
- `SourceOffset` remains producer evidence and is not rebased.

Retained facts are renumbered contiguously in source list order. Targets retain
their source order while their fact and node ids are remapped. The projection
does not synthesize a target for an unanchored fact or infer one from an IL
offset.

## Consumer contract

`CSharpBodyDiff` remains the owner of structural correspondence. It issues
correspondence over the original documents using unique product-owned IL
provenance, then uses each projection's node map to name the projected nodes
that own comparison rows.

`CSharpStructuralDiffDocument` retains the original mixed documents in its
correspondence payload and the C# projections at top level. Construction and
strict replay continue to reissue correspondence and derive both projections
and rows rather than accepting caller-authored identity.

No CLI or viewer rendering changes in this slice. The public projection is
host-neutral typed substrate for producers and structural artifacts.

## Gates

`CSharpAnnotatedSourceProjectionTests` gates:

- exact UTF-16 rebasing across astral text, LF, CRLF, multiline, and multi-span
  structure;
- unioned complete-line IL ownership;
- visible rejection of partial ownership, line-terminator selection, and C#
  structure crossing removed text;
- preservation of source identity, C# node provenance, regions, facts, mixed
  targets, and unanchored facts; and
- deterministic node and fact renumbering with immutable node identity.

`CSharpStructuralComparisonTests.StructuralDiffDocument_ProjectsInterleavedIlWithoutInferringFromText`
is the producer-to-portable-artifact gate. Its original correspondence names
C# node id 1 while the projected rows name node id 0, proving that the consumer
uses the explicit projection map rather than equal ids or source text.

Both gates run in the Release `ILInspector.Decompiler.Tests` executable.

## Non-goals

This design does not:

- issue or infer cross-document correspondence;
- define C# structural comparison or movement classification;
- parse C# or IL text;
- define an IL-only projection;
- define a renderer or Markout lowering;
- normalize line endings or remove otherwise unproved text; or
- claim that equal projected text, spans, provenance, or local ids establish
  identity across documents.
