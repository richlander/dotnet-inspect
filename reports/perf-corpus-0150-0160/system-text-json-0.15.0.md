# Performance Triage run: System.Text.Json@10.0.9 — dotnet-inspect 0.15.0

One run = one library under one tool version. Pinned library, published
tool via `dnx`. Date: 2026-06-30.

- Library: `System.Text.Json@10.0.9` (pinned)
- Tool: `dotnet-inspect@0.15.0` (published, via `dnx -y`)
- Total triage rows: 158 (high 3, medium 118, low 37)

## Commands

```bash
dnx dotnet-inspect@0.15.0 -y -- library System.Text.Json@10.0.9 -S "Performance Triage" --top 25
dnx dotnet-inspect@0.15.0 -y -- library System.Text.Json@10.0.9 -S "Performance Triage" --loop --min-confidence high --top 25
dnx dotnet-inspect@0.15.0 -y -- library System.Text.Json@10.0.9 -S "Performance Triage"   # full, for row tallies
```

## Shape and confidence tally (full triage)

| Shape | Rows |
| ----- | ---: |
| `box-value-type` | 51 |
| `small-array` | 48 |
| `span-to-array-copy` | 27 |
| `capturing-delegate` | 19 |
| `instance-method-group-delegate` | 10 |
| `enumerator-allocation` | 2 |
| `allocation-hotspot` | 1 |

| Confidence | Rows |
| ---------- | ---: |
| high | 3 |
| medium | 118 |
| low | 37 |

## Headline ranked triage (top 25)

