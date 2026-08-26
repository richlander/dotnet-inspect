# ILInspector.JsExportSurface

`ILInspector.JsExportSurface` is a C#-faithful object model of an assembly's
`[JSExport]` wasm/JS interop surface, projected from `ILInspector.Metadata`'s
`ApiSurface`/`ApiSurfaceExtractor`.

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
  `BindManagedFunction` calls, exactly one trusted call whose proven first
  string-literal argument equals the export's structured runtime binding name,
  non-empty equal metadata/body module MVIDs, an exact
  `System.Runtime.InteropServices.JavaScript` `JSMarshalerArgument`, plus a
  **reachable** wrapper-to-stub-to-export MethodDef call chain. The
  registration's second argument must be an `int32` literal equal to the
  decimal suffix of the wrapper's own `__Wrapper_<Name>_<digits>` name, and its
  `ReadOnlySpan<JSMarshalerType>` descriptor argument must resolve to one
  element per export return and parameter, each built by a
  `JSMarshalerType` factory compatible with that managed type. A diagnosed
  registration, wrapper, or stub body, prefix sibling, or handwritten candidate
  cannot publish another export.
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
consumer such as [`tsbindgen`](../tsbindgen).
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
reload reads back. The context initializer must construct the default options
into a static field, load that field into the `JsonSerializerOptions` copy
constructor, pass the copy to the context constructor, and store the constructed
context into the exact field `Default` returns. Every one of those calls and
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

`JsExportSurfaceBuilderTests.Build_RejectsBodylessJsExportsWithoutRuntimeWrappers`,
`Extract_RetainsFilteredJsExportRowsFromCompilerGeneratedTypes`,
`Build_RejectsJsExportWithoutGeneratedRuntimeWrapper`,
`Build_RejectsHandwrittenRuntimeWrapperCandidate`,
`Build_DoesNotBorrowWrapperRegistrationFromAnotherType`,
`Build_RejectsRegistrationBodyCountMismatch`,
`Build_RejectsDuplicatedRuntimeBindingTarget`,
`Build_RejectsRuntimeWrapperFromDifferentModule`,
`Build_RejectsRuntimeWrapperWithoutModuleIdentity`,
`Build_RejectsRuntimeWrapperWithNullModuleIdentity`,
`Build_RejectsRuntimeWrapperWithUnauthenticatedMarshalerArgument`,
`Build_RejectsRuntimeRegistrationWithUntrustedCoreAlias`,
`Build_RejectsRuntimeWrapperWithUntrustedCoreVoid`,
`Build_WithBodiesRejectsLegacyNullWrapperProvenance`,
`Build_DoesNotCreditPrefixSiblingWrapper`,
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
`TsBindGenCommandTests.Invoke_FilteredGeneratedTypeExportFailsBeforePublication`
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
- `Build_RejectsUnreachableGeneratedWrapperEntry` — the wrapper still contains
  its call to the generated stub, but returns before reaching it.
- `Build_RejectsRegistrationWithMismatchedSignatureHash` — the registration
  keeps its exact binding name; only the `int32` hash changes.
- `Build_RejectsRegistrationWithSwappedDescriptorElement` — the registration
  keeps its name, hash, and element count; only the marshaler the element holds
  stops matching the export's own return type.
- `Build_AcceptsGeneratedContextWithUnrelatedStaticOptions` — the positive
  control, a real source-generated context whose user partial adds an unrelated
  static `JsonSerializerOptions`.
- `TsBindGen_ReadsOneImageForMetadataAndBodyEvidence` — `tsbindgen` reads the
  assembly once and shares one immutable image, so a metadata surface cannot be
  composed with bodies read separately from different content.

Two boundaries are deliberately *not* claimed. The wrapper's pointer and byref
argument marshaling is out of scope: publication proves the chain is reachable
and correctly named, shaped, and described, not that each `JSMarshalerArgument`
slot is threaded correctly inside the generated stub. And the descriptor check
compares the generated descriptor graph against the export's managed signature
through a compatibility table; it is not a reimplementation of the runtime's
`JSExportGenerator`. An export whose managed type that table does not recognize
— a delegate parameter, or a `[JSMarshalAs]` override that redirects marshaling
— fails visibly with an unsupported-surface message rather than being published
on weaker evidence.

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
`TsBindGenCommandTests.Invoke_UnsupportedScalarContextOptionsFailsBeforeDeclarationOrWrapperPublication`
are the gates. `Build_RejectsAuthenticJsExportOperatorBeforePublication`,
`SourceGeneratedJsExport_EmitsOnlyOrdinaryMethodWrappers`, and
`TsBindGenCommandTests.Invoke_JsExportOperatorFailsBeforeDeclarationOrWrapperPublication` gate the
ordinary-method boundary.

Run its test suite in Release:

```bash
dotnet run --project tests/ILInspector.JsExportSurface.Tests -c Release
```

Tests validate this library and `tsbindgen` together against
`ILInspector.JsExportSurface.Fixtures`, a small purpose-built `[JSExport]`
surface used only as a regression fixture.
