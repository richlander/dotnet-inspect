# Inspect-web background analysis

## Status and owner

This document owns **background analysis** in the website: Library and Type
analysis the page starts on its own, without a user action, to give a reader
immediate context. It owns the page-side queue, its order, relevance and
de-duplication, and the foreground-first rule. It runs the depth-1 step of the
comparison targets that [Browser Diff targets](inspect-web-diff-targets.md#diff)
owns. It is tracked by
[#9716](https://github.com/richlander/dotnet-inspect/issues/9716).

It consumes the single-threaded Worker contract in [Inspect-web worker
runtime](inspect-web-worker-runtime.md#speculative-worker-local-preparation):
the Worker gives background work no priority or preemption, so background work
must return to the Worker event loop under a feature-owned structural bound.
Each adopter's producer owns that bound; for Finding Diff Any it is [Finding
Diff Any steps](finding-diff-any.md#steps).

## Why

A Library page can show which Types changed since the last release, and later
which Types matter most, before the user asks. That context is worth
computing ahead of time only if it never slows what the user is doing. Each
background producer previously scheduled itself, so nothing ordered them and
nothing kept them behind foreground work as a group.

## Contract

> **Claim.** The page runs background analysis one task at a time, in a fixed
> order, only while no foreground Worker operation is outstanding. A task the
> reader has navigated away from is dropped before it starts and canceled
> while it runs. A task whose result is already held or in flight is not
> started again.

### Queue

The queue lives on the main thread, one per page. Only the page knows what is
on screen, so relevance is a page decision, and the Worker already owns
admission and cancellation for each operation.

A task carries a de-duplication key, an order class, a relevance predicate,
and a start function that returns a cancelable run. The queue:

- starts the queued, still-relevant task with the lowest order class, oldest
  first within a class;
- waits for foreground idleness before each start and checks relevance again
  after waiting;
- re-checks relevance whenever the page reconciles, dropping queued tasks that
  are no longer relevant and canceling a running one; and
- ignores a key that is queued, running, or held by its adopter.

A running task is not preempted by a later, higher-order task: background
producers yield the Worker under their structural bound, so the cost of
finishing one is bounded and no work is discarded.

### Foreground first

A Worker operation is foreground unless its client binding declares it
background. Background operations do not count toward Worker activity, so
foreground idle-gated work, such as Type heat and method leverage, is not
delayed by a running background task. A binding may be declared background
only when its producer yields the Worker under a structural bound.

### Order

1. **Visible subject:** cues for the Type on screen.
2. **Library baselines:** Finding Diff Any against the default baselines, in the
   order listed below.
3. **Library rankings:** whole-Library rankings such as structural salience
   and top leverage.

Library analysis tabs stay on demand and are not queued.

### Default baselines

A Gallery package's Library page runs, for each checked scenario among the
Package's [comparison targets](inspect-web-diff-targets.md#diff), that
scenario's depth-1 step, whatever depth the reader has chosen. With both
scenarios checked, as by default, that is two comparisons:

1. **Supply chain**, both axes: the active version against the highest
   candidate below it, the automatic previous version that
   [Browser Diff targets](inspect-web-diff-targets.md#specific-version)
   defines once.
2. **API evolution**, API axis only: the active version against the
   projection's representative of the highest major below it.

The [version window projection](version-resolution.md#version-window-projection)
selects both points. A point that both windows name, as for an `X.0.0`
release, is acquired once. An explicit Library Compare targets the versions
the user selects and does not change these defaults.

The background run acquires these packages without a user action. That is an
approved exception to explicit network work, limited to the depth-1 steps.
The rest of a deeper window, and a Specific version, run and acquire only when
the reader enters Compare; choosing them does not.

### Navigation cues

Outside Library Compare, the Type navigation marks each Type that the last
step of the Supply chain window, the active version against the automatic
previous version, reports with an axis `Changed` or `Indeterminate`. When
Supply chain is unchecked, the navigation shows no cue. Cues over a whole
window belong to [#9920](https://github.com/richlander/dotnet-inspect/issues/9920).
The cue names the baseline version and each reported axis, and an
`Indeterminate` axis is described as undecided rather than changed. Inside
Library Compare, cues come from the Compare result for the user's target, as
before.

Change glyphs have two levels: a Type's glyph says that something changed,
and a Member's glyph says what changed. Every Type cue, inside or outside
Library Compare, is the change glyph, two opposing arrows, whichever axes are
reported. A Member cue places a specific glyph between comparison chevrons: a
lollipop, the provided-interface mark, for API, and `IL` for implementation.
A Member with both API and implementation differences shows the change glyph.

A cue appears on a Type only when the navigation lists it, so a change
confined to internal Types is visible under the internal or all accessibility
filter, not the default public one. A failed baseline is visible in the Type
navigation status with a retry; a loading baseline shows nothing.

### Member cues and the Type on screen

Every Type cue leads to a view. When the selected Type has a cue, the Members
header names the change and opens Library Compare against the same baseline:
**Compare API** for an API cue, **Compare bodies** (Member Body Diff) for an
implementation cue, and both when both axes are reported.

For either reported axis, the queue runs `queryTypeFindingDiffAny` as
visible-subject work. It compares only the selected Type through the shared
Type query, using the same exact implementation counterparts as the Library
baseline. It does not request the whole-Library API diff. Both endpoint Member
fingerprints are retained in the result; the current Members list and Member
navigation join only the current-side fingerprint.

A Member with an API change has the API Member glyph, one with a Body change
the implementation glyph, and one with both the change glyph. An
`Indeterminate` state also places the relevant glyph and says that axis is
undecided. Group rows combine the states of their overloads. Residual changes
remain in the Members header, with the matching Compare actions; they never
manufacture a Member cue. A failed comparison remains visible with Retry.
Explicit Library Compare keeps its complete comparison's cues.

The selected Type may finish rendering before version inventory arrives.
Inventory completion reconciles the background queue even when it updates
only the version controls, so baseline and Member cues do not require another
navigation or render to start.

## Gates

- `background-analysis.test.ts`: order, foreground idleness before start,
  relevance before start and while running, and key de-duplication.
- `library-finding-diff-any.test.ts`: baseline request derivation, cue mapping
  per axis state, and failure status.
- `engine-worker-ordinary.test.ts`: a background binding does not count as
  Worker activity.
- `type-finding-diff-any.test.ts`: the one-Type API Diff request, held member
  presence, visible failure, and the Members header line for each cue state.

## Adoption

1. **Queue and Finding Diff Any last-patch baseline** (#9817): the queue, the
   background binding, and the navigation cues.
2. **Member cues for the Type on screen** (#9826): the first
   visible-subject task and the Members header's Compare actions.
3. **Library Compare lists and the API evolution default window:** with
   [Finding Diff Any adoption step 5](finding-diff-any.md#adoption).
4. **Type heat and method leverage:** move their pumps onto the queue's
   visible-subject class and retire their own pumps.
5. **Library rankings:** structural salience and top leverage, after their
   caching in [#9793](https://github.com/richlander/dotnet-inspect/issues/9793).

## Non-claims

- The queue does not preempt a running task; it relies on each producer's
  structural bound.
- It does not cache across page loads; that is
  [#9793](https://github.com/richlander/dotnet-inspect/issues/9793).
- Platform and uploaded Libraries have no version inventory and have no
  default baselines.
