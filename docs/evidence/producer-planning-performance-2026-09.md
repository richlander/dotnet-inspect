# Producer Planning performance record, September 2026

## Status

Measured evidence for [Producer Planning](../design/producer-planning.md) and
[open and closed queries](../design/open-and-closed-queries.md), recorded
2026-09-26 and 2026-09-27. It keeps every approach that was tried, including
the ones that lost, and the comparisons with hand-written loops, NLinq-style
folds, and today's product paths. The data is
[producer-planning-performance-2026-09.tsv](producer-planning-performance-2026-09.tsv):
one row per run, machine, variant, build, and assembly, with the median of the
round medians, the round range, and allocation. Round-level data, the probes,
and the oracles are on branch `exp/producer-planning-async-count` under
`tools/PlanningProbe`.

The numbers are evidence, not a contract. They hold for the named builds,
inputs, and machines.

## Method

**Machines.**

| Machine | Hardware and OS | Runtime identifier |
| --- | --- | --- |
| mac | Apple M4 Pro, 12 cores, macOS 27.0 | osx-arm64 |
| annies-mac-mini | Apple M4, 10 cores, macOS 27.0 | osx-arm64, running the Mac-built binaries |
| merritt | AMD Ryzen 9 9900X, 24 threads, Ubuntu 24.04 | linux-x64, clang linker |
| fernie | Intel Core i9-9900K, 16 threads, Ubuntu 26.04 | linux-x64, gcc linker |

**Inputs.** Eight real assemblies: CommandLineParser 2.9.1, Humanizer.Core
2.14.1, Mono.Cecil 0.11.6, Newtonsoft.Json 13.0.4, System.Text.Json 9.0.0,
NuGet.Packaging 7.9.0, Microsoft.CodeAnalysis.CSharp 5.9.0, and
System.Private.CoreLib 9.0.14.

**Builds.** Every probe is a NativeAOT publish in Release with
`OptimizationPreference=Speed`, invariant globalization, and EventSource off.
Allocation profiles used a separate JIT build with EventSource on.

**Procedure.** Each probe warms up with five calls, then times calls until its
budget (2 to 3 seconds) or 5,000 samples, with at least 20, and reports the
median, the 10th and 90th percentiles, and allocated bytes per call. Each run
rotates its binaries and variants through six or eight rounds, so that drift
affects every variant equally. A cell is the median of its round medians.
Ratios divide by the oracle measured by the same binary in the same run.
Every variant's answer was compared on every assembly, and no cell disagreed.

**Layout control.** The padding experiment is run `presence-layout-padding` in
the data, with variants `pad40` and `pad97`.

**Exclusions.** A run is excluded, but kept in the data with its reason, when
another workload loaded the machine: async count step 3 on fernie (load 22 to
29), shared classification on fernie (load 10 to 12), scope guards on merritt
(load 13), and generic producer state on merritt (load 6).

## Oracles

- **Hand-rolled, original.** For unsafe presence, the retired
  `LibraryBodyIndex.HasUnsafeEvidence` at merge base `7de7e646c`, unchanged.
  It is optimal for its question, so the target is parity.
- **Hand-rolled, constructed.** No original existed for async count or the
  classified questions, so their oracles were written for this record. They
  hoist the compiler-generated-type filter out of the method loop. A flat
  variant without the hoist separates hoisting from planning.
