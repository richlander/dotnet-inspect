# Platform target currency

## Status and approved scope

This document is the normative owner for the shared Platform target currency
tracked by [#6361](https://github.com/richlander/dotnet-inspect/issues/6361).
It is a focused prerequisite for:

- the platform-first product direction in
  [#6228](https://github.com/richlander/dotnet-inspect/issues/6228);
- Workspace ecosystem registration and call-graph breadth in
  [#6012](https://github.com/richlander/dotnet-inspect/issues/6012);
- `PlatformHouse` composition in
  [#6301](https://github.com/richlander/dotnet-inspect/issues/6301); and
- platform-owner extraction from `DotnetInspector.Services` in
  [#6335](https://github.com/richlander/dotnet-inspect/issues/6335).

The user-approved product direction has two parts:

1. Platform receives equal product currency and opportunity with packages.
2. Platform receives analogous host-neutral APIs and infrastructure where the
   domains have corresponding roles.

Analogy does not erase domain differences. Packages are independently
published payloads selected through configured authorities. A platform is a
coherent set of framework-family assets with distinct reference and
implementation representations. This owner supplies only the common identity
footing that lets later APIs express those differences without strings.

The first production consumer is
[Platform Library Population Declaration](platform-library-population-declaration.md).
That declaration retains one owner-issued `PlatformFamily` rather than owning
another copy of the family identity.

## Authority and exact claim

**Platform Target Currency** owns:

> Issue immutable, resource-free identities for one supported logical platform
> family and one exact family target, preserving the family, target-framework,
> and version association so platform facts can be joined without
> reconstructing correspondence from strings.

This owner defines:

- the closed version-1 `PlatformFamily`;
- package-neutral exact `PlatformVersion`;
- canonical `PlatformTargetFramework`;
- `PlatformFamilyTarget`;
- construction, validation, equality, and deterministic family order; and
- the boundary between shared platform identity and source-specific
  coordinates.

It does not define:

- Workspace or ecosystem registration intent;
- a composed application platform with multiple family targets;
- source discovery, authorization, ranking, fallback, or acquisition;
- reference/API or implementation/body views;
- exact-library, bounded-set, or whole-population demand;
- library membership, focus/support roles, or completion;
- installed-hive, reference-pack, implementation-pack, or browser-index
  realization;
- source evidence, generation, producer, lease, or cache identity;
- package subsumption, assembly binding, Workspace admission, traversal, or
  presentation; or
- `PlatformHouse` request, settlement, or result semantics.

Those owners retain this currency beside their own evidence. A discovery or
realization owner may explicitly construct a validated family target from its
authoritative source coordinate when exact selection occurs; it must return
that target with the evidence that established the correspondence. Incidental
display text, neighboring files, package names, and independently equal
strings remain insufficient.

## Why Platform needs its own currency

Package inspection already distinguishes several roles:

```text
package intent
  -> exact package coordinate
  -> source-authorized candidate
  -> acquired payload
  -> Workspace realization
```

The identities remain separate because an unresolved request, one exact
package, authority evidence, bytes, and an admitted Workspace participant are
not interchangeable.

Platform currently has the same conceptual stages but lacks the corresponding
domain floor:

```text
population declaration
  -> exact family target
  -> source-specific realization request
  -> realized population
  -> Workspace admission
```

Without the exact family-target currency, each component recreates the middle
identity:

- Source Selection names `DotNetRuntime` and `AspNetCore`;
- search uses `runtime`, `aspnetcore`, and `netstandard`;
- Queries uses `runtime` and `aspnetcore` acquisition strings;
- installed realization uses manifest family and version values;
- pruning groups a family, TFM, and pack version;
- reference packs use `Microsoft.NETCore.App.Ref` and
  `Microsoft.AspNetCore.App.Ref`;
- shared frameworks use `Microsoft.NETCore.App` and
  `Microsoft.AspNetCore.App`;
- implementation packs use package IDs containing runtime identifiers; and
- Browser indexes use `netcore.app` and `aspnetcore.app`.

These are legitimate source or presentation vocabularies. They are not proof
that two facts describe the same platform target. A shared owner-issued target
makes correspondence explicit while leaving each adapter's coordinate intact.

## Contract

### Platform family

The version-1 family set is closed:

```text
PlatformFamily
  = DotNetRuntime
  | AspNetCore
```

`DotNetRuntime` identifies the logical `Microsoft.NETCore.App` product family.

`AspNetCore` identifies the logical `Microsoft.AspNetCore.App` product family.

The product-family names explain the identities; they are not source
coordinates carried by the values. `PlatformFamily` has no arbitrary-string
constructor.

The canonical family order is:

1. `DotNetRuntime`
2. `AspNetCore`

This order exists only for deterministic composition, diagnostics, and
transport. It is not source precedence, fallback order, focus priority, or
permission to stop after the first family.

`.NET Standard` is not a version-1 `PlatformFamily`. It remains a
reference-only search framework without an implementation population for
call-graph body traversal. WindowsDesktop and workload-specific families
require separately approved scope.

### Platform target framework

`PlatformTargetFramework` is the selected family target's canonical base
framework. It identifies the release line of the platform assets themselves,
not necessarily the unreduced framework text of the project or consumer that
requested them.

Version 1 accepts:

- `netcoreapp<major>.<minor>` for .NET Core 1.0 through 3.1; and
- `net<major>.<minor>` for .NET 5 and later.

The value:

- is lowercase ASCII;
- uses decimal major and minor components without signs or leading zeroes
  except the single digit `0`;
- contains no platform qualifier, profile, runtime identifier, version range,
  wildcard, or whitespace; and
- preserves the distinction between the `netcoreapp` and `net` forms.

Project-framework reduction is not part of this owner. A project target such
as `net10.0-windows` must be explicitly projected to the applicable base
platform target by the project or compatibility owner. The currency does not
strip suffixes or infer a platform target from arbitrary framework text.

### Platform version

`PlatformVersion` is one exact canonical SemVer 2 platform version.

The value:

- is package-neutral and does not expose `NuGetVersion`;
- contains no range, wildcard, floating label, or implicit latest value;
- preserves prerelease and build metadata in its canonical full identity;
- compares semantic precedence when an owner explicitly requests ordering;
  and
- does not collapse two canonical identities merely because SemVer precedence
  ignores their differing build metadata.

The target framework does not imply a platform version. `net11.0` and
`11.0.0` are separate facts and must both be supplied by the caller that owns
target selection.

Their release bands must agree: the platform version's major and minor
components equal the target framework's major and minor components. Patch,
prerelease, and build-metadata components remain version identity and have no
TFM counterpart. `DotNetRuntime/net10.0/11.0.0` is invalid; selecting a
different platform band is a version-selection result that must issue the
corresponding target framework rather than relabeling the version.

### Exact family target

`PlatformFamilyTarget` retains exactly:

```text
PlatformFamilyTarget
  Family
  TargetFramework
  Version
```

All three values participate in equality. They travel together whenever a
component claims that evidence describes one exact family target.

A family target is:

- immutable and portable;
- resource-free;
- independent of paths, hives, package IDs, producers, caches, and network;
- independent of reference versus implementation view;
- independent of one exact library or whole-population demand;
- valid in NativeAOT and Browser/Wasm; and
- not evidence that any source can realize it.

The owner does not define a `PlatformTarget` composition in version 1. A web
application commonly combines one `AspNetCore` family target with one
`DotNetRuntime` support target. The later composition owner must state its
same-framework, compatibility, ordering, and replacement rules rather than
having this identity owner imply them.

## Identity, role, and evidence stay separate

Four concepts must not collapse:

| Concept | Example | Meaning |
| --- | --- | --- |
| Family identity | `PlatformFamily.DotNetRuntime` | Which logical product family |
| Registration role | `PlatformLibraryPopulationDeclaration(DotNetRuntime)` | This Workspace is relevant to that family population |
| Exact target identity | `DotNetRuntime`, `net11.0`, `11.0.0` | Which family target a later fact describes |
| Source evidence | installed-hive generation, reference-pack coordinate, implementation-pack producer | How one source realized that exact target |

The family identity is reusable domain currency. The declaration, target, and
source evidence remain distinct typed roles even when they retain the same
family.

An owner that receives an exact typed target retains it unchanged in requests
and results. A discovery owner that starts from logical intent may mint a new
exact family target only from its authoritative selected source coordinate,
using this owner's validation, and must return the new target with that source
evidence. Equal display strings, CLI aliases, paths, package IDs, assembly
names, namespaces, and browser labels do not independently establish
correspondence.

## Transfer from Source Selection

[Platform Library Population Declaration](platform-library-population-declaration.md)
continues to own resource-free registration relevance:

```text
PlatformLibraryPopulationDeclaration(PlatformFamily)
```

This owner takes over only the underlying closed family identity and its
product-family meaning. Source Selection retains:

- the declaration role;
- declaration construction and non-action;
- population relevance semantics;
- reference-versus-implementation view separation;
- focus-population versus support-closure meaning; and
- the rule that registration chooses no target, version, source, demand,
  acquisition, or admission.

The declaration is not a type alias. Passing a raw `PlatformFamily` where a
population declaration is required would erase the registration role in the
same way that passing package text where a package-prefix declaration is
required would erase source intent.

No runtime or persisted-data migration is required by this transfer because
the declaration's implementation and host adoption have not landed.

## Source-specific projections

Every adapter projection is explicit:

| Shared currency | Source-specific value |
| --- | --- |
| `DotNetRuntime` | `runtime`, `netcore.app`, `Microsoft.NETCore.App`, `Microsoft.NETCore.App.Ref`, or a runtime implementation-pack ID |
| `AspNetCore` | `aspnetcore`, `aspnetcore.app`, `Microsoft.AspNetCore.App`, `Microsoft.AspNetCore.App.Ref`, or an ASP.NET Core implementation-pack ID |

The table records existing correspondences; it does not authorize a universal
string parser. Each adopting owner defines the projection at its integration boundary. An
outbound projection retains the shared input beside the source-specific
result. An exact discovery result may construct the shared target from the
authoritative source coordinate and retains both together; it does not recover
identity from a later display or path scan.

### Installed realization

`InstalledPlatformFamily` remains an installed-source coordinate with its own
validation and manifest semantics. `InstalledPlatformVersion` remains the
installed owner's version value until that owner adopts this currency in a
separate effort.

Translation from `PlatformFamilyTarget` to an installed request occurs above
the package-free installed adapter boundary. The installed adapter does not
need Source Selection, package-source, ecosystem, or Workspace dependencies.

### Reference and implementation packs

Pack IDs and package-producer evidence remain source coordinates. A
reference-pack or implementation-pack adapter maps from one exact family
target and returns evidence retaining that correspondence. The family target
does not imply a pack ID, runtime identifier, feed, or authorization.

### Pruning

Platform/package pruning owns subsumption evidence and comparison. Its current
family/TFM/version grouping is a planned adopter of
`PlatformFamilyTarget`; this owner does not move pruning semantics or package
version comparison into the target currency.

The separately owned `PlatformHouse` contract consumes those facts for product
reference processing. This currency owner defines neither that routing nor the
meaning of a pruning result.

### Queries and Workspace

Queries retains acquisition, admission, revisions, leases, and realized-member
coordinates. Its platform acquisition strings become explicit projections
from a family target in a separate adoption. They remain operation and
reacquisition coordinates rather than the universal platform identity.

### Browser

Browser catalog labels and transport values remain host projections. Browser
must receive or retain the shared currency before mapping to
`netcore.app`, `aspnetcore.app`, `runtime`, or `aspnetcore`; it cannot recover
the shared identity from those strings after transport.

## Relationship to PlatformHouse

[#6301](https://github.com/richlander/dotnet-inspect/issues/6301) defines
`House` as a major clearing-house service that aggregates candidates from
multiple sources, applies policy, and settles one authorized result.

`PlatformHouse` therefore consumes this currency; it does not own or mint a
parallel family or target identity.

The separately owned
[PlatformHouse Reference Processing](platform-house-reference-processing.md)
contract defines the sole product-facing platform reference-processing facade,
including its requests, policy, settlement, retained correspondence, and
visible non-success outcomes. It encapsulates pruning and transparent .NET
Standard processing while composing, rather than redefining, the pruning
owner's target-bound facts and Metadata's structured type-forwarding contract.
`.NET Standard` does not become a third `PlatformFamily`, registration
population, or implementation target.

This document records that production-adoption dependency but does not define
the House contract. Pack acquisition, installed realization, type indexing,
pruning, Metadata forwarding, and documentation remain focused capabilities.
Moving their implementations into a platform-named assembly does not transfer
their contracts to the House or make the House an assembly bucket.

The target `DotnetInspector.Platforms` project is reserved for this lower
contract floor. Packages, Source Selection, Queries, and later platform
composition may depend on the floor; the floor does not depend on them.
[#6335](https://github.com/richlander/dotnet-inspect/issues/6335) separately
chooses operational project boundaries for pack, resolver, type-catalog, and
House implementations. Those implementations do not enter the contract-floor
assembly merely because they concern Platform.

## Pathological cases

### Same framework, different family versions

A web target selects `DotNetRuntime/net11.0/11.0.2` and
`AspNetCore/net11.0/11.0.1`. The identities remain distinct exact family
targets. A consumer cannot replace the ASP.NET Core version with the runtime
version because their framework text and release band match.

### Mismatched target band

An adapter discovers runtime version `11.0.0` while handling a `net10.0`
family-target request. It cannot construct
`DotNetRuntime/net10.0/11.0.0`. The adapter returns the source owner's
non-match or version-selection outcome, or explicitly issues a distinct
`net11.0` target when its owning operation permits discovery of another band.

### Same semantic precedence, different identity

Two source candidates report `11.0.0+servicing.1` and
`11.0.0+servicing.2`. SemVer precedence treats them equally, but
`PlatformVersion` identity does not. A source-selection owner must preserve or
reject the ambiguity rather than coalescing the candidates.

### Platform-qualified project target

A project targets `net10.0-windows`. Direct construction of
`PlatformTargetFramework` from that text is rejected. The project/compatibility
owner may separately establish that `net10.0` is its applicable base platform
target; this owner does not infer it by truncating text.

### Runtime support for ASP.NET Core

An ASP.NET Core realization needs a .NET runtime closure. The result may retain
both exact family targets, but this owner does not merge them or label one as
focus or support. Those are realization and consumer roles.

### Browser and source aliases

A Browser row says `netcore.app`, while an implementation request says
`runtime`. Neither string constructs `PlatformFamily.DotNetRuntime`. The
Browser integration must preserve the owner-issued family through both
projections.

### .NET Standard

Search selects `SearchPlatformFramework.NetStandard`. No version-1
`PlatformFamily` can be constructed from that value. If the reference contract
must connect to an implementation definition, the operation routes through
`PlatformHouse`, which applies Metadata's structured forwarding result
transparently. The caller does not select a .NET Standard implementation
population or invoke platform-specific forwarding policy directly.

## Analogous implementation evidence

Package APIs provide the closest repository analogy:

- resource-free declarations remain distinct from operation requests;
- exact coordinates remain distinct from source authority and acquired
  payloads;
- source results retain owner-issued correspondence instead of reconstructing
  it from producer text; and
- Workspace coordinates retain acquisition meaning rather than becoming the
  package's universal identity.

The transferred behavior is the typed staging and correspondence discipline.
The following package properties do not transfer:

- configured feed authorities and package-source mapping;
- independently selectable payloads;
- floating-version defaults;
- producer identity as universal source evidence; and
- partial candidate aggregation as sufficient platform realization.

.NET's `FrameworkReference` is additional comparative evidence: it separates a
logical framework family from target-specific reference and implementation
assets. MSBuild evaluation, implicit references, hostfxr roll-forward, and
application launch remain outside this contract.

## Ownership and adoption

| Owner | Responsibility |
| --- | --- |
| This owner | Family, target-framework, version, exact family-target identity, validation, equality, and non-action |
| Source Selection | Population declaration role and target-independent relevance |
| Platform source adapters | Source coordinates, realization, evidence, completion, and failures |
| PlatformHouse | Separately owned product-facing reference-processing facade and target-bound settlement |
| Platform/package pruning | Target-bound package subsumption facts and comparison |
| Queries and Workspace | Operation lowering, admission, revisions, leases, and participant lifetime |
| Metadata | Canonical assembly identity and guarded inspection |
| CLI and Inspect Web | User intent, target selection, diagnostics, and presentation |

There are nine counted production-adoption steps:

1. Lock this focused target-currency design under #6361.
2. Implement the package-neutral values in the target
   `DotnetInspector.Platforms` contract floor.
3. Have Source Selection retain `PlatformFamily` in
   `PlatformLibraryPopulationDeclaration`.
4. Have pruning retain `PlatformFamilyTarget` instead of its parallel
   family/framework/version strings.
5. Have Queries lower acquisition coordinates from and retain correspondence
   to the exact family target.
6. Define and implement `PlatformHouse` source settlement and realization
   receipts over this currency, including its sole-facade reference-processing
   boundary, pruning composition, and transparent .NET Standard forwarding,
   while keeping installed adapters package-free.
7. Route Queries, Workspace, dependency traversal, and assembly-reference
   platform fallback through the House, retiring their direct composition of
   platform mechanics.
8. Adopt the shared target and House result in CLI platform operations and the
   Platform subject.
9. Transport and consume the same target and result in Inspect Web, retiring
   private family inference and direct platform processing from browser labels
   and acquisition strings.

Steps 3 through 9 remain separately reviewed owner adoptions. This design
changes only the currency owner and the pre-implementation Source Selection
contract that previously owned the two family identities.

This component carries no rendering data, so Markout and host-specific
rendering strategy do not apply.

## Demo

The application catalog retains a registration declaration:

```text
PlatformLibraryPopulationDeclaration(
  PlatformFamily.DotNetRuntime)
```

A later target selector issues:

```text
PlatformFamilyTarget(
  Family: DotNetRuntime,
  TargetFramework: net11.0,
  Version: 11.0.0)
```

Pruning, a reference-pack catalog, a future `PlatformHouse` request, and a
Workspace realization can retain that exact value beside their independently
owned evidence.

The neighboring ASP.NET Core target is distinct:

```text
PlatformFamilyTarget(
  Family: AspNetCore,
  TargetFramework: net11.0,
  Version: 11.0.0)
```

Their equal framework and version values do not merge the families. A later
web-platform composition may contain both.

## Required gates

| Gate | Required observation |
| --- | --- |
| Closed family contract | Public construction exposes exactly `DotNetRuntime` and `AspNetCore`; arbitrary text and .NET Standard cannot create a family. |
| Canonical framework contract | Supported base TFMs round-trip canonically; platform-qualified, ranged, wildcard, malformed, and non-platform TFMs reject. |
| Exact version contract | Stable, prerelease, and build-metadata-bearing exact versions retain canonical identity; ranges and floating values reject. |
| Target association | Family, framework, and version all participate in equality and cannot be independently relabeled. |
| Resource-free construction | Creating, comparing, and ordering values performs no filesystem, environment, cache, network, package, or Workspace operation. |
| Declaration adoption | `PlatformLibraryPopulationDeclaration` retains the exact owner-issued family and cannot be constructed by display-string inference. |
| Public consumer canary | One reusable non-host consumer constructs and compares the values without CLI or Browser dependencies. |
| Contract-floor composition | Project references follow the documented lower-to-higher direction. No automated absence gate is required for this design. |

The first implementation slice owns these Release gates. Pruning, Queries,
`PlatformHouse`, CLI, and Browser add adoption-specific correspondence gates
in their own efforts.

The operator selected **no automated absence-gate coverage** for the
contract-floor dependency direction. The architecture and code-review boundary
remain binding, but the implementation does not add a repository dependency-
policy or reflection gate solely to prove the absence of higher-layer
references.

## Non-claims

This design does not define:

- a complete platform, application target, or framework-reference graph;
- platform family compatibility or version selection;
- roll-forward, prerelease selection, or latest-version policy;
- reference-pack, shared-framework, or implementation-pack coordinates;
- source authorization, candidate aggregation, acquisition, or caching;
- platform member inventories, facades, private implementations, or docs;
- focus/support member roles or realization completeness;
- pruning contents or package-to-platform delegation;
- Workspace registration storage, platform role, or participant replacement;
- call-graph population or traversal; or
- CLI, Browser, persistence, JSON, or Markout schemas.
