---
id: perf-version-queries
description: Latency targets for version-related commands
commands: [--version, --versions, --latest-version]
areas: [performance, versioning, cache]
---

# Performance: Version Queries

> Validate that version-related commands meet latency targets. These are among the most frequently called commands — agents use them to orient before deeper inspection.

All timings assume NativeAOT build (`./install.sh`) and warm OS cache (second+ invocation). First invocation after install may be 40-50ms due to OS-level cold start.

## Preconditions

Named isolated session ensures reproducible timing (no shared state, no NuGet cache).

```bash
export DOTNET_INSPECT_ISOLATED=perf-queries
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
dotnet-inspect System.CommandLine@2.0.2 -v:q
```

Prime the version index cache:

```bash
dotnet-inspect System.CommandLine --versions > /dev/null
```

## 1. Cached version lookup

> Target: ≤ 15ms. No network, no NuGet index — just app cache or NuGet cache on disk.

```prompt
How fast is a cached version lookup?
```

```bash
dotnet-inspect System.CommandLine --version
```

```expect
2.0.2
```

```perf
max_ms: 15
```

```query
head -1
```

## 2. Latest version from NuGet index

> Target: ≤ 25ms. Reads the version index (with TTL-based caching).

```bash
dotnet-inspect System.CommandLine --latest-version
```

```expect
2.0.3
```

```perf
max_ms: 25
```

```query
head -1
```

## 3. Full version list

> Target: ≤ 25ms. Same version index, returns all entries.

```bash
dotnet-inspect System.CommandLine --versions
```

```expect
2.0.3
2.0.2
```

```perf
max_ms: 25
```

```query
head -2
```

## 4. @latest resolution

> Target: ≤ 25ms. Resolves `@latest` to the current NuGet version.

```bash
dotnet-inspect System.CommandLine@latest --version
```

```expect
2.0.3
```

```perf
max_ms: 25
```

```query
head -1
```

## 5. Bare name to package metadata (quiet)

> Target: ≤ 25ms. Router resolves bare name, prints terse package info.

```bash
dotnet-inspect System.CommandLine -v:q
```

```expect
Source: NuGet
```

```perf
max_ms: 25
```

```query
grep -o 'Source: [A-Za-z]*'
```

## 6. Type list for a platform library (quiet)

> Target: ≤ 50ms. Loads assembly metadata and enumerates public types.

```bash
dotnet-inspect type System.Text.Json -v:q
```

```expect
Source: Platform
```

```perf
max_ms: 50
```

```query
grep -o 'Source: [A-Za-z]*'
```

## 7. Package metadata (quiet)

> Target: ≤ 50ms. Loads cached package and prints terse metadata.

```bash
dotnet-inspect package System.CommandLine -v:q
```

```expect
Source: NuGet
```

```perf
max_ms: 50
```

```query
grep -o 'Source: [A-Za-z]*'
```

## 8. Library metadata for platform assembly (quiet)

> Target: ≤ 50ms. Reads platform assembly metadata from disk.

```bash
dotnet-inspect library System.Text.Json -v:q
```

```expect
Source: Platform
```

```perf
max_ms: 50
```

```query
grep -o 'Source: [A-Za-z]*'
```

## 9. Error: nonexistent version (fast fail)

> Target: ≤ 25ms. Should fail from version index without network round-trip.

```bash
dotnet-inspect System.CommandLine@99.99.99 --version
```

```expect-error
not found
```

```perf
max_ms: 25
exit_code: 1
```

```query
grep 'not found'
```

## 10. Bare name routing vs explicit --package (cold cache)

> Target: ≤ 50ms. After clearing cache, `type --package` primes the cache. A subsequent bare name `type` should hit the cache — not re-download or re-resolve via platform probing.

```bash
dotnet-inspect cache clear
```

Prime the cache with explicit package:

```bash
dotnet-inspect type --package System.CommandLine -v:q
```

```expect
Source: NuGet
```

```perf
max_ms: 4000
```

Now the bare name should route to the cached package, not re-download:

```bash
dotnet-inspect type System.CommandLine -v:q
```

```expect
Source: NuGet
```

```perf
max_ms: 50
```

```query
grep -o 'Source: [A-Za-z]*'
```

Re-prime the cache for remaining tests:

```setup
dotnet-inspect System.CommandLine@2.0.2 -v:q
dotnet-inspect System.CommandLine --versions > /dev/null
```

## 11. Error: nonexistent package

> Target: ≤ 1000ms. Must query NuGet to confirm the package doesn't exist. This test legitimately requires network — when diagnosing with DEBUG builds, this test will trigger the network guard (expected).

```bash
dotnet-inspect System.CommandLine2@99.99.99
```

```expect-error
not found
```

```perf
max_ms: 1000
exit_code: 1
```

```query
grep 'not found'
```

## Profiling

See the [performance testing skill](../../../skills/workflow-scenarios/performance-testing.md) for dotnet-trace profiling, the `perf` command, and diagnosing unexpected network access.
