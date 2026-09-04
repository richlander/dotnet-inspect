# Inspect Web Surface Composition

This document owns browser host page-level composition and placement: which
working surfaces exist, where they sit relative to navigation, how Unified
Settings and package-source presentation are placed, how the layout responds
to viewport size, where the shell-owned Application menu and contextual
working-surface actions sit, and the data bar and Diagnostics. Internal
surface semantics -- the package-query engine, the Annotated Source viewer,
shell actions, and package-source registration -- remain with their existing
focused owners; this document places them.

## Ownership and boundaries

This owner defines:

- which working surfaces exist (Type API, Member API, Type Metadata, Source,
  Annotated Source, Package query, Diagnostics) and their page-level placement relative to
  Type/Member navigation;
- the `/query` route's placement and layout, including placement of its
  per-row `Open in workspace` action;
- Source and Annotated Source pane placement and independent scrolling;
- Unified Settings' section composition (Appearance, Decompiler style,
  Package sources) and contextual entry;
- package-source presentation placement (feed tabs absence, producer-label
  display);
- the second-line allocation between the subject/inspector region and the
  shell-owned Application menu;
- contextual working-surface action placement and responsive continuity;
- responsive composition across viewport sizes; and
- the data bar and Diagnostics surface.

It does not own:

- the package-query surface's internal request, state, evidence, and
  rendering contract (owned by
  [`package-query-experience.md`](package-query-experience.md)) or the
  product-owned facet identities, ordering, evidence, failures, and
  completion it consumes (owned by
  [`package-query-cli.md`](package-query-cli.md));
- the Annotated Source viewer's internal disclosure, actions, selection,
  annotation, media, and Escape/focus behavior (owned by
  [Annotated Source viewer interaction](annotated-source-viewer-interaction.md));
- package-source registration, eligibility, capabilities, credentials,
  source-scoped caching, or producer identity (owned by
  [Browser package sources](browser-package-sources.md));
- which subject, coordinate, or lens is active, or navigation-descriptor
  rendering (owned by
  [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md));
- the consumer effect lifecycle, browser history, or effect-authority
  validation (owned by
  [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md));
- shell actions or modal/routed classification (owned by
  [Inspect Web Shell Interaction](inspect-web-shell-interaction.md)); and
- selector-pill visual states or progressive filter disclosure (owned by
  [Inspect Web Presentation Language](inspect-web-presentation-language.md)).

## Inputs or consumed contracts

This document consumes, without redefining:

- the package-query controller, adapter, route, renderer, and engine
  projection contract owned by
  [`package-query-experience.md`](package-query-experience.md), including its
  facet catalog from [`package-query-cli.md`](package-query-cli.md);
- the Annotated Source document model and viewer-local interaction owned by
  [Annotated Source viewer interaction](annotated-source-viewer-interaction.md);
- registration, enablement, multi-selection, capability, authentication, and
  cache-action descriptors owned by
  [Browser package sources](browser-package-sources.md);
- the standard typed Workspace transition supplied by
  [Artifact acquisition and workspaces](artifact-acquisition-and-workspaces.md),
  whose returned result [Inspect Web Navigation
  Consumer](inspect-web-navigation-consumer.md) commits (canonical location,
  browser history, and focus); and
