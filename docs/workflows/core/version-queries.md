---
id: version-queries
description: Query package versions, wildcard patterns, and custom NuGet sources
commands: [--version, --versions, --latest-version, --add-source, --nugetconfig]
areas: [versioning, cache, nuget, wildcards, sources]
---

# Version Queries

> Understand what version of a package is available — locally and remotely. A common first step when orienting to a project or resolving a dependency question.

These scenarios cover the split between **best-known** (app cache → NuGet cache → remote) and **remote-only** (always queries NuGet), plus error handling for nonexistent versions and packages.

## Preconditions

Named isolated session ensures reproducible results (no shared state, no NuGet cache).

```bash
export DOTNET_INSPECT_ISOLATED=version-queries
```

```bash
dotnet-inspect cache clear
```

Prime the cache and version index:

```bash
dotnet-inspect System.CommandLine@2.0.2 -v:q
```

```bash
dotnet-inspect System.CommandLine --versions > /dev/null
```

## 1. Get the best-known version

> Goal: Get the version most likely to be relevant, fast. Checks app cache first, then NuGet cache, then remote — returning the first hit.

### 1a. Using `--version` (cached)

```prompt
What version of System.CommandLine do I have cached?
```

```bash
dotnet-inspect System.CommandLine@2.0.2 --version
```

```expect
2.0.2
```

```query
head -1
```

### 1b. Using `--version` (empty cache)

```setup
dotnet-inspect cache clear
```

```bash
dotnet-inspect System.CommandLine --version
```

```query
grep -Eq '^[0-9]+(\.[0-9]+){2}$' && echo stable-version
```

```expect
stable-version
```

## 2. Get the latest published version

> Goal: Check the latest version available on NuGet.

### 2a. Using `--latest-version`

```prompt
What is the latest version of System.CommandLine on NuGet?
```

```bash
dotnet-inspect System.CommandLine --latest-version
```

```query
grep -Eq '^[0-9]+(\.[0-9]+){2}$' && echo stable-version
```

```expect
stable-version
```

### 2b. Using `@latest`

```bash
dotnet-inspect System.CommandLine@latest --version
```

```query
grep -Eq '^[0-9]+(\.[0-9]+){2}$' && echo stable-version
```

```expect
stable-version
```

### 2c. Using `@latest` with package command

```bash
dotnet-inspect package System.Text.Json@latest -v:q
```

```expect
Source: NuGet
```

```query
grep -Eq 'Version: [0-9]+(\.[0-9]+){2} \|' && echo stable-version
```

```expect
stable-version
```

### 2d. Query latest prerelease version

By default, unpinned package resolution chooses the latest stable version. Add `--preview`
or `--prerelease` to include prerelease versions when resolving latest.

```bash
dotnet-inspect package System.Text.Json --latest-version --preview
```

```query
grep -Eq '^[0-9]+(\.[0-9]+){2}-[^ ]+$' && echo prerelease-version
```

```expect
prerelease-version
```

### 2e. Resolve latest prerelease package

```bash
dotnet-inspect package System.Text.Json@latest --preview -v:q
```

```expect
# System.Text.Json
Source: NuGet
```

```query
grep -Eq 'Version: [0-9]+(\.[0-9]+){2}-[^ |]+ \|' && echo prerelease-version
```

```expect
prerelease-version
```

### 2f. Inspect an exact prerelease library

```bash
dotnet-inspect library System.Text.Json.dll \
  --package System.Text.Json@11.0.0-preview.6.26359.118 -S Signals
```

```expect
# System.Text.Json.dll
## Signals
```

These variants require network access to the configured NuGet sources.

## 3. List all available versions

> Goal: See every published version, newest first.

### 3a. Using `--versions`

```bash
dotnet-inspect System.CommandLine --versions
```

```query
awk 'NF { count++ } END { if (count >= 2) print "at-least-two" }'
```

```expect
at-least-two
```

### 3b. Using `--versions-with-feed`

`--versions` unions versions across every configured source and drops the
provenance. `--versions-with-feed` keeps it, emitting one row per (version, feed)
pair, so a version carried by two sources appears twice.

```bash
dotnet-inspect package System.CommandLine --versions-with-feed -n 3 --tsv
```

```query
awk -F '\t' 'NR == 1 && $1 == "version" && $2 == "feed" { print "header-ok" } NR > 1 && NF == 2 { rows++ } END { if (rows >= 1) print "rows-ok" }'
```

```expect
header-ok
rows-ok
```

The interesting case is more than one source. `System.CommandLine` is available
from both nuget.org and the public `dotnet-public` feed:

