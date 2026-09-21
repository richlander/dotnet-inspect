# Network observation

> How to observe Debug HTTP request starts and use offline mode when a workflow
> must prohibit network access.

## Why it matters

Many dotnet-inspect commands should be fully offline because they read from
platform assemblies or cached NuGet packages. Unexpected PDB, SourceLink, or
package requests can add substantial latency. Debug request logging reveals
those paths; `--offline` enforces the no-network boundary.

## How it works

- **Debug observation** — online Debug CLI builds call
  `HttpClientFactory.EnableNetworkTrafficLogging(...)`.
- **Request-start events** — `NetworkTelemetry` records a credential-redacted
  URL, client kind, traffic kind, and current request purpose.
- **No completion claim** — the global observation does not report response
  status, retries, body bytes, or duration.
- **Offline enforcement** — `--offline` or
  `DOTNET_INSPECT_OFFLINE=1` blocks HTTP in every build configuration.

## Quick start

Build Debug and run a command whose network behavior you want to inspect:

```bash
set -e -o pipefail
: "${DOTNET_INSPECT_WORKFLOW_VERSION:?set the expected --version output}"
dotnet build src/DotnetInspect.Cli/DotnetInspect.Cli.csproj -t:Rebuild
export INSPECT="$PWD/artifacts/bin/dotnet-inspect/debug/dotnet-inspect"
test -x "$INSPECT"
test "$("$INSPECT" --version)" = "$DOTNET_INSPECT_WORKFLOW_VERSION"
"$INSPECT" --flavor | grep -q '^CoreCLR;'
"$INSPECT" library System.Text.Json -v:q
```

Any `Network traffic [...]` line identifies a managed request that started.
Use `--offline` when the operation must be prevented rather than observed.

## When network access is expected

| Verbosity | Typical network use | Why |
| --- | --- | --- |
| `-v:q` | None for a warm local or platform query | Summary only |
| `-v:m` | None for a warm local or platform query | Metadata from local assembly |
| default | Depends on the requested subject and cache state | Package and documentation acquisition may be required |
| `-v:d` | Often | PDB and SourceLink acquisition may be required |

## Enforce offline execution

Use the product's offline mode rather than relying on Debug logging:

```bash
dotnet run --project src/DotnetInspect.Cli/DotnetInspect.Cli.csproj -- \
  library System.Text.Json -v:q --offline
```

## Observation versus enforcement

These are complementary mechanisms:

| Mechanism | Build | Behavior |
| --- | --- | --- |
| Debug request logging | Debug | Reports managed request starts |
| `--offline` flag | Any build | Blocks HTTP through the product transport |

Use logging to diagnose routing and latency. Use `--offline` for a deterministic
cache-only operation.

## Implementation details

Debug request logging is enabled in `Program.cs`:

```csharp
#if DEBUG
if (!offline)
{
    HttpClientFactory.EnableNetworkTrafficLogging(
        CSharpIdentifier.ContainRenderedText);
}
#endif
```

`NetworkTelemetry.Scope(...)` classifies request purpose.
`NetworkTelemetry.Subscribe(...)` supports aggregate or diagnostic observers.
Operation-specific evidence must be captured by the owning operation instead
of reconstructing completion facts from this process-global request-start
stream.

## Validation scenarios

The [network observation workflow](../../docs/workflows/advanced/network-guard.md)
shows Debug logging and offline enforcement. The
[offline usage workflow](../../docs/workflows/advanced/offline-usage.md)
validates `--offline` for platform and NuGet packages.
