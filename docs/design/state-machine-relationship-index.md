# State-machine relationship index

`StateMachineRelationshipIndex` is the Metadata-owned structural substrate for
compiler state machines. It authenticates relationships between a kickoff
`MethodDef`, its claimed same-module state-machine `TypeDef`, and the exact
`MethodDef` rows that implement required interface roles.

The index reports physical metadata facts. It does not decide whether a method
is generated source, whether body evidence should be attributed to a kickoff,
whether a decompiler can reconstruct a source method, or whether a caller
should receive a recommendation.

## Contract

The index is built eagerly for one `MetadataReader` and publishes immutable
relationships using durable module-scoped addresses:

- `MetadataMethodAddress` for kickoff and implementation methods;
- `MetadataTypeDefinitionAddress` for the state-machine type;
- `MetadataTypeDefinitionName` for its exact parsed lookup name;
- `StateMachineClaimKind` for classic async, async iterator, or synchronous
  iterator claims; and
- `StateMachineMethodRole` for each exact interface role.

Consumers can query by kickoff method, state-machine type, or implementation
method. Each query returns one closed result:

- `Resolved` carries the authenticated relationship;
- `Absent` means the queried row has no state-machine relationship; and
- `Rejected` carries typed failure evidence.

`Rejected` is not absence. Its failure identifies unresolved, malformed,
duplicate, cross-kind, budget-exceeded, or ambiguous metadata and retains the
available kickoff addresses, state-machine addresses, and parsed claimed type
names. Ambiguous claimed names remain discoverable from every matching
`TypeDef`, and a rejection shared by multiple claims retains every contributing
kickoff. Rejection publications form shared components during construction, so
overlapping failures merge once and every forward or reverse entry freezes to
the same immutable result without repeatedly scanning the accumulated indexes.
`StateMachineRelationshipIndex_RejectsMalformedTrustedConstructor`,
`StateMachineRelationshipIndex_RejectsCompetingKickoffClaims`,
`StateMachineRelationshipIndex_RejectsAmbiguousClaimedType`,
`StateMachineRelationshipIndex_RejectsSharedStateMachineClaims`, and
`StateMachineRelationshipIndex_MergesEveryOverlappingRejection` gate those
distinctions and evidence paths;
`StateMachineRelationshipIndex_MergesRejectionsWithoutQuadraticRescan` gates
the bounded propagation cost.

## Authentication

A claim enters the index only when all of these conditions hold:

1. The kickoff has a recognized state-machine attribute from an authenticated
   platform assembly or the authenticated current core library.
2. The attribute constructor is an instance `.ctor(System.Type)` returning
   `void`, with no generic parameters or custom-modified parameter type.
3. Its serialized type name is bounded, well formed, and names the current
   assembly when assembly-qualified. An explicit qualifier is compared exactly:
   `PublicKeyToken=null` names an unsigned assembly and does not match a signed
   one, and `Culture=neutral` does not match a cultured one. Only an omitted
   qualifier is unconstrained.
   `StateMachineRelationshipIndex_MatchesExplicitAssemblyQualifiers` gates both
   directions.
4. The name resolves to one same-module `TypeDef`.
5. One kickoff claims that state-machine type, with one claim kind.
6. The state-machine type directly declares each required interface and each
   required role resolves to one matching instance IL method with a body.

Malformed trusted attributes are rejected, while same-named attributes from
untrusted assemblies are ignored. The distinction is gated by
`StateMachineRelationshipIndex_RejectsMalformedTrustedConstructor` and
`StateMachineRelationshipIndex_IgnoresUntrustedAttributeSpoof`.
Reflection-name escaping is decoded before matching raw metadata names, so
compiler-generated names containing escaped commas remain resolvable.
`StateMachineRelationshipIndex_ResolvesGeneratedAndCustomBuilderKickoffs`
gates this with explicit implementations of a two-argument generic interface.

The required roles are:

| Claim kind | Required roles |
| --- | --- |
| Classic async | `IAsyncStateMachine.MoveNext`, `IAsyncStateMachine.SetStateMachine` |
| Async iterator | the classic async roles, `IAsyncEnumerator<T>.MoveNextAsync`, and `IAsyncDisposable.DisposeAsync` |
| Synchronous iterator | `IEnumerator.MoveNext`, `IDisposable.Dispose` |

