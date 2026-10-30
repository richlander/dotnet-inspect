# QuerySpace library boundary

## Status

Focused design for
[#7976](https://github.com/richlander/dotnet-inspect/issues/7976).
It establishes the library boundary under which portable query, row planning,
semantic selection, Query Operation, and Query Space composition contracts
move from `DotnetInspector.QueryEngine`.

The `QuerySpace` project now carries portable intent and payload contracts,
row-query and semantic-selection contracts, Query Operation registration,
immutable Query Space descriptor and request contracts, executable typed row
scope bindings, and named multi-sequence row-query execution.
`DotnetInspector.QueryEngine` is retired. Section-owned Count outcomes now live
in `DotnetInspector.Sections`, which composes one explicit structural row
association into a complete-source Rows or Count request.

The existing portable-query, row-query, row-selection, Query Operation,
Sections, and direct-consumer Release gates verify this physical boundary and
the initial executable composition structure. Complete section-row resolution,
source generation, System.Text.Json integration, and broader product adoption
remain separate focused slices under the
[adoption sequence](#adoption-sequence). Later properties in
[Required evidence](#required-evidence) remain **unverified** until their named
adoption lands.

[#9250](https://github.com/richlander/dotnet-inspect/issues/9250) split the
library into two assemblies along its
[lifetime classes](#six-lifetime-classes) and named the
[two participation tiers](#two-assemblies-and-two-participation-tiers) that
decide which assembly a component references. The assembly split landed in
[#9285](https://github.com/richlander/dotnet-inspect/pull/9285), and Product
Vocabulary became its first tier-1 declaration owner in
[#9294](https://github.com/richlander/dotnet-inspect/pull/9294).

[#9341](https://github.com/richlander/dotnet-inspect/issues/9341) extends that
floor with the general explanation declaration and detached-data currencies
defined by [Resource Explanation](resource-explanation.md). The extension adds
one lifetime class that the original query-focused split did not need to name:
immutable detached typed data that is neither a reusable declaration nor an
execution result.

## Owner and exact claim

**QuerySpace Library Boundary** owns this exact claim:

> `QuerySpace` is one host-neutral .NET library, delivered as a
> dependency-free contract floor (`QuerySpace.Primitives`) and a planning
> and execution assembly (`QuerySpace`) that references it, whose public
> boundary composes the existing owner-issued portable query, row planning,
> semantic selection, operation registration, Query Space structural, and
> value-vocabulary and explanation declaration and detached-data contracts for
> reuse by dotnet-inspect, Inspect Web, and independent .NET applications. It
> owns their physical and API composition, dependency boundary, lifetime
> boundary, and extension seams without taking semantic ownership from their
> focused designs.

This owner defines:

- the `QuerySpace` package, its two assemblies, root namespace, and focused
  child namespaces;
- the assignment of each lifetime class to one of the two assemblies, and
  the two participation tiers that follow from it;
- which reusable contracts belong in that library and which product contracts
  remain outside it;
- the separation among reusable declarations, detached typed data, durable
  requests, reusable execution machinery, one-execution state, and execution
  results;
- the rule that an application's data types need not implement a QuerySpace
  interface;
- the common seam through which a reference interpreter, source delegation,
  or a specialized kernel may execute the same structural plan;
- the boundary for optional generated declarations and execution witnesses;
- the boundary for optional System.Text.Json correspondence; and
- migration and retirement of `DotnetInspector.QueryEngine`.

This owner does not define:

- portable query identity, canonical payload, compatibility, or resolution
  semantics;
- row predicate, ordering, `Head`, `Tail`, `Window`, or `Top` semantics;
- Query Operation registration or operation-plan semantics;
- Package, Library, Find, Depends, Graph, References, or section semantics;
- projection, exact Count, source delegation, completion, or continuation
  semantics;
- explanation schema, snapshot, catalog, path, traversal, or Document
  semantics;
- acquisition, paging, retry, caching, or host lifetime;
- CLI grammar, Browser interaction, envelopes, Markout, rendering, or output
  formats;
- a universal executable plan shared by operation owners; or
- the source-generator, System.Text.Json adapter, or optimized-kernel
  implementation owned by later focused work.

The focused owners linked throughout this document retain those decisions.
Moving their types changes physical composition, not semantic authority.

## Product and external-consumer goal

The current carrier is already dependency-free, but its
`DotnetInspector.QueryEngine` identity presents generic query infrastructure as
dotnet-inspect product machinery. The target library has a smaller and more
accurate identity:

```text
QuerySpace
```

It supports three concrete consumers:

1. dotnet-inspect CLI routes construct and execute owner-issued Package,
   Library, Find, Depends, Graph, and section-row plans;
2. Inspect Web and Browser/Wasm construct the same host-neutral requests and
   consume the same capability descriptors; and
3. an independent .NET application describes its own rows and operations,
   resolves a request, executes it without CLI or Browser dependencies, and
   performs any later shaping with LINQ or another application tool.

The first direct external-consumer fixture must use application-owned data
types that implement no QuerySpace interface. That fixture demonstrates the
independent-library claim rather than only proving that dotnet-inspect can
reference its own extracted assembly.

The held Package Query and Library Query work supplies the production adoption
path. `Microsoft.Extensions.*` package qualification and assembly-reference
rows provide real query shapes; they are not synthetic justification for a
general-purpose language.

## Conventional basis

The boundary deliberately combines familiar roles without copying one
precedent wholesale:

| Precedent | Adopted | Deliberate difference |
| --- | --- | --- |
| `IEnumerable<T>` and pattern-based `foreach` | Low participation burden and direct traversal when a concrete shape is known. | Data does not carry the complete QuerySpace machinery contract, and `IEnumerable<T>` is only an optional source adapter. |
| `IQueryable<T>` | Structural request and execution-provider separation. | No expression trees, opaque provider execution, arbitrary method-call graph, or deferred query object capturing a live source. |
| LINQ | Familiar filtering, ordering, and terminal roles. | Work bounds, semantic row selection, projection, and terminals remain distinct owner-issued operations. |
| [NLinq](https://github.com/agocke/NLinq/tree/229e2435fc10f8a50e0fecb5e5a57bb18832f415) | Typed source adapters, struct execution state, static dispatch, source-specific folds, and specialized terminals. | Runtime-authored plans remain structural data rather than nested generic pipeline types. |
| System.Text.Json source generation | A generated witness may connect an ordinary type to reusable machinery without reflection or a data-type interface. | A QuerySpace generator runs beside, not after, the STJ generator and cannot inspect or authenticate STJ's generated witness. |
| [ts-jsexport](ts-jsexport.md) | Post-compilation inspection can authenticate the exact source-generated STJ witness and effective direction as contract evidence. | QuerySpace source generation has no equivalent access to final generated metadata or IL. |

The design favors familiar roles over API mimicry. In particular, there is no
single `IQueryable<T>` equivalent because one object must not conflate source
data, capabilities, unresolved intent, resolved plans, execution lifetime, and
results.

## Library contents

The `QuerySpace` library carries reusable contracts from these existing
owners:

| Contract | Semantic owner |
| --- | --- |
| Portable intent, identities, vocabulary, atomic resolution, and canonical codec | [Portable query intent](portable-query-intent.md) and [Portable query payload](portable-query-payload.md) |
| Typed row intent, vocabulary, predicate and order resolution, structural row plans, and reference execution | [L2 row query and ordering](row-query-order.md) |
| Typed `Head`, `Tail`, `Window`, and `Top` plans and complete-sequence reference execution | [Semantic row selection](semantic-row-selection.md) |
| Generic operation definition, routes, profiles, effects, and effective capability projection | [Query Operation Infrastructure](query-operation-infrastructure.md) |
| Query-space descriptors, scopes, row-intent associations, terminal requirements, and structural composition | [Query Space Composition](query-space-composition.md) |
| Value-vocabulary declaration, term, map, and snapshot-identity contracts, and the typed value-vocabulary identity a facet names | [Vocabulary mappings](vocabulary-mappings.md) |
| General explanation identities, schemas, data-shape and resource-type declarations, schema-conforming values, observations, addresses, and detached resource snapshots | [Resource Explanation](resource-explanation.md) |

The library does not carry:

- product-owned vocabularies, operation plans, or result types;
- declared section schemas, section projection, envelopes, or
  `SectionCountOutcome`;
- source-selection, acquisition, delegation, completion, or continuation
  implementations;
- explanation catalogs, path registration, exact resolution, traversal,
  Documents, envelopes, format lowering, or host binding;
- host policy, command parsing, Browser controls, or presentation;
- lazy results that retain borrowed execution state; or
- arbitrary executable content supplied by a portable request.

`SectionCountOutcome` belongs to `DotnetInspector.Sections`. Its temporary
location in the current carrier does not make section-result semantics part of
QuerySpace.

## Namespaces and identity

The package name is `QuerySpace`. It ships two assemblies,
`QuerySpace.Primitives` and `QuerySpace`, whose contents are assigned by
[lifetime class](#two-assemblies-and-two-participation-tiers). Namespaces
follow reusable role, not assembly: a type keeps its namespace when it is
assigned to `Primitives`, so the split changes no consumer source. The public
namespaces are:

```text
QuerySpace
QuerySpace.Rows
QuerySpace.Operations
QuerySpace.Composition
QuerySpace.Vocabulary
QuerySpace.Explanation
```

Portable intent and shared structural identities use the root namespace.
Row-query and semantic-selection contracts use `QuerySpace.Rows`. Generic
operation registration uses `QuerySpace.Operations`. Multi-scope Query Space
descriptors and requests use `QuerySpace.Composition`. Value-vocabulary
declarations, terms, maps, and snapshot identity, whose semantics
[Vocabulary Mappings](vocabulary-mappings.md) owns, use
`QuerySpace.Vocabulary`; they live in `QuerySpace.Primitives`.
General explanation identities, declarations, values, observations, and
detached resource snapshots, whose semantics
[Resource Explanation](resource-explanation.md) owns, use
`QuerySpace.Explanation`; they also live in `QuerySpace.Primitives`.
The two namespaces share one value grammar: explanation vocabulary-term values
name `QuerySpace.Vocabulary` term identities, and vocabulary maps carry
`QuerySpace.Explanation` values under
[Vocabulary Mappings' value grammar](vocabulary-mappings.md#value-grammar).

The initial public center preserves established semantic type names rather than
renaming them only for symmetry:

| Public role | Target type family |
| --- | --- |
| Portable structural intent | `PortableQueryIntent`, its terms, identities, operators, bounds, stages, and orders |
| Portable declaration and resolution | `PortableQueryVocabulary<TPredicate, TPlan>` and its atomic resolution result |
| Typed row declaration | `RowQueryVocabulary<TRow>` and owner-issued key and order identities |
| Resolved row meaning | `ResolvedRowQueryPlan<TRow>` |
| Ordered semantic selection | `RowSelectionPlan<TOrder>` and `RowSelectionStage<TOrder>` |
| Generic operation registration | `QueryOperationDefinition<TPredicate, TPlan>`, routes, profiles, effects, and registry |
| Complete capability projection | `QuerySpaceDescriptor` |
| One composed structural request | `QuerySpaceRequest` |
| Explanation declaration and detached snapshot | `ExplanationSchema`, `ExplanationValue`, `ExplanationResourceKey`, and `ExplanationResourceSnapshot` families |

There is no public `IQueryable<T>` analogue and no universal executable
`QueryPlan<T>`. Query Space resolution retains the associations among
operation-owned plans, row plans, participating row sets, and the terminal
without replacing those plans with one erased executable type.

The migration changes CLR type identities. `DotnetInspector.QueryEngine` is
retired after all repository consumers move; it is not retained as a forwarding
assembly or compatibility namespace. The current carrier has no independently
published compatibility promise that justifies preserving two public
identities for the same contract.

## Six lifetime classes

The reusable boundary distinguishes six lifetime classes:

| Class | Contents | Lifetime |
| --- | --- | --- |
| Declaration | Facets, row vocabularies, operation registrations, query-space definitions, and descriptors | Reusable, immutable, resource-free |
| Detached typed data | Owner-issued schema-conforming values, observations, immutable catalog snapshots, and detached resource snapshots | Immutable and resource-free; complete when constructed; independent of evaluator lifetime |
| Portable request | Portable intents, row associations, selected row sets, and terminal requirement | Durable and serializable under the owning compatibility rules; resource-free |
| Resolved plan and binding | Normalized operands, order and selection stages, owner-issued structural plans, typed accessors, operand evaluators, and comparer machinery | Immutable and resource-free; reusable only under its owner-issued definitions and compatibility |
| Execution context | Bound source adapters, enumerators, buffers, cancellation, destinations, and leases | Owned or borrowed for one bounded execution |
| Execution result | Selected rows, Count or another owner-issued execution outcome, completion, diagnostics, and opaque continuation receipts | Detached from execution resources but issued by one execution contract |

A declaration, detached datum, portable request, resolved plan, or reusable
binding contains no live source, enumerator, network client, buffer lease,
cancellation source, lazy computation, or deferred source operation. A
detached datum and an execution result expose no borrowed buffer and perform no
hidden work when inspected or enumerated.

Detached typed data differs from an execution result by authority and
lifetime, not by mutability. A semantic owner may issue it while declaring a
product contract or after resolving a subject, without running a QuerySpace
evaluator and without carrying completion, continuation, or execution
diagnostics. `VocabularySnapshot` and `ExplanationResourceSnapshot` are
representative detached data. Rows, Count, completion, and continuation remain
execution results in the upper assembly.

An opaque source continuation may be retained beside a completed result under
its source owner's contract. It is a durable receipt, not a live QuerySpace
handle and not part of portable query identity.

## Two assemblies and two participation tiers

The six lifetime classes map onto two assemblies:

| Assembly | Lifetime classes | Contents | Dependencies |
| --- | --- | --- | --- |
| `QuerySpace.Primitives` | Declaration; Detached typed data; Portable request | Query-space, row-scope, and facet descriptors; row-vocabulary, resource, source-binding, request-association, and operation-route identities; value-vocabulary declarations, terms, maps, and immutable snapshots; explanation declarations, values, keys, observations, addresses, and detached snapshots; portable intents, requests, row associations, and terminal requirements; producer-capability identities and declarations; the `IQueryOperationRoute` contract and its delegate-free capability records | Platform only, as for every other contract floor; listed in `dependency-free-contract-floors` |
| `QuerySpace` | Resolved plan and binding; Execution context; Execution result; the portable payload codec; declarations that still carry binders | Planners, normalized operands, structural plans, typed accessors, bindings, reference execution, results, receipts, `PortableQueryPayloadCodec`, and the binder-bearing declarations named below | Platform and `QuerySpace.Primitives` |

`Primitives` contains no planner, evaluator, execution, or codec type, no
type that carries an accessor delegate, operand evaluator, or comparer
factory, and no type whose construction requires one of those. A type that
holds a live source, enumerator, buffer, cancellation source, deferred
enumeration, or `Lazy<T>` cannot be placed there. Constructors may accept
enumerable inputs only when construction eagerly validates and copies them
into detached storage. "Binding" is judged by content, not by name: a record
that only declares which terms and orders a route supports is a declaration
and belongs in `Primitives`; a type that evaluates, accesses, or compares rows
is a binding and stays in `QuerySpace`. The portable intent and payload
*types* are Portable-request data and belong to `Primitives`; the codec that
gives an intent its canonical byte spelling is machinery over that data and
stays in `QuerySpace`, where
[Portable query payload](portable-query-payload.md) already places it and
where `PortableQueryIdentity`, whose identity rule depends on it, also lives.
This follows
[`ILInspector.MetadataPrimitives`](../metadata-primitives.md): a
dependency-free floor of mechanical currencies beneath an assembly that owns
the semantics built on them.

Members that cross the assembly line as `internal` today (for example
`ResolvedRowQueryOrderIdentity`'s constructor, `QuerySpaceRowScopeDescriptor`'s
facet and order lookups, and the capability-record constructors used by
`QueryOperationRoute<,>`) use `InternalsVisibleTo` from `Primitives` to
`QuerySpace`, as `ILInspector.MetadataPrimitives` already does for
`ILInspector.Metadata`.

The lifetime classes name the rule; the current type inventory does not yet
obey it everywhere, because the plan/binding separation in adoption step 4 has
not landed for every declaration and `VocabularySnapshot` still retains lazy
identity computation. The assembly line is therefore drawn by rule *and* by
these explicit dispositions at the exact head:

| Type family | Assembly | Why |
| --- | --- | --- |
| `PortableQueryIdentity` | `QuerySpace` | Its only constructors call `PortableQueryPayloadCodec`; identity is vocabulary plus codec-confirmed canonical bytes. It moves down only if a codec-free construction is designed by the Portable query payload owner. |
| `RowQueryVocabulary<TRow>`, `RowQueryKey<TRow>`, `RowQueryNamedOrder<TRow>` | `QuerySpace` | They carry typed accessor delegates and comparer factories, which the lifetime table assigns to Resolved plan and binding. Their identities (`RowQueryVocabularyIdentity` and the key and order identities) go down. They move down only after adoption step 4 separates the structural declaration from its binding. |
| `PortableQueryVocabulary<TPredicate, TPlan>`, `PortableQueryKeyDeclaration<TPredicate>`, `PortableQueryBinding<TPredicate>`, `QueryOperationDefinition<TPredicate, TPlan>`, `QueryOperationRoute<TPredicate, TPlan>` | `QuerySpace` | They bind predicates and plans. `IQueryOperationRoute` and the route and operation identities go down. |
| Producer-capability satisfaction and plan candidates | `QuerySpace` | Resolved-plan class. Producer-capability identities, requirements, provision declarations, and coverage declarations go down. |
| `QueryOperationTermBinding`, `QueryOperationOrderBinding`, `QueryOperationTermCapability`, `QueryOperationOrderCapability`, `QueryOperationRouteCapabilities` | `QuerySpace.Primitives` | Delegate-free declarations of which terms and orders a route supports; `IQueryOperationRoute.Capabilities` requires them. The word "binding" in two of the names describes a declared term-to-operator association, not an accessor. |
| `PortableQueryModel` | `QuerySpace.Primitives` | Identity texts and the `ScalarOrder` comparer instance over strings that intents and the codec both consume; a fixed comparer over text is a datum, not a comparer factory over rows. |
| `PortableQueryRowSelection`, `QueryOperationRegistry` | `QuerySpace.Primitives` | Pure mappings and lists over `Primitives` types; no row access or evaluation. |
| `VocabularySnapshot` | `QuerySpace.Primitives` | Complete immutable detached typed data. Its construction and content identity are floor operations, but the retained `Lazy<T>` used by the current implementation must become eager before the strengthened content gate lands. |
| Explanation identities, declarations, value carriers, resource keys, public addresses, observations, targets, and `ExplanationResourceSnapshot` | `QuerySpace.Primitives` | Owner-issued Declaration or Detached-typed-data classes needed by semantic owners that cannot reference Resource Explanation machinery. |
| Explanation catalogs, path registrations, owner adapters, exact resolution, traversal, `ResourceExplanationDocument`, envelopes, serializers, and renderers | Outside `QuerySpace.Primitives` | Composition, operation-result, or host machinery not needed for an owner to publish its schema or detached snapshot. |
| `RowQueryText` | `QuerySpace` | Its `Key<TRow>` helpers take row accessors. |
| Everything else in the Declaration, Detached-typed-data, and Portable-request classes | `QuerySpace.Primitives` | Data and identities with no codec, binder, delegate, deferred work, or execution state. |

A type that moves from `QuerySpace` to `Primitives` in a later adoption does
so by its owner's design, which names the separation that made it eligible.
`PrimitivesCarriesOnlyFloorTypes` will enforce the rule over the assembly's
public surface and carried implementation types when adoption step 10 lands,
so a disposition cannot drift silently.

The split exists so that a component can choose how it participates:

- **Tier 1, typed data.** The component publishes declarations and detached
  typed data: stable identities, deterministic order, construction-time
  validity. It references `QuerySpace.Primitives` only. It gets discovery,
  explanation, and joins by identity, and it can never alter execution
  semantics because it cannot reach them. `ILInspector.Decompiler` is tier 1:
  its style, body-kind, and node-kind catalogs become value-vocabulary
  declarations beside the catalogs that own them.
- **Tier 2, optimized integration.** The component owns a source and needs
  execution control: unit enumeration, lanes and breadth, early stop, request
  collapse across consumers, completion evidence. It references `QuerySpace`
  and carries the bar [Source delegation](source-delegation.md) sets: a
  closed barrier, completion evidence rather than asserted satisfaction, no
  silent fallback, and an equivalence gate against the reference path.
  `ILInspector.Analysis` is tier 2 under
  [Assembly Analysis Operation](assembly-analysis-operation.md).

A tier-1 component's policy rule admits `QuerySpace.Primitives` and not
`QuerySpace`; a tier-2 component's rule admits both. The repository's L1
queries declare demand on a tier-2 component in `Primitives` types and
receive typed results; they do not construct the tier-2 component's planning
or binding types. Adopting that rule for existing Analysis demand belongs to
the Analysis programs, not to this document.

Tier-1 declarations are composed, not registered. A host composes the
declarations it ships into the snapshot or catalog that the declarations'
semantic owner defines, and no component owns the complete list of a
declaration kind. Adopters such as [Product Vocabulary](vocabulary.md#ownership)
cite this rule rather than restating it.

## Explanation declaration floor

[Resource Explanation](resource-explanation.md#primitive-placement-test)
defines the semantic object model and the test for types that lower-level
owners must publish. This library-boundary owner makes the physical-placement
decision: the general declaration and detached-snapshot currencies live under
`QuerySpace.Explanation` in `QuerySpace.Primitives`.

This placement does not make explanation a query feature. `QuerySpace` is the
existing general typed-data and execution library, and its primitive assembly
already supplies the dependency-free tier-1 contract shared by semantic
owners. Creating an `Inspector.Primitives` peer would split one schema and
value currency across two floors, require correspondence between their
identities, and leave every adopter to choose or reference both. The narrower
extension is one namespace in the established floor.

The namespace remains general by excluding domain types. Metadata,
decompilation, Analysis, Findings, Package, and Library keep their native
identities and semantics. They publish owner-issued named shapes and
schema-conforming values; types such as `MemberAnchor` do not move, gain a
QuerySpace interface, or become plain text merely to cross the boundary.

The floor contains these type families:

| Type family | Lifetime class | Placement reason |
| --- | --- | --- |
| Owner-domain, schema, schema-version, data-shape, resource-type, fact, relationship, field, choice-case, and public-address-kind identities | Declaration | Stable cross-owner currencies needed before composition |
| Scalar kind, shape kind, cardinality, observation state, snapshot scope, embedded-value budget, and declaration-node budget | Declaration | Common finite grammar and contract currencies |
| Scalar, vocabulary-term, named-record, closed-choice, and named-reference data-shape declarations, including ordered fields and cases | Declaration | Reusable schema grammar that semantic owners publish |
| Resource-type, fact, relationship, public-address-kind, and owner schema declarations | Declaration | Owner-issued explanation contract modules |
| `ExplanationValue` and its scalar, term, record, and choice carriers | Detached typed data | Schema-conforming product values independent of CLR domain types |
| `ExplanationResourceKey` and typed public-address values | Detached typed data | Heterogeneous graph identity and owner-issued navigation currency |
| Fact and relationship observations, relationship targets, and typed unavailable or failed outcome data | Detached typed data | Complete owner-issued state with no evaluator or host lifetime |
| `ExplanationResourceSnapshot` | Detached typed data | Installed or resolved request-scoped resource state published directly by a semantic owner |

The exact CLR design may use sealed unions, value types, and immutable arrays,
but it preserves those public families and their distinct identities. It does
not introduce a universal semantic base class, an untyped property dictionary,
reflection-based shape discovery, or a dependency on a concrete owner's CLR
types.

The floor also owns the intrinsic operations required to construct those
currencies correctly:

- eager validation and copying of declaration and snapshot inputs;
- schema-conformance checks over an explicitly supplied schema closure;
- deterministic structural equality and ordering for values and keys; and
- finite canonical value measurement needed to enforce declared byte, depth,
  node, and embedded-count budgets.

Those operations retain no catalog, execution context, serializer, or host
state. Canonical value measurement is an intrinsic operation over the typed
value grammar, not a public wire codec or a JSON lowering. Construction
rejects invalid or over-budget values; it does not silently truncate them or
defer validation until rendering.

Resource Explanation remains above the floor and owns:

- `ResourcePath`, canonical registrations, aliases, and exact resolution;
- owner modules and adapters;
- cross-owner schema-reference and relationship-join validation;
- installed catalog and contextual overlay composition;
- capability search, traversal request and execution, schema-slice selection,
  and traversal receipt;
- `ResourceExplanationDocument` and its
  `InspectionEnvelope<ResourceExplanationDocument>`; and
- source-generated serialization, Markout and other format lowering, Browser
  controls, and CLI gesture binding.

`ResourceExplanationDocument` is an operation result rather than owner-issued
detached data, even though its completed Content is immutable and
resource-free. Its traversal bounds, self-contained schema slice, and
projection completeness belong to Resource Explanation and therefore do not
move into the primitive floor.

The existing closed `ResourceExplanationOwner`,
`ResourceExplanationResourceKind`, `ResourceExplanationIdentity`,
`ResourceExplanationDetail`, and `ResourceExplanationRelationship` families
remain in `DotnetInspector.Sections` only until the installed Resource
Explanation adopter moves to the shared schema and snapshot currencies. They
are retired in that adoption rather than forwarded, wrapped, or copied into
`QuerySpace.Explanation`.

## Data and machinery separation

An ordinary application data type participates without implementing
`IEnumerable<T>`, `IQueryable<T>`, `IRowSource<T>`, or another QuerySpace
interface:

```text
ordinary data
  + handwritten or generated execution binding
  + immutable structural plan
  + selected execution strategy
```

Separation is logical and contractual, not a requirement to allocate a wrapper
object. A reusable binding may statically describe how a concrete source and row
type expose values. One execution may pass an existing array, list, immutable
array, application collection, or source-owned adapter directly to generic or
generated machinery.

`IEnumerable<T>` and `IReadOnlyList<T>` remain valid compatibility inputs for a
reference evaluator. They do not define QuerySpace capabilities and are not the
universal source contract.

Public interfaces are used only when an independently implemented, runtime
polymorphic contract is both necessary and stable. The initial declaration,
request, plan, and result model favors immutable records, sealed definitions,
value types, generic registration, and static execution entry points. This is
interface-avoiding, not interface-hostile.

## Structural plans and executable bindings

A plan records meaning independently from the machinery that evaluates it.
For each row predicate or order it retains enough owner-issued structure to
identify:

- facet or order identity;
- operator identity;
- normalized operand;
- semantic position;
- applicable row scope or row set; and
- ordering, cardinality, and terminal consequences required by composition.

An accessor, predicate delegate, comparer, or generated callback is never the
sole representation of that meaning.

The existing complete-sequence row evaluators remain the semantic reference
implementation. Their current allocation behavior does not define the
execution contract. A row-owner adoption may separate reusable execution
bindings from the complete structural plan while preserving every existing
semantic and failure gate.

The common execution choice is:

```text
structural plan + binding + source
        |
        +-- accepted source delegation
        |
        +-- recognized local topology -> specialized typed kernel
        |
        `-- otherwise ----------------> reference interpreter
```

Every accepted strategy has the same observable comparison, order, stage,
failure, terminal, disposal, cancellation, completion, and continuation
semantics. Query Space does not expose which strategy ran as query meaning.

## Compiler acceleration

Compiler acceleration specializes measured plan **topologies**, not runtime
values. A topology may describe shapes such as:

```text
one StartsWith predicate -> Head
two equality predicates -> Count
baseline order -> Head
predicate -> Top
```

Operand values remain ordinary plan data. The public representation never
becomes a nested generic pipeline whose type changes for every runtime-authored
query.

The default structural interpreter provides complete coverage. A specialized
kernel is optional and may decline a plan without changing support. Adding a
kernel changes performance only; it does not add a facet, operator, order,
selection stage, terminal, or source capability.

The first specialized kernel computes exact Count from source cardinality for
predicate-free, unordered plans composed from Head, Tail, and Window. Top and
plans that require row values decline to the reference interpreter. The
`SpecializedRowExecutionMatchesReferenceEvaluator` gate compares the
cardinality algebra with materialized selection across empty, successful, and
strict-Window-failure cases.

Graph Libraries is the first production adopter. For its admitted Count
topology, terminal resolution captures the exact local source cardinality
rather than copying row values. Its Rows terminal still captures a complete
row snapshot. Measured adopter evidence remains required for each additional
kernel topology.

## Optional source generation

Source generation is an optional declaration and acceleration mechanism. The
core runtime library remains Roslyn-free.

A generator may emit:

- descriptor and registration tables;
- stable identity constants;
- typed row accessors and operand binders;
- source adapters and comparer bindings;
- plan-topology dispatch;
- selected fused execution kernels; and
- compile-time diagnostics for contradictory or incomplete declarations.

Generated code is a witness for an explicit declaration. It does not infer
facets from every public property, infer comparison policy from a CLR type,
infer source capability from a similarly named method, or create arbitrary
expression execution.

Handwritten and generated declarations produce equivalent descriptors,
structural plans, failures, and results. The generated form may remove
reflection, closure, boxing, and dispatch costs without becoming a second query
language.

## Optional System.Text.Json correspondence

The canonical payload codec may use the platform `System.Text.Json`
implementation. Beyond that codec implementation detail, the core package
supports three independent witness adoption states:

1. a QuerySpace witness only;
2. QuerySpace and System.Text.Json witnesses; or
3. an STJ witness only, consumed through a separately owned adapter.

For the second state, the useful sharing is authored declaration and explicit
correspondence, not generator-to-generator inspection or forced reuse of erased
runtime machinery.

Roslyn generators run as peers over the original compilation. A QuerySpace
generator may observe user-authored STJ attributes and serializer-context
declarations, but it cannot inspect, authenticate, or derive a contract from
the source emitted by the STJ generator in that compilation. Emitting code that
predicts STJ's generated member names would couple QuerySpace to another
generator's implementation details and would not prove the generated witness's
effective behavior.

The middle scenario therefore uses one of two explicit compositions:

1. application code supplies the exact generated `JsonTypeInfo<T>` to an
   optional QuerySpace/STJ adapter after both generators have produced their
   independent witnesses; or
2. a separately owned post-compilation tool inspects the completed assembly and
   authenticates the exact context, root, mode, and generated flow.

Either composition may associate:

- one QuerySpace facet identity;
- one CLR member identity;
- one exact `JsonSerializerContext` and registered root;
- one JSON property identity or name;
- the effective metadata or serialization generation mode; and
- an explicitly compatible operand codec.

QuerySpace and STJ may each generate a direct typed accessor or other optimized
machine code. Avoiding a few duplicate instructions does not justify boxing,
object-typed access, interface dispatch, or coupling query execution to a JSON
writer.

An STJ property does not implicitly become a query facet. JSON naming,
inclusion, conversion, and serialization direction do not define query
identity, admitted operators, comparison, missing-value behavior, ranking,
work authorization, or source delegation.

The exact serializer context matters. One CLR type may be registered in
multiple contexts with different options or generation modes. Runtime or
post-compilation correspondence therefore names the context and root rather
than assigning one global JSON contract to the CLR type. A QuerySpace generator
may reuse explicitly authored names and attributes as declaration input, but
that is not authentication of the resulting STJ witness.

The ts-jsexport implementation demonstrates the post-compilation option, not a
source-generator composition precedent. It authenticates the exact generated
`JsonTypeInfo<T>` flow and effective generation direction from completed
metadata and IL, then consumes the resulting wire-contract facts. It does not
reuse the serializer implementation as TypeScript generation machinery.
QuerySpace follows the same evidence-versus-implementation separation only
when a post-compilation integration has equivalent evidence.

The third, STJ-only state may provide a convenient metadata-driven adapter, but
its capability and performance are explicit. Serialization-only fast-path
metadata does not imply a metadata-driven query binding. The future integration
owner defines the exact supported modes, fallback behavior, and diagnostics.

## Dependencies and platforms

Both assemblies may depend on .NET platform libraries. `System.Text.Json` is
one: the canonical payload codec in `QuerySpace` uses it to read a payload (its
canonical writer is handwritten), and the value-vocabulary snapshot identity in
`Primitives` uses `Utf8JsonWriter` for its canonical projection, as the
`Inspector.Findings` floor already does for its comparison documents. Neither
assembly has an external package, product, CLI, Browser, Markout, or Roslyn
dependency. The explanation floor may use platform primitives internally for
canonical value measurement, but it exposes no general serializer and owns no
Document wire shape. STJ-generated witness and adapter integration remains
outside the core library.

`QuerySpace.Primitives` depends on the platform only. It is listed in the
`dependency-free-contract-floors` rule of `eng/dependency-policy.json`, which
enforces platform-only over the project and compiled-assembly graphs; the rule
admits platform assemblies such as `System.Text.Json` and makes no claim about
them. That gate is a fresh evidence choice for a new assembly. The operator
considered and declined a separate serializer-absence claim for `Primitives`:
the floor is defined by what it declares (data and identities, no planner,
binder, evaluator, execution, or codec type), not by which platform assemblies
it reads.

For `QuerySpace` itself, the user's earlier evidence choice stands: its
negative external-package and product dependency claim has **no automated
absence gate** and remains **unverified**. The implementation slice must
report its actual project, package, and platform assembly references, but this
design does not require a project-graph or compiled-reference enforcement gate
for that assembly.

The public runtime contract remains compatible with NativeAOT and
single-threaded Browser/Wasm. Optional build-time generation may use Roslyn
without adding Roslyn to the generated application's runtime graph. An optional
STJ integration may depend on System.Text.Json without adding it to the core
package graph.

## Failures and diagnostics

Construction rejects contradictory declarations before publishing a
descriptor. Resolution remains atomic: it returns one owner-issued structured
failure and no executable request.

An unavailable specialization is not a failure; execution uses another
semantically complete strategy. An advertised generated or delegated
capability with missing executable machinery is a construction or binding
failure rather than a silent fallback.

Unexpected exceptions from trusted application callbacks preserve their
owner-defined behavior. QuerySpace does not convert them into successful empty
results or generic invalid-query failures.

## Pathological cases

- A domain type already implements `IEnumerable<T>`. QuerySpace neither treats
  that interface as its capability schema nor requires another source
  interface.
- One small runtime-authored plan has no specialized kernel. The reference
  interpreter executes it without weakening semantics.
- A binding has a generated fast path for `Equal -> Head` but receives
  `Contains -> Top`. It declines the kernel and uses complete interpretation;
  the plan remains supported.
- A plan's executable delegate survives but its structural facet identity or
  normalized operand is missing. Resolution fails rather than publishing an
  opaque executable plan.
- One row type appears in two serializer contexts with different naming or
  ignore policies. Correspondence remains context-root-specific.
- An STJ context has serialization-only generation for one root. It may
  serialize detached QuerySpace results but does not imply a metadata-driven
  query binding or deserialization capability.
- A JSON property is intentionally not queryable, while a derived query facet
  is intentionally not serialized. Neither inventory is widened to make them
  look symmetrical.
- Generated registration advertises an operator without emitting or binding
  corresponding executable machinery. Construction fails before descriptor
  publication.
- Execution borrows a pooled buffer. The returned result copies or otherwise
  detaches its values before the execution context releases the buffer.

## Adoption sequence

Implementation proceeds as focused slices:

1. Lock this library boundary and correct architecture and family maps.
2. Create `src/QuerySpace/QuerySpace.csproj`; migrate portable intent, payload,
   semantic row selection, and row-query contracts with their direct consumer
   tests.
3. Migrate generic Query Operation and Query Space composition contracts;
   return section-owned outcomes to `DotnetInspector.Sections`, update all
   repository references, and retire `DotnetInspector.QueryEngine`.
4. Have the row-query owner adopt the complete structural-plan and reusable
   execution-binding separation while retaining the current evaluator as its
   semantic oracle.
5. Add one direct non-dotnet-inspect .NET consumer over application-owned data
   types that implement no QuerySpace interface.
6. Add optional generator infrastructure after explicit Package Query, Library
   Query, and References row-space adopters expose stable repeated
   boilerplate; populate specialized kernels only from measurements.
7. Design and implement optional STJ correspondence and an explicit STJ-only
   adapter outside the core runtime package.
8. Complete Package Query and Library Query CLI adoption, then adopt the same
   descriptors and plans in Inspect Web/Browser Wasm and remove superseded
   product-local paths.
9. Split `QuerySpace.Primitives` from `QuerySpace` by the lifetime classes
   and the explicit dispositions in
   [Two assemblies and two participation tiers](#two-assemblies-and-two-participation-tiers),
   moving only codec-free, binder-free types, and add it to
   `dependency-free-contract-floors`; then let Product Vocabulary adopt it as
   the first tier-1 declaration under
   [#9250](https://github.com/richlander/dotnet-inspect/issues/9250).
   Binder-bearing declarations follow step 4, which must land first for any
   of them to move.
10. Extend the floor with `QuerySpace.Explanation` under
    [#9341](https://github.com/richlander/dotnet-inspect/issues/9341). The
    implementation slice adds the declaration and detached-data currencies,
    strengthens the floor-content gate, makes `VocabularySnapshot` eager, and
    migrates installed Resource Explanation as the first production caller.
    This is the one bounded pattern-plus-first-adopter slice: it retires the
    existing closed explanation unions rather than landing an unused substrate
    or a parallel model. Contextual Member, Query Space, vocabulary, Findings,
    reusable-reference, and other semantic owners then adopt one focused owner
    at a time. CLI and Browser/Wasm consume the same completed host-neutral
    Document as those adoptions land.

Each step names one semantic owner or this library-boundary owner. A later slice
does not reopen this document to absorb its adopting owner's semantics.

## Required evidence

| Gate or evidence | Required property |
| --- | --- |
| `QuerySpaceDirectConsumerExecutes` | An independent consumer constructs an executable Query Space binding and structural request, resolves its row association through the public Sections composition API, executes it over an existing application-owned collection, and verifies the detached result. Its row and collection types implement no QuerySpace interface, execution requires no separately allocated source wrapper, and the consumer has no dotnet-inspect host dependency. |
| Existing portable-query gates | Moving the types preserves intent, identity, ordering, payload, compatibility, and failure behavior. |
| Existing row-query and row-selection gates | Moving the types preserves predicate, order, stage, failure, and reference-evaluator behavior. |
| Existing Query Operation gates | Moving the types preserves executable registration and capability projection. |
| `GeneratedAndHandwrittenBindingsAreEquivalent` | When generation lands, both paths produce the same descriptor, plan, failure, and result for the covered declarations. |
| `SpecializedRowExecutionMatchesReferenceEvaluator` | When compiler acceleration lands, every admitted specialized topology matches the reference evaluator over contract-defining and pathological inputs. |
| Focused STJ integration gates | When integration lands, an explicitly supplied runtime witness or post-compilation evidence establishes exact context and root correspondence; direction respects effective generation mode, and an STJ property never creates query semantics implicitly. |
| Dependency report | The implementation PR reports the core project's evaluated project, package, and platform assembly references; by explicit user choice, no automated absence gate is required and the negative external-package and product dependency claim remains unverified. |
| `dependency-free-contract-floors` covers `QuerySpace.Primitives` | A project or package reference added to `Primitives` fails the dependency-policy gate over both graphs. This gives full coverage for the claim that concrete product-domain assemblies do not enter the floor. |
| `PrimitivesCarriesOnlyFloorTypes` | A public-surface inventory of `QuerySpace.Primitives` admits only Declaration, Detached-typed-data, and Portable-request types. It contains no planner, evaluator, execution context, public codec, continuation-bearing result, or type carrying an accessor delegate, operand evaluator, comparer factory, live source, retained enumeration, `Lazy<T>`, stream, cancellation state, or disposable resource. The inspection recursively follows generic arguments, arrays, and carried non-exported implementation fields so private storage cannot hide disallowed machinery. Constructor input may be enumerable only when the published type retains detached eager storage. The lifetime-class assignment and every explicit disposition are pinned by type family, not inferred from names. |
| Explanation floor construction gates | Contract-defining tests construct every declaration and detached-data family, preserve deterministic order and equality, accept the finite boundary values, and reject duplicate identities, unresolved locally required shapes, invalid cardinality or observation payloads, over-budget values, and deferred or partial construction. |
| Installed Resource Explanation uses `QuerySpace.Explanation` | The first implementation adopter constructs its installed schemas and snapshots from the shared floor, produces the same CLI and Browser/Wasm-ready host-neutral Document, and removes the superseded closed owner, resource-kind, identity, detail, and relationship unions. No second explanation model remains. |

## Non-claims

This design does not claim:

- zero-allocation query construction or execution;
- that generated execution is always faster than interpretation;
- that every source shape receives a specialized adapter or kernel;
- that every JSON property should be queryable or every query facet
  serializable;
- compatibility with arbitrary `IQueryable<T>` providers or expression trees;
- transparent pagination, lazy remote enumeration, or hidden continuation
  advancement;
- that explanation catalogs, paths, traversal, Documents, envelopes, or
  format lowerings belong in `QuerySpace.Primitives`;
- that a concrete domain identity or CLR type becomes part of the shared floor
  merely because an owner publishes its named explanation shape;
- a second `Inspector.Primitives` assembly or a universal primitive assembly
  for unrelated product contracts;
- stable public APIs before the implementation slice validates the proposed
  roles with direct consumers; or
- package publication before the migration, external-consumer, and production
  adoption gates land.
