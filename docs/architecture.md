# Architecture

This document describes the design and implementation of dotnet-inspect.

## Design Philosophy

dotnet-inspect is designed for **LLM-driven .NET development**. The tool prioritizes:

1. **Structured output** - Markdown tables and JSON that LLMs can parse reliably
2. **Progressive disclosure** - Verbosity controls let you request exactly the detail level needed
3. **Minimal tokens** - `--table`, `--tsv`, and `--compact` options reduce output size
4. **Self-documenting** - `skill` command prints the SKILL.md; `--help` and `-v` show CLI structure

## Command Structure

The tool is organized around source inspection, API lookup, relationship, and utility commands:

```text
┌─────────────────────────────────────────────────────────────┐
│                        package                               │
│  NuGet package metadata, dependencies, file structure        │
├─────────────────────────────────────────────────────────────┤
│                        library                              │
│  PE headers, SourceLink, determinism audit                   │
├─────────────────────────────────────────────────────────────┤
│                         type                                 │
│  Type shape, public signatures, and summaries                │
├─────────────────────────────────────────────────────────────┤
│                        member                               │
│  Member docs, overload selection, Source/IL drill-in         │
├─────────────────────────────────────────────────────────────┤
│                         find                                 │
│  Search for types across packages, platform, and assets      │
├─────────────────────────────────────────────────────────────┤
│                         diff                                 │
│  Compare API surfaces between package/platform versions      │
├─────────────────────────────────────────────────────────────┤
│            depends / extensions / implements                 │
│  Relationship discovery for APIs, packages, and libraries    │
├─────────────────────────────────────────────────────────────┤
│                        source                               │
│  SourceLink URLs, source text, and token+IL offset mapping   │
├─────────────────────────────────────────────────────────────┤
│                      cache / skill                           │
│  Cache inspection and embedded agent guidance                │
└─────────────────────────────────────────────────────────────┘
```

### package

Inspects NuGet package metadata without extracting libraries:

- Package ID, version, authors, license
- Target frameworks and dependencies per TFM
- File listing (DLLs or all files with `--all`)
- Version history from nuget.org

### library

Inspects .NET library files (PE/COFF format):

- Assembly identity (name, version, public key token)
- PE characteristics (architecture, compilation type)
- SourceLink and determinism audit
- Unsafe code detection

### type and member

Extract public API surface using metadata:

- `type` renders type shape, summaries, members, and `--shape` declarations
- `member` renders member tables, docs, overload selectors, decompiled/lowered C#, SourceLink-backed original source, and IL
- Both support package/platform/library sources and section/field projection

### diff

Compares API surfaces between two package versions:

- Added, removed, and modified types
- Member-level changes within types
- Version range syntax: `Package@v1..v2`

### find

Searches for types across packages, platform libraries, projects, and local assets.

### relationships

`depends`, `extensions`, and `implements` expose dependency graphs, extension methods/properties, implementors, and subclasses.

### source

Resolves SourceLink URLs, source text, and MethodDef token + IL offset pairs to source locations.

## LLM Integration

### SKILL.md

