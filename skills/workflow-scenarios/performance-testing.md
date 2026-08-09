# Performance Testing

> How to use perf workflows as a pre-ship gate to catch latency regressions before they reach users.

## Performance is a feature

dotnet-inspect is designed for sub-50ms responses on common queries. Agents call it repeatedly during migrations and API exploration — even small regressions compound into noticeable slowdowns. The perf workflows catch these before shipping.

## Prerequisites

Performance testing requires a **NativeAOT build**. Publish the exact revision
under test, then identify both its apphost and version explicitly:

```bash
set -e -o pipefail
export DOTNET_INSPECT_WORKFLOW_BINARY=/tmp/dotnet-inspect-workflow-aot/dotnet-inspect
export DOTNET_INSPECT_WORKFLOW_VERSION="$(
  dotnet msbuild src/dotnet-inspect/dotnet-inspect.csproj \
    -getProperty:VersionPrefix -nologo
)+$(git rev-parse --short=7 HEAD)"
rm -rf /tmp/dotnet-inspect-workflow-aot
dotnet publish src/dotnet-inspect -c Release -r <runtime-id> \
  --self-contained true -o /tmp/dotnet-inspect-workflow-aot
export PATH="$(dirname "$DOTNET_INSPECT_WORKFLOW_BINARY"):$PATH"
```

Verify the install:

```bash
set -e -o pipefail
: "${DOTNET_INSPECT_WORKFLOW_BINARY:?set the exact published apphost path}"
: "${DOTNET_INSPECT_WORKFLOW_VERSION:?set the expected --version output}"
test -x "$DOTNET_INSPECT_WORKFLOW_BINARY"
test "$("$DOTNET_INSPECT_WORKFLOW_BINARY" --version)" = \
  "$DOTNET_INSPECT_WORKFLOW_VERSION"
test "$(command -v dotnet-inspect)" = "$DOTNET_INSPECT_WORKFLOW_BINARY"
"$DOTNET_INSPECT_WORKFLOW_BINARY" --flavor | grep -q '^NativeAOT;'
```

Expected flavor: `NativeAOT`.

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

The workflow document owns the current measured budgets. In particular,
network-backed latest-version and missing-package checks are external-service
smoke scenarios, not local-cache latency gates. Do not copy their limits into
other workflows; follow the `perf` block beside each command.

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
set -e -o pipefail
: "${DOTNET_INSPECT_WORKFLOW_VERSION:?set the expected --version output}"
dotnet build src/dotnet-inspect -c Release
export PROFILE_INSPECT="$PWD/artifacts/bin/dotnet-inspect/release/dotnet-inspect"
test -x "$PROFILE_INSPECT"
test "$("$PROFILE_INSPECT" --version)" = "$DOTNET_INSPECT_WORKFLOW_VERSION"
"$PROFILE_INSPECT" --flavor | grep -q '^CoreCLR;'
```

### Profiling a single command

```bash
dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler -- \
  "$PROFILE_INSPECT" --version
```

This produces a `.nettrace` file. Open it with:

- **Visual Studio** — built-in trace viewer
- **PerfView** — Windows, detailed analysis
- **speedscope** — browser-based flamegraph (`dotnet-trace convert --format Speedscope <file>.nettrace`)

### Profiling without hidden CLI commands

The public CLI no longer exposes dedicated `perf` or `perf-test` subcommands. When you need a profile, run the built app directly under `dotnet-trace` or wrap the target operation in a small local harness.

Use the regular `dotnet-inspect` command line with a warm cache and a repeated workload so `dotnet-trace` has enough samples to build a meaningful profile:

```bash
dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler -- \
  "$PROFILE_INSPECT" package System.Text.Json -v:q
```

### Cold vs warm comparison

Use a fresh cache to capture first-invocation latency (JIT, cache misses) and then compare against a warm run:

```bash
# Cold start (clear cache first)
"$PROFILE_INSPECT" cache clear
"$PROFILE_INSPECT" package System.Text.Json -v:q

# Warm path (same command, cache populated)
"$PROFILE_INSPECT" package System.Text.Json -v:q
```

### Diagnosing unexpected network access

Unexpected network calls (especially PDB downloads from MSDL) can add 700ms+ latency to queries that should be instant. See the [network guard skill](network-guard.md) for how to catch and diagnose these.

## Reference

- [Version query perf scenarios](../../docs/workflows/perf/perf-version-queries.md) — the primary perf workflow with latency targets
