# Annotated Source viewer model

[`ViewerSession.tla`](ViewerSession.tla) is the bounded executable design for
interaction inside one embedded Annotated Source reader and one modal viewer.
The prose owner is
[Annotated Source viewer interaction](../../annotated-source-viewer-interaction.md).

## Scope and assumptions

The model assumes:

- one loaded, immutable annotated document;
- a finite product-issued Finding, target, node, and default-annotation census;
- every target belongs to exactly one Finding;
- C# and IL targets are disjoint;
- one Finding has both C# and IL targets, one is IL-only, and one is
  unanchored;
- user gestures are atomic; and
- shell open, dismissal, and focus handoff occur as one atomic boundary event.

The model explores every subset of the two annotatable Findings as the default
set. Its finite configuration contains three Findings, three targets, two
media, and one selectable node. These bounds exercise empty, singleton,
all-equal, C#-only, IL-only, dual-target, and unanchored cases without claiming
that production cardinality is bounded.

## Checked behavior

The safety invariants check:

- state and record types;
- legal primary and detail shapes;
- the embedded reader's default-and-C# selection boundary;
- absence of modal detail after dismissal;
- at least one visible medium;
- exact derivation of **Default**, **All**, **Clear**, and **Custom**;
- a concrete valid focus target throughout an open modal;
- exact chip-or-inspector focus restoration from historical detail evidence;
- stable control focus and exact clearing when an annotation toggle removes
  the primary Finding;
- fresh modal initialization and transfer of a representable embedded primary;
- exact dismissal, embedded-primary derivation, and **Explore** focus; and
- the rule that Escape cannot dismiss the modal while Finding detail is open.

The embedded reader can produce primary state only from a default rendered C#
chip. Its ineligible-primary rejection branch is therefore structural, not a
reachable transition: `EmbeddedStateIsConstrained` makes an unanchored,
IL-only, or non-default embedded primary unrepresentable. Dismissal from the
modal exercises both eligible and ineligible primary derivation.

`Next` includes embedded chip inspection, modal opening, pointer and Escape
dismissal, chip and inspector Finding detail, node selection, detail closure,
embedded Escape fall-through, **Default**, **All**, **Clear**, annotation
toggle, and media toggle. The action-coverage run reached every action.

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
| Generated states | 82,470 |
| Distinct states | 6,164 |
| Search depth | 11 |

The coverage run reported nonzero distinct transitions for every top-level
action. The smallest count was four for `OpenEmbeddedChip`; representative
transition counts were 12 for `OpenModal`, 40 each for the two dismissal
actions, 652 for `CloseCurrentDetail`, 2,376 for `ToggleAnnotation`, and 1,824
for `ToggleMedium`.

## Mutation evidence

Nine deliberate one-line mutations were run against the same configuration.
Each produced a concrete counterexample:

| Mutation | Violated invariant |
| --- | --- |
| Permit disabling the final visible medium | `AtLeastOneMediumIsVisible` |
| Open the modal with all media instead of C# only | `ModalOpeningIsFresh` |
| Let Escape dismiss the modal while detail is open | `EscapeCannotBypassDetail` |
| Restore focus to a sibling medium's chip | `DetailClosureRestoresExactFocus` |
| Drop focus after an annotation toggle | `ModalAlwaysHasFocus` |
| Drop focus after the **Default** command | `ModalAlwaysHasFocus` |
| Preserve stale reported state after an annotation toggle | `ReportedStateIsDerived` |
| Preserve Finding detail through modal dismissal | `DetailShapes` |
| Preserve a stale embedded primary through dismissal | `ModalDismissalIsExact` |

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
- source, IL, Finding, node, target, or coordinate production; or
- performance and production-scale cardinality.

Those boundaries remain prose and implementation-test obligations in the
owning design.
