# Package Set Registry

## Status

Implemented focused component design for the initial registry in
[#5681](https://github.com/richlander/dotnet-inspect/issues/5681), supporting
the typed source-selection work tracked by
[#5602](https://github.com/richlander/dotnet-inspect/issues/5602).
[#5720](https://github.com/richlander/dotnet-inspect/issues/5720) resolves
application inventory composition with
[Static Ecosystem Packs](ecosystem-packs.md); end-to-end delivery is tracked by
[#5728](https://github.com/richlander/dotnet-inspect/issues/5728).

The product implements named package-set identity, discovery, and membership
in a host-neutral application registry. Reusable package contracts remain
below it, while the shipped inventory lives in
`DotnetInspector.Ecosystems` beside other source-authored ecosystem data. The
initial registry and Release gates landed in
[#5753](https://github.com/richlander/dotnet-inspect/pull/5753), covering
registry construction, exact audited membership, CLI adaptation, non-friend
use, friendship, and project layering.

This amendment defines the next package set, `package-set.aspire-core`, from
measured NuGet and API evidence. Its descriptor, membership, and named Release
gates remain unimplemented and therefore unverified.

Related designs:

- [Search scope resolution](search-scope-resolution.md) owns when search
  defaults apply and how explicit source selections compose. It consumes
  package-set membership but no longer owns it.
- The future typed search-scope domain tracked by #5602 will carry resolved
  package-coordinate selection without making lower source infrastructure
  reference the application registry.
- [Artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md)
  owns realization of package coordinates into artifacts and workspace
  generations.
- [Package source model](package-source-model.md) owns source authorization,
  provider routing, version discovery, failures, and payload acquisition.
- [Static Ecosystem Packs](ecosystem-packs.md) owns ecosystem registration and
  the front-end-only application assembly. An ecosystem pack stores only
  `PackageSetId`; the package-set manifest remains a separate registry even
  when both registrations are authored in one pack source unit.

## Authority and exact claim

The Package Set Registry is the application authority for discoverable,
product-owned named package sets.

It owns:

- one canonical typed identity for each issued package set;
- concise product-owned title, summary, and display order;
- one immutable ordered package-coordinate membership snapshot per set;
- the complete closed application registration set;
- deterministic static enumeration; and
- exact identity lookup.

It does not own:

- search defaults, selector composition, or declaration normalization;
- CLI flags, aliases, routing, help, or diagnostics;
- recommendations, automatic selection, or workspace actions;
- package-source authorization, version realization, network access, or
  acquisition;
- artifact admission, workspace generation, replacement, or lifetime;
- end-user-defined or remotely supplied package sets; or
- rendering, localization, or persistence formats.

The registry answers only: **which product-owned package sets exist, what are
they called, and which package coordinates do they currently contain?**

## Why this is a separate owner

`--extensions` and `--aspnetcore` currently look like CLI Boolean flags, but
their package membership is useful outside the CLI:

- a browser workspace can enumerate sets and offer **Add Extensions** without
  copying a TypeScript package list;
- each front end can select one stable set identity without embedding one enum
  case or package inventory per current set;
- both front ends can disclose the same IDs, descriptions, and membership
  counts from one application inventory; and
- every host can consume one package order instead of maintaining parallel
  arrays.

Leaving membership in `ScopeConstants` makes discovery impossible without
depending on the CLI assembly. Moving default activation into the registry
would create the opposite problem: it would make a static data catalog own
one command family's policy. Identity and membership therefore move here;
selection and realization remain adjacent concerns.

This follows the repository's established static-registry convention:
product-owned descriptors have typed stable IDs and deterministic enumeration,
while hosts consume rather than restate them. It also lets one ecosystem source
unit supply both a package-set registration and an ecosystem-pack registration
without embedding membership in the pack or adding ecosystem data to reusable
package infrastructure. The
[View Facet Registry](view-facet-registry.md) is the closest precedent.
Package sets deliberately need a smaller contract: they have no target-aware
availability evaluator, execution binding, tombstone arm, or query result.

## Descriptor contract

One immutable descriptor has this conceptual shape:

```text
PackageSetDescriptor
  Id       PackageSetId
  Title    string
  Summary  string
  Order    int
  Members  immutable ordered PackageCoordinate sequence
```

`Title` is concise visible text. `Summary` is one complete sentence suitable
for discovery or an accessible description. These values are product-authored
static data; inspected artifacts and remote sources cannot supply them.

`Order` is an explicit ascending integer unique in the registry. The private
manifest is authored in strictly ascending `Order`; complete construction
rejects declaration order that disagrees rather than sorting it. Enumeration
preserves that validated sequence. Identity text, title, and package count are
not tie-breakers.

`Members` is an immutable snapshot in product-defined order. Each member uses
the package owner's `PackageCoordinate` contract and has no version, framework,
or runtime-identifier override. Versionless membership floats only when a later
authorized realization owner processes the coordinate; the registry performs
no version discovery. Exact-version package sets require a future focused
extension backed by a lightweight package-owner normalized-coordinate currency.
Target framework, runtime, source, payload, and workspace state remain
operation or realization inputs rather than package-set data.

Holding a descriptor proves that its complete member sequence passed registry
construction. Callers cannot replace its identity, metadata, order, or member
collection.

Descriptor and registry construction are non-public. The product publishes
only descriptors produced by complete validated registry construction. Friend
tests in `DotnetInspector.Ecosystems.Tests` may construct alternate complete
tables to prove rejection and immutability; an ordinary consumer cannot forge a
descriptor or register a replacement.

Placement is deliberately split:

- `DotnetInspector.Packages` owns the existing `PackageCoordinate` currency and
  validation used by registration; and
- `DotnetInspector.Ecosystems` owns `PackageSetId`, `PackageSetDescriptor`,
  non-public registration construction, canonical shipped IDs, the private
  manifest, discovery, and exact lookup.

The dependency points downward from Ecosystems to Packages. Packages, source
resolution, Queries, Services, Vocabulary, and other reusable components do not
reference the application registry. This design does not move package identity
or coordinate grammar out of their package owner; package-set identity is an
application-catalog concept and does not flow below the front ends.

## Identity

A package-set identity is an ordinal, case-sensitive ASCII string:

```text
package-set.<name>
```

`<name>` is one or more lower-case ASCII alphanumeric words separated by a
single hyphen, begins with a letter, and ends with a letter or digit. The
complete identity is at most 80 characters. Examples are
`package-set.microsoft-extensions` and `package-set.aspnetcore`.

There is no trimming, case folding, Unicode normalization, label slugging, or
CLI-alias lookup. `PackageSetId.TryCreate` is the conceptual non-throwing
boundary from text to typed identity. Grammar-invalid text is rejected there
and never reaches registry lookup. Consumers treat a successfully constructed
value as opaque and use exact typed equality.

`PackageSetId`, `TryCreate`, and application-owned `PackageSetIds` live in
`DotnetInspector.Ecosystems`. `PackageSetIds` publishes the canonical typed
values for the shipped sets. Product adapters map known controls and CLI flags
through those values rather than reparsing registry-owned string literals.
`TryCreate` remains the boundary for a value arriving as text from a supported
front-end caller or transport.

Exact registry lookup accepts only a `PackageSetId`. A grammar-valid but
unregistered identity returns a typed unknown result. Titles, summaries, CLI
option names, and package prefixes are not identity and are never lookup
aliases.

An issued identity and the stable purpose recorded in this design are product
contracts. An identity is not renamed or reused for a different purpose.
Membership may change in a later product build when that change remains within
the stated purpose; order and membership changes are observable product
changes requiring focused evidence.

Package-set identity does not make membership immutable across product
releases. A consumer requiring reproducible membership retains the selected
descriptor snapshot rather than assuming a later registry build has identical
membership. A reproducible workspace retains the exact coordinates produced by
realization because a descriptor may contain floating package coordinates.

## Membership validation

Registry construction validates the complete static table before publication:

- identities are canonical and unique;
- titles and summaries are present;
- display order is unique and the manifest sequence is strictly ascending;
- every package coordinate passes the package owner's synchronous,
  network-free `PackageCoordinateResolver.Validate` operation;
- every package coordinate has a null version, framework, and runtime
  identifier;
- one set contains no duplicate normalized package coordinate; and
- member order is retained exactly.

Coordinate normalization and identity belong to the package owner. After
validation and the versionless-membership check, the duplicate key is the
lower-case package ID. The registry does not parse NuGet versions itself or
derive package equality from display text.

Duplicates across different package sets are valid. A consumer selecting
several sets decides how their ordered memberships compose and deduplicate;
the registry does not perform cross-set normalization.

Invalid product registrations fail registry construction. There is no
success-shaped registry that silently drops an invalid set or member.

Generic registry construction does not decide whether one package belongs in
a shipped set. That is product policy evaluated before a source change is
accepted. The Microsoft.Extensions and ASP.NET Core manifests use the
following authoring rule:

- the package ID begins with the set's exact reserved prefix;
- nuget.org reports a listed, stable major-10 release, verified ownership, and
  `Microsoft` among the package owners;
- the package is not deprecated and is not a .NET tool;
- its current package archive contains at least one managed `lib/` or `ref/`
  assembly; and
- none of those assembly simple names is already supplied by the current
  `Microsoft.NETCore.App` or `Microsoft.AspNetCore.App` shared frameworks.

Every qualifying package is included. Member order is ordinal by package ID.
This makes the sets complete additive snapshots for their stated prefixes,
rather than hand-picked examples or alternate delivery of platform APIs.
Updating either manifest requires rerunning the same audit against the then
current stable product line and recording the result with the source change.
The registry does not run this network and archive audit at runtime.

Aspire Core is intentionally role-bounded rather than prefix-exhaustive. Its
authoring audit evaluates every currently listed stable `Aspire.*` package and
includes every package that satisfies all of these conditions:

- the package is verified and names `aspire` among its owners;
- its stable major version matches the current stable `Aspire.Hosting` product
  line;
- its archive owns public managed consumer APIs under `lib/` or `ref/`; and
- its public surface satisfies one of two mechanical roles:
  - the **application-model foundation** role declares both
    `Aspire.Hosting.IDistributedApplicationBuilder` and
    `Aspire.Hosting.ApplicationModel.IResourceBuilder<T>`; or
  - the **first-party test host** role declares public types in the
    `Aspire.Hosting.Testing` namespace or a descendant namespace, directly
    depends on the foundation package, and exposes a public signature using a
    foundation-owned application-model type.

The rule excludes packages whose owned payload is an SDK, build task, analyzer,
tool, template, platform/runtime asset, or packaging-only facade. It also
excludes external-system integrations, client instrumentation adapters, and
separately purposed Aspire subsystems such as polyglot type-system support.
Merely accepting, returning, extending, or transitively delivering
foundation-owned types does not satisfy either role.

Every qualifying package is included in ordinal package-ID order. Updating the
Aspire Core manifest requires rerunning this role audit across the complete
then-current stable `Aspire.*` universe and recording both newly qualifying
packages and exclusion canaries. Prefix discovery remains a separate explicit
ecosystem action; the curated set does not claim broad Aspire coverage.

## Discovery and lookup

Static discovery returns every descriptor in ascending `Order`. It performs no
source configuration, filesystem access, network request, package resolution,
artifact acquisition, workspace operation, or query execution.

Exact lookup accepts one successfully constructed `PackageSetId` and returns
its descriptor or a typed unknown result. It does not consult aliases, package
prefixes, titles, summaries, CLI spellings, dynamic providers, or neighboring
identities. Unknown lookup does not select a default or replacement.

The registry is a closed static application catalog. There is no public
registration, removal, mutation, refresh, or plugin API. Enumeration and
lookup return immutable snapshots and are safe to reuse for the process
lifetime, including in a single-threaded Browser/Wasm host.

The production dependency boundary is inherited from
[Static Ecosystem Packs](ecosystem-packs.md#dependency-boundary): only the
`dotnet-inspect` front end and exactly one managed inspect-web composition
facade may reference `DotnetInspector.Ecosystems`. Lower source, package,
workspace, query, service, browser-Core, and Vocabulary components cannot
discover or look up the registry directly.

## Initial registry

The implemented registry currently issues two descriptors:

| Identity | Title | Summary | Order |
| --- | --- | --- | ---: |
| `package-set.microsoft-extensions` | Microsoft.Extensions | Current Microsoft.Extensions packages that add managed APIs beyond the shared frameworks. | 100 |
| `package-set.aspnetcore` | ASP.NET Core | Current ASP.NET Core packages that add managed APIs beyond the shared frameworks. | 200 |

Their stable purposes bound later membership changes:

- `package-set.microsoft-extensions` contains every current package under the
  `Microsoft.Extensions.` prefix that satisfies the additive package rule.
- `package-set.aspnetcore` contains every current package under the
  `Microsoft.AspNetCore.` prefix that satisfies the additive package rule.

The private application table is the only package-membership inventory. This
document does not duplicate its package coordinates. Initial adoption replaces
the historical 16- and 5-package `ScopeConstants` arrays with audited
44-package Microsoft.Extensions and 53-package ASP.NET Core snapshots in
`DotnetInspector.Ecosystems`. Each package ID becomes a versionless
`PackageCoordinate` with null framework and runtime identifier. The CLI
projects each selected member back to `PackageId` before current scope
resolution, so this slice does not widen `ScopeResolver` from its existing
`string[]` currency or absorb #5602.

The 2026-09-03 audit used the nuget.org Catalog from 2025-10-01 through catalog
commit `2026-09-04T00:48:30.3297457Z`, exact Search and manifest reads, current
package archives, and the installed 10.0.11 shared frameworks. It scanned
1,361 Catalog pages and found 185 listed current package IDs: 96
Microsoft.Extensions and 89 ASP.NET Core. Microsoft ownership and package-type
checks excluded `Microsoft.Extensions.Logging.Log4Net.AspNetCore`, an
unverified third-party package, and
`Microsoft.Extensions.AI.Evaluation.Console`, a .NET tool, before archive
inspection. The remaining 94 classified as 44 additive, 47
shared-framework-only, and 3 without qualifying assemblies. The ASP.NET Core
prefix contained one unverified third-party package,
`Microsoft.AspNetCore.Mvc.Formatters.Xml.Extensions`; the remaining 88
classified as 53 additive, 17 shared-framework-only, and 18 without qualifying
assemblies. Search-by-prefix alone was not a completeness oracle: it reached
the Gallery source-page boundary and missed three current Microsoft.Extensions
IDs, all of which were shared-framework-only. The audit also found one
deprecated package,
`Microsoft.Extensions.ApiDescription.Client`; it had no qualifying assemblies
and is excluded independently by both rules.

An ecosystem source unit may supply both:

```text
MicrosoftExtensionsPackageSet.Registration
MicrosoftExtensionsPack.Registration
```

These are separate values published by separate complete static manifests. The
package-set registration contains the coordinates. The ecosystem-pack
registration contains only `PackageSetIds.MicrosoftExtensions`. Neither table
enumerates, looks up, or constructs the other during runtime publication.
Source authors spell both values explicitly even when they share one source
unit. That separation is a reviewed application-authoring rule; independent
literal manifest expectations and the later pack-reference gate detect drift
without deriving either expected table from the other.

A package set may exist without an ecosystem pack. Removing or renaming either
registration is an application policy change; complete-manifest validation and
literal cross-reference gates prevent duplicate inventory or a shipped dangling
pack reference.

The Catalog, Search metadata, and archives are authoring evidence, not a
runtime provider. A reviewed source snapshot remains the sole shipped
membership authority.

## Aspire Core adoption

The next registry adoption issues one descriptor after the existing two:

| Identity | Title | Summary | Order |
| --- | --- | --- | ---: |
| `package-set.aspire-core` | Aspire Core | Current stable Aspire application-model and testing APIs. | 300 |

Its stable purpose is the role-bounded application-model surface defined in
[Membership validation](#membership-validation). The 2026-09-04 audit found
exactly two qualifying packages:

```text
Aspire.Hosting
Aspire.Hosting.Testing
```

The audit used NuGet Search queries `q=id:Aspire` and `q=Aspire.`, exact-prefix
filtering, Catalog history from 2023-11-01 through catalog commit
`2026-09-04T03:56:04.4095328Z`, exact registration metadata, current package
archives, and production dotnet-inspect API queries.

Both Search forms returned the same 138 listed stable exact `Aspire.*` IDs.
Catalog contained 9,354 Aspire events for 163 IDs published since Aspire's
first public release on 2023-11-14. The 26 Catalog-only IDs were all
preview-only. The one Search-only ID, `Aspire.Util.TestLogger`, was an
unverified third-party package published in 2021 before Microsoft Aspire and
therefore before the Catalog scan boundary. Any package satisfying the current
major-line rule necessarily has a post-boundary Catalog event, so the combined
Search and Catalog evidence closes the qualifying universe despite that older
unrelated ID.

The complete audit funnel was:

| Disposition | Excluded | Remaining |
| --- | ---: | ---: |
| Listed stable exact `Aspire.*` IDs | 0 | 138 |
| Exclude packages without the `aspire` owner | 4 | 134 |
| Exclude stable versions outside the current major-13 line | 20 | 114 |
| Exclude SDK, tool, RID-tool, and template package types | 10 | 104 |
| Exclude current-line `Dependency` packages without public `lib/` or `ref/` consumer APIs | 22 | 82 |
| Exclude extension, integration, adapter, runtime, and separate-subsystem APIs that satisfy neither mechanical role | 80 | 2 |

The two remaining packages satisfy the foundation and test-host roles and are
included.

The prefix includes integration libraries, client adapters, the CLI and RID
packages, dashboard and orchestration payloads, SDKs, installers, templates,
legacy compatibility lines, partner-owned packages, and unrelated
third-party use of the prefix. Package type alone is insufficient because
several dashboard and orchestration runtime packages report
`Dependency`.

Archive and API inspection separated the likely core candidates:

| Package | Owned public API | Disposition |
| --- | ---: | --- |
| `Aspire.Hosting` | 379 types | Include: satisfies the foundation role by declaring both markers. |
| `Aspire.Hosting.Testing` | 5 types | Include: satisfies the test-host namespace, dependency, and signature checks. |
| `Aspire.Hosting.AppHost` | 0 `lib/` types; 4 `tools/` types | Exclude at the managed-consumer-API check: its public types are MSBuild tasks and it only delivers the foundation transitively. |
| `Aspire.AppHost.Sdk` | No `lib/` or `ref/` assembly | Exclude at package-type and managed-API checks: SDK and tool payload. |
| `Aspire.TypeSystem` | 32 types | Exclude: fails both roles as a separate polyglot type-system and code-generation surface. |

`tools/PackagePrefixBenchmark.cs` makes prefix discovery evidence
reproducible:

```bash
dotnet run tools/PackagePrefixBenchmark.cs -- search Aspire. 500,1000 3
dotnet run tools/PackagePrefixBenchmark.cs -- profile Aspire. 500,1000 3
```

Three trials at each take returned all 138 packages with no truncation: median
Search time was 1.27-1.35 seconds with 11 requests, and median profile time was
3.86-4.04 seconds with 149 requests. A separate single cold production command,
`find IResourceBuilder --package-prefix Aspire. -t 1000`, took 120.02 seconds,
made 276 HTTP requests, and reported 32 selected packages without inspectable
assemblies. That command used the fixed 500-package prefix-expansion cap, which
covers all 138 candidates; `-t 1000` raised only the result-row limit. These
measurements are neither runtime budgets nor stable performance promises. The
package count, mixed roles, and assembly-free payloads demonstrate that broad
prefix search is a useful explicit discovery gesture but a poor default
workspace.

Six sampled integration packages preserved the existing Aspire scanner shape:
`Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.Redis`,
`Aspire.Hosting.Azure.Storage`, `Aspire.Hosting.AWS`,
`CommunityToolkit.Aspire.Hosting.Ollama`, and
`CommunityToolkit.Aspire.Hosting.MailPit` expose public `Add*` methods that
accept `Aspire.Hosting.IDistributedApplicationBuilder` and return
`Aspire.Hosting.ApplicationModel.IResourceBuilder<T>`. This is supporting
evidence for the Integration-owned scanner work in #5719, not a package-set
membership rule.

## Consumer composition

### Typed source declaration

The source-selection owner tracked by #5602 defines the typed handoff after a
front end resolves `PackageSetId` through this registry. Lower source
normalization cannot ask the registry for a descriptor because it must not
reference `DotnetInspector.Ecosystems`.

The front-end adapter contributes the selected descriptor's immutable ordered
coordinates through that owner-issued handoff. The source owner retains
explicitness, cross-source deduplication, and realization semantics. It does
not copy registry membership into another static table or infer a set from an
ID prefix.

### CLI

During staged adoption, `--extensions` maps exactly to
`PackageSetIds.MicrosoftExtensions` and `--aspnetcore` maps exactly to
`PackageSetIds.AspNetCore`. The corresponding serialized identities are
`package-set.microsoft-extensions` and `package-set.aspnetcore`. The CLI
front end performs exact registry lookup and contributes the resulting ordered
coordinates to current scope resolution. It continues to own option spellings
and diagnostics. A future generic `--package-set` option is a separate CLI
design, not implied by registry discoverability.

### Browser workspace

The one managed inspect-web composition facade may enumerate descriptors to
render an **Add package set** control. Selecting **Microsoft.Extensions**
resolves the descriptor in that facade and contributes its immutable ordered
coordinates through browser source intent. Browser Core receives only
owner-issued source/package values and never references the application
registry. Existing package-source and workspace owners then resolve coordinates
and construct a new workspace generation.

The registry does not mutate the current workspace, decide whether prior
coordinates remain pinned, reserve budget, acquire packages, or publish the
replacement generation. Those decisions belong to the focused browser
adoption tracked under #5602.

### Vocabulary

The lower shared Vocabulary catalog cannot reference
`DotnetInspector.Ecosystems`. Front ends project package-set discovery directly
from the application registry. Any future generic query-value representation
requires a host-contribution seam owned by the Vocabulary design and cannot
duplicate package-set identities or membership. Until such a seam exists,
package sets do not appear as a `vocabulary` section.

## Demo

The initial implementation demonstrates discovery through an ordinary
non-friend consumer:

```text
Available package sets

Microsoft.Extensions   44 packages
ASP.NET Core           53 packages

Selected: package-set.microsoft-extensions
```

The consumer then inspects the selected descriptor's ordered coordinates. It
does not acquire packages or construct a workspace.

A browser adoption mockup can later consume the same descriptors:

```text
Add package set

[Add] Microsoft.Extensions   44 packages
[Add] ASP.NET Core           53 packages
```

The neighboring case is exact lookup of `package-set.aspnetcore`; it must
return the same descriptor value and member order as enumeration rather than a
host-local copy.

The Aspire implementation extends that same demo without turning broad
discovery into package-set membership:

```text
Available package sets

Microsoft.Extensions   44 packages
ASP.NET Core           53 packages
Aspire Core             2 packages

Selected: package-set.aspire-core
  Aspire.Hosting
  Aspire.Hosting.Testing
```

The neighboring ecosystem-pack mockup keeps each capability explicit:

```text
Aspire
  Add Aspire Core
  Search official packages               Aspire.
  Search Community Toolkit packages      CommunityToolkit.Aspire.
  Analyze Aspire integrations
```

The Package Set Registry owns only the first action. Prefix and scanner
availability remain subject to their separate owners and may land in separate
changes.

## Required gates

The target Release suite is `PackageSetRegistryTests` in
`DotnetInspector.Ecosystems.Tests` plus the ordinary non-friend
`DotnetInspector.Ecosystems.Consumer.Tests` project.

| Gate | Property |
| --- | --- |
| `PackageSetRegistryTests.InitialCatalogIsDiscoverableInDeclaredOrder` | Enumeration returns every shipped descriptor in explicit order with exact IDs and metadata. The implemented baseline is two descriptors; Aspire adoption extends the literal expectation to the third descriptor at order 300. |
| `PackageSetRegistryTests.ExactLookupReturnsEnumeratedDescriptor` | Exact lookup and enumeration expose the same immutable descriptor and member order. |
| `PackageSetRegistryTests.InvalidRegistrationsFailBeforePublication` | Malformed or duplicate IDs, duplicate or out-of-order manifest order, invalid, versioned, or target-specific coordinates, and within-set duplicate package IDs reject complete internal registry construction rather than publishing a shortened catalog. |
| `PackageSetRegistryTests.DescriptorAndMembershipAreImmutableSnapshots` | Caller collection mutation and returned-collection use cannot change registry metadata, membership, or order. |
| `PackageSetRegistryTests.InvalidTextDoesNotConstructAnIdentity` | Case variants, labels, CLI spellings, whitespace, and other non-canonical text fail the identity-construction boundary. |
| `PackageSetRegistryTests.WellKnownIdsResolveToInitialDescriptors` | Product adapters can use every canonical typed shipped ID without parsing literals, and each resolves to the matching enumerated descriptor. Aspire adoption adds `PackageSetIds.AspireCore`. |
| `PackageSetRegistryTests.UnknownIdentityDoesNotAliasOrSelectADefault` | A well-formed unregistered identity returns typed unknown and does not resolve by neighboring identity, label, or default. |
| `PackageSetRegistryTests.InitialManifestMatchesAuditedSnapshot` | Literal expectations prove the application manifest contains the exact audited 44- and 53-package ID sequences in ordinal order, each lifted to a versionless target-neutral coordinate; exclusion canaries pin known deprecated, legacy-only, and shared-framework-only IDs. |
| `SearchScopeResolutionTests.PackageSetFlagsUseAuditedMembership` | `--extensions` and `--aspnetcore` project registry members back to the exact audited ordered package-ID strings while retaining composition and deduplication behavior after the transfer. |
| `PackageSetRegistryConsumerTests.PublicSurfaceSupportsDiscoveryAndLookup` | A non-friend consumer references only the supported public surface to enumerate, select, and inspect a package set without CLI, source, acquisition, or workspace types. |

The implementation PR should also retain the current search-scope tests for
composition, deduplication, and ordering when membership moves out of
`ScopeConstants`. The literal registry manifest gate, not expectations read
back from the registry, proves the audited membership shipped exactly. Deleting
the donor arrays and wiring exact registry lookup are reviewed source changes;
the CLI behavior gate proves the flags retain their ordering and composition
contract while intentionally replacing ad hoc membership. This design does
not claim a repository-wide source scan proving that no dead copy of the
literals exists.

The first slice creating `DotnetInspector.Ecosystems` also lands the
front-end-only dependency, inspect-web project-graph, test-only friendship, and
lower-owner no-friend gates named by
[Static Ecosystem Packs](ecosystem-packs.md#required-gates). Later
ecosystem-pack adoption adds
`ProductEcosystemPackTests.EveryPackageSetReferenceResolves` and
`ProductEcosystemPackTests.ShippedPackManifestCarriesOnlyPackageSetIdentity`;
those gates are deferred because the registry donor-transfer slice publishes no
pack rows.

Aspire implementation adds or extends these Release gates:

| Gate | Property |
| --- | --- |
| `PackageSetRegistryTests.AspireCoreManifestMatchesAuditedSnapshot` | Literal expectations prove exact ordinal membership of `Aspire.Hosting` and `Aspire.Hosting.Testing` as versionless target-neutral coordinates. |
| `PackageSetRegistryTests.AspireCoreExcludesNonCoreCanaries` | The manifest excludes literal boundary canaries: `Aspire.ClickHouse.Driver`, `Aspire.Hosting.Dapr`, `Aspire.Hosting.AppHost`, `Aspire.AppHost.Sdk`, `Aspire.Hosting.CodeGeneration.TypeScript`, `Aspire.Dashboard.Sdk.linux-x64`, `Aspire.TypeSystem`, `Aspire.Hosting.Docker`, `Aspire.Hosting.Yarp`, `Aspire.Hosting.Python`, `Aspire.Hosting.Azure`, `Aspire.Hosting.Orchestration.linux-x64`, `Aspire.Cli`, and `Aspire.ProjectTemplates`. |

These gates prove the reviewed source snapshot and registry behavior. They do
not make the network audit a runtime operation or prove prefix-query and
Integration-scanner contracts owned elsewhere.

## Landing sequence

1. The initial registry, audited Microsoft.Extensions and ASP.NET Core
   manifests, CLI donor transfer, and application-shell gates landed in #5753.
2. Lock the focused Aspire Core membership rule and audit evidence in this
   amendment.
3. Add `PackageSetIds.AspireCore`, the two-member private manifest, exact
   Release gates, and non-friend discovery coverage without adding a generic
   CLI option.
4. Reference that identity from the Aspire ecosystem pack only after the pack
   adoption satisfies its separately owned prefix and scanner dependencies.
5. Have #5602 define the typed resolved-coordinate handoff for generic CLI and
   browser **Add package set** adoption.

The Aspire package-set implementation changes only the application registry
and its contract tests. It does not add prefix execution, scanner binding,
workspace realization, browser presentation, Vocabulary entries, or
end-user-defined sets.

## Non-claims

This design does not define:

- a runtime exhaustive package prefix, live query, or NuGet Catalog view;
- broad `Aspire.*` membership in `package-set.aspire-core`;
- package recommendation, ranking, popularity, or compatibility;
- source availability or whether every member can be realized;
- exact-version package-set membership;
- workspace atomicity, version pinning, or replacement behavior;
- dynamic registration, plugins, or user-defined package sets;
- a reusable package-layer inventory or lookup service;
- package-set persistence or transport;
- a generic `--package-set` CLI option.
