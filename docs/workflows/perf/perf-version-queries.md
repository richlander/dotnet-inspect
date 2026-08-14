---
id: perf-version-queries
description: Latency targets for version-related commands
commands: [--version, --versions, --latest-version]
areas: [performance, versioning, cache]
---

# Performance: Version Queries

> Validate that version-related commands meet latency targets. These are among the most frequently called commands — agents use them to orient before deeper inspection.

All timings invoke the published NativeAOT executable directly. Local,
cache-backed scenarios use five warm samples so a single process launch does
not dominate the result. NuGet latest-version resolution remains a network
operation even after its local payload is warm and has a separate, looser smoke
bound.

## Preconditions

Point `INSPECT` at the published NativeAOT executable and validate the apphost:

```bash
set -e -o pipefail
: "${DOTNET_INSPECT_WORKFLOW_BINARY:?set the exact published apphost path}"
: "${DOTNET_INSPECT_WORKFLOW_VERSION:?set the expected --version output}"
export INSPECT="$DOTNET_INSPECT_WORKFLOW_BINARY"
test -x "$INSPECT"
test "$("$INSPECT" --version)" = "$DOTNET_INSPECT_WORKFLOW_VERSION"
"$INSPECT" --flavor | grep -q '^NativeAOT;'
export DOTNET_INSPECT_ISOLATED=perf-queries
```

```bash
"$INSPECT" cache clear
```

Prime an exact package payload and the version index:

```bash
"$INSPECT" package System.CommandLine@2.0.3 -v:q > /dev/null
"$INSPECT" package System.CommandLine --versions > /dev/null
```

Warm the payload for the actual latest version without pinning its value:

```bash
latest=$("$INSPECT" package System.CommandLine --latest-version | head -1)
"$INSPECT" package "System.CommandLine@$latest" -v:q > /dev/null
```

## 1. Exact cached version lookup

> Target: ≤ 250ms for five warm invocations. No network or NuGet index lookup.

```prompt
How fast is a cached version lookup?
```

```bash
for i in 1 2 3 4; do
  "$INSPECT" package System.CommandLine@2.0.3 --version > /dev/null
done
"$INSPECT" package System.CommandLine@2.0.3 --version
```

```expect
2.0.3
```

```perf
max_ms: 250
```

```query
head -1
```

## 2. Latest version from NuGet

> Network operation: resolve the current version from NuGet. The payload for
> that version is warm, but the version lookup itself may contact the feed.
> The 5s bound is an external-service smoke target, not a local latency target.

```bash
"$INSPECT" package System.CommandLine --latest-version
```

```perf
max_ms: 5000
```

```query
awk '/^[0-9]+\.[0-9]+\.[0-9]+([-.].*)?$/ { print "version-format-ok"; exit }'
```

```expect
version-format-ok
```

## 3. Full version list

> Target: ≤ 150ms for five warm reads of the cached version index.

```bash
for i in 1 2 3 4; do
  "$INSPECT" package System.CommandLine --versions > /dev/null
done
"$INSPECT" package System.CommandLine --versions
```

```perf
max_ms: 150
```

```query
awk '/^[0-9]+\.[0-9]+\.[0-9]+([-.].*)?$/ { count++ } END { if (count > 1) print "multiple-versions-ok" }'
```

```expect
multiple-versions-ok
```

## 4. @latest resolution

> Network operation: resolve `@latest` from NuGet, then use the already-warm
> payload. The 5s bound is an external-service smoke target.

```bash
"$INSPECT" package System.CommandLine@latest --version
```

```perf
max_ms: 5000
```

```query
awk '/^[0-9]+\.[0-9]+\.[0-9]+([-.].*)?$/ { print "version-format-ok"; exit }'
```

```expect
version-format-ok
```

## 5. Exact package metadata (quiet)

> Target: ≤ 250ms for five warm reads of a pinned package payload.

