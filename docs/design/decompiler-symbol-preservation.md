# Decompiler name and symbol preservation

This document is the Decompiler-owned contract for identifier and symbol names
in reconstructed C#. It answers five questions:

1. Which names the current decompiler preserves.
2. Which surviving names it should preserve but currently does not.
3. Which names it synthesizes only for readable presentation.
4. Which source spellings the compiled artifact cannot establish.
5. Which artifact identities survive but have no lossless C# spelling.

The contract is intentionally about names and bindings. It does not require the
decompiler to reproduce source syntax choices such as a switch expression
instead of a switch statement, an expression body instead of a block, or one
equivalent qualification style instead of another.

Metadata and Portable PDB acquisition remain owned by their existing layers.
The identity currencies consumed here remain owned by
[Type, Member, and API Representation](type-member-api-representation.md).
This document specifies only how the Decompiler adopts that evidence into
reconstructed source.

## Preservation vocabulary

The word *preserved* is narrower than "looks like the source":

| Classification | Meaning |
| -------------- | ------- |
| **Preserved** | An authoritative artifact fact identifies the name, and reconstructed C# emits that identity. C# escaping such as `@delegate` is a lossless spelling of the same identifier. |
| **Recoverable gap** | Authoritative artifact evidence identifies the name and its binding, but the current Decompiler does not carry or consume it. Every gap in this document has a tracking issue. |
| **Synthesized** | No authoritative source name survives. The Decompiler may choose an honest readable name, but must not present it as recovered source identity. |
| **Unrepresentable** | Authoritative artifact evidence identifies the name and binding, but no lossless legal C# spelling exists. Preserve typed identity and fail or degrade visibly; do not sanitize it into a different claimed identity. |
| **Irrecoverable** | The artifact does not retain enough evidence to identify the authored name or bind that name to the reconstructed use. The Decompiler must not guess. |

"Authoritative artifact fact" means evidence in the inspected PE, metadata, or
matching Portable PDB, not the fixture source file known to a test author.
Obfuscation or post-compilation rewriting changes the artifact's identity. The
Decompiler can preserve the resulting metadata name when C# can represent it,
or report that identity as **Unrepresentable** when it cannot. It cannot
establish the name that existed before the rewrite.

## Evidence model

Evidence is adopted in this order:

| Evidence | What it can authorize | What it cannot authorize |
| -------- | --------------------- | ------------------------ |
| ECMA-335 metadata | Declared namespace, type, member, parameter, and generic-parameter identities; field and property identities used by supported generated forms | A lossless C# spelling for every legal metadata identity; pre-obfuscation names, local-variable names, labels, or aliases used at a particular source occurrence |
| Matching Portable PDB | A `LocalVariable` name bound to an exact IL local slot and its `LocalScope`; other typed debug records once a Decompiler consumer exists | Names for values that never occupy an IL local; source syntax or use-site binding not represented by a debug record |
| Authenticated compiler-generated metadata | Source identifiers embedded in a recognized lowering, such as successful lambda/local-function raises and iterator state-machine fields | Decoding an unauthenticated lookalike, or claiming a source name after the relevant raise declines |
| Authenticated semantic IL name literal | A string that an owned lowering binds as an identifier, such as an `Expression.Parameter` name or dynamic binder member name | Treating an arbitrary string literal, including a `nameof` result, as identifier provenance |
| Decompiler analysis | Collision-free, readable presentation names derived from typed IR roles | Authored identity |
| Checksum-verified source content | Exact text for the separate `PDB Source` view | Permission to inject source-only declarations into reconstructed `Decompiled Source` |

Structured symbol identity and rendered spelling remain separate. A `TypeRef`,
method token, or parameter ordinal identifies a symbol even when the printer
must qualify, escape, sanitize, or decline its preferred short spelling. The
printer must prefer valid, correctly bound C# over a familiar-looking name that
would bind to another symbol.

Parameter-name provenance survives metadata import independently of its initial
fallback spelling. Argument IR nodes retain their exact parameter or implicit
receiver binder rather than treating a reusable argument index as identity.
After nested functions
have raised, final allocation keeps every exact parameter identity fixed and
renames only synthesized fallbacks around exact enclosing and descendant
binders, exact retained locals, generic binders, and flattened local-function
declarations. When allocation changes a spelling, full-source hosts consume the
complete resulting parameter-name override for both the method or constructor
declaration and its body.

