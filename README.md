# dotnet-inspect

CLI tool for inspecting .NET libraries and NuGet packages. It is for .NET what `docker inspect` and `kubectl describe` are for container land — view metadata, APIs, vulnerabilities, provenance, and compare versions.

## Installation

```bash
dotnet tool install -g dotnet-inspect
```

Or run without installing (like `npx`):

```bash
dnx dotnet-inspect -y -- <command>
```

## Quick Reference

| Command | Purpose |
|---------|---------|
| `package X` | Package metadata, dependencies, files, versions |
| `library X` | Library metadata, symbols, SourceLink audit, dependency tree |
| `type` | Discover types (terse, no docs) — use `--shape` for hierarchy |
| `member X` | Inspect members (docs on by default, supports dotted syntax) |
| `diff X` | Compare versions with breaking/additive classification |
| `extensions X` | Find extension methods/properties for a type |
| `implements X` | Find types implementing an interface or extending a class |
| `find X` | Search for types across packages, frameworks, and local assets |
| `samples X` | Fetch and display code samples |
| `platform` | List installed frameworks |
| `cli` | CLI args explorer (tree view) |

### Bare Names

A bare name like `dotnet-inspect System.Text.Json` uses a router to pick the best source. Platform libraries (`System.*`, `Microsoft.AspNetCore`) resolve to the installed SDK by default. Other names resolve to NuGet packages. Use explicit `package` or `library --package` to override.

### Common Flags

| Flag | Description |
|------|-------------|
| `-v:q/m/n/d` | Verbosity: quiet, minimal (default), normal, detailed |
| `--platform` | Search all platform frameworks (find, extensions, implements) |
| `--json` | JSON output |
| `-S Name` | Select section or field (`-S` alone to discover) |
| `-F Name` | Explicit field selection (when name is ambiguous) |
| `--markdown` | Full document output (oneline is default) |
| `--shape` | Type shape diagram (hierarchy + members) — `type` command |
| `--docs` / `--no-docs` | Control XML docs — `member` has docs on by default |
| `--source-link-audit` | SourceLink/determinism audit |
| `-T:q/d` | Tips verbosity (contextual hints on stderr) |

## Commands

### package

Inspect NuGet packages. This is the default command for bare names that don't match platform libraries.

```bash
dotnet-inspect package System.Text.Json                     # Metadata (latest version)
dotnet-inspect package System.Text.Json@8.0.0 -v:d          # Detailed (shows vulnerabilities)
dotnet-inspect package System.Text.Json --versions          # List available versions
dotnet-inspect package System.Text.Json --version 11.0.0-preview*  # Wildcard version
dotnet-inspect package System.Text.Json --layout --lib      # File tree (lib/ only)
dotnet-inspect package System.Text.Json --dependencies      # Package dependency tree
dotnet-inspect package System.Text.Json --tfms              # List target frameworks
```

#### Multi-library packages

Some packages bundle multiple libraries per TFM (e.g., `Microsoft.Azure.SignalR`).

```bash
dotnet-inspect package Microsoft.Azure.SignalR              # Shows Libraries: 2
dotnet-inspect package Microsoft.Azure.SignalR --layout     # File tree
dotnet-inspect member Microsoft.Azure.SignalR -v:q --library Microsoft.Azure.SignalR.Common.dll  # Secondary library
```

#### Custom NuGet sources

```bash
dotnet-inspect package MyPackage --source https://my-feed/v3/index.json
dotnet-inspect package MyPackage --add-source https://dev-feed/v3/index.json --prerelease
dotnet-inspect package MyPackage --nugetconfig ./nuget.config
```

### library

Inspect a library — from platform, NuGet package, or local file.

```bash
dotnet-inspect library System.Text.Json                     # Platform library (runtime)
dotnet-inspect library --package System.Text.Json            # Library from NuGet package
dotnet-inspect library ./bin/MyLib.dll                       # Local file
dotnet-inspect library --package System.Text.Json -S         # Discover available sections and fields
dotnet-inspect library --package System.Text.Json --source-link-audit  # SourceLink audit
dotnet-inspect library Microsoft.Extensions.AI.OpenAI --dependencies   # Dependency tree (visual)
dotnet-inspect library System.Text.Json --references -S "Library References"  # Direct references
dotnet-inspect library --package System.Text.Json --extract-resources resources/  # Extract resources
```

### type

Discover types in a package or library — terse output, no docs by default.

```bash
dotnet-inspect type --package System.Text.Json                   # All types in package
dotnet-inspect type -t "JsonS*" --package System.Text.Json       # Types matching glob
dotnet-inspect type 'HashSet<T>' --platform System.Collections --shape  # Type shape diagram
dotnet-inspect type --platform System.Text.Json                  # Platform library
dotnet-inspect type --package System.Text.Json --json            # JSON output
```

### member

Inspect type members — docs on by default, supports dotted syntax.

```bash
dotnet-inspect member JsonSerializer --package System.Text.Json         # All members (docs on)
dotnet-inspect member JsonSerializer --package System.Text.Json --no-docs  # Suppress docs
dotnet-inspect member JsonSerializer --package System.Text.Json -m Serialize  # Filter to member
dotnet-inspect member -m JsonSerializer.Deserialize --package System.Text.Json  # Dotted syntax
dotnet-inspect member 'Option<T>' --package System.CommandLine          # Generic types (quote!)
```

