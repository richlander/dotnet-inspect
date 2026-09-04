# Inspect Web Navigation Consumer

This document owns the browser-side navigation-result consumer model: the
single session-scoped holder of returned effect authority that installs a
product navigation result, commits canonical location and browser history,
resolves focus, announces status, and tracks synchronization debt. It is the
sole authority for what happens after
[Inspection Subject Navigation](inspection-subject-navigation.md) or a
dedicated synchronization request returns a typed outcome. It does not decide
which subject, coordinate, or lens is rendered; that is
[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md).

## Ownership and boundaries

This owner defines:

- canonical location composition and browser refresh/shared-link restoration;
- browser-history push, replace, and adopt classification, including
  non-installing traversal realignment;
- the product transition lifecycle: semantic outcome and synchronization
  disposition, atomic installation, and the persistent shell live region;
- effect-authority validation for every deferred focus and announcement
  callback;
- synchronization debt, dedicated-request identity, and request/lifetime
  correlation across remount;
- destination-lifetime tracking: mounting, replacement, destruction, and
  remount as they bound consumer authority;
- installation, focus, announcement, acknowledgement, and abandonment
  ordering; and
- the model-checked evidence for this consumer lifecycle.

It does not own:

- which coordinate, subject, or lens descriptor is rendered, or the widget
  interaction (roving tabs, listbox commit, menu arrow navigation) that
  produces a submitted action ID (owned by
  [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md));
- selector-pill visual states or progressive filter disclosure (owned by
  [Inspect Web Presentation Language](inspect-web-presentation-language.md));
- shell actions, modal dialog contract, or routed-versus-modal
  classification (owned by
  [Inspect Web Shell Interaction](inspect-web-shell-interaction.md));
- page-level placement or responsive composition (owned by
  [Inspect Web Surface Composition](inspect-web-surface-composition.md));
- product snapshot construction, reconciliation, recommendation, or
  retained-session authority (owned by
  [Inspection Subject Navigation](inspection-subject-navigation.md)); and
