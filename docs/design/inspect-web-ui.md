# Inspect Web UI

This document is the composition map for the `dotnet-inspect` website
redesign. It states the overall redesign summary, the product dependencies
the redesign composes, the document map for its six focused owners, the
relationships among them, and the boundary with the reference product. It
does not itself define selector visual language, navigation rendering,
consumer effect lifecycle, shell interaction, or page-level composition; each
of those is a separately owned focused design linked below.

## Ownership and boundaries

This document owns only composition: the redesign summary, the cross-owner
sequencing between the focused documents, and the reference-product
boundary. It does not own:

- inspection or acquisition behavior;
- API, metadata, package, type, or member classification;
- vocabulary identities, labels, ordering, or defaults;
- artifact validation, grouping, provenance, or acquisition failure
  semantics;
- package-source resolution, authorization, credentials, or cache authority;
- subject or lens recommendation, reconciliation, or fallback;
- navigation snapshot revisions, intent ordering, or effect-authority
  validity;
- canonical packet encoding or decoding;
- CLI and library output formatting; or
- any UI-internal visual language, rendering, consumer, shell, or
  composition behavior claimed by the six focused owners in the
  [document map](#document-map) below.

## Document map

| Document | Owns |
| -------- | ---- |
| [Inspect Web Presentation Language](inspect-web-presentation-language.md) | Reusable visual and accessibility language: selector-control states, progressive filter disclosure, shared subject-heading rules, and compact source-provenance presentation. |
| [Inspect Web SlideStrip](inspect-web-slide-strip.md) | Reusable one-region ordered-item presentation: Label, optional Short Label and Icon, derived Index, whole-strip modes, contiguous sliding windows, edge disclosure, and focus preservation. |
| [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md) | Rendering and interacting with product-issued coordinate, workspace, subject, hierarchy, Library, lens, and activation descriptors, including the first composition of two SlideStrip controls as the Slideable Subject Strip. |
| [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) | The browser-side navigation-result consumer model: canonical location and refresh, browser history, product transition lifecycle, effect authority, synchronization debt, and renderer/destination lifetimes. |
| [Inspect Web Shell Interaction](inspect-web-shell-interaction.md) | The persistent shell and shared transient/routed surface interaction: shell actions, shared menu/modal semantics, Spotlight Search, Open, Settings entry, the command palette, and routed-versus-modal classification. |
| [Inspect Web Surface Composition](inspect-web-surface-composition.md) | Browser host page-level composition and placement: working surfaces, Unified Settings, package-source presentation, responsive composition, and the data bar and Diagnostics. |

Each focused document states its own Ownership and boundaries, Inputs or
consumed contracts, Non-claims, and (where applicable) implementation gates
and acceptance scenarios. This document does not repeat those contracts.

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
- [Workspace Definitions](workspace-definitions.md) owns stable portable
  fields, versioning, migration, valid combinations, per-coordinate view
  state, canonical packet projection, and restoration, tracked by
  [#4787](https://github.com/richlander/dotnet-inspect/issues/4787).
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
| Persistent hierarchy | Use one title line for product, inspected target, and Search/history; replace custom subject/inspector rendering with the Slideable Subject Strip |
| Workspace title bar | Follow `dotnet-inspect` with the icon-backed typed Package > Library > Type > Member target path, then responsive Back/Forward and flush-right Search |
| Subject navigation | Establish Workspace, Package, Type, and Member in the second row now; add Library when product descriptors are ready |
| Subject zone | Compose separately styled subject and inspector SlideStrip controls with inspector-first allocation and discrete boundary movement |
| Workspace selection | Keep ordinary single-workspace use free of tabs; manage retained coordinates inside the Workspace subject |
| Package coordinate | Render version and TFM selectors in Package content; platform is workspace content, not a workspace |
| Library inspection | Select all libraries or one library within Library |
| Type headings | Use a compact exact-target heading in API and Source; retain detail in Metadata |
| Filters | Collapse selector rows by default and summarize hidden restrictions |
| Selected controls | Use one accent selected-state treatment across selector families |
| Source provenance | Use a compact status/action row without validation prose or link glyphs |
| Search and opening | Open Spotlight from a responsive flush-right title-line control immediately after Back/Forward; use a separate local-artifact Open flow |
| Settings | Use one Settings experience with contextual entry points |
| Data bar | Show build identity, acquired source, CLI, and skill links on one line |

Together, these decisions make the web shell read like the CLI without
rendering a command string. The title line progresses from `dotnet-inspect` to
the icon-backed ordered target path, then a responsive Search/history cluster.
The full-width zone below adopts the Slideable Subject Strip for subject and
inspector navigation. Each reusable strip selects one uniform representation
mode and a contiguous window inside the width the composite assigns it.
Inspector-first allocation preserves multiple inspector controls when capacity
permits, while explicit controls move the boundary to semantic window and mode
thresholds. Segment-level copy remains on the typed title-line target. Package
coordinate editing, target inventories, and other navigation remain inside the
working surface rather than consuming persistent chrome.

This focused update establishes one new owner,
[Inspect Web SlideStrip](inspect-web-slide-strip.md), and pairs it with exactly
one first adoption in Navigation Presentation. SlideStrip owns reusable
single-region representation and window behavior. Navigation Presentation
owns the SSS composition's two tablists, different styling and navigation,
inspector-first width allocation, boundary controls, and subject-driven
inspector replacement.

Moving application and contextual actions out of the subject row remains
required product direction, but it is not part of this focused pattern and
first-adopter contract. This update only removes Surface Composition's stale
fixed-breakpoint inspector restatement so that representation remains owned by
Navigation Presentation. Shell Interaction and Surface Composition retain
their current application-action contracts until
[#5482](https://github.com/richlander/dotnet-inspect/issues/5482) defines the
shell-owned application control and
[#5483](https://github.com/richlander/dotnet-inspect/issues/5483) relocates it
and the contextual actions at the page-composition layer.

## Cross-document relationships

The six focused owners compose in one direction, from product data to
rendered pixels, with the effect-authority handoff running the other way on
every user action:

1. [Inspect Web SlideStrip](inspect-web-slide-strip.md) selects one
   whole-strip visual mode and a contiguous window for one adopter-supplied
   inventory without owning that inventory's semantic roles or navigation.
2. [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
   renders the subject, hierarchy, Library, and lens descriptors issued by
   Inspection Subject Navigation and the View Facet Registry, using the
   SlideStrip control for each SSS region and the
   shared visual language from
   [Inspect Web Presentation Language](inspect-web-presentation-language.md)
   for selector pills, progressive disclosure, and heading suppression.
3. A user action submits only an opaque product-issued action ID. Its typed
   result -- semantic outcome, synchronization disposition, and effect
   authority -- is consumed exclusively by
   [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md),
   which installs the returned snapshot, commits canonical location and
   browser history, and resolves focus and announcement under that
   authority. Navigation Presentation never validates authority itself; it
   only renders whatever the consumer installs.
4. [Inspect Web Shell Interaction](inspect-web-shell-interaction.md) owns the
   persistent shell and the modal/routed surfaces it launches (Spotlight,
   Open, Settings, Diagnostics). It hands committed navigation actions to the
   same consumer for focus resolution and history commitment, and it hosts
   the persistent live region and focus anchor the consumer targets.
5. [Inspect Web Surface Composition](inspect-web-surface-composition.md)
   places the working surfaces those other owners render -- Source,
   Annotated Source, Package query, Settings, and Diagnostics -- into the
   page layout, deferring their internal behavior to each surface's existing
   focused owner (`package-query-experience.md`, `package-query-cli.md`,
   [Annotated Source viewer interaction](annotated-source-viewer-interaction.md),
   and [Browser package sources](browser-package-sources.md)).

No focused document redefines another's contract. A change to one owner's
rendering, interaction, or placement rules does not require reopening the
others unless it changes the opaque descriptor, action ID, or typed outcome
they exchange.

## Reference-experience boundary

No external application is the overall Inspect Web UX target. The product's
Workspace -> Package -> Library -> Type -> Member model and its lenses remain
normative. Reference applications supply evidence for individual capabilities:

| Capability | Reference evidence |
| ---------- | ------------------ |
| Product-to-subject-to-inspector grammar | the `dotnet-inspect` CLI |
| Elastic strip allocation and returned unused width | tmux window list and status line |
| Spotlight, command palette, keyboard navigation, and focus | Visual Studio Code |
| Dense web-native package exploration and shareable state | npmx.dev |
| Assembly, Type, and Member hierarchy | ILSpy and Visual Studio Object Browser |
| Read-only inspection posture and evidence panes | Chrome DevTools |
| URLs, browser history, and familiar web conventions | GitHub |

These references are neither architectural owners nor templates to copy. tmux
is the primary allocation evidence because its natural-width window entries
return unused space to neighboring status content; ordinary browser, terminal,
and editor tabs typically reserve a fixed tab region instead. SlideStrip
diverges from tmux by preserving typed item identity, complete accessible
labels, policy-selected visual representations, and focused-item reveal rather
than exposing raw window indexes or clipping text without identity. The
CLI correspondence does not turn the title line into editable command text;
Visual Studio Code does not imply an editor workbench, command center, Activity
Bar, file Explorer, editor tabs, movable regions, or desktop-window
assumptions; and Chrome DevTools does not imply a browser-debugging information
architecture.

No single established application or component model matches SlideStrip or
the complete Slideable Subject Strip composition. Their conventional parts
form a deliberate hybrid: tmux contributes elastic natural-width allocation,
Priority+ navigation contributes deterministic whole-strip mode preference,
carousel and scrollable-tab models contribute a disclosed contiguous window
without removing identities, and split views contribute user-directed
allocation between adjacent regions. SlideStrip diverges from ordinary
Priority+ controls by sliding one consistently represented window instead of
mixing compact and full items or moving entries into an overflow menu. The SSS
diverges from ordinary split views by moving between semantic window and mode
thresholds instead of a draggable pixel-sized divider.

[npmx.dev](https://npmx.dev/) contributes fast package exploration, density,
code-first working surfaces, keyboard access, and persistent package context.
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
dotnet-inspect model. Every reference is bounded to the capability named above
and none redefines the product domain.

## Implementation gates

The named Inspect Web test gates for this redesign -- `navigation-consumer.test.ts`
and `navigation-focus.test.ts` -- are recorded beside the contract they prove,
split between
[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md#implementation-gates)
(descriptor rendering and widget focus) and
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md#implementation-gates)
(effect authority, installation, history, and destination lifetime).
`InspectWebPackageSourceSettingsTests.RendersEnablementAndSelectionWithoutDefaultFeed`
is recorded in
[Inspect Web Surface Composition](inspect-web-surface-composition.md#package-source-composition).
Until those gates exist and pass, the linked documents define the target
contract but do not claim Inspect Web implementation conformance.