Property, indexer, and event accessors require one declaration-level allocation
across sibling bodies and C#'s implicit `value` binder. Until
[#5778](https://github.com/richlander/dotnet-inspect/issues/5778) owns that
composition, whole-type rendering uses the authoritative MethodSemantics role
even when a rewriter changed the accessor MethodDef name, and fails visibly
when an accessor body needs a changed parameter spelling or its final implicit
value binder is not exactly `value`, rather than omitting the body or emitting
a declaration/body mismatch.

Retained local allocation similarly uses one printer-owned collision relation
for output and fidelity. Exact names in sibling arms of the same raised switch
expression may be reused because their scopes are disjoint. Two bindings in
one arm, an enclosing reservation, or a flattened local-function declaration
still collides and lowers fidelity when the exact local cannot be emitted.

### Portable PDB boundary

The current Decompiler PDB consumer reads `LocalVariable` names and
`LocalScope` ranges, but collapses them into at most one name and one
scope-derived placement fact per IL slot. It does not consume every table or
custom debug record in a Portable PDB. A matching PDB therefore improves local
naming only where the compiler emitted a slot-backed local, named that slot,
and the current per-slot model can represent the evidence.

Two examples make the boundary concrete:

- A Portable PDB `LocalConstant` records a declaration name and value, but IL
  literal instructions do not record which occurrences came from that
  constant. The declaration survives; the use-site binding does not.
- A Portable PDB `ImportScope` can record aliases and imports, but an IL type or
  member reference does not record which alias, if any, was used at that source
  occurrence.

Neither case authorizes heuristic substitution in reconstructed source.

### Generated-name boundary

Compiler-generated names are evidence only when both their metadata context and
their recognized grammar establish the lowering. `GeneratedCodeIdentity` owns
the shared authentication policy used by the lambda, local-function, and
iterator consumers. An owner-specific consumer may decode a source portion
only after equivalent typed authentication, and a raising pass may emit that
source name only when it also reconstructs the binding.

When a raise declines, the output must retain a visibly generated, legal
fallback and lower fidelity as appropriate. It must not decode the embedded
source substring into a name that would bind differently.

Classic-async product imports require a
`ClassicAsyncRequestAdapterResult.RequestAvailable` built from Metadata's
exact kickoff/state-machine relationship. The reconstruction pass imports the
certified `MoveNext` MethodDef and verifies the kickoff's state-machine type
against that certificate before decoding a hoisted-local field such as
`<alpha>5__2`. That owner-specific typed authentication makes the raised
`alpha` identity **Preserved**. The tokenless synthetic-IR seam remains
shape-only test support and does not authorize a product result.

Several current paths produce useful names without yet satisfying this
authentication boundary:

- Anonymous-object import recognizes the same-assembly
  `<>f__AnonymousType...` name and reads its ordered property rows, but does not
  yet require `CompilerGeneratedAttribute`. The current positive result is
  preserved metadata spelling under a weaker admission rule;
  [#5585](https://github.com/richlander/dotnet-inspect/issues/5585) tracks the
  missing lookalike guard.
- Auto-property backing-field import decodes
  `<Property>k__BackingField` from the name grammar plus a matching property
  row, but does not require compiler-generated owner evidence.
- Primary-constructor capture rendering decodes `<parameter>P` from the name
  grammar alone rather than binding the field to authenticated generated
  metadata and the exact constructor parameter.

[#5595](https://github.com/richlander/dotnet-inspect/issues/5595) tracks the
two generated-field authentication gaps.

These are disclosed current limitations, not precedents for new generated-name
consumers.

## Current preservation contract

The following table describes current supported behavior. The probe IDs resolve
to runnable fixture commands under [Fixture probes](#fixture-probes).

| Scenario | Current contract | Probe | Regression gate |
| -------- | ---------------- | ----- | --------------- |
| Metadata declarations | Preserve ordinary and keyword artifact namespace, type, field, property, event, method, parameter, and generic-parameter names. Escape C# keywords without changing identifier identity. For body-bearing methods, parameter and retained-local identities without a lossless C# spelling or binding lower body fidelity; this includes an exact parameter colliding with a flattened local-function declaration. Type self-declarations can return a typed refusal. A synthesized method parameter yields to an exact nested binder, and its final spelling is shared by the declaration and body. Until #5778, an accessor whose body cannot use C#'s implicit `value` binder fails visibly; MethodSemantics supplies that role independently of the accessor MethodDef name. Static argument zero named `this` and bodyless-member refusal remain explicit gaps (P29 and P30). Full Unicode admission remains incomplete (P28). | P1, P25, P26, P28, P29, P30 | `KeywordIdentifierTests.KeywordParameter_IsEscaped`, `MetadataDeclarationQueryTests`, `MetadataExtensionFindingsTests.GenericExtensionSignaturePreservesBinderAndCollisionFreeFallback`, `PipelineImporterTests.Import_MissingParameterName_SynthesizesOrdinalName`, `UnspeakableNameFidelityTests.SynthesizedOuterParameterYieldsToExactCapturedLambdaParameter`, `UnspeakableNameFidelityTests.ExactParameterConflictingWithFlattenedLocalFunction_DegradesToPartial`, `MemberBodyProducerMemberRenderTests.ProduceMember_MissingSetterValueName_FailsVisibly`, `MemberBodyProducerMemberRenderTests.ProduceMember_RenamedEventValueNames_FailVisibly`, `ApiOutputFormatterTests.FormatSourceWithDeclaration_UsesBodyOwnedParameterNames`, and `TypeShellProducerTests.HostileMetadataSelfNameIsNotRendered`. |
| PDB local variables | Prefer an admitted Portable PDB local name associated with the exact IL slot and scope when the current per-slot model and collision allocation can represent it. Sibling arms of one raised switch expression may preserve the same exact name; same-arm reuse, wider reservations, and flattened local-function declarations still collide. P24 and P27 record the remaining general scope-reuse gaps. A raised nested function checks names only for slots its final body still binds or reads, because those IR nodes do not carry the root function's explicit eliminated-slot set. | P2, P24, P27 | `IrImporterTests.LocalNames_RecoveredFromPdb_RenderSourceNamesNotVSlots`, `PatternSwitchExpressionPassTests.ProductTargetMethod_SelfRaisesToPatternSwitchExpression`, and the local-collision gates in `UnspeakableNameFidelityTests`; P24 is manually probed, while `PdbLocalNameScopeTests.ReusedSlotWithDifferentScopeNames_ExposesCurrentLastNameLoss` pins P27's artifact shape and current loss. |
| Lambda parameters and captures | When an authenticated lambda raise succeeds, preserve generated-method parameter names and substitute authenticated captured-field names back to their source identifiers. Current C# permits nested parameters and locals to reuse enclosing parameter or local names; same-list duplicates and collisions with an actually referenced enclosing binder lower fidelity. | P3, P26 | `LambdaRaisingPassTests.NonCapturingExpressionBody_RaisesSimpleLambda`, `CapturingExpressionBody_SubstitutesCaptureAndRaisesLambda`, the `*ReusingOuter*` gates, and `UnspeakableNameFidelityTests` |
| Expression-tree lambda parameters | When the fully owned expression-tree factory shape raises, preserve each `Expression.Parameter` string as the lambda parameter identity. | P23 | `ExpressionTreeFidelityTests.SimpleArithmeticLambda_RecoversLambda_StaysFull` |
| Dynamic member names | When the authenticated runtime-binder call-site shape raises, preserve its member-name string as the dynamic member identity. | P23 | `DynamicCallSitePassTests.CanonicalPositive_PrintsDynamicMemberAccess` |
| Local-function declarations and parameters | When an authenticated local-function raise succeeds, recover ordinary source function and generated-method parameter names, and bind calls to the raised declaration. Captured environment identity remains binder-owned when a raised body contains another function whose arguments reuse the environment's numeric ordinal. Current C# permits nested parameters and locals to reuse enclosing parameter or local names; same-list duplicates and collisions with an actually referenced enclosing binder lower fidelity. P28 tracks full-grammar admission across authenticated generated-name paths. | P4, P26, P28 | `LocalFunctionRaisingPassTests.StaticLocalFunction_RecoveredAsDeclarationAndUnqualifiedCall`, `CapturingLocalWithNestedLambda_RaisesByEnvironmentIdentity`, the `*ReusingOuter*` gates, and `UnspeakableNameFidelityTests` |
| Anonymous-object properties | Preserve property metadata names when the current same-assembly anonymous-type shape raises, and bind initializer values to those names. The current admission is name-pattern-based (#5585) and uses the narrow identifier grammar tracked by #5616. | P5, P23 | `AnonymousObjectPassTests` gates ASCII positive output, not generated-type authentication or full Unicode admission. |
| Tuple elements in signatures | Preserve `TupleElementNamesAttribute` names on supported method returns and parameters, properties, and events. Composed field declarations remain a recoverable gap. | P6 | `TupleTypeViewTests` gates metadata decoding across all positions; method/property/event composition is manually probed. |
| Iterator hoisted locals | Recover an ordinary source local name from authenticated iterator state-machine evidence when reconstruction owns the corresponding field and use. P28 records the keyword and full-Unicode spelling gap. | P7, P28 | `IteratorReconstructionPassTests.CountingLoopIterator_RendersLoopAndYield` |
| Classic async local names | Product import authenticates the exact kickoff, state machine, and `MoveNext` before decoding ordinary `alpha` from its hoisted field. It preserves `beta` from the matching PDB local. Without symbols, `alpha` remains preserved while `beta` falls back to `V_1`. Loop-role names such as `sum` and `task` are synthesized preferences, not recovered identity, and remain collision-resolved. P28 records the full-grammar generated-name gap. | P8, P28 | `PipelineImporterTests.Import_CarriesAuthenticatedClassicRequestSeed` and `ClassicAsyncRequestAdapterTests.ClassicPass_ImportsCertifiedExecutionMethod` gate owner authentication; `ClassicAsyncReconstructionHonestyTests.SequentialAwaitLocalNameComesFromSymbols` gates the raised names with PDBs, while `LoopRoleNamesAreSynthesized` gates role provenance. `NestedScopeNameCollisionTests` gates synthesized-name collision handling. The no-symbol result is manually probed; #5587 tracks correcting its vacuous focused test. |

These guarantees are conditional on successful reconstruction. A method may
carry a recoverable substring in a generated metadata name while the containing
lambda, local function, iterator, or async shape remains lowered. That case is
not "partially preserved"; it is a declined raise with generated fallback
identity.

## Known recoverable gaps

These are cases where the artifact carries a binding-capable name, but current
output does not preserve it.

| Gap | Surviving evidence | Current output | Target | Issue and probe |
| --- | --- | --- | --- | --- |
| Static argument zero named `this` | The Param row preserves the ordinary parameter identity `this`, and `HasThis == false` proves that argument zero is not an implicit receiver. | The declaration emits `@this`, but shared value, receiver, ref, and cast paths can print its uses as the `this` keyword, producing CS0026 while fidelity remains Full. | Gate every implicit-receiver spelling on `HasThis`; otherwise render the ordinary parameter identity as `@this` in every position. | [#3260](https://github.com/richlander/dotnet-inspect/issues/3260), P29 |
| Declined capturing local functions | The generated local-function method embeds `AddSquare`; the display-class field embeds captured `n`. | Generated support identifiers such as `___c__DisplayClass...` and `__CapturingLocalFunctionWithLocal_g__AddSquare...` remain. | Raise the supported environment and function, or retain an honest valid fallback without losing the available source identity when binding can be proved. | [#3129](https://github.com/richlander/dotnet-inspect/issues/3129), P11 |
| Same-named local functions in disjoint source scopes | Each authenticated generated method embeds `Pick`; local-function ordinals and distinct MethodDefs distinguish the two definitions. | Both calls retain generated fallback names because declarations are flattened into one scope. | Recover each declaration into its own source scope while preserving each call's binding. | [#3878](https://github.com/richlander/dotnet-inspect/issues/3878), P12 |
| Tuple element names on locals | `TupleElementNames` custom debug information is attached to each exact Portable PDB `LocalVariable`. | Local variable names survive, but types render as `ValueTuple<int, int>` and uses as `Item1`/`Item2`. | Carry the names with the local type and use `(int Sum, int Product)` plus `.Sum`/`.Product` when structurally valid. | [#5578](https://github.com/richlander/dotnet-inspect/issues/5578), P13 |
| Full C# identifier grammar on PDB locals | Exact slot-bound names include the keyword `class` and combining-mark identifier `A\u0301`; C# can losslessly spell them as `@class` and `A\u0301`. | PDB-name admission rejects keywords before escaping and accepts a narrower character grammar, so output synthesizes replacements. Raised nested functions lack an explicit eliminated-slot set, so fidelity checks only the slots their final bodies still bind or read. | Admit compiler-supported identifier identities, reserve the underlying identity for collisions, and apply position-appropriate escaping in root and raised nested functions. | [#5586](https://github.com/richlander/dotnet-inspect/issues/5586), P14 |
| Generated backing-field and primary-constructor names | Auto-property fields have matching property rows; primary-constructor captures can be bound to exact constructor parameters and compiler-generated owner evidence. | Current output decodes `<Property>k__BackingField` from grammar plus the property row and `<parameter>P` from grammar alone. | Authenticate each generated field and its exact source-symbol binding before decoding. | [#5595](https://github.com/richlander/dotnet-inspect/issues/5595), P22 |
| Full C# identifier grammar in semantic-name consumers | Owned expression-tree and dynamic lowerings retain identifier strings; anonymous types retain property metadata names. | Combining-mark names make all three raises decline because they share the narrow `IsEscapableIdentifier` admission. | Admit the compiler-supported grammar only after each lowering establishes the string or metadata name's typed binding. | [#5616](https://github.com/richlander/dotnet-inspect/issues/5616), P23 |
| Full C# identifier grammar across metadata and authenticated generated names | Metadata and certified iterator/classic-async lowerings retain exact identifier identities, including keywords and combining-mark names. | Some method, type-segment, generic-parameter, and generated-local paths still use the narrower `IsEscapable` admission. Valid names can lower fidelity or fall back to `V_n`. | Use one compiler-characterized, position-aware admission contract while keeping metadata, generated, semantic-literal, and PDB evidence ownership distinct. | [#5657](https://github.com/richlander/dotnet-inspect/issues/5657), P28 |
| Same-named PDB locals in disjoint scopes | Distinct PDB local rows bind `same` to different IL slots and non-overlapping `LocalScope` ranges. | The function-wide allocator preserves the first `same` and replaces the second despite the legal disjoint scopes. | Allocate final names against lexical overlap rather than whole-function use, while retaining collision safety. | [#5617](https://github.com/richlander/dotnet-inspect/issues/5617), P24 |
| Different PDB names for one reused slot | Distinct PDB local rows bind `first` and `second` to the same IL slot in non-overlapping `LocalScope` ranges. | The per-slot importer keeps only the later name and can apply `second` to uses in the earlier `first` scope. | Carry scope-qualified local identity through import and final declaration placement. | [#5617](https://github.com/richlander/dotnet-inspect/issues/5617), P27 |
| Tuple element names on composed fields | `TupleElementNamesAttribute` identifies the field's element names, and the metadata view decodes them. | Type source renders `ValueTuple<int, string> NamedTupleField` while neighboring property and event declarations retain tuple names. | Carry the field's typed tuple-name evidence into composed C# field declarations. | [#5618](https://github.com/richlander/dotnet-inspect/issues/5618), P6 |

The current decline behavior is itself gated:

- `LocalFunctionRaisingPassTests.CapturingLocalFunctionWithLocal_StaysLowered`
  prevents an unproved capturing raise.
- `UnraisedLocalFunctionCallTests.SameNamedRaiseCandidates_AreBothDeclined`
  prevents false same-name binding.
- `TupleBinaryOperatorPassTests.SourceNamedLocalTupleFieldComparison_IsNotRaised`
  pins the current tuple-local output until #5578 supplies stronger evidence.
- `UsingStatementPassTests.KeywordNamedDisposedOnlyResource_PreservesPdbVariableDeclaration`
  proves the keyword PDB name retains declaration identity while pinning the
  current fallback spelling until #5586.

The combining-mark half of P14, both P22 generated-field paths, all three P23
Unicode paths, P24, both P28 paths, and P6's composed-field result are manual
fixture probes. P27 is an automated synthetic-artifact probe of the current
loss. Their issues own the missing target-positive and close-negative gates.

Those tests are safety rails, not declarations that the gaps are complete.

## Known unrepresentable handling gaps

These identities cannot be represented losslessly in C#, but their current
declaration path does not yet return the typed refusal required by the
**Unrepresentable** classification.

| Gap | Surviving evidence | Current output | Target | Issue and probe |
| --- | --- | --- | --- | --- |
| Bodyless member parameter identity | An interface, abstract, or extern method Param row can retain an exact identity such as `bad-name`, or one that conflicts with its method generic parameter, even though the member has no `IrFunction`. | Type composition can emit invalid declaration text such as `int Echo(int bad-name);` or `int GenericEcho<arg0>(int arg0);` without a body-fidelity cause. | Add a CSharp-owned typed declaration refusal/degradation path without manufacturing body IR. | [#5663](https://github.com/richlander/dotnet-inspect/issues/5663), P30 |

## Synthesized presentation names

When no usable source name survives, the Decompiler has three honest
presentations:

| Consumer | Default | Contract | Probe and gate |
| -------- | ------- | -------- | -------------- |
| Library and deterministic harness | Stable `V_index` local names | Keep artifact-independent output stable for fidelity and corpus comparison. | P9; `IrImporterTests.OpenWithoutSymbols_IgnoresPdb_RendersVSlotsNotSourceNames` |
| User-facing product source views | Readable names such as `num`, `num2`, or type/role-derived names | Derive only from typed IR evidence, avoid collisions, and never claim source identity. Fall back to `V_index` when evidence is insufficient. | P10; `ReadableLocalNamesTests`, `StyleOptionCatalogTests.ProductDefaults_EnableReadableNames_WithoutChangingLibraryDefaults`, and `ByteNeutralityGateTests` |
| Metadata parameters without names | Stable `arg{ordinal}` parameter names, adding the smallest `_n` suffix needed to avoid an exact binder or declaration collision | A signature can retain parameter types without optional Param rows, or with a Param row whose name is empty. Preserve that synthesized provenance through import, then reserve surviving ordinary and nested parameter identities, method generic binders, exact retained locals, and flattened local-function declarations before allocating a legal fallback without claiming authored identity. Enclosing type generic parameters are not reserved because C# permits an ordinary parameter to reuse that name. An existing artifact name is never renamed; an unrepresentable artifact binding instead requires fidelity or typed refusal, with the bodyless gap tracked by #5663. | P25; `MetadataDeclarationQueryTests.MethodDeclaration_SynthesizesParameterWhenParamRowIsAbsent`, `ParameterNameResolution_ReservesMethodGenericParameterName`, the missing-name gates in `PipelineImporterTests`, `UnspeakableNameFidelityTests.SynthesizedOuterParameterYieldsToExactCapturedLambdaParameter`, and `ApiOutputFormatterTests.FormatSourceWithDeclaration_UsesBodyOwnedParameterNames` gate provenance, final nested-scope allocation, and declaration/body agreement. |

The complete synthesis policy is owned by
[Readable local names](readable-local-names.md). Improving an `S_N`, `V_N`, or
awkward synthesized name is presentation work, not source-name recovery.
Accessor declaration/body coordination remains the explicit
[#5778](https://github.com/richlander/dotnet-inspect/issues/5778) boundary.
[#3165](https://github.com/richlander/dotnet-inspect/issues/3165) tracks
real-world readability improvements. P15 is the fixture boundary: `y` existed
in source, but the Release artifact retained only the value flow, so any
replacement for `S_256` would still be synthesized. P15 is a manual fixture
probe; no focused automated gate currently asserts that `y` is absent.

Fidelity reflects the evidence available to a particular inspection. The same
IL can therefore render identical fallback text at Full fidelity without
symbols and at Partial fidelity when a matching PDB exposes an exact local
identity that C# cannot represent. P26 gates that deliberate distinction.

## Names that reconstructed source cannot promise

The following cases either lack enough artifact evidence for an exact authored
name or retain an exact artifact identity that C# cannot spell. Each row has a
fixture probe so the boundary can be rechecked as producers and the Decompiler
evolve.

| Scenario | Why exact preservation is unavailable | Required behavior | Probe |
| -------- | ------------------------------------- | ----------------- | ----- |
| C#-unrepresentable metadata or PDB identity | ECMA-335 permits namespace, type, member, and parameter names such as `A+B` or `bad-name`; a PDB can bind the same text to a local. Exact parameter identities can also collide with method generic parameters or enclosing raised-function parameters in a C# declaration space. | Preserve typed identity and surface a typed refusal or partial-fidelity output. Do not sanitize the name and claim a different binding. The current bodyless-member exception is tracked by #5663/P30. | P26, P30 |
| Optimized-away local | `pointer` has no IL local slot and no Portable PDB `LocalVariable`; only `value` survives. | Preserve `value`; express the remaining address/dereference semantics without inventing `pointer`. | P16 |
| Runtime-async stack value | The runtime-async fixture PDB names only slot-backed `beta`; `alpha` remains on the lowered value path without a named local record. The classic lowering instead hoists `alpha` into the named state-machine field `<alpha>5__2`; `beta` is a slot-backed PDB local in both lowerings. | Preserve `beta`; synthesize or structure the other runtime-async value honestly. Do not copy `alpha` from fixture source or from the classic sibling. | P17 |
| Source label | IL branches retain target offsets, not a source label such as `done`. | Structure the control flow or use an IL-derived label when a retained branch requires one. Do not claim the authored label. | P18 |
| Local-constant use | The PDB retains `hugeCount` and its value, but IL does not associate a matching literal instruction with that declaration. | Do not replace matching literals with `hugeCount` unless a future evidence source proves use-site binding. A separate symbol inventory could expose the declaration without changing reconstructed expressions. | P19 |
| Source alias or qualification choice | An import scope may retain `IrConvert = ...Convert`, but IL member/type references identify the target, not whether this occurrence used the alias, a short name, `global::`, or a fully qualified name. | Emit an unambiguous C# spelling of the target symbol. Do not claim the authored alias. | P20 (manual; no focused non-inference gate) |
| `nameof` expression | Compilation stores the resulting string `"x"`; it does not retain provenance that distinguishes `nameof(x)` from an authored string literal. | Preserve the string value. Do not infer `nameof` from a matching in-scope identifier. | P21 (manual; no focused non-inference gate) |

The same rule covers discards, compiler-elided temporaries, pre-obfuscation
names, and other source-only identities: semantic resemblance is not identity
evidence.

## Source content is a separate result

A checksum-verified `PDB Source` result may show exact authored text, including
names that were erased from IL and local-variable debug records. It should be
used when the user's question is "what source was built?" `Decompiled Source`
answers a different question: "what source can be reconstructed from the
artifact?"

The Decompiler does not parse acquired source and splice names into its IR.
Doing so would create a second source-to-MethodDef correspondence problem and
could inject declarations with no reconstructed artifact binding. P16
demonstrates the intended separation: `PDB Source` shows `pointer`, while
`Decompiled Source` does not pretend that the erased local survived.

PDB acquisition, checksum validation, local-clone policy, and SourceLink remain
owned by [PDB acquisition](../pdb-acquisition.md).

## Fixture probes

Run these commands from the repository root. Build once in Release so the
commands inspect the same optimized fixture shape used by the Decompiler suite:

```bash
dotnet build dotnet-inspect.slnx -c Release

CFG=artifacts/bin/ILInspector.Decompiler.Tests/release/ILInspector.Decompiler.Tests.dll
CSHARP_TEXT=tests/CSharpText.Tests/bin/Release/net11.0/CSharpText.Tests.dll
CLASSIC=artifacts/bin/ILInspector.Decompiler.Fixtures.ClassicAsync/release/ILInspector.Decompiler.Fixtures.ClassicAsync.dll
LADDER=artifacts/bin/ILInspector.Decompiler.Fixtures.Ladder/release/ILInspector.Decompiler.Fixtures.Ladder.dll
METADATA_TESTS=tests/ILInspector.Metadata.Tests/bin/Release/net11.0/ILInspector.Metadata.Tests.dll
RUNTIME=artifacts/bin/ILInspector.Decompiler.Fixtures.RuntimeAsync/release/ILInspector.Decompiler.Fixtures.RuntimeAsync.dll

inspect_member() {
  dotnet run --project src/dotnet-inspect -c Release --no-build -- \
    member "$1" "$2" --library "$3" \
    -S "Decompiled Source" --bare --tips q
}
```

### P1: metadata declaration names and keyword escaping

```bash
inspect_member ILInspector.Decompiler.Tests.CfgSampleClass KeywordParam "$CFG"
inspect_member ILInspector.Decompiler.Tests.CfgSampleClass GreaterAsByte "$CFG"

dotnet run --project src/dotnet-inspect -c Release --no-build -- \
  type ILInspector.Metadata.Tests.TupleSampleClass \
  --library "$METADATA_TESTS" \
  -S "Decompiled Source" --bare --tips q
```

Expected name observations:

```csharp
public static int KeywordParam(int @delegate)
public static bool GreaterAsByte<T>(T left, T right)
```

The type command additionally shows the metadata namespace and type plus field
`NamedTupleField`, property `NamedTupleProp`, event `TupleEvent`, and method
names. That whole-type composition observation is manual; the metadata-layer
surface is gated by `MetadataDeclarationQueryTests`.

### P2: Portable PDB local names

```bash
inspect_member ILInspector.Decompiler.Tests.CfgSampleClass ReverseCopy "$CFG"
```

Expected: the declarations and uses are named `i` and `j`, not `V_0` and
`V_1`.

### P3: lambda parameter and capture names

```bash
inspect_member ILInspector.Decompiler.Tests.CfgSampleClass CapturingLambda "$CFG"
```

Expected:

```csharp
x => x + n
```

### P4: local-function declaration and parameter names

```bash
inspect_member ILInspector.Decompiler.Tests.CfgSampleClass DoubleViaLocalFunction "$CFG"
```

Expected:

```csharp
return Twice(x);
static int Twice(int v) => v * 2;
```

### P5: anonymous-object property names

```bash
inspect_member ILInspector.Decompiler.Tests.CfgSampleClass AnonNamed "$CFG"
```

Expected:

```csharp
new { Id = x, Name = y }
```

### P6: tuple element names in a signature

```bash
inspect_member ILInspector.Decompiler.Tests.CfgSampleClass TuplePair "$CFG"

dotnet run --project src/dotnet-inspect -c Release --no-build -- \
  type ILInspector.Metadata.Tests.TupleSampleClass \
  --library "$METADATA_TESTS" \
  -S "Decompiled Source" --bare --tips q
```

Expected:

```csharp
public static (int Sum, int Product) TuplePair(int a, int b)
public (int width, int height) NamedTupleProp { get; set; }
public event Action<(int code, string message)> TupleEvent;
```

The same type output currently renders
`ValueTuple<int, string> NamedTupleField`; that field-only recoverable gap is
tracked by #5618. The composed-source comparison is manual, while
`TupleTypeViewTests` gates the artifact-backed names across method, field,
property, and event metadata views.

### P7: iterator hoisted-local name

```bash
inspect_member ILInspector.Decompiler.Tests.CfgSampleClass YieldRange "$CFG"
```

Expected: the reconstructed loop consistently uses `i`.

### P8: classic-async hoisted-local names

```bash
inspect_member \
  ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures \
  TwoSequentialNamedAwaits \
  "$CLASSIC"

dotnet run --project tools/DecompilerHarness -c Release --no-build -- \
  "$CLASSIC" \
  --dump 'ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures::TwoSequentialNamedAwaits' \
  --skip-pdb

dotnet run --project src/ILInspector.Decompiler.Tests -c Release --no-build -- \
  -method '*LoopRoleNamesAreSynthesized*'
```

Expected with symbols: current output declares and uses both `alpha` and
`beta`. `alpha` is preserved from an authenticated classic-async
kickoff/state-machine relationship plus its exact hoisted field; `beta` is
preserved from the PDB. Without symbols, output still preserves `alpha` but
falls back to `V_1` for `beta`. The no-symbol result is a manual probe; #5587
tracks its focused gate. The final command verifies that reconstruction records
role-derived `sum` and `task` as synthesized preferences rather than recovered
identity. The generated-name collision gates verify that such preferences are
renamed without lowering fidelity when an enclosing binder already owns the
spelling.

### P9: deterministic no-symbol local names

`--skip-pdb` is a DecompilerHarness option, not a product CLI option.

```bash
dotnet run --project tools/DecompilerHarness -c Release --no-build -- \
  "$CFG" \
  --dump 'ILInspector.Decompiler.Tests.CfgSampleClass::ReverseCopy' \
  --skip-pdb
```

Expected: the final source uses stable `V_0` and `V_1` names.

### P10: user-facing no-symbol synthesis

Copy only the PE so the product cannot discover the adjacent PDB:

```bash
probe_dir=$(mktemp -d /tmp/dotnet-inspect-name-probe.XXXXXX) || exit 1
cp "$CFG" "$probe_dir/"
inspect_member \
  ILInspector.Decompiler.Tests.CfgSampleClass \
  ReverseCopy \
  "$probe_dir/ILInspector.Decompiler.Tests.dll"
rm "$probe_dir/ILInspector.Decompiler.Tests.dll"
rmdir "$probe_dir"
```

Expected: the user-facing defaults synthesize `num` and `num2`. Those are
readable substitutes, not claims that the source used those names.

### P11: declined capturing local-function names

```bash
inspect_member \
  ILInspector.Decompiler.Tests.CfgSampleClass \
  CapturingLocalFunctionWithLocal \
  "$CFG"
```

Current gap: output retains names such as `___c__DisplayClass...` and
`__CapturingLocalFunctionWithLocal_g__AddSquare...` instead of reconstructing
the source `AddSquare` binding and captured `n`.

### P12: same-named local functions in disjoint scopes

```bash
inspect_member \
  ILInspector.Decompiler.Tests.DuplicateLocalFunctionNameSamples \
  BothRaise \
  "$CFG"
```

Current gap: calls use two generated `__BothRaise_g__Pick_...` names instead of
two source-scoped `Pick` declarations.

### P13: tuple element names on locals

```bash
inspect_member \
  ILInspector.Decompiler.Tests.TupleBinaryAdversarialSamples \
  SourceNamedLocalTupleFields \
  "$CFG"
```

Current gap: `leftCopy` and `rightCopy` survive, but their local types and uses
lose `Sum` and `Product` and render `ValueTuple<int, int>` plus
`Item1`/`Item2`.

### P14: full C# identifier grammar on PDB locals

```bash
inspect_member \
  ILInspector.Decompiler.Tests.CfgSampleClass \
  KeywordNamedDisposedOnlyUsingResource \
  "$CFG"

inspect_member \
  CSharpText.Tests.UnicodeIdentifierFixtures \
  CombiningMarkLocal \
  "$CSHARP_TEXT"
```

Current gap: the first PDB binds source `@class` to the resource local as
`class`, but user-facing output synthesizes `iDisposable` instead of applying
the lossless C# escape. The second PDB binds the valid combining-mark identifier
`A\u0301`, but the narrower local-name admission falls back instead of
preserving it. The combining-mark result is a manual fixture probe; #5586 owns
its missing focused gate.

Both names have a lossless C# spelling, so current representability fidelity
can remain Full even when the narrower allocator substitutes a fallback. Full
in this case means that no unrepresentable identity or binding collision was
proved; it does not assert that the selected display spelling preserved every
available artifact identity. The explicit P14 observation and #5586, rather
than the fidelity enum alone, record that preservation gap.

### P15: erased local with an honest synthesized name

```bash
inspect_member \
  ILInspector.Decompiler.Tests.CfgSampleClass \
  StaticLocalFunctionWithLocal \
  "$CFG"
```

Expected boundary: the raised local function uses `S_256`, not the source name
`y`. Release compilation retained the value flow but no slot-backed local-name
evidence for `y`. This is a manual fixture probe; no focused automated gate
currently asserts the absence of `y`.

### P16: optimized-away local and source-view contrast

```bash
inspect_member \
  ILInspector.Decompiler.Tests.CfgSampleClass \
  RuntimeAsyncNoAwaitUnsafe \
  "$CFG"

dotnet run --project src/dotnet-inspect -c Release --no-build -- \
  member ILInspector.Decompiler.Tests.CfgSampleClass \
  RuntimeAsyncNoAwaitUnsafe \
  --library "$CFG" \
  --repo "$PWD" \
  -S "PDB Source" --bare --tips q
```

Expected boundary: `Decompiled Source` preserves `value` but has no `pointer`
declaration. The separate checksum-verified `PDB Source` view shows both names.
This is a manual fixture probe; no focused automated regression gate currently
asserts the absence of `pointer` from the artifact.

### P17: lowering-dependent async names

```bash
inspect_member \
  ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures \
  TwoSequentialNamedAwaits \
  "$CLASSIC"

inspect_member \
  ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures \
  TwoSequentialNamedAwaits \
  "$RUNTIME"
```

Expected boundary: classic async preserves `alpha` from its authenticated
state-machine relationship and exact hoisted field, and preserves `beta` from
the PDB local. Runtime async preserves the same PDB-backed `beta` but currently
renders its unnamed first value as `S_256`. Direct
Portable PDB inspection shows only the `beta` local row in the runtime artifact.
This comparison is a manual probe rather than an automated cross-lowering name
gate.

### P18: erased source label

```bash
inspect_member \
  ILInspector.Decompiler.Tests.CfgSampleClass \
  GotoCommonExitGuardedMerge \
  "$CFG"
```

Expected boundary: structured output contains no authored `done` label.
`CompletenessTests.CommonExitGotos_RecoveredByRegionExitDiamond` gates the
structured result, not preservation of an erased label.

### P19: local-constant declaration without use-site binding

```bash
inspect_member \
  ILInspector.Decompiler.Tests.StackAllocSpanPassTests \
  DirectPointerWithByteSizeCountOverflow_DoesNotRaise \
  "$CFG"
```

Expected boundary: the source constant `hugeCount` appears as literal
`1073741825`. The matching Portable PDB has a `LocalConstant` named
`hugeCount`, but does not bind the IL literal occurrence to that declaration.
This artifact-boundary observation is a manual probe; the pass test gates only
the stack-allocation behavior.

### P20: source alias without use-site binding

```bash
inspect_member \
  ILInspector.Decompiler.Tests.StackAllocSpanPassTests \
  ConvertWrappedStackallocPointer_Raises \
  "$CFG"
```

Expected boundary: the fixture source aliases the IR node as `IrConvert`, while
decompiled output identifies the target type as `Convert`. The method-level
render does not claim the file-level alias choice. This is a manual fixture
probe; no focused automated gate currently asserts alias non-inference.

### P21: `nameof` without expression provenance

```bash
inspect_member \
  ILInspector.Decompiler.Tests.CfgSampleClass \
  ChecksThenTry \
  "$CFG"
```

Expected boundary:

```csharp
throw new ArgumentOutOfRangeException("x");
```

The literal value survives; the fact that source wrote `nameof(x)` does not.
This is a manual fixture probe; no focused automated gate currently asserts
`nameof` non-inference.

### P22: generated field names awaiting authentication

```bash
inspect_member \
  ILInspector.Decompiler.Tests.BackingFieldSample \
  Number \
  "$CFG"

dotnet run --project src/dotnet-inspect -c Release --no-build -- \
  type ILInspector.Decompiler.Tests.PrimaryCtorSample \
  --library "$CFG" \
  -S "Decompiled Source" --bare --tips q
```

Current output maps `<Number>k__BackingField` to the property name `Number` and
maps `<alpha>P` and `<beta>P` to `alpha` and `beta`. Those are useful
compiler-fixture spellings, but the current decoders do not yet establish the
authentication required for **Preserved** identity. These are manual fixture
probes; #5595 owns the missing positive and lookalike gates.

### P23: semantic IL name literals and full Unicode identifiers

```bash
inspect_member \
  ILInspector.Decompiler.Tests.ExpressionTreeSamples \
  Simple \
  "$CFG"

inspect_member \
  LadderRung9.DynamicAndExpressionTrees \
  DynamicGetLength \
  "$LADDER"

inspect_member \
  CSharpText.Tests.UnicodeIdentifierFixtures \
  CombiningMarkExpressionTree \
  "$CSHARP_TEXT"

inspect_member \
  CSharpText.Tests.UnicodeIdentifierFixtures \
  CombiningMarkDynamicMember \
  "$CSHARP_TEXT"

inspect_member \
  CSharpText.Tests.UnicodeIdentifierFixtures \
  CombiningMarkAnonymousProperty \
  "$CSHARP_TEXT"
```

The ASCII fixtures raise to `x => unchecked(x + 1)` and `value.Length`,
preserving names carried by authenticated semantic string operands. The
combining-mark fixtures retain expression-factory, runtime-binder, and
generated anonymous-type scaffolding instead of emitting `A\u0301`. These are
manual Unicode probes; #5616 owns full-grammar admission and close literal
lookalikes.

### P24: same-named PDB locals in disjoint scopes

```bash
inspect_member \
  CSharpText.Tests.PdbScopeFixtures \
  DisjointScopeLocals \
  "$CSHARP_TEXT"
```

The PDB binds `same` to two distinct slots with disjoint scopes. Current output
preserves the first name but renders the second as a stable or readable
fallback and reports Partial because the second exact identity was not
represented. This is a manual fixture probe; #5617 owns the
lexical-scope-aware allocation gate that can preserve both identities.

### P25: unnamed metadata parameter synthesis

```bash
dotnet run --project tests/ILInspector.Metadata.Tests -c Release --no-build -- \
  -method '*MethodDeclaration_SynthesizesParameterWhenParamRowIsAbsent*'

dotnet run --project tests/ILInspector.Metadata.Tests -c Release --no-build -- \
  -method '*ParameterNameResolution*'

dotnet run --project tests/ILInspector.Metadata.Tests -c Release --no-build -- \
  -method '*GenericExtensionSignaturePreservesBinderAndCollisionFreeFallback*'

dotnet run --project src/ILInspector.Decompiler.Tests -c Release --no-build -- \
  -method '*Import_MissingParameterName_SynthesizesOrdinalName*'

dotnet run --project src/ILInspector.Decompiler.Tests -c Release --no-build -- \
  -method '*Import_SynthesizedParameterName_DoesNotCollideWithArtifactName*'

dotnet run --project src/ILInspector.Decompiler.Tests -c Release --no-build -- \
  -method '*Import_SynthesizedParameterName_DoesNotCollideWithMethodGenericParameter*'
```

The metadata gates cover a signature with no Param row and one with a present
but empty name; both use `arg0`, which is ordinal synthesis rather than source
identity. They also cover an unnamed ordinal zero beside an artifact parameter
named `arg0`, preserving the artifact name and synthesizing distinct fallback
`arg0_1`. The production extension-scanner gate emits method generic `arg0`
beside an unnamed ordinary parameter and verifies the lightweight signature
preserves `Echo<arg0>` while synthesizing `arg0_1`. The API-surface, composed
declaration, and Decompiler-import gates preserve the same binding. The
Metadata parameter-resolution probe also emits the legal contrast: enclosing
type generic `arg0` does not force a rename, so the ordinary fallback remains
`arg0`.

### P26: metadata identities without a C# spelling

```bash
dotnet run --project src/ILInspector.CSharp.Tests -c Release --no-build -- \
  -method '*HostileMetadataSelfNameIsNotRendered*'

dotnet run --project src/ILInspector.Decompiler.Tests -c Release --no-build -- \
  -method '*UnspeakableNameFidelityTests*'

dotnet run --project src/ILInspector.Decompiler.Tests -c Release --no-build -- \
  -method '*ReusingOuter*'
```

The first synthetic metadata fixture uses legal type identities that C# cannot
spell losslessly and verifies whole-type source returns a typed refusal. The
second fixture matrix verifies unspellable member and body-bearing parameter
identities, retained root and live raised nested PDB-local identities,
same-list duplicates, actual captured-binder conflicts, and method-generic
collisions lower body fidelity instead of being presented as preserved C#
identifiers. The third compiler-produced gate verifies the legal contrast in
both directions: nested parameters and locals may reuse an enclosing parameter
or local identity, and both exact names remain preserved with Full fidelity.
`NestedScopeNameCollisionTests.MaterializedLambdaSlot_AvoidsOuterStackSlotName`
keeps pass-synthesized local names on the conservative path: they are not
artifact identity and remain collision-resolved across nested scopes.

### P27: different PDB names for one reused slot

```bash
dotnet run --project src/ILInspector.Decompiler.Tests -c Release --no-build -- \
  -method '*ReusedSlotWithDifferentScopeNames_ExposesCurrentLastNameLoss*'
```

The synthetic artifact has one IL local slot. Its matching Portable PDB names
that slot `first` in scope `[3,14)` and `second` in scope `[14,27)`. Current
import retains only `second` and can use it in both ranges. The fixture test
pins that evidence and current loss; #5617 owns the scope-qualified target
representation and close negative cases.

### P28: full identifier grammar across authenticated name paths

```bash
inspect_member \
  ILInspector.Decompiler.Tests.CfgSampleClass \
  KeywordNamedIteratorLocal \
  "$CFG"

dotnet run --project src/dotnet-inspect -c Release --no-build -- \
  member \
  CSharpText.Tests.UnicodeIdentifierFixtures \
  CombiningMarkGenericParameter \
  --library "$CSHARP_TEXT" \
  -S "Fidelity Causes; Decompiled Source" --tips q

dotnet run --project src/ILInspector.Decompiler.Tests -c Release --no-build -- \
  -method '*FullGrammarGenericParameterName_ExposesCurrentNarrowAdmission*'
```

The first compiler-produced artifact retains `class` in authenticated iterator
state-machine evidence; current local allocation renders a synthesized `num`
rather than the lossless C# spelling `@class`. The second artifact retains the
generic-parameter identity `T\u0301`; output prints that exact identity, but
fidelity accounting incorrectly reports `unspellable-generic-parameter-name`.
The focused test pins that classification until #5657 corrects it.

As in P14, the first result can remain Full because representability fidelity
does not certify that the current narrower allocator selected the exact
identity. The second result is the converse accounting defect: it emits the
exact representable identity but lowers fidelity. These signals therefore
cannot replace the scenario-specific preservation probes.

Issue #5657 owns the compiler-characterized admission contract across these
metadata and authenticated generated-name paths. P14 and P23 remain the focused
owners for PDB-local and semantic-IL evidence respectively.

### P29: static argument zero named `this`

```bash
inspect_member \
  ILInspector.Decompiler.Tests.StructStaticThisParameter \
  BoxVirtual \
  "$CFG"
```

Current gap: metadata and the declaration preserve the ordinary parameter as
`@this`, but the body prints `(this).ToString()`. A static method has no
implicit receiver, so that spelling produces CS0026. Issue #3260 owns complete
value, receiver, ref, and cast coverage; fixing only this one displayed
occurrence would leave the shared-printer rule inconsistent.

### P30: unrepresentable parameter on a bodyless member

```bash
dotnet run --project src/ILInspector.Decompiler.Tests -c Release --no-build -- \
  -method '*BodylessUnrepresentableParameter*'
```

The synthetic metadata fixture defines abstract methods whose exact Param-row
identities are `bad-name` and `arg0`, the latter colliding with its method
generic parameter. These members have no `IrFunction`, so current type
composition emits `int Echo(int bad-name)` and
`int GenericEcho<arg0>(int arg0)` without a body-fidelity cause. Issue #5663
owns the CSharp declaration-layer typed refusal.

## Change discipline

Any change that moves a scenario between classifications must:

1. Identify the exact new evidence and its owner.
2. Carry a typed binding through the IR rather than infer identity from display
   text.
3. Add a positive fixture, a close negative, and a no-symbol or malformed-symbol
   case when the evidence is optional.
4. Preserve honest fallback behavior when the raise or evidence check declines.
5. Update the scenario's probe, regression gate, and tracking issue in this
   document.

Readable renaming alone moves only synthesized presentation. A scenario becomes
preserved only when the artifact evidence identifies both the name and the
symbol to which each emitted use binds.
