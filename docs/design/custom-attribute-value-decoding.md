# Custom-Attribute Value Decoding

`dotnet-inspect` reads custom-attribute values out of assemblies it did not
produce. A custom-attribute value is a byte blob whose meaning is not
self-describing: the blob carries values, and the *constructor signature* — a
separate blob, in a separate table — says how wide each value is. Decoding one
requires reading two attacker-supplied structures together and agreeing about
where every element begins and ends.

This document owns the contract that makes that safe.

## Responsibility

This design owns the safety and fidelity contract for decoding custom-attribute
values from untrusted metadata: what `CustomAttributeValueGuard` promises,
what `AttributeDecoder` may assume from it, how enum widths are resolved, and
what must remain true of the two together.

The implementation lives in `src/ILInspector.MetadataPrimitives/`:
`CustomAttributeValueGuard.cs`, `AttributeDecoder.cs`, and
`EnumUnderlyingPrimitive.cs`.

## Boundaries

- **Consumes** an owner-issued `MetadataReader` and `CustomAttribute` handle.
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
  [Security work follows the actual trust
  boundary](../../AGENTS.md#security-work-follows-the-actual-trust-boundary).
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
| **Containment invariant** | Stated below. |
| **Enforcement gate** | Stated below. |

The tool never executes inspected code, so the realistic damage is not code
execution but resource exhaustion and misread memory: a small download that
costs disproportionate CPU or memory, or a decode that reads a length from the
wrong offset.

## The containment invariants

Two properties must hold together. They are independent: either can break while
the other holds, and each has produced a real defect.

> **I1 — Alignment.** For any blob the guard reports safe, on every path where
> SRM's `CustomAttributeDecoder` continues decoding, the guard must have
> skipped exactly the bytes SRM consumes.
>
> **I2 — Bounding.** Before SRM runs, the guard must have refused every
> attacker-declared quantity SRM would act on — `SZArray` element counts,
> named-argument counts, nesting depth, and charged work — regardless of
> whether the two walkers agree about where those quantities live.

I1 is about *agreement*; I2 is about *magnitude*.

I2 is the reason this component exists at all. SRM reads a declared `Int32`
`SZArray` count or `UInt16` named-argument count and allocates an
`ImmutableArray` builder from it **before** reading any element and before any
provider callback fires. Four attacker-chosen bytes can therefore request a
gigabyte-scale builder, and the blob-length charge on the value heap never sees
that amplification. No amount of cursor agreement prevents this.

Defects therefore fall into three categories, not two:

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

The third category is why I1 alone is not the contract. A gate that checks only
offset agreement passes the unbounded case.

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
2. **Fail open to SRM, not past it.** Where the guard cannot understand a blob,
   it hands the blob to SRM rather than inventing a judgment, because SRM's own
   failure is catchable and ours would be a guess.

## Enforcement gate

**Current state: `unverified`.** Both invariants are currently supported by
`tests/ILInspector.Metadata.Tests/CustomAttributeValueGuardTests.cs`, which
pins individual hostile shapes by example. That is a regression suite, not a
gate: it proves the shapes somebody already thought of, and every divergence
found so far was found by a reviewer imagining a new one.

**Required gate:** a differential oracle, tracked as issue #5065, that
generates inputs over the grammar below, runs both walkers, and asserts I1 and
I2 separately.

### Generated grammar

The generator must cover, and must be able to combine:

- **Fixed-argument types:** each primitive; `string`; `object` (boxed);
  `System.Type` as a serialized name; `SZARRAY` of each of these; `VAR`/`MVAR`
  where generic substitution applies.
- **Enum spellings:** `ELEMENT_TYPE_VALUETYPE` with a `TypeDef` handle, the
  same with a `TypeRef` handle, and the serialized (`0x55`) name form — each
  independently, because they resolve differently.
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
3. **Sentinel discrimination.** Appending a byte pattern at the offset the
   guard skipped to distinguishes the aligned case from the divergent one: if
   the walkers agree, the sentinel is never interpreted as data; if they
   diverge, it is consumed and appears in a decoded value or changes the
   outcome.

I2 is checked separately and does not depend on these: the gate asserts that a
blob whose declared quantities exceed the remaining bytes is refused *before*
`DecodeValue` is invoked at all.

Seed the corpus with the regressions already found by hand so the gate is
demonstrably non-vacuous, and pin any failing seed as an ordinary case.

Until that gate exists, any statement in this document that either invariant
*holds* is unverified in the sense of [Asserted properties name their
gate](../../AGENTS.md#asserted-properties-name-their-gate). Statements about
what the invariants *require* are normative regardless.

A TLA+ model is not the right instrument here. The property is not a stateful,
concurrent, or scheduling interaction; it is a differential property of two
sequential parsers over one grammar, where the second parser is an external
binary we cannot model faithfully. Generating real blobs and running the real
decoder is both cheaper and more honest.

## Shape: a paired walker, and what that costs

The repository's preferred containment shape is structural: a constrained type
whose construction establishes an invariant, threaded through the object model
so the compiler preserves it — `HardenedJson` at a parsing boundary,
`InertText.InertString` for values.

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

### Handle-typed enums: same handle, same resolution function

When an argument is spelled `ELEMENT_TYPE_VALUETYPE` followed by a coded
handle, the guard resolves the width **directly from the handle** —
`EnumUnderlyingPrimitive.TryResolveDefinition`, then `FromDefinition` — and
does not consult the caller's resolver at all. SRM later reaches
`ArgTypeProvider`, whose pending-handle path calls those same two functions on
the same handle.

Agreement here comes from a shared *handle and resolution function*, not from a
shared object. That is deliberate, and routing this path through a resolver
would break it: a resolver is keyed by name, a name is a flattened spelling,
and a flattened spelling discards the resolution scope that distinguishes two
definitions or an external reference from a local type. Issue #4914 was exactly
that collapse.

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

### Resolution order, and what `Int32` actually means

`ResolveEnum` tries the structural definition path first and consults the
name-keyed resolver only when that fails. `Int32` is the **last** fallback, not
the answer for every unknown name. The full order is:

1. Structural resolution from the handle.
2. The provider's name-keyed resolution, which consults the local
   `TypeDefinitionsByName` index and any trusted external width table.
3. `Int32`.

The middle step matters for threat modelling: a cross-assembly `TypeRef` that
fails structural resolution can still match a *local* definition whose
flattened spelling collides with it, and take that definition's width. The
inspected image therefore has some influence over the width chosen for a name
it does not define. This is currently a **fidelity** risk rather than a
divergence, because both walkers reach the same definition through the same
shared index — `ExternalReferenceCollidingWithNestedName_IsRefusedNotDecoded`
pins exactly that case, with both sides agreeing on eight bytes and the blob
refused on the count.

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

**This replay is the component's principal hazard.** Any work reachable from
element-type parsing is multiplied by an attacker-chosen element count, on
input the guard *accepts* — so no refusal bounds it. Four separate
amplifications of this shape have been found and fixed individually (a
re-materialized type name, a re-validated constructor `TypeSpec`, a replayed
custom-modifier chain, and a re-skipped nested descendant, the last measured at
a 537x multiplier). The structure that produced all four remains.

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
| `EnumUnderlyingPrimitive.MaxNestingDepth` | 128 | **Stops matching, then falls back.** `Matches` returns `false` past this depth; resolution then continues down the name path. The decode is not refused. |

`MaxSerializedDepth` is a safety bound. `MaxNestingDepth` is a termination
bound on a recursive structural comparison, protecting against an uncatchable
native stack overflow. Exceeding it does not refuse anything; it degrades
structural matching to name-based resolution.

That degradation is **symmetric** — both walkers lose structural matching at
the same depth and fall back the same way — which is why alignment survives it.
`NestingDeeperThanTheMatchBound_GuardSkipMatchesDecodeWidth` pins a successful
product decode at depth 200 for exactly this reason.

> **Do not describe `MaxNestingDepth` as refusing a decode.** If a future change
> makes the fallback asymmetric, this bound becomes a divergence source rather
> than a safe degradation.

`MaxSerializedDepth` also caps chains the guard walks, which has a practical
consequence for tests: a fixture built with more than 512 custom modifiers is
refused before any behavior under test is reached.

### Charging is refusal accounting, not materialization

The guard reports work through a `beforeMaterialize` observer *before* doing
it, so a large charge can appear next to a `false` result. That pairing means
"this is what the blob claimed, and we declined," not "this was allocated."

Reading a large charge as evidence of materialization has misled three separate
reviewers. State it explicitly in any new charging code.

The guard contains no `throw` for budget purposes; a caller's observer raises.
How that exception travels depends on which side raised it:

- **Guard-side observer calls are direct**, so the caller's exception
  propagates as-is.
- **Provider callbacks made during `DecodeValue` are wrapped** in
  `MaterializationObserverException` and rethrown afterwards, because an
  exception crossing SRM's callback boundary would otherwise be swallowed by
  SRM's own catch behavior and turn a budget stop into a silent decode failure.

## Failure semantics

The guard's return value is not "is this blob well-formed." It is "is it safe
to let SRM try." Both `true` and `false` are reached deliberately, and *where*
a malformed state is detected decides which one you get.

| Condition | Result | Why |
| --- | --- | --- |
| Truncated blob | `true` | SRM's failure is catchable and precise; ours would be a guess. Let the decoder own the error. |
| Unknown fixed-argument element code, unsupported serialized type, invalid named-argument kind | `false` | The guard positively classifies these as forms it cannot track. It refuses rather than walking blind alongside a decoder it can no longer follow. |
| Declared count exceeds what the remaining bytes can describe | `false` | Invariant I2. The count is the amplification vector and must be refused before SRM allocates from it. |
| Serialized nesting past `MaxSerializedDepth` | `false` | Refuse rather than truncate. |
| `TypeDefinitionIndexException` while binding the resolver | `false` | The walk never finished, so a later `DecodeValue` with a different provider must not run. A genuine failure, not a blob-format success. |
| `BadImageFormatException` / `ArgumentOutOfRangeException` **reaching the public boundary** | `true` | Same reasoning as truncation: hand it to SRM. |
| The same exceptions **caught inside a parsing helper** | `false` | A helper that cannot complete its own read has lost track of the blob; it reports failure rather than returning a plausible offset. |

Two consequences worth stating for anyone editing this:

- "Unrecognized" is not one outcome. A form the guard *positively identifies*
  as unsupported refuses; a read that simply *runs out of bytes* defers to SRM.
- Only exceptions that reach the public whole-operation boundary produce
  `true`. Helper-local catches produce `false`. Adding a new catch changes the
  contract, so state which of the two you intend.

The deliberate `true` results are the reason this component is easy to misread.
They are *not* fail-open in the safety sense, because they hand the blob to a
decoder that will itself fail closed and catchably. The genuine fail-opens are
a `true` reached by skipping the wrong bytes (I1) and a `true` reached without
refusing an attacker-declared quantity (I2).

## Open work

| Issue | Concern |
| --- | --- |
| #4992 | Whether the width-alignment collapse fixed on the handle path remains reachable on the blob-authored name path. Open and unproven. |
| #5047 | Per-element element-type replay; resolve once and loop, as SRM does. |
| #5065 | The differential oracle named above as this design's enforcement gate. |
| #4879 | Enum constants whose signature does not match `value__`. Fidelity. |
| #5062 | Signature decode laundering internal errors into `SignatureRejected`. |

Issue #5067 tracks this space as a whole.
