# TLA+ methodology

TLA+ is used per
[`AGENTS.md`](../AGENTS.md#keep-specifications-readable-model-interactions) to
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
and its [configuration](models/artifact-session-admission/ArtifactSessionAdmission.cfg)
model `ArtifactSetSession` admission for
[Artifact acquisition and workspace composition](design/artifact-acquisition-and-workspaces.md#artifactsetsession).

The model demonstrates single-flight admission, incompatible-generation
exclusion, voluntary and disposal-forced draining, late-result suppression,
guard witnesses, and weak-fairness progress.

## AssemblyContextGroupLifecycle

[`AssemblyContextGroupLifecycle.tla`](models/assembly-context-group-lifecycle/AssemblyContextGroupLifecycle.tla)
is accompanied by safety, liveness, and mutation configurations and a model
[`README.md`](models/assembly-context-group-lifecycle/README.md).

It models the existing `AssemblyContextGroup` callback, image-budget,
result-publication, finalization, disposal, and quiescent-release lifecycle for
[Inspection space](inspection-space.md). It demonstrates same-participant
contention, ordinary versus one-shot release, exceptional retry, resource
ordering, and independent counterexample mutations.

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
