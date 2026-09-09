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
| [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md) | Rendering and interacting with product-issued coordinate, workspace, subject, hierarchy, Library, lens, and activation descriptors, including adaptive full-label Tabs or current-label Choosers for subject and inspector navigation. |
| [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) | The browser-side navigation-result consumer model: canonical location and refresh, browser history, product transition lifecycle, effect authority, synchronization debt, and renderer/destination lifetimes. |
| [Inspect Web Shell Interaction](inspect-web-shell-interaction.md) | The persistent shell and shared transient/routed surface interaction: shell actions, shared menu/modal semantics, Spotlight Search, Open, Settings entry, the command palette, and routed-versus-modal classification. |
| [Inspect Web Surface Composition](inspect-web-surface-composition.md) | Browser host page-level composition and placement: working surfaces including Member Diff, Unified Settings, package-source presentation, responsive composition, and the data bar and Diagnostics. |

Each focused document states its own Ownership and boundaries, Inputs or
consumed contracts, Non-claims, and (where applicable) implementation gates
and acceptance scenarios. This document does not repeat those contracts.

## Product dependencies

This document composes six adjacent owner contracts without defining them:

- [Inspection Subject Navigation](inspection-subject-navigation.md) owns
  Workspace-bound Package, Library, Type, and Member
  descriptors, availability, initial recommendation, and reconciliation, plus
  retained-session intent and effect authority. Workspace is the inventory
  container for retained coordinate occurrences; the website does not recreate package
  tabs or a second coordinate-selection identity. The shared capability and
  its CLI and Browser/Wasm adoption sequence are tracked end to end by #5512.
  [#5013](https://github.com/richlander/dotnet-inspect/issues/5013) strengthens
  that same owner's lens recommendation with non-vacuous role-first selection,
  a direct-Member rule, deterministic fallback, and all-non-success precedence.
- [View Facet Registry](view-facet-registry.md) owns stable facet IDs,
  descriptors, structural applicability, order, and facet-availability
  outcomes.
- [Workspace Definitions](workspace-definitions.md) owns stable portable
  fields, versioning, migration, valid combinations, per-coordinate view
  state, canonical packet projection, and restoration. #4787 established the
  current version-2 shape; #5525 tracks adoption of explicit Workspace and
  Package subjects plus optional retained occurrence and descendant context
  independent from the active subject.
- [Browser package sources](browser-package-sources.md#default-feed-decision)
  owns browser source selection and the decision that first-run Gallery
  bootstrap does not become default-feed or acquisition-preference semantics.
- [Member source diff presentation](member-source-diff-presentation.md) owns
  canonical comparison endpoints, analytical relations, statistics, and the
  shared mapped-text presentation consumed by Member Diff.
- [Inspect Web source-diff transport](inspect-web-source-diff-transport.md)
  owns the complete typed worker payload, admission, closed outcomes,
  provenance, and optional authorized endpoint destinations consumed by the
  browser surface.

Inspect Web renders those owner-issued descriptors and outcomes. Their product
semantics are not prerequisites for reviewing the UI composition in this
document and are not re-specified here.

## Current redesign

This is a coordinated information-architecture and density rework, not a set of
independent cosmetic changes.

| Area | Direction |
| ---- | --------- |
| Persistent hierarchy | Use one title line for product, Query/Workspace application scopes, subject/inspector navigation, and Search/history; keep the inspected target on its separate row |
| Workspace title bar | Follow `dotnet-inspect` with the icon-backed typed Package > Library > Type > Member target path, then responsive Back/Forward and flush-right Search |
| Application scopes | Render Query and Workspace in a separate quiet strip that yields before inspection identity under width pressure |
| Subject navigation | Establish Package, Type, and Member now; add Library when product descriptors are ready |
| Subject zone | Render complete full-label subject and inspector tablists when they fit; otherwise adapt either group to its current-label Chooser |
| Workspace selection | Keep ordinary single-workspace use free of coordinate tabs; manage retained coordinates inside the Workspace application scope |
| Package coordinate | Render version and TFM selectors in Package content; platform is workspace content, not a workspace |
| Library inspection | Select all libraries or one library within Library |
| Type headings | Use a compact exact-target heading in API, no duplicate local heading in full-area Source, and detailed context in Metadata |
| Filters | Collapse selector rows by default and summarize hidden restrictions |
| Selected controls | Use one accent selected-state treatment across selector families |
| Source provenance | Keep compact provenance in the bottom footer and page-owned Copy/Open actions in the separate working-surface action group |
| Member Diff | Place the same-member PDB-versus-decompiled viewer beside Source and Annotated Source as a full-area Member working surface; keep mode, change navigation, position, and authorized endpoint Open actions outside its scroller |
| Search and opening | Open Spotlight from a responsive flush-right title-line control immediately after Back/Forward; use a separate local-artifact Open flow |
| Settings | Use one Settings experience with contextual entry points |
| Data bar | Show build identity, acquired source, CLI, and skill links on one line |

Together, these decisions make the web shell read like the CLI without
rendering a command string. The title line progresses from `dotnet-inspect` to
the icon-backed ordered target path, then a responsive Search/history cluster.
The full-width zone below gives subject and inspector navigation one flexible
region. Each group renders its complete owner-ordered full-label tablist when
the measured pair fits and otherwise becomes one current-label Chooser whose
menu exposes the complete inventory. Both tablists use manual activation, and
the constrained form keeps the committed subject and inspector readable
without allocation controls or manually positioned windows. Segment-level copy
remains on the typed title-line target. Package coordinate editing, target
inventories, and other navigation remain inside the working surface rather
than consuming persistent chrome.

Navigation Presentation owns this adaptive pair, its measured fit, widget
semantics, and subject-driven inspector replacement. SlideStrip remains a
separate reusable-control owner pending the focused
[#6277](https://github.com/richlander/dotnet-inspect/issues/6277) retention or
retirement decision under #6158; its windowing contract is no longer part of
the target subject/inspector composition.

The page-level action line keeps working-surface actions distinct from the
Application menu: Source, Annotated Source, and Member Diff supply contextual
groups between the navigation region and the fixed application control.
Surface Composition owns that placement and the transition from legacy direct
application controls without changing any action's semantics or availability.
Shell Interaction owns the Application menu's identity, inventory, and
behavior.

## Cross-document relationships

The six focused owners compose in one direction, from product data to
rendered pixels, with the effect-authority handoff running the other way on
every user action:

1. [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
   renders the subject, hierarchy, Library, and lens descriptors issued by
   Inspection Subject Navigation and the View Facet Registry, using its
   measured Tabs/Chooser pair and the shared visual language from
   [Inspect Web Presentation Language](inspect-web-presentation-language.md)
   for selector pills, progressive disclosure, and heading suppression.
2. A user action submits only an opaque product-issued action ID. Its typed
   result -- semantic outcome, synchronization disposition, and effect
   authority -- is consumed exclusively by
   [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md),
   which installs the returned snapshot, commits canonical location and
   browser history, and resolves focus and announcement under that
   authority. Navigation Presentation never validates authority itself; it
   only renders whatever the consumer installs.
3. [Inspect Web Shell Interaction](inspect-web-shell-interaction.md) owns the
   persistent shell and the modal/routed surfaces it launches (Spotlight,
   Open, Settings, Diagnostics). It hands committed navigation actions to the
   same consumer for focus resolution and history commitment, and it hosts
   the persistent live region and focus anchor the consumer targets.
4. [Inspect Web Surface Composition](inspect-web-surface-composition.md)
   places the working surfaces those other owners render -- Source,
   Annotated Source, Member Diff, Package query, Settings, and Diagnostics --
   into the page layout, deferring their internal behavior to each surface's
   existing focused owner (`package-query-experience.md`,
   `package-query-cli.md`,
   [Annotated Source viewer interaction](annotated-source-viewer-interaction.md),
   [Member source diff presentation](member-source-diff-presentation.md),
   [Inspect Web source-diff transport](inspect-web-source-diff-transport.md),
   the Diff viewer interaction tracked by
   [#5686](https://github.com/richlander/dotnet-inspect/issues/5686), and
   [Browser package sources](browser-package-sources.md)).

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
| Full-label tabs and current-label overflow disclosure | WAI-ARIA tabs and menu-button patterns; Carbon tabs |
| Spotlight, command palette, keyboard navigation, and focus | Visual Studio Code |
| Dense web-native package exploration and shareable state | npmx.dev |
| Assembly, Type, and Member hierarchy | ILSpy and Visual Studio Object Browser |
| Read-only inspection posture and evidence panes | Chrome DevTools |
| URLs, browser history, and familiar web conventions | GitHub |

These references are neither architectural owners nor templates to copy.
WAI-ARIA supplies the conventional Tabs and Menu Button interaction patterns;
Carbon demonstrates full-label tabs with directional overflow controls. Inspect
Web deliberately uses a current-label Chooser instead of per-group scroll
buttons when a complete tablist does not fit. The accepted extra disclosure
keeps both committed identities readable and every hidden choice one menu away.
SlideStrip remains a separately owned reusable control rather than the
subject/inspector model. The CLI correspondence does not turn the title line
into editable command text;
Visual Studio Code does not imply an editor workbench, command center, Activity
Bar, file Explorer, editor tabs, movable regions, or desktop-window
assumptions; and Chrome DevTools does not imply a browser-debugging information
architecture.

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
