# Inspect Web UI

This document owns the presentation and interaction language of the
`dotnet-inspect` website. It records reusable visual rules so equivalent
controls communicate state consistently across workspace, package, library,
type, member, metadata, source, and settings surfaces.

The rules are normative targets for `prototypes/inspect-web`. When the current
implementation differs, this document describes the intended behavior rather
than preserving the inconsistency.

## Ownership and boundaries

The Inspect Web UI owner defines:

- the visual meaning of shared control states;
- the interaction grammar for recurring website controls; and
- the composition rules that make equivalent controls look and behave alike;
- the shell hierarchy and responsive presentation of product-issued subjects;
- focus movement and consumer-side effect-authority handling for navigation;
- browser-history, canonical-state, and restoration expectations at the UI
  boundary; and
- the placement of Settings, Diagnostics, source provenance, and product
  promotion.

It consumes product-owned identities, labels, ordering, defaults, availability,
query semantics, workspace state, artifact-acquisition outcomes, and
package-source descriptors. In particular:

- [Product Vocabulary](vocabulary.md) remains authoritative for vocabulary
  values and selection semantics;
- [Artifact acquisition and workspaces](artifact-acquisition-and-workspaces.md)
  owns admitted inputs, provenance, workspace composition, and failures;
- [Browser package sources](browser-package-sources.md) owns browser source
  registration, eligibility, credentials, and producer identity; and
- [Untrusted data threat model](untrusted-data-threat-model.md) owns rejection
  and failure behavior for local and network inputs.

Individual inspect-web components retain their rendering, binding, and
state-transition responsibilities.

This document does not own:

- inspection or acquisition behavior;
- API, metadata, package, type, or member classification;
- vocabulary identities, labels, ordering, or defaults;
- artifact validation, grouping, provenance, or acquisition failure semantics;
- package-source resolution, authorization, credentials, or cache authority;
- subject or lens recommendation, reconciliation, or fallback;
- navigation snapshot revisions, intent ordering, or effect-authority validity;
- canonical packet encoding or decoding;
- CLI and library output formatting; or
- the internal implementation boundaries among inspect-web modules.

## Product dependencies

This document composes four adjacent owner contracts without defining them:

