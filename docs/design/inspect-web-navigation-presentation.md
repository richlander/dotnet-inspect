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

- the Workspace, Package, Library, Type, and Member subject hierarchy and the
  single-line inspection command that identifies the active coordinate and
  subject;
- the Workspace surface that replaces the package tab strip;
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
- the returned effect authority and synchronization disposition that
  [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md)
  validates before this document's rendered focus targets receive focus.

## Subject hierarchy and inspection command

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

### Single-line inspection command

Package workspace tabs and the Package, Library, Type, and Member primary
tablist are removed. One single-line inspection command identifies the
Workspace root, active coordinate, and current leaf subject:

```text
dotnet-inspect  System.Text.Json@10.0.0/net10.0  System.Text.Json.JsonSerializer.DeserializeAsync  Copy target
dotnet-inspect  MyAssembly.dll                   MyNamespace.MyType                              Copy target
dotnet-inspect  DotnetInspect.TestAssets.ToolV2  Package                                         Copy target
```

The command starts without a separator glyph. Spacing and typography distinguish
its three identity roles:

1. `dotnet-inspect` is the Workspace root.
2. The coordinate identifies the active package, platform, project, file, or
   other product-owned workspace input.
3. The current subject identifies the active root, Library, Type, or Member.
   At a package root its label is `Package`; another coordinate kind uses its
   owner-issued root label.

One trailing visible `Copy target` button follows those identity roles.

The displayed subject need not mechanically repeat every parent. A Type or
Member normally uses its product-owned qualified display identity. A defining
Library appears when it is the active subject or when the product reports that
qualification is required to disambiguate identity.

The command remains one line. When space is constrained, intermediate
qualification elides before the coordinate or leaf subject. The complete
product-owned identity remains in the accessible name and focused or expanded
presentation.

Each identity role and the trailing action is a real interactive control rather
than click behavior attached to inert text:

- activating `dotnet-inspect` opens Workspace;
- activating the coordinate opens a coordinate menu whose actions include
  navigation to the Package or other root overview, `Search packages`, and
  controls for applicable version, TFM, or acquisition detail;
- activating the always-present current subject opens a hierarchy menu
  containing every ordered applicable root, Library, Type, and Member
  descriptor supplied by Inspection Subject Navigation, including unavailable
  and failed descendants with their evidence; and
- `Copy target` copies the product-issued canonical current target.

The coordinate and subject menus are not primary-view tablists. Their items use
the product-issued subject identities and availability results. They make root
and every applicable subject level reachable even when the compact command
omits other labels.

Copying a target and copying a restorable workspace URL are different actions.
`Copy target` does not reconstruct identity from display text. The `share`
command continues to copy the canonical workspace link.

Browser Back and Forward own navigation history. The secondary row that
repeated back/forward buttons, package identity, active lens, Copy, and Taste is
removed.

### Workspace surface

The Workspace surface replaces the package tab strip. It consumes
product-issued descriptors for every open coordinate and shows:

- coordinate identity and acquisition kind;
- optional owner-issued current-subject context;
- loading, ready, or failed state;
- an activation action; and
- an explicit Close action.

Activating or closing an entry submits its opaque identity and renders the
returned workspace outcome. The UI does not choose a subject, lens, successor,
or fallback for the product. Separate coordinates remain separate even when
their display package IDs match.

Workspace renders stable focus targets for its heading and every coordinate
entry, including the returned active entry. Post-result focus and failure
handling are owned by
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md#workspace-result-focus).

Workspace also exposes the same Search and Open actions as the shell. It does
not infer source identity, package equivalence, or local-file correspondence
from display labels.

### Lens navigation semantics

The lens strip is derived only from the current navigation snapshot's
owner-ordered lens descriptors. When that collection is non-empty, every lens
or member section is a tab with `role="tab"` and `aria-selected`, including
identically labelled tabs owned by different subjects. An effective lens is
selected programmatically rather than conveyed by color alone. When no
effective lens exists, every tab has `aria-selected="false"`. An empty
descriptor collection omits the tablist and leaves the no-effective-lens status
region as the content following the inspection command.

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

The inspection command, Workspace, lens strip, and content region all render
the same returned active-subject identity. The UI does not infer initial,
fallback, or reconciliation policy from descriptor order, assembly order,
current filters, package kind, or display text.

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
If a colliding Type is selected, compact workspace context also shows its
defining library. API and Source continue to rely on the inspection command for
that identity; disambiguation does not restore the removed metadata block.

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

The old full-width `PACKAGE` row remains removed. Package identity, version,
and TFM are instead one compact coordinate argument immediately after
`dotnet-inspect`:

```text
dotnet-inspect  System.Text.Json@10.0.0/net10.0  System.Text.Json.JsonSerializer
```

The coordinate remains visible across Package, Library, Type, and Member
subjects. Activating it opens the applicable package, version, and TFM controls
without adding another persistent row. Changing the coordinate updates the
shared workspace by submitting the typed transition and rendering its outcome.

Package Overview presents package details, but it is no longer the only place
from which the coordinate may be edited. Existing package fields do not repeat
the same version and TFM beside the command control.

Resolved assembly assets are Library details and do not enter the package
coordinate or Package Overview.

Non-package inputs use their product-owned coordinate display instead of
inventing package/version/TFM fields.

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
2. Confirm that the inspection command uses the active Type as its level-one
   heading and the subject menu renders every descriptor with its distinct
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
8. Confirm that the trailing `Copy target` button remains visible and copies
   the product-issued canonical target rather than display text.

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
