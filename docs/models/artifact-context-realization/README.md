# Artifact context realization

Models the target workspace interaction that realizes reusable,
binding-consistent assembly universes from already-sealed artifact
generations.

The universe key contains the exact candidate-artifact selection plus binding
and authorization policy generations. Required inspection targets belong to
each demand instead: two demands may share one universe while one succeeds and
the other reports that its required target is unavailable.

The owning design is
[`docs/design/artifact-acquisition-and-workspaces.md`](../../design/artifact-acquisition-and-workspaces.md).

## Scope

The model checks:

- one in-flight realization per exact universe key;
- joining and later reuse across demands with different targets;
- one stable group identity for every successful universe;
- target-specific rejection without poisoning that ready universe;
- visible universe failure without caching a partial group; and
- progress from every pending demand to a ready, rejected, or failed outcome.

It treats universe identity, universe validity, and target acceptance as
owner-issued inputs. It does not model artifact acquisition, package-coordinate
resolution, managed-metadata decoding, binding algorithms, query callbacks,
group disposal, or artifact-session quiescence. Those interactions remain
owned by `ArtifactSessionAdmission`, `PackageRealizationAdmission`, and
`AssemblyContextGroupLifecycle`.

## Files

- `ArtifactContextRealization.tla` defines the interaction and properties.
- `ArtifactContextRealization_MC.tla` supplies three concrete universe keys and
  five demands.
- `ArtifactContextRealization.cfg` is the correctness gate.
- `ReachabilityJoin.cfg`, `ReachabilityReuse.cfg`,
  `ReachabilityTargetIsolation.cfg`, `ReachabilityDistinctUniverses.cfg`, and
  `ReachabilityRetry.cfg` are expected-failure reachability probes. Each checks
  the negation of one latching witness as an invariant; TLC's counterexample
  proves the corresponding transition is reachable.

## Running

Use the repository-pinned TLA+ `v1.8.0` tools:

```bash
cd docs/models/artifact-context-realization
java -XX:+UseParallelGC \
  -cp ~/.local/share/tlaplus/tla2tools.jar tlc2.TLC \
  -cleanup -config ArtifactContextRealization.cfg \
  ArtifactContextRealization_MC.tla

for config in ReachabilityJoin ReachabilityReuse \
  ReachabilityTargetIsolation ReachabilityDistinctUniverses \
  ReachabilityRetry; do
  java -XX:+UseParallelGC \
    -cp ~/.local/share/tlaplus/tla2tools.jar tlc2.TLC \
    -cleanup -noGenerateSpecTE -config "$config.cfg" \
    ArtifactContextRealization_MC.tla
done
```

Run the commands sequentially because TLC processes in one directory otherwise
share the default `states/` checkpoint path. The correctness configuration must
complete without errors. Every reachability configuration must report its
named invariant as violated.
