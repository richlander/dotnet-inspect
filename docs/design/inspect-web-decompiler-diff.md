# Inspect Web decompiler diff

## Status and ownership

This document owns the **Decompiler** mode of the
[Inspect Web Compare Explore](inspect-web-compare-explore.md) destination: the
Browser presentation of one
[Annotated Source diff document](annotated-source-diff-document.md), under
the Compare tracker
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213).

Its normative claim is:

> The Decompiler mode presents one Annotated Source diff document as a
> decompiled text diff in the diff viewer, C# or IL, with the document's fact
> changes as code lenses on the lines they are about. It presents the
> document and computes nothing: no comparison, no correspondence, and no
> fact pairing.

A diff is a representation and a presentation. The Annotated Source diff
document is the representation, and this mode is one presentation of it.

This owner defines:

- the payload the mode needs from the document's Browser export;
- the medium switch and its default;
- code lenses: which fact pairs show, their text, their placement, and their
  detail;
- the mode's outcomes; and
- the mode's lifetime and retention, which Compare Explore delegates.

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Annotated Source diff document](annotated-source-diff-document.md) | Sides and their outcomes, per-medium text comparisons and Too complex outcomes, line maps, and the fact comparison |
| [Inspect Web Compare Explore](inspect-web-compare-explore.md) | The Member diff destination, mode availability, and the full-bleed viewer |
| [Inspect Web diff viewer interaction](inspect-web-diff-viewer-interaction.md) | Rows, modes, navigation, and whitespace and move controls |
| [Inspect Web source-diff transport](inspect-web-source-diff-transport.md) | The bounded line-diff payload shape and its admission |
| [Member source diff presentation](member-source-diff-presentation.md) | The shared Presentation lowering of a text comparison to a Markout `MappedTextDiff` |
| [Operation Authority](inspect-web-operation-authority.md) | Operation identity, cancellation, supersession, and publication |

## Why

A decompiled text diff shows which lines changed. The Annotated Source diff
document also says what those changes mean to the runtime: an allocation or
a throw added, a call target changed. A code lens puts that meaning on the
line it is about, the way an editor shows references or test status above a
declaration, without mixing it into the text.

## Payload

The document's Browser export (the document's adoption step ADD3) delivers
one payload per destination, built in shared Presentation from the document,
so the Browser neither compares nor lowers:

- the two sides' outcomes and endpoint provenance;
- per medium, either the source-diff transport payload lowered from that
  medium's text comparison, or that medium's Too complex outcome; and
- the lens list: every fact pair from the document's fact comparison, each
  with its pair kind, descriptor, category, conditionality, the detail on
  each side where present, and, for each side it has a fact on, that fact's
  compared lines in each medium, resolved through the document's targets and
  line maps; and, for a side with no fact comparison because the other side
  is not Present, that side's own facts with their lines.

A side's lines in a medium are the sequence lines of that side, so a lens
lands on rows through the coordinates the diff viewer already carries. The
payload is admitted with the source-diff transport's limits per medium, plus
a limit of 512 lenses; a document with more lenses shows its diff and states
that lenses were omitted, never dropping some of them silently.

## Code lenses

A lens is a short line above the first row of its lines, on the side it
describes:

| Pair | Side | Lens text |
| --- | --- | --- |
| Added | After | `<category> added · <detail>`, such as `allocation added · List<int>` |
| Removed | Before | `<category> removed · <detail>` |
| Changed | Both | `<category> changed · <before detail> → <after detail>` |
| Present | Both | Hidden by default; shown by **Show unchanged facts** as `<category> · <detail>` |

A conditional fact adds its conditionality, as in **throw added · conditional
· ArgumentException**. A lens whose fact has no line in the displayed medium
is listed in the mode's lens summary instead of on a row. A fact about the
Member header rather than its body appears in the lens summary.

Activating a lens opens its detail inline, below the lens: the pair kind,
descriptor, category, conditionality, each side's detail, and, for a Changed
pair, the match that joined it. The detail offers no navigation; opening a
side's Annotated Source from the diff is not part of this design.

The lens summary heads the mode: counts of added, removed, and changed facts
by category, such as **2 allocations added · 1 throw added**, with each entry
moving focus to its first lens.

Lenses are row decorations. The diff viewer renders caller-supplied
decorations on side lines through its row model; that rendering is a
prerequisite amendment to the diff viewer, recorded in the adoption steps
below.

## Medium and whitespace

A **C# | IL** switch selects the medium; C# is the default. Both media are in
one payload, so switching runs nothing. A medium with a Too complex outcome
shows that outcome in place of its diff. The diff viewer's whitespace control
defaults to **Mark**, since both sides are printer output.

## Outcomes

| Document | Mode shows |
| --- | --- |
| Both sides Present | The diff and lenses |
| One side Absent | The present side as source in the selected medium, with its own facts as plain lenses, `<category> · <detail>`, since the document holds no fact comparison, beside **Not present on this side** |
| Unavailable, NotApplicable, or Failed side | That side's typed reason; a present other side as source |
| Query failed or canceled | The failure or **Canceled**, never an empty diff |

## Lifetime and retention

The mode starts its request when it first becomes active, under Operation
Authority, for the exact destination and the retained Package model. Closing
the viewer supersedes a pending request, and a late completion publishes
nothing. A settled result other than a failure or cancellation is retained
for the retained Package model's life under the destination's request
identity, so reopening Explore shows it without running again.

## Non-claims

This design does not claim:

- any comparison, correspondence, or fact pairing of its own;
- that a lens's fact is the same runtime occurrence on both sides;
- navigation from a lens to Annotated Source; or
- a CLI presentation, which the document's own hosts define.

## Adoption

| Step | Delivers | Production host |
| --- | --- | --- |
| DV4 | Diff viewer amendment: caller-supplied decorations on side lines | Compare Explore's Decompiler mode |
| DD1 | The Decompiler mode over the document's Browser export: medium switch, outcomes, lifetime | The same mode |
| DD2 | Code lenses and the lens summary | The same mode |

DD1 follows the document's ADD1 and ADD3; DD2 follows ADD2. DD2 lands with a
pinned real-package pair whose Member gains an allocation or a throw.

## Acceptance scenarios

1. Open Explore for a changed method Member, switch to **Decompiler**, and
   confirm the C# diff with **Mark** whitespace; switch to **IL** and confirm
   no request runs.
2. For a version that adds an allocation, confirm one **allocation added**
   lens on the After row that holds it, and a summary entry that moves focus
   to it.
3. For a Changed pair, confirm one lens on each side with both details.
4. Confirm Present facts are hidden until **Show unchanged facts**.
5. Confirm a header fact and a fact without a line in the displayed medium
   appear in the lens summary, not on a row.
6. For an added Member, confirm the present side shows as source with its
   facts as plain lenses beside **Not present on this side**.
7. With a Too complex IL medium, confirm the C# diff and lenses still show.
8. Close and reopen Explore and confirm a settled result shows without
   running again.
