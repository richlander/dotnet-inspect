# Package cache publication model

This TLA+ model is the executable interaction companion to
[Cache concurrency and publication](../../cache-concurrency.md).
It explores one exact package coordinate across process-local single-flight
registries and independent cross-process publishers.

The model exists to answer interaction questions that are difficult to settle
from prose:

- Can callers that join one task observe different shared outcomes?
- Can a reader or competing writer observe a partial final directory?
- Does a publisher that loses the rename race converge on the valid winner?
- Can a caller-only cancellation affect the shared task or another waiter?
- Can a failed shared task prevent a later request from retrying?
- What happens when a process crashes before or after the winning rename?

## Relationship to the implementation

The model abstracts these current paths:

- `AsyncCache<TKey, TValue>` and the process-wide registry in
  `PackageExtractor`;
- package publication in `NuGetCache.CommitPackage`; and
- package-cache lookup through `NuGetCache.EnumerateCachedPackageContent`.

It does not describe every rename-based cache in the repository. In particular,
it makes no claim about `NuGetFetch.PackageCache`, which uses a different
protocol.

The model separates the initial validity probe, existence check, validity
recheck, staging steps, rename, and loser validation. That separation lets TLC
schedule another process between observations instead of encoding the desired
outcome as one indivisible protocol action.

The `Resolving` phase represents the value factory having settled while the
outer `AsyncCache.ResolveAsync` task remains pending and registered.
`EvictSettledEntry` removes that attempt from the registry, and
`PublishOutcome` later makes the outer task's outcome observable to its joined
callers. A replacement attempt can start between those actions while old
waiters continue to reference their original task.

## Assumptions and non-claims

The checked model assumes:

- one already-derived exact acquisition key; source-policy and key derivation
  are outside the model;
- a unique staging sibling per publisher on the same local filesystem as the
  final path;
- normal local-filesystem, non-overwriting directory-rename semantics;
- a validated and marked staging tree is non-empty;
- no concurrent explicit cache clear and no mutation after publication;
- `Complete` abstracts the combined structure, marker, retained-archive, and
  package-admission checks; and
- process crashes may abandon staging, while power-loss durability and storage
  corruption are outside the model.

HTTP transport, ZIP extraction, byte-level persistence, package version
resolution, dependency traversal, and equivalence between the model and the
implementation are non-claims. TLC results establish properties of this model
under these assumptions and bounds, not properties of the shipped
implementation. Model-to-implementation correspondence is currently
unverified.

## Checked configurations

The full safety and quiet liveness configurations use two processes, three
callers per process, and at most two attempts per process. One caller per
process remains dormant until a non-successful shared attempt creates retry
demand. The first attempt is scripted to produce either failure or factory
cancellation, so both retry paths are explored exactly one level deep.

| Configuration | Purpose |
| --- | --- |
| `PackageCachePublicationSafety.cfg` | Explores absent and already-invalid target slots, caller cancellation, shared failure and cancellation, rename failure, process crash, retry, and competing publishers. Checks type safety, atomic final-path visibility, target ownership, at most one winning publisher, one registry-selected acquisition per process, joined-outcome consistency, and complete-target return. |
| `PackageCachePublicationLiveness.cfg` | Scripts the first attempt in each process to fail or receive factory cancellation; disables other injected failure, cancellation, crash, and unrelated rename failure; and checks retry success, waiter completion, and exact losing-attempt success under weak fairness. |
| `PackageCachePublicationAdversarialLiveness.cfg` | Uses two callers per process and enables injected failure, factory and caller cancellation, crash, rename failure, and an initially invalid target. Checks waiter settlement and exact losing-attempt convergence under weak fairness while leaving disruptive environment actions unfair. |
| `PackageCachePublicationCompletionOverlap.cfg` | Reachability witness that negates the real remove-before-observable-completion window. It must find attempt 2 registered while attempt 1 is still completing. |
| `PackageCachePublicationBrokenAtomic.cfg` | Negative control that replaces atomic rename with direct final-path copy. It must violate `FinalPathIsAtomic`. |
| `PackageCachePublicationBrokenEviction.cfg` | Negative control that retains failed registry entries. It must violate `NonSuccessAttemptEventuallySucceeds`. |

`BoundedOut` is a model-checking terminal state for a request that arrives after
the configured attempt bound is exhausted. It does not represent product
behavior, is reachable only from `Idle`, and cannot discharge either waiter
completion or retry success.

### Filesystem assumptions and protocol consequences

`AtomicRename` encodes the same-volume, non-overwriting, atomic directory-rename
premise. `FinalPathIsAtomic`, `TargetOwnerIsConsistent`, and
`AtMostOnePublisher` validate the model's use of that premise; the direct-copy
negative control demonstrates why it is load-bearing.

The remaining safety invariants and temporal properties are protocol
consequences checked across the encoded interleavings: one registry-selected
acquisition per process, joined callers observing their own task's shared
outcome, successful callers requiring a complete target, eventual removal and
retry after a shared non-success, waiter settlement, and exact-attempt loser
convergence. TLC action coverage was nonzero for task joining, the
factory-settled/removal window, caller and factory cancellation, injected
failure, crash, atomic rename, unrelated rename failure, and loser convergence.

## Running TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md) for the pinned
toolchain. The recorded run used Eclipse Temurin OpenJDK 25.0.4.1 and TLA+
tools 1.8.0 (`TLC2 2026.08.21.155922`, revision `9787e65`). The checked
`tla2tools.jar` has SHA-256
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.

The recorded runs added `-coverage 1` to capture action coverage; omit it for a
quieter and faster routine check. Run these commands sequentially because
concurrent TLC processes using `-cleanup` can remove one another's metadata.

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/package-cache-publication
java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup \
  -config PackageCachePublicationSafety.cfg \
  PackageCachePublication.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup \
  -config PackageCachePublicationLiveness.cfg \
  PackageCachePublication.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup \
  -config PackageCachePublicationAdversarialLiveness.cfg \
  PackageCachePublication.tla
```

The negative controls are expected to exit unsuccessfully:

```bash
java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config PackageCachePublicationBrokenAtomic.cfg \
  PackageCachePublication.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config PackageCachePublicationBrokenEviction.cfg \
  PackageCachePublication.tla
```

The completion-overlap reachability witness is also expected to exit
unsuccessfully because its invariant denies a state the model must reach:

```bash
java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config PackageCachePublicationCompletionOverlap.cfg \
  PackageCachePublication.tla
```

## Recorded result

| Configuration | Generated states | Distinct states | Maximum depth | Result |
| --- | ---: | ---: | ---: | --- |
| `PackageCachePublicationSafety.cfg` | 709,513,146 | 55,523,669 | 35 | No error |
| `PackageCachePublicationLiveness.cfg` | 1,291,289 | 319,168 | 33 | No error |
| `PackageCachePublicationAdversarialLiveness.cfg` | 1,848,013 | 257,277 | 31 | No error |

The atomic-publication counterexample reaches a marked staging tree and then
exposes the final path as `Partial` before direct copying completes. The
failed-eviction counterexample retains attempt 1 in each process-local registry;
retry callers join that completed failed task instead of allocating attempt 2,
so no caller can reach a successful retry.

The completion-overlap witness finds attempt 2 registered while attempt 1 is
still in `Completing`, proving the model preserves old waiter identity across
the removal/completion window.

The witness and mutation configurations were run with one worker and produced
their named violations. Their state counts are intentionally not recorded:
they stop at the first counterexample, so the concrete trace and violated
property are the stable evidence.

These are negative controls, not product defects. No unexpected counterexample
was found in the checked current-protocol configurations.
