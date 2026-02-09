# Architecture

This document describes the design and implementation of dotnet-inspect.

## Design Philosophy

dotnet-inspect is designed for **LLM-driven .NET development**. The tool prioritizes:

1. **Structured output** - Markdown tables and JSON that LLMs can parse reliably
2. **Progressive disclosure** - Verbosity controls let you request exactly the detail level needed
3. **Minimal tokens** - `--signatures-only` and `--compact` options reduce output size
4. **Self-documenting** - `llmstxt` command provides comprehensive usage examples

## Command Structure

The tool is organized around seven commands plus a meta command, each targeting a different level of abstraction:

```text
┌─────────────────────────────────────────────────────────────┐
│                        package                               │
│  NuGet package metadata, dependencies, file structure        │
├─────────────────────────────────────────────────────────────┤
│                        assembly                              │
│  PE headers, SourceLink, determinism audit                   │
├─────────────────────────────────────────────────────────────┤
│                          api                                 │
│  Public API surface: types, methods, signatures              │
├─────────────────────────────────────────────────────────────┤
│                         type                                 │
│  Single type: hierarchy, interfaces, members (shape view)    │
├─────────────────────────────────────────────────────────────┤
│                         diff                                 │
│  Compare API surfaces between two package versions           │
├─────────────────────────────────────────────────────────────┤
│                        samples                               │
│  Extract code sample references from XML doc comments        │
├─────────────────────────────────────────────────────────────┤
│                       platform                               │
│  List platform/framework assemblies from installed SDK       │
└─────────────────────────────────────────────────────────────┘
```

### package

Inspects NuGet package metadata without extracting assemblies:

- Package ID, version, authors, license
- Target frameworks and dependencies per TFM
- File listing (DLLs or all files with `--all`)
- Version history from nuget.org

### assembly

Inspects .NET assembly files (PE/COFF format):

- Assembly identity (name, version, public key token)
- PE characteristics (architecture, compilation type)
- SourceLink and determinism audit
- Unsafe code detection

### api

Extracts public API surface using reflection metadata:

- All public types or filtered by glob pattern
- Full method signatures with parameter names
- Filtering by member name, unsafe signatures, hidden/obsolete
- Source URLs via SourceLink

### type

Displays a single type's shape in shape format:

- Inheritance chain (base classes)
- Implemented interfaces
- Members grouped by kind

### diff

Compares API surfaces between two package versions:

- Added, removed, and modified types
- Member-level changes within types
- Version range syntax: `Package@v1..v2`

### samples

Extracts code sample references from XML doc comments:

- Sandcastle-style `<code source=...>` references
- `<seealso href=...>` file references
- Region extraction from source files

### platform

Lists platform/framework assemblies from the installed .NET SDK:

- Available frameworks and versions
- Assembly listing per framework
- Useful for inspecting BCL types

## LLM Integration

### llms.txt

The `llmstxt` command outputs a comprehensive usage guide embedded in the binary. This file is designed to be included in LLM context to enable effective tool usage.

```csharp
// Embedded as a resource and streamed to stdout
var stream = assembly.GetManifestResourceStream("dotnet-inspect.llms.txt");
```

The llms.txt content includes:

- Command examples for common workflows
- Output format options
- Filtering and verbosity controls
- Tips for version comparison and member lookup

### Output Designed for LLMs

- **Markdown tables** - Structured, parseable format
- **Consistent formatting** - Same structure across invocations
- **Full signatures** - Parameter names included, not just types
- **Minimal noise** - Hidden/obsolete members excluded by default

## Assembly Inspection

The tool uses `System.Reflection.Metadata` and `System.Reflection.PortableExecutable` for low-level assembly inspection without loading assemblies into the runtime.

### Metadata Extraction

