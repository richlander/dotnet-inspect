# Decompiler name and symbol preservation

This document is the Decompiler-owned contract for identifier and symbol names
in reconstructed C#. It answers four questions:

1. Which names the current decompiler preserves.
2. Which surviving names it should preserve but currently does not.
3. Which names it synthesizes only for readable presentation.
4. Which source spellings the compiled artifact cannot establish.

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
| **Irrecoverable** | The artifact does not retain enough evidence to identify the authored name or bind that name to the reconstructed use. The Decompiler must not guess. |

"Authoritative artifact fact" means evidence in the inspected PE, metadata, or
matching Portable PDB, not the fixture source file known to a test author.
Obfuscation or post-compilation rewriting changes the artifact's identity. The
Decompiler can preserve the resulting metadata name; it cannot establish the
name that existed before the rewrite.

## Evidence model

Evidence is adopted in this order:

| Evidence | What it can authorize | What it cannot authorize |
| -------- | --------------------- | ------------------------ |
| ECMA-335 metadata | Declared namespace, type, member, parameter, and generic-parameter identities; field and property identities used by supported generated forms | Pre-obfuscation names, local-variable names, labels, aliases used at a particular source occurrence |
| Matching Portable PDB | A `LocalVariable` name bound to an exact IL local slot and its `LocalScope`; other typed debug records once a Decompiler consumer exists | Names for values that never occupy an IL local; source syntax or use-site binding not represented by a debug record |
| Authenticated compiler-generated metadata | Source identifiers embedded in a recognized lowering, such as successful lambda/local-function raises and iterator state-machine fields | Decoding an unauthenticated lookalike, or claiming a source name after the relevant raise declines |
| Decompiler analysis | Collision-free, readable presentation names derived from typed IR roles | Authored identity |
| Checksum-verified source content | Exact text for the separate `PDB Source` view | Permission to inject source-only declarations into reconstructed `Decompiled Source` |

Structured symbol identity and rendered spelling remain separate. A `TypeRef`,
method token, or parameter ordinal identifies a symbol even when the printer
must qualify, escape, sanitize, or decline its preferred short spelling. The
printer must prefer valid, correctly bound C# over a familiar-looking name that
would bind to another symbol.

### Portable PDB boundary

The current Decompiler PDB consumer reads `LocalVariable` names and
`LocalScope` ranges. It does not consume every table or custom debug record in a
Portable PDB. A matching PDB therefore improves local naming only where the
compiler emitted a slot-backed local and named that slot.

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

Several current paths produce useful names without yet satisfying this
authentication boundary:

