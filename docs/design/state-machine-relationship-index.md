# State-machine relationship index

`StateMachineRelationshipIndex` is the Metadata-owned structural substrate for
compiler state machines. It authenticates relationships between a kickoff
`MethodDef`, its claimed same-module state-machine `TypeDef`, and the exact
disposition of each interface role in that relationship.

A resolved relationship is an owner-issued certificate of structural identity,
not a claim that every generated support method survived post-build processing.
The certificate separates the evidence required to identify the kickoff,
state-machine type, and execution method from the completeness of support
roles.

The index reports physical metadata facts. It does not decide whether a method
is generated source, whether body evidence should be attributed to a kickoff,
whether a decompiler can reconstruct a source method, or whether a caller
should receive a recommendation.

## Status and decision

Implemented, advancing
[#5307](https://github.com/richlander/dotnet-inspect/issues/5307).
This document is the normative owner for relationship certificate issuance and
role dispositions. The exact claim is that classic async identity remains
resolvable when `SetStateMachine` alone is absent, with that absence carried
explicitly rather than converted to rejection. The
[classic async inverse design](classic-async-reconstruction.md#immediate-boundary)
is a consumer map; #5277 and #5276 own adapter and inverse implementation.

## Contract

The index is built eagerly for one `MetadataReader` and publishes immutable
relationships using durable module-scoped addresses:

- `MetadataMethodAddress` for kickoff and implementation methods;
- `MetadataTypeDefinitionAddress` for the state-machine type;
- `MetadataTypeDefinitionName` for its exact parsed lookup name;
- `StateMachineClaimKind` for classic async, async iterator, or synchronous
  iterator claims; and
- `StateMachineMethodRole` with one closed disposition for each exact interface
  role.

A role disposition is `Present(Method)`, carrying the exact
`MetadataMethodAddress`, or `AbsentFromArtifact` where the claim-kind contract
explicitly admits absence. There is no omitted, unknown, or inferred role
state.

Consumers can query by kickoff method, state-machine type, or implementation
method. Each query returns one closed result:

- `Resolved` carries the authenticated relationship certificate and its
  complete role dispositions;
- `Absent` means the queried row has no state-machine relationship; and
- `Rejected` carries typed failure evidence.

Implementation-method queries index `Present(Method)` addresses only.
`AbsentFromArtifact` has no synthetic address and is observable only through a
resolved kickoff- or state-machine-keyed relationship or relationship
enumeration.

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

A claim enters the index only when all of these identity conditions hold:

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
6. The kickoff is a managed IL method with a body.
7. The state-machine type directly declares each required interface.
8. Every must-be-present role resolves to one matching instance IL method with
   a body.

Malformed trusted attributes are rejected, while same-named attributes from
untrusted assemblies are ignored. The distinction is gated by
`StateMachineRelationshipIndex_RejectsMalformedTrustedConstructor` and
`StateMachineRelationshipIndex_IgnoresUntrustedAttributeSpoof`.
Reflection-name escaping is decoded before matching raw metadata names, so
compiler-generated names containing escaped commas remain resolvable.
`StateMachineRelationshipIndex_ResolvesGeneratedAndCustomBuilderKickoffs`
gates this with explicit implementations of a two-argument generic interface.

Role requirements are:

| Claim kind | Must be `Present` | May be `AbsentFromArtifact` |
| --- | --- | --- |
| Classic async | `IAsyncStateMachine.MoveNext` | `IAsyncStateMachine.SetStateMachine` |
| Async iterator | `IAsyncStateMachine.MoveNext`, `IAsyncStateMachine.SetStateMachine`, `IAsyncEnumerator<T>.MoveNextAsync`, `IAsyncDisposable.DisposeAsync` | None |
| Synchronous iterator | `IEnumerator.MoveNext`, `IDisposable.Dispose` | None |

For a present role, an exact matching `MethodImpl` declaration wins. Without
one, the index accepts one implicit public virtual implementation with the
exact name and signature. An explicit `IAsyncEnumerator<T>` declaration must
use the same TypeSpec encoding as the implemented interface, preserving its
generic argument and custom modifiers instead of accepting an erased interface
shape. The matcher also rejects custom-modified signatures,
`class`/`valuetype` mismatches, bare or wrong-arity generic interfaces, static
methods, non-IL methods, and `MethodImpl` bodies declared by another type.
`StateMachineRelationshipIndex_ResolvesExactInterfaceImplementations`,
`StateMachineRelationshipIndex_ExplicitMethodImplWinsOverNamedDecoy`, and
`StateMachineRelationshipIndex_RejectsInvalidImplementationShapes` gate these
positive and negative forms;
`StateMachineRelationshipIndex_RejectsMalformedAsyncEnumeratorShape` gates the
constructed-interface distinction.

`AbsentFromArtifact` is narrower than failure to resolve a role. Candidate
recognition happens before full role validation:

- an explicit candidate is a `MethodImpl` whose readable declaration names the
  required interface and role, before its signature or body is accepted; and
- an implicit candidate is a MethodDef on the state-machine type with the exact
  role name, before its visibility, flags, signature, or body is accepted.

Existing explicit-over-implicit precedence still determines whether a valid
role resolves. If no valid role resolves, classic `SetStateMachine` receives
`AbsentFromArtifact` only when the bounded scan found no candidate in either
tier. Any candidate that fails full validation instead produces `Rejected`.
That includes an explicit declaration with the right interface and role name
but a wrong signature, even when its body uses an interface-qualified or
otherwise non-implicit name.

Unreadable or malformed declarations and exhausted scan or decode budgets also
remain `Rejected`; they never certify absence.

This narrow rule records what the inspected artifact contains without treating
post-link incompleteness as missing identity. Extending
`AbsentFromArtifact` to another claim kind or role requires its own artifact
evidence and contract change.

## Evidence-carrying certificate

The resolved relationship value is the certificate. It carries a closed,
typed account of the observations that Metadata accepted:

```text
StateMachineRelationship
  Claim
    Kickoff                    exact module-scoped MethodDef address
    Kind                       classic async, async iterator, or iterator
    ClaimedStateMachineName    exact parsed claim name
    StateMachineType           unique same-module TypeDef address
    RequiredInterfaces         directly declared recognized interfaces
  Roles
    Role -> Present(Method) | AbsentFromArtifact
```

The claim receipt records the correlation established by the authenticated
attribute and unique same-module type resolution. Each `Present` receipt binds
an exact role to an exact MethodDef after the interface and signature checks
above. Each admitted `AbsentFromArtifact` receipt records a completed negative
scan under the same bounds. Consumers may rely on those identities and
dispositions without repeating Metadata matching.

For classic async, `Present(IAsyncStateMachine.MoveNext)` is the certified
execution identity. `SetStateMachine` is support-role completeness and does not
participate in that execution identity.

The certificate does not authenticate body semantics. In particular, it does
not say that the current bodies are original compiler output, that kickoff IL
contains a builder `Start` correlation, or that a decompiler can reconstruct
source. Analysis or Decompiler may produce additional body-evidence receipts,
but may not use them to replace the owner-issued kickoff, state-machine, or
execution identity.

## Contract demonstration

The ordinary full-trim artifact from `ClassicAsyncArtifactMatrixTests`
produces this certificate:

```text
Relationship: Resolved(ClassicAsync)
Kickoff:      RecoverableAsync
StateMachine: <RecoverableAsync>d__0
Roles:
  MoveNext:        Present(<RecoverableAsync>d__0.MoveNext)
  SetStateMachine: AbsentFromArtifact
```

The role-preserved artifact produces the same certificate with
`SetStateMachine: Present(...)`. The SDK reference artifact also resolves with
both roles present; its synthesized `ldnull; throw` bodies are outside this
Metadata certificate and remain a Decompiler decline. The unused trimmed
method has no kickoff or state-machine rows and produces no relationship.

A near-negative artifact with a `SetStateMachine` candidate that has the wrong
signature is not equivalent to the ordinary-trim artifact:

```text
Relationship: Rejected(Unresolved)
Evidence:     invalid present SetStateMachine candidate
```

This distinction prevents a damaged or contradictory role from becoming a
success-shaped absence.

## Validation status

Release gates enforce the certificate:

- `ClassicAsyncArtifactMatrixTests.TrimmedArtifactWithoutRolePreservation_AuthenticatesAbsentSupport`
  independently establishes the ordinary full-trim artifact's direct
  `IAsyncStateMachine` declaration, exact body-bearing `MoveNext`, and absence
  of both explicit and implicit `SetStateMachine` candidates before requiring
  `Resolved(ClassicAsync)` with `SetStateMachine: AbsentFromArtifact`.
- `ClassicAsyncArtifactMatrixTests.ImplementationAndRolePreservedTrim_AuthenticateRecoverableClassicRecipe`
  and
  `ClassicAsyncArtifactMatrixTests.ReferenceArtifact_AuthenticatesRelationshipsOverBodyReplacingIl`
  require both roles to remain `Present` in the role-preserved and SDK
  reference artifacts.
- `StateMachineRelationshipIndex_ResolvesClassicAsyncWithAbsentSupportRole`
  requires one closed disposition for each classic role, retains
  implementation lookup for `MoveNext`, and exposes no support MethodDef
  through `TryGetMethod`.
- `StateMachineRelationshipIndex_RejectsInvalidImplementationShapes` requires
  invalid or missing `MoveNext` to reject and prevents malformed, bodyless,
  ambiguous, or contradictory `SetStateMachine` candidates from becoming
  absence. Its explicit wrong-signature arm uses a body name that cannot enter
  implicit matching, and its malformed-interface arm rejects a self-cyclic
  declaration parent rather than certifying absence.
- `AsyncLoweringFixtureMatrixTests.IdenticalSource_ProducesClassicAndRuntimeAsyncPhysicalShapes`
  preserves runtime async as `Absent` while the identical classic source
  produces a resolved relationship with every role present.

`StateMachineRelationshipIndex_RejectsInvalidImplementationShapes` gates
several present-but-invalid role shapes in addition to the classic-specific
arms above.

## Non-claims

The certificate does not:

- prove that a support role existed before linking or identify which tool
  removed it;
- authenticate classic async when the trusted claim, unique same-module type,
  direct `IAsyncStateMachine` declaration, kickoff body, or execution
  `MoveNext` is unavailable;
- admit an absent role for async iterators or synchronous iterators;
- establish kickoff-body correlation, reconstruction eligibility, source
  fidelity, or behavioral equivalence; or
- acquire a pre-transform assembly or infer a replacement identity from names,
  neighboring types, or body shape.

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
Malformed metadata encountered while acquiring or classifying one
custom-attribute constructor is isolated to that attribute's owning kickoff.
Constructor coded-index extraction remains inside that row-local recovery
boundary, and no classification is cached until it yields a stable constructor
handle. The kickoff is rejected as `Malformed`, without fabricating a claim
kind, state-machine identity, or claimed name that the damaged row did not
establish; discovery continues for other kickoff methods in the module.
`StateMachineRelationshipIndex_IsolatesMalformedConstructorRow` and
`StateMachineRelationshipIndex_IsolatesReservedConstructorTag` gate the
acquisition and coded-index paths. This includes typed
type-name-reader rejections returned for malformed and over-budget constructor
type names and resolution-scope walks, TypeSpec guard rejections, and failures
nested inside composite TypeSpec shapes, not only exceptions thrown while
reading constructor rows.
`StateMachineRelationshipIndex_IsolatesTypeSpecificationGuardRejection` and
`StateMachineRelationshipIndex_PreservesNestedConstructorTypeNameFailure` gate
those TypeSpec paths. Other malformed metadata that prevents module-wide
construction remains a whole-module failure.
Each ambiguous claimed name is expanded into its matching type definitions once
per image rather than once per kickoff, so rejection evidence stays complete
while the work stays bounded by the `TypeDef` row count. The TypeDef index
retains ambiguous handles with amortized-linear growth. Existing signature,
custom-attribute, serialized-name, and metadata-relationship guards bound
recursive or allocated decoding.

`Relationships` is a total `StateMachineRelationshipsResult`. Successful
construction returns `Available`, whose relationship array may legitimately be
empty. Whole-module construction failure returns `Rejected` with the same
immutable `StateMachineRelationshipFailure` reported by every valid keyed
query. Exhausting a bound reports `BudgetExceeded`; malformed SRM data reports
`Malformed`. Neither keyed queries nor enumeration turn whole-module failure
into absence or an empty success. See
[C2](#c2--query-failures-are-never-success-shaped).
`StateMachineRelationshipIndex_PropagatesTypedBudgetFailure` and
`StateMachineRelationshipIndex_RejectsMethodTableBeyondScanBudget`,
`StateMachineRelationshipIndex_ReportsTypeDefNameBudget`, and
`TypeDefinitionIndex_DuplicateNamesAllocateLinearly` gate the visible budget
results and TypeDef indexing cost;
`StateMachineRelationshipIndex_ChargesUnrelatedAttributeRows` and
`StateMachineRelationshipIndex_RejectsOversizedTypeBeforeDecode` gate the
attribute-row and serialized-name bounds;
`StateMachineRelationshipIndex_CachesConstructorAuthentication` and
`StateMachineRelationshipIndex_CachesThrownConstructorAuthenticationFailure`
gate reuse of both returned and recoverably thrown constructor
classifications, so repeated references to one damaged constructor cannot
convert its kickoff-local rejection into whole-module budget failure;
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
`StateMachineRelationshipIndex_IsolatesMalformedConstructorRow` gates that one
unreadable constructor row rejects only its owning kickoff while a valid
relationship elsewhere in the module remains available.
`StateMachineRelationshipIndex_IsolatesRejectedConstructorTypeName` gates the
same containment for returned malformed-name and name-budget failures.
`StateMachineRelationshipIndex_IsolatesTypeReferenceTraversalRejection` gates
containment for a returned resolution-scope node-budget failure.

## Completeness

The sections above specify what one query returns. C1, C3, and C6 below specify
what must hold across *every structural async state machine* in an image at
once; synchronous iterator state machines are outside those population
invariants. C2, C4, and C5 concern index failure and rejection merging rather
than that async-only population. The distinction matters because a single
lookup can be correct while the index as a whole has silently lost rows.

A **structural async state machine** is a `TypeDef` in the image that directly
declares `System.Runtime.CompilerServices.IAsyncStateMachine`, judged by
namespace and name only. That is deliberately more inclusive than any trust
policy the index applies, so it cannot under-count the population the index is
answerable for. It is also deliberately *narrow* in one respect: synchronous
iterator state machines are outside it, because the cross-check that recomputes
this population matches only that one interface.

Each invariant below names the gate that enforces it, or is marked
`unverified`, per
[`Asserted properties name their gate`](../evidence-and-validation.md#asserted-properties-name-their-gate).

### C1 — Totality

A published index classifies every structural async state machine the way an
**independent recount of the population** would: resolved where a claim
authenticates and every role receives an admitted disposition, rejected where
identity fails or a role cannot receive an admitted disposition, absent where
no claim exists.

The independence is the entire content of the invariant, and it is easy to
state too weakly. An index that loses a row does not answer with some
distinguished "not reached" value — it answers `Absent`, which is exactly what
it answers for a machine that genuinely has no claim. Nothing *inside* the
index separates those two cases. Totality is therefore only checkable against a
population computed without the index, which is what C6 requires.

Gate: `unverified` as stated.
`StateMachineCompletenessTests.OwnBuildOutputs_EveryStructuralAsyncStateMachineIsAuthenticated`,
`StateMachineCompletenessTests.Neighbours_EveryStructuralAsyncStateMachineIsAuthenticated`,
and
`StateMachineCompletenessTests.CoreLibrary_EveryStructuralAsyncStateMachineIsAuthenticated`
provide narrower implementation evidence. They independently recount
structural machines in deterministic build outputs and require
`Structural == Resolved` with no rejections or absences. They cover only
populations in which every machine is expected to resolve; the absent and
rejected columns remain unverified.

### C2 — Query failures are never success-shaped

After construction fails, every valid `GetByKickoff`, `GetByStateMachine`, and
`GetByImplementation` query reports that failure. Exhausting a bound yields
`BudgetExceeded`; malformed SRM data yields `Malformed`. Neither keyed path
answers `Absent` for rows construction never examined.

`Relationships` reports the same distinction at collection scope:
`StateMachineRelationshipsResult.Available` carries the complete relationship
array after successful construction, including a legitimately empty array, and
`StateMachineRelationshipsResult.Rejected` carries the whole-module failure.
The result is closed rather than an array plus an optional flag, so enumeration
cannot proceed without first observing which state construction reached.

Gate: `StateMachineRelationshipIndexTests.StateMachineRelationshipIndex_PropagatesTypedBudgetFailure`,
`StateMachineRelationshipIndexTests.StateMachineRelationshipIndex_RejectsMethodTableBeyondScanBudget`,
`StateMachineRelationshipIndexTests.StateMachineRelationshipIndex_RelationshipsReportsGlobalFailure`,
`StateMachineRelationshipIndexTests.StateMachineRelationshipIndex_RelationshipsKeepsSuccessfulEmptyDistinct`,
`StateMachineRelationshipIndexTests.StateMachineRelationshipIndex_InvalidMvidPreservesGlobalFailureForValidHandles`,
`StateMachineRelationshipIndexTests.StateMachineRelationshipIndex_PortablePdbReturnsGlobalFailure`.

The first two gates assert that one queried kickoff returns `Rejected` with
kind `BudgetExceeded`. The collection gates distinguish a successful empty
index from whole-module budget failure and require collection and keyed
queries to expose the same immutable failure. The malformed-MVID gate exercises
nil, ordinary out-of-range, and overflow-wrapping GUID handles, and exercises
one valid MethodDef through both method-keyed paths and one valid TypeDef through
the type-keyed path. The Portable PDB gate proves failure recovery cannot throw
again when no module table exists. Exhaustive valid-row coverage for the
malformed whole-module path remains `unverified`.

### C3 — Whole-module failure rejects the whole module

When construction fails for the module, **every** structural async state machine
in that module reports `Rejected` — not `Absent`, and not a mixture.

This does **not** make the two failure paths distinguishable by shape, and an
earlier draft of this section wrongly claimed it did. The implication runs one
way only. A whole-module failure is always total; a per-claim refusal *may*
also be total, because a claim can reach every machine in the module, and in a
single-machine module it necessarily does. So observing a **partial** rejection
proves the failure was per-claim, while observing a total one proves nothing
about which path produced it. Combined with C4, a consumer cannot obtain the
distinction from keyed-result shape; it must inspect the outer `Relationships`
result.

Trimming is explicitly **not** evidence for this invariant. An admitted absent
classic support role now produces a resolved relationship. A trimmed artifact
can still lose identity evidence or retain a malformed role candidate,
producing per-claim refusal rather than whole-module failure. Making that
refusal total does not change which failure path produced it.

Gate:
`StateMachineCompletenessTests.GlobalFailure_RejectsEveryStructuralAsyncStateMachine`.
The gate independently recounts every structural async machine in a
multi-machine compiled fixture and requires a whole-module budget failure to
return the same `Rejected` failure for each one.
`StateMachineCompletenessTests.Sweep_RejectedStateMachine_FailsTheSweep`
provides the total per-claim negative control; it is evidence for the sweep,
not a C3 gate. The malformed whole-module path remains `unverified`.

### C4 — `Failure.Kind` does not identify the cause

`Failure.Kind` alone does not separate a refused claim from a module that
failed to index. `Malformed` and `BudgetExceeded` each arise from both paths.
`Unresolved`, `Ambiguous`, `CrossKind`, and `Duplicate` arise only from the
per-claim path, so the kinds are informative but not decisive.

A consumer needing the distinction must inspect the outer `Relationships`
result, not infer it from `Failure.Kind`, rendered failure text, or the number
of rejected keyed queries. Per C3 a total keyed rejection is consistent with
both paths. `Relationships.Rejected` identifies whole-module failure;
per-claim refusals remain keyed results beneath `Relationships.Available`.

Gate:
`StateMachineRelationshipIndexTests.StateMachineRelationshipIndex_RelationshipsDistinguishesFailureScopeFromKind`.
Its paired `Malformed` and `BudgetExceeded` arms each require the same kind on
one local rejection beneath `Relationships.Available` and one whole-module
`Relationships.Rejected`, so only the outer result identifies scope.

### C5 — Merged rejections agree

A **rejection publication** is the atomic input to merging; a machine is not.
One publication can carry several kickoff, state-machine, and implementation
tokens, several claimed names, one `(Kind, Detail)` pair, and diagnostic
evidence from those identity domains.

A **merge key** is a domain-tagged identity: kickoff MethodDef,
state-machine TypeDef, implementation MethodDef, or a claimed type name
admitted for reuse. The tag matters because equal numeric tokens in different
metadata tables are not the same key. A claimed name carried only as
diagnostic evidence is not thereby a merge key. Two publications are adjacent
when they share a merge key. The component they belong to is the connected
component of that undirected publication graph, so overlap is transitive. This
statement is conditional on a fixed set of publications and keys; it does not
claim that arbitrarily reordering discovery would produce the same
publications.

The published projection of each connected component guarantees:

- Every kickoff, state-machine, and implementation query into the component
  returns the same immutable `Failure` instance.
- Each evidence array contains the distinct union of that evidence contributed
  by every publication in the component.
- The selected `(Kind, Detail)` pair comes intact from one contributing
  publication.

Those are membership and agreement properties, not ordering or selection
properties. Evidence order and the contributing publication chosen for the
reason are unspecified; consumers must not depend on either.

Gate: `StateMachineRelationshipIndexTests.StateMachineRelationshipIndex_MergesEveryOverlappingRejection`,
`StateMachineRelationshipIndexTests.StateMachineRelationshipIndex_RejectsSharedStateMachineClaims`,
`StateMachineRelationshipIndexTests.StateMachineRelationshipIndex_ExpandsAmbiguousClaimsOnce`.

Together these narrower gates cover shared projection across kickoff and
state-machine keys, direct overlap, claimed-name connectivity, unioned kickoff
evidence, and 4,000-item accumulation. Their ordered-token assertion is
stronger than C5 requires.

Transitive closure through mixed key domains, implementation merge keys, the
union of `ClaimedTypes`, state-machine evidence contributed by multiple
publications, and intact selection of `(Kind, Detail)` are `unverified`.

### C6 — Completeness is externally checkable

The population in C1 is derivable from raw metadata without loading the
assembly, without the index, and without trusting either. A cross-check may
therefore recompute it independently and compare.

This is what keeps C1 from being self-certifying, and per C1 it is not optional
garnish: a consumer that asked the index for both the population and the
classification could not detect a lost row at all.

Gate: partial.
`StateMachineCompletenessTests.OwnBuildOutputs_EveryStructuralAsyncStateMachineIsAuthenticated`,
`StateMachineCompletenessTests.Neighbours_EveryStructuralAsyncStateMachineIsAuthenticated`,
and
`StateMachineCompletenessTests.CoreLibrary_EveryStructuralAsyncStateMachineIsAuthenticated`
recount the population from raw metadata without index discovery. The recount
recognizes `TypeReference` and `TypeDefinition` interface encodings;
`TypeSpecification` remains `unverified`.

### Model

[`models/state-machine-completeness/`](models/state-machine-completeness/)
holds two small TLA+ models rather than conflating their state domains.
`StateMachineCompleteness.tla` checks C1, C3, and the structural-async
`GetByStateMachine` fragment of C2, plus cause-specific failure mapping,
absorption, and termination.
`RejectionComponentMerge.tla` checks C5 over published rejections, tagged
merge keys, and diagnostic payloads.

Neither models C4, which is a statement about what a consumer may infer rather
than about system state, or C6 — though C6 licenses C1's formulation, since the
completeness model checks classification against an independently modeled
population rather than against the index's own report.

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
  reconstruction eligibility and proof, stage replay, rendering, and honest
  decline.
- **Research and queries** own composition and presentation.
- **Implementation Diff** owns its populations, correspondence policy, budgets,
  and result shapes.

The shared index does not require those consumers to accept identical
populations. It gives each consumer the same authenticated physical facts so
they do not independently reinterpret state-machine metadata.
