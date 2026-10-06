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
- The [routing and settlement model](models/open-query-routing-settlement/README.md)
  checks the interaction among exclusive classification, inclusive fan-out,
  unit order, and independently settling Head closings.

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

> A closed query means its terminal applied, in unit order, to the stream its
> open query selects through per-unit routing. Every lowering of a closed
> query, whether a derived terminal, a closing merged with others, or a kernel,
> publishes the result, outcome, and receipt that the reference execution of
> that closed query alone would publish.

This pattern defines:

- the open query and the closed query;
- per-unit exclusive classification and inclusive fan-out;
- the terminals and the order in which one derives another; and
- the equivalence that every lowering must preserve.

This pattern does not define:

- producer declarations, dependency closure, or planning, which
  [Producer Planning](producer-planning.md) owns;
- merging closings across consumers, traversal, or kernel selection, which
  level 2 owns;
- a population's unit order, identity, or duplicate policy;
- any classifier, predicate, or projection's domain meaning, which its owner
  defines;
- whole-source fallback, ranking, or ordering after a terminal; or
- presentation, Findings, or envelopes.

## Open queries

An **open query** is a population of units at a grain, the scope that narrows
it, and a per-unit predicate or projection. It has no terminal, so it does not
know how many units its consumers need. Scope is declared: a type scope for
whole types and scope guards over a classification, as Producer Planning
defines them. An open query can be narrowed with more scope. Two open queries
with the same identity and parameters are the same query, which is what lets
their closings be merged.

The population owner supplies one deterministic **unit order**. The reference
execution visits that order without an implicit alphabetical, score, identity,
or presentation sort. An optimized source may traverse differently only when
the selected stream remains observably equal to that unit-ordered reference.

Whether two observations with equal projected values are one unit or two
belongs to the population owner. A terminal counts selected units, not distinct
projected values. Deduplication that changes Head membership must therefore
belong to the population or to an explicit selection stage before Head; applying
it after Head is not an equivalent lowering.

## Per-unit routing

Routing decides which open-query streams receive the current unit. It has two
forms:

- **Exclusive classification.** One owner-issued classifier assigns the unit
  one typed class or no class. If several underlying tests could match, the
  classifier's declared precedence chooses exactly one class; lower-precedence
  alternatives do not also publish for that unit. An open query declares the
  classes it accepts. Classification is deterministic input to scheduling;
  whichever consumer or producer executes first cannot change it.
- **Inclusive fan-out.** Independent open queries evaluate the same unit. Every
  matching active query may select and project it, so one unit may contribute
  to several result streams. Each closing settles independently.

Exclusive classification is per unit. It does not mean "use stream B only if
stream A is empty across the whole source." That whole-source fallback cannot
publish B until A's absence is established and is not part of this pattern. A
future owner that requires that exhaustive semantic must define it separately.

