# Annotated Source viewer model

[`ViewerSession.tla`](ViewerSession.tla) is the bounded executable design for
interaction inside one embedded Annotated Source reader and one modal viewer.
The prose owner is
[Annotated Source viewer interaction](../../annotated-source-viewer-interaction.md).

## Scope and assumptions

The model assumes:

- one loaded, immutable annotated document;
- finite product-issued annotation, Finding, target, node, and
  default-annotation censuses;
- an immutable product-issued supported-media set containing C# and optionally
  IL;
- every target belongs to exactly one annotation;
- every Finding target belongs to exactly one Finding;
- C# and IL targets are disjoint;
- one Finding has two distinct C# targets plus an IL target, one is IL-only,
  and one is unanchored;
- one non-Finding structural annotation has one IL target, is initially off,
  and participates in **All** without gaining Finding detail or inspector
  actions;
- user gestures are atomic; and
- shell open, dismissal, and focus handoff occur as one atomic boundary event.

The model explores every subset of the two annotatable Findings as the default
set when both media are supported, and every subset of the C#-annotatable
Finding when only C# is supported. Its finite target identities span both
document configurations; the immutable supported-media set derives each
document's complete annotation universe. The configuration contains three
Findings, one structural annotation, five targets, two supported-media sets,
two media, and one selectable node. These bounds exercise empty, singleton,
all-equal, C#-only documents, optional IL, two same-medium targets for one
Finding, IL-only Findings, dual-media targets, unanchored Findings, and a
non-Finding annotation that distinguishes **Default** from **All** in the
dual-media document, without claiming that production cardinality is bounded.

## Checked behavior

The safety invariants check:

- state and record types;
- legal primary and detail shapes;
- the embedded reader's default-and-C# selection boundary;
- destruction of embedded detail on modal opening;
- absence of modal detail after dismissal;
- at least one document-supported visible medium;
- exact derivation of the annotation universe and defaults from supported
  media, including a non-Finding structural annotation that is excluded from
  defaults;
- independent precedence checks for exact derivation of **Default**, **All**,
  **Clear**, and **Custom**;
- a concrete valid focus target throughout an open modal;
- exact embedded and modal chip-or-inspector opening, including the exact
  same-medium target, plus historical eligible-primary transfer;
- exact enabled-action-set equality for embedded and modal annotation chips,
  persistent inspector actions, annotatable-Finding toggles, and
  supported-medium toggles, plus exact availability of selectable nodes and
  every modeled fixed action, including an unanchored inspector witness and
  pointer **Close** while detail is open, all witnessed through the actual
  `Next` transition relation and the transition's recorded action identity;
- exact rendered-target derivation from active membership and currently
  visible media using each target's owning annotation rather than assuming
  every target owns Finding behavior;
- exact annotation and presentation preservation across modal Finding and node
  selection;
- exact detail closure, primary and presentation preservation, and
  chip-or-inspector focus restoration without changing surface;
- stable control focus plus exact annotation membership, primary, and detail
  outcomes and presentation preservation derived from pre-toggle state;
- exact **Default**, **All**, **Clear**, medium, and coordinate outcomes
  derived from pre-control state, including orthogonal-state preservation and
  final-medium rejection;
- fresh modal initialization and transfer of a representable embedded primary;
- shell-permitted modal-opening focus at the heading or, for a transferred
  Finding, its persistent inspector as the modeled current-selection target;
- exact dismissal, embedded-primary derivation, and **Explore** focus; and
- the rule that Escape cannot bypass Finding detail on either surface, plus
  exact viewer-state and focus preservation when embedded Escape falls
  through.

The embedded reader can produce primary state only from a default rendered C#
chip. Its ineligible-primary rejection branch is therefore structural, not a
reachable transition: `EmbeddedStateIsConstrained` makes an unanchored,
IL-only, or non-default embedded primary unrepresentable. Dismissal from the
modal exercises both eligible and ineligible primary derivation.

