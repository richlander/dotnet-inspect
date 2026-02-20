# Profiling with dotnet-trace

> How to profile dotnet-inspect with dotnet-trace to find where time is being spent.

## Why not `dotnet run`?

`dotnet-trace collect -- dotnet run ...` measures the entire `dotnet run` host — MSBuild evaluation, assembly loading, JIT of the host itself — not just your app. To profile the app in isolation, build first and then trace the **apphost executable** directly.

## Setup

Build the Release configuration (CoreCLR, not NativeAOT — dotnet-trace requires the CLR runtime):

```bash
dotnet build src/dotnet-inspect -c Release
```

Verify the apphost runs:

```bash
./artifacts/bin/dotnet-inspect/release/dotnet-inspect --version
```

## Profiling a single command

Trace the apphost directly:

```bash
dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler -- \
  ./artifacts/bin/dotnet-inspect/release/dotnet-inspect --version
```

This produces a `.nettrace` file in the current directory. Open it with:

- **Visual Studio** — built-in trace viewer
- **PerfView** — Windows, detailed analysis
- **speedscope** — browser-based flamegraph (`dotnet-trace convert --format Speedscope <file>.nettrace`)

## Profiling with the `perf` command

The hidden `perf` command runs a hot loop over a specific code path, which gives dotnet-trace enough samples to build a meaningful profile.

### Available modes

| Mode | Description | Example target |
| --- | --- | --- |
| `package` | Full package inspection | `System.CommandLine` |
| `version` | Version lookups (GetVersionsAsync) | `System.CommandLine` |
| `library` | Platform library access | `System.Text.Json` |
| `type` | Type listing from package or platform | `System.Text.Json` |

### Collecting a trace

```bash
dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler -- \
  ./artifacts/bin/dotnet-inspect/release/dotnet-inspect \
  perf System.Text.Json -n 100 --mode library
```

### Testing cold-start paths

Use `--skip-warmup` to measure first-invocation latency (JIT, cache misses, etc.):

```bash
./artifacts/bin/dotnet-inspect/release/dotnet-inspect \
  perf System.Text.Json -n 1 --mode library --skip-warmup
```

### Comparing cold vs warm

```bash
# Cold start (clear cache first)
./artifacts/bin/dotnet-inspect/release/dotnet-inspect cache clear
./artifacts/bin/dotnet-inspect/release/dotnet-inspect \
  perf System.Text.Json -n 1 --mode library --skip-warmup

# Warm path (same command, cache populated)
./artifacts/bin/dotnet-inspect/release/dotnet-inspect \
  perf System.Text.Json -n 10 --mode library
```

## Diagnosing unexpected network access

Unexpected network calls (especially PDB downloads from MSDL) can add 700ms+ latency to queries that should be instant. Two mechanisms help diagnose and prevent this:

- [**Network Guard**](network-guard.md) — DEBUG builds assert on unexpected HTTP requests, catching violations during development.
- [**Offline Usage**](offline-usage.md) — `--offline` blocks all network access at runtime, forcing cache-only operation in any build.
