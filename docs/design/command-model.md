# Command Model

This document describes the conceptual model of dotnet-inspect commands. It serves as a contract for tool behavior that users and skill authors can rely on.

## Core Concepts

### Library Sources

dotnet-inspect works with .NET libraries. There are three ways to specify where a library comes from:

| Command | Source | Example |
|---------|--------|---------|
| `package X` | NuGet package | `package System.Text.Json` |
| `platform X` | Local SDK/runtime | `platform System.Text.Json` |
| `library ./path` | Local file | `library ./bin/MyLib.dll` |

These commands accept the same inspection flags (`--audit`, `--sourcelink`, `--api`, etc.) because they all ultimately inspect libraries.

### Inspection Flags

Once you've specified a source, flags control what information to show:

| Flag | Shows |
|------|-------|
| `--library` | Library metadata (version, TFM, architecture) |
| `--sourcelink` | SourceLink presence and URL (fast, no verification) |
| `--audit` | Full provenance verification (SourceLink reachability, determinism) |
| `--api` | Public API surface |

### The `audit` Command

`audit` is an opinionated command for provenance verification. It:

- Auto-detects input type (package name, file path, nupkg, directory)
- Always runs strict verification (no flags needed)
- Uses verbosity as the sole control mechanism

```bash
dotnet inspect audit Markout@0.1.4           # package
dotnet inspect audit ./bin/MyLib.dll         # file
dotnet inspect audit ./artifacts/*.nupkg     # nupkg files
```

| Verbosity | Output |
|-----------|--------|
| `-v:q` | One-line pass/fail for each check |
| `-v:n` | Audit table with source coverage summary |
| `-v:d` | Full details including missing source files |

`audit` returns non-zero exit code if any check fails, making it suitable for CI gates.

## Command Patterns

### Package-centric workflow

Start with a package, drill down into details:

```bash
dotnet inspect package Newtonsoft.Json           # metadata
dotnet inspect package Newtonsoft.Json --library # library info
dotnet inspect package Newtonsoft.Json --audit    # provenance check
dotnet inspect package Newtonsoft.Json --api      # public API
```

### Platform-centric workflow

Inspect libraries from the local .NET SDK/runtime:

```bash
dotnet inspect platform                          # list frameworks
dotnet inspect platform System.Text.Json         # inspect library
dotnet inspect platform System.Text.Json --audit # provenance check
```

### File-centric workflow

Inspect local library files:

```bash
dotnet inspect library ./bin/MyLib.dll          # basic info
dotnet inspect library ./bin/MyLib.dll --audit  # provenance check
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

# These are equivalent  
dotnet inspect audit Markout@0.1.4
dotnet inspect package Markout@0.1.4 --audit
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

| Deprecated | Use Instead | Removal Target |
|------------|-------------|----------------|
| `library --package X` | `package X --library` | 0.3.0 |
| `--strict` | `--audit` (now always strict) | 0.3.0 |
