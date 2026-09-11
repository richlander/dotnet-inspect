# Package-backed Platform realization

## Status and approved scope

This document is the normative owner for package-backed Platform target
discovery and reference/implementation pack realization. It is production-
adoption step 5 in
[PlatformHouse realization and reference processing](platform-house-reference-processing.md)
and is tracked by
[#6561](https://github.com/richlander/dotnet-inspect/issues/6561).

The implementation is staged without changing the eleven-step count:

1. **Step 5a** adds package-backed target discovery, immutable reference-pack
   realization, and a thin PlatformHouse adapter.
2. **Step 5b** adds RID-specific implementation-pack acquisition,
   manifest-defined framework closure, and implementation realization through
   the same adapter.

The first consumers are the CLI and Browser/Wasm production hosts through
their later Workspace adoption in steps 9 and 10. This step builds their shared
source substrate; it does not move either host onto it yet.

## Authority and exact claim

**Package-backed Platform realization** owns:

> Given explicit package-source authorization, one supported Platform family
> and target framework, an optional exact target version, host-supplied package
> acquisition capabilities, and finite work bounds, discover or realize the
> corresponding reference or RID-specific implementation distribution pack as
> one source-authorized Platform contribution, preserving package authority,
> producer, and retained-content generation evidence without publishing the
> distribution package as a Package participant.

This owner establishes:

- the closed family-to-reference/runtime package mapping;
- package-backed target discovery and Platform version projection;
- exact reference and implementation source coordinates;
- reference-pack population membership;
- implementation-pack manifest projection and support closure;
- immutable assembly snapshots and Metadata identity validation;
- package-content and Platform source-generation correspondence;
- source-specific closed outcomes and work bounds; and
- the thin package-source-to-PlatformHouse contribution mapping.

It does not establish:

- package-source configuration, mapping, credentials, transport, retry,
  candidate issuance, archive admission, cache publication, or store lifetime;
- `PackageHouse` settlement or package participation;
- Platform target/version selection, source precedence, or fallback;
- installed-hive location or acquisition;
- JSON manifest interpretation;
- target-framework reduction from project or package inputs;
- Workspace admission, call-graph population, dependency traversal, or host
  presentation; or
- bare-library handoff, binding, forwarding, source, analysis, or
  decompilation.

## Normative basis and real-asset evidence

The .NET targeting-pack design and distribution-packaging guidance establish
reference packs as compile-time assets and runtime packs as RID-specific
implementation distributions:

- [.NET targeting packs and runtime packs](https://github.com/dotnet/designs/blob/main/accepted/2019/targeting-packs-and-runtime-packs.md)
- [.NET distribution packaging](https://learn.microsoft.com/dotnet/core/distribution-packaging)

The product contract is narrower: it defines how dotnet-inspect projects
authorized package content to exact Platform targets and immutable library
populations.

Pinned nuget.org evidence at version `11.0.0-rc.1.26425.128` establishes these
ordinary layouts:

| Family | Reference package | Runtime package |
| --- | --- | --- |
| `DotNetRuntime` | `Microsoft.NETCore.App.Ref` | `Microsoft.NETCore.App.Runtime.<rid>` |
| `AspNetCore` | `Microsoft.AspNetCore.App.Ref` | `Microsoft.AspNetCore.App.Runtime.<rid>` |

Those distribution-pack IDs begin with .NET Core 3.0. The version-1 source
therefore supports `netcoreapp3.0`, `netcoreapp3.1`, and `net5.0` or later.
Earlier `netcoreapp` values remain valid shared Platform currency but produce no
package-backed candidate through this mapping. Historical pre-pack distribution
coordinates require separately approved source scope rather than an inferred
fallback to another package family.

Reference packages contain:

```text
ref/net11.0/*.dll
ref/net11.0/*.xml
data/FrameworkList.xml
data/PackageOverrides.txt
data/PlatformManifest.txt
```

Runtime packages contain:

```text
runtimes/<rid>/lib/net11.0/*.dll
runtimes/<rid>/lib/net11.0/<Framework>.deps.json
runtimes/<rid>/lib/net11.0/<Framework>.runtimeconfig.json
```

Minimized package fixtures preserve those layouts and manifest shapes. A
focused real-package probe preserves the pinned evidence that synthetic
fixtures cannot economically establish: package IDs, paths, and the presence
of runtime manifests beside implementation assemblies.

## Boundary and dependency direction

The source implementation lives in
`DotnetInspector.Platforms.Packages`.

```text
DotnetInspector.Platforms
DotnetInspector.Packages
ILInspector.Metadata
DotnetInspector.Platforms.Formats
        |
        v
DotnetInspector.Platforms.Packages
        |
        v
DotnetInspector.PlatformHouse.Packages
        |
        v
DotnetInspector.PlatformHouse
```

`DotnetInspector.Platforms.Packages` consumes package-owner-issued
authorization, complete version discovery, exact candidates, admitted payloads,
stores, producer identity, and retained-content generation. It does not
construct source clients, read ambient NuGet configuration, issue HTTP
requests, interpret credentials, open package archives directly, or call
`PackageHouse`.

`DotnetInspector.PlatformHouse.Packages` is the integration boundary above the
source and House owners. It validates House capability authorization, maps
House target/population/view demands to source requests, and pairs live source
values with resource-free `PlatformSourceContribution` evidence.

Neither House calls the other directly or indirectly. Distribution packages
remain source containers when reached through this path. An exact package
request may inspect the same `.nupkg` through ordinary Package semantics, but
that result does not prove Platform target identity or membership.

The package-source prerequisite is the public lease capability tracked by
[#6560](https://github.com/richlander/dotnet-inspect/pull/6560): complete
version discovery for an explicit selection contract. Platform consumes that
package-owned result and does not reuse the dependency-specific discovery
contract or reconstruct configured-authority aggregation.

## Identity and evidence roles

The identities remain separate:

| Role | Owner | Meaning |
| --- | --- | --- |
| `PlatformFamilyTarget` | Platform target currency | Package- and RID-neutral family, TFM, and exact version. |
| Reference package ID | This source owner | Distribution coordinate selected from the family. |
| Runtime package ID and RID | This source owner | RID-specific implementation distribution coordinate. |
| `PackageAcquisitionCandidate` | Package source model | Exact package coordinate plus authorized reporting authorities. |
| Package Platform target candidate/selection | This source owner | Exact target joined to the package-owner-issued candidate that established it. |
| Configured package authority | Package source model | Authority permitted to provide the selected payload. |
| Producer identity | NuGetFetch/package source | Credential-free origin of the admitted bytes. |
| Package content generation | Package storage | Immutable retained payload generation. |
| Package Platform source generation | This source owner | One discovery or realization attempt. |
| Platform source generation/evidence | PlatformHouse | Resource-free contribution correspondence. |

Package IDs, RIDs, authorities, producers, cache origins, and content
generations never enter `PlatformFamilyTarget`. Equal target values from
installed and package-backed sources remain equal Platform targets with
distinct source evidence.

## Source capabilities and lifetime

One package-backed source receives explicit live capabilities:

```text
PackagePlatformSource(
  package-source settlement lease,
  package-source authorization,
  authority-and-producer scoped store factory,
  payload limits,
  optional transfer policy,
  source maximums)
```

The source does not own or dispose the settlement lease, authorization, source
clients, stores, transfer policy, or operation context. It owns only its
immutable source results and private assembly byte snapshots.

Retiring the package-source lease prevents new discovery, candidate
settlement, and payload acquisition. Existing successful source results remain
readable because realized libraries own private byte snapshots rather than
reopening the package store.

The source authorization is evaluated independently for each closed package
ID. Authorization for a reference package does not authorize its runtime pack,
the other family, another RID, or another version.

## Step 5a: target discovery

Discovery receives one `PlatformFamily`, one canonical
`PlatformTargetFramework`, and a maximum candidate count.

It:

1. maps the family to its exact reference package ID;
2. requests a complete, listed, prerelease-inclusive version inventory through
   the package-source settlement lease;
3. accepts only versions representable as canonical `PlatformVersion`;
4. retains only versions whose major/minor release band equals the requested
   TFM;
5. asks the package-owner result to issue the discovered
   `PackageAcquisitionCandidate` for each accepted version;
6. joins each issued candidate to the corresponding `PlatformFamilyTarget`;
7. sorts by Platform SemVer precedence and then exact version identity; and
8. returns one immutable candidate inventory tied to a fresh package Platform
   source generation.

Package versions outside the Platform version grammar are outside this
source's target currency and do not become candidates. A matching version with
SemVer build metadata cannot be produced by ordinary NuGet package identity
and therefore never enters discovery.

An authoritative empty package listing produces a successful empty target
inventory. Partial or failed configured-authority discovery never produces a
shortened successful inventory. Exceeding the candidate bound is incomplete.

Target discovery establishes that authorized package authorities listed the
exact reference-pack version. It does not claim that its payload has already
been acquired or validated. Exact realization consumes the source-issued
selection or separately authorizes an externally established exact target,
then acquires the resulting candidate; absence or invalid layout remains
visible there.

The PlatformHouse adapter projects each candidate to
`PlatformFamilyTarget`. PlatformHouse retains target selection policy and does
not receive package IDs or live package candidates in the resource-free
contribution. The live package Platform discovery result remains beside that
contribution. When target settlement selects this source's candidate, the
orchestrator asks the inventory to issue a
`PackagePlatformTargetSelection` for the exact target. That selection retains
the package candidate, inventory association, target correspondence, and
source generation for later realization.

## Step 5a: exact reference coordinate

An exact reference source coordinate is:

```text
PackageReferencePackCoordinate(
  family,
  target framework,
  exact Platform version,
  exact reference package ID)
```

The package ID is derived from the family and validated during construction;
it is retained as source evidence, not caller-selected arbitrary text.

The exact package coordinate omits a RID. Its version is the exact canonical
Platform version. Targets containing SemVer build metadata are rejected
because NuGet package coordinates cannot preserve that identity.

Reference realization has two explicit source-specific forms:

- **Discovered selection** consumes a
  `PackagePlatformTargetSelection` issued by this source's target inventory.
  The source verifies the selection association and exact target, then uses its
  retained `PackageAcquisitionCandidate` unchanged.
- **Externally established exact target** consumes the package reference
  coordinate directly and asks the package owner to issue a caller-pinned
  candidate. This form applies when the House request was exact without this
  package discovery result as its selection evidence, including a target
  established by another authorized Platform source.

The adapter must choose the discovered form whenever this source's discovery
evidence established the settled target. Omitting that live selection is not
permission to reconstruct a caller-pinned candidate.

Realization:

1. verifies the source selection or exact external coordinate;
2. preserves the discovered candidate, or issues a caller-pinned candidate for
   the externally established exact-target form;
3. acquires one admitted retained payload only through the resulting
   candidate;
4. verifies the returned package ID and version;
5. selects the exact package population root `ref/<tfm>/`;
6. snapshots the requested assembly or complete reference population; and
7. retains the serving authority, producer, package content generation, and
   cache/download origin beside the source result.

A discovered candidate may be served only by authorities that reported its
coordinate under the complete discovery contract, including for cache hits.
The externally established exact-target form may use every authority admitted
by its package-ID authorization. No fallback version, TFM, package ID, or
source widening is permitted within either form.

## Reference population membership

For one exact coordinate, the complete package-backed reference population is
every top-level entry beneath exact prefix `ref/<tfm>/` whose final segment has
a case-insensitive `.dll` extension.

Nested entries, XML companions, analyzers, `data/` files, and non-DLL entries
are outside the population. Package entry paths use `/`; host filesystem path
rules and casing do not define membership.

The source enumerates the admitted package content once under a finite entry
bound. Two entries that collide case-insensitively at the selected logical
coordinate reject the population. The source sorts selected logical
coordinates ordinally before reading bodies.

Every selected DLL must:

- fit the per-entry and aggregate realization byte bounds;
- be copied into private immutable source-owned memory;
- contain ECMA-335 metadata;
- be an assembly rather than a netmodule;
- not be Windows Metadata; and
- have a Metadata-projected assembly identity distinct from every other
  population member.

Any selected member that fails those conditions rejects the whole population.
No shortened success is returned.

## Exact-library reference realization

An exact assembly demand uses one complete Metadata
`AssemblyReferenceIdentity`.

The source projects its simple name to
`ref/<tfm>/<name>.dll`, requires one case-insensitive coordinate match without
collision, snapshots that member, and verifies the decoded identity is
equivalent to the request.

The path projection is only an acquisition optimization. Metadata identity is
the completion gate. An absent member is source absence; a coordinate collision
or identity mismatch is rejected source evidence.

An opaque PlatformHouse `PlatformLibraryIdentity` cannot be projected to a
package member. The adapter rejects that demand until a source-issued library
identity correspondence exists.

## Step 5b: implementation coordinate and closure

Step 5b adds one exact implementation source coordinate:

```text
PackageImplementationPlatformCoordinate(
  family,
  target framework,
  exact Platform version,
  RID)
```

The family and RID derive the exact runtime package ID. The RID is explicit
host input and source evidence; it does not enter `PlatformFamilyTarget`.

The source acquires one exact runtime pack per framework in the selected
support closure. For `DotNetRuntime`, the root is
`Microsoft.NETCore.App.Runtime.<rid>`. For `AspNetCore`, the root is
`Microsoft.AspNetCore.App.Runtime.<rid>`, whose runtime configuration may add
`Microsoft.NETCore.App` at an exact compatible version and therefore require
the corresponding .NET runtime pack.

Each framework's same-named `runtimeconfig.json` and `deps.json` is read from:

```text
runtimes/<rid>/lib/<tfm>/
```

The source supplies their bounded immutable bytes to
`DotnetInspector.Platforms.Formats`. It does not parse JSON or recreate
framework-reference, roll-forward, runtime-target, or logical-asset rules.

The final framework graph is resolved deterministically under explicit
framework, resolution-step, manifest-library, manifest-asset, assembly, and
byte bounds. Implementation membership is only the managed assets declared by
the selected dependency manifests, projected to exact entries in the
corresponding runtime pack. Broad DLL scanning is not authoritative.

The implementation realization preserves each library's framework owner,
manifest coordinate, runtime package coordinate, RID, serving authority,
producer, package content generation, digest, and assembly identity. Duplicate
logical coordinates, package-content collisions, duplicate assembly
identities, manifest/member mismatches, or incompatible framework closure
reject atomically.

## Generations and PlatformHouse projection

Every discovery or realization attempt issues a fresh
`PackagePlatformSourceGeneration`.

A successful reference realization retains:

- the exact package reference coordinate;
- the requested population demand;
- the serving configured authority;
- payload producer and cache/download origin;
- exact package content generation;
- immutable source-owned libraries; and
- the source generation.

The PlatformHouse adapter issues resource-free:

- `PlatformSourceGeneration` for the source attempt;
- `PlatformSourceCoordinateIdentity` for the package-backed source coordinate;
- `PlatformTargetCorrespondenceIdentity` joining that coordinate to the exact
  House target; and
- `PlatformSourceEvidenceIdentity` for the contribution.

The live source result remains beside the House contribution. House receipts
do not retain configured authorities, source clients, stores, package content,
streams, or byte buffers.

Successful complete-population realization contributes
`Authoritative`. Exact one-library realization contributes `DemandComplete`.
Target discovery contributes only the candidates established by one complete
package-source discovery result.

## Failure and work semantics

The source outcome is closed:

- `Succeeded` retains an immutable inventory or realization;
- `Unavailable` records denied authorization, exact package absence, or absent
  requested membership;
- `Rejected` records an unrepresentable coordinate, invalid package layout,
  coordinate collision, malformed assembly, identity mismatch, invalid
  manifest, or invalid framework closure;
- `Incomplete` records partial source discovery, source/House deadline expiry,
  or a finite source/request work bound reached before completion; and
- `Failed` records package authority, transport, store, or content-read
  failures that prevent an authoritative answer.

Package-owner failures remain attached to the source outcome. The adapter
projects a credential-safe summary to PlatformHouse without replacing or
reclassifying the package evidence.

Cancellation remains cancellation. Caller cancellation is never converted to
another outcome.

The source bounds:

- configured-authority version discovery through the package operation
  context;
- discovered Platform candidates;
- package payload archive and expansion through package payload limits;
- observed package entries;
- frameworks and framework-resolution steps;
- manifest libraries and assets;
- realized assembly count;
- bytes per selected entry;
- aggregate realized bytes; and
- duration and cancellation.

No bound failure returns a partial successful population.

## Platform compatibility

The source uses only host-neutral package clients, stores, retained content,
SRM, and format readers. It supports Windows, Linux, macOS, and
single-threaded Browser/Wasm.

It does not read installed dotnet hives, environment variables, global package
folders, host filesystem paths, or ambient NuGet configuration. A desktop host
may supply filesystem-backed package stores; a Browser/Wasm host supplies
in-memory stores and typed source clients through the same contracts.

No dependency or API in this design introduces a supported-platform
exception.

## Evidence gates

The step 5a Release gates prove:

- exact family-to-reference-package mapping;
- prerelease-inclusive authoritative target discovery;
- canonical Platform version and TFM-band filtering;
- empty, partial, failed, and candidate-bound outcomes;
- discovered-candidate reporting-authority preservation and externally
  established exact-target authorization without source widening;
- filesystem-backed and in-memory package content produce equal source
  membership;
- top-level reference population selection and case-collision rejection;
- one-library demand does not read unrelated assembly bodies;
- assembly, netmodule, WinMD, identity mismatch, and duplicate identity
  rejection;
- per-entry, aggregate-byte, entry-count, assembly-count, duration, and
  cancellation bounds;
- immutable snapshots remain readable after source inputs are retired; and
- target discovery and reference realization map to authorized
  PlatformHouse contributions.

The step 5b Release gates additionally prove:

- RID-specific package mapping;
- runtime manifest acquisition through package content;
- `AspNetCore` support closure through the exact .NET runtime pack;
- manifest-defined membership rather than broad DLL scanning;
- framework, manifest, logical-coordinate, assembly-identity, and digest
  correspondence;
- collision, missing-member, incompatible-closure, malformed-manifest, and
  work-limit outcomes; and
- equal CLI-capable filesystem and Browser/Wasm in-memory realization.

The normal solution build, dependency-policy evaluator, CI routing gate, and
project-graph tests enforce the dependency direction. The pinned real-package
probe is reproducible design evidence; minimized fixtures are the ordinary CI
gate.

## Production adoption and retirement

This owner completes counted step 5 in two sub-slices. It does not add another
counted step.

After 5a and 5b:

1. step 6 defines provenance-retaining bare-library handoff;
2. steps 7 and 8 add target-bound indexing and documentation;
3. step 9 adopts PlatformHouse in Workspace, reference resolution, call graphs,
   and dependency traversal;
4. step 10 adopts the same requests and outcomes in CLI and Inspect Web; and
5. step 11 retires direct package-backed Platform selection in
   `WorkspaceContextLoader`, `BrowserPlatformCatalog`,
   `BrowserPlatformWorkspace`, and the remaining legacy services.

[#4413](https://github.com/richlander/dotnet-inspect/issues/4413) is a
downstream Browser performance beneficiary. Step 5b removes broad repeated
runtime-pack scanning by defining manifest membership, while step 10 owns the
Browser migration that realizes the performance change.

## Demo

### Package-backed reference target

```text
request
  family: DotNetRuntime
  target framework: net11.0
  source capability: authorized package-backed reference source

target discovery
  package: Microsoft.NETCore.App.Ref
  configured authorities: explicit host authorization
  listed versions:
    11.0.0-preview.7
    11.0.0-rc.1.26425.128
  Platform candidates:
    DotNetRuntime / net11.0 / 11.0.0-preview.7
    DotNetRuntime / net11.0 / 11.0.0-rc.1.26425.128

exact realization
  selected target: 11.0.0-rc.1.26425.128
  discovered package candidate: Microsoft.NETCore.App.Ref
  serving authorities: only authorities that reported the selected version
  retained payload generation: package-owned
  population: ref/net11.0/*.dll
  result:
    authoritative immutable reference libraries
    package authority + producer + generation evidence
    resource-free PlatformHouse correspondence
```

What to notice: target identity contains no package ID or producer. The live
source selection preserves which authorities reported the chosen version, and
exact realization fails visibly if their payload or expected layout is
unavailable. A target established outside this package discovery path instead
uses explicit caller-pinned package semantics.

### ASP.NET Core implementation closure

```text
request
  family: AspNetCore
  target: net11.0 / 11.0.0-rc.1.26425.128
  RID: linux-x64

root runtime pack
  Microsoft.AspNetCore.App.Runtime.linux-x64
  Microsoft.AspNetCore.App.runtimeconfig.json
    -> Microsoft.NETCore.App 11.0.0-rc.1.26425.128

support runtime pack
  Microsoft.NETCore.App.Runtime.linux-x64

dependency manifests
  -> exact managed logical assets
  -> no broad scan of unrelated DLLs

result
  one AspNetCore-focused implementation contribution
  explicit DotNetRuntime support closure
  per-package authority, producer, generation, RID, manifest, and library
  evidence
```

What to notice: the RID and runtime package IDs remain source coordinates. The
selected Platform target remains the package-neutral ASP.NET Core family
target, while the result visibly retains its .NET runtime support closure.
