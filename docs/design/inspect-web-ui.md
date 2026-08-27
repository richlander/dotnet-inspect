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
- canonical packet encoding or decoding;
- CLI and library output formatting; or
- the internal implementation boundaries among inspect-web modules.

## Current redesign

This is a coordinated information-architecture and density rework, not a set of
independent cosmetic changes.

| Area | Direction |
| ---- | --------- |
| Subject navigation | Use one single-line Workspace, coordinate, and current-subject command |
| Workspace selection | Replace package tabs with a Workspace surface |
| Package coordinate | Keep a compact package, version, and TFM argument beside `dotnet-inspect` |
| Library inspection | Select all libraries or one library within Library |
| Default view | Open ordinary inspectable artifacts on Type API |
| Type headings | Let the inspection command identify API and Source; retain detail in Metadata |
| Filters | Collapse selector rows by default and summarize hidden restrictions |
| Selected controls | Use one accent selected-state treatment across selector families |
| Source provenance | Use a compact status/action row without validation prose or link glyphs |
| Search and opening | Use Spotlight for search and a separate local-artifact Open flow |
| Settings | Use one Settings experience with contextual entry points |
| Data bar | Show build identity, acquired source, CLI, and skill links on one line |

Together, these decisions move subject-specific controls and detail into the
view that owns them. Persistent chrome carries one compact inspection command,
shell actions, lens navigation, and one data line. Content views spend their
vertical space on the package, library, type, member, API, metadata, or source
material the user selected.

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

