# dotnet-inspect

CLI tool for inspecting .NET assemblies and NuGet packages. View metadata, APIs, vulnerabilities, audit provenance, and compare versions.

## Installation

```bash
dotnet tool install -g dotnet-inspect
```

## Quick Reference

| Command | Purpose |
|---------|---------|
| `package X` | Package metadata, dependencies, files |
| `platform X` | Inspect SDK/runtime assembly |
| `assembly ./path` | Inspect local file |
| `audit X` | Verify provenance (SourceLink, determinism) |
| `api X` | Public API surface |
| `type X` | Type hierarchy and members |
| `diff X` | Compare versions |
| `find X` | Search for types |

### Common Flags

| Flag | Description |
|------|-------------|
| `--audit` | Full provenance verification (always strict) |
| `--sourcelink` | Show SourceLink presence/URL (fast, no HTTP) |
| `--json` | JSON output |
| `-v:q/m/n/d` | Verbosity: quiet, minimal, normal, detailed |

## Commands

### package

Inspect NuGet packages. This is the default command.

```bash
dotnet-inspect System.Text.Json                    # Metadata (latest version)
dotnet-inspect System.Text.Json@8.0.4 -v:d         # Detailed (shows vulnerability)
dotnet-inspect System.Text.Json --versions         # List available versions
dotnet-inspect System.Text.Json --audit            # Provenance verification; optional `--strict` mode
dotnet-inspect System.Text.Json --files --all      # File structure
```

### audit

Verify package/assembly provenance. Always runs strict verification.

```bash
dotnet-inspect audit Markout@0.1.4                 # Package
dotnet-inspect audit ./bin/MyLib.dll               # Local file
dotnet-inspect audit ./artifacts/*.nupkg           # Multiple nupkgs
dotnet-inspect audit Markout@0.1.4 -v:q            # Quiet (pass/fail)
```

Note: Strict audit hits the network and will take longer.

### platform

List frameworks or inspect platform assemblies.

```bash
dotnet-inspect platform                            # List frameworks
dotnet-inspect platform --framework runtime        # List runtime assemblies
dotnet-inspect platform System.Text.Json           # Inspect assembly
dotnet-inspect platform System.Text.Json --audit   # Audit platform assembly
```

### api2

Extract public API surface with positional syntax and fuzzy matching.

```bash
dotnet-inspect api2 System.Text.Json                              # All types in package
dotnet-inspect api2 System.Text.Json JsonSerializer               # Specific type
dotnet-inspect api2 System.Text.Json JsonSerializer Serialize     # Filter to member(s)
dotnet-inspect api2 System.Text.Json JsonArray -v:d -s:Interfaces # Interfaces only
dotnet-inspect api2 System.Text.Json JsonSerializer -s:Methods    # Methods section only
dotnet-inspect api2 System.Text.Json JsonSerializer -s            # Header only (no sections)
dotnet-inspect api2 --platform System.Text.Json JsonSerializer    # Platform assembly
```

Example: `dotnet-inspect api2 System.Text.Json JsonArray -v:d -s:Interfaces,Baseclass`

```text
# System.Text.Json.Nodes.JsonArray (System.Text.Json 10.0.2)

Kind: class | Modifiers: sealed | Base: System.Text.Json.Nodes.JsonNode | Assembly: System.Text.Json | Package: System.Text.Json | Version: 10.0.2

## Interfaces

| Interface |
| --------- |
| System.Collections.Generic.ICollection<System.Text.Json.Nodes.JsonNode> |
| System.Collections.Generic.IEnumerable<System.Text.Json.Nodes.JsonNode> |
| System.Collections.Generic.IList<System.Text.Json.Nodes.JsonNode> |
| System.Collections.IEnumerable |

## Baseclass

| Type |
| ---- |
| System.Text.Json.Nodes.JsonNode |
```

### api

Extract public API surface (explicit flags).

```bash
dotnet-inspect api --package System.Text.Json                     # All types
dotnet-inspect api JsonSerializer --package System.Text.Json      # Specific type
dotnet-inspect api JsonSerializer --package System.Text.Json -m Serialize  # Filter to member
dotnet-inspect api --platform System.Text.Json                    # Platform assembly
```

### assembly

Inspect a specific assembly file.

```bash
dotnet-inspect assembly ./bin/MyLib.dll            # Local file
dotnet-inspect assembly ./bin/MyLib.dll --audit    # With provenance check
```

### diff

Compare API surfaces between versions.

```bash
dotnet-inspect diff JsonSerializer --package System.Text.Json@9.0.0..10.0.2
dotnet-inspect diff JsonSerializer --platform System.Text.Json@8.0.23..10.0.2
```

### type

Show type hierarchy with members.

```bash
dotnet-inspect type JsonSerializer --package System.Text.Json
```

## Custom NuGet Sources

```bash
dotnet-inspect package MyPackage --source https://my-feed/v3/index.json
dotnet-inspect package MyPackage --add-source https://dev-feed/v3/index.json
dotnet-inspect package MyPackage --nugetconfig ./nuget.config
```

## Output Control

**Verbosity** (`-v`): q(uiet) → m(inimal) → n(ormal) → d(etailed)

Each level includes a **compact summary line** with key metadata:

```text
Type: Library | TFM: net10.0 | Updated: 2026-01-13 | Vulnerabilities: 1
```

**Sections**: Use `-s:Name` to include or `-x:Name` to exclude sections by name. Bare `-s` shows header only.

```bash
dotnet-inspect System.Text.Json -v:d -x:Statistics,Files   # Exclude by name
dotnet-inspect api2 System.Text.Json JsonSerializer -s:Methods  # Include only Methods
dotnet-inspect api2 System.Text.Json JsonSerializer -s      # Header only
```

## LLM Integration

This tool is [designed for LLM-driven development](docs/llm-design.md). Run `dotnet-inspect llmstxt` for detailed usage patterns.

A skill for use with GitHub Copilot agent mode is available at [dotnet-skills](https://github.com/richlander/dotnet-skills).

## Requirements

.NET 10.0 SDK or later

## License

MIT
