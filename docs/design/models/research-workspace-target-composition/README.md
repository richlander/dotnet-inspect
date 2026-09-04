# Workspace Research target composition model

This TLA+ model is the executable companion to
[Workspace Research target composition](../../research-workspace-target-composition.md).
It instantiates the Metadata-owned
[`TypeForwardingResolution`](../type-forwarding-resolution/TypeForwardingResolution.tla)
module and the owner-issued
[`AssemblyBindingPolicyVersionLifecycle`](../binding-selection-version/AssemblyBindingPolicyVersionLifecycle.tla)
module. It adds the Queries-owned association from one workspace side through
sealed Queries and Research input identities to one pre-existing effective
target attempt.

## Product relationship

The imported modules supply direct, forwarded, and typed terminal resolution
paths over three assemblies and a two-hop bound, plus one fresh
binding-policy-version replacement. The composition adds:

- two comparison sides with distinct group and receipt identities;
- independent admitted and sealed-terminal facts for the selected and opposite
  sides;
- selected-side duplicate-occurrence and foreign-input facts that distinguish
  an exact population from repeated or broader sealed populations;
- independent active receipt and complete Research domains, plus active attempt
  and census domains, that distinguish exact current evidence from stale or
  broader owner results and bounded total value slots;
- acquisition-registration-shaped participant identity;
- opaque Queries, Research, attempt, domain, and census ids joined by immutable
  owner-issued maps;
- two opaque selection-scope identities that expose cross-scope substitution;
- one immutable admitted terminal candidate chosen before Metadata resolution;
- pre-existing root and terminal attempt kinds plus exact domain-side census
  records;
- query-level participant image availability before Metadata forwarding; and
- composed, unavailable, rejected, contract-fault, and completed terminal
  states.

The normal policy selects only the exact terminal assembly on the requested
side when that assembly is the pre-existing admitted candidate represented in
the owner maps. A different resolved terminal cannot be retrofitted into those
maps and is rejected. Successful composition preserves the complete forwarding
path and the side's binding-policy version, then completes only with a resolved
endpoint.

## Join currency

The model represents the product join as:

```text
side
  + side-specific group
  + owner-issued binding version
  + terminal acquisition registration
  + query operation and question
  + exact Research selection scope
  + query input
  + projected Research input
  + Research domain and pre-existing attempt
```

The concrete product also retains catalog-generation identity, domain-side
census payload, and MVID-scoped address evidence. The model represents exact
selection-scope identity, carried versus exact-address request kind,
terminal-domain health, and attempt identity, while abstracting those detailed
payloads.

The receipt, attempt, and census maps are installed in `Init` and never change.
Each separates an active owner-published domain from total bounded value slots
used by mutations. Population validity independently requires the active
receipt domain to equal the sealed Queries population and the complete
Research input domain to equal that receipt before either imported owner
advances. Their opaque ids model already completed owner work; no composition
transition constructs a Research identity, attempt, domain, scope, or census.
The two scope ids are model-only tagged tuple witnesses whose components are
never inspected. TLA+ record values encode owner-issued associations, not
permission for product composition to derive those identities from semantic
fields.

## Imported contract

`Forwarding` is a named `INSTANCE` of `TypeForwardingResolution` with explicit
constant and variable substitutions. Both safety configurations recheck the
imported path, phase, scope, cycle, hop-budget, terminal ownership, declaration,
and candidate-validation invariants. The successful-population CI safety
configuration also checks `ForwardingBehaviorRefinesOwner`, which requires the
composed behavior to satisfy the imported temporal specification. The
exhaustive configuration includes invalid populations that reject before the
owner is invoked, so it does not claim temporal refinement for those inputs.

`BindingLifecycle` is a named `INSTANCE` of
`AssemblyBindingPolicyVersionLifecycle`. The safety configuration rechecks
fresh replacement and `BindingBehaviorRefinesOwner`.

For a valid population, carried request, and successfully retained participant
set, the composition calls `Forwarding!Advance` and
`BindingLifecycle!Advance`; it does not copy either owner's transitions. An
invalid population or unsupported exact-address request rejects before those
actions. A query-level participant image-open rejection becomes unavailable
without invoking either imported owner. Once forwarding reaches `Terminal`,
Queries either selects the pre-existing attempt, reports typed non-success, or
detects binding-version drift as a contract fault.

## Checked properties

The safety configuration checks that:

- a missing, duplicated, foreign, or broader Research population rejects
  before forwarding resolution;
- an unsupported exact-address request rejects before forwarding or binding
  advances;
- a query-level image-open failure becomes unavailable before forwarding or
  binding advances;
- a selected endpoint belongs to the requested side and its admitted group;
- a selected endpoint is the exact terminal assembly from Metadata;
- the exact Research attempt comes from the Queries-to-Research projection;
- the selected Research input retains the selected Queries input's exact
  terminal acquisition registration;
