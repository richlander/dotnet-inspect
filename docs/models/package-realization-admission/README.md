# Package realization exact-request admission model

`PackageRealizationAdmission.tla` models the **target design** for
exact-request, single-flight admission and lease-scoped lifetime around
`InspectionWorkspace.RealizePackageAssemblyContextRoles`. It does not claim
that this is shipped behavior. Admission implementation is tracked by #4960;
request identity and its implementation prerequisites are tracked by #5118.

The owning design is
[`docs/design/inspection-layers.md`](../../design/inspection-layers.md)'s
"Package-realization exact-request admission" section. The adjacent
"Package-role planning and cleanup boundary" owns internal realization,
binding, limits, group quiescence, and cleanup. This model owns only request
identity, whether that whole operation starts, atomic result publication, and
how the cache retains and releases its result.

## Scope

The harness contains three normalized package-coordinate atoms, four opaque
content-generation atoms, two exact realization-options values, and three
demands. The main bound uses:

- two demands submit the same ordered two-coordinate request and exact options;
- one submits an overlapping but non-identical request.

Focused scenario bounds reuse the third demand with unequal options, unequal
content generation, a reordered coordinate sequence, a duplicate coordinate,
an empty selected sequence, or the same exact request for cancellation reuse.

Each coordinate atom abstracts one complete
`RealizedMemberCoordinate.Package`, including its producer, and is paired with
an acquisition-owned immutable content-generation identity. Producer identity
alone is insufficient because one source can replace content under the same
nominal coordinate. A request sequence contains only selected package roots
with a non-empty surface role. Root-only roots are omitted before admission.
The exact cache identity is the ordered sequence of coordinate/generation
bindings plus exact options equality.

Order is intentionally significant. The compatibility API exposes ordered
participant collections and the groups expose participant order, so silently
sorting a request would change observable behavior. An adopting successor may
define a different order contract in a separate effort; this model preserves
the current one.

The module retains `Coordinates` as an internal symbol so the previously
checked lifetime state machine remains mechanically recognizable, but each
element now denotes a complete exact request identity rather than one package
coordinate.

Each request identity has an independent cache state:

- `Absent`, `InFlight`, and `Ready` govern open-workspace admission;
- `Draining` receives an in-flight operation when disposal closes admission;
  and
- `Closing`, `Releasing`, and `Released` govern terminal cleanup of a realized
  entry.

An eligible demand may admit an absent exact request, join an in-flight exact
request, reuse a ready exact result through an independent lease, detach
through caller cancellation, return its lease, fail, or be rejected after
disposal. A duplicate normalized coordinate is rejected before lookup. An
empty selected-coordinate sequence bypasses admission without a cache entry,
lease, or cleanup request.

Overlapping requests, reordered requests, unequal content generations, and
requests with unequal options never join or reuse one another. They may run
concurrently even when they contain some of the same package coordinates
because each operation constructs and validates its own combined binding
topology. There is no partial per-coordinate reuse.

A success publishes the whole combined realization to every still-attached
demand in one transition. The model assigns an identity to the physical
operation independently from its callers. Caller cancellation records the
operation that demand left but does not abandon it; even after every attached
demand cancels, the same operation can complete and a later exact demand can
reuse its result. A failure explicitly settles the operation and clears the
request entry so a later exact demand may retry.

Active lease accounting is derived from demands in `Leased`; it is not a
separately mutable counter. A ready realization remains cached with zero leases
until workspace disposal. Disposal closes every request atomically. A late
successful admission owns a real combined realization but transfers it
directly to `Closing` without publication or lease issuance. Cleanup starts
only after all leases return, starts at most once, and records either successful
release or visible cleanup failure before the cache becomes terminal.

## Fairness boundary

Weak fairness covers front-door resolution, admission resolution, lease
return, in-flight completion, cleanup start, and cleanup completion. Caller
cancellation is optional rather than fairness-forced.
`EveryIssuedLeaseEventuallyReturns` assumes that every lease holder eventually
returns its lease. The model cannot prove that assumption about callers.

