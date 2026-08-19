# MSDL CORS proxy

## Why this exists

Browser-hosted `dotnet-inspect` decompiles Microsoft-authored packages that
ship no embedded PDB and no `.snupkg` on nuget.org (this is the common case
for framework/platform packages such as `Microsoft.Extensions.Http`). The
only remaining PDB source is Microsoft's public symbol server, `msdl.
microsoft.com`.

A direct browser `fetch` to MSDL fails. MSDL responds to a symbol request
with a `302` redirect that carries no CORS headers at all; the eventual
Azure Blob Storage target of that redirect *is* CORS-friendly
(`Access-Control-Allow-Origin: *`), but the WHATWG Fetch spec applies the
CORS check to every hop of a redirect chain, not just the final response.
The browser aborts the fetch as a network error at the MSDL hop, before it
ever reaches the compliant blob. `curl` (which ignores CORS) succeeds, which
is why this was easy to miss during investigation.

This applies to **any** Microsoft-authored package, not just ones sourced
from `dotnet/runtime` -- MSDL is the shared fallback symbol source for the
whole `IsMicrosoftPackage`/platform-assembly acquisition path in
`SymbolPackageDownloader.cs`.

## What this service does

A small ASP.NET Core Minimal API, published Native AOT, that fetches the PDB
from MSDL server-side (where CORS does not apply) and streams the bytes back
to the browser with permissive CORS headers for the site's own origins.

`GET /msdl/{pdbFileName}/{symbolKey}` maps directly onto the MSDL request
shape `SymbolPackageDownloader.SymbolServers.cs` already builds client-side:

```text
https://msdl.microsoft.com/download/symbols/{pdbFileName}/{symbolKey}/{pdbFileName}
```

## Security model

Per `docs/design/untrusted-data-threat-model.md`'s "reject, don't sanitize"
policy, this is an allow list, not a sanitizer:

- The MSDL host is a compile-time constant (`MsdlClient.MsdlHost`). The
  client never supplies a URL, a host, or anything resembling one -- only the
  two path segments MSDL itself expects. This makes an open-redirect/SSRF
  outcome structurally impossible, not merely filtered.
- `pdbFileName` must be a single safe path segment (no `..`, `/`, `\`, `:`,
  NUL, and not absolute) ending in `.pdb`, capped at 255 characters.
- `symbolKey` must be 33-40 hex digits, matching the two shapes
  `SymbolPackageDownloader.cs` produces: a 32-hex-digit GUID plus either the
  fixed `FFFFFFFF` portable-PDB stamp or a variable-length Windows-PDB age.
- Anything outside those shapes is rejected with `400`, before any outbound
  request is made.
- The validator is a small, independent reimplementation (not a reference to
  `DotnetInspector.Packages`) so this externally-facing edge service stays
  self-contained, trimmable, and easy to audit on its own.
- The upstream response is capped at 200 MB, enforced twice: an early
  rejection when `Content-Length` already exceeds the cap, and a hard
  backstop that aborts the stream mid-flight if the actual byte count ever
  exceeds it (a lying or malformed `Content-Length` does not bypass the
  cap).

## Local development

```bash
dotnet run --project prototypes/inspect-web/msdl-proxy
curl http://localhost:5299/healthz
```

Run its tests with (xUnit executable, not `dotnet test`):

```bash
dotnet run --project prototypes/inspect-web/msdl-proxy.Tests -c Release
```

## Deployment (manual, not yet automated)

This service targets Azure Container Apps' Consumption plan, linked to the
`dotnet-inspect.ca` / `dotnet-inspect.net` Static Web Apps as a
["bring your own API"](https://learn.microsoft.com/azure/static-web-apps/apis-overview)
backend. The Consumption plan's monthly free grant (180,000 vCPU-seconds,
360,000 GiB-seconds, 2,000,000 requests) comfortably covers this workload,
and a plain container has no Native AOT caveats the way Azure Functions'
isolated-worker model currently does.

Provisioning the Container App and linking it to the Static Web Apps
instances requires Azure subscription access this repository's automation
does not have. Until an operator completes that setup, this service builds
and tests but is not deployed, and the browser engine still falls back to a
direct (CORS-failing) MSDL request for packages with no embedded PDB or
snupkg. The remaining manual steps:

1. Build and push the image (`docker build` from this directory, or let a
   future CI job do it) to a container registry.
2. Create the Container App from that image (`az containerapp create`),
   setting `MSDL_PROXY_ALLOWED_ORIGINS` to the site's origins if they differ
   from the defaults in `Program.cs`.
3. Link it as a backend to both Static Web Apps instances
   (`az staticwebapp backends link`) so `/msdl/*` requests route to it.
4. Wire the browser engine (`BrowserSourceOperations.cs` /
   `SymbolPackageDownloader.cs`) to call the proxy instead of `msdl.
   microsoft.com` directly when running under `browser-wasm`.