- the selected census is the exact domain-side census containing that attempt;
- the selected attempt, census, and retained root attempt belong to the exact
  requested selection scope;
- the selected attempt is resolved;
- the selected terminal domain is healthy;
- the pre-existing root attempt is preserved;
- forwarding hops and the captured binding-policy version are preserved;
- direct resolution retains the facade root;
- a forwarded root remains input-locally unavailable;
- only carried requests select an endpoint;
- an unusable attempt or census becomes unavailable before later root-shape
  rejection;
- a root-shape contradiction is rejected only after the effective attempt is
  otherwise usable;
- typed non-success publishes no partial endpoint; and
- completion cannot occur without a selected endpoint.

The liveness configuration checks that every behavior reaches composed
completion, typed unavailability, rejection, or contract fault under weak
fairness.

## Exact scenario configurations

The ordinary exact-outcome gates force the terminal classification rather than
accepting any converged phase:

| Configuration | Required outcome | Generated / distinct | Maximum depth |
| --- | --- | ---: | ---: |
| `DirectCompletion.cfg` | A valid direct definition completes with the facade root. | 147,478 / 8 | 4 |
| `ForwardedCompletion.cfg` | A valid one-hop route with a blocked facade census completes from the healthy terminal census. | 147,544 / 20 | 7 |
| `BlockedTerminalCensusUnavailable.cfg` | An owner-valid failed terminal attempt and blocked census remain unavailable. | 147,541 / 17 | 6 |
| `ImageOpenFailureUnavailable.cfg` | Query-level participant image rejection becomes unavailable before either imported owner advances. | 147,458 / 4 | 2 |
| `ExactAddressRejected.cfg` | An exact-address request rejects before either imported owner advances. | 147,600 / 288 | 2 |
| `MissingTerminalPopulationRejected.cfg` | A group participant missing from the sealed population is rejected before resolution. | 148,032 / 1,152 | 2 |
| `DuplicatePopulationRejected.cfg` | A duplicated sealed occurrence for one group participant is rejected before resolution. | 148,032 / 1,152 | 2 |
| `ForeignPopulationRejected.cfg` | An extra sealed input outside the group is rejected before resolution. | 148,032 / 1,152 | 2 |
| `BroaderResearchPopulationRejected.cfg` | A valid root-only receipt paired with a Research result containing an additional terminal input is rejected before resolution. | 147,744 / 576 | 2 |

## Mutation configurations

| Configuration | Broken policy | Expected result |
| --- | --- | --- |
| `BrokenFacadeEndpoint.cfg` | Replaces a forwarded terminal assembly with the facade. | Violates `SelectedEndpointMatchesResolvedTerminal`. |
| `BrokenCrossSideEndpoint.cfg` | Selects the terminal participant from the opposite comparison side. | Violates `SelectedEndpointBelongsToRequestedSide`. |
| `BrokenResearchReceipt.cfg` | Reconstructs a Research input with a foreign receipt token. | Violates `SelectedResearchAttemptUsesPopulationReceipt`. |
| `BrokenCrossScope.cfg` | Substitutes a resolved attempt and healthy census from another selection scope. | Violates `SelectedScopeMatchesRequest`. |
| `BrokenTerminalCorrespondence.cfg` | Reuses a sealed candidate's collapsed query id for another terminal assembly. | Violates `SelectedResearchInputMatchesSelectedQueryInput`. |
| `BrokenCensusSubstitution.cfg` | Substitutes a healthy census for another domain and attempt set. | Violates `SelectedCensusMatchesAttempt`. |
| `BrokenRootRelabel.cfg` | Relabels the pre-existing forwarded root attempt as resolved. | Violates `RootAttemptIsPreserved`. |
| `BrokenNonResolvedAttempt.cfg` | Selects a pre-existing non-resolved terminal attempt. | Violates `SelectedAttemptIsResolved`. |
| `BrokenForwardingEvidence.cfg` | Drops the Metadata forwarding path. | Violates `ForwardingEvidenceIsPreserved`. |
| `BrokenBindingDrift.cfg` | Ignores an owner-issued binding-version replacement. | Violates `BindingVersionIsPreserved`. |
| `BrokenUnavailableInvocation.cfg` | Completes after typed endpoint unavailability. | Violates `ResearchCompletionHasSelectedEndpoint`. |

## Bounds and non-claims

Three assemblies admit direct resolution, one- and two-hop forwarding,
alternative terminal ownership, and a repeated-candidate cycle in the imported
model. Two sides are the minimum needed to expose cross-side substitution. The
selected side is fixed to Before without loss of symmetry; the mutation uses
After. Boolean admission and sealing facts distinguish a usable terminal,
an admitted but unsealed input, and an out-of-group target without enumerating
irrelevant participant subsets. Separate Booleans represent a repeated sealed
occurrence, an extra selected-side input outside the group, and a selected
terminal receipt member and complete Research member, making multiplicity,
group membership, receipt-domain equality, and Research-domain equality
independently checkable. The immutable terminal candidate represents the
already populated participant whose owner-issued maps may satisfy the route;
nondeterministically choosing `Target` or `Other` covers both possible
terminal identities without deriving a map from the later forwarding result.