The model also cannot prove that implementation waiting is non-blocking.
`PackageRealizationAsyncDisposal_NeverBlocksSingleThreadedHost` is the separate
implementation gate required to show that Browser/Wasm can schedule lease
return and cleanup through awaited continuations rather than a blocking wait.

## Non-claims

The model does not cover:

- package-coordinate construction, normalization, content-generation
  construction, or their correspondence to a `PackageRootRealization`;
- package selection, role planning, binding, group construction, aggregate
  budget arithmetic, group quiescence, or the internal cleanup algorithm;
- construction of a shareable package-role completion or demand-local
  participant projection;
- assembly or PE content identity;
- eviction, time-to-live, memory-pressure release, or cross-workspace
  persistence;
- exception payloads or output presentation;
- the existence of a retained multi-call production caller; or
- implementation conformance.

## Checked properties

| Property | Claim |
| --- | --- |
| `TypeOK` | Every variable remains in its declared state domain. |
| `SingleFlightPerRequest` | At most one demand leads an admission for each exact request identity. |
| `ConsistentLeaseOutcomeHistory` | Every demand ever issued a lease for one exact request records the same realization identity, including after return. |
| `CacheStateConsistent` | Cache state, realization identity, leader, cleanup state, and demand lease history remain mutually consistent. |
| `ExactRequestReuse` | A reusable result comes only from the demand's exact ordered coordinate/generation bindings and exact options. |
| `WholeRequestPublication` | A ready entry cannot coexist with an unresolved demand attached to that operation. |
| `CancellationCannotAbandonOperation` | Every operation left by a canceled caller remains active or reaches an explicit success/failure completion. |
| `NoLeaseAfterAdmissionCloses` | Lease-issuing transitions observe an open workspace and an admissible cache state. |
| `NoPublicationAfterDisposal` | Successful publication observes an open workspace and an in-flight cache entry. |
| `ReleaseStartsOnlyAfterLeasesReturn` | Cleanup observes a closing entry with no active leases and no prior cleanup start. |
| `CleanupStartsAtMostOnce` | An exact request starts package-role cleanup at most once. |
| `DisposedCacheCannotReopen` | A disposed workspace never returns a request to in-flight or ready. |
| `ReleasedRealizationsHaveNoActiveLeases` | Releasing and released realizations have no active demand leases. |
| `EveryDemandEventuallyResolves` | Under weak fairness, each pending demand bypasses, receives a lease, cancels, or receives a visible failed/rejected result. |
| `EveryIssuedLeaseEventuallyReturns` | Under the explicit caller-return assumption, every issued lease is eventually returned. |
| `EveryDisposedRealizationEventuallyReleases` | Under weak fairness and lease return, every disposed realized entry reaches terminal cleanup. |
| `EveryDrainingAdmissionEventuallySettles` | Under weak fairness, every operation draining through disposal reaches failure or cleanup. |

The lease, publication, and cleanup-order claims use monotonic witness
variables. Deliberate mutations weaken their actions and falsify those
witnesses. The inexact-reuse mutation returns a realization from another
request identity, and the partial-publication mutation makes a cache entry
ready while one attached demand remains unresolved; these checks observe
incorrect state rather than restating normal guards. Cleanup uses a start
counter so a second start is observable. Double return is also an explicit
transition, proving idempotence is not hidden as stutter.

## Configurations

