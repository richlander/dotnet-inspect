# EVIL authored-corpus run history

This directory stores the compact trend history for the EVIL authored-source
correspondence benchmark tracked by #3079.

`history.jsonl` is newline-delimited JSON, newest-last. Each line is one full
`--benchmark-authored-corpus --json` run summarized to stable header metrics.
The multi-megabyte per-row JSON stays out-of-tree as a session artifact, issue
attachment, or CI artifact. Do not commit full per-row run payloads here.

## Schema

Each row contains these fields:

- `date`: UTC run date, formatted as `yyyy-mm-dd`.
- `commit`: short source commit SHA for the harness under test, or `null` when
  the original run did not record it.
- `poolMatched` and `poolTotal`: corpus assembly coverage for the supplied
  assembly pool.
- `evaluated`: target methods evaluated by the benchmark.
- `validPct`: one-decimal valid percentage reported for the run.
- `correct`: valid rows that match authored source.
- `validDifferent`: the valid-different partition. The sub-buckets must sum to
  `total`; the benchmark fails the run if they do not.
  - `total`: all valid rows that differ from authored source.
  - `lowering`: authored sugar the compiler erases (inherent, unrecoverable).
  - `knownTaste`: a documented product decision, already accounted for.
  - `frontierIlExact`: cosmetic frontier rows with IL-exact output.
  - `frontierIlDiff`: semantic frontier rows with IL-different output.
  - `frontierIlNoVerdict`: rows the compile-back oracle returned **no verdict**
    for. This is instrument failure, not a classification — those rows are
    *unmeasured*, not "neither exact nor diff". It is recorded even when zero so
    that a shortfall can never be mistaken for data.

  `lowering`, `knownTaste`, and `frontierIlNoVerdict` are absent only on rows
  recorded before the store carried them, where the values are not recoverable
  from any retained artifact. Absent means **not recorded**, never zero.
- `invalid`: rows that did not round-trip.
- `invalidBreakdown`: `null` for runs before #3096; otherwise the
  FaultIsolation-backed split:
  - `productBodyDefect`: invalid rows isolated to the target body.
  - `harnessShellReconstruction`: invalid rows isolated to the reconstructed
    shell or closure. These rows are **unmeasured** for product status, not
    product-clean — a broken shell masks whatever the body did.
  - `unclassified`: invalid rows without a product-vs-harness classification.
- `notFull`: rows uncheckable at `Full` fidelity (a surfaced decompiler
  limitation, not a corpus problem).
- `unsupported`: unsupported ReturnToSender targets.
- `drift`: rows where corpus source could not be resolved.
- `unknownOutcome`: rows whose probe outcome the classifier does not recognize.
  Unreachable while every outcome is classified; it exists so that an
  unclassified outcome surfaces instead of inflating a real bucket.
- `inputsComplete`: `true` only when the run had no unmatched rows and evaluated
  at least one target. This reports **only that the inputs were all present**.
  It makes no claim that every evaluated row was measured — see
  `frontierIlNoVerdict` and `harnessShellReconstruction`, both of which are
  unmeasured rather than clean. (Rows before #3244 spell this field `honest`,
  which overclaimed exactly that distinction.)
- `sweepManifestSha256`: SHA-256 of the pool sweep manifest, or `null` when the
  manifest is unknown.
