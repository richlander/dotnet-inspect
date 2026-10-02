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
  dotnet msbuild src/DotnetInspect.Cli/DotnetInspect.Cli.csproj \
    -getProperty:VersionPrefix -nologo
)+$(git rev-parse --short=7 HEAD)"
dotnet clean src/DotnetInspect.Cli -c Release -r <runtime-id>
rm -rf /tmp/dotnet-inspect-workflow-aot
dotnet publish src/DotnetInspect.Cli -c Release -r <runtime-id> \
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
network-backed `@latest` and missing-package checks are external-service
smoke scenarios, not local-cache latency gates. Do not copy their limits into
other workflows; follow the `perf` block beside each command.

## Interpreting failures

A `max_ms` failure means the command is slower than expected. Common causes:

- **Unexpected network access** — a code path unexpectedly starts HTTP work. Use [network observation](network-guard.md) to diagnose it and `--offline` to enforce a no-network run.
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
dotnet build src/DotnetInspect.Cli -c Release -t:Rebuild
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

Unexpected network calls, especially PDB downloads from MSDL, can add material
latency to otherwise local queries. See
[network observation](network-guard.md) for Debug request logging and offline
enforcement.

## Correlate static triage with an allocation trace

Export nested JSON, whose deep rows carry the declaring method coordinate. The
`runfaster` prototype is available only in the dotnet-inspect repository and is
not included in the published packages. From a source checkout, pass it that
document and a trace captured from the same assembly build:

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "Performance:*" \
  --where "Priority>=high" --json > triage.json
dotnet run --project src/runfaster -- \
  correlate --triage triage.json --trace workload.nettrace
```

Compact `Performance:* --jsonl` rows omit deep provenance and cannot support an
exact trace join. `runfaster` keeps their operation `Token` separate from the
source-facing `MethodToken`, uses `EvidenceMethod` as the physical body token
when supplied, and reports missing runtime coordinates explicitly. Blank
flattened cells are treated as absent; invalid non-empty or conflicting
supplied evidence-method tokens fail visibly.
Method-name samples can still establish method-level heat, but only a complete
runtime coordinate can produce an exact `confirmed-hot` result.
For a filtered export, the trace join stops at the first frame in the
represented assembly; it does not walk past an unexported in-assembly callee
and credit an outer caller. If `--library` and `--triage` name the same physical
candidate, the shape-compatible triage row carries the runtime evidence.
The raw library row is marked `superseded-by-triage`, not workload-cold.
Exact `string-materialization` rows intentionally have no static allocated
type. RunFaster accepts only an observed `System.String` at their same-build
nearest-preceding IL coordinate and lists the result under
`Runtime-confirmed string materialization`. Those rows remain outside the
automatic optimization verdict because runtime volume alone cannot distinguish
required output from removable intermediate text; inspect the result consumer
before choosing a rewrite. Supplied allocation-type fields, method-only heat,
and aggregate `SupportingCallSite` coordinates cannot confirm string
materialization: none identifies an observed `System.String` allocation at the
exact string-producing operation.
For a repeated-scan aggregate with a supporting call site, `runfaster` promotes
an allocation observation only when the same build has a raw library allocation
at that coordinate and exactly one aggregate support in that build claims it.
Each build resolves independently before cross-build ambiguity attribution.
The raw site is the attribution anchor; sampled allocation types can differ
because GC allocation ticks resolve to the nearest preceding IL allocation
site. RunFaster resolves the nearest raw allocation first, then attaches support
at that exact coordinate; a later non-allocation scan-call support therefore
cannot hide the raw site. When an exact triage row and one aggregate support
both project the same raw site, the aggregate carries the evidence and the
exact row is superseded rather than splitting bytes. An exact row at another
offset or from another build remains independent. Method-name and CPU samples
can mark the aggregate method hot, but only an accepted allocation-coordinate
join supersedes its raw allocation anchor. Otherwise the aggregate remains cold
for the workload and the raw row keeps the evidence.
Type-level ambiguity and its site cap count the shared coordinate once unless
several library MVIDs make an older MVID-less triage row's module version
ambiguous.
Triage and library inputs from different builds retain distinct MVIDs and can
therefore increase ambiguity or exceed the type-confirmation site cap.

## Reference

- [Version query perf scenarios](../../../docs/workflows/perf/perf-version-queries.md) —
  the primary perf workflow with latency targets
