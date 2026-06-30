# Findings — Performance Triage corpus, dotnet-inspect 0.15.0 vs 0.16.0

Overall report for the 6-library Performance Triage corpus. The per-run evidence
is in the 12 `*-0.15.0.md` / `*-0.16.0.md` files (one per library per tool
version); this file is the cross-version analysis and the curve baseline.

Run date: 2026-06-30. Tool: published, via `dnx dotnet-inspect@<ver> -y -- ...`.

## Verdict (favorable or not?)

**0.16.0 is non-regressive across the entire corpus, with one clear precision
win and one honest additive — and no library got worse.** Raw row-count deltas
are *not* the quality signal: a drop is only good if it removed false positives,
and an increase is only bad if it added noise to the actionable prefix. Measured
against the signal that matters — *did false positives fall while the
high-confidence pay-dirt stayed intact?* — every library is favorable or
neutral, none negative.

| Library | Rows 0.15 → 0.16 | High-conf pay-dirt 0.15 → 0.16 | False positives removed | Verdict |
| ------- | ---------------- | ------------------------------ | ----------------------- | ------- |
| System.Text.Json@10.0.9 | 158 → 132 (−26) | 3 → 3 (kept) | 27 `span-to-array-copy` | **Favorable** — precision up, zero pay-dirt lost |
| Aspire.Hosting@13.4.6 | 529 → 553 (+24) | 21 → 21 (kept) | 0 | **Favorable** — additive, all +24 low-conf/amortized |
| Newtonsoft.Json@13.0.4 | 345 → 345 (0) | 15 → 15 (kept) | 0 | Neutral — identical output |
| Serilog@4.3.1 | 92 → 92 (0) | 3 → 3 (kept) | 0 | Neutral — identical output |
| Polly@8.7.0 | 434 → 434 (0) | 0 → 0 | 0 | Neutral — identical output (facade assembly) |
| AutoMapper@16.1.1 | 199 → 199 (0) | 1 → 1 (kept) | 0 | Neutral — identical output |

Read the verdict column, not the row-count column. The two libraries that moved
both moved in the favorable direction; the four that did not move kept every
high-confidence pay-dirt row.

## Why row count is the wrong metric

A Performance Triage table is a precision/recall instrument, not a bug count:

- **A drop is favorable only if it is false positives.** System.Text.Json fell
  158 → 132 entirely because 0.16.0 removed 27 `span-to-array-copy` rows that
  were non-escaping, deliberate buffer materialization in a hand-optimized
  library. The 3 high-confidence loop-boxing rows — the actual pay-dirt — are
  untouched. Fewer rows, same findings: favorable.
- **An increase is favorable if it is correctly demoted.** Aspire rose 529 → 553
  because the new `async-state-machine` shape added 24 rows — every one
  low-confidence and amortized (async-iterator / `IAsyncEnumerable` streaming
  methods, allocated once per enumeration, not per item). The high/medium bands,
  and the ranked actionable prefix, are unchanged. More honest context without
  polluting pay-dirt: favorable.
- **Flat is fine.** Four libraries are byte-identical between versions, with
  every high-confidence row preserved — exactly what you want from a release
  that should only fire where its mechanisms apply.

## The two changes 0.16.0 actually makes

### 1. Precision — escape-gated allocation triage

The headline accuracy win. 0.16.0 promotes a span→array (and local-array) copy
only when reaching-definitions proves the array escapes. On System.Text.Json
that erased all 27 `span-to-array-copy` false positives while keeping the
high-confidence pay-dirt; the Facts overlay even ties the top row to `alloc.box
int` at `IL_035F`. It fired exactly where it should (a span-heavy library) and
nowhere it should not (the other five had no non-escaping span copies to gate).

### 2. Recall — the `async-state-machine` shape (correctly narrow)

0.16.0 adds the Rung 7 `async-state-machine` shape, gated to be **class-only**
and **amortized**. It lit up only on Aspire (+24 low-confidence rows, all
async-iterator class state machines) and stayed silent on Polly and Newtonsoft
despite their heavy async — because ordinary optimized async compiles to *struct*
state machines, which the gate excludes by design (verified: `Polly.Core` also
returns zero async rows). Aspire firing and Polly not is the intended behavior.
The companion `materialize-in-loop` shape produced zero rows across the corpus.

## Two critical views (skill variants)

Each per-library analysis was read through two independent skill lenses; the
cross-corpus conclusion of each:

- **Variant A — tool-embedded `skill performance` (capability lens).**
  Consistently isolates the real pay-dirt as a small high-confidence,
  loop-marked prefix: loop boxing (System.Text.Json), hot capturing-delegate /
  string-build-in-loop (Aspire, Serilog), `BigInteger` boxing + reflection
  captures (Newtonsoft), config-time delegate allocation (AutoMapper
  `AddMapsCore`), and one predicate hot path (Polly
  `ExceptionPredicates.FirstMatchOrDefault`). Everything else is breadth.
- **Variant B — richlander-dotnet-skills `dotnet-allocation-triage` (validation
  lens).** Treats every row as a *candidate* requiring dynamic confirmation
  (BenchmarkDotNet `[MemoryDiagnoser]`, `dotnet-trace --profile gc-verbose`,
  `dotnet-counters`) before any code change, and deprioritizes the medium/low
  tail a profiler would likely show as cold.

The lenses agree on the bottom line — pay-dirt is real but narrow — and are
complementary: Variant A ranks and explains the static signal; Variant B gates
it against runtime ground truth.

## Curve baseline and measurement cadence

This is the **first point on the improvement curve**. The curve's data points,
per library, are: total rows, rows by shape, rows by confidence, and the
high-confidence pay-dirt count — captured in each per-run file. The qualitative
check is the verdict column above.

To add the next point, re-run the same pinned corpus on the new tool version and
regenerate these files (see `README.md` for the exact commands). The expectation
as the Research-overlay infrastructure matures (`Cost`/`Semantics`/`Facts`,
reaching-definitions, the static→dynamic `--il-offset` bridge) is richer,
more-correlated signal — measured the same way, against the same corpus.

### Follow-ups to watch on the curve

- `materialize-in-loop` produced zero rows across all six libraries — needs a
  known-positive fixture to confirm recall.
- `async-state-machine` recall is class-only by design; struct-state-machine
  async cost, if it ever becomes a target, needs a separate signal and a
  measured reason, not a loosening of this gate.
- Polly's real async cost lives in `Polly.Core`; future runs should add the
  implementation assembly alongside the `Polly.dll` facade.