| Configuration | Purpose |
| --- | --- |
| `PackageRealizationAdmission.cfg` | Checks all safety and liveness properties over the main exact-request bound. |
| `ReachabilityJoin.cfg` | Proves that a matching in-flight demand can be joined. |
| `ReachabilityRetryAfterFailure.cfg` | Proves that failure returns an exact request to retryable admission. |
| `ReachabilityMultiDemandConsistency.cfg` | Proves that multiple attached demands can share one successful realization. |
| `ReachabilityOverlappingRequests.cfg` | Proves that overlapping but non-identical requests can remain independently in flight. |
| `ReachabilityOptionIsolation.cfg` | Proves that unequal options isolate otherwise equal coordinate sequences. |
| `ReachabilityContentGenerationIsolation.cfg` | Proves that unequal content generations isolate otherwise equal coordinates and options. |
| `ReachabilityReorderedRequestIsolation.cfg` | Proves that reordered coordinate sequences use separate entries. |
| `ReachabilityDuplicateRejection.cfg` | Proves duplicate normalized coordinates reject before admission. |
| `ReachabilityRootOnlyBypass.cfg` | Proves an empty selected sequence bypasses admission. |
| `ReachabilityDetachedCancellation.cfg` | Proves caller cancellation can detach while shared work remains live or ready. |
| `ReachabilityCanceledOperationReuse.cfg` | Proves all attached callers can cancel before the same operation completes and is later reused. |
| `ReachabilityZeroLeaseRetention.cfg` | Proves that a zero-lease ready entry remains retained and can be reused. |
| `ReachabilityDisposalWait.cfg` | Proves that disposal can wait for an active lease and later begin cleanup. |
| `ReachabilityDrainedSuccess.cfg` | Proves that a late success after disposal transfers directly to closing. |
| `ReachabilityDoubleReturn.cfg` | Proves that a second return is observable but accounting-neutral. |
| `BrokenInexactReuse.cfg` | Reuses a result from another request identity. |
| `BrokenPartialPublish.cfg` | Publishes ready while an attached demand remains unresolved. |
| `BrokenCancellationAbandonsOperation.cfg` | Removes a physical operation on final-caller cancellation without completing it. |
| `BrokenLeaseAfterClose.cfg` | Issues a lease from a closing entry after disposal. |
| `BrokenReleaseWithActiveLease.cfg` | Starts cleanup while a demand still holds a lease. |
| `BrokenLatePublish.cfg` | Publishes a draining operation after disposal. |
| `BrokenDoubleCleanup.cfg` | Starts cleanup more than once for one request. |
| `BrokenResurrection.cfg` | Returns a disposed closing or terminal entry to ready. |

Reachability configurations check the negation of a witness or reachable state
and are expected to fail on their named `No...Observed` invariant. Their
counterexamples prove that the intended paths are reachable. Broken
configurations are also expected to fail, but on the safety property named in
the table. A successful run of either kind would mean its probe no longer
demonstrates the intended behavior.

## Running TLC

Use the repository-pinned TLA+ `v1.8.0` `tla2tools.jar` and run configurations
sequentially unless each process receives a distinct `-metadir`:

```bash
cd docs/models/package-realization-admission
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup -workers auto -config PackageRealizationAdmission.cfg \
  PackageRealizationAdmission_MC.tla

for config in ReachabilityJoin ReachabilityRetryAfterFailure \
  ReachabilityMultiDemandConsistency ReachabilityOverlappingRequests \
  ReachabilityOptionIsolation ReachabilityContentGenerationIsolation \
  ReachabilityReorderedRequestIsolation ReachabilityDuplicateRejection \
  ReachabilityRootOnlyBypass ReachabilityDetachedCancellation \
  ReachabilityCanceledOperationReuse ReachabilityZeroLeaseRetention \
  ReachabilityDisposalWait ReachabilityDrainedSuccess \
  ReachabilityDoubleReturn BrokenInexactReuse BrokenPartialPublish \
  BrokenCancellationAbandonsOperation BrokenLeaseAfterClose \
  BrokenReleaseWithActiveLease BrokenLatePublish BrokenDoubleCleanup \
  BrokenResurrection; do
  java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
    -cleanup -noGenerateSpecTE -config "$config.cfg" \
    PackageRealizationAdmission_MC.tla
done
```

