# Workspace binding-policy realization model

This TLA+ model is the executable interaction companion to
[Explicit local/designated/platform assembly context](../../artifact-acquisition-and-workspaces.md#explicit-localdesignatedplatform-assembly-context).
It checks the workspace-owned preparation, adoption, publication, retirement,
and replacement of one assembly context carrying a completed composed binding
policy.

## Questions checked

- Can a policy completion from another preparation or participant plan be
  adopted?
- Can changed role evidence, a changed delegate map, or a foreign policy
  version reach group construction?
- Does policy adoption precede group construction?
- Can a group or policy become visible without the other?
- Can pre-publication policy drift reach current workspace state?
- Does each rejected preparation retain its exact typed cause?
- Does observed drift make the old generation unavailable before a replacement
  becomes current?
- Does every started generation settle, and can a replacement complete?

## Relationship to adjacent models

The model consumes rather than redefines three adjacent contracts:

- the
  [binding selection/version contract](../../type-forwarding-resolution.md#atomic-selectionversion-snapshots)
  owns non-reusable policy versions, atomic selection snapshots, and
  policy-local refresh; its
  [models](../binding-selection-version/README.md) check those interactions;
- the
  [binding composition-currency model](../binding-composition-currency/README.md)
  owns the complete identity-eligible handoff and its finalization; and
- the
  [artifact-session admission model](../../../models/artifact-session-admission/README.md)
  owns demand joining, cancellation, reservation, adapter completion, and
  aggregate artifact publication.

`WorkspaceBindingPolicyRealization` begins after acquisition and assembly
projection have produced one exact planned participant-registration sequence,
role projection, and delegate map. It ends when the workspace atomically
exposes a matching group/policy pair or records a typed failure. Existing query
leases, group cleanup, and resource quiescence remain owned by the
generation-access, group-lifecycle, and workspace-close models.

## State space

The bounded state machine uses two context generations and two non-reusable
policy versions. The first generation explores:

- exact completion plus version advance before completion, after completion,
  after adoption, after private group construction, after publication, or
  never; and
- one mismatch each for preparation identity, participant plan, role
  projection, delegate map, and completion version.

Mismatch scenarios do not also advance the composite policy version, keeping each
typed failure attributable to one cause. Those failures are terminal and do not
promise an automatic successful retry. Only retirement after observed drift
enables an exact second generation. The second generation proves replacement
progress once started, without claiming that the workspace starts it
automatically or converges while policy state continues changing.

The model represents participant plans, role projections, and delegate maps as
opaque owner-issued values. It proves exact association and lifecycle ordering,
not their internal construction or policy meaning.

## Assumptions and non-claims

The one modeled version is the composed policy's distinct outer
`AssemblyBindingPolicyVersion`. Individual delegate versions and the
composite's owner-local refresh are abstracted into one outer-token advance.

Current publication is modeled as one owner-mediated group/policy reference
pair. There is no independent reader action, memory-model claim, or proof of a
particular synchronization primitive. The named implementation gate must
establish that concurrent readers can observe only one complete current pair.

The model does not represent reservation arithmetic, quiescent cleanup, or the
possibility that a requested replacement is refused or fails. The second
generation is the admitted, stable-policy success scenario. Its temporal
property applies only after replacement preparation starts.

Each mismatch scenario changes one receipt field. The model proves that each
cause is rejected and typed independently, but not the precedence when one
receipt contains several mismatches; the correspondence implementation gate
owns that ordered case. The model permits only one outer-token advance and
makes no progress claim under continuing policy churn.

## Drift and availability

`AdvanceComposedPolicyVersion` represents the adjacent composite-policy owner
replacing its outer non-reusable version. `RequestCurrentAccess` represents an
access request arriving after that change; it has not entered the generation or
observed policy state. `ObservePublishedDrift` represents the workspace gate
that detects the mismatch and atomically retires current availability. The old
immutable generation may remain current before an access request reaches that
gate; its already committed answers remain valid under the binding-version
contract.

Once the workspace observes the mismatch, it atomically removes both the group
and policy from current admission before starting the replacement. The model
does not prescribe notification or polling cadence. Existing leased work and
physical cleanup after retirement remain governed by adjacent lifetime models;
"unavailable" here means no current-generation admission or new context view,
not immediate resource disposal.

## Checked properties

| Property | Claim |
| --- | --- |
| `TypeOK` | Every state value remains inside its declared finite model domain. |
| `PublicationIsAtomic` | Current group and policy visibility are both absent or name the same generation. |
| `PublishedGenerationIsComplete` | A current generation has one adopted exact completion and a privately constructed group. |
| `GroupConstructionRequiresPolicyAdoption` | No placeholder or post-construction policy insertion can create a group. |
| `AdoptedPolicyMatchesPreparation` | The completion belongs to the exact workspace-issued preparation. |
| `AdoptedPolicyMatchesParticipants` | The completion covers the exact planned participant sequence. |
| `AdoptedPolicyMatchesRoles` | The completion covers the exact immutable role projection. |
| `AdoptedPolicyMatchesDelegateMap` | The completion covers the exact delegated-policy map. |
| `AdoptedPolicyMatchesCapturedVersion` | The completion carries the version captured by its preparation. |
| `FailureClassificationIsExact` | Every rejected completion or pre-publication drift records its exact typed cause. |
| `FailedGenerationIsUnavailable` | A failed generation publishes neither group nor policy. |
| `RetiredGenerationIsUnavailable` | Observed drift removes both current handles for the retired generation. |
| `ReplacementFollowsRetirement` | A replacement cannot publish before a previously published generation is retired. |
| `PublicationObservedCurrentVersion` | Every publish step independently witnesses that its captured version was current. |
| `RetirementWasAtomic` | Every drift-retirement step independently witnesses atomic group/policy removal. |
| `EveryStartedGenerationSettles` | Under weak fairness, each started generation reaches failure, publication, or retirement. |
| `ObservedVersionDriftEventuallyRetires` | After a current-access gate requests drift observation, the foreign-version generation eventually retires. |
| `StartedReplacementEventuallyPublishes` | Once the admitted stable replacement starts, it eventually publishes. |

## Configurations

| Configuration | Purpose |
| --- | --- |
| `WorkspaceBindingPolicyRealizationSafety.cfg` | Checks all fifteen safety invariants over exact, mismatched, pre-publication-drift, published-drift, and replacement scenarios. |
| `WorkspaceBindingPolicyRealizationLiveness.cfg` | Checks build settlement, requested-access drift retirement, and progress after stable replacement starts. |
| `BrokenPreparationMatch.cfg` | Accepts a completion from another preparation; it must violate `AdoptedPolicyMatchesPreparation`. |
| `BrokenParticipantMatch.cfg` | Accepts a foreign participant plan; it must violate `AdoptedPolicyMatchesParticipants`. |
| `BrokenRoleMatch.cfg` | Accepts a foreign role projection; it must violate `AdoptedPolicyMatchesRoles`. |
| `BrokenDelegateMapMatch.cfg` | Accepts a foreign delegate map; it must violate `AdoptedPolicyMatchesDelegateMap`. |
| `BrokenCompletionVersion.cfg` | Accepts a foreign completion version; it must violate `AdoptedPolicyMatchesCapturedVersion`. |
| `BrokenFailureClassification.cfg` | Collapses a specific mismatch into the policy-version failure; it must violate `FailureClassificationIsExact`. |
| `BrokenPolicyBeforeGroup.cfg` | Constructs a group directly from an unadopted completion; it must violate `GroupConstructionRequiresPolicyAdoption`. |
| `BrokenPublishVersion.cfg` | Publishes after pre-publication version drift; it must violate `PublicationObservedCurrentVersion`. |
| `BrokenAtomicPublication.cfg` | Publishes a group without its policy; it must violate `PublicationIsAtomic`. |
| `BrokenAtomicRetirement.cfg` | Retires the group while leaving its policy current; it must independently violate `RetirementWasAtomic`. |
| `BrokenReplacementBeforeRetirement.cfg` | Publishes generation two over a still-current generation one; it must violate `ReplacementFollowsRetirement`. |
| `ReachabilityReplacement.cfg` | Negates replacement publication and fails only after a complete retire-and-replace trace. |

## Running TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md) for the pinned
toolchain. Run configurations sequentially because concurrent TLC processes
using `-cleanup` can remove one another's metadata.

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/workspace-binding-policy-realization

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config WorkspaceBindingPolicyRealizationSafety.cfg \
  WorkspaceBindingPolicyRealization.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config WorkspaceBindingPolicyRealizationLiveness.cfg \
  WorkspaceBindingPolicyRealization.tla
```

The broken and reachability configurations are expected to exit
unsuccessfully:

```bash
for config in \
  BrokenPreparationMatch \
  BrokenParticipantMatch \
  BrokenRoleMatch \
  BrokenDelegateMapMatch \
  BrokenCompletionVersion \
  BrokenFailureClassification \
  BrokenPolicyBeforeGroup \
  BrokenPublishVersion \
  BrokenAtomicPublication \
  BrokenAtomicRetirement \
  BrokenReplacementBeforeRetirement \
  ReachabilityReplacement
do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
    -workers 1 -cleanup -noGenerateSpecTE \
    -config "$config.cfg" WorkspaceBindingPolicyRealization.tla
done
```

## Recorded result

The positive configurations completed with no errors:

| Configuration | Generated states | Distinct states | Maximum depth | Result |
| --- | ---: | ---: | ---: | --- |
| Safety | 68 | 65 | 14 | All fifteen safety invariants passed. |
| Liveness | 68 | 65 | 14 | All three temporal properties passed. |

The safety graph starts eleven initial scenarios. It executed 12 preparations,
12 policy completions, 12 adoption decisions, five private group constructions,
three publications, three pre-publication invalidations, five composite-policy
version advances, one current-access request, and one published-generation
retirement.

Every mutation exited with TLC status 12 on its intended invariant:

| Configuration | Generated / distinct | Maximum depth | Counterexample |
| --- | ---: | ---: | --- |
| Broken preparation match | 41 / 41 | 4 | A foreign preparation completion became adopted. |
| Broken participant match | 42 / 42 | 4 | A completion for another participant plan became adopted. |
| Broken role match | 43 / 43 | 4 | A completion for another role projection became adopted. |
| Broken delegate-map match | 44 / 44 | 4 | A completion for another delegate map became adopted. |
| Broken completion version | 45 / 45 | 4 | A foreign policy version became adopted. |
| Broken failure classification | 41 / 41 | 4 | A preparation mismatch was reported as policy-version drift. |
| Broken policy-before-group | 35 / 35 | 4 | Private group construction bypassed policy adoption. |
| Broken publish version | 61 / 57 | 7 | A group/policy pair published after its captured version became foreign. |
| Broken atomic publication | 54 / 52 | 6 | The group became current without its policy. |
| Broken atomic retirement | 63 / 60 | 9 | Retirement removed the group but left its policy current. |
| Broken replacement ordering | 79 / 70 | 12 | Generation two published over a still-current generation one. |
| Replacement reachability | 68 / 65 | 14 | Generation one retired after observed drift, then generation two published. |

The runs used the repository-pinned TLA+ v1.8.0 tools, TLC build
`2026.08.21.155922` revision `9787e65`. The checked `tla2tools.jar` SHA-256 was
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.
The available runtime was OpenJDK `21.0.12`; the runbook's preferred Java 25
runtime was not installed on this shared host. Java 21 satisfies the tool's
Java 11-or-later requirement, so the machine configuration was left unchanged
and the runtime deviation is recorded here.
