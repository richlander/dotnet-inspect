# Inspect Web managed API

This Azure Static Web Apps managed Function app hosts narrowly bounded
same-origin bridges for public providers that Inspect Web cannot read directly
because of browser CORS.

## MSDL symbol bridge

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

### MSDL security model

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

## Package-change evidence bridge

The package-change report needs NuGet.org Catalog documents and GitHub-reviewed
NuGet advisory documents. Neither provider exposes those responses to Browser
CORS, so the Browser transport rewrites admitted provider requests to:

```text
GET /api/package-changes/nuget?path=<admitted NuGet v3 path>
GET /api/package-changes/advisories?<reviewed NuGet advisory query>
```

The [Inspect Web public-evidence
bridge](../../docs/design/inspect-web-public-evidence-bridge.md) owns the
boundary. The routes accept no caller-selected URL or host. They construct
credential-free requests to compile-time NuGet.org and GitHub origins from
closed path and query grammars, disable redirects, require JSON, and bound
decoded responses to the existing consumer limits. Invalid input is rejected
before outbound traffic; provider status, timeout, transport failure, and
oversize remain distinct non-success outcomes. NuGet Catalog documents on the
fixed `/v3/catalog0/` routes may omit `Content-Type`; the bridge admits that
known provider behavior while the service index and advisory responses still
require JSON media types.

`PackageChangeProxyRequestValidatorTests`,
`PackageChangeProxyClientTests`, `PackageChangeProxyFunctionTests`, and
`BrowserEngineBoundaryTests` gate request admission, fixed authority, response
bounds, failure mapping, routing, and Browser request rewriting.

## Response security

Responses produced by the symbol and health functions carry these headers,
and package-change responses carry the same policy plus
`Cache-Control: no-store`. The policy covers validation failures, missing
content, oversized declarations, and handled upstream failures:

| Header | Value |
| --- | --- |
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `no-referrer` |
| `X-Frame-Options` | `DENY` |
| `Strict-Transport-Security` | `max-age=63072000; includeSubDomains` |

This function-owned policy covers public content returned from our origin. The
values match the static site's baseline, but Azure Static Web Apps does not
apply `globalHeaders` to managed API responses. The focused Function tests
check the headers, status codes, and successful bodies in Release. Responses
generated outside these functions, such as platform routing errors or
unhandled host failures, are outside this gate.

## Development

Run the executable xUnit project:

```bash
dotnet run --project tests/MsdlProxy.Tests -c Release
```

This is a Microsoft Testing Platform executable. Use `--filter-class` and
`--filter-method` after `--` for focused selections.

Produce the prebuilt managed-API artifact used by deployment:

```bash
dotnet publish src/MsdlProxy/MsdlProxy.csproj \
  -c Release \
  --output artifacts/inspect-web-publish/api
```

The staging, CoreCLR, and promotion workflows deploy that artifact through
`api_location` with Azure's app and API builds disabled. Artifact upload
explicitly includes the hidden `.azurefunctions` runtime dependencies, and
every post-download deployment check requires the generated extension loader.
No Container App, container registry, linked backend, CORS configuration, or
separate Azure resource is required.
