# Structural Clone Analysis

`StructuralCloneAnalysis` is the Analysis-owned comparator for normalized
method body structure. It answers one bounded pairwise question: are two IL
method bodies in the same retained PE image exact or separated by one
documented normalized structural edit?

This is an internal product API. Bounded discovery over a caller-supplied
same-PE population remains exact-only. The product API does not expose a public
CLI command.

## Result contract

Execution disposition and measured relationship are separate:

| Disposition | Relation |
| --- | --- |
| `Completed` | `Exact`, `Near`, or `Different` |
| `Unsupported` | None |
| `LimitReached` | None |
| `Failed` | None |

Every non-completed result carries typed blockers. Every result carries a
bounded-work receipt. An exact result additionally carries product-owned block
and local correspondence. A near result carries every complete restoring
alignment; a completed different result and an incomplete near search retain
the near-search receipt when alignment was attempted.

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
`Compare_MalformedMetadataDirectoryFailsWithoutThrowing`,
`Compare_MalformedMetadataRootFailsWithoutThrowing`,
`Compare_MalformedModuleIdentityFailsWithoutThrowing`,
`Compare_MalformedUserStringTrailerFails`,
`Compare_UserStringHintVariantsRemainSupported`,
`Compare_CompilerProducedNonAsciiUserStringRemainsSupported`,
`Compare_MethodDefinitionRequiresCompleteMethodSignature`, and
`Compare_MalformedLocalSignatureFailsAndRetainsMeasuredReceiptCounts` gate the
corresponding fail-closed metadata boundaries.

`StructuralCloneAnalysisTests` gates this policy with compiler-produced and
synthetic close-positive/close-negative cases.

## Bounded near alignment

`Near` means exactly one normalized structural edit whose unchanged remainder
the existing exact comparator proves `Exact`. Near alignment does not implement
a second graph verifier or a general graph-edit distance.

The bounded edit families are:

- one changed, inserted, or removed normalized operation;
- one changed, inserted, or removed directed CFG edge;
- one inserted or removed non-entry block, including its operations and
  incident edges.

A changed operation is tested at the same ordinal in potentially corresponding
blocks, and the removed normalized values must differ under the restoring local
correspondence. A changed edge retains its role and changes its target. An
insertion or removal is tested by removing one candidate from the larger side.
A block edit removes one non-entry block and translates the exact
correspondence back to the original block coordinates. Typed evidence retains
the affected blocks and normalized operation or edge values. Those coordinates
are same-comparison locations, not global identities.

Changed operation and edge candidates constrain the exact witness itself: one
joint witness must map the two edited source blocks, and must not map a changed
local operand or edge target as unchanged. Refinement-class membership alone is
not proof of that mapping; correspondence classes remain conservative
over-approximations whose members are not independently selectable.

Candidate enumeration indexes masked block shapes and body-level block
multisets, so only removals that can preserve necessary exact invariants reach
the verifier. Operation order, entry/exit shape, local types, local-use sites,
and incoming and outgoing edge-role multisets participate; raw local slots and
CFG target identities do not. Removing an operation incrementally rewrites the
use-site keys for locals used in that block; removing an edge rewrites local
contexts in its source and target blocks. Per-block, per-local profiles derive
these rewrites from aggregate use counts, sums, and squared sums without
rescanning the block for each candidate. The index uses aggregate hashes. A
collision can add exact comparisons but cannot establish or suppress a
relationship.

Enumeration remains exhaustive within explicit candidate-index-work,
candidate, exact-verification-work, alternative, and block-affected-element
limits. Index construction and lookup consume the index budget; each attempted
exact-restoring candidate charges its body-rebuild work before it runs, every
exact-refinement round charges its structural element work before rebuilding
keys, and each witness-search step consumes the remaining aggregate
verification budget. The alignment receipt reports both index and verification
charges. Every distinct restoring alternative is returned in deterministic
order. Multiple alternatives, or ambiguity in any restoring exact
correspondence, makes the alignment `Ambiguous`; the comparator does not choose
a preferred edit by layout or search order. If any limit prevents complete
enumeration, the result is `LimitReached`, never partial `Near` or unproven
`Different`.

