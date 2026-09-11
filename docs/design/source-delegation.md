# Source delegation

## Problem

The row and limit contracts define observable meaning by reference execution:
acquire the complete logical sequence, then apply predicates, ordering,
semantic selection, and Rows or Count locally. Real sources — package feeds,
indexes, caches — can often do part of that work far more cheaply: filter
upstream, stop after the requested clamp, or answer a count from exact index
metadata. But they can only do so operationally, through caps, pages, and
provider signals that resemble answers while proving nothing: a provider cap
equal to the requested N looks exactly like N applicable rows, and an empty
page looks like exhaustion.

Without a contract, every such optimization either silently changes
observable semantics or is rejected wholesale, leaving every command to pay
complete acquisition cost. This document defines the narrow agreement that
makes the optimization safe: a caller may delegate a proven prefix of its
resolved plan to a source, the source returns one closed result carrying
completion evidence, and that result substitutes for the reference
computation only when the evidence proves the substitution exact. Everything
else in this document serves that sentence.

Both directions of this arrangement are delegations. The default composition
already delegates in the natural direction: every source hands raw member
values to one centralized result-construction path, hardened by and serving
all sources at once. This contract governs the reverse direction — a source
taking over a prefix of result construction — which carries a naturally
higher bar, because each reverse-delegating source re-implements observable
semantics the centralized path provides once. The contract makes that bar
explicit: delegated work stays behind the source-closed barrier, satisfaction
is proven with completion evidence rather than asserted, accepted work never
falls back silently, and every adoption passes an equivalence gate against
the centralized reference.

## Status

