# Architecture

This document describes the design and implementation of dotnet-inspect.

## Design Philosophy

dotnet-inspect is designed for **LLM-driven .NET development**. The tool prioritizes:

1. **Structured output** - Markdown tables and JSON that LLMs can parse reliably
2. **Progressive disclosure** - Verbosity controls let you request exactly the detail level needed
3. **Minimal tokens** - `--signatures-only` and `--compact` options reduce output size
4. **Self-documenting** - `llmstxt` command provides comprehensive usage examples

## Command Structure

The tool is organized around four primary inspection verbs, each targeting a different level of abstraction:

```
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
│  Single type: hierarchy, interfaces, members (tree view)     │
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

Displays a single type's shape in tree format:
- Inheritance chain (base classes)
- Implemented interfaces
- Members grouped by kind

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

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│    PEReader     │────▶│ MetadataReader  │────▶│  Type/Method    │
│  (PE headers)   │     │ (ECMA-335 meta) │     │  Definitions    │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

Key inspectors:

| Inspector | Purpose |
|-----------|---------|
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

Two levels of unsafe detection:

1. **Assembly-level**: Checks for `System.Security.UnverifiableCodeAttribute` (indicates `AllowUnsafeBlocks` was enabled)

2. **Method-level** (`--unsafe` filter): Checks if decoded signature contains pointer types (`*` character). This identifies methods that **require unsafe context to call** (pointer parameters/return types), not methods that merely use unsafe internally:

```csharp
private static bool HasUnsafeSignature(string? signature)
{
    return signature?.Contains('*') ?? false;
}
```

Note: Methods marked with the `unsafe` keyword but without pointers in their signature (e.g., using `stackalloc` internally) are not detected by `--unsafe`. This is intentional - the filter surfaces methods whose *public API* requires unsafe context.

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
|------|------|-------------|
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

```
src/dotnet-inspect/
├── Commands/           # Command implementations
│   ├── ApiCommand.cs
│   ├── AssemblyCommand.cs
│   ├── PackageCommand.cs
│   ├── TypeCommand.cs
│   └── LlmsTxtCommand.cs
├── Inspectors/         # Core inspection logic
│   ├── ApiSurfaceExtractor.cs
│   ├── AssemblyAuditor.cs
│   ├── SourceLinkResolver.cs
│   └── ...
├── Output/             # Formatting
│   ├── MarkoutViewFormatter.cs
│   └── OutputFormatter.cs
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
