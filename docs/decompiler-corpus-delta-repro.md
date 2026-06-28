# Reproducing decompiler corpus deltas

Use this when a decompiler quality-diff card reports changed rows and the PR body
shows only a capped Markdown sample. The card is intentionally reviewer-sized;
this guide explains how to reproduce the full delta locally at the matching
commit.

## Pick the matching commit

The corpus card is commit-specific. Reproducing it from a later PR head can
legitimately change the row set.

Use the first available source of truth:

1. **Card revision**, if the PR body or comment prints one.
2. **CI run commit**, if you are reproducing the `decompiler-pr-corpus` artifact
   from a check run.
3. **Current PR head**, only when you intentionally want to inspect the latest
   branch state rather than the exact posted card.

For the current PR head:

```bash
gh pr checkout <PR>
git rev-parse HEAD
```

For an exact card revision that is still reachable from the PR branch:

```bash
gh pr checkout <PR>
git fetch origin pull/<PR>/head
git checkout <card-sha>
```

If the exact SHA is no longer reachable because the branch was force-pushed,
download the CI artifact from the run that produced the card, or ask the author
to regenerate the card from the current PR head.

## Reproduce the PR quick quality card

The PR quick card must use the PR quick corpus script with the PR quick baseline.
Do not mix it with the daily/manual corpus script or baseline.

```bash
dotnet build src/dotnet-inspect -c Release -p:PublishAot=false
bash eng/prepare-decompiler-pr-corpus.sh /tmp/pr-corpus-assemblies.txt
mapfile -t assemblies < /tmp/pr-corpus-assemblies.txt
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --diff-corpus-baseline tools/DecompilerHarness/corpus/pr-quick-baseline.json \
  --quality-diff-card \
  --corpus-method-cap 100 \
  --compile-cap 0 \
  --corpus-fidelity-cap 0 \
  --max-examples 24
```

Use `--max-examples 24` when you want local output to match the review card's
"show about two dozen rows" convention. Use a smaller value for a terse smoke
check.

## Reproduce the full method delta

Add `--emit-corpus-delta` to write the full machine-readable per-method delta.
This is useful when the collapsed Markdown section is capped.

```bash
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --diff-corpus-baseline tools/DecompilerHarness/corpus/pr-quick-baseline.json \
  --quality-diff-card \
  --emit-corpus-delta /tmp/corpus-delta.json \
  --corpus-method-cap 100 \
  --compile-cap 0 \
  --corpus-fidelity-cap 0 \
  --max-examples 24
```

The JSON delta records method-level changes such as `fullyRaised`, `residual`,
`validity`, `fidelityCheck`, and `passBug`. Aggregate-only metrics that do not
yet have per-method snapshot rows require their own evidence layer before a full
row-level delta can be reproduced.

## Reproduce the daily/manual real-world card

Use this broader card when reviewing risky decompiler behavior or validating a
manual corpus claim. It uses the daily/manual corpus script and baseline.

```bash
dotnet build src/dotnet-inspect -c Release -p:PublishAot=false
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
mapfile -t assemblies < /tmp/corpus-assemblies.txt
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --diff-corpus-baseline tools/DecompilerHarness/corpus/real-world-baseline.json \
  --quality-diff-card \
  --compile-cap 25 \
  --corpus-fidelity-cap 3 \
  --max-examples 24
```

Only rebaseline after reviewed corpus movement:

```bash
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --emit-corpus-baseline tools/DecompilerHarness/corpus/real-world-baseline.json \
  --compile-cap 25 \
  --corpus-fidelity-cap 3 \
  --max-examples 24
```