- portable field identity, packet versioning, codec, projection, or
  restoration semantics (owned by
  [Workspace Definitions](workspace-definitions.md) and
  [#4787](https://github.com/richlander/dotnet-inspect/issues/4787)).

## Inputs or consumed contracts

This document consumes, without redefining:

- the typed semantic outcome, synchronization disposition, opaque effect
  authority, and generation returned by
  [Inspection Subject Navigation](inspection-subject-navigation.md);
- the opaque projectable, non-projectable, or failed canonical-packet outcome
  owned by [Workspace Definitions](workspace-definitions.md) and
  [#4787](https://github.com/richlander/dotnet-inspect/issues/4787); this
  document never parses compact packet fields itself;
- the coordinate, subject/hierarchy menu, lens tab, and Library listbox
  targets whose focus this document resolves, as rendered by
  [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md);
  and
- the persistent shell control, modal dialogs, and routed-surface
  classification defined by
  [Inspect Web Shell Interaction](inspect-web-shell-interaction.md).

## Canonical location and refresh

For a package-backed workspace, the visible URL keeps only a human-readable
package courtesy identity. Durable workspace state remains in the
product-owned canonical packet:

```text
?package=System.Text.Json&w=<opaque-canonical-packet>
```

The UI does not expand Package, Library, Type, Member, filter, or source
identity into readable path segments merely to make them URL-addressable. A
non-package workspace omits the `package` courtesy field rather than placing a
local path or other sensitive coordinate in readable URL state.

[Workspace Definitions](workspace-definitions.md) owns which workspace state
is portable and how it is encoded, decoded, and restored, tracked by
[#4787](https://github.com/richlander/dotnet-inspect/issues/4787). The UI
receives a typed projectable, non-projectable, or failed outcome; it never
inspects compact fields.

- A projectable outcome supplies the opaque canonical packet and an optional
  product-issued package courtesy identity. The UI location adapter composes
  `w=<packet>` and, only when that courtesy identity is present,
  `package=<identity>`.
- A non-projectable outcome leaves the current presentation session-local,
  supplies the visible reason used by explicit Share, and removes any prior
  `w` and package courtesy fields before the transition's push or replace.
  Session history may retain the live presentation, but refresh starts without
  a stale canonical workspace.
- A failed transition retains the prior workspace and URL and surfaces the
  typed failure.

Browser history uses the same classification:

- push a history entry for an applied explicit action that performs Home,
  Workspace, or Diagnostics routing; opens, closes, or activates a coordinate;
  changes package version or TFM; changes the Package, Library, Type, or Member
  subject; or changes the active lens or Member section;
- replace the current entry for committed filter changes and portable overload,
  body, or source-target refinements, plus maintenance, dedicated
  synchronization, or non-applied outcomes whose `Synchronization required`
  disposition installs refreshed or reconciled state; and
- adopt the browser-selected entry without calling `pushState` or
  `replaceState` when initial shared-link activation, refresh, Back, or Forward
  restores the exact requested state. If that restoration instead installs a
  changed unavailable, synchronized, or reconciled snapshot, replace the
  selected entry with the returned canonical state. If Back or Forward returns
  a `Current` unavailable-without-replacement, rejected, failed, or aborted
  result, replace the browser-selected entry with the installed snapshot's
  canonical location, surface and announce the exact outcome, and focus the
  retained destination heading or persistent shell fallback. If the same
  semantic outcome is `Synchronization required`, install its complete
  snapshot and replace the selected entry with that snapshot's canonical
  location before presenting the outcome. Neither case writes a new entry, and
  both let a later traversal continue past the failed location; and
- do not mutate history for hover, focus, uncommitted listbox movement,
  disclosure animation, or incidental scroll position.

The session-scoped location adapter tracks whether the browser-selected entry
is aligned with the installed snapshot. A Back or Forward event records one
UI-local unresolved traversal serial and the installed snapshot's retained
canonical location before submitting product restoration. This serial is
presentation bookkeeping, not product intent or effect authority.

A newer Back or Forward event replaces the unresolved traversal with its own
serial because it now owns the browser-selected entry. Before the UI submits
any non-browser intent or dedicated synchronization request while an unresolved
traversal remains, it synchronously replaces the selected entry with the
retained canonical location and marks it aligned. The later intent then uses
its ordinary push, replace, or no-write classification. A synchronization
request is not a product navigation intent, but it uses this same pre-request
alignment rule because its response carries no traversal serial. Exact
restoration adoption, changed-snapshot installation, or current-authority
location realignment also marks the matching traversal aligned. Product-side
discard or stale-authority abandonment performs no history write; its successor
has already replaced the traversal obligation, realigned the selected entry
before submission, or preserved the session-scoped obligation for remount.

Destination destruction does not clear an unresolved traversal. Before a
replacement destination renders after remount, the location adapter
synchronously replaces the still-selected entry with the retained canonical
location and marks that traversal aligned. This repair is independent of
snapshot-synchronization debt: a `Current` non-installing restoration can
require it even though the product acknowledgement receipt is already current.

A future packet projection does not decide its own history granularity. It
inherits this UI-owned push, replace, or adopt classification.

On browser refresh or shared-link activation, the UI submits the opaque packet
to the product codec and renders its atomic success or typed failure. It does
not use the readable package courtesy field as a fallback workspace.

## Product transition lifecycle

Subject, lens, coordinate, version, TFM, and canonical-restoration actions use
one retained Inspection Subject Navigation session. The UI treats snapshot
generations, action IDs, intent tokens, and effect authority as opaque. It does
not mint, order, compare, or reconstruct them.

A navigation destination surface is any inspection surface or routed Home,
Workspace, or Diagnostics surface that consumes a navigation result. Its
lifetime begins when its renderer is mounted for a returned destination and
ends when that renderer is replaced or unmounted. Modal and transient controls
may invoke navigation, but they do not independently consume the returned
navigation authority.

Each current product result has two orthogonal classifications. Its semantic
outcome decides what the UI presents:

| Outcome | UI presentation |
| ------- | --------------- |
| Applied | Present the requested transition from the returned snapshot |
| Unavailable | Show the exact unavailable result without fallback |
| Rejected | Show the rejection |
| Failed | Show the diagnostic |
| Aborted | Show the typed prerequisite failure |
| Superseded | Receive no consumer result or authority and produce no visible effect |

Its synchronization disposition decides whether the complete returned snapshot
must be installed:

| Disposition | UI obligation |
| ----------- | ------------- |
| Current | Use the already installed snapshot and perform only the semantic outcome's remaining effects |
| Synchronization required | Install and render the complete returned snapshot under the current effect authority before presenting the semantic outcome or acknowledging |

Outcome names do not authorize the UI to infer whether product and consumer
state agree. In particular, unavailable-without-semantic-replacement, rejected,
failed, and aborted results may still require installation because an earlier
result advanced the retained product session before the consumer acknowledged
it. The UI neither compares snapshot revisions nor reconstructs the missing
state. It consumes the typed disposition and installs only the complete
snapshot carried by that result.

Installation is one atomic consumer effect: it commits the returned snapshot,
its rendered content, and the UI-owned canonical URL and history-commit
classification. The semantic outcome remains visible after installation; a
failed synchronization does not become an applied navigation result merely
because it carried newer state. A `Current` unavailable-without-replacement,
rejected, failed, or aborted result retains the installed snapshot, URL, and
ordinary history classification. Superseded work never reaches the consumer.

The non-installing rules above describe `Current` results whose initiating UI
action has not already changed the browser entry. After Back or Forward selects
an entry, such a result instead replaces that selected entry with the installed
snapshot's canonical location as defined by the browser-history classification.
This is location realignment, not a snapshot installation or successful
restoration. A `Synchronization required` result instead installs its complete
snapshot and aligns the selected entry with that installed state.

Location realignment is a required consumer effect. Immediately before the
history write, the consumer validates both the returned authority and the
matching unresolved traversal serial. If either is stale or foreign, it changes
neither canonical URL nor history and abandons the authority; the current
successor owns the still-current alignment obligation. Realignment completes
before acknowledgement. If an implementation defers the write, its callback
repeats both checks at execution time.

An applied result uses the initiating explicit action's push-or-replace
classification or a browser restoration's adopt classification. An unavailable
result whose disposition requires a changed reconciled snapshot replaces the
current history entry because refreshed evidence, not the requested unavailable
target, produced that state. A rejected, failed, aborted, or
unchanged-unavailable result with `Synchronization required` also replaces the
current entry: the write records product state committed before this semantic
outcome, not a successful activation of the rejected or failed request. These
replacement rules supersede an initiating adopt classification and never push
an entry for a subject or lens the user did not activate.

A dedicated synchronization result has no semantic navigation change. When no
new browser traversal has selected an entry since its request, it installs the
complete current snapshot under fresh authority and replaces the current entry
with that snapshot's canonical location. The request carries no traversal
serial. If Back or Forward selects an entry after the request and before the
result is consumed, the result is foreign to that newer selection: the consumer
abandons its authority without installation or history mutation, preserves
synchronization debt, and lets the traversal's current result or a later fresh
request discharge it. A consumed synchronization result preserves surviving
focus and announces only a visible change; after remount, detached-focus
recovery uses the new destination heading or persistent shell fallback.

Product-initiated maintenance results use the same disposition-driven,
authority-validated installation and acknowledgement lifecycle. A
`Synchronization required` maintenance result replaces the current history
entry and updates the canonical URL from the returned projectable state; a
`Current` result performs no snapshot or history write. Maintenance does not
move focus merely because evidence refreshed. If installation removes the
focused element, the focus-preservation rule below applies. The live region
announces a maintenance result only when it changes visible status, the active
subject, or the effective lens.

Before installation or location realignment, the UI asks the session whether
the returned effect authority is current. Rendering may schedule later focus
and status-announcement callbacks; each callback repeats the authority check at
execution time. Validation performed for installation or realignment is not
continuing authority for a later effect. A callback that finds stale or foreign
authority changes neither focus, visible status, active panel, canonical URL,
nor history.

The persistent `dotnet-inspect` shell owns one polite live region outside
replaceable destination renderers, with `role="status"`, `aria-live="polite"`,
and `aria-atomic="true"`. It is mounted empty before a destination renderer and
survives renderer replacement; a current-authority announcement callback
changes its text only after any required installation. An applied result
announces the returned active subject and effective lens. An unavailable,
rejected, failed, or aborted result announces the same visible reason or
diagnostic shown by the surface. Superseded work reaches no consumer and
announces nothing. A no-effective-lens region remains visible content; the
shell live region announces its exact heading and evidence rather than a
different hidden explanation.

Receiving a `Synchronization required` result sets the session-scoped
synchronization-debt marker. After all required location-realignment,
installation, focus, and announcement effects complete, the UI acknowledges
the authority. A `Synchronization required` result cannot be acknowledged
merely because its snapshot equals locally rendered state: installation must
have completed under that result's exact current effect authority. Successful
acknowledgement is the only consumer action that clears the marker.

If authority becomes stale before completion, the UI abandons it. Abandoning a
`Synchronization required` result preserves the marker whether abandonment
occurred before installation or after installation but before acknowledgement.
A later current result may discharge the debt through its own disposition and
authority. Otherwise, once no current result can complete it, the
session-scoped consumer requests dedicated synchronization from Inspection
Subject Navigation. It tracks only the outstanding obligation, never the
product revision.

The consumer keeps at most one dedicated synchronization request outstanding,
retaining its opaque request identity and originating destination lifetime
until the product settles it. Renderer replacement or destruction retires that
lifetime as a possible consumer but does not pretend that the product request
was cancelled. A response for a retired lifetime is recognized by its retained
request context and its authority is abandoned. Only after that request settles
may an outstanding marker issue a fresh request for the current lifetime. A
returned current semantic or maintenance authority must likewise settle before
another request is issued because acknowledgement of that result may discharge
the pending synchronization request. These consumer-side request rules do not
impose a product retry limit.

A session-scoped UI navigation consumer is the sole holder of returned
navigation authority and outlives individual routed and inspection surfaces.
When a navigation destination surface's renderer is replaced or unmounted, the
consumer abandons every returned authority associated with that lifetime before
discarding its callbacks. It also abandons a result that returns after its
destination was destroyed or remounted. A remounted surface has a new lifetime,
cannot consume callbacks from the destroyed one, and schedules a fresh
synchronization request when the marker remains set and no earlier request is
awaiting product settlement. Mounting and request creation are separate
consumer actions; the outstanding marker preserves the obligation between
them. Every admitted request retains its exact identity until product
settlement. Another abandonment preserves the marker and permits a later
request after the prior one settles; the UI imposes no retry ceiling.
Superseded work requires neither acknowledgement nor abandonment because the
product session discards it without publishing effect authority.

When current-authority installation replaces a destination renderer, the
consumer atomically abandons every other returned authority associated with the
outgoing lifetime before transferring the installing operation and its later
callbacks to the new lifetime. Replacement does not merely make old callbacks
eventually stale; it settles their authority before the old renderer is
discarded.

The common installation, focus, announcement, acknowledgement, abandonment,
and destination-lifetime obligations are modeled by
[`UiEffectLifecycle.tla`](models/inspect-web-navigation-consumer/UiEffectLifecycle.tla).
The model assumes that the product session supplies opaque authority, a
complete typed outcome, and the product-owned synchronization disposition,
then explores two bounded consumer operations across three mounted-surface
lifetimes so supersession, renderer replacement, destruction, remount, or a
dedicated synchronization response can intervene between every deferred
effect. It includes prerequisite abort, product-side discard of superseded
work, synchronization debt, bounded request identity, old-lifetime response
abandonment, and fresh remount requests without modeling product revisions. TLC
exhaustively checked 166,998 generated states and 117,928 distinct states at
depth 20. Separate mutation configurations produced a counterexample when
current-authority validation, disposition-driven installation,
install-before-focus ordering, deferred-focus separation, complete-effect
acknowledgement, acknowledgement debt clearing, destruction abandonment,
abandonment debt preservation, remount synchronization, request/lifetime
correlation, bounded request admission, persistent focus safety, or replacement
abandonment was removed. The model also requires every exact admitted
synchronization request to reach settlement. The dedicated recovery
configuration reached the complete trace in which an old-lifetime response is
abandoned before a fresh current-lifetime response is consumed. The
[model README](models/inspect-web-navigation-consumer/README.md) records the
tool versions, bounds, action coverage, mutation results, and deliberate
abstraction of exact browser-history classification and browser-trigger-specific
entry realignment. The named restoration and synchronization gates carry those
additional conformance claims. This proves the finite design model; the
implementation gates below establish conformance in Inspect Web.

### Shell and menu focus resolution

Activating an available item for a non-modal transition closes the menu. A
successful inspection transition focuses the returned active-subject
level-one heading; a successful routed transition focuses that surface's
level-one heading. An unavailable, rejected, failed, or aborted result that
retains the renderer returns focus to the stable menu-button invoker and makes
the outcome visible. When an unavailable result or another non-applied result
with `Synchronization required` installs a replacement renderer, its focus
effect resolves the corresponding coordinate or subject menu button in the new
lifetime when that target still represents the initiating action. Otherwise,
or when that control is not mounted, it focuses the new destination's level-one
heading, then the persistent `dotnet-inspect` shell control when no destination
heading is mounted. It never focuses the invoker node from the outgoing
renderer. Installation, focus, and announcement occur only while their
returned effect authority remains current.

Before an asynchronous transition or snapshot installation removes the focused
element, the UI synchronously parks focus on the persistent `dotnet-inspect`
shell control outside replaceable destination renderers. This applies to
closing a focused menu, dialog, or drawer, replacing a native Library `select`
with the custom listbox, and omitting a focused lens tablist after a
no-effective-lens result. This parking step reflects local surface cleanup, not
a product result.

Current effect authority is still required to move focus from that persistent
anchor to a result-derived destination. Installation that replaces a renderer
associates later callbacks with the newly mounted destination lifetime, never
the outgoing one. A replacement listbox receives focus only when the exact
previously focused Library identity survives; an omitted tablist moves focus to
the no-effective-lens heading. If work is superseded or its destination is
destroyed, focus remains on the mounted shell control rather than falling to
the document body.

Renderer replacement does not dismiss a surviving modal or move its contained
focus merely because its stored ordinary-dismissal target belonged to the
outgoing renderer. Before discarding that renderer, the UI atomically replaces
such a target with the newly mounted destination's level-one heading. If no
destination heading is mounted, it uses the persistent `dotnet-inspect` shell
control. A modal dismissal target never retains an element from a destroyed
renderer lifetime.

The coordinate and subject/hierarchy menus these rules resolve focus for are
defined by
[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md#coordinate-and-subject-menu-interaction).
The persistent shell control, modal dialogs, and routed-surface classification
these rules park focus on or move focus between are defined by
[Inspect Web Shell Interaction](inspect-web-shell-interaction.md).

### Workspace result focus

A successful Workspace entry action that opens an inspection surface focuses
the returned active-subject level-one heading. When the returned destination
remains in Workspace, focus moves to the returned active entry. If a closed
entry has no returned active entry, focus moves to the next rendered entry at
its former position, then the previous entry, then the Workspace heading.

A typed failure that retains the Workspace renderer keeps focus on the
invoking entry while surfacing and announcing the failure. If synchronization
replaces that renderer, the ordinary authority-validated replacement and
fallback rules in [Shell and menu focus resolution](#shell-and-menu-focus-resolution)
apply instead; focus never returns to an outgoing-renderer element.

Opening a demo from the Workspace is an explicit replacement action. The
consumer retains the source history entry while acquisition and destination
rendering are in flight, pushes the canonical Workspace destination only after
replacement succeeds, and leaves the source entry unchanged on failure.
Any ordinary inspection action that leaves the demo catalog, including scope,
Search, and loaded-package occurrence activation, follows the same push
classification so Back returns to the catalog. If a catalog-origin NuGet
package or Platform-library acquisition fails, the consumer restores the prior
Workspace and catalog, restarts derived Workspace occurrence discovery,
surfaces retry there, and returns focus to the stable Search control without
committing a destination.

### Package query entry and return

Package query's `/query` route is a full-bleed routed surface under
[Inspect Web Shell Interaction](inspect-web-shell-interaction.md)'s general
Back/Forward and focus-restoration classification. This UI owns the entry and
return specifics beyond that classification. Browser Back and Forward own
entry and return. Entry from Search returns focus to Search when it remains
rendered; entry from the application-scope strip returns focus to its Query
control. Either path falls back to the prior surface's level-one heading. The
route's visible `Back` action invokes the same history transition, falling back
to Home only when the route was loaded without an in-app predecessor. Each
query history entry carries its own predecessor identity and focus target in
session-only history state, so a later query route cannot change an older
entry's Back behavior.

Selecting Query without a new seed restores the current session request,
streamed rows, failures, and completion state. Selecting Workspace from Query
pushes the retained Workspace surface when one exists, so Back returns to the
unchanged query entry. A direct query visit with no retained Workspace renders
the Workspace control unavailable rather than routing its label to Home.
If the retained Workspace cannot be projected into a complete workspace URL,
the transition still pushes an active-package Workspace successor, preserves
the complete in-memory surface, and exposes the projection failure. Refreshing
that degraded successor restores only its represented active package; Back
still returns to the unchanged query entry.

`Open in workspace` commits its result through the same typed transition
lifecycle as any other product-issued outcome: success leaves `/query`, pushes
the returned Workspace location, and focuses the inspection destination;
failure keeps the query route, rows, and request intact and returns focus to
the invoking row action. The request's package-ID/version submission and
failure semantics are owned by
[Package Query Experience](package-query-experience.md#layout).

## Non-claims

This document does not render navigation descriptors, decide which subject or
lens is active, define selector-pill visual states, define shell modal
dialogs or routed-surface classification, or define page-level placement. It
proves only that returned product authority is consumed, installed,
acknowledged, or abandoned correctly, and that its required visible effects
occur in the defined order.

## Implementation gates

Before implementation claims this interaction contract, it must add and pass
these named Inspect Web tests. Descriptor-rendering and widget-focus gates for
this same test file are recorded in
[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md#implementation-gates):

- `navigation-consumer.test.ts`:
  `typed outcomes commit only returned state and release authority` covers
  applied, unavailable with and without a replacement snapshot, rejected,
  failed, and aborted results under both synchronization dispositions, plus
  product-side discard of superseded work. It proves that semantic outcome
  never substitutes for the disposition: every `Synchronization required`
  result installs its complete returned snapshot, while every `Current`
  non-applied result retains the installed snapshot. It proves exact canonical
  URL and history handling, including replacement for catch-up and
  reconciliation-driven installations. It also proves that such replacement
  resolves menu-result focus in the new renderer, with destination-heading and
  persistent-shell fallbacks, and announces through the pre-existing shell
  live region before acknowledgement or abandonment.
- `navigation-consumer.test.ts`:
  `synchronization catch-up replaces history without inventing navigation`
  covers rejected, failed, aborted, and unchanged-unavailable results carrying
  `Synchronization required`, plus a dedicated synchronization result. It
  proves that each installs the complete product snapshot and replaces rather
  than pushes or adopts history. The non-applied cases retain their exact
  semantic evidence; the dedicated result introduces no semantic navigation
  outcome.
- `navigation-consumer.test.ts`:
  `abandoned synchronization debt requests fresh authority after remount`
  abandons synchronization-required authority before installation and after
  installation but before acknowledgement. It proves that neither path clears
  the session-scoped marker, that remount requests dedicated synchronization,
  and that only one product request remains pending across renderer replacement
  or destruction. A late response retains its exact request identity and old
  lifetime, is abandoned rather than consumed by the remounted destination, and
  must settle before the current lifetime issues another request. Repeated
  abandonment permits later requests without a UI retry ceiling. It also proves
  that remount and request creation are separate actions, the outstanding
  marker survives between them, and every exact issued request reaches
  settlement. A newer current semantic result may instead discharge the same
  obligation through its returned disposition. A Back or Forward traversal
  started after a request makes the request's result foreign to the selected
  entry, so it is abandoned without a history write.
- `navigation-consumer.test.ts`:
  `maintenance results honor synchronization disposition without stealing
  focus` covers authority validation, no snapshot or history write for
  `Current`, canonical URL replacement for `Synchronization required`,
  selective announcement, focused-element removal, surviving-modal
  dismissal-target replacement, and acknowledgement. The modal retains
  contained focus during renderer replacement, then ordinary dismissal reaches
  the new destination heading rather than an outgoing-renderer element.
- `navigation-consumer.test.ts`:
  `deferred effects revalidate authority when each callback executes`
  supersedes a result after installation and proves that its queued focus and
  announcement callbacks have no visible effect or history mutation.
- `navigation-consumer.test.ts`:
  `stale explicit authority cannot install returned state` returns an applied
  explicit result, begins a newer intent before installation, and proves that
  the stale result changes no rendered snapshot, canonical URL, history entry,
  focus, or shell announcement before its authority is abandoned.
- `navigation-consumer.test.ts`:
  `non-installing browser restoration realigns the selected entry` exercises
  Back and Forward with `Current` unavailable-without-replacement, rejected,
  failed, and aborted outcomes. Each case retains the snapshot, replaces the
  browser-selected entry with its canonical location, surfaces and announces
  the outcome, focuses the retained destination heading or shell fallback, and
  pushes no entry. Companion `Synchronization required` cases instead install
  the complete returned snapshot and replace the selected entry with its
  canonical location before presenting the same semantic evidence. The gate
  also supersedes each returned authority before realignment or installation
  and proves that stale work changes neither canonical URL nor history,
  produces no later focus or announcement, and is abandoned before it can be
  acknowledged.
- `navigation-consumer.test.ts`:
  `superseded restoration cannot strand the browser-selected entry` covers a
  restoration superseded before product result publication, after authority
  return but before realignment, and by a newer browser traversal. A
  non-browser successor first repairs the unresolved selected entry before
  submission; a browser successor replaces the traversal serial. In every case
  a non-installing current result leaves the address bar and selected entry
  aligned with the installed snapshot while superseded work writes nothing.
- `navigation-consumer.test.ts`:
  `acknowledgement follows every required visible effect` proves that
  location realignment, installation, focus, and announcement complete before
  acknowledgement whenever each effect is required. For `Synchronization
  required`, it additionally proves that equal local snapshot contents do not
  permit acknowledgement until the complete result has been installed under
  that exact current authority.
- `navigation-consumer.test.ts`:
  `surface destruction abandons authority and suppresses stale callbacks`
  destroys and remounts a surface before its callbacks execute, then returns a
  late result for the destroyed lifetime. A companion `Current` non-installing
  Back/Forward case destroys the destination before location realignment and
  proves that remount repairs the preserved traversal obligation before
  rendering, independently of synchronization debt.
- `navigation-consumer.test.ts`:
  `renderer replacement abandons outgoing authority before transfer` holds old
  returned authority while a current installation replaces the renderer and
  proves that no outgoing-lifetime authority survives replacement.
- `navigation-focus.test.ts`'s
  `lens tabs and Library options separate focus from committed selection` is
  defined in full by
  [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md#implementation-gates).
  Its result-authorized focus and outgoing-renderer-invoker assertions also
  gate this document's focus-order claim.

The implementation fixture supplies typed product results through the normal
navigation-consumer boundary. It does not construct a parallel host catalog or
bypass effect-authority validation merely to observe the renderer.

These gates are not implemented by this documentation-only design. Until they
exist and pass, the prose and TLA+ model define the target contract but do not
claim Inspect Web implementation conformance.

## Acceptance scenarios

An implementation claiming this redesign is complete must satisfy these
outcomes.

### Workspace focus acceptance

1. Activate a Workspace entry whose successful outcome opens an inspection
   surface and confirm that focus reaches the returned active-subject heading.
2. Activate an entry whose successful outcome remains in Workspace and confirm
   that focus reaches the returned active entry.
3. Close an entry with no returned active entry and confirm focus moves to the
   next rendered entry at its former position, then the previous entry, then
   the Workspace heading.
4. Supply a typed failure that retains the Workspace renderer and confirm that
   focus remains on the invoking entry while the failure is surfaced and
   announced.
5. Repeat with a synchronization-required result that replaces the renderer
   and confirm that focus resolves only within the new lifetime under current
   effect authority.

### Transition effects and surface lifetime

1. Return an applied outcome carrying a replacement snapshot and confirm that
   installation atomically updates rendered state, canonical URL, and the
   initiating action's push-or-replace history classification.
2. Hold stale returned authority from an older intent while the current applied
   installation replaces the destination renderer. Confirm that replacement
   abandons the outgoing authority before transferring the current operation
   and its callbacks to the new lifetime.
3. Return an unavailable outcome whose refreshed or reconciled snapshot changes
   the active subject. Confirm that it installs the exact returned snapshot but
   replaces history rather than pushing the unrequested subject change. Confirm
   that focus reaches the corresponding menu button in the new renderer, or its
   destination-heading or persistent-shell fallback, and never the outgoing
   invoker node. Confirm that its deferred announcement changes the pre-existing
   shell live region after installation rather than inserting an already-filled
   destination-owned region.
4. Return `Current` unavailable-without-replacement, rejected, and failed
   outcomes. Confirm that each retains the prior snapshot, URL, and history
   while presenting its exact evidence.
5. Confirm that authority is validated before installation and independently
   inside each deferred focus and polite-live-region callback.
6. Supersede an applied result after installation but before its callbacks
   execute.
7. Confirm that focus was parked before its invoking control disappeared, that
   neither stale callback changes focus, status, active panel, URL, or history,
   and that the stale authority is abandoned.
8. Fail a prerequisite before navigation can run and return a `Current` aborted
   effect. Confirm that it retains snapshot, URL, and history, presents and
   announces its typed failure, then acknowledges its current authority.
9. Complete older work after a newer intent owns the session and confirm that
   the product discards it without publishing a consumer result, authority,
   announcement, URL change, or history change.
10. Keep a modal open while maintenance replaces the destination renderer.
   Confirm that focus remains contained in the modal, its outgoing-renderer
   dismissal target is replaced with the new destination heading, and ordinary
   dismissal never focuses a detached element.
11. Return another result and confirm that acknowledgement occurs only after its
   required installation, focus, and announcement effects complete.
12. Install a maintenance snapshot and confirm that it replaces URL history,
   does not move surviving focus, announces only a visible change, and
   acknowledges its authority.
13. Destroy a surface while it holds unconsumed authority, then remount the
    same surface kind and return another result for the destroyed lifetime.
14. Confirm that destruction and the late return both abandon authority and
    that callbacks from the prior lifetime cannot affect the remounted surface.
    Repeat after a `Current` non-installing Back or Forward result returns but
    before its location realignment. Confirm that remount preserves and repairs
    the unresolved traversal before rendering even though no synchronization
    debt exists.
15. Return an applied explicit result, begin a newer intent before installation,
    and confirm that the stale result changes no rendered snapshot, canonical
    URL, history entry, focus, or shell announcement before abandonment.
16. Leave the consumer behind the retained product session, then return
    rejected, failed, aborted, and unchanged-unavailable results with
    `Synchronization required`. Confirm that each installs the complete returned
    snapshot, replaces history without recording the unsuccessful request,
    presents its exact semantic evidence after installation, and acknowledges
    only after every required effect.
17. Install a synchronization-required result and abandon it before
    acknowledgement. Remount the destination and confirm that the
    session-scoped consumer retains any already-pending request's identity and
    old lifetime rather than issuing a second request. Return that old response
    and confirm that it is abandoned and settled before the current lifetime
    separately issues fresh synchronization, installs the complete current
    snapshot under fresh authority, replaces history, and acknowledges. Confirm
    that every exact issued request reaches settlement. Repeat abandonment and
    confirm that another request remains possible. Start Back or Forward after
    a request but before its result and confirm that the foreign result is
    abandoned without changing the newly selected entry.
18. While synchronization remains outstanding, return a newer current
    maintenance or explicit result. Confirm that its disposition controls
    installation and that acknowledgement of that current authority discharges
    the same obligation without waiting for the dedicated response.

### Canonical adapter

1. Supply a projectable outcome containing an opaque packet and package courtesy
   identity and confirm that the UI composes both query fields with the
   transition's push, replace, or adopt history classification.
2. Supply a projectable non-package outcome and confirm that the UI composes
   only `w` without placing a local coordinate in readable URL state.
3. Starting from a projectable location, supply a non-projectable outcome and
   confirm that explicit Share presents the owner-issued reason and that the
   location removes the stale `w` and package courtesy fields.
4. Supply a failed outcome and confirm that the prior workspace and URL remain.
5. Confirm that route preflight, refresh, and shared-link activation never parse
   compact packet fields or use the readable package courtesy identity as a
   fallback workspace.

### Browser history

1. Activate a coordinate, navigate to a Type, and select Source.
2. Change several result filters.
3. Use Browser Back and confirm that it returns to the prior pushed subject or
   lens state rather than stepping through each filter change. Confirm that the
   restored snapshot adopts the browser-selected entry without a history write.
4. Use Browser Forward and confirm that it restores the Source state with its
   latest replaced refinements without adding or replacing an entry.
5. Refresh that location and open it as a shared link. Confirm that exact
   restoration adopts the current entry, while a changed unavailable or
   reconciled restoration replaces that entry with its returned canonical
   state.
6. Use Back and Forward with `Current` unavailable-without-replacement,
   rejected, failed, and aborted restoration outcomes. Confirm that each retains
   the rendered snapshot, replaces the browser-selected entry with the retained
   canonical location, surfaces and announces the outcome, focuses the retained
   destination heading or shell fallback, and lets a later traversal continue
   past the failed location without a pushed entry. Repeat with
   `Synchronization required` and confirm that the complete returned snapshot
   is installed and its canonical location replaces the selected entry before
   the same semantic evidence is presented.
7. Supersede a Back restoration before product result publication, then repeat
   after authority returns but before realignment. Follow each with a
   non-browser action whose current result does not install. Confirm that the
   selected entry was repaired before successor submission, the retained
   snapshot and address remain aligned, and superseded work writes nothing.
8. Supersede a Back restoration with Forward, then return a non-installing
   outcome for the current Forward restoration. Confirm that only the newest
   traversal serial may realign the selected entry.
