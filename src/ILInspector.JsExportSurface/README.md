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
  complete wrapper-to-stub-to-export MethodDef call chain. A diagnosed
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
context carries the authentic
`[GeneratedCode("System.Text.Json.SourceGeneration", ...)]` marker emitted by
the System.Text.Json source generator. A handwritten context with matching
`[JsonSerializable]`, property name, and `JsonTypeInfo<T>` signature remains
unsupported when reached. A matching property must be an instance,
parameterless, getter-only property; an indexed or otherwise user-shaped
sibling is retained as reached failure evidence rather than inheriting the
registration. The generated getter's receiver must also flow from the same
context's authenticated `Default` property, whose return carries the same
structured definition identity as the context. A custom context instance can
carry runtime `JsonSerializerOptions` that change the wire shape independently
of source-generation metadata, so an unproven receiver fails.
Two matching generated-root PropertyDefs with the same metadata identity also
fail rather than letting declaration metadata select one while runtime code
calls the other.

`JsExportSurfaceBuilderTests.Build_RejectsBodylessJsExportsWithoutRuntimeWrappers`,
`Extract_RetainsFilteredJsExportRowsFromCompilerGeneratedTypes`,
`Build_RejectsReachedHandwrittenSerializerContextGetter`,
`Build_RejectsJsExportWithoutGeneratedRuntimeWrapper`,
`Build_RejectsHandwrittenRuntimeWrapperCandidate`,
`Build_DoesNotBorrowWrapperRegistrationFromAnotherType`,
`Build_RejectsRegistrationBodyCountMismatch`,
`Build_RejectsDuplicatedRuntimeBindingTarget`,
`Build_RejectsRuntimeWrapperFromDifferentModule`,
`Build_RejectsRuntimeWrapperWithoutModuleIdentity`,
`Build_RejectsRuntimeWrapperWithNullModuleIdentity`,
`Build_RejectsRuntimeWrapperWithUnauthenticatedMarshalerArgument`,
`Build_WithBodiesRejectsLegacyNullWrapperProvenance`,
`Build_DoesNotCreditPrefixSiblingWrapper`,
`Build_RejectsDiagnosedRuntimeWrapperChain`,
`Build_ProjectsRuntimeQualifiedDeclaringTypePath`,
`Build_ProjectsNestedRuntimeDeclaringTypePath`,
`Build_RejectsIndexedGetterWithGeneratedRootName`,
`Build_RejectsDuplicateGeneratedRootPropertyIdentity`,
`Build_RejectsDefaultContextReturnWithCollidingStructuredIdentity`,
`Build_RejectsCustomSerializerContextInstanceReceiver`, and
`TsBindGenCommandTests.Invoke_FilteredGeneratedTypeExportFailsBeforePublication`
gate these publishability and provenance boundaries against compiled fixtures.

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
`SourceGeneratedJsExport_EmitsOrdinaryWrapperButNotOperatorWrapper`, and
`TsBindGenCommandTests.Invoke_JsExportOperatorFailsBeforeDeclarationOrWrapperPublication` gate the
ordinary-method boundary.

Run its test suite in Release:

```bash
dotnet run --project tests/ILInspector.JsExportSurface.Tests -c Release
```

Tests validate this library and `tsbindgen` together against
`ILInspector.JsExportSurface.Fixtures`, a small purpose-built `[JSExport]`
surface used only as a regression fixture.
