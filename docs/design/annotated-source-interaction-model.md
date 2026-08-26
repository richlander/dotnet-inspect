# Annotated Source interaction model

Status: proposed, merge-blocking design for
[PR #4448](https://github.com/richlander/dotnet-inspect/pull/4448).

**Owner:** Annotated Source browser interaction.

This is the owning document for the browser interaction model. It does not
redefine:

- the Finding and annotation semantics in
  [Hidden-Fact Annotations](hidden-fact-annotations.md);
- the evidence coordinates in
  [Annotated Source Finding Provenance](annotated-source-finding-provenance.md);
- the terminal caret layout in [Caret Stacking](caret-stacking.md); or
- the workspace identity and acquisition rules in
  [Inspection Space Architecture](../inspection-space.md).

The browser must not invent a second fact, syntax, or member-identity model to
implement this interaction model.

## Product model

Annotated Source has three surfaces with increasing disclosure:

1. **Embedded reader** — the default member experience. It shows the canonical
   signature, C# source, and the default anchored annotations without the
   explorer's control and inspector chrome. A prominent **Explore** action
   opens the full experience.
2. **Full explorer** — the source, annotation controls, structural controls,
   evidence details, and explicit navigation actions. It is a durable workspace
   view, not a modal whose existence is invisible to navigation and URLs.
3. **Facts** — the structured projection over the same typed Finding
   identities. Bidirectional selection between Facts and Annotated Source is a
   later capability; neither surface may reconstruct or loosely match the
   other's facts.

The embedded reader and full explorer render the same product-owned annotated
document. They differ only in disclosure and interaction state.

### Read, inspect, act

Every gesture belongs to one stage:

- **Read** — see source and default annotations without operating the explorer.
- **Inspect** — select a source node or Finding and reveal its exact details.
- **Act** — choose an explicit destination or copy operation.

A read or inspect gesture never silently performs an action. In particular,
selecting an invocation never chooses between its member overview and source.

## Visual vocabulary

The visual treatment communicates the interaction contract:

- A **chip** is always an actionable `<button>`.
- Inert metadata is a **label**, not a chip, and must not use the chip treatment.
- A **toggle chip** changes visibility and exposes `aria-pressed`.
- A **selection chip** selects or focuses the identity it names.
- An **evidence chip** opens evidence for the Finding it names.
- A **destination action** names where it goes, such as **Member** or
  **Source**. A generic **Navigate** action is prohibited.

Placement and styling must distinguish those verbs. A shared pill shape may
not make toggle, selection, evidence, and navigation actions
indistinguishable.

### Click and keyboard matrix

| Affordance | Default | Activation |
| --- | --- | --- |
| Ordinary source text | Visible | Native text selection only |
| Addressable source span | Visually ordinary | Selects the tightest product-issued node at that span |
| Invocation source span | Visually ordinary | Selects its exact `InvocationExpression`; does not navigate |
| Structural CodeLens chip | Off | Selects and reveals the named structural node |
| Finding annotation chip | On when its anchored Finding is in the default set | Opens that Finding's evidence |
| Finding toggle chip | Full explorer only | Adds or removes that Finding's source annotation |
| Node selection chip | Full explorer only | Selects or focuses that exact node |
| **Member** destination | After selecting an invocation or remote Finding | Opens the target member overview |
| **Source** destination | After selecting an invocation or remote Finding | Opens the target member source |
| Copy action | Where the copied identity is visible | Copies exactly the identity or source named by the action |

Pointer and keyboard activation have identical effects. Dragging a text
selection never activates the source span beneath it.

The selected-node annotation beneath source is descriptive. If it is inert, it
is rendered as plain metadata rather than as a chip. If it is retained as a
selection chip, activating it must focus the corresponding inspector detail;
it never acquires an implicit navigation destination.

## Which annotations appear

### Default

The default preset is deliberately useful without configuration:

- C# is visible.
- The canonical member signature precedes the body.
- Every anchored product Finding in the document is visible, including
  Allocation, Unsafety, Lifetime, and Semantics Findings.
- Allocation annotations are therefore a first-class part of the experience.
  A document known to contain allocation Findings but showing none is a
  projection or presentation defect, not an intentional default.
- Invocation targets remain selectable but receive no persistent underline.
- Unanchored Findings remain discoverable in the full inspector but cannot be
  drawn against invented source coordinates.

### Opt-in

These layers are off by default:

- IL source;
- IL offsets and raw source ranges;
- structural CodeLens annotations;
- node-kind and region overlays;
- captured-variable overlays; and
- exhaustive or diagnostic annotation families added in the future.

A capability catalog, not browser string matching, owns whether a future
annotation family belongs to the default or opt-in set.

Arbitrary syntax nodes do not receive source chips merely because they are
addressable. A node receives a default annotation only when a default-visible
Finding targets it. Structural chips appear only for product-issued structural
candidates while the structural layer is enabled.

## State model

The explorer keeps these concepts separate:

- **Primary selection** — at most one node or Finding owns inspector detail and
  destination actions.
- **Active annotations** — zero or more Finding, structure, or capture
  annotations may remain visible.
- **Evidence peek** — at most one transient evidence surface is open.
- **Presentation** — visible media, offset visibility, and the annotation
  preset.
- **Workspace view** — package, type, member, overload, section, and embedded
  versus full Annotated Source mode.

Multiple annotations may remain active while the latest activation becomes the
primary selection. Implementations must not infer the primary selection from
array order; it is explicit state.

### Default, All, and Clear

The full explorer exposes three annotation presets:

- **Default** restores the default annotation set and clears transient
  selection and evidence state.
- **All** enables every available annotatable layer for the current document.
  It does not change source media or offset visibility.
- **Clear** removes active annotations, selection, and evidence peeks. It does
  not hide the source or reset presentation preferences.

When individual toggles diverge from **Default** or **All**, the UI reports a
custom state. C#/IL and offset controls remain orthogonal to these presets.

## Navigation and durable state

Annotated Source participates in the workspace navigation model rather than
maintaining an unrelated modal history.

- Opening the full explorer records a full-mode workspace view.
- **Member** and **Source** navigation records the current view before moving.
- Back from a destination restores the originating package, type, member,
  overload, Annotated Source section, and full explorer mode.
- Forward restores the destination.
- Local selection, toggles, peeks, and scrolling do not create back-stack
  entries.
- Session history may restore local explorer state and scroll for a previously
  visited entry. It must at minimum restore the full-mode view and its primary
  selection.
- Changing members while Annotated Source is the active section keeps
  Annotated Source active when the destination supports it.

The workspace packet can deep-link to:

- the member's embedded Annotated Source reader; or
- the full explorer for that member.

The packet records the durable presentation mode, not transient popovers or
scroll positions. A direct full-explorer link must open the explorer after the
annotated document loads, rather than briefly rendering the embedded hand-off
and requiring another gesture.

Type-level Annotated Source is future scope. If introduced, the active
Annotated Source mode stays sticky when navigating between types and members
that support it; unsupported destinations fail visibly rather than silently
switching lenses.

## Escape and focus

Escape is handled by the topmost active layer:

1. Close an evidence popover or other transient menu and restore focus to its
   opener.
2. Otherwise exit the full explorer to the embedded reader and restore focus
   to **Explore**.
3. Only when neither is active may the workspace-level Escape behavior run.

A popover must therefore consume its Escape before the explorer or workspace
handler. Opening, closing, back navigation, and forward navigation each restore
focus to a surviving, semantically equivalent control.

## Source presentation

Source text does not use underlines as persistent interaction decoration.
Invocation availability, Finding availability, active selection, capture
scope, and structure must use distinct non-underline treatments such as tint,
gutter marks, weight, or explicit annotation rows. Hover and focus states may
increase contrast but must not introduce a different action.

The explicit caret rows used to connect a visible annotation label to an exact
extent are annotation geometry, not a text-decoration underline. They appear
only for active annotations, not across every addressable source span.

IL offsets and raw source ranges are off by default. A full-explorer control
reveals them together wherever node or Finding coordinates are shown. The
control's label names the coordinate system; unexplained hexadecimal values do
not appear in the default reader.

The same canonical signature shown by Source precedes the annotated body in
both surfaces. The browser uses the product-supplied signature and does not
reconstruct it from rendered source or member display text.

## Facts and allocations

Facts and Annotated Source are two projections over shared typed identities:

- Finding descriptor and instance identity;
- member identity;
- IL evidence coordinate;
- annotated node identity; and
- source span.

When Facts becomes available in the browser, selecting a Fact can open
Annotated Source with that Finding primary and active, and an annotation can
open the matching Facts row. Until then, unavailable Facts remain visibly
unavailable; Annotated Source does not synthesize a substitute table.

Allocation Findings use the same path as every other Finding. They are not a
special browser heuristic and are not inferred from `new`, `box`, or rendered
C#. A compiled fixture with known allocation Findings must gate their presence
in the default embedded reader and the full explorer.

## Current implementation gaps

The implementation is not conformant merely because this document exists.
Before PR #4448 merges, each row must either be implemented or explicitly
accepted as a named follow-up.

| Area | Current behavior | Target |
| --- | --- | --- |
| Embedded reader | A hand-off card hides all annotated source | Signature, C#, and default Findings are visible with a prominent **Explore** action |
| Chip vocabulary | Some pill-shaped labels are inert; actions use several unrelated verbs | Every chip is actionable and every action follows the click matrix |
| Invocation navigation | Explicit **Member**/**Source** exists only after selection | Keep explicit destinations; remove every generic navigation action |
| Defaults | C# and all anchored facts start active; structural CodeLens also starts on | Findings default on; structure, captures, IL, and coordinates opt in |
| Presets | One **clear** action combines several state changes | Separate **Default**, **All**, and **Clear** annotation semantics |
| History | Full mode is local modal state outside `WorkspaceView` | Full mode round-trips through history and workspace packets |
| Escape | Browser popovers and the explorer can compete with workspace Escape | Topmost transient surface closes first |
| Source decoration | Invocation and other states use persistent underlines | No persistent source-text underlines |
| Coordinates | Node chips always show ranges and IL offsets | Coordinates are explicit and off by default |
| Facts | The browser Facts query is visibly unsupported | Preserve shared identities; add bidirectional projection later |
| Allocations | Presence in the experience is not proven | Default visibility is compiler-fixture gated |
| Signature | Annotated Source begins at the body | Reuse the canonical Source signature |

## Validation contract

The interaction model requires:

- a markup/action matrix test proving every chip-shaped element is a button with
  exactly one documented verb;
- default, all, clear, and custom-state tests;
- close negative cases proving opt-in layers are absent by default;
- browser history and workspace-packet round trips for embedded and full mode;
- a real-browser back/forward test after **Member** and **Source** navigation;
- layered Escape and focus-restoration tests;
- a style gate rejecting persistent source-text underlines;
- offset-off and offset-on rendering tests;
- a compiler-produced allocation fixture in the default reader and explorer;
- signature parity with the Source view; and
- identity-preserving Facts integration tests when that capability is enabled.

JavaScript `.click()` alone is insufficient for source affordances. At least one
real-browser gate must use pointer hit testing and keyboard activation for each
action class.
