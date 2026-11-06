# Copilot CLI Session

> [!NOTE]
> - **Session ID:** `2107511f-527b-4e16-8338-8268b16922e3`  
> - **Started:** 11/6/2026, 2:57:28 PM  
> - **Duration:** 1m 23s  
> - **Exported:** 11/6/2026, 2:58:52 PM  

---

<sub>2s</sub>

### User

You are a fresh participant in a cooperative usability trial. Your scratch directory is /tmp/cli-trial-20261106/document-1; work only there. Do not read anything else on this machine outside your scratch directory and the .NET SDK, including repositories, designs, earlier results, or other trials. Do not spawn agents. This protocol is cooperative, not a security sandbox.

Guide:
You are working without any .NET inspection tool. You have a shell, the .NET SDK, Python, jq, and network access to nuget.org, the Microsoft symbol server, and source hosts such as GitHub. dotnet-inspect is not available: do not install, download, or invoke it, including through dnx.
Answer with evidence you obtain yourself, for example from the package files, its XML documentation, metadata you read with your own code, or the published source. Keep downloads and scripts in your scratch directory.
The user values low latency. Do only the work a question needs, stop when the evidence answers it, and say plainly when it does not.
A Library symbol document for System.Text.Json 10.0.14 (lib/net10.0) is in your scratch directory as System.Text.Json.symbols.json. Read it with your own jq or grep. It is one self-describing JSON object: a header naming the library and request, types keyed by documentation ID, members with one record per line, lookup dictionaries derived from those records, and section outcomes. Every Type and Member is identified by its documentation ID. It was generated before this session.

Write answer.json in your scratch directory as {"answers": [{"question": N, "answer": ..., "evidence": [...], "complete": true or false, "why": "..."}], "friction": "..."}. Cite the commands, files, or URLs each answer rests on. Do not count your own tool calls or bytes. Stop with explicit uncertainty when the evidence is insufficient.

Task:
A developer is working with System.Text.Json 10.0.14 from nuget.org (lib/net10.0). Answer these questions in order. For each answer, cite the evidence it rests on and say whether you believe it is complete and why.

1. Which public or protected APIs in the library accept a System.IO.Stream parameter? List every one, with its declaring type and signature.
2. Which APIs give a caller a JsonDocument, whether as a return value, as the result of a Task or ValueTask, or through an out parameter? List every one.
3. Which APIs anywhere in the library are marked RequiresUnreferencedCode? Group them by declaring type and member name, and for each group say whether a same-named overload exists that is not marked.
4. What are the exact signature and the documentation summary of JsonDocument.ParseAsync?
5. Which types in the library are named for converters? For each one a developer outside the library can derive from, what must the derived class override?
6. Where in source, as a file URL and line, is each JsonSerializer.Deserialize overload that takes a Stream?

---

<sub>6s</sub>

### `bash`

**Inspect symbol document structure**

`$ pwd; ls -la; wc -c System.Text.Json.symbols.json; jq 'keys, (.header // {}), (.sectionOutcomes // {})' System.Text.Json.symbols.json | head -150`

<details>
<summary>33 lines</summary>

```
/tmp/cli-trial-20261106/document-1
total 3976
drwxr-xr-x@  8 rich  wheel      256 Nov  6 14:57 .
drwxr-xr-x@ 12 rich  wheel      384 Nov  6 14:57 ..
-rw-r--r--@  1 rich  wheel        0 Nov  6 14:57 err.txt
-rw-r--r--@  1 rich  wheel     2913 Nov  6 14:57 prompt.txt
-rw-r--r--@  1 rich  wheel        0 Nov  6 14:57 result.md
-rwxr-xr-x@  1 rich  wheel     1026 Nov  6 14:57 run.sh
-rw-r--r--@  1 rich  wheel       37 Nov  6 14:57 session-id
-rw-r--r--@  1 rich  wheel  2021861 Nov  6 14:57 System.Text.Json.symbols.json
 2021861 System.Text.Json.symbols.json
[
  "attribute_type_members",
  "base_type_derived_types",
  "counts",
  "generator",
  "interface_implementing_types",
  "library",
  "members",
  "name_word_types",
  "outcome",
  "parameter_type_members",
  "produced_type_members",
  "request",
  "return_type_members",
  "schema_version",
  "section_outcomes",
  "suffix_family_types",
  "types"
]
{}
{}
<shellId: 0 completed with exit code 0>
```

</details>


---

<sub>9s</sub>

### `bash`

**Understand member and lookup schemas**

`$ jq '{library,request,counts,section_outcomes,outcome, generator, schema_version, member0:(.members|if type=="array" then .[0] else (to_entries|.[0]) end),type0:(.types|to_entries|.[0]),paramlookup:(.parameter_type_members|keys|.[0:12]),returnlookup:(.return_type_members|keys|.[0:12]),producedlookup:(.produced_type_members|keys|.[0:12]),attributelookup:(.attribute_type_members|keys|.[0:12]),namelookup:(.name_word_types|keys|.[0:12])}' System.Text.Json.symbols.json | head -250`

<details>
<summary>216 lines</summary>