- `methodologyVersion`: how `invalidBreakdown.productBodyDefect` was computed.
  **Every version is a lower bound on decompiler-caused body defects**; a later
  version tightens the bound rather than measuring the true count. Copy this
  field verbatim from the run JSON's top-level `methodologyVersion`.

  Absent (or `null`) means **v1**: substitution control only — a body defect is
  credited only when the checksum-verified authored body compiles in the failing
  row's shell, so a broken shell masks the defect entirely.

  **v2** keeps the v1 substitution control and adds span attribution, which
  recovers some of the rows a broken shell used to mask. Under a broken shell it
  credits a body defect only when *both* hold:

  1. the authored body is error-free within its own body span (the control), and
  2. the decompiled body carries an in-body error that a broken shell
     **provably cannot manufacture**.

  Condition 2 is the entire soundness argument and is enforced by default-deny.
  Only two error classes qualify:

  - any **parser diagnostic** inside the body span (from
    `SyntaxTree.GetDiagnostics()`, which no shell state can influence), and
  - a **body-intrinsic semantic error** drawn from an explicit allowlist,
    currently exactly `CS0128` (duplicate local declaration).

  Context-dependent errors — unresolved names, types, or members (`CS0103`,
  `CS0246`, `CS0234`, `CS1061`, `CS1069`), conversions, and overload resolution
  — are **never** credited, because a broken shell reconstructor produces them
  identically to a real body defect. Every other case declines, which is a
  deliberate false negative that preserves the lower bound.

  Because the allowlist defines what `v2` means, expanding it requires a new
  `methodologyVersion`; do not add IDs under the existing stamp. This is
  enforced mechanically, not by convention:
  `SpanAttributionTests.BodyIntrinsicAllowlist_IsPinnedToCurrentMethodologyVersion`
  asserts set equality between the live allowlist and the set pinned for the
  current version, so any addition fails the gate until the version is bumped
  and a new pin recorded. A companion test pins the excluded *categories*
  (resolution, conversion, overload, scope collision, definite assignment) that
  the prose above forbids. v1 and v2 counts are not directly comparable, and the
  progress card never diffs `productBodyDefect` across the boundary.

## Append procedure

1. Build the harness at the commit under test. **This must be a commit on
   `main`.** The store is a trend series, so every row has to be reproducible
   from the recorded commit; a feature-branch commit is not, and it carries
   whatever product state the branch happened to be based on. Measuring a
   methodology or product change against its own base is a valid experiment —
   it just belongs in the PR that makes the change, not in this series.
2. Prepare or reuse the EVIL pool:

   ```bash
   bash eng/prepare-evil-corpus.sh /tmp/evil-pool
   ```

