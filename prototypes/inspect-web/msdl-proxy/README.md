# MSDL managed API

## Why this exists

Browser-hosted `dotnet-inspect` decompiles Microsoft-authored packages that
ship no embedded PDB and no `.snupkg` on nuget.org. The remaining PDB source is
Microsoft's public symbol server at `msdl.microsoft.com`.

A direct browser request fails because MSDL responds with a redirect that has
no CORS headers. The eventual Azure Blob Storage response is CORS-friendly, but
the browser applies CORS to every redirect hop and stops at MSDL.

Each Azure Static Web App therefore deploys this project as its managed
Functions API. The browser calls its own origin at:

```text
GET /api/msdl/{pdbFileName}/{symbolKey}
```

The function performs the MSDL request server-side and returns the PDB bytes.
`BrowserEngineBoundaryTests.MsdlProxy_RewritesExactSymbolRequestToCurrentSwaApi`
gates the host rewrite from MSDL's URL shape to this route.

## Security model

The endpoint is anonymous and serves only public symbol content. Its authority
is deliberately narrow:

- The MSDL host is a compile-time constant. The client supplies only the two
  path segments MSDL expects, never a URL or host.
- `pdbFileName` must be one safe path segment ending in `.pdb`, capped at 255
  characters.
- `symbolKey` must be 33-40 hex digits, matching the portable- and Windows-PDB
  key shapes produced by `SymbolPackageDownloader`.
- Invalid segments return `400` before an outbound request.
- Upstream responses are capped at 8 MiB, matching the browser's
  `MaxPortablePdbBytes` limit. A declared oversize fails before reading, and a
  bounded stream enforces the same limit when the declaration is absent or
  false.

`MsdlRequestValidatorTests`, `MsdlClientTests`, and
`MsdlProxyFunctionTests` gate these properties. The validator remains a small
independent implementation rather than referencing `DotnetInspector.Packages`,
so the externally facing function does not acquire the product library's wider
surface.

### Response security

Responses produced by the symbol and health functions carry these headers,
including validation failures, missing symbols, oversized declarations, and
handled upstream failures:

| Header | Value |
| --- | --- |
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `no-referrer` |
| `X-Frame-Options` | `DENY` |
| `Strict-Transport-Security` | `max-age=63072000; includeSubDomains` |

This function-owned policy covers public symbol bytes returned from our origin.
The values match the static site's baseline, but Azure Static Web Apps does not
apply `globalHeaders` to managed API responses. `MsdlProxyFunctionTests` executes
the MVC results and checks the headers, status codes, and successful bodies in
Release. Responses generated outside these functions, such as platform routing
errors or unhandled host failures, are outside this gate.

## Development

Run the executable xUnit project:

```bash
dotnet run --project prototypes/inspect-web/msdl-proxy.Tests -c Release
```

Produce the prebuilt managed-API artifact used by deployment:

```bash
dotnet publish prototypes/inspect-web/msdl-proxy/MsdlProxy.csproj \
  -c Release \
  --output artifacts/inspect-web-publish/api
```

The staging, CoreCLR, and promotion workflows deploy that artifact through
`api_location` with Azure's app and API builds disabled. Artifact upload
explicitly includes the hidden `.azurefunctions` runtime dependencies, and
every post-download deployment check requires the generated extension loader.
No Container App, container registry, linked backend, CORS configuration, or
separate Azure resource is required.
