# Inspect Web Surface Composition

This document owns browser host page-level composition and placement: which
working surfaces exist, where they sit relative to navigation, how Unified
Settings and package-source presentation are placed, how the layout responds
to viewport size, and the data bar and Diagnostics. Internal surface
semantics -- the package-query engine, the Annotated Source viewer, and
package-source registration -- remain with their existing focused owners;
this document places them.

## Ownership and boundaries

This owner defines:

- which working surfaces exist (Source, Annotated Source, Package query,
  Diagnostics) and their page-level placement relative to Type/Member
  navigation;
- the `/query` route's placement and layout, including placement of its
  per-row `Open in workspace` action;
- Source and Annotated Source pane placement and independent scrolling;
- Unified Settings' section composition (Appearance, Decompiler style,
  Package sources) and contextual entry;
- package-source presentation placement (feed tabs absence, producer-label
  display);
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
- the routed-versus-modal classification and shell actions owned by
  [Inspect Web Shell Interaction](inspect-web-shell-interaction.md).

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
- the title line and full-width subject zone each remain one line;
- the subject zone remains outside and above the navigation/content grid;
- the product and subject-root icon slots have equal width and padding so their
  following text shares one left alignment;
- Back and Forward remain immediately left of the subject-zone Search control,
  and Share remains visible;
- Help and Settings disappear before Search, the `dotnet-inspect` Home
  control, subject navigation, or inspected-subject actions;
- subject and inspector navigation scroll horizontally instead of wrapping;
- subject-path segments and optional advertisements elide visually without
  losing the complete accessible subject path or segment-level copy controls;
  the Search label may collapse to its icon after those semantics are retained;
  and
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

## Non-claims

This document does not define the package-query engine's internal request,
state, or evidence contract, the Annotated Source viewer's internal
interaction, package-source registration semantics, navigation-descriptor
rendering, the consumer effect lifecycle, or shell/modal semantics.

## Acceptance scenarios

An implementation claiming this redesign is complete must satisfy these
outcomes.

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
4. Confirm that the workspace title bar, subject/inspector strip, and target
   selector remain single-line scrolling or truncating surfaces rather than
   wrapping.
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