The tool includes an embedded SKILL.md that is distributed via the [dotnet/skills](https://github.com/dotnet/skills) marketplace. Skills are loaded automatically into the LLM's context when activated, providing decision trees, command patterns, and usage examples without requiring the LLM to run a command first.

Run `dotnet-inspect skill` to print the embedded SKILL.md.

### Output Designed for LLMs

- **Markdown tables** - Structured, parseable format
- **Consistent formatting** - Same structure across invocations
- **Full signatures** - Parameter names included, not just types
- **Minimal noise** - Hidden/obsolete members excluded by default

## Library Inspection

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

### Async Method Detection

The **Async Methods** section lists public async methods and classifies each as one of two kinds:

- **Runtime** — runtime async ("async v2"), introduced in .NET 11. The compiler emits the
  method with the `MethodImplAttributes.Async` flag (`0x2000`) and no state machine; the
  runtime drives the continuation. Enabled by compiling with `<Features>runtime-async=on</Features>`
  on `net11.0`. Adoption is selective: in .NET 11 Preview 4, `System.Private.CoreLib` uses
  runtime async (mixed with some state-machine methods), while many framework assemblies
  (e.g. `System.Text.Json`) still compile their async methods as state machines.
- **State machine** — classic compiler-generated async ("async v1"). The compiler rewrites
  the method into a state machine and marks it with `AsyncStateMachineAttribute` (or
  `AsyncIteratorStateMachineAttribute` for `async` iterators).

The two are mutually exclusive. Detection reads metadata directly — no IL scan required:

```csharp
// Runtime async: method implementation flag 0x2000
bool isRuntimeAsync = (method.ImplAttributes & (MethodImplAttributes)0x2000) != 0;

// State-machine async: AsyncStateMachineAttribute / AsyncIteratorStateMachineAttribute
```

Like the Unsafe and P/Invoke sections, detection is **public-surface only** (skips
accessors and compiler-generated `<...>` types), so it surfaces the async API a caller sees.

The **Signals** section carries a roll-up **Async Kind** row summarizing the whole assembly's
public async surface: `Runtime`, `State machine`, `Mixed` (both kinds present), or `None`
(no public async methods). It reuses the same cheap, always-computed presence flags
(`HasRuntimeAsync`/`HasStateMachineAsync`) gathered in the single metadata pass, so it adds no
extra scanning cost.

> Note: runtime async is a *compiler* opt-in. A method compiled with `runtime-async=on`
> emits the `0x2000` flag regardless of body shape (loops, `try`/`catch`/`finally`,
> `await using`, `await foreach`, `ConfigureAwait(false)` all classify as Runtime).

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

### Serialization Architecture

The tool supports two output formats — Markdown (via [Markout](https://github.com/richlander/markout)) and JSON — with a clean separation between data and presentation.

**Data models** (`Models/`) are pure data types with no serialization attributes. They represent the raw inspection results:

```csharp
// Models/InspectionResult.cs — pure data, no Markout
public class InspectionResult
{
    public string PackageName { get; set; }
    public long? TotalDownloads { get; set; }
    public List<string>? TargetFrameworks { get; set; }
    // ...
}
```

**View models** (`Views/`) wrap the data models and add all Markout presentation concerns — display attributes, sections, field builders, computed display properties, and formatting:

```csharp
// Views/InspectionResultView.cs — Markout presentation
[MarkoutSerializable(TitleProperty = nameof(PackageName), AutoFields = false)]
public class InspectionResultView
{
    private readonly InspectionResult _data;

    [MarkoutValueFormatter(typeof(CompactNumberFormatter))]
    [MarkoutPropertyName("Downloads")]
    public long? TotalDownloads => _data.TotalDownloads;

    [MarkoutSection(Name = "Package")]
    public List<MarkoutField> Metadata => GetMetadataFields();
    // ...
}
```

**`OutputFormatter`** acts as the pivot point between the two paths:

- **JSON** → serializes the data model directly via `JsonContext` (STJ source-gen with `SnakeCaseLower` naming policy)
- **Markout** → wraps data in a view model, then serializes via `MarkoutContext`

```csharp
// JSON: data model goes straight to STJ
JsonSerializer.Serialize(result, JsonContext.Default.InspectionResult);

// Markout: data model wrapped in view model first
var view = new InspectionResultView(result);
context.Serialize(view);
```

This ensures data models never reference Markout, and presentation logic is fully contained in `Views/` and `Output/`.

### Value Formatting

Numeric and date formatting is handled declaratively through Markout attributes on view model properties:

| Attribute | Purpose | Example |
| --------- | ------- | ------- |
| `[MarkoutJoin(", ")]` | Joins list properties | `["net8.0", "net9.0"]` → `"net8.0, net9.0"` |
| `[MarkoutFormat("yyyy-MM-dd")]` | Format string via `ISpanFormattable` | `DateTimeOffset` → `"2024-06-15"` |
| `[MarkoutValueFormatter(typeof(...))]` | Custom `IMarkoutValueFormatter<T>` | `5100000000` → `"5.1B"` |
| `[MarkoutBoolFormat("✓", "✗")]` | Boolean display strings | `true` → `"✓"` |

Formatter implementations live in `Output/ValueFormatters.cs` (`ByteSizeFormatter`, `CompactNumberFormatter`).

### Output Modes

| Mode | Flag | Description |
| ---- | ---- | ----------- |
| Markdown | (default) | Tables with headers, powered by Markout |
| Table | `--table` | One result per line, pretty-printed columns |
| TSV | `--tsv` | Normalized tab-separated rows for agents and shell tools |
| JSON | `--json` | Full structured output |
| Compact JSON | `--json --compact` | Minified, omits false/null values |

### Verbosity Control

Output follows a **height × width** model:

- **Width** (verbosity): `-v:q` through `-v:d` controls column density
- **Height** (sections): `-s:1,2` or `-x:3` includes/excludes H2 sections

This allows precise control over output size for LLM context management.

## Caching

Packages are resolved in order:

1. **NuGet global cache** (`~/.nuget/packages`) — read-only (never written to)
2. **App cache** (`~/.local/share/dotnet-inspect/packages`) — downloaded packages cached here

`PackageCacheService` enforces this invariant: the app reads from both caches but only writes to the app cache. This prevents corrupting the shared NuGet cache.

## Project Structure

The codebase is organized into four layers, from bottom (domain-agnostic) to top (application-specific):

```text
┌─────────────────────────────────────────────────────────────┐
│  dotnet-inspect (App layer)                                 │
│                                                             │
│  Commands/        CLI commands, orchestration               │
│  Models/          Pure data types (no serialization attrs)  │
│  Views/           Markout view models (presentation only)   │
│  Output/          Formatters, serialization pivot            │
│  Inspectors/      App-specific inspection logic             │
│  Options/         CLI option types                          │
├─────────────────────────────────────────────────────────────┤
│  DotnetInspector.Services (Shared services)                 │
│                                                             │
│  PackageMetadataService    NuGet metadata (downloads, etc)  │
│  PackageCacheService       cache management                 │
│  NuspecParser              nuspec → NuspecData DTO           │
│  DepsJsonParser            deps.json → DepsJsonData DTO     │
│  TfmSelector               TFM selection, assembly paths    │
│  + 7 more shared services                                   │
├─────────────────────────────────────────────────────────────┤
│  DotnetInspector.Packages (Domain provider — NuGet)         │
│                                                             │
│  PackageExtractor, NuGetCache, TfmResolver                  │
│  DependencyGroup, PackageDependency                         │
├─────────────────────────────────────────────────────────────┤
│  DotnetInspector.Metadata (Domain provider — PE/Assembly)   │
│                                                             │
│  AssemblyReader, ApiSurface models, PdbReader                │
└─────────────────────────────────────────────────────────────┘
```

### Layer Rules

- **Domain providers** are application-agnostic. They know about NuGet packages and PE files, not about dotnet-inspect.
- **Services** return DTOs (`NuspecData`, `DepsJsonData`, `PackageMetadata`), never mutate app types. They use `Action<string>?` for logging instead of app-specific logger types.
- **Models** are pure data with no Markout references. JSON conditional attributes (`[JsonIgnore(Condition = ...)]`) are acceptable since they control data serialization, not presentation.
- **Views** wrap models and own all Markout attributes, sections, field builders, and computed display properties. They are the only types registered in `MarkoutContext`.
- **Commands** orchestrate: they call services, populate models, and hand off to `OutputFormatter`. Most commands should not import Markout directly.

### Key Files

```text
src/dotnet-inspect/
├── Commands/                   # CLI commands (orchestration)
├── Inspectors/                 # App-specific inspection logic
├── Models/                     # Pure data types
│   ├── InspectionResult.cs     #   Package inspection data
│   ├── AssemblyAudit.cs        #   Assembly audit data
│   └── RidPackageReference.cs  #   RID package data
├── Views/                      # Markout presentation
│   ├── InspectionResultView.cs #   Package view model
│   ├── AssemblyAuditView.cs    #   Assembly audit view model
│   ├── AssemblyAuditReport.cs  #   Multi-assembly report wrapper
│   └── FlatDependency.cs       #   Dependency table row (view-only type)
├── Output/                     # Formatters and utilities
│   ├── OutputFormatter.cs      #   JSON/Markout pivot point
│   ├── ValueFormatters.cs      #   ByteSizeFormatter, CompactNumberFormatter
│   ├── FindOutputFormatter.cs  #   Find command rendering
│   ├── DiffOutputFormatter.cs  #   Diff command rendering
│   └── MemberTableFormatter.cs #   API member table rendering
├── Options/                    # CLI option types
├── JsonContext.cs              # STJ source-gen (data models)
└── MarkoutContext.cs           # Markout source-gen (view models)

src/DotnetInspector.Services/   # Shared, app-agnostic services
src/DotnetInspector.Packages/   # NuGet domain provider
src/DotnetInspector.Metadata/   # PE/assembly domain provider
```

## Key Design Decisions

1. **No assembly loading** — Uses `MetadataReader` to avoid loading assemblies into the runtime, enabling inspection of any .NET library regardless of target framework.

2. **Data/View model split** — Data models (`Models/`) have zero Markout references. View models (`Views/`) own all presentation. This prevents serialization concerns from bleeding into domain types.

3. **Services return DTOs** — Services never mutate app types. They return focused DTOs that commands compose into models. This keeps services reusable across applications.

4. **Read-only NuGet cache** — The app reads from the shared NuGet global cache but never writes to it. Downloads go to the app's own cache directory.

5. **Embedded SKILL.md** — The skill definition is compiled into the binary as a resource, ensuring it's always available and version-matched. Distributed via the dotnet/skills marketplace.

6. **Default exclusions** — `[EditorBrowsable(Never)]` and `[Obsolete]` members are hidden by default to reduce noise. Use `--all` to include them.

7. **Automatic TFM selection** — When multiple target frameworks exist, the highest is auto-selected. Override with `--tfm`.

8. **Signature-first output** — Full method signatures with parameter names are the primary output, not just type names, because LLMs need complete information to generate correct code.
