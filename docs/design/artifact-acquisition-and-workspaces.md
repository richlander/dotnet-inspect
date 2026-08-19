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
  |                                 ArtifactSetSession
  |                                   owned by workspace
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
| Artifact | One immutable inspectable content item | logical identity, media/kind hint, digest, source-neutral opener | package or assembly policy |
| Artifact acquisition | One adapter's typed attempt to contribute artifacts | outcomes, diagnostics, provenance, content leases | workspace binding |
| Artifact source adapter | Resolves one source-specific coordinate | source protocol, authorization, listing, archive rules | inspection queries |
| `ArtifactSetSession` | Live, stable set of artifacts admitted to one workspace | all child acquisition leases and artifact handles | source-specific resolution or assembly binding |
| Workspace | Logical inspection composition | contexts, roles, query plans, authorization snapshot | feed or archive mechanics |
| Assembly context group | One binding-consistent universe | participants, binding policy, retained assembly snapshots | package acquisition |
| Resolved assembly reference | Neutral handle for one selected managed assembly | assembly identity and repeatable content opener | package coordinate parsing or storage implementation |
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

The artifact contract supplies only a source-neutral provenance marker and an
owner-issued artifact identity. Each adapter defines its own provenance record:
package coordinates and producer key in the package companion, CI run and
artifact ids in a CI companion, and local path/fingerprint evidence in the
local adapter. The workspace retains that typed record and correlates it with
the artifact identity.

Assembly projection carries the artifact identity, not a Metadata-owned union
of every possible source kind. Adapter-aware queries or hosts can ask the
workspace for the source-specific provenance record through the adapter's typed
contract. Assembly-only consumers can preserve and compare the correlation
identity without referencing or interpreting package or CI types.

This is not an untyped property bag. Adding a new source requires a typed
provenance record and its owning projection/serialization code, but does not
require changing Metadata.

## `ArtifactSetSession`

`ArtifactSetSession` is the workspace's acquisition lifetime and consistency
boundary. The name describes a role; the first implementation may fold the role
into `InspectionWorkspace` rather than add a second public container with the
same lifetime.

The session:

1. accepts typed artifact-acquisition outcomes from multiple adapters;
2. retains every admitted source-specific content lease;
3. exposes a stable, source-neutral artifact catalog;
4. preserves provenance on each artifact;
5. rejects identity collisions or represents them explicitly;
6. makes acquisition failures visible to workspace construction;
7. releases all child leases when the workspace is disposed.

The session has a construction phase and a sealed phase. Adapters contribute
only during construction. Queries observe only the sealed catalog; adding,
removing, or replacing an acquisition creates a new session generation rather
than mutating the one queries use.

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
- a context cannot silently omit a failed required member;
- an optional acquisition must be declared optional by the workspace
  definition, not inferred from failure;
- disposal failure cannot replace the primary acquisition or inspection
  failure;
- cancellation remains cancellation.

## Source adapters

An artifact source adapter owns the semantics needed to resolve its coordinate.
It returns artifacts and leases; it does not create an assembly session.

### Local adapter

The local adapter accepts explicit files or directories under host policy. It
opens local content without acquiring package or remote-storage dependencies.
Directory enumeration and path containment remain local-adapter concerns.

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

### Project adapter

The project adapter interprets restore outputs and project build products. A
restored package asset may retain package provenance, but the adapter—not the
assembly layer—understands `project.assets.json`, package roots, and restore
layout.

### Platform adapter

The platform adapter resolves installed or remotely acquired platform content.
Platform packs may happen to be transported as NuGet packages, but transport
does not make "package" the workspace model. The adapter retains both platform
identity and the producer authorization needed for re-acquisition.

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
7. retain the download/archive lease for the workspace lifetime.

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

A storage lease may expose a repeatable stream opener, a bounded buffer, or a
contained local path when the implementation has one. Consumers cannot require
the path form. Storage does not:

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

## Assembly boundary

The assembly layer begins when a consumer asks whether an artifact is a managed
assembly. It decodes managed metadata, mints assembly identity, and opens
`AssemblyInspectionSession`.

It accepts neutral content:

```text
artifact identity
repeatable OpenRead
optional contained local path
source-neutral provenance handle
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

The workspace owns the artifact set session and one or more assembly context
groups. Groups compose projected assembly participants under one binding policy.
They may span artifact sources.

The execution path is:

```text
workspace
  -> select context group and participant
  -> execute typed query
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
6. hosts choose adapters through project references and capabilities.

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
- `AssemblyResolutionProvenance` is defined by Metadata but enumerates package,
  project, platform, local, and embedded source concepts.
- `IPackageContent` correctly provides a pathless package-content seam, but it
  is package-specific and must not become the generic artifact contract.

These types need not move in one change. The design requires each migration
slice to reduce the forbidden dependency closure rather than add another
source-specific case to the assembly layer.

## Migration

The migration is intentionally incremental:

1. **Land the design and closure gates.** Record the target forbidden
   dependencies and add a package-free closure canary before moving behavior.
2. **Extract source-neutral artifact contracts.** Introduce artifact identity,
   content opener, provenance handle, acquisition outcome, and lease contracts
   in a package- and Metadata-free project.
3. **Prove local acquisition.** Adapt explicit local files/directories into the
   contracts and form a workspace without any package reference.
4. **Separate workspace realization.** Move package/platform realization out of
   core assembly Queries into optional adapters or companion projects.
5. **Adapt package acquisition.** Reuse current package stores, source policy,
   package admission, and TFM selection behind a package artifact adapter.
6. **Retire package-aware assembly sets.** Replace package cases in
   `AssemblySetRequest` with host composition of artifact acquisitions and
   workspace groups.
7. **Migrate API source selection.** Select package assets in the package
   adapter, then pass neutral assembly artifacts to the existing assembly/query
   path.
8. **Add other adapters independently.** Project, platform, embedded, and CI
   adapters land only with their own typed coordinates, capabilities, limits,
   and provenance gates.

Each slice must preserve current visible diagnostics and selection semantics.
The migration does not justify a success-shaped fallback or an unbounded eager
materialization.

## Required gates

The target remains unverified until tests equivalent to these exist:

- `AssemblyOnlyHostClosure_ExcludesPackageAndNuGetImplementations`
- `MetadataClosure_ExcludesPackageAndStorageImplementations`
- `CoreAssemblyQueries_ExcludePackageImplementations`
- `ArtifactSetSession_ComposesArtifactsFromMultipleSources`
- `ArtifactSetSession_DisposesEveryContributingLease`
- `ArtifactSetSession_PreservesPrimaryFailureWhenCleanupFails`
- `RequiredAcquisitionFailure_DoesNotShortenWorkspaceContext`
- `AssemblyContextGroup_CanBindParticipantsFromDifferentArtifactSources`
- `PackageAdapter_ProjectsSelectedEntriesWithoutLeakingPackageTypes`
- `LocalOnlyWorkspace_ExecutesAssemblyQueryWithoutPackageCapabilities`
- `CiArtifactScenario_PreservesProviderRunCommitAndDigestProvenance`

The first three are structural closure gates. The remainder are behavior and
lifetime gates. A browser-compatible implementation must run the same
composition sequentially without requiring threads, blocking waits, or a
filesystem.

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
