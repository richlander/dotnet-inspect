# `ts-jsexport` TypeScript facade generation

Status: **proposed**. The current `tsbindgen` implementation emits TypeScript
declarations and can also emit a JavaScript runtime wrapper. This document
defines the replacement architecture; its target properties are **unverified**
until the gates under [Acceptance](#acceptance) exist.

This is the owning document for the `ts-jsexport` TypeScript facade. It defines
how one
[`JsExportSurface`](../../src/ILInspector.JsExportSurface/README.md) becomes one
TypeScript source module. It does not own .NET JavaScript interop thunk
generation, `JsExportSurface` authentication, TypeScript compiler behavior, or
browser hosting.

## Decision

`ts-jsexport` generates TypeScript source from a compiled .NET assembly's
`[JSExport]` surface. The generated module is an opinionated developer facade
over the already-callable API returned by `getAssemblyExports()`.

The tool is general-purpose within that convention. Any .NET project can use it
when its compiled assembly exposes supported static `[JSExport]` methods and,
when richer wire types are wanted, supported System.Text.Json source-generated
contracts. Inspect-web is the first real consumer and repository canary, not a
hard-coded target or the definition of the tool's domain.

The current tool must be built from this repository's source; no `ts-jsexport`
package from this project has been distributed on NuGet. Distribution may
change without changing this architecture. The design neither requires nor
forbids a future .NET tool package.

The consumer's TypeScript compiler turns that source into executable
JavaScript. A consumer may also emit `.d.ts` declarations when it maintains a
compiled module or package boundary. Within one TypeScript source environment,
the generated `.ts` file supplies both implementation and types.

`ts-jsexport` does not generate derived JavaScript or declarations itself:

```text
compiled .NET assembly
        |
        v
ILInspector.JsExportSurface
        |
        | runtime-publishable functions, exact runtime dispatch identities,
        | and authenticated JSON wire contracts
        v
ts-jsexport
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

1. **`ts-jsexport` is a build-time tool.** It reads a compiled assembly as
   metadata and IL data and generates a TypeScript facade for the type paths and
   static methods represented by its `[JSExport]` surface.
2. **Inspect-web is a consumer of the tool.** Its managed
   `InspectWeb.Engine.dll` exposes dotnet-inspect functionality through one
   `InspectionEngine` type containing static `[JSExport]` methods.
3. **`ILInspector.JsExportSurface` is part of the tool's implementation.** It
   is a host-side library over Metadata- and Analysis-owned facts. It constructs
   the target-language-neutral export and wire-evidence model consumed by
   `ts-jsexport`.

`ILInspector.JsExportSurface` is not the generated binding, an API that
application TypeScript calls, or a library required by the browser runtime. The
inspected engine assembly does not execute it. The tool uses it while generating
source, before the resulting JavaScript and managed application meet in the
browser.

The phases compose as follows:

```text
ts-jsexport process on a developer or CI host

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
`ts-jsexport` executable nor its `ILInspector.JsExportSurface` implementation
library crosses into the browser execution phase.

## Why this layer generates TypeScript

The .NET SDK already supplies the unopinionated JavaScript interop layer.
For an attributed method, the SDK-generated assembly contains a method-specific
wrapper, stub, marshalling descriptor, and registration initializer.
`dotnet.js` and `dotnet.runtime.js` provide the generic browser runtime that
turns those registrations into the object returned by
`getAssemblyExports()`.

That raw object is usable without `ts-jsexport`:

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

`ts-jsexport` sits above that boundary. It deliberately chooses TypeScript and
adds application-facing policy:

- TypeScript names and syntax;
- `Task<T>` projection to `Promise<T>`;
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
| .NET JavaScript interop | `[JSExport]` selection, generated managed thunks, marshalling descriptors, registration, generic runtime support, and the SDK-owned `dotnet.d.ts` description of `dotnet.js` | application JSON meaning, assembly-specific export shape, TypeScript facade policy |
| `ILInspector.JsExportSurface` | C#-faithful export facts, exact runtime-publication identity, authenticated JSON wire evidence | TypeScript names, syntax, wrappers, compiler configuration |
| `ts-jsexport` | deterministic TypeScript facade source and assembly-specific export shape from one `JsExportSurface` | thunk generation, generic runtime declarations, runtime implementation, TypeScript compilation, browser publication |
| Consumer | TypeScript compiler configuration, availability of the SDK-owned runtime declaration, derived artifacts, module resolution, and hosting | reinterpreting or weakening the `JsExportSurface` input |

These boundaries are intentionally asymmetric. `ts-jsexport` may reject an input
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
const queryPackageRuntimeKey =
  /* generated owner-issued exact key */ "QueryPackage.123456789";

export async function queryPackage(
  packageId: string,
  version: string,
  targetFramework: string,
): Promise<BrowserPackage> {
  const json = await requireManagedExports()
    .InspectionEngine
    [queryPackageRuntimeKey](packageId, version, targetFramework);
  const parsed: unknown = JSON.parse(json);
  return parsed as BrowserPackage;
}
```

The private generated key is opaque runtime identity from the input surface.
It is not inferred from `QueryPackage`, its public TypeScript spelling, or the
illustrative numeric value.

The raw signature, parsed wire type, and public signature must remain explicit
in the generator model. Display text or a public return annotation must never
be used to reconstruct one of the other views.

## Trust boundaries in generated TypeScript

`getAssemblyExports()` is a runtime boundary whose application-specific shape
is not declared by the generic .NET JavaScript module. The generated module
therefore treats its result as `unknown` and contains one explicit assertion to
the internal managed-export structure generated from the same authenticated
surface. Before publishing initialized state, it walks every exact
declaring-type path with own data-property descriptors and requires the final
runtime dispatch key to be an own data property whose value is callable. It
does not invoke accessors or accept inherited properties while validating.
An absent, inherited, accessor-backed, or non-callable exact path is an
initialization failure, not a later `undefined is not a function` error or
cross-assembly dispatch through a shared prototype.

`JSON.parse()` is another boundary. Its result is immediately treated as
`unknown`; only an authenticated wire contract permits the generated wrapper
to assert a more specific result type. A string return without that evidence
remains a string.

The export-inventory check validates only the exact callable paths required for
dispatch. It does not validate JSON payloads. The TypeScript assertions state
compile-time facts established by producer-owned evidence. Unsupported,
incomplete, or ambiguous evidence fails generation visibly rather than
producing `unknown`, an empty interface, or an untyped success-shaped wrapper.

## Generated module

For one input assembly, `ts-jsexport` emits one self-contained TypeScript module
containing:

1. public enum and DTO declarations for reached wire contracts;
2. one private structural type for the raw `getAssemblyExports()` object;
3. private runtime and narrowed managed-export storage plus accessors;
4. `initializeRuntime()`, which single-flight creates one runtime, captures the
   inspected assembly's exports, publishes both private values only after
   acquisition succeeds, and returns no raw runtime or export object;
5. `runEntryPoint(mainAssemblyName?, args?)`, which forwards to
   `runtime.runMain()` on that same private runtime and returns its
   `Promise<number>`;
6. one exported facade function per supported `[JSExport]` method; and
7. the exact JSON parse operation for each authenticated envelope.

Runtime creation and managed entry-point execution are separate operations.
`initializeRuntime()` never invokes `runMain()` implicitly. The consumer
decides whether and when to call `runEntryPoint()`, whether to await its
completion, and what its exit code means. This permits a consumer to call
configuration exports before starting `Main`, to observe a bounded entry point,
or to retain the promise for a long-running one without blocking facade
publication.

The optional `mainAssemblyName` identifies the runtime's configured entry-point
assembly. It is never inferred from the inspected assembly identity passed to
`getAssemblyExports()`; a generated facade may inspect a class library while
the runtime hosts a different main assembly. Promise fulfillment, rejection,
and nonzero exit codes pass through unchanged.

Initialization has one terminal state machine per generated module instance.
The first `initializeRuntime()` call records the in-flight work before calling
`dotnet.create()`. Concurrent calls join that work, and calls after success are
fulfilled without creating or acquiring again. Any creation, acquisition, or
validation failure is terminal for that module in the current JavaScript realm:
later initialization calls preserve the same rejection, and retry requires a
page reload or worker-realm restart. Runtime and export storage remain
unpublished unless the whole operation succeeds.

That single-flight guarantee is deliberately module-local. A consumer using
several separately generated facade modules configures the SDK's shared
module-scoped `dotnet` builder before invoking any facade initializer, then
serializes their first initialization unless its runtime owner guarantees
shared in-flight acquisition. Generated facades import that same builder but
never change its configuration; after the first serialized initializer
completes, later `dotnet.create()` calls reuse the SDK's completed runtime
instance. A facade whose local acquisition or validation fails never exits or
disposes the potentially shared runtime. Cross-module coordination,
configuration, and runtime lifetime remain consumer and runtime policy.

The focused
[lifecycle model](models/ts-jsexport-lifecycle/README.md) model-checks those
per-facade and shared-runtime interactions for two facades and two callers per
facade. It states its abstraction boundary, fairness assumptions, checked
bounds, safety and progress properties, and counterexample mutations. The model
establishes evidence about the lifecycle design, not the generated
implementation; the runtime and browser gates below remain required.

Managed operations and `runEntryPoint()` fail visibly until initialization has
fulfilled, using one consistent module-owned not-initialized error across all
entry points. After terminal failure they preserve that initialization failure
rather than replacing it with the not-initialized error. Once initialized, each
`runEntryPoint()` call forwards independently to `runtime.runMain()`; the facade
adds no repeat-entry-point cache or legality policy and preserves the runtime's
result or rejection.

### Async lowering compatibility

Supported `Task` and `Task<T>` exports have one facade contract regardless of
how their bodies were lowered. `ts-jsexport` does not inspect async bodies,
classify lowering, or branch on compiler-async versus runtime-async forms. Given
structurally equal owner-issued `JsExportFunction` facts, it produces
byte-identical TypeScript.

Lowering-independent body and JSON-wire authentication is a precondition on
the input surface, owned by `ILInspector.JsExportSurface`. That owner does not
currently issue equivalent authenticated return facts for runtime async;
[#4790](https://github.com/richlander/dotnet-inspect/issues/4790) records the
focused prerequisite. The target supports both lowerings by consuming that
owner-issued invariant after #4790 lands, not by reconstructing its evidence.

Inspect-web's paired deployment canary is a separate consumer responsibility.
[#4792](https://github.com/richlander/dotnet-inspect/issues/4792) owns its
compiler-async and runtime-async build and browser-execution policy. Neither
runtime selection nor deployment policy changes the generated facade contract.

### Correspondence, not managed API translation

The generated managed-operation surface is a one-to-one view of the supported
runtime exports. Every managed-operation facade function corresponds to exactly
one `[JSExport]` method with generated runtime publication glue, and every
supported export corresponds to exactly one such function. Module
infrastructure such as `initializeRuntime` and `runEntryPoint` is identified
separately and is not presented as a managed operation. The generator does not
invent operations, combine several exports into one workflow, or expose a
managed member that has no JavaScript export thunk.

The correspondence preserves the declaring-type path, parameter order and
types, exact owner-issued runtime dispatch identity, synchronous or
asynchronous invocation, and raw marshalled result. TypeScript naming,
`Promise<T>` projection, and an authenticated JSON-envelope parse are defined
facade transformations; they do not create another managed operation.

The runtime dispatch identity is opaque input, distinct from both the managed
method name and the public TypeScript binding. The generated implementation
indexes the narrowed export aggregate with that exact key. It never relies on a
bare method name or registration order to select an overload. The current
surface authenticates but does not project that key; #4791 owns the focused
input-contract prerequisite.

Each supported overload remains a distinct managed operation and receives its
own facade function. This is correspondence over runtime exports, not
TypeScript or managed overload resolution.

Inspect-web intentionally presents its boundary as one managed type containing
static `[JSExport]` methods. `ts-jsexport` can retain qualified declaring-type
paths from another supported surface, but it does not project managed classes,
instances, constructors, inheritance, properties, or overload resolution into
a TypeScript object model. A rich C# implementation can sit behind an exported
static method; that implementation remains managed code running in WebAssembly.

`ts-jsexport` is not a C#-to-TypeScript compiler or an IL-to-TypeScript
translator. Its owner-issued input rests on narrow IL evidence establishing
that generated runtime publication exists and that a specific JSON wire
contract reaches an exported argument or result. The generator itself does not
translate or reinterpret the exported method body, dependencies, control flow,
or managed object model.

Applications that want a richer TypeScript API author that layer above the
generated facade. Such a layer may group operations, introduce classes,
normalize inputs, compose workflows, or add application policy without
weakening the generated module's one-to-one correspondence with the runtime
exports.

The module imports only the generic .NET runtime JavaScript module at runtime.
The same module specifier resolves against the SDK-owned `dotnet.d.ts` during
TypeScript compilation; generated source does not explicitly import a
declaration file. The generated module supplies its own implementation, public
facade types, and assembly-specific narrowing, while the SDK declaration
supplies the generic `dotnet`, `RuntimeAPI`, `runMain()`, and
`getAssemblyExports()` contracts.

If a consumer emits declarations for the facade, `tsc` derives them from the
generated TypeScript source. Those facade declarations are distinct from the
SDK's declaration for the runtime module.

Generated identifiers must be valid, collision-free TypeScript bindings. One
deterministic, scope-aware allocator validates the composed module before any
output is published. At module scope it handles operation-to-operation,
wire-declaration-to-wire-declaration, operation-to-wire-declaration,
operation-to-infrastructure, helper, and reserved-name collisions. Module
infrastructure, runtime imports, and helpers are allocated first and are never
renamed or displaced by a public declaration.

Within each facade function, generated wrapper locals and every module binding
referenced by that function are reserved first and remain immovable. Legal
managed parameter spellings are then retained only when unique and unreserved
in that function scope. A colliding parameter fallback derives from the
complete managed operation identity and parameter ordinal. If distinct
parameter identities still produce the same legal TypeScript spelling, the
same stable canonical-identity digest rule disambiguates them. Parameter order
and types remain unchanged, and genuinely illegal identifier input still fails
generation visibly.

The allocator never rewrites an owner-issued declaring-type path or runtime
dispatch key. TypeScript identifier legality is not evidence that a path is
owned by the acquired assembly-export aggregate; generated runtime traversal
uses the own-data-property checks above rather than ordinary dotted or bracket
property lookup.

On collision, an operation fallback derives from its complete managed operation
identity: fully qualified declaring type, method name, and parameter types. An
enum or DTO declaration fallback derives from its complete owner-issued managed
type identity. If distinct identities still produce the same legal TypeScript
spelling, a stable digest of the corresponding canonical identity
disambiguates them. All signatures and reached-property types refer to enums
and DTOs through typed identity and the allocated-name map, never through a
simple display name. A spelling collision never drops a supported operation,
parameter, enum, or DTO, replaces module infrastructure, or makes the otherwise
supported surface ungeneratable.

The current exact `ConfigureHost(string)` browser bootstrap is consumer policy,
not a general implication of `[JSExport]`. `ts-jsexport` emits it as an ordinary
facade function and does not call it implicitly. Any required invocation,
argument, or ordering belongs to the consumer. No exported method name carries
hidden bootstrap semantics.

## Compiler handoff

`ts-jsexport` does not embed, acquire, or configure TypeScript. Its output is one
TypeScript source module. A consumer can include that source directly in a
TypeScript program, compile it to JavaScript, or additionally emit declarations
for a compiled module boundary.

The generated source has one external declaration dependency: the
`dotnet.d.ts` supplied by the same .NET SDK/runtime pack as the imported
`dotnet.js`. `ts-jsexport` does not parse that declaration or derive facade
operations from it. Instead, it emits TypeScript written against the SDK-owned
contract, just as authored TypeScript would be.

That relationship gives the generated source the official types and
documentation for `dotnet.create()`, `RuntimeAPI`, `runMain()`, and
`getAssemblyExports()`. The TypeScript compiler can reject incorrect runtime
API use, and editors can provide navigation, completion, and explanation when
a person reads the generated file. The generator therefore does not need to
invent a partial local runtime interface that could drift from the selected
.NET SDK.

`dotnet.d.ts` does not supply the richer facade. Its `getAssemblyExports()`
result is necessarily application-agnostic. `ILInspector.JsExportSurface`
supplies the assembly-specific export and wire facts; `ts-jsexport` turns those
facts into the private raw-export structure, public facade types, and one
explicit narrowing at the generic runtime boundary. The generator must not
copy, synthesize, or hand-maintain a substitute declaration for the generic
runtime API.

The declaration has no runtime role. The emitted JavaScript imports
`dotnet.js`; it does not load `dotnet.d.ts`, and deployment does not require the
declaration merely to execute already-compiled JavaScript.

The SDK declaration is also an implementation-private dependency. Generated
public functions must not expose `RuntimeAPI` or another SDK runtime type. A
consumer compiling the generated `.ts` source needs `dotnet.d.ts`; a consumer
receiving only emitted facade JavaScript and its optional declarations does
not. Declaration emission must not leak an import of the SDK runtime
declaration into the facade's public contract.

The two consumption forms are distinct:

| Consumption form | Facade files consumed | Role of a facade `.d.ts` |
| --- | --- | --- |
| TypeScript source | generated `.ts` | none; the source contains implementation and types |
| Compiled module or package | emitted `.js` plus emitted `.d.ts` | describes the JavaScript API when the `.ts` source is not consumed |

The `.d.ts` file is never a second implementation and is not required merely
because the consumer uses TypeScript. It is a declaration-only description of
compiled JavaScript, analogous to a public interface artifact. `ts-jsexport`
does not require consumers to create or distribute one.

The tool's immediate output obligation is valid, deterministic TypeScript whose
runtime import and public facade semantics survive TypeScript compilation.
The consumer must make the matching SDK-owned runtime declaration available to
that compilation. Output placement, facade declaration emission, module
resolution, bundling, stale-derived-artifact checks, and publication belong to
the consumer and are not specified here.

## Related tool categories

Other generators answer different questions. Similar output syntax does not
make them the same architectural layer.

### ABI and component binding generators

[`wasm-bindgen`](https://rustwasm.github.io/docs/wasm-bindgen/) and
[Emscripten Embind](https://emscripten.org/docs/porting/connecting_cpp_and_javascript/embind.html)
generate essential glue between JavaScript and a low-level Wasm ABI.
[`napi-rs`](https://napi.rs/) similarly packages JavaScript loaders and
declarations around native Node-API or Wasm binaries.

[`componentize-dotnet`](https://github.com/bytecodealliance/componentize-dotnet)
orchestrates a different low-level boundary: it turns .NET projects into
WASI 0.2 WebAssembly components. It wraps `wit-bindgen` so WIT contracts
generate corresponding C# imports or exports, then composes NativeAOT-LLVM and
WebAssembly component tooling into the build. The resulting contract is
language-neutral WIT and the component model, not a browser TypeScript module,
`[JSExport]`, or `dotnet.js`.

These tools own low-level ABI or component bindings. The JavaScript-facing
tools commonly perform conversions without which the binary API would not be
naturally callable. That responsibility corresponds most closely to the
combination of .NET's generated `[JSExport]` thunks and generic JavaScript
runtime, not to `ts-jsexport`. Any `.d.ts` generation they provide corresponds
to only part of `ts-jsexport`.

### Compiler-facing CLR binding generators

Tsonic's
[`tsbindgen`](https://tsonic.org/tsbindgen/) reflects broad CLR and framework
surfaces into TypeScript declaration packages and CLR binding metadata. Tsonic
uses those packages when compiling TypeScript that calls .NET APIs into
target-native source. Its generated JavaScript modules are resolution stubs,
not browser implementations of the declared APIs.

That is the general managed-API projection that `ts-jsexport` intentionally
does not perform. `ts-jsexport` starts from the narrower runtime-publishable
`[JSExport]` surface and emits an executable browser facade over
`getAssemblyExports()`. Its distinct name also avoids presenting it as a
variant of Tsonic's established `tsbindgen` product.

### Network client generators

[NSwag](https://github.com/RicoSuter/NSwag),
[OpenAPI Generator](https://openapi-generator.tech/), and
[gRPC-Web](https://github.com/grpc/grpc-web) commonly generate TypeScript
clients that call a service over HTTP or an RPC transport. They own request
construction, transport invocation, response decoding, and public client
types.

That is a fair comparison for the *shape* of `ts-jsexport` output: both produce
an opinionated TypeScript facade with application-level results. It is not the
same transport. `ts-jsexport` invokes an in-browser managed runtime directly;
it does not define a network protocol, HTTP client, proxy, or server endpoint.

### Shape-only DTO generators

[TypeGen](https://typegen.readthedocs.io/) and
[Reinforced.Typings](https://github.com/reinforced/Reinforced.Typings) project
.NET data shapes into TypeScript. They do not necessarily own any invocation
or transport. They are often used beside a web API, but they can describe
shared files, messages, or other values just as well.

These tools are closest to `ts-jsexport`'s DTO projection, but not to its runtime
initialization, export lookup, invocation, or authenticated method-body
evidence.

## Non-goals

`ts-jsexport` does not:

- generate or replace .NET's `[JSExport]` ABI thunks;
- teach `dotnet.js` or `dotnet.runtime.js` new marshalling types;
- depend on inspect-web names, DTOs, or startup policy;
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

- the command, project, and configured package identity are named `tsbindgen`;
- stdout is a generated `.d.ts` declaration surface;
- `--emit-js` generates the runtime facade directly as JavaScript;
- the JavaScript and declaration emitters spell parallel projections that must
  remain synchronized;
- the runtime wrapper is untyped JavaScript;
- `ConfigureHost(string)` is an implicit name-and-signature convention;
- initialization publishes individual export bindings, invokes
  `ConfigureHost`, unconditionally awaits `runtime.runMain()`, and then returns
  the raw export object;
- `TsTypeMapper` contains `ValueTask` and `ValueTask<T>` branches and unit tests
  that remain reachable for hand-composed surfaces, although the SDK's
  JavaScript interop source generator rejects a compiled `[JSExport]`
  `ValueTask` signature with `SYSLIB1072`;
- authenticated async return-wire discovery recognizes compiler-generated
  `MoveNext` result sinks but not a runtime-async export's own physical body;
- `JsExportSurfaceBuilder` authenticates each generated registration's
  signature hash but omits that exact dispatch identity from
  `JsExportFunction`; and
- the current emitter traverses declaring-type paths with ordinary property
  lookup, invokes a bare runtime method name, rejects same-spelling managed
  operations, and rejects distinct enum or DTO identities with the same simple
  name instead of allocating distinct public names.

Those are migration inputs, not compatibility requirements.

The replacement should retain the surface-authentication and mapping work while
removing the dual-emitter and direct-JavaScript architecture.

## Migration

The implementation effort should:

1. rename the command, package, project-facing documentation, and generated
   headers to `ts-jsexport`;
2. replace direct `.d.ts` and JavaScript facade emission with one TypeScript
   module emitter;
3. retain explicit raw interop, wire, and public signatures in the generator
   model;
4. emit only that TypeScript module;
5. remove hidden `ConfigureHost` bootstrap semantics;
6. replace the current initializer with `initializeRuntime()`, which publishes
   one private narrowed export aggregate only after runtime creation and export
   acquisition succeed, uses the terminal single-flight lifecycle above, and
   does not return raw exports;
7. expose `runEntryPoint()` as separately identified module infrastructure over
   the same private runtime, without invoking it from initialization or leaking
   `RuntimeAPI` into the public declaration;
8. consume lowering-independent wire facts after the
   `ILInspector.JsExportSurface` prerequisite in #4790 lands;
9. consume each exact owner-issued runtime dispatch identity after the
   `ILInspector.JsExportSurface` prerequisite in
   [#4791](https://github.com/richlander/dotnet-inspect/issues/4791) lands;
10. traverse owner-issued declaring-type paths and dispatch keys only through
    own data-property descriptors, failing initialization before publication
    for an inherited, accessor-backed, absent, or non-callable path;
11. remove `ValueTask` mapping branches, reject such a hand-composed input
    visibly, and retain the SDK compile-time negative;
12. allocate deterministic operation, parameter, enum, and DTO names from
    complete managed identities, route every typed reference through that
    allocation, and preserve parameter order and types instead of rejecting
    legal spelling collisions; and
13. preserve deterministic output and failure-before-publication behavior.

Steps 9 and 12 are atomic for methods sharing one declaring-type path and
managed name. Until the exact runtime dispatch identity from #4791 is consumed,
such overloads remain a visible generation rejection; allocating two facade
names that both call the ambiguous bare runtime key is never an intermediate
state.

## Consumer migration residual

Adopting the generated TypeScript in inspect-web is a separate focused effort
owned by inspect-web. That effort must decide compiler configuration, source
and derived-artifact placement, application module resolution, Vite
externalization, startup policy including `ConfigureHost` and managed
entry-point invocation, build ordering, availability of the SDK-owned
`dotnet.d.ts`, stale-output checks, and publication. This document supplies the
TypeScript module handoff but does not decide those consumer contracts.

Issue #4792 records the required real-consumer async canary as independently
reviewable inspect-web work. This design consumes its end-to-end result without
restating or owning the consumer's build graph, runtime selection, canary
operation, or browser-smoke policy.

Issue #4842 separately records the multi-assembly browser canary. It proves
that two generated facade modules attach to one consumer-coordinated runtime
and retain assembly-specific dispatch without turning its fixture assemblies
into a proposed production-layer split; #4497 remains the owner of any such
product decision.

## Acceptance

The target remains unverified until all of these gates exist:

- a project-graph and publish-artifact gate proves that inspect-web's runtime
  dependency closure contains neither `ts-jsexport` nor
  `ILInspector.JsExportSurface`;
- a set-equality gate proves that supported `[JSExport]` methods and generated
  managed-operation facade functions have exact one-to-one correspondence,
  excluding separately identified `initializeRuntime` and `runEntryPoint`
  infrastructure;
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
- structurally equal hand-composed owner-issued surfaces produce byte-identical
  TypeScript without any lowering-specific generator branch;
- after #4790, an integration gate gives the command its paired
  compiler-async and runtime-async assemblies and proves structurally equal
  owner-issued surface facts generate byte-identical TypeScript; the physical
  lowering and authentication gates remain with that prerequisite owner;
- an SDK compile-negative fixture requires method-scoped `SYSLIB1072` to be
  present for `[JSExport]` `ValueTask` and `ValueTask<T>` signatures without
  assuming it is the build's only cascading diagnostic, while a hand-composed
  surface test proves the TypeScript mapper also rejects those unsupported
  inputs visibly;
- a compiler test resolves the generated runtime import against the
  SDK-owned `dotnet.d.ts`, with no generator-owned ambient or copied substitute,
  rejects an invalid use of the generic runtime API, and proves the
  assembly-specific `getAssemblyExports()` narrowing;
- a declaration-emission test proves the public facade declaration does not
  expose or import SDK runtime types;
- a compiler test proves the generated TypeScript emits executable JavaScript
  without changing runtime import or public facade semantics;
- collision fixtures cover operation-to-operation, overload,
  DTO-to-DTO, enum-to-enum, enum-to-DTO, operation-to-infrastructure,
  operation-to-wire-declaration, parameter-to-parameter,
  parameter-to-wrapper-local, parameter-to-referenced-module-binding, helper,
  reserved-name, and post-normalization collisions; every supported operation,
  parameter, enum, and DTO retains one deterministic declaration or binding
  without renaming or replacing module infrastructure, parameter order and
  types remain unchanged, and every wire-type reference resolves to the
  allocated declaration for its exact typed identity;
- after #4791, an overloaded compiled fixture with distinct results proves each
  generated facade function indexes the owner-issued exact runtime key rather
  than the ambiguous bare method name;
- runtime export-aggregate fixtures cover both intermediate path segments and
  final dispatch keys: inherited, accessor-backed, absent, and non-callable
  properties fail before publication, call-counting getters are never invoked,
  two assembly roots cannot cross-dispatch through a shared prototype, and an
  equivalent own-data-property path with an own callable key succeeds;
- runtime tests prove initialization failure, publication only after export
  acquisition and exact callable-path validation, no raw-object return, exact
  export dispatch, JSON parsing, and exception propagation;
- initialization runtime tests prove module-local concurrent single-flight
  behavior, exactly one `dotnet.create()` and export acquisition, idempotence
  after success, terminal failure without hidden retry or runtime exit, and
  preservation of the original initialization rejection;
- managed operations and `runEntryPoint()` use the same module-owned
  not-initialized error before initialization succeeds and preserve a terminal
  initialization failure afterward;
- runtime tests prove initialization never invokes `runMain()`, while
  `runEntryPoint(mainAssemblyName?, args?)` uses the same private runtime,
  forwards both arguments on every call, and preserves each returned exit code
  or rejection;
- a runtime test with a call-counting managed-export aggregate, including an
  exact `ConfigureHost(string)` operation, runs without inspect-web host
  globals and proves initialization invokes zero managed operations; only an
  explicit facade call invokes `ConfigureHost` with the caller's argument;
- the separately owned #4792 gate demonstrates the same facade contract across
  its chosen paired lowerings without adding consumer policy to this generator;
  and
- a command test proves failed generation does not publish partial TypeScript
  output.

No individual syntax assertion establishes this architecture. The gates must
exercise the generated TypeScript through the real compiler and the emitted
JavaScript through the real runtime seam.
