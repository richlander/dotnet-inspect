# Version Resolution

dotnet-inspect uses Docker-style version tags to balance freshness against
latency. Version discovery is cached briefly; package contents are cached
permanently by exact version.

## Four modes

| Syntax | Behavior | Network I/O |
| --- | --- | --- |
| `Name@2.0.3` | **Pinned** — use the exact version from cache; download only if missing | Never, if cached |
| `Name` | **Latest stable** — resolve the latest stable version, then use/download that exact package | Only on version-cache miss |
| `Name --preview` | **Latest prerelease** — resolve the latest version including prerelease/preview versions | Only on version-cache miss |
| `Name@latest` | **Always check** — query NuGet for the latest version every time | Always |
| `Name@A..B` | **Addressable vector** — resolve the inclusive published-version range without downloading package payloads | Only on version-list cache miss |

### Pinned (`Name@version`)

The version is treated as immutable. If the package is already in the NuGet
global cache (`~/.nuget/packages`) or the app cache, it is used immediately.
No network request is made. If the version has never been downloaded, it is
fetched once and cached permanently.

### Latest stable (`Name`)

This is the default and the most common case. Resolution follows this order:

1. **Version cache** — check the version-resolution cache (1-hour TTL). If a
   cached version string exists, use it.
2. **Network** — if the version cache misses, query NuGet for the latest stable
   version.
3. **Package cache** — after resolving the version, use the NuGet global cache
   or app package cache for that exact version; download only if missing.

Adding `--preview`/`--prerelease` switches step 1/2 to a separate prerelease-aware
version cache/feed query and may resolve to a preview version.

For platform ref packs (e.g., `Microsoft.NETCore.App.Ref`), the same strategy
applies: if a pack directory exists on disk, use it without querying NuGet.

Package metadata (publish date, downloads, deprecation, vulnerabilities) is
also cached with a 1-hour TTL.

### Always check (`Name@latest`)

Forces a full network refresh. Bypasses the disk scan, version cache, and
metadata cache. Useful when you want to verify you have the absolute latest
version or check for newly published security advisories.

### Addressable vector (`Name@A..B`)

A package range names an immutable, inclusive vector of published versions. The
vector follows the caller's direction: `1.0.0..2.0.0` is oldest-to-newest and
`2.0.0..1.0.0` is newest-to-oldest. Both endpoints must exist.

```bash
dotnet-inspect package System.Text.Json@8.0.0..8.0.5 --versions
dotnet-inspect type JsonSerializer \
  --package System.Text.Json@8.0.0..8.0.5 --at '#4'
dotnet-inspect member JsonSerializer Serialize \
  --package System.Text.Json@8.0.0..8.0.5 --at 8.0.5
dotnet-inspect timeline \
  --package System.Text.Json@8.0.0..8.0.5 \
  --type System.Text.Json.JsonSerializer \
  --finding api.member --at first --at last
```

`package --versions` enumerates the vector. Range-capable API commands require
`--at` with an exact version, a one-based `#N` address, `first`, or `last`.
Resolving the vector reads version metadata only. The command downloads or
opens a package only after the caller selects an address, so an agent can probe
previous, midpoint, or adjacent versions without triggering an unbounded scan.

`timeline` uses the same vector without changing that authorization rule. With
no `--at`, it renders every address as `Unevaluated` and recommends a probe
without downloading package payloads. Repeated `--at` selectors perform sparse
correlation; `--at all` is the explicit dense-traversal opt-in. Type focus may
select the type-presence (`api.type`), owned-member (`api.member`), or applied
attribute (`api.attribute`) census. Adding `--member` to `api.member` selects
one exact member identity track. The same member focus composes with
`analysis.allocation`, `analysis.call-site`, and `analysis.unsafety`; only the
selected method body is decoded at each evaluated address. Sparse transitions
spanning unevaluated cells are labeled as gaps and do not claim the exact
version of a change.

```bash
dotnet-inspect timeline --package Foo@1.0.0..2.0.0 \
  --type Foo.Parser --member Parse \
  --finding analysis.unsafety --at first --at last
```

This locates candidate unsafe-operation boundaries without replacing the final
adjacent `diff --finding analysis.unsafety` introduction proof.

Stable endpoints exclude prereleases by default. A prerelease endpoint or
`--preview` on `package --versions` includes prereleases within the range.
Existing `diff --package Name@A..B` remains the two-endpoint projection of the
same familiar range syntax. After caller-directed probes locate a candidate
boundary, `diff -S "Finding Transitions"` confirms a focused type or member as
the native `PairFinding.Added`, `Present`, `Removed`, or `Changed` transition:

```bash
dotnet-inspect diff \
  --package System.Text.Json@8.0.6..9.0.0 \
  --type System.Text.Json.Schema.JsonSchemaExporter \
  -S "Finding Transitions"
```

The same final-pair contract applies to member-scoped allocation onset. Select
the Analysis producer explicitly; the command compares only the supplied
endpoints and reports each native allocation occurrence pair:

```bash
dotnet-inspect diff --package Foo@1.4.0..1.5.0 \
  -t Foo.Parser -m Parse \
  --finding analysis.allocation
```

`PairFinding.Added` is the confirmed allocation introduction. `Present`,
`Removed`, and `Changed` distinguish a wrong boundary, disappearance, or
allocation-facet change without inferring those states from aggregate counts.

After locating an allocation boundary, the same caller-selected endpoint pair
can test whether a new direct call explains it:

