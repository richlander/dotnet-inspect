---
name: dotnet-inspect
description: Query .NET APIs across NuGet packages, platform libraries, and local files. Search for types, list API surfaces, compare versions, find extension methods and implementors. Use whenever you need to answer questions about .NET library contents.
---

# dotnet-inspect

Query .NET library APIs — the same commands work across NuGet packages, platform libraries (System.*, Microsoft.AspNetCore.*), and local .dll/.nupkg files.

## When to Use This Skill

- **"What types are in this package?"** — `find` searches by glob pattern
- **"What's the API surface?"** — `api` lists types, members, signatures, type shape
- **"What changed between versions?"** — `diff` classifies breaking/additive changes
- **"What extends this type?"** — `extensions` finds extension methods/properties
- **"What implements this interface?"** — `implements` finds concrete types
- **"What version/metadata does this have?"** — `package` and `library` inspect metadata

## Search Scope

Search commands (`find`, `extensions`, `implements`) work across all of .NET:

```bash
dnx dotnet-inspect -y -- find "Chat*"                    # default scope (platform + curated)
dnx dotnet-inspect -y -- find "Chat*" --platform         # platform frameworks only
dnx dotnet-inspect -y -- find "Chat*" --extensions       # Microsoft.Extensions.* packages
dnx dotnet-inspect -y -- find "Chat*" --aspnetcore       # Microsoft.AspNetCore.* packages
dnx dotnet-inspect -y -- find "Chat*" --platform --extensions  # combine scopes
dnx dotnet-inspect -y -- find "Chat*" --package Foo      # specific NuGet package
dnx dotnet-inspect -y -- find "Chat*" --platform --package Foo  # platform + a specific package
```

Scope flags are combinable — use multiple flags to widen the search. `--package` works on all commands. `api`, `library`, `diff` also accept `--platform <name>` for a specific platform library.

## Examples by Task

### List API surface

```bash
dnx dotnet-inspect -y -- api System.Text.Json                              # all types in library
dnx dotnet-inspect -y -- api System.Text.Json JsonSerializer               # members of a type
dnx dotnet-inspect -y -- api 'HashSet<T>' --platform System.Collections --shape  # type shape diagram
dnx dotnet-inspect -y -- api JsonSerializer --package System.Text.Json -m Serialize  # filter to member
```

### Search for types

```bash
dnx dotnet-inspect -y -- find "*Handler*" --package System.CommandLine
dnx dotnet-inspect -y -- find "Option*,Argument*,Command*" --package System.CommandLine --terse
dnx dotnet-inspect -y -- find "*Logger*"
```

### Compare versions (migrations)

```bash
dnx dotnet-inspect -y -- diff --package System.CommandLine@2.0.0-beta4.22272.1..2.0.2
dnx dotnet-inspect -y -- diff --package System.Text.Json@9.0.0..10.0.0 --breaking
dnx dotnet-inspect -y -- diff JsonSerializer --package System.Text.Json@9.0.0..10.0.0
```

### Find extensions and implementors

```bash
dnx dotnet-inspect -y -- extensions HttpClient                   # what extends HttpClient?
dnx dotnet-inspect -y -- extensions IServiceCollection           # across default scope
dnx dotnet-inspect -y -- implements Stream                       # what extends Stream?
dnx dotnet-inspect -y -- implements IDisposable --platform       # across all platform frameworks
```

### Inspect packages and libraries

```bash
dnx dotnet-inspect -y -- package System.Text.Json                # metadata, latest version
dnx dotnet-inspect -y -- package System.Text.Json --versions     # available versions
dnx dotnet-inspect -y -- library System.Text.Json                # library metadata, symbols
dnx dotnet-inspect -y -- library ./bin/MyLib.dll                 # local file
```

## Command Reference

| Command | Purpose |
| ------- | ------- |
| `api` | Public API surface — types, members, signatures, `--shape` for hierarchy |
| `find` | Search for types by glob pattern across any scope |
| `diff` | Compare API surfaces between versions — breaking/additive classification |
| `extensions` | Find extension methods/properties for a type |
| `implements` | Find types implementing an interface or extending a base class |
| `package` | Package metadata, files, versions, dependencies |
| `library` | Library metadata, symbols, references, dependencies |

## Key Syntax

- **Generic types** need quotes: `'Option<T>'`, `'IEnumerable<T>'`
- **Positional args** for `api`: `api <source> <type> <member>` (not flags)
- **Diff ranges** use `..`: `--package System.Text.Json@9.0.0..10.0.0`
- **Signatures** include `params` and default values from metadata

## Installation

Use `dnx` (like `npx`). Always use `-y` and `--` to prevent interactive prompts:

```bash
dnx dotnet-inspect -y -- <command>
```

## Full Documentation

For comprehensive syntax, edge cases, and the flag compatibility matrix:

```bash
dnx dotnet-inspect -y -- llmstxt
```
