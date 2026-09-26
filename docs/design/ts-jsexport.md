# `ts-jsexport` TypeScript facade generation

Status: **single-assembly generation is implemented at the generator
boundary**. The repository contains the `ts-jsexport` tool, typed facade
emitter, canonical compiled fixture, and the compiler/runtime gates under
[Acceptance](#acceptance). Metadata-rooted facade contexts are implemented
under [#5466](https://github.com/richlander/dotnet-inspect/issues/5466).
Explicit producer-declared JSON input bindings are implemented under
[#8327](https://github.com/richlander/dotnet-inspect/issues/8327). Complete
member-level inference and symmetric input/output declarations are specified
under [#8370](https://github.com/richlander/dotnet-inspect/issues/8370).
Inspect-web adoption and browser deployment canaries remain separate work
under #5003, #4792, issue #4842, and #4497.

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

A producer whose raw `[JSExport]` ABI carries JSON as `string` may declare
input and output associations on the export type. Separate repeatable input
and output attributes support methods with zero, one, or several JSON inputs.
The attributes identify compiler-bound JSON roots but do not author their
TypeScript shapes.

Certification is complete per exported member. A member either derives all of
its non-intrinsic JSON associations from authenticated implementation flow or
declares all of them. Partially inferred and partially declared members remain
generatable for diagnosis but are uncertified; production generation treats
that warning as an error. Canonical facade drift checking remains the gate for
a refactor that removes all evidence and would otherwise resemble an
intentional raw-string API.

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
   repeatable `JsExportRootAttribute` used by a compiled context and the
   repeatable `JsExportJsonInputAttribute` and
   `JsExportJsonOutputAttribute` used by an export type. It has no inspection,
   generation, runtime, or consumer policy.
2. **`ts-jsexport` is a build-time tool.** It reads a compiled assembly as
   metadata and IL data and generates a TypeScript facade for the type paths and
   static methods represented by its `[JSExport]` surface. In context mode it
   first resolves the complete root set, then generates the same independent
   facade for each resolved assembly.
3. **Inspect-web is a consumer of the tool.** Its managed
   `DotnetInspect.Web.dll` exposes dotnet-inspect functionality through one
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
declared root and wire type. The assembly contains no product behavior;
applications do not call it and generated JavaScript does not import it. No
host-side Metadata, Analysis, surface, or emitter library crosses through that
reference.

The phases compose as follows:

```text
ts-jsexport process on a developer or CI host

DotnetInspect.Web.dll --compiled context--> seven rooted export assemblies
                                                    |
                                                    v
                                          JsExportSurface models
                                                    |
                                                    v
                                           TypeScript emitter
                                                    |
                                                    v
                                      seven inspect-web facades


browser execution

seven facade modules --> dotnet.js / dotnet.runtime.js
                                      |
                                      v
                         seven managed export assemblies
                                      |
                                      v
                         dotnet-inspect product libraries
```

The compiler step between the diagrams derives one JavaScript module from each
generated TypeScript facade. Neither the `ts-jsexport` executable nor its
`ILInspector.JsExportSurface` implementation library crosses into the browser
execution phase.

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
  await runtime.getAssemblyExports("DotnetInspect.Web");
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
- initialization and explicit one-runtime composition; and
- consumer-facing DTO, enum, and JSON union declarations.

Generating JavaScript plus JSDoc would express those TypeScript decisions
indirectly and require the generator to own comment containment, JSDoc import
syntax, typedef allocation, and JavaScript-to-declaration synchronization.
Generating native TypeScript expresses the selected language directly and lets
the consumer's compiler own JavaScript and optional declaration emission.

## Ownership

| Owner | Owns | Does not own |
| --- | --- | --- |
| .NET JavaScript interop | `[JSExport]` selection, generated managed thunks, marshalling descriptors, registration, generic runtime support, and the SDK-owned `dotnet.d.ts` description of `dotnet.js` | application JSON meaning, assembly-specific export shape, TypeScript facade policy |
| `ILInspector.JsExportSurface` | C#-faithful export facts, exact runtime-publication identity, authenticated JSON wire evidence, and validation of producer-declared JSON input associations | TypeScript names, syntax, wrappers, compiler configuration |
| `TsJsExport.Contracts` | producer-side context root and JSON input declarations | export discovery, declaration validation, facade grouping, generation, runtime behavior |
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

The same distinction applies to inputs. A managed `string candidatesJson`
parameter remains `string` in the private raw export signature even when the
method body deserializes it. Raw marshalling does not by itself establish the
JSON value type or which string parameter carries it.

### Wire view

When `ILInspector.JsExportSurface` authenticates the method body's serializer
flow and exact source-generated `JsonTypeInfo<T>`, the returned string has a
known JSON wire shape. That evidence may establish `BrowserPackage` as the
parsed result type.

For deserialization, owner-issued parameter bindings additionally associate an
authenticated JSON root with one exact declared parameter position. Only those
bindings establish typed facade inputs. A producer declaration is the preferred
source of that association. During migration, Analysis may continue to issue a
binding from one exact deserializer flow when no declaration exists.
Unpositioned roots, conflicted roots, and neighboring string parameters never
acquire attribution from a name, type, or relative position.

Flow-inferred bindings remain subject to the existing exact-provenance rule.
Compiler-hoisted parameters retain an inferred association only when Analysis
proves one exact reachable store from the declared string parameter to the
loaded field; an alias, additional store, unresolved store, or transformed
value keeps an undeclared public parameter raw. An explicit producer
declaration does not require the managed parser to remain in the export body or
its async state machine.

Wire DTOs are producer-owned snapshots. Their properties are readonly, arrays
use `ReadonlyArray<T>`, and string-keyed dictionaries use
`Readonly<Record<string, T>>`. Direct JS-interop arrays remain mutable because
they are runtime values, not serialized snapshots.

### Semantic JSON strings

An exact platform `System.DateTimeOffset` reached through an authenticated
System.Text.Json wire contract becomes an opaque TypeScript string:

```ts
declare const dateTimeOffsetStringBrand: unique symbol;

export type DateTimeOffsetString = string & {
  readonly [dateTimeOffsetStringBrand]: "DateTimeOffsetString";
};
```

System.Text.Json owns the runtime representation: its default
`DateTimeOffset` converter writes the ISO 8601-1:2019 extended profile and
preserves an offset. The generated type preserves that semantic distinction
without converting the value to JavaScript `Date`, which would discard the
original offset and expose a different mutability and validity model. This is
stricter than the common OpenAPI `string` plus `date-time` format lowering:
the value remains assignable to `string`, but an untreated `string` is not
assignable to `DateTimeOffsetString`.

Consumers that validate the serialized text accept the default writer's
optional one-to-seven fractional second digits rather than assuming the fixed
seven digits produced by an explicit round-trip format string. Validation also
rejects impossible Gregorian dates, out-of-range time or offset components, and
values whose offset would place the UTC instant outside the `DateTimeOffset`
range. The same mapping composes through supported union alternatives,
including nullable and collection-contained case trees.

The mapping requires the exact platform type identity carried by the
authenticated source-generated JSON shape. A producer-defined type with the
same display name does not acquire the mapping. A member-level custom converter
remains unsupported under the existing converter rule rather than inheriting
the platform contract.

The facade emits no unchecked constructor, parser, or decoder. A deserialize
caller supplies a value previously received through an authenticated boundary
or validated by its own runtime owner. Direct JS-interop signatures remain
unchanged; this mapping applies only to JSON wire views.

For a serialize-only record, owner-issued `Conditional` member presence becomes
an exact optional property:

```ts
readonly property?: T;
```

Optionality describes possible key absence independently of nullability. When
System.Text.Json omits the member's null or default value, the generated
present-value type removes only that member's outer `null`; nested nullability
and `null` supplied by another authenticated wire contract remain intact. A
`Present` nullable member therefore remains `property: T | null`, while a
nullable `WhenWritingNull` member becomes `property?: T`. `Never` remains
present. The emitter consumes `JsExportSurface` presence facts rather than
reading serializer attributes or inferring absence from C# nullability.
A conditional `JsonElement` member uses the recursive `JsonValue`
present-value alias, which includes JSON `null` but excludes JavaScript
`undefined`; it is therefore `property?: JsonValue`, not
`property?: unknown`. Other arbitrary JSON outputs retain their existing
opaque `unknown` contract.

A conditional member whose present-value type is an immediate record generic
parameter is unsupported. The open generic declaration does not retain enough
owner-issued information to distinguish a CLR default null from JSON null in
every authenticated closed instantiation, and `property?: T` becomes unsound
when a supported argument maps to `unknown`. Generation fails visibly rather
than publishing that declaration.

A conditional member whose union alias can collapse to `unknown` is likewise
unsupported. This includes a top-level `JsonElement` case supplied directly or
through a closed generic union argument, and an open record generic parameter
flowing through a union case. Nested `JsonElement` values inside an array,
collection, dictionary, or record do not collapse the member's present-value
type and therefore do not require the `JsonValue` helper. Finite nesting of the
same generic union definition remains a closed substitution path and is
analyzed through every supplied argument; cycle suppression applies only to
recursive union-case traversal.

### Direction-specific wire declarations

Issue [#7279](https://github.com/richlander/dotnet-inspect/issues/7279)
projects one managed type into distinct input and output declarations when its
authenticated deserialization and serialization wire shapes differ. OpenAPI
`readOnly`/`writeOnly` properties and TypeSpec visibility provide analogous
direction-scoped schema projection. They are evidence for the distinction, not
the contract owner: `ts-jsexport` continues to consume only direction,
presence, and type evidence issued by `JsExportSurface`.

The declaration identity is the managed `ApiType` plus one declaration
direction:

- `Both` when a bidirectional type's input and output projections are
  equivalent;
- `Deserialize` for a split input declaration; and
- `Serialize` for a split output declaration.

A type reached in only one direction keeps one declaration with that direction
and its existing unsuffixed preferred name. A bidirectional type also keeps one
`Both` declaration when its projections are equivalent. A split type instead
receives the preferred names `TypeNameInput` and `TypeNameOutput`. The
direction is part of the canonical allocation identity, so these preferred
names participate in the existing deterministic module-wide collision
allocation alongside managed types, operations, parameters, and
infrastructure. No unsuffixed compatibility alias is emitted for a split
declaration: it would recreate the ambiguous contract this feature removes.

Equivalence is computed over the complete projected declaration, not only the
type's direct attributes. The declaration planner starts with bidirectional
records whose effective member participation, property requiredness, or
present-value type differs by direction. It then repeatedly propagates the
split through any bidirectional record or union whose projected member or case
type selects different directional declarations. Traversal follows nullable,
array, collection, dictionary, generic-instance, nested-record, and union case
shapes. The fixed point handles recursion and mutually recursive strongly
connected components without guessing an order. Generic parameters retain one
parameter identity; supplied type arguments select their directional
declarations at each reference site.

Declaration emission, member type mapping, union case mapping, and operation
signature mapping consume this one declaration plan. They do not independently
decide whether a type is split. Within an output declaration every resolved
local reference selects the serialize projection; within an input declaration
every resolved local reference selects the deserialize projection. Public JSON
input parameters select deserialize declarations. Parsed JSON returns,
including `JsonText<T>` realization, select serialize declarations. Raw
`[JSExport]` signatures remain unchanged.

Directional projection does not broaden an unsupported serializer contract.
In particular, polymorphic deserialization remains unsupported until its
owning contract establishes reader-side case selection. A declaration whose
active direction has unsupported constructor binding, converter evidence,
member presence, or type mapping remains visibly unsupported in that
direction. The supported opposite declaration does not authenticate it.

The canonical Inspect Web facade remains the production drift gate. The
compiled fixture facade additionally demonstrates the generated wrapper at
runtime: a direction-sensitive DTO is accepted through its input declaration,
serialized to the managed call, returned JSON is parsed through its output
declaration, and the directional member difference is observable. Authored
TypeScript consumers adopt changed generated names rather than retaining
hand-written shared or compatibility shapes.

### Translating unions and nullability

C# and TypeScript can both describe alternative and nullable values, but they
do not use the same type-system representation. On the C# side, this design
receives union alternatives from the generated `union` declaration model.
Nullability is separate: nullable-reference annotations such as `string?` are
compiler metadata over the same CLR reference type, while `int?` is
`System.Nullable<int>`. On the TypeScript side, a native union represents both
case alternatives and nullability; with strict null checking, nullable `T` is
spelled `T | null`.

The generator therefore translates meaning rather than preserving source
syntax:

- `string?` becomes `string | null`.
- `int?` becomes `number | null`.
- `GenericNested<string?>` becomes `GenericNested<string | null>`.
- `GenericNested<string?>?` becomes
  `GenericNested<string | null> | null`.
- Union cases `TValue` and `int`, plus a default-null case, become
  `TValue | number | null`.

These rows remain distinct before translation. Union case evidence determines
the alternatives; nullable metadata and authenticated serializer evidence
determine where `null` is possible. Their TypeScript forms then compose and
normalize as unions. In particular, `property?: T` is not another spelling of
`property: T | null`: the former permits an absent property and introduces
`undefined`, while the latter describes a present JSON property whose value
may be `null`.

This document uses *lowering* for the broader conversion into the public
TypeScript facade, but union and nullable projection does not materially lower
the abstraction level. It is closer to type-level transpilation: one
source-language wire-type vocabulary is translated into another at roughly the
same semantic altitude. The representation changes because TypeScript uses
union syntax for both concepts, while the distinctions established by C# and
System.Text.Json evidence must remain observable in the translated type.

### JSON union lowering

Slice 3 of [#5892](https://github.com/richlander/dotnet-inspect/issues/5892),
tracked by [#6106](https://github.com/richlander/dotnet-inspect/issues/6106),
consumes the union evidence owned by
[`JsExportSurface`](../../src/ILInspector.JsExportSurface/README.md#json-union-alternatives).
It does not infer another serializer contract from the union's `Value` member.

A serialize-reached supported union becomes a TypeScript type alias containing
its mapped case alternatives and the producer's default-null alternative.
Case types retain their existing JSON mappings, including readonly snapshots,
byte-array Base64 strings, and local enum/DTO declarations. Aliases and every
reference participate in the same identity-based module-name allocation as
other wire declarations. The raw export remains string-valued; the public
facade parses that JSON and returns the generated alias.
Case signature trees supply identities rather than nested nullable-reference
annotations. Reference-valued entries in case arrays and dictionaries therefore
remain nullable conservatively; null must not disappear from supported JSON.

Generic unions with direct type-parameter cases use generic aliases whose
arguments are JSON wire types. Closed uses retain their structured argument
identities. A parameter embedded inside a case signature remains unsupported:
substituting a wire type into a CLR container is not generally faithful
(`T[]` writes an array for `T = int`, but a Base64 string for `T = byte`).
Generic JSON records with direct, recursively parametric members use generic
TypeScript interfaces. Closed constructions are discovered only from
authenticated source-generated JSON roots, and their arguments are substituted
through supported records, arrays, dictionaries, nullable values, and unions.
A direct serializer root does not retain nullable-reference annotations for
its generic arguments, so reference-shaped arguments remain conservatively
nullable. Union case signatures have the same nested-annotation erasure and
apply the same rule to generic-record alternatives. Ordinary record member
signatures retain their nested nullable annotations and project those precise
generic arguments instead, including when a supported generic union wraps a
generic record directly, through a nullable value wrapper, or through supported
array and dictionary containers.
Metadata nullability traversal follows the compiler transform encoding:
`System.Nullable<T>` and non-generic value types contribute no independent
transform slots, so following reference annotations remain aligned. Generic
value types and generic parameters retain their compiler-issued placeholder
slots.

Recursive composition remains parametric only when substituting a wire type
preserves the surrounding wire shape. `GenericNested<T>[]` is an array of
records for every supported `T`; direct `T[]` is not parametric because
`T = byte` closes to `byte[]`, whose JSON form is a Base64 string rather than
an array. A generic parameter directly wrapped in an array therefore fails
visibly before publication, including when that array is nested in another
supported container. Nullable-reference annotation on an unconstrained
parameter does not change that CLR array shape, while a value-constrained
`T?[]` remains a genuine array of `System.Nullable<T>` and stays supported.
Open, other embedded non-parametric, or unauthenticated constructions also fail
visibly; the boundary does not infer arbitrary CLR generic shapes. Parameter
arrays are identified from decoded generic-parameter positions rather than
display names, so a qualified concrete array remains distinct even when its
type name matches a parameter name.

Deserialize-reached unions, unavailable case/null evidence, unsupported
converters, unmapped alternatives, and recursive union-case alias components
fail visibly before publication. Recursive DTO interfaces are not union-case
alias components and retain their existing behavior.
Unused union registrations remain inert. No discriminator, replacement
transport, or runtime schema validator is introduced.

The shared `InertText.InertString` field converter is one deliberate converter
exception. Its exact metadata identity and converter evidence authenticate a
JSON string wire value, while the generated TypeScript contract preserves its
provenance as an opaque string brand:

```ts
declare const inertStringBrand: unique symbol;

export type InertString = string & {
  readonly [inertStringBrand]: "InertString";
};
```

The JSON payload and JavaScript runtime value remain strings. The generated
facade grants the brand only where the authenticated C# wire contract names the
exact `InertText.InertString` type; an unrelated type with the same simple name
remains an ordinary generated type. The brand has no public constructor,
decoder, or unchecked helper. Its module-private `unique symbol` key follows
the conventional TypeScript nominal-typing pattern used by Inspect Web's other
opaque identities; the generator allocates that private binding against the
whole module just like public declarations. The brand carries neither policy,
forms, concerns, nor truncation state, and it does not mean HTML-, attribute-,
DOM-, or URL-safe. Consumers retain their sink-specific escaping.

The generator and browser receive only the already-encoded representation.
They do not import or expose `InertText.Encoding`; recovering original text
remains a separate CLI concern under the InertText audit boundary.
Authenticated serialize-only polymorphic `System.Text.Json` base records lower
to discriminated TypeScript unions. Their case interfaces consume the same
owner-issued member-presence evidence as ordinary records, including exact
optional present-value mapping; unsupported or deserialize-reached
polymorphism fails visibly.

`JsonUnionWireTests` and the compiler/runtime consumer harness
`eng/test-ts-jsexport-typescript.sh` gate the generated contract against actual
source-generated serializer results and compiled TypeScript consumers,
including an annotation-erased null reference root, a generic-record union
alternative with null content, a precise nullable generic argument in an
ordinary record member both directly and through generic-union collection
arguments, a nullable generic record struct, and mixed nullable value/reference
generic arguments, rejected direct and container-nested `T[]` and
unconstrained `T?[]` constructions whose `byte[]` payloads are Base64 text, a
supported value-constrained `T?[]` neighboring case, and a concrete array whose
name collides with a generic parameter.
The four-step adoption path remains Metadata evidence, JsExportSurface
evidence, this CLI generation/harness slice, and inspect-web browser/Wasm
adoption. The existing TypeScript emitter owns this format lowering; no new
human-readable rendering path or architecture retirement is needed.

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
in the generator model. For an authenticated JSON input, the model likewise
retains the raw managed parameter type, the public wire parameter type, and the
required serialization step as separate facts. Display text, TypeScript
spelling, or an unauthenticated annotation must never be used to reconstruct
one of the other views. A validated producer declaration may establish the
association between an already authenticated wire type and an exact raw
parameter; it does not replace either fact.

For example, a raw managed operation with
`string candidatesJson` and an authenticated
`BrowserDependencyCoordinateCandidate[]` binding becomes:

```ts
export function matchPackageDependencyCoordinate(
  packageId: string,
  declaredRange: string | null,
  candidatesJson: ReadonlyArray<BrowserDependencyCoordinateCandidate>,
): BrowserDependencyCoordinateMatch {
  const json = serializeJsonInput(
    candidatesJson,
    "PackageExports.MatchPackageDependencyCoordinate.123456789",
    "candidatesJson",
  );
  const result = requireManagedExports()
    .PackageExports
    ["MatchPackageDependencyCoordinate.123456789"](
      packageId,
      declaredRange,
      json,
    );
  const parsed: unknown = JSON.parse(result);
  return parsed as BrowserDependencyCoordinateMatch;
}
```

The generated helper calls `JSON.stringify()` exactly once per bound parameter
and requires a string result before dispatch. A thrown serialization error
propagates unchanged; an `undefined` result throws a `TypeError`. The wrapper
never substitutes fallback JSON or dispatches after either failure. Multiple
bound parameters serialize independently in declared order.

## Complete JSON contract certification

### Motivation and scope

Both JSON directions can be certified from implementation flow when the
serializer operation remains positionable at the export boundary. An input
flows from one raw string parameter into an authenticated
`Deserialize<T>` call. An output flows from one authenticated `Serialize<T>`
result to the raw string return. Either flow can become intentionally hidden
inside an operation coordinator, lambda, or helper. Requiring those operations
to remain visibly in the export body couples the public TypeScript contract to
an incidental implementation shape.

A raw export may also have several genuine string parameters beside one or
more JSON parameters. The C# signature remains authoritative for intrinsic
interop types; declarations cover only JSON associations that the signature
cannot express.
Library API Diff in
[#8272](https://github.com/richlander/dotnet-inspect/pull/8272) and Compare
Clone in [#8310](https://github.com/richlander/dotnet-inspect/pull/8310)
both had to expose existing deserialization in the export solely to establish
that association. The Method Body Comparison implementation at
[`c49f4ff`][method-body-operation] retains the motivating nested-parser shape,
while its [published production gate][method-body-production] and
[published-runtime benchmark][method-body-benchmark] still author and
serialize the request from TypeScript.

Issue [#8327](https://github.com/richlander/dotnet-inspect/issues/8327)
records the input-declaration expansion.
Issue [#8370](https://github.com/richlander/dotnet-inspect/issues/8370)
records the complete-member certification expansion. It adds symmetric output
declarations and certification diagnostics, but no JSON schema validator,
runtime parser, new JS interop marshalling type, or TypeScript-authored wire
shape.

### Declaration

The export type declares one association with a repeatable class attribute:

```csharp
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using DotnetInspect.Web.Interop.Source.Operations;
using TsJsExport;

namespace DotnetInspect.Web.Interop.Source;

[JsExportJsonInput(
    nameof(SourceExports.QueryMethodBodyComparison),
    "requestJson",
    typeof(BrowserMethodBodyComparisonRequest))]
[JsExportJsonOutput(
    nameof(SourceExports.QueryMethodBodyComparison),
    typeof(BrowserMethodBodyComparisonResult))]
public static partial class SourceExports
{
    [JSExport]
    public static async Task<string> QueryMethodBodyComparison(
        string operationId,
        string requestJson)
    {
        BrowserMethodBodyComparisonResult result =
            await MethodBodyComparisonOperations.RunMethodBodyComparison(
                operationId,
                requestJson);
        return JsonSerializer.Serialize(
            result,
            BrowserSourceJsonContext.Default.BrowserMethodBodyComparisonResult);
    }
}
```

The export remains a thin interop boundary. Request parsing and operation
coordination belong to the functionality namespace:

```csharp
using System.Text.Json;

namespace DotnetInspect.Web.Interop.Source.Operations;

internal static class MethodBodyComparisonOperations
{
    internal static Task<BrowserMethodBodyComparisonResult>
        RunMethodBodyComparison(
            string operationId,
            string requestJson)
    {
        BrowserMethodBodyComparisonRequest request =
            JsonSerializer.Deserialize(
                requestJson,
                BrowserSourceJsonContext.Default
                    .BrowserMethodBodyComparisonRequest)
            ?? throw new ArgumentException(
                "A method-body comparison request is required.");

        return MethodBodyComparisonWorkflow.RunAsync(
            operationId,
            request);
    }
}
```

`RunMethodBodyComparison` returns the typed result to
`SourceExports.QueryMethodBodyComparison`; the export then serializes and
returns it. Keeping the outgoing serializer call at the export boundary
preserves existing result-flow authentication, while the input declaration
allows deserialization to remain in the functionality layer.

The producer contract uses two sealed, non-inherited, repeatable class
attributes rather than one aggregate attribute. Separate attributes avoid
parallel parameter-name and type arrays, naturally support several JSON
inputs, and allow an output-only export to declare no input association.

`JsExportJsonInputAttribute` has one constructor taking, in order:

1. the managed method name;
2. the raw parameter name; and
3. the compiler-bound `System.Type` of the incoming JSON wire root.

Its readable properties are `MethodName`, `ParameterName`, and `WireType`.
The method argument should use `nameof` so a managed rename is compiler-bound.
C# has no equivalent parameter-symbol expression at a class declaration, so
the parameter name remains a string that generation resolves and validates.
This is enforced metadata rather than an advisory hint: if `"requestJson"` is
stale, misspelled, or does not identify exactly one parameter on the named
export, `ts-jsexport` reports a generation error and publishes no replacement
facade source. A consumer that runs generation or canonical-facade drift
checking in its build, as inspect-web does, turns that error into a build
break. Generation never ignores the declaration or falls back to a raw string
facade.

`JsExportJsonOutputAttribute` has one constructor taking, in order:

1. the managed method name; and
2. the compiler-bound `System.Type` of the outgoing JSON wire root; and
3. an optional `deferParsing` boolean, `false` by default.

Its readable properties are `MethodName`, `WireType`, and `DeferParsing`. At
most one output declaration may resolve to a given exported member.

The declaration belongs on the export type, including an otherwise empty
partial declaration in a dedicated contract file. It does not belong on the
raw parameter, the serializer context, or the request DTO. The export type owns
the association between one operation and one parameter; the serializer
context owns JSON construction, and the DTO remains reusable data rather than
transport-routing metadata.

The attribute is defined by the dependency-free `TsJsExport.Contracts`
assembly. Loading accepts only its exact metadata name and defining assembly
identity under the same identity rule as `JsExportRootAttribute`. A same-named
attribute from another assembly is not a declaration.

### Resolution and authentication

`ILInspector.JsExportSurface` resolves each input declaration before issuing a
parameter binding:

1. The attribute-bearing type resolves to the exact declaring type represented
   by the surface.
2. `MethodName` and `ParameterName` identify exactly one supported
   `[JSExport]` method and one declared parameter on that method. An overloaded
   set is valid only when the named parameter makes one candidate unique.
3. The raw parameter's exact interop type is `System.String`.
4. `WireType` resolves to one exact metadata type identity.
5. The export assembly's authenticated source-generated serializer inventory
   contains exactly one deserialization-capable `JsonTypeInfo<WireType>`.
6. The resulting JSON shape is supported for the deserialize direction.

Zero or multiple matching methods, parameters, wire types, or serializer
contracts fail the whole surface. The first contract has no serializer-context
override: ambiguity is a producer ownership problem rather than a reason to
make every declaration name infrastructure. A later extension may add an
explicit context selector only if real producers demonstrate that one export
assembly must intentionally register the same wire root in several contexts.

Output declaration resolution follows the same authentication rules, except
that it identifies one exact exported method, requires its raw return to be
`System.String` or `Task<System.String>`, and requires exactly one
serialization-capable source-generated contract for the declared root.

### Deferred complete JSON output

An output declaration with `deferParsing: true` remains a complete declared
JSON association, but the generated facade preserves its runtime result as an
encoded string rather than calling `JSON.parse`. The public TypeScript return
uses the collision-safe generic brand:

```ts
declare const jsonTextBrand: unique symbol;

export type JsonText<T> = string & {
  readonly [jsonTextBrand]: T;
};
```

For an asynchronous export, `Task<string>` therefore becomes
`Promise<JsonText<T>>`. The `Promise` represents asynchronous completion; the
`JsonText<T>` value represents one complete encoded JSON document after that
completion. A synchronous `string` export becomes `JsonText<T>`.

The brand authenticates only the producer-declared association with `T`. It
does not claim that a JavaScript consumer has parsed, structurally validated,
or admitted the text under a feature-specific size or complexity policy. The
generated module exposes no public constructor, unchecked branding helper,
parser, or decoder. A consumer that requires bounded admission supplies its
own decoder and obtains `T` only after that decoder succeeds.

`JsonText<T>` is not a streaming, chunking, pagination, or progressive-
realization contract. It always represents one complete JSON document.
`ReadableStream`, `AsyncIterable`, chunk identity, ordering, backpressure,
cancellation, and partial-failure semantics require a separately owned design.

Ordinary output declarations retain the existing parsed lowering. Deferred
and parsed declarations use the same authenticated source-generated shape,
complete-member certification, conflict checking, TypeScript declaration
inventory, and canonical drift protection.

The declaration establishes only the parameter association. The source-
generated serializer contract remains the authority for members, names,
nullability, constructor binding, converters, unions, collections, and
direction-sensitive presence. The raw `[JSExport]` metadata remains the
authority for parameter position and runtime type. The generated declaration
contains no handwritten TypeScript shape.

### Relationship to flow evidence

Certification has three member-level results:

1. **Inferred** — no JSON declaration resolves to the member, and every
   observed JSON association is authenticated and positioned.
2. **Declared** — at least one JSON declaration resolves to the member, every
   observed JSON association has an equal declaration, and no association is
   supplied only by inference.
3. **Incomplete** — observed evidence is unpositionable, or a member combines
   a declaration on one JSON association with inference on another.

A member with no JSON association is intrinsically certified by its raw C#
signature and does not require an attribute.

When a declared input or output also has reachable authenticated flow evidence,
an equal wire type corroborates the declaration and a different wire type
fails the surface. A duplicate declaration for the same method and slot fails
even when its wire type is equal. Distinct declared parameters may use the same
wire type.

An unpositioned or transformed deserializer call does not erase an independently
validated declaration. It remains visible in the unpositioned wire-root
inventory and cannot create an inferred binding. A declaration therefore
allows parsing to stay inside a coordinator, lambda, or helper without asking
Analysis to prove transitive implementation flow.

Incomplete members retain every valid inferred and declared binding so a local
developer can inspect the best available facade, but generation emits a
certification warning. Production invokes `ts-jsexport` with
`--warnings-as-errors`, which rejects the whole generation before publication.
A conflict is a generation error, never a warning, precedence rule, or silent
fallback to raw string.

If a refactor removes every serializer fact, the current assembly alone cannot
distinguish lost evidence from an intentionally raw string contract. Consumers
therefore retain canonical generated-facade comparison. The warning gate catches
partial loss; canonical drift catches total disappearance.

### Generated effect

A declaration changes only the public facade view. For:

```csharp
[JsExportJsonInput(
    nameof(FooExports.QueryFoo),
    "requestJson",
    typeof(FooRequest))]
[JsExportJsonOutput(
    nameof(FooExports.QueryFoo),
    typeof(Foo))]
public static partial class FooExports
{
}
```

with raw export:

```csharp
[JSExport]
public static Task<string> QueryFoo(string requestJson)
```

and authenticated return serialization of `Foo`, the private runtime
signature remains:

```ts
(requestJson: string) => Promise<string>
```

while the public facade is:

```ts
export function queryFoo(requestJson: FooRequest): Promise<Foo>
```

`FooRequest` and `Foo` are ordinary JSON wire types distinguished by direction:
TypeScript authors and the generated wrapper serialize the request; managed
code authors and the generated wrapper produce the result, which the wrapper
parses. A type may
participate in either or both directions. `Request` and `Result` suffixes have
no generator meaning.

The existing serialization helper, dispatch ordering, and failure semantics
remain unchanged. The declaration does not cause managed deserialization,
validate JavaScript values against a runtime schema, or alter the raw
JavaScript/.NET ABI.

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

`JSON.stringify()` is the corresponding authenticated input boundary. The
public parameter accepts the mapped wire value, while the private export still
requires its raw string envelope. Only an exact owner-issued parameter binding
permits the wrapper to serialize that value. Serialization exceptions and
non-string results fail visibly before managed dispatch.

The export-inventory check validates only the exact callable paths required for
dispatch. It does not validate JSON payloads. The TypeScript assertions state
compile-time facts established by producer-owned evidence. Unsupported,
incomplete, or ambiguous evidence fails generation visibly rather than
producing `unknown`, an empty interface, or an untyped success-shaped wrapper.

## Generated module

For one input assembly, `ts-jsexport` emits one self-contained TypeScript module
containing:

1. public enum and DTO declarations for reached wire contracts;
2. one public `JsExportRuntime` structural handle limited to
   `getAssemblyExports()` and `runMain()`;
3. one private structural type for the raw `getAssemblyExports()` object;
4. private runtime and narrowed managed-export storage plus accessors;
5. `createRuntime()`, which invokes the configured SDK builder and returns the
   narrow handle rather than the SDK's full `RuntimeAPI`;
6. `initializeRuntime(runtime?)`, which single-flight acquires the inspected
   assembly's exports through a supplied handle or, when omitted, through one
   locally created handle, and publishes both private values only after
   acquisition succeeds;
7. `runEntryPoint(mainAssemblyName?, args?)`, which forwards to
   `runtime.runMain()` on that same private runtime and returns its
   `Promise<number>`;
8. one exported facade function per supported `[JSExport]` method; and
9. the exact JSON parse operation for each authenticated envelope.

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
The first `initializeRuntime()` call records the in-flight work before it
awaits either the supplied handle or `createRuntime()`. Concurrent calls join
that work, and calls after success are fulfilled without creating or acquiring
again. Any creation, acquisition, or validation failure is terminal for that
module in the current JavaScript realm: later initialization calls preserve
the same rejection, and retry requires a page reload or worker-realm restart.
Runtime and export storage remain unpublished unless the whole operation
succeeds.

That single-flight guarantee is deliberately module-local. A consumer using
several separately generated facade modules chooses one runtime owner, calls
that module's `createRuntime()` exactly once, and passes the same returned
promise or completed `JsExportRuntime` handle to every module's
`initializeRuntime(runtime)` call. The consumer serializes those first
initializations unless its runtime owner deliberately permits concurrent
attachment. This contract does not rely on repeated `dotnet.create()` calls
being idempotent: the Mono SDK memoizes a completed runtime while the CoreCLR
SDK rejects a second creation. Generated facades import the configured builder
but never change it. A facade whose local acquisition or validation fails never
exits or disposes the shared runtime. Cross-module coordination, configuration,
and runtime lifetime remain consumer and runtime policy.

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

The correspondence preserves the declaring-type path, parameter order, raw
parameter types, exact owner-issued runtime dispatch identity, synchronous or
asynchronous invocation, and raw marshalled result. TypeScript naming,
`Promise<T>` projection, an authenticated JSON-envelope parse, and
authenticated parameter serialization are defined facade transformations;
they do not create another managed operation.

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

### TypeScript lexical spelling policy

`ts-jsexport` owns the lexical spelling of generated TypeScript identifiers.
Preferred spelling and identifier allocation are separate stages: first derive
the category-specific spelling below, then make that spelling legal, unique,
and non-reserved through the deterministic allocator.

| Identifier category | Preferred spelling |
| --- | --- |
| Generated type declaration | Preserve the owner-issued managed simple declaration name, removing only a generic arity suffix such as `` `2 ``. |
| Operation or parameter binding | Apply `System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName` exactly; for example, `URLValue` becomes `urlValue`. |
| JSON object key | Preserve authenticated serializer evidence. `[JsonPropertyName]` evidence takes precedence; otherwise apply the authenticated serializer naming policy. |
| Enum wire string | Preserve authenticated enum-member evidence. `JsonStringEnumMemberName` takes precedence; otherwise retain the owner-issued member spelling. |
| Generator-owned infrastructure | Use a fixed TypeScript-native spelling owned by the generator, such as `InertString`, `inertStringBrand`, `initializeRuntime`, or `runEntryPoint`. |
| Declaring-type path or runtime dispatch key | Preserve the opaque owner-issued runtime identity without respelling. |

Type declaration names and value bindings therefore follow conventional
TypeScript category casing when the managed producer follows conventional .NET
naming: PascalCase types remain PascalCase, while operations and parameters
become camelCase. The generator does not split words, reinterpret vocabulary,
or perform acronym-aware respelling beyond the exact named serializer
transform. A type named `BrowserExactLibraryApiAssemblyIdentity`, for example,
retains that spelling rather than being translated to another vocabulary.

JSON object keys and enum wire strings are wire data, not facade-style
bindings. A legal property name may be emitted directly and another supported
name may be quoted, but neither is recased merely to match surrounding
TypeScript. Runtime declaring-type paths and dispatch keys likewise remain
owner-issued identities rather than presentation text.

Generated identifiers must be valid, collision-free TypeScript bindings. One
deterministic, scope-aware allocator validates the composed module before any
output is published. At module scope it handles operation-to-operation,
wire-declaration-to-wire-declaration, operation-to-wire-declaration,
operation-to-infrastructure, helper, and reserved-name collisions. Module
infrastructure, runtime imports, and helpers are allocated first and are never
renamed or displaced by a public declaration.

Within each facade function, generated wrapper locals and every module binding
referenced by that function are reserved first and remain immovable. A
preferred parameter spelling is retained when it is legal, unique, and
unreserved in that function scope. A colliding parameter fallback derives from
the complete managed operation identity and parameter ordinal. If distinct
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

This implementation consumes the contract through an in-repository project
reference. It does not make the contract a separately distributed package;
external producer acquisition and versioning require coordinated package and
release scope.

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
must be distinct after Unicode Form C normalization under ordinal,
case-insensitive comparison and valid as a single portable file stem.
Normalization is comparison-only; the exact assembly simple name remains the
artifact spelling. Including the `.ts` suffix, an artifact name may use at most
255 UTF-16 code units and 255 UTF-8 bytes. The tool does not repair or
disambiguate an invalid set. The consumer chooses the output directory and
continues to own public module specifiers, initialization order, entry-point
selection, and any authored coordinator. In particular, the assembly
containing the context is not implicitly the browser host, and attribute order
does not make one root the host.

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
The context assembly's directory is not an implicit search location; producers
name every candidate file or directory explicitly.

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

`JsExportContextLoaderTests.ContextRootsResolveExactCompiledAssemblySet` gates
successful cross-assembly resolution from a real compiled context.
`ContextMissingRootAssemblyFailsClosed` gates the non-vacuous missing-file
case. `ContextRejectsDuplicateAssemblyRoots`,
`ContextIncludesEveryExportAcrossRootedAssembly`,
`ContextRejectsRootAttributeFromWrongContractIdentity`, and
`ContextFailureReturnsNoFacadeSources` gate the close negatives above.
`TsJsExportCommandTests.ContextModeWritesCanonicalCompleteSet` gates exact
set materialization. `JsExportContextLoaderTests` gates non-portable artifact
name rejection and ordinal case-insensitive collision detection.
`ContextModeRejectsExistingOutputDirectory` gates the fresh-directory
precondition, including an older successful directory whose context set was
larger.
`TsJsExportContractsTests.RootAttributeHasExactMetadataContract` and
`ContractsProjectHasNoProjectOrPackageReferences` gate the producer
contract's shape and dependency boundary. The existing NativeAOT publish lane
includes context-mode command execution.

### Single-assembly compatibility

The existing assembly-input command remains the direct form for one facade. A
context containing one root and direct generation of that rooted assembly must
produce byte-identical TypeScript for the same runtime-module option. Context
membership changes orchestration evidence, not facade semantics.

`TsJsExportCommandTests.ContextAndDirectModesProduceIdenticalSingleFacade`
gates this correspondence.

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
- infer wire types from method names, parameter names, return-type display
  text, or nearby serializer metadata; producer-declared names resolve exact
  export metadata but never supply the wire shape;
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

### Certification refinement

Issue [#8327](https://github.com/richlander/dotnet-inspect/issues/8327)
introduced producer-declared JSON inputs and Method Body Comparison as the
first production adopter. Its original migration plan would eventually have
declared every input and removed flow association.

Issue [#8370](https://github.com/richlander/dotnet-inspect/issues/8370)
supersedes that universal-declaration endpoint with the lower-ceremony hybrid
model in this document. Existing exports remain inferred when their complete
JSON boundary is certifiable. Declarations are required only when
implementation evidence is incomplete, and then cover the complete member
rather than one isolated side. The implementation stages are:

1. add authenticated output declarations and complete-member certification;
2. enable `--warnings-as-errors` in the canonical Inspect Web generation path;
3. convert real incomplete producers to complete declarations; and
4. retain canonical facade drift checking for total evidence disappearance.

Observable Browser/Wasm adoption includes Method Body Comparison and Source
Comparison. The latter demonstrates a request parser nested in operation
coordination while the generated public facade accepts
`BrowserSourceComparisonRequest` directly. The CLI has no JavaScript-export or
generated-TypeScript boundary and is not a consumer of this capability. This
is a build-time contract specifically for JavaScript-export producers.

An inferred member may become declared, and a declared member may become
inferred, when its generated TypeScript is unchanged. No raw `[JSExport]`
signature or runtime dispatch identity changes in either transition.

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

The first explicit-input consumer is Method Body Comparison. Its existing
Source owner continues to own operation coordination, cancellation, request
validation, comparison semantics, and result projection. This design owns only
the declaration-to-facade association that lets authored TypeScript pass
`BrowserMethodBodyComparisonRequest` while the managed ABI remains string
valued.

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

The complete certification expansion adds these contract-defining gates:

- `TsJsExportContractsTests` pins
  `JsExportJsonInputAttribute` as a sealed, repeatable, non-inherited class
  attribute and `JsExportJsonOutputAttribute` as its symmetric output
  declaration, with the exact constructors and readable-property contracts
  above, and keeps `TsJsExport.Contracts` dependency-free;
- compiled fixtures prove a declaration on an otherwise empty partial export
  type resolves one exact method, raw string parameter, wire root, and
  deserialization-capable source-generated contract without requiring the
  deserializer call in the export body;
- close-negative fixtures reject the wrong contract assembly identity,
  absent or ambiguous methods and stale, absent, or ambiguous parameter-name
  literals, a non-string raw parameter, unresolved or ambiguous serializer
  ownership, unsupported deserialize shapes, duplicate declarations, and
  declared/observed wire-type conflicts before any facade source is published;
- paired fixtures prove equal completely declared and completely inferred
  members issue structurally equal wire facts and byte-identical TypeScript;
- mixed fixtures prove distinct parameters may share one wire type, an
  unpositioned deserializer cannot erase a valid declaration, and a member
  combining declared and inference-only JSON associations is diagnosed as
  incomplete;
- output fixtures prove declarations for synchronous and asynchronous returns,
  an export with no JSON input side, malformed and duplicate declarations,
  serializer-direction rejection, and declared/observed conflicts;
- command and context gates prove certification warnings remain visible by
  default and `--warnings-as-errors` publishes no direct or context output;
- compiler and runtime fixtures prove a declaration changes only the public
  parameter type and generated serialization step while the private managed
  signature, dispatch key, JSON text, result inference, and failure behavior
  remain unchanged; and
- the Method Body Comparison production gate and published-runtime benchmark
  use complete input/output declarations, pass
  `BrowserMethodBodyComparisonRequest` directly through the generated Source
  facade while its managed parser remains nested under its existing operation
  coordination, and retain the same public result type.

- `InspectWebProjectGraphPolicy` and the `Verify browser site artifact` CI step
  prove that inspect-web's runtime dependency closure contains none of
  `ts-jsexport`, `ILInspector.JsExportSurface`, or
  `ILInspector.TypeScriptGeneration`;
- `eng/generate-inspect-web-engine-facade.sh --check` executes inspect-web's
  compiled context once, requires the exact seven rooted artifacts and consumer
  mappings, compiles all seven `.js` and `.d.ts` outputs against the SDK-owned
  `dotnet.d.ts` from the engine's MSBuild-resolved Browser/Wasm runtime pack
  with host-independent LF output, proves the seven checked-in TypeScript
  sources are current, and type-checks authored consumers against the fourteen
  exact transient outputs;
- the deployment and promotion verifiers pin the exact rooted-assembly set (the
  seven names above) and structural invariants such as exactly one SDK
  `create()` call, one runtime, and zero entry-point invocations, but assert
  only `js_export_method_count > 0` rather than an exact total: the total
  drifts with ordinary per-method feature work across seven independently
  owned export classes, while the byte-for-byte source, declaration, and
  published-JavaScript comparisons already in the same verifiers catch any
  change to the exported surface. An exact-count assertion here would
  duplicate that coverage while adding a value contributors must remember to
  bump in lockstep across every workflow copy — see
  [#6051](https://github.com/richlander/dotnet-inspect/issues/6051);
- `verify-engine-facade-runtime.ts` executes the compiler-derived JavaScript
  without a `window` global, proves initialization performs no managed
  operation or entry-point call, and then exercises explicit host
  configuration, synchronous and asynchronous managed operations, and
  `runEntryPoint()`;
- `verify-published-engine-facades.ts` runs the published Browser/Wasm runtime
  without a `window` global and proves all seven production facades initialize
  over one runtime, dispatch through their own assemblies, invoke the host
  entry point once, and carry a synchronous build identity plus a genuinely
  awaited host canary;
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
- compiled parameter-binding fixtures prove that only exact owner-issued JSON
  input declarations or transitional exact flow associations replace a public
  raw string with its readonly wire type; non-first and multiple independent
  bindings preserve declared order, while conflicted, unpositioned, duplicate,
  and out-of-range associations fail or remain raw without guessed
  attribution;
- compiler and runtime tests prove each typed JSON input is serialized inside
  the generated wrapper, the private managed-export signature remains string
  valued, managed dispatch receives the exact JSON text, independent inputs
  serialize in order, and thrown or `undefined` serialization results fail
  before dispatch without fallback JSON;
- close-negative tests keep direct interop values distinct from authenticated
  JSON wire values;
- exact `InertText.InertString` wire members emit an opaque string brand, the
  TypeScript compiler rejects an untreated string at that boundary, and a
  same-named application type does not acquire the brand;
- the inert-text fixture remains a scalar JSON string at runtime and the
  generated module exposes no decoder or unchecked branding helper;
- exact platform `System.DateTimeOffset` wire values emit the collision-safe
  `DateTimeOffsetString` brand, the TypeScript compiler rejects an untreated
  string, a same-named application type does not acquire the brand, and the
  compiled fixture preserves the System.Text.Json timestamp text at runtime;
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
- lexical-spelling tests pin owner-issued type declaration names with generic
  arity removed, the exact `System.Text.Json` camel-case transform for
  operation and parameter bindings including acronym runs, serializer-owned
  JSON property and enum-member wire spelling, and fixed generator-owned
  infrastructure names;
- collision fixtures cover operation-to-operation, overload,
  DTO-to-DTO, enum-to-enum, enum-to-DTO, operation-to-infrastructure,
  operation-to-wire-declaration, parameter-to-parameter,
  parameter-to-wrapper-local, parameter-to-referenced-module-binding, helper,
  reserved-name, and post-normalization collisions; every supported operation,
  parameter, enum, and DTO retains one deterministic declaration or binding
  without renaming or replacing module infrastructure, parameter order and
  types remain unchanged, and every wire-type reference resolves to the
  allocated declaration for its exact typed identity;
- compiled conditional-presence fixtures emit exact optional serialize-side
  properties under `exactOptionalPropertyTypes`, remove only member-level outer
  `null` from present values, preserve nested and unconditionally present
  nullability, retain `Never` as required, and declare `JsonValue` for
  member-level, context-default, and polymorphic-only conditional JSON members;
- direction-specific declaration fixtures prove direct, nested, recursive,
  generic, and union split propagation; equivalent bidirectional shapes retain
  one declaration; deterministic allocation resolves collisions with
  `Input`/`Output` preferred names; operation inputs and parsed returns select
  their respective declarations; and a compiled runtime wrapper demonstrates
  serialization and parsing across the asymmetric contract;
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
[method-body-operation]: https://github.com/richlander/dotnet-inspect/blob/c49f4ff193da1b90fe795e8c3ca4318440f373f8/inspect-web/DotnetInspect.Web.Interop.Source/BrowserMethodBodyOperation.cs
[method-body-production]: https://github.com/richlander/dotnet-inspect/blob/c49f4ff193da1b90fe795e8c3ca4318440f373f8/inspect-web/browser/method-body-production.spec.ts
[method-body-benchmark]: https://github.com/richlander/dotnet-inspect/blob/c49f4ff193da1b90fe795e8c3ca4318440f373f8/inspect-web/browser/benchmark-published-runtime.ts
