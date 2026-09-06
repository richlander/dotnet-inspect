# ILInspector.JsExportSurface

`ILInspector.JsExportSurface` is a C#-faithful object model of an assembly's
`[JSExport]` wasm/JS interop surface, projected from `ILInspector.Metadata`'s
`ApiSurface`/`ApiSurfaceExtractor`.

It is a host-side inspection and binding-generation library. `ts-jsexport`
references it while reading a compiled assembly as metadata and IL data; the
inspected assembly does not execute or reference this project, and browser
applications do not need it in their runtime bundle.

`JsExportSurfaceBuilder.Build(surface, bodyIndex)` discovers:

- **Functions** — every runtime-publishable, ordinary static `[JSExport]`
  method, with its declaring type, parameters, and return type reported
  unmodified (a `Task<string>` is reported as `Task<string>`, never unwrapped
  to a target-language concept such as `Promise<T>`). Attributed operators,
  constructors, generic methods, bodyless `abstract`/`extern` methods, and
  other non-method member kinds are rejected: they do not receive runtime
  JSExport glue. Authentic rows on filtered MethodDefs, including lambdas in
  compiler-generated types, remain surface-scoped failure evidence rather than
  disappearing with the filtered API declaration. Live extraction also
  retains exact-name `__Wrapper_*_<digits>` MethodDef tokens backed by
  target-matched `DynamicDependency` rows on one SDK-generated registration
  MethodDef. A registration for another declaring type, or a handwritten
  registration elsewhere, cannot be borrowed. Analysis then authenticates the
  exact registration token as a body containing the retained number of trusted
  `BindManagedFunction` calls, exactly as many trusted calls whose proven first
  string-literal argument equals the export's structured runtime binding name
  as managed exports sharing that name, non-empty equal metadata/body module
  MVIDs, an exact
  `System.Runtime.InteropServices.JavaScript` `JSMarshalerArgument`, plus a
  **reachable** wrapper-to-stub-to-export MethodDef call chain. The
  registration's second argument must be an `int32` literal equal to the
  decimal suffix of the wrapper's own `__Wrapper_<Name>_<digits>` name, and its
  signed decimal value is retained with the method name as the exact
  `RuntimeDispatchKey` published by `getAssemblyExports()`. Overloads sharing a
  managed name are matched by both binding name and authenticated signature
  hash, so each receives its own runtime key without relying on registration
  order. The number of same-name runtime bindings must equal the number of
  managed exports in the overload group. This retains the stricter
  one-binding-name requirement for a non-overloaded export and prevents an
  unmatched registration from taking over the runtime's bare-name
  compatibility alias. The
  `ReadOnlySpan<JSMarshalerType>` descriptor argument must resolve to one
  element per export return and parameter, each built by a
  `JSMarshalerType` factory compatible with that managed type. Trusted
  `System.Action` and `System.Func` parameters require the generated
  `Action(...)` or `Function(...)` factory respectively, with every nested
  parameter and synchronous return descriptor authenticated in managed order.
  The resulting target-language-neutral `JsExportDelegateParameter` facts are
  correlated to the containing method parameter by index. A diagnosed
  registration, wrapper, or stub body, prefix sibling, or handwritten
  candidate cannot publish another export.
  An attributed body in a non-partial type is rejected because it has no
  runtime publication glue.
- **Records** — the transitive closure of record shapes reachable from the
  assembly's `JsonSerializerContext`-derived type's `[JsonSerializable(typeof(T))]`
  roots, since `[JSExport]` method signatures alone don't reveal the DTO shapes
  serialized inside their bodies. Traversal follows serialized properties and
  `[JsonInclude]` fields. Each record carries the context's JSON naming policy;
  a record reached through contexts with conflicting policies is marked
  unsupported rather than inheriting whichever context metadata happens to
  appear first.

