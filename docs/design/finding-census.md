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
declaration facts. The producers are meant to agree through gates that hold
them together: Fast Diff's pending Shared Body equality slice, which gives its
walk canonical equality under the Body policy and gates it for exactness
against canonical IL comparison, and the declaration view's claim that its
comparison is empty exactly when Fast Diff's census facts are equal. Today
the walk compares raw encodings, so `br.s` against `br` or a changed stack
size is `Changed` for it and equal for canonical comparison, and its one
Body gate is one way, soundness of `Unchanged` only.

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
> typed kind, or `Failed`. Draining a cursor yields its atoms and then its
> terminal, which is the kind's `FindingInspection<T>` for that row, and a
> `Complete` drain sealed is the kind's `FindingCensus<T>`; the kind's
> `Inspect` is that drain. Over two cursors for one paired row, **Exists**
> returns `Changed` when the lockstep reaches a position where the two
> atoms' bytes differ, one side has a census and the other an absence, or
> one cursor ends `Complete` while the other yields an atom; `Unchanged`
> when both end `Complete` at the same position with every atom equal, or
> both end `Absent` of one kind; and `Indeterminate` when either cursor ends
> `Failed` before a difference is decided. **Exhaustive** drains both
> cursors and returns the kind's `FindingComparison<T>` over the two drains.
> Exists is `Unchanged` exactly when the two drains are equal atom by atom
> and terminal for terminal without a `Failed`, and `Indeterminate` exactly
> when a drain ends `Failed` and no difference precedes the failure.

The two scenarios are the depth of one read, not two kinds of read. The
[Diff system](diff-system.md)'s execution modes, paired steps and per-point
census, are the other axis: a cursor is per-point by construction, and
lockstep is the paired step over two of them.

| | Paired steps | Per-point census |
| --- | --- | --- |
| **Exists** | Lockstep over two cursors to the first difference; Fast Diff's walk is its provision today | Lockstep over two censuses the per-point mode already sealed, to the first difference; Exists seals nothing itself |
| **Exhaustive** | Not a scenario of the census; a pair-dependent native algorithm such as `IlBodyDiff`'s hunk alignment keeps its own result beside the census, as Finding nomenclature allows | Two sealed censuses through `FindingMatcher` in the kind's mode, folded to `PairFinding<T>` by the kind's fold |

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
| IL operations | A canonical operation, as IL diff canonicalization shapes it: the folded opcode family, its operand as a `SymbolKey` for a metadata token, a value for an immediate or a user string, and each branch or switch target as the target's instruction index within its own body | Instruction order | `Ordered`, with the move pass, then the kind's target validation over the match | IL diff canonicalization; `IlFindings` |
| Declaration facts | One census fact, keyed by its whole `SymbolKey` | Ascending key bytes | `IdentitySet` | Declaration view for the atom; Fast Diff for the census it compares today |

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
non-lazy step, and it is the instruction map canonical comparison already
builds to align targets. An opcode ECMA-335 does not define has no length, so
the pre-scan stops there and records the fault's offset; the cursor then
yields every atom before that offset and ends `Failed` at it. With the map
incomplete, a target at or past the fault has no index, so a branch or
switch atom before the fault carries its target as **undecided**, and the
Exists rules below say how an undecided target is read. A declaration cursor
sorts before it yields, so it materializes its keys; the census of one row is
small, and the order is what Exists needs.

**Policy.** A cursor is created under one named normalization policy of
its kind's owner, and its atoms' bytes and projected keys follow that
policy. The Body axis reads IL under the Body policy, which compares every
operand by name and signature without its assembly, so an IL operand's
`SymbolKey` under it carries no assembly identity, as Fast Diff's Identity
states; IL diff canonicalization's default policy, no normalization, keeps
the assembly, and a view under it reads a dependency-version bump as a
difference the Body axis does not. Two cursors are read together only under
one policy, and a sealed census records the policy it was drained under.

