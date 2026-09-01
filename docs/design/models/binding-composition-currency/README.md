# Binding composition-currency model

This TLA+ model is the executable interaction companion to
[Complete identity-eligible binding composition](../../type-forwarding-resolution.md#complete-identity-eligible-binding-composition).
It checks one owner-issued candidate domain, one adjacent arbitration decision,
the delegated snapshot's pre-consumption validity, and the Metadata boundary.

The model answers these focused questions:

- Does the handoff contain every and only identity-eligible candidate?
- Can one acquisition registration appear more than once in the handoff?
- Is its evidence order independent of incidental enumeration order?
- Can a consumer inject a candidate or silently drop one from the final result?
- Can finalization substitute another in-domain candidate for the proposed
  contender?
- Does finalization preserve a complete active/inactive partition?
- Does an empty or foreign decision become a visible rejection?
- Can a terminal selection or ambiguity be reopened to promote an inactive
  shadow?
- Can a terminal selection or ambiguity be preserved when its active
  candidates are not first in canonical order?
- Can an unconsumed handoff reach Metadata as a frozen result?
- Can a snapshot from outside the captured policy version be interpreted?
- Does every issued handoff reach a terminal decision or supersession?

## Relationship to the product

Each initial state chooses three acquisition registrations, every enumeration
order, one source result, an identity-eligible set, and a proposed contender
set. `CompositionRequired` sources issue a canonical sequence containing the
complete eligible set. Existing selected, ambiguous, unavailable, rejected,
and missing-disposition results issue no domain. `NoNameOwner` is preserved
unchanged here; its separately modeled policy-tier advancement is outside this
model.

Selected and ambiguous source results range over every legal active subset of
their complete active/inactive evidence. This includes singleton winners and
multi-candidate ties that do not begin with the first canonical candidate.
Preservation checks compare the result with that independently chosen incoming
partition rather than reconstructing a decision from evidence order.

The adjacent policy proposes only the highest-precedence contender set. A
one-member set becomes `Selected`; a larger set becomes `Ambiguous`; and the
handoff derives every remaining domain member as inactive. An empty or foreign
contender set becomes `Rejected`. Both active and inactive projections preserve
the issued order. The model intentionally abstracts the precedence relation: it
checks the handoff boundary for every possible proposed set rather than
selecting designated or platform candidates itself.

Each initial state pairs the generation's captured delegate version with either
a matching or foreign returned snapshot. A mismatch produces the abstract
`Superseded` control result before the payload or domain is interpreted. A
matching snapshot may be finalized provisionally even if the live delegate
changes later; the distinct outer composite token, later version replacement,
and commit check remain owned and checked by the
[composite binding-version model](../binding-selection-version/README.md#transforming-composite-policy).
When no arbitration consumer is present, the Metadata boundary rejects the
unfinalized handoff instead of freezing it.

## Worked trace

For canonical domain `<<candidateOne, candidateTwo, candidateThree>>`, a
consumer may propose `{candidateTwo}`. Finalization returns `Selected` with
active projection `<<candidateTwo>>` and inactive projection
`<<candidateOne, candidateThree>>`; no candidate is lost and the domain order
is preserved in each projection. If that terminal result passes through
another composite, the independently modeled incoming partition keeps
`candidateTwo` active. The terminal-canonicalization mutation instead
reconstructs `candidateOne` as the winner from evidence order and violates
`NonDomainResultsArePreserved`.

For a separate singleton domain `<<candidateOne>>`, `candidateTwo` is foreign
because it is outside that issued domain. The handoff returns
`Rejected(InvalidCompositionResult)` when the consumer proposes it. The
broken-injection configuration deliberately accepts that contender and TLC
produces a three-state counterexample to `FinalCandidatesComeFromDomain`.

## Assumptions and non-claims

The three-candidate bound covers empty, singleton, selected, ambiguous, proper
subset, full-set, and foreign-candidate decisions. It also covers every legal
selected or ambiguous terminal partition, including noncanonical active
candidates. Candidate symbols represent distinct
`AssemblyAcquisitionRegistration` identities whose exact descriptors are
preserved abstractly. The canonical model order represents the issuing owner's
stable order for an equal request and version; it does not prescribe a product
sort key. Candidate decisions are sets in the model, so duplicate array entries
and descriptor substitution under one registration cannot be expressed; those
target-contract checks remain unverified pending product gates. Empty and
foreign decisions are model-checked. The issued domain remains an ordered
sequence, so a separate mutation checks duplicate registration issuance.

Identity matching and nonempty domain construction are model inputs. The
product factory is the proposed nonempty-domain gate; the model cannot reach an
empty `CompositionRequired` source. Candidate acquisition and readability,
name ownership, policy-tier routing, designated/platform role assignment and
precedence, workspace construction, cache implementation, live-version
replacement after a matching snapshot, and retry timing are outside the model.
TLC results establish properties of this state machine, not of the shipped
implementation. Formal model-to-product correspondence is unverified.

## Checked configurations

| Configuration | Purpose |
| --- | --- |
| `BindingCompositionCurrencySafety.cfg` | Explores every source kind, eligible set, enumeration, contender set, consumer presence, and matching/foreign snapshot pair. Checks complete and exact issuance, canonical order, non-domain-result preservation, exact ordered final partitions, empty/foreign-decision rejection, unfinalized-handoff rejection, foreign-snapshot exclusion, and type safety. |
| `BindingCompositionCurrencyLiveness.cfg` | Checks that every non-domain result or matching handoff eventually completes through preservation, finalization, boundary rejection, or snapshot supersession under weak fairness. |
| `BindingCompositionCurrencyBrokenOmission.cfg` | Omits one identity-eligible candidate. It must violate `DomainIsComplete`. |
| `BindingCompositionCurrencyBrokenAddition.cfg` | Adds one identity-ineligible candidate. It must violate `DomainContainsOnlyEligible`. |
| `BindingCompositionCurrencyBrokenDuplicateRegistration.cfg` | Repeats one eligible registration in the issued sequence. It must violate `DomainOrderMatchesMembers`. |
| `BindingCompositionCurrencyBrokenOrder.cfg` | Uses incidental enumeration order for the issued sequence. It must violate `DomainOrderIsCanonical`. |
| `BindingCompositionCurrencyBrokenInjection.cfg` | Accepts a contender outside the issued domain. It must violate `FinalCandidatesComeFromDomain`. |
| `BindingCompositionCurrencyBrokenDecisionSubstitution.cfg` | Substitutes another in-domain contender for a valid proposed winner. It must violate `ValidDecisionIsHonored`. |
| `BindingCompositionCurrencyBrokenDrop.cfg` | Drops one non-contending domain member instead of retaining it as inactive. It must violate `FinalPartitionPreservesDomain`. |
| `BindingCompositionCurrencyBrokenSelectedShadowPromotion.cfg` | Reopens a terminal selected result and selects one of its inactive shadows. It must violate `InactiveEvidenceNeverPromoted`. |
| `BindingCompositionCurrencyBrokenShadowPromotion.cfg` | Reopens a terminal ambiguity and selects one of its inactive shadows. It must violate `InactiveEvidenceNeverPromoted`. |
| `BindingCompositionCurrencyBrokenProjectionOrder.cfg` | Reverses the final active projection relative to the issued order. It must violate `FinalProjectionOrderIsPreserved`. |
| `BindingCompositionCurrencyBrokenTerminalCanonicalization.cfg` | Reconstructs a terminal winner from canonical evidence order instead of preserving the incoming partition. It must violate `NonDomainResultsArePreserved`. |
| `BindingCompositionCurrencyBrokenUnfinalized.cfg` | Lets an unconsumed handoff reach Metadata as `CompositionRequired`. It must violate `UnfinalizedHandoffIsRejected`. |
| `BindingCompositionCurrencyBrokenStaleVersion.cfg` | Interprets a snapshot whose returned token differs from the captured delegate token. It must violate `ForeignSnapshotIsNotInterpreted`. |

`TypeOK`, `NonDomainResultsNeverIssueDomain`,
`NonDomainResultsArePreserved`, and `SupersededPublishesNoDecision` are
whole-state structural checks. The thirteen
broken configurations are independent negative controls for the interaction
claims most likely to regress.

All configurations disable TLC's deadlock check because `Completed` is an
intentional terminal phase. The temporal specification permits stuttering in
that state.

## Running TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md) for the pinned
toolchain. Run configurations sequentially because concurrent TLC processes
using `-cleanup` can remove one another's metadata.

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/binding-composition-currency

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config BindingCompositionCurrencySafety.cfg \
  BindingCompositionCurrency.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config BindingCompositionCurrencyLiveness.cfg \
  BindingCompositionCurrency.tla
```

The mutation configurations are expected to exit unsuccessfully:

```bash
for config in \
  BindingCompositionCurrencyBrokenOmission \
  BindingCompositionCurrencyBrokenAddition \
  BindingCompositionCurrencyBrokenDuplicateRegistration \
  BindingCompositionCurrencyBrokenOrder \
  BindingCompositionCurrencyBrokenInjection \
  BindingCompositionCurrencyBrokenDecisionSubstitution \
  BindingCompositionCurrencyBrokenDrop \
  BindingCompositionCurrencyBrokenSelectedShadowPromotion \
  BindingCompositionCurrencyBrokenShadowPromotion \
  BindingCompositionCurrencyBrokenProjectionOrder \
  BindingCompositionCurrencyBrokenTerminalCanonicalization \
  BindingCompositionCurrencyBrokenUnfinalized \
  BindingCompositionCurrencyBrokenStaleVersion
do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
    -workers 1 -cleanup -noGenerateSpecTE \
    -config "$config.cfg" BindingCompositionCurrency.tla
done
```

## Recorded result

The positive configurations completed with no errors:

| Configuration | Generated states | Distinct states | Maximum depth | Result |
| --- | ---: | ---: | ---: | --- |
| Safety | 12,576 | 12,576 | 3 | All 16 invariants passed. |
| Liveness | 12,576 | 12,576 | 3 | `CompositionConverges` passed. |

The safety graph starts 5,952 initial states and covers both consumer-presence
states and both returned snapshot versions. It executed 1,344
`IssueComposition`, 4,608 `IssueNonDomain`, 336 `Finalize`, and 336
`RejectUnfinalized` transitions. Foreign snapshots complete through their issue
action without exposing a domain.

Each mutation exited with TLC status 12 on its intended invariant:

| Configuration | Generated / distinct | Maximum depth | Counterexample |
| --- | ---: | ---: | --- |
| Broken omission | 769 / 769 | 2 | An eligible two-member set issued only `candidateOne`, violating `DomainIsComplete`. |
| Broken addition | 1,153 / 1,153 | 2 | A singleton eligible set issued an added ineligible candidate, violating `DomainContainsOnlyEligible`. |
| Broken duplicate registration | 1,345 / 1,345 | 2 | A repeated eligible registration violated `DomainOrderMatchesMembers` before finalization. |
| Broken order | 1,729 / 1,729 | 2 | Incidental enumeration reversed a two-member issued sequence, violating `DomainOrderIsCanonical`. |
| Broken injection | 721 / 721 | 3 | A foreign contender became selected while the real domain member became inactive, violating `FinalCandidatesComeFromDomain`. |
| Broken decision substitution | 217 / 217 | 3 | Another in-domain candidate replaced the proposed contender, violating `ValidDecisionIsHonored`. |
| Broken drop | 289 / 289 | 3 | Finalization selected one of two domain members and discarded the other instead of retaining it as inactive, violating `FinalPartitionPreservesDomain`. |
| Broken selected-shadow promotion | 289 / 289 | 3 | A terminal selected result was reopened and its inactive candidate became selected, violating `InactiveEvidenceNeverPromoted`. |
| Broken ambiguous-shadow promotion | 73 / 73 | 3 | A terminal two-way tie with one inactive candidate was reopened and that inactive candidate became selected, violating `InactiveEvidenceNeverPromoted`. |
| Broken projection order | 169 / 169 | 3 | A two-member active projection reversed the issued order, violating `FinalProjectionOrderIsPreserved`. |
| Broken terminal canonicalization | 1,537 / 1,537 | 2 | A noncanonical terminal winner was replaced with the first canonical candidate, violating `NonDomainResultsArePreserved`. |
| Broken unfinalized handoff | 1,345 / 1,345 | 3 | An absent arbitration consumer let `CompositionRequired` reach Metadata unchanged, violating `UnfinalizedHandoffIsRejected`. |
| Broken foreign snapshot | 673 / 673 | 3 | A `versionTwo` snapshot was interpreted against captured `versionOne` and became a rejected decision, violating `ForeignSnapshotIsNotInterpreted`. |

The runs used the repository-pinned TLA+ v1.8.0 tools, TLC build
`2026.08.21.155922` revision `9787e65`. The checked
`tla2tools.jar` SHA-256 was
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.
The available runtime was OpenJDK `21.0.12`; the runbook's preferred Java 25
runtime was not installed on this shared host. Java 21 satisfies the tool's
Java 11-or-later requirement, so the machine configuration was left unchanged
and the runtime deviation is recorded here.
