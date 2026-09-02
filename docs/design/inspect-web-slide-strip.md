# Inspect Web SlideStrip

This document owns `SlideStrip`, a reusable Inspect Web control for presenting
one finite ordered inventory in a single horizontal region. A strip selects
one whole-strip representation mode, exposes a contiguous window of consistently
represented items, and slides that window as capacity or navigation changes.
It preserves item identity and focus across representation and window changes.

`SlideStrip` is the first focused reusable control in the Inspect Web UI. New
surfaces should adopt shared controls when their behavior matches an existing
contract instead of creating a visually similar custom implementation.
Similarity alone is not conformance: each adopter still owns its semantic
roles, navigation, activation, selection, and composition with adjacent
controls.

## Ownership and boundaries

This owner defines:

- the ordered item-presentation contract;
- the Label, Short Label, Icon, and Index representation slots;
- policy-controlled whole-strip fallback between viable modes;
- deterministic contiguous windows over any finite item inventory;
- capacity handling, edge-availability disclosure, and focused-item reveal;
- focus and accessible-name preservation across mode and window changes; and
- styling slots that allow adopters to distinguish compatible uses without
  forking allocation behavior.

It does not own:

- product identity, descriptor construction, or item ordering;
- ARIA roles such as tab, menuitem, option, or button;
- keyboard navigation, activation, selection, or disabled-state semantics;
- width allocation between multiple `SlideStrip` instances;
- application-shell placement, responsive page composition, or persistence;
- virtualization, remote data acquisition, or incremental inventory loading;
  or
- a universal component framework for every Inspect Web control.

The adopter remains responsible for those semantics and supplies only
owner-issued values. `SlideStrip` never derives identity or behavior from
rendered label text.

## Item contract

One strip receives an ordered finite inventory. The contract imposes no
product-specific item-count limit; the caller remains responsible for
constructing and retaining the finite inventory for one installed
presentation.

Every item supplies:

- one opaque stable identity;
- one required complete label, which remains the item's accessible name;
- an optional short label;
- an optional Unicode icon with decorative or labelled treatment chosen by the
  adopter's semantic control; and
- styling tokens or slots that do not change identity or representation
  order.

Logically, the control asks an adopter-supplied representation resolver for
Label, Short Label, Icon, or Index content for each item. The concrete
implementation may use fields or a callback, but it preserves that result
contract. Label must return the complete label. Short Label and Icon may return
no value. For Index, `SlideStrip` supplies the item's one-based owner-order
ordinal and the adopter styles it, for example as `[2]`. Index is presentation
derived from the current installed order; it is never stable identity.

The complete label is available to accessibility APIs and as focused or hover
disclosure regardless of the visible mode. Short Label, Icon, and Index are
presentation only. They are never submitted as action identity, parsed back
into one, or used to distinguish otherwise identical items.

An item without a short label or icon simply omits that representation. The
control does not synthesize initials, abbreviations, or icons. Label and Index
remain available without those optional values.

## Representation policy

An adopter supplies one finite preferred-to-minimum list of presentation modes.
Each mode contains:

- one representation kind: Label, Short Label, Icon, or Index;
- a positive minimum visible-item count; and
- the normal interactive sizing and between-item decoration for that mode.

Each representation kind occurs at most once. Label must occur exactly once
and is always viable. Index is viable whenever the adopter includes it. A
Short Label or Icon mode is viable only when every installed item supplies
that value. If even one item omits it, the control skips the whole mode instead
of mixing representations within one window. A mode's requested count is
clamped to the installed inventory count.

The control rejects policy construction when Label is absent or duplicated, a
kind is duplicated, a requested count is not positive, or an item fails to
return its required Label. It does not silently invent a usable policy.

The minimum visible-item count expresses the adopter's density preference. A
subject policy can require only one Label item, preserving a complete label as
capacity falls. An inspector policy can require two Label items and then prefer
a Short Label, Icon, or Index mode that keeps multiple inspectors visible.