Census health derives from the represented attempt kind. A failed terminal
attempt therefore has an owner-valid blocked census and remains unavailable. A
same-side domain with multiple inputs is not represented as one resolved
attempt plus one failed peer because Research classifies that domain
`DomainAmbiguous` before either request resolves. A `DeclaringTypeForwarded`
root attempt has a blocked facade census, while exact forwarded completion
uses the distinct healthy terminal census.

The single binding lifecycle abstracts the invariant shared by every
participant policy consumed by one query. The product design separately
requires checking each participant's live `BindingPolicy.Version` before and
after resolution, with root and non-root implementation gates.

The model does not implement or prove:

- workspace acquisition, publication, image lifetime, or supplemental
  admission;
- Metadata declaration decoding or binding selection;
- Queries population-sealer validation;
- Research request, attempt, domain, census, or correspondence construction;
- two-sided terminal-domain correspondence, which the product design marks
  unverified until its named Release gate lands;
- MVID and metadata-token agreement;
- cancellation, concurrency, CLI, browser, or presentation behavior; or
- implementation conformance.

Those remain owned by their product contracts and named Release gates.

## Running TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md). The repository gate
runs every configuration in a selected model directory. Entries in
`eng/tla-expected-exit-codes.txt` additionally require the listed
configurations to produce their exact semantic verdict.

All 24 configurations are exact-outcome gates. The exhaustive safety and
liveness configurations retain the complete input cross-product and fit the
shared runner's 600-second per-configuration budget. The `CiSafety` and
`CiLiveness` configurations retain a fast successful-owner-input check, while
the nine exact scenarios force promised terminal classifications and the
eleven focused mutations force their intended safety violations.

## Recorded result

The repository-pinned TLA+ v1.8.0 tools completed all positive and exact
scenario configurations:

| Configuration | Generated states | Distinct states | Maximum depth | Result |
| --- | ---: | ---: | ---: | --- |
| Exhaustive safety | 609,080 | 533,048 | 10 | All composition invariants passed over valid, missing, duplicated, extra, broader, unsupported-request, and query-image-rejection inputs. |
| Exhaustive liveness | 609,080 | 533,048 | 10 | `CompositionConverges` passed. |
| CI safety | 151,842 | 3,338 | 10 | Exact-population carried inputs with retained images passed all safety checks and both behavior refinements. |
| CI liveness | 151,842 | 3,338 | 10 | `CompositionConverges` passed over exact-population carried inputs with retained images. |

Every mutation exited with TLC status 12 on its intended invariant:

| Configuration | Generated / distinct | Maximum depth | Counterexample |
| --- | ---: | ---: | --- |
| Broken facade endpoint | 147,612 / 34 | 7 | A forwarding chain reached another assembly, but composition selected the facade. |
| Broken cross-side endpoint | 147,612 / 34 | 7 | A forwarded endpoint used the opposite side's group and input identities. |
| Broken Research receipt | 147,612 / 34 | 7 | The endpoint attempt carried a reconstructed Research input with a foreign receipt token. |
| Broken cross-scope endpoint | 147,612 / 34 | 7 | A coherent resolved attempt, domain, and healthy census from another selection scope became effective. |
| Broken terminal correspondence | 147,612 / 34 | 7 | Another terminal assembly reused the admitted candidate's collapsed query id. |
| Broken census substitution | 147,612 / 34 | 7 | A healthy census from another domain and attempt set replaced the exact terminal census. |
| Broken root relabel | 147,612 / 34 | 7 | A forwarded root attempt was retained as resolved instead of `DeclaringTypeForwarded`. |
| Broken non-resolved attempt | 147,612 / 34 | 7 | A pre-existing unavailable, missing, not-requested, or failed attempt became effective. |
| Broken forwarding evidence | 147,612 / 34 | 7 | A forwarded endpoint completed with an empty retained path. |
| Broken binding drift | 147,618 / 37 | 7 | Composition published after the owner-issued binding version advanced. |
| Broken unavailable invocation | 147,616 / 124 | 4 | Composition completed after a terminal resolution supplied no endpoint. |

The runs used TLC build `2026.09.01.002747`, revision `95b800c`, from the
repository-pinned TLA+ v1.8.0 `tla2tools.jar`. The checked jar SHA-256 was
`dbcc75552f21978a4846688b8e23be1a6b6c0b3fcee35d78fec2df167958ec94`.
The runtime was Homebrew OpenJDK `25.0.4.1`.
