# Annotated Type Map planning

## Status and owner

This document owns the host-neutral registry and exact-selection planning
substrate for the [Annotated Type Map](annotated-type-map.md). It owns:

- immutable registration of the families a map can carry: shape-fact families
  and admitted finding families;
- binding each registered family to terminal-specialized concrete
  `InspectionQuery<AnnotatedTypeMapContribution>` adapter queries;
- the map's planning focus: one exact Type, or a set of that Type's Member
  keys, at one version or over a version pair;
- resolution of one closed exact family selection;
- deterministic projection of the typed queries that selection needs; and
- the rule that a producer computes only the families a plan selects.

Its exact claim is:

> For one exact Type focus, an optional set of its Member keys, one or two
> versions, one immutable registry snapshot, one exact family selection, and
> one terminal, Annotated Type Map planning resolves only exact registered
> families admitted for that focus, binds each to its terminal-specialized
> adapter query, rejects unsupported or unavailable selections explicitly, and
> returns one immutable typed-query demand whose execution remains owned by
> the inspection query catalog.

This owner mirrors
[Annotated Type Relations planning](annotated-type-relations-planning.md),
which plans the relations reaching outside a Type; this one plans the findings
inside it. The two stay separate instances until both run in production. A
shared registration and planning substrate is factored from two working
instances, not designed ahead of the second one.

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
  sides. On CoreLib 10 to 11, under NativeAOT, the memory-safety index was
  about 90% of that extraction time.

Both consumers need only the API shape facts of a few Members. A plan that
names that family, at that scope, lets the producer skip the rest by
construction, rather than by a flag added to one entry point.

## Consumed contracts

