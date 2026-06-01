# Version Resolution

dotnet-inspect uses Docker-style version tags to balance freshness against
latency. A cached package is served in under 50 ms; a network round-trip
typically costs 1–4 seconds.

## Three modes

| Syntax | Behavior | Network I/O |
| --- | --- | --- |
| `Name@2.0.3` | **Pinned** — use the exact version from cache; download only if missing | Never, if cached |
| `Name` | **Prefer cache** — use the newest cached version; refresh when caches expire | Only on TTL expiry |
| `Name@latest` | **Always check** — query NuGet for the latest version every time | Always |

### Pinned (`Name@version`)

The version is treated as immutable. If the package is already in the NuGet
global cache (`~/.nuget/packages`) or the app cache, it is used immediately.
No network request is made. If the version has never been downloaded, it is
fetched once and cached permanently.

### Prefer cache (`Name`)

This is the default and the most common case. Resolution follows this order:

1. **Disk scan** — check the NuGet global cache and app cache for any stable
   (non-prerelease) version of the package. If found, use the highest version.
2. **Version cache** — if no package is cached, check the version-resolution
   cache (1-hour TTL). If a cached version string exists, use it.
3. **Network** — if both caches miss, query NuGet for the latest stable version.

For platform ref packs (e.g., `Microsoft.NETCore.App.Ref`), the same strategy
applies: if a pack directory exists on disk, use it without querying NuGet.

Package metadata (publish date, downloads, deprecation, vulnerabilities) is
also cached with a 1-hour TTL.

### Always check (`Name@latest`)

Forces a full network refresh. Bypasses the disk scan, version cache, and
metadata cache. Useful when you want to verify you have the absolute latest
version or check for newly published security advisories.

## Cache locations

| Cache | Location | TTL | Written by |
| --- | --- | --- | --- |
| NuGet global cache | `~/.nuget/packages/{name}/{version}/` | Permanent | `dotnet restore`, NuGet client |
| App package cache | `$LOCAL_APP_DATA/dotnet-inspect/packages/{name}/{version}/` | Permanent | dotnet-inspect |
| Platform packs | `$LOCAL_APP_DATA/dotnet-inspect/packs/{pack}/{version}/` | Permanent | dotnet-inspect |
| Version resolution | `$LOCAL_APP_DATA/dotnet-inspect/versions/` | 1 hour | dotnet-inspect |
| Package metadata | `$LOCAL_APP_DATA/dotnet-inspect/metadata/` | 1 hour | dotnet-inspect |
| Symbol miss markers | `$LOCAL_APP_DATA/dotnet-inspect/symbol-misses/` | 1 day | dotnet-inspect |
| Source audit markers | `$LOCAL_APP_DATA/dotnet-inspect/source-audit/` | Permanent for hits, 1 day for misses | dotnet-inspect |

## Network download/cache behavior

Network calls use the cache behavior below. Negative cache entries are written
only for definitive 404/not-found responses; transient failures, timeouts,
offline mode, and unsupported local feed URLs are not cached as misses.

| Download or check | Cache behavior |
| --- | --- |
| Pinned package `.nupkg` extraction | Uses NuGet global cache or app package cache permanently; downloads only when missing. |
| Bare package version resolution | Uses disk scan first, then the version-resolution cache with a 1-hour TTL, then NuGet. |
| Wildcard version resolution | Uses the same version-list cache as `--versions` with a 1-hour TTL for nuget.org-backed sources. |
| `@latest` package resolution | Always checks NuGet and bypasses version/metadata caches. |
| Package index scan | Cached permanently for extracted package contents. |
| Package metadata | Cached for 1 hour in the metadata cache. |
| Dependency publish dates | Reuses the package metadata cache, so dependency-age audit does not refetch known publish dates. |
| Successful symbol-server PDB downloads | Cached permanently under `packages/symbols/servers/`. |
| Symbol-server PDB 404s | Cached as misses for 1 day, so detailed audit does not retry unavailable PDBs on every run. |
| Successful `.snupkg` PDB extraction | Extracted PDB is cached permanently under `packages/symbols/{package}/{version}/`. |
| Missing `.snupkg` URLs and `.snupkg` files without the requested PDB | Cached as misses for 1 day. The `.snupkg` archive itself is not retained. |
| SourceLink audit source checks | Successful HEAD checks are cached permanently; 404s are cached as misses for 1 day. |
| `source --cat` raw source downloads | Not cached by this command path. |
| `source --verify` URL checks | Not cached by this command path. |
| Service-index discovery for custom NuGet feeds | Not cached. nuget.org flat-container paths avoid this lookup. |
| GitHub advisory enrichment | Not separately cached; it is covered when the package metadata cache is hit. |

## Design rationale

The Docker analogy:

- `docker run nginx:1.25` → pinned, reproducible
- `docker run nginx` → uses local image if present, pulls if not
- `docker pull nginx` → always checks the registry

NuGet packages are immutable once published (a given version string always
refers to the same content), so pinned versions never go stale. The bare-name
default optimizes for the interactive CLI use case where sub-second response
time matters more than always having the absolute latest version.
