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

Named isolated session ensures reproducible results (no shared state, no NuGet cache). Offline mode is enabled by default; scenarios requiring network explicitly disable it.

```bash
export DOTNET_INSPECT_ISOLATED=version-queries
export DOTNET_INSPECT_OFFLINE=1
```

```bash
dotnet-inspect cache clear
```

Prime the cache and version index:

```bash
DOTNET_INSPECT_OFFLINE=0 dotnet-inspect System.CommandLine@2.0.2 -v:q
```

```bash
DOTNET_INSPECT_OFFLINE=0 dotnet-inspect System.CommandLine --versions > /dev/null
```

## 1. Get the best-known version

> Goal: Get the version most likely to be relevant, fast. Checks app cache first, then NuGet cache, then remote — returning the first hit.

### 1a. Using `--version` (cached)

```prompt
What version of System.CommandLine do I have cached?
```

```bash
dotnet-inspect System.CommandLine --version
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
DOTNET_INSPECT_OFFLINE=0 dotnet-inspect System.CommandLine --version
```

```expect
2.0.3
```

```query
head -1
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

```expect
2.0.3
```

```query
head -1
```

### 2b. Using `@latest`

```bash
dotnet-inspect System.CommandLine@latest --version
```

```expect
2.0.3
```

```query
head -1
```

### 2c. Using `@latest` with package command

```bash
dotnet-inspect package System.Text.Json@latest -v:q
```

```expect
Source: NuGet
```

```query
grep -oE 'Version: [0-9.]+'
```

## 3. List all available versions

> Goal: See every published version, newest first.

### 3a. Using `--versions`

```bash
dotnet-inspect System.CommandLine --versions
```

```expect
2.0.3
2.0.2
```

```query
head -2
```

## 4. Handle a nonexistent version

> Goal: Get a clear error when requesting a version that doesn't exist for a known package.

### 4a. Using `--version` with bad version

```bash
dotnet-inspect System.CommandLine@99.99.99 --version
```

```expect-error
Version '99.99.99' of package 'System.CommandLine' not found. Use --versions to see available versions.
```

```query
grep 'not found'
```

### 4b. Using bare name with bad version

```bash
dotnet-inspect System.CommandLine@99.99.99
```

```expect-error
Version '99.99.99' of package 'system.commandline' not found. Use --versions to see available versions.
```

```query
grep 'not found'
```

## 5. Handle a nonexistent package

> Goal: Get a clear error when requesting a package that doesn't exist at all.

### 5a. Using bare name

This test requires network access to confirm the package doesn't exist.

```bash
DOTNET_INSPECT_OFFLINE=0 dotnet-inspect System.CommandLine2@99.99.99
```

```expect-error
Package 'system.commandline2' not found.
```

```query
grep 'not found'
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

```bash
dotnet-inspect package System.CommandLine --nugetconfig ./nuget.config -v:q
```

```expect
# System.CommandLine
Source: NuGet
```
