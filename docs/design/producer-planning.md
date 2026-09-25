# Producer planning

## Status

Focused pattern design for
[#8568](https://github.com/richlander/dotnet-inspect/issues/8568). It defines
one contract: how evidence producers declare what they need and publish, how a
plan is computed from those declarations before any body is read, and what one
execution of that plan means.

No owner has adopted the pattern yet. Library Body Analysis Execution
([Library body Analysis service](library-body-analysis-service.md)) is the
intended first adopter; Research is the intended second. Each adoption is its
own focused effort and states its own decisions. The adoption sequence, the
`LibraryBodyIndex` drain evidence, and the census of current producers live
in [#8568](https://github.com/richlander/dotnet-inspect/issues/8568), not here.

Every property below is **unverified** until its gate lands with the first
adoption; see [Verification](#verification).

## Examples

The examples use System.Text.Json 10.0.0, the package the
[operation-participation](analysis-surfaces-and-universes.md#operation-participation)
Diff demo uses.

**A narrow question stays narrow.** A caller asks whether the library contains
any unsafe evidence. It requests one producer, safety evidence. The plan, which
exists before the first byte of the library is read, contains that producer and
the one substrate it requires, decoded instructions. No call classification,
allocation analysis, signal derivation, or call graph appears in the plan, so
none runs. No separate fast path is needed to answer a narrow question cheaply.

**Two analyses share one pass.** `diff` selects `--analysis
call-site,allocation` for `JsonSerializer.Serialize:1`. The analysis catalog
maps each analysis to producer declarations. For each endpoint the planner
computes one plan. Both producers require decoded instructions, and call-site
additionally requires call classification. Execution decodes the one selected
body once and visits both producers. The receipt records that each producer
participated for one body and that each substrate was built once.

**A consumer owns its interpretation.** The JS export surface's JSON
wire-contract rules need field-store, field-load, and return-flow facts. Analysis publishes
those facts through a flow producer. The wire-contract meaning is declared by
the JS export surface as its own producer at a higher tier, requiring the flow
producer. Analysis never learns that JSON exists.

**Research composes across tiers.** A Research fact producer for one member
requires Analysis allocation evidence and the decompiler's imported IR for that
member. Research's plan lowers its Analysis requirements into an Analysis
request scoped to exactly that body. A composed execution decodes the body once
and visits the Analysis producers and then the Research producer, which joins
their facts by IL offset.

**A single-threaded host gets the same answer.** Inspect Web runs the same plan
on Browser/Wasm with an executor that yields between bodies. It publishes the
same results and receipt facts as the desktop parallel executor, because both
are substitutions for one reference execution.

## Owner and exact claim

**Producer Planning** owns this exact claim:

> Given the producer declarations a consumer requests and an explicit body
> scope, the planner computes, without reading any body, the closed set of
> producers, shared substrates, and effective scope the request requires, or
> rejects the request as a whole. The reference execution of that plan visits
> each body in effective scope once, gives each planned producer read-only
> access to that body's shared substrates, and publishes each producer's
> detached typed result with one receipt of actual participation. Any other
> executor is admissible only when it publishes the same results and receipt
> facts as the reference execution.

This owner defines:

- what a producer declaration states;
- the tier rule and how a higher-tier plan lowers into lower-tier requests;
- plan construction, validation, scope expansion, and whole-request rejection;
- reference execution semantics: units, phases, ordering, substrate lifetime,
  isolation, and failure containment;
- the substitution rule for every other executor; and
- the participation receipt.

This owner does not define:

- any producer's algorithm, evidence semantics, or result type;
- analysis identity, operation participation, default sets, cost, or discovery,
  which the [analysis catalog](analysis-surfaces-and-universes.md#operation-participation)
  and [capability composition](inspection-capability-composition.md) own;
- acquisition or [package read demand](package-read-demand.md), which consumes
  declared requirements but keeps its own decision;
- cache keys, retention, or persistence, which
  [stateless core services](stateless-core-services.md) and each cache owner
  keep;
- Workspace admission, snapshots, or lifetime;
- Research target resolution, admission, or correspondence;
- decompiler IR transforms or their ordering; or
- presentation, Findings, or envelopes.

## The model

A **producer** computes one owner's evidence. It is described by a
**declaration**: a statically constructed value that states the producer's
identity and version, its tier, the unit it visits, the substrates and other
producers it requires, the parameters that distinguish one request from another,
and the type of the result it publishes. A consumer names the declaration to
request the producer and names it again to read the typed result. There is no
lookup by runtime type, string, or position.

A **substrate** is shared, derived per-unit data that more than one producer may
need, such as decoded instructions, a control-flow graph, call classification,
or declared-source attribution. A substrate is itself declared and owned by the
component that owns its facts; the
[instruction substrate](instruction-substrate.md) owns decoding and block
construction. Producers do not own substrates and do not build them for each
other.

A **unit** is what one visit covers. The first unit kind is one physical method
body. A producer may also declare a **completion**, which runs once after every
unit in effective scope has been visited and combines the per-unit facts into
the published library result. Whole-library facts such as leverage or a local
call graph are completions, and completions may require other producers'
completed results.

A **plan** is the planner's output: the closed producer set, the substrates
each unit needs, the effective scope with the reason for every expansion, and
the order in which producers are visited. A plan is data. It can be inspected,
explained, compared, and handed to acquisition before execution begins.

A **receipt** records what actually happened, as opposed to what the plan
permitted. It is written by the executor, not by producers.

## Planning

The planner closes the requested declarations over their requirements, then
validates the whole closure before any body is read. A request is rejected as a
whole, with one typed reason for each offending declaration, when a requirement
names nothing, when requirements form a cycle, when a declaration requires a
higher tier, or when two requested declarations with the same identity have
different parameters. Validation does not drop, substitute, or narrow entries.

Visit order within a unit follows requirements only. Ties are broken by
declaration identity, never by the order in which a consumer listed producers or
a module registered them.

Scope has three states: requested, expanded, and effective. A producer that
needs bodies outside the requested scope, such as the lifted state-machine body
of a selected async method, declares that expansion. The plan records every
expansion with its owner-issued reason. Execution never widens scope on its
own.

## Reference execution and substitution

The reference execution is serial. It visits units in metadata order, builds
each substrate at most once per unit when a planned producer first needs it,
visits producers in plan order, and discards the unit's substrates after the
last visit. Completions then run in plan order. Published results are the
producers' outputs from that sequence.

Every other executor, including one that runs units in parallel, yields to a
host between units, reuses cached results, or answers from a precomputed index,
is a substitution. It is admissible only when its published results and receipt
facts equal the reference execution's for the same plan and input. This
follows the reference-interpreter and substitution model of the
[QuerySpace library](query-space-library.md) and
[source delegation](source-delegation.md): executors change cost, never meaning.

## Properties fixed at conception

These properties are part of the contract because each one is cheap to hold
from the start and expensive or impossible to add later. QuerySpace is the
local precedent: its plans are structural data, not opaque delegates, which is
what lets work collapse into a source or a specialized kernel. A LINQ-shaped
design cannot be tuned into that later, because the information the optimizer
needs was never captured.

### Demand is declared before execution

**Rule.** A producer declares every requirement statically. No producer
discovers a new requirement while it runs; a conditional need is an optional
requirement stated in the declaration.

*Keeps possible:* computing the plan, cost, read demand, and explanation before
the first byte is read, skipping every substrate no one requires, and scope
pushdown into acquisition.

*Lesson:* LLVM's legacy pass manager and Roslyn's runtime callback
registration show how much a scheduler loses when dependencies surface only
during execution. `LibraryBodyIndex`'s lazy members are the local instance:
the index cannot know what its consumers will ask for.

### Producers are read-only and communicate only through declared results

**Rule.** A producer does not mutate substrates, does not keep state that spans
units, and observes another producer only through that producer's declared
result. Producers own no shared lazily initialized state; anything shared is a
substrate or a library-scope input computed before units are visited.

*Keeps possible:* running units in parallel, running independent producers
concurrently, fusing producers into one pass, and caching per-unit results.

*Lesson:* GCC's global compiler state is the classic barrier to parallel
compilation. Roslyn had to add concurrent execution as an opt-in because
existing analyzers were not safe. The decompiler already states the local form
of this rule for its passes: they "communicate through the tree, never
side-channel state".

### The executor owns the loop

**Rule.** Producers visit units the executor gives them. No producer iterates
over the library itself.

*Keeps possible:* cancellation and work bounds at unit boundaries, cooperative
yielding on single-threaded Browser/Wasm, progress reporting, per-producer time
attribution, and parallel scheduling, all without producer changes.

*Lesson:* Roslyn's syntax and operation callbacks, and Go's shared `inspect`
traversal, give N analyzers one walk. Analyses that walk the program
themselves cannot be interrupted, parallelized, or fused.

### Results are detached, comparable, and keyed by their inputs

**Rule.** A published result retains no reader, resolver, stream, or live
authority. It has defined equality. Each declaration states its input
footprint: the unit's bytes alone, the unit plus its declaring module's
metadata, or resolved references beyond the module. A result is a function of
that footprint, the declaration identity and version, the effective scope, and
the parameters.

*Keeps possible:* reuse within one Workspace realization, a persistent cache
through the existing cache port, and early cutoff, where a body whose footprint
did not change between two package versions reuses its results. It also keeps
possible transferring results between a worker and the Browser UI thread.

*Lesson:* Roslyn replaced `ISourceGenerator` with `IIncrementalGenerator`, an
entirely new API, because the original's values could not be compared or
cached. Go's analysis facts are serializable by contract, which is what allows
separate-process and build-system drivers. Salsa and Bazel both depend on stable
input keys fixed in advance.

### Substrate lifetime ends with the unit

**Rule.** A producer copies whatever it publishes out of the substrates during
its visit. No result or completion refers to a unit's substrates after the
visit ends.

*Keeps possible:* memory bounded by the largest body rather than the library,
and streaming large assemblies, which Browser hosts need.

*Lesson:* whole-program analyzers that retain every method's IR run out of
memory before they run out of time. Resource lifecycle analysis already has to
compose its evidence "before those operation-local facts are discarded"; this
makes that the general rule.

### Outcomes are per producer and typed

**Rule.** Each producer's result carries its own outcome: not requested,
complete, incomplete with an owner-issued limitation, or failed. A producer
failure is contained to that producer. Every dependent producer receives a
typed prerequisite failure rather than a missing value. Independent producers
are unaffected.

*Keeps possible:* partial answers that stay honest, which the repository's
[failure visibility](../../AGENTS.md#repository-wide-engineering-constraints)
and complete-census rules require, and adding producers without widening every
consumer's failure surface.

*Lesson:* Roslyn contains a crashing analyzer as a diagnostic instead of
failing the compilation. Retrofitting an outcome shape onto result types that
never had one touches every consumer.

### Participation is observed, not declared

**Rule.** The receipt records, for each producer and substrate, the units it
was attempted on and how each attempt ended. The executor writes it at the
point work starts and ends.

*Keeps possible:* proving minimum work in tests, `explain`-style cost
reporting, and per-producer timing, without instrumenting each producer.

*Lesson:* LLVM's `-time-passes` and `-print-after-all` come from the pass
manager, not the passes. The
[selective implementation metric](library-body-analysis-service.md#selective-implementation-metric-analysis)
receipt is a hand-built instance of this rule for one producer family.

### Analysis and rewriting are separate tiers of machinery

**Rule.** This pattern covers read-only producers only. A component that
rewrites a representation, such as the decompiler's IR passes, keeps its own
ordered pipeline. If it adopts shared analyses, it does so through a separate
invalidation contract.

*Keeps possible:* an invalidation-free analysis tier, which is what makes
fusion, caching, and parallelism simple.

*Lesson:* analysis invalidation is one of the largest bug classes in LLVM's
pass managers. It exists only because rewriting and analysis share one cache.

### Composition is static and identities stay separate

**Rule.** Declarations are values in code. A plan contains only declarations
reachable from its request. Nothing is discovered by reflection, scanning, or
registration side effects. A producer's identity is an internal code identity
with a version. It is never an [analysis catalog](analysis-surfaces-and-universes.md#analysis-identity)
identity; the catalog binds its manifest-grade analysis identities to
declarations.

*Keeps possible:* NativeAOT and Browser/Wasm, deterministic plans, refactoring
producers without breaking manifests, and one catalog analysis that maps to
several producers.

*Lesson:* order-by-registration and name-keyed discovery make behavior depend
on link order and spelling. Research's current string-keyed producers show the
axis that [Assembly Inspection Query](assembly-inspection-query.md#prior-art-the-research-producer-registry)
already flagged.

## Tiers

A **tier** is a layer of producers with the same dependency position. Analysis
producers form the lowest tier. Research and consumer-owned interpretations,
such as the JSON wire contract, form higher tiers.

- A declaration may require declarations from its own tier or a lower tier,
  never a higher one. Analysis therefore never depends on Research or on any
  consumer.
- A higher-tier plan **lowers**: its lower-tier requirements become a
  lower-tier request with the scope and parameters the higher tier needs. The
  lower tier plans that request with no knowledge of the requester.
- Fusion across tiers belongs to the composing tier. A composed execution may
  visit lower-tier and higher-tier producers over one unit, because the
  composing tier can see both declaration sets. The lower tier's reference
  semantics are unchanged by being composed.
- A higher tier may introduce its own substrates. For example, Research's
  imported decompiler IR is a Research-tier substrate built from the
  decompiler, not an Analysis concern.

Research is therefore not only a consumer. It consumes Analysis results as
lowered requirements and adopts the same planning engine for its own producers.
That replaces the parallel machinery it has today: string-keyed producer
dependencies, requirement unions expressed as Analysis feature bits, and a
memoized context over one `LibraryBodyIndex` instance. How Research adopts, and
what happens to its session and admission contracts, is Research's own focused
effort.

The engine's contract names no Analysis, Research, or decompiler type, so it
can move below all of its adopters without redesign. Where it lives physically
is decided when the second tier adopts it.

## Relationship to adjacent owners

| Owner | Relationship |
| --- | --- |
| [Library body Analysis service](library-body-analysis-service.md) | First adopter. Its producer coordination, features, and fixed result slots become declarations and a plan at adoption; its focused result types are unchanged. |
| [Analysis catalog and operation participation](analysis-surfaces-and-universes.md#operation-participation) | Selects manifest-grade analyses and binds each to producer declarations. It owns cost, defaults, and discovery. |
| [Package read demand](package-read-demand.md) | Consumes the declared requirements that a plan exposes before execution. |
| [QuerySpace](query-space-library.md) and [source delegation](source-delegation.md) | Precedent for reference semantics and exact substitution. A query over producer results may later push its predicates into a plan's scope. |
| [Stateless core services](stateless-core-services.md) and [analysis index cache](analysis-index-cache.md) | Own retention and caching of the detached results this pattern publishes. |
| [Instruction substrate](instruction-substrate.md) | Owns the lowest substrates: decoding and blocks. |
| Research ([ownership paths](generic-research-ownership-paths.md), [assembly context](research-assembly-context-ownership.md)) | Intended second adopter as a higher tier. |
| Decompiler IR passes | Separate rewriting tier. Not an adopter of this pattern. |

## Future opportunities

These are not part of the claim. They are listed because the properties above
exist to keep them possible.

- **Parallel and cooperative executors** for desktop and Browser/Wasm, gated by
  equivalence to the reference execution. The desktop builder already runs
  per-method work in parallel; this makes that parallelism a substitution with
  a named equivalence gate.
- **Early cutoff across versions.** Diff between two package versions reuses
  per-body results wherever a body's declared footprint is unchanged.
- **Query pushdown.** A population query over producer results narrows a plan's
  scope or producer set before execution, as source delegation does for rows.
- **Persistent results** through the stateless-services cache port, keyed by
  footprint, declaration version, scope, and parameters.
- **Workspace-level units** for assembly-group producers such as call census,
  with cross-unit facts flowing only through declared completions.
- **A decompiler analysis cache** under its own invalidation contract,
  borrowing the declaration shape but not the read-only tier.

## Prior art

Surveyed as evidence, not authority. No code or schema is transferred.

- Go `golang.org/x/tools/go/analysis`: analyzers as values with declared
  requirements and typed results, a shared traversal, serializable facts, and
  multiple drivers over one contract. This is the closest match.
- Roslyn analyzers and incremental generators: callback registration over one
  walk, per-analyzer failure containment, concurrency added late as an opt-in,
  and an API replacement forced by non-comparable values.
- LLVM new pass manager and MLIR: lazily built, cached analyses and
  manager-owned instrumentation, with the cost of invalidation.
- Salsa and Bazel: stable input keys and early cutoff.
- Research's `ResearchFactRegistry` and this repository's decompiler pipeline:
  local precedents for declared dependencies and for an ordered list of passes
  that serves as the architecture document.

## Verification

These gates land with the first adoption and run in Release. Until then, every
property above is **unverified**.

- **Plan without bytes:** a plan and its declared requirements are computed
  for a real package without opening any body.
- **Whole-request rejection:** cycles, missing requirements, upward-tier
  requirements, and conflicting parameters are each rejected with typed reasons
  and no execution.
- **Undeclared access fails visibly:** a producer that reads a result or
  substrate it did not declare fails instead of receiving a value.
- **Executor equivalence:** parallel and serial executors publish equal results
  and receipt facts over the same input. The existing parallel-build
  determinism tests are the starting point.
- **Minimum work:** the receipt for a single-producer request shows no
  unplanned producer or substrate participated.
- **Failure containment:** an injected producer failure leaves independent
  producers' results unchanged and gives dependents a typed prerequisite
  failure.

Whether the static-composition rule gets full, partial, or no gate coverage is
an open decision for the operator, under the
[absence-claim rule](../evidence-and-validation.md#absence-claims-choose-their-coverage).
