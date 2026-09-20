# C# printer allocation benchmarks

This BenchmarkDotNet tool measures allocations and throughput for the C# printer
without changing printer behavior. It is the dynamic confirmation layer for the
exact static string-materialization evidence added in
[#7912](https://github.com/richlander/dotnet-inspect/pull/7912) and tracked by
[#7921](https://github.com/richlander/dotnet-inspect/issues/7921).

## Measurement contract

Each workload has two independently reported paths:

- `RenderOnly` measures `CSharpPrinter.Print` over an already imported and
  raised `IrFunction`. Import and raising happen in `GlobalSetup`, outside the
  timed region.
- `ProductPath` imports a fresh `IrFunction` and runs
  `CSharpPrinter.PrintRaised` inside the timed region. It measures the ordinary
  import, raise, and print composition, preventing a change from appearing to
  win by moving work outside the printer.

The workloads are real compiled methods:

| Workload | Scale | Method |
| --- | --- | --- |
| `small` | Small | `List<T>.get_Count` from CoreLib |
| `representative` | Representative | `CSharpPrinter.AppendContainer` |
| `large` | Large | `CSharpPrinter.AppendStatementCore` |

The benchmark uses the repository's selected Release SDK, BenchmarkDotNet's
calibrated in-process job, and `MemoryDiagnoser`. BenchmarkDotNet 0.15.8 does
not yet recognize the .NET 11 runtime moniker in its default out-of-process
validator; the in-process job measures the actual selected .NET 11 host without
downgrading the product runtime or replacing BenchmarkDotNet with a one-shot
allocation counter. Benchmark output records the runtime, SDK, job, elapsed
time, and allocated bytes per operation. The two paths answer different
questions; their ratio is not an optimization target.

## Validate workload identity and output

Run validation before every baseline and comparison:

```bash
dotnet run --project tools/CSharpPrinter.Benchmarks -c Release -- --validate
```

Validation emits tab-separated workload scale, assembly, module version ID,
MethodDef token, output length, and SHA-256. It imports and renders each method
twice through the product path, then verifies that rendering the already raised
IR produces identical output. A missing method, failed render, unstable output,
or path mismatch fails visibly.

## Run the benchmark

Run the complete baseline:

```bash
dotnet run --project tools/CSharpPrinter.Benchmarks -c Release -- \
  --filter '*' --exporters markdown json
```

Use a short job only for harness smoke testing, never as the published
before/after result:

```bash
dotnet run --project tools/CSharpPrinter.Benchmarks -c Release -- \
  --filter '*' --job short
```

BenchmarkDotNet writes artifacts under `BenchmarkDotNet.Artifacts/`; that
directory is ignored. Preserve the validation output and the Markdown/JSON
summary for a before/after comparison, but do not commit machine-specific
benchmark artifacts.

## Optimization discipline

For a printer change:

1. Name the exact `Performance: Strings` or other Analysis finding that
   motivates the experiment.
2. Run validation and the baseline at the unchanged candidate.
3. Make one focused change and re-run on the same machine, runtime, SDK, and
   benchmark configuration.
4. Re-run source-built `dotnet-inspect` to confirm the targeted static operation
   changed as intended.
5. Reject the change if allocation merely moves, throughput materially
   regresses, or any output fingerprint changes.

Static evidence selects candidates; only the dynamic before/after result
establishes a realized performance win.
