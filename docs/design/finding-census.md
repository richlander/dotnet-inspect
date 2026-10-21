# Finding census

## Status and owner

This document owns the **Finding census as a stream**: the census cursor that
yields one row's observations one at a time in a canonical order, and the two
scenarios that read a pair of cursors, **Exists** and **Exhaustive**. It owns
the cursor's order, its terminal events, the decision rules of Exists, the
agreement between the two scenarios, and the rule that binds every Exists
provision, including [Fast Diff](fast-diff.md)'s paired walk, to the cursor's
equality.

[Finding nomenclature](finding-nomenclature.md) owns the words: observation,
census, inspection, comparison, and their outcomes.
[Finding instance census](finding-instance-census.md) owns the sealed
`FindingCensus<T>` that draining a cursor produces. Each kind's atoms belong
to that kind's owner: [IL diff canonicalization](il-diff-canonicalization.md)
for IL operations and the [declaration view](declaration-view.md) for
declaration facts. The matcher and its modes belong to `Inspector.Findings`.
[Diff capabilities](diff-capabilities.md#the-exists-contract) owns the Exists
contract that both scenarios must satisfy together, and
[Diff system](diff-system.md) owns the request and its execution modes.

## Why

The diff system needs two answers about one row across two versions, and it
needs them to be the same answer at two costs:

- **Exists** says whether the row changed on an axis and stops at the first
  difference. Library Compare's changed-Types list, the Type Compare cues, and
  the [Annotated Type Map](annotated-type-map.md)'s Exists gate read it.
- **Exhaustive** says what changed, every observation and every difference to
  the end. The IL view, the declaration view, and Member Body Diff read it.

Today each answer has its own producer. Fast Diff walks two bodies in lockstep
and compares declaration facts as sorted key lists, stopping at the first
difference and building nothing. `IlFindings` canonicalizes one body into a
complete census of `il.op` Findings, runs the shared matcher, and folds the
alignment into pairs. The declaration view is designed to do the same for
declaration facts. The producers agree because gates hold them together: Fast
Diff's Body exactness gate against canonical IL comparison, and the
declaration view's claim that its comparison is empty exactly when Fast Diff's
census facts are equal.

That is two implementations of one equality per kind, which is the drift Fast
Diff's own Why rules out, and it leaves the slowest Exhaustive shape as the
natural one: read one side fully, read the other side fully, then compare.
The name Fast Diff also reads as a fourth kind of diff, when it is one
scenario, Exists, over the same census.

A census that streams removes the duplication. Each kind has one producer, a
**cursor**, that yields the row's observations in a canonical order fixed by
that side alone. Exists is two cursors advanced in lockstep until the first
unequal atom. Exhaustive is the same two cursors drained into sealed censuses
and matched. One producer, two consumers, and their agreement follows from
construction rather than from a gate between two walks.

## Contract

> **Claim.** For one row and one kind, a **census cursor** over one image
> yields the row's observations one at a time, each a `Finding<T>` with its
> `FindingKey`, in a **canonical order** that is a function of that side
> alone, and ends in exactly one terminal: `Complete`, `Absent` with its
> typed kind, or `Failed`. Draining a cursor yields the `FindingInspection<T>`
> that the kind's `Inspect` returns for that row, and its `Complete` census
> sealed is the kind's `FindingCensus<T>`. Over two cursors for one paired
> row, **Exists** returns `Changed` when the lockstep reaches a position
> where the two atoms' keys differ or one cursor ends `Complete` while the
> other yields an atom, `Unchanged` when both end `Complete` at the same
> position with every atom equal, and `Indeterminate` when either cursor ends
> `Failed` before a difference is decided. **Exhaustive** drains both
> cursors and returns the kind's `FindingComparison<T>`. Exists is
> `Unchanged` exactly when both drained inspections are `Complete` and their
> censuses are equal atom by atom, and `Indeterminate` exactly when a
> drained inspection is `Failed` and no difference precedes the failure.

The two scenarios are the depth of one read, not two kinds of read. The
[Diff system](diff-system.md)'s execution modes, paired steps and per-point
census, are the other axis: a cursor is per-point by construction, and
lockstep is the paired step over two of them.

| | Paired steps | Per-point census |
| --- | --- | --- |
| **Exists** | Lockstep over two cursors to the first difference; Fast Diff's walk is its provision today | Two sealed censuses compared atom by atom to the first difference |
| **Exhaustive** | Not a scenario of the census; a pair-dependent native algorithm such as `IlBodyDiff` keeps its own alignment beside the census, as Finding nomenclature allows | Two sealed censuses through `FindingMatcher` in the kind's mode, folded to `PairFinding<T>` |

## The cursor

A cursor is a one-sided producer with a position. It is created for one row
of one kind over one image, advances to yield the next atom or its terminal,
and is never advanced after its terminal. The atom at a position is the
observation `Inspect` would have placed at that ordinal in its complete
census, so draining and `Inspect` are the same operation at two speeds. A
cursor holds the kind's decoding state, such as a `BlobReader` and the side's
memoized symbol keys, and nothing of the other side.

**Canonical order** is the order of the sealed census and is fixed by the one
side. It is what makes positional equality meaningful across two images:

| Kind | Atom | Order | Matcher mode | Owner |
| --- | --- | --- | --- | --- |
| IL operations | A canonical operation, as IL diff canonicalization shapes it, with operands as symbolic identities and targets as aligned instruction indexes | Instruction order | `Ordered`, with the move pass | IL diff canonicalization; `IlFindings` |
| Declaration facts | One census fact, keyed by its whole `SymbolKey` | Ascending key bytes | `IdentitySet` | Declaration view; Fast Diff's census |

For an ordered kind, positional equality is sequence equality. For an
identity-set kind, the sort makes positional equality multiset equality, so
Exists over sorted cursors decides the same equality the identity-set matcher
decides. A kind added later brings its atom, its order, and its matcher mode,
and the order must be a function of one side: an order that depends on the
other image, on token numbers, or on row order is not canonical, as Fast
Diff's [Identity](fast-diff.md#identity) rules state for keys.

**Laziness** is per kind and stated, not assumed. An IL cursor decodes one
instruction per advance and resolves one operand per advance, memoized per
side. It needs one lookahead: a branch or switch target is an instruction
index, and a forward target's index is unknown until the decoder reaches it,
so the cursor runs a boundary pre-scan over the body, which decodes lengths
and resolves nothing, before it yields its first atom. That is the one
non-lazy step, and it is the step canonical comparison already needs to align
targets. A declaration cursor sorts before it yields, so it materializes its
keys; the census of one row is small, and the order is what Exists needs.

**Atoms compare as bytes.** A `FindingKey` on a sealed Finding is a lossless
string encoding of the atom's canonical bytes, as the declaration view
states for its facts, and the symbolic identity of an IL operand is the same
`SymbolKey` Fast Diff memoizes. The cursor yields the bytes, lockstep compares
the bytes, and only sealing for Exhaustive builds the string. A cursor that
yields a record with display text would pay for every operation what a view
pays only for the operations it shows, which the measurement gate below
catches.

**Terminals** are the inspection outcomes of Finding nomenclature, reached in
sequence:

- `Complete` after the last atom;
- `Absent(SubjectAbsent)` or `Absent(NoApplicableInput)` before any atom, as
  the kind's owner authorizes it: a method with no IL body is
  `NoApplicableInput` for the IL kind, and a row the image does not hold is
  `SubjectAbsent`; and
- `Failed` with its `InspectionError` at the position the kind could not
  read: a body that cannot be decoded, an operand that cannot be resolved, an
  opcode ECMA-335 does not define, or a declaration fact whose key cannot be
  formed. Atoms before the failure were yielded and are kept; no atom follows
  it.

A fault is an event in the sequence, not an exception that escapes it, so a
reader can order it against the atoms it saw. Nothing is inferred from a null
body or an empty blob; the cursor's owner supplies the typed kind.

## Exists

Exists reads two cursors for one paired row in lockstep and stops at the
first event that decides. The events at one position, in the order they are
tested:

| Old | New | Decision |
| --- | --- | --- |
| atom | atom, equal bytes | advance both |
| atom | atom, unequal bytes | `Changed` |
| atom | `Complete` | `Changed` |
| `Complete` | atom | `Changed` |
| `Complete` | `Complete` | `Unchanged` |
| `Failed` | any | `Indeterminate` |
| any | `Failed` | `Indeterminate` |
| `Absent(k)` | `Absent(k)` | `Unchanged` |
| `Absent` | atom, `Complete`, or `Absent` of another kind | `Changed` |

The order of rows is the order of the lockstep: a difference at position
three is decided at position three, and neither cursor is advanced again, so
a fault at position ninety on either side is never seen. A fault before any
difference is `Indeterminate` whichever side faulted, and when both fault at
the same position, since equal so far is not equal. That is Diff
capabilities' rule 3, undecided is never read either way, and it is the one
bounded exception to its rule 2 that the contract already names: a row Exists
decides `Changed` before a fault is `Changed`, and its Exhaustive view is the
visible failure the kind reports for the side it could not read.

Two absent terminals with equal kinds are `Unchanged`, and a `Complete`
census, even an empty one, against an absence is `Changed`, which is Finding
nomenclature's topology: an `abstract` method made `virtual` is
`NoApplicableInput` against `Complete([])`, a difference, as Fast Diff's Body
axis reports it today.

Exists builds nothing. It seals no census, forms no string, and runs no
matcher. It reads at most one atom past the last equal position on each side,
and for an unchanged row it reads every atom once, which is the cost of
proving equality and the cost Fast Diff pays today.

Exists decides one row on one kind. A row's axis verdict combines its kinds:
the Body axis of a method is `Changed` when its IL or its declaration facts
are, `Indeterminate` when neither is `Changed` and either is, and
`Unchanged` otherwise. A Type's verdict is the same combination over the rows
it owns under rule 1 of [Fast Diff's Type level](fast-diff.md#type), and a
Library's over its Types. Those folds, the axes, the pair partition, and the
API axis surface stay Fast Diff's; this document owns the row's reading and
nothing above it.

### Provisions

The Exists contract binds provisions, not code, as Diff capabilities states.
Fast Diff's paired walk is an Exists provision: it decides the same events as
lockstep over two IL cursors, pairwise and without a cursor, and its
declaration census is a sorted key list compared to the first difference,
which is lockstep over two declaration cursors by another name. The walk is
kept while it is measurably cheaper than lockstep and deleted when lockstep
matches it, under the rule that a fast provision is justified by performance
alone and never by a second definition of change. Until then, both answer
under one gate: for every row of every fixture pair, the walk's verdict
equals lockstep's.

## Exhaustive

Exhaustive drains each cursor to its `FindingInspection<T>`, seals a
`Complete` census as Finding instance census defines, and, when both sides
are `Complete`, runs `FindingMatcher` in the kind's mode and folds the match
into `PairFinding<T>` transitions, retaining the `FindingInspectionTransition`
in every case. That is `IlFindings.Compare` for IL operations and the
declaration view's comparison for declaration facts; this document adds no
operation to them. What it adds is where their inputs come from: the cursor,
so that the census Exhaustive matches is the census Exists compared.

Exhaustive always drains before it matches. The ordered matcher needs both
whole sequences for its matrix, and the identity-set matcher needs both whole
multisets, so there is no lazy Exhaustive; laziness is Exists's property. The
matcher's bound is the matcher's own: a body beyond
`IlFindings.MaxCanonicalOperations` on either side is a `Failed` comparison
that the view reports visibly, as Finding adoption's fail-visibly rule
requires, while Exists over the same pair still decides, since lockstep is
linear and unbounded. That is the one place the two scenarios' outcomes
differ by design, and the agreement below is stated so that it is not a
divergence: agreement is on the drained censuses, which both scenarios share,
not on the matcher's success.

A pair-dependent native algorithm, such as `IlBodyDiff`'s order-preserving
alignment with target validation, may run beside the census comparison when
both sides are `Complete`, as Finding nomenclature's inspection topology
allows. It is not a scenario of the census. It must not be the source of
Exists, and a view that shows its rows still declares the census comparison's
level.

## Agreement

The two scenarios agree per row and per kind, and the agreement is the
Exists contract of Diff capabilities applied to one producer:

1. **Unchanged is exact.** Exists `Unchanged` holds exactly when both drained
   inspections are `Complete` with censuses equal atom by atom, or both are
   `Absent` of one kind. Then the Exhaustive comparison, when its matcher
   runs, has only `Present` pairs and a same-topology transition, so the
   view has nothing to show.
2. **Changed is observable.** Exists `Changed` holds exactly when both
   inspections are non-`Failed` and the censuses differ at some position or
   the topology differs, or when a difference precedes a failure. Then the
   Exhaustive comparison, when its matcher runs, has a non-`Present` pair or
   a non-same topology; when the matcher cannot run, the view shows the
   failure, never an empty diff.
3. **Undecided is never read either way.** Exists `Indeterminate` holds
   exactly when an inspection is `Failed` and no difference precedes the
   failure. The Exhaustive comparison is `Failed` for the same reason, and
   both say so.

Because the drained census is the cursor's own output, 1 and 3 hold by
construction once the cursor is gated complete against `Inspect`. Rule 2's
first clause is the same. Its second clause, a difference before a failure,
is the one Exists decides and Exhaustive cannot, and both report it as the
contract says.

## Gates

`Inspector.Findings` gates the cursor and lockstep domain-free; each kind's
test class gates its cursor:

- **Completeness:** for every row of the `ILInspector.Metadata.FastDiff`
  fixture pair and every body of the IL diff fixtures, draining the kind's
  cursor yields the inspection `Inspect` returns, atom for atom and terminal
  for terminal, including `Absent` kinds and the position of a `Failed`.
- **Canonical order:** draining a cursor twice yields equal atoms; draining
  cursors over an image pair that differs only in token numbers, row order,
  or a non-platform dependency version yields equal atoms, as Fast Diff's
  dependency-versions and canonical-encodings pairs require.
- **Exists agreement:** over the same fixture pairs, Exists per row and kind
  equals atom-by-atom equality of the drained censuses with the topology
  rule, and the Body-axis fold of IL and declaration Exists per method
  equals Fast Diff's Body verdict for the method, which re-bases Fast
  Diff's Body exactness gate on the cursor.
- **Fault order:** a body that differs and then holds an undefined opcode is
  `Changed`; an undefined opcode, an unresolvable operand, or an undecodable
  body before any difference is `Indeterminate` on either side and on both;
  a declaration fact whose key cannot be formed is the same for its kind.
  The cases are Fast Diff's `FastDiffIdentityTests` and
  `FastDiffMetadataSafetyTests` images, read through the cursor.
- **Laziness:** for a pair that differs at its first atom, Exists advances
  each IL cursor once past the pre-scan and resolves one operand per side;
  for an unchanged pair, each cursor is advanced exactly its census length
  plus one. Atom counts are asserted, not inferred from time.
- **Allocation:** lockstep over two IL cursors for an unchanged body
  allocates nothing after the cursors are created, measured as Fast Diff
  measures its walk.
- **Cost:** `eng/measure-fast-diff.cs` runs lockstep beside the walk over the
  same real image pair on the perf hosts, Library-wide and per Type, under
  NativeAOT and Browser/Wasm. The walk is deleted only when lockstep matches
  or beats it on every reported number; a lockstep that does not is not an
  improvement, and the walk stays as the provision.
- **Bound:** a body beyond `MaxCanonicalOperations` on one side is decided
  by Exists and is a visible `Failed` comparison for Exhaustive, with no
  `Unchanged` or empty view in either.

## Non-claims

- This document does not define canonical IL, declaration facts, or any
  kind's atom; each kind's owner does, and the cursor yields what that owner
  defines.
- It does not define correspondence. Rows arrive paired; a Member whose
  signature changed is two rows, as Fast Diff's identity and the map state.
- It does not change `FindingCensus<T>`, receipts, instance keys, the
  matcher, or the fold. Draining a cursor seals through the existing
  operation.
- It does not add a third scenario. Counts, samples, and bounded reads are
  hosts' uses of Exists or Exhaustive, not depths of their own.
- It does not own the axes, the pair partition, the API axis surface, or the
  Type and Library folds; Fast Diff does.
- It does not rename Fast Diff's public types. Adoption step 4 does that
  under Fast Diff's owner, after the walk's fate is measured.

## Adoption

1. **IL cursor.** A cursor over one body for the IL kind, placed with the
   kind's canonicalization under the layering of Diff system's layer 1, with
   `IlFindings.Inspect` draining it; gated by completeness, canonical order,
   and fault order.
2. **Exists over cursors.** Domain-free lockstep in `Inspector.Findings`,
   run beside Fast Diff's walk for every method of the fixture pairs under
   the Exists agreement gate, and measured against the walk on the perf
   hosts. The walk is deleted or kept by the cost gate's answer.
3. **Declaration cursor.** The declaration view's census through a sorted
   cursor shared with Fast Diff's declaration census, which is the view's
   adoption step DV1 taken through this owner; gated as step 1 is.
4. **Exists owner.** Fast Diff's document becomes the owner of Exists at
   Library, Type, and Member scope over the census, keeping its axes,
   partition, folds, and gates, with the paired walk recorded as a retired
   or retained provision, and its public types renamed under that owner in
   one slice with the hosts that consume them.
