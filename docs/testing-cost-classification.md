# Classifying test cost

Classify every new or materially expanded test as PR-fast or
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

- Runs whole-assembly or whole-solution analysis (e.g.
  `LibraryBodyAnalysisService.ExecutePath`
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

The CI workflow has a ceiling of **16 runner jobs** for any event. Count matrix
entries separately and include the always-run `changes`, `provenance`, and
`ci-required` jobs. The workflow contract counts even path-gated jobs to keep
the bound safe as routing changes. The current workflow defines 14 jobs, with
the dependency-policy job selected only for pushes to `main`.

- `ci.yml` runs one Release solution build and a bounded smoke population for
  CLI routes, inspection queries, InertText, dependency policy, and the
  package-manifest verifier. It also runs the complete fast portable query
  suite and the producer-capability adopter tests, which hold the QuerySpace
  planning and result-validation rules that the Lean models prove
  ([Lean methodology](lean-methodology.md)); together they take a few seconds.
  Embedded skill tests run in that job when selected.
  The daily Linux Deep Inspect test lane runs the complete CLI, CSharp text,
  query, analysis, NuGet, metadata, and other host-neutral suites. Its
  Windows/macOS lane retains tests with platform-sensitive behavior.
- The daily test lane runs the slow legacy source-identity inventory over the
  full C# tree. It also owns the runtime-flavor and NativeAOT probes, the
  DEBUG-conditional sidecar test, authenticated package fixture, JSExport
  acceptance checks, and PR-quick decompiler corpus sensor formerly in the PR
  matrix. PR CI does not repeat that exhaustive and specialized work.
- Inspect Web keeps Browser/Wasm platform probes and managed API tests in PR
  CI. Its daily Deep Inspect web lane runs frontend analysis and build, Node
  tests, the browser engine, Firefox UI tests, complete facade and canary
  checks, and published-application validation. The daily frontend build
  generates its facades once before consuming them for analysis, build, and
  browser tests.
- Packaging PRs pack the pointer and `any` fallback packages. The daily Linux
  test lane packs all three variants, installs the tool from local packages,
  and runs the package command smokes.
- The decompiler suite uses the same MTP trait options behind discoverable
  presets: `dotnet run --project tests/ILInspector.Decompiler.Tests -c Release
  -- --gate fast` expands to `--filter-not-trait "Speed=Slow"`, while `--gate
  slow` expands to `--filter-trait "Speed=Slow"`. The path-gated
  `decompiler-gates` PR job owns the fast subset. Daily Deep Inspect runs the
  bounded compile-back receipt and `--gate no-corpus`, which owns every
  excluded non-corpus test, including broad whole-pipeline sweeps. See
  [`docs/decompiler-correctness-pipeline.md`](decompiler-correctness-pipeline.md)
  for that suite's full `Area`/`Speed` trait combination and its
  `--gate fast`/`--gate slow` equivalents.

  Deep Inspect runs that complete non-corpus population once on Linux.
  Windows and macOS retain the fast decompiler population as a low-cost
  boundary canary for newline, runtime-layout, and external-tool differences;
  slow and corpus coverage is not repeated on those hosts.

  #6889 is the scale reference for this policy: measurement found 247 cases at
  or above two seconds plus policy-defined corpus, fidelity, compile-back, and
  whole-assembly suites in the nominal fast preset. Classifying 41 wholly-slow
  classes and 72 individually-slow methods reduced the local fast path from
  6,940 tests in 2,300 seconds to 5,445 tests in 159 seconds, with no remaining
  case at or above the threshold.

Tagging a test is a policy change (when it runs), not a behavior change (what
it asserts). It requires no `.github/workflows/*.yml` edits.
