# Platform package-to-Library correspondence

## Status and approved scope

This document is the normative owner for the exact Platform package-to-Library
correspondence tracked by
[#8503](https://github.com/richlander/dotnet-inspect/issues/8503). It is a
focused prerequisite of the
[Assembly Reference Resolution Ladder](assembly-reference-resolution-ladder.md)
adoption tracked by
[#8466](https://github.com/richlander/dotnet-inspect/issues/8466).

The first production scenario is `Microsoft.Azure.SignalR` 1.33.1 targeting
`net8.0`: after PackageHouse selects its framework reference and package
dependency group, a pruned ASP.NET Core package edge must be associable with
the exact Platform Library that can satisfy an external `AssemblyRef` without
downloading the subsumed package payload.

## Authority and exact claim

**Platform package-to-Library correspondence** owns:

> Given one exact Platform family-composition snapshot, its exact complete
> Platform Library populations, one exact composed platform prune inventory,
> and explicit correspondence declarations issued for those same snapshots,
> classify one canonical prune-inventory package identity as associated with
> zero, one, or several exact Platform Library memberships, or return typed
> ambiguous, unavailable, or failed evidence without acquiring that package's
> payload.

It owns:

- the immutable correspondence snapshot and its identity;
- exact association among the family composition, prune inventory, Platform
  population receipts, and declaration generation;
- validation that each declaration names an entry and Library membership from
  those exact inputs;
- one-to-many package-to-Library correspondence;
- per-package completeness and explicit negative declarations;
- closed associated, not-associated, ambiguous, unavailable, and failed
  outcomes; and
- the resource-free query used by later route composition.

It does not own:

- package dependency discovery, target-group selection, or reachability;
- framework-reference selection or Platform family-composition policy;
- package-version comparison or the `Subsumed` decision;
- Platform target selection, population realization, or assembly binding;
- route eligibility, precedence, or Workspace replacement;
- package or Platform payload acquisition;
- the provenance rules used to author a correspondence declaration; or
- presentation.

[Platform package supply policy](platform-package-supply-policy.md) remains the
sole owner of package-coordinate pruning. The
[Assembly Reference Resolution Ladder](assembly-reference-resolution-ladder.md)
joins that owner's exact `PlatformSupplyReceipt` with this owner's result only
after ordinary context binding returns `NoNameOwner`.

## Why the shipped pack files are insufficient

The .NET targeting pack carries three adjacent but independent facts:

- `data/PackageOverrides.txt` contains package identities and supplied-version
  ceilings used by the SDK's package-pruning policy;
- `data/FrameworkList.xml` enumerates targeting-pack members and their assembly
  metadata; and
- `data/PlatformManifest.txt` enumerates shared-framework file membership,
  pack identity, assembly version, and file version.

None carries the relation needed here. For example, the .NET 11 runtime pack
contains these independent records:

```text
System.Text.Json|11.0.0-rc.1.26425.128
System.Text.Json.dll|Microsoft.NETCore.App.Ref|11.0.0.0|11.0.26.42628
```

The second field of `PlatformManifest.txt` is the containing targeting pack,
not the component package that the platform replaces. The shared-framework SDK
describes the manifest as a list of files and versions. It does not define a
package-origin column. `FrameworkList.xml` likewise describes member files,
not their package correspondence.

The ASP.NET Core build demonstrates where useful provenance is lost. Its
`CreatePackageOverrides` target observes `ReferencePath.NuGetPackageId` while
forming package-prune rows, and project references contribute their file
names. The emitted file retains only `package ID|version`; the assembly
occurrence that supplied the package ID is not serialized. The independently
generated Platform manifest retains the member but not that original package
identity.

The SDK consumes all three files independently in
`ResolveTargetingPackAssets`; it does not reconstruct package-to-member
provenance. The current Inspect Web generator has the same boundary: it emits
Platform `rows` and prune `supplies` as separate arrays.

Consequently, exact acquisition of a reference pack can establish both endpoint
populations but cannot establish their relation. A source with only those
files must return `Unavailable`. It must not join package ID, assembly simple
name, filename, pack label, or display name.

Authoritative upstream references:

- the
  [Shared Framework SDK manifest contract](https://github.com/dotnet/arcade/blob/89377f40f28510cc68d5e84e0ac66d7e564ac006/src/Microsoft.DotNet.SharedFramework.Sdk/README.md#platform-manifest-generation)
  defines file membership and versions;
- ASP.NET Core
  [forms package overrides from resolved-reference provenance](https://github.com/dotnet/aspnetcore/blob/c7cef3bfadeb7416a67e6b06bf2a736dd540806b/src/Framework/App.Ref/src/Microsoft.AspNetCore.App.Ref.sfxproj);
  and
- the SDK
  [reads the three targeting-pack files independently](https://github.com/dotnet/sdk/blob/3b1d59fe28a146e203859d99925810e77cbffb9c/src/Tasks/Microsoft.NET.Build.Tasks/ResolveTargetingPackAssets.cs).

These inputs justify an additional explicit relation. They do not authorize a
name-based approximation.

## Owner inputs

### Exact family composition

The owner consumes one immutable family-composition snapshot. The snapshot
lists every selected `PlatformFamilyTarget` and the completed authoritative
reference-population result for that family.

The composition is evidence, not policy. Another owner decides that an
ASP.NET Core operation composes `AspNetCore` and `DotNetRuntime`; this owner
only preserves that exact decision. Every family target must use the same
canonical target framework. Each population receipt must settle its listed
target and carry authoritative complete-population source contributions.

The composition snapshot receives an opaque identity. Equal family, framework,
and version fields do not transfer correspondence to another snapshot.

### Exact prune inventory

The supplied `PlatformPruneInventory` must be exact for every family in the
composition:

- its target framework equals the composition target framework;
- its family set equals the selected family set;
- every family has `Exact` precision;
- source and target pack versions are equal; and
- each family pack version equals the corresponding
  `PlatformFamilyTarget.Version`.

The owner retains the exact immutable inventory instance. A replacement
inventory, even with equal rows, requires a new correspondence snapshot.
Adding an opaque inventory identity is an implementation refinement of the
existing prune owner, not permission for this owner to reinterpret its rows.

`PlatformPruneInventory.Compose` may choose one conservative entry when
families publish the same package identity. Correspondence consumes that
owner-issued composed entry as-is; it does not repeat or revise duplicate
resolution.

### Exact Platform Library membership

An exact Library membership is one `PlatformPopulationMember` from one
completed population result retained by the family-composition snapshot. Its
`LibraryReference`, `PlatformPopulationMemberAttribution`,
`PlatformFamilyTarget`, population receipt, source contribution, and source
generation travel together.

The diagnostic `PlatformLibraryIdentity.Name`, an assembly simple name, and a
catalog row label are not Library identity. A serialized catalog may use local
row keys, but the loader resolves those keys to the exact population members
it issues for that catalog generation before this owner constructs a
correspondence snapshot.

### Explicit declaration generation

One declaration generation classifies package entries against Library
memberships. For each package identity it covers, it declares exactly one of:

- a non-empty set of exact Library membership keys; or
- an explicit empty set, meaning the package has no Platform Library
  correspondence in this composition;
- competing non-empty sets that the declaration producer could not
  disambiguate;
- typed unavailable evidence; or
- typed failed evidence.

Omission means **not covered**, never not associated. This distinction permits
a producer to publish partial declarations without manufacturing negative
evidence.

A declaration is explicit when its producer supplies the package entry key and
Library membership key as separate typed fields. The correspondence owner
never derives either field from the other. Suitable production evidence
includes relationship-bearing framework build provenance or a reviewed,
generated catalog artifact that already records the relation. Package asset
selection and Metadata binding may validate or produce such an artifact
outside the inspection operation, but equal text alone cannot.

Observation of one package payload proves only that exact package coordinate's
selected assets. It cannot by itself generalize a package-identity relation to
every requested version below a prune ceiling. An identity-wide declaration
requires a source that owns that broader relation, such as framework build
provenance or an explicit product catalog declaration. Package payload evidence
may still validate the named endpoint and expose drift during catalog
generation.

A generated artifact binds its declarations to:

- exact target and family coordinates;
- the prune inventory source coordinates;
- the Platform catalog generation or content digest;
- stable local prune-entry and Library-row keys; and
- the producer schema version.

Loading the artifact issues fresh in-memory identities and validates every
local key against the exact loaded snapshots. Durable keys do not themselves
become cross-generation Library identity.

## Construction

Construction is single-shot and resource-free over already materialized
inputs:

1. Validate the family composition and every completed population receipt.
2. Validate exact prune-inventory correspondence to the composition.
3. Validate the declaration generation's target, inventory, catalog, and
   schema identities.
4. Resolve every declared prune-entry key to the exact entry selected by the
   supplied composed inventory.
5. Resolve every declared Library key to exactly one member in the retained
   family population.
6. Resolve typed ambiguous, unavailable, and failed declaration records
   without converting them to positive or empty sets.
7. Reject duplicate declaration records for one package unless they are
   byte-for-byte repetitions eliminated by the declaration-format owner.
8. Preserve each package's declared Library set in deterministic
   family-then-population order.
9. Issue one immutable correspondence snapshot only after all covered
   declarations validate.

The snapshot stores no stream, path, package-content handle, acquisition
callback, or mutable catalog. It retains the exact resource-free receipts and
members needed to prove its associations.

Malformed keys, a foreign population member, mismatched target or generation,
duplicate Library membership, and contradictory records within one declared
source fail construction. They never shorten the snapshot or become an empty
association. A declaration producer that legitimately composes several
authorized sources may instead publish a typed ambiguous record containing
the competing sets and their source evidence.

## Query and closed outcomes

The query accepts one canonical package identity already present in the exact
prune inventory. It returns:

- **Associated** — the declaration is covered and names one or more exact
  Library memberships;
- **NotAssociated** — the declaration is covered and explicitly names no
  Library membership;
- **Ambiguous** — available declaration evidence names alternatives that
  cannot be resolved to one exact declared Library set;
- **Unavailable** — the required exact inventory, family composition,
  population, declaration generation, or per-package coverage is absent; or
- **Failed** — construction or source processing failed and retained typed
  failure evidence.

One package mapping to several Libraries is `Associated`, not `Ambiguous`.
Ambiguity means the declaration producer could not establish which declared
set applies to the exact snapshots, such as two authoritative build inputs
that disagree. A durable Library key resolving to several loaded members is an
invalid catalog and fails construction rather than becoming ordinary
ambiguity.

Every outcome retains, when available:

- the correspondence request;
- the exact family-composition identity;
- the exact prune inventory;
- the exact declaration-generation identity; and
- every population receipt consulted.

`Associated` additionally retains the exact `PlatformPruneEntry` and non-empty
Library membership set. `NotAssociated` retains the same entry plus its
explicit empty declaration. `Ambiguous`, `Unavailable`, and `Failed` preserve
their competing, missing, or failed owner evidence rather than returning a
success-shaped empty set.

The package identity comparison is the case-insensitive canonical comparison
already owned by `PlatformPruneInventory`. Library membership comparison is
exact receipt-and-member identity. No query parses display text.

## Composition with pruning and routing

Correspondence answers relevance; pruning answers delegation. Neither result
implies the other.

For `System.Text.Json` under one exact .NET 11 composition:

1. the correspondence snapshot associates the inventory's
   `System.Text.Json` package identity with the exact Platform population
   member for the Platform Library;
2. `PlatformPrunePolicy` evaluates the requested package coordinate against
   the exact inventory entry; and
3. the ladder may form a Platform route on behalf of that package edge only
   when both results are exact and the prune receipt says `Subsumed`.

Therefore:

| Package coordinate | Correspondence | Pruning | Route consequence |
| --- | --- | --- | --- |
| `System.Text.Json@9.0.0` | Associated | `Subsumed` | The edge may delegate to the exact Platform Library |
| `System.Text.Json@12.0.0` | Associated | `NotSubsumed` | The edge remains package-owned |
| same-named undeclared package | Unavailable or explicitly not associated | any | No Platform substitution from equal text |

The all-subsumed path consumes only package manifests, dependency selection,
the exact prune inventory, and this resource-free correspondence. It does not
acquire the subsumed dependency package payload.

The ladder separately establishes that the requested `AssemblyRef` binds to
one of the associated exact Platform Library memberships. Correspondence does
not perform that binding or turn a framework reference into Library
membership.

## Pathological cases

### Meta-package

`Microsoft.AspNetCore.App` may have a prune entry but no same-named Library.
Only an explicit empty declaration produces `NotAssociated`. The missing
same-named member is not consulted.

### One package, several Libraries

A package that historically supplied several assemblies declares every
corresponding Platform membership. The result is one `Associated` outcome with
an ordered non-empty set. A later exact `AssemblyRef` match selects within that
set under the Platform binding owner.

### Library without a prune entry

`Microsoft.Extensions.FileProviders.Embedded` can be a Platform member without
a prune entry. It cannot enter this owner's package request domain. Platform
membership alone does not manufacture a prunable package identity.

### Same text without a declaration

A fixture contains a package ID and Platform assembly with equal text but no
declaration row. The result is `Unavailable`, not `Associated`. Adding an
explicit empty declaration changes it to `NotAssociated`; adding a validated
Library key changes it to `Associated`.

### Cross-generation replay

A declaration artifact is loaded with a different Platform catalog or prune
inventory generation whose visible rows are equal. Construction rejects or
returns unavailable evidence. Field equality cannot transfer the receipt.

### Family overlap

If two selected families expose candidate memberships for one declared key,
the result is `Ambiguous` unless the declaration itself explicitly identifies
both as the intended one-to-many set. Family order, pack order, and first match
do not select a winner.

## Production adoption

The first implementation has three slices:

1. Add the host-neutral snapshot, declaration, validation, and query contract,
   plus exact inventory-generation identity if the implementation cannot
   preserve inventory reference identity directly.
2. Extend the checked-in Platform catalog generator and loader with explicit
   local prune-entry-to-Library-row declarations. Generation fails when a
   declared key does not resolve. Runtime Browser and CLI consumers load the
   same host-neutral contract; neither host joins `rows` and `supplies`.
3. Compose the result with `PlatformSupplyReceipt` in the Assembly Reference
   Resolution Ladder after PackageHouse framework-reference and dependency
   selection.

The current Browser schema's separate `rows` and `supplies` arrays are
migration input, not the new owner. New code must issue modern
PlatformHouse population evidence and the correspondence snapshot rather than
adding a second host-local relation. Installed or package-backed sources that
have only an acquired targeting pack return `Unavailable` until paired with an
exact declaration artifact; they do not fall back to same-name joins.

No legacy `RealizedPackageDependencyContext` adoption is planned. Package
consumers use PackageHouse results and receipts, and Platform consumers use
PlatformHouse population results and receipts.

## Evidence gates

Release tests must prove:

- exact `System.Text.Json` association from an inventory entry to one real
  Platform population member;
- one package associated with several exact members;
- explicit empty correspondence for a meta-package;
- same package and assembly text without a declaration is not association;
- projected inventory cannot produce exact correspondence;
- target, family composition, inventory, declaration, population, and source
  generation replay is rejected;
- duplicate and foreign declaration keys fail visibly;
- ambiguous declarations remain ambiguous;
- `System.Text.Json@9.0.0` plus `Subsumed` can form the resource-free Platform
  prerequisite, while `12.0.0` remains package-owned; and
- the all-subsumed scenario performs no dependency package payload
  acquisition.

The real-asset gate uses the same pinned `Microsoft.Azure.SignalR` 1.33.1
scenario as PackageHouse framework-reference evidence and resolves its
`Microsoft.AspNetCore.SignalR.Core` dependency through an exact ASP.NET Core
correspondence. Synthetic fixtures cover multi-Library, ambiguity, replay, and
failure shapes that real packs do not expose predictably.

## Non-goals

- Inferring correspondence from names, paths, prefixes, or pack labels.
- Redefining `PackageOverrides.txt`, `FrameworkList.xml`, or
  `PlatformManifest.txt`.
- Selecting Platform families from a package framework reference.
- Comparing package and assembly versions.
- Acquiring a pruned dependency package to answer the live route question.
- Defining PackageHouse or PlatformHouse settlement.
- Performing Metadata binding or route precedence.
- Supporting Windows Metadata.
- Rendering correspondence as a CLI or Browser section.
