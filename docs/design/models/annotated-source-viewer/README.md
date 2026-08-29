# Annotated Source viewer model

[`ViewerSession.tla`](ViewerSession.tla) is the bounded executable design for
interaction inside one embedded Annotated Source reader and one modal viewer.
The prose owner is
[Annotated Source viewer interaction](../../annotated-source-viewer-interaction.md).

## Scope and assumptions

The model assumes:

- one loaded, immutable annotated document;
- a finite product-issued Finding, target, node, and default-annotation census;
- an immutable product-issued supported-media set containing C# and optionally
  IL;
- every target belongs to exactly one Finding;
- C# and IL targets are disjoint;
- one Finding has two distinct C# targets plus an IL target, one is IL-only,
  and one is unanchored;
- user gestures are atomic; and
- shell open, dismissal, and focus handoff occur as one atomic boundary event.

The model explores every subset of the two annotatable Findings as the default
set when both media are supported, and every subset of the C#-annotatable
Finding when only C# is supported. Its finite target identities span both
document configurations; the immutable supported-media set derives each
document's annotation universe. The configuration contains three Findings,
four targets, two supported-media sets, two media, and one selectable node.
These bounds exercise empty, singleton, all-equal, C#-only documents, optional
IL, two same-medium targets for one Finding, IL-only Findings, dual-media
targets, and unanchored cases without claiming that production cardinality is
bounded.

## Checked behavior

The safety invariants check:

- state and record types;
- legal primary and detail shapes;
- the embedded reader's default-and-C# selection boundary;
- destruction of embedded detail on modal opening;
- absence of modal detail after dismissal;
- at least one document-supported visible medium;
- exact derivation of the annotation universe and defaults from supported
  media;
- independent precedence checks for exact derivation of **Default**, **All**,
  **Clear**, and **Custom**;
- a concrete valid focus target throughout an open modal;
- exact embedded and modal chip-or-inspector opening, including the exact
  same-medium target, plus historical eligible-primary transfer;
- exact persistent-inspector inventory equality with the Finding census,
  including an unanchored-Finding witness;
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
- exact dismissal, embedded-primary derivation, and **Explore** focus; and
- the rule that Escape cannot bypass Finding detail on either surface.

The embedded reader can produce primary state only from a default rendered C#
chip. Its ineligible-primary rejection branch is therefore structural, not a
reachable transition: `EmbeddedStateIsConstrained` makes an unanchored,
IL-only, or non-default embedded primary unrepresentable. Dismissal from the
modal exercises both eligible and ineligible primary derivation.

`Next` includes embedded chip inspection, modal opening, pointer and Escape
dismissal, chip and inspector Finding detail, node selection, detail closure,
embedded Escape fall-through, **Default**, **All**, **Clear**, annotation
toggle, media toggle, and coordinate toggle. The action-coverage run reached
every action.

This is a safety model only. It makes no liveness claim: users may stop after
any gesture, and asynchronous navigation progress belongs to another owner.

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
error:

| Result | Value |
| --- | ---: |
| Generated states | 422,928 |
| Distinct states | 27,738 |
| Search depth | 12 |

The coverage run reported nonzero distinct transitions for every top-level
action. The smallest count was 12 for `OpenEmbeddedChip`; representative
transition counts were 18 for `OpenModal`, 60 each for the two dismissal
actions, 1,344 for `OpenModalFinding`, 1,548 for `CloseCurrentDetail`, 5,976
each for `ToggleAnnotation` and `ToggleMedium`, and 3,096 for
`ToggleCoordinates`.

## Mutation evidence

Thirty-eight deliberate targeted mutations were run against the same
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
| Clear primary selection when directly closing detail | `DetailClosureOutcomeIsExact` |
| Include an unsupported-medium Finding in the annotation universe | `AnnotatableUniverseIsSupported` |
| Record a sibling C# target for an embedded chip opener | `FindingOpeningIsExact` |
| Clear eligible primary state while opening the modal | `ModalOpeningIsFresh` |
| Change surface while directly closing detail | `DetailClosureOutcomeIsExact` |
| Reset visible media during an annotation toggle | `AnnotationToggleOutcomeIsExact` |
| Reset coordinates while opening modal Finding detail | `FindingOpeningIsExact` |
| Reset coordinates while selecting a node | `NodeSelectionOutcomeIsExact` |
| Restrict inspector actions to annotatable Findings | `InspectorInventoryIsComplete` |

The mutations are evidence that these properties are observed by the checked
invariants rather than restatements that TLC cannot falsify.

## Non-claims

The model does not represent:

- browser-history entries, workspace packets, or canonical view restoration;
- modal stacking or focus trapping owned by the shell;
- asynchronous loading, navigation authority, cancellation, or supersession;
- pointer geometry, drag selection, DOM ordering, or rendering;
- declaration construction;
- Finding census construction or cross-projection identity;
- source, IL, Finding, node, target, or coordinate production (coordinate
  visibility itself is modeled); or
- performance and production-scale cardinality.

Those boundaries remain prose and implementation-test obligations in the
owning design.
