# TLA+ methodology

TLA+ is used per
[`docs/design-scope.md`](design-scope.md#keep-specifications-readable-model-interactions) to
check stateful or concurrent interactions that are hard to reason about in
prose alone.

Keep each model in its own directory, normally under `docs/models/` or the
owning design's `models/` directory. Keep its `.tla` module and any companion
`.cfg` configurations, model `README.md`, or local exclusions for generated
TLC artifacts together; do not place standalone model files in the parent
models directory or combine unrelated models in one directory.

## Compose models along product boundaries

TLA+ modules can share definitions and behavior across model directories.
TLA+ is untyped, so this is reuse of constants, operators, state variables,
actions, and temporal formulas rather than Lean-style type or proof import.
Likewise, TLC's successful bounded check of one instance is not a proof
certificate transferred to another instance. A consumer gains confidence by
rechecking the owner's properties under the consumer's additional states,
actions, schedules, and fairness assumptions.

Keep reusable modules with the component that owns the corresponding product
contract. Give every repository module a globally unique, owner-specific name,
and keep the import graph acyclic and pointed in the same direction as product
dependencies. Do not create a generic common-module collection or move a
currency away from its product owner merely to make reuse convenient. SANY
rejects cycles; reviewers enforce the product-dependency direction.

Use a named `INSTANCE` with explicit substitutions when a consumer binds an
owner module's constants or variables:

```tla
BindingVersion ==
    INSTANCE AssemblyBindingPolicyVersionLifecycle WITH
        InitialVersion <- VersionOne,
        ReplacementVersion <- VersionTwo,
        version <- liveVersion,
        advanced <- versionAdvanced
```

Named instances preserve the ownership boundary in expressions such as
`BindingVersion!Advance`. A consumer configuration cannot name a qualified
operator directly, so expose intentional checks through local aliases:

```tla
BindingVersionAdvanceIsFresh ==
    BindingVersion!AdvancedVersionIsFresh

BindingVersionBehaviorRefinesOwner ==
    BindingVersion!SafetySpec
```

The owner module keeps its own finite harness and configurations. A consumer
then:

- uses the owner action rather than duplicating its assignments;
- restates the owner's assumptions as consumer-local obligations under the
  chosen substitutions;
- rechecks owner invariants through local aliases;
- checks that its projected behavior refines the owner's safety specification;
- adds separate cross-layer invariants and liveness properties; and
- records the instance substitutions, inherited checks, and remaining
  abstractions in its model README.

Keep scenario bounds, mutation switches, and consumer-specific fairness out of
the reusable owner module. Import the smallest stable boundary that the
consumer needs rather than an entire lower-layer state machine. A focused
negative control should bypass or weaken the imported boundary and demonstrate
that the consuming configuration detects the violation; merely restating the
same predicate in two modules is not composition evidence.

An imported action brings its guards as well as its assignments. Compare the
consumer's state graph and action coverage before and after adoption so a
stronger owner guard cannot silently prune behavior that the prior model
explored. A direct imported invariant may be implied by the stronger behavior
refinement and still be useful as a focused diagnostic; do not count the two
formulas as independent evidence when one entails the other.

`eng/run-tla-checks.sh` supplies every model directory through TLA Tools'
`TLA-Library` path. For a changed module, it uses SANY's resolved
`EXTENDS`/`INSTANCE` closure to select direct and transitive consumers.
Deleting an imported module fails when SANY checks its surviving consumer;
updated consumers and replacement modules are selected by their own changed
paths. Duplicate repository module names and names that shadow modules in the
pinned TLA+ standard library fail before checking because the repository
library has one module namespace.

List contract-defining configurations in
`eng/tla-expected-exit-codes.txt`. The per-PR gate requires each listed
configuration to produce its exact TLC semantic exit code; a different
coherent verdict and a timeout both fail. Changing the manifest checks every
model directory it names, and malformed, duplicate, stale, non-canonical, or
unsupported entries fail before TLA Tools run. Keep this manifest sparse:
unlisted legacy configurations continue to accept any recognized coherent TLC
verdict, and an unlisted timeout remains explicitly unverified rather than
failing an unrelated PR.

A TLA+ model that exists only as uncommitted files in a local worktree is not
a checked-in asset: it is not backed up, reviewable, or visible to other
contributors and agents. Commit a model to its branch and push that branch as
soon as it reaches a checkable state: its module parses and TLC runs against
its `.cfg` without unexpected errors. Do not wait for the owning design PR to
be otherwise complete. Treat an uncommitted
`.tla`/`.cfg`/model-`README.md` set as a bug to fix, not a stable resting
state.

The per-PR TLA+ gate checks each model directory whose `.tla` or `.cfg` files
change in that candidate, plus direct and transitive consumers and model
directories named by a changed exact-outcome manifest. Other
gate-infrastructure changes run structural gate tests but do not check
unchanged model content. The gate does not sweep unrelated committed models.
Run `eng/run-tla-checks.sh --all` only for an explicit repository-wide local
investigation; it is not a per-PR gate.

The examples below are a user-curated set of at most six merged models, not an
inventory of repository models. The set is intentionally incomplete.
Contributors and agents must not add a model as part of normal model work; only
the user may add, remove, or replace a curated example.

## TsJsExportLifecycle

[`TsJsExportLifecycle.tla`](design/models/ts-jsexport-lifecycle/TsJsExportLifecycle.tla)
is accompanied by scenario and mutation configurations and a model
[`README.md`](design/models/ts-jsexport-lifecycle/README.md).

It models two generated `ts-jsexport` facades, multiple callers, one shared SDK
runtime, shared-in-flight and serialized coordination, local failure isolation,
terminal state, and bounded realm restart. It demonstrates separate
success/failure scenarios and targeted counterexample mutations without
claiming implementation or browser conformance.

## ArtifactSessionAdmission

[`ArtifactSessionAdmission.tla`](models/artifact-session-admission/ArtifactSessionAdmission.tla)
and its [model guide](models/artifact-session-admission/README.md)
model `ArtifactSetSession` admission for
[Artifact acquisition and workspace composition](design/artifact-acquisition-and-workspaces.md#artifactsetsession).

The model demonstrates single-flight admission, incompatible-generation
exclusion, cancellation before attachment and after disposal enters draining,
voluntary and disposal-forced draining, late-result suppression, guard
witnesses, and weak-fairness progress. Focused broken-policy configurations
prove the exact incompatible-pending and post-disposal-draining paths are
required for progress. Guard mutations prove cancellation requires an
owner-recorded request, while reachability configurations prove each exact race
executes in the intended order.

## Inspection subject navigation

[`NavigationSession.tla`](design/models/inspection-subject-navigation/NavigationSession.tla),
[`AtomicRestoration.tla`](design/models/inspection-subject-navigation/AtomicRestoration.tla),
and
[`SnapshotAuthority.tla`](design/models/inspection-subject-navigation/SnapshotAuthority.tla)
are accompanied by matching configurations and a shared model
[`README.md`](design/models/inspection-subject-navigation/README.md).

The three independent models divide one design into retained-session ordering
and effect authority, atomic subject-and-lens restoration, and retained versus
stateless snapshot custody. They demonstrate choosing separate finite models
for orthogonal mechanisms, naming the token or operation whose progress a
liveness property claims, independently retaining requested payloads, and
pairing action coverage with targeted mutation probes.

## AssemblyContextGroupLifecycle

[`AssemblyContextGroupLifecycle.tla`](models/assembly-context-group-lifecycle/AssemblyContextGroupLifecycle.tla)
is accompanied by safety, liveness, and mutation configurations and a model
[`README.md`](models/assembly-context-group-lifecycle/README.md).

It models the existing `AssemblyContextGroup` callback, image-budget,
result-publication, finalization, disposal, and quiescent-release lifecycle for
[Inspection space](inspection-space.md). It demonstrates same-participant
contention, ordinary versus one-shot release, exceptional retry, resource
ordering, and independent counterexample mutations.

## PlatformOverlayResolution

[`PlatformOverlayResolution.tla`](design/models/platform-overlay-resolution/PlatformOverlayResolution.tla)
is accompanied by safety, liveness, and broken-policy configurations and a
model
[`README.md`](design/models/platform-overlay-resolution/README.md).

It models arbitration among already-classified designated, platform, and
unentitled candidates across every registration order. It demonstrates proving
policy precedence independent of incidental order and version equality,
retaining shadowed candidates as evidence, making unruled ties visible, and
using committed negative controls to prevent a known coherence failure from
becoming a success-shaped missing result.

## ZfsHoldTight

[`ZfsHoldTight.tla`](https://github.com/richlander/zfs-hold-tight/blob/425e111a4a3b11f9eeeb9410af1edd1acb196093/docs/model/ZfsHoldTight.tla)
is accompanied by a
[configuration](https://github.com/richlander/zfs-hold-tight/blob/425e111a4a3b11f9eeeb9410af1edd1acb196093/docs/model/ZfsHoldTight.cfg)
and recorded
[design and results](https://github.com/richlander/zfs-hold-tight/blob/425e111a4a3b11f9eeeb9410af1edd1acb196093/docs/DESIGN.md#status).

It models retention-policy changes against a persisted pruning watermark,
genuine feed gaps, and a pool-wide health latch. It demonstrates separating
current policy from historical authority, modeling exogenous events without
accidentally freezing them, refining over-broad properties from TLC
counterexamples, and pairing model claims with implementation regression
tests. Calendar details and keeper expiry are deliberately outside its model
scope.
