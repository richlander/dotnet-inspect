# Inspect Web decompiler diff

## Status and ownership

This document owns the cross-version decompiler diff of one Member: the
**Decompiler** mode of the
[Inspect Web Compare Explore](inspect-web-compare-explore.md) destination,
under the Compare tracker
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213) and the
Implementation Diff adoption tracker
[#4706](https://github.com/richlander/dotnet-inspect/issues/4706), step 9.

Its normative claim is:

> For one Member diff destination, two independently resolved Annotated
> Source documents, one per package version, produce one text diff per
> medium through the product's shared text-diff presentation. The
> Decompiler mode shows that diff in the diff viewer, C# or IL, with each
> side's Finding annotations on its own lines, and without a Browser-side
> matcher or a cross-version node correspondence.

This owner defines:

- the pairing of two per-version Annotated Source documents for one
  destination, their independent resolution, and the pair's outcomes;
- which document lines form each side's C# and IL sequences;
- the comparison and presentation of those sequences through the shared
  text-diff pipeline, and the lowering of Finding annotations onto the
  diff;
- the Decompiler mode's medium switch, annotation display, and destinations;
  and
- the mode's availability, lifetime, and retention, which Compare Explore
  delegates to this owner.

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Inspect Web Compare Explore](inspect-web-compare-explore.md) | The Member diff destination, its endpoint coordinates and anchors, mode availability, and the full-bleed viewer |
| Annotated Source member document query (`AnnotatedMemberDocumentQuery`) and [Annotated Source viewer interaction](annotated-source-viewer-interaction.md) | One version's complete annotated member document: text, media, nodes, and Findings; and the detail and action vocabulary for a Finding |
| [Text whitespace characterization](text-whitespace-characterization.md) and [Text move characterization](text-move-characterization.md) | Whitespace-only and moved facts and the whitespace policy; decompiled → decompiled comparisons are whitespace-sensitive |
| [Member source diff presentation](member-source-diff-presentation.md) | The shared Presentation lowering to `AnalysisDiff<string>`, statistics, characterization, and Markout `MappedTextDiff` |
| [Inspect Web source-diff transport](inspect-web-source-diff-transport.md) | The bounded line-diff payload, including Markout annotations, and its admission |
| [Inspect Web diff viewer interaction](inspect-web-diff-viewer-interaction.md) | Rows, modes, navigation, and whitespace and move controls |
| [Operation Authority](inspect-web-operation-authority.md) | Operation identity, cancellation, supersession, and publication |

## Why

The authored Source diff compares what the author wrote. The decompiler diff
compares what the compiler emitted, which answers different questions: did
the code generation change, did an allocation or a throw path appear, did a
call target move? It also exists for Members without PDB source and for
compiler-generated shape, where no authored Source diff is possible.

The substrate exists but is not joined:

- Annotated Source already produces one version's complete member document
  in the Browser's engine Worker, with C# and IL media and Findings.
- `WorkspaceImplementationComparisonQuery` compares one Member's decompiled
  C# and IL across two versions for the CLI, but its C# lane compares
  trimmed canonical body lines, a line-identity policy the whitespace
  characterization plans to retire, and it produces no annotated documents.
- `CSharpStructuralComparison` matches C# nodes by IL origin, which exists
  only within one physical method body, so it cannot pair nodes across
  versions.

Two per-version documents compared as text give both the diff and the
annotations with no new matcher.

## Pairing

For one destination, the pair resolves each present endpoint independently
in its own retained package image, through the Annotated Source member
document query, with the endpoint's coordinates and anchor exactly as the
destination carries them. A side resolves or fails on its own; neither side's
identity, text, or outcome informs the other.

Both sides use the same decompiler style: the Settings preference in effect
when the request starts. The pair records that style as part of its identity,
so a style change starts a new pair rather than mixing styles.

| Pair outcome | Meaning |
| --- | --- |
| Compared | Both sides produced a document |
| One-sided | One endpoint is absent from the relation, an added or removed Member; the present side's document is shown as source |
| Unavailable | A present side has no document, with that side's typed reason; the other side's document remains available |
| Failed | A side's query failed, with that side's typed failure |
| Too complex | A medium's comparison exceeds the transport's admission profile |
| Canceled | The request was superseded or canceled |

## Sequences and comparison

