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

Inspect NuGet packages - view metadata, dependencies, and file structure.

```bash
dotnet-inspect package System.Text.Json              # Package metadata
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
dotnet-inspect api JsonSerializer --package System.Text.Json    # Specific type
dotnet-inspect api JsonSerializer --package System.Text.Json -m Deserialize  # Filter to member
dotnet-inspect api Command --package System.CommandLine --docs  # With documentation
dotnet-inspect api CommandLineBuilder --package dotnet-inspect  # dotnet-inspect inspecting itself
```

## Key Features

- **Package inspection**: View metadata, dependencies, target frameworks, and file structure
- **API surface extraction**: List types and members with full signatures
- **Version comparison**: Compare APIs between package versions using diff
- **SourceLink support**: Fetch source URLs and documentation from embedded PDBs
- **Multiple output formats**: Markdown (default) or JSON
- **Smart TFM selection**: Auto-selects highest target framework when multiple exist
- **Caching**: Reads from NuGet cache and caches downloads for fast repeated access

## Output Formats

- **Markdown** (default): Human-readable tables
- **JSON** (`--json`): Machine-readable output
- **Compact JSON** (`--json --compact`): Minified, omits defaults

## Verbosity Levels

Control output detail with `-v:`:

| Flag | Level | Description |
|------|-------|-------------|
| `-v:q` | Quiet | Summary only |
| `-v:m` | Minimal | Summary + compact metadata |
| `-v:n` | Normal | Full sections (default) |
| `-v:d` | Detailed | All sections with full tables |

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
