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
dotnet-inspect System.Text.Json --audit            # Provenance verification
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

### platform

List frameworks or inspect platform assemblies.

```bash
dotnet-inspect platform                            # List frameworks
dotnet-inspect platform --framework runtime        # List runtime assemblies
dotnet-inspect platform System.Text.Json           # Inspect assembly
dotnet-inspect platform System.Text.Json --audit   # Audit platform assembly
```

### api

Extract public API surface.

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

<<<<<<< HEAD
Each level includes a **compact summary line** with key metadata:

```text
Type: Library | TFM: net10.0 | Updated: 2026-01-13 | Vulnerabilities: 1
```

**Sections**: Use `--discover` to list, then `-s:1,2` (include) or `-x:3` (exclude)

```bash
dotnet-inspect System.Text.Json -v:d               # All sections
dotnet-inspect System.Text.Json -v:d -x:3,4        # Exclude sections 3,4
```

## LLM Integration

This tool is [designed for LLM-driven development](docs/llm-design.md). Run `dotnet-inspect llmstxt` for detailed usage patterns.

A skill for use with GitHub Copilot agent mode is available at [dotnet-skills](https://github.com/richlander/dotnet-skills).

## Requirements

.NET 10.0 SDK or later

## License

MIT
