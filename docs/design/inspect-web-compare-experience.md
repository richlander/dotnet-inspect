# Inspect Web Compare Experience

## Status and owner

This document owns the Browser Compare experience tracked by
[#6486](https://github.com/richlander/dotnet-inspect/issues/6486) under the
end-to-end tracker
[#5083](https://github.com/richlander/dotnet-inspect/issues/5083).

Its normative claim is:

> Inspect Web presents Diff and Clone as two explicit modes of one Compare
> inspector at Library, Type, and Member. Library and Type are drill-down
> inventories rather than master/detail surfaces; activating a row moves to
> the exact child subject with Compare and the current mode retained. Member
> is the first detailed-result boundary.

The approved visual proposal is recorded in
[#5083](https://github.com/richlander/dotnet-inspect/issues/5083#issuecomment-5610466100).
This design owns the interaction and Browser-specific result projection, not
the proposal's incidental wording, sample values, or pixel dimensions.
The user explicitly approved this Browser-only scope by requesting the website
proposal and authorizing work to proceed from the revised interaction.

The first adoption is the Compare placement in
[Inspect Web Surface Composition](inspect-web-surface-composition.md). This is
the bounded new-pattern plus first-adopter exception from
[Design scope and composition](../design-scope.md#stage-implementation-after-locking-the-design).
Other owner adoption remains separately staged.

## Ownership and boundaries

This owner defines:

- one Compare working experience rendered for the owner-issued Compare lens
  descriptor at Library, Type, and Member;
- the explicit Diff and Clone mode state within that inspector;
- Browser-specific Library Type rows and Type Member rows projected from
  complete owner-issued comparison or clone-search evidence;
- the rule that Library and Type rows navigate rather than select an in-place
  detail pane;
- retention of the active Compare mode through Compare-owned drill-down;
- the Type Diff whole-Type action and its absence from Type Clone;
- Member-level Diff and Clone summary/detail composition before Explore;
- coverage, truncation, failure, and checked-relation disclosure required
  beside those rows; and
- Compare-local restoration of mode, row, list position, and focus after
  closing an immersive viewer.

It does not own:

- Package Diff baseline or Clone search-scope settings;
- package, library, Type, Member, participant, method, correspondence, or
  provenance identity;
- Diff comparison, compatibility classification, clone retrieval, ranking,
  suppression, coverage, or checked clone relations;
- subject or lens activation semantics, effect authority, canonical state,
  history, or focus settlement after navigation;
- View Facet Registry membership, labels, applicability, ordering, or
  availability;
- immersive Diff or Clone viewer internals;
- the separately owned method-body and paired authored-Source comparison
  evidence reused by immersive viewers; or
- page-level shell, navigation-pane, action-row, responsive, or data-bar
  placement.

## Inputs and prerequisites

Compare consumes, without redefining:

- the Package-owned Diff baseline and Clone scope described by
  [Browser Diff targets](inspect-web-diff-targets.md) and
  [Structural Clone Search Scope](structural-clone-search-scope.md);
- the complete Library-root Diff result owned by
  [Library API Diff Presentation](library-api-diff-presentation.md), including
  its exact nested Type and Member evidence;
- complete Library, Type, or Member clone-search results projected through
  [Clone Candidates Presentation](clone-candidate-presentation.md);
- exact Type and Member inventory identities and activation descriptors from
  [Inspection Subject Navigation](inspection-subject-navigation.md);
- the applicable Compare lens descriptor from
  [View Facet Registry](view-facet-registry.md);
- navigation-result installation, canonical location, history, effect
  authority, focus, and announcement from
  [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md); and
- current-view work identity, cancellation, supersession, and result
  publication from
  [Inspect Web Operation Authority](inspect-web-operation-authority.md); and
- optional owner-issued immersive destinations whose viewer interaction is
  separately owned.

The subject-scoped `library.compare`, `type.compare`, and `member.compare`
descriptor contract is owned by
[View Facet Registry](view-facet-registry.md#compare-facet-extension) and
tracked by
[#6494](https://github.com/richlander/dotnet-inspect/issues/6494). Its runtime
registrations, Registry-private target consumption, public exact-entry result,
and bounded Browser wire projection are implemented by
[#6519](https://github.com/richlander/dotnet-inspect/issues/6519). The Browser
adapter receives only the public Library, Type, or Member entry result; it does
not inspect execution targets or choose Diff or Clone.

Drill-down also requires one product-owned atomic descendant-subject and
exact-lens activation. That contract is tracked by
[#6490](https://github.com/richlander/dotnet-inspect/issues/6490), but its named
runtime gates remain unverified. The Browser must not emulate it by
coordinating a subject request and a later lens request through local mutable
state. Sticky Compare drill-down therefore remains specified but unimplemented.

## One inspector, two modes

Diff and Clone are modes inside one Compare inspector, not separate entries in
the persistent inspector group. The mode control remains inside the working
surface and uses exactly two choices:

- **Diff**
- **Clone**

Changing mode obtains that mode's result for the current exact subject. Clone
executes the subject's own query. Diff may project an exact Type or Member view
from the complete Library-root document; it does not assemble a partial result
from separately fetched rows. Changing mode does not change the subject,
Package settings, or another inspector's state. Loading, failure, unavailable,
incomplete, and successful-empty results retain the selected mode and one
stable Compare frame.

Compare mode is Browser presentation state scoped to the retained Package
model. It survives Compare-owned Library to Type and Type to Member drill-down,
Back and Forward restoration, and closing Explore. It is neither a Navigation
lens identity nor a portable Workspace field. Replacing or removing the
retained Package model discards it with the rest of that model's session-local
comparison settings.

Mode retention is independent of how the Compare lens becomes active. An
atomic descendant-subject plus Compare-lens action, standalone exact-lens
activation, Back or Forward restoration, and ordinary lens recommendation all
present the retained Package model's current mode. None mints fresh mode state
or resets an existing selection.

This preserves a future recommendation change without coupling it to mode. If
Compare later becomes the preferred lens for a subject, entering that subject
through ordinary recommendation keeps **Diff** when Diff is current and keeps
**Clone** when Clone is current. Only a retained Package model with no Compare
mode state uses Diff as the presentation default. Assigning Compare a
recommendation role remains a separate Registry and Navigation policy change.

## Shared surface frame

Every subject uses one quiet frame:

```text
Compare <subject>                                  Diff | Clone
effective target or scope                         Change target
summary metrics
subject-specific result
coverage and result status
```

The effective target row explains the active Package-owned setting without
repeating its controls. **Change target** returns to Package Overview's
Comparison targets work area. Compare does not render a second version,
breadth, discovery, or work-limit editor.

Summary metrics precede the inventory or Member result. They report only
owner-issued counts and classifications. Diff completeness and Clone
coverage/truncation remain visibly distinct; the surface never presents a
bounded Clone result as exhaustive.

## Library drill-down

Library Compare contains Type rows and no selected-Type detail pane.

### Library Diff

The successful result renders one row for each changed Type in the complete
Library Diff result. A row carries:

- the exact Type display and owner-issued descendant activation;
- added, removed, or changed-in-place state;
- whether the Type definition changed;
- distinct changed-member count; and
- compact breaking, additive, or potentially breaking counts when present.

Activating the row moves to that exact Type with Compare Diff retained. It
does not select a detail region inside Library, open the immersive viewer, or
derive a Type result from display text.

A removed Type has no current-Package Navigation subject. Its row remains
visible with complete Before-side change evidence but is not activatable.
Compare does not synthesize a current Type identity or silently open a
different Type.

### Library Clone

The successful result renders Type rows grouped from the document's per-seed
coverage, joined to exact Type inventory identity. A row may summarize how
many eligible seed Members produced ranked candidates, along with incomplete
or unsupported seed evidence. It does not render candidate pairs, promote the
highest pair score to a Type similarity score, or imply that the Type is a
clone.

Activating the row moves to that exact Type with Compare Clone retained and
executes the Type-scoped Clone query. The Type result is not formed by
filtering, regrouping, or reranking the Library query's bounded candidate-row
population.

## Type drill-down

Type Compare contains Member rows and no selected-Member detail pane.

### Type Diff

Type Diff obtains the complete Library-root Diff document for the containing
Library and projects the exact Type entry. Direct entry at Type uses the same
root query; it does not construct an independent `TypeDiff` input.

The successful result begins with one **Whole type diff** row when an
owner-issued immersive destination is available. It represents the complete
Type-scoped Diff result and opens that destination without changing subject.
Unavailable or failed destination construction remains visible and does not
remove the changed-Member inventory.

The remaining rows represent changed Members using exact owner-issued Member
identity and compact change classification. Activating a Member row moves to
that exact Member with Compare Diff retained. It does not select Member detail
inside Type.

A removed Member has no current-Type Navigation subject. Its row remains
visible with complete Before-side change evidence but is not activatable.

### Type Clone

Type Clone has no whole-Type row or action. It renders one row for each logical
Member containing at least one admitted seed body with ranked candidates,
joined to exact Member inventory identity. Accessor or overload seed coverage
may be summarized beneath that logical Member without inventing one Member
similarity score.

Activating a row moves to that exact Member with Compare Clone retained and
executes the Member-scoped Clone query. The Member result is not a filtered
view of the Type query's bounded candidate rows.

## Member result boundary

Member is the first subject that presents detailed comparison evidence in the
Compare working surface.

Member Diff projects the exact Member entry from the containing Library-root
Diff document. It summarizes correspondence, compatibility, and the complete
API-change evidence carried there. Source or implementation coverage appears
only when a separately owned immersive destination supplies it. **Explore**
opens that owner-issued destination.

Member Clone renders the Member-scoped globally ranked candidate rows and
selected-candidate evidence supplied by Clone Candidates Presentation.
Retrieval score remains separate from any independently issued checked
relation. **Explore** opens only an owner-issued destination for the selected
pair.

The legacy contextual **Compare method bodies** and
**Compare authored source** actions and dialogs are retired under
[#6491](https://github.com/richlander/dotnet-inspect/issues/6491). Their
managed method-body and paired authored-Source evidence remains separately
owned substrate that an immersive Omni destination may consume. Compare does
not recreate the retired dialogs or treat representation comparison and
cross-version comparison as interchangeable.

## Identity and aggregation

Compare joins evidence only through typed owner-issued identity:

- Library Diff Type rows use exact metadata Type identity.
- Type Diff Member rows use exact metadata Member anchors and occurrence
  identity.
- Clone seed methods resolve through exact participant, MVID, and MethodDef
  identity to Navigation-issued Type and Member subjects.

Display text, assembly simple name, declaring-Type spelling, or candidate
address text is never a join key.

Library Clone aggregation groups the document's per-seed coverage by exact
declaring Type. Type Clone aggregation groups it by exact logical Member while
retaining
the contributing physical seed bodies and their failures. A missing,
ambiguous, failed, or out-of-scope identity join remains visible and excludes
that evidence from an activatable row; it does not fall back to text matching.

## Navigation and history

Each drill-down row carries the product-issued exact descendant subject and
Compare-lens activation required by #6490. Activation produces one navigation
transition and one history entry. The user never observes an intermediate
Overview or API lens, and the Browser does not issue a second compensating lens
activation.

Diff or Clone mode is installed with the destination Compare renderer only
after Navigation Consumer accepts a current-authority applied result. Stale or
superseded work cannot change mode, rows, focus, history, or the active
surface.

A current-authority unavailable, rejected, failed, or aborted result is not
treated as successful drill-down. Compare defers its presentation and any
required complete-snapshot installation, canonical-history alignment,
renderer replacement, focus settlement, and announcement to Navigation
Consumer's typed synchronization disposition. Those synchronization effects
may replace the visible surface, rows, or history with product state committed
by an earlier result; they do not install the non-applied request's descendant
or mode.

Back and Forward restore the canonical subject and Compare lens through
Navigation. Compare then presents the retained Package model's current mode;
mode is not copied into individual history entries and does not alter the
canonical packet. If the Package model no longer owns mode state, Compare uses
Diff as its presentation default without changing Package target settings.

## Operation lifetime

Every Diff or Clone request begins under
[Inspect Web Operation Authority](inspect-web-operation-authority.md) for the
exact Package model, subject, mode, and effective target or scope generation.
Only the current authorized operation may publish rows, metrics, coverage,
failure, or an Explore destination.

Switching mode, drilling to a child subject, changing the Package target or
scope, navigating through history, or removing or replacing the Package model
supersedes incompatible pending work. Cancellation is best-effort; a late
completion with stale authority changes no visible result, mode, selection,
focus, or history.

## Explore and return

Explore is a transition from a settled Compare result, not another query owner.
Opening it preserves:

- exact subject and Compare mode;
- selected Member or candidate row;
- inventory scroll position; and
- the invoking action as the preferred return-focus target.

Closing returns to that state without rerunning a still-current query. If the
underlying result has been replaced, Compare renders the replacement and moves
focus to the nearest surviving result heading or persistent Compare control
rather than a detached row.

Library has no Explore action. Type Clone has no Explore action. Type Diff
offers only **Whole type diff**. Member Diff and Member Clone offer Explore for
their settled detailed result when the corresponding destination is available.

## Responsive behavior

Library and Type retain the same drill-down topology at narrow widths. Their
rows become one full-width list; they do not enter the navigation-pane
inventory/detail switch because Compare has no in-place detail pane at those
subjects.

Member follows Surface Composition's existing Member navigation/detail
behavior. The immersive viewer remains full-bleed. Viewport changes never
alter Compare mode, execute a query, select a row, or change subject.

## Failure and empty states

Every result state retains the common frame, target explanation, mode control,
and local recovery action when one exists.

- A successful empty Library Diff says that no Types changed.
- A successful empty Type Diff says that no Members changed and omits Whole
  type diff when there is no diff destination.
- A successful empty Clone result says that no ranked candidates were returned
  and separately reports whether coverage was complete.
- Incomplete Clone coverage retains visible seed and participant evidence even
  when no activatable Type or Member row can be formed.
- Query, projection, identity-join, and destination failures remain distinct;
  none becomes a successful empty list.

## Non-claims

This design does not claim:

- that a Clone candidate is a checked clone relation;
- that a Library or Type has one aggregate similarity score;
- that a child result can be reconstructed from a parent result;
- that whole-Type Diff viewer semantics already exist;
- that Member representation comparison and cross-version comparison are one
  axis;
- that Compare mode is portable Workspace state; or
- that the Browser may infer subject actions or identity from rendered text.

## Adoption

The staged path is:

1. this Browser Compare contract and its Surface Composition placement;
2. Registry-owned Library, Type, and Member Compare facet contract in #6494;
3. Navigation-owned atomic descendant subject and lens activation in #6490;
4. the host-neutral facet execution handoff, consumer-paired runtime
   registration, and bounded managed/browser projection in #6519;
5. Library Diff adoption and retirement of the transient authored-source
   comparison interaction;
6. Library and Type Clone drill-down adoption;
7. Type and Member Diff narrowing;
8. Member Clone detail and checked-relation composition when available; and
9. whole-Type and Member immersive viewer adoption under their focused owners.

Each stage lands only when its own result and failure states are complete. An
unimplemented downstream destination remains unavailable; the UI does not
advertise an action backed by a placeholder or success-shaped fallback.

Stage 4 is implemented as the subject-scoped managed entry handoff and bounded
Browser wire projection. It installs no Compare UI and executes no Diff or
Clone query; stages 5 through 9 remain the production result experiences.

The stage-3 Navigation capability follows Navigation's existing shared
two-host plan: #6111 and #5513 consume its stateless exact-pair evaluator in
the CLI, while #6113, #5510, and #5511 supply retained Browser/Wasm action and
result authority. Compare mode and history remain Browser-only presentation
concerns; the exact descendant and Registry mapping are shared product
behavior.

## Acceptance scenarios

1. Open Library Compare Diff and confirm that every current-side result row is
   a Type navigation item, removed Types remain visible and non-activatable, no
   selected-Type detail pane exists, and activating a row produces one Type
   Compare Diff history entry.
2. Open Library Compare Clone with a result limit reached and confirm that Type
   rows derive from complete per-seed coverage rather than the returned top-N
   pair rows. Activate a Type and confirm that its own Clone query executes.
3. Open Type Compare Diff and confirm that Whole type diff appears before the
   changed-Member rows only when its destination is available. Activate a
   Member and confirm that Member Compare Diff opens without an intermediate
   Member Overview.
4. Open Type Compare Clone and confirm that no whole-Type action appears.
   Activate a Member and confirm that its own Clone query executes.
5. Switch Library Compare from Diff to Clone, drill to Type and Member, use
   Back and Forward, and confirm that the retained Package's current mode
   remains active without becoming Navigation lens identity or portable state.
   Repeat with a recommendation fixture that selects Compare for the
   destination subject: current Diff remains Diff, current Clone remains
   Clone, and only absent mode state defaults to Diff.
6. Open Member Clone and confirm that candidate score and checked relation are
   separately labelled. Open Explore and close it; confirm that the same row,
   scroll position, mode, and useful focus are restored.
7. Exercise successful-empty, incomplete, failed query, failed identity join,
   and unavailable Explore outcomes at every applicable subject. Confirm that
   each retains the common frame and that none is rendered as successful empty
   evidence.
8. Repeat Library and Type drill-down at a narrow viewport and confirm that one
   list fills the working surface without a master/detail switch or horizontal
   page overflow.
9. Start Diff, switch to Clone before completion, then drill down while Clone
   is pending. Confirm that stale parent and Diff completions publish nothing.
   Repeat across Back, Forward, target replacement, and Package removal.
10. Supply a removed Member in Type Diff. Confirm that its Before-side evidence
    remains visible, the row has no descendant activation, and no current
    Member identity is inferred.
11. Return a current-authority non-applied drill-down result with
    `Synchronization required`. Confirm that Navigation Consumer installs the
    complete returned snapshot and aligns history before presenting the
    semantic outcome, without installing the rejected descendant or treating
    the drill-down as applied.