| Owner | Contract consumed |
| --- | --- |
| [Annotated Type Map](annotated-type-map.md) | Map entries, shape facts, admitted findings, Member keys, scopes, terminals, and the map diff |
| [Finding Diff Any](finding-diff-any.md) | Member identity and ownership, the census key, and the API and Body equality |
| [Query Space producer capabilities](query-space-producer-capabilities.md) | Requirements, provisions, owner-certified covering, and resource-free plan selection |
| [Query Space composition](query-space-composition.md) | Closed host-neutral query spaces and terminal-specialized requests |
| [Finding nomenclature](finding-nomenclature.md) and [Annotated Source Finding provenance](annotated-source-finding-provenance.md#classification-contract) | Finding descriptors and their classifications |
| Typed inspection query catalog | Exact query identity, prerequisite closure, cost, execution order, shared work, and result access |

Planning consumes these contracts without redefining a fact, finding,
descriptor, key, equality, or execution rule.

## Families

A **family** is the unit a request selects and a producer contributes. There
are two kinds:

- a **shape-fact family**: one of the map's shape facts at Member grain, the
  API facts or the Body facts of [the map](annotated-type-map.md#the-map); and
- a **finding family**: one finding descriptor family the map admits, under
  [Admitted findings](annotated-type-map.md#admitted-findings).

The map owns which families exist and which are admitted. This document adds
none. A fact the map does not carry, such as a Member's memory-safety
contract or JSExport evidence, is not a family here; a producer that also
computes it does so outside any map plan.

## Registration

One family registration binds:

- the family's exact descriptor;
- the focus scopes it supports: whole Type, Member keys, or both; and
- zero or one adapter query for each of Rows, Count, and Exists.

The descriptor remains the family's identity; registration introduces no
second id. A registration supporting Member keys declares how the producer
selects Members by key, so the plan never asks it for Members it cannot bound.

An adapter query is an `InspectionQuery<AnnotatedTypeMapContribution>`. Its
required and optional dependencies stay registered in the inspection query
catalog; this registry neither repeats nor executes that dependency graph.
Image-scoped preparation, such as a metadata index, is a catalog prerequisite
of exactly the adapter queries that read it.

Registration rejects repeated descriptor instances, distinct lookalike
descriptors with one id, a family the map does not admit, and a registration
with no supported terminal or scope.

## Exact request and resolution

One planning request carries:

- one exact Type focus authority, and for Member scope, a non-empty set of
  that Type's Finding Diff Any Member keys;
- one image, or an ordered image pair for the map diff;
- one non-empty exact family set; and
- exactly one Rows, Count, or Exists terminal.

This slice accepts only exact family selections. All-applicable requests,
presets, and host gestures are later request UX.

Resolution uses descriptor instance identity, not id text. For each selected
family it requires a registration supporting the requested scope and a
registered adapter query for the requested terminal that exists in the
supplied catalog. A pair request resolves the same families on both sides;
the comparison between the two maps stays with the map diff. Failure is typed
and names the affected family. Resolution never substitutes another terminal,
widens Member scope to the whole Type, or picks a same-named query.

## Plan and execution boundary

The accepted immutable plan retains the exact focus, Member keys, versions,
terminal, the selected registrations in registry order, each family's exact
adapter query, a distinct typed-query demand in deterministic order, and the
maximum catalog-issued cost over that demand.

Repeated query definitions collapse only in the demand projection, so families
that share one adapter, or one prerequisite such as a per-image index, share
its execution while staying separately selected. Sharing is construction, not
coverage: it never lets one family's result satisfy another.

The plan contains no execution delegate. The consumer passes its demand to the
inspection query catalog, which owns dependency expansion, execution, sharing,
cancellation, and result access. A focused generation owner settles the
contributions into the map, as
[Annotated Type Relations generation](annotated-type-relations-generation.md)
does for relations.

## Producers compute only selected families

An adapter computes the families its plan selects, at the plan's scope, and no
other facts. A provider able to compute several families at once is
registered once per family it serves, and each registration's adapter
projects only that family, under an owner-certified covering relationship in
the [producer capabilities](query-space-producer-capabilities.md#covering-relationship)
sense.

`ApiSurfaceExtractor` is the first such provider. Registered for the API
shape-fact family, its adapter must project only the facts the complete API
comparison reads, at the requested Member keys: no memory-safety facts, no
JSExport evidence, and therefore no memory-safety index. A narrower producer,
such as one over `MetadataDeclarationSession` or the Finding Diff Any census,
may later register for the same family; the plan chooses between them from
the request and host-known inputs before opening an image, never from a
completed result.

## Failure contract

Resolution returns one typed rejection for an unknown family, a family the
map does not admit, an unsupported scope, an unsupported terminal, or an
adapter query absent from the supplied catalog. Invalid construction, such as
a non-Type focus, an empty Member key set, a Member key of another Type, or
duplicate families, fails at the request boundary. Registration defects fail
while forming the immutable catalog. None becomes an empty successful plan.

## Gates

- **Exact identity:** a lookalike family descriptor with a registered id
  cannot resolve.
- **Registration uniqueness:** duplicate instances and duplicate ids fail
  before a catalog is published.
- **Admission:** a family the map does not admit cannot register.
- **Scope:** a Member-key request resolves only families registered for Member
  scope and never widens to the whole Type.
- **Terminal specialization:** Rows, Count, and Exists select only their exact
  adapter query; an unsupported terminal fails.
- **Catalog authority:** an unregistered adapter query fails instead of
  executing through an alternate path.
- **Follow the T:** every adapter query has the concrete
  `AnnotatedTypeMapContribution` result type; general query infrastructure
  never understands it.
- **Selected work only:** for the API shape-fact family, the adapter's
  extraction builds no memory-safety index and projects no memory-safety or
  JSExport facts, and its result compares equal, under the complete API
  comparison, to the complete extraction of the same Members.
- **Shared demand:** families bound to one query, or one prerequisite,
  produce one typed-query demand while remaining separate selections.
- **Determinism:** equal registry and request snapshots produce equal ordered
  family and query demands.

## Adoption

1. Lock this planning owner. This documentation slice.
2. Implement family registration and exact selection planning.
3. Implement contribution settlement and map generation, in a focused
   generation owner.
4. Register the API shape-fact family over `ApiSurfaceExtractor` at Type and
   Member-key scope, and plan Finding Diff Any's
   [API census settlement](finding-diff-any.md#api-census-settlement) through
   it: a map diff request for the API shape-fact family at the Member keys
   that own uncertain differences. #10776 already projects that settlement
   with the facts the comparison reads; this step moves the same request onto
   the plan.
5. Register the admitted Local-instruction finding families for the
   one-version map of
   [#10217](https://github.com/richlander/dotnet-inspect/issues/10217).
6. Plan the Type-scoped API diff of
   [#10221](https://github.com/richlander/dotnet-inspect/issues/10221) as a
   map diff request at Type scope.

Each step lands its gates and, where it changes a production path, exact
NativeAOT before and after evidence.

## Non-claims

- This owner does not define any shape fact, finding, descriptor, key,
  equality, admission rule, or map entry; the map and its consumed owners do.
- It does not execute producers, settle contributions, compare maps, or
  generate the document.
- It does not define all-applicable requests, presets, portable request
  spelling, CLI grammar, Browser controls, or rendering.
- It does not plan relations between Members or Types; those belong to
  [Annotated Type Relations](annotated-type-relations.md).
- It does not add a second dependency planner, acquisition layer, cache, or
  continuation.
- It does not retire `ApiSurfaceExtractor` or decide when each of its
  consumers moves to a narrower producer.