```bash
for i in 1 2 3 4; do
  "$INSPECT" package System.CommandLine@2.0.3 -v:q > /dev/null
done
"$INSPECT" package System.CommandLine@2.0.3 -v:q
```

```expect
Source: NuGet
```

```perf
max_ms: 250
```

```query
grep -o 'Source: [A-Za-z]*'
```

## 6. Type list for a platform library (quiet)

> Intended target: ≤ 250ms for five warm invocations (≤ 50ms each). Loads
> platform assembly metadata and enumerates public types.

```bash
for i in 1 2 3 4; do
  "$INSPECT" type System.Text.Json -v:q > /dev/null
done
"$INSPECT" type System.Text.Json -v:q
```

```expect
Source: Platform
```

```perf
max_ms: 250
```

```query
grep -o 'Source: [A-Za-z]*'
```

## 7. Package metadata (quiet)

> Target: ≤ 250ms for five warm reads of a pinned package.

```bash
for i in 1 2 3 4; do
  "$INSPECT" package System.CommandLine@2.0.3 -v:q > /dev/null
done
"$INSPECT" package System.CommandLine@2.0.3 -v:q
```

```expect
Source: NuGet
```

```perf
max_ms: 250
```

```query
grep -o 'Source: [A-Za-z]*'
```

## 8. Library metadata for platform assembly (quiet)

> Target: ≤ 300ms for five warm reads of platform assembly metadata.

```bash
for i in 1 2 3 4; do
  "$INSPECT" library System.Text.Json -v:q > /dev/null
done
"$INSPECT" library System.Text.Json -v:q
```

```expect
Source: Platform
```

```perf
max_ms: 300
```

```query
grep -o 'Source: [A-Za-z]*'
```

## 9. Error: nonexistent version (fast fail)

> Target: ≤ 150ms for five warm checks against the cached version index.

```bash
for i in 1 2 3 4; do
  "$INSPECT" package System.CommandLine@99.99.99 --version \
    > /dev/null 2>&1 || true
done
"$INSPECT" package System.CommandLine@99.99.99 --version 2>&1
```

```expect-error
not found
```

```perf
max_ms: 150
exit_code: 1
```

```query
grep 'not found'
```

## 10. Bare name routing after explicit package priming

> The explicit package command is network-backed after a cache clear and
> resolves the same unversioned candidate as the subsequent bare-name command.
> The bare-name command is measured warm and should reuse the cached candidate
> metadata and package payload. Its warm target is ≤ 1250ms for five invocations
> (≤ 250ms each).

```bash
"$INSPECT" cache clear
```

Prime the cache with explicit package routing:

```bash
"$INSPECT" type --package System.CommandLine Command \
  --markdown -v:q
```

```expect
Source: NuGet
```

```perf
max_ms: 10000
```

Now the bare name should route to the cached package, not re-download:

```bash
for i in 1 2 3 4; do
  "$INSPECT" type System.CommandLine -v:q > /dev/null
done
"$INSPECT" type System.CommandLine -v:q
```

```expect
Source: NuGet
```

```perf
max_ms: 1250
```

```query
grep -o 'Source: [A-Za-z]*'
```

Re-prime the cache for remaining tests:

```setup
"$INSPECT" package System.CommandLine@2.0.3 -v:q > /dev/null
"$INSPECT" package System.CommandLine --versions > /dev/null
```

## 11. Error: nonexistent package

> Network operation: NuGet must confirm that the package does not exist. Feed
> availability is an external precondition, so this scenario validates the
> diagnostic without imposing a local latency target.

```bash
"$INSPECT" package DotnetInspect.Workflow.Missing.Package@99.99.99 2>&1
```

```expect-error
not found
```

```query
grep 'not found'
```

## Profiling

See the [performance testing skill](../../../skills/workflow-scenarios/performance-testing.md) for dotnet-trace profiling and diagnosing unexpected network access.
