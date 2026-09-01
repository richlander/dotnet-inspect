# Inspect Web Presentation Language

This document owns the reusable visual and accessibility language of the
`dotnet-inspect` website: the shared rules that let equivalent controls
communicate state consistently across workspace, package, library, type,
member, metadata, source, and settings surfaces. It is a component-level
contract; it does not decide which subject, coordinate, or lens is active,
and it does not own the consumer effect lifecycle that installs navigation
results.

The rules are normative targets for `prototypes/inspect-web`. When the
current implementation differs, this document describes the intended
behavior rather than preserving the inconsistency.

## Ownership and boundaries

This owner defines:

- the visual meaning of shared selector-control states (selected, rest,
  hover, keyboard focus, disabled) and their accessibility contract;
- the interaction grammar and collapsed-summary rules for progressive filter
  disclosure;
- the shared heading rules across the API, Source, and Metadata lenses: API
  and Source render a compact exact-target heading, while Metadata retains its
  detailed type-level context;
  and
- the compact status/action presentation for successful and failed source
  provenance.

It does not own:

- which coordinate, subject, or lens is active, or how navigation
  descriptors are rendered and activated (owned by
  [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md));
- the consumer effect lifecycle, focus movement following a typed navigation
  outcome, browser history, or canonical-location handling (owned by
  [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md));
- shell actions, modal or routed classification, or Spotlight/Open/Settings
  entry (owned by
  [Inspect Web Shell Interaction](inspect-web-shell-interaction.md));
- page-level placement, layout, or responsive composition of working surfaces
  (owned by
  [Inspect Web Surface Composition](inspect-web-surface-composition.md));
- PDB checksum verification, SourceLink transport, or browse-URL
  authorization semantics, which remain product facts this language only
  presents compactly; and
- vocabulary values, selection semantics, or any other product-owned
  identity, label, or evidence.

## Inputs or consumed contracts

This document consumes, without redefining:

- [Product Vocabulary](vocabulary.md) for vocabulary values and selection
  semantics;
- the product-issued active-subject label, effective-lens label, and
  no-effective-lens outcome rendered by
  [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md);
  and
- product-issued source-provenance results, including optional
  producer-authorized browse URLs.

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

## Shared heading rules

The title line identifies the active subject, coordinate options, inspector,
and broad workspace context. The inspected-subject line immediately below it
identifies the exact Package, Library, Type, or Member in full. Content headings
may use the shorter local name. This section defines how lenses retain that
orientation without restoring duplicated hero metadata.

### API and Source lenses

API and Source render a compact local heading followed by their primary
content. The inspected-subject line remains the visible owner of the complete
qualified identity. When the snapshot has an effective lens, the lens panel's
accessible heading relationship includes the inspected-subject identity and
the active inspector label.

When the snapshot has no effective lens, the UI renders no `tabpanel`. A status
region references the target heading and its visible `Lens unavailable`
or `Lens failed` heading. It explains the returned outcome without fabricating
an active tab, panel, or fallback lens.

Home, Workspace, and Diagnostics render their own visible level-one heading.
The persistent `dotnet-inspect` root control remains available and opens
Workspace. Returning to an inspection surface restores its exact-target
heading; two visible level-one headings are never rendered for one routed
surface.

The compact API and Source heading does not repeat:

- the kind icon;
- the namespace eyebrow;
- the declaration signature;
- the member count;
- the accessibility summary;
- the target framework;
- the library; or
- the package and version.

The removed fields do not leave placeholders or reserved vertical space, and
they are not moved into collapsed duplicate headers on either lens. Page order,
including Source provenance placement and Annotated Source **Copy** and
**Explore** action placement, is owned by
[Inspect Web Surface Composition](inspect-web-surface-composition.md#source-and-annotated-source).

This makes the API surface or source document the primary content of its page
and increases the amount visible without scrolling.

### Metadata lens

The Metadata lens retains the detailed type heading. It is the type-level view
for kind, namespace, declaration shape, target framework, library, package, and
version context.

The exact-target heading remains the common orientation point between API,
Metadata, and Source. Switching lenses changes the amount of surrounding
detail, not the selected subject or its display identity.

## Source provenance

Successful source provenance is presented as a compact status and action row,
not as an explanation of the product's safety mechanisms. This rule applies to
type, member, and graph source surfaces.

The placement of that row relative to Source or Annotated Source content is
owned by
[Inspect Web Surface Composition](inspect-web-surface-composition.md#source-and-annotated-source).

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

## Non-claims

This document does not decide which lens, subject, or coordinate is active,
does not define browser-history or focus-authority behavior following a
navigation result, and does not define page-level layout or placement. It
states only the shared visual and accessibility language that those owners
apply.
