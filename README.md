# dotnet-inspect

CLI tool for inspecting .NET assemblies and NuGet packages. View metadata, APIs, vulnerabilities, and compare versions.

## Installation

```bash
dotnet tool install -g dotnet-inspect
```

## Test Packages

Use `System.Text.Json` versions to explore different scenarios:

| Version | Scenario |
|---------|----------|
| 10.0.2 | Latest, multi-targeting (net462, net8.0, net9.0, net10.0, netstandard2.0) |
| 8.0.4 | Vulnerable (CVE-2024-43485) |
| 5.0.0 | Deprecated |

Other useful test packages:
- `dotnet-ef` - Tool package with commands
- `Azure.Mcp` - RID-specific tool (shows RID Packages section)
- `Microsoft.Data.SqlClient` - Library with native runtimes

## Commands

### package

```bash
dotnet-inspect System.Text.Json                    # Metadata (latest version)
dotnet-inspect System.Text.Json 8.0.4 -v:d         # Detailed view (shows vulnerability)
dotnet-inspect System.Text.Json 5.0.0              # Shows deprecation notice
dotnet-inspect System.Text.Json --versions         # List available versions
dotnet-inspect dotnet-ef --files --all             # Tool package file structure
dotnet-inspect package --discover                  # List available sections
```

### api

```bash
dotnet-inspect api --package System.Text.Json                     # List all types
dotnet-inspect api JsonSerializer --package System.Text.Json      # Specific type
dotnet-inspect api JsonSerializer --package System.Text.Json -m Serialize  # Filter to member
dotnet-inspect api 'Option<T>' --package System.CommandLine       # Generic types
dotnet-inspect api Command --package System.CommandLine --ctor    # Constructors with details
dotnet-inspect api --platform System.Text.Json                    # Platform assembly (no download)
```

### assembly

```bash
dotnet-inspect assembly --package System.Text.Json --tfm net8.0 --audit  # SourceLink audit
```

### diff

```bash
dotnet-inspect diff JsonSerializer --package System.Text.Json@9.0.0..10.0.2
dotnet-inspect diff JsonSerializer --platform System.Text.Json@8.0.23..10.0.2
```

### type

```bash
dotnet-inspect type JsonSerializer --package System.Text.Json     # Inheritance tree
```

### samples (experimental)

Fetch code samples via SourceLink for packages with `<code source=...>` doc references:

```bash
dotnet-inspect samples --package Newtonsoft.Json --list           # List available samples
dotnet-inspect samples JsonConvert --package Newtonsoft.Json      # Fetch samples for type
dotnet-inspect samples --package Markout --list                   # Works with SourceLink-enabled packages
```

Note: Platform assemblies (dotnet/runtime) use inline examples and are not supported.

### platform

```bash
dotnet-inspect platform --list-versions            # Installed SDK versions
dotnet-inspect platform --framework runtime        # List runtime assemblies
```

## Output Control

**Verbosity** (`-v`): q(uiet) → m(inimal) → n(ormal) → d(etailed)

Each level includes a **compact summary line** with key metadata:
```
Type: Library | TFM: net10.0 | Updated: 2026-01-13 | Vulnerabilities: 1
```

**Sections**: Use `--discover` to list sections, then filter with `-s:1,2` (include) or `-x:3` (exclude)

```bash
dotnet-inspect System.Text.Json -v:d               # All sections
dotnet-inspect System.Text.Json -v:d -x:3,4        # Exclude Package Dependencies and Files
```

## Output Formats

- Markdown tables (default)
- `--signatures-only` - Plain signatures
- `--json` - JSON output
- `--json --compact` - Minified JSON

## LLM Integration

This tool is [designed for LLM-driven development](docs/llm-design.md). Run `dotnet-inspect llmstxt` for detailed usage patterns.

## Requirements

.NET 10.0 SDK or later

## License

MIT
