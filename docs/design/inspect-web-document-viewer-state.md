# Inspect Web document viewer state

## Owned claim

The package document modal has one feature-owned state value. Its variants
make closed, pending, available, and failed presentation states distinct and
carry only the request and result evidence valid in each state.

This document owns that modal state and its publication contract. Package
document acquisition, Markdown sanitization, workspace navigation, and modal
placement remain unchanged.

## State contract

The state has four variants:

- `closed` carries no request or result;
- `loading` carries the package document request;
- `ready` carries that request, rendered body HTML, and optional projected
  frontmatter; and
- `failed` carries that request and the failure text, including an empty
  string.

Only the current loading state may publish success or failure. Closing the
modal makes it closed and prevents an earlier completion from reopening it.
Replacing the request installs a different loading state, including when its
coordinates equal the earlier request. Ready and failed are settled states.

The loading state is the feature's publication receipt at document
acquisition, body rendering, and inline frontmatter rendering. A workspace
snapshot does not retain its pending promise, so snapshot capture converts
loading to failed while retaining the request. An empty current failure or
captured loading state renders `The document could not be loaded.` rather than
a success-shaped empty article.

## Composition and boundaries

`document-inspection.ts` owns transitions, snapshot settlement, and the typed
open-state predicate. `dotnet-inspect.ts` retains package-document validation,
modal placement, focus, keyboard, workspace snapshot, engine, and sanitized
Markdown composition, consuming those owned operations rather than
reconstructing lifecycle meaning. `doc-viewer.ts` renders an already-open state
exhaustively.

This slice does not create or replace logical operation authority. It does not
convert Graph Source, Member Source, Type Source, package lenses, Spotlight,
runtime-pack or package-version requests, top-level view, or workspace
selection. It introduces no generic state framework, reducer, generated wire
union, or source-code inventory gate.

## Adoption and evidence

[Issue #6422](https://github.com/richlander/dotnet-inspect/issues/6422)
tracks one production adoption step: use the union in the existing inspect-web
document coordinator, workspace snapshot normalization, composition root, and
modal renderer. No second host or shared substrate is introduced.

`test/document-inspection.test.ts` gates close and replacement invalidation,
stale completion suppression at every asynchronous stage, settled success and
failure, and frontmatter projection. `test/doc-viewer.test.ts` gates exhaustive
visible outcomes and fallback presentation. The focused composition and
workspace snapshot tests gate typed open-state use and loading-snapshot
settlement. Frontend type checking rejects consumers that use document, body,
frontmatter, or error evidence without narrowing the state.
