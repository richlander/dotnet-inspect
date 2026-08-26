# Annotated Source interaction model

Status: proposed, merge-blocking design for
[PR #4448](https://github.com/richlander/dotnet-inspect/pull/4448).

**Owner:** Annotated Source browser interaction.

This is the owning document for the browser interaction model. It does not
redefine:

- the Finding and annotation semantics in
  [Hidden-Fact Annotations](hidden-fact-annotations.md);
- the evidence-coordinate semantics in
  [Finding Coordinates](finding-coordinates.md);
- the terminal caret layout in [Caret Stacking](caret-stacking.md); or
- the workspace identity and acquisition rules in
  [Inspection Space Architecture](../inspection-space.md).

The browser must not invent a second fact, syntax, or member-identity model to
implement this interaction model.

## Product model

Annotated Source has three surfaces with increasing disclosure:

1. **Embedded reader** — the default member experience. It shows the
   product-issued body-aware C# declaration, C# source, and the default anchored
   annotations without the explorer's persistent controls and inspector
   chrome. Finding annotation chips and their transient Finding detail remain
   available. A prominent **Explore** action opens the full experience.
2. **Full explorer** — the source, annotation controls, structural controls,
   source-node selection, persistent Finding detail, and explicit navigation
   actions. It is a durable workspace view, not a modal whose existence is
   invisible to navigation and URLs.
3. **Facts** — the structured Finding projection. Bidirectional selection
   between Facts and Annotated Source is a later capability that first requires
   the shared typed Finding identity defined below; neither surface may
   reconstruct or loosely match the other's facts.

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
- A **Finding annotation chip** makes its Finding primary and opens the
  product-issued Finding detail as one inspect transition. Relationship
  evidence appears within that detail when the owner provides it.
- A **destination action** names where it goes, such as **Member** or
  **Source**. A generic **Navigate** action is prohibited.

Placement and styling must distinguish those verbs. A shared pill shape may
not make toggle, selection, Finding-detail, and navigation actions
indistinguishable.

### Click and keyboard matrix

| Affordance | Surface | Availability | Activation |
| --- | --- | --- | --- |
| Ordinary source text | Both | Visible | Native text selection only |
| Addressable source span | Full | Visually ordinary | Selects the tightest product-issued node at that span |
| Invocation-like source span | Full | Visually ordinary | Selects the tightest product-issued invocation-like node; does not navigate |
| Structural CodeLens chip | Full | Off by default | Selects and reveals the named structural node |
| Finding annotation chip | Both | Its anchored Finding is active | Makes the Finding primary and opens its Finding detail |
| Finding toggle chip | Full | Inspector control | Adds or removes that Finding's source annotation |
| Node selection chip | Full | Inspector control | Selects or focuses that exact node |
| **Member** destination | Full | Primary has one actionable member target | Opens that target's member overview |
| **Source** destination | Full | Primary has one actionable source target | Opens that target's member source |
| **Explore** | Embedded | The annotated document is loaded | Replaces the current entry with full mode using the state transition below |
| **Exit** | Full | Visible | Closes transient detail and replaces the current entry with embedded mode |
| Copy action | Both | Copied identity is visible | Copies exactly the identity or source named by the action |

Pointer and keyboard activation have identical effects. Dragging a text
selection never activates the source span beneath it.

Invocation hit testing takes precedence over generic addressable-node hit
testing. The product capability catalog, not browser string matching, owns the
invocation-like set. Its initial entries are `InvocationExpression`,
`IndirectInvocationExpression`, and `ObjectCreationExpression`. When multiple
invocation-like nodes contain the activation point, the tightest wins:
smallest span containing the activation point, then smallest total node
extent, then lowest product-issued node id. Total node extent is the sum of
the lengths of all its spans, not the length of their bounding range.
Enclosing invocation-like nodes and nested generic nodes remain selectable
through explicit structural controls. Outside an invocation-like node, source
activation uses the same ordering over all addressable nodes.

Research owns semantic target resolution; the workspace query layer owns
acquisition, residency, and identity validation.
`AnnotatedMemberDocumentQuery`, and any assembly-context projection of the
same contract, binds the semantic result to the annotated document's
product-issued node or fact id. The workspace layer then emits one closed
selection result for the browser:

- **Actionable** carries one or both typed capabilities. **Member** carries an
  acquisition-qualified `InspectionGraphMemberIdentity.AcquiredApi`.
  **Source** carries that identity plus an exact
  `AssemblyMemberSourceRequest`, including its physical MethodDef token. A
  missing capability carries a typed reason.
- **Unresolved** means no product-issued call occurrence or member target
  joined to the primary selection.
- **Ambiguous** means one occurrence resolved to more than one candidate.
- **Aggregate** means the primary selection deliberately represents multiple
  distinct occurrences, so it has no single destination.
- **Unavailable** means exactly one semantic target was resolved but
  acquisition, residency, identity-skew validation, or policy rejection
  prevented every destination capability. It carries the typed reason.

The browser transports that result and must not derive it from display text,
IL offsets, node kinds, candidate ordering, or ambient assembly guesses.
Destination actions appear only for the capabilities carried by
**Actionable**. Every missing capability and non-actionable result appears as
inert explanatory metadata, not an invented target or a disabled
success-shaped action.

Finding detail is likewise product-issued and bound to the selected fact id.
The fact's descriptor, category, conditionality, detail, origin, and targets
always form useful detail. Optional relationship evidence is either available
with its typed payload or unavailable with a reason. A Finding annotation chip
therefore never becomes inert and never opens an empty success-shaped peek:
when optional evidence is absent, its detail reports that absence as an inert
label.

The selected-node annotation beneath source is descriptive. If it is inert, it
is rendered as plain metadata rather than as a chip. If it is retained as a
selection chip, activating it must focus the corresponding inspector detail;
it never acquires an implicit navigation destination.

## Which annotations appear

### Initial presentation

The initial presentation is deliberately useful without configuration:

- C# is visible.
- The product-issued body-aware C# declaration precedes the body.
- Every default-visible anchored product Finding with a target on a visible
  medium is visible. The current default catalog includes Allocation,
  Unsafety, Cost, Semantics, and Lifetime Findings.
- Allocation annotations are therefore a first-class part of the experience.
  A default-visible allocation Finding with a C# target but no annotation in
  the initial C# presentation is a projection or presentation defect.
- In the full explorer, invocation-like targets remain selectable. In both
  surfaces they receive no persistent underline.
- A default-visible Finding whose targets are all on hidden media remains
  active but receives no source annotation until one of its target media is
  visible.
- Unanchored Findings remain discoverable in the full inspector but cannot be
  drawn against invented source coordinates.

### Opt-in presentation

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

Both Annotated Source surfaces keep these concepts separate:

- **Primary selection** — at most one node or Finding owns inspector detail and
  destination actions.
- **Active annotations** — zero or more Finding, structure, or capture
  annotations are selected for rendering; presentation may hide an instance
  that has no target on a visible medium.
- **Finding detail** — at most one transient Finding detail surface is open.
- **Presentation** — visible media and offset visibility.
- **Reported annotation state** — Default, All, Clear, or Custom, derived from
  the active annotation set.
- **Workspace view** — package, type, member, overload, section, and embedded
  versus full Annotated Source mode.

Activating a Finding annotation chip makes its Finding primary and opens its
detail. Activating a node selection makes that node primary and closes Finding
detail. Visibility toggles do not change primary selection unless they remove
the primary Finding; that transition clears both the primary selection and its
detail.

Multiple annotations may remain active while one explicit primary selection
owns detail. Implementations must not infer it from active-annotation array
order.

Embedded and full mode have separate local interaction state within one
workspace entry:

- The embedded reader always uses the default annotation-instance set, C#, and
  no offsets. It renders that set through the visible-medium rule below. It may
  retain one default-visible anchored Finding as primary and may show that
  Finding's transient detail.
- The full explorer retains its annotation-instance set, primary selection,
  Finding detail, media, and coordinate preferences while the workspace entry
  remains alive, including while that entry is in embedded mode.
- **Explore** restores retained full state. On first use, it initializes the
  default annotation-instance set, C# visible, IL and offsets hidden, and no
  node selection. An embedded primary Finding and its open detail carry into
  that initial full state.
- **Exit**, or Escape when no transient layer is open, leaves retained full
  state unchanged and derives a new embedded primary from the full primary. A
  default-visible anchored Finding with a C# target transfers as primary; a
  node, unanchored Finding, non-default Finding, or Finding without a C# target
  clears the embedded primary. Previously retained embedded primary state is
  not restored. Embedded Finding detail always closes, and focus returns to
  **Explore**. Full-only state remains retained for a later **Explore**.
- A direct full-mode link has no retained state to restore. It starts with the
  default annotation-instance set, C# visible, IL and offsets hidden, and no
  primary selection or transient detail.

### Default, All, and Clear

The available annotation universe contains the product-issued instances that
can be drawn in at least one supported medium of the current document:

- anchored body Finding ids with at least one
  `AnnotatedSourceTarget`;
- structural-candidate ids; and
- capture ids.

Unanchored and member-header Findings remain available as inspector detail but
are not annotation instances and never participate in Default, All, Clear, or
Custom. A layer contributes its available instances; the layer name is not
itself a member of the universe. The default annotation set is the
catalog-selected subset of that universe. Its current default Finding families
are Allocation, Unsafety, Cost, Semantics, and Lifetime. The active, or
effective, annotation set is the current subset selected for display.

The rendered annotation set is the active instances that have at least one
target on a currently visible medium. Changing C#/IL presentation can therefore
reveal or hide an annotation without adding or removing its active instance or
changing the reported annotation state.

The full explorer exposes three annotation-set commands:

- **Default** restores the default annotation-instance set and clears transient
  selection and Finding detail.
- **All** enables every available annotation instance from every annotatable
  layer for the current document. It does not change source media or offset
  visibility.
- **Clear** removes active annotations, selection, and Finding detail. It does
  not hide the source or reset presentation preferences.

The reported annotation state is derived from the active instance set, not the
last command. **Default** wins when the active set equals the default set.
Otherwise, **All** applies when it equals the available universe, **Clear**
applies when it is empty, and **Custom** applies to every other set. Toggling
one default Finding off therefore produces **Custom** even though the Finding
layer remains available, provided another active instance remains. If it was
the only active default instance, the resulting empty set reports **Clear**.
This precedence handles documents whose Default, All, or empty sets overlap.
C#/IL and offset controls remain orthogonal to these commands and states.

## Navigation and durable state

Annotated Source participates in the workspace navigation model rather than
maintaining an unrelated modal history.

- **Explore** replaces the current history entry with the full-mode workspace
  view; it does not push an entry.
- **Exit**, and Escape when no transient layer is open, replace that entry with
  the embedded-mode workspace view. This also applies to a direct full-mode
  link, including when it is the first history entry.
- **Member** and **Source** navigation record the current view before moving.
- Back from a destination restores the originating package, type, member,
  overload, Annotated Source section, and full explorer mode.
- Forward restores the destination.
- Local selection, toggles, Finding detail, and scrolling do not create
  back-stack entries.
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

1. Close Finding detail or another transient menu and restore focus to its
   opener.
2. Otherwise perform the same transition as **Exit** and restore focus to
   **Explore**.
3. Only when neither is active may the workspace-level Escape behavior run.

A popover must therefore consume its Escape before the explorer or workspace
handler. Pointer activation of **Exit** closes any transient Finding detail and
exits in one transition. Opening, closing, back navigation, and forward
navigation each restore focus to a surviving, semantically equivalent control.

## Source presentation

Source text does not use underlines as persistent interaction decoration.
Addressable-node and invocation-like availability remains visually ordinary.
Hover and keyboard focus may reveal the available selection action. Active
selection, visible Finding annotations, capture scope, and structure use
distinct non-underline treatments such as tint, gutter marks, weight, or
explicit annotation rows. Hover and focus states must not introduce a
different action.

The explicit caret rows used to connect a visible annotation label to an exact
extent are annotation geometry, not a text-decoration underline. They appear
only for active annotations, not across every addressable source span.

IL offsets and raw source ranges are off by default. A full-explorer control
reveals them together wherever node or Finding coordinates are shown. The
control's label names the coordinate system; unexplained hexadecimal values do
not appear in the default reader.

The CSharp-owned body-aware declaration produced from the same
`CSharpMemberBody` as the annotated body precedes that body in both surfaces.
The declaration writer used by the `CSharpFormatter.FormatMemberWithBody` path
owns its modifiers, escaping, finalizer spelling, and body-dependent
`async`/`unsafe` decisions. The Research annotated-member projection carries
that declaration in a dedicated field and the browser transports it with the
annotated document.

`ApiMember.Signature` and `BrowserMemberSurface.Signature` remain API-list
skeleton spellings; `MemberAnchor.CanonicalSignature` remains identity and copy
data. Neither is the Annotated Source declaration, and the browser never
re-derives that declaration by parsing source or another display string.

## Facts and allocations

Facts and Annotated Source are intended to become two projections over shared
typed identities:

- Finding descriptor and instance identity;
- member identity;
- IL evidence coordinate;
- annotated node identity; and
- source span.

`AnnotatedSourceFact.Id` is local to one annotated document, and the current
annotated projection does not carry a cross-projection instance key. Before
Facts integration, the product must seal the immutable Finding census and issue
an opaque typed `FindingCensusReceipt` plus a producer-issued
`FindingInstanceKey` that is injective within that receipt. Both projections
carry the same receipt and instance key unchanged; only the pair identifies an
instance. Independently executed projections may link only when their receipts
match.

`FindingSubject.Key` identifies the subject and `FindingKey.IdentityKey`
identifies a correspondence candidate; neither, alone or together, identifies
one Finding instance. Document-local ids, descriptors, offsets, ordinals,
rendered-field tuples, and equal instance keys from different census receipts
are likewise not cross-projection identity.

When that identity and Facts become available in the browser, selecting a Fact
can open Annotated Source with that Finding primary and active, and an
annotation can open the matching Facts row. Until then, unavailable Facts
remain visibly unavailable; Annotated Source does not synthesize a substitute
table.

Allocation Findings use the same path as every other Finding. They are not a
special browser heuristic and are not inferred from `new`, `box`, or rendered
C#. A compiled fixture with a known C#-targeted allocation Finding gates its
presence in the initial embedded reader and the full explorer. A separate
IL-only fixture gates active-but-hidden behavior while C# alone is visible and
appearance when IL is shown. A close-negative fixture with allocation-like
syntax but no product-issued allocation Finding gates its absence.

## Current implementation gaps

The implementation is not conformant merely because this document exists.
Before PR #4448 merges, each row must either be implemented or explicitly
accepted as a named follow-up.

| Area | Current behavior | Target |
| --- | --- | --- |
| Embedded reader | A hand-off card hides all annotated source | Signature, C#, default Finding annotation chips, transient Finding detail, and a prominent **Explore** action |
| Chip vocabulary | Some pill-shaped labels are inert; actions use several unrelated verbs | Every chip is actionable and every action follows the click matrix |
| Invocation navigation | Explicit **Member**/**Source** exists only after selection | Research and workspace resolution emit the closed, acquisition-qualified selection result |
| Finding primary state | Evidence and annotation arrays can imply selection indirectly | Finding activation establishes one explicit primary Finding and useful detail; toggles do not |
| Initial presentation | C# and all anchored facts start active; structural CodeLens also starts on | Default Findings are active; only visible-medium targets render; structure, captures, IL, and coordinates start off |
| Annotation commands | One **clear** action combines several state changes | Separate **Default**, **All**, and **Clear** commands with derived-state precedence |
| History | Full mode is local modal state outside `WorkspaceView` | Explore/Exit/Escape replace the current entry, preserve the defined surface-local state, and destinations round-trip the originating full view |
| Escape | Browser popovers and the explorer can compete with workspace Escape | Topmost transient surface closes first; Exit follows the same mode transition |
| Source decoration | Invocation and other states use persistent underlines | Availability is ordinary; active states use distinct non-underline treatments |
| Coordinates | Node chips always show ranges and IL offsets | Coordinates are explicit and off by default |
| Facts | The browser Facts query is visibly unsupported and annotated facts carry only document-local ids | Add one sealed census receipt and shared Finding instance key before bidirectional projection |
| Allocations | Presence and provenance in the experience are not proven | Product-issued presence and syntax-only absence are compiler-fixture gated |
| Declaration | Annotated Source begins at the body | Carry the CSharp-owned body-aware declaration with the annotated-member projection |
| Real-browser conformance | Tests use Node and a fake DOM; no CI-integrated browser runner exists | Add a CI-integrated real-browser harness for pointer, keyboard, history, focus, and drag-selection gates |

## Validation contract

The interaction model requires:

- surface-specific markup/action matrix tests proving every chip-shaped element
  is a button with exactly one documented verb;
- embedded-reader tests proving Finding detail is available while
  source-node selection and destination actions are absent;
- invocation hit tests proving the cataloged invocation-like kinds, the
  tightest-node tie-break, discontinuous-span total extent, and precedence over
  nested generic nodes;
- destination-action tests for Member-only, Source-only, and combined
  actionable capabilities, plus unresolved, ambiguous, aggregate, and
  unavailable-with-reason close negatives;
- Finding-detail tests proving unavailable optional evidence produces useful
  detail and an inert explanatory label rather than an inert chip or empty
  peek;
- primary-state tests with multiple active annotations proving Finding and node
  selection transitions are explicit and toggles are independent;
- Default, All, Clear, and Custom derived-state tests, including overlapping
  effective-set precedence, a one-Finding-off **Custom** case with a non-empty
  residual set, and a singleton-default **Clear** case;
- close negative cases proving opt-in layers and anchored opt-in Findings are
  absent by default;
- browser history and workspace-packet round trips for embedded and full mode,
  including Explore/Exit/Escape replacement from the first history entry,
  retained Clear and Custom full state, direct-full default state, and
  deterministic Exit transfer with a different retained embedded primary;
- a real-browser back/forward test after **Member** and **Source** navigation;
- layered Escape and focus-restoration tests;
- a real-browser drag-selection negative proving pointer movement selects text
  without activating its source span;
- a style gate rejecting persistent source-text underlines;
- offset-off and offset-on rendering tests;
- a compiler-produced allocation Finding with a C# target in both surfaces, an
  IL-only anchored Finding that remains active while hidden in C# and appears
  when IL is shown, and allocation-like syntax with no Finding in either
  surface;
- declaration parity with the CSharp body-aware formatter for escaped names,
  finalizers, and body-dependent modifiers; and
- identity-preserving Facts integration tests, including wrong-census receipt
  negatives, when that capability is enabled.

JavaScript `.click()` alone is insufficient for source affordances. At least one
real-browser gate must use pointer hit testing and keyboard activation for each
action class.
