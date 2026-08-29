# Package realization admission and lifetime model

`PackageRealizationAdmission.tla` models the **target design** for
coordinate-keyed, single-flight admission and lease-scoped lifetime in
`InspectionWorkspace.RealizePackageAssemblyContextRoles`. It does not claim
that this is shipped behavior. Admission implementation is tracked by #4960,
and the shared-realization lifetime contract is tracked by #5015.

The owning design is
[`docs/design/inspection-layers.md`](../../design/inspection-layers.md)'s
"Package-realization coordinate admission" section. The adjacent
"Package-role planning and cleanup boundary" owns the internal realization,
group-quiescence, and cleanup operation. This model owns only whether that
operation starts for a coordinate and how the cache retains and releases its
result.

## Scope

The model contains two package coordinates and four demands. Each coordinate
has an independent cache state:

- `Absent`, `InFlight`, and `Ready` govern open-workspace admission;
- `Draining` receives an in-flight operation when disposal closes admission;
  and
- `Closing`, `Releasing`, and `Released` govern terminal cleanup of a realized
  entry.

A demand may admit an absent coordinate, join an in-flight operation, reuse a
ready realization through an independent lease, return that lease, fail, or be
rejected after disposal. Active lease accounting is derived from demands in
`Leased`; it is not a separately mutable counter. A ready realization remains
cached with zero leases until workspace disposal.

Disposal closes every coordinate atomically. A late successful admission owns
a real realization but transfers it directly to `Closing` without publication
or lease issuance. Cleanup starts only after all leases return, starts at most
once, and records either successful release or visible cleanup failure before
the cache becomes terminal.

## Fairness boundary

Weak fairness covers admission resolution, lease return, in-flight completion,
cleanup start, and cleanup completion. In particular,
`EveryIssuedLeaseEventuallyReturns` assumes that every lease holder eventually
returns its lease. The model cannot prove this assumption about callers.

The model also cannot prove that implementation waiting is non-blocking.
`PackageRealizationAsyncDisposal_NeverBlocksSingleThreadedHost` is the separate
implementation gate required to show that Browser/Wasm can schedule lease
return and cleanup through awaited continuations rather than a blocking wait.

## Non-claims

The model does not cover:

- decomposition or rollback of one caller request spanning several package
  coordinates;
- construction or normalization of a realized package coordinate;
- package selection, package-role planning, group quiescence, or the internal
  cleanup algorithm;
- assembly or PE content identity;
- eviction, time-to-live, memory-pressure release, or cross-workspace
  persistence;
- exception payloads or output presentation; or
- implementation conformance.

## Checked properties

| Property | Claim |
| --- | --- |
| `TypeOK` | Every variable remains in its declared state domain. |
| `SingleFlightPerCoordinate` | At most one demand leads an admission for each coordinate. |
| `ConsistentLeaseOutcomeHistory` | Every demand ever issued a lease for one coordinate records the same realization identity, including after return. |
| `CacheStateConsistent` | Cache state, realization identity, leader, cleanup state, and demand lease history remain mutually consistent. |
| `NoLeaseAfterAdmissionCloses` | Lease-issuing transitions observe an open workspace and an admissible cache state. |
| `NoPublicationAfterDisposal` | Successful publication observes an open workspace and an in-flight cache entry. |
| `ReleaseStartsOnlyAfterLeasesReturn` | Cleanup observes a closing entry with no active leases and no prior cleanup start. |
| `CleanupStartsAtMostOnce` | A coordinate starts package-role cleanup at most once. |
| `DisposedCacheCannotReopen` | A disposed workspace never returns a coordinate to in-flight or ready. |
| `ReleasedRealizationsHaveNoActiveLeases` | Releasing and released realizations have no active demand leases. |
| `EveryDemandEventuallyResolves` | Under weak fairness, each pending demand receives a lease or a visible failed/rejected result. |
| `EveryIssuedLeaseEventuallyReturns` | Under the explicit caller-return assumption, every issued lease is eventually returned. |
| `EveryDisposedRealizationEventuallyReleases` | Under weak fairness and lease return, every disposed realized entry reaches terminal cleanup. |
| `EveryDrainingAdmissionEventuallySettles` | Under weak fairness, every operation draining through disposal reaches failure or cleanup. |

