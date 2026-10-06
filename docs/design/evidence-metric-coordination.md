# Evidence and metric coordination

## Status, owner, and exact claim

Status: **proposed design contract** for
[#8908](https://github.com/richlander/dotnet-inspect/issues/8908).

The **Evidence and Metric Coordination** pattern owns this exact claim:

> Given one exact owner-issued population binding, one explicit admitted
> evidence selection, one non-empty set of owner-issued metric requirements,
> and explicit work bounds, an adopting coordinator normalizes the metrics'
> owner-declared evidence prerequisites, shares compatible acquisition and
> derivation work, evaluates only the requested metrics and their
> prerequisites, and returns population-bound typed results with requested,
> effective, and actual-work receipts.

This is one cross-cutting coordination pattern. It defines the separation and
handoff among population, admitted evidence, canonical facts or edges, metrics,
roll-up, and work accounting. It does not define any population, evidence
meaning, metric algorithm, result row, graph relationship, or host experience.

The contract is **unverified** until one focused producer adoption and one
production consumer supply the gates under
[Required evidence](#required-evidence).

## Product motivation

Several product questions have the same coordination shape:

- Which overload contains the substantive implementation?
- Which Types are structural foundations or orchestrators?
- Which methods are the strongest audit candidates?
- Which assemblies or packages form cycles or clusters?

Those questions vary independently along three dimensions:

| Dimension | Question |
| --- | --- |
| Population | Which exact owner-issued subjects participate? |
| Admitted evidence | May declaration signatures, method bodies, or both establish facts? |
| Metric | Which relationship, degree, size, complexity, clustering, allocation, or safety question is evaluated? |

Combining all three dimensions in one feature-specific request makes useful
facts difficult to share. Treating a metric as evidence obscures prerequisites
and completion. Treating a displayed scope name or token set as a population
loses owner-issued identity and correspondence. Running one producer per
metric repeats acquisition and derivation work.

The initial real witness is
`System.Text.StringBuilder.AppendFormat` in .NET 11 RC1. Its overload family
contains thin forwarding methods and one substantially larger implementation.
Body size and sibling-call relationships should be selectively available to a
Member metrics consumer without requiring the complete historical
Implementation Profile bundle.

`System.Text.Json` 10.0.0 is the broader relationship witness. Its Type
structure supports signature-backed and body-backed relationship questions
whose downstream distinct-neighbor metric can match while their evidence
producers, qualifications, and populations remain separately owned.

## Adjacent owners

This pattern references adjacent contracts without redefining them.

| Owner | Imported contract |
| --- | --- |
| [Inspection Analysis Requests](analysis-surfaces-and-universes.md) | Report surface, finite universe, question mode, result projection, and validated pre-execution request plan. |
| [Analysis Universe Realization](analysis-universe-realization.md) | Exact universe correspondence, executable capability bindings, population order, incidence, lifetimes, and preserved failures. |
| [Inspection Operation Kernels](inspection-operation-kernels.md) | Resolved structural plan, typed data or provider bindings, finite execution scope, terminal requirement, and reference-execution discipline. |
| [Library Body Analysis Service](library-body-analysis-service.md) | Body-evidence acquisition, prerequisite planning, physical scope, producer participation, and detached focused results. |
| [Inspector.Graph Execution](inspector-graph-execution.md) | Domain-neutral execution over caller-bound typed graph structure and graph-owned work receipts. |
| QuerySpace owners | Query resolution, predicates, ordering, semantic selection, terminals, and source delegation. |
| Domain evidence owners | Population identity, evidence meaning, canonical facts or edges, completion, and producer-local failures. |
| Metric owners | Metric meaning, algorithm, units, ordering, tie-breaking, and metric-local limits. |
| Product composition | Subject realization, capability and cost authorization, owner binding, host-neutral result composition, and production scheduling. |

The pattern is narrower than an Analysis request. A result projection such as
Rows or Graph is an output shape. A metric such as body size, incoming degree,
or SCC membership is a question evaluated before that output shape is
published.

The pattern is also narrower than universe realization. It consumes one exact
owner-issued population binding; it does not construct, widen, enumerate, or
authenticate that population.

## Coordination contract

One coordinated execution has these conceptual inputs:

```text
EvidenceMetricExecution
  PopulationBinding
  AdmittedEvidenceSelection
  MetricRequirements
  RollupRequirements
  WorkBounds
```

This is a conceptual contract, not a frozen CLR type, serialized schema,
universal planner, or runtime registry. Each adopter uses owner-issued types.

### Population binding

The population binding identifies the exact candidate population over which
evidence and metrics are meaningful. Its owner supplies:

- the subject and population identity;
- deterministic population order when order is part of the contract;
- the applicable generation, snapshot, receipt, or other join currency;
- membership, expansion, and roll-up semantics;
- completion, rejection, unavailability, and failure evidence; and
- the lifetime in which population identities and operations remain valid.

The coordinator treats this binding as opaque. It does not infer membership
from display names, tokens, paths, collection positions, or equal-looking
values. It cannot substitute a broader population and filter afterward.

An overload family, Types in one Library, assemblies in one package graph, and
packages in one Workspace are different owner-issued population bindings.
They are not variants of one universal subject algebra.

### Admitted evidence selection

The evidence selection states which owner-issued evidence families may
establish facts for this execution. Examples include:

- declaration or signature evidence;
- method-body evidence; and
- an explicitly supported composition of both.

Selection grants neither acquisition authority nor metric demand. Product
composition separately supplies authorization, finite scope, and work bounds.
Omitted evidence is unavailable to metric prerequisite normalization and
performs no evidence-specific work.

Evidence owners retain:

- observation and relationship meaning;
- identity and correspondence;
- acquisition and decoding;
- qualification and completion;
- positive, empty, incomplete, unavailable, and failed outcomes; and
- producer-local work units.

The coordinator never relabels body evidence as signature evidence, treats a
fact from one generation as evidence for another, or upgrades partial evidence
to complete because a metric returned a value.

### Metric requirements

A metric requirement names one owner-issued question over the bound
population. Its owner declares:

- accepted population roles;
- accepted evidence families;
- semantic evidence prerequisites;
- required canonical fact or edge shapes;
- algorithm, units, direction, ordering, and tie-breaking;
- applicable metric and roll-up bounds; and
- result and completion semantics.

Metric requirements are demand, not authorization. A product request may
authorize several metrics while a resolved QuerySpace terminal, projection,
predicate, or order demands only a subset. Only effective demand enters
coordination.

Named profiles are presets over an exact metric set, evidence selection, and
population role. They are not separate execution architectures.

### Roll-up requirements

Roll-up is explicit whenever facts established for one population unit
contribute to a metric over another unit. The applicable owner defines:

- source and target units;
- identity correspondence;
- duplicate and self-edge treatment;
- aggregation units;
- qualification propagation; and
- ordering and tie-breaking.

A Library metric cannot mean "build every Member row, then group it" unless
the owning design names that as a reference execution. The roll-up requirement
must reach planning early enough to avoid unnecessary lower-level result
materialization.

## Normalization and execution

The observable coordination sequence is:

```text
owner-issued population binding
  + admitted evidence selection
  + effective metric requirements
    -> validate metric/evidence/population compatibility
    -> normalize semantic evidence prerequisites
    -> normalize executable work prerequisites
    -> coordinate compatible acquisition
    -> derive owner-issued canonical facts or edges
    -> evaluate metric algorithms
    -> perform explicit roll-up
    -> publish typed results and work receipt
```

### Semantic and executable prerequisites

The plan preserves two prerequisite kinds:

```text
requested metrics
  -> semantic evidence prerequisites
  -> effective evidence
  -> executable work prerequisites
  -> effective work stages
```

A semantic prerequisite is another public fact or evidence result required to
interpret the metric. A sibling-overload relationship metric, for example,
requires direct-call facts.

An executable prerequisite is internal work required by an evidence owner's
implementation. A direct-call producer may require decoded instructions and a
method context without publishing instruction-shape or control-flow metrics.
Execution of that shared stage does not make every fact it could support
available.

Prerequisite edges are owner-declared and retained in the plan and receipt.
The coordinator does not discover dependencies dynamically from runtime result
types or use a service locator.

### Compatible work sharing

The invariant is **one coordinated execution with no duplicate compatible
acquisition or derivation**, not one literal traversal.

Some metrics can share one metadata walk, body decode, call scan, relationship
projection, adjacency view, or population accumulator. Other algorithms
legitimately require distinct passes, different order, or separately bounded
state. The coordinator may schedule either shape when it:

- coalesces compatible demand so metrics do not independently repeat the same
  acquisition or derivation for the same population unit;
- preserves owner-required batching, retry, and multi-pass behavior while
  recording its actual charged work rather than describing it as one pass;
- attributes the stage to every metric or co-running feature that required it;
- does not run an unrelated producer merely because its prerequisites overlap;
- preserves deterministic owner-issued result order; and
- records actual participation where work starts.

Retained content or indexes may be reused across executions only under their
owner's identity, generation, lifetime, and compatibility contract. Equal
display values or equal-looking plans are insufficient.

### Canonical facts and relationship edges

Evidence owners may publish reusable typed facts or edges before a metric
algorithm runs. Examples include:

- encoded body size;
- direct call sites;
- sibling-overload call relationships;
- signature Type-use relationships; and
- body Type-use relationships.

These are evidence-level values, not host rows and not Graph-owned topology.
A domain composer may bind typed relationship edges into Inspector.Graph when
their owner-issued semantics match the selected Graph operation. Graph then
owns topology and structural work, while the evidence owner retains edge
meaning and population completion.

Two producers may feed the same downstream metric only when the metric owner
explicitly accepts both typed edge semantics. Sharing a degree algorithm does
not claim that signature and body relationships mean the same thing.

### Metric execution and terminals

Metrics execute over the effective evidence and exact population. Scalar,
relational, and graph-shaped metrics keep their native algorithms.

Terminals are not implemented by materializing a larger result and filtering
it afterward when the selected terminal can avoid that work. Exists, Count,
Rows, Graph Document, and ranked Top may require different execution and
retention while preserving the metric's meaning.

QuerySpace intent that can narrow population, metric demand, ordering, or a
terminal must reach coordination before work starts. A post-execution
QuerySpace filter cannot retroactively claim producer savings.

## Results, completion, and failure

One result is associated with the exact population binding and evidence
selection that produced it. A host or downstream operation cannot publish it
into another subject, population generation, or Workspace merely because the
row identities appear equal.

For each requested metric, the result distinguishes:

| Outcome | Meaning |
| --- | --- |
| Available | The owner-issued metric value is complete for its represented qualified population. |
| Available empty | The requested metric completed and its exact represented result is empty or zero. |
| Incomplete | A usable partial value exists with owner-issued limitations. |
| Unavailable | The population or evidence cannot support the metric for a typed reason. |
| Budget exhausted | A named work bound prevented completion; retained healthy evidence remains visible. |
| Failed | Metric or prerequisite execution failed with owner-issued diagnostic association. |

Omission means not requested. It is never projected as zero, false, available
empty, or unsupported.

Positive evidence may remain usable when unrelated work is incomplete. A
negative relationship result, exact Count, complete ranking, or absence claim
requires the applicable population, evidence, derivation, and metric execution
to be complete. No layer turns an incomplete producer into a successful empty
metric.

## Work receipt

The detached result carries or references a typed receipt that distinguishes:

- exact population identity and join currency;
- authorized and admitted evidence;
- requested metrics;
- effective evidence after semantic prerequisite normalization;
- planned executable stages and prerequisite reasons;
- actual stage participation and metric causes;
- population units attempted, completed, bounded, unavailable, and failed;
- canonical facts, edges, or views built or reused;
- metric and roll-up work performed;
- configured and consumed work bounds;
- terminal settlement versus population exhaustion; and
- qualification, limitations, and diagnostics applicable to each result.

Actual work is recorded by the owner that observes it. A plan cannot claim
that a stage ran, and a downstream composer cannot manufacture an
evidence-owner completion receipt.

The receipt supports minimum-work gates and performance diagnosis. It does not
become a universal telemetry schema or user-facing quality conclusion.

## Implementation Profiles

An Implementation Profile is a named closed metric request over one exact
method population and body evidence. It may request body size, control flow,
calls, allocations, safety, sibling relationships, and other versioned facts.
It is not an evidence family and does not own a parallel body-analysis
substrate.

During migration:

1. focused Analysis results become the sole source of each profile fact;
2. the complete profile adapter composes those results without acquiring or
   decoding bodies;
3. production callers move from the monolithic feature to the named selective
   request; and
4. the compatibility feature and aggregate-index entry point retire after the
   final caller migrates.

The migration is incomplete if the adapter consumes shared result types while
retaining the previous body scan, decode, call collection, or other avoidable
work.

## Example compositions

### Selective sibling-overload relationships

```text
population: exact Member overload-family binding
evidence:   body direct-call facts
metric:     directed sibling-overload relationships and degree
roll-up:    physical evidence body to logical Member identity
```

The body producer owns direct-call and sibling-edge meaning.
MemberMetricsInspect owns the exact-family product composition. A Graph degree
kernel may later consume the typed edges when its relationship semantics match.

### Namespace leverage, Type sea level, and mountain peaks

```text
population: exact Types-in-Library binding for namespace roll-up;
            exact Types-in-namespace binding for Type leverage
evidence:   signature relationships or body relationships
metric:     distinct external-source-Type namespace score;
            directed distinct-neighbor Type degree and deterministic order
roll-up:    producer-issued relationship endpoints to exact namespace and
            Type identities
```

Signature and body relationship populations remain separate and separately
qualified. Inspector.Graph may execute the same structural degree operation
for both when each product composition supplies compatible typed edges.

### Complete profile preset

```text
population: exact method or overload-family binding
evidence:   bodies
metrics:    named CompleteProfileV1 metric set
roll-up:    physical evidence bodies to logical declared methods
```

The profile is a preset and compatibility result adapter over the coordinated
execution.

### Library dependency structure

```text
population: exact assemblies or packages
evidence:   signature references or body calls
metrics:    relationships, SCCs, levels, or clustering
roll-up:    owner-issued lower-level endpoints to the bound population
```

The dependency owner defines edge admission and roll-up. Graph owns only the
selected topology algorithms and structural receipt.

## Platform, lifetime, and safety

The pattern requires no reflection, runtime plugin discovery, expression
trees, inspected-assembly loading, or Roslyn. Adopters remain SRM-only and
NativeAOT-friendly where their evidence owners are.

Single-threaded Browser/Wasm is the baseline. Parallel scheduling is optional
and cannot change population membership, prerequisite normalization, work
charging, result order, completion, or failure visibility.

The pattern introduces no new trust boundary. Evidence owners retain
construction-time containment of untrusted package, assembly, metadata, and IL
content. The coordinator does not execute inspected code.

Detached results retain no reader, stream, mutable Workspace, lease-bound
provider, or execution callback unless a focused owner explicitly defines a
borrowed result.

## Production adoption

Issue #8908 is the end-to-end tracker. Adoption is seven focused slices:

1. **Pattern:** lock this coordination contract without changing an existing
   owner's execution.
2. **Body Analysis adoption:** map the existing selective implementation
   metric request and result to separate evidence, canonical relationship, and
   metric concepts.
3. **Member operation:** implement MemberMetricsInspect over an exact overload
   population, initially selecting body size and sibling relationships.
4. **CLI adoption:** consume the shared Member metrics envelope for one
   overload-family experience.
5. **Inspect Web adoption:** request the same envelope asynchronously and join
   it to the already-published Member population without delaying base Member
   rendering.
6. **Profile migration and retirement:** move complete-profile consumers to
   the named selective request, reduce profiles to an adapter, and remove
   superseded compatibility work.
7. **Second evidence mode:** adopt the same relationship-degree metric over
   signature and body Type relationships, proving evidence-mode independence.

`System.Text.StringBuilder.AppendFormat` is the first Member production
witness. `System.Text.Json` 10.0.0 is the first Type relationship witness.
Broader package clustering and audit-complexity work remain later
owner-specific adoptions.

Each slice names one adopting owner and its design. This pattern does not
authorize a multi-owner implementation sweep.

## Required evidence

Pattern acceptance and tracker completion are deliberately separate.

The pattern locks when review confirms:

1. the three dimensions remain independent across all four example
   compositions;
2. the population binding remains owner-issued and opaque;
3. canonical facts or edges are distinct from metric and output projection;
4. coordinated execution requires no duplicate compatible acquisition without
   promising one literal traversal; and
5. the document does not redefine an adopting owner's evidence, metric,
   population, or failure semantics.

The first Body Analysis adoption must add Release gates proving:

1. omitted metrics perform no metric-specific work;
2. requested, effective, and actual-work receipts remain distinct;
3. shared prerequisites execute once per compatible physical body;
4. available empty relationship evidence is distinguishable from omission and
   incomplete scope; and
5. the complete-profile adapter triggers no second evidence pass.

The overall tracker closes only after:

1. one scalar metric and one relationship metric use the coordination model;
2. one Graph metric is reused over separately owned signature and body
   relationship evidence;
3. CLI and Inspect Web consume the same host-neutral Member metrics result;
4. exact base/head NativeAOT and focused Analysis performance evidence under
   the [modernization evidence
   contract](../evidence-and-validation.md#nativeaot-beforeafter-for-modernization)
   shows that selective requests avoid superseded complete-profile cost and
   add no default-site latency; and
5. the monolithic compatibility path and its duplicate work retire.

Until a named gate lands and runs in Release, its asserted property is
**unverified**.

## Model assessment

This pattern adds no mutable membership, scheduler state, incremental
publication protocol, cross-generation cache replacement, or new lifetime
authority. It composes immutable plans, owner-issued bindings, sequential
execution, and detached results. A TLA+ model would duplicate adjacent owners
without checking a new state machine.

If an adoption introduces mutable population membership during execution,
concurrent partial publication, cross-generation result replacement, or a new
lease or cancellation protocol, stop and model that focused interaction before
implementation.

## Non-claims

This pattern does not:

- define a universal subject, population, evidence, metric, relationship,
  result, receipt, or failure type;
- replace Analysis requests, universe realization, QuerySpace, Inspector.Graph,
  or Inspection Operation Kernels;
- define producer algorithms, metric formulas, complexity weights, audit-risk
  scores, clustering choices, or relationship semantics;
- require scalar metrics to become graph operations;
- claim that signature and body relationships have equal meaning;
- require one literal traversal or prohibit an algorithmically necessary
  second pass;
- authorize unbounded acquisition, implicit "all" evidence, or hidden
  capability escalation;
- permit building every lower-level row and filtering afterward as a general
  roll-up implementation;
- define CLI syntax, section names, output formats, Browser layout, colors, or
  asynchronous interaction;
- make Implementation Profiles the canonical metric result;
- introduce runtime registration, service location, reflection discovery, or
  executable portable plans; or
- authorize all adopting owners to change in one PR.
