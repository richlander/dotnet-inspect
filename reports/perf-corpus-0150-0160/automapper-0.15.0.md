# Performance Triage run: AutoMapper@16.1.1 — dotnet-inspect 0.15.0

One run = one library under one tool version. Pinned library, published
tool via `dnx`. Date: 2026-06-30.

- Library: `AutoMapper@16.1.1` (pinned)
- Tool: `dotnet-inspect@0.15.0` (published, via `dnx -y`)
- Total triage rows: 199 (high 1, medium 129, low 69)

## Commands

```bash
dnx dotnet-inspect@0.15.0 -y -- library AutoMapper@16.1.1 -S "Performance Triage" --top 25
dnx dotnet-inspect@0.15.0 -y -- library AutoMapper@16.1.1 -S "Performance Triage" --loop --min-confidence high --top 25
dnx dotnet-inspect@0.15.0 -y -- library AutoMapper@16.1.1 -S "Performance Triage"   # full, for row tallies
```

## Shape and confidence tally (full triage)

| Shape | Rows |
| ----- | ---: |
| `small-array` | 107 |
| `capturing-delegate` | 68 |
| `instance-method-group-delegate` | 16 |
| `enumerator-allocation` | 5 |
| `scan-method-in-loop-call` | 1 |
| `linq-scan-in-loop` | 1 |
| `box-value-type` | 1 |

| Confidence | Rows |
| ---------- | ---: |
| high | 1 |
| medium | 129 |
| low | 69 |

## Headline ranked triage (top 25)

