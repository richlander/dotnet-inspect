# `ts-jsexport`

`ts-jsexport` generates an application-facing TypeScript facade from a
compiled .NET assembly's authenticated `[JSExport]` surface. It preserves much
more than the raw JavaScript ABI: asynchronous results, nullability, callbacks,
source-generated JSON shapes, direction-specific input and output contracts,
optional JSON members, unions, semantic string types, and exact runtime
dispatch identities can all cross the boundary as TypeScript.

The tool reads metadata and IL but never loads or executes the inspected
assembly. It generates TypeScript source; the consumer's TypeScript compiler
and .NET Browser/Wasm runtime remain the owners of JavaScript emission,
bundling, runtime configuration, and hosting.

`ts-jsexport` is currently built from repository source and is not distributed
on NuGet. Adoption outside this repository is therefore possible today, but it
is a source-integration workflow rather than a packaged-tool experience.

## The approach

The .NET runtime already makes `[JSExport]` methods callable through
`getAssemblyExports()`. That raw object is intentionally application-agnostic:
it knows that a method accepts or returns `string`, for example, but not that
the string is a JSON document with a particular source-generated contract.

`ts-jsexport` adds a typed facade above that ABI:

```text
compiled producer assembly
        |
        | metadata, generated runtime glue, and authenticated JSON evidence
        v
ILInspector.JsExportSurface
        |
        | target-language-neutral export and wire facts
        v
ts-jsexport
        |
        | deterministic TypeScript source
        v
consumer-owned tsc
        |
        +-- JavaScript implementation
        `-- optional .d.ts declarations
```

The generator distinguishes three related views instead of pretending they
are identical:

1. **Raw interop view** — the values actually marshalled by .NET, such as
   `(requestJson: string) => Promise<string>`.
2. **JSON wire view** — the authenticated System.Text.Json input and output
   shapes carried inside those strings.
3. **Public facade view** — the TypeScript API authors should call, such as
   `(request: SearchRequest) => Promise<SearchResult>`.

This separation keeps the runtime ABI honest while giving TypeScript the
highest-fidelity contract that the compiled producer can prove.

## What fidelity is preserved

The generated facade currently preserves supported forms of:

- exact `[JSExport]` membership, declaring-type paths, overloads, parameter
  order, and generated runtime dispatch keys;
- synchronous values, `Task`/`Task<T>` as `Promise`-based APIs, nullability,
  arrays, and synchronous `Action`/`Func` callbacks;
- System.Text.Json source-generated DTOs, records, enums, property names,
  constructor binding, collections, dictionaries, and recursive records;
- readonly JSON snapshots, including `ReadonlyArray<T>` and
  `Readonly<Record<string, T>>`;
- the distinction between a required nullable property and an optional
  property whose key may be absent;
- different deserialize and serialize shapes as `TypeNameInput` and
  `TypeNameOutput` when one managed type is direction-sensitive;
- supported union and serialize-only polymorphic contracts as native
  TypeScript unions;
- typed JSON inputs and parsed outputs while the private managed ABI remains
  string-valued;
- deferred complete JSON documents as `JsonText<T>` when the consumer, rather
  than the facade, must own parsing or bounded admission;
- semantic strings such as `DateTimeOffsetString` and `InertString` as opaque
  brands rather than undifferentiated `string`; and
- deterministic, collision-safe TypeScript identifiers derived from complete
  managed identities.

Unsupported or ambiguous evidence fails visibly instead of being widened to a
plausible-looking but weaker declaration. The complete contract and rejection
boundaries are owned by
[`docs/design/ts-jsexport.md`](../../docs/design/ts-jsexport.md).

## A minimal producer boundary

The producer remains an ordinary Browser/Wasm assembly with static
`[JSExport]` methods and source-generated JSON metadata:

```csharp
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using TsJsExport;

public sealed record SearchRequest(string Query);

public sealed record SearchResult(
    string Query,
    int MatchCount);

