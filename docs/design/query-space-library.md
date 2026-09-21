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

## Owner and exact claim

**QuerySpace Library Boundary** owns this exact claim:

> `QuerySpace` is one dependency-free, host-neutral .NET library whose public
> boundary composes the existing owner-issued portable query, row planning,
> semantic selection, operation registration, and Query Space structural
> contracts for reuse by dotnet-inspect, Inspect Web, and independent .NET
> applications. It owns their physical and API composition, dependency
> boundary, lifetime boundary, and extension seams without taking semantic
> ownership from their focused designs.

This owner defines:

- the `QuerySpace` assembly, package, root namespace, and focused child
  namespaces;
- which reusable contracts belong in that library and which product contracts
  remain outside it;
- the separation among durable structural data, reusable execution machinery,
  one-execution state, and detached results;
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

The library does not carry:

- product-owned vocabularies, operation plans, or result types;
- declared section schemas, section projection, envelopes, or
  `SectionCountOutcome`;
- source-selection, acquisition, delegation, completion, or continuation
  implementations;
- host policy, command parsing, Browser controls, or presentation;
- lazy results that retain borrowed execution state; or
- arbitrary executable content supplied by a portable request.

`SectionCountOutcome` belongs to `DotnetInspector.Sections`. Its temporary
location in the current carrier does not make section-result semantics part of
QuerySpace.

## Namespaces and identity

The package and assembly name is `QuerySpace`. Its public namespaces are
organized by reusable role:

```text
QuerySpace
QuerySpace.Rows
QuerySpace.Operations
QuerySpace.Composition
```

Portable intent and shared structural identities use the root namespace.
Row-query and semantic-selection contracts use `QuerySpace.Rows`. Generic
operation registration uses `QuerySpace.Operations`. Multi-scope Query Space
descriptors and requests use `QuerySpace.Composition`.

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

There is no public `IQueryable<T>` analogue and no universal executable
`QueryPlan<T>`. Query Space resolution retains the associations among
operation-owned plans, row plans, participating row sets, and the terminal
without replacing those plans with one erased executable type.

The migration changes CLR type identities. `DotnetInspector.QueryEngine` is
retired after all repository consumers move; it is not retained as a forwarding
assembly or compatibility namespace. The current carrier has no independently
published compatibility promise that justifies preserving two public
identities for the same contract.

## Five lifetime classes

The reusable boundary distinguishes five lifetime classes:

| Class | Contents | Lifetime |
| --- | --- | --- |
| Declaration | Facets, row vocabularies, operation registrations, query-space definitions, and descriptors | Reusable, immutable, resource-free |
| Portable request | Portable intents, row associations, selected row sets, and terminal requirement | Durable and serializable under the owning compatibility rules; resource-free |
| Resolved plan and binding | Normalized operands, order and selection stages, owner-issued structural plans, typed accessors, operand evaluators, and comparer machinery | Immutable and resource-free; reusable only under its owner-issued definitions and compatibility |
| Execution context | Bound source adapters, enumerators, buffers, cancellation, destinations, and leases | Owned or borrowed for one bounded execution |
| Result | Selected rows, Count or another owner-issued outcome, completion, diagnostics, and opaque continuation receipts | Detached from execution resources |

A descriptor, portable request, resolved plan, or reusable binding contains no
live source, enumerator, network client, buffer lease, cancellation source, or
deferred source operation.
A result exposes no borrowed buffer and performs no hidden work when inspected
or enumerated.

An opaque source continuation may be retained beside a completed result under
its source owner's contract. It is a durable receipt, not a live QuerySpace
handle and not part of portable query identity.

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

The initial library extraction preserves the structural seam but does not claim
an allocation or throughput improvement. Kernel selection and performance
claims require measured adopter evidence in their focused implementation
slices.

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

The `QuerySpace` runtime library may depend on .NET platform libraries,
including `System.Text.Json` for the canonical payload codec. It has no external
package, product, CLI, Browser, Markout, or Roslyn dependency. STJ-generated
witness and adapter integration remains outside the core library.

Per the user's evidence choice for this design, the negative external-package
and product dependency claim has **no automated absence gate** and remains
**unverified**. The implementation slice must report its actual project,
package, and platform assembly references, but this design does not require a
project-graph or compiled-reference enforcement gate.

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
- stable public APIs before the implementation slice validates the proposed
  roles with direct consumers; or
- package publication before the migration, external-consumer, and production
  adoption gates land.