```bash
dotnet-inspect diff --package Foo@1.4.0..1.5.0 \
  -t Foo.Parser -m Parse \
  --finding analysis.call-site
```

Here `PairFinding.Added` confirms a new call-site occurrence in `Parse`.
`PairFinding.Changed` can instead show that an existing call moved into a loop
or changed dispatch/opcode facets. The caller method is the target; the rows
identify its callees.

To confirm a definite unsafe-operation boundary in the same method:

```bash
dotnet-inspect diff --package Foo@1.4.0..1.5.0 \
  -t Foo.Parser -m Parse \
  --finding analysis.unsafety
```

`PairFinding.Added` confirms that an unsafe operation was introduced;
`Present` and `Removed` distinguish persistence from disappearance. Operation
kind and producer detail establish identity, while IL offsets remain local to
each endpoint.

## Cache locations

| Cache | Location | TTL | Written by |
| --- | --- | --- | --- |
| NuGet global cache | `~/.nuget/packages/{name}/{version}/` | Permanent | `dotnet restore`, NuGet client |
| App package cache | `$LOCAL_APP_DATA/dotnet-inspect/package-content-v2/{name}/{version}/` | Permanent | dotnet-inspect |
| Platform packs | `$LOCAL_APP_DATA/dotnet-inspect/packs-v2/{pack}/{version}/` | Permanent | dotnet-inspect |
| Version resolution | `$LOCAL_APP_DATA/dotnet-inspect/versions/` | 1 hour | dotnet-inspect |
| Package metadata | `$LOCAL_APP_DATA/dotnet-inspect/metadata/` | 1 hour | dotnet-inspect |
| Symbol miss markers | `$LOCAL_APP_DATA/dotnet-inspect/symbol-misses/` | 1 day | dotnet-inspect |
| SourceLink availability markers | `$LOCAL_APP_DATA/dotnet-inspect/source-audit/` | Permanent for hits, 1 day for misses | dotnet-inspect |

## Network download/cache behavior

Network calls use the cache behavior below. Negative cache entries are written
only for definitive 404/not-found responses; transient failures, timeouts,
offline mode, and unsupported local feed URLs are not cached as misses.

| Download or check | Cache behavior |
| --- | --- |
| Pinned package `.nupkg` extraction | Uses NuGet global cache or app package cache permanently; downloads only when missing. |
| Bare package version resolution | Uses the version-resolution cache with a 1-hour TTL, then NuGet; package caches are used only after the version is resolved. |
| Bare package `--preview` resolution | Uses a separate prerelease-aware version-resolution cache with a 1-hour TTL, then NuGet. |
| Wildcard version resolution | Uses the same version-list cache as `--versions` with a 1-hour TTL for nuget.org-backed sources. |
| Addressable package range | Uses the version-list cache to resolve the vector; package caches are consulted only after a caller selects a cell. |
| `@latest` package resolution | Always checks NuGet and bypasses version/metadata caches. |
| Package index scan | Cached permanently for extracted package contents. |
| Package metadata | Cached for 1 hour in the metadata cache. |
| Dependency publish dates | Reuses the package metadata cache, so dependency-age audit does not refetch known publish dates. |
| Successful symbol-server PDB downloads | Cached permanently under `packages/symbols/servers/`. |
| Symbol-server PDB 404s | Cached as misses for 1 day, so detailed audit does not retry unavailable PDBs on every run. |
| Successful `.snupkg` PDB extraction | Extracted PDB is cached permanently under `packages/symbols/{package}/{version}/`. |
| Missing `.snupkg` URLs and `.snupkg` files without the requested PDB | Cached as misses for 1 day. The `.snupkg` archive itself is not retained. |
| SourceLink audit source checks | Successful HEAD checks are cached permanently; 404s are cached as misses for 1 day. |
| Selected-member `Original Source` downloads | Not cached by this command path. |
| `SourceLink Availability` URL checks | Not cached by this command path. |
| Service-index discovery for custom NuGet feeds | Not cached. nuget.org flat-container paths avoid this lookup. |
| GitHub advisory enrichment | Not separately cached; it is covered when the package metadata cache is hit. |

## Package and pack publication

Version resolution ends with an exact package coordinate. Package extraction
then shares one acquisition task per coordinate within a process and publishes
complete app-cache entries transactionally. Separate processes may duplicate
immutable work, but readers observe only a marked, atomically published winner.
Platform-pack projection uses the same model in a separate cache namespace.

Cache identity is the cache root, normalized package id, and version.
Source order selects the producer on a miss and is not a separate durable cache
identity. See [cache concurrency and publication](cache-concurrency.md) for the
single-flight boundary, dependency-overlap safety, filesystem rename semantics,
failure model, and NuGet, Docker, and Git precedents.

## Design rationale

The following Docker tag analogy concerns version selection. Docker daemon
request deduplication is covered separately in
[cache concurrency and publication](cache-concurrency.md).

| Docker command | dotnet-inspect command | Version behavior |
| --- | --- | --- |
| `docker run nginx:1.25` | `dotnet-inspect package System.Text.Json@10.0.0` | Uses a pinned, reproducible coordinate. |
| `docker run nginx` | `dotnet-inspect package System.Text.Json` | Uses the newest locally cached stable version, or resolves from NuGet when absent. |
| `docker pull nginx` | `dotnet-inspect package System.Text.Json@latest` | Always checks NuGet for the current version. |

NuGet packages are immutable once published (a given version string always
refers to the same content), so pinned versions never go stale. The bare-name
default optimizes for the interactive CLI use case where sub-second response
time matters more than always having the absolute latest version.
