# PackageRealizationAdmission

Models the **target design** for admission into
`InspectionWorkspace.RealizePackageAssemblyContextRoles`: a coordinate-keyed,
single-flight admission cache over package-realization demands. It does not
claim this is the shipped behavior (see the header comment in
[`PackageRealizationAdmission.tla`](PackageRealizationAdmission.tla) for the
gap it targets, tracked by issue #4960), and it does not cover assembly/PE
content identity, which is not independently decidable the way a package
coordinate (id, version, framework, producer) is.

Owning design:
[`docs/design/inspection-layers.md`](../../design/inspection-layers.md)'s
"Package-realization coordinate admission" section, which is deliberately
separate from that document's "Package-role planning and cleanup boundary"
(target design for #4745) -- this model checks whether an admitting operation
starts at all for a repeated coordinate, not the internal plan/open/cleanup
shape of one such operation.

## Files

- [`PackageRealizationAdmission.tla`](PackageRealizationAdmission.tla) — the
  model: per-coordinate cache states (`Absent`/`InFlight`/`Ready`), per-demand
  states (`Pending`/`Admitting`/`Joined`/`Ready`/`Failed`), and the `Admit`,
  `Join`, `ReuseReady`, `CompleteSuccess`, `CompleteFailure` actions.
- [`PackageRealizationAdmission_MC.tla`](PackageRealizationAdmission_MC.tla) —
  model-checking harness. TLC's `.cfg` constant grammar cannot express the
  `CoordinateOf` function literal directly, so this module instantiates the
  base model with a concrete mapping over declared model values (`c1`, `c2`,
  `d1`..`d4`).
- [`PackageRealizationAdmission.cfg`](PackageRealizationAdmission.cfg) — the
  correctness gate: `TypeOK` and the three safety invariants
  (`SingleFlightPerCoordinate`, `ConsistentOutcomeAmongReadyDemands`,
  `CacheStateConsistent`), plus the `EveryDemandEventuallyResolves` liveness
  property. Must pass with no errors.
- `ReachabilityJoin.cfg`, `ReachabilityRetryAfterFailure.cfg`,
  `ReachabilityMultiDemandConsistency.cfg` — reachability probes, each
  checking the negation of one latching witness
  (`joinWitness`/`retryAfterFailureWitness`/`consistentOutcomeWitness`) as an
  INVARIANT. **Each is expected to report a violation**: the counterexample
  TLC prints is the proof that the corresponding transition (a join, a retry
  after a coordinate's prior failure, two demands sharing one successful
  outcome) is actually reachable in the model, not merely permitted by an
  unreachable guard. Do not add these to the main correctness gate — an
  expected failure there would look like a regression.

## Running

```bash
JAVA="$(brew --prefix openjdk@25)/bin/java"   # or your platform's pinned java
"$JAVA" -jar ~/.local/share/tlaplus/tla2tools.jar \
  -config PackageRealizationAdmission.cfg PackageRealizationAdmission_MC.tla

# Reachability probes (each expected to report one violation):
"$JAVA" -jar ~/.local/share/tlaplus/tla2tools.jar \
  -config ReachabilityJoin.cfg PackageRealizationAdmission_MC.tla
```
