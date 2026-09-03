# Static Ecosystem Packs

## Status

Focused cross-cutting pattern proposal for
[#5710](https://github.com/richlander/dotnet-inspect/issues/5710).

This document defines the source-level structure by which the product can
elevate a .NET ecosystem coherently through discovery metadata, an optional
curated package set, recorded package-prefix queries, and an optional
Integration scanner implementation.

The pattern and all target gates are unimplemented. Every asserted target
property is unverified until its named Release gate lands. Existing
Microsoft.Extensions and ASP.NET Core package membership and search behavior
remain unchanged.

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
- [Capability-driven section registry spike](capability-section-registry-spike.md)
  supplies comparative evidence for a static table of noncapturing execution
  bindings rather than runtime registration or an object graph.
- [#5602](https://github.com/richlander/dotnet-inspect/issues/5602) tracks typed
  source intent and staged CLI/browser source adoption.
- [#5728](https://github.com/richlander/dotnet-inspect/issues/5728) is the
  non-normative end-to-end delivery tracker joining the focused owner work,
  application catalog, both front ends, and first complete ecosystem adoption.

## Authority and exact claim

The Ecosystem Pack owner defines one static source-level contribution shape and
one application-owned manifest of the contributions compiled into a product
build.

One pack registration may contain:

- stable ecosystem identity and product-owned discovery metadata;
- one optional package-set identity;
- zero or more ordered package-prefix discovery entries; and
- one optional Integration-owned static scanner binding.

The owner also defines:

- pack and prefix-entry identity;
- intrinsic registration and manifest-table validation;
- deterministic manifest discovery and exact lookup;
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
- source-selection defaults or cross-source deduplication;
- CLI or browser actions, rendering, or recommendations; or
- runtime plugins, registration, discovery, unloading, or mutation.

The exact claim is:

> The host-neutral application catalog defines how one ecosystem contribution
> is described, discovered, and selected. Source in that catalog defines which
> static contributions ship and supplies their data and scanner
> implementations. Lower package-set, query, and Integration infrastructure
> remains independent and subject to its owners' separate contracts.

## Why a pack is the product unit

A package set alone answers which exact package coordinates the product has
selected. A package prefix answers which live source query a user may run. An
Integration scanner answers how realized package APIs are interpreted. Users
experience those capabilities as one ecosystem even though their semantics
belong to different owners.

The pack is the smallest composition unit that keeps those capabilities
discoverable together without merging their contracts:

```text
ecosystem pack
  discovery metadata
  optional PackageSetId
  zero or more package-prefix entries
  optional Integration scanner binding
```

The registration contains references and application bindings. It does not
copy package membership, prefix execution, or Integration result semantics.

## Contract shape

The conceptual static data shape is:

```text
EcosystemPackRegistration
  Descriptor   EcosystemPackDescriptor
  PackageSet   PackageSetId?
  Prefixes     immutable ordered EcosystemPackagePrefix sequence
  Scanner      EcosystemIntegrationScannerBinding?

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
```

The public discovery boundary exposes immutable descriptor and prefix-action
metadata, package-set identity, and whether a scanner is available. Typed
selection returns the chosen owner-issued request or binding. It does not
expose a mutable application manifest or a scanner implementation object.

There is no `Ecosystem`, `IEcosystemModule`, pack factory, catalog builder,
service registration, or per-pack runtime object. Registration construction is
internal to `DotnetInspector.Ecosystems`: source-authored packs and the static
manifest live in that assembly. Only immutable discovery and typed selection
surfaces are public. Neither front end can construct or publish a registration,
and no external construction path can add a pack to product discovery.

The intended host-neutral application component is
`DotnetInspector.Ecosystems`. It sits above Packages and
Metadata/Integrations and contains the application manifest and concrete pack
source. Its only production consumers are the `dotnet-inspect` CLI front end
and the `InspectWeb.Engine` managed browser facade. `InspectWeb.Engine.Core`,
Packages, Metadata, Queries, Services, Presentation, Vocabulary, and other
reusable infrastructure do not reference it. Selected owner-issued package or
scanner currencies flow from the two front ends into existing infrastructure;
the catalog itself does not flow downward.

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

Generic catalog mechanics in `DotnetInspector.Ecosystems` consume the
registration contract but do not name a shipped ecosystem. A new application
pack supplies its own descriptor, prefix data, package-set reference, scanner
implementation, tests, and manifest row. It does not add an enum member,
switch arm, parser branch, or special-case query path to lower infrastructure.
Separate Integration adoption may add owner-issued concept or policy names
under that owner's contract; adding the manifest registration itself does not.

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
`ecosystem-packs-stay-out-of-reusable-product-libraries`. Within
`dotnet-inspect.slnx`, it denies `DotnetInspector.Ecosystems` from every
production target except `dotnet-inspect`.

The dependency-policy solution does not include inspect-web, so it does not
claim to prove that boundary. The browser owner separately adds
`BrowserEngineLayeringTests.EcosystemCatalogIsFacadeOnly`, which reads the
inspect-web project graph and permits the reference only from the current
managed front-end facade, `InspectWeb.Engine`. `InspectWeb.Engine.Core` and
every other inspect-web production project reject it. Test projects and the
focused `DotnetInspector.Ecosystems.Consumer.Tests` non-friend canary may
reference the catalog, but only `DotnetInspector.Ecosystems.Tests` may be an
assembly friend.

Together, the solution dependency policy and browser project-graph gate provide
full coverage for the current production dependency claim.
`DotnetInspector.Ecosystems` consumes package-set, prefix, and scanner
currencies through their public owner-issued surfaces; those lower assemblies
do not grant it `InternalsVisibleTo`. The non-friend front-end canary separately
proves that discovery and selection require only the ecosystem assembly's
public surface. No source-text or string-constant scan is needed: Integrations
already names concepts such as Aspire legitimately, and such a scan would
conflate semantic evidence policy with an application-pack dependency.

`DotnetInspector.Ecosystems.csproj` declares exactly one friend,
`DotnetInspector.Ecosystems.Tests`. The CLI, `InspectWeb.Engine`, the non-friend
canary, and all other production and test assemblies receive no friend access.
Friendship is not an alternate registration, publication, or selection
channel.

## Materialization

The application manifest follows the repository's static-registry pattern:

- discovery initializes only immutable static registration metadata;
- discovery does not resolve package-set membership, contact a package source,
  acquire an artifact, open a workspace, or invoke a scanner;
- exact lookup does not invoke the selected pack's scanner;
- selecting a package-set action returns only that referenced package-set
  identity;
- selecting a prefix action returns only that prefix request to the front end;
  and
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

## Package-set composition

`PackageSet` is absent or contains one `PackageSetId`. Discovery shows only
that a curated-set action exists. The pack does not resolve, retain, copy, or
count the set's coordinates.

Selecting the set returns only `PackageSetId` to the front end for handoff to
source-selection or realization orchestration. The catalog does not perform
Package Set Registry lookup or own typed unknown behavior. Selection does not
automatically select the pack's prefixes or scanner. A curated set is not
represented as prefix expansion, and its membership does not claim exhaustive
ecosystem coverage.

Pack registration validates the typed identity but does not look it up during
discovery or selection. The application adoption suite exhaustively proves that
every shipped pack reference resolves; it uses literal expected identities
rather than deriving expectations from either registry.

This is a deliberate split between inert runtime discovery and shipped-product
validity. A manually altered or mismatched build could discover an action whose
set is unknown at selection, but the literal application gate prevents such a
reference from shipping in a repository build without making discovery eagerly
materialize the Package Set Registry.

The current Package Set Registry remains the only package-set authority. This
pattern does not itself authorize pack-authored package registrations.
Allowing a pack source file to own its referenced membership is a residual
design question for that owner, tracked by
[#5720](https://github.com/richlander/dotnet-inspect/issues/5720). It may
instead retain its closed catalog and require pack-referenced sets to be
registered there.

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
EcosystemIntegrationScannerBinding.Create(
  static observations => AspireIntegrationScanner.Scan(observations))
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

The current broad `EcosystemIntegrationScanner` remains the behavior oracle
until the Integration owner adopts this pattern. Extracting Aspire or another
ecosystem-specific semantic scanner requires first separating common guarded
traversal from the decoded observation context in a focused Integration change;
it does not join the pack-contract PR.

The executable pack registration cannot land the scanner slot before that
focused owner issues the binding currency under
[#5719](https://github.com/richlander/dotnet-inspect/issues/5719). The pack
design does not introduce a generic delegate or parallel scan result as a
placeholder.

## Discovery and selection

Static discovery returns every shipped descriptor in ascending unique `Order`.
Exact lookup accepts `EcosystemPackId` and returns the matching registration
view or typed unknown. It does not use titles, prefix text, package-set identity,
scanner identity, or neighboring values as aliases.

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
only by the CLI and `InspectWeb.Engine` front ends. Neither copies the pack
list, package-set identity, prefix metadata, or scanner availability.

The CLI may later expose ecosystem discovery or map existing source selectors
to pack capabilities. It retains token grammar, command policy, diagnostics,
Markout lowering, and progressive disclosure.

`InspectWeb.Engine` may project an ecosystem action surface from the same
descriptors through its generated facade. The TypeScript front end retains
interaction and browser presentation. Browser infrastructure retains
asynchronous acquisition, budget reservation, workspace replacement, rollback,
and disposal without referencing the ecosystem catalog.

For Integration execution, the facade selects the opaque binding and passes
that owner-issued value with the realized operation inputs to
Integration-owned orchestration. Browser Core may carry the Integration value
through an Integration-typed parameter, but it does not reference the ecosystem
catalog or rediscover the pack.

The current browser exception names `InspectWeb.Engine`. If the proposed
JSExport facade partition lands first, its owner must designate exactly one
managed composition facade as the replacement exception. Other export projects
consume that facade's projected operations or DTOs and do not reference the
catalog. Core and every other reusable browser project remain forbidden.

The registry preserves typed data through both host boundaries. This design
defines no broad report or output format; hosts render focused discovery and
action metadata through their existing presentation owners.

## Initial packs and staged adoption

The first application adoption may describe the two product selections that
already exist:

| Pack identity | Package-set identity | Recorded prefix | Scanner |
| --- | --- | --- | --- |
| `ecosystem.microsoft-extensions` | `package-set.microsoft-extensions` | `Microsoft.Extensions.` | absent until focused Integration adoption |
| `ecosystem.aspnetcore` | `package-set.aspnetcore` | `Microsoft.AspNetCore.` | absent until focused Integration adoption |

Such registrations must not change package membership, search defaults,
source order, limits, or existing Integration behavior.

Aspire is the first intended new-pack candidate. A complete Aspire pack depends
on separately approved owner work:

1. package/API evidence defines a small stable Aspire Core package set;
2. the Package Set Registry decides where Aspire Core membership is authored;
   and
3. Integrations defines and adopts the static scanner binding.

An Aspire pack may then expose:

```text
ecosystem.aspire
  package-set.aspire-core
  official  -> Aspire.
  community -> CommunityToolkit.Aspire.
  scanner   -> AspireIntegrationScanner.Scan
```

Entity Framework Core, OpenTelemetry, gRPC, Orleans, and other ecosystems are
candidate packs, not registrations authorized by this design.

## Demo

The product mockup demonstrates the intended application adoption without
package or scanner work:

```text
Ecosystems

Microsoft.Extensions
  Add curated packages
  Search Microsoft.Extensions packages

ASP.NET Core
  Add curated packages
  Search Microsoft.AspNetCore packages
```

The pattern implementation demo uses two synthetic registrations:

```text
Example Data
  Search Example.Data packages

Example Compute
  Inspect integrations
```

Discovery shows only each declared action. Selecting the first pack returns
its prefix intent and does not return, construct, or invoke the neighboring
scanner binding.

## Required gates

The pattern's target Release suite is `EcosystemPackRegistryTests` plus an
ordinary non-friend consumer.

| Gate | Property |
| --- | --- |
| `EcosystemPackRegistryTests.SyntheticManifestIsDiscoverableInDeclaredOrder` | Static discovery returns a literal expected synthetic descriptor and action sequence in unique explicit order. |
| `EcosystemPackRegistryTests.ExactLookupUsesOnlyTypedIdentity` | Exact ID lookup returns the enumerated registration view; labels, prefix text, package-set IDs, case variants, and unknown IDs do not alias a pack. |
| `EcosystemPackRegistryTests.InvalidStaticRegistrationsFailBeforePublication` | Malformed or duplicate IDs, duplicate pack order, duplicate prefix-entry IDs/order, out-of-order prefix sequences, and empty registrations reject the complete static manifest before publishing any view rather than publishing a shortened view. |
| `EcosystemPackRegistryTests.CatalogMaterializationPerformsNoObservableWork` | Materializing and discovering a synthetic manifest perform no package-set resolution, package-source or workspace work, scanner invocation, or pack/scanner instance construction; initialization timing itself is not asserted. |
| `EcosystemPackRegistryTests.DiscoveryDoesNotResolveOrExecuteCapabilities` | Discovery performs no package-set membership resolution, package-source work, artifact/workspace work, or scanner invocation. |
| `EcosystemPackRegistryTests.ScannerSelectionReturnsOnlyTheSelectedBinding` | Selecting one synthetic pack returns only its scanner binding and leaves every neighboring binding unreturned and uninvoked. |
| `EcosystemPackRegistryTests.PrefixSelectionPreservesExactValidatedIntent` | After the prefix owner issues its currency, selecting a prefix returns that typed request unchanged and does not expand, combine, count, or execute it. |
| `EcosystemPackRegistryTests.PackageSetSelectionPreservesExactTypedIdentity` | Selecting a curated set returns only its `PackageSetId` and does not copy membership or activate another pack capability. |
| `EcosystemPackConsumerTests.PublicSurfaceSupportsStaticDiscoveryAndSelection` | An ordinary non-friend front-end consumer discovers and selects available actions through only the public surface, without registration construction, manifest publication, scanner implementation, CLI types, package clients, or workspaces. |
| `EcosystemPackAssemblyBoundaryTests.FriendsOnlyDedicatedTests` | `DotnetInspector.Ecosystems.Tests` is the assembly's only `InternalsVisibleTo`; the CLI, inspect-web facade, non-friend canary, and all other assemblies are absent. |
| `EcosystemPackAssemblyBoundaryTests.OwnerContractsRequireNoFriendAccess` | Repository-owned lower assemblies derived from the ecosystem assembly's compiled references omit `DotnetInspector.Ecosystems` from `InternalsVisibleTo`; compiling the ecosystem assembly therefore exercises only public owner contracts. |
| `eng/dependency-policy.json` rule `ecosystem-packs-stay-out-of-reusable-product-libraries` | Within `dotnet-inspect.slnx`, project and compiled assembly graphs reject every production dependency on `DotnetInspector.Ecosystems` except direct use by `dotnet-inspect`; existing IL rules independently reject the reusable IL-library edges they select. |
| `BrowserEngineLayeringTests.EcosystemCatalogIsFacadeOnly` | The inspect-web project graph permits `DotnetInspector.Ecosystems` only in the current managed front-end facade and rejects it from `InspectWeb.Engine.Core` and every other browser production project. |

Application adoption adds
`ProductEcosystemPackTests.ShippedManifestMatchesLiteralPolicy` and
`ProductEcosystemPackTests.EveryPackageSetReferenceResolves` with literal
descriptor and reference expectations. Integration adoption owns scanner
invocation and witness gates. Generic catalog tests use synthetic pack names
and do not establish built-in ecosystem policy.

The implementation must also retain existing NativeAOT and Browser/Wasm build
coverage. Static binding values root their target methods in published output.
The first real scanner adoption must record the Browser/Wasm publish-size delta
and decide whether it warrants a retained sensor; the pattern design does not
claim that cost is zero.

## Landing sequence

Overall delivery is tracked by #5728.

1. Lock this focused pack pattern.
2. Advance whichever independent owner track is needed for the first real pack.

| Independent track | Owner work |
| --- | --- |
| Curated package set | #5720 decides how packs reference source-authored membership, followed by Package Set Registry implementation. |
| Recorded prefix | #5602 issues the typed package-prefix request currency. |
| Semantic scanner | #5719 issues the opaque binding and decoded observation-context contract. |

3. Implement `DotnetInspector.Ecosystems` when any one owner-issued action
   currency supports a coherent real application row. The first slice includes
   identity, descriptors, the private manifest, discovery, exact lookup, the
   available optional action slot or slots, and their focused gates.
4. Add Microsoft.Extensions and ASP.NET Core rows without changing current
   package membership or search behavior.
5. Add each remaining action slot independently when its owner track lands; no
   later slot reopens already implemented action semantics.
6. Measure Aspire Core and propose Aspire through its package-set, Integration,
   and application-pack owner changes.
7. Adopt CLI and browser actions through their separately owned slices.

The three owner tracks are residual decisions rather than outcomes authorized
by this pattern. No implementation slice waits for every optional slot: each
lands only when its owner-issued currency exists and one real application
scenario makes that slice coherent.

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
- automatic execution of every capability exposed by a pack;
- recommendation, ranking, popularity, or compatibility policy;
- generic CLI syntax or browser interaction details;
- registration of ecosystem identities in the lower shared Vocabulary catalog;
  front ends project the application catalog directly, and any future generic
  query-value adoption requires a separately owned design; or
- a requirement that every ecosystem have any particular one of package set,
  prefix, or scanner; every shipped pack must still expose at least one of
  those capabilities.