`Next` includes embedded chip inspection, modal opening, pointer and Escape
dismissal, chip and inspector Finding detail, node selection, detail closure,
embedded Escape fall-through, **Default**, **All**, **Clear**, annotation
toggle, media toggle, and coordinate toggle. The action-coverage run reached
every action. `CloseCurrentDetail` abstracts the identical viewer-state outcome
of the detail close control and detail-level Escape; the distinct Escape
actions model only modal dismissal and embedded fall-through after detail is
absent.

The shell permits primary-input, current-selection, or heading focus when it
opens a modal. Primary-input composition is not otherwise part of this bounded
viewer state, so `OpenModal` explores heading focus and the persistent
inspector action for an eligible transferred Finding. A temporary reachability
canary that prohibited that current-selection choice failed after 41 generated
states, demonstrating that the model does not pin opening focus to the heading.

This is a safety model only. It makes no liveness claim: users may stop after
any gesture, ignored input is admitted as stuttering, and asynchronous
navigation progress belongs to another owner.

## Running TLC

With `tla2tools.jar` available:

```bash
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup \
  -config docs/design/models/annotated-source-viewer/ViewerSession.cfg \
  docs/design/models/annotated-source-viewer/ViewerSession.tla

java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup -coverage 1 \
  -config docs/design/models/annotated-source-viewer/ViewerSession.cfg \
  docs/design/models/annotated-source-viewer/ViewerSession.tla
```

TLC 1.8.0, build `2026.08.21.155922`, revision `9787e65`, completed with no
error. The checked configuration registers 29 safety invariants:

| Result | Value |
| --- | ---: |
| Generated states | 825,314 |
| Distinct states | 53,760 |
| Search depth | 13 |

The coverage run reported nonzero distinct transitions for every top-level
action. The smallest count was 12 for `OpenEmbeddedChip`; representative
transition counts were 24 for `OpenModal`, 60 each for the two dismissal
actions, 2,592 for `OpenModalFinding`, 2,988 for `CloseCurrentDetail`, 11,736
each for `ToggleAnnotation` and `ToggleMedium`, and 5,976 for
`ToggleCoordinates`.

## Mutation evidence

Sixty deliberate targeted mutations were run against the same
configuration.
Each produced a concrete counterexample:

