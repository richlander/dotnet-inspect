# Annotated Type Relations

## Status and owner

This document owns **Annotated Type Relations**: the host-neutral document that
describes how one exact Type relates to subjects outside its owned closure. It
owns:

- the document's focus, subjects, logical relations, occurrences, annotations
  used as relation color, family outcomes, and focus joins;
- preservation of producer-owned logical-relation identity and the separation
  of semantic direction from focus-relative incidence;
- the boundary between topology, supporting occurrences, and independently
  selected color;
- the Type-focused relation inventory that the shape must admit;
- Rows, Count, and Exists over the retained logical relations; and
- equivalence of table and graph projections over one document.

Its exact claim is:

> For one exact Type and one resolved set of relationship and color
> descriptors,
> Annotated Type Relations retains each admitted logical relationship incident
> to the Type's owned closure and reaching outside it, its semantic direction,
> its focus-relative incidence, its producer-native occurrences, annotations
> targeting retained subjects, relations, or occurrences, and the
> independently complete family outcomes needed to interpret both uncolored
> and color-filtered absence.

This owner is tracked by
[#10283](https://github.com/richlander/dotnet-inspect/issues/10283). It is the
relationship-side counterpart to [Annotated Type Map](annotated-type-map.md):
the Map looks within one Type; Relations looks outside it.

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Subject Relations](subject-relations-workflows.md) | Canonical one-hop logical rows, exact focus and candidate-population binding, producer outcomes, focus-relative direction, and relation forms |
| [Inspection graph document](inspection-graph-document.md) | Producer-owned relationship descriptors, exact semantic endpoints, occurrences, characteristics, derivation, endpoint projection, and semantic direction |
| [Annotated Type Map](annotated-type-map.md) and [Fast Diff](fast-diff.md) | Type, Member, accessor, and generated-body ownership |
| [Scope symmetry](scope-symmetry.md) | Library, Type, and Member targets; Rows, Count, and exact Exists; no rows within rows |
| [Finding nomenclature](finding-nomenclature.md) and [Finding coordinates](finding-coordinates.md) | Producer-owned findings, descriptors, subjects, evidence, and outcomes used as annotations |
| [Memory-safety models](memory-safety-models.md) | Independent propagation, unsafe-use, and safe-boundary vocabulary and evidence |
| [Call-graph characteristics](call-graph-characteristics.md) | Ordinary call topology and the separate Analysis-owned async-sibling opportunity relationship |
| [Integrations](integrations.md) | Owner-issued Integration concepts and actionable ecosystem currency |
| [Inspection envelope](inspection-envelope.md) | Completed host boundary carrying Content, Share, and diagnostics |

The query and generation infrastructure, producer registrations, and request
UX for all applicable or selected relations are focused successors. This
document constrains their output; it does not own their planning or gestures.

## Why

Annotated Type Map answers what is inside a Type: its owned Members, their
shape, and findings about their own instructions. It deliberately excludes
callee facts, call relationships, and effects attributable to another Type.

Those exclusions are useful relationships, not missing local annotations. A
reader looking at one Type may ask:

- which external Members its Members call, and which Members call them;
- which Types receive its extension surface or occupy its generic slots;
- which Types derive from it, which Types it derives from, and which interfaces
  it implements;
- which exception Types its Members throw;
- which repository naming families it satisfies; and
- where it refers to ecosystem anchor Types such as `IChatClient`.

These questions do not all form a call graph. Some are Type topology, some are
Member-to-Type evidence, some target owner-issued semantic subjects, and some
need an incoming candidate population. Their shared shape is an exact relation
incident to one Type, with evidence and honest completion.

The same topology can answer materially different questions when it carries
producer-owned color. An agent may ask which callees propagate an unsafe
obligation, which Members contain unsafe implementation while remaining safe
boundaries, which async paths reach blocking work with an available async
alternative, or which referenced Types are actionable currency for an
Ecosystem. Color explains why a retained relation matters without changing
what that relation is.

## Focus and owned closure

One document focuses one exact Type in one exact binding context. Its owned
closure contains:

- the Type;
- its Members under Annotated Type Map's accessor ownership; and
- physical implementation bodies attributed to those logical Members under
  the same generated-body ownership.

A descriptor admits the focus as:

- an exact semantic endpoint;
- an original occurrence endpoint retained behind a projected relation; or
- the strict typed owner of an admitted endpoint.

Those admission mechanisms remain distinct. Display names, namespaces, and
package labels never establish ownership or incidence.

A retained relation reaches outside the closure through at least one semantic
or original occurrence endpoint under its descriptor's admission rule. A
relation whose complete endpoint and occurrence closure stays inside the Type
may support another retained relation, but is not an external relation row.

## Document shape

The document has seven independent planes:

```text
AnnotatedTypeRelationsDocument
  Focus
  Selection
  Subjects[]
  Relations[]
  Occurrences[]
  Annotations[]
  FamilyOutcomes[]
  FocusJoins[]
```

### Focus and selection

`Focus` retains the exact Type and binding context. `Selection` records the
resolved relationship descriptors, incidence directions, color descriptors,
and any color predicate that determined which logical relations are listed.
The document does not infer selection from populated rows: a selected
relationship or color family with zero rows or an unavailable producer remains
selected and visible.

The shape does not decide how a request spells all applicable relationships,
an explicit subset, or an authored preset. A portable request preserves that
intent; the result records the exact resolved descriptor set.

### Subjects

Every semantic and original occurrence endpoint retains its owner-issued
subject identity and lifetime. Endpoint kinds may include Member, Type,
Library, Package, or an owner-issued semantic subject such as a naming family
or language-pattern role.

Equal display text never joins subjects. A portable document detaches a
subject only through its owner's portable projection; display text is not a
fallback identity.

### Logical relations

One canonical row is one logical relation:

```text
(semantic source, semantic target, relationship descriptor,
 optional producer discriminator)
```

The descriptor owns the discriminator and logical identity. Different
descriptors sharing endpoints remain different rows. Different exact
endpoints with the same display text remain different rows.

Relationship kind and semantic direction are mandatory topology. They are not
annotations and do not change when a caller asks for incoming incidence.

### Occurrences

An occurrence is one producer-native contribution to a logical relation. It
retains:

- the same relationship descriptor as its logical relation;
- its original source and target subjects;
- its producer-native coordinate and evidence;
- its descriptor-owned occurrence identity; and
- source occurrence receipts when it is derived.

Several occurrences may support one logical relation. They remain individually
addressable and never collapse merely because they render alike. A projected
Type relation may retain Member, metadata-row, or body-site occurrence
endpoints.

An observed or derived relation has at least one occurrence. A relation with
none is admitted only when its descriptor explicitly defines synthetic
construction and derivation.

### Annotations

An annotation is producer-owned **color** targeting one retained subject,
logical relation, or occurrence. It retains its descriptor, evidence, producer
identity, outcome, and derivation. Color descriptors are an open owner-issued
set, not one universal enum or Boolean signal.

Annotations never create, retarget, or change the identity of topology. For
example, a call to a `RequiresDynamicCode` Member remains a `call` relation;
the callee's finding explains why that relation is notable.

One annotation may target several retained hooks only when its owner defines
that targeting. The document does not duplicate the observation to make each
hook look independently authored.

Observed color retains its direct evidence. Derived or propagated color also
retains the source observations and relationship or occurrence receipts that
support the derivation. A producer defines its own transfer and absorption
rules; the document never copies color across every neighboring relation.

Color may decorate an otherwise admitted relation or participate in an
explicit admission predicate. Decoration does not change Rows. A color
predicate admits only relations with a matching annotation, records that
predicate in `Selection`, and composes relationship and color completion before
settling Count or Exists.

### Family outcomes

Each selected relationship or color descriptor retains its own:

- candidate population identity;
- supported targets and, for relationships, incidence directions;
- producer availability and completion;
- work limits and continuation where applicable;
- failures and diagnostics.

