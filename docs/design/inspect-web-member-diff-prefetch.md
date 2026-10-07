# Inspect Web Member diff prefetch

## Status and owner

This document owns **Inspect Web Member diff prefetch**: how the Browser caches
Fast Diff results and completed Member diffs, and whether and how it
speculatively computes Member diffs after a Type opens. It is tracked by
[#9716](https://github.com/richlander/dotnet-inspect/issues/9716).

dotnet-inspect builds robust, capable inspection features that provide
foundational capabilities or compelling experiences and are conventionally
sound, delightfully new or unique, or both. This policy serves that mission by
spending idle compute only where it measurably shortens the user's next action.

It consumes the typed state and cause from [Fast Diff](fast-diff.md) and the
Member Body result from [Member Body Diff](inspect-web-member-body-diff.md). It
does not define Fast Diff semantics, Member Body presentation, or the Compare
operation lifetime owned by
[Compare experience](inspect-web-compare-experience.md).

## Caches

- **Fast Diff results** are cached by exact pair identity, scope, and subject.
  The Library result is reused when the same Compare reopens.
- **Completed Member diffs** are cached in a bounded LRU keyed by exact Member
  identity together with the Member Body cache key inputs: ordered versions,
  framework and compile asset, Library identities, and mechanisms. A Member
  opened against a different baseline never reuses another baseline's diff.

A cache is an optimization. A miss recomputes and never changes a result.

## Speculative prefetch

When a Type opens, Browser runs the Fast Diff Type pass and marks Members. It
may then speculatively compute complete Member diffs, subject to all of these:

- only Members whose cause includes a confirmed `Body` difference are
  prefetched; API-only Members take the API diff path and need no prefetch;
- prefetch starts only when the host is idle, in priority order: the hovered or
  focused Member, then the remaining list, up to a fixed cap;
- user-initiated work preempts prefetch, and navigation cancels it; and
- each completed Member diff is one independent event, so streaming delivers
  real incremental results rather than replaying a finished array.

## Cancellation

Prefetch follows the Compare operation lifetime: it runs under the current
operation authority, and a stale completion changes no visible result.
Cancellation of the underlying computation is best-effort in that contract.
Prefetch must not make a prefetch-held single-threaded Wasm worker delay the
user's next action. Until the computation honors cancellation, the host must
either bound each prefetch unit so a stale unit finishes quickly, or not
prefetch. Making cancellation reach the computation is a prerequisite owned by
the Operation Authority and Compare owners, not assumed here.

## Adoption gate

Prefetch is not part of the first Browser slices. It is adopted only when
measurement shows it helps: member-open latency without prefetch is noticeable,
and with prefetch the next Member open is served from cache at a materially
lower time-to-first-diff, at an acceptable idle compute cost. The measured
quantities are time-to-first-diff on Member open with and without prefetch,
prefetch hit rate (the user opens a Member that was prefetched), and compute
spent on Members never opened. Measurements use NativeAOT-compatible product
paths and the repository performance evidence rules.

## Open design questions

- The prefetch cap and the idle-time definition for a Wasm host.
- Whether Operation Authority should gain cancellation that reaches the
  computation, and in which owner.

## Acceptance scenarios

1. Reopening a Compare or Type serves Fast Diff from cache with an identical
   result.
2. The same Member opened against two different baselines never reuses the
   first baseline's completed diff.
3. Navigating away cancels in-flight prefetch before the next user-initiated
   Member diff starts, or no prefetch unit outlives navigation by more than its
   bound.
4. An API-only Member is never prefetched.
