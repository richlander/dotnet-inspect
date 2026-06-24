# Performance Testing

> How to use perf workflows as a pre-ship gate to catch latency regressions before they reach users.

## Performance is a feature

dotnet-inspect is designed for sub-50ms responses on common queries. Agents call it repeatedly during migrations and API exploration — even small regressions compound into noticeable slowdowns. The perf workflows catch these before shipping.

## Prerequisites

Performance testing requires a **NativeAOT build** — CoreCLR and NativeAOT have very different baselines (~95ms vs ~7ms). Always measure with the production build:

```bash
./install.sh
```

Verify the install:

```bash
time dotnet-inspect --version
```

Expected: under 15ms steady-state.

## Running perf scenarios

Perf workflow docs use `perf` blocks with `max_ms` targets:

```markdown
`` `perf
max_ms: 25
`` `
```

To validate:

1. Set up an isolated session (avoids cache interference):

   ```bash
   export DOTNET_INSPECT_ISOLATED=perf-testing
   ```

2. Prime the cache as described in the workflow's Preconditions section.
3. Run each `bash` block and measure wall-clock time.
4. Compare against the `max_ms` target. The command passes if it completes within the target.

### Latency targets by command class

| Command class | Target | Example |
| --- | --- | --- |
| Version lookups | ≤ 15ms | `--version`, `--latest-version` |
| Cached metadata | ≤ 25ms | `package -v:q`, bare name routing |
| Type/member listing | ≤ 50ms | `type System.Text.Json -v:q` |
| Network-dependent | ≤ 1000ms | Nonexistent package lookup |

## Interpreting failures

A `max_ms` failure means the command is slower than expected. Common causes:

- **Unexpected network access** — a code path accidentally hits the network. Use the [network guard](network-guard.md) to diagnose.
- **Cache miss** — the preconditions didn't prime the cache correctly. Check the `setup` blocks.
- **Regression in hot path** — new code added overhead. Profile to find where.

## Profiling regressions

When a perf scenario fails, profile with `dotnet-trace` against the **Release CoreCLR apphost** (not NativeAOT — dotnet-trace requires the CLR runtime).

### Why not `dotnet run`?

`dotnet-trace collect -- dotnet run ...` measures the entire `dotnet run` host — MSBuild evaluation, assembly loading, JIT of the host itself — not just your app. Build first and trace the **apphost executable** directly.

### Setup

```bash
dotnet build src/dotnet-inspect -c Release
./artifacts/bin/dotnet-inspect/release/dotnet-inspect --version
```

### Profiling a single command

```bash
dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler -- \
  ./artifacts/bin/dotnet-inspect/release/dotnet-inspect --version
```

This produces a `.nettrace` file. Open it with:

- **Visual Studio** — built-in trace viewer
- **PerfView** — Windows, detailed analysis
- **speedscope** — browser-based flamegraph (`dotnet-trace convert --format Speedscope <file>.nettrace`)

### Profiling without hidden CLI commands

The public CLI no longer exposes dedicated `perf` or `perf-test` subcommands. When you need a profile, run the built app directly under `dotnet-trace` or wrap the target operation in a small local harness.

### Diagnosing unexpected network access

Unexpected network calls (especially PDB downloads from MSDL) can add 700ms+ latency to queries that should be instant. See the [network guard skill](network-guard.md) for how to catch and diagnose these.

## Reference

- [Version query perf scenarios](../../docs/workflows/perf/perf-version-queries.md) — the primary perf workflow with latency targets
