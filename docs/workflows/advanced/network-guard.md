---
id: network-guard
description: DEBUG-only network guard for validating offline code paths
commands: [library]
areas: [network, debugging, performance]
---

# Network Guard

> A DEBUG-only mechanism that asserts code paths make no network calls unless explicitly allowed. Automatically enabled in DEBUG builds.

## Background

The tool downloads PDB symbols from Microsoft Symbol Server (MSDL) to enable SourceLink resolution. This can add ~700ms latency to cold starts. For quick queries (`-v:q`, `-v:m`), network access is not required since SourceLink information isn't displayed.

The network guard is automatically enabled in DEBUG builds and will throw if any HTTP request is made unexpectedly. This catches violations during development.

## How It Works

In DEBUG builds:
1. Network access is **denied by default** at startup
2. Network is **allowed** when:
   - `OFFLINE=1` is set (OfflineHandler handles blocking)
   - Verbosity is Detailed (`-v:d`) or SourceLink audit is requested
3. Any unexpected HTTP request throws `NetworkGuardException` with the URL

In Release builds, the guard is compiled out completely (zero overhead).

## Preconditions

Build the tool in DEBUG mode:

```bash
dotnet build src/dotnet-inspect/dotnet-inspect.csproj -c Debug
```

Use an isolated session to avoid cache interference:

```bash
export DOTNET_INSPECT_ISOLATED=networkguard
```

## 1. Quiet verbosity passes network guard

> Goal: `-v:q` completes without network access.

```prompt
Verify that a quiet library inspection works offline.
```

```bash
dotnet run --project src/dotnet-inspect/dotnet-inspect.csproj -c Debug -- library System.Text.Json -v:q
```

```expect
# System.Text.Json.dll
Source: Platform
```

```expect-not
Network guard violation
```

## 2. Minimal verbosity passes network guard

> Goal: `-v:m` completes without network access.

```bash
dotnet run --project src/dotnet-inspect/dotnet-inspect.csproj -c Debug -- library System.Text.Json -v:m
```

```expect
# System.Text.Json.dll
## Library Info
```

```expect-not
Network guard violation
```

## 3. Detailed verbosity allows network

> Goal: `-v:d` allows network for PDB/SourceLink information.

```bash
dotnet run --project src/dotnet-inspect/dotnet-inspect.csproj -c Debug -- library System.Text.Json -v:d
```

```expect
# System.Text.Json.dll
## Library Info
## Symbols
```

```expect-not
Network guard violation
```

## 4. Offline mode allows network (OfflineHandler blocks)

> Goal: `OFFLINE=1` disables the guard since OfflineHandler provides blocking.

```bash
DOTNET_INSPECT_OFFLINE=1 dotnet run --project src/dotnet-inspect/dotnet-inspect.csproj -c Debug -- library System.Text.Json -v:d
```

```expect
# System.Text.Json.dll
## Library Info
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

## Performance Impact

| Verbosity | Network | Cold Start |
|-----------|---------|------------|
| `-v:q` | Blocked | ~36ms |
| `-v:m` | Blocked | ~36ms |
| `-v:d` | Allowed | ~758ms (with MSDL) |

The ~722ms difference is the MSDL symbol server round-trip, now avoided for quick queries.

## Related

- `DOTNET_INSPECT_OFFLINE=1` blocks network at runtime (all builds)
- PDB download skipped for verbosity < Detailed in `LibraryMetadataService.cs`
- PDB acquisition happens in `SourceEnricher.AcquirePdbAsync`
