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
- `validDifferent`: compact valid-different counts:
  - `total`: all valid rows that differ from authored source.
  - `frontierIlExact`: cosmetic frontier rows with IL-exact output.
  - `frontierIlDiff`: semantic frontier rows with IL-different output.
- `invalid`: rows that did not round-trip.
- `invalidBreakdown`: `null` for runs before #3096; otherwise the
  FaultIsolation-backed split:
  - `productBodyDefect`: invalid rows isolated to the target body.
  - `harnessShellReconstruction`: invalid rows isolated to the reconstructed
    shell or closure.
  - `unclassified`: invalid rows without a product-vs-harness classification.
- `unsupported`: unsupported ReturnToSender targets.
- `drift`: rows where corpus source could not be resolved.
- `honest`: `true` only when the run had no unmatched rows and evaluated at
  least one target.
- `sweepManifestSha256`: SHA-256 of the pool sweep manifest, or `null` when the
  manifest is unknown.
- `methodologyVersion`: how `invalidBreakdown.productBodyDefect` was computed.
  Absent (or `null`) means **v1**: substitution control only — a body defect is
  credited only when the checksum-verified authored body compiles in the failing
  row's shell, so a broken shell masks the defect and the count is a lower bound.
  **v2** adds span attribution: when the shell is broken it additionally credits
  a body defect if the authored body is error-free within its own body span while
  the decompiled body carries an in-body error. v1 and v2 counts are not directly
  comparable; the progress card never diffs `productBodyDefect` across the
  boundary. Copy this field verbatim from the run JSON's top-level
  `methodologyVersion`.

## Append procedure

1. Build the harness at the commit under test.
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
   non-zero; the JSON is still authoritative.

4. Archive the full JSON and `/tmp/evil-pool/sweep-manifest.json` out-of-tree.
5. Record the UTC date, short SHA, and sweep-manifest SHA-256.
6. Copy the run JSON's top-level `methodologyVersion` into the row.
7. Append one compact JSON object to `history.jsonl`.
8. Validate every line parses before committing.

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
(`v1`/`v2`). Because v1 and v2 `productBodyDefect` counts are not comparable,
when the movement window straddles a version boundary the product-defect metric
is split into `Product defects (v1 lower bound)` and
`Product defects (v2 span-measured)` rows, each populated only for its own
version's columns. Markout therefore never charts a step across the boundary; a
window of uniform version keeps the single `Product defects` row.