The lease, publication, and cleanup-order claims use monotonic witness
variables. Deliberate mutations weaken their action guards and falsify those
witnesses, so the checks do not merely restate the normal guards. Cleanup uses
a start counter rather than a Boolean latch so a second start is observable.
Double return is also an explicit transition, proving idempotence is not hidden
as an unobservable stutter.

## Configurations

| Configuration | Purpose |
| --- | --- |
| `PackageRealizationAdmission.cfg` | Checks all safety and liveness properties over two coordinates and four demands. |
| `ReachabilityJoin.cfg` | Proves that an in-flight demand can be joined. |
| `ReachabilityRetryAfterFailure.cfg` | Proves that failure returns a coordinate to retryable admission. |
| `ReachabilityMultiDemandConsistency.cfg` | Proves that multiple attached demands can share one successful realization. |
| `ReachabilityZeroLeaseRetention.cfg` | Proves that a zero-lease ready entry remains retained and can be reused. |
| `ReachabilityDisposalWait.cfg` | Proves that disposal can wait for an active lease and later begin cleanup. |
| `ReachabilityDrainedSuccess.cfg` | Proves that a late success after disposal transfers directly to closing. |
| `ReachabilityDoubleReturn.cfg` | Proves that a second return is observable but accounting-neutral. |
| `BrokenLeaseAfterClose.cfg` | Issues a lease from a closing entry after disposal. |
| `BrokenReleaseWithActiveLease.cfg` | Starts cleanup while a demand still holds a lease. |
| `BrokenLatePublish.cfg` | Publishes a draining operation after disposal. |
| `BrokenDoubleCleanup.cfg` | Starts cleanup more than once for one coordinate. |
| `BrokenResurrection.cfg` | Returns a disposed closing or terminal entry to ready. |

The reachability configurations check the negation of a monotonic latch and
are expected to fail on their named `No...Observed` invariant. Their
counterexamples prove that the intended paths are reachable. The broken
configurations are also expected to fail, but on the safety property named in
the evidence table. A successful run of either kind would mean its probe no
longer demonstrates the intended behavior.

## Running TLC

Use the repository-pinned TLA+ `v1.8.0` `tla2tools.jar` and run configurations
sequentially unless each process receives a distinct `-metadir`:

```bash
cd docs/models/package-realization-admission
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup -workers auto -config PackageRealizationAdmission.cfg \
  PackageRealizationAdmission_MC.tla

for config in ReachabilityJoin ReachabilityRetryAfterFailure \
  ReachabilityMultiDemandConsistency ReachabilityZeroLeaseRetention \
  ReachabilityDisposalWait ReachabilityDrainedSuccess \
  ReachabilityDoubleReturn BrokenLeaseAfterClose \
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

| Configuration | Result | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: |
| `PackageRealizationAdmission.cfg` | No error | 317,669 | 99,535 | 20 |
| `ReachabilityJoin.cfg` | `NoJoinObserved` violated | 8 | 8 | 3 |
| `ReachabilityRetryAfterFailure.cfg` | `NoRetryAfterFailureObserved` violated | 89 | 74 | 4 |
| `ReachabilityMultiDemandConsistency.cfg` | `NoMultiDemandConsistencyObserved` violated | 96 | 75 | 4 |
| `ReachabilityZeroLeaseRetention.cfg` | `NoZeroLeaseRetentionObserved` violated | 362 | 246 | 5 |
| `ReachabilityDisposalWait.cfg` | `NoDisposalWaitObserved` violated | 907 | 582 | 7 |
| `ReachabilityDrainedSuccess.cfg` | `NoDrainedSuccessObserved` violated | 108 | 85 | 4 |
| `ReachabilityDoubleReturn.cfg` | `NoDoubleReturnObserved` violated | 267 | 188 | 6 |
| `BrokenLeaseAfterClose.cfg` | `NoLeaseAfterAdmissionCloses` violated | 272 | 191 | 5 |
| `BrokenReleaseWithActiveLease.cfg` | `ReleaseStartsOnlyAfterLeasesReturn` violated | 335 | 233 | 6 |
| `BrokenLatePublish.cfg` | `NoPublicationAfterDisposal` violated | 108 | 90 | 5 |
| `BrokenDoubleCleanup.cfg` | `CleanupStartsAtMostOnce` violated | 1,164 | 695 | 6 |
| `BrokenResurrection.cfg` | `DisposedCacheCannotReopen` violated | 351 | 242 | 5 |

The normal configuration explored its complete bounded state graph. Each
reachability and mutation configuration stopped at its first expected
counterexample.
