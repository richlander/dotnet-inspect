# Ecosystem Receiver Index

## Status

Proposed focused design for
[#9889](https://github.com/richlander/dotnet-inspect/issues/9889). Nothing in
this document is implemented. The adoption sequence below is the counted path
to observable behavior in the CLI and Inspect Web.

## Owner and exact claim

**Ecosystem Receiver Index** owns this exact claim:

> For one Ecosystem's declared package-prefix population, enumerated and
> pinned to exact package coordinates at generation, a checked-in
> deterministic artifact records the extension members that the live
> reverse extension census admits by default for the selected Libraries of
> each pinned package, keyed by Metadata receiver type identity, together with
> a complete coverage record. For every receiver identity, the artifact's
> members equal that census's default output with that receiver identity over
> the pinned coordinates. For any receiver query, a lookup returns the members
> the live reverse extension question returns by default over the same pinned
> coordinates, and a host answers it without acquiring packages.

This owner defines:

- the artifact's content, identity, schema version, and deterministic
  serialization;
- the coverage record, including what was enumerated, indexed, rejected,
  bounded, or contributed no extension members;
- the receiver key, and the lookup over it that consumes Metadata's
  receiver-matching rule;
- the equivalence property between the artifact and the live question, and
  its gates;
- how a consumer observes the pinned coordinates, so staleness is visible;
  and
- the generator contract: its inputs, its single product-owned builder, and
  its regeneration command.

It does not define:

- extension-member decoding, member anchors, receiver and return type
  identity, signature text, or the admission rule for accessibility, hidden,
  and obsolete members, which remain with Metadata
  (`metadata.extension-member` observations,
  `MetadataExtensionRelationEvidence`, and `ExtensionMethodScanner`);
- how a receiver query string matches a receiver type, which remains with
  Metadata's `TypeMatcher` applied to the live path's normalized receiver
  text;
- package-prefix enumeration, paging, or completion, which remain with
  `PackagePrefixDeclaration` and NuGetFetch's prefix search;
- Library selection within a package, which remains with the live path's
  package assembly selection in `AssemblySetResolver`
  (`TfmSelector.SelectHighestAssembliesFromPackage` plus its exclusion of
  `runtimes/` assemblies);
- Ecosystem membership, lineage, or prefix declarations, which remain with
  [Static Ecosystem Packs](ecosystem-packs.md);
- receiver applicability through the type hierarchy or generic constraints
  (see [Non-claims](#non-claims));
- `find --ecosystem` prefix following
  ([#8811](https://github.com/richlander/dotnet-inspect/issues/8811),
  [#9888](https://github.com/richlander/dotnet-inspect/issues/9888)); or
- host gestures, rendering, natural-language query, or Spotlight query
  classification.

## The problem, concretely

Aspire and Microsoft.Extensions are hub-and-fan-out Ecosystems. A small set of
receivers (`IDistributedApplicationBuilder`, `IHostApplicationBuilder`,
`IServiceCollection`, `ILoggingBuilder`, `IConfigurationBuilder`) is extended
by many packages under the Ecosystem's prefix. The Ecosystem's core packages
declare the receivers; the prefix population supplies the members users look
for.

The reverse question already exists as a live product path:

```console
dotnet-inspect extensions Aspire.Hosting.IDistributedApplicationBuilder \
  --package-prefix Aspire.
```

Every receiver question repeats the whole prefix acquisition. Cold
measurements with `dotnet-inspect 0.27.0+a38bbc6`, NativeAOT, linux-x64, each
with fresh cache directories, on 2026-10-10:

| Live question | Wall | Cache | Peak RSS | Rows |
| --- | --- | --- | --- | --- |
| `extensions …IDistributedApplicationBuilder --package-prefix Aspire.` | 158 s | 4.7 GB | 2.5 GB | 102 |
| `extensions …IServiceCollection --package-prefix Microsoft.Extensions.` | 22 s | 185 MB | 0.24 GB | 193 |
| `find '.Add*' --package-prefix Aspire.` | 147 s | 1.5 GB | 1.7 GB | 660 |
| `find '.Add*' --package-prefix Microsoft.Extensions.` | 34 s | 185 MB | 0.24 GB | 1,181 |

The answer is small relative to the work. The `Aspire.` prefix enumerated 138
packages; its `Add*` extension rows fell on 11 receiver types. The
`Microsoft.Extensions.` prefix enumerated 123 packages; its rows fell on 25
receiver types. Inspect Web cannot pay this cost per question under
Browser/Wasm.

`extensions` rows also omit return types, so a builder chain is not
navigable from the result: `AddPostgres` returns
`IResourceBuilder<PostgresServerResource>`, whose own extension members
(`AddDatabase`, `WithPgAdmin`, `WithDataVolume`) are the next step.

## Real assets and known-answer controls

Versions are those observed in the measurements above. Every artifact must
reproduce these controls before any empty lookup is
trusted. They are the generator's acceptance gate and the equivalence
oracle's fixed points.

| Ecosystem | Package | Receiver | Member | Return type |
| --- | --- | --- | --- | --- |
| Aspire | `Aspire.Hosting.Redis@13.6.1` | `Aspire.Hosting.IDistributedApplicationBuilder` | `AddRedis` | `IResourceBuilder<RedisResource>` |
| Aspire | `Aspire.Hosting.PostgreSQL@13.6.1` | `Aspire.Hosting.IDistributedApplicationBuilder` | `AddPostgres` | `IResourceBuilder<PostgresServerResource>` |
| Aspire | `Aspire.StackExchange.Redis@13.6.1` | `Microsoft.Extensions.Hosting.IHostApplicationBuilder` | `AddRedisClient` | `void` |
| Microsoft.Extensions | `Microsoft.Extensions.Http@10.0.12` | `Microsoft.Extensions.DependencyInjection.IServiceCollection` | `AddHttpClient` | `IServiceCollection` and `IHttpClientBuilder` overloads |

The `Aspire.StackExchange.Redis` row is the cross-layer case. Its receiver is
declared by Microsoft.Extensions.Hosting, below Aspire in the Ecosystem
lineage, so the index must key members by the receiver's identity, not by the
Ecosystem that declares the receiver.

## Pathological cases

Each case below must have a fixture or recorded probe before implementation.

- **Type-parameter receivers.** `Microsoft.Extensions.Azure` declares
  `AddBlobServiceClient<TBuilder>(this TBuilder builder, …)`. The receiver is
  a method type parameter whose meaning lies in its constraint. Metadata does
  not currently issue constraint facts. The index records such a receiver as
  a type-parameter receiver and never files it under a guessed concrete type.
  Looking up `IAzureClientFactoryBuilder` does not return it until constraint
  facts exist; the result names the unresolved type-parameter receivers it
  could not match rather than presenting a complete answer.
- **Generic receivers and fuzzy matching.** Receiver keys keep Metadata's
  exact identity: `IResourceBuilder<T>` and
  `IResourceBuilder<PostgresServerResource>` are distinct keys. A lookup does
  not use exact key equality. It applies Metadata's `TypeMatcher`, which drops
  type arguments, compares arity-free base names, accepts namespace suffixes,
  and ignores case. Observed on `dotnet-inspect 0.27.0+a38bbc6`:
  `extensions 'System.Collections.Generic.IEnumerable<System.Int32>'
  --platform System.Linq --count` and the same query for `IEnumerable<T>` both
  return 66, and `ienumerable` returns 68 because the non-generic interface
  also matches. A lookup for `IResourceBuilder<RedisResource>` therefore
  returns every `IResourceBuilder<…>` member, as the live question does, with
  no constraint filtering. That result is equivalent, not applicable; the
  difference is a declared [non-claim](#non-claims).
- **Admission is Metadata's, and only the default is indexed.** By default
  the live question admits public extension methods, including public methods
  on a non-public extension class, and drops never-browsable and obsolete
  members; a hidden declaring type removes all its members, and hidden
  accessors change property signature text. `--all` also admits non-public
  members: on `dotnet-inspect 0.27.0+a38bbc6`,
  `extensions 'IEnumerable<T>' --platform System.Linq --count` returns 68 by
  default and 77 with `--all`. Metadata issues no per-member admission facts,
  so the artifact stores the census's default output, members and signature
  text as admitted, rather than re-deriving admission. The `--all` mode is a
  [non-claim](#non-claims).
- **Name heuristics are not extension facts.** A row-shape guess (declaring
  type ending in `Extensions`, or first parameter type) admits
  `DistributedApplicationBuilder.AddResource<T>(T resource)`, which is an
  instance method. The index admits only members that Metadata reports as
  extension members.
- **Prefix is not ownership.** `Aspire.` matched community packages such as
  `Aspire.Util.TestLogger`; `Microsoft.Extensions.` matched
  `Microsoft.Extensions.Logging.Log4Net.Jakeuj`. The index retains each
  package's nuget.org verification and owners. It does not filter on them;
  consumers choose presentation.
- **Bounded or partial enumeration.** Prefix search can stop at its take,
  source pagination, or client pagination limit. A package can be rejected
  before caching, or contain no admissible Library. Each outcome is recorded
  in coverage. An artifact whose enumeration did not complete is still
  publishable only if the coverage record states the bound; lookups over it
  report the population as partial.

## Artifact contract

One artifact per Ecosystem prefix declaration. It contains:

- **Identity.** Schema version, Ecosystem identity, the exact prefix
  declaration, and the builder's algorithm version.
- **Population.** Every enumerated package with its exact version, the
  selected Libraries and target framework, nuget.org verification and owners,
  and its disposition: indexed, indexed with zero extension members, or
  rejected with the typed reason. A Library whose inspection fails is recorded
  in coverage with its reason, never as a Library with zero members.
- **Enumeration completion.** The prefix search's completion outcome and any
  bound it reached.
- **Members.** For each admitted extension member: canonical receiver type
  identity and receiver kind (named type, open generic, or type parameter),
  the live path's normalized receiver text, member anchor, member kind
  (method or property), signature text as admitted, declaring type, return
  type identity, and the package and Library coordinate that contributes
  it.

Invariants:

- **Pure function of pinned inputs.** The artifact is a function of the
  pinned package coordinates, their content, and the builder version. It
  contains no wall-clock time, machine path, cache location, or enumeration
  order artifact. Regenerating from the same pinned coordinates produces
  byte-identical output.
- **Ordinal canonical order.** Packages, members, and receivers serialize in
  a defined ordinal order independent of enumeration or parallel completion.
- **Missing is never empty.** A rejected or unenumerated package is never
  represented as a package with zero members.
- **Untrusted text stays contained.** Identifiers in the artifact originate
  from internet-supplied packages. Consumers project them through the
  containment owned by the
  [untrusted data threat model](untrusted-data-threat-model.md), exactly as
  they would project live Metadata names.

## Lookup semantics

A lookup takes one Ecosystem artifact and one receiver query, and returns:

- every member whose normalized receiver text Metadata's `TypeMatcher`
  matches against the normalized query, exactly as the live path matches, in
  canonical order;
- the type-parameter receivers in the artifact that the query did not match,
  reported as unresolved for this lookup;
- the artifact's pinned coordinates and coverage, so a consumer can state
  "as of" and "partial"; and
- an empty member set only as a complete-empty answer when the coverage
  record has no gap: enumeration completed, no package was rejected, and no
  selected Library failed inspection. Otherwise the empty set carries the
  gap.

Lookup performs no acquisition and no network access. Checking pinned
versions against current nuget.org versions is a separate, explicit,
network-bearing host gesture.

## Equivalence with the live question

Equivalence has two levels, each with its own oracle.

- **Content.** For every receiver identity, the artifact's members equal the
  typed per-assembly extension census the live path computes over the same
  pinned coordinates in default admission, compared by member anchor,
  receiver identity, return type identity, declaring type, and signature
  text. The live `extensions` output cannot serve here: it carries no anchor
  or return type and collapses overloads by display name.
- **Lookup.** For any receiver query, the lookup's members, collapsed by the
  live path's overload grouping, equal the default live
  `extensions <query> --package-prefix <prefix>` result over the same pinned
  coordinates. This level checks that the lookup applies the same matcher to
  the same receiver text.

Both levels require one shared composition. The live path's per-assembly
census is CLI-internal today. Step 2 moves it to a host-neutral layer that the
live command and the builder both consume, with the same Library selection. A
second decoder, matcher, or generator-side selection rule would make
equivalence a coincidence rather than a property.

## Generation and placement

A thin generator tool orchestrates enumeration and acquisition and invokes the
product-owned builder. Following the
[harness boundary](../evidence-and-validation.md#harness-boundary), the tool
does not construct, normalize, or repair index content. The precedent is
`tools/InspectWeb.PlatformIndexGenerator`, whose checked-in
`inspect-web/assets/platform-index.json` lets review see membership and
version changes together.

The artifact is checked in once and consumed by both hosts: the CLI through an
embedded resource and Inspect Web through a static asset built from the same
file. Regeneration is an explicit, network-bearing command that is never run
during an ordinary build or test.

Generation fails, rather than publishing, when any known-answer control for
that Ecosystem is missing.

## Evidence and gates

| Property | Gate | Lane |
| --- | --- | --- |
| Deterministic serialization | Build twice from fixture assemblies and compare bytes | PR-fast |
| Missing is never empty | Fixture population with a rejected package, a Library that fails inspection, and an empty Library; only the empty Library contributes to a complete-empty answer | PR-fast |
| Extension facts only | Fixture containing an instance `Add*` method and a classic and C# 14 extension member | PR-fast |
| Stored members are the census's default admission | Fixture with never-browsable, obsolete, non-public, and hidden-accessor extension members compared with the shared census | PR-fast |
| Lookup applies Metadata's matcher | Fixture lookups for open, closed, short-name, and differently cased receiver queries against keys built from the same fixture | PR-fast |
| Type-parameter receivers are unresolved, not misfiled | Fixture with a constrained `TBuilder` receiver | PR-fast |
| Checked-in artifact parses and contains every known-answer control | Load each embedded artifact | PR-fast |
| Content equals the live census over pinned coordinates | Recompute the shared census for each pinned coordinate and compare typed identities | Slow, network; daily |
| Lookup equals live `extensions` over pinned coordinates | Default lookups for each indexed receiver compared with live collapsed rows | Slow, network; daily |
| Work reduction in each host | Exact NativeAOT base/head comparison of the live and indexed paths, with result cardinality and content | Adoption PRs |

## Adoption sequence

Five steps from this design to observable behavior in both hosts, tracked by
[#9889](https://github.com/richlander/dotnet-inspect/issues/9889):

1. This design.
2. Host-neutral extension census shared by the live command and the
   builder; artifact schema, generator, checked-in Aspire and
   Microsoft.Extensions artifacts; and the CLI first adopter:
   `extensions <Type> --ecosystem <id>` answers from the artifact.
3. Inspect Web adoption: a Type's incoming Extensions and Spotlight read the
   same asset.
4. Chain navigation in both hosts: a member's return type becomes the next
   receiver lookup.
5. A scheduled regenerate-and-compare lane that reports drift between pinned
   and current coordinates.

There is no retirement step. The live `--package-prefix` path remains the
authority for unindexed populations and the oracle for the index.

## Non-claims

- **Hierarchy and constraint applicability.** Whether an extension on
  `IEnumerable<T>` applies to `List<T>`, or whether a constrained
  `IResourceBuilder<T>` member applies to `IResourceBuilder<RedisResource>`,
  needs type-hierarchy and generic-constraint facts. Lookups inherit the live
  question's matcher, which is broader than applicability; a lookup result is
  a candidate set, not an applicability judgment. Constraint facts belong to
  Metadata and are a prerequisite, not part of this claim.
- **`--all` admission.** The artifact does not answer the live `--all` mode,
  which admits non-public members. A host that receives that mode for an
  Ecosystem uses the live path or reports the mode as unsupported; it never
  answers from the artifact. Indexing it would need per-member admission facts
  from Metadata first.
- **Populations beyond declared prefixes.** Extenders outside every Ecosystem
  prefix are not indexed. Lookups state the indexed population; they do not
  imply global completeness.
- **Freshness.** The artifact is as current as its pinned coordinates. Step 5
  reports drift; it does not make lookups live.
- **Server-side computation.** No Azure Function, service, or runtime cache
  computes or serves the index.

## Alternatives not selected

- **Compute in the Inspect Web Function.** The
  [public-evidence bridge](inspect-web-public-evidence-bridge.md) is a
  closed-grammar, stateless transport and explicitly does not own provider
  caching. Server-side package inspection would add a stateful service,
  storage, cost, and an untrusted-content execution surface. A checked-in
  artifact gives deterministic review and offline use in both hosts.
- **Rely on the persistent package cache.** `PackageIndexCache` amortizes
  repeated work per exact package, but the first question in every
  environment, including every browser session, still pays full acquisition.
- **Probabilistic sketches.** Bloom filters or MinHash signatures trade
  exactness for size. At hundreds of packages and low thousands of members,
  the exact index is small enough to ship, and exactness is what makes the
  live-path equivalence checkable.
- **Index all of nuget.org.** No declared population, owner, or completion
  boundary exists for that scale.
