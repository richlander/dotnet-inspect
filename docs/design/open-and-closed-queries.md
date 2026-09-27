# Open and closed queries

## Status

Draft focused design for a cross-cutting pattern. It defines only the pattern:
what an open query and a closed query are, the terminals that close a query,
how one terminal is derived from another, and what any lowering of a closed
query must preserve. Adopters apply it without redefining it:

- [Producer Planning](producer-planning.md) makes a producer an open query.
- [QuerySpace](query-space-library.md) closes open queries for its consumers
  and merges closings across consumers; request collapse is tracked in
  [#8574](https://github.com/richlander/dotnet-inspect/issues/8574) and method
  bodies as a source in
  [#8577](https://github.com/richlander/dotnet-inspect/issues/8577).

Every property below is **unverified** until its gate lands; see
[Verification](#verification).

## Examples

**One open query, three closings.** "Is this method async?" is an open query
over a library's methods: a population, a scope, and a per-method predicate,
with no answer yet. The library summary closes it with Count to print
`Async Methods: 22`. A badge closes it with Exists. The Async Methods section
closes it with Rows to list them. Asked together, they are one piece of work:
the most expansive closing, Rows, is executed; Count and Exists are derived
from it; and each consumer receives the shape it asked for.

**A constant closed query.** "Does this library contain unsafe evidence?" is
Exists over the unsafe-evidence open query. Nothing about it depends on
runtime input, so it can be lowered to a kernel: one loop specialized to the
predicate and to Exists, decided when the code is compiled.

**Several closed queries in one pass.** "Which methods are P/Invoke, how many
are async, and does any other signature carry a pointer?" are three open
queries that share one classification, closed with Rows, Count, and Exists.
They run as one pass. The classification is computed once per method, and
each question is visited only for the methods in its scope.

## Owner and exact claim

This pattern owns this exact claim:

> A closed query means its terminal applied, in unit order, to the units its
> open query selects. Every lowering of a closed query, whether a derived
> terminal, a closing merged with others, or a kernel, publishes the result,
> outcome, and receipt that the reference execution of that closed query alone
> would publish.

This pattern defines:

- the open query and the closed query;
- the terminals and the order in which one derives another; and
- the equivalence that every lowering must preserve.

This pattern does not define:

- producer declarations, dependency closure, or planning, which
  [Producer Planning](producer-planning.md) owns;
- merging closings across consumers, traversal, or kernel selection, which
  level 2 owns;
- any predicate's or projection's meaning, which its owner defines; or
- presentation, Findings, or envelopes.

## Open queries

An **open query** is a population of units at a grain, the scope that narrows
it, and a per-unit predicate or projection. It has no terminal, so it does not
know how many units its consumers need. Scope is declared: a type scope for
whole types and scope guards over a classification, as Producer Planning
defines them. An open query can be narrowed with more scope. Two open queries
with the same identity and parameters are the same query, which is what lets
their closings be merged.

## Closed queries and terminals

A **closed query** is an open query with a terminal:

| Terminal | Answer | May stop early |
| --- | --- | --- |
| Exists | Whether any selected unit satisfies the predicate | Yes, at the first |
| Count at least N | Whether N selected units satisfy it | Yes, at the Nth |
| Count | How many selected units satisfy it | No |
| Rows | The selected units' projections, in unit order | No |
| Fold | An owner-defined aggregation, such as a call graph | No |

Rows derives Count, Count derives Count at least N, and Exists is Count at
least one. Closings of the same open query at different thresholds merge to
the largest. Fold derives nothing, because its aggregation belongs to its
owner. When several consumers close the same open query, the most expansive
closing is executed and the others are derived.
The work may stop early only when every closing executed for that open query
permits it.

A derived closing has the outcome its own reference execution would have. If
the executed Rows fails at a unit, a derived Count fails too. A derived Exists
that was already settled at an earlier unit stays settled, because its own
execution would have stopped there before reaching the failure.

## Lowering

Lowering follows one rule: carry type currency from the request through the
pass, and exchange it for plan data only where requests must merge, never
once per unit. The plan stays data, so requests can be validated, merged,
explained, and carried between hosts. Execution becomes types wherever the
shape is known.

A closed query may be lowered to a **kernel**: one loop specialized to its
predicate or projection and its terminal, with nothing else to coordinate in
its pass. A closed query written as a constant in code is a concrete
instantiation decided at compile time. A closed query composed at runtime
selects a kernel once per execution, never once per unit.

Several closed queries fused into one pass run their shared open-query work,
such as a classification, once per unit and feed each terminal. When the
combination is a constant in code, it lowers to a **typed fused kernel**: the
shared work and the terminals compose into one nested type, so the pass is one
specialized loop. When the combination is known only at runtime, the pass is
interpreted over typed leaves. It crosses from plan data into typed code once
per leaf per unit, or once per batch of units.

A kernel's speed must not depend on how many producers exist elsewhere in the
program. Whole-program devirtualization is not a substitute for
specialization: it gives up as implementations accumulate. The per-unit
members of a kernel's predicate, projection, classifier, and terminals are
small enough to inline into its loop, and are declared so.

A kernel substitutes for the reference execution, so it keeps the reference
execution's semantics: failure containment, stopping before the next untrusted
read, type scope, and the receipt, including units visited, per-producer
participation, and layer acquisition.

## Authoring

Authors write open queries. Consumers close them. Genuine aggregations use
Fold. Writing one producer per terminal over the same predicate, such as an
async count and an async presence, closes the query inside the producer: it
defeats merging and duplicates the predicate.

## Priorities

The pattern serves three goals in order: requests the planner can see, then
performance, then authoring ergonomics.
[Planning API tradeoffs](planning-api-tradeoffs.md) records that ordering,
where complexity is allowed to live, and the ledger of what each optimization
cost authors.

## Relationship to prior art

NLinq separates the pipeline, `Where` and `Select`, from the terminal, `Count`,
`Any`, and `ToList`, and specializes each terminal's fold to a struct
predicate. Kernels borrow that, and typed fused kernels borrow its nesting of
stages into one type. NLinq's self-typed enumerator also lets a source override
a terminal, such as a list answering `Count` from its length. That is the
pushdown point for sources that can answer a closing natively. NLinq composes
the whole pipeline as a type at one call site, which is what makes merging
across consumers impossible there, so open queries stay data until the planner
closes them. In SQL, a `SELECT`
closes one query; shared scans and multi-query optimization close many against
one read.

## Verification

- **Kernel equivalence.** A kernel and the reference execution publish the
  same result, outcome, failure unit, units visited, participation, and layer
  acquisition, for Count and Exists, over fixtures with failing units, types in
  and out of scope, and an empty image. The gate is
  `ProducerPlanningTests.ClosedQueryKernel_MatchesTheInterpretedExecutor`, on
  the experiment branch.
- **Thresholds and Rows.** Count at least N and Rows kernels match the
  reference execution under the same gate, in
  `ProducerPlanningTests.ClosedQueryKernel_MatchesTheInterpretedExecutor` and
  `RowsKernel_MatchesTheInterpretedExecutor`, on the experiment branch; merged
  thresholds are gated by
  `Planner_MergesExistsThresholdsToTheLargestAndAllDominates`.
- **Typed fused kernels.** A typed fused kernel matches the interpreted fused
  pass's results and outcomes. This is **unverified**: the first typed fused
  kernel fails the whole request on a unit failure rather than each question.
- **Derivation.** Count derived from Rows, and Exists derived from Count, match
  their own reference executions, including a failure after an Exists was
  settled. This is **unverified**.
- **Merging.** Closings merged across consumers each receive the shape they
  asked for. This is **unverified** until level 2 adopts closing (#8574).
