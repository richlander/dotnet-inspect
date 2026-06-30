# Performance Triage run: Newtonsoft.Json@13.0.4 — dotnet-inspect 0.16.0

One run = one library under one tool version. Pinned library, published
tool via `dnx`. Date: 2026-06-30.

- Library: `Newtonsoft.Json@13.0.4` (pinned)
- Tool: `dotnet-inspect@0.16.0` (published, via `dnx -y`)
- Total triage rows: 345 (high 15, medium 204, low 126)

## Commands

```bash
dnx dotnet-inspect@0.16.0 -y -- library Newtonsoft.Json@13.0.4 -S "Performance Triage" --top 25
dnx dotnet-inspect@0.16.0 -y -- library Newtonsoft.Json@13.0.4 -S "Performance Triage" --loop --min-confidence high --top 25
dnx dotnet-inspect@0.16.0 -y -- library Newtonsoft.Json@13.0.4 -S "Performance Triage"   # full, for row tallies
dnx dotnet-inspect@0.16.0 -y -- library Newtonsoft.Json@13.0.4 -S "Performance Triage" --triage-shape async-state-machine,materialize-in-loop --top 25
```

## Shape and confidence tally (full triage)

| Shape | Rows |
| ----- | ---: |
| `small-array` | 137 |
| `box-value-type` | 120 |
| `capturing-delegate` | 61 |
| `instance-method-group-delegate` | 10 |
| `linq-scan-in-loop` | 7 |
| `enumerator-allocation` | 7 |
| `scan-method-in-loop-call` | 3 |

| Confidence | Rows |
| ---------- | ---: |
| high | 15 |
| medium | 204 |
| low | 126 |

## Headline ranked triage (top 25)