- Anonymous-object import recognizes the same-assembly
  `<>f__AnonymousType...` name and reads its ordered property rows, but does not
  yet require `CompilerGeneratedAttribute`. The current positive result is
  preserved metadata spelling under a weaker admission rule;
  [#5585](https://github.com/richlander/dotnet-inspect/issues/5585) tracks the
  missing lookalike guard.
- The fixture-shaped classic-async pass recognizes generated field names and
  kickoff/`MoveNext` shapes rather than consuming the Metadata-issued
  relationship requested by
  [#5277](https://github.com/richlander/dotnet-inspect/issues/5277) under
  [#4472](https://github.com/richlander/dotnet-inspect/issues/4472). Its
  current `alpha` spelling is an unauthenticated heuristic, not **Preserved**
  identity. The relationship and hoisted field retain enough evidence to make
  authenticated recovery a **Recoverable gap**.
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
| Metadata declarations | Preserve artifact namespace, type, field, property, event, method, parameter, and generic-parameter names. Escape C# keywords without changing identifier identity. | P1 | `KeywordIdentifierTests.KeywordParameter_IsEscaped`; the generic-name portion has a manual probe but no focused automated gate. |
| PDB local variables | Prefer a usable Portable PDB local name associated with the exact IL slot over every synthesized fallback. | P2 | `IrImporterTests.LocalNames_RecoveredFromPdb_RenderSourceNamesNotVSlots` |
| Lambda parameters and captures | When an authenticated lambda raise succeeds, preserve generated-method parameter names and substitute authenticated captured-field names back to their source identifiers. | P3 | `LambdaRaisingPassTests.NonCapturingExpressionBody_RaisesSimpleLambda` and `CapturingExpressionBody_SubstitutesCaptureAndRaisesLambda` |
| Local-function declarations and parameters | When an authenticated local-function raise succeeds, recover the source function name and generated-method parameter names, and bind calls to the raised declaration. | P4 | `LocalFunctionRaisingPassTests.StaticLocalFunction_RecoveredAsDeclarationAndUnqualifiedCall` |
| Anonymous-object properties | Preserve property metadata names when the current same-assembly anonymous-type shape raises, and bind initializer values to those names. The current admission is name-pattern-based and has the #5585 authentication gap disclosed above. | P5 | `AnonymousObjectPassTests` gates the positive output, not generated-type authentication. |
| Tuple elements in signatures | Preserve `TupleElementNamesAttribute` names on supported return and parameter signatures. | P6 | `TupleTypeViewTests` and `CSharpDeclarationWriterTests`; the whole-member composition is also manually probed. |
| Iterator hoisted locals | Recover a source local name from authenticated iterator state-machine evidence when reconstruction owns the corresponding field and use. | P7 | `IteratorReconstructionPassTests.CountingLoopIterator_RendersLoopAndYield` |
| Classic async local names | The current fixture-shaped pass heuristically decodes `alpha` from the hoisted field name without the required authentication; this is not **Preserved** identity. It preserves `beta` from the matching PDB local. Without symbols, current output still decodes `alpha` but falls back to `V_1` for `beta`. | P8 | `ClassicAsyncReconstructionHonestyTests.SequentialAwaitLocalNameComesFromSymbols` is the positive PDB-local gate. Both the no-symbol result and `alpha` authentication boundary are manually probed; #5587 tracks correcting the vacuous negative test, while #5277/#4472 track authenticated classic-async identity. |

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
| Declined capturing local functions | The generated local-function method embeds `AddSquare`; the display-class field embeds captured `n`. | Generated support identifiers such as `___c__DisplayClass...` and `__CapturingLocalFunctionWithLocal_g__AddSquare...` remain. | Raise the supported environment and function, or retain an honest valid fallback without losing the available source identity when binding can be proved. | [#3129](https://github.com/richlander/dotnet-inspect/issues/3129), P11 |
| Same-named local functions in disjoint source scopes | Each authenticated generated method embeds `Pick`; local-function ordinals and distinct MethodDefs distinguish the two definitions. | Both calls retain generated fallback names because declarations are flattened into one scope. | Recover each declaration into its own source scope while preserving each call's binding. | [#3878](https://github.com/richlander/dotnet-inspect/issues/3878), P12 |
| Tuple element names on locals | `TupleElementNames` custom debug information is attached to each exact Portable PDB `LocalVariable`. | Local variable names survive, but types render as `ValueTuple<int, int>` and uses as `Item1`/`Item2`. | Carry the names with the local type and use `(int Sum, int Product)` plus `.Sum`/`.Product` when structurally valid. | [#5578](https://github.com/richlander/dotnet-inspect/issues/5578), P13 |
| Classic-async hoisted local names | The Metadata-issued kickoff/state-machine relationship and generated field `<alpha>5__2` can authenticate `alpha`. | Current compiler-fixture output spells `alpha`, but derives it from a fixture-shaped name/IL heuristic rather than the typed relationship. | Consume the authenticated relationship before decoding and binding the hoisted field name. | [#5277](https://github.com/richlander/dotnet-inspect/issues/5277) under [#4472](https://github.com/richlander/dotnet-inspect/issues/4472), P8 |
| Full C# identifier grammar on PDB locals | Exact slot-bound names include the keyword `class` and combining-mark identifier `A\u0301`; C# can losslessly spell them as `@class` and `A\u0301`. | PDB-name admission rejects keywords before escaping and accepts a narrower character grammar, so output synthesizes replacements. | Admit compiler-supported identifier identities, reserve the underlying identity for collisions, and apply position-appropriate escaping. | [#5586](https://github.com/richlander/dotnet-inspect/issues/5586), P14 |
| Generated backing-field and primary-constructor names | Auto-property fields have matching property rows; primary-constructor captures can be bound to exact constructor parameters and compiler-generated owner evidence. | Current output decodes `<Property>k__BackingField` from grammar plus the property row and `<parameter>P` from grammar alone. | Authenticate each generated field and its exact source-symbol binding before decoding. | [#5595](https://github.com/richlander/dotnet-inspect/issues/5595), P22 |

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

The combining-mark half of P14 and both P22 generated-field paths are manual
fixture probes. Their issues own the missing focused positive and lookalike
gates.

Those tests are safety rails, not declarations that the gaps are complete.

## Synthesized presentation names

When no usable source name survives, the Decompiler has two honest
presentations:

| Consumer | Default | Contract | Probe and gate |
| -------- | ------- | -------- | -------------- |
| Library and deterministic harness | Stable `V_index` local names | Keep artifact-independent output stable for fidelity and corpus comparison. | P9; `IrImporterTests.OpenWithoutSymbols_IgnoresPdb_RendersVSlotsNotSourceNames` |
| User-facing product source views | Readable names such as `num`, `num2`, or type/role-derived names | Derive only from typed IR evidence, avoid collisions, and never claim source identity. Fall back to `V_index` when evidence is insufficient. | P10; `ReadableLocalNamesTests`, `StyleOptionCatalogTests.ProductDefaults_EnableReadableNames_WithoutChangingLibraryDefaults`, and `ByteNeutralityGateTests` |

The complete synthesis policy is owned by
[Readable local names](readable-local-names.md). Improving an `S_N`, `V_N`, or
awkward synthesized name is presentation work, not source-name recovery.
[#3165](https://github.com/richlander/dotnet-inspect/issues/3165) tracks
real-world readability improvements. P15 is the fixture boundary: `y` existed
in source, but the Release artifact retained only the value flow, so any
replacement for `S_256` would still be synthesized. P15 is a manual fixture
probe; no focused automated gate currently asserts that `y` is absent.

## Names that reconstructed source cannot promise

The following cases lack enough artifact evidence for an exact authored name or
binding. Each row has a fixture probe so the boundary can be rechecked as the
compiler and Decompiler evolve.

| Scenario | Why exact preservation is unavailable | Required behavior | Probe |
| -------- | ------------------------------------- | ----------------- | ----- |
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
```

Expected name observations:

```csharp
public static int KeywordParam(int @delegate)
public static bool GreaterAsByte<T>(T left, T right)
```

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
```

Expected:

```csharp
public static (int Sum, int Product) TuplePair(int a, int b)
```

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
```

Expected with symbols: current output declares and uses both `alpha` and
`beta`. `beta` is preserved from the PDB; `alpha` is the unauthenticated
classic-async heuristic tracked by #5277/#4472, not **Preserved** identity.
Without symbols, current output still uses `alpha` but falls back to `V_1` for
`beta`. The no-symbol result is a manual probe; #5587 tracks its focused gate.

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
probe_dir=$(mktemp -d /tmp/dotnet-inspect-name-probe.XXXXXX)
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

Expected boundary: classic async currently decodes `alpha` from the hoisted
field name without the required authentication and preserves `beta` from the
PDB local. Runtime async preserves the same PDB-backed `beta` but currently
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
