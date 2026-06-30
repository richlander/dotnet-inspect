# Performance Triage run: Serilog@4.3.1 — dotnet-inspect 0.15.0

One run = one library under one tool version. Pinned library, published
tool via `dnx`. Date: 2026-06-30.

- Library: `Serilog@4.3.1` (pinned)
- Tool: `dotnet-inspect@0.15.0` (published, via `dnx -y`)
- Total triage rows: 92 (high 3, medium 26, low 63)

## Commands

```bash
dnx dotnet-inspect@0.15.0 -y -- library Serilog@4.3.1 -S "Performance Triage" --top 25
dnx dotnet-inspect@0.15.0 -y -- library Serilog@4.3.1 -S "Performance Triage" --loop --min-confidence high --top 25
dnx dotnet-inspect@0.15.0 -y -- library Serilog@4.3.1 -S "Performance Triage"   # full, for row tallies
```

## Shape and confidence tally (full triage)

| Shape | Rows |
| ----- | ---: |
| `small-array` | 42 |
| `capturing-delegate` | 29 |
| `instance-method-group-delegate` | 16 |
| `box-value-type` | 3 |
| `enumerator-allocation` | 2 |

| Confidence | Rows |
| ---------- | ---: |
| high | 3 |
| medium | 26 |
| low | 63 |

## Headline ranked triage (top 25)

```text
# Serilog.dll

Name: Serilog | Version: 4.3.1-main-5625030 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 158.5 KB | Source: NuGet | Modified: 2026-02-11

## Performance Triage

| Member | Root Reach | Shape | Evidence | Fix | Confidence | Loop | IL |
| ------ | ---------- | ----- | -------- | --- | ---------- | ---- | -- |
| `Serilog.Configuration.LoggerSinkConfiguration.FallbackChain(System.Action<Serilog.Configuration.LoggerSinkConfiguration>, System.Action<Serilog.Configuration.LoggerSinkConfiguration>, System.Action<Serilog.Configuration.LoggerSinkConfiguration>[])` | 1 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_00E5` |
| `Serilog.Settings.KeyValuePairs.KeyValuePairSettings.ApplyDirectives(System.Collections.Generic.List<System.Linq.IGrouping<string, Serilog.Settings.KeyValuePairs.KeyValuePairSettings.ConfigurationMethodCall>>, System.Collections.Generic.IList<System.Reflection.MethodInfo>, object, System.Collections.Generic.IReadOnlyDictionary<string, Serilog.Core.LoggingLevelSwitch>)` | 1 | capturing-delegate | delegate over a captured receiver or closure | Consumed by a lazy LINQ operator (Where/Select/…): a static local function removes this closure, but the LINQ call still allocates a deferred-query iterator per call — reduced, not eliminated. Replace the query with an explicit loop (or a precomputed index when used for lookups) to remove both. | high | loop | `IL_0079` |
| `Serilog.Settings.KeyValuePairs.KeyValuePairSettings.ApplyDirectives(System.Collections.Generic.List<System.Linq.IGrouping<string, Serilog.Settings.KeyValuePairs.KeyValuePairSettings.ConfigurationMethodCall>>, System.Collections.Generic.IList<System.Reflection.MethodInfo>, object, System.Collections.Generic.IReadOnlyDictionary<string, Serilog.Core.LoggingLevelSwitch>)` | 1 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_0095` |
| `Serilog.Data.LogEventPropertyValueRewriter.VisitDictionaryValue(TState, Serilog.Events.DictionaryValue)` | 1 | enumerator-allocation | foreach over an interface allocates a reference-type enumerator (System.Collections.Generic.IEnumerator&lt;System.Collections.Generic.KeyValuePair&lt;Serilog.Events.ScalarValue, Serilog.Events.LogEventPropertyValue&gt;&gt;) | Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List&lt;T&gt;) uses a struct enumerator, or index/iterate it once outside the loop. | medium | loop | `IL_004F` |
| `Serilog.Formatting.Json.JsonFormatter.WriteRenderingsValues(System.Collections.Generic.IEnumerable<System.Linq.IGrouping<string, Serilog.Parsing.PropertyToken>>, System.Collections.Generic.IReadOnlyDictionary<string, Serilog.Events.LogEventPropertyValue>, System.IO.TextWriter)` | 1 | enumerator-allocation | foreach over an interface allocates a reference-type enumerator (System.Collections.Generic.IEnumerator&lt;Serilog.Parsing.PropertyToken&gt;) | Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List&lt;T&gt;) uses a struct enumerator, or index/iterate it once outside the loop. | medium | loop | `IL_0063` |
| `Serilog.ILogger.Write(Serilog.Events.LogEventLevel, System.Exception, string, object[])` | 110 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_002B` |
| `Serilog.Core.Logger.Write(Serilog.Events.LogEventLevel, System.Exception, string, object[])` | 20 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_002B` |
| `Serilog.ILogger.Write(Serilog.Events.LogEventLevel, string, T)` | 12 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_000D` |
| `Serilog.ILogger.Write(Serilog.Events.LogEventLevel, string, T0, T1)` | 12 | small-array | newarr with small constant length (2) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_000D` |
| `Serilog.ILogger.Write(Serilog.Events.LogEventLevel, string, T0, T1, T2)` | 12 | small-array | newarr with small constant length (3) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_000D` |
| `Serilog.ILogger.Write(Serilog.Events.LogEventLevel, System.Exception, string, T)` | 12 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_000E` |
| `Serilog.ILogger.Write(Serilog.Events.LogEventLevel, System.Exception, string, T0, T1)` | 12 | small-array | newarr with small constant length (2) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_000E` |
| `Serilog.ILogger.Write(Serilog.Events.LogEventLevel, System.Exception, string, T0, T1, T2)` | 12 | small-array | newarr with small constant length (3) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_000E` |
| `Serilog.LoggerConfiguration.CreateLogger()` | 8 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_004F` |
| `Serilog.Configuration.LoggerEnrichmentConfiguration.Wrap(Serilog.Configuration.LoggerEnrichmentConfiguration, System.Func<Serilog.Core.ILogEventEnricher, Serilog.Core.ILogEventEnricher>, System.Action<Serilog.Configuration.LoggerEnrichmentConfiguration>)` | 3 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_009A` |
| `Serilog.Configuration.LoggerDestructuringConfiguration.ByTransforming(System.Func<TValue, object>)` | 1 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0051` |
| `Serilog.Configuration.LoggerDestructuringConfiguration.ByTransformingWhere(System.Func<System.Type, bool>, System.Func<TValue, object>)` | 1 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_003F` |
| `Serilog.Configuration.LoggerDestructuringConfiguration.With()` | 1 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0002` |
| `Serilog.Configuration.LoggerEnrichmentConfiguration.With()` | 1 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0002` |
| `Serilog.Configuration.LoggerEnrichmentConfiguration.WithProperty(string, object, bool)` | 1 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0002` |
| `Serilog.Configuration.LoggerFilterConfiguration.ByExcluding(System.Func<Serilog.Events.LogEvent, bool>)` | 1 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_000F` |
| `Serilog.Configuration.LoggerFilterConfiguration.ByIncludingOnly(System.Func<Serilog.Events.LogEvent, bool>)` | 1 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0002` |
| `Serilog.Configuration.LoggerFilterConfiguration.With()` | 1 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0002` |
| `Serilog.Context.EnricherStack.System.Collections.Generic.IEnumerable<Serilog.Core.ILogEventEnricher>.GetEnumerator()` | 1 | box-value-type | box Serilog.Context.EnricherStack.Enumerator | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_0006` |
| `Serilog.Context.EnricherStack.System.Collections.IEnumerable.GetEnumerator()` | 1 | box-value-type | box Serilog.Context.EnricherStack.Enumerator | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_0006` |
```

