# PackageHouse CLI payload benchmarks

This BenchmarkDotNet tool compares the eager and pull-based CLI payload-transfer
paths introduced by [#8384](https://github.com/richlander/dotnet-inspect/pull/8384).
It measures the host-visible benefit before Browser/Wasm adoption.

## Measurement contract

The workload is the real `lib/net10.0/Avalonia.Base.dll` entry from the pinned
`Avalonia` 12.1.2 fixture. Its 2,355,200 expanded bytes are large enough to
make entry-sized allocation visible while remaining representative of a
package assembly a CLI user requests.

Both paths begin with the same admitted filesystem-backed
`PackageHouseSettlement.Acquired` and copy the same exact bytes to the same
non-retaining CLI destination:

- `EagerMaterializeThenWrite` opens the legacy seekable entry, allocates one
  complete entry-sized `byte[]`, fills it, then writes that array.
- `PullThenWrite` opens the cold House stream and copies it progressively with
  the CLI sink's pooled 64 KiB transfer buffer. Reading through EOF performs the
  candidate path's declared-size and CRC validation.

Package acquisition, archive admission, fixture extraction, validation hashes,
and process startup are global setup rather than measured work. CoreCLR is
therefore the appropriate runtime; this benchmark makes no startup claim and
does not require NativeAOT. The destination counts writes without retaining
bytes, modeling redirected CLI output while excluding filesystem and terminal
variance from the producer/copy comparison. The exact-file production sink is
separately gated by
`OutputFormatterTests.ExactByteDestination_PullsProgressivelyToAFile`.

Each fixed invocation performs 640 transfers so an iteration runs long enough
for stable timing; `OperationsPerInvoke` normalizes the result back to one CLI
transfer. Fixing invocation count also bounds the work performed by benchmark
calibration. BenchmarkDotNet's `MemoryDiagnoser` reports
allocated bytes and collections per transfer; 30 measured iterations report
elapsed time and the eager/pull ratio. A result is a win only when it eliminates
the entry-sized allocation without a material throughput regression.

## Validate workload identity and output

Run validation before every measurement:

```bash
dotnet run --project tools/PackageHouse.CliBenchmarks -c Release -- --validate
```

Validation emits the package, version, entry, expanded length, package SHA-256,
entry SHA-256, transfer-buffer size, and transfers per invocation. It also
executes both paths and requires exact byte equality.

## Run the benchmark

Run the publishable CoreCLR measurement:

```bash
dotnet run --project tools/PackageHouse.CliBenchmarks -c Release -- \
  --exporters markdown json
```

Use the short in-process configuration only to smoke-test the harness:

```bash
dotnet run --project tools/PackageHouse.CliBenchmarks -c Release -- \
  --smoke
```

BenchmarkDotNet writes ignored machine-specific artifacts under
`BenchmarkDotNet.Artifacts/`. Preserve the validation output and generated
Markdown/JSON result with the PR evidence; do not commit those artifacts.
