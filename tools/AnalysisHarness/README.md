# Analysis Harness

The fixture-catalogue side of the analysis harness (#1818, Layer 1) — the analysis analog of
the decompiler generated-fixture catalogue (#1742). It makes the `ILInspector.Analysis` rung
fixtures **addressable** and **generated**, and grades each target method's signals against an
**expected-signal ledger** using the real analyzer.

Unlike the decompiler harness there is no mechanical compile-back oracle: analysis output is
triage candidates a human/agent judges. The mechanical part the catalogue *can* check is "does
the analyzer emit the declared signals?", so each entry declares its expected `MethodSignals` /
`OptimizationOpportunity` outcome. The interesting entries are **owned boundaries** —
deliberately-accepted false positives/negatives at the SRM-only, no-referenced-assembly-loading
edge — recorded so a future improvement flips them on purpose rather than as a silent diff.

## Run

```bash
dotnet build tools/AnalysisHarness -c Release
DLL=tools/AnalysisHarness/bin/Release/net11.0/analysis-harness.dll

dotnet "$DLL" --generated-fixtures list          # list entries + expected outcomes
dotnet "$DLL" --generated-fixtures               # build and grade every fixture
dotnet "$DLL" --generated-fixtures alloc --json  # grade a subset (id/prefix/tag), JSON output
dotnet "$DLL" --generated-fixtures exception.unsuffixed.external --keep  # keep temp projects
```

Each fixture builds in isolation: a consumer assembly (the inspected one) plus, when the
fixture is cross-assembly, a referenced external assembly (with an extern alias for the
name-collision case). Isolation keeps same-fully-qualified-name and alias fixtures from clashing
across catalogue entries. Fixtures may opt into framework references when the signal depends on a
trusted platform identity, such as the real ASP.NET Core `RenderTreeBuilder`.

## Deferred catalogue seeds

The catalogue should pin only shapes with a clear discriminator. Deferred #1871 seeds stay out of
the generated ledger until their analyzer shape exists:

| Seed | Blocker |
| --- | --- |
| State-machine allocation | The useful form is cross-method: an iterator/async state machine allocated and consumed in an outer loop. The naive per-method constructor shape floods every iterator and is intentionally not pinned (#1805). |
| LINQ materialize/dataflow shapes | Need the #1807 dataflow/escape discriminator so transient, streamable object graphs can be separated from necessary model construction. |

## Test

`ILInspector.Analysis.Tests/AnalysisFixtureCatalogTests` runs the catalogue and asserts each
target. It is `[Trait("Speed", "Slow")]` (every fixture runs `dotnet build`), so it is excluded
from the fast PR lane — like the decompiler generated-fixture catalogue. Run it directly:

```bash
dotnet artifacts/bin/ILInspector.Analysis.Tests/release/ILInspector.Analysis.Tests.dll \
  -method "*SeedCatalogue*"
```

## Layers above this

This is Layer 1 of #1818. The corpus stability sensor (Layer 2) is `--corpus-list`:

```bash
dotnet "$DLL" --corpus-list assemblies.txt                                    # stability card
dotnet "$DLL" --corpus-list assemblies.txt --diff-corpus-baseline corpus/analysis-baseline.json
```

It sweeps the analyzer over a pinned corpus (one assembly path per line) and reports the
mechanically-checkable signals — did every assembly open, did the analyzer choke (recoverable
diagnostics) — diffed against a committed baseline. A REGRESSION (an assembly that stops opening,
times out, or whose diagnostics increase) exits nonzero; signal-count DRIFT is reported, not
failed. Each assembly is bounded by a per-assembly timeout so one pathological input cannot hang
the sweep.

Allocation metadata readout is a measurement-only companion for deciding whether
path/escape evidence is strong enough to inform future confidence or ranking work:

```bash
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
dotnet run --project tools/AnalysisHarness -c Release -- \
  --allocation-readout /tmp/corpus-assemblies.txt --top 12
dotnet run --project tools/AnalysisHarness -c Release -- \
  --allocation-readout /tmp/corpus-assemblies.txt --json
```

The readout aggregates occurrence buckets (`kind`, `allocation`, `path`,
`path-confidence`, `post-dominance`, `escape`) and opportunity buckets (`shape`,
`allocation`, `path`, `path-confidence`, `post-dominance`, `confidence`), plus
cross-tabs. Text output caps each bucket with `--top`; JSON keeps all buckets.
Use it before changing confidence/ranking so the proposal names its measured
population.

A caller-loop census measures whether an otherwise once-per-call Performance
Triage row can be repeated by an upstream caller's loop:

```bash
eng/prepare-performance-triage-corpus.sh /tmp/performance-triage-assemblies.txt
dotnet run --project tools/AnalysisHarness -c Release -- \
  --caller-loop-census /tmp/performance-triage-assemblies.txt --max-depth 4 --top 20
dotnet run --project tools/AnalysisHarness -c Release -- \
  --caller-loop-census /tmp/performance-triage-assemblies.txt --max-depth 4 --json
```

The census remains measurement-only and does not alter rows or ranking. It
shares the product's typed invocation analysis, which projects direct evidence
as `CallerLoop`, `CallerLoopDepth`, and `CallerLoopWitness` while leaving local
`Loop`, multiplicity, confidence, weight, and candidate identity unchanged.
The census extends the same graph beyond one hop to classify the broader
population.

The graph includes resolved `call`, `callvirt`, and `newobj` edges; function
loads and unresolved `calli` signatures are excluded. It records the nearest
deterministic path from a loop invocation to each triage method. Results
separate direct, transitive, beyond-bound, and no-witness rows and report both
row and distinct-method denominators, provenance, and
caller-loop/shape/confidence/local-multiplicity cross-tabs. JSON includes every
row and its exact witness path for classification.

The original Aspire Dashboard acceptance target is separately pinned because it
ships in the platform-specific Dashboard SDK package rather than
`Aspire.Hosting`:

```bash
eng/prepare-aspire-dashboard-corpus.sh /tmp/aspire-dashboard-assemblies.txt
dotnet run --project tools/AnalysisHarness -c Release -- \
  --caller-loop-census /tmp/aspire-dashboard-assemblies.txt --max-depth 4 --json
```

That run is also an invocation-edge near miss: the current Caller Graph path to
`ColorGenerator.GetColorIndex` crosses an in-loop `ldftn` that creates a
`RenderFragment`; it is not a call. The invocation-only census therefore
correctly reports no witness. Proving that callback is repeatedly consumed
requires a separate deferred-callback discriminator.

A deferred-callback census measures that separate construction boundary:

```bash
dotnet run --project tools/AnalysisHarness -c Release -- \
  --deferred-callback-census /tmp/performance-triage-assemblies.txt \
  --max-depth 4 --top 20
dotnet run --project tools/AnalysisHarness -c Release -- \
  --deferred-callback-census /tmp/aspire-dashboard-assemblies.txt \
  --max-depth 4 --json
```

The census joins an in-loop function load to an adjacent delegate constructor
and immediate consumer, then reuses the typed invocation graph downstream from
the callback target. It distinguishes cached or unconsumed constructions,
unknown consumers, trusted `RenderTreeBuilder.AddAttribute` registration, and
an immediately constructed parameterless delegate invocation. Only the last
classification sets `ConsumptionProven=true`. Framework registration proves
which callback object was passed to which API, but not whether, when, or how
often the framework invokes it. This measurement does not alter Performance
Triage candidates, local `Loop`, multiplicity, confidence, weight, or ranking.
When more than one callback path reaches a row, the report keeps the strongest
available class (`Invoke`, registration, then unknown) and the nearest
deterministic witness within that class.

The 2026-07-14 pinned run found no statically proven callback consumption. The
six-library corpus had 2,176 function loads and 2,459 opportunities; 10
opportunity rows were reachable through 39 unknown consumers, and none had
proven consumption. Aspire Dashboard had 1,019 function loads and 488
opportunities; 12 sites were trusted framework registrations, eight
opportunity rows were reachable, and none had proven consumption.
`ColorGenerator.GetColorIndex` is reached at downstream depth 2 through the
expected `RenderFragment` registration, with `ConsumptionProven=false`.
Consequently this census records an explicit non-action: registration evidence
is useful diagnostic provenance, but is not strong enough for a product-side
caller-loop projection or ranking change without runtime evidence or a stronger
framework invocation contract.

A recursive-traversal census measures a different repetition boundary: an exact
resolved self-call that occurs structurally inside the caller's loop.

```bash
dotnet run --project tools/AnalysisHarness -c Release -- \
  --recursive-traversal-census /tmp/performance-triage-assemblies.txt \
  --max-depth 4 --top 20
dotnet run --project tools/AnalysisHarness -c Release -- \
  --recursive-traversal-census /tmp/aspire-dashboard-assemblies.txt \
  --max-depth 4 --json
```

The in-loop self-call identifies branching traversal potential. It does not
prove runtime heat, recursion depth, collection size, or that either the loop or
recursive branch executes. The discriminator rejects ordinary recursion,
mutual recursion, `callvirt` self-dispatch, and function loads. From each
qualifying root, the census reuses the typed invocation graph to report exact
downstream Performance Triage rows as traversal-root, direct, transitive,
beyond-bound, or none. Candidate identity, local `Loop`, multiplicity,
confidence, weight, and product ranking remain unchanged.

The pinned six-library run opened all six assemblies and found 29 traversal
roots among 2,459 opportunity rows. Seventy-five rows were reachable: 11 on a
traversal root, nine direct, 20 transitive within depth four, and 35 beyond the
bound. The separately pinned Aspire Dashboard run was much narrower: one root
and one reachable row among 488 opportunities. The root was
`TraceDetail.AddSelfAndChildren`; its direct call to
`OtlpSpan.GetChildSpans` remained candidate `pt~6756cfa4ed4130d8`, local
multiplicity `once`, and `LocalInLoop=false`. The witness records the root's
in-loop self-call followed by its direct, non-loop call to `GetChildSpans`.

Recent registry improvements are orthogonal controls. The allocation-fanout
view still distinguishes 53 once paths in `LibrarySections.CreatePipeline`
from one in `LibrarySourcePlans..cctor`, and 34 in `SpikeSections..cctor` from
zero in `PlanFor`, `CompilePlan`, and `CapabilityPlan.ExecuteAsync`. Those
registry methods have no recursive-traversal evidence. This is therefore a
selective static receipt, not a product ranking change: use runtime allocation
evidence to decide whether a reachable row is hot before changing production
code.

A 2026-07-01 fixed-corpus run (`14/14` assemblies opened) showed 41,890
allocation occurrences and 6,587 optimization opportunities. `return-post-dominates`
was common (29,175 occurrences; 4,817 opportunities), but not selective by
itself: it appears heavily in `small-array`, `box-value-type`, and delegate
rows. `local-only` escape evidence was sparse (133 occurrences, ~0.3%), so
`LocalOnly + post-dominance` is not yet broad enough to justify global confidence
or ranking changes. Treat this as a query/example signal first; behavior changes
should remain shape-specific and cite the measured bucket they target.

Layer 3 (sampled precision/recall) splits by oracle: recall is mechanical once curated, precision
needs judgement.

```bash
dotnet "$DLL" --paydirt-recall ILInspector.Decompiler.dll       # committed reference sites must surface
dotnet "$DLL" --precision-sample ILInspector.Decompiler.dll --top 20   # worksheet for TP/FP labeling
```

`--paydirt-recall` exits nonzero if a committed reference paydirt site stops surfacing as a
loop+high triage candidate (a recall regression). `--precision-sample` emits the top-N ranked
candidates as a labeling worksheet — there is no automatic precision oracle, so an agent/human
labels true vs false positive.

Deep Inspect's census lane runs the generated-fixture catalogue, corpus stability sensor,
and paydirt recall gate. Precision labeling, baseline refreshes, and recall-reference
edits remain maintainer-owned upkeep: they are documented conventions, not automatic CI gates.

## Leak triage corpus sensor (#1992)

`--leak-triage` sweeps the fail-closed ArrayPool leak-triage analyzer
(`LeakTriageAnalyzer`) over a corpus and reports where it fires, plus
measurement-only candidate/suppression buckets, as a
[Markout](https://github.com/richlander/markout) card:

```bash
dotnet "$DLL" --leak-triage assemblies.txt --top 5          # Markdown card (default)
dotnet "$DLL" --leak-triage assemblies.txt --tsv            # section-tagged TSV
dotnet "$DLL" --leak-triage assemblies.txt --jsonl          # one heterogeneous JSON record per row
```

The card has five sections — a **Summary** (assemblies / opened / failed / timed out / total
findings / total candidates), a **By shape** histogram (`arraypool-rent-not-returned`,
`arraypool-use-after-return`, `arraypool-double-return`), **Findings** (assembly / shape / method,
`--top` bounding examples per assembly), **Candidate buckets**, and **Candidates**. In structured
output, **Candidates** also carries the analyzer evidence plus the rent and use offsets (`rent_offset`
/ `il_offset` in JSONL) so precision samples can jump directly to the relevant IL. Candidate buckets
are not product findings; they measure recall gates such as
`normal-path-leak-candidate`, `exception-path-leak-candidate`,
`use-after-return-candidate`, `ownership-transfer-suppressed`,
`alias-or-field-suppressed`, `cross-method-suppressed`, and
`incomplete-cfg-or-rd-suppressed`. Candidate rows can overlap: for example, a cross-method
suppression can also carry an exception-path candidate when the normal path may still release or
transfer ownership but an unprotected call boundary can throw first. The exception-path candidate
bucket suppresses known nonthrowing setup calls such as `GC.KeepAlive`, `Array.Copy`,
`Array.Clear`, `MemoryExtensions.AsSpan`, `Span<T>.Clear`, and array-to-`Span<T>` conversion that
feeds an immediate `Span<T>.CopyTo`; these remain cross-method suppressions rather than product
findings. One declarative Markout model renders the dense Markdown table and decomposes into
section-tagged TSV/JSONL. It is a single-run census with no baseline, so it uses plain sectioned
rows, not composite/delta cells; a `--diff-baseline` mode against a committed snapshot is the
natural home for those (`Change`/`[MarkoutDelta]`). Each assembly is bounded by a per-assembly
timeout, and any per-assembly input failure (a directory path, a truncated PE) becomes an
`Opened=false` row rather than crashing the sweep.

This is the evidence engine that must earn any user-facing `Leak Triage` section: the analyzer
fails closed on incomplete CFG/RD, non-`Shared` pools, aliases, field stores, cross-method
ownership, and ambiguous uses, so an **empty findings card on real code means recall — not a
product section — is the next lever**. Use the candidate buckets to decide which recall gate to
model next. A 2026-07-05 run over CoreLib, `Microsoft.CodeAnalysis`, and
`Microsoft.CodeAnalysis.CSharp` produced **0 findings** (all gates suppressed), while the fixture
assembly's three known-misuse methods surfaced exactly once each. Wire the section only once this
card shows non-zero, high-precision rows on real assemblies.

## Leak actionability corpus sensor (#2439)

`--leak-actionability` classifies the leak-triage `exception-path-leak-candidate` bucket by
**actionability**, as a [Markout](https://github.com/richlander/markout) card:

```bash
dotnet "$DLL" --leak-actionability assemblies.txt --top 5      # Markdown card (default)
dotnet "$DLL" --leak-actionability assemblies.txt --tsv        # section-tagged TSV
dotnet "$DLL" --leak-actionability assemblies.txt --jsonl      # one JSON record per row
```

Like `--leak-triage`, this is **measurement-only** — it changes no analyzer behavior and wires no
product surface. For each `exception-path-leak-candidate`, it re-resolves via SRM every call the
rented array flows into between `Rent` and the method's end, then classifies the boundary set by
what it touches:

- `untrusted-actionable` — a boundary **reads/decodes/parses external input**
  (`Stream.Read`, `Decoder.GetChars`, `Encoding.GetString`, `Parse`/`Tokenize`/`Deserialize`):
  genuinely actionable, since the exception is one a caller commonly catches.
- `trusted-low-actionability` — every boundary is an **in-memory transform of validated data**
  (`Escape`, `Encode`/`Transcode`, `Array.Copy`, format/write, `new string`): low actionability —
  the deliberate high-perf no-`finally` BCL idiom that leaks only on a rare/invariant-violating
  exception.
- `unknown` — unclassified boundary; stays measurement-only.

A candidate is `untrusted-actionable` if **any** boundary is untrusted, else
`trusted-low-actionability` if any is a known in-memory transform, else `unknown`. This is the
evidence engine for the #2439 Slice-4 decision about which exception-path candidates could graduate
toward findings; keeping the split out of the analyzer means it never affects a user-facing
accusation. A 2026-07-07 run over the .NET 9.0.14 shared framework (305 assemblies) classified the
34 exception-path candidates as 6 `untrusted-actionable` (e.g. `MessagePackReader::ReadStringSlow`,
`HashAlgorithm::ComputeHash`), 19 `trusted-low-actionability` (e.g. `Utf8JsonWriter` escape
writers, `BinaryWriter::Write`), and 9 `unknown`.

Boundary attribution is a `Rent`-to-end window scan, coarser than the analyzer's def-use set (it
can include an unrelated call in the window); it is exact for the small single-rent methods this
bucket is dominated by. A def-use-precise attribution is the natural follow-up.

## MemoryPool lifecycle corpus sensor (#2439, Slice 3)

`--memorypool-lifecycle` is the second resource family alongside the ArrayPool leak-triage work: a
**measurement-only** census of `MemoryPool<T>.Rent` sites, as a
[Markout](https://github.com/richlander/markout) card.

```bash
dotnet "$DLL" --memorypool-lifecycle assemblies.txt --top 5   # Markdown card (default)
dotnet "$DLL" --memorypool-lifecycle assemblies.txt --tsv     # section-tagged TSV
dotnet "$DLL" --memorypool-lifecycle assemblies.txt --jsonl   # one JSON record per row
```

For every `MemoryPool<T>.Rent` call it tracks the returned `IMemoryOwner<T>` through the shared
reaching-definitions def/use web and buckets the site by how the owner is released:

- `disposed-in-scope` — the owner is `Dispose`d in a `finally`/fault handler covering the
  acquisition (the `using` idiom): exception-safe.
- `exception-path-leak-candidate` — the owner is disposed, but only on the normal path (no covering
  handler): an exception between `Rent` and `Dispose` leaks it.
- `normal-path-leak-candidate` — the owner is never disposed and never escapes (e.g. rented then
  popped, or rented and never used): it leaks on the normal path.
- `ownership-transfer-suppressed` — the owner is returned, stored to a field, or passed to another
  method/constructor: its lifetime is owned elsewhere, so no accusation.
- `incomplete-or-ambiguous-suppressed` — incomplete CFG/RD, an address-taken or multiply-defined
  owner slot, or an unmodeled disposition: fail-closed.

Like `--leak-triage` this changes no analyzer behavior and wires no product surface, and it is
**precision-first**: anything not provably disposed or leaked is suppressed. A run over the .NET
9.0.14 shared framework (`Microsoft.NETCore.App` + `Microsoft.AspNetCore.App`, 308 assemblies)
found 19 `MemoryPool<T>.Rent` sites — **all 19 `ownership-transfer-suppressed`** (the owner is
handed to a `BufferSegment`/`DiagnosticPoolBlock`/field that owns disposal), with **zero** false
leak accusations. Synthetic positive-control fixtures confirm every bucket fires: `using` →
`disposed-in-scope`, normal-path `Dispose` (no `finally`) → `exception-path-leak-candidate`,
rent-then-discard → `normal-path-leak-candidate`, and return/field/pass →
`ownership-transfer-suppressed`.

Attribution is immediate-consumer based; a fully stack-precise attribution is the natural
follow-up. Known limitation: when the owner is kept purely on the evaluation stack (the Release
idiom `Rent(); dup; ...; Dispose()` with no local) there is no slot for the def/use web to track,
so the site is reported `incomplete-or-ambiguous-suppressed` rather than guessed. The
exception-safe `using` shape always uses a local (the finally must reload the owner), so
`disposed-in-scope` is detected regardless of optimization level.

## Allocation convergence parity

The Rung 4 allocation-convergence build must prove that a candidate
occurrence-derived projection produces the same decompiler allocation annotations
as the legacy decompiler allocation annotation producer. Use the parity gate with two
`AnnotationStructuredView.Json(...)` outputs: the legacy allocation annotations
and the candidate projection serialized in the same structured shape.

```bash
dotnet "$DLL" --allocation-parity legacy-annotations.json candidate-annotations.json
```

The comparison ignores non-allocation annotations and is exact for allocation
annotation `id`, IL `offset`, `conditionality`, and `detail`. Duplicate rows are
counted, so Track A can use this as a mechanical exit gate for identical
projection rather than manually inspecting mixed-source output.

## Precision labeling convention

`--precision-sample` is an agent/human worksheet, not a gate. The analyzer has no automatic
precision oracle, so labels must be reviewable evidence rather than an implied pass/fail.

Use this convention for precision pilots and recurring reviews:

- **Owner:** one agent prepares labels from the worksheet; a maintainer or second reviewer resolves
  disputed rows before the result is treated as a quality signal.
- **Storage:** post the labeled worksheet as an issue or PR comment on the tracker that requested
  the sample. Do not commit labels unless they are promoted into curated recall references.
- **Cadence:** run on demand for behavior changes, ranking changes, new shape families, or a
  suspected false-positive cluster. Do not add it to PR CI or automatic schedules.
- **Sample size:** start with `--top 20` for a normal review. Use a smaller sample only for a pilot
  that validates the convention itself; state the sample size in the comment.
- **Bar:** a sample is healthy when at least 80% of labeled rows are true positives and no
  high-confidence false-positive cluster dominates the top rows. Below that bar, open or link a
  follow-up issue for each cluster before treating the signal as healthy.
- **Stale labels:** labels are tied to the assembly, command, commit, and tool version. Re-label
  after ranking, confidence, or shape changes; do not compare old labels to new output without
  re-running the worksheet.

Required fields per row:

| Field | Meaning |
| --- | --- |
| `Candidate` | Copy the worksheet row identity: type, method, signature, shape, confidence, loop, root reach. |
| `Label` | `TP`, `FP`, or `Unknown`. `Unknown` is allowed when the row needs runtime/profile evidence. |
| `Reason` | One sentence explaining why the row is real paydirt, a false positive, or undecidable. |
| `Evidence` | The command/output inspected, usually `dotnet-inspect member ... -S "Annotated Source,IL"` or equivalent. |
| `Action` | `none`, `open issue`, `promote to recall reference`, or `needs maintainer decision`. |

Comment template:

```text
### Analysis precision sample — <assembly>

Command: dotnet run --project tools/AnalysisHarness -c Release -- --precision-sample <assembly> --top <N>
Commit/tool: <git sha>
Summary: <TP>/<labeled> TP, <FP>/<labeled> FP, <Unknown> unknown. Healthy: yes/no.

| Candidate | Label | Reason | Evidence | Action |
| --- | --- | --- | --- | --- |
| <type>::<method>(<signature>) [shape, confidence, loop, root reach] | TP/FP/Unknown | <why> | <command/output> | <next> |
```

## Corpus baseline and recall-reference upkeep

The committed corpus files are review artifacts:

| File | Purpose |
| --- | --- |
| `corpus/analysis-baseline.json` | The last accepted corpus stability snapshot for the pinned corpus. |
| `corpus/paydirt-reference.json` | Curated recall anchors that must keep surfacing as loop+high candidates. |

Refresh `analysis-baseline.json` only when the corpus changed, the analyzer intentionally changed
signal counts, or a previous timeout/failure is now measured. Do not rebaseline a regression away:
first explain why each change is drift, an improvement, or an environment/corpus update.

Use this command shape for baseline updates:

```bash
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
dotnet run --project tools/AnalysisHarness -c Release -- \
  --corpus-list /tmp/corpus-assemblies.txt \
  --diff-corpus-baseline tools/AnalysisHarness/corpus/analysis-baseline.json \
  --emit-corpus-snapshot /tmp/analysis-baseline.json
```

Then copy `/tmp/analysis-baseline.json` to `tools/AnalysisHarness/corpus/analysis-baseline.json`
only after reviewing the diff card. The PR or issue comment must include the command, the diff-card
summary, and the reason each movement is acceptable.

Edit `paydirt-reference.json` only after a labeled precision worksheet or targeted investigation
identifies a stable, high-value recall anchor. A reference should have:

- qualified assembly, type, method, signature, and shape identity;
- stable loop+high ranking on the current analyzer;
- evidence that it is real paydirt, not just a convenient row; and
- enough reach or representative value to make it worth guarding.

Remove or update a reference only when the underlying code moved, the shape was intentionally
renamed, or the candidate stopped being real paydirt. Missing references are recall regressions
until proven stale. Validate reference edits with:

```bash
dotnet run --project tools/AnalysisHarness -c Release -- \
  --paydirt-recall artifacts/bin/ILInspector.Decompiler/release/ILInspector.Decompiler.dll \
  --reference tools/AnalysisHarness/corpus/paydirt-reference.json
```

Deep Inspect failure triage:

| Symptom | First classification | Expected action |
| --- | --- | --- |
| Assembly stops opening or times out | Analyzer or environment regression | Reproduce locally; fix analyzer/runtime issue before rebaselining. |
| Analyzer diagnostics increase | Analyzer regression | Find the failing method family and fix or file a focused issue. |
| Signal counts move with no regressions | Drift | Review the card; rebaseline only with an explanation. |
| Paydirt recall misses a site | Recall regression or stale reference | Re-run precision evidence; fix the analyzer or update the reference with rationale. |
| Corpus assembly added/removed | Corpus update | Refresh the baseline after confirming the pinned corpus change is intentional. |
