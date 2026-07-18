# IL diff canonicalization boundary

`IlBodyDiff` is a low-level body-diff substrate in
`ILInspector.Instructions`. It compares decoded IL operations after a small
amount of canonicalization, then projects producer-owned rows through
`IlDiffPrinter`.

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

`IlBodyDiffProfile.CompileBackFidelityV1` is the product-owned body-comparison
profile used by compile-back fidelity contract V1. The contract reports a
method as `Exact` only when both the opcode stream and this comparison match.
It reports `OpcodeDiff` when opcode names differ, `OperandDiff` when opcode
names match but this comparison differs, and `FidelityUnavailable` when the
comparison cannot produce a verdict. The contract version covers the complete
set of measurements and outcome semantics, not only operand comparison.

The profile preserves value, symbolic-token, and branch-topology comparison
while folding `ldarg*`, `ldarga*`, `starg*`, `ldloc*`, `ldloca*`, and `stloc*`
encoding macros into their operation families and omitting their raw slot
numbers. Types defined by either compared module use a shared `<current>`
scope because compile-back intentionally changes the output assembly name.
Platform assembly scopes (`System.*`, `mscorlib`, `netstandard`,
`Microsoft.CSharp`, and `Microsoft.VisualBasic*`) use a shared `<platform>`
scope so framework version and type-forwarding changes do not masquerade as
member-target changes. Non-platform assembly identities remain observable.
The default profile remains unchanged.

The boundary is intentionally narrow: the diff answers "which decoded IL
operations changed?" It does not claim source equivalence, semantic equivalence,
or durable identity for every operand category.

## Compared operation shape

Each instruction becomes a `CanonicalIlOperation`:

- `Offset`: the original IL offset, retained as evidence and display context.
- `OpcodeFamily`: the opcode name after limited macro folding.
- `Operand`: an optional typed operand identity.

Two operations match when their opcode family and operand identity match, with
special target-handling rules for branches and switches. Unmatched operations
become `IlDiffRow` entries. Rows retain the original offset so display and
diagnostics can point back to the body, but the offset is not the row's durable
identity.

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

## Current non-guarantees

### Raw slot policy

Slot operands are raw local/argument slot numbers. They are useful evidence for
small local changes, but they are not stable semantic identities. Compiler
rewrites, Debug/Release differences, or equivalent local reordering can produce
slot noise.

Some local macros currently surface as opcode-family changes (`ldloc.0`,
`stloc.1`) rather than as `Slot` operands. That is part of the current boundary,
not a stronger local identity contract. The opt-in
`IlBodyDiffProfile.CompileBackFidelityV1` profile deliberately normalizes these
families and ignores their slot operands for decompile/recompile evidence.

### Exception-region shape

The body decoder is EH-aware, and malformed EH regions fail closed through
`MethodInstructions`. `IlBodyDiff` does not currently emit producer-owned EH
region rows. Catch/finally availability or protected-range changes surface only
through operation-level rows unless a future substrate layer adds explicit EH
row kinds. `CompileBackFidelityV1` has the same boundary: an `Exact` result
under fidelity contract V1 is EH-blind and is not a semantic-equivalence claim.

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

## When to extend the boundary

Prefer extending the substrate only when a measured fixture or corpus case shows
that current rows are too noisy or too weak for a product consumer. Likely
extensions should remain Instructions-owned:

- explicit EH availability/shape rows;
- improved local/argument identity beyond raw slots;
- unsupported-boundary diagnostics when a row is intentionally approximate;
- more precise failure row kinds when a new unsupported boundary is measured.

Research, RTS, and CLI consumers should request or preserve these typed rows
rather than synthesizing their own IL wording.
