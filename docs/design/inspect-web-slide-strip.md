# Inspect Web SlideStrip

This document owns `SlideStrip`, a reusable Inspect Web control for presenting
one finite ordered inventory in a single horizontal region. A strip selects
among full labels, short labels, and icons as its viewport changes, preserves
item identity and focus across those representation changes, and scrolls
internally when the inventory's minimum representations cannot fit.

`SlideStrip` is the first focused reusable control in the Inspect Web UI. New
surfaces should adopt shared controls when their behavior matches an existing
contract instead of creating a visually similar custom implementation.
Similarity alone is not conformance: each adopter still owns its semantic
roles, navigation, activation, selection, and composition with adjacent
controls.

## Ownership and boundaries

This owner defines:

- the ordered item-presentation contract;
- the full-label, short-label, and icon representation slots;
- policy-controlled fallback and promotion between those representations;
- deterministic presentation states over any finite item inventory;
- capacity handling, internal horizontal scrolling, and focused-item reveal;
- focus and accessible-name preservation while one item changes visual
  representation; and
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
- an optional icon with decorative or labelled treatment chosen by the
  adopter's semantic control; and
- styling tokens or slots that do not change identity or representation
  order.

The complete label is available to accessibility APIs and as focused or hover
disclosure regardless of the visible representation. A short label or icon is
presentation only. It is never submitted as action identity, parsed back into
one, or used to distinguish otherwise identical items.

An item without a short label or icon simply omits that representation. The
policy skips unavailable representations; it does not synthesize initials,
numbers, abbreviations, or icons.

## Representation policy

An adopter supplies one strip-level representation policy. The policy defines:

- the preferred-to-minimum representation order, including the required full
  label exactly once and any available subset of short label and icon;
- a deterministic promotion order over each item's available
  representation-to-representation transitions;
- the desired presentation state: minimum, maximum, or one retained finite
  reveal level;
- a presentation-continuity key and initial desired state; and
- the normal interactive sizing that every representation must retain.

A common policy is `label -> short label -> icon`, but the control does not
make that sequence universal. A text-only strip may use
`label -> short label`; an icon-free item may retain its short label as its
minimum representation. Because every valid policy includes the required full
label, filtering unavailable optional representations always leaves at least
one representation for every item.

Before constructing states, the control removes a dominated representation:
one that is less preferred but no narrower than an available more-preferred
representation under the installed styling. The remaining representation
chain grows monotonically in normal inline size toward the preferred form.
This keeps a localized short label or unusually wide icon from creating a
non-monotonic collapse sequence.

The item-priority order may use adopter-owned state such as an active identity.
It must be a complete deterministic order over the installed inventory. When
an adopter has no active item, it supplies another explicit origin or complete
order rather than asking `SlideStrip` to infer selection from focus.

## Presentation states

`SlideStrip` constructs a finite promotion plan:

1. An empty inventory has one empty state, measures zero item-content width,
   has no adjacent thresholds, does not scroll, and contains no focus target.
2. For a non-empty inventory, state zero renders every item at its minimum
   available representation.
3. Each subsequent state promotes exactly one item to its next preferred
   available representation.
4. At each state, the control selects the highest-priority currently eligible
   transition from the adopter-supplied complete order over item and
   representation-transition pairs. A transition is eligible only after that
   item's preceding transition has occurred. The policy therefore decides
   whether one item reaches its preferred representation before another item
   promotes or whether equivalent promotion rounds alternate among items.
5. The final state renders every item at its preferred available
   representation.

The plan contains no demotion between consecutive states and no state changes
item identity, order, semantics, or normal interactive size.

When state zero fits, the strip is in **fitting mode**. It computes the greatest
feasible state whose items fit without wrapping or shrinking below normal
sizing. The rendered state is the lesser of that feasible state and the
adopter's desired state.

When state zero does not fit, the strip is in **overflow-minimum mode**. It
renders state zero inside its scrolling viewport; no fitting state exists and
the rendered-state equation is not evaluated. The next richer allocation
threshold is the width required to leave overflow-minimum mode with state zero
fully fitting.

The desired state may survive a temporary fitting-mode capacity clamp or
overflow-minimum mode so widening restores the requested fidelity.

The presentation-continuity key decides whether a desired ordinal survives a
new promotion plan. When the key is unchanged, the strip retains the desired
ordinal and clamps it to the new plan length; the ordinal intentionally applies
to the new plan prefix rather than naming specific promoted items. When the key
changes, the strip resets to the adopter-supplied initial desired state.
Inventory identity, representation availability, policy version, or other
adopter-owned facts may participate in that key. Width and measured thresholds
alone do not require a new key.

An adopter that wants maximum readable content supplies the maximum desired
state. An adopter that exposes user-controlled disclosure retains an explicit
desired reveal level. `SlideStrip` owns the state calculation, but it does not
persist desired state in URLs, history, workspace packets, or application
storage.

## Capacity and sliding

The strip remains one non-wrapping horizontal region.

