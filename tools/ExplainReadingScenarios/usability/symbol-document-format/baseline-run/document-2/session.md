# Copilot CLI Session

> [!NOTE]
> - **Session ID:** `9fee0eca-28ea-4664-9080-c70351f962c1`  
> - **Started:** 11/6/2026, 2:59:40 PM  
> - **Duration:** 1m 14s  
> - **Exported:** 11/6/2026, 3:00:55 PM  

---

<sub>2s</sub>

### User

You are a fresh participant in a cooperative usability trial. Your scratch directory is /tmp/cli-trial-20261106/document-2; work only there. Do not read anything else on this machine outside your scratch directory and the .NET SDK, including repositories, designs, earlier results, or other trials. Do not spawn agents. This protocol is cooperative, not a security sandbox.

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

**Inspect symbol document layout and metadata**

`$ pwd; ls -lah; jq 'keys, (.header // .metadata // {}), (.sectionOutcomes // .sections // {} | keys)' System.Text.Json.symbols.json | head -180`

<details>
<summary>32 lines</summary>

```
/tmp/cli-trial-20261106/document-2
total 3976
drwxr-xr-x@  8 rich  wheel   256B Nov  6 14:59 .
drwxr-xr-x@ 13 rich  wheel   416B Nov  6 14:59 ..
-rw-r--r--@  1 rich  wheel     0B Nov  6 14:59 err.txt
-rw-r--r--@  1 rich  wheel   2.8K Nov  6 14:57 prompt.txt
-rw-r--r--@  1 rich  wheel     0B Nov  6 14:59 result.md
-rwxr-xr-x@  1 rich  wheel   1.0K Nov  6 14:57 run.sh
-rw-r--r--@  1 rich  wheel    37B Nov  6 14:57 session-id
-rw-r--r--@  1 rich  wheel   1.9M Nov  6 14:57 System.Text.Json.symbols.json
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
[]
<shellId: 0 completed with exit code 0>
```

</details>


---

<sub>9s</sub>

### `bash`

**Read symbol document provenance and section completeness**

`$ jq '{library,request,counts,generator,schema_version,section_outcomes,outcome}' System.Text.Json.symbols.json | head -220`

