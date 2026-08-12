---
id: verbosity-and-tips
description: Verbosity levels, section selection, row counts, and contextual tips behavior
commands: [-v, -D, -S, --count]
areas: [output, verbosity, sections, count, tips]
---

# Verbosity and Tips

> The tool has four verbosity levels, section discovery and selection, row
> counts, and a tips system. Verbosity controls how much content is shown. Tips
> are contextual suggestions written to `stderr` that guide the next step.

Verbosity levels:

| Level | Flag | Content | Tips |
| ----- | ---- | ------- | ---- |
| Quiet | `-v:q` | Compact identity and context | No |
| Minimal (default) | `-v:m` or none | One high-value base section | Yes |
| Normal | `-v:n` | Multiple network-free base sections | No |
| Detailed | `-v:d` | All applicable base sections | No |

The `member` command follows the same scale for member lists. A selected overload defaults to `Signature`; normal verbosity adds bounded local implementation sections: `Decompiled Source` (raised C# without IL comments) and `IL` (raw IL). `Source Locations` is an explicit SourceLink file/line URL table that does not fetch source bodies. `Annotated Source` is the mixed C#+IL view with hidden-fact comments; `Original Source` is SourceLink-backed source when available. `-S @Source` selects Decompiled, Annotated, Original, and IL evidence. The `Facts` section — the structured member/offset/line-keyed table of the same Research overlay facts — is opt-in via `-S "Facts"` / `--tsv`.

Discovery (`-D`) lists available sections and category doors. Section
selection (`-S`, with lowercase `-s` as an alias) filters to specific sections;
bare `-S` requests the compact network-free overview. Tips are suppressed when
sections are selected. `--count` with exactly one selected section returns a
single integer count, while `--bare` is a presentation-only modifier that
strips framing from an already-selected payload.

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
dotnet-inspect System.CommandLine@2.0.3 -v:q
```

## 1. Default verbosity (package)

> Goal: Default shows H1, description, the primary information section, and tips on stderr.

```prompt
Tell me about System.CommandLine. What are the key metrics?
```

```bash
dotnet-inspect System.CommandLine@2.0.3
```

```expect
# System.CommandLine
## Package Info
| Field | Value |
| Version | 2.0.3 |
```

```query
grep '^# '
grep '| Version |'
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

> Goal: Quiet shows only H1 and compact fields. No sections, no tips.

```prompt
Give me a quick summary of System.CommandLine with minimal output.
```

```bash
dotnet-inspect System.CommandLine@2.0.3 -v:q
```

```expect
# System.CommandLine
Version: 2.0.3
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

## 5. Normal verbosity (package)

> Goal: Normal shows multiple network-free base sections. No tips.

```prompt
Show the standard network-free details for System.CommandLine.
```

```bash
dotnet-inspect System.CommandLine@2.0.3 -v:n
```

```expect
## Package Info
## Dependencies
## Target Frameworks
```

```expect-not
## Statistics
Tips:
```

```query
grep '^## '
```

## 6. Detailed verbosity (package)

> Goal: Detailed shows all sections. No tips.

```prompt
Show me everything about System.CommandLine — all sections.
```

```bash
dotnet-inspect System.CommandLine@2.0.3 -v:d
```

```expect
## Package Info
## Statistics
## Dependencies
```

```expect-not
Tips:
```

```query
grep '^## '
```

## 7. Detailed verbosity (platform library)

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

## 8. Discover available sections

> Goal: `-D` lists target-aware section and category names. No tips.

### 8a. Package

```prompt
What sections are available for System.CommandLine?
```

```bash
dotnet-inspect System.CommandLine@2.0.3 -D
```

```expect
| Package Info | section |
| Dependencies | section |
```

```expect-not
Tips:
```

```query
grep 'Package'
grep 'Dependencies'
```

### 8b. Platform library

```prompt
What sections are available for System.Text.Json?
```

```bash
dotnet-inspect System.Text.Json -D
```

```expect
| Library Info | section |
| Extension Methods | section |
```

```expect-not
Tips:
```

```query
grep 'Library Info'
grep 'Extension Methods'
```

## 9. Select a specific section

> Goal: `-S [name]` shows only that section. No tips.

### 9a. Package section

```prompt
Show me just the Package section for System.CommandLine.
```

```bash
dotnet-inspect System.CommandLine@2.0.3 -S "Package Info"
```

```expect
## Package Info
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

### 9b. Platform library section

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

## 10. Count a specific section

> Goal: `--count` returns one integer for a single selected section. No tips.

```prompt
How many async methods are in System.Text.Json?
```

```bash
dotnet-inspect System.Text.Json -S "Async*" --count
```

```expect-not
#
|
Tips:
```

```query
awk '/^[0-9]+$/ && $1 > 0 { print "positive" }'
```

```expect
positive
```
