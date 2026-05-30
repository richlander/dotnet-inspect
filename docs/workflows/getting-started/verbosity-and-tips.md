---
id: verbosity-and-tips
description: Verbosity levels, section selection, row counts, and contextual tips behavior
commands: [-v, -S, --count]
areas: [output, verbosity, sections, count, tips]
---

# Verbosity and Tips

> The tool has three verbosity levels, section selection, row counts, and a tips system. Verbosity controls how much content is shown. Tips are contextual suggestions written to `stderr` that guide the next step.

Verbosity levels:

| Level | Flag | Content | Tips |
| ----- | ---- | ------- | ---- |
| Quiet | `-v:q` | H1 + metadata field list | No |
| Minimal (default) | (none) | H1, description, metadata field list, one section table | Yes |
| Detailed | `-v:d` | H1, description, metadata field list, all sections | No |

The `member` command follows the same scale: quiet shows the heading-only summary, default shows full signatures with docs, detailed adds Source/Lowered C#/IL.

Section selection (`-S`, with lowercase `-s` as an alias) lists available sections or filters to specific ones. Tips are suppressed when sections are selected. `--count` with exactly one selected section returns a single integer.

## Preconditions

Isolated session with cached packages.

```bash
export DOTNET_INSPECT_ISOLATED=verbosity-tips
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
dotnet-inspect System.CommandLine -v:q
```

## 1. Default verbosity (package)

> Goal: Default shows H1, description, oneline fields, one section table, and tips on stderr.

```prompt
Tell me about System.CommandLine. What are the key metrics?
```

```bash
dotnet-inspect System.CommandLine
```

```expect
# System.CommandLine
Version: 2.0.8
## Package
| Field | Value |
```

```query
grep '^# '
grep -oE 'Version: [0-9.]+'
grep '^## '
```

```expect-stderr
Tips:
```

## 2. Default verbosity (platform library)

> Goal: Platform library default shows H1, metadata fields, Library Info section table, and no tips.

```prompt
Tell me about the System.Text.Json library.
```

```bash
dotnet-inspect System.Text.Json
```

```expect
# System.Text.Json.dll
## Library Info
| Field | Value |
```

```query
grep '^# '
grep '^## '
```

## 3. Quiet verbosity (package)

> Goal: Quiet shows only H1 and oneline field list. No sections, no tips.

```prompt
Give me a quick summary of System.CommandLine with minimal output.
```

```bash
dotnet-inspect System.CommandLine -v:q
```

```expect
# System.CommandLine
Version: 2.0.8
```

```expect-not
##
Tips:
```

```query
grep '^# '
grep -oE 'Version: [0-9.]+'
```

## 4. Quiet verbosity (platform library)

> Goal: Same compact format for platform libraries.

```prompt
Give me a quick summary of System.Text.Json.
```

```bash
dotnet-inspect System.Text.Json -v:q
```

```expect
# System.Text.Json.dll
Source: Platform
```

```expect-not
##
Tips:
```

```query
grep '^# '
grep -o 'Source: [A-Za-z]*'
```

## 5. Detailed verbosity (package)

> Goal: Detailed shows all sections. No tips.

```prompt
Show me everything about System.CommandLine — all sections.
```

```bash
dotnet-inspect System.CommandLine -v:d
```

```expect
## Package
## Statistics
## Package Dependencies
```

```expect-not
Tips:
```

```query
grep '^## '
```

## 6. Detailed verbosity (platform library)

> Goal: Multiple sections for platform libraries. No tips.

```prompt
Show me everything about System.Text.Json — all sections.
```

```bash
dotnet-inspect System.Text.Json -v:d
```

```expect
## Library Info
## Extension Methods
## Custom Attributes
```

```expect-not
Tips:
```

```query
grep '^## '
```

## 7. List available sections

> Goal: `-S` with no argument lists section names. No tips.

### 7a. Package

```prompt
What sections are available for System.CommandLine?
```

```bash
dotnet-inspect System.CommandLine -S
```

```expect
Package
Package Dependencies
```

```expect-not
Tips:
```

```query
grep 'Package'
grep 'Dependencies'
```

### 7b. Platform library

```prompt
What sections are available for System.Text.Json?
```

```bash
dotnet-inspect System.Text.Json -S
```

```expect
Library Info
Extension Methods
```

```expect-not
Tips:
```

```query
grep 'Library Info'
grep 'Extension Methods'
```

## 8. Select a specific section

> Goal: `-S [name]` shows only that section. No tips.

### 8a. Package section

```prompt
Show me just the Package section for System.CommandLine.
```

```bash
dotnet-inspect System.CommandLine -S Package
```

```expect
## Package
| Field | Value |
```

```expect-not
## Statistics
Tips:
```

```query
grep '^## '
grep '| Field'
```

### 8b. Platform library section

```prompt
Show me the extension methods for System.Text.Json.
```

```bash
dotnet-inspect System.Text.Json -S "Extension Methods"
```

```expect
## Extension Methods
| Name | Kind |
```

```expect-not
## Library Info
Tips:
```

```query
grep '^## '
grep '| Name'
```

## 9. Count a specific section

> Goal: `--count` returns one integer for a single selected section. No tips.

```prompt
How many async methods are in System.Text.Json?
```

```bash
dotnet-inspect System.Text.Json -S "Async*" --count
```

```query
awk '/^[0-9]+$/ && $1 > 0 { print "positive" }'
```

```expect
positive
```

```expect-not
#
|
Tips:
```