- **NLinq-style.** A scratch copy of [NLinq](https://github.com/agocke/NLinq)
  at `229e243` (MIT), with a struct source over method definitions that
  overrides `Fold` with nested loops. It gives a filtered struct fold for one
  question and a composite struct accumulator for several. It is the ceiling
  for call-site composition.
- **Legacy product paths.** The naive baselines. For unsafe presence, every
  method-evidence row is built and then checked for any. For the classified
  questions, `MethodClassificationScanner.Scan` classifies every public method
  into rows that are then filtered, as the CLI does.

## Unsafe presence: rows, then check, against Exists

The legacy question built rows for every method and then checked whether any
existed. The Exists terminal answers the same question.

| Assembly | Rows, then check (ms) | Exists (ms) | Speedup |
| --- | ---: | ---: | ---: |
| CommandLine | 71.28 | 8.47 | 8.4× |
| Humanizer | 40.50 | 5.56 | 7.3× |
| Mono.Cecil | 75.59 | 14.05 | 5.4× |
| Newtonsoft.Json | 172.50 | 5.74 | 30.0× |
| System.Text.Json | 139.50 | 0.18 | 758.2× |
| NuGet.Packaging | 186.62 | 7.37 | 25.3× |
| Roslyn C# | 1695.21 | 20.27 | 83.7× |
| CoreLib | 1760.57 | 2.70 | 652.2× |

## Unsafe presence: the reference executor and its changes

Unsafe presence spends about 1.5 microseconds per method in its probe, so the
executor's own cost stays near the noise floor. Every build stayed within a
few percent of the original loop.

| Build | Range against the original loop |
| --- | --- |
| Reference executor, `f7f1468c0` | 0.98–1.03× |
| First single-producer kernel, uncommitted | 1.00–1.03×; 1.02–1.07× on fernie |
| LINQ removal, `14138031c` | 0.99–1.02× |
| Executor steps ported, `d73a445f0` | 0.99–1.03×; 1.00–1.08× on fernie |
| Scope guards and type scope ported, `925e73ee2` | 0.99–1.02× |
| Open query through the closed-query kernel, `c20817038` | 0.97–1.02×; 1.00–1.07× on fernie |

- **Allocation.** The reference executor allocated 127 KB more than the
  original on CommandLine. A per-method fact dictionary accounted for about
  72 KB, removed by retaining facts only when a reader needs them. A per-method
  fact list accounted for most of the rest, removed by the fold contract. About
  50 KB remains unexplained and is below the allocation profile's resolution.
- **Dispatch.** With one or two producer types in the program, NativeAOT's
  whole-program analysis devirtualized the executor completely, emitting a
  type switch for two producers. With five producer types it fell back to
  interface dispatch per unit.

## Async count: a cheap producer

The per-method work is tens of nanoseconds, so executor overhead is visible.
Multiples of the hoisted hand-rolled loop, mac:

| Stage | CommandLine | Humanizer | Mono.Cecil | Newtonsoft.Json | System.Text.Json | NuGet.Packaging | Roslyn C# | CoreLib |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| First planned version | 2.34× | 2.07× | 3.85× | 2.21× | 2.10× | 4.06× | 4.90× | 2.02× |
| Module lookup only when declared | 1.28× | 1.20× | 1.49× | 1.22× | 1.24× | 1.44× | 1.36× | 1.12× |
| Fold into a typed accumulator | 1.27× | 1.20× | 1.48× | 1.20× | 1.21× | 1.40× | 1.33× | 1.11× |
| Cursor on the stack | 1.21× | 1.15× | 1.37× | 1.16× | 1.17× | 1.33× | 1.25× | 1.08× |
| Prerequisite check only with dependencies | 1.20× | 1.14× | 1.35× | 1.14× | 1.15× | 1.30× | 1.24× | 1.06× |
| Open query, interpreted (type-scoped) | 1.08× | 1.11× | 1.28× | 1.09× | 1.12× | 1.20× | 1.17× | 1.09× |
| Open query, closed-query kernel | 1.01× | 1.02× | 1.05× | 0.99× | 1.00× | 1.02× | 1.03× | 1.01× |
| NLinq-style fold (flat) | 1.10× | 1.07× | 1.16× | 1.07× | 1.08× | 1.14× | 1.11× | 1.05× |

| Exists stage | CommandLine | Humanizer | Mono.Cecil | Newtonsoft.Json | System.Text.Json | NuGet.Packaging | Roslyn C# | CoreLib |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| First planned version | 2.85× | 1.91× | 3.21× | 7.31× | 42.90× | 12.46× | 4.34× | 3.29× |
| Module lookup only when declared | 1.08× | 1.13× | 1.29× | 1.06× | 1.45× | 1.15× | 1.23× | 1.09× |
| Closed-query kernel | 1.02× | 1.02× | 1.06× | 1.01× | 1.10× | 1.00× | 1.02× | 1.02× |

- The first planned version built the module lookup, about a dozen resolvers
  and indexes, although no producer declared it. That fixed cost was about 170
  microseconds per execution and dominated Exists.
- The flat NLinq fold is at most 1.16×, and all of its gap is the unhoisted
  type filter.
- The closed-query kernel reaches parity and beats the flat NLinq fold,
  because the open query's type scope hoists the filter.

## Classified methods: three questions in one pass

P/Invoke rows, an async count, and whether any other signature carries a
pointer, closed with Rows, Count, and Exists. Multiples of the hand-fused
loop, mac:

| Stage | CommandLine | Humanizer | Mono.Cecil | Newtonsoft.Json | System.Text.Json | NuGet.Packaging | Roslyn C# | CoreLib |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Legacy superset scan | 2.03× | 2.05× | 2.59× | 1.90× | 1.94× | 3.43× | 2.17× | 4.54× |
| Planned, separate executions | 2.23× | 2.37× | 2.81× | 2.37× | 2.36× | 2.74× | 2.42× | 1.33× |
| Planned fused, duplicated predicates | 2.15× | 2.30× | 2.62× | 2.28× | 2.29× | 2.61× | 2.31× | 1.29× |
| Hand-written, separate loops | 1.86× | 2.05× | 2.14× | 2.03× | 2.02× | 2.12× | 1.97× | 1.12× |
| + shared classification (A) | 2.17× | 2.12× | 3.16× | 2.07× | 2.11× | 2.92× | 2.63× | 1.65× |
| + single-slot facts (B) | 1.99× | 1.91× | 2.76× | 1.88× | 1.90× | 2.60× | 2.23× | 1.46× |
| + direct dependency reads (C) | 1.74× | 1.67× | 2.27× | 1.63× | 1.65× | 2.17× | 1.87× | 1.35× |
| + scope guards (D) | 1.41× | 1.40× | 1.77× | 1.37× | 1.40× | 1.72× | 1.51× | 1.22× |
| + type scope | 1.26× | 1.40× | 1.69× | 1.34× | 1.36× | 1.57× | 1.44× | 1.19× |
| + generic producer state (K1) | 1.21× | 1.31× | 1.58× | 1.26× | 1.32× | 1.46× | 1.36× | 1.17× |
| NLinq composite fold | 1.12× | 1.12× | 1.21× | 1.12× | 1.12× | 1.21× | 1.13× | 1.06× |
| Typed fused kernel (K4) | 1.03× | 1.07× | 1.14× | 1.06× | 1.07× | 1.08× | 1.08× | 1.04× |
| Typed fused kernel, inlined (K4a) | 1.02× | 1.04× | 1.05× | 1.03× | 1.03× | 1.04× | 1.03× | 1.01× |

Rows from different runs are comparable because each divides by the
hand-fused loop measured in its own run.

- **Fusion shares only declared work.** With each question re-testing scope
  and re-decoding async attributes, one pass cost nearly as much as three
  separate executions. A shared classifier helped only after its facts stopped
  going through a per-unit dictionary and its reads stopped going through two
  lookups.
- **Scope is data the planner can use.** Scope guards and type scope cut the
  visits a flat pipeline cannot avoid.
- **Generic producer state** removed per-unit interface dispatch. It helped
  fused passes on arm64 by 0.02× to 0.10× and did not change x64 or single
  open queries. The likely cause is NativeAOT's interface dispatch cells,
  which are cheap when a call site sees one type and slow when it sees
  several. This was not confirmed directly.
- **Typed fused kernels** compose a constant request into one nested generic
  type. With the classifier, composite, and sinks inlined, the loop makes the
  same calls as the hand-written one: 1.01–1.05× on all four machines.
  Hoisting the try region out of the loop changed nothing measurable.
- **NLinq's composite fold** reaches 1.06–1.21× on mac and up to 1.40× on
  merritt. It cannot hoist a type filter, and its call-site composition cannot
  merge questions from different consumers.

## Closed-query kernels

Open queries closed by each terminal, as multiples of the matching hoisted
hand-rolled loop:

| Closing | Machine | Interpreted | Kernel |
| --- | --- | ---: | ---: |
| Count | mac | 1.08–1.28× | 0.99–1.05× |
| Count | annies-mac-mini | 1.05–1.27× | 1.01–1.04× |
| Count | merritt | 1.10–1.43× | 1.05–1.18× |
| Count | fernie | 1.08–1.27× | 1.03–1.09× |
| Exists | mac | 1.07–1.48× | 1.00–1.10× |
| Exists | annies-mac-mini | 1.04–1.47× | 1.01–1.13× |
| Exists | merritt | 1.07–1.57× | 1.03–1.39× |
| Exists | fernie | 1.06–1.50× | 1.02–1.24× |
| Rows | mac | 1.06–1.36× | 1.01–1.06× |
| Rows | annies-mac-mini | 1.08–1.34× | 1.02–1.06× |
| Rows | merritt | 1.10–1.53× | 1.05–1.19× |
| Rows | fernie | 1.09–1.33× | 1.02–1.07× |
| Count at least 5 | mac | 1.04–1.25× | 1.00–1.03× |
| Count at least 5 | annies-mac-mini | 1.06–1.25× | 1.02–1.04× |
| Count at least 5 | merritt | 1.08–1.42× | 1.03–1.18× |
| Count at least 5 | fernie | 1.06–1.25× | 1.01–1.06× |

| Legacy path | Machine | Multiple of hand-rolled |
| --- | --- | ---: |
| Classified rows, then filter async (Rows) | mac | 2.57–5.08× |
| Classified rows, then filter async (Rows) | annies-mac-mini | 2.44–4.72× |
| Classified rows, then filter async (Rows) | merritt | 2.77–5.74× |
| Classified rows, then filter async (Rows) | fernie | 2.28–4.61× |

The widest Exists ranges come from System.Text.Json, which settles within
about 4 microseconds, so fixed setup dominates. Rows kernels allocate within
1 KB of the hand-rolled loop. merritt runs consistently higher than the other
machines, and the cause is not yet known.

## Allocation profiles

- **Async count.** About 254 KB per CommandLine scan in every variant, planned
  or hand-rolled, and 93% of it is attribute type-name text that
  `AttributeReader.HasAttribute` builds to compare names. Comparing name
  handles first would remove it for every caller.
- **Unsafe presence.** About 26 MB per CommandLine scan in the original and
  planned paths alike, mostly rich type decoding during call resolution.
  Calls resolve without a per-target memo, while call sites repeat targets
  2.4 to 5.9 times across the eight assemblies.

## What the record shows

1. The largest wins came from the plan knowing something the executor was not
   using: which resources are needed, how long facts must live, who reads
   what, and which units and types are in scope.
2. Fusion helps only when producers share work and declare it. A fused pass
   over independently written producers is a monolith's loop with the
   duplication left in.
3. Whole-program devirtualization is not a substitute for specialization. It
   gives up as producer types accumulate.
4. Specialization gives direct calls, and inlining the small per-unit members
   turns them into one loop. Kernels pay when per-unit work is cheap; for
   expensive probes they are noise.
5. Plans are data, and execution is types. Requests stay data where they must
   be validated, merged, and explained. Closed queries lower to kernels, and
   constant multi-question requests lower to typed fused kernels.

## Open items

- fernie runs above the original loop for some builds of unsafe presence and
  not others, including a build without the closed-query kernel. Rebuilding
  `c20817038` with inert padding methods, which shift code layout without
  changing logic, moved CoreLib from 1.068× to 1.066× with one padding and to
  0.996× with another, so that outlier is code layout. fernie's i9-9900K is
  affected by the jump-conditional-code erratum, whose microcode mitigation
  makes branch alignment matter; hardware counters would confirm it, but
  unprivileged `perf` is disabled on the host. The remaining 1% to 2.5% on
  other assemblies persisted under both paddings and is unexplained. merritt
  measured 0.96–1.02× in the same run.
- merritt's kernel ratios are consistently the highest of the four machines.
- About 50 KB of planned-path allocation over the original loop is
  unexplained.
- The typed fused kernel fails the whole request on a unit failure rather than
  each question, and has no equivalence gate yet.

## Remaining opportunities

Ranked by expected effect on command latency, following the priorities in
[Planning API tradeoffs](../design/planning-api-tradeoffs.md): skipping work
first, then doing less per unit, then doing it faster.

| Rank | Opportunity | Evidence | Owners | Author cost |
| ---: | --- | --- | --- | --- |
| 1 | Lower row selection into sources: Head, Skip, Window, and Tail as closings and stages, with stop policies | Rows are built in full and then trimmed; Exists over rows-then-check measured 5–758× | Semantic row selection, source delegation, QuerySpace, Producer Planning | None; derived from the request |
| 2 | Split open queries into a predicate and a projection | Skipped and counted rows should pay only the predicate; legacy rows spend most of their cost formatting text | Producer Planning | Two members instead of one |
| 3 | Source-native answers: counts from table sizes, random access, reverse order, and reference-table presence checks | A self-typed source, as in NLinq, can answer a closing without visiting | QuerySpace (#8577) | None for authors; per source |
| 4 | Scope at coarser grains | Type scope already skips whole types; assemblies and packages are next | Producer Planning, QuerySpace | One predicate per grain |
| 5 | Merge requests across consumers into one traversal | Planned subsets beat the superset scan 2–5×; merging keeps that when several sections ask | QuerySpace (#8574) | None |
| 6 | The classified-methods migration as the first production caller of scope guards and type scope, with the Async Methods section as the demo | Guards and type scope have no production caller yet | Producer Planning, CLI sections | None for consumers |
| 7 | Typed fused kernels in production, generated, with per-question outcomes and an equivalence gate | 1.01–1.05× of the hand-fused loop against 1.2–1.6× interpreted | Producer Planning | None if generated |
| 8 | Memoize call resolution in the unsafe probe | 26 MB per CommandLine scan; call sites repeat targets 2.4–5.9× | Analysis | None |
| 9 | Compare attribute names by handle before materializing text | 93% of async-count allocation is attribute name text | Metadata | None |
| 10 | Share the opened subject across executions | Every execution opens its own readers today | Assembly session lifetime (#8576) | None |
| 11 | Reduce fixed per-execution setup | Exists that settles in about 4 microseconds runs 1.10–1.39× | Producer Planning | None |
| 12 | Batch units per dispatch in interpreted fused passes | Dispatch returns once a program has many producer types | Producer Planning | None |
| 13 | Typed guard declarations instead of raw class masks | Masks are hard to read and easy to get wrong | Producer Planning | Less than today |
| 14 | Typed claim checks for results | Results reach consumers typed, without casts | Producer Planning | Slightly less than today |
| 15 | Investigate merritt's higher kernel ratios, fernie's residual 1–2.5%, and 50 KB of unexplained allocation | See [Open items](#open-items) | Investigation | None |

### Row windows

The first opportunity has the most detail, because most commands are row
oriented and most requests are small windows over large populations.

Today `-n`, `--tail`, and `--rows A..B` apply to a complete row list:
`RowSelectionExecutor.Apply` receives every row and then selects.
[Source delegation](../design/source-delegation.md) already defines how a
source may take such work, proven by completion evidence, with
`Head(N)` to Count as its canonical witness, and
[semantic row selection](../design/semantic-row-selection.md) owns what Head,
Tail, Window, and Top mean. No production source has adopted delegation yet.

In the terms of [open and closed queries](../design/open-and-closed-queries.md):

- **Head(N)**, for `-n N`, closes an open query with Rows and a stop policy
  at N selected units. Its witness is reaching N, or exhausting the source
  with fewer.
- **Skip(N)**, proposed as `CountContinue(N)`, is a stage: it consumes N
  selected units, testing only the predicate, and hands the live cursor to the
  next participant.
- **Window(A..B)**, for `--rows A..B`, is Skip(A - 1) followed by
  Head(B - A + 1) over one cursor. When fewer than A units exist, proving the
  window fails requires exhausting the source, and that failure stays the row
  selection owner's decision, reported as evidence.
- **Tail(N)** keeps only the last N selected units and projects them at the
  end. It stops early only on a source that can traverse in reverse.
- **Stop policies**, proposed as `CountExitOracle`, decide when to stop from
  progress: Head, thresholds, and every question settled in a fused pass. A
  declared policy is a pure function of progress, so its stop is exact and
  explainable. An external stop, such as a user abort, a budget, or a page that
  is full, is recorded as an incomplete stop and never presented as an exact
  answer. Both stop at a unit boundary, before the next untrusted read.
- **Ordering** limits all of these. When a section orders rows by anything
  other than traversal order, Head and Window cannot stop early; a bounded
  top-N with deferred projection still avoids projecting the rest.

In a kernel, each stage, terminal, and stop policy is a struct type parameter,
so `Skip(3)` followed by `Head(3)` compiles into one loop, as the typed fused
kernel does.

The first measurement should be Head(6), Skip(3) then Head(3), and Skip(99)
then Head(11) over the async-methods open query, with the projection split
out and made realistically expensive. It should compare them with building
every row and then selecting, and with a hand-written loop.

## Reproducing

Check out `exp/producer-planning-async-count`. Publish
`tools/PlanningProbe/PlanningProbe.csproj` with
`-c Release -r <rid> -p:IsPublishable=true`, then run
`PlanningProbe <budget-ms> <variant> <dll>...`. The variant names match the
`variant` column. With a build published with `-p:ProbeProfile=true`,
`PlanningProbe alloc <calls> <variant> <dll>` prints an allocation profile,
and `PlanningProbe calls <dll>...` counts call sites against distinct call
targets. Unsafe presence used a separate `PresenceProbe` with the same
measurement loop, whose `ProbeSide` build property selects the original, the
reference executor, or rows. Round-level data is in
`tools/PlanningProbe/results/rounds-2026-09.tsv` on the same branch.
