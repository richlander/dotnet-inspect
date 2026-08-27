# `ts-bindgen` TypeScript facade generation

Status: **proposed**. The current `tsbindgen` implementation emits TypeScript
declarations and can also emit a JavaScript runtime wrapper. This document
defines the replacement architecture; its target properties are **unverified**
until the gates under [Acceptance](#acceptance) exist.

This is the owning document for the `ts-bindgen` TypeScript facade. It defines
how one
[`JsExportSurface`](../../src/ILInspector.JsExportSurface/README.md) becomes one
TypeScript source module. It does not own .NET JavaScript interop thunk
generation, `JsExportSurface` authentication, TypeScript compiler behavior, or
browser hosting.

## Decision

`ts-bindgen` generates TypeScript source from a compiled .NET assembly's
`[JSExport]` surface. The generated module is an opinionated developer facade
over the already-callable API returned by `getAssemblyExports()`.

The consumer's TypeScript compiler turns that source into executable
JavaScript. A consumer may also emit `.d.ts` declarations when it maintains a
compiled module or package boundary. Within one TypeScript source environment,
the generated `.ts` file supplies both implementation and types.

`ts-bindgen` does not generate derived JavaScript or declarations itself:

```text
compiled .NET assembly
        |
        v
ILInspector.JsExportSurface
        |
        | runtime-publishable functions and authenticated JSON wire contracts
        v
ts-bindgen
        |
        | TypeScript source
        v
consumer-owned tsc
        |
        +-- executable JavaScript
        `-- optional .d.ts at a compiled module boundary
```

## Roles and execution phases

This repository contains three similarly named but operationally separate
parts:

1. **`ts-bindgen` is a build-time tool.** It reads a compiled assembly as
   metadata and IL data and generates a TypeScript facade for the type paths and
   static methods represented by its `[JSExport]` surface.
2. **Inspect-web is a consumer of the tool.** Its managed
   `InspectWeb.Engine.dll` exposes dotnet-inspect functionality through one
   `InspectionEngine` type containing static `[JSExport]` methods.
3. **`ILInspector.JsExportSurface` is part of the tool's implementation.** It
   is a host-side library over Metadata- and Analysis-owned facts. It constructs
   the target-language-neutral export and wire-evidence model consumed by
   `ts-bindgen`.

`ILInspector.JsExportSurface` is not the generated binding, an API that
application TypeScript calls, or a library required by the browser runtime. The
inspected engine assembly does not execute it. The tool uses it while generating
source, before the resulting JavaScript and managed application meet in the
browser.

The phases compose as follows:

```text
ts-bindgen process on a developer or CI host

InspectWeb.Engine.dll --read as PE/IL data--> Metadata and Analysis facts
                                                    |
                                                    v
                                          JsExportSurface model
                                                    |
                                                    v
                                           TypeScript emitter
                                                    |
                                                    v
                                        inspect-web-engine.ts


browser execution

inspect-web-engine.js --> dotnet.js / dotnet.runtime.js
                                      |
                                      v
                             InspectWeb.Engine.dll
                                      |
                                      v
                         dotnet-inspect product libraries
```

The compiler step between the diagrams derives
`inspect-web-engine.js` from `inspect-web-engine.ts`. Neither the
`ts-bindgen` executable nor its `ILInspector.JsExportSurface` implementation
library crosses into the browser execution phase.

## Name

The command, tool package, and user-facing product name are `ts-bindgen`.
The hyphen follows the established `*-bindgen` naming convention and
distinguishes this tool from the unrelated `tsbindgen` package on NuGet.

`ts-bindgen` names the tool's target-language personality, not every constraint
on its input. Its one-line contract supplies the necessary scope:

> Generate a TypeScript facade from a .NET assembly's `[JSExport]` surface.

`ts-jsexport-bindgen` would encode more of the current input but would still be
incomplete: the tool also authenticates and projects System.Text.Json wire
contracts that are not present in `[JSExport]` signatures. Encoding both input
mechanisms would make the name longer without making the contract complete.
Names such as `jsexport-bindgen` or `dotnet-js-bindgen` would instead suggest
ownership of the low-level JavaScript ABI glue that remains with .NET.

The shorter name is therefore deliberate. Documentation and diagnostics must
consistently say that the input is a compiled .NET assembly's `[JSExport]`
surface; the name alone must not be used to imply arbitrary
C#-to-TypeScript generation. Support and evidence requirements belong in the
technical contract rather than the product name or tagline.

## Why this layer generates TypeScript

The .NET SDK already supplies the unopinionated JavaScript interop layer.
For an attributed method, the SDK-generated assembly contains a method-specific
wrapper, stub, marshalling descriptor, and registration initializer.
`dotnet.js` and `dotnet.runtime.js` provide the generic browser runtime that
turns those registrations into the object returned by
`getAssemblyExports()`.

That raw object is usable without `ts-bindgen`:

```js
const runtime = await dotnet.create();
const exports =
  await runtime.getAssemblyExports("InspectWeb.Engine");
const json =
  await exports.InspectionEngine.QueryPackage(
    packageId,
    version,
    targetFramework,
  );
```

Low-level binding generation should remain application-policy-neutral and
unopinionated. It targets JavaScript and marshals the declared runtime values,
but it should not infer that a string contains JSON, choose an application DTO,
or prescribe a frontend source language.

`ts-bindgen` sits above that boundary. It deliberately chooses TypeScript and
adds application-facing policy:

- TypeScript names and syntax;
- `Task<T>` and `ValueTask<T>` projection to `Promise<T>`;
- public wrapper signatures distinct from raw interop signatures;
- authenticated JSON parsing and exact wire-result types;
- readonly producer-owned JSON snapshots;
- initialization and one-runtime reuse; and
- consumer-facing DTO and enum declarations.

Generating JavaScript plus JSDoc would express those TypeScript decisions
indirectly and require the generator to own comment containment, JSDoc import
syntax, typedef allocation, and JavaScript-to-declaration synchronization.
Generating native TypeScript expresses the selected language directly and lets
the consumer's compiler own JavaScript and optional declaration emission.

## Ownership

| Owner | Owns | Does not own |
| --- | --- | --- |
| .NET JavaScript interop | `[JSExport]` selection, generated managed thunks, marshalling descriptors, registration, generic runtime support | application JSON meaning, TypeScript facade policy |
| `ILInspector.JsExportSurface` | C#-faithful export facts, runtime-publication evidence, authenticated JSON wire evidence | TypeScript names, syntax, wrappers, compiler configuration |
| `ts-bindgen` | deterministic TypeScript facade source from one `JsExportSurface` | thunk generation, runtime implementation, TypeScript compilation, browser publication |
| Consumer | TypeScript compiler configuration, derived artifacts, module resolution, and hosting | reinterpreting or weakening the `JsExportSurface` input |

These boundaries are intentionally asymmetric. `ts-bindgen` may reject an input
surface it cannot faithfully represent in TypeScript. It must not broaden
acceptance by reimplementing or weakening the evidence rules owned by
`ILInspector.JsExportSurface`.

The consumer compiler owns derived artifacts, but it does not own facade
semantics. Compiler configuration is outside this design.

## Three type views

One exported method can have three related but non-interchangeable type views.

### Raw interop view

This is what `getAssemblyExports()` actually exposes after .NET marshalling.
For example:

```csharp
[JSExport]
public static Task<string> QueryPackage(
    string packageId,
    string version,
    string targetFramework)
```

has the raw TypeScript view:

```ts
(
  packageId: string,
  version: string,
  targetFramework: string,
) => Promise<string>
```

The .NET interop layer knows only that the result is a string. It does not know
whether the string contains JSON.

### Wire view

When `ILInspector.JsExportSurface` authenticates the method body's serializer
flow and exact source-generated `JsonTypeInfo<T>`, the returned string has a
known JSON wire shape. That evidence may establish `BrowserPackage` as the
parsed result type.

Wire DTOs are producer-owned snapshots. Their properties are readonly, arrays
use `ReadonlyArray<T>`, and string-keyed dictionaries use
`Readonly<Record<string, T>>`. Direct JS-interop arrays remain mutable because
they are runtime values, not serialized snapshots.

### Public facade view

The exported TypeScript wrapper presents the application-level result:

```ts
export async function queryPackage(
  packageId: string,
  version: string,
  targetFramework: string,
): Promise<BrowserPackage> {
  const json = await requireManagedExports()
    .InspectionEngine
    .QueryPackage(packageId, version, targetFramework);
  const parsed: unknown = JSON.parse(json);
  return parsed as BrowserPackage;
}
```

The raw signature, parsed wire type, and public signature must remain explicit
in the generator model. Display text or a public return annotation must never
be used to reconstruct one of the other views.

## Trust boundaries in generated TypeScript

`getAssemblyExports()` is a runtime boundary whose application-specific shape
is not declared by the generic .NET JavaScript module. The generated module
therefore treats its result as `unknown` and contains one explicit assertion to
the internal managed-export structure generated from the same authenticated
surface.

`JSON.parse()` is another boundary. Its result is immediately treated as
`unknown`; only an authenticated wire contract permits the generated wrapper
to assert a more specific result type. A string return without that evidence
remains a string.

These assertions do not perform runtime validation. They state compile-time
facts established by producer-owned evidence. Unsupported, incomplete, or
ambiguous evidence fails generation visibly rather than producing `unknown`,
an empty interface, or an untyped success-shaped wrapper.

## Generated module

For one input assembly, `ts-bindgen` emits one self-contained TypeScript module
containing:

1. public enum and DTO declarations for reached wire contracts;
2. one private structural type for the raw `getAssemblyExports()` object;
3. private initialized-state storage and a narrowing accessor;
4. `initializeEngine`, which creates one runtime, captures its exports, awaits
   `runtime.runMain()`, and only then publishes initialized state;
5. one exported facade function per supported `[JSExport]` method; and
6. the exact JSON parse operation for each authenticated envelope.

### Correspondence, not managed API translation

The generated managed-operation surface is a one-to-one view of the supported
runtime exports. Every managed-operation facade function corresponds to exactly
one `[JSExport]` method with generated runtime publication glue, and every
supported export corresponds to exactly one such function. Module
infrastructure such as `initializeEngine` is identified separately and is not
presented as a managed operation. The generator does not invent operations,
combine several exports into one workflow, or expose a managed member that has
no JavaScript export thunk.

The correspondence preserves the declaring-type path, parameter order and
types, synchronous or asynchronous invocation, and raw marshalled result.
TypeScript naming, `Promise<T>` projection, and an authenticated JSON-envelope
parse are defined facade transformations; they do not create another managed
operation.

Inspect-web intentionally presents its boundary as one managed type containing
static `[JSExport]` methods. `ts-bindgen` can retain qualified declaring-type
paths from another supported surface, but it does not project managed classes,
instances, constructors, inheritance, properties, or overload resolution into
a TypeScript object model. A rich C# implementation can sit behind an exported
static method; that implementation remains managed code running in WebAssembly.

`ts-bindgen` is not a C#-to-TypeScript compiler or an IL-to-TypeScript
translator. It reads narrow IL evidence only to establish that generated
runtime publication exists and that a specific JSON wire contract reaches an
exported argument or result. It never translates the exported method body,
dependencies, control flow, or managed object model into TypeScript.

Applications that want a richer TypeScript API author that layer above the
generated facade. Such a layer may group operations, introduce classes,
normalize inputs, compose workflows, or add application policy without
weakening the generated module's one-to-one correspondence with the runtime
exports.

The module imports only the generic .NET runtime JavaScript module at runtime.
It does not import a sibling declaration module because its TypeScript source
already contains the implementation and types. If a consumer emits
declarations, `tsc` derives them from that source.

Generated identifiers must be valid, collision-free TypeScript bindings.
Reserved module bindings, helper names, wrapper-local names, DTO names,
function names, and nested managed-export paths are validated as one composed
module before any output is published.

The current exact `ConfigureHost(string)` browser bootstrap is consumer policy,
not a general implication of `[JSExport]`. `ts-bindgen` emits it as an ordinary
facade function and does not call it implicitly. Any required invocation,
argument, or ordering belongs to the consumer. No exported method name carries
hidden bootstrap semantics.

## Compiler handoff

`ts-bindgen` does not embed, acquire, or configure TypeScript. Its output is one
TypeScript source module. A consumer can include that source directly in a
TypeScript program, compile it to JavaScript, or additionally emit declarations
for a compiled module boundary.

The two consumption forms are distinct:

| Consumption form | Files consumed | Role of `.d.ts` |
| --- | --- | --- |
| TypeScript source | generated `.ts` | none; the source contains implementation and types |
| Compiled module or package | emitted `.js` plus emitted `.d.ts` | describes the JavaScript API when the `.ts` source is not consumed |

The `.d.ts` file is never a second implementation and is not required merely
because the consumer uses TypeScript. It is a declaration-only description of
compiled JavaScript, analogous to a public interface artifact. `ts-bindgen`
does not require consumers to create or distribute one.

The tool's immediate output obligation is valid, deterministic TypeScript whose
runtime import and public facade semantics survive TypeScript compilation.
Output placement, declaration emission, module resolution, bundling,
stale-derived-artifact checks, and publication belong to the consumer and are
not specified here.

## Related tool categories

Other generators answer different questions. Similar output syntax does not
make them the same architectural layer.

### Binary and in-process ABI binding generators

[`wasm-bindgen`](https://rustwasm.github.io/docs/wasm-bindgen/) and
[Emscripten Embind](https://emscripten.org/docs/porting/connecting_cpp_and_javascript/embind.html)
generate essential glue between JavaScript and a low-level Wasm ABI.
[`napi-rs`](https://napi.rs/) similarly packages JavaScript loaders and
declarations around native Node-API or Wasm binaries.

Their JavaScript commonly performs conversions without which the binary API
would not be naturally callable. That responsibility corresponds most closely
to the combination of .NET's generated `[JSExport]` thunks and generic
JavaScript runtime, not to `ts-bindgen`. The `.d.ts` generation those tools may
also provide corresponds to only part of `ts-bindgen`.

### Network client generators

[NSwag](https://github.com/RicoSuter/NSwag),
[OpenAPI Generator](https://openapi-generator.tech/), and
[gRPC-Web](https://github.com/grpc/grpc-web) commonly generate TypeScript
clients that call a service over HTTP or an RPC transport. They own request
construction, transport invocation, response decoding, and public client
types.

That is a fair comparison for the *shape* of `ts-bindgen` output: both produce
an opinionated TypeScript facade with application-level results. It is not the
same transport. `ts-bindgen` invokes an in-browser managed runtime directly;
it does not define a network protocol, HTTP client, proxy, or server endpoint.

### Shape-only DTO generators

[TypeGen](https://typegen.readthedocs.io/) and
[Reinforced.Typings](https://github.com/reinforced/Reinforced.Typings) project
.NET data shapes into TypeScript. They do not necessarily own any invocation
or transport. They are often used beside a web API, but they can describe
shared files, messages, or other values just as well.

These tools are closest to `ts-bindgen`'s DTO projection, but not to its runtime
initialization, export lookup, invocation, or authenticated method-body
evidence.

## Non-goals

`ts-bindgen` does not:

- generate or replace .NET's `[JSExport]` ABI thunks;
- teach `dotnet.js` or `dotnet.runtime.js` new marshalling types;
- generate bindings for arbitrary public C# APIs;
- translate C# or IL implementations into TypeScript;
- synthesize a richer object-oriented or workflow API from exported methods;
- infer wire contracts from names, return-type display text, or nearby
  serializer metadata;
- become a general JavaScript, C#, OpenAPI, or multi-language generator;
- bundle, download, or select a TypeScript compiler;
- generate network clients or define a network protocol;
- bundle the generated runtime module with inspect-web's application assets; or
- provide runtime JSON schema validation.

## Current mismatches

The current implementation predates this decision:

- the command and package are named `tsbindgen`;
- stdout is a generated `.d.ts` declaration surface;
- `--emit-js` generates the runtime facade directly as JavaScript;
- the JavaScript and declaration emitters spell parallel projections that must
  remain synchronized;
- the runtime wrapper is untyped JavaScript; and
- `ConfigureHost(string)` is an implicit name-and-signature convention.

Those are migration inputs, not compatibility requirements. This repository is
the tool's only consumer. The unrelated `tsbindgen` NuGet package establishes
no compatibility obligation for this project.

The replacement should retain the surface-authentication and mapping work while
removing the dual-emitter and direct-JavaScript architecture.

## Migration

The implementation effort should:

1. rename the command, package, project-facing documentation, and generated
   headers to `ts-bindgen`;
2. replace direct `.d.ts` and JavaScript facade emission with one TypeScript
   module emitter;
3. retain explicit raw interop, wire, and public signatures in the generator
   model;
4. emit only that TypeScript module;
5. remove hidden `ConfigureHost` bootstrap semantics;
6. preserve runtime initialization, including `runtime.runMain()` before
   initialized state is published; and
7. preserve deterministic output and failure-before-publication behavior.

## Consumer migration residual

Adopting the generated TypeScript in inspect-web is a separate focused effort
owned by inspect-web. That effort must decide compiler configuration, source
and derived-artifact placement, application module resolution, Vite
externalization, startup policy including `ConfigureHost`, build ordering,
stale-output checks, and publication. This document supplies the TypeScript
module handoff but does not decide those consumer contracts.

## Acceptance

The target remains unverified until all of these gates exist:

- a project-graph and publish-artifact gate proves that inspect-web's runtime
  dependency closure contains neither `ts-bindgen` nor
  `ILInspector.JsExportSurface`;
- a set-equality gate proves that supported `[JSExport]` methods and generated
  managed-operation facade functions have exact one-to-one correspondence,
  excluding separately identified module infrastructure;
- a generator test proves one TypeScript source contains both the runtime
  wrapper implementation and its public TypeScript types;
- a close-negative fixture changes a managed implementation without changing
  its export or wire contract and produces byte-identical TypeScript;
- compiler tests reject mutations to raw managed-export parameter and return
  types;
- compiler tests reject mutations to public wrapper parameter and return
  types;
- close-negative tests keep direct interop values distinct from authenticated
  JSON wire values;
- a compiler test proves the generated TypeScript emits executable JavaScript
  without changing runtime import or public facade semantics;
- runtime tests prove initialization failure, one-runtime reuse,
  `runtime.runMain()` invocation before initialized state is published, exact
  export dispatch, JSON parsing, and exception propagation; and
- a command test proves failed generation does not publish partial TypeScript
  output.

No individual syntax assertion establishes this architecture. The gates must
exercise the generated TypeScript through the real compiler and the emitted
JavaScript through the real runtime seam.