Signature shape, `InitLocals`, and the local-type multiset remain hard
discriminators. Multi-edit contrasts remain `Different`. Exact results do not
carry alignment, and exact discovery calls the exact-only comparison path, so
adding near cases cannot merge discovery clusters.

`Compare_CompilerProducedOneOperationChanges_AreNear`,
`Compare_ChangedOperationsRequireOneJointBlockWitness`,
`Compare_ChangedLocalUseHasJointRestoringWitness`,
`Compare_LargeMultiBlockNearUsesMaskedCandidateIndex`,
`Compare_LargeLocalNearUsesLocalUseIndex`,
`Compare_OneEdgeChangeInsertionAndRemoval_AreNear`,
`Compare_UnreachableBlockInsertionAndRemovalCarriesContents`,
`Compare_SymmetricGraph_ReportsStableExactAndNearAmbiguity`, and
`Compare_NearEnumerationLimitsDoNotBecomeDifferent` gate the relationship,
evidence, reversal, ambiguity, and fail-closed limits.

## Exact discovery

`StructuralCloneAnalysis.Discover` partitions a caller-supplied set of
`MethodDefinitionHandle` values from one retained `PEReader`. It does not
enumerate the PE or widen the population. Duplicate handles and invalid handles
are caller errors. Method-count admission is atomic: when the population
exceeds the method limit, no metadata or body work starts.

Discovery has two bounded production passes:

1. Produce each admitted method once, emit its side-free outcome and receipt,
   calculate a compact retrieval fingerprint, and discard the full body facts.
2. Re-produce only multi-member candidate buckets, retain one bucket at a time,
   and partition it by exact comparisons against current group
   representatives.

The fingerprint contains only necessary exact invariants: normalized method
signature shape and `InitLocals`; local count and an order-independent local
type multiset; block, instruction, and edge counts; an order-independent
multiset of per-block operation/exit fingerprints; and duplicate-preserving
global incoming and outgoing edge-role multisets. Local slot numbers and CFG
target identities are excluded. Offsets, body size, `MaxStack`, declared
parameter and non-void return identities, and block order are also excluded.
Hash collisions can add comparisons but cannot establish identity. Only the
existing exact comparator can put two methods in one cluster.

Overall discovery disposition is separate from per-method production:

| Discovery disposition | Meaning |
| --- | --- |
| `Completed` | Every admitted candidate bucket was fully partitioned. |
| `LimitReached` | A method, comparison, or verification budget suppressed work. |
| `Failed` | Malformed metadata or operational body production failed. |

Unsupported methods remain explicit per-method outcomes and do not downgrade
an otherwise complete run. Failure takes precedence over a limit when both
occur. A comparison consumes budget when attempted, so reaching the exact
budget after the last required comparison is still complete.

Every emitted cluster is complete for its candidate bucket. A cluster carries
sorted typed method addresses and exact representative comparisons for every
non-anchor member. If a bucket cannot be fully verified, discovery emits no
cluster from it; instead it emits the full affected method set and a typed
suppression reason. Completed clusters from other buckets remain visible, but
absence from a cluster is negative evidence only when the overall disposition
is `Completed` and there are no suppressed buckets.

Cluster identity is the module MVID plus the sorted, unique full MethodDef
tokens of its members. This is deterministic and collision-free within one
admitted PE/population. It is not a global identity: MVIDs can be duplicated,
and membership changes when the input population changes.

The discovery receipt reports admitted and suppressed methods, per-disposition
method counts, candidate/completed/suppressed buckets, exact/different/
unresolved comparisons, and total body productions.