The API and Source lenses begin with their primary content. Their accessible
heading relationship includes both the product-owned active-subject label from
the inspection command and the active lens label. The active-subject token is
the visible level-one heading: the coordinate token for a Package or other root
subject, and the current-subject token for Library, Type, or Member. The lens
panel's `aria-labelledby` references that label and the active lens tab.

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
compact provenance and action row before source content. The removed fields do
not leave placeholders or reserved vertical space. They are also not moved
into collapsed duplicate headers on either lens.

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
- **Package** means one selected NuGet or platform package coordinate.
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
dotnet-inspect  System.Text.Json@10.0.0/net10.0  System.Text.Json.JsonSerializer.DeserializeAsync
dotnet-inspect  MyAssembly.dll                   MyNamespace.MyType
```

The command starts without a separator glyph. Spacing and typography distinguish
its three roles:

1. `dotnet-inspect` is the Workspace root.
2. The coordinate identifies the active package, platform, project, file, or
   other product-owned workspace input.
3. The current subject identifies the active Library, Type, or Member.

The displayed subject need not mechanically repeat every parent. A Type or
Member normally uses its product-owned qualified display identity. A defining
Library appears when it is the active subject or when the product reports that
qualification is required to disambiguate identity.

The command remains one line. When space is constrained, intermediate
qualification elides before the coordinate or leaf subject. The complete
product-owned identity remains in the accessible name and focused or expanded
presentation.

Each visible role is a real interactive control rather than click behavior
attached to inert text:

- activating `dotnet-inspect` opens Workspace;
- activating the coordinate opens its applicable package, version, TFM, or
  acquisition-detail controls;
- activating an ancestor portion of the current subject moves to that subject;
  and
- `Copy target` copies the product-issued canonical current target.

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
- the retained current Library, Type, or Member path;
- loading, ready, or failed state;
- an activation action; and
- an explicit Close action.

Activating an entry restores its retained subject and lens. Closing one entry
requests the product-owned workspace transition. The returned state identifies
the next active entry or an empty Home state; the UI does not choose a successor
by package label or visual order. Separate coordinates remain separate even
when their display package IDs match.

Workspace also exposes the same Search and Open actions as the shell. It does
not infer source identity, package equivalence, or local-file correspondence
from display labels.

### Lens navigation semantics

Each subject's lens strip remains a tablist. Every lens or member section is a
tab with `role="tab"` and `aria-selected`, including identically labelled tabs
owned by different subjects. The active lens must therefore be available
programmatically rather than conveyed by color alone.

Each tablist has the accessible name `<Subject> lenses`. A tab references its
panel with `aria-controls`; the panel uses `role="tabpanel"` and
`aria-labelledby`.

Lens tablists use one tab stop and manual activation:

- `Tab` enters on the tab with `tabindex="0"`, initially the active tab, and
  leaves the tablist from the focused tab.
- Left and Right Arrow move focus through the horizontal tabs.
- Home and End move focus to the first and last tab.
- Enter or Space activates a focused available tab.
- Activating an `aria-disabled` tab has no effect.

Roving `tabindex` keeps only the focused tab at `tabindex="0"`. Moving focus
does not change `aria-selected` or start lens work until activation.

### Subject availability and reconciliation

Subject availability remains explicit even though unavailable levels no longer
occupy permanent primary tabs:

- A root subject is available whenever a coordinate workspace is active.
- Package is that root subject only for a package-backed coordinate.
- Library is available when the product supplies a validated, non-empty Library
  subject descriptor set for the active coordinate.
- Type is available when Library is available and the workspace has a current
  Type selection.
- Member is available when Type is available and the current Type has a current
  Member selection.

Reconciliation first computes subjects, then decides whether navigation must
move:

1. The product supplies the validated Library subject descriptor set for the
   new coordinate.
2. A successful empty set is valid. The UI clears the Library, Type, and Member
   subjects and leaves the coordinate on its root subject without presenting a
   producer failure.
3. A malformed set or producer failure also clears the dependent subjects, but
   surfaces the typed failure and stops subject reconciliation. The UI does not
   guess a subject.
4. A validated non-empty set has exactly one owner-issued default. If the
   previous Library subject is absent, the UI selects that default. The default
   may be `All libraries` or one library.
5. The host asks the owning product model to resolve the existing Type within
   both the active coordinate and active Library subject.
6. It resolves Member only when the retained Type still owns that Member.
7. A missing Type or Member selection is cleared. Reconciliation does not
   silently substitute another Type or Member.

Navigation changes only when the active subject becomes unavailable:

- Member moves to Type when Type remains available, otherwise to Library when
  Library remains available, otherwise to the coordinate root.
- Type moves to Library when Library remains available, otherwise to the
  coordinate root.
- Library moves to the coordinate root when no valid Library subject descriptor
  is available.
- The coordinate root remains active through coordinate reconciliation.

Resolving a missing Library subject to the owner-issued default happens before
Type and Member reconciliation. It keeps an active Library subject available
but does not itself redirect navigation.

### Initial subject and lens

A newly acquired coordinate starts at the deepest preferred subject the product
can validly supply:

1. Type API when a valid default Library subject and Type exist.
2. The owner-issued initial Library lens when a Library exists but no Type does.
3. The coordinate's root overview when no inspectable Library or Type exists.

This default applies only to initial workspace creation. Browser refresh,
history navigation, and shared-link restoration use the canonical packet
instead of reapplying the default.

A tools v2 pointer package is the required Package-fallback case. Acquisition
succeeds, Package identity and metadata remain available, Library, Type, and
Member are unavailable, and the workspace opens Package Overview without
presenting the absence of types as an inspection failure.

### Lens ownership

Lenses are grouped by the subject they inspect:

| Subject | Lenses |
| ------- | ------ |
| Package | Overview, Dependencies |
| Library | References, Integrations, Opportunities, Analysis, Metadata |
| Type | API, Metadata, Source |
| Member | Overview, Call graph, Facts, Source, Annotated source |

Package Dependencies contains declared package dependencies by target
framework. Direct assembly references belong to Library References.
Integrations, Opportunities, Analysis, and Library Metadata also describe
assembly content.

A lens appears only in its owning subject. The UI does not retain one mixed lens
strip under Package or repeat library lenses in both Package and Library.
Lens identity is scoped by its owning subject, so Library Metadata and Type
Metadata are distinct lenses that may share a display label.

### Library selection

The Library view lists every library admitted from the active coordinate and an
`All libraries` subject when the product admits aggregate
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
option; `aria-selected` identifies the committed Library subject.

The active option has a visible focus indicator in addition to its rest or
committed-selection styling. The indicator is not conveyed by color alone,
and remains distinct from the committed `aria-selected` state. The UI scrolls
the active option into view whenever it moves.

Library selection uses manual commit:

- Up and Down Arrow move only the active option.
- Home and End move the active option to the first and last option.
- Printable input, including Space, moves the active option through prefix
  typeahead and never commits the Library subject.
- Enter commits the active option as the Library subject and starts the
  selected lens work.
- Escape or focus leaving the listbox without a commit restores the active
  option to the committed selection.

Native `select` uses the platform's equivalent selection and commit behavior.

The selected subject controls every Library lens:

- `All libraries` requests a coordinate-wide result over the complete library
  set.
- An individual library requests the same lens for only that assembly.
- The selected subject persists when switching among References, Integrations,
  Opportunities, Analysis, and Metadata.
- Changing package version or TFM retains the individual selection only when
  the same library identity is present in the new coordinate; otherwise it uses
  the new coordinate's owner-issued default Library subject. An invalid
  descriptor set follows the reconciliation failure branch and clears the
  dependent subjects.

The active library subject remains visible while the library list is filtered
or collapsed. A lens heading distinguishes aggregate results from a
single-library result.

Package and Type navigation honor the same active Library subject. With
`All libraries`, their type lists include every admitted library; with one
library selected, they include only that library's types. The type-navigation
heading shows `All libraries` or the selected library as context and links back
to the Library subject for changes. It is not a second library selector.

The active Library subject also constrains the eligible Type and Member
subjects. A Type from another library is not retained merely because it still
exists elsewhere in the package coordinate.

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
shared workspace and runs subject reconciliation.

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

Browser refresh must restore the same committed inspection view. Every
committed state transition therefore participates in canonical state when the
product packet owns a representation for it, including:

- open workspace coordinates and the active entry;
- package version and TFM;
- current Package, Library, Type, or Member subject;
- active lens or Member section;
- committed Library selection;
- selected Type, Member, overload, or body target;
- result-affecting filters; and
- selected source or body target when portable identity exists.

Transient interaction state is excluded: hover, keyboard focus, an uncommitted
listbox option, animation, incidental scroll position, and whether a disclosure
is momentarily open.

If restoration cannot resolve an artifact or product identity, it follows the
visible failure and reconciliation rules. It does not silently open a different
subject.

## Shell actions

The global shell uses visible text actions:

```text
Home   Search   Open   Settings
```

An optional decorative glyph does not replace any visible label.

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

The coordinate control may open Spotlight directly in its package scope.
Search and coordinate selection use the same result identities and acquisition
path.

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

Source, Annotated Source, Metadata explorers, and Diagnostics are working
surfaces rather than documents inset inside a general page.

### Source and Annotated Source

Source and Annotated Source use the full area to the right of Type or Member
navigation. They do not retain the old breadcrumb row, subject hero, metadata
summary, centered maximum-width column, or inset source card.

Their layout is:

```text
Types or Members | PDB Source                    open source   copy
                 | source content
