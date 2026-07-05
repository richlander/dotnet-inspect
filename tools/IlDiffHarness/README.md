# IL Diff Harness

Developer harness for measuring `IlBodyDiff` over paired assemblies. It is not a
shipped `dotnet-inspect` command; it emits a compact Markout-rendered Markdown
card for fixture, corpus, RTS, or Research follow-up work.

```bash
dotnet run --project tools/IlDiffHarness -c Release -- \
  old.dll new.dll --max-examples 5

dotnet run --project tools/IlDiffHarness -c Release -- \
  --pair old1.dll new1.dll --pair old2.dll new2.dll --max-examples 5

dotnet run --project tools/IlDiffHarness -c Release -- \
  --pairs pairs.tsv --max-examples 5

dotnet run --project tools/IlDiffHarness -c Release -- \
  --pairs pairs.tsv --format jsonl --max-examples 5
```

Pair manifests use one old/new assembly pair per line, separated by a tab.
Empty lines and lines beginning with `#` are ignored. Relative paths are
resolved from the manifest directory.

The card includes:

- compared body count;
- self-diff empty count;
- pair exact-empty and changed-body counts;
- failure count and failure buckets;
- top hunk kinds and opcode families;
- per-pair summary rows;
- capped examples rendered through `IlDiffPrinter`.

Reported failure buckets are card data, not process failures. The harness exits
nonzero only for command-line, IO, or metadata-read errors.

Output defaults to Markout Markdown. Use `--format tsv` or `--format jsonl` to
render the same card sections through Markout table formats. JSONL output
omits Markdown section separators so each non-empty line is a JSON object.
