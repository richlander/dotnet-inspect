# Producer planning

## Status

Focused design for
[#8568](https://github.com/richlander/dotnet-inspect/issues/8568). It owns
level 3 of a three-level analysis architecture, the **coherent work
description**: which producers a command's request needs, what each of them
needs from its subject, and what they publish.

| Level | Concern | Owner |
| --- | --- | --- |
| 1. Resource sharing and lifetime | One opened subject, borrowed by every analyzer and source; reuse without leaks or double release | [Assembly image lifetime](assembly-image-lifetime.md) (`AssemblyInspectionSession`), [resource ownership and borrowing](resource-ownership-and-borrowing.md); gap tracked in [#8576](https://github.com/richlander/dotnet-inspect/issues/8576) |
| 2. Work ordering and collapsing | Breadth and depth of reads on the subject, merged across all requests; traversal, batching, parallelism, pushdown | [QuerySpace](query-space-library.md) and [source delegation](source-delegation.md); request collapse in [#8574](https://github.com/richlander/dotnet-inspect/issues/8574); method bodies as a source in [#8577](https://github.com/richlander/dotnet-inspect/issues/8577) |
| 3. Work description | The closed, validated set of producers, their requests, dependencies, and published results | This document |

Performance comes mostly from levels 1 and 2. This level makes that possible by
describing work completely and declaratively, so the lower levels can share,
reorder, collapse, and parallelize it without changing its meaning.

No owner has adopted this design yet. Library Body Analysis Execution
([Library body Analysis service](library-body-analysis-service.md)) is the
intended first adopter; Research is the intended second. The adoption
sequence, the `LibraryBodyIndex` drain evidence, and the producer census are
kept in #8568, not here.

Every property below is **unverified** until its gate lands with the first
adoption; see [Verification](#verification).

## Examples

The examples use System.Text.Json 10.0.0, the package the
[operation-participation](analysis-surfaces-and-universes.md#operation-participation)
Diff demo uses.

**A narrow question describes narrow work.** A caller asks whether the library
contains any unsafe evidence. The work description names one producer, safety
evidence, and its one request: every method body, decoded to instructions. No
call classification, allocation, signal, or call-graph producer appears, so
the lower levels are never asked for their data. No separate fast path is
needed for narrow questions to be cheap.

**Two analyses, one description.** `diff` selects `--analysis
call-site,allocation` for `JsonSerializer.Serialize:1`. The analysis catalog
binds each analysis to producer declarations. The work description names both
producers and their requests on the one selected body. Both need decoded
instructions, and call-site also needs call classification. Whether that body
is decoded once and visited by both producers is decided by level 2; this
level states only what each producer needs.

**A query travels with the work.** The `library -S @Performance` triage over
System.Text.Json 10.0.0 is filtered to one namespace, ranked, and cut to the
top ten. The work description carries the row query with the optimization
producer's request. Level 2 can evaluate the namespace predicate from metadata
before reading any body, so it narrows breadth. The ranking and the top-ten cut
need every candidate, so they run over the published rows. The answer equals
running the whole query over every row.

**A consumer owns its interpretation.** The JS export surface's JSON
wire-contract rules need field-store, field-load, and return-flow facts.
Analysis publishes those facts through a flow producer. The wire-contract
meaning is declared by the JS export surface as its own producer at a higher
tier, requiring the flow producer. Analysis never learns that JSON exists.

**Research composes across tiers.** A Research fact producer for one member
requires Analysis allocation evidence and the decompiler's imported IR for that
member. Research's work description includes the Analysis producers it
requires, scoped to exactly that body, and its own producer after them.

## Owner and exact claim

**Producer Planning** owns this exact claim:

> Given a command's request, expressed as producer declarations with
> parameters and a subject scope, the planner produces one work description
> without reading any subject content, or rejects the request as a whole. The
> description names the closed set of producers the request needs, each
> producer's resource requests and dependencies, and the typed results,
> outcomes, and receipt it publishes. Every execution that honors the
> description publishes the results that a serial execution of it would.

This owner defines:

- what a producer declaration states;
- dependency closure, validation, and whole-request rejection;
- the tier rule and how a higher-tier request includes lower-tier producers;
- the producer contract: what a producer may read, retain, and publish;
- the shape of per-producer outcomes and of the participation receipt; and
- the requirements this level places on levels 1 and 2.

This owner does not define:

- opening, sharing, retaining, or releasing subjects (level 1);
- read ordering, request collapse, batching, traversal, parallelism,
  scheduling, pushdown, or per-unit data lifetime (level 2);
- any producer's algorithm, evidence semantics, or result type;
- analysis identity, operation participation, default sets, cost, or
  discovery, which the
  [analysis catalog](analysis-surfaces-and-universes.md#operation-participation)
  and [capability composition](inspection-capability-composition.md) own;
- [package read demand](package-read-demand.md), which consumes declared
  requests but keeps its own decision;
- cache keys, retention, or persistence;
- Research target resolution, admission, or correspondence;
- decompiler IR transforms or their ordering; or
- presentation, Findings, or envelopes.

## The model

A **producer** computes one owner's evidence. It is described by a
**declaration**: a statically constructed value that states the producer's
identity and version, its tier, the unit it visits, its resource requests, the
producers it depends on, the parameters that distinguish one request from
another, its input footprint, and the type of the result it publishes. A
consumer names the declaration to request the producer and names it again to
read the typed result. There is no lookup by runtime type, string, or
position.

A **resource request** is a QuerySpace request against the subject that owns
the data: its breadth (which units, including any declared expansion) and its
depth (which layers of each unit, such as decoded instructions or a
control-flow graph), with a terminal. Requests use level 2's vocabulary. This
level never merges them.

A **unit** is what one visit covers; the first unit kind is one physical method
body. A producer may also declare a **completion**, which runs after every unit
in its scope has been visited and combines the per-unit facts into the
published result. Whole-library facts such as leverage or a local call graph
are completions, and a completion may depend on other producers' completed
results.

A **work description** is this level's output: the closed producer set, each
producer's requests, the dependency edges among producers and completions, and
the result, outcome, and receipt shapes. It is data. It can be inspected,
explained, compared, and handed to level 2 before any work begins.

## Planning

The planner closes the requested declarations over their dependencies, then
validates the whole closure without reading subject content. A request is
rejected as a whole, with one typed reason for each offending declaration,
when a dependency names nothing, when dependencies form a cycle, when a
declaration depends on a higher tier, or when two requested declarations with
the same identity have different parameters. Validation does not drop,
substitute, or narrow entries.

Dependency edges are the only order this level imposes. Ties are broken by
declaration identity, never by the order in which a consumer listed producers
or a module registered them. Any other order is level 2's choice.

A producer that needs units outside its requested breadth, such as the lifted
state-machine body of a selected async method, declares that expansion with an
owner-issued reason. The request carries it; level 2 carries it out.

## The producer contract

These rules bind every producer. Each one is cheap to hold from the start and
expensive or impossible to add later, because it is what lets levels 1 and 2
deliver their gains without changing results. QuerySpace is the local
precedent: its plans are structural data, not opaque delegates, which is what
lets work collapse into a source. A LINQ-shaped design cannot be tuned into
that later, because the information the optimizer needs was never captured.

### Demand is declared before work begins

**Rule.** A producer declares every request and dependency statically. No
producer discovers a new need while it runs; a conditional need is an optional
request stated in the declaration.

*Lets the lower levels:* compute collapse, cost, read demand, and pushdown
before the first byte is read.

*Lesson:* LLVM's legacy pass manager and Roslyn's runtime callback
registration show how much a scheduler loses when needs surface only during
execution. `LibraryBodyIndex`'s lazy members are the local instance.

### Requests are QuerySpace requests, never merged here

**Rule.** A producer's need for shared data is a QuerySpace request with
QuerySpace meaning, including any row query a consumer supplies. Exists and
Count are observations of the Rows of the same population. This level does not
merge, deduplicate, or reduce requests, and it does not decide what is pushed
down; QuerySpace and the source do that for every consumer.

A producer's row vocabulary is owned with its result type, is host-neutral,
and is bound through QuerySpace composition. A host binds and presents it; a
host never defines it.

*Lets the lower levels:* share one read among producers and other consumers,
and push work toward the source in every host.

*Lesson:* a query applied after everything is built can never make work
cheaper. That is LINQ in QuerySpace clothing: the query is declared
structurally, but the source still builds every row before any predicate,
Count, or limit runs. A second planner beside QuerySpace would repeat the
optimizer for one consumer family. #8571 records today's drift.

### Producers are read-only and communicate only through declared results

**Rule.** A producer does not mutate what it reads, does not keep state that
spans units, and observes another producer only through that producer's
declared result. Anything shared is a level 1 or level 2 input, never a
producer's lazily initialized field.

*Lets the lower levels:* run units in parallel, visit independent producers
concurrently, and fuse producers into one traversal.

*Lesson:* GCC's global compiler state is the classic barrier to parallel
compilation, and Roslyn had to add concurrent execution as an opt-in because
existing analyzers were not safe. The decompiler states the local form for its
passes: they "communicate through the tree, never side-channel state".

### Producers visit; they do not iterate

**Rule.** A producer is given units. No producer loops over the subject
itself.

*Lets the lower levels:* cancel and bound work at unit boundaries, yield on
single-threaded Browser/Wasm, report progress, attribute time per producer,
and schedule in parallel, all without producer changes.

*Lesson:* Roslyn's operation callbacks and Go's shared `inspect` traversal give
N analyzers one walk. Analyses that walk the program themselves cannot be
interrupted, parallelized, or fused.

### Nothing outlives its visit except the published result

**Rule.** A producer copies whatever it publishes out of the unit's data during
its visit. No result or completion refers to a unit's data after the visit.

*Lets the lower levels:* discard each unit's data after the last visit, so
memory is bounded by the largest body.

*Lesson:* whole-program analyzers that retain every method's IR run out of
memory before they run out of time.

### Results are detached, comparable, and keyed by their inputs

**Rule.** A published result retains no reader, lease, resolver, or stream. It
has defined equality. Its declared input footprint is one of: the unit alone,
the unit plus its declaring module's metadata, or resolved references beyond
the module. A result is a function of that footprint, the declaration identity
and version, the scope, and the parameters.

*Lets the lower levels:* reuse results within one Workspace realization,
persist them through the cache port, and reuse per-body results across package
versions wherever a body's footprint is unchanged.

*Lesson:* Roslyn replaced `ISourceGenerator` with `IIncrementalGenerator`
because the original's values could not be compared or cached. Go's analysis
facts are serializable by contract, which allows separate-process drivers.

### Outcomes are per producer and typed

**Rule.** Each producer's result carries its own outcome: not requested,
complete, incomplete with an owner-issued limitation, or failed. A failure is
contained to that producer. Each dependent receives a typed prerequisite
failure rather than a missing value, and independent producers are
unaffected.

*Lesson:* Roslyn contains a crashing analyzer as a diagnostic instead of
failing the compilation. Retrofitting an outcome shape onto result types that
never had one touches every consumer.

### Participation is observed, not declared

**Rule.** This level defines the receipt's shape: for each producer and each
requested depth layer, the units attempted and how each attempt ended. Level 2
records it where work actually starts and ends; producers never report it.

*Lesson:* LLVM's `-time-passes` comes from the pass manager, not the passes.
The
[selective implementation metric](library-body-analysis-service.md#selective-implementation-metric-analysis)
receipt is a hand-built instance for one producer family.

### Analysis and rewriting are separate

**Rule.** This design covers read-only producers only. A component that
rewrites a representation, such as the decompiler's IR passes, keeps its own
ordered pipeline and, if it shares analyses, its own invalidation contract.

*Lesson:* analysis invalidation is one of the largest bug classes in LLVM's
pass managers, and it exists only because rewriting and analysis share one
cache.

### Composition is static and identities stay separate

**Rule.** Declarations are values in code. A work description contains only
declarations reachable from its request. Nothing is discovered by reflection,
scanning, or registration side effects. A producer's identity is an internal
code identity with a version. It is never an
[analysis catalog](analysis-surfaces-and-universes.md#analysis-identity)
identity; the catalog binds its manifest-grade identities to declarations.

*Lesson:* order-by-registration and name-keyed discovery make behavior depend
on link order and spelling. Research's string-keyed producers show the axis
that [Assembly Inspection Query](assembly-inspection-query.md#prior-art-the-research-producer-registry)
already flagged.

## Requirements on the lower levels

This level relies on the following. Each requirement names what the work
description needs; the owning level decides how to meet it.

### Level 1: resource sharing and lifetime

1. One immutable image per subject for the whole life of the work. Analysis
   never opens or reopens a subject by path.
2. Every producer and source borrows the subject. The lease outlives all work
   that reads it, and detached results outlive the lease without retaining it.
3. Reuse and joins use session or content identity, never the reference
   identity of a derived result object.
4. Release happens once and deterministically, however many producers
   borrowed.

Today analysis owns its own reader lifetime for each execution, and Research
shares state by `LibraryBodyIndex` instance identity. #8576 tracks closing that
gap.

### Level 2: work ordering and collapsing

1. Accept each producer's resource requests and the work description's
   dependency edges, and collapse requests across producers and other
   consumers.
2. Visit each unit's producers in an order consistent with those edges, and
   run each completion only after its inputs complete.
3. Contain a failing visit to that producer and its dependents.
4. Publish results equal to a serial execution of the work description,
   whatever ordering, batching, collapse, or parallelism it uses.
5. Record actual participation in the receipt shape defined here.

Request collapse is tracked in #8574, and method bodies as a source in #8577.

## Tiers

A **tier** is a layer of producers with the same dependency position. Analysis
producers form the lowest tier. Research and consumer-owned interpretations,
such as the JSON wire contract, form higher tiers.

- A declaration may depend on its own tier or a lower tier, never a higher
  one. Analysis therefore never depends on Research or on any consumer.
- A higher-tier request includes the lower-tier producers it depends on, with
  the scope and parameters it needs. The lower tier's declarations are used
  unchanged and know nothing of the requester.
- A higher tier may declare its own resource requests. Research's imported
  decompiler IR is a Research-tier request, not an Analysis concern.

Research is therefore not only a consumer. It consumes Analysis results and
describes its own producers with the same declarations. That replaces the
parallel machinery it has today: string-keyed producer dependencies,
requirement unions expressed as Analysis feature bits, and a memoized context
keyed by one `LibraryBodyIndex` instance. How Research adopts this, and what
happens to its session and admission contracts, is Research's own focused
effort.

This contract names no Analysis, Research, or decompiler type, so it can move
below all of its adopters without redesign. Where it lives physically is
decided when the second tier adopts it.

## Relationship to adjacent owners

| Owner | Relationship |
| --- | --- |
| [Library body Analysis service](library-body-analysis-service.md) | First adopter. Its producer coordination, features, and fixed result slots become declarations and a work description; its focused result types are unchanged. |
| [Analysis catalog and operation participation](analysis-surfaces-and-universes.md#operation-participation) | Selects manifest-grade analyses and binds each to declarations. It owns cost, defaults, and discovery. |
| [Assembly image lifetime](assembly-image-lifetime.md) and [resource ownership](resource-ownership-and-borrowing.md) | Level 1. Supplies and tracks the borrowed subject. |
| [QuerySpace](query-space-library.md) and [source delegation](source-delegation.md) | Level 2. Owns request meaning, collapse, and completion evidence, and plans reads against sources. |
| [Package read demand](package-read-demand.md) | Consumes the declared requests in a work description. |
| [Stateless core services](stateless-core-services.md) and [analysis index cache](analysis-index-cache.md) | Own retention and caching of the detached results. |
| Research ([ownership paths](generic-research-ownership-paths.md), [assembly context](research-assembly-context-ownership.md)) | Intended second adopter as a higher tier. |
| Decompiler IR passes | Separate rewriting pipeline. Not an adopter. |

## Prior art

Surveyed as evidence, not authority. No code or schema is transferred.

- Go `golang.org/x/tools/go/analysis`: analyzers as values with declared
  requirements and typed results, a shared traversal, serializable facts, and
  several drivers over one contract. This is the closest match.
- Roslyn analyzers and incremental generators: callback registration over one
  walk, per-analyzer failure containment, concurrency added late as an opt-in,
  and an API replacement forced by values that could not be compared.
- LLVM's new pass manager and MLIR: declared analyses and manager-owned
  instrumentation, and the cost of invalidation.
- Research's `ResearchFactRegistry` and this repository's decompiler pipeline:
  local precedents for declared dependencies and for an ordered pass list that
  serves as the architecture document.

The sharing and collapse precedents (multiple-query optimization, shared scans,
Haxl, DataLoader) belong to level 2 and are surveyed on #8574.

## Verification

These gates land with the first adoption and run in Release. Until then, every
property above is **unverified**.

- **Description without bytes:** a work description and its requests are
  computed for a real package without opening any subject content.
- **Whole-request rejection:** cycles, missing dependencies, upward-tier
  dependencies, and conflicting parameters are each rejected with typed
  reasons, and nothing runs.
- **Undeclared access fails visibly:** a producer that reads a result or data
  layer it did not declare fails instead of receiving a value.
- **Minimum description:** a single-producer request's description and receipt
  contain no unrequested producer.
- **Failure containment:** an injected producer failure leaves independent
  producers' results unchanged and gives dependents a typed prerequisite
  failure.

Equivalence between executors and pushdown equivalence are level 2 gates,
tracked in #8577 and #8574.

Whether the static-composition rule gets full, partial, or no gate coverage is
an open decision for the operator, under the
[absence-claim rule](../evidence-and-validation.md#absence-claims-choose-their-coverage).
