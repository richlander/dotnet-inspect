# Analysis universe realization

A validated analysis request proves that a finite universe description can
support the requested question. It does not make that universe executable.
This design owns the operation-scoped handoff that binds the exact description
and its satisfied requirements to owner-issued population access, operations,
and lifetimes.

Tracking: [#3629](https://github.com/richlander/dotnet-inspect/issues/3629).

## Status

This is target design. `AnalysisUniverseDescription` and
`AnalysisRequestPlan` implement the descriptive and pre-execution side, while
Workspace owns retained assembly-context groups and query access. No production
contract currently joins those sides without exposing owner internals or
reconstructing their semantics.

The first intended adopter is Workspace-backed analysis. Integration Census is
a prospective consumer after its separate #5319 incidence adoption, not part
of this owner's contract.

## Authority

**Analysis Universe Realization** owns:

- the owner-issued offer that retains an authenticated route from one
  description back to its universe provider;
- exact correspondence from one finite `AnalysisUniverseDescription` to its
  executable realization;
- exact binding of a validated plan's universe requirements to owner-issued
  executable capabilities;
- immutable capability-owner-declared population and context order;
- operation-scoped access that retains every required owner lifetime;
- issuance rejection before producer execution; and
- preservation of universe completeness and failures across the handoff.

The owner defines how adjacent contracts compose. It does not define their
subject identities, population selection, binding policy, producer algorithms,
outcome semantics, or presentation.

## Imported owner contracts

| Owner | Imported contract |
| --- | --- |
| [Analysis surfaces and universes](analysis-surfaces-and-universes.md) | The exact finite universe description, validated plan, satisfied requirement identities, and pre-execution rejection boundary. |
| [Artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md) | Admitted participants, assembly-context groups, provenance, query authorization, access leases, failures, and close behavior. |
| [Inspection space](../inspection-space.md) | Typed query planning, deterministic sequential execution, optional equivalent concurrency, and narrow producer contexts. |
| [Inspection layers](inspection-layers.md) | L1 prerequisite closure, scope, execution order, and producer invocation. |
| [Structured type-forwarding resolution](type-forwarding-resolution.md) | Metadata-owned requests, context generations, binding policy, exact resolution outcomes, and terminal forwarding evidence. |
| [Progressive disclosure](progressive-disclosure.md) | Host preflight, capability authorization, and cost enforcement for the exact operation. |
| [Integrations](integrations.md) | A prospective consumer's participant, Type, binding-context, and exact-resolution requirements; #5319 owns its incidence adoption. |

## Roles

The contract has five closed roles:

- the **analysis owner** issues requirement descriptors that reference exact
  universe capability descriptors;
- the **universe provider** issues one universe offer containing the description
  and the authenticated route to its description-scoped realization;
- each **capability owner** defines the typed population, operation, identity,
  lifetime, and outcome contract carried by an executable binding;
- the **execution coordinator** retains the offer, submits its description for
  planning, obtains plan-specific execution access, and schedules the accepted
  plan; and
- the **producer** consumes only its typed executable bindings without gaining
  universe-provider construction authority.

The host normally supplies the execution coordinator. An accepted plan returns
to the retained offer for execution binding. The plan does not discover a
provider by identity lookup, service location, or inspection of the
description.

## Contract

An **analysis universe realization** is immutable provider state corresponding
to one finite universe description. It grants no query access and exposes no
population by itself. An **execution access** is the operation-scoped handoff
that binds one validated plan to typed capability access and owner-issued
leases over that realization.

Several plans may reuse one realization, but each receives separate execution
access. Population rosters, context access, operations, and authorization are
available only through that plan-specific access.

The universe provider issues an offer that retains both the exact description
and its authenticated binding route. A consumer never enumerates a mutable
workspace, reads private group state, derives an executable population from
descriptive fields, or supplies a provider obtained out of band.

### Exact description correspondence

The offer and realization retain the exact owner-issued
`AnalysisUniverseDescription` used by the validated plan. An independently
constructed description with equal-looking identity, boundary, capability, or
display values is not interchangeable. An offer rejects a description it did
not issue even when every visible field appears equal.

The realization neither widens nor narrows the requested or realized boundary.
Selecting additional workspace content, adding a context, or changing a
completeness limit produces another owner-issued description and realization.
A retained superset workspace may back several finite realizations without
reacquiring equal content, but each realization preserves its own exact
boundary and membership.

The realization is projection-neutral. Several separately validated plans may
use it only when each retains the exact description and independently receives
execution access, authorization, and requirement bindings for its operation.
Reuse of retained content or capability state does not transfer one plan's
projection validation or host grant to another.

### Executable requirement bindings

For one validated plan, execution access contains exactly one binding for each
retained `AnalysisUniverseRequirementDescriptor`, keyed by the exact retained
requirement object and its owner-issued identity rather than the identifier
alone. The universe provider resolves the requirement through its declared
`AnalysisUniverseCapabilityDescriptor`, which must be the same descriptor
instance retained by the universe description.

One executable capability may satisfy several requirements that reference the
same exact capability descriptor. Each requirement still has one binding; the
bindings may share the capability owner's access rather than acquire duplicate
state. Missing, duplicate, extraneous, foreign-provider, or merely name-equal
bindings reject issuance before producer execution.

Each capability binding retains:

- the exact requirement identity it satisfies;
- its capability-owner-issued typed population, operation, or context access;
- deterministic declared order for every roster it carries;
- the scope and lifetime in which its identities and operations remain valid;
  and
- typed completeness, rejection, unavailability, and failure evidence required
  by that capability.

The realization contract does not replace these typed payloads with one
universal subject model or string-keyed capability dictionary. A Type roster,
participant roster, manifest population, binding-context roster, and exact
resolution operation keep their respective owners and types.

Capability declaration remains distinct from execution outcome. A validated
plan proves that the universe provider declares the required capability over
the finite boundary. It does not prove executable binding, promise that every
member is healthy, or promise that every producer attempt succeeds.

### Population, context, and incidence

When a requirement declares an ordered population, its capability owner issues
the complete finite roster in deterministic declared order. Every entry in the
requested boundary and every realized entry has exactly one typed terminal
population outcome. Rejected, unavailable, and failed entries are retained
rather than filtered out.

When a requirement declares evaluation contexts, its capability owner issues:

- a finite ordered context roster;
- one stable owner-issued identity for each context;
- the authoritative population-to-context incidence; and
- narrow context access for the operation.

Context identity is not inferred from object identity, collection position,
display text, an assembly-context group's binding-policy version, or a
project-specific wrapper. A binding-policy version remains policy snapshot
identity. The provider separately establishes whether two population entries
are evaluated in the same context.

One execution access may contain one or several contexts. A
binding-consistent group of co-dependent assemblies can supply one context;
comparisons across package versions or framework contexts can supply several.
The context binding records the capability owner's boundary and incidence but
does not infer relationships across groups.

Consumers derive expected context-addressed work from the capability-owner-issued
incidence, never from an assumed Cartesian product. A total relation is valid
only when the issued incidence explicitly places every relevant population
entry in every context.

### Lifetime and access

Execution access is an owner-issued lease over the exact realization and plan.
Before publishing that access, the universe provider acquires only the
population, context, content, and query leases that their owners issue for the
operation. A failed or cancelled issuance releases any partially acquired
access and does not publish a partially usable result.

While execution access is live:

- capability-owner-issued handles and operations remain usable only while their
  own authorization, generation, and lease contracts permit;
- a workspace or catalog close follows its existing lease and quiescence
  contract;
- per-observation authorization revalidation remains in force;
- the consumer cannot widen access or obtain another capability by inspecting
  retained objects; and
- producer results may materialize portable evidence but cannot leak readers,
  mutable workspace state, or lease-bound handles beyond their owner lifetime.

Release prevents future use through that execution access and releases its
retained owner leases. Immutable population and context identity values may
remain in a typed result and keep the comparison semantics their owner grants
inside the original workspace generation; non-portable does not mean
lease-bound. Operations, readers, and access handles expire. Already
materialized portable results remain valid under their own contracts.

The realization neither pins a Metadata catalog generation nor caches an
authorization decision for the whole operation. Authorization narrowing,
policy-version change, or catalog-generation supersession surfaces through the
adjacent owner's typed rejection or failure. The realization does not mask,
retry, or reinterpret it.

A later operation obtains new authorization and execution access even when the
host retains the same workspace.

### Failure and completeness

The realization preserves the exact universe completeness and failures retained
by the description. Capability bindings preserve their more specific
population and access outcomes. Neither layer may convert a non-success into an
omitted member, empty successful population, zero count, negative
classification, or absence proof.

An issuance mismatch or unavailable required access rejects execution before a
producer runs. A member-specific non-success after valid issuance remains a
typed capability or producer outcome attributed to that member and context.
Unexpected defects remain fatal under the producing owner's contract.

The realization does not reinterpret adjacent failure algebras. It preserves
their typed payloads and the correspondence needed for a producer to determine
which domain is incomplete.

### Deterministic sequential baseline

The realization performs no producer execution and chooses no scheduler. It
provides immutable population order, context order, incidence, and capability
access so the inspection-space executor can run the plan sequentially.
Sequential execution is the normative Browser/Wasm baseline.

A future executor may use the same realization concurrently only where each
capability owner permits concurrent access. Concurrency cannot change
authorization, membership, context identity, incidence, result order, failure
visibility, budgets, or lifetime validity. The provider does not require
threads to issue or consume a realization.

## Issuance outcomes

Issuance has a closed top-level result:

| Outcome | Meaning |
| --- | --- |
| Ready | Carries execution access to the exact realization and every required executable binding. |
| Rejected | Carries a typed pre-producer reason and no usable execution access. |
| Cancelled | Preserves host cancellation after releasing partial access; it is not a rejection or failure-shaped result. |

Rejection distinguishes at least:

- plan and realization universe mismatch;
- foreign offer or description issuer;
- changed provider boundary;
- missing, duplicate, extraneous, or wrong-identity requirement binding;
- invalid population or context order, identity, or incidence;
- description completeness or failure mismatch;
- denied operation authorization; and
- unavailable or failed required lifetime acquisition.

The specific authorization, population, Metadata, and acquisition failures
remain owner-issued payloads rather than being flattened into display text.

Request planning rejects a description that does not declare a required
capability. Realization issuance separately rejects an accepted plan when the
provider cannot bind that declared capability to executable access or when its
runtime roster, context identity, or incidence contradicts the description.

## Demo

The documentation can be read against this mock composition:

```text
validated Workspace analysis plan
  universe: workspace-types-v1
  requirements:
    source participants
    selected Types
    binding contexts
    exact peer resolution

retained Workspace
  group net8.0: App + Dependencies
  group net9.0: App + Dependencies

owner-issued offer workspace-types-v1
  description + authenticated route to its realization

plan-specific execution access
  participant binding: [App, Dependencies, RejectedPlugin]
  Type binding:        [App.Client, Dependencies.Service]
  context binding:     [net8.0, net9.0]
  incidence:           App.Client -> [net8.0, net9.0]
                       Dependencies.Service -> [net8.0]
  resolution binding:  exact context-bound operation access
  population failure:  RejectedPlugin -> typed rejection

sequential executor
  consumes the declared orders and narrow access
  never enumerates Workspace or reconstructs a context identity
```

The rejected participant remains visible, both contexts remain distinct, and
the same source Type can be evaluated in each context without changing its
identity. A neighboring single-context analysis uses the same handoff with one
context; no special concurrent or matrix path is required.

This mock demonstrates realization and access only. It is not an abbreviated
Integration catalog. Integrations owns its complete requirement set, producer
attempts, candidates, dispositions, and projections; #5319 owns its adoption
of provider-issued context incidence.

## Close negative cases

| Case | Required result |
| --- | --- |
| Equal-looking description supplied instead of the plan's exact description | Rejected before producer execution |
| Exact description is paired with an offer from another provider | Rejected before producer execution |
| Required capability is declared but has no executable binding | Rejected before producer execution |
| Provider binds one requirement twice or adds an unrequested binding | Rejected before producer execution |
| Two requirements reference one exact capability | Two requirement bindings may share one capability-owner access |
| Population capability owner omits a rejected or failed member | Rejected as inconsistent completeness |
| One participant occurs in two contexts | One participant identity with provider-issued incidence to two distinct context identities |
| Two contexts share a binding-policy version | They remain distinct unless the context capability owner says they are the same context |
| Workspace contains an unselected peer | It is not admitted merely because retained content exists |
| Wider plan uses the same retained workspace | New description or realization boundary; no implicit widening |
| Required lease cannot be acquired | Typed issuance rejection and no partial access |
| Cancellation occurs after some access is acquired | Partial access is released and cancellation remains cancellation |
| Workspace close begins during sequential issuance | Issuance either obtains valid owner leases or returns no access; no partial state publishes |
| Workspace close begins during active execution | Existing owner lease and quiescence rules apply; no reconstructed fallback |
| Authorization narrows or a Metadata generation is superseded | Adjacent-owner typed non-success; realization does not use cached authority or pin the generation |
| Producer finds no evidence | Producer-owned completed empty outcome, not realization failure |
| Producer or exact resolution fails for one member | Typed member/context outcome; unrelated domains remain governed by their owners |

## Model assessment

This contract adds no mutable generation, scheduler, concurrent publication, or
new close protocol. Issuance is sequential and publishes only after all
owner-issued access is acquired; close during issuance is resolved entirely by
those owners' existing lease-acquisition results. The contract composes
existing immutable plan, workspace, catalog, authorization, and lease contracts
and retains sequential execution as the baseline. A new TLA+ model would
duplicate those owners without checking a new state machine.

If an adoption introduces mutable realization membership, concurrent
realization publication, concurrent issuance and close state outside existing
lease acquisition, incremental context addition, or a new lifetime protocol
rather than composing existing leases, stop and model that interaction before
implementation.

## Adoption sequence

1. Lock this reusable realization contract.
2. Adopt it in Workspace with one focused implementation effort that issues
   executable bindings without exposing mutable workspace internals.
3. Adopt it in the Integration Census executor under the Integration owner,
   including the context-incidence change tracked by #5319.
4. Let later analysis owners adopt the same pattern independently.

The pattern PR does not implement the first adopter under the bounded
first-adopter exception. Workspace adoption remains the next separate slice.

## Non-claims

- No universal subject identity, result IR, or serialized universe format.
- No ownership of universe selection, acquisition, or workspace composition.
- No mutable workspace or assembly-context-group enumeration surface.
- No redefinition of Metadata binding, forwarding, or resolution outcomes.
- No producer registry, execution algorithm, candidate policy, or result
  semantics.
- No Integration inventory, graph, matrix, Section, or `find` behavior.
- No automatic acquisition, universe widening, network authorization, or cost
  grant.
- No concurrency requirement; single-threaded Browser/Wasm remains supported.
- No portable identity claim for workspace-scoped participants or contexts.
- No claim that a non-portable identity value expires with execution access;
  its owner defines its workspace-generation comparison scope.
- No claim that equal content, assembly identity, policy version, or display
  text proves correspondence.

## Verification

The design remains unverified until a Workspace adoption lands these named
gates:

- `AnalysisUniverseRealization_RequiresExactDescription`
- `AnalysisUniverseRealization_RejectsForeignProviderOffer`
- `AnalysisUniverseRealization_BindsEveryPlanRequirementExactlyOnce`
- `AnalysisUniverseRealization_OneCapabilityMayBackSeveralRequirements`
- `AnalysisUniverseRealization_RejectsExtraneousExecutableBinding`
- `AnalysisUniverseRealization_PreservesProviderOrderAndFailures`
- `AnalysisUniverseRealization_UsesOwnerIssuedContextIdentity`
- `AnalysisUniverseRealization_DoesNotUseBindingPolicyVersionAsContextIdentity`
- `AnalysisUniverseRealization_PreservesPopulationContextIncidence`
- `AnalysisUniverseRealization_KeepsOwnerAccessAliveUntilRelease`
- `AnalysisUniverseRealization_IdentityValuesSurviveAccessReleaseWithinScope`
- `AnalysisUniverseRealization_RejectedIssuanceReleasesPartialAccess`
- `AnalysisUniverseRealization_CancellationReleasesPartialAccess`
- `AnalysisUniverseRealization_CloseDuringIssuancePublishesNoPartialAccess`
- `AnalysisUniverseRealization_DoesNotCacheAuthorizationOrPinMetadataGeneration`
- `AnalysisUniverseRealization_DoesNotExposeMutableWorkspace`
- `AnalysisUniverseRealization_CompatiblePlansRequireIndependentAuthorization`
- `AnalysisUniverseRealization_WiderBoundaryRequiresNewRealization`
- `AnalysisUniverseRealization_SequentialExecutionUsesDeclaredOrder`
- `AnalysisUniverseRealization_HasNoThreadingRequirement`

Integration-specific execution, candidate, completeness, row, graph, and matrix
gates remain owned by [Integrations](integrations.md).