```

The compact provenance/action row remains attached to the source pane. The
navigation pane and source content may scroll independently. Collapsing
navigation gives the working surface the full viewport width.

Annotated Source appears inline by default and may open a full-bleed viewer,
matching the full-bleed Metadata viewer composition. This document owns that
composition, not the Annotated Source document model or viewer internals.

Decompiler style is contextual:

- Settings owns the persistent Decompiler style preference.
- Decompiled Source, Annotated Source, and decompiled call-graph source may
  link directly to that Settings section.
- PDB Source does not show the control because authored source is unaffected.

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
closed. Changes may apply live, but every setting is rendered and owned by the
same component and state.

Diagnostics is a separate full-bleed experience launched from Settings or
Spotlight. It is not another settings implementation.

## Package-source presentation

[Browser package sources](browser-package-sources.md) owns source
registration, eligibility, capabilities, credentials, source-scoped caching,
and producer provenance. This UI owner consumes those typed results and does
not redefine them.

The initial UI does not expose feed tabs or in-workspace source switching.
Settings presents registered sources and one visible `Default feed` choice for
ordinary new package search and acquisition. That choice is a host shortcut
over the package-source owner's registration and selected-source contracts. It
does not claim one global endpoint, source-order precedence, or that only one
producer may be eligible.

Once a package is acquired, Workspace shows its product-reported producer as
read-only context. Changing `Default feed` updates later source-owner search and
acquisition inputs; it does not reinterpret bytes already loaded into a
Workspace. Inspecting the same coordinate from another producer is a new
acquisition.

Session authentication uses the credential contract from
[Browser credentials](browser-package-sources.md#browser-credentials).
Credential entry and authentication state appear only for the owning source.
Refresh may restore the selected source and workspace identity while visibly
requesting authentication again; it does not silently switch producers.

Source-scoped persistent package payloads may appear in Diagnostics and cache
management. Credentials never appear there.

Search results and version choices show producer labels when source identity is
needed to explain availability or distinguish results. The UI uses
owner-issued redacted labels rather than parsing endpoints.

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
updates canonical state after commit.

The persistent inspection command is navigation context, not an always-editable
text input. The site does not introduce a broad set of single-letter page
shortcuts. One discoverable palette shortcut plus ordinary control-specific
keyboard behavior is sufficient.

## Responsive composition

One information hierarchy adapts across viewport sizes:

- wide layouts retain Type or Member navigation beside a full working surface;
- narrow layouts move navigation into an explicit drawer or overlay;
- the inspection command remains one line;
- coordinate and leaf subject have highest truncation priority;
- intermediate qualification elides first;
- lens navigation scrolls horizontally instead of wrapping; and
- full identities remain available through accessible labels and focused or
  expanded states.

Responsive layout is not workspace state. Changing viewport size does not alter
the selected coordinate, subject, lens, filters, or canonical packet.

Density comes from removing duplication and conditionally presenting
navigation, not from making text or controls too small to use.

## Data bar and Diagnostics

The bottom data bar is one compact product-information line. It does not wrap,
expand, or host runtime diagnostics:

```text
dotnet-inspect v0.35.2 · abc1234 · Aug 27, 2026 UTC · Package source: Corporate mirror · CLI tool · Agent skill
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