When state zero fits, the strip does not scroll. Items progressively promote or
collapse through the finite states as capacity changes. A non-fitting
promotion remains unapplied; later states cannot bypass it because the
promotion plan is an ordered prefix.

When state zero does not fit, the strip:

- renders state zero;
- preserves every item in owner order;
- keeps every representation at normal interactive size;
- enables internal horizontal scrolling rather than wrapping or clipping
  items from the inventory; and
- maximizes visibility of the focused item, fully revealing it whenever the
  viewport can contain its normal size.

Focused-item visibility has priority over an adopter-requested active anchor.
The strip reveals the active anchor only when no item in the strip owns focus
or both items can remain visible. When an item is wider than the viewport, the
strip aligns the nearest edge needed to maximize its visible portion rather
than shrinking it.

`Slideable` refers to this internal movement across an inventory larger than
the region and to discrete movement through semantic representation states. It
does not imply pointer dragging, inertial pane resizing, or persisted pixel
width.

## Focus and replacement

Changing an item's visible representation does not replace its semantic
control. If an implementation must replace DOM, it restores the same opaque
item identity before the browser can fall back to the document body.

The strip preserves:

- the focused item;
- the adopter-owned selected or current state;
- the adopter-owned roving tab stop or equivalent navigation state; and
- the scroll position needed to keep the restored focus visible.

Selection and focus remain distinct. A focused item is not promoted in
semantic priority, selected, or activated unless the adopter's supplied policy
explicitly says so.

The adopter owns focus transfer when the entire strip is removed or replaced
by a different semantic control. `SlideStrip` owns focus only while the same
strip and item identity remain installed.

Representation animation is optional. It must not replace the focused control,
delay accessibility state, or change the final presentation state, and it is
omitted when reduced motion is requested.

## Styling and composition

The control exposes styling hooks for:

- the strip region and scrolling viewport;
- each semantic item;
- the full-label, short-label, and icon representations;
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
`SlideStrip` independently owns representation selection and internal
scrolling within the width it receives.

For discrete composition, a strip exposes the normal inline size required by
its minimum state, preferred state, current rendered state, and adjacent
allocation thresholds. In overflow-minimum mode, the next richer threshold
first fits the complete minimum state; subsequent richer thresholds each
admit one promotion. These are presentation measurements, not persisted pixel
identity. A composer may move an allocation boundary to one of those
thresholds; it may not select representations independently of the strip's
policy.

## First adoption

[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md#slideable-subject-strip)
is the first adopter. Its Slideable Subject Strip composes:

- one subject `SlideStrip` with full subject labels and stable boxed short
  labels; and
- one inspector `SlideStrip` with full inspector labels and stable boxed
  one-based short labels.

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
- every available representation combination;
- dominated short-label or icon representations under installed styling;
- deterministic promotion order and finite bounds;
- empty-inventory and overflow-minimum modes;
- preferred, clamped, and restored desired states;
- retained and reset continuity keys across promotion-plan changes;
- state-zero fit versus internal-scroll boundaries;
- unequal item widths;
- focused-item and active-anchor reveal;
- viewports narrower than the focused item's normal size;
- focus, accessible name, and adopter-owned navigation state across
  representation replacement;
- dynamic inventory replacement with retained and removed identities;
- reduced-motion equivalence; and
- two differently styled first-adopter strips without duplicated allocation
  logic.

These properties are `unverified` until the implementation gates run in the
normal Inspect Web frontend and production Browser/Wasm suites.

## Acceptance scenarios

1. Render an inventory whose items provide label, short-label, and icon
   representations. Narrow through every promotion threshold and confirm that
   exactly one deterministic representation state renders at each width.
2. Omit different optional representations and confirm that the policy skips
   them without inventing content or changing identity. Make a less-preferred
   representation no narrower than a more-preferred one and confirm that the
   dominated representation does not enter the promotion plan. Reject a policy
   that omits or duplicates the required full label.
3. Retain a preferred desired state, narrow until it is clamped, and widen
   again. Confirm that the requested state returns unless the adopter changed
   it while clamped. Replace the promotion plan under the same continuity key
   and confirm that the ordinal is retained and clamped; replace the key and
   confirm reset to the adopter's initial state.
4. Narrow below the complete state-zero width and confirm that the strip
   enters overflow-minimum mode, scrolls internally, and preserves every item
   in order. Separate focus from the requested active anchor and confirm that
   focused-item visibility wins; use an item wider than the viewport and
   confirm that the nearest edge maximizes its visible portion.
5. Change visible representations and replace the installed inventory while
   focus and selection differ. Confirm that retained identities preserve
   focus and adopter-owned navigation state and that removed identities use
   the adopter's external focus rule. Install an empty inventory and confirm
   the empty state has zero item-content width, no thresholds, scrolling, or
   focus target.
6. Render two adopters with different representation policies, styles, and
   semantic roles. Confirm that both use the same state and overflow contract
   without inheriting one another's navigation behavior.
7. Repeat every transition with reduced motion and confirm identical final
   representation, focus, and scrolling states.
