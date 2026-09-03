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
- acquisition-registration-shaped participant identity;
- opaque Queries, Research, attempt, domain, and census ids joined by immutable
  owner-issued maps;
- one immutable admitted terminal candidate chosen before Metadata resolution;
- pre-existing root and terminal attempt kinds plus exact domain-side census
  records; and
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
  + query input
  + projected Research input
  + Research domain and pre-existing attempt
```

The concrete product also retains the exact selection-scope identity,
catalog-generation identity, domain-side census, and MVID-scoped address
evidence. The model represents carried versus exact-address request kind,
terminal-domain health, and attempt identity, while abstracting the detailed
payloads.

The receipt, attempt, and census maps are installed in `Init` and never change.
Their opaque ids model already completed owner work; no composition transition
constructs a Research identity, attempt, domain, or census. TLA+ record values
encode the associations exposed by those maps, not permission for product
composition to reproduce an owner-issued identity structurally.

## Imported contract

`Forwarding` is a named `INSTANCE` of `TypeForwardingResolution` with explicit
constant and variable substitutions. The safety configuration rechecks the
imported path, phase, scope, cycle, hop-budget, terminal ownership, declaration,
and candidate-validation properties. It also checks
`ForwardingBehaviorRefinesOwner`, which requires the composed behavior to
satisfy the imported temporal specification.

`BindingLifecycle` is a named `INSTANCE` of
`AssemblyBindingPolicyVersionLifecycle`. The safety configuration rechecks
fresh replacement and `BindingBehaviorRefinesOwner`.

The composition calls `Forwarding!Advance` and `BindingLifecycle!Advance`; it
does not copy either owner's transitions. Once forwarding reaches `Terminal`,
Queries either selects the pre-existing attempt, reports typed non-success, or
detects binding-version drift as a contract fault.

## Checked properties

The safety configuration checks that:

- a selected endpoint belongs to the requested side and its admitted group;
- a selected endpoint is the exact terminal assembly from Metadata;
- the exact Research attempt comes from the Queries-to-Research projection;
- the selected Research input retains the selected Queries input's exact
  terminal acquisition registration;
- the selected census is the exact domain-side census containing that attempt;
- the selected attempt is resolved;
- the selected terminal domain is healthy;
- the pre-existing root attempt is preserved;
- forwarding hops and the captured binding-policy version are preserved;
- direct resolution retains the facade root;
- a forwarded root remains input-locally unavailable; and
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

## Mutation configurations

| Configuration | Broken policy | Expected result |
| --- | --- | --- |
| `BrokenFacadeEndpoint.cfg` | Replaces a forwarded terminal assembly with the facade. | Violates `SelectedEndpointMatchesResolvedTerminal`. |
| `BrokenCrossSideEndpoint.cfg` | Selects the terminal participant from the opposite comparison side. | Violates `SelectedEndpointBelongsToRequestedSide`. |
| `BrokenResearchReceipt.cfg` | Reconstructs a Research input with a foreign receipt token. | Violates `SelectedResearchAttemptUsesPopulationReceipt`. |
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
irrelevant participant subsets. The immutable terminal candidate represents
the already populated participant whose owner-issued maps may satisfy the
route; nondeterministically choosing `Target` or `Other` covers both possible
terminal identities without deriving a map from the later forwarding result.

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
- two-sided terminal-domain correspondence;
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

The exhaustive safety and liveness configurations retain the complete input
cross-product and are recorded below from a pinned local run. They are
intentionally unlisted because each exceeds the shared CI runner's 120-second
per-configuration budget. The listed `CiSafety` and `CiLiveness`
configurations preserve the full forwarding graph, both terminal candidates,
binding replacement, all safety invariants, and convergence while constraining
the static owner inputs to successful admitted evidence. The focused mutation
configurations cover the excluded invalid-input dimensions.

## Recorded result

The repository-pinned TLA+ v1.8.0 tools completed both positive
configurations:

| Configuration | Generated states | Distinct states | Maximum depth | Result |
| --- | ---: | ---: | ---: | --- |
| Exhaustive safety | 10,083,184 | 7,650,160 | 10 | All imported and composition invariants passed; both behavior refinements passed. |
| Exhaustive liveness | 10,083,184 | 7,650,160 | 10 | `CompositionConverges` passed. |
| CI safety | 22,818 | 3,338 | 10 | The CI-bounded inputs passed the same safety and refinement checks. |
| CI liveness | 22,818 | 3,338 | 10 | `CompositionConverges` passed over the CI-bounded inputs. |

Every mutation exited with TLC status 12 on its intended invariant:

| Configuration | Generated / distinct | Maximum depth | Counterexample |
| --- | ---: | ---: | --- |
| Broken facade endpoint | 3,275,942 / 2,526,044 | 6 | A forwarding chain reached another assembly, but composition selected the facade. |
| Broken cross-side endpoint | 3,446,910 / 2,651,631 | 6 | A forwarded endpoint used the opposite side's group and input identities. |
| Broken Research receipt | 318,981 / 277,320 | 3 | The endpoint attempt carried a reconstructed Research input with a foreign receipt token. |
| Broken terminal correspondence | 3,759,364 / 2,877,428 | 6 | Another terminal assembly reused the admitted candidate's collapsed query id. |
| Broken census substitution | 318,340 / 276,798 | 3 | A healthy census from another domain and attempt set replaced the exact terminal census. |
| Broken root relabel | 3,754,354 / 2,877,413 | 6 | A forwarded root attempt was retained as resolved instead of `DeclaringTypeForwarded`. |
| Broken non-resolved attempt | 3,720,924 / 2,852,295 | 6 | A pre-existing unavailable, missing, not-requested, or failed attempt became effective. |
| Broken forwarding evidence | 3,753,769 / 2,876,986 | 6 | A forwarded endpoint completed with an empty retained path. |
| Broken binding drift | 1,112,549 / 770,861 | 4 | Composition published after the owner-issued binding version advanced. |
| Broken unavailable invocation | 700,288 / 562,104 | 4 | Composition completed after a terminal resolution supplied no endpoint. |

The runs used TLC build `2026.09.01.002747`, revision `95b800c`, from the
repository-pinned TLA+ v1.8.0 `tla2tools.jar`. The checked jar SHA-256 was
`dbcc75552f21978a4846688b8e23be1a6b6c0b3fcee35d78fec2df167958ec94`.
The runtime was Homebrew OpenJDK `25.0.4.1`.
