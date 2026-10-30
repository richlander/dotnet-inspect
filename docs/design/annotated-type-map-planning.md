# Annotated Type Map planning

## Status and owner

This document owns the host-neutral registry and exact-selection planning
substrate for the [Annotated Type Map](annotated-type-map.md). It owns:

- immutable registration of what a map can carry: its map-carried
  contributions (the two shape-fact families and the fact stream) and its
  admitted finding families;
- binding each registered family to terminal-specialized concrete
  `InspectionQuery<AnnotatedTypeMapContribution>` adapter queries;
- two request forms over those families: a **map request**, whose result the
  Map owner settles into a map or map diff, and a **family contribution
  request**, which returns only the selected families' contributions;
- resolution of one closed exact family selection;
- deterministic projection of the typed queries that selection needs; and
- the rule that each adapter constructs only its own family.

Its exact claim is:

> For one exact Type, or a set of Member-scope questions within it, one or two
> versions, one immutable registry snapshot, one exact family selection, and
> one request form, Annotated Type Map planning resolves only exact registered
> families admitted for that focus, binds each family its result reads to its
> adapter query, rejects unsupported or unavailable selections explicitly, and
> returns one immutable typed-query demand whose execution remains owned by
> the inspection query catalog.

It is tracked by
[#10215](https://github.com/richlander/dotnet-inspect/issues/10215) and
[#10217](https://github.com/richlander/dotnet-inspect/issues/10217).

This owner mirrors
[Annotated Type Relations planning](annotated-type-relations-planning.md),
which plans the relations reaching outside a Type; this one plans the families
inside it. The two stay separate instances until both have a production
adopter. Factoring one shared registration and planning substrate from them is
tracked by [#10880](https://github.com/richlander/dotnet-inspect/issues/10880).

## Why

The map's [adoption](annotated-type-map.md#adoption) needs one version's API
shape facts per Member. Today the only provider of those facts is
`ApiSurfaceExtractor`, which computes every fact it knows for every admitted
Member on every call: signature spelling and identity, memory-safety facts,
JSExport evidence, layout, implementation flags, and obsolescence. A caller
narrows it with flags and Type filters, but cannot say which facts it needs.
Image-wide preparation therefore runs once per call, not once per image.

Two measurements show the cost:

- [#10221](https://github.com/richlander/dotnet-inspect/issues/10221) found
  that every extraction rebuilds the image's memory-safety index, about
  5.5 ms per side on CoreLib, although the API comparison never reads it.
- [#10776](https://github.com/richlander/dotnet-inspect/pull/10776) settles
  uncertain API census differences by extracting the selected Type on both
  sides. At its head `1e4422408`, on CoreLib 10 to 11 under NativeAOT, the
  memory-safety index was about 90% of that extraction time.

Both consumers need only the API shape facts of some Members. A request that
names that family, for those Members, lets the producer skip the rest by
construction, rather than by a flag added to one entry point.

## Consumed contracts

| Owner | Contract consumed |
| --- | --- |
| [Annotated Type Map](annotated-type-map.md) | Map entries, shape facts, admitted findings, Member keys, scopes, listing, terminals, and the map diff |
| [Scope symmetry](scope-symmetry.md) | Library, Type, and Member scopes as owner-issued subjects |
| [Finding Diff Any](finding-diff-any.md) | Member identity and ownership, the census key, and the API and Body equality |
| [Query Space producer capabilities](query-space-producer-capabilities.md) | Requirements, provisions, shared construction, and resource-free plan selection |
| [Query Space composition](query-space-composition.md) | Closed host-neutral query spaces and terminal-specialized requests |
| [Finding nomenclature](finding-nomenclature.md) and [Annotated Source Finding provenance](annotated-source-finding-provenance.md#classification-contract) | Finding descriptors and their classifications |
| Typed inspection query catalog | Exact query identity, prerequisite closure, cost, execution order, shared work, and result access |

Planning consumes these contracts without redefining a fact, finding,
descriptor, key, equality, scope, listing rule, or execution rule.

## Families

A **family** is the unit a producer contributes. There are three kinds:

- the two **shape-fact families**: the map's API facts and its Body facts at
  Member grain, as [the map](annotated-type-map.md#the-map) defines them;
- the **fact stream**: for each method a Member owns, the keys of every fact
  in the method's fact census, admitted or not, in the order the map diff
  matches them; and
- a **finding family**: one finding descriptor family the map admits, under
  [Admitted findings](annotated-type-map.md#admitted-findings).

The shape-fact families and the fact stream are the **map-carried
contributions**: the map holds them whatever analyses a request names, where
the map's result reads them. The map owns which families exist and which are
admitted. This document adds none. A fact the map does not carry, such as a Member's memory-safety
contract or JSExport evidence, is not a family here; a producer that computes
it does so outside any plan of this owner.

## Request forms

### Map request

A map request asks for a map at one version, or a map diff over a version
pair, with a Rows, Count, or Exists terminal. Its finding families are the
analyses the map's request names; a one-version request must name at least
one. The map-carried contributions are not selectable. Resolution demands, for
each **result kind**, exactly what that result reads:

| Result kind | Shape-fact families | Fact stream | Selected finding families |
| --- | --- | --- | --- |
| One-version Rows | both | yes | yes |
| One-version Count or Exists | no | no | yes |
| Map-diff Rows | both | when a finding family is selected | yes |
| Map-diff Count or Exists | both | no | kept in the selection, no demand |

- A one-version Rows result is a retained map: it holds both shape-fact
  families and the fact stream so that two retained maps can be compared, and
  its findings for listing.
- One-version Count and Exists read only admitted findings, the map's listing
  rule for one version, so they demand only the selected finding families.
- A map-diff Rows result lists by the shape comparison across both axes, as
  the Agreement gate requires. When it names finding families, its fact
  comparison matches facts by their position in the whole stream before
  filtering them to admitted descriptors. When it names none, no finding
  transition can be admitted, so it demands no fact stream and builds no
  census.
- A map diff's Count and Exists read only the shape comparison. They demand
  only the shape-fact families; the selected finding families stay in the
  resolved selection but bind no adapter, because they cannot change the
  result. A Type's Exists in the map diff therefore stays the shape
  comparison, and a host drawing Type marks builds no fact census and computes
  no map of an unopened Type.

Which analyses a request names never changes how a fact is matched.

### Family contribution request

A family contribution request asks for the selected families'
contributions, at one version or over a pair, and nothing else. It may select
any non-empty set of registered families, including a single shape-fact
family or the fact stream. Its result is the contributions themselves, keyed by Member key, each
with its own outcome. It has no map listing, no Rows, Count, or Exists, and no
map diff; it is not a map. A consumer that compares two contributions, such as
Finding Diff Any's API census settlement, owns that comparison.

## Focus

Both forms take one exact Type focus authority. For Member scope, a request
may carry a non-empty set of that Type's Finding Diff Any Member keys. The set
is a set of Member-scope questions, one per key, as
[Scope symmetry](scope-symmetry.md) frames each scope as one owner-issued
subject: each Member keeps its own result and outcome. The set lets those
questions share one plan and one typed-query demand; it introduces no scope.
For a map request, each Member's answer follows the map's Member-scope row.

A Member key is a Finding Diff Any census key's exact bytes; for a pair, keys
are Finding Diff Any's side-independent keys. The request boundary checks a
key set for presence and repetition only, because no public key type exposes
the declaring Type it embeds: that part of the census key grammar is internal
to Finding Diff Any. The adapter that answers the questions reads the focus
Type's census, so a key outside it is a typed failure of that Member's
question, never a silent empty answer.

## Registration

One family registration binds:

- the family's exact descriptor;
- whether it supports Type focus, Member-scope questions, or both; and
- its adapter queries: one per result kind the family serves in the table
  above, and one for family contribution requests. One-version and map-diff
  results of the same terminal never share an adapter.

The descriptor remains the family's identity; registration introduces no
second id. A registration supporting Member-scope questions declares how its
producer bounds its work to the requested keys.

An adapter query is an `InspectionQuery<AnnotatedTypeMapContribution>`. Its
required and optional dependencies stay registered in the inspection query
catalog; this registry neither repeats nor executes that dependency graph.
Image-scoped preparation, such as a metadata index, is a catalog prerequisite
of exactly the adapter queries that read it.

Registration rejects repeated descriptor instances, distinct lookalike
descriptors with one id, a family the map does not admit, and a registration
with no adapter query or no supported focus.

## Exact request and resolution

One planning request carries:

- the request form, and for a map request, its terminal;
- one exact Type focus authority, and optionally a non-empty Member key set;
- one image, or an ordered image pair; and
- the selected families: finding families for a map request, any non-empty
  family set for a family contribution request.

This slice accepts only exact family selections. All-applicable requests,
presets, and host gestures are later request UX.

Resolution uses descriptor instance identity, not id text. For each family that the
result kind demands, it requires a registration supporting the requested
focus and the adapter query for that result kind, present in the supplied
catalog. A selected family the result kind does not demand is retained in the
resolved selection and binds no adapter. A pair
request resolves the same families on both sides; comparing the two sides
stays with the map diff or with the consumer of a contribution request.
Failure is typed and names the affected family. Resolution never substitutes
another terminal or form, widens Member-scope questions to the whole Type, or
picks a same-named query.

## Plan and execution boundary

The accepted immutable plan retains the request form, terminal, focus, Member
keys, versions, the resolved registrations in registry order, each family's
exact adapter query, a distinct typed-query demand in deterministic order, and
the maximum catalog-issued cost over that demand.

Repeated query definitions collapse only in the demand projection.

The plan contains no execution delegate. The consumer passes its demand to the
inspection query catalog, which owns dependency expansion, execution, sharing,
cancellation, and result access. A focused generation owner settles a map
request's contributions into the map, as
[Annotated Type Relations generation](annotated-type-relations-generation.md)
does for relations.

## Each adapter constructs only its family

Each adapter query is its own provision. It constructs its family, at the
plan's focus, and no other facts; it does not compute a broader result and
filter it. Two families share construction only of a prerequisite that both
read, such as a per-image index, through the catalog's prerequisite sharing.

The method **fact census** is such a prerequisite. The fact-stream
contribution and every finding family read it: the fact stream keeps every
fact's key, and a finding adapter derives its admitted findings from the same
census without constructing facts the census does not hold. The census is
built once per method a plan demands, and only when a demanded family reads
it.
Shared construction is not coverage: one family's result never satisfies
another family's requirement, as the
[producer capabilities](query-space-producer-capabilities.md#covering-relationship)
owner states.

`ApiSurfaceExtractor` is the first provider of the API shape-fact family. Its
adapter constructs only the facts the complete API comparison reads, at the
requested Member keys: no memory-safety facts, no JSExport evidence, and
therefore no memory-safety index. A narrower producer, such as one over
`MetadataDeclarationSession` or the Finding Diff Any census, may later
register for the same family; the plan chooses from the request and
host-known inputs before opening an image, never from a completed result.

## Failure contract

Resolution returns one typed rejection for an unknown family, a family the
map does not admit, a map-carried contribution selected in a map request, an
unsupported focus, an unsupported form or terminal, or an adapter query
absent from the supplied catalog. Invalid construction, such as a non-Type
focus, an empty or repeated Member key set, duplicate families, a map request
with no terminal, a family contribution request with a terminal or no family,
or a one-version map request with no finding family, fails at the request
boundary. Registration defects fail while forming the
immutable catalog. None becomes an empty successful plan.

## Traceability

A hard-coded provider is traced by reading its code: a fact's field leads to
the line that sets it. A registry replaces that with an open set of producers,
so this owner keeps three trails instead.

- **The family is the trail.** Every contribution carries its exact family
  (`AnnotatedTypeMapContribution.Family`), and every plan records, in registry
  order, each family, why it is there (selected, map-carried, or selected but
  not demanded), and the adapter query it binds
  (`AnnotatedTypeMapPlan.Trail`). Which producer supplied a fact is read from
  the result and its plan, not searched for in code.
- **The registry snapshot is pinned.** `AnnotatedTypeMapCatalog.Describe`
  states each registration as one stable line: family, kind, classification,
  focus support, and each result kind's adapter query. A gate test pins a
  registry's snapshot, so adding or swapping a producer is a visible review
  diff. The production registry's snapshot is pinned from the first
  production registration, adoption step 4.
- **The plan explains itself.** `AnnotatedTypeMapPlan.Explain` states the
  result kind and one line per family with its reason and adapter. Hosts show
  it as Debug evidence when they expose map requests, the map's adoption steps
  6 and 7.

A finding family declares its provenance classification when it is created,
and registration admits only Local instruction. The provenance matrix does not
yet type its classifications in code, so the declaration is the producer's,
gated by the admission test.

## Gates

- **Exact identity:** a lookalike family descriptor with a registered id
  cannot resolve.
- **Registration uniqueness:** duplicate instances and duplicate ids fail
  before a catalog is published.
- **Admission:** a family the map does not admit cannot register.
- **Result kinds:** each map request demands exactly its row of the result-kind
  table; a map request that selects a map-carried contribution is rejected,
  and a one-version request with no finding family is rejected.
- **Census prerequisite:** the fact stream and every finding family in one plan
  read one fact census per demanded method. A plan whose demanded families read
  no census, including every map-diff Count or Exists plan, builds none: a
  map-diff Exists fixture that selects a finding family resolves it in the
  selection, binds no adapter for it, and builds no census, and a map-diff
  Rows fixture that selects no finding family builds no census.
- **Member questions:** a Member key set resolves only families supporting
  Member-scope questions, keeps one result per key, and never widens to the
  whole Type.
- **Result-kind specialization:** each request selects only the exact adapter
  query of its result kind; an unsupported form or result kind fails.
- **Catalog authority:** an unregistered adapter query fails instead of
  executing through an alternate path.
- **Follow the T:** every adapter query has the concrete
  `AnnotatedTypeMapContribution` result type; general query infrastructure
  never understands it.
- **Own family only:** the API shape-fact adapter builds no memory-safety index
  and constructs no memory-safety or JSExport facts, and its result compares
  equal, under the complete API comparison, to the complete extraction of the
  same Members.
- **Shared prerequisites:** families bound to one query, or reading one
  prerequisite, produce one demand entry for it while staying separate
  selections.
- **Determinism:** equal registry and request snapshots produce equal ordered
  family and query demands.
- **Trail:** every resolved family appears in the plan's trail with its reason
  and adapter, and a family selected but not demanded binds none.
- **Snapshot:** a registry's `Describe` lines are pinned by a test.
- **Explanation:** a plan's `Explain` lines are stable for equal registry and
  request snapshots.

## Adoption

1. Lock this planning owner. This documentation slice.
2. Implement family registration and exact selection planning for both
   request forms, with the trail, snapshot, and explanation of
   [Traceability](#traceability). `AnnotatedTypeMapPlanning.cs` in
   `DotnetInspector.Sections` implements it, gated by
   `AnnotatedTypeMapPlanningTests`; `AnnotatedTypeMapContribution` carries
   only its family until step 3 defines its settled content.
3. Implement contribution settlement and map generation, in a focused
   generation owner.
4. Register the API shape-fact family over `ApiSurfaceExtractor` for Type
   focus and Member-scope questions, and route Finding Diff Any's API census
   settlement through a family contribution request over the version pair.
   Settlement moves from extracting the selected Type to asking for the API
   shape facts of the Members that own uncertain differences, or of the whole
   Type when the Type itself owns one.
5. Register the Body shape-fact family over the map's Body fact per method
   from its adoption step 2, the fact-stream contribution over the method fact
   census of the map's adoption step 3, and the admitted Local-instruction
   finding families that read the same census. With both shape-fact families
   registered, map requests resolve, starting with the one-version map of
   #10217. Until this step, only family contribution requests resolve.
6. Route the Type-scoped API diff of #10221 through a family contribution
   request for the API shape-fact family at Type focus.

Host exposure follows the map's own adoption: CLI output in its step 6 and
Inspect Web Type Compare in its step 7 issue map requests through this
planner. Each step here lands its gates and, where it changes a production
path, exact NativeAOT before and after evidence.

## Non-claims

- This owner does not define any shape fact, finding, descriptor, key,
  equality, admission rule, listing rule, terminal, or map entry; the map and
  its consumed owners do.
- It does not plan Library scope. The map's Library scope arrives with its
  map diff in its own adoption step 5; Library-scope planning follows it.
- It does not define pair-scoped adapters. A pair resolves per-side
  result-kind contributions, and the map diff compares those rather than
  stopping a lockstep walk at its first difference; the map admits both routes
  to the same result. If the map's adoption steps 3 and 5 show that lockstep,
  which can census only listed Members, is cheaper, pair-scoped adapters are a
  follow-up to this owner.
- It does not execute producers, settle contributions, compare maps or
  contributions, or generate the document.
- It does not define all-applicable requests, presets, portable request
  spelling, CLI grammar, Browser controls, or rendering.
- It does not plan relations between Members or Types; those belong to
  [Annotated Type Relations](annotated-type-relations.md).
- It does not add a second dependency planner, acquisition layer, cache, or
  continuation.
- It does not retire `ApiSurfaceExtractor` or decide when each of its
  consumers moves to a narrower producer.
