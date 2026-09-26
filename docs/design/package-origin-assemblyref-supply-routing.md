# Package-origin AssemblyRef supply routing

## Status and approved scope

This document is the focused composition owner for associating one external
`AssemblyRef` from an acquired package Library with an ordinary Package
supplier or, when applicable, a Platform supplier. It is tracked by
[#8503](https://github.com/richlander/dotnet-inspect/issues/8503) as a
prerequisite of the
[Assembly Reference Resolution Ladder](assembly-reference-resolution-ladder.md)
adoption in
[#8466](https://github.com/richlander/dotnet-inspect/issues/8466).

The motivating production scenarios are:

- an unpruned `System.Text.Json@10.0.0` PackageRef whose selected package
  content supplies `System.Text.Json.dll`;
- Polly-family AssemblyRefs whose reachable PackageRefs and selected package
  content identify the supplying package;
- `Microsoft.Azure.SignalR` 1.33.1 targeting `net8.0`, where
  `Microsoft.Azure.SignalR.dll` references
  `Microsoft.AspNetCore.SignalR.Core`, the selected package target declares a
  `Microsoft.AspNetCore.App` framework reference, and the requested assembly
  is available from the ASP.NET Core reference and runtime packs; and
- a package Library whose Metadata references `System.Text.Json` without a
  `System.Text.Json` PackageRef, where the selected .NET Runtime Platform
  supplies the assembly.

## Authority and exact claim

**Package-origin AssemblyRef supply routing** owns:

> Given one exact external `AssemblyRef` from an acquired package Library, a
> completed referencing-context `NoNameOwner`, complete reachable PackageRef
> evidence, selected package-role evidence when acquired, and eligible
> Platform evidence when applicable, associate the request first with ordinary
> Package suppliers and then with the Platform specialization without treating
> package identity, filename, or Platform membership as a substitute for the
> final Metadata binding decision.

It owns:

- association of the exact referencing occurrence and `AssemblyRef` with its
  selected package target;
- resource-free candidate correlation between the `AssemblyRef` simple name
  and actual reachable PackageRefs;
- composition of package candidate correlation, selected package content, and
  the unchanged Metadata binding request;
- the corresponding specialization over eligible Platform families, exact
  Platform Library membership, and Platform source evidence;
- preservation of package-pruning decisions as orthogonal input rather than
  AssemblyRef classification;
- the complete supplier-association result consumed by the Assembly Reference
  Resolution Ladder; and
- visible incomplete, unavailable, ambiguous, or failed composition when a
  required owner result does not settle.

It does not own:

- package dependency parsing, candidate version resolution, or target
  selection;
- package-prune inventory construction or version comparison;
- package asset selection, archive transport, or payload lifetime;
- framework-reference parsing or nearest-framework selection;
- Platform target selection, catalog construction, assembly binding, or view
  correspondence;
- reference-pack or runtime-pack coordinates and member selection;
- ladder precedence, Workspace replacement, or final binding outcomes; or
- presentation.

[Package Dependency Evidence](package-dependency-evidence.md) owns actual
PackageRef declarations and reachability.
[Platform package supply policy](platform-package-supply-policy.md) owns
package-edge delegation.
[PlatformHouse realization and reference processing](platform-house-reference-processing.md)
owns exact Platform assembly-reference resolution.
[Package-backed Platform realization](package-backed-platform-realization.md)
owns Platform reference- and runtime-pack realization.
The Assembly Reference Resolution Ladder owns final route precedence and
result.

## AssemblyRef supply routing is the general problem

The route starts from one exact Metadata request:

```text
package Library occurrence
  -> exact Metadata AssemblyRef
  -> referencing context: NoNameOwner
  -> complete external supplier association
     -> ordinary Package supplier candidates
     -> eligible Platform supplier candidates
  -> selected supplier realization
  -> decoded Metadata identity and binding policy
  -> exact Library or typed non-success
```

The referencing-context `NoNameOwner` establishes only that the already
selected Library context does not supply the name. It neither chooses an
external package nor makes Platform applicable.

Package and Platform routes use the same evidence pattern:

| Stage | Ordinary Package route | Platform specialization |
| --- | --- | --- |
| Supplier eligibility | Actual reachable PackageRef | Owner-issued eligible Platform family |
| Resource-free correlation | Exact namesake or boundary-aligned package-family prefix | Exact Platform Library membership |
| Member correlation | Selected package-role filename or validated identity inventory | Reference- or runtime-pack member |
| Completion gate | Decoded `AssemblyDef` identity and Metadata binding policy | Decoded `AssemblyDef` identity and Metadata binding policy |

The correlation stages answer where bounded work should look. They do not
replace Metadata identity or binding policy.

## Ordinary Package routing is primary

The ordinary external route begins with actual PackageRefs reachable from the
referencing package under the selected target. It never performs an
ecosystem-wide NuGet search from the assembly name.

### PackageRef candidate correlation

The request simple name is compared with canonical reachable Package IDs using
ordinal-ignore-case package identity semantics:

- **Exact namesake** — package ID and assembly simple name are equal.
- **Package-family prefix** — one identity is the other's prefix at a `.`
  boundary, such as a `Polly` family reference and a `Polly.*` package.
- **No name affinity** — the PackageRef is not a resource-free candidate from
  package identity alone.

Exact and prefix matches are candidate evidence, not proof that the package
contains the assembly. A separately owner-issued selected-role inventory may
nominate a package with no name affinity. Conversely, the absence of name
affinity cannot support a complete no-owner claim unless complete selected-role
evidence has excluded that package.

This keeps the common path cheap without claiming that every package follows a
package-ID-to-assembly naming convention.

### Selected package-content correlation

For each retained candidate PackageRef:

1. candidate resolution supplies one exact authorized package coordinate;
2. PackageHouse selects the compile and implementation roles for the request;
3. an exact filename match such as `System.Text.Json.dll` identifies the
   likely selected member;
4. the member is acquired, potentially through a Range request; and
5. its decoded complete `AssemblyDef` identity is evaluated against the
   original `AssemblyRef`.

The filename match is package-content evidence and a member-acquisition
optimization. It is not the binding authority. A selected `Alias.dll` whose
Metadata identity is `Contoso.Real` participates as `Contoso.Real`; a complete
selected-role identity inventory may therefore establish a supplier that
filename correlation alone would miss.

Likewise, a same-named file with an incompatible version, culture, public key,
or content remains `NameOwnedNoMatch`, rejected, or another owner-issued
non-success. It is not a successful binding.

### `System.Text.Json@10.0.0`

For a package Library with an external `System.Text.Json` AssemblyRef and an
actual `System.Text.Json@10.0.0` dependency on a .NET 9 target:

1. the PackageRef is reachable under the selected package target;
2. pruning returns `NotSubsumed`, so the package edge remains;
3. exact namesake correlation nominates that retained PackageRef;
4. selected package content supplies `System.Text.Json.dll`;
5. decoded Metadata confirms whether its identity satisfies the AssemblyRef;
   and
6. the resulting Package owner prevents the same request from selecting the
   Platform specialization.

The package version is never compared with the AssemblyRef version. Pruning
compares package versions; Metadata binding compares assembly identities.

### Polly-family packages

Polly illustrates both ordinary correlations:

- exact PackageRef and AssemblyRef names identify namesake candidates such as
  `Polly.Core`;
- a boundary-aligned `Polly`/`Polly.*` relation identifies a package-family
  candidate without claiming ownership; and
- the selected package role and decoded assembly identity establish which
  package actually supplies `Polly.dll`, `Polly.Core.dll`, or
  `Polly.Extensions.Http.dll`.

A broad shared prefix does not search NuGet, admit an undeclared package, or
bind an assembly. It operates only over the complete reachable PackageRef set
and must be confirmed by selected package content.

## Platform is a supplier specialization

Platform routing follows the same shape after ordinary Package association
finds no retained Package owner:

```text
exact AssemblyRef
  -> owner-issued eligible Platform families
  -> exact Platform Library membership
  -> reference- or runtime-pack member correlation
  -> decoded Metadata identity and binding policy
```

The specialization differs in how supplier eligibility is established:

- an explicit package framework reference can admit ASP.NET Core;
- the selected Platform context can admit the baseline .NET Runtime family;
  and
- neither a target framework nor an assembly-name prefix admits every
  installed Platform family.

Exact Platform catalog or source evidence then establishes membership for the
unchanged AssemblyRef. A known reference- or runtime-pack coordinate replaces
PackageRef candidate resolution. The selected member still requires decoded
Metadata identity before binding completes.

For an exact Reference-view request, package-backed Platform realization uses:

| Platform family | Reference distribution package |
| --- | --- |
| .NET Runtime | `Microsoft.NETCore.App.Ref` |
| ASP.NET Core | `Microsoft.AspNetCore.App.Ref` |

For an Implementation-view request with an explicit RID, it uses:

| Platform family | Runtime distribution package |
| --- | --- |
| .NET Runtime | `Microsoft.NETCore.App.Runtime.<rid>` |
| ASP.NET Core | `Microsoft.AspNetCore.App.Runtime.<rid>` |

Projecting the request name to `ref/<tfm>/<name>.dll` or the corresponding
runtime member is an acquisition optimization. Decoded Metadata identity
remains the completion gate.

## Azure SignalR Platform specialization

For `Microsoft.Azure.SignalR` 1.33.1 at `net8.0`:

1. PackageHouse selects `lib/net8.0/Microsoft.Azure.SignalR.dll`.
2. The selected package target carries a `Microsoft.AspNetCore.App` framework
   reference.
3. Metadata observes an `AssemblyRef` to
   `Microsoft.AspNetCore.SignalR.Core`.
4. The already selected package Library context returns `NoNameOwner`.
5. No retained reachable PackageRef supplies the request.
6. The framework reference admits the ASP.NET Core Platform family.
7. Platform target policy supplies one exact ASP.NET Core 8 target.
8. Owner-issued Platform evidence establishes exact membership for the
   requested Library.
9. The Reference source selects
   `ref/net8.0/Microsoft.AspNetCore.SignalR.Core.dll` from
   `Microsoft.AspNetCore.App.Ref@8.0.x` and verifies its Metadata identity.
10. An implementation-demanding operation separately follows view
    correspondence to the
    `Microsoft.AspNetCore.App.Runtime.<rid>@8.0.x` member.

There is no modern
`Microsoft.AspNetCore.SignalR.Core@8.0.0` component PackageRef to correlate.
Platform supplies the same AssemblyRef through a different eligibility and
member-discovery path.

## Runtime Platform specialization without a PackageRef

For a package Library that references `System.Text.Json` but declares no
`System.Text.Json` PackageRef:

1. Metadata observes the exact `System.Text.Json` `AssemblyRef`.
2. The selected package Library context returns `NoNameOwner`.
3. The reachable PackageRef set contains no ordinary Package candidate.
4. The selected Platform context admits the .NET Runtime family.
5. Owner-issued Platform evidence establishes exact `System.Text.Json`
   membership.
6. Reference work selects `System.Text.Json.dll` from
   `Microsoft.NETCore.App.Ref`.
7. Implementation work follows view correspondence to
   `Microsoft.NETCore.App.Runtime.<rid>`.
8. The selected member's Metadata identity completes binding.

The Platform route does not invent a PackageRef. Absence of a same-named
PackageRef is an ordinary reason to continue from Package association to the
Platform specialization.

## Pruning is higher-level and orthogonal

Package pruning classifies actual package edges independently from any
AssemblyRef:

```text
selected package dependency graph
  -> exact coordinate for each edge
  -> PlatformPrunePolicy
     -> Subsumed: retain delegation receipt; remove Package supplier
     -> NotSubsumed or NotComparable: retain Package supplier
  -> AssemblyRef supplier association
```

`PlatformSupplyReceipt.DelegatesToPlatform` is conclusive when its result is
`Subsumed`. It authorizes skipping package acquisition without proving that
any particular AssemblyRef belongs to Platform.

Pruning can therefore change a scenario only in one direction:

```text
retained PackageRef + matching selected content
  -> ordinary Package supplier

the same PackageRef after Subsumed pruning
  -> no Package supplier from that edge
  -> eligible Platform specialization may independently supply the AssemblyRef
```

Platform resolution never creates, restores, or redirects to a Package edge.
An AssemblyRef with no corresponding PackageRef has no pruning input. A
subsumed package whose assemblies do not exist in the selected Platform
remains conclusively pruned; the independent AssemblyRef then produces the
Platform owner's typed absence.

## Interpreting adjacent targeting-pack data

An exact .NET 11 targeting pack may contain:

```text
System.Text.Json|11.0.0-rc.1.26425.128
System.Text.Json.dll|Microsoft.NETCore.App.Ref|11.0.0.0|11.0.26.42628
```

The records are conclusive in separate stages:

- the `PackageOverrides.txt` row decides whether an actual
  `System.Text.Json` PackageRef is `Subsumed`; and
- the `PlatformManifest.txt` row establishes `System.Text.Json.dll` membership
  in the Platform distribution.

They do not serialize a direct package-to-assembly relation, and none is
required.

- With `System.Text.Json@10.0.0` on .NET 9, pruning retains the edge; ordinary
  Package correlation plus selected content can establish the supplier.
- With a subsumed `System.Text.Json` edge, the Package supplier is removed and
  exact Platform membership can establish the specialized supplier.
- With no `System.Text.Json` edge, the prune row is irrelevant and exact
  Platform membership can still establish the supplier.

## Composition contract

The supplier-association input retains:

- the exact referencing package Library occurrence;
- the exact Metadata `AssemblyBindingRequest`;
- the completed referencing-context `NoNameOwner`;
- the selected package target and PackageHouse receipt;
- the complete reachable PackageRef snapshot;
- one terminal `PlatformSupplyReceipt` for every edge to which pruning
  applies;
- every retained PackageRef and its exact or prefix correlation result;
- selected package-role filename and decoded identity evidence when available;
- owner-issued Platform-family eligibility when the specialization is
  evaluated;
- the exact selected Platform family composition and target when eligible;
- owner-issued exact Platform Library membership for the unchanged request
  when that family is evaluated; and
- Workspace, source-plan, operation, and work-ledger identities.

The composition validates:

1. package and framework evidence belongs to the same selected PackageHouse
   result and target;
2. every actual package edge is delegated or retained by its pruning result;
3. no delegated edge appears as a Package acquisition candidate;
4. every Package candidate is an actual reachable PackageRef;
5. package-ID and filename correlation remain candidate evidence until decoded
   Metadata identity settles ownership;
6. Platform-family eligibility comes from owner-issued framework or Workspace
   evidence;
7. Platform membership and source evidence correspond to the selected family
   and target; and
8. every final supplier evaluation carries the unchanged Metadata request.

## Closed association outcomes

Supplier association produces:

- **PackageOwned** — one or more retained selected package roles own the
  requested assembly;
- **PlatformApplicable** — no retained Package route owns the request and one
  eligible Platform membership can supply it;
- **NoSupplier** — complete Package and Platform evidence establishes no name
  owner;
- **Unavailable** — required Package or Platform evidence is unavailable;
- **Ambiguous** — several equally eligible suppliers own the request;
- **Incomplete** — reachability, pruning, selected-role, Platform, or
  finite-work evidence cannot settle; or
- **Failed** — an owner failed while producing required evidence.

The ladder consumes these outcomes under its existing precedence. The
association owner does not select a lower-precedence supplier while a
higher-precedence candidate remains unsettled.

## Pathological cases

### Exact PackageRef name with no matching member

A namesake PackageRef is a candidate, but its selected role contains no
matching filename or decoded assembly identity. It does not own the
AssemblyRef. Complete remaining supplier evidence decides whether another
Package or Platform route may proceed.

### Prefix PackageRef with unrelated content

A boundary-aligned package-family prefix nominates a candidate but grants no
ownership. Selected content that lacks the requested assembly closes that
candidate as `NoNameOwner`.

### Non-namesake package containing the assembly

A complete owner-issued selected-role inventory can nominate a package whose
ID has no name affinity. Decoded Metadata identity, not the package ID or
filename, determines ownership.

### Retained Package and Platform both supply the name

An unpruned Package route whose selected content owns the AssemblyRef prevents
the Platform specialization from selecting that request. Platform membership
does not override the retained Package supplier.

### Subsumed Package with no matching Platform Library

The package edge remains conclusively delegated. Missing Platform membership
is a typed Platform absence, not permission to reopen package acquisition.

## Production adoption

The end-to-end adoption has three remaining slices:

1. Add host-neutral supplier association over PackageHouse dependency,
   selected-role, pruning, framework-reference, and exact Platform membership
   evidence for one `AssemblyBindingRequest`.
2. Invoke existing PackageHouse and PlatformHouse sources for the selected
   Package or Platform supplier while preserving Reference and Implementation
   view demands.
3. Adopt the same association and ladder result in CLI and Browser/Wasm, then
   retire host-local package-name, Platform-name, and overlap routing.

PackageHouse dependency and framework-reference evidence already exist.
PlatformHouse and its installed and package-backed exact-assembly adapters
already exist. This design supplies the missing common composition between
them; it does not introduce a package-to-Library catalog.

No rendering strategy applies. The result is host-neutral route evidence, not
a section or broad information domain.

## Evidence gates

Future Release gates must prove:

- `System.Text.Json@10.0.0` on .NET 9 remains unpruned, is correlated by exact
  PackageRef name and selected `System.Text.Json.dll` content, and binds
  through decoded Metadata identity;
- real Polly-family dependencies exercise exact and boundary-prefix PackageRef
  correlation followed by selected-content verification;
- a same-named or prefix-related PackageRef without the assembly does not own
  the request;
- a selected package member with an unrelated filename can participate only
  through owner-validated Metadata identity;
- `Microsoft.Azure.SignalR` 1.33.1 at `net8.0` resolves
  `Microsoft.AspNetCore.SignalR.Core` through the ASP.NET Core Platform
  specialization without a component PackageRef;
- a real package Library with a `System.Text.Json` `AssemblyRef` and no
  `System.Text.Json` PackageRef resolves through the .NET Runtime Platform
  specialization;
- `System.Text.Json@9.0.0` delegates through exact pruning, acquires no package
  payload, and independently resolves through Platform;
- incomplete Package correlation never becomes Platform preference;
- a delegated edge is not reopened for package acquisition; and
- equivalent CLI and Browser/Wasm inputs produce the same supplier-association
  outcome.

The System.Text.Json, Polly, and Azure SignalR cases use real nuget.org
packages. Synthetic fixtures cover false-positive prefix correlation,
non-namesake content, missing Platform membership, ambiguity, incomplete
evidence, and source failure.

## Non-goals

- Searching NuGet globally by assembly simple name.
- Treating package-ID or filename correlation as binding authority.
- Mapping package IDs directly to Platform Library identities.
- Treating pruning as an AssemblyRef classifier.
- Reopening a `Subsumed` package edge.
- Redefining package pruning, PackageHouse or PlatformHouse binding, or ladder
  precedence.
- Supporting Windows Metadata.
- Rendering supplier association.
