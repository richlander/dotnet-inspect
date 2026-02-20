---
id: verbosity-and-tips
description: Verbosity levels and contextual tips behavior
commands: [-v, -s]
areas: [output, verbosity, tips]
---

# Verbosity and Tips

> The tool has three verbosity levels and a tips system. Verbosity controls how much content is shown. Tips are contextual suggestions written to `stderr` that guide the next step.

Verbosity levels:

| Level | Flag | Content | Tips |
| ----- | ---- | ------- | ---- |
| Quiet | `-v:q` | H1 + oneline field list | No |
| Minimal (default) | (none) | H1, description, oneline field list, one section table | Yes |
| Detailed | `-v:d` | H1, description, oneline field list, all sections | No |

Section selection (`-s`) lists available sections or filters to specific ones. Tips are suppressed when sections are selected.

## Preconditions

Isolated session with cached packages. Offline mode ensures no unexpected network dependencies.

```bash
export DOTNET_INSPECT_ISOLATED=verbosity-tips
export DOTNET_INSPECT_OFFLINE=1
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
DOTNET_INSPECT_OFFLINE=0 dotnet-inspect System.CommandLine -v:q
```

## 1. Default verbosity (package)

> Goal: Default shows H1, description, oneline fields, one section table, and tips on stderr.

```bash
dotnet-inspect System.CommandLine
```

```expect
# System.CommandLine
Version: 2.0.3
## Package
| Property | Value |
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

> Goal: Platform library default shows H1, oneline fields, Library Info section table, and no tips.

```bash
dotnet-inspect System.Text.Json
```

```expect
# System.Text.Json.dll
Source: Platform
## Library Info
| Property | Value |
```

```query
grep '^# '
grep -o 'Source: [A-Za-z]*'
grep '^## '
```

## 3. Quiet verbosity (package)

> Goal: Quiet shows only H1 and oneline field list. No sections, no tips.

```bash
dotnet-inspect System.CommandLine -v:q
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

> Goal: `-s` with no argument lists section names. No tips.

### 7a. Package

```bash
dotnet-inspect System.CommandLine -s
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

```bash
dotnet-inspect System.Text.Json -s
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

> Goal: `-s [name]` shows only that section. No tips.

### 8a. Package section

```bash
dotnet-inspect System.CommandLine -s Package
```

```expect
## Package
| Property | Value |
```

```expect-not
## Statistics
Tips:
```

```query
grep '^## '
grep '| Property'
```

### 8b. Platform library section

```bash
dotnet-inspect System.Text.Json -s "Extension Methods"
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
