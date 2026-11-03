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
dotnet-inspect type System.Text.Json -t '*' -v:q --offline
```

```expect
# System.Text.Json
Source: Platform
```

## 2. Cached NuGet package offline

> Goal: A previously cached NuGet package can be inspected with `--offline`.

Prime the required content under an explicit anonymous source (requires
network). A bare Package Library view does not promise to cache every payload
needed by a later Type inspection. Configured credential-bearing sources may
also have only process-local storage; use the same durable authority for
priming and offline consumption:

```setup
export OFFLINE_WORKSPACE=$(
  dotnet-inspect workspace --package System.CommandLine@2.0.3 --tfm net8.0 \
    --nuget-source-anonymous https://api.nuget.org/v3/index.json
)
dotnet-inspect type Command --workspace "$OFFLINE_WORKSPACE" --markdown -v:q
```

Then inspect offline:

```bash
dotnet-inspect package System.CommandLine --workspace "$OFFLINE_WORKSPACE" \
  --offline -v:q
```

```expect
System.CommandLine@2.0.3
NuGet
System.CommandLine.dll
```

```bash
dotnet-inspect type Command --workspace "$OFFLINE_WORKSPACE" \
  --offline --markdown -v:q
```

```expect
# System.CommandLine.Command
Package: system.commandline
Version: 2.0.3
Source: NuGet
```

## 3. Uncached package fails gracefully offline

> Goal: Requesting a package that is not cached produces a clear error instead of hanging or crashing.

```bash
dotnet-inspect package DotnetInspect.Workflow.Missing.Package@99.99.99 \
  --offline -v:q
```

```expect-error
is not available offline; no cached package was found
```
