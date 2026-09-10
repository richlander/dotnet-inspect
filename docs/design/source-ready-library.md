# Source-ready library representation

## Status and scope

This document is the normative owner for the shared source-ready library
representation introduced by step 2 of
[#6512](https://github.com/richlander/dotnet-inspect/issues/6512).

The first production consumer is `SourceHouse`. PackageHouse, PlatformHouse,
Workspace, and direct-library adapters are later producers. This slice defines
only the successful content handoff between those owners and source
composition. It does not define their acquisition, selection, failure, or
settlement outcomes.

The representation is implemented by `DotnetInspector.Libraries`. That project
depends on the source-neutral retained-content contract in
`Inspector.Artifacts.Workspaces` and the assembly projection issued by
`ILInspector.Metadata`. It does not depend on Packages, PlatformHouse,
SourceLink, the Decompiler, Services, Queries, or either product host.

## Authority and exact claim

**Source-ready Library Representation** owns:

> Bind one Metadata-projected physical assembly to its owner-retained guarded
> content, optionally designate one owner-retained artifact as that assembly's
> companion Portable PDB candidate, and retain already-present source content,
> while preserving each artifact's identity, generation, provenance, and
> access lifetime without introducing a path, reacquisition operation, or
> unverified PDB-match claim.

The owner defines:

- `SourceReadyAssembly`, which binds one `ArtifactContentReference` to the
  exact `ArtifactAssemblyProjection` issued for the same artifact generation;
- `SourceReadyLibrary`, which carries that selected assembly and an optional
  companion Portable PDB candidate;
- `SourceReadyPortablePdbCandidate`, which records the declared association
  between the exact assembly projection and the candidate content;
- `SourceReadySourceContent`, which retains already-present source bytes by
  owner-issued artifact identity; and
- construction guards that reject an assembly projection for a different
  artifact or generation.

It does not define:

- package, platform, project, Workspace, or direct-library acquisition;
- package coordinates, platform targets, asset selection, or view pairing;
- whether the selected assembly is a reference or implementation view;
- Portable PDB discovery, extraction, parsing, or storage;
- assembly-to-PDB content validation;
- SourceLink interpretation, source retrieval, or decompilation;
- SourceHouse source-result or PDB-access policy;
- source request, source result, or settlement receipt shapes; or
- ownership of the artifact session or query lease.

Those facts remain in their owning results and receipts. An owner projects one
already-selected source target into `SourceReadyLibrary` only after its own
selection succeeds.

## Why this is not the bare-library aggregate

PackageHouse and PlatformHouse describe a broader provenance-retaining bare
library that can carry reference and implementation views, target context,
paired-view correspondence, and owner-specific settlement evidence.
SourceHouse needs a smaller input: one exact physical assembly selected for the
source request and any already-retained companion PDB.

This contract is therefore a projection of a successful bare-library result,
not the owner of the broader result. It deliberately does not introduce a
package/platform union or generic target-context abstraction. Package and
platform evidence remains strongly typed in the producing owner's result while
the source-ready projection preserves the selected artifacts' typed
`IArtifactProvenance`.

## Type and correspondence model

```text
SourceReadyLibrary
  Assembly
    Content -> ArtifactContentReference
    Projection -> ArtifactAssemblyProjection
  PortablePdbCandidate?
    Assembly -> exact AssemblyProjectionRegistration above
    Content -> ArtifactContentReference
  RetainedSourceContent*
    Registration -> exact ArtifactAcquisitionRegistration
    Content -> ArtifactContentReference
```

`SourceReadyAssembly` accepts a content reference only when:

- the projection registration's artifact is the content registration's exact
  artifact identity; and
- the projection registration's generation is the content registration's
  exact artifact generation.

Reference identity is intentional. Artifact identities and generations are
opaque owner-issued currencies; reconstructing correspondence from ordinals,
paths, assembly names, module IDs, or display text is invalid.

The assembly and companion PDB may come from different artifact generations.
For example, PackageHouse can retain an assembly from a package payload and a
PDB from an independently authorized symbol acquisition. Each
`ArtifactContentReference` retains its own generation and lease
correspondence.

`SourceReadyPortablePdbCandidate` records that the producer designated the
content as the companion candidate for the exact assembly. It does not claim
that the candidate's Portable PDB content ID matches the assembly's CodeView
entry. The PDB owner must still validate applicability before either
SourceLink interpretation or PDB-enriched decompilation uses it.
The candidate must be a distinct artifact; an assembly cannot be relabeled as
its own external companion. An embedded Portable PDB remains part of the
assembly artifact and follows the separate embedded-PDB interpretation path.

If an upstream owner already issued stronger correspondence evidence, that
evidence remains in the owner's typed result or provenance and is preserved
beside the source-ready value. This representation does not translate it into
a weaker common label.

## Content and lifetime

Both assembly and optional PDB bytes are exposed only through
`ArtifactContentReference`. Consequently:

- content is the immutable snapshot retained by the artifact session;
- access revalidates the exact owner-issued query lease;
- disposing or replacing the lease rejects future opens;
- ending the artifact generation rejects future opens; and
- a stream already returned remains governed by the artifact access contract.

`SourceReadyLibrary` does not own or dispose the lease. The producer or
orchestration result that returns the value retains the session and lease for
the complete SourceHouse operation. A source result that must outlive that
operation retains its own owner-issued lifetime rather than assuming the input
lease remains valid.

The public representation has no filesystem path, stream property, content
delegate, package-store key, or acquisition callback. Consumers open already
retained content; they do not rediscover it.

Already-retained source content is snapshotted into an immutable sequence.
Each entry captures its registration and provenance and remains addressable by
its typed `ArtifactIdentity`. The sequence rejects duplicate artifacts and
rejects relabeling the assembly or PDB artifact as source content.

An embedded Portable PDB remains inside the assembly artifact and therefore
needs no second content reference. SourceHouse policy determines whether the
PDB service may inspect or extract it.

## Failure boundary

`SourceReadyLibrary` is a successful handoff value, not a success-shaped
failure envelope. Acquisition failure, artifact rejection, ambiguity,
incomplete selection, and missing required content remain visible in the
producing owner's outcome and prevent that owner from publishing this value.

An absent companion PDB is valid because decompilation can proceed without
one. A designated companion that later fails Portable PDB parsing or
assembly-match validation rejects only that optional contribution unless the
producer's own result falsely claimed validated correspondence. SourceHouse
then follows the request's independent PDB policy and source-result demand.

## Pathological case

PackageHouse retains an assembly from a package and a symbol-server response
that was selected by coordinate but whose Portable PDB content ID does not
match the assembly's CodeView entry. It may hand both guarded artifacts to
`SourceReadyLibrary` only as an assembly plus companion candidate.

The PDB owner rejects the candidate during validation. `BestAvailable` may
continue to PDB-free decompilation; `AuthoredOnly` reports authored source as
unavailable. No layer reopens the package, probes an adjacent path, or treats
the candidate designation as proof of a content match.

## Evidence

The focused `DotnetInspector.Libraries.Tests` executable gates:

- exact artifact and generation binding between assembly content and Metadata
  projection;
- pathless assembly and optional PDB content;
- preservation of assembly, PDB, and retained-source provenance;
- explicit assembly association for the PDB candidate;
- rejection when the assembly artifact is relabeled as its own PDB candidate;
- valid operation without a companion PDB; and
- lease invalidation remaining visible through the retained content
  references.

PDB content matching is intentionally not a claim of this owner and is not
manufactured by these tests.

## Production adoption

This contract satisfies step 2 of #6512. The remaining tracker steps introduce
the two producer services and SourceHouse, then project successful PackageHouse,
PlatformHouse, Workspace, and direct-library results into this representation
before adopting SourceHouse in CLI and Browser/Wasm hosts.
