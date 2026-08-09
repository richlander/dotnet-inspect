# Browser architecture spike

## Recommendation

Use a small DOM-based JavaScript UI with a .NET 11 inspection engine running in
a Web Worker.

The engine should use the supported .NET 11 `browser-wasm` runtime. Start with
the interpreter for the quickest inner loop, then measure browser AOT for the
hot inspection paths after the product surface has been trimmed.

```text
nuget.org deep link
        |
        v
JavaScript UI and command completion
        |
        | typed request/result envelopes
        v
Web Worker: .NET 11 browser-wasm engine
        |
        +-- package acquisition and bounded cache
        +-- DotnetInspector.Packages / Services
        +-- ILInspector.Metadata
        +-- selected Analysis / Findings producers
```

The worker boundary keeps package decompression, metadata scans, and analysis
off the UI thread. It also prevents the UI from depending on the runtime's
specific JavaScript interop mechanism.

## What NuGet Package Explorer establishes

The NuGet Package Explorer repository was inspected at commit
`a46ed5495e90c1167469191a3ef8ad717a62ea8d`.

- The browser app currently targets `net10.0-browserwasm` through Uno.
- It uses Mono `InterpreterAndAOT`, not NativeAOT.
- Uno, Skia, XAML controls, Monaco, MEF composition, and compatibility layers
  are appropriate for its shared desktop application but create a much larger
  UI/runtime surface than this inspection experience needs.
- It enables an IndexedDB-backed filesystem and uses the NuGet client to
  download and open packages.
- Its public route is `/packages/{id}/{version}`; `/packages?q=...` is the
  package-search route.

The route is worth matching because nuget.org already emits it and users can
move between the two tools without learning a second link shape. The package
tree and shared cross-platform XAML layer are not constraints this app needs to
inherit.

## Runtime decision

There are three different ideas often called ".NET AOT to Wasm":

| Path | Browser DOM host | Status for this prototype |
| --- | --- | --- |
| Mono browser AOT | Yes | Supported baseline for .NET 11 |
| CoreCLR/RyuJIT browser Wasm | Early .NET 11 preview | Track, but do not make it a prototype dependency |
| NativeAOT-LLVM browser Wasm | Experimental branch | Rejected: no maintained browser product path |
| NativeAOT for `wasi-wasm` | No direct DOM/browser host | Useful for WASI components, not the frontend engine |

NativeAOT-LLVM can compile browser smoke tests, but its browser hosting work is
not an appropriate product dependency. Current activity is centered on WASI,
while the browser `dotnet.js` work remains an experimental runtimelab path.
The app will retain dotnet-inspect's trimming and NativeAOT-friendly product
constraints without depending on that runtime.

Create a focused engine spike before wiring the full app:

1. Target .NET 11 and expose one operation:
   `inspect package bytes -> assembly identities`.
2. Run it in a Web Worker and fetch one pinned package from nuget.org.
3. Publish both interpreter and browser-AOT variants.
4. Record compressed transfer size, first interaction, package download,
   metadata scan, and peak memory.
5. Keep the worker contract independent of Mono so the active CoreCLR/RyuJIT
   browser path can be evaluated later without changing the UI.

## Product boundaries

- Keep expensive, exhaustive, source-content, and network sections explicit,
  matching the CLI's progressive-disclosure contract.
- Do not pass CLI text into the engine. Parse commands in the UI into typed
  focus, source, section, and projection requests.
- Return the existing typed models or browser-specific projections over them;
  do not scrape Markdown or infer identity from display strings.
- Acquire package bytes once per immutable package identity and share them
  across section requests.
- Start with one selected TFM and one assembly. Multi-package scope should
  schedule independent requests and preserve provenance on every result.
- Treat package contents and metadata strings as untrusted input and render
  them as text, never HTML.

## Why not Uno or Avalonia first

The important reuse boundary is the inspection engine, not desktop UI code.
This prototype needs high-quality text input, lists, tables, code, focus
management, URLs, and accessibility—all native browser strengths. A JavaScript
shell avoids paying for a XAML/layout compatibility layer and makes command
completion easier to tune.

Uno or Avalonia becomes compelling only if a shared desktop/mobile UI becomes a
goal. That is separate from proving the browser inspection workflow.