For each role, an exact matching `MethodImpl` declaration wins. Without one,
the index accepts one implicit public virtual implementation with the exact
name and signature. An explicit `IAsyncEnumerator<T>` declaration must use the
same TypeSpec encoding as the implemented interface, preserving its generic
argument and custom modifiers instead of accepting an erased interface shape.
The matcher also rejects custom-modified signatures, `class`/`valuetype`
mismatches, bare or wrong-arity generic interfaces, static methods, non-IL
methods, and `MethodImpl` bodies declared by another type.
`StateMachineRelationshipIndex_ResolvesExactInterfaceImplementations`,
`StateMachineRelationshipIndex_ExplicitMethodImplWinsOverNamedDecoy`, and
`StateMachineRelationshipIndex_RejectsInvalidImplementationShapes` gate these
positive and negative forms;
`StateMachineRelationshipIndex_RejectsMalformedAsyncEnumeratorShape` gates the
constructed-interface distinction.

## Bounds and malformed input

Discovery scans at most
`MetadataSafetyPolicy.MaxCorrespondenceMethodRows` `MethodDef` rows. A separate
cumulative relationship budget charges every inspected custom-attribute row,
including the bounded authentication work needed to ignore an unrelated or
untrusted attribute, plus interface rows, `MethodImpl` rows, and candidate
implementation methods. Constructor authentication is cached per metadata
handle. Untrusted constructor parents are rejected before method signatures are
decoded, and each distinct terminal assembly reference is projected once with
its public-key blob charged once, so a type reference shared by many
constructors cannot re-copy and re-hash an unbounded key. Projecting a
reference row also decodes its name and culture, and distinct rows can share one
oversized name string while differing only by version, which defeats row-keyed
projection caching; those strings are therefore charged per projected row. This
image's own
assembly identity is projected once for the whole index, with its public-key
blob charged the same way, so an assembly-qualified claim repeated across many
kickoffs cannot re-copy and re-hash the assembly-definition key either. A
separate cumulative
signature-work budget charges every
constructor, method, and TypeSpec blob that is decoded. (`SameConstructedInterface`
also charges the TypeSpec blobs it compares, but those blobs are already charged
by the decode on the same iteration, so that charge is redundant defence rather
than a load-bearing bound, and no gate names it.) A cumulative
name-work budget bounds both metadata names materialized while classifying
distinct constructors and serialized state-machine names before attribute
decoding. Reading a type-name chain charges every node it consumes, including
nil-named ones: a nil component decodes to zero characters, so a charge keyed
only on decoded length would account for nothing while the read still allocates
one segment per node, letting a deep chain of nil-named nodes materialize
proportionally for free. The chain's own structural cost — one reference per
node in the builder and again in its immutable copy — is charged up front for
the same reason. Resolving a constructor's declaring type walks its
resolution-scope chain before any name is read, and that walk is charged on
every exit rather than only when the chain terminates in a platform assembly.
Cycle detection rescans the visited prefix at each step, so the walk costs
work quadratic in the chain's depth; aiming a deep chain at a non-platform
assembly reference returns before the name read and would otherwise buy that
work for nothing, once per distinct constructor row. The charge is the number
of handle comparisons actually performed, so the budget bounds the scan and not
merely the allocation the scan leads to. This image's own assembly name and
culture are charged alongside its public key, because an unsigned assembly has
a nil key blob and would otherwise reach the projection entirely uncharged.
State-machine `System.Type` values also receive an individual encoded
byte-length preflight before SRM materializes their strings, and the whole value
blob is validated before decode: a trusted claim constructor takes exactly one
`System.Type`, so a value carrying named arguments or trailing bytes is
`Malformed` without materializing payloads the claim contract already forbids.
Each ambiguous claimed name is expanded into its matching type definitions once
per image rather than once per kickoff, so rejection evidence stays complete
while the work stays bounded by the `TypeDef` row count. The TypeDef index
retains ambiguous handles with amortized-linear growth. Existing signature,
custom-attribute, serialized-name, and metadata-relationship guards bound
recursive or allocated decoding.

