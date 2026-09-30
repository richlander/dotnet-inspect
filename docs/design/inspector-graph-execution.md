# Inspector.Graph execution and derived views

## Status, owner, and claim

Status: **design contract** for
[#8747](https://github.com/richlander/dotnet-inspect/issues/8747), a focused
successor under
[#8669](https://github.com/richlander/dotnet-inspect/issues/8669).
The selected-adjacency and distinct-neighbor-degree implementation is tracked
by [#8821](https://github.com/richlander/dotnet-inspect/issues/8821).

The **Inspector.Graph Execution** owner defines this claim:

> `Inspector.Graph` executes typed, domain-neutral operations over graph
> structure, requesting only declared relationship expansion, constructing
> only declared structural views, and returning terminal-specific results with
> applicable completion, limits, and failures intact.

[Inspector.Graph library
boundary](inspector-graph-library-boundary.md) owns the immutable carrier and
graph-local validity. [Inspection operation
kernels](inspection-operation-kernels.md) owns the cross-cutting typed handoff,
reference-execution, and specialization pattern. [Inspection graph
document](inspection-graph-document.md) owns .NET subject, relationship,
evidence, and product-completion meaning. This document owns only Graph
execution over caller-bound typed structure.

The contract is **unverified** until the gates under
[Required evidence](#required-evidence) land and run in Release.

## Collection and algebra

`Inspector.Graph` is both a generic collection for graph-shaped data and an
algebra over that collection. This differs from QuerySpace, whose row carrier
is deliberately simple and whose primary complexity is query execution. Graph
must make its structural representation and its operations fit each other:
direction, adjacency, cycles, paths, and visited state affect both correctness
and cost.

The collection does not discover the values placed in it. A domain-aware
producer or composer may establish that method A calls method B, represent the
methods as typed subjects, and supply a typed `Calls` edge. Graph may then ask
whether that edge is present, enumerate its adjacency, or walk it without
understanding methods, IL, or what `Calls` means.

This distinction is permanent:

- the caller owns domain identity, relationship meaning, evidence, and
  population;
- Graph owns topology, graph-local execution, and structural results; and
- product composition owns the meaning of the Graph result for a user-facing
  question.

## User outcome

The first production experience remains the OpenTelemetry external-focused
call view. Existing product composition identifies members and call
relationships; Graph executes the bounded structural operation; the CLI and
Inspect Web consume one typed product result.

The first new query-first proof is a distinct-neighbor degree operation over a
Library-scale type graph. Research owns what "sea level" and "peak" mean, the
relationships admitted to each calculation, and any domain exclusions. Graph
owns incoming or outgoing adjacency, distinct-neighbor counting, self-loop
policy supplied by the plan, deterministic structural results, and the
terminal mechanics. System.Text.Json is the representative asset and
System.Private.CoreLib is the pathological scale case.

The result is not merely a reusable implementation. It lets a person or agent
ask where work concentrates in an unfamiliar Library and receive the same
evidence-backed answer from CLI rows and an Inspect Web overlay.

## Conventional basis

This design combines established practices without copying one complete
model:

| Design | Evidence adopted | Deliberate boundary |
| --- | --- | --- |
| .NET generic collections | A data shape owns operations intrinsic to that shape; callers supply element meaning | Graph is immutable and evidence-bearing rather than a mutable general-purpose collection |
| Boost Graph Library concepts and property maps | Algorithms consume graph capabilities and caller-associated values without owning domain meaning | Runtime-authored plans, typed failures, and completion remain explicit |
| QuerySpace | Structural plans, terminal-specific execution, source delegation, and reference-versus-specialized equivalence | Graph topology is richer than rows and is not expressed as QuerySpace syntax |
| Producer Planning PRs #8734, #8735, and #8736 | Declare demand before work, skip excluded scope, fold into typed accumulators, retain data only as long as required, and specialize measured closed shapes | Method-definition layers, class masks, and producer scheduling remain Analysis concerns |
| [.NET CVE schema](https://github.com/dotnet/designs/blob/main/accepted/2025/cve-schema/cve_schema.md) | Canonical facts plus derived forward and reverse indexes make common questions direct and predictable | Product-named JSON indexes and presentation labels do not enter Graph |

The CVE schema validates its representation by writing real `jq` queries: poor
queries are evidence of a poor data shape. Graph adopts the same test at its
own boundary. A common graph question should select one declared structural
operation or keyed view, not require every consumer to rebuild adjacency,
perform unrelated algorithms, or scan the complete edge set repeatedly.

## Execution inputs

One execution has the four inputs required by the operation-kernel pattern:

```text
GraphExecution
  StructuralPlan
  DocumentOrProviderBinding
  ExecutionScope
  TerminalRequirement
```

This is a conceptual contract, not a frozen CLR API.

### Structural plan

The immutable structural plan declares all work before execution begins. It
contains, as applicable:

- operation identity;
- roots or document-local start targets;
- typed relationship selection;
- outgoing, incoming, or bidirectional traversal;
- caller-supplied structural scope;
- self-loop treatment;
- depth, node, edge, path, or other owner-dimensioned bounds;
- required structural views; and
- one terminal requirement with a named result unit.

The plan preserves caller-issued relationship identities as typed values.
Graph may compare those values under the admitted typed equality but does not
branch on their domain meaning.

Product modes such as single seed, peer seeds, and induced set lower to
structural roots and scope before Graph execution. This design does not
transfer their product meaning or admission rules to Graph.

A runtime-authored plan remains immutable structural data. Its CLR type does
not change for every relationship value, root set, depth, or other runtime
combination. Executable predicates, providers, and comparers live in typed
bindings rather than becoming the only representation of plan meaning.

### Typed binding

A binding connects the plan to one immutable document or to caller-owned
providers. It supplies only the capabilities selected by the plan:

- subject and relationship equality;
- node and edge access;
- selected structural views;
- optional node admission or edge admission;
- optional batched relationship expansion; and
- the owner-issued snapshot, generation, or receipt currency required for
  coherent joins.

The binding is neither a type-keyed service locator nor an `object` bag.
Reflection, string type names, and runtime inspection of a provider's concrete
type do not establish capability.

### Execution scope and result lifetime

The caller supplies a finite scope. Graph neither widens that scope nor
acquires an inspected subject. Every document, view, and provider in one
execution is bound to the same applicable owner-issued snapshot or generation.

Execution may borrow documents, views, providers, buffers, and frontier state.
Its result is detached: it retains typed values, local ids, completion,
limits, failures, and a work receipt, but no live provider callback, mutable
frontier, enumerator, reader, lease, or Workspace.

## Two execution forms

### Closed-document execution

A closed-document operation reads one already-populated immutable
`GraphDocument`. It performs no relationship discovery and invokes no
provider.

The closed-document binding reuses the relationship equality admitted by that
document. Its detached receipt carries the document's runtime identity rather
than retaining the document, so local-id results stay associated with exactly
the structure that issued them.

The operation is structurally exact over the supplied document when it
exhausts its declared structural scope. That does not make the document a
complete representation of the caller's domain. For example, Graph can answer
exactly that no `Calls` edge from A to B is present in the document while the
product must still qualify whether call production was complete.

Closed-document operations include direct relationship membership, adjacency,
degree, and algorithms whose complete input is the document. Complex algorithm
semantics such as roll-up, strongly connected components, levels, and
communities remain separately owned focused designs.

### Provider-backed expansion

A provider-backed operation begins with caller-bound roots and requests typed
relationship batches while Graph advances its frontier:

```text
current frontier
  -> caller-owned typed provider
  -> subjects, relationships, occurrences, limits, failures, completion
  -> graph-owned admission, visited state, and next frontier
```

The provider owns domain lookup, relationship construction, evidence, and
population completion. Graph owns frontier order, visited topology, declared
direction, structural bounds, and terminal settlement.

Expansion is batchable over one frontier. One unconstrained provider call per
visited node is not the default contract because it prevents shared
acquisition, source pushdown, and predictable bounds. A focused adopter may
use that shape only with explicit cost and bound evidence.

A provider response is associated with the exact frontier, relationship
selection, scope, and owner-issued snapshot that produced it. An empty batch
does not prove domain absence unless its completion covers that association.

Closed-document and provider-backed execution share operation and terminal
semantics. They differ in population authority: the former examines topology
already present, while the latter may add topology through a caller-owned
binding.

When the terminal is Document, provider-backed expansion constructs one
canonical document from the admitted topology. Other terminals do not
materialize that document unless their own contracts require it.

## Canonical structure and derived views

The immutable graph document is the canonical structural truth. A derived
view accelerates or reshapes that truth; it does not become an independent
source of nodes, edges, evidence, or completion.

Dense document-local ids provide direct access to canonical nodes, groups,
edges, and occurrences. Other views are acquired only when declared by the
plan. Candidate views include:

- outgoing edge ids by node id;
- incoming edge ids by node id;
- adjacency restricted to selected relationships;
- distinct neighboring node ids;
- reverse mappings for a selected structural projection; and
- terminal-specific keyed results.

This follows the CVE schema's split between arrays for discovery and
dictionaries for known-key lookup. It does not require one physical
representation or make every possible index permanent.

A derived view:

1. identifies the exact source document or owner-issued generation;
2. refers to canonical content by document-local id rather than copying
   caller payloads;
3. preserves canonical order or declares a deterministic structural order;
4. records the relationship, direction, scope, and self-loop policy from which
   it was derived;
5. cannot be applied to another document or generation; and
6. carries no stronger completion claim than its source and derivation.

An implementation may build a view during construction, bind a reusable
caller-owned view, or create an execution-local view. The observable contract
is identical. It must not build outgoing, incoming, relationship-partitioned,
or terminal-specific indexes that no selected operation uses.

Product-named views such as `method_callers`, `package_dependencies`, or
`cve_releases` remain with product composition. Graph may supply the generic
typed structural result from which those views are named and published.

The first derived-view proof uses one neighbor plan that selects relationship
values, direction, and self-loop policy. Its adjacency terminal returns one
document-local row per canonical node with selected edge ids and distinct
neighboring node ids. Its degree terminal returns only document-local node ids
and distinct-neighbor counts; it does not materialize adjacency rows and count
them afterward. Both terminals order rows, edge ids, and neighbor ids by their
canonical document-local ids and identify the exact source document in their
work receipt.

## Shape-native operations and terminals

An operation declares one structural question. Initial operation families are:

- direct relationship membership;
- outgoing, incoming, or bidirectional adjacency;
- bounded traversal; and
- bounded reachability.

The first implementation need not expose every family. Each added operation
must have a production caller and contract-defining pathological cases.

A terminal declares what the caller requires from that operation. Initial
terminal categories are:

- **Exists:** whether any admitted structural match exists;
- **Count:** the exact cardinality of a named population;
- **Rows:** typed structural values in deterministic order;
- **Document:** a retained graph document containing the admitted topology; and
- **Path:** an owner-defined path result for an operation that defines path
  selection and tie-breaking.

The operation owner names the Count unit: nodes, logical edges, occurrences,
neighbors, paths, or another declared population. There is no unqualified
Graph Count.

Terminals are peers, not projections that must be implemented through Rows:

- Exists stops before the next read once a positive answer is settled.
- Count does not allocate row or document payloads merely to count them.
- Rows materializes only its declared row population.
- Document preserves the topology and evidence required by its contract.
- Path materializes only the path result selected by its operation.

An implementation may derive one terminal from another only when it preserves
the required ordering, failure, limit, and completion behavior without extra
observable work.

## Two completion axes

Graph execution keeps two independent completion questions:

| Axis | Question | Owner |
| --- | --- | --- |
| Structural execution | Did the selected Graph operation exhaust its declared topology and bounds? | Graph |
| Domain population | Did every applicable producer supply the relationships needed for the represented subject, relationship, and scope? | Producer and product composition |

Neither axis substitutes for the other. Exhausting a closed document does not
prove that its domain population is complete. A complete provider response does
not prove that a depth-, node-, edge-, or path-bounded traversal exhausted the
requested topology.

Graph reports structural answers even when domain population is incomplete.
The product result retains the population qualification rather than suppressing
a useful answer or presenting it as an unqualified domain conclusion.

Terminal publication follows these rules:

| Result | Required qualification |
| --- | --- |
| Positive Exists or a found path | The retained positive structural evidence is valid even when unrelated work is incomplete |
| Negative Exists or no path | Exact over the document when structural execution completes; a domain absence claim also requires applicable population completion |
| Count | Exact for the represented topology when structural execution completes; an exact domain count also requires applicable population completion |
| Rows or Document | May retain healthy partial evidence, with every applicable limit and failure visible |
| Direct membership in a closed document | Exact as a statement about that document; a domain absence claim additionally requires applicable population completion |

Limits and failures are scoped. A failure in an unrelated relationship,
provider, direction, or subject region does not taint positive evidence or a
result whose declared population cannot depend on it. An applicable failure or
bound cannot be discarded merely because the terminal returned a value.

## QuerySpace composition

Graph and QuerySpace remain sibling substrates with no project reference
between them.

A product composer may use QuerySpace before Graph execution to select roots,
relationships, providers, characteristics, or an admitted terminal. During
provider-backed expansion, a Queries or Research adapter may implement a typed
Graph provider by executing a resolved QuerySpace request for one frontier
batch. After Graph execution, QuerySpace may select, order, or count product
rows lowered from the Graph result.

The product layer owns those composition choices. Graph does not parse
QuerySpace intent, and QuerySpace does not define graph direction, visited
state, reachability, or path semantics.

A post-execution row query cannot retroactively reduce Graph work. If a
product question requires scope or a terminal to affect traversal, product
composition must place that intent in the Graph plan before execution.

## Reference execution and specialization

Every operation first has one complete reference implementation. It defines
observable:

- admitted topology and traversal order;
- stopping behavior;
- result ordering and tie-breaking;
- limit and failure precedence;
- both completion axes; and
- the work receipt.

The reference path may favor clarity, but its public execution contract must
not require `IEnumerable<T>`, captured delegates, boxing, reflection, or
virtual dispatch where a typed binding can expose the same work directly.

Optimization follows the evidence from Producer Planning:

1. exclude work through root, relationship, direction, scope, and bound
   selection;
2. acquire only the structural views and provider resources declared by the
   plan;
3. stop when the selected terminal is settled;
4. fold into terminal-specific typed state rather than materializing unused
   values;
5. retain frontier, visited, evidence, and derived state only as long as
   correctness and later readers require; and
6. specialize measured closed plan topologies only after those structural
   savings.

Visited state required for cycle control is correctness state, not disposable
intermediate data. A retention optimization may not remove it while another
reachable path could revisit the same node under the operation's identity
rules.

A specialized kernel:

- recognizes one supported structural plan before observable execution;
- uses the same typed bindings and result contract;
- adds no operation, relationship, scope, or terminal capability;
- returns the same values, ordering, limits, failures, completion, and receipt
  as reference execution; and
- never falls back after accepting the execution and producing effects.

The dispatcher may specialize a plan topology while retaining relationship
values, roots, predicates, and bounds as runtime data. Generated code is
optional and cannot become the only correct path.

## Work receipt

Execution records work where it occurs. A receipt distinguishes at least:

- canonical nodes and edges examined;
- frontier subjects submitted to providers;
- provider batches completed, bounded, or failed;
- structural views built or reused;
- nodes admitted to visited state;
- terminal settlement versus structural exhaustion; and
- the applicable source document or generation.

Receipt units are operation-owned and typed. They support tests, diagnostics,
and performance evidence; they do not become user-facing domain conclusions.
A producer does not self-report Graph work that only the executor can observe.

## First production adoption

Adoption is staged so each slice remains independently coherent:

1. **Carrier implementation.** Implement the BCL-only structural document,
   direct-consumer fixture, graph-local invariant tests, and permanent
   dependency gates from the library-boundary design.
2. **Reference migration.** Move the complete-document neighborhood and focus
   behavior onto Graph reference execution without specialization. Preserve the
   OpenTelemetry result, CLI output, Inspect Web lowering, limits, failures,
   and completion.
3. **Derived-view proof.** Implement selected incoming and outgoing adjacency
   plus distinct-neighbor degree. Research binds the #8732 sea-level and peak
   definitions; CLI and Inspect Web consume one product result.
4. **Provider-backed proof.** Bind one real QuerySpace-backed relationship
   provider through Queries or Research and preserve complete-document
   reference behavior.
5. **Measured specialization.** Specialize one production terminal only after
   the reference path and work receipt identify a meaningful hot shape.
6. **Retirement.** Remove superseded Queries-owned traversal, indexing, and
   projection paths once their callers use Graph.

Later roll-up, strongly connected component, level, weak component, and
community algorithms are separate focused efforts under #8744. They consume
this execution contract rather than expanding it.

## Pathological cases

Contract tests include:

- an empty graph and a root with no incident edge;
- one self-loop under both admitted self-loop policies;
- duplicate payload values under caller-supplied equality;
- a deep chain that cannot rely on recursive call-stack depth;
- a dense cyclic graph whose visited state prevents repeated work;
- parallel typed relationships between the same endpoints;
- incoming and outgoing queries over the same canonical edges;
- a provider that returns an empty complete batch;
- an empty incomplete batch;
- a provider failure after healthy positive evidence;
- node, edge, and depth bounds reached exactly before and after settlement;
- negative Exists and Count under incomplete population;
- deterministic ties independent of hash iteration order; and
- a derived view presented with the wrong source document or generation.

The System.Private.CoreLib type graph supplies the scale and depth case. The
OpenTelemetry call graph supplies external traversal, physical occurrence, and
partial-completion cases.

## Required evidence

| Gate | Required claim |
| --- | --- |
| `GraphExecutionReferenceCoversEveryPlan` | Every admitted plan has reference behavior; unsupported plans fail before work |
| `GraphExecutionDirectConsumerRuns` | An independently compiled consumer uses ordinary application payloads to execute a closed-document structural question without a dotnet-inspect dependency |
| `GraphDerivedViewsMatchCanonicalScan` | Every selected outgoing, incoming, relationship, and distinct-neighbor view matches a canonical edge scan |
| `GraphTerminalResultsAreEquivalent` | Exists, Count, Rows, Document, and each admitted Path terminal agree on values, ordering, failures, limits, completion, and receipts where their contracts overlap |
| `GraphCompletionAxesRemainDistinct` | Structural exhaustion cannot manufacture domain completion, and provider completion cannot erase a Graph bound |
| `GraphPositiveEvidenceSurvivesUnrelatedFailure` | Applicable qualification is scoped and healthy evidence remains visible |
| `GraphProviderExpansionIsBatchableAndBounded` | Frontier batches, work bounds, settlement, and empty/partial/failed completion follow the provider contract |
| `GraphSpecializationMatchesReference` | Every specialized topology is compared with reference execution over normal and pathological inputs |
| `GraphExecutionIsPlatformCompatible` | Release tests exercise NativeAOT and single-threaded Browser/Wasm without unsupported dependencies or hidden fallback |
| `GraphProductionAdopterPreservesResults` | Exact base/head product results match for the OpenTelemetry migration and the first degree-based adopter |
| `GraphExecutionPerformanceIsMeasured` | NativeAOT and Browser/Wasm measurements report execution time, allocation where available, work receipts, and every supported terminal on representative and pathological assets |
| Existing `Inspector.Graph` dependency gate | The complete execution implementation remains BCL-only in both the evaluated project graph and compiled assembly references |

`eng/measure-graph-degree.cs` is the NativeAOT scorecard for directed
single-relationship degree execution and unchanged bidirectional and
multi-relationship controls over the real System.Text.Json and CoreLib
type-use graphs. Its `--construction` mode measures validated `GraphDocument`
construction over the same canonical nodes and edges while requiring stable
topology cardinalities and content checksums.

The implementation may split these claims across focused suites, but no
source-text scan or debug-only assertion counts as the gate.

## Non-claims

This design does not:

- define .NET subject, relationship, evidence, producer, role, or completion
  meaning;
- define single-seed, peer-seed, induced-set, focus, or product-scope
  semantics;
- change the structural carrier or its six payload planes;
- make Graph discover methods, calls, packages, or another domain fact;
- make QuerySpace a Graph dependency or express Graph operations as row-query
  syntax;
- define product-named views, CLI commands, Web presentation, or rendering;
- define the semantics of roll-up, strongly connected components, levels,
  weak components, or community detection;
- require every execution to build every structural index;
- promise constant-time lookup, allocation-free traversal, or specialization
  without measured evidence;
- treat document absence as domain absence without applicable population
  completion;
- define concurrent execution or require threads unavailable to
  single-threaded Browser/Wasm; or
- authorize one implementation PR to complete every adoption slice.

## Immediate successors

After this design locks:

1. implement the carrier and permanent dependency gates from #8722;
2. migrate complete-document neighborhood and focus to the reference executor;
3. file the focused distinct-neighbor-degree adoption for #8732;
4. file the QuerySpace-backed provider adoption using the OpenTelemetry
   scenario;
5. specialize only the first measured production topology; and
6. file each additional algorithm required by #8744 as its own focused design
   and adoption.
