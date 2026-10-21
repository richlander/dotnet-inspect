# Annotated Type Relations planning

## Status and owner

This document owns the host-neutral registry and exact-selection planning
substrate for [Annotated Type Relations](annotated-type-relations.md). It owns:

- immutable registration of relationship and color descriptors;
- binding each registered descriptor to terminal-specialized adapter queries;
- validation of exact Type-focus admission and semantic direction;
- resolution of one closed exact descriptor selection;
- deterministic projection of the existing typed queries needed by that
  selection; and
- preservation of QuerySpace and `InspectionQueryCatalog` as the owners of
  portable intent, dependency closure, cost, execution, and shared work.

Its exact claim is:

> For one exact Type focus, one immutable registry snapshot, one exact
> relationship and color selection, and one terminal, Annotated Type Relations
> planning resolves only exact registered descriptors admitted for that Type
> direction, binds each to its terminal-specialized adapter query, rejects
> unsupported or unavailable selections explicitly, and returns one immutable
> typed-query demand whose execution remains owned by the existing inspection
> query catalog.

This owner is the first half of infrastructure step 2 in
[#10283](https://github.com/richlander/dotnet-inspect/issues/10283). Document
generation is the second half. Splitting planning from generation keeps
terminal pushdown and producer execution with their existing owners rather
than making a document composer into another query engine.

## Consumed contracts

| Owner | Contract consumed |
| --- | --- |
| [Annotated Type Relations](annotated-type-relations.md) | Exact Type focus, relationship and color descriptor selection, semantic direction, and Rows/Count/Exists terminals |
| [Inspection graph document](inspection-graph-document.md) | Relationship identity, Type seed admission, semantic endpoint role, and characteristic descriptors used as color |
| [Subject Relations](subject-relations-workflows.md) | Canonical one-hop relation families and focus-relative direction |
| [Query Space Composition](query-space-composition.md) | Closed host-neutral query spaces and terminal-specialized requests |
| [QuerySpace library boundary](query-space-library.md) | Portable and structural query contracts |
| Typed inspection query catalog | Exact query-definition identity, prerequisite closure, cost, execution order, shared work, and result access |

Planning consumes these contracts without redefining their descriptor,
population, query, or execution semantics.

## Registration

One relationship registration binds:

- one exact `InspectionGraphRelationshipDescriptor`;
- its canonical `SubjectRelationForm`; and
- zero or one adapter query for each of Rows, Count, and Exists.

The relationship descriptor remains the semantic identity. Registration does
not introduce a second family id. Type-focus direction support is derived from
the descriptor's Type seed admissions: semantic source is outgoing and
semantic target is incoming. A descriptor with no Type admission cannot
register here.

One color registration binds one exact
`InspectionGraphCharacteristicDescriptor` to the same terminal-specialized
adapter-query shape. The characteristic descriptor remains the color identity.

An adapter query is an ordinary typed `InspectionQueryDefinition`. Its owner
adapts producer-native results to the contribution expected by the later
document generator. Required and optional dependencies remain registered in
`InspectionQueryCatalog`; this registry neither repeats nor executes that
dependency graph.

Registration rejects:

- repeated descriptor instances;
- distinct lookalike descriptors with the same id in one descriptor domain;
- relationship descriptors without Type seed admission; and
- registrations with no supported terminal.

The relationship and color id domains remain separate. Equal text across those
domains does not merge topology and color.

## Exact request and resolution

One planning request carries:

- one exact Type `SubjectRelationFocusAuthority`;
- one non-empty exact relationship-descriptor set;
- incoming, outgoing, or both focus incidence;
- zero or more selected color descriptors;
- a subset of selected colors required by later admission; and
- exactly one Rows, Count, or Exists terminal.

This slice accepts only exact descriptor selections. `AllApplicable`, authored
presets, portable replay, authorization, and host gestures remain later request
UX. Keeping discovery separate prevents an exact request from silently
expanding as the registry changes.

Resolution uses descriptor instance identity, not id text. A lookalike
descriptor carrying a registered id is unknown. Registry ids remain useful for
diagnostics and later portable correspondence, never process-local binding.

For each selected relationship, resolution verifies that its Type seed
admissions support at least one requested incidence direction. `Both` admits a
family supporting either direction and preserves the descriptor's actual
direction support; it does not claim that every family supports both.

For every selected relationship and color, resolution requires a registered
adapter query for the requested terminal and requires that exact query
definition to exist in the supplied inspection query catalog. Failure is typed
and identifies the affected descriptor. Resolution never substitutes another
terminal or a same-named query.

## Plan and execution boundary

The accepted immutable plan retains:

- the exact focus, terminal, and requested direction;
- selected relationship and color registrations in registry order;
- the required-color subset;
- each descriptor's exact terminal adapter query;
- a distinct typed-query demand in deterministic order; and
- the maximum catalog-issued cost over that demand.

Repeated query definitions collapse only in the demand projection. This lets
several descriptors share one adapter or prerequisite execution while every
descriptor remains independently selected.

The plan contains no execution delegate. A consumer passes its query demand to
the existing typed inspection query catalog, which owns dependency expansion,
execution, sharing, cancellation, and result access. The later document
generator consumes the resulting canonical contributions and cannot reacquire
or rerun producer work.

## Failure contract

Resolution returns one typed rejection for:

- an unknown relationship descriptor;
- an unknown color descriptor;
- unsupported Type-focus direction;
- an unsupported terminal; or
- an adapter query absent from the supplied inspection query catalog.

Invalid construction such as a non-Type focus, duplicate request descriptors,
or a required color not present in the selected color set fails at the request
boundary. Registration defects fail while forming the immutable catalog. None
becomes an empty successful plan.

## Gates

- **Exact identity:** a lookalike relationship or color descriptor with the
  same id cannot resolve.
- **Registration uniqueness:** duplicate instances and duplicate ids fail
  before a catalog is published.
- **Type admission:** incoming and outgoing resolution follow descriptor-owned
  Type seed roles; `Both` does not invent missing direction support.
- **Terminal specialization:** Rows, Count, and Exists select only their exact
  adapter query; an unsupported terminal fails.
- **Catalog authority:** an unregistered adapter query fails instead of
  executing through an alternate path.
- **Shared demand:** descriptors bound to the same exact query produce one
  typed-query demand while remaining separate selections.
- **Color admission:** every required color is selected, and color selection
  does not alter relationship identity.
- **Determinism:** equal registry and request snapshots produce equal ordered
  descriptor and query demands.

## Real assets and production path

The first production adopter remains outgoing calls to trimming and
NativeAOT-annotated APIs in `System.Text.Json` 10.0.13 and `Newtonsoft.Json`
13.0.4. Their call and annotation producers will register terminal adapters
against this substrate after document generation lands.

The complete feature now has six independently mergeable steps:

1. lock the Annotated Type Relations document shape;
2. implement exact descriptor registration and selection planning;
3. implement contribution settlement and document generation;
4. adopt outgoing calls to trimming and NativeAOT-annotated callees;
5. expose all-applicable and selected-family requests in the CLI; and
6. expose the same host-neutral request and document in Inspect Web.

## Non-claims

- This owner does not define relationship or color semantics.
- It does not execute producers, adapt producer-native evidence, settle family
  outcomes, filter Rows by color, or generate the result document.
- It does not define `AllApplicable`, presets, portable request spelling, CLI
  grammar, Browser controls, rendering, or authorization.
- It does not add a second dependency planner, acquisition layer, cache,
  continuation, or relationship engine.
- It does not require one query per descriptor; exact query identity may be
  shared where an adapter owner deliberately does so.
