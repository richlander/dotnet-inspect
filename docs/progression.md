# Command Progression

`dotnet-inspect` uses a consistent command model where three sources (package, platform, library) can be inspected with common flags.

## Input Sources

| Command | Source | Example |
|---------|--------|---------|
| `package X` | NuGet package | `package System.Text.Json@9.0.0` |
| `platform X` | Local SDK/runtime | `platform System.Text.Json` |
| `library ./path` | Local file | `library ./bin/MyLib.dll` |

## Common Inspection Flags

These flags work with `package`, `platform`, and `library` commands:

| Flag | Description |
|------|-------------|
| `--library` | Show library metadata (version, TFM, architecture) |
| `--sourcelink` | Show SourceLink presence and URL (fast, no HTTP) |
| `--audit` | Full provenance verification (always strict) |

## The `audit` Command

For quick provenance checks, use the opinionated `audit` command:

```bash
dotnet inspect audit Markout@0.1.4           # package
dotnet inspect audit ./bin/MyLib.dll         # file
dotnet inspect audit ./artifacts/*.nupkg     # multiple nupkgs
```

`audit` always runs strict verification. Verbosity controls output detail:

| Verbosity | Output |
|-----------|--------|
| `-v:q` | One-line pass/fail |
| `-v:n` | Audit table + source coverage |
| `-v:d` | Full details including missing sources |

## Progression Examples

### Package inspection → library details

```bash
# Start with package metadata
dotnet inspect package System.Text.Json

# Add library info
dotnet inspect package System.Text.Json --library

# Full provenance audit
dotnet inspect package System.Text.Json --audit
```

### Platform inspection

```bash
# List installed frameworks
dotnet inspect platform

# Inspect a platform library
dotnet inspect platform System.Text.Json

# Audit a platform library
dotnet inspect platform System.Text.Json --audit
```

### Quick audit for CI

```bash
# Simple pass/fail
dotnet inspect audit Markout@0.1.4 -v:q

# Audit build output
dotnet inspect audit ./artifacts/release/*.nupkg
```

## Equivalences

Some patterns are equivalent:

```bash
# These produce the same output
dotnet inspect audit Markout@0.1.4
dotnet inspect package Markout@0.1.4 --audit

# Platform shorthand
dotnet inspect platform System.Text.Json
dotnet inspect library System.Text.Json --platform
```

## Deprecations

| Deprecated | Use Instead |
|------------|-------------|
| `package X --metadata` | `library X` |
