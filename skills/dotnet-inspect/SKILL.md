---
name: dotnet-inspect
description: Inspect .NET libraries and NuGet packages. Use when you need to understand package contents, view public API surfaces, compare APIs between versions, or audit libraries for SourceLink/determinism. Essential for .NET development tasks involving package exploration or API discovery.
---

# dotnet-inspect

A CLI tool for inspecting .NET libraries and NuGet packages.

## Requirements

- .NET 10+ SDK

## Installation

Use `dnx` to run without global installation (like `npx` for Node):

```bash
dnx dotnet-inspect -y -- <command>
```

**Important**:

- Always use `-y` to skip the interactive confirmation prompt (which breaks LLM tool use). New package versions also trigger this prompt.
- Always use `--` to separate dnx options from tool arguments. Without it, `--help` shows dnx help, not dotnet-inspect help.

## Quick Patterns

Start with these common workflows:

```bash
# Understand a type's API shape (start here - most useful for learning APIs)
dnx dotnet-inspect -y -- api JsonSerializer --package System.Text.Json --shape

# Compare API changes between versions (essential for migrations)
dnx dotnet-inspect -y -- diff --package System.CommandLine@2.0.0-beta4.22272.1..2.0.2
dnx dotnet-inspect -y -- diff Command --package System.CommandLine@2.0.0-beta4.22272.1..2.0.2
dnx dotnet-inspect -y -- diff "*Json*" --package System.Text.Json@9.0.0..10.0.0

# Search for types by pattern (single or batch with comma-separated patterns)
dnx dotnet-inspect -y -- find "*Handler*" --package System.CommandLine
dnx dotnet-inspect -y -- find "Option*,Argument*,Command*" --package System.CommandLine --terse

# Find extension methods for a type
dnx dotnet-inspect -y -- extensions HttpClient --framework runtime
dnx dotnet-inspect -y -- extensions HttpClient --framework runtime --reachable

# Find types implementing an interface or extending a class
dnx dotnet-inspect -y -- implements IDisposable --framework runtime
dnx dotnet-inspect -y -- implements Stream --framework runtime

# Package metadata and versions
dnx dotnet-inspect -y -- package System.Text.Json
dnx dotnet-inspect -y -- package System.Text.Json --versions

# Get XML documentation for a type
dnx dotnet-inspect -y -- api Option --package System.CommandLine --docs

# Drill into a specific method — get source, decompiled C#, and IL
dnx dotnet-inspect -y -- api --package Microsoft.Extensions.Options OptionsFactory --select  # See Select column
dnx dotnet-inspect -y -- api --package Microsoft.Extensions.Options OptionsFactory -m Create --index 1  # Member doc
```

## Key Flags

| Flag | Purpose | Commands |
| ---- | ------- | -------- |
| `-v:d` | Detailed output (full signatures, more info) | all commands |
| `--docs` | Include XML documentation from source | `api` |
| `-m Name` | Filter to specific member(s) | `api` |
| `--select` | Show Select column for member addressing | `api` |
| `--index N` | Target Nth overload for decompiled member doc | `api` |
| `-t Name` | Filter to specific type(s), supports globs | `diff` |
| `-n 10` | Limit results | `find`, `extensions`, `package --versions` |
| `--terse` | Compact output (alias for --oneline --grouped) | `find` |
| `--oneline` | Space-separated type names on one line | `find` |
| `--name-only` | Show only names, one per line | `find`, `diff` |
| `--stat` | Show only statistics (no member details) | `diff` |
| `--breaking` | Show only breaking changes | `diff` |
| `--additive` | Show only additive changes | `diff` |
| `--grouped` | Group results by pattern (with --oneline) | `find` |
| `--reachable` | Include extensions on reachable types | `extensions` |
| `--prerelease` | Include prerelease versions | `package --versions` |

**Signatures include `params` and default values** — you can determine calling conventions directly from output:
`void .ctor(string name, params string[] aliases)`, `Parse(args, configuration = null)`

**Generic types:** Use quotes around generic types: `'Option<T>'`, `'IEnumerable<T>'`

**Diff supports globs and whole-package comparison:** Omit the type name to see all changes across a package version range. Use glob patterns like `"*Writer*"` to filter.

## Command Reference

| Command | Purpose |
| ------- | ------- |
| `api <type>` | **Start here.** Public API surface (table format, or `--shape` for hierarchy) |
| `diff` | Compare API surfaces between package versions (whole-package or filtered) |
| `find <pattern>` | Search for types across packages, libraries, or frameworks |
| `extensions <type>` | Find extension methods for a type |
| `implements <type>` | Find types implementing an interface or extending a class |
| `package <name>` | Package metadata, files, versions, dependencies |
| `library <path>` | Library info, SourceLink/determinism audit |
| `llmstxt` | Complete usage examples for all commands |

## Full Documentation

For comprehensive examples and edge cases:

```bash
dnx dotnet-inspect -y -- llmstxt
```

## When to Use This Skill

- Exploring what types/APIs a NuGet package provides
- Searching for types by pattern across packages or frameworks
- Finding types that implement an interface or extend a base class
- Understanding method signatures and overloads
- Comparing API changes between package versions
- Auditing libraries for SourceLink and determinism
- Finding types matching a pattern (`--filter "Progress*"`)
- Getting documentation from source (`--docs`)
- Viewing decompiled source, IL, and annotated IL for methods
