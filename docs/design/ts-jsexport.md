# `ts-jsexport` TypeScript facade generation

Status: **single-assembly generation is implemented at the generator
boundary**. The repository contains the `ts-jsexport` tool, typed facade
emitter, canonical compiled fixture, and the compiler/runtime gates under
[Acceptance](#acceptance). Metadata-rooted facade contexts are specified here
under [#5462](https://github.com/richlander/dotnet-inspect/issues/5462) but
remain unimplemented. Inspect-web adoption and browser deployment canaries
remain separate work under #5003, #4792, issue #4842, and #4497.

This is the owning document for the `ts-jsexport` TypeScript facade. It defines
how one
[`JsExportSurface`](../../src/ILInspector.JsExportSurface/README.md) becomes one
TypeScript source module and how one metadata-declared facade context selects a
closed set of those independent modules. It does not own .NET JavaScript
interop thunk generation, `JsExportSurface` authentication, TypeScript compiler
behavior, public module specifiers, startup order, or browser hosting.

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

A consumer that needs several facades may declare their managed root types on
one context class. The context is a compiler-checked input inventory, not a
generated aggregate API: each root resolves to a different assembly and still
produces one assembly-specific TypeScript module.

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

This repository contains four similarly named but operationally separate
parts:

1. **`TsJsExport.Contracts` is a producer contract.** It contains the
   repeatable `JsExportRootAttribute` used by a compiled context. It has no
   inspection, generation, runtime, or consumer policy.
2. **`ts-jsexport` is a build-time tool.** It reads a compiled assembly as
   metadata and IL data and generates a TypeScript facade for the type paths and
   static methods represented by its `[JSExport]` surface. In context mode it
   first resolves the complete root set, then generates the same independent
   facade for each resolved assembly.
3. **Inspect-web is a consumer of the tool.** Its managed
   `InspectWeb.Engine.dll` exposes dotnet-inspect functionality through one
   `InspectionEngine` type containing static `[JSExport]` methods.
4. **`ILInspector.JsExportSurface` is part of the tool's implementation.** It
   is a host-side library over Metadata- and Analysis-owned facts. It constructs
   the target-language-neutral export and wire-evidence model consumed by
   `ts-jsexport`.

`ILInspector.JsExportSurface` is not the generated binding, an API that
application TypeScript calls, or a library required by the browser runtime. The
inspected engine assembly does not execute it. The tool uses it while generating
source, before the resulting JavaScript and managed application meet in the
browser.

`TsJsExport.Contracts` is intentionally different. Producers reference its
small, dependency-free attribute contract so the C# compiler can bind every
root type. The assembly contains no product behavior; applications do not call
it and generated JavaScript does not import it. No host-side Metadata, Analysis,
surface, or emitter library crosses through that reference.

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
| `TsJsExport.Contracts` | producer-side context root declaration | export discovery, facade grouping, generation, runtime behavior |
| `ts-jsexport` | deterministic TypeScript facade source and assembly-specific export shape from one `JsExportSurface`; exact context-root resolution into a closed set of independent surfaces; canonical context artifact filenames from assembly simple names | thunk generation, generic runtime declarations, runtime implementation, TypeScript compilation, browser publication, public module specifiers or startup order |
| Consumer | context membership, fresh context output-directory location, TypeScript compiler configuration, public module specifiers, availability of the SDK-owned runtime declaration, derived artifacts, module resolution, composition, and hosting | placing pre-existing files in the context output directory; reinterpreting or weakening the context or `JsExportSurface` inputs |

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
the input surface, owned by `ILInspector.JsExportSurface`. The paired compiled
fixture gate
`Build_ProducesEqualWireFactsAcrossAsyncLoweringsForDirectSerializerResult`
proves that owner issues equivalent authenticated return facts when the
serializer result reaches completion with direct call provenance.
`Build_ProducesEqualWireFactsAcrossAsyncLoweringsForSerializerStoredAcrossSuspension`
proves the same equivalence when Analysis carries the result through one
authenticated compiler state-machine field. The target supports both lowerings
by consuming owner-issued facts, not by reconstructing field flow.
`Build_RejectsConditionalSerializerStoreAcrossAsyncLowerings` proves that a
branch-local serializer overwrite does not hide the raw kickoff-supplied value.

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
bare method name or registration order to select an overload.
`JsExportFunction.RuntimeDispatchKey` projects the authenticated key as the
focused input-contract prerequisite owned by #4791.

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

## Metadata-rooted facade contexts

### Producer declaration

A producer may define an otherwise empty context class with one repeatable root
attribute per intended facade:

```csharp
using TsJsExport;

[JsExportRoot(typeof(BrowserHostExports))]
[JsExportRoot(typeof(BrowserPackageExports))]
[JsExportRoot(typeof(BrowserMetadataExports))]
internal sealed class InspectWebJsExportContext;
```

This follows the established context-root shape used by
[`JsonSerializableAttribute`][json-serializable] and by this repository's
[`MarkoutContextAttribute`][markout-context]: a repeatable class attribute
carries a compiler-bound `System.Type`, and one context gathers the finite root
set. The analogy ends at generation shape. `ts-jsexport` reads already-compiled
metadata rather than participating in the producer's C# compilation, and it
emits one independent facade per root assembly rather than one combined
serializer implementation.

`JsExportRootAttribute` is sealed, non-inherited, valid only on classes,
repeatable, and has one constructor taking `System.Type`. The context does not
derive from a generator base class and need not be partial because no C# source
is added to it. The tool receives the context assembly and exact context type;
several unrelated contexts may therefore coexist in one assembly without
discovery ambiguity.

The attribute is defined by the dependency-free `TsJsExport.Contracts`
producer-contract assembly. Context loading accepts only a constructor whose
declaring type and defining assembly identity match that contract's exact
metadata name, version, culture, and public-key token, when present. A
same-named attribute from another assembly identity is not a root declaration.
The context assembly is a trusted build input; this check catches ordinary
configuration drift and does not distinguish a malicious unsigned replacement
with the same metadata identity.

### Root meaning

Each attribute's `System.Type` is the compiler-bound assembly anchor for one
facade. Its own members have no special selection role. A valid context
satisfies all of these invariants:

1. Every root resolves through metadata to one exact managed assembly and one
   non-generic type definition.
2. Every rooted assembly is distinct, so the context contains exactly one root
   per facade assembly.
3. The rooted assembly exposes at least one supported `[JSExport]` method.

The assembly-wide meaning is what keeps context mode from becoming
generator-side filtering. A root entitles its assembly's complete authenticated
export surface, including exports declared by types other than the anchor. It
does not select a subset from a larger surface or split one assembly into
several modules. A producer that wants another facade first gives that facade
its own managed assembly.

The context's custom-attribute table is not an ordering or naming channel.
`ts-jsexport` canonicalizes roots by exact assembly and type identity before
generation. In context command mode, the exact assembly simple name determines
the canonical `<assembly-name>.ts` artifact filename. Assembly simple names
must be distinct under ordinal, case-insensitive comparison and valid as a
single portable file stem; the tool does not repair or disambiguate an invalid
set. The consumer chooses the output directory and continues to own public
module specifiers, initialization order, entry-point selection, and any
authored coordinator. In particular, the assembly containing the context is
not implicitly the browser host, and attribute order does not make one root
the host.

### Resolution and closed-set failure

Context mode resolves roots from metadata and producer-supplied assembly search
locations without loading or executing the context or rooted assemblies. It
uses the same SRM-only, NativeAOT-friendly inspection path as single-assembly
generation. A serialized `System.Type` name without an assembly qualification
resolves against the context's defining assembly; an assembly-qualified name
resolves against the supplied search locations. The referenced simple name,
version, culture, and public-key token, when present, must equal the resolved
`AssemblyDef`, and the serialized metadata type name must resolve to exactly one
type definition in that assembly. Display names and filenames are not identity.
These checks detect ordinary build and resolution drift; unsigned metadata
identity does not authenticate an assembly against a malicious replacement.

The context is authoritative even when the available file set is incomplete.
An unresolved root, absent assembly, identity mismatch, duplicate assembly,
duplicate type, empty rooted surface, unsupported surface, or ambiguous
resolution fails the whole context before any facade source is returned.
Scanning whatever assemblies happen to be present is not a substitute: it
cannot distinguish an intentionally smaller set from an omitted facade.

Context generation produces a complete in-memory set of
root-identity/assembly-identity/artifact-name/source tuples only after every
root has resolved and every assembly has produced a supported
`JsExportSurface`. It reuses the single-assembly emitter without a
context-specific TypeScript branch.

The context command accepts one context assembly, one exact context type, one
or more assembly search locations, one runtime-module option shared by the
set, and one output directory that must not already exist. It validates and
generates the complete set before creating that directory or writing its first
canonical artifact. It never merges a context set into an existing directory,
deletes stale files, or treats pre-existing contents as part of the current
set. Every successful output directory therefore contains exactly the
canonical artifacts generated from that invocation's context.

The command does not promise a filesystem-wide transaction if its process or
host fails during publication; an interrupted invocation may leave its newly
created output directory incomplete and the non-existing-directory precondition
makes that state visible on retry. Consumers generate into a fresh scratch
path for every attempt and own cleanup plus the final directory or deployment
swap.

`JsExportContextLoaderTests.ContextRootsResolveExactCompiledAssemblySet` will
gate successful cross-assembly resolution from a real compiled context.
`ContextMissingRootAssemblyFailsClosed` will gate the non-vacuous missing-file
case. `ContextRejectsDuplicateAssemblyRoots`,
`ContextIncludesEveryExportAcrossRootedAssembly`,
`ContextRejectsRootAttributeFromWrongContractIdentity`, and
`ContextFailureReturnsNoFacadeSources` will gate the close negatives above.
`TsJsExportCommandTests.ContextModeWritesCanonicalCompleteSet` will gate exact
set materialization and the portable, collision-free artifact-name rule.
`ContextModeRejectsExistingOutputDirectory` will gate the fresh-directory
precondition, including an older successful directory whose context set was
larger.
`TsJsExportContractsTests.RootAttributeHasExactMetadataContract` and
`ContractsProjectHasNoProjectOrPackageReferences` will gate the producer
contract's shape and dependency boundary. The existing NativeAOT publish lane
will include context-mode command execution before this path is considered
NativeAOT-compatible.
These gates are unimplemented until context support lands.

### Single-assembly compatibility

The existing assembly-input command remains the direct form for one facade. A
context containing one root and direct generation of that rooted assembly must
produce byte-identical TypeScript for the same runtime-module option. Context
membership changes orchestration evidence, not facade semantics.

`TsJsExportCommandTests.ContextAndDirectModesProduceIdenticalSingleFacade`
will gate this correspondence and is unimplemented until context support lands.

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
- discover facade membership by scanning available assemblies;
- split one assembly's exports into several facade modules;
- infer module names, output paths, host identity, or startup order from
  context attribute order;
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

## Legacy retirement

Issue #5003 ended the temporary legacy coexistence after inspect-web adopted
the native TypeScript handoff. The `tsbindgen` command, project, package
identity, direct JavaScript emitter, declaration-only command path, and
parallel-output generation script are removed. Shared host-side mapping and
declaration mechanics remain in `ILInspector.TypeScriptGeneration`; the
inspect-web owner documents its source placement, compiler-derived artifacts,
startup policy, build ordering, drift gate, and publication path.

## Migration

The generator implementation performs this migration:

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
8. consume wire facts issued by `ILInspector.JsExportSurface` without
   inspecting lowering or reconstructing state-machine field provenance;
9. consume each exact owner-issued runtime dispatch identity established by
   the completed `ILInspector.JsExportSurface` prerequisite in
   [#4791](https://github.com/richlander/dotnet-inspect/issues/4791);
10. traverse owner-issued declaring-type paths and dispatch keys only through
    own data-property descriptors, failing initialization before publication
    for an inherited, accessor-backed, absent, or non-callable path;
11. remove `ValueTask` mapping branches, reject such a hand-composed input
    visibly, and retain the SDK compile-time negative;
12. retain authenticated synchronous delegate facts, preserve callback
    parameter order and nullability, and reject Promise-returning delegates
    rather than inventing a JavaScript async callback contract. `Action`
    callbacks use `(...args) => undefined`, not `void`, because TypeScript
    otherwise accepts Promise-returning functions; named callbacks must
    likewise declare or infer an `undefined` return;
13. allocate deterministic operation, parameter, enum, and DTO names from
    complete managed identities, route every typed reference through that
    allocation, and preserve parameter order and types instead of rejecting
    legal spelling collisions; and
14. preserve deterministic output and failure-before-publication behavior.

Steps 9 and 13 are atomic for methods sharing one declaring-type path and
managed name. The generator consumes the exact runtime dispatch identity from
issue #4791; allocating two facade names that both call an ambiguous bare
runtime key is never an intermediate state.

## Consumer integration

Inspect-web adopts the generated TypeScript through its separately owned
consumer build and runtime contract. Its owner decides compiler configuration,
source and derived-artifact placement, application module resolution, Vite
externalization, startup policy including `ConfigureHost` and managed
entry-point invocation, build ordering, availability of the SDK-owned
`dotnet.d.ts`, stale-output checks, and publication. This document supplies the
TypeScript module handoff but does not own those consumer contracts.

Issue #4792 records the required real-consumer async canary as independently
reviewable inspect-web work. This design consumes its end-to-end result without
restating or owning the consumer's build graph, runtime selection, canary
operation, or browser-smoke policy.

Issue #4842 separately records the multi-assembly browser canary. It proves
that two generated facade modules attach to one consumer-coordinated runtime
and retain assembly-specific dispatch without turning its fixture assemblies
into a proposed production-layer split; #4497 remains the owner of any such
product decision.

Browser callback-lifetime canaries are likewise consumer evidence rather than
generator ownership. Same-operation callback routing belongs to the managed
operation bridge in
[#5094](https://github.com/richlander/dotnet-inspect/issues/5094), worker-epoch
lifetime belongs to the worker protocol in
[#5093](https://github.com/richlander/dotnet-inspect/issues/5093), and both
depend on inspect-web adopting the generated facade under
[#5003](https://github.com/richlander/dotnet-inspect/issues/5003). A canary
against the current main-thread handcrafted browser contract would prove a
superseded placement rather than the intended architecture.

## Acceptance

The complete generator-and-consumer architecture is accepted through these
gates. Generator-owned gates are implemented by
`TypeScriptFacadeEmitterTests`, `TsJsExportCommandTests`, and
`eng/test-ts-jsexport-typescript.sh`; consumer-owned residuals retain their
issue references below.

- `InspectWebProjectGraphPolicy` and the `Verify browser site artifact` CI step
  prove that inspect-web's runtime dependency closure contains none of
  `ts-jsexport`, `ILInspector.JsExportSurface`, or
  `ILInspector.TypeScriptGeneration`;
- `eng/generate-inspect-web-engine-facade.sh --check` regenerates inspect-web's
  checked-in TypeScript source, compiles its `.js` and `.d.ts` artifacts against
  the SDK-owned `dotnet.d.ts` from the engine's MSBuild-resolved Browser/Wasm
  runtime pack with host-independent LF output, and proves all three files are
  current;
- `verify-engine-facade-runtime.ts` executes the compiler-derived JavaScript
  without a `window` global, proves initialization performs no managed
  operation or entry-point call, and then exercises explicit host
  configuration, synchronous and asynchronous managed operations, and
  `runEntryPoint()`;
- `verify-published-engine-facade.ts` runs the published Browser/Wasm runtime
  without a `window` global and proves the production facade carries a
  synchronous build identity and a genuinely awaited package-version query;
- a set-equality gate proves that supported `[JSExport]` methods and generated
  managed-operation facade functions have exact one-to-one correspondence,
  excluding separately identified `initializeRuntime` and `runEntryPoint`
  infrastructure;
- compiled context fixtures prove that repeatable `JsExportRootAttribute`
  metadata resolves the exact root type and assembly set without loading
  inspected code, rejects omitted or mismatched root assemblies instead of
  treating the available files as complete, rejects two roots in one assembly,
  includes every export across all declaring types in a rooted assembly, and
  returns no generated source when any root fails;
- context command fixtures prove a successful fresh output directory contains
  exactly the canonical artifacts for its context and that an existing
  directory, including one generated from a formerly larger context, is
  rejected rather than merged or cleaned;
- direct generation and a one-root context produce byte-identical TypeScript
  for the same assembly and runtime-module option;
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
- an integration gate gives the command paired compiler-async and
  runtime-async assemblies and proves structurally equal owner-issued surface
  facts generate byte-identical TypeScript; direct and state-machine-field
  serializer-to-completion lowering and authentication remain gated by
  `Build_ProducesEqualWireFactsAcrossAsyncLoweringsForDirectSerializerResult`
  and
  `Build_ProducesEqualWireFactsAcrossAsyncLoweringsForSerializerStoredAcrossSuspension`
  in the prerequisite owner;
- an SDK compile-negative fixture requires method-scoped `SYSLIB1072` to be
  present for `[JSExport]` `ValueTask` and `ValueTask<T>` signatures without
  assuming it is the build's only cascading diagnostic, while a hand-composed
  surface test proves the TypeScript mapper also rejects those unsupported
  inputs visibly;
- compiled synchronous `Action` and `Func` fixtures prove that the owner
  authenticates the exact generated `Action(...)` and `Function(...)`
  descriptors, every nested payload descriptor, managed parameter order,
  supported callback arity, signature hash, and wrapper target before
  publishing delegate facts;
- TypeScript mapping tests prove that only those authenticated facts become
  synchronous function types, preserving callback parameter order,
  nullability, primitive payload types, and return type after every display
  type is correlated with its authenticated assembly and type identity.
  Framework mappings require exact metadata names and generic arity; local
  mappings require retained resolution origin, complete containing-assembly
  identity, exact structured metadata definition name, and declaration kind
  before nullable-reference spelling is accepted. Every delegate fact must
  associate uniquely with an in-range managed parameter. Authenticated
  framework payloads retain their framework meaning during rendering even when
  a local declaration has the same display spelling. Unauthenticated,
  untrusted-framework, mismatched, unclassified-nullable, malformed-arity,
  unassociated, over-arity, `Void`-payload, or async-disguising evidence
  remains a diagnosed `unknown`;
- an SDK compile-negative fixture requires method-scoped `SYSLIB1072` for a
  Promise-returning `Func<..., Task<T>>` callback and a callback with more than
  three parameters without assuming either is the build's only cascading
  diagnostic;
- a compiler test resolves the generated runtime import against the
  SDK-owned `dotnet.d.ts`, with no generator-owned ambient or copied substitute,
  rejects an invalid use of the generic runtime API, and proves the
  assembly-specific `getAssemblyExports()` narrowing. The compiled fixture
  includes synchronous `Action` and `Func` exports; valid inline and named
  `undefined`-returning callbacks compile and execute through the runtime seam,
  while async and `void`-returning `Action` callbacks fail compilation;
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
- an overloaded compiled fixture with distinct results proves each
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
- the separately owned #4497 work may adopt the context contract for the
  production inspect-web facade set; that owner decides the adoption and
  continues to own public module names, coordinator policy, exact deployed
  inventory, and browser evidence; and
- a command test proves failed generation does not publish partial TypeScript
  output.

No individual syntax assertion establishes this architecture. The gates must
exercise the generated TypeScript through the real compiler and the emitted
JavaScript through the real runtime seam.

[json-serializable]: https://learn.microsoft.com/dotnet/api/system.text.json.serialization.jsonserializableattribute
[markout-context]: https://github.com/richlander/markout/blob/main/src/Markout/Attributes/MarkoutContextAttribute.cs