- [Inspection Subject Navigation](inspection-subject-navigation.md) owns
  inspection-subject descriptors, availability, initial recommendation, and
  reconciliation, plus retained-session intent and effect authority.
  [#5013](https://github.com/richlander/dotnet-inspect/issues/5013) strengthens
  that same owner's lens recommendation with non-vacuous role-first selection,
  a direct-Member rule, deterministic fallback, and all-non-success precedence.
- [View Facet Registry](view-facet-registry.md) owns stable facet IDs,
  descriptors, structural applicability, order, and facet-availability
  outcomes.
- [#4787](https://github.com/richlander/dotnet-inspect/issues/4787) owns stable
  portable fields, versioning, migration, valid combinations, per-coordinate
  view state, canonical packet projection, and restoration.
- [Browser package sources](browser-package-sources.md#default-feed-decision)
  owns browser source selection and the decision that first-run Gallery
  bootstrap does not become default-feed or acquisition-preference semantics.

Inspect Web renders those owner-issued descriptors and outcomes. Their product
semantics are not prerequisites for reviewing the UI composition in this
document and are not re-specified here.

## Current redesign

This is a coordinated information-architecture and density rework, not a set of
independent cosmetic changes.

| Area | Direction |
| ---- | --------- |
| Subject navigation | Use one single-line Workspace, coordinate, and current-subject command |
| Workspace selection | Replace package tabs with a Workspace surface |
| Package coordinate | Keep a compact package, version, and TFM argument beside `dotnet-inspect` |
| Library inspection | Select all libraries or one library within Library |
| Type headings | Let the inspection command identify API and Source; retain detail in Metadata |
| Filters | Collapse selector rows by default and summarize hidden restrictions |
| Selected controls | Use one accent selected-state treatment across selector families |
| Source provenance | Use a compact status/action row without validation prose or link glyphs |
| Search and opening | Use Spotlight for search and a separate local-artifact Open flow |
| Settings | Use one Settings experience with contextual entry points |
| Data bar | Show build identity, acquired source, CLI, and skill links on one line |

Together, these decisions move subject-specific controls and detail into the
view that owns them. Persistent chrome carries the `dotnet-inspect` Workspace
root control, shell actions, and one data line. Inspection surfaces add the
compact coordinate/subject command and lens navigation. Content views spend
their vertical space on the package, library, type, member, API, metadata, or
source material the user selected.

## Selector controls

Selector controls are compact pill-shaped buttons used to choose a value or
filter a result set. Type kind, member kind, accessibility, and member trait
controls use the same state language even when their values and selection
semantics differ. This section applies to selector pills, not to subject or lens
navigation.

### Selected state

The accent color is the website-wide indication that a selector value is
currently selected. It is not an accessibility color and must not be reserved
for an accessibility selector.

A selected selector uses all four of these visual signals:

| Property | Selected treatment |
| -------- | ------------------ |
| Border color | Accent |
| Border weight | Two pixels rather than the one-pixel rest border |
| Text | Accent |
| Background | Selected-control background |

The heavier border is the non-color visual cue. Control dimensions reserve
space for it so selection does not move adjacent pills. Border, text, and
background colors reinforce the state. The treatment applies uniformly to
concrete values such as `public` and `method` and aggregate values such as
`all access` and `all kinds`.

The current implementation is inconsistent: the shared
`.namespace-chips button.active` rule gives kind and trait selectors a neutral
active treatment, while `.access-chips button.active` gives accessibility
selectors the accent treatment. The target design is one shared selected
treatment with no accessibility-specific active-state override.

### Other states

Selector states must remain distinguishable from selection:

| State | Treatment |
| ----- | --------- |
| Rest | One-pixel standard border, subdued text, transparent background |
| Hover | Stronger neutral border and text; never the selected accent treatment |
| Selected | Two-pixel accent border and accent text with the selected-control background |
| Keyboard focus | A focus indicator in addition to the underlying rest or selected state |
| Disabled or unavailable | Explicitly disabled treatment and native disabled semantics |

Hover and keyboard focus are transient interaction states. Neither may make an
unselected value look selected, and neither may erase the selected treatment.
Subdued unselected values must remain visibly interactive rather than appearing
disabled.

### Semantic and accessibility contract

- Visual selection reflects the control's actual selection state, independent
  of which selector family rendered it.
- Every selector pill exposes `aria-pressed="true"` or
  `aria-pressed="false"` consistently.
- Color is supplementary. Border weight and programmatic pressed state carry
  the same information.
- A selected aggregate value uses the same treatment as a selected concrete
  value. The word `all` does not create a neutral or secondary selected state.
- Multi-select controls apply the selected treatment to every selected value;
  single-select controls apply it to the one selected value.

This topic does not change which value is selected by default or whether a
particular selector is single-select or multi-select. Those are separate
behavioral decisions.

### Progressive disclosure

Selector rows are hidden by default in the type and member navigation panes.
The recovered vertical space belongs to the type or member list, which is the
primary content of each pane.

Each pane provides one compact `Filters` disclosure button beside its text
filter. The button expands or collapses the complete selector region for that
pane:

- type kind and accessibility selectors expand together;
- member kind, accessibility, and trait selectors expand together; and
- collapsing the region never clears or changes a selection.

The Type navigation pane does not retain a second library filter. The active
Library subject controls whether Package and Type navigation show types from
all libraries or one library. Library selection is subject context, not a hidden
Type filter dimension.

The disclosure state is user-controlled after the pane first appears. It
survives rerenders and selector changes while that pane remains active so
choosing one value does not immediately hide the controls. It is not persisted
as a user preference across browser sessions.

#### Collapsed summary

Hidden controls must not create hidden state. The disclosure button summarizes
any selection that restricts the visible result set:

- the visible label becomes `Filters · N`, where `N` is the number of
  restrictive selector dimensions;
- the count uses the accent color without giving the button selector selected
  styling;
- the accessible name identifies the active restrictions.

For example, a type pane showing only public types has one restrictive
dimension even though `public` is the product default. Its collapsed control is
presented as `Filters · 1` with an accessible name such as
`Filters · 1, accessibility: public`. A member pane with `all kinds`,
`all access`, and `all traits` has no restrictive dimensions and presents a
neutral `Filters` button.

The count is by selector dimension, not by selected value. Selecting `public`
and `protected` in one multi-select accessibility control still contributes
one restrictive dimension.

#### Disclosure semantics

- The disclosure button is not a selector and does not expose `aria-pressed`.
- The button exposes `aria-expanded` and references the selector region with
  `aria-controls`.
- Expanding the region does not move keyboard focus automatically. The user
  may continue to the first selector through ordinary tab order.
- Collapsing the region returns focus to the disclosure button when focus was
  inside the region.
- A restored or deep-linked filter is summarized while collapsed; it does not
  force the selector region open.
- The collapsed summary supplements the visible result count. It does not
  replace result text such as `20 of 84 member groups`.

## Subject headings

The single-line inspection command is the common orientation point for the
current Package, Library, Type, or Member subject. A content lens does not
repeat that identity merely to create a local hero heading.

### API and Source lenses

The API and Source lenses begin with their primary content. When the snapshot
has an effective lens, their accessible heading relationship includes both the
product-owned active-subject label from the inspection command and the active
lens label. While an inspection surface is active, the active-subject token is
the visible level-one heading for a root, Library, Type, or Member subject. The
lens panel's `aria-labelledby` references that label and the active lens tab.

When the snapshot has no effective lens, the UI renders no `tabpanel`. A status
region references the active-subject label and its visible `Lens unavailable`
or `Lens failed` heading. It explains the returned outcome without fabricating
an active tab, panel, or fallback lens.

Home, Workspace, and Diagnostics replace the coordinate, subject, and
`Copy target` portion of the inspection-command region with their own visible
level-one heading. The persistent `dotnet-inspect` root control remains
available and opens Workspace. Returning to an inspection surface restores the
complete command and its active-subject heading; two visible level-one headings
are never rendered for one routed surface.

They do not repeat:

- the kind icon;
- the namespace eyebrow;
- the type or member name;
- the declaration signature;
- the member count;
- the accessibility summary;
- the target framework;
- the library; or
- the package and version.

API content begins immediately after lens navigation. Source places only its
compact provenance and action row before source content. Annotated Source puts
its **Copy** and **Explore** actions in the page-owned inspection-command row,
begins immediately with highlighted source content, and places compact
provenance after that content. The removed fields do not leave placeholders or
reserved vertical space. They are also not moved into collapsed duplicate
headers on either lens.

This makes the API surface or source document the primary content of its page
and increases the amount visible without scrolling.

### Metadata lens

The Metadata lens retains the detailed type heading. It is the type-level view
for kind, namespace, declaration shape, target framework, library, package, and
version context.

The inspection command remains the common orientation point between API,
Metadata, and Source. Switching lenses changes the amount of surrounding detail,
not the selected subject or its display identity.

## Source provenance

Successful source provenance is presented as a compact status and action row,
not as an explanation of the product's safety mechanisms. This rule applies to
type, member, and graph source surfaces.

In the Type or Member view, a Source lens is ordered as:

1. Compact source provenance and actions.
2. Source content.

No type metrics or metadata summary appears between these elements.

For checksum-verified source resolved through PDB information, the visible
status is compact:

```text
PDB Source                                      open source   copy
PDB Source                                                    copy
```

`PDB Source` implies that the source satisfied the product's PDB checksum
verification contract. The page does not repeat `PDB-checksum-verified`, the
SourceLink transport, repository URL, or commit hash in explanatory prose.
Those facts remain part of the product result; they do not need to occupy the
source viewport.

The `open source` action appears only when the product result supplies an
optional producer-authorized browse URL. A raw resolved or fetch URL and
provenance prose do not establish that authorization, and the UI does not parse
prose to infer it. The action uses plain text with no trailing arrow or
external-link glyph. Its accessible name may state that it opens a new browser
tab. The `copy` action remains available with or without a browse URL.

`Decompiled source` remains the concise status for product-generated source.
If a PDB source attempt failed and the product returned a meaningful limitation
with a fallback, that limitation remains visible as a separate failure note.
Successful provenance text must not be used to explain every validation step.

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

A successful entry action that opens an inspection surface focuses its
active-subject level-one heading. When an action remains in Workspace, focus
moves to the returned active entry. If a closed entry has no returned active
entry, focus moves to the next rendered entry at its former position, then the
previous entry, then the Workspace heading. A typed failure retains focus on
the invoking entry while surfacing the failure.

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

The selected subject controls every Library lens:

- `All libraries` requests a coordinate-wide result over the complete library
  set.
- An individual library requests the same lens for only that assembly.
- The selected subject persists when switching among returned Library lenses.
- Changing package version or TFM submits the realized coordinate result to
  Inspection Subject Navigation, which reconciles from its installed snapshot.

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

`All libraries` is a real aggregate inspection mode, not a client-side
concatenation of independently rendered library pages. The UI consumes an
owner-provided aggregate result that defines ordering, identity, deduplication,
and partial-failure behavior.

Each Library lens supplies explicit aggregate and single-library capability
facets with visible rejection reasons. If the selected lens cannot provide the
current subject arity, it reports that mode as unavailable. This rule is
symmetric: an aggregate-only lens does not pretend to provide one-library data,
and a single-library-only lens does not pretend to provide an aggregate.

Switching lenses does not silently change the Library subject to obtain a
supported arity. The unavailable result identifies the mismatch and leaves the
subject control available so the user can choose a supported subject. The UI
must not infer capability from source family or transport method.

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

## Canonical location and refresh

For a package-backed workspace, the visible URL keeps only a human-readable
package courtesy identity. Durable workspace state remains in the
product-owned canonical packet:

```text
?package=System.Text.Json&w=<opaque-canonical-packet>
```

The UI does not expand Package, Library, Type, Member, filter, or source
identity into readable path segments merely to make them URL-addressable. A
non-package workspace omits the `package` courtesy field rather than placing a
local path or other sensitive coordinate in readable URL state.

The workspace-definition work tracked by
[#4787](https://github.com/richlander/dotnet-inspect/issues/4787) owns which
workspace state is portable and how it is encoded, decoded, and restored. The
UI receives a typed projectable, non-projectable, or failed outcome; it never
inspects compact fields.

- A projectable outcome supplies the opaque canonical packet and an optional
  product-issued package courtesy identity. The UI location adapter composes
  `w=<packet>` and, only when that courtesy identity is present,
  `package=<identity>`.
- A non-projectable outcome leaves the current presentation session-local,
  supplies the visible reason used by explicit Share, and removes any prior
  `w` and package courtesy fields before the transition's push or replace.
  Session history may retain the live presentation, but refresh starts without
  a stale canonical workspace.
- A failed transition retains the prior workspace and URL and surfaces the
  typed failure.

Browser history uses the same classification:

- push a history entry for an applied explicit action that performs Home,
  Workspace, or Diagnostics routing; opens, closes, or activates a coordinate;
  changes package version or TFM; changes the Package, Library, Type, or Member
  subject; or changes the active lens or Member section;
- replace the current entry for committed filter changes and portable overload,
  body, or source-target refinements, plus maintenance, dedicated
  synchronization, or non-applied outcomes whose `Synchronization required`
  disposition installs refreshed or reconciled state; and
- adopt the browser-selected entry without calling `pushState` or
  `replaceState` when initial shared-link activation, refresh, Back, or Forward
  restores the exact requested state. If that restoration instead installs a
  changed unavailable, synchronized, or reconciled snapshot, replace the
  selected entry with the returned canonical state. If Back or Forward returns
  a `Current` unavailable-without-replacement, rejected, failed, or aborted
  result, replace the browser-selected entry with the installed snapshot's
  canonical location, surface and announce the exact outcome, and focus the
  retained destination heading or persistent shell fallback. If the same
  semantic outcome is `Synchronization required`, install its complete
  snapshot and replace the selected entry with that snapshot's canonical
  location before presenting the outcome. Neither case writes a new entry, and
  both let a later traversal continue past the failed location; and
- do not mutate history for hover, focus, uncommitted listbox movement,
  disclosure animation, or incidental scroll position.

The session-scoped location adapter tracks whether the browser-selected entry
is aligned with the installed snapshot. A Back or Forward event records one
UI-local unresolved traversal serial and the installed snapshot's retained
canonical location before submitting product restoration. This serial is
presentation bookkeeping, not product intent or effect authority.

A newer Back or Forward event replaces the unresolved traversal with its own
serial because it now owns the browser-selected entry. Before the UI submits
any non-browser intent or dedicated synchronization request while an unresolved
traversal remains, it synchronously replaces the selected entry with the
retained canonical location and marks it aligned. The later intent then uses
its ordinary push, replace, or no-write classification. A synchronization
request is not a product navigation intent, but it uses this same pre-request
alignment rule because its response carries no traversal serial. Exact
restoration adoption, changed-snapshot installation, or current-authority
location realignment also marks the matching traversal aligned. Product-side
discard or stale-authority abandonment performs no history write; its successor
has already replaced the traversal obligation, realigned the selected entry
before submission, or preserved the session-scoped obligation for remount.

Destination destruction does not clear an unresolved traversal. Before a
replacement destination renders after remount, the location adapter
synchronously replaces the still-selected entry with the retained canonical
location and marks that traversal aligned. This repair is independent of
snapshot-synchronization debt: a `Current` non-installing restoration can
require it even though the product acknowledgement receipt is already current.

A future packet projection does not decide its own history granularity. It
inherits this UI-owned push, replace, or adopt classification.

On browser refresh or shared-link activation, the UI submits the opaque packet
to the product codec and renders its atomic success or typed failure. It does
not use the readable package courtesy field as a fallback workspace.

## Shell actions

The global shell uses visible text actions:

```text
Home   Search   Open   Settings
```

An optional decorative glyph does not replace any visible label.

### Product transition lifecycle

Subject, lens, coordinate, version, TFM, and canonical-restoration actions use
one retained Inspection Subject Navigation session. The UI treats snapshot
generations, action IDs, intent tokens, and effect authority as opaque. It does
not mint, order, compare, or reconstruct them.

A navigation destination surface is any inspection surface or routed Home,
Workspace, or Diagnostics surface that consumes a navigation result. Its
lifetime begins when its renderer is mounted for a returned destination and
ends when that renderer is replaced or unmounted. Modal and transient controls
may invoke navigation, but they do not independently consume the returned
navigation authority.

Each current product result has two orthogonal classifications. Its semantic
outcome decides what the UI presents:

| Outcome | UI presentation |
| ------- | --------------- |
| Applied | Present the requested transition from the returned snapshot |
| Unavailable | Show the exact unavailable result without fallback |
| Rejected | Show the rejection |
| Failed | Show the diagnostic |
| Aborted | Show the typed prerequisite failure |
| Superseded | Receive no consumer result or authority and produce no visible effect |

Its synchronization disposition decides whether the complete returned snapshot
must be installed:

| Disposition | UI obligation |
| ----------- | ------------- |
| Current | Use the already installed snapshot and perform only the semantic outcome's remaining effects |
| Synchronization required | Install and render the complete returned snapshot under the current effect authority before presenting the semantic outcome or acknowledging |

Outcome names do not authorize the UI to infer whether product and consumer
state agree. In particular, unavailable-without-semantic-replacement, rejected,
failed, and aborted results may still require installation because an earlier
result advanced the retained product session before the consumer acknowledged
it. The UI neither compares snapshot revisions nor reconstructs the missing
state. It consumes the typed disposition and installs only the complete
snapshot carried by that result.

Installation is one atomic consumer effect: it commits the returned snapshot,
its rendered content, and the UI-owned canonical URL and history-commit
classification. The semantic outcome remains visible after installation; a
failed synchronization does not become an applied navigation result merely
because it carried newer state. A `Current` unavailable-without-replacement,
rejected, failed, or aborted result retains the installed snapshot, URL, and
ordinary history classification. Superseded work never reaches the consumer.

The non-installing rules above describe `Current` results whose initiating UI
action has not already changed the browser entry. After Back or Forward selects
an entry, such a result instead replaces that selected entry with the installed
snapshot's canonical location as defined by the browser-history classification.
This is location realignment, not a snapshot installation or successful
restoration. A `Synchronization required` result instead installs its complete
snapshot and aligns the selected entry with that installed state.

Location realignment is a required consumer effect. Immediately before the
history write, the consumer validates both the returned authority and the
matching unresolved traversal serial. If either is stale or foreign, it changes
neither canonical URL nor history and abandons the authority; the current
successor owns the still-current alignment obligation. Realignment completes
before acknowledgement. If an implementation defers the write, its callback
repeats both checks at execution time.

An applied result uses the initiating explicit action's push-or-replace
classification or a browser restoration's adopt classification. An unavailable
result whose disposition requires a changed reconciled snapshot replaces the
current history entry because refreshed evidence, not the requested unavailable
target, produced that state. A rejected, failed, aborted, or
unchanged-unavailable result with `Synchronization required` also replaces the
current entry: the write records product state committed before this semantic
outcome, not a successful activation of the rejected or failed request. These
replacement rules supersede an initiating adopt classification and never push
an entry for a subject or lens the user did not activate.

A dedicated synchronization result has no semantic navigation change. When no
new browser traversal has selected an entry since its request, it installs the
complete current snapshot under fresh authority and replaces the current entry
with that snapshot's canonical location. The request carries no traversal
serial. If Back or Forward selects an entry after the request and before the
result is consumed, the result is foreign to that newer selection: the consumer
abandons its authority without installation or history mutation, preserves
synchronization debt, and lets the traversal's current result or a later fresh
request discharge it. A consumed synchronization result preserves surviving
focus and announces only a visible change; after remount, detached-focus
recovery uses the new destination heading or persistent shell fallback.

Product-initiated maintenance results use the same disposition-driven,
authority-validated installation and acknowledgement lifecycle. A
`Synchronization required` maintenance result replaces the current history
entry and updates the canonical URL from the returned projectable state; a
`Current` result performs no snapshot or history write. Maintenance does not
move focus merely because evidence refreshed. If installation removes the
focused element, the focus-preservation rule below applies. The live region
announces a maintenance result only when it changes visible status, the active
subject, or the effective lens.

Before installation or location realignment, the UI asks the session whether
the returned effect authority is current. Rendering may schedule later focus
and status-announcement callbacks; each callback repeats the authority check at
execution time. Validation performed for installation or realignment is not
continuing authority for a later effect. A callback that finds stale or foreign
authority changes neither focus, visible status, active panel, canonical URL,
nor history.

The persistent `dotnet-inspect` shell owns one polite live region outside
replaceable destination renderers, with `role="status"`, `aria-live="polite"`,
and `aria-atomic="true"`. It is mounted empty before a destination renderer and
survives renderer replacement; a current-authority announcement callback
changes its text only after any required installation. An applied result
announces the returned active subject and effective lens. An unavailable,
rejected, failed, or aborted result announces the same visible reason or
diagnostic shown by the surface. Superseded work reaches no consumer and
announces nothing. A no-effective-lens region remains visible content; the
shell live region announces its exact heading and evidence rather than a
different hidden explanation.

Receiving a `Synchronization required` result sets the session-scoped
synchronization-debt marker. After all required location-realignment,
installation, focus, and announcement effects complete, the UI acknowledges
the authority. A `Synchronization required` result cannot be acknowledged
merely because its snapshot equals locally rendered state: installation must
have completed under that result's exact current effect authority. Successful
acknowledgement is the only consumer action that clears the marker.

If authority becomes stale before completion, the UI abandons it. Abandoning a
`Synchronization required` result preserves the marker whether abandonment
occurred before installation or after installation but before acknowledgement.
A later current result may discharge the debt through its own disposition and
authority. Otherwise, once no current result can complete it, the
session-scoped consumer requests dedicated synchronization from Inspection
Subject Navigation. It tracks only the outstanding obligation, never the
product revision.

The consumer keeps at most one dedicated synchronization request outstanding,
retaining its opaque request identity and originating destination lifetime
until the product settles it. Renderer replacement or destruction retires that
lifetime as a possible consumer but does not pretend that the product request
was cancelled. A response for a retired lifetime is recognized by its retained
request context and its authority is abandoned. Only after that request settles
may an outstanding marker issue a fresh request for the current lifetime. A
returned current semantic or maintenance authority must likewise settle before
another request is issued because acknowledgement of that result may discharge
the pending synchronization request. These consumer-side request rules do not
impose a product retry limit.

A session-scoped UI navigation consumer is the sole holder of returned
navigation authority and outlives individual routed and inspection surfaces.
When a navigation destination surface's renderer is replaced or unmounted, the
consumer abandons every returned authority associated with that lifetime before
discarding its callbacks. It also abandons a result that returns after its
destination was destroyed or remounted. A remounted surface has a new lifetime,
cannot consume callbacks from the destroyed one, and schedules a fresh
synchronization request when the marker remains set and no earlier request is
awaiting product settlement. Mounting and request creation are separate
consumer actions; the outstanding marker preserves the obligation between
them. Every admitted request retains its exact identity until product
settlement. Another abandonment preserves the marker and permits a later
request after the prior one settles; the UI imposes no retry ceiling.
Superseded work requires neither acknowledgement nor abandonment because the
product session discards it without publishing effect authority.

When current-authority installation replaces a destination renderer, the
consumer atomically abandons every other returned authority associated with the
outgoing lifetime before transferring the installing operation and its later
callbacks to the new lifetime. Replacement does not merely make old callbacks
eventually stale; it settles their authority before the old renderer is
discarded.

The common installation, focus, announcement, acknowledgement, abandonment,
and destination-lifetime obligations are modeled by
[`UiEffectLifecycle.tla`](models/inspect-web-navigation-consumer/UiEffectLifecycle.tla).
The model assumes that the product session supplies opaque authority, a
complete typed outcome, and the product-owned synchronization disposition,
then explores two bounded consumer operations across three mounted-surface
lifetimes so supersession, renderer replacement, destruction, remount, or a
dedicated synchronization response can intervene between every deferred
effect. It includes prerequisite abort, product-side discard of superseded
work, synchronization debt, bounded request identity, old-lifetime response
abandonment, and fresh remount requests without modeling product revisions. TLC
exhaustively checked 166,998 generated states and 117,928 distinct states at
depth 20. Separate mutation configurations produced a counterexample when
current-authority validation, disposition-driven installation,
install-before-focus ordering, deferred-focus separation, complete-effect
acknowledgement, acknowledgement debt clearing, destruction abandonment,
abandonment debt preservation, remount synchronization, request/lifetime
correlation, bounded request admission, persistent focus safety, or replacement
abandonment was removed. The model also requires every exact admitted
synchronization request to reach settlement. The dedicated recovery
configuration reached the complete trace in which an old-lifetime response is
abandoned before a fresh current-lifetime response is consumed. The
[model README](models/inspect-web-navigation-consumer/README.md) records the
tool versions, bounds, action coverage, mutation results, and deliberate
abstraction of exact browser-history classification and browser-trigger-specific
entry realignment. The named restoration and synchronization gates carry those
additional conformance claims. This proves the finite design model; the
implementation gates below establish conformance in Inspect Web.

### Shared transient-surface semantics

Coordinate and subject menus use menu-button semantics. Their invoking control
exposes `aria-expanded` and `aria-controls`; opening moves focus to the current
item or first item. Arrow navigation includes unavailable and failed
`aria-disabled` items so their reasons and diagnostics remain discoverable;
Enter activates a non-current available item through its product action or
opens the Member choices for a `Selection required` item through the local
presentation action above. Escape closes the menu and returns focus to the
invoker. Outside pointer dismissal or tabbing away preserves the new focus
destination instead.

Activating an available item for a non-modal transition closes the menu. A
successful inspection transition focuses the returned active-subject
level-one heading; a successful routed transition focuses that surface's
level-one heading. An unavailable, rejected, failed, or aborted result that
retains the renderer returns focus to the stable menu-button invoker and makes
the outcome visible. When an unavailable result or another non-applied result
with `Synchronization required` installs a replacement renderer, its focus
effect resolves the corresponding coordinate or subject menu button in the new
lifetime when that target still represents the initiating action. Otherwise,
or when that control is not mounted, it focuses the new destination's level-one
heading, then the persistent `dotnet-inspect` shell control when no destination
heading is mounted. It never focuses the invoker node from the outgoing
renderer. Installation, focus, and announcement occur only while their
returned effect authority remains current.

Before an asynchronous transition or snapshot installation removes the focused
element, the UI synchronously parks focus on the persistent `dotnet-inspect`
shell control outside replaceable destination renderers. This applies to
closing a focused menu, dialog, or drawer, replacing a native Library `select`
with the custom listbox, and omitting a focused lens tablist after a
no-effective-lens result. This parking step reflects local surface cleanup, not
a product result.

Current effect authority is still required to move focus from that persistent
anchor to a result-derived destination. Installation that replaces a renderer
associates later callbacks with the newly mounted destination lifetime, never
the outgoing one. A replacement listbox receives focus only when the exact
previously focused Library identity survives; an omitted tablist moves focus to
the no-effective-lens heading. If work is superseded or its destination is
destroyed, focus remains on the mounted shell control rather than falling to
the document body.

Renderer replacement does not dismiss a surviving modal or move its contained
focus merely because its stored ordinary-dismissal target belonged to the
outgoing renderer. Before discarding that renderer, the UI atomically replaces
such a target with the newly mounted destination's level-one heading. If no
destination heading is mounted, it uses the persistent `dotnet-inspect` shell
control. A modal dismissal target never retains an element from a destroyed
renderer lifetime.

When a menu item opens a modal, the menu closes without returning focus to its
invoker and the modal applies its initial-focus rule. The stable menu-button
invoker, not the removed menu item, becomes the modal's ordinary-dismissal
return target; dismissal does not reopen the menu.

Spotlight, Open, Settings, the narrow navigation drawer, and the full-bleed
Annotated Source viewer are modal dialogs:

- each has a visible accessible name and close action;
- opening moves focus to its primary input, current selection, or heading;
- background content is inert while it is open;
- Tab and Shift+Tab remain within the dialog;
- Escape closes it unless an owner-issued destructive confirmation is active;
  the Annotated Source viewer first offers Escape to the viewer-owned transient
  layer defined by
  [Annotated Source viewer interaction](annotated-source-viewer-interaction.md);
  and
- ordinary dismissal returns focus to the invoking control.

Only one modal is open at a time. A modal action that opens another modal closes
the first without returning focus, then applies the second modal's
initial-focus rule. Dismissing the second modal returns to the originating
non-modal inspection or routed surface and does not reopen the first modal.

Opening or closing a modal does not create a browser-history entry. When a
modal action commits navigation, the modal closes without applying its
ordinary-dismissal return rule and synchronously parks focus as defined above.
An inspection destination then focuses its active-subject level-one heading;
Home, Workspace, or Diagnostics focuses the routed surface's level-one heading.
If the transition returns a typed failure, the prior surface and history remain
active, the failure is visible, and focus moves to the modal's stable invoking
control when it is still rendered, otherwise to the retained surface's
level-one heading. The failed modal does not reopen.
Browser Back or Forward while a modal is open first dismisses it without
returning focus to the invoker, then performs the history transition. History
navigation focuses the restored destination heading without reopening the
modal.

Home, Workspace, Package query, and Diagnostics are routed full-bleed surfaces
rather than dialogs. Navigation places focus on their visible level-one heading
or, for Package query, its prefix input under that heading. Browser Back returns
to the prior routed surface and restores focus through the history transition.

### Search

The persistent `Package or Package@version` input is removed. Search opens
Spotlight as the one search experience for:

- packages;
- loaded coordinates;
- Libraries;
- Types;
- Members;
- platform inputs; and
- commands.

Coordinate activation always opens the coordinate menu. That menu contains an
explicit `Search packages` action that closes the menu and opens Spotlight in
package scope. Search and coordinate selection use the same result identities
and acquisition path.

Spotlight reacts to every supported way its input value changes, including
typing, paste, drag and drop of text, autofill where applicable, and input
method composition. Pasting a package coordinate updates results immediately;
it is not dependent on keyboard events that paste does not emit.

### Open

Open admits user-provided artifacts rather than searching package sources. Its
overlay provides:

- a native file picker;
- a drag-and-drop target;
- a focused paste target for clipboard file items; and
- visible per-input progress, rejection, and failure results.

It may accept multiple related files when the artifact-acquisition owner
authorizes that input shape. The UI consumes owner-issued accepted-input
descriptors and outcomes; it does not infer supported extensions,
correspondence, or workspace composition. Arbitrary pasted text is not guessed
to be binary or base64.

### Settings

Settings opens the one shared configuration experience. Separate persistent
theme controls, a global Taste button, and duplicate settings popovers are
removed.

## Working surfaces

Source, Annotated Source, and Diagnostics are working surfaces rather than
documents inset inside a general page. This redesign does not change Metadata
viewer composition.

The package-query surface's internal query behavior remains owned by
`package-query-experience.md`; product facet identities, ordering, evidence,
failures, and completion remain owned by `package-query-cli.md`. Its former
package-tab placement stays superseded.

### Package query

Package query is the routed `/query` working surface. It has no package tab and
no active inspection coordinate. The global Search experience exposes one
visible `Package query` action that closes Spotlight, preserves the current
package-search text as the initial prefix when it is valid, and pushes the
route. A direct or refreshed `/query` visit starts with an empty prefix; query
requests are session state and are not encoded in the URL in this slice.
Seeding the prefix does not start source work; Run or facet selection dispatches
the request.

The route renders one visible level-one `Package query` heading followed by an
editable `Package ID prefix` input and `Run query` action. Entry focuses that
input. Browser Back and Forward own entry and return. Returning to the prior
surface restores focus to the stable Search control when it is still rendered;
otherwise it focuses that surface's level-one heading. The page's visible
`Back` action invokes the same history transition, falling back to Home only
when the route was loaded without an in-app predecessor. Each query history
entry carries its own predecessor identity and focus target in session-only
history state, so a later query route cannot change an older entry's Back
behavior.

The desktop layout gives the product-ordered nuspec facet rail a fixed readable
column and lets rows consume the remaining width. At a narrow viewport the
query bar remains first, facets become a wrapping horizontal control region,
and results follow in one column. The prefix input, Run, Cancel, every facet,
Back, and every `Open in workspace` action keep visible text or an explicit
accessible name at both widths. Streamed row appends preserve the current
query-page scroll position.

Selecting a facet submits its opaque product ID and starts a fresh request; it
never filters the current rows locally. The Browser adapter streams product
rows, partial failures, and completion events into the query controller.
Changing the prefix, toggling a facet, cancelling, leaving the route, or
starting another run aborts or supersedes the active source operation. Rows
already received remain visible after explicit cancellation, while events from
an older generation cannot enter a replacement outcome.

This first production surface is nuspec-only. It renders exactly the
product-issued nuspec facet catalog and does not render promoted facets,
selection checkboxes, or `Deepen`. Those controls require a separately owned
product operation and UI change.

`Open in workspace` submits the row's product-issued package ID and exact
version once through the existing typed package-opening transition. Success
leaves `/query`, pushes the returned Workspace location, and focuses the
inspection destination. Failure keeps the query route, rows, and request
intact, renders the typed failure, and returns focus to the invoking row action.
Package query does not infer a framework, source, or fallback from display
text; the existing Workspace transition owns that resolution.

### Source and Annotated Source

Source and Annotated Source use the full area to the right of Type or Member
navigation. They do not retain the old breadcrumb row, subject hero, metadata
summary, centered maximum-width column, or inset source card.

Their layout is:

```text
Types or Members | PDB Source                    open source   copy
                 | source content

Types or Members | selected subject                         Copy   Explore
                 | annotated source content
                 | product provenance
```

Source retains its compact provenance/action row. Annotated Source gives the
page-owned inspection-command row its contextual actions and keeps provenance
as a compact footer attached to the source pane. It does not add another
visible title or presentation summary inside the pane. The navigation pane and
source content may scroll independently. Collapsing navigation gives the
working surface the full viewport width.

Annotated Source appears inline by default and may open the full-bleed modal
viewer governed by the shared transient-surface contract. This document owns
that composition, including C# highlighting that preserves the product
document's exact text and coordinates, not the Annotated Source document model
or viewer internals.

Decompiler style is contextual:

- Settings owns the persistent Decompiler style preference.
- Decompiled Source, Annotated Source, and decompiled call-graph source may
  link directly to that Settings section.
- PDB Source does not show the control because authored source is unaffected.

From the full-bleed Annotated Source viewer, that action closes the viewer and
opens Settings. Closing Settings returns to inline Annotated Source without
reopening the viewer; any changed style regenerates the affected inline output.

Changing style regenerates only affected decompiler output. The preference is
not part of the visible inspection command and a shared workspace does not
impose the sender's style preference on its recipient.

### Type navigation

Type navigation remains beside Type and Member working surfaces. Package and
Library navigation may also expose Types where their owning lens requires it,
but no second Library filter is introduced.

## Unified Settings

Settings is one surface with focused sections:

- Appearance;
- Decompiler style; and
- Package sources.

Contextual entry points open the same surface at the relevant section. Settings
preserves the current workspace and returns to the same inspection view when
closed. Changes may apply live, but one Settings component renders each
owner-issued setting descriptor and dispatches its typed action. Domain owners
retain validation and state semantics.

Diagnostics is a separate full-bleed experience launched from Settings or
Spotlight. It is not another settings implementation.

## Package-source presentation

[Browser package sources](browser-package-sources.md) owns source
registration, eligibility, capabilities, credentials, source-scoped caching,
and producer provenance. This UI owner consumes those typed results and does
not redefine them.

The initial UI does not expose feed tabs or in-workspace source switching.
Settings renders the package-source owner's registration, enablement,
multi-selection, capability, authentication, and cache-action descriptors and
submits their typed actions. There is no `Default feed` control: the source
owner bootstraps Gallery only when no persisted registry exists, then the
selected source set is the complete browser policy.

Once a package is acquired, Workspace, package headings, and the data bar show
its owner-issued compact producer label as read-only context. Source-scoped
cache descriptors and actions may appear in Diagnostics.

Search results and version choices render the owner-issued compact producer
label verbatim for every represented producer. No surface shortens, parses, or
reconstructs that label from an endpoint.

## Command palette

The existing command palette is the keyboard counterpart to the visible
inspection command. It uses the same product-issued coordinates, subjects, and
lenses:

```text
package System.Text.Json
version 10.0.0
framework net10.0
library System.Text.Json.dll
type System.Text.Json.JsonSerializer
member DeserializeAsync
show source
share
```

Command execution uses the same state transitions as pointer interaction and
applies the same projectable, non-projectable, or failed canonical-state
classification after commit.

The persistent inspection command is navigation context, not an always-editable
text input. The site does not introduce a broad set of single-letter page
shortcuts. One discoverable palette shortcut plus ordinary control-specific
keyboard behavior is sufficient.

## Responsive composition

One information hierarchy adapts across viewport sizes:

- wide layouts retain Type or Member navigation beside a full working surface;
- narrow layouts replace the navigation pane with a visible
  `Types` or `Members` button that opens the shared modal navigation drawer;
- the inspection command remains one line;
- `Copy target` remains a visible trailing action;
- coordinate and leaf subject have highest truncation priority;
- intermediate qualification elides first;
- lens navigation scrolls horizontally instead of wrapping; and
- full identities remain available through accessible labels and focused or
  expanded states.

Responsive layout is not workspace state. Changing viewport size does not alter
the selected coordinate, subject, lens, filters, or canonical packet.

When narrowing replaces a navigation pane while focus is inside it, focus moves
to the new `Types` or `Members` drawer button without opening the drawer. When
widening replaces an open drawer, the drawer closes without returning focus to
its removed invoker and focus moves to the equivalent visible navigation item,
or to the active-subject heading when no equivalent item is rendered. When a
closed drawer button is replaced, the same transfer occurs only if that button
owned focus; otherwise the current focus remains unchanged. In particular,
widening does not move focus out of another open modal.

Density comes from removing duplication and conditionally presenting
navigation, not from making text or controls too small to use.

## Data bar and Diagnostics

The bottom data bar is one compact product-information line. It does not wrap,
expand, or host runtime diagnostics:

```text
dotnet-inspect v0.35.2 · abc1234 · Aug 27, 2026 UTC · Package source: Corporate mirror (pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json) · CLI tool · Agent skill
```

The data bar includes:

- dotnet-inspect version;
- linked short commit;
- concise UTC build date without a `built` prefix;
- read-only package producer, or the applicable non-package acquisition kind;
- the same `CLI tool` link used on Home; and
- the same `agent skill` link used on Home.

On a narrow viewport, the line remains non-wrapping and horizontally scrollable.
It does not discard the source or promotional actions to fit.

The data bar does not contain:

- Wasm-ready prose;
- download, startup, precompute, or total timings;
- package-cache counts;
- assembly or framework duplication;
- an API-surface label; or
- an expansion toggle.

Diagnostics opens as a full-bleed surface and may include:

- runtime and Wasm state;
- network operations and typed failures;
- exact build provenance;
- package-source health;
- candidate and payload cache contents;
- coordinate, producer, size, and persistence for each cache entry;
- cache limits and eviction state; and
- owner-authorized cache-management actions.

Diagnostics consumes owner-issued data and actions. It does not infer package
source, cache authority, or credential state.

## Reference-product boundary

[npmx.dev](https://npmx.dev/) is an interaction reference for density,
shareable state, code-first working surfaces, keyboard access, and persistent
package context. It is not the website's information architecture.

Inspect Web does not copy:

- npm-style `main`, `docs`, `code`, `diff`, `changelog`, and `stats` hierarchy;
- a package-only subject model;
- duplicated version and dependency sidebars;
- a README-centric landing page;
- social, popularity, installation, or registry-administration emphasis;
- a package file tree as the default Source navigation model;
- a broad set of single-letter shortcuts; or
- npmx branding and component styling.

Package, Library, Type, and Member ownership and their local lenses remain the
dotnet-inspect model. Npmx influences interaction quality without redefining
the product domain.

## Implementation gates

Before implementation claims this interaction contract, it must add and pass
these named Inspect Web tests:

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
  `typed outcomes commit only returned state and release authority` covers
  applied, unavailable with and without a replacement snapshot, rejected,
  failed, and aborted results under both synchronization dispositions, plus
  product-side discard of superseded work. It proves that semantic outcome
  never substitutes for the disposition: every `Synchronization required`
  result installs its complete returned snapshot, while every `Current`
  non-applied result retains the installed snapshot. It proves exact canonical
  URL and history handling, including replacement for catch-up and
  reconciliation-driven installations. It also proves that such replacement
  resolves menu-result focus in the new renderer, with destination-heading and
  persistent-shell fallbacks, and announces through the pre-existing shell
  live region before acknowledgement or abandonment.
- `navigation-consumer.test.ts`:
  `synchronization catch-up replaces history without inventing navigation`
  covers rejected, failed, aborted, and unchanged-unavailable results carrying
  `Synchronization required`, plus a dedicated synchronization result. It
  proves that each installs the complete product snapshot and replaces rather
  than pushes or adopts history. The non-applied cases retain their exact
  semantic evidence; the dedicated result introduces no semantic navigation
  outcome.
- `navigation-consumer.test.ts`:
  `abandoned synchronization debt requests fresh authority after remount`
  abandons synchronization-required authority before installation and after
  installation but before acknowledgement. It proves that neither path clears
  the session-scoped marker, that remount requests dedicated synchronization,
  and that only one product request remains pending across renderer replacement
  or destruction. A late response retains its exact request identity and old
  lifetime, is abandoned rather than consumed by the remounted destination, and
  must settle before the current lifetime issues another request. Repeated
  abandonment permits later requests without a UI retry ceiling. It also proves
  that remount and request creation are separate actions, the outstanding
  marker survives between them, and every exact issued request reaches
  settlement. A newer current semantic result may instead discharge the same
  obligation through its returned disposition. A Back or Forward traversal
  started after a request makes the request's result foreign to the selected
  entry, so it is abandoned without a history write.
- `navigation-consumer.test.ts`:
  `maintenance results honor synchronization disposition without stealing
  focus` covers authority validation, no snapshot or history write for
  `Current`, canonical URL replacement for `Synchronization required`,
  selective announcement, focused-element removal, surviving-modal
  dismissal-target replacement, and acknowledgement. The modal retains
  contained focus during renderer replacement, then ordinary dismissal reaches
  the new destination heading rather than an outgoing-renderer element.
- `navigation-consumer.test.ts`:
  `deferred effects revalidate authority when each callback executes`
  supersedes a result after installation and proves that its queued focus and
  announcement callbacks have no visible effect or history mutation.
- `navigation-consumer.test.ts`:
  `stale explicit authority cannot install returned state` returns an applied
  explicit result, begins a newer intent before installation, and proves that
  the stale result changes no rendered snapshot, canonical URL, history entry,
  focus, or shell announcement before its authority is abandoned.
- `navigation-consumer.test.ts`:
  `non-installing browser restoration realigns the selected entry` exercises
  Back and Forward with `Current` unavailable-without-replacement, rejected,
  failed, and aborted outcomes. Each case retains the snapshot, replaces the
  browser-selected entry with its canonical location, surfaces and announces
  the outcome, focuses the retained destination heading or shell fallback, and
  pushes no entry. Companion `Synchronization required` cases instead install
  the complete returned snapshot and replace the selected entry with its
  canonical location before presenting the same semantic evidence. The gate
  also supersedes each returned authority before realignment or installation
  and proves that stale work changes neither canonical URL nor history,
  produces no later focus or announcement, and is abandoned before it can be
  acknowledged.
- `navigation-consumer.test.ts`:
  `superseded restoration cannot strand the browser-selected entry` covers a
  restoration superseded before product result publication, after authority
  return but before realignment, and by a newer browser traversal. A
  non-browser successor first repairs the unresolved selected entry before
  submission; a browser successor replaces the traversal serial. In every case
  a non-installing current result leaves the address bar and selected entry
  aligned with the installed snapshot while superseded work writes nothing.
- `navigation-consumer.test.ts`:
  `acknowledgement follows every required visible effect` proves that
  location realignment, installation, focus, and announcement complete before
  acknowledgement whenever each effect is required. For `Synchronization
  required`, it additionally proves that equal local snapshot contents do not
  permit acknowledgement until the complete result has been installed under
  that exact current authority.
- `navigation-consumer.test.ts`:
  `surface destruction abandons authority and suppresses stale callbacks`
  destroys and remounts a surface before its callbacks execute, then returns a
  late result for the destroyed lifetime. A companion `Current` non-installing
  Back/Forward case destroys the destination before location realignment and
  proves that remount repairs the preserved traversal obligation before
  rendering, independently of synchronization debt.
- `navigation-consumer.test.ts`:
  `renderer replacement abandons outgoing authority before transfer` holds old
  returned authority while a current installation replaces the renderer and
  proves that no outgoing-lifetime authority survives replacement.
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
navigation-consumer boundary. It does not construct a parallel host catalog or
bypass effect-authority validation merely to observe the renderer.

These gates are not implemented by this documentation-only design. Until they
exist and pass, the prose and TLA+ model define the target contract but do not
claim Inspect Web implementation conformance.

## Acceptance scenarios

An implementation claiming this redesign is complete must satisfy these
outcomes:

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

### Transition effects and surface lifetime

1. Return an applied outcome carrying a replacement snapshot and confirm that
   installation atomically updates rendered state, canonical URL, and the
   initiating action's push-or-replace history classification.
2. Hold stale returned authority from an older intent while the current applied
   installation replaces the destination renderer. Confirm that replacement
   abandons the outgoing authority before transferring the current operation
   and its callbacks to the new lifetime.
3. Return an unavailable outcome whose refreshed or reconciled snapshot changes
   the active subject. Confirm that it installs the exact returned snapshot but
   replaces history rather than pushing the unrequested subject change. Confirm
   that focus reaches the corresponding menu button in the new renderer, or its
   destination-heading or persistent-shell fallback, and never the outgoing
   invoker node. Confirm that its deferred announcement changes the pre-existing
   shell live region after installation rather than inserting an already-filled
   destination-owned region.
4. Return `Current` unavailable-without-replacement, rejected, and failed
   outcomes. Confirm that each retains the prior snapshot, URL, and history
   while presenting its exact evidence.
5. Confirm that authority is validated before installation and independently
   inside each deferred focus and polite-live-region callback.
6. Supersede an applied result after installation but before its callbacks
   execute.
7. Confirm that focus was parked before its invoking control disappeared, that
   neither stale callback changes focus, status, active panel, URL, or history,
   and that the stale authority is abandoned.
8. Fail a prerequisite before navigation can run and return a `Current` aborted
   effect. Confirm that it retains snapshot, URL, and history, presents and
   announces its typed failure, then acknowledges its current authority.
9. Complete older work after a newer intent owns the session and confirm that
   the product discards it without publishing a consumer result, authority,
   announcement, URL change, or history change.
10. Keep a modal open while maintenance replaces the destination renderer.
   Confirm that focus remains contained in the modal, its outgoing-renderer
   dismissal target is replaced with the new destination heading, and ordinary
   dismissal never focuses a detached element.
11. Return another result and confirm that acknowledgement occurs only after its
   required installation, focus, and announcement effects complete.
12. Install a maintenance snapshot and confirm that it replaces URL history,
   does not move surviving focus, announces only a visible change, and
   acknowledges its authority.
13. Destroy a surface while it holds unconsumed authority, then remount the
    same surface kind and return another result for the destroyed lifetime.
14. Confirm that destruction and the late return both abandon authority and
    that callbacks from the prior lifetime cannot affect the remounted surface.
    Repeat after a `Current` non-installing Back or Forward result returns but
    before its location realignment. Confirm that remount preserves and repairs
    the unresolved traversal before rendering even though no synchronization
    debt exists.
15. Return an applied explicit result, begin a newer intent before installation,
    and confirm that the stale result changes no rendered snapshot, canonical
    URL, history entry, focus, or shell announcement before abandonment.
16. Leave the consumer behind the retained product session, then return
    rejected, failed, aborted, and unchanged-unavailable results with
    `Synchronization required`. Confirm that each installs the complete returned
    snapshot, replaces history without recording the unsuccessful request,
    presents its exact semantic evidence after installation, and acknowledges
    only after every required effect.
17. Install a synchronization-required result and abandon it before
    acknowledgement. Remount the destination and confirm that the
    session-scoped consumer retains any already-pending request's identity and
    old lifetime rather than issuing a second request. Return that old response
    and confirm that it is abandoned and settled before the current lifetime
    separately issues fresh synchronization, installs the complete current
    snapshot under fresh authority, replaces history, and acknowledges. Confirm
    that every exact issued request reaches settlement. Repeat abandonment and
    confirm that another request remains possible. Start Back or Forward after
    a request but before its result and confirm that the foreign result is
    abandoned without changing the newly selected entry.
18. While synchronization remains outstanding, return a newer current
    maintenance or explicit result. Confirm that its disposition controls
    installation and that acknowledgement of that current authority discharges
    the same obligation without waiting for the dedicated response.

### Workspace composition

1. Supply two open-coordinate descriptors with different optional subject
   context and status.
2. Confirm that Workspace renders those descriptors without deriving identity
   from their labels.
3. Activate and close entries and confirm that each action submits the opaque
   coordinate identity once and renders the returned workspace outcome.
4. Confirm that an action opening inspection focuses the resulting
   active-subject heading and that an action remaining in Workspace focuses the
   returned active entry, nearest surviving entry, or Workspace heading
   according to the defined order.
5. Supply a typed failure and confirm that focus remains on the invoking entry.

### Canonical adapter

1. Supply a projectable outcome containing an opaque packet and package courtesy
   identity and confirm that the UI composes both query fields with the
   transition's push, replace, or adopt history classification.
2. Supply a projectable non-package outcome and confirm that the UI composes
   only `w` without placing a local coordinate in readable URL state.
3. Starting from a projectable location, supply a non-projectable outcome and
   confirm that explicit Share presents the owner-issued reason and that the
   location removes the stale `w` and package courtesy fields.
4. Supply a failed outcome and confirm that the prior workspace and URL remain.
5. Confirm that route preflight, refresh, and shared-link activation never parse
   compact packet fields or use the readable package courtesy identity as a
   fallback workspace.

### Browser history

1. Activate a coordinate, navigate to a Type, and select Source.
2. Change several result filters.
3. Use Browser Back and confirm that it returns to the prior pushed subject or
   lens state rather than stepping through each filter change. Confirm that the
   restored snapshot adopts the browser-selected entry without a history write.
4. Use Browser Forward and confirm that it restores the Source state with its
   latest replaced refinements without adding or replacing an entry.
5. Refresh that location and open it as a shared link. Confirm that exact
   restoration adopts the current entry, while a changed unavailable or
   reconciled restoration replaces that entry with its returned canonical
   state.
6. Use Back and Forward with `Current` unavailable-without-replacement,
   rejected, failed, and aborted restoration outcomes. Confirm that each retains
   the rendered snapshot, replaces the browser-selected entry with the retained
   canonical location, surfaces and announces the outcome, focuses the retained
   destination heading or shell fallback, and lets a later traversal continue
   past the failed location without a pushed entry. Repeat with
   `Synchronization required` and confirm that the complete returned snapshot
   is installed and its canonical location replaces the selected entry before
   the same semantic evidence is presented.
7. Supersede a Back restoration before product result publication, then repeat
   after authority returns but before realignment. Follow each with a
   non-browser action whose current result does not install. Confirm that the
   selected entry was repaired before successor submission, the retained
   snapshot and address remain aligned, and superseded work writes nothing.
8. Supersede a Back restoration with Forward, then return a non-installing
   outcome for the current Forward restoration. Confirm that only the newest
   traversal serial may realign the selected entry.

### Package-source composition

1. Supply registration, enablement, multi-selection, capability,
   authentication, and cache-action descriptors and confirm that the one
   Settings surface renders them and dispatches their typed actions.
2. Give two custom sources the same display name and confirm that search results
   and version choices render their distinct owner-issued compact labels
   verbatim.
3. Supply an acquired producer label and confirm that Workspace, package
   headings, and the data bar render it as read-only context.
4. Confirm that no feed tabs or in-workspace feed-switching control appears.
5. Confirm that `Default feed` is absent and that source multi-selection is the
   complete browser policy after first-run Gallery bootstrap.

`InspectWebPackageSourceSettingsTests.RendersEnablementAndSelectionWithoutDefaultFeed`
gates enabled, disabled, selected, and unselected source descriptors together
with the absence of a synthesized `Default feed` control.

### Search input

1. Activate the coordinate and confirm that its menu opens rather than
   Spotlight.
2. Activate an available non-search action and confirm that the menu closes and
   the returned active-subject heading receives focus.
3. Supply a typed failure for a coordinate action and confirm that the current
   surface remains visible, the failure is surfaced, and focus returns to the
   coordinate button.
4. Reopen the menu, activate `Search packages`, and confirm that the menu closes
   and
   package-scoped Spotlight receives initial focus.
5. Paste a complete package coordinate without pressing another key.
6. Confirm that results update immediately.
7. Dismiss Spotlight without navigating and confirm that focus returns to the
   coordinate button without reopening its menu.
8. Reopen package-scoped Spotlight, select the result, and confirm that the same
   acquisition transition is used as pointer-driven package selection.

### Local Open

1. Open the local-artifact overlay.
2. Add supported files through the picker, drag and drop, and clipboard file
   paste.
3. Confirm that each path produces the same owner-issued workspace result.
4. Paste arbitrary text and confirm that the UI does not guess that it is
   binary content.

### Source working surface

1. Open Type Source with Type navigation visible.
2. Confirm that the source pane uses all remaining width and begins with only
   compact provenance and actions.
3. Collapse Type navigation and confirm that source content expands to the full
   viewport width.
4. Open PDB Source and confirm that no Decompiler style control appears.
5. Open Decompiled Source and confirm that its style action opens the shared
   Settings section.

### Narrow viewport

1. Start from a committed Type Source state.
2. Narrow the viewport until Type navigation moves to its drawer.
3. Activate the visible `Types` button and confirm the drawer's accessible
   dialog name, initial focus, focus containment, Escape dismissal, and focus
   return.
4. Confirm that the inspection command and lens strip remain single-line
   scrolling or truncating surfaces rather than wrapping.
5. With focus in the wide navigation pane, narrow the viewport and confirm that
   focus moves to the new drawer button without opening it.
6. Open the drawer, restore the wide viewport, and confirm that the drawer
   closes and focus moves to the equivalent visible navigation item or the
   active-subject heading.
7. Open Settings at the narrow viewport, restore the wide viewport, and confirm
   that focus remains contained in Settings.
8. Confirm that coordinate, subject, lens,
   filters, and canonical state did not change.

### Modal and routed surfaces

1. Open and close Spotlight, Open, and Settings by pointer, keyboard, and
   Escape.
2. Confirm accessible naming, initial focus, modal containment, inert
   background content, and focus return for each.
3. Launch Diagnostics from Settings and Spotlight and confirm that focus moves
   to the routed Diagnostics heading rather than back to the modal invoker.
4. Commit navigation from Search, Open, and the narrow drawer and confirm that
   focus moves to the resulting active-subject heading.
5. Return typed failures from Search, Open, and the narrow drawer and confirm
   that the prior surface and history remain active, the failure is visible,
   and focus moves to the surviving modal invoker or retained surface heading.
6. Keep a modal open across product-maintenance renderer replacement and confirm
   that focus remains inside it until dismissal, then moves to the replacement
   destination heading rather than the removed invoking control.
7. Open and close the full-bleed Annotated Source viewer and confirm the shared
   modal focus, Escape, containment, and history behavior.
8. From that viewer, open Decompiler style Settings and confirm that the viewer
   closes, Settings receives focus, and closing Settings returns to inline
   Annotated Source without reopening the viewer.
9. Navigate to Home, Workspace, Package query, and Diagnostics and confirm that
   each is a routed surface with one visible level-one heading, no
   coordinate/subject command, and a persistent `dotnet-inspect` control that
   opens Workspace.
10. Use Browser Back and Forward while a modal is open and confirm that the
   modal is dismissed, the restored destination heading receives focus, and the
   modal does not reopen.

### Package query route

1. Open Package query from Search and confirm that Spotlight closes, `/query`
   is pushed, the package-search text becomes the initial valid prefix, and the
   `Package ID prefix` input receives focus.
2. Refresh or directly load `/query` and confirm that the route starts without
   a persisted request or inferred package coordinate.
3. Toggle two product-issued nuspec facets and confirm that each change starts
   a fresh engine request with opaque IDs, cancels the prior request, and
   suppresses its late rows and failures.
4. Confirm that product rows, evidence, partial failures, and exhausted,
   bounded, failed, cancelled, and zero-row completion states remain distinct.
5. Cancel after rows arrive and confirm that the rows remain visible, the state
   reads as cancelled, and the Browser source operation stops.
6. Use Browser Back and Forward and the visible Back action, confirming route
   restoration and focus return to the stable Search control or destination
   heading.
7. Open a row in Workspace and confirm one typed package transition using its
   exact product-issued ID and version. Confirm success focuses the inspection
   destination and a typed failure retains `/query`, the result set, and the
   invoking row action.
8. At desktop and narrow widths, confirm that the prefix, Run, facets, results,
   evidence, completion, Cancel, Back, and `Open in workspace` remain visible
   and keyboard reachable, with no promoted facet, selection, or `Deepen`
   control.

### Data and diagnostics

1. Confirm that version, commit, UTC date, the complete owner-issued compact
   producer label, CLI tool, and agent skill occupy one non-expanding data-bar
   line.
2. Confirm that timings, cache counts, runtime readiness, assembly identity,
   and framework do not appear in that line.
3. Open Diagnostics and confirm that detailed runtime, source, and cache
   evidence and owner-authorized cache actions appear in the full-bleed
   surface.
