# Annotated Type Map planning

## Status and owner

This document owns the host-neutral registry and exact-selection planning
substrate for the [Annotated Type Map](annotated-type-map.md). It owns:

- immutable registration of the families a map can carry: its two shape-fact
  families and its admitted finding families;
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
> families admitted for that focus, binds each to its adapter query, rejects
> unsupported or unavailable selections explicitly, and returns one immutable
> typed-query demand whose execution remains owned by the inspection query
> catalog.

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

A **family** is the unit a producer contributes. There are two kinds:

- the two **shape-fact families**: the map's API facts and its Body facts at
  Member grain, as [the map](annotated-type-map.md#the-map) defines them; and
- a **finding family**: one finding descriptor family the map admits, under
  [Admitted findings](annotated-type-map.md#admitted-findings).

The map owns which families exist and which are admitted. This document adds
none. A fact the map does not carry, such as a Member's memory-safety
contract or JSExport evidence, is not a family here; a producer that computes
it does so outside any plan of this owner.

## Request forms

### Map request

A map request asks for a map at one version, or a map diff over a version
pair, with a Rows, Count, or Exists terminal. Shape facts are not selectable
in a map request: every map carries both shape-fact families, because the
map's listing, its terminals, and its Agreement gate read the shape comparison
across both axes. Resolution adds both shape-fact families to every map
request. The request selects only finding families, as the map's request names
its analyses.

### Family contribution request

A family contribution request asks for the selected families'
contributions, at one version or over a pair, and nothing else. It may select
any non-empty set of registered families, including a single shape-fact
family. Its result is the contributions themselves, keyed by Member key, each
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

A Member key embeds its declaring Type, so a key of another Type fails at the
request boundary from the key alone, without opening an image. For a pair,
keys are Finding Diff Any's side-independent keys.

## Registration

One family registration binds:

- the family's exact descriptor;
- whether it supports Type focus, Member-scope questions, or both; and
- its adapter queries: one per supported map terminal, and one for family
  contribution requests.

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

Resolution uses descriptor instance identity, not id text. For each family it
requires a registration supporting the requested focus and the adapter query
for the requested form and terminal, present in the supplied catalog. A pair
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
map does not admit, a shape-fact family selected in a map request, an
unsupported focus, an unsupported form or terminal, or an adapter query
absent from the supplied catalog. Invalid construction, such as a non-Type
focus, an empty Member key set, a key of another Type, or duplicate families,
fails at the request boundary. Registration defects fail while forming the
immutable catalog. None becomes an empty successful plan.

## Gates

- **Exact identity:** a lookalike family descriptor with a registered id
  cannot resolve.
- **Registration uniqueness:** duplicate instances and duplicate ids fail
  before a catalog is published.
- **Admission:** a family the map does not admit cannot register.
- **Complete shape:** every resolved map request includes both shape-fact
  families, and a map request that selects one is rejected.
- **Member questions:** a Member key set resolves only families supporting
  Member-scope questions, keeps one result per key, and never widens to the
  whole Type.
- **Form and terminal specialization:** each request selects only its exact
  adapter query; an unsupported form or terminal fails.
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

## Adoption

1. Lock this planning owner. This documentation slice.
2. Implement family registration and exact selection planning for both
   request forms.
3. Implement contribution settlement and map generation, in a focused
   generation owner.
4. Register the API shape-fact family over `ApiSurfaceExtractor` for Type
   focus and Member-scope questions, and route Finding Diff Any's API census
   settlement through a family contribution request over the version pair.
   Settlement moves from extracting the selected Type to asking for the API
   shape facts of the Members that own uncertain differences, or of the whole
   Type when the Type itself owns one.
5. Register the admitted Local-instruction finding families for the
   one-version map of #10217.
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
- It does not plan Library scope. The map defers its Library-scope producer to
  its own adoption step 4; Library-scope planning follows that producer.
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