`Discover_CompilerProducedPopulation_FindsClosedExactFamilies` gates realistic
retrieval, exact families, the edge-role close negative, and unsupported
methods. `Discover_ThreeMemberFamily_UsesRepresentativeEvidence` and
`Discover_InputOrder_DoesNotChangeClusterIdentity` gate representative
partitioning and deterministic identity.
`Discover_ExactComparisonBudget_CanComplete`,
`Discover_MidBucketBudget_EmitsNoPartialCluster`, and
`Discover_MethodLimitAdmission_IsAtomic` gate bounded completeness and
suppression. `Discover_DuplicateHandles_AreCallerError`,
`Discover_InvalidLimits_AreCallerError`, and
`Discover_MalformedModuleIdentity_ReturnsTypedFailure` gate caller and
malformed-input boundaries.

## Seeded fuzzy retrieval

`StructuralCloneAnalysis.RetrieveSimilar` ranks likely peers for one seed over
a caller-supplied population. The same-image overload ranks one PE; the
cross-image overload accepts a seed PE and one candidate PE and retains each
side's MVID-scoped method identity. Retrieval is a separate evidence plane: it
emits no `Exact`, `Near`, or `Different` relation and never establishes
correspondence. The current `Compare` contract remains same-PE; cross-reader
relationship verification requires a separate metadata-correspondence owner
and is not inferred from a retrieval score.

The product produces each admitted body once and compares compact feature
multisets. Same-image retrieval retains reader-local metadata, user-string, and
signature-token values. Cross-image retrieval cannot compare those tokens
directly, so it uses their portable operand categories while retaining
immediates, argument positions, local types, block shapes, and typed edge
roles. That deliberate loss of discrimination remains visible through corpus
hard negatives and semantic hazards rather than guessed cross-reader identity.
Only candidates with the same normalized method-signature shape as the seed
enter either ranking, including candidates that share no scored features and
therefore score zero. The integer score ranges from zero through 10,000 and
combines:

- normalized operation identity, including same-reader operands and local type;
- opcode and operand-category positions within blocks;
- exit, operation-count, and incoming/outgoing-count block shapes;
- typed edge roles with coarse source and target block shapes;
- the local-type multiset.

The components carry weights of 35%, 20%, 20%, 20%, and 5%, respectively.
Block order and local slot numbers do not participate. Scores select and order
candidates only; a high score can intentionally describe a hard negative.
Ties resolve by component scores and then full MethodDef token within the
candidate image, so input order cannot move a candidate.

Method admission is atomic. Unsupported non-seed methods remain explicit
method outcomes and do not make an otherwise complete ranking partial.
Candidate production limits and failures remain visible in the overall
disposition and blockers; candidates from completed methods remain available,
but their ranks explicitly exclude suppressed methods and cannot prove
negative recall. `MaximumResults` bounds returned rows after all eligible
candidates are scored, and the receipt distinguishes ranked, returned, and
suppressed rows. Atomic admission or seed failure suppresses the unprocessed
candidate population. Seed unsupported, limit, and failure states remain
separate retrieval dispositions.

`StructuralCloneRetrievalTests` gates exact and near recall, contrastive
ordering, input-order determinism, visible top-K suppression, partial-ranking
disposition, atomic admission, seed-independent populations, duplicate caller
errors, unsupported seeds, cross-image artifact-scoped identities, candidate
metadata failure, same-MVID collision handling across separate readers, and the
separation between ranking score and relationship.

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

Schema 4 also declares expected edit-count summaries for every `Near` case,
the closed-world exact-discovery population, and seeded retrieval expectations.
`analysis-harness --clone-corpus` resolves those identities through SRM and
grades comparison, every returned near-alignment alternative, exact discovery,
recall-at-K, and strict contrastive score ordering independently. Expected exact
connected components derive only from declared expected relations. Complete
actual clusters must equal them, so both missed families and undeclared
cross-component merges fail. Retrieval expectations name candidates and hard
negatives that each candidate must strictly outscore; a deterministic ranking
tie does not satisfy that contrast. The harness consumes product ranks and
scores without reconstructing the similarity model. The harness does not own
retrieval, normalization,
alignment, clustering, CFG correspondence, or verification logic.
`StructuralCloneCorpusTests` gates ledger validity, both fixture inventory
views, all direct outcomes, exact closed-world clustering, fuzzy recall, and
contrastive ordering.