## TLC evidence

Checked on Linux with Eclipse Temurin/OpenJDK `25.0.4.1` and the
repository-pinned TLA+ `v1.8.0` prerelease (`TLC2 2026.08.21.155922`, rev
`9787e65`). The checked `tla2tools.jar` has SHA-256
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.

| Configuration | Result | Generated states | Distinct states | Depth/state |
| --- | --- | ---: | ---: | ---: |
| `PackageRealizationAdmission.cfg` | No error | 110,517 | 43,804 | 17 |
| `ReachabilityJoin.cfg` | `NoJoinObserved` violated | 10 | 10 | 3 |
| `ReachabilityRetryAfterFailure.cfg` | `NoRetryAfterFailureObserved` violated | 95 | 72 | 4 |
| `ReachabilityMultiDemandConsistency.cfg` | `NoMultiDemandConsistencyObserved` violated | 65 | 52 | 4 |
| `ReachabilityOverlappingRequests.cfg` | `NoOverlappingRequestsObserved` violated | 12 | 12 | 3 |
| `ReachabilityOptionIsolation.cfg` | `NoOptionIsolationObserved` violated | 12 | 12 | 3 |
| `ReachabilityContentGenerationIsolation.cfg` | `NoContentGenerationIsolationObserved` violated | 12 | 12 | 3 |
| `ReachabilityReorderedRequestIsolation.cfg` | `NoReorderedRequestIsolationObserved` violated | 12 | 12 | 3 |
| `ReachabilityDuplicateRejection.cfg` | `NoDuplicateRejectionObserved` violated | 6 | 6 | 2 |
| `ReachabilityRootOnlyBypass.cfg` | `NoRootOnlyBypassObserved` violated | 6 | 6 | 2 |
| `ReachabilityDetachedCancellation.cfg` | `NoDetachedCancellationObserved` violated | 11 | 11 | 3 |
| `ReachabilityCanceledOperationReuse.cfg` | `NoCanceledOperationReuseObserved` violated | 1,781 | 949 | 7 |
| `ReachabilityZeroLeaseRetention.cfg` | `NoZeroLeaseRetentionObserved` violated | 272 | 178 | 5 |
| `ReachabilityDisposalWait.cfg` | `NoDisposalWaitObserved` violated | 1,292 | 706 | 6 |
| `ReachabilityDrainedSuccess.cfg` | `NoDrainedSuccessObserved` violated | 105 | 76 | 4 |
| `ReachabilityDoubleReturn.cfg` | `NoDoubleReturnObserved` violated | 398 | 246 | 5 |
| `BrokenInexactReuse.cfg` | `ExactRequestReuse` violated | 94 | 71 | 4 |
| `BrokenPartialPublish.cfg` | `WholeRequestPublication` violated | 66 | 53 | 4 |
| `BrokenCancellationAbandonsOperation.cfg` | `CancellationCannotAbandonOperation` violated | 10 | 10 | 3 |
| `BrokenLeaseAfterClose.cfg` | `NoLeaseAfterAdmissionCloses` violated | 410 | 253 | 5 |
| `BrokenReleaseWithActiveLease.cfg` | `ReleaseStartsOnlyAfterLeasesReturn` violated | 414 | 255 | 5 |
| `BrokenLatePublish.cfg` | `NoPublicationAfterDisposal` violated | 105 | 76 | 4 |
| `BrokenDoubleCleanup.cfg` | `CleanupStartsAtMostOnce` violated | 1,354 | 727 | 6 |
| `BrokenResurrection.cfg` | `DisposedCacheCannotReopen` violated | 414 | 255 | 5 |

The normal configuration explored its complete bounded state graph. Each
reachability and mutation configuration stopped at its first expected
counterexample. "Depth/state" is the complete graph depth for the normal run
and the final counterexample state number for expected-failure runs.
