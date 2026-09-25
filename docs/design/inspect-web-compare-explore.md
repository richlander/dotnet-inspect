# Inspect Web Compare Explore

## Status and owner

This document owns the Member Diff **Explore** destination of the Browser
Compare inspector: stage 9 of the adoption path in
[Inspect Web Compare Experience](inspect-web-compare-experience.md), under
the end-to-end tracker
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213).

Its normative claim is:

> Member Compare Diff issues one Explore destination for a relation that has
> at least one available comparison mode. Explore opens a full-bleed,
> transient viewer that shows one cross-version comparison of the exact
> Member at a time and switches between its available modes: the authored
> Source text diff and, when its owner issues it, the decompiler diff.
> Opening it changes no subject, lens, or history; closing it restores the
> same Compare state.

The inline Member Diff owned by the Compare Experience is the detailed-result
boundary: it shows what changed, the endpoints, and the authored Source diff.
Explore adds width and the evidence a text diff cannot show. It does not
repeat the inline sections.

The precedents are Annotated Source's **Explore**, an embedded reader whose
Explore opens a full-bleed modal viewer over the same product document, and
editor diff views that switch between a text diff and a structural view of
the same pair.

## Ownership and boundaries

This owner defines:

- when a Member relation carries an Explore destination and what that
  destination carries;
- the viewer's modes, their availability, their order, and the default mode;
- the association of every mode request with the exact Member relation, the
  two Diff baseline endpoints, and the retained Package model;
- mode-level available, identical, changed, unavailable, failed, and canceled
  states and how each is presented;
- the viewer's operation lifetime: when mode work starts, what supersedes it,
  and what closing disposes; and
- the Compare-local state restored on return.

It does not own:

- the Library API Diff document, its Member relations, or its classified
  changes ([Library API diff presentation](library-api-diff-presentation.md)
  and [Inspect Web Library API Diff](inspect-web-library-api-diff.md));
- Package Diff baseline selection
  ([Browser Diff targets](inspect-web-diff-targets.md));
- paired authored-Source acquisition, comparison, or transport
  ([Selected member source pair query](member-source-pair-query.md),
  [Inspect Web authored Source comparison](inspect-web-source-comparison.md),
  and [Inspect Web source-diff transport](inspect-web-source-diff-transport.md));
- text diff rows, modes, navigation, or presentation controls
  ([Inspect Web diff viewer interaction](inspect-web-diff-viewer-interaction.md));
- the decompiler diff's comparison, transport, or presentation, which a
  separate focused owner defines;
- the inline Member Diff composition, the placement of the Explore action, or
  the inline comparison's retention
  ([Inspect Web Compare Experience](inspect-web-compare-experience.md));
- modal focus, Escape, dismissal, and history rules
  ([Inspect Web Shell Interaction](inspect-web-shell-interaction.md));
- subject, lens, canonical location, or effect authority
  ([Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md)); or
- operation identity, cancellation, supersession, and publication
  ([Inspect Web Operation Authority](inspect-web-operation-authority.md)).

## Modes

| Mode | Evidence | Available when |
| --- | --- | --- |
| **Text** | The paired authored-Source comparison, shown in the full-bleed host of the diff viewer | Every present endpoint of the relation is a method anchor, the paired query's domain |
| **Decompiler** | The cross-version decompiled comparison issued by the decompiler diff owner | That owner issues it for the relation |

A mode is offered only when it is available. The viewer shows one mode at a
time, with a mode switch when more than one is available. **Text** is the
default when available. Switching modes keeps each mode's state for the life
of the open viewer.

A property, field, or event Member has no **Text** mode until an
accessor-level authored-Source comparison exists. A declaration comparison is
not a mode until the member declaration pair query exists; the viewer shows
no placeholder for it.

## Destination issuance

Explore is a product-issued destination, not a Browser inference. The
Browser wire projection of the Library API Diff document carries, on each
Member relation, one optional Explore destination:

- the two endpoint coordinates (package, version, framework, compile asset,
  and assembly identity) exactly as the document's Target and Current
  endpoints;
- the exact anchor to resolve on each present side (the relation's Before
  identity for the target endpoint, its After identity for the current
  endpoint), with the absent side marked absent;
- the destination kind, `member-diff`; and
- the modes available for the relation.

The destination is issued only when the relation has at least one present
side whose identity is an exact metadata member of its endpoint and at least
one mode is available. Until the decompiler diff owner issues its mode, that
means every present endpoint is a method anchor. A relation with a missing or
synthesized identity carries no destination. An added Member has a current
side only and a removed Member a target side only; each mode shows the
present side beside an explicit **Not present on this side** endpoint.

The Compare Experience places the **Explore** action. The Browser renders it
only for a settled Member Diff result that carries a destination, never as a
placeholder, a disabled action, or an action derived from display text.

## Viewer composition

The viewer is a full-bleed modal dialog under the shell's shared modal
semantics. Its accessible name is the Member's display and the baseline, for
example `Example.Widget.Run · 1.0.0 → 2.0.0`. Initial focus goes to the
viewer heading.

