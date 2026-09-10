# Inspect Web Spotlight package search state

## Owned claim

Spotlight package discovery has one feature-owned result state. Its variants
make idle, pending, available, and failed results distinct and carry only the
request, cache, result, or failure evidence valid in each state.

This document owns that result state and its publication contract.
`spotlight.ts` owns editable query and scope input, result rendering, selection,
and keyboard interaction. The NuGet query, package ranking and acquisition,
workspace navigation, and mounted-surface refresh remain unchanged.

## State contract

The result state has four variants:

- `idle` carries no request or result;
- `loading` carries the requested query and an optional prior `ready` value;
- `ready` carries the successful query and its hits, including an empty list;
  and
- `failed` carries the attempted query and visible failure text.

The current loading object is the publication receipt. A completion may publish
only while that exact object remains current and the current trimmed input and
scope still admit its query. Input compatibility remains an independent gate:
changing input without scheduling another request still prevents stale
publication.

Successful results are the only cache. Starting another query retains the
prior ready value inside loading. Returning to that cached query or leaving
package-capable scope restores the retained value without another request;
without one, the state becomes idle. Short input and reset discard package
search state. Failure is settled and retryable and does not masquerade as a
successful empty result.

A workspace snapshot does not retain scheduled or in-flight work. Snapshot
capture therefore settles loading to its retained ready cache or to idle.
Restored state never remains loading without a publication owner.

## Composition and boundaries

`spotlight-package-search.ts` owns transitions, cache selection, snapshot
settlement, and narrow result/loading/error projections. `dotnet-inspect.ts`
retains editable Spotlight input and scope, workspace snapshot composition,
the NuGet query and debounce ports, result ranking, and mounted-surface refresh,
consuming those owned operations rather than reconstructing lifecycle meaning.
`spotlight.ts` consumes only loading and failure projections for presentation.

This slice does not create or replace logical operation authority. It does not
convert runtime-pack, .NET release, package-version, Graph Source, document
viewer, Member Source, Type Source, package-lens, top-level-view, or workspace
selection state. It introduces no generic state, operation, reducer, or cache
framework, generated wire union, or source-code inventory gate.

## Adoption and evidence

[Issue #6467](https://github.com/richlander/dotnet-inspect/issues/6467)
tracks this focused production adoption under the state-union program in
[issue #4570](https://github.com/richlander/dotnet-inspect/issues/4570).

The production path has one step: use the union in the existing Spotlight
package-search coordinator, workspace snapshot normalization, composition root,
result selection, and modal presentation. No second host or shared substrate is
introduced.

`test/spotlight-package-search.test.ts` gates debounce replacement, cache
retention, empty success, retryable failure, current-input and scope admission,
stale completion, reset, and snapshot settlement. The focused composition and
workspace snapshot tests gate narrow projection use and loading-snapshot
settlement. Frontend type checking rejects consumers that use query, cache,
hits, or error evidence without narrowing the result state.
