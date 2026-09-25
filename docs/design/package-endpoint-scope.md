# Package endpoint scope

## Status, owner, and claim

This document is the normative owner for **opening one package version as an
inspection scope through the PackageHouse, in any host**. It serves
[#8386](https://github.com/richlander/dotnet-inspect/issues/8386), and moves
the remaining pairwise `diff` package endpoints onto the House. The operator
(Rich, 2026-09-24) directed: "move over the rest of diff to the modern infra"
and "Make sure that the result is host-neutral APIs we can share with the
website."

The claim:

> Given a PackageHouse, one exact package coordinate, one target framework,
> and one [asset demand](package-read-demand.md#asset-demand), a package
> endpoint scope realizes the package Root into an `InspectionWorkspace` and
> exposes the Root's surface participants, and its implementation participants
> when the demand includes them. The scope owns the realized resources until
> it is disposed. Every host opens an endpoint the same way, and each
> consumer's comparison or query runs over participants, never over file
> paths.

It consumes, and does not redefine:

- House realization and asset demand
  ([PackageHouse](package-house.md),
  [package read demand](package-read-demand.md));
- the package Root and its roles
  ([package asset-selection correspondence](package-asset-selection-correspondence.md));
- Workspace scope and expansion
  ([workspace scope and expansion](workspace-scope-and-expansion.md));
- every comparison contract, including the
  [Diff operation and subject-section adoption](command-transition-model.md#diff-operation-and-subject-section-adoption),
  [Library API diff](library-api-diff-presentation.md),
  [analysis diff](analysis-diff.md), and
  [implementation diff](implementation-diff.md).

## Basis

Three hosts open a package version for inspection, three ways:

| Host | Today | Acquisition |
| --- | --- | --- |
| CLI `find` exact-package search | `ConfiguredPackageSearchWorkspace` | House, ranged, `Surface` |
| Inspect Web package views and Compare | `BrowserPackageWorkspace.OpenScopeAsync` | House `Acquire`, complete |
| CLI pairwise `diff --package ID@A..B` | `ApiSurfaceEndpointResolver`, then `AssemblySetResolver` | legacy `PackageExtractor`, complete download, file paths |

All three end in an `InspectionWorkspace`, except CLI `diff`, which works on
extracted file paths. Its API views extract surfaces from paths, and its body
views open method bodies by path. Ranged House content has no files, so
`diff` can't read by range, and the browser can't reuse `diff`'s endpoint
code. The comparison itself is already shared:
`LibraryApiDiffInspection.Execute` diffs two assembly groups and participants,
and both the CLI and Inspect Web call it.

`diff --history` already acquires through House version cells, ranged, with
`Surface` for API findings ([package read demand](package-read-demand.md),
adoption step 7). A three-version `Avalonia` history fell from 38.1 MB to
6.0 MB received. A pairwise `diff --package` of the same package still
downloads both archives whole.

## Contract

### Opening

`PackageEndpointScope.OpenAsync` is host-neutral, in `DotnetInspector.Queries`
beside the package Root. It takes:

- a PackageHouse and a source operation lease;
- one exact coordinate, meaning a package ID and a normalized version;
- one target framework; and
- one asset demand, with optional
  [named implementation assemblies](package-read-demand.md#named-implementation-and-aligned-blocks).

It realizes the coordinate with a `Realize` operation carrying that demand,
binds the Root to the demand, and adds the Root to a Workspace the scope
owns. The result is either an open scope or a typed non-success:

- not found;
- no compatible target framework;
- a realization failure, carrying the House evidence; or
- a Workspace admission failure.

A non-success is never an empty scope.

The host supplies the House. It chooses ranged or complete access, the store,
and limits. For example, the CLI supplies ranged access with the per-authority
durable store, and Inspect Web supplies its browser store. The scope doesn't
choose acquisition policy.

### Participants

An open scope exposes:

- the surface participants: one per selected compile asset, in selection
  order, with each one's assembly group; and
- the implementation participants, when the demand is
  `SurfaceAndImplementation`: one per realized implementation asset, with its
  surface correspondence.

A consumer reads a participant through a callback that holds the group for
its duration, as `BrowserInspectionScope.UseSurfaceParticipant` does today.
Participants carry package provenance: ID, version, framework, and asset
path. Consumers that report provenance, such as PDB and SourceLink source
selection, read it from the participant and never from a file path.

### Versions

The scope takes exact coordinates. Host-level version selection runs before
opening, through the owner the host already uses: the
[Package Version Service](package-version-service.md) for floating versions,
and the host's range parser for `A..B`.

### Offline

A host whose House can't reach a source keeps its existing offline path. In
the CLI, that means answering from the local package cache through a complete,
filesystem-backed House store, as the search Root does today. The scope's
contract is the same online and offline. Only the House the host supplies
differs.

### Disposal

Disposing the scope releases the Workspace and the realized Root. Consumers
return detached results, meaning envelopes, documents, and projections, and
never retain a participant after the scope is disposed.

## Adoption

1. This document.
2. **The host-neutral scope and its first consumer.** `PackageEndpointScope`
   is built in Queries. The API views of CLI pairwise `diff --package` move
   onto it with `Surface` demand, ranged: Library API Diff, API changes, and
   Finding Transitions. Each endpoint opens one scope, and the comparison
   runs over participants.
3. **CLI `find` moves onto it.** The exact-package search opens its Root
   through `PackageEndpointScope`, and `ConfiguredPackageSearchWorkspace`
   keeps only host policy. Output must not change.
4. **The body views of CLI pairwise `diff` move onto it** with
   `SurfaceAndImplementation`: Analysis, Implementation, Complexity,
   Structural, PDB source, and the workspace implementation comparison.
   - Method bodies, dependency resolution, and PDB and SourceLink source
     acquisition read from participants.
   - The legacy `AssemblySet` endpoint path for `diff` then retires.
   - This also fixes a current gap: without `--tfm`, a package that ships
     `ref/` has its implementation compared against reference stubs.
5. **Inspect Web adopts it.** `BrowserPackageWorkspace.OpenScopeAsync` opens
   its package endpoints through `PackageEndpointScope`. It keeps its browser
   store and complete acquisition until Inspect Web adopts ranged access
   ([package cache policy](package-cache-policy.md#adoption), step 5). Compare
   and the package views then share one endpoint path with the CLI.
6. **Platform endpoints.** Platform `diff --platform` endpoints adopt the
   scope when platform lookups move onto the House. That migration belongs to
   [package-backed platform realization](package-backed-platform-realization.md#production-adoption-and-retirement).

`diff --library` compares local files and needs no scope.

## Pathological cases and gates

All gates run in Release.

| Case | Expected | Gate |
| --- | --- | --- |
| 1. A package endpoint opened with `Surface` | surface participants only; no implementation role | contract suite |
| 2. `SurfaceAndImplementation` | implementation participants with their surface correspondence | contract suite |
| 3. A coordinate that doesn't exist, or no compatible framework | typed non-success, never an empty scope | contract suite |
| 4. CLI `find` exact-package search, after step 3 | output byte-identical to before the move | existing `find` gates |
| 5. `diff --package ID@A..B` Library API Diff, API changes, and Finding Transitions | output byte-identical to the legacy path; both endpoints read only their surface folders by range | CLI harness, real asset `Avalonia` |
| 6. The same pairwise diff twice | the second makes no package request | CLI harness |
| 7. Offline pairwise diff with both endpoints cached | the same output from the local cache | CLI harness |
| 8. Inspect Web Compare, after step 5 | the same Library API diff result through the shared scope | Web boundary tests |

## Non-claims

This document does not:

- change any comparison, correspondence, History, Count, or rendering
  contract;
- change version selection or range parsing;
- choose a host's acquisition policy, store, or limits;
- move `diff --library` or platform endpoints; or
- add a Package comparison: pairwise `diff --package` remains a Library
  comparison whose endpoints packages supply.
