# Platform/package pruning

## Status

Focused design proposal for
[#6228](https://github.com/richlander/dotnet-inspect/issues/6228). Nothing
described here is implemented.

The .NET SDK already decides which package references are unnecessary because
the shared framework supplies them. This document makes that decision a typed
fact the product can consume. It does not decide whether a package exists or
whether a name should be presented as platform-only, package-only, or both;
those answers compose this fact with package discovery and the platform
library catalog.

Consumers named by #6228 — Spotlight ranking, CLI bare-name routing, ecosystem
configuration, and pruned traversal edges — adopt this owner's fact. Each
remains a separate effort with its own design; none is specified here.

## Authority and exact claim

This owner defines one fact and one comparison:

> For an exact platform target, which package identities the target subsumes,
> and whether a requested package version is at or below the version that
> target supplies.

An inventory is read per shared framework and composed for a target, because
subsumption is a property of the framework references an app actually has.

It owns:

- the shipped prune inventory and its projection from upstream data;
- whether a package identity has a subsumption entry for one exact target;
- the subsumption comparison against a requested version;
- the supplied-version value associated with that exact target, and the
  invariant that prevents one target's inventory from being paired with
  another target's version; and
- the staleness contract for its own data.

It does not own:

- whether a package exists, or the final platform/package/both classification
  of a name;
- ranking, presentation, or which subject a consumer opens;
- Workspace registration, call-graph focal length, graph edges, or edge
  outcomes;
- package acquisition, source selection, or version discovery;
- ecosystem identity, package sets, or prefixes; or
- the platform library catalog, which is a separate inventory with its own
  owner.

## Invariants

These hold for every inventory independent of how a consumer uses it.

### Discovering a newer platform cannot relabel older data

An inventory is a property of one platform target, not a separately
configurable ruleset. Its framework, pack version, membership, and supplied
versions travel together. One target's inventory cannot be relabeled with
another target's version or composed with a family from another target.

A shipped .NET 10.0.0 projection does not become a .NET 10.0.1 inventory when
discovery finds the newer patch. The product either keeps the 10.0.0 source
coordinate visible or acquires the 10.0.1 inventory.

### A console app and a web app do not have the same inventory

The upstream file is published per shared framework. A target's inventory is
the union of the families that target references, and composition requires
agreement on the target framework. A composition across target frameworks
describes no real application.

A console app normally contributes `Microsoft.NETCore.App`; a web app also
contributes `Microsoft.AspNetCore.App`. The same target framework can therefore
have different applicable entries depending on the framework families the app
actually references.

### Missing data must not become yes

An absent inventory, an identity absent from an inventory, or a requested
version that cannot be compared subsumes nothing. An empty inventory expresses
that state directly. It does not assert that any package is absent or decide
what a consumer should acquire or traverse.

If a request has no version, the answer is not "probably subsumed." If a
target's inventory is unavailable, the answer is not an empty success-shaped
inventory for that target. Both remain visibly unable to produce `Subsumed`.

## Boundary scenarios

### A package may target a higher platform version than the workspace

A workspace using .NET 10 may open a package whose selected assets target
.NET 11. The package remains a valid inspection subject; the version mismatch
is not an error in this owner.

For a package edge carrying a .NET 11 package version, the .NET 10 inventory
answers `NotSubsumed` when that request is above its supplied-version ceiling.
It does not relabel the package, reject the subject, or claim that the older
platform can satisfy it. Whether a later traversal can compose that package
with the workspace platform is owned by platform compatibility and resolution,
not by this comparison.

### A workspace may have no platform at all

Removing the platform is a coherent workspace configuration, not a broken
inventory. This owner represents it with an empty inventory: no package
identity has a subsumption entry and every version comparison answers
`NotSubsumed`.

The scenario makes absence concrete without turning it into package policy.
The empty inventory does not assert that packages exist, choose which edges a
consumer follows, or authorize acquisition; it says only that no platform
target is available to subsume them.

## The upstream fact

The SDK ships the decision as `PackageId|Version` pairs, in the reference
pack under `data/PackageOverrides.txt`:

```text
System.Text.Json|10.0.11
System.Reflection.Metadata|10.0.11
System.Memory|5.0.0
Microsoft.CSharp|4.7.0
```

The version states the highest version of that package the framework supplies.
This document calls it the entry's **supplied version**. A requested package
version at or below it is **subsumed**; a version above it is not, and remains
a distinct package that the platform cannot answer for.

The reference pack is the authority rather than the SDK's `PrunePackageData`.
Compared like for like, the two have identical membership; they differ only in
how precisely they state the supplied version, and the reference pack is
per-exact-target and already acquired by the catalog generator. Every
reference pack the generator fetches carries the file, both families, net6.0
through net11.0.

### Extensions packages move when the app's framework families move

The upstream file is published per shared framework, and a target subsumes an
identity only when it references that framework. This document names the two
families as the reference packs do — `Microsoft.NETCore.App` and
`Microsoft.AspNetCore.App`. The library catalog spells the same families
`netcore.app` and `aspnetcore.app`. A console app references
`Microsoft.NETCore.App`; a web app also references `Microsoft.AspNetCore.App`
and subsumes more.

This is not a technicality, and the `Microsoft.Extensions.*` prefix shows why.
Counting the entries under that prefix across both families, `net10.0` and
`net11.0` each subsume **46** package identities — the total is unchanged.
What changed between the two releases is which shared framework publishes
them:

`Microsoft.Extensions.*` package identities subsumed, by supplying framework:

| Target framework | Supplied by `Microsoft.NETCore.App` | Supplied by `Microsoft.AspNetCore.App` | Total |
| --- | --- | --- | --- |
| `net10.0` | 0 | 46 | 46 |
| `net11.0` | 9 | 37 | 46 |

A console app references only `Microsoft.NETCore.App`. On `net10.0` that means
none of those 46 are subsumed for it; on `net11.0` nine are. A web app
references both families and subsumes all 46 either way.

The same package identity is subsumed or not depending on the target's
composition, with no change to the identity, the prefix, or any rule the
product authors.

A target's applicable inventory is therefore the union of the families it
references. Composition requires agreement on the target framework, since a
composition across frameworks describes no real app. The shipped families
publish disjoint identities, so a conflict rule is defensive rather than
load-bearing; when one is needed, the lower supplied version wins, because
that is the direction that cannot over-claim.

### An unchanged package ceiling may span multiple platform patches

Equality with one pack version does not establish that an entry follows future
patches. For example, `Microsoft.Extensions.Caching.Memory` is `10.0.0` in both
`Microsoft.AspNetCore.App.Ref` 10.0.0 and 10.0.1. Treating equality at the band
floor as a patch-following signal would turn the same unchanged upstream value
into two different product facts.

The shipped projection therefore stores the literal supplied version observed
at its committed band-floor target. It does not carry a `live` flag and does
not derive a later value from a later-discovered pack version. The literal is
exact only for the projection's committed target. For another patch in the
same band, projected membership remains a labeled hint and version comparison
returns `NotComparable` until that target's exact inventory is available.

When the exact reference pack for a target is acquired, its actual
`PackageOverrides.txt` values replace the projected supplied versions for that
target. Exact data is read, not predicted. Membership and supplied version
remain separate claims so consumers that need only membership do not depend on
patch-exact data.

## The algorithm

Given an exact target and a package identity, optionally with a requested
version:

1. If the identity has no entry in the target's inventory, the target makes no
   subsumption claim for it.
2. If the identity has an entry, the target supplies that package identity up
   to the entry's supplied version.
3. With a requested version, compare it against the supplied version. At or
   below is subsumed; above is not.

Comparison uses NuGet semantic version ordering. A requested floating or
absent version is not a comparison and must not be treated as subsumed by
default; the consumer decides what an unversioned request means.

### A platform library may still have a NuGet package

Joining the `net11.0` inventory to the platform library catalog illustrates
the data shape. These are inventory/catalog relationships, not claims about
whether a NuGet package exists:

| Inventory entry | Same-named catalog library | Example | Count |
| --- | --- | --- | --- |
| absent | unknown or absent | `Newtonsoft.Json`, `Microsoft.Extensions.AI` | unbounded |
| present | absent | `NETStandard.Library`, `Microsoft.NETCore.Platforms` | 166 |
| present | present | `System.Text.Json`, `Microsoft.Extensions.DependencyInjection.Abstractions` | 274 |
| absent | present | `System.Security.Cryptography`, `System.Private.CoreLib`, `Microsoft.Extensions.FileProviders.Embedded` | 39 |

The last row is deliberately not called platform-only.
`Microsoft.Extensions.FileProviders.Embedded` has a platform library and a
published package but no prune entry for the measured target. Package
existence comes from package discovery; inventory absence cannot establish its
opposite.

The middle two rows partition the inventory: 166 + 274 = 440 entries. The last
two rows partition the catalog: 274 + 39 = 313 libraries.

Counts computed 2026-09-07 against the `net11.0` catalog target and
`Microsoft.NETCore.App.Ref` / `Microsoft.AspNetCore.App.Ref`
11.0.0-preview.7.26381.103. They are illustrative of scale, not a contract;
the entry-and-library row grew by 36 between .NET 10 and .NET 11.

### ASP.NET Core is nearly one-to-one; .NETCore is not

The inventory-and-library total is dominated by populations that carry
different information. Split by supplying family:

Counts of package identities, except `Catalog libraries`, which counts
assemblies:

| Supplying framework | Catalog libraries | Entry and library | Library, no entry | Entry, no library |
| --- | --- | --- | --- | --- |
| `Microsoft.NETCore.App` | 181 | 143 | 38 | 165 |
| `Microsoft.AspNetCore.App` | 132 | 131 | 1 | 1 |

ASP.NET Core is very nearly one-to-one: every assembly has a matching package
identity in the inventory, so its 131 says more about how that framework is
packaged than about package availability. Its two join exceptions are the
whole story: `Microsoft.Extensions.FileProviders.Embedded` is the only catalog
library without an entry, and `Microsoft.AspNetCore.App` is the only entry
without a same-named library, being the meta-package.

### System.Text.Json and System.Runtime need different freshness

Some supplied versions move with platform patches, such as
`System.Text.Json`; many legacy ceilings remain literal, such as
`System.Runtime|4.3.1`. The distinction matters for freshness but is not
inferred into the shipped projection. A projected literal is always safe to
compare for its committed target, and an acquired exact inventory supplies the
current ceiling for another target directly.

## Consumer composition boundary

This owner returns inventory presence, supplying family, supplied version, and
a version-comparison result. It does not transform selection or choose a
navigation, search, traversal, acquisition, or presentation outcome.

[#6228](https://github.com/richlander/dotnet-inspect/issues/6228) records
non-normative scenarios in which later owners may consume the fact:

- a search or routing owner may combine inventory membership with package
  discovery and catalog presence to rank platform and package subjects;
- a package-graph owner may use a `Subsumed` result when deciding whether an
  edge needs package acquisition;
- an `AssemblyRef` resolver may consume the graph outcome together with its own
  asset-group and platform-binding rules; and
- a section owner may expose the inventory for auditability.

Those owners define whether, when, and how the fact changes observable
behavior. This document neither mandates their outcome nor treats their
examples as part of this owner's contract. In particular, it does not compare
assembly versions with package versions, infer package existence from catalog
or inventory absence, or define an app-authored-reference exemption.

### A name may exist in both places without being subsumed

The inventory becomes useful when another owner joins it to facts from package
discovery and the platform catalog. Keeping the columns separate shows why no
one column can answer the final question:

| Name | Inventory entry | Catalog library | Published package | What this owner establishes |
| --- | --- | --- | --- | --- |
| `System.Text.Json` | yes | yes | yes | The target supplies the package identity through a version ceiling |
| `Microsoft.Extensions.FileProviders.Embedded` | no | yes | yes | The target makes no package-subsumption claim |
| `System.Private.CoreLib` | no | yes | no known package | The target makes no package-subsumption claim |
| `Newtonsoft.Json` | no | no | yes | The target makes no package-subsumption claim |

The second row is the discriminator. Calling every catalog library without an
inventory entry platform-only would erase a real package. Calling every absent
entry package-only would erase the platform library. A search or routing owner
can still produce a three-way user-facing answer, but only after it joins all
three facts.

### A newer package must remain newer

For an exact .NET 11 inventory whose `System.Text.Json` entry supplies version
11, the comparison produces:

| Requested package version | Owner result | Why |
| --- | --- | --- |
| `9.0.0` | `Subsumed` | Below the supplied version |
| `10.0.0` | `Subsumed` | NuGet semantic order, not string order |
| `11.0.0` | `Subsumed` | The ceiling is inclusive |
| `12.0.0` | `NotSubsumed` | The package has leapfrogged the platform |
| absent, floating, or unparsable | `NotComparable` | There is no safe version comparison |

The table stops at the owner boundary. A traversal owner may use
`Subsumed` when deciding whether package acquisition is necessary, while a
search owner may use it when ranking two already-known subjects. Neither
outcome is implied by the comparison itself.

### Fifteen assembly references may produce zero package edges

`System.Text.Json` 10.0.0 illustrates why this fact is not an
`AssemblyRef` classifier:

| Dependency group | Declared package dependencies |
| --- | --- |
| `net10.0` | 0 |
| `net9.0`, `net8.0` | 2 |
| `netstandard2.0` | 7 |
| `net462` | 8 |

Its `net10.0` assembly still carries 15 assembly references. The empty package
dependency group means there is no package edge on which to ask the pruning
question, not that the assembly references disappeared. Down-level groups
declare `System.Memory`, `System.Buffers`, and other package identities, so
those graph edges can carry the package identity and version this owner needs.

The example assigns no resolution policy. Asset-group selection decides which
dependency group applies, the package graph decides which package edge exists,
and an `AssemblyRef` resolver owns assembly binding. This owner enters only
when a consumer already has a package identity and target to compare.

### Common and Protocols look alike but enter through different paths

`Microsoft.Azure.SignalR` 1.33.1 contains multiple assemblies and package
dependencies in the same `net8.0` asset group:

| Reference | Relevant fact before pruning | Pruning input |
| --- | --- | --- |
| `Microsoft.Azure.SignalR.Common` | Assembly is in the same asset group | none |
| `Microsoft.Azure.SignalR.Protocols` | Separate package dependency at 1.33.1 | package identity and requested version |
| `Microsoft.AspNetCore.SignalR` | Platform catalog has a same-named library | none until a package edge exists |
| `System.Memory` | Platform catalog has a same-named library | none until a package edge exists |
| `Azure.Core` | Transitive package dependency through `Azure.Identity` | package identity and requested version |

`Common` and `Protocols` share a prefix, version, and public key token, yet one
is in-package and the other is a package edge. Name shape cannot supply the
missing graph fact. Conversely, catalog presence for
`Microsoft.AspNetCore.SignalR` or `System.Memory` does not manufacture a
package edge. The surrounding owners establish those facts before this owner
can answer whether a real package edge is subsumed.

### A project file and an assets file do not ask the same question

NuGet's direct-reference rules explain questions later graph consumers must
settle without making those answers part of this owner:

| Input | What the artifact reveals | Consumer-owned question |
| --- | --- | --- |
| `.csproj` | App-authored direct package references | Whether an authored reference is exempt from graph transformation |
| `.nuspec` or package dependency group | Library package edges | Where to apply the subsumption comparison |
| `project.assets.json` or `app.deps.json` | A graph after some SDK processing | Whether pruning has already been applied and must not be repeated |

The inventory and comparison are unchanged in all three rows. Input-kind and
processing policy belong to the project, package-graph, and restored-artifact
owners that can interpret those artifacts.

## The inventory is a queryable fact

The inventory is structured data, not only a private comparison input. Its
stable fields are package identity, supplying family, supplied version, and
whether that version came from the shipped projection or an acquired exact
pack. The in-memory comparison API and any future rendered rows must project
the same owner-issued fact.

A section owner may expose that data for auditability, but it owns registration,
selection policy, cost and size axes, Markout lowering, and host adoption. This
document defines no section or execution policy.

### The same version text may have different precision

For example, the same literal can carry different precision without changing
its provenance:

| Queried target | Source target | Precision | Stored value | Comparable? |
| --- | --- | --- | --- | --- |
| ASP.NET Core 10.0.0 | 10.0.0 projection | projected, same target | `Microsoft.Extensions.Caching.Memory` at 10.0.0 | yes |
| ASP.NET Core 10.0.1 | 10.0.0 projection | projected, different target | `Microsoft.Extensions.Caching.Memory` at 10.0.0 | no |
| ASP.NET Core 10.0.1 | acquired 10.0.1 pack | exact | `Microsoft.Extensions.Caching.Memory` at 10.0.0 | yes |

The second and third rows intentionally retain the same supplied-version text.
The target association and precision, not the spelling of the version, decide
whether a comparison is valid.

## Correspondence with the NuGet specification

`PrunePackageReference` is specified in three accepted NuGet designs:
[`accepted/2024/prune-package-reference.md`](https://github.com/NuGet/Home/blob/dev/accepted/2024/prune-package-reference.md)
(NuGet/Home#13634),
[`accepted/2025/prune-package-reference-rollout.md`](https://github.com/NuGet/Home/blob/dev/accepted/2025/prune-package-reference-rollout.md)
(#14066), and
[`accepted/2025/PrunePackageReference-with-direct-PackageReference.md`](https://github.com/NuGet/Home/blob/dev/accepted/2025/PrunePackageReference-with-direct-PackageReference.md)
(#14325). They are evidence about the behavior this owner must agree with, not
authority over inspection.

Three things they confirm:

| This design | The specification |
| --- | --- |
| Supplied version is an inclusive ceiling | "The version is consider to the maximum version to be pruned"; NuGet removes "any of the specified packages or lower" |
| Membership is per target framework | "The feature is framework specific" |
| The inventory is the `PackageOverrides` data | "The list of packages being removed is the exact same that's part of the build time conflict resolution in the .NET SDK" |

NuGet's direct-reference exemption and `RestoreEnablePackagePruning` switch
govern restore behavior. They do not change which identities a target can
supply, so this owner neither adopts nor diverges from those policies. A
consumer design that applies the fact to project or package graphs owns any
corresponding exemption or switch.

### System.Text.Json package version 10 is not assembly version 10.0.0.0

Pruning decides package identities. It does not decide assemblies, files,
types, asset-group selection, or graph policy, and it never compares an
assembly version with a package version.

The distinction is observable in `System.Text.Json` 10.0.11: the package
version is 10.0.11 while the assembly version is 10.0.0.0. Comparing the
assembly reference to the package ceiling would use the wrong identity and
version currency even though the leading major happens to match.

### .NET Standard may publish no inventory

The rollout spec enables pruning "for *all* .NET (Core) and .NET Standard 2.0
and above". This document's inventories come from reference packs, and the
catalog carries `netstandard2.0` and `netstandard2.1` as reference-only
targets with no such pack. The SDK's `PrunePackageDataRoot` in
11.0.100-preview.7 contains only a `10.0` band with the three .NET shared
frameworks, so where .NET Standard prune data is sourced is unresolved. Until
it is, no inventory is published for a .NET Standard target here. That is safe
under the never-over-claim contract but is not yet known to match the SDK.

## Contracts

### A false yes is worse than a false no

Reporting a package as subsumed when the platform supplies an older version
would delegate the user to a stale implementation and hide that a newer
package exists. Reporting it as not subsumed when the platform has in fact
caught up merely offers a package the user did not need.

The first is a correctness failure; the second is not. Every uncertainty in
this owner — stale data, an unknown target, a failed acquisition — must
resolve toward **not subsumed**.

Projected data from a different exact target cannot produce `Subsumed`;
it remains `NotComparable` until exact data for the requested target is
available.

### Discovering 10.0.1 cannot relabel a 10.0.0 projection

`suppliedVersion` binds to the target the inventory came from, never to a
version observed later by discovery. Relabeling projected values with a newly
discovered version while presenting inventory from a shipped snapshot mixes
two coordinates, which
[version resolution](version-resolution.md#browser-platform-catalog-targets)
already forbids. Selecting a discovered version remains valid with an older
projection; the comparison is `NotComparable`. Acquiring that version's
inventory is required only before its values can be published or used as exact.

### A search hint may need membership without a current ceiling

Membership answers "does this target publish a subsumption entry for this
package identity?" The supplied version answers "is this requested version
subsumed?" A consumer that needs only the first must not be made to depend on
the freshness of the second.

## Data acquisition

The shipped artifact is a projection of one committed band-floor inventory:
package identity, supplying family, and the literal supplied version at that
target. It records its target and projected precision.

### ASP.NET Core 10.0.0 and 10.0.1 have identical raw data

The projection makes no patch-following inference. `Microsoft.AspNetCore.App`
10.0.0 and 10.0.1 demonstrate why: their raw override files are byte-identical,
including `Microsoft.Extensions.Caching.Memory|10.0.0`. A projection that
classified band-floor equality as live would change despite unchanged input.

Membership is expected to be stable within a major band, but that upstream
behavior is an observed property, not a published contract. The projection
therefore remains labeled with its source target. A nightly comparison reports
membership changes rather than silently regenerating them, and another patch
does not use the projected literal for a version-bound subsumption result.

| Option | Assessment |
| --- | --- |
| **Regenerate and compare in CI**, as `eng/generate-inspect-web-engine-facade.sh --check` does at `ci.yml:343` | The network-dependent comparison belongs in a nightly lane. It verifies membership stability and reports any upstream change for review. |
| **Download at runtime** | Not required for the shipped baseline. It would add a startup network dependency for data whose exact values arrive through the normal reference-pack acquisition path; until then, cross-patch version comparison remains `NotComparable`. |

### Opening a platform pack can sharpen an earlier answer

The refinement path already exists: when catalog generation or platform
realization acquires an exact reference pack, the owner reads its actual
inventory and replaces the projection for that target.

For example, a workspace can start with projected .NET 10.0.0 membership while
showing that a .NET 10.0.1 version comparison is unavailable. If opening a
platform library acquires the 10.0.1 reference pack, the same workspace can
replace that projected value with the exact 10.0.1 ceiling. No separate prune
download or background mutation is needed.

The recommendation is therefore to ship the literal projection per major
version, verify membership in a nightly regenerate-and-compare lane, and treat
reference-pack acquisition as the exact-value refinement path. Runtime
download of prune data is not proposed.

## Staleness contract

| Must be fresh | May be stale |
| --- | --- |
| The selected platform patch version | Projected prune membership, labeled with its source target |
| Acquired reference-pack bytes | Projected band-floor supplied versions |
| Exact supplied versions presented as exact | Projected precision presented as projected |

Anything presented as exact for a target must be fresh. Projected data may be
stale only while its source target and projected precision remain visible and
it cannot produce an exact cross-target subsumption result.

A .NET 10.0.1 request over a 10.0.0 projection demonstrates all three columns:
the selected target is fresh, the projected membership is visibly sourced from
10.0.0, and the old supplied-version literal cannot answer the 10.0.1
comparison. After the 10.0.1 pack is acquired, even an unchanged literal is
exact because its source target now matches.

## Gates

The first implementation slice is #6239. Its gates live in
`DotnetInspector.Services.Tests.PlatformPruneInventoryTests` and run with
`dotnet run --project tests/DotnetInspector.Services.Tests -c Release`.

| Property | Gate | State |
| --- | --- | --- |
| Inventory membership is exact for a known family and target | `ReadsInventoryMembershipForExactTarget` | pending — #6239 |
| Inventory absence does not assert package absence | `InventoryAbsenceDoesNotClassifyPackageAvailability` | pending — #6239 |
| Literal supplied versions are preserved exactly | `PreservesLiteralSuppliedVersions` | pending — #6239 |
| Subsumption compares by NuGet semantic order, not string order | `SubsumptionUsesSemanticVersionOrder` | pending — #6239 |
| A version above the supplied version is not subsumed | `LeapfroggingPackageIsNotSubsumed` | pending — #6239 |
| Uncertainty resolves away from subsumed | `UncertaintyResolvesAwayFromSubsumed` | pending — #6239 |
| Supplied versions remain bound to the inventory target | `SuppliedVersionDoesNotAdoptDiscoveredTarget` | pending — #6239 |
| A projected inventory cannot subsume for another exact target | `ProjectedInventoryIsNotComparableAcrossTargets` | pending — #6239 |
| A malformed override line fails rather than dropping an identity | `MalformedOverrideLineFails` | pending — #6239 |
| Family composition decides inventory membership | `FamilyCompositionDecidesInventoryMembership` | pending — #6239 |
| Composition refuses mismatched targets and prefers the lower supplied version | `CompositionRefusesMismatchedTargetsAndPrefersTheLowerSuppliedVersion` | pending — #6239 |
| An empty inventory subsumes nothing | `EmptyInventorySubsumesNothing` | pending — #6239 |
| An acquired exact pack replaces projected supplied versions | `Pruning_ExactPackReplacesProjectedVersions` | pending — projection slice |
| Band-floor equality does not infer patch-following behavior | `Pruning_BandFloorEqualityRemainsLiteral` | pending — projection slice |
| A within-band membership change is reported for review | `Pruning_MembershipChangeIsReported` | pending — projection slice |

A pending gate names the slice that lands it. An inventory is its target, so an
unknown target is not expressible against the comparison API. The uncertainty
cases that do exist are an identity absent from the inventory and a request
that cannot be compared.

The gates follow the scenarios above rather than only the implementation
shape: the newer-package case drives `LeapfroggingPackageIsNotSubsumed`, the
no-platform case drives `EmptyInventorySubsumesNothing`, the unchanged
ASP.NET Core pair drives `Pruning_BandFloorEqualityRemainsLiteral`, and the
same-text/different-target table drives
`ProjectedInventoryIsNotComparableAcrossTargets`.

## Non-claims

This document specifies no consumer behavior, presentation, package
acquisition, source selection, or traversal semantics. It adds no dependency
and no platform exception. `Microsoft.WindowsDesktop.App` has upstream prune
data but no catalog target; whether it participates is deferred to #6228.