<details>
<summary>55 lines</summary>

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
  "generator": {
    "name": "library-symbol-document-prototype",
    "version": "0"
  },
  "schema_version": "1",
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
  "outcome": "complete"
}
<shellId: 1 completed with exit code 0>
```

</details>


---

<sub>9s</sub>

### `bash`

**Inspect type and member schema and lookup keys**

`$ jq '{types_type:(.types|type),types_sample:(.types|to_entries|.[0:2]),members_type:(.members|type),members_sample:(.members|if type=="array" then .[0:2] else to_entries|.[0:2] end), lookup_keys:{parameters:(.parameter_type_members|keys|.[0:8]),produced:(.produced_type_members|keys|.[0:8]),attributes:(.attribute_type_members|keys|.[0:8])}}' System.Text.Json.symbols.json | head -260`

<details>
<summary>232 lines</summary>

```
{
  "types_type": "object",
  "types_sample": [
    {
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
    {
      "key": "T:System.Runtime.InteropServices.JsonMarshal",
      "value": {
        "name": "JsonMarshal",
        "metadata_name": "JsonMarshal",
        "namespace": "System.Runtime.InteropServices",
        "declaring_type": "",
        "declaration_kind": "definition",
        "forwarded_to_assembly": "",
        "definition_kind": "class",
        "accessibility": "public",
        "is_static": true,
        "is_abstract": false,
        "is_sealed": false,
        "constructor_accessibility": "",
        "generic_parameters": [],
        "base_type": "T:System.Object",
        "interfaces": [],
        "required_overrides": [],
        "inaccessible_required_overrides": 0,
        "attribute_types": [],
        "suffix_family": "Marshal",
        "two_word_suffix_family": "JsonMarshal",
        "name_words": [
          "Json",
          "Marshal"
        ],
        "documentation": {
          "status": "complete",
          "summary": "An unsafe class that provides a set of methods to access the underlying data representations of JSON types."
        },
        "source": {
          "status": "complete",
          "urls": [
            "https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Runtime/InteropServices/JsonMarshal.cs"
          ]
        },
        "findings": [],
        "metadata_token": 33554438
      }
    }
  ],
  "members_type": "array",
  "members_sample": [
    {
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
    {
      "documentation_id": "M:System.Runtime.InteropServices.JsonMarshal.GetRawUtf8PropertyName(System.Text.Json.JsonProperty)",
      "canonical_signature": "M:System.Runtime.InteropServices.JsonMarshal.GetRawUtf8PropertyName(System.Text.Json.JsonProperty)",
      "display_signature": "System.ReadOnlySpan<byte> GetRawUtf8PropertyName(System.Text.Json.JsonProperty property)",
      "declaring_type": "T:System.Runtime.InteropServices.JsonMarshal",
      "namespace": "System.Runtime.InteropServices",
      "declaring_type_suffix_family": "Marshal",
      "declaring_type_two_word_suffix_family": "JsonMarshal",
      "declaring_type_name_words": [
        "Json",
        "Marshal"
      ],
      "kind": "method",
      "name": "GetRawUtf8PropertyName",
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
          "name": "property",
          "type": "System.Text.Json.JsonProperty",
          "type_mentions": [
            "T:System.Text.Json.JsonProperty"
          ],
          "modifier": "",
          "has_default_value": false
        }
      ],
      "overrides": "",
      "attribute_types": [],
      "documentation": {
        "status": "complete",
        "summary": "Gets a ReadOnlySpan view over the raw JSON data of the given JsonProperty name."
      },
      "source": {
        "status": "complete",
        "url": "https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Runtime/InteropServices/JsonMarshal.cs",
        "line": 46
      },
      "findings": [],
      "metadata_token": 100663532
    }
  ],
  "lookup_keys": {
    "parameters": [
      "T:System.Action`1",
      "T:System.Action`2",
      "T:System.Boolean",
      "T:System.Buffers.IBufferWriter`1",
      "T:System.Buffers.ReadOnlySequence`1",
      "T:System.Byte",
      "T:System.Char",
      "T:System.Collections.Generic.IEnumerable`1"
    ],
    "produced": [
      "T:System.Action`1",
      "T:System.Action`2",
      "T:System.Boolean",
      "T:System.Buffers.ReadOnlySequence`1",
      "T:System.Byte",
      "T:System.Char",
      "T:System.Collections.Generic.IAsyncEnumerable`1",
      "T:System.Collections.Generic.IEnumerable`1"
    ],
    "attributes": [
      "T:System.CLSCompliantAttribute",
      "T:System.ComponentModel.EditorBrowsableAttribute",
      "T:System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute",
      "T:System.Diagnostics.CodeAnalysis.FeatureSwitchDefinitionAttribute",
      "T:System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute",
      "T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute",
      "T:System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute",
      "T:System.ObsoleteAttribute"
    ]
  }
}
<shellId: 2 completed with exit code 0>
```

</details>


---

<sub>13s</sub>

### Copilot

The symbol document reports complete declarations, documentation, and source-link sections. I’ll use its member records for the API lists and its source locations for the Stream overloads.

---

<sub>13s</sub>

### `bash`

**Find public or protected Stream parameter APIs**

`$ jq -r '.parameter_type_members["T:System.IO.Stream"] as $ids | .members[] | select(.documentation_id as $id | $ids | index($id)) | [.declaring_type,.display_signature,.documentation_id] | @tsv' System.Text.Json.symbols.json`

<details>
<summary>31 lines</summary>

```
T:System.Text.Json.JsonDocument	System.Text.Json.JsonDocument Parse(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default)	M:System.Text.Json.JsonDocument.Parse(System.IO.Stream,System.Text.Json.JsonDocumentOptions)
T:System.Text.Json.JsonDocument	System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ParseAsync(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	System.Threading.Tasks.ValueTask<TValue?> DeserializeAsync<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsync``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	System.Threading.Tasks.ValueTask<TValue?> DeserializeAsync<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsync``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo)	M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})
T:System.Text.Json.JsonSerializer	object? Deserialize(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)	M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo)
T:System.Text.Json.JsonSerializer	System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Type,System.Text.Json.Serialization.JsonSerializerContext,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)	M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.Serialization.JsonSerializerContext)
T:System.Text.Json.JsonSerializer	System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, bool topLevelValues, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Boolean,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo, bool topLevelValues, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Boolean,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.SerializeAsync``1(System.IO.Stream,``0,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	void Serialize<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Serialize``1(System.IO.Stream,``0,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.SerializeAsync(System.IO.Stream,System.Object,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	void Serialize(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Serialize(System.IO.Stream,System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.SerializeAsync``1(System.IO.Stream,``0,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	void Serialize<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo)	M:System.Text.Json.JsonSerializer.Serialize``1(System.IO.Stream,``0,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})
T:System.Text.Json.JsonSerializer	System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.SerializeAsync(System.IO.Stream,System.Object,System.Text.Json.Serialization.Metadata.JsonTypeInfo,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	void Serialize(System.IO.Stream utf8Json, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)	M:System.Text.Json.JsonSerializer.Serialize(System.IO.Stream,System.Object,System.Text.Json.Serialization.Metadata.JsonTypeInfo)
T:System.Text.Json.JsonSerializer	System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.SerializeAsync(System.IO.Stream,System.Object,System.Type,System.Text.Json.Serialization.JsonSerializerContext,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	void Serialize(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)	M:System.Text.Json.JsonSerializer.Serialize(System.IO.Stream,System.Object,System.Type,System.Text.Json.Serialization.JsonSerializerContext)
T:System.Text.Json.Nodes.JsonNode	System.Text.Json.Nodes.JsonNode? Parse(System.IO.Stream utf8Json, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> nodeOptions = null, System.Text.Json.JsonDocumentOptions documentOptions = default)	M:System.Text.Json.Nodes.JsonNode.Parse(System.IO.Stream,System.Nullable{System.Text.Json.Nodes.JsonNodeOptions},System.Text.Json.JsonDocumentOptions)
T:System.Text.Json.Nodes.JsonNode	System.Threading.Tasks.Task<System.Text.Json.Nodes.JsonNode?> ParseAsync(System.IO.Stream utf8Json, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> nodeOptions = null, System.Text.Json.JsonDocumentOptions documentOptions = default, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.Nodes.JsonNode.ParseAsync(System.IO.Stream,System.Nullable{System.Text.Json.Nodes.JsonNodeOptions},System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)
T:System.Text.Json.Utf8JsonWriter	Utf8JsonWriter(System.IO.Stream utf8Json, System.Text.Json.JsonWriterOptions options = default)	M:System.Text.Json.Utf8JsonWriter.#ctor(System.IO.Stream,System.Text.Json.JsonWriterOptions)
T:System.Text.Json.Utf8JsonWriter	void Reset(System.IO.Stream utf8Json)	M:System.Text.Json.Utf8JsonWriter.Reset(System.IO.Stream)
<shellId: 3 completed with exit code 0>
```

</details>


---

<sub>13s</sub>

### `bash`

**Find JsonDocument-producing APIs and out parameters**

`$ jq -r '.members[] | select((.return_type|test("(^|[<, ])System\\.Text\\.Json\\.JsonDocument($|[>, ])")) or any(.parameters[]?; (.modifier=="out" and .type=="System.Text.Json.JsonDocument"))) | [.declaring_type,.display_signature,.documentation_id,.return_type,([.parameters[]? | select(.modifier=="out") | .type] | join(","))] | @tsv' System.Text.Json.symbols.json`

<details>
<summary>13 lines</summary>

```
T:System.Text.Json.JsonDocument	System.Text.Json.JsonDocument Parse(System.ReadOnlyMemory<byte> utf8Json, System.Text.Json.JsonDocumentOptions options = default)	M:System.Text.Json.JsonDocument.Parse(System.ReadOnlyMemory{System.Byte},System.Text.Json.JsonDocumentOptions)	System.Text.Json.JsonDocument	
T:System.Text.Json.JsonDocument	System.Text.Json.JsonDocument Parse(System.Buffers.ReadOnlySequence<byte> utf8Json, System.Text.Json.JsonDocumentOptions options = default)	M:System.Text.Json.JsonDocument.Parse(System.Buffers.ReadOnlySequence{System.Byte},System.Text.Json.JsonDocumentOptions)	System.Text.Json.JsonDocument	
T:System.Text.Json.JsonDocument	System.Text.Json.JsonDocument Parse(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default)	M:System.Text.Json.JsonDocument.Parse(System.IO.Stream,System.Text.Json.JsonDocumentOptions)	System.Text.Json.JsonDocument	
T:System.Text.Json.JsonDocument	System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ParseAsync(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)	System.Threading.Tasks.Task<System.Text.Json.JsonDocument>	
T:System.Text.Json.JsonDocument	System.Text.Json.JsonDocument Parse(System.ReadOnlyMemory<char> json, System.Text.Json.JsonDocumentOptions options = default)	M:System.Text.Json.JsonDocument.Parse(System.ReadOnlyMemory{System.Char},System.Text.Json.JsonDocumentOptions)	System.Text.Json.JsonDocument	
T:System.Text.Json.JsonDocument	System.Text.Json.JsonDocument Parse(string json, System.Text.Json.JsonDocumentOptions options = default)	M:System.Text.Json.JsonDocument.Parse(System.String,System.Text.Json.JsonDocumentOptions)	System.Text.Json.JsonDocument	
T:System.Text.Json.JsonDocument	System.Text.Json.JsonDocument ParseValue(ref System.Text.Json.Utf8JsonReader reader)	M:System.Text.Json.JsonDocument.ParseValue(System.Text.Json.Utf8JsonReader@)	System.Text.Json.JsonDocument	
T:System.Text.Json.JsonSerializer	System.Text.Json.JsonDocument SerializeToDocument<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.SerializeToDocument``1(``0,System.Text.Json.JsonSerializerOptions)	System.Text.Json.JsonDocument	
T:System.Text.Json.JsonSerializer	System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)	System.Text.Json.JsonDocument	
T:System.Text.Json.JsonSerializer	System.Text.Json.JsonDocument SerializeToDocument<TValue>(TValue value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo)	M:System.Text.Json.JsonSerializer.SerializeToDocument``1(``0,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})	System.Text.Json.JsonDocument	
T:System.Text.Json.JsonSerializer	System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)	M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Text.Json.Serialization.Metadata.JsonTypeInfo)	System.Text.Json.JsonDocument	
T:System.Text.Json.JsonSerializer	System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)	M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Type,System.Text.Json.Serialization.JsonSerializerContext)	System.Text.Json.JsonDocument
<shellId: 4 completed with exit code 0>
```

</details>


---

<sub>13s</sub>

### `bash`

**Find APIs annotated RequiresUnreferencedCode**

`$ jq -r '.attribute_type_members["T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"] as $ids | .members[] | select(.documentation_id as $id | $ids | index($id)) | [.declaring_type,.name,.display_signature,.documentation_id] | @tsv' System.Text.Json.symbols.json`

<details>
<summary>55 lines</summary>

```
T:System.Text.Json.JsonSerializer	Deserialize	TValue? Deserialize<TValue>(System.Text.Json.JsonDocument document, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonDocument,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Deserialize	object? Deserialize(System.Text.Json.JsonDocument document, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonDocument,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Deserialize	TValue? Deserialize<TValue>(System.Text.Json.JsonElement element, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonElement,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Deserialize	object? Deserialize(System.Text.Json.JsonElement element, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonElement,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Deserialize	TValue? Deserialize<TValue>(System.Text.Json.Nodes.JsonNode? node, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.Nodes.JsonNode,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Deserialize	object? Deserialize(System.Text.Json.Nodes.JsonNode? node, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.Nodes.JsonNode,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	DeserializeAsync	System.Threading.Tasks.ValueTask<TValue?> DeserializeAsync<TValue>(System.IO.Pipelines.PipeReader utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsync``1(System.IO.Pipelines.PipeReader,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	DeserializeAsync	System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Pipelines.PipeReader utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Pipelines.PipeReader,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	DeserializeAsyncEnumerable	System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Pipelines.PipeReader utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Pipelines.PipeReader,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	DeserializeAsyncEnumerable	System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Pipelines.PipeReader utf8Json, bool topLevelValues, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Pipelines.PipeReader,System.Boolean,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	SerializeToDocument	System.Text.Json.JsonDocument SerializeToDocument<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.SerializeToDocument``1(``0,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	SerializeToDocument	System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	SerializeToElement	System.Text.Json.JsonElement SerializeToElement<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.SerializeToElement``1(``0,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	SerializeToElement	System.Text.Json.JsonElement SerializeToElement(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.SerializeToElement(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	SerializeToNode	System.Text.Json.Nodes.JsonNode? SerializeToNode<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.SerializeToNode``1(``0,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	SerializeToNode	System.Text.Json.Nodes.JsonNode? SerializeToNode(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.SerializeToNode(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Deserialize	TValue? Deserialize<TValue>(System.ReadOnlySpan<byte> utf8Json, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize``1(System.ReadOnlySpan{System.Byte},System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Deserialize	object? Deserialize(System.ReadOnlySpan<byte> utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize(System.ReadOnlySpan{System.Byte},System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	DeserializeAsync	System.Threading.Tasks.ValueTask<TValue?> DeserializeAsync<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsync``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	Deserialize	TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	DeserializeAsync	System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	Deserialize	object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	DeserializeAsyncEnumerable	System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	DeserializeAsyncEnumerable	System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, bool topLevelValues, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Boolean,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	Deserialize	TValue? Deserialize<TValue>(string json, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize``1(System.String,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Deserialize	TValue? Deserialize<TValue>(System.ReadOnlySpan<char> json, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize``1(System.ReadOnlySpan{System.Char},System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Deserialize	object? Deserialize(string json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize(System.String,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Deserialize	object? Deserialize(System.ReadOnlySpan<char> json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize(System.ReadOnlySpan{System.Char},System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Deserialize	TValue? Deserialize<TValue>(ref System.Text.Json.Utf8JsonReader reader, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.Utf8JsonReader@,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Deserialize	object? Deserialize(ref System.Text.Json.Utf8JsonReader reader, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	SerializeToUtf8Bytes	byte[] SerializeToUtf8Bytes<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.SerializeToUtf8Bytes``1(``0,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	SerializeToUtf8Bytes	byte[] SerializeToUtf8Bytes(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	SerializeAsync	System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.SerializeAsync``1(System.IO.Stream,``0,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	Serialize	void Serialize<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Serialize``1(System.IO.Stream,``0,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	SerializeAsync	System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.SerializeAsync(System.IO.Stream,System.Object,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	Serialize	void Serialize(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Serialize(System.IO.Stream,System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Serialize	string Serialize<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Serialize``1(``0,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Serialize	string Serialize(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Serialize(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	SerializeAsync	System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Pipelines.PipeWriter utf8Json, TValue value, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.SerializeAsync``1(System.IO.Pipelines.PipeWriter,``0,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	SerializeAsync	System.Threading.Tasks.Task SerializeAsync(System.IO.Pipelines.PipeWriter utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)	M:System.Text.Json.JsonSerializer.SerializeAsync(System.IO.Pipelines.PipeWriter,System.Object,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)
T:System.Text.Json.JsonSerializer	Serialize	void Serialize<TValue>(System.Text.Json.Utf8JsonWriter writer, TValue value, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Serialize``1(System.Text.Json.Utf8JsonWriter,``0,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializer	Serialize	void Serialize(System.Text.Json.Utf8JsonWriter writer, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)	M:System.Text.Json.JsonSerializer.Serialize(System.Text.Json.Utf8JsonWriter,System.Object,System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.JsonSerializerOptions	GetConverter	System.Text.Json.Serialization.JsonConverter GetConverter(System.Type typeToConvert)	M:System.Text.Json.JsonSerializerOptions.GetConverter(System.Type)
T:System.Text.Json.JsonSerializerOptions	MakeReadOnly	void MakeReadOnly(bool populateMissingResolver)	M:System.Text.Json.JsonSerializerOptions.MakeReadOnly(System.Boolean)
T:System.Text.Json.JsonSerializerOptions	Default	System.Text.Json.JsonSerializerOptions Default { get; }	P:System.Text.Json.JsonSerializerOptions.Default
T:System.Text.Json.JsonSerializerOptions	Web	System.Text.Json.JsonSerializerOptions Web { get; }	P:System.Text.Json.JsonSerializerOptions.Web
T:System.Text.Json.JsonSerializerOptions	Strict	System.Text.Json.JsonSerializerOptions Strict { get; }	P:System.Text.Json.JsonSerializerOptions.Strict
T:System.Text.Json.Nodes.JsonArray	Add	void Add<T>(T? value)	M:System.Text.Json.Nodes.JsonArray.Add``1(``0)
T:System.Text.Json.Nodes.JsonNode	ReplaceWith	void ReplaceWith<T>(T value)	M:System.Text.Json.Nodes.JsonNode.ReplaceWith``1(``0)
T:System.Text.Json.Nodes.JsonValue	Create	System.Text.Json.Nodes.JsonValue? Create<T>(T? value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)	M:System.Text.Json.Nodes.JsonValue.Create``1(``0,System.Nullable{System.Text.Json.Nodes.JsonNodeOptions})
T:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver	#ctor	DefaultJsonTypeInfoResolver()	M:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver.#ctor
T:System.Text.Json.Serialization.Metadata.JsonTypeInfo	CreateJsonTypeInfo	System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> CreateJsonTypeInfo<T>(System.Text.Json.JsonSerializerOptions options)	M:System.Text.Json.Serialization.Metadata.JsonTypeInfo.CreateJsonTypeInfo``1(System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.Serialization.Metadata.JsonTypeInfo	CreateJsonTypeInfo	System.Text.Json.Serialization.Metadata.JsonTypeInfo CreateJsonTypeInfo(System.Type type, System.Text.Json.JsonSerializerOptions options)	M:System.Text.Json.Serialization.Metadata.JsonTypeInfo.CreateJsonTypeInfo(System.Type,System.Text.Json.JsonSerializerOptions)
T:System.Text.Json.Serialization.Metadata.JsonTypeInfo	CreateJsonPropertyInfo	System.Text.Json.Serialization.Metadata.JsonPropertyInfo CreateJsonPropertyInfo(System.Type propertyType, string name)	M:System.Text.Json.Serialization.Metadata.JsonTypeInfo.CreateJsonPropertyInfo(System.Type,System.String)
<shellId: 5 completed with exit code 0>
```

</details>


---

<sub>13s</sub>

### `bash`

**Read ParseAsync documentation and converter-related type derivation**

`$ jq '{parse_async:[.members[]|select(.declaring_type=="T:System.Text.Json.JsonDocument" and .name=="ParseAsync")|{documentation_id,display_signature,documentation,source}],converter_types:[.types|to_entries[]|select(.value.name|test("Converter";"i"))|{id:.key,name:.value.name,accessibility:.value.accessibility,is_static:.value.is_static,is_abstract:.value.is_abstract,is_sealed:.value.is_sealed,constructor_accessibility:.value.constructor_accessibility,required_overrides:.value.required_overrides,inaccessible_required_overrides:.value.inaccessible_required_overrides,base_type:.value.base_type}]}' System.Text.Json.symbols.json | head -420`

<details>
<summary>126 lines</summary>

```
{
  "parse_async": [
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
  "converter_types": [
    {
      "id": "T:System.Text.Json.Serialization.JsonConverter",
      "name": "JsonConverter",
      "accessibility": "public",
      "is_static": false,
      "is_abstract": true,
      "is_sealed": false,
      "constructor_accessibility": "internal",
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
      "inaccessible_required_overrides": 13,
      "base_type": "T:System.Object"
    },
    {
      "id": "T:System.Text.Json.Serialization.JsonConverterAttribute",
      "name": "JsonConverterAttribute",
      "accessibility": "public",
      "is_static": false,
      "is_abstract": false,
      "is_sealed": false,
      "constructor_accessibility": "public",
      "required_overrides": [],
      "inaccessible_required_overrides": 0,
      "base_type": "T:System.Text.Json.Serialization.JsonAttribute"
    },
    {
      "id": "T:System.Text.Json.Serialization.JsonConverterFactory",
      "name": "JsonConverterFactory",
      "accessibility": "public",
      "is_static": false,
      "is_abstract": true,
      "is_sealed": false,
      "constructor_accessibility": "protected",
      "required_overrides": [
        "M:System.Text.Json.Serialization.JsonConverter.CanConvert(System.Type)",
        "M:System.Text.Json.Serialization.JsonConverterFactory.CreateConverter(System.Type,System.Text.Json.JsonSerializerOptions)"
      ],
      "inaccessible_required_overrides": 0,
      "base_type": "T:System.Text.Json.Serialization.JsonConverter"
    },
    {
      "id": "T:System.Text.Json.Serialization.JsonConverter`1",
      "name": "JsonConverter<T>",
      "accessibility": "public",
      "is_static": false,
      "is_abstract": true,
      "is_sealed": false,
      "constructor_accessibility": "protected_internal",
      "required_overrides": [
        "M:System.Text.Json.Serialization.JsonConverter`1.Read(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions)",
        "M:System.Text.Json.Serialization.JsonConverter`1.Write(System.Text.Json.Utf8JsonWriter,`0,System.Text.Json.JsonSerializerOptions)"
      ],
      "inaccessible_required_overrides": 0,
      "base_type": "T:System.Text.Json.Serialization.JsonConverter"
    },
    {
      "id": "T:System.Text.Json.Serialization.JsonNumberEnumConverter`1",
      "name": "JsonNumberEnumConverter<TEnum>",
      "accessibility": "public",
      "is_static": false,
      "is_abstract": false,
      "is_sealed": true,
      "constructor_accessibility": "public",
      "required_overrides": [],
      "inaccessible_required_overrides": 0,
      "base_type": "T:System.Text.Json.Serialization.JsonConverterFactory"
    },
    {
      "id": "T:System.Text.Json.Serialization.JsonStringEnumConverter",
      "name": "JsonStringEnumConverter",
      "accessibility": "public",
      "is_static": false,
      "is_abstract": false,
      "is_sealed": false,
      "constructor_accessibility": "public",
      "required_overrides": [],
      "inaccessible_required_overrides": 0,
      "base_type": "T:System.Text.Json.Serialization.JsonConverterFactory"
    },
    {
      "id": "T:System.Text.Json.Serialization.JsonStringEnumConverter`1",
      "name": "JsonStringEnumConverter<TEnum>",
      "accessibility": "public",
      "is_static": false,
      "is_abstract": false,
      "is_sealed": false,
      "constructor_accessibility": "public",
      "required_overrides": [],
      "inaccessible_required_overrides": 0,
      "base_type": "T:System.Text.Json.Serialization.JsonConverterFactory"
    }
  ]
}
<shellId: 6 completed with exit code 0>
```

</details>


---

<sub>20s</sub>

### `bash`

**Cross-check JsonDocument output including out/ref records**

`$ jq '{doc_produced:.produced_type_members["T:System.Text.Json.JsonDocument"],doc_return:.return_type_members["T:System.Text.Json.JsonDocument"],doc_param:.parameter_type_members["T:System.Text.Json.JsonDocument"],doc_named:[.members[]|select(.name|test("Parse|Document"))|select(.declaring_type=="T:System.Text.Json.JsonDocument")|{name,display_signature,parameters}]}' System.Text.Json.symbols.json | head -300`

<details>
<summary>237 lines</summary>

```
{
  "doc_produced": [
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
  "doc_return": [
    "M:System.Text.Json.JsonDocument.Parse(System.Buffers.ReadOnlySequence{System.Byte},System.Text.Json.JsonDocumentOptions)",
    "M:System.Text.Json.JsonDocument.Parse(System.IO.Stream,System.Text.Json.JsonDocumentOptions)",
    "M:System.Text.Json.JsonDocument.Parse(System.ReadOnlyMemory{System.Byte},System.Text.Json.JsonDocumentOptions)",
    "M:System.Text.Json.JsonDocument.Parse(System.ReadOnlyMemory{System.Char},System.Text.Json.JsonDocumentOptions)",
    "M:System.Text.Json.JsonDocument.Parse(System.String,System.Text.Json.JsonDocumentOptions)",
    "M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)",
    "M:System.Text.Json.JsonDocument.ParseValue(System.Text.Json.Utf8JsonReader@)",
    "M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Text.Json.Serialization.Metadata.JsonTypeInfo)",
    "M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)",
    "M:System.Text.Json.JsonSerializer.SerializeToDocument(System.Object,System.Type,System.Text.Json.Serialization.JsonSerializerContext)",
    "M:System.Text.Json.JsonSerializer.SerializeToDocument``1(``0,System.Text.Json.JsonSerializerOptions)",
    "M:System.Text.Json.JsonSerializer.SerializeToDocument``1(``0,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})",
    "P:System.Text.Json.Serialization.Metadata.JsonMetadataServices.JsonDocumentConverter"
  ],
  "doc_param": [
    "M:System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonDocument,System.Text.Json.Serialization.Metadata.JsonTypeInfo)",
    "M:System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonDocument,System.Type,System.Text.Json.JsonSerializerOptions)",
    "M:System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonDocument,System.Type,System.Text.Json.Serialization.JsonSerializerContext)",
    "M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonDocument,System.Text.Json.JsonSerializerOptions)",
    "M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonDocument,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})"
  ],
  "doc_named": [
    {
      "name": "Parse",
      "display_signature": "System.Text.Json.JsonDocument Parse(System.ReadOnlyMemory<byte> utf8Json, System.Text.Json.JsonDocumentOptions options = default)",
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
      "name": "Parse",
      "display_signature": "System.Text.Json.JsonDocument Parse(System.Buffers.ReadOnlySequence<byte> utf8Json, System.Text.Json.JsonDocumentOptions options = default)",
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
      "name": "Parse",
      "display_signature": "System.Text.Json.JsonDocument Parse(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default)",
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
      "name": "ParseAsync",
      "display_signature": "System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ParseAsync(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default, System.Threading.CancellationToken cancellationToken = default)",
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
      "name": "Parse",
      "display_signature": "System.Text.Json.JsonDocument Parse(System.ReadOnlyMemory<char> json, System.Text.Json.JsonDocumentOptions options = default)",
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
      "name": "Parse",
      "display_signature": "System.Text.Json.JsonDocument Parse(string json, System.Text.Json.JsonDocumentOptions options = default)",
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
      "name": "TryParseValue",
      "display_signature": "bool TryParseValue(ref System.Text.Json.Utf8JsonReader reader, out System.Text.Json.JsonDocument? document)",
      "parameters": [
        {
          "name": "reader",
          "type": "System.Text.Json.Utf8JsonReader",
          "type_mentions": [
            "T:System.Text.Json.Utf8JsonReader"
          ],
          "modifier": "ref",
          "has_default_value": false
        },
        {
          "name": "document",
          "type": "System.Text.Json.JsonDocument?",
          "type_mentions": [
            "T:System.Text.Json.JsonDocument"
          ],
          "modifier": "out",
          "has_default_value": false
        }
      ]
    },
    {
      "name": "ParseValue",
      "display_signature": "System.Text.Json.JsonDocument ParseValue(ref System.Text.Json.Utf8JsonReader reader)",
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
    }
  ]
}
<shellId: 7 completed with exit code 0>
```

</details>


---

<sub>20s</sub>

### `bash`

**Inspect grouping of trimming-annotated API names**

`$ jq -r '.attribute_type_members["T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"] as $ids | [.members[] | select(.documentation_id as $id | $ids | index($id)) | {type:.declaring_type,name, signature:.display_signature}] | group_by(.type,.name) | .[] | {type:.[0].type,name:.[0].name,marked:map(.signature),unmarked_same_named:(.[0] as $g | input_filename)}' System.Text.Json.symbols.json 2>&1 | head -50`

<details>
<summary>51 lines</summary>

```
{
  "type": "T:System.Text.Json.JsonSerializer",
  "name": "Deserialize",
  "marked": [
    "TValue? Deserialize<TValue>(System.Text.Json.JsonDocument document, System.Text.Json.JsonSerializerOptions? options = null)",
    "object? Deserialize(System.Text.Json.JsonDocument document, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)",
    "TValue? Deserialize<TValue>(System.Text.Json.JsonElement element, System.Text.Json.JsonSerializerOptions? options = null)",
    "object? Deserialize(System.Text.Json.JsonElement element, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)",
    "TValue? Deserialize<TValue>(System.Text.Json.Nodes.JsonNode? node, System.Text.Json.JsonSerializerOptions? options = null)",
    "object? Deserialize(System.Text.Json.Nodes.JsonNode? node, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)",
    "TValue? Deserialize<TValue>(System.ReadOnlySpan<byte> utf8Json, System.Text.Json.JsonSerializerOptions? options = null)",
    "object? Deserialize(System.ReadOnlySpan<byte> utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)",
    "TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null)",
    "object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)",
    "TValue? Deserialize<TValue>(string json, System.Text.Json.JsonSerializerOptions? options = null)",
    "TValue? Deserialize<TValue>(System.ReadOnlySpan<char> json, System.Text.Json.JsonSerializerOptions? options = null)",
    "object? Deserialize(string json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)",
    "object? Deserialize(System.ReadOnlySpan<char> json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)",
    "TValue? Deserialize<TValue>(ref System.Text.Json.Utf8JsonReader reader, System.Text.Json.JsonSerializerOptions? options = null)",
    "object? Deserialize(ref System.Text.Json.Utf8JsonReader reader, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)"
  ],
  "unmarked_same_named": "System.Text.Json.symbols.json"
}
{
  "type": "T:System.Text.Json.JsonSerializer",
  "name": "DeserializeAsync",
  "marked": [
    "System.Threading.Tasks.ValueTask<TValue?> DeserializeAsync<TValue>(System.IO.Pipelines.PipeReader utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)",
    "System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Pipelines.PipeReader utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)",
    "System.Threading.Tasks.ValueTask<TValue?> DeserializeAsync<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)",
    "System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)"
  ],
  "unmarked_same_named": "System.Text.Json.symbols.json"
}
{
  "type": "T:System.Text.Json.JsonSerializer",
  "name": "DeserializeAsyncEnumerable",
  "marked": [
    "System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Pipelines.PipeReader utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)",
    "System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Pipelines.PipeReader utf8Json, bool topLevelValues, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)",
    "System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)",
    "System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, bool topLevelValues, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)"
  ],
  "unmarked_same_named": "System.Text.Json.symbols.json"
}
{
  "type": "T:System.Text.Json.JsonSerializer",
  "name": "Serialize",
  "marked": [
    "void Serialize<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.JsonSerializerOptions? options = null)",
<shellId: 8 completed with exit code 0>
```

</details>


---

<sub>20s</sub>

### `bash`

**Check converter family definitions and potential overrides**

`$ jq '{converter_word:.name_word_types.Converter,converter_suffix:.suffix_family_types.Converter, types:[.types|to_entries[]|select(.value.name|test("Converter"))|{id:.key,definition_kind:.value.definition_kind,constructor_accessibility:.value.constructor_accessibility,required_overrides:.value.required_overrides,documentation:.value.documentation.summary}],attribute_members:[.members[]|select(.declaring_type=="T:System.Text.Json.Serialization.JsonConverterAttribute")|{name,signature:.display_signature,is_abstract,accessibility}]}' System.Text.Json.symbols.json | head -350`

<details>
<summary>118 lines</summary>

```
{
  "converter_word": [
    "T:System.Text.Json.Serialization.JsonConverter",
    "T:System.Text.Json.Serialization.JsonConverterAttribute",
    "T:System.Text.Json.Serialization.JsonConverterFactory",
    "T:System.Text.Json.Serialization.JsonConverter`1",
    "T:System.Text.Json.Serialization.JsonNumberEnumConverter`1",
    "T:System.Text.Json.Serialization.JsonStringEnumConverter",
    "T:System.Text.Json.Serialization.JsonStringEnumConverter`1"
  ],
  "converter_suffix": [
    "T:System.Text.Json.Serialization.JsonConverter",
    "T:System.Text.Json.Serialization.JsonConverter`1",
    "T:System.Text.Json.Serialization.JsonNumberEnumConverter`1",
    "T:System.Text.Json.Serialization.JsonStringEnumConverter",
    "T:System.Text.Json.Serialization.JsonStringEnumConverter`1"
  ],
  "types": [
    {
      "id": "T:System.Text.Json.Serialization.JsonConverter",
      "definition_kind": "class",
      "constructor_accessibility": "internal",
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
      "documentation": "Converts an object or value to or from JSON."
    },
    {
      "id": "T:System.Text.Json.Serialization.JsonConverterAttribute",
      "definition_kind": "class",
      "constructor_accessibility": "public",
      "required_overrides": [],
      "documentation": "When placed on a property or type, specifies the converter type to use."
    },
    {
      "id": "T:System.Text.Json.Serialization.JsonConverterFactory",
      "definition_kind": "class",
      "constructor_accessibility": "protected",
      "required_overrides": [
        "M:System.Text.Json.Serialization.JsonConverter.CanConvert(System.Type)",
        "M:System.Text.Json.Serialization.JsonConverterFactory.CreateConverter(System.Type,System.Text.Json.JsonSerializerOptions)"
      ],
      "documentation": "Supports converting several types by using a factory pattern."
    },
    {
      "id": "T:System.Text.Json.Serialization.JsonConverter`1",
      "definition_kind": "class",
      "constructor_accessibility": "protected_internal",
      "required_overrides": [
        "M:System.Text.Json.Serialization.JsonConverter`1.Read(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions)",
        "M:System.Text.Json.Serialization.JsonConverter`1.Write(System.Text.Json.Utf8JsonWriter,`0,System.Text.Json.JsonSerializerOptions)"
      ],
      "documentation": "Converts an object or value to or from JSON."
    },
    {
      "id": "T:System.Text.Json.Serialization.JsonNumberEnumConverter`1",
      "definition_kind": "class",
      "constructor_accessibility": "public",
      "required_overrides": [],
      "documentation": "Converter to convert enums to and from numeric values."
    },
    {
      "id": "T:System.Text.Json.Serialization.JsonStringEnumConverter",
      "definition_kind": "class",
      "constructor_accessibility": "public",
      "required_overrides": [],
      "documentation": "Converts enumeration values to and from strings."
    },
    {
      "id": "T:System.Text.Json.Serialization.JsonStringEnumConverter`1",
      "definition_kind": "class",
      "constructor_accessibility": "public",
      "required_overrides": [],
      "documentation": "Converter to convert enums to and from strings."
    }
  ],
  "attribute_members": [
    {
      "name": "#ctor",
      "signature": "JsonConverterAttribute(System.Type converterType)",
      "is_abstract": false,
      "accessibility": "public"
    },
    {
      "name": "#ctor",
      "signature": "JsonConverterAttribute()",
      "is_abstract": false,
      "accessibility": "protected"
    },
    {
      "name": "CreateConverter",
      "signature": "System.Text.Json.Serialization.JsonConverter? CreateConverter(System.Type typeToConvert)",
      "is_abstract": false,
      "accessibility": "public"
    },
    {
      "name": "ConverterType",
      "signature": "System.Type? ConverterType { get; }",
      "is_abstract": false,
      "accessibility": "public"
    }
  ]
}
<shellId: 9 completed with exit code 0>
```

</details>


---

<sub>20s</sub>

### `bash`

**List source links and lines for Stream Deserialize overloads**

`$ jq -r '.members[] | select(.declaring_type=="T:System.Text.Json.JsonSerializer" and .name=="Deserialize" and any(.parameters[]?; .type=="System.IO.Stream")) | [.display_signature,.source.url,(.source.line|tostring),.documentation_id] | @tsv' System.Text.Json.symbols.json`

<details>
<summary>6 lines</summary>

```
TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null)	https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs	80	M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions)
object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)	https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs	151	M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions)
TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo)	https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs	238	M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})
object? Deserialize(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)	https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs	263	M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo)
object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)	https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs	340	M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.Serialization.JsonSerializerContext)
<shellId: 10 completed with exit code 0>
```

</details>


---

<sub>24s</sub>

### `bash`

**Classify annotated names by existence of unmarked overloads**

`$ jq -r '. as $root | .attribute_type_members["T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"] as $ids | [.members[] | select(.documentation_id as $id | $ids | index($id))] | group_by(.declaring_type,.name) | .[] | .[0] as $g | [$g.declaring_type, $g.name, (length|tostring), ([$root.members[] | select(.declaring_type==$g.declaring_type and .name==$g.name and (.documentation_id as $id | $ids | index($id) | not))] | length | tostring)] | @tsv' System.Text.Json.symbols.json`

<details>
<summary>21 lines</summary>

```
T:System.Text.Json.JsonSerializer	Deserialize	16	24
T:System.Text.Json.JsonSerializer	DeserializeAsync	4	6
T:System.Text.Json.JsonSerializer	DeserializeAsyncEnumerable	4	4
T:System.Text.Json.JsonSerializer	Serialize	6	9
T:System.Text.Json.JsonSerializer	SerializeAsync	4	6
T:System.Text.Json.JsonSerializer	SerializeToDocument	2	3
T:System.Text.Json.JsonSerializer	SerializeToElement	2	3
T:System.Text.Json.JsonSerializer	SerializeToNode	2	3
T:System.Text.Json.JsonSerializer	SerializeToUtf8Bytes	2	3
T:System.Text.Json.JsonSerializerOptions	Default	1	0
T:System.Text.Json.JsonSerializerOptions	GetConverter	1	0
T:System.Text.Json.JsonSerializerOptions	MakeReadOnly	1	1
T:System.Text.Json.JsonSerializerOptions	Strict	1	0
T:System.Text.Json.JsonSerializerOptions	Web	1	0
T:System.Text.Json.Nodes.JsonArray	Add	1	1
T:System.Text.Json.Nodes.JsonNode	ReplaceWith	1	0
T:System.Text.Json.Nodes.JsonValue	Create	1	36
T:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver	#ctor	1	0
T:System.Text.Json.Serialization.Metadata.JsonTypeInfo	CreateJsonPropertyInfo	1	0
T:System.Text.Json.Serialization.Metadata.JsonTypeInfo	CreateJsonTypeInfo	2	0
<shellId: 11 completed with exit code 0>
```

</details>


---

<sub>28s</sub>

### `bash`

**Verify total counts and type-level trimming annotations**

`$ jq '{marked_types:[.types|to_entries[]|select(.value.attribute_types|index("T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"))|.key],marked_members:(.attribute_type_members["T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"]|length),all_members:(.members|length),stream_count:(.parameter_type_members["T:System.IO.Stream"]|length),document_produced_count:(.produced_type_members["T:System.Text.Json.JsonDocument"]|length)}' System.Text.Json.symbols.json`

<details>
<summary>8 lines</summary>

```
{
  "marked_types": [],
  "marked_members": 54,
  "all_members": 975,
  "stream_count": 30,
  "document_produced_count": 13
}
<shellId: 12 completed with exit code 0>
```

</details>


---

<sub>28s</sub>

### `bash`

**Inspect same-named unannotated examples per group**

`$ jq -r '. as $root | .attribute_type_members["T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"] as $ids | [.members[] | select(.documentation_id as $id | $ids | index($id))] | group_by(.declaring_type,.name) | .[] | .[0] as $g | [$g.declaring_type, $g.name, (([$root.members[] | select(.declaring_type==$g.declaring_type and .name==$g.name and (.documentation_id as $id | $ids | index($id) | not))] | map(.display_signature)) | join(" | "))] | @tsv' System.Text.Json.symbols.json | cut -c 1-600`

<details>
<summary>21 lines</summary>

```
T:System.Text.Json.JsonSerializer	Deserialize	TValue? Deserialize<TValue>(System.Text.Json.JsonDocument document, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo) | object? Deserialize(System.Text.Json.JsonDocument document, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo) | object? Deserialize(System.Text.Json.JsonDocument document, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context) | TValue? Deserialize<TValue>(System.Text.Json.JsonElement element, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonT
T:System.Text.Json.JsonSerializer	DeserializeAsync	System.Threading.Tasks.ValueTask<TValue?> DeserializeAsync<TValue>(System.IO.Pipelines.PipeReader utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default) | System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Pipelines.PipeReader utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default) | System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.I
T:System.Text.Json.JsonSerializer	DeserializeAsyncEnumerable	System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Pipelines.PipeReader utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default) | System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Pipelines.PipeReader utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo, bool topLevelValues, System.Threading.CancellationToken cancellat
T:System.Text.Json.JsonSerializer	Serialize	void Serialize<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo) | void Serialize(System.IO.Stream utf8Json, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo) | void Serialize(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context) | string Serialize<TValue>(TValue value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo) | string Serialize(object? valu
T:System.Text.Json.JsonSerializer	SerializeAsync	System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default) | System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default) | System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Type inp
T:System.Text.Json.JsonSerializer	SerializeToDocument	System.Text.Json.JsonDocument SerializeToDocument<TValue>(TValue value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo) | System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo) | System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
T:System.Text.Json.JsonSerializer	SerializeToElement	System.Text.Json.JsonElement SerializeToElement<TValue>(TValue value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo) | System.Text.Json.JsonElement SerializeToElement(object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo) | System.Text.Json.JsonElement SerializeToElement(object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
T:System.Text.Json.JsonSerializer	SerializeToNode	System.Text.Json.Nodes.JsonNode? SerializeToNode<TValue>(TValue value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo) | System.Text.Json.Nodes.JsonNode? SerializeToNode(object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo) | System.Text.Json.Nodes.JsonNode? SerializeToNode(object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
T:System.Text.Json.JsonSerializer	SerializeToUtf8Bytes	byte[] SerializeToUtf8Bytes<TValue>(TValue value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo) | byte[] SerializeToUtf8Bytes(object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo) | byte[] SerializeToUtf8Bytes(object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
T:System.Text.Json.JsonSerializerOptions	Default	
T:System.Text.Json.JsonSerializerOptions	GetConverter	
T:System.Text.Json.JsonSerializerOptions	MakeReadOnly	void MakeReadOnly()
T:System.Text.Json.JsonSerializerOptions	Strict	
T:System.Text.Json.JsonSerializerOptions	Web	
T:System.Text.Json.Nodes.JsonArray	Add	void Add(System.Text.Json.Nodes.JsonNode? item)
T:System.Text.Json.Nodes.JsonNode	ReplaceWith	
T:System.Text.Json.Nodes.JsonValue	Create	System.Text.Json.Nodes.JsonValue Create(bool value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null) | System.Text.Json.Nodes.JsonValue? Create(System.Nullable<bool> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null) | System.Text.Json.Nodes.JsonValue Create(byte value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null) | System.Text.Json.Nodes.JsonValue? Create(System.Nullable<byte> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null) | System.Text.Json.N
T:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver	#ctor	
T:System.Text.Json.Serialization.Metadata.JsonTypeInfo	CreateJsonPropertyInfo	
T:System.Text.Json.Serialization.Metadata.JsonTypeInfo	CreateJsonTypeInfo
<shellId: 13 completed with exit code 0>
```

</details>


---

<sub>41s</sub>

### `bash`

**Verify externally derivable converter required method signatures**

`$ jq -r '. as $root | .types | to_entries[] | select(.value.name|test("Converter")) | select(.value.constructor_accessibility=="public" or .value.constructor_accessibility=="protected" or .value.constructor_accessibility=="protected_internal") | select(.value.is_sealed|not) | . as $t | $t.value.required_overrides[]? | . as $id | $root.members[] | select(.documentation_id==$id) | [$t.value.name, .display_signature, .accessibility] | @tsv' System.Text.Json.symbols.json`

```
JsonConverterFactory	bool CanConvert(System.Type typeToConvert)	public
JsonConverterFactory	System.Text.Json.Serialization.JsonConverter? CreateConverter(System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)	public
JsonConverter<T>	T? Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)	public
JsonConverter<T>	void Write(System.Text.Json.Utf8JsonWriter writer, T value, System.Text.Json.JsonSerializerOptions options)	public
<shellId: 14 completed with exit code 0>
```


---

<sub>53s</sub>

### Copilot

The lookups identify 30 Stream-parameter APIs, 13 JsonDocument-producing APIs, and 54 trimming-annotated members. I’ll include the individual signatures and group the annotations by declaring type and name.

---

<sub>53s</sub>

### `apply_patch`

<details>
<summary>Arguments</summary>

```json
"*** Begin Patch\n*** Add File: make_answer.py\n+import json\n+from pathlib import Path\n+\n+\n+path = Path(\"System.Text.Json.symbols.json\")\n+data = json.loads(path.read_text())\n+members = data[\"members\"]\n+by_id = {member[\"documentation_id\"]: member for member in members}\n+types = data[\"types\"]\n+stream_ids = data[\"parameter_type_members\"][\"T:System.IO.Stream\"]\n+document_ids = data[\"produced_type_members\"][\"T:System.Text.Json.JsonDocument\"]\n+attribute_ids = data[\"attribute_type_members\"][\n+    \"T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute\"\n+]\n+assert (len(stream_ids), len(document_ids), len(attribute_ids)) == (30, 13, 54)\n+\n+\n+def type_name(member):\n+    return member[\"declaring_type\"].removeprefix(\"T:\")\n+\n+\n+def signature(member):\n+    prefix = member[\"accessibility\"].replace(\"_\", \" \")\n+    if member[\"is_static\"]:\n+        prefix += \" static\"\n+    return f'{prefix} {member[\"display_signature\"]}'\n+\n+\n+def api(member):\n+    return {\n+        \"declaring_type\": type_name(member),\n+        \"signature\": signature(member),\n+        \"documentation_id\": member[\"documentation_id\"],\n+    }\n+\n+\n+stream_apis = [api(by_id[identifier]) for identifier in stream_ids]\n+document_apis = [api(by_id[identifier]) for identifier in document_ids]\n+marked_groups = {}\n+for identifier in attribute_ids:\n+    member = by_id[identifier]\n+    key = (type_name(member), member[\"name\"])\n+    marked_groups.setdefault(key, []).append(member)\n+\n+marked_apis = []\n+for (declaring_type, name), marked_members in sorted(marked_groups.items()):\n+    unmarked = [\n+        member\n+        for member in members\n+        if type_name(member) == declaring_type\n+        and member[\"name\"] == name\n+        and member[\"documentation_id\"] not in attribute_ids\n+    ]\n+    marked_apis.append(\n+        {\n+            \"declaring_type\": declaring_type,\n+            \"member_name\": name,\n+            \"marked_signatures\": [signature(member) for member in marked_members],\n+            \"same_named_unmarked_overload\": bool(unmarked),\n+            \"unmarked_example\": signature(unmarked[0]) if unmarked else None,\n+        }\n+    )\n+\n+parse_async = [\n+    member\n+    for member in members\n+    if member[\"declaring_type\"] == \"T:System.Text.Json.JsonDocument\"\n+    and member[\"name\"] == \"ParseAsync\"\n+]\n+assert len(parse_async) == 1\n+parse_async = parse_async[0]\n+\n+converter_types = []\n+for identifier in data[\"name_word_types\"][\"Converter\"]:\n+    converter = types[identifier]\n+    externally_derivable = (\n+        not converter[\"is_sealed\"]\n+        and not converter[\"is_static\"]\n+        and converter[\"constructor_accessibility\"]\n+        in {\"public\", \"protected\", \"protected_internal\"}\n+    )\n+    converter_types.append(\n+        {\n+            \"type\": identifier.removeprefix(\"T:\"),\n+            \"externally_derivable\": externally_derivable,\n+            \"required_overrides\": (\n+                [signature(by_id[member_id]) for member_id in converter[\"required_overrides\"]]\n+                if externally_derivable\n+                else None\n+            ),\n+            \"reason_if_not_derivable\": (\n+                \"sealed\"\n+                if converter[\"is_sealed\"]\n+                else \"constructor is internal\"\n+                if converter[\"constructor_accessibility\"] == \"internal\"\n+                else None\n+            ),\n+        }\n+    )\n+assert len(converter_types) == 7\n+\n+deserialize_stream = [\n+    member\n+    for member in members\n+    if member[\"declaring_type\"] == \"T:System.Text.Json.JsonSerializer\"\n+    and member[\"name\"] == \"Deserialize\"\n+    and any(parameter[\"type\"] == \"System.IO.Stream\" for parameter in member[\"parameters\"])\n+]\n+assert len(deserialize_stream) == 5\n+source_locations = [\n+    {\n+        \"signature\": signature(member),\n+        \"file_url\": member[\"source\"][\"url\"],\n+        \"line\": member[\"source\"][\"line\"],\n+        \"line_url\": f'{member[\"source\"][\"url\"]}#L{member[\"source\"][\"line\"]}',\n+    }\n+    for member in deserialize_stream\n+]\n+\n+answers = [\n+    {\n+        \"question\": 1,\n+        \"answer\": stream_apis,\n+        \"evidence\": [\n+            \"System.Text.Json.symbols.json: library, request.admission, section_outcomes, parameter_type_members['T:System.IO.Stream'], members[].display_signature/accessibility/is_static\",\n+            \"jq '.parameter_type_members[\\\"T:System.IO.Stream\\\"] as $ids | .members[] | select(.documentation_id as $id | $ids | index($id)) | [.declaring_type,.display_signature,.documentation_id]' System.Text.Json.symbols.json\",\n+        ],\n+        \"complete\": True,\n+        \"why\": \"The declarations section is complete and the Stream parameter index yields all 30 admitted public/protected APIs.\",\n+    },\n+    {\n+        \"question\": 2,\n+        \"answer\": document_apis,\n+        \"evidence\": [\n+            \"System.Text.Json.symbols.json: produced_type_members['T:System.Text.Json.JsonDocument'], members[].return_type/parameters/display_signature\",\n+            \"jq '.produced_type_members[\\\"T:System.Text.Json.JsonDocument\\\"]' System.Text.Json.symbols.json\",\n+        ],\n+        \"complete\": True,\n+        \"why\": \"All 13 document-producing entries are included, including TryParseValue's nullable out parameter and ParseAsync's Task result; no ValueTask-producing entry occurs in the complete index.\",\n+    },\n+    {\n+        \"question\": 3,\n+        \"answer\": marked_apis,\n+        \"evidence\": [\n+            \"System.Text.Json.symbols.json: attribute_type_members['T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute'], members[].attribute_types/name/declaring_type/display_signature\",\n+            \"jq '.attribute_type_members[\\\"T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute\\\"] | length' System.Text.Json.symbols.json\",\n+        ],\n+        \"complete\": True,\n+        \"why\": \"All 54 annotated members are grouped by declaring type and member name; same-named unmarked members are compared against the full public/protected member list. No admitted types carry the attribute.\",\n+    },\n+    {\n+        \"question\": 4,\n+        \"answer\": {\n+            \"declaring_type\": type_name(parse_async),\n+            \"signature\": signature(parse_async),\n+            \"summary\": parse_async[\"documentation\"][\"summary\"],\n+        },\n+        \"evidence\": [\n+            \"System.Text.Json.symbols.json: members['M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)'].display_signature/is_static/accessibility/documentation.summary\"\n+        ],\n+        \"complete\": True,\n+        \"why\": \"There is one ParseAsync overload on JsonDocument and its documentation summary has complete status.\",\n+    },\n+    {\n+        \"question\": 5,\n+        \"answer\": converter_types,\n+        \"evidence\": [\n+            \"System.Text.Json.symbols.json: name_word_types.Converter, types[...].constructor_accessibility/is_sealed/required_overrides/inaccessible_required_overrides, members[...].display_signature\",\n+            \"jq '.name_word_types.Converter' System.Text.Json.symbols.json\",\n+        ],\n+        \"complete\": True,\n+        \"why\": \"All seven public converter-named types are listed; accessibility of constructors and sealing determines external derivability, and required_overrides supplies the compulsory overrides.\",\n+    },\n+    {\n+        \"question\": 6,\n+        \"answer\": source_locations,\n+        \"evidence\": [\n+            \"System.Text.Json.symbols.json: members[] where declaring_type=='T:System.Text.Json.JsonSerializer', name=='Deserialize', and parameters[].type=='System.IO.Stream'; source.url and source.line\",\n+            *[location[\"line_url\"] for location in source_locations],\n+        ],\n+        \"complete\": True,\n+        \"why\": \"All five synchronous Deserialize Stream overloads have complete source-link locations; DeserializeAsync is a differently named method.\",\n+    },\n+]\n+\n+result = {\n+    \"answers\": answers,\n+    \"friction\": \"The symbol document made API enumeration and source lookup direct. Its display signatures omit public/static modifiers, so those were reconstructed from their explicit member fields.\",\n+}\n+Path(\"answer.json\").write_text(json.dumps(result, indent=2, ensure_ascii=False) + \"\\n\")\n*** End Patch\n"
```

</details>

```
Added 1 file(s): /private/tmp/cli-trial-20261106/document-2/make_answer.py
```


---

<sub>56s</sub>

### `bash`

**Generate answer JSON and review counts and edge cases**

`$ python3 make_answer.py && jq '{questions:[.answers[]|{question,items:(.answer|if type=="array" then length else 1 end),complete}],q2_out:[.answers[1].answer[]|select(.signature|contains("out "))],q4:.answers[3].answer,q5:.answers[4].answer,q6:[.answers[5].answer[]|{line,signature}]}' answer.json`

<details>
<summary>119 lines</summary>

```
{
  "questions": [
    {
      "question": 1,
      "items": 30,
      "complete": true
    },
    {
      "question": 2,
      "items": 13,
      "complete": true
    },
    {
      "question": 3,
      "items": 20,
      "complete": true
    },
    {
      "question": 4,
      "items": 1,
      "complete": true
    },
    {
      "question": 5,
      "items": 7,
      "complete": true
    },
    {
      "question": 6,
      "items": 5,
      "complete": true
    }
  ],
  "q2_out": [
    {
      "declaring_type": "System.Text.Json.JsonDocument",
      "signature": "public static bool TryParseValue(ref System.Text.Json.Utf8JsonReader reader, out System.Text.Json.JsonDocument? document)",
      "documentation_id": "M:System.Text.Json.JsonDocument.TryParseValue(System.Text.Json.Utf8JsonReader@,System.Text.Json.JsonDocument@)"
    }
  ],
  "q4": {
    "declaring_type": "System.Text.Json.JsonDocument",
    "signature": "public static System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ParseAsync(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default, System.Threading.CancellationToken cancellationToken = default)",
    "summary": "Parses a Stream as UTF-8-encoded data representing a single JSON value into a JsonDocument. The stream is read to completion."
  },
  "q5": [
    {
      "type": "System.Text.Json.Serialization.JsonConverter",
      "externally_derivable": false,
      "required_overrides": null,
      "reason_if_not_derivable": "constructor is internal"
    },
    {
      "type": "System.Text.Json.Serialization.JsonConverterAttribute",
      "externally_derivable": true,
      "required_overrides": [],
      "reason_if_not_derivable": null
    },
    {
      "type": "System.Text.Json.Serialization.JsonConverterFactory",
      "externally_derivable": true,
      "required_overrides": [
        "public bool CanConvert(System.Type typeToConvert)",
        "public System.Text.Json.Serialization.JsonConverter? CreateConverter(System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)"
      ],
      "reason_if_not_derivable": null
    },
    {
      "type": "System.Text.Json.Serialization.JsonConverter`1",
      "externally_derivable": true,
      "required_overrides": [
        "public T? Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)",
        "public void Write(System.Text.Json.Utf8JsonWriter writer, T value, System.Text.Json.JsonSerializerOptions options)"
      ],
      "reason_if_not_derivable": null
    },
    {
      "type": "System.Text.Json.Serialization.JsonNumberEnumConverter`1",
      "externally_derivable": false,
      "required_overrides": null,
      "reason_if_not_derivable": "sealed"
    },
    {
      "type": "System.Text.Json.Serialization.JsonStringEnumConverter",
      "externally_derivable": true,
      "required_overrides": [],
      "reason_if_not_derivable": null
    },
    {
      "type": "System.Text.Json.Serialization.JsonStringEnumConverter`1",
      "externally_derivable": true,
      "required_overrides": [],
      "reason_if_not_derivable": null
    }
  ],
  "q6": [
    {
      "line": 80,
      "signature": "public static TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null)"
    },
    {
      "line": 151,
      "signature": "public static object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)"
    },
    {
      "line": 238,
      "signature": "public static TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo)"
    },
    {
      "line": 263,
      "signature": "public static object? Deserialize(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)"
    },
    {
      "line": 340,
      "signature": "public static object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)"
    }
  ]
}
<shellId: 15 completed with exit code 0>
```

</details>


---

<sub>1m 3s</sub>

### `apply_patch`

<details>
<summary>Arguments</summary>

```json
"*** Begin Patch\n*** Update File: make_answer.py\n@@\n     converter_types.append(\n         {\n-            \"type\": identifier.removeprefix(\"T:\"),\n+            \"type\": f'{converter[\"namespace\"]}.{converter[\"name\"]}',\n@@\n source_locations = [\n     {\n         \"signature\": signature(member),\n         \"file_url\": member[\"source\"][\"url\"],\n         \"line\": member[\"source\"][\"line\"],\n-        \"line_url\": f'{member[\"source\"][\"url\"]}#L{member[\"source\"][\"line\"]}',\n+        \"line_url\": (\n+            member[\"source\"][\"url\"].replace(\n+                \"https://raw.githubusercontent.com/dotnet/dotnet/\",\n+                \"https://github.com/dotnet/dotnet/blob/\",\n+            )\n+            + f'#L{member[\"source\"][\"line\"]}'\n+        ),\n*** End Patch\n"
```

</details>

```
Modified 1 file(s): /private/tmp/cli-trial-20261106/document-2/make_answer.py
```


---

<sub>1m 9s</sub>

### `bash`

**Regenerate and validate answer completeness and JSON structure**

`$ python3 make_answer.py && jq -e '(.answers|length)==6 and ([.answers[].complete]|all) and (.answers[0].answer|length)==30 and (.answers[1].answer|length)==13 and (.answers[2].answer|length)==20 and ([.answers[2].answer[].marked_signatures[]]|length)==54 and (.answers[4].answer|length)==7 and (.answers[5].answer|length)==5 and ([.answers[5].answer[]|select(.line_url|contains("/blob/"))]|length)==5' answer.json >/dev/null && jq -r '.answers[2].answer[] | [.declaring_type,.member_name,(.marked_signatures|length),.same_named_unmarked_overload] | @tsv' answer.json && ls -lh answer.json`

<details>
<summary>22 lines</summary>

```
System.Text.Json.JsonSerializer	Deserialize	16	true
System.Text.Json.JsonSerializer	DeserializeAsync	4	true
System.Text.Json.JsonSerializer	DeserializeAsyncEnumerable	4	true
System.Text.Json.JsonSerializer	Serialize	6	true
System.Text.Json.JsonSerializer	SerializeAsync	4	true
System.Text.Json.JsonSerializer	SerializeToDocument	2	true
System.Text.Json.JsonSerializer	SerializeToElement	2	true
System.Text.Json.JsonSerializer	SerializeToNode	2	true
System.Text.Json.JsonSerializer	SerializeToUtf8Bytes	2	true
System.Text.Json.JsonSerializerOptions	Default	1	false
System.Text.Json.JsonSerializerOptions	GetConverter	1	false
System.Text.Json.JsonSerializerOptions	MakeReadOnly	1	true
System.Text.Json.JsonSerializerOptions	Strict	1	false
System.Text.Json.JsonSerializerOptions	Web	1	false
System.Text.Json.Nodes.JsonArray	Add	1	true
System.Text.Json.Nodes.JsonNode	ReplaceWith	1	false
System.Text.Json.Nodes.JsonValue	Create	1	true
System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver	#ctor	1	false
System.Text.Json.Serialization.Metadata.JsonTypeInfo	CreateJsonPropertyInfo	1	false
System.Text.Json.Serialization.Metadata.JsonTypeInfo	CreateJsonTypeInfo	2	false
-rw-r--r--@ 1 rich  wheel    45K Nov  6 15:00 answer.json
<shellId: 16 completed with exit code 0>
```

</details>


---

<sub>1m 13s</sub>

### `bash`

**Clean helper script and confirm final answer file**

`$ rm make_answer.py && jq -e 'has("answers") and has("friction") and ([.answers[] | has("evidence") and has("why") and has("complete")]|all)' answer.json >/dev/null && ls -l answer.json`

```
-rw-r--r--@ 1 rich  wheel  46169 Nov  6 15:00 answer.json
<shellId: 17 completed with exit code 0>
```


---

<sub>1m 14s</sub>

### Copilot

Wrote [answer.json](answer.json) with all six answers, per-answer evidence, completeness assessments, and the requested friction note.

---

<sub>Generated by [GitHub Copilot CLI](https://github.com/features/copilot/cli)</sub>
