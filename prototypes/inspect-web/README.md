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
- `Cmd/Ctrl+K` focuses the persistent command prompt.
- `Cmd/Ctrl+F` or `/` focuses the type filter.
- Arrow keys select a completion, `Tab` accepts it, and `Enter` runs it.
- Arrow keys or `j`/`k` navigate the type index.
- Number keys `1` through `6` switch lenses when an input is not focused.
- `share` copies the package, version, framework, type, and lens selection.

The prototype keeps package scope, target framework, selected type, and lens
as separate axes. Package and framework changes issue new engine queries while
type filtering and selection remain local over the returned public surface.
Downloaded package bytes are retained in a bounded session cache shared by API,
documentation, source, call-graph, and Facts queries. The cache holds at most
four packages or 64 MB and evicts the least recently used entry.

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

## Prototype boundary

Package acquisition, framework selection, public types, public members, and
lazy per-overload XML documentation and lazy source resolution are real engine
results. The initial API surface carries only metadata-owned XML documentation
identities; selecting an overload queries its documentation entry. Source
resolution prefers SourceLink content authenticated by the portable-PDB
checksum and falls back to decompiling the matching implementation asset. The
Call graph member section scans implementation IL and lists direct in-assembly
callers and callees, then projects them through `ILInspector.CallGraph` so graph
identity, direction, cycles, boundaries, and escaping are not reimplemented in
JavaScript. The Facts member section performs method-scoped Analysis and
exposes its failures rather than returning an empty result. Cancellation,
persistent caching, worker isolation, package/type-wide Findings, dependencies,
graph interaction, and IL remain integration work; their lenses report that
they have not been queried rather than displaying fixture results.

See [architecture-spike.md](architecture-spike.md) for the proposed .NET 11
browser engine and the NativeAOT decision.