The most-preferred viable mode remains installed while it meets its requested
visible count. Its visible count becomes the comparison baseline when it fails.
The first less-preferred viable mode that meets its own requested count and
admits more items than that baseline replaces it. This prevents an early
compact-mode transition while the preferred window remains useful and prevents
a wide short label or icon from causing a mode change with no density benefit.

The policy also supplies:

- a deterministic initial window anchor;
- the preferred owner-order direction for equal-ranked window placements;
- a window-continuity key; and
- the normal focused-item alignment when one item is wider than the viewport.

The anchor may use adopter-owned state such as an active identity. When the
adopter has no active item, it supplies another explicit owner-order origin
rather than asking `SlideStrip` to infer selection from focus.

## Presentation states

`SlideStrip` computes one deterministic state from the installed inventory,
viable modes, allocation, retained window, and focus:

1. An empty inventory has one empty state, measures zero item-content width,
   has no edge indicators or focus target, and ignores slide requests.
2. For each viable mode, the control enumerates contiguous owner-order windows
   whose item and decoration widths fit at normal interactive size. Edge
   indicators overlay the corresponding viewport boundary and consume no
   additional allocation.
3. During an adopter navigation transaction, candidate windows must contain
   its pending destination identity. Otherwise, if an item owns focus, they
   must contain it. On initial or reset placement with neither input, they must
   contain the policy's initial anchor.
4. Within each mode, the control first maximizes visible item count, then
   minimizes movement from the retained leading identity, then uses the
   policy's preferred direction as the final tie-break.
5. If the most-preferred viable mode meets its requested count, it is selected.
   Otherwise, the control selects the first less-preferred viable mode whose
   window meets its requested count and exposes more items than the failed
   preferred mode's visible-count baseline.
6. If no mode qualifies, it selects the viable mode with the greatest visible
   count, breaking ties toward the more-preferred mode.
7. If no normal-sized window fits, the control creates one fallback singleton
   containing the pending navigation destination, otherwise the focused item,
   otherwise the retained leading identity, otherwise the initial anchor. It
   uses the mode selected by step 6; when every mode has zero fitting items,
   the preference tie-break selects Label. The item remains normal-sized, may
   be clipped by the viewport, and is aligned by the policy. Overlaid edge
   indicators still disclose hidden inventory without reducing its visible
   portion.
8. Within the selected mode, widening adds adjacent items one at a time until
   the complete inventory is visible; narrowing removes edge items one at a
   time without mixing modes.

Every non-empty state therefore has one representation mode and one non-empty
contiguous owner-order window. A state never reorders items, combines Label
with Index or another mode, changes item semantics, or shrinks normal
interactive size.

The window-continuity key decides whether a retained leading identity survives
a new inventory or measurement plan. When the key is unchanged and that
identity remains installed, the control uses it in the ranking above. A
successful slide request updates it to the resulting window's leading
identity. If the identity is removed or the key changes, the control discards
it and places a new window around the adopter's current initial anchor. Width
alone does not reset the retained identity.

## Capacity and sliding

The strip remains one non-wrapping horizontal region.

The visible window initially contains the policy anchor. Adopter navigation to
a hidden item supplies a transient pending destination and commits one atomic
transaction: compute and install its window or fallback singleton, transfer the
sole roving tab stop and focus, then allow the previous focused item to become
hidden. During that transaction the pending destination outranks current-focus
containment; outside it, the window never hides its focused item. The browser
never observes focus on an unmounted target, an intermediate second tab stop,
or document-body fallback. Focused-item visibility otherwise outranks the
retained leading identity and any active anchor. When the focused item is wider
than the viewport, the strip aligns the nearest edge needed to maximize its
visible portion rather than shrinking it.

A slide-before or slide-after request moves the contiguous window by one
owner-order position when hidden inventory exists in that direction. Sliding
does not select or activate an item. When the requested movement would hide the
focused item, the newly revealed item at the movement edge becomes the pending
destination and the atomic reveal-tab-stop-focus transaction applies. The
adopter owns keyboard meaning and may issue slide requests after its existing
arrow-key navigation resolves a destination; pointer, touch, trackpad, or wheel
handling uses the same focus-preserving operation.