Focused cross-cutting L1 pattern for
[#5202](https://github.com/richlander/dotnet-inspect/issues/5202), following
the L2 contract locked by [Section-row shaping](section-row-shaping.md). This
revision restructures the draft reviewed on
[#5209](https://github.com/richlander/dotnet-inspect/pull/5209): the same
semantic guarantees, reframed as a small effect protocol plus a delegated
result contract, with structural binding replacing token policing.
[#5235](https://github.com/richlander/dotnet-inspect/issues/5235) owns the
independently-adoptable-tier proof for the row and limit systems; this
contract is written to be adoptable through its public surface alone, without
dotnet-inspect's layer names, section rendering, or CLI.

The shared protocol is implemented in
[`DotnetInspector.SourceDelegation`](../../src/DotnetInspector.SourceDelegation/).
Its first exercising consumer is the
[public contract harness](../../tests/DotnetInspector.SourceDelegation.Tests/),
whose Release suite runs in PR CI. Production source and caller adoption remain
separate work; this slice does not change Gallery, L2, browser, or CLI execution.

[#6042](https://github.com/richlander/dotnet-inspect/issues/6042) owns the shared
protocol implementation and public contract harness. It is milestone 6 of the
eight-step [Gallery adoption path #5919](https://github.com/richlander/dotnet-inspect/issues/5919).
Milestone 7 adopts the protocol through Gallery acquisition, L2 finite-input
binding, and the existing website query path; milestone 8 adds CLI execution.
The website's current ordinary-acquisition path remains supported until that
focused adoption replaces it.

This design uses two established evidence sources:

- the complete L2 reference composition in
  [Section-row shaping](section-row-shaping.md#reference-composition), plus
  the row-query and semantic-selection contracts it composes; and
- current typed planning, result, capability, and completion patterns in the
  query, package-source, Finding, and metadata-projection components.

The protocol is deliberately linear and excludes concurrent execution and
incremental result publication. A later adoption that introduces either must
model its scheduling, cancellation, and publication interactions rather than
claiming this contract covers them.

Related designs:

- [Inspection layers](inspection-layers.md) owns the L1/L2 dependency
  direction and consumer-neutral handoff.
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
- [Item and line selection composition](item-and-line-limits.md) sequences
  this pattern with its callers and adopters.

## Framing

This document defines two things and nothing else:

- **An effect protocol** — four rules governing when source delegation may be
  attempted, committed, and abandoned. These rules are temporal: delegated
  execution has effects and observations that cannot be undone, so the order
  in which commitment and fallback are allowed is itself the contract.
- **A delegated result contract** — the conditions under which a source
  result may substitute for the caller's reference computation: what was
  delegated, what came back, what proves it, and when substitution is
  refused.

The result shape here is rows and row counts, but the contract reads no row
semantics. Predicates, ordering, semantic stages, Rows, and Count keep their
existing owners. A consumer may substitute its own member payload type without
changing the effect protocol; a different terminal aggregate requires its own
result branch and equivalence gate. The L1/L2 positioning in this document is
dotnet-inspect composition policy recorded for this repository's adoption; the
contract surface itself carries no layer names.

### Alignment with repository principles

The design applies the repository's
[development practices](../development-practices.md) to shared source execution:

| Principle | How this design applies it |
| --- | --- |
| Build useful shared capabilities | One protocol lets source optimizations serve multiple consumers while preserving the caller's reference result. Gallery discovery's CLI and browser adoption path is tracked in [#5919](https://github.com/richlander/dotnet-inspect/issues/5919). |
| Prefer the simplest sufficient design | Four effect rules and a closed result algebra express the commitment and completion decisions. Structural candidate/result binding supplies the association; the protocol remains linear. |
| Keep hosts thin and preserve structured information | Candidates, member outcomes, rows, counts, dispositions, and evidence retain their typed meaning through shared execution. Hosts consume those outcomes through their existing composition and rendering paths. |
| Preserve owner boundaries | The caller owns plan partitioning and residual execution; operation owners define source-closed behavior; sources own acquisition and proof construction. The protocol composes those responsibilities. |
| Preserve behavior-safe defaults and visible failure | Pure decline leaves the reference strategy available. Accepted execution publishes an explicit outcome, and completion evidence determines whether that outcome can substitute for the reference result. |
| Gate observable behavior | Release cases exercise effect ordering, atomic publication, and evidence acceptance against the reference semantics. The canonical boundary distinguishes a provider cap equal to N from a witness proving N applicable rows. |

Design review evaluates this alignment. The named Release gates below establish
the specific behavioral contracts that make the shared execution useful and
reliable.

### Trust model

Adopting callers and sources are cooperating components. Per repository
policy, this contract does not defend against local or intra-repo actors.
Bindings that cooperating code could violate only by writing code to violate
them are enforced **by construction** — the types make the invalid state
unrepresentable — not by identity tokens, runtime validation, or rejection
rules. [By construction, not by gate](#by-construction-not-by-gate) records
where each such guarantee lives.

External provider behavior is the untrusted input. What a provider's caps,
pages, and terminal tokens do and do not prove is exactly what
[completion evidence](#completion-evidence) disciplines; that discipline is
contract, gated, and applies to every adoption.

Reverse delegation does expand the trusted surface. The natural path trusts
a provider only for enumeration — the raw values it returns — while a
delegated prefix additionally trusts provider-computed predicate, order,
clamp, or count work the natural path would perform locally. That expansion
is deliberate and is never justified by response content: the source owner's
adopted provider contract, capability declaration, and equivalence gate justify
it. Those gates prove the adapter against the reference oracle; they do not
prove future provider honesty. Before acceptance, declining reverse delegation
retains the smaller natural-path trust surface. After acceptance, this pattern
does not provide Byzantine verification or a fallback from a provider that
violates its adopted contract. Completion evidence instead stops an adopted
provider's honest operational signals — protocol-optional features, caps, and
terminal tokens — from being misread as semantic proof. Authorization and
containment of its untrusted content stay owned by the source owner and threat
model.

## Authority and scope

The L1 source delegation pattern is the authority for one source-owned
execution of one caller-formed delegation and for acceptance of its result.

This design owns:

- the effect protocol: pure planning, single acceptance, no fallback after
  acceptance, atomic publication;
- the closed result algebra and each branch's validity rules;
- completion-evidence bases and their acceptance rules;
- the safe-prefix partition precondition for delegated work; and
- the equivalence gates required before an optimized row handoff or Count
  result may substitute for the reference result.

This design does not own:

- construction, validation, or ordering of the L2 logical plan;
- the meaning of projection, predicates, baseline order, semantic stages,
  Rows, or Count;
- how a caller groups declared row sets or derives its prefix and residual;
- which operations are source-closed — each operation owner declares that
  for its own operations;
- source-specific acquisition, pagination, retries, caching, merge,
  deduplication, authoritative count APIs, or proof construction;
- CLI syntax, diagnostics, presentation, or disclosure wording;
- source failure taxonomies or provenance lifetimes; or
- concrete implementation APIs.

The caller supplies already-resolved typed delegation candidates. This
pattern transports the delegated operations without interpreting them and
never invents an alternative partition.

## Vocabulary

Two identities are owner-issued tokens, because they must be matched across
the delegation boundary after the fact:

- **member identity** — one ordered member binding within the caller's
  execution group. An implementation uses the caller's existing
  declared-row-set and shaping identities when their owner adopts this
  pattern; this document does not mint a parallel identity system.
- **completion-requirement identity** — the identity of one caller-owned typed
  requirement that states, for the candidate's result shape, which evidence
  establishes Rows usability or exact Count sufficiency. The source returns
  evidence for the caller's requirement, never one it selected itself.

Equality for both is owner-issued token equality only; display names, option
spellings, provider names, and sequence position do not participate.

Everything else the prior draft carried as an identity token — request,
offer, input, group, capability, permit, residual, and receipt — is a
structural component of the candidate or result object and needs no token
discipline: a result refers to the accepted candidate it answers, evidence is
a field of the member entry it proves, and the residual is a continuation the
caller retains and the source never receives.

### Source-closed operations

Each operation owner declares, in its own contract, whether an operation is
**source-closed**: it can produce no owner-domain row-query or semantic
failure and requires no owner-observable callback, comparer, or resolver
invocation. Strict `Window` and the current row-query and semantic callback,
comparer, and resolver contracts are not source-closed.

Source-closed constrains the owner-observation surface during delegated
execution, not materialization or physical strategy: a source may stream or
buffer a source-closed operation internally, publication stays governed by
the [effect protocol](#effect-protocol)'s atomicity rule, and owner-side
coordination after the result — caller validation, evidence acceptance, and
a row-handoff residual executing under its owners' contracts — is normal.
Strict `Window` is materializable by a source yet still not source-closed,
because failing the window is an owner-domain failure.

Only source-closed operations may enter delegated work. A source capability
never waives an owner requirement: a completion witness cannot replace an
earlier strict-stage requirement, and an exact value cannot compensate for
different failure, callback, or ordering observations. This version carries
no channel through which a source could discharge an owner-observable
invocation or transport an owner-domain failure; adding one is a separate
focused extension to this contract, not an implementer choice.

## Delegation candidates

The caller forms an ordered, immutable list of delegation candidates in
preference order. Each candidate contains:

1. one immutable, already-authorized source input;
2. one caller-defined execution group whose type carries its complete ordered
   member list, with each owner-issued member identity appearing exactly once;
3. one delegated operation-prefix entry derived for every execution-group
   member in that same order — the caller's proven safe prefix as typed,
   owner-issued operation content, which the pattern transports without
   interpretation and the source must execute exactly; empty for an
   acquisition-only row handoff;
4. the required source capability;
5. one typed completion requirement with its owner-issued identity; and
6. exactly one result shape:
   - **row handoff** — the caller retains a residual continuation holding
     every non-delegated operation; or
   - **exact Count** — the candidate covers the complete resolved plan and
     has no residual.

An empty candidate list means no source delegation is available. A capability
or delegated prefix the source does not publish or cannot honor makes the
candidate unsupported, not invalid; planning moves to the next candidate. A
capability is a selection key only: the delegated work is defined by the
candidate's operation prefix, never inferred from the capability, it is
never permission to reinterpret another owner's operation, and a source
claim of similar work never adds an operation the caller did not delegate.

### Safe prefix and residual

For a row-handoff candidate, the delegated operations form one contiguous
reference-order prefix — possibly empty — of every member's resolved plan.
The residual contains every operation at and after the first omitted
operation, in the same reference order. Prefix and residual are disjoint,
together cover the complete plan, and apply no operation twice. An
acquisition-only row handoff delegates the empty prefix and retains the
complete owner-operation plan in its residual.

For an exact-Count candidate, every operation in every member's resolved plan
must be source-closed, because there is no residual in which to retain an
owner-observed operation. When the plan contains such an operation, the
caller uses a row handoff or the reference path instead, so the operation and
Count execute under their owner-defined observation and failure contract.

The caller owns constructing these partitions and proving them against its
reference plan. Candidate construction derives prefix entries from the
execution group's unique ordered member collection rather than accepting a
parallel identity list. The source receives the candidate — including the
delegated prefix it must execute exactly — and never the residual. The proof is
the caller's adoption gate
([`SourceDelegationPartitionMatchesReference`](#required-gates)), applied
before a candidate is ever presented to planning; the prefix transported in
the candidate is exactly the proven prefix.

## Effect protocol

1. **Planning is pure.** Planning inspects immutable candidate structure and
   immutable capability declarations, and performs no network, filesystem,
   source-result cache, provider, pagination, row-callback, comparer, or
   source-content work. It visits candidates in declaration order and either
   selects the first candidate whose complete capability, delegated operation
   prefix, member shape, completion requirement, input, and result shape the
   source supports, or declines all candidates. Planning does not publish an
   accepted-plan handle. A decline is a capability result, not a source
   failure, and the caller may then use any later strategy, including its
   complete reference path.
2. **Acceptance is the commitment point.** Acceptance binds the exact
   candidate, and the contract exposes no free-standing accepted-plan value:
   acceptance and execution form one public operation, the accepted binding
   escapes only inside the published result, and a second execution
   therefore has no handle to replay. An implementation that separates the
   two internally keeps the accepted plan private and publishes at most one
   outcome.
3. **No fallback after acceptance.** After acceptance, the source returns one
   result or propagates an unexpected implementation exception. A runtime
   capability miss, provider failure, cancellation, or insufficient evidence
   is never converted into a decline, and the caller never silently retries
   another candidate or the reference path, because source effects may
   already have occurred. The row-handoff residual is not fallback: it is the
   continuation the caller retained before execution began, and only a
   validated `RowHandoff` enters it.
4. **Publication is atomic.** Physical execution may stream or buffer
   internally, but no logical success or partial result is published before
   one complete outcome exists, and published `RowValues` are fully
   acquired: no deferred source enumeration, acquisition, or source failure
   remains to occur after publication, including inside residual processing.
   Result member maps and each `RowValues` sequence are immutable snapshots of
   membership and order; source-side collection mutation after construction
   cannot change them. Opaque caller-owned row objects are not cloned.
   `NotSatisfied`, an exception, or a defective result is terminal for the
   accepted plan and never enters the residual.

## The result algebra

A source result answers exactly the accepted plan it was constructed from:

```text
SourceDelegationResult =
    RowHandoff(ordered member outcomes)
  | ExactCount(ordered member counts)
  | NotSatisfied(ordered member dispositions)

member outcome    = RowValues(member, caller-owned values,
                              disposition, completion evidence)
                  | Unavailable(member, disposition, completion evidence)
member count      = ExactCountValue(member, non-negative count,
                                    completion evidence)
member disposition = NotSatisfiedMember(member, disposition,
                                        completion evidence)
```

Results are constructed from the accepted plan's member list, so every member
appears exactly once, in execution-group order, with its identity carried
explicitly — by construction, not by validation.
The member map and every `RowValues` sequence snapshot membership and order
before publication; row values themselves remain opaque caller-owned objects.

- **`RowHandoff`** is valid only for a row-handoff candidate. A `RowValues`
  entry is constructed only when the caller-owned completion requirement
  accepts its disposition-and-evidence pair as Rows-usable for the complete
  accepted candidate and exact retained residual; it is then eligible for the
  caller's residual admission. The owning group and terminal composition may
  suppress every residual invocation without changing the handoff result. An
  `Unavailable` entry carries no rows. A row handoff preserves usable rows
  beside unavailable members because the L2 consumer owns that composition:
  an expected acquisition, absence, cancellation, or completion disposition
  that leaves one member's values unusable uses that member's `Unavailable`
  outcome rather than failing the whole candidate. The same cause may
  accompany `RowValues` when the completion requirement accepts the acquired
  rows under the acquisition-only incomplete-stop case defined below.
- **`ExactCount`** is valid only for an exact-Count candidate. It contains
  one non-negative exact count with accepted completion evidence for every
  member. It cannot carry rows, publish a partial count map, or invent a
  total across members.
- **`NotSatisfied`** is valid for an exact-Count candidate when any member is
  not exact, and for a row-handoff candidate only for a candidate-scoped
  expected failure that prevents a complete member map. It carries one
  disposition-and-evidence entry per member and no row or Count payload. It
  does not replace `Unavailable` when the source can still determine every
  member outcome. A candidate-scoped failure keeps that scope: affected
  members reference the same immutable disposition-and-evidence value rather
  than relabeling the cause as member-scoped.

Unexpected programming failures propagate under their owning execution
contract; they are never converted into an empty or success-shaped result.

## Completion evidence

Completion evidence is an immutable typed value carried by the member entry
it proves. It names its evidence scope — this member, or the complete
candidate — and one source-owned evidence basis:

- **logical exhaustion** — the adopted source contract proved that no further
  value exists in the source domain named by the candidate;
- **requirement witness** — the source produced the typed witness required by
  the caller's completion requirement; or
- **incomplete stop** — a provider, page, work, time, memory, or acquisition
  bound, or cancellation, stopped execution without proving exact completion;
  only an acquisition-only handoff may remain Rows-usable, under the caller
  owner's existing typed source-result contract; or
- **unavailable source outcome** — an expected source failure or absent member
  or candidate domain prevented a usable or exact result. The accompanying
  owner-issued disposition carries the precise cause.

Logical exhaustion and requirement-witness evidence establish Rows usability
or exact Count only when the typed completion requirement accepts the exact
basis. An incomplete stop never establishes exact Count. It establishes Rows
usability only when the delegated operation prefix is empty: no reverse
semantic work occurred, so the caller owner's existing typed source-result
contract decides whether the incomplete rows are usable for its exact residual.
After any non-empty delegated prefix, an incomplete stop leaves the member
`Unavailable`; delegated Rows must instead carry logical-exhaustion or
requirement-witness evidence accepted as exact. An unavailable source outcome
establishes neither. Logical exhaustion means that the candidate's named
domain exists and was proven exhausted; absence is not exhaustion. Evidence
referenced by a member entry is member-scoped by default; a member may
reference one candidate-scoped value only when that basis establishes the
member's own disposition, usability, or exactness claim. One candidate-scoped
value may prove the same candidate-wide failure for every member, but
exhaustion of one member proves nothing about another, and a group aggregate
that cannot establish individual member values is insufficient for exact
Count.

Evidence is never inferred from row or Count values. Returning exactly the
requested number, returning fewer rows than a page size, receiving an empty
page, or observing a provider-specific terminal token proves nothing unless
the adopted source contract constructs the matching evidence. The adopting
source owner defines how its evidence is constructed and proves the claim
with its own non-vacuous gate.

## Rows usability and Count sufficiency

Rows usability and Count sufficiency are different conclusions:

- `RowValues` means the caller-owned completion requirement accepted the
  values and evidence as usable for the complete row-handoff candidate and its
  exact retained residual.
- `ExactCount` means every count is sufficient for the complete accepted
  Count candidate.
- `Unavailable` and `NotSatisfied` are neither.

A row handoff may carry incomplete-stop evidence when the caller-formed Rows
contract accepts the acquisition-only case above. That handoff preserves the
source result the caller owner would have consumed on its reference path,
including that owner's residual usability decision. After a non-empty
delegated prefix, incomplete Rows are not accepted; supporting them is a
separate focused extension that would require owner-side equivalence changes.
The same evidence is not thereby sufficient for Count: one insufficient,
failed, or absent member forces `NotSatisfied`, and successful-looking counts
for the other members do not escape.

## `Head(N) -> Count` as the canonical witness

The `RowSelection` owner defines `Head(N)` as a lenient clamp. The L2 owner
defines Count as the exact cardinality after that clamp. After the
RowSelection owner separately declares `Head(N)` source-closed and proves that
declaration with `SourceClosedDeclarationsMatchOwnerContracts`, an adopting
caller may form an exact-Count candidate whose completion requirement accepts
either:

- a requirement witness proving that N applicable ordered rows reached the
  clamp; or
- logical exhaustion proving that fewer than N applicable rows exist.

The source may return N immediately after the first proof. It may return
`k < N` only after the second. A provider or work cap equal to N is
incomplete-stop evidence, not the required witness.

This example applies the adjacent owners' locked semantics; it does not move
`Head` or Count meaning into this pattern.

## Example: an OData-backed source

This section is illustrative, not normative. An OData-backed source shows
both the opportunity and the discipline, because the provider protocol
itself accepts delegated row and limit work:

- `$top` can represent a `Head` clamp only after the RowSelection owner
  separately declares that operation source-closed and its gate passes.
  `$filter` and `$orderby` can enter the prefix only when their operation
  owners separately define typed non-callback operations and declare them
  source-closed; the current row-query callback and comparer operations remain
  outside the delegated prefix. The adoption must also prove that the service
  honors each mapped option, rather than trusting capability metadata alone.
- `$count=true` returns a server-computed `@odata.count`; the adopted source
  contract may construct a requirement witness or exhaustion evidence from
  it under that service's documented semantics.
- A response carrying `@odata.nextLink` is server-driven paging: stopping
  there is an incomplete stop, whatever the page happens to contain. The
  source may follow the links to completion. It may hand off the acquired rows
  only under the caller owner's acquisition-only usability contract; after
  executing a non-empty delegated prefix, it must complete the required result
  or report the member unavailable.
- A service that silently ignores an unsupported `$orderby` returns
  plausible values in the wrong order — exactly the behavior drift the
  adoption's equivalence gate exists to catch before it ships.

The delegation also chains: the caller delegates to the source, and the
source delegates onward to the OData service. This contract governs only the
caller/source boundary. How the source satisfies its accepted plan —
following next links, trusting `@odata.count`, checking capability
annotations — is adoption-owned acquisition and proof construction, and the
same rule holds at every depth: an operational bound anywhere in the chain
never surfaces as semantic proof. Per the [trust model](#trust-model),
delegating `$orderby` or `$count` trusts the service for computation the
natural path performs locally — the adoption's capability proof and
equivalence gate justify that expansion. A provider that later violates its
adopted contract can still produce an incorrect delegated result; detecting
Byzantine provider behavior is not a claim of this pattern.

## By construction, not by gate

The prior draft policed the following at runtime with identity tokens,
validation rules, and rejection paths. Under this contract each is structural
in the product runtime. Safety-relevant claims about that shape are proven by
the named Release gates below rather than by runtime identity policing.

| Guarantee | Structural encoding |
| --- | --- |
| A result answers exactly one accepted plan | The result type is constructed from, and refers to, the accepted plan. |
| Execution happens at most once | No accepted-plan value escapes the public surface; acceptance and execution form one operation whose binding appears only inside the result. `SourceDelegationAcceptanceExecutesOnce` proves the implementation invokes and publishes once. |
| Member maps and row sequences are immutable, complete, ordered, and duplicate-free | Candidate prefixes and results are built from the execution group's unique ordered member collection and snapshot collection membership and order; `SourceDelegationPartitionMatchesReference` proves exact candidate binding, and the result-branch and atomic-publication gates prove exact immutable result binding. |
| Evidence reaches the caller only inside a member entry | Evidence is carried by the entries it accompanies rather than as free-standing values; whether a referenced basis and scope establish each entry's claim remains the semantic check owned by `SourceDelegationCompletionEvidenceBasisIsAccepted`. |
| The residual is caller-owned | The residual is a caller-held continuation; the candidate the source sees does not contain it. |
| The source cannot rewrite the plan | No result branch carries operations, cursors, or plan fragments. |
| Permit classifications cannot be downgraded | There are no permits; source-closed is declared by the operation owner in its own contract and proven by that owner's gate. |

Planning visits candidates in declaration order and accepts the first
supported one; this is stated behavior verified by ordinary tests, not a
named gate, because no promised safety property depends on which supported
candidate wins.

## Security and platform boundary

Remote content does not mint member or completion-requirement identities and
does not construct candidates, plans, or evidence. The adopting source interprets
provider observations and carries the resulting owner-issued contained
disposition and evidence types.

The contract authorizes no source, endpoint, credential, cache, or filesystem
path. Host and source owners perform that authorization before execution.

This pattern introduces no platform exception. Its eventual shared component
must preserve the repository's approved dependency direction and platform
constraints; the independently adoptable package and dependency closure are
owned and proven by [#5235](https://github.com/richlander/dotnet-inspect/issues/5235).
Adopters may use platform capabilities in their own owning components only
under those components' existing platform contracts.

## Required gates

The pattern implementation and each optimized adoption must add the
applicable named Release gates. Release tests exercise public construction,
execution outcomes, and reference-equivalence fixtures. The product runtime
carries only the semantic decisions the contract itself defines, such as
evidence-basis acceptance and result-shape validity; the guarantees in
[By construction, not by gate](#by-construction-not-by-gate) ship as type
shape, not checks.

The public harness covers the protocol-owned effect, result-algebra, and
completion-evidence gates below. Its toy partition examples exercise public
candidate construction and reference composition, but do not certify a
production caller's `SourceDelegationPartitionMatchesReference` gate.
`SourceClosedDeclarationsMatchOwnerContracts`,
`OwnerObservationsRemainReferenceBarriers`, and the two optimized section-row
equivalence gates remain unverified until their owning adoptions land.

| Gate | Contract |
| --- | --- |
| `SourceDelegationPlanningIsPure` | Planning inspects only immutable candidate structure and immutable capability declarations, and performs zero source, provider, source-result cache, filesystem, network, row-callback, comparer, or content operations. |
| `SourceDelegationDeclineAllowsReferenceFallback` | A pure all-candidates decline permits the caller's retained reference strategy and is never reported as a source failure. |
| `SourceDelegationAcceptanceExecutesOnce` | One public invocation that accepts a candidate invokes source execution at most once, publishes at most one outcome, and exposes no accepted-plan handle that permits replay. |
| `SourceDelegationAcceptedFailureNeverFallsBack` | After acceptance, no result or failure tries a later candidate or the reference path. Only a validated `RowHandoff` is eligible for its retained residual; the owning group and terminal composition may suppress all residual invocation. Within an admitted handoff, only Rows-usable entries are eligible. An acquisition-only incomplete entry follows the caller owner's existing residual-usability contract; an incomplete entry after a non-empty delegated prefix is `Unavailable`. `NotSatisfied`, exceptions, and defective results enter no residual. |
| `SourceDelegationOutcomePublicationIsAtomic` | Streaming or buffered physical strategies expose no logical success or partial member map before the complete outcome, and published row values retain no deferred source enumeration, acquisition, or source failure. Result member maps and row sequences defensively snapshot membership and order; mutating any source collection after construction cannot change the published result, while individual opaque row objects are not cloned. |
| `SourceDelegationPartitionMatchesReference` | The caller's adoption gate proves that candidate construction binds exactly the execution group's complete ordered member-identity sequence with no missing, extra, or duplicate member, and that every row-handoff member delegates one contiguous reference-order prefix (possibly empty) while retaining the exact disjoint suffix in its residual, with complete coverage and no duplicated operation; it rejects a malformed or non-prefix partition before planning, and the delegated prefix transported in the candidate is exactly the proven prefix. Every exact-Count candidate covers the complete resolved plan. |
| `SourceClosedDeclarationsMatchOwnerContracts` | Each operation owner's gate proves its source-closed declaration against its reference failure and invocation contract; an operation is delegable only under its owner's current declaration. |
| `OwnerObservationsRemainReferenceBarriers` | An operation not declared source-closed never enters delegated work; retained in the residual or reference path, it preserves exact invocation, failure identity, scope, all-or-failure behavior, and precedence. |
| `SourceDelegationRowHandoffIsComplete` | `RowHandoff` occurs only for a row-handoff candidate and contains exactly one immutable outcome for every accepted member in execution-group order, with no missing, extra, duplicate, or reordered member. Every outcome is exactly one `RowValues` or `Unavailable` entry; only `RowValues` carries a fully acquired immutable row-sequence snapshot, and its disposition-and-evidence pair satisfies the typed completion requirement's Rows-usability rule for the exact accepted candidate and residual before residual admission. Caller-owned group or terminal composition may suppress every residual invocation. |
| `SourceDelegationExactCountIsAtomic` | `ExactCount` occurs only for an exact-Count candidate and contains exactly one non-negative exact value with accepted completion evidence for every accepted member in execution-group order, with no missing, extra, duplicate, or reordered member. It carries no rows and publishes no partial map or invented total. |
| `SourceDelegationNotSatisfiedCarriesEvidence` | An inexact accepted Count or a candidate-scoped row-handoff failure returns exactly one disposition-and-evidence entry for every accepted member in execution-group order, with no missing, extra, duplicate, or reordered member and no row or Count payload. The broader failure retains candidate scope through references to one canonical value, and a determinable member-scoped Rows failure remains `Unavailable` inside `RowHandoff`. |
| `SourceDelegationCompletionEvidenceBasisIsAccepted` | Logical exhaustion and requirement-witness evidence establish Rows usability or exact Count only when the typed completion requirement accepts the basis. Incomplete-stop evidence never establishes Count and establishes Rows usability only under the caller owner's existing source-result contract for an acquisition-only handoff; after any non-empty delegated prefix, the member remains `Unavailable`. Unavailable-outcome evidence establishes neither. A member-referenced candidate-scoped value must establish that member's own claim, exhaustion of one member proves nothing about another, absence is not exhaustion, and exact Count requires proof of every member value. |
| `OperationalBoundsNeverProveCompletion` | Provider, page, work, time, memory, acquisition, and cancellation bounds remain incomplete-stop evidence even when their numeric value equals a requested semantic bound or returned row count. |
| `RowsUsabilityAndCountSufficiencyStayDistinct` | A capped acquisition-only handoff preserves the caller owner's typed Rows-usability decision and incompleteness evidence through its residual, while the same evidence remains Count-insufficient. After any non-empty delegated prefix, incomplete-stop evidence keeps the member `Unavailable`, and the corresponding exact-Count candidate returns `NotSatisfied` and no cardinality. |
| `OptimizedRowHandoffMatchesSectionRowReference` | The optimized row-handoff path is proven to execute and, after any residuals admitted by the owning composition, matches the complete section-row reference result exactly for values, order, member identity, unavailable-member composition, source evidence, every owner-observable invocation, and terminal failure identity, scope, and precedence. Fixtures exercise an acquisition-only incomplete handoff through a non-empty residual under the caller owner's existing usability contract; incomplete handoffs after non-empty delegated prefixes that remain unavailable; a multi-member Count handoff whose unavailable companion suppresses all residual execution; immutable result snapshots under mutation of every source collection; an exact sentinel callback/comparer/resolver exception; and a case where both the semantic and callback failures are reachable with reference precedence preserved. Query, ordering, and semantic-operation cases are required only when the adoption delegates matching source-closed operations. |
| `OptimizedCountMatchesSectionRowReference` | The optimized Count path is proven to execute and matches the complete section-row reference result for empty, below-bound exhausted, bound-satisfied, oversized, multi-member, and sentinel-failure cases; insufficient evidence rejects rather than succeeding. |

## Non-claims

This design does not assert that any current source can accept a delegation,
define an L2 candidate-construction policy, choose a source-specific proof,
or change current product behavior. Every adoption remains a separate focused
effort by the source or caller owner.
