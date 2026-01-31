# dotnet-inspect

A CLI tool for inspecting .NET assemblies and NuGet packages. Useful for understanding package contents, comparing API surfaces between versions, and auditing assemblies for SourceLink/determinism.

## Installation

```bash
# Install as a global tool
dotnet tool install -g dotnet-inspect

# Or run without installing
dnx dotnet-inspect
```

## Quick Start

```bash
# Inspect a NuGet package
dotnet-inspect package System.Text.Json

# View public API of a type
dotnet-inspect api JsonSerializer --package System.Text.Json

# Compare APIs between versions
diff <(dotnet-inspect api JsonSerializer --package System.Text.Json@9.0.0) \
     <(dotnet-inspect api JsonSerializer --package System.Text.Json@10.0.2)

# Audit assembly for SourceLink/determinism
dotnet-inspect assembly --package System.Text.Json --tfm net8.0 --audit

# Inspect a tool package (dotnet-inspect inspecting itself)
dotnet-inspect package dotnet-inspect --files --all
```

## Commands

### package

Inspect NuGet packages - view metadata, dependencies, vulnerabilities, and file structure.

```bash
dotnet-inspect package System.Text.Json              # Package metadata (minimal verbosity)
dotnet-inspect package System.Text.Json -v normal    # Include Metadata table + sections
dotnet-inspect package System.Text.Json 8.0.4        # Specific version (shows vulnerability)
dotnet-inspect package System.CommandLine --files    # List DLLs
dotnet-inspect package System.CommandLine --versions # List available versions
dotnet-inspect package dotnet-inspect --files --all  # Inspect tool packages
```

### assembly

Inspect .NET assemblies - view assembly info and audit for SourceLink/determinism.

```bash
dotnet-inspect assembly MyLib.dll --audit
dotnet-inspect assembly --package System.Text.Json --tfm net8.0 --audit
dotnet-inspect assembly --package dotnet-inspect     # dotnet-inspect inspecting itself
```

### api

View public API surface of assemblies or specific types.

```bash
dotnet-inspect api --package System.CommandLine                 # List all types
dotnet-inspect api --package System.CommandLine --filter "Command*"  # Filter by glob pattern
dotnet-inspect api JsonSerializer --package System.Text.Json    # Specific type
dotnet-inspect api 'Option<T>' --package System.CommandLine     # Generic types (C#-style syntax)
dotnet-inspect api JsonSerializer --package System.Text.Json -m Deserialize  # Filter to member
dotnet-inspect api Command --package System.CommandLine --ctor  # Show constructors with details
dotnet-inspect api JsonSerializer --package System.Text.Json --all  # Include hidden/obsolete
dotnet-inspect api Unsafe --package System.Runtime.CompilerServices.Unsafe --unsafe  # Unsafe methods only
dotnet-inspect api Command --package System.CommandLine --docs  # With documentation
dotnet-inspect api JsonSerializer --package Newtonsoft.Json --docs --browsable-urls  # Docs with browser-friendly URLs
dotnet-inspect api JsonSerializer --package Newtonsoft.Json --source-url --fields-only  # Source URL only, no members
dotnet-inspect api CommandLineBuilder --package dotnet-inspect  # dotnet-inspect inspecting itself
```

### type

View type shape with hierarchy, interfaces, and members in tree format.

```bash
dotnet-inspect type JsonSerializer --package System.Text.Json  # Inheritance, interfaces, members
dotnet-inspect type Command --package System.CommandLine       # Shows base class and interfaces
dotnet-inspect type JsonSerializer --package System.Text.Json --json  # JSON output
```

### diff

Compare API surfaces between package versions with semantic awareness.

```bash
dotnet-inspect diff JsonSerializer --package System.Text.Json@9.0.0..10.0.0  # Compare type between versions
dotnet-inspect diff Command --package System.CommandLine@2.0.1..2.0.2        # See what changed
```

### platform

List installed .NET SDK frameworks and their assemblies.

```bash
dotnet-inspect platform                           # List installed frameworks
dotnet-inspect platform --list-versions           # Show all installed versions
dotnet-inspect platform --framework runtime       # List assemblies in runtime
dotnet-inspect platform --framework runtime@9.0.12  # Specific version
dotnet-inspect platform --framework runtime --types  # Include public type counts
```

Use `api --platform` to inspect platform assemblies directly:

```bash
dotnet-inspect api --platform System.Text.Json              # List types in platform assembly
dotnet-inspect api JsonSerializer --platform System.Text.Json  # Specific type
dotnet-inspect api JsonSerializer --platform System.Text.Json --docs  # With documentation
dotnet-inspect api --platform System.Text.Json --framework runtime@9.0.12  # Specific runtime version
```

## Key Features

- **Package inspection**: View metadata, dependencies, target frameworks, and file structure
- **Security awareness**: Detects vulnerabilities (with CVE IDs) and deprecation status from NuGet APIs
- **API surface extraction**: List types and members with full signatures including parameter names
- **Generic type support**: Use C#-style syntax (`Option<T>`) or CLR backtick notation (`Option`1`)
- **Constructor emphasis**: `--ctor` shows constructors with parameter details (required vs optional)
- **API diff**: Compare type APIs between package versions with semantic awareness
- **Type hierarchy**: View inheritance chains and implemented interfaces in tree format
- **Type filtering**: Filter types by glob pattern (e.g., `--filter "*Json*"`)
- **Smart defaults**: Excludes `[EditorBrowsable(Never)]` and `[Obsolete]` members by default
- **Unsafe code filtering**: Filter to methods with pointer signatures using `--unsafe`
- **Version comparison**: Compare APIs between package versions using diff
- **SourceLink support**: Fetch source URLs and documentation from Portable PDBs (embedded or .snupkg)
- **Platform assembly inspection**: Inspect .NET SDK reference assemblies without downloading packages
- **LLM-friendly URLs**: Source links use `/raw/` format (302 redirect) by default; use `--browsable-urls` for `/blob/`
- **Fields-only mode**: Show only type info (source URL, docs) without member tables via `--fields-only`
- **Multiple output formats**: Markdown tables, tree view, signatures-only, or JSON
- **Smart TFM selection**: Auto-selects highest target framework when multiple exist
- **Caching**: Reads from NuGet cache and caches downloads for fast repeated access

## Output Formats

- **Markdown** (default): Human-readable tables, powered by [Markout](https://github.com/richlander/markout)
- **Signatures only** (`--signatures-only`): Plain method signatures, minimal tokens
- **JSON** (`--json`): Machine-readable output
- **Compact JSON** (`--json --compact`): Minified, omits defaults

## Output Control

Output verbosity follows a **height × width** model for progressive disclosure:

- **Width** (verbosity) controls information density: `-v:q` (quiet) → `-v:d` (detailed)
- **Height** (sections) controls which sections appear: `-s:1,2` (include) or `-x:3` (exclude)

This lets you dial in exactly the information you need. Run a command once to see section numbers, then filter.

```bash
dotnet-inspect package System.Text.Json -v:d      # Detailed: all sections, full tables
dotnet-inspect package System.Text.Json -s:1     # Section 1 only (metadata)
dotnet-inspect package System.Text.Json -v:d -x:2  # Detailed, but skip dependencies
```

### Verbosity Levels

| Level | Flag | Package Command Output |
|-------|------|------------------------|
| Quiet | `-v q` | H1 title + compact line |
| Minimal | `-v m` | H1 + description + compact line **(default)** |
| Normal | `-v n` | + Vulnerabilities section + Metadata table |
| Detailed | `-v d` | + tier 2 sections (Files, Package Dependencies) |

### Compact Line Format

At quiet and minimal verbosity, essential fields appear in a pipe-delimited compact format:

```
Type: Library | TFM: net8.0 | Updated: 2024-07-09 | Vulnerabilities: 1
```

- **Type**: Package type (Library, DotnetTool, etc.)
- **TFM**: Newest/highest target framework in the package
- **Updated**: Publication date from NuGet
- **Deprecated**: Deprecation reason (shown if package is deprecated)
- **Vulnerabilities**: Count of known security vulnerabilities

At normal+ verbosity, these fields also appear in the Metadata table alongside extended properties (Authors, License, Downloads, etc.).

## Caching

The tool uses two cache locations:

1. **NuGet cache** (`~/.nuget/packages`): Read-only, checked first
2. **App cache** (`~/.local/share/dotnet-inspect/packages`): Downloaded packages are cached here

Use `--verbose` to see cache activity.

## LLM Integration

This tool is designed for LLM-driven .NET development. Run `dotnet-inspect llmstxt` for comprehensive usage examples optimized for LLM context.

## Requirements

- .NET 10.0 SDK or later

## License

MIT
