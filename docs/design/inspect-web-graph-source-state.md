# Inspect Web graph source state

## Owned claim

The Graph Source modal has one feature-owned state value. Its variants make
closed, pending, available, failed, and cancelled presentation states distinct,
and carry only the request and result evidence valid in each state.

This document owns that modal state and its publication contract.
[Inspect-web operation authority](inspect-web-operation-authority.md) owns
logical operation identity, replacement, cancellation, and current-view
admission. [Inspect Web presentation language](inspect-web-presentation-language.md)
owns Source provenance vocabulary and leaves Graph Source composition with the
modal. The source query, acquisition policy, and generated Browser transport
remain unchanged.

## State contract

The state has five variants:

- `closed` carries no request or result;
- `loading` carries the requested member coordinates and modal title;
- `ready` carries those coordinates, the title, and one source result;
- `failed` carries those coordinates, the title, and the failure text,
  including an empty string; and
- `cancelled` carries the coordinates and title of visible work that may be
  requested again.

Only the current loading state may publish success or failure. Closing the
modal makes it closed and prevents an earlier completion from reopening it.
Replacement or cancellation makes pending graph work cancelled before another
source surface takes ownership. Ready and failed are settled states.

Automatic loading is permitted only from cancelled. In particular, an empty
failure or missing source payload is still failed: it renders
`No source was returned.` and is not requested again on the next render.
Explicit user reload may reuse the request from any open state.

## Composition and boundaries

`source-inspection.ts` owns transitions and exposes narrow predicates for open,
reloadable, and request-carrying states. `dotnet-inspect.ts` retains modal
placement, focus, keyboard, snapshot, and engine composition, consuming those
predicates rather than reconstructing lifecycle meaning. `graph-source.ts`
renders an already-open state exhaustively.

The current graph and member callers still share the legacy Source singleton.
While that remains true, the current loading state is the graph feature's
publication receipt alongside the existing cross-source generation. This is
not an alternative logical-operation authority or a new cancellation
contract. The ordered member-plus-graph migration and singleton retirement
remain owned by
[the managed-operation bridge](inspect-web-managed-operation-bridge.md#source-host-adoption-and-retirement).

This slice does not convert member Source, Type Source, the document viewer,
package lenses, top-level view, or workspace selection. It introduces no
generic state framework, reducer, generated wire union, or source-code
inventory gate.

## Adoption and evidence

[Issue #6406](https://github.com/richlander/dotnet-inspect/issues/6406)
tracks one production adoption step: use the union in the existing inspect-web
source coordinator, composition root, and modal renderer. No second host or
shared substrate is introduced.

`test/source-inspection.test.ts` gates close and replacement invalidation,
stale completion suppression, settled empty failure or missing payload, and
cancellation.
`test/graph-source.test.ts` gates exhaustive visible outcomes and fallback
presentation. The focused composition tests gate snapshot settlement,
auto-load eligibility, focus and keyboard ownership, and delegation through
the existing coordinator. Frontend type checking rejects consumers that use
payload, error, or request evidence without narrowing the state.
