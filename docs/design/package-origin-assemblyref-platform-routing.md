# Package-origin AssemblyRef Platform routing

## Status and approved scope

This document is the focused composition owner for routing an external
`AssemblyRef` from an acquired package Library to an eligible Platform Library.
It is tracked by
[#8503](https://github.com/richlander/dotnet-inspect/issues/8503) as a
prerequisite of the
[Assembly Reference Resolution Ladder](assembly-reference-resolution-ladder.md)
adoption in
[#8466](https://github.com/richlander/dotnet-inspect/issues/8466).

The motivating production scenarios are:

- `Microsoft.Azure.SignalR` 1.33.1 targeting `net8.0`, where
  `Microsoft.Azure.SignalR.dll` references
  `Microsoft.AspNetCore.SignalR.Core`, the selected package target declares a
  `Microsoft.AspNetCore.App` framework reference, and the requested assembly
  is available from the ASP.NET Core reference and runtime packs; and
- any package Library whose Metadata references `System.Text.Json` without a
  `System.Text.Json` package dependency, where ordinary API binding uses the
  .NET Runtime reference pack and implementation traversal uses the
  RID-specific .NET Runtime pack.

## Authority and exact claim

**Package-origin AssemblyRef Platform routing** owns:

> Given one exact external `AssemblyRef` from an acquired package Library, a
> completed referencing-context `NoNameOwner`, owner-issued Platform-family
> eligibility, a package graph whose actual edges have terminal pruning
> decisions, an exact selected Platform family composition, and owner-issued
> membership for the requested Platform Library, authorize at most one exact
> PlatformHouse assembly-reference operation without requiring a same-named
> package edge, deriving an assembly identity from a package identity, or
> acquiring a package edge already delegated to the Platform.

It owns:

- association of the exact referencing occurrence and `AssemblyRef` with the
  selected package target and Platform-family eligibility evidence;
- the order in which package pruning, retained-package competition, Platform
  family eligibility, and PlatformHouse invocation compose;
- the rule that a `Subsumed` package edge is conclusively delegated before
  AssemblyRef routing;
- composition of owner-issued exact Platform Library membership with the
  unchanged Metadata request;
- authorization of one exact PlatformHouse assembly-reference request after
  the ordinary context misses;
- preservation of every retained or delegated package-edge receipt beside the
  route result; and
- visible incomplete or unavailable composition when any required owner result
  is absent.

It does not own:

- package dependency parsing, candidate resolution, or target selection;
- framework-reference parsing or nearest-framework selection;
- package-prune inventory construction or version comparison;
- Platform target selection, catalog membership, assembly binding, or view
  correspondence;
- reference-pack or runtime-pack package coordinates and member selection;
- package transport, Range requests, caching, or payload lifetime;
- ladder precedence, Workspace replacement, or final binding outcomes; or
- presentation.

[Platform package supply policy](platform-package-supply-policy.md) remains the
sole owner of package-edge delegation.
[PlatformHouse realization and reference processing](platform-house-reference-processing.md)
owns exact Platform assembly-reference resolution.
[Package-backed Platform realization](package-backed-platform-realization.md)
owns mapping a Platform family target to its reference or runtime distribution
package and selecting its exact member.
The Assembly Reference Resolution Ladder owns the final route order and result.

## The primary problem is AssemblyRef-to-Platform mapping

The route starts with Metadata identity, not package identity:

```text
package Library occurrence
  -> exact Metadata AssemblyRef
  -> referencing context: NoNameOwner
  -> selected framework references establish eligible Platform families
  -> exact Platform family target and source plan
  -> owner-issued exact Platform Library membership
  -> PlatformHouse ResolveAssemblyReference
  -> exact Platform Library or typed non-success
```

Owner-issued Platform-family evidence answers which families participate. An
explicit package framework reference can admit ASP.NET Core. The selected
Platform context can independently admit the baseline .NET Runtime family.
Neither identifies a Platform assembly. The exact `AssemblyRef` answers which
assembly identity to bind. Bounded Platform catalog or source discovery
establishes whether the selected Platform population contains the requested
Library before the route is issued. PlatformHouse then binds the exact
authoritative realization under its own policy.

This path does not search NuGet for a component package named like the
assembly. For modern ASP.NET Core, many shared-framework assemblies have no
same-version component package at all. The NuGet-resident packages used by the
Platform source are the targeting and runtime packs themselves.

The same rule applies to .NET Runtime Libraries. An `AssemblyRef` to
`System.Text.Json` does not require a `System.Text.Json` package edge. When no
such edge exists, pruning has no input and performs no work; the exact
AssemblyRef proceeds directly through the eligible .NET Runtime Platform
family.

## Package-backed reference and implementation views

For an exact Reference-view request, the package-backed Platform source derives
the reference-pack package ID from `PlatformFamilyTarget`:

| Platform family | Reference distribution package |
| --- | --- |
| .NET Runtime | `Microsoft.NETCore.App.Ref` |
| ASP.NET Core | `Microsoft.AspNetCore.App.Ref` |

The exact Platform version is the package version. The source projects the
requested assembly simple name to `ref/<tfm>/<name>.dll` only as an acquisition
optimization. It requires one member at that coordinate and verifies the
decoded complete Metadata assembly identity against the original
`AssemblyRef`. Equal filenames are not the completion gate.

For an Implementation-view request, the family and explicit RID derive the
runtime-pack coordinate:

| Platform family | Runtime distribution package |
| --- | --- |
| .NET Runtime | `Microsoft.NETCore.App.Runtime.<rid>` |
| ASP.NET Core | `Microsoft.AspNetCore.App.Runtime.<rid>` |

The runtime source follows the selected framework's runtime configuration and
dependency manifest, selects the exact managed member, and retains
reference-to-implementation view correspondence. ASP.NET Core may compose the
.NET Runtime implementation framework as binding support.

Implementation traversal does not invent a PackageRef. For example, after a
reference-view `System.Text.Json` binding, call-graph traversal or
decompilation requiring bodies follows PlatformHouse view correspondence to
`Microsoft.NETCore.App.Runtime.<rid>` and selects its
`System.Text.Json.dll`.

The package source may satisfy either operation by reading the package ZIP
central directory and then issuing a Range request for the exact selected
member. Full download remains a transport-policy choice. In either case,
Platform source settlement must retain the exact package candidate, authority,
producer, content generation, payload origin, member coordinate, digest, and
decoded assembly identity. This composition document neither requires nor
implements a particular transfer path.

## Azure SignalR walkthrough

For `Microsoft.Azure.SignalR` 1.33.1 at `net8.0`:

1. PackageHouse selects `lib/net8.0/Microsoft.Azure.SignalR.dll`.
2. The selected package target carries a `Microsoft.AspNetCore.App` framework
   reference.
3. Metadata observes an `AssemblyRef` to
   `Microsoft.AspNetCore.SignalR.Core`.
4. The already selected package Library context returns `NoNameOwner`.
5. The framework reference makes the ASP.NET Core family eligible.
6. Platform target policy supplies one exact ASP.NET Core 8 target.
7. Owner-issued Platform evidence establishes exact membership for the
   requested Library.
8. PlatformHouse asks the selected Reference source for the exact
   `AssemblyRef`.
9. The package-backed source derives `Microsoft.AspNetCore.App.Ref@8.0.x`,
   selects
   `ref/net8.0/Microsoft.AspNetCore.SignalR.Core.dll`, and verifies its
   Metadata identity.
10. API, type, member, and reference binding can use that Reference view.
11. Decompilation, body analysis, or another implementation-demanding
    operation separately selects the corresponding
    `Microsoft.AspNetCore.App.Runtime.<rid>` member.

There is no `Microsoft.AspNetCore.SignalR.Core@8.0.0` package edge in the
selected `net8.0` package group. No pruning or package-to-Library
correspondence is needed for this AssemblyRef. The route is Platform-eligible
because framework-reference evidence admits the family and owner-issued
Platform evidence independently proves exact assembly membership.

## Runtime Library walkthrough without a PackageRef

For an acquired `net8.0` package Library that references `System.Text.Json` but
declares no `System.Text.Json` package dependency:

1. Metadata observes the exact `System.Text.Json` `AssemblyRef`.
2. The selected package Library context returns `NoNameOwner`.
3. Package reachability contains no `System.Text.Json` edge, so pruning neither
   creates nor classifies one.
4. The selected Platform context admits the .NET Runtime family.
5. Owner-issued Platform evidence establishes exact membership for
   `System.Text.Json`.
6. PlatformHouse resolves the reference identity from
   `Microsoft.NETCore.App.Ref@8.0.x`.
7. If the operation requests implementation traversal, PlatformHouse follows
   exact view correspondence to
   `Microsoft.NETCore.App.Runtime.<rid>@8.0.x`.
8. The package-backed source reads the exact `System.Text.Json.dll` member,
   potentially with a Range request, and verifies its Metadata identity.

The absence of a `System.Text.Json` PackageRef is ordinary. Package discovery
is not a prerequisite for Platform Library discovery.

## Pruning is a conclusive earlier decision

Package pruning operates on package edges before external AssemblyRef route
competition:

```text
selected package dependency graph
  -> exact coordinate for each edge
  -> PlatformPrunePolicy
     -> Subsumed: retain delegation receipt; do not acquire package edge
     -> NotSubsumed or NotComparable: retain package route
  -> AssemblyRef routing over Platform plus retained package routes
```

`PlatformSupplyReceipt.DelegatesToPlatform` is conclusive when its result is
`Subsumed`. The delegated edge does not need a second package-to-Platform
Library correspondence before package acquisition can be skipped. Pruning is
package-wide; it does not classify individual `AssemblyRef` values.

An assembly reference without a corresponding package edge bypasses this stage.
The prune inventory must not manufacture a package edge merely because it
contains a same-named package identity.

After pruning:

- a delegated edge remains visible as provenance explaining why no package
  payload was acquired, but it is not a competing package route;
- a retained edge may compete only when selected package-role evidence
  establishes that the package can own the exact requested assembly identity;
- Platform membership is always evaluated independently from the original
  `AssemblyRef`; and
- equal package and assembly names are unnecessary to the decision.

This preserves the boundary already stated by the pruning owner: package
identity decides whether a package edge delegates, while Metadata identity and
PlatformHouse decide whether a Platform Library satisfies an `AssemblyRef`.

## Interpreting the adjacent targeting-pack data

An exact .NET 11 targeting pack may contain:

```text
System.Text.Json|11.0.0-rc.1.26425.128
System.Text.Json.dll|Microsoft.NETCore.App.Ref|11.0.0.0|11.0.26.42628
```

Both records are conclusive in their own domains:

- the `PackageOverrides.txt` row says that the exact target supplies package
  identity `System.Text.Json` through the stated NuGet version; and
- the `PlatformManifest.txt` row says that
  `System.Text.Json.dll` belongs to the stated Platform distribution with the
  stated assembly and file versions.

The records do not serialize a package-to-assembly edge: the first contains no
assembly identity, and the second names the containing targeting pack rather
than a component package. That absence is not a blocker because this route
does not require such an edge.

For `System.Text.Json@9.0.0`:

1. pruning conclusively delegates the package edge;
2. no `System.Text.Json` package payload is acquired;
3. the independent `System.Text.Json` `AssemblyRef` is resolved against the
   exact Platform population; and
4. the Platform source acquires the Reference or Implementation pack member
   required by the operation.

For `System.Text.Json@12.0.0`, pruning retains the package edge. Its selected
package assets may then establish package name ownership and compete with the
Platform route. The same Platform membership does not override the retained
package decision.

The matching text in the two sample rows is useful audit evidence but is not a
join the host needs to perform.

If there is no `System.Text.Json` package edge, the first row is not consulted
for that AssemblyRef. The second row and the exact Platform population support
Platform membership, while implementation traversal selects the runtime-pack
member independently.

## Composition contract

The route-preparation input retains:

- the exact referencing package Library occurrence;
- the exact Metadata `AssemblyBindingRequest`;
- the completed referencing-context `NoNameOwner`;
- the selected package target and PackageHouse receipt;
- owner-issued Platform-family eligibility, including selected
  framework-reference evidence when applicable;
- the complete package-reachability snapshot;
- one terminal `PlatformSupplyReceipt` for every package edge to which pruning
  applies;
- every retained package route and any selected-role assembly evidence already
  available for it;
- the exact selected Platform family composition and target; and
- owner-issued exact Platform Library membership for the unchanged
  `AssemblyRef`; and
- the Workspace, source-plan, operation, and work-ledger identities.

The composition validates:

1. all package and framework evidence belongs to the same selected PackageHouse
   result and target;
2. every package edge is either delegated by an exact `Subsumed` receipt or
   remains represented as a retained package route;
3. no delegated edge appears as a package acquisition candidate;
4. each eligible Platform family was selected by owner-issued package-target,
   framework-reference, or Workspace evidence rather than an assembly-name
   prefix;
5. the exact Platform target, source plan, and Library membership projection
   correspond to that family composition; and
6. the membership projection and PlatformHouse request carry the unchanged
   Metadata binding request.

Missing reachability, unresolved pruning, unavailable framework-reference
evidence, unsettled target selection, unavailable Platform membership, or an
incomplete retained-package name-ownership question preserves the producing
owner's typed non-success. It does not become Platform preference.

## Retained-package competition

Pruning removes delegated edges from package competition. For remaining edges,
the ladder still needs complete package name-ownership evidence before it can
prefer Platform or report absence.

That evidence comes from package-selected roles:

- a restored artifact may already retain exact selected assets;
- PackageHouse acquisition and target selection may project the exact compile
  or runtime assembly identities; or
- a complete selected package role may return `NoNameOwner`.

A package declaration, package ID, filename, or equal simple name does not
replace selected-role evidence. This rule applies only to retained package
routes. It does not reopen a package edge that pruning already delegated.

## Closed preparation outcomes

Route preparation produces:

- **Applicable** — the exact Platform family is eligible, the target and source
  plan are settled, owner-issued evidence establishes exact Platform Library
  membership for the unchanged request, all package edges are terminally
  pruned or retained, and no retained package route owns the requested
  assembly;
- **PackageOwned** — one or more retained package routes own the requested
  assembly and remain available to the package rung;
- **NoEligiblePlatform** — selected framework and Workspace evidence admit no
  Platform family for the request;
- **Unavailable** — required Platform target, source, or catalog evidence is
  unavailable;
- **Ambiguous** — eligible Platform or retained package evidence has several
  equally valid owners;
- **Incomplete** — reachability, pruning, selected-role, target, or finite-work
  evidence cannot close; or
- **Failed** — an owner failed while producing required evidence.

`Applicable` authorizes one PlatformHouse operation. It does not turn later
source realization or binding-policy failure into success. PlatformHouse
preserves `NameOwnedNoMatch`, ambiguity, unavailable, rejected, incomplete, or
failed evidence under its own contract.

## Pathological cases

### Framework family without the Library

A selected framework reference makes the family eligible, but exact Platform
membership remains absent. Route preparation preserves the Platform catalog or
source owner's typed absence and issues no applicable-platform rung. It does
not search NuGet by assembly name.

### Subsumed package with no same-named assembly

A package edge delegates because its exact package coordinate is `Subsumed`.
The absence of a same-named Platform assembly does not reverse pruning.
AssemblyRefs are resolved independently; any missing required Platform member
remains a visible Platform failure.

### Runtime AssemblyRef without a PackageRef

The package graph contains no edge for `System.Text.Json`, but Metadata contains
an exact `System.Text.Json` `AssemblyRef`. The route performs no pruning lookup
for a synthetic edge. It resolves the Platform reference Library and, when
traversal requires bodies, its runtime-pack implementation.

### Retained package and Platform both own the name

A package above the prune ceiling remains package-owned. Exact selected package
asset evidence establishes its name ownership, so the package rung dominates
the same-request Platform candidate under the ladder's precedence contract.

### Several framework families

ASP.NET Core may compose .NET Runtime binding support. The explicit family
composition and Platform source policy determine the candidate population.
Assembly-name prefixes and pack enumeration order do not choose a family.

## Production adoption

The end-to-end adoption has three remaining slices:

1. Add orchestration that associates PackageHouse framework-reference evidence
   and terminal package-pruning receipts with owner-issued exact Platform
   membership for one external `AssemblyBindingRequest`.
2. Invoke the existing installed and package-backed PlatformHouse
   assembly-reference sources from the applicable-platform rung, preserving
   Reference and Implementation view demands and source evidence.
3. Adopt the same route preparation and ladder result in CLI and Browser/Wasm,
   then retire host-local platform-name lookup and package/platform overlap
   rules.

PackageHouse framework-reference evidence is already implemented. PlatformHouse
and its installed and package-backed exact-assembly adapters are already
implemented. This design supplies the missing composition between them; it
does not introduce another package/Library catalog.

No rendering strategy applies. The result is host-neutral route evidence, not
a section or broad information domain.

## Evidence gates

Future Release gates must prove:

- `Microsoft.Azure.SignalR` 1.33.1 at `net8.0` selects
  `Microsoft.AspNetCore.App`, observes the
  `Microsoft.AspNetCore.SignalR.Core` `AssemblyRef`, and resolves it from the
  exact ASP.NET Core reference pack without a component-package lookup;
- an implementation-demanding operation selects the corresponding runtime-pack
  member under an explicit RID;
- a real package Library with a `System.Text.Json` `AssemblyRef` and no
  `System.Text.Json` package edge resolves through the .NET Runtime reference
  pack and traverses through the RID-specific runtime-pack member;
- package-backed realization verifies Metadata identity after member-path
  projection;
- `System.Text.Json@9.0.0` delegates through exact pruning, acquires no package
  payload, and independently resolves the Platform AssemblyRef;
- `System.Text.Json@12.0.0` retains its package route despite identical
  Platform membership;
- a framework reference to a family lacking the requested Library returns the
  Platform owner's typed absence and issues no applicable-platform rung;
- incomplete pruning never becomes Platform preference;
- a delegated edge is not reopened for package acquisition;
- a retained package route needs selected-role assembly evidence rather than
  package-name equality; and
- equivalent CLI and Browser/Wasm inputs produce the same route-preparation
  outcome.

The Azure SignalR and System.Text.Json cases use real nuget.org packages.
Synthetic fixtures cover missing Platform membership, retained-package
ambiguity, incomplete pruning, and source failure.

## Non-goals

- Mapping package IDs to Platform Library identities.
- Searching NuGet by assembly simple name.
- Treating pruning as an AssemblyRef classifier.
- Reopening a `Subsumed` package edge.
- Redefining package pruning, PlatformHouse binding, or ladder precedence.
- Selecting a Platform family from an assembly-name prefix.
- Supporting Windows Metadata.
- Rendering route preparation.