Relationship-family outcomes additionally retain Rows, Count, and Exists.
Color-family outcomes retain coverage of their declared target population. A
color-filtered empty answer is complete only when both the relationship
population and every color family used by the predicate settle absence.

A complete neighboring family never hides or upgrades an incomplete one. A
relationship family with no admitted rows is a complete negative only when its
own population and evidence are complete for the selected direction. A color
family with no annotations is a complete negative only when its declared target
population and evidence are complete.

### Focus joins

Every row records how it is incident to the focused closure: semantic source,
semantic target, original occurrence source, original occurrence target, or
strict owned endpoint. Its focus-relative reading is `Incoming`, `Outgoing`,
or both for a self-incident relation.

Incidence never reverses semantic endpoints. A subclass-to-base relation stays
`subclass -> base` when viewed as incoming from the base.

## Relation color inventory

The inventory below constrains composition without moving producer semantics
into this owner.

### Memory-safety color

[Memory-safety models](memory-safety-models.md) defines unsafe implementation
and unsafe propagation as independent dimensions:

- **unsafe implementation** is positive local evidence that a Member
  established an unsafe context in its declaration or implementation; and
- **unsafe propagation** is the caller contract that using a Member requires
  its caller to establish an unsafe context.

A Member may have neither color, either color independently, or both. In
particular, a safe boundary has unsafe implementation evidence without unsafe
propagation, while a bodyless Member may propagate unsafe. Neither color may be
derived from the other.

Path or graph projections retain those colors as separate annotations. A
separate path color involving a propagator retains the exact call occurrences
and producer-owned transfer or absorption evidence; it does not manufacture a
propagation contract on intermediate Members. Merely reaching an unsafe
implementation does not prove propagation.

### Async-to-blocking color

An ordinary synchronous call from an async source remains a `call` relation.
The existing `analysis.async-sibling-opportunity` remains a separate derived
relationship from the authenticated async source to the callable async
alternative. It does not become a call annotation or replace the synchronous
call.

A color producer may separately identify that an async Member directly or
transitively reaches owner-classified blocking work. Direct and transitive
claims are distinct. A transitive color retains the complete admitted call-path
receipts; an incomplete path cannot settle absence.

Presentation may color the synchronous call to show that an async alternative
exists only by deriving from the exact async-sibling opportunity and its cited
call occurrence. The color is an affordance over two retained relationships,
not a substitute for either one.

### Ecosystem currency color

An exact CLR reference remains its own relation. The Integration owner may
color its subject, relation, or occurrence with the exact Ecosystem and concept
identities that classify the referenced Type or Member as actionable
Integration currency.

Here, currency means an API or Type that a user can search, call, configure, or
wire into an application. It is not a wall-clock freshness judgment. The color
retains the Integration scanner's owner-issued evidence and population
identity; package membership or an assembly reference alone does not establish
it.

## Type-focused relation inventory

The inventory shapes the document but is not a closed universal enum. A family
enters a result only through an owner-issued descriptor, endpoint and
occurrence contracts, Type-focus admission, and family outcome.

| View | Canonical relation | Focus reading and evidence boundary |
| --- | --- | --- |
| Callees | owned Member calls external Member | Outgoing; exact call edge and physical sites; unresolved or indirect targets qualify absence |
| Callers | external Member calls owned Member | Incoming; requires an explicit caller population and its completion |
| Extension receivers | extension Member extends receiver Type | Incoming to the receiver, outgoing from its owning provider; exact extension declaration |
| Generic receivers | generic Type receives an exact Type in one parameter slot | Outgoing from the receiver, incoming for the received Type; exact constructed occurrence and slot position |
| Subclasses | derived Type inherits base Type | Incoming to the base; direct declaration or an owner-issued hierarchy path |
| Superclasses | derived Type inherits base Type | Outgoing from the derived Type; direct and transitive projections remain distinguishable |
| Implemented interfaces | implementing Type implements interface Type | Type topology; direct declaration and any owner-issued inherited path remain distinguishable |
| Thrown Types | throwing Member throws exact exception Type | Outgoing from the throwing Type or incoming to the exception; local throw evidence does not imply escape |
| Naming schemes | Type conforms to an owner-issued repository family or scheme | Derived classification retaining matched rules, coverage, and exact scheme identity |
| Ecosystem Type references | Type or owned Member references an exact ecosystem anchor Type | Exact declaration or body occurrences plus a separate owner-issued ecosystem/concept association |

