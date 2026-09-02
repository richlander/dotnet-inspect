# Annotated Source viewer interaction

Status: proposed, merge-blocking design for
[PR #4448](https://github.com/richlander/dotnet-inspect/pull/4448).

**Owner:** interaction inside the embedded Annotated Source reader and the
modal Annotated Source viewer.

This document owns the viewer's disclosure, action vocabulary, selection,
annotation, media, detail, Escape, and focus behavior. It consumes rather than
redefines:

- the modal lifecycle and shell composition in
  [Inspect Web Shell Interaction](inspect-web-shell-interaction.md), and
  browser history and destination-focus rules in
  [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md);
- the annotated document, supported-media set, Finding, target, node, and
  coordinate contracts produced by the product;
- canonical view and packet state from
  [Workspace Definitions](workspace-definitions.md);
- complete C# declaration text from the CSharp layer;
- a future producer-issued Finding census identity from
  [#4986](https://github.com/richlander/dotnet-inspect/issues/4986); and
- future Facts/member projection from
  [#4717](https://github.com/richlander/dotnet-inspect/issues/4717); and
- product-issued invocation destinations from
  [Annotated Source invocation destinations](annotated-source-invocation-destinations.md).

The modal's open state, local selection, annotation choices, presentation
choices, and detail are transient viewer state. This document does not add
them to browser history, workspace definitions, or share packets.

This effort makes one bounded responsibility transfer from
[Inspect Web Shell Interaction](inspect-web-shell-interaction.md):
viewer-local transient layers get the first opportunity to consume Escape in
the Annotated Source modal. The viewer reports whether it consumed the
gesture; Shell Interaction retains modal dismissal, while
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) retains
history composition and destination focus. No other modal receives this
exception from this design.

## Experience

Annotated Source presents the same product-owned document through two levels
of disclosure:

1. The **embedded reader** shows the complete C# declaration, C# body, and
   default visible Finding annotations. A reader can inspect a Finding without
   entering the modal. **Explore** opens the modal viewer.
2. The **modal viewer** adds IL, offsets, annotation controls, source-node
   selection, a persistent inspector, and explicit destination actions. It is
   the full-bleed modal defined by
   [Inspect Web Shell Interaction](inspect-web-shell-interaction.md), not a
   durable workspace lens.

Every gesture belongs to one stage:

- **Read** source and default annotations.
- **Inspect** a source node or Finding and reveal its exact detail.
- **Act** through an explicitly named destination or copy action.

Reading and inspection never silently navigate. Selecting invocation-like
source identifies the product-issued node; it does not guess whether the user
wants **Member** or **Source**.

## Action vocabulary

A chip is always an actionable `<button>`. Inert metadata is a label and must
not use chip styling.

- A **toggle chip** changes membership or visibility and exposes
  `aria-pressed`.
- A **selection chip** selects the exact identity it names.
- A **Finding annotation chip** makes its Finding primary and opens that
  Finding's detail.
- A **Finding inspector action** is the persistent modal opener for a Finding.
  It opens the same detail without changing annotation membership.
- A **destination action** names the destination, such as **Member** or
  **Source**. A generic **Navigate** action is prohibited.
- **Explore** opens the modal. **Close** dismisses it.
- A copy action names what it copies. Copying annotated source copies the
  source document, never annotation labels or inspector chrome.

One pill treatment must not make toggles, selections, Finding detail, and
destinations appear interchangeable.

| Affordance | Embedded | Modal | Activation |
| --- | --- | --- | --- |
| Ordinary source text | Yes | Yes | Native text selection only |
| Addressable source span | No action | Yes | Selects the tightest product-issued node |
| Invocation-like source span | No action | Yes | Selects the tightest invocation-like node; does not navigate |
| Finding annotation chip | Default rendered Findings | Rendered Findings | Makes that Finding primary and opens detail |
| Finding toggle chip | No | Every annotatable Finding | Adds or removes one active annotation |
| Finding inspector action | No | Every Finding | Makes that Finding primary and opens detail |
| Node selection chip | No | Selected/related nodes | Selects or focuses that exact node |
| Medium toggle | No | Each document-supported medium | Shows or hides that medium; rejects hiding the last visible medium |
| Coordinate toggle | No | Product coordinates available | Shows or hides offsets and source ranges |
| Named destination | No | Product capability only | Requests that exact destination |
| **Explore** | Yes | No | Opens a fresh modal session |
| **Close** | No | Yes | Dismisses the modal and any viewer detail |

Pointer and keyboard activation have the same semantic result. Pointer
movement that becomes a text drag remains native text selection and does not
activate the source span beneath it.

Source hit testing uses product-issued spans. Invocation-like candidates take
precedence over generic addressable nodes. Within either candidate set, the
tightest node wins: smallest span containing the activation point, then
smallest total node extent, then lowest product-issued node id. Total extent is
the sum of all span lengths, not the distance between the first and last span.
The product capability catalog owns which node kinds are invocation-like; the
browser does not infer them from text or node-kind strings.

## Viewer state

The viewer keeps these concepts independent:

- **Primary selection:** at most one Finding or node owns inspector detail and
  destination actions.
- **Active annotations:** the annotatable instances selected for display,
  including instances currently hidden by media choices.
- **Rendered annotations:** active instances with at least one target on a
  visible medium.
- **Finding detail:** at most one transient detail surface, bound to one
  Finding and its exact opener.
- **Presentation:** visible C#/IL media and coordinate visibility.
- **Reported annotation state:** **Default**, **All**, **Clear**, or
  **Custom**, derived from the active set.

Implementations must not infer primary selection from active-set ordering.

Activating a Finding annotation chip or inspector action makes that Finding
primary and opens detail. The detail records whether its opener was the
persistent inspector action or one exact annotation target, including the
target's medium. A C# chip and an IL chip for one Finding are different
openers. This selection transition preserves active annotations, visible
media, and coordinate visibility.

Selecting a node makes that node primary, closes Finding detail, and preserves
active annotations, visible media, and coordinate visibility. Annotation
toggles do not make a Finding primary. Removing the primary Finding from the
active set clears primary and detail together; focus remains on the activated
annotation toggle.

### Modal session

Each **Explore** activation starts a fresh modal session:

- the active set is **Default**;
- C# is visible, while IL and coordinates are hidden;
- no node is selected; and
- Finding detail is closed.

The shell chooses initial modal focus from its permitted primary input, current
selection, or heading targets. The viewer requires the chosen target to remain
valid but does not require heading focus. The bounded model represents the
heading and, for an eligible transferred Finding, its persistent inspector
action as the current-selection focus choice; exact shell focus composition
remains outside this viewer owner.

The embedded reader can produce a primary only from a default rendered C# chip,
so every representable embedded primary is eligible to transfer into the fresh
modal. Unanchored, IL-only, and non-default Findings have no embedded action and
cannot become embedded primary. Embedded detail never transfers: the modal has
different controls and therefore cannot truthfully retain an embedded opener.

Modal dismissal destroys modal-local state. It derives the embedded primary
from the modal primary using the same default-and-C# eligibility rule, closes
detail, and leaves the embedded reader at its fixed presentation. A later
**Explore** starts fresh; it does not resurrect the dismissed modal's
annotation, media, coordinate, node, or detail state.

The modal is opened and dismissed through
[Inspect Web Shell Interaction](inspect-web-shell-interaction.md). Those
operations do not push or replace browser-history entries. Ordinary dismissal
returns focus to the stable **Explore** control. Browser Back or Forward first
dismisses the modal and then
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) performs
the history navigation.

## Annotation sets

The annotation universe contains product-issued instances that can be drawn in
at least one supported medium of the current document. It may include anchored
body Findings and product-issued structural or capture annotations. A layer
contributes instances; the layer name is not itself an instance.

Unanchored and member-header Findings remain available through persistent
inspector actions but are not annotation instances. The browser must not
invent coordinates to include them in **Default**, **All**, **Clear**, or
**Custom**.

The modal inspector presents **Selection** and **Findings** as peer sections.
It does not add a second heading that renames the Findings section. With no
primary selection, **Selection** renders a non-action **Nothing selected** tile
in the same content position that selected-node tiles occupy.

Targets on a medium unsupported by the current document do not make a Finding
annotatable and do not produce a toggle. The default set is the
catalog-selected subset of that document-relative universe. Initially:

- C# is the only visible medium;
- default Findings with C# targets render;
- default Findings with targets only on hidden media remain active but do not
  render; and
- structural, capture, exhaustive, and diagnostic layers are off.

Allocation, Unsafety, Cost, Semantics, and Lifetime are current default
Finding families. The browser consumes that catalog; it does not classify
Findings from source text.

The modal exposes:

- **Default**, which restores the default set and clears primary and detail;
- **All**, which activates the entire annotation universe without changing
  primary, detail, media, or coordinate visibility; and
- **Clear**, which empties the active set and clears primary and detail without
  hiding source or resetting presentation.

Each command leaves focus on its activated control. Annotation toggles preserve
media and coordinate visibility. Annotation, media, and coordinate toggles
retain focus on the activated toggle, including when that transition removes a
chip or closes Finding detail. The open modal therefore always retains a
concrete focus target.

The reported state is derived in this precedence order:

1. **Default** when active equals the default set.
2. **All** when active equals the universe.
3. **Clear** when active is empty.
4. **Custom** otherwise.

The precedence makes overlapping cases deterministic: an empty default reports
**Default**, and a default equal to the universe also reports **Default**.
Turning off one member of a multi-instance default produces **Custom**; turning
off the only default instance produces **Clear**.

## Media and coordinates

The product supplies the media that the current document actually contains.
C# is always supported; IL is supported only when the document contains IL
lines. The viewer exposes toggles only for that set and never treats an absent
medium as visible.

C#/IL visibility changes presentation, not annotation membership. Revealing
supported IL may render an already-active IL-only Finding; hiding IL hides that
target without removing the Finding from the active set or changing the
reported annotation state. On a mixed line, spans belonging only to a hidden
medium remain as invisible layout geometry but are neither focusable nor
actionable; visible sibling spans keep their product-issued coordinates.

At least one supported source medium is always visible. Activating the control
for the last visible medium leaves media, annotations, selection, detail, and
coordinates unchanged, with focus on that control. An unsupported medium
cannot satisfy this guard. A document with no available visible medium would
look like a successful empty result.

Hiding a medium does not clear primary or close detail. If it removes the
detail's exact annotation chip, closing detail focuses the same Finding's
persistent inspector action. A sibling chip on another medium is not a
semantically equivalent opener and must not receive focus.

Coordinates are off by default. A modal toggle reveals offsets and source
ranges wherever the product supplies them, including Finding-detail source
offsets, and retains focus when activated. It changes no annotation, medium,
primary, or detail state. Annotation-set and medium controls preserve
coordinate visibility. Dismissal destroys the preference, so a later modal
session starts with coordinates hidden. The toggle's label names the
coordinate system; unexplained hexadecimal values do not appear in the
embedded reader.

## Finding detail and focus

Finding detail always has useful product-issued content. Its descriptor,
category, conditionality, detail, origin, and targets are shown when present.
Unavailable optional evidence appears with its typed reason; it does not turn
the chip inert or produce an empty success-shaped detail.

Every Finding has a persistent modal inspector action even when it is
unanchored, inactive, attached to the member header, or rendered only on a
hidden medium. An annotation chip is an additional spatial opener, never the
only way to inspect a Finding. The inspector-action identity set is exactly the
consumed Finding census; annotation eligibility must not filter it.

Closing detail leaves the current surface, primary selection, active
annotations, media, and coordinate visibility unchanged. It clears only the
transient detail and restores focus to the exact opener if it still exists:

- an inspector-opened detail returns to that Finding's inspector action;
- a chip-opened detail returns to the exact medium-specific chip while that
  chip is still rendered; and
- if that exact chip disappeared, focus returns to the Finding's persistent
  inspector action, even if a same- or different-medium sibling chip remains.

Removing the primary annotation closes detail indirectly and leaves focus on
the annotation toggle that performed the removal. **Default** and **Clear**
leave focus on their own controls. The persistent-inspector fallback applies
when detail itself closes and its recorded opener disappeared. No
detail-closing path may leave focus in removed content or nowhere inside the
open modal.

Escape is layered inside the viewer:

1. Close Finding detail or another viewer-local transient layer and restore
   focus as above.
2. If no transient layer remains and the modal is open, dismiss the modal
   through the shell owner.
3. In the embedded reader with no transient layer, leave viewer state and
   focus unchanged and return Escape unhandled to the workspace.

Embedded viewer-local Escape is eligible only while the embedded reader is the
active workspace surface and no shell overlay owns focus. Opening Spotlight or
another shell overlay leaves embedded detail state intact, but that hidden
detail cannot consume Escape or receive restored focus through the overlay.

Pointer activation of **Close** may dismiss the whole modal even while detail
is open. It is not the keyboard Escape transition. The shell then restores
focus to **Explore**.

Focus is trapped inside the open modal by
[Inspect Web Shell Interaction](inspect-web-shell-interaction.md). Successful
destination navigation closes the modal and lets
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) focus
the destination. Presentation, synchronization, announcements, and focus for
every non-applied navigation outcome remain governed by Navigation Consumer.
Superseded work produces no viewer effect. Addressable source spans carry
stable DOM identities so a shell rerender that preserves the current member
also preserves source focus rather than leaving focus outside the modal.

