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

## Design rationale

The Docker analogy:

- `docker run nginx:1.25` → pinned, reproducible
- `docker run nginx` → uses local image if present, pulls if not
- `docker pull nginx` → always checks the registry

NuGet packages are immutable once published (a given version string always
refers to the same content), so pinned versions never go stale. The bare-name
default optimizes for the interactive CLI use case where sub-second response
time matters more than always having the absolute latest version.
