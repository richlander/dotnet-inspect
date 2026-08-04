# Version resolution

dotnet-inspect uses Docker-style version tags to balance freshness against
latency. Version discovery is cached briefly; package contents are cached
permanently by exact version.

The command modes, listing rules, source-scoped candidate caches, and
payload-provenance rules describe current behavior. Package source mapping and
the remaining source-policy boundaries are tracked by the
[package source model](package-source-model.md).

## Four modes

| Syntax | Behavior | Network I/O |
| --- | --- | --- |
| `Name@2.0.3` | **Pinned** — use the exact version from cache; download only if missing | Never, if cached |
| `Name` | **Latest stable** — resolve the latest stable version, then use/download that exact package | Only on version-cache miss |
| `Name --preview` | **Latest prerelease** — resolve the latest version including prerelease/preview versions | Only on version-cache miss |
| `Name@latest` | **Always check** — query NuGet for the latest version every time | Always |
| `Name@A..B` | **Addressable vector** — resolve the inclusive published-version range without downloading package payloads | Only on version-list cache miss |

### Pinned (`Name@version`)

The version is treated as immutable and the caller supplies the candidate. If
the package is already in a payload cache under an eligible producer, it is
used immediately. A global-folder entry qualifies only when its
`.nupkg.metadata.source` matches an eligible feed. No network request is made
on a qualifying hit. Otherwise, the package is fetched from an eligible source
and cached permanently under that producer.

### Latest stable (`Name`)

This is the default and the most common case. Resolution follows this order:

1. **Version cache** — check each eligible feed's version-resolution cache
   (1-hour TTL) for a source-scoped candidate list.
2. **Network** — if the version cache misses, query NuGet for the latest stable
   version.
3. **Package cache** — after resolving the version and retaining the feeds that
   reported it, use only a payload cached under one of those producers;
   otherwise download from one of them.

Adding `--preview`/`--prerelease` switches step 1/2 to a separate prerelease-aware
version cache/feed query and may resolve to a preview version.

SDK-installed ref and runtime packs are direct platform inputs, not NuGet
package candidates, and may be selected from the installed SDK/runtime. A pack
projected into dotnet-inspect's `packs-v2` cache is different: it is a
source-derived payload. Its version comes from an eligible feed or candidate
cache, and the projected payload must retain that producer's authorization.

Package metadata (publish date, downloads, deprecation, vulnerabilities) is
also cached with a 1-hour TTL.

Those aggregate metadata services are NuGet.org-specific. They are queried only
when the resolved source list contains the canonical
`https://api.nuget.org/v3/index.json` service index (an optional trailing slash
is equivalent). Another path on that host, a subdomain, or an endpoint with a
query or fragment is a custom source and goes through service-index discovery.
A custom-only feed therefore does not leak its package identity to NuGet.org or
reuse a NuGet.org metadata cache entry for a same-named private package. Package
acquisition and RID companion-package verification continue to follow the
configured sources.

