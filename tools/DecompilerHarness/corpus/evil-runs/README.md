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
6. Append one compact JSON object to `history.jsonl`.
7. Validate every line parses before committing.
