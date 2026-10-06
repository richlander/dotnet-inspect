# Planning API tradeoffs

## Status

Focused design rationale for the authoring and execution surface of
[Producer Planning](producer-planning.md) and
[open and closed queries](open-and-closed-queries.md). It records the
priorities that decide between usability, planning, and performance, where
complexity is allowed to live, what was taken from
[NLinq](https://github.com/agocke/NLinq) and what was not, and a cost and
benefit ledger for each optimization measured in the
[performance record](../evidence/producer-planning-performance-2026-09.md).
It defines no contract; the owning designs do.

## Priorities

In order:

1. **Planner-capable.** Requests stay data that the planner can validate,
   merge across consumers, explain, and lower. A feature the planner cannot
   see is not a planning feature.
2. **Performance.** dotnet-inspect is row-oriented, so latency is what tool
   users see. Skipping work is worth far more than doing it faster.
3. **Ergonomics.** Authors should write little and learn few concepts. The
   goal is not LINQ-level usability; sharper edges are acceptable where they
   buy planning or significant performance.

Two rules make the ordering pragmatic:

- **Take significant performance wins even when they cost some
  ergonomics.** Tool users see the latency; they never see the API.
- **Refuse small performance wins that damage ergonomics.** A few percent on a
  microbenchmark does not justify a harder authoring surface.

A win is significant when it is visible in a command's latency: a skipped
traversal, an avoided materialization, or a large fraction of per-row cost.
It is small when it is a few percent of work that is already cheap.

## Where complexity lives

Complexity is budgeted by audience. Each concept belongs to the lowest tier
that can own it, and nothing an author does not need escapes the substrate.

| Tier | Who | What they write or see |
| --- | --- | --- |
| Consumers | Commands, sections, the website | A question and a terminal: `HasEvidence`, a count, rows, a window |
| Common authors | Most analyses | One open query: a struct predicate or projection, the layers it reads, optionally a type scope |
| Advanced authors | A few shared analyses per unit kind | A classifier with scope guards on it, or an owner-defined Fold |
| Substrate | Planning and QuerySpace maintainers | Planner, executor, kernels, stop policies, retention, resource gating, equivalence gates |
| Generated | Nobody by hand | Typed fused kernels for constant multi-question requests |

The substrate derives everything it can from declarations: which resources to
acquire, how long facts live, which passes exist, which units and types are in
scope, and which kernel runs. An author who has to configure one of those has
found a missing derivation.

## What was taken from NLinq

NLinq makes one pipeline as fast as a hand-written loop by making the whole
pipeline a type. The contrary view to this design is fair: several costs paid
here were NLinq's lessons, relearned by measurement.

| Question | NLinq | This design | Why |
| --- | --- | --- | --- |
| Is the query a type or data? | A type, composed at the call site | Data until closed; execution lowers to types | Merging across consumers, rejection, explanation, and portability need data. Kernels recover the speed. |
| Per-unit logic | Struct functions (`IFunc`) | Struct predicates and projections | Taken. Specialization gives direct, inlinable calls. |
| Terminals | Folds over struct functions | Typed fold contract and kernels per terminal | Taken, late. The first executor kept every fact in a list. |
| Composition | Nested generic stages | Nested generic sinks, only for constant requests | Taken where the shape is known in code. It is not an authoring surface. |
| Sources | Self-typed enumerators that can override terminals | Not yet | The largest remaining gap. Sources that answer natively skip work. |
| Early exit | `Any` pulls until the first match | Stop policies, stopping before the next untrusted read | Different mechanism, same effect, with failure containment. |
| Call-site type arguments | Required | Refused | Ergonomics, and the planner owns composition. |
| Merging consumers, failure containment, receipts | Not provided | Required | The planner's purpose, and untrusted input. |

The reference executor was written interpreter-first to pin down correctness:
failure propagation, receipts, and stopping before an untrusted read. It
reached NLinq's execution lessons only by measurement: LINQ inside the
executor, a list of every fact, per-unit dictionaries, and interface dispatch
per unit. The lesson for future substrate is to start from NLinq's execution
shape (struct functions, folds, specialized loops) and add the planner's
obligations on top, rather than the reverse.

## Ledger

Each optimization, what it cost authors, and whether it stays. Gains are from
the performance record.

| Optimization | Performance | Ergonomic cost | Decision |
| --- | --- | --- | --- |
| Acquire only declared resources | Up to 40× on early Exists | None; derived | Keep |
| Fold contract | Removes per-fact retention | Seed, accumulate, and complete for custom Folds; hidden for predicates and rows | Keep |
| Retention decided by the plan | Removes per-unit dictionaries | None; derived | Keep |
| Generic producer state | 0.02× to 0.10× on fused passes, arm64 | None; substrate | Keep |
| Struct predicates and projections | Parity with hand-written loops for cheap work | A struct instead of a lambda | Keep. A sharp edge worth having. |
| Closed-query kernels | 1.1× to 1.5× to parity for cheap work | None; derived | Keep |
| Shared classification with scope guards | Removes duplicated per-unit work in fused passes | A classifier and guard declarations; raw masks today | Keep, with typed guard declarations instead of masks |
| Type scope | Skips whole types | One predicate | Keep |
| Typed fused kernels | 1.2× to 1.5× over the interpreted fused pass | Nested generic types | Keep only if generated or limited to a few constant requests |
| Inlining attributes on per-unit members | 0.03× to 0.09× in typed fused kernels | Invisible if confined to the substrate and generated code | Keep in the substrate; do not require it of authors |
| One try region per traversal | Not measurable | Slightly less clear code | Do not pursue |
| First single-producer kernel over producer classes | Not measurable for expensive work | A second execution path | Superseded by closed-query kernels |

## Decision rules

- A new author-facing concept needs a planning reason or a significant,
  measured performance reason, and a production caller.
- Prefer a derivation over a declaration, and a declaration over
  configuration.
- Prefer skipping work to making work faster. Plan-level wins come first:
  closing with the right terminal, scope, stop policies, and source-native
  answers.
- Keep performance mechanisms that cost ergonomics in the substrate or in
  generated code, where tool users benefit and authors never see them.
- Record every measured choice, including the rejected ones, in the
  performance record, so a later decision can see what was tried.
