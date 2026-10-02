# Inspect Web code evidence viewer

## Status and owner

This document owns the shared full-bleed Browser composition used to inspect
code-shaped evidence. Implementation is tracked by
[#9129](https://github.com/richlander/dotnet-inspect/issues/9129).

Its normative claim is:

> One code evidence viewer shell presents one owner-issued evidence surface at
> a time through a near-full-width center region and a bounded right rail. The
> shell composes header, controls, content, rail, detail, and failure slots; it
> does not define, infer, or translate the evidence rendered in those slots.

Annotated Source is the first production consumer. Member Diff Explore adopts
the same shell in
[#9112](https://github.com/richlander/dotnet-inspect/pull/9112), using the
center region for the mapped-text diff and the rail for owner-issued evidence
choices and endpoint context.

## Ownership and boundaries

This owner defines:

- the full-bleed modal frame shared by code-evidence consumers;
- header, control-strip, workspace, and detail-layer placement;
- one central evidence region and one bounded right rail;
- independent content and rail scroll ownership;
- the responsive transition that stacks the rail below the content; and
- the rendering boundary through which a consumer supplies those regions.

It does not own:

- generic modal lifecycle, inert background, dismissal, focus containment, or
  focus return
  ([Inspect Web Shell Interaction](inspect-web-shell-interaction.md));
- Annotated Source documents, annotations, media, selection, inspector
  contents, detail, or viewer-local Escape behavior
  ([Annotated Source viewer interaction](annotated-source-viewer-interaction.md));
- Member Diff destination issuance, modes, requests, outcomes, or return state
  ([Inspect Web Compare Explore](inspect-web-compare-explore.md));
- mapped-text diff rows, correspondence, navigation, copy, or presentation
  controls
  ([Inspect Web diff viewer interaction](inspect-web-diff-viewer-interaction.md));
- evidence availability, identity, or ordering; or
- routed surfaces, browser history, Workspace state, or share packets.

The shell is Browser presentation code. It does not add a product document or
portable wire contract.

## Composition

The shell accepts owner-rendered regions:

| Region | Contract |
| --- | --- |
| Header | Accessible heading, owner identity, and close or destination actions |
| Controls | Optional controls for the active evidence surface |
| Content | The active code-shaped evidence and its own rendering semantics |
| Rail | Owner-issued choices, context, or inspector content |
| Detail | Optional transient layer associated with the active content |
| Failure | Visible replacement body when the consumer rejects its document |

The shell renders content and rail as peer regions. Content receives all
remaining width after the bounded rail and is the visual priority. The rail
does not overlay or reduce the content through consumer-specific absolute
positioning.

Consumers keep their own stable ids, data attributes, action vocabulary, and
focus targets. The shell adds shared structural classes; it does not rewrite
consumer markup or dispatch consumer actions.

The renderer supports two modal-lifecycle hosts:

- a self-hosted overlay renders the backdrop and accessible dialog semantics;
  and
- a frame hosted by an existing native dialog renders only the shared header,
  controls, workspace, detail, and failure composition.

The frame form never nests another dialog role. The placement owner keeps its
existing dismissal, focus containment, and focus-return behavior while using
the same shell structure and CSS.

## Evidence choices

A placement owner may use the rail as a picker when it has multiple
owner-issued evidence kinds. The owner supplies the choices, availability,
order, active identity, operation state, and activation behavior. The Browser
must not derive choices from rendered headings, source text, endpoint labels,
or payload shape.

The picker selects evidence, not presentation:

- **Authored Source** and a future **Decompiler** comparison are Member Diff
  evidence choices.
- Unified and side-by-side are presentations of the active mapped-text diff.
- C# and IL are Annotated Source media choices within one annotated document.
- Findings and relationships are inspector content for that document.

This separation prevents one visual control from conflating document
selection, media visibility, diff layout, and annotation membership.

A consumer with one evidence kind need not render a one-item picker. Its rail
must still contain useful owner-issued context or inspector content; otherwise
the consumer may omit the rail and let content take the full width.

## Scrolling and responsive behavior

At desktop widths, the workspace uses one flexible content column and one
bounded rail. Content and rail scroll independently. The modal frame and page
do not become horizontal scroll owners; long code lines scroll inside the
active evidence renderer.

At the shared narrow boundary, the rail stacks after the content and receives
a separating border. Resizing changes only layout. It does not rerun evidence
work, reset consumer state, change the active evidence, or move identity
between regions.

Controls may scroll horizontally within their strip when their owner-issued
inventory exceeds the available width. That does not make the whole viewer or
page horizontally scrollable.

## Failure

A consumer that rejects its document renders a visible failure body inside the
same shell. The header and close action remain available. Rejection does not
become an empty workspace, a missing modal, or a success-shaped content
surface.

The shell does not catch consumer rendering failures. Each consumer defines
which typed rejection it can present and preserves unrelated failures.

## Adoption

1. **Annotated Source extraction.** Move its modal frame and two-region
   workspace to the shared renderer and CSS vocabulary. Preserve Annotated
   Source markup identities, actions, selection, controls, detail, independent
   scroll positions, responsive behavior, and rejection presentation.
2. **Member Diff Explore.** Restack #9112 onto the shared shell. Remove the
   What changed and Declaration panes, render the authored-Source diff in the
   content region, and render owner-issued evidence choices plus endpoint
   context in the rail.
3. **Additional consumers.** Adopt only when a focused owner has code-shaped
   evidence that benefits from the same full-bleed hierarchy. A shared shell is
   not authority to convert ordinary pages or unrelated modals.

Each adoption keeps one content renderer for its evidence. The shell does not
introduce a second Annotated Source renderer or a second mapped-diff row walk.

## Acceptance scenarios

1. Open Annotated Source Explore and confirm the source remains the width
   owner, the inspector remains bounded, and both regions retain independent
   scroll positions.
2. Reject an Annotated Source document and confirm the shared header and Close
   action remain available around a visible failure body.
3. Resize Annotated Source Explore across the narrow boundary and confirm the
   inspector stacks below the source without resetting media, annotations,
   selection, detail, or scroll state.
4. Open Member Diff Explore after #9112 adopts the shell and confirm the
   mapped-text diff occupies the center region, the three-pane composition is
   absent, the native dialog contains no nested dialog role, and the rail
   exposes only destination-issued evidence.
5. Switch unified and side-by-side presentation and confirm the active evidence
   choice does not change; switch evidence and confirm the retained diff
   presentation preference does not become an evidence choice.

## Non-claims

This design does not claim:

- one document or row model for Annotated Source and mapped diffs;
- a generic evidence producer, acquisition API, or serialization format;
- that every code-shaped surface needs a rail;
- that evidence choices are globally registered or Browser-inferred;
- persistence of viewer state in history, Workspace, or share packets; or
- adoption by CLI, Markout, or non-Browser hosts.
