# Inspect Web UI

This document owns the presentation and interaction language of the
`dotnet-inspect` website. It records reusable visual rules so equivalent
controls communicate state consistently across package, type, member, and
metadata surfaces.

The rules are normative targets for `prototypes/inspect-web`. When the current
implementation differs, this document describes the intended behavior rather
than preserving the inconsistency.

## Ownership and boundaries

The Inspect Web UI owner defines:

- the visual meaning of shared control states;
- the interaction grammar for recurring website controls; and
- the composition rules that make equivalent controls look and behave alike.

It consumes product-owned identities, labels, ordering, defaults, availability,
and query semantics. In particular,
[Product Vocabulary](vocabulary.md) remains authoritative for vocabulary values
and selection semantics. Individual inspect-web components retain their
rendering, binding, and state-transition responsibilities.

This document does not own:

- inspection or acquisition behavior;
- API, metadata, package, type, or member classification;
- vocabulary identities, labels, ordering, or defaults;
- CLI and library output formatting; or
- the internal implementation boundaries among inspect-web modules.

## Current redesign

This is a coordinated information-architecture and density rework, not a set of
independent cosmetic changes.

| Area | Direction |
| ---- | --------- |
| Primary navigation | Use Package, Library, Type, and Member views |
| Package coordinate | Remove the persistent row and edit version and TFM in Package |
| Library inspection | Select all libraries or one library within Library |
| Type headings | Keep API and Source name-only; retain detail in Metadata |
| Filters | Collapse selector rows by default and summarize hidden restrictions |
| Selected controls | Use one accent selected-state treatment across selector families |
| Source provenance | Use a compact status/action row without validation prose or link glyphs |

Together, these decisions move subject-specific controls and detail into the
view that owns them. Persistent chrome carries only workspace identity and
navigation. Content views spend their vertical space on the package, library,
type, member, API, metadata, or source material the user selected.

## Selector controls

Selector controls are compact pill-shaped buttons used to choose a value or
filter a result set. Type kind, member kind, accessibility, and member trait
controls use the same state language even when their values and selection
semantics differ. This section applies to selector pills, not to primary-view
or lens navigation.

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

The Type view does not retain a second library filter. The active Library
subject controls whether Package and Type navigation show types from all
libraries or one library. Library selection is view context, not a hidden Type
filter dimension.

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

## Type page headings

Type pages use lens-specific information hierarchy. A shared type selection
does not require every lens to repeat the same heading detail.

### API and Source lenses

The API and Source lenses retain only the selected type's name above their
primary content. The name is the page heading and uses the product-owned type
display label.

These compact headings do not repeat:

- the kind icon;
- the namespace eyebrow;
- the declaration signature;
- the member count;
- the accessibility summary;
- the target framework;
- the library; or
- the package and version.

API content begins immediately after the type name. Source places only its
compact provenance and action row between the type name and source content.
The removed fields do not leave placeholders or reserved vertical space. They
are also not moved into collapsed duplicate headers on either lens.

This makes the API surface or source document the primary content of its page
and increases the amount visible without scrolling.

### Metadata lens

The Metadata lens retains the detailed type heading. It is the type-level view
for kind, namespace, declaration shape, target framework, library, package, and
version context.

The type name remains the common orientation point between the API and Metadata
lenses and the Source lens. Switching lenses changes the amount of surrounding
detail, not the selected type or its display identity.

## Source provenance

Successful source provenance is presented as a compact status and action row,
not as an explanation of the product's safety mechanisms. This rule applies to
type, member, and graph source surfaces.

In the Type view, the Source lens is ordered as:

1. Type name.
2. Compact source provenance and actions.
3. Source content.

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

## Primary views

A package and a library are different inspection subjects:

- **Package** means the selected NuGet package or platform package coordinate.
- **Library** means one assembly contained in that active package coordinate.
- **Type** means one selected type from the active package coordinate.
- **Member** means one selected member of the active type.

The primary view control presents four choices:

