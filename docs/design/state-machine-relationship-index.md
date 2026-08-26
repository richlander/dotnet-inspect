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
names. `StateMachineRelationshipIndex_RejectsMalformedTrustedConstructor`,
`StateMachineRelationshipIndex_RejectsCompetingKickoffClaims`, and
`StateMachineRelationshipIndex_PreservesUnresolvedClaimedType` gate those
distinctions.

## Authentication

A claim enters the index only when all of these conditions hold:

1. The kickoff has a recognized state-machine attribute from an authenticated
   platform assembly or the authenticated current core library.
2. The attribute constructor is an instance `.ctor(System.Type)` returning
   `void`, with no generic parameters or custom-modified parameter type.
3. Its serialized type name is bounded, well formed, and names the current
   assembly when assembly-qualified.
4. The name resolves to one same-module `TypeDef`.
5. One kickoff claims that state-machine type, with one claim kind.
6. The state-machine type directly declares each required interface and each
   required role resolves to one matching instance IL method with a body.

Malformed trusted attributes are rejected, while same-named attributes from
untrusted assemblies are ignored. The distinction is gated by
`StateMachineRelationshipIndex_RejectsMalformedTrustedConstructor` and
`StateMachineRelationshipIndex_IgnoresUntrustedAttributeSpoof`.

The required roles are:

| Claim kind | Required roles |
| --- | --- |
| Classic async | `IAsyncStateMachine.MoveNext`, `IAsyncStateMachine.SetStateMachine` |
| Async iterator | the classic async roles, `IAsyncEnumerator<T>.MoveNextAsync`, and `IAsyncDisposable.DisposeAsync` |
| Synchronous iterator | `IEnumerator.MoveNext`, `IDisposable.Dispose` |

For each role, an exact matching `MethodImpl` declaration wins. Without one,
the index accepts one implicit public virtual implementation with the exact
name and signature. The matcher also rejects custom-modified signatures,
static methods, non-IL methods, and `MethodImpl` bodies declared by another
type.
`StateMachineRelationshipIndex_ResolvesExactInterfaceImplementations`,
`StateMachineRelationshipIndex_ExplicitMethodImplWinsOverNamedDecoy`, and
`StateMachineRelationshipIndex_RejectsInvalidImplementationShapes` gate these
positive and negative forms.

## Bounds and malformed input

Discovery scans at most
`MetadataSafetyPolicy.MaxCorrespondenceMethodRows` `MethodDef` rows. A separate
cumulative relationship budget charges recognized-name attribute candidates,
including the bounded authentication work needed to ignore an untrusted
lookalike, plus interface rows, `MethodImpl` rows, and candidate implementation
methods. Existing signature, custom-attribute, serialized-name, and
metadata-relationship guards bound recursive or allocated decoding.

Exhausting a bound rejects the index with `BudgetExceeded`; malformed SRM data
rejects it with `Malformed`. Neither becomes an empty successful index.
`StateMachineRelationshipIndex_PropagatesTypedBudgetFailure` and
`StateMachineRelationshipIndex_RejectsMethodTableBeyondScanBudget` gate the
visible budget results.

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
