# Binding selection/version models

These TLA+ models are the executable interaction companions to
[Structured type-forwarding resolution](../../type-forwarding-resolution.md#atomic-selectionversion-snapshots).
`BindingSelectionVersion` checks one captured policy version, a cold or cached
binding answer, commit-point validation, and immutable generation publication.
`CompositeBindingVersion` checks delegated-version drift, transforming
composite tokens, immutable routing-state replacement, and retry progress.
Both the composite model and the workspace consumer instantiate the
owner-issued `AssemblyBindingPolicyVersionLifecycle` module for the modeled
outer-token replacement.

The models answer these focused questions:

- Can a selection be paired with a version observed from another policy state?
- Can a retired version token become current again without exposing an ABA
  transition?
- Can a new answer be accepted under a reused old token?
- Can an old cache entry become current under a reused token?
- Can Metadata publish binding or resolution caches, or make a generation
  current, before the version commit point?
- Can a policy change before commit go undetected?
- Can a policy change after commit incorrectly invalidate historical
  publication?
- Does a matching transformed answer carry the composite token?
- Does delegate drift retire composite state before propagating a foreign
  snapshot, and can the next generation make progress?
- Can source-relative route learning change an answer under the old token?
- Does each consumer mutate the composed policy version only through the
  owner-issued lifecycle action or a stuttering step?

## Relationship to the product

### Selection commit and publication

Each initial state chooses a cold selection or version-keyed cache lookup. The
consumer captures `VersionOne` from the initial policy state. Policy state may
advance twice while the request is active, allowing transitions before answer
creation, between answer and version association, before commit validation, or
after commit but before physical publication.

The policy configuration atomically returns the answer and version of one
state, assigns a fresh token to each replacement state, and requires the
current token still to equal the captured token at the commit linearization
point. Binding-cache, resolution-cache, and current-generation publication
occur only after commit. A later policy change does not retroactively
invalidate that immutable historical generation. The cache branch represents
a previously frozen `(VersionOne, AnswerOne)` entry.

### Transforming composite policy

Each composite initial state chooses stable delegation, delegate drift, or
source-relative route learning. A stable delegated answer is interpreted and
returned under `CompositeVersionOne`. Delegate drift atomically publishes
`CompositeVersionTwo` with the observed delegate version before forwarding the
foreign snapshot without interpreting it. Route learning likewise publishes a
fresh composite token before the changed answer can be used. Both replacement
paths retry and complete under the refreshed token.

`AssemblyBindingPolicyVersionLifecycle` owns the reusable two-generation
abstraction: one initial outer token, one distinct replacement token, and the
only modeled transition between them. `CompositeBindingVersion` binds that
module to `compositeVersion` and `refreshed`, uses its `Advance` action for both
refresh paths, and rechecks its freshness invariant and safety specification.
The
[workspace binding-policy realization model](../workspace-binding-policy-realization/README.md)
binds the same owner module to its current composed-policy token.

## Assumptions and non-claims

The three policy states have distinct answers so answer/version skew is
observable. Real equal answers remain safe because version replacement still
invalidates the generation and structural binding snapshots decide whether a
resolution recipe can be reused later.

The models abstract request identity, selection arms and evidence, descriptor
contents, acquisition registration, retained sessions, inventories,
declaration caching, resource budgets, policy-specific state construction,
workspace realization, and host retry timing. The selection model represents
binding-cache, resolution-cache, and current-generation publication only as
booleans so it can prove their commit ordering, not their payload correctness.
Validated acquisition and declaration evidence may be retained before the
final comparison under the owning design's existing rules; this model neither
requires nor proves transactional rollback of those effects. The composite
model treats routing and delegate answers as abstract state.
Its refreshed retry runs against stable replacement state. Repeated delegate
or route changes may supersede repeated generations; convergence under
continuing policy churn and the timing of another attempt belong to the
workspace owner rather than this policy-local model.
Issue #5224 owns miss dispositions; the
[binding composition-currency model](../binding-composition-currency/README.md)
owns the complete identity-eligible handoff; #5216 owns workspace generation
replacement. TLC results establish properties of these state machines under
the stated bounds, not of the shipped implementation. Formal
model-to-implementation correspondence is unverified.

## Checked configurations

### Selection configurations

| Configuration | Purpose |
| --- | --- |
| `BindingSelectionVersionSafety.cfg` | Explores cold and cached requests across every policy-advance timing. It checks atomic returned snapshots, fresh replacement tokens, captured-version answer integrity, commit-point current-version validation, stale-cache rejection, and policy-dependent publication only after commit. |
| `BindingSelectionVersionLiveness.cfg` | Checks that every cold or cached request eventually commits or aborts and reaches terminal completion under weak fairness. |
| `BindingSelectionVersionBrokenAssociation.cfg` | Reads the version separately after answer creation. It must violate `ReturnedColdSnapshotIsAtomic`. |
| `BindingSelectionVersionBrokenColdAba.cfg` | Reuses `VersionOne` for the third state and new answer. It must violate `CommittedAnswerBelongsToCapturedVersion`. |
| `BindingSelectionVersionBrokenCachedAba.cfg` | Reuses `VersionOne` so the initial cache entry becomes current in the third state. It must violate `CachedAnswerNotCommittedAfterStateChange`. |
| `BindingSelectionVersionBrokenFinalValidation.cfg` | Skips the commit-point current-version comparison. It must violate `CommitObservedCapturedVersion`. |
| `BindingSelectionVersionBrokenPrecommitMutation.cfg` | Publishes binding and resolution cache state and makes the generation current before validation. It must violate `UncommittedGenerationHasNoPolicyPublication`. |

### Composite configurations

| Configuration | Purpose |
| --- | --- |
| `CompositeBindingVersionSafety.cfg` | Checks stable composite-token success, uninterpreted foreign-snapshot propagation, atomic composite refresh, route-version replacement, stale-token exclusion, and refreshed retry completion. |
| `CompositeBindingVersionLiveness.cfg` | Checks that stable, delegate-drift, and route-change evaluations all reach a completed answer under weak fairness. |
| `CompositeBindingVersionBrokenSuccessToken.cfg` | Returns a matching transformed answer under the delegate token. It must violate `StableMatchUsesCompositeVersion`. |
| `CompositeBindingVersionBrokenRelabel.cfg` | Interprets delegate drift and relabels it with the stale composite token. It must violate `OldCompositeTokenNeverGovernsChangedAnswer`. |
| `CompositeBindingVersionBrokenNoRefresh.cfg` | Forwards drift without refreshing composite state. It must violate `EvaluationConverges`. |
| `CompositeBindingVersionBrokenRouteToken.cfg` | Changes routing and its answer under the old composite token. It must violate `OldCompositeTokenNeverGovernsChangedAnswer`. |
| `CompositeBindingVersionBrokenLifecycle.cfg` | Writes the projected lifecycle state without the owner action. It must violate `BindingVersionBehaviorRefinesOwner`. |
| `AssemblyBindingPolicyVersionLifecycle.cfg` | Checks the owner-issued initial state, fresh replacement, type invariant, and eventual modeled advance independently of either consumer. |

All configurations disable TLC's deadlock check because `Done` is an
intentional terminal phase. The temporal specifications permit stuttering in
that state.

## Running TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md) for the pinned
toolchain. Run configurations sequentially because concurrent TLC processes
using `-cleanup` can remove one another's metadata.

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/binding-selection-version

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config BindingSelectionVersionSafety.cfg \
  BindingSelectionVersion.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config BindingSelectionVersionLiveness.cfg \
  BindingSelectionVersion.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config CompositeBindingVersionSafety.cfg \
  CompositeBindingVersion.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config CompositeBindingVersionLiveness.cfg \
  CompositeBindingVersion.tla
```

The mutation configurations are expected to exit unsuccessfully:

```bash
java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config BindingSelectionVersionBrokenAssociation.cfg \
  BindingSelectionVersion.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config BindingSelectionVersionBrokenColdAba.cfg \
  BindingSelectionVersion.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config BindingSelectionVersionBrokenCachedAba.cfg \
  BindingSelectionVersion.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config BindingSelectionVersionBrokenFinalValidation.cfg \
  BindingSelectionVersion.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config BindingSelectionVersionBrokenPrecommitMutation.cfg \
  BindingSelectionVersion.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config CompositeBindingVersionBrokenSuccessToken.cfg \
  CompositeBindingVersion.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config CompositeBindingVersionBrokenRelabel.cfg \
  CompositeBindingVersion.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config CompositeBindingVersionBrokenNoRefresh.cfg \
  CompositeBindingVersion.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config CompositeBindingVersionBrokenRouteToken.cfg \
  CompositeBindingVersion.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config CompositeBindingVersionBrokenLifecycle.cfg \
  CompositeBindingVersion.tla
```

## Recorded result

The positive selection configurations completed with no errors:

| Configuration | Generated states | Distinct states | Maximum depth | Result |
| --- | ---: | ---: | ---: | --- |
| Safety | 38 | 36 | 7 | All eight invariants passed. |
| Liveness | 38 | 36 | 7 | `SelectionConverges` passed. |

The safety graph starts both cold and cache operations. It executed two
`Capture`, thirteen `Advance`, three `EvaluateCold`, three `EvaluateCache`,
nine `Commit`, and six `Publish` transitions.
`AssociateObservedVersion` is intentionally unreachable in the positive
atomic-association configuration and is exercised by its mutation.

Each selection mutation exited with TLC status 12 on its intended invariant:

| Configuration | Generated / distinct | Maximum depth | Counterexample |
| --- | ---: | ---: | --- |
| Broken association | 21 / 20 | 5 | Policy state advanced between answer creation and a consumer-side version read, producing a version/answer pair from different states and violating `ReturnedColdSnapshotIsAtomic`. |
| Broken cold ABA | 29 / 27 | 6 | The third state reused `VersionOne`, so its new cold answer committed as belonging to the captured first-state version and violated `CommittedAnswerBelongsToCapturedVersion`. |
| Broken cached ABA | 34 / 32 | 6 | The third state reused `VersionOne`, making the first-state cache entry current again and violating `CachedAnswerNotCommittedAfterStateChange`. |
| Broken commit validation | 21 / 20 | 5 | Policy state advanced after a valid answer, commit skipped the current-version comparison, and `commitVersion` differed from the captured token. |
| Broken pre-commit mutation | 6 / 6 | 3 | Cold evaluation published binding and resolution caches and made the generation current before version validation, violating `UncommittedGenerationHasNoPolicyPublication`. |

The standalone owner lifecycle completed with two generated and two distinct
states at depth two. `TypeOK`, `AdvancedVersionIsFresh`, and
`VersionEventuallyAdvances` passed.

The positive composite configurations also completed with no errors:

| Configuration | Generated states | Distinct states | Maximum depth | Result |
| --- | ---: | ---: | ---: | --- |
| Safety | 11 | 11 | 4 | All eight invariants and the owner-behavior refinement property passed. |
| Liveness | 11 | 11 | 4 | `EvaluationConverges` passed. |

The composite graph starts all three scenarios. It executed three `Begin`
transitions, one stable evaluation, one delegate-drift evaluation, one route
change evaluation, and two refreshed retries. Those state and action counts
match the pre-composition model, proving that the imported `Advance` guard did
not prune an existing behavior.

The three composite invariant mutations exited with TLC status 12. The broken
refresh and owner-lifecycle temporal mutations exited with status 13. Every
mutation failed on its intended property:

| Configuration | Generated / distinct | Maximum depth | Counterexample |
| --- | ---: | ---: | --- |
| Broken success token | 7 / 7 | 3 | A stable transformed answer returned the delegate token instead of `CompositeVersionOne`, violating `StableMatchUsesCompositeVersion`. |
| Broken drift relabel | 8 / 8 | 3 | Delegate drift was interpreted as `AnswerTwo` and relabeled with stale `CompositeVersionOne`, violating `OldCompositeTokenNeverGovernsChangedAnswer`. |
| Broken drift refresh | 10 / 10 | 4 | Drift was forwarded without publishing fresh composite state, leaving `Retry` unable to progress and violating `EvaluationConverges`. |
| Broken route token | 9 / 9 | 3 | Route learning changed the answer under stale `CompositeVersionOne`, violating `OldCompositeTokenNeverGovernsChangedAnswer`. |
| Broken owner lifecycle | 8 / 8 | 3 | The consumer marked the policy refreshed without replacing its token through the owner action, violating `BindingVersionBehaviorRefinesOwner`. |

The runs used the repository-pinned TLA+ v1.8.0 tools, TLC build
`2026.09.01.002747` revision `95b800c`. The checked `tla2tools.jar`
SHA-256 was
`dbcc75552f21978a4846688b8e23be1a6b6c0b3fcee35d78fec2df167958ec94`.
The available runtime was OpenJDK `21.0.12`; the runbook's preferred Java 25
runtime was not installed on this shared host. Java 21 satisfies the tool's
Java 11-or-later requirement, so the machine configuration was left unchanged
and the runtime deviation is recorded here.
