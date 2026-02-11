---
name: dotnet-inspect
description: Inspect .NET libraries and NuGet packages. Use when you need to understand package contents, view public API surfaces, compare APIs between versions, or audit libraries for SourceLink/determinism. Essential for .NET development tasks involving package exploration or API discovery.
---

# dotnet-inspect

A CLI tool for inspecting .NET libraries and NuGet packages. It operates on platform libraries (e.g., `System.Collections`), NuGet packages (e.g., `Microsoft.Extensions.AI`), and local files.

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
dnx dotnet-inspect -y -- api 'HashSet<T>' --platform System.Collections --shape

# Bare names — routes automatically (platform for System.*/Microsoft.*, NuGet for others)
dnx dotnet-inspect -y -- Microsoft.Extensions.AI

# Compare API changes between versions (essential for migrations)
dnx dotnet-inspect -y -- diff System.CommandLine@2.0.0-beta4.22272.1..2.0.3
dnx dotnet-inspect -y -- diff "*Json*" --package System.Text.Json@9.0.0..10.0.0

# Search for types by pattern (single or batch with comma-separated patterns)
dnx dotnet-inspect -y -- find "*Handler*" --package System.CommandLine
dnx dotnet-inspect -y -- find "Chat*,Diction*" --dotnet --terse

# Find extension methods for a type (detects C# 14 extension properties too)
dnx dotnet-inspect -y -- extensions HttpClient --framework runtime --reachable
dnx dotnet-inspect -y -- extensions DbContext --dotnet

# Find types implementing an interface or extending a class
dnx dotnet-inspect -y -- implements Stream --dotnet

# Package metadata and versions
dnx dotnet-inspect -y -- package System.Text.Json -v:d
dnx dotnet-inspect -y -- package System.Text.Json --versions

# Library metadata, SourceLink audit, dependency tree
dnx dotnet-inspect -y -- library --package System.Text.Json --source-link-audit
dnx dotnet-inspect -y -- library Microsoft.Extensions.AI.OpenAI --dependencies

# Code samples
dnx dotnet-inspect -y -- samples Markout MarkoutWriter --list

# Get XML documentation for a type
dnx dotnet-inspect -y -- api Option --package System.CommandLine --docs
```

## Key Flags

| Flag | Purpose | Commands |
| ---- | ------- | -------- |
| `-v:d` | Detailed output (full signatures, all sections) | all |
| `--shape` | Type shape diagram (hierarchy + members) | `api` |
| `--docs` | Include XML documentation | `api` |
| `-m Name` | Filter to specific member(s), supports globs | `api` |
| `--select` | Show Select column for member addressing | `api` |
| `--index N` | Target Nth overload for decompiled member doc | `api` |
| `-n 10` | Limit results | `find`, `extensions`, `package --versions` |
| `--dotnet` | runtime + aspnetcore + curated Microsoft packages | `find`, `extensions`, `implements` |
| `--terse` | Compact output (alias for --oneline --grouped) | `find` |
| `--reachable` | Include extensions on reachable types | `extensions` |
| `--dependencies` | Dependency tree (visual) | `library`, `package` |
| `--source-link-audit` | SourceLink/determinism audit | `library` |
| `--stat` | Statistics only (no member details) | `diff` |
| `--breaking` | Breaking changes only | `diff` |
| `--prerelease` | Include prerelease versions | `package --versions` |
| `--json` | JSON output | all |
| `-s Name` | Include section (glob-capable) | all |

**Signatures include `params` and default values** — you can determine calling conventions directly from output.

**Generic types:** Use quotes: `'Option<T>'`, `'IEnumerable<T>'`

## Command Reference

| Command | Purpose |
| ------- | ------- |
| `api <type>` | **Start here.** Public API surface (table format, or `--shape` for hierarchy) |
| `diff` | Compare API surfaces between package versions |
| `find <pattern>` | Search for types across packages, libraries, or frameworks |
| `extensions <type>` | Find extension methods/properties for a type |
| `implements <type>` | Find types implementing an interface or extending a class |
| `package <name>` | Package metadata, files, versions, dependencies |
| `library <name>` | Library info, symbols, dependencies, SourceLink audit |
| `samples <pkg> <type>` | Fetch and display code samples |
| `platform` | List installed frameworks |
| `cli` | CLI args explorer |
| `llmstxt` | Complete usage examples for all commands |

## Full Documentation

For comprehensive examples and edge cases:

```bash
dnx dotnet-inspect -y -- llmstxt
```

## When to Use This Skill

- Exploring what types/APIs a NuGet package provides
- Understanding method signatures, overloads, and type shape
- Searching for types by pattern across packages or frameworks
- Finding types that implement an interface or extend a base class
- Comparing API changes between package versions
- Viewing library dependency trees
- Auditing libraries for SourceLink and determinism
- Getting documentation from source (`--docs`)
- Viewing decompiled source, IL, and annotated IL for methods