```text
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│    PEReader     │────▶│ MetadataReader  │────▶│  Type/Method    │
│  (PE headers)   │     │ (ECMA-335 meta) │     │  Definitions    │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

Key inspectors:

| Inspector | Purpose |
| --------- | ------- |
| `ApiSurfaceExtractor` | Extracts public types, methods, properties, fields, events |
| `AssemblyAuditor` | Reads PE headers, attributes, SourceLink, determinism |
| `SourceLinkResolver` | Maps source files to URLs via embedded SourceLink JSON |

### Signature Decoding

Method and property signatures are decoded using `SignatureTypeProvider`, which implements `ISignatureTypeProvider<string, object?>`:

```csharp
public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
{
    PrimitiveTypeCode.Int32 => "int",
    PrimitiveTypeCode.String => "string",
    // ...
};

public string GetPointerType(string elementType) => $"{elementType}*";
public string GetByReferenceType(string elementType) => $"ref {elementType}";
```

Parameter names are extracted from the `Parameter` table in metadata, matching by sequence number.

### Unsafe Code Detection

Unsafe code detection operates at two levels:

#### Assembly-Level Detection

Checks for `System.Security.UnverifiableCodeAttribute` on the assembly. This attribute is emitted by the C# compiler when `AllowUnsafeBlocks` is enabled, indicating the assembly contains unverifiable code.

```csharp
private static bool CheckForUnsafeCode(MetadataReader reader)
{
    foreach (var attrHandle in reader.CustomAttributes)
    {
        var attr = reader.GetCustomAttribute(attrHandle);
        string? attrName = GetAttributeName(reader, attr);
        if (attrName == "System.Security.UnverifiableCodeAttribute")
            return true;
    }
    return false;
}
```

#### Method-Level Detection (`--unsafe` filter)

The `--unsafe` filter identifies methods with **pointer types in their signature**:

```csharp
private static bool HasUnsafeSignature(string? signature)
{
    return signature?.Contains('*') ?? false;
}
```

This detects:

- Pointer parameters: `void Process(byte* buffer)`
- Pointer return types: `int* GetPointer()`
- Function pointers: `delegate*<int, void>`

This approach is intentionally **API-focused** - it surfaces methods that require the caller to use an unsafe context. Methods that use unsafe internally but expose a safe API are not included.

#### What's Not Detected

Methods marked with the `unsafe` keyword but without pointers in their signature are not detected. For example:

```csharp
public unsafe int StackAlloc()
{
    Span<int> span = stackalloc int[10];  // unsafe internally
    return span[0];
}
```

This method is `unsafe` but has a safe signature (`int StackAlloc()`), so `--unsafe` won't include it.

#### Fully Accurate Implementation

A complete implementation would require IL analysis to detect all unsafe constructs:

1. **Scan method bodies for unsafe IL opcodes:**
   - `localloc` (0xFE 0x0F) - used by `stackalloc`
   - `ldind.*` / `stind.*` - pointer indirection
   - `cpblk` / `initblk` - block memory operations
   - `sizeof` on unmanaged types

2. **Check for pointer local variables** in the method's `LocalVariableSignature`

3. **Detect `fixed` statements** by looking for pinned local variables (the `ELEMENT_TYPE_PINNED` modifier in signatures)

Example IL scan:

```csharp
var body = pe.GetMethodBody(method.RelativeVirtualAddress);
var ilBytes = body.GetILBytes();

