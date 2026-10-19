# Ecosystem Find Index

## Status

Proposed focused design for
[#10090](https://github.com/richlander/dotnet-inspect/issues/10090). Nothing in
this document is implemented. The adoption sequence is the counted path to
observable behavior in the CLI and Inspect Web.

## Owner and exact claim

**Ecosystem Find Index** owns this exact claim:

> For one Ecosystem package-prefix declaration enumerated and pinned to exact
> package, Library, and assembly coordinates at generation, one checked-in
> deterministic artifact records the complete default-public Type and Member
> source facts consumed by Find semantic evaluation, together with complete
> population and inspection coverage. The same Find evaluator can therefore
> answer a normalized Type or Member question from those facts without
> acquiring a package, opening an assembly, or accessing the network, while
> retaining exact producer identity, source order, completion, and visible
> staleness.

This owner defines:

- the artifact identity, source-fact content, coverage, bounds, schema version,
  and deterministic serialization;
- validation and projection of the artifact into detached source facts that
  Find semantic evaluation can consume;
- equivalence between indexed facts and live facts over the same pinned
  coordinates;
- the performance and size goals for indexed prefix evaluation;
- the generation contract and known-answer controls; and
- how a consumer observes pinned coordinates and coverage.

It does not define:

- Type or Member Find grammar, matching, classification, limits, or reduction,
  which remain with
  [Find semantic evaluation](find-semantic-evaluation.md);
- bounded-before-prefix staging, continuation, membership reduction, candidate
  bounds, or completion, which remain with
  [Ecosystem Find Search](ecosystem-find-search.md);
- package-prefix enumeration and version selection;
- package, target-framework, Library, or assembly selection;
- Metadata decoding, accessibility, hidden-member, or obsolete-member policy;
- Ecosystem declarations, lineage, or prefix membership;
- receiver queries, applicability, constraints, or chain navigation, which
  remain with
  [Ecosystem Receiver Index](ecosystem-receiver-index.md); or
- host syntax, presentation, activation, refresh gestures, or network policy.

The owner is intentionally one artifact boundary. CLI adoption, Inspect Web
adoption, and any change to the live prefix candidate bound remain focused
consumer efforts.

## Problem and real assets

The live prefix path established by
[#9891](https://github.com/richlander/dotnet-inspect/pull/9891) is the
correctness authority and fallback, but acquisition is too expensive as the
ordinary experience for a shipped Ecosystem.

Exact-head NativeAOT measurements for the bounded live path on
`dotnet-inspect-perf-3` used fresh caches and returned two Aspire prefix rows:

| Measure | Observed live path |
| --- | ---: |
| Total wall | 12.83-43.68 s |
| Peak RSS | 448-450 MiB |
| Package cache | 443 MiB |
| Cached packages | 12 |
| Rows | 2 |

Those runs did not measure time to first row. They characterize total cold
completion only. Warm `.AddRedis` population TSV and JSONL completed in 2.47
and 2.54 seconds at 319 and 318 MiB median RSS. The prior unbounded design
scanned 501 package versions, used 5.9 GB of cache, and took about 20 seconds
warm. The live candidate bound removes uncontrolled fan-out; it does not make
cold acquisition an acceptable indexed-Ecosystem lookup.

The known-answer controls are:

| Ecosystem | Exact package | Find question | Required member |
| --- | --- | --- | --- |
| Aspire | `Aspire.Hosting.Redis@13.6.1` | `.AddRedis` | `AddRedis` |
| Aspire | `Aspire.Hosting.PostgreSQL@13.6.1` | `.AddPostgres` | `AddPostgres` |
| Aspire | `Aspire.StackExchange.Redis@13.6.1` | `.AddRedisClient` | `AddRedisClient` |
| Microsoft.Extensions | `Microsoft.Extensions.Http@10.0.12` | `.AddHttpClient` | `AddHttpClient` |

Generation fails rather than publishing an artifact that omits a control for a
population containing its exact package coordinate.

## Basis and owner map

<!-- markdownlint-disable MD013 -->

| Owner | Contract consumed |
| --- | --- |
| [Ecosystem Find Search](ecosystem-find-search.md) | Prefix continuation, ordered package blocks, Ecosystem membership, row and candidate bounds, scoped failures, and completion |
| [Find semantic evaluation](find-semantic-evaluation.md) | Normalized questions, Type and Member classification, exact source evaluation, population reduction, limits, and source coverage |
| [Find type-search service](find-search-service.md) | Existing Type matching meaning and compatibility oracle |
| [Find member-name search service](find-member-search-service.md) | Existing Member matching, declaration filtering, pattern multiplicity, and accepted-row meaning |
| [Static Ecosystem Packs](ecosystem-packs.md) | Ecosystem identity and ordered package-prefix declarations |
| [Incremental package-prefix candidates](package-prefix-candidate-stream.md) | Live enumeration, source order, paging, and completion used during generation and fallback |
| Package and Library selection owners | Exact package version, target framework, selected Library, selected asset, and typed rejection |
| Metadata | Structured Type and Member facts, admission, declaration order, anchors, and inspection failures |
| [Ecosystem Receiver Index](ecosystem-receiver-index.md) | Neighboring precedent for pinned population coverage, deterministic generation, containment, embedded/static-asset placement, and drift reporting; no schema or semantic ownership is shared |
| CLI and Inspect Web owners | Demand, network permission, fallback gesture, presentation, and operation lifetime |

<!-- markdownlint-enable MD013 -->

The conventional basis is a checked-in inverted-search corpus generated from
immutable versioned inputs, with one deterministic manifest and an explicit
live oracle. The deliberate repository-specific choice is to store source
facts rather than query-result rows. Find grammar and classification continue
to evolve in one semantic owner; a query-result cache would freeze a second
matcher into the artifact and answer only anticipated questions.

The Receiver Index is a precedent, not a shared artifact. It stores only
extension members and facts needed by receiver applicability. Treating it as a
complete Find corpus would turn omitted ordinary Types and members into false
complete-empty answers. A future implementation may extract a shared
population-manifest component if both artifact owners can consume it without
combining their semantic contracts.

## Artifact unit and identity

There is one artifact per exact Ecosystem package-prefix declaration. An
artifact identifies:

- its schema version and builder algorithm version;
- the Find source-fact schema version;
- canonical Ecosystem identity;
- the exact prefix declaration identity and spelling;
- the prefix enumeration policy and bound used at generation;
- the version-selection policy identity;
- the Library-selection policy identity; and
- the structured-identity format versions used by Type names, assembly
  identities, and Member anchors.

Changing any item that can change admitted sources or facts changes the
artifact identity or schema version. A consumer validates the whole identity
before exposing any row. It never partially reads a mismatched artifact.

The artifact contains no wall-clock time, machine path, cache path, credentials,
source URI, or transient request identity. Staleness is visible through exact
pinned coordinates and policy versions. Checking current versions is a
separate explicit network-bearing operation.

## Population manifest

The manifest records enumeration in semantic source order. Each package entry
retains:

- source ordinal;
- exact package ID and version, including prerelease identity;
- selected target framework and each selected Library and assembly asset;
- the package and Library selection evidence required to reconstruct
  `FindSourceIdentity`;
- one disposition: `Indexed`, `IndexedEmpty`, `Rejected`, `Failed`, or
  `NotEvaluated`;
- a typed reason for every disposition other than `Indexed`; and
- fact counts and inspection coverage for each selected assembly.

The manifest separately records prefix enumeration completion and every bound
reached. A package with no selected Library is not an empty indexed package. A
selected assembly with an inspection failure is not a source with zero facts.

Source ordinal is owner-issued input to the builder. Canonical serialization
may sort physical records, but lookup restores semantic source order from that
ordinal. Permuting asynchronous acquisition or inspection completion therefore
cannot change bytes or Find order; changing the enumerated candidate order is
a population change and can.

## Source facts

The artifact stores the smallest complete default-public fact set needed by
the existing evaluator. It does not store preclassified matches.

For every admitted Type declaration it records:

- structured `MetadataTypeDefinitionName`;
- declaration kind and default-public visibility evidence;
- exact declaration observation or equivalent owner-issued identity;
- declaration order; and
- exact package, Library, assembly, and source association.

For every admitted Member declaration it records:

- producer-issued `MemberAnchor`;
- structured declaring `MetadataTypeDefinitionName`;
- Metadata member name;
- declaration and member order;
- member kind, display signature, and return Type facts used by current Find
  results; and
- exact package, Library, assembly, and source association.

It additionally records:

- Type or Member facts whose required structured identity could not be issued,
  with contained display evidence and refusal reason;
- Metadata inspection failures and the fact range they may cover; and
- assembly-level completion for Type and Member fact production.

Names and display text originating in packages use the same construction-time
containment as live Metadata results under the
[untrusted-data threat model](untrusted-data-threat-model.md). Display text is
never identity. The artifact contains no IL bodies, assembly bytes, metadata
reader state, executable callback, stream, or lease.

The first schema contains default-public facts only. It cannot answer
`FindVisibility.All`. A future wider-admission schema is a separate design
change, not an instruction to guess from default facts.

## Determinism and validation

The serialized artifact is a pure function of:

- exact pinned package content;
- owner-issued enumeration and selection evidence;
- the builder and fact-schema versions; and
- the contained source facts and coverage.

Packages, Libraries, assemblies, Types, members, failures, and unresolved facts
serialize in defined ordinal order. Regeneration from identical inputs
produces byte-identical output regardless of acquisition or worker completion
order.

Loading validates schema, artifact identity, ordinals, uniqueness, exact
coordinate associations, bounds, contained text, and coverage closure before
projecting facts. Validation failure rejects the artifact as one typed
outcome. It never drops a malformed record and continues with an apparently
complete population.

## Indexed evaluation

An indexed prefix evaluation takes:

```text
EcosystemFindIndexedRequest
  validated artifact
  normalized FindQuestion
  requested source window
```

The request accepts only default-public Type or Member questions. The source
window is issued by Ecosystem Find Search and preserves its candidate bound and
remaining row window. This owner does not reinterpret those limits.

The index projects each admitted source into the portable source facts consumed
by Find semantic evaluation. The semantic owner performs all pattern matching,
classification, declaring-Type filtering, accepted-row handling, match limits,
source evaluation, and population reduction. Indexed evaluation introduces no
second `TypeMatcher`, Member matcher, or ordering rule.

The result retains:

- the semantic owner's ordered source evaluations or block;
- the artifact identity and exact pinned coordinates;
- indexed, empty, rejected, failed, unresolved, and unevaluated coverage;
- whether the requested source window exhausted the artifact; and
- a typed refusal when the question, visibility, schema, or declaration is not
  supported.

The result is detached and resource-free. A host lowers it through the existing
Ecosystem prefix block and presentation contracts.

## Continuation and live fallback

Bounded and core Ecosystem work still settles before prefix work. The Browser
may still insert admitted Workspace Find before deciding whether to resume.
Only an owner-issued prefix continuation permits indexed or live prefix
evaluation.

For a validated artifact, an indexed adopter reads source facts in recorded
source order and publishes package-scoped blocks through that continuation.
For an unsupported request, the index returns a typed refusal before rows.

This owner does not silently choose the live path. A host may use live prefix
evaluation only when its existing gesture and source policy authorize network
work. In particular:

- custom package sources and explicit refresh are live operations;
- `FindVisibility.All` is unsupported by the first artifact and requires an
  explicit live-capable gesture or a visible refusal;
- an absent, mismatched, malformed, or incomplete artifact is visible and
  never becomes complete-empty; and
- an ordinary indexed lookup never initiates version discovery, acquisition,
  or cache writes as fallback.

The live path over the artifact's exact package coordinates is the equivalence
oracle. A current package-prefix search is not: its candidate set and selected
versions may have changed since generation.

## Empty, partial, and stale answers

A zero-row result is complete-empty only when:

- prefix enumeration exhausted under the recorded generation policy;
- every package in the requested source window is `Indexed` or
  `IndexedEmpty`;
- every selected assembly completed the requested Type or Member fact census;
- no unresolved fact could match the question; and
- Find semantic evaluation exhausted the question without a row or source
  limit.

Otherwise zero rows are partial or refused with the exact gap. A nonempty result
also retains population and source gaps; rows do not prove completeness.

Pinned coordinates are not a freshness claim. Hosts present them as the
indexed population identity. The scheduled lane reports drift; it does not
mutate an installed artifact or make ordinary lookup network-bearing.

## Equivalence

Equivalence has two independently gated levels.

**Fact equivalence** compares every indexed source with live Metadata fact
production over the same exact package, target framework, Library, and assembly
coordinates. Structured Type names, anchors, member facts, order, failures, and
coverage must match.

**Answer equivalence** runs the same normalized Find question through indexed
facts and live facts, then compares the semantic owner's typed source
evaluations and reduced block. The oracle covers Type and Member direct
questions, globs, namespaces, declaring-Type filters, multiple patterns,
finite match and accepted-row windows, Count evidence, no-match, and failures.

CLI text is not the oracle. Presentation can omit semantic facts and can change
without changing indexed correctness.

## Performance and size goals

Adoption measures exact NativeAOT production binaries on linux-x64. The
bounded/core phase is primed and held identical so the indexed prefix phase is
not confused with unrelated bounded acquisition. Reports also include one
whole-command scenario.

<!-- markdownlint-disable MD013 -->

| Measure | Goal |
| --- | --- |
| Prefix-continuation to first indexed row for Aspire `.AddRedis` | 200 ms or less |
| Prefix-continuation to complete indexed Aspire `.Add*` TSV/JSONL result | 1 s or less, excluding terminal output backpressure |
| Network bytes and package-cache bytes written by indexed prefix evaluation | 0 |
| Peak RSS above `dotnet-inspect --version` on the same binary | 64 MiB or less |
| Aspire plus Microsoft.Extensions artifacts | 16 MiB or less checked in; 4 MiB or less Brotli-compressed |
| Whole-command time to first and final row | Reported separately; no claim until bounded/core inputs are also prebuilt |
| Generation | Reported; no goal beyond the scheduled lane timeout |

<!-- markdownlint-enable MD013 -->

Result cardinality and a stable typed-result digest accompany every timing.
Time to first row and total completion are separate measures. A step that
misses a goal stops for operator direction; it does not proceed on a promise of
later compression or tuning.

The size limit is a feasibility ceiling, not a target. If it is missed, the
first permitted lever is a lossless compact identity and string-table encoding.
Dropping facts, coverage, coordinates, or failures and probabilistic sketches
are not permitted.

## Generation and placement

One product-owned builder accepts owner-issued pinned population and source
facts. A thin generator orchestrates explicit network-bearing enumeration,
acquisition, and Metadata inspection, but does not construct, normalize,
repair, filter, or reorder artifact content.

The artifact is checked in once. The CLI consumes it as an embedded resource;
Inspect Web consumes a static asset built from the same file. Ordinary build,
test, startup, and lookup do not regenerate it.

Generation fails when:

- any known-answer control is missing;
- population or inspection coverage is structurally inconsistent;
- a selected source lacks an exact coordinate;
- deterministic regeneration differs; or
- the artifact exceeds the adopted size goal.

## Evidence and gates

<!-- markdownlint-disable MD013 -->

| Property | Gate | Lane |
| --- | --- | --- |
| Deterministic serialization | Build from fixture facts twice with fixed source ordinals and permuted package, Library, and worker completion order; compare bytes | PR-fast |
| Identity and coverage closure | Fixtures with duplicate coordinates, bad ordinals, mismatched policy versions, rejected packages, empty Libraries, inspection failures, unresolved facts, and bounded enumeration | PR-fast |
| Missing is never empty | Each gap fixture runs matching and nonmatching questions; only complete indexed facts may produce complete-empty | PR-fast |
| Fact equivalence | Fixture assemblies produce live and indexed Type and Member facts compared by typed identity, order, failures, and coverage | PR-fast |
| Answer equivalence | The same normalized Type and Member questions over fixture live/indexed facts produce equal semantic evaluations and blocks | PR-fast |
| No second matcher | Non-vacuity tests remove indexed-fact delivery to the semantic evaluator and fail; no artifact-side match classification exists | PR-fast |
| Known answers | Load each checked-in artifact and reproduce `AddRedis`, `AddPostgres`, `AddRedisClient`, and `AddHttpClient` with exact coordinates | PR-fast |
| Pinned corpus fact equivalence | Regenerate live facts for every checked-in exact coordinate and compare | Slow, network; daily |
| Pinned corpus answer equivalence | Representative Type and Member question census over live and indexed facts | Slow, network; daily |
| Offline lookup | Network guard and fresh package cache prove no requests and no cache writes | Adoption PRs |
| Performance and size goals | Exact NativeAOT measurements of every goal with cardinality and typed-result digest | Adoption PRs |
| Both hosts use one artifact | CLI embedded-resource and Inspect Web static-asset gates compare artifact digest and host-neutral result | Host adoption PRs |

<!-- markdownlint-enable MD013 -->

## Adoption sequence

Five steps lead from design to observable behavior in both hosts:

1. **Design.** This focused owner and its artifact, equivalence, coverage,
   fallback, and performance contracts.
2. **Builder and semantic input.** Product-owned builder, deterministic fixture
   artifact, and one Find-semantic-evaluation input over portable indexed
   source facts.
3. **Artifacts and CLI.** Checked-in Aspire and Microsoft.Extensions artifacts;
   CLI indexed prefix continuation; exact NativeAOT and offline evidence.
4. **Inspect Web and Spotlight.** The same artifact and host-neutral indexed
   evaluation under Browser operation authority.
5. **Freshness lane.** Scheduled regenerate-and-compare reporting coordinate,
   coverage, fact, answer, size, and performance drift.

The implementation tracker must split step 2's semantic-owner adoption from
this artifact owner if they cannot lock through the one-first-adopter exception
in [Design scope and composition](../design-scope.md#stage-implementation-after-locking-the-design).

The live path is not retired. It remains required for unindexed declarations,
custom sources, unsupported modes, explicit refresh, equivalence, and
generation. Removing or changing the live candidate bound is a separate
Ecosystem Find Search effort; this design preserves it.

## Non-claims

- Bounded/core Ecosystem populations are not indexed here. Whole-command time
  can therefore include their realization.
- The first artifact does not answer include-all visibility.
- The artifact does not cover packages outside the exact declared prefix
  population recorded at generation.
- No automatic background refresh, provider cache, Azure Function, or server
  computation exists.
- No fuzzy, natural-language, receiver-applicability, or chain-navigation
  semantics are added.
- Indexed lookup does not weaken or bypass semantic row, accepted-row, source,
  or candidate limits.
- Artifact presence does not claim current nuget.org freshness.

## Alternatives not selected

- **Cache rendered query results.** This anticipates spellings, duplicates
  matching and row selection, cannot answer new grammar, and loses reusable
  source identity.
- **Reuse the Receiver Index as complete Find data.** It intentionally omits
  ordinary Types and members and would make omission indistinguishable from
  absence.
- **Ship package archives or metadata images.** Hosts would still pay parsing,
  admission, and retained-image cost and Browser would need assembly
  inspection resources.
- **Use the persistent package cache.** Every fresh machine and Browser session
  still pays acquisition and inspection.
- **Silently fall back to live acquisition.** Ordinary indexed lookup would
  become unexpectedly network-bearing and failures could look like success.
- **Index all of nuget.org.** No declared population, completion boundary, or
  product ownership exists for that scale.