This describes the current gate. The target
[package source model](package-source-model.md#enrichment-is-a-separate-capability)
narrows it further when package source mapping is enabled: NuGet.org must be
eligible for the package id, not merely active somewhere in configuration.

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

## Listed vs. unlisted versions

NuGet lets a publisher **unlist** a version: it stays restorable by exact
coordinate but is hidden from discovery on nuget.org. The flat-container
`index.json` that drives version enumeration lists **every** published version
and carries no listed flag, so it cannot distinguish an unlisted version on its
own. Only the nuget.org **registration** index exposes the per-version
`catalogEntry.listed` bit. Resolution reads the SemVer2 registration hive
(`registration5-gz-semver2`) rather than the SemVer1 hive, because the SemVer1
hive omits SemVer2 versions entirely — reading it would let unlisted SemVer2
prereleases escape filtering. That hive is gzip-encoded and transparently
decompressed by the shared HTTP client.

Version resolution applies one shared listing-aware policy so discovery matches
the nuget.org gallery:

- **Enumeration** (`Name --versions`, wildcard resolution) and the
  **flat-container / prerelease "latest"** paths consult the registration index
  and drop versions whose `catalogEntry.listed` is explicitly `false`. The
  stable "latest" path already uses the listing-aware search API and is
  unaffected. This is nuget.org-only; other feeds have no listed concept and are
  returned unfiltered.
- **Explicit access is preserved.** A pinned concrete `Name@Version` never
  enumerates, so a known unlisted version still resolves and loads — matching
  NuGet's own behavior of restoring a known unlisted version. `Name@latest`,
  wildcard versions, and addressable-vector endpoints are discovered
  coordinates; they retain the feeds that reported each selected version.
- **Fail-open vs. fail-closed on outage.** If the registration index cannot be
  fetched or parsed (network failure, or a valid-JSON document whose shape
  defies the expected schema), the condition is logged and behavior depends on
  the caller. **Raw enumeration** (`Name --versions`) fails **open** — the
  unfiltered list is returned rather than silently dropping real versions.
  **Auto-selecting** callers that pick a single version — nuget.org "latest"
  resolution and wildcard pattern resolution (`Name@3.0.*`) — fail **closed**,
  returning no result rather than risk selecting an unlisted version from an
  unfiltered snapshot. A fail-open (unfiltered) snapshot is **not** cached, so a
  transient registration outage cannot re-surface unlisted versions for the
  cache TTL; only an authoritatively filtered list is persisted. The version
  cache category is versioned (`versions-v5`). Every key has unambiguous
  producer, cache-kind, and package-id fields; latest entries additionally
  identify stable or prerelease selection. A prerelease-inclusive cache read
  also considers the stable entry, because stable candidates remain valid in
  that wider result set; package-existence probes likewise accept either
  flavor. Neither another feed nor a suffix-bearing package id can alias it.
  Candidate versions are accepted only as unpadded strings that parse as NuGet
  versions, and selected values are normalized before they are cached or used
  as coordinates. The category bump also fences older source-blind,
  pre-filter, ambiguous-key, and noncanonical-NuGet.org-attribution entries.

### Revealing unlisted versions

The listing status is available as a typed bit (`PackageVersionInfo.Listed`)
rather than only a hidden filter, so a surface can *mark* unlisted versions
instead of silently omitting them. `package --versions --include-unlisted`
opts into this: it lists every version, including unlisted ones, as a
`Version`/`Listing` table (each row marked `listed` or `unlisted`) across the
Markdown, `--tsv`, and `--jsonl` shapes. Hiding remains the default, so the bare
`--versions` output is unchanged.

Because a pinned `Name@Version` names an explicit coordinate, the `--versions`
query that verifies a single pinned version also consults the include-unlisted
listing, so verifying a known unlisted version reports it rather than
"not found". When listing status is unknown (fail-open, or a non-nuget.org
feed), versions are reported as listed.

`--include-unlisted` composes with the other `--versions` lenses. With a limit
(`--versions 1 --include-unlisted`) it takes the listing-aware path. A pinned
`Name@Version` and `Name@latest` still emit a one-row tagged table rather than a
bare version, so the result always carries the `listed`/`unlisted` column the
flag requests.
(`Name@latest` resolves through the listing-aware latest path, so its single row
is listed by construction.) With an addressable range (`Name@A..B --versions
--include-unlisted`) the vector is resolved from the full listing set — unlisted
versions included — so an unlisted endpoint resolves rather than being reported
as a missing endpoint, and each in-range row is marked. Prereleases are included
whenever a range endpoint is itself a prerelease (matching the default range
path), so a prerelease-endpoint range resolves without `--preview`. The bare
range (without the flag) resolves against listed versions only, matching the
hidden default.

The version-list cache stores the listed bit per version. Each cache line
carries an explicit two-character tab suffix (`\tL` listed, `\tU` unlisted).
Publication is atomic. A malformed or empty latest entry falls through to the
listing snapshot or feed, while a missing listing suffix, invalid version, or
empty snapshot is a cache miss rather than authoritative candidate metadata.

## Cache locations

| Cache | Location | TTL | Written by |
| --- | --- | --- | --- |
| NuGet global cache | `~/.nuget/packages/{name}/{version}/` | Permanent | `dotnet restore`, NuGet client; payload-only, with producer in `.nupkg.metadata` |
| App package cache | `$LOCAL_APP_DATA/dotnet-inspect/package-content-v5/{name}/{version}/{source}/` | Permanent | dotnet-inspect |
| Platform packs | `$LOCAL_APP_DATA/dotnet-inspect/packs-v2/{pack}/{version}/` | Permanent | dotnet-inspect |
| Version resolution | `$LOCAL_APP_DATA/dotnet-inspect/versions-v5/` | 1 hour | dotnet-inspect; one entry per producer, cache kind, package id, and latest flavor where applicable |
| Package metadata | `$LOCAL_APP_DATA/dotnet-inspect/metadata/` | 1 hour | dotnet-inspect |
| Symbol miss markers | `$LOCAL_APP_DATA/dotnet-inspect/symbol-misses/` | 1 day | dotnet-inspect |
| SourceLink availability markers | `$LOCAL_APP_DATA/dotnet-inspect/source-audit/` | Permanent for hits, 1 day for misses | dotnet-inspect |

The app package cache carries a `{source}` segment because cached content is
scoped to the source that supplied it; see
[Source conformance](cache-concurrency.md#source-conformance). The NuGet global
cache has no such segment. dotnet-inspect reads its
`.nupkg.metadata.source` before using it as a payload replica of an authorized
feed.

## Network download/cache behavior

Network calls use the cache behavior below. Negative cache entries are written
only for definitive 404/not-found responses; transient failures, timeouts,
offline mode, and unsupported local feed URLs are not cached as misses.

| Download or check | Cache behavior |
| --- | --- |
| Pinned package `.nupkg` extraction | Uses a global or app payload only when its recorded producer is eligible; downloads otherwise. |
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
| `SourceLink: Availability` URL checks | Not cached by this command path. |
| Service-index discovery for custom NuGet feeds | Not cached. nuget.org flat-container paths avoid this lookup. |
| GitHub advisory enrichment | Not separately cached; it is covered when the package metadata cache is hit. |

## Package and pack publication

Version resolution ends with an exact package coordinate. Package extraction
then shares one acquisition task per coordinate within a process and publishes
complete app-cache entries transactionally. Separate processes may duplicate
immutable work, but readers observe only a marked, atomically published winner.
Platform-pack projection uses the same model in a separate cache namespace.

Cache identity is the cache root, normalized package id, version, and the source
that supplied the bytes. A discovered coordinate may be fulfilled only by a
feed that reported it; a pinned coordinate may be fulfilled by any eligible
feed. See the
[cache concurrency and publication](cache-concurrency.md) for the
single-flight boundary, dependency-overlap safety, filesystem rename semantics,
failure model, and NuGet, Docker, and Git precedents.

The NuGet global folder participates only in payload fulfillment. Its recorded
source must be authorized for the exact coordinate; installed versions do not
expand the candidate set. `--no-nuget-cache` excludes it entirely.

## Multiple sources

The active sources form an eligible set, not a precedence list. Package source
mapping may narrow that set for a package id; see the
[package source model](package-source-model.md). Version operations combine all
eligible sources.

| Operation | Combination | Order sensitive |
| --- | --- | --- |
| `--latest-version` | Highest semantic version carried by any eligible source. | No |
| `--versions` | Union across all sources, deduplicated. | No |
| `--versions-with-feed` | Union across all sources, one row per (version, feed). | No |

An added private or nightly feed can therefore raise the latest-version answer
even when NuGet.org also carries the package. Source declaration order cannot
make one feed shadow another. Use package source mapping, or select a single
source, when only one feed may answer for an id.

`--versions-with-feed` keeps provenance that the merged views discard. It shows
which feeds carry each coordinate, including a coordinate published by more than
one feed.

### Listing status across sources

Listing status is a nuget.org concept; see
[listed vs. unlisted versions](#listed-vs-unlisted-versions). Other feeds do not
publish one, so their versions are reported as listed. That leaves the merged
views with a question they cannot answer well: when a version is unlisted on
nuget.org but also published to a private feed, is it listed?

The merged views answer "listed" — a version listed on any source counts as
listed. This keeps a version that is genuinely available from a private feed from
disappearing, but it does mean adding a private feed that mirrors nuget.org can
re-surface a version nuget.org has hidden.

`--versions-with-feed` does not have to answer, because it has already split the
version by feed. It applies listing per row, so the nuget.org row is hidden while
the private-feed row survives. When the two views disagree about a version, this
is why.

These semantics are pinned by `SourcePrecedenceTests`.

## Design rationale

The following Docker tag analogy concerns version selection. Docker daemon
request deduplication is covered separately in
[cache concurrency and publication](cache-concurrency.md).

| Docker command | dotnet-inspect command | Version behavior |
| --- | --- | --- |
| `docker run nginx:1.25` | `dotnet-inspect package System.Text.Json@10.0.0` | Fixes the version; producer provenance determines the bytes. |
| `docker run nginx` | `dotnet-inspect package System.Text.Json` | Resolves the newest stable candidate from selected feeds, then uses a matching cached payload when available. |
| `docker pull nginx` | `dotnet-inspect package System.Text.Json@latest` | Always checks NuGet for the current version. |

A pinned version does not by itself guarantee byte reproducibility across
feeds: two feeds may publish different payloads for one coordinate. A pinned
coordinate plus one authorized producer, or another verified content identity,
is reproducible. The bare-name default optimizes for the interactive CLI use
case where sub-second response time matters more than always having the
absolute latest version.