// Check for localloc opcode
for (int i = 0; i < ilBytes.Length - 1; i++)
{
    if (ilBytes[i] == 0xFE && ilBytes[i + 1] == 0x0F)
        return true;  // Has stackalloc
}
```

This approach is significantly more expensive (requires reading IL for every method) and would slow down API extraction. The current signature-based approach is a pragmatic choice that covers the most common use case: finding methods that expose pointers in their public API.

### SourceLink Resolution

SourceLink information is embedded in PDBs (portable or embedded) as custom debug information with GUID `CC110556-A091-4D38-9FEC-25AB9A351A6A`.

The JSON contains document mappings:

```json
{
  "documents": {
    "/_/*": "https://raw.githubusercontent.com/dotnet/runtime/abc123/*"
  }
}
```

The resolver:

1. Extracts SourceLink JSON from PDB
2. Parses document mappings
3. For each type/method, finds the source document via `MethodDebugInformation`
4. Applies URL pattern to generate raw source URL
5. Converts to GitHub browse URL with line number

### Determinism Audit

Determinism is detected via the `DebuggableAttribute` blob. Specifically, checking bit 0x100 in the debugging modes flags:

```csharp
// Bit 8 (0x100) = IgnoreSymbolStoreSequencePoints (deterministic)
isDeterministic = (debuggingModes & 0x100) != 0;
```

## Output Formatting

### Markout Integration

The tool uses [Markout](https://github.com/richlander/markout) for Markdown serialization. Markout provides a structured format for human-readable, machine-parseable output. See the [Markout specification](https://github.com/richlander/markout/blob/main/docs/specification.md) for format details.

Types are annotated with `[MarkoutSerializable]` and registered in `MarkoutContext`:

```csharp
[MarkoutContext(typeof(InspectionResult))]
[MarkoutContext(typeof(AssemblyInfo))]
[MarkoutContext(typeof(ApiSurface))]
// ...
public partial class MarkoutContext : MarkoutSerializerContext
{
}
```

Properties use `[MarkoutPropertyName]` for display names and `[MarkoutIgnore]` to exclude from output.

### Output Modes

| Mode | Flag | Description |
| ---- | ---- | ----------- |
| Markdown | (default) | Tables with headers, powered by Markout |
| Signatures only | `--signatures-only` | Plain method signatures, one per line |
| JSON | `--json` | Full structured output |
| Compact JSON | `--json --compact` | Minified, omits false/null values |

### Verbosity Control

Output follows a **height × width** model:

- **Width** (verbosity): `-v:q` through `-v:d` controls column density
- **Height** (sections): `-s:1,2` or `-x:3` includes/excludes H2 sections

This allows precise control over output size for LLM context management.

## Caching

Packages are resolved in order:

1. **NuGet global cache** (`~/.nuget/packages`) - read-only
2. **App cache** (`~/.local/share/dotnet-inspect/packages`) - downloaded packages cached here

The `NuGetCache` class handles resolution, download from nuget.org, and extraction.

## Project Structure

```text
src/dotnet-inspect/
├── Commands/           # Command implementations
│   ├── ApiCommand.cs
│   ├── AssemblyCommand.cs
│   ├── DiffCommand.cs
│   ├── LlmsTxtCommand.cs
│   ├── PackageCommand.cs
│   ├── PlatformCommand.cs
│   ├── SamplesCommand.cs
│   └── TypeCommand.cs
├── Inspectors/         # Core inspection logic
│   ├── ApiSurfaceExtractor.cs
│   ├── AssemblyAuditor.cs
│   ├── DocCommentParser.cs
│   ├── SourceLinkResolver.cs
│   └── ...
├── Output/             # Formatting
│   ├── OutputFormatter.cs
│   └── VerboseLogger.cs
├── Options/            # Command options records
├── CommandLineBuilder.cs
├── SignatureTypeProvider.cs
├── MarkoutContext.cs
├── JsonContext.cs
└── llms.txt            # Embedded LLM usage guide
```

## Key Design Decisions

1. **No assembly loading** - Uses `MetadataReader` to avoid loading assemblies into the runtime, enabling inspection of any .NET assembly regardless of target framework.

2. **Embedded llms.txt** - The usage guide is compiled into the binary as a resource, ensuring it's always available and version-matched.

3. **Default exclusions** - `[EditorBrowsable(Never)]` and `[Obsolete]` members are hidden by default to reduce noise. Use `--all` to include them.

4. **Automatic TFM selection** - When multiple target frameworks exist, the highest is auto-selected. Override with `--tfm`.

5. **Signature-first output** - Full method signatures with parameter names are the primary output, not just type names, because LLMs need complete information to generate correct code.
