---
name: dotnet-inspect
description: Inspect .NET assemblies and NuGet packages. Use when you need to understand package contents, view public API surfaces, compare APIs between versions, or audit assemblies for SourceLink/determinism. Essential for .NET development tasks involving package exploration or API discovery.
---

# dotnet-inspect

A CLI tool for inspecting .NET assemblies and NuGet packages.

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

## Getting Started

Run this command for complete usage instructions:

```bash
dnx dotnet-inspect -y -- llmstxt
```

**DO THIS FIRST.** The `llmstxt` command provides comprehensive examples for all commands and workflows.

## Quick Reference

| Command | Purpose |
|---------|---------|
| `package <name>` | Inspect NuGet package metadata, files, versions, dependencies |
| `assembly <path>` | Inspect .NET assembly info, SourceLink/determinism audit |
| `api <type>` | View public API surface of a type |
| `type <type>` | Show type shape with hierarchy and members (tree view) |
| `find <pattern>` | Search for types across packages, assemblies, or frameworks |
| `diff <type>` | Compare API surfaces between package versions |
| `llmstxt` | Show complete usage examples |

## Example Usage

```bash
# Package exploration
dnx dotnet-inspect -y -- package System.Text.Json
dnx dotnet-inspect -y -- package System.CommandLine --files
dnx dotnet-inspect -y -- package System.Text.Json --versions

# View type APIs
dnx dotnet-inspect -y -- api JsonSerializer --package System.Text.Json
dnx dotnet-inspect -y -- api Command --package System.CommandLine -m SetAction

# Compare versions
dnx dotnet-inspect -y -- diff JsonSerializer --package System.Text.Json@9.0.0..10.0.0

# Type hierarchy
dnx dotnet-inspect -y -- type Command --package System.CommandLine

# Search for types
dnx dotnet-inspect -y -- find "*Logger*" --framework runtime
dnx dotnet-inspect -y -- find JsonSerializer --package System.Text.Json
```

## When to Use This Skill

- Exploring what types/APIs a NuGet package provides
- Searching for types by pattern across packages or frameworks
- Understanding method signatures and overloads
- Comparing API changes between package versions
- Auditing assemblies for SourceLink and determinism
- Finding types matching a pattern (`--filter "Progress*"`)
- Getting documentation from source (`--docs`)
