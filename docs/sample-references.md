# Sample References in XML Doc Comments

The `samples` command extracts code sample references from XML documentation comments (`///`).
This document describes the supported formats and known repositories with examples.

## Supported Formats

### 1. Sandcastle-style `<code source=...>` (External File References)

References external source files with optional region and title attributes:

```xml
/// <example>
///   <code lang="cs" source="../../samples/Demo.cs" region="BasicUsage" title="Basic usage example" />
/// </example>
```

**Attributes:**

- `source` (required): Relative path to the source file
- `region` (optional): Named region within the file (e.g., `#region BasicUsage`)
- `title` (optional): Human-readable description
- `lang` (optional): Language identifier (`cs`, `fs`, `vb`)

### 2. `<seealso href=...>` References

References external source files via seealso links:

```xml
/// <seealso href="../../samples/Usage.cs">Usage examples</seealso>
/// <seealso href="../samples/Demo.cs" region="QuickStart">Quick start guide</seealso>
```

**Attributes:**

- `href` (required): Relative path to a code file (`.cs`, `.fs`, `.vb`, `.fsx`, `.csx`)
- `region` (optional): Named region within the file
- Inner text provides the description

**Note:** HTTP/HTTPS URLs are ignored; only relative file paths are processed.

### 3. Inline `<code>` Examples

Code embedded directly in documentation (supported for display but not as external references):

```xml
/// <example>
/// <code language="csharp">
/// var result = MyMethod();
/// Console.WriteLine(result);
/// </code>
/// </example>
```

## Supported File Extensions

- `.cs` - C#
- `.fs` - F#
- `.vb` - Visual Basic
- `.fsx` - F# Script
- `.csx` - C# Script

## Repository Examples

### Repositories with External Sample References

#### Newtonsoft.Json ✅

The Newtonsoft.Json repository extensively uses Sandcastle-style `<code source=...>` references
in its MAML documentation files (`Doc/*.aml`). Sample files are located in
`Src/Newtonsoft.Json.Tests/Documentation/`.

**Example locations:**

- `Doc/SerializationCallbacks.aml` - References `SerializationTests.cs` with regions like `SerializationCallbacksObject`
- `Doc/PreserveObjectReferences.aml` - References serialization test samples
- `Doc/SerializationAttributes.aml` - References `Samples/Serializer/*.cs`
- `Doc/CustomContractResolver.aml` - References `CustomContractResolver.cs`
- `Doc/ToObjectComplex.aml` - References `Samples/Linq/ToObjectComplex.cs`

**Sample patterns:**

```xml
<code lang="cs" source="..\Src\Newtonsoft.Json.Tests\Documentation\SerializationTests.cs" 
      region="SerializationCallbacksObject" title="Serialization Callback Attributes" />
```

#### Markout ✅

The Markout repository uses both Sandcastle-style and seealso references in source files.

**Files with samples:**

- `src/MarkOut/MarkoutWriter.cs` - References `samples/Serialization/WriterUsage.cs`
- `src/MarkOut/MarkoutSerializer.cs` - References `samples/Serialization/BasicUsage.cs`
- `src/MarkOut/TreeNode.cs` - References `samples/Serialization/WriterUsage.cs`
- `src/MarkOut/MarkoutSerializerContext.cs` - References `BasicUsage.cs` and `SectionFiltering.cs`
- `src/MarkOut/MarkoutContextAttribute.cs` - References `samples/Serialization/BasicUsage.cs`

**Sample patterns (Sandcastle-style):**

```xml
/// <example>
///   <code lang="cs" source="../../samples/Serialization/WriterUsage.cs" region="UseMarkoutWriter" title="Basic writer usage" />
/// </example>
```

**Sample patterns (seealso):**

```xml
/// <seealso href="../../samples/Serialization/WriterUsage.cs">Direct writer usage examples</seealso>
```

### Repositories with Inline Examples Only

#### dotnet/runtime ⚠️

Uses inline `<code>` blocks within `<example>` tags. Does not use external `source=` references.
Also uses `<include file=...>` for centralized XML documentation.