```bash
dotnet-inspect package System.CommandLine --versions-with-feed -n 4 \
  --source https://api.nuget.org/v3/index.json \
  --source https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json
```

```expect
nuget.org
pkgs.dev.azure.com
```

```query
awk 'NR > 1 { if (!seenFeed[$2]++) feedCount++; if (++versionRows[$1] == 2) duplicate = 1 } END { if (feedCount >= 2) print "multiple-feeds"; if (duplicate) print "duplicate-version" }'
```

```expect
multiple-feeds
duplicate-version
```

Two things to note. `-n 4` selects four complete version/feed rows after all
eligible sources have contributed and the aggregate's completeness is known.
The feed label is the source's configured name when it has a meaningful one,
otherwise the host; sources passed as bare `--source` URLs all carry the same
internal name, so the host is what distinguishes them.

### 3c. Listing status accompanies each feed row

Unlisted versions are hidden here exactly as they are from `--versions`. Add
`--include-unlisted` to show them, and a `Listing` column appears:

```bash
dotnet-inspect package Markout --versions-with-feed -n 3 --include-unlisted \
  --source https://api.nuget.org/v3/index.json
```

```expect
Version
Feed
Listing
```

```query
awk 'NR > 1 { if ($1 == "10.0.2" && $3 == "unlisted") anchor = 1; if ($3 == "listed") listed = 1 } END { if (anchor && listed) print "listing-status-ok" }'
```

```expect
listing-status-ok
```

The column is applied **per feed**, which is the one thing this view can express
and the merged views cannot. Only nuget.org publishes a listing status; other
feeds have no such concept and their versions are always reported as listed. So a
version unlisted on nuget.org but also published to a private feed is hidden for
its nuget.org row and kept for the private one. `--versions` has to pick a single
answer for the version and reports it as listed.

`--json`, `--jsonl`, and `--tsv` all work; the default is a markdown table.

## 4. Handle a nonexistent version

> Goal: Get a clear error when requesting a version that doesn't exist for a known package.

### 4a. Using `--version` with bad version

```bash
dotnet-inspect System.CommandLine@99.99.99 --version
```

```expect-error
Version '99.99.99' of package 'system.commandline' not found. Use --versions to see available versions.
```

```expect-stderr
Version '99.99.99' of package 'system.commandline' not found.
```

### 4b. Using bare name with bad version

```bash
dotnet-inspect System.CommandLine@99.99.99
```

```expect-error
Version '99.99.99' of package 'system.commandline' not found. Use --versions to see available versions.
```

```expect-stderr
Version '99.99.99' of package 'system.commandline' not found.
```

## 5. Handle a nonexistent package

> Goal: Get a clear error when requesting a package that doesn't exist at all.

### 5a. Using bare name

This test requires network access to confirm the package doesn't exist.

```bash
dotnet-inspect System.CommandLine2@99.99.99
```

```expect-error
Package 'system.commandline2' not found.
```

```expect-stderr
Package 'system.commandline2' not found.
```

## 6. Wildcard version patterns

> Goal: Match the latest version within a pattern, useful for tracking patch releases or preview builds.

### 6a. Patch wildcard

```prompt
What is the latest 9.0.x version of System.Text.Json?
```

```bash
dotnet-inspect package System.Text.Json --version '9.0.*' -v:q
```

```expect
# System.Text.Json
Source: NuGet
```

```query
grep -oE 'Version: 9\.0\.[0-9]+'
```

### 6b. Preview wildcard

```bash
dotnet-inspect package System.Text.Json --version '11.0.0-preview*' -v:q
```

```expect
# System.Text.Json
Source: NuGet
```

```query
grep -oE 'Version: 11\.0\.0-preview[^ |]+'
```

## 7. Custom NuGet sources

> Goal: Access packages from private feeds or nightly builds using `--add-source`.

### 7a. Add a feed for preview packages

```bash
dotnet-inspect package System.Text.Json --version '11.0.0-preview*' --add-source 'https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11/nuget/v3/index.json' -v:q
```

```expect
# System.Text.Json
Source: NuGet
```

```query
grep -oE 'Version: [^ |]+'
```

### 7b. Using nuget.config file

```setup
mkdir -p artifacts/workflows/version-queries
cat > artifacts/workflows/version-queries/nuget.config <<'EOF'
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
```

```bash
dotnet-inspect package System.CommandLine \
  --nugetconfig artifacts/workflows/version-queries/nuget.config -v:q
```

```expect
# System.CommandLine
Source: NuGet
```
