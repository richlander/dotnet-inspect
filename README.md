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
| `api X` | Public API surface (table format, or `--shape` for hierarchy) |
| `diff X` | Compare versions with breaking/additive classification |
| `extensions X` | Find extension methods/properties for a type |
| `implements X` | Find types implementing an interface or extending a class |
| `find X` | Search for types |

### Common Flags

| Flag | Description |
|------|-------------|
| `--sourcelink-audit` | Full provenance verification (parallel HTTP HEAD) |
| `--json` | JSON output |
| `-v:q/m/n/d` | Verbosity: quiet, minimal, normal, detailed |

## Commands

### package

Inspect NuGet packages. This is the default command.

```bash
dotnet-inspect System.Text.Json                    # Metadata (latest version)
dotnet-inspect System.Text.Json@8.0.4 -v:d         # Detailed (shows vulnerability)
dotnet-inspect System.Text.Json --versions         # List available versions
dotnet-inspect System.Text.Json --sourcelink-audit  # Provenance verification
dotnet-inspect System.Text.Json --files --all      # File structure
```

#### Multi-assembly packages

Some packages bundle multiple assemblies per TFM (e.g., `Microsoft.Azure.SignalR` ships both `Microsoft.Azure.SignalR.dll` and `Microsoft.Azure.SignalR.Common.dll`). The `Libraries` field shows when a package contains more than one assembly.

```bash
dotnet-inspect Microsoft.Azure.SignalR                # Shows Libraries: 2
dotnet-inspect Microsoft.Azure.SignalR --files        # See all assemblies per TFM
dotnet-inspect Microsoft.Azure.SignalR --tfms         # List target frameworks
```

```text
| Target Frameworks | 2 |
| Libraries | 2 |
```

Use `--files` to see the full layout:

```text
└─ lib
   ├─ net8.0
   │  ├─ Microsoft.Azure.SignalR.Common.dll
   │  └─ Microsoft.Azure.SignalR.dll
   └─ netstandard2.0
      ├─ Microsoft.Azure.SignalR.Common.dll
      └─ Microsoft.Azure.SignalR.dll
```

Inspect or compare individual assemblies using `package --metadata` with `--tfm`:

```bash
dotnet-inspect package Microsoft.Azure.SignalR --metadata                   # Primary assembly (highest TFM)
dotnet-inspect package Microsoft.Azure.SignalR --metadata --tfm net8.0      # Specific TFM
dotnet-inspect api Microsoft.Azure.SignalR                                  # API surface (all assemblies)
```

### platform

List frameworks or inspect platform assemblies.

```bash
dotnet-inspect platform                            # List frameworks
dotnet-inspect platform --framework runtime        # List runtime assemblies
dotnet-inspect platform System.Text.Json           # Inspect assembly
dotnet-inspect platform System.Text.Json --sourcelink-audit  # Audit platform assembly
```

### api

Extract public API surface with positional syntax and fuzzy matching.

```bash
dotnet-inspect api System.Text.Json                              # All types in package
dotnet-inspect api System.Text.Json JsonSerializer               # Specific type
dotnet-inspect api System.Text.Json JsonSerializer Serialize     # Filter to member(s)
dotnet-inspect api System.Text.Json JsonArray -v:d -s:Interfaces # Interfaces only
dotnet-inspect api System.Text.Json JsonSerializer -s:Methods    # Methods section only
dotnet-inspect api System.Text.Json JsonSerializer -s            # Header only (no sections)
dotnet-inspect api --platform System.Text.Json JsonSerializer    # Platform assembly
```

Example: `dotnet-inspect api System.Text.Json JsonArray -v:d -s:Interfaces,Baseclass`

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

### extensions

Find extension methods and properties for a type. Scopes control where to search.

```bash
dotnet-inspect extensions string                                          # Runtime extensions (default scope)
dotnet-inspect extensions 'IEnumerable<T>'                                # Generic types
dotnet-inspect extensions string --assembly ./MyLib.dll                   # Search a local assembly
dotnet-inspect extensions ChatClient --package Microsoft.Extensions.AI.OpenAI  # Cross-package extensions
dotnet-inspect extensions IServiceCollection \
  --package Microsoft.Extensions.DependencyInjection \
  --package Microsoft.Extensions.Azure \
  --package AWSSDK.Extensions.NETCore.Setup                                   # Multi-package scan
dotnet-inspect extensions string --framework runtime --assembly ./MyLib.dll  # Multiple scopes
dotnet-inspect extensions HttpClient --reachable                          # Include extensions on reachable types
dotnet-inspect extensions HttpResponseMessage --reachable                  # Useful when the type itself has no extensions
```

Detects both classic extension methods and C# 14 extension properties.

### assembly

Inspect a specific assembly file.

```bash
dotnet-inspect assembly ./bin/MyLib.dll            # Local file
dotnet-inspect assembly ./bin/MyLib.dll --sourcelink-audit  # With provenance check
```

### diff

Compare API surfaces between versions. Changes are classified as breaking, additive, or potentially breaking.

```bash
dotnet-inspect diff System.Text.Json@9.0.0..10.0.2                # positional package
dotnet-inspect diff --package System.Text.Json@9.0.0..10.0.2      # explicit flag
dotnet-inspect diff --platform System.Text.Json@8.0.23..10.0.2    # platform assembly
dotnet-inspect diff System.Text.Json@9.0.0..10.0.2 --stat         # compact summary
dotnet-inspect diff System.Text.Json@9.0.0..10.0.2 --breaking     # breaking changes only
dotnet-inspect diff System.Text.Json@9.0.0..10.0.2 --additive     # additive changes only
dotnet-inspect diff System.Text.Json@9.0.0..10.0.2 JsonSerializer  # filter to type
```

### implements

Find types implementing an interface or extending a class.

```bash
dotnet-inspect implements IDisposable --framework runtime
dotnet-inspect implements Stream --framework runtime
dotnet-inspect implements IJsonTypeInfoResolver --package System.Text.Json
```

### cache

Manage the dotnet-inspect cache (downloaded packages, sources, symbols).

```bash
dotnet-inspect cache                               # Show cache size breakdown
dotnet-inspect cache --clean                        # Clear the cache
```

### cli

Show CLI command structure as an API listing.

```bash
dotnet-inspect cli                                  # All commands
dotnet-inspect cli api                              # Single command with options
dotnet-inspect cli -v:q                             # Command names only
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
dotnet-inspect api System.Text.Json JsonSerializer -s:Methods  # Include only Methods
dotnet-inspect api System.Text.Json JsonSerializer -s      # Header only
```

## LLM Integration

This tool is [designed for LLM-driven development](docs/llm-design.md). Run `dotnet-inspect llmstxt` for detailed usage patterns.

A skill for use with GitHub Copilot agent mode is available at [dotnet-skills](https://github.com/richlander/dotnet-skills).

## Requirements

.NET 10.0 SDK or later

## License

MIT
