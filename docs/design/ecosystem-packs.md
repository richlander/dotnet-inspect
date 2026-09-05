# Static Ecosystem Packs

## Status

Focused cross-cutting pattern proposal for
[#5710](https://github.com/richlander/dotnet-inspect/issues/5710), extended by
the demo-content stack in
[#5772](https://github.com/richlander/dotnet-inspect/issues/5772).

This document defines the source-level structure by which the product can
elevate a .NET ecosystem coherently through discovery metadata, an optional
curated package set, recorded package-prefix queries, and an optional
Integration scanner implementation, plus product demos that exercise ordinary
shipping sections over exact pinned inputs.

The Package Set Registry includes Microsoft.Extensions, ASP.NET Core, and the
audited 82-package Aspire inventory. The static pack registry, four-pack
manifest, ten-demo contribution, Workspace-owned lazy source binding, CLI
handoff, inspect-web facade handoff, and the corresponding active Release gates
named below are implemented. The assembly-friend tests, solution
dependency-policy rule, and strengthened inspect-web facade boundary gate are
active. The optional scanner slot and Aspire binding selection are implemented
under [#5935](https://github.com/richlander/dotnet-inspect/issues/5935), using
the Integration-owned compatibility binding. CLI/browser scanner selection
remains staged. Prefix slots remain absent until their owner issues the
required currency; existing search and full Integration behavior is unchanged.

Participating normative owner:

- [Static workspaces: definitions, assembly groups, and projections](workspace-definitions.md#product-demos-are-closed-section-presets)
  solely owns `ProductDemoSourceBinding` construction, validation, resolution,
  execution handoff, and failures.

Supporting owners:

- [Package Set Registry](package-set-registry.md) owns package-set identity,
  membership validation, immutable snapshots, discovery, and exact lookup.
- [Integrations](integrations.md) owns scanner input, concepts, evidence,
  currency, failures, completion, and query results.
- [Search scope resolution](search-scope-resolution.md) owns current CLI search
  defaults and ordered source composition.
- [Package source model](package-source-model.md) owns source authorization,
  package discovery, paging, failures, and payload acquisition.
- [Artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md)
  owns realization and workspace generations.
- [Inspection bundles and demos](../inspection-space.md#inspection-bundles-and-demos)
  owns the bundle and runtime-workspace composition boundary.
- [Capability-driven section registry spike](capability-section-registry-spike.md)
  supplies comparative evidence for a static table of noncapturing execution
  bindings rather than runtime registration or an object graph.
- [#5602](https://github.com/richlander/dotnet-inspect/issues/5602) tracks typed
  source intent and staged CLI/browser source adoption.
- [#5728](https://github.com/richlander/dotnet-inspect/issues/5728) is the
  non-normative end-to-end delivery tracker joining the focused owner work,
  application catalog, both front ends, and first complete ecosystem adoption.
- [#5770](https://github.com/richlander/dotnet-inspect/issues/5770) is the
  inspect-web consumer for one flat, inert product-demo list in the singular
  Workspace viewer.
- [#5772](https://github.com/richlander/dotnet-inspect/issues/5772) is the
  three-slice Aspire and ecosystem-demo delivery stack.

## Approved composition scope

The operator approved #5772 as one bounded two-owner composition: this owner
defines the static product-demo inventory and exact dispatch, while Workspace
Definitions issues the one lazy source-binding currency required to transfer
application-authored demo sources out of Queries. The handoff is
`ProductDemoSourceBinding` in and `ResolvedScenario` or an owner-domain failure
out. This approval does not transfer record, validation, resolution, section,
run-plan, execution, or failure semantics to the catalog and does not admit
changes to any other Workspace Definitions contract. Package-set, prefix, and
scanner owner tracks remain separate efforts.

## Authority and exact claim

The Ecosystem Pack owner defines one static source-level contribution shape and
one application-owned manifest of the contributions compiled into a product
build.

One pack registration may contain:

- stable ecosystem identity and product-owned discovery metadata;
- one optional package-set identity;
- zero or more ordered package-prefix discovery entries; and
- one optional Integration-owned static scanner binding; and
- zero or more ordered Workspace-Definitions-owned product-demo source
  bindings with application-owned display metadata.

The owner also defines:

- pack and prefix-entry identity;
- intrinsic registration and manifest-table validation;
- deterministic manifest discovery and exact lookup;
- demo-to-pack grouping, global product-demo order, grouped demo discovery, and
  flattened product-demo discovery;
- selection-time materialization boundaries; and
- the rule that adding a manifest registration adds no ecosystem-specific
  name, enum, switch arm, parser branch, or query path to reusable
  infrastructure.

It does not own:

- package-set membership, equality, or lookup;
- package-prefix query semantics, bounds, paging, or completion;
- Integration concepts, scanning evidence, currency, or results;
- package acquisition, artifact admission, workspace construction, or
  lifetime;
- demo record shape, scenario identity, validation, resolution, section or
  facet admission, run-plan lowering, execution, or failure semantics;
- source-selection defaults or cross-source deduplication;
- CLI or browser actions, rendering, or recommendations; or
- runtime plugins, registration, discovery, unloading, or mutation.

The exact claim is:

> The host-neutral application catalog defines how one ecosystem contribution
> is described, discovered, and selected. Source in that catalog defines which
> static contributions ship and supplies their data, product-demo sources, and
> scanner implementations. Product-demo inventory, grouping, display metadata,
> and product order are application-catalog concerns; demo records, resolution,
> section admission, run plans, and execution remain Workspace Definitions
> concerns. The co-located Package Set Registry remains a separate owner, while
> lower package-coordinate, query, and Integration infrastructure remains
> independent and subject to its owners' separate contracts.

## Why a pack is the product unit

A package set alone answers which package IDs the product has selected. A
package prefix answers which live source query a user may run. An Integration
scanner answers how realized package APIs are interpreted. A product demo fixes
exact package versions and a normal product view that demonstrates the
ecosystem. Users experience those capabilities as one ecosystem even though
their semantics belong to different owners.

The pack is the smallest composition unit that keeps those capabilities
discoverable together without merging their contracts:

```text
ecosystem pack
  discovery metadata
  optional PackageSetId
  zero or more package-prefix entries
  optional Integration scanner binding
  zero or more product-demo contributions
```

The registration contains references and application bindings. It does not
copy package membership, prefix execution, Integration result semantics, or
Workspace Definitions semantics. Selecting any one capability does not select
or execute its neighbors.

## Contract shape

The conceptual static data shape is:

```text
EcosystemPackRegistration
  Descriptor   EcosystemPackDescriptor
  PackageSet   PackageSetId?
  Prefixes     immutable ordered EcosystemPackagePrefix sequence
  Scanner      EcosystemIntegrationScannerBinding?
  Demos        immutable ordered EcosystemDemoRegistration sequence

EcosystemPackDescriptor
  Id           EcosystemPackId
  Title        string
  Summary      string
  Order        int

EcosystemPackagePrefix
  Id           EcosystemPackagePrefixId
  Request      PackagePrefixRequest
  Title        string
  Summary      string
  Order        int

EcosystemDemoRegistration
  Title        string
  Summary      string
  Order        int
  Source       ProductDemoSourceBinding

EcosystemDemoDescriptor
  Ecosystem    EcosystemPackId
  ScenarioId   string
  Title        string
  Summary      string
  Order        int

EcosystemDemoSelection
  Descriptor   EcosystemDemoDescriptor
  Scenario     ResolvedScenario
```

`ProductDemoSourceBinding` is issued by Workspace Definitions. It carries the
exact scenario ID and an opaque owner-defined resolution operation. The
scenario ID is the product-demo identity; the ecosystem catalog does not mint
a parallel demo ID or infer identity from title, order, package coordinate, or
ecosystem.

The public discovery boundary exposes immutable pack, prefix-action, and demo
descriptor metadata, package-set identity, and whether a scanner is available.
`EcosystemPackDescriptor.HasScanner` reports scanner availability without
exposing the binding. `EcosystemPackCatalog.SelectScanner` accepts the exact
typed pack ID and returns `EcosystemScannerSelectionResult`: `Known` carries
only the selected owner-issued binding, `Unavailable` identifies a registered
pack without that capability, and `Unknown` preserves an unregistered ID.
Neither missing case selects a neighboring scanner or a default.
Exact demo selection returns one `EcosystemDemoSelection`, retaining the
catalog descriptor beside the Workspace-Definitions-owned resolved scenario.
Hosts use descriptor title and summary for product discovery and display;
`ScenarioDefinition.Title` and `Description` remain portable definition fields,
not a second product-catalog metadata authority. Other typed selections return
only the chosen owner-issued request or binding. No discovery or selection
surface exposes a mutable application manifest, demo record factory, delegate,
or scanner implementation object.

There is no `Ecosystem`, `IEcosystemModule`, pack factory, catalog builder,
service registration, or per-pack runtime object. Registration construction is
internal to `DotnetInspector.Ecosystems`: source-authored packs and the static
manifest live in that assembly. Only immutable discovery and typed selection
surfaces are public. Neither front end can construct or publish a registration,
and no external construction path can add a pack to product discovery.

The intended host-neutral application component is
`DotnetInspector.Ecosystems`. It sits above Packages and
Queries/Workspace Definitions and Metadata/Integrations and contains the
application manifest and concrete pack source. Its only production consumers
are the `dotnet-inspect` CLI front end and the
`InspectWeb.Engine.CatalogExports` managed browser facade.
`InspectWeb.Engine.Core`, the host and sibling export facades, Packages,
Metadata, Queries, Services, Presentation, Vocabulary, and other reusable
infrastructure do not reference it. Selected owner-issued package, demo, or
scanner currencies flow from the catalog or two front ends into existing
infrastructure; the catalog itself does not flow downward.

## Identity

An ecosystem pack has one canonical identity:

```text
ecosystem.<name>
```

`<name>` is one or more lower-case ASCII alphanumeric words separated by a
single hyphen. It begins with a letter, ends with a letter or digit, and keeps
the complete identity at or below 80 characters.

Identity is ordinal and case-sensitive. There is no trimming, case folding,
label lookup, prefix inference, package-name inference, or CLI-alias lookup.
An issued identity is not reused for another ecosystem.

`EcosystemPackId.TryCreate` is the conceptual non-throwing text boundary.
Grammar-invalid text never reaches exact lookup. A grammar-valid unregistered
identity produces typed unknown.

Prefix-entry identity is local to one ecosystem pack and uses the same
lower-case alphanumeric and single-hyphen name grammar. The stable external
selection key is the typed pair:

```text
(EcosystemPackId, EcosystemPackagePrefixId)
```

This lets `ecosystem.aspire` distinguish `official` from `community` without
creating a global prefix namespace or inferring either choice from display
text.

## Static application manifest

One application-owned static table names the packs compiled into the product:

```text
ProductEcosystemPacks
  PlatformPack.Registration
  MicrosoftExtensionsPack.Registration
  AspNetCorePack.Registration
  AspirePack.Registration
```

The example names are illustrative pack source, not required core types. The
manifest is explicit and statically rooted. It uses no reflection, assembly
enumeration, `Activator`, dependency injection, configuration file, package
loading, or runtime registration API.

The manifest is fixed for one product build. Changing the shipped set is an
ordinary reviewed source change. Runtime callers cannot add, remove, replace,
reorder, or refresh registrations.

The manifest sequence is authored in strictly ascending unique descriptor
`Order`. Complete manifest validation rejects declaration order that disagrees
with descriptor order rather than sorting it. Discovery preserves that
validated sequence.

Generic catalog mechanics in `DotnetInspector.Ecosystems` consume the
registration contract but do not name a shipped ecosystem. A new application
pack supplies its own descriptor, prefix data, package-set reference, demo
sources, scanner implementation, tests, and manifest row as applicable. It
does not add an enum member, switch arm, parser branch, or special-case query
path to lower infrastructure. Separate Workspace Definitions or Integration
adoption may add owner-issued bindings, records, concepts, or policy names
under those owners' contracts; adding the manifest registration itself does
not.

Packs do not reference, invoke, order, or inherit from other packs. Shared
algorithms belong to their owning infrastructure or a separately justified
shared helper rather than one pack becoming another pack's substrate.

## Dependency boundary

The repository dependency policy is the enforcing gate for application-pack
separation. It evaluates both project references and compiled Release assembly
references, so an unused project edge and a binary edge are both visible.

Existing policy already prevents reusable `ILInspector.*` libraries selected
by its engine rules from taking a dependency on
`DotnetInspector.Ecosystems`:

- `engine-libraries-stay-below-tool-libraries` denies `DotnetInspector.*` from
  the broad engine-library set; and
- Metadata's explicit allow-only rule does not admit the ecosystem assembly.

The implementation adds one focused project-and-assembly rule,
`ecosystem-catalog-stays-in-approved-hosts`. Within
`dotnet-inspect.slnx`, it denies `DotnetInspector.Ecosystems` from every
production target except `dotnet-inspect`.

The dependency-policy solution does not include inspect-web, so it does not
claim to prove that boundary. The browser owner separately gates
`BrowserEngineLayeringTests.EcosystemCatalogIsFacadeOnly`, which reads the
evaluated direct MSBuild `ProjectReference` items for every inspect-web
production project. For each project whose declared graph can reach the catalog
facade, it also reads the Release-built assembly's metadata `AssemblyRef` rows.
`InspectWeb.Engine.CatalogExports` is the sole permitted project and compiled
assembly reference. The compiled check rejects host source that consumes the
catalog through the transitive host-to-catalog-facade project graph. Public
demo identities are runtime-valued properties rather than compile-time
constants, and the same gate rejects public literal fields before relying on
the compiled reference: a supported source use therefore cannot erase the
catalog dependency through constant inlining.
`InspectWeb.Engine.Core`, the host and sibling export facades, and every other
inspect-web production project reject both a declared edge and a compiled
catalog reference. Test projects and the focused
`DotnetInspector.Ecosystems.Consumer.Tests` non-friend canary may reference the
catalog, but only `DotnetInspector.Ecosystems.Tests` may be an assembly friend.

Together, the solution dependency policy and retargeted browser project-graph
gate provide full coverage for the production dependency claim.
`DotnetInspector.Ecosystems` consumes package-coordinate, prefix, and scanner
currencies through their public owner-issued surfaces. Demo adoption adds a
normal public reference from `DotnetInspector.Ecosystems` to
`DotnetInspector.Queries` so application source can construct definition
records and consume its `ProductDemoSourceBinding` and resolution surface.
Queries and all other lower assemblies still do not reference the ecosystem
assembly or grant it `InternalsVisibleTo`. This direction preserves L1
ownership: the application catalog supplies fixed inputs to the owner rather
than reimplementing record validation, scenario resolution, section admission,
or run-plan lowering.

The non-friend front-end canary separately proves that discovery and selection
require only the ecosystem assembly's public surface. No source-text or
string-constant scan is needed: Queries and Integrations may legitimately name
application concepts in owner-domain records and evidence, and such a scan
would conflate semantic content with an application-catalog dependency.

`DotnetInspector.Ecosystems.csproj` declares exactly one friend,
`DotnetInspector.Ecosystems.Tests`. The CLI, inspect-web facades, the non-friend
canary, and all other production and test assemblies receive no friend access.
Friendship is not an alternate registration, publication, or selection channel.

## Materialization

The application manifest follows the repository's static-registry pattern:

- discovery initializes only immutable static registration metadata;
- discovery does not resolve package-set membership, contact a package source,
  acquire an artifact, open a workspace, or invoke a scanner;
- exact lookup does not invoke the selected pack's scanner;
- selecting a package-set action returns only that referenced package-set
  identity;
- selecting a prefix action returns only that prefix request to the front end;
- grouped or flattened demo discovery returns only immutable metadata and does
  not invoke a demo source;
- selecting one demo dispatches only that demo's
  Workspace-Definitions-owned source binding; the binding constructs and
  validates only its returned peer records, resolves its exact scenario ID,
  and returns the resolved scenario beside the selected catalog descriptor; and
- selecting Integration analysis returns only the selected pack's static
  scanner binding to Integration orchestration.

The pattern does not require constructing an ecosystem object at any stage.
The scanner binding statically roots its method and may materialize one
process-lifetime delegate value when the table initializes. That value is not
a scanner or operation object, and table initialization does not invoke it. A
scanner that needs operation-local state places it in the Integration-owned
caller context or in values created by the scan operation, never in a retained
pack instance.

The runtime may preinitialize immutable static data. That timing is not a
semantic property because initialization performs no observable work,
capability resolution, I/O, scanner invocation, or pack/scanner instance
construction. The table may pay one bounded initialization cost and subsequent
discovery and exact lookup reuse it. The implementation should remain a direct
ordered table while the shipped pack count is small; it must not introduce a
runtime registration graph, dependency resolver, or factory layer to optimize
a scale the product does not have.

## Product demos

A demo contribution is application-authored content over a
Workspace-Definitions-owned source binding. The application catalog owns:

- which product demos ship in one build;
- the pack that groups each demo;
- title and summary;
- one globally unique explicit `Order`; and
- grouped and flattened metadata discovery.

Workspace Definitions retains:

- the exact scenario ID carried by the binding;
- `InspectionDefinitionRecord` types and the peer-graph contract;
- record and reference validation;
- exact scenario resolution;
- section or facet admission;
- `ProductDemoRunPlan`;
- execution semantics; and
- visible failures.

The pack registration stores one owner-issued binding and does not copy,
inspect, or reinterpret its source or records. Workspace Definitions solely
owns binding construction, admission, private source storage, record
validation, exact resolution, section admission, and failures. The catalog
consumes only the binding's public scenario identity and resolution result.

Complete ecosystem-manifest validation rejects duplicate scenario IDs,
duplicate demo orders, empty title or summary, and a pack-local demo sequence
whose orders are not strictly ascending. It does not invoke a source to
validate its records. Catalog selection dispatches only the chosen binding;
the selected application factory constructs the records, while record and
reference validation, single-scenario admission, exact resolution, and
owner-domain failure remain Workspace Definitions behavior. An owner-domain
failure remains visible rather than becoming an empty or default demo.

Demo order is global across the product rather than derived from pack order.
Grouped discovery presents packs in pack order and each pack's demos in their
ascending demo order. Flattened discovery performs one bounded metadata-only
merge into ascending global demo order. This lets the application preserve the
current interleaved product order while retaining literal ecosystem grouping.
The flat and grouped surfaces project the same registrations; there is no
second demo manifest.

Demo inputs are exact and pinned. A demo contribution may reference a package
that also appears in its pack's curated package set, but the two statements
have different semantics: the package set is an unversioned source-selection
snapshot, while the demo is one reproducible scenario over exact coordinates.
Selecting a demo does not select, resolve, count, or update the package set,
does not expand a prefix, and does not return or invoke the scanner. Selecting
another pack capability does not construct demo records.

`ecosystem.platform` is an application grouping for basic .NET product demos,
not a source-coordinate inference rule. A Platform demo may retain an exact
package coordinate when that is the existing reproducible scenario. The
catalog never infers grouping from package IDs, namespaces, titles, or
workspace coordinate kinds.

## Package-set composition

`PackageSet` is absent or contains one `PackageSetId`. Discovery shows only
that a curated-set action exists. The pack does not resolve, retain, copy, or
count the set's coordinates.

Selecting the set returns only `PackageSetId` to the front end. The ecosystem
catalog does not perform Package Set Registry lookup or own typed unknown
behavior. The front end resolves the ID through the co-located application
registry, then hands only owner-issued package-coordinate or source values to
lower orchestration. Selection does not automatically select the pack's
prefixes or scanner. A curated set is not represented as prefix expansion, and
its membership does not claim exhaustive ecosystem coverage.

Pack registration validates the typed identity but does not look it up during
discovery or selection. The application adoption suite exhaustively proves that
every shipped pack reference resolves; it uses literal expected identities
rather than deriving expectations from either registry.

This is a deliberate split between two co-located static tables. The compiled
pack registration shape carries only `PackageSetId`, not package-set
descriptors, registrations, coordinates, or registry access. Literal
application gates prove shipped references resolve, while the generic discovery
gate retains the no-lookup runtime contract without asserting static
initialization timing.

The Package Set Registry remains the only package-set authority. Its #5720
composition decision places the private shipped package-set manifest in
`DotnetInspector.Ecosystems` with `PackageSetId`; only package-coordinate
currency and validation remain below in `DotnetInspector.Packages`. One
ecosystem source unit may author both a package-set registration and a pack
registration, but the two static manifests remain separate and the pack stores
only `PackageSetId`. No reusable package, source, query, service, Vocabulary,
or browser-Core component references the application registry.

## Recorded package prefixes

Each prefix entry is an explicit discovery action with product-owned title and
summary. A pack may have no prefix, one prefix, or several independently
selectable prefixes. For example, an Aspire pack may distinguish official and
community package families.

The immutable prefix sequence is authored in strictly ascending unique
`Order`. Complete registration validation rejects an out-of-order sequence
rather than sorting it. Discovery preserves the validated sequence, so
declaration order and `Order` cannot disagree across hosts.

The prefix value will be carried by the owner-issued typed package-prefix
intent tracked under #5602. The pack does not:

- expand the prefix during discovery;
- store a package count;
- claim exhaustive source coverage;
- choose the package query's result limit;
- hide source paging or truncation;
- combine several prefixes implicitly; or
- infer a prefix from ecosystem, package-set, or Integration identity.

Prefix validation belongs to the typed source-intent or package-query owner.
The executable pack registration cannot land this slot until that owner issues
a validated request currency. This design defines the ordered discovery role
and unchanged handoff but does not establish an interim string grammar.

## Integration scanner binding

`Scanner` is absent or names one Integration-owned opaque executable binding.
It represents an ecosystem-specific semantic scanner, not a static list of
Integration concept IDs.

The binding is statically rooted and noncapturing:

```text
EcosystemIntegrationScannerBinding.Create(AspireIntegrationScanner.Scan)
```

The concrete binding, context, invocation, and result types belong to the
Integration owner. The binding exposes no public `Invoke`, delegate, or target
method after construction. Only Integration orchestration can execute it. That
owner retains:

- which realized participants are traversed;
- all SRM access and guarded decode;
- construction of an immutable decoded Integration observation context;
- Integration concept and producer-policy identity;
- actionable type/member currency;
- evidence and provenance;
- ordering and deduplication;
- partial participant failures and completion; and
- query and projection behavior.

The pack supplies only the ecosystem-specific interpretation over that
Integration-owned observation context and binds it to its registration. It
receives no `PEReader`, `MetadataReader`, workspace, artifact bytes, or
acquisition capability. It does not perform guarded decode, acquire scanner
input, or lower scanner results for a host.

The catalog never invokes the scanner. Selecting Integration analysis returns
only the selected owner-issued binding to Integration orchestration, which
invokes it under that owner's operation and failure contract. Exactly-once
scanner invocation and result fidelity are therefore Integration adoption
gates, not pack-registry gates.

One binding is sufficient for one pack. If an ecosystem needs several
classification passes, its scanner composes them behind the single
Integration-owned binding rather than exposing a runtime scanner collection or
execution graph.

The [Integration-owned contract](integration-scanner-binding.md), locked under
issue #5719 and implemented under #5902, separates common guarded traversal from
decoded observations. The broad scanner remains the behavior oracle.
The Aspire registration uses `EcosystemIntegrationScanner.AspireBinding`
directly during migration, retaining one owner-side semantic policy rather
than copying its predicates into application source. Other packs have no
scanner contribution yet.

Catalog adoption under #5935 is step 3 of the
[six-step scanner path](integration-scanner-binding.md#adoption-and-retirement)
in #5728. Explicit CLI and browser selection follow separately. Moving Aspire
interpretation fully into application source and retiring owner-side
compatibility remain the final step, after existing full-scan/presence
consumers retain their behavior. The catalog introduces neither a generic
delegate nor a parallel scan result.

## Discovery and selection

Static discovery returns every shipped descriptor in ascending unique `Order`.
Exact lookup accepts `EcosystemPackId` and returns the matching registration
view or typed unknown. It does not use titles, prefix text, package-set identity,
scanner identity, or neighboring values as aliases.

Grouped demo discovery returns each pack's immutable demo descriptors without
invoking sources. Flattened demo discovery returns the same descriptors in
global demo order and retains each descriptor's exact `EcosystemPackId`.
Exact demo selection uses the owner-issued scenario ID with ordinal,
case-sensitive equality. Unknown text does not alias by title, package,
ecosystem, or order and does not select a default. A known selection returns
its catalog descriptor and owner-resolved scenario together so a host never
re-derives product display metadata from definition records.

Discovery answers:

```text
Which ecosystems does this product build elevate, and which explicit actions
does each make available?
```

It does not choose an action. Package-set addition, prefix search, and
Integration inspection have different costs and outcomes and remain separate
explicit selections.

## Host plan

The static manifest is host-neutral application product data directly consumed
only by the CLI and `InspectWeb.Engine.CatalogExports` front ends. Neither
copies the pack list, package-set identity, prefix metadata, or scanner
availability.

The CLI may later expose ecosystem discovery or map existing source selectors
to pack capabilities. It retains token grammar, command policy, diagnostics,
Markout lowering, and progressive disclosure.

The existing `demo list` and `demo <scenario-id>` surfaces move to the
application catalog without changing their output or execution semantics.
`demo list` consumes flattened metadata only. Running a demo selects its exact
scenario ID, then consumes the Workspace-Definitions-owned resolved scenario
and run plan through the existing type/member section pipeline. Product-facing
title and summary come from the selected catalog descriptor, not from the
resolved scenario's portable metadata.

`InspectWeb.Engine.CatalogExports` projects an ecosystem action surface from
the same descriptors through its generated facade. The TypeScript front end
retains interaction and browser presentation. Browser infrastructure retains
asynchronous acquisition, budget reservation, workspace replacement, rollback,
and disposal without referencing the ecosystem catalog.

For [#5770](https://github.com/richlander/dotnet-inspect/issues/5770), the
managed facade projects the flattened demo metadata as one inert list. The
TypeScript application may ignore ecosystem grouping for that view while
dispatching the exact stable scenario ID. Listing performs no package
acquisition or demo resolution, and opening one demo continues to replace the
singular live Workspace through the existing browser path.

For Integration execution, the facade selects the opaque binding and passes
that owner-issued value with the realized operation inputs to
Integration-owned orchestration. Browser Core may carry the Integration value
through an Integration-typed parameter, but it does not reference the ecosystem
catalog or rediscover the pack.

The implemented
[JSExport facade partition](inspect-web-jsexport-partitioning.md) designates
`InspectWeb.Engine.CatalogExports` as the sole managed ecosystem-catalog
consumer and assigns the complete discovery, selection, execution adaptation,
and facade-local DTO closure to it. Sibling export facades neither consume that
facade nor reference the catalog. The TypeScript application composes the
separate facade result into its application model. Core and every other
reusable browser project remain forbidden.

The registry preserves typed data through both host boundaries. This design
defines no broad report or output format; hosts render focused discovery and
action metadata through their existing presentation owners.

## Initial packs and staged adoption

The first application adoption describes four packs from already-owned
currencies and content:

| Pack identity | Package-set identity | Product demos | Residual capabilities |
| --- | --- | --- | --- |
| `ecosystem.platform` | absent | `stj-serializer`, `stj-serialize-callgraph`, `stj-getdecimal-callgraph` | no prefix or scanner planned by this slice |
| `ecosystem.microsoft-extensions` | `package-set.microsoft-extensions` | `extensions-callgraph`, `config-bind-callgraph`, `options-add-callgraph`, `di-tryadd-callgraph`, `http-addhttpclient-callgraph` | prefix waits for #5602; no scanner contributed yet |
| `ecosystem.aspnetcore` | `package-set.aspnetcore` | none initially | prefix waits for #5602; no scanner contributed yet |
| `ecosystem.aspire` | `package-set.aspire` | `aspire-postgres-callgraph`, `aspire-redis-callgraph` | scanner selectable through the catalog; host selection remains staged; prefixes wait for #5602 |

The eight existing demo IDs, metadata, global order, records, pins, and run
plans remain unchanged. Their global orders are assigned in their current
product sequence. The two new Aspire demos follow them. The literal
demo-to-pack mapping is application policy and is not inferred from their
package coordinates or titles.

| Global order | Scenario ID | Pack |
| ---: | --- | --- |
| 100 | `stj-serializer` | `ecosystem.platform` |
| 200 | `extensions-callgraph` | `ecosystem.microsoft-extensions` |
| 300 | `stj-serialize-callgraph` | `ecosystem.platform` |
| 400 | `config-bind-callgraph` | `ecosystem.microsoft-extensions` |
| 500 | `options-add-callgraph` | `ecosystem.microsoft-extensions` |
| 600 | `di-tryadd-callgraph` | `ecosystem.microsoft-extensions` |
| 700 | `http-addhttpclient-callgraph` | `ecosystem.microsoft-extensions` |
| 800 | `stj-getdecimal-callgraph` | `ecosystem.platform` |
| 900 | `aspire-postgres-callgraph` | `ecosystem.aspire` |
| 1000 | `aspire-redis-callgraph` | `ecosystem.aspire` |

Such registrations must not change package membership, search defaults,
source order, limits, existing Integration behavior, or demo execution
semantics.

Aspire is the first intended new-pack candidate. The eventual full Aspire
capability set depends on separately approved owner work:

1. package/API evidence defines a complete current `aspire`-co-owned Aspire API
   package set;
2. the application Package Set Registry authors Aspire membership beside
   the pack source under its separate static manifest; and
3. Integrations defines and adopts the static scanner binding.

The initial Aspire row does not wait for the prefix or scanner tracks. Its
package-set and demo capabilities are independently coherent; later owner-issued
slots extend the same registration without changing those semantics.

An Aspire pack may then expose:

```text
ecosystem.aspire
  package-set.aspire
  demos     -> AddPostgres call graph, AddRedis call graph
  official  -> Aspire.
  community -> CommunityToolkit.Aspire.
  scanner   -> AspireIntegrationScanner.Scan
```

The first Aspire demo sources are exact package-local Call Graph presets:

| Scenario ID | Package | Type and member | Stable anchor |
| --- | --- | --- | --- |
| `aspire-postgres-callgraph` | `Aspire.Hosting.PostgreSQL@13.5.3` | `Aspire.Hosting.PostgresBuilderExtensions.AddPostgres` | `e5a66a2bd9` |
| `aspire-redis-callgraph` | `Aspire.Hosting.Redis@13.5.3` | `Aspire.Hosting.RedisBuilderExtensions.AddRedis` | `7618364a03` |

Production `dotnet-inspect` resolves both packages to `net8.0` and emits
nonempty ordinary Call Graph sections. `AddPostgres` reaches the PostgreSQL
resource and eventing path; the selected four-parameter `AddRedis` overload
retains both inbound overload delegation and outbound resource, eventing, and
health-check paths. The implementation gates exact pins and anchors rather
than re-discovering a member by display name.

Entity Framework Core, OpenTelemetry, gRPC, Orleans, and other ecosystems are
candidate packs, not registrations authorized by this design.

## Demo

Scanner selection is a shared application-catalog API, not a new CLI or
browser action:

```csharp
var selection = EcosystemPackCatalog.SelectScanner(EcosystemPackIds.Aspire);
if (selection is not EcosystemScannerSelectionResult.Known scanner)
    throw new InvalidOperationException("The shipped Aspire scanner is unavailable.");

using var session = AssemblyInspectionSession.Open(path);
var rows = session.EcosystemIntegrations(scanner.Binding);
```

On the pinned `Aspire.Hosting.PostgreSQL@13.5.3` and
`Aspire.Hosting.Redis@13.5.3` `net8.0` assemblies this yields six and four
Aspire rows respectively, retaining the same ordered rows and evidence as the
full scanner's Aspire subset. `ILInspector.Metadata.dll` is a neighboring
input with zero Aspire rows. Selecting `ecosystem.microsoft-extensions`
instead returns typed `Unavailable`; discovering or selecting any pack does
not itself run a scanner.

The flat product-demo projection preserves current order and appends Aspire:

```text
Demos

System.Text.Json                     Browse a real package API
Cross-package call graph             Trace calls across three packages
Serialize call graph                 Dense package-local STJ graph
Configuration Bind                  Recursive binder call graph
Options hub                         Inbound fan-in at AddOptions
DI TryAdd hub                       Keyed/scoped Try* fan-in
AddHttpClient                       HttpClient factory registration
JsonElement.GetDecimal              STJ number parse path
Aspire AddPostgres                  PostgreSQL resource registration graph
Aspire AddRedis                     Redis resource registration graph
```

The grouped ecosystem projection uses the same registrations:

```text
Platform
  3 demos

Microsoft.Extensions
  Add curated packages
  5 demos

ASP.NET Core
  Add curated packages

Aspire
  Add curated packages
  2 demos
```

Listing either projection constructs no definition records. Selecting
`aspire-postgres-callgraph` constructs and resolves only that scenario and
does not resolve `package-set.aspire`, expand a prefix, construct the Redis
demo, or return a scanner binding.

## Required gates

The pattern's target Release suite is `EcosystemPackRegistryTests` plus an
ordinary non-friend consumer.

| Gate | Property |
| --- | --- |
| `EcosystemPackRegistryTests.SyntheticManifestIsDiscoverableInDeclaredOrder` | Static discovery returns a literal expected synthetic descriptor and action sequence in unique explicit order. |
| `EcosystemPackRegistryTests.ExactLookupUsesOnlyTypedIdentity` | Exact ID lookup returns the enumerated registration view; labels, prefix text, package-set IDs, case variants, and unknown IDs do not alias a pack. |
| `EcosystemPackRegistryTests.InvalidStaticRegistrationsFailBeforePublication` | Duplicate pack IDs/order, out-of-order pack sequences, and empty registrations reject the complete static manifest before publishing any view rather than publishing a shortened view. |
| `EcosystemPackRegistryTests.InvalidDemoRegistrationsFailBeforePublication` | Duplicate global scenario IDs/order, empty display metadata, and non-ascending pack-local demo order reject the complete manifest without invoking a demo source. |
| `EcosystemPackRegistryTests.DiscoveryAndMaterializationDoNotInvokeDemoSources` | Materializing and discovering a synthetic manifest perform no package-set resolution, package-source or workspace work, demo-source invocation, scanner invocation, or pack/scanner instance construction; initialization timing itself is not asserted. Pack, grouped-demo, and flat-demo discovery do not resolve or execute capabilities. |
| `EcosystemPackRegistryTests.FlattenedDemoDiscoveryPreservesGlobalProductOrder` | A synthetic interleaved manifest returns one descriptor per registration in unique global demo order while retaining literal pack identity; grouped and flattened views contain the same descriptor instances. |
| `EcosystemPackRegistryTests.DemoSelectionInvokesOnlyTheSelectedSourceAndRetainsCatalogMetadata` | Exact scenario-ID selection dispatches only that binding and returns its unchanged catalog descriptor beside the Workspace-Definitions-owned resolved scenario; neighboring demo sources remain untouched, and catalog metadata may differ from portable scenario metadata. |
| `EcosystemPackRegistryTests.DemoSelectionPreservesOwnerFailures` | Unknown IDs produce typed unknown without invoking a source, while a selected source's mismatched scenario record remains an owner-domain failure rather than an empty or default demo. |
| `ProductDemoSourceBindingTests.ResolveRequiresExactlyOneMatchingScenario` | A selected source must return exactly one scenario with the binding's exact ID; absent, duplicate, and mismatched scenario records fail visibly. |
| `ProductDemoSourceBindingTests.ResolvePreservesDefinitionAndSectionFailures` | Invalid peer records and unsupported demo sections remain visible Workspace-Definitions-owned failures. |
| `EcosystemPackRegistryTests.ScannerSelectionReturnsOnlyTheSelectedBinding` | Selecting one synthetic pack returns only its scanner binding and leaves every neighboring binding unreturned and uninvoked. |
| `EcosystemPackRegistryTests.ScannerOnlyPackIsValidAndMissingCapabilityIsDistinctFromUnknownPack` | A scanner-only contribution is valid; exact selection distinguishes a known pack without a scanner from an unknown pack. |
| `ProductEcosystemPackTests.AspireIsTheOnlyShippedScannerAndRetainsTheOwnerBinding` | Literal shipped availability identifies only Aspire and selection preserves the Integration-owned compatibility binding by identity. |
| `PackageSetRegistryConsumerTests.PublicSurfaceHandsSelectedScannerToIntegrationOwner` | A non-friend consumer discovers and selects Aspire, passes only the binding to the public Integration operation, and retains typed missing-capability/unknown results. |
| `EcosystemPackRegistryTests.PrefixSelectionPreservesExactValidatedIntent` | After the prefix owner issues its currency, selecting a prefix returns that typed request unchanged and does not expand, combine, count, or execute it. |
| `EcosystemPackRegistryTests.PackageSetSelectionPreservesExactTypedIdentity` | Selecting a curated set returns only its `PackageSetId` and does not copy membership or activate another pack capability. |
| `PackageSetRegistryConsumerTests.PublicSurfaceSupportsEcosystemDiscoveryAndDemoSelection` | An ordinary non-friend front-end consumer discovers and selects available actions through only the public surface, without registration construction, manifest publication, demo factories, scanner implementation, CLI types, package clients, or workspaces. |
| `EcosystemPackAssemblyBoundaryTests.FriendsOnlyDedicatedTests` | `DotnetInspector.Ecosystems.Tests` is the assembly's only `InternalsVisibleTo`; the CLI, inspect-web facade, non-friend canary, and all other assemblies are absent. |
| `EcosystemPackAssemblyBoundaryTests.OwnerContractsRequireNoFriendAccess` | Repository-owned lower assemblies derived from the ecosystem assembly's compiled references omit `DotnetInspector.Ecosystems` from `InternalsVisibleTo`; compiling the ecosystem assembly therefore exercises only public owner contracts. |
| `eng/dependency-policy.json` rule `ecosystem-catalog-stays-in-approved-hosts` | Within `dotnet-inspect.slnx`, project and compiled assembly graphs reject every production dependency on `DotnetInspector.Ecosystems` except direct use by `dotnet-inspect`; existing IL rules independently reject the reusable IL-library edges they select. |
| `BrowserEngineLayeringTests.EcosystemCatalogIsFacadeOnly` | Public product-demo identities contain no literal fields that source access could inline without an assembly reference. Evaluated direct `ProjectReference` items reject catalog edges from every inspect-web production project except `InspectWeb.Engine.CatalogExports`. For each project whose declared graph can reach that facade, Release-built metadata `AssemblyRef` rows reject compiled catalog consumption through transitive availability. |

Application adoption adds
`ProductEcosystemPackTests.ShippedManifestMatchesLiteralPolicy` and
`ProductEcosystemPackTests.EveryPackageSetReferenceResolves` with literal
descriptor and reference expectations, plus
`ProductEcosystemPackTests.ShippedPackManifestCarriesOnlyPackageSetIdentity` to
prove the compiled registration and descriptor property shapes carry
`PackageSetId` and no package-set descriptor, registration, coordinate
sequence, or registry property.
`EcosystemPackRegistryTests.SyntheticManifestIsDiscoverableInDeclaredOrder`
constructs and discovers a pack with an unregistered package-set identity,
gating the generic registry path's no-lookup behavior.
`ProductEcosystemPackTests.ShippedDemoManifestMatchesLiteralPolicy` fixes the
ten scenario IDs, pack mapping, metadata, and global order without deriving
expectations from source records.
`ProductEcosystemPackTests.ExistingDemoSourcesPreserveDonorRecordsAndRunPlans`
resolves the transferred eight sources and pins their package coordinates,
navigation shape, type and member selection, section, and run-plan lowering to
the donor behavior.
`ProductEcosystemPackTests.AspireDemoSourcesMatchLiteralPinsAndAnchors` gates
the two exact package IDs, versions, TFMs, types, member anchors, and Call Graph
bindings.
`DemoCommandTests.Cli_EveryCallGraphDemo_Table_EmitsNonEmptyRows` gates
nonempty ordinary CLI Call Graph execution through the existing section
pipeline, including both Aspire scenarios.
`DemoCommandTests.ListUsesCatalogDescriptorMetadata` and
`BrowserProductHomeDemosTests.CatalogProjectionUsesEcosystemDescriptorMetadata`
prove both hosts use application-catalog title and summary even when the
portable scenario metadata differs.
Integration adoption owns scanner invocation and witness gates. Generic
catalog tests use synthetic pack names and do not establish built-in ecosystem
policy.

The implementation must also retain existing NativeAOT and Browser/Wasm build
coverage. Static binding values root their target methods in published output.
The first real scanner adoption must record the Browser/Wasm publish-size delta
and decide whether it warrants a retained sensor; the pattern design does not
claim that cost is zero.

## Landing sequence

Overall delivery is tracked by #5728.

The owner tracks may advance independently:

| Independent track | Owner work |
| --- | --- |
| Curated package set | #5720 places source-authored membership in the separate private application Package Set Registry, followed by registry implementation. |
| Product demo | Workspace Definitions issues the lazy source binding; #5772 transfers application-authored sources and host discovery to the ecosystem catalog. |
| Recorded prefix | #5602 issues the typed package-prefix request currency. |
| Semantic scanner | #5719 issues the opaque binding and decoded observation-context contract. |

1. Lock this focused pack pattern.
2. Advance whichever independent owner track is needed for the first real pack.
3. Under the approved #5772 two-owner composition, issue the
   Workspace-Definitions-owned lazy source binding; transfer the eight
   application-authored donor sources; add the two Aspire sources; implement
   pack identity, descriptors, private manifest, package-set and demo slots,
   grouped and flattened discovery, exact lookup and selection; and publish the
   four-pack, ten-demo manifest with its focused gates. Do not change current
   package membership, existing demo execution, or search behavior.
4. Add each remaining action slot independently when its owner track lands; no
   later slot reopens already implemented action semantics.
5. Adopt CLI and browser actions through the same implementation slice's
   application-catalog handoff; Integration remains independently adoptable.

The four owner tracks remain separately owned; #5720 records the package-set
composition decision, while prefix and scanner contracts remain residual. No
implementation slice waits for every optional slot: each lands only when its
owner-issued currency exists and one real application scenario makes that slice
coherent.

The package-set donor transfer already created
`DotnetInspector.Ecosystems`, limited friendship to
`DotnetInspector.Ecosystems.Tests`, and landed the solution dependency-policy
and inspect-web project-graph gates. The demo track adds the Queries dependency
and must keep those existing boundaries green; it does not add parallel
boundary mechanisms.

## Non-claims

This design does not define:

- runtime plugins, downloadable packs, reflection discovery, configuration
  registration, dependency injection, hot reload, unloading, or catalog
  mutation;
- an ecosystem class, module object, scanner object, factory, service
  provider, or execution graph;
- package-set membership or prefix-query results;
- a new Integration concept, classifier, evidence shape, or query;
- package acquisition or workspace behavior;
- demo definition-record, resolution, section, run-plan, or execution
  semantics;
- inference of demo grouping from package, namespace, title, or source kind;
- an implication that a demo uses, exhausts, or activates its pack's curated
  package set;
- automatic execution of every capability exposed by a pack;
- recommendation, ranking, popularity, or compatibility policy;
- generic CLI syntax or browser interaction details;
- registration of ecosystem identities in the lower shared Vocabulary catalog;
  front ends project the application catalog directly, and any future generic
  query-value adoption requires a separately owned design; or
- a requirement that every ecosystem have any particular one of package set,
  prefix, scanner, or demos; every shipped pack must still expose at least one
  capability.
