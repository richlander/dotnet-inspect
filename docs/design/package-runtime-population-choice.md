# Package runtime population choice

## Status and ownership

Status: **proposed; not implemented**.

This document is the normative owner for host-neutral runtime-population
choice, tracked by
[#10241](https://github.com/richlander/dotnet-inspect/issues/10241).

The exact claim is:

> Given one exact Package endpoint, or an ordered pair of exact Package
> endpoints, whose operation requires implementation Library bytes and whose
> target-framework request is already fixed, choose one canonical runtime
> identifier axis for that operation or return typed non-success before
> Package Asset Selection issues any implementation population.

The choice owner selects the runtime axis. It does not enumerate package
entries, select target frameworks, issue asset populations, admit managed
images, acquire content, resolve Source, compare implementations, or present
the result.

[Package asset-selection correspondence](package-asset-selection-correspondence.md)
remains the lower mechanism that issues one exact target-and-RID
implementation population or typed non-success for each endpoint.
[Package Library Scope](package-library-scope.md) consumes the issued role
population only after this choice when the operation depends on Library
evidence.

## Product rule

Runtime-population choice applies only when an operation requires
implementation bytes and no neutral implementation population answers the
request. Compile/API operations retain their compile population and do not
invoke this policy merely because runtime folders exist.

One request uses one runtime axis:

- a single-endpoint request selects one RID for that endpoint;
- a paired request selects one common RID for both endpoints; and
- no request merges RIDs or assigns unrelated RIDs independently to the two
  endpoints.

An exact RID may come from an explicit product gesture or exact retained
operation context. The executing host RID, archive enumeration order, display
text, package ID suffix, and a RID graph do not supply implicit authority.

Without an exact RID:

- a single endpoint infers its RID only when exactly one eligible RID exists;
- a pair infers its RID only when the canonical intersection of both eligible
  RID sets contains exactly one RID;
- zero eligible single-endpoint RIDs is no match;
- zero common paired RIDs is missing counterpart; and
- several eligible single-endpoint or common paired RIDs are ambiguous.

An explicit or retained RID in a paired request applies unchanged to both
endpoints. If either endpoint does not carry an eligible population for that
RID, the result is missing counterpart. This owner does not infer RID renames,
compatibility, fallback, or replacement across package versions.

## Requests and policy source

The public type names may change during implementation. The distinctions may
not.

```text
PackageRuntimePopulationChoiceRequest
  OperationAssociation
  Endpoints
    Single(PackageEndpoint)
    Pair(Before PackageEndpoint, After PackageEndpoint)
  RuntimeAuthority
    None
    Explicit(RuntimeIdentifier)
    RetainedContext(RuntimeIdentifier, ContextAssociation)

PackageEndpoint
  exact Package source coordinate
  exact package-content generation
  exact requested target framework
  owner-issued eligible runtime-population inventory
```

`OperationAssociation` identifies the exact Source or Compare request that
will consume the result. It is not a display name or an open operation-kind
string. Each adopter defines and supplies its native association without
moving Source or Compare lifecycle into this owner.

`Explicit` records an exact user or API choice admitted by the consuming
operation. `RetainedContext` records an exact RID and the owner-issued
association that made it current for this request. Equality of the RID string
alone cannot relabel one context as another.

`None` permits only the one-candidate inference rules above. It does not mean
product default, host default, first candidate, or best candidate.

## Lower-owner inventory

Each endpoint inventory is immutable evidence issued by Package Asset
Selection for one exact content generation and target-framework request. It
contains every structurally eligible canonical
`runtimes/<rid>/lib/<tfm>` population that could answer that request, including
the exact RID, selected target framework, population identity or path, and
ordered asset identities.

The inventory excludes neutral `lib`, `runtimes/<rid>/native`, analyzers,
build assets, tools, satellites, and every unrelated role. It does not open
candidate assemblies. Managed-image admission remains downstream.

This owner may compare canonical RID identities and count or intersect
inventory entries. It may not reconstruct inventory from archive paths or
repeat target-framework compatibility. Package Asset Selection needs a
focused prerequisite if its current receipt cannot expose this complete
candidate evidence; that prerequisite remains owned and reviewed there.

## Outcomes and evidence

```text
PackageRuntimePopulationChoiceResult
  Selected
    exact OperationAssociation
    exact endpoint Package coordinates and generations
    exact requested target framework per endpoint
    exact selected target framework per endpoint
    one selected canonical RuntimeIdentifier
    ChoiceSource
      Explicit
      RetainedContext
      SingleEligible
      UniqueCommon
    exact selected population identity per endpoint
    lower owner-issued selection receipt per endpoint
  NoMatch
    exact endpoint and complete eligible RID candidates
  Ambiguous
    exact endpoint or pair and complete eligible RID candidates
  MissingCounterpart
    exact pair, requested or inferred RID evidence, and each endpoint's
    complete eligible RID candidates
  Refused
    invalid, stale, foreign, or incomplete association evidence
  Failed
    complete lower-owner failure evidence
```

Every outcome remains tied to the exact request. `Selected` proves that the
same RID axis governed every endpoint, not that the selected bytes are equal,
compatible, portable, or successfully admitted. Non-success retains enough
candidate evidence for a host to explain the next action without parsing a
selector message or rescanning the archive.

The selected target framework remains endpoint-local. A pair may select
different compatible target frameworks while sharing one exact RID. The
result retains both values and does not describe them as equal.

## Source adoption boundary

Package Source consumes the single-endpoint arm only when Source requires one
implementation Library image. It selects one exact population and Library
before PDB, SourceLink, or decompilation work begins.

Equal PDB checksums, SourceLink mappings, source URLs, source text, or assembly
names across several RID populations cannot prove which implementation
Library the request selected. Source equivalence therefore cannot collapse an
ambiguous runtime choice into success.

The Source adopter owns its explicit gesture and retained-context authority,
Library selection inside the chosen population, Source operation, user-visible
diagnostics, and CLI and Browser/Wasm presentation.

## Compare adoption boundary

An implementation-byte Package comparison consumes the paired arm. Both
endpoints use the one selected common RID. An explicit or retained RID must be
available on both endpoints; otherwise comparison returns missing counterpart
before implementation bytes are paired.

Compile/API comparison modes continue to use their compile populations and do
not adopt this policy. The Compare owner classifies which modes require
implementation bytes, supplies the ordered endpoint association, and owns
comparison semantics, correspondence, result shape, and presentation.

Different endpoint RIDs, RID compatibility, and RID renames are not inferred.
A future cross-RID comparison would require its own explicit comparison policy
and evidence rather than weakening this common-axis rule.

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
empty-compile-group mechanics. A scenario-adjacent fixture supplies two
managed populations, such as `linux-x64` and `win-x64`, plus missing-common,
unique-common, and several-common controls.

The user explicitly approved this focused owner on 2026-10-20 after the
survey gap and separate Source/Compare adoption boundaries were presented.
That approval permits the specification despite the absence of a qualifying
published multi-RID package. It does not permit either adopter to claim
ecosystem prevalence.

## Adoption plan and gates

The counted delivery is:

1. define this host-neutral policy and its Package Asset Selection inventory
   prerequisite;
2. adopt the single-endpoint request for Package Source in shared code, CLI,
   and Browser/Wasm;
3. adopt the paired request for implementation Compare while leaving
   compile/API modes unchanged; and
4. retire replaced Source and Compare archive search or implicit-host-RID
   behavior.

The specification gate is Markdown validation. Later Release gates must cover:

- explicit and retained exact RID authority;
- one eligible single-endpoint RID;
- zero and several eligible single-endpoint RIDs;
- one unique common RID across an ordered pair;
- zero and several common RIDs;
- explicit paired RID missing from either endpoint;
- distinct endpoint target frameworks under one common RID;
- stale or foreign operation/context associations;
- Source refusing source-equivalence inference across ambiguous RIDs;
- Compare refusing unrelated endpoint RIDs; and
- compile/API comparison bypassing runtime choice.

These product gates are **unverified** until their owning implementation and
adoption slices land.

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
- package acquisition, caching, admission, Workspace membership, or
  Navigation; or
- native, analyzer, build, tool, or satellite asset roles.