The strip exposes leading and trailing edge-availability states. Their
overlaid visual treatment may be a vertical highlight or fade, but the
indicators are not inventory items, identities, selections, or independent tab
stops. A leading indicator is present exactly when an earlier item is hidden; a
trailing indicator is present exactly when a later item is hidden. Both appear
for an interior window.

`Slideable` refers to discrete movement of that contiguous window and to
whole-strip mode changes at semantic capacity boundaries. It does not imply
pointer dragging, inertial pane resizing, a freely mixed representation row,
or persisted pixel width.

## Focus and replacement

Changing the whole-strip representation or moving the visible window does not
replace an item's semantic control. If an implementation must replace DOM, it
restores the same opaque item identity before the browser can fall back to the
document body.

The strip preserves:

- the focused item;
- the adopter-owned selected or current state;
- the adopter-owned roving tab stop or equivalent navigation state; and
- the window position needed to keep the restored focus visible.

Selection and focus remain distinct. Sliding to reveal a focused item does not
select or activate it.

The adopter owns focus transfer when the entire strip is removed or replaced
by a different semantic control. `SlideStrip` owns focus only while the same
strip and item identity remain installed.

Representation animation is optional. It must not replace the focused control,
delay accessibility state, or change the final presentation state, and it is
omitted when reduced motion is requested.

## Styling and composition

The control exposes styling hooks for:

- the strip region and window viewport;
- each semantic item;
- the Label, Short Label, Icon, and Index representations;
- the leading and trailing edge indicators;
- current, selected, focused, unavailable, and disabled states supplied by the
  adopter; and
- leading, trailing, or between-item decoration that does not participate as
  inventory identity.

Styling may change color, typography, spacing, borders, and representation
content supplied through the named slots, and those choices participate in
capacity measurement. Styling may not reorder items, hide a policy-selected
representation, create a second focus target for one item, or shrink a control
below the policy's normal interactive sizing to manufacture capacity.

Multiple strips may be composed by an owner that allocates width between them.
The composer owns that negotiation and any cross-strip controls. Each
`SlideStrip` independently owns mode selection and its contiguous window within
the width it receives.

For discrete composition, a strip exposes the normal inline size required by:

- the complete preferred-mode inventory;
- each viable mode's minimum visible-item count at the current origin;
- the current mode and window;
- the adjacent width that adds or removes one item; and
- the adjacent width at which policy changes the whole-strip mode.

These are presentation measurements, not persisted pixel identity. A composer
may move an allocation boundary to one of those thresholds; it may not select a
mode or window independently of the strip's policy.

## First adoption

