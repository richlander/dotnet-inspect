# Inspect-web background analysis

## Status and owner

This document owns **background analysis** in the website: Library and Type
analysis the page starts on its own, without a user action, to give a reader
immediate context. It owns the page-side queue, its order, relevance and
de-duplication, the foreground-first rule, and the default comparison
baselines. It is tracked by
[#9716](https://github.com/richlander/dotnet-inspect/issues/9716).

It consumes the single-threaded Worker contract in
[Inspect-web worker runtime](inspect-web-worker-runtime.md#speculative-worker-local-preparation):
the Worker gives background work no priority or preemption, so background
work must return to the Worker event loop under a feature-owned structural
bound. Each adopter's producer owns that bound; for Fast Diff it is
[Fast Diff steps](fast-diff.md#steps).

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
2. **Library baselines:** Fast Diff against the default baselines, in the
   order listed below.
3. **Library rankings:** whole-Library rankings such as structural salience
   and top leverage.

Library analysis tabs stay on demand and are not queued.

### Default baselines

A Gallery package's Library page compares its current version against:

1. **Last patch version**, both axes: the highest listed version below the
   current one with the same major and minor version. When there is none, as
   for an `X.Y.0` release, it is the highest listed version below the current
   one. Both cases are the inventory's existing `previousVersion`.
2. **Last major version**, API axis only: the highest listed version with a
   lower major version.

Prereleases are candidates only when the current version is a prerelease. A
baseline that resolves to the same version as an earlier one runs once. An
explicit Library Compare targets the single version the user selects and does
not change these defaults.

The background run acquires each baseline package without a user action. That
is an approved exception to explicit network work, limited to these baseline
packages.

### Navigation cues

Outside Library Compare, the Type navigation marks each Type that the
last-patch baseline reports with an axis `Changed` or `Indeterminate`:
**API differences** when the API axis is not `Unchanged`, otherwise
**implementation differences**. Both glyphs sit between comparison chevrons:
a lollipop, the provided-interface mark, for API, and `IL` for
implementation. Library Compare's API cue uses the same lollipop glyph. The
cue names the baseline version, and an `Indeterminate` axis is described as
undecided rather than changed. Inside Library Compare, cues come from the
Compare result for the user's target, as before.

A cue appears on a Type only when the navigation lists it, so a change
confined to internal Types is visible under the internal or all accessibility
filter, not the default public one. A failed baseline is visible in the Type
navigation status with a retry; a loading baseline shows nothing.

### Member cues and the Type on screen

Every Type cue leads to a view. When the selected Type has a cue, the Members
header names the change and opens Library Compare against the same baseline:
**Compare API** for an API cue and **Compare bodies**, Member Body Diff, for an
implementation cue.

For an API cue, the queue runs the Type-surface Library API Diff of that one
Type against the baseline as visible-subject work, and the member list and
member navigation mark each Member it reports changed with the API glyph. When
that diff reports no changed Member, the header says the change is in the
Type's declaration; when it fails, the header shows the failure with a retry.
Implementation cues are not placed on Members until Fast Diff has per-Member
states ([Fast Diff adoption](fast-diff.md#adoption) step 7); until then the
Members header's Compare bodies action is their view.

## Gates

- `background-analysis.test.ts`: order, foreground idleness before start,
  relevance before start and while running, and key de-duplication.
- `library-fast-diff.test.ts`: baseline request derivation, cue mapping per
  axis state, and failure status.
- `engine-worker-ordinary.test.ts`: a background binding does not count as
  Worker activity.
- `type-api-diff-cues.test.ts`: the one-Type API Diff request, held member
  presence, visible failure, and the Members header line for each cue state.

## Adoption

1. **Queue and Fast Diff last-patch baseline** (#9817): the queue, the
   background binding, and the navigation cues.
2. **Member cues for the Type on screen** (this slice): the first
   visible-subject task and the Members header's Compare actions.
3. **Library Compare lists and last-major baseline:** with
   [Fast Diff adoption step 5](fast-diff.md#adoption).
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
