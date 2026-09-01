# Custom-Attribute Value Decoding

`dotnet-inspect` reads custom-attribute values out of assemblies it did not
produce. A custom-attribute value is a byte blob whose meaning is not
self-describing: the blob carries values, and the *constructor signature* — a
separate blob, in a separate table — says how wide each value is. Decoding one
requires reading two attacker-supplied structures together and agreeing about
where every element begins and ends.

This document owns the contract that makes that safe.

**Status: descriptive, with known gaps.** The invariants below are the contract
this component is held to, not a description of what it currently guarantees.
Seven verified divergences are open against it, listed under [Known
gaps](#known-gaps). Treat any statement that an invariant *holds* as unverified
until the differential oracle of issue #5065 exists.

The same caution applies to this document's **detail-level claims** — the
mechanism tables, cited line numbers, and failure-semantics rows. They were
established by reading, and adversarial review has twice corrected rows that
reading got wrong. They are checked by no gate. #5065 is the instrument that
would verify them mechanically; until it exists, verify against the code
before relying on any specific row.

## Responsibility

This design owns the safety contract for decoding custom-attribute
values from untrusted metadata: what `CustomAttributeValueGuard` promises,
what `AttributeDecoder` may assume from it, how enum widths are resolved, and
what must remain true of the two together.

The implementation lives in `src/ILInspector.MetadataPrimitives/`:
`CustomAttributeValueGuard.cs`, `AttributeDecoder.cs`, and
`EnumUnderlyingPrimitive.cs`.

## Boundaries

- **Consumes** an owner-issued `MetadataReader` and `CustomAttribute` value.
  Acquiring that value from a handle belongs upstream.
  Acquisition, image lifetime, and provenance belong to their owners.
- **Consumes** `SignatureBlobGuard` for structural signature bounds. This design
  does not redefine that component's depth policy or failure semantics.
- **Produces** either a decoded `CustomAttributeValue<string>` or a null result
  meaning *not decoded*. It never produces a partially decoded value.
- **Defers** work-charging policy to the caller's observer. This design says
  what is charged, not what a budget does with it.

## Non-claims

- Not a general signature-decoding contract. Method, field, and property
  signature decoding are owned elsewhere.
- Not a defense against local actors, our own code, or other contributors. See
  [Trust boundaries](untrusted-data-threat-model.md#trust-boundaries).
- Not a promise that attribute values are *correct*, only that reading them
  cannot be turned into an unbounded or out-of-bounds operation.
- Does not own the `SZARRAY` element-replay redesign tracked in issue #5047.
  This document states the invariants that redesign must preserve.

## The trust boundary

| Element | Value |
| --- | --- |
| **Actor** | Whoever authored a package, symbol, or assembly the user inspects. Publishing to a public feed is enough; no local access is needed. |
| **Input path** | `CustomAttribute.Value` (the value blob) together with the constructor signature blob reached through the attribute's constructor handle, plus the `TypeDef`/`TypeRef` rows those signatures name. |
| **Affected boundary** | `AttributeDecoder`, and every caller that renders, indexes, counts, or searches attribute values. |
| **Containment invariants** | Three, stated below. |
| **Enforcement gate** | Stated below. |

The tool never executes inspected code, so the realistic damage is not code
execution but resource exhaustion and misread memory: a small download that
costs disproportionate CPU or memory, or a decode that reads a length from the
wrong offset.

### Prior art: there is no upstream bound to inherit

Every mainstream .NET consumer of custom-attribute blobs allocates on the
attacker-declared count before reading a single element.

| Consumer | Decoder | Bounds the declared count first? |
| --- | --- | --- |
| `System.Reflection.Metadata` | own | No — `ImmutableArray.CreateBuilder<CustomAttributeTypedArgument<T>>(count)` |
| Roslyn `MetadataDecoder` | own, not SRM | No — `new TypedConstant[count]` |
| ILSpy, ILCompiler (Native AOT) | SRM `DecodeValue` | No; inherited from SRM |
| ILLink | Mono.Cecil | No — `new CustomAttributeArgument[uint32]` |
| CoreCLR `ParseCaValue` | native | Effectively yes — appends into a dynamic `SArray`, never sized upfront |

The prevailing model is "decode and throw; the caller catches
`BadImageFormatException`." That model is correct for a compiler or an
interactive decompiler, whose inputs the developer chose to reference. It is not
correct here, where the assembly author is the adversary and the tool inspects
whatever a public feed serves.

`BlobReader.RemainingBytes` is public and would answer the question, but no
decode path consults it before sizing an allocation. So the pre-walk in this
component is not redundant with an upstream check; there is no upstream check.

## The containment invariants

Three properties must hold together. They are independent: any one can break
while the others hold, and each has produced a real defect.

They are stated as **targets**. Each is normative — a violation is a defect to
fix, not a behavior to document — but none is currently established by a gate,
and two have open violations.

> **I1 — Alignment.** For any blob the guard reports safe, on every path where
> SRM's `CustomAttributeDecoder` continues decoding, the guard must have
> skipped exactly the bytes SRM consumes.
>
> **I2 — Bounding the decoder.** Before SRM runs, the guard must have refused
> every attacker-declared quantity that drives SRM's *cost* — both what it
> **allocates** (`SZArray` element counts, named-argument counts) and what it
> **spends** (nesting depth, and repeated work SRM performs per fixed
> argument). This holds regardless of whether the two walkers agree about where
> those quantities live.
>
> **I3 — Bounding ourselves.** The guard's own total cost must stay near-linear
> in the size of the metadata it is given, across *every* attacker-controlled
> cardinality together — declared element counts, declared parameter counts,
> distinct handles and names, and the size of the tables a resolution scans.
> Bounding each dimension separately is not sufficient, because the attacker
> chooses them jointly.

I1 is about *agreement*; I2 is about *what the decode costs*; I3 is about *what
asking costs*.

**I1's surface is argument type classification, not only enum width.** The
width question — how many bytes an enum-typed argument occupies — is the most
frequent way the walkers disagree, but it is not the boundary of the invariant.
Deciding *whether* an argument is an enum at all is part of the same agreement,
because SRM reaches `GetUnderlyingEnumType` only after
`ICustomAttributeTypeProvider.IsSystemType` returns `false`. A `System.Type`
argument misclassified as an enum is read as four bytes instead of a
length-prefixed `SerString`, and every subsequent field is read from the wrong
offset — the identical failure mode as a wrong width, reached through a
different decision.

dotnet/runtime#57531 is that case in the wild, and it is worth stating in
concrete terms because it bounds how much the classification decision is worth.
In `Kentico.Content.Web.Mvc.dll`, misclassifying the `System.Type` argument of
`RegisterPageBuilderLocalizationResourceAttribute` consumes the `SerString`
length byte and the first three characters of the type name, so the following
`string[]` element count is read from the middle of that name — `"tico"`,
`0x6F636974`, 1,868,786,036 declared slots. At
`CustomAttributeValueGuard.DeclaredSlotCharge` that is 28,515 MiB, which is the
28,517 MiB the reporter observed. The blob is entirely legal; only the
classification is wrong.

`CustomAttributeValueGuardTests`'s
`SystemTypeArgumentReadAsEnum_ChargesTheAmplifiedCount_AndIsUnsafe` is the gate
for the refusal, paired with
`SystemTypeArgument_FromShippedAttribute_DecodesAndStaysBounded` for the
fidelity half. Both run over the captured 80-byte blob and differ only in the
first parameter's declared type; neither materializes the amplified array,
because the charge is asserted through `beforeMaterialize` and `DecodeValue` is
never called on the misclassified image.

| Invariant | Holds today? | Basis |
| --- | --- | --- |
| **I1 — Alignment** | Believed to hold on the resolver-supplied path; unverified | Pinned by example only. One example is now a captured real-world artifact: see the classification pair above. The resolver-less overload is explicitly out of scope; see [Known gaps](#known-gaps). |
| **I2 — Bounding the decoder** | **No.** Violated by #5098 | SRM's per-argument re-derivation of the generic context is not bounded by anything the guard checks. |
| **I3 — Bounding ourselves** | **No.** Violated by #5091, #5047, #5130, and #5132 | Four independent amplifications on our own side, spanning one walk and the cross-row loop. |

**I2 and I3 are the same question asked of two different parties, and the guard
is not the expensive one by default.** It is tempting to bound only our own
work, since that is the code we own. But the guard exists to make SRM's decode
safe, so an input that is cheap for the guard and quadratic for SRM is a
failure of this component even though every line of the guard ran in linear
time. Worse, the guard's own optimizations can *hide* such a case: memoizing a
lookup that SRM repeats makes the guard fast and leaves the decoder slow, and
nothing in the guard's own profile shows it.

I2 is the reason this component exists at all. SRM reads a declared `Int32`
`SZArray` count or `UInt16` named-argument count and allocates an
`ImmutableArray` builder from it immediately — before reading any element, and
before any further callback through which that count could be observed or
charged. Four attacker-chosen bytes can therefore request a gigabyte-scale
builder, and the blob-length charge on the value heap never sees that
amplification. No amount of cursor agreement prevents this.

**I2 covers SRM's time as well as its memory, and the time case is the one this
guard is most likely to miss.** SRM re-derives each fixed argument's type from
the constructor's generic context independently: it passes its context reader
*by value* per argument and re-skips the preceding generic arguments each time.
A constructor with `P` fixed arguments all spelled `VAR (G-1)`, against an
instantiation carrying `G` generic arguments, therefore costs SRM `Θ(P × G)` on
metadata of size `Θ(P + G)`.

The guard does not experience that cost, because it memoizes the located
argument offset and reuses it. Guard work is `Θ(P + G)`, I1 holds — both sides
select the same type — I3 holds, and the blob is approved. This is precisely
the case where an optimization on our side conceals an amplification on theirs.
Tracked as issue #5098.

I3 exists because a guard that refuses an attack expensively has not prevented
it. The guard walks each declared element, so any work placed on the
per-element path is multiplied by an attacker-chosen count *before* the refusal
that count would eventually trigger.

**I3 is deliberately stated over aggregate cost rather than per-element
repetition.** "Perform this work once per distinct input" is the right *fix*
for the four amplifications found so far, but it is the wrong *invariant*,
because it is satisfied by a walk that is still quadratic. A constructor
declaring `P` fixed arguments, each naming a distinct unresolvable `TypeRef`,
resolved against an image holding `T` type definitions, costs `Θ(P × T)` on
metadata of size `Θ(P + T)` — because `EnumUnderlyingPrimitive.TryFindDefinition`
scans every definition, and each comparison is itself a recursive structural
match. Every handle is resolved exactly once. No count is repeated. I1 and I2
both hold. The signature-node cap bounds `P`, but two capped dimensions
multiplied are still billions of comparisons. Tracked as issue #5091.

**Charging is not an escape hatch for I3.** The `beforeMaterialize` observer is
optional and is frequently absent, and `Charge` returns immediately when it is
null. Work that is "charged" to nobody is unbounded work. A cost may be
delegated to an observer only where a caller is guaranteed to be present and
its refusal is guaranteed to stop the walk — which, per issue #5085, is not
currently true either.

Defects therefore fall into four categories:

- **Fidelity.** Both walkers use the same wrong width. Output is wrong; nothing
  is unsafe. They still agree where every element ends, so the guard's bounds
  still bound the decode.
- **Divergence (I1).** The walkers use *different* widths for the same value.
  From the first divergent element onward the guard validates different bytes
  than the decoder reads. An array length the guard never saw becomes a length
  the decoder acts on — and the guard already said the blob was safe, so
  nothing downstream is looking.
- **Aligned but unbounded (I2).** The walkers agree perfectly and the blob is
  still an amplification. Delete the remaining-byte count check and both sides
  still locate the same count at the same offset; the guard reports `Truncated`
  — which becomes `true` — and SRM allocates from that count before reading
  elements. Agreement is preserved and the attack succeeds.
- **Aligned, bounded, and expensive (I3).** The guard refuses correctly and
  cheaply *for SRM*, having already spent attacker-multiplied effort reaching
  that refusal. Nothing about the cursor, the declared counts, or SRM's
  behavior records that the guard did the work.

The third and fourth categories are why I1 alone is not the contract. A gate
that checks only offset agreement passes the unbounded case.

Both divergence and unboundedness are fail-opens, and both fail open precisely
*because* the guard succeeded. That is why either is treated as severe even
when the visible symptom is only a wrong number in output.

### I1 is conditional, and the condition is load-bearing

I1 is scoped to paths where SRM continues decoding, because the guard
deliberately accepts blobs SRM will reject. A generic constructor header is the
clearest case: the guard consumes the arity and keeps walking, while SRM
rejects `header.IsGeneric` right after the value prolog and consumes nothing
further.

That is not a violation. The guard's job on such a blob is to hand it to a
decoder that fails closed and catchably, not to predict the failure. Stating
I1 unconditionally would make every such intentional acceptance look like a
defect and would make the enforcement gate report false failures.

**`TypeSpec`-spelled enums are the same shape, with a cost attached.** A
`CLASS`/`VALUETYPE` element type carries a `TypeDefOrRefOrSpec` coded index,
which can name a `TypeSpecification`. The guard accepts whatever handle it
reads. Structural enum resolution recognizes only definitions and references,
so a `TypeSpec` falls to the name path, where `TypeResolver` *decodes the
specification signature* to render a name. SRM does the opposite: it admits
only `TypeDefinition` and `TypeReference` to the provider and throws
`BadImageFormatException` for every other handle kind.

So for this spelling the guard performs real decoding work that SRM never
performs, and SRM rejects before the provider is consulted. I1 is satisfied
vacuously — SRM does not continue — but the guard has done unbounded work on a
blob that was never going to decode. Alternating two distinct `TypeSpec`
handles across `P` parameters defeats the single-slot handle memo and re-decodes
a large shared specification blob each time, which is an I3 case reached
entirely through a spelling the grammar did not previously generate.

The lesson generalizes: **every handle kind the guard accepts but SRM refuses is
simultaneously an I1 exemption and an I3 exposure.** Enumerate them
deliberately rather than discovering them.

### Why agreement is hard here

The second walker is not ours. `System.Reflection.Metadata` owns the decode; we
own only the guard. We cannot change SRM's parse, we cannot read its cursor,
and it can change between runtime versions. Agreement has to be re-established
by construction rather than assumed.

Two consequences follow, and both are load-bearing:

1. **Share the decision, do not re-implement it.** Wherever a width decision
   depends on resolving a name or a handle, the two walkers must reach it
   through the same handle and the same resolution function, or through the
   same provider instance — never through two implementations believed to be
   equivalent. The next section states which mechanism applies where, because
   they are not the same on both paths.
2. **Fail open to SRM only where we genuinely cannot judge.** Where the guard
   *runs out of bytes*, or a parser exception reaches the public boundary, it
   hands the blob to SRM rather than inventing a judgment, because SRM's own
   failure is catchable and ours would be a guess. This does **not** extend to
   forms the guard positively identifies as unsupported: an unrecognized
   element code or an unsupported serialized form is refused, not deferred.
   Widening the rule past that distinction would convert a deliberate refusal
   into approval.

#### Width agreement is a resource property, not only a fidelity one

This is the documented root cause of `dotnet/runtime#57531`, filed against SRM
in August 2021 by a NuGet engineer scanning packages on nuget.org — the same
tool category as this one. A shipping package on the feed drove the reporter's
scanner to a 28.5 GB allocation:

```text
Reading attribute 'RegisterPageBuilderLocalizationResourceAttribute'... found bad image format!
Memory is at 28517.114097595215 MB
BIG MEMORY!
```

The reporter's provider resolved every enum as `Int32`:

```csharp
public PrimitiveTypeCode GetUnderlyingEnumType(object type) => PrimitiveTypeCode.Int32;
```

The issue was closed on exactly that basis: an incorrect `GetUnderlyingEnumType`
makes the decoder consume the wrong number of bytes, the cursor drifts, and a
later field is then read as an array count. The pre-allocation itself was never
changed, and the current source still has no length check.

Two things follow. A width disagreement does not merely produce a wrong value —
it relocates every subsequent read, so it can convert a valid blob into a
multi-gigabyte allocation request. And the published position of the decoder's
owner is that width agreement is a **caller obligation**. I1 is therefore
load-bearing for I2, and the asymmetry recorded as Gap 5 is a bound defect
rather than a fidelity nicety.

Roslyn, which maintains its own decoder rather than calling SRM, resolves the
same two width paths — the value-side serialized name and the signature-side
type — through a single symbol-model lookup, and treats an unresolvable
underlying type as a hard failure (`throw new UnsupportedSignatureContent()`)
rather than defaulting to `Int32`. That is the same discipline stated as
consequence 1 above, reached independently by the other production decoder.

## Known gaps

Each row is a **verified** divergence between the contract above and the
component's current behavior. They are listed rather than omitted, because a
design document describing only intended behavior would misrepresent a
component with seven open violations.

| # | Gap | Invariant | Issue |
| --- | --- | --- | --- |
| 1 | SRM re-derives each fixed argument's type from the generic context independently, costing `Θ(P × G)`. The guard memoizes the offset and never experiences it, so it approves. | I2 | #5098 |
| 2 | A failed resolution scans every type definition, so `P` distinct unresolvable arguments cost `Θ(P × T)`. This applies to **both** the handle path and the resolver-less serialized-name path (`TryFindDefinition`). | I3 | #5091 |
| 3 | `SZARRAY` element types are re-parsed once per element rather than once per array. | I3 | #5047 |
| 4 | An observer exception is caught by the malformed-metadata handler, turning a caller's budget stop into an approval — and, through `TypeResolver`, absorbing it silently into a default width. | Failure semantics | #5085 |
| 5 | The resolver-less `IsSafeToDecode` overload resolves widths through a different order, so its `true` does not carry I1 for a caller decoding with a resolver-backed provider. | I1 scope | #5120 |
| 6 | Every memo in the guard is a **single slot keyed on the previous input**, so alternating two values defeats all four — including a guard-side `Θ(P × G)` that mirrors gap 1. | I3 | #5130 |
| 7 | `A` attribute rows sharing one `B`-byte blob are guarded and decoded independently, costing `Θ(A × B)` in work and retained values from `Θ(A + B)` of metadata. Absent a shared `MaterializationContext`, each `TryDecode` also builds a fresh provider and rebuilds the type-definition index, adding `Θ(A × T)`. | I3 | #5132 |

Gaps 1, 2, 3, and 6 share a root cause worth naming: **the guard and SRM
memoize different things.** Where the guard caches work SRM repeats, the guard is fast
and the decode is quadratic (gap 1). Where the guard repeats work it could
cache, the guard is quadratic and the decode is fine (gaps 2 and 3). Neither
side's profile reveals the other's cost, which is why all four were found by
reading rather than by measurement. Evaluate any fix against **both** walkers.

Gap 5 is an **API-shape hazard rather than a live defect**: `AttributeDecoder`
is the only production caller and always supplies the resolver. It is recorded
because the surface permits the unsafe composition and nothing prevents it. I1
is therefore scoped to the resolver-supplied overload throughout this document;
see [Resolution order](#resolution-order-and-what-int32-actually-means).

Gaps 1, 2, 4, 5, 6, and 7 were all found while writing or reviewing this document,
against a component that had already been through eight rounds of
defect-driven review. That is the argument for the oracle below: reading finds
these one at a time, and only after somebody thinks to look.

## Enforcement gate

**Current state: `unverified`.** All three invariants are currently supported
only by `tests/ILInspector.Metadata.Tests/CustomAttributeValueGuardTests.cs`,
which pins individual hostile shapes by example. That is a regression suite,
not a gate: it proves the shapes somebody already thought of, and every
divergence found so far was found by a reviewer imagining a new one.

**Required gate:** a differential oracle, tracked as issue #5065, that
generates inputs over the grammar below, runs both walkers, and asserts I1, I2,
and I3 separately, across both the metadata axes and the observer axis. A gate
that asserts only offset agreement passes both the unbounded and the expensive
attack; a gate defined only over generated metadata cannot see gap 4 at all.

**This specification is itself partial.** Six of the seven known gaps were
found by reading rather than by any gate, and five of them were found after
this document's first draft — including two found by reviewing this very
section. That is evidence the enumeration below is incomplete rather than
evidence it is done. Treat it as the starting corpus for the oracle, not as a
closed description of the input space.

### Generated grammar

The generator must cover, and must be able to combine:

- **Fixed-argument types:** each primitive; `string`; `object` (boxed);
  `System.Type` as a serialized name; `SZARRAY` of each of these; and `VAR`
  where generic substitution applies. **`MVAR` is a refusal case, not a
  substitution case** — `ProcessGenericParameter` returns `Unsafe` for a method
  generic parameter before locating anything, and SRM handles only type
  parameters, so `MVAR` must be generated as a negative case and must not
  appear in the alternating-index cost cases below.
- **Enum spellings:** the handle forms and the serialized (`0x55`) name form,
  each independently, because they resolve differently. The handle forms must
  be generated with **both** `ELEMENT_TYPE_VALUETYPE` and
  `ELEMENT_TYPE_CLASS`, each with a `TypeDef`, a `TypeRef`, a **`TypeSpec`**,
  and a **nil** handle. The guard routes both spellings through the same path
  and accepts every handle kind, while SRM admits only `TypeDef` and `TypeRef`;
  metadata is untrusted, so the gate cannot assume an enum is spelled legally.
- **Nesting:** boxed and `SZARRAY` nesting at, just below, and just above
  `MaxSerializedDepth`.
- **Custom-modifier chains** preceding element types, at and above the
  signature depth bound.
- **Named arguments:** fields and properties, including the serialized-enum and
  array-typed forms.
- **Declared counts:** valid, zero, negative, `-1`, and counts far exceeding
  the remaining bytes.
- **Malformed extensions:** truncation at every structural offset; unknown
  element-type codes; a generic constructor header.
- **Metadata states:** distinct `TypeDef` rows that render to one display name;
  a `TypeRef` whose flattened spelling collides with a local definition;
  unresolvable `TypeRef`s; nested-type chains deeper than the match bound.

### Observing SRM's consumption

SRM does not expose its cursor, so the gate must establish consumption from
observable consequences rather than by reading an offset. Three observables
are available together, and the design requires all three:

1. **Provider call sequence.** SRM calls the supplied
   `ICustomAttributeTypeProvider` for each type it resolves. Recording the
   ordered calls yields the decoder's parse path, which the guard's own walk
   can be compared against directly.
2. **Decoded values.** The fixed and named argument values SRM returns say
   which bytes it interpreted and as what width.
3. **Boundary discrimination — and its limit.** A single sentinel appended at
   the offset the guard skipped to is **not sufficient**, because it only
   detects the case where SRM consumes *farther* than the guard. Neither the
   guard nor SRM rejects trailing bytes, so when the guard skips farther than
   SRM, the sentinel lies in data SRM never reads: the decode succeeds with
   exactly the provider calls and values the aligned case would produce.

**These three observables cannot establish I1 by themselves, and no arrangement
of them can.** They are all observations of *SRM*, and SRM's behavior does not
depend on where the guard landed. Making a boundary "consequential" — requiring
a following argument that decodes only at the correct offset — constrains the
decoder, not the guard: if SRM is correct it decodes correctly, and a guard that
over-consumed simply runs out of bytes, returns `Result.Truncated`, and is
mapped to `true` by `Check(...) != Result.Unsafe`. Provider calls, decoded
values, and any sentinel all match the aligned case exactly.

So guard-over-consumption is invisible to every SRM-side signal. The asymmetry
is structural: SRM is the oracle, and an oracle cannot report where its
*counterpart* stopped reading.

**The gate therefore requires the guard's own final offset to be observable to
the test.** That is a testability requirement on this component, not a property
of the generated input — an internal or test-visible boundary trace that the
oracle compares against SRM's reconstructed consumption. Without it, I1 can be
established in one direction only, and the direction it misses is the one where
the guard has already said `true`.

I2 is checked separately and does not depend on these, but it needs **two**
assertions, because I2 covers both what SRM allocates and what it spends:

- **Allocation.** A blob whose declared quantities exceed the remaining bytes
  is refused *before* `DecodeValue` is invoked at all. This is a direct
  property of the generated input and the guard's return value.
- **Time.** A blob that costs SRM asymptotically more work than its own size is
  refused. This one is **not** observable from the three signals above — gap 1
  uses entirely valid counts and lawful bytes, and SRM's per-argument
  re-derivation of the generic context happens inside `SkipType` without any
  provider callback, so it produces no observable trace at all.

The time half therefore needs a **deterministic policy oracle**, not an
observation: a model of the work SRM would perform for the generated shape,
against which the guard's refusal is asserted. It is the only invariant of the
three that must be gated against a *model* of the decoder rather than against
the decoder itself, because the cost it bounds is invisible from outside.

**This oracle is not yet specified, and the sketch an earlier revision gave was
not implementable.** That sketch made cost a function of parameter count,
generic arity, and the `VAR` index sequence. Those are insufficient. `SkipType`
re-derivation cost depends on the complete *shapes* of the arguments preceding
the one being skipped — pointers, arrays, function pointers, custom modifiers,
and nested generic instantiations all recurse or loop
(`CustomAttributeDecoder.cs:422-505`). Two blobs with identical counts, arity,
and index sequences can differ in cost by orders of magnitude. Neither a unit
of work nor a refusal threshold is defined anywhere in this document.

Specifying that oracle is open work, tracked with the gate itself under #5065.

Specifying I2 as "refuses over-large declared counts" alone, as an earlier
draft did, is exactly the check that gap 1 passes.

I3 is checked separately as well, and cannot be observed from SRM at all. It is
also the hardest of the three to gate, because a one-dimensional check is
misleading: raising a single declared count while holding everything else fixed
is exactly the measurement that the existing per-element memoization already
passes, and it is blind to the quadratic described above.

The gate must therefore vary the attacker-controlled dimensions **jointly**, at
minimum:

- declared `SZArray` element counts,
- declared constructor parameter counts,
- **the constructor's generic arity**, which multiplies against parameter count
  on both sides — `Θ(P × G)` for SRM per gap 1, and for the guard too under the
  next item,
- **the *sequence* of `VAR` indices, and of handles and names, not merely how
  many appear.** Every memo in the guard is a single slot keyed on the
  *previous* input, so a run of identical values is cheap while two values in
  alternation evict on every step. For argument location that restarts the skip
  from argument zero, giving `Θ(P × G)` guard-side; the same shape defeats the
  enum-name, enum-handle, and `System.Type` memos. Only the repeating shape is
  currently tested, which is why gap 6 (#5130) passes all four existing
  amplification regressions.
- the number of *distinct* handles and names referenced,
- the number of rows in the tables a failed resolution scans,
- the number of attributes decoded from one image, and
- **the size of a blob shared across many attribute rows.** `A` attribute rows
  may name one `B`-byte value blob, which the blob heap stores once; each row
  is nevertheless guarded and decoded independently, so `Θ(A + B)` of metadata
  yields `Θ(A × B)` of work and retained value.

A dimension list is not enough on its own: several of these are only
adversarial in combination with a particular *arrangement*, not at a particular
*size*. The generator must therefore control shape as well as magnitude.

and assert that total work — signature reparses, name renderings, definition
scans, enum-width resolutions, and structural match steps — stays near-linear
in total metadata size across the product of those dimensions, not merely flat
along any one of them.

Four existing tests, each named for the defect it pins, assert the
one-dimensional property one instance at a time. The gate must generalize them
*and* cover the dimension none of them measures.

Seed the corpus with the regressions already found by hand so the gate is
demonstrably non-vacuous, and pin any failing seed as an ordinary case.

### Observer states

The three axes above are all defined over *generated metadata*. That leaves a
gap the gate as first specified could not close: the `beforeMaterialize`
observer is a second untrusted-ish input, supplied by the caller, and the
guard's behavior depends on how it returns. Gap 4 lives entirely in this axis,
so a gate blind to it cannot catch the defect this document names.

The generator must therefore cross every metadata case with the observer
states:

- **absent** (`null`) — the common case, and the one that makes "charging" no
  bound at all,
- **non-throwing**, recording charges,
- **throwing `BadImageFormatException`**, and
- **throwing `ArgumentOutOfRangeException`**,

at the regions that can absorb such an exception. An earlier revision of this
section claimed four such regions; that claim was wrong, and the correction
matters because it changes what the gap actually is:

| Region | Where | Effect on an observer exception |
| --- | --- | --- |
| The guard's public boundary | `CustomAttributeValueGuard.IsSafeToDecode:85-97` | Caught; returns `true`. The walk stops, but the refusal is inverted. |
| `TypeResolver` reference and definition paths | `TypeResolver.cs:107`, `:250` | **Does not absorb.** The observer is invoked *before* the surrounding `try`, and the catching overloads never receive it. The exception propagates to the public boundary above. |
| `TypeResolver` exported-type path | `TypeResolver.cs:546`, `:551` | **Not reachable.** `GetTypeName` dispatches only `TypeRef`, `TypeDef`, and `TypeSpec`. |
| `SignatureDecoder.Decode`, reached for `TypeSpec` names | `SignatureDecodeResult.cs:148-176` | Absorbs, but see below. |
| The bound type-name provider | `AttributeDecoder.cs:532-542`, `:285-288` | **Does not absorb.** It wraps the failure and rethrows. |

So there is exactly **one** inversion, not a family of them: an observer
exception reaches the guard's public catch and returns `true`. The walk stops;
the refusal is inverted. That is gap 4, and it is bad enough on its own.

The `TypeSpec` path is the only one that absorbs mid-resolution, and it cannot
produce an I1 divergence here: SRM rejects a `TypeSpec` coded handle in a
custom-attribute blob before ever calling the provider, as this document states
under [Handle-typed enums](#handle-typed-enums-same-handle-same-resolution-function--when-it-resolves).

An earlier revision claimed that a budget stop could manufacture a genuine I1
divergence by substituting a default width mid-resolution. **It cannot.** That
claim was recorded on #5085 and has been retracted there.

The assertion is not merely that the guard returns something reasonable. It is
that **an observer that throws to stop the walk never produces `true`, and is
never silently absorbed into a default width.** A caller's refusal is not a
statement about the blob, and turning one into an approval is the inversion gap
4 records.

Until that gate exists, any statement in this document that any invariant
*holds* is unverified in the sense of [Asserted properties name their
gate](../evidence-and-validation.md#asserted-properties-name-their-gate). Statements about
what the invariants *require* are normative regardless.

A TLA+ model is not the right instrument here, and the repository's existing
model is the useful contrast.
[`docs/models/package-realization-admission/`](../models/package-realization-admission/README.md)
models exact-request-keyed admission and lease-scoped lifetime: genuinely
stateful, concurrent, and full of interleavings a test cannot enumerate. That
is what TLA+ is for.

This property is the opposite shape. It is a differential property of two
*sequential* parsers over one grammar, with no concurrency and no scheduling,
where the second parser is an external binary whose parse we cannot model
faithfully — and a model of SRM that we wrote ourselves would re-introduce
exactly the "two implementations believed to be equivalent" error this design
forbids. Generating real blobs and running the real decoder is both cheaper and
more honest.

## Shape: a paired walker, and what that costs

The repository's preferred containment shape is structural: a constrained type
whose construction establishes an invariant, threaded through the object model
so the compiler preserves it — `InertText.InertString` for values. A weaker
but related shape centralizes parsing at a named boundary without carrying a
type: `HardenedJson`, which returns an ordinary `JsonDocument`. The threat
model distinguishes the two at
[Untrusted JSON rejects duplicate properties](untrusted-data-threat-model.md#untrusted-json-rejects-duplicate-properties).

This component cannot take that shape, and the reason is worth stating plainly
so it is not mistaken for an oversight. Structural containment works when we
own the operation being contained. Here the operation is inside SRM. We cannot
wrap `DecodeValue` in a type that makes desynchronization unrepresentable,
because the thing that must not desynchronize is SRM's internal cursor.

What we have instead is a **paired walker**: a second parser that must track an
external one byte for byte. That shape has a permanent cost, and the cost is
that agreement is not compiler-checked. Any edit to our walker, and any change
in SRM across runtime versions, can break agreement while every build and every
existing test still passes.

Two mitigations follow directly, and they are requirements, not suggestions:

- **Share the decision rather than re-deriving it**, by handle-and-function or
  by provider instance depending on the path, so the pairing is expressed
  structurally wherever it can be. See
  [How the two walkers reach the same width](#how-the-two-walkers-reach-the-same-width).
- **Gate the rest generatively**, because what cannot be compiler-checked must
  be searched for. See issue #5065.

## How the two walkers reach the same width

Width agreement is established by *two different mechanisms* depending on how
the enum is spelled. Conflating them is the mistake this section exists to
prevent.

### Handle-typed enums: same handle, same resolution function — when it resolves

When an argument is spelled `ELEMENT_TYPE_VALUETYPE` or `ELEMENT_TYPE_CLASS`
followed by a coded handle, the guard first attempts to resolve the width
**directly from the handle** — `EnumUnderlyingPrimitive.TryResolveDefinition`,
then `FromDefinition`. On that path it does not consult the caller's resolver.
SRM later reaches `ArgTypeProvider`, whose pending-handle path calls those same
two functions on the same handle. This holds for `TypeDef` and `TypeRef`
handles only. A `TypeSpec` never reaches the provider: SRM's
`CustomAttributeDecoder` accepts only `TypeDef` and `TypeRef` and otherwise
throws, so the guard's `TypeSpec` name decoding has no SRM counterpart to agree
with.

Agreement here comes from a shared *handle and resolution function*, not from a
shared object. That is deliberate, and routing this path through a resolver
would break it: a resolver is keyed by name, a name is a flattened spelling,
and a flattened spelling discards the resolution scope that distinguishes two
definitions or an external reference from a local type. Issue #4914 was exactly
that collapse.

**But the handle path is not unconditionally structural.** When
`TryResolveDefinition` fails — an unresolvable or external `TypeRef` is the
ordinary case — the guard renders the handle to a name and calls the caller's
resolver, and `ArgTypeProvider` falls through to its own name index and then to
the same resolver. So an unresolved handle-typed enum is resolved by the
*serialized-name* mechanism described next, and inherits its requirements,
including referential stability. Treating every handle-typed enum as
structurally resolved is wrong in exactly the population most likely to be
hostile: references into assemblies that are absent or attacker-named.

The structural path must stay handle-keyed; the fallback must be recognized as
a name path and held to the name path's rules.

> **Rule.** The handle path must stay handle-keyed. Do not "simplify" it to go
> through the resolver.

### Serialized-name enums: same provider instance

When an argument is spelled as a serialized enum (`0x55`) and a `SerString`,
the width must come from a name. Here `AttributeDecoder.TryDecode` constructs
one `ArgTypeProvider` and uses it twice: it passes
`provider.GetUnderlyingEnumType` to the guard as the guard's resolver, then
passes the *same provider instance* to `attribute.DecodeValue(provider)`.

Two rules preserve agreement on this path:

- **Name projection must match before the oracle is consulted.** SRM resolves a
  blob-authored serialized name through `GetTypeFromSerializedName` *before*
  asking for its underlying type. A guard that consults the width oracle with
  the raw `SerString` asks a different question whenever the two spellings
  normalize differently — for example when a name only parses after its
  assembly suffix is removed. The guard composes the same two steps for this
  reason.
- **A caller-supplied resolver must be referentially stable for the
  operation.** `ArgTypeProvider.GetUnderlyingEnumType` *invokes* the supplied
  delegate; the guard and SRM invoke it in separate phases. A resolver that
  returns different widths for the same name across those phases reintroduces
  divergence even though both calls went through one provider. Product
  resolvers are pure lookups over a fixed table, which satisfies this; the
  requirement is stated because nothing enforces it.

`BindEnumWidthResolver` normalizes a bare resolver by wrapping it in an
`ArgTypeProvider` so a direct `IsSafeToDecode(..., resolver)` call asks the
projected question rather than the raw one. It is **normalization, not
enforcement**: it cannot make a later `DecodeValue` use the provider it built,
and it cannot make a stateful resolver stable. Same-provider decoding is
guaranteed only on the `TryDecode` path, which is the supported product path.
The resolver-less overload is a conservative test-only path.

### Frozen cross-assembly enum-width adapter

Custom-attribute enum width can consume one frozen
[`TypeResolutionContext`](type-forwarding-resolution.md) through
`TypeResolutionEnumWidth`: planned serialized names become structured
requests, `Resolve` locates an already-retained defining image, and the
resolved definition's authenticated kind plus
`TypeResolutionContext.TryGetEnumUnderlyingType` establish a sealed
core-library-derived `System.Enum` definition and read its single valid
`value__` field without exposing a reader. Reflection-name escapes are
projected back to exact metadata namespace and type segments, and the
pre-decode guard applies SRM's own serialized-name projection before consulting
the width table, so a name that only parses once its assembly suffix is removed
cannot give the guard and the decoder different widths. Unplanned, unbound,
malformed, or callback-ambiguous names stay `Int32`.

Explicit assembly qualifiers stay constraints rather than widening to
wildcards: an explicit `Culture=neutral` is spelled so it cannot match a
culture-specific candidate, and an explicit `PublicKeyToken=null` names an
unsigned assembly. Because an empty token reads as a wildcard during binding,
the adapter records it on the request and then drops a resolved candidate that
turned out to be signed, keeping the qualifier a constraint without changing
the identity contract that `AssemblyDependencyResolver` and `MetadataSource`
also consume. The qualifier constrains the assembly the reference bound to, so
when forwarding hops were followed the narrowing inspects the first hop's
source rather than the terminal definition. A definition that is not a
CLI-valid enum -- unsealed, not directly derived from `System.Enum`, generic,
carrying a non-public, non-special, or literal `value__`, or carrying a
non-literal static field -- supplies no width.

An argument whose signature names a type by handle is resolved from the
definition that handle denotes, on both sides, never from its rendered name. A
definition handle denotes itself; a reference is matched structurally, by name
and resolution scope. Distinct definitions can render to one string: a nested
type joins its declaring type with `.`, exactly as a namespace joins a type
name, so a nested `Kind` declared in `Samples.E` and a top-level `Kind` in
namespace `Samples.E` both render `Samples.E.Kind`. A reference additionally
carries a resolution scope that its flattened spelling discards. Any
name-keyed index must therefore drop one colliding definition, and routing
either side through a name would let the guard and the decode select different
definitions and skip different widths. Both sides ask
`EnumUnderlyingPrimitive.TryResolveDefinition` about the same handle and take
the width from the definition it returns;
`NestedTypeNameCollision_GuardSkipMatchesDecodeWidth` gates both handle forms
and `CollidingTypeDefNames_EachResolveTheirOwnWidth` gates the premise. A
supplied name resolver never overrides a definition the signature already
named, on either side. Structural matching walks a reference's nested scope
chain but does not consult its terminal assembly or module scope, so a
reference whose chain matches a definition in this reader resolves to that
definition even when it nominally denotes another assembly. That is
long-standing behavior, gated by
`TypeRefEnumMatchingLocalInt64_SeesFollowingArrayCount`, and it is what keeps
this side aligned with a decode that would otherwise reach the same local
definition through its rendered name. A reference whose chain matches no
definition here resolves by name as before.

A name that has no pending handle -- a reference to a type this reader does not
define, or a name the blob authored -- is looked up by spelling, and that
lookup depends on where the name came from. A handle-derived name is an exact
metadata spelling that reaches the provider verbatim, and metadata names may
contain characters a reflection type name treats as escapes, so it is matched
by its exact spelling before its reflection-normalized one. A blob-authored
name is reflection syntax whose escapes are meaningful -- `E\+Kind` names the
metadata type `E+Kind`, not one spelled with a backslash -- so it is normalized
first and never matched verbatim. Both sides of the guard/decode pair classify
a name the same way, so the two remain aligned either way. That classification
belongs to a single pending lookup, not to a spelling: the provider records
only that the name it produced most recently came from the blob, and clears
that mark when it produces a handle-derived name. Remembering spellings instead
would let a blob-authored occurrence change how a later handle-derived
occurrence of the same spelling resolves, making a consumed width depend on
argument order. The guard also resolves a repeated enum name once rather than
once per array element, because the element count is attacker-chosen and
per-element parsing is the amplification the guard exists to prevent.

Product extract does not yet collect custom-attribute enum names into a
generation; that remains residual on
[#4741](https://github.com/richlander/dotnet-inspect/issues/4741).
`TypeResolutionEnumWidthTests` gates the adapter, and
`CustomAttributeValueGuardTests` gates guard/decoder width alignment through
`EscapedTypeDefEnumName_GuardSkipMatchesDecodeWidth` and
`EnumArrayElements_ResolveTheWidthOncePerName`.

### Resolution order, and what `Int32` actually means

`ResolveEnum` tries the structural definition path first and consults the
name-keyed resolver only when that fails. `Int32` is the **last** fallback, not
the answer for every unknown name.

**Resolution is a 2×2 matrix, not two orders.** `enumUnderlyingType` is an
optional parameter, and the enum may be spelled either as a handle or as a
serialized name. Those two axes are independent, and all four cells behave
differently:

| | **With a resolver** (product path) | **Without a resolver** (bare overload) |
| --- | --- | --- |
| **Handle-spelled** (`ResolveEnum`) | Structural `TryResolveDefinition`, then the provider's name-keyed resolution over the local `TypeDefinitionsByName` index and any trusted external width table, then `Int32`. | Structural `TryResolveDefinition`, then `FromHandle` — which for a `TypeRef` **scans every type definition** via `TryFindDefinition` — then `Int32`. No name index. |
| **Serialized-name** (`ResolveEnumName`) | `ProjectSerializedEnumName`, then the provider. Never touches the definition table directly. | `FromSerializedName` → `TryFromSerializedName` → `TryFindDefinition`, which **also scans every type definition**, then `Int32`. |

Two corrections to the intuitive reading follow, and both matter:

- The resolver-less path is **not** "structural, then give up." Both of its
  cells reach a full linear scan of the definition table. That scan is the
  source of the `Θ(P × T)` cost in gap 2.
- The resolver-supplied *serialized* cell is the only one that never consults
  local definitions itself; it delegates entirely to the provider. So "the
  guard consults the definition table" is true in three cells out of four, and
  false in the one the product actually uses for serialized names.

The handle/resolver-less divergence is pinned by an existing regression: the
bare overload reaches a different width than product `TryDecode` for the same
blob. Anyone reasoning about alignment must therefore say **which cell** they
mean. The resolver-less column is not a simplified product path; it is a
different resolution order, and only the resolver-supplied column is the one
SRM's provider mirrors. I1 is scoped accordingly — see gap 5.

The middle step of the product path matters for threat modelling: a
cross-assembly `TypeRef` that fails structural resolution can still match a
*local* definition whose flattened spelling collides with it, and take that
definition's width. The inspected image therefore has some influence over the
width chosen for a name it does not define. This is currently a **fidelity**
risk rather than a divergence, because both walkers reach the same definition
through the same shared index —
`ExternalReferenceCollidingWithNestedName_IsRefusedNotDecoded` pins exactly
that case, with both sides agreeing on eight bytes and the blob refused on the
count.

Say "resolves to `Int32` when structural, local-name, and trusted-external
resolution all fail" rather than "unknown names resolve to `Int32`."

## The two width-resolution paths are not symmetric

**A fixture exercising one path proves nothing about the other.** Issue #4914
was a name-path collapse — distinct type definitions that render to the same
display name shared one index entry, so the guard could resolve a width from
the wrong definition while the decoder resolved from the handle. The fix
resolves definition-typed enums from their handle. Issue #4992 asks whether the
same collapse remains reachable on the blob-authored name path; it is open and
explicitly unproven.

Any change to either path must state which path it changes and must not assume
symmetry between them.

## The walk

The guard walks the value blob iteratively on an explicit heap-allocated work
stack rather than recursively, so a deeply nested blob cannot overflow the
native stack before any bound is consulted.

Elements of an `SZARRAY` share one element type, which appears once in the
signature. The guard replays it: `ProcessSzArrayElements` rewinds
`_signature.Offset` to the element's `SignatureStart` and re-parses the element
type for every element, restoring the signature offset once the last element is
done.

**This replay is the component's principal hazard, and it is what I3 exists to
bound.** Any work reachable from element-type parsing is multiplied by an
attacker-chosen element count, on input the guard *accepts* — so no refusal
bounds it. Four separate amplifications of this shape have been found and fixed
individually (a re-materialized type name, a re-validated constructor
`TypeSpec`, a replayed custom-modifier chain, and a re-skipped nested
descendant, the last measured at a 537x multiplier). The structure that
produced all four remains.

Each of those four is now pinned by its own hand-written test —
`ArrayElementCustomModifiers_AreSkippedOncePerArray`,
`GenericParameterArrayElements_ResolveTheTypeSpecOnce`,
`SignatureTypedArrayElements_RenderTheTypeNameOncePerHandle`, and
`EnumArrayElements_ResolveTheWidthOncePerName`. Four tests named for the four
scars is the signature of a missing invariant: each was written after the fact,
and none of them would catch the fifth. That is why I3 is stated as a contract
and given a gate rather than left as review discipline.

SRM does not do this: it resolves an `ArgumentTypeInfo` once and then loops.
Issue #5047 tracks converging on that shape.

Until it lands, this is a standing rule for anyone editing the guard:

> **Work introduced into element-type parsing is per-element work.** Before
> adding a resolution, materialization, or validation step reachable from
> `ProcessFixedArg`, establish that it is memoized or that it is cheap enough
> to be paid an attacker-chosen number of times.

## Bounds, and what each one actually does

The two numeric bounds in this component are frequently conflated. They are not
the same kind of bound and they do not have the same consequence.

| Bound | Value | Effect |
| --- | --- | --- |
| `CustomAttributeValueGuard.MaxSerializedDepth` | `SignatureBlobGuard.DefaultMaxDepth` (512) | **Refuses.** Boxed/`SZARRAY` nesting past this depth returns `Unsafe`, so the blob is never handed to SRM. |
| `EnumUnderlyingPrimitive.MaxNestingDepth` | 128 | **Stops matching, then falls back.** `Matches` returns `false` past this depth. On the resolver-supplied product path, resolution then continues down the name path. On the resolver-less overload it does not: `ResolveEnum` calls `FromHandle`, which falls back directly to `Int32` (`EnumUnderlyingPrimitive.cs:168-177`). The decode is not refused either way. |

`MaxSerializedDepth` is a safety bound. `MaxNestingDepth` is a termination
bound on a recursive structural comparison, protecting against an uncatchable
native stack overflow. Exceeding it does not refuse anything; it degrades
structural matching to name-based resolution.

On the resolver-supplied path that degradation is **symmetric** — both walkers lose structural matching at
the same depth and fall back the same way — which is why alignment survives it.
`NestingDeeperThanTheMatchBound_GuardSkipMatchesDecodeWidth` pins a successful
product decode at depth 200 for exactly this reason.

> **Do not describe `MaxNestingDepth` as refusing a decode.** If a future change
> makes the fallback asymmetric, this bound becomes a divergence source rather
> than a safe degradation.

`MaxSerializedDepth` also caps chains the guard walks, which has a practical
consequence for tests: a fixture built with more than 512 custom modifiers is
refused before any behavior under test is reached.

### What a declared count can claim

The bounds above are ours. The ceilings that make them necessary are SRM's, and
they are not uniform:

| Declared count | Read as | Ceiling | Slot |
| --- | --- | --- | --- |
| `SZARRAY` element count | `Int32` from the value blob | `int.MaxValue` | `CustomAttributeTypedArgument<T>`, two references |
| Fixed-argument count | compressed integer from the constructor signature | 0x1FFFFFFF | same |
| Named-argument count | `UInt16` from the value blob | 65,535 | `CustomAttributeNamedArgument<T>` |

Only the array count is a serious amplifier: a blob of a dozen bytes can ask for
tens of gigabytes. The named-argument count is capped by its own encoding at
roughly two megabytes and should not be described as an equivalent threat. The
fixed-argument count is sometimes assumed safe because it comes from the
constructor signature rather than the value blob, but in a hostile assembly that
signature is attacker-written too, so it is bounded only by the compressed-integer
encoding.

### Charging is refusal accounting, not materialization

The guard reports work through a `beforeMaterialize` observer *before* doing
it, so a large charge can appear next to a `false` result. That pairing means
"this is what the blob claimed, and we declined," not "this was allocated."

Reading a large charge as evidence of materialization has misled three separate
reviewers. State it explicitly in any new charging code.

The guard contains no `throw` for budget purposes; a caller's observer raises.
How that exception travels depends on where it was raised and what type it is,
and the combination is not clean:

- **Provider callbacks are wrapped** in `MaterializationObserverException` and
  rethrown, because an exception crossing SRM's callback boundary would
  otherwise be swallowed by SRM's own catch behavior and turn a budget stop
  into a silent decode failure. This applies during `DecodeValue` and also when
  the guard drives the provider.
- **Direct guard-side observer calls are not wrapped**, so the exception type
  decides the outcome. Most types propagate. But `BadImageFormatException` and
  `ArgumentOutOfRangeException` are caught by the public boundary's
  malformed-metadata handlers and converted to `true` — meaning an observer
  that raises either type to stop the walk instead **approves the blob**, and
  SRM then runs.

> That last case is a real hazard, not a documentation detail: it makes a
> caller's budget stop indistinguishable from a malformed-blob deferral. The
> two concerns should not share a catch. Tracked as issue #5085.

## Failure semantics

The guard's return value is not "is this blob well-formed." It is "is it safe
to let SRM try." Both `true` and `false` are reached deliberately, and *where*
a malformed state is detected decides which one you get.

| Condition | Result | Why |
| --- | --- | --- |
| Value-walk read runs out of bytes (`Result.Truncated`) | `true` | SRM's failure is catchable and precise; ours would be a guess. Let the decoder own the error. |
| Truncation in the constructor `TypeSpec` **prologue** — region A, from the blob start through the generic argument count | `true` | `ResolveConstructorInstantiation` returns `Safe` with `found == false` at each of its truncation points, and the caller propagates that. |
| **Detected** failure while stepping over a preceding generic argument — region B, arguments `0` through `parameterIndex - 1` | `false` | `TrySkipSrmAttributeType` reports failure rather than a plausible offset, and `LocateGenericArgument` maps that to `Unsafe`. |
| Truncation of the **selected** generic argument's own type — region C, e.g. a `CLASS` whose coded index is cut off | `true` | `BlobReader.ReadTypeHandle()` returns a **nil handle** rather than throwing. The nil handle flows through `SkipNamedType`, resolves to the default `Int32`, and the walk either skips four bytes or reports `Truncated`. Both map to `true`. No exception, and no public catch, is involved. |
| A **substituted** `VAR` in the selected generic argument — also region C | `false` | `ProcessGenericParameter` clears `_substituteGenerics` before re-entering, so a second substitution reaches the element switch as an unrecognized form and returns `Unsafe`. Re-entering on the same `TypeSpec` would otherwise overflow the stack. |
| A **complete** `MVAR` that the walk actually examines | `false` | `ProcessGenericParameter` returns `Unsafe` for `methodParameter`, and SRM likewise handles only type parameters. Method generic parameters are refused, never substituted. |
| An `MVAR` whose index byte is **truncated** | `true` | `ProcessGenericParameter` returns `Safe` on `RemainingBytes < 1` *before* it reads the index and tests `methodParameter` (`:681-687`). |
| An `MVAR` among the **trailing** generic arguments — region D | `true` | `LocateGenericArgument` skips only arguments `0..parameterIndex-1` (`:748`), so arguments after the selected one are never examined. |
| Truncation in a generic argument **after** the selected index — region D | *not examined* | `LocateGenericArgument` skips only `0` through `parameterIndex - 1` and stops. Bytes after the selected argument are never read by the guard, so their state cannot affect its result. |
| Unknown fixed-argument element code, unsupported serialized type, invalid named-argument kind | `false` | The guard positively classifies these as forms it cannot track. It refuses rather than walking blind alongside a decoder it can no longer follow. |
| Declared count exceeds what the remaining bytes can describe | `false` | Invariant I2. The count is the amplification vector and must be refused before SRM allocates from it. |
| Serialized nesting past `MaxSerializedDepth` | `false` | Refuse rather than truncate. |
| `TypeDefinitionIndexException` raised while building the type-definition index during guard-side name fallback | `false` | The walk never finished, so a later `DecodeValue` with a different provider must not run. Note this is raised lazily when the bound delegate is *invoked*, not when the resolver is bound, so prefix values may already have been walked and charged. |
| `BadImageFormatException` / `ArgumentOutOfRangeException` **reaching the public boundary** | `true` | Same reasoning as truncation: hand it to SRM. Also catches observer exceptions of these types; see the hazard above. |
| The same exceptions **caught inside one of this guard's own parsing helpers** | `false` | A helper that cannot complete its own read has lost track of the blob; it reports failure rather than returning a plausible offset. |
| `BadImageFormatException` caught inside **`SignatureBlobGuard.IsSafeToDecode`** | `true` | That component deliberately returns `true` for truncated-but-shallow blobs: the depth check is what it owns, and a truncated blob is shallow up to the truncation. This guard propagates that `true`. |

Three consequences worth stating for anyone editing this:

- "Unrecognized" is not one outcome. A form the guard *positively identifies*
  as unsupported refuses; a read that simply *runs out of bytes* defers to SRM.
- **The generic-argument regions do not partition cleanly by offset, and an
  earlier draft's claim that they did was wrong.** Region C alone produces
  *both* results depending on *what* is malformed rather than *where*:
  truncation of the selected type throws and yields `true`, while a substituted
  `VAR` yields `false`. Region D is not examined at all, so a generator can
  place arbitrary garbage after the selected argument without changing the
  guard's answer. The partition is therefore by **role in the walk** — prologue,
  skipped predecessor, selected type, unread suffix — and the outcome within a
  region still depends on the malformation. State both coordinates when adding
  a case.
- **"Helper catches produce `false`" is not a general rule.** It holds for this
  guard's own parsing helpers. It does *not* hold for
  `SignatureBlobGuard.IsSafeToDecode`, whose catch deliberately returns `true`,
  nor for `TypeResolver`, which absorbs both exception types on every
  name-rendering path and returns a `null` name that becomes a default width.
  Adding a catch changes the contract, so state which behavior you intend and
  which component owns the decision.

The deliberate `true` results are the reason this component is easy to misread.
They are *not* fail-open in the safety sense, because they hand the blob to a
decoder that will itself fail closed and catchably. The genuine fail-opens are
a `true` reached by skipping the wrong bytes (I1), a `true` reached without
refusing an attacker-declared quantity (I2), and — as #5085 shows — a `true`
reached because the *caller's own* attempt to stop the walk was mistaken for a
malformed blob.

## Open work

| Issue | Concern |
| --- | --- |
| #4992 | Whether the width-alignment collapse fixed on the handle path remains reachable on the blob-authored name path. Open and unproven. |
| #5047 | Per-element element-type replay; resolve once and loop, as SRM does. Gap 3. |
| #5065 | The differential oracle named above as this design's enforcement gate. |
| #5085 | An observer exception can be caught as malformed metadata and turned into an approval. Gap 4. Found while reviewing this document. |
| #5091 | Quadratic guard work across declared parameter count and type-definition count. Gap 2. Found while reviewing this document. |
| #5098 | Blobs that cost SRM quadratic work across parameter count and generic arity, which the guard's memoization hides. Gap 1. Found while reviewing this document. |
| #5120 | The resolver-less `IsSafeToDecode` overload does not carry I1. Gap 5. Found while reviewing this document. |
| #5130 | Every memo is a single slot, so alternating input defeats all four. Gap 6. Found while reviewing this document. |
| #5132 | Quadratic cost across attribute rows sharing one value blob. Gap 7. Found while reviewing this document. |
| #4879 | Enum constants whose signature does not match `value__`. Fidelity. |
| #5062 | Signature decode laundering internal errors into `SignatureRejected`. |
| #4741 | Product extraction does not yet plan custom-attribute enum names into a frozen type-resolution generation. |

Issue #5067 tracks this space as a whole.

The amplification gaps are not independent cleanups. Gaps 1, 2, 3, and 6 are one
question — *which side pays for a lookup, and is the answer stable under
adversarial ordering* — asked in four places. A fix that only moves cost from
the guard to the decoder, or that makes a memo hit more often without making it
hit on *every distinct input*, has not resolved any of them. Prefer one coherent
change evaluated against both walkers over four local optimizations.

Gap 7 is deliberately excluded from that grouping: it is cross-row, so no
per-walk memo can address it.