```text
# Newtonsoft.Json.dll

Name: Newtonsoft.Json | Version: 13.0.4 | TFM: .NETCoreApp,Version=v6.0 | Arch: AnyCPU | Size: 706.4 KB | Source: NuGet | Modified: 2025-09-16

## Performance Triage

| Member | Root Reach | Shape | Evidence | Fix | Confidence | Loop | IL |
| ------ | ---------- | ----- | -------- | --- | ---------- | ---- | -- |
| `Newtonsoft.Json.JsonWriter.WriteValue(Newtonsoft.Json.JsonWriter, Newtonsoft.Json.Utilities.PrimitiveTypeCode, object)` | 78 | box-value-type | box System.Numerics.BigInteger | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_03BF` |
| `Newtonsoft.Json.Serialization.JsonSerializerInternalReader.CreateObjectUsingCreatorWithParameters(Newtonsoft.Json.JsonReader, Newtonsoft.Json.Serialization.JsonObjectContract, Newtonsoft.Json.Serialization.JsonProperty, Newtonsoft.Json.Serialization.ObjectConstructor<object>, string)` | 24 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_00FE` |
| `Newtonsoft.Json.JsonValidatingReader.ProcessValue()` | 9 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_0091` |
| `Newtonsoft.Json.JsonValidatingReader.WriteToken(System.Collections.Generic.IList<Newtonsoft.Json.Schema.JsonSchemaModel>)` | 9 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_00FB` |
| `Newtonsoft.Json.Utilities.ReflectionObject.Create(System.Type, System.Reflection.MethodBase, string[])` | 6 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_0140` |
| `Newtonsoft.Json.Utilities.ReflectionObject.Create(System.Type, System.Reflection.MethodBase, string[])` | 6 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_018B` |
| `Newtonsoft.Json.Utilities.DynamicUtils.BinderWrapper.CreateSharpArgumentInfoArray(int[])` | 3 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_0052` |
| `Newtonsoft.Json.Utilities.ReflectionUtils.GetChildPrivateProperties(System.Collections.Generic.IList<System.Reflection.PropertyInfo>, System.Type, System.Reflection.BindingFlags)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_0051` |
| `Newtonsoft.Json.Utilities.ReflectionUtils.GetChildPrivateProperties(System.Collections.Generic.IList<System.Reflection.PropertyInfo>, System.Type, System.Reflection.BindingFlags)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_00A3` |
| `Newtonsoft.Json.Utilities.ReflectionUtils.GetChildPrivateProperties(System.Collections.Generic.IList<System.Reflection.PropertyInfo>, System.Type, System.Reflection.BindingFlags)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_0114` |
| `Newtonsoft.Json.Utilities.ReflectionUtils.GetFieldsAndProperties(System.Type, System.Reflection.BindingFlags)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_00E7` |
| `Newtonsoft.Json.Linq.JContainer.MergeEnumerableContent(Newtonsoft.Json.Linq.JContainer, System.Collections.IEnumerable, Newtonsoft.Json.Linq.JsonMergeSettings)` | 2 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_0126` |
| `Newtonsoft.Json.Schema.JsonSchemaBuilder.ResolveReferences(Newtonsoft.Json.Schema.JsonSchema)` | 2 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_00C3` |
| `Newtonsoft.Json.Utilities.ExpressionReflectionDelegateFactory.BuildMethodCall(System.Reflection.MethodBase, System.Type, System.Linq.Expressions.ParameterExpression, System.Linq.Expressions.ParameterExpression)` | 2 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_005C` |
| `Newtonsoft.Json.Converters.DiscriminatedUnionConverter.ReadJson(Newtonsoft.Json.JsonReader, System.Type, object, Newtonsoft.Json.JsonSerializer)` | 1 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_007D` |
| `Newtonsoft.Json.Serialization.JsonSerializerInternalReader.CreateObjectUsingCreatorWithParameters(Newtonsoft.Json.JsonReader, Newtonsoft.Json.Serialization.JsonObjectContract, Newtonsoft.Json.Serialization.JsonProperty, Newtonsoft.Json.Serialization.ObjectConstructor<object>, string)` | 24 | linq-scan-in-loop | Enumerable.All(...) inside a loop | Linear LINQ scan per iteration; precompute a set/dictionary index (or hoist the result) once outside the loop. | medium | loop | `IL_0103` |
| `Newtonsoft.Json.Serialization.JsonSerializerInternalReader.CreateObjectUsingCreatorWithParameters(Newtonsoft.Json.JsonReader, Newtonsoft.Json.Serialization.JsonObjectContract, Newtonsoft.Json.Serialization.JsonProperty, Newtonsoft.Json.Serialization.ObjectConstructor<object>, string)` | 24 | enumerator-allocation | foreach over an interface allocates a reference-type enumerator (System.Collections.IEnumerator) | Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List&lt;T&gt;) uses a struct enumerator, or index/iterate it once outside the loop. | medium | loop | `IL_04BC` |
| `Newtonsoft.Json.JsonValidatingReader.WriteToken(System.Collections.Generic.IList<Newtonsoft.Json.Schema.JsonSchemaModel>)` | 9 | linq-scan-in-loop | Enumerable.Any(...) inside a loop | Linear LINQ scan per iteration; precompute a set/dictionary index (or hoist the result) once outside the loop. | medium | loop | `IL_005A` |
| `Newtonsoft.Json.JsonValidatingReader.WriteToken(System.Collections.Generic.IList<Newtonsoft.Json.Schema.JsonSchemaModel>)` | 9 | linq-scan-in-loop | Enumerable.Contains(...) inside a loop | Linear LINQ scan per iteration; precompute a set/dictionary index (or hoist the result) once outside the loop. | medium | loop | `IL_00E1` |
| `Newtonsoft.Json.JsonValidatingReader.WriteToken(System.Collections.Generic.IList<Newtonsoft.Json.Schema.JsonSchemaModel>)` | 9 | linq-scan-in-loop | Enumerable.First(...) inside a loop | Linear LINQ scan per iteration; precompute a set/dictionary index (or hoist the result) once outside the loop. | medium | loop | `IL_012A` |
| `Newtonsoft.Json.JsonValidatingReader.WriteToken(System.Collections.Generic.IList<Newtonsoft.Json.Schema.JsonSchemaModel>)` | 9 | linq-scan-in-loop | Enumerable.Any(...) inside a loop | Linear LINQ scan per iteration; precompute a set/dictionary index (or hoist the result) once outside the loop. | medium | loop | `IL_0165` |
| `Newtonsoft.Json.JsonValidatingReader.WriteToken(System.Collections.Generic.IList<Newtonsoft.Json.Schema.JsonSchemaModel>)` | 9 | enumerator-allocation | foreach over an interface allocates a reference-type enumerator (System.Collections.Generic.IEnumerator&lt;Newtonsoft.Json.Schema.JsonSchemaModel&gt;) | Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List&lt;T&gt;) uses a struct enumerator, or index/iterate it once outside the loop. | medium | loop | `IL_016D` |
| `Newtonsoft.Json.JsonValidatingReader.get_CurrentMemberSchemas()` | 9 | enumerator-allocation | foreach over an interface allocates a reference-type enumerator (System.Collections.Generic.IEnumerator&lt;System.Collections.Generic.KeyValuePair&lt;string, Newtonsoft.Json.Schema.JsonSchemaModel&gt;&gt;) | Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List&lt;T&gt;) uses a struct enumerator, or index/iterate it once outside the loop. | medium | loop | `IL_00E3` |
| `Newtonsoft.Json.Utilities.DynamicUtils.BinderWrapper.CreateSharpArgumentInfoArray(int[])` | 3 | small-array | newarr with small constant length (2) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium | loop | `IL_002D` |
| `Newtonsoft.Json.Utilities.DynamicUtils.BinderWrapper.CreateSharpArgumentInfoArray(int[])` | 3 | small-array | newarr with small constant length (2) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium | loop | `IL_004A` |
```

## Hot, high-confidence subset (--loop --min-confidence high --top 25)

```text
# Newtonsoft.Json.dll

