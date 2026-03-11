---
name: dotnet-inspect
version: 0.6.0
description: Query .NET APIs across NuGet packages, platform libraries, and local files. Search for types, list API surfaces, compare and diff versions, find extension methods and implementors. Use whenever you need to answer questions about .NET library contents.
---

# dotnet-inspect

Query .NET library APIs — the same commands work across NuGet packages, platform libraries (System.*, Microsoft.AspNetCore.*), and local .dll/.nupkg files.

## Quick Decision Tree

- **Code broken?** → `diff --package Foo@old..new` first, then `member`
- **Need API surface?** → `member Type --package Foo` (compact table by default)
- **Need type shape?** → `type Type --package Foo` (tree view by default for single type)
- **Need signatures?** → `member Type --package Foo -m Method` (default shows full signatures + docs)
- **Need source/IL?** → `member Type --package Foo -m Method:1 -v:d` (Source, Lowered C#, IL)
- **Need constructors?** → `member 'Type<T>' --package Foo -m .ctor` (use `<T>` not `<>`)
- **Need all overloads?** → `member Type --package Foo --show-index` (shows `Name:N` indices)
- **Need package dependencies?** → `depends --package Foo`
- **Need type hierarchy?** → `depends 'INumber<TSelf>'`
- **Need specific fields?** → `-S Section --fields "PDB*"` (structured query, no DSL)

## When to Use This Skill

- **"What types are in this package?"** — `type` discovers types, `find` searches by pattern
- **"What's the API surface?"** — `type` for discovery, `member` for detailed inspection (docs on)
- **"What changed between versions?"** — `diff` classifies breaking/additive changes
- **"This code uses an old API — fix it"** — `diff` the old..new version, then `member` to see the new API
- **"What extends this type?"** — `extensions` finds extension methods/properties (`--reachable` for transitive)
- **"What implements this interface?"** — `implements` finds concrete types
- **"What does this type depend on?"** — `depends` walks type hierarchy, package deps, or library refs
- **"What version/metadata does this have?"** — `package` and `library` inspect metadata
- **"What TFMs are available?"** — `package Foo --tfms`, then `type --package Foo --tfm net8.0`
- **"Show me something cool"** — `demo` runs curated showcase queries

## Key Patterns

Default output is compact columnar tables (like `docker images` or `git log --oneline`). No flags needed for scanning:

```bash
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json    # scan members
dnx dotnet-inspect -y -- type --package System.Text.Json                     # scan types
dnx dotnet-inspect -y -- diff --package System.CommandLine@2.0.0-beta4.22272.1..2.0.3  # triage changes
```

Four formatters: **plaintext** (default), **markdown** (`-v` or `--markdown`), **oneline** (`--oneline`), **json** (`--json`). Verbosity (`-v:q/m/n/d`) controls which sections are included; formatter controls how they render. They compose freely — except `--oneline` and `-v` cannot be combined.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -v:m  # markdown with docs
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -v:d  # detailed (source/IL)
dnx dotnet-inspect -y -- System.Text.Json -v:n --plaintext                      # all local sections, plaintext
```

Use `diff` first when fixing broken code — triage changes, then drill into specifics:

```bash
dnx dotnet-inspect -y -- diff --package System.CommandLine@2.0.0-beta4.22272.1..2.0.3  # what changed?
dnx dotnet-inspect -y -- member Command --package System.CommandLine@2.0.3               # new API surface
```

## Structured Queries (like Go templates, without a DSL)

Discover the schema, then select and project — no template language needed:

```bash
dnx dotnet-inspect -y -- System.Text.Json -D                          # list sections
dnx dotnet-inspect -y -- System.Text.Json -D --effective              # sections with data (dry run)
dnx dotnet-inspect -y -- library System.Text.Json -D --tree           # full schema tree
dnx dotnet-inspect -y -- System.Text.Json -S Symbols                  # render one section
dnx dotnet-inspect -y -- System.Text.Json -S Symbols --fields "PDB*"  # project specific fields
dnx dotnet-inspect -y -- type System.Text.Json --columns Kind,Type    # project specific columns
```

## Search Scope

Search commands (`find`, `extensions`, `implements`, `depends`) use scope flags:

- **(no flags)** — all platform frameworks (runtime, aspnetcore, netstandard)
- **`--platform`** — all platform frameworks
- **`--extensions`** — curated Microsoft.Extensions.* packages
- **`--aspnetcore`** — curated Microsoft.AspNetCore.* packages
- **`--package Foo`** — specific NuGet package (combinable with scope flags)

`type`, `member`, `library`, `diff` accept `--platform <name>` as a string for a specific platform library.

## Command Reference

| Command | Purpose |
| ------- | ------- |
| `type` | **Discover types** — terse output, no docs, use `--shape` for hierarchy |
| `member` | **Inspect members** — docs on by default, supports dotted syntax (`-m Type.Member`) |
| `find` | Search for types by glob or fuzzy match across any scope |
| `diff` | Compare API surfaces between versions — breaking/additive classification |
| `extensions` | Find extension methods/properties for a type (`--reachable` for transitive) |
| `implements` | Find types implementing an interface or extending a base class |
| `depends` | Walk dependency graphs upward — type hierarchy, package deps, or library refs |
| `package` | Package metadata, files, versions, dependencies, `search` for NuGet discovery |
| `library` | Library metadata, symbols, references, SourceLink audit |
| `demo` | Run curated showcase queries — list, invoke, or feeling-lucky |

## Filtering and Limiting

```bash
dnx dotnet-inspect -y -- type System.Text.Json -k enum               # filter by kind (type and member commands)
dnx dotnet-inspect -y -- type System.Text.Json -t "*Converter*"      # glob filter on type names
dnx dotnet-inspect -y -- member System.Text.Json JsonDocument -m Parse  # filter by member name
dnx dotnet-inspect -y -- type System.Text.Json -5                    # first 5 lines (like head -5)
```

**Do not pipe output through `head`, `tail`, or `Select-Object`.** Use built-in limiting:

- **`-n N` or `-N`** — line limit (like `head`). Keeps headers, truncates cleanly.
- **`-m N`** (numeric) — item limit (members per kind section).
- **`-k Kind`** — filter by kind: `class/struct/interface/enum/delegate` (type) or `method/property/field/event/constructor` (type single-type view, member).
- **`-S Section`** — show only a specific section (glob-capable).

## Key Syntax

- **Generic types** need quotes: `'Option<T>'`, `'IEnumerable<T>'`
- **Use `<T>` not `<>`** for generic types — `"Option<>"` resolves to the abstract base, `'Option<T>'` resolves to the concrete generic with constructors
- **`type` uses `-t`** for type filtering, **`member` uses `-m`** for member filtering (not `--filter`)
- **Dotted syntax** for `member`: `-m JsonSerializer.Deserialize` or `-m System.Text.Json.JsonSerializer.Deserialize`
- **Diff ranges** use `..`: `--package System.Text.Json@9.0.0..10.0.0`
- **Derived types** only show their own members — query the base type too

## Installation

Use `dnx` (like `npx`). Always use `-y` and `--` to prevent interactive prompts:

```bash
dnx dotnet-inspect -y -- <command>
```

## Full Documentation

For the full mental model, structured queries, and migration workflow:

```bash
dnx dotnet-inspect -y -- llmstxt
```
