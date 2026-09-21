# Ecosystem population loading

## Status and approved scope

This document is the normative owner for Ecosystem Population Loading, tracked
by [#7317](https://github.com/richlander/dotnet-inspect/issues/7317) under the
Workspace-rooted inspection tracker
[#7301](https://github.com/richlander/dotnet-inspect/issues/7301).

The operator approved one Ecosystem-specific loader extension point and the
first two production uses: a Workspace obtains the .NET runtime platform
through the `.NET Runtime` Ecosystem and the ASP.NET Core platform through the
`ASP.NET Core` Ecosystem rather than through a separately presented Platform
component. An Ecosystem may contribute one special loader when its population
cannot be satisfied by ordinary exact-Library, package-root, or package-prefix
processing.

This is a new focused cross-cutting pattern. It defines the loader binding,
explicit load request, execution boundary, result algebra, association, and
non-action rules. Static Ecosystem Packs, Workspace Ecosystem Registration
Handoff, PlatformHouse, PackageHouse, Workspace admission, Navigation, CLI,
and Inspect Web adopt the pattern in separately reviewed efforts. This document
does not redefine those owners.

The first production consumers are the CLI and Browser/Wasm Workspace
experiences. They use the same host-neutral loader operation. Host composition
supplies different authorized PlatformHouse source plans where needed; the
loader contract does not create a desktop-only path.

The stage-2 contract substrate is implemented in
`DotnetInspector.EcosystemLoading`. It includes canonical loader identity,
typed static binding, exact registration correspondence and selection,
single-use requests, closed replies and outcomes, resource-free receipts,
request-bound adjacent-owner request and receipt identities, completed-child
ownership tokens, and one-shot Library owner transfer or retirement.
`DotnetInspector.EcosystemLoading.Tests` owns the focused Release gates, while
`DotnetInspector.Ecosystems.Consumer.Tests` exercises the public surface
without friend access. Catalog registration, PlatformHouse adapters, Workspace
admission correspondence, and the initial production loaders are implemented;
Navigation, host envelopes, and host adoption remain later stages.
The shared substrate binds completion evidence to the exact request and
structurally permits owners only from completed child tokens. The stage-5
PlatformHouse adapters remain responsible for issuing completion evidence only
after their owner-specific expected-child and settled-child sets agree.
The admission composer consumes owner-bearing results beside the exact
Workspace and preserves accepted occurrences without moving Workspace
mutation authority into a loader.

## Authority and exact claim

**Ecosystem Population Loading** owns:

> Given one explicitly selected owner-issued Ecosystem registration from one
> exact Workspace revision, one statically registered loader binding, one
> bounded population demand, and caller-authorized capabilities, invoke that
> binding once and return either one exact owner-preserving population result
> or one typed non-success. Loading does not occur during catalog discovery,
> registration, Workspace construction, or restoration. A loader cannot mutate
> or admit into the Workspace; successful results enter the ordinary Workspace
> admission boundary separately.

The owner defines:

- the opaque static loader binding;
- the exact request association among Workspace revision, Ecosystem
  registration, loader binding, and operation demand;
- the loader invocation boundary;
- the closed terminal result algebra;
- ownership transfer at the successful-result boundary;
- cancellation, failure, and incomplete-result behavior; and
- the separation between a saved Ecosystem registration and executable
  application composition.

It consumes without redefining:

- Static Ecosystem Packs' application identity and choice of loader;
- Workspace Ecosystem Registration Handoff's lower immutable declaration;
- Workspace Scope's identity, revision, registration, admission, and lifetime;
- PlatformHouse and PackageHouse requests, source plans, outcomes, receipts,
  and Library handoffs;
- Library ownership and borrowing;
- Navigation subjects, routes, and contribution relations; and
- host source authorization and presentation.

## Architectural role and House composition

An Ecosystem is the product-facing aggregate, not a source or settlement
domain. One Ecosystem registration may describe ordinary package populations
and special source-native platform populations together because both are
relevant to the same user-visible area. Their shared Ecosystem identity does
not merge their source identity, evidence, lifetime, or ownership.

Ecosystem Population Loading is the application service boundary for explicitly
realizing a selected special population. It preserves the association from the
Ecosystem registration to every owner-issued child request and typed outcome.
The consuming Ecosystem operation composes that result with independently
processed ordinary populations. Neither layer is an `EcosystemHouse`, and
neither issues replacement source authority, settlement, receipt, or Library
ownership contracts.

| Role | Owned responsibility |
| --- | --- |
| Ecosystem | Product identity, registration, and aggregation of package and platform populations |
| Ecosystem Population Loading | Bounded special-population orchestration preserving owner-issued child requests, outcomes, and receipts |
| PlatformHouse | Exact platform-target and platform-source settlement, cleanup, receipts, and atomic Platform-origin Library ownership transfer |
| PackageHouse | Exact or selecting package settlement, package-source acquisition and cleanup, receipts, and Package-origin ownership transfer |

The Houses remain independent even when one product flow presents both under
the same Ecosystem. Each actual operation uses that domain's owner-issued
request and capability plan and retains the corresponding outcome and receipt.
Composition cannot reinterpret a package coordinate as a platform target,
infer Platform membership from package identity, or use one House's success as
the other House's settlement.

The initial `.NET Runtime` and `ASP.NET Core` special loaders delegate their
source-native populations to PlatformHouse. Their ordinary package-prefix
registrations remain inert relevance for already admitted Package occurrences.
When a separately selected bounded package operation produces a PackageHouse
receipt and Workspace admits the resulting Package-origin Libraries, the
prefix may relate those occurrences to the Ecosystem. It does not initiate that
operation or admission. The Package-origin and Platform-origin relations
compose only in the consuming Ecosystem or Navigation result. This
clarification changes no current loading or admission behavior.

```text
independently selected package operation
  -> PackageHouse outcome and receipt
  -> ordinary Workspace admission
  -> Package-origin Library occurrence
  -> inert PackagePrefix("System.") supplies .NET Runtime Ecosystem relation

selected .NET Runtime special population
  -> Ecosystem Population Loading
  -> PlatformHouse outcome and receipt
  -> ordinary Workspace admission
  -> Platform-origin Library occurrence
  -> exact .NET Runtime Ecosystem relation

Workspace or Navigation result
  -> presents both source-distinct populations under the .NET Runtime Ecosystem
```

## User experience

The user selects or restores `.NET Runtime` or `ASP.NET Core`, not a Platform
component:

```text
Workspace
|- .NET Runtime
|  |- System.Text.Json (.NET Library)
|  |- System.Text.Json 10.0.0 (Package)
|  |  |- System.Text.Json (Library)
|  |- System.Text.Json 10.0.1 (Package)
|     |- System.Text.Json (Library)
|- ASP.NET Core
|  |- Microsoft.AspNetCore.Http.Abstractions (ASP.NET Core Library)
|  |- Microsoft.AspNetCore.OpenApi 10.0.0 (Package)
|     |- Microsoft.AspNetCore.OpenApi (Library)
```

The `.NET Runtime` and `ASP.NET Core` Ecosystems each have two independent
contributions:

```text
.NET Runtime Ecosystem
  ordinary population
    PackagePrefix("System.")
  special loader
    .NET runtime platform population

ASP.NET Core Ecosystem
  ordinary population
    PackagePrefix("Microsoft.AspNetCore.")
  special loader
    ASP.NET Core platform population
```

The prefix supplies inert relevance and route evidence for already admitted
`System.*` packages. It does not acquire or admit either Package occurrence.
The loader satisfies the source-native runtime population through
PlatformHouse. The resulting `System.Text.Json` Library remains
Platform-origin and source-distinct from the two Package-origin Libraries.

The same separation applies to ASP.NET Core. Its prefix supplies relevance for
already admitted `Microsoft.AspNetCore.*` packages, while its loader satisfies
the source-native `Microsoft.AspNetCore.App` population through PlatformHouse.

Adding either Ecosystem to a saved Workspace definition records its exact
Ecosystem registration. Opening that definition constructs a fresh live
Workspace and may explicitly request realization of the selected Ecosystem.
The visible gesture is "add or open `.NET Runtime`" or "add or open `ASP.NET Core`";
no user-facing Platform registration, selector, root, or fallback is
introduced.

## Why a loader is separate from registration

Registration says what a Workspace is about. Loading is an operation with
target policy, source authorization, finite work, failure, cancellation, and
resource ownership.

Putting source work in registration would make all of these otherwise inert
actions acquire content:

- discovering the Ecosystem catalog;
- constructing a `WorkspacePlan`;
- adding or removing a registration;
- serializing or restoring a saved Workspace definition; and
- comparing registration revisions.

That would violate the existing resource-free registration boundary and make
restoration behavior depend on ambient host capabilities.

The loader therefore remains an explicit operation selected after one exact
registration revision is current. A product flow may make that operation the
normal consequence of opening or adding `.NET Runtime` or `ASP.NET Core`, but the
operation and any failure stay observable and independently cancellable.

## Static loader binding

The owner-issued currencies are `EcosystemPopulationLoaderId` and
`EcosystemPopulationLoaderBinding`. Conceptually:

```text
EcosystemPopulationLoaderBinding.Create(
  EcosystemPopulationLoaderId("ecosystem-loader.runtime"),
  RuntimeEcosystemPopulationLoader.LoadAsync)

EcosystemPopulationLoaderBinding.Create(
  EcosystemPopulationLoaderId("ecosystem-loader.aspnetcore"),
  AspNetCoreEcosystemPopulationLoader.LoadAsync)
```

Construction accepts exactly one target-free static method group. It rejects a
captured target and a combined invocation list. The binding is immutable
application-lifetime metadata, not a loader instance, service provider,
Workspace handle, source capability, or cache. Its ID is stable
application-owned identity for selection, receipts, and diagnostics; it is not
derived from a pack ID, title, method name, declaring type, or delegate target.

The binding exposes no public delegate, target method, or general-purpose
`Invoke`. Only Ecosystem Population Loading orchestration invokes it under this
owner's request and result contract.

The application catalog may associate zero or one loader binding with one
Ecosystem pack. Zero means that ordinary contributions fully describe the
Ecosystem. One means that explicit realization may select the special loader.
Several special behaviors compose behind one owner-issued binding rather than
creating an ordered runtime plugin chain.

The term *plugin* describes the product extension role. It does not introduce
dynamic assembly discovery, reflection activation, package-installed code,
external processes, hot reload, mutable registration, or third-party
execution. Every shipped binding is statically compiled, reviewed, and rooted
for NativeAOT.

## Request

Loader selection precedes request construction:

```text
EcosystemPopulationLoaderSelection
  Known(binding)
  Unavailable(exact Ecosystem registration, demand, diagnostics)
  Rejected(exact Ecosystem registration, demand, diagnostics)
```

`Unavailable` means the exact application pack is known but supplies no loader
for the requested special demand. `Rejected` means application correspondence
is unknown or does not match the retained registration. Neither outcome
invents a loader ID or invokes source work.

Only `Known` can form an `EcosystemPopulationLoadRequest`.

One `EcosystemPopulationLoadRequest` contains:

```text
Workspace identity
Workspace registration revision
exact retained Ecosystem registration
selected loader binding
population demand
operation policy
authorized capability plan
finite work
```

The request carries the exact lower
`WorkspaceEcosystemRegistrationDeclaration`, not only its ID. Orchestration
verifies that the selected application pack and loader binding correspond to
that retained declaration before invocation. Equal display text or equal
serialized identity without owner-issued correspondence is insufficient.
The application catalog recognizes a declaration projected by its current
static manifest through exact retained object correspondence. It does not look
up a binding from the declaration ID or structurally compare a separately
constructed declaration.

Population demand is explicit:

```text
EcosystemPopulationDemand
  WholePopulation
  ExactLibrary(owner-issued exact source coordinate)
```

`WholePopulation` asks the loader to satisfy every special contribution needed
for the selected operation. `ExactLibrary` allows a direct Navigation or
resolution path to request one known source-native Library without realizing
the complete population. A loader may return typed `Unavailable` when its
source owner cannot support the requested demand.

The operation policy supplies target selection and source behavior through
owner-issued typed values. A loader may not derive target framework, version,
reference or implementation view, network permission, or installed-root
selection from the Ecosystem name.

The authorized capability plan is a closed owner-issued composition, not an
`IServiceProvider`, string-keyed service locator, or arbitrary callback bag.
Each capability retains its own request, authorization, work, outcome, and
lifetime contract. Extending the plan for a new source owner requires a
focused adoption rather than an untyped escape hatch.

The shared implementation keeps the catalog-visible binding non-generic while
its executable form is typed over one loader-specific input composition. That
input exposes only resource-free operation-policy, capability-plan, and work
identities to the request receipt. The binding never recovers typed inputs
through reflection, `dynamic`, object lookup, or service location.
The exact parent load request issues each adjacent-owner request and receipt
identity, and settlement rejects identities issued for another parent request.
For a PlatformHouse population child, the settlement additionally retains the
exact owner-issued `PlatformHouseRequestSnapshot` and
`PlatformPopulationRealizationReceipt`. It does not reconstruct either value
from the Ecosystem, family, target, source label, or generated child identity.
This evidence is resource-free; the adjacent Artifact session remains separate
owning authority.

## Invocation and result

Orchestration validates the bound request before invocation. One accepted
request invokes exactly that binding once. A mismatch discovered after
selection returns bound `Rejected` without capability work.

The terminal result is:

```text
EcosystemPopulationLoadOutcome
  Completed(
    receipt,
    owner-preserving Library and Artifact authorities)
  Unavailable(receipt, diagnostics)
  Ambiguous(receipt, diagnostics)
  Incomplete(receipt, partial evidence, diagnostics)
  Rejected(receipt, diagnostics)
  Failed(receipt, diagnostics)
```

Cancellation terminates through the owner's ordinary cancellation contract and
does not become `Unavailable`, `Incomplete`, or an empty `Completed`.

Every bound invocation outcome retains:

- Workspace identity and selected registration revision;
- exact Ecosystem registration;
- exact loader binding identity;
- population demand;
- applied operation policy and authorized capability-plan identity;
- every selected adjacent-owner request and terminal receipt through exact
  resource-free owner-issued identities; and
- for a PlatformHouse population child, the exact PlatformHouse request
  snapshot and population receipt issued by that child operation; and
- completion, omission, or failure diagnostics.

`Completed` means the demand was completely satisfied according to the loader
and every selected source owner. It is invalid when a request-relevant child
operation is incomplete, unavailable, rejected, failed, or silently omitted.
An empty completed population is valid only when the selected source owner
positively establishes that the exact demand has no members.

`Ambiguous` means owner evidence permits several active candidates without an
authorized precedence. A child that can issue `Ambiguous` remains child
`Ambiguous` and is projected as loader `Ambiguous`; it is not relabeled as
`Unavailable`, `Incomplete`, `Rejected`, or empty completion. The current
Platform complete-population producers do not issue an ambiguous population
result, so the Platform population projection makes no stronger claim. A
future Platform population ambiguity producer must add its typed construction
and exact projection gate before that outcome becomes supported.

`Incomplete` preserves successful partial evidence but makes no completeness
claim. It may carry owners only from independently `Completed` child
operations. A child source operation that is itself incomplete transfers no
owner unless that adjacent owner's contract explicitly says otherwise.
PlatformHouse transfers no owners from a non-completed operation, so an
incomplete PlatformHouse population contributes resource-free evidence only.
Workspace admission and Navigation retain the loader-level incomplete outcome
rather than treating any admitted members from other completed children as the
complete Ecosystem.

The owning result is consumed inside host-neutral operation orchestration
before returning to a host. After ownership has transferred to Workspace or
been retired, the host-facing API exposes the resource-free content, receipt,
portable projection, and diagnostics through
`InspectionEnvelope<EcosystemPopulationLoadContent>`. The envelope does not
carry Library owners, source leases, callbacks, or Workspace mutation
authority.

## Workspace admission boundary

A loader does not receive an `InspectionWorkspace`, Workspace mutation
authority, or a callback that admits Libraries.

A completed result, or a loader-level incomplete result containing
independently completed child operations, transfers each adjacent-owner
Library owner beside its exact resource-free Library reference to the calling
orchestrator. A completed PlatformHouse child also transfers its exact adjacent
`ArtifactSetSession` as separate one-shot authority associated with that child
settlement.

The owner batch retires untransferred Library owners before retiring an
untransferred Artifact session. Artifact-session retirement may begin while a
transferred Library owner remains live, but its awaited completion remains
pending until the owner's content leases quiesce. The batch therefore never
awaits Artifact retirement before disposing its own untransferred Library
owners, and it never silently abandons the pending authority. Artifact cleanup
failures remain visible as owner-batch retirement failure.

`EcosystemPopulationAdmissionOperation` is the host-neutral composer between
those owner-bearing outcomes and ordinary Workspace admission. It accepts the
exact loader outcome beside the target Workspace and consumes `Completed` and
`Incomplete` owner batches once. Non-owning outcomes produce a resource-free
result that retains the exact loader receipt and makes no admission attempt.
The operation has no cancellation parameter: once it begins consuming
authorities, every child is admitted or retired before the call completes.

Admission is atomic per session-backed completed child, not across the whole
Ecosystem load. The composer transfers one child's exact Artifact session and
its ordered Library-owner batch together to
`InspectionWorkspace.AdmitLibraryBatchAsync`. Workspace retains ownership as
soon as that call returns any typed outcome: `Accepted`, `Rejected`, or
`Failed`. A returned rejection or failure therefore remains the actual
Workspace disposition and is not followed by a second retirement attempt.
Earlier accepted children remain accepted if a later child rejects or fails;
the composer performs no cross-child rollback.

The initial composer supports only a completed child that carries both a
non-empty Library-owner batch and one adjacent Artifact session. A
Library-bearing child without an Artifact session, or an Artifact-session
child without Libraries, is a valid loader shape but not an admissible shape
for this session-backed Workspace API. The composer atomically takes and
retires those child authorities, then reports a typed `Unsupported`
disposition with any retirement failures. It does not relabel the loader
outcome or the Workspace outcome.

Each attempted child disposition retains the exact child settlement, ordered
loaded-Library references, and Workspace admission outcome. Each accepted
Library additionally receives an
`EcosystemPopulationLibraryAdmissionCorrespondence` joining:

- the exact loader receipt and registration revision;
- the exact role-bearing loaded-Library reference and child settlement;
- the exact Workspace admission receipt; and
- the exact admitted Workspace Library occurrence.

Only an accepted Library whose owner-issued roles include `Focus` receives an
`EcosystemPopulationLibraryContributionWitness`. A binding-support-only
Library is admitted for binding but receives no Ecosystem contribution
witness. The witness is historical acceptance evidence for that exact load,
registration, and occurrence. It does not prove that a current Navigation route
exists after registration replacement or removal; Navigation owns current
route issuance and reconciliation.

The loader receipt does not claim that admission succeeded. The later admission
result retains correspondence to the loader receipt. If Workspace admission
throws before consuming one child's authorities, the composer retires that
locally owned child before propagating failure. An unexpected orchestration
failure retires every remaining owner and Artifact session and throws
`EcosystemPopulationAdmissionException`, which retains all earlier completed
child dispositions and accepted correspondence. This exception does not turn
the failure into a success-shaped partial result.

This separation keeps an Ecosystem extension from bypassing:

- source authorization;
- Library ownership and borrowing;
- Workspace compatibility and admission;
- occurrence identity and replacement;
- Navigation relation issuance; or
- failure visibility.

## Initial platform-family loaders

The `.NET Runtime` and `ASP.NET Core` Ecosystems are the first loader consumers. Each
static catalog registration supplies one ordinary package-prefix population
and one special loader:

```text
EcosystemPack
  id: ecosystem.runtime
  title: .NET Runtime
  ordinary populations:
    PackagePrefix("System.")
  loader:
    DotNetEcosystemPopulationLoader

EcosystemPack
  id: ecosystem.aspnetcore
  title: ASP.NET Core
  ordinary populations:
    PackagePrefix("Microsoft.AspNetCore.")
  loader:
    AspNetCoreEcosystemPopulationLoader
```

Neither loader owns a platform target or source algorithm. Each composes the
owner-issued platform contracts while preserving its exact family:

```text
Workspace revision
  -> .NET Runtime Ecosystem registration
  -> .NET Runtime loader binding
  -> PlatformLibraryPopulationDeclaration(DotNetRuntime)
  -> family-preserving platform demand
  -> PlatformHouse request
  -> PlatformHouse outcome and receipt
  -> exact Platform-origin Library population
  -> ordinary Workspace admission

Workspace revision
  -> ASP.NET Core Ecosystem registration
  -> ASP.NET Core loader binding
  -> PlatformLibraryPopulationDeclaration(AspNetCore)
  -> family-preserving platform demand
  -> PlatformHouse request
  -> PlatformHouse outcome and receipt
  -> exact ASP.NET Core focus Library population
  -> ordinary Workspace admission
```

The public nominal input types retain the same Platform population declaration
object used by the static pack registration. A live Platform capability exposes
the exact capability-plan identity retained by the loader request and receives
that declaration plus the operation cancellation token. It owns construction
and execution of the authorized PlatformHouse request; the product loader does
not accept a service locator, source callback bag, target text, or family text.
Package-backed and installed population materializers return the same
source-neutral `PlatformPopulationArtifactMaterializationOutcome` consumed by
the capability, so hosts do not reconstruct or reflect over source-specific
results. A validated public completed-outcome factory composes an already
owner-issued Platform population with its adjacent Artifact session for
host-authorized materializers that do not use those two adapters.

The loader validates that the returned PlatformHouse request retains the
declaration's exact family before transferring any authority. A mismatch
retires every completed Library owner and the adjacent Artifact session, then
returns owner-issued `InvalidTargetCorrespondence` rejection evidence. A
retirement failure remains a typed Platform failure instead of being reported
as rejection or completion.

The `.NET Runtime` loader must retain the
`PlatformLibraryPopulationDeclaration(DotNetRuntime)` association through the
PlatformHouse result. It cannot infer `DotNetRuntime` from `.NET Runtime`, `System.`,
`net11.0`, an assembly name, or installed layout.

The ASP.NET Core loader must likewise retain
`PlatformLibraryPopulationDeclaration(AspNetCore)`. It cannot infer that
family from `ASP.NET Core`, `Microsoft.AspNetCore.`, an assembly name, or a
shared-framework layout.

An ASP.NET Core PlatformHouse result may retain a .NET runtime support closure.
The loader preserves the source owner's focus and binding-support roles.
Workspace may admit support Libraries needed for binding, but only exact
`AspNetCore` focus members receive an ASP.NET Core Ecosystem contribution
relation. A runtime support Library receives a `.NET Runtime` Ecosystem route only from
an independent `.NET Runtime` registration and loader receipt; equal target or assembly
text is not relation evidence.

CLI composition may authorize installed and package-backed PlatformHouse
sources. Browser/Wasm composition authorizes package-backed, generated-catalog,
or embedded sources and never references installed adapters. Both hosts invoke
the same loader contract for both Ecosystems and interpret the same result
algebra.

Neither loader processes its package prefix. Generic bounded package discovery
remains a separate operation. A package named `System.Text.Json` or
`Microsoft.AspNetCore.Http.Abstractions` cannot satisfy a PlatformHouse
request, and an equal Metadata assembly identity cannot replace the matching
source-native Platform Library. The two loaders remain independent;
`AspNetCore` is never merged into `DotNetRuntime` or hidden behind the `.NET Runtime`
loader.

## Persistence and restoration

Saved Workspace definitions retain the exact lower Ecosystem registration and
ordinary resource-free contributions. They do not serialize:

- delegates or loader instances;
- application assembly or type names;
- capability plans or source credentials;
- PlatformHouse or PackageHouse requests or receipts;
- loaded Library owners; or
- a claim that the Ecosystem is currently realized.

Restoration reconstructs lower declarations and therefore does not preserve
the live object correspondence issued by the current application catalog.
Until a later application-owned durable rebind operation validates the
portable declaration and issues new correspondence, explicit realization
returns selection `Rejected`. It does not match a pack or loader from equal
identity text or equal serialized content.

Once such correspondence exists, explicit realization of a known pack with no
current loader returns selection `Unavailable`; an unknown or mismatched pack
returns selection `Rejected`. Neither produces a bound request or loader
receipt. Restoration does not fall back to a Platform component, infer a
loader from either Ecosystem ID, or reuse a loader recorded by an older
process. Durable rebind is not part of the live-selection stage implemented
here.

Changing the shipped loader affects later operations. It does not mutate a
live Workspace, reinterpret a completed receipt, or alter the identity of
already admitted Libraries.

## Multiple Ecosystems and repeated loads

Two Ecosystems may load or route to the same exact Library. Their loader
receipts and Ecosystem relations remain distinct; Workspace admission applies
its own correspondence and occurrence rules rather than duplicating or merging
by display name.

Repeating one loader request is a new operation. The loader owns no hidden
single-flight or cache. Adjacent Houses and Workspace may reuse or replace
content only through their own generation and occurrence contracts.

Removing an Ecosystem registration prevents later loader selection from that
registration. It does not evict content already admitted to the Workspace.
Navigation may remove the Ecosystem route while retaining a direct Workspace or
Package route to the same exact Library.

## Failures

Failure remains attributable to the exact boundary that produced it:

| Condition | Outcome |
| --- | --- |
| Unknown Ecosystem pack | Selection `Rejected` before request construction |
| Known pack without a loader | Selection `Unavailable` before request construction |
| Loader and retained registration do not correspond | `Rejected` before capability work |
| Required host capability absent | Bound `Unavailable` retaining the loader, with no source work |
| Finite discovery or acquisition bound exhausted | `Incomplete` |
| Several active adjacent-owner candidates without precedence | Child `Ambiguous`, projected as loader `Ambiguous` |
| Platform family mismatch | Child `Rejected`, projected as loader `Rejected` |
| Source or Library construction failure | `Failed` |
| Workspace admission rejects returned content | Separate admission non-success retaining the loader receipt |
| Library owners have no adjacent Artifact session | Admission child `Unsupported`, with the owners retired |
| Artifact session has no Library owners | Admission child `Unsupported`, with the session retired |
| Unexpected admission orchestration failure | Exception retaining completed child dispositions and retiring all unconsumed authorities |
| Cancellation | Cancellation with all untransferred Library and Artifact authorities retired |

No branch converts a loader failure into an empty Workspace, a Package-origin
substitute, a direct Platform registration, or a successful Ecosystem route
with no supporting relation.

## Analogous implementation evidence

The repository's
[Integration scanner binding](integration-scanner-binding.md) is the closest
positive pattern. It uses one opaque, statically rooted, noncapturing binding;
the application catalog selects it, while the owning operation controls
invocation and result admission. Ecosystem loading adopts that separation but
adds asynchronous source work, explicit authorization, typed non-success, and
resource ownership because loading is not pure interpretation.

[MSBuild SDK resolvers](https://learn.microsoft.com/en-us/dotnet/api/microsoft.build.framework.sdkresolver)
demonstrate host-controlled extension selection with structured success and
failure. Their priority chain and folder-based or runtime registration do not
transfer: dotnet-inspect has one exact pack-selected binding and no dynamic
discovery or fallback order.

[ASP.NET Core hosting startup
assemblies](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/platform-specific-configuration)
demonstrate application enhancement selected from external configuration.
Their ambient startup execution and assembly scanning are deliberately
rejected because Workspace registration and restoration must remain
resource-free and deterministic.

NuGet's external credential-provider plugins in this repository demonstrate a
process-isolated protocol suitable for third-party authentication. That
boundary is unnecessary here: shipped Ecosystem loaders are trusted product
composition and need owner-preserving Library transfer, not an extensible
third-party protocol.

## Pathological cases

The contract must preserve these cases:

1. `.NET Runtime` loads a source-native `System.Text.Json` Library while two admitted
   `System.Text.Json` Package versions remain separate subjects and routes.
2. ASP.NET Core loads source-native
   `Microsoft.AspNetCore.Http.Abstractions` while an admitted package with the
   same Library name remains source-distinct.
3. An ASP.NET Core realization includes .NET runtime binding support; only its
   `AspNetCore` focus members receive the ASP.NET Core Ecosystem relation.
4. Browser/Wasm has no installed source capability; the same `.NET Runtime` or
   ASP.NET Core request succeeds through an authorized package-backed source
   or returns typed `Unavailable`.
5. A PlatformHouse population returns incomplete after exhausting a finite
   work bound; it transfers no Library owners, and the loader preserves its
   resource-free partial evidence as `Incomplete`.
6. An Ecosystem registration is removed while its loaded Libraries remain
   admitted; later loading through that registration rejects, while direct
   Library inspection remains valid.
7. Two Ecosystems produce a relation to one exact Library; their receipts and
   routes remain distinct without duplicate Library identity.
8. A restored `ecosystem.runtime` or `ecosystem.aspnetcore` registration has no
   current application-issued correspondence; restoration succeeds as
   registration state, and explicit realization returns visible selection
   `Rejected` without matching identity text or inventing a binding or receipt.
9. Workspace admission fails after successful PlatformHouse realization; every
   unconsumed Library owner and Artifact session is retired, and no loader
   receipt is relabeled as admission success. Authorities consumed by a typed
   Workspace outcome are never retired a second time.
10. One Platform Library owner transfers while its Artifact session remains in
    the owner batch; batch retirement starts Artifact retirement, remains
    pending without deadlock, and completes after that Library owner retires.
11. An adjacent owner returns several active candidates without authorized
    precedence; the exact child request and receipt remain visible and the
    loader returns `Ambiguous`, not `Rejected` or empty completion.
12. One session-backed completed child is accepted before a later child cannot
    be admitted; the earlier Workspace occurrence remains accepted and the
    later child reports its own disposition without whole-load rollback.
13. An ASP.NET Core completed child contains both Focus and runtime
    binding-support Libraries; all are admitted together, but only exact Focus
    members receive ASP.NET Core contribution witnesses.
14. A completed owner-bearing child has no adjacent Artifact session; its
    owners are retired and the admission result reports the unsupported shape
    without changing the loader receipt.

The motivating real assets are
`Microsoft.NETCore.App.Ref@10.0.0/ref/net10.0/System.Text.Json.dll` and
`System.Text.Json@10.0.0/lib/net10.0/System.Text.Json.dll`, plus
`Microsoft.AspNetCore.App.Ref@10.0.0/ref/net10.0/Microsoft.AspNetCore.Http.Abstractions.dll`
and
`Microsoft.AspNetCore.Http.Abstractions@2.3.0/lib/netstandard2.0/Microsoft.AspNetCore.Http.Abstractions.dll`.
Equal simple names across either pair, and equal Metadata assembly identity
where present, must not erase Platform and Package source distinction.

## Production adoption

This shared capability has ten focused stages:

1. Lock this Ecosystem Population Loading contract.
2. **Implemented:** host-neutral binding, request, outcome, receipt, and
   owner-transfer contract with a public consumer canary.
3. **Implemented:** Static Ecosystem Packs define `ecosystem.runtime` and
   `ecosystem.aspnetcore`, their package prefixes, and their independent loader
   bindings; retire the user-facing `Platform` pack identity.
4. **Implemented for live catalog-issued registrations:** Workspace Ecosystem
   Registration Handoff preserves the exact lower declaration used by
   application loader selection without moving executable callbacks into
   Workspace state. Durable correspondence reissue after portable restoration
   remains staged.
5. **Implemented:** the `.NET Runtime` and ASP.NET Core loaders expose nominal
   public inputs, retain their exact static Platform population declarations,
   invoke one host-authorized typed PlatformHouse population capability, and
   project completed or terminal results through the common handoff. Platform
   focus and binding-support roles, exact request and receipt evidence, Library
   owners, and the adjacent Artifact session remain owner-issued. Missing host
   capability and unsupported exact-Library demand return typed `Unavailable`
   without source work or Package fallback. Package-backed and installed
   materializers return the common capability outcome directly, and the loader
   rejects a returned request for a different Platform family only after
   retiring any completed authorities.
6. **Implemented:** compose completed and incomplete owner-bearing results with
   ordinary Workspace admission. Retain exact loader, child, role, admission,
   and occurrence correspondence; issue historical contribution witnesses
   only for accepted Focus Libraries; report unsupported owner shapes and
   preserve per-child dispositions without whole-load rollback.
7. **Implemented:** project accepted Focus witnesses through a one-way adapter
   into closed Navigation-consumable point-in-time contribution evidence. Exact
   historical admission remains available only while the exact historical
   Ecosystem occurrence and contribution relation remain present in the
   caller-supplied current registration revision; a foreign current revision is
   rejected, removal and removal/re-addition stay typed, and no Package ancestry
   is inferred. The adapter consumes the inseparable owner-issued Focus witness,
   and its internal result constructors do not accept independently
   substitutable registration, admission, and Library values.
8. Have Navigation issue and reconcile current `.NET Runtime` and ASP.NET Core
   subjects and routes from that contribution evidence.
9. Adopt equivalent explicit loading in the CLI and Browser/Wasm; Browser uses
   only supported non-installed source capabilities.
10. Retire direct host-local platform population activation and include the
   behavior in a separately authorized release and production deployment.

This sequence composes with the overall Workspace-rooted Navigation plan in
issue #7301. It does not replace that plan's Ecosystem Overview, Registry,
saved Workspace, editing, Spotlight, or presentation stages.

## Evidence and required gates

The design-only PR is Markdown-only and requires `markdownlint`. Implementation
stages add these focused Release gates:

| Property | Required gate |
| --- | --- |
| Static binding | Construction accepts one target-free static method and rejects captures and invocation lists |
| Non-action | Catalog discovery, plan construction, registration mutation, serialization, and restoration invoke no loader or source capability |
| Exact selection | Missing or mismatched correspondence returns an unbound selection non-success; one bound request invokes only the exact selected binding |
| Association | Every bound invocation outcome retains Workspace revision, Ecosystem registration, loader, demand, capability plan, child requests, and receipts |
| Exact Platform evidence | A Platform child retains the exact owner-issued PlatformHouse request snapshot and population receipt; foreign-parent composition rejects |
| Ambiguity | A child owner that issues ambiguity remains child and loader `Ambiguous`, never rejection or empty completion; no Platform population ambiguity is claimed without a typed producer |
| Complete result | `Completed` requires every request-relevant child contribution to complete; omission and bounded exhaustion remain incomplete |
| Partial ownership | A loader-level incomplete result transfers owners only from independently completed children; incomplete PlatformHouse work transfers none |
| Admission separation | A loader has no Workspace mutation authority; the host-neutral composer retains the loader receipt while Workspace owns admission and occurrence publication |
| Admission correspondence | Every accepted Library joins the exact loader receipt, child settlement, owner-issued roles, Workspace admission receipt, and occurrence |
| Per-child atomicity | One session-backed child enters Workspace atomically; earlier accepted children are not rolled back when another child rejects, fails, or is unsupported |
| Focus contribution | Every accepted Focus Library receives one historical Ecosystem contribution witness; binding-support-only Libraries receive none |
| Navigation intake | The Focus-only adapter preserves exact historical admission and returns current contribution evidence only for the same Workspace and exact declaration retained by the supplied current revision |
| Unsupported admission shape | A Library-bearing child without an Artifact session, or a session without Libraries, is retired and reported as unsupported without changing the loader outcome |
| Owner disposition | Every returned Library owner and adjacent Artifact session transfers once or is retired on non-success, cancellation, unsupported shape, or partial admission; Artifact retirement follows untransferred Library retirement and cleanup failure remains visible |
| Product family correspondence | A Runtime or ASP.NET Core loader rejects a Platform outcome whose request names another family; a completed mismatch retires every Library owner and Artifact authority before returning |
| Adapter composition | A public package-backed capability wraps the package PlatformHouse adapter, completes the Runtime loader, admits its Libraries, and projects their exact Focus witnesses through Navigation intake without private conversion, reflection, or friend access |
| Platform-family source distinction | Runtime and Package `System.Text.Json`, and ASP.NET Core and Package `Microsoft.AspNetCore.Http.Abstractions`, remain distinct through loading, admission, and Navigation |
| ASP.NET Core focus role | Runtime binding-support Libraries do not receive an ASP.NET Core Ecosystem relation without an independent exact witness |
| Host parity | CLI and Browser/Wasm issue equivalent logical requests and interpret the same outcomes with different authorized source plans |
| Visible absence | Missing loader or host capability returns typed `Unavailable` without a Platform fallback |

No repository-wide absence gate is required for dynamic plugin APIs or direct
Workspace mutation. Those are ownership and construction-shape rules enforced
by focused API review. Positive tests over the public binding and loader
operation prove the supported boundary.

## TLA+ assessment

No new TLA+ model is required for the first contract slice. One invocation has
a linear request-to-terminal-result transition and delegates source and
Workspace publication state to existing owners. The important joins are exact
owner-issued identities and receipts, covered by construction and outcome
gates.

If a later implementation introduces shared in-flight operation coalescing,
retry scheduling, or asynchronous admission publication inside this owner,
that new stateful protocol requires a focused model before adoption.

## Non-claims

This design does not define:

- dynamic, third-party, reflection-discovered, or package-installed plugins;
- arbitrary callbacks, `IServiceProvider`, or string-keyed capability lookup;
- Ecosystem catalog identity, display order, or pack membership;
- lower Workspace registration construction or persistence format;
- platform target selection, source realization, or PlatformHouse internals;
- package-prefix expansion or PackageHouse internals;
- Workspace admission, replacement, revision publication, or lifetime;
- Library identity, ownership, or borrowing;
- Navigation identity, route, reconciliation, or presentation;
- automatic loading during registration or restoration;
- eviction when a registration is removed;
- loader result caching, retry, or single-flight behavior;
- support for Windows Metadata; or
- a claim that every Ecosystem needs or can provide a complete special
  population.
