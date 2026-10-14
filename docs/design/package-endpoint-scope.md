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

> Given a PackageHouse, one package demand (an exact coordinate or a
> selection the House settles), one target context (an exact framework or the
> House's owner default), and one
> [asset demand](package-read-demand.md#asset-demand), a package
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
- one package demand: an exact coordinate, meaning a package ID and a
  normalized version, or a `Selecting` demand that the House settles;
- one target context: an exact framework, or `OwnerDefault`, under which the
  House applies its highest-available compile selection; and
- one asset demand, with optional
  [named implementation assemblies](package-read-demand.md#named-implementation-and-aligned-blocks);
  or one exact compile-Library selector, which PackageHouse resolves before
  acquisition and which cannot be combined with independently named
  implementation assemblies.

It realizes the package with a `Realize` operation carrying those demands,
binds the Root to the demand, and adds the Root to a Workspace the scope
owns. The result is either an open scope or a typed non-success:

- not found;
- no compatible target framework, or no compile selection under the owner
  default;
- a realization failure, carrying the House evidence; or
- a Workspace admission failure.

A non-success is never an empty scope.
When an exact Library selector has complete compile-selection evidence but
produces no admissible participant, the failure also retains the narrowed Root
contribution. A semantic operation can therefore preserve its own typed
missing, ambiguous, or participant-unavailable result without weakening the
scope invariant that every open endpoint has a surface participant.

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

The scope takes the House's package demand, so version and framework
selection stay in the House and behave as they do for `package`. An exact
coordinate passes through unchanged. `latest` is an `AlwaysLatest` demand that
the House settles through the
[Package Version Service](package-version-service.md), which always refreshes
it, and a `*` pattern is a `Wildcard` demand that the House settles by
discovery. The host's range parser still splits `A..B` into one demand per
endpoint.

### Offline

For an exact coordinate whose package is cached, the House opens the scope
offline from the host's stores without a network request: step 7's exact Type
inspection does this through the legacy-compatible authority store, and
durable authorities also read their own NuGet global-packages entries.

A floating version depends on its spelling. `latest` is an `AlwaysLatest`
selection, which always discovers, so it fails offline on the House route as
it does on the legacy route. A `*` pattern is a `Wildcard` selection, which the
[Package Version Service](package-version-service.md#adoption) doesn't serve
yet, so the House can't settle it offline. The legacy route can: it resolves a
pattern from version listings it cached within the last hour.

The CLI's other offline paths are host routes that don't use a House: `diff`
takes the legacy `AssemblySet` route through `PackageExtractor`, and the
search Root acquires through the Workspace loader with a filesystem package
store. Step 4 part 1 moves offline `diff` onto the scope, except a diff with
any endpoint that only discovery can settle, a `*` pattern or `latest`, which
keeps the legacy route as a whole, so a scope endpoint is never compared with
an `AssemblySet` endpoint. The legacy route answers a `*` pattern from its
cached listings and reports why `latest` can't be resolved offline, as
`package` does; the House route has no such answer for either, and surfaces
only the network guard. A `*` pattern moves when the House settles a
`Wildcard` selection offline, through the Version Service's adoption of
wildcard keys or from cached listing evidence. Until then, those requests also
keep the legacy selection and its machine-dependent constraint rows, which
step 4 part 2 removed from the scope route. The scope's contract is the
same online and offline; only the House the host supplies differs.

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
   lookup, which step 4 part 2 retired, and makes no request.
   If both endpoint coordinates are cached there, the request takes the
   legacy path. When this step landed, the House authority stores read
   neither cache; durable authorities now reuse their own global-packages
   entries, as the next step's prerequisite records.

   Library API Diff resolves no generic constraints on either path, so its
   parity doesn't depend on the host. Every other request, including API
   changes and API Finding Transitions, takes the legacy path unchanged,
   decided before any package read. Both endpoints' admission checks run
   concurrently to completion, so both directories reach the entry cache and
   a repeated request makes no package request. Only then do both surfaces
   realize concurrently, and a realization that fails cancels the other.

   The selector rule is decided from the archive directory before any
   surface folder is read. The House reads a directory by range only for a
   Realize or a file demand, so the check is a ranged Acquire whose
   file demand names the nuspec. That demand also reads the archive's
   root folder. The check's plan has a zero size cut, so it reads by range
   whatever the archive's size, and a fallback below the cut never downloads
   an archive the legacy path downloads again. This is a transitional
   admission cost, and it retired with the legacy selector in step 4 part 2. It
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
   2026-09-24), step 4 owns that fix, which landed in part 2; this slice
   doesn't change it.
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
   - Prerequisite, resolved: House authorities serve a package already in
     a NuGet global-packages folder when its metadata source resolves to
     the same durable authority, local or HTTP, as the
     [package source model](package-source-model.md) states. The global-packages
     folder has always been in scope. Step 2's deferral to the legacy path
     retired in part 2.
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
   - Step 4 lands in six parts, each independently mergeable. New shared
     pieces live in shared assemblies, not the CLI host, so the diff system's
     paired steps ([diff system](diff-system.md#adoption-and-retirement), step
     4) reuse them:
     1. **The House's selection, as `package` has it.** The scope request
        carries a PackageHouse demand and target context, as
        [Opening](#opening) and [Versions](#versions) state, so `diff`
        selects exactly as `package` does, and no consumer chooses a version
        or framework itself. A floating version (`*`, `latest`) is a
        `Selecting` demand that the House settles. Without `--tfm`, the
        target context is `OwnerDefault`, and the House applies its
        highest-available compile selection to each endpoint. When the two
        endpoints select different frameworks, the output names each
        endpoint's framework. For example, `System.Text.Json@10.0.0..11.0.0`
        compares 10.0.0 for `net10.0` with 11.0.0 for `net11.0` by default,
        and `--tfm net10.0` is required to compare both endpoints for
        `net10.0`, because an explicit target applies to both. The CLI
        supplies a legacy-compatible store, as `type` does, so the scope
        reads the app cache alongside the NuGet global-packages folder.
        Offline `diff` opens through the scope too, except a `*` pattern or
        `latest`, as [Offline](#offline) states. This part keeps step 2's
        admission rule, now over the settled version: a floating endpoint's
        directory check settles it once, and the Realize opens that exact
        version. Without `--tfm`, the rule compares the legacy no-target
        selector with the House's owner-default selection, until part 2
        retires it. Library API Diff names the endpoint frameworks on both
        routes, so the route doesn't change output; the other views name
        them when they move.
     2. **API views over a merged participant surface.** Every view that
        reads only the API surface opens its endpoints through the scope:
        Library API Diff, Changes and its filters, multi-Library packages,
        member targets, and analysis sets whose analyses take the API or
        API-attribute route. The CLI admits a request when every section it
        requests runs the API comparison alone, or when its analysis set
        selects only those analyses.
        - `PackageEndpointApiSurfaceQuery` merges every surface
          participant's surface into one endpoint surface, in selection
          order. It projects each participant within the scope's surface
          group, so generic constraints don't bind against installed runtimes
          or local package caches. The machine-dependent constraint rows
          drop out.
        - The scope's selection is authoritative, which is the selector fix.
          For `Avalonia` 11.3.14..12.1.2 `--tfm net8.0`, the surface is
          `ref/net8.0` alone, so no change row doubles and no ambiguous
          constraint binding fails. Without `--tfm`, the legacy selector's
          `tools/` preference gives way to the House's compile selection.
        - The admission rule, its directory check, the check's
          zero-size-cut House, and the cache deferral retire. Once the
          scope's output differs from the legacy path's by design, a
          fallback or a deferral to the legacy path would make the output
          depend on which archives are cached. Each endpoint's Realize
          settles its own demand, and a cached package opens through the
          legacy-compatible store with no request.
        - The other API views name the endpoint frameworks when they differ,
          as Library API Diff does, on both routes.
        - String literals read method bodies, so they move with the body
          views, as part 3 states.
     3. **Body views with `SurfaceAndImplementation`.** Analysis Diff,
        Implementation Diff, Complexity Context, Structural Context, and
        analysis sets that read method bodies open their endpoints with
        `SurfaceAndImplementation`. They read each endpoint's
        implementation participants rather than extracted files.
        - `PackageEndpointBodyQuery` hands both endpoints' implementation
          participants to one comparison while it holds their groups. Each
          participant comes with its image, a descriptor that reads that
          image, and a resolver that binds its references within the
          implementation group. The comparison returns materialized results.
        - Implementation participants are the package's runtime assets for
          the selected framework. A package that ships `ref/` compares its
          implementation, never its reference stubs, with or without
          `--tfm`. For example, `System.Runtime.CompilerServices.Unsafe`
          4.7.1..5.0.0 compares `lib/netcoreapp2.0` by default, where the
          legacy path compared the `ref/netstandard2.0` and
          `ref/netstandard2.1` stubs. With `--tfm netstandard2.0`, it
          compares `lib/netstandard2.0`, where the legacy path failed on its
          doubled `ref/` and `lib/` selection.
        - Method-body references bind through the group's binding policy,
          not through installed runtimes or local caches. The body views
          take the merged surface for member targets and inspection
          failures, so they drop the machine-dependent constraint rows and
          name the endpoint frameworks, as the API views do.
        - PDB source and the workspace implementation comparison keep the
          legacy path until parts 4 and 5. The workspace comparison serves
          Implementation Diff with one Type and one Member selector.
        - String literals keep the legacy path for now. Their comparison
          still reads extracted files, and its multi-Library form is in
          review. They move onto implementation participants before part 6.
     4. **PDB and SourceLink from participants,** with a demand for listed
        adjacent Portable PDBs and participant provenance.
     5. **The workspace implementation comparison and Annotated Source
        Diff** open their Roots from the scope, and a forwarded target is
        acquired through the House rather than a pinned extraction.
     6. **Legacy retirement.** The online legacy package route and the
        testing switch retire, and the parity gates are rewritten as scope
        gates. The offline `*` route retires once the House settles a
        `Wildcard` selection offline, as [Offline](#offline) states.
5. **Inspect Web adopts it.** `BrowserPackageWorkspace.OpenScopeAsync` opens
   its package endpoints through `PackageEndpointScope`. It keeps its browser
   store and complete acquisition until Inspect Web adopts ranged access
   ([package cache policy](package-cache-policy.md#adoption), step 5). Compare
   and the package views then share one endpoint path with the CLI.
6. **Platform endpoints.** Platform `diff --platform` endpoints adopt the
   scope when platform lookups move onto the House. That migration belongs to
   [package-backed platform realization](package-backed-platform-realization.md#production-adoption-and-retirement).
7. **Exact package Type inspection.** The cold shared exact-Type operation
   opens a `Surface` endpoint scope and executes the existing Metadata query
   against its admitted Root. CLI `type` supplies desktop House policy. Its
   complete authority store explicitly admits the exact configured source's
   legacy application-cache slot when that authority has a durable,
   credential-free identity; the strict authority store remains the default,
   and credentialed authorities never reinterpret a legacy slot.
   Inspect Web executes the same operation over its already-retained
   House-backed Root and assembly-role realization rather than reacquiring the
   package. When that realization contains other Roots, its retained adapter
   opens snapshot-backed references in a short-lived query group containing
   only the selected Root's surface participants, so unrelated Workspace
   package failures cannot alter the exact-Type projection. The detached
   `InspectionEnvelope<ExactTypeInspectionResult>` remains unchanged; raw
   `WorkspaceContextLoadOptions` are no longer an input to the completed
   operation.
8. **Exact package Library API inspection.** The eligible cold CLI
   `type --package ID@VERSION --library LIBRARY --tfm TFM` route plans one
   exact compile-Library `Surface` demand before acquisition and opens it
   through this scope. PackageHouse chooses cache, ranged entry acquisition,
   or complete fallback. The complete compile inventory and selector outcome
   remain attached to the Root while only the selected Library body becomes a
   surface participant. The detached Markdown, JSON, envelope, Share,
   diagnostics, and legacy configured-source identity remain unchanged.
   Namesake and First Library population semantics remain outside this step;
   once either produces one exact selector, it can converge on the same
   operation.

`diff --library` compares local files and needs no scope.

## Pathological cases and gates

All gates run in Release.

| Case | Expected | Gate |
| --- | --- | --- |
| 1. A package endpoint opened with `Surface` | surface participants only; no implementation role | contract suite |
| 2. `SurfaceAndImplementation` | implementation participants with their surface correspondence | contract suite |
| 3. A coordinate that doesn't exist, or no compatible framework | typed non-success, never an empty scope | contract suite |
| 4. CLI `find` exact-package search, after step 3 | output byte-identical to before the move | existing `find` gates |
| 5. `diff --package ID@A..B` Library API Diff | output byte-identical to the legacy path where both select the same assemblies; both endpoints read their archive directory, then only their surface folder by range, with no complete request | CLI harness, real assets `System.Text.Json` 9.0.0 and 10.0.0 |
| 6. The same pairwise diff twice | the second makes no package request | CLI harness |
| 7. Offline pairwise diff with both endpoints cached | the same output from the local cache; after step 4 part 1, an exact pin opens through the scope with no request, with and without `--tfm`, and `latest` and a `*` pattern keep the legacy route, which reports `latest` as `package` does and answers a `*` pattern from cached listings | CLI harness: exact pins with and without `--tfm` against a feed that rejects every request; the route for offline `latest` and `*` by the scope-selection theory |
| 8. Inspect Web Compare, after step 5 | the same Library API diff result through the shared scope | Web boundary tests |
| 9. Pairwise diff without `--tfm`, after step 4 part 1 | each endpoint selects the framework `package` selects for it, through the House's owner-default compile selection, and the output names each endpoint's framework when they differ | CLI harness, real assets `System.Text.Json` 9.0.0 and 10.0.0: `net9.0` against `net10.0` by default, each the framework `package` reports for that version, and both `net9.0` with `--tfm net9.0`. The operator's example, 10.0.0 against the 11.0 release candidate (`net10.0` against `net11.0`), is the same rule; it is checked live on nuget.org, not gated |
| 10. Floating pairwise diff, after step 4 part 1 | `*` and `latest` settle as `package` settles them, through the House's selecting demand, once per endpoint | CLI harness, `System.Text.Json` 9.0.0 and 10.0.0 with a prerelease above 10.0.0, so `*` (prerelease included) and `latest` (stable) settle differently; each settled version equals `package`'s for the same spelling |
| 11. API changes, API Finding Transitions, multi-Library packages, and member targets, after step 4 part 2 | the merged participant surface, read by range; output byte-identical to the legacy path where both select the same assemblies; `Avalonia` 11.3.14..12.1.2 `--tfm net8.0` reads only `ref/net8.0`, repeats a change row only for overloads, and completes without an inspection failure | CLI harness, real assets: `System.Text.Json` 9.0.0 and 10.0.0 API changes and Transitions; `NUnit` 4.1.0..4.2.2, two Libraries below the size cut, each archive read once, directory first; `Avalonia` 11.3.14..12.1.2 against the legacy oracle's repeated rows |
| 12. Body views, after step 4 part 3 | implementation participants, never reference stubs, with or without `--tfm`; output byte-identical to the legacy path where both select the same assemblies | contract suite for the body query; CLI harness, real assets: `System.Text.Json` 9.0.0 and 10.0.0 Analysis Diff, Implementation Diff, and a member-targeted body analysis set, read by range; `System.Runtime.CompilerServices.Unsafe` 4.7.1 and 5.0.0, which ship `ref/` and `lib/`, against the legacy oracle's reference stubs |
| 13. Legacy retired, after step 4 part 6 | no `diff --package` request reaches the `AssemblySet` endpoint path, except an offline `*` pattern until the House settles `Wildcard` offline | CLI harness |
| 14. Performance, every step 4 part that changes code | exact-head NativeAOT numbers against `main`, built on linux-x64 and run back to back under `perf-guard`, in the PR body and the session: cold and warm wall time, bytes received, and disk written | perf hosts, fixed pairs: `System.Text.Json` 10.0.0..11.0 with and without `--tfm`; `Avalonia` 11.3.14..12.1.2 `--tfm net8.0`; `NUnit` 4.1.0..4.2.2; `MediatR` 12.2.0..12.4.1; `Microsoft.Extensions.Hosting` 8.0.0..9.0.0; `Microsoft.CodeAnalysis.CSharp` 4.12.0..4.14.0 |
| 15. Exact CLI Library API inspection | unchanged detached output and typed missing/malformed outcomes; complete compile evidence with only the selected Library body materialized | Sections, Services, and CLI contract suites; real assets `System.Text.Json` 10.0.0, `Avalonia` 12.1.3, and `Microsoft.CodeAnalysis.CSharp` 5.9.0 |

## Non-claims

This document does not:

- change any comparison, correspondence, History, Count, or rendering
  contract;
- change version selection or range parsing: the House and the Package
  Version Service keep selection, and the scope only passes their demand
  through;
- choose a host's acquisition policy, store, or limits;
- move `diff --library` or platform endpoints; or
- add a Package comparison: pairwise `diff --package` remains a Library
  comparison whose endpoints packages supply.
