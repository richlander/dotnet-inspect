# `ts-bindgen` TypeScript facade generation

Status: **proposed**. The current `tsbindgen` implementation emits TypeScript
declarations and can also emit a JavaScript runtime wrapper. This document
defines the replacement architecture; its target properties are **unverified**
until the gates under [Acceptance](#acceptance) exist.

This is the owning document for the `ts-bindgen` TypeScript facade. It defines
how one authenticated
[`JsExportSurface`](../../src/ILInspector.JsExportSurface/README.md) becomes one
TypeScript source module. It does not own .NET JavaScript interop thunk
generation, `JsExportSurface` authentication, TypeScript compiler behavior, or
browser hosting.

## Decision

`ts-bindgen` generates TypeScript source from a compiled .NET assembly's
authenticated `[JSExport]` surface. The generated module is an opinionated
developer facade over the already-callable API returned by
`getAssemblyExports()`.

The consumer's pinned TypeScript compiler turns that source into:

- the JavaScript module deployed beside the .NET WebAssembly runtime; and
- the `.d.ts` declarations consumed by TypeScript application code.

`ts-bindgen` does not generate those derived artifacts itself:

```text
compiled .NET assembly
        |
        v
ILInspector.JsExportSurface
        |
        | authenticated functions and JSON wire contracts
        v
ts-bindgen
        |
        | TypeScript source
        v
consumer-owned tsc
        |
        +-- deployable JavaScript
        `-- public .d.ts declarations
```

## Name

The command, tool package, and user-facing product name are `ts-bindgen`.
The hyphen follows the established `*-bindgen` naming convention and
distinguishes this tool from the unrelated `tsbindgen` package on NuGet.

`ts-bindgen` names the tool's target-language personality, not every constraint
on its input. Its one-line contract supplies the necessary scope:

> Generate a TypeScript facade from an authenticated .NET `[JSExport]`
> surface.

`ts-jsexport-bindgen` would encode more of the current input but would still be
incomplete: the tool also authenticates and projects System.Text.Json wire
contracts that are not present in `[JSExport]` signatures. Encoding both input
mechanisms would make the name longer without making the contract complete.
Names such as `jsexport-bindgen` or `dotnet-js-bindgen` would instead suggest
ownership of the low-level JavaScript ABI glue that remains with .NET.

The shorter name is therefore deliberate. Documentation and diagnostics must
consistently say that the input is a compiled .NET assembly with a supported,
authenticated `[JSExport]` surface; the name alone must not be used to imply
arbitrary C#-to-TypeScript generation.

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
the consumer's pinned compiler own JavaScript and declaration emission.

## Ownership

| Owner | Owns | Does not own |
| --- | --- | --- |
| .NET JavaScript interop | `[JSExport]` selection, generated managed thunks, marshalling descriptors, registration, generic runtime support | application JSON meaning, TypeScript facade policy |
| `ILInspector.JsExportSurface` | C#-faithful export facts, runtime-publication evidence, authenticated JSON wire evidence | TypeScript names, syntax, wrappers, compiler configuration |
| `ts-bindgen` | deterministic TypeScript facade source from one `JsExportSurface` | thunk generation, runtime implementation, TypeScript compilation, browser publication |
| Consumer build | pinned TypeScript version and options, emitted JavaScript and `.d.ts` placement, stale-output checks | reinterpreting or weakening the authenticated input surface |
| Browser host | serving the emitted module beside `_framework`, runtime asset layout, application startup | binding discovery or type projection |

These boundaries are intentionally asymmetric. `ts-bindgen` may reject an
authenticated surface it cannot faithfully represent in TypeScript. It must
not broaden acceptance by reimplementing or weakening the evidence rules owned
by `ILInspector.JsExportSurface`.

The consumer compiler owns derived artifacts, but it does not own facade
semantics. Changing compiler flags must not silently change the module shape
that `ts-bindgen` promises. The consumer therefore pins the compiler and checks
both generated-source drift and derived-artifact drift.

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
4. `initializeEngine`, which creates one runtime and captures its exports;
5. one exported facade function per supported `[JSExport]` method; and
6. the exact JSON parse operation for each authenticated envelope.

The module imports only the generic .NET runtime JavaScript module at runtime.
It does not import its own emitted declarations: `tsc` derives those
declarations from the same TypeScript source.

Generated identifiers must be valid, collision-free TypeScript bindings.
Reserved module bindings, helper names, wrapper-local names, DTO names,
function names, and nested managed-export paths are validated as one composed
module before any output is published.

The current exact `ConfigureHost(string)` browser bootstrap is consumer policy,
not a general implication of `[JSExport]`. `ts-bindgen` emits it as an ordinary
facade function and does not call it implicitly. An inspect-web-owned entry
module calls that function with `window.location.origin` after initialization.
No exported method name carries hidden bootstrap semantics.

## Consumer compilation and publication

`ts-bindgen` does not embed or acquire TypeScript. Each consumer selects and
pins the compiler that defines its emitted JavaScript contract.

For inspect-web, the generated module remains separate from the Vite bundle:

```text
generated inspect-web-engine.ts
        |
        | dedicated emitting tsconfig
        v
engine/wwwroot/inspect-web-engine.js
src/inspect-web-engine.d.ts
```

The emitted JavaScript stays beside `_framework/dotnet.js`, preserving the
runtime-relative import. Vite continues to treat
`/inspect-web-engine.js` as an external browser module. Application TypeScript
resolves that public module identity to the emitted declaration file.

The generation workflow must define an acyclic order for:

1. producing the managed assembly inspected by `ts-bindgen`;
2. generating TypeScript from that exact assembly;
3. compiling the generated TypeScript;
4. building the frontend bundle; and
5. publishing the final .NET WebAssembly site.

Previously committed outputs may not silently satisfy an earlier stage.
The implementation must provide explicit generation and check modes and fail
visibly when an authoritative or derived artifact is stale.

Whether derived JavaScript and declarations are committed is an inspect-web
repository policy, not a `ts-bindgen` contract. If committed, CI must reproduce
them from the generated TypeScript and exact pinned compiler. If produced only
during the build, every build and publish entry point must run the generating
stages.

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
- the runtime wrapper expresses its types through JSDoc; and
- `ConfigureHost(string)` is an implicit name-and-signature convention.

Those are migration inputs, not compatibility requirements. This repository is
the tool's only consumer. The unrelated `tsbindgen` NuGet package establishes
no compatibility obligation for this project.

The replacement should retain the authenticated surface and mapping work while
removing the dual-emitter and checked-JavaScript architecture.

## Migration

The implementation effort should:

1. rename the command, package, project-facing documentation, and generated
   headers to `ts-bindgen`;
2. replace direct `.d.ts` and JavaScript facade emission with one TypeScript
   module emitter;
3. retain explicit raw interop, wire, and public signatures in the generator
   model;
4. make inspect-web compile that module with its pinned TypeScript compiler;
5. derive the deployed JavaScript and public `.d.ts` from the same source;
6. move the `ConfigureHost` invocation into inspect-web-owned startup;
7. preserve runtime behavior and generated-output drift gates; and
8. delete the obsolete JSDoc-only configuration and tests.

The implementation should begin after the authored inspect-web TypeScript
conversion tracked by
[#4574](https://github.com/richlander/dotnet-inspect/issues/4574), because both
efforts change the frontend compiler and toolchain boundary.

PR [#4774](https://github.com/richlander/dotnet-inspect/pull/4774) is a proven
checked-JavaScript fallback, not the target architecture. It should remain
unmerged while the TypeScript replacement is developed, then be closed as
superseded after the replacement proves the same behavior.

## Acceptance

The target remains unverified until all of these gates exist:

- a generator test proves one TypeScript source produces both the runtime
  wrapper behavior and public declarations;
- compiler tests reject mutations to raw managed-export parameter and return
  types;
- compiler tests reject mutations to public wrapper parameter and return
  types;
- close-negative tests keep direct interop values distinct from authenticated
  JSON wire values;
- generated-output checks reproduce JavaScript and `.d.ts` from the exact
  TypeScript source and pinned compiler;
- runtime tests prove initialization failure, one-runtime reuse, exact export
  dispatch, JSON parsing, and exception propagation; and
- a publication test proves stale or failed generation cannot leave
  success-shaped current artifacts.

No individual syntax assertion establishes this architecture. The gates must
exercise the generated TypeScript through the real compiler and the emitted
JavaScript through the real runtime seam.