```text
# AutoMapper.dll

Name: AutoMapper | Version: 16.1.1 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 294 KB | Source: NuGet | Modified: 2026-03-13

## Performance Triage

| Member | Root Reach | Shape | Evidence | Fix | Confidence | Loop | IL |
| ------ | ---------- | ----- | -------- | --- | ---------- | ---- | -- |
| `AutoMapper.MapperConfigurationExpression.AddMapsCore(System.Collections.Generic.IEnumerable<System.Reflection.Assembly>)` | 12 | instance-method-group-delegate | delegate over an instance method group (binds the receiver) | Each call allocates a delegate that binds the receiver; cache it in a field when the receiver is stable, or use a static method with explicit state. | high | loop | `IL_0112` |
| `AutoMapper.TypeMap.TypeMapDetails.ApplyIncludedMemberTypeMap(AutoMapper.IncludedMember, AutoMapper.TypeMap)` | 22 | enumerator-allocation | foreach over an interface allocates a reference-type enumerator (System.Collections.Generic.IEnumerator&lt;AutoMapper.ValueTransformerConfiguration&gt;) | Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List&lt;T&gt;) uses a struct enumerator, or index/iterate it once outside the loop. | medium | loop | `IL_00C1` |
| `AutoMapper.Configuration.TypeMapConfiguration.ReverseIncludedMembers(AutoMapper.TypeMap)` | 18 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium | loop | `IL_0059` |
| `AutoMapper.ProfileMap.ApplyMemberMaps(AutoMapper.TypeMap, AutoMapper.Internal.IGlobalConfiguration)` | 18 | enumerator-allocation | foreach over an interface allocates a reference-type enumerator (System.Collections.Generic.IEnumerator&lt;AutoMapper.IncludedMember&gt;) | Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List&lt;T&gt;) uses a struct enumerator, or index/iterate it once outside the loop. | medium | loop | `IL_0055` |
| `AutoMapper.ProfileMap.Configure(AutoMapper.TypeMap, AutoMapper.Internal.IGlobalConfiguration)` | 18 | enumerator-allocation | foreach over an interface allocates a reference-type enumerator (System.Collections.Generic.IEnumerator&lt;AutoMapper.PropertyMapAction&gt;) | Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List&lt;T&gt;) uses a struct enumerator, or index/iterate it once outside the loop. | medium | loop | `IL_006A` |
| `AutoMapper.MapperConfigurationExpression.AddMapsCore(System.Collections.Generic.IEnumerable<System.Reflection.Assembly>)` | 12 | enumerator-allocation | foreach over an interface allocates a reference-type enumerator (System.Collections.Generic.IEnumerator&lt;AutoMapper.AutoMapAttribute&gt;) | Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List&lt;T&gt;) uses a struct enumerator, or index/iterate it once outside the loop. | medium | loop | `IL_00A9` |
| `AutoMapper.MapperConfigurationExpression.AddMapsCore(System.Collections.Generic.IEnumerable<System.Reflection.Assembly>)` | 12 | enumerator-allocation | foreach over an interface allocates a reference-type enumerator (System.Collections.Generic.IEnumerator&lt;AutoMapper.Configuration.IMemberConfigurationProvider&gt;) | Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List&lt;T&gt;) uses a struct enumerator, or index/iterate it once outside the loop. | medium | loop | `IL_00F3` |
| `AutoMapper.Internal.Mappers.StringToEnumMapper.CheckEnumMember(System.Linq.Expressions.Expression, System.Type, System.Linq.Expressions.Expression, System.Reflection.MethodInfo)` | 2 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium | loop | `IL_0085` |
| `AutoMapper.AutoMapperConfigurationException.get_Message()` | 1 | small-array | newarr with small constant length (6) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium | loop | `IL_01F7` |
| `AutoMapper.AutoMapperConfigurationException.get_Message()` | 1 | small-array | newarr with small constant length (6) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium | loop | `IL_0268` |
| `AutoMapper.Configuration.Conventions.ReplaceName.GetSourceMember(AutoMapper.Internal.TypeDetails, System.Type, System.Type, string)` | 1 | linq-scan-in-loop | Enumerable.Contains(...) inside a loop | Linear LINQ scan per iteration; precompute a set/dictionary index (or hoist the result) once outside the loop. | medium | loop | `IL_0060` |
| `AutoMapper.PropertyMap..ctor(AutoMapper.PropertyMap, AutoMapper.TypeMap)` | 22 | scan-method-in-loop-call | Linearly scans a sequence (Enumerable.Single); invoked inside a loop by AutoMapper.TypeMap.TypeMapDetails::&lt;ApplyInheritedTypeMap&gt;g__ApplyInheritedPropertyMaps&#124;62_0 | A method that linearly scans a sequence is called on every iteration of a caller's loop; precompute an index the caller can reuse, or hoist the scan out of the loop. | low | loop |  |
| `AutoMapper.Internal.TypeDetails.GetFields(System.Func<System.Reflection.FieldInfo, bool>)` | 42 | capturing-delegate | delegate over a captured receiver or closure | Consumed by a lazy LINQ operator (Where/Select/…): a static local function removes this closure, but the LINQ call still allocates a deferred-query iterator per call — reduced, not eliminated. Replace the query with an explicit loop (or a precomputed index when used for lookups) to remove both. | medium |  | `IL_0021` |
| `AutoMapper.Internal.TypeDetails.GetProperties(System.Func<System.Reflection.PropertyInfo, bool>)` | 42 | capturing-delegate | delegate over a captured receiver or closure | Consumed by a lazy LINQ operator (Where/Select/…): a static local function removes this closure, but the LINQ call still allocates a deferred-query iterator per call — reduced, not eliminated. Replace the query with an explicit loop (or a precomputed index when used for lookups) to remove both. | medium |  | `IL_0021` |
| `AutoMapper.Internal.TypeExtensions.GetBaseProperty(System.Type, string)` | 38 | capturing-delegate | delegate over a captured receiver or closure | Consumed by a lazy LINQ operator (Where/Select/…): a static local function removes this closure, but the LINQ call still allocates a deferred-query iterator per call — reduced, not eliminated. Replace the query with an explicit loop (or a precomputed index when used for lookups) to remove both. | medium |  | `IL_001A` |
| `AutoMapper.Internal.TypeExtensions.GetBaseField(System.Type, string)` | 28 | capturing-delegate | delegate over a captured receiver or closure | Consumed by a lazy LINQ operator (Where/Select/…): a static local function removes this closure, but the LINQ call still allocates a deferred-query iterator per call — reduced, not eliminated. Replace the query with an explicit loop (or a precomputed index when used for lookups) to remove both. | medium |  | `IL_001A` |
| `AutoMapper.Internal.TypeExtensions.GetBaseMethod(System.Type, string)` | 28 | capturing-delegate | delegate over a captured receiver or closure | Consumed by a lazy LINQ operator (Where/Select/…): a static local function removes this closure, but the LINQ call still allocates a deferred-query iterator per call — reduced, not eliminated. Replace the query with an explicit loop (or a precomputed index when used for lookups) to remove both. | medium |  | `IL_001A` |
| `AutoMapper.Internal.ReflectionHelper.GetMemberPath(System.Type, string[], AutoMapper.TypeMap)` | 25 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0017` |
| `AutoMapper.Execution.ObjectFactory.CallConstructor(System.Type, AutoMapper.Internal.IGlobalConfiguration)` | 23 | capturing-delegate | delegate over a captured receiver or closure | Consumed by a lazy LINQ operator (Where/Select/…): a static local function removes this closure, but the LINQ call still allocates a deferred-query iterator per call — reduced, not eliminated. Replace the query with an explicit loop (or a precomputed index when used for lookups) to remove both. | medium |  | `IL_00E6` |
| `AutoMapper.Execution.ObjectFactory.CreateReadOnlyDictionary(System.Type[])` | 23 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0018` |
| `AutoMapper.Execution.ObjectFactory.GetIEnumerableArguments(System.Type)` | 23 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0017` |
| `AutoMapper.Execution.ExpressionBuilder.ContextMap(AutoMapper.Internal.TypePair, System.Linq.Expressions.Expression, System.Linq.Expressions.Expression, AutoMapper.MemberMap)` | 22 | small-array | newarr with small constant length (2) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0006` |
| `AutoMapper.Execution.ExpressionBuilder.OverMaxDepth(AutoMapper.TypeMap)` | 22 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0019` |
| `AutoMapper.Execution.TypeMapPlanBuilder.CreateAssignmentFunc(System.Linq.Expressions.Expression)` | 22 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_004E` |
| `AutoMapper.Execution.TypeMapPlanBuilder.CreateAssignmentFunc(System.Linq.Expressions.Expression)` | 22 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_008C` |
```

## Hot, high-confidence subset (--loop --min-confidence high --top 25)

```text
# AutoMapper.dll

Name: AutoMapper | Version: 16.1.1 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 294 KB | Source: NuGet | Modified: 2026-03-13

## Performance Triage

| Member | Root Reach | Shape | Evidence | Fix | Confidence | Loop | IL |
| ------ | ---------- | ----- | -------- | --- | ---------- | ---- | -- |
| `AutoMapper.MapperConfigurationExpression.AddMapsCore(System.Collections.Generic.IEnumerable<System.Reflection.Assembly>)` | 12 | instance-method-group-delegate | delegate over an instance method group (binds the receiver) | Each call allocates a delegate that binds the receiver; cache it in a field when the receiver is stable, or use a static method with explicit state. | high | loop | `IL_0112` |
```

## Read (this run)

Pay-dirt is a single high-confidence row; the narrow real candidate is the
`AddMapsCore` config-time delegate allocation. AutoMapper is reflection- and
expression-heavy, so most of the 199 rows (129 medium, 69 low) are intrinsic to
the mapping engine and fail the discriminating test — they are how the engine
works, not isolated fixable costs.
