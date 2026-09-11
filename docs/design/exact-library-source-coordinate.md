# Exact library source coordinate

## Status and approved scope

This document is the normative owner for the resource-free exact-library
source coordinate tracked by
[#6609](https://github.com/richlander/dotnet-inspect/issues/6609). It is the
remaining source-coordinate prerequisite for inert Workspace registration in
[#6577](https://github.com/richlander/dotnet-inspect/issues/6577), within the
experience plan in
[#6012](https://github.com/richlander/dotnet-inspect/issues/6012).

The first production consumer is the `ExactLibrary` arm of the
[Workspace Registration and Call-Graph Focal Length](workspace-registration-and-call-graph-scope.md)
vocabulary. That consumer retains this value without acquiring or admitting
the named Library.

The coordinate contract and Source Selection value are implemented by
`DotnetInspector.SourceSelection.ExactLibrarySourceCoordinate`. Workspace
retention, portable restoration, realization, and host adoption remain in
their separately owned slices.

## Authority and exact claim

**Exact Library Source Coordinate** owns:

> Identify one exact managed Library within either one exact NuGet package
> coordinate or one declared Platform library population, while retaining the
> source domain and exact Metadata assembly identity before target/view
> selection, source authorization, acquisition, Workspace admission, analysis,
> or traversal.

The coordinate is source-aware and resource-free. It says which Library is
relevant and which source domain must interpret that relevance. It does not
claim that the Library is available for a later requested target or view.

This owner defines:

- the closed package and Platform coordinate arms;
- the lower owner-issued values retained by each arm;
- exact Library equality within one source domain;
- the rule that equal assembly identity across source domains remains
  distinct;
- construction, immutability, and non-action; and
- the handoff boundary to Workspace registration and later source realization.

It does not define:

- NuGet package-coordinate or Metadata assembly-identity grammar;
- Platform family or population-declaration identity;
- target framework, runtime identifier, version roll-forward, or view policy;
- package asset selection or Platform population realization;
- source authorization, transport, caching, acquisition, or admission;
- assembly binding, package/platform pruning, or provenance;
- Workspace revision, persistence, editing, or call-graph behavior; or
- CLI or browser presentation.

## Why Source Selection owns the coordinate

`DotnetInspector.SourceSelection` owns immutable source intent that is valid
before realization. `PackagePrefixDeclaration` identifies a package discovery
population, and `PlatformLibraryPopulationDeclaration` identifies a logical
Platform population. An exact-library coordinate is the single-Library member
counterpart: it retains one exact source domain and one exact managed assembly
identity without selecting an execution policy.

The alternatives carry the wrong semantics:

- `WorkspaceMemberCoordinate` and `RealizedMemberCoordinate` are Queries-owned
  acquisition inputs and results. They retain targets, producers, or realized
  context rather than reusable source intent.
- `PackageCompileAsset` is one target-specific selected asset occurrence. Its
  path and compile/reference role are realization evidence, not the logical
  Library coordinate across later view selection.
- `PackageHouseLibraryHandoff` and acquired Navigation Library subjects exist
  only after source work and admission.
- `PlatformLibraryIdentity` is an opaque PlatformHouse operation identity. Its
  diagnostic name is explicitly not an assembly coordinate.
- `AssemblyResolutionProvenance` records how one physical candidate was
  selected. Provenance does not say which source route a future registration
  requests.
- a filename, assembly simple name, namespace, package display label, or
  ecosystem title cannot establish source or Library identity.

The coordinate therefore belongs beside other Source Selection declarations.
It composes existing lower values without transferring their construction or
validation into this owner.

## Contract shape

The version-1 coordinate is a closed typed union:

```text
ExactLibrarySourceCoordinate
  = Package(
      PackageSourceCoordinate,
      ManagedMetadataIdentity.Assembly)
  | Platform(
      PlatformLibraryPopulationDeclaration,
      ManagedMetadataIdentity.Assembly)
```

The package arm retains:

- one exact normalized `PackageSourceCoordinate`, including package ID and
  version; and
- one exact managed assembly identity issued from Metadata.

The Platform arm retains:

- one `PlatformLibraryPopulationDeclaration`, including its exact
  `PlatformFamily`; and
- one exact managed assembly identity issued from Metadata.

The Metadata value is an assembly definition identity, not a partial assembly
reference pattern. Modules are not version-1 Library coordinates. The
coordinate does not accept a free-form name and does not parse an assembly
display string. Public construction rejects an identity without an assembly
version, so a name-only reference pattern cannot enter this exact coordinate.
Metadata-equivalence rules govern comparison; candidate-matching wildcard
semantics do not.

The package coordinate deliberately does not retain an authorized source,
producer, target framework, runtime identifier, asset path, or compile/runtime
role. Those values answer which bytes or view one operation may use. They are
not stable identity for the relevant Library within the exact package version.

The Platform coordinate deliberately does not retain a target framework,
Platform version, installed hive, pack path, reference/implementation view, or
source capability. The declared family and assembly identity remain stable
input while a later operation chooses a target and may return typed no-match or
unavailable evidence.

## Exact identity and equality

Coordinate equality has two parts:

1. the source arm and its source-owner value must be equal; and
2. Metadata's assembly-identity equivalence must say the Library identities
   are equal.

Package and Platform arms are unequal even when their assembly identities are
equivalent. Two package coordinates are unequal when their exact package IDs
or versions differ. Two Platform coordinates are unequal when their declared
families differ.

Assembly name, culture, and public-key-token comparison follows Metadata's
existing case-insensitive equivalence; null, empty, and `neutral` culture
spellings follow that owner's equivalence; version remains exact. This owner
invokes the Metadata comparer rather than copying or redefining its grammar.
The retained Metadata value remains available as issued for diagnostics and
later owner codecs.

Hashing follows the same source-arm and Metadata-equivalence rules. Equality
does not consult files, package contents, installed packs, or network state.

## A logical Library, not one selected asset

One source coordinate can be realized through different views:

```text
Package(System.Text.Json@11 preview, System.Text.Json 11.0.0.0)
  + net10.0
  + Reference
      -> ref/net10.0/System.Text.Json.dll or typed non-success

Package(System.Text.Json@11 preview, System.Text.Json 11.0.0.0)
  + net10.0
  + Implementation
      -> lib/runtime-selected System.Text.Json.dll or typed non-success
```

The coordinate does not assert that every view exists or that reference and
implementation bytes are identical. A source-realization owner selects one
request-specific asset and preserves correspondence back to the coordinate.

The same applies to Platform:

```text
Platform(DotNetRuntime, System.Text.Json 11.0.0.0)
  + exact target
  + Reference
      -> one matching DotNetRuntime focus Library or typed non-success

Platform(DotNetRuntime, System.Text.Json 11.0.0.0)
  + exact target
  + Implementation
      -> one matching DotNetRuntime focus Library or typed non-success
```

A target whose focus population does not contain that exact assembly identity
is a visible no-match or unavailable result. A support-closure member cannot
satisfy the coordinate merely because it was realized beside the focus
population. The source preserves the population owner's focus attribution; the
coordinate does not infer it from an assembly name, path, or dependency edge.
The coordinate does not roll forward the assembly identity or silently select
a same-name neighbor.

## Source-domain distinction

Package and Platform routes can contain equivalent assembly identities without
becoming one coordinate:

```text
Package(
  System.Text.Json@11.0.0-preview.7.26381.103,
  System.Text.Json, Version=11.0.0.0, ...)

Platform(
  DotNetRuntime,
  System.Text.Json, Version=11.0.0.0, ...)
```

These values answer different questions. The package arm asks the package
source domain to realize the Library from that exact package coordinate. The
Platform arm asks the Platform source domain to realize it from the declared
family. Neither route is fallback permission for the other.

This distinction is required for high-fidelity package inspection. A caller
that registers the package coordinate does not silently opt into Platform
pruning or substitution. A discovery-oriented caller may instead select an
ecosystem or Platform population registration.

## Construction and non-action

Construction is:

- immutable and deterministic;
- allocation-bounded;
- limited to retaining non-null owner-issued values;
- independent of source authorization and host capabilities;
- free of filesystem, cache, package, SDK, and network access; and
- valid for CLI and Browser/Wasm consumers.

There is no empty, unknown, local-path, embedded-content, project, or
display-name arm. Adding another source domain requires a new version of this
closed contract and its consumers; unknown input cannot become a generic
string-backed success.

Constructing or comparing a coordinate does not:

- query package sources or installed Platform hives;
- inspect package manifests or assembly bytes;
- choose a target framework, view, producer, or source;
- create a Workspace Root or participant;
- acquire, cache, admit, analyze, or traverse content;
- activate pruning or binding policy; or
- prove that later realization will succeed.

## Real motivating evidence

The motivating package is
[`System.Memory.Data@11.0.0-preview.7.26381.103`][memory-data]. Its
`lib/net10.0/System.Memory.Data.dll` references
`System.Text.Json, Version=11.0.0.0`. The exact
`System.Text.Json@11.0.0-preview.7.26381.103` package and the corresponding
`Microsoft.NETCore.App.Ref@11.0.0-preview.7.26381.103` population both contain
assemblies with that ECMA-335 identity, while their module identities and
source routes differ.

The repository's
`OccurrenceRootedParticipant_RealPackageTopologyPreservesGlobalCompositionContinuation`
gate already proves the equal-identity, physically distinct package and
Platform candidates from those pinned real assets. The exact-library
coordinate implementation preserves the same two source routes as distinct
resource-free values in
`RealPackageAndPlatformAssembliesRemainDistinctCoordinates`. That focused gate
supplements rather than replaces the existing composition test.

[memory-data]: https://www.nuget.org/packages/System.Memory.Data/11.0.0-preview.7.26381.103

## Pathological cases

### Equal Metadata identity in package and Platform

Package and Platform values retain equivalent `System.Text.Json` assembly
identities. They remain unequal because the source arms differ. A set of
registrations retains both.

### Same Library in two package versions

Two exact package coordinates retain the same assembly identity at different
package versions. They remain distinct. Version-selection or replacement
policy does not run during coordinate comparison.

### Same simple name, different assembly identity

Two values have the same assembly name but different version, culture, or
public-key token. Metadata equivalence keeps them distinct. No simple-name
fallback occurs.

### Alternate equivalent assembly spelling

Two owner-issued Metadata identities differ only by casing or neutral-culture
spelling covered by Metadata equivalence. They compare equal within the same
source coordinate. The source value remains retained as issued.

### Assembly reference pattern instead of definition identity

A caller supplies a partial reference pattern rather than an exact
Metadata-owned assembly definition identity. That caller has not met the
construction precondition. Public restoration must use the Metadata owner's
exact identity codec; this coordinate does not invent a second parser or repair
the input.

### Target lacks the exact Library

A later operation selects a target or view that cannot realize the exact
identity. The source returns typed no-match, unavailable, incomplete, or failed
evidence under its owner. The registration remains valid and does not broaden
to a same-name Library.

### Platform support closure contains the identity

An ASP.NET Core realization contains a .NET runtime Library only as support
closure. That Library cannot satisfy an `AspNetCore` exact-library coordinate.
Only a focus member attributed to the retained Platform population declaration
can satisfy it; a separately registered `DotNetRuntime` coordinate may select
the runtime Library.

## Analogous implementation evidence

NuGet distinguishes an exact package coordinate from the compile and runtime
assets selected for a target. Its documented package layout allows reference
assets under `ref/<tfm>`, implementation assets under `lib/<tfm>`, and
RID-specific runtime assets under `runtimes/<rid>/lib/<tfm>`. The useful
transferred property is that one package identity can contain multiple
target/view representations of a Library; an exact Library declaration should
not persist one selected asset path as its logical identity.

.NET `FrameworkReference` similarly names a logical shared framework while the
SDK later resolves target-specific reference and implementation assets. The
useful transferred property is separation of logical Platform source intent
from one installed path or selected view.

This design does not transfer NuGet dependency resolution, MSBuild item
evaluation, compile/runtime asset fallback, hostfxr roll-forward, or assembly
load behavior. Those implementations are evidence for separation, not
authority for this coordinate.

## Ownership and adoption

| Owner | Responsibility retained |
| --- | --- |
| NuGet package source contracts | Exact normalized package ID/version coordinate |
| Metadata | Exact managed assembly definition identity and equivalence |
| Platform population declaration | Exact logical Platform family population |
| This Source Selection owner | Closed source arms, composition, equality, immutability, and non-action |
| Workspace registration | Ordered retention, revisions, duplicate handling, and operation selection |
| Workspace Definitions | Canonical portable codec and restoration validation |
| Package and Platform realization | Target/view selection, source work, correspondence, completion, and failures |
| Metadata binding | Candidate identity, binding, forwarding, and resolution |
| CLI and Browser/Wasm | Explicit registration intent and presentation |

Five counted production-adoption steps reach both hosts:

1. Lock this focused coordinate contract.
2. Implement the Source Selection value and focused Release gates.
3. Retain it in Workspace registration revisions and Workspace Definitions.
4. Let CLI raw/high-fidelity consumers explicitly register exact Libraries.
5. Let Browser/Wasm editing and restoration consume the same coordinate.

Steps 1 and 2 may land together under the design-scope exception for one
cross-cutting pattern and its first adopting owner. Workspace, Definitions,
CLI, and Browser adoption remain focused follow-ups under #6012 and #6570.

The coordinate adds no rendering surface, so Markout and host-specific
rendering strategy do not apply.

## Required gates

| Gate | Required observation |
| --- | --- |
| Closed public contract | Public construction exposes only package and Platform arms over the named owner-issued values. |
| Resource-free construction | Creating, comparing, and hashing coordinates performs no source, filesystem, cache, SDK, network, Workspace, or Metadata-byte work. |
| Exact source retention | Each arm exposes the same package or Platform population value and exact Metadata assembly identity supplied by the caller. |
| Cross-source distinction | Equivalent package and Platform assembly identities remain distinct, including the pinned real `System.Text.Json` case. |
| Same-source equality | Source-equal values use Metadata's existing assembly-identity equivalence and hash consistently. |
| Neighbor distinction | Package version, Platform family, assembly version, culture, and public-key-token differences remain distinct under their owners' equality. |
| Exact definition admission | Public construction rejects a versionless assembly-reference pattern. |
| Public consumer | A non-friend consumer constructs, stores, compares, and pattern-matches both arms. |
| Workspace retention | One revision preserves exact order and coordinates without acquisition or participant mutation. |
| Portable restoration | Definitions round-trip the semantic coordinate without serializing path, source client, producer, MVID, provenance, artifact identity, or generation. |
| Host adoption | CLI and Browser/Wasm submit the same typed coordinate rather than reconstructing it from display text. |

The first eight gates belong to this coordinate's implementation slice. The
last three remain unverified until their focused owners land. The real pinned
asset gate may reuse the repository's existing package/reference-pack fixture;
synthetic values remain appropriate for impossible valid-product boundaries.

No source-code absence scan is required. Positive public-consumer and
resource-free behavior gates enforce this slice without treating trusted
contributors as hostile.

## TLA+ assessment

No TLA+ model is required. The coordinate is one immutable value with no
publication, revision, replacement, scheduling, concurrency, or failure-state
transition. Workspace revision modeling remains with the Workspace owner when
that owner retains this value.

## Non-claims

This design does not define:

- a Library catalog or discovery index;
- a generic source plugin or extensible source discriminator;
- local-file, project, embedded, native, WinMD, or netmodule registration;
- package source selection, producer identity, target, RID, or asset path;
- Platform target/version/view/source selection;
- reference-to-implementation correspondence;
- source fallback, package/platform substitution, or pruning;
- acquisition, cache, admission, binding, analysis, or call graphs;
- Workspace defaults, curation, revisions, persistence, or editing; or
- CLI or browser rendering.