The corpus includes authored arithmetic and metadata-operand exact pairs,
constant and call-target near pairs, control-flow, operation-reordering, and
two-operation hard negatives, exact parameter-type and return-type semantic
hazards, and the EH unsupported boundary. Exact discovery still finds four
families and does not cluster near pairs. Three seeded fixture queries gate one
exact and two near peers against hard negatives.

## Cross-assembly version-pair corpus

`tools/AnalysisHarness/corpus/structural-clone-cross-assembly.json` grades the
cross-image retrieval overload over the authored `DiffFixtures.V1` and
`DiffFixtures.V2` pair. The harness verifies assembly/type declarations,
requires distinct MVIDs, resolves declared members independently on each side,
and passes the entire right-side type population to the product. It consumes
product ranks, similarity evidence, blockers, receipts, and artifact-scoped
method addresses without reconstructing portable features.

The seven-query ledger labels every actual top-two row and includes stable,
multi-edit, user-string, call-target, branch-target, type-token, and allocation
regression cases. Its 14 reviewed rows contain six relevant peers, six hard
negatives, and two semantic hazards. Six of seven relevant labels are recovered
at K for 42.85% labeled precision and 85.71% labeled recall; the evolved
allocation-loop member remains an explicit rank-three miss. These figures
describe only the authored version-pair ledger. They establish retrieval
behavior, not cross-reader clone relations or semantic equivalence.
`StructuralCloneCrossAssemblyCorpusTests.CommittedCorpus_GradesVersionPair`
gates the real fixture run and exact card, while
`CommittedCorpus_PinsNonVacuousReviewCoverage` gates the project/type
declarations and review-set counts on every host.

## Source-reviewed CoreLib corpus

`tools/AnalysisHarness/corpus/structural-clone-corelib.json` is a separate
real-artifact evidence plane. It does not extend the fixture corpus's
closed-world relationship or discovery claims. The ledger pins one CoreLib by
file name, SHA-256, MVID, and corresponding VMR source commit. MethodDef tokens
are artifact identity. Type and method names are mechanically checked audit
fields; signatures and source locations are human-review audit fields. The
harness does not fetch or parse source, so the source relevance judgment
remains independent of product structural evidence.

Each seeded query declares a reviewed K and labels every actual top-K candidate
with one of four source-review categories:

- `Relevant` means the source review considers the candidate part of the
  intended family.
- `HardNegative` means the candidate is a deliberately close but unrelated
  operation.
- `OrdinaryNegative` is an unrelated control.
- `SemanticHazard` means structural identity is misleading without method
  semantics, such as distinct runtime intrinsic operations sharing fallback
  throw bodies.

Relevant labels beyond K are explicit known misses. Unlabeled methods are
unknown, never negatives. Therefore "labeled precision" is relevant rows
divided by reviewed top-K rows, while "labeled recall" is relevant rows
recovered at K divided by all relevant labels declared for that query. Neither
metric estimates all possible relationships in CoreLib.
If a top-K row lacks a label, the query fails, the row remains visible as
`Unreviewed`, and precision is unavailable rather than treating the unknown row
as a negative.
Aggregate evidence separates actual reviewed rows from requested review slots;
suppressed or absent rows never enter a reviewed-row denominator.
A nonempty fully labeled prefix retains precision over its actual returned
rows, while incomplete retrieval still fails the query and makes recall
unavailable.

`analysis-harness --clone-corelib-corpus` enumerates the full pinned MethodDef
population, calls product `RetrieveSimilar`, and separately calls product
`Compare` for every label under the declared comparison limit. It gates
complete retrieval, full top-K labeling, maximum ranks, strict score
contrasts, expected comparison disposition/relation, and per-query metric
floors. The harness owns none of those product calculations.

