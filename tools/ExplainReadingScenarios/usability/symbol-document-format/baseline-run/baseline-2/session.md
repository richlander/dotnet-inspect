# Copilot CLI Session

> [!NOTE]
> - **Session ID:** `be95b962-2c42-4549-8362-a5615e7fab31`  
> - **Started:** 11/6/2026, 2:59:39 PM  
> - **Duration:** 2m 3s  
> - **Exported:** 11/6/2026, 3:01:42 PM  

---

<sub>2s</sub>

### User

You are a fresh participant in a cooperative usability trial. Your scratch directory is /tmp/cli-trial-20261106/baseline-2; work only there. Do not read anything else on this machine outside your scratch directory and the .NET SDK, including repositories, designs, earlier results, or other trials. Do not spawn agents. This protocol is cooperative, not a security sandbox.

Guide:
You are working without any .NET inspection tool. You have a shell, the .NET SDK, Python, jq, and network access to nuget.org, the Microsoft symbol server, and source hosts such as GitHub. dotnet-inspect is not available: do not install, download, or invoke it, including through dnx.
Answer with evidence you obtain yourself, for example from the package files, its XML documentation, metadata you read with your own code, or the published source. Keep downloads and scripts in your scratch directory.
The user values low latency. Do only the work a question needs, stop when the evidence answers it, and say plainly when it does not.

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

<sub>8s</sub>

### Copilot

I’ll inspect the NuGet assembly and XML docs, then check the source links for the Stream overloads. I’ll keep all files in the scratch directory.

---

<sub>8s</sub>

### `bash`

**Inspect scratch directory and .NET SDK**

`$ pwd && ls -la && dotnet --info | head -35`

<details>
<summary>41 lines</summary>

```
/tmp/cli-trial-20261106/baseline-2
total 32
drwxr-xr-x@  7 rich  wheel   224 Nov  6 14:59 .
drwxr-xr-x@ 13 rich  wheel   416 Nov  6 14:59 ..
-rw-r--r--@  1 rich  wheel     0 Nov  6 14:59 err.txt
-rw-r--r--@  1 rich  wheel  2431 Nov  6 14:57 prompt.txt
-rw-r--r--@  1 rich  wheel   151 Nov  6 14:59 result.md
-rwxr-xr-x@  1 rich  wheel  1026 Nov  6 14:57 run.sh
-rw-r--r--@  1 rich  wheel    37 Nov  6 14:57 session-id
.NET SDK:
 Version:           11.0.100-rc.3.26522.148
 Commit:            5254d8bcf5
 Workload version:  11.0.100-manifests.f0f6d40b
 MSBuild version:   18.12.0-1.26522.148+5254d8bcf

Runtime Environment:
 OS Name:     Mac OS X
 OS Version:  27.2
 OS Platform: Darwin
 RID:         osx-arm64
 Base Path:   /usr/local/share/dotnet/sdk/11.0.100-rc.3.26522.148

.NET workloads installed:
There are no installed workloads to display.
Configured to use workload sets when installing new manifests.
No workload sets are installed. Run "dotnet workload restore" to install a workload set.

Host:
  Version:      11.0.0-rc.3.26522.148
  Architecture: arm64
  Commit:       5254d8bcf5

.NET SDKs installed:
  9.0.312 [/usr/local/share/dotnet/sdk]
  10.0.300 [/usr/local/share/dotnet/sdk]
  11.0.100-preview.1.26104.118 [/usr/local/share/dotnet/sdk]
  11.0.100-preview.2.26159.112 [/usr/local/share/dotnet/sdk]
  11.0.100-preview.3.26207.106 [/usr/local/share/dotnet/sdk]
  11.0.100-preview.4.26230.115 [/usr/local/share/dotnet/sdk]
  11.0.100-preview.5.26302.115 [/usr/local/share/dotnet/sdk]
  11.0.100-preview.6.26359.118 [/usr/local/share/dotnet/sdk]
  11.0.100-preview.7.26381.103 [/usr/local/share/dotnet/sdk]
  11.0.100-rc.1.26425.128 [/usr/local/share/dotnet/sdk]
  11.0.100-rc.3.26522.148 [/usr/local/share/dotnet/sdk]
<shellId: 0 completed with exit code 0>
```

</details>


---

<sub>8s</sub>

### `bash`

**Check NuGet package availability**

`$ curl -fsSI https://api.nuget.org/v3-flatcontainer/system.text.json/10.0.14/system.text.json.10.0.14.nupkg | head -12`

<details>
<summary>13 lines</summary>

```
HTTP/2 200 
date: Fri, 06 Nov 2026 22:59:47 GMT
content-type: application/octet-stream
content-length: 2223225
cache-control: public, max-age=86400
content-md5: je9wbWL+3B6yN3HBQXeEyQ==
last-modified: Sun, 25 Oct 2026 17:28:13 GMT
etag: 0x8DF32BD59149582
x-ms-request-id: c752db5f-101e-006c-0e42-6e5660000000
x-ms-version: 2009-09-19
x-ms-meta-da7b2905_0f3c_4262_921c_b1593d1336f1_esrp_requestid: 5d39b5cf-24cb-42a9-829e-4dd80211af7a
x-ms-meta-sha512: swrFDRqf2gCM/hIHRjbMnbD5bYeh2s9esC6RSE9Qr8uAyLqPqNVcqTO1Tns4WEkMQ2r7Lyz22Idh8kV/oUOJHw==
<shellId: 1 completed with exit code 0>
```

</details>


---

<sub>12s</sub>

### `bash`

**Download package and inspect XML documentation**

`$ curl -fsSL https://api.nuget.org/v3-flatcontainer/system.text.json/10.0.14/system.text.json.10.0.14.nupkg -o system.text.json.10.0.14.nupkg && mkdir -p package && unzip -q system.text.json.10.0.14.nupkg -d package && ls -l package/lib/net10.0 && grep -A8 -B3 '<repository' package/*.nuspec && rg -n 'M:System.Text.Json.JsonDocument.ParseAsync|M:System.Text.Json.JsonSerializer.Deserialize[^" ]*Stream|T:System.Text.Json.Serialization.*Converter' package/lib/net10.0/System.Text.Json.xml | head -65`

<details>
<summary>81 lines</summary>

```
total 2448
-rw-r--r--@ 1 rich  wheel  649040 Oct 23 05:45 System.Text.Json.dll
-rw-r--r--@ 1 rich  wheel  599660 Oct 23 05:45 System.Text.Json.xml
    <releaseNotes>https://go.microsoft.com/fwlink/?LinkID=799421</releaseNotes>
    <copyright>© Microsoft Corporation. All rights reserved.</copyright>
    <serviceable>true</serviceable>
    <repository type="git" url="https://github.com/dotnet/dotnet" commit="0427b9abbead3fa64c6655eb4290c537397131fa" />
    <dependencies>
      <group targetFramework=".NETFramework4.6.2">
        <dependency id="Microsoft.Bcl.AsyncInterfaces" version="10.0.14" exclude="Build,Analyzers" />
        <dependency id="System.IO.Pipelines" version="10.0.14" exclude="Build,Analyzers" />
        <dependency id="System.Text.Encodings.Web" version="10.0.14" exclude="Build,Analyzers" />
        <dependency id="System.Buffers" version="4.6.1" exclude="Build,Analyzers" />
        <dependency id="System.Memory" version="4.6.3" exclude="Build,Analyzers" />
        <dependency id="System.Runtime.CompilerServices.Unsafe" version="6.1.2" exclude="Build,Analyzers" />
90:    <member name="M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)">
902:    <member name="M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo)">
913:    <member name="M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions)">
922:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
925:    <member name="M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.Serialization.JsonSerializerContext)">
934:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
973:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
1006:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
1029:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
1089:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
1114:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
1141:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
1162:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
1185:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
1204:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
1227:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
1273:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
1277:    <member name="M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions)">
1286:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1289:    <member name="M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0})">
1298:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1324:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1341:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1364:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1406:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1418:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1434:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1444:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1456:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1466:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1478:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1508:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1541:        <para>There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</para>
1560:        <para>There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</para>
1567:    <member name="M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo,System.Threading.CancellationToken)">
1580:    <member name="M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)">
1601:    <member name="M:System.Text.Json.JsonSerializer.DeserializeAsync(System.IO.Stream,System.Type,System.Text.Json.Serialization.JsonSerializerContext,System.Threading.CancellationToken)">
1611:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
1631:        <para>There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</para>
1651:    <member name="M:System.Text.Json.JsonSerializer.DeserializeAsync``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)">
1672:    <member name="M:System.Text.Json.JsonSerializer.DeserializeAsync``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Threading.CancellationToken)">
1682:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1737:    <member name="M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Boolean,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)">
1751:    <member name="M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions,System.Threading.CancellationToken)">
1762:    <member name="M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Boolean,System.Threading.CancellationToken)">
1775:    <member name="M:System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable``1(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo{``0},System.Threading.CancellationToken)">
1806:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="inputType" />  or its serializable members.</exception>
1818:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="inputType" />  or its serializable members.</exception>
1847:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="inputType" /> or its serializable members.</exception>
1885:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="inputType" /> or its serializable members.</exception>
1901:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1914:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1924:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1944:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
1970:          There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="inputType" />  or its serializable members.</exception>
1984:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="inputType" />  or its serializable members.</exception>
2026:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="inputType" />  or its serializable members.</exception>
2039:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
2075:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
2098:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="inputType" />  or its serializable members.</exception>
2106:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="inputType" /> or its serializable members.</exception>
2117:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
2125:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <typeparamref name="TValue" /> or its serializable members.</exception>
2149:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="inputType" />  or its serializable members.</exception>
2157:      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="inputType" /> or its serializable members.</exception>
<shellId: 2 completed with exit code 0>
```

</details>


---

<sub>20s</sub>

### `bash`

**Read ParseAsync documentation and converter type names**

`$ sed -n '55,112p' package/lib/net10.0/System.Text.Json.xml && sed -n '875,943p' package/lib/net10.0/System.Text.Json.xml && rg -n '<member name="T:System.Text.Json.*Converter[^" ]*"' package/lib/net10.0/System.Text.Json.xml`

