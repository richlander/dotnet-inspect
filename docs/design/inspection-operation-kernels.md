# Inspection operation kernels

## Status, owner, and claim

Status: **design contract** for
[#8669](https://github.com/richlander/dotnet-inspect/issues/8669).

The **Inspection Operation Kernel** pattern owns this claim:

> A domain-neutral inspection operation kernel executes one resolved
> structural plan over caller-supplied typed data or providers and returns one
> owner-defined terminal result with completion, limits, and failures intact.
> Product composition selects subjects, evidence producers, capabilities, and
> lifetimes; the kernel does not acquire or interpret the inspected domain.

Issue #8669 authorizes this cross-owner pattern. Its selected coverage is a
full permanent gate for the future `Inspector.Graph` dependency boundary.

This is one cross-cutting pattern owner. It defines the layer boundary and
typed handoff that focused operation substrates may adopt. It does not redefine
QuerySpace plans, Graph topology, Finding correspondence, Analysis evidence,
Research interpretation, Workspace lifetime, host interaction, or rendering.

The contract is unverified until the first focused adopter supplies the gates
under [Required evidence](#required-evidence). Existing QuerySpace and
`Inspector.Findings` behavior is supporting evidence, not implementation proof
for another kernel.

## Purpose

dotnet-inspect has three different kinds of reusable work:

- evidence producers establish facts, relationships, and correspondence;
- broad operations add population, topology, or arity to that evidence; and
- product composition realizes subjects, authorizes work, and delivers results
  to CLI and browser hosts.

Those roles should compose without making the producer own an operation or
making the operation understand every inspected domain. The target layering is:

```text
producer-owned evidence
  Analysis | Metadata | CallGraph | packages
                              |
                              v
domain-neutral operation substrate
  QuerySpace | Inspector.Findings | future Inspector.Graph
                              |
                              v
product composition
  ILInspector.Research | DotnetInspector.Queries
                              |
                              v
hosts
  DotnetInspect.Cli | DotnetInspect.Web
```

The arrows are typed handoffs, not one universal operation plan or result.
Dependency altitude alone does not decide ownership. A high-level operation
kernel can remain domain-neutral, while a low-level producer can remain the
authority for sophisticated domain evidence.

## User outcome

The motivating experience starts with a normal inspection question:

```text
Which exits from this scope eventually reach a subject selected as an
Integration type?
```

QuerySpace can select the target population and applicable relationship
providers. Graph can walk those relationships and retain the evidence-backed
target corridor. Analysis, Metadata, CallGraph, package, and Integration owners
keep the meaning of every relationship and observation. The CLI and Inspect Web
receive the same typed result without either host rebuilding the operation.

The pattern serves that experience by keeping the operation reusable and
compiler-visible without flattening deep inspection evidence into generic
strings, objects, or callbacks.

## Four layers

### Evidence producers

An evidence producer owns:

- the meaning and identity of its observations;
- the inspected subject and evidence population;
- producer-specific decoding, analysis, matching, and inference;
- positive evidence, unavailable or failed outcomes, and producer completion;
  and
- any relationship direction, correspondence, or occurrence receipt it
  establishes.

`ILInspector.Analysis` is one producer, not the definition of the producer
layer. Metadata, CallGraph, package, and Integration components also issue
evidence under their own contracts.

A producer may use a domain-neutral substrate internally. That use does not
transfer the producer's semantics to the substrate.

### Domain-neutral operation substrate

A domain-neutral operation substrate owns reusable structures or execution
mechanics intrinsic to inspection but independent of both IL-bearing programs
and the broader .NET ecosystem.

Current examples have different shapes:

- `QuerySpace` owns portable query intent, structural plans, reusable
  execution, and terminal composition.
- `Inspector.Findings` owns Finding and comparison information structures,
  including `AnalysisDiff<T>`.
- Target `Inspector.Graph` may own graph structures, reference traversal,
  focus/path algorithms, and terminal execution.

These components form an operation **stratum**, not one
`Inspector.Orchestrators` assembly. Each keeps a focused contract, dependency
surface, lifetime, and independent consumer story.

An information structure is not automatically an execution kernel.
`AnalysisDiff<T>` is a complete producer-issued relation document; it does not
perform domain matching. No `Inspector.Diff` project is justified merely for
symmetry with QuerySpace or Graph.

### Domain composition

Domain composition owns:

- selected Package, Library, Type, Member, endpoint, or Workspace subjects;
- finite evidence universes and retained generations;
- producer selection and execution;
- capability, cost, and work-bound authorization;
- binding owner-issued evidence into an operation request; and
- delivery through the operation's typed result and
  `InspectionEnvelope<TContent>`.

`DotnetInspector.Queries` is the ordinary product composition layer.
`ILInspector.Research` may use operation substrates to derive richer
program-evidence interpretations while remaining anchored to the IL domain. A
Research component that publishes a derived Finding owns that new evidence,
but still composes its lower-level inputs in this layer. Neither Research nor
Queries becomes a generic service locator.

### Hosts

CLI and browser hosts own gestures, interaction, host lifetimes, and
presentation. Both consume the same product-composed operation result.

Hosts do not:

- select topology by reparsing labels;
- recreate producer correspondence;
- define a private operation vocabulary;
- infer completeness from an empty result; or
- duplicate a reference interpreter for host convenience.

## Kernel contract

A kernel execution has four independently owned inputs:

```text
KernelExecution
  StructuralPlan
  DataOrProviderBindings
  ExecutionScope
  TerminalRequirement
```

This is a conceptual contract, not a frozen CLR API.

### Structural plan

The structural plan is immutable data describing work already validated by its
semantic owner. It retains owner-issued descriptor, operation, stage, order,
mode, direction, or relationship identities as applicable.

The kernel may inspect only the structural vocabulary it owns or imports
explicitly. It does not parse command text, infer semantics from display
labels, or accept arbitrary executable content in a portable plan.

Runtime-authored plans remain structural data. The public plan type does not
become a nested generic pipeline whose CLR type changes for every runtime
combination.

### Data or provider bindings

Bindings connect plan roles to caller-owned typed data or providers. The
caller retains the subject and evidence semantics. A binding supplies the
stable identity, comparison, adjacency, or value access required by the
kernel's declared algorithm.

A binding is not an untyped result bag. The kernel must not discover
capability through `object`, reflection, string naming, or a type-keyed service
locator.

### Execution scope

The caller supplies the finite execution scope and any borrowed lifetime. A
kernel neither widens that scope nor acquires another subject.

When bindings observe a retained Workspace or another mutable owner, every
binding in one execution uses the same owner-issued generation, snapshot, or
receipt currency. The detached terminal result does not retain a live
Workspace, reader, stream, lease, or provider callback unless its focused
owner explicitly defines a borrowed result contract.

### Terminal requirement

A terminal requirement names the result the caller needs. Terminal identity,
unit, and semantics remain owner-issued.

Rows, exact Count, graph Document, reachability, or path results are not
interchangeable merely because one can sometimes be derived from another. A
kernel may avoid materializing values only when it can produce the requested
terminal with equivalent completion, failure, and ordering behavior.

A generic `Count` without an owner-defined unit is invalid. Graph counts may
refer to nodes, logical edges, occurrences, exits, paths, or another declared
population; the operation owner names the unit before execution.

Avoiding document-value materialization does not make Count constant-time or
allocation-free. Exact Graph Count may still require complete traversal and
visited-state work.

## Reference execution and specialization

Every adopted kernel has one complete reference interpreter. It defines
observable plan, ordering, failure, completion, and terminal behavior for every
supported structural plan.

An optional specialized kernel may recognize a closed plan topology and use
statically typed providers, struct execution state, fused loops, or generated
dispatch. Specialization:

- changes performance only;
- adds no operation, relationship, predicate, mode, stage, or terminal;
- may decline before observable execution begins;
- returns the same typed result and completion as reference execution; and
- never falls back silently after accepted execution has produced effects.

The caller may use
[Source Delegation](source-delegation.md) when a provider executes an accepted
prefix or terminal. Its effect, completion, and equivalence rules remain
authoritative; this pattern does not create a weaker graph-specific fallback.

Compiler transparency is an execution goal, not a requirement that all plans
be compile-time constants. A runtime structural dispatcher may select a known
generic kernel. Unsupported topologies use the reference interpreter.

An `IEnumerable<T>` adapter may be convenient input, but the hot execution
contract must not require interface enumeration, captured delegates, boxing,
or virtual dispatch when a statically bound provider can expose the same work
to the compiler.

## Graph and QuerySpace composition

Graph and QuerySpace remain separate owners. Their composition has three
stages.

### Before traversal

Product composition may use QuerySpace to select:

- graph seeds or peers;
- a finite target population;
- relationship-provider bindings;
- requested characteristics; or
- a terminal and row intent admitted by the Graph operation.

The QuerySpace request is resolved before Graph execution. Graph does not parse
portable query text or infer a QuerySpace facet from a graph descriptor.

### During traversal

A QuerySpace-backed relationship provider may supply one typed expansion batch
for a Graph frontier:

```text
Graph frontier
  -> resolved provider binding
  -> subjects and relationships
  -> occurrences and evidence
  -> limits, failures, and completion
  -> next Graph frontier
```

The provider owns relationship construction and evidence. Graph owns frontier
discipline, visited topology, traversal direction, path/focus policy, and
Graph-terminal construction.

Expansion must be batchable over one frontier. A composition that requires one
general QuerySpace execution per visited node is not the default contract; an
adopter must justify that work shape and its bounds explicitly.

An empty expansion establishes no absence unless the provider's completion
evidence covers the applicable relationship and subject population.

Graph traversal bounds and QuerySpace row limits remain distinct. A composer
may translate between them only when both owners name the same population and
completion rule. Otherwise a provider row limit yields partial expansion; it
does not complete a deliberately smaller graph.

The core `Inspector.Graph` contract need not reference QuerySpace. A focused
bridge may reference both substrates when it has more than one concrete
composition point; otherwise `DotnetInspector.Queries` owns the product
binding. Either shape preserves the same typed handoff and may not duplicate
either owner's semantics.

### After traversal

QuerySpace may select, order, or count declared graph rows after the Graph
result exists. This is reference composition and does not reduce graph work or
prove absence in a population the traversal did not complete.

If Graph can satisfy a QuerySpace Rows or exact Count terminal without
materializing the complete document, the Graph owner adopts Source Delegation
for that terminal. The delegated result must prove equivalence to the
complete-document reference path, including ordering, strict-stage failures,
limits, and completion.

## Graph adoption boundary

The first focused adopter is the target
[`Inspector.Graph` boundary](inspector-graph-library-boundary.md). Its first
transfer is one cohesive responsibility: the domain-neutral structural
document currently carried by `DotnetInspector.Queries`. Reference execution
remains the next focused transfer.

Candidate responsibilities for that focused transfer include:

- document-local nodes, groups, logical edges, and occurrences;
- relationship and characteristic descriptors;
- graph mode and traversal request mechanics;
- generic focus/path projection over supplied classifications; and
- reference and terminal execution.

The focused Graph design must decide the exact transfer. This pattern does not
freeze those CLR types or authorize a mechanical move.

The following responsibilities remain with their current domain owners:

| Responsibility | Retained owner |
| --- | --- |
| Call decoding, call relationships, dispatch, and physical call sites | Analysis and CallGraph |
| Metadata declarations, hierarchy, signatures, and API visibility | Metadata |
| Package identity, selection, and dependency evidence | Package owners |
| Integration meaning and candidate policy | Integration and Research owners |
| Workspace generations, borrowing, and acquisition | Workspace and House owners |
| Analysis-set selection, cost, capabilities, and product operation binding | `DotnetInspector.Queries` |
| CLI and browser interaction and lowering | Their host owners |

Current `InspectionGraphDocument` subject identities directly reference
CallGraph, Analysis, Metadata, acquisition, and Integration types. Extraction
must preserve those owner-issued identities through typed bindings. It must not
replace them with display text, `object`, a universal subject identity, or a
type-keyed result bag.

PR [#8453](https://github.com/richlander/dotnet-inspect/pull/8453) is a
precursor: it moved reusable focus selection out of CallGraph, adapted one
complete bounded call projection into Inspection Graph, and gave CLI and Web
the same host-neutral result. It did not adopt the future project boundary.

## Diff and Findings boundary

`Inspector.Findings` already owns domain-neutral Finding and comparison
information. `AnalysisDiff<T>` retains complete endpoint sequences and
producer-issued relations; the producer, not the format, owns matching.

The product Diff operation remains composition:

- endpoint and population realization;
- selected analysis set;
- producer execution;
- correspondence supplied under each analysis owner's contract;
- comparison-document assembly; and
- host delivery and presentation.

A future `Inspector.Comparison` or Diff execution kernel requires a focused
claim, at least two concrete consumers, and reusable mechanics not already
owned by `Inspector.Findings`. Symmetry with Graph is not sufficient.

`ILInspector.ILDiff` remains IL-specific because its public contract acts on
decoded method bodies and assembly evidence.

## Naming boundary

The naming rules in
[Library family boundaries](library-family-boundaries.md) remain
authoritative.

- `Inspector.*` is the family for subject-neutral inspection substrate.
- `ILInspector.*` owns IL-bearing program evidence.
- `DotnetInspector.*` owns broader .NET ecosystem subjects and product
  composition.
- An independent root names a focused domain coherent outside inspection.

This pattern supports `Inspector.Graph` as the target name only after its
focused boundary proves that its public contract is subject-neutral. It does
not authorize a broad rename.

`DotnetInspector.SourceDelegation` is a candidate for a separately reviewed
family correction because its contract is generic and currently carries no
domain dependency. `QueryOverflow` is instead a QuerySpace companion. Both
need focused consumer and dependency censuses before any rename.

## Conventional basis

The pattern combines established designs without copying one whole model:

| Design | Evidence adopted | Deliberate boundary |
| --- | --- | --- |
| QuerySpace | Runtime structural plans, a complete reference interpreter, static specialized kernels, and terminal-specific execution | Operation kernels do not absorb QuerySpace vocabulary or require every operation to be a query |
| Boost Graph Library graph concepts and property maps | Algorithms can consume graph capabilities and associated data without owning one graph representation | This design retains typed evidence, completion, failures, and runtime-authored plans rather than adopting C++ concepts or code |
| LINQ | Declarative selection and terminal roles are familiar to .NET callers | Interface enumeration and higher-order callbacks are optional adapters, not the required hot execution shape |
| System.Text.Json source generation | Generated typed witnesses can remove reflection and dispatch while preserving one public contract | Generated code does not define another operation language or authenticate another generator |
| Source Delegation | Optimized execution substitutes only with accepted completion evidence and reference equivalence | Each operation retains its own terminal and provider semantics |

Relevant external references:

- [Boost Graph Library graph concepts](https://www.boost.org/doc/libs/release/libs/graph/doc/graph_concepts.html)
- [.NET LINQ overview](https://learn.microsoft.com/dotnet/standard/linq/)
- [System.Text.Json source generation](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)

## Production adoption

Adoption is staged by owner:

1. **Pattern:** this document locks the operation-kernel handoff.
2. **Graph boundary:** the
   [focused design](inspector-graph-library-boundary.md) establishes
   `Inspector.Graph`, its dependencies, public contracts, direct
   external-consumer fixture, and structural transfer from Queries.
3. **Graph reference execution:** one focused slice migrates the complete
   document and current focus behavior without adding specialization.
4. **QuerySpace bridge:** one focused slice binds a real Graph relationship
   provider and preserves complete-document reference behavior.
5. **Compiler-transparent terminal:** one measured slice specializes a
   production terminal and proves exact equivalence and NativeAOT performance.
6. **Host adoption and retirement:** CLI and Inspect Web consume the same
   extracted result through C# and TypeScript call sites, then superseded
   Queries-owned graph mechanics retire.
7. **Diff census:** a separate issue determines whether reusable Diff execution
   mechanics exist beyond `Inspector.Findings`; no project is created without
   that evidence.
8. **Naming audits:** Source Delegation and QueryOverflow are considered
   independently.

The OpenTelemetry external-focused call view is the first real Graph scenario.
Its complete semantic document, edge Rows, and exact Count expose the terminal
distinction. The neighboring induced Integration graph and direct-use Graph
provide non-call shapes that prevent fitting the boundary only to CallGraph.

## Required evidence

The pattern and future adoptions require:

1. **Full dependency-boundary coverage.** A Release architecture gate inspects
   both the production project graph and built assembly references. The future
   `Inspector.Graph` assembly must reference no `ILInspector.*`,
   `DotnetInspector.*`, or `DotnetInspect.*` assembly.
2. **Direct consumer.** An independent .NET fixture uses application-owned
   subject and relationship types without an `ILInspector.*` or
   `DotnetInspector.*` dependency.
3. **Reference behavior.** Contract tests cover empty, cyclic, disconnected,
   unknown, failed, truncated, and deterministic-tie topology.
4. **Specialization equivalence.** Every specialized plan topology is compared
   with the reference interpreter for Document or Rows, exact Count, limits,
   failures, and completion.
5. **Provider completion.** Empty, partial, failed, and bounded expansion
   batches cannot become complete graph absence.
6. **Snapshot coherence.** A composed execution uses one owner-issued
   generation or snapshot across every provider binding.
7. **Terminal distinction.** Exact Count does not materialize document values
   when its adopted specialization claims that benefit; Document or Rows still
   retains complete values and evidence.
8. **NativeAOT production evidence.** Every modernization reports exact
   base/head NativeAOT behavior and performance for every supported terminal
   under
   [the evidence contract](../evidence-and-validation.md#nativeaot-beforeafter-for-modernization).
9. **Both hosts.** The extracted shared result reaches CLI and Inspect Web;
   tests exercise the C# and TypeScript call sites and preserve typed
   diagnostics and format lowering.

Until a gate lands and runs in Release, its property is **unverified**.

No source-text scan substitutes for the dependency gate. The full gate follows
compiled and project dependency evidence rather than treating repository
contributors as hostile.

## Non-claims

This pattern does not:

- define one universal operation plan, result, subject identity, relationship,
  evidence value, or terminal;
- create an `Inspector.Orchestrators` assembly;
- make every query a graph edge or every graph traversal a query;
- move Analysis, Metadata, CallGraph, package, Integration, Research, Diff, or
  QuerySpace semantics;
- define Graph node, relationship, focus, path, or rendering semantics;
- require every operation to expose Count or support specialization;
- permit hidden fallback after an optimized execution begins;
- add runtime plugins, reflection discovery, expression trees, or arbitrary
  executable portable content;
- change CLI grammar, command defaults, output formats, or Workspace lifetime;
- rename Source Delegation, QueryOverflow, or any existing project; or
- authorize one PR to extract Graph, add QuerySpace execution, specialize
  terminals, migrate both hosts, and retire the old path.

## Immediate successors

After this pattern locks:

1. implement the focused `Inspector.Graph`
   [library boundary](inspector-graph-library-boundary.md);
2. file the Graph reference-execution and typed-provider design;
3. file the Graph/QuerySpace composition adoption using the OpenTelemetry
   scenario;
4. file the compiler-transparent terminal specialization only after the
   reference path is production-backed; and
5. perform the Diff and naming censuses independently.

Each successor names one architectural owner and links this pattern only for
the handoff it adopts.