- the Application menu's identity, inventory, action outcomes, modal return,
  and shell-replacement behavior owned by
  [Inspect Web Shell Interaction](inspect-web-shell-interaction.md#application-menu);
  and
- the Slideable Subject Strip's inventories, representations, internal
  allocation, terminal-deficit behavior, and focus contract owned by
  [Inspect Web Navigation
  Presentation](inspect-web-navigation-presentation.md#slideable-subject-strip).

## Shell navigation and application actions

The second persistent shell line is one page-level navigation band with a
flexible navigation region, an optional working-surface action region, and a
fixed application region:

```text
[ subject and inspector region: minmax(0, 1fr) ]
[ working-surface actions, when supplied ] [ Application menu ]
```

This composition follows tmux's useful status-line topology: a window list
uses the flexible middle capacity while a separately allocated `status-right`
region remains available at the trailing edge
([tmux manual](https://man.openbsd.org/tmux.1)). Inspect Web transfers that
allocation relationship, not tmux's passive status content or terminal
formatting. The shell-owned three-line Application menu follows Firefox's
stable application-menu placement, as recorded by
[Shell Interaction](inspect-web-shell-interaction.md#convention-and-comparison-evidence).

The deliberate placement is the trailing edge of the second shell line, below
the title line's trailing Search/history cluster. Moving the button into the
title line would displace Search from its owned flush-right position. Moving it
into the subject region would make an application action appear to be a
SlideStrip item. Moving it into the data bar or working surface would make its
availability depend on unrelated scrolling or content.

The subject and inspector region receives every inline pixel remaining after
any working-surface action group, the Application menu control, and the
ordinary inter-region gaps. It has `min-width: 0`; Navigation Presentation's
composite resolves normal, control-free, and terminal-deficit behavior
entirely inside that assigned page boundary. Its internal minimum may scroll
inside the region, but it never pushes, overlaps, or scrolls either action
region and never creates page-level horizontal overflow.

The optional working-surface action region exists only when the active surface
supplies page-level contextual actions. It is not part of either SlideStrip and
does not add items to the Application menu. Source supplies Copy and optional
Open there; Annotated Source supplies Copy and Explore there. The complete
Source action group remains visible as the subject and inspector region yields
or scrolls under pressure.

The Application menu occupies one non-shrinking control-sized slot aligned to
the navigation band's inline end. It remains visible at every supported
viewport width and is not part of the subject or inspector tablist, their
overflow viewport, or their allocation ladder. Wide and narrow layouts keep
the active surface's region topology; width changes only the capacity assigned
to the Slideable Subject Strip.

The menu surface is placed in the shared top-level overlay layer, anchored to
the button's inline end and constrained to the viewport. It may cover the
working surface while open, but it does not reflow the shell or content. The
subject region, navigation/content grid, working-surface scrollers, and
horizontally scrolling data bar must not clip or move the menu.

Responsive allocation does not replace or clone the Application menu button.
The same rendered control remains the return-focus target while CSS changes
the subject region's capacity. Shell Interaction's logical-identity rule still
handles a genuine shell replacement.

Adoption replaces the old direct Share, Settings, and Help controls atomically:
the direct controls and Application menu never render as two simultaneous
application-action homes. A working-surface action group remains a separate
sibling throughout that replacement; implementations may place both groups in
one trailing allocation during the transition, but must preserve their
distinct accessible grouping. If a direct application control owns focus
during that one-time shell replacement, focus moves to the Application menu
button without opening it. If Settings is already open, modal focus remains
contained and ordinary dismissal resolves the new Application menu button.
Focus elsewhere in the document remains unchanged.

### Contextual working-surface actions

Contextual actions remain with the working surface or result they affect and
never enter the Application menu. Full-area source surfaces use the dedicated
page-level working-surface action region; result-local surfaces retain their
actions in the result:

- Source places `Copy` and optional `Open` in the working-surface action region
  while source content starts at the top of its pane and compact provenance
  stays attached to the bottom.
- Annotated Source places `Copy` and `Explore` in the working-surface action
  region while product provenance stays attached to the bottom.
- Package query keeps `Open in workspace` with its result row.
- Contextual Decompiler style entry remains adjacent to affected decompiled
  output.

At wide widths, a result-local working-surface identity or status occupies the
leading capacity and its action group occupies the trailing capacity. At
narrow widths, descriptive text elides first. If the complete result-local
action group still cannot fit, the same controls move together below the
description rather than disappearing, entering the Application menu, or being
recreated in another region. A resize changes layout only: a contextual
control that owns focus keeps focus, and a modal it opened returns to that same
logical surface action when the surface still exists.

An independently scrolling source or annotated-content pane begins at the top
of the working surface, while its page-level actions remain outside the
scroller. A result collection may scroll as a unit; per-result actions remain
inside their result row because that row is the context they act on.

### Placement implementation gates

Before implementation claims this placement contract, it must add and pass
these named browser tests in `workspace-titlebar.spec.ts`:

- `application menu keeps a fixed trailing slot outside SlideStrip overflow`
  proves the wide, control-free, terminal-deficit, overflowing-content, and
  horizontally scrolling data-bar cases without page-level overflow or menu
  clipping.
- `application and contextual actions preserve focus across responsive layout`
  proves that resizing does not remount the Application menu or contextual
  action controls, that atomic direct-action replacement moves focused legacy
  actions to the menu button, and that an open Settings modal resolves the new
  button on dismissal.

## Working surfaces

Type API, Member API, Type Metadata, Source, Annotated Source, and Diagnostics
are working surfaces rather than documents inset inside a general page. Package
Metadata and the Metadata Explorer retain their separately owned composition.

The package-query surface's internal query behavior remains owned by
`package-query-experience.md`; product facet identities, ordering, evidence,
failures, and completion remain owned by `package-query-cli.md`. Its former
package-tab placement stays superseded.

### Type and Member API

Type API and Member API use the full area to the right of Type or Member
navigation. They do not retain a centered document column, a large subject
hero, or repeated package, library, namespace, and target-framework context.
The persistent subject path remains the owner of that hierarchy.

The Type API surface contains:

```text
Members                         visible / total groups · overloads
Filters                                            active restrictions
member rows
                                                  select-row guidance
```

`Members` and its count use the same quiet label hierarchy as the navigation
pane rather than competing with the subject path. The count changes with the
active member filters. The collapsed `Filters` row owns member text, kind,
accessibility, and trait controls. Member rows use the complete remaining
scroll area. The bottom guidance does not repeat the count.

If active filters exclude the selected member, the detail pane retains a
full-area Member empty state with the quiet header and adjustment guidance. It
does not fall back to the inset Type heading or remove the pane's scroll owner.

Opening a member group preserves the same composition. A group with multiple
overloads renders the exact member name and overload count in the quiet header,
then gives the overload rows the remaining scroll area. Opening one overload
keeps the exact member name in that header with its kind and overload ordinal.
Overview, Call graph, and Facts scroll below it.

Member Overview retains package documentation, declaration copying, stable
identity, parameters, returns, exceptions, and applicability. It removes the
large documentation-style title and the repeated Namespace, Assembly, Package,
and framework summary because the subject path already supplies that
orientation. Call graph and Facts retain their owned result semantics and use
the same full-area scroller.

Member Source and Annotated Source remain the heading-free full-area exceptions
defined below. Loading and failure states stay visible and do not become
success-shaped empty surfaces.

At narrow widths, Type and Member header identity and status may elide, but the
overload total or selected overload ordinal is not selectively hidden.

### Type Metadata

Type Metadata uses the full area to the right of Type navigation. It does not
retain a centered document column or the large type hero. The persistent
subject path remains the owner of the selected package and type hierarchy.

The surface contains:

```text
Metadata                                  kind · accessibility
type shape rows
member composition and relationship sections
exact type identity            TFM · library · package@version
```

The quiet header labels the lens and reports type kind and accessibility
without competing with the subject path. Type shape rows begin at the top of
the independently scrolling content region and use its full width. Member
composition, interfaces, derived types, attributes, relationship graphs, and
inspection warnings retain their owned semantics and follow in the same
scroller.

The fixed bottom context row preserves the exact type identity and package
coordinate needed to compare or capture the projection without restoring a
large duplicate heading. Loading and failure states retain the same header,
scroll owner, and bottom context row; they remain visibly distinct from a
successful empty projection.

At narrow widths, header status and both context values may elide as complete
strings. The surface retains one scroll owner and creates no page-level
horizontal overflow.

### Package query

Package query is the routed `/query` working surface. It has no package tab and
no active inspection coordinate.
[Inspect Web Shell Interaction](inspect-web-shell-interaction.md#search) owns
the global Search action that closes Spotlight, validates and seeds the
initial prefix, and requests this route. [Inspect Web Navigation
Consumer](inspect-web-navigation-consumer.md#package-query-entry-and-return)
owns this route's browser-history entry and return-focus behavior, including
its visible `Back` action.

The route renders one visible level-one `Package query` heading followed by an
editable `Package ID prefix` input and `Run query` action.
[Inspect Web Shell Interaction](inspect-web-shell-interaction.md#shared-menu-and-modal-semantics)
owns entry focus on that input. [Package Query
Experience](package-query-experience.md#states) owns that a direct or
refreshed visit starts empty and uncommitted (its `Composing` state), so
seeding the prefix from Search does not itself start source work.

The desktop layout gives the product-ordered nuspec facet rail a fixed readable
column and lets rows consume the remaining width. At a narrow viewport the
query bar remains first, facets become a wrapping horizontal control region,
and results follow in one column. The prefix input, Run, Cancel, every facet,
Back, and every `Open in workspace` action keep visible text or an explicit
accessible name at both widths. Streamed row appends preserve the current
query-page scroll position.

Facet dispatch, cancellation and supersession, streaming and completion
states, and nuspec-only v1 scope are owned by
[Package Query Experience](package-query-experience.md#layout); this document
places the query bar, facet rail, and result column without redefining that
lifecycle.

`Open in workspace` is placed as a per-row action. Its request semantics --
submitting the row's product-issued package ID and exact version once, without
inferring a framework, source, or fallback from display text -- are owned by
[Package Query Experience](package-query-experience.md#layout). Its returned
result is committed by
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md#package-query-entry-and-return):
success leaves `/query` for the inspection destination, and failure keeps the
query route, rows, and request intact.

### Source and Annotated Source

Source and Annotated Source use the full area to the right of Type or Member
navigation. They do not retain the old breadcrumb row, subject hero, metadata
summary, centered maximum-width column, or inset source card.

Their layout is:

```text
Working-surface actions                                  Copy   Open
Types or Members | source content
                 | source provenance

Working-surface actions                               Copy   Explore
Types or Members | annotated source content
                 | product provenance
```

Source and Annotated Source give the page-owned working-surface action region
their contextual actions. Source keeps compact provenance as a footer attached
to the source pane; Annotated Source keeps product provenance in the same
position. Neither adds another visible title or presentation summary inside
the pane. The navigation pane and source content may scroll independently.
Collapsing navigation gives the working surface the full viewport width.

Annotated Source appears inline by default and may open the full-bleed modal
viewer governed by the shared transient-surface contract. This document owns
the inline/full-bleed placement decision; C# highlighting fidelity to the
product document's exact text and coordinates, and every other viewer-internal
behavior, are owned by
[Annotated Source viewer interaction](annotated-source-viewer-interaction.md).

Decompiler style is contextual:

- Settings owns the persistent Decompiler style preference.
- Decompiled Source, Annotated Source, and decompiled call-graph source may
  link directly to that Settings section.
- PDB Source does not show the control because authored source is unaffected.

From the full-bleed Annotated Source viewer, that action closes the viewer and
opens Settings. Closing Settings returns to inline Annotated Source without
reopening the viewer; any changed style regenerates the affected inline output.

Changing style regenerates only affected decompiler output. The preference is
not part of the title line or subject zone, and a
shared workspace does not impose the sender's style preference on its
recipient.

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

## Responsive composition

One information hierarchy adapts across viewport sizes:

- wide layouts retain Type or Member navigation beside a full working surface;
- narrow layouts replace the navigation pane with a visible
  `Types` or `Members` button that opens the shared modal navigation drawer;
- the title line and second-line shell navigation band each remain one line;
- the subject zone remains outside and above the navigation/content grid;
- the product and inspected-target root marks retain bounded icon slots in the
  title line;
- title-line Back and Forward sit immediately left of Search, which is flush
  right; the trailing cluster yields from full Search, to a `Search` button, to
  flush-right arrows, to nothing as the inspected target grows;
- subject and inspector representations adapt through Navigation
  Presentation's measurement-driven Slideable Subject Strip contract rather
  than a fixed shell breakpoint;
- the shell navigation band's fixed trailing Application menu slot remains
  visible while the Slideable Subject Strip adapts entirely inside the
  remaining flexible region;
- contextual action groups stay with their working surfaces and may move below
  descriptive text as a complete group rather than entering the shell band or
  disappearing;
- subject and inspector navigation follows Navigation Presentation's
  contiguous horizontal window contract instead of wrapping;
- subject-path segments and optional advertisements elide visually without
  losing the complete accessible subject path or segment-level copy controls;
  the Search label may collapse from its scoped label to `Search` before the
  control disappears; and
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

The data bar's narrow horizontal scrolling is independent of the shell
navigation band. It never scrolls or obscures the Application menu.

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

## Non-claims

This document does not define the package-query engine's internal request,
state, or evidence contract, the Annotated Source viewer's internal
interaction, package-source registration semantics, navigation-descriptor
rendering, the consumer effect lifecycle, or shell/modal semantics.

## Acceptance scenarios

An implementation claiming this redesign is complete must satisfy these
outcomes.

### Application and contextual action placement

1. At a wide viewport, confirm that the second shell line contains a flexible
   subject/inspector region followed by one non-shrinking Application menu
   control at the inline end. Confirm that the button is outside both tablists.
2. Narrow through the Slideable Subject Strip's normal, control-free, and
   terminal-deficit states. Confirm that only the flexible region changes
   width, its internal minimum scrolls when required, the Application menu
   remains visible, and the page does not overflow horizontally.
3. Overflow the subject strip, working surface, source content, and data bar,
   then open the Application menu. Confirm that it is anchored to the button,
   constrained to the viewport, rendered above those regions, and neither
   clipped by them nor causes reflow.
4. Confirm that direct Share, Settings, and Help controls are absent when the
   Application menu is present. During atomic adoption, focus each legacy
   control before shell replacement and confirm that focus moves to the closed
   Application menu button; focus elsewhere remains unchanged.
5. Open Settings before atomic adoption completes, install the new shell, and
   confirm that focus remains inside Settings and dismissal returns to the new
   Application menu button without opening the menu.
6. Focus the Application menu button and resize repeatedly. Confirm that the
   same control remains focused and is not cloned, moved into the title line,
   or included in SlideStrip overflow.
7. Confirm that Source and Annotated Source actions occupy a dedicated group
   between the SlideStrip region and Application menu without entering either
   inventory. Confirm that Package query and contextual Decompiler style
   actions remain with their result. At a narrow viewport, confirm that Source
   Copy and optional Open remain visible, result-local action groups move
   together below descriptive text when needed, and focused actions retain
   focus.
8. Confirm that source and annotated content begin at the top of their working
   surfaces and scroll independently of their page-level action groups.
   Confirm that result overflow remains within its contextual action
   placement.

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

### Type and Member API working surfaces

1. Open a Type API surface with no member filters and confirm that the quiet
   header, collapsed Filters row, member list, and bottom guidance exactly fill
   the inspector pane without page overflow.
2. Apply member text and selector filters and confirm that the header reports
   the live visible/total group count, the collapsed summary discloses the
   restrictions, and no second result-count row or footer count appears.
3. Open a member group with multiple overloads and confirm that the exact
   member name and overload count remain in the quiet header while the overload
   rows own the scroll area.
4. Open one overload and switch between Overview, Call graph, and Facts.
   Confirm that the quiet exact-member header remains stable, each section
   scrolls independently below it, and Overview contains no large duplicate
   hero or package-coordinate summary.
5. Apply a filter that excludes the selected member and confirm that the detail
   pane provides a full-area Member empty state and adjustment guidance without
   returning to Type scope or restoring the inset Type heading.
6. Repeat the Type list, overload picker, and selected-overload checks at a
   narrow viewport. Confirm that each surface retains its topology and creates
   no page-level horizontal overflow while preserving the overload total or
   selected overload ordinal in the rendered status.

### Type Metadata working surface

1. Open Type Metadata and confirm that the quiet Metadata header, full-width
   type shape rows, scrolling relationship sections, and bottom exact-target
   context row exactly fill the inspector pane without an inset type hero.
2. Exercise loading, projection failure, relationship warnings, and a type with
   enough sections to scroll. Confirm that each state keeps the same surface
   frame, that failures remain visible, and that only the content region
   scrolls.
3. Repeat with a long generic type identity, long package coordinate, and a
   narrow viewport. Confirm that header and footer values elide as complete
   strings without selective loss or page-level horizontal overflow.

### Source working surface

1. Open Type Source with Type navigation visible.
2. Confirm that the source pane uses all remaining width, Copy and optional
   Open appear in the working-surface action region, source content begins at
   the top of the pane, and compact provenance remains attached to its bottom.
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
4. Confirm that the title-line inspected target and second-line shell
   navigation band remain single-line rather than wrapping. Confirm that the
   title-line target elides while preserving its complete accessible path,
   only the Slideable Subject Strip uses contiguous windows and edge
   disclosure, and the Application menu retains its trailing slot.
5. With focus in the wide navigation pane, narrow the viewport and confirm that
   focus moves to the new drawer button without opening it.
6. Open the drawer, restore the wide viewport, and confirm that the drawer
   closes and focus moves to the equivalent visible navigation item or the
   active-subject heading.
7. Open Settings at the narrow viewport, restore the wide viewport, and confirm
   that focus remains contained in Settings.
8. Confirm that coordinate, subject, lens,
   filters, and canonical state did not change.

### Package query route

1. Open Package query and confirm that the route renders one level-one
   `Package query` heading, an editable `Package ID prefix` input, and `Run
   query` action.
2. At desktop and narrow widths, confirm that the prefix input, Run, Cancel,
   every facet, Back, and every `Open in workspace` action remain visible and
   keyboard reachable, and that streamed row appends preserve the current
   query-page scroll position.
3. Confirm request dispatch, facet toggling, cancellation, supersession,
   completion states, nuspec-only scope, and `Open in workspace` request
   semantics per
   [Package Query Experience's acceptance scenarios](package-query-experience.md#acceptance-scenarios).
4. Confirm Search entry, Spotlight closing, and prefix seeding per
   [Inspect Web Shell Interaction](inspect-web-shell-interaction.md#search-input).
5. Confirm initial routed-surface focus per
   [Inspect Web Shell Interaction](inspect-web-shell-interaction.md#modal-and-routed-surfaces),
   then browser-history entry/return, the visible `Back` action, and
   post-transition focus per
   [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md#package-query-entry-and-return).

### Data and diagnostics

1. Confirm that version, commit, UTC date, the complete owner-issued compact
   producer label, CLI tool, and agent skill occupy one non-expanding data-bar
   line.
2. Confirm that timings, cache counts, runtime readiness, assembly identity,
   and framework do not appear in that line.
3. Open Diagnostics and confirm that detailed runtime, source, and cache
   evidence and owner-authorized cache actions appear in the full-bleed
   surface.
