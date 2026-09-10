# PackageHouse composition

## Status and approved scope

This document is the normative owner for `PackageHouse`, tracked by
[#6426](https://github.com/richlander/dotnet-inspect/issues/6426).

The user approved a broad package architecture with one higher-level package
concept above package input, transport, and policy. `Workspace` delegates
package acquisition and package-policy decisions to that concept instead of
reconstructing them. The accepted `House` convention from
[#6301](https://github.com/richlander/dotnet-inspect/issues/6301) names this
concept `PackageHouse`: a foundational clearing-house service that aggregates
candidates from multiple sources, applies policy, and settles one authorized
result.

This is a broad, explicitly approved composition design. It defines only the
House facade, request, settlement, result, receipts, and typed handoffs.
Adjacent owners retain their identities, algorithms, failures, and lifetimes.
Their adoption remains separately reviewed.

The first production adopter is shared package realization for
`Workspace`. The CLI and Inspect Web then consume the same House contract
through their package and Workspace paths.

The current implementation basis is
`DesktopPackageSourceComposition` in `DotnetInspector.Packages`. Its package
authority, candidate, manifest, and payload behavior is mature, but its
desktop construction and host-specific identity are transitional.
[#4653](https://github.com/richlander/dotnet-inspect/pull/4653) remains useful
design and implementation evidence; it is not the branch this architecture
extends. Its remaining intent is retired through the focused adoption steps in
[#6426](https://github.com/richlander/dotnet-inspect/issues/6426).

## Authority and exact claim

**PackageHouse Composition** owns:

> Given one typed package demand, one optional owner-issued target-selection
> context, one host-authorized package source and input plan, one closed
> package operation, and finite work, settle the demand to one typed package
> result that preserves the request, package identity and version evidence,
> source authority, pruning decision, payload and asset correspondence,
> dependency realization correspondence, provenance, completion, failures,
> and retained lifetime without reconstructing identity from strings.

The owner defines:

- the product-facing `PackageHouse` facade;
- the package-demand envelope;
- the package operation profile and common operation context;
- the distinction between settlement, acquisition, and realization;
- composition of owner-issued package source, version, pruning, payload,
  asset-selection, and dependency capabilities;
- the House result envelope and terminal outcome;
- the package decision, acquisition, and realization receipts;
- typed package-to-platform delegation;
- package-to-library unwrapping with retained provenance;
- target-aware dependency-edge realization handoff;
- the evidence `Workspace` receives before package admission; and
- the rule that product package processing does not recreate House decisions
  in hosts or Workspace composition.

It does not define:

- package-source identity, source classification, mapping, candidate
  aggregation, payload authorization, or source failure semantics;
- package input parsing or normalized dependency evidence;
- package ID, package version, version-range, or framework grammar;
- version ordering, latest, wildcard, range, or prerelease policy;
- platform/package pruning inventory or comparison;
- dependency candidate resolution or graph traversal;
- package archive admission, content generation, or cache publication;
- compile, runtime, resource, analyzer, or build asset selection;
- artifact identity, Workspace admission, revisions, leases, replacement, or
  participant lifetime;
- platform target settlement or realization;
- Metadata, source, analysis, decompilation, rendering, or presentation; or
- host source registration, credentials, network permission, cache capacity,
  cancellation gesture, or display policy.

Those owners issue typed facts and capabilities. PackageHouse composes them
and preserves their evidence.

## Why packages need a House

Package behavior is already split into focused owners:

```text
typed package input
  -> package dependency evidence
  -> version and pruning policy
  -> configured package authorities
  -> source clients and transport
  -> exact candidate and payload
  -> package content and selected assets
  -> Workspace admission
```

The split is correct. The missing concept is the product-facing owner that
keeps one request and its evidence associated across those boundaries.

Without that owner:

- the CLI creates desktop source composition directly;
- Browser/Wasm constructs source clients, acquisition, caching, and package
  workspaces through a separate host path;
- dependency traversal resolves exact package candidates without itself
  issuing target package realization;
- Workspace acquisition can receive a coordinate without the exact selection
  request that produced its intended asset universe;
- pruning and package-to-platform delegation risk becoming host composition;
  and
- package inspection and library unwrapping can collapse into whichever
  assembly path a caller happened to select.

PackageHouse does not eliminate the focused owners. It gives their results one
settlement boundary and gives hosts one package operation to invoke.

## Relationship to package input, transport, and policy

PackageHouse is above these roles by composition, not by ownership transfer.

| Role | Owner-issued input consumed by PackageHouse |
| --- | --- |
| Package input | Normalized declaration, relationship, authorship, processing, and completion evidence from [Package Dependency Evidence](package-dependency-evidence.md) and [#6266](https://github.com/richlander/dotnet-inspect/issues/6266) |
| Source authority | Configured authority, candidate, aggregate, manifest, payload, and failure outcomes from the [Package Source Model](package-source-model.md) |
| Transport | Protocol-independent `IPackageSourceClient` operations and result identity from [Browser Package Sources](browser-package-sources.md) and `NuGetFetch` |
| Version policy | Exact, latest, wildcard, range, listing, and prerelease decisions from [Version Resolution](version-resolution.md) |
| Pruning | Exact target-bound package supply decisions from [Platform Package Supply Policy](platform-package-supply-policy.md) |
| Dependency expansion | Exact candidate and immutable graph evidence from [Package Dependency Traversal](package-dependency-traversal.md) |
| Asset selection | Generation- and request-bound outcomes from [Package Asset-selection Correspondence](package-asset-selection-correspondence.md) |
| Artifact lifetime | Generation-scoped content, contributions, and sessions from [Artifact Acquisition and Workspaces](artifact-acquisition-and-workspaces.md) |

PackageHouse does not accept transport URLs, rendered dependency rows, asset
paths, assembly names, or package labels as substitutes for those typed
inputs.

## Source-settlement lease

PackageHouse is the clearing house where source leases are issued and
source-owner receipts are settled. For the first operational slice in
[#6477](https://github.com/richlander/dotnet-inspect/issues/6477), the House
issues one `PackageHouseSourceLease` over an injected
configured-authority-to-`IPackageSourceClient` capability and a lower-owner
operation-context factory.

The lease owns one package acquisition candidate-issuer identity. It settles:

- caller-pinned exact candidate authorization;
- complete dependency-version discovery across one explicit
  `PackageSourceAuthorization`; and
- exact manifest acquisition through a candidate issued by that lease.

The authorization value remains package-source-owner evidence. The source
client remains a caller-owned capability whose result identity must match both
the exact configured-authority association and the exact client that performed
the operation. PackageHouse neither discovers a broader authority set nor
reconstructs source identity from endpoints.

When a caller omits `NuGetOperationContext`, the lease uses that injected
factory and disposes the created context after the settlement. A
caller-supplied context remains caller-owned.

Retiring the lease rejects new settlement and candidate use. Completed
candidate, discovery, manifest, failure, and timeout evidence remains valid as
data after retirement. Retirement does not dispose source clients,
authentication contexts, transports, caller-supplied `NuGetOperationContext`
instances, payload streams, package stores, artifact content, or Workspace
participants.

`DesktopPackageSourceComposition` owns its desktop capabilities and borrows
them into one House source lease for its lifetime. Browser/Wasm and query
adapters borrow their host-created clients into the same lease contract.
Step 4 routes complete PackageHouse request/result operations through this
substrate; this slice does not make a candidate or manifest result a complete
House terminal result.

## Package demand

A House request begins with one typed package demand:

- one exact normalized package coordinate;
- one package ID plus an owner-issued version-selection constraint; or
- one already-resolved package edge retaining its declaration, candidate, and
  root-relative correspondence.

Package prefix, keyword search, catalog enumeration, and ecosystem population
planning remain discovery operations. They may produce package demands, but a
prefix or search row is not itself a House package identity.

An unresolved version demand retains its original constraint beside every
selection observation. Resolution issues one exact coordinate or a typed
non-success; it does not replace the request with a display version.

The initial resource-free contract floor in #6433 deliberately exposes only
the exact-coordinate demand. The selecting and resolved-edge arms remain
architectural requirements, but they do not enter the public contract until
their owners issue a version-selection request and resolution receipt, or an
edge correspondence, that PackageHouse can consume without interpreting
selector text.

## Package target context

A request may carry one package-owned target-selection context:

- canonical requested target framework when the operation is target-aware;
- optional runtime identifier;
- the selection mode that states whether the framework is exact or an
  owner-defined default;
- optional correspondence to the typed package input, restored target, or
  dependency edge that supplied the context; and
- optional correspondence to one exact `PlatformFamilyTarget` when another
  owner has already established that relationship.

The package target context is not `PlatformFamilyTarget`.

Package frameworks include .NET Standard, platform-qualified TFMs, and other
NuGet compatibility forms that are not platform target currency. Ordinary
package selection also does not require an exact platform family and version.
Platform correspondence remains an additional fact; equal framework text
cannot manufacture it.

Requested target framework and selected asset-folder framework remain
separate. Compatibility may select a lower applicable folder. The receipt
retains both.

## Operation profiles

Each request authorizes one maximum package work profile:

| Profile | Authorized result |
| --- | --- |
| **Settle** | Exact coordinate, authority-bearing candidate, pruning decision, or typed non-success without package payload bytes |
| **Acquire** | Settled demand plus one authorized package payload and package-content generation |
| **Realize** | Acquired package plus one typed asset-selection result for the requested package target and role |

A profile is an upper work bound, not a promise that every lower stage runs.
Pruning may complete a target-aware request with a platform delegation before
payload acquisition. A cache hit may satisfy acquisition without transport.
`Realize` may validly produce zero selected libraries while preserving
package-shaped content and the asset-selection outcome.

Dependency graph construction remains query-owned. A traversal consumer can
issue `Settle` or `Realize` operations for admitted edges; PackageHouse does
not own graph scheduling, cycle termination, depth, or traversal work budgets.

## Host-authorized plan and operation

The host supplies:

- active package source declarations or explicit source capabilities;
- package-source mapping and authorization;
- any permitted package input capability;
- network and local-source permission;
- cache and payload limits;
- one owner-issued package operation context;
- caller cancellation; and
- any policy generation required by the focused owners.

The plan is capability, not result. Registration does not prove a source
supports the requested operation, a package exists, a version is selectable,
or a payload is authorized.

One House operation consumes one shared operation identity and ceiling across
all selected authorities, source routes, compatibility requests, payload
consumption, and owner adapters. Request timeout may permit owner-authorized
fallback while time remains. Operation timeout is terminal. Caller
cancellation remains cancellation.

The detailed deadline and payload-stream lifetime contracts remain with
`NuGetFetch` and the Package Source Model.

## Settlement order and invariants

The House preserves this semantic order:

1. retain the typed demand and target context;
2. establish or consume version and source-authority evidence;
3. when target-bound pruning is comparable, obtain the pruning result before
   package payload acquisition;
4. either issue one typed platform delegation or retain package processing;
5. acquire only a source-authorized exact payload when the profile permits;
6. select assets only from the acquired package generation and retained target
   context when realization is requested; and
7. issue one terminal result retaining every completed decision and failure.

The order does not require one implementation method or serial execution of
independent read-only observations. It requires that no result claims a later
transition without the owner-issued evidence that authorizes it.

In particular:

- partial source evidence cannot select latest, wildcard, or range when an
  unreadable authority could change the answer;
- `Subsumed` can skip package payload acquisition, while every other pruning
  outcome preserves package processing or visible non-success;
- a discovered coordinate can be acquired only from an authority that
  reported it under the retained discovery contract;
- a caller-pinned coordinate may succeed from one eligible authority without
  proving peer authorities readable;
- an acquired payload does not imply a selected compile or runtime asset;
- selected assets must belong to the retained package content generation; and
- no failure becomes a success-shaped empty package, dependency set, or asset
  set.

## House result and receipts

Every terminal House result retains:

- the original request and operation profile;
- target context and its owner correspondence;
- exact coordinate when one was settled;
- version and configured-authority evidence;
- source, producer, transport, and payload provenance when present;
- pruning result and package-processing decision when applicable;
- package content generation and payload lifetime when acquired;
- requested and selected framework/RID evidence when realized;
- selected package assets and their package-relative identities;
- dependency-edge correspondence when the request came from traversal;
- completion and every typed failure; and
- one owner-issued settlement identity that consumers retain opaquely.

The result contains three separable receipts when the corresponding work ran:

- a **package decision receipt** records coordinate settlement, authority,
  pruning, and the decision to acquire, delegate, or stop; and
- a **package acquisition receipt** binds the retained decision and candidate
  to the selected configured authority, source result and producer, payload
  origin, and package-content generation; and
- a **package realization receipt** binds that acquisition to an
  asset-selection-owner receipt and any library-focused handoffs.

Every terminal arm retains one common immutable evidence envelope containing
the settlement identity, every completed receipt, and every typed failure.
A later no-match, rejection, incompleteness, or failure does not rewrite or
discard an earlier package-retention decision or successful acquisition.
Recovered request-scoped failures may accompany `Settled`; operation timeout
is terminal and cannot. House failure adaptation retains an owner-issued
authority failure and associates it with the House operation; an owner
`PackageSourceTimeoutKind.Operation` remains terminal through that adaptation.
Operation timeout takes precedence over an otherwise successful selection:
`Failed` retains the completed acquisition and realization receipts, including
selected assets or an explicit empty compile group.

Consumers may retain the decision without retaining payload bytes. A disposed
payload or expired artifact session does not erase the historical decision
evidence, but it does make content access visibly unavailable.

The settlement identity does not replace package coordinate, content
generation, Workspace membership, or dependency-edge identity. It associates
them for this operation.

## Terminal outcomes

The closed result distinguishes:

- **Settled** -- the requested profile completed with package evidence;
- **Delegated** -- package pruning issued an authorized platform delegation
  and package payload acquisition was skipped;
- **Not found** -- every required authority established absence for the
  applicable contract;
- **No match** -- available evidence could not satisfy version, target, asset,
  or role selection;
- **Ambiguous** -- complete evidence admits more than one result and policy
  does not choose between them;
- **Rejected** -- request, authority, content, policy, or admission evidence is
  unusable;
- **Unavailable** -- a required capability or source is unavailable;
- **Incomplete** -- usable evidence exists but cannot authorize the requested
  settlement; and
- **Failed** -- operation work failed without a safe partial settlement.

Caller cancellation is thrown with the caller's token and is not a terminal
House result. Operation timeout is a typed terminal failure retaining its
deadline identity.

A `Settled` package may contain zero, one, or many selected libraries.
Package-shaped inspection remains available when its acquired content and
lifetime remain available.

For a completed realization, the asset-selection owner outcome determines the
House terminal family:

- selected runtime or compile assets produce `Settled`;
- an explicit empty compile group produces `Settled` with zero library
  handoffs;
- no assets or no matching target produce `NoMatch`;
- runtime ambiguity produces `Ambiguous`; and
- invalid runtime or implementation-asset evidence produces `Rejected`.

Every non-success mapping retains the completed acquisition and realization
receipts. Constructing a realization receipt is evidence that selection ran;
it does not by itself make the operation successful.

## Target-aware dependency-edge realization

[#6424](https://github.com/richlander/dotnet-inspect/issues/6424) owns the
focused handoff from one resolved dependency edge to target package
realization.

For every edge that a traversal consumer chooses to realize, PackageHouse
retains:

- the source projection and normalized declaration identity;
- the exact candidate and authority correspondence;
- the root-relative edge occurrence when the graph owner supplies it;
- the package target context used for target realization; and
- the target package's requested-versus-selected asset evidence.

The association belongs to the edge occurrence or an owner-issued shared
request referenced by that occurrence. It does not belong solely to the
semantic package node: the same exact coordinate can be reached under
different target contexts.

The defining case is:

```text
Package A realization
  requested framework: net10.0
  selected package assets: net10.0
  dependency edge: B

PackageHouse realizes B for the retained edge context
  available folders: net10.0, net11.0
  requested framework: net10.0
  selected folder: net10.0
```

Calling B with no selection context and choosing its highest folder would
violate the House correspondence. Selecting `net11.0` would violate package
framework applicability. A compatible lower folder may be valid, but its
selected identity remains distinct from the `net10.0` request.

## Package pruning and platform delegation

The package owner invokes the focused platform package supply policy before
payload acquisition when the request has complete comparable target and
package version evidence.

PackageHouse consumes the policy-issued `PlatformSupplyReceipt`; it does not
attach an independently supplied `PlatformSupply` to package and target
labels. Delegation additionally requires the actual supplying shared-framework
family and exact inventory family version to match the retained
`PlatformFamilyTarget`.

Only `Subsumed` authorizes delegation. The package decision receipt retains:

- the original package demand;
- resolved or unresolved version evidence;
- the exact platform target used for comparison;
- the complete pruning result and supplier evidence; and
- the fact that payload acquisition did not run.

The House returns a typed `PlatformDelegation` to application orchestration.
It does not call `PlatformHouse` directly. Orchestration may issue one ordinary
platform request and retains the association between the package decision and
platform settlement receipts.

`NotSubsumed`, `NotComparable`, unavailable, ambiguous, stale, or incomplete
pruning evidence cannot become delegation.

## Package-shaped and library-focused results

A package remains a first-class container subject. Its metadata, dependency
declarations, assets, content entries, source, and processing evidence are not
discarded merely because a consumer also wants assemblies.

Library-focused realization is explicit. It may select zero, one, or many
compile or implementation assets and unwrap each as a provenance-retaining
bare library.

Each unwrapped library retains:

- exact package coordinate and settlement receipt;
- package content generation and package-relative asset identity;
- requested and selected target context;
- compile, runtime, reference, or implementation role;
- producer and acquisition provenance;
- correspondence to any paired compile/implementation asset; and
- the content lease required to keep bytes readable.

The House does not choose one assembly because its file name resembles the
package ID unless the asset-selection owner explicitly defines that role.
Shared Library inspection begins only after this handoff.

## Workspace boundary

`Workspace` owns registration, transactions, revisions, admission, order,
replacement, leases, and participant lifetime. It does not own package source,
version, pruning, payload, or asset-selection policy.

For package realization, Workspace orchestration:

1. forms or receives one typed package demand and target context;
2. supplies host-authorized capabilities and operation limits;
3. calls PackageHouse;
4. retains the House result and receipts;
5. admits only the returned package contribution and selected library
   contributions; and
6. reports or preserves every non-success without reconstructing a neighboring
   package result.

PackageHouse does not mutate Workspace. It returns an immutable realization
with the retained lifetime needed for a Workspace transaction to admit it.
Rejected admission disposes House-owned resources under the artifact owner
contract; an existing Workspace remains unchanged.

An exact package Root reacquisition request from
[#5837](https://github.com/richlander/dotnet-inspect/issues/5837) re-enters the
same House realization path. It retains the original selection request while
allowing a replacement physical generation.

## Host boundaries

The CLI and Inspect Web own:

- command or gesture interpretation;
- source registration and credential input;
- online, offline, and local capability selection;
- operation cancellation and host capacity;
- Workspace lifetime;
- section and row selection; and
- diagnostics and presentation.

They do not own package candidate completeness, version selection, pruning,
payload authorization, target-aware edge realization, or asset-selection
semantics.

Browser/Wasm can supply Gallery, configurable HTTP, in-memory, and supported
local capabilities without receiving a desktop constructor or filesystem
assumption. The CLI can supply installed configuration, package-source
mapping, credential-provider, filesystem, and cache capabilities. Equal
authorized inputs produce the same House package decision and realization
semantics.

This owner carries typed operation data but no rendering model. CLI output
uses Markout through its presentation owners. Inspect Web lowers the same
typed result through its Browser boundary and owns interactive rendering.

## Project and dependency boundaries

The logical House owner may span focused projects. A project name alone does
not define the owner.

The intended dependency shape is:

```text
NuGetFetch and package source capabilities
               |
               v
DotnetInspector.Packages
  identity, source, version, pruning, payload, asset selection
  PackageHouse request/result contracts and lower settlement
               |
               +----------------------+
               |                      |
               v                      v
DotnetInspector.Queries      DotnetInspector.PackageQueries
  evidence and traversal      package/query adapters and composition
               |                      |
               +----------+-----------+
                          |
                          v
              Workspace orchestration
                          |
                 +--------+--------+
                 |                 |
                 v                 v
           DotnetInspect.Cli  DotnetInspect.Web
```

The first implementation may keep host-neutral PackageHouse source settlement
inside `DotnetInspector.Packages`, replacing the
`DesktopPackageSourceComposition` identity. Query adapters that require both
`DotnetInspector.Packages` and `DotnetInspector.Queries` belong in
`DotnetInspector.PackageQueries` only when the boundary investigation in
[#6432](https://github.com/richlander/dotnet-inspect/issues/6432) confirms that
ownership; this design does not pre-decide whether that project remains,
merges, or decomposes.

`DotnetInspector.Queries` does not depend on a higher project merely to keep
its evidence and traversal algorithms usable. PackageHouse does not introduce
a generic Houses assembly. Exact moves from `DotnetInspector.Services` remain
tracked by [#6335](https://github.com/richlander/dotnet-inspect/issues/6335).

## Current implementation retirement

Retirement is staged:

1. introduce the House contract and host-neutral source capability
   construction;
2. move existing package operations behind the House without behavior change;
3. adopt Workspace, CLI, and Browser/Wasm one owner at a time;
4. remove direct host package-policy paths after their replacement is live;
5. remove the `DesktopPackageSourceComposition` identity; and
6. close or supersede #4653 after every carried requirement and unresolved
   finding is assigned to a landed focused slice or explicit residual issue.

No compatibility facade or obsolete CLI path is retained solely to preserve
the old architecture. A direct path remains only while it is the shipping path
for a supported scenario.

## Composition evidence

The Package Source Model's TLA+ model continues to own concurrent authority,
route fallback, association, completeness, and operation-timeout behavior.
PackageHouse consumes those checked outcomes rather than copying the source
state machine.

The initial House adds deterministic evidence-preserving orchestration. No new
TLA+ model is selected for the design slice. An implementation that introduces
concurrent pruning, settlement, acquisition, or realization transitions must
first define the new scheduling property and compose or extend the existing
model rather than relying on tests to explore races.

The sole-facade composition boundary uses positive outcome canaries and design
review. Repository-wide absence of bypasses is **unverified**; no automated
source or compiled-graph scan is required. Each adoption proves its
representative product path and retires the direct path it replaces.

## Pathological cases

### Dependency offers a newer asset folder

Package A is realized for `net10.0` and reaches package B. B contains
`net10.0` and `net11.0` asset folders. The edge realization retains
`net10.0`, selects B's `net10.0` assets, and records requested and selected
frameworks separately.

### Same package coordinate, different target contexts

Two roots reach `Contoso.Json@4.0.0`, one for `net8.0` and one for `net10.0`.
The semantic coordinate can be shared, but the realization receipts and
selected asset universes remain distinct. A node cache keyed only by coordinate
cannot answer both.

### Partial sources cannot select latest

One configured authority reports `4.0.0`; another required authority times
out. The House may return partial discovery evidence, but it cannot settle
`latest`, a wildcard, or a range when the unread authority could change the
answer.

### Package is supplied by Platform

A target-bound `System.Text.Json` package demand is proven `Subsumed`. The
House skips package payload acquisition and returns a platform delegation plus
the complete package decision receipt. It does not infer
`System.Text.Json.dll` or call PlatformHouse directly.

### Package contains multiple libraries

One package realizes three selected compile assets. Package-shaped inspection
retains the container and all asset evidence. A library-focused request emits
three provenance-retaining library handoffs; the House does not silently pick
one by enumeration order.

### Package contains no applicable library

The payload is valid package content but the requested target has no applicable
compile group. The result preserves package inspection and returns typed asset
`No match`; it does not become package acquisition failure or a success-shaped
empty library.

### Browser and CLI use equivalent authorities

Browser Gallery and CLI NuGet.org capabilities report equivalent
authority-bearing package evidence under their owner contracts. PackageHouse
applies the same candidate, version, pruning, and realization semantics while
retaining their distinct producer and transport provenance.

### Retired source lease retains evidence, not authority

A source lease issues an exact candidate and settles a manifest failure. The
host retires the lease. The candidate and failure remain inspectable evidence,
but another candidate resolution or manifest operation through that lease is
rejected. The source client remains alive because its host, not PackageHouse,
owns it.

### Direct library bypasses PackageHouse

A user supplies one DLL path. It enters shared Library inspection with direct
provenance. It is not wrapped in a package request merely to create symmetry.

### Platform pack remains Platform-shaped

PlatformHouse acquires a targeting or runtime pack through a package-backed
source adapter. The pack ID and archive remain source coordinates for that
platform operation; no ordinary Package participant or PackageHouse result is
manufactured unless a separate explicit package demand exists.

## Analogous implementation evidence

Three existing designs bound this House:

- the Package Source Model already demonstrates package-owned aggregation over
  multiple typed source clients and exact authority association;
- PlatformHouse demonstrates a peer container/source facade that preserves
  target and source evidence while leaving lower algorithms with their owners;
  and
- artifact Workspace sessions demonstrate immutable contribution and lifetime
  handoff without moving admission policy into a source owner.

NuGet asset selection also demonstrates the central target-context distinction:
one requested target chooses one applicable package asset universe. PackageHouse
preserves that request across package boundaries; it does not copy NuGet's
restore graph or compatibility implementation.

The analogy is not authority. Packages remain independently published
containers selected through configured authorities. Platform is a coherent
family realization. Their Houses converge only at typed orchestration and the
provenance-retaining Library handoff.

## Adoption plan

[#6426](https://github.com/richlander/dotnet-inspect/issues/6426) owns eleven
counted production-adoption steps:

1. Lock this focused PackageHouse composition design and adoption/retirement
   plan.
2. Define the resource-free request, operation, result, receipt, and
   package-to-library/platform handoff contracts.
3. Generalize `DesktopPackageSourceComposition` to the House-issued,
   host-neutral source-settlement lease over injected source capabilities
   tracked by #6477.
4. Route version settlement, candidate authorization, manifest acquisition,
   and payload acquisition through the House.
5. Adopt normalized package input and processing evidence from #6266.
6. Apply package-owned pruning before payload acquisition and issue typed
   platform delegation.
7. Adopt target-aware dependency-edge realization from #6424.
8. Route package Root construction, durable reacquisition, multi-package
   realization, and participant lifetime through shared Workspace
   orchestration.
9. Adopt the House in CLI package operations and dependency expansion.
10. Adopt the same contracts in Inspect Web Browser/Wasm.
11. Move remaining package components from `DotnetInspector.Services`, remove
    direct host bypasses and the desktop composition identity, and close or
    supersede #4653.

Each step after this design is separately reviewed and leaves a usable
product. A direct path is retired only after its House replacement is live in
every supported host that uses it.

## Required gates

| Claim | Required Release evidence |
| --- | --- |
| Exact implementation floor | The public demand family exposes only the exact-coordinate arm until a version-owner request and resolution receipt exist. |
| Request association | A result retains the exact demand, operation, target context, and owner-issued settlement identity without reconstructing them from display values. |
| Terminal evidence | Every terminal arm retains the same immutable evidence envelope, completed receipts, and typed failures; direct and owner-adapted operation timeouts cannot produce success. |
| Source lease authority | One lease owns one candidate issuer; candidates from another lease and clients or results from another configured-authority association are rejected. |
| Source lease retirement | Retirement rejects new source settlement while completed source evidence remains readable. |
| Source capability ownership | Retiring a House source lease does not dispose caller-owned clients or caller-supplied operation contexts. |
| Source completeness | Partial authority evidence cannot settle latest, wildcard, range, or authoritative absence. |
| Pruning order | `Subsumed` skips payload acquisition and every other pruning state cannot issue platform delegation. |
| Pruning correspondence | Platform delegation consumes the policy-issued inventory/coordinate/supply receipt, compares the coordinate through the package owner's normalization, and matches the actual supplier family and exact target version. |
| Payload authority | Discovered payload comes only from a reporting authority; pinned payload follows the Package Source Model's eligible-authority rule. |
| Selection correspondence | Realization consumes a selector-issued generation/request/outcome receipt matching the exact acquisition and target context. |
| Selection completion | Selected assets and explicit empty compile groups settle; no-match, ambiguity, and invalid outcomes map to their corresponding terminal arms without losing receipts. |
| Target-aware realization | A `net10.0` dependency with `net10.0` and `net11.0` folders selects `net10.0` and retains requested-versus-selected evidence. |
| Context separation | The same coordinate realized under two target contexts retains two realization receipts and cannot share one selected asset universe. |
| Package shape | Zero, one, and many selected library outcomes preserve package-shaped inspection and typed asset status. |
| Workspace handoff | Admission consumes one immutable House realization; rejection leaves the prior Workspace unchanged and disposes rejected resources correctly. |
| Platform delegation | Package and platform receipts remain separately typed and associated by orchestration without a House-to-House call. |
| Host equivalence | CLI and Browser/Wasm canaries over equivalent owner-issued inputs observe the same House settlement semantics. |
| Retirement | Each adopting owner has a positive House-path canary before its direct path is removed. Repository-wide bypass absence remains unverified. |

Focused owner suites enforce their own algorithms. House tests compose public
owner outcomes; they do not manufacture package evidence or inspect private
test seams.

## Demo

### Target context crosses a dependency edge

```text
Workspace package request
  package: A@1.0.0
  target: net10.0

PackageHouse
  settle A -> authorized A@1.0.0
  acquire A -> package generation A1
  select A assets -> lib/net10.0
  traverse selected dependency edge -> B
  settle B -> authorized B@2.0.0
  realize B with retained target net10.0

B package folders
  lib/net10.0
  lib/net11.0

result
  requested: net10.0
  selected: lib/net10.0
  edge and package realization receipts retained
```

### Package exits through platform delegation

```text
PackageHouse request
  package: System.Text.Json@10.0.0
  exact platform target: DotNetRuntime/net11.0/11.0.0

pruning: Subsumed
payload acquisition: skipped

PackageHouse result
  Delegated
  package decision receipt
  typed PlatformDelegation

application orchestration
  -> ordinary PlatformHouse request
  -> separately retained platform settlement receipt
```

### Package remains a container

```text
PackageHouse realization
  package: Contoso.Tools@3.0.0
  selected libraries:
    Contoso.Tools.dll
    Contoso.Tools.Abstractions.dll

package inspection
  retains nuspec, dependencies, content, assets, and source evidence

library-focused handoff
  emits two libraries with package provenance
```

## Non-claims

This design does not:

- define one universal package input format;
- make PackageHouse a parser, transport, cache, query, or Workspace bucket;
- change NuGet framework compatibility or restore behavior;
- define dependency graph traversal, version selection, pruning comparison, or
  asset-selection algorithms;
- make package search, package prefixes, or ecosystem registration House
  identities;
- imply one assembly per package;
- infer package ownership from assembly names, paths, namespaces, or display
  text;
- turn `PlatformFamilyTarget` into package framework currency;
- call PlatformHouse directly or absorb platform settlement;
- make platform distribution packs ordinary Package participants;
- define CLI flags, Browser views, Markout schemas, or presentation;
- add a generic Houses project;
- require a repository-wide source or dependency-graph bypass scan; or
- claim #4653's unresolved findings are fixed until focused replacement slices
  land their corresponding behavior.
