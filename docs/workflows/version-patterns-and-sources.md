---
id: version-patterns-and-sources
description: Wildcard version patterns and custom NuGet source configuration
commands: [package, --version, --add-source, --nugetconfig]
areas: [versioning, nuget, sources, wildcards, preview]
---

# Version Patterns and Custom Sources

> Beyond exact version pinning, the tool supports wildcard version patterns for matching the latest within a range, and custom NuGet source configuration for accessing private feeds and preview packages.

## Preconditions

Isolated session. Some scenarios require network access for NuGet queries.

```bash
export DOTNET_INSPECT_ISOLATED=version-patterns
```

```bash
dotnet-inspect cache clear
```

## 1. Wildcard version patterns

> Goal: Match the latest version within a pattern, useful for tracking patch releases or preview builds.

### 1a. Patch wildcard

```prompt
What's the latest 9.0.x version of System.Text.Json?
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

### 1b. Preview wildcard

```prompt
What's the latest preview of System.Text.Json 11?
```

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

## 2. Custom NuGet sources

> Goal: Access packages from private feeds or nightly builds using `--add-source`.

### 2a. Add a feed for preview packages

```prompt
Get the latest .NET 11 preview of System.Text.Json from the nightly feed.
```

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

## 3. NuGet config file

> Goal: Use a `nuget.config` file to specify feeds, credentials, and source mappings.

```bash
dotnet-inspect package System.CommandLine --nugetconfig ./nuget.config -v:q
```

```expect
# System.CommandLine
Source: NuGet
```
