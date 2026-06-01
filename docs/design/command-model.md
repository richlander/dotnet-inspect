# Command Model

This document describes the conceptual model of dotnet-inspect commands. It serves as a contract for tool behavior that users and skill authors can rely on.

## Core Concepts

### Library Sources

dotnet-inspect works with .NET libraries. There are three ways to specify where a library comes from:

| Command | Source | Example |
| --------- | -------- | --------- |
| `package X` | NuGet package | `package System.Text.Json` |
| `platform X` | Local SDK/runtime | `platform System.Text.Json` |
| `library ./path` | Local file | `library ./bin/MyLib.dll` |

These commands expose library/package inspection views. Use the top-level `audit` command when the goal is provenance, compatibility, or supply-chain signal reporting.

### Inspection commands

Use inspection commands for detailed exploration:

| Command | Shows |
| ------- | ----- |
| `package` | Package metadata, versions, dependencies, files, TFMs. |
| `library` | Library metadata, symbols, SourceLink, references, resources. |
| `type`/`member` | API shape, docs, source, decompiled C#, and IL. |
| `audit` | Package/library audit signals. |

### The `audit` Command

`audit` is an opinionated command for signal reporting. It:

- Auto-detects input type (package name, file path, nupkg, directory)
- Reports metadata-only signals by default
- Uses `--full`/`--all` for broad opt-in enrichment and `-v:d` for detailed expensive sections

```bash
dotnet inspect audit Markout@0.1.4           # package metadata signals
dotnet inspect audit System.Text.Json        # platform library metadata signals
dotnet inspect audit ./bin/MyLib.dll         # local file metadata signals
dotnet inspect audit System.Text.Json --full
dotnet inspect audit System.Text.Json --all
dotnet inspect audit System.Text.Json -v:d
dotnet inspect audit package Markout --full
```

`--full` (alias: `--all`) enables broad target-appropriate audit scope. For libraries, that means symbol/PDB acquisition. For packages, it means NuGet registry signals. `-v:d` adds detailed audit sections; for libraries that includes SourceLink Audit, which verifies every tracked source URL and may be expensive for large assemblies. `--source-audit` (alias: `--source`), `--symbols`, and `--nuget` remain available for narrower control.

## Command Patterns

### Package-centric workflow

Start with a package, drill down into details:

```bash
dotnet inspect package Newtonsoft.Json           # metadata
dotnet inspect package Newtonsoft.Json --library # library info
dotnet inspect audit package Newtonsoft.Json     # audit signals
dotnet inspect package Newtonsoft.Json --api      # public API
```

### Platform-centric workflow

Inspect libraries from the local .NET SDK/runtime:

```bash
dotnet inspect platform                          # list frameworks
dotnet inspect platform System.Text.Json         # inspect library
dotnet inspect audit System.Text.Json            # platform audit signals
```

### File-centric workflow

Inspect local library files:

```bash
dotnet inspect library ./bin/MyLib.dll          # basic info
dotnet inspect audit ./bin/MyLib.dll            # audit signals
```

### Quick audit workflow

For CI or quick checks:

```bash
dotnet inspect audit Markout@0.1.4 -v:q
# SourceLink: passed
# Deterministic: passed
```

## Equivalences

Some commands are aliases or have equivalent forms:

```bash
# These are equivalent
dotnet inspect platform System.Text.Json
dotnet inspect library System.Text.Json --platform

# Use explicit package audit when package routing matters
dotnet inspect audit Markout@0.1.4
dotnet inspect audit package Markout@0.1.4
```

## Stability Guarantees

The following are considered stable and will not change without a major version bump:

1. **Command names**: `package`, `platform`, `library`, `audit`, `api`, `find`, `type`, `diff`
2. **Input syntax**: Package references use `name@version` format
3. **Exit codes**: Zero for success, non-zero for failure
4. **JSON output**: Schema for `--json` output is stable per command

The following may change:

1. **Markdown output format**: For human consumption, may evolve
2. **Verbosity level details**: What appears at each `-v:` level
3. **New flags**: New inspection capabilities may be added

## Deprecations

When commands or flags are deprecated:

1. They continue to work but emit a warning to stderr
2. Documentation is updated to show the new approach
3. After two minor versions, deprecated items may be removed

Current deprecations:

| Deprecated             | Use Instead                   | Removal Target |
| ---------------------- | ----------------------------- | -------------- |
| `package X --metadata` | `library X`                   | 0.3.0          |
| `--strict`             | `audit` (signal report)       | 0.3.0          |