[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md#slideable-subject-strip)
is the first adopter. Its Slideable Subject Strip composes:

- one subject `SlideStrip` supplying only Label mode and preserving one
  complete Label at minimum;
  and
- one inspector `SlideStrip` with full labels, optional supplied short labels
  or icons, and boxed derived one-based owner-order Index values, using a
  policy that prefers multiple compact inspectors over one full label.

Navigation Presentation retains the two tablists, their different styling and
navigation, inspector-first width allocation, cross-strip reveal controls, and
subject-driven inspector replacement. This document does not redefine those
domain rules.

The bounded first-adopter pairing proves the reusable contract before another
Inspect Web owner adopts it. Future controls require their own focused adoption
work; this design does not pre-approve migration of every existing control.

## Non-claims

This design does not claim:

- that every toolbar, tab row, breadcrumb, selector, or command region should
  become a `SlideStrip`;
- a fixed implementation language API or public package boundary;
- virtualization for very large inventories;
- bidirectional or vertical writing-mode support in the first implementation;
- icon acquisition, validation, or sanitization;
- draggable allocation between adjacent strips; or
- current implementation support before the named gates exist and pass.

The first implementation remains inside `prototypes/inspect-web` and must stay
Browser/Wasm-compatible. A reusable package or broader product-host boundary
requires a later focused design based on a second proven adopter.

## Implementation gates

The implementation PR must add focused tests that prove:

- zero, one, and many finite items;
- every viable and unavailable whole-strip representation mode;
- mode skipping when any item omits Short Label or Icon;
- the density-benefit rule under installed styling;
- non-monotonic requested counts across successive modes;
- deterministic mode and contiguous-window selection;
- empty inventory and the one-item capacity floor;
- retained and reset window-continuity keys;
- visible counts from complete inventory through one item;
- unequal item widths;
- focused-item, retained-leading-identity, and active-anchor precedence;
- viewports narrower than the focused item's normal size;
- focus, accessible name, and adopter-owned navigation state across
  mode and window changes;
- exact leading, trailing, and dual edge-indicator states;
- overlaid indicators that do not alter capacity or item hit targets;
- slide-before and slide-after bounds;
- focused one-item slide transactions in both directions;
- atomic reveal-then-focus navigation to an out-of-window identity;
- invalid policy and missing required-Label rejection;
- dynamic inventory replacement with retained and removed identities;
- reduced-motion equivalence; and
- two differently styled first-adopter strips without duplicated allocation
  logic.

These properties are `unverified` until the implementation gates run in the
normal Inspect Web frontend and production Browser/Wasm suites.

## Acceptance scenarios

1. Render four Label items and narrow from four visible items through three,
   two, and one. Confirm that every state is one contiguous owner-order window
   using Label for every visible item. Slide right from `Overview | Call graph`
   to `Call graph | Facts`; confirm trailing-only, dual-edge, and leading-only
   indicators at the corresponding bounds.
2. Supply Short Label, Icon, and Index modes. Confirm that each installed state
   uses exactly one kind for the whole visible window. Omit Short Label or Icon
   from one item and confirm that the whole mode is skipped without inventing
   content or mixing fallback forms. Confirm that Label remains the accessible
   name in every mode and Index changes with owner order without changing
   opaque identity. Reject absent or duplicate Label modes, duplicate kinds,
   non-positive requested counts, and an item whose resolver omits Label.
3. Configure one policy with Label minimum count one and another with Label
   minimum count two followed by Short Label and Index minimum counts two.
   Narrow both. Confirm that the first reaches one full label while the second
   selects the first viable compact mode that exposes multiple items. Make the
   compact mode admit no additional item and confirm that the preferred mode
   remains installed. Then make Short Label show three items while failing its
   requested count four, and Index show two while meeting its requested count
   two; confirm that Index is selected against the failed Label baseline.
4. Retain an interior leading identity, narrow and widen, and confirm that it
   survives while installed. Replace measurements or optional values under the
   same continuity key and confirm deterministic ranking and clamping. Remove
   the identity or replace the key and confirm reset around the adopter's
   current initial anchor.
5. Separate focus from the retained leading identity and active anchor. Move
   focus to an installed item outside the window and confirm that the new
   window is installed before the sole roving tab stop and focus move, using
   the smallest slide needed to reveal it. Use an item wider than the viewport
   and confirm that the fallback singleton preserves normal size, the
   configured edge maximizes its visible portion, and overlaid indicators do
   not consume capacity or obscure its hit target.
6. At a one-item window, focus the visible item and issue slide-after and
   slide-before requests. Confirm that the newly revealed boundary item becomes
   the pending destination and that window, sole tab stop, and focus move
   atomically without selection or activation.
7. Replace the installed inventory while focus and selection differ. Confirm
   that retained identities preserve focus and adopter-owned navigation state
   and that removed identities use the adopter's external focus rule. Install
   an empty inventory and confirm zero item-content width, no edge indicators,
   ignored slide requests, and no focus target.
8. Render two adopters with different mode policies, styles, minimum visible
   counts, and semantic roles. Confirm that both use the same window contract
   without inheriting one another's navigation behavior.
9. Repeat every transition with reduced motion and confirm identical final
   mode, window, focus, and edge-indicator states.