| Mutation | Violated invariant |
| --- | --- |
| Permit disabling the final visible medium | `AtLeastOneMediumIsVisible` |
| Open the modal with all media instead of C# only | `ModalOpeningIsFresh` |
| Let Escape dismiss the modal while detail is open | `EscapeCannotBypassDetail` |
| Restore focus to a sibling medium's chip | `FocusIsValid` |
| Drop focus after an annotation toggle | `ModalAlwaysHasFocus` |
| Drop focus after the **Default** command | `ModalAlwaysHasFocus` |
| Preserve stale reported state after an annotation toggle | `ReportedStateIsDerived` |
| Preserve Finding detail through modal dismissal | `DetailShapes` |
| Preserve a stale embedded primary through dismissal | `ModalDismissalIsExact` |
| Clear an unrelated primary while toggling another Finding | `DetailShapes` |
| Make annotation membership toggle a no-op | `AnnotationToggleOutcomeIsExact` |
| Make **Default** membership a no-op | `ControlOutcomeIsExact` |
| Make **All** membership a no-op | `ControlOutcomeIsExact` |
| Omit the structural annotation from **All** | `ControlOutcomeIsExact` |
| Let **Clear** preserve primary and detail | `ControlOutcomeIsExact` |
| Let **All** reset visible media | `ControlOutcomeIsExact` |
| Focus **Clear** after activating **Default** | `ControlOutcomeIsExact` |
| Make a medium toggle a no-op | `ControlOutcomeIsExact` |
| Let a medium toggle close Finding detail | `ControlOutcomeIsExact` |
| Focus the modal heading after a medium toggle | `ControlOutcomeIsExact` |
| Make the coordinate toggle a no-op | `ControlOutcomeIsExact` |
| Let **All** reset coordinate visibility | `ControlOutcomeIsExact` |
| Focus the modal heading after a coordinate toggle | `ControlOutcomeIsExact` |
| Substitute unsupported IL for the final supported medium | `TypeOK` |
| Record inspector detail for a chip opener | `FindingOpeningIsExact` |
| Record a sibling C# target for a chip opener | `FindingOpeningIsExact` |
| Preserve embedded detail through modal opening | `EmbeddedDetailExistsOnlyWhileEmbedded` |
| Swap **Default** and **All** precedence | `ReportedStateIsDerived` |
| Treat a hidden chip as an available opener | `FocusIsValid` |
| Let embedded Escape fall through while detail is open | `EscapeCannotBypassDetail` |
| Clear primary selection and focus while embedded Escape falls through | `EmbeddedEscapeOutcomeIsExact` |
| Clear primary selection when directly closing detail | `DetailClosureOutcomeIsExact` |
| Include an unsupported-medium Finding in the annotation universe | `AnnotatableUniverseIsSupported` |
| Restrict the annotation universe to Findings | `AnnotatableUniverseIsSupported` |
| Record a sibling C# target for an embedded chip opener | `FindingOpeningIsExact` |
| Clear eligible primary state while opening the modal | `ModalOpeningIsFresh` |
| Make transfer eligibility reject every primary | `ModalOpeningIsFresh` |
| Change surface while directly closing detail | `DetailClosureOutcomeIsExact` |
| Reset visible media during an annotation toggle | `AnnotationToggleOutcomeIsExact` |
| Reset coordinates while opening modal Finding detail | `FindingOpeningIsExact` |
| Reset coordinates while selecting a node | `NodeSelectionOutcomeIsExact` |
| Restrict the inspector action guard to annotatable Findings | `InspectorActionsAreAvailable` |
| Make rendered-target derivation ignore visible media | `RenderedTargetsAreExact` |
| Remove one default rendered embedded chip action | `EmbeddedChipActionsAreExact` |
| Remove one rendered modal chip action | `ModalChipActionsAreExact` |
| Allow a hidden modal chip action | `ModalChipActionsAreExact` |
| Remove one annotatable-Finding toggle action | `AnnotationToggleActionsAreExact` |
| Remove one supported-medium toggle action | `MediumToggleActionsAreExact` |
| Disable **Explore** while embedded detail is open | `FixedActionAvailabilityIsExact` |
| Disable pointer **Close** while modal detail is open | `FixedActionAvailabilityIsExact` |
| Disable modal Escape dismissal for one detail-free state | `FixedActionAvailabilityIsExact` |
| Disable direct detail closure on the embedded surface | `FixedActionAvailabilityIsExact` |
| Disable embedded Escape fall-through for one detail-free state | `FixedActionAvailabilityIsExact` |
| Disable **Default** when the active set is already default | `FixedActionAvailabilityIsExact` |
| Disable **All** when every annotation is already active | `FixedActionAvailabilityIsExact` |
| Disable **Clear** when the active set is already empty | `FixedActionAvailabilityIsExact` |
| Disable hiding coordinates after they are visible | `FixedActionAvailabilityIsExact` |
| Remove one selectable-node action | `NodeActionsAreExact` |
| Restrict the `Next` branch for **Explore** to detail-free states | `FixedActionAvailabilityIsExact` |
| Remove the unanchored Finding's inspector path from `Next` | `InspectorActionsAreAvailable` |

The mutations are evidence that these properties are observed by the checked
invariants rather than restatements that TLC cannot falsify. The three new
domain, **All**, and embedded-Escape mutations failed in the initial state,
after 74 generated states, and after 160 generated states, respectively.
The two `Next`-restriction mutations now fail
`FixedActionAvailabilityIsExact` after 9 generated states and
`InspectorActionsAreAvailable` after 7 generated states.

## Non-claims

The model does not represent:

- browser-history entries, workspace packets, or canonical view restoration;
- modal stacking, focus trapping, or the shell's complete initial-focus target
  set;
- asynchronous loading, navigation authority, cancellation, or supersession;
- pointer geometry, drag selection, DOM ordering, or rendering;
- declaration construction;
- Finding census construction or cross-projection identity;
- individual structural or capture controls beyond their participation in the
  bulk annotation sets and rendering;
- source, IL, Finding, node, target, or coordinate production (coordinate
  visibility itself is modeled); or
- performance and production-scale cardinality.

Those boundaries remain prose and implementation-test obligations in the
owning design.
