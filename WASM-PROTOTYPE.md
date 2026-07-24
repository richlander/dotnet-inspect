# dotnet-inspect Browser Prototype

This repository contains a browser prototype for `dotnet-inspect`. It runs the
inspection engine compiled to WebAssembly with .NET 11 and presents a keyboard-
oriented, TUI-inspired interface for exploring NuGet packages.

The prototype is intentionally built around the real inspection engine rather
than fixture data. Packages are downloaded in the browser, inspected with the
same metadata, source, decompilation, findings, and call-graph services used by
`dotnet-inspect`, and cached locally for reuse.

## Current experience

- Open one or more NuGet packages, using the latest stable version when no
  version is supplied.
- Browse namespaces and types, with overloads collapsed until a member is
  selected.
- View an API Overview styled after Microsoft Learn, including signatures,
  documentation, source links, and applicable framework information.
- Acquire source through SourceLink when available, falling back to
  `dotnet-inspect` decompilation.
- Inspect member Facts and identity data, including the canonical member anchor.
- Explore a combined caller/callee graph across all loaded package assemblies.
- Switch between light and dark presentation; the graph legend explains the
  semantic relationship colors.
- Use the command prompt and keyboard navigation for common actions.

The cross-package call-graph demo loads
`Microsoft.Extensions.DependencyInjection.Abstractions` and
`Microsoft.Extensions.Options`, then navigates to a member with callers across
the package boundary.

## Running locally

The browser host lives under `prototypes/inspect-web`. Build or publish the
engine with the .NET 11 SDK selected by the repository, then serve the host and
the generated Wasm assets over HTTP(S). WebAssembly, module scripts, and
SourceLink requests require a real origin; opening the HTML file directly is
not supported.

The development server should be bound to an externally reachable interface
when testing from another machine. HTTPS is recommended when the browser's
secure-context requirements apply.

## Remaining backlog

### Call graph

- Add hover details containing the complete member identity and signature.
- Make graph nodes activate the exact overload and provide reliable back/forward
  navigation.
- Integrate the merged caller and callee graph with zoom/pan and a depth control.
- Add Mermaid or graph-level filtering for callers, callees, packages, and
  assemblies.
- Discover and offer additional package dependencies when an assembly is
  referenced but not yet loaded.
- Add more compelling cross-package demos and an overview of integrations
  across the complete workspace.

### Package and API exploration

- Improve package/version completion and package loading progress.
- Add framework/TFM comparison and preview-version selection.
- Add richer member-level findings, facts, and implementation diffs.
- Link type and member references between the Overview, Facts, Source, and Call
  graph views.

### Source and documentation

- Show source provenance and checksum status more prominently.
- Expand Learn-style documentation rendering for remarks, examples, inherited
  members, and version/moniker selection.
- Add source navigation and syntax-aware interactions beyond static highlighting.

### Product and engineering

- Add browser-level automated tests and a visual regression baseline.
- Measure startup, package acquisition, inspection, and graph rendering costs;
  keep expensive work explicit rather than eager.
- Resolve the current .NET 11 preview Emscripten wrapper issue when the SDK is
  installed under a path containing spaces (for example, macOS's
  `Application Support` directory).
- Define a deployment model, package cache limits, telemetry/privacy policy,
  and nuget.org deep-link integration.