```
{
  "library": {
    "name": "System.Text.Json",
    "file_name": "System.Text.Json.dll",
    "assembly_version": "10.0.0.0",
    "module_version_id": "2ddc75da-7a95-4826-8729-839cc65f9eea",
    "source_kind": "nuget",
    "package_id": "System.Text.Json",
    "package_version": "10.0.14",
    "package_path": "lib/net10.0/System.Text.Json.dll",
    "target_framework": "net10.0"
  },
  "request": {
    "admission": "public_api",
    "sections": [
      "declarations",
      "name_families",
      "documentation",
      "source_link"
    ]
  },
  "counts": {
    "types": 85,
    "members": 975
  },
  "section_outcomes": [
    {
      "name": "declarations",
      "status": "complete"
    },
    {
      "name": "name_families",
      "status": "complete"
    },
    {
      "name": "documentation",
      "status": "complete"
    },
    {
      "name": "source_link",
      "status": "complete"
    },
    {
      "name": "findings",
      "status": "not_requested"
    }
  ],
  "outcome": "complete",
  "generator": {
    "name": "library-symbol-document-prototype",
    "version": "0"
  },
  "schema_version": "1",
  "member0": {
    "documentation_id": "M:System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(System.Text.Json.JsonElement)",
    "canonical_signature": "M:System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(System.Text.Json.JsonElement)",
    "display_signature": "System.ReadOnlySpan<byte> GetRawUtf8Value(System.Text.Json.JsonElement element)",
    "declaring_type": "T:System.Runtime.InteropServices.JsonMarshal",
    "namespace": "System.Runtime.InteropServices",
    "declaring_type_suffix_family": "Marshal",
    "declaring_type_two_word_suffix_family": "JsonMarshal",
    "declaring_type_name_words": [
      "Json",
      "Marshal"
    ],
    "kind": "method",
    "name": "GetRawUtf8Value",
    "accessibility": "public",
    "is_static": true,
    "is_abstract": false,
    "is_virtual": false,
    "is_override": false,
    "is_sealed": false,
    "is_extension": false,
    "generic_parameters": [],
    "return_type": "System.ReadOnlySpan<byte>",
    "return_type_mentions": [
      "T:System.Byte",
      "T:System.ReadOnlySpan`1"
    ],
    "produced_types": [
      "T:System.ReadOnlySpan`1"
    ],
    "parameters": [
      {
        "name": "element",
        "type": "System.Text.Json.JsonElement",
        "type_mentions": [
          "T:System.Text.Json.JsonElement"
        ],
        "modifier": "",
        "has_default_value": false
      }
    ],
    "overrides": "",
    "attribute_types": [],
    "documentation": {
      "status": "complete",
      "summary": "Gets a ReadOnlySpan view over the raw JSON data of the given JsonElement."
    },
    "source": {
      "status": "complete",
      "url": "https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Runtime/InteropServices/JsonMarshal.cs",
      "line": 27
    },
    "findings": [],
    "metadata_token": 100663531
  },
  "type0": {
    "key": "T:System.Runtime.CompilerServices.IsExternalInit",
    "value": {
      "name": "IsExternalInit",
      "metadata_name": "IsExternalInit",
      "namespace": "System.Runtime.CompilerServices",
      "declaring_type": "",
      "declaration_kind": "forwarder",
      "forwarded_to_assembly": "System.Runtime",
      "definition_kind": "",
      "accessibility": "public",
      "is_static": false,
      "is_abstract": false,
      "is_sealed": false,
      "constructor_accessibility": "",
      "generic_parameters": [],
      "base_type": "",
      "interfaces": [],
      "required_overrides": [],
      "inaccessible_required_overrides": 0,
      "attribute_types": [],
      "suffix_family": "",
      "two_word_suffix_family": "",
      "name_words": [
        "Is",
        "External",
        "Init"
      ],
      "documentation": {
        "status": "forwarder",
        "summary": ""
      },
      "source": {
        "status": "forwarder",
        "urls": []
      },
      "findings": [],
      "metadata_token": 654311425
    }
  },
  "paramlookup": [
    "T:System.Action`1",
    "T:System.Action`2",
    "T:System.Boolean",
    "T:System.Buffers.IBufferWriter`1",
    "T:System.Buffers.ReadOnlySequence`1",
    "T:System.Byte",
    "T:System.Char",
    "T:System.Collections.Generic.IEnumerable`1",
    "T:System.Collections.Generic.KeyValuePair`2",
    "T:System.DateTime",
    "T:System.DateTimeOffset",
    "T:System.Decimal"
  ],
  "returnlookup": [
    "T:System.Action`1",
    "T:System.Action`2",
    "T:System.Boolean",
    "T:System.Buffers.ReadOnlySequence`1",
    "T:System.Byte",
    "T:System.Char",
    "T:System.Collections.Generic.IAsyncEnumerable`1",
    "T:System.Collections.Generic.IEnumerable`1",
    "T:System.Collections.Generic.IEnumerator`1",
    "T:System.Collections.Generic.IList`1",
    "T:System.Collections.Generic.KeyValuePair`2",
    "T:System.DateOnly"
  ],
  "producedlookup": [
    "T:System.Action`1",
    "T:System.Action`2",
    "T:System.Boolean",
    "T:System.Buffers.ReadOnlySequence`1",
    "T:System.Byte",
    "T:System.Char",
    "T:System.Collections.Generic.IAsyncEnumerable`1",
    "T:System.Collections.Generic.IEnumerable`1",
    "T:System.Collections.Generic.IEnumerator`1",
    "T:System.Collections.Generic.IList`1",
    "T:System.Collections.Generic.KeyValuePair`2",
    "T:System.DateTime"
  ],
  "attributelookup": [
    "T:System.CLSCompliantAttribute",
    "T:System.ComponentModel.EditorBrowsableAttribute",
    "T:System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute",
    "T:System.Diagnostics.CodeAnalysis.FeatureSwitchDefinitionAttribute",
    "T:System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute",
    "T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute",
    "T:System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute",
    "T:System.ObsoleteAttribute"
  ],
  "namelookup": [
    "Array",
    "Attribute",
    "Collection",
    "Comment",
    "Condition",
    "Constructor",
    "Context",
    "Converter",
    "Creation",
    "Data",
    "Default",
    "Defaults"
  ]
}
<shellId: 1 completed with exit code 0>
```

</details>


---

<sub>15s</sub>

### `bash`

**Find Stream-accepting public APIs**

`$ jq '{lookup:.parameter_type_members["T:System.IO.Stream"], matches:[.members[]|select(any(.parameters[]?; .type == "System.IO.Stream"))|{documentation_id,declaring_type,display_signature,accessibility}]}' System.Text.Json.symbols.json`

<details>
<summary>217 lines</summary>

```
{
  "lookup": [
    "M:System.Text.Json.JsonDocument.Parse(System.IO.Stream,System.Text.Json.JsonDocumentOptions)",
    "M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo)",
    "M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions)",
    "M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.Serialization.JsonSerializerContext)",
    "M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Type,System.Text.Json.Serialization.JsonSerializerContext,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Boolean,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Boolean,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.DeserializeAsync``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.DeserializeAsync``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions)",
    "M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})",
    "M:System.Text.Json.JsonSerializer.Serialize(System.IO.Stream,System.Object,System.Text.Json.Serialization.Metadata.JsonTypeInfo)",
    "M:System.Text.Json.JsonSerializer.Serialize(System.IO.Stream,System.Object,System.Type,System.Text.Json.JsonSerializerOptions)",
    "M:System.Text.Json.JsonSerializer.Serialize(System.IO.Stream,System.Object,System.Type,System.Text.Json.Serialization.JsonSerializerContext)",
    "M:System.Text.Json.JsonSerializer.SerializeAsync(System.IO.Stream,System.Object,System.Text.Json.Serialization.Metadata.JsonTypeInfo,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.SerializeAsync(System.IO.Stream,System.Object,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.SerializeAsync(System.IO.Stream,System.Object,System.Type,System.Text.Json.Serialization.JsonSerializerContext,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.SerializeAsync``1(System.IO.Stream,``0,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.SerializeAsync``1(System.IO.Stream,``0,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonSerializer.Serialize``1(System.IO.Stream,``0,System.Text.Json.JsonSerializerOptions)",
    "M:System.Text.Json.JsonSerializer.Serialize``1(System.IO.Stream,``0,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})",
    "M:System.Text.Json.Nodes.JsonNode.Parse(System.IO.Stream,System.Nullable{System.Text.Json.Nodes.JsonNodeOptions},System.Text.Json.JsonDocumentOptions)",
    "M:System.Text.Json.Nodes.JsonNode.ParseAsync(System.IO.Stream,System.Nullable{System.Text.Json.Nodes.JsonNodeOptions},System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)",
    "M:System.Text.Json.Utf8JsonWriter.#ctor(System.IO.Stream,System.Text.Json.JsonWriterOptions)",
    "M:System.Text.Json.Utf8JsonWriter.Reset(System.IO.Stream)"
  ],
  "matches": [
    {
      "documentation_id": "M:System.Text.Json.JsonDocument.Parse(System.IO.Stream,System.Text.Json.JsonDocumentOptions)",
      "declaring_type": "T:System.Text.Json.JsonDocument",
      "display_signature": "System.Text.Json.JsonDocument Parse(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonDocument",
      "display_signature": "System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ParseAsync(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.DeserializeAsync``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Threading.Tasks.ValueTask<TValue?> DeserializeAsync<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.DeserializeAsync``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Threading.Tasks.ValueTask<TValue?> DeserializeAsync<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "object? Deserialize(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Type,System.Text.Json.Serialization.JsonSerializerContext,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.Serialization.JsonSerializerContext)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Boolean,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, bool topLevelValues, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Boolean,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo, bool topLevelValues, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.SerializeAsync``1(System.IO.Stream,``0,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.Serialize``1(System.IO.Stream,``0,System.Text.Json.JsonSerializerOptions)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "void Serialize<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.JsonSerializerOptions? options = null)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.SerializeAsync(System.IO.Stream,System.Object,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.Serialize(System.IO.Stream,System.Object,System.Type,System.Text.Json.JsonSerializerOptions)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "void Serialize(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.SerializeAsync``1(System.IO.Stream,``0,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.Serialize``1(System.IO.Stream,``0,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "void Serialize<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.SerializeAsync(System.IO.Stream,System.Object,System.Text.Json.Serialization.Metadata.JsonTypeInfo,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.Serialize(System.IO.Stream,System.Object,System.Text.Json.Serialization.Metadata.JsonTypeInfo)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "void Serialize(System.IO.Stream utf8Json, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.SerializeAsync(System.IO.Stream,System.Object,System.Type,System.Text.Json.Serialization.JsonSerializerContext,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.Serialize(System.IO.Stream,System.Object,System.Type,System.Text.Json.Serialization.JsonSerializerContext)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "void Serialize(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.Nodes.JsonNode.Parse(System.IO.Stream,System.Nullable{System.Text.Json.Nodes.JsonNodeOptions},System.Text.Json.JsonDocumentOptions)",
      "declaring_type": "T:System.Text.Json.Nodes.JsonNode",
      "display_signature": "System.Text.Json.Nodes.JsonNode? Parse(System.IO.Stream utf8Json, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> nodeOptions = null, System.Text.Json.JsonDocumentOptions documentOptions = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.Nodes.JsonNode.ParseAsync(System.IO.Stream,System.Nullable{System.Text.Json.Nodes.JsonNodeOptions},System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.Nodes.JsonNode",
      "display_signature": "System.Threading.Tasks.Task<System.Text.Json.Nodes.JsonNode?> ParseAsync(System.IO.Stream utf8Json, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> nodeOptions = null, System.Text.Json.JsonDocumentOptions documentOptions = default, System.Threading.CancellationToken cancellationToken = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.Utf8JsonWriter.#ctor(System.IO.Stream,System.Text.Json.JsonWriterOptions)",
      "declaring_type": "T:System.Text.Json.Utf8JsonWriter",
      "display_signature": "Utf8JsonWriter(System.IO.Stream utf8Json, System.Text.Json.JsonWriterOptions options = default)",
      "accessibility": "public"
    },
    {
      "documentation_id": "M:System.Text.Json.Utf8JsonWriter.Reset(System.IO.Stream)",
      "declaring_type": "T:System.Text.Json.Utf8JsonWriter",
      "display_signature": "void Reset(System.IO.Stream utf8Json)",
      "accessibility": "public"
    }
  ]
}
<shellId: 2 completed with exit code 0>
```

</details>


---

<sub>15s</sub>

### `bash`

**Find JsonDocument-producing APIs**

`$ jq '{lookup:.produced_type_members["T:System.Text.Json.JsonDocument"], matches:[.members[]|select(.return_type|contains("System.Text.Json.JsonDocument"))|{documentation_id,declaring_type,display_signature,return_type,parameters}], outMatches:[.members[]|select(any(.parameters[]?; .modifier == "out" and (.type|contains("System.Text.Json.JsonDocument"))))|{documentation_id,declaring_type,display_signature}]}' System.Text.Json.symbols.json`

<details>
<summary>363 lines</summary>

```
{
  "lookup": [
    "M:System.Text.Json.JsonDocument.Parse(System.Buffers.ReadOnlySequence{System.Byte},System.Text.Json.JsonDocumentOptions)",
    "M:System.Text.Json.JsonDocument.Parse(System.IO.Stream,System.Text.Json.JsonDocumentOptions)",
    "M:System.Text.Json.JsonDocument.Parse(System.ReadOnlyMemory{System.Byte},System.Text.Json.JsonDocumentOptions)",
    "M:System.Text.Json.JsonDocument.Parse(System.ReadOnlyMemory{System.Char},System.Text.Json.JsonDocumentOptions)",
    "M:System.Text.Json.JsonDocument.Parse(System.String,System.Text.Json.JsonDocumentOptions)",
    "M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonDocument.ParseValue(System.Text.Json.Utf8JsonReader@)",
    "M:System.Text.Json.JsonDocument.TryParseValue(System.Text.Json.Utf8JsonReader@,System.Text.Json.JsonDocument@)",
    "M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Text.Json.Serialization.Metadata.JsonTypeInfo)",
    "M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)",
    "M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Type,System.Text.Json.Serialization.JsonSerializerContext)",
    "M:System.Text.Json.JsonSerializer.SerializeToDocument``1(``0,System.Text.Json.JsonSerializerOptions)",
    "M:System.Text.Json.JsonSerializer.SerializeToDocument``1(``0,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})"
  ],
  "matches": [
    {
      "documentation_id": "M:System.Text.Json.JsonDocument.Parse(System.ReadOnlyMemory{System.Byte},System.Text.Json.JsonDocumentOptions)",
      "declaring_type": "T:System.Text.Json.JsonDocument",
      "display_signature": "System.Text.Json.JsonDocument Parse(System.ReadOnlyMemory<byte> utf8Json, System.Text.Json.JsonDocumentOptions options = default)",
      "return_type": "System.Text.Json.JsonDocument",
      "parameters": [
        {
          "name": "utf8Json",
          "type": "System.ReadOnlyMemory<byte>",
          "type_mentions": [
            "T:System.Byte",
            "T:System.ReadOnlyMemory`1"
          ],
          "modifier": "",
          "has_default_value": false
        },
        {
          "name": "options",
          "type": "System.Text.Json.JsonDocumentOptions",
          "type_mentions": [
            "T:System.Text.Json.JsonDocumentOptions"
          ],
          "modifier": "",
          "has_default_value": true
        }
      ]
    },
    {
      "documentation_id": "M:System.Text.Json.JsonDocument.Parse(System.Buffers.ReadOnlySequence{System.Byte},System.Text.Json.JsonDocumentOptions)",
      "declaring_type": "T:System.Text.Json.JsonDocument",
      "display_signature": "System.Text.Json.JsonDocument Parse(System.Buffers.ReadOnlySequence<byte> utf8Json, System.Text.Json.JsonDocumentOptions options = default)",
      "return_type": "System.Text.Json.JsonDocument",
      "parameters": [
        {
          "name": "utf8Json",
          "type": "System.Buffers.ReadOnlySequence<byte>",
          "type_mentions": [
            "T:System.Buffers.ReadOnlySequence`1",
            "T:System.Byte"
          ],
          "modifier": "",
          "has_default_value": false
        },
        {
          "name": "options",
          "type": "System.Text.Json.JsonDocumentOptions",
          "type_mentions": [
            "T:System.Text.Json.JsonDocumentOptions"
          ],
          "modifier": "",
          "has_default_value": true
        }
      ]
    },
    {
      "documentation_id": "M:System.Text.Json.JsonDocument.Parse(System.IO.Stream,System.Text.Json.JsonDocumentOptions)",
      "declaring_type": "T:System.Text.Json.JsonDocument",
      "display_signature": "System.Text.Json.JsonDocument Parse(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default)",
      "return_type": "System.Text.Json.JsonDocument",
      "parameters": [
        {
          "name": "utf8Json",
          "type": "System.IO.Stream",
          "type_mentions": [
            "T:System.IO.Stream"
          ],
          "modifier": "",
          "has_default_value": false
        },
        {
          "name": "options",
          "type": "System.Text.Json.JsonDocumentOptions",
          "type_mentions": [
            "T:System.Text.Json.JsonDocumentOptions"
          ],
          "modifier": "",
          "has_default_value": true
        }
      ]
    },
    {
      "documentation_id": "M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)",
      "declaring_type": "T:System.Text.Json.JsonDocument",
      "display_signature": "System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ParseAsync(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default, System.Threading.CancellationToken cancellationToken = default)",
      "return_type": "System.Threading.Tasks.Task<System.Text.Json.JsonDocument>",
      "parameters": [
        {
          "name": "utf8Json",
          "type": "System.IO.Stream",
          "type_mentions": [
            "T:System.IO.Stream"
          ],
          "modifier": "",
          "has_default_value": false
        },
        {
          "name": "options",
          "type": "System.Text.Json.JsonDocumentOptions",
          "type_mentions": [
            "T:System.Text.Json.JsonDocumentOptions"
          ],
          "modifier": "",
          "has_default_value": true
        },
        {
          "name": "cancellationToken",
          "type": "System.Threading.CancellationToken",
          "type_mentions": [
            "T:System.Threading.CancellationToken"
          ],
          "modifier": "",
          "has_default_value": true
        }
      ]
    },
    {
      "documentation_id": "M:System.Text.Json.JsonDocument.Parse(System.ReadOnlyMemory{System.Char},System.Text.Json.JsonDocumentOptions)",
      "declaring_type": "T:System.Text.Json.JsonDocument",
      "display_signature": "System.Text.Json.JsonDocument Parse(System.ReadOnlyMemory<char> json, System.Text.Json.JsonDocumentOptions options = default)",
      "return_type": "System.Text.Json.JsonDocument",
      "parameters": [
        {
          "name": "json",
          "type": "System.ReadOnlyMemory<char>",
          "type_mentions": [
            "T:System.Char",
            "T:System.ReadOnlyMemory`1"
          ],
          "modifier": "",
          "has_default_value": false
        },
        {
          "name": "options",
          "type": "System.Text.Json.JsonDocumentOptions",
          "type_mentions": [
            "T:System.Text.Json.JsonDocumentOptions"
          ],
          "modifier": "",
          "has_default_value": true
        }
      ]
    },
    {
      "documentation_id": "M:System.Text.Json.JsonDocument.Parse(System.String,System.Text.Json.JsonDocumentOptions)",
      "declaring_type": "T:System.Text.Json.JsonDocument",
      "display_signature": "System.Text.Json.JsonDocument Parse(string json, System.Text.Json.JsonDocumentOptions options = default)",
      "return_type": "System.Text.Json.JsonDocument",
      "parameters": [
        {
          "name": "json",
          "type": "string",
          "type_mentions": [
            "T:System.String"
          ],
          "modifier": "",
          "has_default_value": false
        },
        {
          "name": "options",
          "type": "System.Text.Json.JsonDocumentOptions",
          "type_mentions": [
            "T:System.Text.Json.JsonDocumentOptions"
          ],
          "modifier": "",
          "has_default_value": true
        }
      ]
    },
    {
      "documentation_id": "M:System.Text.Json.JsonDocument.ParseValue(System.Text.Json.Utf8JsonReader@)",
      "declaring_type": "T:System.Text.Json.JsonDocument",
      "display_signature": "System.Text.Json.JsonDocument ParseValue(ref System.Text.Json.Utf8JsonReader reader)",
      "return_type": "System.Text.Json.JsonDocument",
      "parameters": [
        {
          "name": "reader",
          "type": "System.Text.Json.Utf8JsonReader",
          "type_mentions": [
            "T:System.Text.Json.Utf8JsonReader"
          ],
          "modifier": "ref",
          "has_default_value": false
        }
      ]
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.SerializeToDocument``1(``0,System.Text.Json.JsonSerializerOptions)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Text.Json.JsonDocument SerializeToDocument<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)",
      "return_type": "System.Text.Json.JsonDocument",
      "parameters": [
        {
          "name": "value",
          "type": "TValue",
          "type_mentions": [],
          "modifier": "",
          "has_default_value": false
        },
        {
          "name": "options",
          "type": "System.Text.Json.JsonSerializerOptions?",
          "type_mentions": [
            "T:System.Text.Json.JsonSerializerOptions"
          ],
          "modifier": "",
          "has_default_value": true
        }
      ]
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)",
      "return_type": "System.Text.Json.JsonDocument",
      "parameters": [
        {
          "name": "value",
          "type": "object?",
          "type_mentions": [
            "T:System.Object"
          ],
          "modifier": "",
          "has_default_value": false
        },
        {
          "name": "inputType",
          "type": "System.Type",
          "type_mentions": [
            "T:System.Type"
          ],
          "modifier": "",
          "has_default_value": false
        },
        {
          "name": "options",
          "type": "System.Text.Json.JsonSerializerOptions?",
          "type_mentions": [
            "T:System.Text.Json.JsonSerializerOptions"
          ],
          "modifier": "",
          "has_default_value": true
        }
      ]
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.SerializeToDocument``1(``0,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Text.Json.JsonDocument SerializeToDocument<TValue>(TValue value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo)",
      "return_type": "System.Text.Json.JsonDocument",
      "parameters": [
        {
          "name": "value",
          "type": "TValue",
          "type_mentions": [],
          "modifier": "",
          "has_default_value": false
        },
        {
          "name": "jsonTypeInfo",
          "type": "System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>",
          "type_mentions": [
            "T:System.Text.Json.Serialization.Metadata.JsonTypeInfo`1"
          ],
          "modifier": "",
          "has_default_value": false
        }
      ]
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Text.Json.Serialization.Metadata.JsonTypeInfo)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)",
      "return_type": "System.Text.Json.JsonDocument",
      "parameters": [
        {
          "name": "value",
          "type": "object?",
          "type_mentions": [
            "T:System.Object"
          ],
          "modifier": "",
          "has_default_value": false
        },
        {
          "name": "jsonTypeInfo",
          "type": "System.Text.Json.Serialization.Metadata.JsonTypeInfo",
          "type_mentions": [
            "T:System.Text.Json.Serialization.Metadata.JsonTypeInfo"
          ],
          "modifier": "",
          "has_default_value": false
        }
      ]
    },
    {
      "documentation_id": "M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Type,System.Text.Json.Serialization.JsonSerializerContext)",
      "declaring_type": "T:System.Text.Json.JsonSerializer",
      "display_signature": "System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)",
      "return_type": "System.Text.Json.JsonDocument",
      "parameters": [
        {
          "name": "value",
          "type": "object?",
          "type_mentions": [
            "T:System.Object"
          ],
          "modifier": "",
          "has_default_value": false
        },
        {
          "name": "inputType",
          "type": "System.Type",
          "type_mentions": [
            "T:System.Type"
          ],
          "modifier": "",
          "has_default_value": false
        },
        {
          "name": "context",
          "type": "System.Text.Json.Serialization.JsonSerializerContext",
          "type_mentions": [
            "T:System.Text.Json.Serialization.JsonSerializerContext"
          ],
          "modifier": "",
          "has_default_value": false
        }
      ]
    },
    {
      "documentation_id": "P:System.Text.Json.Serialization.Metadata.JsonMetadataServices.JsonDocumentConverter",
      "declaring_type": "T:System.Text.Json.Serialization.Metadata.JsonMetadataServices",
      "display_signature": "System.Text.Json.Serialization.JsonConverter<System.Text.Json.JsonDocument?> JsonDocumentConverter { get; }",
      "return_type": "System.Text.Json.Serialization.JsonConverter<System.Text.Json.JsonDocument?>",
      "parameters": []
    }
  ],
  "outMatches": [
    {
      "documentation_id": "M:System.Text.Json.JsonDocument.TryParseValue(System.Text.Json.Utf8JsonReader@,System.Text.Json.JsonDocument@)",
      "declaring_type": "T:System.Text.Json.JsonDocument",
      "display_signature": "bool TryParseValue(ref System.Text.Json.Utf8JsonReader reader, out System.Text.Json.JsonDocument? document)"
    }
  ]
}
<shellId: 3 completed with exit code 0>
```

</details>


---

<sub>15s</sub>

### `bash`

**Find trimming annotated APIs**

`$ jq '{lookup:.attribute_type_members["T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"], matches:[.members[]|select(.attribute_types|index("T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"))|{documentation_id,declaring_type,name,display_signature,attribute_types}]}' System.Text.Json.symbols.json`

<details>
<summary>10 lines</summary>

```
Output too large to read at once (38.9 KB). Saved to: /var/folders/09/c3s_t1kd4m91p7yltrx08snw0000gn/T/1794005863375-copilot-tool-output-1702-608b1595-1bf2-43b8-bc1d-a8f86dfd1652.txt
Consider using tools like rg (for searching), head/tail (for viewing start/end), view with view_range (for specific sections), or jq (for JSON) to examine portions of the output.

Preview (first 500 chars):
{
  "lookup": [
    "M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions)",
    "M:System.Text.Json.JsonSerializer.Deserialize(System.ReadOnlySpan{System.Byte},System.Type,System.Text.Json.JsonSerializerOptions)",
    "M:System.Text.Json.JsonSerializer.Deserialize(System.ReadOnlySpan{System.Char},System.Type,System.Text.Json.JsonSerializerOptions)",
    "M:System.Text.Json.JsonSerializer.Deserialize(System.String,System.Type,System.Te
<shellId: 4 completed with exit code 0>
```

</details>


---

<sub>15s</sub>

### `bash`

**Read ParseAsync docs and converter types**

`$ jq '{parseAsync:[.members[]|select(.declaring_type=="T:System.Text.Json.JsonDocument" and .name=="ParseAsync")|{documentation_id,display_signature,documentation,source}], converters:[.types|to_entries[]|select(.value.name|contains("Converter"))|{documentation_id:.key,name:.value.name,accessibility:.value.accessibility,is_abstract:.value.is_abstract,is_sealed:.value.is_sealed,constructor_accessibility:.value.constructor_accessibility,base_type:.value.base_type,required_overrides:.value.required_overrides,inaccessible_required_overrides:.value.inaccessible_required_overrides}]}' System.Text.Json.symbols.json`

<details>
<summary>119 lines</summary>

```
{
  "parseAsync": [
    {
      "documentation_id": "M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)",
      "display_signature": "System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ParseAsync(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default, System.Threading.CancellationToken cancellationToken = default)",
      "documentation": {
        "status": "complete",
        "summary": "Parses a Stream as UTF-8-encoded data representing a single JSON value into a JsonDocument. The stream is read to completion."
      },
      "source": {
        "status": "complete",
        "url": "https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Document/JsonDocument.Parse.cs",
        "line": 213
      }
    }
  ],
  "converters": [
    {
      "documentation_id": "T:System.Text.Json.Serialization.JsonConverter",
      "name": "JsonConverter",
      "accessibility": "public",
      "is_abstract": true,
      "is_sealed": false,
      "constructor_accessibility": "internal",
      "base_type": "T:System.Object",
      "required_overrides": [
        "M:System.Text.Json.Serialization.JsonConverter.CanConvert(System.Type)",
        "M:System.Text.Json.Serialization.JsonConverter.GetDefaultConverterStrategy",
        "M:System.Text.Json.Serialization.JsonConverter.OnTryReadAsObject(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions,System.Text.Json.ReadStack@,System.Object@)",
        "M:System.Text.Json.Serialization.JsonConverter.OnTryWriteAsObject(System.Text.Json.Utf8JsonWriter,System.Object,System.Text.Json.JsonSerializerOptions,System.Text.Json.WriteStack@)",
        "M:System.Text.Json.Serialization.JsonConverter.ReadAsObject(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions)",
        "M:System.Text.Json.Serialization.JsonConverter.ReadAsPropertyNameAsObject(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions)",
        "M:System.Text.Json.Serialization.JsonConverter.ReadAsPropertyNameCoreAsObject(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions)",
        "M:System.Text.Json.Serialization.JsonConverter.ReadNumberWithCustomHandlingAsObject(System.Text.Json.Utf8JsonReader@,System.Text.Json.Serialization.JsonNumberHandling,System.Text.Json.JsonSerializerOptions)",
        "M:System.Text.Json.Serialization.JsonConverter.TryReadAsObject(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions,System.Text.Json.ReadStack@,System.Object@)",
        "M:System.Text.Json.Serialization.JsonConverter.TryWriteAsObject(System.Text.Json.Utf8JsonWriter,System.Object,System.Text.Json.JsonSerializerOptions,System.Text.Json.WriteStack@)",
        "M:System.Text.Json.Serialization.JsonConverter.WriteAsObject(System.Text.Json.Utf8JsonWriter,System.Object,System.Text.Json.JsonSerializerOptions)",
        "M:System.Text.Json.Serialization.JsonConverter.WriteAsPropertyNameAsObject(System.Text.Json.Utf8JsonWriter,System.Object,System.Text.Json.JsonSerializerOptions)",
        "M:System.Text.Json.Serialization.JsonConverter.WriteAsPropertyNameCoreAsObject(System.Text.Json.Utf8JsonWriter,System.Object,System.Text.Json.JsonSerializerOptions,System.Boolean)",
        "M:System.Text.Json.Serialization.JsonConverter.WriteNumberWithCustomHandlingAsObject(System.Text.Json.Utf8JsonWriter,System.Object,System.Text.Json.Serialization.JsonNumberHandling)",
        "P:System.Text.Json.Serialization.JsonConverter.Type"
      ],
      "inaccessible_required_overrides": 13
    },
    {
      "documentation_id": "T:System.Text.Json.Serialization.JsonConverterAttribute",
      "name": "JsonConverterAttribute",
      "accessibility": "public",
      "is_abstract": false,
      "is_sealed": false,
      "constructor_accessibility": "public",
      "base_type": "T:System.Text.Json.Serialization.JsonAttribute",
      "required_overrides": [],
      "inaccessible_required_overrides": 0
    },
    {
      "documentation_id": "T:System.Text.Json.Serialization.JsonConverterFactory",
      "name": "JsonConverterFactory",
      "accessibility": "public",
      "is_abstract": true,
      "is_sealed": false,
      "constructor_accessibility": "protected",
      "base_type": "T:System.Text.Json.Serialization.JsonConverter",
      "required_overrides": [
        "M:System.Text.Json.Serialization.JsonConverter.CanConvert(System.Type)",
        "M:System.Text.Json.Serialization.JsonConverterFactory.CreateConverter(System.Type,System.Text.Json.JsonSerializerOptions)"
      ],
      "inaccessible_required_overrides": 0
    },
    {
      "documentation_id": "T:System.Text.Json.Serialization.JsonConverter`1",
      "name": "JsonConverter<T>",
      "accessibility": "public",
      "is_abstract": true,
      "is_sealed": false,
      "constructor_accessibility": "protected_internal",
      "base_type": "T:System.Text.Json.Serialization.JsonConverter",
      "required_overrides": [
        "M:System.Text.Json.Serialization.JsonConverter`1.Read(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions)",
        "M:System.Text.Json.Serialization.JsonConverter`1.Write(System.Text.Json.Utf8JsonWriter,`0,System.Text.Json.JsonSerializerOptions)"
      ],
      "inaccessible_required_overrides": 0
    },
    {
      "documentation_id": "T:System.Text.Json.Serialization.JsonNumberEnumConverter`1",
      "name": "JsonNumberEnumConverter<TEnum>",
      "accessibility": "public",
      "is_abstract": false,
      "is_sealed": true,
      "constructor_accessibility": "public",
      "base_type": "T:System.Text.Json.Serialization.JsonConverterFactory",
      "required_overrides": [],
      "inaccessible_required_overrides": 0
    },
    {
      "documentation_id": "T:System.Text.Json.Serialization.JsonStringEnumConverter",
      "name": "JsonStringEnumConverter",
      "accessibility": "public",
      "is_abstract": false,
      "is_sealed": false,
      "constructor_accessibility": "public",
      "base_type": "T:System.Text.Json.Serialization.JsonConverterFactory",
      "required_overrides": [],
      "inaccessible_required_overrides": 0
    },
    {
      "documentation_id": "T:System.Text.Json.Serialization.JsonStringEnumConverter`1",
      "name": "JsonStringEnumConverter<TEnum>",
      "accessibility": "public",
      "is_abstract": false,
      "is_sealed": false,
      "constructor_accessibility": "public",
      "base_type": "T:System.Text.Json.Serialization.JsonConverterFactory",
      "required_overrides": [],
      "inaccessible_required_overrides": 0
    }
  ]
}
<shellId: 5 completed with exit code 0>
```

</details>


---

<sub>15s</sub>

### `bash`

**Locate Stream Deserialize overloads**

`$ jq '[.members[]|select(.declaring_type=="T:System.Text.Json.JsonSerializer" and .name=="Deserialize" and any(.parameters[]?;.type=="System.IO.Stream"))|{documentation_id,display_signature,source}]' System.Text.Json.symbols.json`

<details>
<summary>48 lines</summary>

```
[
  {
    "documentation_id": "M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions)",
    "display_signature": "TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null)",
    "source": {
      "status": "complete",
      "url": "https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs",
      "line": 80
    }
  },
  {
    "documentation_id": "M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions)",
    "display_signature": "object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)",
    "source": {
      "status": "complete",
      "url": "https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs",
      "line": 151
    }
  },
  {
    "documentation_id": "M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})",
    "display_signature": "TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo)",
    "source": {
      "status": "complete",
      "url": "https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs",
      "line": 238
    }
  },
  {
    "documentation_id": "M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo)",
    "display_signature": "object? Deserialize(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)",
    "source": {
      "status": "complete",
      "url": "https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs",
      "line": 263
    }
  },
  {
    "documentation_id": "M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.Serialization.JsonSerializerContext)",
    "display_signature": "object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)",
    "source": {
      "status": "complete",
      "url": "https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs",
      "line": 340
    }
  }
]
<shellId: 6 completed with exit code 0>
```

</details>


---

<sub>18s</sub>

### `bash`

**Group RequiresUnreferencedCode members and check overloads**

`$ jq -r '. as $d | [.members[] | select(.attribute_types | index("T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"))] | group_by([.declaring_type,.name])[] | .[0] as $m | "\($m.declaring_type) :: \($m.name) annotated=\(length) unmarked_same_name=\([$d.members[] | select(.declaring_type == $m.declaring_type and .name == $m.name and (.attribute_types | index("T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute") | not))] | length)\n" + (map("  " + .documentation_id) | join("\n"))' System.Text.Json.symbols.json`

<details>
<summary>75 lines</summary>

```
T:System.Text.Json.JsonSerializer :: Deserialize annotated=16 unmarked_same_name=24
  M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonDocument,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonDocument,System.Type,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonElement,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonElement,System.Type,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.Nodes.JsonNode,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.Nodes.JsonNode,System.Type,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize``1(System.ReadOnlySpan{System.Byte},System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize(System.ReadOnlySpan{System.Byte},System.Type,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize``1(System.String,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize``1(System.ReadOnlySpan{System.Char},System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize(System.String,System.Type,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize(System.ReadOnlySpan{System.Char},System.Type,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.Utf8JsonReader@,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer :: DeserializeAsync annotated=4 unmarked_same_name=6
  M:System.Text.Json.JsonSerializer.DeserializeAsync``1(System.IO.Pipelines.PipeReader,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
  M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Pipelines.PipeReader,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
  M:System.Text.Json.JsonSerializer.DeserializeAsync``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
  M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer :: DeserializeAsyncEnumerable annotated=4 unmarked_same_name=4
  M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Pipelines.PipeReader,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
  M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Pipelines.PipeReader,System.Boolean,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
  M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
  M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Boolean,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer :: Serialize annotated=6 unmarked_same_name=9
  M:System.Text.Json.JsonSerializer.Serialize``1(System.IO.Stream,``0,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Serialize(System.IO.Stream,System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Serialize``1(``0,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Serialize(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Serialize``1(System.Text.Json.Utf8JsonWriter,``0,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.Serialize(System.Text.Json.Utf8JsonWriter,System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer :: SerializeAsync annotated=4 unmarked_same_name=6
  M:System.Text.Json.JsonSerializer.SerializeAsync``1(System.IO.Stream,``0,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
  M:System.Text.Json.JsonSerializer.SerializeAsync(System.IO.Stream,System.Object,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
  M:System.Text.Json.JsonSerializer.SerializeAsync``1(System.IO.Pipelines.PipeWriter,``0,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
  M:System.Text.Json.JsonSerializer.SerializeAsync(System.IO.Pipelines.PipeWriter,System.Object,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer :: SerializeToDocument annotated=2 unmarked_same_name=3
  M:System.Text.Json.JsonSerializer.SerializeToDocument``1(``0,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer :: SerializeToElement annotated=2 unmarked_same_name=3
  M:System.Text.Json.JsonSerializer.SerializeToElement``1(``0,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.SerializeToElement(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer :: SerializeToNode annotated=2 unmarked_same_name=3
  M:System.Text.Json.JsonSerializer.SerializeToNode``1(``0,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.SerializeToNode(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer :: SerializeToUtf8Bytes annotated=2 unmarked_same_name=3
  M:System.Text.Json.JsonSerializer.SerializeToUtf8Bytes``1(``0,System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializerOptions :: Default annotated=1 unmarked_same_name=0
  P:System.Text.Json.JsonSerializerOptions.Default
T:System.Text.Json.JsonSerializerOptions :: GetConverter annotated=1 unmarked_same_name=0
  M:System.Text.Json.JsonSerializerOptions.GetConverter(System.Type)
T:System.Text.Json.JsonSerializerOptions :: MakeReadOnly annotated=1 unmarked_same_name=1
  M:System.Text.Json.JsonSerializerOptions.MakeReadOnly(System.Boolean)
T:System.Text.Json.JsonSerializerOptions :: Strict annotated=1 unmarked_same_name=0
  P:System.Text.Json.JsonSerializerOptions.Strict
T:System.Text.Json.JsonSerializerOptions :: Web annotated=1 unmarked_same_name=0
  P:System.Text.Json.JsonSerializerOptions.Web
T:System.Text.Json.Nodes.JsonArray :: Add annotated=1 unmarked_same_name=1
  M:System.Text.Json.Nodes.JsonArray.Add``1(``0)
T:System.Text.Json.Nodes.JsonNode :: ReplaceWith annotated=1 unmarked_same_name=0
  M:System.Text.Json.Nodes.JsonNode.ReplaceWith``1(``0)
T:System.Text.Json.Nodes.JsonValue :: Create annotated=1 unmarked_same_name=36
  M:System.Text.Json.Nodes.JsonValue.Create``1(``0,System.Nullable{System.Text.Json.Nodes.JsonNodeOptions})
T:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: #ctor annotated=1 unmarked_same_name=0
  M:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver.#ctor
T:System.Text.Json.Serialization.Metadata.JsonTypeInfo :: CreateJsonPropertyInfo annotated=1 unmarked_same_name=0
  M:System.Text.Json.Serialization.Metadata.JsonTypeInfo.CreateJsonPropertyInfo(System.Type,System.String)
T:System.Text.Json.Serialization.Metadata.JsonTypeInfo :: CreateJsonTypeInfo annotated=2 unmarked_same_name=0
  M:System.Text.Json.Serialization.Metadata.JsonTypeInfo.CreateJsonTypeInfo``1(System.Text.Json.JsonSerializerOptions)
  M:System.Text.Json.Serialization.Metadata.JsonTypeInfo.CreateJsonTypeInfo(System.Type,System.Text.Json.JsonSerializerOptions)
<shellId: 7 completed with exit code 0>
```

</details>


---

<sub>29s</sub>

### `bash`

**Confirm converter requirements and type annotations**

``$ jq '{typeAnnotated:[.types|to_entries[]|select(.value.attribute_types|index("T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"))|.key], converterTypes:.name_word_types.Converter, converterMembers:[.members[]|select((.declaring_type=="T:System.Text.Json.Serialization.JsonConverterFactory" or .declaring_type=="T:System.Text.Json.Serialization.JsonConverter`1" or .declaring_type=="T:System.Text.Json.Serialization.JsonConverterAttribute") and (.name=="CanConvert" or .name=="CreateConverter" or .name=="Read" or .name=="Write" or .name=="#ctor"))|{documentation_id,display_signature,accessibility,is_abstract,is_virtual}]}' System.Text.Json.symbols.json``

<details>
<summary>78 lines</summary>

```
{
  "typeAnnotated": [],
  "converterTypes": [
    "T:System.Text.Json.Serialization.JsonConverter",
    "T:System.Text.Json.Serialization.JsonConverterAttribute",
    "T:System.Text.Json.Serialization.JsonConverterFactory",
    "T:System.Text.Json.Serialization.JsonConverter`1",
    "T:System.Text.Json.Serialization.JsonNumberEnumConverter`1",
    "T:System.Text.Json.Serialization.JsonStringEnumConverter",
    "T:System.Text.Json.Serialization.JsonStringEnumConverter`1"
  ],
  "converterMembers": [
    {
      "documentation_id": "M:System.Text.Json.Serialization.JsonConverterAttribute.#ctor(System.Type)",
      "display_signature": "JsonConverterAttribute(System.Type converterType)",
      "accessibility": "public",
      "is_abstract": false,
      "is_virtual": false
    },
    {
      "documentation_id": "M:System.Text.Json.Serialization.JsonConverterAttribute.#ctor",
      "display_signature": "JsonConverterAttribute()",
      "accessibility": "protected",
      "is_abstract": false,
      "is_virtual": false
    },
    {
      "documentation_id": "M:System.Text.Json.Serialization.JsonConverterAttribute.CreateConverter(System.Type)",
      "display_signature": "System.Text.Json.Serialization.JsonConverter? CreateConverter(System.Type typeToConvert)",
      "accessibility": "public",
      "is_abstract": false,
      "is_virtual": true
    },
    {
      "documentation_id": "M:System.Text.Json.Serialization.JsonConverterFactory.#ctor",
      "display_signature": "JsonConverterFactory()",
      "accessibility": "protected",
      "is_abstract": false,
      "is_virtual": false
    },
    {
      "documentation_id": "M:System.Text.Json.Serialization.JsonConverterFactory.CreateConverter(System.Type,System.Text.Json.JsonSerializerOptions)",
      "display_signature": "System.Text.Json.Serialization.JsonConverter? CreateConverter(System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)",
      "accessibility": "public",
      "is_abstract": true,
      "is_virtual": false
    },
    {
      "documentation_id": "M:System.Text.Json.Serialization.JsonConverter`1.#ctor",
      "display_signature": "JsonConverter()",
      "accessibility": "protected_internal",
      "is_abstract": false,
      "is_virtual": false
    },
    {
      "documentation_id": "M:System.Text.Json.Serialization.JsonConverter`1.CanConvert(System.Type)",
      "display_signature": "bool CanConvert(System.Type typeToConvert)",
      "accessibility": "public",
      "is_abstract": false,
      "is_virtual": false
    },
    {
      "documentation_id": "M:System.Text.Json.Serialization.JsonConverter`1.Read(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions)",
      "display_signature": "T? Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)",
      "accessibility": "public",
      "is_abstract": true,
      "is_virtual": false
    },
    {
      "documentation_id": "M:System.Text.Json.Serialization.JsonConverter`1.Write(System.Text.Json.Utf8JsonWriter,`0,System.Text.Json.JsonSerializerOptions)",
      "display_signature": "void Write(System.Text.Json.Utf8JsonWriter writer, T value, System.Text.Json.JsonSerializerOptions options)",
      "accessibility": "public",
      "is_abstract": true,
      "is_virtual": false
    }
  ]
}
<shellId: 8 completed with exit code 0>
```

</details>


---

<sub>29s</sub>

### `bash`

**Check annotation totals and document return types**

`$ jq -r '. as $d | [.members[] | select(.attribute_types | index("T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"))] | length as $n | "annotated count: \($n)", "RUC API kind counts:", ([ $d.members[] | select(.attribute_types | index("T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute")) | .kind ]|group_by(.)|map({kind:.[0],count:length})|tostring)' System.Text.Json.symbols.json; jq '[.members[] | select(.return_type|test("(^|[<])System\\.Text\\.Json\\.JsonDocument[?>]?$")) | .documentation_id] | length' System.Text.Json.symbols.json`

```
annotated count: 54
RUC API kind counts:
[{"kind":"constructor","count":1},{"kind":"method","count":50},{"kind":"property","count":3}]
12
<shellId: 9 completed with exit code 0>
```


---

<sub>29s</sub>

### `bash`

**Verify source lines for Stream deserialization**

`$ curl -fsSL 'https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs' -o JsonSerializer.Read.Stream.cs && nl -ba JsonSerializer.Read.Stream.cs | sed -n '76,84p;147,155p;234,242p;259,268p;336,344p'`

<details>
<summary>47 lines</summary>

```
76	        public static TValue? Deserialize<TValue>(
    77	            Stream utf8Json,
    78	            JsonSerializerOptions? options = null)
    79	        {
    80	            ArgumentNullException.ThrowIfNull(utf8Json);
    81	
    82	            JsonTypeInfo<TValue> jsonTypeInfo = GetTypeInfo<TValue>(options);
    83	            return jsonTypeInfo.Deserialize(utf8Json);
    84	        }
   147	            Stream utf8Json,
   148	            Type returnType,
   149	            JsonSerializerOptions? options = null)
   150	        {
   151	            ArgumentNullException.ThrowIfNull(utf8Json);
   152	            ArgumentNullException.ThrowIfNull(returnType);
   153	
   154	            JsonTypeInfo jsonTypeInfo = GetTypeInfo(options, returnType);
   155	            return jsonTypeInfo.DeserializeAsObject(utf8Json);
   234	        public static TValue? Deserialize<TValue>(
   235	            Stream utf8Json,
   236	            JsonTypeInfo<TValue> jsonTypeInfo)
   237	        {
   238	            ArgumentNullException.ThrowIfNull(utf8Json);
   239	            ArgumentNullException.ThrowIfNull(jsonTypeInfo);
   240	
   241	            jsonTypeInfo.EnsureConfigured();
   242	            return jsonTypeInfo.Deserialize(utf8Json);
   259	        public static object? Deserialize(
   260	            Stream utf8Json,
   261	            JsonTypeInfo jsonTypeInfo)
   262	        {
   263	            ArgumentNullException.ThrowIfNull(utf8Json);
   264	            ArgumentNullException.ThrowIfNull(jsonTypeInfo);
   265	
   266	            jsonTypeInfo.EnsureConfigured();
   267	            return jsonTypeInfo.DeserializeAsObject(utf8Json);
   268	        }
   336	            Stream utf8Json,
   337	            Type returnType,
   338	            JsonSerializerContext context)
   339	        {
   340	            ArgumentNullException.ThrowIfNull(utf8Json);
   341	            ArgumentNullException.ThrowIfNull(returnType);
   342	            ArgumentNullException.ThrowIfNull(context);
   343	
   344	            JsonTypeInfo jsonTypeInfo = GetTypeInfo(context, returnType);
<shellId: 10 completed with exit code 0>
```

</details>


---

<sub>33s</sub>

### `bash`

**Identify source declaration lines and factory overrides**

`$ nl -ba JsonSerializer.Read.Stream.cs | sed -n '62,81p;135,153p;222,239p;249,264p;323,342p'; jq -r '.types["T:System.Text.Json.Serialization.JsonConverterFactory"].required_overrides[]' System.Text.Json.symbols.json`

<details>
<summary>96 lines</summary>

```
62	        /// <exception cref="System.ArgumentNullException">
    63	        /// <paramref name="utf8Json"/> is <see langword="null"/>.
    64	        /// </exception>
    65	        /// <exception cref="JsonException">
    66	        /// The JSON is invalid,
    67	        /// <typeparamref name="TValue"/> is not compatible with the JSON,
    68	        /// or when there is remaining data in the Stream.
    69	        /// </exception>
    70	        /// <exception cref="NotSupportedException">
    71	        /// There is no compatible <see cref="System.Text.Json.Serialization.JsonConverter"/>
    72	        /// for <typeparamref name="TValue"/> or its serializable members.
    73	        /// </exception>
    74	        [RequiresUnreferencedCode(SerializationUnreferencedCodeMessage)]
    75	        [RequiresDynamicCode(SerializationRequiresDynamicCodeMessage)]
    76	        public static TValue? Deserialize<TValue>(
    77	            Stream utf8Json,
    78	            JsonSerializerOptions? options = null)
    79	        {
    80	            ArgumentNullException.ThrowIfNull(utf8Json);
    81	
   135	        /// <exception cref="JsonException">
   136	        /// The JSON is invalid,
   137	        /// the <paramref name="returnType"/> is not compatible with the JSON,
   138	        /// or when there is remaining data in the Stream.
   139	        /// </exception>
   140	        /// <exception cref="NotSupportedException">
   141	        /// There is no compatible <see cref="System.Text.Json.Serialization.JsonConverter"/>
   142	        /// for <paramref name="returnType"/> or its serializable members.
   143	        /// </exception>
   144	        [RequiresUnreferencedCode(SerializationUnreferencedCodeMessage)]
   145	        [RequiresDynamicCode(SerializationRequiresDynamicCodeMessage)]
   146	        public static object? Deserialize(
   147	            Stream utf8Json,
   148	            Type returnType,
   149	            JsonSerializerOptions? options = null)
   150	        {
   151	            ArgumentNullException.ThrowIfNull(utf8Json);
   152	            ArgumentNullException.ThrowIfNull(returnType);
   153	
   222	        /// <typeparam name="TValue">The type to deserialize the JSON value into.</typeparam>
   223	        /// <returns>A <typeparamref name="TValue"/> representation of the JSON value.</returns>
   224	        /// <param name="utf8Json">JSON data to parse.</param>
   225	        /// <param name="jsonTypeInfo">Metadata about the type to convert.</param>
   226	        /// <exception cref="System.ArgumentNullException">
   227	        /// <paramref name="utf8Json"/> or <paramref name="jsonTypeInfo"/> is <see langword="null"/>.
   228	        /// </exception>
   229	        /// <exception cref="JsonException">
   230	        /// The JSON is invalid,
   231	        /// <typeparamref name="TValue"/> is not compatible with the JSON,
   232	        /// or when there is remaining data in the Stream.
   233	        /// </exception>
   234	        public static TValue? Deserialize<TValue>(
   235	            Stream utf8Json,
   236	            JsonTypeInfo<TValue> jsonTypeInfo)
   237	        {
   238	            ArgumentNullException.ThrowIfNull(utf8Json);
   239	            ArgumentNullException.ThrowIfNull(jsonTypeInfo);
   249	        /// <returns>A <paramref name="jsonTypeInfo"/> representation of the JSON value.</returns>
   250	        /// <param name="utf8Json">JSON data to parse.</param>
   251	        /// <param name="jsonTypeInfo">Metadata about the type to convert.</param>
   252	        /// <exception cref="System.ArgumentNullException">
   253	        /// <paramref name="utf8Json"/> or <paramref name="jsonTypeInfo"/> is <see langword="null"/>.
   254	        /// </exception>
   255	        /// <exception cref="JsonException">
   256	        /// The JSON is invalid,
   257	        /// or when there is remaining data in the Stream.
   258	        /// </exception>
   259	        public static object? Deserialize(
   260	            Stream utf8Json,
   261	            JsonTypeInfo jsonTypeInfo)
   262	        {
   263	            ArgumentNullException.ThrowIfNull(utf8Json);
   264	            ArgumentNullException.ThrowIfNull(jsonTypeInfo);
   323	        /// The JSON is invalid,
   324	        /// the <paramref name="returnType"/> is not compatible with the JSON,
   325	        /// or when there is remaining data in the Stream.
   326	        /// </exception>
   327	        /// <exception cref="NotSupportedException">
   328	        /// There is no compatible <see cref="System.Text.Json.Serialization.JsonConverter"/>
   329	        /// for <paramref name="returnType"/> or its serializable members.
   330	        /// </exception>
   331	        /// <exception cref="InvalidOperationException">
   332	        /// The <see cref="JsonSerializerContext.GetTypeInfo(Type)"/> method on the provided <paramref name="context"/>
   333	        /// did not return a compatible <see cref="JsonTypeInfo"/> for <paramref name="returnType"/>.
   334	        /// </exception>
   335	        public static object? Deserialize(
   336	            Stream utf8Json,
   337	            Type returnType,
   338	            JsonSerializerContext context)
   339	        {
   340	            ArgumentNullException.ThrowIfNull(utf8Json);
   341	            ArgumentNullException.ThrowIfNull(returnType);
   342	            ArgumentNullException.ThrowIfNull(context);
M:System.Text.Json.Serialization.JsonConverter.CanConvert(System.Type)
M:System.Text.Json.Serialization.JsonConverterFactory.CreateConverter(System.Type,System.Text.Json.JsonSerializerOptions)
<shellId: 11 completed with exit code 0>
```

</details>


---

<sub>58s</sub>

### Copilot

The symbol document covers the API questions. I also checked the source file for question 6: its recorded source lines point to method bodies, so I’ll cite the declaration lines instead.

---

<sub>58s</sub>

### `apply_patch`

<details>
<summary>Arguments</summary>

```json
"*** Begin Patch\n*** Add File: make_answer.py\n+import json\n+from collections import defaultdict\n+from pathlib import Path\n+\n+\n+document = json.loads(Path(\"System.Text.Json.symbols.json\").read_text())\n+members = document[\"members\"]\n+types = document[\"types\"]\n+by_id = {member[\"documentation_id\"]: member for member in members}\n+trimming_attribute = \"T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute\"\n+document_type = \"System.Text.Json.JsonDocument\"\n+\n+\n+def signature(member):\n+    modifiers = [member[\"accessibility\"].replace(\"_\", \" \")]\n+    if member[\"is_static\"]:\n+        modifiers.append(\"static\")\n+    return \" \".join(modifiers) + \" \" + member[\"display_signature\"]\n+\n+\n+stream_members = [\n+    member\n+    for member in members\n+    if any(param[\"type\"] == \"System.IO.Stream\" for param in member[\"parameters\"])\n+]\n+assert len(stream_members) == len(document[\"parameter_type_members\"][\"T:System.IO.Stream\"]) == 30\n+\n+document_returns = {\n+    document_type,\n+    f\"System.Threading.Tasks.Task<{document_type}>\",\n+    f\"System.Threading.Tasks.ValueTask<{document_type}>\",\n+}\n+document_members = [\n+    member\n+    for member in members\n+    if member[\"return_type\"] in document_returns\n+    or any(\n+        param[\"modifier\"] == \"out\" and param[\"type\"].rstrip(\"?\") == document_type\n+        for param in member[\"parameters\"]\n+    )\n+]\n+assert len(document_members) == 13\n+assert {member[\"documentation_id\"] for member in document_members} == set(\n+    document[\"produced_type_members\"][\"T:System.Text.Json.JsonDocument\"]\n+)\n+\n+marked_groups = defaultdict(list)\n+unmarked_names = set()\n+for member in members:\n+    key = (member[\"declaring_type\"], member[\"name\"])\n+    if trimming_attribute in member[\"attribute_types\"]:\n+        marked_groups[key].append(member)\n+    else:\n+        unmarked_names.add(key)\n+assert sum(map(len, marked_groups.values())) == 54\n+assert not [\n+    name for name, info in types.items() if trimming_attribute in info[\"attribute_types\"]\n+]\n+\n+parse_async = [\n+    member\n+    for member in members\n+    if member[\"declaring_type\"] == \"T:System.Text.Json.JsonDocument\"\n+    and member[\"name\"] == \"ParseAsync\"\n+]\n+assert len(parse_async) == 1\n+parse_async = parse_async[0]\n+\n+converter_ids = document[\"name_word_types\"][\"Converter\"]\n+assert len(converter_ids) == 7\n+converter_types = []\n+for type_id in converter_ids:\n+    info = types[type_id]\n+    derivable = (\n+        not info[\"is_sealed\"]\n+        and info[\"constructor_accessibility\"]\n+        in {\"public\", \"protected\", \"protected_internal\"}\n+        and not info[\"inaccessible_required_overrides\"]\n+    )\n+    converter_types.append(\n+        {\n+            \"type\": type_id.removeprefix(\"T:\"),\n+            \"derivable_outside_library\": derivable,\n+            \"required_overrides\": [\n+                signature(by_id[member_id])\n+                for member_id in info[\"required_overrides\"]\n+            ]\n+            if derivable\n+            else None,\n+            **(\n+                {\n+                    \"reason\": \"Internal constructor and inaccessible abstract members\"\n+                    if info[\"constructor_accessibility\"] == \"internal\"\n+                    else \"Sealed type\"\n+                }\n+                if not derivable\n+                else {}\n+            ),\n+        }\n+    )\n+\n+declaration_lines = {\n+    \"M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions)\": 76,\n+    \"M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions)\": 146,\n+    \"M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})\": 234,\n+    \"M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo)\": 259,\n+    \"M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.Serialization.JsonSerializerContext)\": 335,\n+}\n+deserialize_stream = [\n+    member\n+    for member in stream_members\n+    if member[\"declaring_type\"] == \"T:System.Text.Json.JsonSerializer\"\n+    and member[\"name\"] == \"Deserialize\"\n+]\n+assert {member[\"documentation_id\"] for member in deserialize_stream} == set(\n+    declaration_lines\n+)\n+\n+answers = [\n+    {\n+        \"question\": 1,\n+        \"answer\": [\n+            {\n+                \"declaring_type\": member[\"declaring_type\"].removeprefix(\"T:\"),\n+                \"signature\": signature(member),\n+                \"documentation_id\": member[\"documentation_id\"],\n+            }\n+            for member in stream_members\n+        ],\n+        \"evidence\": [\n+            \"System.Text.Json.symbols.json: library, request.admission=public_api, \"\n+            \"parameter_type_members['T:System.IO.Stream'], members[].parameters, \"\n+            \"members[].display_signature; jq queries selecting parameters with type == 'System.IO.Stream'\"\n+        ],\n+        \"complete\": True,\n+        \"why\": \"All 30 public/protected admitted members with an exact Stream parameter are listed; \"\n+        \"the complete parameter lookup agrees with the member scan.\",\n+    },\n+    {\n+        \"question\": 2,\n+        \"answer\": [\n+            {\n+                \"declaring_type\": member[\"declaring_type\"].removeprefix(\"T:\"),\n+                \"signature\": signature(member),\n+                \"documentation_id\": member[\"documentation_id\"],\n+            }\n+            for member in document_members\n+        ],\n+        \"evidence\": [\n+            \"System.Text.Json.symbols.json: produced_type_members['T:System.Text.Json.JsonDocument'], \"\n+            \"members[].return_type and members[].parameters[].modifier; jq queries for \"\n+            \"JsonDocument return/Task/out members\"\n+        ],\n+        \"complete\": True,\n+        \"why\": \"All 13 direct producers are included; the complete produced-type lookup agrees. \"\n+        \"JsonDocumentConverter returns a converter, not a document. Generic APIs whose \"\n+        \"type argument could be JsonDocument are not declared to produce one specifically.\",\n+    },\n+    {\n+        \"question\": 3,\n+        \"answer\": [\n+            {\n+                \"declaring_type\": type_id.removeprefix(\"T:\"),\n+                \"member_name\": name,\n+                \"marked_apis\": [\n+                    {\n+                        \"signature\": signature(member),\n+                        \"documentation_id\": member[\"documentation_id\"],\n+                    }\n+                    for member in group\n+                ],\n+                \"same_named_unmarked_overload_exists\": (type_id, name) in unmarked_names,\n+            }\n+            for (type_id, name), group in sorted(marked_groups.items())\n+        ],\n+        \"evidence\": [\n+            \"System.Text.Json.symbols.json: attribute_type_members\"\n+            \"['T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute'], \"\n+            \"members[].attribute_types, members[].declaring_type, members[].name; \"\n+            \"jq group_by([.declaring_type,.name]) and same-name unmarked-member query\"\n+        ],\n+        \"complete\": True,\n+        \"why\": \"The 54 marked public/protected members are grouped by type and name; each group \"\n+        \"was compared to every same-named member in the complete API list. No admitted \"\n+        \"type has this attribute.\",\n+    },\n+    {\n+        \"question\": 4,\n+        \"answer\": {\n+            \"signature\": signature(parse_async),\n+            \"documentation_summary\": parse_async[\"documentation\"][\"summary\"],\n+        },\n+        \"evidence\": [\n+            \"System.Text.Json.symbols.json: \"\n+            + parse_async[\"documentation_id\"]\n+            + \" display_signature, is_static, accessibility, documentation.summary\"\n+        ],\n+        \"complete\": True,\n+        \"why\": \"The sole JsonDocument.ParseAsync declaration has complete documentation \"\n+        \"in the library symbol document.\",\n+    },\n+    {\n+        \"question\": 5,\n+        \"answer\": converter_types,\n+        \"evidence\": [\n+            \"System.Text.Json.symbols.json: name_word_types.Converter, types[documentation_id]\"\n+            \".constructor_accessibility/.is_sealed/.required_overrides/\"\n+            \".inaccessible_required_overrides, and referenced members[].display_signature\"\n+        ],\n+        \"complete\": True,\n+        \"why\": \"All seven converter-named types are listed; required overrides are derived \"\n+        \"from abstract member metadata. Derivable concrete classes and the attribute \"\n+        \"require no overrides. The non-generic JsonConverter has an internal constructor \"\n+        \"and cannot be directly subclassed externally.\",\n+    },\n+    {\n+        \"question\": 6,\n+        \"answer\": [\n+            {\n+                \"signature\": signature(member),\n+                \"documentation_id\": member[\"documentation_id\"],\n+                \"file_url\": member[\"source\"][\"url\"].replace(\n+                    \"https://raw.githubusercontent.com/dotnet/dotnet/\",\n+                    \"https://github.com/dotnet/dotnet/blob/\",\n+                ),\n+                \"declaration_line\": declaration_lines[member[\"documentation_id\"]],\n+                \"link\": member[\"source\"][\"url\"].replace(\n+                    \"https://raw.githubusercontent.com/dotnet/dotnet/\",\n+                    \"https://github.com/dotnet/dotnet/blob/\",\n+                )\n+                + f\"#L{declaration_lines[member['documentation_id']]}\",\n+            }\n+            for member in deserialize_stream\n+        ],\n+        \"evidence\": [\n+            \"System.Text.Json.symbols.json: JsonSerializer.Deserialize Stream members \"\n+            \"and their source.url; downloaded \"\n+            \"https://raw.githubusercontent.com/dotnet/dotnet/\"\n+            \"0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/\"\n+            \"System.Text.Json/src/System/Text/Json/Serialization/\"\n+            \"JsonSerializer.Read.Stream.cs and inspected its numbered lines with nl -ba\"\n+        ],\n+        \"complete\": True,\n+        \"why\": \"All five synchronous Deserialize Stream overloads were matched to their \"\n+        \"actual declaration-start lines. The symbol document's source.line instead \"\n+        \"points at the first executable line inside each method.\",\n+    },\n+]\n+\n+Path(\"answer.json\").write_text(\n+    json.dumps(\n+        {\n+            \"answers\": answers,\n+            \"friction\": \"The symbol document's source.line values for Deserialize(Stream) \"\n+            \"are first executable sequence points, not signature lines; checked the \"\n+            \"published source to report declaration lines. No .NET inspection tool used.\",\n+        },\n+        indent=2,\n+    )\n+    + \"\\n\"\n+)\n*** End Patch\n"
```

</details>

```
Added 1 file(s): /private/tmp/cli-trial-20261106/document-1/make_answer.py
```


---

<sub>1m 1s</sub>

### `apply_patch`

<details>
<summary>Arguments</summary>

```json
"*** Begin Patch\n*** Update File: make_answer.py\n@@\n def signature(member):\n     modifiers = [member[\"accessibility\"].replace(\"_\", \" \")]\n     if member[\"is_static\"]:\n         modifiers.append(\"static\")\n+    if member[\"is_abstract\"]:\n+        modifiers.append(\"abstract\")\n     return \" \".join(modifiers) + \" \" + member[\"display_signature\"]\n*** End Patch\n"
```

</details>

```
Modified 1 file(s): /private/tmp/cli-trial-20261106/document-1/make_answer.py
```


---

<sub>1m 6s</sub>

### `bash`

**Generate and verify structured answers**

`$ python3 make_answer.py && jq -e '{questions:[.answers[].question], counts:[.answers[].answer|if type=="array" then length else 1 end], marked_total:([.answers[2].answer[].marked_apis|length]|add), q4:.answers[3].answer, q5:.answers[4].answer, q6:[.answers[5].answer[]|{signature,link}]}' answer.json && ls -lh answer.json`

<details>
<summary>92 lines</summary>

```
{
  "questions": [
    1,
    2,
    3,
    4,
    5,
    6
  ],
  "counts": [
    30,
    13,
    20,
    1,
    7,
    5
  ],
  "marked_total": 54,
  "q4": {
    "signature": "public static System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ParseAsync(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default, System.Threading.CancellationToken cancellationToken = default)",
    "documentation_summary": "Parses a Stream as UTF-8-encoded data representing a single JSON value into a JsonDocument. The stream is read to completion."
  },
  "q5": [
    {
      "type": "System.Text.Json.Serialization.JsonConverter",
      "derivable_outside_library": false,
      "required_overrides": null,
      "reason": "Internal constructor and inaccessible abstract members"
    },
    {
      "type": "System.Text.Json.Serialization.JsonConverterAttribute",
      "derivable_outside_library": true,
      "required_overrides": []
    },
    {
      "type": "System.Text.Json.Serialization.JsonConverterFactory",
      "derivable_outside_library": true,
      "required_overrides": [
        "public abstract bool CanConvert(System.Type typeToConvert)",
        "public abstract System.Text.Json.Serialization.JsonConverter? CreateConverter(System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)"
      ]
    },
    {
      "type": "System.Text.Json.Serialization.JsonConverter`1",
      "derivable_outside_library": true,
      "required_overrides": [
        "public abstract T? Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)",
        "public abstract void Write(System.Text.Json.Utf8JsonWriter writer, T value, System.Text.Json.JsonSerializerOptions options)"
      ]
    },
    {
      "type": "System.Text.Json.Serialization.JsonNumberEnumConverter`1",
      "derivable_outside_library": false,
      "required_overrides": null,
      "reason": "Sealed type"
    },
    {
      "type": "System.Text.Json.Serialization.JsonStringEnumConverter",
      "derivable_outside_library": true,
      "required_overrides": []
    },
    {
      "type": "System.Text.Json.Serialization.JsonStringEnumConverter`1",
      "derivable_outside_library": true,
      "required_overrides": []
    }
  ],
  "q6": [
    {
      "signature": "public static TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null)",
      "link": "https://github.com/dotnet/dotnet/blob/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs#L76"
    },
    {
      "signature": "public static object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)",
      "link": "https://github.com/dotnet/dotnet/blob/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs#L146"
    },
    {
      "signature": "public static TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo)",
      "link": "https://github.com/dotnet/dotnet/blob/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs#L234"
    },
    {
      "signature": "public static object? Deserialize(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)",
      "link": "https://github.com/dotnet/dotnet/blob/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs#L259"
    },
    {
      "signature": "public static object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)",
      "link": "https://github.com/dotnet/dotnet/blob/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs#L335"
    }
  ]
}
-rw-r--r--@ 1 rich  wheel    53K Nov  6 14:58 answer.json
<shellId: 12 completed with exit code 0>
```

</details>


---

<sub>1m 12s</sub>

### `apply_patch`

<details>
<summary>Arguments</summary>

```json
"*** Begin Patch\n*** Update File: make_answer.py\n@@\n     converter_types.append(\n         {\n-            \"type\": type_id.removeprefix(\"T:\"),\n+            \"type\": info[\"namespace\"] + \".\" + info[\"name\"],\n+            \"documentation_id\": type_id,\n*** End Patch\n"
```

</details>

```
Modified 1 file(s): /private/tmp/cli-trial-20261106/document-1/make_answer.py
```


---

<sub>1m 16s</sub>

### `bash`

**Finalize JSON and remove temporary work files**

`$ python3 make_answer.py && jq -e '(.answers|length)==6 and (.answers|all(.complete == true and (.evidence|length)>0)) and ([.answers[0].answer|length,.answers[1].answer|length,.answers[4].answer|length,.answers[5].answer|length] == [30,13,7,5]) and ([.answers[2].answer[].marked_apis|length]|add)==54 and ([.answers[4].answer[].type]|index("System.Text.Json.Serialization.JsonConverter<T>"))!=null' answer.json >/dev/null && rm -f -- make_answer.py JsonSerializer.Read.Stream.cs && ls -lh answer.json`

```
jq: error (at answer.json:723): Cannot index number with string "answers"
<shellId: 13 completed with exit code 5>
```


---

<sub>1m 20s</sub>

### `bash`

**Verify output and clean temporary files**

`$ python3 -c 'import json; d=json.load(open("answer.json")); a=d["answers"]; assert len(a)==6 and all(x["complete"] and x["evidence"] for x in a); assert [len(a[i]["answer"]) for i in [0,1,4,5]]==[30,13,7,5]; assert sum(len(g["marked_apis"]) for g in a[2]["answer"])==54; assert any(x["type"]=="System.Text.Json.Serialization.JsonConverter<T>" for x in a[4]["answer"]); print("answer.json valid, 6 complete answers")' && rm -f -- make_answer.py JsonSerializer.Read.Stream.cs && ls -lh answer.json`

```
answer.json valid, 6 complete answers
-rw-r--r--@ 1 rich  wheel    53K Nov  6 14:58 answer.json
<shellId: 14 completed with exit code 0>
```


---

<sub>1m 23s</sub>

### Copilot

Wrote `answer.json` in the scratch directory with all six answers, citations, completeness assessments, and friction noted. For the Stream `Deserialize` overloads, it cites verified declaration lines rather than the symbol document’s method-body lines.

---

<sub>Generated by [GitHub Copilot CLI](https://github.com/features/copilot/cli)</sub>