## Source presentation

Persistent source affordances do not use underlines. Addressable and
invocation-like source remains visually ordinary until hover or keyboard
focus reveals selection. Active selection, Finding annotations, structure, and
captures use distinct non-underline treatments such as tint, gutter marks,
weight, or explicit annotation rows. Hover and focus cannot introduce a
different action from activation.

An explicit chip-style annotation row appears immediately before its targeted
source line and begins at its product-issued source-span start, not at a shared
left edge. It provides CodeLens-like context for the source that follows. The
browser preserves the exact source prefix as layout geometry so annotation
placement follows the language's visible indentation without reconstructing or
changing source text. Annotations with the same start may share a row; distinct
starts remain separately aligned.

Caret rows connecting an annotation label to an exact extent are annotation
geometry, not text-decoration underlines. Unlike chip-style context, they
appear after the extent they annotate and only for rendered active annotations.

The complete declaration preceding the body is product-issued C# text. Work
tracked by [#4852](https://github.com/richlander/dotnet-inspect/issues/4852)
owns making all declarations, including constructor initializers,
representable. The browser transports that text unchanged and does not
reconstruct C# from an API signature, identity, or body.

The portable document is validated before rendering. A rejected document
remains a visible failure at the shell boundary; it does not abort the global
render or become an empty success. A rejected modal remains dismissible and
inside the shell-owned modal focus contract. Dismissal does not require
revalidating the rejected document; when the embedded **Explore** control is
unavailable, focus moves to the embedded rejection heading instead.

## Adjacent integrations

This viewer consumes closed, typed outcomes rather than owning their
construction:

- [#4986](https://github.com/richlander/dotnet-inspect/issues/4986) owns the
  producer-issued census receipt and Finding instance key needed for
  identity-preserving Facts integration.
- [#4717](https://github.com/richlander/dotnet-inspect/issues/4717) owns
  Research composition of member/Facts projections.
- [Annotated Source invocation destinations](annotated-source-invocation-destinations.md)
  owns the Research join from physical calls and C# provenance to typed member
  targets.
- [Workspace Definitions](workspace-definitions.md) owns canonical workspace
  restoration and any future portable view fields, with implementation tracked
  by [#4787](https://github.com/richlander/dotnet-inspect/issues/4787).
- [#4852](https://github.com/richlander/dotnet-inspect/issues/4852) owns
  complete declaration representability.

Until each capability exists, its absence remains visible. This viewer does not
join Facts by descriptors or offsets, synthesize C#, mint packet fields, infer
targets from display text, or implement a second asynchronous navigation
authority.

## Executable interaction model

[`ViewerSession`](models/annotated-source-viewer/ViewerSession.tla) is the
bounded executable design model for viewer-local interaction. It checks:

- fresh modal initialization and eligible primary transfer;
- modal dismissal and exercised embedded-primary eligibility derivation;
- exact chip versus persistent-inspector detail openers, including distinct
  same-medium targets for one Finding and embedded-chip activation;
- exact enabled action sets for embedded and modal annotation chips,
  persistent inspector actions, annotatable-Finding toggles, and
  supported-medium toggles, plus exact availability of selectable nodes and
  every modeled fixed action, including an unanchored inspector witness and
  pointer **Close** while detail is open, all checked against the executable
  model's complete transition relation;
- exact rendered-target derivation from active annotations and visible media;
- distinct Finding and non-Finding annotation identities, with structural
  annotations excluded from defaults but included by **All** and rendered from
  their own targets;
- primary selection, active annotations, rendered annotations, and derived
  reported state with independently checked precedence;
- exact **Default**, **All**, **Clear**, medium, and coordinate control
  outcomes, including preserved orthogonal state;
- document-supported C#/IL visibility with at least one available medium and
  a document-relative annotation universe, plus coordinate visibility;
- layered Escape on both surfaces and pointer dismissal;
- exact viewer-state preservation when embedded Escape falls through;
- destruction of embedded detail on modal opening;
- exact focus and state preservation after direct or indirect detail closure;
  and
- annotation and presentation preservation across Finding and node selection.

The model deliberately omits shell history, modal stacking outside this
viewer, asynchronous navigation authority, packet state, declaration
construction, Finding census construction, and document production. Its
finite bounds, TLC result, mutation evidence, and non-claims are recorded in
the [model README](models/annotated-source-viewer/README.md).

## Validation contract

Conformance requires:

- TLC success and nonzero action coverage for the checked-in model, plus the
  documented mutation counterexamples;
- action-matrix tests proving every chip-shaped element is a button with one
  documented verb and equivalent pointer/keyboard activation, plus exact set
  equality for enabled embedded and modal annotation chips, persistent
  inspector actions, annotatable-Finding toggles, and supported-medium toggles,
  plus exact availability of selectable nodes and fixed controls, with an
  unanchored inspector witness and pointer **Close** exercised while detail is
  open;
- modal-session tests proving fresh initialization, eligible primary transfer,
  independently derived transfer eligibility, embedded-detail destruction on
  opening, shell-permitted heading or current-selection focus, state
  destruction on dismissal, and no detail transfer;
- active-versus-rendered tests covering C#-only, IL-only, dual-target, and
  unanchored Findings plus a non-Finding structural annotation, with rendered
  targets derived directly from owning-annotation membership and visible media;
- **Default**, **All**, **Clear**, and **Custom** precedence tests, including
  empty and universe-equal defaults plus rejection of unsupported-medium
  Findings from the universe and default set, exclusion of structural or
  capture annotations from defaults, and their inclusion by **All**;
- media tests proving controls come only from the product-supported set,
  unsupported media cannot satisfy the non-empty guard, membership is
  orthogonal, a hidden opener falls back to the exact Finding's inspector
  action, a same- or different-medium sibling chip is not substituted, toggles
  retain focus, mixed-line hidden segments remain as inert layout geometry,
  and the final visible medium cannot be disabled;
- coordinate tests proving hidden fresh state, exact toggling and focus,
  annotation-set and media preservation, dismissal destruction, and hidden
  state on reopening, including Finding-detail source offsets;
- primary tests proving Finding and node transitions are explicit and toggles
  do not select;
- detail-open and close tests proving exact opener identity, including two
  same-medium targets and embedded-chip activation, historical eligible-primary
  transfer on modal opening, and preservation of surface, primary selection,
  annotation membership, media, and coordinates on direct close;
- detail-content tests using valid product-issued fixtures, proving chip and
  persistent-inspector paths render the same non-empty content, preserve every
  present descriptor, category, conditionality, detail, origin, and target, and
  show the typed reason for each unavailable optional value rather than an
  empty success-shaped panel;
- Finding- and node-selection tests beginning from non-default annotation,
  media, and coordinate state and proving those orthogonal states are
  preserved;
- layered Escape tests distinguishing detail closure, modal dismissal, and
  embedded fall-through, with an independent before/after oracle for every
  viewer-owned state field and focus;
- focus tests for direct close, annotation-set controls, annotation, media, and
  coordinate toggles, pointer dismissal, rejected navigation, and successful
  destination handoff;
- hit tests covering pointer coordinates, keyboard activation, invocation
  precedence, discontinuous spans, deterministic tightest-node selection, and
  drag-selection non-activation;
- source-copy tests proving annotations and chrome are excluded;
- replacement-render tests proving stable addressable-source focus, plus shell
  containment tests proving rejected documents remain visible, dismissible
  without revalidation, and focused at the embedded rejection after dismissal;
- a style gate rejecting persistent source-text underlines; and
- a CI-integrated real-browser gate for pointer hit testing, focus, Escape,
  modal trapping, backdrop dismissal, and drag selection.

Node-only unit tests and synthetic `.click()` calls are insufficient evidence
for browser geometry, native text selection, or focus restoration.
