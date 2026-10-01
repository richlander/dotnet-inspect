# Ecosystem Find Search

## Status and owner

Proposed focused design for
[#8811](https://github.com/richlander/dotnet-inspect/issues/8811).

**Ecosystem Find Search** owns one host-neutral operation:

> Given one normalized Find question and an ordered selection of exact
> Ecosystem registrations, evaluate every selected Ecosystem's bounded
> populations before any package-prefix population, evaluate each distinct
> concrete source's settlement, inventory, and source-local matches at most
> once, and return ordered durable Find blocks that retain every admitting
> Ecosystem membership, scoped failure, and completion fact.

The operation is a map/reduce over source-owner facts:

1. map each distinct bounded or prefix-discovered source to one owner-issued
   immutable source inventory and source-local match result;
2. reduce those facts into every selected Ecosystem whose declarations admit
   the source, applying the Find owner's population-level tier selection to
   each bounded Ecosystem independently; and
3. publish phase-identified blocks without retracting an earlier block.

The owner defines phase scheduling, cross-Ecosystem source deduplication,
membership reduction, the event and completed-Document contract, work bounds,
and cancellation checkpoints. It does not define Find grammar or matching,
Ecosystem membership declarations, source discovery, package or platform
realization, host output, Spotlight interaction, or cache storage.

## Demo

The first CLI production scenario is:

```console
dotnet-inspect find '.Add*' --ecosystem aspire --jsonl
```

The bounded block completes first from `Aspire.Hosting` and
`Aspire.Hosting.Testing`, including `AddProject`, `AddContainer`, and
`AddParameter`. Prefix work then discovers and evaluates concrete `Aspire.*`
packages, producing package-scoped blocks such as `AddRedis` from
`Aspire.Hosting.Redis` and `AddPostgres` from
`Aspire.Hosting.PostgreSQL`.

The all-Ecosystem form is:

```console
dotnet-inspect find '.Add*' --ecosystem all --jsonl
```

`all` is CLI syntax, not an Ecosystem identity. The host resolves it to the
catalog's ordered exact registrations before invoking this operation.
Shared sources are evaluated once, while each result retains all selected
Ecosystem memberships that admit it.

The corresponding Browser scenario starts with no visible Ecosystem hit
groups. Bounded blocks reveal Ecosystems as they settle. Opening the
Ecosystems category can list the complete catalog independently of hits.
Selecting Aspire starts a replacement request with Aspire prefix demand; it
does not mutate the running request.

## Basis and owner map

<!-- markdownlint-disable MD013 -->

| Owner | Contract consumed |
| --- | --- |
| [Static Ecosystem Packs](ecosystem-packs.md) | Canonical Ecosystem identity, product order, core packages, platform populations, and ordered package-prefix declarations |
| [Find Workspace scope](find-workspace-scope.md) | Explicit Ecosystem selection, bounded named-population realization, and the rule that prefix populations are a separate operation |
| [Find type-search service](find-search-service.md) and [Find member-name search service](find-member-search-service.md) | Owner-issued pattern grammar, Direct/Glob/namespace/Member classification, candidate rows, source-local failures, and result limits |
| [Type Find population selection](type-find-population-selection.md) | First-nonempty Prefix, Substring, and Partial settlement over each complete ordered bounded Type population while retaining exact source associations |
| [Incremental package-prefix candidates](package-prefix-candidate-stream.md) | Pull-driven package candidates, source order, paging, cancellation, and source completion |
| PlatformHouse and PackageHouse | Exact source settlement, realization, provenance, and visible failure |
| [Engine-to-browser async event streams](engine-browser-async-event-stream.md) | Progress, durable item, item failure, and completed event categories |
| [Inspection Envelope](inspection-envelope.md) | Completed host-neutral Content, Share, and diagnostics |
| [Host-observable content kinds](host-observable-content-kinds.md) | Document, partial completion, and complete-empty meaning |
| [Output Shapes](output-shapes.md) | CLI and Browser lowering of the completed content and streamed rows |
| CLI and Inspect Web focused owners | Syntax, format defaults, operation authority, presentation, focus, navigation, and activation |

<!-- markdownlint-enable MD013 -->

Type population-level settlement now has a host-neutral selector. Source
realization and inventory, source-local Direct/Glob and Member matching, and
CLI/Browser plan lowering remain adoption prerequisites rather than
responsibilities absorbed here. An adopter supplies typed boundaries that:

1. realize and inventory one source into immutable source-local facts;
2. match one normalized question against those facts without reading the
   source again; and
3. apply the Type selector, or another Find-owner-issued population contract,
   to references from one bounded source group or one exact prefix package.

This operation schedules and associates those results; it never reconstructs
a candidate from display text.

## Request

The semantic request has four inputs:

```text
EcosystemFindSearchRequest
  FindQuestion
  Ecosystems
  PrefixDemand
  MaximumPrefixPackages
```

`FindQuestion` is an owner-issued Type or Member Find plan. It carries parsed
patterns, visibility, and evaluator-owned semantic row limits. This operation
does not parse a leading dot, commas, globs, generic notation, or exact member
selectors.

`Ecosystems` is a nonempty ordered array of exact selected registrations.
Each entry retains:

- canonical registration identity and display metadata;
- product order;
- bounded population declarations;
- core-package declarations; and
- package-prefix declarations in authored order.

The array rejects duplicate registration identities. Host syntax such as
`--ecosystem all`, short names, repeated options, or an active Browser subject
must be resolved before construction.

Array order is search order. A host lowers an Ecosystem selection to its
[layered Find order](ecosystem-hierarchy.md#layered-find), so each selected
Ecosystem precedes its ancestors. A `--where ecosystem=` predicate removes
every other layer from the array before construction.

`PrefixDemand` is an immutable set of selected registration identities.
Changing Browser demand creates a replacement operation through the Browser's
operation authority. There is no mutable "add a prefix while this request is
running" side channel.

`MaximumPrefixPackages` is a positive operation-work bound. It does not count
bounded platform or core sources, does not select final Find rows, and does not
claim prefix exhaustion when reached.

The request is rejected before source work when:

- its Find question is invalid;
- the selected registration array is empty or repeats an identity;
- prefix demand names an unselected registration;
- no selected registration contributes either a bounded population or a
  demanded prefix; or
- the prefix-package bound is outside the owner-issued supported range.

An Ecosystem with no bounded population remains valid when one of its prefixes
is demanded. Its bounded block is a complete empty block, not an invalid
Workspace or proof that its prefix contains no match.

## Source and membership identity

The operation keeps four facts separate:

1. **Producer source** is the exact owner-issued Package, platform, or Library
   coordinate that supplied the declaration.
2. **Ecosystem membership** identifies each selected registration that
   admitted that source.
3. **Membership basis** is `PlatformPopulation`, `CorePackage`, or
   `PackagePrefix`, including the exact declaration identity.
4. **Acquisition origin** records where bytes were obtained when its owner
   exposes that evidence; it is not producer or Ecosystem identity.

For one Ecosystem, an exact platform or core membership takes precedence over
a matching prefix membership. A source may still carry a prefix membership
for another selected Ecosystem. Membership arrays follow selected Ecosystem
order and contain at most one basis per Ecosystem.

The producer source is never inferred from Library, package, assembly, or
Ecosystem display text. Deduplication consumes source-owner request and
coordinate identity:

- equal bounded source requests settle once;
- exact package IDs compare with the package owner's case-insensitive
  identity before version settlement;
- settled Package coordinates include the exact selected version and asset
  context;
- platform sources include their family and selected target generation; and
- two requests that differ in target policy or source authority remain
  distinct even when their display names match.

Once a prefix candidate exposes a valid package ID, the reducer computes its
complete selected membership from all selected core and prefix declarations.
It does not wait for an overlapping later prefix search to rediscover the same
ID. A later duplicate therefore adds no work and cannot change an already
published membership vector.

## Phase contract

The operation has a global two-phase barrier:

```text
Bounded
  -> every selected Ecosystem bounded block settles
  -> Prefix, when demand exists
  -> Completed
```

Cancellation may terminate either active phase. No prefix search, package
settlement, or package evaluation starts before all bounded blocks settle.

### Bounded phase

The bounded phase includes every selected registration's finite named
populations:

- platform populations;
- core packages; and
- exact Library populations when a registration supplies them.

Each selected Ecosystem receives one complete bounded Find block over its
contributing sources. A shared source may contribute to several blocks but is
settled, realized, inventoried, and source-locally matched once. Each
Ecosystem's Find population selector can reference those immutable facts
without repeating source work. A bounded block can settle as complete, partial,
or failed according to its owner-issued source outcomes.

With a finite row window, a layer starts only after every earlier layer has
settled and the window still has room; sources within one layer may still
settle concurrently. Without one, blocks may settle in any execution order.
Each block carries its stable selected
Ecosystem ordinal; arrival order is not result order. A blocking consumer
orders bounded blocks by that ordinal. A streaming consumer may reveal a block
immediately and must not interpret arrival order as rank.

The bounded evaluator applies its ordinary complete-population Find contract,
including its full owner-issued tier ladder. A later prefix result never
retracts or reclassifies a bounded row.

### Prefix phase

After the bounded barrier, demanded prefixes produce candidates through their
owner's incremental page stream. Prefix declarations are considered in
selected Ecosystem order and then authored prefix order. Equal declarations
may share one source enumeration; overlapping but unequal prefixes remain
distinct source observations.

Each newly admitted package ID receives a stable candidate ordinal before
settlement. Candidate work is case-insensitively deduplicated across every
selected prefix. One settled exact package is evaluated as one independent
Find scope and produces at most one durable prefix block.

A durable prefix block contains only owner-certified monotonic Find rows:

- Type rows whose match remains true when another package is evaluated; and
- Member `Direct` or `Glob` rows from the settled package.

Population-relative similarity suggestions are not durable prefix rows in the
first contract. Computing a global partial-ranking answer would require
prefix exhaustion and would prevent progressive publication. `NotFound` is
package-local progress, not a durable block.

The evaluator may apply its ordinary tier precedence inside one exact package.
The aggregate operation does not claim one global first-nonempty tier over the
eventual prefix universe. Every durable row retains its package block, phase,
and match classification, so a stronger later match does not invalidate an
earlier truthful row.

Prefix blocks carry candidate ordinals. Bounded concurrent execution may
publish blocks in settlement order; deterministic consumers can order the
completed Document by candidate ordinal. The stream does not claim stable
wall-clock arrival order across hosts.

## Event and completed content

The operation adopts the shared async-event categories with this closed
vocabulary:

```text
EcosystemFindSearchEvent
  Progress
    phase
    optional Ecosystem identity
    completed work
    optional honest total
  BoundedBlock
    Ecosystem identity and ordinal
    owner-issued Find block
    source coverage
  PrefixBlock
    exact Package coordinate and candidate ordinal
    Ecosystem memberships
    owner-issued Find block
  ItemFailure
    phase
    exact source or prefix identity
    optional Ecosystem memberships
    owner-issued failure
  Completed
    EcosystemFindSearchSummary
```

Bounded blocks are durable even when they contain zero rows, because zero
matches and "not settled yet" are distinct Ecosystem states. Prefix blocks
with zero monotonic rows are not durable; their completed work contributes to
progress and terminal accounting. Scoped failures are durable and never
converted into empty blocks.

Normal completion returns:

```text
InspectionEnvelope<EcosystemFindSearchDocument>
  Content
    request identity
    bounded blocks in selected Ecosystem order
    prefix blocks in candidate order
    scoped failures
    completion and coverage
  Share
  Diagnostics
```

The completed Document repeats durable semantic content so blocking hosts,
structured output, cache consumers, and replay do not depend on event history.
An adopter validates streamed item and failure counts against the terminal
summary before publishing completed state.

Explicit cancellation or host supersession produces no completed inspection
envelope. Already published durable blocks remain valid partial observations,
while the host marks the operation canceled. Events from a superseded Browser
operation cannot enter its replacement outcome.

## Bounds, failures, and completion

The bounded phase is finite and attempts every selected bounded source unless
canceled or stopped at a layer boundary. The Find owner delegates one
row-window optimization: when the request carries a finite row window, the
bounded phase settles layers in array order, and once the settled layers
fill the window, later layers do not start. A layer that has started always
settles completely, so stopping never truncates a block. The operation then
completes as `RowLimitReached`, naming each unsearched layer, and no prefix
work starts. Without a finite window, every layer is attempted, as before.

The prefix-package bound is shared across all demanded prefixes. A candidate
consumes one unit when admitted for settlement, including a candidate that
later fails. Duplicate candidates consume no additional unit. When the bound
is reached, later prefix pages and declarations do not start.

Terminal completion distinguishes at least:

- `Exhausted`: every demanded prefix source exhausted;
- `CandidateLimitReached`: the package bound stopped later work;
- `SourcePageLimitReached` or `ClientPageLimitReached`: a prefix source
  reported its owner-issued bound;
- `RowLimitReached`: the row window filled at a layer boundary; the named
  later layers did not start;
- `Partial`: useful blocks exist beside scoped bounded or candidate failures;
  and
- `Failed`: no valid Document can be constructed under the operation's
  owner-issued failure rules.

The Document retains per-prefix completion rather than collapsing several
sources to one unexplained boolean. A complete empty Document proves no match
only across every successfully completed bounded source and exhausted demanded
prefix source named by its coverage.

## Execution and cancellation

Execution policy is host-neutral within declared bounds:

- a CLI adopter may evaluate distinct settled sources with bounded parallel
  concurrency;
- Browser/Wasm may evaluate them cooperatively and sequentially; and
- either host may use a cache that preserves the same source and membership
  identities.

Concurrency changes settlement latency, not source identity, membership,
candidate ordinal, completed-Document order, or the bounded-before-prefix
barrier.

The cancellation token crosses prefix enumeration, exact settlement,
realization, inventory, Find evaluation, and event publication boundaries.
After cancellation is observed, the operation requests no new page or source
work and publishes no new durable event. In-flight owners retain their own
resource drainage and typed cancellation behavior.

The checked
[Ecosystem Find Search model](models/ecosystem-find-search/README.md)
explores concurrent source settlement, the global phase barrier, source
deduplication, complete membership publication, cancellation, and terminal
completion. The model is evidence about the bounded abstract scheduler, not a
proof of CLI, Browser, source, House, or Find implementation.

## Cache boundary

The operation does not own a persistent cache. Adopters may reuse
owner-issued source indexes only when the cache key preserves:

- exact Package or platform source coordinate;
- selected target framework and asset context;
- Find index schema version; and
- source-owner generation or immutable-content evidence.

Exact NuGet package versions are immutable content. Prefix enumeration is an
eventual catalog observation and requires separate source coverage and
freshness evidence; a cached package index cannot prove that a prefix has no
new package or version.

Cache hits do not change logical producer source, Ecosystem membership, or
completion. Acquisition/cache origin remains optional provenance evidence.
Defining storage, eviction, persistence, refresh interaction, and cache
publication belongs to the applicable cache owner.

## Host adoption

The CLI is the first production adopter:

- it resolves short and canonical names plus `--ecosystem all`;
- it maps streaming formats to prefix demand by default;
- it maps blocking formats to no prefix demand unless the user explicitly
  requests it;
- it renders producer `Source` separately from Ecosystem membership; and
- it consumes the same typed stream for sequential or bounded-parallel
  execution.

Those mappings are CLI policy. Output format is not passed to the semantic
operation; the host lowers its default to an explicit `PrefixDemand`.

Inspect Web then adopts the same request, event, and Document:

- the initial Spotlight Ecosystems search selects all registrations with no
  prefix demand;
- bounded blocks reveal hit-bearing Ecosystems;
- the Ecosystems category lists the catalog independently of result blocks;
- choosing one Ecosystem starts a replacement request that demands its
  prefixes; and
- operation authority suppresses stale publication after text, scope, or
  demand replacement.

Spotlight grouping, visible batch size, focus, activation, and navigation
remain Browser-owned. Selecting a Type or Member requires a separately
owner-issued exact activation association; this operation never constructs
one from its rendered row.

## Evidence and acceptance

The authentic first assets are:

- Aspire bounded roots `Aspire.Hosting` and `Aspire.Hosting.Testing`;
- `Aspire.Hosting.Redis` and `Aspire.Hosting.PostgreSQL` from the `Aspire.`
  prefix;
- Runtime platform content acquired both from an installed pack and a package
  fallback without changing logical Ecosystem identity; and
- one package admitted by overlapping selected Ecosystem declarations.

Release gates for an implementation must establish:

1. every bounded block settles before prefix work starts;
2. shared source settlement, realization, inventory, and source-local matching
   occur once while every applicable membership is retained;
3. core membership outranks same-Ecosystem prefix membership;
4. bounded and candidate ordinals make completed content deterministic under
   parallel settlement;
5. late duplicate prefix candidates add no work or membership mutation;
6. cancellation requests no later page or source and publishes no later
   durable block;
7. partial and failed sources remain visible beside healthy results;
8. CLI and Browser equivalent requests produce equal completed Content; and
9. the pathological case of an early useful block followed by a blocked,
   failed, or canceled later source retains the early block without claiming
   exhaustion.

NativeAOT evidence is required for every CLI terminal adopted by the
implementation. Browser/Wasm evidence must include a cold all-Ecosystem
bounded pass and one demanded prefix phase. This design makes no fixed latency,
allocation, or cache-hit claim.

## Non-claims

This design does not:

- define Type or Member pattern grammar, match tiers, or similarity;
- create a global similarity ranking across an unexhausted prefix universe;
- define Package Query rows or replace Package Query Ecosystem population;
- change package-prefix paging, source relevance order, or source limits;
- define package/platform version selection, realization, or Workspace
  admission;
- make a discovered package a retained Workspace member;
- grant graph-traversal permission from Find discovery;
- define CLI flags, columns, stderr wording, exit status, or default format;
- define Spotlight scopes, grouping, batch size, focus, or activation;
- define cache storage, persistence, refresh, or eviction;
- search every prefix without explicit host-lowered demand; or
- treat acquisition origin as producer or Ecosystem identity.
