# Admission assembly projection model

`AdmissionAssemblyProjection.tla` models the target interaction defined by the
[admission-scoped artifact projection](../../assembly-inspection-query.md#admission-scoped-artifact-projection)
contract.

The artifact owner validates one admission or query authority and lends one
immutable image for a callback. The assembly-query owner classifies an
admission image into content-free correspondence facts and later validates
query-authorized retained bytes against those frozen facts.

## Scope

The model contains one artifact projection and one later query. Five managed
assembly images separate the matching dimensions:

- `A` is the projected image;
- `B` has `A`'s identity and generation but a different MVID;
- `C` has `A`'s MVID and generation but a different assembly identity; and
- `D` has `A`'s assembly identity and MVID but belongs to a different artifact
  generation and artifact identity; and
- `E` has `A`'s generation, assembly identity, and MVID but a different
  artifact identity.

Native, managed-module, unsupported Windows Metadata, malformed, and
empty-MVID images exercise the typed query and admission non-participant
outcomes. In the model, `projectionRegistration` is the opaque,
provenance-free `ArtifactIdentity` minted with the owner-private acquisition
registration; it is not the current public
`ArtifactAcquisitionRegistration` value.

The model checks:

- projection requires current admission authority;
- projected facts retain the exact opaque artifact identity;
- projected and published facts carry no content authority;
- only supported ECMA-335 managed assemblies with non-empty MVIDs project;
- native and module classifications and unsupported Windows Metadata,
  malformed, or empty-MVID rejections carry no assembly facts;
- publication uses the exact projected artifact identity, generation, assembly
  identity, and MVID;
- query validation begins only after publication and requires current query
  authority; and
- successful query validation requires exact artifact identity, generation,
  assembly identity, and MVID agreement, with typed non-assembly and rejection
  reasons.

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
| `BrokenDroppedRegistration.cfg` | Omitting the exact opaque artifact identity violates `ProjectedRegistrationIsExact`. |
| `BrokenRegistrationValidation.cfg` | Accepting a different artifact identity in the same generation violates `ValidatedImageMatchesProjection`. |
| `BrokenIdentityValidation.cfg` | Accepting a different assembly identity violates `ValidatedImageMatchesProjection`. |
| `BrokenMvidValidation.cfg` | Accepting a replacement MVID violates `ValidatedImageMatchesProjection`. |
| `BrokenRevokedQuery.cfg` | Validation under revoked query authority violates `QueryValidationRequiresCurrentAuthority`. |
| `BrokenUnsupportedAdmission.cfg` | Projecting unsupported Windows Metadata violates `OnlySupportedAssembliesProject`. |
| `BrokenUnsupportedQuery.cfg` | Validating unsupported Windows Metadata violates `UnsupportedQueryIsRejected`. |

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
  BrokenDroppedRegistration BrokenRegistrationValidation \
  BrokenIdentityValidation BrokenMvidValidation BrokenRevokedQuery \
  BrokenUnsupportedAdmission BrokenUnsupportedQuery; do
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
| `Safety.cfg` | No error | 74 | 56 | 6 |
| `ReachabilityMatching.cfg` | `MatchingQueryRoundTripIsUnreachable` violated | 34 | 27 | 5 |
| `ReachabilityMismatch.cfg` | `MismatchRejectionIsUnreachable` violated | 35 | 28 | 5 |
| `BrokenStaleAdmission.cfg` | `ProjectionRequiresCurrentAdmission` violated | 11 | 10 | 3 |
| `BrokenLeakedAuthority.cfg` | `ProjectedFactsCarryNoAuthority` violated | 4 | 4 | 2 |
| `BrokenDroppedRegistration.cfg` | `ProjectedRegistrationIsExact` violated | 4 | 4 | 2 |
| `BrokenRegistrationValidation.cfg` | `ValidatedImageMatchesProjection` violated | 38 | 31 | 5 |
| `BrokenIdentityValidation.cfg` | `ValidatedImageMatchesProjection` violated | 36 | 29 | 5 |
| `BrokenMvidValidation.cfg` | `ValidatedImageMatchesProjection` violated | 35 | 28 | 5 |
| `BrokenRevokedQuery.cfg` | `QueryValidationRequiresCurrentAuthority` violated | 44 | 37 | 6 |
| `BrokenUnsupportedAdmission.cfg` | `OnlySupportedAssembliesProject` violated | 7 | 7 | 2 |
| `BrokenUnsupportedQuery.cfg` | `UnsupportedQueryIsRejected` violated | 41 | 34 | 5 |

The positive configuration explored its complete bounded state graph. Each
probe or mutation stopped at its first expected counterexample. The shortest
capability and artifact-identity counterexamples are two transitions: a valid
managed assembly projects while retaining authority or omitting its exact
opaque correspondence value. Artifact, assembly-identity, and MVID mutations
reach publication and then accept the corresponding replacement view. The
positive safety configuration separately requires the foreign-generation view
to produce `GenerationMismatch`. The revoked query mutation publishes exact
facts, obtains and revokes query authority, and then validates under that stale
authority. The unsupported-admission mutation projects marker-bearing Windows
Metadata, while the unsupported-query mutation publishes `A` and accepts the
otherwise matching unsupported image instead of returning its dedicated
rejection.

## Assumptions and non-claims

- The artifact owner's authority validation and immutable callback bytes are
  inputs to this model; their internal implementation is not modeled.
- The artifact owner's mapping from failed authority validation to the outer
  `AdmissionUnauthorized` or `QueryUnauthorized` result is not modeled. The
  design assigns those exact mappings and callback/producer non-invocation to
  named Release gates.
- One symbolic content-authority bit stands for every forbidden path, stream,
  opener, content reference, lease, and retained byte route.
- The model does not cover artifact acquisition, workspace roles, group
  construction, binding policy, member correspondence, PDB or source
  acquisition, or CLI presentation.
- The model establishes properties of the bounded abstract protocol, not
  implementation conformance.
