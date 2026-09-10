# Installed reference-pack realization

## Status and approved scope

This document is the normative owner for package-free discovery and immutable
realization of reference packs from one explicit dotnet hive. It is the
reference half of production-adoption step 4 in
[PlatformHouse Realization and Reference Processing](platform-house-reference-processing.md)
and is tracked by
[#6012](https://github.com/richlander/dotnet-inspect/issues/6012).

The first production consumer is
`DotnetInspector.PlatformHouse.Installed`, which translates an authorized
PlatformHouse request into this source owner's coordinates and returns the live
source result beside a resource-free House contribution.

This is step 4a within the existing eleven-step adoption sequence. It does not
complete step 4. Installed implementation-platform realization remains owned
by
[Platform Composition and Overlays](platform-composition-and-overlays.md#installed-implementation-platform-realization)
and requires the manifest-defined closure described there before it can
contribute authoritative implementation evidence.

## Authority and exact claim

**Installed Reference-Pack Realization** owns:

> Given one host-selected dotnet hive, one installed platform family, one
> canonical base target framework, and finite work bounds, discover the exact
> installed targeting-pack versions that contain that framework or atomically
> snapshot the exact requested reference-pack population.

The owner establishes:

- the installed reference-pack coordinate and family-to-pack projection;
- version and target-framework discovery within one explicit hive;
- reference-pack population membership;
- exact-library coordinate projection;
- immutable assembly snapshots and Metadata identity validation;
- source generations and typed terminal outcomes; and
- the work and cancellation boundary for installed reference acquisition.

It does not establish:

- which dotnet hive a host should choose;
- a platform target or version-selection policy;
- source precedence, fallback, or aggregation;
- installed shared-framework or implementation-pack membership;
- reference-to-implementation correspondence;
- package-backed targeting packs;
- Workspace admission or replacement;
- assembly binding, forwarding, type resolution, or documentation lookup; or
- CLI or browser presentation.

## Normative basis and supporting evidence

The .NET targeting-pack design defines targeting packs as compile-time
reference assets and places framework-specific reference assemblies beneath a
pack's `ref/<tfm>` directory:

- [.NET targeting packs and runtime packs](https://github.com/dotnet/designs/blob/main/accepted/2019/targeting-packs-and-runtime-packs.md)
- [.NET distribution packaging](https://learn.microsoft.com/dotnet/core/distribution-packaging)

Those sources establish the ecosystem layout. This design owns the narrower
product contract: which entries dotnet-inspect treats as the source population,
how it snapshots them, and what it may claim to PlatformHouse.

The existing `PlatformResolver` is supporting evidence, not the owner. It
demonstrates current product knowledge of targeting-pack locations, but it also
mixes installed and downloaded roots, collapses source correspondence, and
returns path-backed results. This owner does not wrap that behavior.

## Boundary and dependency direction

The source implementation lives in
`DotnetInspector.Platforms.Installed`.

```text
DotnetInspector.Platforms
ILInspector.Metadata
        |
        v
DotnetInspector.Platforms.Installed
        |
        v
DotnetInspector.PlatformHouse.Installed
        |
        v
DotnetInspector.PlatformHouse
```

`DotnetInspector.Platforms.Installed` may depend on the package-neutral target
currency and Metadata identity projection. It must not reference:

- `DotnetInspector.Packages` or NuGet implementations;
- `DotnetInspector.Services`;
- Source Selection, Workspaces, Queries, CLI, or Browser;
- package caches or package-source configuration; or
- inspected-assembly loading or Roslyn.

`DotnetInspector.PlatformHouse.Installed` is the integration boundary above
both owners. It translates House target currency into installed-source
coordinates, validates House capability authorization, and pairs live source
values with resource-free `PlatformSourceContribution` evidence.

The source owner remains usable without PlatformHouse. PlatformHouse does not
learn installed paths or source implementation types.

## Installed source identity and coordinate

One source instance is bound to:

```text
InstalledReferencePackSource(
  hive: InstalledDotnetHiveIdentity,
  dotnetRoot: explicit host-selected path)
```

The host chooses the root. The source does not search `DOTNET_ROOT`, SDK
locations, package caches, or other ambient roots.

An exact source coordinate is:

```text
InstalledReferencePackCoordinate(
  hive,
  installed family,
  target framework,
  exact canonical SemVer)
```

The installed family projection is closed:

| Installed family | Pack coordinate |
| --- | --- |
| `DotNetRuntime` | `Microsoft.NETCore.App.Ref` |
| `AspNetCore` | `Microsoft.AspNetCore.App.Ref` |

For coordinate `(hive, family, tfm, version)`, the exact population root is:

```text
<dotnet-root>/packs/<pack>/<version>/ref/<tfm>/
```

Paths and pack names are source coordinates. They are not
`PlatformFamilyTarget` identity.

## Target discovery

Discovery receives one installed family, one canonical base TFM, and a maximum
candidate count.

It:

1. examines only the selected hive's exact pack root;
2. observes version directories under a finite source-owned entry limit;
3. retains a directory only when its exact `ref/<tfm>` directory exists;
4. accepts only canonical SemVer directory names whose major and minor match
   the requested TFM;
5. sorts candidates by SemVer precedence and then exact version identity; and
6. returns one immutable inventory tied to one source generation.

An absent pack root produces a successful empty inventory. It is authoritative
evidence that this source generation supplied no candidate; it is not evidence
that another source lacks the target.

A matching `ref/<tfm>` directory beneath a non-canonical version is rejected
rather than silently omitted. Exceeding the observation or candidate limit is
incomplete rather than a shortened successful inventory.

The PlatformHouse bridge converts each exact source candidate to
`PlatformFamilyTarget` and retains the source generation and evidence identity.
Selection policy remains House-owned.

## Reference population membership

For one exact coordinate, the complete installed reference population is every
top-level file with a case-insensitive `.dll` extension beneath the exact
`ref/<tfm>` directory.

Subdirectories, XML documentation, analyzers, and non-DLL files are outside
this population. The rule is source-owned; consumers must not reconstruct
membership from a returned path or assembly-name prefix.

Every selected DLL must:

- fit the per-file and aggregate byte bounds;
- be copied into immutable source-owned memory;
- contain ECMA-335 metadata;
- be an assembly rather than a netmodule;
- not be Windows Metadata; and
- have a Metadata-projected assembly identity distinct from every other
  population member.

Any selected member that fails those conditions rejects the whole population.
No shortened success is returned.

## Exact-library realization

An exact-library demand uses one complete Metadata
`AssemblyReferenceIdentity`. The installed source projects the simple assembly
name to `<name>.dll` beneath the exact population root, snapshots that file,
and verifies that the decoded assembly identity is equivalent to the request.

The file-name projection is only a coordinate optimization. The Metadata
identity check is the completion gate. An absent file is source absence; an
identity mismatch is invalid installed-layout evidence.

An opaque PlatformHouse `PlatformLibraryIdentity` cannot be interpreted as a
file or assembly name. The bridge rejects that request until a source-issued
library-identity correspondence exists.

## Immutable values, generations, and resource-free evidence

Each source attempt issues a fresh
`InstalledPlatformSourceGeneration`.
Successful realization returns live `InstalledReferenceLibrary` values whose
content is a private immutable byte snapshot. Opening a library reads that
snapshot; it never reopens the installed path.

The bridge issues:

- one `PlatformSourceGeneration` corresponding to the source attempt;
- one `PlatformSourceCoordinateIdentity` corresponding to the exact installed
  coordinate;
- one `PlatformTargetCorrespondenceIdentity` joining that coordinate to the
  exact House target; and
- one `PlatformSourceEvidenceIdentity` for the contribution.

These identities are resource-free. The House contribution and receipt do not
retain dotnet-root paths, streams, buffers, or live source objects.

## Failure and work semantics

The source outcome is closed:

- `Succeeded` retains the immutable inventory or realization;
- `Unavailable` distinguishes exact absence from a temporarily unavailable
  source;
- `Rejected` records invalid coordinates, layout, or member evidence;
- `Incomplete` records a finite work bound reached before completion; and
- `Failed` records an I/O failure.

Cancellation remains cancellation and is not converted into another result.
The House bridge enforces its source-operation and duration budget before or
during source work and maps a duration expiry to an incomplete contribution.

The source bounds:

- observed version or population entries;
- discovered candidates;
- realized assembly count;
- bytes per file;
- aggregate realized bytes; and
- cancellation.

No limit failure produces a partial successful population.

## Platform compatibility

The installed source adapter supports Windows, Linux, and macOS. It is
intentionally unavailable in Browser/Wasm because it requires a host-selected
local dotnet hive and filesystem access.

The host-neutral target currency and PlatformHouse contracts remain
Browser/Wasm compatible. Browser uses separately authorized package-backed,
generated-catalog, or embedded sources. Browser must not reference the
installed source or its House bridge.

## Production adoption

This slice advances the existing eleven-step PlatformHouse adoption sequence:

1. Steps 1-3 established target currency, the House design, and the contract
   seam.
2. Step 4a adds installed reference target discovery, immutable reference-pack
   realization, and the package-free House bridge.
3. Step 4b must implement the manifest-defined installed implementation
   closure and reference-to-implementation source correspondence.
4. Steps 5-11 remain unchanged.

Step 4 is complete only when both 4a and 4b are implemented and reviewed. No
production host switches from `PlatformResolver` in 4a; later House adoption
selects this capability through the existing host-neutral source plan.

## Evidence gates

The focused executable suite
`DotnetInspector.PlatformHouse.Installed.Tests` proves:

- explicit-root, family-specific, TFM-specific discovery;
- deterministic canonical candidate ordering;
- candidate and observation limit behavior;
- atomic complete-population rejection;
- immutable post-acquisition snapshots;
- exact assembly-name projection plus Metadata identity validation;
- authoritative House contribution construction;
- source-plan authorization; and
- rejection of opaque library identities.

Ordinary CI runs the focused suite. The normal solution build and dependency
policy gates enforce the project graph.

## Demo

Given:

```text
<dotnet-root>/packs/Microsoft.NETCore.App.Ref/11.0.0/ref/net11.0/
  System.Runtime.dll
  System.Text.Json.dll
```

an authorized discovery request for `DotNetRuntime + net11.0` contributes:

```text
PlatformFamilyTarget(
  DotNetRuntime,
  net11.0,
  11.0.0)
```

and a complete reference realization returns immutable snapshots of both
assemblies plus one authoritative `PlatformSourceContribution.Realization`.
Removing or replacing the files afterward does not change the returned live
value. Requesting a complete implementation population still has no installed
adapter in this slice and cannot be presented as supported.