Name: Newtonsoft.Json | Version: 13.0.4 | TFM: .NETCoreApp,Version=v6.0 | Arch: AnyCPU | Size: 706.4 KB | Source: NuGet | Modified: 2025-09-16

## Performance Triage

| Member | Root Reach | Shape | Evidence | Fix | Confidence | Loop | IL |
| ------ | ---------- | ----- | -------- | --- | ---------- | ---- | -- |
| `Newtonsoft.Json.JsonWriter.WriteValue(Newtonsoft.Json.JsonWriter, Newtonsoft.Json.Utilities.PrimitiveTypeCode, object)` | 78 | box-value-type | box System.Numerics.BigInteger | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_03BF` |
| `Newtonsoft.Json.Serialization.JsonSerializerInternalReader.CreateObjectUsingCreatorWithParameters(Newtonsoft.Json.JsonReader, Newtonsoft.Json.Serialization.JsonObjectContract, Newtonsoft.Json.Serialization.JsonProperty, Newtonsoft.Json.Serialization.ObjectConstructor<object>, string)` | 24 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_00FE` |
| `Newtonsoft.Json.JsonValidatingReader.ProcessValue()` | 9 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_0091` |
| `Newtonsoft.Json.JsonValidatingReader.WriteToken(System.Collections.Generic.IList<Newtonsoft.Json.Schema.JsonSchemaModel>)` | 9 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_00FB` |
| `Newtonsoft.Json.Utilities.ReflectionObject.Create(System.Type, System.Reflection.MethodBase, string[])` | 6 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_0140` |
| `Newtonsoft.Json.Utilities.ReflectionObject.Create(System.Type, System.Reflection.MethodBase, string[])` | 6 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_018B` |
| `Newtonsoft.Json.Utilities.DynamicUtils.BinderWrapper.CreateSharpArgumentInfoArray(int[])` | 3 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_0052` |
| `Newtonsoft.Json.Utilities.ReflectionUtils.GetChildPrivateProperties(System.Collections.Generic.IList<System.Reflection.PropertyInfo>, System.Type, System.Reflection.BindingFlags)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_0051` |
| `Newtonsoft.Json.Utilities.ReflectionUtils.GetChildPrivateProperties(System.Collections.Generic.IList<System.Reflection.PropertyInfo>, System.Type, System.Reflection.BindingFlags)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_00A3` |
| `Newtonsoft.Json.Utilities.ReflectionUtils.GetChildPrivateProperties(System.Collections.Generic.IList<System.Reflection.PropertyInfo>, System.Type, System.Reflection.BindingFlags)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_0114` |
| `Newtonsoft.Json.Utilities.ReflectionUtils.GetFieldsAndProperties(System.Type, System.Reflection.BindingFlags)` | 3 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_00E7` |
| `Newtonsoft.Json.Linq.JContainer.MergeEnumerableContent(Newtonsoft.Json.Linq.JContainer, System.Collections.IEnumerable, Newtonsoft.Json.Linq.JsonMergeSettings)` | 2 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_0126` |
| `Newtonsoft.Json.Schema.JsonSchemaBuilder.ResolveReferences(Newtonsoft.Json.Schema.JsonSchema)` | 2 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_00C3` |
| `Newtonsoft.Json.Utilities.ExpressionReflectionDelegateFactory.BuildMethodCall(System.Reflection.MethodBase, System.Type, System.Linq.Expressions.ParameterExpression, System.Linq.Expressions.ParameterExpression)` | 2 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_005C` |
| `Newtonsoft.Json.Converters.DiscriminatedUnionConverter.ReadJson(Newtonsoft.Json.JsonReader, System.Type, object, Newtonsoft.Json.JsonSerializer)` | 1 | capturing-delegate | delegate over a captured receiver or closure | Each call allocates a closure delegate; a static local function with explicit state parameters avoids it. | high | loop | `IL_007D` |
```

## 0.16.0 Rung 7 shapes (--triage-shape async-state-machine,materialize-in-loop)

```text
# Newtonsoft.Json.dll

Name: Newtonsoft.Json | Version: 13.0.4 | TFM: .NETCoreApp,Version=v6.0 | Arch: AnyCPU | Size: 706.4 KB | Source: NuGet | Modified: 2025-09-16
```

## Read (this run)

Identical to 0.15.0 — same total, same shape mix, same confidence mix, same
top-25 ordering. No `span-to-array-copy` rows existed to escape-gate away, and
no class async state machines were detected, so neither 0.16.0 mechanism fired.
The 15 high-confidence pay-dirt rows are preserved. A neutral run: no regression,
no visible improvement on this library.