This library intentionally stays free of any target-language opinion (naming
policy, `Promise` unwrapping, `.d.ts` syntax); that "personality" belongs to a
consumer such as the
[`ts-jsexport` TypeScript facade](../../docs/design/ts-jsexport.md).
The single-argument `Build(surface)` overload is a declaration-only
compatibility seam for metadata-focused tests and hand-composed surfaces. It
does not establish runtime publication; the product path always supplies
Analysis body evidence. The body-backed overload requires exact non-null
wrapper candidates; legacy null provenance is accepted only by the
declaration-only seam.

Serializer-context getters authenticate registered roots only when their
context carries the
`[GeneratedCode("System.Text.Json.SourceGeneration", ...)]` marker emitted by
the System.Text.Json source generator and Analysis confirms the known generated
implementation. The root getter must pass its own `this` receiver to the
context's `Options` getter and an exact `ldtoken` of the registered root type
to trusted `JsonSerializerOptions.GetTypeInfo`, and the `GetTypeInfo` result
must be the value stored into the instance cache field that the getter's entry
reload reads back, and *every* reachable return of that getter must hand back
either the cache load or that same fresh `GetTypeInfo` result. A getter has a
cached path and a fresh path that merge at a shared `ret`, so proving one store
and one load leaves the fresh path's returned value unproven; the complete set
of return alternatives comes from Analysis's `MethodReturnFlow`, and a null,
unresolved, or extra alternative fails closed. Cache field loads and stores are
linked by `FieldIdentity`, which canonicalizes a unique local `MemberRef`
name-and-signature match to its reader-local `FieldDef`, including when the
member parent is a `TypeRef`, `TypeSpec`, or `ModuleRef` whose declaring type
resolves back to the current module or assembly. A colliding external scope,
duplicate match, signature mismatch, or otherwise unresolved access cannot stand
in for that field and fails closed. Write candidates are selected with
`MightBeSameFieldAs` rather than equality, so a store that names the field
without canonicalizing to its definition is counted rather than dropped.
The context initializer must construct the default options
into a static field, load that field into the `JsonSerializerOptions` copy
constructor, pass the copy to the context constructor, and store the constructed
context into the exact field `Default` returns. The generated context
constructor reached by that chain must itself forward to
`JsonSerializerContext::.ctor(JsonSerializerOptions)`, passing its own `this` as
receiver and its own options parameter as the argument, so a constructor that
drops the caller's authenticated options on the floor cannot inherit their
trust. That check rests on Analysis proving an `ldarg` is the *original*
argument: a slot that is reassigned by `starg`, merged from several definitions,
or address-taken by `ldarga` no longer resolves as an argument at all. A
diagnosed, incomplete, or unreachable constructor body fails rather than being
skipped. The base-constructor call must dominate every normal return, so an
early-return branch cannot bypass forwarding while leaving one authentic call
reachable. Every one of those calls and
field accesses must be reachable from its body entry. Unrelated static
initialization in the same generated `.cctor` — for example a user-written
partial's own `public static readonly JsonSerializerOptions` — is allowed,
because the chain is followed link by link rather than counted; ambiguity fails
closed. The publicly constructible marker is classification evidence, not
publication authority. A handwritten context with matching
`[JsonSerializable]`, marker, property name, and `JsonTypeInfo<T>` signature
remains unsupported when reached. A matching property must be an instance,
parameterless, getter-only property; an indexed or otherwise user-shaped
sibling is retained as reached failure evidence rather than inheriting the
registration. The generated getter's receiver must also flow from the same
context's authenticated `Default` property. Body-backed publication requires
its return to carry a non-null structured definition identity equal to the
context; the declaration-only compatibility seam continues to accept legacy
missing structured names. A custom context instance can carry runtime
`JsonSerializerOptions` that change the wire shape independently of
source-generation metadata, so an unproven receiver fails.
Two matching generated-root PropertyDefs with the same metadata identity also
fail rather than letting declaration metadata select one while runtime code
calls the other.

