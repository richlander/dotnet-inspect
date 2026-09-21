# Query operation infrastructure

## Status

**Implemented host-neutral substrate with Package Query and Library Query
adopted in CLI and Inspect Web, Graph Libraries adopted across its CLI query
sections, Dependency adopted across its command and section routes, and Find
adopted across its Type and Member result routes; remaining production adoption
continues under
[#7712](https://github.com/richlander/dotnet-inspect/issues/7712), targeting
0.26.0.** The user explicitly approved defining this shared pattern before
Package Query, Library Query, Find, Depends, and Graph adopt it separately.

The repository already has implemented portable intent, typed row-query
resolution, semantic row selection, operation-specific plans, and query
discovery. `QuerySpace.Operations` carries
`QueryOperationDefinition<TPredicate, TPlan>`, executable term and order
bindings, Query Profiles, validated typed routes, profile-scoped portable
resolution, effective capability projection, and heterogeneous
`QueryOperationRegistry` lookup. `QueryOperationInfrastructureGateTests` is the
Release gate for this substrate. [QuerySpace Library
Boundary](query-space-library.md) owns its physical composition in
`QuerySpace`; this document retains semantic authority.

Current CLI query discovery remains partly maintained through command-specific
catalogs. Package Query is the first production adopter: one effective route
now supplies plan resolution, CLI discovery terms, and Browser control terms.
Graph Libraries is the second adopter: one command route and five row-set
routes supply its Cluster plan, CLI lowering, and section discovery. Dependency
is the third adopter: its Type and hierarchy routes share one registered
vocabulary and retain their existing lowering paths. Find is the fourth
adopter: its Type and Member routes share one result-row profile. Library Query
is the fifth adopter: one explicit-population route supplies its reference plan,
CLI discovery, and Browser gesture. The remaining operations still bind their
syntax through separate paths.

[Query Space Composition](query-space-composition.md) now owns the target
composition of one operation route with explicit row spaces, terminal
requirements, structural plan descriptions, and optional source continuation.
The current `ResultPredicate` operation-term role and route-local projection of
row capabilities are transitional implementation surfaces. Their retirement is
a focused adoption of that pattern; this document continues to own operation
definition, route binding, and operation-plan resolution.

## Authority and exact claim

**Query Operation Infrastructure** is the single normative owner established
here. Its exact claim is:

> One query-capable operation registers its canonical vocabulary, applicable
> subject roles, result grain, query-term and order bindings, plan resolver,
> and declared row sets once. A command or section route explicitly binds that
> operation to one admitted subject role and query profile, after which shared
> infrastructure derives query discovery and host lowering without redefining
> the operation's semantics.

This owner defines:

- the composition of an operation definition, route binding, and executable
  query-capability registration;
- the condition under which a command or operation-backed section inherits a
  facet;
- the separation among subject binding, operation planning, and result-row
  planning;
- the requirement that discovery derive from executable registration; and
- host agreement on canonical intent and owner-issued plans.

It does not define Package, Library, Type, Member, Find, Dependency, Graph, or
section semantics. Each adopter retains its population, evidence, compatibility,
acquisition, traversal, completion, failure, result, and presentation
contracts.

## Motivation and production demo

Users should learn one query interaction model without the product pretending
that every operation asks the same question:

```console
# Package candidates; Package Query owns package aggregation.
dotnet-inspect package query 'Microsoft.Extensions.*' \
  --where "license=MIT"

# One Graph-owned selector scopes every selected graph projection.
dotnet-inspect graph libraries consumer.dll provider.dll \
  --where "Cluster=3"

# Target Library Query: one row per candidate assembly.
dotnet-inspect library query ./bin \
  --where "references=System.Text.Json"
```

Find returns Type and Member results. Package Query returns Package results.
Library Query returns Library results. Depends may return direct dependency
evidence or a rooted hierarchy. Graph returns identity-preserving topology.
Uniformity therefore means that each route receives the same infrastructure
for syntax lowering, discovery, intent, resolution, bounds, and result shaping.
It does not mean that every route exposes the same keys or plan type.

The same distinction applies to subject-first sections:

```console
# Operation-first dependency question.
dotnet-inspect depends --package Aspire.Hosting.Redis@13.5.3

# Subject-first projection of the same Dependency operation.
dotnet-inspect package Aspire.Hosting.Redis@13.5.3 \
  -S "Dependency Hierarchy"
```

When both routes bind the same operation role and query profile, they receive
the same query capabilities automatically. The section does not copy the
Depends facet inventory.

## Conventional basis

This design follows two established patterns:

- LINQ query providers separate a common query representation from the
  provider-specific plan and execution that give the query meaning.
- GraphQL schemas expose only owner-declared executable fields and derive
  introspection from those declarations rather than from rendered output.

dotnet-inspect needs stronger boundaries than either analogy supplies on its
own. Work authorization, acquisition cost, partial completion, evidence
provenance, result grain, and host portability remain explicit owner-issued
contracts. The common infrastructure composes those contracts; it does not
infer them.

## Adjacent owners

This pattern composes existing focused owners:

- [Query Space Composition](query-space-composition.md) owns the effective
  composition of this operation route with row spaces, terminal requirements,
  structural plan descriptions, and adjacent continuation capability.
- [Portable query intent](portable-query-intent.md) owns the canonical
  serializable vocabulary, terms, execution bounds, ordered selection stages,
  and order operations.
- [L2 row query and ordering](row-query-order.md) owns typed predicate and
  order resolution over one declared row vocabulary.
- [Semantic row selection](semantic-row-selection.md) owns Head, Tail, Window,
  and Top execution.
- [Section-row shaping](section-row-shaping.md) owns declared-row-set binding,
  projection, terminal Count, and common result binding.
- [CLI row selection](cli-row-selection.md) and
  [CLI execution bounds](cli-execution-bounds.md) own their L3 spellings and
  validation.
- [Progressive disclosure](progressive-disclosure.md) owns the `-Q` gesture
  and rendering of query capability discovery.
- [Operation commands and subject sections](operation-command-and-subject-section-composition.md)
  owns command placement and operation-backed section equivalence.
- [Inspection operation composition](inspection-operation-composition.md)
  owns cross-host sequencing around settlement, Workspace admission, query,
  sections, rows, Share, and presentation.
- Each operation owner defines its vocabulary, plan, evidence, result,
  completion, and failures.

`InspectionQueryPlan<TContext>` remains the prerequisite-aware producer plan
used by inspection queries. It is not the universal query-operation plan
defined here. Package Query's `PackageQueryPlan` and the row-query
`ResolvedRowQueryPlan<TRow>` are likewise valid owner-specific executable
plans, not incomplete versions of one future common plan.

## Three-stage contract

Every query-capable route preserves three distinct semantic stages:

```text
subject or population binding
  -> operation-owned query intent and executable plan
  -> typed operation result and declared row sets
  -> shared row query, selection, projection, and Count
```

### Subject or population binding

This stage establishes the admitted inputs to the operation:

- Package Query binds a bounded package candidate population.
- Library Query binds a bounded Library candidate population.
- Find binds an authorized Type or Member search population.
- Depends binds one or more dependency roots and source context.
- Graph binds seeds, peers, or a Workspace population.
- A subject section binds an already resolved Package, Library, Type, or
  Member through its declared operation role.

Subject identity, source authority, compatibility, and acquisition policy
remain with their owners. A query facet must not reconstruct a subject from
display text.

### Operation-owned planning

The operation's canonical vocabulary states the semantic question over the
bound inputs. The operation resolves one complete intent atomically into its
own executable plan.

Examples include package-candidate qualification, Library-reference
qualification, Type or Member search qualification, Dependency traversal, and
Graph population selection. These plans may produce one or more declared row
sets.

### Result-row planning

After an operation declares typed result rows, the shared row path may apply:

- result predicates;
- baseline order;
- ranking order;
- Head, Tail, Window, or Top;
- projection; and
- terminal Count.

A result window does not authorize upstream work. An execution bound does not
filter completed rows. An operation may perform source pushdown only when its
owner proves that the observable rows, order, completion, and failures remain
equivalent.

## Operation definition

One query-capable operation supplies one definition containing:

| Part | Contract |
| --- | --- |
| Operation identity | Stable owner-issued identity independent of command text, section name, or display label. |
| Canonical vocabulary | The keys, operators, values, families, incompatibilities, bounds, stages, and orders accepted by portable intent. |
| Subject roles | The finite roles through which roots, candidates, seeds, peers, or an already resolved subject may enter the operation. |
| Result grains | The owner-issued semantic units returned by the operation, such as Package, Library, Type, Member, dependency evidence, hierarchy node, edge, or graph occurrence. |
| Declared row sets | Typed row vocabularies that may be filtered, ordered, selected, projected, or counted after operation execution. |
| Query-term bindings | Executable associations from canonical terms to owner plan construction. |
| Order bindings | Executable associations from named or field orders to typed owner order resolution. |
| Resolver | The atomic transition from canonical intent and a valid subject binding to one executable owner plan or one structured failure. |
| Effects | Required capabilities, work dimensions, acquisition tiers, and completion consequences retained by the owner plan. |

The operation definition is structural and resource-free. It may refer to
owner-issued binders and plan factories but does not acquire candidates,
execute producers, or inspect content during discovery.

An operation may compose a subject-qualification vocabulary with one or more
declared row vocabularies. That composition does not merge their semantic
stages. The definition records which query term or order belongs to which
stage and row set.

## Query capability bindings

A query capability is executable owner-issued behavior, not a heading or a
help row. Every query-term or order binding records:

- a stable owner-issued binding identity;
- one semantic role;
- the subject roles and result grains to which it applies;
- the owner-issued binder used during atomic plan resolution;
- any capability, acquisition-tier, cost, or work-bound consequences;
- the structural description projected to discovery.

The binding is then one of two disjoint shapes.

A **query-term binding** additionally records:

- its canonical query key;
- admitted operators and value domain; and
- composition and incompatibility rules.

Its supported semantic roles are:

| Role | Meaning |
| --- | --- |
| Subject qualification | Determines whether a candidate subject belongs in the operation result. |
| Operation selector | Selects an owner-defined mode, occurrence population, traversal projection, or other pre-result plan input. |
| Result predicate | Transitional current role that filters one declared typed result-row set; Query Space adoption moves this binding to the corresponding explicit row space. |

An **order binding** instead records:

- either one named-order identity or one orderable row-key identity;
- whether it supplies deterministic sequence order, Top ranking, or both;
- admitted directions; and
- the typed comparer resolver required by L2 row planning.

Its supported semantic roles are:

| Role | Meaning |
| --- | --- |
| Baseline order | Defines deterministic sequence order for one declared row set. |
| Ranking order | Defines the ranking consumed by a Top stage for one declared row set. |

Execution bounds and selection stages are carried by canonical intent but are
not query-term or order bindings. They retain the contracts of their existing
owners.

A query key or order identity may have more than one role only through separate
explicit bindings. Sharing spelling does not merge the stages.

## Route binding and automatic inheritance

A command, subject query, or operation-backed section becomes query-capable by
registering one route binding:

| Part | Meaning |
| --- | --- |
| Operation | The exact operation definition the route executes. |
| Subject role | The route by which its input participates in that operation. |
| Result grain | The semantic unit the route exposes. |
| Declared row sets | The operation result rows the route may project. |
| Query profile | An owner-issued named subset or preset over applicable query-term and order bindings. |
| Host lowering | The adapter from the host gesture to canonical intent and the adapter from the owner result to host presentation. |

The shared registry validates the binding at construction time:

1. The operation owns the named subject role, result grain, row sets, and
   query profile.
2. Every inherited query term and order is applicable to that exact
   combination.
3. Every advertised query term and order has an executable binder.
4. Every admitted order resolves to a typed order binding.
5. Required capabilities and work dimensions are representable by the route.
6. The route cannot add a key, operator, value, or meaning outside the
   operation definition.

After that explicit binding, the registry derives:

- the effective query-term and order sets;
- `-Q` discovery rows;
- accepted shared query gestures;
- host control metadata;
- portable vocabulary identity;
- syntax-to-intent lowering targets; and
- restoration validation.

This is the automatic inheritance boundary. Registering an arbitrary subject,
section, schema, or output view is insufficient.

A route may intentionally expose a narrower owner-issued profile. It may not
copy a query term or order into a route-local descriptor, change its meaning,
or advertise a descriptive capability that has no binder.

## When a section facet applies to a population query

A facet available through `-Q` for a section is not automatically available to
Find, Package Query, or Library Query merely because those operations can
inspect the same subject.

Reuse is valid only when all of these are true:

1. The population query produces the same owner-issued result grain or binds
   the same declared row vocabulary.
2. The facet's semantic role is meaningful at that stage.
3. Its evidence is available under the population query's capability and work
   contract.
4. Its binder preserves the same value, failure, completion, and ordering
   semantics.
5. The operation owner includes it in an applicable query profile.

For example:

- A Type visibility predicate may be reusable for Find Type result rows if
  Find binds the same typed row vocabulary.
- A Performance Triage facet over optimization-opportunity rows does not
  become a Type candidate predicate merely because a Type section can render
  those rows.
- A Body Kind selector that authorizes body-occurrence production does not
  become a general Find row predicate.
- A Graph Cluster selector scopes a Graph occurrence population before several
  projections. A rendered `Cluster` column elsewhere does not acquire that
  meaning.

Parity among population queries is therefore measured by infrastructure and
capability classes, not identical facet inventories.

## Same spelling across result grains

An owner may deliberately reuse a canonical key when the operand and comparison
meaning are stable but the result grain supplies a different quantifier.

Assembly-reference qualification is the motivating case:

- Library Query evaluates whether one candidate Library directly references
  the normalized simple assembly name.
- Package Query evaluates whether any admitted managed `ref/` or `lib/`
  Library in one candidate Package references that name.

Both bindings may reuse a Metadata-owned reference matcher and the
`references` key. They remain separate executable bindings because their
candidate grain, quantifier, acquisition, evidence, completion, and failures
differ. Package Query must not call Library Query once per asset and erase
package-level partial failure; Library Query must not acquire package
aggregation merely to reuse the spelling.

Repeated-term composition also remains owner-issued. If both operations define
repeated `references` terms as conjunction, each must prove that behavior at
its own result grain.

## Canonical intent and executable plans

Hosts lower accepted gestures to one `PortableQueryIntent` for the operation's
registered vocabulary. The intent crosses persistence, sharing, and host
boundaries. Typed bindings and executable plans remain process-local.

Resolution is atomic:

```text
route binding + subject binding + canonical intent
  -> validate applicability and compatibility
  -> bind every facet, bound, stage, and order
  -> executable owner plan
```

Any unknown, unsupported, incompatible, or unbindable part produces one
structured failure and no executable request. A host must not silently drop a
term because it does not render the corresponding control.

There is no universal executable plan type. An owner may return
`PackageQueryPlan`, a Dependency request, a Graph request, a Find request, or
another type-correct plan. Shared infrastructure requires a common resolution
protocol and result envelope, not a common internal representation.

## Discovery

Query discovery is a projection of the effective executable route binding. It
must not be maintained as a second term or order capability inventory.

For the CLI:

- bare `-Q` lists only routes or sections with at least one effective query
  term or order;
- named `-Q` describes the applicable keys, gestures, operators, values,
  comparisons, orders, and costs;
- discovery performs no acquisition or producer execution; and
- unsupported subject-role or facet combinations are absent rather than
  displayed with a runtime no-op.

[Progressive disclosure](progressive-disclosure.md) retains the rendering,
selection, schema, and Count behavior of `-Q`.

Browser/Wasm projects the same structural descriptors into controls, presets,
or free-form query input. A Browser control is not the registry. Omitting one
control does not authorize a host-local vocabulary or execution path.

## Commands and operation-backed sections

Top-level and subject-first routes may bind the same operation:

```text
top-level command
  -> explicit roots or population
  -> operation role

subject command section
  -> already resolved subject and context
  -> operation role
```

If their subject bindings are semantically equivalent, the two routes resolve
the same canonical intent through the same operation definition and retain the
same result, completion, diagnostics, and portable projection.

An operation-backed section may contribute an owner-issued preset. The preset
may select a result projection, default query profile, or fixed operation
parameter. It may not define route-local query semantics.

Ordinary sections that only render producer evidence remain ordinary sections.
They do not become query operations merely by registering a schema or exposing
fields.

## Work, rows, and completion

The infrastructure keeps four quantities distinct:

| Quantity | Meaning |
| --- | --- |
| Candidate population | Subjects admitted before operation qualification. |
| Work bound | Maximum authorized upstream work in one owner-issued dimension. |
| Matched operation results | Results satisfying operation-owned qualification and completion rules. |
| Selected result rows | Rows remaining after predicates, order, selection stages, projection, or Count. |

Package Query's candidate `--take`, Library Query's future candidate bound,
Find's scope bounds, Dependency depth, and Graph path limits are not aliases
for `-n` or Head. A route that exposes both must report their distinct
completion consequences.

Failures remain visible at the stage that produced them. An acquisition,
decode, query, traversal, or row-resolution failure must not become a
success-shaped empty result.

## Host agreement

CLI and Browser/Wasm may expose different gestures while consuming the same
operation definition:

- CLI parses command text and shared query options.
- Browser may use structured controls, presets, or restored portable intent.
- Both construct the same canonical vocabulary terms and subject role.
- Both resolve through the same owner-issued resolver.
- Both receive the same typed content, portable projection, diagnostics, and
  completion evidence.

Host-only interaction state, command-line aliases, navigation state, and
presentation remain outside the operation definition.

When a host does not expose an optional facet, it constructs no term for that
facet. It does not execute a different default operation. Owner-issued defaults
belong to the operation definition and therefore agree across hosts.

## Pathological cases

The following cases are contract-defining:

### Rendered field without a facet

A section exposes a `Kind` column but has no executable `Kind` predicate.
Discovery does not advertise one. Neither the registry nor a host parses the
rendered cell to manufacture a query.

### Facet valid for only one row set

An operation returns summary and occurrence rows. A predicate registered only
for occurrences is unavailable when a route projects summaries, even when both
tables display similarly named fields.

### Subject and section name collision

Two sections called `References` belong to different operations or result
grains. The section name does not establish shared query identity. Only the
operation and facet identities do.

### Unsupported restored facet

A shared packet restores a facet added by a newer product version. The older
host returns a structured unsupported-vocabulary failure before acquisition.
It does not run the remaining terms.

### Capability-gated facet

A facet requires package content or source acquisition. Discovery may describe
the required capability, but execution without authorization fails before
work. The facet is not treated as a nonmatch.

### Work bound beside a row window

A query carries a candidate work bound and a Head result stage. The operation
honors the candidate bound first, records completion, then applies Head to the
matched row sequence. It does not stop candidate work merely because Head has
enough current matches unless its owner defines and proves an equivalent
short-circuit.

### Equivalent command and section routes

A top-level Depends route and a Dependency Hierarchy section bind the same
resolved package root and intent. They must not disagree about facet meaning,
traversal failure, completion, or selected rows.

### Same key, different grain

Package Query and Library Query both accept `references=System.Text.Json`.
The former returns Package rows under package aggregation; the latter returns
Library rows under direct reference evidence. Neither substitutes its result
grain for the other.

## Current substrate and migration boundary

The migration begins from useful but separate systems:

- `PortableQueryIntent` and `PortableQueryVocabulary<TPredicate, TPlan>`
  provide canonical intent and atomic owner resolution.
- `QueryOperationDefinition<TPredicate, TPlan>`,
  `QueryOperationRoute<TPredicate, TPlan>`, and `QueryOperationRegistry`
  provide executable registration, applicability validation, effective
  capability projection, and profile-scoped resolution over that intent.
- Package Query supplies the first production operation definition and route
  over its portable vocabulary and `PackageQueryPlan`. Its effective route
  projects the ordered registered terms consumed by CLI `-Q` and Inspect Web
  controls, and all complete intents resolve through that route.
- `RowQueryIntent`, `ResolvedRowQueryPlan<TRow>`, `RowQueryResolver`, and
  `RowQueryExecutor` implement the host-neutral row path.
- `InspectionQueryPlan<TContext>` implements prerequisite-aware producer
  scheduling for inspection queries.
- `SectionQueryCatalog` still binds command sections and summaries explicitly,
  but its Package Query term rows are projected from the effective operation
  route.
- Graph Libraries owns one executable Cluster selector, a command-wide route,
  and five operation-backed row-set routes. CLI parsing and each section's
  `-Q` projection derive from those effective routes.
- Dependency owns separate type-relationship and rooted-hierarchy profiles.
  The type route projects the existing Source, Target, Kind, Traversal, row
  selection, ranking, and depth semantics. Top-level and Package hierarchy
  routes share the hierarchy profile, depth dimension, and row stages.
- Find owns distinct Type and Member result routes over their authorized search
  populations. Both routes share the executable Head, Tail, and Window result
  profile and intentionally expose no query-term or order inventory.

The new registry is implemented, and Package Query has replaced its
host-local term inventories with one route-backed projection. Remaining
production adopters still need to replace their parallel route-local capability
descriptions. The registry does not replace the implemented intent, row,
producer, or operation plans.

## Counted production adoption

[#7712](https://github.com/richlander/dotnet-inspect/issues/7712) owns this
eight-step path:

1. Lock this pattern and its authority map.
2. Implement executable operation, query-term, order, profile, and route
   registration; derive `-Q` from the effective binding.
3. **Implemented:** adopt Package Query in CLI and Inspect Web without changing
   its vocabulary, package aggregation, evidence, bounds, failures, or result
   rows.
4. **Implemented:** adopt Graph query surfaces and operation-backed Graph
   sections without changing Graph topology or projection semantics.
5. **Implemented:** adopt Depends and operation-backed Dependency sections
   without changing root, traversal, evidence, completion, or hierarchy
   semantics.
6. **Implemented:** adopt Find Type and Member query capability without changing
   its discovery grammar, scope rules, or result grains.
7. **Implemented:** add Library Query over explicit Library populations, with
   assembly-reference qualification as its first production facet.
8. Remove superseded command-local query catalogs and lowerers after every
   adopter has Release-gate coverage.

Each operation adoption is a separate focused owner change. A broad
implementation PR spanning those owners is not authorized by this design.

Package Query is the first CLI-plus-Browser adopter. Its operation definition
registers the package-population subject role, Package result grain, Packages
row set, complete term profile, candidate and match dimensions, admitted row
stages, acquisition tiers, and package-content capability. `PackageQuery.Terms`
and `PackageQuery.RegisteredTerms` are projections of that effective route;
CLI and Browser host adapters add only their syntax and control presentation.
Later hosts may consume the same pattern without requiring every CLI operation
to gain a Browser page in this release.

Graph Libraries is the second adopter. Its operation definition registers the
explicit Library pair subject role, Library-pair direct-use result grain,
Cluster selector, and five existing projection row sets. One command route and
one route per operation-backed section expose the same selector. The CLI lowers
`--where` to canonical portable intent, resolves the owner-issued plan, and
applies that plan through
`AssemblyPairDirectUseClusterProjection.ScopeToObservedCluster`. The existing
pair query, cluster derivation, root-path composition, section selection,
Markout lowering, completion, and failure contracts remain unchanged. Inspect
Web has no two-Library selection surface, so this focused adoption adds no
Browser gesture; a future Browser consumer can use the same route and plan.

Dependency is the third adopter. Its operation definition remains beside the
existing type-relationship row vocabulary and host-neutral Dependency content
in `DotnetInspector.Sections`, so registration cannot drift into a second
Source, Target, Kind, or Traversal implementation. The positional-Type route
admits those existing predicates and orders, `Top`, ordinary row stages, and
the independent Depth work dimension. Asset-mode `depends` and Package
`Dependency Hierarchy` bind distinct subject roles to the same hierarchy
profile, depth dimension, and row-stage contract. CLI lowering retains the
existing Dependency acquisition, traversal, evidence, completion, hierarchy,
failure, and rendering paths.

Find is the fourth adopter. One operation binds distinct authorized Type and
Member search-population roles to their existing result grains and row sets.
Both routes use the same result-row profile, which admits Head, Tail, and Window
without inventing predicates, ordering, or ranking that the Find owners do not
define. The CLI lowers its existing semantic row-selection grammar to portable
intent, resolves the active route before acquisition, and executes the
owner-issued plan after the complete Type or Member search result is available.
The existing pattern grammars, scope authorization, operation limit,
completion, Count, diagnostics, result shapes, and rendering remain unchanged.

Library Query is the fifth adopter. Its operation registers one explicit
Library-population role, Library result grain, Libraries row set, candidate
dimension, direct `references` qualification, and Head, Tail, and Window
stages. The CLI binds either one top-level DLL directory or one platform
reference pack through `AssemblySetResolver`; the host-neutral inspection owns
Metadata evaluation, Library-grain results, visible failures, and completion.
Inspect Web supplies its already-realized package surface as exact typed
participants, evaluates them through the same plan and envelope, and projects
matches to product-issued asset IDs without matching display names.
The focused [Library Query](library-query.md) design owns its population,
evidence, work-bound, result, and Count contracts.

Before an implementing PR or stack merges, record its user-observable change
on the [0.26.0 release tracker](https://github.com/richlander/dotnet-inspect/issues/7493).

## Required gates

`QueryOperationInfrastructureGateTests` provides the substrate Release gates
for:

- construction-time rejection of descriptive query terms or orders without
  executable binders;
- rejection of route bindings naming inapplicable subject roles, result
  grains, row sets, facets, bounds, or orders;
- exact effective term, order, dimension, and stage projection from the
  selected registration;
- profile-scoped atomic resolution that starts no plan after an unsupported
  term or order; and
- required term families, dimensions, and Top ranking remaining executable
  through the selected route.

Package Query's Release gates cover exact route projection, exact CLI `-Q`
projection, exact Browser-control projection, and equivalent canonical intent
from corresponding CLI and Browser gestures. Existing Package Query gates
continue to cover independent candidate and match bounds, row selection,
visible failures, acquisition authorization, execution, and result rows.

Graph Libraries' Release gates cover exact command-route projection, automatic
Cluster inheritance across all five row-set routes, canonical CLI intent,
atomic rejection of unsupported terms, and the existing cluster-scoped command
behavior and visible unavailable-cluster failures.

Dependency's Release gates cover exact type-route projection, executable
predicate/order/Top lowering, hierarchy command/Package-section equivalence,
automatic `-Q` inheritance, separation of Depth work from result-row stages,
pre-acquisition rejection, and the existing visible acquisition, decode,
traversal, and row-resolution failures.

Library Query's Release gates cover route-derived discovery without
acquisition, portable reference qualification, repeated-term conjunction,
candidate-bound separation from result rows, directory and platform
populations, Count completeness, visible malformed-reference failures,
participant-backed Browser execution, exact asset-ID projection, and Library
navigation filtering.

Adopter-specific owners name the authentic package, assembly, or repository
fixtures that establish their behavior. This pattern does not manufacture a
synthetic universal operation.

## Non-claims

This design does not:

- define one universal query vocabulary, subject, result type, plan type,
  section schema, renderer, or command;
- require Find and Package Query to expose identical keys;
- make every displayed field queryable or orderable;
- infer query facets from a section name, schema, producer, or subject type;
- make a facet from one result grain available to another without an explicit
  owner-issued binding;
- treat acquisition limits, traversal controls, predicates, ranking, row
  selection, projection, and presentation limits as interchangeable;
- require a command to expose every gesture supported by the infrastructure;
- redefine Package, Library, Find, Dependency, Graph, section, or host
  interaction semantics; or
- authorize one implementation PR across all adopters.