```text
# System.Text.Json.dll

Name: System.Text.Json | Version: 10.0.9 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 633.8 KB | Source: NuGet | Modified: 2026-05-21

## Performance Triage

| Member | Root Reach | Shape | Evidence | Fix | Confidence | Loop | IL |
| ------ | ---------- | ----- | -------- | --- | ---------- | ---- | -- |
| `System.Text.Json.JsonSerializer.TryReadMetadata(System.Text.Json.Serialization.JsonConverter, System.Text.Json.Serialization.Metadata.JsonTypeInfo, ref System.Text.Json.Utf8JsonReader, ref System.Text.Json.ReadStack)` | 5 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_035F` |
| `System.Text.Json.Serialization.Converters.DictionaryOfTKeyTValueConverter.OnWriteResume(System.Text.Json.Utf8JsonWriter, TCollection, System.Text.Json.JsonSerializerOptions, ref System.Text.Json.WriteStack)` | 1 | box-value-type | box System.Collections.Generic.Dictionary.Enumerator | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_010C` |
| `System.Text.Json.Serialization.Converters.DictionaryOfTKeyTValueConverter.OnWriteResume(System.Text.Json.Utf8JsonWriter, TCollection, System.Text.Json.JsonSerializerOptions, ref System.Text.Json.WriteStack)` | 1 | box-value-type | box System.Collections.Generic.Dictionary.Enumerator | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_018C` |
| `System.Text.Json.Serialization.IEnumerableConverterFactoryHelpers.GetImmutableDictionaryCreateRangeMethod(System.Type, System.Type, System.Type)` | 2 | small-array | newarr with small constant length (2) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium | loop | `IL_0051` |
| `System.Text.Json.Serialization.IEnumerableConverterFactoryHelpers.GetImmutableEnumerableCreateRangeMethod(System.Type, System.Type)` | 2 | small-array | newarr with small constant length (1) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium | loop | `IL_0051` |
| `System.Text.Json.JsonHelpers.TraverseGraphWithTopologicalSort(T, System.Func<T, System.Collections.Generic.ICollection<T>>, System.Collections.Generic.IEqualityComparer<T>)` | 1 | enumerator-allocation | foreach over an interface allocates a reference-type enumerator (System.Collections.Generic.IEnumerator&lt;T&gt;) | Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List&lt;T&gt;) uses a struct enumerator, or index/iterate it once outside the loop. | medium | loop | `IL_0089` |
| `System.Text.Json.Schema.JsonSchemaExporter.MapJsonSchemaCore(ref System.Text.Json.Schema.JsonSchemaExporter.GenerationState, System.Text.Json.Serialization.Metadata.JsonTypeInfo, System.Text.Json.Serialization.Metadata.JsonPropertyInfo, System.Text.Json.Serialization.JsonConverter, System.Nullable<System.Text.Json.Serialization.JsonNumberHandling>, System.Text.Json.Serialization.Metadata.JsonTypeInfo, bool, bool, System.Nullable<System.Collections.Generic.KeyValuePair<string, System.Text.Json.Schema.JsonSchema>>, bool)` | 1 | allocation-hotspot | 20 heap allocations in a loop (newobj/newarr/box) | Many allocations in one loop are often reducible: pool or cache reused objects, use spans/stackalloc for transient buffers, and avoid intermediate collections on hot paths. | medium | loop |  |
| `System.Text.Json.Serialization.Metadata.PolymorphicTypeResolver..ctor(System.Text.Json.JsonSerializerOptions, System.Text.Json.Serialization.Metadata.JsonPolymorphismOptions, System.Type, bool)` | 154 | enumerator-allocation | foreach over an interface allocates a reference-type enumerator (System.Collections.Generic.IEnumerator&lt;System.Text.Json.Serialization.Metadata.JsonPropertyInfo&gt;) | This allocation is in constructor/type-initializer setup, not a steady-state per-call path. Optimize only if profiles show this setup is hot or repeated unexpectedly. | low | loop | `IL_01F3` |
| `System.Text.Json.ThrowHelper.GetResourceString(System.Text.Json.ExceptionResource, int, int, byte, System.Text.Json.JsonTokenType)` | 194 | box-value-type | box char | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_004B` |
| `System.Text.Json.ThrowHelper.GetResourceString(System.Text.Json.ExceptionResource, int, int, byte, System.Text.Json.JsonTokenType)` | 194 | box-value-type | box char | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_005D` |
| `System.Text.Json.ThrowHelper.GetResourceString(System.Text.Json.ExceptionResource, int, int, byte, System.Text.Json.JsonTokenType)` | 194 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_007F` |
| `System.Text.Json.ThrowHelper.GetResourceString(System.Text.Json.ExceptionResource, int, int, byte, System.Text.Json.JsonTokenType)` | 194 | box-value-type | box System.Text.Json.JsonTokenType | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_0096` |
| `System.Text.Json.ThrowHelper.GetResourceString(System.Text.Json.ExceptionResource, int, int, byte, System.Text.Json.JsonTokenType)` | 194 | box-value-type | box System.Text.Json.JsonTokenType | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_00AA` |
| `System.Text.Json.ThrowHelper.GetResourceString(System.Text.Json.ExceptionResource, int, int, byte, System.Text.Json.JsonTokenType)` | 194 | box-value-type | box System.Text.Json.JsonTokenType | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_00BE` |
| `System.Text.Json.ThrowHelper.GetResourceString(System.Text.Json.ExceptionResource, int, int, byte, System.Text.Json.JsonTokenType)` | 194 | box-value-type | box System.Text.Json.JsonTokenType | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_00D7` |
| `System.Text.Json.ThrowHelper.GetResourceString(System.Text.Json.ExceptionResource, int, int, byte, System.Text.Json.JsonTokenType)` | 194 | box-value-type | box System.Text.Json.JsonTokenType | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_00FC` |
| `System.Text.Json.JsonEncodedText.EncodeHelper(System.ReadOnlySpan<byte>, System.Text.Encodings.Web.JavaScriptEncoder)` | 161 | span-to-array-copy | System.ReadOnlySpan&lt;T&gt;::ToArray | Let the span flow through to the consumer instead of materializing a copy when the array is not retained. | medium |  | `IL_001C` |
| `System.Text.Json.JsonHelpers.EscapeValue(System.ReadOnlySpan<byte>, int, System.Text.Encodings.Web.JavaScriptEncoder)` | 161 | span-to-array-copy | System.Span&lt;T&gt;::ToArray | Let the span flow through to the consumer instead of materializing a copy when the array is not retained. | medium |  | `IL_005D` |
| `System.Text.Json.ThrowHelper.ThrowInvalidOperationException_MultiplePropertiesBindToConstructorParameters(System.Type, string, string, string)` | 154 | small-array | newarr with small constant length (4) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_0006` |
| `System.Text.Json.BitStack.PushToArray(bool)` | 128 | small-array | newarr with small constant length (2) | If the array does not escape, a span or stackalloc may avoid the allocation. | medium |  | `IL_000A` |
| `System.Text.Json.ThrowHelper.GetJsonElementWrongTypeException(System.Text.Json.JsonValueKind, System.Text.Json.JsonValueKind)` | 126 | box-value-type | box System.Text.Json.JsonValueKind | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_000C` |
| `System.Text.Json.ThrowHelper.GetResourceString(ref System.Text.Json.Utf8JsonReader, System.Text.Json.ExceptionResource, byte, string)` | 81 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_00D7` |
| `System.Text.Json.ThrowHelper.GetResourceString(ref System.Text.Json.Utf8JsonReader, System.Text.Json.ExceptionResource, byte, string)` | 81 | box-value-type | box System.Text.Json.JsonTokenType | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_01EF` |
| `System.Text.Json.ThrowHelper.GetResourceString(ref System.Text.Json.Utf8JsonReader, System.Text.Json.ExceptionResource, byte, string)` | 81 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | medium |  | `IL_021A` |
| `System.Text.Json.Utf8JsonReader.CheckLiteralMultiSegment(System.ReadOnlySpan<byte>, System.ReadOnlySpan<byte>, ref int)` | 81 | span-to-array-copy | System.Span&lt;T&gt;::ToArray | Let the span flow through to the consumer instead of materializing a copy when the array is not retained. | medium |  | `IL_0284` |
```

## Hot, high-confidence subset (--loop --min-confidence high --top 25)

```text
# System.Text.Json.dll