| View | Subject |
| ---- | ------- |
| Package | Package-wide identity and relationships |
| Library | All libraries in the package or one selected library |
| Type | One selected type |
| Member | One selected member |

View labels name the kind of subject, not its cardinality. The label is
`Library` even when its selected subject is `All libraries`.

### Navigation semantics

The primary view control is a tablist. Package, Library, Type, and Member are
tabs with `role="tab"` and `aria-selected`. An unavailable tab remains focusable
in the tablist with `aria-disabled="true"` so its existence is discoverable;
activation is suppressed. View tabs do not use `aria-pressed`.

Each view's lens strip is a separate tablist. Every lens or member section is a
tab with `role="tab"` and `aria-selected`, including identically labelled tabs
owned by different views. The active lens must therefore be available
programmatically rather than conveyed by color alone.

Each tablist has an accessible name: `Primary view` for the view control and
`<View> lenses` for its lens strip. A tab references its panel with
`aria-controls`; the panel uses `role="tabpanel"` and `aria-labelledby`.

Tablists use one tab stop and manual activation:

- `Tab` enters on the tab with `tabindex="0"`, initially the active tab, and
  leaves the tablist from the focused tab.
- Left and Right Arrow move focus through the horizontal tabs.
- Home and End move focus to the first and last tab.
- Arrow navigation includes `aria-disabled` tabs so they remain discoverable.
- Enter or Space activates a focused available tab.
- Activating an `aria-disabled` tab has no effect.

Roving `tabindex` keeps only the focused tab at `tabindex="0"`. Moving focus
does not change `aria-selected` or start lens work until activation.

### View availability and reconciliation

The four view controls remain visible so the information architecture does not
shift as subjects are selected:

- Package is available whenever a package workspace is active.
- Library is available when the product supplies a validated, non-empty Library
  subject descriptor set for the active coordinate.
- Type is available when the workspace has a current type selection.
- Member is available when the current type has a current member selection.

An unavailable view is `aria-disabled`, not natively disabled. It is not hidden,
does not activate, and does not retain stale content. Native disabled semantics
remain appropriate for unavailable selector pills outside the tablist.

Reconciliation first computes subjects, then decides whether navigation must
move:

1. The product supplies the validated Library subject descriptor set for the
   new coordinate. A non-empty set has exactly one owner-issued default.
2. If the previous Library subject is absent, the UI selects that default. The
   default may be `All libraries` or one library. An empty or malformed set
   makes Library unavailable; the UI surfaces the producer failure and does not
   guess a subject.
3. The host asks the owning product model to resolve the existing Type within
   both the active coordinate and active Library subject.
4. It resolves Member only when the retained Type still owns that Member.
5. A missing Type or Member selection is cleared. Reconciliation does not
   silently substitute another Type or Member.

Subject invalidation in a non-active view only disables that view's tab. It
does not move the user away from Package or Library.

Initial workspace activation may accept an owner-issued default Type. That
initial choice is distinct from replacing a user's invalidated selection after
a coordinate or Library-subject change.

Navigation changes only when the active view becomes unavailable:

- Member moves to Type when Type remains available, otherwise to Library when
  Library remains available, otherwise to Package.
- Type moves to Library when Library remains available, otherwise to Package.
- Library moves to Package when no valid Library subject descriptor set is
  available.
- Package remains active through coordinate reconciliation.

Resolving a missing Library subject to the owner-issued default happens before
Type and Member reconciliation. It keeps an active Library view available but
does not itself redirect navigation.

### Lens ownership

Lenses are grouped by the subject they inspect:

| View | Lenses |
| ---- | ------ |
| Package | Overview, Dependencies |
| Library | References, Integrations, Opportunities, Analysis, Metadata |
| Type | API, Metadata, Source |
| Member | Overview, Call graph, Facts, Source, Annotated source |

Package Dependencies contains declared package dependencies by target
framework. Direct assembly references belong to Library References.
Integrations, Opportunities, Analysis, and Library Metadata also describe
assembly content.