For a direct serializer-to-completion contract, return JSON-wire facts are
lowering-independent. A synchronous `string` export must return only
authenticated source-generated `Serialize<T>` results. A compiler-async
`Task<string>` export must feed those results into the authenticated `MoveNext`
builder completion sink, either directly or through Analysis's typed proof of
one exact state-machine field across suspension. A runtime-async `Task<string>`
export must instead carry Analysis's explicit `Runtime` attribution on the
exact exported physical method and return those same serializer results from
that method's own `ret`.
The resolver does not infer lowering from identity coincidences or recognize
runtime-async IL shapes.
Incomplete coverage, a raw/serialized mixture, an untrusted `Task<string>`
declaration, or serializer evidence from a lifted local function or another
method leaves `ReturnWireType` unset.
`Build_ProducesEqualWireFactsAcrossAsyncLoweringsForDirectSerializerResult`
and
`Build_ProducesEqualWireFactsAcrossAsyncLoweringsForSerializerStoredAcrossSuspension`
gate equal owner-issued facts from paired compilations of genuinely awaited
direct and field-carried exports.
`Build_RejectsConditionalSerializerStoreAcrossAsyncLowerings` gates the close
negative where only one branch overwrites a kickoff-initialized parameter field
with a serializer result.
`RuntimeAsyncAuthenticationRejectsForgedAttributionAndMetadata`,
`Build_RuntimeAsyncRejectsMixedSerializerAndRawReturns`,
`Build_RuntimeAsyncRejectsIncompleteReturnCoverage`, and
`Build_RuntimeAsyncRejectsAnotherMethodsSerializerEvidence` gate the close
negative boundaries.

`JsExportSurfaceBuilderTests.Build_RejectsBodylessJsExportsWithoutRuntimeWrappers`,
`Extract_RetainsFilteredJsExportRowsFromCompilerGeneratedTypes`,
`Build_RejectsJsExportWithoutGeneratedRuntimeWrapper`,
`Build_RejectsHandwrittenRuntimeWrapperCandidate`,
`Build_DoesNotBorrowWrapperRegistrationFromAnotherType`,
`Build_RejectsRegistrationBodyCountMismatch`,
`Build_RejectsRuntimeWrapperFromDifferentModule`,
`Build_RejectsRuntimeWrapperWithoutModuleIdentity`,
`Build_RejectsRuntimeWrapperWithNullModuleIdentity`,
`Build_RejectsSecondRuntimeBindingTargetWithDifferentHash`,
`Build_RejectsUnmatchedRuntimeBindingForOverloadGroup`,
`Build_RejectsRuntimeWrapperWithUnauthenticatedMarshalerArgument`,
`Build_RejectsRuntimeRegistrationWithUntrustedCoreAlias`,
`Build_RejectsRuntimeWrapperWithUntrustedCoreVoid`,
`Build_WithBodiesRejectsLegacyNullWrapperProvenance`,
`Build_DoesNotCreditPrefixSiblingWrapper`,
`Build_ProjectsDistinctRuntimeDispatchKeysForCompiledOverloads`,
`Build_PreservesNegativeRuntimeDispatchKeyLiteral`,
`Build_DoesNotBorrowAnotherOverloadWrapperRegistration`,
`Build_RejectsDiagnosedRuntimeWrapperChain`,
`Build_ProjectsRuntimeQualifiedDeclaringTypePath`,
`Build_ProjectsNestedRuntimeDeclaringTypePath`,
`Build_RejectsIndexedGetterWithGeneratedRootName`,
`Build_RejectsDuplicateGeneratedRootPropertyIdentity`,
`Build_RejectsDefaultContextReturnWithCollidingStructuredIdentity`,
`Build_RejectsDefaultContextReturnWithoutStructuredIdentity`,
`Build_RejectsReachedHandwrittenSerializerContextImplementation`,
`Build_RejectsGeneratedRootGetterWithoutTrustedBodyFlow`,
`Build_RejectsGeneratedContextWithoutTrustedDefaultInitialization`,
`Build_RejectsCustomSerializerContextInstanceReceiver`, and
`TsJsExportCommandTests.Invoke_FilteredGeneratedTypeExportFailsBeforePublication`
gate these publishability and provenance boundaries against compiled fixtures.