Name: System.Text.Json | Version: 10.0.9 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 633.8 KB | Source: NuGet | Modified: 2026-05-21

## Performance Triage

| Member | Root Reach | Shape | Evidence | Fix | Confidence | Loop | IL |
| ------ | ---------- | ----- | -------- | --- | ---------- | ---- | -- |
| `System.Text.Json.JsonSerializer.TryReadMetadata(System.Text.Json.Serialization.JsonConverter, System.Text.Json.Serialization.Metadata.JsonTypeInfo, ref System.Text.Json.Utf8JsonReader, ref System.Text.Json.ReadStack)` | 5 | box-value-type | box int | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_035F` |
| `System.Text.Json.Serialization.Converters.DictionaryOfTKeyTValueConverter.OnWriteResume(System.Text.Json.Utf8JsonWriter, TCollection, System.Text.Json.JsonSerializerOptions, ref System.Text.Json.WriteStack)` | 1 | box-value-type | box System.Collections.Generic.Dictionary.Enumerator | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_010C` |
| `System.Text.Json.Serialization.Converters.DictionaryOfTKeyTValueConverter.OnWriteResume(System.Text.Json.Utf8JsonWriter, TCollection, System.Text.Json.JsonSerializerOptions, ref System.Text.Json.WriteStack)` | 1 | box-value-type | box System.Collections.Generic.Dictionary.Enumerator | Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it. | high | loop | `IL_018C` |
```

## Read (this run)

Pay-dirt is narrow: the three high-confidence, loop-marked rows are the real
candidates — `JsonSerializer.TryReadMetadata` boxing an `int`, and the two
`DictionaryOfTKeyTValueConverter.OnWriteResume` boxed `Dictionary.Enumerator`
rows. Everything else fails the discriminating test on this hand-optimized
library: 51 `box-value-type` are largely `ThrowHelper` exception-path boxing,
48 `small-array` are reflection/setup helpers, and the 27 `span-to-array-copy`
rows are mostly deliberate non-escaping materialization at API/state boundaries
(`JsonEncodedText.EncodeHelper`, `EscapeValue`, `CheckLiteralMultiSegment`) —
i.e. near-universal noise, not fixable pay-dirt.