**Example locations with inline code:**

- `src/libraries/Microsoft.Extensions.Logging.Abstractions/src/LoggerExtensions.cs`
- `src/libraries/Microsoft.Extensions.Logging.EventSource/src/LoggingEventSource.cs`
- `src/libraries/System.Linq/src/System/Linq/Join.cs`
- `src/libraries/System.Runtime.InteropServices.JavaScript/src/System/Runtime/InteropServices/JavaScript/JSImportAttribute.cs`
- `src/libraries/Microsoft.Extensions.Configuration.CommandLine/src/CommandLineConfigurationExtensions.cs`
- `src/libraries/Microsoft.Extensions.Http/src/ITypedHttpClientFactory.cs`
- `src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/SuppressGCTransitionAttribute.cs`

**Inline pattern:**

```xml
/// <example>
/// <code language="csharp">
/// logger.LogDebug(0, exception, "Error while processing request from {Address}", address)
/// </code>
/// </example>
```

#### dotnet/aspnetcore ❌

Uses inline documentation only. No external sample references found.
`<seealso href=...>` is used only for external URLs (RFC specifications, etc.).

#### dotnet/roslyn ❌

Uses inline `<example>` tags with embedded code. No external sample references.
`<seealso href=...>` used only for GitHub issues and Microsoft Learn documentation.

#### dotnet/aspire ❌

Uses inline `<code>` blocks per their documentation standards.
No external sample references.

#### dotnet/extensions ❌

Standard XML doc comments with `<seealso cref=...>` (type references, not file paths).
No external sample references found.

#### dotnet/efcore ❌

Uses inline `<code>` blocks with embedded code snippets.
External documentation via `<see href="https://aka.ms/efcore-docs-*">` URLs.
No file-based sample references.

#### dotnet/roslyn-api-docs ❌

Read-only API documentation mirror. Contains compiled XML documentation from Roslyn source.
Original doc comments live in dotnet/roslyn and dotnet/roslyn-sdk repositories.

### Repositories with Different Sample Formats

#### dotnet/dotnet-api-docs 📝

Uses **Docfx Markdown syntax** (`:::code`) rather than Sandcastle XML attributes.
This format is not currently supported by the `samples` command.

**Format:**

```markdown
:::code language="csharp" source="~/snippets/csharp/System.Net.Http/HttpClient/source.cs" id="Snippet1":::
```

**Attributes:**

- `language` - Language identifier (`csharp`, `vb`, `fsharp`, `cpp`, `xaml`)
- `source` - Path starting with `~/snippets/...`
- `id` - Snippet identifier (e.g., `Snippet1`, `Snippet13`)
- `?highlight=` - Optional line highlighting (e.g., `?highlight=4,9,37`)

**Example locations:**

- `xml/System.Net.Http/HttpClient.xml` - C# and F# samples
- `xml/System.Text/StringBuilder.xml` - Overview samples
- `xml/System.Data/DataTable.xml` - ADO.NET samples
- `xml/System.IO/FileSystemWatcher.xml` - File system samples
- `xml/System.Xml/XmlWriter.xml` - XML processing samples

**Multi-language examples are common** (C#, VB.NET, F#, C++).

## Testing the `samples` Command

### Newtonsoft.Json

```bash
# Note: Newtonsoft.Json samples are in MAML files, not source code doc comments
# The tool reads from compiled library PDB/source, so MAML samples may not be available
```

### Markout

```bash
# List samples for the Markout package
dotnet-inspect samples --package Markout --list

# Get samples for a specific type
dotnet-inspect samples TreeNode --package Markout

# Fetch and display sample content
dotnet-inspect samples MarkoutWriter --package Markout
```

## Notes

1. **Path normalization**: Backslashes are converted to forward slashes automatically
2. **Region extraction**: The tool extracts content between `#region Name` and `#endregion` markers
3. **URL resolution**: Relative paths are resolved to raw GitHub URLs using SourceLink information
4. **HTTP URLs ignored**: `<seealso href="https://...">` links are not treated as sample references
