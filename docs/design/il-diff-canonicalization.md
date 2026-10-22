# IL diff canonicalization boundary

> **Map:** [Type, member, and API representation](type-member-api-representation.md) is the entry
> point for choosing a type, member, or API identity shape. This document owns
> the details below.

`IlBodyDiff` is a low-level body-diff producer in `ILInspector.ILDiff`. It
compares decoded `ILInspector.Instructions` operations after a small amount of
canonicalization, then projects producer-owned rows through `IlDiffPrinter`.
The public types use the `ILInspector.ILDiff` namespace of their owning
assembly; consumers import it explicitly beside `ILInspector.Instructions`.

`IlAssemblyDiff` is the assembly/member producer above that substrate. It owns
method identity for pairing bodies, runs self-diff and pair-diff checks, and
emits summary counts, failure buckets, operation-family buckets, and typed
example diffs. Use `CompareFiles` / `CompareStreams` when the caller starts from
assembly paths or streams; use the lower-level `Compare` overload only when the
caller already owns `PEReader` / `MetadataReader` instances. Harnesses own
rendering and Markout/card projection; they should consume
`IlAssemblyDiffResult` / `IlAssemblyDiffPairResult` rather than reimplementing
method matching or bucket wording.

`CompareMembers` is the member-scoped entry point for callers that already
resolved exact `MethodDefinitionHandle` values. It returns old/new
`IlMemberDiffSubject` identity labels plus the underlying `IlBodyDiffResult`,
so RTS, Research, and diagnostics can attach IL diff evidence to their own
member identity without running an assembly-wide card.

`CompareMemberEndpoints` is the total endpoint-topology entry point. Its caller
supplies an `IlMemberDiffEndpoint.Present` with an exact method definition or an
explicit `IlMemberDiffEndpoint.SubjectAbsent`; the adapter performs no selector
resolution or cross-version correspondence. Each present endpoint becomes a
`Complete`, `NoApplicableInput`, or `Failed` Finding inspection. The resulting
`IlMemberEndpointComparison` always retains both endpoint subjects and the
exact `FindingComparison<CanonicalIlOperation>`. It contains an
`IlMemberDiffResult` only for `Complete`/`Complete`, which is the only topology
that invokes `IlBodyDiff`.

This path does not infer absence from a null reader, handle, or body. RVA-zero
methods are present but `NoApplicableInput`; only the explicit endpoint arm is
`SubjectAbsent`. The producer gates are
`CompareMemberEndpoints_BodyfulPair_RetainsFindingAndNativeResults`,
`CompareMemberEndpoints_BodyfulAndBodyless_UsesNoApplicableInputWithoutPairDiff`,
`CompareMemberEndpoints_BodyfulAndSubjectAbsent_RetainsExplicitAbsenceWithoutPairDiff`,
`CompareMemberEndpoints_BothSubjectAbsent_IsExactWithoutPairDiff`,
`CompareMemberEndpoints_DecodeFailure_RetainsFailedInspectionWithoutPairDiff`,
and `PresentEndpoint_RejectsNullAndNilEvidence`.

The older assembly-wide and `CompareMembers` paths retain old/new-body-missing
failure rows for source compatibility while their existing consumers migrate.
Those compatibility failures are not emitted by `CompareMemberEndpoints`,
where endpoint topology owns the state.

`IlBodyDiffNormalization` exposes independent, domain-neutral normalization
mechanics without defining a consumer's equality policy:

- `NormalizeVariableLayout` folds `ldarg*`, `ldarga*`, `starg*`, `ldloc*`,
  `ldloca*`, and `stloc*` encoding macros into their operation families and
  omits their raw slot numbers.
- `NormalizeCurrentAssemblyScope` gives types and members defined by either
  compared assembly a shared `<current>` scope.
- `NormalizePlatformAssemblyScope` gives known platform references
  (`System.*`, `mscorlib`, `netstandard`, `Microsoft.CSharp`, and
  `Microsoft.VisualBasic*`) a shared `<platform>` scope while preserving the
  current assembly and non-platform references.
