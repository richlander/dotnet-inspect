# Admission assembly projection model

`AdmissionAssemblyProjection.tla` models the target interaction defined by the
[admission-scoped artifact projection](../../assembly-inspection-query.md#admission-scoped-artifact-projection)
contract.

The artifact owner validates one admission or query authority and lends one
immutable image for a callback. The assembly-query owner classifies an
admission image into content-free registration facts and later validates
query-authorized retained bytes against those frozen facts.

## Scope

The model contains one artifact projection and one later query. Four managed
assembly images separate the matching dimensions:

- `A` is the projected image;
- `B` has `A`'s identity and generation but a different MVID;
- `C` has `A`'s MVID and generation but a different assembly identity; and
- `D` has `A`'s identity and MVID but a different artifact generation and
  registration.

Native, managed-module, malformed, and empty-MVID images exercise the
non-participant outcomes.

The model checks:

- projection requires current admission authority;
- projected facts retain the exact artifact registration;
- projected and published facts carry no content authority;
- only managed assemblies with non-empty MVIDs project;
- native and module classifications and malformed or empty-MVID rejections
  carry no assembly facts;
- publication uses the exact projected registration, generation, identity,
  and MVID;
- query validation begins only after publication and requires current query
  authority; and
- successful query validation requires exact registration, generation,
  identity, and MVID agreement.

The model has no liveness claim. Artifact admission, content-access
quiescence, and group disposal are independently modeled by
`ArtifactSessionAdmission`, `ArtifactGenerationAccess`, and
`AssemblyContextGroupLifecycle`. This model does not redefine those owners.

## Configurations

| Configuration | Expected result |
| --- | --- |
| `Safety.cfg` | Every safety invariant passes. |
| `ReachabilityMatching.cfg` | The probe invariant fails, proving matching validation is reachable. |
| `ReachabilityMismatch.cfg` | The probe invariant fails, proving mismatch rejection is reachable. |
| `BrokenStaleAdmission.cfg` | Projection after admission revocation violates `ProjectionRequiresCurrentAdmission`. |
| `BrokenLeakedAuthority.cfg` | A projection retaining content authority violates `ProjectedFactsCarryNoAuthority`. |
| `BrokenDroppedRegistration.cfg` | Omitting the exact artifact registration violates `ProjectedRegistrationIsExact`. |
| `BrokenIdentityValidation.cfg` | Accepting a different assembly identity violates `ValidatedImageMatchesProjection`. |
| `BrokenMvidValidation.cfg` | Accepting a replacement MVID violates `ValidatedImageMatchesProjection`. |
| `BrokenGenerationValidation.cfg` | Accepting a foreign generation and registration violates `ValidatedImageMatchesProjection`. |
| `BrokenRevokedQuery.cfg` | Validation under revoked query authority violates `QueryValidationRequiresCurrentAuthority`. |

## Running TLC

Use the repository-pinned TLA+ v1.8.0 tools:

```bash
cd docs/design/models/admission-assembly-projection
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup -config Safety.cfg AdmissionAssemblyProjection.tla
for config in ReachabilityMatching ReachabilityMismatch; do
  java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
    -cleanup -noGenerateSpecTE -config "$config.cfg" \
    AdmissionAssemblyProjection.tla
done
for config in BrokenStaleAdmission BrokenLeakedAuthority \
  BrokenDroppedRegistration BrokenIdentityValidation BrokenMvidValidation \
  BrokenGenerationValidation BrokenRevokedQuery; do
  java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
    -cleanup -noGenerateSpecTE -config "$config.cfg" \
    AdmissionAssemblyProjection.tla
done
```

Run configurations sequentially because TLC otherwise shares its default
checkpoint directory.

## Checked results

Checked on Linux with OpenJDK `21.0.12` and the repository-pinned TLA+ v1.8.0
prerelease (`TLC2 2026.08.21.155922`, rev `9787e65`). The checked
`tla2tools.jar` has SHA-256
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.

| Configuration | Result | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: |
| `Safety.cfg` | No error | 146 | 111 | 6 |
| `ReachabilityMatching.cfg` | `MatchingQueryRoundTripIsUnreachable` violated | 44 | 36 | 5 |
| `ReachabilityMismatch.cfg` | `MismatchRejectionIsUnreachable` violated | 45 | 37 | 5 |
| `BrokenStaleAdmission.cfg` | `ProjectionRequiresCurrentAdmission` violated | 12 | 11 | 3 |
| `BrokenLeakedAuthority.cfg` | `ProjectedFactsCarryNoAuthority` violated | 4 | 4 | 2 |
| `BrokenDroppedRegistration.cfg` | `ProjectedRegistrationIsExact` violated | 4 | 4 | 2 |
| `BrokenIdentityValidation.cfg` | `ValidatedImageMatchesProjection` violated | 46 | 38 | 5 |
| `BrokenMvidValidation.cfg` | `ValidatedImageMatchesProjection` violated | 45 | 37 | 5 |
| `BrokenGenerationValidation.cfg` | `ValidatedImageMatchesProjection` violated | 47 | 39 | 5 |
| `BrokenRevokedQuery.cfg` | `QueryValidationRequiresCurrentAuthority` violated | 72 | 64 | 6 |

The positive configuration explored its complete bounded state graph. Each
probe or mutation stopped at its first expected counterexample. The shortest
capability and registration counterexamples are two transitions: a valid
managed assembly projects while retaining authority or omitting its exact
artifact registration. Identity, MVID, and generation mutations reach
publication and then accept the corresponding replacement image. The revoked
query mutation publishes exact facts, obtains and revokes query authority, and
then validates under that stale authority.

## Assumptions and non-claims

- The artifact owner's authority validation and immutable callback bytes are
  inputs to this model; their internal implementation is not modeled.
- One symbolic content-authority bit stands for every forbidden path, stream,
  opener, content reference, lease, and retained byte route.
- The model does not cover artifact acquisition, workspace roles, group
  construction, binding policy, member correspondence, PDB or source
  acquisition, or CLI presentation.
- The model establishes properties of the bounded abstract protocol, not
  implementation conformance.
