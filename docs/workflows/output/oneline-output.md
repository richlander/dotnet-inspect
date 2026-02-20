---
id: oneline-output
description: Compact one-line-per-item output for scripting and agents
commands: [--oneline]
areas: [output, agents, scripting]
---

# Oneline Output

> The `--oneline` flag produces compact, machine-friendly output with one item per line. The format is a mix of `docker images` (columnar with header) and `git log --oneline` (one item per line). Combined with `--no-header`, it's ideal for piping to other tools or parsing by agents.

## Preconditions

Isolated session. This workflow uses only platform libraries (no cache priming needed).

```bash
export DOTNET_INSPECT_ISOLATED=oneline-output
```

## 1. Member listing with oneline

> Goal: Show members in compact tabular format, one per line.

### 1a. Using `--oneline`

```prompt
List JsonSerializer members in a compact one-per-line format.
```

```bash
dotnet-inspect type System.Text.Json JsonSerializer --oneline -m 3 --no-header
```

```expect
property
method
```

```expect-not
Tips:
```

```query
head -3
```

### 1b. With header

```bash
dotnet-inspect type System.Text.Json JsonSerializer --oneline -m 3
```

```expect
KIND
property
method
```

```expect-not
Tips:
```

```query
head -4
```

## 2. Type listing with oneline

> Goal: Show types in compact tabular format with kind, name, and member count.

### 2a. Full listing

```bash
dotnet-inspect type System.Text.Json --oneline
```

```expect
KIND       TYPE                                                                 MEMBERS
class      System.Text.Json.JsonSerializer
struct     System.Text.Json.JsonElement
enum       System.Text.Json.JsonValueKind
```

```expect-not
Tips:
```

```query
head -5
wc -l
```

### 2b. Limited results

```bash
dotnet-inspect type System.Text.Json --oneline -t 3 --no-header
```

```expect
class
```

```expect-not
Tips:
KIND
```

```query
wc -l
```

## 3. Oneline output for grep

> Goal: Pipe oneline output to grep for filtering.

### 3a. Filter methods only

```bash
dotnet-inspect type System.Text.Json JsonSerializer --oneline --no-header | grep '^method'
```

```expect
method
```

```expect-not
property
```

```query
wc -l
```

## 4. Oneline output for awk

> Goal: Extract specific columns with awk for further processing.

### 4a. Extract type names only

```bash
dotnet-inspect type System.Text.Json --oneline --no-header | awk '{print $2}' | head -5
```

```expect
System.Runtime.InteropServices.JsonMarshal
System.Text.Json.JsonDocument
```

```expect-not
class
MEMBERS
```

```query
head -3
```

### 4b. Sum member counts

```bash
dotnet-inspect type System.Text.Json --oneline --no-header | awk '{sum += $3} END {print "Total members:", sum}'
```

```expect
Total members:
```

```query
grep -oE '[0-9]+'
```

### 4c. Filter by member count threshold

```bash
dotnet-inspect type System.Text.Json --oneline --no-header | awk '$3 > 50 {print $2, $3}'
```

```expect
System.Text.Json.JsonElement
System.Text.Json.JsonSerializer
System.Text.Json.Utf8JsonWriter
```

```query
wc -l
```

### 4d. Sort structs by member count

```bash
dotnet-inspect type System.Text.Json --oneline --no-header | awk '$1 == "struct" {print $3, $2}' | sort -rn | head -5
```

```expect
57 System.Text.Json.JsonElement
```

```expect-not
class
enum
```

```query
head -3
```