Find is the motivating production adopter, tracked by
[#8984](https://github.com/richlander/dotnet-inspect/issues/8984). Its
candidate owner can issue one exclusive match classification such as Exact,
Prefix, Substring, Similar, or None while retaining candidate discovery order.
An exact-only query accepts only Exact; an ordinary query accepts its declared
broader class set. Head then counts accepted candidates across those classes
and stops at the Nth. The Find owner separately defines those classes, their
tests, candidate identity, and source order; this pattern does not redefine
them.

## Closed queries and terminals

A **closed query** is an open query with a terminal:

| Terminal | Answer | May stop early |
| --- | --- | --- |
| Exists | Whether any selected unit satisfies the predicate | Yes, at the first |
| AtLeast(N) | Whether N selected units satisfy it | Yes, at the Nth |
| Count | How many selected units satisfy it | No |
| Rows | The selected units' projections, in unit order | No |
| Head(N) | The first N selected units' projections | Yes, at the Nth |
| Window(A..B) | The selected units at positions A through B | Yes, at position B |
| Tail(N) | The last N selected units' projections | No, unless the source can traverse in reverse |
| GroupBy(key) | The selected units' identities, keyed by a declared key | No |
| CountBy(key) | How many selected units share each key | No |
| Fold | An owner-defined aggregation, such as a call graph | No |

Head, Window, Tail, and Top mean what
[semantic row selection](semantic-row-selection.md) defines: Head is a lenient
clamp, and a strict Window fails unless position B exists. This pattern
expresses them as closings; it does not redefine them.

Rows derives Count, Count derives AtLeast(N), and Exists is AtLeast(1). Rows
derives GroupBy(key) only when each row carries both the key and its unit's
typed identity, because GroupBy returns identities and a projection alone may
map two units to the same row; otherwise GroupBy runs as its own closing.
GroupBy(key) derives CountBy(key), whose counts are its lists' lengths, and
Rows derives CountBy(key) from any key its rows carry. Closings of the same
open query at different thresholds merge to the largest. Fold derives nothing, because its aggregation belongs to its
owner. When several consumers close the same open query, the most expansive
closing is executed and the others are derived.
The work may stop early only when every closing executed for that open query
permits it.

For fused queries, reaching a terminal deactivates only that closing. It is not
visited, projected, or charged for later units, while other active closings may
keep the shared traversal alive. The shared traversal stops early before the
next available unit exactly when every consumer it serves has settled;
otherwise it continues through source exhaustion. Head records whether it
reached N selected units or the source exhausted first; both are successful
Head outcomes, but they are distinct completion evidence.

A derived closing has the outcome its own reference execution would have,
which depends only on the operations that closing requires. Count, AtLeast(N),
and Exists require the predicate, not the projection. If the executed Rows
fails to project a unit whose predicate succeeded, a derived Count is
unaffected; the execution records predicate and projection outcomes
separately, or runs the derived closing on its own. If the predicate fails at
a unit, a derived Count fails too. A derived Exists that was already settled
at an earlier unit stays settled, because its own execution would have stopped
there before reaching the failure.

## Phases

A closing lowers to **phases** over one cursor. A phase pairs a
**processor**, which decides what happens to each selected unit, with a
**condition**, which decides when the phase ends:

| Processor | What it does with each selected unit |
| --- | --- |
| Discard | Nothing; only the predicate runs |
| Count | Counts it |
| Rows | Projects it and keeps the row |
| Buffer(N) | Keeps the most recent N |
| Top(N by key) | Keeps the best N in a bounded heap |
| Fold | Folds it, as its owner defines |

| Condition | When the phase ends |
| --- | --- |
| Never | At the end of the source |
| At N | After N selected units |
| StopWhen(policy) | When a declared policy over progress says so |

When a phase ends, the cursor either stops or passes, still in place, to the
next phase. A **stage** is a phase that passes the cursor on. Skip(N) is the
stage that discards N selected units and continues.

Every phase ends in one of two recorded ways: its condition was met, or the
source was exhausted first. The closing's owner interprets which. Head(N)
accepts exhaustion with fewer rows. Window(A..B) accepts only reaching
position B: exhaustion before B, whether in its skip or in its rows, is the
owner's strict-window failure, and is never reported as source exhaustion or
as successful truncation. The same record is the completion evidence that
[source delegation](source-delegation.md) requires: reached N, or exhausted.

The named closings are the surface, and each lowers to phases:

| Closing | Phases |
| --- | --- |
| Count | Count until never |
| Exists | Discard until 1 |
| AtLeast(N) | Count until N |
| Rows | Rows until never |
| Head(N) | Rows until N |
| Skip(N), as a stage | Discard until N, then the next phase |
| Window(A..B) | Skip(A - 1), then Rows until B - A + 1, requiring the condition met |
| Tail(N) | Buffer(N) until never |

Merging follows from phases. Closings of the same open query run their
processors on the shared cursor, and the pass continues until every phase is
done. "Rows 4 to 6 of 22" is Window(4..6) and Count merged: the predicate runs
to the end for the count, and only three rows are projected. Derivation is
processor subsumption: Rows subsumes Count, and Count subsumes AtLeast(N).

A **stop policy** is a pure function of progress, such as selected units,
rows emitted, or which closings are settled. It is declared with the request,
so its stop is exact and explainable. An **interruption** is an external stop,
such as a user abort, a budget, or a full page. It is recorded as an
incomplete stop and never presented as an exact answer. Both stop at a unit
boundary, before the next untrusted read.

Names differ by layer. Commands and authors use the closing names. The plan
uses processors, conditions, and phases. Kernels take each processor and
condition as a struct type parameter, and a sequence of phases becomes a small
state machine in one loop.

## Typed results

The planner is not only a reducer. It produces a typed result for a declared
request set, and the result's type is known when the request is planned,
before any subject is read:

| Result kind | Closings | Result type |
| --- | --- | --- |
| Scalar | Count, Exists, AtLeast(N) | An integer or a Boolean |
| Rows | Rows, Head(N), Window(A..B), Tail(N) | A sequence of the projection's row type |
| Keyed | GroupBy(key), CountBy(key) | A map from key to identities, or to counts |
| Result set | A request set | Shared rows plus each closing derived from them |

These are planner result types, not presentation. The
[output-shape ladder](output-shapes.md#the-shape-ladder) keeps its rungs and
owns how a result is presented: a Rows result may render as a Table, one
field of it as a Vector, and a result set as a Document's sections. This
pattern does not map result kinds to rungs.

A request set lowers one of two ways:

- **Stream.** Each closing runs as phases and kernels and stops as early as
  its phases allow. It suits one-shot answers, counts over large
  populations, and windows.
- **Result set.** The most expansive closing runs once, and every closing
  derivable from its rows under the rules above is published beside them as a
  view; a closing that is not derivable runs in the same pass as its own
  closing. It suits consumers that display the rows and ask many questions of
  them, such as an interactive page or a static bundle. Because the views
  derive from the same rows, their numbers agree by construction.

The result-set lowering has prior art in the
[.NET CVE schema](https://github.com/dotnet/designs/blob/main/accepted/2025/cve-schema/cve_schema.md):
flat denormalized rows as a first layer, and computed indexes, keyed lists of
identities, as a second layer that turns joins into lookups. Its indexes are
chosen by hand for anticipated questions. Here the request set declares the
questions, so the planner materializes exactly the views it is asked for.

What changes is the order of work, not the ladder. Today a command builds
its rows and a request narrows them afterward, even to a single number.
Planned, the requested presentation lowers to the result the planner
produces: a count produces an integer without building rows, and a result set
produces rows once with its views; the output-shape owner then presents it. Consumers read results through typed claim checks, so a Count yields
an integer and Rows yields its row type, with nothing cast or recomputed.

The planner produces the value; the
[inspection envelope](inspection-envelope.md) owner keeps the service layer.
A closing's typed result becomes an envelope's content, and the plan's receipt
and completion evidence can travel as the envelope's typed evidence
companion. A closing can also be its own canonical share, but only when the
[portable query intent](portable-query-intent.md) owner admits a portable
representation of its open query and terminal. Otherwise the envelope keeps
its [non-projectable share outcome](inspection-envelope.md#share-outcome);
the planner does not manufacture a share.

Package-version `--count` shows what this replaces. Today four sites lower it
by hand: they build every version row, apply the row selection, take the
number of rows, and construct an `InspectionEnvelope<int>` from the row
envelope, with a share marked as having no canonical projection. Planned, it
is a Count closing over the version population: the planner produces the
integer and the envelope carries it. The closing becomes the share once the
version population and Count have a portable representation; until then the
share stays non-projectable, as today. For package
versions the gain is one generic lowering instead of four, not speed, since a
package has dozens to hundreds of versions. It is also a first adopter outside
method definitions, which needs sources to be a general abstraction.

## Lowering

Lowering follows one rule: carry type currency from the request through the
pass, and exchange it for plan data only where requests must merge, never
once per unit. The plan stays data, so requests can be validated, merged,
explained, and carried between hosts. Execution becomes types wherever the
shape is known.

### Minimal execution strategy

The pattern has two production lowering families:

1. A **traversal kernel** runs the open query over a source cursor and applies
   the closing's phases. One closed query may run as one specialized loop;
   compatible closed queries may share one traversal and settle independently.
2. A **source-native closing** lets the source issue the exact terminal result
   without producing the reference row stream or acquiring layers that the
   closing does not need. It remains the same closed query and must carry the
   completion evidence required by
   [Source delegation](source-delegation.md).

The reference interpreter is the equivalence oracle, not a third production
strategy. A feature-specific shortcut that cannot identify the open query,
closing, result, outcome, and receipt it implements is a transition path, not
another accepted lowering family.

Terminal specialization is an execution choice inside a traversal kernel, not
an author-facing pattern. A runtime request selects the kernel once before the
cursor advances. The kernel may retain a runtime terminal branch when that is
the smallest fast implementation, or specialize the processor and condition
as type parameters when NativeAOT evidence shows that doing so removes
material nested work. Both forms implement the same closing and pass the same
equivalence gate. Binary-size growth and maintenance cost count against
specialization just as latency and allocation count for it.

This division keeps the strategy consistent without requiring unlike sources
to have identical loops. A metadata table may answer Count from cardinality, a
filtered metadata population may scan without projecting rows, and an IL
population may visit raw operands without resolving row identities. Those are
source-native implementations of the same closing, not new terminal APIs.

A closed query may be lowered to a **kernel**: one loop specialized to its
predicate or projection and its phases, with nothing else to coordinate in
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

### Adoption proof

A lowering strategy is proven for broad adoption only after it demonstrates
all of these roles:

- **Semantic equivalence.** The optimized result, outcome, failure boundary,
  completion, and receipt equal the closed query's reference execution.
- **Minimal work.** Scalar closings neither project nor retain rows, settled
  closings receive no later units, and source-native closings acquire only
  their declared layers. Structural receipts or counters name the avoided
  work.
- **NativeAOT performance.** Exact binaries measure latency, managed
  allocation, and binary-size cost for every supported closing. Peak process
  memory is added when avoided retained graphs or large intermediate
  populations make managed allocation insufficient.
- **Independent adoption.** The strategy serves at least one cheap metadata
  population and one body or similarly nested population without adding
  terminal-specific concepts to either producer's authoring surface.
- **Fusion.** Count and Exists derive from Rows when their predicate completed,
  while non-derivable or independently settling closings share traversal
  without changing their individual failure or settlement semantics.

The first adoption program uses Method-definition queries as the reusable
traversal-kernel witness, exact member-group overloads as the independent
metadata witness, and direct-call and body-use populations as source-native
and nested-work witnesses. Each owner adopts the pattern separately; no one
implementation PR sweeps those owners.

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
- **Thresholds and Rows.** AtLeast(N) and Rows kernels match the
  reference execution under the same gate, in
  `ProducerPlanningTests.ClosedQueryKernel_MatchesTheInterpretedExecutor` and
  `RowsKernel_MatchesTheInterpretedExecutor`, on the experiment branch; merged
  thresholds are gated by
  `Planner_MergesExistsThresholdsToTheLargestAndAllDominates`.
- **Phases.** Closings lowered to phases publish what the named closings'
  reference executions publish, including a strict Window's failure after
  exhaustion and Head's lenient clamp. This is **unverified**; today's kernels
  implement Count, Exists, AtLeast(N), and Rows directly.
- **Typed fused kernels.** A typed fused kernel matches the interpreted fused
  pass's results and outcomes. This is **unverified**: the first typed fused
  kernel fails the whole request on a unit failure rather than each question.
- **Derivation.** Count derived from Rows, and Exists derived from Count, match
  their own reference executions, including a failure after an Exists was
  settled. This is **unverified**.
- **Result sets.** Every keyed view in a result set equals the same closing
  run alone. This is **unverified**.
- **Merging.** Closings merged across consumers each receive the shape they
  asked for. This is **unverified** until level 2 adopts closing (#8574).
- **Minimal execution strategy.** Traversal kernels and source-native closings
  each match the same reference result and receipt, and an adopter introduces
  no third feature-specific execution contract. This is **unverified** until
  the independent-adoption program above completes.
- **Minimal work and performance.** Structural evidence proves that scalar
  closings avoid row projection and retention, and exact NativeAOT evidence
  records latency, managed allocation, and binary size for each lowering.
  Cross-source proof and peak-memory evidence remain **unverified**.
- **Per-unit routing and settlement.** The
  [bounded TLA+ model](models/open-query-routing-settlement/README.md) checks
  exclusive classification, inclusive fan-out, unit-ordered Head results,
  no work after one closing settles, and shared traversal until every closing
  settles or exhausts. It establishes evidence about the pattern, not an
  implementation gate; implementation equivalence remains **unverified**.
