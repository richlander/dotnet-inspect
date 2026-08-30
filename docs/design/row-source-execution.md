# Typed row-source execution

## Status

Focused cross-cutting L1 pattern proposal for
[#5202](https://github.com/richlander/dotnet-inspect/issues/5202), following
the L2 contract locked by
[Section-row shaping](section-row-shaping.md).

The current product has no general row-source execution contract and does not
implement this proposal. All asserted behavior is unverified until the Release
gates in [Required gates](#required-gates) land.

This design uses two established evidence sources:

- the complete L2 reference composition in
  [Section-row shaping](section-row-shaping.md#reference-composition), plus the
  row-query and semantic-selection contracts it composes; and
- current typed planning, result, capability, and completion patterns in the
  query, package-source, Finding, and metadata-projection components.

The protocol below is deliberately linear and excludes concurrent execution
and incremental result publication. A later adoption that introduces either
must model its scheduling, cancellation, and publication interactions rather
than claiming this state table covers them.

Single-use acceptance and publication only after one terminal outcome make the
current transition invariant by construction; this proposal makes no broader
scheduling-model claim.

Related designs:

- [Inspection layers](inspection-layers.md) owns the L1/L2 dependency direction
  and consumer-neutral handoff.
- [Section-row shaping](section-row-shaping.md) owns declared-row-set binding,
  Rows and Count meaning, residual L2 execution, and result binding.
- [Row query and ordering](row-query-order.md) owns predicate, baseline-order,
  ranking, and execution-observation semantics.
- [Semantic row selection](semantic-row-selection.md) owns `Head`, `Tail`,
  `Window`, and `Top` semantics and the complete-sequence reference oracle.
- [Progressive disclosure](progressive-disclosure.md) owns user-visible
  disclosure of non-semantic operational bounds.
- [Untrusted data threat model](untrusted-data-threat-model.md) owns
  containment of internet-origin data.
- [Item and line selection composition](item-and-line-limits.md) sequences this
  pattern with its callers and adopters.

## Authority and scope

The L1 typed row-source execution pattern is the authority for negotiating and
recording one source-owned execution of one caller-formed row request offer.

This design owns:

- request, offer, capability, acceptance, receipt, and evidence identities;
- pure deterministic offer planning;
- the distinction between planning decline and accepted execution;
- the closed logical execution outcomes;
- binding source disposition and completion evidence to the accepted offer and
  its ordered members;
- the rule that fallback is allowed only before acceptance;
- the offer-partition precondition for row-handoff and exact-Count offers;
- validation of source receipts and outcome membership; and
- the source-pattern gates required before optimized row-handoff or Count
  results may be accepted.

This design does not own:

- construction, validation, or ordering of the L2 logical plan;
- the meaning of projection, predicates, baseline order, semantic stages,
  Rows, or Count;
- how a caller groups declared row sets or derives and constructs an offered
  prefix and residual request;
- which operations any existing owner permits a source to execute;
- source-specific acquisition, pagination, retries, caching, merge,
  deduplication, authoritative count APIs, or proof construction;
- CLI syntax, diagnostics, presentation, or disclosure wording;
- source failure taxonomies or provenance lifetimes; or
- concrete implementation APIs.

The caller supplies already-resolved typed offers. This pattern neither reads
the caller's plan nor invents an alternative partition.

## Contract vocabulary

The contract uses ten owner-issued identities:

- **`RowSourceRequestIdentity`** identifies one immutable negotiation.
- **`RowSourceOfferIdentity`** identifies one caller-formed execution
  alternative within that request.
- **`RowSourceInputIdentity`** identifies the immutable, already-authorized
  source input or context to which the offer applies.
- **`RowSourceExecutionGroupIdentity`** identifies the caller-defined ordered
  group to which the offer applies.
- **`RowSourceMemberIdentity`** identifies one ordered member binding within
  the group.
- **`RowSourceCapabilityIdentity`** identifies the source-owned capability
  selected to satisfy an offer.
- **`RowSourcePermitIdentity`** identifies one operation-owner permission
  included by the caller.
- **`RowSourceCompletionRequirementIdentity`** identifies the caller-owned
  completion requirement the source result must satisfy.
- **`RowSourceResidualIdentity`** identifies a caller-retained residual request
  for a row handoff.
- **`RowSourceAcceptanceReceiptIdentity`** identifies one accepted offer
  execution.

Each permit binding also carries one owner-declared
`RowSourcePermitOwnerObservation` value:

- **`SourceClosed`** means the delegated operation cannot produce an
  owner-domain row-query or semantic failure and requires no owner-observable
  callback, comparer, or resolver invocation; or
- **`OwnerObservationRequired`** means the owner contract permits a typed
  owner failure or requires an owner-observable callback, comparer, or resolver
  invocation.

The operation owner fixes this value when it issues the permit. The same permit
identity cannot appear with another value, and a source cannot downgrade the
classification. The pattern reads the classification without interpreting the
operation. This version carries no channel through which a source could
discharge an owner-observable invocation.

Equality is owner-issued token equality only. Display names, option spellings,
provider names, URLs, pagination state, structural plan comparison, and
sequence position do not participate.

An implementation may use the caller's existing declared-row-set and shaping
identities as member or group tokens when their owner explicitly adopts this
pattern. This document does not redefine those identities or require a second
parallel identity system.

## Caller-formed offers

One request contains an ordered, immutable list of execution offers. An offer
contains:

1. the request and offer identities;
2. one immutable source-input identity;
3. one caller-defined execution-group identity;
4. the complete ordered member-identity list;
5. the required source-capability identity;
6. the exact owner-issued execution-permit bindings, each carrying its
   owner-declared owner-observation value;
7. one completion-requirement identity; and
8. exactly one output contract:
   - **row handoff**, naming a caller-owned residual-request identity; or
   - **exact Count**, whose accepted offer identity selects the caller-owned
     Count terminal contract.

The caller owns whether such an offer is semantically legal. An operation with
no owner-issued source-execution permit cannot appear inside an offer merely
because a source claims it can perform similar work. A capability identity is
not permission to reinterpret another owner's operation. The caller may include
or omit one complete permit binding; it cannot author or alter its
owner-observation value.

The residual identity is opaque to the source. It names a residual request the
caller already constructed and retained. The source never returns executable
operations, edits a plan, chooses a cursor, or synthesizes a residual suffix.

An empty offer list means that no source delegation is available. Offer
identities must be unique within one request; member identities and permit
identities must each be unique within one offer. Group, input, capability,
completion-requirement, and residual identities may be reused when the caller
intentionally references the same owner-issued instance. Unknown or
scope-mismatched caller-owned identities reject request construction rather
than selecting an arbitrary binding. A structurally valid owner-issued
capability or permit identity that the selected source does not publish or
cannot honor is unsupported, not invalid; planning declines that offer rather
than returning a contract-validation failure.

## Pure offer planning

Planning validates the complete request before source work begins, then visits
offers in declaration order. It returns exactly one result:

```text
RowSourcePlanResult =
    ContractValidationFailure(typed failure)
  | Declined
  | Accepted(request identity,
             offer identity,
             capability identity,
             acceptance receipt identity)
```

The first offer whose complete output contract, capability, permit set,
owner-observation values, completion requirement, input, and member shape
the source can honor is accepted. If none is supported, planning returns
`Declined`.

Planning is pure. It may inspect immutable capability declarations, but
performs no network, filesystem, source-result cache lookup, provider,
pagination, row-callback, comparer, or source-content work. A decline is a
capability result, not a source failure. The caller may then use a later
non-source strategy, including its complete reference path.

Validation covers the complete request and every offer's structure, owner
scope, uniqueness, and internal compatibility. It does not ask whether the
selected source publishes or can honor a capability or permit. An invalid
request returns `ContractValidationFailure`; no offer's support is probed.

## Acceptance is a point of no fallback

Acceptance binds the exact request, offer, source input, capability, group,
ordered member list, permit bindings, completion requirement, output contract,
and acceptance receipt identity. Execution may begin only from that receipt
and may begin at most once.

After acceptance, the source returns one accepted-execution outcome or
propagates an unexpected implementation exception. It cannot convert a runtime
capability miss, provider failure, cancellation, or insufficient completion
evidence into `Declined`. The caller cannot silently retry a different offer or
the reference path after accepted work because source effects or delegated
observations may already have occurred.

A caller-owned row-handoff residual is not fallback. It is the continuation
named by the accepted offer before source execution began. Only a successfully
validated `RowHandoff` authorizes the caller to enter that residual and supplies
its complete ordered member map. `NotSatisfied`, an exception, or a returned
contract violation is terminal for the accepted offer and never enters the
residual.

## Accepted execution outcomes

The conceptual source result is:

```text
RowSourceExecutionOutcome =
    RowHandoff(acceptance receipt,
               ordered row-source member outcomes)
  | ExactCount(acceptance receipt,
               ordered exact-count member entries)
  | NotSatisfied(acceptance receipt,
                 ordered not-satisfied member entries)
```

A row-source member outcome is:

```text
RowSourceMemberOutcome =
    RowValues(member identity,
              caller-owned values,
              owner-issued source disposition,
              completion evidence)
  | Unavailable(member identity,
                owner-issued source disposition,
                completion evidence)

RowSourceExactCountMember =
    ExactCountValue(member identity,
                    non-negative exact count,
                    completion evidence)

RowSourceNotSatisfiedMember =
    NotSatisfiedMember(member identity,
                       owner-issued source disposition,
                       completion evidence)
```

All branches retain the exact request, offer, source input, group, and ordered
member bindings through the acceptance receipt. Every member from the offer
occurs exactly once and no unknown member occurs.

`RowHandoff` is valid only for a row-handoff offer. Each `RowValues` entry is
Rows-usable for the complete caller-formed offer and may enter the retained
residual request. An `Unavailable` entry contains no row values. A row handoff
preserves usable rows beside unavailable members because the L2 consumer owns
that composition. An expected acquisition, absence, cancellation, or
completion disposition attributable to one member uses that member's
`Unavailable` outcome rather than failing the complete offer.

`ExactCount` is valid only for an exact-Count offer. It contains one
non-negative exact count and matching completion evidence for every offered
member. It cannot contain rows, omit a member, publish a partial count map, or
invent a total across members. Each entry carries its member identity directly;
the source and caller never pair parallel count and evidence lists by
position.

`NotSatisfied` is valid for an exact-Count offer when any member is not exact.
For a row-handoff offer, it is valid only for an offer-scoped expected failure
that prevents the source from producing one complete ordered member map. It
contains one ordered disposition-and-evidence entry per member and no row or
Count payload. It does not replace an `Unavailable` member when the source can
still determine every member outcome. Unexpected programming failures
propagate according to their owning execution contract rather than being
converted into an empty result.

An offer-scoped failure retains that broader scope. Each affected member entry
references the same canonical scoped disposition and evidence value so the
ordered map remains complete, but the contract does not relabel the cause as a
member-scoped failure. Repeated references to that one immutable value are not
duplicate evidence. Because one offer binds exactly one execution group, a
second group-level evidence scope would be redundant.

Physical execution may stream or buffer internally, but no logical success or
partial top-level result is published before one complete outcome validates.
An invalid returned receipt, branch, member map, evidence binding, or payload
is a propagated typed contract violation. It never becomes `Declined`,
`Unavailable`, `NotSatisfied`, or another publishable source outcome.

## Completion evidence

Completion evidence is an immutable typed receipt bound to:

- the request, offer, acceptance receipt, and execution-group identities;
- the source-input identity;
- the exact completion-requirement identity from the offer;
- the source-capability identity actually used; and
- exactly one typed evidence scope, either the complete offer or one member
  identity; and
- one source-owned evidence basis.

Evidence referenced by a member entry is member-scoped by default in every
outcome branch. A member may reference offer-scoped evidence only when that
basis establishes the member's own disposition, usability, or exactness claim.
One offer-scoped value may therefore prove the same offer-wide failure for
every member, but exhaustion of one member cannot prove completeness for
another.

The pattern recognizes three evidence-basis roles:

- **logical exhaustion** — the adopted source contract proved that no further
  value exists in the source domain named by the offer;
- **requirement witness** — the source produced the typed witness required by
  the offer's owner-issued completion requirement; or
- **incomplete stop** — a provider, page, work, time, memory, or acquisition
  bound, or cancellation, stopped execution without satisfying the
  requirement.

The first two may satisfy an offer only when the caller-owned completion
requirement accepts that exact evidence basis. An `incomplete stop` never does.
The pattern validates identity and basis compatibility; the adopting
source owner defines how its evidence is constructed and proves the claim with
its own non-vacuous gate.

Evidence is not inferred from row or Count values. Returning exactly the
requested number, returning fewer rows than a page size, receiving an empty
page, or observing a provider-specific terminal token proves nothing unless
the adopted source contract constructs the matching evidence.

Stale evidence from another request, offer, receipt, input, group, scope,
capability, or completion requirement rejects. Evidence cannot be transferred
because two requests are structurally equal. The composite request/offer/receipt/input/group/scope/capability/requirement
binding is the evidence key; two evidence values for the same key are
duplicates even though evidence has no separate identity token. Multiple
member entries may reference one canonical offer-scoped evidence value; a
duplicate means two distinct evidence values claim the same composite key.

## Rows usability and Count sufficiency

Rows usability and Count sufficiency are different conclusions:

- `RowValues` means the values are usable for the complete accepted
  row-handoff offer and its named residual request.
- `ExactCount` means every count is sufficient for the complete accepted Count
  offer.
- `Unavailable` and `NotSatisfied` are neither.

A row handoff may carry incomplete-stop evidence when the caller-formed
Rows contract permits incomplete rows with disclosure. The same evidence is
not thereby sufficient for Count.

An exact Count requires evidence accepted by the offer's completion
requirement for every member. One insufficient, failed, absent, or missing
member forces `NotSatisfied`; successful-looking counts for the other members
do not escape. A completion requirement may accept one offer-scoped basis only
when that basis proves every member's exact count; a group aggregate that
cannot establish the individual member values is insufficient.

## `Head(N) -> Count` as the canonical witness

The `RowSelection` owner defines `Head(N)` as a lenient clamp. The L2 owner
defines Count as the exact cardinality after that clamp. An adopting caller may
therefore form an exact-Count offer whose completion requirement accepts
either:

- a requirement witness proving that N applicable ordered rows reached the
  clamp; or
- logical exhaustion proving that fewer than N applicable rows exist.

The source may return N immediately after the first proof. It may return
`k < N` only after the second. A provider or work cap equal to N is
incomplete-stop evidence, not the required witness.

This version can accept the exact-Count offer only when every operation in the
resolved plan for every offered member is covered by a `SourceClosed` permit.
An exact-Count offer has no residual. An offer containing an
`OwnerObservationRequired` permit is well formed but unsupported; if an
operation lacks a permit, the caller cannot form the exact-Count offer. In
either case, the caller uses a row handoff or the reference path so the
remaining operations and Count execute under their owner-defined observation
and failure contract.

This example applies the adjacent owner's locked semantics; it does not move
`Head` or Count meaning into this pattern.

## Other delegated observations

An offer's permit set must cover every observation the delegated work can make,
including any owner-defined callback invocation, exception identity, failure
precedence, ordering, or all-or-failure boundary.

The pattern does not decide which row-query or semantic operations are
delegable. In particular:

- an operation without a permit is a barrier;
- a source capability cannot waive a required callback;
- a completion witness cannot replace an earlier strict-stage requirement; and
- an exact value cannot compensate for different failure or callback
  observations.

For a row-handoff offer, **safe prefix** means that the delegated operations
form one contiguous reference-order prefix of every member's resolved plan.
The named residual contains every operation at and after the first omitted
operation in the same reference order. The prefix and residual are disjoint,
together cover the complete plan, and apply no operation twice. For an
exact-Count offer, the delegated operations cover the complete resolved plan
because no residual exists. The caller owns constructing these partitions and
proving them against its reference plan; the source receives only the opaque
offer, permit bindings, and residual identity.

This version supports only permits classified `SourceClosed`. A presented
offer containing an `OwnerObservationRequired` permit is well formed but
unsupported, so planning may continue to a later offer or decline. The caller
may instead omit that operation from the offered work and retain it in a
row-handoff residual; the barrier applies to delegation, not to the entire
logical request. Strict `Window` and the current row-query and semantic
callback, comparer, and resolver contracts require
`OwnerObservationRequired`. Adding an owner-observation or failure-transport
channel is a separate focused extension to this outcome algebra, not an
implementer choice.

If the caller cannot form a permitted offer that preserves those observations,
planning declines and the reference path remains authoritative.

## Deterministic transition order

The logical transition order is:

1. validate request identity and the complete ordered offer list;
2. validate every offer's identities, member map, permit set, completion
   requirement, and output contract;
3. probe complete-offer support in declaration order and accept the first
   exactly supported offer, or decline all;
4. when accepted, bind one immutable acceptance receipt;
5. execute that receipt at most once;
6. validate the returned receipt, branch, member order, evidence, and payload
   invariants; and
7. publish one complete outcome; and
8. only for a validated `RowHandoff`, enter its named caller-owned residual
   with the complete ordered member map.

The first validation failure wins. Validation examines every offer before
support probing begins. After acceptance, no later offer's support is probed
and no alternative is tried after accepted execution fails.

## Security and platform boundary

Remote content does not mint request, offer, capability, permit, member,
completion-requirement, or receipt identities. Source-specific remote text does
not enter this pattern as a diagnostic string; an adopter carries only its
owner-issued contained disposition and evidence types.

The contract authorizes no source, endpoint, credential, cache, or filesystem
path. Host and source owners perform that authorization before execution.

The shared contract must remain host-neutral, NativeAOT-compatible,
Browser/Wasm-compatible, reflection-free, and free of dedicated-thread,
filesystem, network, console, process, or native-interop dependencies.
Adopters may use platform capabilities in their own owning components only
under those components' existing platform contracts.

## Required gates

The pattern implementation and each optimized adoption must add the applicable
named Release gates:

| Gate | Contract |
| --- | --- |
| `RowSourceIdentitiesAreOwnerIssued` | Request, offer, input, group, member, capability, permit, completion-requirement, residual, and receipt identities use owner-issued token equality; display and structurally equal plans do not bind. Each permit identity binds one immutable owner-declared owner-observation value. |
| `RowSourceRequestValidationIsAtomic` | Duplicate offer identities, duplicate members or permits within one offer, and unknown, missing, empty, scope-mismatched, or incompatible caller-owned identities, member maps, permit classifications, requirements, residuals, or output contracts return the deterministic first `ContractValidationFailure` and probe no offer support; intentional reuse of the same input, group, capability, requirement, or residual identity across offers remains valid. A well-formed capability or permit that the source does not publish or cannot honor remains an unsupported offer. |
| `RowSourcePlanningIsPure` | Planning inspects only immutable capability declarations and performs zero source, provider, source-result cache, filesystem, network, row-callback, comparer, or content operations. |
| `RowSourceSelectsFirstSupportedOffer` | After complete structural validation, offers are support-probed in declaration order; the first offer whose complete output contract, capability, permit set, owner-observation values, completion requirement, input, and member shape are supported is accepted, no later offer's support is probed, and an all-declined request performs no execution. |
| `RowSourceDeclineAllowsReferenceFallback` | A pure all-offers decline permits the caller's retained reference strategy and is never reported as a source failure. |
| `RowSourceAcceptanceIsSingleUse` | One acceptance receipt binds the exact request and offer, executes at most once, and rejects replay or a receipt from any other negotiation. |
| `RowSourceResidualIsCallerOwned` | Only a successfully validated `RowHandoff` resolves to the residual identity retained for its accepted offer and supplies that residual's complete ordered member map; the source cannot return operations, replace the residual, select a different caller plan, or enter the residual from `NotSatisfied`, an exception, or a contract violation. |
| `RowSourceOfferPartitionMatchesReference` | The caller's adoption gate proves that every row-handoff offer delegates one contiguous reference-order prefix and retains the exact disjoint suffix in its residual, with complete coverage and no duplicated operation for every member. It rejects a non-prefix partition before presenting the offer; that caller-side precondition failure is outside `RowSourcePlanResult`, and this pattern performs no plan or partition check. Every exact-Count offer covers the complete resolved plan with one permit binding per operation because it has no residual. |
| `RowSourceOutcomeMembershipIsExact` | Every outcome preserves offer member order, contains every member exactly once with an explicit member identity, rejects unknown or duplicate members, and never reconstructs identity from position, parallel-list position, or source labels. |
| `RowSourceRowHandoffMatchesOffer` | `RowHandoff` occurs only for a row-handoff offer; every `RowValues` entry is usable for that complete offer and residual, every `Unavailable` entry carries no rows, and every entry's member- or offer-scoped evidence establishes that member's own claim. |
| `RowSourceExactCountIsAtomic` | `ExactCount` occurs only for an exact-Count offer, contains one non-negative exact value and accepted completion evidence per member, carries no rows, preserves order and identity, and publishes no partial map or invented total. |
| `RowSourceNotSatisfiedCarriesEvidence` | An inexact accepted Count or an offer-scoped row-handoff failure that prevents a complete member map returns one explicit-identity disposition-and-evidence entry per member with no rows or Count payload; the broader failure retains offer scope through repeated references to one canonical evidence value, and a determinable member-scoped Rows failure remains `Unavailable` inside `RowHandoff`. |
| `RowSourceAcceptedFailureNeverFallsBack` | Removing capability after acceptance, reaching an incomplete stop, returning an accepted member- or offer-scoped source failure, throwing, or rejecting a returned contract never tries a later offer, reference execution, or row-handoff residual; only a validated `RowHandoff` may enter its preselected residual. |
| `RowSourceCompletionEvidenceIsBound` | Evidence matches the exact request, offer, receipt, source input, group, typed offer/member scope, capability, and completion-requirement identities; stale, transferred, missing, incompatible, or distinct duplicate-key evidence rejects, while repeated references to one canonical offer-scoped value remain valid only when it establishes each referencing member's claim. Exact Count additionally requires proof of every member value. |
| `OperationalBoundsNeverProveCompletion` | Provider, page, work, time, memory, acquisition, and cancellation bounds remain incomplete even when their numeric value equals a requested semantic bound or returned row count. |
| `RowsUsabilityAndCountSufficiencyStayDistinct` | A capped row-handoff offer may return Rows-usable values with incomplete-stop evidence, while the corresponding exact-Count offer returns `NotSatisfied` and no cardinality. |
| `PermitOwnerObservationIsDeclared` | Every permit identity binds exactly one immutable operation-owner classification. Substitution, omission, or downgrade rejects, and the operation owner's adoption gate proves each permit's `SourceClosed` or `OwnerObservationRequired` declaration against its reference failure and invocation contract. |
| `DelegatedObservationsMatchPermits` | Every supported offer preserves all ordering and atomic-publication obligations named by its exact `SourceClosed` permit bindings. Removing a binding makes the offer unsupported rather than weakening an observation. |
| `OwnerObservationsRemainReferenceBarriers` | An offer that presents an `OwnerObservationRequired` permit is unsupported and declines before execution. A later safe-prefix offer that omits the barrier may still be accepted with the owner-observed operation retained in its residual; the reference or residual path preserves exact invocation, failure identity, scope, all-or-failure behavior, and precedence. |
| `OptimizedRowHandoffMatchesSectionRowReference` | The optimized row-handoff path is proven to execute and, after its named residual, matches the complete section-row reference result for surviving values, order, member identity, unavailable-member composition, source evidence, owner-observable invocation, and terminal failure. Separate accepted-safe-prefix fixtures exercise a typed strict-window residual failure and an exact sentinel callback/comparer/resolver exception; another fixture makes both reachable and preserves reference precedence. None publishes partial rows. The caller's partition gate rejects a reordering sentinel before presenting it to source planning. Query, ordering, and semantic-operation cases are required only when the adoption defines matching `SourceClosed` permits. |
| `OptimizedCountMatchesSectionRowReference` | The optimized path is proven to execute and matches the complete section-row reference result for empty, below-bound exhausted, bound-satisfied, oversized, multi-member, and sentinel-failure cases; insufficient evidence rejects rather than succeeding. Query, ordering, and semantic-operation cases are required only when the adoption defines matching `SourceClosed` permits. |
| `RowSourceOutcomePublicationIsAtomic` | Streaming or buffered physical strategies expose no logical success or partial member map before the complete validated outcome. |
| `RowSourceReturnedContractViolationsPropagate` | A stale receipt, wrong branch, missing or reordered member, invalid evidence binding, or incompatible payload returns no logical source outcome and propagates as the deterministic typed contract violation rather than `Declined`, `Unavailable`, or `NotSatisfied`. |
| `RowSourceContractIsPresentationFree` | Requests, offers, receipts, outcomes, dispositions, and evidence contain no CLI spelling, heading, formatted value, diagnostic sentence, renderer state, or provider display label. |
| `RowSourceContractHasOnlyFrameworkDependencies` | The shared contract's evaluated Release compile/runtime/native assets contain only framework references and the contract component. |
| `RowSourceContractForbidsHostApis` | A static product-closure gate rejects reflection, inspected-assembly loading, filesystem, network, console, process, native-interop, parallel-loop, and dedicated-thread APIs even when they are framework APIs. |
| `RowSourceContractRunsOnNativeAotAndBrowser` | The request, planning, accepted execution, row-handoff, exact-Count, and not-satisfied reference matrix runs in Release under NativeAOT and single-threaded Browser/Wasm hosts. |

## Non-claims

This design does not assert that any current source can accept an offer, define
an L2 offer-construction policy, choose a source-specific proof, or change
current product behavior. Every adoption remains a separate focused effort by
the source or caller owner.
