# Performance Triage run: Polly@8.7.0 — dotnet-inspect 0.15.0

One run = one library under one tool version. Pinned library, published
tool via `dnx`. Date: 2026-06-30.

- Library: `Polly@8.7.0` (pinned)
- Tool: `dotnet-inspect@0.15.0` (published, via `dnx -y`)
- Total triage rows: 434 (high 0, medium 9, low 425)

## Commands

```bash
dnx dotnet-inspect@0.15.0 -y -- library Polly@8.7.0 -S "Performance Triage" --top 25
dnx dotnet-inspect@0.15.0 -y -- library Polly@8.7.0 -S "Performance Triage" --loop --min-confidence high --top 25
dnx dotnet-inspect@0.15.0 -y -- library Polly@8.7.0 -S "Performance Triage"   # full, for row tallies
```

## Shape and confidence tally (full triage)

| Shape | Rows |
| ----- | ---: |
| `capturing-delegate` | 367 |
| `instance-method-group-delegate` | 59 |
| `small-array` | 5 |
| `box-value-type` | 2 |
| `scan-method-in-loop-call` | 1 |

| Confidence | Rows |
| ---------- | ---: |
| high | 0 |
| medium | 9 |
| low | 425 |

## Headline ranked triage (top 25)

```text
# Polly.dll

Name: Polly | Version: 8.7.0 | TFM: .NETCoreApp,Version=v6.0 | Arch: AnyCPU | Size: 281.4 KB | Source: NuGet | Modified: 2026-06-09

## Performance Triage

| Member | Root Reach | Shape | Evidence | Fix | Confidence | Loop | IL |
| ------ | ---------- | ----- | -------- | --- | ---------- | ---- | -- |
| `Polly.ExceptionPredicates.FirstMatchOrDefault(System.Exception)` | 27 | scan-method-in-loop-call | Linearly scans a sequence (Enumerable.FirstOrDefault); invoked inside a loop by Polly.Retry.RetryEngine::Implementation | A method that linearly scans a sequence is called on every iteration of a caller's loop; precompute an index the caller can reuse, or hoist the scan out of the loop. | low | loop |  |
| `Polly.ExceptionPredicates.FirstMatchOrDefault(System.Exception)` | 27 | capturing-delegate | delegate over a captured receiver or closure | Consumed by a lazy LINQ operator (Where/Select/…): a static local function removes this closure, but the LINQ call still allocates a deferred-query iterator per call — reduced, not eliminated. Replace the query with an explicit loop (or a precomputed index when used for lookups) to remove both. | medium |  | `IL_0020` |
| `Polly.ResultPredicates.AnyMatch(TResult)` | 15 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | medium |  | `IL_0024` |
| `Polly.Policy.Implementation(System.Action<Polly.Context, System.Threading.CancellationToken>, Polly.Context, System.Threading.CancellationToken)` | 10 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | medium |  | `IL_0015` |
| `Polly.Context.GetEnumerator()` | 1 | box-value-type | box System.Collections.Generic.Dictionary.Enumerator | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_000B` |
| `Polly.Context.System.Collections.IEnumerable.GetEnumerator()` | 1 | box-value-type | box System.Collections.Generic.Dictionary.Enumerator | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_000B` |
| `Polly.Policy.Wrap(Polly.ISyncPolicy[])` | 1 | small-array | newarr with small constant length (2) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0040` |
| `Polly.Policy.Wrap(Polly.ISyncPolicy<TResult>[])` | 1 | small-array | newarr with small constant length (2) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0040` |
| `Polly.Policy.WrapAsync(Polly.IAsyncPolicy[])` | 1 | small-array | newarr with small constant length (2) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0040` |
| `Polly.Policy.WrapAsync(Polly.IAsyncPolicy<TResult>[])` | 1 | small-array | newarr with small constant length (2) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0040` |
| `Polly.PolicyBuilder.HandleInner(System.Func<System.Exception, bool>)` | 8 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_0012` |
| `Polly.AsyncRetrySyntax.RetryAsync(Polly.PolicyBuilder, int, System.Func<System.Exception, int, Polly.Context, System.Threading.Tasks.Task>)` | 7 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_003C` |
| `Polly.Policy.Timeout(System.Func<Polly.Context, System.TimeSpan>, Polly.Timeout.TimeoutStrategy, System.Action<Polly.Context, System.TimeSpan, System.Threading.Tasks.Task>)` | 7 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_0029` |
| `Polly.Policy.Timeout(System.Func<Polly.Context, System.TimeSpan>, Polly.Timeout.TimeoutStrategy, System.Action<Polly.Context, System.TimeSpan, System.Threading.Tasks.Task>)` | 7 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_0029` |
| `Polly.Policy.TimeoutAsync(System.Func<Polly.Context, System.TimeSpan>, Polly.Timeout.TimeoutStrategy, System.Func<Polly.Context, System.TimeSpan, System.Threading.Tasks.Task, System.Threading.Tasks.Task>)` | 7 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_0029` |
| `Polly.Policy.TimeoutAsync(System.Func<Polly.Context, System.TimeSpan>, Polly.Timeout.TimeoutStrategy, System.Func<Polly.Context, System.TimeSpan, System.Threading.Tasks.Task, System.Threading.Tasks.Task>)` | 7 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_0029` |
| `Polly.AsyncRetryTResultSyntax.RetryAsync(Polly.PolicyBuilder<TResult>, int, System.Func<Polly.DelegateResult<TResult>, int, Polly.Context, System.Threading.Tasks.Task>)` | 6 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_003C` |
| `Polly.PolicyBuilder.OrResult(System.Func<TResult, bool>)` | 4 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_0012` |
| `Polly.AsyncRetrySyntax.RetryForeverAsync(Polly.PolicyBuilder, System.Func<System.Exception, Polly.Context, System.Threading.Tasks.Task>)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_0028` |
| `Polly.AsyncRetrySyntax.RetryForeverAsync(Polly.PolicyBuilder, System.Func<System.Exception, int, Polly.Context, System.Threading.Tasks.Task>)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_0028` |
| `Polly.AsyncRetrySyntax.WaitAndRetryAsync(Polly.PolicyBuilder, int, System.Func<int, Polly.Context, System.TimeSpan>, System.Func<System.Exception, System.TimeSpan, int, Polly.Context, System.Threading.Tasks.Task>)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_0029` |
| `Polly.AsyncRetrySyntax.WaitAndRetryForeverAsync(Polly.PolicyBuilder, System.Func<int, Polly.Context, System.TimeSpan>, System.Func<System.Exception, System.TimeSpan, Polly.Context, System.Threading.Tasks.Task>)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_0028` |
| `Polly.AsyncRetrySyntax.WaitAndRetryForeverAsync(Polly.PolicyBuilder, System.Func<int, System.Exception, Polly.Context, System.TimeSpan>, System.Func<System.Exception, System.TimeSpan, Polly.Context, System.Threading.Tasks.Task>)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_0036` |
| `Polly.AsyncRetryTResultSyntax.RetryForeverAsync(Polly.PolicyBuilder<TResult>, System.Func<Polly.DelegateResult<TResult>, Polly.Context, System.Threading.Tasks.Task>)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_0028` |
| `Polly.AsyncRetryTResultSyntax.RetryForeverAsync(Polly.PolicyBuilder<TResult>, System.Func<Polly.DelegateResult<TResult>, int, Polly.Context, System.Threading.Tasks.Task>)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | low |  | `IL_0028` |
```

## Hot, high-confidence subset (--loop --min-confidence high --top 25)

```text
# Polly.dll

Name: Polly | Version: 8.7.0 | TFM: .NETCoreApp,Version=v6.0 | Arch: AnyCPU | Size: 281.4 KB | Source: NuGet | Modified: 2026-06-09
```

## Read (this run)

This is `Polly.dll`, the thin facade. There are zero high-confidence rows and
only one row is actually loop-marked; 426 of 434 rows are low-confidence or
broad delegate-shape candidates (367 `capturing-delegate`, 59
`instance-method-group-delegate`). The single best candidate is
`ExceptionPredicates.FirstMatchOrDefault`. Note: Polly's real async/resilience
cost lives in `Polly.Core`, not this facade assembly.
