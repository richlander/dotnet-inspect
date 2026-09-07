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

An inventory is read per shared framework and composed for a target, because
subsumption is a property of the framework references an app actually has.

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

## Invariants

These four hold across every consumer and are the frame for everything below.

### A workspace registers a platform version by default

A new workspace has one, per
[approved lazy traversal](approved-lazy-traversal.md)'s construction defaults.
The common case therefore has an inventory without the user choosing one.

### Pruning rules travel with the platform version, one to one

An inventory is a property of a platform version, not a separate artifact to
select, configure, or enable. Selecting a platform version selects its rules;
there is no version whose rules can be swapped, and no way to pair one
version's inventory with another's libraries.

**The on/off switch for pruning is the platform itself.** Pruning is on
exactly when a platform version is registered and off exactly when none is,
which is the no-platform case below. There is no second switch, because the
alternative is to disagree with what the SDK does for the same target, and
because a switch of its own is the thing that would let the rules drift from
the libraries they describe.

Every .NET and ASP.NET Core shared framework in the catalog publishes an
inventory, `net6.0` through `net11.0`, both families. A target that publishes
none subsumes nothing, which is the no-platform case below rather than an
error; .NET Standard is currently in that position here, and
[whether it should be](#open-question-net-standard) is unresolved.

### A package may target a higher platform version than the workspace

Opening a package whose assets target a newer platform than the registered one
is allowed and inspects normally. Nothing about the package becomes invalid,
and the mismatch is not an error at open.

The consequence is confined to traversal: an edge from that package into the
platform may find an incompatible base, and that is
[platform composition and overlays](platform-composition-and-overlays.md)'s
compatibility question — risk at load, outcome at traversal — not this
owner's. Subsumption already declines to over-claim here, since a version
above the supplied version is not subsumed.

### A workspace may have no platform at all

Removing the platform is a coherent configuration, not a broken one. It means
no traversal into the platform and no inventory, so nothing is subsumed and
every package reference is followed as a package reference. That is the
correct behavior for that workspace, not a degraded one.

Consumers must model the absence of an inventory rather than assuming one, and
an empty inventory expresses it directly: every identity classifies as
package-only and nothing is subsumed.

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

### An inventory is per shared framework

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
none of those 46 are subsumed for it, and
`Microsoft.Extensions.DependencyInjection.Abstractions` is an ordinary package;
on `net11.0` nine are, and that package is Platform. A web app references both
families and subsumes all 46 either way.

The same identity is Platform or package depending on the target's
composition, with no change to the identity, the prefix, or any rule the
product authors.

A target's applicable inventory is therefore the union of the families it
references. Composition requires agreement on the target framework, since a
composition across frameworks describes no real app. The shipped families
publish disjoint identities, so a conflict rule is defensive rather than
load-bearing; when one is needed, the lower supplied version wins, because
that is the direction that cannot over-claim.

### Live and frozen entries

Entries divide into two populations, and the split is derivable rather than
authored:

| Population | Rule | Behavior |
| --- | --- | --- |
| **Live** | supplied version equals the pack's own version | moves with every patch release |
| **Frozen** | anything else | legacy packages pinned at 4.3.x/5.0.0; never moves |

The live population is 15 entries at `Microsoft.NETCore.App` 10.0, 24 at 11.0, and 0 for
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
| 3 | Overlapping, library present | `System.Text.Json`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.AspNetCore.SignalR` | 274 |
| 4 | Platform-only | `System.Security.Cryptography`, `System.Net.Quic`, `mscorlib`, `System.Private.CoreLib` | 39 |

Steps 3 and 4 partition the catalog: 274 + 39 = 313 libraries. Steps 2 and 3
partition the inventory: 166 + 274 = 440 entries.

Step 2 is dominated by 155 `runtime.*` RID-specific legacy packages. Its
recognizable remainder is the host and targeting infrastructure —
`NETStandard.Library`, `Microsoft.NETCore.App`, `Microsoft.NETCore.Platforms`,
`Microsoft.NETCore.DotNetHost` — packages the framework subsumes without
shipping an assembly under that name.

Step 4 is not the implementation-detail category a single `System.Private.*`
example suggests. Only 3 of its 39 entries are `.Private.`. The rest divide
into compatibility facades with no package twin — `mscorlib`, `netstandard`,
`System.Core`, `System.Xml`, `System.Data` — and substantial public
implementations that were simply never shipped as packages, including
`System.Security.Cryptography`, `System.Net.Quic`, `System.Net.HttpListener`,
`System.Runtime.InteropServices.JavaScript`, and `Microsoft.VisualBasic.Core`.
A user searching `System.Security.Cryptography` must reach the platform,
because nothing else supplies it.

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

Counts of package identities, except `Catalog libraries`, which counts
assemblies:

| Supplying framework | Catalog libraries | Overlapping | Platform-only | Entry, no library |
| --- | --- | --- | --- | --- |
| `Microsoft.NETCore.App` | 181 | 143 | 38 | 165 |
| `Microsoft.AspNetCore.App` | 132 | 131 | 1 | 1 |

ASP.NET Core is very nearly one-to-one: every assembly has a matching package
identity, so its 131 says more about how that framework is packaged than about
platform/package overlap. Its two exceptions are the whole story —
`Microsoft.Extensions.FileProviders.Embedded` is its only platform-only
library, and `Microsoft.AspNetCore.App` is its only entry without one, being
the meta-package.

Within `Microsoft.NETCore.App`, 119 of the 143 overlaps are frozen at netstandard-era
versions:

| Supplied major version | 4.x | 5.x | 6.x | 7.x | 10.x |
| --- | --- | --- | --- | --- | --- |
| Frozen overlaps | 103 | 12 | 2 | 1 | 1 |

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

The last two columns split `Count` by supplying framework:

| Kind | Example | Supplied version | Count | `…NETCore.App` | `…AspNetCore.App` |
| --- | --- | --- | --- | --- | --- |
| Live overlap | `System.Text.Json` | `11.0.0-preview.7.26381.103` | 55 | 24 | 31 |
| Frozen overlap | `System.Runtime` | `4.3.1` | 219 | 119 | 100 |

For a frozen overlap the package is long dead and the platform absorbed it
years ago, so any plausible requested version is subsumed and the comparison
is a formality. For a live overlap the package still ships in lockstep with
the runtime, so the comparison is the whole question. A consumer that wants to
explain *why* a name resolved to the platform will find the distinction more
useful than the raw verdict.

## Where the rule applies

Pruning transforms **resolution**, never **selection**. That single line settles
the cases; the rest of this section is its consequences.

### Selection is never transformed

A user who names a subject gets that subject. `System.Text.Json` on nuget.org
is a real, official asset, and opening it must stay trivial. Pruning never
suppresses it, redirects it, or downgrades it to a footnote.

Search advertises a subsumed name as **both**. Platform is the preferred
default because it is what a build would bind, and the package remains a
visible, selectable alternative labeled by source. Preferring is ranking, not
hiding.

### Navigation inside a selected subject stays inside it

Opening the `System.Text.Json` package and walking Library to Type to Member
never leaves the assembly, so no reference is resolved and pruning never fires.
The user is inspecting that artifact, and every view answers from it.

Pruning becomes interesting only where an edge leaves an assembly.

### Resolution across an edge delegates to the platform

An edge into a **subsumed** identity resolves to the platform, matching what
the SDK does at build time, and it does so even when the workspace also
contains that package. Containment does not decide; the comparison does.

The comparison runs both ways, so holding the package changes nothing on its
own:

| Platform | Requested `System.Text.Json` | Subsumed? | Traversal target |
| --- | --- | --- | --- |
| 11.0 | 11.0 | yes | platform |
| 11.0 | 10.x | yes | platform |
| 10.0 | 11.0 | no | the `System.Text.Json` package |

In the first two rows a contained package does not capture the edge. In the
third the platform cannot answer for the requested version, so the contained
package is the target — and being in the workspace is what makes it
resolvable. A single workspace can route two edges to different targets when
they request different versions, because each edge is compared on its own.

Two edges are involved, and only the first is this owner's:

| Edge | Carries | Pruning's role |
| --- | --- | --- |
| Package-graph edge | package identity and version | **Direct.** A subsumed edge delegates rather than acquiring the package. |
| `AssemblyRef` | assembly name and assembly version | **Indirect.** Nothing to decide, because the competing package-backed participant was never admitted. |

That split matters because the two are not the same currency. Package
`10.0.11` presents assembly version `10.0.0.0`, so comparing an `AssemblyRef`
version against a package-version watermark would be a category error. This
owner decides package-graph edges; assembly binding stays with the resolver.

### Delegation is bounded by the supplied version

The third row above is the whole of it. An edge requesting a version **above**
the supplied version is not subsumed, and delegating it would show an older
surface while hiding the newer package — the over-claiming failure this design
exists to prevent.

For the common case the distinction is invisible, because a workspace's
platform is usually at least as new as its packages. It becomes visible when a
package leapfrogs the runtime, which is exactly when it must.

### The selected dependency group is the local oracle

Inside an opened package the author has already answered "package or platform"
for us, per target framework, in the nuspec dependency group. `System.Text.Json`
10.0.0 declares:

| Group | Declared dependencies |
| --- | --- |
| `net10.0` | **0** |
| `net9.0`, `net8.0` | 2 |
| `netstandard2.0` | 7 |
| `net462` | 8 |

On `net10.0` the group is empty because the platform supplies everything, while
`lib/net10.0/System.Text.Json.dll` still carries 15 `AssemblyRef`s, all at
`10.0.0.0`. That is the quantified form of the observation that platform
references have no package edge to follow: 15 assembly references, zero package
references.

Down-level the same names are packages again. `lib/netstandard2.0` references
`System.Memory 4.0.2.0` and `System.Buffers 4.0.2.0`, and the group declares
both as real dependencies. So the same assembly name is platform or package
depending only on the selected group, and no oracle in this document is needed
to know which — the group already says.

An asset outside every dependency group has no such answer. Analyzers are the
case: they follow analyzer rules and target `netstandard2.0`, so
`System.Text.Json`'s source generator references
`System.Collections.Immutable 6.0.0.0`, `System.Memory 4.0.1.2`, and
`netstandard 2.0.0.0`. Resolving those against the workspace's platform target
would bind a compiler-host assembly to a surface it was never built against.
Framework context follows the asset, not the workspace.

### Worked example: a multi-assembly package

`Microsoft.Azure.SignalR` 1.33.1 exercises all three outcomes from one
`lib/net8.0/Microsoft.Azure.SignalR.dll`, and contains the discriminator case
that any implementation must get right:

| Reference | Version | Outcome | Decided by |
| --- | --- | --- | --- |
| `Microsoft.Azure.SignalR.Common` | 1.33.1.0 | in-package | present in `lib/net8.0/` |
| `Microsoft.Azure.SignalR.Protocols` | 1.33.1.0 | package edge | declared dependency |
| `Microsoft.AspNetCore.SignalR` | 8.0.0.0 | platform | `aspnetcore.app` catalog |
| `System.Memory` | 8.0.0.0 | platform | `netcore.app` catalog |
| `Azure.Core` | 1.38.0.0 | package edge | transitive, via `Azure.Identity` |

`Common` and `Protocols` share a name prefix, a version, and a public key
token. Nothing about the references distinguishes them. Only the asset group's
contents and the dependency group do — which is why the boundary is the asset
group rather than the package, and why name shape must never stand in for
either.

`Azure.Core` adds the reminder that a package edge may be transitive rather
than declared, so the third step consults the resolved graph, not the group's
literal list.

### Boundary: this owner decides only the package edge

The full ladder for an `AssemblyRef` leaving an assembly is:

1. satisfied inside the referencing asset's own group — follow it;
2. otherwise a platform library in that asset's framework context, and platform
   traversal is enabled — follow it;
3. otherwise a package edge — apply this document, then approved traversal;
4. otherwise remain visibly unresolved.

Only step 3 belongs to this owner. Step 1 is asset-group composition, step 2 is
the platform library catalog, and step 4 is the existing rule that failure stays
visible. They are recorded here because the pruning rule is unreadable without
them, not because this document specifies them. A focused design for
`AssemblyRef` resolution across these steps remains to be written.

### Relationship to platform composition and overlays

[Platform composition and overlays](platform-composition-and-overlays.md)
already governs which *artifact* backs one assembly identity among admitted
participants, and its precedence rule prefers a designated artifact over a
platform one. The two do not overlap and do not conflict:

| Owner | Question | Decided among | Decided at |
| --- | --- | --- | --- |
| Pruning | package identity or platform? | a package-graph edge's candidates | package-graph construction |
| Overlay precedence | which artifact for this assembly identity? | admitted participants | reference binding |

They compose in the expected direction. That owner already states its
exception "does not weaken identity matching or promote package, project,
sibling, discovered, or other non-designated candidates" — so a package-backed
participant never outranks a platform one there either, which is the same
outcome pruning produces earlier and for a different reason. A designated
local build of `System.Text.Json.dll` still wins over the platform, because
pruning says nothing about designated artifacts; it only declines to fetch a
package.

The overlay work is not, as might be assumed, confined to low-level assemblies
without package twins. Its motivating shape is a local build composed over an
installed hive, which applies equally to an assembly that does have a package
twin. The distinction is designated-versus-platform, not twinned-versus-not,
which is why it stays orthogonal to this owner.

## The inventory is a queryable fact, not only a decision input

Pruning decides edges, but the inventory that decides them is data with more
than one use. It should be readable directly, as a section, rather than being
observable only through its effects.

This matters most for agents and for auditability. An agent asking why a
reference resolved to the platform, or which packages a target subsumes before
running anything, should read the rule rather than infer it from outcomes. A
person debugging an unexpected delegation has the same need. Both are badly
served by a fact that exists only inside a decision.

Treating the inventory as a data source rather than a private input has one
design consequence, which is why it belongs here: **the projection is part of
this owner's contract, not an implementation detail.** Package identity, the
supplying family, the live flag, and the supplied version are the fields a
consumer may rely on and this owner may not reshape freely. The in-memory
comparison API and the rendered rows are two projections of the same fact.

Section shape is the section owner's to register, not this document's, but the
axes follow from the data:

| Axis | Value | Why |
| --- | --- | --- |
| `SizeClass` | `Verbose` | 440 entries at `net11.0` across both families |
| `Cost` | `NetworkFree` from the shipped projection; `Moderated` when a reference pack must be acquired | Matches the [staleness contract](#staleness-contract): shipped data answers immediately, acquisition sharpens it |
| Execution policy | `ExplicitOnly` | It is not the single high-value section of any command, so it must not enter an automatic verbosity preset |

The rows should distinguish live from frozen entries and name the supplying
family, since those are the two facts that explain a subsumption result rather
than merely restating it.

This is design intent for the projection slice; nothing here is implemented.

## Correspondence with the NuGet specification

`PrunePackageReference` is specified in three accepted NuGet designs:
[`accepted/2024/prune-package-reference.md`](https://github.com/NuGet/Home/blob/dev/accepted/2024/prune-package-reference.md)
(NuGet/Home#13634),
[`accepted/2025/prune-package-reference-rollout.md`](https://github.com/NuGet/Home/blob/dev/accepted/2025/prune-package-reference-rollout.md)
(#14066), and
[`accepted/2025/PrunePackageReference-with-direct-PackageReference.md`](https://github.com/NuGet/Home/blob/dev/accepted/2025/PrunePackageReference-with-direct-PackageReference.md)
(#14325). They are evidence about the behavior this owner must agree with, not
authority over inspection.

Four things they confirm:

| This design | The specification |
| --- | --- |
| Supplied version is an inclusive ceiling | "The version is consider to the maximum version to be pruned"; NuGet removes "any of the specified packages or lower" |
| Membership is per target framework | "The feature is framework specific" |
| The inventory is the `PackageOverrides` data | "The list of packages being removed is the exact same that's part of the build time conflict resolution in the .NET SDK" |
| Something the human named is exempt from pruning | "Pruning direct PackageReference of current project - Warn and don't prune" (NU1510) |

The last row is a correspondence, not an identity, and the mapping needs
stating because the two models have different vocabularies.

The spec's exemption is narrow: only the **project being built** keeps its
direct reference. A referenced project's own direct `PackageReference` is
pruned, and a package's dependency on another package is transitive from the
root project and always pruned. So the exempt thing is one app-authored
reference, and the spec never discusses choosing a subject to inspect, because
it has no such concept.

This product has no project. The structural match is between NuGet's root
project and this product's **selected subject**: in both, exactly one thing the
human named is exempt, and everything reached from it is decided by the rules.
That is why selection is not transformed here — not because the spec says so
about inspection, which it does not, but because both models privilege the
named thing and neither extends that privilege to what it reaches.

NuGet/Home#14325 proposes softening the direct case from a warning to
privatizing the reference (`PrivateAssets='all'`, `IncludeAssets='none'`), which keeps the
package present while contributing nothing. That direction is consistent with
this document's position: the named package remains addressable.

### Named divergence: this product has one switch, not two

NuGet exposes `RestoreEnablePackagePruning` to disable the feature. This owner
deliberately does not, per the [invariants](#invariants): the platform
registration is the only switch. The justification is that the property exists
to de-risk a restore behavior change for builds with custom asset handling,
and none of those hazards apply to inspection, which never restores, copies,
or executes. Adding a second switch here would buy nothing and would let the
rules drift from the libraries they describe.

### Open question: .NET Standard

The rollout spec enables pruning "for *all* .NET (Core) and .NET Standard 2.0
and above". This document's inventories come from reference packs, and the
catalog carries `netstandard2.0` and `netstandard2.1` as reference-only
targets with no such pack. The SDK's `PrunePackageDataRoot` in
11.0.100-preview.7 contains only a `10.0` band with the three .NET shared
frameworks, so where .NET Standard prune data is sourced is unresolved. Until
it is, a .NET Standard target subsumes nothing here, which is the
[no-platform case](#a-workspace-may-have-no-platform-at-all) and safe under
the never-over-claim contract, but it is not yet known to match the SDK.

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

Implemented gates live in
`DotnetInspector.Services.Tests.PlatformPruneInventoryTests`; run them with
`dotnet run --project src/DotnetInspector.Services.Tests -c Release`.

| Property | Gate | State |
| --- | --- | --- |
| Membership classification is exact for a known target | `ClassifiesPlatformOnlyPackageOnlyAndOverlapping` | implemented |
| A live entry derives its supplied version; a frozen entry stores it | `DerivesLiveSuppliedVersionAndStoresFrozenLiterals` | implemented |
| Subsumption compares by NuGet semantic order, not string order | `SubsumptionUsesSemanticVersionOrder` | implemented |
| A version above the supplied version is not subsumed | `LeapfroggingPackageIsNotSubsumed` | implemented |
| Uncertainty resolves away from subsumed | `UncertaintyResolvesAwayFromSubsumed` | implemented |
| The derivation binds to the committed target | `DerivationDoesNotAdoptDiscoveredVersion` | implemented |
| A malformed override line fails rather than dropping an identity | `MalformedOverrideLineFails` | implemented |
| Family composition decides the Platform/Extensions boundary | `FamilyCompositionDecidesThePlatformExtensionsBoundary` | implemented |
| Composition refuses mismatched targets and prefers the lower supplied version | `CompositionRefusesMismatchedTargetsAndPrefersTheLowerSuppliedVersion` | implemented |
| A workspace with no platform subsumes nothing and follows every package reference | `NoPlatformInventorySubsumesNothing` | implemented |
| The derived supplied version matches an acquired reference pack | `Pruning_DerivedVersionMatchesAcquiredReferencePack` | pending — projection slice |
| The projection is stable across patch releases | `Pruning_ProjectionIsStableAcrossPatchReleases` | pending — projection slice |
| Selecting a subsumed package opens that package | `Pruning_SelectedPackageIsNotRedirectedToPlatform` | pending — consumer slice |
| Navigation inside a selected package stays in it | `Pruning_IntraAssemblyNavigationDoesNotDelegate` | pending — consumer slice |
| A subsumed edge delegates although the workspace holds the package | `Pruning_ContainedPackageDoesNotCaptureSubsumedEdge` | pending — consumer slice |
| Search advertises a subsumed name as both | `Pruning_SubsumedNameRemainsSelectableAsPackage` | pending — consumer slice |
| The inventory projects to stable rows carrying family, live flag, and supplied version | `Pruning_InventoryProjectsAuditableRows` | pending — projection slice |

A pending gate names the slice that lands it. `Pruning_UnknownTargetIsNotSubsumed`
was removed rather than left unimplemented: an inventory *is* its target, so an
unknown target is not expressible against this API. The uncertainty cases that
do exist — an identity absent from the inventory, and a request that cannot be
compared — are covered by `UncertaintyResolvesAwayFromSubsumed`, and the
cross-target case by `CompositionRefusesMismatchedTargets…`.

## Non-claims

This document specifies no consumer behavior, no presentation, no acquisition
policy, and no traversal semantics. It adds no dependency and no platform
exception. `Microsoft.WindowsDesktop.App` has upstream prune data but no
catalog target; whether it participates is deferred to #6228.
