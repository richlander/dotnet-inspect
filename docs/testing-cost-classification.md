# Classifying test cost

[AGENTS.md](../AGENTS.md#building-and-testing) states the binding rule:
classify every new or materially expanded test as PR-fast or
`[Trait("Speed", "Slow")]`. Exhaustive and whole-assembly tests are slow by
policy; otherwise measure suspected slow tests in isolation. A slow
classification is complete only when daily Deep Inspect or a focused
pre-merge gate owns the excluded evidence. This doc owns the threshold,
placement convention, and existing consumers.

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
  dotnet run --project tests/DotnetInspect.Cli.Tests -c Release -- \
    --filter-not-trait "Speed=Slow" --report-xunit-xml \
    --report-xunit-xml-filename fast-tests.xml --results-directory /tmp
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

- `ci.yml`'s PR-blocking fast leg filters `Speed=Slow` from the CLI and
  Analysis suites. The CLI selection is split across six parallel matrix
  entries: five select non-overlapping class-name prefix ranges, and the
  sixth selects their complement. The complement makes the partition
  exhaustive even when a future test class uses an unexpected identifier.
  `deep-inspect.yml` runs both suites fully unfiltered, so a newly tagged test
  automatically keeps running daily.
- The CSharp text and inspection-query suites use the same PR filter. Deep
  Inspect's daily platform lane runs both suites fully unfiltered.
- The offline NuGet suite excludes both `Network=Live` and `Speed=Slow` in PR
  CI. The daily platform lane retains the offline boundary but does not exclude
  `Speed=Slow`. The focused repository guard selects the legacy
  source-identity method directly, so that method remains a pre-merge gate for
  changed C# paths even though ordinary Linux test runs exclude it.
- The metadata suite uses the same MTP `--filter-not-trait "Speed=Slow"`
  selection in PR CI and the optional Windows PR workflow. Deep Inspect runs
  its full suite, including the pinned custom-attribute package gate, and
  retains that gate's per-platform evidence report.
- The decompiler suite uses the same MTP trait options behind discoverable
  presets: `dotnet run --project tests/ILInspector.Decompiler.Tests -c Release
  -- --gate fast` expands to `--filter-not-trait "Speed=Slow"`, while `--gate
  slow` expands to `--filter-trait "Speed=Slow"`. The path-gated
  `decompiler-gates` PR job owns the fast subset and a bounded compile-back
  receipt; daily Deep Inspect's `--gate no-corpus` run owns every excluded
  non-corpus test, including broad whole-pipeline sweeps. See
  [`docs/decompiler-correctness-pipeline.md`](decompiler-correctness-pipeline.md)
  for that suite's full `Area`/`Speed` trait combination and its
  `--gate fast`/`--gate slow` equivalents.

  #6889 is the scale reference for this policy: measurement found 247 cases at
  or above two seconds plus policy-defined corpus, fidelity, compile-back, and
  whole-assembly suites in the nominal fast preset. Classifying 41 wholly-slow
  classes and 72 individually-slow methods reduced the local fast path from
  6,940 tests in 2,300 seconds to 5,445 tests in 159 seconds, with no remaining
  case at or above the threshold.

Tagging a test is a policy change (when it runs), not a behavior change (what
it asserts). It requires no `.github/workflows/*.yml` edits.
