# dotnet-inspect browser prototype

This prototype explores a type-first, keyboard-driven browser experience for
`dotnet-inspect`. It opens on the types exposed by the selected
package/framework compile assets, with public types selected by default. A small
JavaScript shell and static fixture data originally established the workspace
model; the current prototype now queries real package API surfaces through the
.NET WebAssembly engine.

## Run

Follow the browser-WASM instructions below. The JavaScript shell requires the
published engine assets and is no longer independently fixture-backed.

## Interaction model

- Package tabs establish scope; the framework selector chooses the API surface.
- The left pane filters and navigates types by namespace, kind, accessibility,
  and library.
- Package, type, and member scopes expose their own product-backed lens strip.
- Public member rows collapse overloads. Selecting a concrete overload and
  opening Source uses the product authored-source acquisition contract. The
  browser accepts checksum-verified local source but does not fetch
  artifact-supplied SourceLink URLs because browser Wasm cannot enforce the
  product's DNS/IP SSRF boundary; that typed absence is disclosed before
  falling back to dotnet-inspect decompilation.
- Member Overview shows the product-owned stable selector, anchor digest, and
  canonical signature with copy actions.
- The `demo` action loads `Microsoft.Extensions.DependencyInjection.Abstractions`
  and `Microsoft.Extensions.Options`, selects `AddSingleton:4`, and opens its
  workspace-wide Call graph.
- Opening Call graph builds a bounded caller/callee graph and renders the
  product-owned, format-neutral projection through browser-host Mermaid
  lowering. Caller discovery
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
- Number keys switch the active scope's lenses when an input is not focused.
- `share` copies the package, version, framework, type, and lens selection.
- The Taste popover and Settings page render their style groups, summaries, and
  ordering from the product-owned `StyleOptionCatalog`; the browser does not
  restate that taxonomy.

The prototype keeps package scope, target framework, selected type, and lens
as separate axes. Package and framework changes issue new engine queries while
type filtering and selection remain local over the returned public surface.
Downloaded package bytes are retained in a bounded session cache shared by API,
documentation, source, call-graph, and Facts queries. The cache holds at most
12 packages or 128 MB and evicts the least recently used entry; an individual
package is rejected above 64 MB. Runtime-pack assemblies have a separate
16-entry/128-MB least-recently-used cache, and range responses plus expanded
archive entries are capped before retention. Assembly materialization is
limited to 128 entries and 128 MB expanded in aggregate; package manifests and
documentation XML are limited to 16 MB before secure parsing. Shared
workspaces and resident package surfaces are deduplicated by package, version,
and framework and retain at most 12 package models.

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
Free `dotnet-inspect-web` Azure Static Web App on every push to `main`. The
deployed site is:

```text
https://ambitious-field-014c33f1e.7.azurestaticapps.net
```

The workflow builds the browser-Wasm project itself, then uploads the prebuilt
`wwwroot` with Azure's app build disabled. Its deployment credential is the
`AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB` GitHub Actions secret. The Azure
Static Web App resource's production Branch setting is `main`.

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
overload queries its documentation entry. Source resolution uses the typed
authored-source outcome, discloses the browser host's remote-fetch restriction,
and falls back to decompiling the matching implementation asset. The Call graph
member section scans
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
