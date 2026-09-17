# Decompiler storage-place overlap

## Status and owner

This focused design is tracked by
[#7321](https://github.com/richlander/dotnet-inspect/issues/7321).

**Owner:** Decompiler.

**Claim:** a Decompiler transform can ask one typed, conservative question
about storage identity and overlap without rebuilding IR-shape-specific alias
rules.

The first prospective consumer is EH normal-continuation return timing. Its
correctness-first implementation does not depend on this design: it retains
post-cleanup evaluation whenever the returned local or argument's address is
taken. This design governs only later precision that proves a transformation
safe.

## Contract

A **storage place** denotes memory that can be read or written. It consists of:

- a root such as a local, argument, static field, object, array, fresh
  temporary, or unknown storage; and
- zero or more typed projections such as an instance field, array element,
  inline-array element, dereference, or unknown interior.

Roots and projections retain owner-issued typed identities. Display text,
source names, node order, equal offsets from another body, or recursively
reconstructed strings do not identify a place.

Comparing two places produces exactly one relation:

| Relation | Meaning |
| --- | --- |
| `Same` | Both paths denote the same complete place. |
| `Contains` | The left place contains the complete right place. |
| `ContainedBy` | The left place is contained by the complete right place. |
| `Overlaps` | Exact available layout proves partial overlap without containment. |
| `Disjoint` | Available identity and layout prove no overlap. |
| `Unknown` | The available facts do not prove any stronger relation. |

Only `Disjoint` licenses a consumer to treat mutation as irrelevant. `Same`,
containment, and `Overlaps` are positive overlap evidence. `Unknown` is a
decline result, never a synonym for `Disjoint`.

An expression can resolve to multiple possible places. Joins preserve every
alternative and separately record whether the set is complete. A missing
definition, unsupported projection, unknown call transfer, pointer arithmetic,
or unavailable layout makes the result incomplete; it must not delete known
alternatives or manufacture disjointness.

## Place overlap is not carrier flow

Storage overlap and managed-reference carrier flow are different facts.

- `ref result.Value` is an interior place inside `result`; its write overlaps
  the returned aggregate even though `int` cannot contain a managed reference.
- A `ref` field inside a holder contains a reference value whose referent may be
  `result`; the holder field's own storage need not overlap `result`.
- Addressing an ordinary nested field projects farther into the receiver's
  storage. Loading a byref field follows a contained reference instead.

A consumer that needs both facts composes them explicitly. The overlap owner
does not infer a referent merely because a value's type can contain a managed
reference, and carrier analysis does not infer storage containment from a
byref result type alone.

## Resolution and transfer boundaries

The resolver consumes one exact Decompiler IR function and its typed
definition/use evidence. It preserves these semantic distinctions:

- local and argument addresses establish exact roots;
- ordinary field and element addresses extend an existing storage path;
- dereferencing a managed reference resolves through that reference's possible
  referents rather than extending the carrier's own storage path;
- ref assignments and conditional joins transfer the complete alternative set;
- ref-local and stack-slot loads consume all reaching definitions;
- constructor, call, and ref-return transfer uses only signature facts and
  explicitly eligible receiver or argument positions; and
- fresh temporary storage is distinct from the place whose value initialized
  it.

Unsupported call behavior and unresolved definitions produce incomplete
results. This is intraprocedural analysis, not whole-program inference.

All transfer is monotone over a finite set of function-owned roots,
projections, definitions, and expression occurrences. A fixed point may add a
known alternative or change completeness from complete to incomplete; it
cannot create a fresh semantic identity during iteration. This is the
termination invariant.

## Required boundaries

The pathological cases define the minimum design boundary:

1. **Interior returned field:** `ref int alias = ref result.Value` overlaps
   `result`.
2. **Contained ref field:** `holder.Value = ref result` transfers `result` as
   the field value's referent; it does not make the holder's ordinary storage
   identical to `result`.
3. **Nested field address:** `outer.Holder.Value` first projects into
   `outer.Holder`, then follows the contained ref field.
4. **Conditional destination:** all reaching local or stack-slot definitions
   remain alternatives.
5. **Repeated call receiver:** revisiting one expression occurrence is
   idempotent and cannot grow the fixed point.
6. **Unknown destination or call:** the result is incomplete and consumers
   decline.

An implementation may support more IR shapes, but unsupported shapes remain
`Unknown`; they do not broaden the claim.

## Evidence from analogous systems

The surveyed systems are evidence, not architectural authority.

- **ILSpy** retains typed local, field, element, indirect-load, and
  indirect-store nodes. Its scoped-ref analysis traces storage roots through
  nested field and inline-array addresses, while carrying separate referent and
  ref-containing-value escape facts. Its variable splitter conservatively
  abandons splitting for unsupported address uses.
  [Storage-root tracing](https://github.com/icsharpcode/ILSpy/blob/72dbe6f41d480728ffa60bb68d1b95f9118d6b15/ICSharpCode.Decompiler/IL/Transforms/IntroduceScopedModifierOnLocals.cs#L311-L362)
  and
  [separate escape transfer](https://github.com/icsharpcode/ILSpy/blob/72dbe6f41d480728ffa60bb68d1b95f9118d6b15/ICSharpCode.Decompiler/IL/Transforms/IntroduceScopedModifierOnLocals.cs#L364-L535)
  support typed roots and separate carrier facts.
- **Roslyn** separately computes ref escape and value escape. In particular, a
  byref field's referent follows the receiver value, while an ordinary
  value-type field's address follows receiver storage.
  [Field escape rules](https://github.com/dotnet/roslyn/blob/aedc2d6e71ca3069a50bac71b96b5d21432da88d/src/Compilers/CSharp/Portable/Binder/Binder.ValueChecks.cs#L1773-L1794)
  support the carrier/place distinction, while Roslyn's valid-bound-tree input
  and source diagnostics do not transfer.
- **RyuJIT** retains locals, local fields, field addresses, and indexed
  addresses, and uses exact byte-range intersection when layout is known with a
  conservative may-define result otherwise.
  [Overlap policy](https://github.com/dotnet/runtime/blob/e58c6f856560125f138112a37429b2ccd5553f52/src/coreclr/jit/gentree.cpp#L21214-L21275)
  supports explicit relation results; ABI, SSA, promotion, and enregistration
  policy do not transfer.
- **dnSpyEx's ILSpy fork** uses reaching-definition unions plus a local forward
  scan for understood address consumers.
  [Address-use heuristic](https://github.com/dnSpyEx/ILSpy/blob/b063cd99fbfc052a022634a1efb5dd2ddddb7fbb/ICSharpCode.Decompiler/ILAst/ILAstBuilder.cs#L666-L785)
  supports conservative fallback but demonstrates why transfer rules should
  not remain pass-local.

No surveyed implementation supplies this exact contract. ILSpy provides the
closest decompiler behavior, Roslyn the clearest carrier/referent distinction,
and RyuJIT the clearest concrete overlap vocabulary.

## Evidence and gates

An implementation must name the consumer and gate every asserted property:

- algebra tests cover reflexivity, symmetry, containment direction, and the
  rule that only proved `Disjoint` licenses non-overlap;
- fixed-point tests cover multiple definitions, cycles, repeated expression
  occurrences, incompleteness, and termination;
- compiler-produced Release fixtures cover every admitted transfer shape and
  assert imported IR structure;
- the consuming transform supplies runtime behavior, render placement, and
  exact compile-back gates for its claimed rewrite; and
- unsupported pointer, call, layout, and definition shapes assert `Unknown` or
  incomplete resolution rather than success-shaped emptiness.

The returned-struct interior-field fixture from PR #6907 is the required
pathological case. Harnesses compile and inspect product-owned artifacts; they
do not repair the C# later used as product evidence.

## Non-claims

- No whole-program alias or points-to analysis.
- No source-language lifetime or ref-safety diagnostics.
- No JIT optimization, ABI, SSA, register, or struct-promotion policy.
- No change to Metadata or Instructions fact ownership.
- No assumption that inspected IL originated from C#.
- No requirement that every Decompiler pass adopt this contract together.
- No need to implement precision when a consumer's conservative decline already
  preserves correctness and acceptable output.

## Adoption

Issue #7321 owns two steps:

1. lock this focused Decompiler contract and its pathological boundaries; and
2. implement it for one measured Decompiler consumer, using EH return-timing
   precision recovery as the first adopter only when the conservative policy
   leaves material output quality on the table.

The existing decompiler producer is shared by CLI and Browser/Wasm, so one
host-neutral adoption reaches both production hosts. No new command, output
section, rendering strategy, dependency, or platform exception is implied.
