# dotnet-inspect browser prototype

This prototype explores a type-first, keyboard-driven browser experience for
`dotnet-inspect`. It opens on the public types exposed by the selected
package/framework compile assets. A small JavaScript shell and static fixture
data originally established the workspace model; the current prototype now
queries real package API surfaces through the .NET WebAssembly engine.

## Run

Follow the browser-WASM instructions below. The JavaScript shell requires the
published engine assets and is no longer independently fixture-backed.

## Interaction model

- Package tabs establish scope; the framework selector chooses the API surface.
- The left pane filters and navigates public types grouped by namespace.
- Selecting a type preserves that context across API, source, metadata, IL,
  dependency, and Finding lenses.
- Public member rows collapse overloads. Selecting a concrete overload and
  opening Source immediately attempts checksum-verified SourceLink source,
  then falls back to dotnet-inspect decompilation.
- Member Overview shows the product-owned stable selector, anchor digest, and
  canonical signature with copy actions.
- The `demo` action loads `Microsoft.Extensions.DependencyInjection.Abstractions`
  and `Microsoft.Extensions.Options`, selects `AddSingleton:4`, and opens its
  workspace-wide Call graph.
- Opening Call graph builds a bounded caller/callee graph and renders the
  shared product-owned Mermaid projection in the browser. Caller discovery
  spans the implementation assemblies from every package currently loaded in
  the workspace; callee traversal currently remains within the target
  assembly.
- Opening Facts lazily analyzes only the selected overload and separates
  objective method, allocation, call, safety, and exception evidence from
  ranked Performance Triage judgments.
- The package Dependencies lens combines declared NuGet dependency groups with
  the selected assembly's flat direct-reference list. Assembly references come
  from the same typed `AssemblyReferencesQuery` used by the CLI. Package compile
  asset selection comes from the shared content-shaped
  `PackageCompileAssetSelector`; JavaScript carries its opaque asset identity
  without parsing `ref/` or `lib/` paths.
- `Cmd/Ctrl+K` focuses the persistent command prompt.
- `Cmd/Ctrl+F` or `/` focuses the type filter.
- Arrow keys select a completion, `Tab` accepts it, and `Enter` runs it.
- Arrow keys or `j`/`k` navigate the type index.
- Number keys `1` through `6` switch lenses when an input is not focused.
- `share` copies the package, version, framework, type, and lens selection.
- The Taste popover and Settings page render their style groups, summaries, and
  ordering from the product-owned `StyleOptionCatalog`; the browser does not
  restate that taxonomy.

The prototype keeps package scope, target framework, selected type, and lens
as separate axes. Package and framework changes issue new engine queries while
type filtering and selection remain local over the returned public surface.
Downloaded package bytes are retained in a bounded session cache shared by API,
documentation, source, call-graph, and Facts queries. The cache holds at most
four packages or 64 MB and evicts the least recently used entry.

Package acquisition uses nuget.org by default. If the browser cannot reach the
active source, the app asks for an anonymous, CORS-enabled NuGet v3 mirror
service-index URL, validates its `PackageBaseAddress`, and retries through the
mirror. The service-index URL is stored in browser local storage and can be
replaced or cleared under Settings. Package search uses the mirror's
`SearchQueryService` when it publishes one. Symbol packages remain a separate
NuGet.org capability because `PackageBaseAddress` does not serve `.snupkg`
payloads; when that endpoint is blocked, Source falls back to decompilation.

When a non-default mirror is active, the opaque share packet (`w`) includes its
service-index URL (`n`). Opening that link validates the same way Settings does
and applies the mirror for **this session only**, with a banner to **Keep** it in
local storage or dismiss the offer. History back/forward may re-apply a packet
mirror in-session but does not rewrite local storage, so Settings → Use nuget.org
stays durable across Back. Shares from nuget.org omit `n` so they do not wipe a
recipient's stored corporate mirror. A shared mirror that fails validation falls
back to the recipient's stored source (if any) and surfaces a notice. Settings
rewrite the address-bar packet before reload so a stale `n` is not the active
entry after a source change.

## Run the .NET 11 browser-WASM prototype

Install the experimental browser workload selected by the repository SDK:

```bash
dotnet workload install wasm-experimental
cd prototypes/inspect-web/engine
dotnet run -c Release
```

Open `http://127.0.0.1:5198`. The browser downloads `System.Text.Json` version
`10.0.0` directly from NuGet's flat-container API, selects its `net10.0`
compile assets, and uses `AssemblyInspectionSession.ApiSurface()` to populate
the public type workspace. Remote addresses require HTTPS because the .NET
loader uses secure-context browser APIs.

For a network that blocks nuget.org, open Settings and enter its permitted
mirror's NuGet v3 service index, for example
`https://mirror.example/nuget/v3/index.json`. The mirror must allow anonymous
browser requests and CORS from the site's origin; credential-bearing URLs are
rejected and are never written to local storage. Some upstream proxies also
refuse to ingest a cold package from a browser request. Such a mirror must be
warmed by a non-browser client before the browser can download that package;
the app reports this as a mirror failure rather than prompting for another
mirror.

Create a deployable static bundle with:

```bash
dotnet publish -c Release
```

The .NET 11 preview Emscripten wrapper currently mishandles an SDK packs path
that contains whitespace. If that applies to the local SDK installation, pass
`EmscriptenSdkToolsPath` pointing to a no-whitespace link to the installed
Emscripten `tools` directory.

## Deploy

`.github/workflows/deploy-inspect-web.yml` publishes and deploys this app to the
Free `dotnet-inspect-web` Azure Static Web App on every push to
`prototype/wasm-command-ui`. The deployed site is:

```text
https://ambitious-field-014c33f1e.7.azurestaticapps.net
```

The workflow builds the browser-Wasm project itself, then uploads the prebuilt
`wwwroot` with Azure's app build disabled. Its deployment credential is the
`AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB` GitHub Actions secret. To make
`main` the production source later, change the workflow's push branch and the
Azure Static Web App resource's Branch setting from
`prototype/wasm-command-ui` to `main`.

## Prototype boundary

Package acquisition, framework selection, public types, public members, and
lazy per-overload XML documentation, lazy source resolution, package dependency
groups, and direct assembly references are real engine results. Direct
references run through the shared typed query registry and expose failure
rather than returning a successful empty list. The product-selected compile
asset is shared by the package surface and Dependencies request; JavaScript
does not independently rank frameworks or choose between `ref/` and `lib/`.
`LayeringTests.BrowserDependencies_UsesProductQueriesAndCompileAssetSelection`
enforces that product-query and product-selection wiring. The initial API
surface carries only metadata-owned XML documentation identities; selecting an
overload queries its documentation entry. Source resolution prefers SourceLink content
authenticated by the portable-PDB checksum and falls back to decompiling the
matching implementation asset. The Call graph member section scans
implementation IL and lists direct in-assembly callers and callees, then
projects them through `ILInspector.CallGraph` so graph identity, direction,
cycles, boundaries, and escaping are not reimplemented in JavaScript. The Facts
member section performs method-scoped Analysis and exposes its failures rather
than returning an empty result. Cancellation, persistent caching, worker
isolation, package/type-wide Findings, graph interaction, and IL remain
integration work; their lenses report that they have not been queried rather
than displaying fixture results.

See [architecture-spike.md](architecture-spike.md) for the proposed .NET 11
browser engine and the NativeAOT decision.