3. Run the full corpus with JSON output:

   ```bash
   dotnet run --project tools/DecompilerHarness -c Release --no-build -- \
     --benchmark-authored-corpus external/authored-source-corpus/evil/corpus.jsonl \
     --json $(cat /tmp/evil-pool/assemblies.txt) > evil-run-YYYYMMDD-SHA.json
   ```

   Exit code 1 is expected while `invalid`, `drift`, or `unsupported` is
   non-zero; the JSON is still authoritative. That contract is unchanged, and
   deliberately so: it applies whenever `--ratchet-baseline` is absent, which is
   the case for this append run. To judge a run by movement instead of by
   perfection, see [The regression ratchet](#the-regression-ratchet).

4. Archive the full JSON and `/tmp/evil-pool/sweep-manifest.json` out-of-tree.
5. Record the UTC date, short SHA, and sweep-manifest SHA-256.
6. Copy the run JSON's top-level `methodologyVersion` into the row.
7. Append one compact JSON object to `history.jsonl`, copying **every** bucket —
   the full `validDifferent` partition (including zeros), `notFull`, `drift`,
   `unsupported`, and `unknownOutcome`. A row that omits a bucket shrinks the
   partition silently, which is the defect #3244 fixed.
8. Validate every line parses before committing.

### The partition is enforced, not assumed

`AuthoredCorpusHistoryCardTests` reads this tracked store and fails when:

- a row's `validDifferent` sub-buckets do not sum to its `total`, or its
  top-level buckets do not sum to `evaluated`
  (`TrackedHistory_CompleteRows_PartitionExactly`); or
- any row other than the grandfathered ones omits a bucket
  (`TrackedHistory_OnlyGrandfatheredRowsOmitThePartition`, asserted as set
  equality so the grandfather list cannot go stale).

The benchmark applies the same check to its own output before exiting, and
fails with a `BLOCKER:` line rather than emitting a payload that looks complete.

Exactly one row is grandfathered: **2026-07-20**, recorded before the store
carried the full partition and with no retained artifact to recover it from. Its
sub-buckets are absent rather than zero. Every other historical row was
backfilled from its own archived run JSON, verified by matching all nine shared
fields against the recorded row.

## The regression ratchet

`--ratchet-baseline <history.jsonl>` judges a run by **movement against this
store** instead of by perfection:

```bash
dotnet run --project tools/DecompilerHarness -c Release --no-build -- \
  --benchmark-authored-corpus external/authored-source-corpus/evil/corpus.jsonl \
  --ratchet-baseline tools/DecompilerHarness/corpus/evil-runs/history.jsonl \
  --ratchet-pool-manifest /tmp/evil-pool/sweep-manifest.json \
  --json $(cat /tmp/evil-pool/assemblies.txt)
```

It exists because the benchmark's exit code was a constant. Success required
`invalid == 0`, which on a 12,000-row adversarial corpus sitting near 5,200
invalid is unreachable, so the exit code read identically at 56.7% valid and at
40% valid and detected no regression at all (#3245).

The fix separates two questions the old contract conflated:

- **Measurement integrity** — unmatched rows, an empty run, a non-closing
  partition, drift, unsupported targets, or an unrecognized outcome — still
  fails hard, with or without a baseline. These do not say the decompiler got
  worse; they say the run is not trustworthy, and an untrustworthy number must
  not be compared to anything. A ratchet skip never rescues an integrity
  failure.
- **Quality level** is the thing being measured. Without a baseline it keeps the
  historical `invalid == 0` contract. With one, the run fails on a *regression*
  in `valid`, `correct`, `invalid`, or `invalidBreakdown.productBodyDefect`.

### The band is zero

Every metric ratchets strictly. The four-run spread in this store
(56.6/56.2/56.2/56.5) is not instrument noise to be absorbed: each of those runs
is a *different commit* against an *identical pool manifest*, so the movement is
code-attributable — exactly what the ratchet exists to catch. Re-running one
commit against one pinned pool came back bit-identical on every counted metric.
A tolerance band would therefore be the harness declining to report real
movement, not compensating for a noisy instrument.

The ratcheted metric is the **exact valid count** (`correct` + `validDifferent.total`),
not `validPct`. The store records the percentage to one decimal, so 6,802/12,000
and 6,801/12,000 both read as 56.7 — a genuinely lost valid row would clear a
"zero tolerance" ratchet on the rounded figure. Because `evaluated` is equal by
the comparability key, the exact count says the same thing with none of the
rounding. Rows predating the `validDifferent` partition cannot reconstruct it, so
for them the metric is omitted rather than inferred from the percentage.

### Comparability, and the difference between a skip and a pass

A baseline row is comparable only when it shares the run's `evaluated`,
`poolMatched`, `poolTotal`, and `sweepManifestSha256`. The newest comparable row
wins — not the newest row outright, so a resized corpus or a repooled sweep
cannot silently retarget the ratchet at an incomparable baseline.

`methodologyVersion` is deliberately **not** part of that key. It defines how
`productBodyDefect` is computed and nothing else, so folding it in discarded
three perfectly sound metrics at every version bump — and, because every row
after a bump was then incomparable, it is what made the tracked-store gate a
permanent skip. It is applied per metric instead: across a bump, `valid`,
`correct`, and `invalid` keep ratcheting and only `productBodyDefect` drops out.

The **baseline governs** the pool hash. When the recorded row identifies its
pool, a run that cannot identify its own is not comparable to it. The weaker
"check only when both sides recorded one" rule was unsound: it assumed a drifted
pool always surfaces as unmatched rows or unresolved identities, but a package
resolving to a newer version that still carries the same method identities
resolves cleanly, drifts nothing, and would have been ratcheted against numbers
measured on different code. `--ratchet-pool-manifest <sweep-manifest.json>` is
how a live run identifies its pool; the recorded value is the first 8 bytes of
the manifest's SHA-256, lowercase hex.

> The store's historical hashes were recorded by hand. That derivation is
> asserted here, not verified against them. If one was produced a different way
> it will not match — and the result is a loud skip, never a false pass, which is
> the direction this has to fail in.

Three outcomes, deliberately distinct:

| Situation | Exit | Output |
| --- | --- | --- |
| Baseline missing or unparseable | non-zero | hard error |
| Baseline parses, no comparable row | non-zero | loud `RATCHET SKIPPED` |
| Baseline compared | 0 or non-zero | per-metric `held`/`REGRESSED` |

**A skip fails.** It carries no quality opinion — nothing was compared — but
exiting 0 on it would rebuild the defect this replaces one level up: a gate
reporting success having measured nothing. The weekly caller makes that concrete.
Its pool is resolved from current top-N package versions, so it *will* drift off
the recorded manifest; on a green skip the job would pass forever in silence, and
the silence would look exactly like health. Passing `--ratchet-baseline` is a
demand for a verdict, and "none available" fails that demand. The remedy is a
corpus refresh or a corrected baseline, never a product change. A run with no
baseline to compare against simply does not pass the flag.

For the same reason a typo'd path is a hard error rather than "nothing to
compare", and `--ratchet-baseline` without `--benchmark-authored-corpus` (or
`--ratchet-pool-manifest` without `--ratchet-baseline`) is refused rather than
ignored.

A **malformed corpus row** is an integrity failure, not a logged curiosity, for
the same reason. Dropping one silently shrinks `evaluated`, which makes the run
incomparable, which produces a skip — so before this was enforced, a corpus
quietly losing rows would have *disarmed* the gate instead of tripping it.

`productBodyDefect` is reported with its lower-bound caveat attached: it counts
decompiler-caused body defects the oracle could adjudicate (~5.9% coverage), so
its movement is a floor, not a census.

### Who runs it

- **Every PR**: `AuthoredCorpusRatchetTests` ratchets the newest row of this
  tracked store against the newest earlier row it is comparable with, so a
  hand-appended row that halves the quality fails review. Rows recorded before
  the ratchet existed are data, not a contract, so only the new row is judged.
  The gate asserts that the comparison actually *happened*, not merely that it
  found no regressions — an empty-regressions assertion alone passes for a skip,
  which is how the first version of this gate was vacuous.
- **Weekly**: the `authored-corpus-ratchet` lane in `deep-inspect.yml` restores
  the vendored corpus, prepares the EVIL pool, and runs the benchmark against
  this store. It is a *periodic* gate — the corpus and the 100-package sweep are
  far too expensive for the PR lane — so a product regression surfaces within a
  week of landing rather than at the PR that causes it.

## Progress card

Render a Markout progress card over this trend store — every recorded run as a
trend table plus a pivoted movement table over the most recent runs — with:

```bash
dotnet run --project tools/DecompilerHarness -c Release -- --history-card
```

Options:

- `--history-window <n>`: bound the movement pivot to the last `n` runs
  (default 3; `<= 0` uses every run). The Runs trend table always lists every
  run.
- `--history-path <file>`: read a specific `history.jsonl` instead of the
  committed default.

The card reads no assemblies and runs no decompiler. Its headline metric is
`invalidBreakdown.productBodyDefect` (genuine target-body decompiler defects),
not raw `invalid`: per #3079 the raw invalid population is dominated by harness
shell-reconstruction noise that does not move on decompiler fixes.

The **Movement** table is the transpose (pivot) of **Runs**: each metric is a
row and each recent run a column. Every metric row declares its optimization
goal, so Markout renders the goal glyph (`↑` higher-is-better / `↓`
lower-is-better) on the label and a per-step polarity glyph (`✓`/`✗`) on each
column compared to the previous populated one — no hand-computed delta or trend
word. Runs that predate PR #3096 have no `invalidBreakdown`, so their
product/harness columns render as `—` and the product-defect pivot cell is
absent (`-`) with no fabricated step, keeping the missing signal honest.

The Runs table's `Method` column reports each run's `methodologyVersion`
(`v1`/`v2`). Both versions are lower bounds, but v2's tighter rule counts
strictly more rows, so the counts are not comparable. When the movement window
straddles a version boundary the product-defect metric is split into
`Product defects (v1 substitution lower bound)` and
`Product defects (v2 span-measured lower bound)` rows, each populated only for
its own version's columns. Markout therefore never charts a step across the
boundary; a window of uniform version keeps the single `Product defects` row.
