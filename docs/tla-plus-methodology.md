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

A TLA+ model that exists only as uncommitted files in a local worktree is not
a checked-in asset: it is not backed up, reviewable, or visible to other
contributors and agents. Commit a model to its branch and push that branch as
soon as it reaches a checkable state: its module parses and TLC runs against
its `.cfg` without unexpected errors. Do not wait for the owning design PR to
be otherwise complete. Treat an uncommitted
`.tla`/`.cfg`/model-`README.md` set as a bug to fix, not a stable resting
state.

The per-PR TLA+ gate checks each model directory whose `.tla` or `.cfg` files
change in that candidate. Gate-infrastructure changes run structural gate
tests but do not check unchanged model content. The gate does not sweep
unrelated committed models.
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
