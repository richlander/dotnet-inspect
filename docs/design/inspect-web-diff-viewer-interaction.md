# Inspect Web diff viewer interaction

## Status and ownership

This document owns the Diff viewer interaction tracked by
[#5686](https://github.com/richlander/dotnet-inspect/issues/5686), milestone 6
of the source-diff adoption path in
[Inspect Web source-diff transport](inspect-web-source-diff-transport.md#purpose-and-delivery),
under the Compare tracker
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213).

Its normative claim is:

> Inspect Web renders one transported mapped text diff as rows in the
> sequence order of its Before and After lines, in unified and side-by-side
> modes, with deterministic change navigation, bounded row windows, and
> presentation controls for whitespace-only changes and moves, without
> browser-side matching, re-splitting, or reordering.

The viewer is one component with two hosts: an embedded reader on a page and a
full-bleed reader. Both show the same rows from the same payload; the
full-bleed host adds width, not evidence.

This owner defines:

- the row model and its derivation from the transported diff;
- unified and side-by-side modes and the responsive fallback;
- intraline highlighting, the final-line-terminator marker, and copying;
- context collapse and bounded row windows;
- Previous and Next change navigation, its position, keys, and announcements;
- the whitespace and move presentation controls, their defaults, and their
  disclosure; and
- the identical, unavailable, rejected, failed, canceled, and too-complex
  frames.

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Inspect Web source-diff transport](inspect-web-source-diff-transport.md) | The complete, bounded, typed payload: sequences with terminator assertions, relations, statistics, mapped changes with inner mappings and annotations, provenance, and optional Open destinations |
| [Text whitespace characterization](text-whitespace-characterization.md) | Whitespace-only and moved change outcomes, move ids and ends, whitespace edits, and the [whitespace policy](text-whitespace-characterization.md#whitespace-policy) defaults, delivered by its transport slice S4 |
| Markout [`MappedTextDiff`](https://github.com/richlander/markout/blob/main/docs/design/mapped-text-diff.md) | Ordered, non-overlapping changes; equal-cardinality unchanged gaps; single-line inner-mapping spans in UTF-16 units |
| [Surface composition](inspect-web-surface-composition.md) | Placement of the viewer and its action region on the Member Diff surface |
| [Inspect Web Compare Experience](inspect-web-compare-experience.md) and [Inspect Web Compare Explore](inspect-web-compare-explore.md) | Placement on Member Compare and in the full-bleed Explore destination |
| [Inspect Web Shell Interaction](inspect-web-shell-interaction.md) | Full-bleed modal semantics, Escape, and focus return |

It does not own source acquisition, comparison, correspondence, payload
admission, page placement, which diff a page shows, or a decompiler diff.

## Why

The Member Diff Explore viewer from #8491 renders by walking the analytical
relation population. That population is stored in
[canonical order](analysis-diff.md#ordering-and-value-semantics), which sorts
relations that hold Before lines first and every addition last, and which the
Analysis diff owner states is not a presentation order. The result puts an
added line after the end of the member. A probe of that renderer with Before
`a`, `c` and After `a`, `b`, `c` produces the rows `a`, `a`, `c`, `c`, `+b`:
each unchanged line appears twice, and the addition appears last.

The mapped diff already carries presentation order. Its changes are ordered
and non-overlapping, and the lines between them are equal-cardinality
unchanged gaps. Walking it by position is the conventional unified diff, the
same walk Markout's own display lowering performs. The analytical relations
remain evidence about each line, not a layout.

## Row model

The viewer derives rows by one positional walk of the mapped diff, in order:

1. Each unchanged gap contributes one **context** row per line pair, carrying
   both side coordinates.
2. Each change contributes its **removal** rows for its Before range, then its
   **addition** rows for its After range.

Every Before and every After line appears in exactly one row, and rows follow
both sequences' order. The walk reads only mapped ranges and sequence lines.
It never matches lines, compares text, re-splits, reorders, or zips an N:M
correspondence.

Relation facts decorate rows; they never place them. A row carries the facts
of the relation that holds its line: changed or unchanged content, stable or
moved placement. Facts from the whitespace characterization decorate the
rows of their change: whitespace-only, moved with its move id and end, or
changed. Each characterized change lowers to exactly one transported mapped
change, so a mapped change is never a whole region of several characterized
changes.

Unified mode shows the rows in walk order with two line-number columns and a
marker column: a space for context, `−` for removal, `+` for addition.
A context row's two lines need not be equal text: the mapped diff does not
compare unchanged-gap text, and a whitespace-only change shown as context has
different text on each side. Unified mode prints a context row's Before line,
as `diff -w` does. Side-by-side mode prints each side's own line, so a column
never shows text its sequence does not hold.

Side-by-side mode shows each context row on both sides and aligns each
change's removal and addition blocks at their tops, filling the shorter block
with empty cells, as Markout's side-by-side lowering does. Side-by-side never
pairs a removal with an addition line to imply correspondence the producer
did not issue.

## Intraline, terminator, and copying

An inner mapping highlights its exact span on its side's line. Spans are
UTF-16 code units, single-line, and monotone within a change. A span that
falls outside its line or overlaps a previous span is a decoding defect: the
viewer shows the change without highlighting and reports the defect in the
frame, never skipping it silently.

A side whose final line terminator is asserted absent shows an accessible
visible marker after its last line, labeled **No newline at end**; when both
sides end in the same context row, the row shows one marker. When that line
lies in a collapsed run, the run's omission row states that the side ends
without a newline. A side whose assertion is unknown shows no marker.

Line numbers, markers, and decorations are outside the selectable text, so a
selection copies source text only. **Copy Before** and **Copy After** assemble
one side's complete text from its sequence and terminator assertion by
literal joining, as the transport owner specifies.

## Context and windows

The embedded host collapses unchanged runs to three lines of context around
each visible change. A collapsed run becomes one omission row that names its
Before and After line ranges and expands in place, twenty lines at a time or
completely. The full-bleed host starts with every line expanded. Expansion is
viewer-local state for the current diff.

The viewer renders a bounded window of rows around the viewport, at most 400
rows beyond it in each direction, in every range, including a long changed
range. The transport already bounds each side to 1,024 lines; windowing keeps
layout cost proportional to the viewport, not to the diff.

## Change navigation

The navigable changes are the visible changes in walk order. A change hidden
by the whitespace control, or rendered as context under **Hide**, is not
navigable. **Previous** and **Next** move to
the adjacent navigable change, scroll its first row into view, and move focus
to it; they are disabled, not wrapped, at either end. The position reads
`3 of 7`. With no navigable change, both are disabled and the position reads
`No changes`. Keys `n` and `p` do the same while focus is inside the viewer
and not in a text field. Each move announces the position and the change's
line ranges through a polite live region.

## Whitespace presentation

When the payload carries whitespace facts, the viewer offers one control with
three choices:

- **Mark:** every whitespace-only change is shown in a lighter shade of its
  removal or addition color with a `whitespace-only: <kinds>` label.
- **Highlight:** Mark, plus each whitespace edit drawn from the transported
  edit facts, as the characterization's lowering describes:
  - the part of an edit that lies within one line is highlighted as a span,
    with spaces and tabs drawn as `·` and `→`, and literal `·`, `→`, and
    backslash escaped, as Markout's rich formats do;
  - an edit that crosses lines is drawn as its segment on each line plus a
    visible line-break marker where a boundary was added or removed;
  - an edit empty on one side is drawn on that side as a visible insertion
    point; and
  - a whitespace-only change that only adds or removes lines, such as blank
    lines, is marked at line level.
- **Hide:** the insensitive policy's Suppress mode, with the policy's test. A
  whitespace-only change whose sides have the same nonzero number of lines
  and no `LineBreaks` or `FinalLineTerminator` edit renders as context; every
  other whitespace-only change is hidden, with an omission row stating how
  many whitespace-only lines it hides on each side.

The decoding-defect rule for spans applies to transported inner mappings, not
to producer edit facts, whose ranges may legitimately span lines or be empty.

Every Inspect Web comparison defaults to **Mark**. Authored → authored
comparisons are sensitive by the whitespace policy, and the browser presents
the PDB and decompiled member comparison sensitively by the policy's
recorded exception. Choosing
**Hide** is explicit and applies to the current diff only; it is never
retained across diffs, so a new comparison never opens with differences
hidden. **Mark** and **Highlight** are retained as a preference for the page
session, and a new diff opens in the last of the two that was chosen.

Under **Hide**, the summary states, per side, every whitespace-only line it
suppresses, whether rendered as context or hidden, as the policy's two-sided
insensitive summary requires. When every change of the diff is whitespace-only,
the frame reads **No differences except whitespace**, never **Identical**,
even when every such change renders as context and no row is hidden. A color
shade is always paired with a label or glyphs, never the only cue.

Without whitespace facts the control is absent, and the viewer shows the
changes as the mapped diff issues them.

## Move presentation

When the payload carries move facts, the viewer offers **Linked** and
**Plain**:

- **Linked:** each end of a move shows its label, such as `move 3 to +42` or
  `move 3 from −12`. Hovering or focusing either end highlights both.
  Activating the label scrolls to the partner end, moves focus there, and
  announces it.
- **Plain:** moved changes render as ordinary removals and additions.

A move also carries its producer-issued content fact. Under **Mark** and
**Highlight**, when the content is whitespace-only, each end's label and
accessible label add `whitespace-only: <kinds>`, as in
`move 1 from −1; whitespace-only: indentation`, and **Highlight** draws the
move's transported edits on both ends; when the content changed, the label
adds `changed`. Under **Hide**, the whitespace-only content fact is omitted
and the move stays a move, as the whitespace policy states.

**Linked** is the default. The choice is retained for the page session. Move
ids are the producer's ids; the viewer never assigns or renumbers them.

## Frames and outcomes

Every outcome keeps one frame with both endpoint labels and the statistics
the payload carries, extended by whitespace-only lines and moved blocks when
the facts are present.

| Outcome | Frame content |
| --- | --- |
| Complete with changes | Rows and controls |
| Identical | **Identical**, both endpoints, no rows and no navigation |
| Unavailable | The typed reason on each unavailable endpoint, keeping the available endpoint's label |
| Rejected or failed | The typed failure, with retry where the host offers it |
| Canceled | **Canceled**, never an empty or identical diff |
| Too complex | The transport's refusal, with no partial rows |

Controls that do not apply to an outcome are absent, not disabled. A new
outcome never keeps controls or rows from a previous one.

## Hosts

The embedded host shows the viewer inside a page section with collapsed
context and a compact control row that includes mode selection. The
full-bleed host shows it expanded. Mode preference, unified or side-by-side,
is retained for the page session and shared by every host; below 720 CSS
pixels of viewer width, side-by-side falls back to unified without changing
the stored preference. Viewer state is transient: it is not part of the URL,
browser history, the Workspace, or a share packet.

A host may place the viewer's mode, navigation, and **Open Before** and
**Open After** controls in its own action region, as Surface composition's
Member Diff surface does for the later PDB and decompiled adopter; the
controls keep this owner's semantics wherever they appear. **Open Before**
and **Open After** use only the destinations the transport carries and are
absent when it carries none.

Placement owners decide where each host appears and which payload it shows.
This owner adds no page-level scroller or horizontal overflow; long lines
scroll inside the viewer.

## Accessibility

Rows form a table. Each row's line-number cells are its row headers, and a
windowed row carries its position in the complete row population through
`aria-rowindex` against the table's `aria-rowcount`, so assistive technology
reports true positions. Marker glyphs are hidden from assistive technology,
and each removal or addition row carries a text label, **Removed** or
**Added**, with its decorations, such as **whitespace-only** or **move 3**.
Controls are native buttons or radio groups with visible focus. Every pointer
gesture has a keyboard equivalent.

## Non-claims

This design does not claim:

- a decompiler, IL, or structural diff; the full-bleed Explore destination's
  decompiler mode belongs to a separate owner;
- that equal line text or the absence of rows implies API, C#, or IL
  equivalence;
- that the viewer may compute, repair, or reorder correspondence, statistics,
  or whitespace facts;
- that hidden whitespace is insignificant; or
- persistent Settings for these preferences.

## Adoption

| Step | Delivers | Production host |
| --- | --- | --- |
| DV1 | Row walk, unified mode, intraline highlighting, terminator marker, copying, navigation, frames; replaces the Explore relation renderer | The Member Diff Explore Authored Source pane, then the inline Member Compare section when the Compare Experience adopts it |
| DV2 | Side-by-side mode, responsive fallback, context collapse, row windows | The same hosts |
| DV3 | Whitespace and move controls, after transport slice S4 carries the facts | The same hosts |

DV1 fixes the ordering and duplication defects in the shipped viewer and
lands with a pinned real-package pair whose member gains a line in the middle
of its body. DV3 lands with Scrutor 4.2.2 → 5.0.0 `Decorate`, whose authored
Source changed only in whitespace.

## Precedent

- GitHub's pull request diff collapses context into expandable rows, offers
  unified and split views, and hides whitespace on request with a visible
  notice.
- VS Code's diff editor aligns changed blocks at their tops with empty space
  in side-by-side mode and navigates changes with Previous and Next.
- `git diff --color-moved` distinguishes moved blocks from ordinary changes.

None of them reorders a diff by relation or content, and this viewer does not
either.

## Acceptance scenarios

1. Render Before `a`, `c` and After `a`, `b`, `c` and confirm the rows `a`,
   `+b`, `c`, each line once, in both modes.
2. Render a change of two removals and three additions and confirm unified
   order and top-aligned side-by-side blocks with one empty Before cell.
3. Navigate a diff with three changes by button and by `n` and `p`, and
   confirm position, focus, announcements, and disabled ends.
4. Collapse and expand a long unchanged run in the embedded host, and confirm
   the full-bleed host starts expanded.
5. With whitespace facts, confirm the Mark default and Highlight glyphs and
   escapes. Under Hide on Scrutor `Decorate`, whose one whitespace-only change
   renders as context, confirm the summary counts its suppressed line on each
   side and the frame reads **No differences except whitespace**; open another
   comparison and confirm Hide was not retained.
6. Under Highlight, confirm a brace reflow (`class Foo {` → `class Foo` /
   `{`) shows its segments and a line-break marker, and a removed blank line
   is marked at line level. Under Hide in side-by-side mode, confirm a
   re-indented line shows each side's own text in its column.
7. With move facts, confirm linked labels, partner highlighting, and scrolling
   to the partner; switch to Plain and confirm ordinary rows.
   Repeat with a relocated and re-indented block, and confirm both ends read
   `whitespace-only: indentation` under Mark, draw the edits under Highlight,
   and drop the content fact under Hide while remaining a move.
8. Confirm each outcome frame, and that a canceled or too-complex result never
   shows rows or **Identical**.
9. At 390 pixels, confirm side-by-side falls back to unified, the stored mode
   survives a return to a wide viewport, and no page-level horizontal
   scrolling appears.
