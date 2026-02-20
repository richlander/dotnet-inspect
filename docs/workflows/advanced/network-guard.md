---
id: network-guard
description: DEBUG-only network guard that asserts no unexpected HTTP requests
commands: [library]
areas: [network, debugging, performance]
---

# Network Guard

> DEBUG builds include a network guard that throws on unexpected HTTP requests. This catches code paths that accidentally depend on the network. Some commands legitimately need network access (e.g., `-v:d` for PDB/SourceLink), so those are exempted from the guard. The guard is compiled out in Release builds (zero overhead).

## Background

The tool downloads PDB symbols from Microsoft Symbol Server (MSDL) to enable SourceLink resolution. This can add ~700ms latency to cold starts. For quick queries (`-v:q`, `-v:m`), network access is not required since SourceLink information isn't displayed.

The network guard is automatically enabled in DEBUG builds and will throw if any HTTP request is made unexpectedly. This catches violations during development.

## Preconditions

Build the tool in DEBUG mode:

```bash
dotnet build src/dotnet-inspect/dotnet-inspect.csproj
```

Use an isolated session to avoid cache interference:

```bash
export DOTNET_INSPECT_ISOLATED=networkguard
```

Set the apphost path for convenience:

```bash
INSPECT=artifacts/bin/dotnet-inspect/debug/dotnet-inspect
```

## 1. Quiet verbosity passes guard (apphost)

> Goal: `-v:q` completes without network access, run via the apphost.

```bash
$INSPECT library System.Text.Json -v:q
```

```expect
# System.Text.Json.dll
Source: Platform
```

```expect-not
Network guard violation
```

## 2. Minimal verbosity passes guard (apphost)

> Goal: `-v:m` completes without network access, run via the apphost.

```bash
$INSPECT library System.Text.Json -v:m
```

```expect
# System.Text.Json.dll
## Library Info
```

```expect-not
Network guard violation
```

## 3. Detailed verbosity allows network

> Goal: `-v:d` is exempted from the guard because it legitimately fetches PDB/SourceLink data from MSDL.

```bash
$INSPECT library System.Text.Json -v:d
```

```expect
# System.Text.Json.dll
## Library Info
## Symbols
```

```expect-not
Network guard violation
```

## 4. Quiet verbosity via dotnet run

> Goal: Same guard behavior works through `dotnet run`.

```bash
dotnet run --project src/dotnet-inspect/dotnet-inspect.csproj -- library System.Text.Json -v:q
```

```expect
# System.Text.Json.dll
Source: Platform
```

```expect-not
Network guard violation
```

## Implementation Details

The network guard is implemented in `HttpClientFactory.cs`:

```csharp
internal sealed class NetworkGuardHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
#if DEBUG
        if (HttpClientFactory.IsNetworkDenied)
        {
            var message = $"Network guard violation: {request.Method} {request.RequestUri}";
            Debug.Fail(message);
            throw new NetworkGuardException(message);
        }
#endif
        return base.SendAsync(request, cancellationToken);
    }
}
```

The guard is enabled by default in `Program.cs`:

```csharp
#if DEBUG
// Network guard is always on to catch unintended network access.
// Disabled for offline mode (OfflineHandler handles it) and detailed verbosity.
if (!offline)
    DotnetInspector.Core.HttpClientFactory.DenyNetwork();
#endif
```

Commands opt-out when they legitimately need network (`AssemblyCommand.cs`):

```csharp
#if DEBUG
// Detailed verbosity legitimately needs network for PDB/SourceLink
if (options.Verbosity >= Verbosity.Detailed || options.IncludeSourcelinkAudit)
    DotnetInspector.Core.HttpClientFactory.AllowNetwork();
#endif
```
