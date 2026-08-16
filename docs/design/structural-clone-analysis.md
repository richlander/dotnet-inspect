# Structural Clone Analysis

`StructuralCloneAnalysis` is the Analysis-owned comparator for normalized method
body structure. The first slice answers one bounded question: are two IL method
bodies in the same retained PE image exactly equal under the documented
normalization?

This is an internal product API. It does not discover candidate pairs or expose
a CLI command.

## Result contract

Execution disposition and measured relationship are separate:

| Disposition | Relation |
| --- | --- |
| `Completed` | `Exact`, `Different`, or a future `Near` |
| `Unsupported` | None |
| `LimitReached` | None |
| `Failed` | None |

Every non-completed result carries typed blockers. Every result carries a
bounded-work receipt. An exact result additionally carries product-owned block
and local correspondence.

The comparator is A-vs-A: both `MethodDefinitionHandle` values belong to one
`PEReader`. Metadata, string, and signature operands therefore retain their
reader-local token identity. Cross-reader A-vs-B matching needs an explicit
metadata correspondence owner and is not implied by this contract.

## Exact policy

The first slice normalizes:

- short and long encodings of argument and local operations;
- local slot numbers through a type-compatible bijection;
- short and long integer constant and branch encodings;
- basic-block order through graph isomorphism.

It preserves as exact discriminators:

- normalized operation order, constants, and metadata operands;
- argument positions;
- local types, including complete recursive metadata resolution scope,
  class/value-type signature kind, multidimensional array sizes and lower
  bounds, and `InitLocals` when locals exist;
- branch roles, switch target order, and duplicate switch targets;
- method-definition calling convention, instance/static shape, generic arity,
  argument count, and void/value return shape, including through custom
  modifiers;
- `nop` instructions and redundant branches.

Declared parameter and non-void return type identities do not define body
equality. This is intentional: the comparator can expose structurally repeated
overload bodies while downstream consumers retain typed method identities.
Declared `MaxStack` is also outside the body relationship.

Exception-handling bodies, region-leaving or external control flow, unsupported
local type shapes, non-IL implementations, and methods without IL are
unsupported. Malformed or incomplete bodies fail visibly, including invalid
local/argument slots, invalid metadata or user-string operands, non-method
or incomplete method, local, and `calli` signatures, standalone-only calling
conventions or sentinels on method definitions, position-invalid `void` types,
non-method nested function-pointer signatures, invalid array shapes or generic
parameter indexes, reserved signature-header flags, malformed module identity,
malformed `#US` terminal flags, and terminal fallthrough. Valid
standalone unmanaged call sites, function-pointer signatures, `void` returns
and pointers, pinned locals, and custom-modifier shapes remain supported within
the guarded decode policy. Body-byte, instruction, block, CFG-edge, local, and
witness-search limits produce `LimitReached`, not `Different`.
The body-byte bound applies before instruction/CFG materialization; receipts
retain every count measured before a comparison stops, including CFG edges,
refinement rounds, and witness-search steps.
`Compare_PeLimitsReportMeasurementsAndBoundBodyDecode` gates pre-graph receipt
counts, including edges.
`Compare_InstructionLimitPrecedesMetadataOperandValidation` gates that the
instruction bound applies before per-instruction metadata work.
`Compare_EdgeLimitPrecedesMetadataOperandValidation` gates that measured edge
limits stop before metadata-operand validation and graph materialization.
`Compare_MalformedModuleIdentityFailsWithoutThrowing`,
`Compare_MalformedUserStringTrailerFails`,
`Compare_UserStringHintVariantsRemainSupported`,
`Compare_CompilerProducedNonAsciiUserStringRemainsSupported`,
`Compare_MethodDefinitionRequiresCompleteMethodSignature`, and
`Compare_MalformedLocalSignatureFailsAndRetainsMeasuredReceiptCounts` gate the
corresponding fail-closed metadata boundaries.

`StructuralCloneAnalysisTests` gates this policy with compiler-produced and
synthetic close-positive/close-negative cases.

## Correspondence and automorphisms

Joint block/local refinement narrows possible correspondence classes until it
reaches a fixed point, subject to its finite theoretical ceiling. A bounded
search with indexed edge membership then proves one exact graph-isomorphism
witness.

If every final left-side class has one right-side member, correspondence is
`Unique`. Otherwise it is `Ambiguous`. A multi-member class is a conservative
over-approximation of an automorphism orbit; its members are not independently
selectable mappings. The comparator does not enumerate all automorphisms.

Symmetric exact and close-negative cases, plus left/right reversal, are gated
by `StructuralCloneAnalysisTests`.

## Relationship corpus

`tools/AnalysisHarness/corpus/structural-clone-relationships.json` owns
independent candidate relationships and expected outcomes. Each case records:

- two typed metadata method identities;
- expected disposition and, separately, expected relation;
- difficulty, intent, actionability, and tags.

`analysis-harness --clone-corpus` resolves those identities through SRM and
grades only the public `StructuralCloneAnalysis.Compare` API. It does not own
normalization, CFG correspondence, or verification logic.
`StructuralCloneCorpusTests` gates ledger validity, fixture inventory coverage,
and all committed outcomes.

The initial corpus includes authored arithmetic and metadata-operand exact
pairs, a control-flow close negative, exact parameter-type and return-type
semantic hazards, and the EH unsupported boundary. Candidate discovery, fuzzy
ranking, precision/recall measurement, and CoreLib scale runs are later slices.

Clone detection is intentionally neutral about why two bodies are similar.
Deduplication, refactoring, provenance investigation, copied-code detection,
known-insecure-pattern remediation, and CFG stress testing are possible
downstream scenarios, not conclusions of the comparator. Structural similarity
does not establish authorship, copying intent, license provenance, or that a
matched body shares a seed's vulnerability.

## Presentation boundary

The comparator owns CFG/IL relationship truth and correspondence. A future
Research projection may use that provenance to drive the implementation-diff
presentation established by #4092. C# rendering must not become a second clone
verifier, and this first slice introduces no Decompiler dependency.

PR #4048's `body-shape` search is a complementary discovery surface. It finds
occurrences of one exact stable rendered-C# syntax kind and returns source
extents; clone comparison measures a whole body's normalized IL/CFG
relationship. A remediation workflow can use a body-shape result to identify or
explain a risky construct, then use clone discovery to expand from a confirmed
seed. Any future user-facing clone search should align with `body-shape` on
stable member selectors, MethodDef tokens, explicit failures, limits, and
structured output, while preserving the separate evidence planes.