Exhausting a bound rejects the index with `BudgetExceeded`; malformed SRM data
rejects it with `Malformed`. Neither becomes an empty successful index.
`StateMachineRelationshipIndex_PropagatesTypedBudgetFailure` and
`StateMachineRelationshipIndex_RejectsMethodTableBeyondScanBudget`,
`StateMachineRelationshipIndex_ReportsTypeDefNameBudget`, and
`TypeDefinitionIndex_DuplicateNamesAllocateLinearly` gate the visible budget
results and TypeDef indexing cost;
`StateMachineRelationshipIndex_ChargesUnrelatedAttributeRows` and
`StateMachineRelationshipIndex_RejectsOversizedTypeBeforeDecode` gate the
attribute-row and serialized-name bounds;
`StateMachineRelationshipIndex_CachesConstructorAuthentication` gates
constructor-classification reuse;
`StateMachineRelationshipIndex_BoundsAttributeNameMaterialization` gates that a
name-work budget is enforced during attribute classification at all, but note
that its one-unit budget is spent by the assembly-key charge before any
constructor name is read, so it does not gate the constructor type-name charges
themselves — `ChargesNilNamedTypeNameChainNodes` is what gates those; and
`StateMachineRelationshipIndex_BoundsCumulativeConstructorSignatures` and
`StateMachineRelationshipIndex_BoundsCumulativeSerializedTypeNames` gate the
remaining cumulative decode and materialization paths.
`StateMachineRelationshipIndex_ChargesUntrustedAssemblyKeyOnce` gates that an
untrusted public key is charged, and charged once rather than once per
constructor; its fixture gives several assembly-reference rows one shared key
blob, so the blob-keyed charge set — not handle-keyed memoization — is what
makes it pass.
`StateMachineRelationshipIndex_ChargesOwnAssemblyKeyOnce` gates the same
property for this image's own assembly key across repeated qualified claims:
one arm fails if the charge is removed, the other if the projection stops being
cached.
`StateMachineRelationshipIndex_ChargesRepeatedAssemblyRowNames` gates the
per-row name charge with several reference rows sharing one oversized name
string: one arm fails if the charge is removed, the other if it over-charges an
image the budget should admit.
`StateMachineRelationshipIndex_ChargesNilNamedTypeNameChainNodes` gates every
name-work charge on the constructor type-name path together: the
resolution-scope walk, the chain's structural cost, and the per-node name
components. Rather than picking a literal budget with margin, each arm measures
the fixture's minimum admitting budget by binary search and asserts it against a
recorded number, then checks that one unit below that boundary fails visibly.
A tuned literal only has to fall between the charged and under-charged
thresholds, so removing one of several charges can leave it on the same side of
the boundary and the gate stays green while the property it names is gone;
measuring the boundary makes every charge load-bearing, and two of the four
charges move it by under three percent, which no literal with usable margin
would catch. The two arms differ only in whether the chain's nodes are
nil-named, because the nil and non-nil component charges are separate branches
of `MetadataTypeNameBudget.TryRead`.
`StateMachineRelationshipIndex_ChargesUnsignedAssemblyNameAndCulture` gates that
this image's own assembly name and culture are charged when its key blob is nil,
a branch `ChargesOwnAssemblyKeyOnce` never reaches.
`StateMachineRelationshipIndex_RejectsNamedArgumentsBeforeDecode` gates the
value-blob preflight; and
`StateMachineRelationshipIndex_ExpandsAmbiguousClaimsOnce` gates that
kickoff-by-duplicate fan-out stays linear while preserving every kickoff and
type-definition candidate in the merged failure.

## Completeness

The sections above specify what one query returns. This section specifies what
must hold across *every* structural state machine in an image at once. The
distinction matters because the failure modes differ: a single lookup can be
correct while the index as a whole has silently lost rows.

A **structural state machine** is a `TypeDef` in the image that directly
declares a required state-machine interface, judged by namespace and name only.
That is deliberately more inclusive than any trust policy the index applies, so
it cannot under-count the population the index is answerable for.

