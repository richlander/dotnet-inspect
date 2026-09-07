# Platform/package pruning

## Status

Focused design proposal for
[#6228](https://github.com/richlander/dotnet-inspect/issues/6228). Nothing
described here is implemented.

The .NET SDK already decides which package references are unnecessary because
the shared framework supplies them. This document makes that decision a typed
fact the product can consume, so that "is this name a platform library, a
package, or both?" stops being a name heuristic.

Consumers named by #6228 — Spotlight ranking, CLI bare-name routing, ecosystem
configuration, and pruned traversal edges — adopt this owner's fact. Each
remains a separate effort with its own design; none is specified here.

## Authority and exact claim

This owner defines one fact and one comparison:

> For an exact platform target, which package identities the target subsumes,
> and whether a requested package version is at or below the version that
> target supplies.

It owns:

- the shipped prune inventory and its projection from upstream data;
- the classification of a package identity as platform-only, package-only, or
  overlapping, for one exact target;
- the subsumption comparison against a requested version;
- the derivation of a live entry's supplied version, and the invariant binding
  that derivation to a committed target; and
- the staleness contract for its own data.

It does not own:

- ranking, presentation, or which subject a consumer opens;
- traversal permission, graph edges, or edge outcomes;
- package acquisition, source selection, or version discovery;
- ecosystem identity, package sets, or prefixes; or
- the platform library catalog, which is a separate inventory with its own
  owner.

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

### Live and frozen entries

Entries divide into two populations, and the split is derivable rather than
authored:

| Population | Rule | Behavior |
| --- | --- | --- |
| **Live** | supplied version equals the pack's own version | moves with every patch release |
| **Frozen** | anything else | legacy packages pinned at 4.3.x/5.0.0; never moves |

The live population is 15 entries at `netcore.app` 10.0, 24 at 11.0, and 0 for
net6.0 through net8.0. `Microsoft.AspNetCore.App` states every entry at its
band floor and has no live population at all.

The monthly bump of a live entry is mechanically generated per patch release.
Its supplied version is therefore not merely predictable but derivable:

```text
suppliedVersion(id, target) = target.packVersion   if id is live
                            = the frozen literal   otherwise
```

## The algorithm

Given an exact target and a package identity, optionally with a requested
version:

1. If the identity has no entry in the target's inventory, it is
   **package-only**. The platform cannot supply it.
2. If the identity has an entry but no library of that name exists in the
   target's catalog, it is **overlapping**: a package the framework subsumes
   without shipping under that assembly name.
3. If the identity has an entry and a catalog library, it is **overlapping**.
4. A catalog library with no entry is **platform-only**. No package supplies
   it.
5. With a requested version, compare it against the supplied version derived
   above. At or below is subsumed; above is not.

Comparison uses NuGet semantic version ordering. A requested floating or
absent version is not a comparison and must not be treated as subsumed by
default; the consumer decides what an unversioned request means.

### Current populations

| Step | Category | Example | Count |
| --- | --- | --- | --- |
| 1 | Package-only | `Newtonsoft.Json`, `Microsoft.Extensions.AI` | unbounded |
| 2 | Overlapping, no library of that name | `NETStandard.Library`, `Microsoft.NETCore.Platforms` | 166 |
| 3 | Overlapping, library present | `System.Text.Json` | 274 |
| 4 | Platform-only | `System.Private.CoreLib` | 39 |

Steps 3 and 4 partition the catalog: 274 + 39 = 313 libraries. Steps 2 and 3
partition the inventory: 166 + 274 = 440 entries.

Step 2 is dominated by 155 `runtime.*` RID-specific legacy packages. Its
recognizable remainder is the host and targeting infrastructure —
`NETStandard.Library`, `Microsoft.NETCore.App`, `Microsoft.NETCore.Platforms`,
`Microsoft.NETCore.DotNetHost` — packages the framework subsumes without
shipping an assembly under that name.

Step 1 has no count because it is the complement: every package identity not
in the inventory. `Microsoft.Extensions.AI` is worth naming, because it is
platform-adjacent and ships out of band, so an ecosystem may treat it as a
core package while pruning correctly classifies it as package-only.

Counts computed 2026-09-07 against the `net11.0` catalog target and
`Microsoft.NETCore.App.Ref` / `Microsoft.AspNetCore.App.Ref`
11.0.0-preview.7.26381.103. They are illustrative of scale, not a contract;
step 3 grew by 36 entries between .NET 10 and .NET 11.

### Read the totals per family

The step-3 total is dominated by two populations that carry little
information, and a consumer that treats 274 as the size of the interesting
problem will over-build. Split by supplying family:

| Family | Libraries | Overlapping | Platform-only | Entry, no library |
| --- | --- | --- | --- | --- |
| `netcore.app` | 181 | 143 | 38 | 165 |
| `aspnetcore.app` | 132 | 131 | 1 | 1 |

ASP.NET Core is very nearly one-to-one: every assembly has a matching package
identity, so its 131 says more about how that framework is packaged than about
platform/package overlap. Its two exceptions are the whole story —
`Microsoft.Extensions.FileProviders.Embedded` is its only platform-only
library, and `Microsoft.AspNetCore.App` is its only entry without one, being
the meta-package.

Within `netcore.app`, 119 of the 143 overlaps are frozen at netstandard-era
versions:

| Supplied major | 4.x | 5.x | 6.x | 7.x | 10.x |
| --- | --- | --- | --- | --- | --- |
| Entries | 103 | 12 | 2 | 1 | 1 |

These are real entries and pruning classifies them correctly, but
`System.Runtime@4.3.1` and `System.Buffers@5.0.0` will not be the answer to a
query anyone asks today.

The working set is therefore the 55 live overlaps, not the 274. Sizing
caches, tests, or presentation against the larger number mistakes the shape of
the data. Step 2 has the same distortion: 155 of its 166 entries are
`runtime.*` RID-specific legacy packages, leaving roughly a dozen real ones.

### Live and frozen overlaps behave differently

Step 3 divides again along the live/frozen split, and the two behave unlike
each other in practice:

| | Example | Supplied version | Count | `netcore` | `aspnetcore` |
| --- | --- | --- | --- | --- | --- |
| Live overlap | `System.Text.Json` | `11.0.0-preview.7.26381.103` | 55 | 24 | 31 |
| Frozen overlap | `System.Runtime` | `4.3.1` | 219 | 119 | 100 |

For a frozen overlap the package is long dead and the platform absorbed it
years ago, so any plausible requested version is subsumed and the comparison
is a formality. For a live overlap the package still ships in lockstep with
the runtime, so the comparison is the whole question. A consumer that wants to
explain *why* a name resolved to the platform will find the distinction more
useful than the raw verdict.

## Contracts

### Subsumption never over-claims

Reporting a package as subsumed when the platform supplies an older version
would delegate the user to a stale implementation and hide that a newer
package exists. Reporting it as not subsumed when the platform has in fact
caught up merely offers a package the user did not need.

The first is a correctness failure; the second is not. Every uncertainty in
this owner — stale data, an unknown target, a failed acquisition — must
resolve toward **not subsumed**.

The supplied version only ever increases, so stale data satisfies this
contract by construction.

### The derivation binds to its committed target

`suppliedVersion` binds to the target the inventory came from, never to a
version observed later by discovery. Deriving from a newly discovered version
while presenting inventory from a shipped snapshot mixes two coordinates,
which
[version resolution](version-resolution.md#browser-platform-catalog-targets)
already forbids. Selecting a discovered version requires acquiring its
inventory first.

### Membership and supplied version are separate claims

Membership answers "does this name overlap the platform?" and is stable within
a major band. The supplied version answers "is this version subsumed?" and
moves monthly. A consumer that needs only the first must not be made to depend
on the freshness of the second.

## Data acquisition

The shipped artifact is a projection of the upstream file, not a copy: package
identity, the live flag, and the band-floor supplied version for live entries.
Frozen entries keep their literal.

**The projection is stable across patch releases.** Projecting the reference
packs for 10.0.10 and 10.0.11 — whose raw `PackageOverrides.txt` files differ
in 15 entries — produces byte-identical output. The artifact moves at major
boundaries and when the live population changes, not monthly.

That property decides the open question in #6228, because the objection to a
regenerate-and-compare gate was that upstream moves on its own schedule and
would turn CI red on a calendar rather than on a change.

| Option | Assessment |
| --- | --- |
| **Regenerate and compare in CI**, as `eng/generate-inspect-web-engine-facade.sh --check` does at `ci.yml:343` | Viable, because the projection is patch-stable. Unlike that script it needs network access to fetch reference packs, so it belongs in a nightly lane rather than PR CI. |
| **Download at runtime** | Solves a problem the projection does not have. It would fetch data that changes at most yearly, on a startup path, trading a hermetic asset for a network dependency. |

Neither option supplies self-healing on its own, and neither needs to. The
self-healing path already exists: the derivation above sharpens the shipped
projection into a patch-exact supplied version whenever a reference pack is
acquired, which the catalog generator and platform realization do anyway.

The recommendation is therefore to ship the projection per major version,
verify it in a nightly regenerate-and-compare lane, and treat reference-pack
acquisition as the refinement path. Runtime download of prune data is not
proposed.

### The derivation is a checked assumption

The mechanical-bump property is not published upstream as a contract. Whenever
a real reference pack is acquired, the derived supplied version must be
asserted equal to the pack's actual value, so the assumption is a gate rather
than a bet.

## Staleness contract

| Must be fresh | May be stale |
| --- | --- |
| The selected platform patch version | Prune membership |
| Reference and runtime pack bytes | The live flag |
| Patch-exact supplied versions | Band-floor supplied versions |

Anything naming a specific version a user might act on must be fresh; anything
naming a shape or an identity may be stale.

## Gates

Named here, unimplemented; each lands with the work it covers.

| Property | Gate |
| --- | --- |
| Membership classification is exact for a known target | `Pruning_ClassifiesPlatformOnlyPackageOnlyAndOverlapping` |
| Subsumption compares by NuGet semantic order, not string order | `Pruning_SubsumptionUsesSemanticVersionOrder` |
| A version above the supplied version is not subsumed | `Pruning_LeapfroggingPackageIsNotSubsumed` |
| Uncertainty resolves to not-subsumed | `Pruning_UnknownTargetIsNotSubsumed` |
| The derivation binds to the committed target | `Pruning_DerivationDoesNotAdoptDiscoveredVersion` |
| The derived supplied version matches an acquired pack | `Pruning_DerivedVersionMatchesAcquiredReferencePack` |
| The projection is stable across patch releases | `Pruning_ProjectionIsStableAcrossPatchReleases` |

## Non-claims

This document specifies no consumer behavior, no presentation, no acquisition
policy, and no traversal semantics. It adds no dependency and no platform
exception. `Microsoft.WindowsDesktop.App` has upstream prune data but no
catalog target; whether it participates is deferred to #6228.
