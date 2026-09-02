# Inspect Web Navigation Presentation

This document owns rendering and interacting with the product-issued
coordinate, workspace, subject, hierarchy, Library, lens, and activation
descriptors that
[Inspection Subject Navigation](inspection-subject-navigation.md) and the
[View Facet Registry](view-facet-registry.md) return. It defines what the
website shows for those descriptors and which opaque identity a user action
submits back to the product. It does not define the consumer-side effect
lifecycle that installs a returned result, moves focus afterward, or commits
browser history; that model belongs to
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md).

## Ownership and boundaries

This owner defines:

- the Workspace, Package, Library, Type, and Member subject hierarchy, the
  title-line inspected-target region, and the second-row Slideable Subject
  Strip that first adopts the reusable
  [SlideStrip](inspect-web-slide-strip.md) control;
- the Workspace subject that owns retained-coordinate management;
- lens-tab rendering, roving-tabindex interaction, and no-effective-lens
  status presentation;
- the subject/hierarchy menu and coordinate menu, including their
  menu-button interaction pattern;
- consumption rules for lens descriptor ownership, ordering, and status;
- Library subject selection, including the native-select and custom-listbox
  presentations;
- rendering the aggregate (`All libraries`) identity, per-lens capability
  outcomes, and evidence;
- the compact package/version/TFM coordinate argument; and
- Type and Member inventory-row rendering and activation.

It does not own:

- selector-pill visual states, progressive filter disclosure, or the shared
  subject-heading suppression rules (owned by
  [Inspect Web Presentation Language](inspect-web-presentation-language.md));
- the consumer effect lifecycle: canonical location and refresh, browser
  history classification, effect-authority validation, synchronization debt,
  and destination-lifetime focus/announcement/acknowledgement ordering
  (owned by
  [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md));
- shell actions, Spotlight, Open, Settings entry, or modal/routed
  classification (owned by
  [Inspect Web Shell Interaction](inspect-web-shell-interaction.md));
- page-level placement or responsive composition (owned by
  [Inspect Web Surface Composition](inspect-web-surface-composition.md));
- subject or lens recommendation, reconciliation, availability evidence,
  retained-session authority, aggregate-vs-single-library result semantics
  (ordering, identity, deduplication, partial-failure, and unsupported
  arity), or Library-subject persistence across lens and coordinate changes,
  which remain
  [Inspection Subject Navigation](inspection-subject-navigation.md)'s product
  data model; and
- lens membership, identity, labels, summaries, or order, which remain the
  [View Facet Registry](view-facet-registry.md)'s product data model.

## Inputs or consumed contracts

This document consumes, without redefining:

- the complete navigation snapshot -- active subject, generation, ordered
  applicable subject descriptors, activation actions, availability evidence,
  and lens outcome -- issued by
  [Inspection Subject Navigation](inspection-subject-navigation.md);
- stable facet IDs, descriptors, structural applicability, order, and
  facet-availability outcomes issued by the
  [View Facet Registry](view-facet-registry.md);