[JsonSerializable(typeof(SearchRequest))]
[JsonSerializable(typeof(SearchResult))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class SearchJsonContext : JsonSerializerContext;

[JsExportJsonInput(
    nameof(SearchExports.Search),
    "requestJson",
    typeof(SearchRequest))]
[JsExportJsonOutput(
    nameof(SearchExports.Search),
    typeof(SearchResult))]
[SupportedOSPlatform("browser")]
public static partial class SearchExports
{
    [JSExport]
    public static string NormalizeQuery(string query) =>
        query.Trim();

    [JSExport]
    public static async Task<string> Search(string requestJson)
    {
        SearchRequest request = JsonSerializer.Deserialize(
            requestJson,
            SearchJsonContext.Default.SearchRequest)
            ?? throw new ArgumentException("A search request is required.");

        SearchResult result =
            await SearchOperations.RunAsync(request);

        return JsonSerializer.Serialize(
            result,
            SearchJsonContext.Default.SearchResult);
    }
}
```

`SearchOperations.RunAsync` represents application-owned network, storage, or
other naturally asynchronous work; it is not a generator hook.

The SDK runtime exposes each operation through `getAssemblyExports()`.
**Runtime shape** below describes the values crossing that SDK-owned boundary;
it is not the name of a generated type. The actual runtime aggregate also uses
the managed declaring-type path and an opaque dispatch key.

| Managed export | SDK runtime value shape | Generated public facade |
| --- | --- | --- |
| `NormalizeQuery` | `(query: string) => string` | `normalizeQuery(query: string): string` |
| `Search` | `(requestJson: string) => Promise<string>` | `search(requestJson: SearchRequest): Promise<SearchResult>` |

The synchronous export remains synchronous. The asynchronous export remains
asynchronous because its application work is genuinely asynchronous, not
because facade generation requires a `Promise`. Its wrapper serializes
`SearchRequest`, invokes the exact runtime export, parses the returned JSON,
and exposes readonly producer-owned DTO declarations.

`JsExportJsonInput` and `JsExportJsonOutput` declare associations the raw
signature cannot express. Direct serializer flow can also be inferred when it
is complete and positionable; explicit declarations let parsing or
coordination move behind helpers without degrading the public contract.

## Adopt it in another project

### 1. Meet the producer prerequisites

Use a .NET SDK and Browser/Wasm runtime pack that provide
`System.Runtime.InteropServices.JavaScript`, `[JSExport]`, `dotnet.js`, and the
matching SDK-owned `dotnet.d.ts`.

The generation input must be the compiled assembly, not just C# source.
Supported JSON contracts must use System.Text.Json source generation. The tool
authenticates compiled export publication and wire evidence; it does not infer
contracts from naming conventions or nearby DTOs.

### 2. Reference the producer contract

Until the contract is packaged, reference the dependency-free repository
project from assemblies that declare facade roots or JSON associations:

```xml
<ProjectReference
  Include="path/to/dotnet-inspect/src/TsJsExport.Contracts/TsJsExport.Contracts.csproj" />
```

Only `TsJsExport.Contracts` belongs in the producer graph. The generator,
metadata inspection, analysis, and TypeScript-emitter projects are build-time
implementation and must not enter the browser application's runtime closure.

### 3. Generate one facade

Build the producer, then invoke the repository tool:

```bash
dotnet run --project path/to/dotnet-inspect/src/ts-jsexport -c Release -- \
  path/to/Search.dll \
  --runtime-module ./_framework/dotnet.js \
  --warnings-as-errors \
  --output generated/search.ts
```

`--runtime-module` is copied into the generated import and must be the module
specifier by which the consumer reaches the matching SDK runtime. If
`--output` is omitted, the TypeScript is written to stdout. For file output,
the parent directory must already exist.

Direct file output is published atomically only after the complete surface is
authenticated and mapped. An unsupported surface leaves an existing
destination unchanged.

### 4. Generate a closed facade set

For several independently compiled facade assemblies, declare one context in a
producer assembly. In this example, `SearchExports` and `ReportingExports`
anchor different assemblies:

```csharp
using TsJsExport;

[JsExportRoot(typeof(SearchExports))]
[JsExportRoot(typeof(ReportingExports))]
internal sealed class BrowserJsExportContext;
```

Each root anchors its complete assembly export surface; it does not select only
the named type. Generate the context into a fresh directory:

```bash
dotnet run --project path/to/dotnet-inspect/src/ts-jsexport -c Release -- \
  path/to/Browser.Host.dll \
  --context Browser.Host.BrowserJsExportContext \
  --assembly-search-path path/to/publish \
  --runtime-module ./_framework/dotnet.js \
  --warnings-as-errors \
  --output generated/facades
```

The compiled attributes are the authoritative closed set. Search locations
only resolve their exact assembly identities; the context assembly's directory
is not scanned implicitly. Generation fails as a whole for missing, ambiguous,
duplicate, empty, or unsupported roots. The destination must not already
exist, and success writes one canonical `<assembly-name>.ts` file per rooted
assembly.

Generate into a scratch directory and swap the successful result into place.
The consumer owns scratch cleanup, public module names, and the final
deployment transaction.

### 5. Compile against the matching SDK declaration

The generated source imports `dotnet.js` and type-checks against the
SDK-provided `dotnet.d.ts`; `ts-jsexport` does not copy or invent that generic
runtime contract.

Use the generated `.ts` directly in a TypeScript program, or compile it to
`.js` and optionally emit `.d.ts` for a package or other compiled-module
boundary. Strict compiler settings are recommended; enabling
`exactOptionalPropertyTypes` preserves the intended distinction between an
absent JSON key and a present `undefined` value.

The consumer remains responsible for module resolution, compiler
configuration, bundling, generated-artifact placement, and hosting.

### 6. Initialize and call the facade

Each generated module exports:

- `createRuntime()`, which creates one narrow runtime handle;
- `initializeRuntime(runtime?)`, which acquires and validates the assembly
  exports through a supplied or locally created runtime;
- `runEntryPoint(mainAssemblyName?, args?)`, which explicitly forwards to
  `runMain()`; and
- one typed function per supported `[JSExport]` operation.

For one facade:

```ts
import {
  initializeRuntime,
  search,
} from "./generated/search.js";

await initializeRuntime();

const result = await search({ query: "metadata" });
console.log(result.matchCount);
```

For several facades, create the runtime once and pass the same handle to each
module. Initialize them in the order required by the selected runtime:

```ts
import * as host from "./generated/Browser.Host.js";
import * as search from "./generated/Search.js";

const runtime = host.createRuntime();
await host.initializeRuntime(runtime);
await search.initializeRuntime(runtime);
```

Initialization is terminal and single-flight within each generated module.
The facade does not configure the SDK builder, invoke a managed operation, or
run the entry point implicitly.

### 7. Make drift a build failure

Treat generated TypeScript as a derived contract:

1. regenerate from the exact compiled producer;
2. use `--warnings-as-errors` for production generation;
3. compile the result against the selected SDK's `dotnet.d.ts`;
4. type-check authored TypeScript consumers;
5. compare canonical generated output with the checked-in or packaged
   artifact; and
6. run at least one emitted-JavaScript canary against the real runtime.

This catches stale declarations, lost serializer evidence, unsupported
producer changes, SDK declaration mismatches, and authored consumers that have
not adopted a changed contract.

## Incremental migration from handwritten bindings

Adoption does not require replacing an entire JavaScript boundary at once:

1. Choose one export assembly and one real operation whose handwritten wrapper
   already has a known runtime behavior.
2. Generate a facade beside the existing wrapper and compare the raw call,
   JSON payload, failure behavior, and public TypeScript signature.
3. Add compiler and runtime canaries before changing production imports.
4. Move authored consumers to generated operation and DTO imports one coherent
   feature at a time.
5. Add explicit JSON declarations only where implementation-flow inference is
   incomplete or would constrain a cleaner managed implementation.
6. Add canonical drift checking, then remove the replaced handwritten
   declarations and wrappers.
7. Introduce a facade context when the independently migrated assemblies form
   one intentional closed product set.

Keep application policy above the generated facade. Richer classes, workflows,
state management, retries, and presentation belong in authored TypeScript;
the generated layer intentionally remains a one-to-one typed view of
authenticated runtime exports.

## When it is not the right tool

`ts-jsexport` is not a C#-to-TypeScript compiler, a binding generator for
arbitrary public APIs, a network client generator, a runtime schema validator,
or a replacement for the .NET JavaScript interop ABI. It does not project
managed classes, constructors, inheritance, or implementation logic.

It is a fit when:

- a Browser/Wasm application already exposes static `[JSExport]` operations;
- TypeScript should consume those operations without parallel handwritten
  declarations;
- JSON DTO fidelity should come from the compiled producer and its
  source-generated serializer contracts; and
- unsupported or ambiguous contracts should stop generation rather than
  silently widen.

## Repository examples and validation

Inspect Web is the production showcase:

- `src/DotnetInspect.Web/InspectWebJsExportContext.cs` declares its closed
  facade set;
- `eng/generate-inspect-web-engine-facade.sh` generates, compiles, verifies,
  and installs the canonical artifacts;
- `inspect-web/DotnetInspect.Web/facades/` contains the generated TypeScript;
  and
- authored consumers under `inspect-web/src/` import those facade contracts.

Run the generator tests in Release:

```bash
dotnet run --project tests/ILInspector.JsExportSurface.Tests -c Release
```

After installing the existing Inspect Web dependencies, run the generated
TypeScript compiler and emitted-JavaScript runtime gates:

```bash
eng/test-ts-jsexport-typescript.sh
```

That gate resolves the runtime import against the installed SDK's
`dotnet.d.ts`; it does not provide a generator-owned runtime declaration.
