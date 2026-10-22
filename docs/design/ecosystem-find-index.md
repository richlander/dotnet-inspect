# Ecosystem Find Index

## Status

Proposed focused design for
[#10090](https://github.com/richlander/dotnet-inspect/issues/10090). Nothing in
this document is implemented. The adoption sequence is the counted path to
observable behavior in the CLI and Inspect Web.

## Owner and exact claim

**Ecosystem Find Index** owns this exact claim:

> For one indexed Ecosystem, one checked-in deterministic artifact records the
> actionable Type and Member symbols admitted by Integration scanning over that
> Ecosystem's exact ordered package-prefix population, pinned to package,
> Library, assembly, and symbol coordinates at generation. A consumer admits
> that artifact only when it admits the Ecosystem. Existing Find semantic
> evaluation can search the artifact's structured rows as an early prefix layer
> without acquiring packages, while the live prefix path remains responsible
> for every other Find match.

This owner defines:

- the artifact identity, indexed-symbol content, generation coverage, schema
  version, and deterministic serialization;
- validation and projection of indexed Integration symbols into detached Type
  and Member facts that Find semantic evaluation can consume;
- equivalence between each indexed symbol and the Integration scanner
  observation over the same pinned coordinate;
- the performance and size goals for the indexed layer;
- the generation contract and known-answer controls; and
- how a consumer observes pinned coordinates and coverage.

It does not define:

- which symbols are actionable Integration currency, which remains with
  [Integrations](integrations.md);
- Type or Member Find grammar, matching, classification, row limits, or
  reduction, which remain with
  [Find semantic evaluation](find-semantic-evaluation.md);
- index-before-bounded-before-live staging, candidate limits,
  cross-layer duplicate suppression, membership reduction, or completion,
  which remain with
  [Ecosystem Find Search](ecosystem-find-search.md);
- package-prefix enumeration and version selection;
- package, target-framework, Library, or assembly selection;
- Metadata decoding and symbol identity issuance;
- Ecosystem declarations, lineage, admission, prefix membership, or scanner
  configuration;
- host syntax, presentation, activation, refresh gestures, or network policy;
  or
- receiver queries, applicability, constraints, or chain navigation, which
  remain with
  [Ecosystem Receiver Index](ecosystem-receiver-index.md).

The owner is one selective artifact boundary. It does not claim a complete
Find population and cannot prove that a nonmatching symbol is absent from the
Ecosystem prefix.

## User-visible operation

The motivating command remains:

```console
dotnet-inspect find '.AddRedis' --ecosystem aspire --jsonl
```

Its serial work is:

1. settle the existing bounded/core Ecosystem layers;
2. when prefix demand and row capacity remain, admit the artifact for each
   admitted Ecosystem, search its Integration symbol rows, and publish matches;
3. stop if the semantic row window is full or cancellation occurs;
4. otherwise, when the host authorized live prefix work, continue through the
   existing package-prefix network phase; and
5. suppress a live row only when its exact source-backed Type or Member
   identity was already published from the index.

With no semantic row limit, exhausting the index does **not** complete the
prefix question. Authorized streaming Find continues to the live network phase.
An index miss likewise continues immediately. When the host did not authorize
live prefix work, completion remains explicitly partial and names the unsearched
live prefix; the index never turns selective coverage into complete-empty.

A package represented in the index is still admitted and inspected by the live
phase. The Integration scanner deliberately selects useful currency rather
than every Find symbol, so skipping that package could hide unrelated matching
Types or members. Exact duplicate rows are suppressed after live evaluation;
package acquisition, candidate accounting, failures, and coverage remain
visible.

## Problem and real assets

The live prefix path established by
[#9891](https://github.com/richlander/dotnet-inspect/pull/9891) is the
correctness authority and fallback, but its first useful Integration results
arrive behind package discovery and acquisition.

Exact-head NativeAOT cold measurements for its bounded live path completed with
two Aspire prefix rows:

| Measure | Observed live path |
| --- | ---: |
| Total wall | 12.83-43.68 s |
| Peak RSS | 448-450 MiB |
| Package cache | 443 MiB |
| Cached packages | 12 |
| Rows | 2 |

Those runs did not measure time to first row. They show only that completing a
cold live search is too expensive for the earliest high-value results. Warm
`.AddRedis` population TSV and JSONL completed in 2.47 and 2.54 seconds at 319
and 318 MiB median RSS.

The Integration owner already identifies the useful symbols the index should
accelerate: public package-owned actionable Types and curated starter APIs,
with exact structured Type identities or Member anchors. The known-answer
controls are:

| Ecosystem | Exact package | Integration symbol |
| --- | --- | --- |
| Aspire | `Aspire.Hosting.Redis@13.6.3` | `AddRedis` |
| Aspire | `Aspire.Hosting.PostgreSQL@13.6.3` | `AddPostgres` |
| Aspire | `Aspire.StackExchange.Redis@13.6.3` | `AddRedisClient` |
| Microsoft.Extensions | `Microsoft.Extensions.Http@10.0.13` | `AddHttpClient` |

Generation fails rather than publishing an artifact that omits a control when
its exact package coordinate and scanner concept are in generation scope.

## Basis and owner map

<!-- markdownlint-disable MD013 -->

| Owner | Contract consumed |
| --- | --- |
| [Integrations](integrations.md) | Configured concepts, scanner observations, actionable Type and starter-API admission, exact structured candidate identity, evidence, and row ordering |
| [Static Ecosystem Packs](ecosystem-packs.md) | Ecosystem identity, admission, ordered package-prefix declarations, and the optional Integration scanner binding |
| [Ecosystem Find Search](ecosystem-find-search.md) | Index-before-bounded-before-live staging and continuation, row and live-candidate bounds, membership, duplicate suppression, scoped failures, and completion |
| [Find semantic evaluation](find-semantic-evaluation.md) | Normalized Type and Member questions, matching, classification, exact source association, row limits, and source evaluation |
| [Incremental package-prefix candidates](package-prefix-candidate-stream.md) | Live enumeration, source order, paging, cancellation, and completion used by generation and the later live phase |
| Package and Library selection owners | Exact package version, target framework, selected Library, selected asset, and typed rejection |
| Metadata | Structured Type and Member identity, declaration order, scanner observations, and inspection failures |
| [Ecosystem Receiver Index](ecosystem-receiver-index.md) | Neighboring precedent for pinned population evidence, deterministic generation, containment, embedded/static-asset placement, and drift reporting; no schema or semantic ownership is shared |
| CLI and Inspect Web owners | Prefix demand, network permission, output, activation, and operation lifetime |

<!-- markdownlint-enable MD013 -->

The conventional basis is a small checked-in search accelerator generated from
immutable versioned inputs, followed by the authoritative complete search when
needed. The artifact stores exact scanner-admitted symbols rather than cached
query results: Find grammar and matching remain reusable, and one symbol can
answer many questions.

The Receiver Index is a precedent, not a Find input. It stores extension
members organized by receiver applicability; this index stores only symbols
that Integration scanning over the selected Ecosystem population already
admitted as useful currency.

## Artifact unit and identity

There is one artifact per indexed Ecosystem. It is one serialized table, not
one artifact per prefix, Integration concept, symbol, or Find hit. The table
contains the rows generated from that Ecosystem's exact ordered package-prefix
population. Each row retains the exact prefix source that produced it.

An artifact becomes an eligible Find source only when the CLI or another
consumer admits its canonical Ecosystem registration. Admission does not infer
an artifact from a prefix or scanner identity and does not admit an artifact
belonging to another Ecosystem. For an admitted artifact, indexed evaluation
uses only rows belonging to prefix sources demanded by that Find operation.

The artifact identity includes:

- schema and builder algorithm versions;
- indexed-symbol projection version;
- canonical Ecosystem identity;
- the exact ordered prefix declaration sequence;
- the exact Integration scanner and configuration identities used for
  generation;
- prefix enumeration and version-selection policy identities;
- Library-selection policy identity; and
- structured Type identity and Member-anchor format versions.

Changing any item that can change selected packages, scanner observations, or
symbol identity changes the artifact identity or schema version. A consumer
validates the whole identity and canonical Ecosystem association before
exposing any row.

The artifact contains no wall-clock time, machine path, cache path, credentials,
source URI, or transient request identity. Staleness is visible through exact
pinned coordinates and policy versions. Checking current versions is a
separate explicit network-bearing operation.

## Recorded symbols

The artifact records one row only when the Integration owner reports the
symbol as an admitted actionable Type or starter API while scanning the
Ecosystem's generated prefix population.

Each symbol retains:

- its exact prefix-source identity;
- every canonical Integration concept identity that admitted it;
- the scanner's candidate kind and owner-issued evidence;
- exact package ID and version;
- selected target framework, Library, assembly asset, and assembly identity;
- source and declaration order needed by Find;
- for a Type, exact `MetadataTypeDefinitionName`;
- for a Member, producer-issued `MemberAnchor`, structured declaring Type,
  Metadata name, kind, signature, and return Type facts already carried by the
  Integration observation; and
- contained display text for presentation correspondence.

One compact Integration row may represent several overload observations. The
artifact records each exact Member anchor separately so Find can preserve
overload identity and pattern multiplicity. A symbol admitted by several
Integration concepts is stored once with every concept association.

The artifact does **not** record:

- ordinary public Types or members that no Integration concept admitted;
- inferred symbols based on namespace, package prefix, declaring-Type suffix,
  or naming convention;
- external famous Types that supplied evidence but are not actionable
  package-owned currency;
- Integration opportunities; or
- preclassified Find matches.

Names and display text originating in packages use the same construction-time
containment as live scanner results under the
[untrusted-data threat model](untrusted-data-threat-model.md). Display text is
never identity. No assembly bytes, metadata reader state, stream, lease,
callback, or executable content is retained.

## Generation coverage

Generation records enough coverage to explain what the artifact accelerated,
not to claim complete Find coverage:

- prefix enumeration completion and every bound reached;
- every enumerated exact package coordinate and source ordinal;
- package, Library, and assembly selection or typed rejection;
- scanner availability, completion, failure, and configured concept set;
- indexed symbol counts and unresolved symbol identities; and
- known-answer control disposition.

`IndexedEmpty` means the scanner completed and admitted no Integration symbols
for that source. It does not mean Find would return no rows. A rejected,
failed, bounded, or unresolved source stays visible but does not prevent useful
indexed rows from being published.

Source ordinal is owner-issued input to the builder. Canonical serialization
may sort physical records, but lookup restores source and declaration order
from recorded ordinals. Permuting asynchronous acquisition or scanner
completion cannot change bytes or Find order; changing prefix candidate order
is a population change and can.

## Determinism and validation

The artifact is a pure function of exact pinned package content, owner-issued
enumeration and selection evidence, Integration observations, and schema
versions. Packages, Libraries, assemblies, concepts, symbols, failures, and
unresolved identities serialize in defined ordinal order.

Loading validates schema, artifact identity, ordinals, uniqueness, exact
coordinate associations, contained text, and coverage closure before
projecting any symbol. Validation failure rejects the artifact as one typed
outcome. It never drops a malformed record and continues with an apparently
valid index.

## Indexed Find evaluation

The indexed operation takes:

```text
EcosystemFindIndexedIntegrationRequest
  ordered admitted Ecosystem artifacts
  demanded prefix-source identities
  normalized FindQuestion
  remaining semantic row window
```

It projects exact indexed symbols into the portable Type or Member facts
consumed by Find semantic evaluation. The semantic owner performs pattern
matching, classification, declaring-Type filtering, row limiting, source
evaluation, and result construction. The artifact introduces no second
`TypeMatcher`, Member matcher, or query grammar.

Lookup processes artifacts in admitted Ecosystem order, each artifact's sources
in recorded prefix order, and declarations in scanner order. It stops when:

- every indexed symbol was evaluated;
- the remaining semantic row window is full; or
- cancellation is observed.

Lookup returns matching rows, exact published-symbol identities, indexed
coverage, and whether it exhausted every demanded source in the admitted
artifact sequence. A zero-row exhausted lookup means only that no indexed
Integration symbol matched.

Indexed rows consume the shared semantic row window. Indexed sources do not
consume `MaximumPrefixPackages`: that bound counts packages admitted for live
settlement. When live search starts, it receives the unchanged remaining live
candidate budget.

## Live continuation and duplicate suppression

The indexed layer runs before bounded/core settlement and only under the same
owner-issued prefix demand that can authorize the live phase. This design does
not make blocking formats search prefixes by default.

After exhaustion of the admitted artifacts and demanded indexed sources:

- if no semantic row capacity remains, Ecosystem Find Search completes as
  `RowLimitReached` without network work;
- if live prefix work was not authorized, completion names the unsearched live
  prefix and remains partial;
- if live work was authorized and capacity remains, existing prefix
  enumeration and package evaluation start; and
- with no semantic row limit, authorized live work always follows the index.

Live enumeration does not skip a package because it contributed indexed
symbols. The package consumes one live candidate unit when admitted, and its
failures and coverage remain visible. After live evaluation, composition
suppresses only a row whose exact identity and exact producer source equal a
published indexed row:

- Type duplicate key: exact source coordinate plus structured Type identity;
- Member duplicate key: exact source coordinate plus `MemberAnchor`.

Same-spelled symbols from another package, Library, assembly, or declaration
remain distinct. Duplicate suppression never suppresses source failure,
coverage, or candidate accounting. The indexed row remains earlier and is
never retracted.

Index absence, mismatch, malformed content, or partial generation coverage does
not silently disable the live path. The host follows its existing prefix
demand and network authority: authorized live work can proceed beside a
visible index failure; unauthorized work remains unsearched.

## Equivalence

Equivalence is selective and has two levels.

**Symbol equivalence** compares every indexed symbol with the Integration
scanner observation over the same exact Ecosystem, prefix source, package,
target framework, Library, assembly, scanner configuration, and configured
concept set. Structured identity, concept associations, candidate evidence,
order, failures, and coverage match.

**Find projection equivalence** projects one scanner observation and its
indexed form into the same portable Type or Member fact, then runs the same
normalized Find question through the semantic owner. Match classification,
source association, row identity, and display facts match.

The index is not compared with the complete live Find result because it is
selective by design. End-to-end gates instead prove ordered union: indexed
matches first, later non-Integration live matches retained, exact indexed/live
duplicates suppressed, and live failures preserved.

CLI text is not the oracle. Presentation can omit semantic evidence and can
change without changing indexed correctness.

## Performance and size goals

Adoption measures exact NativeAOT production binaries on linux-x64. Index-hit
latency and the added miss-path cost before a bounded result are reported
separately so unrelated bounded acquisition is not attributed to the index.

<!-- markdownlint-disable MD013 -->

| Measure | Goal |
| --- | --- |
| Indexed-evaluation start to first Aspire `.AddRedis` row | 200 ms or less |
| Complete indexed-symbol scan for Aspire `.Add*` | 500 ms or less, excluding terminal output backpressure |
| Index miss before an otherwise identical bounded result | Reported as exact NativeAOT base/head latency; no goal until the first adoption establishes a baseline |
| Network bytes and package-cache bytes before indexed exhaustion | 0 |
| Peak RSS above `dotnet-inspect --version` on the same binary | 32 MiB or less |
| Aspire and Microsoft.Extensions Ecosystem artifacts together | 2 MiB or less checked in; 500 KiB or less Brotli-compressed |
| Time from indexed exhaustion to first live network request | Reported to prove phase order; no latency goal |
| Whole-command first and final row | Reported separately; live completion remains network-dependent |
| Generation | Reported; no goal beyond the scheduled lane timeout |

<!-- markdownlint-enable MD013 -->

Result cardinality and stable typed-result digests accompany every timing.
Time to first indexed row, index exhaustion, first live request, first live row,
and total completion are distinct observations. A step that misses a goal
stops for operator direction.

If the size goal is missed, the first permitted lever is a lossless compact
identity and string-table encoding. Dropping identities, Integration evidence,
coverage, or failures and probabilistic sketches are not permitted.

## Generation and placement

One product-owned builder accepts owner-issued pinned population, scanner
observations, and coverage. A thin generator orchestrates explicit
network-bearing enumeration, acquisition, and scanning, but does not classify,
repair, filter, or reorder Integration content.

`DotnetInspector.Ecosystems` owns the portable construction and codec surface:
`EcosystemFindIndexBuilder` projects one `EcosystemFindIndexBuildRequest`, and
`EcosystemFindIndexJson` publishes an artifact only after complete validation.
`EcosystemFindIndexedIntegration` orders admitted artifacts and demanded
prefixes, then adapts their exact symbols through the Query-owned
`TypeFindCandidateEvaluator` or `MemberFindCandidateEvaluator`. It returns
semantic matches, exact published identities, selective indexed coverage, and
exhausted, row-limit, or canceled completion without opening an assembly or
acquiring a package.
The contract records the product scanner identity, configured Integration
concept sequence, generation-policy versions, and Type and Member identity
format versions; callers cannot substitute free-form identities.

Each Ecosystem artifact is checked in once. The CLI consumes it as an embedded
resource; Inspect Web consumes a static asset built from the same file.
Ordinary build, test, startup, and indexed lookup do not regenerate it.

The initial CLI artifacts live in
`src/DotnetInspector.Ecosystems/FindIndexes/`. The explicit network-bearing
`eng/generate-ecosystem-find-indexes.cs` generator enumerates at most 25
packages per declared prefix, selects `net10.0` compile Libraries, runs the
complete product Integration interpretation, records bounded-enumeration and
source gaps, and writes deterministic Aspire and Microsoft.Extensions
artifacts. The 25-package bound is the smallest current bound that includes all
four known-answer controls in source order.

Generation fails when any known-answer control is missing, identity or coverage
is inconsistent, deterministic regeneration differs, or the artifact exceeds
the adopted size goal.

## Evidence and gates

<!-- markdownlint-disable MD013 -->

| Property | Gate | Lane |
| --- | --- | --- |
| Deterministic serialization | Build fixture artifacts twice with fixed ordinals and permuted acquisition/scanner completion order; compare bytes | PR-fast |
| Integration symbols only | Fixture scanner admits actionable and non-actionable observations; only owner-admitted exact Type/Member candidates appear | PR-fast |
| Ecosystem artifact admission | Selecting one of two fixture Ecosystems admits only that Ecosystem's artifact; repeated prefix or scanner identities do not admit the other artifact | PR-fast |
| Multi-prefix artifact | One fixture Ecosystem with two ordered prefixes produces one artifact whose rows retain their exact prefix source and evaluate in prefix order | PR-fast |
| Identity and overloads | Multi-concept symbol and compact multi-overload row retain every concept and exact underlying Type or Member identity once | PR-fast |
| Generation gaps stay visible | Rejected package, failed Library, unavailable scanner, unresolved identity, and bounded enumeration remain coverage, never Find absence | PR-fast |
| Symbol equivalence | Fixture live scanner observations and indexed symbols compare by typed identity, evidence, concept, order, failure, and coverage | PR-fast |
| Find projection equivalence | Direct, glob, declaring-Type-filtered, multi-pattern, and finite-row questions classify one live/indexed symbol equally | PR-fast |
| Stopping conditions | Index exhaustion continues authorized live work; full row window, cancellation, and absent live authority start no network work | PR-fast |
| Indexed packages are reinspected | Indexed package contributes a second non-Integration live match; package is evaluated and only exact duplicate is suppressed | PR-fast |
| Exact duplicate key | Same-spelled rows from different sources remain; exact source-backed Type and Member duplicates collapse with indexed row first | PR-fast |
| Bounds | Indexed rows consume row capacity but not live package candidates; reinspected indexed packages consume live candidate units | PR-fast |
| Known answers | Checked-in artifacts reproduce `AddRedis`, `AddPostgres`, `AddRedisClient`, and `AddHttpClient` with exact coordinates | PR-fast |
| Pinned corpus equivalence | Regenerate scanner observations for every pinned coordinate and compare | Slow, network; daily |
| Offline index phase | Debug network guard proves no request and no package-cache write before index exhaustion | Adoption PRs |
| Performance and size | Exact NativeAOT measurement of every goal with result cardinality and typed digests | Adoption PRs |
| Both hosts use the same Ecosystem artifacts | CLI embedded-resource and Inspect Web static-asset gates compare each artifact digest and host-neutral indexed result | Host adoption PRs |

<!-- markdownlint-enable MD013 -->

## Adoption sequence

Six steps lead from design to observable behavior in both hosts:

1. **Design.** This artifact owner and its selective Integration-symbol,
   coverage, equivalence, and performance contracts.
2. **Builder.** Product-owned builder and deterministic per-Ecosystem fixture
   artifacts over owner-issued Integration scanner observations.
3. **Find projection.** One Find-semantic-evaluation input over portable indexed
   Type and Member facts.
4. **Ordered CLI composition.** Ecosystem Find Search inserts indexed results
   before live prefix work, preserves row and candidate bounds, and suppresses
   only exact duplicates; checked-in Aspire and Microsoft.Extensions artifacts
   ship with exact NativeAOT and network-phase evidence.
5. **Inspect Web and Spotlight.** The same per-Ecosystem artifacts and
   host-neutral indexed evaluation under Browser operation authority.
6. **Freshness lane.** Scheduled regenerate-and-compare reporting coordinate,
   scanner, symbol, coverage, size, and performance drift.

Steps 3 and 4 are focused adoptions by existing owners, not responsibilities
absorbed into this artifact design. The implementation tracker keeps them
separate unless one qualifies for the one-first-adopter exception in
[Design scope and composition](../design-scope.md#stage-implementation-after-locking-the-design).

The live path is not retired. It remains the complete search for arbitrary Find
symbols and is required for custom sources, explicit refresh, generation, and
unindexed Ecosystems.

## Non-claims

- The index is not a complete Type or Member inventory.
- Index exhaustion or a miss never proves no Ecosystem Find match.
- Bounded/core Ecosystem populations are not indexed here.
- Include-all visibility is not expanded beyond symbols admitted by the
  Integration owner.
- The artifact does not cover packages outside the exact generated prefix
  population.
- No automatic background refresh, provider cache, Azure Function, or server
  computation exists.
- No fuzzy, natural-language, receiver-applicability, or chain-navigation
  semantics are added.
- The index does not weaken semantic row limits or the later live package
  candidate bound.

## Alternatives not selected

- **Complete public-symbol corpus.** It is much larger, duplicates broad Find
  inventory semantics, and is unnecessary for accelerating the Integration
  APIs users seek first.
- **Cache rendered query results.** It anticipates spellings, duplicates Find
  matching, and cannot answer new questions over the same symbol set.
- **Skip indexed packages during live search.** Selective Integration coverage
  cannot prove those packages contain no other matches.
- **Reuse the Receiver Index.** It organizes extension members by receiver
  applicability rather than Ecosystem Integration admission.
- **Ship package archives or metadata images.** Hosts still pay parsing,
  admission, and Browser resource costs.
- **Silently stop after the index.** A selective accelerator cannot satisfy an
  authorized complete live prefix question.
- **Index all of nuget.org.** No declared population, completion boundary, or
  product ownership exists for that scale.
