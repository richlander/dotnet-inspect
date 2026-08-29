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

The main bound contains three normalized package-coordinate atoms, two exact
realization-options values, and three demands:

- two demands submit the same ordered two-coordinate request and exact options;
- one submits an overlapping but non-identical request.

Focused scenario bounds reuse the third demand with unequal options, a
reordered coordinate sequence, a duplicate coordinate, or an empty selected
sequence.

Each coordinate atom abstracts one complete
`RealizedMemberCoordinate.Package`, including its producer. A request sequence
contains only selected package roots with a non-empty surface role. Root-only
roots are omitted before admission. The exact cache identity is the ordered
selected-coordinate sequence plus exact options equality.

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

Overlapping requests, reordered requests, and requests with unequal options
never join or reuse one another. They may run concurrently even when they
contain some of the same package coordinates because each operation constructs
and validates its own combined binding topology. There is no partial
per-coordinate reuse.

A success publishes the whole combined realization to every still-attached
demand in one transition. Caller cancellation removes only that demand; it
does not cancel the workspace-owned operation for peers or future reuse. A
failure clears the request entry so a later exact demand may retry.

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

- package-coordinate construction, normalization, or its correspondence to a
  `PackageRootRealization`;
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
| `ExactRequestReuse` | A reusable result comes only from the demand's exact ordered coordinates and exact options. |
| `WholeRequestPublication` | A ready entry cannot coexist with an unresolved demand attached to that operation. |
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
| `ReachabilityReorderedRequestIsolation.cfg` | Proves that reordered coordinate sequences use separate entries. |
| `ReachabilityDuplicateRejection.cfg` | Proves duplicate normalized coordinates reject before admission. |
| `ReachabilityRootOnlyBypass.cfg` | Proves an empty selected sequence bypasses admission. |
| `ReachabilityDetachedCancellation.cfg` | Proves caller cancellation can detach while shared work remains live or ready. |
| `ReachabilityZeroLeaseRetention.cfg` | Proves that a zero-lease ready entry remains retained and can be reused. |
| `ReachabilityDisposalWait.cfg` | Proves that disposal can wait for an active lease and later begin cleanup. |
| `ReachabilityDrainedSuccess.cfg` | Proves that a late success after disposal transfers directly to closing. |
| `ReachabilityDoubleReturn.cfg` | Proves that a second return is observable but accounting-neutral. |
| `BrokenInexactReuse.cfg` | Reuses a result from another request identity. |
| `BrokenPartialPublish.cfg` | Publishes ready while an attached demand remains unresolved. |
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
  ReachabilityOptionIsolation ReachabilityReorderedRequestIsolation \
  ReachabilityDuplicateRejection ReachabilityRootOnlyBypass \
  ReachabilityDetachedCancellation ReachabilityZeroLeaseRetention \
  ReachabilityDisposalWait ReachabilityDrainedSuccess \
  ReachabilityDoubleReturn BrokenInexactReuse BrokenPartialPublish \
  BrokenLeaseAfterClose BrokenReleaseWithActiveLease BrokenLatePublish \
  BrokenDoubleCleanup BrokenResurrection; do
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
| `PackageRealizationAdmission.cfg` | No error | 66,575 | 24,859 | 17 |
| `ReachabilityJoin.cfg` | `NoJoinObserved` violated | 10 | 10 | 3 |
| `ReachabilityRetryAfterFailure.cfg` | `NoRetryAfterFailureObserved` violated | 95 | 69 | 4 |
| `ReachabilityMultiDemandConsistency.cfg` | `NoMultiDemandConsistencyObserved` violated | 65 | 49 | 4 |
| `ReachabilityOverlappingRequests.cfg` | `NoOverlappingRequestsObserved` violated | 12 | 12 | 3 |
| `ReachabilityOptionIsolation.cfg` | `NoOptionIsolationObserved` violated | 12 | 12 | 3 |
| `ReachabilityReorderedRequestIsolation.cfg` | `NoReorderedRequestIsolationObserved` violated | 12 | 12 | 3 |
| `ReachabilityDuplicateRejection.cfg` | `NoDuplicateRejectionObserved` violated | 6 | 6 | 2 |
| `ReachabilityRootOnlyBypass.cfg` | `NoRootOnlyBypassObserved` violated | 6 | 6 | 2 |
| `ReachabilityDetachedCancellation.cfg` | `NoDetachedCancellationObserved` violated | 9 | 9 | 3 |
| `ReachabilityZeroLeaseRetention.cfg` | `NoZeroLeaseRetentionObserved` violated | 254 | 152 | 5 |
| `ReachabilityDisposalWait.cfg` | `NoDisposalWaitObserved` violated | 1,106 | 575 | 6 |
| `ReachabilityDrainedSuccess.cfg` | `NoDrainedSuccessObserved` violated | 105 | 73 | 4 |
| `ReachabilityDoubleReturn.cfg` | `NoDoubleReturnObserved` violated | 375 | 218 | 5 |
| `BrokenInexactReuse.cfg` | `ExactRequestReuse` violated | 94 | 68 | 4 |
| `BrokenPartialPublish.cfg` | `WholeRequestPublication` violated | 66 | 50 | 4 |
| `BrokenLeaseAfterClose.cfg` | `NoLeaseAfterAdmissionCloses` violated | 387 | 225 | 5 |
| `BrokenReleaseWithActiveLease.cfg` | `ReleaseStartsOnlyAfterLeasesReturn` violated | 391 | 227 | 5 |
| `BrokenLatePublish.cfg` | `NoPublicationAfterDisposal` violated | 105 | 73 | 4 |
| `BrokenDoubleCleanup.cfg` | `CleanupStartsAtMostOnce` violated | 1,161 | 589 | 6 |
| `BrokenResurrection.cfg` | `DisposedCacheCannotReopen` violated | 391 | 227 | 5 |

The normal configuration explored its complete bounded state graph. Each
reachability and mutation configuration stopped at its first expected
counterexample. "Depth/state" is the complete graph depth for the normal run
and the final counterexample state number for expected-failure runs.
