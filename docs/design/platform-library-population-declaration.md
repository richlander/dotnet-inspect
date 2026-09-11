# Platform library population declaration

## Status and approved scope

This document is the normative owner for the resource-free platform library
population declaration tracked by
[#6328](https://github.com/richlander/dotnet-inspect/issues/6328). It is one
focused prerequisite within stage 3 of
[#6012](https://github.com/richlander/dotnet-inspect/issues/6012) and supports
the platform-first direction tracked by
[#6228](https://github.com/richlander/dotnet-inspect/issues/6228).

The first production use is the Platform arm of
[Workspace Ecosystem Registration Handoff](workspace-ecosystem-registration-handoff.md).
The declaration lets the application catalog say that a Workspace is relevant
to the .NET runtime or ASP.NET Core library population without selecting or
executing a platform source.

The declaration contract is implemented by
`DotnetInspector.SourceSelection.PlatformLibraryPopulationDeclaration`.
Catalog projection, Workspace retention, source realization, and host adoption
remain in their separately owned slices.

## Authority and exact claim

**Platform Library Population Declaration** owns:

> Issue one immutable resource-free declaration for either the .NET runtime or
> ASP.NET Core logical library population, before any target, version, source,
> reference/implementation view, operation bound, acquisition, or Workspace
> admission is selected.

The declaration identifies relevance. It does not contain a library inventory
and does not prove that any source can realize the population.

This owner defines:

- the declaration role over one owner-issued
  [Platform Family](platform-target-currency.md#platform-family);
- the distinction between focus population and binding-support closure;
- the separation between logical population and source-specific views;
- the correspondence that later source adapters must preserve; and
- declaration construction, equality, and non-action.

It does not define:

- target-framework or version selection;
- reference-pack, installed shared-framework, or remote implementation-pack
  discovery;
- platform catalog generation or library enumeration;
- source authorization, transport, caching, budgets, or acquisition;
- Workspace admission, platform-role assignment, or assembly binding;
- package pruning or package-to-platform correspondence;
- call-graph focal length, traversal, or result completeness; or
- CLI or browser presentation.

## Why the declaration belongs to Source Selection

`DotnetInspector.SourceSelection` already owns immutable source intent that is
valid before realization. Its `PackagePrefixDeclaration` records a source
population without choosing a request bound, source, version, or acquisition.
The platform declaration has the same role.

The operational alternatives are the wrong layer:

- `DotnetInspector.Services.PlatformResolver` resolves installed and cached
  reference packs and shared frameworks. Its path-bearing, version-selected
  results are source evidence, not registration intent.
- `WorkspaceMemberCoordinate.Platform` in Queries requests implementation-pack
  acquisition for one target and optional assembly. It is an operation input,
  not a retained relevance declaration.
- `RealizedMemberCoordinate.Platform` binds an exact package version, producer,
  target framework, and optional assembly after acquisition.
- `SearchPlatformFramework` is a search-normalization result. It includes
  .NET Standard and carries search policy rather than Workspace registration
  identity.
- browser platform-index rows are generated inventory hints for exact targets.
  They are neither source authority nor a portable registration declaration.

Placing the new declaration in Queries would prevent lower source-selection
consumers from using it and would make the declaration depend on query
orchestration. Placing it in Services would make a static intent value depend
on the repository's broad operational services assembly. Placing it in
Ecosystems would reverse the application-catalog boundary.

The declaration therefore extends Source Selection's existing construction
role. Platform adapters, Queries, Ecosystems, CLI, and Browser consume it
without transferring their behavior into this owner.

## Contract shape

The version-1 declaration retains one closed owner-issued family:

```text
PlatformLibraryPopulationDeclaration(PlatformFamily)
```

`PlatformFamily` and its exact target currency are owned by
[Platform Target Currency](platform-target-currency.md). The declaration is a
distinct role type rather than an alias: it states that the retained family is
a relevant logical library population. Display labels and CLI aliases are
neither family nor declaration identity.

`PlatformFamily.DotNetRuntime` denotes the logical
`Microsoft.NETCore.App` product family. The declaration says its library
population is relevant.

`PlatformFamily.AspNetCore` denotes the logical
`Microsoft.AspNetCore.App` product family. The declaration says its library
population is relevant.

These product-family names explain the values; they are not paths, installed
framework coordinates, package IDs, or source lookup keys carried by the
declaration.

Construction is:

- immutable and deterministic;
- allocation-bounded;
- independent of the local machine, installed SDKs, caches, and network;
- valid in Browser/Wasm and NativeAOT; and
- free of source authorization, capability lookup, or other observable work.

There is no empty or unknown declaration instance and no declaration without
an owner-issued `PlatformFamily`. An application lookup may still return a
typed unknown ecosystem or an unavailable projection under the handoff owner,
but lower declaration construction cannot produce a success-shaped value with
no population identity.

## A logical family, not a fixed roster

One declaration may be realized for different targets and views:

```text
DotNetRuntime
  + net11.0
  + ReferenceApi
  + WholePopulation
      -> one complete reference-family population or typed non-success

DotNetRuntime
  + net11.0
  + ImplementationBody
  + ExactLibrary(System.Text.Json)
      -> one demand-complete result or typed non-success
```

The declaration does not claim that those two populations contain identical
assembly names. Reference packs may contain facades or reference-only
assemblies; implementation layouts may contain private implementation
libraries. That difference is evidence, not an inconsistency to erase.

A later operation selects the target, required view, and demand. The
realization owner defines whether that demand is one exact library, a bounded
subset, or the whole declared population and returns a result complete for that
stated realization unit, or its typed unavailable, rejected, incomplete,
failed, or cancelled outcome. This declaration does not require whole-family
work for an exact-library request. When an operation requests and claims the
whole population, however, missing source work cannot become an empty or
shortened successful population.

The declaration is stable when a target, version, or source changes. A new
realization result carries the new target and source evidence; Workspace
registration does not have to be rewritten merely because the platform
version advances.

## Focus population and support closure

A selected population has two distinct roles:

| Role | Meaning |
| --- | --- |
| Focus member | A library the selected source attributes to the declared product family for the requested view |
| Support member | A library realized only because a focus member needs a coherent binding closure |

Only focus members form the registration-selected population. Support members
may participate in the binding context and may become reachable through a
broader consumer request, but their presence does not relabel them as members
of the selected declaration.

This distinction is required for ASP.NET Core. Realizing
`Microsoft.AspNetCore.App` implementations normally requires a compatible
`Microsoft.NETCore.App` dependency closure. Those runtime libraries support
ASP.NET Core binding; they do not become `AspNetCore` focus members. A
Workspace that separately registers `DotNetRuntime` may select those runtime
libraries through that declaration.

A source realization must preserve owner-issued family attribution for every
returned focus and support member. A consumer cannot infer the role from:

- namespace or assembly-name prefixes;
- directory or archive paths;
- package IDs;
- file names;
- reference versus implementation presence; or
- the order in which sources or members were enumerated.

If a source cannot distinguish focus members from support closure, it cannot
claim a focus/support-classified realization for this declaration.

## Source-specific correspondence

An integration boundary above each platform source explicitly maps a
declaration to the source owner's coordinate:

| Declaration | Example source coordinate |
| --- | --- |
| `DotNetRuntime` | `Microsoft.NETCore.App.Ref`, `Microsoft.NETCore.App`, or one .NET runtime implementation pack |
| `AspNetCore` | `Microsoft.AspNetCore.App.Ref`, `Microsoft.AspNetCore.App`, or one ASP.NET Core implementation pack |

The examples identify current source families; they do not define one
universal coordinate or fallback order.

Correspondence is owner-issued. Equal strings such as `runtime`,
`Microsoft.NETCore.App`, and a runtime-pack package ID do not establish that
they represent the same declaration. The integration boundary retains the
declaration beside its request and result, or mints an owner-issued receipt
binding the result back to it.

This boundary placement is required for installed realization. The
Source Selection project references package abstractions, while the
package-free installed-platform adapter closure must continue excluding package
and NuGet implementations. Queries or another integration component above both
owners translates `DotNetRuntime` or `AspNetCore` into the existing
installed-owner coordinate and joins the result back to the retained
declaration. The installed realization and its adapters do not reference
Source Selection and do not learn the declaration type.

An installed shared-framework realization, a downloaded reference pack, and a
remote implementation pack are different source contracts. A consumer may
choose among available capabilities under its own request, but one source's
absence does not authorize another source unless that consumer explicitly
allows the route.

## Relationship to existing platform values

### Search platform framework

Search may project:

```text
DotNetRuntime -> SearchPlatformFramework.Runtime
AspNetCore    -> SearchPlatformFramework.AspNetCore
```

That projection is search policy. It does not make
`SearchPlatformFramework` the declaration identity.

`SearchPlatformFramework.NetStandard` has no version-1 population declaration.
.NET Standard is a reference contract without an implementation population for
call-graph body traversal. Existing search support remains valid and separate.
Adding a .NET Standard registration population requires a separately approved
contract for its supported consumers and visible unavailable behavior.

### Workspace platform member

An operation may lower a declaration plus target and demand into
`WorkspaceMemberCoordinate.Platform`, for example:

```text
DotNetRuntime + net11.0 + all implementation libraries
  -> Platform(family: runtime, framework: net11.0, assembly: absent)
```

The coordinate is not reversible into a declaration. Its string family,
target, version, and optional assembly are acquisition fields. A realized
coordinate additionally carries producer evidence. Neither value belongs in
static ecosystem registration.

### Installed platform family

An integration component above the package-free installed boundary may map the
declaration to an exact `InstalledPlatformFamily` and later retain the
manifest-defined result beside the declaration. The installed family and
version are source coordinates. The declaration remains the target-independent
relevance identity; the installed adapter continues to consume only its own
coordinates.

### Assembly identity and provenance

Metadata remains authoritative for decoded assembly identity.
`AssemblyResolutionProvenance.PlatformAsset` records how one assembly was
selected after realization. Neither metadata identity nor provenance can
reconstruct the declaration or establish focus membership without the source
owner's correspondence.

## Ecosystem projection

Static Ecosystem Packs may contribute:

```text
ecosystem.platform
  -> Platform(DotNetRuntime)

ecosystem.aspnetcore
  -> Platform(AspNetCore)
     PackagePrefix(Microsoft.AspNetCore.)
```

Microsoft.Extensions contributes no platform declaration. Its registered
population is package-prefix-based even though a selected platform target may
subsume some Microsoft.Extensions package versions. Platform/package pruning
and the assembly-reference resolution ladder decide that overlap from exact
target evidence; the population declaration does not.

The application catalog retains these explicit associations. It does not map
an ecosystem to a platform population from equal display text, namespace roots,
core packages, package sets, or existing demos.

## Materialization and non-action

Constructing, comparing, discovering, or projecting a declaration:

- performs no filesystem or network work;
- enumerates no platform directory, package, archive, or browser index;
- selects no target or version;
- acquires and opens no assembly;
- invokes no platform resolver or package source;
- reserves no Workspace or operation budget;
- admits no Root or participant;
- grants no platform-role or core-library entitlement;
- creates no graph seed or edge; and
- makes no source availability or completeness claim.

The declaration can therefore be retained in a fresh empty Workspace and in
portable definitions. Later operations remain responsible for explicit finite
work and visible failure.

## Pathological cases

### ASP.NET Core pulls in the runtime

An ASP.NET Core implementation source realizes both
`Microsoft.AspNetCore.App` and its `Microsoft.NETCore.App` dependency. The
result marks ASP.NET Core libraries as focus members and runtime libraries as
support members. Returning the union as the `AspNetCore` focus population is a
contract violation.

### Reference-only and implementation-only libraries

A reference view contains a facade absent from the implementation focus set,
while the implementation view contains a private library absent from the
reference set. Both results may be complete for their requested views. The
declaration does not force a union or intersection and the consumer does not
substitute the wrong view.

### A call graph receives only reference assemblies

A call-graph operation asks to realize its registered focus population with
implementation bodies, but only a reference source is available. The source
returns typed unavailable or incomplete evidence for that demand. It does not
publish an empty graph or claim that the declared population has no calls.

### Equal names in different families

Two source populations carry the same assembly simple name. The declaration
does not merge them. Source realization and Metadata binding retain full
identity, family correspondence, ambiguity, and precedence evidence under
their owners.

### Stale browser inventory

A generated browser index lacks the current target. The declaration remains
valid, but the browser source cannot claim a complete result for that target.
It returns visible non-success or uses another explicitly authorized source;
it does not shorten the population.

### .NET Standard search

Search includes `SearchPlatformFramework.NetStandard`. That does not create a
`PlatformLibraryPopulationDeclaration` value. A registration consumer cannot
cast or infer the search enum into a supported implementation population.

## Analogous implementation evidence

.NET's [`FrameworkReference`][framework-reference] identifies a logical shared
framework such as `Microsoft.AspNetCore.App`, while the SDK and runtime resolve
the applicable targeting and implementation assets later. The useful
transferred property is the separation between logical framework intent and
its target-specific representations.

ASP.NET Core documentation explicitly distinguishes the
`Microsoft.AspNetCore.App` shared framework from separately published NuGet
packages. That supports retaining both `AspNetCore` and
`PackagePrefix(Microsoft.AspNetCore.)` as distinct population contributions.

This design does not transfer MSBuild evaluation, implicit SDK references,
hostfxr roll-forward, build output, application launch, or installed-layout
fallback behavior. Those systems are comparative evidence, not authority for
Workspace registration.

[framework-reference]: https://learn.microsoft.com/aspnet/core/fundamentals/target-aspnetcore#use-the-aspnet-core-shared-framework

## Ownership and adoption

| Owner | Responsibility retained |
| --- | --- |
| [Platform Target Currency](platform-target-currency.md) | Closed family and exact family-target identity |
| This declaration owner in Source Selection | Registration role, focus/support meaning, source-view separation, declaration equality, and non-action |
| [Workspace Ecosystem Registration Handoff](workspace-ecosystem-registration-handoff.md) | Platform contribution arm, application-pack correspondence, and curated-Workspace validation |
| Integration above source boundaries | Explicit declaration-to-source correspondence without importing Source Selection into package-free installed realization |
| Platform source adapters | Source-owned coordinates, target/view/demand selection, inventory, focus/support evidence, completion, and failures |
| [Platform composition](platform-composition-and-overlays.md) | Coherent package-free installed closure, realization, entitlement, precedence, and compatibility |
| Queries and Workspace | Integration lowering, retained correspondence, acquisition, admission, revisions, lifetimes, and selected population receipts |
| [Platform/package pruning](platform-package-pruning.md) | Exact target-bound package subsumption evidence |
| Metadata | Assembly identity, binding, provenance, and guarded inspection |
| Call Graph | Required implementation view, focal-length population composition, traversal bounds, completeness, and graph result |
| CLI and Inspect Web | Explicit raw-versus-curated construction choice, user intent, diagnostics, and interaction |

There are seven counted production-adoption steps:

1. Lock this focused declaration contract under #6328.
2. Implement the lower Platform target currency and have
   `DotnetInspector.SourceSelection` retain its closed family in the
   declaration.
3. Adopt the type in the Platform arm of the Queries-owned lower ecosystem
   registration declaration.
4. Project `DotNetRuntime` from `ecosystem.platform` and `AspNetCore` from
   `ecosystem.aspnetcore` in the application catalog's curated Workspace
   manifest.
5. Define and implement integration requests and source-realization results
   that preserve the declaration, exact target, view, demand, focus/support
   roles, completion claim, and typed non-success across installed/reference
   and remote implementation sources, without introducing Source Selection
   into the package-free installed adapter closure.
6. Have the CLI choose the Ecosystems-owned curated constructor where its
   command experience calls for curation, then lower selected declarations
   through the shared realization contract.
7. Have the Inspect Web application boundary make the same explicit
   construction choice and Browser Core consume the shared realization
   contract, retiring declaration inference from its private platform-index
   family strings.

Steps 3 and 4 compose with the remaining catalog work in #6012 stage 3.
Workspace Scope, Definitions, focal-length execution, and host editing retain
their later #6012 stages. Exact-library registration is a separate source-owner
prerequisite.

This component adds no rendering surface, so Markout and host-specific
rendering strategy do not apply.

## Demo

The application catalog constructs:

```text
ecosystem.platform
  Populations
    Platform(DotNetRuntime)

ecosystem.aspnetcore
  Populations
    Platform(AspNetCore)
    PackagePrefix(Microsoft.AspNetCore.)
```

A later call-graph request binds `AspNetCore` to `net11.0` and an
implementation-body view. The source result identifies:

```text
focus
  Microsoft.AspNetCore.*

support
  Microsoft.NETCore.App dependency closure
```

The exact member list comes from the selected source and target, not from this
declaration or the namespace illustration.

A neighboring API search may project the same declaration to an ASP.NET Core
reference-pack view. It may return facades that the implementation view does
not. Neither operation changes the retained registration declaration.

## Required gates

| Gate | Required observation |
| --- | --- |
| Closed declaration contract | Public construction exposes exactly `DotNetRuntime` and `AspNetCore`; values are immutable, stable, and distinct. |
| Resource-free construction | Creating and comparing declarations requires no host capability, path, cache, source, target, network, or Workspace. |
| Search separation | Runtime and ASP.NET Core search projections retain correspondence; .NET Standard cannot become a version-1 population declaration. |
| Catalog projection | Platform and ASP.NET Core packs retain the exact declared values; Microsoft.Extensions receives no platform population by inference. |
| Integration-layer separation | Declaration correspondence is retained above source-specific realization; the installed adapter closure remains package-free and consumes only installed-owner coordinates. |
| Demand-bound completion | Exact-library and bounded demands do not require whole-family realization; a result claiming the whole population cannot omit failed or unavailable work. |
| Focus/support boundary | ASP.NET Core realization keeps runtime dependency closure as support rather than focus. |
| View-specific completeness | Reference and implementation results each identify their requested view and cannot substitute for one another. |
| Visible non-success | Missing target, view, demand, source data, or incomplete claimed work does not become an empty successful result. |
| Browser correspondence | Browser transport retains declaration identity rather than reconstructing it from `netcore.app`, `aspnetcore.app`, assembly names, or index rows. |

The first four gates belong to the declaration, handoff, and catalog
implementation slices. The final six require the later integration,
source-realization, and host-adoption slices and are unverified until those
owners land their Release gates. The installed dependency boundary is already
owned by
`InstalledPlatformAdapterClosure_ExcludesPackageAndNuGetImplementations`; its
future integration gate must show that declaration lowering remains outside
that closure.

No source-code absence scan is required. Existing project dependency policy
continues to enforce that reusable components do not depend on the application
ecosystem catalog.

## Non-claims

This design does not define:

- a platform library catalog or a fixed assembly roster;
- source discovery, fallback, ranking, or availability;
- target-framework, exact version, roll-forward, or prerelease policy;
- reference, implementation, facade, or private-implementation classification;
- platform source requests, results, receipts, or operation budgets;
- installed hive, reference-pack, shared-framework, implementation-pack, or
  browser-index formats;
- package-prefix matching, package pruning, or package acquisition;
- full assembly identity, binding, forwarders, or platform precedence;
- Workspace registration storage, persistence, or editing;
- call-graph population composition or traversal;
- exact-library registration; or
- WindowsDesktop, .NET Standard, Xamarin, Mono, NativeAOT runtime-pack, or
  workload-specific population declarations.