**Atoms compare as bytes.** An atom's **canonical bytes** are its whole
content as the kind's owner defines it under the policy, and they are what
lockstep compares, with one exclusion: a branch or switch target that is
undecided, as the pre-scan rule above leaves it, is left out of the
comparison, and the position is pending when the rest is equal. A target that
does not land on an instruction boundary of its own body, or lands outside
it, has no instruction index, and the atom carries its raw offset instead, as
canonical comparison falls back to for such a target.
The atom's `FindingKey` is the kind's **matcher projection** of those bytes,
which may be the same or less: a declaration fact's identity key is a lossless
encoding of its whole census key, as the declaration view states, so its
projection is the bytes; an IL operation's identity key omits its branch and
switch targets, as IL diff canonicalization requires so that a branch over an
inserted instruction still aligns, and the kind's fold validates targets over
the match. Equality of canonical bytes is therefore the stricter relation,
and it is the one both scenarios' agreement is stated on. The symbolic
identity of an IL operand is the same `SymbolKey` Fast Diff memoizes, byte
exact and self-delimiting, never a decoded and formatted name, so two names
whose bytes differ but decode alike stay distinct, as Fast Diff's
[Identity](fast-diff.md#identity) requires. The cursor yields the bytes,
lockstep compares the bytes, and only sealing for Exhaustive builds the key
string. A cursor that yields a record with display text would pay for every
operation what a view pays only for the operations it shows, which the
measurement gate below catches.

**Terminals** are the inspection outcomes of Finding nomenclature, reached in
sequence:

- `Complete` after the last atom;
- `Absent(SubjectAbsent)` or `Absent(NoApplicableInput)` before any atom, as
  the kind's owner authorizes it: a method with no IL body is
  `NoApplicableInput` for the IL kind, and a row the image does not hold is
  `SubjectAbsent`; and
- `Failed` with its `InspectionError` at the position the kind could not
  read: a body that cannot be decoded, an operand that cannot be resolved or
  is truncated, an opcode ECMA-335 does not define, or a declaration fact
  whose key cannot be formed. Atoms before the failure were yielded and are
  kept; no atom follows it. A cursor that sorts before it yields, as the
  declaration cursor does, fails at position zero and yields nothing. A row
  whose own identity cannot be formed, such as a malformed Type, is `Failed`
  at position zero before its presence is read, since an absence of it on
  the other side cannot be proven without that identity.

A fault is an event in the sequence, not an exception that escapes it, so a
reader can order it against the atoms it saw. Nothing is inferred from a null
body or an empty blob; the cursor's owner supplies the typed kind.

## Exists

Exists reads two cursors for one paired row in lockstep and stops at the
first event that decides. The events at one position, in the order they are
tested:

| One side | Other side | Decision |
| --- | --- | --- |
| `Failed` before presence | any | `Indeterminate` |
| `Absent(k)` | `Absent(k)` | `Unchanged` |
| `Absent` | atom, `Complete`, `Failed` after presence, or `Absent` of another kind | `Changed` |
| atom | atom, equal bytes, targets decided | advance both |
| atom | atom, equal bytes, a target undecided on either | advance both, pending |
| atom | atom, unequal bytes | `Changed` |
| atom | `Complete` | `Changed` |
| `Complete` | `Complete`, nothing pending | `Unchanged` |
| `Failed` | atom, `Complete`, or `Failed` | `Indeterminate` |

The rows are symmetric in the two sides, and the table is read top down at
each position: identity first, then presence, then reading. A row whose
identity cannot be formed is `Indeterminate` against anything, as Fast Diff
reads a malformed one-sided Type. For a row with an identity, presence is
decided before reading: an absence is known from the row before any atom is
decoded, as Fast Diff decides a body present on one side only from its RVA,
so a body that cannot be decoded on one side and is absent on the other is
`Changed`, not `Indeterminate`. The order of
positions is the order of the lockstep: a difference at position three is
decided at position three, and neither cursor is advanced again, so a fault
at position ninety on either side is never seen. A fault before any
difference is `Indeterminate` whichever side faulted, and when both fault at
the same position, since equal so far is not equal. An undecided target can
only occur on a side whose cursor will end `Failed`, so a pending position
never resolves: a body whose only differences before an undefined opcode are
targets is `Indeterminate`, and a non-target difference before the fault is
`Changed`, as Fast Diff states both. That is Diff capabilities' rule 3,
undecided is never read either way, and the `Changed`-before-fault case is
the bounded exception to its rule 2 that the contract names: the row's
Exhaustive view is the visible failure the kind reports for the side it could
not read.

Two absent terminals with equal kinds are `Unchanged`, and a `Complete`
census, even an empty one, against an absence is `Changed`, which is Finding
nomenclature's topology: an `abstract` method made `virtual` is
`NoApplicableInput` against `Complete([])`, a difference, as Fast Diff's Body
axis reports it today.

Exists builds nothing. It seals no census, forms no string, and runs no
matcher. It reads at most one atom past the last equal position on each side,
and for an unchanged row it reads every atom once, which is the cost of
proving equality and the cost Fast Diff pays today.

Exists decides one row on one kind. How a row's kinds combine into an axis
verdict, how the rows a Type owns under rule 1 of
[Fast Diff's Type level](fast-diff.md#type) combine into the Type's, and in
what order the walk reads them, so that a declaration fault read first makes
a Type `Indeterminate` before its bodies are compared, are Fast Diff's, as
are the axes, the pair partition, and the API axis surface. This document
owns the row's reading on one kind and nothing above it.

### Provisions

The Exists contract binds provisions, not code, as Diff capabilities states.
Fast Diff's paired walk becomes an Exists provision once its Shared Body
equality slice lands: from then it decides the same events as lockstep over
two IL cursors under the Body policy, pairwise and without a cursor, and its
declaration census is one sorted key list per Type compared to the first
difference, which is lockstep over the Type's declaration cursors, in key
order, by another name. Before that slice the walk decides a raw equality,
and this document makes no claim that it equals lockstep. Once it is a
provision, the walk is kept while it is measurably cheaper than lockstep and
deleted when lockstep matches it, under the rule that a fast provision is
justified by performance alone and never by a second definition of change.
Until then, both answer under one gate: for every row of every fixture pair,
the walk's verdict equals lockstep's.

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
ordered matcher's bounds are the matcher's own and are applied at `Compare`,
after both drains, never at `Inspect`: the per-side operation limit
`IlFindings.MaxCanonicalOperations` and the matrix cell limit
`FindingMatcher.MaxOrderedMatchCells`, of which the first implies the second.
A pair beyond either drains to two `Complete` censuses and is a `Failed`
comparison that the view reports visibly, as Finding adoption's fail-visibly
rule requires, while Exists over the same pair decides, since lockstep is
linear and unbounded. That is the second place the two scenarios' outcomes
differ by design, beside a difference decided before a fault. It is a
bounded exception to Diff capabilities' rule 2, registered there beside the
decode-failure exception: for such a row, the view shows the bound failure,
which is not empty and does not claim the row is unchanged; for an unchanged
pair beyond the bounds, Exists is `Unchanged` and the view shows the same
failure, which shows no difference. Today `IlFindings.Inspect` applies the
operation limit, which would make a large unchanged body `Failed` on both
sides; adoption step 1 moves it to `Compare`.

The IL kind's fold includes target validation over the match: after the
ordered matcher aligns operations by their projected keys, each matched
branch or switch is `Present` only when its targets' indexes map to each
other under the alignment, and otherwise is one `Removed` and one `Added`,
which is `IlFindings.Compare` today and is why a retargeted branch is a
difference while a branch over an insertion is not. That step is pair
dependent, and it runs over census atoms, so it is Exhaustive's. A native
algorithm that is not over census atoms, such as `IlBodyDiff`'s hunk and
row production, may run beside the census comparison when both sides are
`Complete`, as Finding nomenclature's inspection topology allows. It is not
a scenario of the census, it must not be the source of Exists, and a view
that shows its rows still declares the census comparison's level.

## Agreement

The two scenarios agree per row and per kind, and the agreement is the
Exists contract of Diff capabilities applied to one producer:

1. **Unchanged is exact.** Exists `Unchanged` holds exactly when both drains
   are `Complete` with censuses equal in canonical bytes atom by atom, or
   both are `Absent` of one kind. Equal bytes give equal projected keys and
   an identity alignment under which every target maps to itself, so the
   Exhaustive comparison, when its matcher runs, has only `Present` pairs
   with no difference and a same-topology transition, and the view has
   nothing to show.
2. **Changed is observable.** Exists `Changed` holds exactly when both drains
   are non-`Failed` and differ in bytes at some position or in topology, or
   when a difference precedes a failure. Then the Exhaustive comparison, when
   its matcher runs, has a pair other than `Present` with no difference: an
   `Added` or `Removed`, a `Changed`, a `Present` whose difference is a move,
   as the ordered matcher's move pass folds a relocated run, a target that
   fails validation, or a non-same topology; when the matcher cannot run, by
   a failure or by its bounds, the view shows that, never an empty diff.
3. **Undecided is never read either way.** Exists `Indeterminate` holds
   exactly when a drain ends `Failed` and no difference precedes the
   failure. The Exhaustive comparison is `Failed` for the same reason, and
   both say so.

Because the drain is the cursor's own output, 1 and 3 hold by construction
once the cursor is gated complete. Rule 2's first clause is the same. Its
second clause, a difference before a failure or a pair beyond the matcher's
bound, is the one Exists decides and Exhaustive cannot, and both report it
as the contract says.

## Gates

`Inspector.Findings` gates the cursor and lockstep domain-free; each kind's
test class gates its cursor:

- **Completeness:** for every row of the `ILInspector.Metadata.FastDiff`
  fixture pair and every body of the IL diff fixtures, draining the kind's
  cursor yields the same atoms and terminal as a whole read of the row
  through the kind's canonicalization under one policy, including `Absent`
  kinds and the position of a `Failed`, an IL offset for the IL kind and
  position zero for a sorted kind; and, from adoption step 1, the kind's
  `Inspect` returns that drain.
- **Canonical order:** draining a cursor twice yields equal atoms; draining
  cursors over an image pair that differs only in token numbers, row order,
  or a non-platform dependency version yields equal atoms, as Fast Diff's
  dependency-versions and canonical-encodings pairs require.
- **Exists agreement:** over the same fixture pairs, Exists per row and kind
  equals byte equality of the two drains with the topology rule, and for
  every row Exists reports `Changed` the Exhaustive comparison has a pair
  other than `Present` with no difference or cannot run, with a body whose
  only change is a relocated run among the cases; Exists on the IL kind per
  method equals Fast Diff's per-method body verdict once Shared Body
  equality lands, which re-bases that slice's Body exactness gate on the
  cursor; and Exists on the declaration kind per Type equals Fast Diff's
  declaration census equality for the Type. Folds above the row are Fast
  Diff's gates.
- **Identity:** over the `FastDiffIdentityTests` images, two method names
  whose bytes differ but decode alike, a callee switch between names that
  collide when joined by punctuation, and the other identity cases, the IL
  cursor's atoms differ where Fast Diff's walk differs, which the formatted
  operand identity of `IlFindings` today does not satisfy.
- **Fault order:** a body that differs and then holds an undefined opcode is
  `Changed`; an undefined opcode, an unresolvable operand, or an undecodable
  body before any difference is `Indeterminate` on either side and on both;
  a body whose only differences before an undefined opcode are targets is
  `Indeterminate`; a body undecodable on one side and absent on the other is
  `Changed`; a malformed Type present on one side only is `Indeterminate`;
  and a declaration fact whose key cannot be formed is `Indeterminate` for
  its kind. The cases are Fast Diff's
  `FastDiffIdentityTests` and `FastDiffMetadataSafetyTests` images, read
  through the cursor.
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
- **Bound:** a pair with a body beyond `MaxCanonicalOperations` on one side
  drains to two `Complete` censuses, is decided by Exists, and is a visible
  `Failed` comparison for Exhaustive, with no empty view; an unchanged pair
  of such bodies is Exists `Unchanged` with the same visible failure.

## Non-claims

- This document does not define canonical IL, declaration facts, or any
  kind's atom; each kind's owner does, and the cursor yields what that owner
  defines.
- It does not define correspondence. Rows arrive paired; a Member whose
  signature changed is two rows, as Fast Diff's identity and the map state.
- It does not change `FindingCensus<T>`, receipts, instance keys, or the
  matcher. Draining a cursor seals through the existing operation. It
  changes `IlFindings` only as adoption step 1 states.
- It does not add a third scenario. Counts, samples, and bounded reads are
  hosts' uses of Exists or Exhaustive, not depths of their own.
- It does not own the axes, the pair partition, the API axis surface, or the
  Type and Library folds; Fast Diff does.
- It does not rename Fast Diff's public types. Adoption step 4 does that
  under Fast Diff's owner, after the walk's fate is measured.

## Adoption

1. **IL cursor.** A cursor over one body for the IL kind, placed with the
   kind's canonicalization under the layering of Diff system's layer 1, with
   `IlFindings.Inspect` draining it. Under IL diff canonicalization's owner,
   the cursor takes a named policy, the atom's operand identity becomes the
   `SymbolKey` under it, the atom carries each target's instruction index
   with the projected key still omitting it, and the operation limit moves
   from `Inspect` to `Compare`; gated by completeness, canonical order,
   identity, fault order, and bound. It depends on the Body policy's
   normalization ([#10205](https://github.com/richlander/dotnet-inspect/issues/10205)).
2. **Exists over cursors.** Domain-free lockstep in `Inspector.Findings`,
   run beside Fast Diff's walk for every method of the fixture pairs under
   the Exists agreement gate, and measured against the walk on the perf
   hosts. It depends on Fast Diff's Shared Body equality slice, without
   which the walk and lockstep decide different equalities. The walk is
   deleted or kept by the cost gate's answer.
3. **Declaration cursor.** The declaration view's census through a sorted
   cursor shared with Fast Diff's declaration census, which is the view's
   adoption step DV1 taken through this owner; gated as step 1 is.
4. **Exists owner.** Fast Diff's document becomes the owner of Exists at
   Library, Type, and Member scope over the census, keeping its axes,
   partition, folds, and gates, with the paired walk recorded as a retired
   or retained provision, and its public types renamed under that owner in
   one slice with the hosts that consume them.