- [Browser package sources](browser-package-sources.md#default-feed-decision)
  for browser source selection and default-feed policy; and
- [Inspect Web SlideStrip](inspect-web-slide-strip.md) for ordered item
  representation modes, contiguous windows, capacity handling, edge
  disclosure, and focus preservation within each strip; and
- the returned effect authority and synchronization disposition that
  [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md)
  validates before this document's rendered focus targets receive focus.

## Subject hierarchy, inspectors, and target selection

Workspace, Package, Library, Type, and Member are progressively narrower
inspection subjects:

- **Workspace** means the retained set of open inspection coordinates.
- **Package** means one selected package-adapter coordinate.
- **Library** means all admitted libraries or one library in that coordinate.
- **Type** means one selected type in the active Library subject.
- **Member** means one selected member of the active Type.

A local file, restored project, or another non-package artifact source occupies
the same coordinate position without being mislabelled as a Package. Package is
one common root subject, not the universal acquisition model. A non-package
coordinate uses its product-owned root subject and overview when no Library,
Type, or Member is active. This document does not invent package lenses for it.

### Persistent navigation composition

An inspection workspace has two persistent lines before its primary content:

1. The title line begins with `dotnet-inspect`, then renders the icon-backed
   ordered active subject path, Search, and browser-history actions.
2. The **subject zone** renders the Slideable Subject Strip before the
   working-content grid.

The two rows together follow the CLI's product-to-subject-to-inspector grammar
but are not command text. Inventories, hierarchy menus, and other target
navigation stay inside the working surface.

### Slideable Subject Strip

The Slideable Subject Strip is a Navigation Presentation composition of two
independently styled `SlideStrip` controls and two semantically separate
tablists:

```text
[ subject SlideStrip ] [allocation controls] [ inspector SlideStrip ]
```

The subject tablist begins with the presentation-owned Workspace entry into
retained-coordinate management, then renders the ordered root, Library, Type,
and Member subject descriptors supplied by Inspection Subject Navigation. The
prototype establishes `Workspace`, `Package`, `Type`, and `Member` now;
Library joins when its product descriptor and behavior are ready. The current
subject is selected programmatically and is not conveyed by color alone.

The inspector tablist follows the subjects and contains the active subject's
owner-ordered lenses or, for Member, its applicable sections. Subject changes
replace the inspector set; inspectors never become workspace coordinate
switchers or inspected-subject identities. Application and contextual actions
are not inventory items in either strip.

The subject strip maps each subject descriptor to:

- its owner-issued label as Label;
- no Short Label or Icon in the first adoption;
- its owner-order Index, available to the generic control but excluded from the
  first subject policy; and
- a Label mode with minimum visible count one.

The inspector strip maps each inspector descriptor to:

- its owner-issued label as Label;
- an optional owner-issued Short Label such as `CG`;
- an optional owner-issued Unicode Icon;
- its boxed one-based owner-order number as Index; and
- preferred-to-minimum modes Label, Short Label, Icon, and Index, skipping
  optional modes that cannot represent the complete installed inventory.

The inspector Label mode requests at least two visible items. Each compact mode
also requests at least two, so the strip prefers the first viable compact mode
that preserves multiple inspectors over a one-label window. A one-inspector
inventory clamps those requests to one. When no mode can fit two controls,
SlideStrip's one-item floor applies.

The subject strip's initial anchor is the active subject. The inspector strip's
initial anchor is the effective inspector. Equal-ranked windows expand toward
the following item before the preceding item in both strips.

When a non-empty inventory has no effective inspector, its first owner-ordered
inspector is the presentation-priority origin without becoming selected and
without changing the independently focused roving tab. Short labels, icons,
and inspector indexes are presentation vocabulary; they are never parsed or
submitted as action identity. The complete Label remains every control's
accessible name and hover title.

The composite, rather than either `SlideStrip`, owns width allocation between
the regions. A strip's **policy minimum width** is the normal inline size
required by its least-capacity policy outcome around SlideStrip's effective
required identity: pending navigation destination, otherwise current focus,
otherwise retained leading identity or initial anchor. That is one Label for
subjects and two Index controls for inspectors, clamped to the installed
inspector count. One fixed non-interactive separator remains between non-empty
strips in every allocation state. Every fit calculation below uses the
composite width remaining after that measured separator; the allocation
controls are adjacent controls whose width is reserved only while they are
mounted. Each first-adopter policy also supplies a positive fallback-visibility
floor that preserves its complete focus indicator and a recognizable portion
of Label or Index content.

1. When both complete inventories fit in Label mode, they consume natural width
   and the allocation controls are absent.
2. While the controls and both policy minimum widths fit, the composite
   reserves those widths. The inspector-first allocation gives the subject
   strip exactly its policy minimum width and gives the inspector strip the
   remainder long enough to select the inspector's mode and window. It then
   reserves only the exact normal inline width required by that selected
   inspector result and returns all remaining width to the subject strip. This
   begins with at least one complete subject Label while subjects use otherwise
   idle width without reducing inspector output.
3. `Show more subjects` moves the boundary to the subject strip's next richer
   window threshold, adding one adjacent full subject Label.
   `Show more inspectors` returns it to the previous subject threshold. The
   inspector strip recomputes its whole-strip mode and contiguous window in the
   returned width, retains only the exact normal inline width required by that
   result, and returns all other width to subjects; neither strip mixes
   representations. The requested boundary advances by one threshold, but
   passively returned inspector slack may admit additional subject Labels
   without changing inspector output.
4. The subject-forward bound is the richest subject threshold that still
   leaves the inspector strip its policy minimum width. The inspector-first
   bound is the default allocation from step 2. When no richer subject
   threshold preserves the inspector minimum, the default allocation is both
   bounds and `Show more subjects` is disabled.
5. **Control-free pressure** begins when the composite cannot fit both
   allocation controls and both strips' policy minimum widths. The controls are
   omitted. When the remaining width can fit both policy minima, the subject
   receives its minimum and the inspector receives the rest long enough to
   select its result. The inspector retains only the exact width required by
   that mode and window; the subject receives all remaining width.
6. **Terminal deficit** begins only when the control-free width cannot fit both
   policy minima. One subject share and two inspector shares define the target,
   with any rounding remainder assigned to the inspector. The composite then
   chooses the allocation that first minimizes total assigned width left unused
   by the two rendered windows, then minimizes distance from that target; a
   remaining tie gives the larger share to the inspector. Unused width is
   allocation beyond a normal-sized rendered window; a clipped fallback
   singleton consumes its complete share. This treats the ratio as a bias
   rather than a hard cap and returns compact-mode slack to a clipped peer.
   Each strip independently selects its mode and largest fitting contiguous
   window at the candidate allocation. A share below the policy minimum uses
   SlideStrip's one-item floor and uses the fallback singleton only when no
   normal-sized item fits. An item wider than its viewport follows the
   focused-item alignment rule.
7. A terminal candidate must give each non-empty strip at least its
   fallback-visibility floor. If the composite viewport cannot fit both floors
   and the separator, the composite retains that internal minimum width and
   scrolls inside its assigned page boundary. It never assigns zero width to a
   non-empty strip and never forces page-level horizontal overflow.

An empty inspector inventory omits the inspector strip and both allocation
controls. The subject strip then receives the composite's complete width and
renders the largest full-Label window that fits. The subject inventory is never
empty because Workspace remains its presentation-owned root entry. If that
width is below the subject fallback-visibility floor, the subject-only
composite retains the floor in an internally scrolling viewport inside its
assigned page boundary.

At the inspector-first allocation, when the inspector viewport can fit two
Labels, the effective inspector and one adjacent inspector remain readable.
When two Labels no longer fit, the inspector uses the first viable compact mode
that exposes at least two controls. A user-requested subject-forward allocation
may therefore change the complete inspector window uniformly from Label to
Short Label, Icon, or Index.

Both allocation buttons remain mounted between the all-preferred and
control-free states and use `aria-disabled="true"` at their respective bounds.
Their accessible names are `Show more subjects` and `Show more inspectors`;
visible arrows are only direction cues. Allocation changes do not alter
subject or inspector identity, order, availability, activation, selection, or
keyboard behavior.

The retained subject allocation is composite-local boundary state, distinct
from either strip's retained window. SSS uses the active subject identity and
ordered inspector identity sequence as its allocation-continuity key.
Selection changes, asynchronous shell replacement, and resize retain the
boundary ordinal while that key is unchanged; a new subject or changed
inspector sequence resets to inspector-first. Capacity, control-free pressure,
or terminal deficit may temporarily clamp the rendered allocation without
discarding the retained request. Each strip separately retains its own window
under the generic continuity contract. None of this state enters workspace
packets, Share URLs, browser history, or product navigation results.

Allocation-button bounds are computed from the currently rendered boundary,
not an unclamped retained request. Activating either button replaces the
retained request with the adjacent ordinal relative to that rendered boundary,
so every enabled activation requests at least one visible subject-window
change. Exact-width inspector slack return may passively admit more Labels; the
next bounds are then recomputed from that richer rendered boundary.

The subject tablist uses one tab stop and manual activation. Left and Right
Arrow move focus through the complete installed subject order, sliding the
window by the smallest amount needed when focus reaches a hidden item. Home and
End move to the first and last subject. Focus movement does not select a
subject until Enter or Space activates it. Every subject references the shared
subject panel, which is labelled by the active subject. The inspector tablist
retains the equivalent lens semantics below. Allocation-button activation
changes only allocation and focus remains on the button. Each strip's leading
and trailing highlights disclose hidden items but add no tab stop. Any sliding
animation preserves the focused element and is omitted when reduced motion is
requested.

Whenever a presentation-local capacity or measurement change removes an
allocation control that owns focus, the composite transfers focus before
removal. This includes transitions to all-preferred or control-free pressure.
`Show more subjects` moves focus and the subject tablist's sole roving tab stop
to its active tab; `Show more inspectors` does the same for the active
inspector tab. If the inspector tablist has no active tab, focus moves to the
active subject tab. Removing unfocused allocation controls does not move focus.

When asynchronous navigation or snapshot installation removes a focused
allocation control, Navigation Consumer's destination-lifetime rule governs
instead: the UI synchronously parks focus on the persistent `dotnet-inspect`
shell control before replacement, and only current returned effect authority
may move focus to a result-derived destination after installation. The
composite does not choose that destination or bypass the parking step.

`Slideable` combines each reusable strip's contiguous-window movement with the
composite's discrete boundary movement. It does not add pointer drag,
continuous user-sized layout, mixed per-item fallback, or persisted pixel
width.

### Inspected target

The inspected target follows the product root in the first shell row. It is not
part of either pane. Its primary advertisement is an ordered typed path:

```text
System.Text.Json > System.Text.Json.JsonSerializer > DeserializeSync
```

The path contains the applicable Package, Library, Type, and Member display
identities supplied by their owners. Workspace renders `Workspace`. The
presentation does not parse one display string to derive another, and the
segments are orientation rather than inert navigation breadcrumbs.

The Package segment receives the strongest visual emphasis, following
npmx.dev's useful emphasis and direct-copy treatment for current package
identity. Narrower segments follow in order and the current leaf remains
visually identifiable with the shared accent. The complete path remains in the
accessible name and title when visible segments elide.

Each product-issued Package, Library, Type, or Member segment is an individually
copyable control. Activating one copies that segment's owner-issued canonical
name, not the combined rendered path or text parsed from another segment.
Workspace is presentation-owned retained-coordinate management and remains
plain orientation text rather than inventing a canonical name.

The inspected target begins with a fixed-width root-icon slot. A package uses the
embedded JPEG or PNG named by its validated nuspec `<icon>` declaration. The
package entry is read under NuGet's 1 MB icon limit and admitted by image
content, not by its filename extension. A 2048-by-2048 decoded-dimension limit
bounds Browser decoder work; this is deliberately stricter than NuGet's
encoded-file contract because the shell renders the image at 20 CSS pixels.
The UI never fetches the deprecated nuspec `<iconUrl>`. When no usable embedded
icon exists, the package uses NuGet Gallery's default package icon:
`https://nuget.org/Content/gallery/img/default-package-icon-256x256.png`.
Platform and other root subjects may use their own marks. The
`dotnet-inspect` bot retains its product-mark slot before the adjacent inspected
target.

NuGet Gallery's header logo is recorded separately for a future
source-attribution affordance:
`https://nuget.org/Content/gallery/img/logo-header-94x29.png`. It is NuGet
identity, not a package icon, and must not replace either an owner-issued
package icon or the default package fallback.

The title line gives the inspected target priority over its trailing
Search/history cluster. That cluster yields space before the target path and
may not become another persistent tab strip, coordinate selector, or
independently reconstructed identity.

Second-row `Share` copies the canonical workspace link. A separate `Copy name`
action is absent because copy belongs to the segment whose typed identity is
being copied.

Browser Back and Forward own navigation history. Compact Back and Forward
buttons sit immediately to the left of the visible Spotlight Search control.
Search terminates the title line flush with its right edge. The controls are
outside the typed target and do not become breadcrumbs. The right-side cluster
yields space when the target grows: the input-like Search control first becomes
a `Search` button, then disappears while the arrows remain flush right, and
finally the arrows disappear.

### Workspace surface

Workspace is the first subject and the only persistent entry point for retained
coordinate management. Its working surface consumes product-issued descriptors
for every open coordinate and shows:

- coordinate identity and acquisition kind;
- optional owner-issued current-subject context;
- loading, ready, or failed state;
- an activation action; and
- an explicit Close action.

Activating or closing an entry submits its opaque identity and renders the
returned workspace outcome. The UI does not choose a subject, lens, successor,
or fallback for the product. Separate coordinates remain separate even when
their display package IDs match.

Closing an inactive coordinate preserves the active coordinate's inspection
state and keeps Workspace selected. Closing the active coordinate selects the
returned successor while remaining in Workspace. Share and refresh preserve
the Workspace subject and its retained coordinates.

Workspace renders stable focus targets for its heading and every coordinate
entry, including the returned active entry. Post-result focus and failure
handling are owned by
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md#workspace-result-focus).

Workspace also exposes the same Search and Open actions as the shell. It does
not infer source identity, package equivalence, local-file correspondence, or
a composite workspace name from display labels.

### Lens navigation semantics

The lens strip is derived only from the current navigation snapshot's
owner-ordered lens descriptors. When that collection is non-empty, every lens
or member section is a tab with `role="tab"` and `aria-selected`, including
identically labelled tabs owned by different subjects. An effective lens is
selected programmatically rather than conveyed by color alone. When no
effective lens exists, every tab has `aria-selected="false"`. An empty
descriptor collection omits the tablist and leaves the no-effective-lens status
region as the content following the Slideable Subject Strip.

Each tablist has the accessible name `<Subject> lenses`. The effective tab
references its panel with `aria-controls`; the panel uses `role="tabpanel"` and
`aria-labelledby`. Without an effective lens, tabs do not reference a
nonexistent panel.

Lens tablists use one tab stop and manual activation:

- `Tab` enters on the tab with `tabindex="0"`, initially the effective tab or,
  when none exists, the first owner-ordered descriptor, and leaves the tablist
  from the focused tab.
- Left and Right Arrow move focus through the horizontal tabs.
- Home and End move focus to the first and last tab.
- Arrow navigation includes `aria-disabled` lens tabs so unavailable and
  failed lenses remain discoverable.
- Enter or Space activates a focused available tab by submitting its opaque
  subject-scoped lens identity through Inspection Subject Navigation.
- Activating an `aria-disabled` tab has no effect.

Roving `tabindex` keeps only the focused tab at `tabindex="0"`. Moving focus
does not change `aria-selected` or start lens work until activation.
When no effective lens exists, the first owner-ordered tab may itself be
disabled; the UI does not skip it to infer a preferred available neighbor.

An unavailable lens remains in its owner-issued position with
`aria-disabled="true"` and an accessible description of its reason. A failed
lens is also disabled, but exposes its owner-issued diagnostic distinctly from
valid unavailability. Neither status retains stale panel content.

When descriptors in one tablist have the same Title, each tab references
its owner-issued Summary as an accessible description and exposes that same
sentence as non-live help on keyboard focus or pointer hover. If both Title and
Summary collide, that help appends the exact owner-issued ID as the final
disambiguator. The UI does not parse the ID or invent distinguishing copy.

With no effective lens, the status region renders `Lens unavailable` for a
validly unavailable outcome and `Lens failed` for a failed outcome. If an
effective lens exists beside unavailable or failed peers, its tab and panel
remain active while the disabled peers and their evidence remain discoverable.
The UI never uses descriptor order or a familiar local lens name to choose a
replacement.

### Subject availability and reconciliation

The UI consumes the complete Inspection Subject Navigation snapshot. That
snapshot supplies the active subject, generation, ordered applicable subject
descriptors, activation actions, availability evidence, and lens outcome.

The hierarchy menu exposes every returned applicable subject level. An
unavailable or failed Library, Type, or Member item remains discoverable with
`aria-disabled="true"` and its owner-issued reason or diagnostic. A current
item carries `aria-current="page"` and no activation action. Menu focus remains
separate: arrow navigation does not move `aria-current`, and returning focus to
the current item does not submit it. Activating a non-current available item
submits its opaque action ID with the issuing generation; the UI renders the
returned snapshot or typed outcome without deriving a target from row identity
or display text.

A `Selection required` Member state remains distinct from unavailable or
failed. Its hierarchy item is enabled, labelled `Choose a member`, carries no
product action ID, and uses `aria-controls` to identify the Member choices
surface. It is neither `aria-current` nor `aria-disabled`; at a narrow viewport
it also uses `aria-haspopup="dialog"` for the shared modal navigation drawer.
Activation is a local presentation action: it closes the hierarchy menu and
moves focus to the first owner-ordered visible Member row in the navigation
pane. If host filters hide every row, focus moves to the Member text filter
instead. At a narrow viewport the UI opens the Member drawer before applying
the same row-or-filter focus rule, so focus remains contained in the modal.
Each row's product-issued activation state governs any later commit. Opening
the choices changes no snapshot, URL, or history and does not invent a default
Member.

The Workspace subject, second-row subject/inspector region, title-line
inspected target, and content region all render the same returned navigation
snapshot.
The UI does not infer initial, fallback, or reconciliation policy from
descriptor order, assembly order, current filters, package kind, or display
text.

### Coordinate and subject menu interaction

Coordinate and subject menus use menu-button semantics. Their invoking control
exposes `aria-expanded` and `aria-controls`; opening moves focus to the current
item or first item. Arrow navigation includes unavailable and failed
`aria-disabled` items so their reasons and diagnostics remain discoverable;
Enter activates a non-current available item through its product action or
opens the Member choices for a `Selection required` item through the local
presentation action above. Escape closes the menu and returns focus to the
invoker. Outside pointer dismissal or tabbing away preserves the new focus
destination instead.

The typed outcome of activating a menu item -- including focus movement,
history commitment, and effect-authority validation -- is defined by
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md#shell-and-menu-focus-resolution).

### Lens descriptor ownership

The [View Facet Registry](view-facet-registry.md) owns lens membership,
identity, labels, summaries, structural subject kind, and order. The UI renders
every descriptor returned for the active subject in owner-issued order,
preserving its exact ID and available, unavailable, or failed status. It
submits only the opaque ID of an available descriptor. It does not retain a
subject-to-lens table, add a locally known lens, or omit an owner-issued
descriptor because its current renderer lacks support.

A lens appears only in the subject-scoped descriptor set returned by Inspection
Subject Navigation. The UI does not retain one mixed lens strip under Package
or repeat a facet under another subject. Distinct registry IDs may share a
display label; the UI neither deduplicates them nor derives identity from that
label. A missing renderer for an owner-issued available descriptor is an
implementation defect, not authority to hide it, downgrade it, or fall back.

### Library selection

The Library view lists every library admitted from the active coordinate and an
`All libraries` subject when the product admits aggregate
inspection for that coordinate.

The control consumes the snapshot's ordered Library subject descriptors and
active identity. It submits a selected non-current available descriptor's
opaque action ID with the issuing generation without inferring a selection
from package kind, endpoint shape, assembly count, or lens capability.

The Library subject control is single-select. A compact population may use a
native `select` only when every returned option is available. A population
containing unavailable or failed options uses a visible library list with
`role="listbox"`, `role="option"`, and `aria-selected` so its evidence remains
discoverable. It is not a selector-pill group with `aria-pressed` and is not a
lens tablist.

The custom listbox has the accessible name `Libraries` and one tab stop. Focus
remains on the listbox while `aria-activedescendant` identifies the active
option; `aria-selected` identifies the committed Library subject.

Unavailable and failed options remain in owner-issued order with
`aria-disabled="true"`. An unavailable option exposes its reason; a failed
option exposes its diagnostic distinctly. The custom listbox allows either to
receive active focus for discoverability but never commits it.

The active option has a visible focus indicator in addition to its rest or
committed-selection styling. The indicator is not conveyed by color alone,
and remains distinct from the committed `aria-selected` state. The UI scrolls
the active option into view whenever it moves.

Library selection uses manual commit:

- Up and Down Arrow move only the active option.
- Home and End move the active option to the first and last option.
- Printable input, including Space, moves the active option through prefix
  typeahead and never commits the Library subject.
- Enter commits only a non-current available option carrying an activation
  action and starts the returned lens work. A current, unavailable, or failed
  option is a no-op.
- Escape or focus leaving the listbox without a commit restores the active
  option to the committed selection.

Native `select` uses the platform's equivalent selection and commit behavior.
It is replaced by the custom listbox if a later snapshot introduces an
unavailable or failed option.

The control renders the selected Library subject returned by Inspection
Subject Navigation across every Library lens. Switching lenses does not
locally alter that subject. Changing package version or TFM submits the
realized coordinate result to Navigation and renders its reconciled snapshot
rather than retaining or reconstructing Library identity in the browser.

The active library subject remains visible while the library list is filtered
or collapsed. A lens heading distinguishes aggregate results from a
single-library result.

Package and Type navigation render producer-owned Type and Member inventory
rows with the activation descriptors returned in the snapshot. They submit the
supplied action ID and generation; they do not derive actions from row identity
or text. The type-navigation heading shows the product-issued Type-inventory
Library context and links back to the Library subject for changes. It is not a
second library selector, and the UI does not recalculate context, eligibility,
or retention from assembly membership.

When the product surface identifies colliding types under `All libraries`, type
navigation qualifies only those rows with their product-owned defining library.
If a colliding Type is selected, the subject zone also shows its
defining library. API and Source continue to rely on that line for the complete
identity; disambiguation does not restore the removed metadata block.

### Aggregate results

[Inspection Subject Navigation](inspection-subject-navigation.md#aggregate-and-single-library-capability)
owns `All libraries` aggregate result semantics -- ordering, identity,
deduplication, and partial-failure behavior -- each lens's aggregate and
single-library capability, and Library-subject persistence across lens and
coordinate changes. This document renders that owner-provided result and
capability evidence without reinterpreting them.

The UI renders each Library lens's aggregate and single-library capability
with its visible owner-issued rejection reason. When the selected lens cannot
provide the current subject arity, the UI shows that mode as unavailable and
leaves the Library subject control available so the user can choose a
supported subject; it does not infer capability from source family or
transport method or itself change the Library subject to obtain a supported
arity.

## Package coordinate controls

The old full-width `PACKAGE` row remains removed. Package version and TFM
controls render in the Package working surface:

```text
dotnet-inspect  ⬡ System.Text.Json                         ← →  Search
Workspace Package Type Member | Overview Dependencies Metadata   Share Settings ?

Package coordinate
Version 10.0.0   Framework net10.0
```

The coordinate editor is available while Package is selected, across its
inspectors. It is absent from Workspace, Library, Type, and Member so package
editing does not consume persistent shell space. Changing the coordinate
updates the shared workspace by submitting the typed transition and rendering
its outcome. Package Overview does not repeat a separate target-framework
selector.

Resolved assembly assets are Library details and do not enter the package
coordinate or Package Overview.

Non-package inputs use their product-owned coordinate display instead of
inventing package/version/TFM fields.

Platform libraries may be present in the workspace, but Platform is not a
workspace entry or subject.

## Type navigation

This owner renders product-issued Type inventory rows and their activation
descriptors. Package and Library navigation may also expose Types where their
owning lens requires it, but no second Library filter is introduced. Placement
beside Type and Member working surfaces and replacement by the narrow
navigation drawer are owned by
[Inspect Web Surface Composition](inspect-web-surface-composition.md#responsive-composition).

## Non-claims

This document does not define browser-history classification, canonical-URL
composition, effect-authority validation, synchronization debt, or
destination-lifetime focus and announcement ordering. It does not define
selector-pill visual states or progressive filter disclosure. It does not
define shell actions, modal/routed classification, or page-level placement.
It does not define continuous resizing or a draggable divider for the
Slideable Subject Strip.
It does not invent subject or lens recommendation, reconciliation, or
fallback policy beyond what the product returns. It does not define
aggregate-vs-single-library result semantics, capability-arity rules, or
subject persistence across lens or coordinate changes, which remain
[Inspection Subject Navigation](inspection-subject-navigation.md)'s product
data model.

## Implementation gates

Before implementation claims this rendering and interaction contract, it must
add and pass these named Inspect Web tests:

- `navigation-consumer.test.ts`:
  `owner descriptors retain exact identity order and status` uses an
  owner-ordered descriptor absent from every legacy Package, Library, Type, and
  Member lens array, plus available, unavailable, and failed peers and a
  three-descriptor Title collision in which two Summaries also collide. The
  rendered strip must preserve every exact ID, position, and status without host
  additions, omissions, deduplication, or fallback. The gate activates the
  legacy-absent descriptor and all three duplicate-title descriptors and proves
  that each exact subject-scoped registry ID, rather than a label, ordinal, or
  legacy token, is submitted. It also proves that duplicate titles expose each
  owner-issued Summary on focus and through an accessible description, using
  the exact ID only when Title and Summary both collide. This is the
  non-vacuity gate for registry consumption.
- `navigation-consumer.test.ts`:
  `no effective lens renders status without a selected tab or panel` covers
  non-empty and empty descriptor collections for unavailable and failed
  outcomes.
- `navigation-consumer.test.ts`:
  `unavailable and failed navigation options preserve distinct evidence`
  covers lens tabs, hierarchy-menu items, and Library-listbox options.
- `navigation-consumer.test.ts`:
  `subject activation submits only action identity and issuing generation`
  rejects commands reconstructed from row identity or display text.
- `navigation-consumer.test.ts`:
  `hierarchy menu keeps current subject distinct from focus` moves focus through
  available and disabled items while the committed item alone retains
  `aria-current="page"`.
- `navigation-consumer.test.ts`:
  `selection required renders guidance without committing a Member` proves
  that `Choose a member` is an enabled local presentation action with no
  product action ID. It verifies `aria-controls`, narrow-layout dialog
  disclosure, focus on the first owner-ordered visible Member row, fallback to
  the Member text filter when filters hide every row, modal containment, no
  snapshot or history mutation, and no locally selected default.
- `navigation-focus.test.ts`:
  `lens tabs and Library options separate focus from committed selection`
  covers roving tabs, disabled-option discoverability, manual listbox commit,
  cancellation, synchronous focus parking, native-select replacement, tablist
  omission, result-authorized focus, and rejection of an outgoing-renderer menu
  invoker as a post-replacement focus target.
- `scope-bar.test.ts` and `workspace-titlebar.spec.ts`:
  `slideable subject strip composes reusable strips without losing navigation`
  cover the separate subject and inspector whole-strip mode policies,
  contiguous windows and edge indicators, inspector-first width allocation,
  exact selected-window slack return at every control-present boundary,
  semantic boundary thresholds, single-label subject capacity, multi-item
  compact inspector capacity, control-free removal, terminal-deficit
  unused-width then ratio-distance ordering and inspector tie-break,
  fallback-visibility floors, two-strip and subject-only internal-minimum
  scrolling, presentation-local window and allocation retention, reduced-motion
  behavior, and focus/tab-stop preservation across allocation changes and
  asynchronous shell replacement.

The implementation fixture supplies typed product results through the normal
navigation boundary. It does not construct a parallel host catalog or bypass
effect-authority validation merely to observe the renderer.

These gates are not implemented by this documentation-only design. Until they
exist and pass, the prose defines the target contract but does not claim
Inspect Web implementation conformance.

## Acceptance scenarios

An implementation claiming this contract is complete must satisfy these
outcomes. These rendering and widget-focus outcomes are proved by the gates
above; remaining focus-resolution and history claims inside these scenarios
are proved by the gates in
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md#implementation-gates).

### Subject composition

1. Supply an owner result whose active subject is a Type and whose hierarchy
   contains available, unavailable, and failed descriptors above and below
   that Type.
2. Confirm that the working surface uses the active Type as its level-one
   heading and the subject selector renders every descriptor with its distinct
   unavailable reason or failure diagnostic.
3. Activate a non-current available descriptor and confirm that the UI submits
   only its opaque action ID with the issuing generation, renders the returned
   outcome, and focuses its active-subject heading.
4. Supply a root-only result and confirm that the always-present subject control
   uses the owner-issued root label and the hierarchy menu still exposes every
   unavailable lower-level descriptor and reason.
5. Reopen the hierarchy menu, move focus away from the current item, and
   confirm that only the committed subject retains `aria-current="page"`.
6. Supply `Selection required` Member context and confirm that the UI shows
   enabled `Choose a member` guidance without an action ID. Activate it in wide
   and narrow layouts and confirm that it focuses or opens the product-issued
   Member choices without selecting one or changing snapshot, URL, or history.
   Apply filters that hide every Member row and confirm that the same action
   focuses the Member text filter, remaining inside the narrow modal drawer.
7. Supply a typed transition failure and confirm that it is visible without the
   UI selecting another subject and that focus returns to the subject
   menu-button invoker.
8. Confirm that every copyable inspected-target segment copies its own
   product-issued canonical identity rather than display text and that no
   separate `Copy target` action occupies the subject zone.

### Slideable strip allocation

1. Render Workspace, Package, Library, Type, and Member with five Member
   inspectors at a width where every Label fits. Confirm that both tablists use
   natural-width Label mode and the allocation controls are absent.
2. Narrow through subject windows containing five, four, three, two, and one
   complete Label. Confirm that no subject is replaced by `[W]`, another short
   form, or Index. Slide an interior two-subject window by one position and
   confirm that leading and trailing highlights exactly disclose hidden items.
3. Continue through inspector widths that fit all Labels, three Labels, and
   two Labels. Narrow below the two-Label threshold and confirm that the entire
   inspector window changes to the first viable Short Label, Icon, or Index
   mode that exposes at least two controls. Confirm that no window mixes a
   complete Label with a compact representation. Omit Short Label and Icon from
   one inspector and confirm that both whole-strip modes are skipped in favor
   of Index.
4. Move the active inspector to the final owner-ordered entry and confirm that
   the initial window contains it and expands toward the nearest preceding
   inspector without reordering either control. Install a non-empty inventory
   with no effective inspector and confirm that the initial window begins at
   the first owner-ordered inspector without selecting it or moving the focused
   roving tab. Install an empty inspector inventory and confirm that the
   inspector strip and both allocation controls are absent while the subject
   strip uses the complete composite width.
5. Activate `Show more subjects` repeatedly and confirm that each activation
   requests the next subject-window threshold and admits at least one adjacent
   full Label. Confirm that the inspector strip recomputes one uniform mode and
   contiguous window within its smaller viewport, retains exactly the width
   required by that result, and returns all other width to subjects. When that
   slack crosses another subject threshold, confirm that the additional Label
   appears without changing inspector output. Confirm that no subject,
   inspector, selection, or product navigation state changes.
6. Activate `Show more inspectors` and confirm that each activation removes
   at least one visible subject Label and returns the requested width to the
   inspector strip before exact result-width slack return. Confirm that focus
   remains on the allocation button. At each rendered bound, confirm that the
   corresponding mounted button is `aria-disabled="true"` and activation has
   no effect.
7. Rove focus to an inactive subject and inspector and replace the shell
   asynchronously. Confirm that the focused typed tab remains the sole tab
   stop in its tablist, each retained window still contains its focus, and
   allocation bias survives while the subject and ordered inspector identity
   sequence remain installed. Change that sequence without changing the
   subject and confirm that allocation resets to inspector-first.
8. Focus each allocation button in turn and install a presentation that removes
   it through a presentation-local resize or measurement change: all labels
   fitting and control-free pressure. Confirm that focus and the sole roving
   tab stop transfer to the active tab in the named region before removal.
   Confirm that an absent active inspector falls back to the active subject and
   that removing unfocused allocation controls does not move focus. Repeat
   through asynchronous label and inventory replacement, including an empty
   inspector inventory; confirm that focus first parks on the persistent
   `dotnet-inspect` shell control and moves to a result-derived destination only
   under Navigation Consumer's current effect authority.
9. Narrow until the controls plus both policy minima cannot fit but the minima
   fit after control removal. Confirm that the controls disappear, both minima
   remain satisfied, and inspector width beyond its selected compact or Label
   window returns to subjects.
   Continue into terminal deficit and confirm that the one-subject-share to
   two-inspector-share target first minimizes unused rendered-window capacity
   and then minimizes distance from the target, with ties favoring inspector
   width. Confirm that compact-mode slack returns to a clipped peer. Confirm
   that each strip selects one uniform mode and contiguous window, using a
   fallback singleton only when its share cannot fit a normal item. Focus an
   item wider than its viewport and confirm that its visible portion is
   maximized; with focus in one strip, confirm that a distinct active anchor
   does not displace it. Confirm that every compact control retains its full
   accessible name and title and neither strip nor the page overflows its
   assigned boundary. Narrow below both fallback-visibility floors plus the
   separator and confirm that the composite scrolls internally at that minimum
   rather than assigning either strip zero width.
10. Repeat the allocation transitions with reduced motion enabled and confirm
    that modes, windows, edge indicators, and focus reach the same final states
    without sliding animation.

### Lens inventory and outcomes

1. Supply owner-ordered available, unavailable, and failed descriptors,
   including one absent from every legacy browser lens array and three with the
   same display label, two of which also share a Summary.
2. Confirm that every descriptor appears once in exact owner order with its
   exact identity, label, status, reason, and diagnostic. Focus the
   duplicate-title tabs and confirm that each owner-issued Summary is visible
   and programmatically descriptive; when both summaries collide, confirm that
   the exact ID distinguishes them without replacing their labels.
3. Focus every disabled tab and confirm that unavailable and failed evidence is
   discoverable while activation remains a no-op.
4. Activate the legacy-absent descriptor and all duplicate-label descriptors.
   Confirm that moving focus did not select them, each activation submits its
   exact opaque subject-scoped identity, and the returned effective lens becomes
   the one selected tab and panel.
5. Supply a non-empty descriptor collection with no effective lens and an
   unavailable outcome. Confirm that no tab is selected, no panel exists, and
   the `Lens unavailable` status is labelled by the active subject.
6. Repeat with a failed outcome and confirm `Lens failed` preserves the
   diagnostic rather than presenting valid unavailability.
7. Supply an empty descriptor collection and confirm that the tablist is
   omitted without introducing a locally familiar fallback lens.

### Workspace composition

1. Supply two open-coordinate descriptors with different optional subject
   context and status.
2. Confirm that Workspace renders those descriptors without deriving identity
   from their labels.
3. Activate and close entries and confirm that each action submits the opaque
   coordinate identity once and renders the returned workspace outcome.

Post-result focus and failure acceptance are specified by
[Workspace focus acceptance](inspect-web-navigation-consumer.md#workspace-focus-acceptance).
