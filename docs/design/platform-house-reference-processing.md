# PlatformHouse reference processing

## Status and approved scope

This document is the normative owner for the host-neutral `PlatformHouse`
composition boundary. It is tracked by
[#6301](https://github.com/richlander/dotnet-inspect/issues/6301) and is a
focused prerequisite of the platform-first tracker
[#6228](https://github.com/richlander/dotnet-inspect/issues/6228).

The design depends on:

- [Platform Target Currency](platform-target-currency.md) for exact runtime
  and ASP.NET Core family targets;
- [Platform Library Population Declaration](platform-library-population-declaration.md)
  for target-independent Workspace relevance;
- [Platform/package pruning](platform-package-pruning.md) for exact-target
  package subsummation facts;
- [Structured type-forwarding resolution](type-forwarding-resolution.md) for
  Metadata-owned forwarding and binding outcomes;
- [Platform composition and overlays](platform-composition-and-overlays.md)
  for source-specific coherent implementation realization; and
- the
  [Assembly Reference Resolution Ladder](assembly-reference-resolution-ladder.md)
  for the ordering and lifetime of an applicable-platform rung.

The approved scope is:

- `PlatformHouse` is the sole product-facing platform reference-processing
  facade;
- package pruning and .NET Standard reference handling are encapsulated behind
  that facade;
- .NET Standard remains transparent compatibility processing rather than a
  `PlatformFamily`, registration population, or implementation target; and
- Metadata type forwarding connects reference contracts to definitions under
  a platform-authorized binding context.

The first production consumer is the applicable-platform rung of the assembly
reference resolution ladder. Queries, Workspace composition, CLI platform
operations, and Inspect Web adopt the same House contract in separately
reviewed slices.

This is one owner claim. The design specifies the House request, settlement,
result, evidence-retention, and encapsulation contracts. It consumes the
owner-issued inputs listed above without redefining their identities,
algorithms, lifetimes, or failure semantics.

## Authority and exact claim

**PlatformHouse Reference Processing** owns:

> Given one exact platform family target, one host-authorized platform source
> plan, one closed platform operation, and finite operation work, compose
> source candidates and owner-issued platform facts into one typed settlement
> that preserves target, source, reference-contract, implementation-supplier,
> pruning, forwarding, completion, and failure evidence.

It owns:

- the product-facing `PlatformHouse` facade;
- common request context shared by platform operations;
- closed realization, assembly-reference, type-resolution, and package-
  reference operation shapes;
- host-authorized source-capability composition;
- candidate validation, comparison, selection, and settlement;
- the distinction between reference and implementation demand;
- reference-to-implementation correspondence between settled platform views;
- platform-specific invocation of pruning and Metadata forwarding;
- the House result envelope and settlement receipt;
- visible unavailable, ambiguous, rejected, and incomplete outcomes; and
- the rule that product reference processing does not compose lower platform
  mechanisms outside the House.

It does not own:

- platform family, target-framework, or version identity;
- target-independent ecosystem registration;
- package identity, package source authority, package dependency evidence,
  package candidate selection, or package payload acquisition;
- prune inventory construction, supplied-version comparison, or staleness;
- assembly identity, declaration probing, binding selection, forwarding hops,
  or Metadata resolution outcomes;
- installed-hive discovery, manifest-defined implementation closure,
  reference-pack layout, package-pack acquisition, Browser catalog generation,
  or source-specific cache policy;
- assembly-reference rung ordering or package-route reachability;
- Workspace admission, revisions, replacement, leases, or participant
  lifetime;
- call-graph traversal, search ranking, section selection, rendering, or host
  presentation; or
- XML documentation, SourceLink, or PDB acquisition.

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
- `PlatformPruneInventory` and `PlatformPrunePolicy` expose exact package
  subsummation facts without owning their product use;
- Queries currently map platform coordinates directly to representative
  implementation-pack packages; and
- CLI and Browser paths compose these mechanisms differently.

These are useful implementations or owner facts, but none is the product
boundary. Direct composition makes the caller responsible for distinctions it
cannot safely reconstruct:

- which exact platform target the evidence describes;
- whether the request needs reference contracts, implementation bodies, or
  both;
- whether a package edge is actually subsumed;
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
| **Source plan** | An immutable host-authorized set of platform source capabilities and their explicit selection policy. |
| **Source capability** | A bounded adapter entry point for one source-specific operation. It is not source authority by display name. |
| **Source contribution** | One source's typed candidate, non-match, failure, or incomplete evidence, retaining exact target correspondence. |
| **View demand** | Whether the consumer requires reference contracts, implementation bodies, or both. |
| **Population demand** | Whether the consumer requires one exact library or one complete family population. |
| **Reference contract** | A reference assembly or facade used to describe an API contract. It does not prove an implementation supplier. |
| **Implementation supplier** | The exact physical assembly candidate containing the implementation definition or body. |
| **View correspondence** | House-owned evidence connecting one resolved reference definition to one authorized implementation-resolution start without pretending that the transition is a Metadata forwarding hop. |
| **Settlement** | The House decision that selects, composes, or declines source contributions under the request's policy. |
| **Settlement receipt** | Resource-free evidence binding the request, target, source-plan generation, selected contributions, owner results, completion, and work. |

The vocabulary deliberately does not call `.NET Standard` a platform family.
Its reference contract may participate in one operation while the House target
remains an exact .NET runtime or ASP.NET Core family target.

## Contract shape

```text
exact PlatformFamilyTarget
  + one closed operation
  + host-authorized source plan
  + finite work and cancellation
        |
        v
PlatformHouse
  1. validate request and correspondence
  2. ask only authorized source capabilities
  3. retain every owner-issued contribution
  4. compose pruning or Metadata only when the operation requires it
  5. settle the requested view and population demand
        |
        v
one closed House outcome
  - completed operation result + settlement receipt
  - unavailable
  - ambiguous
  - rejected
  - incomplete
```

The House performs no ambient source discovery. A desktop host may authorize
installed, cached, and network-backed capabilities; a Browser/Wasm host may
authorize generated, embedded, or network-backed capabilities. The House sees
only the supplied plan and never widens it because another source is locally
reachable.

### Common request context

Every operation carries:

- one exact `PlatformFamilyTarget`;
- one immutable source-plan identity and policy generation;
- the exact Workspace revision or standalone operation identity;
- the requested reference and implementation views;
- one-library or whole-population demand;
- finite source, candidate, assembly, byte, forwarding-hop, and deadline
  budgets;
- owner-issued prerequisite correspondence; and
- caller cancellation.

The exact numeric defaults remain host policy. The House owns validation,
checked charging, and the rule that a broad source plan or population demand
is never unbounded work.

The House does not accept a floating or implicit target. A target-selection
owner may discover and issue an exact `PlatformFamilyTarget` before calling
the House, retaining the selection evidence required by the target-currency
contract.

### Closed operations

The version-1 facade has four conceptual operation kinds. Exact public type and
member names may change during implementation, but the distinctions may not be
collapsed into strings, optional parameters, or nullable tuples.

| Operation | Input unique to the operation | Completed value |
| --- | --- | --- |
| **Realize** | One library identity or complete population demand | Requested reference and/or implementation realization with source correspondence |
| **Resolve assembly reference** | One exact Metadata `AssemblyBindingRequest` and platform-route prerequisites | Metadata-owned binding decision plus the platform contribution used by the ladder |
| **Resolve type definition** | One exact Metadata `TypeResolutionRequest`, starting reference candidate, and required view | Metadata-owned `TypeResolutionOutcome` plus reference/implementation correspondence |
| **Evaluate package reference** | One exact package coordinate and owner-issued package-edge association | Retained-package or delegated-to-platform decision preserving the pruning result |

`Realize` supports direct platform browsing and supplies source candidates for
the other operations. The three reference-processing operations are the only
product-facing paths that may turn pruning, platform catalogs, .NET Standard
reference contracts, or platform-specific Metadata policy into a binding,
delegation, or implementation result.

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

## Source plans and contributions

### Host authorization is explicit

A source plan identifies each capability the host permits for the exact
operation and the policy for considering it. It may authorize:

- installed reference packs;
- installed implementation-platform realization;
- package-backed reference packs;
- package-backed implementation packs;
- a generated Browser platform catalog;
- embedded platform content; or
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
- the source-specific coordinate and owner-issued target correspondence;
- supported view and population demand;
- source generation or freshness evidence;
- candidate identities or realized owner result;
- authoritative, partial, or unavailable completion; and
- typed rejection or failure when the operation did not succeed.

A source that discovers another exact target may return it as discovery
evidence only when its source contract permits target selection. It does not
answer the original exact request by relabeling that evidence.

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
- a reference assembly does not prove an implementation body; and
- equal names or versions do not merge different families, targets, sources,
  generations, or physical suppliers.

ASP.NET Core source realizations may retain a .NET runtime support closure.
The source owner establishes that closure and its version behavior. The House
preserves the focus target, support targets, members, and evidence rather than
inventing one merged family target.

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
- complete associated package-edge evidence when any exists;
- the source-plan and operation identities; and
- the remaining work ledger.

The House:

1. evaluates package overlap when associated package edges exist;
2. establishes whether a platform route is applicable;
3. realizes the required platform library or source-owned atomic unit;
4. forms the platform-authorized Metadata binding policy over the selected
   candidates; and
5. returns the unchanged Metadata binding decision with House correspondence.

The House does not call the package rung, search NuGet by assembly name, or
advance the ladder. A completed platform `NoNameOwner` lets the ladder proceed
under its own contract. `NameOwnedNoMatch`, ambiguity, unavailable evidence,
rejection, or incomplete work retain their existing terminal meaning.

## Package-reference processing and pruning

Pruning enters only when an owner has already established a real package edge
and exact package coordinate. An assembly name, namespace, catalog match, or
package-like prefix cannot manufacture the missing package association.

For one package reference, the House asks the pruning owner for the exact
target's result and preserves it:

| Pruning result | House reference decision |
| --- | --- |
| `Subsumed` | The package edge may be delegated to Platform when the remaining platform-route evidence is complete. |
| `NotSubsumed` | Retain the package route. |
| `NotComparable` | Retain the package route with the comparison limitation visible. |
| Inventory unavailable or incomplete | Retain or report incomplete according to the caller's operation; never delegate to Platform. |

For an `AssemblyRef` associated with several request-eligible package edges,
every associated edge must be `Subsumed` before the House can issue an
all-associated-delegated platform contribution. One retained or uncomparable
edge prevents same-request platform substitution.

A package-independent platform library remains a separate case. Complete
platform catalog evidence and complete package-edge association may establish
that no request-eligible package route overlaps the exact `AssemblyRef`.
Catalog membership alone cannot establish package independence.

The House does not compare assembly versions with package versions. It does
not reinterpret the pruning owner's conservative result, publish an inventory,
or infer package existence from inventory absence.

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
   returns its unchanged reference-view outcome;
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

## Result and receipt contract

Each operation returns an operation-specific value inside one common House
outcome envelope:

- **Completed** — the requested operation settled, with its typed value and
  settlement receipt;
- **Unavailable** — an authorized source, exact target, required view,
  candidate, inventory, or implementation supplier could not be obtained;
- **Ambiguous** — owner evidence permits several active candidates without an
  authorized precedence;
- **Rejected** — the request, source plan, target correspondence, candidate,
  owner result, budget, or retained evidence is invalid; or
- **Incomplete** — bounded work, partial source evidence, route association,
  catalog coverage, or generation replacement prevents a conclusive result.

Cancellation remains `OperationCanceledException`.

`Completed` does not mean every possible facet succeeded. It means every facet
required by the exact operation settled. For example:

- a reference-only `Realize` operation may complete without implementation
  evidence;
- package-reference evaluation may complete as retained-package;
- an assembly-reference operation may complete with Metadata
  `NoNameOwner`; and
- a type-resolution operation may complete with Metadata `NotFound` only when
  the selected readable image authoritatively returned that result.

The settlement receipt binds:

- the exact request and House target;
- source-plan identity and policy generation;
- every selected or outcome-relevant source contribution;
- reference and implementation view correspondence;
- pruning results and package-edge associations when consulted;
- Metadata binding or forwarding outcomes when invoked;
- source, Workspace, and catalog generations;
- completeness; and
- consumed work.

The receipt does not expose credentials, mutable buffers, live streams,
filesystem paths as identity, or a capability that can repeat source work.
Bound source handles and leases remain owned by their source or Workspace
contract.

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

For standalone platform inspection, the caller owns the operation lifetime and
disposes every returned source lease. The same House request and settlement
semantics apply; standalone use does not create Workspace authority.

House caches key successes and failures by exact target, operation shape,
source-plan generation, source generation, view, and population demand. A
replacement or policy change cannot reuse a prior binding decision merely
because the paths or display labels are equal.

## Encapsulation boundary

Product code that decides platform binding, package delegation, or transparent
.NET Standard implementation resolution calls `PlatformHouse`.

Lower-level owner APIs remain valid for:

- their own implementations and tests;
- source-adapter construction;
- generic Metadata resolution in non-platform domains;
- pruning data generation and audit projection; and
- independently designed diagnostics that expose an owner fact without using
  it to change reference processing.

They are not parallel product service surfaces. In particular:

- CLI and Browser do not call `PlatformResolver`, `PlatformPackService`,
  `PlatformTypeCatalog`, or pruning policy to process a reference;
- Queries and Workspace do not recreate platform source precedence or
  .NET Standard forwarding policy;
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
  consumes Packages, Metadata, source capabilities, and owner results
             |
             v
Queries / Workspace integration
             |
             v
CLI and Browser hosts
```

`DotnetInspector.Platforms` remains the lower resource-free contract floor. It
does not acquire House request, source, pruning, Metadata, or result
dependencies.

The operational House belongs in a separate host-neutral project boundary
above Packages and Metadata. If source-adapter dependency inversion requires a
smaller contract seam, that seam remains part of the same House owner rather
than expanding the identity floor. Exact project names and moves are tracked
by
[#6335](https://github.com/richlander/dotnet-inspect/issues/6335).

Source-specific implementations remain focused:

- installed implementation realization preserves its package-free dependency
  boundary;
- package-backed pack acquisition may depend on package source and payload
  owners;
- Browser-generated catalogs remain portable and contain no desktop adapter;
- pruning remains with the package/pruning owner; and
- forwarding remains with Metadata.

The House composition project may depend on these owner contracts. None of
them depends on the operational House merely to preserve its own facts. Source
capabilities are injected or adapted in the direction that avoids
`Packages -> House -> Packages` and equivalent cycles.

## Pathological cases

### A newer package leapfrogs the platform

The exact platform target supplies `System.Text.Json` through version 11.0.0,
while an associated package edge requests 12.0.0. Pruning returns
`NotSubsumed`. The House retains the package route even if the platform catalog
contains `System.Text.Json.dll`. Name overlap does not override package-version
evidence.

### One associated edge is not comparable

Two complete package edges are associated with one `AssemblyRef`. One is
`Subsumed`; the other has no exact version and is `NotComparable`. The House
does not issue an all-associated-delegated platform contribution. The
uncomparable package route remains visible.

### No package edge exists

A runtime assembly references `System.Runtime`, and complete route evidence
establishes no associated package edge. The House may issue a
package-independent platform contribution from exact catalog and target
evidence. It did not use pruning to manufacture the absence.

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
3. Introduce the host-neutral House request, outcome, receipt, source-plan, and
   contribution types without moving source implementations.
4. Adapt installed reference and implementation realization to contribute
   exact-target source evidence while preserving the package-free installed
   boundary.
5. Adapt package-backed reference and implementation packs to contribute
   source-authorized exact-target evidence.
6. Integrate pruning behind the House for package-reference and platform-route
   decisions without moving pruning semantics.
7. Move target-bound type indexing behind the House and integrate transparent
   .NET Standard resolution through Metadata's structured forwarding contract.
8. Adopt the House platform contribution in the assembly-reference ladder,
   Queries, Workspace replacement, and dependency traversal.
9. Adopt the same House requests and outcomes in CLI and Inspect Web.
10. Retire direct product reference-processing entry points in
    `PlatformResolver`, `PlatformPackService`, `PlatformTypeCatalog`, and host
    composition, then complete the `DotnetInspector.Services` decomposition
    tracked by #6335.

Each step after the House contract is a separately reviewed owner adoption.
The stack preserves a usable product after every step; a bypass is retired only
after its House replacement is live in every supported host that uses it.

No CLI flag is retained solely for compatibility. User-facing platform
coordinates project to the shared target and House request, and unsupported
legacy combinations fail visibly under the adopting command owner.

## Demo

### Package edge delegated to Platform

```text
request
  target: DotNetRuntime / net11.0 / 11.0.0
  operation: evaluate package reference
  edge: System.Text.Json 10.0.0
  demand: implementation library
  sources: host-authorized installed + package-backed plan

House composition
  prune result: Subsumed by exact target
  platform library: System.Text.Json
  implementation supplier: exact runtime realization member

completed
  decision: delegated to Platform
  receipt:
    exact target
    package edge and prune evidence
    selected implementation source and member
    source-plan generation and work
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

### Package edge retained

```text
request
  target: DotNetRuntime / net11.0 / 11.0.0
  operation: evaluate package reference
  edge: System.Text.Json 12.0.0

completed
  prune result: NotSubsumed
  decision: retain package route
  no platform acquisition performed on behalf of that edge
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
| Exact target retention | Every completed and non-success outcome retains the request's exact `PlatformFamilyTarget`; a source result for another target cannot settle it. |
| Explicit source authorization | No source capability, cache, installed root, or network route is used unless present in the captured source plan. |
| Demand-bounded work | One-library demand does not become whole-population work unless the selected source reports an atomic larger realization unit. |
| Reference and implementation separation | Reference-only success cannot satisfy implementation-body demand; a combined result retains explicit correspondence. |
| Reference-definition bridge | A reference `TypeDef` reaches implementation only through House-owned view correspondence and a separate Metadata request; no forwarding hop is invented. |
| Conservative pruning | Only `Subsumed` can delegate a real package edge to Platform; `NotSubsumed`, `NotComparable`, and missing inventory do not. |
| Complete overlap | Every request-associated package edge must be subsumed before all-associated platform delegation. |
| Metadata ownership | Platform type resolution invokes the structured Metadata API and preserves its exact outcome and forwarding hops. |
| Transparent .NET Standard | A `.NET Standard` facade can resolve through an exact runtime target without constructing a `NetStandard` family or implementation population. |
| Physical supplier retention | A resolved implementation type or assembly retains its physical supplier rather than being relabeled as the reference facade. |
| Visible incomplete evidence | Source, catalog, forwarding, pruning, work, and generation incompleteness never become absence or a success-shaped empty result. |
| Ladder composition | The assembly-reference ladder receives one House platform contribution and preserves its own rung order and result algebra. |
| Host parity | Representative CLI and Browser operations issue equivalent House requests and interpret the same typed outcomes. |
| Installed boundary | Browser composition does not reference desktop installed adapters, and installed realization remains package-free. |
| Encapsulation | Representative ladder, Query, CLI, and Browser paths use the House. Per user choice, no automated repository-wide bypass-absence gate is required. |

The design-only PR is Markdown-only and requires `markdownlint`. Each
implementation slice adds the smallest gate covering its adopted property.

The House carries typed operation data but defines no presentation. Markout
and host-specific rendering are not part of this owner.

## Non-claims

This design does not:

- add `.NET Standard`, WindowsDesktop, or workloads as a `PlatformFamily`;
- define framework compatibility, runtime roll-forward, or target selection;
- define platform catalog membership or source-specific family composition;
- create a package-to-assembly naming convention;
- make pruning an `AssemblyRef` classifier;
- alter package dependency traversal or the assembly ladder's route order;
- alter Metadata forwarding, binding, declaration, or terminal outcomes;
- make a reference assembly an implementation supplier;
- define XML documentation, SourceLink, PDB, or symbol-server policy;
- define Workspace admission, replacement, or lease lifetime;
- require network access or desktop filesystem capabilities;
- permit inspected-assembly loading or Roslyn;
- add WinMD support; or
- create an aggregate platform assembly containing every platform helper.