A lens appears only in its owning view. The UI does not retain one mixed lens
strip under Package or repeat library lenses in both Package and Library.
Lens identity is scoped by its owning view, so Library Metadata and Type
Metadata are distinct lenses that may share a display label.

### Library selection

The Library view lists every library admitted from the active package
coordinate and an `All libraries` subject when the product admits aggregate
inspection for that coordinate.

The product supplies ordered Library subject descriptors and an owner-issued
default. A coordinate whose initial Library lens supports aggregate inspection
may default to `All libraries`; when the initial Library lens requires one
library, the product defaults to an owner-issued library. The UI does not infer
that choice from package kind, endpoint shape, or assembly count.

The default must be a valid subject for the coordinate. Lens capability does
not silently replace it: if the active lens cannot inspect the default subject
arity, the subject remains selected and the lens reports its unavailable
result.

The Library subject control is single-select. A compact population may use a
native `select`; a visible library list uses `role="listbox"` with
`role="option"` and `aria-selected`. It is not a selector-pill group with
`aria-pressed` and is not a lens tablist.

The custom listbox has the accessible name `Libraries` and one tab stop. Focus
remains on the listbox while `aria-activedescendant` identifies the active
option. Up and Down Arrow move the active option and selection, Home and End
move to the first and last option, and printable input performs prefix
typeahead. Native `select` uses the platform's equivalent behavior.

The selected subject controls every Library lens:

- `All libraries` requests a package-wide result over the complete library set.
- An individual library requests the same lens for only that assembly.
- The selected subject persists when switching among References, Integrations,
  Opportunities, Analysis, and Metadata.
- Changing package version or TFM retains the individual selection only when
  the same library identity is present in the new coordinate; otherwise it uses
  the new coordinate's owner-issued default Library subject.

The active library subject remains visible while the library list is filtered
or collapsed. A lens heading distinguishes aggregate results from a
single-library result.

Package and Type navigation honor the same active Library subject. With
`All libraries`, their type lists include every admitted library; with one
library selected, they include only that library's types. The type-navigation
heading shows `All libraries` or the selected library as context and links back
to Library for changes. It is not a second library selector.

The active Library subject also constrains the eligible Type and Member
subjects. A Type from another library is not retained merely because it still
exists elsewhere in the package coordinate.

When the product surface identifies colliding types under `All libraries`, type
navigation qualifies only those rows with their product-owned defining library.
If a colliding Type is selected, compact workspace context also shows its
defining library. API and Source retain their name-only content heading;
disambiguation does not restore the removed metadata block.

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

The persistent `PACKAGE` row is removed from the workspace chrome. Package
identity, version selection, TFM selection, and resolved asset detail do not
occupy vertical space above every package, type, member, and lens view.

The two package surfaces have distinct responsibilities:

| Surface | Responsibility |
| ------- | -------------- |
| Package tab | Select an open package workspace and summarize its active coordinate |
| Package view | Present package details and edit the active version and TFM |

The active package tab retains a compact version and TFM summary while a
workspace is active so the coordinate remains visible in Library, Type, and
Member views. It does not contain the full selectors.

### Package view

The Package view is the editing surface for the package coordinate. It contains:

- the package identity;
- a version selector;
- a TFM selector.

These controls integrate with the package content rather than recreating the
removed full-width row. Existing package heading fields must not duplicate the
same version and TFM values beside the selectors.

Resolved assembly assets are library details and appear in the Library view,
not in the Package view.

Changing version or TFM updates the shared package workspace. The resulting
coordinate applies when the user moves among Package, Library, Type, and Member
views.

### Type navigation remains available

The type navigation list remains available in both the Package and Type views.
Moving the coordinate selectors into Package does not make the user leave the
package experience to browse its types.

The Type view does not repeat version and TFM selectors. The active package tab
provides compact context, and the Package view is the single place to change
the coordinate.

This placement rule does not prescribe which TypeScript module renders or binds
the controls. Component ownership may remain separate from where the controls
are composed on screen.