The broader product relation dialect also includes signature uses, object
construction, metadata references and constraints, package dependencies and
ownership, Integration observations and compositions, opportunities,
structural matches, and language-pattern candidates. Their endpoint diversity,
occurrence forms, and completion semantics are requirements on this shape even
before each family has a Type-focused adopter.

### Type topology

Base classes, subclasses, and implemented interfaces are Type topology.
Directness and transitivity are independent of focus direction. A superclass
chain, indirect subclass match, or inherited interface retains the direct
declarations or owner-issued path that supports its projected row; it never
becomes an indistinguishable one-hop edge.

### Receiver relations

Receiver is broader than extension syntax:

- an extension receiver receives an added method, property, or other
  owner-issued extension-member surface through its declaration's receiver
  site; and
- a generic receiver receives an exact Type argument through a declared
  generic parameter slot.

The forms share a Receiver view and typed-slot concept, not a producer or
evidence codec. For example:

```text
Finding<AllocationFact>
  -> Finding<> --receives parameter position 0--> AllocationFact
```

The open declaration `Finding<T>` proves the slot and constraints, not that a
concrete Type was received. Parameter name `T` is presentation; generic
definition identity and zero-based position identify the slot. A complete
negative requires a declared population of constructed occurrences.

### Thrown-Type relations

One local throw occurrence establishes:

```text
throwing Member -> exact exception Type
```

The Type view rolls up owned Members without losing the Member or physical
throw site. A local throw may be caught and does not prove escape. Propagated,
task-fault, runtime, documented, unknown, and untyped rethrow cases need their
own producers and evidence claims.

### Semantic classifications

A naming relation targets an owner-issued nominal family or repository scheme;
it is not a CLR edge inferred from a suffix. An ecosystem reference retains an
exact CLR reference and separately attaches an owner-issued ecosystem or
concept association. Package membership alone establishes neither relation.

## Rows and terminals

Rows is the deterministic projection of admitted logical relations. It never
flattens occurrence evidence into duplicate relation rows or fabricates a
logical endpoint for unresolved evidence. A producer may retain that evidence
and its reason in the family outcome; it qualifies absence without becoming a
successful row.

The document adopts [Scope symmetry](scope-symmetry.md):

| Scope | Rows |
| --- | --- |
| Library | one row per Type with at least one admitted relation or an unsettled selected-family outcome, carrying relation Count and outcome rather than nested relation rows |
| Type | the focused Type's admitted external logical relations |
| Member | the admitted external logical relations incident to that exact owned Member |

Count equals the admitted Rows and carries an unsettled outcome when additional
rows may exist. Exists is `true` when an admitted row exists, undecided when
none exists but any selected relationship or predicate color family cannot
settle absence, and `false` only when every family participating in admission
is complete and no row exists.

A Member's row at Member scope equals that row in its Type document. A
Library-scope ephemeral result never nests each Type's relation Rows.

## Projection

The keyed document is canonical. Table and graph are projections over the same
subjects, logical relations, occurrences, annotations, and outcomes.

A graph projection may roll exact endpoints up to Types or Packages only
through descriptor-owned endpoint projection while retaining original
occurrence endpoints. A table may expand occurrences beneath one logical row.
Neither projection changes relation identity, semantic direction, focus
incidence, or completion.

Presentation may omit a repeated relation title or unselected annotation. It
must not hide the selection or unsettled outcome of a color used for admission,
or parse labels to recover identity or semantics.

## First adopter

The first focused adopter is outgoing calls from a selected Type to external
Members annotated with `RequiresUnreferencedCode`, `RequiresDynamicCode`, or
`RequiresAssemblyFiles`.

It uses the general shape as follows:

- `call` is the relationship descriptor;
- owned caller and external callee Members are semantic endpoints;
- physical call sites are occurrences;
- trimming and NativeAOT findings target the callee;
- Type, Library, Package, and Platform boundaries are characteristics derived
  through owner-issued correspondence; and
- call resolution and callee-color completion remain separate outcomes.

This adopter must not introduce a call-specific row, occurrence, annotation,
or completion shape.

Real assets are `System.Text.Json` 10.0.13 and `Newtonsoft.Json` 13.0.4. Both
carry substantial trimming and dynamic-code annotations; a consumer Type
calling those APIs demonstrates same-Library, dependency-package, and Platform
resolution. The adopter records its exact measured scenarios and durable
fixtures in its focused design.

## Gates

- **Identity:** unequal exact endpoints or descriptors never join because
  their display text matches; equal endpoints under different descriptors
  remain different rows.
- **Direction:** incoming traversal preserves producer semantic direction and
  records focus-relative incidence separately.
- **Occurrences:** several native occurrences support one row without losing
  coordinates; duplicate descriptor-owned occurrence identity is rejected.
- **Projection:** a rolled-up Type relation retains every original Member or
  metadata occurrence endpoint.
- **Annotations:** annotations on a subject, relation, and occurrence do not
  change topology or each other's identity.
- **Color independence:** unsafe implementation and unsafe propagation exercise
  all four present/absent combinations without either color implying the other.
- **Color derivation:** propagated color retains its source observations,
  relation receipts, and producer-owned transfer or absorption result.
- **Colored selection:** an empty color-filtered result is false only when both
  relationship and predicate-color populations are complete; otherwise Exists
  is undecided.
- **Async separation:** the synchronous call, async-sibling opportunity, and
  optional async-to-blocking color retain distinct identities and evidence.
- **Ecosystem currency:** Integration color retains exact Ecosystem and concept
  identity and cannot be inferred from package or assembly membership.
- **Derivation:** observed, derived, and synthetic relations remain distinct;
  a derived relation retains its source occurrence receipts.
- **Isolation:** failure or incompleteness in one selected family neither
  erases rows nor strengthens outcomes from another.
- **Selection:** the document records every selected relationship and color
  descriptor, including complete-empty, unavailable, and failed families.
- **Exact terminals:** Count equals Rows and Exists follows Rows plus selected
  family outcomes at Library, Type, and Member scope.
- **Symmetry:** a Member's relation answer is identical at Member scope and
  inside its Type answer.
- **Projection equivalence:** table and graph projections contain the same
  logical relations and retain the same occurrence and completion evidence.

## Adoption

The complete feature has six independently mergeable steps:

1. lock this document shape;
2. implement exact descriptor registration and selection planning under
   [Annotated Type Relations planning](annotated-type-relations-planning.md);
3. implement contribution settlement and document generation over
   owner-issued relation and color outcomes;
4. adopt outgoing calls to trimming and NativeAOT-annotated callees;
5. expose all-applicable and selected-family requests in the CLI; and
6. expose the same host-neutral requests and document in Inspect Web.

Each additional relation family adopts the locked shape through its own owner.
Existing Type Relationships and Subject Relations paths retire or compose only
after their focused successor demonstrates parity.

## Non-claims

- This owner does not define a universal relationship enum, graph database, or
  producer.
- It does not define a universal color enum or reinterpret every producer
  observation as one severity scale.
- It does not define relationship discovery, producer planning, acquisition,
  caching, or request spelling.
- It does not redefine Subject Relations rows, Inspection Graph descriptors,
  producer evidence, Findings, or Type and Member ownership.
- It does not redefine memory-safety roles, async-sibling opportunity
  selection, blocking classification, or Integration currency.
- It does not make annotations part of logical-relation identity.
- It does not claim whole-Workspace incoming completeness without an explicit
  population and receipt.
- It does not reproduce ILLink or ILC dataflow analysis or predict a publish's
  complete warning set.
- It does not define relation diff. A future diff compares two one-version
  documents without changing this document's identity or evidence.