## Acceptance scenarios

The redesign is not complete unless these outcomes hold:

### Ordinary package

1. Open a package with an owner-issued default Library and Type.
2. Confirm that the workspace starts on Type API.
3. Confirm that the inspection command shows the active package coordinate and
   Type without a package-tab or primary-view row.
4. Switch to Metadata and confirm that detailed Type identity appears there
   without returning to API or Source.

### Tools v2 pointer package

1. Open the pinned `DotnetInspect.TestAssets.ToolV2` pointer-package fixture.
2. Confirm that acquisition succeeds with no inspectable Library or Type.
3. Confirm that the workspace opens Package Overview.
4. Confirm that absence of types is disclosed as ordinary availability rather
   than a malformed-workspace or inspection failure.

### Refresh restoration

1. Open two coordinates.
2. Select the second coordinate, one Library, a Type, a Member, a non-default
   lens, and result-affecting filters.
3. Refresh the browser.
4. Confirm that the same coordinate, subject, lens, and committed filters are
   restored from the canonical packet.
5. Confirm that keyboard focus, hover, an uncommitted Library option, and
   incidental scroll position are not restored.

### Package-source authentication

1. Register an authenticated browser source and select it as `Default feed`.
2. Acquire a package and confirm that Workspace and the data bar show the
   owner-issued producer label.
3. Refresh after session credentials are discarded.
4. Confirm that restoration pauses with a visible authentication requirement
   and does not switch the workspace to another producer.

### Search input

1. Open Spotlight.
2. Paste a complete package coordinate without pressing another key.
3. Confirm that results update immediately.
4. Select the result and confirm that the same acquisition transition is used
   as pointer-driven package selection.

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
3. Confirm that the inspection command and lens strip remain single-line
   scrolling or truncating surfaces rather than wrapping.
4. Restore the wide viewport and confirm that coordinate, subject, lens,
   filters, and canonical state did not change.

### Data and diagnostics

1. Confirm that version, commit, UTC date, acquired producer, CLI tool, and
   agent skill occupy one non-expanding data-bar line.
2. Confirm that timings, cache counts, runtime readiness, assembly identity,
   and framework do not appear in that line.
3. Open Diagnostics and confirm that detailed runtime, source, and cache
   evidence and owner-authorized cache actions appear in the full-bleed
   surface.