The header shows the subject, the baseline, the relation classification and
its change chips, the mode switch when more than one mode is available, and
the close action. The body is the active mode's content at full width and
height, with one vertical scroll owner and no page-level horizontal overflow.

**Text** mode is the full-bleed host of the
[diff viewer interaction](inspect-web-diff-viewer-interaction.md): every line
expanded, unified or side-by-side, change navigation, and the whitespace and
move controls. **Decompiler** mode is the decompiler diff owner's view.

Viewport changes never rerun a mode's request or change the active mode.

## Requests and lifetime

Opening the viewer is an explicit request for evidence. A mode starts its
request when it first becomes active, under Operation Authority, for the
exact destination and the retained Package model. Requests use the
destination's endpoint coordinates and anchors as submitted; none reads the
current inspector state, the version control, or display text.

**Text** mode uses the inline comparison's retained result when one exists
for the same request identity, as the Compare Experience defines it, instead
of running the comparison again. A **Text** result settled inside Explore is
retained under that same identity, so the inline section shows it too.

Only the current authorized operation may publish a mode. Changing the
Package Diff baseline, navigating away from the Member, replacing or removing
the Package model, or closing the viewer supersedes pending mode work;
cancellation is best-effort and a late completion publishes nothing. Closing
disposes the viewer's operations through the existing authority boundary.

A failed or canceled mode result is not retained and runs again when the mode
next becomes active. Retention is a snapshot of settled evidence, never a
cache consulted across Package models or baselines.

The viewer is transient. It creates no Navigation subject, lens, canonical
location, history entry, or Workspace packet. Refresh and shared links restore
the Member Compare surface, not the open viewer.

## Return

Closing follows the Compare Experience's Explore return rule: the same
subject, Compare lens, and retained mode; the same Member row; the inventory
scroll position; and focus on the invoking Explore action. If the settled Diff
result was replaced while the viewer was open, the Compare surface renders the
replacement and focuses its nearest surviving heading or persistent control,
and the viewer's stale mode results are not shown again.

## Failure and empty states

- A mode whose request is pending shows a loading state in the body.
- An unavailable endpoint, such as one without authored Source, shows the
  typed reason on that endpoint and keeps the other endpoint's evidence.
- A failed request shows the failure with a retry action for that mode only.
- A canceled request shows canceled; it does not present as identical or
  empty.
- Identical evidence shows as identical with both endpoints visible.
- The header and close action remain in every state.

## Non-claims

This design does not claim:

- that Explore is available for Library or Type Compare, or for Clone;
- the decompiler diff's comparison, transport, or presentation;
- that equal text implies API, C#, or IL equivalence;
- that the viewer is a routed surface or portable state;
- that the Browser may pair endpoints, match lines, or derive identities
  itself; or
- that any mode introduces a text diff representation other than the
  product's `AnalysisDiff<string>`, its Markout `MappedTextDiff` lowering,
  and the source-diff transport.

## Adoption

1. **Text-mode viewer.** Replace the three-pane viewer from #8491 with the
   full-bleed **Text** mode over the diff viewer's full-bleed host; remove the
   What changed and Declaration panes; narrow destination issuance to
   relations with an available mode; and share retained **Text** results with
   the inline section.
2. **Decompiler mode.** Add the mode switch and **Decompiler** mode when the
   decompiler diff owner issues it.

Each stage lands only when its own result and failure states are complete.
The #8491 relation-order renderer and its placeholder pane retire in stage 1.

## Acceptance scenarios

1. Open Member Compare Diff for a changed method Member and confirm Explore
   appears only after the result settles and carries a destination; activate
   it and confirm one full-bleed dialog opens in **Text** mode with the
   header, no What changed or Declaration pane, and that the subject, lens,
   URL, and history are unchanged.
2. Show the inline authored Source diff, then open Explore and confirm
   **Text** mode shows the same result without a second request; open
   Explore first on another Member and confirm the inline section then shows
   the retained result.
3. Confirm **Text** mode shows identical, changed, one-sided unavailable, and
   failed outcomes separately and never substitutes empty or decompiled text,
   and that the CLI's `-v:d` rendering of the same pair shows the same ranges
   and statistics.
4. Open Member Compare Diff for a changed property Member before the
   decompiler diff owner issues its mode, and confirm no Explore destination
   is issued.
5. Open Explore for an added Member and a removed Member and confirm the
   present side appears beside an explicit absent side.
6. Close with Escape and with the close action and confirm the same Member
   row, scroll position, mode, and focus on Explore are restored; change the
   Package Diff baseline while the viewer is open and confirm the viewer
   closes, its pending work publishes nothing, and Compare renders the
   replacement.
7. Open Explore at desktop and 390px widths and confirm one vertical scroll
   owner, no page-level horizontal overflow, and that a resize changes layout
   only.
8. After the decompiler diff owner issues its mode, confirm the mode switch
   appears, **Text** remains the default, and switching keeps each mode's
   state while the viewer is open.