Member selection and decompilation — use `--select` to see `Name:N` addressing hints, then drill in:

```bash
$ dotnet-inspect member OptionsFactory --package Microsoft.Extensions.Options --select
## Constructors

| Select | Name | Signature |
| ------ | ---- | --------- |
| `.ctor:1` | .ctor | `void .ctor(IEnumerable<IConfigureOptions<TOptions>>, ...)` |
| `.ctor:2` | .ctor | `void .ctor(IEnumerable<IConfigureOptions<TOptions>>, ..., IEnumerable<IValidateOptions<TOptions>>)` |

## Methods

| Select | Name | Signature |
| ------ | ---- | --------- |
| `Create` | Create | `TOptions Create(string)` |
```

Then target a member using the `Name:N` shorthand to get source, decompiled C#, and IL:

```bash
$ dotnet-inspect member OptionsFactory --package Microsoft.Extensions.Options Create
## Source                          # Original C# (via SourceLink)
## Lowered C#                     # Decompiled C# faithful to IL semantics
## IL                             # Raw IL disassembly with resolved tokens
## IL (Annotated)                 # IL with pre-execution stack state at each instruction
```

### diff

Compare API surfaces between versions. Changes are classified as breaking, additive, or potentially breaking.

```bash
dotnet-inspect diff System.CommandLine@2.0.0-beta4.22272.1..2.0.3 -v:q  # Full package diff
dotnet-inspect diff JsonSerializer --package System.Text.Json@9.0.0..10.0.2  # Single type
dotnet-inspect diff "*Writer*" --package Markout@0.1.8..0.2.0            # Glob filter
dotnet-inspect diff --platform System.Text.Json@8.0.23..10.0.2           # Platform versions
dotnet-inspect diff System.Text.Json@9.0.0..10.0.2 --oneline            # One change per line
dotnet-inspect diff System.Text.Json@9.0.0..10.0.2 --breaking           # Breaking only
```

### find

Search for types across packages, frameworks, and local assets.

```bash
dotnet-inspect find HttpClient                           # Runtime (default scope)
dotnet-inspect find "*Stream*" -n 10                     # Glob, limit results
dotnet-inspect find "*Json*" --package System.Text.Json  # Search in package
dotnet-inspect find "ChatClient*" --oneline                # Columnar output
dotnet-inspect find ILogger --aspnetcore                 # ASP.NET Core packages
dotnet-inspect find "*Command*" --project ./MyApp.csproj # Project dependencies
```

### extensions

Find extension methods and properties for a type. Detects both classic extension methods and C# 14 extension properties.

```bash
dotnet-inspect extensions HttpClient                         # Runtime (default)
dotnet-inspect extensions HttpClient --reachable             # Include reachable types
dotnet-inspect extensions DbContext                           # Default scope
dotnet-inspect extensions IDistributedApplicationBuilder \
  --package Aspire.Hosting --package Aspire.Hosting.Redis    # Multi-package scan
```

### implements

Find types implementing an interface or extending a base class.

```bash
dotnet-inspect implements Stream                             # Default scope
dotnet-inspect implements IDisposable --platform             # All platform frameworks
dotnet-inspect implements IJsonTypeInfoResolver --package System.Text.Json
```

### samples

Fetch and display code samples from SourceLink-indexed sources.

```bash
dotnet-inspect samples Markout MarkoutWriter --list  # List available samples
dotnet-inspect samples Markout MarkoutWriter         # Print all samples
dotnet-inspect samples Newtonsoft.Json JObject        # Third-party examples
```

### platform

List installed frameworks.

```bash
dotnet-inspect platform                             # List frameworks
dotnet-inspect platform --framework runtime         # List runtime libraries
dotnet-inspect platform --list-versions             # Installed SDK versions
```

### cache

```bash
dotnet-inspect cache                                # Show cache size breakdown
dotnet-inspect cache --clean                        # Clear the cache
```

### cli

```bash
dotnet-inspect cli                                  # Tree view (single level)
dotnet-inspect cli -v:d                             # Deep view (all levels)
dotnet-inspect cli api                              # Specific command with options
```

## Output Control

**Verbosity** (`-v`): q(uiet) → m(inimal) → n(ormal) → d(etailed)

Each level includes a **compact summary line** with key metadata:

```text
Version: 8.0.0 | Type: Library | TFM: net8.0 | Updated: 2023-11-14 | Vulnerabilities: 2
```

**Output Selection**: Use `-S Name` to select sections or fields by name. Section names win when ambiguous; use `-F Name` to force field selection. Bare `-S` lists available names.

**JSON**: `--json` for full JSON, `--json --compact` for minified.

## LLM Integration

This tool is [designed for LLM-driven development](docs/llm-design.md). Run `dotnet-inspect llmstxt` for detailed usage patterns.

A skill for use with GitHub Copilot agent mode is available at [dotnet-skills](https://github.com/richlander/dotnet-skills).

## Requirements

.NET 10.0 SDK or later

## License

MIT
