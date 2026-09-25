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

`PackageEndpointScope.OpenAsync` is host-neutral, in
`DotnetInspector.PackageQueries` beside the House Root contribution adapter it
consumes; `DotnetInspector.Queries`, which owns the package Root, cannot
reference that adapter. It takes:

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

The caller also supplies the demand; the scope doesn't infer it from the
views a command renders. Today the CLI passes `Surface` for the API views and,
after step 4, `SurfaceAndImplementation` for the body views. Once analysis
[operation participation](https://github.com/richlander/dotnet-inspect/pull/8548)
lands, the demand should follow from the universe requirements of the selected
analyses, lowered by [package read demand](package-read-demand.md). The scope's
input doesn't change when that happens.

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
   is built in PackageQueries. Library API Diff, the default view of CLI
   pairwise `diff --package`, moves onto it with `Surface` demand, ranged.
   Each endpoint opens one scope, and the comparison runs over the two
   participants. This slice keeps byte-identical parity with the legacy path,
   so the CLI admits a request only when:
   - both versions are exact, `--tfm` names one framework, and the host is
     online;
   - the request selects Library API Diff: no member target, no Finding, and
     no section but Changes; and
   - for each endpoint, the legacy selector applied to the archive directory
     picks exactly the scope's surface assets, and that is one Library.

   Before any read, the CLI checks the local caches the legacy path answers
   from with no network request: the application cache and a NuGet
   global-packages folder. The check reuses the legacy extraction's own cache
   lookup (`PackageExtractor.IsExactPackageCachedAsync`) and makes no request.
   If both endpoint coordinates are cached there, the request takes the
   legacy path. The House authority stores read neither cache yet.

   Library API Diff resolves no generic constraints on either path, so its
   parity doesn't depend on the host. Every other request, including API
   changes and API Finding Transitions, takes the legacy path unchanged,
   decided before any package read. Both endpoints' admission checks run
   concurrently to completion, so both directories reach the entry cache and
   a repeated request makes no package request. Only then do both surfaces
   realize concurrently, and a realization that fails cancels the other.

   The selector rule is decided from the archive directory before any
   surface folder is read. The House reads a directory by range only for a
   Realize or a document demand, so the check is a ranged Acquire whose
   document demand names the nuspec. That demand also reads the archive's
   root folder. The check's plan has a zero size cut, so it reads by range
   whatever the archive's size, and a fallback below the cut never downloads
   an archive the legacy path downloads again. This is a transitional
   admission cost, and it retires with the legacy selector in step 4. It
   holds for every size. Measured on nuget.org:
   - A fallback pays, per endpoint and on top of main, the size probe, one
     directory tail (65,557 bytes), and the root folder (about 17 KB for
     `Avalonia`).
   - An admitted request pays, per endpoint and on top of a Realize alone,
     a second directory tail (65,557 bytes) and the root folder (about 2 KB
     for `System.Text.Json`). That is one more request round before the
     Realize, which rereads the cached directory's tail.
   - Below the size cut, the Realize finds the check's cached directory and
     reads its surface by range too, not the complete archive. For a very
     small archive the two directory tails outweigh the saving: `MediatR`
     12.2.0..12.4.1 receives 383 KB cold instead of main's 195 KB, while
     disk falls from 0.70 MB to 0.41 MB. A fallback below the cut, such as
     `NUnit` 4.1.0..4.2.2 (two Libraries), receives 1.67 MB instead of
     1.44 MB and downloads each archive once.

   Evidence for the selector rule: with `--tfm`,
   `TfmSelector.SelectAssembliesByTfmFromPackage`
   merges the `ref/<tfm>`, `lib/<tfm>`, and `tools/<tfm>` DLLs. For `Avalonia`
   11.3.14..12.1.2 with `--tfm net8.0`, that doubles every change row and adds
   an "incomplete: metadata inspection failed" note. Fixing it is an
   intentional change to single-surface output. By operator decision (Rich,
   2026-09-24), step 4 owns that fix, when the legacy endpoint path retires;
   this slice doesn't change it.
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
   - Prerequisite: before the legacy path retires, the
     [package cache policy](package-cache-policy.md) owner decides whether
     and how House authorities serve a package already in a NuGet
     global-packages folder. Step 2 defers to the legacy path when both
     endpoints are there, and this document doesn't decide it.
   - API changes and API Finding Transitions move with them, over the
     scope's merged surface, as an intentional output change. The legacy
     merged surface binds generic-constraint dependencies through
     `AssemblyDependencyResolver`, and its answer depends on the host and the
     machine. Platform-key references bind through the trusted platform
     assemblies under JIT, which include the tool's own closure. Under
     NativeAOT there are none, and those references bind through installed
     runtimes found from `DOTNET_ROOT` and standard locations. Other
     references also reach local NuGet caches. An unbound constraint becomes
     an inspection-failure row. For example, `NUnit` 4.1.0..4.2.2
     `--tfm net6.0 --type NUnit.Framework.Assert --finding api.member` is
     clean under JIT. Under NativeAOT with no installed runtime, it adds 4
     unbound-constraint rows and exits 1. The scope's surface drops those
     machine-dependent rows.
   - Step 2 first tried to admit these views while references stayed inside
     the endpoint and the trusted platform assemblies, and abandoned the
     rule. It had no NativeAOT answer, and it couldn't be decided before the
     surface read, so its fallbacks cost more than main. Cold under JIT on
     nuget.org:
     - `MediatR` 12.2.0..12.4.1 `--finding api.type`: 49 KB and 0.3 s more;
     - `Microsoft.Extensions.Hosting` 8.0.0..9.0.0 `--member`: 1.16 MB
       instead of 0.58 MB received, and 0.93 s instead of 0.64 s;
     - `Microsoft.CodeAnalysis.CSharp` 4.12.0..4.14.0 `--member`: 43.4 MB
       instead of 34.4 MB received, and 4.7 s instead of 3.8 s.
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
| 5. `diff --package ID@A..B` Library API Diff | output byte-identical to the legacy path; both endpoints read only their surface and root folders by range; API changes, API Finding Transitions, and a selector mismatch stay on the legacy path, and the first two read nothing by range | CLI harness, real assets `System.Text.Json` 9.0.0 and 10.0.0; `Avalonia` 11.3.14..12.1.2 for the selector fallback |
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