## Linked evidence, not adjacent evidence

Every fact above has to be *connected* to the next one. A trusted call present
in a body, a constructor counted in a `.cctor`, or a `JSMarshalerType` factory
sitting near a registration is not evidence by itself, because generated IL can
be edited to keep all of those while breaking what they produce. Authentication
therefore rests on Analysis's resolved-value union, field store/load facts, and
block reachability rather than on presence or proximity.

`GeneratedJsExportAuthenticationTests` gates that distinction by patching the
IL bytes of the real compiled fixtures and asserting the unpatched control still
publishes:

- `Build_RejectsGeneratedRootGetterThatDiscardsTypeInfo` — trusted `Options`,
  `GetTypeFromHandle`, and `GetTypeInfo` calls all remain; only the `castclass`
  that carries the result into the cache field is replaced.
- `Build_RejectsGeneratedContextWithUnlinkedDefaultInstance` — all three
  expected constructors still run; only the store that links the constructed
  context to the field `Default` returns is removed.
- `Build_RejectsGeneratedRootGetterThatReturnsNullOnTheFreshPath` — the cache
  load, the trusted `GetTypeInfo` call, and the cache store all survive; only
  the fresh path's returned value becomes `ldnull`. Its companion
  `PatchedRootGetter_ReportsNullAsAProvenReturnAlternative` asserts the patched
  body is not merely unresolvable, so the rejection is the return-flow check
  doing work rather than a decode failure.
- `Build_RejectsGeneratedContextConstructorThatDropsOptions` — the generated context
  constructor still calls the `JsonSerializerContext` base constructor with its
  own `this`; only the forwarded options argument becomes `ldnull`.
- `Build_RejectsUnreachableGeneratedWrapperEntry` — the wrapper still contains
  its call to the generated stub, but returns before reaching it.
- `Build_RejectsRegistrationWithMismatchedSignatureHash` — the registration
  keeps its exact binding name; only the `int32` hash changes.
- `Build_RejectsRegistrationWithSwappedDescriptorElement` — the registration
  keeps its name, hash, and element count; only the marshaler the element holds
  stops matching the export's own return type.
- `Build_RejectsDelegateRegistrationWithWrongNestedDescriptor` and
  `Build_RejectsDelegateRegistrationWithWrongResultDescriptor` — the generated
  delegate factory remains, but one argument or result payload descriptor
  changes.
- `Build_RejectsDelegateRegistrationWithReorderedDescriptors` and
  `Build_RejectsDelegateRegistrationWithWrongOuterFactory` — the authenticated
  managed order is swapped or the same-arity `Function` factory is replaced by
  `Action`.
- `Build_RejectsDelegateRegistrationWithMismatchedSignatureHash` and
  `Build_RejectsDelegateWrapperThatCallsDifferentExport` — a delegate export
  cannot borrow another generated registration or wrapper target.
- `Build_PublishesAuthenticatedSynchronousDelegateSignatures` — the unmodified
  compiled `Action` and `Func` controls publish their exact managed parameter
  and return shapes.
- `TryGetDelegateShape_RejectsDecodedFourArgumentAction` — decoded callback
  metadata beyond the SDK's three-parameter limit is not authenticated even
  when its generic definition is otherwise `Action`.
- `Build_AcceptsGeneratedContextWithUnrelatedStaticOptions` — the positive
  control, a real source-generated context whose user partial adds an unrelated
  static `JsonSerializerOptions`.
