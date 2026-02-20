# Network Guard

> How to use the DEBUG-only network guard to catch unexpected HTTP requests in dotnet-inspect.

## Why it matters

Many dotnet-inspect commands should be fully offline — they read from platform assemblies or cached NuGet packages. Accidental network access (e.g., PDB downloads from MSDL) adds 700ms+ latency and breaks the offline promise. The network guard catches these violations during development.

## How it works

- **DEBUG builds only** — the guard is compiled out in Release builds (zero overhead).
- `HttpClientFactory.DenyNetwork()` is called at startup; any HTTP request throws `NetworkGuardException`.
- Commands that legitimately need network (e.g., `-v:d` for SourceLink/PDB) call `AllowNetwork()` to opt out.

## Quick start

Build DEBUG and run a command that should be offline:

```bash
dotnet build src/dotnet-inspect/dotnet-inspect.csproj
INSPECT=artifacts/bin/dotnet-inspect/debug/dotnet-inspect
$INSPECT library System.Text.Json -v:q
```

If you see `Network guard violation` in the output, something is making an unexpected HTTP request.

## When network access is expected

| Verbosity | Network? | Why |
| --- | --- | --- |
| `-v:q` | No | Summary only |
| `-v:m` | No | Metadata from local assembly |
| default | No | Full signatures and docs from local data |
| `-v:d` | Yes | Fetches PDB/SourceLink from MSDL |

## `dotnet run` also triggers the guard

Since `dotnet run` compiles in DEBUG by default, you can use it to exercise the guard without building separately:

```bash
dotnet run --project src/dotnet-inspect/dotnet-inspect.csproj -- library System.Text.Json -v:q
```

## Offline mode vs network guard

These are complementary mechanisms:

| Mechanism | Build | Behavior |
| --- | --- | --- |
| Network guard | DEBUG only | Throws on unexpected HTTP — catches bugs |
| `--offline` flag | Any build | Blocks all HTTP — user-facing feature |

Use the guard during development. Use `--offline` in production when you want cache-only operation.

## Implementation details

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

Commands opt out when they legitimately need network (`AssemblyCommand.cs`):

```csharp
#if DEBUG
// Detailed verbosity legitimately needs network for PDB/SourceLink
if (options.Verbosity >= Verbosity.Detailed || options.IncludeSourcelinkAudit)
    DotnetInspector.Core.HttpClientFactory.AllowNetwork();
#endif
```

## Validation scenarios

The [network guard workflow](../../docs/workflows/advanced/network-guard.md) validates the guard across verbosity levels using the apphost and `dotnet run`. The [offline usage workflow](../../docs/workflows/advanced/offline-usage.md) validates the `--offline` flag for platform and NuGet packages.
