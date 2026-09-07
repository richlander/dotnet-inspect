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

- the application-scope strip that composes the presentation-owned Query route
  entry with the product-issued Workspace subject entry;
- the Package, Library, Type, and Member subject hierarchy, the inspected-target
  rendering, and the adaptive subject and inspector navigation groups;
- the separately presented Workspace subject that owns retained-coordinate
  management;
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
  for browser source selection and default-feed policy;
- the returned effect authority and synchronization disposition that
  [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md)
  validates before this document's rendered focus targets receive focus.

## Application scopes, subject hierarchy, and target selection

The application-scope strip composes two entries with different semantic
owners:

- **Query** is a presentation-owned route entry for package discovery and
  bounded streamed evaluation without an active inspection coordinate.
- **Workspace** is the product-issued Workspace subject presented as the entry
  to retained-coordinate management.

Inspection Subject Navigation continues to own Workspace, Package, Library,
Type, and Member identity. Inspect Web presents Workspace separately because it
manages retained Packages, while Package, Library, Type, and Member form the
progressively narrower active-coordinate subject strip:

- **Package** means one selected package-adapter coordinate.
- **Library** means all admitted libraries or one library in that coordinate.
- **Type** means one selected type in the active Library subject.
- **Member** means one selected member of the active Type.

Current Browser platform rows remain working host-local inventory behavior
outside shared Scope and Navigation. This cutover neither suppresses those rows
nor relabels them Package, but it does not give them a product-owned subject or
overview. A future Platform or other structural subject requires its own named
consumer and focused owner contract.

### Persistent navigation composition

Surface Composition places this owner's two persistent navigation
presentations before the primary content:

1. Row one renders the application-scope strip followed by the Slideable
   Subject Strip between the product control and the Shell Interaction-owned
   history, Search, and Application menu controls.
2. Row two renders the icon-backed ordered active subject path before any
   page-level contextual working-surface actions.

The two rows together follow the CLI's product-to-subject-to-inspector grammar
but are not command text. Inventories, hierarchy menus, and other target
navigation stay inside the working surface.

### Application scope strip

The leading application-scope strip renders `Query` and `Workspace` as one
presentation composition. Query remains semantically separate from Inspection
Subject Navigation; Workspace retains its product-issued subject identity and
action even though it is rendered outside the inspection-subject strip. Query
is selected only on `/query`; Workspace is selected only while
retained-coordinate management is visible. Neither remains selected merely
because an inspection coordinate was reached through it.
The strip is navigation rather than a tablist: the current Query or Workspace
surface uses `aria-current="page"`, and ordinary Package, Library, Type, or
Member inspection leaves both entries without `aria-current`.

Selecting Query enters the routed query surface through Navigation Consumer's
history and focus contract. A return without a new seed restores the current
session's request and outcome rather than resetting them. Selecting Workspace
submits the product-issued Workspace action and shows retained-coordinate
management. The Query entry issues no product subject identity, and the strip
issues no Package, Library, Type, Member, or lens identity.

The application-scope strip uses a quieter treatment than the subject and
inspector strips. Surface Composition gives it lower responsive priority: it
yields before either inspection strip reduces required identity. A selected or
focused control is not removed without the focus transfer and alternate access
owned by the composing surface.

### Slideable Subject Strip

