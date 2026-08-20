# Artifact acquisition and workspace composition

How storage, packages, artifacts, assemblies, workspaces, sessions, and
inspection producers remain separate concepts while still composing into one
inspection experience.

This is a design proposal. The target boundaries and gates in this document are
**unverified** until their named implementation gates exist. Current types that
do not satisfy the target are identified explicitly under
[Current mismatches](#current-mismatches).

See [inspection-space.md](../inspection-space.md) for workspace and query
planning, [inspection-layers.md](inspection-layers.md) for consumer layers, and
[assembly-inspection-query.md](assembly-inspection-query.md) for the
`ResolvedAssemblyReference` and `AssemblyInspectionSession` seam.
[workspace-definitions.md](workspace-definitions.md) owns static context
coordinates, while
[inspection-graph-document.md](inspection-graph-document.md) owns graph
subjects and relationships.

## Decision

A workspace may contain artifacts acquired from any number of heterogeneous
sources. Source adapters contribute source-neutral artifact content and typed
provenance. The workspace owns the resulting lifetimes and composes artifacts
into binding-consistent assembly context groups.

Packages, local files, restored projects, platform packs, embedded bundle
content, and remote CI build artifacts are peer **artifact sources**. None is
the universal model.

The target layering is:

```text
host composition
  |
  +-- local adapter -----------+
  +-- package adapter ---------+
  +-- project adapter ---------+--> artifact acquisitions
  +-- platform adapter --------+          |
  +-- CI artifact adapter -----+          v
  |                              ArtifactSetSession(s)
  |                                 owned by workspace
  v                                          |
InspectionWorkspace                         |
  +-- AssemblyContextGroup <----------------+
        |
        v
      query
        |
        v
AssemblyInspectionSession
        |
        v
metadata / analysis / source inspection producer
```

The query boundary is intentional. A consumer does not take an artifact handle
and invoke producers ad hoc. A query selects the participant and owns the
`AssemblyInspectionSession` lifetime through which producers run.

Two different roles must not both be called "source producer":

- an **artifact source adapter** runs before the workspace session and acquires
  content;
- an **inspection producer** runs inside a query after an assembly session is
  open and produces metadata, Analysis, decompiler, or authored-source
  evidence.

## Why this boundary

The immediate pressure came from package acquisition. Several consumers need a
package root or package entry before they can select an assembly. Routing that
need through `AssemblySetResolver` appears to centralize acquisition, but it
makes the assembly-set abstraction own package identity, package extraction,
NuGet source options, and package-specific TFM policy.

[PR #4391](https://github.com/richlander/dotnet-inspect/pull/4391) validated
that such routing could preserve current behavior. Its implementation and two
exact-head review rounds were clean. The PR was closed because the shape
required an "acquisition-only assembly set": a successful assembly set
containing no assemblies whose purpose was to return a package root. That is
evidence that the dependency direction was wrong, not evidence that an empty
assembly set is a useful concept.

The stronger requirement is a compile-time one:

> It must be possible to build a dotnet-inspect variant that inspects only local
> libraries without referencing package resolution, NuGet transport, package
> storage, or package-specific workspace implementations.

Turning package support off at runtime does not satisfy this requirement. The
package implementation must be absent from the local-only product's project and
package dependency closure.

## Concepts and owners

| Concept | Meaning | Owns | Must not own |
| --- | --- | --- | --- |
| Storage | Retention and retrieval of opaque bytes | cache keys, publication, eviction, content leases | package selection, PE identity, workspace binding |
| Artifact | One immutable inspectable content item | logical identity, media/kind hint, digest, owner-mediated content access | package or assembly policy |
| Artifact acquisition | One adapter's typed attempt to contribute artifacts | outcomes, diagnostics, provenance, content leases | workspace binding |
| Artifact source adapter | Resolves one source-specific coordinate | source protocol, authorization, listing, archive rules | inspection queries |
| `ArtifactSetSession` | One sealed artifact generation admitted to a workspace | child acquisition leases and artifact handles | source-specific resolution or assembly binding |
| Workspace | Logical inspection composition | artifact sessions, contexts, roles, query plans | feed or archive mechanics |
| Assembly context group | One binding-consistent universe | participants, binding policy, retained assembly snapshots | package acquisition |
| Resolved assembly reference | Neutral handle for one selected managed assembly | assembly identity and guarded repeatable content access | package coordinate parsing or storage implementation |
| Assembly inspection session | One opened PE inspection lifetime | reader/image lifetime and session-scoped operations | artifact acquisition |
| Inspection producer | Computes one family of facts | metadata, IL, source, or comparison evidence | source discovery |

An artifact is broader than an assembly. An artifact set may contain assemblies,
portable PDBs, XML documentation, manifests, source archives, or other content.
Only an assembly projection attempts to decode an artifact as managed PE
content and mint a `ResolvedAssemblyReference`.

Artifact identity and assembly identity are separate:

- **artifact identity** answers which immutable content item a source
  contributed;
- **assembly identity** is decoded from managed metadata;
- **workspace participant identity** binds that assembly identity to one
  artifact and one policy snapshot;
- **presentation identity** is projected later.

No layer infers one identity from another's display text.

### Provenance and correspondence

Source-specific provenance remains typed without becoming a source-specific
dependency of the artifact or assembly projects.

The artifact contract supplies only an owner-issued artifact identity and a
source-neutral marker implemented by typed provenance records. Each adapter
defines its own record: package coordinates and producer key in the package
companion, CI run and artifact ids in a CI companion, and local
path/fingerprint evidence in the local adapter. The workspace retains the
record and correlates it with the artifact identity.

Assembly projection carries the artifact identity, not a Metadata-owned union
of every possible source kind. Adapter-aware queries or hosts can ask the
workspace for the source-specific provenance record through the adapter's typed
contract. Assembly-only consumers can preserve and compare the correlation
identity without referencing or interpreting package or CI types.

This is not an untyped property bag. Adding a new source requires a typed
provenance record and its owning projection/serialization code, but does not
require changing Metadata.

Correspondence is likewise owner-issued. After validating its coordinate
against acquired content, an adapter mints an acquisition registration scoped
to one artifact generation. Assembly projection records which artifact
registration produced each assembly registration. A package-aware query can
then consume the package adapter's typed realization and correspondence proof;
it does not ask Metadata to reinterpret package fields.

Artifact identity and acquisition registration answer exact correspondence
only inside their owning artifact generation. A digest and immutable source
coordinate can provide durable content evidence, but neither recreates the
owner-issued registration after that generation ends.

Caller designation is a policy input, not source provenance or assembly
identity. The current `AssemblyResolutionProvenance.DesignatedAsset` carries the
fact that a caller explicitly enumerated a corpus/build-layout assembly so the
decompiler can grant core-library identity trust. In the target architecture,
the local/project adapter records how the artifact was acquired, while the
authorized admission records an explicit designation role on the workspace
registration. A producer may consume that role under the current plan's trust
policy; it cannot infer designation from a path or metadata name.

## `ArtifactSetSession`

`ArtifactSetSession` is one acquisition lifetime and consistency boundary owned
by a workspace. A workspace may own several sessions, normally one for each
context realization or other atomic admission. Each session may aggregate
several source adapters.

The session:

1. accepts typed artifact-acquisition outcomes from multiple adapters;
2. retains every admitted source-specific content lease;
3. exposes a stable, source-neutral artifact catalog without an unguarded
   content opener;
4. preserves provenance on each artifact;
5. rejects identity collisions or represents them explicitly;
6. makes acquisition failures visible to workspace construction;
7. releases all child leases after every dependent group is quiescent.

The session has a construction phase and a sealed phase. Adapters contribute
only during construction. Queries observe only the sealed catalog; adding,
removing, or replacing an acquisition creates a new session rather than
mutating one that queries use. Loading another context into a retained workspace
adds another sealed session and group; it does not discard or mutate sessions
and groups already in use.

Sealing does not claim that GitHub, Azure DevOps, a local directory, and a
package feed participated in one transaction. Each acquisition records its own
immutable coordinate, producer, content identity, and observation. The session
guarantees that those recorded acquisitions do not silently drift after
admission.

An implementation may ensure stable bytes by retaining a snapshot, by retaining
content-addressed storage, or by validating the recorded digest whenever it
reopens content. A mutable path or expiring download URL is not sufficient
identity. If the recorded content can no longer be opened, the operation fails
visibly rather than reading replacement bytes.

The workspace registers an admission operation before its first asynchronous
adapter call and owns that operation through atomic group publication.
Workspace disposal first closes admission, requests cancellation of in-flight
operations, and prevents a late result from publishing a session or group. A
late acquisition outcome transfers directly to cleanup: every returned lease
is disposed even when the adapter did not observe cancellation. This rule also
handles single-threaded Browser/Wasm reentrancy; it does not depend on a
parallel thread reaching a lock.

Disposal then disposes published groups. A group may already have an active
callback that has not performed its first lazy content open. Artifact leases
therefore outlive `Dispose()` and are released only after every dependent group
reports quiescence. Synchronous disposal may initiate this deferred release; it
must not invalidate content under an active callback. Cleanup failures compose
with, and never replace, the active operation failure.

Retaining content does not retain authority. The artifact owner issues two
different source-neutral access leases:

- an **admission lease** authorizes the context loader to project sealed
  artifacts into assembly identities and participants while constructing the
  group. It is issued under the first authorized query plan that demands that
  context; loading an inert definition alone cannot obtain one. The lease
  expires when group publication succeeds or the admission attempt aborts.
  Neither the group nor its participants retain it;
- a **query lease** revalidates the current query plan's capabilities and
  source policy before it can select participants, observe binding or
  correspondence answers, receive content, or use a retained snapshot.

Changed, narrowed, or revoked authorization rejects the query before catalog or
participant selection even when the selected image remains authorized and the
bytes and prior binding answers remain in memory. Reuse of a group also
requires that its binding-policy and correspondence generation be compatible
with the current plan's complete authorization scope. Reauthorizing only the
image is insufficient because catalog membership and binding answers can
themselves reveal unauthorized candidates.

During construction, only the current admission lease exposes content. After
group publication, guarded content access rejects that expired lease and
accepts only a current query lease. An artifact catalog descriptor or
`ResolvedAssemblyReference` cannot bypass the owner with a bare `Func<Stream>`
or readable path. A path on a target descriptor is inert location evidence, not
read authority; when a producer genuinely requires a path, the current lease
may provide a lease-scoped path to the exact retained snapshot. Receiving or
opening that path grants no designation or core-library trust; those remain
separate workspace admission roles. This is a target change from the current
parameterless
`ResolvedAssemblyReference.OpenRead` and public readable `Path`.

It does not:

- resolve package versions;
- parse project assets;
- choose a target framework;
- inspect PE metadata;
- construct assembly binding groups;
- render diagnostics.

### Multiple sources are ordinary

A workspace is not associated with one source. For example:

```text
workspace
  artifact set
    local acquisition
      ./bin/MyApp.dll
    package acquisition
      Newtonsoft.Json@13.0.4/lib/net6.0/Newtonsoft.Json.dll
    platform acquisition
      System.Runtime.dll
    CI acquisition
      base-build/api/Contracts.dll
      pr-build/api/Contracts.dll
```

Provenance belongs to each artifact, not to the workspace or the artifact set.
An assembly context group may contain participants projected from several
acquisitions when its binding policy permits that composition. Conversely,
artifacts from one acquisition may be partitioned into several groups when they
represent incompatible framework or runtime contexts.

The artifact set therefore owns content lifetime but not assembly grouping.

### Acquisition outcomes

An adapter returns a typed outcome, conceptually:

```text
ArtifactAcquisitionOutcome
  = Acquired(artifacts, provenance, lease)
  | Unavailable(diagnostic)
  | Rejected(diagnostic)
  | Failed(diagnostic)
```

The exact type names are an implementation decision. The semantic requirements
are not:

- a required acquisition failure cannot become an empty successful set;
- each declared context member must realize at least one artifact eligible for
  that member's projection; an empty or wholly non-projectable `Acquired`
  result is a typed member failure;
- a context cannot silently omit a failed required member;
- every member in a static workspace context remains required;
- host composition may make an entire acquisition optional before constructing
  a context, but failure never makes a declared context member optional;
- disposal failure cannot replace the primary acquisition or inspection
  failure;
- cancellation propagates as cancellation rather than occupying a failure arm
  or being relabeled as an acquisition diagnostic.

## Source adapters

An artifact source adapter owns the semantics needed to resolve its coordinate.
It returns artifacts and leases; it does not create an assembly session.

### Local adapter

The local adapter accepts explicit files or directories under host policy. It
opens local content without acquiring package or remote-storage dependencies.
Directory enumeration and path containment remain local-adapter concerns.

Before sealing, it copies every admitted file into an immutable retained or
content-addressed snapshot under explicit entry and byte budgets, then computes
identity from those exact bytes. Consumers never receive a mutable source-file
stream after a separate digest check. Rebuild, replacement, symlink retargeting,
or deletion after admission cannot substitute new bytes into the retained
snapshot. Directory admission snapshots only the selected, bounded entries,
not an unbounded tree.

This adapter is the proof that the abstraction is independently useful. A
local-only host composes:

```text
artifact contracts + local adapter + Metadata + Queries + chosen producers
```

Its dependency closure excludes `DotnetInspector.Packages`, `NuGetFetch`,
NuGet protocol libraries, package stores, and package-specific query
implementations.

### Package adapter

The package adapter owns:

- package coordinates and version selection;
- source authorization and source mapping;
- package archive acquisition and admission;
- nuspec and package asset-group semantics;
- TFM/RID asset selection;
- package-specific provenance.

It may internally use a package content lease or package session. That is a
package-layer implementation detail, not the shared workspace currency.

The adapter projects selected package entries into neutral artifacts. Package
identity stays in artifact/workspace provenance and optional package query
results. It does not become a case in a Metadata-owned provenance union that
assembly inspection must understand.

The adapter also validates package coordinate, version, selected asset path,
producer, and content identity before minting a package realization and
acquisition registration. Package-aware graph and dependency queries move to an
optional companion and consume that proof. The shared graph document may retain
its serialized `package` subject kind as a full-host contract; core assembly
queries do not construct package subjects or parse package provenance.

### Project adapter

The project adapter interprets restore outputs and project build products. A
restored package asset may retain package provenance, but the adapter—not the
assembly layer—understands `project.assets.json`, package roots, and restore
layout.

### Platform adapter

The platform adapter resolves installed or remotely acquired platform content.
Platform packs may happen to be transported as NuGet packages, but transport
does not make "package" the workspace model. An installed-platform adapter is
package-free. A NuGet-backed remote-platform implementation may instead live
with the optional package acquisition implementation so that it reuses package
source mapping, producer authorization, version selection, and payload-cache
rules rather than duplicating them. It returns a neutral validated platform
realization; the platform graph projection and core assembly path do not
reference its package transport.

It validates platform family, version, selected assembly, producer, and content
identity before minting a platform realization and generation-scoped
correspondence proof. Platform-aware graph projection consumes that proof
without parsing NuGet versions or Metadata-owned platform provenance in core
assembly Queries. The realization records evidence; it does not grant
core-library trust. Workspace admission assigns any platform-trust role under
explicit host policy after validating that evidence.

### Embedded adapter

The embedded adapter resolves bundle-relative content from an explicitly
authorized inspection bundle. It must preserve the bundle content digest and
declared logical identity without turning a bundle into a pseudo-package.

## Remote CI build artifacts

Remote build artifacts are a required architecture scenario because they
exercise the boundary without any package semantics.

An Azure DevOps or GitHub Actions adapter could resolve a coordinate such as:

```text
provider
repository or project
immutable run or build id
artifact name
optional entry selection
```

The adapter would:

1. use explicit host-supplied network and credential capabilities;
2. query the provider for the exact immutable run or build;
3. retain repository, commit, PR, workflow or pipeline, job, artifact name,
   provider artifact id, and digest as provenance;
4. acquire the artifact archive lazily or eagerly under declared budgets;
5. apply archive traversal, entry-count, expanded-size, and content limits;
6. contribute selected entries as neutral artifacts;
7. retain the download/archive lease until the owning artifact session's
   dependent groups are quiescent.

Later queries reauthorize that provider, repository/project, run/build, and
artifact coordinate before receiving a query access lease. Retaining the
download does not preserve a credential grant after the host removes it.

The workspace could then compare:

```text
Baseline context
  GitHub Actions run for base commit
  platform reference artifacts

Candidate context
  Azure DevOps build for PR commit
  selected local dependency override
  the same platform reference artifacts
```

Queries and assembly sessions see artifact handles, assembly identities, and
workspace provenance. They do not know which provider supplied the bytes, how
its API authenticates, or whether the content arrived in a zip archive.

This scenario establishes several design tests:

- one workspace can own acquisitions from different providers;
- contexts can compose artifacts from different sources;
- provenance survives comparison without becoming assembly policy;
- archive storage is not package storage;
- source authorization stays with acquisition;
- stable coordinates use immutable run/build ids and digests, not moving branch
  names.

No CI adapter is required in the first implementation. The scenario is an
acceptance test for the abstractions that precede it.

## Storage boundary

Storage owns opaque content retention. It may provide filesystem, memory,
browser, remote-cache, or content-addressed implementations.

An owner-authorized access lease may expose a repeatable stream opener, a
bounded buffer, or a lease-scoped path to retained content when the storage
implementation has one. A catalog or target descriptor cannot expose those
routes, and consumers cannot require the path form. A leased path is content
transport, not evidence of caller designation or another workspace role.
Storage does not:

- decide whether content is a package or assembly;
- parse a nuspec or PE header;
- select package assets;
- assign workspace roles;
- authorize a producer for a package coordinate.

Authorization and storage eligibility remain separate. A cache hit is usable
only when the current source adapter proves that the request authorizes the
content and its producer.

Package stores may implement package-specific admission and entry access above
the generic storage boundary. Their interfaces must not leak into the
source-neutral artifact or assembly layers.

Portable-PDB and authored-source storage also need neutral ownership.
`IPdbStore`, source authorization, and package symbol-source options currently
live in the package project and appear in a core assembly query. The target
extracts a neutral symbol-content store and source-access capability below core
Queries. NuGet symbol lookup and package-source authorization remain in an
optional package/source companion that adapts to those contracts.

## Assembly boundary

The assembly layer begins when a consumer asks whether an artifact is a managed
assembly. It decodes managed metadata, mints assembly identity, and opens
`AssemblyInspectionSession`.

It accepts neutral content:

```text
artifact identity
acquisition registration
guarded OpenRead(admission or query access lease)
optional lease-scoped path to retained snapshot
```

It does not accept:

- package ids or versions;
- NuGet source options;
- package roots or entry paths;
- storage cache implementations;
- project restore models;
- CI provider clients.

Package, project, platform, and CI provenance may remain available beside the
assembly participant in the workspace. Metadata does not define or pattern
match those source-specific provenance variants.

## Workspace and query boundary

The workspace owns one or more artifact set sessions and one or more assembly
context groups. When an authorized query plan first demands a context, the
artifact owner issues its admission lease; the context loader constructs and
seals a session from all required acquisitions for that context, then creates
its group. Loading a definition alone performs none of that work. Retained hosts
may repeat the authorized operation to add contexts. Groups compose projected
assembly participants under one binding policy and may span artifact sources
within their session.

The execution path is:

```text
workspace
  -> plan demands an unrealized context
  -> artifact owner authorizes admission for that plan
  -> context loader seals artifact session and creates group
  -> execute typed query
  -> artifact owner authorizes this query plan and retained catalog generation
  -> owner issues query access lease
  -> select context group and participant
  -> query opens or borrows AssemblyInspectionSession
  -> inspection producers compute evidence
  -> query returns typed result and failure
```

The query owns session use. A host or presentation layer cannot open raw
readers and invoke producers around the query registry.

Operations that do not inspect assemblies remain narrower. Package metadata,
feed discovery, archive listing, and artifact inventory queries do not create
fake assembly participants merely to enter the workspace path.

## Project and dependency boundaries

Project references must enforce optionality. Runtime registration alone is
insufficient because an unused package implementation would still burden a
local-only application.

The target project graph has these roles:

```text
artifact contracts
  ^                 ^
  |                 |
storage impls   source adapters
                    |
       +------------+-------------+
       |            |             |
     local       package          CI
                    |
              package domain

artifact contracts --> Metadata --> core assembly Queries
artifact contracts --> workspace composition

full host --> core Queries + selected optional adapters/companions
local host --> core Queries + local adapter
```

Exact project names are deferred, but the split must produce these compile-time
properties:

1. artifact contracts reference no storage implementation, package domain, or
   assembly inspection project;
2. Metadata references no package domain, NuGet library, source adapter, or
   storage implementation;
3. core assembly workspace/query projects reference no package implementation;
4. package-specific queries live in an optional companion rather than forcing
   the package domain into core assembly queries;
5. package-aware composition references both sides through an adapter; neither
   Packages nor Metadata references the other;
6. package graph correspondence is validated and projected by the optional
   package companion rather than core assembly Queries;
7. platform correspondence is minted by the platform adapter and projected
   without a package or Metadata provenance dependency;
8. platform graph projection references neither package/NuGet implementations
   nor the package companion; an installed-platform adapter has the same
   closure, while an optional NuGet-backed remote-platform implementation
   reuses package acquisition without exposing it through the realization;
9. neutral symbol/PDB storage and source-access contracts do not reference
   package source policy;
10. hosts choose adapters through project references and capabilities.

## Current mismatches

Several current types are migration inputs, not target precedent:

- `AssemblySetRequest` carries packages, projects, platform inputs, directories,
  NuGet source options, package selection mode, and temporary-directory policy.
  `AssemblySetResolver` directly calls `PackageExtractor`.
- `AssemblySet` owns temporary package extraction directories.
- `DotnetInspector.Services` references `DotnetInspector.Packages` and
  `NuGet.Versioning`, so its full closure is not suitable as an assembly-only
  service layer.
- `DotnetInspector.Queries` references `DotnetInspector.Packages` directly.
- `WorkspaceContextLoader` realizes package and platform coordinates inside the
  core query project.
- `AssemblyContextSourceQueryContext` exposes package-owned `IPdbStore`,
  `IPackageSourceAuthorization`, and `NuGetSourceOptions` even for
  assembly-authored-source queries.
- `InspectionGraphPackageBoundary` validates package and platform
  correspondence by pattern matching Metadata-owned provenance and parsing
  package versions inside core Queries.
- `AssemblyResolutionProvenance` is defined by Metadata but enumerates package,
  project, platform, local, embedded, and caller-designated concepts. The
  `DesignatedAsset` arm also combines acquisition provenance with a trust-policy
  role.
- `MetadataContext.Open(string)` and `MetadataSource.OpenCore(string, ...)`
  treat a raw path as caller designation and grant core-library trust without
  consulting an admission role. That is current compatibility behavior, not
  the target meaning of a lease-scoped retained-snapshot path.
- `workspace-definitions.md` currently maps member kinds directly onto that
  closed Metadata provenance hierarchy.
- `type-forwarding-resolution.md` currently calls that hierarchy authoritative
  and gates the parameterless opener shape.
- `IPackageContent` provides path-optional package entry access, but also
  exposes `RootPath`, `NupkgPath`, and unguarded archive/entry openers for
  compatibility with current desktop consumers. It is a package-specific
  migration input, not the generic guarded artifact contract.

These types need not move in one change. The design requires each migration
slice to reduce the forbidden dependency closure rather than add another
source-specific case to the assembly layer.

## Migration

The migration is intentionally incremental:

1. **Land the design and closure gates.** Record the target forbidden
   dependencies and add a package-free closure canary before moving behavior.
2. **Extract source-neutral artifact contracts.** Introduce artifact identity,
   guarded content access, provenance marker, acquisition registration and
   outcome, admission/query authorization, quiescent lifetime, and lease
   contracts in a package- and Metadata-free project.
3. **Prove local acquisition.** Adapt explicit local files/directories into the
   contracts with admission-time content identity and form a workspace without
   any package reference. Preserve explicit caller designation as authorized
   workspace-role evidence rather than Metadata provenance.
4. **Extract neutral symbol capabilities.** Move PDB content storage and
   source-access authorization below core assembly Queries; keep NuGet symbol
   source policy in an optional companion.
5. **Separate workspace realization.** Move package/platform realization out of
   core assembly Queries into optional adapters or companion projects, and make
   retained workspaces own multiple sealed artifact sessions.
6. **Adapt package acquisition.** Reuse current package stores, source policy,
   package admission, and TFM selection behind a package artifact adapter.
7. **Move package correspondence.** Have the package adapter mint typed
   realization proofs and move package graph construction out of core assembly
   Queries while preserving the full host's graph wire contract.
8. **Move platform correspondence.** Have the platform adapter mint typed
   realization proofs and remove platform provenance/version parsing from core
   assembly Queries without pulling the package companion into platform
   projection. Keep installed-platform acquisition package-free; place any
   NuGet-backed remote-platform implementation with the optional package
   acquisition side.
9. **Retire package-aware assembly sets.** Replace package cases in
   `AssemblySetRequest` with host composition of artifact acquisitions and
   workspace groups.
10. **Migrate API source selection.** Select package assets in the package
   adapter, then pass neutral assembly artifacts to the existing assembly/query
   path.
11. **Add other adapters independently.** Project, platform, embedded, and CI
   adapters land only with their own typed coordinates, capabilities, limits,
   and provenance gates.

Each slice must preserve current visible diagnostics and selection semantics.
The migration does not justify a success-shaped fallback or an unbounded eager
materialization.

## Required gates

The target remains unverified until tests equivalent to these exist:

- `ArtifactContractsClosure_ExcludesMetadataPackagesAndStorageImplementations`
- `PackagesClosure_ExcludesMetadata`
- `AssemblyOnlyHostClosure_ExcludesPackageAndNuGetImplementations`
- `LocalOnlyHostClosure_ExcludesPackageFeedCacheAndArchiveImplementations`
- `MetadataClosure_ExcludesPackageAndStorageImplementations`
- `CoreAssemblyQueries_ExcludePackageImplementations`
- `CoreAssemblySourceQueries_ExcludePackageSymbolCapabilities`
- `PlatformProjectionClosure_ExcludesPackageNuGetAndPackageCompanion`
- `InstalledPlatformAdapterClosure_ExcludesPackageAndNuGetImplementations`
- `ArtifactSetSession_ComposesArtifactsFromMultipleSources`
- `ArtifactSetSession_SealedGenerationCannotMutate`
- `ArtifactIdentity_IsScopedToOwningGeneration`
- `DesignatedArtifactTrust_RequiresAuthorizedAdmissionRole`
- `PlatformArtifactTrust_RequiresAuthorizedAdmissionRole`
- `LeaseScopedPath_IsNotADesignationGrant`
- `ArtifactSetSession_DisposesEveryContributingLease`
- `ArtifactSetSession_ReleasesLeasesOnlyAfterDependentGroupsQuiesce`
- `ArtifactSetSession_PreservesPrimaryFailureWhenCleanupFails`
- `WorkspaceDisposal_CancelsAdmissionAndDisposesLateOutcome`
- `BrowserWorkspace_DisposalDuringAwaitedAdmissionCannotPublish`
- `ArtifactAdmission_ProjectsAssembliesThroughAuthorizedLease`
- `AdmissionLease_CannotOpenContentAfterGroupPublication`
- `ArtifactAccess_RejectsChangedOrRevokedQueryAuthorization`
- `ArtifactCatalog_RejectsRevokedPolicyBeforeParticipantSelection`
- `ArtifactCatalog_NarrowedPolicyCannotReusePriorGeneration`
- `DefinitionLoadAndScenarioResolution_PerformNoAcquisition`
- `ArtifactDescriptor_ExposesNoUnguardedContentRoute`
- `ArtifactOpen_RejectsContentSubstitutionAfterAdmission`
- `LocalArtifactSnapshot_MutationCannotChangeInspectionBytes`
- `ArtifactAcquisition_CancellationRemainsCancellation`
- `RequiredMember_EmptyOrNonProjectableAcquisitionFailsContext`
- `RequiredAcquisitionFailure_DoesNotShortenWorkspaceContext`
- `AssemblyContextGroup_CanBindParticipantsFromDifferentArtifactSources`
- `RetainedWorkspace_CanAddASecondSealedContextGeneration`
- `PackageAdapter_ProjectsSelectedEntriesWithoutLeakingPackageTypes`
- `PackageGraphProjection_UsesAdapterOwnedCorrespondence`
- `PlatformGraphProjection_UsesAdapterOwnedCorrespondence`
- `RemotePlatformPack_UsesPackageMappingVersionAndProducerAuthorization`
- `RemotePlatformPack_RejectsUnauthorizedOrNarrowedProducerCache`
- `LocalOnlyWorkspace_ExecutesAssemblyQueryWithoutPackageCapabilities`
- `CiArtifactScenario_PreservesProviderRunCommitAndDigestProvenance`
- `CrossProviderCiArtifacts_CompareAcrossSealedAuthorizedContexts`
- `BrowserWorkspace_ComposesSequentiallyWithoutFilesystemOrThreads`

The first nine are structural edge/closure gates derived from the actual project
graph, not a hand-maintained allow list. The remainder are behavior and lifetime
gates. The local-only query gate covers metadata and authored-source query
families so a metadata-only success cannot hide package-owned source
capabilities. The browser gate runs the same composition sequentially without
threads, blocking waits, or a filesystem.

## Non-goals

- Defining a universal package session.
- Treating every archive as a package.
- Making storage infer semantic identity from filenames or paths.
- Replacing assembly context groups with artifact sets; artifact lifetime and
  assembly binding remain separate axes.
- Requiring every workspace artifact to be an assembly.
- Scraping arbitrary deployed Wasm applications for runtime assemblies. A
  cooperating application may supply an explicit manifest or adapter, but
  framework-version-specific boot-resource discovery is not a general source
  contract.
- Implementing Azure DevOps or GitHub Actions acquisition in the first slice.
- Changing user-visible CLI commands in the design PR.
