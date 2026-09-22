---
id: network-guard
description: Observe Debug network traffic and enforce offline execution
commands: [library]
areas: [network, debugging, performance]
---

# Network observation

> The historical Debug network guard has been retired. Debug CLI builds log
> managed HTTP request starts with their traffic kind; `--offline` is the
> supported enforcement mechanism when a workflow must prove that no HTTP
> request can proceed.

## Background

The tool may download packages, PDB symbols, and SourceLink content. Debug
builds publish credential-redacted request-start observations such as
`symbol-download` and `source-fetch` to stderr. These observations are useful
for diagnosing latency and unexpected routing, but they do not block requests
and do not report response status or completion.

## Preconditions

Build the tool in DEBUG mode:

```bash
dotnet build src/DotnetInspect.Cli/DotnetInspect.Cli.csproj -c Debug \
  -p:PublishAot=false -t:Rebuild
```

Use an isolated session to avoid cache interference:

```bash
export DOTNET_INSPECT_ISOLATED=networkguard
```

Set the apphost path for convenience:

```bash
set -e -o pipefail
: "${DOTNET_INSPECT_WORKFLOW_VERSION:?set the expected --version output}"
export INSPECT="$PWD/artifacts/bin/dotnet-inspect/debug/dotnet-inspect"
test -x "$INSPECT"
test "$("$INSPECT" --version)" = "$DOTNET_INSPECT_WORKFLOW_VERSION"
"$INSPECT" --flavor | grep -q '^CoreCLR;'
```

## 1. Observe quiet verbosity (apphost)

> Goal: inspect whether `-v:q` starts any managed HTTP request.

```bash
$INSPECT library System.Text.Json -v:q
```

```expect
# System.Text.Json.dll
Source: Platform
```

No `Network traffic [...]` line is expected for a warm platform query.

## 2. Observe minimal verbosity (apphost)

> Goal: inspect whether `-v:m` starts any managed HTTP request.

```bash
$INSPECT library System.Text.Json -v:m
```

```expect
# System.Text.Json.dll
## Library Info
```

No `Network traffic [...]` line is expected for a warm platform query.

## 3. Observe detailed source traffic

> Goal: a cold detailed query may report symbol and source request starts.

```bash
$INSPECT library System.Text.Json -v:d
```

```expect
# System.Text.Json.dll
## Library Info
## Symbols
```

```expect
Network traffic [symbol-download]:
```

## 4. Enforce no network

> Goal: use the production offline capability when no HTTP request may proceed.

```bash
dotnet run --project src/DotnetInspect.Cli/DotnetInspect.Cli.csproj -- \
  library System.Text.Json -v:q --offline
```

```expect
# System.Text.Json.dll
Source: Platform
```

## Implementation and background

See the [network observation skill](../../../skills/workflow-scenarios/network-guard.md)
for implementation details and the
[offline workflow](offline-usage.md) for deterministic enforcement.
