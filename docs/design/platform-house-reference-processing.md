# PlatformHouse realization and reference processing

## Status and approved scope

This document is the normative owner for the host-neutral `PlatformHouse`
composition boundary. It is tracked by
[#6301](https://github.com/richlander/dotnet-inspect/issues/6301) and is a
focused prerequisite of the platform-first tracker
[#6228](https://github.com/richlander/dotnet-inspect/issues/6228).
Versionless target defaults are tracked by the composition map
[#7742](https://github.com/richlander/dotnet-inspect/issues/7742); the focused
PlatformHouse contract slice is
[#7743](https://github.com/richlander/dotnet-inspect/issues/7743), and staged
target-selection execution is tracked by
[#7778](https://github.com/richlander/dotnet-inspect/issues/7778), with
selected-target one-Library realization tracked by
[#7825](https://github.com/richlander/dotnet-inspect/issues/7825) and
selected-target complete reference-population realization tracked by
[#7892](https://github.com/richlander/dotnet-inspect/issues/7892).
Exact-target Type resolution from an implementation-view candidate is tracked
by [#8298](https://github.com/richlander/dotnet-inspect/issues/8298).
The former documentation-source extension from
[#6375](https://github.com/richlander/dotnet-inspect/issues/6375) transfers to
[DocumentationHouse](documentation-house.md) under #6579.

The design depends on:

- [Platform Target Currency](platform-target-currency.md) for exact runtime
  and ASP.NET Core family targets;
- [Platform Library Population Declaration](platform-library-population-declaration.md)
  for target-independent Workspace relevance;
- [Exact Library Source Coordinate](exact-library-source-coordinate.md) for the
  resource-free Platform Library identity retained through realization;
- [Workspace Ecosystem Registration Handoff](workspace-ecosystem-registration-handoff.md)
  for curated-Workspace construction, ecosystem projections, and their lower
  population declarations;
- [Workspace registration and call-graph focal length](workspace-registration-and-call-graph-scope.md)
  for selection of registered ecosystem populations by one bounded operation;
- [Package dependency evidence](package-dependency-evidence.md) for typed
  package references and their source provenance;
- [Platform/package pruning](platform-package-pruning.md) for exact-target
  package subsumption facts retained by the package owner;
- [Platform package supply policy](platform-package-supply-policy.md) for the
  typed upstream delegation that a package-processing owner may issue after
  consuming exact-target pruning facts;
- [Structured type-forwarding resolution](type-forwarding-resolution.md) for
  Metadata-owned forwarding and binding outcomes;
- [Platform Manifest Formats](platform-manifest-formats.md) for host-neutral
  interpretation of shared-framework runtime configurations and dependency
  manifests;
- [Platform composition and overlays](platform-composition-and-overlays.md)
  for source-specific coherent implementation realization;
- [Library ownership and borrowing](library-ownership-and-borrowing.md) for the
  shared realized-Library reference, content roles, aggregate owner, operation
  authority, and synchronous content snapshots;
- the
  [Assembly Reference Resolution Ladder](assembly-reference-resolution-ladder.md)
  for the ordering and lifetime of an applicable-platform rung; and
- [DocumentationHouse](documentation-house.md) for downstream documentation
  settlement over owner-issued platform reference and implementation evidence.

The approved scope is:

- `PlatformHouse` is the sole product-facing platform realization and
  reference-processing facade;
- typed package references enter
  [PackageHouse](package-house.md), which alone applies the
  pruning service when deciding whether to continue package processing or
  issue a typed platform delegation;
- package, platform, and direct-library flows converge at the shared Library
  contract after their independently owned realization paths;
- selected Workspace ecosystem populations lower to family-preserving target
  demands before `PlatformHouse` performs target settlement or realization;
- platform adapters may project settled reference and implementation evidence
  into DocumentationHouse contributions without moving documentation
  settlement into PlatformHouse;
- .NET Standard remains transparent compatibility processing rather than a
  `PlatformFamily`, registration population, or implementation target; and
- Metadata type forwarding connects reference contracts to definitions under
  a platform-authorized binding context.

The first production consumer is the applicable-platform rung of the assembly
reference resolution ladder. PackageHouse delegation, Queries, Workspace
composition, CLI platform operations, and Inspect Web adopt the same House
contract in separately reviewed slices.

Production-adoption step 3 is implemented by the resource-free request,
operation, source-plan, contribution, outcome, and receipt contracts in
`DotnetInspector.PlatformHouse`. No source implementation or product consumer
moved in that slice. After the PackageHouse contract floor landed, the request
origin also gained an orchestration-owned delegation association. It retains
no PackageHouse type or receipt; orchestration keeps the package and platform
receipts separate.

The implemented contract retains exact requests, capabilities, source
generations, coordinates, targets, contributions, and settlement dispositions
directly. It does not mint separate House-local occurrence identities for
source contributions, source-to-target correspondence, or target selection.
The completion identity remains because it binds a detached completion receipt
to its separately live operation value.

Exact-target, one-Library shared ownership handoff is implemented in
`DotnetInspector.PlatformHouse.Execution` under #7237. It validates selected
resource-free source content against the exact request, target, population,
view, Artifact reference, and assembly identity before atomically constructing
one `LibraryReference` and `LibraryContentOwner`. Artifact registration
provenance binds the exact source realization to the published content, and
Metadata's owner-issued Artifact projection binds managed identity and MVID to
that exact Artifact. The owning outcome transfers the owner separately from its
resource-free value and composed receipt.
Reference-only, reference-plus-implementation, and implementation-only role
closure are gated in Release.

Installed successful-result materialization is implemented in
`DotnetInspector.PlatformHouse.Execution.Installed` under #7269. It accepts
only owner-issued successful installed source results for one exact assembly
demand, selects that assembly from each immutable source snapshot, and
publishes the required views into one request-bounded Artifact generation.
Installed coordinates and content facts remain source-specific provenance;
`PlatformLibraryArtifactProvenance` binds each published item to its exact
House realization contribution, and Metadata issues the exact Artifact
projection consumed by the shared one-Library executor.

The completed installed result returns the `LibraryContentOwner` and
`ArtifactSetSession` as separate caller-owned authorities. Starting Artifact
retirement while the Library remains live waits for the transferred content
children; retiring the Library then permits Artifact retirement to complete.
Terminal executor results and cancellation before the handoff retire every
composition-owned content lease and the session before they escape. The
materializer accepts explicit operation-consumed work, matching the shared
executor, because reference and terminal source contracts do not always issue
exact aggregate work observations. Successful implementation realizations do
issue their exact source-observed manifest-plus-assembly byte total. The
executor neither guesses absent observations nor turns selected payload size
into a claim about all source work.
The selected-target executor therefore applies one source-neutral accounting
rule to installed and package-backed adapters: an unmeasured failed, rejected,
or incomplete source reserves the residual assembly, compiled-XML, and byte
allowance delegated to that invocation. Exact owner-issued observations remain
exact. Unmeasured unavailability remains zero so an ordinary absent source can
fall back; every other unobserved terminal capable of hiding performed work
cannot be reused as though its invocation consumed nothing.

Selected-target complete reference-population realization is implemented in
`DotnetInspector.PlatformHouse.Execution` under #7892. It composes the same
family-default target selector, source-policy reducer, cumulative finite-work
accounting, and selected-association route with the complete-population
Artifact materializer. Installed and package-backed factories prepare only
authoritative non-empty reference populations for the frozen target.
Completion transfers every source-ordered Library owner beside one separate
Artifact session while retaining the original request, selected target,
discovery and realization settlements, and cumulative work. Terminal paths
transfer neither authority.

Package-backed successful-result materialization is implemented in
`DotnetInspector.PlatformHouse.Execution.Packages` under #7304. It accepts only
adapter-issued successful package reference and implementation results for one
exact assembly demand. Source-specific preparation selects one equivalent
immutable member per requested view and retains package candidate, configured
authority, producer identity, content generation, payload origin, member
coordinate, and digest evidence as resource-free Artifact provenance.

Installed and package-backed preparation both invoke the source-neutral
Artifact materialization kernel in
`DotnetInspector.PlatformHouse.Execution`. The kernel bounds one Artifact
generation to the selected content, obtains Metadata's exact projections,
rejects physical managed-identity mismatch, invokes the generic one-Library
executor, and owns query/content authority cleanup and Artifact retirement
precedence. Source projects retain only source-specific selection and
provenance; they consume the narrow typed composition contract without friend
access and do not duplicate publication or cleanup behavior.

The completed package-backed result, like the installed result, returns the
`LibraryContentOwner` and `ArtifactSetSession` as separate caller-owned
authorities. Package Source settlement may complete before either authority is
retired because successful package Platform realizations already own detached
immutable bytes. No source client, Package Source lease, package payload, or
store lifetime enters the Artifact, Library, or House result.

Installed reference-only complete-population materialization is implemented in
`DotnetInspector.PlatformHouse.Execution.Installed` under #7322. It consumes one
authoritative adapter-issued installed reference population, preserves source
order, publishes every distinct managed assembly into one finite Artifact
generation, obtains Metadata's exact projection for each Artifact, and
constructs one ordinary `LibraryContentOwner` per reference-only Library.

The population operation accepts all content authorities atomically.
Completion transfers the ordered owners beside resource-free Library references
and returns the Artifact session separately. Every terminal, cancellation, or
partial-construction path retires accepted owners and releases unaccepted
content children before Artifact retirement. One population-wide source
contribution appears once in the House settlement rather than once per Library.

Installed implementation-only complete-population materialization is
implemented under #7339. It consumes one authoritative adapter-issued
manifest-defined implementation closure, preserves source order, and reuses the
same population-wide bounded Artifact publication, exact Metadata projection,
atomic owner transfer, cleanup, and settlement contracts. Platform-owned
implementation declaration-surface evidence assigns each selected runtime
content both mandatory Library roles.

Installed paired complete-population materialization is implemented under issue #7354.
It consumes independently authoritative adapter-issued reference and
implementation populations, publishes both into one finite Artifact generation,
and joins exact managed identities through the source-neutral PlatformHouse
kernel. The result is the deterministic lossless union defined above: paired
Libraries retain separate API and implementation content, unmatched references
remain reference-only, and unmatched implementations retain the
implementation-only declaration-surface role closure. Both source contributions
settle once. Completion transfers every Library owner beside the separate
Artifact session; terminal and cancellation paths transfer neither authority.

Package-backed paired complete-population materialization is implemented under
issue #7383. It consumes independently authoritative adapter-issued package
reference and implementation populations and invokes the same source-neutral
lossless-union kernel. Every Library retains its exact package candidate,
authority, producer, content generation, origin, coordinate, digest, and
framework evidence as applicable. Package Source settlement remains independent
because the source results already own detached immutable bytes. Completion
transfers every Library owner beside the separate Artifact session; terminal
and cancellation paths transfer neither authority.

Package-backed reference-only complete-population materialization is implemented
under issue #7395. It consumes one authoritative adapter-issued package
reference population, preserves source order, and invokes the source-neutral
reference-population Artifact and ownership kernel. Every Library retains its
exact package candidate, authority, producer, content generation, origin,
coordinate, identity, and source generation. Package Source settlement remains
independent because the source result already owns detached immutable bytes.
Completion transfers every reference-only Library owner beside the separate
Artifact session; terminal and cancellation paths transfer neither authority.

Package-backed implementation-only complete-population materialization is
implemented under issue #7457. It consumes one authoritative adapter-issued
package runtime population, preserves source order and exact per-member package
and runtime-support provenance, and invokes the source-neutral implementation
population Artifact and ownership kernel. Platform-owned declaration-surface
evidence assigns each selected runtime content both mandatory Library roles.
Package Source settlement remains independent because the source result already
owns detached immutable bytes. Completion transfers every implementation-only
Library owner beside the separate Artifact session; terminal and cancellation
paths transfer neither authority.

Complete-population materialization also preserves source-issued logical
membership. Each returned `PlatformPopulationMember` retains the exact
`PlatformFamilyTarget` and `Focus` or `BindingSupport` role supplied by the
source integration, and the value, receipt, Library reference, and owner remain
aligned by exact index. ASP.NET Core implementation closure therefore retains
ASP.NET Core focus members and .NET Runtime binding-support members as distinct
family populations. The Library source coordinate uses the member family, not
the root request family. Paired views may join equal managed identities only
when their member attribution also agrees; unclassified or mismatched
attribution is a typed rejection before ownership escapes.

The `Failed` House terminal arm and resource-free typed failure-stage evidence
are implemented, including installed and package-backed Artifact publication
and retirement stages. Internal Library operation leases, cleanup-failure
production by non-owning operations, and product adoption remain unverified.
Further PlatformHouse adoption continues under #7177 and #6621 slice 5; the
source materialization slices are tracked by #7269, #7304, #7322, #7339, and
issues #7354, #7383, #7395, and #7457.

This is one owner claim. The design specifies the House request, settlement,
result, evidence-retention, and encapsulation contracts. It consumes the
owner-issued inputs listed above without redefining their identities,
algorithms, lifetimes, or failure semantics.

Documentation settlement was originally added as a PlatformHouse extension
under #6375. That cohesive responsibility has transferred to
[DocumentationHouse](documentation-house.md) under #6579. The superseded
PlatformHouse documentation request, source-facet, contribution, attempt, and
receipt types have been removed; they are not part of this contract.

The [PackageHouse](package-house.md) and direct-library paths below form a thin composition map,
not additional normative owners inside this document. Their focused designs
own their complete requests, outcomes, and receipts. This document owns only
the typed boundary into PlatformHouse and the evidence its library-focused
result must preserve at the shared Library inspection handoff.

## Authority and exact claim

**PlatformHouse Realization and Reference Processing** owns:

> Given one typed platform target demand, one host-authorized platform source
> plan, one closed platform operation, and finite operation work, settle the
> demand to one exact `PlatformFamilyTarget`, then compose
> source candidates and owner-issued platform facts into one typed settlement.
> When that operation returns realized Libraries, transfer each shared
> `LibraryContentOwner` beside its resource-free `LibraryReference` and exact
> content references. Preserve the request, any selected exact target, selected
> discovery contributions, source, reference-contract,
> implementation-supplier, forwarding, completion, and failure evidence
> without retaining live authority in House evidence.

It owns:

- the product-facing `PlatformHouse` facade;
- common request context shared by platform operations;
- composition of explicit target demands with owner-issued version-selection
  results into one exact `PlatformFamilyTarget`;
- closed realization, assembly-reference, and type-resolution operation
  shapes;
- host-authorized source-capability composition;
- candidate validation, comparison, selection, and settlement;
- the distinction between reference and implementation demand;
- reference-to-implementation correspondence between settled platform views;
- platform-specific invocation of Metadata forwarding;
- validation and transfer of selected live source content into one shared
  Library owner per realized Library;
- provenance-retaining `LibraryReference` and `LibraryContentReference` results
  for one-library and population demand;
- separation of caller-owned Library owners from resource-free House values
  and receipts;
- acceptance of ordinary platform requests issued from orchestration-owned
  typed delegations without interpreting the package reference that caused
  them;
- validation and retention of Workspace population-declaration correspondence
  carried by an operation-issued platform request;
- the House result envelope and settlement receipt;
- visible unavailable, ambiguous, rejected, failed, and incomplete outcomes;
  and
- the rule that product reference processing does not compose lower platform
  mechanisms outside the House.

It does not own:

- platform family, target-framework, or version identity;
- target-independent ecosystem registration;
- Workspace construction policy, registration revisions, focal-length selection,
  population planning, or cross-population result composition;
- package identity, package source authority, package dependency evidence,
  package candidate selection, package version resolution, package payload
  acquisition, or package asset selection;
- package-reference processing, pruning-service invocation, prune inventory
  construction, supplied-version comparison, or staleness;
- package-to-library or package-to-platform-library correspondence;
- framework compatibility, runtime roll-forward, or source-specific version
  inventory and ranking algorithms;
- assembly identity, declaration probing, binding selection, forwarding hops,
  or Metadata resolution outcomes;
- installed-hive discovery, manifest-defined implementation closure,
  reference-pack layout, package-pack acquisition, Browser catalog generation,
  or source-specific cache policy;
- Library reference, content-role, construction, borrowing, operation-lease,
  or retirement semantics;
- assembly-reference rung ordering or package-route reachability;
- Workspace admission, revisions, replacement, leases, or participant
  lifetime;
- call-graph traversal, search ranking, section selection, rendering, or host
  presentation;
- XML-documentation identity, grammar, parsing, or textual normalization;
- Portable PDB identity, SourceLink mapping, source acquisition, checksum
  verification, or source-comment parsing; or
- compiled XML, authored-source documentation, documentation-channel, field,
  conflict, or package-specific documentation-source settlement.

## Why a House is needed

Current platform behavior is divided across mechanisms with different
coordinates and implicit policy:

- `PlatformResolver` combines ambient installed discovery, reference and
  implementation lookup, framework aliases, version selection, assembly
  probing, type lookup, and .NET Standard handling;
- `PlatformPackService` combines pack-name mapping, package acquisition,
  cache layout, source policy, and assembly-name bias;
- `PlatformTypeCatalog` derives a reference-pack index and applies
  type-selection heuristics;
- Queries currently map platform coordinates directly to representative
  implementation-pack packages; and
- CLI and Browser paths compose these mechanisms differently.

These are useful implementations or owner facts, but none is the product
boundary. Direct composition makes the caller responsible for distinctions it
cannot safely reconstruct:

- which exact platform target the evidence describes;
- whether the request needs reference contracts, implementation bodies, or
  both;
- whether a .NET Standard facade resolved to an implementation definition;
- whether a source result is complete, projected, stale, unavailable, or
  unauthorized; and
- whether several source results correspond to one target and may be combined.

`PlatformHouse` centralizes those decisions without centralizing every
algorithm. It is a clearing house over focused owners, not a renamed
`DotnetInspector.Services` bucket.

## Vocabulary

| Term | Meaning |
| --- | --- |
| **House target** | One exact owner-issued `PlatformFamilyTarget` against which the operation is settled. |
| **Target demand** | One exact `PlatformFamilyTarget`, one framework-scoped version-selection request, or one family-scoped named host default, each with explicit policy and authorized target-discovery capabilities where selection is required. |
| **Source plan** | An immutable host-authorized set of platform source capabilities and their explicit selection policy. |
| **Source capability** | A bounded adapter entry point for one source-specific operation. It is not source authority by display name. |
| **Source contribution** | One source's typed candidate, non-match, failure, or incomplete evidence, retaining exact target correspondence. |
| **Platform delegation** | An orchestration handoff produced from an upstream package-processing result. It authorizes one ordinary platform request for an exact family target while retaining the original package and pruning evidence outside this House. It does not identify a platform assembly. |
| **Platform Library realization** | One selected platform Library whose exact `LibraryReference` and assembly `LibraryContentReference` values retain physical identity, Platform provenance, exact target, source, and API/implementation role correspondence. Its separately returned `LibraryContentOwner` is the sole live content authority. |
| **Library ownership handoff** | The owning realization result that transfers one `LibraryContentOwner` to the caller beside its resource-free `LibraryReference`. The House value and receipt retain the reference and correspondence, never the owner or an operation lease. |
| **View demand** | Whether the consumer requires reference contracts, implementation bodies, or both. |
| **Population demand** | Whether the consumer requires one exact library or one complete family population. |
| **Reference contract** | A reference assembly or facade used to describe an API contract. It does not prove an implementation supplier. |
| **Implementation supplier** | The exact physical assembly candidate containing the implementation definition or body. |
| **View correspondence** | House-owned evidence connecting one resolved reference definition to one authorized implementation-resolution start without pretending that the transition is a Metadata forwarding hop. |
| **Settlement** | The House decision that selects, composes, or declines source contributions under the request's policy. |
| **Settlement receipt** | Resource-free evidence binding the request, target, source-plan generation, selected contributions, owner-issued resource-free results, completion, and work. |

The vocabulary deliberately does not call `.NET Standard` a platform family.
Its reference contract may participate in one operation while the House target
remains an exact .NET runtime or ASP.NET Core family target.

## Contract shape

```text
typed target demand
  + one closed operation
  + host-authorized source plan
  + finite work and cancellation
        |
        v
PlatformHouse
  1. validate request and selection policy
  2. settle one exact PlatformFamilyTarget
  3. ask only authorized source capabilities
  4. retain every owner-issued contribution
  5. compose Metadata only when the operation requires it
  6. settle the requested view and population demand
  7. construct and transfer shared Library owners for selected Libraries
        |
        v
one closed House outcome
  - completed operation result + settlement receipt
  - unavailable
  - ambiguous
  - rejected
  - failed
  - incomplete
```

The House performs no ambient source discovery. A desktop host may authorize
installed, cached, and network-backed capabilities; a Browser/Wasm host may
authorize generated, embedded, or network-backed capabilities. The House sees
only the supplied plan and never widens it because another source is locally
reachable.

### Common request context

Every operation carries:

- one typed target demand;
- one immutable source-plan identity and policy generation;
- the owner-issued Workspace revision identity or standalone operation
  identity;
- the typed platform-request origin, including an exact
  `PlatformLibraryPopulationDeclaration` or orchestration-owned delegation
  identity when one caused the operation;
- the requested reference and implementation views;
- one-library or whole-population demand;
- finite source, candidate, assembly, byte, forwarding-hop, and deadline
  budgets;
- owner-issued prerequisite correspondence; and
- caller cancellation.

The exact numeric work defaults remain host policy. The House owns validation,
checked charging, and the rule that a broad source plan or population demand
is never unbounded work.

### Target and version settlement

Target demand has three distinct typed forms:

- an **exact demand** carries one existing `PlatformFamilyTarget` unchanged;
- a **framework-scoped demand** carries one `PlatformFamily`, one exact target
  framework, one explicit version requirement, authorized target-discovery
  capabilities, and finite discovery and comparison work; and
- a **family-default demand** carries one `PlatformFamily`, one named immutable
  policy generation, its typed preferred and fallback discovery stages,
  authorized target-discovery capabilities, and finite discovery and
  comparison work. It does not manufacture a target framework before
  discovery.

The House asks only those capabilities for owner-issued exact target
candidates and composes the version-selection owner's result. It does not
parse version syntax, define compatibility or roll-forward, enumerate ambient
installations, or rank candidates by an unstated "latest" rule.

Successful target settlement freezes one exact `PlatformFamilyTarget` before
library acquisition, view selection, or Metadata work begins. Every outcome
retains the original target demand and any candidate, policy, or failure
evidence used to settle it. An omitted CLI version is therefore not an
implicit target inside the House: the command supplies a named default policy
that remains visible in the request and receipt.

#### Versionless runtime host default

> Given one versionless runtime-family demand, one staged authorized discovery
> policy, and finite work, select the greatest eligible installed exact target
> at or above `10.0.1`; only after authoritative preferred absence, select the
> greatest stable `net10.0` servicing target from complete fallback discovery.
> Freeze that exact target before realization while retaining no live discovery
> or acquisition authority in the terminal outcome.

The versionless `DotNetRuntime` policy is named and immutable by policy
generation. The `10.0.1` floor is policy data; changing it creates another
generation rather than reinterpreting an existing request or cache entry. The
policy has two ordered stages:

1. **Preferred available target.** Aggregate every authorized preferred
   discovery contribution. Eligible candidates have SemVer precedence greater
   than or equal to `10.0.1`; installed previews and release candidates are
   eligible when they meet that floor. Select the eligible exact target with
   greatest SemVer precedence, breaking equal precedence by greatest ordinal
   exact canonical version identity. An exact target already joins its TFM and
   version, so selection does not infer a TFM from display text.
2. **Stable baseline fallback.** Invoke the authorized fallback discovery
   capabilities only when every required preferred contribution
   authoritatively establishes no eligible target. Retain only stable
   candidates in the `net10.0` release band at or above `10.0.1`, then select
   the greatest SemVer precedence with the same exact-identity tie-break.

The ordinary desktop plan assigns installed discovery to the preferred stage
and package-backed discovery to the fallback stage. Consequently an installed
`10.0.1`, `11.0.0-rc.1`, or later eligible preview prevents network work; an
installed `10.0.0`, `10.0.0-rc.2`, or `9.0.x` does not. With no eligible
installed target, the fallback's complete package-version discovery determines
the current stable `10.0.x` servicing target rather than using a hard-coded
patch. Browser/Wasm may omit the preferred stage and authorize only the
package-backed fallback.

Fallback always performs current complete version discovery through the
authorized package source. A previously downloaded pack, cached payload, prior
House receipt, or installed `10.0` pack does not prove the latest stable
servicing version. Package Source retains ownership of configured-authority
aggregation and version-evidence freshness; when it cannot establish a current
complete inventory, the House does not guess from cached content.

The transitional `PlatformResolver.LookupType` behavior is supporting evidence:
it already chooses the greatest installed runtime reference-pack version and
does not query the network when that catalog exists. The named default
deliberately adds the `10.0.1` floor, cross-feature-band typed target selection,
and package-backed stable fallback rather than wrapping its path-based result.

Preferred `Unavailable(Absent)` or authoritative completed discovery with no
eligible candidate permits the fallback stage. Non-authoritative
`Unavailable(Unavailable)`, rejected, incomplete, or failed preferred evidence
does not prove absence and is terminal; cancellation remains cancellation.
Fallback partial or failed version discovery likewise cannot select from a
shortened inventory. These rules keep network access capability-gated and
prevent a source failure from becoming an apparently successful default.

The request binds each discovery capability to exactly one stage. Stage
membership is typed policy data, not inferred from capability names, source
enumeration order, paths, or whether a capability happens to use the network.
The same capability cannot occur in both stages. The source plan must authorize
every staged capability and may not authorize an unstaged target-discovery
capability for that demand.

An exact demand such as `runtime@9.0.11` bypasses the host default, including
its floor and fallback band. A framework-scoped demand applies its own explicit
version requirement. Neither form silently widens into the versionless policy.

Every completed default settlement retains the original demand, policy identity
and generation, selected exact target, and every discovery contribution needed
to justify preferred selection or fallback. Terminal outcomes retain bounded
resource-free evidence under the same correspondence rules. No source
operation, package candidate, payload, opener, callback, stream, or other live
authority enters the settlement or receipt.

Target settlement is an internal phase of the same closed House operation that
performs realization. When the selected discovery source issued an exact
source-specific candidate association needed for realization, the executor
keeps that association ephemeral and passes it only to that source's
realization adapter. Another authorized realization source receives the frozen
external exact target under its own contract. The association never enters the
House value, receipt, contribution, cache, or a separately returned
target-selection result.

The target-selection reducer consumes source results; it does not define their
inventories. Cross-feature-band installed discovery remains owned by
[Installed reference-pack realization](installed-reference-pack-realization.md),
and complete package-version discovery remains owned by
[Package-backed Platform realization](package-backed-platform-realization.md).
Those focused successors decide how their source contracts produce the exact
candidates required here.

#### Selected complete reference population

A family-default, reference-only complete-population operation selects and
realizes one authoritative reference population in the same closed House
operation. The original family-default request remains the request and receipt
identity. The selected exact target and any source-issued package discovery
association are ephemeral execution currency; neither replaces the target
demand nor enters the completed value.

After target settlement, the executor applies the request's existing Reference
source policy to lazy installed or package-backed complete-population
capabilities. A preferred installed success suppresses later package discovery
and realization. A package capability may receive a selected discovery
association only when the discovery capability and owner-issued route match
the capability declaration and the association names the frozen target.
Mismatch rejects before package operation authority is issued.

Each successful attempt must retain one authoritative complete-population
contribution for the original request, selected target, exact source
generation, and requested population. The selected attempt must contain at
least one distinct managed assembly identity. A failed, rejected, incomplete,
partial, empty, or over-budget attempt cannot publish its observed prefix as a
shortened population.

Target discovery and realization share one cumulative finite-work ledger.
Exact source observations remain exact. When an ordinary unavailable attempt
reports no work, it consumes no assembly or byte allowance. When a failed,
rejected, or incomplete attempt reports no work, the executor reserves the
delegated assembly and byte allowance before considering another source, so
unmeasured work cannot be reused.

Completion publishes the selected source's members in source order through one
bounded Artifact generation and transfers every resulting
`LibraryContentOwner` atomically beside the separately owned
`ArtifactSetSession`. The receipt retains the original request, selected target
settlement, target-discovery settlements, selected and outcome-relevant
realization settlements, source generations, and cumulative consumed work.
Every terminal path transfers neither authority and completes cleanup before
projecting its outcome. Cancellation observed during successful cleanup
remains cancellation; cleanup failure is the primary `Failed` outcome and
retains whether cancellation was observed.

This complete reference population is stage 7a input to later target-bound type
indexing. Stage 7b derives a catalog only after this operation has settled; the
population executor does not index types, route a host request, or migrate
`PlatformTypeCatalog`.

The production path in #7742 is refined here: lock this contract, add installed
cross-feature-band discovery, adapt package-backed stable fallback, implement
the source-neutral reducer, realize the selected complete reference population,
derive its target-bound type catalog, then adopt the same requests and outcomes
in the CLI and Browser/Wasm before retiring direct router selection. Rendering
is not part of target settlement; hosts project the retained typed evidence
through their existing output boundaries.

### Closed operations

The version-1 facade has three conceptual operation kinds. Exact public type and
member names may change during implementation, but the distinctions may not be
collapsed into strings, optional parameters, or nullable tuples.

| Operation | Input unique to the operation | Completed value |
| --- | --- | --- |
| **Realize** | One library identity or complete population demand | One owning Library realization or one platform population of owning Library realizations, each with resource-free source and view correspondence |
| **Resolve assembly reference** | One exact Metadata `AssemblyBindingRequest` and platform-route prerequisites | Metadata-owned binding decision plus the platform contribution used by the ladder |
| **Resolve type definition** | One exact Metadata `TypeResolutionRequest`, starting candidate with its owner-issued view, and required view | One resource-free Platform projection of the exact Metadata terminal arm and forwarding evidence, plus reference/implementation correspondence when the required view differs from the starting view |

`Realize` supports direct platform browsing and supplies source candidates for
the other operations. The two reference-resolution operations are the only
product-facing paths that may turn platform catalogs, .NET Standard reference
contracts, or platform-specific Metadata policy into a binding or
implementation result.

### Type-resolution starting views

The starting candidate's owner issues its exact view separately from the
required terminal view:

- a **Reference** start may complete a Reference request directly; an
  Implementation or ReferenceAndImplementation request additionally requires
  explicit view correspondence and a second Metadata resolution;
- an **Implementation** start may complete an Implementation request directly
  from one Metadata outcome; it does not manufacture a reference outcome or
  view correspondence; and
- ReferenceAndImplementation is a terminal demand, not a candidate view.

The House rejects an unsupported start/required-view pair before Metadata
work. The completed value and receipt preserve whether the operation returned
one reference outcome, one implementation outcome, or the existing paired
reference-and-implementation outcomes.

The live `TypeResolutionOutcome` remains inside execution while its Metadata
catalog and copied Library images are available. Before those authorities are
retired, PlatformHouse projects its exact terminal arm, ordered forwarding
hops, terminal assembly identity, physical definition address and supplier
when resolved, and typed non-success evidence into a detached
`PlatformTypeDefinitionResolutionResult`. The projection retains
owner-issued Metadata names, tokens, identities, provenance, and failure
values, but no `ResolvedAssemblyReference`, opener, candidate, context,
catalog, stream, or disposal obligation.

### View and population demand

View demand is closed:

- **Reference** requires contract assemblies suitable for API and Metadata
  inspection;
- **Implementation** requires assemblies that can supply implementation bodies;
  and
- **ReferenceAndImplementation** requires both views and explicit
  correspondence between them.

Population demand is also closed:

- **Library** names one exact requested assembly identity or one owner-issued
  platform-library identity; and
- **Population** requests the complete applicable family population and any
  source-owned support closure.

One-library demand does not authorize whole-pack indexing, whole-platform
realization, or population completeness work unless the selected source's
atomic realization unit requires it. When a source must realize a larger
coherent unit, the result says which unit was realized and why; the House does
not relabel that cost as a one-file operation.

Reference success does not satisfy implementation demand. A reference-only
source can complete API inspection while implementation-body or call-graph
requests remain visibly unavailable.

For complete paired populations, each source retains its independently authored
membership and provenance while PlatformHouse owns their correspondence. The
House pairs only equal complete managed assembly identities. It preserves every
reference member in reference-source order, attaches an exact matching
implementation when present, then appends unmatched implementation members in
implementation-source order. Unmatched references remain reference-only;
unmatched implementations use the implementation-only declaration-surface
rule. Equal simple names with different version, culture, or public-key token
remain distinct Libraries. Filename, path, manifest coordinate, and display
text do not establish correspondence.

## Source plans and contributions

### Host authorization is explicit

A source plan identifies each capability the host permits for the exact
operation and the policy for considering it. It may authorize:

- installed or package-backed target discovery;
- installed reference packs;
- installed implementation-platform realization;
- package-backed reference packs;
- package-backed implementation packs;
- a generated Browser platform catalog;
- embedded platform content;
- companion content projected by a separately owned downstream adapter; or
- another separately designed platform source.

The list is illustrative rather than an authority grant. Each source owner
defines its own request, evidence, failure, cache, and lifetime contract before
it can contribute.

Network-backed work remains explicit or capability-gated. The House cannot
turn an installed miss into a NuGet request, a Browser catalog miss into a
desktop probe, or a package-cache hit into current source authorization unless
the source plan explicitly permits that route.

### Source contribution contract

Every contribution retains:

- source-capability identity;
- the exact requested House target;
- the source-specific coordinate and exact target;
- supported view and population demand;
- source generation or freshness evidence;
- candidate identities or resource-free realized-source evidence;
- authoritative, partial, or unavailable completion; and
- typed rejection or failure when the operation did not succeed.

Those retained values and the contribution object itself establish
correspondence. A source adapter does not mint an additional House-local
source-evidence or source-to-target occurrence identity.

Live source content obligations remain separately owned beside the
contribution until PlatformHouse either leaves them with their source
integration or transfers them into atomic Library construction. They never
enter a contribution or House receipt.

Target-discovery results are separate from realization contributions. After
the House freezes one exact target, a realization source that discovers
another exact target may return it as discovery evidence only when its source
contract permits that result. It does not answer the settled exact request by
relabeling that evidence.

Paths, pack names, package IDs, framework aliases, file names, and Browser
labels are source coordinates or presentation. They never independently prove
House target correspondence.

### Settlement

The source plan states whether a source order is precedence, fallback,
aggregation, or facet-specific selection. The House does not infer that policy
from enumeration order.

Settlement obeys these rules:

- a candidate must retain exact House-target correspondence;
- reference and implementation contributions settle independently;
- two facets may be paired only when owner-issued evidence establishes their
  correspondence;
- a reference definition reaches an implementation-resolution start only
  through House-owned view correspondence;
- one source's absence does not authorize another source;
- an earlier success may stop later work only when the explicit policy proves
  that later contributions cannot change validity, ambiguity, or required
  completeness;
- a selected source failure is not hidden by a success-shaped empty result;
- a partial population cannot satisfy complete-population demand;
- a reference assembly does not prove an implementation body;
- equal names or versions do not merge different families, targets, sources,
  generations, or physical suppliers.

For ordered Reference-source settlement, `Precedence` and `Fallback` differ
only at source failure. Both may continue after an unavailable contribution.
`Precedence` makes a failed higher-priority capability terminal; `Fallback`
may retain that failure as `OutcomeRelevant` and select a later capability.
Rejected or incomplete evidence is terminal in either mode. After selection,
supplied later contributions are `Shadowed`.

`Aggregation` requires one contribution from every authorized capability. One
authoritative realization plus authoritative absence from every peer selects
that realization. Multiple authoritative realizations are ambiguous, while
missing or incomplete evidence cannot complete aggregation. A required source
failure remains failed, and rejected owner evidence remains rejected.

ASP.NET Core source realizations may retain a .NET runtime support closure.
The source owner establishes that closure and its version behavior. The House
preserves the focus target, support targets, members, and evidence rather than
inventing one merged family target. A support target may occupy a different
release band when the source owner selected it through its explicit
roll-forward policy.

## Workspace ecosystem realization

Workspace ecosystem registration describes target-independent relevance. It
does not select a platform target, authorize a source, acquire a library, or
invoke `PlatformHouse`. Runtime work begins only when a bounded operation
selects one registered population from one exact Workspace revision.

The Ecosystems-owned curated Workspace manifest projects these lower
contributions:

| Curated ecosystem | Platform contribution | Package-prefix contribution |
| --- | --- | --- |
| Platform | `Platform(DotNetRuntime)` | None |
| ASP.NET Core | `Platform(AspNetCore)` | `Microsoft.AspNetCore.` |
| Microsoft.Extensions | None | `Microsoft.Extensions.` |

The
[Workspace Ecosystem Registration Handoff](workspace-ecosystem-registration-handoff.md)
owns this manifest, complete curated construction, and each
application-to-lower-declaration correspondence.
`PlatformHouse` does not recognize the display labels `Platform`,
`ASP.NET Core`, or `Microsoft.Extensions`, and it does not reconstruct a
platform family from a package prefix.

When an operation selects registered ecosystems, its population planner
processes every request-relevant contribution independently:

1. it retains the owner-issued Workspace revision identity, ecosystem
   registration, and lower contribution;
2. for each `PlatformLibraryPopulationDeclaration`, it issues one platform
   target demand whose family is exactly the declaration's `PlatformFamily`;
3. it adds operation-owned target-framework and version policy, reference or
   implementation view, one-library or whole-population demand, source plan,
   and finite work;
4. it calls `PlatformHouse` with that typed request and retains the resulting
   exact `PlatformFamilyTarget`, realization, receipt, or non-success; and
5. it sends each package-prefix contribution through bounded package discovery
   and package processing rather than through `PlatformHouse`.

The House rejects a Workspace-origin request when the target demand's family
does not equal the retained platform population declaration. Successful target
settlement preserves this correspondence:

```text
Workspace revision identity
  -> ecosystem registration
  -> PlatformLibraryPopulationDeclaration(PlatformFamily)
  -> family-preserving target demand
  -> exact PlatformFamilyTarget
  -> PlatformHouse realization receipt
```

The operation-level association retains the ecosystem registration that
selected the lower declaration. The House request and receipt retain the exact
Workspace revision identity and `PlatformLibraryPopulationDeclaration`; they
do not retain the complete revision snapshot or depend on application ecosystem
identity or display metadata.

### Family mismatch example and continuation

Suppose one selected ASP.NET Core registration retains:

```text
Workspace revision identity: ws-rev-a7
ecosystem registration: ASP.NET Core
population declaration: Platform(AspNetCore)
```

The population planner incorrectly issues:

```text
target demand:
  family: DotNetRuntime
  framework: net11.0
  version policy: current Workspace platform band
```

`PlatformHouse` returns a typed rejection retaining the declared
`AspNetCore` family and requested `DotNetRuntime` family. It performs no target
discovery, source selection, acquisition, artifact-cache publication, Metadata
work, or Workspace replacement for that request. The House may retain the
typed rejection under its ordinary failure-cache contract.

After the rejection, the population planner:

1. associates it with the exact ASP.NET Core registration and platform
   contribution that produced the request;
2. does not retry with a rewritten family, infer a family from the ecosystem
   label, substitute the separately registered runtime population, or route
   the `Microsoft.AspNetCore.` package prefix through `PlatformHouse`;
3. may retain successful results from independently selected runtime or
   package-prefix contributions under the consuming operation's result
   contract; and
4. reports the selected ASP.NET Core platform population as rejected, so an
   ecosystem or graph result requiring that population cannot claim complete
   coverage.

The consuming operation decides whether its result algebra returns a rejected
operation or a partial result carrying this rejection. It cannot turn the
mismatch into absence or a success-shaped empty population. Because no source
was consulted, changing source authorization or adding a package is not the
remedy; the operation-planning correspondence must be corrected.

The registration does not choose the target framework or version. A Workspace
operation may provide an already-settled target context or an explicit
selection policy. Equal TFM or version text across `DotNetRuntime` and
`AspNetCore` never merges their family identities.

The curated Platform and ASP.NET Core registrations therefore produce
independent House requests when both populations are selected. An ASP.NET Core
source may additionally return an explicit .NET runtime support closure. That
closure does not replace the selected `DotNetRuntime` contribution or collapse
the two exact family targets. The population-composition and Workspace
admission owners may coalesce only owner-issued exact artifact correspondence,
while retaining both registration and family-target paths.

Call-graph focal length determines whether these registrations are selected:

- `Self` selects only the focal registration or containing library;
- `SelfAndRegisteredEcosystems` adds all ecosystem registrations in the exact
  Workspace revision; and
- `Everything` additionally admits every other Workspace population and
  already-admitted participant available under the operation.

The current three curated registrations participate in the latter two modes
when the caller chose curated construction and unless the user removed them. A
raw Workspace contributes none until explicitly registered. Selection still
grants no source authority: each platform and package contribution uses its
own explicit source policy and bounds. A missing, unauthorized, ambiguous,
failed, or incomplete contribution remains visible; the planner cannot
silently omit it and claim a complete ecosystem or graph population.

## Typed ingress and shared Library convergence

Package, platform, and library are distinct entry domains. Textual equality
between their coordinates does not transfer identity or authority across those
domains.

Package and platform are peer container/source domains with analogous
opportunity: each keeps its own typed identity, version settlement,
acquisition, provenance, container inspection, and Library realization.
Neither is modeled as a wrapper around the other or as a specialized form of
the shared Library owner.

The owning command or asset-reference parser classifies ingress before any
House runs:

- a CLI package selector, `.nuspec` dependency, project
  `PackageReference`, or restored package edge enters `PackageHouse` as a typed
  package coordinate;
- a CLI platform selector or project `FrameworkReference` enters
  `PlatformHouse` as a typed target demand; the request may already contain an
  exact target or may authorize explicit version selection;
- a file path, project `Reference` or `HintPath`, built project output, or
  already-acquired assembly enters shared Library inspection as a direct
  library;
- a Metadata `AssemblyRef` enters the Assembly Reference Resolution Ladder and
  reaches `PlatformHouse` only if the ladder authorizes its platform rung; and
- an upstream `PlatformDelegation` authorizes orchestration to issue an
  ordinary `PlatformHouse` request without changing the package coordinate
  that produced it into an assembly or library identity.

A bare CLI token such as `System.Text.Json` is therefore not intrinsically an
`AssemblyRef` or `PackageRef`. The command grammar and discovery result issue
the typed request before source processing begins.

The three asset flows are:

```text
PackageRef
  -> PackageHouse
  -> pruning service when comparable
     -> Subsumed: emit platform delegation and exit package acquisition
     -> otherwise:
          resolve package version when required
          re-evaluate pruning for the exact coordinate when required
          acquire -> select -> shared Library realization

Platform request or orchestration-issued delegation
  -> PlatformHouse
  -> target and version resolution
  -> acquire authorized realization
  -> select reference and/or implementation view
  -> construct shared Library owner and reference

Direct library
  -> separately owned realization under #6621 slice 6
  -> shared Library contract
```

The analogous CLI subject scenarios are:

| Subject | Entry domain | Native inspection | Library-focused handoff |
| --- | --- | --- | --- |
| `library` | Direct library coordinate | Library-shaped from entry | The separately owned #6621 slice 6 adopts the shared Library contract without entering PlatformHouse. |
| `package` | Typed package coordinate through PackageHouse | Package-shaped metadata, dependencies, and assets | Each explicitly selected library becomes a shared Library realization with package provenance. |
| `platform` | Typed target demand through PlatformHouse | Platform-shaped target, population, and view evidence | Each explicitly selected platform library becomes a shared Library realization with Platform provenance. |

These paths converge on the shared Library owner, not on another House or a
consumer-specific ready wrapper. Each realized Library separates:

- one caller-owned `LibraryContentOwner`, which is the only live content
  authority;
- one resource-free `LibraryReference`;
- exact assembly `LibraryContentReference` values carrying `ApiAssembly`,
  `ImplementationAssembly`, or both roles;
- physical Artifact and assembly identity for every retained view;
- direct, package, or Platform origin and source evidence;
- exact target context when one exists;
- reference-to-implementation correspondence when both views exist; and
- visible acquisition, construction, rejection, ambiguity, failure, or
  incompleteness evidence.

The shared contract does not imply one Library per package or one Library per
platform. A package or platform may produce zero, one, or many selected
Libraries and retains its own non-Library assets and container-shaped
inspection. Library construction occurs only when a consumer explicitly
requests a Library-focused result.

`PlatformHouse` owns the platform half of this convergence: it turns one
settled platform Library demand into the shared Library shape without
discarding platform target, source, or view correspondence. For every selected
Library, the House validates that the resource-free source contribution and
live source content obligations describe the same exact target, source, and
views. The source adapter registers content with
`PlatformLibraryArtifactProvenance`, which retains the exact realization
contribution beside source-specific resource-free provenance. Content selection
recovers the contribution from that registration rather than accepting an
independent caller pairing. It accepts only Metadata's owner-issued
`ArtifactAssemblyProjection` for the exact Artifact generation and identity and
derives the managed assembly identity from that projection. The resulting
`LibraryReference` retains the exact Platform arm of
`ExactLibrarySourceCoordinate`; an opaque House operation identity,
caller-supplied assembly display name, or projection for another Artifact
cannot replace it.

View demand closes the required Library roles before construction:

- **Reference** maps the selected reference content to `ApiAssembly`.
- **ReferenceAndImplementation** maps the selected reference content to
  `ApiAssembly`, the selected runtime content to `ImplementationAssembly`, and
  preserves their Platform-owned correspondence. One physical content may
  carry both roles only when that correspondence explicitly establishes both.
- **Implementation** maps the selected runtime content to
  `ImplementationAssembly` and requires Platform-owned evidence selecting the
  same content as that implementation-only Library's declaration surface
  before also assigning `ApiAssembly`. Without that evidence, PlatformHouse
  returns typed `Unavailable` evidence for the required API role before
  Library construction.

The House never infers both roles merely because one content item is
available. After closing the roles, it invokes the Library owner's atomic
construction once.

Before Library construction accepts those obligations, their source integration
retains ownership. After acceptance, `LibraryContentOwner` owns them. The House
does not wrap a source handle, retain an obligation in a receipt, or create a
Platform-named lease. Library construction rejection follows the Library
owner's transfer contract; PlatformHouse preserves the rejection and its exact
source and target correspondence.

The owning realization result returns each `LibraryContentOwner` separately
from the resource-free House value and receipt. That value and receipt retain
the exact `LibraryReference`, content references, roles, Platform provenance,
source evidence, target, and view correspondence. Shared Library inspection
owns assembly-level metadata, API, dependency, source, analysis, and
decompilation behavior after that handoff.

Any PlatformHouse Metadata work that reads a newly constructed Library first
obtains a fresh `LibraryOperationLease` from its owner. The House reads exact
content references only through synchronous snapshots, materializes detached
or independently owned values before awaiting, and settles every internal
lease before completing the operation. A caller never receives an internal
lease. The Library owner design, not PlatformHouse, defines construction,
issuance, borrowing, and retirement mechanics.

### Platform asset handling map

The physical container does not decide the product identity. Frameworks and
targeting packs use NuGet package files as a distribution mechanism, but a
Platform operation treats their package IDs and layouts as source coordinates,
not as Package participants or assembly identities.

| Asset | Platform treatment | Other explicit treatment |
| --- | --- | --- |
| Installed `Microsoft.NETCore.App` or `Microsoft.AspNetCore.App` shared framework | An implementation supplier for the selected platform target and family. There may be no corresponding package artifact on the machine. | An assembly path obtained independently enters direct Library inspection. |
| `Microsoft.NETCore.App.Ref` or `Microsoft.AspNetCore.App.Ref` targeting pack | A reference-view source. `PlatformHouse` selects the authorized DLLs and constructs shared Library realizations without publishing the acquired pack as a Package participant. | An exact package coordinate or local `.nupkg` may be inspected separately as a package. That package inspection does not establish a Platform target or transfer package identity to its DLLs. |
| Runtime implementation pack such as `Microsoft.NETCore.App.Runtime.<rid>` | An implementation-view source. Public implementations, facades, and private implementation Libraries retain their Platform roles and provenance after shared Library construction. | Explicit package inspection may describe the distribution container; an extracted DLL enters direct Library inspection. Neither route implies Platform membership. |
| `NETStandard.Library.Ref` or `NETStandard.Library` reference assets | A .NET Standard reference-contract contribution used during transparent forwarding; never a platform family or implementation population. | Explicit package inspection may describe the package. An extracted `netstandard.dll` is an ordinary direct library whose forwarding evidence remains visible. |
| Any manually downloaded or extracted DLL | No Platform inference from its path, parent pack name, file name, or assembly name. | Direct Library inspection reads that one file with direct provenance. A caller must issue a separately typed Platform request to obtain platform target or view correspondence. |

A Platform-backed Workspace therefore contains the Platform participant and
the libraries selected from its sources, not `Microsoft.NETCore.App.Ref`,
`Microsoft.AspNetCore.App.Ref`, runtime-pack, or .NET Standard source packages
as ordinary package participants. Package discovery may also omit distribution
packages that are not listed for browsing. This is not a global prohibition on
package inspection: an exact package request or a local `.nupkg` request uses
the generic Package path and inspects the container under package semantics.
No result from that path can be reused as proof of Platform identity,
selection, or compatibility.

Forwarder-only facades require the same distinction. For platform population,
type listing, and member traversal, a facade with only `ExportedType`
forwarders contributes no independent type definitions: the
[structured type-forwarding owner](type-forwarding-resolution.md) follows its
metadata evidence to the terminal definition authorized by the selected view.
A reference-view terminal does not prove an implementation supplier;
House-owned reference-to-implementation correspondence and a second Metadata
resolution establish that bridge when implementation evidence is required.
The facade does not become an additional implementation library merely because
it was present in a runtime or reference pack. The artifact itself has not
disappeared, however. Direct Library inspection may still select the facade,
report that it is a facade, and show its forwarding evidence. "Collapse" means
zero independent definition contribution after forwarding, not deletion or an
inability to inspect the file.

### Workspace admission and platform skew

Workspace admission and shared Library construction do not require compatibility
with the Workspace platform. A package-selected or direct library may be
retained when its target is newer than the loaded platform. Its own metadata,
API, IL, source, decompilation, comparison, and other same-participant
inspection remain available. Admission records membership and provenance; it
does not prove that every cross-participant traversal can succeed.

`PlatformHouse` is not called merely to approve that admission or to acquire a
complete matching platform. It participates only when a bounded operation
issues a platform target demand or the assembly-reference ladder authorizes its
applicable-platform rung.

[Platform composition and overlays](platform-composition-and-overlays.md#overlay-compatibility-is-a-property-of-the-pair-and-request)
owns the compatibility rule. At a platform traversal, ordinary binding must
select an eligible assembly candidate and Metadata must resolve the exact
requested member identity and signature. A same-named member, another overload,
display-text match, or shape-compatible signature is not a substitute. A
breaking signature change is therefore an unavailable member; known
participant/platform skew turns that miss into the owner-defined attributed
compatibility failure. Other participants and unrelated operations remain
usable.

Even a successful exact bind uses evidence from the loaded platform supplier.
Under an owner-classified unsupported downgrade, its nullable annotations,
attributes, documentation, source, bodies, and source-level `unsafe` placement
may make platform-derived documentation, source, decompilation, or analysis
incorrect for that unsupported composition even though the member identity
binds. The House preserves exact target, supplier provenance, and the
compatibility result so the compatibility owner and hosts can retain and
present that downgrade context; it does not define the warning or its
presentation. A supported upward-compatible pairing does not become a warning
merely because its platform target or descriptive evidence differs from the
library's original target.

## Assembly-reference processing

The
[Assembly Reference Resolution Ladder](assembly-reference-resolution-ladder.md)
owns rung order, route-set completeness, Workspace replacement, and the final
ladder outcome. `PlatformHouse` owns only the applicable-platform contribution.

The ladder calls the House after the referencing-context rung returns
`NoNameOwner` and after its route owner supplies:

- the exact `AssemblyBindingRequest`;
- exact origin and resolver lineage;
- the applicable `PlatformFamilyTarget`;
- typed authorization to evaluate the platform rung;
- the source-plan and operation identities; and
- the remaining work ledger.

Because the ladder operates inside an already-settled Workspace context, this
request carries an exact target demand. The House:

1. validates the exact platform target and route authorization;
2. realizes the required platform library or source-owned atomic unit;
3. forms the platform-authorized Metadata binding policy over the selected
   candidates; and
4. returns the unchanged Metadata binding decision with House correspondence.

The House does not call the package rung, search NuGet by assembly name, or
advance the ladder. A completed platform `NoNameOwner` lets the ladder proceed
under its own contract. `NameOwnedNoMatch`, ambiguity, unavailable evidence,
rejection, or incomplete work retain their existing terminal meaning.

### Exact source-neutral binding execution

The source-neutral binding kernel starts after source policy selects one
authorized immutable reference snapshot for an explicit Metadata
assembly-reference target. It accepts an exact-target
`ResolveAssemblyReference` request with global binding origin, Reference view,
one authoritative reference realization for that same request and identity,
and finite work. Source discovery, target selection, and source-relative
ladder continuation remain outside this boundary.

PlatformHouse owns the snapshot from acceptance onward. A completed operation
has these ordered obligations:

1. publish the snapshot into one bounded Artifact generation and preserve the
   source-issued contribution as its provenance;
2. construct one reference Library and accept its Artifact content authority;
3. issue a fresh Library operation lease and supply owner-attested bytes to
   Metadata only during one synchronous borrow;
4. obtain a detached `AssemblyBindingDecision.Resolved` whose selected
   supplier carries the exact Artifact acquisition registration;
5. settle the operation lease before asynchronously retiring the Library
   owner; and
6. retire the adjacent Artifact session before publishing the completed House
   result.

The returned value is the unchanged detached Metadata decision. Its House
completion retains the exact request, route-prerequisite identity, target,
selected source contribution, physical supplier correspondence, and consumed
work. It transfers no Library owner, Artifact session, operation or content
lease, descriptor, opener, stream, reader, callback, or disposal obligation.

The result remains provisional until cleanup finishes. Invalid request,
source, target, identity, authority, or retained correspondence produces
`Rejected`; insufficient work produces `Incomplete`; Artifact publication,
Metadata, Library construction or borrow, lease settlement, Library
retirement, child release, or Artifact retirement failure produces `Failed`.
When caller cancellation and cleanup failure coexist, `Failed` is primary and
retains cancellation as causal evidence. Otherwise cancellation is rethrown
only after every accepted authority has settled or retired.

`PlatformAssemblyReferenceResolverTests.ResolveAsync_DetachesDecisionAndRetiresAuthorities`
gates the real-framework-assembly completion path and exact physical supplier
correspondence in Release.
`PlatformAssemblyReferenceResolverTests.ResolveAsync_RejectsNonGlobalInputBeforeSourceAccess`
and
`PlatformAssemblyReferenceResolverTests.ResolveAsync_RejectsMismatchedRouteBeforeSourceAccess`
gate the focused input and exact route-correspondence boundaries before source
access in Release.
`PlatformAssemblyReferenceResolverTests.PublicResultClosure_IsResourceFree`
gates the completed public type closure in Release.
`PlatformAssemblyReferenceResolverTests.ResolveAsync_ReportsMetadataIdentityFailure`
gates visible Metadata identity failure in Release.
`PlatformAssemblyReferenceResolverTests.ResolveAsync_ObservesCancellationAfterCleanup`
gates terminal cancellation after owned cleanup in Release.
`PlatformAssemblyReferenceResolverTests.ResolveAsync_CleanupFailureRemainsPrimaryOverCancellation`
gates Artifact publication cleanup failure as primary over cancellation in
Release.

### Multi-source assembly-reference policy execution

The multi-source resolver accepts a finite set of source-prepared attempts for
the exact Reference selection captured by the request. An attempt is ephemeral
execution input, not House evidence. A successful attempt carries an
owner-issued candidate identity and the existing materialization item,
including its immutable opener. A terminal attempt carries only the exact
resource-free source contribution and, for rejection, its mapped House
classification. Attempts, openers, source diagnostics, and live source values
never enter an outcome or receipt.

The resolver indexes attempts by capability identity, rejects duplicate
capabilities or candidate identities, and traverses the source plan's
capability order rather than caller enumeration order. Missing required
attempts remain incomplete. An in-budget ledger records at least one source
operation for every supplied attempt, while assembly and byte work apply to
successful materializations. Exhausted work with an under-reported ledger
closes as `Incomplete` without retaining source settlements that ledger cannot
support. The policy modes execute as follows:

- `Precedence` selects the first authoritative realization after unavailable
  predecessors. A higher-priority failure is terminal.
- `Fallback` also selects the first authoritative realization, but may
  continue after an unavailable or failed predecessor. Every predecessor that
  permits continuation remains `OutcomeRelevant`.
- `Aggregation` requires every capability. Exactly one realization plus
  authoritative absence from every peer selects; multiple realizations return
  owner-identity ambiguity without opening either item; zero realizations
  returns unavailable only after every supplied capability settles without a
  stronger terminal result.

Rejected or incomplete ordered evidence prevents later selection. Supplied
evidence after an ordered terminal or selected attempt is `Shadowed`.
Aggregation evidence is `OutcomeRelevant`; a selected aggregate realization
is `Selected`. Once policy selects a realization, the shared binding kernel
owns publication, Metadata projection, Library borrowing, cleanup, and final
receipt construction unchanged.

Terminal precedence is:

1. observe cancellation before and throughout attempt acceptance, including
   once after enumeration completes;
2. validate the request before enumerating attempts, using `Incomplete`
   without settlement when work is already exhausted and `Rejected` otherwise;
3. validate attempt correspondence under the same exhausted-work precedence
   without retaining foreign evidence;
4. select the source-policy decision;
5. reject an in-budget consumed-work ledger that cannot cover the supplied
   attempts;
6. preserve a corresponding policy-terminal source failure as
   `Failed(Source)` when the ledger covers the supplied attempts;
7. otherwise make exhausted work `Incomplete`, retaining policy evidence only
   when the ledger covers it;
8. make required missing/incomplete evidence `Incomplete`; and
9. apply the remaining selection, ambiguity, rejection, or unavailability
   decision.

No terminal, shadowed, or ambiguous path opens source content.

`PlatformAssemblyReferenceResolverTests.ResolveAsync_FallbackUsesPlanOrderAndRetainsPriorAbsence`
gates plan-order execution independent of input enumeration.
`ResolveAsync_PrecedenceFailureStopsBeforeLaterSuccess`,
`ResolveAsync_FallbackSupersedesPriorFailure`, and
`ResolveAsync_FallbackTerminalPreventsLaterSelection` gate the ordered-mode
distinctions.
`ResolveAsync_AggregationSelectsAfterAuthoritativePeerAbsence`,
`ResolveAsync_AggregationAmbiguityOpensNoSourceContent`, and
`ResolveAsync_AggregationRetainsPeerTerminalOutcome` gate aggregate
settlement. `ResolveAsync_AggregationFailurePrecedesPeerTerminal` and
`ResolveAsync_AggregationFailurePrecedesMissingCapability` gate source-failure
precedence over weaker aggregate terminals.
`ResolveAsync_MissingCapabilityIsIncompleteWithoutOpeningSuccess`,
`ResolveAsync_RejectsDuplicateCapabilityAttemptsWithoutOpening`, and
`ResolveAsync_RejectsDuplicateCandidateIdentityWithoutOpening` gate complete
and unique attempt correspondence.
`ResolveAsync_RejectsForeignAttemptBeforeSourceAccess` and
`ResolveAsync_BudgetExhaustionPrecedesForeignAttempt` gate foreign-evidence
and finite-work precedence.
`ResolveAsync_RejectsUnderreportedTerminalSourceWork` gates consumed-work
coverage for failure and shadowed attempts.
`ResolveAsync_InvalidRequestDoesNotEnumerateAttempts` gates request rejection
before attempt production.
`ResolveAsync_ObservesCancellationDuringAttemptEnumeration` gates typed
cancellation from a lazy attempt producer before policy settlement.
`ResolveAsync_ExhaustedInvalidRequestIsIncompleteWithoutEnumeration` and
`ResolveAsync_ExhaustedUnderreportedWorkIsIncompleteWithoutSettlement` gate
receipt-compatible exhausted-work closure without unsupported settlement.
`ResolveAsync_ExhaustedUnderreportedFailureIsIncompleteWithoutSettlement`
gates the same closure when policy also observes source failure.

### Installed successful-result binding adoption

The installed adapter admits the exact one-assembly Reference population
implied by an applicable `ResolveAssemblyReference` operation. Its authoritative
contribution preserves the exact House request, target, installed capability,
source generation, source coordinate identity, and one-assembly population.
The paired owner-issued value carries the installed generation, reference-pack
coordinate, file name, assembly identity, and immutable bytes. The installed
execution adapter binds those facts as Artifact provenance, converts the
snapshot to the common source-neutral materialization item, and delegates the
operation unchanged to `PlatformHouseAssemblyReferenceResolver`.

This adapter does not duplicate Artifact publication, Library ownership,
Metadata projection, terminal precedence, cleanup, or receipt construction.
The returned decision therefore remains detached terminal data: the shared
executor settles the exact installed contribution once and retires every
temporary Library and Artifact authority before publication. Installed
source-specific invocation orchestration, source-relative lineage, and ladder
or host composition remain later owner-adoption slices.

`InstalledPlatformHouseAdapterTests.RealizeReference_BindingProducesExactAssemblyContribution`
gates the authoritative one-assembly installed contribution in Release.
`InstalledPlatformAssemblyReferenceResolverTests.ResolveAsync_PreservesInstalledProvenanceAndSettlesContribution`
gates real installed reference-pack provenance and exact source settlement in
Release.
`InstalledPlatformAssemblyReferenceResolverTests.ResolveAsync_RejectsSuccessfulResultForDifferentRequest`
gates exact successful-result correspondence in Release. The source-neutral
before-source-access validation gates above own the delegated ordering claim.

### Package-backed successful-result binding adoption

The package-backed adapter admits the exact one-assembly Reference population
implied by an applicable `ResolveAssemblyReference` operation. Its authoritative
contribution preserves the exact House request, target, package capability,
source generation, source coordinate identity, and one-assembly population.
The paired adapter-issued value carries the package source generation,
reference-pack coordinate, member path, assembly identity, candidate,
configured authority, producer identity, content generation, payload origin,
and immutable bytes.

The package execution adapter binds those facts as Artifact provenance,
converts the snapshot to the common source-neutral materialization item, and
delegates the operation unchanged to
`PlatformHouseAssemblyReferenceResolver`. Package Source operation settlement
completes before this bridge consumes the successful result; no Package Source
lease, payload, or store lifetime enters the Artifact, temporary Library, or
House result.

This adapter does not duplicate Artifact publication, Library ownership,
Metadata projection, terminal precedence, cleanup, or receipt construction.
The returned decision therefore remains detached terminal data: the shared
executor settles the exact package contribution once and retires every
temporary Library and Artifact authority before publication. Package
source-specific invocation orchestration, source-relative lineage, and ladder
or host composition remain later owner-adoption slices.

`PackagePlatformAssemblyReferenceResolverTests.AdapterProducesExactBindingContributionAfterPackageSettlement`
gates the authoritative one-assembly package contribution after Package Source
operation settlement in Release.
`PackagePlatformAssemblyReferenceResolverTests.ResolveAsync_PreservesPackageProvenanceAndSettlesContribution`
gates package-backed reference provenance and exact source settlement in
Release.
`PackagePlatformAssemblyReferenceResolverTests.ResolveAsync_RejectsSuccessfulResultForDifferentRequest`
gates exact successful-result correspondence in Release. The source-neutral
before-source-access validation gates above own the delegated ordering claim.

### Package reference-source terminal adoption

For one exact global assembly-reference operation whose Reference source plan
authorizes only the package capability, an adapter-issued package
`NotSucceeded` result closes as one source-neutral House terminal outcome. The
projector accepts only the exact request snapshot, Reference facet, authorized
capability, and settled target. It performs no package discovery, acquisition,
content opening, Artifact publication, Library construction, or Metadata work.

An accepted `Unavailable`, `Rejected`, `Incomplete`, or `Failed` contribution
appears exactly once in the House receipt as `OutcomeRelevant`. The House
terminal arm preserves the contribution kind; source failure records
`PlatformHouseFailureKind.Source`. Package rejection diagnostics classify
invalid selection as `InvalidRequest`, invalid coordinates as
`InvalidTargetCorrespondence`, and rejected source-owned content or evidence as
`InvalidOwnerResult`. The source-specific diagnostic remains on the
caller-owned package adapter result beside the House outcome rather than being
copied into the host-neutral receipt.

After exact request validation, a corresponding source failure remains
`Failed`. Otherwise, budget exhaustion produces `Incomplete`; a corresponding
terminal contribution is retained, while a foreign or otherwise unusable one
is not settled. Within budget, foreign, unauthorized, mismatched-facet,
mismatched-target, successful, or otherwise unusable contributions produce
`Rejected(InvalidOwnerResult)` with no source settlement. Cancellation remains
`OperationCanceledException`.

The direct projection remains a single-source terminal operation. The package
bridge also prepares the same terminal result as a common ephemeral attempt
for the source-neutral policy executor while retaining its diagnostic on the
caller-owned package result. The bridge itself does not select sources. Target
discovery, source-relative lineage, ladder composition, and host adoption
remain later work.

`PackagePlatformAssemblyReferenceResolverTests.ResolveAsync_ProjectsPackageSourceTerminalOutcomes`
gates the four package terminal arms, exact outcome-relevant settlement, source
failure stage, retained package diagnostic, and absence of further source work
in Release.
`PackagePlatformAssemblyReferenceResolverTests.ResolveAsync_RejectsForeignPackageSourceTerminal`
gates foreign unavailable and incomplete owner evidence before settlement in
Release.
`PackagePlatformAssemblyReferenceResolverTests.ResolveAsync_BudgetExhaustionPrecedesForeignPackageSourceTerminal`
gates typed incomplete precedence without settling that foreign evidence.

### Installed reference-source terminal adoption

For the corresponding one-source installed operation, the installed
assembly-reference bridge accepts the complete adapter result family.
Successful values continue through the existing installed materialization
path. An adapter-issued `NotSucceeded` value delegates its resource-free
contribution to the source-neutral terminal projector above without installed
probing, file opening, Artifact publication, Library construction, or Metadata
work.

The projector applies the same exact request, target, Reference-facet,
capability, and single-source-plan correspondence and the same failure, budget,
invalid-evidence, and cancellation precedence. An accepted installed
`Unavailable`, `Rejected`, `Incomplete`, or `Failed` contribution appears once
as `OutcomeRelevant`; foreign or otherwise unusable evidence is not settled.
Installed `InvalidRequest` diagnostics classify rejection as `InvalidRequest`,
`InvalidCoordinate` as `InvalidTargetCorrespondence`, and rejected
source-owned layout, member, assembly, or other evidence as
`InvalidOwnerResult`. The installed diagnostic remains on the caller-owned
adapter result beside the host-neutral House outcome.

This closes the installed single-source terminal algebra. The installed bridge
also prepares success or terminal results as common ephemeral attempts without
moving its diagnostic into House evidence. Target discovery, source-specific
invocation orchestration, source-relative lineage, ladder composition, and
host adoption remain later work.

`InstalledPlatformAssemblyReferenceResolverTests.ResolveAsync_ProjectsInstalledSourceTerminalOutcomes`
gates the four installed terminal arms, exact outcome-relevant settlement,
source failure stage, retained installed diagnostic, and malformed installed
reference content in Release.
`InstalledPlatformAssemblyReferenceResolverTests.ResolveAsync_ClassifiesInstalledRejection`
gates installed rejection classification.
`InstalledPlatformAssemblyReferenceResolverTests.ResolveAsync_RejectsForeignInstalledSourceTerminal`
gates foreign installed evidence before settlement.

## Typed platform delegation from PackageHouse

Package-reference processing remains package-shaped under the
[PackageHouse](package-house.md) contract. The package owner retains
the original typed `PackageRef`, target context, resolved or unresolved package
version evidence, and the source from which that evidence was obtained.

The
[Platform package supply policy](platform-package-supply-policy.md)
is a focused pure service used by that package owner. When it returns
`Subsumed`, the package owner may stop before package payload acquisition and
issue a typed `PlatformDelegation`. `NotSubsumed`, `NotComparable`, unavailable,
or incomplete pruning evidence does not authorize that delegation. A ranged or
unresolved package version may require package metadata resolution before the
policy can return a final comparable result, but that does not require package
payload acquisition.

The package owner retains:

- the original package coordinate and version evidence;
- the exact target used by the pruning service;
- the complete pruning result and supplying family/version evidence; and
- the fact that package payload acquisition was skipped.

The orchestration owner consumes the delegation and issues an ordinary
platform demand with one exact `PlatformFamilyTarget`. `PlatformHouse` neither
receives nor re-evaluates the raw `PackageRef`, and its result receipt does not
absorb the package decision receipt. The orchestration owner preserves the
association between those two receipts when the end-to-end operation needs to
explain the delegation.

`PlatformSupply` deliberately contains no platform-library identity. A package
ID such as `System.Text.Json` cannot establish an assembly identity with the
same spelling. A delegated population request may realize a complete platform
population. A delegated one-library request additionally requires an
independently typed platform-library or assembly demand from the owning
scenario.

## Transparent .NET Standard processing

.NET Standard is a reference contract, not a `PlatformFamily`,
`PlatformFamilyTarget`, registration population, or implementation population.
It therefore never appears as the House target.

When a platform operation begins from a .NET Standard facade:

1. the source contribution retains the exact .NET Standard reference-contract
   coordinate separately from the House target;
2. the House realizes or selects the exact runtime reference or implementation
   candidates authorized by the operation;
3. the House supplies those candidates through an explicit Metadata binding
   policy;
4. Metadata follows any exact `ExportedType` and `AssemblyRef` evidence and
   returns its reference-view outcome, which the House projects without
   changing its terminal arm or forwarding evidence;
5. when implementation demand remains, the House requires an explicit
   reference-to-implementation view correspondence for the resolved contract
   definition and starts a second Metadata resolution in the authorized
   implementation context; and
6. the House settlement retains the starting facade, both Metadata outcomes,
   every real forwarding hop, the separate view-correspondence transition, the
   terminal physical supplier when resolved, and the runtime target and source
   evidence.

The House never parses a forwarder itself, guesses a sibling from a file name,
or treats the facade's contract version as the runtime target version.
Metadata remains unaware of platform-family policy; it receives candidates and
binding decisions through its generic contract.

Metadata may terminate the reference-view request at a `TypeDef`. That
terminal definition remains correct and unchanged. It does not itself prove
the implementation supplier. The House-owned view correspondence bridges that
reference result to one implementation-resolution start and is recorded
outside the forwarding-hop sequence.

View correspondence is valid only when:

- both sides retain exact source and target evidence;
- a source owner explicitly pairs the reference and implementation assembly,
  or a separately owned compatibility projection explicitly maps the external
  contract definition to one runtime resolution start;
- the same structured `MetadataTypeDefinitionName` is used for the
  implementation request;
- the implementation candidate is authorized by the captured source plan; and
- no file-name, path, namespace, assembly-prefix, or first-match inference
  supplies the relation.

A correspondence may connect a normal platform reference assembly to its
implementation assembly or connect a transparent .NET Standard contract
definition to a runtime compatibility facade or implementation candidate. If
the required correspondence is unavailable or ambiguous, implementation
demand remains unavailable or ambiguous even when a same-named type exists
somewhere in the runtime population.

Reference-only success is sufficient only for reference demand. An operation
requiring implementation bodies succeeds only when Metadata reaches a terminal
definition in an implementation contribution. A forwarding chain ending in an
unavailable, ambiguous, rejected, or unbound implementation candidate keeps
that outcome. It is not reported as type absence or an empty body population.

The same Metadata engine remains available to package, project, and local
domains under their own binding policies. The encapsulation rule is narrower:
only `PlatformHouse` composes that generic engine into product platform
reference processing.

## Type catalogs are derived facets

A platform type catalog is a target-bound index over selected reference or
implementation candidates. It may accelerate one-library discovery or seed a
structured Metadata request, but it does not own type resolution.

The House may retain a catalog facet when:

- every indexed declaration retains its exact assembly candidate;
- definition and forwarding declarations remain distinct;
- the catalog's target, source generation, view, and completeness are visible;
- selection still passes through Metadata identity and binding contracts; and
- a catalog miss is not strengthened into type absence when indexing was
  partial or unavailable.

The current `PlatformTypeCatalog` is therefore migration evidence, not the
future public API. Its useful indexing behavior moves behind the House; its
source-selection heuristics do not become identity or Metadata policy.

Stage 7b derives one complete reference catalog from one completed population
and its still-active ordered Library owners. The derivation:

- consumes the population value and receipt issued by the same completed House
  operation rather than rediscovering a target, source, directory, or path;
- issues one operation lease per exact population member and consumes
  LibraryMetadata's detached declaration correspondence for that member's API
  content;
- retains the exact population member, API-content reference, module-version
  identity, structured Metadata declaration, and declaration kind for every
  public discovery entry, including module exports;
- enforces finite member-inspection, population-assembly, aggregate-byte,
  retained-entry, and duration bounds before publishing;
- publishes only after every population member succeeds and all bounds remain
  satisfied; and
- closes every operation lease before returning while leaving population-owner
  retirement with the caller.

The completed catalog retains only the resource-free population value, its
exact population receipt, consumed derivation work, and detached entries. The
receipt keeps the original target demand, exact selected target, source
generations and settlements, Reference view, and authoritative
complete-population evidence visible. Exact structured lookup returns every
candidate for one `MetadataTypeDefinitionName`, or `Missing` only from that
completed catalog.

Member inspection rejection or failure remains typed and names the exact
population member. Member or aggregate bound exhaustion and deadline
exhaustion return `Incomplete`; no prefix catalog or lookup result escapes.
Every completed or terminal derivation outcome retains the same resource-free
population value and receipt, so non-success cannot lose its target, source,
view, or completeness context.
Cancellation is observed after the active member lease closes and remains
cancellation. If a member owner is already retiring or released, derivation is
rejected without retiring any neighboring owner.

This facet does not parse user text, prefer definitions over forwarders,
collapse duplicate names, choose among candidates, or perform Metadata
binding. Those policies remain downstream of the complete structured index.

## Documentation handoff boundary

PlatformHouse no longer settles documentation. It supplies only the platform
facts that a separately owned documentation adapter may bind into a
[DocumentationHouse](documentation-house.md) contribution:

- the exact platform target;
- Metadata's terminal `ResolvedTypeDefinition` reference declaration supplier;
- Metadata forwarding evidence when a facade led to that terminal supplier;
- the exact implementation supplier and view correspondence when authored
  source is requested;
- the exact shared `LibraryReference` and request-selected API assembly content;
- the exact implementation content reference when authored source is
  requested; and
- resource-free platform, Artifact-registration, and provenance evidence.

The platform documentation adapter, not PlatformHouse, binds an exact compiled
XML `LibraryContentReference` and its owner-issued companion correspondence to
that terminal supplier evidence. It carries no live content authority. A
separate source integration adapter may bind a pre-authorized deferred
SourceHouse `AuthoredOnly` provider to the exact implementation supplier and
view correspondence. Live operation authority and downstream settlement follow
the separately owned Library, DocumentationHouse, and SourceHouse contracts;
PlatformHouse and its documentation adapter neither issue, retain, nor settle
that authority.
DocumentationHouse owns channel demand, compiled-XML and authored-source
attempts, field provenance, conflict preservation, and terminal documentation
outcomes.

One-Library realization accepts an explicit
`CompiledXmlDocumentation` content demand only with a reference view. The
authorized installed or package-backed reference source performs exact
same-basename companion acquisition under the House work budget. The shared
Platform Artifact materializer publishes any returned XML bytes beside the
reference assembly and assigns the closed `CompiledXmlDocumentation` role
associated with the API assembly. This is Library construction, not
documentation settlement: PlatformHouse does not parse XML, select a
documentation subject, or create a DocumentationHouse outcome. Omitting the
content demand performs no companion acquisition and cannot prove absence.

The former PlatformHouse subject-level documentation contracts are removed.
Product consumers compose the Library realization receipt through the separate
platform documentation adapter instead.

## Result and receipt contract

Each operation returns an operation-specific value inside one common House
outcome envelope:

- **Completed** — the requested operation settled, with its typed value and
  settlement receipt;
- **Unavailable** — an authorized target-selection capability, source, exact
  target, required view, candidate, inventory, or implementation supplier
  could not be obtained;
- **Ambiguous** — owner evidence permits several active candidates without an
  authorized precedence;
- **Rejected** — the request, source plan, target correspondence, candidate,
  owner result, budget, or retained evidence is invalid;
- **Failed** — required source or Metadata work cannot settle because of a
  fault, or Library construction, borrowing, lease settlement, retirement, or
  child release fails; and
- **Incomplete** — bounded work, partial source evidence, route association,
  catalog coverage, or generation replacement prevents a conclusive result.

Cancellation remains `OperationCanceledException`.

An explicitly superseded fallback-source failure may remain resource-free
evidence in a later `Completed`, `Unavailable`, or `Incomplete` outcome; its
presence alone does not force `Failed`. Cleanup failure is different:
`OperationCanceledException` propagates only after every internal Library
lease settles and every non-transferred owner retires successfully. If that
cleanup fails during cancellation, `Failed` takes terminal precedence and
retains cancellation as causal evidence.

`Completed` does not mean every possible facet succeeded. It means every facet
required by the exact operation settled. For example:

- a reference-only `Realize` operation may complete without implementation
  evidence;
- a one-library `Realize` operation may complete with one caller-owned
  `LibraryContentOwner` beside a resource-free `LibraryReference` retaining its
  Platform provenance;
- an assembly-reference operation may complete with Metadata
  `NoNameOwner`; and
- a type-resolution operation may complete with Metadata `NotFound` only when
  the selected readable image authoritatively returned that result.

The settlement receipt binds:

- the exact request, target demand, and settled House target when available;
- target-selection policy, candidates, and owner-issued resource-free
  selection result when selection was required;
- source-plan identity and policy generation;
- typed request origin and any orchestration-owned delegation association;
- the owner-issued Workspace revision identity and
  `PlatformLibraryPopulationDeclaration` when a Workspace population caused
  the request;
- every selected or outcome-relevant source contribution;
- reference and implementation view correspondence;
- the exact `LibraryReference`, content references, physical identities, roles,
  origins, Platform targets, source evidence, and correspondence of every
  returned Library;
- Metadata binding or forwarding outcomes when invoked;
- source, Workspace, and catalog generations;
- completeness; and
- consumed work.

The receipt does not expose credentials, mutable buffers, live streams,
filesystem paths as identity, or a capability that can repeat source work.
It retains no source handle, content obligation, `LibraryContentOwner`,
`LibraryOperationLease`, callback, opener, or disposal delegate. Source,
Artifact, and Workspace generations remain owner-issued evidence; PlatformHouse
does not add a Library generation.

The ownership-carrying realization envelope pairs each resource-free completed
Library value with exactly one caller-owned `LibraryContentOwner`. A completed
owning `Realize` operation transfers every owner present in that result once.
Every terminal path first settles every internal operation lease. PlatformHouse
then asynchronously retires every constructed owner not transferred in that
completed owning result, including owners used by a successful assembly-
reference or type-resolution operation. A non-completed operation transfers no
owner. Retirement or child-release failure remains visible as `Failed`; it
cannot become a resource-free success or be hidden by another source
contribution.

## Lifetime and Workspace boundary

The House may discover that an authorized platform source must acquire or
realize content. It does not mutate a sealed `AssemblyContextGroup` or publish a
Workspace generation.

For Workspace-bound resolution:

1. the House returns an owner-issued acquisition or realization demand
   associated with the exact request and target;
2. the assembly ladder suspends under its existing result contract;
3. Workspace performs retirement, preparation, admission, and replacement;
4. the House receives the replacement's fresh source and binding
   correspondence; and
5. the logical operation continues only after validating every generation and
   policy identity.

Workspace may publish the requesting participant before any platform
compatibility traversal occurs. A replacement that adds such a participant is
a membership change, not a compatibility receipt; target skew neither blocks
publication nor changes the meaning of the Workspace revision identity.

For standalone platform inspection, the caller owns every transferred
`LibraryContentOwner` and asynchronously retires each owner after its Library
operations settle. A Workspace-bound caller instead transfers accepted owners
under the Workspace owner's admission contract. Neither caller disposes a
source lease returned by PlatformHouse, because no such lease enters the House
result.

House caches key successes and failures by target demand, settled exact target,
target-selection policy generation, operation shape, source-plan generation,
source generation, view, and population demand. A replacement or policy
change cannot reuse a prior binding decision merely because the paths or
display labels are equal. Cache entries contain only resource-free references,
correspondence, and evidence; they cannot keep a Library owner or operation
lease alive.

## Encapsulation boundary

Product code that decides platform binding or transparent .NET Standard
implementation resolution calls `PlatformHouse`. Product code processing a
`PackageRef` calls `PackageHouse`;
only an orchestration-owned typed platform delegation or ordinary typed
platform request may cross into `PlatformHouse`.

Lower-level owner APIs remain valid for:

- their own implementations and tests;
- source-adapter construction;
- generic Metadata resolution in non-platform domains;
- pruning data generation, PackageHouse decision-making, and audit projection;
  and
- independently designed diagnostics that expose an owner fact without using
  it to change reference processing.

They are not parallel product service surfaces. In particular:

- CLI and Browser do not call `PlatformResolver`, `PlatformPackService`,
  or `PlatformTypeCatalog` to process a platform reference;
- CLI, Browser, Queries, and Workspace do not send a raw `PackageRef` to
  `PlatformHouse` or ask it to invoke pruning;
- Queries and Workspace do not recreate platform source precedence or
  .NET Standard forwarding policy;
- CLI and Browser pass owner-issued platform evidence through the
  DocumentationHouse adapter rather than settling documentation themselves;
- the assembly ladder receives one House platform contribution rather than
  composing platform internals; and
- source adapters do not call back into the House or the assembly ladder.

The user selected no automated absence gate for this composition claim. It
remains binding architecture enforced by design and code review. Future
adoption gates prove representative product paths through the House but do not
scan the repository or compiled graph for every possible bypass.

## Project and dependency boundaries

The logical House owner may span projects; a project name alone does not
define the owner.

The required dependency shape is:

```text
DotnetInspector.Platforms
  package-neutral target currency
             |
             v
PlatformHouse contract and composition
  consumes platform source, Metadata, and owner results
             |
             | one-library Realize
             v
LibraryContentOwner + resource-free LibraryReference
             |
             v
shared Library inspection

PackageHouse --typed platform delegation--> application orchestration
platform CLI or FrameworkReference --------> application orchestration
application orchestration --ordinary platform request--> PlatformHouse

direct library -----------------------------------------> shared Library inspection

Workspace registration snapshot
  -> bounded population planner
     -> Platform(DotNetRuntime) -> target demand -> PlatformHouse
     -> Platform(AspNetCore) ----> target demand -> PlatformHouse
     -> package prefixes --------> package discovery and PackageHouse
```

`DotnetInspector.Platforms` remains the lower resource-free contract floor. It
does not acquire House request, source, package, pruning, Metadata, or result
dependencies.

The operational House begins in the separate host-neutral
`DotnetInspector.PlatformHouse.Execution` project above the
`DotnetInspector.PlatformHouse` contract seam, Metadata, Artifact content
children, and shared Library ownership. Source adapters continue to depend only
on the contract seam while producing their source results. During Artifact
materialization, application orchestration wraps each selected realization in
`PlatformLibraryArtifactProvenance`, retains Metadata's owner-issued projection
from the Artifact admission callback, and passes the resulting resource-free
selection plus separate content lease into execution.
`PackageHouse` does not call into that implementation project; it returns its
typed delegation to an orchestration layer that can issue an ordinary House
request. Further project moves are tracked by
[#6335](https://github.com/richlander/dotnet-inspect/issues/6335).

Source-specific implementations remain focused:

- installed implementation realization preserves its package-free dependency
  boundary;
- package-backed pack acquisition may depend on package source and payload
  owners;
- documentation settlement remains with DocumentationHouse;
- Browser-generated catalogs remain portable and contain no desktop adapter;
- pruning remains with the package/pruning owner; and
- forwarding remains with Metadata.

The House composition project may depend on these owner contracts. None of
them depends on the operational House merely to preserve its own facts. Source
capabilities and the cross-House delegation handoff are injected or adapted in
the direction that avoids `PackageHouse -> PlatformHouse -> Packages` and
equivalent cycles.

## Pathological cases

### A delegated package ID matches an assembly name

PackageHouse delegates a subsumed `System.Text.Json` package coordinate to a
runtime platform target. The delegation does not prove that
`System.Text.Json.dll` is the requested or supplying assembly. PlatformHouse
realizes a whole population unless the owning scenario also supplies an exact
platform-library or assembly demand. Equal spelling cannot create that demand.

### A ranged package reference needs version metadata

PackageHouse receives a package version range that the pruning service cannot
compare. It may resolve package version metadata before making its final
decision, but it does not acquire and unwrap the package payload first.
PlatformHouse receives no request until the package owner emits a typed
delegation with one exact platform target.

### A direct library bypasses both container Houses

A direct-library request does not enter PlatformHouse merely to obtain command
symmetry. Its separately owned #6621 slice 6 adopts the shared Library
contract. This PlatformHouse design neither assigns its acquisition and
construction steps nor defines its operation-lease use.

### A newer-target package remains inspectable

A Workspace retains a .NET 10 platform and loads a package library selected for
.NET 12. The participant is admitted without acquiring or replacing a complete
.NET 12 platform. Package Overview, metadata, API, IL, source, decompilation,
and comparison against another explicitly selected library remain available.

When one operation follows the package library's reference into the Workspace
platform, the loaded .NET 10 candidate is usable only when normal assembly
binding and the exact requested member signature both resolve. A member shared
unchanged by .NET 10 and .NET 12 succeeds while retaining the platform-skew
warning. A member whose signature changed, or which exists only in .NET 12,
returns the attributed compatibility failure. Neither result removes the
package participant or blocks unrelated inspection.

### Runtime implementation facade resolves in its own view

A type request starts from the selected `System.Xml` implementation facade for
one exact .NET runtime target. The starting and required views are both
Implementation. Metadata follows the exact `System.Xml` and
`System.Xml.ReaderWriter` forwarding declarations to the physical
`System.Private.Xml` definition under the House-supplied implementation
policy. The House retains the outcome's exact terminal arm, physical
definition identity, and every forwarding hop in one resource-free result. It
does not introduce a reference resolution or view-correspondence transition.

### .NET Standard facade resolves to runtime implementation

A type request starts from a selected `.NET Standard` facade. The House target
is `DotNetRuntime/net11.0/11.0.0`. Metadata follows the facade's exact
forwarding declarations under the House-supplied reference policy. Whether the
reference path ends at a forwarded or directly declared `TypeDef`, the House
then consumes explicit view correspondence and starts a separate Metadata
resolution in the runtime implementation context. The settlement retains the
reference-contract start, real forwarding hops, the non-forwarding view
transition, and the physical runtime supplier. No `NetStandard` family target
exists.

### A reference TypeDef is not an implementation TypeDef

The reference view defines the requested type directly. Metadata correctly
returns that reference `TypeDef` and performs no forwarding. The House does
not reinterpret the definition as an implementation body. Implementation
demand proceeds only through explicit view correspondence and a second
Metadata request against the implementation context.

### .NET Standard implementation is unavailable

The same facade is available, but the authorized source plan contains no
implementation contribution. A reference-only API request may complete. An
implementation-body request returns unavailable or Metadata's exact unbound
outcome. It does not report that the type has no implementation.

### ASP.NET Core has a runtime support closure

An ASP.NET Core implementation source returns an exact ASP.NET Core focus
target and a separately selected runtime support target. The House preserves
both and the source owner's closure evidence. It neither merges their family
identities nor replaces one patch version with the other.

### Browser catalog is stale

The Browser source plan supplies a generated catalog that lacks the requested
target. The House may use another explicitly authorized Browser capability.
Otherwise it returns unavailable or incomplete. It does not probe desktop
installation paths or relabel the stale catalog.

### A source succeeds after another source fails

Whether the later success is usable depends on the explicit source policy. A
fallback plan may select it while retaining the first failure. An aggregation
plan may remain incomplete if the failed source could contribute a peer
candidate or required facet. Enumeration order alone decides nothing.

### Budget expires after one candidate

One source returns an identity-eligible assembly before work expires, but
another required source has not settled. The House returns incomplete unless
the source plan proves the first source has precedence independent of the
unexamined result. It does not publish a provisional first match as settled.

## Production adoption and retirement

There are ten counted production steps:

1. Lock the lower Platform Target Currency under #6361.
2. Lock this focused House contract under #6301.
3. Introduce the host-neutral target-demand, House request, outcome, receipt,
   source-plan, and contribution types without moving source implementations.
4. Adapt installed target discovery plus reference and implementation
   realization to contribute owner-issued selection and exact-target source
   evidence while preserving the package-free installed boundary.
5. Adapt package-backed target discovery plus reference and implementation
   packs to contribute source-authorized selection and exact-target evidence.
6. Adopt shared Library ownership in PlatformHouse: construct one
   `LibraryContentOwner` and resource-free `LibraryReference` per selected
   Library, transfer returned owners separately from resource-free House
   receipts, and route Platform Library inspection through that shared entry.
7. Move target-bound type indexing behind the House and integrate transparent
   .NET Standard resolution through Metadata's structured forwarding contract.
8. Adopt the House platform contribution in the assembly-reference ladder,
   add the PackageHouse-to-platform delegation handoff without a direct
   House-to-House dependency, lower selected Workspace platform-population
   declarations into family-preserving House requests, and integrate Queries,
   Workspace replacement, call-graph population, and dependency traversal.
9. Adopt the same House requests and outcomes in CLI and Inspect Web.
10. Retire direct product reference-processing in `PlatformResolver`,
    `PlatformPackService`, `PlatformTypeCatalog`, and host composition, then
    complete the `DotnetInspector.Services` decomposition tracked by #6335.

Each step after the House contract is a separately reviewed owner adoption.
The stack preserves a usable product after every step; a bypass is retired only
after its House replacement is live in every supported host that uses it.
Platform documentation adapters and retirement are counted separately by
DocumentationHouse tracker #6579.

Step 4 was staged without changing the ten-step count. Step 4a is owned by
[Installed Reference-Pack Realization](installed-reference-pack-realization.md)
and adds explicit-hive reference target discovery, immutable reference-pack
realization, and the installed PlatformHouse bridge. Step 4b adds the
host-neutral
[Platform Manifest Formats](platform-manifest-formats.md), the manifest-defined
installed implementation closure owned by
[Platform Composition and Overlays](platform-composition-and-overlays.md#installed-implementation-platform-realization),
and its installed PlatformHouse contribution. Both sub-slices are implemented;
step 5 is staged by
[Package-backed Platform realization](package-backed-platform-realization.md).
Step 5a implements package-backed target discovery, reference-pack realization,
and the package PlatformHouse adapter. It supplies source contributions, not
completed House receipts or a target-selection executor. Step 5b implements
RID-specific runtime-pack acquisition and manifest-defined implementation
closure through the same adapter. The two implemented sub-slices preserve the
ten-step count. With both package-backed sub-slices and the concrete Library
owner in #6621 slice 3 implemented, step 6 is active. Step 6a, implemented
under #7237, accepts already selected Artifact-backed content for one exact
Library, closes its requested roles, constructs the shared Library owner and
reference atomically, and transfers the owner beside a resource-free composed
receipt. Step 6b adapts source values into that Artifact-backed handoff. Step
6b.1, implemented under #7269, materializes successful installed reference and
implementation values for one exact assembly demand while returning Artifact
and Library ownership separately. Installed target/source selection and
terminal source-attempt settlement remain with later orchestration. Step 6b.2,
implemented under #7304, materializes equivalent package-backed values while
retaining package authority, producer, content-generation, origin, coordinate,
and digest evidence as resource-free provenance. Both source-specific projects
invoke one shared Artifact publication, Metadata projection, and cleanup
kernel. Step 6c adopts complete-population realization one view and source at a
time. Step 6c.1, implemented under #7322, materializes one authoritative
installed reference population into an ordered all-or-nothing set of
reference-only Library owners plus one separately returned Artifact session.
Step 6c.2, implemented under #7339, applies the same atomic handoff to one
authoritative installed implementation closure and assigns each runtime content
both Library roles under Platform-owned declaration-surface evidence. Paired
step 6c.3, implemented under #7354, joins independent authoritative installed
reference and implementation populations by exact managed assembly identity
and transfers their lossless union as one all-or-nothing population. Step 6c.4,
implemented under #7383, adapts authoritative package-backed paired populations
through the same kernel while retaining exact per-member package provenance and
detached Package Source lifetime. Step 6c.5, implemented under #7395, adapts
one authoritative package-backed reference population through the reference
kernel while preserving source order, exact per-member package provenance, and
detached Package Source lifetime. Step 6c.6, implemented under #7457, adapts
one authoritative package-backed implementation population through the
implementation kernel, preserving source order, exact per-member package and
runtime-support provenance, detached Package Source lifetime, and
Platform-owned declaration-surface role closure. Internal Metadata operations
remain later step-6 slices.

Step 7 is staged without changing the ten-step count. Step 7a, tracked by
[#7892](https://github.com/richlander/dotnet-inspect/issues/7892), selects one
family-default target and realizes its authoritative complete reference
population in the same closed operation. Step 7b derives the target-bound type
catalog from that population through LibraryMetadata's exact declaration
correspondence. Step 7c, implemented under
[#8096](https://github.com/richlander/dotnet-inspect/issues/8096), adapts user
text to that resource-free catalog with typed resolved, ambiguous, missing, and
rejected outcomes. The stage-7a population operation does not expose catalog
semantics or change CLI or Inspect Web routing. Step 9a, tracked by
[#8164](https://github.com/richlander/dotnet-inspect/issues/8164), adopts the
family-default population and catalog query for versionless bare CLI type and
member routing. Browser/Wasm adoption and Services-era resolver retirement remain later
focused slices. The first exact-demand Metadata binding slice, implemented
under [#8298](https://github.com/richlander/dotnet-inspect/issues/8298),
supports one implementation-view start and required implementation result. It
does not implement the reference-to-implementation bridge.

The step-6 ownership correction was designed under
[#6984](https://github.com/richlander/dotnet-inspect/issues/6984). It adopts
the shared Library contract without changing the ten-step PlatformHouse count;
implementation is tracked by
[#7237](https://github.com/richlander/dotnet-inspect/issues/7237). Workspace and
direct-library construction remain the separate #6621 slice 6 and do not enter
this PlatformHouse adoption.

No CLI flag is retained solely for compatibility. User-facing platform
coordinates project to the shared target and House request, and unsupported
legacy combinations fail visibly under the adopting command owner.

## Demo

### Curated Workspace realizes registered ecosystems

```text
Ecosystems.CreateCuratedWorkspace()
  registrations
  Platform
    -> PlatformLibraryPopulationDeclaration(DotNetRuntime)
  ASP.NET Core
    -> PlatformLibraryPopulationDeclaration(AspNetCore)
    -> PackagePrefix(Microsoft.AspNetCore.)
  Microsoft.Extensions
    -> PackagePrefix(Microsoft.Extensions.)

call graph
  focal length: Everything
  target context: net11.0
  source policy: installed platform + authorized package sources

population planner
  Platform registration
    -> DotNetRuntime target demand
    -> PlatformHouse
    -> exact runtime target + realized runtime population
  ASP.NET Core registration
    -> AspNetCore target demand
    -> PlatformHouse
    -> exact ASP.NET Core target + explicit runtime support closure
  ASP.NET Core and Microsoft.Extensions prefixes
    -> bounded package discovery and PackageHouse

completed
  one graph population retains:
    exact Workspace revision identity and registrations
    separate runtime and ASP.NET Core family targets
    every package-prefix request and outcome
    exact artifact correspondence for any coalesced library
    visible source, acquisition, and population completeness
```

What to notice: registration selected the relevant populations but did not
authorize or perform acquisition. The operation supplied target and source
policy, and no ecosystem label or package prefix was reinterpreted as platform
identity.

### PackageRef exits to platform realization

```text
PackageRef ingress
  coordinate: System.Text.Json 10.0.0
  exact target: DotNetRuntime / net11.0 / 11.0.0

PackageHouse
  pruning service: Subsumed
  package payload acquisition: skipped
  output:
    typed platform delegation
    package decision receipt retains PackageRef and pruning evidence

orchestration -> PlatformHouse
  operation: realize
  target: DotNetRuntime / net11.0 / 11.0.0
  demand: platform population
  sources: host-authorized installed + package-backed platform plan

PlatformHouse
  target/version settlement
  authorized realization acquisition
  reference/implementation selection

completed
  platform receipt retains exact target, source, views, and work
  each selected Library has a caller-owned owner and resource-free reference
  with Platform provenance
  package receipt remains associated but separate
```

### Versionless routing selects an exact target before realization

```text
request
  target demand:
    family: DotNetRuntime
    policy: versionless runtime default
    minimum preferred version: 10.0.1
    fallback band: stable net10.0 servicing
  operation: realize
  library: owner-issued System.Text.Json assembly identity
  demand: reference + implementation
  discovery:
    preferred: installed Platform
    fallback: authorized package-backed Platform

House composition
  installed target discovery:
    9.0.11
    10.0.0
    11.0.0-rc.1
  selection:
    exact target: DotNetRuntime / net11.0 / 11.0.0-rc.1
    package discovery: not invoked
    owner-issued candidate and policy evidence retained
  realizes reference and implementation contributions
  verifies view correspondence
  constructs one shared Library owner and reference

completed
  owning realization:
    caller-owned LibraryContentOwner
    resource-free LibraryReference
    ApiAssembly content: reference-pack System.Text.Json.dll
    ImplementationAssembly content: runtime-pack System.Text.Json.dll
    Platform origin, exact target, and source evidence
    reference/implementation correspondence
  resource-free receipt:
    exact Library and content references
    no owner, lease, callback, stream, opener, or disposal delegate
```

What to notice: the owner and the receipt answer different questions. The
versionless input does not erase version identity: the policy first selects
the exact `net11.0` release-candidate target and the owning realization then
reports it. If the installed inventory instead ended at `10.0.0`, preferred
discovery would authoritatively establish no eligible target; only then would
the package-backed stage discover the current stable `10.0.x` inventory and
select its highest servicing target.

The caller-owned `LibraryContentOwner` keeps the selected Platform contents
alive and can issue later operation authority. The `LibraryReference`, content
references, and House receipt explain exactly which Platform target, source,
and views were selected without keeping those contents alive. A NuGet
`System.Text.Json` assembly with equal Metadata identity has a different exact
Library source coordinate and cannot replace either Platform content.

### Direct library bypasses container resolution

```text
CLI or asset-reference ingress
  path, HintPath, or built project output

separately owned direct-library path
  adopts the shared Library contract under #6621 slice 6
  no PackageHouse or PlatformHouse request is manufactured
```

### Transparent .NET Standard forwarding

```text
request
  target: DotNetRuntime / net11.0 / 11.0.0
  operation: resolve type definition
  start: selected netstandard reference facade
  demand: implementation

House composition
  reference contract retained separately
  reference Metadata resolution follows:
    ExportedType -> exact AssemblyRef -> selected runtime candidate
  reference TypeDef retained
  House view correspondence selects an implementation start
  implementation Metadata resolution reaches the physical supplier

completed
  no NetStandard family target
  reference facade, real forwarding hops, view correspondence,
  runtime target, implementation supplier, and source evidence all retained
```

## Evidence and required gates

This design introduces no new independent state machine. Source operations
remain under their source owners, Metadata forwarding remains under its
bounded frozen-context contract, and Workspace replacement remains under the
artifact-session lifecycle. A new TLA+ model would duplicate those owners
rather than establish this request/settlement boundary.

The implementation and adoption slices own these Release gates:

| Property | Required gate |
| --- | --- |
| Target settlement | An exact demand is retained unchanged; a framework-scoped or family-default demand freezes one owner-issued exact `PlatformFamilyTarget` before acquisition, and every outcome retains the demand and selection evidence. |
| Versionless installed default | Installed `9.0.11`, `10.0.0`, and `11.0.0-rc.1` candidates under the named policy select exact `DotNetRuntime / net11.0 / 11.0.0-rc.1`; package discovery is never invoked, and the receipt retains the floor, policy generation, selected target, and preferred discovery evidence. |
| Versionless stable fallback | Preferred discovery containing only versions below `10.0.1` permits one authorized fallback discovery; a complete inventory containing stable `10.0.1` and `10.0.12` plus a later `10.0` preview selects exact stable `10.0.12` and retains both stages' outcome-relevant evidence. |
| Versionless failure visibility | Non-authoritative `Unavailable(Unavailable)`, rejected, incomplete, or failed preferred discovery invokes no fallback and remains visibly terminal; only typed `Unavailable(Absent)` or a completed inventory with no eligible candidate proves preferred absence, and partial or failed fallback discovery cannot select from its observed prefix. |
| Exact-version independence | Exact `runtime@9.0.11` remains exact and bypasses the versionless floor, preferred stage, and stable fallback. |
| Workspace population correspondence | The operation association retains the owner-issued Workspace revision identity and ecosystem registration; the House request and receipt retain that identity, `PlatformLibraryPopulationDeclaration`, family-preserving target demand, and settled target. |
| Workspace family mismatch | An `AspNetCore` population declaration paired with a `DotNetRuntime` target demand rejects before target or source work, remains associated with the selected registration, triggers no retry or relabeling, and prevents complete population coverage. |
| Curated realization | Selecting the current three curated ecosystem registrations issues independent `DotNetRuntime` and `AspNetCore` House requests plus the two authored package-prefix paths; registration alone performs no source work. |
| Family separation | Equal TFM or version text and an ASP.NET Core runtime support closure cannot merge the runtime and ASP.NET Core registrations or their exact family targets. |
| Focal-length integration | `Self`, `SelfAndRegisteredEcosystems`, and `Everything` select the documented population sets without granting source authority or silently omitting a failed selected contribution. |
| Explicit source authorization | No source capability, cache, installed root, or network route is used unless present in the captured source plan. |
| Demand-bounded work | One-library demand does not become whole-population work unless the selected source reports an atomic larger realization unit. |
| Reference and implementation separation | Reference-only success cannot satisfy implementation-body demand; a combined result retains explicit correspondence. |
| Implementation-only role closure | Implementation-only realization succeeds only when Platform-owned declaration-surface evidence assigns the selected runtime content both mandatory roles; without that evidence it returns typed `Unavailable` before Library construction and constructs no owner. |
| Reference-definition bridge | A reference `TypeDef` reaches implementation only through House-owned view correspondence and a separate Metadata request; no forwarding hop is invented. |
| Typed ingress separation | A raw `PackageRef` cannot enter PlatformHouse, and a bare CLI selector cannot become an `AssemblyRef` or `PackageRef` without command-owned classification. |
| Delegation preserves identity boundaries | An upstream platform delegation retains its package decision receipt outside PlatformHouse and cannot establish a platform-library or assembly identity from package spelling. |
| Shared Library construction | The .NET 11 Platform `System.Text.Json` realization constructs one source-distinct `LibraryReference` with reference-pack `ApiAssembly` and runtime-pack `ImplementationAssembly` content, exact Platform target/source/provenance, and view correspondence; an equal-identity NuGet Library cannot substitute. |
| Installed Artifact materialization | Successful installed reference-pack and implementation-layout `System.Text.Json` snapshots publish into one bounded Artifact generation without reopening installed files; source provenance, exact House contributions, Metadata projections, and Library roles remain correspondent. |
| Package Artifact materialization | Successful package-backed reference-pack and runtime-pack `System.Text.Json` snapshots publish into one bounded Artifact generation without package rediscovery or reacquisition; candidate, authority, producer, content generation, payload origin, member coordinate, digest, exact House contributions, Metadata projections, and Library roles remain correspondent. |
| Owning realization handoff | Every completed one-Library and population realization transfers each `LibraryContentOwner` exactly once beside its matching resource-free reference; the House value, receipt, contribution, request, and cache retain no owner or Library lease. |
| Separate installed authorities | Installed one-Library completion returns the Library owner and adjacent Artifact session separately; Artifact retirement waits while the Library retains content and completes after Library retirement. Terminal execution and cancellation return neither authority. |
| Installed reference population ownership | One authoritative installed reference population produces one source-ordered Library owner per distinct managed identity and one population-wide selected source settlement. Completion transfers every owner beside one Artifact session; terminal, cancellation, duplicate identity, incomplete work, and partial construction transfer none and release every accepted or unaccepted content authority. |
| Installed implementation population ownership | One authoritative installed manifest-defined implementation closure produces one source-ordered Library owner per distinct managed identity, assigns each exact runtime content both Library roles under population declaration-surface evidence, and records one selected source settlement. Completion transfers every owner beside one Artifact session; terminal and cancellation paths transfer neither authority. |
| Installed paired population ownership | Independently authoritative installed reference and implementation populations publish into one bounded Artifact generation and settle once each. Exact managed identities pair; same-name non-equivalent identities and unmatched members remain distinct in a reference-first lossless union. Completion transfers every Library owner beside one Artifact session; terminal and cancellation paths transfer neither authority. |
| Separate package authorities | Package-backed one-Library completion returns the Library owner and adjacent Artifact session separately after Package Source operations settle; Artifact retirement waits while the Library retains content and completes after Library retirement. Terminal execution and cancellation return neither authority. |
| Package-backed paired population ownership | Independently authoritative package-backed reference and implementation populations publish into one bounded Artifact generation and settle once each after Package Source operations detach. Exact managed identities form the same reference-first lossless union while every content item retains source-issued package provenance. Completion transfers every Library owner beside one Artifact session; terminal and cancellation paths transfer neither authority. |
| Package-backed reference population ownership | One authoritative package-backed reference population produces one source-ordered reference-only Library owner per distinct managed identity and one source settlement after Package Source detaches. Every content item retains source-issued package provenance. Completion transfers every Library owner beside one Artifact session; terminal, cancellation, duplicate identity, foreign contribution, and incomplete work transfer neither authority. |
| Package-backed implementation population ownership | One authoritative package-backed runtime population produces one source-ordered implementation-only Library owner per distinct managed identity, assigns each exact runtime content both mandatory roles under Platform-owned declaration-surface evidence, and records one source settlement after Package Source detaches. Every content item retains source-issued package and runtime-support provenance. Completion transfers every Library owner beside one Artifact session; terminal, cancellation, duplicate identity, foreign contribution, and incomplete work transfer neither authority. |
| Selected reference population | One family-default, reference-only complete-population operation freezes one exact target, applies the authorized Reference source policy, and publishes one non-empty authoritative population. Installed success suppresses package work; package fallback uses only the exact selected discovery association; failure or work exhaustion cannot publish a shortened population. Completion preserves the original request, selected target and discovery evidence, source-generation settlements, and cumulative work while transferring all Library owners beside the separate Artifact session. Every terminal path cleans up before projection and retains no live authority. |
| Resource-free House boundary | Focused contract tests over contributions, completed House values, type-definition resolution results, receipts, requests, and cache entries prove that they retain no live source handle or content obligation, Artifact owner or lease, Library owner or lease, Metadata context or catalog, candidate, callback, opener, stream, or disposal delegate. |
| Internal Library access | PlatformHouse Metadata work reads exact content only through a fresh internal `LibraryOperationLease`; every borrow ends before `await`, and the lease settles before completion. |
| Terminal owner disposition | Completed non-owning operations, unavailability, ambiguity, rejection, failure, incomplete completion, and cancellation retire every constructed owner the House does not return, including partially constructed multi-Library population work; only a completed owning `Realize` result transfers owners, and retirement failure remains visible. |
| Failure precedence | A superseded source fault may remain evidence in another terminal outcome; construction, borrow, lease-settlement, retirement, or child-release failure produces `Failed`, including when cleanup fails after cancellation. |
| Permissive Workspace admission | A participant targeting a newer platform remains admissible and usable for same-participant inspection without realizing a matching complete platform. |
| Exact traversal compatibility | Under an owner-classified unsupported downgrade, an exact Metadata member-signature match succeeds with downgrade context; a missing or changed signature returns the attributed compatibility failure without blocking unrelated work. Supported upward compatibility does not warn merely because targets differ. |
| Metadata ownership | Platform type resolution invokes the structured Metadata API and projects its exact terminal arm, owner-issued facts, and ordered forwarding hops before retiring the live resolution context. |
| Direct implementation start | An implementation-view `System.Xml.XmlReader` request resolves through `System.Xml.ReaderWriter` to the physical `System.Private.Xml` definition in one Metadata outcome, with no fabricated reference outcome or view correspondence. |
| Transparent .NET Standard | A `.NET Standard` facade can resolve through an exact runtime target without constructing a `NetStandard` family or implementation population. |
| Physical supplier retention | A resolved implementation type or assembly retains its physical supplier rather than being relabeled as the reference facade. |
| Visible incomplete evidence | Source, catalog, forwarding, acquisition, work, and generation incompleteness never become absence or a success-shaped empty result. |
| Ladder composition | The assembly-reference ladder receives one House platform contribution and preserves its own rung order and result algebra. |
| Host parity | Representative CLI and Browser operations issue equivalent House requests and interpret the same typed outcomes. |
| Installed boundary | Browser composition does not reference desktop installed adapters, and installed realization remains package-free. |
| Encapsulation | Representative ladder, Query, CLI, and Browser paths use the House. Per user choice, no automated repository-wide bypass-absence gate is required. |

The implemented step-6a gates are:

- `ExactLibraryRealizer_TransfersReferenceAndImplementationOwner` for exact
  Platform provenance, role closure, owner transfer, post-House borrowing, and
  adjacent Artifact-session retirement;
- `ExactLibraryRealizer_ClosesReferenceOnlyRole` and
  `ExactLibraryRealizer_AssignsBothRolesWithDeclarationEvidence` for the two
  neighboring successful role shapes;
- `ExactLibraryRealizer_RequiresImplementationDeclarationSurface` for typed
  pre-construction unavailability;
- `ExactLibraryRealizer_RejectsForeignContentWithoutAcceptingLease`,
  `ExactLibraryRealizer_RejectsUnauthorizedSourceWithoutAcceptingLease`, and
  `ExactLibraryRealizer_CancellationPrecedesOwnershipAcceptance` for source
  authorization and atomic transfer;
- `ContentSelection_RejectsForeignMetadataProjection` for owner-issued
  source-to-Artifact and Artifact-to-managed-identity binding; and
- `PlatformLibraryCompletedEvidence_IsResourceFree` and
  `PlatformHouseFailedOutcome_RetainsTypedResourceFreeEvidence` for the
  receipt and failure boundaries.

The implemented step-6b.1 gates add the real installed `System.Text.Json`
reference and implementation path, exact source-to-Artifact-to-Metadata
correspondence, separate Library and Artifact authority retirement, and
terminal/cancellation cleanup:

- `InstalledSystemTextJson_TransfersLibraryAndArtifactAuthorities` covers the
  real installed reference and runtime images, source provenance, Metadata
  identity, role closure, owner transfer, borrowing, and ordered retirement;
- `InstalledReferenceOnly_ClosesOneApiRole` and
  `InstalledImplementationOnly_AssignsBothRoles` cover neighboring view shapes;
- `ForeignSuccessfulResult_IsRejectedBeforePublication` and
  `MissingPriorSourceEvidence_ReturnsTerminalAfterCleanup` cover exact
  request/result pairing and terminal-path ownership;
- `CancellationPrecedesArtifactOwnership` covers cancellation before Artifact
  acceptance; and
- `InstalledArtifactProvenance_IsResourceFree` and
  `SuccessfulSourcePairing_IsAdapterIssued` cover the resource-free boundary
  and owner-issued live-value/contribution association.

The implemented step-6b.2 gates add the real package-backed `System.Text.Json`
reference and runtime path, exact package-source-to-Artifact-to-Metadata
correspondence, common publication/cleanup behavior, and separate Library and
Artifact authority retirement:

- `GallerySystemTextJsonMaterializesPairedLibraryAuthorities` covers pinned
  nuget.org reference/runtime packages, detached Package Source lifetime,
  package provenance, Metadata identity, role closure, borrowing, and ordered
  retirement;
- `PackageSystemTextJson_TransfersLibraryAndArtifactAuthorities` covers the
  fast paired path and exact candidate, authority, producer,
  content-generation, contribution, and digest correspondence;
- `PackageReferenceOnly_ClosesOneApiRole` and
  `PackageImplementationOnly_AssignsBothRoles` cover neighboring view shapes;
- `ForeignSuccessfulResult_IsRejectedBeforePublication` and
  `MissingPriorSourceEvidence_ReturnsTerminalAfterCleanup` cover exact
  request/result pairing and terminal-path ownership;
- `CancellationPrecedesArtifactOwnership` covers cancellation before Artifact
  acceptance; and
- `PackageArtifactProvenance_IsResourceFree` and
  `SuccessfulSourcePairing_IsAdapterIssued` cover the resource-free boundary
  and adapter-issued live-value/contribution association.

The implemented step-6c.1 gates add installed reference-only complete
population ownership:

- `InstalledReferencePopulation_TransfersOrderedLibraryAuthorities` covers the
  real installed .NET 11 `System.Runtime` and `System.Text.Json` reference
  images, source order, exact source-to-Artifact-to-Metadata correspondence, one
  selected population contribution, borrowing, all-owner transfer, and Artifact
  retirement after the last Library retires;
- `ReferencePopulationRealizer_TransfersOrderedOwnersAtomically` covers the
  source-neutral two-Library handoff and exact owner/value/receipt index
  correspondence;
- `ReferencePopulationRealizer_RejectsDuplicateIdentityAndCleansLeases` and
  `ReferencePopulationRealizer_IncompleteWorkCleansTransferredLease` cover
  distinct population membership and finite-work terminal cleanup;
- `PopulationArtifactMaterializer_RejectsDuplicateIdentityBeforePublication`
  proves invalid duplicate membership cannot open or publish population
  content;
- `ReferencePopulationRealizer_RetiresPartialOwnerOnInvalidAuthority` covers
  the partial-construction boundary where an earlier accepted owner must retire
  before the Artifact generation can retire; and
- `ReferencePopulationRealizer_CancellationCleansTransferredLease` covers
  cancellation only after the population operation releases transferred
  content authority.

The implemented step-6c.2 gates add installed implementation-only complete
population ownership:

- `InstalledImplementationPopulation_TransfersOrderedLibraryAuthorities`
  covers a real two-assembly .NET implementation closure, source order, exact
  source-to-Artifact-to-Metadata correspondence, implementation provenance,
  declaration-surface role closure, one source settlement, borrowing, atomic
  owner transfer, and Artifact retirement after every Library retires;
- `ImplementationPopulationRealizer_AssignsBothRolesAtomically` covers the
  source-neutral two-Library implementation handoff, exact owner/value index
  correspondence, population view evidence, and the same-content two-role
  invariant; and
- `ImplementationPopulationArtifactMaterializer_RejectsForeignContributionBeforePublication`,
  `ForeignImplementationPopulation_ReturnsTerminal`, and
  `ImplementationPopulationCancellationPrecedesArtifactOwnership` cover
  foreign owner evidence before content opening, installed terminal projection,
  and cancellation before population Artifact acceptance.

The implemented step-6c.3 gates add installed paired complete-population
ownership:

- `InstalledPairedPopulation_TransfersLosslessUnionAuthorities` covers real
  installed paired, reference-only, and implementation-only members; exact
  identity correspondence; reference-first ordering; two source settlements;
  atomic owner transfer; and Artifact retirement after every Library retires;
- `PairedPopulationRealizer_PreservesLosslessUnionAndCorrespondence` covers
  source-neutral pairing, unmatched-member retention, exact owner/value index
  correspondence, population view evidence, and both role-closure shapes;
- `PairedPopulationRealizer_DoesNotPairSameNameDifferentIdentities` proves that
  assembly versions, rather than simple names, control correspondence;
- `PairedPopulationArtifactMaterializer_RejectsForeignContributionBeforePublication`
  proves that either facet's foreign source evidence cannot open or publish
  population content; and
- `PairedPopulationRealizer_IncompleteWorkCleansBothFacets` and
  `PairedPopulationRealizer_CancellationCleansBothFacets` cover finite aggregate
  work and cancellation after both facets transfer content authority.

The implemented step-6c.4 gates add package-backed paired complete-population
ownership:

- `GalleryRuntimePopulationMaterializesPairedAuthorities` covers the pinned
  nuget.org .NET 11 reference and runtime packs, the exact lossless union,
  `System.Text.Json` view correspondence, package provenance, source settlement
  before returned-authority retirement, borrowing, atomic owner transfer, and
  Artifact retirement after every Library retires;
- `PackagePairedPopulation_TransfersLosslessUnionAuthorities` covers
  deterministic paired, reference-only, and implementation-only package
  members; reference-first ordering; exact per-member package provenance; two
  source settlements; owner/value index correspondence; and final Artifact
  retirement;
- `PackagePairedPopulation_RejectsForeignImplementation` proves a foreign
  facet cannot enter Artifact publication; and
- `PackagePairedPopulation_IncompleteWorkTransfersNoAuthority` and
  `PackagePairedPopulation_CancellationTransfersNoAuthority` cover finite
  aggregate work and cancellation before package population Artifact
  acceptance.

The implemented step-6c.5 gates add package-backed reference-only
complete-population ownership:

- `GalleryReferencePopulationMaterializesAuthorities` covers the pinned
  nuget.org .NET 11 reference pack, source order, reference-only role closure,
  package provenance, source settlement before returned-authority retirement,
  borrowing, atomic owner transfer, and Artifact retirement after every Library
  retires;
- `PackageReferencePopulation_TransfersOrderedLibraryAuthorities` covers
  deterministic source order, exact per-member package provenance, one source
  settlement, owner/value index correspondence, borrowing, and final Artifact
  retirement;
- `PackageReferencePopulation_RejectsForeignContribution` proves that foreign
  owner evidence cannot enter Artifact publication, while the existing
  `MalformedNetmoduleWinMdAndDuplicateIdentityRejectAtomically` source gate
  proves duplicate exact identities cannot enter an authoritative package
  population; and
- `PackageReferencePopulation_IncompleteWorkTransfersNoAuthority` and
  `PackageReferencePopulation_CancellationTransfersNoAuthority` cover finite
  aggregate work and cancellation before package reference-population Artifact
  acceptance.

The implemented step-6c.6 gates add package-backed implementation-only
complete-population ownership:

- `GalleryImplementationPopulationMaterializesAuthorities` covers the pinned
  nuget.org .NET 11 runtime pack, source order, same-content two-role closure,
  package and runtime-support provenance, source settlement before
  returned-authority retirement, borrowing, atomic owner transfer, and Artifact
  retirement after every Library retires;
- `PackageImplementationPopulation_TransfersOrderedLibraryAuthorities` covers
  deterministic source order, exact per-member package and runtime-support
  provenance, one source settlement, owner/value index correspondence,
  same-content two-role closure, borrowing, and final Artifact retirement;
- `PackageImplementationPopulation_RejectsForeignContribution` proves that
  foreign owner evidence cannot enter Artifact publication, while the existing
  `MalformedNetmoduleWinMdAndDuplicateIdentityRejectAtomically` source gate
  proves duplicate exact identities cannot enter an authoritative package
  population; and
- `PackageImplementationPopulation_IncompleteWorkTransfersNoAuthority` and
  `PackageImplementationPopulation_CancellationTransfersNoAuthority` cover
  finite aggregate work and cancellation before package
  implementation-population Artifact acceptance.

The implemented step-7a gates add selected-target complete reference
population execution:

- `InstalledPopulationCompletesAndSuppressesPackageWork` preserves the original
  family-default request, selected target, discovery and realization
  settlements, source generation, cumulative work, source order, and separate
  all-owner and Artifact-session retirement while proving installed success
  suppresses package discovery and realization;
- `PackageFallbackReceivesExactSelectedAssociation` and
  `SelectedPackageReferencePopulationCompletesFromDiscoveryAssociation` cover
  the exact fallback discovery association and real package-backed two-Library
  population;
- `ForeignPopulationAssociationRejectsBeforePackageOperation` proves an
  association mismatch cannot issue package operation authority; and
- `ExhaustedPopulationWorkCannotPublishShortenedResult` plus
  `UnmeasuredSelectedPopulationIncompleteReservesDelegatedWork` prove that
  finite-work exhaustion and unmeasured terminal work publish no shortened
  population and cannot reuse reserved work for a later source.

Each later implementation slice adds the smallest gate covering its adopted
property.

The House carries typed operation data but defines no presentation. Markout
and host-specific rendering are not part of this owner.

## Non-claims

This design does not:

- add `.NET Standard`, WindowsDesktop, or workloads as a `PlatformFamily`;
- define framework compatibility, runtime roll-forward, source version
  discovery, or version-ranking algorithms;
- define platform catalog membership or source-specific family composition;
- create a package-to-assembly naming convention;
- process a `PackageRef`, choose a package version, acquire a package payload,
  or apply package pruning;
- redefine the complete PackageHouse request, outcome, or receipt contract
  owned by [PackageHouse Composition](package-house.md);
- make pruning an `AssemblyRef` classifier;
- alter package dependency traversal or the assembly ladder's route order;
- alter Metadata forwarding, binding, declaration, or terminal outcomes;
- make a reference assembly an implementation supplier;
- settle compiled XML or authored-source documentation, documentation fields,
  provenance, or conflicts;
- redefine XML-documentation identity or parsing, SourceLink mapping, PDB
  matching, source acquisition, checksum verification, source-comment parsing,
  or symbol-server policy;
- claim that SourceLink provenance proves the physical syntax tree that
  produced a metadata definition;
- define Workspace admission, replacement, or lease lifetime;
- define Workspace curation, registration editing, call-graph focal
  length, or cross-population composition;
- require network access or desktop filesystem capabilities;
- permit inspected-assembly loading or Roslyn;
- add WinMD support; or
- create an aggregate platform assembly containing every platform helper.
