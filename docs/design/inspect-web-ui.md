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
semantics differ.

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
- Every toggle-style selector exposes `aria-pressed="true"` or
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
`Show filters; accessibility: public`. A member pane with `all kinds`,
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
status is simply:

```text
PDB Source                                      open source   copy
```

`PDB Source` implies that the source satisfied the product's PDB checksum
verification contract. The page does not repeat `PDB-checksum-verified`, the
SourceLink transport, repository URL, or commit hash in explanatory prose.
Those facts remain part of the product result and link target; they do not need
to occupy the source viewport.

The `open source` action uses plain text with no trailing arrow or external-link
glyph. Its accessible name may state that it opens a new browser tab. The
`copy` action remains adjacent to it.

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

### View availability and reconciliation

The four view controls remain visible so the information architecture does not
shift as subjects are selected:

- Package is available whenever a package workspace is active.
- Library is available when the active coordinate contains at least one
  admitted library.
- Type is available when the workspace has a current type selection.
- Member is available when the current type has a current member selection.

An unavailable view is disabled with native disabled semantics. It is not
hidden and does not retain stale content.

After a version, TFM, or library-subject change, the host asks the owning
product model to resolve the existing type and member identities. It retains
each selection only when that owner confirms it in the new coordinate. An
owner-issued default may replace a missing selection; the UI does not select an
arbitrary first row.

If the active Member selection is lost while its Type remains, the UI moves to
Type. If the Type selection is also lost, or the active Library view has no
available libraries, the UI moves to Package. Losing an individual library
selection while other libraries remain returns the Library subject to
`All libraries` and keeps the Library view active.

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
coordinate. The default and first selection is `All libraries`, followed by the
individual libraries in product-owned order.

The selection controls the subject of every Library lens:

- `All libraries` requests a package-wide result over the complete library set.
- An individual library requests the same lens for only that assembly.
- The selected subject persists when switching among References, Integrations,
  Opportunities, Analysis, and Metadata.
- Changing package version or TFM retains the individual selection only when
  the same library identity is present in the new coordinate; otherwise it
  returns to `All libraries`.

The active library subject remains visible while the library list is filtered
or collapsed. A lens heading distinguishes aggregate results from a
single-library result.

Package and Type navigation honor the same active Library subject. With
`All libraries`, their type lists include every admitted library; with one
library selected, they include only that library's types. The type-navigation
heading shows `All libraries` or the selected library as context and links back
to Library for changes. It is not a second library selector.

### Aggregate results

`All libraries` is a real aggregate inspection mode, not a client-side
concatenation of independently rendered library pages. The UI consumes an
owner-provided aggregate result that defines ordering, identity, deduplication,
and partial-failure behavior.

If a lens cannot provide an all-library result, it reports that mode as
unavailable. It must not silently select the first library or present
single-library data under an `All libraries` heading.

## Package coordinate controls

The persistent `PACKAGE` row is removed from the workspace chrome. Package
identity, version selection, TFM selection, and resolved asset detail do not
occupy vertical space above every package, type, member, and lens view.

The two package surfaces have distinct responsibilities:

| Surface | Responsibility |
| ------- | -------------- |
| Package tab | Select an open package workspace and summarize its active coordinate |
| Package view | Present package details and edit the active version and TFM |

The active package tab may retain a compact version and TFM summary so the
workspace coordinate remains visible. It does not contain the full selectors.

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
