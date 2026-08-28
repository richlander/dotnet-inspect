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

`callerCancellationViolation` is a transition monitor. It records whether a
step that moves one waiting caller to `CallerCancelled` also changes shared
protocol state or another caller. `CallerCancellationIsLocal` requires that
monitor to remain false.

## Implementation correspondence

This mapping is traceability, not a refinement proof. It identifies where the
current implementation realizes a modeled concern and which Release gate
checks the observable result. Exact runtime traces, scheduler steps, and formal
equivalence between the TLA+ state machine and C# remain unverified.

| Modeled concern | Current implementation | Release evidence | Correspondence |
| --- | --- | --- | --- |
| One process-local acquisition and joined outcome | `AsyncCache.GetOrAddAsync` publishes one `Lazy<Task<T>>`; `PackageExtractor` keys `s_packageRequests` by the normalized acquisition request. | `GetOrAddAsync_ConcurrentRequestsShareOneTask`; `ExtractPackageAsync_ConcurrentRequestsShareOneDownload`; `ExtractPackageAsync_ConcurrentFailedAttemptSettlesWaitersAndCanRetry` | Observable success and shared non-success are gated. |
| Non-success eviction and retry | `AsyncCache.ResolveAsync` removes faulted, cancelled, and predicate-rejected entries. `PackageExtractor` uses `shouldCache: false`, making the filesystem entry authoritative for every later request. | `GetOrAddAsync_FaultedResolutionCanRetry`; `GetOrAddAsync_CancelledResolutionCanRetry`; `GetOrAddAsync_RejectedResultCanRetry`; `ExtractPackageAsync_ConcurrentFailedAttemptSettlesWaitersAndCanRetry` | Observable retry and retained waiter outcome are gated. The exact remove-before-outer-task-completion interval is established by source ordering but has no deterministic implementation gate. |
| Caller-only cancellation | The package extraction API has no caller cancellation token. A consumer may abandon or independently wrap its own wait, but that is outside the package acquisition protocol. | None | Model environment action, not an implementation-correspondence claim. |
| Probe, existence check, validity recheck, and invalid-slot preservation | `NuGetCache.CommitPackage` rechecks a present target before rejecting it and never deletes an invalid final slot. | `CommitPackage_PreservesExistingInvalidEntry`; `CommitPackage_InvalidPackageLeavesNoVisibleEntry` | Observable invalid-slot behavior is gated. |
| Staged validation, marker write, and atomic publication | `NuGetCache.CommitPackage` builds and validates a unique sibling staging tree, writes its marker, and calls `Directory.Move` to the final path. | `CommitPackage_InvalidPackageLeavesNoVisibleEntry`; `CommitPackage_ConcurrentPublishersConvergeOnOneCompleteTree` | Final outcomes are gated. Intermediate atomic visibility relies on the documented local-filesystem rename premise rather than a deterministic C# trace gate. |
| Losing publisher convergence | The `Directory.Move` `IOException` filter accepts only a winner that passes `IsCommittedPackageValid`; otherwise the failure remains visible. | `CommitPackage_ConcurrentPublishersConvergeOnOneCompleteTree`; `CommitPackage_PreservesExistingInvalidEntry` | Valid-winner convergence and rejection of an already-present invalid slot are gated. The exact raced-invalid-winner path through `PackageExtractor` is established by source conditions but has no end-to-end gate. |
| Reader admission of committed content | `FileSystemPackageStore` surfaces marked final slots and `PackageContentAdmission` revalidates the retained archive against the extracted tree before use. | `CacheHitWithArchive_RejectsMutatedExtractedDll`; `ProductOwned_DeletedNupkg_DoesNotAdmitMutatedTree` | Admitted cache hits are gated; marker presence alone is not treated as complete package admission. |
| Process crash and unrelated rename failure | These are nondeterministic environment actions used to explore protocol consequences under the model's filesystem assumptions. | None | Model-only exploration; no crash-durability or arbitrary-filesystem implementation claim. |

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
implementation. The correspondence table records selected observable gates; it
does not establish formal or complete model-to-implementation equivalence.

## Checked configurations

The full safety and quiet liveness configurations use two processes, three
callers per process, and at most two attempts per process. One caller per
process remains dormant until a non-successful shared attempt creates retry
demand. The first attempt is scripted to produce either failure or factory
cancellation, so both retry paths are explored exactly one level deep.

| Configuration | Purpose |
| --- | --- |
| `PackageCachePublicationSafety.cfg` | Explores absent and already-invalid target slots, caller cancellation, shared failure and cancellation, rename failure, process crash, retry, and competing publishers. Checks type safety, atomic final-path visibility, target ownership, at most one winning publisher, one registry-selected acquisition per process, joined-outcome consistency, local caller cancellation, and complete-target return. |
| `PackageCachePublicationLiveness.cfg` | Scripts the first attempt in each process to fail or receive factory cancellation; disables other injected failure, cancellation, crash, and unrelated rename failure; and checks retry success, waiter completion, and exact losing-attempt success under weak fairness. |
| `PackageCachePublicationAdversarialLiveness.cfg` | Uses two callers per process and enables injected failure, factory and caller cancellation, crash, rename failure, and an initially invalid target. Checks waiter settlement and exact losing-attempt convergence under weak fairness while leaving disruptive environment actions unfair. |
| `PackageCachePublicationCompletionOverlap.cfg` | Reachability witness that negates the real remove-before-observable-completion window. It must find attempt 2 registered while attempt 1 is still completing. |
| `PackageCachePublicationBrokenAtomic.cfg` | Negative control that replaces atomic rename with direct final-path copy. It must violate `FinalPathIsAtomic`. |
| `PackageCachePublicationBrokenCallerCancellation.cfg` | Negative control that propagates one caller's cancellation into its shared task and peer waiters. It must violate `CallerCancellationIsLocal`. |
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
outcome, caller-only cancellation preserving shared protocol state and peer
waiters, successful callers requiring a complete target, eventual removal and
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
  -config PackageCachePublicationBrokenCallerCancellation.cfg \
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

The caller-cancellation counterexample starts two waiters on one active task,
then lets one caller's cancellation resolve that shared task as cancelled and
move its peer to `CallerCancelled`.

The completion-overlap witness finds attempt 2 registered while attempt 1 is
still in `Completing`, proving the model preserves old waiter identity across
the removal/completion window.

The witness and mutation configurations were run with one worker and produced
their named violations. Their state counts are intentionally not recorded:
they stop at the first counterexample, so the concrete trace and violated
property are the stable evidence.

These are negative controls, not product defects. No unexpected counterexample
was found in the checked current-protocol configurations.