- `GeneratorLoader_ReadsOneImageForMetadataAndBodyEvidence` — the TypeScript
  generator loader reads the assembly once and shares one immutable image, so
  a metadata surface cannot be composed with bodies read separately from
  different content.

Two boundaries are deliberately *not* claimed. The wrapper's pointer and byref
argument marshaling is out of scope: publication proves the chain is reachable
and correctly named, shaped, and described, not that each `JSMarshalerArgument`
slot is threaded correctly inside the generated stub. And the descriptor check
compares the generated descriptor graph against the export's managed signature
through a compatibility table; it is not a reimplementation of the runtime's
`JSExportGenerator`. An export whose managed type that table does not recognize
— such as a custom delegate definition or an unsupported `[JSMarshalAs]`
override — fails visibly rather than being published on weaker evidence. The
SDK source generator itself rejects a Promise-returning
`Func<..., Task<T>>` callback and callbacks with more than three parameters
with method-scoped `SYSLIB1072`;
`UnsupportedDelegateShapes_AreRejectedBySdkGenerator` gates that boundary.
Consumers independently reject over-arity hand-composed delegate facts;
`MapParameterType_RejectsDelegateFactsBeyondSdkArity` gates that containment.
They also reject `Void` parameters and `Func<..., Void>` returns that this
producer cannot publish;
`MapParameterType_RejectsVoidDelegatePayloads` gates that boundary.

`[JSMarshalAs<JSType.BigInt>] long` is an authentic override that this library
rejects for a different reason: the descriptor is real and the wrapper is
genuine, but no consumer can describe it yet. `TsTypeMapper` emits
every `long` as TypeScript `number`, which is the wrong type for a JavaScript
`BigInt` and would silently truncate at 2^53. Until descriptor-aware TypeScript
types exist, an export carrying the `get_BigInt64` descriptor fails with a
"recognized but not supported" message naming the override and pointing at
`[JSMarshalAs<JSType.Number>]`. The `get_Int52` descriptor — which `number` does
describe — keeps publishing unchanged.
`JsExportSurfaceBuilderTests.Build_RejectsBigIntMarshaledLongExport` and
`Build_PublishesNumberMarshaledLongExport` gate the pair against compiler
output, not hand-composed metadata.

Generated serializer-root properties use System.Text.Json's default name
grammar: a vector appends `Array`, while a rank-*N* multidimensional array
appends `Array{N}D`, recursively from its element type. Multidimensional roots
are retained as explicit unsupported evidence, not supported serialization:
the System.Text.Json runtime does not serialize them. During generated-property
authentication only, omitted bounds and explicit default zero lower bounds are
equivalent because the serialized `[JsonSerializable]` type-name grammar
carries rank but not those encoding details. `JsExportSurfaceBuilderTests.Extract_RecordsMultidimensionalRootEvidenceAndSourceGeneratorNames`,
`Build_RejectsReachedMultidimensionalSerializerRoot`,
`Build_DoesNotNormalizeNonDefaultMultidimensionalArrayBounds`, and
`SourceGeneratedJson_MultidimensionalRootRemainsUnsupportedAtRuntime` are the
gates.

For scalar roots, unsupported wire-shaping context options are attached to the
exact generated `JsonTypeInfo<T>` getter. They fail visibly only if that getter
reaches an export, despite having no DTO record on which to retain the policy;
unused scalar contexts remain inert. Non-default
`PreferredObjectCreationHandling=Populate` is likewise unsupported: it can
deserialize through a getter without a participating setter, which the current
wire-member projection does not model. Authentic type- and member-level
`[JsonObjectCreationHandling(Populate)]` carry the same unsupported contract;
explicit `Replace` remains supported.
`Build_RejectsReachedUnsupportedScalarContextOptions`,
`Build_RejectsReachedPopulateObjectCreationHandling`,
`Emit_BlocksReachedPopulateObjectCreationHandlingAttribute`,
`Extract_AcceptsExplicitReplaceObjectCreationHandlingAttribute`,
`Build_IgnoresUnusedUnsupportedScalarContextAndResolvesVectorSibling`, and
`TsJsExportCommandTests.Invoke_DoesNotPublishPartialOutputWhenSurfaceIsUnsupported`
are the gates. `Build_RejectsAuthenticJsExportOperatorBeforePublication` and
`SourceGeneratedJsExport_EmitsOnlyOrdinaryMethodWrappers` gate the
ordinary-method boundary.

