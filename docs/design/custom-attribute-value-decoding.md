# Custom-Attribute Value Decoding

`dotnet-inspect` reads custom-attribute values out of assemblies it did not
produce. A custom-attribute value is a byte blob whose meaning is not
self-describing: the blob carries values, and the *constructor signature* — a
separate blob, in a separate table — says how wide each value is. Decoding one
requires reading two attacker-supplied structures together and agreeing about
where every element begins and ends.

**We own that decode.** This document owns the contract that makes it safe.

**Status: slice 2 implementation candidate; D1/D2/D3 gates still open.** This
document states the contract established by
[#5288](https://github.com/richlander/dotnet-inspect/issues/5288), which inverts
this subsystem's relationship to `System.Reflection.Metadata`.
Slice 1 is this document; the current slice 2 candidate is the owned decoder:
`AttributeDecoder.TryDecode` now consumes `CustomAttributeValueGuard`'s owned
walk directly and no production path calls `CustomAttribute.DecodeValue`. The
paired-walker hazards described under
[How this design changed](#how-this-design-changed) are gone from the code.
The remaining slices are the D3 gate (slice 3) and cleanup (slice 4); D1's
generative cost gate (#5733) and D3's value-equality gate (#5148) are still
open, so the invariants below remain the target the implementation is held to
rather than gated facts.

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
- **Consumes** `System.Reflection.Metadata` as a *test-time oracle only*. No
  production path calls `CustomAttribute.DecodeValue`; slice 2 removed the last
  one and deleted the `ArgTypeProvider` that drove it. The owned decoder
  materializes SRM's public `CustomAttributeValue<string>` shape itself.

## Non-claims

- Not a general signature-decoding contract. Method, field, and property
  signature decoding are owned elsewhere.
- Not a defense against local actors, our own code, or other contributors. See
  [Trust boundaries](untrusted-data-threat-model.md#trust-boundaries).
- Not a promise that attribute values are *correct*, only that reading them
  cannot be turned into an unbounded or out-of-bounds operation, and that on
  output from compilers in the certified range they match what the producing
  compiler encoded — except for the one width case D3 explicitly carves out.
- Not a promise that our decoder matches SRM on **illegal** input. The D2 gate
  is one-directional and narrower than it sounds: `null` wherever SRM throws.
  The converse — that we produce a value wherever SRM does — is a D3 claim, and
  only over certified-range output. On illegal input the obligation is D1 and
  D2 only.
- Not a change to the decoder's **output shape**, and not a redesign of its
  public surface. It produces the same `CustomAttributeValue<string>` it
  produces today, and every existing overload keeps its **signature** and its
  **successful-result shape**. Three deliberate behavior corrections are carved
  out and are not compatibility violations: observer exceptions must stop
  propagating through the malformed-metadata catch (#5085),
  `OutOfMemoryException` must stop being swallowed (#5397), and a caller
  resolver's own `BadImageFormatException` must stop being reported as a
  malformed blob (#5759). All three are defects this contract names, so
  preserving them is not an option. Separately, the
  defaulted-width signal D2 requires is **additive** surface — a new overload or
  a richer observer — because the current observer is `Action<int>` and cannot
  carry it. A caller that wants the signal must opt in.

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

> **D1 — Bounded.** Transient algorithmic work — signature reparses, definition
> scans, width resolutions, structural match steps — is near-linear in the size
> of the metadata the decoder is given, across every attacker-controlled
> cardinality *jointly*. Materializing the output is bounded separately and is
> not near-linear: the number of materialized slots is bounded by the value
> blob's length, and retained memory is `O(B + S × (C + N))` for blob length
> `B`, slot count `S`, per-slot constant `C`, and longest rendered type name
> `N` — polynomial in the bytes supplied, not a constant multiple of them.
> **No allocation is ever sized from a declared count that exceeds the
> remaining bytes.**
>
> **D2 — Fail closed, visibly.** A blob whose *structure* the decoder cannot
> follow yields `null` — never a partial value, never a laundered exception,
> never a plausible-looking guess about where the next element begins. Where the
> decoder must guess a width to proceed, the guess is reported alongside the
> value, out-of-band from `CustomAttributeValue<string>`.
>
> **D3 — Fidelity.** On output from compilers in the certified range, decoded
> values equal the values the producing compiler encoded. SRM arbitrates
> wherever its resolution path is independent of ours; where it is not, the
> certified corpus supplies producer truth directly. A width that **no** path
> can resolve, because the defining image is absent, is carved out of D3
> entirely — see [D3](#d3--fidelity).

D1 is about cost. D2 is about what happens when the input wins. D3 is about
being right on input that is not an attack.

### D1 — Bounded

D1 has two clauses that are usually conflated, and separating them is the point
of the invariant.

**The cost clause** is the old I3, narrowed: near-linear aggregate *transient*
work across
declared element counts, declared parameter counts, generic arity, distinct
handles and names, the number of rows a failed resolution scans, the number of
attribute rows decoded from one image, and the size of a blob shared across
many rows — *jointly*, because the attacker chooses them together. Bounding each
dimension separately is not sufficient.

The narrowing matters, and it is not a weakening. Writing the output is itself
work, and you cannot retain a byte without writing it, so the memory clause below
puts a floor under total work: because retained bytes are polynomial, *total*
work is polynomial too. Claiming near-linear total work would contradict the
memory clause outright. What is near-linear is everything the decoder does
*besides* emitting its result — which is where every amplification found so far
has lived, and the only part a gate can meaningfully hold to a linear standard.

The transient clause is a target, and gaps 7 and 8
([#5757](https://github.com/richlander/dotnet-inspect/issues/5757),
[#5758](https://github.com/richlander/dotnet-inspect/issues/5758)) are two
instances of the same class: `Θ(rows × name length)` work from
`Θ(rows + name length)` metadata. The rule is stated over cost, not over one
implementation symptom:
**no per-row operation on the resolution path may cost more than `O(1)` in the
length of a name that is invariant across the loop.** Rendering violates it;
so do content comparison and per-row hashing. The linked issues own the measured
evidence and repair analysis.

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

That per-array argument extends to nesting without weakening. "Every element
costs at least one byte" is a statement about one array, but every slot at every
level corresponds to a *distinct* consumed byte, because a nested array's own
elements consume bytes its parent has not already counted. Slots summed over all
arrays at all depths are therefore bounded by bytes consumed, and the clause
holds for jagged and nested shapes by the same argument rather than needing a
separate depth budget.

**The clause is about amplification, not about allocation in the abstract.**
State it precisely, because two stronger-sounding versions are both wrong.
"`OutOfMemoryException` has no path to arise" is unachievable — a large enough
legitimate blob can exhaust a small enough host, and no parsing discipline
prevents that. "No attacker-declared quantity can produce an
`OutOfMemoryException`" is also wrong, because the element count `N` *is*
attacker-declared and does size the result; what makes it safe is that the
attacker must supply `N` bytes to declare it.

The enforceable claim is therefore about **which cardinality can be declared
without being paid for**: no allocation is sized from a count the blob has not
backed with bytes. `count > RemainingBytes → null` is what establishes it, and
it holds independently of whether the parse is correct. It is *not* a claim that
retained memory is a constant multiple of anything — see the two denominators
below.

**The two bounds have different denominators.** Slot *count* is bounded by the
value blob's length, which defeats #57531. Retained *bytes* are not:
`CustomAttributeTypedArgument<string>.Type` retains a rendered name whose
length comes from the metadata string heap, and metadata may share name
structure that the output repeats. The bounds D1 can assert are therefore:

- **slot count** ≤ the value blob's length — linear, and the property that
  defeats #57531; and
- **retained bytes** = `O(B + S × (C + N))`, for value-blob length `B`, slot
  count `S`, a fixed per-slot constant `C`, and longest rendered type name `N`.

`C` covers per-slot storage such as references, boxing, and the containing
array. The additive `B` term covers values copied from the blob, including
serialized strings. The product term follows from the held
`CustomAttributeValue<string>` output shape, which repeats rendered names
rather than preserving shared metadata structure. The contract admits that
term instead of recording unavoidable output representation as a defect.
[#5755](https://github.com/richlander/dotnet-inspect/issues/5755) records the
evidence and remains the place to revisit the bound if the output-shape hold is
lifted. D1 still forbids a small blob from sizing a large allocation through
`count > RemainingBytes`.

**Charging is now materialization accounting.** Under the previous design the
`beforeMaterialize` observer reported work the guard had *declined* to do, so a
large charge could appear next to a refusal and look like allocation accounting.
With one walker that actually materializes, the charge means what its name says:
this is what we are about to allocate. Keep it, and keep it before the
allocation. The non-production `IsSafeToDecode` compatibility bridge makes no
output allocation but reports the equivalent prospective charge stream while
running the same parser in validation mode.

**Charging is not an escape hatch for the cost clause.** The observer is
optional and frequently absent, and `Charge` returns immediately when it is
null. Work that is charged to nobody is unbounded work. A cost may be delegated
to an observer only where a caller is guaranteed to be present and its refusal
is guaranteed to stop the walk.

Elements of an `SZARRAY` share one element type, so any work reachable from
element-type parsing is multiplied by an attacker-chosen count unless it is
resolved once for the array. Issue #5047 tracks the current replay gap.

> **Standing rule.** Work introduced into element-type parsing is per-element
> work. Before adding a resolution, materialization, or validation step
> reachable from fixed-argument processing, establish that it is memoized or
> that it is cheap enough to be paid an attacker-chosen number of times.

### D2 — Fail closed, visibly

The decoder's output is a value, `null`, or an exception that is **not a
statement about the blob** travelling back to its caller. There is no partial
result, and no exception arising from the blob escapes as a decode outcome.

That division is the whole rule. An exception the *blob* provokes — malformed
metadata, a bad signature, exhausted bytes — is a decode outcome and must become
`null`. An exception that is not about the blob passes through untouched:
caller code raising from a callback and resource exhaustion such as
`OutOfMemoryException` are facts about the caller and the host, not verdicts on
the input.

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

The last row is exact: a caller's refusal propagates and never becomes a value.
That applies to **every** caller boundary. Whether an exception is a statement
about the blob depends on where it was raised, so every boundary that invokes
caller code must preserve that origin. Issues #5085 and #5759 record the
observer and resolver instances.

Resource exhaustion is a separate class. An `OutOfMemoryException` raised
inside materialization crosses no caller boundary, so callback provenance
cannot protect it. It propagates because it is not a malformed-input outcome;
issue #5397 records the current divergence.

**D2 is outcome-shaped, and only that.** Its whole content is which of three
outcomes a caller can observe: a complete value, `null`, or a propagated
exception that is not a statement about the blob — a caller's observer or
resolver failing, or resource exhaustion. Cost and allocation are [D1](#d1--bounded)'s, including the
`count > RemainingBytes` rule — that rule *produces* a `null`, which is why it
appears in the table above, but what it bounds is memory, and stating it as a
D2 claim as well would give a failing gate two invariants to cite.

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
  [D3](#d3--fidelity) for widths that *can* be resolved; where none can, the case
  is carved out of D3 and the residual is tracked as
  [#5742](https://github.com/richlander/dotnet-inspect/issues/5742).

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
| The value's type is decoded without consulting our resolution logic — primitives, strings, `System.Type` names, arrays of these | SRM equality. Cheap, broad, and sufficient. **Independent** is the load-bearing word: the oracle's `ICustomAttributeTypeProvider` must be test-owned and trivial. After slice 2 deletes `ArgTypeProvider` there is no product provider left to borrow, so this holds by construction. | Yes |
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
to "refuse; do not defer".

**What the carve-out costs is a success-shaped wrong value, and D2's refusal
machinery is not a backstop for it.** A real `Int64` enum read as `Int32`
under-consumes four bytes and relocates the cursor, and — because the format has
[no internal framing and no resync point](#the-formats-adversarial-properties) —
the remaining bytes can parse as a complete, structurally valid attribute. This
twelve-byte blob, which its producer wrote as prolog + one `Int64` + zero named
arguments, decodes under the fallback into one `Int32` argument and one
fabricated named argument, consuming the blob *exactly*:

```text
blob     01 00 07 00 00 00 01 00 53 02 00 00
producer fixed=[Int64 0x0253000100000007]  named=[]
fallback fixed=[Int32 7]  named=[Field name='' value=False]
```

No refusal fires, and an end-offset check cannot detect it either, because
consumption is exact. Whether drift surfaces at all is a property of the bytes
that follow, not of the decoder: the `System.Type : long` case recorded under
[Classification](#classification-is-a-display-name-comparison-not-an-identity-test)
happens to fail structurally one argument later, and that is luck, not design.

**So the guess must be visible, which is D2's "visibly" clause doing real work.**
A defaulted width is not a refusal and must not pretend to be one, but neither
may it be indistinguishable from a resolved width. **Slice 2 defines a
per-argument defaulted-width signal on the decoded result**, carried
*out-of-band* — alongside the returned value, not as a new field inside
`CustomAttributeValue<string>` — which is what lets it coexist with #5288's
hold on that output shape. Today no such signal exists —
`EnumUnderlyingPrimitive` returns the
defaulted `Int32` as an ordinary value — which is the gap
[#5742](https://github.com/richlander/dotnet-inspect/issues/5742) tracks.

**This obligation names its own gate, because no existing one covers it.** The
D2 differential is blind to the default (SRM guesses `Int32` identically) and
D3 carves the unresolvable case out, so slice 2 could otherwise report green
with the signal never implemented. Slice 2 therefore lands a test asserting the
signal is **set** for an argument whose width was defaulted and **clear** for
one whose width was resolved, on the same decode path. Until that test exists
the signal is `unverified`, and the enforcement-gates table says so.

What survives intact regardless is [D1](#d1--bounded): the misread cannot become
an unbounded or out-of-bounds operation. The failure mode is a confidently wrong
rendering, bounded in cost, and reported as uncertain once slice 2 lands.

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
half is D1's last clause. Its SRM-specific time claim disappears because SRM
never runs, but #5098's `Θ(P × G)` generic-context re-skip shape remains in the
owned decoder and is therefore an open D1 implementation gap. I3 survives
verbatim as D1's cost clause.

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

The cost of keeping two walkers aligned by inspection is recorded in the open
oracle work (#5148), the classification work (#5450), and the former gaps below.

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
because the previous design's attempt to share this predicate established them.

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

`MaxNestingDepth` also bounds D1 work: structural matching walks a reference
scope chain and a definition declaring chain once per candidate. The cap makes
that factor constant. Raising or removing it requires re-deriving the D1 bound;
issue #5733 owns the measurement and the requirement to sample capped dimensions
beyond their cap.

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

**Current candidate state: the defaulted-width signal is gated; D1, D2, and D3
remain `unverified`.**

| Invariant | Gate | State |
| --- | --- | --- |
| **D1** | #5733 varies attacker-controlled dimensions jointly, measures work rather than allocation, samples capped dimensions past their cap, and must be shown red against the pre-repair head. | Does not exist; six open defects violate it. |
| **D2** | Slice 2 classified and inverted the guard's deferral tests, and added explicit coverage for the defaulted-width signal, caller-boundary provenance (observer and resolver, including `BadImageFormatException` and `ArgumentOutOfRangeException`), and a malformed control. An internally originated `OutOfMemoryException` gate does not yet exist. | Partial in the slice 2 candidate; resource-exhaustion propagation remains unverified. |
| **D3** | #5148 is re-targeted from offset agreement to value equality; stage 1 adds producer-truth widths where an SRM oracle would share the decoder's resolution path. | #5148 open; stage 1 not landed. |
| **Defaulted-width signal** | #5742 asserts that the out-of-band per-argument signal is set for a defaulted width and clear for a resolved width on the same decode path. `DetailedDecode_ReportsDefaultedAndResolvedWidths` and `DetailedDecode_LegacyFuncIsAuthoritative_ButUnresolvedDefaults` gate it. | Gated in the slice 2 candidate. |

Until those gates exist, any statement in this document that an invariant
*holds* is unverified in the sense of [Asserted properties name their
gate](../evidence-and-validation.md#asserted-properties-name-their-gate).
Statements about what the invariants *require* are normative regardless.

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

**SRM** is the oracle, not the counterpart. It is **pinned by the TFM**. That it
is pinned at all is what matters here: it states which decoder D3 is measured
against, and no longer under-specifies a safety invariant, which is what leaving
it implicit would have done under the previous design.

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
[D3](#d3--fidelity) — cross-assembly non-`Int32` enums whose defining image the
workspace has retained — because SRM equality is degenerate there, the oracle
having consulted the same adapter. Widths no path can resolve are carved out of
D3 and are not stage-1 obligations. **Stage 2**
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
| 4 | `A` attribute rows sharing one `B`-byte blob are decoded independently, costing `Θ(A × B)` from `Θ(A + B)` metadata. | D1 | #5132 |
| 5 | An observer exception can be mistaken for malformed metadata and a one-shot budget refusal can become a value. | D2 | #5085 |
| 6 | Internal `OutOfMemoryException` is converted to `null`. | D2 | #5397 |
| 7 | Building the type-definition index costs `Θ(P × L)` for `P` definitions sharing an `L`-character namespace. | D1 | #5757 |
| 8 | A definition scan performs `O(L)` work per row on a loop-invariant name, costing `Θ(T × L)`. | D1 | #5758 |
| 9 | A caller resolver's exception can be mistaken for malformed metadata. | D2 | #5759 |

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
memo can address it.

Gaps 7 and 8 are a second class: a per-row operation on the resolution path
costs `O(L)` in a name length that does not vary across the loop. The rule is
**`O(1)` per row in any loop-invariant name length**. It applies to rendering,
comparison, hashing, and any future operation with the same cost shape.

Gaps 5 and 9 are a third class: caller provenance is discarded before
malformed-input handling. D2 divides exceptions by origin, so **every caller
boundary must preserve where an exception was raised**.

Gap 6 is deliberately excluded from that class. An `OutOfMemoryException`
raised inside materialization crosses no caller boundary; preserving callback
origin cannot satisfy the separate requirement that internal resource
exhaustion propagate.

### Gaps closed by the inversion

Retained so that a reader who finds these issues, or a test named for one, can
place it.

| Former gap | Was | Disposition |
| --- | --- | --- |
| SRM re-derived each fixed argument's type from the generic context, costing `Θ(P × G)`; the guard memoized the offset and never experienced it. | I2 (#5098) | **Transferred to D1.** SRM no longer runs, but the owned decoder currently re-skips the generic context per `VAR` argument and retains the same cost shape. |
| The resolver-less `IsSafeToDecode` overload resolves widths in a different order, so its `true` does not carry I1. | I1 scope (#5120) | **Moot.** There is no alignment claim to carry. |
| The guard and `ArgTypeProvider` each apply their own `"System.Type"` comparison, so the predicate can diverge. | I1 (#5393) | **Moot.** One decoder, one predicate. Recorded as a fidelity caution under [Classification](#classification-is-a-display-name-comparison-not-an-identity-test). |
| Whether the #4914 width-alignment collapse remains reachable on the blob-authored name path. | I1 (#4992) | **Moot as an alignment question.** The name path's own collapse risk is retained as a D3 concern under [The two resolution paths are not symmetric](#the-two-resolution-paths-are-not-symmetric). |

## What slice 2 decided

The inversion changed two contracts this document deliberately left open for
slice 2. Both are now settled.

- **The charge unit.** `beforeMaterialize` now reports work the decoder is
  about to do rather than work it declined to do. `DeclaredSlotCharge` survives
  at `16`, defined explicitly as a *legacy conservative decode-work-per-declared-slot
  proxy* used by existing budgets, **not** exact retained-byte accounting — D1
  admits `O(B + S*(C+N))` retained output and #5755 owns the representation
  evidence. The value is preserved because roughly twenty observer consumers
  budget against it and slice 2 does not retune them (#5733 owns the D1 cost
  gate that would). Serialized-string byte charges are preserved and charged
  raw (not slot-multiplied); existing type-name rendering and type-definition
  index charges are also preserved. Every count is validated before its slot
  charge, and every observer invocation is wrapped in a provenance sentinel so
  a throwing observer is never absorbed as malformed metadata.
- **The decoder's name.** The component keeps the name `CustomAttributeValueGuard`
  because roughly twenty call sites and the whole test suite reference it, but
  it is now the owned value-producing decoder rather than a guard.
  `IsSafeToDecode` remains public as a temporary, inverted-semantics bridge that
  runs the same walk in a non-materializing configuration and discards the
  value; it is not on the production `TryDecode` path. `ArgTypeProvider` is
  deleted as an SRM `ICustomAttributeTypeProvider`; its rendering and resolution
  fold into the decoder's `Classifier`. The additive defaulted-width signal
  rides a new `AttributeDecoder.TryDecodeDetailed` returning
  `DetailedCustomAttributeValue`, with a new `EnumWidthResolver` delegate for
  callers that must report a width unresolved. Slice 4 retires the remaining
  guard-era test names.

## Open work

| Issue | Concern |
| --- | --- |
| #5288 | This inversion. Slice 2 (the decoder), slice 3 (the D3 gate), and slice 4 (cleanup) are outstanding. |
| #5047 | Per-element element-type replay; resolve once and loop. Gap 2. |
| #5098 | Per-`VAR` generic-context re-skip retains `Θ(P × G)` work in the owned decoder. |
| #5065 | The differential oracle. To be **retitled to D3** by #5288 slice 4; it is not D1's gate. |
| #5085 | An observer exception mistaken for malformed metadata. Gap 5. |
| #5091 | Quadratic work across declared parameter count and type-definition count. Gap 1. |
| #5130 | Every memo is a single slot, so alternating input defeats all of them. Gap 3. |
| #5132 | Quadratic cost across attribute rows sharing one value blob. Gap 4. |
| #5148 | The differential generator, to be re-targeted from offset agreement to D3 value equality. |
| #5304 | Stage 2 exhaustive per-position enumeration. |
| #5397 | `TryDecode` swallows `OutOfMemoryException` through a bare catch. Gap 6, D2. Retained rather than closed; see [D2](#d2--fail-closed-visibly). |
| #5733 | The D1 generative bounded-cost gate; #5065 does not measure cost. |
| #5742 | The defaulted `Int32` enum width is indistinguishable from a resolved one. The mitigation for D3's row-three carve-out. |
| #5755 | Retained-name evidence and the representation-bound revisit point if the output-shape hold is lifted. |
| #5757 | Type-definition index construction costs `Θ(P × L)`. Gap 7. |
| #5758 | Definition scanning costs `Θ(T × L)` on a loop-invariant name. Gap 8. |
| #5759 | A caller resolver exception is mistaken for malformed metadata. Gap 9, D2. |
| #4879 | Enum constants whose signature does not match `value__`. Fidelity. |
| #5062 | Signature decode laundering internal errors into `SignatureRejected`. |
| #4741 | Product extraction does not yet plan custom-attribute enum names into a frozen type-resolution generation. |

Issue #5067 tracks this space as a whole.
