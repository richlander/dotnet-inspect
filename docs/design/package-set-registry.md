# Package Set Registry

## Status

Focused component design proposal for
[#5681](https://github.com/richlander/dotnet-inspect/issues/5681), supporting
the typed source-selection work tracked by
[#5602](https://github.com/richlander/dotnet-inspect/issues/5602).

The current product keeps two package arrays in CLI-owned `ScopeConstants`.
This design transfers ownership of named package-set identity, discovery, and
membership to a host-neutral registry. The registry and its target gates are
unimplemented; every asserted target property is unverified until the named
Release gates land.

Related designs:

- [Search scope resolution](search-scope-resolution.md) owns when search
  defaults apply and how explicit source selections compose. It consumes
  package-set membership but no longer owns it.
- The future typed search-scope domain tracked by #5602 will carry a package-set
  identity rather than one declaration variant for every current set.
- [Artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md)
  owns realization of package coordinates into artifacts and workspace
  generations.
- [Package source model](package-source-model.md) owns source authorization,
  provider routing, version discovery, failures, and payload acquisition.
- [Product vocabulary](vocabulary.md) may project this registry for general
  query-value discovery. Such a projection does not become another package-set
  authority.

## Authority and exact claim

The Package Set Registry is the product authority for discoverable,
product-owned named package sets.

It owns:

- one canonical typed identity for each issued package set;
- concise product-owned title, summary, and display order;
- one immutable ordered package-coordinate membership snapshot per set;
- the complete closed product registration set;
- deterministic static enumeration; and
- exact identity lookup.

It does not own:

- search defaults, selector composition, or declaration normalization;
- CLI flags, aliases, routing, help, or diagnostics;
- recommendations, automatic selection, or workspace actions;
- package-source authorization, version realization, network access, or
  acquisition;
- artifact admission, workspace generation, replacement, or lifetime;
- user-authored or remotely supplied package sets; or
- rendering, localization, or persistence formats.

The registry answers only: **which product-owned package sets exist, what are
they called, and which package coordinates do they currently contain?**

## Why this is a separate owner

`--extensions` and `--aspnetcore` currently look like CLI Boolean flags, but
their package membership is useful outside the CLI:

- a browser workspace can enumerate sets and offer **Add Extensions** without
  copying a TypeScript package list;
- a typed source declaration can select one stable set identity without
  embedding one enum case per current product set;
- a future vocabulary projection can disclose the same IDs, descriptions, and
  membership counts; and
- every host can consume one package order instead of maintaining parallel
  arrays.

Leaving membership in `ScopeConstants` makes discovery impossible without
depending on the CLI assembly. Moving default activation into the registry
would create the opposite problem: it would make a reusable data catalog own
one command family's policy. Identity and membership therefore move here;
selection and realization remain adjacent concerns.

This follows the repository's established static-registry convention:
product-owned descriptors have typed stable IDs and deterministic enumeration,
while hosts consume rather than restate them. The
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

`Order` is an explicit ascending integer unique in the registry. Enumeration
uses only this value. Registration order, identity text, title, and package
count are not tie-breakers.

`Members` is an immutable snapshot in product-defined order. Each member uses
the package owner's `PackageCoordinate` contract and has no framework or
runtime-identifier override. An omitted version floats only when a later
authorized realization owner processes the coordinate; the registry performs
no version discovery. Target framework, runtime, source, payload, and workspace
state remain operation or realization inputs rather than package-set data.

Holding a descriptor proves that its complete member sequence passed registry
construction. Callers cannot replace its identity, metadata, order, or member
collection.

Descriptor and registry construction are non-public. The product publishes
only descriptors produced by complete validated registry construction. Friend
tests may construct alternate complete tables to prove rejection and
immutability; an ordinary consumer cannot forge a descriptor or register a
replacement.

The registry is implemented at or below `DotnetInspector.Packages` because its
member currency is that owner's `PackageCoordinate`. This design does not move
package identity or coordinate grammar into another component.

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
releases. A consumer requiring a reproducible workspace retains the selected
descriptor snapshot or the exact coordinates produced by realization rather
than assuming a later registry build has identical membership.

## Membership validation

Registry construction validates the complete static table before publication:

- identities are canonical and unique;
- titles and summaries are present;
- display order is unique;
- every package coordinate passes the package owner's synchronous,
  network-free `PackageCoordinateResolver.Validate` operation;
- every package coordinate has a null framework and runtime identifier;
- one set contains no duplicate normalized package coordinate; and
- member order is retained exactly.

Coordinate normalization and identity belong to the package owner. The
registry reuses the same package-owner normalization as resolution rather than
restating its grammar. The duplicate key is lower-case package ID plus
normalized exact version when present; an absent version is a distinct value.
The registry does not derive package equality from display text.

Duplicates across different package sets are valid. A consumer selecting
several sets decides how their ordered memberships compose and deduplicate;
the registry does not perform cross-set normalization.

Invalid product registrations fail registry construction. There is no
success-shaped registry that silently drops an invalid set or member.

## Discovery and lookup

Static discovery returns every descriptor in ascending `Order`. It performs no
source configuration, filesystem access, network request, package resolution,
artifact acquisition, workspace operation, or query execution.

Exact lookup accepts one successfully constructed `PackageSetId` and returns
its descriptor or a typed unknown result. It does not consult aliases, package
prefixes, titles, summaries, CLI spellings, dynamic providers, or neighboring
identities. Unknown lookup does not select a default or replacement.

The registry is a closed static product catalog. There is no public
registration, removal, mutation, refresh, or plugin API. Enumeration and
lookup return immutable snapshots and are safe to reuse for the process
lifetime, including in a single-threaded Browser/Wasm host.

## Initial registry

The first registry issues two descriptors:

| Identity | Title | Summary | Order |
| --- | --- | --- | ---: |
| `package-set.microsoft-extensions` | Microsoft.Extensions | Selected foundational Microsoft.Extensions packages for common application infrastructure. | 100 |
| `package-set.aspnetcore` | ASP.NET Core | Selected foundational ASP.NET Core packages for common web application infrastructure. | 200 |

Their stable purposes bound later membership changes:

- `package-set.microsoft-extensions` selects first-party packages that define
  foundational dependency injection, logging, configuration, options, hosting,
  file-provider, HTTP, memory-caching, telemetry, and AI application
  infrastructure.
- `package-set.aspnetcore` selects first-party packages that define
  foundational authentication, authorization, Razor component, MVC Core, and
  SignalR web application capabilities.

The implementation table is the only package-membership inventory. This
document does not duplicate its package coordinates. Initial adoption moves the
existing `ScopeConstants.ExtensionsPackages` and
`ScopeConstants.AspNetCorePackages` values unchanged so discoverability does
not also change search behavior.

The purpose statements deliberately describe selected product sets rather than
claiming exhaustive publisher-prefix coverage. Adding every matching NuGet
package, deriving membership from live search, or sourcing membership from the
NuGet Catalog would be different product behavior.

## Consumer composition

### Typed source declaration

The source-selection declaration tracked by #5602 should use one
`PackageSet(PackageSetId)` variant. Its search normalizer asks this registry for
the exact descriptor and contributes the returned ordered coordinates under
its own explicitness and cross-source deduplication rules.

The declaration and normalizer own unknown-set handling at their boundary.
They do not copy registry membership or infer a set from an ID prefix.

### CLI

During staged adoption, `--extensions` maps exactly to
`package-set.microsoft-extensions` and `--aspnetcore` maps exactly to
`package-set.aspnetcore`. The CLI continues to own those spellings and their
diagnostics. A future generic `--package-set` option is a separate CLI design,
not implied by registry discoverability.

### Browser workspace

Inspect-web may enumerate descriptors to render an **Add package set** control.
Selecting **Microsoft.Extensions** contributes its typed identity to browser
source intent. The browser then uses existing package-source and workspace
owners to resolve coordinates and construct a new workspace generation.

The registry does not mutate the current workspace, decide whether prior
coordinates remain pinned, reserve budget, acquire packages, or publish the
replacement generation. Those decisions belong to the focused browser
adoption tracked under #5602.

### Vocabulary

Product vocabulary may expose package-set IDs, titles, summaries, order, and
membership counts as legal query values. It must project this registry rather
than duplicate it. Membership rows, package expansion, and workspace actions
are not required for the first registry slice.

## Demo

The implementation PR should demonstrate discovery through an ordinary
non-friend consumer:

```text
Available package sets

Microsoft.Extensions   16 packages
ASP.NET Core            5 packages

Selected: package-set.microsoft-extensions
```

The consumer then inspects the selected descriptor's ordered coordinates. It
does not acquire packages or construct a workspace.

A browser adoption mockup can later consume the same descriptors:

```text
Add package set

[Add] Microsoft.Extensions   16 packages
[Add] ASP.NET Core            5 packages
```

The neighboring case is exact lookup of `package-set.aspnetcore`; it must
return the same descriptor value and member order as enumeration rather than a
host-local copy.

## Required gates

The target Release suite is `PackageSetRegistryTests` plus one ordinary
non-friend consumer project.

| Gate | Property |
| --- | --- |
| `PackageSetRegistryTests.InitialCatalogIsDiscoverableInDeclaredOrder` | Enumeration returns the two initial descriptors in explicit order with their exact IDs, metadata, and literal expected 16- and 5-package sequences rather than expectations derived from the registry. |
| `PackageSetRegistryTests.ExactLookupReturnsEnumeratedDescriptor` | Exact lookup and enumeration expose the same immutable descriptor and member order. |
| `PackageSetRegistryTests.InvalidRegistrationsFailBeforePublication` | Malformed or duplicate IDs, duplicate order, invalid or target-specific coordinates, and within-set duplicate coordinates reject complete internal registry construction rather than publishing a shortened catalog. |
| `PackageSetRegistryTests.DescriptorAndMembershipAreImmutableSnapshots` | Caller collection mutation and returned-collection use cannot change registry metadata, membership, or order. |
| `PackageSetRegistryTests.InvalidTextDoesNotConstructAnIdentity` | Case variants, labels, CLI spellings, whitespace, and other non-canonical text fail the identity-construction boundary. |
| `PackageSetRegistryTests.UnknownIdentityDoesNotAliasOrSelectADefault` | A well-formed unregistered identity returns typed unknown and does not resolve by neighboring identity, label, or default. |
| `PackageSetRegistryConsumerTests.PublicSurfaceSupportsDiscoveryAndLookup` | A non-friend consumer references only the supported public surface to enumerate, select, and inspect a package set without CLI, source, acquisition, or workspace types. |

The implementation PR should also retain the current search-scope tests for
composition, deduplication, and ordering when membership moves out of
`ScopeConstants`. The literal registry manifest gate, not expectations read
back from the registry, proves the two inventories moved unchanged.

## Landing sequence

1. Lock this focused discoverability contract.
2. Implement the host-neutral registry, Release contract suite, non-friend
   consumer, and the bounded donor transfer from `ScopeConstants`.
3. Adapt current search-scope resolution as the first consumer without changing
   its selection behavior.
4. Have the typed source-selection domain consume `PackageSetId`.
5. Adopt browser **Add package set** and remaining CLI surfaces through their
   own focused issues.

Registry implementation and removal of the donor's two parallel inventories
land together so the repository has one membership authority. Search-scope
adaptation is the bounded consumer required to complete that one-donor
transfer. Browser, vocabulary, generic CLI syntax, and user-authored sets do
not join that PR.

## Non-claims

This design does not define:

- an exhaustive package prefix, live query, or NuGet Catalog view;
- package recommendation, ranking, popularity, or compatibility;
- source availability or whether every member can be realized;
- workspace atomicity, version pinning, or replacement behavior;
- dynamic registration, plugins, or user-defined package sets;
- package-set persistence or transport;
- a generic `--package-set` CLI option; or
- compatibility aliases for the removed `--curated` option.