- `NormalizeAssemblyScope` gives every type and member reference, including
  those either compared assembly defines, one shared `<assembly>` scope, so
  operands compare by namespace, name, and signature alone, without their
  defining or referenced assembly or its version. It subsumes the two scope
  mechanics above. It is the mechanic Finding Diff Any's Body policy composes
  ([#10205](https://github.com/richlander/dotnet-inspect/issues/10205)):
  a Library rebuilt only because a dependency's version moved compares
  `Exact` under it, and a dependency's own changes are found in the
  dependency's diff. Gated by
  `NormalizeAssemblyScope_ToleratesNonPlatformReferenceVersionChanges` and
  `NonPlatformReferenceVersion_FoldsUnderAssemblyScope`.

Values, symbolic targets, and branch topology remain observable under every
normalization. Consumers own any named or versioned policy that composes these
mechanics. Comparisons use no optional normalization by default.

The boundary is intentionally narrow: the diff answers "which decoded IL
operations changed?" It does not claim source equivalence, semantic equivalence,
or durable identity for every operand category.

## Compared operation shape

Each instruction becomes a `CanonicalIlOperation`:

- `Offset`: the original IL offset, retained as evidence and display context.
- `OpcodeFamily`: the opcode name after limited macro folding.
- `Operand`: an optional typed operand identity.
- `Targets`: the IL offsets the operation points at: a branch's target, a
  switch's arms, or an exception clause's extent boundaries. Offsets are
  evidence and display context.
- `TargetIndexes`: the same targets as instruction indexes within the
  operation's own body, `BodyEndTarget` for a clause boundary at the end of
  the body, or `UndecidedTarget` for a target the cursor could not place,
  which only a failed stream carries. Target indexes are not part of an
  operation's matcher identity; the diff validates them over the alignment
  through `TargetsMatch`, where the end of the body aligns only with the end
  of the body.

After the last instruction, each exception clause of the body becomes one
more operation in declaration order, the order ECMA-335 fixes for nested
clauses: its kind is the family (`catch`, `filter`, `finally`, `fault`), its
catch Type is a token operand resolved under the same normalization as any
type operand, and its try, handler, and filter boundaries are its targets.
The decoder has already rejected a clause whose boundary is not a whole
instruction, so a clause atom never carries a boundary the alignment cannot
place.

Two operations match when their opcode family and operand identity match, with
target validation for branches, switches, and clauses. Unmatched operations
become `IlDiffRow` entries. Rows retain the original offset so display and
diagnostics can point back to the body, but the offset is not the row's durable
identity.

## Census cursor

`IlCensusCursor` is the one producer of canonical IL: the [Finding
census](finding-census.md#the-cursor)'s cursor for the IL kind. It is created
for one body under one named policy, from an already decoded
`MethodInstructions`, from a method definition and its image, or from raw IL
and its exception regions, and it yields one `CanonicalIlOperation` per
`Advance`, each instruction and then each exception clause in declaration
order, ending in exactly one `IlCensusTerminal`: `Complete`, `Absent` with its
typed kind, or `Failed` with the stage and IL offset it could not read past.
`Drain` lowers the atoms and terminal into the kind's
`FindingInspection<CanonicalIlOperation>`, which is `IlFindings.Inspect`;
`IlBodyDiff.TryCanonicalize` and `IlBodyDiff.Compare` drain the same cursor
without the Finding projection.

Over raw IL the cursor runs a boundary pre-scan that reads instruction lengths
through the decoder's shallow reader and resolves nothing, then decodes one
instruction and resolves one operand per advance, memoized per resolver by
operand kind and token. An instruction the decoder rejects ends the pre-scan
at its offset: the cursor yields every atom before that offset and ends
`Failed` there, so a difference before the fault is visible in the atoms. A
branch or switch target that starts no instruction of the body, starts one
that follows a prefix, or lies at or past the body's end is carried as
`UndecidedTarget`, and a stream that completes with one ends `Failed` by the
decoder's own rule rather than `Complete`. Once every instruction is read, the
clause stage, a second non-lazy step, builds the decoder's block graph over the
retained instructions to validate the exception-clause table by the decoder's
rules, so a clause-table failure reports the decode's reason and a clause atom
never carries a boundary the alignment cannot place; a pre-scan fault reports
the shallow reader's reason, which can differ in wording from the full
decoder's for the same body. Over an already decoded body the pre-scan is that
decode; an incomplete decode retains no prefix, so the cursor fails at position
zero with the decode's reason. Failing to read a present body, one whose RVA is
non-zero, is a `Reading` failure at position zero; only a definition row the
image cannot read is an `Identity` failure.

`IlBodyDiff.AtomsEqual` is the equality of two atoms' canonical bytes: the
family, the operand identity under the policy, and each target as an
instruction index. It is what a lockstep reader of two cursors decides, and
it is the relation the canonical-encodings and dependency-versions pairs are
gated on. The operation limit is the ordered matcher's and is applied by
`IlFindings.Compare` after both drains: a census beyond
`IlFindings.MaxCanonicalOperations` still drains `Complete`, and the comparison
of such a pair is a `FindingComparison<T>.Failed` with its
`ComparisonFailure` stated and both inspections retained, never a matcher
exception and never an empty diff.

### Cost of the cursor

`eng/measure-il-diff-exhaustive.cs` measures the exhaustive paths over one
image pair under the Body policy: the whole-assembly comparison
(`IlAssemblyDiff.CompareStreams`, fresh readers each run), the census of
every method of each image (`IlFindings.Inspect` from the image, the raw-IL
cursor), the census comparison of every paired method (`IlFindings.Compare`,
paired by declaring Type, name, and decoded signature; it runs the raw-IL
census of both sides), and the Member Body Diff endpoint path
(`IlAssemblyDiff.CompareMemberEndpoints`, which decodes once). The last three
reuse long-lived readers, so the per-reader generated-ordinal index is warm
there and cold on the assembly path, as a host's single member diff pays it.

The cursor's introduction
([#10477](https://github.com/richlander/dotnet-inspect/pull/10477), merge
`b282641e0`) was measured against its base `1bb00c652` on the six pairs of
Finding Diff Any's gates, with a third binary, head with the operand memo
neither read nor written, as the ablation: NativeAOT linux-x64, SDK
11.0.100-rc.1, one development server (AMD Ryzen 9 9900X, 59 GB; one-minute
load 0.4–2.8 across the runs, written into each output), binaries `1331be6e`
(base), `7dd8d3af` (head), and `e773bd44` (memo-off). Each pair ran two rounds
of base, head, and memo-off in that fixed order, each process two warmup runs
and ten timed runs, so every cell pools twenty samples per side; p95 is the
nearest-rank 95th percentile of those twenty. Verdict counts are identical on
all three binaries for every pair. Cells read base → head; Inspect is the sum
of the two images' statistics. The raw outputs, the ablation diff, the binary
hashes, and the pooling script are attached to the PR that recorded this
([#10497](https://github.com/richlander/dotnet-inspect/pull/10497)). The
order is fixed and not counterbalanced; per-round medians show base within
about 1% before and after a memo-off round.

Median wall time in ms, with the paired methods of the before/after images:

| Pair | Assembly | Inspect | Compare | Endpoints | Paired |
| --- | ---: | ---: | ---: | ---: | ---: |
| Aspire.Hosting 13.6.0 to 13.6.1 | 1,863 → 1,786 | 612 → 603 | 1,267 → 1,239 | 2,209 → 2,142 | 13,647 of 13,649/13,649 |
| System.Text.Json 9.0.0 to 10.0.0 | 278 → 272 | 118 → 116 | 199 → 199 | 335 → 321 | 3,756 of 3,871/4,066 |
| Newtonsoft.Json 13.0.3 to 13.0.4 | 444 → 424 | 161 → 154 | 317 → 306 | 537 → 516 | 4,162 of 4,208/4,228 |
| Newtonsoft.Json 11.0.2 to 13.0.4 | 401 → 384 | 152 → 153 | 281 → 279 | 481 → 470 | 3,798 of 4,119/4,222 |
| System.Private.Xml 10 to 11 | 1,992 → 1,876 | 696 → 669 | 1,434 → 1,394 | 2,423 → 2,333 | 16,779 of 17,599/17,006 |
| System.Private.CoreLib 10 to 11 | 2,184 → 2,146 | 870 → 869 | 1,497 → 1,495 | 2,533 → 2,482 | 40,490 of 41,564/44,865 |

p95 wall time in ms (Inspect: the sum of the two images' p95s):

| Pair | Assembly | Inspect | Compare | Endpoints |
| --- | ---: | ---: | ---: | ---: |
| Aspire.Hosting 13.6.0 to 13.6.1 | 1,885 → 1,804 | 634 → 623 | 1,287 → 1,253 | 2,238 → 2,169 |
| System.Text.Json 9.0.0 to 10.0.0 | 285 → 280 | 126 → 121 | 205 → 203 | 344 → 329 |
| Newtonsoft.Json 13.0.3 to 13.0.4 | 453 → 431 | 170 → 162 | 324 → 322 | 548 → 525 |
| Newtonsoft.Json 11.0.2 to 13.0.4 | 411 → 392 | 163 → 162 | 288 → 285 | 490 → 479 |
| System.Private.Xml 10 to 11 | 2,003 → 1,891 | 713 → 687 | 1,455 → 1,407 | 2,452 → 2,351 |
| System.Private.CoreLib 10 to 11 | 2,210 → 2,166 | 896 → 896 | 1,513 → 1,510 | 2,567 → 2,505 |

Median bytes allocated per run, in MB:

| Pair | Assembly | Inspect | Compare | Endpoints |
| --- | ---: | ---: | ---: | ---: |
| Aspire.Hosting 13.6.0 to 13.6.1 | 4,540 → 4,290 | 2,231 → 2,101 | 2,901 → 2,770 | 5,139 → 4,890 |
| System.Text.Json 9.0.0 to 10.0.0 | 755 → 732 | 433 → 417 | 493 → 479 | 861 → 837 |
| Newtonsoft.Json 13.0.3 to 13.0.4 | 1,156 → 1,103 | 590 → 562 | 746 → 718 | 1,315 → 1,262 |
| Newtonsoft.Json 11.0.2 to 13.0.4 | 1,029 → 985 | 579 → 552 | 666 → 642 | 1,173 → 1,129 |
| System.Private.Xml 10 to 11 | 4,574 → 4,345 | 2,563 → 2,417 | 3,142 → 3,018 | 5,424 → 5,192 |
| System.Private.CoreLib 10 to 11 | 5,739 → 5,641 | 3,211 → 3,137 | 3,757 → 3,693 | 6,521 → 6,422 |

Reading. Head's median is below base's on every path of every pair except
four cells within their own p95 spread: Inspect on Newtonsoft.Json 11.0.2 to
13.0.4 (+0.6%) and on CoreLib (−0.2%), and Compare on System.Text.Json and on
CoreLib (−0.1% each). Allocation is 1.5–5.8% lower in every cell. The memo-off
binary separates the two parts of the change. Without the memo, head is above
base on every path: 1.0–2.8% on Assembly and 1.5–3.1% on Endpoints, which
decode once and pay the index map and the `TargetIndexes` array per branch,
switch, and clause atom; 1.5–4.2% on Compare; and 1.6–7.1% on Inspect, the
raw-IL census, which reads every body twice (the pre-scan and the per-advance
decode). The memo then saves, as a share of base, 4.6–7.2% on Assembly,
5.4–10.6% on Inspect, 3.9–5.6% on Compare, and 4.8–6.3% on Endpoints. The
second read's own cost is not measured in isolation; taking Inspect's memo-off
excess less Assembly's as its estimate, an upper-leaning one since Assembly's
base includes work Inspect's does not, gives 0.5–5.5% of the image-backed
census. Inspect's memo saving exceeds that estimate on every pair, by 0.5
points on Newtonsoft.Json 11.0.2 to 13.0.4 and by 3–5 points on the others,
which is why Inspect with the memo is 1.4–4.2% below base on four pairs and
within 0.6% of it on that Newtonsoft.Json pair and on CoreLib. Whether the
pre-scan should become the one decode,
retaining the prefix so a body is read once, is a hypothesis this baseline
leaves to step 2's laziness and cost gates, which measure lockstep over two
cursors against Finding Diff Any's walk and are recorded on #10227; this
table is the baseline of the exhaustive paths those gates leave unchanged.

## Guarantees

### Opcode macro folding

`ldc.i4.*`, `ldc.i4.s`, and `ldc.i4` all fold to `ldc.i4`, with the integer
value carried as an `Immediate` operand. Short branch opcodes fold to their long
family name, so `br.s` and `br` compare as `br`.

Other opcode names are preserved after lower-casing and replacing underscores
with dots. Local macro opcodes such as `ldloc.0` remain distinct opcode families
today; they are not normalized to `ldloc` plus a slot operand.

### Branch and switch target alignment

Branch target operands are represented as displayable `IL_####` offsets, but a
matched branch operation does not compare the raw target text directly. After
LCS alignment, `IlBodyDiff` validates whether the old target instruction maps to
the new target instruction. A pure target-offset shift caused by inserted
instructions before the same logical target should not report the branch as
changed.

Retargeting is still reported. If a branch now points at a different aligned
instruction, the branch is emitted as a remove/add pair.

Switch operands are similar but coarser. The canonical equality rule aligns
switches with the same target count, then target validation checks each target
against the aligned instruction map. A target-count change is a shape change and
does not align as the same canonical switch operation.

### Exception clauses

The exception-clause table is compared as part of the body
([#9942](https://github.com/richlander/dotnet-inspect/issues/9942)). Two
clause atoms match when their kinds match and, for a catch, their catch Types
resolve to the same identity under the requested normalization; the catch Type
follows every scope mechanic a type operand follows. A matched clause is then
validated as a branch is: each of its boundaries must start the instruction
the aligned boundary starts, with the end of the body aligning only with the
end of the body. A clause whose kind or catch Type changed, a `try` that now
starts or ends at a different aligned instruction, and a clause added or
removed are emitted as remove/add rows of the clause atom. A boundary shifted
only by re-encoded or inserted instructions before it is not reported, as a
branch target shifted the same way is not. Nested clauses keep their
declaration order, so a reordered table is a difference.

### Symbolic metadata token operands

When comparison is backed by `MetadataReader`, metadata token operands are
resolved to symbolic identities before comparison:

- strings resolve as user-string values;
- method, field, type, and `ldtoken` operands resolve to formatted symbolic
  identities;
- standalone signatures resolve to a signature-byte identity.

Without a metadata-backed comparison, token operands fail closed. This prevents
raw token numbers from being treated as comparable identities across assemblies
or builds.

### Hunk and display ownership

`IlBodyDiff` owns operation row messages, hunk IDs, and failure rows. Failure
rows carry a producer-owned kind and message for availability, decode,
token-resolution, and unsupported-boundary cases while retaining the legacy
single `Failure` string for compatibility.

`IlDiffPrinter` owns display rows and unified text projection. Research and CLI
layers should preserve or wrap these typed rows rather than reformatting
lower-layer wording.

## Comparison outcomes

Every body comparison produces one total outcome:

| Outcome | Meaning |
| ------- | ------- |
| `Unavailable` | No comparison verdict exists because a body was absent or decoding or resolution failed. |
| `Exact` | Both bodies are equal after applying the requested normalization. |
| `OperandDiff` | Normalized opcode-family sequences match, but one or more operands differ. |
| `OpcodeDiff` | Normalized opcode-family sequences differ; operand differences may also exist. |
| `ExceptionRegionDiff` | Every operation matches and only exception clauses differ: a kind, a catch Type, an extent that no longer aligns, or a clause present on one side. A body whose operations and clauses both differ reads as `OperandDiff` or `OpcodeDiff`, since clause families are part of the family sequence. |

`Exact` is relational: it describes equality between both bodies, not the
quality or provenance of either side. `Unavailable` is not the opposite of
`Exact`; it means that no equality verdict could be produced. Typed failure
rows retain the reason.

Assembly comparison aggregates exact, operand-diff, opcode-diff, exception-
region-diff, and unavailable counts. `ChangedBodyCount` is the sum of the three
difference counts; it excludes unavailable comparisons. `FailureCount` also
includes independent self-diff and identity-resolution failures, so it need not
equal the unavailable count.

## Current non-guarantees

### Raw slot policy

Slot operands are raw local/argument slot numbers. They are useful evidence for
small local changes, but they are not stable semantic identities. Compiler
rewrites, Debug/Release differences, or equivalent local reordering can produce
slot noise.

Some local macros currently surface as opcode-family changes (`ldloc.0`,
`stloc.1`) rather than as `Slot` operands. That is part of the default boundary,
not a stronger local identity contract. The opt-in
`IlBodyDiffNormalization.NormalizeVariableLayout` normalizes these families
and ignores their slot operands when a consumer does not treat variable layout
as observable. It erases slot identity rather than proving a one-to-one slot
renaming, so callers that need to distinguish variable permutations must not
request it.

### Exception-region shape

The body decoder is EH-aware, and malformed EH regions fail closed through
`MethodInstructions`. The clause table is compared as
[Exception clauses](#exception-clauses) states; what a clause protects at run
time beyond its kind, catch Type, and extents, such as the stack state at its
boundaries, is not, so an exact body result is still not a semantic-equivalence
claim.

### Offset identity

Offsets are evidence and display hints. They are not durable identity across
versions. Insertions, compiler shape changes, and EH/layout changes can shift
offsets while preserving the same logical operation. Callers that know a member
or artifact identity should wrap diff rows with that higher-level identity
(for example, `MemberAnchor`) instead of treating raw offsets as stable.

### Semantic equivalence

The canonical operation sequence is not a verifier, optimizer, or source-level
normalizer. It does not prove that two bodies have the same stack behavior,
exception behavior, locals, source shape, or runtime semantics. It is an
operation-diff substrate for evidence and display.

## Harness-owned fidelity policy

The decompiler harness composes product normalizations into its current
fidelity contract:

```csharp
IlBodyDiffNormalization.NormalizeVariableLayout
    | IlBodyDiffNormalization.NormalizeCurrentAssemblyScope
    | IlBodyDiffNormalization.NormalizePlatformAssemblyScope
```

Fidelity V1 combines this operand-aware product result with its harness-owned
opcode canonicalization. The product outcome supplies comparison evidence; it
does not replace the versioned harness verdict. This keeps future fidelity
contracts free to change policy without adding test-policy names to the product
API.

## When to extend the boundary

Prefer extending ILDiff's typed row model only when a measured fixture or corpus
case shows that current rows are too noisy or too weak for a product consumer.
Diff-facing extensions should remain ILDiff-owned:

- explicit EH availability/shape rows;
- improved local/argument identity beyond raw slots;
- unsupported-boundary diagnostics when a row is intentionally approximate;
- more precise failure row kinds when a new unsupported boundary is measured.

When an extension needs richer decoded evidence that benefits consumers beyond
diffing, add that neutral evidence to Instructions separately without moving the
diff row or policy into the substrate.

Research, RTS, and CLI consumers should request or preserve these typed rows
rather than synthesizing their own IL wording.