Each invariant below names the gate that enforces it, or is marked
`unverified`, per
[`AGENTS.md`](../../AGENTS.md#asserted-properties-name-their-gate).

### C1 — Totality

Every structural state machine receives exactly one result: `Resolved`,
`Rejected`, or `Absent`. None is silently unclassified, and none receives two.

Totality is what makes the other invariants checkable: a consumer can count the
structural population from raw metadata, count the results, and require the two
to agree. An index that dropped a row would otherwise be indistinguishable from
an image that never had it.

Gate: `StateMachineCompletenessTests.OwnBuildOutputs_EveryStructuralMachineIsClassified`.

### C2 — Failure is never success-shaped

A construction failure is reported as a failure. Exhausting a bound yields
`BudgetExceeded`; malformed SRM data yields `Malformed`. Neither becomes an
empty successful index, and neither becomes an index that answers `Absent` for
rows it never examined.

Gate: `StateMachineRelationshipIndex_PropagatesTypedBudgetFailure`,
`StateMachineRelationshipIndex_RejectsMethodTableBeyondScanBudget`.

### C3 — Whole-module failure rejects the whole module

When construction fails for the module, **every** structural state machine in
that module reports `Rejected` — not `Absent`, and not a mixture. A per-claim
refusal is the narrow case: it rejects only the machines its claim reaches, and
leaves the rest to resolve normally.

The two are therefore distinguishable by their *shape* even though C4 says they
are not distinguishable by their kind. Whole-module failure is total;
per-claim refusal is partial. A consumer that observes a partial rejection where
it expected a total one is observing a different failure than the one it
diagnosed.

This is the invariant that makes the trimming case observable. When ILLink
removes `SetStateMachine`, the attribute claim survives and the role lookup
finds nothing, so the module's machines are all refused together rather than
disappearing (see #4827). A fixture reproducing that shape must convert *all*
of its machines, not merely one; a partial conversion is a different phenomenon
wearing the same name.

Gate: `StateMachineCompletenessTests.Sweep_RejectedStateMachine_FailsTheSweep`,
which asserts `Resolved == 0` and `Rejected == Structural` against a
string-heap-damaged fixture rather than against a literal count.

### C4 — `Failure.Kind` does not identify the cause

`Failure.Kind` alone does not separate a refused claim from a module that
failed to index. `Malformed` and `BudgetExceeded` each arise from both paths.
`Unresolved`, `Ambiguous`, `CrossKind`, and `Duplicate` arise only from the
per-claim path, so the kinds are informative but not decisive.

A consumer needing the distinction must take it from the shape described in C3,
or from a future explicit discriminator. It must not infer it from
`Failure.Kind` and must not infer it from rendered failure text.

Gate: `unverified`. No test currently forces a consumer to respect this, and
the index exposes no discriminator that would make one meaningful. #4833 tracks
consolidating the failure contract so that this invariant becomes enforceable
rather than advisory.

### C5 — Merged rejections are confluent

Rejections that share a kickoff or a state-machine type merge into one
component during construction. Every forward and reverse entry in a component
freezes to the same immutable result, and that result does not depend on the
order in which the contributing claims were discovered.

This has two halves, and stating only one of them is a trap the model caught.
*Merging happens*: two rejections that share a contributor end up in one
component, transitively, so a claim naming machines 1 and 2 and a claim naming
2 and 3 leave all three together. *Merging is confluent*: within a component
every entry carries the same result and the same evidence, whatever order the
claims arrived in. An index that merged nothing would satisfy the second half
trivially, so the first is not decoration.

Order independence is the substantive part of the second half. Discovery order
follows `TypeDef` and `MethodDef` row order, which is a compiler artifact, so
an index whose answers varied with merge order would give different results for
semantically identical assemblies.

Gate: `StateMachineRelationshipIndex_MergesEveryOverlappingRejection`,
`StateMachineRelationshipIndex_RejectsSharedStateMachineClaims`.

### C6 — Completeness is externally checkable

The population in C1 is derivable from raw metadata without loading the
assembly, without the index, and without trusting either. A cross-check may
therefore recompute it independently and compare.

This is what keeps C1 from being self-certifying. A consumer that asked the
index for both the population and the classification would learn nothing about
rows the index had lost.

Gate: `StateMachineCompletenessTests` computes the population via its own
`ImplementsAsyncStateMachine` walk over `reader.TypeDefinitions`, which shares
no code with the index.

### Model

[`models/state-machine-completeness/`](models/state-machine-completeness/)
holds a small TLA+ model of the fragment of this section that is genuinely
stateful: construction that may fail at any step, rejection components that
merge as claims are discovered, and the relationship between a whole-module
failure and the results individual queries then return.

The model checks C1, C2, C3, and C5 as invariants, and failure absorption as a
temporal property. It does not model C4, which is a statement about what a
consumer may infer rather than about system state, or C6, which is a statement
about an external observer.

The model establishes evidence about the model. It is not evidence about the
implementation; the gates named above are. Its assumptions, bounds, checked
properties, and deliberate counterexamples are recorded in its
[`README.md`](models/state-machine-completeness/README.md).

## Ownership boundaries

Metadata owns the structural relationship and structural refusal. Consumers
own all policy above it:

- **Analysis** owns scope admission, generated-code policy, lifted-owner
  composition, attribution, fallback, and recommendation eligibility.
- **Decompiler** owns kickoff-IR correlation, builder recognition,
  reconstruction eligibility, stage replay, rendering, and honest decline.
- **Research and queries** own composition and presentation.
- **Implementation Diff** owns its populations, correspondence policy, budgets,
  and result shapes.

The shared index does not require those consumers to accept identical
populations. It gives each consumer the same authenticated physical facts so
they do not independently reinterpret state-machine metadata.
