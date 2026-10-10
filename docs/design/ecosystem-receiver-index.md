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
> deterministic artifact records the extension members that the reverse
> extension census admits by default for the selected Libraries of each
> pinned package, keyed by structured receiver type identity, together with
> the generic-constraint and type-hierarchy facts its lookups consume and a
> complete coverage record. A receiver query resolves to one type
> definition, one constructed type, or an explicit name pattern, and the
> lookup returns that query's members with an applicability label for each.
> The live `extensions` command and the lookup share one resolver and one
> matcher, so over the same pinned coordinates they return the same answer,
> and a host answers from the artifact without acquiring packages.

This owner defines:

- the [receiver query model](#receiver-query-model): how query text resolves
  to a definition, a constructed type, or a pattern, and how each receiver
  shape matches it;
- the artifact's content, identity, schema version, and deterministic
  serialization;
- the coverage record, including what was enumerated, indexed, rejected,
  bounded, unresolved, or contributed no extension members;
- the equivalence property between the artifact and the live question, and
  its gates;
- the [performance goals](#performance-goals) for lookup, artifact size, and
  generation;
- how a consumer observes the pinned coordinates, so staleness is visible;
  and
- the generator contract: its inputs, its single product-owned builder, and
  its regeneration command.

It does not define:

- extension-member decoding and the admission rule for accessibility,
  hidden, and obsolete members, which remain with Metadata
  (`metadata.extension-member` observations,
  `MetadataExtensionRelationEvidence`, and `ExtensionMethodScanner`);
- structured type and member identity, its equality, and its issuance and
  refusal, which remain with [API Qualified Anchor](api-qualified-anchor.md)
  (`ApiQualifiedTypeIdentity` and `ApiQualifiedAnchor`); byte-backed names
  ([#9907](https://github.com/richlander/dotnet-inspect/issues/9907)) refine
  that equality without changing this contract;
- generic-constraint and declared supertype decoding, which remain with
  Metadata's declaration evidence;
- package-prefix enumeration, paging, or completion, which remain with
  `PackagePrefixDeclaration` and NuGetFetch's prefix search;
- Library selection within a package, which remains with the live path's
  package assembly selection in `AssemblySetResolver`
  (`TfmSelector.SelectHighestAssembliesFromPackage` plus its exclusion of
  `runtimes/` assemblies);
- Ecosystem membership, lineage, or prefix declarations, which remain with
  [Static Ecosystem Packs](ecosystem-packs.md);
- type matching in `find` and other commands, which keep Metadata's
  `TypeMatcher`;
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

- **Arity conflation in the live matcher.** The live question matches
  receivers with Metadata's `TypeMatcher`, which drops type arguments,
  compares arity-free base names, accepts namespace suffixes, and ignores
  case. Observed on `dotnet-inspect 0.27.0+a38bbc6`:
  `extensions 'System.Collections.Generic.IEnumerable<System.Int32>'
  --platform System.Linq --count` and the same query for `IEnumerable<T>`
  both return 66, and `ienumerable` returns 68 because the non-generic
  `IEnumerable` also matches. `IEnumerable` and `IEnumerable<T>` are
  different types. The [receiver query model](#receiver-query-model) keeps
  them disjoint, and the live command adopts it.
- **Closed generic receivers.** A lookup for
  `IResourceBuilder<RedisResource>` under `TypeMatcher` returns every
  `IResourceBuilder<…>` member, including members closed over
  `PostgresServerResource`. In Aspire, nearly every fluent member is either
  open with a constraint (`IResourceBuilder<T> where T : IResourceWith…`) or
  closed over one resource, so an unlabeled union makes
  [chain navigation](#adoption-sequence) useless. Constructed queries
  therefore match by structure and constraint, and label each row.
- **Type-parameter receivers.** `Microsoft.Extensions.Azure` declares
  `AddBlobServiceClient<TBuilder>(this TBuilder builder, …)`. The receiver is
  a method type parameter whose meaning lies in its constraint. The index
  files such a member under each definition its constraints name, labeled as
  a constrained type parameter, and never under a guessed concrete type. An
  unconstrained type-parameter receiver applies to every query; it is
  reported once as a universal receiver, not repeated in every answer.
- **Unresolvable identities.** Qualified identity issuance refuses an
  unresolved TypeRef rather than guessing. A member whose receiver, return,
  or constraint identity cannot be issued is recorded in coverage as
  unresolved with its contained display signature and refusal reason. It is
  never dropped and never keyed by display text.
- **Admission is Metadata's, and only the default is indexed.** By default
  the live question admits public extension methods, including public methods
  on a non-public extension class, and drops never-browsable and obsolete
  members; a hidden declaring type removes all its members, and hidden
  accessors change property signature text. `--all` also admits non-public
  members: on `dotnet-inspect 0.27.0+a38bbc6`,
  `extensions 'IEnumerable<T>' --platform System.Linq --count` returns 68 by
  default and 77 with `--all`. Metadata issues no per-member admission facts,
  so the artifact stores the census's default output, members and signature
  text as admitted, rather than re-deriving admission. Other modes are
  [non-claims](#non-claims).
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

## Receiver query model

A receiver query is text. It names one of three things, from broad to
narrow. Each level is a different question, not a looser or tighter version
of one fuzzy match.

| Query | Names | Answers |
| --- | --- | --- |
| `IEnumerable*` | An explicit name pattern | Every definition whose name matches, each answered as a definition query and grouped by definition |
| `IEnumerable`, `IEnumerable<T>` | One type definition, by name and arity | Members whose receiver is that definition under any instantiation; `IEnumerable` and `IEnumerable<T>` are disjoint |
| `IEnumerable<int>` | One constructed type | Members that apply to that exact use, each labeled with how it applies |

Fuzziness is something a user asks for with `*`, never the default.

### Resolution

Resolution maps query text to identities in a **definition universe**: the
definitions of every indexed receiver, every constraint target, and every type
declared in the selected Libraries. The live command computes the same
universe from the same census over its scope, so resolution agrees wherever
the populations agree.

1. **Pattern.** Text containing `*` is a glob over definition display names,
   with the namespace optional. It resolves to the matching set of
   definitions.
2. **Definition with placeholders.** `D<>`, `D<,>`, `` D`2 ``, and `D<T>` or
   `D<TKey, TValue>` name the definition of that arity. An argument is a
   placeholder only when it is empty, or is `T` or `T` followed by an
   uppercase letter, and resolves to no definition in the universe.
   Placeholder names are display, not identity, so `IEnumerable<T>` and
   `IEnumerable<TSource>` are the same query.
3. **Constructed.** When every argument resolves to a type, the query is
   constructed. Arguments resolve recursively by these same rules, with C#
   keyword aliases (`int`, `string`) mapping to their primitives. An argument
   that resolves to nothing fails the query and names the argument, rather
   than silently becoming a placeholder. A mix of placeholders and concrete
   arguments is refused as unsupported and named.
4. **Bare name.** A name with no argument list resolves among definitions of
   that name, namespace optional, preferring an exact-case match:
   - if an arity-0 definition exists, the query is that definition, and the
     answer names the sibling arities (`IEnumerable<T>` also exists; use
     `IEnumerable*` for both);
   - if no arity-0 definition exists and exactly one arity does, the query is
     that definition, and the answer states the resolution (`List` resolved
     to `List<T>`); and
   - otherwise, including the same name in two namespaces, the query is
     ambiguous: it returns no members and lists the candidates. Candidates
     are never merged.

A query that resolves to nothing in the universe reports that, together with
the artifact's coverage, instead of a bare empty answer.

### Matching

Receivers are `ApiQualifiedTypeIdentity` values. A receiver's shape is one of:
named (`IServiceCollection`), closed generic
(`IResourceBuilder<PostgresServerResource>`), open generic over the member's
type parameters (`IResourceBuilder<T>`, `IEnumerable<KeyValuePair<TKey,
TValue>>`), or a bare type parameter (`TBuilder`).

A **definition query** for `D` returns members whose receiver is `D` or an
instantiation of `D`, plus bare type-parameter receivers whose constraints
name `D`. Each row is labeled with its receiver shape. A definition query
makes no applicability judgment; it answers what is declared for `D`.

A **constructed query** for `D<A…>` considers the same members and labels
each one:

| Label | Rule |
| --- | --- |
| `Exact` | The receiver equals the query, structurally |
| `Satisfied` | The receiver unifies with the query, binding member type parameters, and every bound parameter's constraints are satisfied |
| `Unknown` | The receiver unifies, but some constraint cannot be decided from the artifact's facts |
| `NotApplicable` | Unification fails, or a constraint is violated; excluded from rows and reported as a count |

Constraint checking uses the member's constraint facts and the artifact's
declared supertypes. A type-argument constraint `T : I` is satisfied when the
bound type is `I` or reaches `I` through declared base types and interfaces,
with type arguments substituted. `class` and `struct` constraints are decided
from the identity's value-type flag. A walk that leaves the artifact's
declared types before reaching `I`, a `new()`, `unmanaged`, or `notnull`
constraint, or a constraint that refers to a still-unbound type parameter,
yields `Unknown`. `Unknown` is never presented as applicable.

A bare type-parameter receiver `TBuilder` is checked the same way, binding
`TBuilder` to the whole query. A pattern query runs a definition query for
each resolved definition.

Receiver applicability through the receiver's own hierarchy, such as whether
an `IEnumerable<T>` extension applies to a `List<int>` query, is a
[non-claim](#non-claims).

### Live command adoption

The live `extensions` command adopts this model through the same host-neutral
resolver and matcher the lookup uses. Two answers change deliberately:

- `extensions IEnumerable` returns only the non-generic receiver's members and
  names `IEnumerable<T>` as a sibling. Today it returns 68 rows over both
  arities.
- `extensions 'IResourceBuilder<RedisResource>'` returns labeled applicable
  rows and a `NotApplicable` count, instead of every `IResourceBuilder<…>`
  member.

The CLI reference documents both changes in the step that ships them.

## Artifact contract

One artifact per Ecosystem prefix declaration. It contains:

- **Identity.** Schema version, Ecosystem identity, the exact prefix
  declaration, the builder's algorithm version, and the
  [API qualified anchor](api-qualified-anchor.md) format the identities use.
- **Population.** Every enumerated package with its exact version and
  prerelease status, the selected Libraries and target framework, nuget.org
  verification and owners, and its disposition: indexed, indexed with zero
  extension members, or rejected with the typed reason. A Library whose
  inspection fails is recorded in coverage with its reason, never as a
  Library with zero members. Pinning records the version the live prefix
  path selects for each package at generation; this owner does not define a
  separate version policy.
- **Enumeration completion.** The prefix search's completion outcome and any
  bound it reached.
- **Members.** For each admitted extension member: its `ApiQualifiedAnchor`;
  receiver identity and receiver shape; return type identity; the member's
  generic-parameter constraints as identities plus special constraints;
  member kind (method or property); signature text as admitted; declaring
  type; and the package and Library coordinate that contributes it.
- **Declared supertypes.** For each type declared in the selected Libraries
  that the members' receivers, constraints, or return types reach: its base
  type and directly implemented interfaces as identities.
- **Unresolved members.** Members whose identities could not be issued, with
  contained display signature and refusal reason.

Identities serialize as structured JSON trees of their typed fields, with
names in their contained form, alongside contained display text. Display text
is never identity. A change to the anchor's name storage or encoding
([#9907](https://github.com/richlander/dotnet-inspect/issues/9907)) changes
the artifact's schema version.

Invariants:

- **Pure function of pinned inputs.** The artifact is a function of the
  pinned package coordinates, their content, and the builder version. It
  contains no wall-clock time, machine path, cache location, or enumeration
  order artifact. Regenerating from the same pinned coordinates produces
  byte-identical output.
- **Ordinal canonical order.** Packages, members, receivers, and supertypes
  serialize in a defined ordinal order independent of enumeration or
  parallel completion.
- **Missing is never empty.** A rejected or unenumerated package, or an
  unresolved member, is never represented as absent.
- **Untrusted text stays contained.** Identifiers in the artifact originate
  from internet-supplied packages. Consumers project them through the
  containment owned by the
  [untrusted data threat model](untrusted-data-threat-model.md), exactly as
  they would project live Metadata names.

## Lookup semantics

A lookup takes one Ecosystem artifact and one receiver query, resolves the
query under the [receiver query model](#receiver-query-model), and returns:

- the resolution: the definition, constructed type, or pattern set the query
  became, any sibling arities, or the ambiguous candidates;
- the matching members with their labels, in canonical order, and the
  `NotApplicable` count for a constructed query;
- universal receivers, reported once;
- unresolved members whose display receiver name matches the query's simple
  name, reported as possible gaps;
- the artifact's pinned coordinates and coverage, so a consumer can state
  "as of" and "partial"; and
- an empty member set only as a complete-empty answer when the coverage
  record has no gap: enumeration completed, no package was rejected, no
  selected Library failed inspection, and no unresolved member could match.
  Otherwise the empty set carries the gap.

Lookup performs no acquisition and no network access. Checking pinned
versions against current nuget.org versions is a separate, explicit,
network-bearing host gesture.

## Equivalence with the live question

Equivalence has two levels, each with its own oracle.

- **Content.** For every receiver identity, the artifact's members equal the
  typed per-assembly extension census the live path computes over the same
  pinned coordinates in default admission, compared by qualified anchor,
  receiver identity, return type identity, constraints, declaring type, and
  signature text. The live `extensions` output cannot serve here: it does not
  carry those identities and collapses overloads by display name.
- **Lookup.** For any receiver query, the lookup's resolution, rows, labels,
  and counts, collapsed by the live path's overload grouping, equal the
  default live `extensions <query>` result scoped by an explicit
  `--package <id>@<version>` for every pinned coordinate. The oracle never uses
  `--package-prefix`, which would re-enumerate and select current versions.

Both levels require one shared composition. The live path's per-assembly
census is CLI-internal today. Step 2 moves it, the resolver, and the matcher
to a host-neutral layer that the live command and the builder both consume,
with the same Library selection. A second decoder, resolver, matcher, or
generator-side selection rule would make equivalence a coincidence rather
than a property.

## Performance goals

The live path's cold cost is the baseline in
[the problem](#the-problem-concretely). Goals are measured with NativeAOT on
linux-x64, cold, with fresh cache directories:

| Measure | Live baseline | Goal |
| --- | --- | --- |
| Lookup wall, `extensions Aspire.Hosting.IDistributedApplicationBuilder --ecosystem aspire` | 158 s | 200 ms or less |
| Lookup wall, `extensions …IServiceCollection --ecosystem microsoft-extensions` | 22 s | 200 ms or less |
| Network bytes and cache bytes written by a lookup | 4.7 GB (Aspire) | 0 |
| Lookup peak RSS | 2.5 GB (Aspire) | Within 32 MB of `dotnet-inspect --version` on the same binary (105 MB measured on `0.27.0+a38bbc6`) |
| Artifact size, Aspire and Microsoft.Extensions combined | — | 2 MB or less as checked in; 500 KB or less Brotli-compressed as served to Inspect Web |
| Generation | — | Reported per run; no goal beyond completing within the scheduled lane's timeout |

A step whose exact-head measurement misses a goal stops for operator
direction; it does not proceed on a promise of later tuning. If the artifact
misses its size goal, the next lever is a compact identity encoding in the
style of Fast Diff's `SymbolKey`
([#9898](https://github.com/richlander/dotnet-inspect/pull/9898)), adopted
through [#9907](https://github.com/richlander/dotnet-inspect/issues/9907),
not a lossy sketch.

Generation pays the live acquisition cost by design. That cost is reported,
not hidden, and the live `extensions` path's own acquisition cost is tracked
separately from this owner.

## Generation and placement

A thin generator tool orchestrates enumeration and acquisition and invokes the
product-owned builder. Following the
[harness boundary](../evidence-and-validation.md#harness-boundary), the tool
does not construct, normalize, or repair index content. Qualified identity
issuance needs an `IApiQualifiedTypeDefinitionResolver` for names declared
outside the issuing Library; the acquisition owner supplies it over each
pinned package's dependency closure. The precedent is
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
| Deterministic serialization | Build from fixture assemblies twice, with permuted package and Library input order, and compare bytes | PR-fast |
| Missing is never empty | Fixture population with a rejected package, a Library that fails inspection, an unresolved member, and an empty Library; only the empty Library contributes to a complete-empty answer | PR-fast |
| Extension facts only | Fixture containing an instance `Add*` method and a classic and C# 14 extension member | PR-fast |
| Stored members are the census's default admission | Fixture with never-browsable, obsolete, non-public, and hidden-accessor extension members compared with the shared census | PR-fast |
| Resolution levels | Fixture queries: pattern; arity-0 and arity-1 siblings; placeholder spellings; single-arity bare name; same name in two namespaces; constructed with an unresolvable argument; mixed placeholders | PR-fast |
| Matching labels | Fixture with exact, open-unconstrained, open-constrained satisfied, violated, and undecidable receivers, a closed receiver over another argument, and a constrained and an unconstrained bare type-parameter receiver | PR-fast |
| Live command and lookup agree | Offline fixture: live `extensions` over fixture packages and lookup over an artifact built from them return equal resolutions, rows, labels, and counts | PR-fast |
| Checked-in artifact parses and contains every known-answer control | Load each embedded artifact | PR-fast |
| Content equals the live census over pinned coordinates | Recompute the shared census for each pinned coordinate and compare typed identities | Slow, network; daily |
| Lookup equals live `extensions` over pinned coordinates | Default lookups for each indexed receiver compared with live results scoped by explicit `--package <id>@<version>` | Slow, network; daily |
| Performance goals | Exact NativeAOT base/head measurement of each [goal](#performance-goals), with result cardinality and content | Adoption PRs |

## Adoption sequence

Six steps from this design to observable behavior in both hosts, tracked by
[#9889](https://github.com/richlander/dotnet-inspect/issues/9889):

1. This design.
2. Receiver query model in the live command: host-neutral extension census,
   resolver, and matcher over qualified identities and constraint facts,
   adopted by `extensions`, with the two deliberate answer changes documented
   in the CLI reference.
3. Artifact: schema, product-owned builder, thin generator, checked-in Aspire
   and Microsoft.Extensions artifacts, and the CLI first adopter:
   `extensions <Type> --ecosystem <id>` answers from the artifact, with the
   performance goals measured.
4. Inspect Web adoption: a Type's incoming Extensions and Spotlight read the
   same asset.
5. Chain navigation in both hosts: a member's return type becomes the next
   constructed receiver query.
6. A scheduled regenerate-and-compare lane that reports drift between pinned
   and current coordinates.

There is no retirement step. The live `--package-prefix` path remains the
authority for unindexed populations and the oracle for the index.

## Non-claims

- **Receiver hierarchy applicability.** Whether an extension on
  `IEnumerable<T>` applies to a `List<int>` query needs the query type's
  supertypes, which may lie outside the artifact. Constructed queries match
  receivers of the queried definition only.
- **Constraints the facts cannot decide.** `new()`, `unmanaged`, `notnull`,
  and constraints whose supertype walk leaves the artifact yield `Unknown`.
  The artifact does not carry Platform type hierarchies.
- **Non-default modes.** The artifact answers only default admission over the
  live path's default Library selection. A host that receives `--all` or any
  other mode that changes admission or Library selection for an Ecosystem
  uses the live path or reports the mode as unsupported; it never answers
  from the artifact.
- **Other commands' matching.** `find` and other commands keep `TypeMatcher`
  until their owners adopt the receiver query model.
- **Populations beyond declared prefixes.** Extenders outside every Ecosystem
  prefix are not indexed. Lookups state the indexed population; they do not
  imply global completeness.
- **Freshness.** The artifact is as current as its pinned coordinates. Step 6
  reports drift; it does not make lookups live.
- **Server-side computation.** No Azure Function, service, or runtime cache
  computes or serves the index.

## Alternatives not selected

- **Reuse `TypeMatcher` for lookup.** Matching the live command's current
  behavior would make equivalence trivial, but it conflates arities and
  returns every instantiation of a generic receiver unlabeled. The index
  would copy the live command's imprecision into a new artifact. Fixing the
  matcher once and sharing it keeps equivalence and gains correctness.
- **Key by normalized receiver text.** Text loses the distinction between a
  type parameter and a type, and between ill-formed names that display alike.
  Qualified identities already carry that structure.
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
