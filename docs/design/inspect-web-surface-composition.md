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
  Annotated Source, Package query, Diagnostics) and their page-level placement
  relative to Type/Member navigation;
- the `/query` route's placement and layout, including placement of its
  per-row `Open in workspace` action;
- Source and Annotated Source pane placement and independent scrolling;
- Unified Settings' section composition (Appearance, Decompiler style,
  Package sources) and contextual entry;
- package-source presentation placement (feed tabs absence, producer-label
  display);
- the placement and allocation of the two persistent shell rows, including the
  application-scope and subject/inspector regions, inspected target,
  shell-owned Application menu, and contextual working-surface actions;
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
  Presentation](inspect-web-navigation-presentation.md#slideable-subject-strip);
  and
- the Query/Workspace application-scope inventory, selection, and interaction
  owned by
  [Inspect Web Navigation
  Presentation](inspect-web-navigation-presentation.md#application-scope-strip).

## Shell navigation and application actions

The persistent shell is two non-wrapping page-level rows:

```text
row one: [product] [Query | Workspace] [subject and inspector region]
         [Back | Forward] [Search] [Application menu]
row two: [inspected target: minmax(0, 1fr)]
         [working-surface actions, when supplied]
```

Row one contains navigation and the stable application-action home. The product
control and Application menu occupy non-shrinking slots. The
Navigation Presentation-owned application-scope strip precedes the
subject/inspector region, which receives the primary flexible allocation. The
Shell Interaction-owned history and Search cluster follows it. Search
progresses from its full label to its compact label and then disappears;
the application-scope strip yields next, while history remains available until
a narrower width. History then disappears before the Slideable Subject Strip
starts reducing active Subject or Inspector identity. Once those controls have
yielded, the SlideStrip resolves its own normal, control-free, and
terminal-deficit states inside the remaining page boundary.

The application-scope strip uses a distinct quiet treatment and may be removed
at constrained widths only after focus has left it. Query remains reachable
through Spotlight's global keyboard entry and Workspace through hierarchical
drill-out or a return action. The standalone `/query` surface does not render
this strip; its visible heading and route-specific Back action orient it.

The subject and inspector region has `min-width: 0`. Its preferred allocation
is large enough to expose complete common inventories, but exact pixel
thresholds are presentation tuning rather than product state. Its internal
minimum may scroll inside the region, but it never pushes, overlaps, or scrolls
the product control or Application menu and never creates page-level
horizontal overflow.

The shell-owned three-line Application menu follows Firefox's stable
application-menu placement, as recorded by
[Shell Interaction](inspect-web-shell-interaction.md#convention-and-comparison-evidence).
It occupies the non-shrinking inline-end slot in row one, after Search. It
remains visible at every supported viewport width and is not part of either
tablist, their overflow viewport, or their allocation ladder.

Row two starts the inspected target at the shell's inline edge and reserves its
trailing capacity for optional page-level contextual actions. Separating the
target from Search means an unusually long Package, Type, or Member path cannot
collapse row-one navigation. Navigation Presentation owns target rendering and
elision inside its allocation.

The optional working-surface action region exists only when the active surface
supplies page-level contextual actions. It is not part of either SlideStrip and
does not add items to the Application menu. Source supplies Copy and optional
Open there; Annotated Source supplies Copy and Explore there. The target yields
space while the complete action group remains visible.

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
sibling throughout that replacement and must preserve its distinct accessible
grouping. If a direct application control owns focus
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
- `row-one Search yields before Subject and Inspector navigation` proves the
  row-one pressure order and that a long row-two target does not affect Search.
- `the inspected target occupies the second row and package selectors stay in
  content` proves row separation and left target alignment.
- `Source fills the detail area below working-surface actions and above
  provenance` proves that page-level Source actions occupy row two rather than
  either navigation or application inventory.

## Working surfaces

Type API, Member API, Type Metadata, Package Overview, Package Dependencies,
Library Metadata, Source, Annotated Source, and Diagnostics are working surfaces
rather than documents inset inside a general page. The Metadata Explorer retains
its separately owned full-bleed composition.

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
orientation.

The C# declaration is the first stable artifact below the quiet member header.
It remains anchored while package documentation is loading or resolves to a
summary, failure, or absence state. A compact `Summary` region follows the
declaration and keeps every documentation state visible without giving missing
documentation the surface's highest prominence. Stable identity begins as a
separate structured section after that region.

```text
C# declaration                                      Copy
signature

Summary
package documentation, loading, failure, or absence

Identity
stable selector, digest, canonical signature

Parameters                                      count
name
modifier + type · default                       documentation

Returns
return type                                     documentation

Exceptions                                      documented count
exception type                                  condition

Applies to  target framework
```

The declaration and identity use the working surface's available width while
summary prose retains a readable line length. Subsequent documentation sections
retain their established readable measure rather than expanding with the
declaration and identity. At constrained widths the declaration scrolls
horizontally without separating its Copy action, and identity labels stack
above their values rather than forcing page-level horizontal overflow.

The member contract below identity uses compact structured sections rather than
restarting a documentation-page hierarchy. Parameters keep name, modifier,
type, and default together as row identity while documentation occupies a
separate readable column. Documented Returns uses the same
identity-and-documentation alignment, and Exceptions pairs each exception type
with its documented condition. Loading, failure, and absence remain distinct
for parameter and exception documentation; a failed query does not become a
claim that documentation is absent. Returns remains absent when no returns
documentation was supplied because the current typed surface does not identify
whether that absence describes a void member or missing package documentation.

Contract rows stack identity above documentation according to the detail pane's
width. Long parameter names, generic types, defaults, and namespace-qualified
exception types remain contained within their row. Applicability closes the
overview as compact terminal metadata rather than another full-weight article
section.

Call graph and Facts retain their owned result semantics and use the same
full-area scroller.

Member Facts begins with a compact `Analysis summary` that separates static
analysis values and supporting IL offsets from subject identity. The subject
path and quiet member header retain declaring type, member kind, and overload
ordinal; the summary does not repeat them. The metadata token follows the
summary as compact identity, before the detail sections.

Summary rows preserve every existing signal, including explicit zero and
`no` values. IL offsets remain non-interactive evidence, not Finding-navigation
links or runtime measurements. The summary retains a readable measure; when
the detail pane narrows, supporting offsets move below their value. Long
values and evidence wrap within their row without widening the member
scroller. Loading and failure retain the member header and remain visibly
distinct from a successful zero-valued summary.

Allocation facts uses compact occurrence rows rather than a wide column strip.
Each occurrence keeps its IL offset and kind with the allocated type, followed
by its heap-counting flag, multiplicity, path, escape, loop flag, and size
estimate. All nine fields remain visible in the returned occurrence order;
there is no expander or new navigation. The section count describes occurrences,
not the heap-counted summary total or a runtime measurement.

Heap counting and loop membership use explicit `yes` and `no` values. A
non-heap-counted occurrence does not imply stack allocation. A missing type is
visibly unavailable; a missing estimate is not available rather than zero.
Returned byte estimates remain labeled as estimates, and raw analysis
classifications retain their values.

Occurrence rows use the summary's readable measure and separators rather than
cards. At constrained pane widths, properties use fewer columns and provenance
moves above the type. Long types, offsets, and property values wrap within
their occurrence. This trades some vertical density for complete visible
evidence without horizontal scrolling. A successful empty result retains
the section and its explicit absence message; loading and failure remain
separate top-level Facts states. Calls and subsequent detail sections retain
their existing presentation.

#### Graph Explore

Member Call graph retains its inline default and exposes `Explore` in the
working-surface action row when a graph result is available. Explore places
the existing interactive result in a full-viewport dialog: a quiet heading,
selected-member context, and Close above the graph, with scope, legend, and
Mermaid source remaining accessible as secondary information. The diagram
takes the remaining space rather than retaining the inline fixed-height card.
At narrow widths context may elide, but Close and graph controls stay available
without page-level horizontal overflow.

This document owns that placement contract. The existing
[shared modal semantics](inspect-web-shell-interaction.md#shared-menu-and-modal-semantics)
own focus, dismissal, background containment, modal replacement, and history.
The implementation uses the browser's native modal dialog and the existing
shell focus helper rather than adding a second custom inert-background system.
Annotated Source's explicit Explore action is the local interaction precedent;
its separate source-viewer sessions are not needed for a graph placement change.

Opening and closing relocate one live graph, without another query, Mermaid
mount, or loss of pan/zoom. Fit remains explicit for the larger viewport.
Ordinary dismissal returns focus to Explore and keeps the selected member and
platform descent. Existing result replacement, including theme changes, may
remount the graph as it does inline. Workspace expansion, platform drill/back,
and their loading, diagnostics, no-body, and failure results stay in Explore.
A known member or source destination closes Explore before navigation; failure
keeps the prior result visible inline and focuses Explore or its stable heading.
Leaving the originating member, overload, package, framework, or inspector
closes the viewer. Opening another dialog also closes it, without reopening it
when that dialog is dismissed.

Package Dependencies uses the same viewer and action-row placement. Explore is
available once dependency groups have been read, including a selected group with
no connected packages. The viewer contains the manifest-group selector, exact-group
notice, graph, and workspace/diagram diagnostics. Package coordinate controls,
dependency lists, assembly references, and the coordinate footer remain on the
underlying page. The viewer identifies the inspected package; group buttons
identify the selected manifest framework independently of the active coordinate.
Selecting a group stays in Explore and updates the existing list and graph.
Closing retains that selection. Pending graph rendering can complete in either
placement; opening or closing does not restart it.

Dependency nodes use the same keyboard activation and drag suppression as Call
graph nodes. Selecting a loaded or unloaded package closes Explore and uses the
existing dependency-navigation path. Success focuses the destination heading;
failure exposes the existing inline notice and restores Explore focus. A package,
version, active framework, assembly, or inspector change closes the viewer rather
than moving an unrelated graph into it. Empty, query-failed, render-failed,
partial-workspace, and truncated results remain visible; graph controls do not
cover truncation diagnostics.

Type Metadata uses the same viewer when its current projection contains a
relationship graph. Only that graph and its relationship warnings move; type
shape, member composition, related-type lists, attributes, and the coordinate
footer stay inline. Pending diagram rendering can complete in either placement.
Browsable nodes use the shared keyboard activation and drag suppression;
unavailable types remain non-interactive with an accessible explanation. Type
activation closes Explore before the existing typed navigation path runs. It
does not acquire another assembly or reinterpret a display label as identity.
Leaving the selected type, package, framework, assembly, or Metadata inspector
closes Explore. Same-owner projection loading and failure remain visible in an
already-open viewer. A replacement without a relationship graph returns to inline
Metadata and focuses its heading rather than leaving an empty explorer.

The browser-only presentation scope was explicitly approved for
[the two-step adoption tracker](https://github.com/richlander/dotnet-inspect/issues/5867).
Step 1 is [Member Call graph](https://github.com/richlander/dotnet-inspect/issues/5868);
step 2 is [Package Dependencies](https://github.com/richlander/dotnet-inspect/issues/5904)
using the same placement component. The separately approved
[Type Metadata adoption](https://github.com/richlander/dotnet-inspect/issues/5943)
is a one-step end-to-end tracker: connect that production browser consumer to the
existing viewer. Inline presentation is not retired.
Existing typed `BrowserCallGraph`/`InspectedCallGraph` and
`DependencyGraphModel`/`DependencyGraphResult` results, Type Metadata's
`TypeGraphMeta`, target bindings, and
Mermaid lowering continue to supply graph data and node identity. This host-only
placement change bypasses Markout for the interactive browser canvas, adds no
graph-analysis substrate, and does not change CLI output, query scope, traversal,
acquisition, or layout algorithms.

The browser gate covers live DOM and interaction retention across placement
changes, result replacement, pending completion, no-body/failure visibility,
dialog focus and dismissal, and narrow geometry. Published Wasm evidence covers
the production action row and destination navigation. Dependency coverage also
exercises group changes, empty groups, pending completion, truncation geometry,
and package navigation/failure. Live platform drill/back evidence is reported
separately from component coverage when acquisition is unavailable.
Type coverage exercises the production Metadata renderer, relationship-warning
placement, available/unavailable nodes, pending completion, projection replacement,
and type navigation; published Wasm evidence covers the real action row and typed
destination.

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

### Package Overview

Package Overview uses the complete package inspector area, following Library
Metadata and Package Dependencies rather than repeating the package hero and inset
coordinate editor. The persistent subject path supplies package identity.

```text
Overview                                      type and member totals
Version · Framework
admitted libraries, package documents
package@version                                    active framework
```

The quiet header preserves the package's type and member totals. Existing
Version and Framework controls occupy one compact row. One independently
scrolling content region contains the admitted-library inventory and document
links. The platform library picker remains with the Libraries section. Library
rows enter the existing Library subject; kind and namespace navigation remain
within that subject. Document opening, counts, and ordering retain their semantics.

The bottom context row preserves the exact package/version and active
framework. At narrow widths the Libraries return control shares the quiet header,
controls wrap within their row, and header/footer values may elide as complete
strings. Long library names, asset paths, and document names remain contained without
page-level horizontal overflow. Many rows scroll inside Overview while its
header, controls, and coordinates remain in place.

Overview presents the already-loaded package. Existing acquisition loading,
failure, and partial-package notices remain in their current host presentation;
this placement change introduces no independent Overview query or state machine.
Empty inventories retain their zero totals and any available package documents.

[The one-step adoption tracker](https://github.com/richlander/dotnet-inspect/issues/6073)
connects the production browser Overview consumer to this frame and retires its
generic hero/inset-coordinate composition. The user explicitly approved this
browser-only presentation scope. Existing typed package data supplies the
content and counts; browser HTML rendering remains the lowering boundary rather
than Markout because this slice arranges interactive controls and navigation.
The frame reuses the current package-surface conventions, not a new general
rendering architecture. Other package lenses remain separate consumers.

Browser coverage exercises the production frame, overflow, narrow navigation
placement, and controls. Published-site evidence separately exercises the full
production Library entry, coordinate, and document navigation. The production
composition fixture also confirms that returning from Library restores Overview.

### Package Dependencies

Package Dependencies uses the complete package inspector area. It does not
retain the generic package hero or inset Package coordinate section used by
document-style package lenses. The persistent subject path remains the owner
of the package identity.

The surface contains:

```text
Dependencies                         package and reference count or state
Version · Framework
target-framework groups, graph, package dependencies, assembly references
package@version                                             active framework
```

The quiet header labels the lens and reports the selected dependency group's
package count together with the selected assembly's direct reference count.
A compact control row keeps Version and Framework available. Dependency-group
selection remains with the result because it selects a manifest group rather
than changing the active package coordinate.

One independently scrolling content region retains the exact-group notice,
target-framework selector, dependency graph, package dependency list, assembly
references, and partial workspace warning. Selecting another manifest group
patches its list and graph in place without changing the surface frame or
resetting the package coordinate.

The action row also exposes [Graph Explore](#graph-explore), retaining inline
presentation as the default and the same dependency-group selection in both
placements.

The fixed bottom context row preserves the exact package coordinate and active
framework. Loading, query failure, no-dependency, no-exact-group, graph
failure, and partial-workspace states retain the same header, controls, scroll
owner, and context row. Failures remain visibly distinct from successful
empty results.

At narrow widths, the `Types` return control shares the quiet header, controls
wrap within their row, and header and footer values may elide as complete
strings. The surface creates no page-level horizontal overflow. This slice
does not change dependency selection, graph construction or navigation,
Package Overview, Integrations, Opportunities, Analysis, Package Metadata, or
the Metadata Explorer.

### Package Metadata

Package Metadata uses the complete package inspector area. It does not retain
the generic package hero or inset Package coordinate section used by the other
package lenses. The persistent subject path remains the owner of the package
identity.

The surface contains:

```text
Metadata images                                  assembly count or state
Version · Framework · optional platform Library
assembly image facts, heaps, and populated tables
package@version                           TFM · optional scoped library
```

The quiet header labels the image-level lens and reports its assembly count or
current state. A compact control row keeps Version and Framework available and,
for the platform package, adds the scoped Library selector. The independently
scrolling content region begins with assembly metadata rather than a repeated
package summary. Each assembly retains its format, header facts, heaps, and
populated-table controls, and those controls continue to open the separately
owned Metadata Explorer.

The fixed bottom context row preserves the exact package coordinate, target
framework, and optional scoped library. Library-required, loading, failure,
partial-failure, and no-image states retain the same header, controls, scroll
owner, and context row. Failures remain visibly distinct from a successful
empty result.

At narrow widths, controls wrap within their row and header and footer values
may elide as complete strings. The surface creates no page-level horizontal
overflow. This slice does not change the Metadata Explorer or other package
lenses.

### Package query

Package query is the routed `/query` working surface. It has no package tab and
no active inspection coordinate.
[Inspect Web Shell Interaction](inspect-web-shell-interaction.md#search) owns
the global Search action that closes Spotlight, validates and seeds the
initial prefix, and requests this route. [Inspect Web Navigation
Consumer](inspect-web-navigation-consumer.md#package-query-entry-and-return)
owns this route's browser-history entry and return-focus behavior, including
its visible `Back` action.

The page header contains the product home link and `Back`, not the
`Query`/`Workspace` application-scope buttons. This placement is independent of
viewport width and whether a workspace is retained in the session; the
workspace shell keeps its application-scope strip.

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
not part of either persistent shell row, and a
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

- wide layouts retain Type or Member navigation beside a full working surface,
  using a bounded inventory column rather than a percentage split; the column
  stays within its readable minimum and maximum while the detail pane receives
  all remaining width, and no draggable divider is introduced;
- narrow layouts replace the split with one presentation-local pane:
  inventory or detail. Detail exposes a visible `Types` or `Members` button
  that switches to the corresponding full-width inventory; activating an
  inventory row switches back to detail, and inventory retains a visible
  detail-return action even when filters leave no activatable row;
- the narrow inventory/detail choice is not workspace state or product
  navigation. Switching panes does not change the selected coordinate,
  subject, lens, filters, canonical packet, URL, or browser history;
- the return button shares the quiet 40-pixel working-surface header when one
  exists. Heading-free Source and Annotated Source and document-style package
  surfaces use a narrow-only local navigation band rather than inventing a
  working-surface title;
- both persistent shell rows remain one line;
- the row-one subject/inspector region remains outside and above the
  navigation/content grid;
- the product and inspected-target root marks retain bounded icon slots in
  their respective rows;
- row-one Back and Forward sit immediately left of Search, and the Application
  menu terminates the row; the navigation cluster yields from full Search, to
  a `Search` button, to arrows, to nothing before the Slideable Subject Strip
  starts reducing active identity;
- subject and inspector representations adapt through Navigation
  Presentation's measurement-driven Slideable Subject Strip contract rather
  than a fixed shell breakpoint;
- row one's fixed trailing Application menu slot remains visible while the
  Slideable Subject Strip adapts entirely inside its assigned region;
- the row-two inspected target elides independently of row-one Search;
- page-level contextual action groups occupy row two; result-local action
  groups stay with their working surfaces and may move below descriptive text
  as a complete group rather than entering either shell navigation inventory
  or disappearing;
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

Activating `Types` or `Members` moves focus to the visible inventory and scrolls
its selected row into view. Activating an inventory row moves focus to the
return button in the resulting narrow detail pane. A filter-focus command first
switches to inventory, then opens and focuses the applicable filter.

When crossing into the narrow layout, focus inside Type or Member navigation
keeps inventory visible; focus inside detail keeps detail visible. Otherwise
the retained presentation-local pane remains visible. Widening reveals both
panes without moving focus, except that focus owned by either removed
narrow-only pane-switch control moves to the equivalent visible navigation
list. A viewport change never moves focus out of another open modal.

Density comes from removing duplication and conditionally presenting
navigation, not from making text or controls too small to use.

The data bar's narrow horizontal scrolling is independent of the shell
navigation band. It never scrolls or obscures the Application menu.

## Data bar and Diagnostics

The bottom data bar is one compact product-information line. It does not wrap,
expand, or host runtime diagnostics:

<!-- markdownlint-disable MD013 -->
```text
dotnet-inspect v0.35.2 · abc1234 · Aug 27, 2026 UTC · Package source: Corporate mirror (pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json) · CLI tool · Agent skill
```
<!-- markdownlint-enable MD013 -->

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

1. At a wide viewport, confirm that row one contains product,
   subject/inspector, history, Search, and one non-shrinking Application menu
   control in that order. Confirm that the button is outside both tablists.
2. Confirm that row two contains the left-aligned inspected target followed by
   page-level contextual actions when supplied.
3. Narrow the viewport and confirm that Search progresses from full to compact
   to hidden, then history hides, before the Slideable Subject Strip starts
   reducing active identity. Continue through its normal, control-free, and
   terminal-deficit states; confirm that the Application menu remains visible
   and the page does not overflow horizontally.
4. Overflow the subject strip, working surface, source content, and data bar,
   then open the Application menu. Confirm that it is anchored to the button,
   constrained to the viewport, rendered above those regions, and neither
   clipped by them nor causes reflow.
5. Confirm that direct Share, Settings, and Help controls are absent when the
   Application menu is present. During atomic adoption, focus each legacy
   control before shell replacement and confirm that focus moves to the closed
   Application menu button; focus elsewhere remains unchanged.
6. Open Settings before atomic adoption completes, install the new shell, and
   confirm that focus remains inside Settings and dismissal returns to the new
   Application menu button without opening the menu.
7. Focus the Application menu button and resize repeatedly. Confirm that the
   same row-one control remains focused and is not cloned or included in
   SlideStrip overflow.
8. Confirm that Source and Annotated Source actions occupy a dedicated row-two
   group without entering either navigation inventory or the Application menu.
   Confirm that Package query and contextual Decompiler style
   actions remain with their result. At a narrow viewport, confirm that Source
   Copy and optional Open remain visible, result-local action groups move
   together below descriptive text when needed, and focused actions retain
   focus.
9. Confirm that source and annotated content begin at the top of their working
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

### Package Metadata working surface

1. Open package Metadata and confirm that the quiet header, compact Version and
   Framework controls, assembly image facts, and bottom exact package context
   fill the inspector pane without the generic package hero or inset coordinate
   section.
2. Open platform Metadata before and after choosing a Library. Confirm that the
   Library selector remains in the compact control row, the required-selection
   state keeps the full-area frame, and the selected assembly's heap and table
   controls still open the Metadata Explorer.
3. Exercise loading, read failure, partial failure, no-image, and a package
   containing enough assembly content to scroll. Confirm that only the content
   region scrolls and that failure is never presented as successful emptiness.
4. Repeat with long package and library names at a narrow viewport. Confirm
   that controls wrap within their row, context values elide as complete
   strings, and no page-level horizontal overflow appears.

### Package Dependencies working surface

1. Open package Dependencies and confirm that the quiet header, compact
   Version and Framework controls, target-framework groups, dependency graph,
   package dependencies, assembly references, and bottom exact package context
   fill the inspector pane without the generic package hero or inset coordinate
   section.
2. Switch manifest target-framework groups and confirm that the dependency
   list and graph update in place while the surface frame, package coordinate,
   and scroll ownership remain stable. Open or load a dependency from both the
   list and graph and confirm that existing navigation behavior is preserved.
3. Exercise loading, query failure, no declared dependencies, no exact group,
   graph rendering failure, and partial workspace failure. Confirm that each
   keeps the full-area frame and that failure is never presented as successful
   emptiness.
4. Open a package with enough graph and list content to scroll. Repeat with a
   long package coordinate and narrow viewport; confirm that the `Types`
   control shares the quiet header, controls wrap within their row, context
   values elide as complete strings, and no page-level horizontal overflow
   appears.

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
2. Narrow the viewport until detail occupies the full content frame and Type
   navigation is replaced by a visible `Types` button.
3. Activate `Types` and confirm that inventory occupies the same full content
   frame, focus moves to its list, the selected row remains visible, and URL
   and browser history do not change. Clear the list with filters and confirm
   that the visible detail-return action still restores the prior detail.
4. Confirm that both persistent shell rows remain single-line rather than
   wrapping. Confirm that the row-two target elides while preserving its
   complete accessible path, only the Slideable Subject Strip uses contiguous
   windows and edge disclosure, and the Application menu retains its row-one
   trailing slot.
5. Activate the selected Type row and confirm that detail returns, focus moves
   to `Types`, and URL and browser history remain unchanged. Activate a
   different row and confirm that its ordinary product navigation semantics
   still apply before detail returns.
6. With focus in the wide navigation pane, narrow the viewport and confirm that
   inventory remains visible. With focus in detail, repeat and confirm that
   detail remains visible. Restore the wide viewport and confirm both panes
   return without disturbing focus, except that a focused narrow return button
   transfers to the visible navigation list.
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
