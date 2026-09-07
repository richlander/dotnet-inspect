# Classifying test cost

[AGENTS.md](../AGENTS.md#building-and-testing) states the binding rule: tag a
test `[Trait("Speed", "Slow")]` when its cost comes from exhaustive or
whole-assembly analysis rather than ordinary unit-test setup. This doc owns
the threshold, placement convention, and existing consumers.

## Why this exists

PR CI's fast leg is a shared, PR-blocking resource. A test that repeatedly
opens or analyzes a real large assembly (the product's own assemblies are a
common and legitimate fixture source) can cost seconds where an ordinary
unit test costs milliseconds. Left untagged, these accumulate silently and
the fast leg's wall time creeps upward with no single commit to blame. PR
[#6095](https://github.com/richlander/dotnet-inspect/pull/6095) found and
tagged 134 such tests after the fast leg's `test` job grew from ~11-18min to
~30-45min over about five weeks — almost entirely from this kind of
untagged, individually-expensive test.

## Threshold

Tag a test `Speed=Slow` when it does one of the following:

- Runs whole-assembly or whole-solution analysis (e.g. `LibraryBodyIndex.Open`
  over a real multi-thousand-method assembly) more than once, or over more
  than one large assembly, in a single test.
- Is a corpus, fidelity, or determinism sweep whose entire purpose is
  exhaustive coverage rather than a single targeted assertion (see the
  decompiler suite's corpus/fidelity tests in
  [`docs/decompiler-correctness-pipeline.md`](decompiler-correctness-pipeline.md)
  for the established precedent).
- Measures at or above **2 seconds** of real wall time in isolation. Do not
  guess — measure with a real xUnit XML timing report:

  ```sh
  dotnet run --project src/dotnet-inspect.Tests -c Release -- \
    --filter-not-trait "Speed=Slow" --report-xunit \
    --report-xunit-filename fast-tests.xml --results-directory /tmp
  ```

  Static analysis (grepping for subprocess helpers, fixture size, etc.) is
  unreliable for this call — a file matching a "slow-looking" helper can have
  only a handful of genuinely slow tests among hundreds of cheap ones. Measure
  the actual per-test time before tagging.

Do not tag a whole test class merely because a few of its tests are slow;
tag the individual `[Fact]`/`[Theory]` methods that actually measure above
the threshold. Reserve class-level tagging (e.g.
`CommandExecutionTests`) for classes where the slow cost is genuinely
pervasive across nearly all of the class's tests.

## Placement convention

Place `[Trait("Speed", "Slow")]` as its own line directly after the
`[Fact]`/`[Theory]` attribute, before any `[InlineData(...)]` rows:

```csharp
[Fact]
[Trait("Speed", "Slow")]
public void SomeExpensiveTest()
```

```csharp
[Theory]
[Trait("Speed", "Slow")]
[InlineData("System.Private.CoreLib")]
[InlineData("System.Collections")]
public void SomeExpensiveTheory(string assemblyName)
```

## Existing consumers (no workflow changes needed to add a tag)

- `ci.yml`'s PR-blocking fast leg runs
  `dotnet run --project src/dotnet-inspect.Tests -c Release -- --filter-not-trait "Speed=Slow"`
  — a newly tagged test is automatically excluded.
- `deep-inspect.yml`'s nightly `dotnet-inspect.Tests` step runs fully
  unfiltered — a newly tagged test automatically keeps running nightly.
- The metadata suite uses the same MTP `--filter-not-trait "Speed=Slow"`
  selection in PR CI and the optional Windows PR workflow. Deep Inspect runs
  its full suite, including the pinned custom-attribute package gate, and
  retains that gate's per-platform evidence report.
- The decompiler suite uses the same trait, but its native xUnit console
  runner takes a different flag spelling than the CLI suite's Microsoft
  Testing Platform runner: `dotnet run --project
  src/ILInspector.Decompiler.Tests -c Release -- -trait- "Speed=Slow"`
  (fast) vs. `-trait "Speed=Slow"` (slow-only). See
  [`docs/decompiler-correctness-pipeline.md`](decompiler-correctness-pipeline.md)
  for that suite's full `Area`/`Speed` trait combination and its
  `--gate fast`/`--gate slow` equivalents.

Tagging a test is a policy change (when it runs), not a behavior change (what
it asserts). It requires no `.github/workflows/*.yml` edits.