The pinned six-query corpus covers Guid operators, Convert hex wrappers,
ASCII/Latin-1 narrowing, stream wrappers, comparer boilerplate, and `Unsafe`
intrinsic stubs. Its 27 reviewed rows contain 16 relevant candidates, nine
semantic hazards, and two hard negatives; 16 of 20 declared relevant labels
are recovered at K. Four relevant methods remain explicit misses. The
resulting 59.25% labeled precision and 80.00% labeled recall describe only this
ledger. `StructuralCloneCoreLibCorpusTests.CommittedCorpus_GradesPinnedCoreLib`
gates the fixed artifact, top-K label coverage, aggregate card, known misses,
strict contrast, and semantic-hazard separation. The same test class gates
strict schema, catalog completeness, and artifact/method identity drift.
Real-artifact tests locate the pinned hash in the installed runtimes or through
`DOTNET_INSPECT_CORELIB_CORPUS_ARTIFACT` and explicitly skip when it is absent;
they do not bind the pin to CI's floating preview host. The always-running
`CommittedCorpus_PinsNonVacuousReviewCoverage` test gates the artifact/source
pins and declared review-set counts. Re-pinning is one corpus-maintainer
operation: artifact and source pins, source labels, expected metrics, and
coverage counts move together.

## Census and demo projection

`analysis-harness --clone-census` is the harness-only whole-assembly and
seed-to-family projection. It enumerates one PE's MethodDef table, calls
`StructuralCloneAnalysis.Discover` exactly once, and presents product-owned
clusters, evidence, blockers, suppression, method outcomes, and receipts. It
does not filter candidate methods, reconstruct fingerprints, compare pairs, or
form clusters.

Every projected method includes its full MethodDef token. Type and method names
are display and selector conveniences, never identity. A `Type::Method`
selector must resolve uniquely; overloads fail and require a token. Nested type
display uses `Outer+Inner`.

Text output is bounded, but a selected seed and at least one member of its
exact family are always pinned. Structured output retains every cluster/member,
suppressed bucket, non-completed method, and unresolved comparison. Elapsed
time measures the one product discovery call and is run evidence, not a
performance baseline.

Seed status preserves discovery completeness:

- `Clustered` is positive product evidence and remains valid when unrelated
  methods make the overall run partial.
- `Unsupported`, `LimitReached`, and `Failed` preserve the seed's production
  disposition.
- `Singleton` is emitted only for a completed, unsuppressed discovery run.
- `Unresolved` replaces negative inference on partial runs.

The census reports eligible methods without an emitted family in every run,
but names that count `ExactSingletonMethods` only when discovery completed.
Product method and candidate-comparison limits remain separate and are echoed
in output.

`StructuralCloneCensusTests` gates exact-family and close-negative seed
behavior, unsupported and partial seed status, token selection, overload
ambiguity, seed pinning under truncation, complete structured output,
malformed metadata, and CLI argument/exit behavior.

`analysis-harness --clone-worksheet` is the harness-only seeded fuzzy
projection. It enumerates one PE's MethodDef table, calls `RetrieveSimilar`
exactly once, and presents product-owned ranks, score components, limits,
blockers, and receipts. Text output bounds displayed candidates with `--top`;
structured output retains every product-returned candidate. The worksheet does
not compare candidates, infer a relation, label precision, or reconstruct
features. `StructuralCloneWorksheetTests` gates candidate projection,
structured completeness, and required seed selection.

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

The `Body Shapes` query is a complementary discovery surface. It finds
occurrences of one exact stable rendered-C# syntax kind and returns source
extents; clone comparison measures a whole body's normalized IL/CFG
relationship. A remediation workflow can use a body-shape result to identify or
explain a risky construct, then use clone discovery to expand from a confirmed
seed. Any future user-facing clone search should align with `Body Shapes` on
stable member selectors, MethodDef tokens, explicit failures, limits, and
structured output, while preserving the separate evidence planes.
