# Inspect Web Compare Explore

## Status and owner

This document owns the Member Diff **Explore** destination of the Browser
Compare inspector: stage 9 of the adoption path in
[Inspect Web Compare Experience](inspect-web-compare-experience.md), under
the end-to-end tracker
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213).

Its normative claim is:

> Member Compare Diff issues one Explore destination for a relation with an
> available authored Source comparison. Explore opens a full-bleed, transient
> viewer over that exact Member comparison. Opening it changes no subject,
> lens, or history; closing it restores the same Compare state.

The inline Member Diff owned by the Compare Experience is the detailed-result
boundary: it shows the classified changes and the compact authored Source diff.
Explore adds width and does not repeat the inline sections. The separately
owned [Member Body Diff](inspect-web-member-body-diff.md) may use the same
full-bleed shell to expand its retained C#/IL document; that expansion does not
add a second comparison mode to this Public API destination.

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
- Member Body inventory, comparison, or inline presentation
  ([Inspect Web Member Body Diff](inspect-web-member-body-diff.md));
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

The viewer offers Text only when it is available. The rail reports it as the
active evidence without rendering a one-item picker.

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
one mode is available, so every present endpoint is a method anchor. A
relation with a missing or synthesized identity carries no destination. An
added Member has a current side only and a removed Member a target side only;
Text shows the present side beside an explicit **Not present on this side**
endpoint.

The Compare Experience places the **Explore** action. The Browser renders it
only for a settled Member Diff result that carries a destination, never as a
placeholder, a disabled action, or an action derived from display text.

## Viewer composition

The viewer is a full-bleed modal dialog under the shell's shared modal
semantics and uses the
[code evidence viewer](inspect-web-code-evidence-viewer.md) frame. Its
accessible name is the Member's display and the baseline, for example
`Example.Widget.Run · 1.0.0 → 2.0.0`. Initial focus goes to the viewer
heading.

The header shows the subject, the baseline, the relation classification and
its change chips, and the close action. The center region is the active mode's
content and receives all width left after the bounded right rail. The rail
shows the active evidence, the mode picker when more than one mode is
available, and concise endpoint context and source destinations. Successful
text comparisons do not repeat endpoint cards or source text in the center.
Content and rail scroll independently, and neither the modal nor the page
becomes the horizontal scroll owner.

**Text** mode is the full-bleed host of the
[diff viewer interaction](inspect-web-diff-viewer-interaction.md): every line
expanded, unified or side-by-side, change navigation, and the whitespace and
move controls.

Viewport changes never rerun a mode's request or change the active mode.

## Requests and lifetime

Opening the viewer is an explicit request for evidence. A mode starts its
request when it first becomes active, under Operation Authority, for the
exact destination and the retained Package model. Requests use the
destination's endpoint coordinates and anchors as submitted; none reads the
current inspector state, the version control, or display text.

**Text** mode consumes the authored-Source comparison the Compare Experience
owns for the same request identity: it shows a settled result, attaches to a
pending one, and otherwise starts that comparison, so one identity never has
two comparisons in flight. Retention and sharing with the inline section are
the Compare Experience's rules.

Only the current authorized operation may publish a mode. Closing the viewer
supersedes the viewer's own pending mode work, and a late completion
publishes nothing; cancellation is best-effort, and closing disposes those
operations through the existing authority boundary. The **Text** comparison
is not the viewer's own work: it belongs to the Compare Experience, and
closing the viewer neither supersedes nor disposes it. While the viewer is
open it is modal, so the Package Diff baseline, the Member, and the Package
model cannot change underneath it.

A failed or canceled mode result is not retained and runs again when the mode
next becomes active.

The viewer is transient. It creates no Navigation subject, lens, canonical
location, history entry, or Workspace packet. Refresh and shared links restore
the Member Compare surface, not the open viewer.

## Return

Closing follows the Compare Experience's Explore return rule: the same
subject, Compare lens, and retained mode; the same Member row; the inventory
scroll position; and focus on the invoking Explore action.

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
- Member Body's comparison, transport, or presentation;
- that equal text implies API, C#, or IL equivalence;
- that the viewer is a routed surface or portable state;
- that the Browser may pair endpoints, match lines, or derive identities
  itself; or
- that any mode introduces a text diff representation other than the
  product's `AnalysisDiff<string>`, its Markout `MappedTextDiff` lowering,
  and the source-diff transport.

## Adoption

1. **Text-mode viewer.** Replace the three-pane viewer from #8491 with the
   full-bleed **Text** mode in the code-evidence shell; remove the What changed
   and Declaration panes; place the mapped-text viewer in the center and the
   active evidence plus endpoint context in the rail; narrow destination
   issuance to relations with an available mode, recorded in the Library API
   Diff wire owner's retained-result inventory and bounds; and consume the
   Compare Experience's shared **Text** comparison.

The stage lands only when its result and failure states are complete. The
relation-order renderer from #8491 and its placeholder pane retire in this
stage. Member Body's optional expansion adopts the settled shell under its own
focused design.

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
4. Open Member Compare Diff for a changed property Member and confirm no
   authored Source Explore destination is issued.
5. Open Explore for an added Member and a removed Member and confirm the
   present side appears beside an explicit absent side.
6. Close with Escape and with the close action and confirm the same Member
   row, scroll position, mode, and focus on Explore are restored.
7. Open Explore at desktop and 390px widths and confirm one vertical scroll
   owner per content and rail region, no page-level horizontal overflow, the
   rail stacks after content at the shared narrow boundary, and that a resize
   changes layout only.
8. Expand a settled Member Body result and confirm the same shell presents the
   retained C#/IL document without adding an authored Source mode or starting a
   second comparison.
