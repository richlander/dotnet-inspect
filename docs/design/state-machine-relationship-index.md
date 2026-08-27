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
constructor, method, and TypeSpec blob that is decoded or compared. A cumulative
name-work budget bounds both metadata names materialized while classifying
distinct constructors and serialized state-machine names before attribute
decoding. Reading a type-name chain charges every node it consumes, including
nil-named ones: a nil component decodes to zero characters, so a charge keyed
only on decoded length would account for nothing while the read still allocates
one segment per node, letting a deep chain of nil-named nodes materialize
proportionally for free. The chain's own structural cost — one reference per
node in the builder and again in its immutable copy — is charged up front for
the same reason. State-machine `System.Type` values also receive an individual encoded
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
`StateMachineRelationshipIndex_BoundsAttributeNameMaterialization` and
`StateMachineRelationshipIndex_CachesConstructorAuthentication` gate
cumulative constructor-name work and reuse; and
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
`StateMachineRelationshipIndex_ChargesNilNamedTypeNameChainNodes` gates the
nil-component and structural chain charges together. Its rejecting arm uses a
budget that admits whenever only one of the two charges is present, so deleting
either one fails the gate; its admitting arm fails if the charges reject an
image the budget should admit.
`StateMachineRelationshipIndex_RejectsNamedArgumentsBeforeDecode` gates the
value-blob preflight; and
`StateMachineRelationshipIndex_ExpandsAmbiguousClaimsOnce` gates that
kickoff-by-duplicate fan-out stays linear while preserving every kickoff and
type-definition candidate in the merged failure.

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