A medium's sequence is the document's lines of that medium, in document
order, with each line's text exactly as the document holds it. The C#
sequence is the C# lines; the IL sequence is the IL lines. A line of one
medium never enters the other's sequence.

For each medium, Presentation compares the two sequences' texts with the
product's text comparison, characterizes the result for whitespace and
moves, and lowers it with the labeled Markout lowering, exactly as the member
source diff does. The two media are compared independently; the pair issues
no correspondence between a C# line and an IL line or between Before and
After nodes.

## Annotations

Each side's Findings become Markout annotations on that side's lines. A
Finding anchored to a node of the displayed medium lowers to a span
annotation on the node's line and characters; a Finding with no node in that
medium is listed in the mode's side summary, not placed on a line. The
Finding's severity maps to Markout's severity, and its text is the Finding's
own short text. An annotation names its side, so the same Finding on both
sides appears once on each.

Annotations are evidence about one side. The diff never asserts that a
Before Finding and an After Finding are the same Finding, and it never
counts a Finding as added or removed. A future Finding census identity may
supply that relation.

Transport admission bounds annotations; a pair whose annotations exceed the
admission profile shows the diff without them and says so, rather than
dropping some silently.

## Decompiler mode

The mode shows the full-bleed host of the diff viewer over one medium's diff,
with a **C# | IL** switch; C# is the default. The viewer's whitespace control
defaults to **Mark**, since both sides are printer output. Annotation
decorations appear on their lines; activating one opens that side's
Annotated Source at the Finding, in a new browsing context, through the
Annotated Source viewer's external-opening destination.

A One-sided pair shows the present side's document lines as source in the
selected medium, beside **Not present on this side**, never as additions or
removals.

The diff viewer renders transported annotations as row decorations; this is
a prerequisite adoption step in the diff viewer (DV4 below), not a
redefinition of its rows.

## Availability, lifetime, and retention

The mode is available for a destination when every present endpoint is a
Member the Annotated Source member document query accepts. Compare Explore
issues the destination's modes accordingly.

The mode starts its request when it first becomes active, under Operation
Authority, for the exact destination, the retained Package model, and the
decompiler style. Closing the viewer supersedes a pending request, and a late
completion publishes nothing. A settled Compared, One-sided, or Unavailable
result is retained for the life of the retained Package model under the
request identity, so reopening Explore shows it without running again; a
Failed, Too complex, or Canceled result is not retained. Switching medium
within a settled result runs nothing.

## Non-claims

This design does not claim:

- a cross-version C# node correspondence, or that equal decompiled text
  means equal behavior;
- that a Before Finding and an After Finding are the same Finding;
- that the decompiled diff replaces the authored Source diff;
- IL-to-C# correspondence across the two sides; or
- a Browser-side matcher.

## Adoption

| Step | Delivers | Production host |
| --- | --- | --- |
| DD1 | The per-version pair and C# comparison in shared Presentation, a Browser Source-facade export over two package scopes following the authored-Source comparison pattern, transport adoption, and the Decompiler mode with the C# medium | Compare Explore's Decompiler mode |
| DV4 | Diff viewer rendering of transported annotations as row decorations | The same mode |
| DD2 | Finding annotations lowered onto the diff, and the IL medium | The same mode |
| DD3 | A CLI consumer of the same Presentation pair, a Member-level decompiled diff section | The CLI `diff` command |

DD1 lands with a pinned real-package pair whose Member's decompiled body
changed between versions. The Browser export and the CLI section consume one
shared Presentation pair, so neither host owns comparison logic.

## Acceptance scenarios

1. Open Explore for a changed method Member and switch to **Decompiler**;
   confirm one request resolves both sides independently and shows the C#
   diff in walk order, with **Mark** as the whitespace default.
2. Switch to **IL** and confirm no request runs and only IL lines appear.
3. Confirm each side's Findings appear on that side's lines, that a Finding
   without a node in the medium appears in the side summary, and that
   activating one opens that side's Annotated Source at the Finding.
4. Open the mode for an added Member and confirm the present side shows as
   source beside **Not present on this side**.
5. Make one side's query fail and confirm the other side's document remains
   available with the failure shown on the failed side.
6. Change the decompiler style, reopen Explore, and confirm a new pair runs
   rather than showing the result for the old style.
7. Close and reopen Explore and confirm a settled Compared result shows
   without running again.
