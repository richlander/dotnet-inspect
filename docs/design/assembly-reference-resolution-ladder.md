# Assembly reference resolution ladder

## Status and approved scope

This document is the normative owner for one host-neutral assembly-reference
resolution ladder. It is tracked by
[#6288](https://github.com/richlander/dotnet-inspect/issues/6288) as effort 11
of the non-normative platform-first tracker
[#6228](https://github.com/richlander/dotnet-inspect/issues/6228).

The first production consumer is progressive call-graph construction in both
the CLI and Inspect Web. The ladder is shared substrate; it is not a
call-graph-specific result or a host-specific retry loop.

## Authority and exact claim

**Assembly Reference Resolution Ladder** owns:

> Given one exact `AssemblyRef`, its referencing assembly-context origin, one
> revision-bound owner-issued route plan, and finite operation work, evaluate
> the referencing context first, then lazily form and evaluate the complete
> applicable-platform and package-dependency route set, and return one typed
> resolved target or one visible terminal non-success outcome.

The exact claim is composition, not discovery by convention. The ladder
consumes:

- Metadata-owned assembly identity, binding request, policy version, selection,
  miss-disposition, ambiguity, and failure contracts;
- Workspace-owned origin, revision, route eligibility, context generation,
  realization, replacement, and operation authorization;
- platform-owned target, library membership, acquisition, and realization
  evidence;
- Platform/Package Pruning's exact package-subsumption comparison;
- Package Dependency Evidence's normalized declarations, edge identities,
  origin association, and completion;
- Package Dependency Candidate Query's exact source-authorized candidate;
- package-owned payload acquisition, target-framework and runtime-asset
  selection, package-role realization, and assembly correspondence; and
- source and acquisition owners' typed failures, deadlines, cancellation, and
  capacities.

The ladder does not reconstruct any of those facts from paths, labels, names,
or successful transport.

## Why this owner is needed

Workspace call graphs currently bind only among participants already present
in one `AssemblyContextGroup`. `MemberCallGraphSession` can build a
cross-library catalog over that group, but it neither resolves nor acquires a
missing platform or package assembly.

Inspect Web compensates for platform calls in
`PlatformCallGraphExports.BuildCallGraphAsync` by:

1. running the call graph;
2. inspecting unexpanded platform call sites;
3. mapping their assembly names to packs;
4. acquiring more assemblies;
5. replacing the browser workspace; and
6. rerunning until the assembly-count limit is reached.

That loop demonstrates the required experience, but it is not the contract.
It is platform-only, host-specific, inferred after graph failure, and cannot
consume package dependency evidence. The CLI and other queries do not receive
the same behavior.

The legacy desktop `AssemblyDependencyResolver` has an ordered resolver, but
it is not this owner either. It combines local filesystem probing, installed
runtime state, nuspec parsing, project documents, corpus search, and compiler
reference selection. Those ambient and tools-oriented assumptions are not
portable to Browser/Wasm and do not preserve the Workspace's owner-issued
route and generation identities.

## Binding semantics remain Metadata-owned

[Structured Type-Forwarding Resolution](type-forwarding-resolution.md) owns
the binding result algebra and the only legal policy-tier continuation:

| Delegated binding selection | Ladder action |
| --- | --- |
| `Selected` | Stop with that selection |
| `Ambiguous` | Stop with ambiguity |
| `Unavailable` | Stop with the exact failure |
| `Rejected` | Stop with the exact rejection |
| `NameOwnedNoMatch` | Stop with the owner-attested miss |
| `Undifferentiated` | Stop; legacy absence cannot authorize fallthrough |
| `NoNameOwner` | Advance to the next eligible rung |

The ladder neither weakens identity matching nor promotes an inactive or
identity-ineligible candidate. It validates every delegated snapshot against
the exact request and captured policy version. A foreign or changed snapshot
cannot be interpreted as a miss.

`NoNameOwner` is not candidate evidence. It only proves that one rung's
complete frozen ownership rule does not own the requested name. The final
ladder may issue `NoNameOwner` only after every eligible rung returns that
disposition. An omitted, unexamined, failed, or bounded rung prevents that
absence claim.

## Contract shape

```text
exact AssemblyRef + exact referencing origin
  + Workspace revision and focal-scope receipt
  + owner-issued route plan
  + finite operation ledger and cancellation
        |
        v
Assembly Reference Resolution Ladder
  1. referencing context
  2. lazy external-route formation
  3. applicable platform
  4. package dependency routes
        |
        +-- acquisition needed
        |     -> owner acquisition and realization
        |     -> Workspace replacement generation
        |     -> continue under the replacement receipt
        |
        v
one closed result
  - resolved target
  - unbound binding with exact miss disposition
  - ambiguous
  - unavailable
  - rejected
  - incomplete
```

The operation may cross immutable Workspace generations, but no binding policy
or query mutates a sealed `AssemblyContextGroup`.

## Exact request

`AssemblyReferenceResolutionRequest` is conceptual vocabulary for one
immutable request. It carries:

- the complete `AssemblyReferenceIdentity` decoded from one exact
  `AssemblyRef`;
- the exact Metadata-owned `AssemblyBindingRequest`, including its binding
  target, seed-or-continuation origin, resolver lineage, and scope;
- the requesting `AssemblyBindingOccurrence`, referencing
  `ResolvedAssemblyReference`, acquisition registration, and
  `AssemblyContextGroup`;
- the exact policy-version snapshot required by Metadata;
- the Workspace revision and current context-generation identity;
- one owner-issued focal-scope receipt defining which routes are eligible;
- one `AssemblyReferenceResolutionRoutePlan`;
- one finite work ledger and shared operation context; and
- caller cancellation.

The binding origin is semantic. Reconstructing a continuation from a package
name, assembly simple name, acquisition registration, path, currently selected
UI subject, or graph node label is invalid. The ladder preserves the exact
requesting occurrence and resolver lineage through delegated selection.

The focal-scope receipt is also semantic. It states which source routes this
operation may consider under the selected call-graph focal length or another
consumer's explicit scope. The ladder validates and consumes it; it does not
derive focal-length meaning.

## Route-plan and external-route currency

`AssemblyReferenceResolutionRoutePlan` is an immutable, revision-bound receipt
issued for the exact referencing origin and operation scope. It contains:

- the required referencing-context rung;
- one deferred owner capability for forming external routes after a context
  miss;
- identities for every contributing owner snapshot available before external
  work; and
- the operation and Workspace identities to which the plan belongs.

The ladder evaluates the context rung before invoking the deferred capability.
A context selection, ambiguity, owned miss, unavailable result, or rejection
performs no platform catalog, pruning, package candidate, package traversal,
asset, source, or acquisition work for this request.

Only after the context returns `NoNameOwner` does the deferred owner form
`AssemblyReferenceExternalRouteSet`. That immutable set contains:

- zero or one applicable-platform rung;
- an ordered finite collection of root-relative package route occurrences;
- one owner-issued platform/package overlap-applicability receipt for the exact
  request;
- exact external-route and package-reachability completion;
- identities for every contributing owner snapshot; and
- the operation and Workspace identities to which the set belongs.

Package-route order preserves dependency evidence and diagnostics. It is never
first-match precedence. Platform is a single rung even when its realization
contains several framework families or packs.

An external route set is **complete** only when its issuing owners establish
that every route eligible for this exact origin and focal scope is represented,
including every package node reachable under the selected root-relative
dependency traversal. A provider may instead issue typed incomplete evidence
with the routes it could establish. The completed context miss remains usable,
but the ladder cannot advance through an incomplete boundary and later claim
resolution or absence if an unrepresented route could change the result.

Equal display fields do not establish route correspondence. Equality and
validation use owner-issued revision, origin, declaration, candidate,
platform-target, realization, and generation identities.

The overlap-applicability receipt is complete before a platform rung can
select. It states one of:

- **Platform independent** — the exact platform library is applicable and the
  issuer proves that no request-eligible non-subsumed package route is
  associated with this exact `AssemblyRef`;
- **All associated package edges subsumed** — the complete non-empty associated
  route set carries `Subsumed` pruning receipts for every request-relevant
  package edge, so those edges are delegated to the platform;
- **Package edge retained** — one or more associated package routes are
  `NotSubsumed`, `NotComparable`, or otherwise package-owned, so no same-request
  platform rung is eligible on their behalf; or
- **Undetermined** — available evidence cannot settle overlap, so route
  formation is incomplete.

Association is owner-issued. It is not inferred from equal package and
assembly names. Forming the receipt may require bounded package candidate,
asset, restored-graph, or platform-catalog evidence after the context miss and
before external binding rungs run. That preparatory work remains charged to
the operation and preserves each owner's result.

## Rung 1: referencing context

The first rung is the exact binding-consistent context containing the
referencing assembly. It consumes that group's
`SourceRelativeAssemblyGroupBindingPolicy` or an equivalent completed
Workspace policy.

The rung evaluates the exact request before any source, package, platform, or
replacement work:

- one identity-eligible participant is selected;
- several eligible participants are ambiguous;
- a participant inventory that owns the simple name but has no
  identity-eligible candidate returns `NameOwnedNoMatch`; and
- a complete context with no owner for the name returns `NoNameOwner`.

The containing package's selected asset group normally enters here, not as a
later package edge. Package realization retains all selected role participants
and their exact surface-to-implementation correspondence, so an intra-package
reference does not rediscover its own package through NuGet.

A same-named package or platform candidate cannot override this rung merely
because it has a newer version, a preferred source, or a more familiar label.
The current context owns its binding domain. In particular, a local
same-name/public-key or version mismatch is terminal rather than permission to
fall through to a package with that name.

## Rung 2: applicable platform

The platform rung exists only when an owner-issued route proves that platform
resolution is applicable to this exact request, origin, target, and operation
scope. Platform registration alone is relevance input; it is not enough to
mint a binding route.

One route carries:

- an exact platform target and family composition;
- a platform-owner-issued name-ownership and library-membership projection;
- the acquisition and realization capability for the platform owner's chosen
  realization unit;
- the exact Workspace and source-policy generations; and
- when the route substitutes for a package edge, the exact pruning receipt and
  package-declaration association that authorize that substitution.

There are two ordinary ways to issue the route:

1. the platform catalog and exact target establish a platform library, and the
   overlap-applicability issuer proves that no request-eligible package route
   is associated with this exact request; or
2. the complete non-empty set of associated package dependency edges is
   `Subsumed` by
   [Platform/Package Pruning](platform-package-pruning.md), and the composition
   owner retains every edge as visible delegated-to-platform evidence.

`NotSubsumed` does not create a platform substitution. A package version above
the target's prune watermark remains a package route even when the platform
contains a same-named library. `NotComparable` also cannot authorize
substitution. Any retained associated edge dominates every same-request
platform route, including one proposed by another subsumed edge. Absent
complete no-overlap or all-associated-subsumed evidence, route formation is
incomplete rather than platform-preferred.

This distinction keeps package version and assembly version separate.
The ladder never compares an `AssemblyRef` version with a NuGet package
version, and it never treats catalog name overlap as pruning evidence.

Once issued, the platform rung owns its complete name decision:

- no supplied platform library owns the name: `NoNameOwner`;
- the platform owns the name but identity policy selects none:
  `NameOwnedNoMatch`;
- one eligible platform participant: `Selected`;
- several eligible platform participants without an owner-issued precedence:
  `Ambiguous`; or
- unavailable, rejected, or bounded realization: the corresponding terminal
  result.

Missing Platform is not rewritten to a package fallback. If the exact scope
requires an applicable platform route but its target, catalog, source,
realization, or acquisition evidence is unavailable, the result says so.

## Rung 3: package dependency routes

The package rung considers only owner-issued package nodes reachable from the
referencing origin under a complete root-relative package dependency
projection. It never searches NuGet by assembly simple name, namespace,
ecosystem, package prefix, or display text.

Each `PackageDependencyAssemblyRoute` carries:

- the exact referencing-origin association;
- one owner-issued root-relative reachability path;
- the exact declaring parent and dependency edge at the path's terminal step;
- the selected target-framework and runtime intent owned by the input;
- its package candidate capability and shared source operation context;
- its route eligibility and optional ecosystem membership receipt; and
- exact completion and identity for every contributing owner.

The route has two disjoint evidence arms:

- **Declaration-backed** — carries one normalized
  `PackageDependencyEvidenceDeclaration`. Package Dependency Candidate Query
  owns version selection or restored-coordinate validation against that
  declaration.
- **Restored-coordinate-backed** — carries one owner-issued produced
  relationship, requested constraint when available, and exact resolved
  `RestoredProjectPackageNodeIdentity` or equivalent canonical
  `PackageSourceCoordinate`. It does not carry or synthesize a normalized
  declaration.

The route does not relabel a transitive package as a direct declaration and
does not claim that its package contains the requested assembly. It preserves
the actual declaring parent and path while establishing why that package node
is request-eligible. Evaluation then composes existing owners:

1. Package Dependency Traversal or an authoritative restored graph supplies
   root-relative reachability, exact edges, candidates, and completion without
   manufacturing a direct declaration.
2. For a declaration-backed route, Package Dependency Candidate Query resolves
   the normalized declaration to one exact `PackageAcquisitionCandidate`.
3. For a restored-coordinate-backed route, Package Source Model authorizes the
   owner-issued exact coordinate as a caller-pinned candidate without version
   discovery, declaration synthesis, or constraint re-selection. The route
   retains the produced relationship and requested-versus-resolved evidence.
4. The package owner acquires the exact authorized payload.
5. Package asset selection chooses the compile and implementation roles for
   the exact target and runtime intent.
6. Package role realization issues the selected asset set, assembly
   identities, and surface-to-implementation correspondence.
7. A package-rung binding policy evaluates the original `AssemblyRef` against
   that complete realized role.

The ladder does not choose a package version, target framework, asset folder,
reference-versus-implementation role, feed, or payload. It does not treat
`PackageCompileAssetSelection.DefaultAsset` as the answer to an arbitrary
assembly reference.

The restored-coordinate arm is not a shortcut from display text to authority.
The produced-relationship owner supplies the exact canonical coordinate and
edge identity; Package Source Model still supplies current source
authorization and the `PackageAcquisitionCandidateCorrespondence`. Missing,
foreign, or incomplete restored graph evidence remains incomplete rather than
falling back to a synthesized declaration.

### Complete package-rung decision

Every request-eligible package route that could affect the binding decision
must settle before the package rung publishes a conclusive result. This is the
cost of making ambiguity and absence truthful:

- zero complete eligible routes, or complete routes whose realized roles do
  not own the name, produce `NoNameOwner`;
- one identity-eligible assembly across all complete routes produces
  `Selected`;
- several identity-eligible assemblies produce `Ambiguous`;
- one or more complete roles own the name but none has an identity-eligible
  assembly produce `NameOwnedNoMatch`; and
- incomplete source discovery, candidate selection, acquisition, asset
  realization, identity decoding, or route enumeration produces `Incomplete`,
  `Unavailable`, or `Rejected` according to the owning failure.

A first observed match is provisional until every route capable of producing a
peer match settles. Budget exhaustion after one match is incomplete evidence,
not success. An incomplete or bounded root-relative dependency closure prevents
definitive package-rung absence or selection when an unexamined node could
supply another candidate. Repeated declarations and paths that resolve to one
corresponding package candidate may share acquisition and realization work
while retaining distinct edge and reachability occurrences.

Package filename is neither package identity nor assembly-name ownership.
`PackageCompileAsset.AssemblyName` is currently derived from the selected
asset's filename and cannot exclude an asset from a complete binding domain.
A package route may avoid decoding unrelated selected assets only when a
separate owner-issued inventory has validated their metadata assembly names.
Otherwise every potentially contributing selected asset is identity-decoded
before the rung can claim `NoNameOwner`, `NameOwnedNoMatch`, one selection, or
ambiguity. A selected `Alias.dll` whose metadata identity is `Contoso.Real` is
valid and participates under `Contoso.Real`; filename and metadata-name
difference is not rejection. Rejection is reserved for malformed metadata,
failed content or selected-asset correspondence, or disagreement with a
separately owner-attested metadata identity.

## Immutable generation realization

External resolution is asynchronous acquisition and Workspace realization,
not a synchronous side effect inside `IAssemblyBindingPolicy.Select`.

When a platform or package route needs a participant that is not in the
current group:

1. the ladder records an acquisition demand associated with the exact request,
   route, operation, Workspace revision, and current generation;
2. the ladder suspends and returns control to the Workspace owner rather than
   ordering acquisition, preparation, retirement, or publication itself;
3. the Workspace atomically stops new admission to the old generation before
   replacement preparation begins, following the existing
   retire-before-prepare lifecycle;
4. a later authorized replacement demand acquires and realizes each owner's
   complete chosen unit, constructs a complete route map and binding policy,
   and publishes the replacement under the Workspace contract; and
5. the ladder continues the same logical request only after validating the
   replacement receipt, fresh owner-issued binding occurrence, resolver
   lineage, and new generation identity.

The continuation carries semantic request identity and consumed work. It does
not reuse descriptors, binding occurrences, resolver lineages, policy
snapshots, or query caches from the prior generation. The replacement
Workspace issues fresh seed occurrences and continuations; the ladder validates
their correspondence to the logical request rather than reconstructing lineage
from a registration. A changed Workspace revision, focal-scope receipt,
source-policy generation, route plan, external route set, or replacement
correspondence ends the attempt as typed incomplete or rejected evidence. It
does not silently restart under new user intent.

The Workspace owner decides the atomic replacement unit. The ladder cannot
append one participant to a sealed group, shorten a package role to the one DLL
that happened to match, or split a coherent platform realization. It consumes
the owner-issued replacement context after publication.

This shared generation loop replaces host-specific acquire-and-rerun behavior.
A call-graph consumer may restart its generation-bound graph session after a
successful replacement, but graph traversal, cache reuse, and final projection
remain owned by Call Graph and Queries.

## Finite work

Every request carries finite caller-selected maxima for:

- package route occurrences examined;
- package candidate and source operations;
- payload and platform acquisitions;
- realized assemblies;
- compressed or transferred bytes where known;
- expanded and retained assembly bytes;
- Workspace replacement generations; and
- the shared operation deadline.

The caller and host own the numeric policy. The ladder owns consistent charging
and the rule that permissive scope is not unbounded execution.

Work is charged before the operation that can consume it, with checked
arithmetic. Owner-specific capacities may be narrower and retain their exact
failure. Cancellation remains `OperationCanceledException`; it is not
converted to a successful partial result.

Exhaustion returns `Incomplete` with the exact stage, configured maximum,
consumed work, completed rung prefix, and any safely retained acquisition or
binding evidence. It never becomes final `NoNameOwner`, `NameOwnedNoMatch`, or
`Selected` when omitted work could change that result.

An earlier terminal rung performs no lower-rung work. Resolving in the
referencing context therefore remains cheap even when `Everything` is the
consumer default.

## Result algebra

`AssemblyReferenceResolutionOutcome` has one closed arm:

- **Resolved** — the exact request, selected `ResolvedAssemblyReference`,
  selected `AssemblyBindingOccurrence` and resolver lineage, selecting rung,
  final Workspace generation, binding-policy snapshot, route and realization
  correspondence, completed rung trace, and work receipt.
- **Unbound** — the exact terminal `AssemblyBindingMissDisposition`, issuing
  rung, completed rung trace, final generation, external-route completion, and
  work receipt.
- **Ambiguous** — every active contender in owner order, selecting rung,
  inactive shadow evidence when supplied by the binding owner, and the same
  association fields.
- **Unavailable** — the exact owner-issued acquisition, source, platform,
  package, image, policy, or generation failure and completed evidence prefix.
- **Rejected** — invalid request, correspondence, owner snapshot, identity,
  route-plan, external-route, policy result, or replacement-generation
  evidence.
- **Incomplete** — external-route formation, package reachability, source,
  acquisition, identity, work-ledger, or generation supersession prevented a
  conclusive result.

The rung trace records each attempted rung's exact selection and supporting
receipts. It does not expose credentials, live streams, mutable package
content, policy capabilities, or reconstructable authorization.

`Resolved` is generation-bound. A later Workspace replacement does not mutate
it into a result for the new generation. Consumers may retain its resource-free
diagnostic evidence, but physical access and query reuse follow the artifact
and Workspace generation owners.

## Focal-length composition boundary

[Workspace Registration and Call-Graph Focal Length](workspace-registration-and-call-graph-scope.md)
owns `Self`, `SelfAndRegisteredEcosystems`, and `Everything`. This ladder does
not reinterpret those values as simple rung numbers.

The call-graph population owner projects a focal length and exact Workspace
revision into the route-eligibility receipt:

- `Self` normally supplies only the containing context's routes;
- `SelfAndRegisteredEcosystems` may add platform and package routes whose
  destinations are established by owner-issued registered-ecosystem
  membership; and
- `Everything` may supply every source-authorized route available within the
  Workspace and operation bounds.

Exact-library and package-prefix registrations can contribute populations
without proving that one package dependency edge satisfies one `AssemblyRef`.
The ladder still requires origin-bound platform or package route evidence.

Registration remains inert. Forming the external route set may perform bounded
catalog or dependency-evidence work only after an operation selects that
population and the context rung returns `NoNameOwner`; it does not mutate
registrations or admit Roots.

## Pathological cases

### Context and package share a name

The context contains exact `Contoso.Protocols, Version=2.0.0.0`. A package edge
could acquire another exact identity with the same name. Rung 1 selects the
context participant and the package route performs no work.

If the context instead owns `Contoso.Protocols` with an incompatible public key
or version under its policy, rung 1 returns `NameOwnedNoMatch`. The package
does not replace that terminal owned miss.

### Platform and package overlap

For `System.Text.Json` on an exact .NET target:

- a package edge at or below the target's prune watermark may issue visible
  delegated-to-platform evidence and an applicable platform route only when
  every associated request-relevant edge is likewise `Subsumed`;
- a package edge above the watermark is `NotSubsumed`, remains a package
  route, and is not silently replaced by the platform assembly; and
- an absent or unparseable package version is `NotComparable`, which cannot
  authorize platform substitution.

If two associated routes straddle the watermark, the retained edge dominates
the subsumed edge and no platform route may select on their behalf.

No comparison between assembly version and package version occurs.

### Dependency evidence does not prove assembly correspondence

A package declares `Contoso.Transport`, but its selected role contains no
assembly owning the requested name. That complete route contributes
`NoNameOwner`; the declaration alone never binds the `AssemblyRef`.

An authoritative restored path may reach an exact `Contoso.Transport`
coordinate without carrying a normalized declaration. The
restored-coordinate-backed route authorizes that exact coordinate through
Package Source Model and retains the real parent and requested-versus-resolved
edge evidence; it does not invent a declaration to call Package Dependency
Candidate Query.

### Several packages contain the same assembly

Two eligible dependency packages realize the same exact assembly identity.
The package rung returns `Ambiguous` unless an adjacent owner has issued an
explicit precedence contract. Declaration order, feed order, acquisition
completion order, and package-name similarity do not choose one.

### Platform is required but unavailable

The external route set contains an applicable platform route, but the exact
target or authorized acquisition cannot settle. The result is `Unavailable` or
`Incomplete` with that cause. It does not fall through to a package merely
because a package by a similar name exists.

### Budget expires after earlier misses

The context and platform rungs return `NoNameOwner`; package route evaluation
then exhausts its candidate or byte budget. The result is `Incomplete` with
the two completed misses and bounded package evidence, not final unbound
binding.

## Analogous designs

The .NET managed assembly loading algorithm establishes two useful
conventions:

- the active `AssemblyLoadContext` is selected from the referring assembly for
  a static reference; and
- load-by-name follows an ordered chain of cache, context policy, default
  probing, and resolution handlers.

See
[Managed assembly loading algorithm](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/loading-managed)
and
[About AssemblyLoadContext](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext).
dotnet-inspect follows the origin-relative and explicitly ordered shape. It
deliberately diverges by never loading inspected code, by preserving typed
non-success evidence, and by crossing immutable inspection generations rather
than mutating a runtime load context.

MSBuild's `ResolveAssemblyReference` likewise resolves strong or simple
assembly names through an explicit ordered `SearchPaths` input and reports
conflicts rather than defining one universal filesystem probe. See
[ResolveAssemblyReference task](https://learn.microsoft.com/en-us/visualstudio/msbuild/resolveassemblyreference-task).
dotnet-inspect adopts the explicit-route principle but does not copy compiler
reference discovery, copy-local policy, GAC behavior, binding redirects, or
filesystem search.

NuGet selects package versions and package assets in separate phases.
`PackageReference` restore first resolves the dependency graph, then emits the
selected compile and runtime assets in `project.assets.json`; packages may
separate `ref` and `lib` assets by target framework. See
[Package dependency resolution](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution),
[PackageReference in project files](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files),
and
[Supporting multiple target frameworks](https://learn.microsoft.com/en-us/nuget/create-packages/supporting-multiple-target-frameworks).
The ladder preserves that separation: a dependency declaration selects a
package candidate, package owners select assets, and only decoded assembly
identity can satisfy the `AssemblyRef`.

These systems are evidence, not authority. The normative behavior remains the
owner contracts named above.

## Demo

An ordinary platform reference:

```text
request
  origin: Contoso.App/lib/net11.0/Contoso.App.dll
  AssemblyRef: System.Runtime, Version=11.0.0.0
  focal length: Everything

ladder
  context
    NoNameOwner
  platform
    target: Microsoft.NETCore.App 11.0.x
    selected: System.Runtime, Version=11.0.0.0
    acquisition: realized in replacement generation 42

result
  Resolved
  rung: platform
  generation: 42
```

The overlapping newer-package neighbor:

```text
request
  origin: Contoso.App/lib/net11.0/Contoso.App.dll
  AssemblyRef: System.Text.Json, Version=12.0.0.0
  package edge: System.Text.Json [12.0.0]
  platform prune watermark: 11.0.0

ladder
  context
    NoNameOwner
  platform substitution
    not applicable: package edge is NotSubsumed
  package
    candidate: System.Text.Json 12.0.0
    selected role: ref/net11.0 + implementation correspondence
    selected: System.Text.Json, Version=12.0.0.0

result
  Resolved
  rung: package dependency
```

What to notice: broad scope permits the package route, but neither the assembly
name nor platform overlap chose a package or erased the exact pruning result.

## Ownership and adoption

| Participating owner | Responsibility retained |
| --- | --- |
| [Structured Type-Forwarding Resolution](type-forwarding-resolution.md) | Assembly identity, binding request and result algebra, policy versions, name ownership, candidate domains, and fixed-chain composition rules |
| Workspace and [Artifact Acquisition](artifact-acquisition-and-workspaces.md) | Revision and operation authorization, context realization, complete route-map adoption, immutable generation publication, replacement, and retirement |
| [Platform Composition and Overlays](platform-composition-and-overlays.md) | Exact platform realization, platform/designated role policy, identity eligibility, precedence, and shadows |
| [Platform/Package Pruning](platform-package-pruning.md) | Exact target/package subsumption fact and version comparison |
| [Package Dependency Evidence](package-dependency-evidence.md) | Normalized declarations, origin and edge identities, framework scope, produced relationships, and completion |
| [Package Dependency Candidate Resolution](package-dependency-candidate-resolution.md) | Declaration-to-exact-source-authorized-candidate result |
| Package acquisition and asset owners | Payload authorization, content lifetime, target-framework and runtime selection, package roles, and surface/implementation correspondence |
| Assembly Reference Resolution Ladder | Exact route-plan and external-route validation, rung order, cross-rung continuation, package-rung aggregation, finite work, generation-bound continuation, and closed result |
| Call Graph and other consumers | Focal scope, request scheduling, traversal, graph restart after replacement, completeness projection, and presentation |

There are eight counted production-adoption stages:

1. Lock this owner contract in #6288.
2. Add the host-neutral request, deferred route-plan, external-route,
   rung-trace, result, and finite-work currencies without adding package or
   host dependencies to Metadata.
3. Add owner-issued root-relative package-route projection and exact
   focal-scope eligibility receipts over Package Dependency Traversal and
   authoritative restored graphs.
4. Add the applicable-platform route adapter over exact target, catalog,
   pruning, acquisition, and realization evidence.
5. Add the package route adapter in `DotnetInspector.PackageQueries` over
   Package Dependency Candidate Query, payload acquisition, asset selection,
   and package-role realization.
6. Add the shared immutable-generation acquisition and continuation loop, with
   the Workspace owner retaining publication and replacement.
7. Adopt the shared ladder in CLI call-graph construction.
8. Adopt it in Inspect Web call-graph construction and remove the
   platform-specific `PlatformCallGraphExports` expansion loop.

Each stage lands through its owning component. This design does not authorize
one implementation PR spanning Workspace, platform, packages, Queries, CLI,
and Browser.

## Acceptance and evidence

Required future Release gates:

| Scenario | Required observation |
| --- | --- |
| Exact in-context match with lower same-name candidates | Context selection is terminal and performs no lower-rung acquisition |
| Same-name in-context identity mismatch | `NameOwnedNoMatch` remains terminal |
| Context miss and exact platform match | Platform selection retains exact target and realization correspondence |
| Subsumed package edge | Visible package-edge-to-platform delegation; no package payload acquisition |
| Package version above prune watermark | Platform substitution is absent and the exact package route remains |
| Associated package edges straddle the prune watermark | Retained package evidence dominates; no platform selection occurs on behalf of the subsumed edge |
| Package edge without assembly membership | Complete `NoNameOwner`, not an inferred match |
| Restored transitive edge without normalized declaration | Exact coordinate authorization preserves restored relationship evidence and synthesizes no declaration |
| Two packages with one exact identity each | Package rung returns `Ambiguous` independent of route and completion order |
| One provisional package match followed by budget exhaustion | `Incomplete`, never `Resolved` |
| Missing required Platform | Typed platform unavailable or incomplete result; no package-name fallback |
| External acquisition | Replacement generation is published atomically; the old group is never mutated |
| Generation or policy changes during the attempt | Typed supersession or rejection; no stale descriptor publication |
| Same request in CLI and Browser | Equal route and result semantics under equivalent owner inputs |

Contract tests derive result cases from the closed result and miss-disposition
declarations so a new arm cannot enter without evidence. Package-route tests
use independently compiled multi-assembly packages and include a package whose
ID differs from the assembly it supplies. Platform overlap tests use exact
pruning receipts rather than a hard-coded `System.*` heuristic.

Browser original-host and Firefox tests gate the shared operation without
ambient filesystem probing. CLI tests gate the same typed result before
Markout lowering. Architecture gates keep the neutral ladder free of inspected
assembly loading, Roslyn, host UI, source credentials, and host filesystem
assumptions.

The design remains unverified until those focused stages land.

## Non-claims

This design does not define:

- Workspace registration values, raw-versus-curated construction,
  call-graph focal lengths,
  traversal direction or depth, graph nodes or edges, or rendering;
- assembly identity equivalence, version roll-forward, designated/platform
  arbitration, candidate-domain eligibility, binding caches, type forwarding,
  or binding failure taxonomy;
- platform catalog generation, platform closure membership, package-pruning
  inventory or comparison, or framework-family selection;
- package-input normalization, dependency graph construction, package version
  selection, source authorization, package payload acquisition, target
  framework compatibility, asset selection, or role realization;
- package identity inferred from assembly or namespace text;
- a global NuGet, SDK, runtime, or filesystem search;
- operation-budget defaults, host cache partitioning, or admission capacity;
- compiler reference discovery or retirement of the legacy desktop tools
  resolver;
- successful partial binding after omitted work;
- mutable assembly-context groups or cross-generation descriptor reuse; or
- implementation, merge, release, or deployment authorization.
