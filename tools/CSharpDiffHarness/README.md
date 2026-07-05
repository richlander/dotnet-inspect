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
- capped examples rendered through `CSharpDiffPrinter`.

Output defaults to Markout Markdown. Use `--format tsv` or `--format jsonl` to
render the same card sections through Markout table formats. JSONL output omits
Markdown section separators so each non-empty line is a JSON object.
