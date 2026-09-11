# ts-jsexport

`ts-jsexport` generates one typed TypeScript facade from a compiled assembly's
authenticated `[JSExport]` surface. It reads metadata and IL but never loads or
executes the inspected assembly.

The tool is currently built from repository source and is not distributed on
NuGet.

## Usage

```bash
dotnet run --project src/ts-jsexport -c Release -- \
  path/to/Exports.dll \
  --runtime-module ./_framework/dotnet.js \
  --output generated/exports.ts
```

`--runtime-module` is the module specifier emitted in the generated import. If
`--output` is omitted, the TypeScript is written to stdout. File output is
published atomically only after the complete surface is authenticated and
mapped; an unsupported surface leaves an existing destination unchanged.

`JsExportRoot` declarations and context mode are the two halves of one
generator mechanism, not separate features: the attributes compile the closed
facade recipe, and `--context` tells `ts-jsexport` which exact recipe type to
execute. Neither concept appears in the generated TypeScript.

To use that mechanism for a closed set of independent facades in this
repository, add a project reference to the dependency-free producer contract:

```xml
<ProjectReference
  Include="path/to/src/TsJsExport.Contracts/TsJsExport.Contracts.csproj" />
```

Then declare a compiled context:

```csharp
using TsJsExport;

[JsExportRoot(typeof(BrowserHostExports))]
[JsExportRoot(typeof(BrowserPackageExports))]
internal sealed class BrowserJsExportContext;
```

Each rooted type anchors its entire assembly, including supported `[JSExport]`
methods declared by other types. Generate the context into a new directory:

```bash
dotnet run --project src/ts-jsexport -c Release -- \
  path/to/Browser.Host.dll \
  --context Browser.Host.BrowserJsExportContext \
  --assembly-search-path path/to/publish \
  --runtime-module ./_framework/dotnet.js \
  --output generated/facades
```

The compiled attributes are the authoritative set; search locations only
resolve their exact assembly identities. The tool does not implicitly scan the
context assembly's directory. Context generation fails as a whole for missing,
ambiguous, duplicate, empty, or unsupported roots. The output path must not
exist, and successful generation writes exactly one canonical
`<assembly-name>.ts` file per rooted assembly. Consumers own scratch-directory
cleanup and any final deployment swap. The contract is currently a source-only
repository project; separately packaging it is future release scope.

The generated module exports:

- readonly producer-owned DTO and enum declarations;
- `initializeRuntime()`, which creates and validates one terminal,
  module-local runtime acquisition;
- `runEntryPoint(mainAssemblyName?, args?)`, which explicitly forwards to the
  initialized runtime; and
- one typed function per supported `[JSExport]` operation.

The facade imports the SDK-owned `dotnet` builder and `RuntimeAPI` type but does
not configure the builder. The consumer owns runtime configuration, TypeScript
compiler settings, module resolution, generated-artifact placement, and
hosting. Initialization never invokes a managed operation or `runMain()`.

[`docs/design/ts-jsexport.md`](../../docs/design/ts-jsexport.md) owns the
architecture and supported contract. Inspect-web adoption is tracked
separately by
[#5003](https://github.com/richlander/dotnet-inspect/issues/5003).

## Validation

Run the generator tests in Release:

```bash
dotnet run --project tests/ILInspector.JsExportSurface.Tests -c Release
```

After installing the existing inspect-web dependencies, run the generated
TypeScript compiler and emitted-JavaScript runtime gates:

```bash
eng/test-ts-jsexport-typescript.sh
```

That gate resolves the runtime import against the installed SDK's
`dotnet.d.ts`; it does not provide a generator-owned runtime declaration.
