# Workspace ecosystem registration handoff

## Status and approved scope

This document is the normative owner for the catalog-to-Workspace ecosystem
registration handoff tracked by
[#6307](https://github.com/richlander/dotnet-inspect/issues/6307). It is one
focused slice of stage 3 in
[#6012](https://github.com/richlander/dotnet-inspect/issues/6012).

The first production consumers are fresh Workspace construction and
registration editing in both the CLI and Inspect Web. The handoff is shared
substrate between the application ecosystem catalog and reusable Workspace
consumers. It is not a new ecosystem catalog, source resolver, Workspace state
model, call-graph request, or host-specific adapter.

## Authority and exact claim

**Workspace Ecosystem Registration Handoff** owns:

> Given one application-issued ecosystem-pack correspondence and one complete
> immutable set of owner-issued Workspace-relevant contributions, construct
> one resource-free lower-layer ecosystem-registration declaration or one
> typed rejection. The application catalog may project a selected pack and one
> separately authored ordered default manifest through that declaration
> without lower layers referencing or rediscovering the catalog.

The owner defines:

- the lower registration identity and immutable declaration envelope;
- the explicit correspondence between one `EcosystemPackId` and one lower
  declaration;
- the closed version-1 contribution slots crossing this boundary;
- complete declaration, correspondence, and default-manifest validation;
- single-pack projection outcomes; and
- the materialization and non-action rules for projection.

It consumes without redefining:

- Ecosystem Packs' application identity, manifest, display metadata, retrieval
  knowledge, prefix entries, and Integration scanner selection;
- Source Selection's `PackagePrefixDeclaration`;
- the platform source owner's future typed library-population declaration;
- Packages' `PackageCoordinate`;
- Integration's `EcosystemIntegrationScannerBinding`; and
- Workspace Scope's future registration revision, mutation, snapshot, and
  restoration contracts.

## Why this owner is needed

`EcosystemPackId` lives in `DotnetInspector.Ecosystems`. That application
catalog references Queries and is intentionally consumed only by the CLI and
`InspectWeb.Engine.CatalogExports`. Reusable Queries and
`InspectWeb.Engine.Core` cannot reference the catalog without reversing the
layering boundary or creating a project cycle.

Passing only `EcosystemPackId.Value` downward would avoid the project reference
while losing the contract. A string does not carry issued correspondence,
contribution completeness, owner-issued source values, or the distinction
between a known pack with no Workspace projection and an unknown pack.

Copying catalog metadata into each host is also invalid. It would create two
default manifests, two identity maps, and host-specific decisions about which
prefixes or platform populations belong to an ecosystem.

The handoff instead performs one application-owned projection. Lower consumers
retain the resulting declaration and never call back into
`DotnetInspector.Ecosystems`.

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
reusable Workspace consumers
  Queries
  InspectWeb.Engine.Core
  future Workspace Scope and Definitions
```

The application catalog may expose:

```text
SelectWorkspaceRegistration(EcosystemPackId)
  -> Known(WorkspaceEcosystemRegistrationDeclaration)
   | Unavailable(EcosystemPackId)
   | Unknown(EcosystemPackId)

DiscoverDefaultWorkspaceRegistrations()
  -> complete immutable ordered declarations
```

Grammar-invalid external text is rejected before exact lookup by the existing
application identity boundary. `Unavailable` means the pack is known but the
product build contributes no lower Workspace declaration. `Unknown` means no
pack registration has that exact application identity.

Default discovery has no partial or unavailable success shape. The static
product manifest is invalid if any authored default cannot project a complete
population declaration.

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
- preservation of the exact paired declaration through lookup and default
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
  = Platform(owner-issued PlatformLibraryPopulationDeclaration)
  | PackagePrefix(PackagePrefixDeclaration)
```

The platform arm is a prerequisite owned by the platform source. It denotes a
platform-source-owned library population such as the .NET runtime or ASP.NET
Core shared framework. This document does not define its family grammar,
target selection, library inventory, acquisition, realization, completion, or
failure behavior.

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

Duplicate platform population identities and duplicate package-prefix
declarations within one ecosystem are rejected. Cross-arm textual similarity
does not establish duplication: a platform population and a
`Microsoft.AspNetCore.` package prefix describe different source domains and
may coexist.

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

## Product default manifest

The application catalog owns one separate authored sequence:

```text
ProductWorkspaceEcosystemDefaults
  ecosystem.platform
  ecosystem.aspnetcore
  ecosystem.microsoft-extensions
```

This order is product policy. It is not derived from pack discovery order,
alphabetical order, package-set order, or namespace roots. The current pack
discovery order places Microsoft.Extensions before ASP.NET Core, so filtering
the ordinary pack manifest would produce the wrong default order.

Complete default-manifest validation requires:

- exactly the three literal product identities above in that order for the
  initial adoption;
- no duplicate application or lower identity;
- every application identity to resolve to one explicit projection; and
- every projected declaration to contain at least one population contribution.

The initial target contributions are:

| Default | Required population declarations |
| --- | --- |
| Platform | source-owned .NET runtime platform population |
| ASP.NET Core | source-owned ASP.NET Core platform population and `Microsoft.AspNetCore.` package prefix |
| Microsoft.Extensions | `Microsoft.Extensions.` package prefix |

Namespace roots and core-package priorities remain additional inert knowledge.
They cannot satisfy the default population requirement by themselves.

Aspire remains a shipped pack but is not a fresh-Workspace default. Adding or
removing a default is an application-manifest change, not a Workspace Scope
default embedded in CLI, Browser, Queries, or persisted data.

The catalog publishes the same lower declaration sequence to the CLI and
`InspectWeb.Engine.CatalogExports`. Browser Core receives only lower
declarations. Neither host copies the three IDs or reconstructs declarations
from ordinary pack discovery.

## Construction, completeness, and failure

Declaration and catalog construction are deterministic over static immutable
values. They perform no I/O and have no operation-level `Incomplete` outcome.
An invalid declaration, correspondence, or default manifest is a product-build
defect and fails complete manifest construction visibly.

Runtime work begins only after a consumer selects a contribution. Platform or
package-prefix owners retain their own `Unavailable`, `Rejected`,
`Incomplete`, cancellation, deadline, and capacity outcomes. The handoff does
not turn one of those results into an empty population or retry through another
contribution.

Lower declaration construction validates:

- canonical lower identity;
- non-null immutable contribution sequences;
- existing owner validation for every carried value;
- duplicate identities within each contribution domain; and
- at least one knowledge, population, or scanner contribution.

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

### Hints-only default

A default row resolves to a declaration with namespace roots and core packages
but no population contribution. The ordinary declaration is valid for
knowledge consumers, but complete default-manifest construction rejects it.
Fresh Workspace construction never silently registers a population that a
call-graph consumer cannot form.

### Shared-framework and package overlap

ASP.NET Core contributes both the platform-source-owned shared-framework
population and `Microsoft.AspNetCore.` package-prefix intent. The handoff
retains both. It neither deduplicates by namespace nor treats one as fallback
for the other. The consumer and assembly-reference resolution owners decide
request-relevant population and binding from their typed evidence.

### Prefix bound leakage

Two consumers select the same Microsoft.Extensions declaration. One searches
at most 25 packages and another at most 500. Both retain the exact same
`PackagePrefixDeclaration`; their operation requests and completion differ.
The product manifest does not encode either bound.

### Default projection unavailable

The product default manifest names Platform, but the pack registration lacks
its source-owned platform population declaration. Static default construction
fails visibly. Discovery does not omit Platform and continue with two defaults.

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
| This handoff | Lower identity, declaration envelope, explicit pack correspondence, projection outcomes, and default-manifest validation |
| [Static Ecosystem Packs](ecosystem-packs.md) | Application pack identity, static contributions, display/actions, shipped pack manifest, and authored product-default choices |
| [Typed Source Intent](search-scope-domain.md) | Package-prefix declaration validation and later request policy |
| Platform source owner | Platform population identity, inventory, target selection, acquisition, completion, and failures |
| [Integrations](integrations.md) | Scanner execution, observations, concepts, evidence, completion, and failures |
| [Workspace Scope](workspace-scope-and-expansion.md) | Registration revisions, edits, complete snapshots, fresh construction, and retained declarations |
| [Workspace Definitions](workspace-definitions.md) | Portable registration and explicit opt-out persistence |
| Call Graph and resolution owners | Population selection, finite work, graph construction, binding, completion, and results |
| CLI | Shared-default construction, command intent, disclosure, and rendering |
| Inspect Web | Shared-default construction, editing, managed transport, and interaction |

There are eight counted adoption steps:

1. Lock this focused handoff design under #6307.
2. Have the platform source owner issue the runtime and ASP.NET Core library
   population declaration without defining it in the application catalog.
3. Implement the lower identity, declaration, validation, and public consumer
   canary in Queries.
4. Add package-prefix slots, explicit lower projections, exact selection
   outcomes, and the authored three-row default manifest to Static Ecosystem
   Packs.
5. Adopt retained ecosystem declarations and fresh defaults in Workspace Scope
   without adding application-catalog dependencies.
6. Adopt portable declarations and explicit empty/default opt-out state in
   Workspace Definitions.
7. Have the CLI obtain fresh defaults only through the catalog projection and
   pass lower declarations into shared Workspace construction.
8. Have `InspectWeb.Engine.CatalogExports` publish the same projection to
   Browser Core, then adopt Workspace editing without another default table.

Exact-library registration remains a separate source-owner slice within #6012
stage 3. Call-graph focal lengths, resolution execution, persistence UX, and
host presentation remain their later counted #6012 stages. This document does
not authorize one PR spanning the eight steps.

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

Fresh construction obtains:

```text
DiscoverDefaultWorkspaceRegistrations()

1. ecosystem.platform
2. ecosystem.aspnetcore
3. ecosystem.microsoft-extensions
```

The CLI and Browser pass that exact sequence to shared Workspace construction.
Restoring an explicitly empty registration sequence passes the empty sequence
instead; neither host calls default discovery during restoration.

The neighboring Aspire pack remains discoverable through the application
catalog but is absent from the default sequence. Selecting Aspire may return
its own lower declaration as contribution slots land; its presence in ordinary
pack discovery does not make it a default.

## Required gates

| Gate | Required observation |
| --- | --- |
| Lower declaration construction | Canonical identity, immutable owner values, duplicate rejection, empty-declaration rejection, and authored order are preserved. |
| Explicit correspondence | Equal text without a retained pair cannot project; mismatched paired spellings and duplicate lower IDs reject complete catalog construction. |
| Projection fidelity | Known selection returns the exact retained declaration; known unavailable and unknown identities remain distinct. |
| Resource-free projection | Discovery and selection invoke no prefix query, platform source, package-set lookup, scanner, acquisition, or Workspace mutation. |
| Product defaults | Literal Platform, ASP.NET Core, Microsoft.Extensions order and required population contributions are enforced without filtering ordinary pack discovery. |
| Prefix policy separation | Projected prefixes retain exact `PackagePrefixDeclaration` values and no request bound or prerelease policy. |
| Catalog dependency policy | Existing full project-and-assembly gates keep `DotnetInspector.Ecosystems` out of Queries and every inspect-web production project except `CatalogExports`. |
| Ordinary consumer canary | A non-friend consumer selects lower declarations through the public catalog surface and uses them without internal access. |
| CLI and Browser adoption | Both hosts receive one equal default declaration sequence from the catalog; neither contains a duplicate identity table. |
| Pathological defaults | Unknown, unavailable, duplicate, and hints-only default rows fail visibly rather than producing a shorter success result. |

The existing ecosystem-catalog dependency gates retain full coverage for the
catalog absence claim. Platform and package-prefix operations keep their own
Release gates; this handoff's tests do not manufacture their population,
acquisition, or completion evidence.

## Non-claims

This design does not define:

- exact-library registration identity or source-owner-issued coordinates;
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
  catalog mutation; or
- a claim that every ecosystem or source can produce a complete population.
