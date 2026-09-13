# Workspace ecosystem registration handoff

## Status and approved scope

This document is the normative owner for the catalog-to-Workspace ecosystem
registration handoff tracked by
[#6307](https://github.com/richlander/dotnet-inspect/issues/6307). It is one
focused slice of stage 3 in
[#6012](https://github.com/richlander/dotnet-inspect/issues/6012), revised by
the construction-ownership replacement
[#6570](https://github.com/richlander/dotnet-inspect/issues/6570).

The first production consumers are the Ecosystems-owned curated Workspace
constructor and explicit ecosystem-registration editing in both the CLI and
Inspect Web. The handoff is shared substrate between the application ecosystem
catalog and reusable Workspace registration. It is not a new ecosystem
catalog, source resolver, Workspace state model, call-graph request, or
host-specific adapter.

The lower identity, declaration envelope, three-arm population union, and
public consumer canary are implemented by
`DotnetInspector.Queries.WorkspaceEcosystemRegistrationDeclaration` under
[#6640](https://github.com/richlander/dotnet-inspect/issues/6640).
[Workspace Scope](workspace-scope-and-expansion.md#inert-registration-adoption)
adopts retention, empty/explicit initialization and exact-revision replacement
under #6577. Application pack correspondence and platform-curated/all-known
construction are implemented under #6786. Persistence and host activation
remain in their separately owned slices.

[Subject Relations](subject-relations-workflows.md#broad-discovery-by-default),
approved in #6763, supplies the two construction intents adopted here. The
platform composition keeps its narrower meaning; all-known construction adds
every shipped ecosystem, including Aspire. This replaces the earlier
one-composition restriction without adding product policy to Workspace.

## Authority and exact claim

**Workspace Ecosystem Registration Handoff** owns:

> Given one application-issued ecosystem-pack correspondence and one complete
> immutable set of owner-issued Workspace-relevant contributions, construct
> one resource-free lower-layer ecosystem-registration declaration or one
> typed rejection. The application catalog may project a selected pack and use
> either separately authored ordered product manifest to construct a new
> independent Workspace through the neutral Workspace API, without lower
> layers referencing or rediscovering the catalog.

The owner defines:

- the lower registration identity and immutable declaration envelope;
- the explicit correspondence between one `EcosystemPackId` and one lower
  declaration;
- the closed version-1 contribution slots crossing this boundary;
- complete declaration, correspondence, and product-manifest validation;
- single-pack projection outcomes; and
- curated Workspace construction and the non-action rules for projection.

It consumes without redefining:

- Ecosystem Packs' application identity, manifest, display metadata, retrieval
  knowledge, prefix entries, and Integration scanner selection;
- Source Selection's `PackagePrefixDeclaration`;
- Source Selection's `ExactLibrarySourceCoordinate` and
  `PlatformLibraryPopulationDeclaration`;
- Packages' `PackageCoordinate`;
- Integration's `EcosystemIntegrationScannerBinding`; and
- Workspace Scope's registration revision, mutation and snapshot contracts.

## Why this owner is needed

`EcosystemPackId` lives in `DotnetInspector.Ecosystems`. That application
catalog references Queries and is intentionally consumed only by the CLI and
`DotnetInspect.Web.Interop.Catalog`. Reusable Queries and
`DotnetInspect.Web.Core` cannot reference the catalog without reversing the
layering boundary or creating a project cycle.

Passing only `EcosystemPackId.Value` downward would avoid the project reference
while losing the contract. A string does not carry issued correspondence,
contribution completeness, owner-issued source values, or the distinction
between a known pack with no Workspace projection and an unknown pack.

Copying catalog metadata into each host is also invalid. It would create two
curated manifests, two identity maps, and host-specific decisions about which
prefixes or platform populations belong to an ecosystem.

The handoff instead performs one application-owned projection. Ecosystems uses
that projection to construct the curated product Workspace. Lower consumers
retain the resulting declarations and never call back into
`DotnetInspector.Ecosystems`.

The Workspace API does not ask the catalog for defaults. It defaults to an
empty Workspace and accepts ordinary explicit registration, including a
complete explicit initial set. This reverses the former relationship: the
product-specific Ecosystems layer depends on and invokes neutral Workspace
construction; Workspace never depends on, invokes, or offers an option for
Ecosystems curation.

## Product scenario served

This handoff is the declaration boundary inside the end-to-end scenario owned
by [Workspace registration and call-graph focal
length](workspace-registration-and-call-graph-scope.md#end-to-end-product-scenario):

```text
discovery-oriented command or Spotlight selection
  -> Ecosystems creates the current curated Workspace
     -> project Platform, ASP.NET Core, and Microsoft.Extensions
        into complete lower ecosystem declarations
        -> initialize one fresh neutral Workspace explicitly
           -> add the selected package as membership
              -> focus a Library, type, or member
                 -> run a bounded graph over the requested focal length
                    -> persist expanded registrations, not curated intent
```

The declaration added by this slice carries the inert information needed at the
second step. For example, the ASP.NET Core declaration can retain both its
Platform population and `Microsoft.AspNetCore.` package-prefix population,
while the Platform declaration retains the .NET runtime population. Later
consumers can select those exact contributions without importing the
application catalog or reconstructing them from display text.

The handoff does not decide that the discovery operation should be curated,
admit the selected package, establish focus, choose `Everything`, resolve a
reference, apply Platform pruning, or serialize a Workspace. Those decisions
remain with the host, Workspace, graph, resolution, pruning, and persistence
owners named by the experience specification. The handoff makes their
composition possible while preserving each decision as an explicit typed
boundary.

The contrasting high-fidelity scenario deliberately bypasses this curated
factory. Package-mode `depends` constructs raw and keeps package-authored
dependency routes as evidence. Both scenarios use the same neutral Workspace
API; only the caller's construction choice differs.

## Boundary shape

```text
EcosystemPackRegistration
  EcosystemPackId
  display metadata and application actions
  optional explicit Workspace projection
        |
        v
EcosystemWorkspaceRegistrationProjection
  exact EcosystemPackId correspondence
  WorkspaceEcosystemRegistrationDeclaration
        |
        v
Ecosystems Workspace constructors
  authored platform and all-known manifests
        |
        v
neutral Workspace API
  empty default construction
  complete explicit initial registration
  later explicit registration
```

`EcosystemPackCatalog` exposes:

```text
SelectWorkspaceRegistration(EcosystemPackId)
  -> Known(WorkspaceEcosystemRegistrationDeclaration)
   | Unavailable(EcosystemPackId)
   | Unknown(EcosystemPackId)

CreatePlatformWorkspace()             -> new synchronous InspectionWorkspace
CreatePlatformWorkspaceAsynchronous() -> new asynchronous InspectionWorkspace
CreateWorkspace()                     -> new synchronous InspectionWorkspace
CreateWorkspaceAsynchronous()         -> new asynchronous InspectionWorkspace
```

Grammar-invalid external text is rejected before exact lookup by the existing
application identity boundary. `Unavailable` means the pack is known but the
product build contributes no lower Workspace declaration. `Unknown` means no
pack registration has that exact application identity.

The `Asynchronous` suffix selects the Workspace's lifetime mode, not deferred
construction; all four calls synchronously return their newly initialized
Workspace. Discovery exposes `HasWorkspaceRegistration`, and single-pack
selection returns the exact retained declaration rather than constructing it.

Curated construction has no partial or unavailable success shape. The static
product manifest is invalid if any authored entry cannot project a complete
population declaration. The catalog may retain an internal immutable
declaration sequence for validation and construction, but public callers obtain
the curated Workspace rather than a default list that they could reinterpret
or apply inconsistently.

## Lower identity and issued correspondence

`WorkspaceEcosystemRegistrationId` is the lower-layer identity retained by
Workspace state. It uses the same canonical external spelling as the
corresponding pack:

```text
ecosystem.<name>
```

Its grammar matches the application identity grammar so persisted and
displayed identity has one spelling. Grammar validity does not prove that a
pack exists or that a declaration was issued.

The application catalog explicitly pairs one `EcosystemPackId` with one
`WorkspaceEcosystemRegistrationDeclaration`. The pair is the correspondence
authority. Equal strings without that pair are not correspondence.

Complete catalog construction requires:

- equal canonical values within the explicit pair;
- at most one lower identity for each application pack;
- at most one application pack for each lower identity; and
- preservation of the exact paired declaration through lookup and curated
  projection.

The equal-value check prevents one product concept from acquiring two external
spellings. It does not authorize a lower consumer to recreate a declaration
from a string or infer an application pack from a lower identity.

## Lower declaration

`WorkspaceEcosystemRegistrationDeclaration` is immutable, resource-free, and
Browser/Wasm-safe. Conceptually it carries:

```text
WorkspaceEcosystemRegistrationDeclaration
  Id                 WorkspaceEcosystemRegistrationId
  NamespaceRoots     immutable authored-order namespace hints
  CorePackages       immutable authored-order PackageCoordinate priorities
  Populations        immutable authored-order population declarations
  IntegrationScanner optional EcosystemIntegrationScannerBinding
```

The declaration contains no title, summary, display order, demo, tool package,
package-set identity, prefix display metadata, host action, source
authorization, operation bound, Workspace revision, or acquired content.

Construction rejects a declaration with no contribution in any slot. This
prevents a known display-only pack from becoming an apparently useful
Workspace registration. A nonempty knowledge or scanner contribution can
still form a declaration, but it does not pretend to supply a call-graph
population.

Each contribution retains its owner's exact value. The handoff does not parse
namespace roots into package names, turn core packages into curated
membership, lower prefixes into query requests, inspect scanner targets, or
translate platform populations into package coordinates.

### Retrieval knowledge

`NamespaceRoots` and `CorePackages` preserve the exact immutable knowledge
already owned by Static Ecosystem Packs. Their validation, descriptive
semantics, authored order, overlap rules, and non-exhaustive meaning remain
defined there.

The handoff adds no positional relationship between a namespace root and a
core package. A root miss is not absence, and a core-package entry is not an
instruction to acquire or admit that package.

### Population declarations

The version-1 population union is closed:

```text
WorkspaceEcosystemPopulationDeclaration
  = ExactLibrary(ExactLibrarySourceCoordinate)
  | Platform(PlatformLibraryPopulationDeclaration)
  | PackagePrefix(PackagePrefixDeclaration)
```

The exact-library arm retains the
[Exact Library Source Coordinate](exact-library-source-coordinate.md) issued by
Source Selection. It preserves that owner's exact package/Platform source arm
and Metadata assembly identity without inspecting, reparsing, or realizing it.

The platform arm retains the
[Platform Library Population Declaration](platform-library-population-declaration.md)
issued by Source Selection. The declaration retains the lower
[Platform Family](platform-target-currency.md#platform-family) for the .NET
runtime or ASP.NET Core logical library population. This document does not
define target selection, reference or implementation view, library inventory,
acquisition, realization, completion, or failure behavior.

The package-prefix arm retains `PackagePrefixDeclaration`, not
`PackagePrefixRequest`. The registration records literal source intent without
choosing a package maximum, prerelease policy, source, deadline, or
acquisition budget. A consumer selecting the population supplies those finite
operation policies.

Authored population order is preserved for stable diagnostics and
prioritization inputs. It is not first-match binding precedence, fallback
permission, or permission to stop after one contribution. A consumer claiming
a complete ecosystem population must apply every request-relevant
contribution or return its own typed incomplete or unavailable result.

Duplicate exact-library coordinates, platform population identities, and
package-prefix declarations within one ecosystem are rejected through their
owners' equality. Cross-arm overlap does not establish duplication: an exact
Platform Library may coexist with its broad Platform population, and an exact
package Library may coexist with a matching package prefix. Those values
describe different population intent and remain in authored order.

### Integration contribution

The optional scanner slot retains the exact
`EcosystemIntegrationScannerBinding` already issued by Integration. Projection
does not invoke it. Workspace registration does not make Integration scanning
part of every operation.

A consumer that selects registered ecosystems for an Integration operation may
hand the binding to Integration orchestration under that owner's execution,
evidence, completion, and failure contract. A call-graph consumer ignores the
slot.

## Excluded catalog actions

The lower declaration deliberately excludes:

- `PackageSetId` and package-set membership;
- product demos and their Workspace Definitions source bindings;
- tool-package references;
- prefix-entry IDs, titles, summaries, and action order; and
- pack title, summary, discovery order, and other presentation metadata.

Registering an ecosystem does not select its curated package set, create
package Roots, install a tool, construct demo records, or choose a host action.
If a later population owner needs package-set-derived input, that owner must
issue a lower typed population declaration without moving application
`PackageSetId` or registry access into reusable Workspace code.

## Application projection

Static Ecosystem Packs remains the sole application owner of pack registration
and the shipped manifest. A pack may contribute zero or one explicit Workspace
projection.

Single-pack selection:

1. validates or receives an exact `EcosystemPackId`;
2. performs exact application-catalog lookup;
3. returns `Unknown` when no pack has that identity;
4. returns `Unavailable` when the pack has no lower projection; and
5. otherwise returns the exact retained lower declaration.

Selection performs no package-set lookup, prefix query, source availability
probe, platform inventory build, acquisition, scanner invocation, Workspace
mutation, or call-graph construction.

The catalog does not synthesize a declaration on selection from descriptor
display properties. Complete construction pairs and validates the declaration
once; discovery and selection reuse that immutable value.

## Curated Workspace manifest

The application catalog owns two separate authored sequences:

```text
PlatformProductWorkspace
  ecosystem.platform
  ecosystem.aspnetcore
  ecosystem.microsoft-extensions

AllKnownProductWorkspace
  ecosystem.platform
  ecosystem.aspnetcore
  ecosystem.microsoft-extensions
  ecosystem.aspire
```

This order is product policy. It is not derived from pack discovery order,
alphabetical order, package-set order, or namespace roots. The current pack
discovery order places Microsoft.Extensions before ASP.NET Core, so filtering
the ordinary pack manifest would produce the wrong curated order.

Complete validation of either manifest requires:

- at least one authored application identity;
- no duplicate application or lower identity;
- every application identity to resolve to one explicit projection; and
- every projected declaration to contain at least one population contribution.

All-known validation additionally requires every registered application pack
to occur in its manifest, including packs whose projection is unavailable.
Such a pack fails complete construction; it is not silently filtered out.
Unknown, unavailable, duplicate and hints-only rows are product-construction
errors, not shorter successful Workspaces.

These sequences are the current product policy, not a permanent closed grammar.
Ecosystems may add, remove, or reorder entries in a later product build with
updated real-scenario evidence and gates. Both orders are authored independently
of discovery; the all-known set check validates coverage without deriving order.

The initial target contributions are:

| Curated registration | Required population declarations |
| --- | --- |
| Platform | `Platform(DotNetRuntime)` |
| ASP.NET Core | `Platform(AspNetCore)` and `Microsoft.AspNetCore.` package prefix |
| Microsoft.Extensions | `Microsoft.Extensions.` package prefix |
| Aspire (all-known only) | `Aspire.` package prefix |

Namespace roots and core-package priorities remain additional inert knowledge.
They cannot satisfy the curated population requirement by themselves.

Aspire is absent from platform curation and present in all-known construction.
Its `Aspire.Hosting` priority and exact Integration-owned scanner binding are
retained alongside the prefix. This supports the real `Aspire.Hosting.Redis`
scenario without resolving that package or invoking the scanner. Platform
retains its runtime declaration for scenarios such as `System.Text.Json`,
without inventing a package coordinate for the framework library.

Adding or removing an entry is an application-manifest change, not a Workspace
Scope default embedded in CLI, Browser, Queries, or persisted data. All-known
means all shipped ecosystem declarations, not every package that might integrate
with those ecosystems or every matching package acquired.

The catalog offers the same construction choices for the CLI and through the
Inspect Web application boundary. Browser Core receives only the resulting
lower Workspace state. Hosts consume that construction result rather than
determining curation from ordinary pack discovery or asking Workspace to
discover defaults.

The curated API is a factory, not a singleton. Every successful call returns a
new Workspace with independent identity, lifetime, registrations, content, and
revision history. Existing Workspaces do not observe later catalog changes.
Each intent's two construction entry points mirror the Workspace owner's existing
synchronous and asynchronous lifetime modes. Ecosystems selects no new close,
cleanup, or artifact-session behavior; callers close the returned Workspace
under its ordinary owner-issued contract.

## Construction, completeness, and failure

Declaration, catalog, and curated Workspace construction are deterministic over
static immutable values. They perform no I/O and have no operation-level
`Incomplete` outcome. An invalid declaration, correspondence, or curated
manifest is a product-build defect and fails complete construction visibly.

Curated construction first validates the complete static manifest, then passes
the complete ordered registration set to the Workspace owner's atomic explicit
initialization path for the selected lifetime mode. That Workspace API is raw,
not curated: an empty input constructs empty, and no option names or discovers
the product manifest. The Workspace owner retains registration validation,
initial revision, failure, and cleanup semantics. Ecosystems returns one fully
initialized Workspace or propagates the owner-issued failure; it never returns
a partially registered Workspace or substitutes a shorter manifest.

Runtime work begins only after a consumer selects a contribution. Platform or
package-prefix owners retain their own `Unavailable`, `Rejected`,
`Incomplete`, cancellation, deadline, and capacity outcomes. The handoff does
not turn one of those results into an empty population or retry through another
contribution.

Lower declaration construction snapshots each supplied sequence exactly once
into an immutable array and then validates that snapshot. It retains the
original immutable element values and the scanner binding by reference without
invocation or introspection.

Lower declaration construction validates:

- canonical lower identity;
- non-null contribution sequences and elements;
- namespace-root shapes and ordinal case-sensitive duplicates;
- Packages-owned coordinate validity, unversioned and target-neutral
  core-package roles, and ordinal-ignore-case duplicate package IDs;
- non-null population payloads and owner-defined equality for exact-library,
  Platform, and package-prefix duplicates;
- duplicate identities within each contribution domain; and
- at least one knowledge, population, or scanner contribution.

The handoff owns admissibility for its raw namespace and role-specific
core-package slots. It does not reparse package-prefix declarations, inspect
exact-library fields, reinterpret Platform families, or invoke scanner
bindings.

It does not establish that:

- a namespace or package exists;
- a package prefix has any matches;
- a platform pack is installed or acquirable;
- a core package or matching prefix result is compatible with a request;
- a scanner will find an Integration;
- a source operation will complete within its bounds; or
- the ecosystem population is exhaustive.

## Pathological cases

### Equal text without correspondence

A lower declaration and an application pack both spell
`ecosystem.example`, but they were not paired in the static registration.
Exact string equality does not authorize projection. Catalog selection returns
its retained pair or `Unavailable`; it never searches lower declarations by
text.

### Display-only pack

A pack contributes a title, demos, and a curated package-set action but no
Workspace declaration. It remains a valid application pack. Workspace
projection returns `Unavailable` rather than an empty declaration.

### Hints-only curated entry

A curated row resolves to a declaration with namespace roots and core packages
but no population contribution. The ordinary declaration is valid for
knowledge consumers, but complete curated-manifest construction rejects it.
Curated Workspace construction never silently registers a population that a
call-graph consumer cannot form.

### Shared-framework and package overlap

ASP.NET Core contributes both `Platform(AspNetCore)` and
`Microsoft.AspNetCore.` package-prefix intent. The handoff retains both. It
neither deduplicates by namespace nor treats one as fallback for the other.
The population owner distinguishes ASP.NET Core focus members from .NET
runtime binding support; consumer and assembly-reference resolution owners
decide request-relevant population and binding from their typed evidence.

An exact ASP.NET Core Library may also appear beside
`Platform(AspNetCore)`, and an exact package Library may appear beside a
matching package prefix. These are deliberate cross-arm overlaps, not
duplicates. Exact coordinates name one Library; broad declarations name
populations.

### Prefix bound leakage

Two consumers select the same Microsoft.Extensions declaration. One searches
at most 25 packages and another at most 500. Both retain the exact same
`PackagePrefixDeclaration`; their operation requests and completion differ.
The product manifest does not encode either bound.

### Curated projection unavailable

The curated manifest names Platform, but the pack registration lacks its
source-owned platform population declaration. Curated construction fails
visibly. It does not omit Platform and return a shorter Workspace.

### Product curation changes

One product build constructs the initial three-registration Workspace. A later
build changes its platform manifest. New platform construction uses the later
complete sequence; an existing Workspace and a restored definition retain
their exact expanded registrations. Neither carries a `curated` mode that is
re-evaluated against the later catalog.

## Analogous implementations

[VS Code contribution points][vscode-contribution-points] demonstrate a useful
separation: static declarations describe available contributions before the
extension implementation is activated. This design transfers the
declaration-before-execution property. It does not transfer runtime plugin
discovery, dynamic installation, activation events, or an extension-host
object model.

[ASP.NET Core framework references][aspnet-framework-reference] demonstrate
that a product-owned platform family is not equivalent to a NuGet package
prefix. `Microsoft.AspNetCore.App` selects a shared-framework contract, while
additional ASP.NET Core packages remain package-source content. This design
transfers that source-domain distinction, not MSBuild evaluation, installed
framework probing, or compile-time reference semantics.

These systems are evidence for static declaration and source-domain
separation, not authorities for dotnet-inspect identity, Workspace, or
operation behavior.

[vscode-contribution-points]: https://code.visualstudio.com/api/references/contribution-points
[aspnet-framework-reference]: https://learn.microsoft.com/aspnet/core/fundamentals/target-aspnetcore

## Ownership and adoption

| Owner | Responsibility retained |
| --- | --- |
| This handoff | Lower identity, declaration envelope, explicit pack correspondence, projection outcomes, platform/all-known manifest validation, and complete construction |
| [Static Ecosystem Packs](ecosystem-packs.md) | Application pack identity, static contributions, display/actions, shipped pack manifest, and authored curated-Workspace choices |
| [Typed Source Intent](search-scope-domain.md) | Package-prefix declaration validation and later request policy |
| Platform source owner | Platform population identity, inventory, target selection, acquisition, completion, and failures |
| [Integrations](integrations.md) | Scanner execution, observations, concepts, evidence, completion, and failures |
| [Workspace Scope](workspace-scope-and-expansion.md) | Empty construction, explicit registration revisions, edits, complete snapshots, and retained declarations |
| [Workspace Definitions](workspace-definitions.md) | Portable complete expanded registrations, including an empty set |
| Call Graph and resolution owners | Population selection, finite work, graph construction, binding, completion, and results |
| CLI | Per-command raw-versus-curated choice, disclosure, and rendering |
| Inspect Web | Raw-versus-curated experience choice, editing, managed transport, and interaction |

There are nine counted adoption steps:

1. Lock this focused handoff design under #6307.
2. Implement the Source-Selection-owned .NET runtime and ASP.NET Core library
   population declarations defined by #6328.
3. Implement the lower identity, declaration, three owner-issued population
   arms, validation, and public consumer canary in Queries.
4. Add explicit lower projections with package-prefix populations, exact
   selection outcomes, and the separately authored platform/all-known manifests
   to Static Ecosystem Packs (#6786). Standalone prefix discovery actions remain
   with their catalog/host adoption.
5. Adopt retained ecosystem declarations, empty default construction, and
   complete explicit initialization in Workspace Scope without adding
   application-catalog dependencies or a curated option.
6. Have Ecosystems construct a fresh independent Workspace from either complete
   manifest through the public Workspace API (#6786).
7. Adopt portable declarations and exact expanded registration state,
   including an empty set, in Workspace Definitions.
8. Have each CLI command explicitly choose raw Workspace construction or the
   Ecosystems curated constructor.
9. Have the Inspect Web application boundary make the same explicit choice,
   then adopt Workspace editing without another curated table.

Exact-library identity remains owned by its completed Source Selection slice,
PR #6613; this handoff only retains that value as one population arm.
Call-graph focal lengths, resolution execution, persistence UX, and host
presentation remain their later counted #6012 stages. This document does not
authorize one PR spanning the nine steps.

## Demo

The target application projection for ASP.NET Core is:

```text
SelectWorkspaceRegistration(ecosystem.aspnetcore)

Known
  Id               ecosystem.aspnetcore
  NamespaceRoots   Microsoft.AspNetCore
  CorePackages     Microsoft.AspNetCore.OpenApi
                   Microsoft.AspNetCore.Authentication.JwtBearer
  Populations      Platform(aspnetcore)
                   PackagePrefix(Microsoft.AspNetCore.)
  Scanner          absent
```

Reading this result does not inspect installed packs or query NuGet.

Construction through `EcosystemPackCatalog` obtains:

```text
CreatePlatformWorkspace()
  -> synchronous InspectionWorkspace
     Registrations
       1. ecosystem.platform
       2. ecosystem.aspnetcore
       3. ecosystem.microsoft-extensions

CreatePlatformWorkspaceAsynchronous()
  -> asynchronous InspectionWorkspace
     same initial registrations

CreateWorkspace()
  -> synchronous InspectionWorkspace
     Registrations
       1. ecosystem.platform
       2. ecosystem.aspnetcore
       3. ecosystem.microsoft-extensions
       4. ecosystem.aspire

CreateWorkspaceAsynchronous()
  -> asynchronous InspectionWorkspace
     same four registrations
```

Direct Workspace construction instead returns an empty registration set.
Restoring an explicitly empty registration sequence constructs raw and remains
empty; neither host calls curated construction during restoration.

The neighboring Aspire pack has a selectable lower declaration and appears only
in the all-known sequence. All four calls leave Package membership empty.

## Required gates

| Gate | Required observation |
| --- | --- |
| Lower declaration construction | Canonical identity, immutable snapshots, exact owner values across all three population arms, duplicate rejection, empty-declaration rejection, and authored order are preserved. |
| Explicit correspondence | Equal text without a retained pair cannot project; mismatched paired spellings and duplicate lower IDs reject complete catalog construction. |
| Projection fidelity | Known selection returns the exact retained declaration; known unavailable and unknown identities remain distinct. |
| Resource-free projection | Discovery and selection invoke no prefix query, platform source, package-set lookup, scanner, acquisition, or Workspace mutation. |
| Curated product Workspace | The current Platform, ASP.NET Core, Microsoft.Extensions order and required population contributions are enforced without filtering ordinary pack discovery. |
| All-known product Workspace | The separate current four-row order includes Aspire and every known pack; missing or unavailable projections cannot be silently omitted. |
| Independent construction | Repeated curated calls return distinct Workspace identities and lifetimes with equal initial registrations. |
| Lifetime preservation | Curated synchronous and asynchronous construction preserve the corresponding Workspace close, cleanup, artifact-session, and report contracts without an Ecosystems-owned variant. |
| Empty lower-layer default | Direct Workspace construction without explicit registrations is empty and has no path that consults Ecosystems or requests curation. |
| Complete failure | Invalid or unavailable curated entries return no partial Workspace and retain the product defect visibly. |
| Product-policy evolution | Changing the curated manifest affects new curated construction only; existing and restored expanded registration sets remain unchanged. |
| Prefix policy separation | Projected prefixes retain exact `PackagePrefixDeclaration` values and no request bound or prerelease policy. |
| Catalog dependency policy | Existing full project-and-assembly gates keep `DotnetInspector.Ecosystems` out of Queries and every inspect-web production project except `CatalogExports`. |
| Ordinary consumer canary | A non-friend consumer selects lower declarations through the public catalog surface and uses them without internal access. |
| CLI and Browser adoption | Both hosts invoke the Ecosystems-owned construction path and observe its exact initial registration sequence. |
| Pathological curation | Unknown, unavailable, duplicate, and hints-only curated rows fail visibly rather than producing a shorter success result. |

The existing ecosystem-catalog dependency gates retain full coverage for the
catalog-to-lower-layer dependency absence claim. There is no dedicated source
or API-prohibition gate policing the statement that Workspace offers no
curated option or that hosts do not determine curation independently. Those
are ownership rules enforced by design review. Required future positive Release
gates instead prove empty default construction, complete explicit
initialization, Ecosystems-owned construction, and both host call paths.
`EcosystemWorkspaceConstructionTests` gates the implemented projection,
manifest, exact-value retention, independent construction, inherited lifetime,
empty Package membership and explicit-state preservation outcomes in Release.
`EcosystemWorkspaceConstructionConsumerTests` in the existing non-friend consumer
suite exercises public construction, selection and result pattern matching.
Existing catalog suites retain
their discovery, demo and scanner gates. CLI and Browser activation and portable
restoration remain unverified future adoption, not evidence manufactured by
the direct-construction canary.

Platform and package-prefix operations keep their own Release gates; this
handoff's tests do not manufacture their population, acquisition, or completion
evidence.

## TLA+ assessment

No new TLA+ model is required for the lower declaration, immutable catalog or
validated manifests. Their construction is a deterministic mapping over exact
pack/declaration pairs, not a publication, revision, replacement or scheduling
protocol. Factories forward one complete immutable set to the existing
Workspace initializer and own no lifetime transition. Workspace's registration
model remains evidence for that owner, not a proof transferred to this
composition; the public factory outcomes have the Release gates named above.

## Non-claims

This design does not define:

- exact-library registration identity or source-owner-issued coordinates
  beyond retaining the owner-issued value;
- platform family grammar, target selection, library inventory, or
  acquisition;
- package-prefix query results, package limits, paging, source authorization,
  or completion;
- package-set projection or automatic package admission;
- Workspace registration revision, persistence, restoration, or editing;
- call-graph focal-length requests, induced populations, traversal, or
  rendering;
- assembly-reference binding or resolution;
- scanner execution or Integration result behavior;
- application display, demo, tool, or package-set actions;
- runtime plugins, dynamic registration, reflection discovery, hot reload, or
  runtime catalog mutation;
- named curated profiles, selection among historical product compositions, or
  a durable `curated` bit in persisted Workspace state; or
- a claim that every ecosystem or source can produce a complete population.
