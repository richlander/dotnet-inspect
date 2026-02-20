---
id: offline-usage
description: Use --offline mode to block all network access and work from cache
commands: [library, package, type, member]
areas: [offline, network]
---

# Offline Usage

> The `--offline` flag (or `DOTNET_INSPECT_OFFLINE=1`) blocks all HTTP requests at runtime, forcing the tool to use only cached data. This works in any build configuration.

## Preconditions

```bash
export DOTNET_INSPECT_ISOLATED=offline-testing
```

## 1. Platform library offline

> Goal: Platform libraries are fully local — offline mode has no effect on them.

```bash
dotnet-inspect library System.Text.Json -v:q --offline
```

```expect
# System.Text.Json.dll
Source: Platform
```

```bash
dotnet-inspect type System.Text.Json -v:q --offline
```

```expect
# System.Text.Json
Source: Platform
```

## 2. Cached NuGet package offline

> Goal: A previously cached NuGet package can be inspected with `--offline`.

Prime the cache first (requires network):

```setup
dotnet-inspect package System.CommandLine@2.0.3 -v:q
```

Then inspect offline:

```bash
dotnet-inspect package System.CommandLine --offline -v:q
```

```expect
# System.CommandLine
Source: NuGet
```

```bash
dotnet-inspect type System.CommandLine --offline -v:q
```

```expect
# System.CommandLine
Source: NuGet
```

## 3. Uncached package fails gracefully offline

> Goal: Requesting a package that is not cached produces a clear error instead of hanging or crashing.

```bash
dotnet-inspect package Humanizer --offline -v:q
```

```expect-error
Network access is disabled (--offline mode)
```