```
<paramref name="utf8Json" /> does not represent a valid single JSON value.</exception>
      <exception cref="T:System.ArgumentException">
        <paramref name="options" /> contains unsupported options.</exception>
      <returns>A JsonDocument representation of the JSON value.</returns>
    </member>
    <member name="M:System.Text.Json.JsonDocument.Parse(System.ReadOnlyMemory{System.Byte},System.Text.Json.JsonDocumentOptions)">
      <summary>Parses memory as UTF-8-encoded text representing a single JSON value into a JsonDocument.</summary>
      <param name="utf8Json">The JSON text to parse.</param>
      <param name="options">Options to control the reader behavior during parsing.</param>
      <exception cref="T:System.Text.Json.JsonException">
        <paramref name="utf8Json" /> does not represent a valid single JSON value.</exception>
      <exception cref="T:System.ArgumentException">
        <paramref name="options" /> contains unsupported options.</exception>
      <returns>A JsonDocument representation of the JSON value.</returns>
    </member>
    <member name="M:System.Text.Json.JsonDocument.Parse(System.ReadOnlyMemory{System.Char},System.Text.Json.JsonDocumentOptions)">
      <summary>Parses text representing a single JSON value into a JsonDocument.</summary>
      <param name="json">The JSON text to parse.</param>
      <param name="options">Options to control the reader behavior during parsing.</param>
      <exception cref="T:System.Text.Json.JsonException">
        <paramref name="json" /> does not represent a valid single JSON value.</exception>
      <exception cref="T:System.ArgumentException">
        <paramref name="options" /> contains unsupported options.</exception>
      <returns>A JsonDocument representation of the JSON value.</returns>
    </member>
    <member name="M:System.Text.Json.JsonDocument.Parse(System.String,System.Text.Json.JsonDocumentOptions)">
      <summary>Parses text representing a single JSON string value into a JsonDocument.</summary>
      <param name="json">The JSON text to parse.</param>
      <param name="options">Options to control the reader behavior during parsing.</param>
      <exception cref="T:System.Text.Json.JsonException">
        <paramref name="json" /> does not represent a valid single JSON value.</exception>
      <exception cref="T:System.ArgumentException">
        <paramref name="options" /> contains unsupported options.</exception>
      <returns>A JsonDocument representation of the JSON value.</returns>
    </member>
    <member name="M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)">
      <summary>Parses a <see cref="T:System.IO.Stream" /> as UTF-8-encoded data representing a single JSON value into a JsonDocument. The stream is read to completion.</summary>
      <param name="utf8Json">The JSON data to parse.</param>
      <param name="options">Options to control the reader behavior during parsing.</param>
      <param name="cancellationToken">The token to monitor for cancellation requests.</param>
      <exception cref="T:System.Text.Json.JsonException">
        <paramref name="utf8Json" /> does not represent a valid single JSON value.</exception>
      <exception cref="T:System.ArgumentException">
        <paramref name="options" /> contains unsupported options.</exception>
      <exception cref="T:System.OperationCanceledException">The cancellation token was canceled. This exception is stored into the returned task.</exception>
      <returns>A task to produce a JsonDocument representation of the JSON value.</returns>
    </member>
    <member name="M:System.Text.Json.JsonDocument.ParseValue(System.Text.Json.Utf8JsonReader@)">
      <summary>Parses one JSON value (including objects or arrays) from the provided reader.</summary>
      <param name="reader">The reader to read.</param>
      <exception cref="T:System.ArgumentException">
        <paramref name="reader" /> contains unsupported options.

-or-

The current <paramref name="reader" /> token does not start or represent a value.</exception>
      <exception cref="T:System.Text.Json.JsonException">A value could not be read from the reader.</exception>
      <returns>A JsonDocument representing the value (and nested values) read from the reader.</returns>
        <see langword="true" /> if an extra comma is allowed; otherwise, <see langword="false" />.</returns>
    </member>
    <member name="P:System.Text.Json.JsonReaderOptions.CommentHandling">
      <summary>Gets or sets a value that determines how the <see cref="T:System.Text.Json.Utf8JsonReader" /> handles comments when reading through the JSON data.</summary>
      <exception cref="T:System.ArgumentOutOfRangeException">The property is being set to a value that is not a member of the <see cref="T:System.Text.Json.JsonCommentHandling" /> enumeration.</exception>
      <returns>One of the enumeration values that indicates how comments are handled.</returns>
    </member>
    <member name="P:System.Text.Json.JsonReaderOptions.MaxDepth">
      <summary>Gets or sets the maximum depth allowed when reading JSON, with the default (that is, 0) indicating a maximum depth of 64.</summary>
      <exception cref="T:System.ArgumentOutOfRangeException">The maximum depth is being set to a negative value.</exception>
      <returns>The maximum depth allowed when reading JSON.</returns>
    </member>
    <member name="T:System.Text.Json.JsonReaderState">
      <summary>Defines an opaque type that holds and saves all the relevant state information, which must be provided to the <see cref="T:System.Text.Json.Utf8JsonReader" /> to continue reading after processing incomplete data.</summary>
    </member>
    <member name="M:System.Text.Json.JsonReaderState.#ctor(System.Text.Json.JsonReaderOptions)">
      <summary>Constructs a new <see cref="T:System.Text.Json.JsonReaderState" /> instance.</summary>
      <param name="options">Defines the customized behavior of the <see cref="T:System.Text.Json.Utf8JsonReader" /> that is different from the JSON RFC (for example how to handle comments, or the maximum depth allowed when reading). By default, the <see cref="T:System.Text.Json.Utf8JsonReader" /> follows the JSON RFC strictly (comments within the JSON are invalid) and reads up to a maximum depth of 64.</param>
      <exception cref="T:System.ArgumentException">The maximum depth is set to a non-positive value (&lt; 0).</exception>
    </member>
    <member name="P:System.Text.Json.JsonReaderState.Options">
      <summary>Gets the custom behavior to use when reading JSON data using the <see cref="T:System.Text.Json.Utf8JsonReader" /> struct that may deviate from strict adherence to the JSON specification, which is the default behavior.</summary>
      <returns>The custom behavior to use when reading JSON data.</returns>
    </member>
    <member name="T:System.Text.Json.JsonSerializer">
      <summary>Provides functionality to serialize objects or value types to JSON and to deserialize JSON into objects or value types.</summary>
    </member>
    <member name="M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Text.Json.Serialization.Metadata.JsonTypeInfo)">
      <summary>Reads the UTF-8 encoded text representing a single JSON value into an instance specified by the <paramref name="jsonTypeInfo" />.
 The Stream will be read to completion.</summary>
      <param name="utf8Json">JSON data to parse.</param>
      <param name="jsonTypeInfo">Metadata about the type to convert.</param>
      <exception cref="T:System.ArgumentNullException">
        <paramref name="utf8Json" /> or <paramref name="jsonTypeInfo" /> is <see langword="null" />.</exception>
      <exception cref="T:System.Text.Json.JsonException">The JSON is invalid,
            or there is remaining data in the Stream.</exception>
      <returns>A <paramref name="jsonTypeInfo" /> representation of the JSON value.</returns>
    </member>
    <member name="M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.JsonSerializerOptions)">
      <summary>Reads the UTF-8 encoded text representing a single JSON value into a <paramref name="returnType" />.
            The Stream will be read to completion.</summary>
      <param name="utf8Json">JSON data to parse.</param>
      <param name="returnType">The type of the object to convert to and return.</param>
      <param name="options">Options to control the behavior during reading.</param>
      <exception cref="T:System.ArgumentNullException">
        <paramref name="utf8Json" /> or <paramref name="returnType" /> is <see langword="null" />.</exception>
      <exception cref="T:System.Text.Json.JsonException">The JSON is invalid, the <paramref name="returnType" /> is not compatible with the JSON, or there is remaining data in the Stream.</exception>
      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
      <returns>A <paramref name="returnType" /> representation of the JSON value.</returns>
    </member>
    <member name="M:System.Text.Json.JsonSerializer.Deserialize(System.IO.Stream,System.Type,System.Text.Json.Serialization.JsonSerializerContext)">
      <summary>Reads the UTF-8 encoded text representing a single JSON value into a <paramref name="returnType" />.
            The Stream will be read to completion.</summary>
      <param name="utf8Json">JSON data to parse.</param>
      <param name="returnType">The type of the object to convert to and return.</param>
      <param name="context">A metadata provider for serializable types.</param>
      <exception cref="T:System.ArgumentNullException">
        <paramref name="utf8Json" />, <paramref name="returnType" />, or <paramref name="context" /> is <see langword="null" />.</exception>
      <exception cref="T:System.Text.Json.JsonException">The JSON is invalid, the <paramref name="returnType" /> is not compatible with the JSON, or there is remaining data in the Stream.</exception>
      <exception cref="T:System.NotSupportedException">There is no compatible <see cref="T:System.Text.Json.Serialization.JsonConverter" /> for <paramref name="returnType" /> or its serializable members.</exception>
      <exception cref="T:System.InvalidOperationException">The <see cref="M:System.Text.Json.Serialization.JsonSerializerContext.GetTypeInfo(System.Type)" /> method on the provided <paramref name="context" /> did not return a compatible <see cref="T:System.Text.Json.Serialization.Metadata.JsonTypeInfo" /> for <paramref name="returnType" />.</exception>
      <returns>A <paramref name="returnType" /> representation of the JSON value.</returns>
    </member>
    <member name="M:System.Text.Json.JsonSerializer.Deserialize(System.ReadOnlySpan{System.Byte},System.Text.Json.Serialization.Metadata.JsonTypeInfo)">
      <summary>Parses the UTF-8 encoded text representing a single JSON value into an instance specified by the <paramref name="jsonTypeInfo" />.</summary>
      <param name="utf8Json">JSON text to parse.</param>
      <param name="jsonTypeInfo">Metadata about the type to convert.</param>
      <exception cref="T:System.Text.Json.JsonException">The JSON is invalid,
            or there is remaining data in the buffer.</exception>
3806:    <member name="T:System.Text.Json.Serialization.JsonConverter">
3818:    <member name="T:System.Text.Json.Serialization.JsonConverter`1">
3863:    <member name="T:System.Text.Json.Serialization.JsonConverterAttribute">
3882:    <member name="T:System.Text.Json.Serialization.JsonConverterFactory">
3996:    <member name="T:System.Text.Json.Serialization.JsonNumberEnumConverter`1">
4249:    <member name="T:System.Text.Json.Serialization.JsonStringEnumConverter">
4273:    <member name="T:System.Text.Json.Serialization.JsonStringEnumConverter`1">
<shellId: 3 completed with exit code 0>
```


---

<sub>20s</sub>

### `bash`

**Retrieve matching published stream-deserialization source**

`$ curl -fsSL 'https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonSerializer.Read.Stream.cs' -o JsonSerializer.Read.Stream.cs && rg -n 'Deserialize(<|\()|DeserializeAsync|public static' JsonSerializer.Read.Stream.cs | head -100`

<details>
<summary>26 lines</summary>

```
16:    public static partial class JsonSerializer
43:        public static ValueTask<TValue?> DeserializeAsync<TValue>(
51:            return jsonTypeInfo.DeserializeAsync(utf8Json, cancellationToken);
76:        public static TValue? Deserialize<TValue>(
83:            return jsonTypeInfo.Deserialize(utf8Json);
111:        public static ValueTask<object?> DeserializeAsync(
146:        public static object? Deserialize(
177:        public static ValueTask<TValue?> DeserializeAsync<TValue>(
186:            return jsonTypeInfo.DeserializeAsync(utf8Json, cancellationToken);
206:        public static ValueTask<object?> DeserializeAsync(
234:        public static TValue? Deserialize<TValue>(
242:            return jsonTypeInfo.Deserialize(utf8Json);
259:        public static object? Deserialize(
297:        public static ValueTask<object?> DeserializeAsync(
335:        public static object? Deserialize(
362:        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
367:            return DeserializeAsyncEnumerable<TValue>(utf8Json, topLevelValues: false, options, cancellationToken);
391:        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
400:            return DeserializeAsyncEnumerableCore(utf8Json, jsonTypeInfo, topLevelValues, cancellationToken);
415:        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
420:            return DeserializeAsyncEnumerable(utf8Json, jsonTypeInfo, topLevelValues: false, cancellationToken);
442:        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
452:            return DeserializeAsyncEnumerableCore(utf8Json, jsonTypeInfo, topLevelValues, cancellationToken);
455:        private static IAsyncEnumerable<T?> DeserializeAsyncEnumerableCore<T>(
497:                        success = listTypeInfo.ContinueDeserialize<StreamReadBufferState, Stream>(
<shellId: 4 completed with exit code 0>
```

</details>


---

<sub>25s</sub>

### Copilot

The package identifies a source commit. I’ll use that revision for the source citations and inspect the assembly metadata to catch overloads and attributes that XML docs alone might miss.

---

<sub>26s</sub>

### `bash`

**Scaffold small .NET metadata probe**

`$ dotnet new console -f net10.0 --no-restore -o probe --quiet && cat probe/probe.csproj`

<details>
<summary>7 lines</summary>

```
Error: Invalid option(s):
--quiet
   '--quiet' is not a valid option

For more information, run:
   dotnet new console -h

For details on the exit code, refer to https://aka.ms/templating-exit-codes#127
<shellId: 5 completed with exit code 127>
```

</details>


---

<sub>28s</sub>

### `bash`

**Create net10 console project**

`$ dotnet new console -f net10.0 --no-restore -o probe && cat probe/probe.csproj`

<details>
<summary>10 lines</summary>

```
The template "Console App" was created successfully.

﻿<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
<shellId: 6 completed with exit code 0>
```

</details>


---

<sub>40s</sub>

### `apply_patch`

<details>
<summary>Arguments</summary>

```json
"*** Begin Patch\n*** Delete File: probe/Program.cs\n*** Add File: probe/Program.cs\n+using System.Reflection;\n+using System.Runtime.Loader;\n+\n+var path = Path.GetFullPath(\"package/lib/net10.0/System.Text.Json.dll\");\n+var context = new AssemblyLoadContext(\"Package\", isCollectible: true);\n+var assembly = context.LoadFromAssemblyPath(path);\n+var types = assembly.GetExportedTypes();\n+var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;\n+var nullability = new NullabilityInfoContext();\n+\n+string TypeName(Type type, NullabilityInfo? info = null)\n+{\n+    if (type.IsByRef) return TypeName(type.GetElementType()!, info?.ElementType);\n+    if (type.IsArray) return TypeName(type.GetElementType()!, info?.ElementType) + \"[]\";\n+    var primitives = new Dictionary<Type, string>\n+    {\n+        [typeof(void)] = \"void\", [typeof(object)] = \"object\", [typeof(string)] = \"string\",\n+        [typeof(bool)] = \"bool\", [typeof(int)] = \"int\", [typeof(byte)] = \"byte\",\n+        [typeof(char)] = \"char\", [typeof(long)] = \"long\"\n+    };\n+    var name = primitives.GetValueOrDefault(type);\n+    if (name == null)\n+    {\n+        name = type.IsGenericParameter ? type.Name : type.FullName?.Split('`')[0].Replace('+', '.') ?? type.Name;\n+        if (type.IsGenericType)\n+            name += \"<\" + string.Join(\", \", type.GetGenericArguments().Select((t, i) =>\n+                TypeName(t, info?.GenericTypeArguments.ElementAtOrDefault(i)))) + \">\";\n+    }\n+    if (info?.ReadState == NullabilityState.Nullable && !type.IsValueType) name += \"?\";\n+    return name;\n+}\n+\n+string Signature(MethodBase method)\n+{\n+    var visibility = method.IsPublic ? \"public\" : method.IsFamily ? \"protected\" :\n+        method.IsFamilyOrAssembly ? \"protected internal\" : method.IsAssembly ? \"internal\" :\n+        method.IsFamilyAndAssembly ? \"private protected\" : \"private\";\n+    var modifiers = method.IsStatic ? \" static\" : method.IsAbstract ? \" abstract\" : method.IsVirtual ? \" virtual\" : \"\";\n+    var returnType = method is MethodInfo m ? TypeName(m.ReturnType, nullability.Create(m.ReturnParameter)) + \" \" : \"\";\n+    var name = method.IsConstructor ? method.DeclaringType!.Name : method.Name;\n+    if (method.IsGenericMethod)\n+        name += \"<\" + string.Join(\", \", method.GetGenericArguments().Select(t => t.Name)) + \">\";\n+    var args = string.Join(\", \", method.GetParameters().Select(p =>\n+    {\n+        var prefix = p.IsOut ? \"out \" : p.ParameterType.IsByRef ? \"ref \" : \"\";\n+        var formatted = prefix + TypeName(p.ParameterType, nullability.Create(p)) + \" \" + p.Name;\n+        if (p.IsOptional)\n+        {\n+            var value = p.DefaultValue;\n+            formatted += \" = \" + (value == null ? \"null\" : value is bool b ? (b ? \"true\" : \"false\") :\n+                value is string s ? \"\\\"\" + s + \"\\\"\" : value is CancellationToken ? \"default\" :\n+                value is Enum ? TypeName(value.GetType()) + \".\" + value : value.ToString());\n+        }\n+        return formatted;\n+    }));\n+    return visibility + modifiers + \" \" + returnType + name + \"(\" + args + \")\";\n+}\n+\n+bool IsPublicOrProtected(MethodBase m) => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;\n+bool IsRuc(MemberInfo m) => m.CustomAttributes.Any(a => a.AttributeType.FullName ==\n+    \"System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute\");\n+IEnumerable<MethodBase> Methods(Type t) =>\n+    t.GetMethods(flags).Cast<MethodBase>().Concat(t.GetConstructors(flags));\n+\n+Console.WriteLine(\"ASSEMBLY \" + assembly.FullName + \" \" + assembly.Location);\n+Console.WriteLine(\"TYPES \" + types.Length);\n+Console.WriteLine(\"\\n=== STREAM PUBLIC/PROTECTED ===\");\n+foreach (var m in types.SelectMany(Methods).Where(IsPublicOrProtected).Where(m =>\n+             m.GetParameters().Any(p => p.ParameterType.FullName == \"System.IO.Stream\"))\n+         .OrderBy(m => m.DeclaringType!.FullName).ThenBy(m => m.Name).ThenBy(Signature))\n+    Console.WriteLine(m.DeclaringType!.FullName + \" :: \" + Signature(m));\n+\n+Console.WriteLine(\"\\n=== JSONDOCUMENT RESULTS PUBLIC/PROTECTED ===\");\n+foreach (var m in types.SelectMany(Methods).Where(IsPublicOrProtected).Where(m =>\n+{\n+    if (m.GetParameters().Any(p => p.IsOut && p.ParameterType.GetElementType()?.FullName == \"System.Text.Json.JsonDocument\"))\n+        return true;\n+    if (m is not MethodInfo method) return false;\n+    var result = method.ReturnType;\n+    return result.FullName == \"System.Text.Json.JsonDocument\" ||\n+        (result.IsGenericType && (result.GetGenericTypeDefinition().FullName == \"System.Threading.Tasks.Task`1\" ||\n+                                  result.GetGenericTypeDefinition().FullName == \"System.Threading.Tasks.ValueTask`1\") &&\n+         result.GetGenericArguments()[0].FullName == \"System.Text.Json.JsonDocument\");\n+}).OrderBy(m => m.DeclaringType!.FullName).ThenBy(m => m.Name).ThenBy(Signature))\n+    Console.WriteLine(m.DeclaringType!.FullName + \" :: \" + Signature(m));\n+\n+Console.WriteLine(\"\\n=== RUC ALL DECLARED METHODS (including internal/private) ===\");\n+var all = assembly.GetTypes();\n+foreach (var group in all.SelectMany(Methods).Where(IsRuc).GroupBy(m => (m.DeclaringType!.FullName, m.Name))\n+             .OrderBy(g => g.Key.FullName).ThenBy(g => g.Key.Name))\n+{\n+    var method = group.First();\n+    var unmarked = Methods(method.DeclaringType!).Where(m => m.Name == method.Name && !IsRuc(m)).ToArray();\n+    Console.WriteLine($\"{group.Key.FullName} :: {group.Key.Name} count={group.Count()} same-named-unmarked={unmarked.Length}\");\n+    foreach (var m in group.OrderBy(Signature)) Console.WriteLine(\"  + \" + Signature(m));\n+    foreach (var m in unmarked.OrderBy(Signature)) Console.WriteLine(\"  - \" + Signature(m));\n+}\n+Console.WriteLine(\"\\n=== RUC TYPES ===\");\n+foreach (var t in all.Where(IsRuc)) Console.WriteLine(t.FullName);\n+\n+Console.WriteLine(\"\\n=== CONVERTER TYPES PUBLIC ===\");\n+foreach (var t in types.Where(t => t.Name.Contains(\"Converter\")).OrderBy(t => t.FullName))\n+{\n+    Console.WriteLine($\"{t.FullName} abstract={t.IsAbstract} sealed={t.IsSealed} base={t.BaseType?.FullName}\");\n+    Console.WriteLine(\"  constructors: \" + string.Join(\"; \", t.GetConstructors(flags).Where(IsPublicOrProtected).Select(Signature)));\n+    if (t.IsSealed || (!t.IsPublic && !t.IsNestedPublic)) continue;\n+    for (var baseType = t; baseType != null && baseType != typeof(object); baseType = baseType.BaseType)\n+        foreach (var m in baseType.GetMethods(flags).Where(m => m.IsAbstract && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly)))\n+            Console.WriteLine(\"  abstract \" + m.DeclaringType?.FullName + \" :: \" + Signature(m));\n+}\n*** End Patch\n"
```

</details>

```
Added 1 file(s): /private/tmp/cli-trial-20261106/baseline-2/probe/Program.cs
Deleted 1 file(s): /private/tmp/cli-trial-20261106/baseline-2/probe/Program.cs
```


---

<sub>45s</sub>

### `bash`

**Run assembly reflection probe and read first findings**

`$ dotnet run --project probe -f net10.0 > probe-output.txt && wc -l probe-output.txt && sed -n '1,115p' probe-output.txt`

<details>
<summary>114 lines</summary>

```
384 probe-output.txt
ASSEMBLY System.Text.Json, Version=10.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51 /private/tmp/cli-trial-20261106/baseline-2/package/lib/net10.0/System.Text.Json.dll
TYPES 84

=== STREAM PUBLIC/PROTECTED ===
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument Parse(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = null)
System.Text.Json.JsonDocument :: public static System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ParseAsync(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = null, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static object? Deserialize(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
System.Text.Json.JsonSerializer :: public static object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
System.Text.Json.JsonSerializer :: public static object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)
System.Text.Json.JsonSerializer :: public static TValue? Deserialize<TValue>(System.IO.Stream utf8Json, JsonTypeInfo`1<TValue?> jsonTypeInfo)
System.Text.Json.JsonSerializer :: public static TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static ValueTask`1<TValue?> DeserializeAsync<TValue>(System.IO.Stream utf8Json, JsonTypeInfo`1<TValue?> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static ValueTask`1<TValue?> DeserializeAsync<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static IAsyncEnumerable`1<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, bool topLevelValues, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static IAsyncEnumerable`1<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, JsonTypeInfo`1<TValue?> jsonTypeInfo, bool topLevelValues, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static IAsyncEnumerable`1<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, JsonTypeInfo`1<TValue?> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static IAsyncEnumerable`1<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static void Serialize(System.IO.Stream utf8Json, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
System.Text.Json.JsonSerializer :: public static void Serialize(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)
System.Text.Json.JsonSerializer :: public static void Serialize(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
System.Text.Json.JsonSerializer :: public static void Serialize<TValue>(System.IO.Stream utf8Json, TValue? value, JsonTypeInfo`1<TValue?> jsonTypeInfo)
System.Text.Json.JsonSerializer :: public static void Serialize<TValue>(System.IO.Stream utf8Json, TValue? value, System.Text.Json.JsonSerializerOptions? options = null)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Stream utf8Json, TValue? value, JsonTypeInfo`1<TValue?> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Stream utf8Json, TValue? value, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.Nodes.JsonNode :: public static System.Text.Json.Nodes.JsonNode? Parse(System.IO.Stream utf8Json, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> nodeOptions = null, System.Text.Json.JsonDocumentOptions documentOptions = null)
System.Text.Json.Nodes.JsonNode :: public static System.Threading.Tasks.Task<System.Text.Json.Nodes.JsonNode?> ParseAsync(System.IO.Stream utf8Json, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> nodeOptions = null, System.Text.Json.JsonDocumentOptions documentOptions = null, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.Utf8JsonWriter :: public Utf8JsonWriter(System.IO.Stream utf8Json, System.Text.Json.JsonWriterOptions options = null)
System.Text.Json.Utf8JsonWriter :: public void Reset(System.IO.Stream utf8Json)

=== JSONDOCUMENT RESULTS PUBLIC/PROTECTED ===
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument Parse(string json, System.Text.Json.JsonDocumentOptions options = null)
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument Parse(System.Buffers.ReadOnlySequence<byte> utf8Json, System.Text.Json.JsonDocumentOptions options = null)
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument Parse(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = null)
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument Parse(System.ReadOnlyMemory<byte> utf8Json, System.Text.Json.JsonDocumentOptions options = null)
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument Parse(System.ReadOnlyMemory<char> json, System.Text.Json.JsonDocumentOptions options = null)
System.Text.Json.JsonDocument :: public static System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ParseAsync(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = null, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument ParseValue(ref System.Text.Json.Utf8JsonReader reader)
System.Text.Json.JsonDocument :: public static bool TryParseValue(ref System.Text.Json.Utf8JsonReader reader, out System.Text.Json.JsonDocument document)
System.Text.Json.JsonSerializer :: public static System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
System.Text.Json.JsonSerializer :: public static System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)
System.Text.Json.JsonSerializer :: public static System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
System.Text.Json.JsonSerializer :: public static System.Text.Json.JsonDocument SerializeToDocument<TValue>(TValue? value, JsonTypeInfo`1<TValue?> jsonTypeInfo)
System.Text.Json.JsonSerializer :: public static System.Text.Json.JsonDocument SerializeToDocument<TValue>(TValue? value, System.Text.Json.JsonSerializerOptions? options = null)

=== RUC ALL DECLARED METHODS (including internal/private) ===
System.Text.Json.JsonSerializer :: Deserialize count=16 same-named-unmarked=24
  + public static object? Deserialize(ref System.Text.Json.Utf8JsonReader reader, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static object? Deserialize(string json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static object? Deserialize(System.ReadOnlySpan<byte> utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static object? Deserialize(System.ReadOnlySpan<char> json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static object? Deserialize(System.Text.Json.JsonDocument document, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static object? Deserialize(System.Text.Json.JsonElement element, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static object? Deserialize(System.Text.Json.Nodes.JsonNode? node, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static TValue? Deserialize<TValue>(ref System.Text.Json.Utf8JsonReader reader, System.Text.Json.JsonSerializerOptions? options = null)
  + public static TValue? Deserialize<TValue>(string json, System.Text.Json.JsonSerializerOptions? options = null)
  + public static TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null)
  + public static TValue? Deserialize<TValue>(System.ReadOnlySpan<byte> utf8Json, System.Text.Json.JsonSerializerOptions? options = null)
  + public static TValue? Deserialize<TValue>(System.ReadOnlySpan<char> json, System.Text.Json.JsonSerializerOptions? options = null)
  + public static TValue? Deserialize<TValue>(System.Text.Json.JsonDocument document, System.Text.Json.JsonSerializerOptions? options = null)
  + public static TValue? Deserialize<TValue>(System.Text.Json.JsonElement element, System.Text.Json.JsonSerializerOptions? options = null)
  + public static TValue? Deserialize<TValue>(System.Text.Json.Nodes.JsonNode? node, System.Text.Json.JsonSerializerOptions? options = null)
  - public static object? Deserialize(ref System.Text.Json.Utf8JsonReader reader, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static object? Deserialize(ref System.Text.Json.Utf8JsonReader reader, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static object? Deserialize(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static object? Deserialize(string json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static object? Deserialize(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static object? Deserialize(System.ReadOnlySpan<byte> utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static object? Deserialize(System.ReadOnlySpan<byte> utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static object? Deserialize(System.ReadOnlySpan<char> json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static object? Deserialize(System.ReadOnlySpan<char> json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static object? Deserialize(System.Text.Json.JsonDocument document, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static object? Deserialize(System.Text.Json.JsonDocument document, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static object? Deserialize(System.Text.Json.JsonElement element, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static object? Deserialize(System.Text.Json.JsonElement element, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static object? Deserialize(System.Text.Json.Nodes.JsonNode? node, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static object? Deserialize(System.Text.Json.Nodes.JsonNode? node, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static TValue? Deserialize<TValue>(ref System.Text.Json.Utf8JsonReader reader, JsonTypeInfo`1<TValue> jsonTypeInfo)
  - public static TValue? Deserialize<TValue>(string json, JsonTypeInfo`1<TValue?> jsonTypeInfo)
  - public static TValue? Deserialize<TValue>(System.IO.Stream utf8Json, JsonTypeInfo`1<TValue?> jsonTypeInfo)
  - public static TValue? Deserialize<TValue>(System.ReadOnlySpan<byte> utf8Json, JsonTypeInfo`1<TValue> jsonTypeInfo)
  - public static TValue? Deserialize<TValue>(System.ReadOnlySpan<char> json, JsonTypeInfo`1<TValue> jsonTypeInfo)
  - public static TValue? Deserialize<TValue>(System.Text.Json.JsonDocument document, JsonTypeInfo`1<TValue?> jsonTypeInfo)
  - public static TValue? Deserialize<TValue>(System.Text.Json.JsonElement element, JsonTypeInfo`1<TValue> jsonTypeInfo)
  - public static TValue? Deserialize<TValue>(System.Text.Json.Nodes.JsonNode? node, JsonTypeInfo`1<TValue> jsonTypeInfo)
System.Text.Json.JsonSerializer :: DeserializeAsync count=4 same-named-unmarked=6
  + public static System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Pipelines.PipeReader utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
  + public static System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
  + public static ValueTask`1<TValue?> DeserializeAsync<TValue>(System.IO.Pipelines.PipeReader utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
  + public static ValueTask`1<TValue?> DeserializeAsync<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
  - public static System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Pipelines.PipeReader utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
  - public static System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Pipelines.PipeReader utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context, System.Threading.CancellationToken cancellationToken = null)
  - public static System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
  - public static System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context, System.Threading.CancellationToken cancellationToken = null)
  - public static ValueTask`1<TValue?> DeserializeAsync<TValue>(System.IO.Pipelines.PipeReader utf8Json, JsonTypeInfo`1<TValue?> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
  - public static ValueTask`1<TValue?> DeserializeAsync<TValue>(System.IO.Stream utf8Json, JsonTypeInfo`1<TValue?> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: DeserializeAsyncEnumerable count=4 same-named-unmarked=4
  + public static IAsyncEnumerable`1<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Pipelines.PipeReader utf8Json, bool topLevelValues, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
  + public static IAsyncEnumerable`1<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Pipelines.PipeReader utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
  + public static IAsyncEnumerable`1<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, bool topLevelValues, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
  + public static IAsyncEnumerable`1<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
  - public static IAsyncEnumerable`1<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Pipelines.PipeReader utf8Json, JsonTypeInfo`1<TValue?> jsonTypeInfo, bool topLevelValues, System.Threading.CancellationToken cancellationToken = null)
  - public static IAsyncEnumerable`1<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Pipelines.PipeReader utf8Json, JsonTypeInfo`1<TValue?> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
  - public static IAsyncEnumerable`1<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, JsonTypeInfo`1<TValue?> jsonTypeInfo, bool topLevelValues, System.Threading.CancellationToken cancellationToken = null)
  - public static IAsyncEnumerable`1<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, JsonTypeInfo`1<TValue?> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: GetTypeInfo count=2 same-named-unmarked=1
  + private static JsonTypeInfo`1<T> GetTypeInfo<T>(System.Text.Json.JsonSerializerOptions options)
  + private static System.Text.Json.Serialization.Metadata.JsonTypeInfo GetTypeInfo(System.Text.Json.JsonSerializerOptions options, System.Type inputType)
<shellId: 7 completed with exit code 0>
```

</details>


---

<sub>48s</sub>

### `bash`

**Inspect RUC groups and converter classes**

`$ rg -n '^System\.|^  \+|^=== CONVERTER|^=== RUC TYPES|^=== RUC ALL' probe-output.txt | tail -120`

<details>
<summary>121 lines</summary>

```
240:System.Text.Json.Serialization.Converters.FSharpValueOptionConverter`2 :: .ctor count=1 same-named-unmarked=0
241:  + public FSharpValueOptionConverter`2(JsonConverter`1<TElement> elementConverter)
242:System.Text.Json.Serialization.Converters.IEnumerableConverterFactory :: .ctor count=1 same-named-unmarked=0
243:  + public IEnumerableConverterFactory()
244:System.Text.Json.Serialization.Converters.ImmutableDictionaryOfTKeyTValueConverterWithReflection`3 :: .ctor count=1 same-named-unmarked=0
245:  + public ImmutableDictionaryOfTKeyTValueConverterWithReflection`3()
246:System.Text.Json.Serialization.Converters.ImmutableDictionaryOfTKeyTValueConverterWithReflection`3 :: ConfigureJsonTypeInfoUsingReflection count=1 same-named-unmarked=0
247:  + internal virtual void ConfigureJsonTypeInfoUsingReflection(System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Text.Json.JsonSerializerOptions options)
248:System.Text.Json.Serialization.Converters.ImmutableEnumerableOfTConverterWithReflection`2 :: .ctor count=1 same-named-unmarked=0
249:  + public ImmutableEnumerableOfTConverterWithReflection`2()
250:System.Text.Json.Serialization.Converters.ImmutableEnumerableOfTConverterWithReflection`2 :: ConfigureJsonTypeInfoUsingReflection count=1 same-named-unmarked=0
251:  + internal virtual void ConfigureJsonTypeInfoUsingReflection(System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Text.Json.JsonSerializerOptions options)
252:System.Text.Json.Serialization.Converters.LargeObjectWithParameterizedConstructorConverterWithReflection`1 :: .ctor count=1 same-named-unmarked=0
253:  + public LargeObjectWithParameterizedConstructorConverterWithReflection`1()
254:System.Text.Json.Serialization.Converters.LargeObjectWithParameterizedConstructorConverterWithReflection`1 :: ConfigureJsonTypeInfoUsingReflection count=1 same-named-unmarked=0
255:  + internal virtual void ConfigureJsonTypeInfoUsingReflection(System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Text.Json.JsonSerializerOptions options)
256:System.Text.Json.Serialization.Converters.ObjectConverterFactory :: .ctor count=1 same-named-unmarked=0
257:  + public ObjectConverterFactory(bool useDefaultConstructorInUnannotatedStructs = true)
258:System.Text.Json.Serialization.Converters.SmallObjectWithParameterizedConstructorConverter`5 :: ConfigureJsonTypeInfoUsingReflection count=1 same-named-unmarked=0
259:  + internal virtual void ConfigureJsonTypeInfoUsingReflection(System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Text.Json.JsonSerializerOptions options)
260:System.Text.Json.Serialization.Converters.StackOrQueueConverterWithReflection`1 :: .ctor count=1 same-named-unmarked=0
261:  + public StackOrQueueConverterWithReflection`1()
262:System.Text.Json.Serialization.Converters.StackOrQueueConverterWithReflection`1 :: ConfigureJsonTypeInfoUsingReflection count=1 same-named-unmarked=0
263:  + internal virtual void ConfigureJsonTypeInfoUsingReflection(System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Text.Json.JsonSerializerOptions options)
264:System.Text.Json.Serialization.IEnumerableConverterFactoryHelpers :: GetImmutableDictionaryConstructingType count=1 same-named-unmarked=0
265:  + private static System.Type GetImmutableDictionaryConstructingType(System.Type type)
266:System.Text.Json.Serialization.IEnumerableConverterFactoryHelpers :: GetImmutableDictionaryCreateRangeMethod count=1 same-named-unmarked=0
267:  + public static System.Reflection.MethodInfo GetImmutableDictionaryCreateRangeMethod(System.Type type, System.Type keyType, System.Type valueType)
268:System.Text.Json.Serialization.IEnumerableConverterFactoryHelpers :: GetImmutableEnumerableConstructingType count=1 same-named-unmarked=0
269:  + private static System.Type GetImmutableEnumerableConstructingType(System.Type type)
270:System.Text.Json.Serialization.IEnumerableConverterFactoryHelpers :: GetImmutableEnumerableCreateRangeMethod count=1 same-named-unmarked=0
271:  + public static System.Reflection.MethodInfo GetImmutableEnumerableCreateRangeMethod(System.Type type, System.Type elementType)
272:System.Text.Json.Serialization.JsonConverter :: ConfigureJsonTypeInfoUsingReflection count=1 same-named-unmarked=0
273:  + internal virtual void ConfigureJsonTypeInfoUsingReflection(System.Text.Json.Serialization.Metadata.JsonTypeInfo? jsonTypeInfo, System.Text.Json.JsonSerializerOptions? options)
274:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: .ctor count=2 same-named-unmarked=0
275:  + private DefaultJsonTypeInfoResolver(bool mutable)
276:  + public DefaultJsonTypeInfoResolver()
277:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: AddMember count=1 same-named-unmarked=0
278:  + private static void AddMember(System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo, System.Type typeToConvert, System.Reflection.MemberInfo memberInfo, System.Reflection.NullabilityInfoContext nullabilityCtx, bool shouldCheckForRequiredKeyword, bool hasJsonIncludeAttribute, ref System.Text.Json.Serialization.Metadata.JsonTypeInfo.PropertyHierarchyResolutionState state)
279:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: AddMembersDeclaredBySuperType count=1 same-named-unmarked=0
280:  + private static void AddMembersDeclaredBySuperType(System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo, System.Type currentType, System.Reflection.NullabilityInfoContext nullabilityCtx, bool constructorHasSetsRequiredMembersAttribute, ref System.Text.Json.Serialization.Metadata.JsonTypeInfo.PropertyHierarchyResolutionState state)
281:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: CreateJsonTypeInfo count=1 same-named-unmarked=0
282:  + private static System.Text.Json.Serialization.Metadata.JsonTypeInfo CreateJsonTypeInfo(System.Type type, System.Text.Json.JsonSerializerOptions options)
283:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: CreatePropertyInfo count=1 same-named-unmarked=0
284:  + private static System.Text.Json.Serialization.Metadata.JsonPropertyInfo CreatePropertyInfo(System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo, System.Type typeToConvert, System.Reflection.MemberInfo memberInfo, System.Reflection.NullabilityInfoContext nullabilityCtx, System.Text.Json.JsonSerializerOptions options, bool shouldCheckForRequiredKeyword, bool hasJsonIncludeAttribute)
285:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: CreateTypeInfoCore count=1 same-named-unmarked=0
286:  + private static System.Text.Json.Serialization.Metadata.JsonTypeInfo CreateTypeInfoCore(System.Type type, System.Text.Json.Serialization.JsonConverter converter, System.Text.Json.JsonSerializerOptions options)
287:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: DetermineCreateObjectDelegate count=1 same-named-unmarked=0
288:  + private static System.Func<object> DetermineCreateObjectDelegate(System.Type type, System.Text.Json.Serialization.JsonConverter converter)
289:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: DetermineParameterNullability count=1 same-named-unmarked=0
290:  + private static System.Reflection.NullabilityState DetermineParameterNullability(System.Reflection.ParameterInfo parameterInfo, System.Reflection.NullabilityInfoContext nullabilityCtx)
291:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: DeterminePropertyAccessors count=1 same-named-unmarked=0
292:  + internal static void DeterminePropertyAccessors<T>(JsonPropertyInfo`1<T> jsonPropertyInfo, System.Reflection.MemberInfo memberInfo, bool useNonPublicAccessors)
293:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: DeterminePropertyNullability count=1 same-named-unmarked=0
294:  + private static void DeterminePropertyNullability(System.Text.Json.Serialization.Metadata.JsonPropertyInfo propertyInfo, System.Reflection.MemberInfo memberInfo, System.Reflection.NullabilityInfoContext nullabilityCtx)
295:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: get_DefaultInstance count=1 same-named-unmarked=0
296:  + internal static System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver get_DefaultInstance()
297:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: get_MemberAccessor count=1 same-named-unmarked=0
298:  + internal static System.Text.Json.Serialization.Metadata.MemberAccessor get_MemberAccessor()
299:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: GetBuiltInConverter count=1 same-named-unmarked=0
300:  + private static System.Text.Json.Serialization.JsonConverter GetBuiltInConverter(System.Type typeToConvert)
301:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: GetConverterForType count=1 same-named-unmarked=0
302:  + internal static System.Text.Json.Serialization.JsonConverter GetConverterForType(System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options, bool resolveJsonConverterAttribute = true)
303:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: GetConverterFromAttribute count=1 same-named-unmarked=0
304:  + private static System.Text.Json.Serialization.JsonConverter GetConverterFromAttribute(System.Text.Json.Serialization.JsonConverterAttribute converterAttribute, System.Type typeToConvert, System.Reflection.MemberInfo memberInfo, System.Text.Json.JsonSerializerOptions options)
305:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: GetCustomConverterForMember count=1 same-named-unmarked=0
306:  + private static System.Text.Json.Serialization.JsonConverter GetCustomConverterForMember(System.Type typeToConvert, System.Reflection.MemberInfo memberInfo, System.Text.Json.JsonSerializerOptions options)
307:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: GetDefaultFactoryConverters count=1 same-named-unmarked=0
308:  + private static System.Text.Json.Serialization.JsonConverterFactory[] GetDefaultFactoryConverters()
309:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: PopulateParameterInfoValues count=1 same-named-unmarked=0
310:  + private static void PopulateParameterInfoValues(System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo, System.Reflection.NullabilityInfoContext nullabilityCtx)
311:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: PopulateProperties count=1 same-named-unmarked=0
312:  + private static void PopulateProperties(System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo, System.Reflection.NullabilityInfoContext nullabilityCtx)
313:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: PopulatePropertyInfo count=1 same-named-unmarked=0
314:  + private static void PopulatePropertyInfo(System.Text.Json.Serialization.Metadata.JsonPropertyInfo jsonPropertyInfo, System.Reflection.MemberInfo memberInfo, System.Text.Json.Serialization.JsonConverter customConverter, System.Nullable<System.Text.Json.Serialization.JsonIgnoreCondition> ignoreCondition, System.Reflection.NullabilityInfoContext nullabilityCtx, bool shouldCheckForRequiredKeyword, bool hasJsonIncludeAttribute)
315:System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy :: .ctor count=1 same-named-unmarked=0
316:  + private FSharpCoreReflectionProxy(System.Reflection.Assembly fsharpCoreAssembly)
317:System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy :: CreateFSharpListConstructor count=1 same-named-unmarked=0
318:  + public Func`2<IEnumerable`1<TElement>, TFSharpList> CreateFSharpListConstructor<TFSharpList, TElement>()
319:System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy :: CreateFSharpMapConstructor count=1 same-named-unmarked=0
320:  + public Func`2<IEnumerable`1<Tuple`2<TKey, TValue>>, TFSharpMap> CreateFSharpMapConstructor<TFSharpMap, TKey, TValue>()
321:System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy :: CreateFSharpOptionSomeConstructor count=1 same-named-unmarked=0
322:  + public Func`2<TElement, TFSharpOption> CreateFSharpOptionSomeConstructor<TFSharpOption, TElement>()
323:System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy :: CreateFSharpOptionValueGetter count=1 same-named-unmarked=0
324:  + public Func`2<TFSharpOption, T> CreateFSharpOptionValueGetter<TFSharpOption, T>()
325:System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy :: CreateFSharpSetConstructor count=1 same-named-unmarked=0
326:  + public Func`2<IEnumerable`1<TElement>, TFSharpSet> CreateFSharpSetConstructor<TFSharpSet, TElement>()
327:System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy :: CreateFSharpValueOptionSomeConstructor count=1 same-named-unmarked=0
328:  + public Func`2<TElement, TFSharpOption> CreateFSharpValueOptionSomeConstructor<TFSharpOption, TElement>()
329:System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy :: CreateFSharpValueOptionValueGetter count=1 same-named-unmarked=0
330:  + public StructGetter`2<TFSharpValueOption, TElement> CreateFSharpValueOptionValueGetter<TFSharpValueOption, TElement>()
331:System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy :: DetectFSharpKind count=1 same-named-unmarked=0
332:  + public System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy.FSharpKind DetectFSharpKind(System.Type type)
333:System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy :: IsFSharpType count=1 same-named-unmarked=0
334:  + public static bool IsFSharpType(System.Type type)
335:System.Text.Json.Serialization.Metadata.JsonPropertyInfo :: DetermineReflectionPropertyAccessors count=1 same-named-unmarked=0
336:  + internal abstract void DetermineReflectionPropertyAccessors(System.Reflection.MemberInfo memberInfo, bool useNonPublicAccessors)
337:System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1 :: DetermineReflectionPropertyAccessors count=1 same-named-unmarked=0
338:  + internal virtual void DetermineReflectionPropertyAccessors(System.Reflection.MemberInfo memberInfo, bool useNonPublicAccessors)
339:System.Text.Json.Serialization.Metadata.JsonTypeInfo :: CreateJsonPropertyInfo count=1 same-named-unmarked=1
340:  + public System.Text.Json.Serialization.Metadata.JsonPropertyInfo CreateJsonPropertyInfo(System.Type propertyType, string name)
342:System.Text.Json.Serialization.Metadata.JsonTypeInfo :: CreateJsonTypeInfo count=3 same-named-unmarked=0
343:  + internal static System.Text.Json.Serialization.Metadata.JsonTypeInfo? CreateJsonTypeInfo(System.Type? type, System.Text.Json.Serialization.JsonConverter? converter, System.Text.Json.JsonSerializerOptions? options)
344:  + public static JsonTypeInfo`1<T?> CreateJsonTypeInfo<T>(System.Text.Json.JsonSerializerOptions options)
345:  + public static System.Text.Json.Serialization.Metadata.JsonTypeInfo CreateJsonTypeInfo(System.Type type, System.Text.Json.JsonSerializerOptions options)
346:System.Text.Json.Serialization.Metadata.JsonTypeInfo :: CreatePropertyUsingReflection count=1 same-named-unmarked=0
347:  + internal System.Text.Json.Serialization.Metadata.JsonPropertyInfo? CreatePropertyUsingReflection(System.Type? propertyType, System.Type? declaringType)
348:System.Text.Json.Serialization.Metadata.ReflectionEmitCachingMemberAccessor :: .ctor count=1 same-named-unmarked=0
349:  + public ReflectionEmitCachingMemberAccessor()
351:=== RUC TYPES ===
352:System.Text.Json.Serialization.Metadata.ReflectionEmitMemberAccessor
353:System.Text.Json.Serialization.Metadata.ReflectionMemberAccessor
355:=== CONVERTER TYPES PUBLIC ===
356:System.Text.Json.Serialization.JsonConverter abstract=True sealed=False base=System.Object
360:System.Text.Json.Serialization.JsonConverter`1 abstract=True sealed=False base=System.Text.Json.Serialization.JsonConverter
366:System.Text.Json.Serialization.JsonConverterAttribute abstract=False sealed=False base=System.Text.Json.Serialization.JsonAttribute
368:System.Text.Json.Serialization.JsonConverterFactory abstract=True sealed=False base=System.Text.Json.Serialization.JsonConverter
373:System.Text.Json.Serialization.JsonNumberEnumConverter`1 abstract=False sealed=True base=System.Text.Json.Serialization.JsonConverterFactory
375:System.Text.Json.Serialization.JsonStringEnumConverter abstract=False sealed=False base=System.Text.Json.Serialization.JsonConverterFactory
380:System.Text.Json.Serialization.JsonStringEnumConverter`1 abstract=False sealed=False base=System.Text.Json.Serialization.JsonConverterFactory
<shellId: 8 completed with exit code 0>
```

</details>


---

<sub>48s</sub>

### `bash`

**Read converter documentation and source overload locations**

`$ sed -n '350,384p' probe-output.txt && sed -n '20,95p' JsonSerializer.Read.Stream.cs && sed -n '130,160p' JsonSerializer.Read.Stream.cs && sed -n '224,273p' JsonSerializer.Read.Stream.cs && sed -n '317,350p' JsonSerializer.Read.Stream.cs && sed -n '3800,3918p' package/lib/net10.0/System.Text.Json.xml`

<details>
<summary>12 lines</summary>

```
Output too large to read at once (23.0 KB). Saved to: /var/folders/09/c3s_t1kd4m91p7yltrx08snw0000gn/T/1794006028015-copilot-tool-output-2223-a31ab5c2-cd5a-4690-b814-3c50e5edcb0a.txt
Consider using tools like rg (for searching), head/tail (for viewing start/end), view with view_range (for specific sections), or jq (for JSON) to examine portions of the output.

Preview (first 500 chars):

=== RUC TYPES ===
System.Text.Json.Serialization.Metadata.ReflectionEmitMemberAccessor
System.Text.Json.Serialization.Metadata.ReflectionMemberAccessor

=== CONVERTER TYPES PUBLIC ===
System.Text.Json.Serialization.JsonConverter abstract=True sealed=False base=System.Object
  constructors: 
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeT
<shellId: 9 completed with exit code 0>
```

</details>


---

<sub>53s</sub>

### `bash`

**Read public RUC method groups and converter obligations**

`$ sed -n '112,220p' probe-output.txt | grep -E '^System\.|^  \+|^===|^  - public' | head -118; sed -n '355,384p' probe-output.txt`

<details>
<summary>139 lines</summary>

```
- public static IAsyncEnumerable`1<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, JsonTypeInfo`1<TValue?> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: GetTypeInfo count=2 same-named-unmarked=1
  + private static JsonTypeInfo`1<T> GetTypeInfo<T>(System.Text.Json.JsonSerializerOptions options)
  + private static System.Text.Json.Serialization.Metadata.JsonTypeInfo GetTypeInfo(System.Text.Json.JsonSerializerOptions options, System.Type inputType)
System.Text.Json.JsonSerializer :: Serialize count=6 same-named-unmarked=9
  + public static string Serialize(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static string Serialize<TValue>(TValue? value, System.Text.Json.JsonSerializerOptions? options = null)
  + public static void Serialize(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static void Serialize(System.Text.Json.Utf8JsonWriter writer, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static void Serialize<TValue>(System.IO.Stream utf8Json, TValue? value, System.Text.Json.JsonSerializerOptions? options = null)
  + public static void Serialize<TValue>(System.Text.Json.Utf8JsonWriter writer, TValue? value, System.Text.Json.JsonSerializerOptions? options = null)
  - public static string Serialize(object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static string Serialize(object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static string Serialize<TValue>(TValue? value, JsonTypeInfo`1<TValue?> jsonTypeInfo)
  - public static void Serialize(System.IO.Stream utf8Json, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static void Serialize(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static void Serialize(System.Text.Json.Utf8JsonWriter writer, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static void Serialize(System.Text.Json.Utf8JsonWriter writer, object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static void Serialize<TValue>(System.IO.Stream utf8Json, TValue? value, JsonTypeInfo`1<TValue?> jsonTypeInfo)
  - public static void Serialize<TValue>(System.Text.Json.Utf8JsonWriter writer, TValue? value, JsonTypeInfo`1<TValue?> jsonTypeInfo)
System.Text.Json.JsonSerializer :: SerializeAsync count=4 same-named-unmarked=6
  + public static System.Threading.Tasks.Task SerializeAsync(System.IO.Pipelines.PipeWriter utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
  + public static System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
  + public static System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Pipelines.PipeWriter utf8Json, TValue? value, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
  + public static System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Stream utf8Json, TValue? value, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = null)
  - public static System.Threading.Tasks.Task SerializeAsync(System.IO.Pipelines.PipeWriter utf8Json, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
  - public static System.Threading.Tasks.Task SerializeAsync(System.IO.Pipelines.PipeWriter utf8Json, object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context, System.Threading.CancellationToken cancellationToken = null)
  - public static System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
  - public static System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context, System.Threading.CancellationToken cancellationToken = null)
  - public static System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Pipelines.PipeWriter utf8Json, TValue? value, JsonTypeInfo`1<TValue?> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
  - public static System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Stream utf8Json, TValue? value, JsonTypeInfo`1<TValue?> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = null)
System.Text.Json.JsonSerializer :: SerializeToDocument count=2 same-named-unmarked=3
  + public static System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static System.Text.Json.JsonDocument SerializeToDocument<TValue>(TValue? value, System.Text.Json.JsonSerializerOptions? options = null)
  - public static System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static System.Text.Json.JsonDocument SerializeToDocument<TValue>(TValue? value, JsonTypeInfo`1<TValue?> jsonTypeInfo)
System.Text.Json.JsonSerializer :: SerializeToElement count=2 same-named-unmarked=3
  + public static System.Text.Json.JsonElement SerializeToElement(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static System.Text.Json.JsonElement SerializeToElement<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)
  - public static System.Text.Json.JsonElement SerializeToElement(object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static System.Text.Json.JsonElement SerializeToElement(object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static System.Text.Json.JsonElement SerializeToElement<TValue>(TValue? value, JsonTypeInfo`1<TValue?> jsonTypeInfo)
System.Text.Json.JsonSerializer :: SerializeToNode count=2 same-named-unmarked=3
  + public static System.Text.Json.Nodes.JsonNode? SerializeToNode(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static System.Text.Json.Nodes.JsonNode? SerializeToNode<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)
  - public static System.Text.Json.Nodes.JsonNode? SerializeToNode(object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static System.Text.Json.Nodes.JsonNode? SerializeToNode(object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static System.Text.Json.Nodes.JsonNode? SerializeToNode<TValue>(TValue? value, JsonTypeInfo`1<TValue?> jsonTypeInfo)
System.Text.Json.JsonSerializer :: SerializeToUtf8Bytes count=2 same-named-unmarked=3
  + public static byte[] SerializeToUtf8Bytes(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static byte[] SerializeToUtf8Bytes<TValue>(TValue? value, System.Text.Json.JsonSerializerOptions? options = null)
  - public static byte[] SerializeToUtf8Bytes(object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
  - public static byte[] SerializeToUtf8Bytes(object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
  - public static byte[] SerializeToUtf8Bytes<TValue>(TValue? value, JsonTypeInfo`1<TValue?> jsonTypeInfo)
System.Text.Json.JsonSerializerOptions :: ConfigureForJsonSerializer count=1 same-named-unmarked=0
  + private void ConfigureForJsonSerializer()
System.Text.Json.JsonSerializerOptions :: get_Default count=1 same-named-unmarked=0
  + public static System.Text.Json.JsonSerializerOptions get_Default()
System.Text.Json.JsonSerializerOptions :: get_Strict count=1 same-named-unmarked=0
  + public static System.Text.Json.JsonSerializerOptions get_Strict()
System.Text.Json.JsonSerializerOptions :: get_Web count=1 same-named-unmarked=0
  + public static System.Text.Json.JsonSerializerOptions get_Web()
System.Text.Json.JsonSerializerOptions :: GetConverter count=1 same-named-unmarked=0
  + public System.Text.Json.Serialization.JsonConverter GetConverter(System.Type typeToConvert)
System.Text.Json.JsonSerializerOptions :: GetOrCreateSingleton count=1 same-named-unmarked=0
  + private static System.Text.Json.JsonSerializerOptions GetOrCreateSingleton(ref System.Text.Json.JsonSerializerOptions location, System.Text.Json.JsonSerializerDefaults defaults)
System.Text.Json.JsonSerializerOptions :: MakeReadOnly count=1 same-named-unmarked=1
  + public void MakeReadOnly(bool populateMissingResolver)
  - public void MakeReadOnly()
System.Text.Json.Nodes.JsonArray :: Add count=1 same-named-unmarked=1
  + public void Add<T>(T? value)
  - public virtual void Add(System.Text.Json.Nodes.JsonNode? item)
System.Text.Json.Nodes.JsonNode :: ConvertFromValue count=1 same-named-unmarked=0
  + internal static System.Text.Json.Nodes.JsonNode ConvertFromValue<T>(T value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
System.Text.Json.Nodes.JsonNode :: ReplaceWith count=1 same-named-unmarked=0
  + public void ReplaceWith<T>(T? value)
System.Text.Json.Nodes.JsonValue :: Create count=1 same-named-unmarked=36
  + public static System.Text.Json.Nodes.JsonValue? Create<T>(T? value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(bool value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(byte value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(char value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(int value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(long value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(System.DateTime value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(System.DateTimeOffset value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(System.Decimal value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(System.Double value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(System.Guid value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(System.Int16 value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(System.SByte value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(System.Single value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(System.UInt16 value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(System.UInt32 value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue Create(System.UInt64 value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(string? value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<bool> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<byte> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<char> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<int> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<long> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<System.DateTime> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<System.DateTimeOffset> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<System.Decimal> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<System.Double> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<System.Guid> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<System.Int16> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<System.SByte> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
=== CONVERTER TYPES PUBLIC ===
System.Text.Json.Serialization.JsonConverter abstract=True sealed=False base=System.Object
  constructors: 
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeToConvert)
System.Text.Json.Serialization.JsonConverter`1 abstract=True sealed=False base=System.Text.Json.Serialization.JsonConverter
  constructors: protected internal JsonConverter`1()
  abstract System.Text.Json.Serialization.JsonConverter`1 :: public abstract T? Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
  abstract System.Text.Json.Serialization.JsonConverter`1 :: public abstract void Write(System.Text.Json.Utf8JsonWriter writer, T value, System.Text.Json.JsonSerializerOptions options)
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeToConvert)
System.Text.Json.Serialization.JsonConverterAttribute abstract=False sealed=False base=System.Text.Json.Serialization.JsonAttribute
  constructors: public JsonConverterAttribute(System.Type converterType); protected JsonConverterAttribute()
System.Text.Json.Serialization.JsonConverterFactory abstract=True sealed=False base=System.Text.Json.Serialization.JsonConverter
  constructors: protected JsonConverterFactory()
  abstract System.Text.Json.Serialization.JsonConverterFactory :: public abstract System.Text.Json.Serialization.JsonConverter? CreateConverter(System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeToConvert)
System.Text.Json.Serialization.JsonNumberEnumConverter`1 abstract=False sealed=True base=System.Text.Json.Serialization.JsonConverterFactory
  constructors: public JsonNumberEnumConverter`1()
System.Text.Json.Serialization.JsonStringEnumConverter abstract=False sealed=False base=System.Text.Json.Serialization.JsonConverterFactory
  constructors: public JsonStringEnumConverter(); public JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy? namingPolicy = null, bool allowIntegerValues = true)
  abstract System.Text.Json.Serialization.JsonConverterFactory :: public abstract System.Text.Json.Serialization.JsonConverter? CreateConverter(System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeToConvert)
System.Text.Json.Serialization.JsonStringEnumConverter`1 abstract=False sealed=False base=System.Text.Json.Serialization.JsonConverterFactory
  constructors: public JsonStringEnumConverter`1(); public JsonStringEnumConverter`1(System.Text.Json.JsonNamingPolicy? namingPolicy = null, bool allowIntegerValues = true)
  abstract System.Text.Json.Serialization.JsonConverterFactory :: public abstract System.Text.Json.Serialization.JsonConverter? CreateConverter(System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeToConvert)
<shellId: 10 completed with exit code 0>
```

</details>


---

<sub>53s</sub>

### `bash`

**Locate five Deserialize Stream definitions and exact declaration lines**

`$ rg -n '^        public static .* Deserialize(<|\()|^        public static .* Deserialize\<' JsonSerializer.Read.Stream.cs; rg -n '^        public static|^            Stream utf8Json|^            Stream utf8' JsonSerializer.Read.Stream.cs | head -85; sed -n '42,55p' JsonSerializer.Read.Stream.cs; sed -n '74,86p' JsonSerializer.Read.Stream.cs; sed -n '142,150p' JsonSerializer.Read.Stream.cs; sed -n '232,265p' JsonSerializer.Read.Stream.cs; sed -n '327,341p' JsonSerializer.Read.Stream.cs`

```
76:        public static TValue? Deserialize<TValue>(
146:        public static object? Deserialize(
234:        public static TValue? Deserialize<TValue>(
259:        public static object? Deserialize(
335:        public static object? Deserialize(
43:        public static ValueTask<TValue?> DeserializeAsync<TValue>(
44:            Stream utf8Json,
76:        public static TValue? Deserialize<TValue>(
77:            Stream utf8Json,
111:        public static ValueTask<object?> DeserializeAsync(
112:            Stream utf8Json,
146:        public static object? Deserialize(
147:            Stream utf8Json,
177:        public static ValueTask<TValue?> DeserializeAsync<TValue>(
178:            Stream utf8Json,
206:        public static ValueTask<object?> DeserializeAsync(
207:            Stream utf8Json,
234:        public static TValue? Deserialize<TValue>(
235:            Stream utf8Json,
259:        public static object? Deserialize(
260:            Stream utf8Json,
297:        public static ValueTask<object?> DeserializeAsync(
298:            Stream utf8Json,
335:        public static object? Deserialize(
336:            Stream utf8Json,
362:        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
363:            Stream utf8Json,
391:        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
392:            Stream utf8Json,
415:        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
416:            Stream utf8Json,
442:        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
443:            Stream utf8Json,
456:            Stream utf8Json,
        [RequiresDynamicCode(SerializationRequiresDynamicCodeMessage)]
        public static ValueTask<TValue?> DeserializeAsync<TValue>(
            Stream utf8Json,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(utf8Json);

            JsonTypeInfo<TValue> jsonTypeInfo = GetTypeInfo<TValue>(options);
            return jsonTypeInfo.DeserializeAsync(utf8Json, cancellationToken);
        }

        /// <summary>
        /// Reads the UTF-8 encoded text representing a single JSON value into a <typeparamref name="TValue"/>.
        [RequiresUnreferencedCode(SerializationUnreferencedCodeMessage)]
        [RequiresDynamicCode(SerializationRequiresDynamicCodeMessage)]
        public static TValue? Deserialize<TValue>(
            Stream utf8Json,
            JsonSerializerOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(utf8Json);

            JsonTypeInfo<TValue> jsonTypeInfo = GetTypeInfo<TValue>(options);
            return jsonTypeInfo.Deserialize(utf8Json);
        }

        /// <summary>
        /// for <paramref name="returnType"/> or its serializable members.
        /// </exception>
        [RequiresUnreferencedCode(SerializationUnreferencedCodeMessage)]
        [RequiresDynamicCode(SerializationRequiresDynamicCodeMessage)]
        public static object? Deserialize(
            Stream utf8Json,
            Type returnType,
            JsonSerializerOptions? options = null)
        {
        /// or when there is remaining data in the Stream.
        /// </exception>
        public static TValue? Deserialize<TValue>(
            Stream utf8Json,
            JsonTypeInfo<TValue> jsonTypeInfo)
        {
            ArgumentNullException.ThrowIfNull(utf8Json);
            ArgumentNullException.ThrowIfNull(jsonTypeInfo);

            jsonTypeInfo.EnsureConfigured();
            return jsonTypeInfo.Deserialize(utf8Json);
        }

        /// <summary>
        /// Reads the UTF-8 encoded text representing a single JSON value into an instance specified by the <paramref name="jsonTypeInfo"/>.
        /// The Stream will be read to completion.
        /// </summary>
        /// <returns>A <paramref name="jsonTypeInfo"/> representation of the JSON value.</returns>
        /// <param name="utf8Json">JSON data to parse.</param>
        /// <param name="jsonTypeInfo">Metadata about the type to convert.</param>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="utf8Json"/> or <paramref name="jsonTypeInfo"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="JsonException">
        /// The JSON is invalid,
        /// or when there is remaining data in the Stream.
        /// </exception>
        public static object? Deserialize(
            Stream utf8Json,
            JsonTypeInfo jsonTypeInfo)
        {
            ArgumentNullException.ThrowIfNull(utf8Json);
            ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        /// <exception cref="NotSupportedException">
        /// There is no compatible <see cref="System.Text.Json.Serialization.JsonConverter"/>
        /// for <paramref name="returnType"/> or its serializable members.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The <see cref="JsonSerializerContext.GetTypeInfo(Type)"/> method on the provided <paramref name="context"/>
        /// did not return a compatible <see cref="JsonTypeInfo"/> for <paramref name="returnType"/>.
        /// </exception>
        public static object? Deserialize(
            Stream utf8Json,
            Type returnType,
            JsonSerializerContext context)
        {
            ArgumentNullException.ThrowIfNull(utf8Json);
            ArgumentNullException.ThrowIfNull(returnType);
<shellId: 11 completed with exit code 0>
```


---

<sub>58s</sub>

### `bash`

**Inspect remaining public RUC and converter docs**

`$ sed -n '220,255p' probe-output.txt && sed -n '335,384p' probe-output.txt && sed -n '3802,3905p' package/lib/net10.0/System.Text.Json.xml`

```
- public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<System.SByte> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<System.Single> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<System.Text.Json.JsonElement> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<System.UInt16> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<System.UInt32> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Nullable<System.UInt64> value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create(System.Text.Json.JsonElement value, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
  - public static System.Text.Json.Nodes.JsonValue? Create<T>(T? value, JsonTypeInfo`1<T?> jsonTypeInfo, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> options = null)
System.Text.Json.Reflection.ReflectionExtensions :: GetSortedTypeHierarchy count=1 same-named-unmarked=0
  + public static System.Type[] GetSortedTypeHierarchy(System.Type type)
System.Text.Json.Serialization.Converters.FSharpListConverter`2 :: .ctor count=1 same-named-unmarked=0
  + public FSharpListConverter`2()
System.Text.Json.Serialization.Converters.FSharpMapConverter`3 :: .ctor count=1 same-named-unmarked=0
  + public FSharpMapConverter`3()
System.Text.Json.Serialization.Converters.FSharpOptionConverter`2 :: .ctor count=1 same-named-unmarked=0
  + public FSharpOptionConverter`2(JsonConverter`1<TElement> elementConverter)
System.Text.Json.Serialization.Converters.FSharpSetConverter`2 :: .ctor count=1 same-named-unmarked=0
  + public FSharpSetConverter`2()
System.Text.Json.Serialization.Converters.FSharpTypeConverterFactory :: .ctor count=1 same-named-unmarked=0
  + public FSharpTypeConverterFactory()
System.Text.Json.Serialization.Converters.FSharpValueOptionConverter`2 :: .ctor count=1 same-named-unmarked=0
  + public FSharpValueOptionConverter`2(JsonConverter`1<TElement> elementConverter)
System.Text.Json.Serialization.Converters.IEnumerableConverterFactory :: .ctor count=1 same-named-unmarked=0
  + public IEnumerableConverterFactory()
System.Text.Json.Serialization.Converters.ImmutableDictionaryOfTKeyTValueConverterWithReflection`3 :: .ctor count=1 same-named-unmarked=0
  + public ImmutableDictionaryOfTKeyTValueConverterWithReflection`3()
System.Text.Json.Serialization.Converters.ImmutableDictionaryOfTKeyTValueConverterWithReflection`3 :: ConfigureJsonTypeInfoUsingReflection count=1 same-named-unmarked=0
  + internal virtual void ConfigureJsonTypeInfoUsingReflection(System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Text.Json.JsonSerializerOptions options)
System.Text.Json.Serialization.Converters.ImmutableEnumerableOfTConverterWithReflection`2 :: .ctor count=1 same-named-unmarked=0
  + public ImmutableEnumerableOfTConverterWithReflection`2()
System.Text.Json.Serialization.Converters.ImmutableEnumerableOfTConverterWithReflection`2 :: ConfigureJsonTypeInfoUsingReflection count=1 same-named-unmarked=0
  + internal virtual void ConfigureJsonTypeInfoUsingReflection(System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Text.Json.JsonSerializerOptions options)
System.Text.Json.Serialization.Converters.LargeObjectWithParameterizedConstructorConverterWithReflection`1 :: .ctor count=1 same-named-unmarked=0
  + public LargeObjectWithParameterizedConstructorConverterWithReflection`1()
System.Text.Json.Serialization.Converters.LargeObjectWithParameterizedConstructorConverterWithReflection`1 :: ConfigureJsonTypeInfoUsingReflection count=1 same-named-unmarked=0
  + internal virtual void ConfigureJsonTypeInfoUsingReflection(System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Text.Json.JsonSerializerOptions options)
System.Text.Json.Serialization.Metadata.JsonPropertyInfo :: DetermineReflectionPropertyAccessors count=1 same-named-unmarked=0
  + internal abstract void DetermineReflectionPropertyAccessors(System.Reflection.MemberInfo memberInfo, bool useNonPublicAccessors)
System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1 :: DetermineReflectionPropertyAccessors count=1 same-named-unmarked=0
  + internal virtual void DetermineReflectionPropertyAccessors(System.Reflection.MemberInfo memberInfo, bool useNonPublicAccessors)
System.Text.Json.Serialization.Metadata.JsonTypeInfo :: CreateJsonPropertyInfo count=1 same-named-unmarked=1
  + public System.Text.Json.Serialization.Metadata.JsonPropertyInfo CreateJsonPropertyInfo(System.Type propertyType, string name)
  - private protected abstract System.Text.Json.Serialization.Metadata.JsonPropertyInfo CreateJsonPropertyInfo(System.Text.Json.Serialization.Metadata.JsonTypeInfo declaringTypeInfo, System.Type declaringType, System.Text.Json.JsonSerializerOptions options)
System.Text.Json.Serialization.Metadata.JsonTypeInfo :: CreateJsonTypeInfo count=3 same-named-unmarked=0
  + internal static System.Text.Json.Serialization.Metadata.JsonTypeInfo? CreateJsonTypeInfo(System.Type? type, System.Text.Json.Serialization.JsonConverter? converter, System.Text.Json.JsonSerializerOptions? options)
  + public static JsonTypeInfo`1<T?> CreateJsonTypeInfo<T>(System.Text.Json.JsonSerializerOptions options)
  + public static System.Text.Json.Serialization.Metadata.JsonTypeInfo CreateJsonTypeInfo(System.Type type, System.Text.Json.JsonSerializerOptions options)
System.Text.Json.Serialization.Metadata.JsonTypeInfo :: CreatePropertyUsingReflection count=1 same-named-unmarked=0
  + internal System.Text.Json.Serialization.Metadata.JsonPropertyInfo? CreatePropertyUsingReflection(System.Type? propertyType, System.Type? declaringType)
System.Text.Json.Serialization.Metadata.ReflectionEmitCachingMemberAccessor :: .ctor count=1 same-named-unmarked=0
  + public ReflectionEmitCachingMemberAccessor()

=== RUC TYPES ===
System.Text.Json.Serialization.Metadata.ReflectionEmitMemberAccessor
System.Text.Json.Serialization.Metadata.ReflectionMemberAccessor

=== CONVERTER TYPES PUBLIC ===
System.Text.Json.Serialization.JsonConverter abstract=True sealed=False base=System.Object
  constructors: 
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeToConvert)
System.Text.Json.Serialization.JsonConverter`1 abstract=True sealed=False base=System.Text.Json.Serialization.JsonConverter
  constructors: protected internal JsonConverter`1()
  abstract System.Text.Json.Serialization.JsonConverter`1 :: public abstract T? Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
  abstract System.Text.Json.Serialization.JsonConverter`1 :: public abstract void Write(System.Text.Json.Utf8JsonWriter writer, T value, System.Text.Json.JsonSerializerOptions options)
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeToConvert)
System.Text.Json.Serialization.JsonConverterAttribute abstract=False sealed=False base=System.Text.Json.Serialization.JsonAttribute
  constructors: public JsonConverterAttribute(System.Type converterType); protected JsonConverterAttribute()
System.Text.Json.Serialization.JsonConverterFactory abstract=True sealed=False base=System.Text.Json.Serialization.JsonConverter
  constructors: protected JsonConverterFactory()
  abstract System.Text.Json.Serialization.JsonConverterFactory :: public abstract System.Text.Json.Serialization.JsonConverter? CreateConverter(System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeToConvert)
System.Text.Json.Serialization.JsonNumberEnumConverter`1 abstract=False sealed=True base=System.Text.Json.Serialization.JsonConverterFactory
  constructors: public JsonNumberEnumConverter`1()
System.Text.Json.Serialization.JsonStringEnumConverter abstract=False sealed=False base=System.Text.Json.Serialization.JsonConverterFactory
  constructors: public JsonStringEnumConverter(); public JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy? namingPolicy = null, bool allowIntegerValues = true)
  abstract System.Text.Json.Serialization.JsonConverterFactory :: public abstract System.Text.Json.Serialization.JsonConverter? CreateConverter(System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeToConvert)
System.Text.Json.Serialization.JsonStringEnumConverter`1 abstract=False sealed=False base=System.Text.Json.Serialization.JsonConverterFactory
  constructors: public JsonStringEnumConverter`1(); public JsonStringEnumConverter`1(System.Text.Json.JsonNamingPolicy? namingPolicy = null, bool allowIntegerValues = true)
  abstract System.Text.Json.Serialization.JsonConverterFactory :: public abstract System.Text.Json.Serialization.JsonConverter? CreateConverter(System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeToConvert)
    </member>
    <member name="M:System.Text.Json.Serialization.JsonConstructorAttribute.#ctor">
      <summary>Initializes a new instance of <see cref="T:System.Text.Json.Serialization.JsonConstructorAttribute" />.</summary>
    </member>
    <member name="T:System.Text.Json.Serialization.JsonConverter">
      <summary>Converts an object or value to or from JSON.</summary>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonConverter.CanConvert(System.Type)">
      <summary>When overridden in a derived class, determines whether the converter instance can convert the specified object type.</summary>
      <param name="typeToConvert">The type of the object to check whether it can be converted by this converter instance.</param>
      <returns>
        <see langword="true" /> if the instance can convert the specified object type; otherwise, <see langword="false" />.</returns>
    </member>
    <member name="P:System.Text.Json.Serialization.JsonConverter.Type">
      <summary>Gets the type being converted by the current converter instance.</summary>
    </member>
    <member name="T:System.Text.Json.Serialization.JsonConverter`1">
      <summary>Converts an object or value to or from JSON.</summary>
      <typeparam name="T">The type of object or value handled by the converter.</typeparam>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonConverter`1.#ctor">
      <summary>Initializes a new <see cref="T:System.Text.Json.Serialization.JsonConverter`1" /> instance.</summary>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonConverter`1.CanConvert(System.Type)">
      <summary>Determines whether the specified type can be converted.</summary>
      <param name="typeToConvert">The type to compare against.</param>
      <returns>
        <see langword="true" /> if the type can be converted; otherwise, <see langword="false" />.</returns>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonConverter`1.Read(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions)">
      <summary>Reads and converts the JSON to type <typeparamref name="T" />.</summary>
      <param name="reader">The reader.</param>
      <param name="typeToConvert">The type to convert.</param>
      <param name="options">An object that specifies serialization options to use.</param>
      <returns>The converted value.</returns>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonConverter`1.ReadAsPropertyName(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions)">
      <summary>Reads a dictionary key from a JSON property name.</summary>
      <param name="reader">The <see cref="T:System.Text.Json.Utf8JsonReader" /> to read from.</param>
      <param name="typeToConvert">The type to convert.</param>
      <param name="options">The options to use when reading the value.</param>
      <returns>The value that was converted.</returns>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonConverter`1.Write(System.Text.Json.Utf8JsonWriter,`0,System.Text.Json.JsonSerializerOptions)">
      <summary>Writes a specified value as JSON.</summary>
      <param name="writer">The writer to write to.</param>
      <param name="value">The value to convert to JSON.</param>
      <param name="options">An object that specifies serialization options to use.</param>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonConverter`1.WriteAsPropertyName(System.Text.Json.Utf8JsonWriter,`0,System.Text.Json.JsonSerializerOptions)">
      <summary>Writes a dictionary key as a JSON property name.</summary>
      <param name="writer">The <see cref="T:System.Text.Json.Utf8JsonWriter" /> to write to.</param>
      <param name="value">The value to convert. The value of <see cref="P:System.Text.Json.Serialization.JsonConverter`1.HandleNull" /> determines if the converter handles <see langword="null" /> values.</param>
      <param name="options">The options to use when writing the value.</param>
    </member>
    <member name="P:System.Text.Json.Serialization.JsonConverter`1.HandleNull">
      <summary>Gets a value that indicates whether <see langword="null" /> should be passed to the converter on serialization, and whether <see cref="F:System.Text.Json.JsonTokenType.Null" /> should be passed on deserialization.</summary>
    </member>
    <member name="P:System.Text.Json.Serialization.JsonConverter`1.Type">
      <summary>Gets the type being converted by the current converter instance.</summary>
    </member>
    <member name="T:System.Text.Json.Serialization.JsonConverterAttribute">
      <summary>When placed on a property or type, specifies the converter type to use.</summary>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonConverterAttribute.#ctor">
      <summary>Initializes a new instance of <see cref="T:System.Text.Json.Serialization.JsonConverterAttribute" />.</summary>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonConverterAttribute.#ctor(System.Type)">
      <summary>Initializes a new instance of <see cref="T:System.Text.Json.Serialization.JsonConverterAttribute" /> with the specified converter type.</summary>
      <param name="converterType">The type of the converter.</param>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonConverterAttribute.CreateConverter(System.Type)">
      <summary>When overridden in a derived class and <see cref="P:System.Text.Json.Serialization.JsonConverterAttribute.ConverterType" /> is <see langword="null" />, allows the derived class to create a <see cref="T:System.Text.Json.Serialization.JsonConverter" /> in order to pass additional state.</summary>
      <param name="typeToConvert">The type of the converter.</param>
      <returns>The custom converter.</returns>
    </member>
    <member name="P:System.Text.Json.Serialization.JsonConverterAttribute.ConverterType">
      <summary>Gets the type of the <see cref="T:System.Text.Json.Serialization.JsonConverterAttribute" />, or <see langword="null" /> if it was created without a type.</summary>
      <returns>The type of the <see cref="T:System.Text.Json.Serialization.JsonConverterAttribute" />, or <see langword="null" /> if it was created without a type.</returns>
    </member>
    <member name="T:System.Text.Json.Serialization.JsonConverterFactory">
      <summary>Supports converting several types by using a factory pattern.</summary>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonConverterFactory.#ctor">
      <summary>When overridden in a derived class, initializes a new instance of the <see cref="T:System.Text.Json.Serialization.JsonConverterFactory" /> class.</summary>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonConverterFactory.CreateConverter(System.Type,System.Text.Json.JsonSerializerOptions)">
      <summary>Creates a converter for a specified type.</summary>
      <param name="typeToConvert">The type handled by the converter.</param>
      <param name="options">The serialization options to use.</param>
      <returns>A converter for which <typeparamref name="T" /> is compatible with <paramref name="typeToConvert" />.</returns>
    </member>
    <member name="P:System.Text.Json.Serialization.JsonConverterFactory.Type">
      <summary>Gets the type being converted by the current converter instance.</summary>
    </member>
    <member name="T:System.Text.Json.Serialization.JsonDerivedTypeAttribute">
      <summary>When placed on a type declaration, indicates that the specified subtype should be opted into polymorphic serialization.</summary>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonDerivedTypeAttribute.#ctor(System.Type,System.Int32)">
      <summary>Initializes a new attribute with specified parameters.</summary>
      <param name="derivedType">A derived type that should be supported in polymorphic serialization of the declared base type.</param>
      <param name="typeDiscriminator">The type discriminator identifier to be used for the serialization of the subtype.</param>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonDerivedTypeAttribute.#ctor(System.Type,System.String)">
<shellId: 12 completed with exit code 0>
```


---

<sub>59s</sub>

### `bash`

**Read converter base sources from package commit**

`$ curl -fsSL 'https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonConverter.cs' -o JsonConverter.cs && rg -n 'class JsonConverter|abstract |protected |public override |public virtual' JsonConverter.cs | head -80; curl -fsSL 'https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/JsonConverterFactory.cs' -o JsonConverterFactory.cs && rg -n 'abstract |public override|class JsonConverter' JsonConverterFactory.cs | head -60`

<details>
<summary>23 lines</summary>

```
16:    public abstract partial class JsonConverter
31:        public abstract Type? Type { get; }
38:        public abstract bool CanConvert(Type typeToConvert);
57:        private protected abstract ConverterStrategy GetDefaultConverterStrategy();
91:        internal bool RequiresReadAhead { get; private protected set; }
142:        internal bool UsesDefaultHandleNull { get; private protected set; }
148:        internal bool HandleNullOnRead { get; private protected init; }
154:        internal bool HandleNullOnWrite { get; private protected init; }
202:        internal abstract object? ReadAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);
203:        internal abstract bool OnTryReadAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options, scoped ref ReadStack state, out object? value);
204:        internal abstract bool TryReadAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options, scoped ref ReadStack state, out object? value);
205:        internal abstract object? ReadAsPropertyNameAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);
206:        internal abstract object? ReadAsPropertyNameCoreAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);
207:        internal abstract object? ReadNumberWithCustomHandlingAsObject(ref Utf8JsonReader reader, JsonNumberHandling handling, JsonSerializerOptions options);
209:        internal abstract void WriteAsObject(Utf8JsonWriter writer, object? value, JsonSerializerOptions options);
210:        internal abstract bool OnTryWriteAsObject(Utf8JsonWriter writer, object? value, JsonSerializerOptions options, ref WriteStack state);
211:        internal abstract bool TryWriteAsObject(Utf8JsonWriter writer, object? value, JsonSerializerOptions options, ref WriteStack state);
212:        internal abstract void WriteAsPropertyNameAsObject(Utf8JsonWriter writer, object? value, JsonSerializerOptions options);
213:        internal abstract void WriteAsPropertyNameCoreAsObject(Utf8JsonWriter writer, object? value, JsonSerializerOptions options, bool isWritingExtensionDataProperty);
214:        internal abstract void WriteNumberWithCustomHandlingAsObject(Utf8JsonWriter writer, object? value, JsonNumberHandling handling);
15:    public abstract class JsonConverterFactory : JsonConverter
33:        public abstract JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options);
<shellId: 13 completed with exit code 0>
```

</details>


---

<sub>1m 1s</sub>

### `bash`

**Confirm converter extensibility and inherited overrides**

`$ sed -n '12,70p' JsonConverter.cs && sed -n '10,60p' JsonConverterFactory.cs && rg -n 'public (sealed )?class JsonStringEnumConverter|public (sealed )?class JsonNumberEnumConverter|override bool CanConvert|override JsonConverter.*CreateConverter|CreateConverter\(' package/lib/net10.0/System.Text.Json.xml | head -32 && sed -n '3990,4030p' package/lib/net10.0/System.Text.Json.xml && sed -n '4245,4290p' package/lib/net10.0/System.Text.Json.xml`

```
{
    /// <summary>
    /// Converts an object or value to or from JSON.
    /// </summary>
    public abstract partial class JsonConverter
    {
        internal JsonConverter()
        {
            IsInternalConverter = GetType().Assembly == typeof(JsonConverter).Assembly;
            ConverterStrategy = GetDefaultConverterStrategy();
        }

        /// <summary>
        /// Gets the type being converted by the current converter instance.
        /// </summary>
        /// <remarks>
        /// For instances of type <see cref="JsonConverter{T}"/> returns typeof(T),
        /// and for instances of type <see cref="JsonConverterFactory"/> returns <see langword="null" />.
        /// </remarks>
        public abstract Type? Type { get; }

        /// <summary>
        /// Determines whether the type can be converted.
        /// </summary>
        /// <param name="typeToConvert">The type is checked as to whether it can be converted.</param>
        /// <returns>True if the type can be converted, false otherwise.</returns>
        public abstract bool CanConvert(Type typeToConvert);

        internal ConverterStrategy ConverterStrategy
        {
            get => _converterStrategy;
            init
            {
                CanUseDirectReadOrWrite = value == ConverterStrategy.Value && IsInternalConverter;
                RequiresReadAhead = value == ConverterStrategy.Value;
                _converterStrategy = value;
            }
        }

        private ConverterStrategy _converterStrategy;

        /// <summary>
        /// Invoked by the base contructor to populate the initial value of the <see cref="ConverterStrategy"/> property.
        /// Used for declaring the default strategy for specific converter hierarchies without explicitly setting in a constructor.
        /// </summary>
        private protected abstract ConverterStrategy GetDefaultConverterStrategy();

        /// <summary>
        /// Indicates that the converter can consume the <see cref="JsonTypeInfo.CreateObject"/> delegate.
        /// Needed because certain collection converters cannot support arbitrary delegates.
        /// TODO remove once https://github.com/dotnet/runtime/pull/73395/ and
        /// https://github.com/dotnet/runtime/issues/71944 have been addressed.
        /// </summary>
        internal virtual bool SupportsCreateObjectDelegate => false;

        /// <summary>
        /// Indicates that the converter is compatible with <see cref="JsonObjectCreationHandling.Populate"/>.
        /// </summary>
        internal virtual bool CanPopulate => false;
    /// Supports converting several types by using a factory pattern.
    /// </summary>
    /// <remarks>
    /// This is useful for converters supporting generics, such as a converter for <see cref="System.Collections.Generic.List{T}"/>.
    /// </remarks>
    public abstract class JsonConverterFactory : JsonConverter
    {
        /// <summary>
        /// When overridden, constructs a new <see cref="JsonConverterFactory"/> instance.
        /// </summary>
        protected JsonConverterFactory() { }

        private protected override ConverterStrategy GetDefaultConverterStrategy() => ConverterStrategy.None;

        /// <summary>
        /// Create a converter for the provided <see cref="System.Type"/>.
        /// </summary>
        /// <param name="typeToConvert">The <see cref="System.Type"/> being converted.</param>
        /// <param name="options">The <see cref="JsonSerializerOptions"/> being used.</param>
        /// <returns>
        /// An instance of a <see cref="JsonConverter{T}"/> where T is compatible with <paramref name="typeToConvert"/>.
        /// If <see langword="null"/> is returned, a <see cref="NotSupportedException"/> will be thrown.
        /// </returns>
        public abstract JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options);

        internal JsonConverter GetConverterInternal(Type typeToConvert, JsonSerializerOptions options)
        {
            Debug.Assert(CanConvert(typeToConvert));

            JsonConverter? converter = CreateConverter(typeToConvert, options);
            switch (converter)
            {
                case null:
                    ThrowHelper.ThrowInvalidOperationException_SerializerConverterFactoryReturnsNull(GetType());
                    break;
                case JsonConverterFactory:
                    ThrowHelper.ThrowInvalidOperationException_SerializerConverterFactoryReturnsJsonConverterFactorty(GetType());
                    break;
            }

            return converter;
        }

        internal sealed override object? ReadAsObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Debug.Fail("We should never get here.");

            throw new InvalidOperationException();
        }

        internal sealed override bool OnTryReadAsObject(
3873:    <member name="M:System.Text.Json.Serialization.JsonConverterAttribute.CreateConverter(System.Type)">
3888:    <member name="M:System.Text.Json.Serialization.JsonConverterFactory.CreateConverter(System.Type,System.Text.Json.JsonSerializerOptions)">
4009:    <member name="M:System.Text.Json.Serialization.JsonNumberEnumConverter`1.CreateConverter(System.Type,System.Text.Json.JsonSerializerOptions)">
4267:    <member name="M:System.Text.Json.Serialization.JsonStringEnumConverter.CreateConverter(System.Type,System.Text.Json.JsonSerializerOptions)">
4292:    <member name="M:System.Text.Json.Serialization.JsonStringEnumConverter`1.CreateConverter(System.Type,System.Text.Json.JsonSerializerOptions)">
    <member name="F:System.Text.Json.Serialization.JsonKnownReferenceHandler.Preserve">
      <summary>Specifies that the built-in <see cref="P:System.Text.Json.Serialization.ReferenceHandler.Preserve" /> be used to handle references.</summary>
    </member>
    <member name="F:System.Text.Json.Serialization.JsonKnownReferenceHandler.Unspecified">
      <summary>Specifies that circular references should throw exceptions.</summary>
    </member>
    <member name="T:System.Text.Json.Serialization.JsonNumberEnumConverter`1">
      <summary>Converter to convert enums to and from numeric values.</summary>
      <typeparam name="TEnum">The enum type that this converter targets.</typeparam>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonNumberEnumConverter`1.#ctor">
      <summary>Initializes a new instance of <see cref="T:System.Text.Json.Serialization.JsonNumberEnumConverter`1" />.</summary>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonNumberEnumConverter`1.CanConvert(System.Type)">
      <summary>When overridden in a derived class, determines whether the converter instance can convert the specified object type.</summary>
      <param name="typeToConvert">The type of the object to check whether it can be converted by this converter instance.</param>
      <returns>
        <see langword="true" /> if the instance can convert the specified object type; otherwise, <see langword="false" />.</returns>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonNumberEnumConverter`1.CreateConverter(System.Type,System.Text.Json.JsonSerializerOptions)">
      <summary>Creates a converter for a specified type.</summary>
      <param name="typeToConvert">The type handled by the converter.</param>
      <param name="options">The serialization options to use.</param>
      <returns>A converter for which <typeparamref name="T" /> is compatible with <paramref name="typeToConvert" />.</returns>
    </member>
    <member name="T:System.Text.Json.Serialization.JsonNumberHandling">
      <summary>Determines how <see cref="T:System.Text.Json.JsonSerializer" /> handles numbers when serializing and deserializing.</summary>
    </member>
    <member name="F:System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals">
      <summary>The "NaN", "Infinity", and "-Infinity" <see cref="F:System.Text.Json.JsonTokenType.String" /> tokens can be read as floating-point constants, and the <see cref="T:System.Single" /> and <see cref="T:System.Double" /> values for these constants will be written as their corresponding JSON string representations.</summary>
    </member>
    <member name="F:System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString">
      <summary>Numbers can be read from <see cref="F:System.Text.Json.JsonTokenType.String" /> tokens. Does not prevent numbers from being read from <see cref="F:System.Text.Json.JsonTokenType.Number" /> token.</summary>
    </member>
    <member name="F:System.Text.Json.Serialization.JsonNumberHandling.Strict">
      <summary>Numbers will only be read from <see cref="F:System.Text.Json.JsonTokenType.Number" /> tokens and will only be written as JSON numbers (without quotes).</summary>
    </member>
    <member name="F:System.Text.Json.Serialization.JsonNumberHandling.WriteAsString">
      <summary>Numbers will be written as JSON strings (with quotes), not as JSON numbers.</summary>
    </member>
    <member name="T:System.Text.Json.Serialization.JsonNumberHandlingAttribute">
    </member>
    <member name="P:System.Text.Json.Serialization.JsonSourceGenerationOptionsAttribute.WriteIndented">
      <summary>Gets or sets a value that indicates whether JSON output is pretty-printed.</summary>
    </member>
    <member name="T:System.Text.Json.Serialization.JsonStringEnumConverter">
      <summary>Converts enumeration values to and from strings.</summary>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonStringEnumConverter.#ctor">
      <summary>Initializes an instance of the <see cref="T:System.Text.Json.Serialization.JsonStringEnumConverter" /> class with the default naming policy that allows integer values.</summary>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonStringEnumConverter.#ctor(System.Text.Json.JsonNamingPolicy,System.Boolean)">
      <summary>Initializes an instance of the <see cref="T:System.Text.Json.Serialization.JsonStringEnumConverter" /> class with a specified naming policy and a value that indicates whether undefined enumeration values are allowed.</summary>
      <param name="namingPolicy">The optional naming policy for writing enum values.</param>
      <param name="allowIntegerValues">
        <see langword="true" /> to allow undefined enum values; otherwise, <see langword="false" />. When <see langword="true" />, if an enum value isn't defined, it will output as a number rather than a string.</param>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonStringEnumConverter.CanConvert(System.Type)">
      <summary>Determines whether the specified type can be converted to an enum.</summary>
      <param name="typeToConvert">The type to be checked.</param>
      <returns>
        <see langword="true" /> if the type can be converted; otherwise, <see langword="false" />.</returns>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonStringEnumConverter.CreateConverter(System.Type,System.Text.Json.JsonSerializerOptions)">
      <summary>Creates a converter for the specified type.</summary>
      <param name="typeToConvert">The type handled by the converter.</param>
      <param name="options">The serialization options to use.</param>
      <returns>A converter for which <typeparamref name="T" /> is compatible with <paramref name="typeToConvert" />.</returns>
    </member>
    <member name="T:System.Text.Json.Serialization.JsonStringEnumConverter`1">
      <summary>Converter to convert enums to and from strings.</summary>
      <typeparam name="TEnum">The enum type that this converter targets.</typeparam>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonStringEnumConverter`1.#ctor">
      <summary>Initializes a new instance of <see cref="T:System.Text.Json.Serialization.JsonStringEnumConverter`1" /> with the default naming policy and that allows integer values.</summary>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonStringEnumConverter`1.#ctor(System.Text.Json.JsonNamingPolicy,System.Boolean)">
      <summary>Initializes a new instance of <see cref="T:System.Text.Json.Serialization.JsonStringEnumConverter`1" />.</summary>
      <param name="namingPolicy">Optional naming policy for writing enum values.</param>
      <param name="allowIntegerValues">
        <see langword="true" /> to allow undefined enum values. When <see langword="true" />, if an enum value isn't defined, it outputs as a number rather than a string.</param>
    </member>
    <member name="M:System.Text.Json.Serialization.JsonStringEnumConverter`1.CanConvert(System.Type)">
      <summary>When overridden in a derived class, determines whether the converter instance can convert the specified object type.</summary>
      <param name="typeToConvert">The type of the object to check whether it can be converted by this converter instance.</param>
      <returns>
        <see langword="true" /> if the instance can convert the specified object type; otherwise, <see langword="false" />.</returns>
<shellId: 14 completed with exit code 0>
```


---

<sub>1m 7s</sub>

### `apply_patch`

<details>
<summary>Arguments</summary>

```json
"*** Begin Patch\n*** Update File: probe/Program.cs\n@@\n-        name = type.IsGenericParameter ? type.Name : type.FullName?.Split('`')[0].Replace('+', '.') ?? type.Name;\n+        name = type.IsGenericParameter ? type.Name : type.IsGenericType\n+            ? type.GetGenericTypeDefinition().FullName!.Split('`')[0].Replace('+', '.')\n+            : type.FullName?.Replace('+', '.') ?? type.Name;\n@@\n-            formatted += \" = \" + (value == null ? \"null\" : value is bool b ? (b ? \"true\" : \"false\") :\n+            formatted += \" = \" + (value == null ? (p.ParameterType.IsValueType ? \"default\" : \"null\") : value is bool b ? (b ? \"true\" : \"false\") :\n@@\n Console.WriteLine(\"\\n=== RUC TYPES ===\");\n foreach (var t in all.Where(IsRuc)) Console.WriteLine(t.FullName);\n \n+Console.WriteLine(\"\\n=== RUC PUBLIC/PROTECTED GROUPS ===\");\n+foreach (var g in types.SelectMany(Methods).Where(m => IsPublicOrProtected(m) && IsRuc(m))\n+             .GroupBy(m => (m.DeclaringType!.FullName, m.Name))\n+             .OrderBy(g => g.Key.FullName).ThenBy(g => g.Key.Name))\n+{\n+    var same = Methods(g.First().DeclaringType!).Any(m => IsPublicOrProtected(m) && m.Name == g.Key.Name && !IsRuc(m));\n+    Console.WriteLine($\"{g.Key.FullName} :: {g.Key.Name} count={g.Count()} unmarked-public-protected-overload={same}\");\n+}\n+Console.WriteLine(\"\\n=== RUC PUBLIC/PROTECTED PROPERTIES ===\");\n+foreach (var t in types)\n+    foreach (var p in t.GetProperties(flags).Where(p => IsRuc(p)))\n+        Console.WriteLine(t.FullName + \" :: \" + p.Name);\n+\n Console.WriteLine(\"\\n=== CONVERTER TYPES PUBLIC ===\");\n*** End Patch\n"
```

</details>

```
Modified 1 file(s): /private/tmp/cli-trial-20261106/baseline-2/probe/Program.cs
```


---

<sub>1m 10s</sub>

### `bash`

**Confirm public RUC groups and corrected API signatures**

`$ dotnet run --project probe -f net10.0 > probe-output.txt && sed -n '/=== RUC PUBLIC\/PROTECTED GROUPS ===/,/=== CONVERTER TYPES PUBLIC ===/p' probe-output.txt && sed -n '1,57p' probe-output.txt`

<details>
<summary>78 lines</summary>

```
=== RUC PUBLIC/PROTECTED GROUPS ===
System.Text.Json.JsonSerializer :: Deserialize count=16 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: DeserializeAsync count=4 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: DeserializeAsyncEnumerable count=4 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: Serialize count=6 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: SerializeAsync count=4 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: SerializeToDocument count=2 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: SerializeToElement count=2 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: SerializeToNode count=2 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: SerializeToUtf8Bytes count=2 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializerOptions :: get_Default count=1 unmarked-public-protected-overload=False
System.Text.Json.JsonSerializerOptions :: get_Strict count=1 unmarked-public-protected-overload=False
System.Text.Json.JsonSerializerOptions :: get_Web count=1 unmarked-public-protected-overload=False
System.Text.Json.JsonSerializerOptions :: GetConverter count=1 unmarked-public-protected-overload=False
System.Text.Json.JsonSerializerOptions :: MakeReadOnly count=1 unmarked-public-protected-overload=True
System.Text.Json.Nodes.JsonArray :: Add count=1 unmarked-public-protected-overload=True
System.Text.Json.Nodes.JsonNode :: ReplaceWith count=1 unmarked-public-protected-overload=False
System.Text.Json.Nodes.JsonValue :: Create count=1 unmarked-public-protected-overload=True
System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: .ctor count=1 unmarked-public-protected-overload=False
System.Text.Json.Serialization.Metadata.JsonTypeInfo :: CreateJsonPropertyInfo count=1 unmarked-public-protected-overload=False
System.Text.Json.Serialization.Metadata.JsonTypeInfo :: CreateJsonTypeInfo count=2 unmarked-public-protected-overload=False

=== RUC PUBLIC/PROTECTED PROPERTIES ===

=== CONVERTER TYPES PUBLIC ===
ASSEMBLY System.Text.Json, Version=10.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51 /private/tmp/cli-trial-20261106/baseline-2/package/lib/net10.0/System.Text.Json.dll
TYPES 84

=== STREAM PUBLIC/PROTECTED ===
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument Parse(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default)
System.Text.Json.JsonDocument :: public static System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ParseAsync(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static object? Deserialize(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
System.Text.Json.JsonSerializer :: public static object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
System.Text.Json.JsonSerializer :: public static object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context)
System.Text.Json.JsonSerializer :: public static TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null)
System.Text.Json.JsonSerializer :: public static TValue? Deserialize<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue?> jsonTypeInfo)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.ValueTask<object?> DeserializeAsync(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.Serialization.JsonSerializerContext context, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.ValueTask<TValue?> DeserializeAsync<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.ValueTask<TValue?> DeserializeAsync<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue?> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, bool topLevelValues, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue?> jsonTypeInfo, bool topLevelValues, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static System.Collections.Generic.IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(System.IO.Stream utf8Json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue?> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static void Serialize(System.IO.Stream utf8Json, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
System.Text.Json.JsonSerializer :: public static void Serialize(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)
System.Text.Json.JsonSerializer :: public static void Serialize(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
System.Text.Json.JsonSerializer :: public static void Serialize<TValue>(System.IO.Stream utf8Json, TValue? value, System.Text.Json.JsonSerializerOptions? options = null)
System.Text.Json.JsonSerializer :: public static void Serialize<TValue>(System.IO.Stream utf8Json, TValue? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue?> jsonTypeInfo)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.Task SerializeAsync(System.IO.Stream utf8Json, object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Stream utf8Json, TValue? value, System.Text.Json.JsonSerializerOptions? options = null, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonSerializer :: public static System.Threading.Tasks.Task SerializeAsync<TValue>(System.IO.Stream utf8Json, TValue? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue?> jsonTypeInfo, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.Nodes.JsonNode :: public static System.Text.Json.Nodes.JsonNode? Parse(System.IO.Stream utf8Json, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> nodeOptions = default, System.Text.Json.JsonDocumentOptions documentOptions = default)
System.Text.Json.Nodes.JsonNode :: public static System.Threading.Tasks.Task<System.Text.Json.Nodes.JsonNode?> ParseAsync(System.IO.Stream utf8Json, System.Nullable<System.Text.Json.Nodes.JsonNodeOptions> nodeOptions = default, System.Text.Json.JsonDocumentOptions documentOptions = default, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.Utf8JsonWriter :: public Utf8JsonWriter(System.IO.Stream utf8Json, System.Text.Json.JsonWriterOptions options = default)
System.Text.Json.Utf8JsonWriter :: public void Reset(System.IO.Stream utf8Json)

=== JSONDOCUMENT RESULTS PUBLIC/PROTECTED ===
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument Parse(string json, System.Text.Json.JsonDocumentOptions options = default)
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument Parse(System.Buffers.ReadOnlySequence<byte> utf8Json, System.Text.Json.JsonDocumentOptions options = default)
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument Parse(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default)
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument Parse(System.ReadOnlyMemory<byte> utf8Json, System.Text.Json.JsonDocumentOptions options = default)
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument Parse(System.ReadOnlyMemory<char> json, System.Text.Json.JsonDocumentOptions options = default)
System.Text.Json.JsonDocument :: public static System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ParseAsync(System.IO.Stream utf8Json, System.Text.Json.JsonDocumentOptions options = default, System.Threading.CancellationToken cancellationToken = default)
System.Text.Json.JsonDocument :: public static System.Text.Json.JsonDocument ParseValue(ref System.Text.Json.Utf8JsonReader reader)
System.Text.Json.JsonDocument :: public static bool TryParseValue(ref System.Text.Json.Utf8JsonReader reader, out System.Text.Json.JsonDocument document)
System.Text.Json.JsonSerializer :: public static System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo jsonTypeInfo)
System.Text.Json.JsonSerializer :: public static System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Type inputType, System.Text.Json.JsonSerializerOptions? options = null)
System.Text.Json.JsonSerializer :: public static System.Text.Json.JsonDocument SerializeToDocument(object? value, System.Type inputType, System.Text.Json.Serialization.JsonSerializerContext context)
System.Text.Json.JsonSerializer :: public static System.Text.Json.JsonDocument SerializeToDocument<TValue>(TValue? value, System.Text.Json.JsonSerializerOptions? options = null)
System.Text.Json.JsonSerializer :: public static System.Text.Json.JsonDocument SerializeToDocument<TValue>(TValue? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue?> jsonTypeInfo)

=== RUC ALL DECLARED METHODS (including internal/private) ===
System.Text.Json.JsonSerializer :: Deserialize count=16 same-named-unmarked=24
  + public static object? Deserialize(ref System.Text.Json.Utf8JsonReader reader, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static object? Deserialize(string json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static object? Deserialize(System.IO.Stream utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static object? Deserialize(System.ReadOnlySpan<byte> utf8Json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
  + public static object? Deserialize(System.ReadOnlySpan<char> json, System.Type returnType, System.Text.Json.JsonSerializerOptions? options = null)
<shellId: 15 completed with exit code 0>
```

</details>


---

<sub>1m 16s</sub>

### `bash`

**Find source definition for ParseAsync and TryParseValue**

`$ for file in JsonDocument.Parse.cs JsonDocument.cs; do url="https://raw.githubusercontent.com/dotnet/dotnet/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Document/$file"; if curl -fsSL "$url" -o "$file"; then rg -n 'ParseAsync\(|TryParseValue\(' "$file" | head -12; fi; done`

<details>
<summary>6 lines</summary>

```
208:        public static Task<JsonDocument> ParseAsync(
394:        public static bool TryParseValue(ref Utf8JsonReader reader, [NotNullWhen(true)] out JsonDocument? document)
396:            return TryParseValue(ref reader, out document, shouldThrow: false, useArrayPools: true);
439:            bool ret = TryParseValue(ref reader, out JsonDocument? document, shouldThrow: true, useArrayPools: true, allowDuplicateProperties);
446:        internal static bool TryParseValue(
<shellId: 16 completed with exit code 0>
```

</details>


---

<sub>1m 16s</sub>

### `bash`

**Cross-check XML summary and source signature patterns**

`$ rg -n 'M:System.Text.Json.JsonDocument.TryParseValue|M:System.Text.Json.JsonDocument.Parse\(' package/lib/net10.0/System.Text.Json.xml | head -12; rg -n '^        public static|^            Stream utf8Json|^            JsonTypeInfo|^            Type returnType' JsonSerializer.Read.Stream.cs | sed -n '1,57p'; sed -n '85,112p' package/lib/net10.0/System.Text.Json.xml`

```
40:    <member name="M:System.Text.Json.JsonDocument.Parse(System.Buffers.ReadOnlySequence{System.Byte},System.Text.Json.JsonDocumentOptions)">
50:    <member name="M:System.Text.Json.JsonDocument.Parse(System.IO.Stream,System.Text.Json.JsonDocumentOptions)">
60:    <member name="M:System.Text.Json.JsonDocument.Parse(System.ReadOnlyMemory{System.Byte},System.Text.Json.JsonDocumentOptions)">
70:    <member name="M:System.Text.Json.JsonDocument.Parse(System.ReadOnlyMemory{System.Char},System.Text.Json.JsonDocumentOptions)">
80:    <member name="M:System.Text.Json.JsonDocument.Parse(System.String,System.Text.Json.JsonDocumentOptions)">
114:    <member name="M:System.Text.Json.JsonDocument.TryParseValue(System.Text.Json.Utf8JsonReader@,System.Text.Json.JsonDocument@)">
43:        public static ValueTask<TValue?> DeserializeAsync<TValue>(
44:            Stream utf8Json,
50:            JsonTypeInfo<TValue> jsonTypeInfo = GetTypeInfo<TValue>(options);
76:        public static TValue? Deserialize<TValue>(
77:            Stream utf8Json,
82:            JsonTypeInfo<TValue> jsonTypeInfo = GetTypeInfo<TValue>(options);
111:        public static ValueTask<object?> DeserializeAsync(
112:            Stream utf8Json,
113:            Type returnType,
120:            JsonTypeInfo jsonTypeInfo = GetTypeInfo(options, returnType);
146:        public static object? Deserialize(
147:            Stream utf8Json,
148:            Type returnType,
154:            JsonTypeInfo jsonTypeInfo = GetTypeInfo(options, returnType);
177:        public static ValueTask<TValue?> DeserializeAsync<TValue>(
178:            Stream utf8Json,
179:            JsonTypeInfo<TValue> jsonTypeInfo,
206:        public static ValueTask<object?> DeserializeAsync(
207:            Stream utf8Json,
208:            JsonTypeInfo jsonTypeInfo,
234:        public static TValue? Deserialize<TValue>(
235:            Stream utf8Json,
236:            JsonTypeInfo<TValue> jsonTypeInfo)
259:        public static object? Deserialize(
260:            Stream utf8Json,
261:            JsonTypeInfo jsonTypeInfo)
297:        public static ValueTask<object?> DeserializeAsync(
298:            Stream utf8Json,
299:            Type returnType,
307:            JsonTypeInfo jsonTypeInfo = GetTypeInfo(context, returnType);
335:        public static object? Deserialize(
336:            Stream utf8Json,
337:            Type returnType,
344:            JsonTypeInfo jsonTypeInfo = GetTypeInfo(context, returnType);
362:        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
363:            Stream utf8Json,
391:        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
392:            Stream utf8Json,
399:            JsonTypeInfo<TValue> jsonTypeInfo = GetTypeInfo<TValue>(options);
415:        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
416:            Stream utf8Json,
417:            JsonTypeInfo<TValue> jsonTypeInfo,
442:        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
443:            Stream utf8Json,
444:            JsonTypeInfo<TValue> jsonTypeInfo,
456:            Stream utf8Json,
457:            JsonTypeInfo<T> jsonTypeInfo,
463:            JsonTypeInfo<List<T?>> listTypeInfo;
        <paramref name="json" /> does not represent a valid single JSON value.</exception>
      <exception cref="T:System.ArgumentException">
        <paramref name="options" /> contains unsupported options.</exception>
      <returns>A JsonDocument representation of the JSON value.</returns>
    </member>
    <member name="M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)">
      <summary>Parses a <see cref="T:System.IO.Stream" /> as UTF-8-encoded data representing a single JSON value into a JsonDocument. The stream is read to completion.</summary>
      <param name="utf8Json">The JSON data to parse.</param>
      <param name="options">Options to control the reader behavior during parsing.</param>
      <param name="cancellationToken">The token to monitor for cancellation requests.</param>
      <exception cref="T:System.Text.Json.JsonException">
        <paramref name="utf8Json" /> does not represent a valid single JSON value.</exception>
      <exception cref="T:System.ArgumentException">
        <paramref name="options" /> contains unsupported options.</exception>
      <exception cref="T:System.OperationCanceledException">The cancellation token was canceled. This exception is stored into the returned task.</exception>
      <returns>A task to produce a JsonDocument representation of the JSON value.</returns>
    </member>
    <member name="M:System.Text.Json.JsonDocument.ParseValue(System.Text.Json.Utf8JsonReader@)">
      <summary>Parses one JSON value (including objects or arrays) from the provided reader.</summary>
      <param name="reader">The reader to read.</param>
      <exception cref="T:System.ArgumentException">
        <paramref name="reader" /> contains unsupported options.

-or-

The current <paramref name="reader" /> token does not start or represent a value.</exception>
      <exception cref="T:System.Text.Json.JsonException">A value could not be read from the reader.</exception>
      <returns>A JsonDocument representing the value (and nested values) read from the reader.</returns>
<shellId: 17 completed with exit code 0>
```


---

<sub>1m 26s</sub>

### `bash`

**Confirm exact ParseAsync and nullable out document signature**

`$ sed -n '187,233p' JsonDocument.Parse.cs && sed -n '388,404p' JsonDocument.Parse.cs && sed -n '102,125p' package/lib/net10.0/System.Text.Json.xml && rg -n 'public static JsonDocument SerializeToDocument|public static .*SerializeToDocument\<' package/lib/net10.0/System.Text.Json.xml | head -10 && sed -n '354,397p' probe-output.txt | head -44`

```
{
            Debug.Assert(json != null);
            return ParseValue(json.AsSpan(), options);
        }

        /// <summary>
        ///   Parse a <see cref="Stream"/> as UTF-8 encoded data representing a single JSON value into a
        ///   JsonDocument.  The Stream will be read to completion.
        /// </summary>
        /// <param name="utf8Json">JSON data to parse.</param>
        /// <param name="options">Options to control the reader behavior during parsing.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>
        ///   A Task to produce a JsonDocument representation of the JSON value.
        /// </returns>
        /// <exception cref="JsonException">
        ///   <paramref name="utf8Json"/> does not represent a valid single JSON value.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///   <paramref name="options"/> contains unsupported options.
        /// </exception>
        public static Task<JsonDocument> ParseAsync(
            Stream utf8Json,
            JsonDocumentOptions options = default,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(utf8Json);

            return ParseAsyncCore(utf8Json, options, cancellationToken);
        }

        private static async Task<JsonDocument> ParseAsyncCore(
            Stream utf8Json,
            JsonDocumentOptions options = default,
            CancellationToken cancellationToken = default)
        {
            ArraySegment<byte> drained = await ReadToEndAsync(utf8Json, cancellationToken).ConfigureAwait(false);
            Debug.Assert(drained.Array != null);
            try
            {
                return Parse(
                    drained.AsMemory(),
                    options.GetReaderOptions(),
                    drained.Array,
                    allowDuplicateProperties: options.AllowDuplicateProperties);
            }
            catch
        /// <exception cref="ArgumentException">
        ///   The current <paramref name="reader"/> token does not start or represent a value.
        /// </exception>
        /// <exception cref="JsonException">
        ///   A value could not be read from the reader.
        /// </exception>
        public static bool TryParseValue(ref Utf8JsonReader reader, [NotNullWhen(true)] out JsonDocument? document)
        {
            return TryParseValue(ref reader, out document, shouldThrow: false, useArrayPools: true);
        }

        /// <summary>
        ///   Parses one JSON value (including objects or arrays) from the provided reader.
        /// </summary>
        /// <param name="reader">The reader to read.</param>
        /// <returns>
        ///   A JsonDocument representing the value (and nested values) read from the reader.
    <member name="M:System.Text.Json.JsonDocument.ParseValue(System.Text.Json.Utf8JsonReader@)">
      <summary>Parses one JSON value (including objects or arrays) from the provided reader.</summary>
      <param name="reader">The reader to read.</param>
      <exception cref="T:System.ArgumentException">
        <paramref name="reader" /> contains unsupported options.

-or-

The current <paramref name="reader" /> token does not start or represent a value.</exception>
      <exception cref="T:System.Text.Json.JsonException">A value could not be read from the reader.</exception>
      <returns>A JsonDocument representing the value (and nested values) read from the reader.</returns>
    </member>
    <member name="M:System.Text.Json.JsonDocument.TryParseValue(System.Text.Json.Utf8JsonReader@,System.Text.Json.JsonDocument@)">
      <summary>Attempts to parse one JSON value (including objects or arrays) from the provided reader.</summary>
      <param name="reader">The reader to read.</param>
      <param name="document">When the method returns, contains the parsed document.</param>
      <exception cref="T:System.ArgumentException">
        <paramref name="reader" /> contains unsupported options.

-or-

The current <paramref name="reader" /> token does not start or represent a value.</exception>
      <exception cref="T:System.Text.Json.JsonException">A value could not be read from the reader.</exception>
      <returns>

=== RUC PUBLIC/PROTECTED GROUPS ===
System.Text.Json.JsonSerializer :: Deserialize count=16 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: DeserializeAsync count=4 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: DeserializeAsyncEnumerable count=4 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: Serialize count=6 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: SerializeAsync count=4 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: SerializeToDocument count=2 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: SerializeToElement count=2 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: SerializeToNode count=2 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializer :: SerializeToUtf8Bytes count=2 unmarked-public-protected-overload=True
System.Text.Json.JsonSerializerOptions :: get_Default count=1 unmarked-public-protected-overload=False
System.Text.Json.JsonSerializerOptions :: get_Strict count=1 unmarked-public-protected-overload=False
System.Text.Json.JsonSerializerOptions :: get_Web count=1 unmarked-public-protected-overload=False
System.Text.Json.JsonSerializerOptions :: GetConverter count=1 unmarked-public-protected-overload=False
System.Text.Json.JsonSerializerOptions :: MakeReadOnly count=1 unmarked-public-protected-overload=True
System.Text.Json.Nodes.JsonArray :: Add count=1 unmarked-public-protected-overload=True
System.Text.Json.Nodes.JsonNode :: ReplaceWith count=1 unmarked-public-protected-overload=False
System.Text.Json.Nodes.JsonValue :: Create count=1 unmarked-public-protected-overload=True
System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver :: .ctor count=1 unmarked-public-protected-overload=False
System.Text.Json.Serialization.Metadata.JsonTypeInfo :: CreateJsonPropertyInfo count=1 unmarked-public-protected-overload=False
System.Text.Json.Serialization.Metadata.JsonTypeInfo :: CreateJsonTypeInfo count=2 unmarked-public-protected-overload=False

=== RUC PUBLIC/PROTECTED PROPERTIES ===

=== CONVERTER TYPES PUBLIC ===
System.Text.Json.Serialization.JsonConverter abstract=True sealed=False base=System.Object
  constructors: 
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeToConvert)
System.Text.Json.Serialization.JsonConverter`1 abstract=True sealed=False base=System.Text.Json.Serialization.JsonConverter
  constructors: protected internal JsonConverter`1()
  abstract System.Text.Json.Serialization.JsonConverter`1 :: public abstract T? Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
  abstract System.Text.Json.Serialization.JsonConverter`1 :: public abstract void Write(System.Text.Json.Utf8JsonWriter writer, T value, System.Text.Json.JsonSerializerOptions options)
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeToConvert)
System.Text.Json.Serialization.JsonConverterAttribute abstract=False sealed=False base=System.Text.Json.Serialization.JsonAttribute
  constructors: public JsonConverterAttribute(System.Type converterType); protected JsonConverterAttribute()
System.Text.Json.Serialization.JsonConverterFactory abstract=True sealed=False base=System.Text.Json.Serialization.JsonConverter
  constructors: protected JsonConverterFactory()
  abstract System.Text.Json.Serialization.JsonConverterFactory :: public abstract System.Text.Json.Serialization.JsonConverter? CreateConverter(System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract System.Type? get_Type()
  abstract System.Text.Json.Serialization.JsonConverter :: public abstract bool CanConvert(System.Type typeToConvert)
System.Text.Json.Serialization.JsonNumberEnumConverter`1 abstract=False sealed=True base=System.Text.Json.Serialization.JsonConverterFactory
<shellId: 18 completed with exit code 0>
```


---

<sub>1m 52s</sub>

### Copilot

The metadata scan found 30 public Stream-parameter APIs, 13 APIs that yield a `JsonDocument`, and 20 public/protected groups with `RequiresUnreferencedCode`. I’ll assemble the cited answers and distinguish public APIs from private implementation methods.

---

<sub>1m 52s</sub>

### `apply_patch`

<details>
<summary>Arguments</summary>

```json
"*** Begin Patch\n*** Add File: make_answer.py\n+import json\n+import re\n+import xml.etree.ElementTree as ET\n+from pathlib import Path\n+\n+root = Path.cwd()\n+report = (root / \"probe-output.txt\").read_text()\n+xml_file = \"package/lib/net10.0/System.Text.Json.xml\"\n+dll_file = \"package/lib/net10.0/System.Text.Json.dll\"\n+nuspec_file = \"package/System.Text.Json.nuspec\"\n+probe_command = \"dotnet run --project probe -f net10.0 > probe-output.txt\"\n+commit = \"0427b9abbead3fa64c6655eb4290c537397131fa\"\n+source_base = f\"https://github.com/dotnet/dotnet/blob/{commit}/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json\"\n+read_source = source_base + \"/Serialization/JsonSerializer.Read.Stream.cs\"\n+document_source = source_base + \"/Document/JsonDocument.Parse.cs\"\n+\n+\n+def section(name):\n+    return report.split(\"=== \" + name + \" ===\\n\", 1)[1].split(\"\\n===\", 1)[0].strip().splitlines()\n+\n+\n+def api_list(name):\n+    entries = []\n+    for line in section(name):\n+        declaring_type, signature = line.split(\" :: \", 1)\n+        if \"out System.Text.Json.JsonDocument document\" in signature:\n+            signature = signature.replace(\"out System.Text.Json.JsonDocument document\",\n+                                          \"out System.Text.Json.JsonDocument? document\")\n+        # NullabilityInfoContext reports the open generic argument as nullable,\n+        # while the original C# declaration spells it JsonTypeInfo<TValue>.\n+        signature = signature.replace(\"JsonTypeInfo<TValue?>\", \"JsonTypeInfo<TValue>\")\n+        entries.append({\"declaring_type\": declaring_type, \"signature\": signature})\n+    return entries\n+\n+\n+stream_apis = api_list(\"STREAM PUBLIC/PROTECTED\")\n+document_apis = api_list(\"JSONDOCUMENT RESULTS PUBLIC/PROTECTED\")\n+marked = {}\n+current = None\n+for line in section(\"RUC ALL DECLARED METHODS (including internal/private)\"):\n+    match = re.match(r\"^(.+?) :: (\\S+) count=\\d+ same-named-unmarked=\\d+$\", line)\n+    if match:\n+        current = match.group(1), match.group(2)\n+        marked[current] = []\n+    elif line.startswith(\"  + public \") or line.startswith(\"  + protected \"):\n+        marked[current].append(line[4:].replace(\"JsonTypeInfo<TValue?>\", \"JsonTypeInfo<TValue>\"))\n+\n+ruc_groups = []\n+for line in section(\"RUC PUBLIC/PROTECTED GROUPS\"):\n+    match = re.match(\n+        r\"^(.+?) :: (\\S+) count=(\\d+) unmarked-public-protected-overload=(True|False)$\", line\n+    )\n+    declaring_type, member, count, unmarked = match.groups()\n+    signatures = marked[(declaring_type, member)]\n+    assert len(signatures) == int(count), (declaring_type, member, count, signatures)\n+    if member.startswith(\"get_\"):\n+        member = member[4:] + \" (getter)\"\n+    elif member == \".ctor\":\n+        member = declaring_type.rsplit(\".\", 1)[-1] + \" (constructor)\"\n+    ruc_groups.append({\n+        \"declaring_type\": declaring_type,\n+        \"member\": member,\n+        \"marked_signatures\": signatures,\n+        \"same_named_unmarked_public_or_protected_overload\": unmarked == \"True\",\n+    })\n+\n+doc_member = ET.parse(root / xml_file).find(\n+    \".//member[@name='M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)']\"\n+)\n+assert doc_member is not None\n+summary_xml = ET.tostring(doc_member.find(\"summary\"), encoding=\"unicode\").strip()\n+assert \"The stream is read to completion.\" in summary_xml\n+summary = \"\".join(doc_member.find(\"summary\").itertext())\n+\n+source_lines = [\n+    (\"Deserialize<TValue>(Stream utf8Json, JsonSerializerOptions? options = null)\", 76),\n+    (\"Deserialize(Stream utf8Json, Type returnType, JsonSerializerOptions? options = null)\", 146),\n+    (\"Deserialize<TValue>(Stream utf8Json, JsonTypeInfo<TValue> jsonTypeInfo)\", 234),\n+    (\"Deserialize(Stream utf8Json, JsonTypeInfo jsonTypeInfo)\", 259),\n+    (\"Deserialize(Stream utf8Json, Type returnType, JsonSerializerContext context)\", 335),\n+]\n+read_source_text = (root / \"JsonSerializer.Read.Stream.cs\").read_text().splitlines()\n+for _, lineno in source_lines:\n+    assert \"public static\" in read_source_text[lineno - 1]\n+\n+converter_types = [\n+    {\"type\": \"System.Text.Json.Serialization.JsonConverter\",\n+     \"externally_derivable\": False,\n+     \"must_override\": [],\n+     \"note\": \"The constructor is internal; direct external subclassing is unavailable.\"},\n+    {\"type\": \"System.Text.Json.Serialization.JsonConverter<T>\",\n+     \"externally_derivable\": True,\n+     \"must_override\": [\n+         \"public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)\",\n+         \"public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)\",\n+     ]},\n+    {\"type\": \"System.Text.Json.Serialization.JsonConverterAttribute\",\n+     \"externally_derivable\": True,\n+     \"must_override\": [],\n+     \"note\": \"When using its protected parameterless constructor to supply a converter, override the virtual CreateConverter(Type typeToConvert); it is not abstract.\"},\n+    {\"type\": \"System.Text.Json.Serialization.JsonConverterFactory\",\n+     \"externally_derivable\": True,\n+     \"must_override\": [\n+         \"public override bool CanConvert(Type typeToConvert)\",\n+         \"public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)\",\n+     ]},\n+    {\"type\": \"System.Text.Json.Serialization.JsonNumberEnumConverter<TEnum>\",\n+     \"externally_derivable\": False, \"must_override\": [],\n+     \"note\": \"Sealed.\"},\n+    {\"type\": \"System.Text.Json.Serialization.JsonStringEnumConverter\",\n+     \"externally_derivable\": True, \"must_override\": [],\n+     \"note\": \"Concrete; inherited abstract members are already implemented.\"},\n+    {\"type\": \"System.Text.Json.Serialization.JsonStringEnumConverter<TEnum>\",\n+     \"externally_derivable\": True, \"must_override\": [],\n+     \"note\": \"Concrete; inherited abstract members are already implemented.\"},\n+]\n+\n+answers = [\n+    {\n+        \"question\": 1,\n+        \"answer\": stream_apis,\n+        \"evidence\": [f\"{dll_file}; {probe_command}; probe-output.txt section STREAM PUBLIC/PROTECTED\",\n+                     f\"{xml_file} (Stream member entries)\"],\n+        \"complete\": True,\n+        \"why\": \"Enumerated all declared methods and constructors on exported types, including protected members, and filtered parameters by System.IO.Stream (30 APIs).\",\n+    },\n+    {\n+        \"question\": 2,\n+        \"answer\": document_apis,\n+        \"evidence\": [f\"{dll_file}; {probe_command}; probe-output.txt section JSONDOCUMENT RESULTS PUBLIC/PROTECTED\",\n+                     f\"{document_source}#L394\"],\n+        \"complete\": True,\n+        \"why\": \"Enumerated declared public/protected APIs with a JsonDocument return, Task<JsonDocument>, ValueTask<JsonDocument>, or out JsonDocument; the source confirms TryParseValue's nullable out annotation (13 APIs).\",\n+    },\n+    {\n+        \"question\": 3,\n+        \"answer\": ruc_groups,\n+        \"evidence\": [f\"{dll_file}; {probe_command}; probe-output.txt sections RUC ALL DECLARED METHODS and RUC PUBLIC/PROTECTED GROUPS\"],\n+        \"complete\": True,\n+        \"why\": \"Covers all 20 public/protected member-name groups on exported types using their actual RequiresUnreferencedCode attributes; same-named overload comparisons include only externally accessible members. Private/internal implementation methods are not caller APIs.\",\n+    },\n+    {\n+        \"question\": 4,\n+        \"answer\": {\n+            \"signature\": \"public static Task<JsonDocument> ParseAsync(Stream utf8Json, JsonDocumentOptions options = default, CancellationToken cancellationToken = default)\",\n+            \"documentation_summary\": summary,\n+            \"documentation_summary_xml\": summary_xml,\n+        },\n+        \"evidence\": [f\"{xml_file}: member M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)\",\n+                     f\"{document_source}#L208-L211\"],\n+        \"complete\": True,\n+        \"why\": \"The signature is verbatim from the matching package source, and the summary is verbatim from the lib/net10.0 package XML documentation.\",\n+    },\n+    {\n+        \"question\": 5,\n+        \"answer\": converter_types,\n+        \"evidence\": [f\"{dll_file}; {probe_command}; probe-output.txt section CONVERTER TYPES PUBLIC\",\n+                     f\"{xml_file}: converter type and member documentation\",\n+                     f\"{source_base}/Serialization/JsonConverter.cs#L16-L57\",\n+                     f\"{source_base}/Serialization/JsonConverterFactory.cs#L15-L33\"],\n+        \"complete\": True,\n+        \"why\": \"The seven exported type names containing Converter were enumerated; constructor accessibility, sealed/abstract flags, and implemented versus abstract inherited methods determine external derivation obligations.\",\n+    },\n+    {\n+        \"question\": 6,\n+        \"answer\": [\n+            {\"overload\": name, \"file_url\": f\"{read_source}#L{lineno}\", \"line\": lineno}\n+            for name, lineno in source_lines\n+        ],\n+        \"evidence\": [f\"{nuspec_file}: repository commit {commit}\",\n+                     f\"JsonSerializer.Read.Stream.cs: lines 76, 146, 234, 259, 335\",\n+                     read_source],\n+        \"complete\": True,\n+        \"why\": \"All five synchronous Deserialize Stream overloads in the package match public definitions in the source file at the NuGet repository commit.\",\n+    },\n+]\n+assert len(stream_apis) == 30\n+assert len(document_apis) == 13\n+assert len(ruc_groups) == 20\n+assert len(converter_types) == 7\n+(root / \"answer.json\").write_text(\n+    json.dumps({\"answers\": answers,\n+                \"friction\": \"No .NET inspection tool was available; used a small .NET SDK reflection program against the downloaded net10.0 package plus its XML documentation and commit-pinned source. The SDK template rejected --quiet once; rerunning without it worked.\"},\n+               indent=2) + \"\\n\"\n+)\n*** End Patch\n"
```

</details>

```
Added 1 file(s): /private/tmp/cli-trial-20261106/baseline-2/make_answer.py
```


---

<sub>1m 56s</sub>

### `bash`

**Generate and validate complete cited JSON answers**

`$ python3 make_answer.py && jq -e '.answers | length == 6 and all(.[]; .complete == true)' answer.json && jq -r '.answers[] | "Q\(.question) items=\(if (.answer|type)=="array" then (.answer|length) else "object" end)"' answer.json && ls -lh answer.json && jq -r '.answers[2].answer[] | "\(.declaring_type)::\(.member) (\(.marked_signatures|length)) same-named unmarked: \(.same_named_unmarked_public_or_protected_overload)"' answer.json`

<details>
<summary>29 lines</summary>

```
true
Q1 items=30
Q2 items=13
Q3 items=20
Q4 items=object
Q5 items=7
Q6 items=5
-rw-r--r--@ 1 rich  wheel    34K Nov  6 15:01 answer.json
System.Text.Json.JsonSerializer::Deserialize (16) same-named unmarked: true
System.Text.Json.JsonSerializer::DeserializeAsync (4) same-named unmarked: true
System.Text.Json.JsonSerializer::DeserializeAsyncEnumerable (4) same-named unmarked: true
System.Text.Json.JsonSerializer::Serialize (6) same-named unmarked: true
System.Text.Json.JsonSerializer::SerializeAsync (4) same-named unmarked: true
System.Text.Json.JsonSerializer::SerializeToDocument (2) same-named unmarked: true
System.Text.Json.JsonSerializer::SerializeToElement (2) same-named unmarked: true
System.Text.Json.JsonSerializer::SerializeToNode (2) same-named unmarked: true
System.Text.Json.JsonSerializer::SerializeToUtf8Bytes (2) same-named unmarked: true
System.Text.Json.JsonSerializerOptions::Default (getter) (1) same-named unmarked: false
System.Text.Json.JsonSerializerOptions::Strict (getter) (1) same-named unmarked: false
System.Text.Json.JsonSerializerOptions::Web (getter) (1) same-named unmarked: false
System.Text.Json.JsonSerializerOptions::GetConverter (1) same-named unmarked: false
System.Text.Json.JsonSerializerOptions::MakeReadOnly (1) same-named unmarked: true
System.Text.Json.Nodes.JsonArray::Add (1) same-named unmarked: true
System.Text.Json.Nodes.JsonNode::ReplaceWith (1) same-named unmarked: false
System.Text.Json.Nodes.JsonValue::Create (1) same-named unmarked: true
System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver::DefaultJsonTypeInfoResolver (constructor) (1) same-named unmarked: false
System.Text.Json.Serialization.Metadata.JsonTypeInfo::CreateJsonPropertyInfo (1) same-named unmarked: false
System.Text.Json.Serialization.Metadata.JsonTypeInfo::CreateJsonTypeInfo (2) same-named unmarked: false
<shellId: 19 completed with exit code 0>
```

</details>


---

<sub>2m 3s</sub>

### Copilot

Wrote the six cited answers to `/tmp/cli-trial-20261106/baseline-2/answer.json`. The file marks each answer complete and explains the evidence and scope used.

---

<sub>Generated by [GitHub Copilot CLI](https://github.com/features/copilot/cli)</sub>
