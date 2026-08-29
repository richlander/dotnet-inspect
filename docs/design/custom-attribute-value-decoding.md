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
  This document states the invariant that redesign must preserve.

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

## The containment invariant

> **When the guard reports a blob safe, the guard must have skipped exactly the
> bytes that SRM's `CustomAttributeDecoder` will subsequently consume.**

This is the whole contract. Everything else in this document exists to preserve
it.

The invariant is about *agreement*, not about correctness, and the distinction
decides how any given defect is triaged:

- If the guard and the decoder both use the wrong width for a value, the result
  is a **fidelity** defect. Output is wrong; nothing is unsafe. Both walkers
  still agree on where every element ends, so the guard's bounds still bound
  the decode.
- If the guard and the decoder use *different* widths for the same value, the
  result is a **safety** defect. The two walkers desynchronize, and from the
  first divergent element onward the guard is validating different bytes than
  the decoder will read. An array length the guard never saw becomes a length
  the decoder acts on — and the guard has already said the blob is safe, so
  nothing downstream is looking.

A divergence is therefore a fail-open, and it fails open precisely because the
guard succeeded. This is why divergence is treated as the severe case even when
the immediately visible symptom is only a wrong number in output.

### Why agreement is hard here

The second walker is not ours. `System.Reflection.Metadata` owns the decode;
we own only the guard. We cannot change SRM's parse, we cannot observe its
intermediate offsets, and it can change between runtime versions. Agreement has
to be re-established by construction rather than assumed.

Two consequences follow, and both are load-bearing:

1. **Shared oracle, not parallel logic.** Wherever a width decision depends on
   resolving a name or handle, both walkers must consult the *same* resolver
   object, not two implementations that are believed to be equivalent.
2. **Fail open to SRM, not past it.** Where the guard cannot understand a blob,
   it must hand the blob to SRM rather than invent a judgment, because SRM's
   own failure is catchable and ours would be a guess.

## Enforcement gate

**Current state: `unverified`.** The invariant is currently enforced by
`tests/ILInspector.Metadata.Tests/CustomAttributeValueGuardTests.cs`, which
pins individual hostile shapes by example. That is a regression suite, not a
gate: it proves the shapes somebody already thought of, and every divergence
found so far was found by a reviewer imagining a new one.

**Required gate:** a differential oracle that generates constructor signatures
and value blobs over the grammar below, runs both walkers, and asserts the
invariant directly — that a guard-approved blob is consumed to exactly the
offset the guard skipped to, and that the guard never approves a blob on which
SRM reads past the value. Tracked as issue #5065.