This stable section anchor continues to identify the row-one subject and
inspector composition. The target interaction is no longer slideable:
[#6158](https://github.com/richlander/dotnet-inspect/issues/6158) replaces
manually positioned windows and cross-strip allocation controls with two
independently meaningful adaptive groups:

```text
[ subject tabs or chooser ] | [ inspector tabs or chooser ]
```

The subject group renders the ordered root, Library, Type, and Member
descriptors supplied by Inspection Subject Navigation. The inspector group
follows it and renders the effective owner-ordered inspector inventory supplied
by View Facet Registry, including retained-coordinate inspectors while
Workspace is active. Subject changes replace the inspector inventory;
inspectors never become workspace coordinate switchers or inspected-subject
identities. Application and contextual actions are not inventory items in
either group.

Each non-empty group has exactly two presentation forms:

- **Tabs** render the complete owner-ordered inventory as full-label tabs. The
  group uses Tabs only when every tab fits at normal interactive size in its
  measured allocation. Tabs never scroll, truncate, wrap, or substitute short
  labels, icons, or indexes.
- **Chooser** renders one menu button labelled by the committed item plus a
  disclosure indicator. Activating it opens the complete owner-ordered
  inventory. A non-empty inspector inventory with no effective inspector uses
  `Choose inspector` and marks no item as committed. While Workspace is active,
  a non-empty subject inventory has no committed subject; its trigger uses
  `Choose subject` and likewise marks no item as committed.

An empty inspector inventory omits the inspector group and separator. The
subject group then receives the complete available width. No presentation
invents a selected inspector, changes descriptor order, or derives action
identity from a label, position, short form, or menu state.

The conventional comparison is a full-label tablist with directional scroll
controls, as demonstrated by
[Carbon tabs](https://carbondesignsystem.com/components/tabs/usage/) and the
[WAI-ARIA tabs pattern](https://www.w3.org/WAI/ARIA/apg/patterns/tabs/).
This design deliberately diverges under pressure: each constrained group uses
one current-label
[menu button](https://www.w3.org/WAI/ARIA/apg/patterns/menu-button/) rather than
adding its own scroll buttons. The accepted cost is one disclosure before a
hidden choice can be activated. The benefit is that the committed subject and
inspector stay readable when they exist, an honest `Choose subject` or
`Choose inspector` label appears when they do not, every hidden item is one
disclosure away, and each affordance names the group it controls. No code or
component architecture is copied from those comparisons.

#### Measured fit and allocation

The composite measures each group's complete full-label Tabs width and ideal
Chooser width after fonts, labels, status affordances, and normal control
styling settle. It evaluates the four possible subject/inspector form pairs
against the width Surface Composition assigns to the whole region:

1. Use Tabs for both groups when that pair fits.
2. Otherwise, among the mixed pairs that fit, use the pair with the greater
   **inline-choice gain**. A group's gain is its owner-ordered inventory count
   minus one when its Chooser represents a committed item, or the complete
   inventory count when it represents none. Labels, duplicate labels, and
   availability status do not change the count.
   A gain tie favors the subject group because subject identity establishes the
   context in which the inspector inventory is interpreted.
3. Otherwise use Chooser for both groups.

The same inputs and measurements always produce the same pair; focus, prior
representation, and resize direction do not bias the result. A mixed state is
therefore ordinary, not a breakpoint exception. The calculation uses measured
fit rather than viewport names or a special 390-pixel rule.

Chooser labels use their ideal full-label width when it fits. When both ideal
Chooser widths do not fit, the available width after the separator is divided
equally, with a rounding remainder assigned to the inspector. Each control
keeps its normal block and focus-indicator size while its visible label may
elide. The complete committed label remains its accessible name and focused or
hover disclosure. The composite stays inside its assigned page boundary and
does not introduce page-level horizontal overflow.

An open Chooser pins that group in Chooser form until it closes. Width changes
may adapt the peer, but never remove the open menu or its trigger. Escape
closes, returns focus to the trigger, then atomically re-evaluates the ordinary
measured pair; if Tabs replace that focused trigger, focus moves to the
committed tab or the first owner-ordered item as defined below. Tab first
closes and advances focus according to the current document order, then
re-evaluates while focus is outside the group, so representation replacement
does not redirect traversal. Measurement state is presentation-local: it does
not enter workspace packets, Share URLs, browser history, product navigation
results, or retained user preferences.

#### Tabs interaction

Both roomy groups use the same manual-activation tab convention:

- each tablist has one roving tab stop, initially the committed tab or, when
  that group has no committed item, its first owner-ordered item;
- Left and Right Arrow move focus without activation;
- Home and End move focus to the first and last tab;
- unavailable and failed tabs remain focusable and expose their owner-issued
  evidence;
- Enter or Space activates a focused available tab by submitting its opaque
  owner-issued action identity and issuing generation; and
- activating the committed tab or a disabled tab starts no transition.

Moving focus never changes `aria-selected`, the active subject, the effective
inspector, the content panel, URL, or browser history. This deliberately
replaces the subject group's automatic arrow-key activation so subjects and
inspectors use one predictable interaction language.

The same descriptor-state rules apply in both forms:

- the committed item is selected in Tabs or checked in Chooser, carries no
  transition action, and activation is a no-op; Chooser activation closes and
  returns focus through the ordinary close sequence;
- a non-current available item is focusable and activation submits its newly
  issued opaque action identity with the issuing generation;
- an unavailable or failed item is focusable, disabled, and preserves its
  distinct owner-issued evidence without activation; and
- `Selection required` is an enabled, uncommitted `Choose a member` action with
  no product action identity. In Tabs it remains unselected and controls the
  Member choices surface. In Chooser it is an ordinary `menuitem`, not a radio
  item. Activation performs the local Member-choice focus action defined under
  [Subject availability and reconciliation](#subject-availability-and-reconciliation).

#### Chooser interaction

Each Chooser follows the WAI-ARIA menu-button pattern. Its button identifies
the owning group, exposes `aria-haspopup="menu"` and `aria-expanded`, and
controls a bounded menu. Ordinary subject and inspector descriptors use
`menuitemradio`; the committed item alone is checked, and a group with no
committed item has no checked radio item. The `Selection required` action uses
`menuitem` as defined above. Unavailable and failed entries remain discoverable
with `aria-disabled="true"` and preserve their distinct reason or diagnostic.

Enter, Space, or pointer activation opens the menu without activating an item.
Focus enters on the committed item when one exists, otherwise the first
owner-ordered item. Up and Down Arrow, Home, End, and supported menu typeahead
move focus without changing committed state. Enter, Space, or pointer
activation commits an available focused item. Escape closes without commit and
starts the atomic trigger-return and fit sequence above. Tab closes without
commit and follows the traversal sequence above. The popup remains bounded to
the viewport and scrolls internally when its complete inventory is taller than
the available space.

Opening, browsing, cancellation, and responsive movement never create parallel
selection state or submit a product action. A committed action closes the menu
and follows Navigation Consumer's ordinary transition, focus, failure,
authority, and history contract.

#### Responsive focus and replacement

A presentation-local change from Tabs to Chooser transfers focus from a tab in
that group to the new trigger before removing the tablist. It does not open the
menu or commit the previously focused item. A change from a closed, focused
Chooser to Tabs transfers focus to the committed tab, or to the first
owner-ordered tab when that group has no committed item. When focus is outside
the changing group, representation changes do not move focus.

An open Chooser remains mounted across width-only changes, so its focused item
and menu traversal survive. Focus continuity is keyed by the stable descriptor
identity, not its generation-scoped action identity. A new installed inventory
in the same renderer lifetime preserves menu focus when that descriptor
identity remains present, including when its availability changes, and rebinds
activation to the newly issued action identity and generation. If the
descriptor is removed or the renderer is replaced, Navigation Consumer's
synchronous parking, replacement, and current-effect-authority rules govern;
this owner never retains an action, menu item, or DOM target from the replaced
generation.

#### Retirement and adoption

This model retires Navigation Presentation's allocation ladder, allocation
buttons, manually retained inline windows, wheel sliding, edge indicators, and
subject/inspector Short Label, Icon, and Index fallbacks. The implementation
must remove those consumer paths when it adopts the new model rather than
keeping a compatibility mode. `SlideStrip` remains a separately owned reusable
control; this document neither changes its contract nor decides whether that
control has enough remaining adoption to retain.

Issue #6158 is the overall end-to-end tracker for the Browser host. Its counted
path has three focused slices:

1. lock this Navigation Presentation contract and its production gates;
2. implement both adaptive groups in the Browser host and delete the retired
   Navigation Presentation interaction in the same production slice,
   [#6276](https://github.com/richlander/dotnet-inspect/issues/6276); and
3. reconcile the now-unconsumed SlideStrip owner in the separate focused
   [#6277](https://github.com/richlander/dotnet-inspect/issues/6277), retaining
   it only with a justified consumer or retiring its implementation and stale
   first-adopter documentation.

The second slice completes the user-visible navigation replacement. The third
closes the existing-architecture retirement plan without broadening this
owner's contract. This is an existing Browser-specific presentation path, not
a new shared product substrate or a CLI rendering domain.

### Inspected target

The inspected target occupies the leading allocation of the second shell row
owned by Surface Composition. It is not part of either pane. Its primary
advertisement is an ordered typed path:

```text
System.Text.Json > System.Text.Json.JsonSerializer > DeserializeSync
```

The path contains the applicable Package, Library, Type, and Member display
identities supplied by their owners. Workspace renders `Workspace`. The
presentation does not parse one display string to derive another, and the
segments are orientation rather than inert navigation breadcrumbs.

The root segment uses a strong neutral treatment. Intermediate ancestors are
muted and yield width before the current leaf. The current leaf uses the shared
purple accent and stronger weight while remaining bounded so it cannot consume
the complete row. When the root is the only segment, root treatment wins rather
than recoloring or artificially capping that identity as a descendant leaf.
The complete path remains in the accessible name and title when visible
segments elide.

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
Current host-local Platform rows may use their own marks without creating a
shared structural subject. The `dotnet-inspect` bot retains its product-mark
slot before the adjacent inspected target.

NuGet Gallery's header logo is recorded separately for a future
source-attribution affordance:
`https://nuget.org/Content/gallery/img/logo-header-94x29.png`. It is NuGet
identity, not a package icon, and must not replace either an owner-issued
package icon or the default package fallback.

Separating the inspected target into row two prevents a long path from changing
row-one Search or history allocation. The target yields only to page-level
contextual actions supplied in its own row.

The Subject and Inspector region contains no Share or separate `Copy name`
action. Copy belongs
to the segment whose typed identity is being copied; the shell-owned
Application menu exposes canonical workspace Share outside both groups as
placed by
[Inspect Web Surface Composition](inspect-web-surface-composition.md#shell-navigation-and-application-actions).

Browser Back, Forward, Search, and the Application menu remain outside the
typed target and do not become breadcrumbs. Shell Interaction owns their
behavior; Surface Composition owns their row-one placement and pressure order.

### Workspace surface

Workspace is the product-issued subject presented as the persistent
application-scope entry point for workspace packet inspection and
retained-coordinate management. The primary inventory is
the set of product-issued packets, not the deduplicated runtime workspaces that
realize them. A packet composes its Workspace, navigation, and initial view as
defined by [Workspace Definitions](workspace-definitions.md); two packets remain
separately selectable when they reuse the same underlying Workspace. The first
browser adoption retains resolved product demo scenarios for the current
session. Inspect Web has exactly one live runtime Workspace. The home page
exposes one **Demos** entry that opens this subject; it does not expose one
button per demo or create a Workspace switcher.

The `/demos` application route presents every entry exposed by the application
product demo catalog, in product order. Listing carries its owner-issued stable
ID as action identity, renders its title and summary, and starts no demo
resolution, acquisition, inspection, or graph work. Each entry exposes a
separate explicit **Open demo** action. Activating that action resolves only the
selected definition and uses its existing replace-and-restore or product-run
path to replace the sole live Workspace. A failed Open demo action keeps the
catalog and prior Workspace available, surfaces a retryable failure there, and
returns focus to the selected demo action.

Available definitions and runtime state remain separate. A demo title does not
rename the Workspace, claim that the definition uniquely owns the loaded
coordinates, or imply that its initial view is still active. The loaded
Workspace section reports runtime state without inferring demo identity from
matching coordinates. Categories, filtering, and separate Aspire or
performance-demo entry points are residual; the first adoption lists every
current demo.

The same content pane separately lists the runtime Workspace's loaded
coordinates with:

- coordinate identity and acquisition kind;
- optional owner-issued current-subject context;
- loading, ready, or failed state; and
- an explicit Close action.

The page's named local Save action and compact saved-definition list are owned
by [Saved Workspaces](inspect-web-saved-workspaces.md). Saved entries are
definitions that may replace the one live Workspace, not another Workspace
inventory or a switcher between simultaneously live Workspaces.

The Packages section's compact [Add package](inspect-web-workspace-add-package.md)
action reuses package search to append a resolved coordinate without replacing
current members. It does not change saved definitions or leave Workspace.

The transitional Browser-owned NuGet close control is implemented through
[Package-row removal](inspect-web-package-removal.md), shared with Home Search.
That focused owner governs the existing Browser successor and empty `/demos`
behavior until product-owned Close is adopted.

Opening a demo, and closing a coordinate after product-owned Close adoption,
submits its owner-issued identity and renders the returned workspace outcome.
For those product-owned actions, the UI does not choose a subject, lens,
successor, or fallback.

Closing an inactive coordinate preserves the active coordinate's inspection
state and keeps Workspace selected. Closing the active coordinate selects its
successor while remaining in Workspace, as governed by the current Close owner.
The `/demos` entry route is an in-session catalog view: it preserves currently
loaded coordinates while open,
but a direct visit or refresh starts with an empty Workspace. After an Open demo
or coordinate action returns to a canonical Workspace URL, Share and refresh
preserve the Workspace subject, its application-scope presentation, and its
retained coordinates. The home-demo packet inventory is session-scoped until
scenario identity is part of the share format; after refresh, the generic
current Workspace remains viewable without reconstructing a demo identity from
matching coordinates.

Workspace renders stable focus targets for its heading, every demo entry, and
every coordinate action. Post-result focus and failure
handling are owned by
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md#workspace-result-focus).
Its content panel is labelled by the active Workspace application-scope
control, including when a cold catalog has no inspection-subject entries.

Workspace also exposes the same Search and Open actions as the shell. It does
not infer source identity, package equivalence, local-file correspondence, or
a composite workspace name from display labels.

### Lens navigation semantics

The lens strip is derived only from the current navigation snapshot's
owner-ordered lens descriptors. A non-empty collection uses the adaptive Tabs
or Chooser form defined above, including identically labelled items owned by
different subjects. An effective lens is selected or checked programmatically
rather than conveyed by color alone. When no effective lens exists, every tab
has `aria-selected="false"` or every radio item has
`aria-checked="false"`. An empty descriptor collection omits the inspector
group and leaves the no-effective-lens status region as the content following
the subject group.

In Tabs form, every lens or Member section uses `role="tab"` and
`aria-selected`. The tablist has the accessible name `<Subject> lenses`. The
effective tab references its panel with `aria-controls`; the panel uses
`role="tabpanel"` and is labelled by that tab. Without an effective lens, tabs
do not reference a nonexistent panel.

In Chooser form, the effective content container uses `role="region"` and is
labelled by the persistent closed-or-open Chooser trigger. The trigger's
menu-control relationship remains reserved for its popup; it does not claim
the content region through `aria-controls`. A Tabs-to-Chooser replacement
updates the content role and accessible-name owner to the installed trigger
before removing the active tab. A Chooser-to-Tabs replacement updates them to
the installed effective tab before removing the trigger. Menu opening,
cancellation, and Tab dismissal do not remove or rename the trigger, so the
content never retains a dangling reference. Without an effective lens, no
content panel or region exists in either form.

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
surface. It is neither `aria-current` nor `aria-disabled`.
Activation is a local presentation action: it closes the hierarchy menu and
moves focus to the first owner-ordered visible Member row in the navigation
pane. If host filters hide every row, focus moves to the Member text filter
instead. At a narrow viewport the UI first switches the content frame to its
Member inventory pane, then applies the same row-or-filter focus rule.
Each row's product-issued activation state governs any later commit. Opening
the choices changes no snapshot, URL, or history and does not invent a default
Member.

The Workspace application scope, row-one subject/inspector region, row-two
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
Library context and links back to the Library subject for changes. When that
Library subject is already active, the same back control returns to the
enclosing coordinate root instead. Its accessible name retains the visible
Library name; the accessible name and tooltip identify the actual parent
destination. It is not a second library selector, and the UI does not
recalculate context, eligibility, or retention from assembly membership.

When the product surface identifies colliding types under `All libraries`, type
navigation qualifies only those rows with their product-owned defining library.
If a colliding Type is selected, the Subject and Inspector region also shows its
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
dotnet-inspect  Workspace Package Type Member | Overview ...  ← → Search ☰
⬡ System.Text.Json

Package coordinate
Version 10.0.0   Framework net10.0
```

The trailing Application menu occupies its own row-one Surface
Composition-owned slot;
it is not a subject or inspector item.

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
inventory/detail push state are owned by
[Inspect Web Surface Composition](inspect-web-surface-composition.md#responsive-composition).

## Non-claims

This document does not define browser-history classification, canonical-URL
composition, effect-authority validation, synchronization debt, or
destination-lifetime focus and announcement ordering. It does not define
selector-pill visual states or progressive filter disclosure. It does not
define shell actions, modal/routed classification, or page-level placement.
It does not define a draggable divider or user-persisted width allocation for
the subject and inspector groups.
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
  `no effective lens renders status without a selected inspector or panel`
  covers Tabs, Chooser, non-empty, and empty descriptor collections for
  unavailable and failed outcomes.
- `navigation-consumer.test.ts`:
  `unavailable and failed navigation options preserve distinct evidence`
  covers lens tabs and Chooser items, hierarchy-menu items, and Library-listbox
  options.
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
- `scope-bar.test.ts`:
  `adaptive subject and inspector groups choose one measured presentation`
  covers all four Tabs/Chooser pairs, complete full-label fit, deterministic
  mixed-pair selection by the exact inline-choice-gain score, subject tie-break,
  empty inventories, no-effective-inspector and Workspace-active
  no-committed-subject states, equal constrained shares, complete accessible
  labels under visual elision, open-Chooser pinning, and the absence of
  allocation controls, compact representations, windows, edge indicators, and
  wheel-sliding state.
- `workspace-titlebar.spec.ts`:
  `adaptive navigation preserves committed state and focus across fit changes`
  covers manual activation for both roomy tablists, Tabs-to-Chooser and
  Chooser-to-Tabs focus handoff, an open menu surviving resize, menu
  cancellation and Tab dismissal, the Workspace-active retained-coordinate
  handoff with no committed subject, panel role and accessible-name continuity
  across both replacement directions and menu dismissal, current-item and
  `Selection required` activation, disabled evidence, same-lifetime
  stable-identity retention with new action rebinding, asynchronous replacement
  parking, and rejection of an outgoing generation's menu action or DOM target.
- `library-hierarchy.spec.ts`:
  `subject and inspector navigation stays explicit from wide to 390px`
  exercises the production Browser shell and bindings with deterministic
  facade results. It covers all-label, mixed, and dual-Chooser layouts;
  Package-to-Library activation; browsing without activation; explicit lens
  and subject commits; Escape cancellation; direct 390-pixel entry and reload;
  no-effective and empty inspector inventories; and unchanged focus, URL, and
  history during presentation-only transitions.

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
   focuses the Member text filter in the visible narrow inventory pane.
7. Supply a typed transition failure and confirm that it is visible without the
   UI selecting another subject and that focus returns to the subject
   menu-button invoker.
8. Confirm that every copyable inspected-target segment copies its own
   product-issued canonical identity rather than display text and that no
   separate `Copy target` action occupies the Subject and Inspector region.

### Adaptive subject and inspector navigation

1. Render Package, Library, Type, and Member with five Member inspectors at a
   width where both complete full-label tablists fit. Confirm that both groups
   use Tabs, every owner-ordered item is visible, and Left or Right Arrow moves
   focus without changing subject, inspector, panel, URL, or history. Confirm
   Enter activates the focused available item.
2. Narrow to a width where only one mixed pair fits. Confirm that the group
   exposing more additional inline choices uses Tabs and its peer uses Chooser.
   Repeat with equal additional-choice counts and confirm that subjects use
   Tabs. Widen and narrow through the same measurements in both directions and
   confirm that the same pair is selected without retained allocation state.
3. Narrow until both groups use Chooser. Confirm that the triggers read the
   committed subject and inspector, such as `Library` and `Overview`, expose
   their complete accessible labels, and remain inside the assigned shell
   region when visible text elides. Confirm that no allocation arrow, edge
   indicator, compact label, icon-only item, index, horizontal tab scrolling,
   or wheel-driven navigation remains.
4. Open the inspector Chooser, move focus to another available item, and
   confirm that the committed item alone stays checked and the content does not
   change. Resize through a width where inspector Tabs would otherwise fit and
   confirm that the open menu, trigger, and focused item remain mounted.
   Press Escape and confirm that the menu closes without a commit. When the
   ordinary fit still uses Chooser, focus ends on its trigger; when Tabs now
   fit, confirm that the atomic re-evaluation moves focus from the removed
   trigger to the committed tab.
5. Reopen each Chooser and activate an available item with Enter, Space, and
   pointer input. Confirm that only the exact opaque action identity and
   issuing generation are submitted. Browse a disabled item and confirm that
   its unavailable reason or failure diagnostic remains discoverable while
   activation is a no-op. Activate the checked item and confirm that the menu
   closes without a product transition. Activate `Choose a member` and confirm
   that it performs the local Member-choice focus action without a product
   action ID. Press Tab from an open menu and confirm dismissal without commit
   followed by ordinary document traversal even when the group changes to
   Tabs after focus leaves it.
6. Focus an inactive roomy tab and narrow until its group becomes a Chooser.
   Confirm that focus transfers to the trigger before the tablist is removed,
   without opening the menu or committing the focused item. For the inspector
   group, confirm that its active content atomically becomes a region labelled
   by the installed trigger before the active tab disappears. Cancel and
   Tab-dismiss the menu and confirm that the same label relationship remains.
   Widen while the closed trigger owns focus and confirm that focus moves to the
   committed tab and the content atomically returns to a tabpanel labelled by
   that installed tab. Repeat with a non-empty inspector inventory that has no
   effective inspector: the trigger reads `Choose inspector`, no item is
   checked, no panel or region exists, and widening moves focus to the first
   owner-ordered tab without selecting it. Repeat while Workspace is active
   with a retained coordinate: the subject trigger reads `Choose subject`, no
   subject item is checked or selected, Package remains the first owner-ordered
   roving tab, and the effective inspector stays independently committed.
7. Replace an open menu's inventory while retaining the renderer lifetime.
   Confirm that an exact surviving stable descriptor identity retains focus,
   including across availability or generation changes, while activation
   rebinds to the newly issued action and generation. Remove that identity or
   replace the renderer and confirm that no stale item or action survives;
   Navigation Consumer's parking and current-effect-authority rules determine
   subsequent focus. Install an empty inspector inventory and confirm that its
   group and separator are omitted while subjects receive the complete region.
8. Exercise Package-to-Library activation at 1440 pixels and 390 pixels, direct
   narrow entry, and reload. Confirm that the committed subject remains
   readable in Tabs or Chooser form. Browse and cancel both groups, then commit
   another inspector and subject. Confirm that presentation-only transitions
   preserve selection, focused external controls, URL, and history, while
   explicit commits follow the ordinary product transition contract.

### Lens inventory and outcomes

1. Supply owner-ordered available, unavailable, and failed descriptors,
   including one absent from every legacy browser lens array and three with the
   same display label, two of which also share a Summary.
2. Confirm that every descriptor appears once in exact owner order with its
   exact identity, label, status, reason, and diagnostic. Focus the
   duplicate-title items in both Tabs and Chooser form and confirm that each
   owner-issued Summary is visible and programmatically descriptive; when both
   summaries collide, confirm that the exact ID distinguishes them without
   replacing their labels.
3. Focus every disabled tab and Chooser item and confirm that unavailable and
   failed evidence is discoverable while activation remains a no-op.
4. Activate the legacy-absent descriptor and all duplicate-label descriptors.
   Confirm that moving focus did not select them, each activation submits its
   exact opaque subject-scoped identity, and the returned effective lens becomes
   the one selected tab and tabpanel in Tabs form or the one checked item and
   labelled region in Chooser form.
5. Supply a non-empty descriptor collection with no effective lens and an
   unavailable outcome. Confirm that no tab or radio item is selected, no panel
   or region exists, and the `Lens unavailable` status is labelled by the
   active subject.
6. Repeat with a failed outcome and confirm `Lens failed` preserves the
   diagnostic rather than presenting valid unavailability.
7. Supply an empty descriptor collection and confirm that the inspector group
   is omitted without introducing a locally familiar fallback lens.

### Workspace composition

1. Supply two open-coordinate descriptors with different optional subject
   context and status.
2. Confirm that Workspace renders those descriptors without deriving identity
   from their labels.
3. Activate and close entries and confirm that each action submits the opaque
   coordinate identity once and renders the returned workspace outcome.

Post-result focus and failure acceptance are specified by
[Workspace focus acceptance](inspect-web-navigation-consumer.md#workspace-focus-acceptance).