## Hot, high-confidence subset (--loop --min-confidence high --top 25)

```text
# Serilog.dll

Name: Serilog | Version: 4.3.1-main-5625030 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 158.5 KB | Source: NuGet | Modified: 2026-02-11

## Performance Triage

| Member | Root Reach | Shape | Evidence | Fix | Confidence | Loop | IL |
| ------ | ---------- | ----- | -------- | --- | ---------- | ---- | -- |
| `Serilog.Configuration.LoggerSinkConfiguration.FallbackChain(System.Action<Serilog.Configuration.LoggerSinkConfiguration>, System.Action<Serilog.Configuration.LoggerSinkConfiguration>, System.Action<Serilog.Configuration.LoggerSinkConfiguration>[])` | 1 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_00E5` |
| `Serilog.Settings.KeyValuePairs.KeyValuePairSettings.ApplyDirectives(System.Collections.Generic.List<System.Linq.IGrouping<string, Serilog.Settings.KeyValuePairs.KeyValuePairSettings.ConfigurationMethodCall>>, System.Collections.Generic.IList<System.Reflection.MethodInfo>, object, System.Collections.Generic.IReadOnlyDictionary<string, Serilog.Core.LoggingLevelSwitch>)` | 1 | capturing-delegate | delegate over a captured receiver or closure | Consumed by a lazy LINQ operator (Where/Select/…): a static local function removes this closure, but the LINQ call still allocates a deferred-query iterator per call — reduced, not eliminated. Replace the query with an explicit loop (or a precomputed index when used for lookups) to remove both. | high | loop | `IL_0079` |
| `Serilog.Settings.KeyValuePairs.KeyValuePairSettings.ApplyDirectives(System.Collections.Generic.List<System.Linq.IGrouping<string, Serilog.Settings.KeyValuePairs.KeyValuePairSettings.ConfigurationMethodCall>>, System.Collections.Generic.IList<System.Reflection.MethodInfo>, object, System.Collections.Generic.IReadOnlyDictionary<string, Serilog.Core.LoggingLevelSwitch>)` | 1 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_0095` |
```

## Read (this run)

Pay-dirt is narrow: three high-confidence loop `capturing-delegate` rows
(`FallbackChain` has an always-allocated closure and delegate inside the loop)
plus two medium loop `enumerator-allocation` rows. The 42 `small-array` rows —
nearly half the table, many on public `ILogger.Write` params-object-array
overloads — rank high by Root Reach but reflect common logging API shape, not
pay-dirt; they fail the discriminating test without hot-path allocation
evidence.
