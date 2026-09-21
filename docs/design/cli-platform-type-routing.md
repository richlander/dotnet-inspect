# CLI Platform type routing

## Owner and claim

This document owns the desktop CLI composition that adapts one versionless
bare type or member target to the target-bound Platform type catalog.

> The CLI selects one explicit dotnet hive, authorizes the named runtime
> family-default policy, obtains one complete resource-free Platform type
> catalog, and projects only a resolved structured catalog entry to an
> explicit downstream command route.

[PlatformHouse reference processing](platform-house-reference-processing.md)
owns target selection, source settlement, complete reference-population
realization, and the catalog's correspondence to that population.
[Platform type catalog query](platform-type-catalog-query.md) owns user-text
normalization, matching, preference, and typed lookup outcomes. This owner
does not reinterpret either contract.

## Input and route result

The path accepts:

- one bare type-or-member token;
- the caller's configured package-source options;
- one CLI command context; and
- the command cancellation token.

It runs only when the caller supplied no explicit package, platform, project,
library, or framework source. Explicit `--framework runtime@version` remains
an exact route and bypasses the versionless policy in this slice.

The host adapter returns one resource-free catalog outcome:

- `Completed`, retaining the exact completed catalog;
- `Unavailable`, when no usable desktop dotnet hive exists;
- `Rejected`, when the House or catalog owner rejects the request;
- `Incomplete`, when finite work prevents a complete result; or
- `Failed`, when source work or authority retirement fails.

The router applies `PlatformTypeCatalogQuery` to the complete catalog. A
resolved entry projects:

- the declaration's structured type name;
- the exact managed assembly identity carried by its API content; and
- the catalog's settled runtime version.

The downstream route is therefore explicit:

```text
type|member <structured type> --platform <assembly> \
  --framework runtime@<settled version>
```

No assembly display-name prefix is treated as identity. Ambiguous or rejected
query outcomes fail visibly. `Missing` is valid only because the catalog is
complete; it permits the router to continue ordinary non-runtime
classification.

## Desktop source composition

The CLI chooses at most one installed dotnet hive. `DOTNET_ROOT` wins when it
contains a `packs` directory. Otherwise the CLI derives the hive containing
the current CoreCLR runtime and accepts it only when that hive contains
`packs`. The installed source receives that exact path and performs no ambient
root search.

When an installed hive exists, the family-default policy uses:

1. installed all-framework target discovery as the preferred stage; and
2. configured Package Source discovery for exact `net10.0` as fallback.

When no installed hive exists, the policy omits the preferred stage and uses
the same package-backed fallback directly. The immutable policy:

- selects `DotNetRuntime`;
- uses stable `10.0.1` as the minimum installed version;
- accepts installed previews and release candidates at or above that SemVer
  floor; and
- selects only the latest stable `10.0.x` package-backed target.

The reference source plan uses installed realization before package-backed
realization. Package discovery and acquisition are lazy: an installed success
does not invoke either package operation. Configured source options are passed
unchanged to the package authorization owner.

## Finite work and cancellation

The CLI grants explicit finite ceilings for source operations, target
candidates and comparisons, assemblies, aggregate bytes, retained type
declarations, and total duration. The complete population and catalog owners
enforce those limits; the router adds only a bounded right-to-left scan of
possible type/member boundaries.

Cancellation remains `OperationCanceledException`. It is observed by target
selection, source realization, catalog derivation, every catalog query, and
authority retirement.

## Ownership and terminal precedence

Completed population realization transfers one Library owner per member and
one adjacent Artifact session. The CLI:

1. derives the catalog while those authorities are live;
2. retains only resource-free catalog and query evidence;
3. starts Artifact retirement;
4. retires every Library owner;
5. awaits Artifact retirement; and
6. publishes the route result only after cleanup succeeds.

All authorities are attempted even when one retirement fails. Cleanup failure
prevents publication of an otherwise successful catalog result and remains a
visible CLI failure. House or derivation non-success remains primary when
cleanup succeeds. No returned route result retains a stream, lease, owner,
callback, opener, package payload, or disposable authority.

## Router policy

The router queries the full token as a type first. On a miss, it tests
top-level dot boundaries from right to left and accepts the first non-missing
type prefix as the member owner. This chooses the longest structured type
without interpreting dots inside generic arguments.

- A resolved full token routes to `type`, or to `member` when the caller
  explicitly selected member mode.
- A resolved prefix routes to `member` with the remaining suffix as its member
  selector.
- Ambiguity or rejection at the longest viable boundary is terminal.
- A complete miss returns control to existing package, non-runtime Platform,
  project, and direct-library classification.

Explicit-source and acquisition-free structural routes run before this path
and remain unchanged.

## Platform compatibility

This owner is deliberately desktop-CLI-specific and supports Windows, Linux,
and macOS. It uses the installed source only when a local dotnet hive is
available and otherwise uses the package-backed source. Browser/Wasm does not
reference this owner or the installed adapter; its separately reviewed
PlatformHouse adoption supplies package-backed capabilities directly.

## Evidence gates

Release tests prove:

- real `System.Text.Json.JsonSerializer` routing through an exact selected
  catalog entry and `System.Text.Json` assembly identity;
- generic `System.Collections.Generic.List<T>.Add` member routing through the
  same catalog path;
- installed completion without package discovery;
- resource-free catalog use after every population authority retires;
- typed ambiguity, rejection, and true missing outcomes from the retired
  catalog;
- visible ambiguity from the production router;
- a true catalog miss returning control to ordinary router classification; and
- explicit `runtime@version` bypassing the family-default path.

Lower owner suites continue to prove selection of installed prereleases,
stable `10.0.x` package fallback, source correspondence, all-or-nothing
population transfer, catalog completeness, and query ranking.

## Production adoption and retirement

This slice adopts the CLI half of PlatformHouse step 9. Browser/Wasm adoption
remains a separate host-owner slice. Services-era `PlatformResolver`,
`PlatformPackService`, and `PlatformTypeCatalog` remain until every supported
host uses the replacement and step 10 can remove the bypasses coherently.

The CLI route is a direct host-specific command projection and does not use
Markout. It adds no rendered section or output schema; the existing `type` and
`member` commands continue to own presentation.

## Non-goals

- Browser/Wasm routing.
- ASP.NET Core family-default selection.
- Exact-demand PlatformHouse execution.
- Metadata binding or implementation-population realization.
- Services-era resolver or catalog removal.
- New CLI options, sections, or output formats.
- Windows Metadata support.
