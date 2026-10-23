# Package runtime population choice

## Status and ownership

Status: **shared policy, Package Navigation, and Source adoption implemented;
Compare adoption pending**.

This document is the normative owner for the package-local Selection RID
policy, tracked by
[#10241](https://github.com/richlander/dotnet-inspect/issues/10241).

The exact claim is:

> Given one exact Package selection whose target-framework request is already
> fixed and whose implementation Library must come from RID-qualified runtime
> assets, choose one canonical Selection RID or return typed non-success before
> Package Asset Selection issues the implementation population.

This owner chooses the RID used by one package-local Selection coordinate. It
does not enumerate package entries, select target frameworks, issue asset
populations, admit managed images, acquire content, resolve Source, compare
implementations, or own Package navigation presentation.

[Package asset-selection correspondence](package-asset-selection-correspondence.md)
remains the lower mechanism that identifies eligible runtime populations and
issues one exact target-and-RID implementation population or typed non-success.
[Package Library Scope](package-library-scope.md) consumes that issued role
population only after this choice when the operation depends on Library
evidence.

## Product rule

Runtime-population choice applies only when an operation requires
implementation bytes and no neutral implementation population answers the
request. Compile/API operations retain their compile population and do not
invoke this policy merely because runtime folders exist.

An explicit product gesture or exact retained Package coordinate supplies an
exact Selection RID. Without that authority:

- zero eligible RIDs is no match;
- exactly one eligible RID is inferred;
- several eligible RIDs select `linux-x64` when it is present; and
- several eligible RIDs without `linux-x64` are ambiguous and require an
  explicit RID.

`linux-x64` is the product-default Selection RID because dotnet-inspect already
uses it as representative CoreCLR implementation IL. The choice is stable
across hosts and does not describe the inspected bytes as executable on the
current host. The implementation must expose one shared product currency
rather than copy the literal across Package, CLI, and Browser adopters.

An exact RID selects only that RID. If it is absent, the result is no match;
the policy does not silently fall back to the sole remaining RID, the product
default, or another candidate.

The executing host RID, archive enumeration order, display text, package ID
suffix, and a RID graph do not supply implicit authority. This owner does not
infer RID compatibility, fallback, renames, or replacement across package
versions.

## Traversal and Selection are different policies

Package-local Selection retains independent target-framework and RID
decisions:

- an omitted target framework uses PackageHouse's owner-issued
  `HighestAvailable` policy;
- an omitted RID is irrelevant while a neutral implementation population
  answers the request; and
- when RID qualification is required, the zero, sole, product-default, and
  ambiguity rules above choose or reject the Selection RID.

Neither decision inherits a Workspace Traversal target or the executing host's
runtime context merely because the package participates in that Workspace. An
operation that needs Traversal and Selection carries both typed decisions.

The Selection RID is retained in the exact Package coordinate after choice.
Later Source or Compare work consumes that retained value; it does not infer a
new RID from the Workspace, from selected Library paths, or from the other
endpoint of a comparison.

## Request and policy source

The shared implementation uses
`PackageRuntimePopulationChoiceRequest`,
`PackageRuntimePopulationAuthority`, and
`PackageRuntimePopulationChoiceOperationAssociation`. The distinctions below
are binding.

```text
PackageRuntimePopulationChoiceRequest
  PackageRuntimePopulationChoiceOperationAssociation
  exact Package source coordinate
  PackageAssetSelectionReceipt
    exact package-content generation
    exact requested target framework
    owner-issued eligible runtime-population inventory
  PackageRuntimePopulationAuthority
    None
    Explicit(RuntimeIdentifier)
    RetainedCoordinate(PackageRuntimePopulationCoordinateAssociation)
```

`PackageRuntimePopulationChoiceOperationAssociation` identifies the exact
Package selection that will consume the result. It is not a display name or an
open operation-kind string.

`Explicit` records an exact user or API choice admitted by the consuming
operation. `RetainedCoordinate` records an exact RID and the owner-issued
Package-coordinate association that made it current. Equality of the RID
string alone cannot relabel one coordinate as another.

`None` authorizes the sole-RID and `linux-x64` default rules. It does not mean
host default, first candidate, newest candidate, or best candidate.

The lower receipt is policy-neutral: its requested RID is absent. Explicit and
retained authority are applied only by this owner, so a lower receipt that
already carries an exact RID is rejected at request construction rather than
being relabeled as another choice source.

## Lower-owner inventory

The immutable inventory is evidence issued by Package Asset Selection for one
exact content generation and target-framework request. It contains every
structurally eligible canonical `runtimes/<rid>/lib/<tfm>` population that
could answer that request, including the exact RID, selected target framework,
population identity or path, and ordered asset identities.

The inventory excludes neutral `lib`, `runtimes/<rid>/native`, analyzers,
build assets, tools, satellites, and every unrelated role. It does not open
candidate assemblies. Managed-image admission remains downstream.

This owner may compare canonical RID identities and count inventory entries.
It may not reconstruct inventory from archive paths or repeat
target-framework compatibility. Package Asset Selection needs a focused
prerequisite if its current receipt cannot expose this complete candidate
evidence; that prerequisite remains owned and reviewed there.

The host-neutral projection presents the product default first when it is
available, followed by the remaining canonical RIDs in ordinal order. This is
the stable available-RID inventory used by product gestures; it is not a
selection algorithm or archive order.

## Outcomes and evidence

```text
PackageRuntimePopulationChoiceResult
  Selected
    exact PackageRuntimePopulationChoiceOperationAssociation
    exact Package coordinate and content generation
    exact requested and selected target framework
    exact selected canonical RuntimeIdentifier
    ChoiceSource
      Explicit
      RetainedCoordinate
      SingleEligible
      ProductDefault
    exact selected PackageRuntimeAssetPopulation
    PackageRuntimePopulationCoordinateAssociation
    lower owner-issued selection receipt
    complete ordered available-RID inventory
  NoMatch
    exact request and complete ordered available-RID inventory
  Ambiguous
    exact request and complete ordered available-RID inventory
  Refused
    invalid, stale, foreign, or incomplete association evidence
  Failed
    complete lower-owner failure evidence
```

Every outcome remains tied to the exact request. `Selected` proves which RID
governed the package-local implementation population, not that its bytes are
portable, executable on the host, equal to another package version, or
successfully admitted.

The result inventory is product-ordered: `linux-x64` first when present, then
the remaining canonical RIDs in ordinal order. The lower receipt remains in
its owner-issued ordinal order.

Non-success retains enough candidate evidence for a host to explain and offer
the next selection without parsing a selector message or rescanning the
archive.

## Package coordinate and Browser adoption

The selected RID is Package-coordinate state, parallel to package-local TFM
Selection rather than a Source- or Compare-local setting. Changing it submits
a typed Package-coordinate transition and replaces every derived Library,
Type, Member, Source, and implementation-Compare result.

The Inspect Web Navigation adoption lists the complete available-RID
inventory directly below the TFM inventory in the Package navigation pane.
The active row is the exact RID retained by the current Package coordinate.
When the result is ambiguous, no RID row is active until the user chooses one.
The same inventory remains reachable through the collapsed content-navigation
surface at narrow widths.

The Navigation owner retains row interaction, responsive labels, focus,
history, pending-state, and presentation details. This owner supplies the
ordered canonical inventory, exact active identity, and typed transition
input only.

## Source adoption boundary

Package Source consumes the retained Selection RID only when Source requires
one implementation Library image. It selects one exact population and Library
before PDB, SourceLink, or decompilation work begins.

Equal PDB checksums, SourceLink mappings, source URLs, source text, or assembly
names across several RID populations cannot prove which implementation Library
the Package coordinate selected. Source equivalence therefore cannot collapse
an ambiguous runtime choice into success.

The Source adopter owns Library selection inside the chosen population, Source
operation semantics, user-visible diagnostics, and CLI and Browser/Wasm
presentation.

## Compare adoption boundary

An implementation-byte Package comparison consumes one exact Selection RID
from the Package-owned comparison target state and applies it unchanged to
both endpoints. If either endpoint cannot issue that RID population, Compare
reports its typed missing-counterpart outcome before pairing implementation
bytes.

Compare does not ask this owner to intersect endpoint RID sets, search for a
uniquely common RID, or choose unrelated per-endpoint RIDs. The user changes
the one Package Selection RID through the Package coordinate inventory when a
different common population is wanted.

If the current Package endpoint uses a neutral implementation population but a
comparison endpoint requires RID qualification, the Package-owned comparison
target uses the retained explicit RID when present and otherwise the product
default `linux-x64`. It does not derive authority from the baseline endpoint.

Compile/API comparison modes continue to use compile populations and do not
adopt this policy. The Compare owner classifies which modes require
implementation bytes and owns endpoint correspondence, missing-counterpart
semantics, result shape, and presentation.

## Motivation and evidence boundary

The Package Library survey under #9833 examined the 500 most downloaded
nuget.org packages at their latest stable versions:

- 15 packages had neutral `lib` plus RID-specific managed implementations;
- 4 runtime packs had managed Libraries only under
  `runtimes/<rid>/lib/<tfm>`; and
- every runtime-only package had exactly one eligible RID.

That survey found no maintained package with no neutral `lib` population and
several managed runtime populations. The published one-RID layouts
`Microsoft.NETCore.App.Runtime.linux-x64@10.0.12` and
`runtime.unix.System.Private.Uri@4.3.0` ground the runtime-directory and
empty-compile-group mechanics.

A scenario-adjacent fixture supplies two managed populations such as
`linux-x64` and `win-x64`, plus a several-RID package without `linux-x64`.
The operator explicitly approved this focused owner despite the survey gap,
then selected `linux-x64`, sole-RID inference, and typed ambiguity when that
default is absent. The design makes no ecosystem-prevalence claim.

## Adoption plan and gates

The counted delivery is:

1. define this host-neutral Selection RID policy and its Package Asset
   Selection inventory prerequisite;
2. adopt retained Selection RID state and the RID inventory beneath TFMs in
   Package Navigation;
3. adopt the selected coordinate for Package Source in shared code, CLI, and
   Browser/Wasm;
4. adopt the same exact RID across implementation Compare endpoints while
   leaving compile/API modes unchanged; and
5. retire replaced Source and Compare archive search or implicit-host-RID
   behavior.

The design, lower-owner inventory prerequisite, shared policy, Package
Navigation, and Source adoption complete steps 1-3. Steps 4-5 remain production
adoption work.

The shared policy is gated by
`PackageRuntimePopulationChoiceTests` and the resource-free receipt graph in
`PackageAssetSelectorTests`. They cover:

- zero and one eligible RID;
- several eligible RIDs with `linux-x64`;
- several eligible RIDs without `linux-x64`;
- explicit and retained exact RID authority;
- an explicit RID absent from the inventory;
- stable product-default-first RID inventory ordering;
- foreign retained coordinate and generation evidence;
- lower-owner failure preservation; and
- rejection of a lower receipt that already carries an exact RID.

The Package Navigation Release gates cover neutral bypass, product-ordered RID
rows beneath TFMs, active retained identity, ambiguity with no active row, and
exact Root reopening.

The Source Release gates cover exact RID retention in Browser request
composition and Worker transport, exact-RID scope reopening before Type,
Member, member-document, or graph Source work, and declaration-context
admission with the same `FindPackageSourceRequest`. A two-RID Browser fixture
keeps only the retained population valid, so archive-order or source-equivalence
inference cannot satisfy the request. CLI Package Source was already conformant:
Package Library selection records the selected RID in
`AssemblyResolutionProvenance.PackageAsset`, and package-local Portable PDB
settlement constructs its exact target from that retained provenance.

Later adoption Release gates must cover:

- Compare applying one exact Package Selection RID to both endpoints;
- Compare reporting a missing counterpart without common-RID search; and
- compile/API comparison bypassing runtime choice.

The Source adoption gates are **verified**. The Compare adoption gates remain
**unverified** until their owning slice lands.

## Non-claims

This owner does not define:

- NuGet RID graphs, host-runtime compatibility, restore behavior, or RID
  fallback;
- target-framework parsing, compatibility, or selection;
- Package Asset Selection inventory construction or population issuance;
- neutral implementation overlays or compile population policy;
- exact Library choice inside an issued population;
- PDB, SourceLink, decompilation, Source equivalence, or Source presentation;
- comparison mode applicability, endpoint Library correspondence, diff or
  clone semantics, or Compare presentation;
- Package Navigation interaction, responsive layout, focus, or history;
- package acquisition, caching, admission, or Workspace membership; or
- native, analyzer, build, tool, or satellite asset roles.
