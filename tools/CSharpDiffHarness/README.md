# C# Diff Harness

Developer harness for measuring `CSharpBodyDiff` over paired assemblies. It is
not a shipped `dotnet-inspect` command; it emits a compact Markout-rendered card
for fixture, corpus, RTS, or Research follow-up work.

```bash
dotnet run --project tools/CSharpDiffHarness -c Release -- \
  old.dll new.dll --max-examples 5

dotnet run --project tools/CSharpDiffHarness -c Release -- \
  --pair old1.dll new1.dll --pair old2.dll new2.dll --max-examples 5

dotnet run --project tools/CSharpDiffHarness -c Release -- \
  --pairs pairs.tsv --max-examples 5

dotnet run --project tools/CSharpDiffHarness -c Release -- \
  --pairs pairs.tsv --format jsonl --max-examples 5

dotnet run --project tools/CSharpDiffHarness -c Release -- \
  --pairs pairs.tsv --emit-snapshot baseline.json

dotnet run --project tools/CSharpDiffHarness -c Release -- \
  --pairs pairs.tsv --diff-baseline baseline.json
```

Pair manifests use one old/new assembly pair per line, separated by a tab.
Empty lines and lines beginning with `#` are ignored. Relative paths are
resolved from the manifest directory.

The card includes:

- pair count;
- exact and changed pair counts;
- row and failure counts;
- failure buckets;
- top change IDs and operation kinds;
- per-pair summary rows;
- optional baseline metric and bucket changes plus comparison rows;
- capped examples rendered through `CSharpDiffPrinter`.

Output defaults to Markout Markdown. Use `--format tsv` or `--format jsonl` to
render the same card sections through Markout table formats. JSONL output omits
Markdown section separators so each non-empty line is a JSON object.

Use `--emit-snapshot <file>` to write stable JSON card data for a run. Use
`--diff-baseline <file>` to compare the current run against a previous snapshot.
Baseline comparisons return exit code `1` for regressions (more failures, more
changed pairs, more rows, fewer exact pairs, or new failure buckets) and report
other metric and bucket changes as non-failing drift. Baseline output uses
Markout metric-change rows for scalar metrics (`Metric | Change | Target` in
Markdown, with goal markers and inline status where a target applies) and
goal-aware segment-change rows for failure buckets. Change IDs and operation
kinds remain context bucket rows. JSONL preserves typed baseline/current fields,
direction/status, and segment values.