Until that gate exists, any statement in this document that the invariant
*holds* is unverified in the sense of [Asserted properties name their
gate](../../AGENTS.md#asserted-properties-name-their-gate). Statements about
what the invariant *requires* are normative regardless.

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

- **Bind the shared decisions into one object** so the pairing is expressed in
  the type system wherever it can be. See below.
- **Gate the rest generatively**, because what cannot be compiler-checked must
  be searched for. See issue #5065.

## Shared-oracle binding

`AttributeDecoder.TryDecode` constructs one `ArgTypeProvider` and uses it
twice: it passes `provider.GetUnderlyingEnumType` to the guard as the guard's
resolver, then passes the *same provider instance* to
`attribute.DecodeValue(provider)`.

This is the single most important structural fact about the component. The
guard does not approximate the decoder's width decisions; it asks the decoder's
own provider. A width the guard used is by construction a width the decoder
will use.

Two rules preserve it:

- **`IsSafeToDecode` must never be called in production with a resolver that is
  not the decoder's provider.** `BindEnumWidthResolver` enforces this by
  wrapping any resolver that is not already an `ArgTypeProvider` in one, so a
  direct call cannot silently ask a different question. The resolver-less
  overload is a conservative test-only path and is not a supported product
  configuration.
- **Name projection must match before the oracle is consulted.** SRM resolves a
  blob-authored serialized enum name through `GetTypeFromSerializedName`
  *before* asking for its underlying type. A guard that consults the width
  oracle with the raw `SerString` asks a different question whenever the two
  spellings normalize differently — for example when a name only parses after
  its assembly suffix is removed. The guard composes the same two steps for
  this reason.

An unrecognized cross-assembly enum resolves to `Int32`, the same default an
absent resolver produces, so an unknown name never yields an attacker-chosen
width.

## Width resolution has two distinct paths

An enum-typed argument reaches the guard in one of two spellings, and they
resolve differently. Conflating them has already produced one fail-open.

| Spelling | How the width is found |
| --- | --- |
| `ELEMENT_TYPE_VALUETYPE` followed by a coded handle | Resolved from the **handle**: `EnumUnderlyingPrimitive.TryResolveDefinition`, then `FromDefinition`. |
| Serialized enum (`0x55`) followed by a `SerString` | Resolved by **name**, after the SRM-matching projection described above. |

`ResolveEnum` tries the definition path first and consults the name-keyed
resolver only when structural resolution fails.

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

## Budgets and refusal semantics

| Bound | Value | Refuses |
| --- | --- | --- |
| `CustomAttributeValueGuard.MaxSerializedDepth` | `SignatureBlobGuard.DefaultMaxDepth` (512) | Boxed/`SZARRAY` nesting depth while skipping a value blob. |
| `EnumUnderlyingPrimitive.MaxNestingDepth` | 128 | Enclosing-type climbs while resolving a nested enum's name. |

Both **refuse** rather than truncate. A truncated-but-plausible result is worse
than a refusal here, because it is indistinguishable from a real answer.

Note that `MaxSerializedDepth` also caps chains the guard walks, which has
practical consequences for tests: a fixture built with more than 512 custom
modifiers is refused by the guard before any behavior under test is reached.

### Charging is refusal accounting, not materialization

The guard reports work through a `beforeMaterialize` observer *before* doing
it, so a large charge can appear next to a `false` result. That pairing means
"this is what the blob claimed, and we declined," not "this was allocated."

Reading a large charge as evidence of materialization has misled three separate
reviewers. State it explicitly in any new charging code.

The guard itself contains no `throw` for budget purposes; a caller's observer
raises, and that exception propagates through `MaterializationObserverException`
rather than being absorbed as a decode failure.

## Failure semantics

The guard's return value is not "is this blob well-formed." It is "is it safe
to let SRM try."

| Condition | Result | Why |
| --- | --- | --- |
| Truncated or unrecognized blob | `true` | SRM's failure is catchable and precise; ours would be a guess. Let the decoder own the error. |
| Declared count exceeds what the remaining bytes can describe | `false` | The count is the amplification vector; it must be refused before allocation. |
| Serialized nesting past `MaxSerializedDepth` | `false` | Refuse rather than truncate. |
| `TypeDefinitionIndexException` while binding the resolver | `false` | The walk never finished, so a later `DecodeValue` with a different provider must not run. This is a genuine failure, not a blob-format success. |
| `BadImageFormatException`, `ArgumentOutOfRangeException` | `true` | Same reasoning as truncation: hand it to SRM. |

The deliberate `true` results are the reason this component is easy to
misread. They are *not* fail-open in the safety sense, because they hand the
blob to a decoder that will itself fail closed and catchably. The genuine
fail-open is a `true` that was reached by skipping the wrong bytes.

## Open work

| Issue | Concern |
| --- | --- |
| #4992 | Whether the width-alignment collapse fixed on the handle path remains reachable on the blob-authored name path. Open and unproven. |
| #5047 | Per-element element-type replay; resolve once and loop, as SRM does. |
| #5065 | The differential oracle named above as this design's enforcement gate. |
| #4879 | Enum constants whose signature does not match `value__`. Fidelity. |
| #5062 | Signature decode laundering internal errors into `SignatureRejected`. |

Issue #5067 tracks this space as a whole.