## JSON union alternatives

This section owns slice 2 of
[#5892](https://github.com/richlander/dotnet-inspect/issues/5892), tracked by
[#6078](https://github.com/richlander/dotnet-inspect/issues/6078).
The consumer remains `ts-jsexport`, followed by inspect-web adoption in the
four-slice tracker. It does not change raw JSExport marshalling.

`Unions` separates a union's case contracts from `Records`: the public `Value`
property is not an object wire member. Recognition consumes Metadata's typed
`HasUnionAttribute` fact. The supported convention is a value type with public
single-argument, by-value case constructors and a public instance,
parameterless `object Value` getter. Exact case signature trees come from
Analysis's same-module declared MethodDefs, joined to the Metadata declaration
by token and structured declaring-type identity. They are not reconstructed
from rendered attributes, parameter names, or C# type spelling.

For a supported convention, `CaseTypes` retains the constructor alternatives
and `IncludesNull` is true: the default union state writes JSON null even if no
constructor parameter is nullable. A case writes its own effective serializer
contract inline, without a `Value` wrapper or an invented discriminator.
The case's existing converter, naming, member, and unsupported-type rules
still apply; case types are not a replacement JSON schema. This is a statement
about successful serialization, not a guarantee that arbitrary getter code or
every possible producer value succeeds.

Case discovery carries naming policy, reached wire directions, and context
scope through constructor edges rather than through the `Value` getter.
Generic alternatives retain their parameter positions; the authenticated
closed root shape supplies their type arguments. Unused registrations stay
unreached, and unsupported declaration-only or union conventions retain a
`SerializationUnsupportedReason` with unavailable null evidence.

Deserialization classification is deliberately not modeled in this slice.
`DeserializationUnsupportedReason` remains explicit even when the runtime can
read a simple scalar union. Writing two object cases or nested unions does not
prove that a default reader can select the right case. Classifier support and
its direction-specific admission require their own evidence before that
boundary can expand.

`JsonUnionWireTests` gates these facts with compiler-produced native unions,
real source-generated serialization, ambiguous and nested read boundaries,
case discovery, direction propagation, closed generic signatures, and
unsupported converter evidence. Its neighboring ordinary DTO remains an
object. The same suite checks that TypeScript generation rejects a reached
union until slice 3 implements lowering, rather than emitting a misleading
interface.

The serializer oracle is bounded to SDK `11.0.100-preview.7.26381.103` and
[its System.Text.Json source](https://github.com/dotnet/dotnet/tree/e2c1e00b3d0f96afb892fb261d5921565b400246/src/runtime/src/libraries/System.Text.Json).
It uses the authenticated default source-generated context property. Explicit
resolver results, `WithAddedModifier`, and custom-options context instances
are not interchangeable with that path and do not inherit its evidence.

Run its test suite in Release:

```bash
dotnet run --project tests/ILInspector.JsExportSurface.Tests -c Release
```

Tests validate this library and `ts-jsexport` together against
`ILInspector.JsExportSurface.Fixtures`, a small purpose-built `[JSExport]`
surface used only as a regression fixture.

Independently compiled inputs live under
[`fixtures/js-export/`](../../fixtures/js-export/), including the shared-source
classic/runtime-async pair and the multi-assembly TypeScript contexts. Preserve
their separate projects, aliases, and assembly names when adding coverage.
The compile-negative project and JavaScript runtime probe remain under `tests/`.
