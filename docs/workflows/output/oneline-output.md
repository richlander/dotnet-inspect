---
id: table-output
description: Compact table and TSV output for scripting and agents
commands: [--table, --tsv]
areas: [output, agents, scripting]
---

# Table and TSV Output

> `--table` produces compact pretty-printed rows for humans. `--tsv` uses the same normalized tabular projection with tab-separated fields for agents and shell tools. Combine either with `--no-headers` to suppress column headers.

## Preconditions

Isolated session. This workflow uses only platform libraries (no cache priming needed).

```bash
export DOTNET_INSPECT_ISOLATED=table-output
```

## 1. Member listing with table output

> Goal: Show members in compact tabular format, one per line.

### 1a. Using `--table`

```prompt
List JsonSerializer members in a compact one-per-line format.
```

```bash
dotnet-inspect type System.Text.Json JsonSerializer --table -m 3 --no-headers
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
dotnet-inspect type System.Text.Json JsonSerializer --table -m 3
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

## 2. Type listing with table output

> Goal: Show types in compact tabular format with kind, name, and member count.

### 2a. Full listing

```bash
dotnet-inspect type System.Text.Json --table
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
dotnet-inspect type System.Text.Json --table -t 3 --no-headers
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

## 3. Table output for grep

> Goal: Pipe table output to grep for filtering.

### 3a. Filter methods only

```bash
dotnet-inspect type System.Text.Json JsonSerializer --table --no-headers | grep '^method'
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

## 4. TSV output for awk

> Goal: Extract specific columns with awk using stable tab delimiters.

### 4a. Extract type names only

```bash
dotnet-inspect type System.Text.Json --tsv --no-headers | awk -F '\t' '{print $2}' | head -5
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
dotnet-inspect type System.Text.Json --tsv --no-headers | awk -F '\t' '{sum += $3} END {print "Total members:", sum}'
```

```expect
Total members:
```

```query
grep -oE '[0-9]+'
```

### 4c. Filter by member count threshold

```bash
dotnet-inspect type System.Text.Json --tsv --no-headers | awk -F '\t' '$3 > 50 {print $2, $3}'
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
dotnet-inspect type System.Text.Json --tsv --no-headers | awk -F '\t' '$1 == "struct" {print $3, $2}' | sort -rn | head -5
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
