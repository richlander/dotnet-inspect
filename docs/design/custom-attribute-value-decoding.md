# Custom-Attribute Value Decoding

`dotnet-inspect` reads custom-attribute values out of assemblies it did not
produce. A custom-attribute value is a byte blob whose meaning is not
self-describing: the blob carries values, and the *constructor signature* — a
separate blob, in a separate table — says how wide each value is. Decoding one
requires reading two attacker-supplied structures together and agreeing about
where every element begins and ends.

**We own that decode.** This document owns the contract that makes it safe.

**Status: prescriptive, ahead of the implementation.** This document states the
contract established by [#5288](https://github.com/richlander/dotnet-inspect/issues/5288),
which inverts this subsystem's relationship to `System.Reflection.Metadata`.
Slice 1 is this document; the decoder itself is slice 2. Until slice 2 lands,
`AttributeDecoder` still calls `CustomAttribute.DecodeValue` behind
`CustomAttributeValueGuard`, and the paired-walker hazards described under
[How this design changed](#how-this-design-changed) are still live in the code.
Read every invariant below as the target the implementation is held to, not as
a description of what ships today.

The same caution applies to this document's **detail-level claims** — the
mechanism tables and cited behaviors. They were established by reading, and
adversarial review has repeatedly corrected rows that reading got wrong. Verify
against the code before relying on any specific row.

## Responsibility

This design owns the safety contract for decoding custom-attribute values from
untrusted metadata: what the decoder promises, how enum widths are resolved,
what it refuses, and what must remain true of its cost and its output.

The implementation lives in `src/ILInspector.MetadataPrimitives/`:
`CustomAttributeValueGuard.cs`, `AttributeDecoder.cs`, and
`EnumUnderlyingPrimitive.cs`.

## Boundaries

- **Consumes** an owner-issued `MetadataReader` and `CustomAttribute` value.
  Acquiring that value from a handle belongs upstream. Acquisition, image
  lifetime, and provenance belong to their owners.
- **Consumes** `SignatureBlobGuard` for structural signature bounds. This design
  does not redefine that component's depth policy or failure semantics.
- **Produces** either a decoded `CustomAttributeValue<string>` or a null result
  meaning *not decoded*. It never produces a partially decoded value.
- **Defers** work-charging policy to the caller's observer. This design says
  what is charged, not what a budget does with it.
- **Consumes** `System.Reflection.Metadata` as a *test-time oracle only*, once
  #5288's slice 2 lands. No production path will call
  `CustomAttribute.DecodeValue`; one still does today.

## Non-claims

- Not a general signature-decoding contract. Method, field, and property
  signature decoding are owned elsewhere.
- Not a defense against local actors, our own code, or other contributors. See
  [Trust boundaries](untrusted-data-threat-model.md#trust-boundaries).
- Not a promise that attribute values are *correct*, only that reading them
  cannot be turned into an unbounded or out-of-bounds operation, and that on
  output from compilers in the certified range they match what the producing
  compiler encoded — except for the one width case D3 explicitly carves out.
- Not a promise that our decoder matches SRM on **illegal** input. There the
  obligation is D1 and D2 only.
- Not a change to any caller's API or output shape. The decoder produces the
  same `CustomAttributeValue<string>` it produces today.

## The trust boundary

| Element | Value |
| --- | --- |
| **Actor** | Whoever authored a package, symbol, or assembly the user inspects. Publishing to a public feed is enough; no local access is needed. |
| **Input path** | `CustomAttribute.Value` (the value blob) together with the constructor signature blob reached through the attribute's constructor handle, plus the `TypeDef`/`TypeRef` rows those signatures name. |
| **Affected boundary** | `AttributeDecoder`, and every caller that renders, indexes, counts, or searches attribute values. |
| **Containment invariants** | Three — D1, D2, D3 — stated below. |
| **Enforcement gate** | Stated below. |

The tool never executes inspected code, so the realistic damage is not code
execution but resource exhaustion and misread memory: a small download that
costs disproportionate CPU or memory, or a decode that reads a length from the
wrong offset.

## The format's adversarial properties

State these before stating any invariant. Each property is a fact about
ECMA-335 §II.23.3, and each one forces a specific obligation on the decoder.
An invariant that does not trace back to a row here is decoration.

| Property | Consequence for the design |
| --- | --- |
| **Not self-describing.** The value blob and the constructor signature are read together. | Two cursors inside one walker. A signature-side error still relocates every value read. |
| **Inconsistently self-describing.** Named arguments, boxed values, and `object[]` elements carry inline types; fixed arguments do not. | Enumerate the domain **per position**. The same logical type has different bytes depending on where it appears. |
| **Width depends on name resolution, possibly cross-assembly.** | You cannot skip a value you do not understand. Resolution is part of the decoder, not a caller callback. |
| **Declared counts drive allocation.** | D1's allocation clause is a separate obligation, not a consequence of correct parsing. |
| **No internal framing and no resync point.** | Decoding is all-or-nothing. There is no partial-recovery design. |
| **One cursor, and it is ours.** | Consumption is directly observable to a test. The previous design's central testability problem — that over-consumption was invisible from SRM-side signals — does not arise. |

The third and fourth rows together are the heart of this component. Because
width depends on resolution, a resolution mistake relocates the cursor; because
counts drive allocation, a relocated cursor can read an allocation size out of
the middle of a string. Those two facts multiply, and the design must break the
product rather than trusting either factor alone. That is why D1's allocation
clause is stated independently of parsing correctness: it must hold *even when
the cursor is in the wrong place*.

## The containment invariants

Three properties must hold together. They are independent: any one can break
while the others hold.

They are stated as **targets**. Each is normative — a violation is a defect to
fix, not a behavior to document.

> **D1 — Bounded.** Total work is near-linear in the size of the metadata the
> decoder is given, across every attacker-controlled cardinality *jointly*.
> Retained memory is at most a constant multiple of the value blob's length.
> **No allocation is ever sized from a declared count that exceeds the
> remaining bytes.**
>
> **D2 — Fail closed, visibly.** A blob whose *structure* the decoder cannot
> follow yields `null` — never a partial value, never a laundered exception,
> never a plausible-looking guess about where the next element begins. No
> declared count can size an allocation disproportionate to the bytes actually
> supplied.
>
> **D3 — Fidelity.** On output from compilers in the certified range, decoded
> values equal the values the producing compiler encoded. SRM arbitrates
> wherever it can; where it cannot — notably an enum width that both decoders
> default to `Int32` — the certified corpus supplies producer truth directly.

D1 is about cost. D2 is about what happens when the input wins. D3 is about
being right on input that is not an attack.

### D1 — Bounded

D1 has two clauses that are usually conflated, and separating them is the point
of the invariant.

**The cost clause** is the old I3, unchanged: near-linear aggregate work across
declared element counts, declared parameter counts, generic arity, distinct
handles and names, the number of rows a failed resolution scans, the number of
attribute rows decoded from one image, and the size of a blob shared across
many rows — *jointly*, because the attacker chooses them together. Bounding each
dimension separately is not sufficient.

D1 is deliberately stated over aggregate cost rather than per-element
repetition. "Perform this work once per distinct input" is the right *fix* for
the amplifications found so far, but it is the wrong *invariant*, because it is
satisfied by a walk that is still quadratic. A constructor declaring `P` fixed
arguments, each naming a distinct unresolvable `TypeRef`, resolved against an
image holding `T` type definitions, costs `Θ(P × T)` on metadata of size
`Θ(P + T)` — because `EnumUnderlyingPrimitive.TryFindDefinition` scans every
definition, and each comparison is itself a recursive structural match. Every
handle is resolved exactly once. No count is repeated. The walk is still
quadratic. Tracked as issue #5091.

**The allocation clause** — no allocation sized from a declared count exceeding
the remaining bytes — is the reason this component exists at all, and it is
stated separately because it must hold *unconditionally*, including on a blob
the decoder is misreading. Every element of every array costs at least one byte
in the blob, at every position, so a count larger than `RemainingBytes` is
unsatisfiable regardless of what the elements turn out to be. Refuse it before
sizing anything.

**Charging is now materialization accounting.** Under the previous design the
`beforeMaterialize` observer reported work the guard had *declined* to do, so a
large charge could appear next to a refusal, which misled three separate
reviewers. With one walker that actually materializes, the charge means what
its name says: this is what we are about to allocate. Keep it, and keep it
before the allocation.

**Charging is not an escape hatch for the cost clause.** The observer is
optional and frequently absent, and `Charge` returns immediately when it is
null. Work that is charged to nobody is unbounded work. A cost may be delegated
to an observer only where a caller is guaranteed to be present and its refusal
is guaranteed to stop the walk.

**Element-type replay is the principal cost hazard.** Elements of an `SZARRAY`
share one element type, which appears once in the signature; the walker replays
it by rewinding the signature offset and re-parsing per element. Any work
reachable from element-type parsing is therefore multiplied by an
attacker-chosen count, on input the decoder *accepts*, so no refusal bounds it.
Four separate amplifications of this shape have been found and fixed
individually — a re-materialized type name, a re-validated constructor
`TypeSpec`, a replayed custom-modifier chain, and a re-skipped nested
descendant, the last measured at a 537× multiplier. Each is pinned by its own
hand-written test — `ArrayElementCustomModifiers_AreSkippedOncePerArray`,
`GenericParameterArrayElements_ResolveTheTypeSpecOnce`,
`SignatureTypedArrayElements_RenderTheTypeNameOncePerHandle`, and
`EnumArrayElements_ResolveTheWidthOncePerName`. Four tests named for four scars
is the signature of a missing invariant, and none of them would catch the
fifth; that is why D1 is stated as a contract with a gate rather than left as
review discipline. Issue #5047 tracks resolving the element type once and
looping, which is the shape SRM already uses.

> **Standing rule.** Work introduced into element-type parsing is per-element
> work. Before adding a resolution, materialization, or validation step
> reachable from fixed-argument processing, establish that it is memoized or
> that it is cheap enough to be paid an attacker-chosen number of times.

### D2 — Fail closed, visibly

The decoder's output is a value or `null`. There is no third state, no partial
result, and no exception that escapes as a decode outcome.

**Refuse; do not defer.** This is the sharpest change from the previous design,
which deliberately returned "safe" for a dozen conditions on the reasoning that
SRM's failure would be catchable and precise while ours would be a guess. There
is no SRM to defer to now, so every one of those conditions becomes `null`:

| Condition | Result |
| --- | --- |
| A read runs out of bytes at any structural position | `null` |
| Unknown fixed-argument element code | `null` |
| Unsupported serialized type form | `null` |
| Invalid named-argument kind | `null` |
| A `TypeSpec`-typed enum | `null` |
| `MVAR` (a method generic parameter), complete or truncated | `null` |
| A jagged fixed argument | `null` |
| A generic constructor header | `null` |
| Serialized nesting past `MaxSerializedDepth` | `null` |
| A declared count exceeding the remaining bytes | `null` |
| A caller observer raising to stop the walk | propagates; never becomes a value |

The last row is the residue of issue #5085. Under the previous design a
`BadImageFormatException` or `ArgumentOutOfRangeException` raised by a caller's
observer was caught by the public boundary's malformed-metadata handler and
converted into *approval* — a caller's budget stop became a decision that the
blob was safe. With one walker there is one catch boundary and one outcome for
malformed metadata, so a caller's refusal and a malformed blob no longer share a
catch by accident. **A caller's refusal is not a statement about the blob**, and
it must never be laundered into one.

**The claim is about amplification, not about allocation.** State it precisely,
because two stronger-sounding versions are both wrong. "`OutOfMemoryException`
has no path to arise" is unachievable — a large enough legitimate blob can
exhaust a small enough host, and no parsing discipline prevents that. "No
attacker-declared quantity can produce an `OutOfMemoryException`" is also wrong,
because the element count `N` *is* attacker-declared and does size the result;
what makes it safe is that the attacker must supply `N` bytes to declare it.

The enforceable claim is therefore a **bounded amplification factor**: retained
memory is at most a constant multiple of the bytes actually supplied, and that
constant is a property of the output representation rather than of anything the
blob declares. `count > RemainingBytes → null` is what establishes it, and it
holds independently of whether the parse is correct.

Sizing that constant honestly matters, because it is easy to undercount. A legal
`SZARRAY<bool>` of `N` elements occupies about `N` bytes of blob and materializes
`N` `CustomAttributeTypedArgument<string>` slots. Each slot is two references
(16 bytes on a 64-bit host) — but `CustomAttributeTypedArgument<T>.Value` is
typed `object`, so every primitive element is **boxed**, adding a separate
heap object per element. For the densest case the true figure is tens of bytes
retained per blob byte, not the 16 the slots alone suggest. Quote the shape of
the bound rather than a specific multiple, and if slice 2 wants a smaller
constant it must introduce and gate value caching explicitly.

What D1 removes is the amplification — the twelve-byte blob that asks for tens of
gigabytes. A host-level memory limit is the host's concern, not this contract's.

Issue #5397 (`TryDecode` swallows `OutOfMemoryException` through a bare catch) is
therefore **not** moot under the inversion, and this document retains it as a D2
defect. #5288's slice 4 lists it among the issues to close; that disposition
assumed OOM had no path at all. Swallowing `OutOfMemoryException` is precisely
the "laundered exception" D2 forbids, and it stays forbidden whether the OOM came
from an attack or from a large honest blob.

This is a **deliberate divergence from the owning specification**, and it is
normative here: slice 4 must not close #5397. The divergence is proposed to
issue #5288 so the owner can fold it into the slice-4 list; until that edit
lands, an implementer following the issue verbatim would leave a known D2
violation in place, so this document governs.

### The `Int32` enum-width default is a named exception to "refuse; do not defer"

The table above says the decoder refuses what it cannot follow. Enum width
resolution does the opposite, and the contradiction is deliberate rather than an
oversight, so it is stated here rather than left for a reader to discover.

When structural, local-name, and trusted-external resolution all fail, the width
resolves to `Int32` (`EnumUnderlyingPrimitive.cs:59`, `:63`, `:177`, `:287`) and
the decode continues. **This is a defined total function, not a failure to
follow.** The decoder always knows how many bytes to consume, so the blob remains
structurally followable and D2 is not violated — D2 governs structure, which is
why its statement above says *structure* explicitly.

Refusing instead would be worse, and the reason bounds how much this is worth: an
enum defined in an assembly the user did not download is the ordinary case, not
the hostile one. Refusing every unresolvable width would turn a large fraction of
real attributes into `null`.

Two consequences must be recorded rather than assumed away:

- **A wrong default width is a D3 fidelity defect with real reach.** The width
  decision relocates the cursor, so an enum that is actually `Int64` decoded as
  `Int32` mis-reads every subsequent argument, not just its own. It cannot become
  an unbounded allocation — D1's allocation clause holds independently of the
  parse — but it can produce a confidently wrong rendering of the whole
  attribute.
- **The D2 gate cannot catch it.** "`null` wherever SRM throws" passes, because
  SRM reaches the same default through the same provider fallback and also
  succeeds. Any instrument that uses SRM as its oracle is blind to a width both
  sides guess identically. Establishing this belongs to D3's certified corpus,
  where the real width is known from the producing compiler, not to a
  differential test. That obligation is stated as a gate requirement under
  [D3](#d3--fidelity).

**Slice 2 owes a comment fix here.** `EnumUnderlyingPrimitive.cs:16` currently
justifies the `Int32` default as being chosen "so the skip stays aligned" — a
paired-walker rationale for an alignment obligation the inversion deletes. The
default survives; its stated reason does not.

**Nested `object[]` stays legal.** Certified compilers emit `1d 08` and `1d 51`
inside `object[]`, so the recursive element rule is retained. It is bounded by
`MaxSerializedDepth`, which is argued against that observed producer shape as
well as against hostile input — a depth bound chosen only against attackers
would be free to be far tighter and would then refuse real attributes.

### D3 — Fidelity

On output from compilers in the certified range, our decoded values equal the
values the producing compiler encoded.

SRM is an oracle, not a counterpart. A disagreement is a **fidelity bug**: the
value we print is wrong. It is never a safety bug, because nothing about SRM's
behavior bounds ours. This is the entire benefit of the inversion, and it is
worth stating in exactly those terms, because under the previous design the same
disagreement was a fail-open in which we had already told a caller the blob was
safe.

**SRM equality is not the claim; it is the cheap way to check most of it.**
Stating D3 as "equals SRM" would be a contract this component can satisfy while
being wrong, and the gap is not hypothetical — it is the largest known fidelity
risk in the component. When an enum's defining assembly is unavailable, our width
resolution defaults to `Int32`; SRM's provider reaches the same default through
the same fallback. If the enum is really `Int64`, both decoders consume four
bytes where eight were written, both mis-read every subsequent argument, and both
produce the identical wrong answer. An SRM-equality oracle passes.

So D3 names **producer truth** as the standard and SRM as an arbiter only where
SRM is actually independent. The distinction that matters is not what SRM knows;
it is whether the oracle's resolution path is separate from the one under test.
SRM reads an enum's width by asking its provider, so an oracle wired to our
resolution logic returns our answer and the comparison proves nothing.

| Where the width comes from | Oracle | Is the decoder obliged to be right? |
| --- | --- | --- |
| The value's type is decoded without consulting our resolution logic — primitives, strings, `System.Type` names, arrays of these | SRM equality. Cheap, broad, and sufficient. | Yes |
| Our frozen adapter resolves the width from a retained defining image, where a faithful SRM oracle would have to consult that **same** adapter | **The certified corpus**, which built the assemblies and knows each enum's underlying type from source. SRM equality here is degenerate, not independent. | Yes — the information is present, so a wrong width is a fidelity defect |
| No resolution path can establish the width, because the defining image is genuinely absent | **None exists.** Both decoders default to `Int32`. | **No.** See the carve-out below |

Row two is an obligation on the D3 gate, not an aspiration: the stage-1 corpus
must include assemblies that reference a non-`Int32` enum across an assembly
boundary *whose defining image the workspace has retained*, assert the decoded
value against the underlying type known from source, and do so without crediting
an SRM run that consulted the same adapter. Omitting that case leaves the defect
class ungated while the gate reports green.

**Row three is a carve-out from D3, not a gate obligation.** When the defining
image is absent, the underlying type is not recoverable from anything the decoder
can see, so no gate can require the produced value — demanding it would state a
contract the component cannot satisfy at any level of effort. The decoder
defaults to `Int32` under the [named exception](#the-int32-enum-width-default-is-a-named-exception-to-refuse-do-not-defer)
to "refuse; do not defer", and the gate's assertion is that documented behavior:
`Int32` is chosen, the choice is reachable by a caller that wants to know, and the
resulting misread is not laundered into a success-shaped value for the *rest* of
the blob. A real `Int64` enum in this position still yields a wrong value and
still drifts the cursor; that consequence is owned by D2's refusal machinery,
which sees the drift, not by a fidelity claim that cannot be met.

Narrowing the carve-out is [#4741](https://github.com/richlander/dotnet-inspect/issues/4741)'s
job: the more names product extraction plans into a frozen generation, the more
of row three becomes row two.

D3 is scoped to legal producer output on purpose. Outside the certified range
the obligation drops from *decode correctly* to *refuse safely* — D1 and D2 with
no fidelity claim. See [Certification bounds](#certification-bounds).

## How this design changed

This section records the inversion so that a reader who finds the old shape in
the code, in a test name, or in a linked issue can place it. It is history, not
contract.

**The previous design was a paired walker.** `CustomAttributeValueGuard` walked
the value blob and discarded the values, then handed the blob to
`CustomAttribute.DecodeValue`. Its central invariant, I1, required that the two
walkers skip exactly the same bytes. A second invariant, I2, required the guard
to bound quantities that drove *SRM's* cost.

**I1 and I2 are deleted, not weakened.** I1 has no second party. I2's allocation
half is D1's last clause; its time half — SRM's `Θ(P × G)` per-argument re-skip
of the generic context, filed as #5098 — disappears because SRM never runs. I3
survives verbatim as D1's cost clause.

**Why the old shape was the root cause.** I1 is a differential property of two
parsers where the second is external, changes across runtime versions, exposes
no cursor, and allocates from the declared count with no hook. Confirmed against
dotnet/runtime `main` on 2026-09-03: `DecodeArrayArgument` reads the `Int32`,
checks only `-1`, `0`, and `< 0`, and calls `ImmutableArray.CreateBuilder(count)`;
`DecodeNamedArguments` does the same on the `UInt16`. dotnet/runtime#57531 asked
for a bound and was closed as a caller obligation.

Worse, I1 was only establishable in one direction. Every available signal —
provider call sequence, decoded values, an appended sentinel — was an observation
of *SRM*, and SRM's behavior does not depend on where the guard landed. A guard
that over-consumed simply ran out of bytes, reported truncation, and was mapped
to "safe"; provider calls, values, and sentinel all matched the aligned case
exactly. An oracle cannot report where its counterpart stopped reading. The
direction that was invisible was precisely the one in which the guard had
already said yes.

The cost of keeping two walkers aligned by inspection was eight recorded gaps, a
six-round oracle PR that never converged (#5148), and seven rounds on a six-line
change (#5450) in which every round found a hole in the previous round's gate and
none found a product defect.

**The unexamined premise.** The previous document argued that the repository's
preferred structural-containment shape — a constrained type whose construction
establishes an invariant — was unavailable here "because the operation is inside
SRM." The operation does not have to be inside SRM. That premise, not any
individual gap, was the root cause.

**The withdrawn conclusion.** The prior-art survey below concluded that a
pre-walk charging declared counts before SRM allocates was "the only available
mitigation." That conclusion is withdrawn. The survey's own table names the other
mitigation twice: Roslyn owns its decoder, and CoreCLR's `ParseCaValue` appends
into a dynamic array rather than sizing one upfront. Issue #5341 already states
the principle for narrow sites — a walker that *replaces* the decode carries no
agreement obligation. This design generalizes it.

### Prior art: there is no upstream bound to inherit

Four of the five mainstream .NET consumers of custom-attribute blobs allocate on
the attacker-declared count before reading a single element.

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
decode path consults it before sizing an allocation. So D1's allocation clause is
not redundant with an upstream check; there is no upstream check. That absence is
a closed decision rather than an oversight: dotnet/runtime#57531 asked for
exactly such a check — "a way to detect this problem prior to allocating the
array" — and was closed without one.

Read the table for what it says about *ownership*, not only about bounds. Two of
the five consumers own their decoder, and the one that bounds the count is the
one that never sizes an allocation from it. Owning the decode and bounding the
allocation are the same decision.

**Reported outside this repository.** The narrow upstream fix — in
`DecodeArrayArgument`, before `CreateBuilder`, `if (count > blobReader.RemainingBytes)
throw new BadImageFormatException();` — is sound for every element type with zero
API change. It does not reach the pinned runtime, so it is not this repository's
fix.

### Worked example: dotnet/runtime#57531

This case motivated the original guard and still motivates D1's allocation
clause, so it is retained. What changed is what it *proves*.

The issue was filed against SRM in August 2021 by a NuGet engineer scanning
packages on nuget.org — the same tool category as this one. A shipping package on
the feed drove the reporter's scanner to a 28,517 MiB allocation:

```text
Reading attribute 'RegisterPageBuilderLocalizationResourceAttribute'... found bad image format!
Memory is at 28517.114097595215 MB
BIG MEMORY!
```

The mechanism, in concrete terms. In `Kentico.Content.Web.Mvc.dll`, the
constructor is `RegisterPageBuilderLocalizationResourceAttribute(System.Type
markedType, params string[] cultureCodes)`. The reporter's provider answered
`false` for `IsSystemType` on the first argument, so SRM took the enum path,
consulted `GetUnderlyingEnumType`, and got a hardcoded `Int32`. Four bytes were
consumed where a length-prefixed `SerString` should have been: the length byte
and the first three characters of the type name. The following `string[]` element
count was then read from the middle of that name — `"tico"`, `0x6F636974`,
1,868,786,036 declared slots, which at the guard's per-slot charge is 28,515 MiB.
**The blob is entirely legal. Only the classification is wrong.**

The reporter counted the problem in "over 3000 packages on NuGet.org (and 8000
contained assemblies)", a list that includes Microsoft's own `Microsoft.ML.*`
assemblies. The closing comment attributed the failure to the consumer's provider
and suspected "enums that have an underlying type that is not `Int32`". That
mechanism does not fit this attribute: the constructor declares no enum parameter
at all. Classification is the trigger; width is only what the drift costs once
classification is already wrong.

**What this proves under the current design.** Not that two walkers must agree —
there is one walker now. It proves that **a resolution mistake and an unbounded
allocation are separable failures, and only the second one is catastrophic.**
Our decoder can misclassify this argument exactly as the reporter's provider did.
The consequence would be a wrong rendered value for the attribute — a D3 fidelity
defect, visible and fixable. It could not become a 28 GiB allocation, because the
count read out of the middle of `"tico"` exceeds the remaining bytes of an 80-byte
blob and D1's allocation clause refuses it without consulting the parse at all.

That is the argument for stating the allocation clause independently rather than
deriving it from correct parsing. `CustomAttributeValueGuardTests`'s
`SystemTypeArgumentReadAsEnum_ChargesTheAmplifiedCount_AndIsUnsafe` gates the
refusal over the captured 80-byte blob, paired with
`SystemTypeArgument_FromShippedAttribute_DecodesAndStaysBounded` for the fidelity
half; the two differ only in the first parameter's declared type.

Read the refusal gate for what it proves and no more. The per-slot charge
saturates at `int.MaxValue` for any count above 134,217,727, and all four
plausible misread widths land on four bytes of this type name that exceed it, so
the charge assertion cannot distinguish the offset named above from a different
misread. It gates amplification-and-refusal; the offset itself is pinned
separately as an assertion over the captured bytes.

## Classification is a display-name comparison, not an identity test

Deciding whether a fixed argument is a `System.Type` selects the reading rule: a
length-prefixed `SerString` if it is, the enum width if it is not. With one
walker this decision is made once, in one place, so it can no longer *diverge*.
It can still be *wrong*, and the ways it can be wrong are worth recording,
because the previous design's seven-round attempt to share this predicate
established them.

The comparison is against the rendered string `"System.Type"`. That is not an
identity test, and two distinct facts make the gap real rather than theoretical:

- **Four metadata layouts render identically.** The name may arrive on a
  `TypeReference` or a `TypeDefinition` row, and on either row it may be split
  across the namespace and name columns (`System` + `Type`) or spelled dotted in
  the name column with the namespace nil (`System.Type`, namespace nil). All four
  render `System.Type`. A test that pins one layout leaves the other three
  unexercised, which was demonstrated by a mutation that left 2,459 tests green
  while genuinely diverging. Note that the axis is the **name layout**, not the
  row kind: crossing by `TypeReference`/`TypeDefinition` alone does not catch it,
  because splitting `System` + `Type` keeps the namespace column populated on
  both.
- **An assembly may define its own `System.Type`.** `namespace System { public
  enum Type : long { A = 1 } }` compiles clean — the compiler emits CS0436 and
  binds references in that assembly to the *local* type. An attribute constructor
  taking that parameter is spelled `ELEMENT_TYPE_VALUETYPE` + `TypeDef`, and an
  argument is encoded as its eight-byte underlying value. Verified against a
  built assembly: the ctor signature is `20 01 01 11 10` and the blob is
  `01 00 88 77 66 55 44 33 22 11 00 00`. SRM resolves that width through the
  handle, so a provider answering `Int32` consumes four bytes and then throws
  `BadImageFormatException` on the remainder — the width error surfaces as a
  structural failure one argument later, not as a wrong value in place.

Both are **D3 fidelity** concerns, and neither is a safety concern, because D1's
allocation clause holds independently of how this predicate answers. Record them
here so that a future reader does not re-derive the safety framing the inversion
removed.

## Enum width resolution

Enum width resolution is unchanged by the inversion except that it now has **one
consumer**. Handle-first structural resolution, then the name resolver, then
`Int32`.

`Int32` is the **last** fallback, not the answer for every unknown name. Say
"resolves to `Int32` when structural, local-name, and trusted-external
resolution all fail" rather than "unknown names resolve to `Int32`."

### Handle-typed enums resolve from the handle

When an argument is spelled `ELEMENT_TYPE_VALUETYPE` or `ELEMENT_TYPE_CLASS`
followed by a coded handle, the width is resolved **directly from the handle** —
`EnumUnderlyingPrimitive.TryResolveDefinition`, then `FromDefinition`.

Keeping this path handle-keyed is load-bearing, and routing it through a resolver
would break it: a resolver is keyed by name, a name is a flattened spelling, and
a flattened spelling discards the resolution scope that distinguishes two
definitions or an external reference from a local type. Issue #4914 was exactly
that collapse.

Distinct definitions can render to one string. A nested type joins its declaring
type with `.`, exactly as a namespace joins a type name, so a nested `Kind`
declared in `Samples.E` and a top-level `Kind` in namespace `Samples.E` both
render `Samples.E.Kind`. A reference additionally carries a resolution scope that
its flattened spelling discards. Any name-keyed index must therefore drop one
colliding definition. `NestedTypeNameCollision_GuardSkipMatchesDecodeWidth` gates
both handle forms and `CollidingTypeDefNames_EachResolveTheirOwnWidth` gates the
premise.

Structural matching walks a reference's nested scope chain but does not consult
its terminal assembly or module scope, so a reference whose chain matches a
definition in this reader resolves to that definition even when it nominally
denotes another assembly. That is long-standing behavior, gated by
`TypeRefEnumMatchingLocalInt64_SeesFollowingArrayCount`.

> **Rule.** The handle path must stay handle-keyed. Do not "simplify" it to go
> through the resolver.

**The handle path is not unconditionally structural.** When `TryResolveDefinition`
fails — an unresolvable or external `TypeRef` is the ordinary case — the decoder
renders the handle to a name and falls through to the name path, inheriting its
rules. Treating every handle-typed enum as structurally resolved is wrong in
exactly the population most likely to be hostile: references into assemblies that
are absent or attacker-named.

### Serialized-name enums resolve by projected name

When an argument is spelled as a serialized enum (`0x55`) and a `SerString`, the
width must come from a name.

**Name projection happens before the width lookup.** A blob-authored serialized
name is reflection syntax; the width table is keyed on metadata spellings. The
projection (`ProjectSerializedEnumName`) is applied first, which matters for names
that only parse once an assembly suffix is removed. This was previously required
so that the guard and SRM asked the same question; it is now simply the correct
question to ask.

A handle-derived name is an exact metadata spelling, and metadata names may
contain characters a reflection type name treats as escapes, so it is matched by
its exact spelling before its reflection-normalized one. A blob-authored name is
reflection syntax whose escapes are meaningful — `E\+Kind` names the metadata type
`E+Kind`, not one spelled with a backslash — so it is normalized first and never
matched verbatim.

That classification belongs to a **single pending lookup, not to a spelling.**
The provider records only that the name it produced most recently came from the
blob, and clears that mark when it produces a handle-derived name. Remembering
spellings instead would let a blob-authored occurrence change how a later
handle-derived occurrence of the same spelling resolves, making a consumed width
depend on argument order.

A repeated enum name resolves once rather than once per array element, because
the element count is attacker-chosen and per-element resolution is the
amplification D1 exists to prevent.
`EnumArrayElements_ResolveTheWidthOncePerName` and
`EscapedTypeDefEnumName_GuardSkipMatchesDecodeWidth` gate these.

### The two resolution paths are not symmetric

**A fixture exercising one path proves nothing about the other.** Issue #4914 was
a name-path collapse: distinct type definitions that rendered to the same display
name shared one index entry. The fix resolves definition-typed enums from their
handle. Any change to either path must state which path it changes and must not
assume symmetry between them.

A cross-assembly `TypeRef` that fails structural resolution can still match a
*local* definition whose flattened spelling collides with it, and take that
definition's width. The inspected image therefore has some influence over the
width chosen for a name it does not define. Under the inversion this is a **D3
fidelity** risk with no safety component;
`ExternalReferenceCollidingWithNestedName_IsRefusedNotDecoded` pins the case.

### Frozen cross-assembly enum-width adapter

Custom-attribute enum width can consume one frozen
[`TypeResolutionContext`](type-forwarding-resolution.md) through
`TypeResolutionEnumWidth`: planned serialized names become structured requests,
`Resolve` locates an already-retained defining image, and the resolved
definition's authenticated kind plus
`TypeResolutionContext.TryGetEnumUnderlyingType` establish a sealed
core-library-derived `System.Enum` definition and read its single valid `value__`
field without exposing a reader. Reflection-name escapes are projected back to
exact metadata namespace and type segments.

Explicit assembly qualifiers stay constraints rather than widening to wildcards:
an explicit `Culture=neutral` is spelled so it cannot match a culture-specific
candidate, and an explicit `PublicKeyToken=null` names an unsigned assembly.
Because an empty token reads as a wildcard during binding, the adapter records it
on the request and then drops a resolved candidate that turned out to be signed,
keeping the qualifier a constraint without changing the identity contract that
`AssemblyDependencyResolver` and `MetadataSource` also consume. The qualifier
constrains the assembly the reference bound to, so when forwarding hops were
followed the narrowing inspects the first hop's source rather than the terminal
definition.

A definition that is not a CLI-valid enum — unsealed, not directly derived from
`System.Enum`, generic, carrying a non-public, non-special, or literal `value__`,
or carrying a non-literal static field — supplies no width. Unplanned, unbound,
malformed, or callback-ambiguous names stay `Int32`.

Product extract does not yet collect custom-attribute enum names into a
generation; that remains residual on
[#4741](https://github.com/richlander/dotnet-inspect/issues/4741).
`TypeResolutionEnumWidthTests` gates the adapter.

## Bounds, and what each one actually does

The two numeric bounds in this component are frequently conflated. They are not
the same kind of bound and they do not have the same consequence.

| Bound | Value | Effect |
| --- | --- | --- |
| `MaxSerializedDepth` | `SignatureBlobGuard.DefaultMaxDepth` (512) | **Refuses.** Boxed/`SZARRAY` nesting past this depth yields `null`. |
| `EnumUnderlyingPrimitive.MaxNestingDepth` | 128 | **Stops matching, then falls back.** `Matches` returns `false` past this depth; resolution continues down the name path. The decode is not refused. |

`MaxSerializedDepth` is a safety bound. `MaxNestingDepth` is a termination bound
on a recursive structural comparison, protecting against an uncatchable native
stack overflow. Exceeding it does not refuse anything; it degrades structural
matching to name-based resolution.

> **Do not describe `MaxNestingDepth` as refusing a decode.**

`MaxSerializedDepth` also caps chains the decoder walks, which has a practical
consequence for tests: a fixture built with more than 512 custom modifiers is
refused before any behavior under test is reached.

The decoder walks the value blob iteratively on an explicit heap-allocated work
stack rather than recursively, so a deeply nested blob cannot overflow the native
stack before any bound is consulted.

### What a declared count can claim

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

## Enforcement gates

**Current state: `unverified` for all three.**

| Invariant | Gate | State |
| --- | --- | --- |
| **D1** | A generative gate varying the attacker-controlled dimensions jointly. Tracked as #5733. | Does not exist. Four hand-written amplification regressions pin four instances; four open defects violate it. |
| **D2** | Slice 2's differential test: `null` wherever SRM throws, plus every existing guard and reader test green. **Blind to the `Int32` width default**, which SRM guesses identically; that belongs to D3. | Lands with slice 2. |
| **D3** | The #5148 generator re-targeted from offset agreement to **value equality**, plus the stage-1 corpus below with zero refusals. **Must include the producer-truth width cases where an SRM oracle would be degenerate** — a non-`Int32` enum resolved across an assembly boundary from a *retained* defining image — and must not credit an SRM run that consulted the same adapter. Widths no path can resolve are carved out, not gated; see [D3](#d3--fidelity). #5065 is retitled to D3 by #5288 slice 4. | #5148 open; stage 1 not landed. The oracle's SRM version is unpinned and must be chosen; see [Certification bounds](#certification-bounds). |

Until those gates exist, any statement in this document that an invariant
*holds* is unverified in the sense of [Asserted properties name their
gate](../evidence-and-validation.md#asserted-properties-name-their-gate).
Statements about what the invariants *require* are normative regardless.

### What the D1 gate must vary

A one-dimensional check is misleading: raising a single declared count while
holding everything else fixed is exactly the measurement the existing
per-element memoization already passes. The gate must vary jointly, at minimum:

- declared `SZArray` element counts;
- declared constructor parameter counts;
- the constructor's generic arity;
- **the *sequence* of `VAR` indices, and of handles and names, not merely how
  many appear.** Every memo in the decoder is a single slot keyed on the
  *previous* input, so a run of identical values is cheap while two values in
  alternation evict on every step. Only the repeating shape is currently tested,
  which is why issue #5130 passes all four existing amplification regressions;
- the number of *distinct* handles and names referenced;
- the number of rows in the tables a failed resolution scans;
- the number of attributes decoded from one image; and
- the size of a blob shared across many attribute rows. `A` attribute rows may
  name one `B`-byte value blob, which the blob heap stores once; each row is
  nevertheless decoded independently, so `Θ(A + B)` of metadata yields `Θ(A × B)`
  of work and retained value.

A dimension list is not enough on its own: several of these are only adversarial
in combination with a particular *arrangement*, not at a particular *size*. The
generator must control shape as well as magnitude, and must assert that total
work — signature reparses, name renderings, definition scans, enum-width
resolutions, and structural match steps — stays near-linear in total metadata size
across the product of those dimensions, not merely flat along any one of them.

Seed the corpus with the regressions already found by hand so the gate is
demonstrably non-vacuous, and pin any failing seed as an ordinary case.

### What the D2 and D3 gates must generate

The generator must cover, and be able to combine:

- **Fixed-argument types:** each primitive; `string`; `object` (boxed);
  `System.Type` as a serialized name; `SZARRAY` of each of these; and `VAR` where
  generic substitution applies. `MVAR` is a **refusal** case, not a substitution
  case, and must not appear in the cost cases above.
- **Enum spellings:** the handle forms and the serialized (`0x55`) name form, each
  independently, because they resolve differently. The handle forms must be
  generated with **both** `ELEMENT_TYPE_VALUETYPE` and `ELEMENT_TYPE_CLASS`, each
  with a `TypeDef`, a `TypeRef`, a `TypeSpec`, and a nil handle. Metadata is
  untrusted, so the gate cannot assume an enum is spelled legally.
- **Classification layouts:** `System.Type` reached through each of the four
  metadata layouts named under
  [Classification](#classification-is-a-display-name-comparison-not-an-identity-test),
  and a hostile assembly-defined type rendering the same string.
- **Nesting:** boxed and `SZARRAY` nesting at, just below, and just above
  `MaxSerializedDepth`.
- **Custom-modifier chains** preceding element types, at and above the signature
  depth bound.
- **Named arguments:** fields and properties, including the serialized-enum and
  array-typed forms.
- **Declared counts:** valid, zero, negative, `-1`, and counts far exceeding the
  remaining bytes.
- **Malformed extensions:** truncation at every structural offset; unknown
  element-type codes; a generic constructor header.
- **Metadata states:** distinct `TypeDef` rows that render to one display name; a
  `TypeRef` whose flattened spelling collides with a local definition;
  unresolvable `TypeRef`s; nested-type chains deeper than the match bound.
- **Observer states:** absent (`null`) — the common case, and the one that makes
  charging no bound at all — non-throwing and recording, and throwing. The
  assertion is that an observer raising to stop the walk never yields a value and
  is never absorbed into a default width.

**A TLA+ model is not the right instrument here**, and the repository's existing
model is the useful contrast.
[`docs/models/package-realization-admission/`](../models/package-realization-admission/README.md)
models exact-request-keyed admission and lease-scoped lifetime: genuinely
stateful, concurrent, and full of interleavings a test cannot enumerate. This
property is a fidelity property of one sequential parser over one grammar, with no
concurrency and no scheduling. Generating real blobs and running the real decoder
is both cheaper and more honest.

## Certification bounds

Two version axes, behaving oppositely.

**SRM** is the oracle, not the counterpart. It is **not** pinned today, and the
D3 gate must choose its oracle rather than inherit one. `Directory.Build.props`
resolves `net10.0` only for `OfficialBuild` non-AOT builds — the single managed
fallback package — while development, tests, and the RID-specific Native AOT
builds take `net$(NETCoreAppMaximumVersion)`. The AOT build that statically links
SRM is therefore the one that does *not* get `net10.0`, and the gate runs on the
latest TFM. Slice 3 must either name the oracle's runtime explicitly or run D3
against each supported SRM version. Naming it states which decoder D3 is measured
against; it no longer under-specifies a safety invariant, which is what leaving it
implicit would have done under the previous design.

**The producer toolchain** cannot narrow D1 or D2, because the adversary does not
use an SDK. It narrows D3: the must-approve set is what compilers in the certified
range actually emit, and everything outside it drops from *decode correctly* to
*refuse safely*.

| Claim | Domain | Narrowable by producer version? |
| --- | --- | --- |
| D1, D2 | all byte sequences | No |
| D3 fidelity | real producer output, against producer truth (SRM arbitrating where it can) | Yes |
| No spurious refusal | real producer output, certified SDK range | Yes |

**Stage 1** sweeps every custom attribute in a pinned real-package corpus built by
SDKs in the certified range and asserts zero refusals and value equality with SRM,
reusing the baseline machinery under `tools/DecompilerHarness/corpus/`. It must
additionally carry the **producer-truth width cases** described under
[D3](#d3--fidelity) — cross-assembly non-`Int32` enums decoded without their
defining reference — because SRM equality is vacuous there. **Stage 2**
(#5304) is the exhaustive per-position enumeration with `MustApprove` /
`MustRefuse` / `KnownGap` dispositions; after the inversion its obligation outside
the certified set is D1 and D2 only.

> **The certified range must never leak into the decoder's code.** It is a claim
> about what we certify, not a license to skip a check because no real SDK emits a
> shape.

Failure direction: a future SDK emitting a spelling outside the certified set
produces a refused legitimate attribute — a fidelity regression, not a safety one.
Certification is versioned and re-runnable, so adding SDK N+1 is a corpus run, not
a redesign.

## Known gaps

Each row is a **verified** divergence between the contract above and the
component's current behavior. They are listed rather than omitted, because a
design document describing only intended behavior would misrepresent the
component.

| # | Gap | Invariant | Issue |
| --- | --- | --- | --- |
| 1 | A failed resolution scans every type definition, so `P` distinct unresolvable arguments cost `Θ(P × T)`. Applies to **both** the handle path and the serialized-name path (`TryFindDefinition`). | D1 | #5091 |
| 2 | `SZARRAY` element types are re-parsed once per element rather than once per array. | D1 | #5047 |
| 3 | Every memo is a **single slot keyed on the previous input**, so alternating two values defeats all of them. | D1 | #5130 |
| 4 | `A` attribute rows sharing one `B`-byte blob are decoded independently, costing `Θ(A × B)` in work and retained values from `Θ(A + B)` of metadata. Absent a shared `MaterializationContext`, each `TryDecode` also rebuilds the type-definition index, adding `Θ(A × T)`. | D1 | #5132 |
| 5 | A caller observer's `BadImageFormatException` or `ArgumentOutOfRangeException` is caught by the malformed-metadata handler, so a budget stop is mistaken for a malformed blob. | D2 | #5085 |
| 6 | `TryDecode` swallows `OutOfMemoryException` through a bare catch, which is exactly the laundered exception D2 forbids. | D2 | #5397 |

Gaps 1, 2, and 3 share a root cause worth naming: **memoization was tuned against
the wrong cost model.** Under the paired-walker design, work the guard cached and
SRM repeated made the guard look fast while the decode stayed quadratic, and work
the guard repeated made the guard quadratic while the decode was fine. Neither
side's profile revealed the other's, which is why these were found by reading
rather than by measurement. With one walker there is one profile, and that is a
real simplification — but the fixes are still owed, and a fix that makes a memo hit
more often without making it hit on *every distinct input* has not resolved any of
them. Prefer one coherent change over three local optimizations.

Gap 4 is deliberately excluded from that grouping: it is cross-row, so no per-walk
memo can address it. Gaps 5 and 6 are both D2 laundering defects in the same
`catch`-shaped surface and should be fixed together.

### Gaps closed by the inversion

Retained so that a reader who finds these issues, or a test named for one, can
place it.

| Former gap | Was | Disposition |
| --- | --- | --- |
| SRM re-derives each fixed argument's type from the generic context, costing `Θ(P × G)`; the guard memoized the offset and never experienced it. | I2 (#5098) | **Moot.** SRM never runs. |
| The resolver-less `IsSafeToDecode` overload resolves widths in a different order, so its `true` does not carry I1. | I1 scope (#5120) | **Moot.** There is no alignment claim to carry. |
| The guard and `ArgTypeProvider` each apply their own `"System.Type"` comparison, so the predicate can diverge. | I1 (#5393) | **Moot.** One decoder, one predicate. Recorded as a fidelity caution under [Classification](#classification-is-a-display-name-comparison-not-an-identity-test). |
| Whether the #4914 width-alignment collapse remains reachable on the blob-authored name path. | I1 (#4992) | **Moot as an alignment question.** The name path's own collapse risk is retained as a D3 concern under [The two resolution paths are not symmetric](#the-two-resolution-paths-are-not-symmetric). |

## Open work

| Issue | Concern |
| --- | --- |
| #5288 | This inversion. Slice 2 (the decoder), slice 3 (the D3 gate), and slice 4 (cleanup) are outstanding. |
| #5047 | Per-element element-type replay; resolve once and loop. Gap 2. |
| #5065 | The differential oracle. To be **retitled to D3** by #5288 slice 4; it is not D1's gate. |
| #5085 | An observer exception mistaken for malformed metadata. Gap 5. |
| #5091 | Quadratic work across declared parameter count and type-definition count. Gap 1. |
| #5130 | Every memo is a single slot, so alternating input defeats all of them. Gap 3. |
| #5132 | Quadratic cost across attribute rows sharing one value blob. Gap 4. |
| #5148 | The differential generator, to be re-targeted from offset agreement to D3 value equality. |
| #5304 | Stage 2 exhaustive per-position enumeration. |
| #5397 | `TryDecode` swallows `OutOfMemoryException` through a bare catch. Gap 6, D2. Retained rather than closed; see [D2](#d2--fail-closed-visibly). |
| #5733 | The D1 generative bounded-cost gate. Filed from review of this slice, because D1 previously named #5065, which does not measure cost. |
| #4879 | Enum constants whose signature does not match `value__`. Fidelity. |
| #5062 | Signature decode laundering internal errors into `SignatureRejected`. |
| #4741 | Product extraction does not yet plan custom-attribute enum names into a frozen type-resolution generation. |

Issue #5067 tracks this space as a whole.
