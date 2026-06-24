---
id: line-and-result-limiting
description: Control output size with line limits, table row limits, result limits, and row counts
commands: [-n, --rows, -t, -m, --versions, --count]
areas: [output, limiting, count, agents]
---

# Line, Result, and Row Counting

> Control how much output is returned. `-n` limits output lines (like `head`). `--rows -n N` interprets the same count as table data rows per rendered table. `-t` and `-m` limit result counts for types and members. `--versions N` limits version lists. `--count` reduces a selected section/vector to one integer row count, while `--bare` stays a presentation-only modifier for already-selected payloads. These are essential for agents that need compact, predictable output.

## Preconditions

Isolated session with cached packages.

```bash
export DOTNET_INSPECT_ISOLATED=line-limiting
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
dotnet-inspect System.CommandLine -v:q
```

## 1. Limit output lines

> Goal: Truncate output to a fixed number of lines, regardless of content.

### 1a. Using `-n`

```prompt
Show me just the first 4 lines about System.Text.Json.
```

```bash
dotnet-inspect System.Text.Json -n 4
```

```expect
# System.Text.Json.dll
## Library Info
```

```query
wc -l | tr -d ' '
```

```expect
4
```

```expect-not
Tips:
```

### 1b. Using `-N` shorthand

```bash
dotnet-inspect System.Text.Json -6
```

```expect
# System.Text.Json.dll
## Library Info
```

```query
wc -l | tr -d ' '
```

```expect
6
```

```expect-not
Tips:
```

## 2. Limit table rows

> Goal: Keep Markdown structure and table headers, but show only the first N data rows per rendered table.
> `--rows` is a mode for `-n`/`-N`; it requires a head count and cannot be combined with `--tail`.

```bash
dotnet-inspect System.Private.CoreLib -S "Async*" --rows -n 6
```

```expect
## Async Methods
| Name | Declaring Type | Kind | Signature |
DisposeAsync
WriteAsync
```

```query
grep -c '^| .* | .* | .* | .* |$'
```

```expect
6
```

## 3. Limit type results

> Goal: Return only the first N types from a type listing.

### 3a. Using `type -t N`

```prompt
Show me just 3 types from System.Text.Json.
```

```bash
dotnet-inspect type System.Text.Json -t 3 --tips q
```

```expect
Types:
JsonNamingPolicy
JsonCommentHandling
```

```expect-not
Tips:
```

## 4. Filter types by glob

> Goal: Filter types to those matching a name pattern.

### 4a. Using `type -t pattern`

```bash
dotnet-inspect type System.Text.Json -t "Json*" --tips q
```

```expect
# System.Text.Json
JsonSerializer
```

```expect-not
SortedSet
Tips:
```

## 5. Limit find results

> Goal: Return only the first N matches from a find search.

### 5a. Using `find -t N`

```bash
dotnet-inspect find "Json*" -t 3 -v:q
```

```expect
# Find: Json*
JsonContent
```

```query
grep -c '^| Json'
```

```expect
3
```

```expect-not
Tips:
```

## 6. Limit member results

> Goal: Return only the first N members from a member listing.

### 6a. Using `member -m N`

```bash
dotnet-inspect member System.Text.Json JsonSerializer -m 3
```

```expect
# System.Text.Json.JsonSerializer
IsReflectionEnabledByDefault
Deserialize
```

```expect-not
Tips:
```

## 7. Filter members by name

> Goal: Show only members matching an exact name, including all overloads.

### 7a. Using positional member name

```bash
dotnet-inspect member System.Text.Json JsonSerializer Deserialize --tips q
```

```expect
## Methods
Deserialize
```

```expect-not
| Serialize |
Tips:
```

## 8. Limit version list

> Goal: Return only the first N versions.

### 8a. Using `--versions N`

```bash
dotnet-inspect System.CommandLine --versions 3
```

```expect
2.0.8
2.0.7
2.0.6
```

```query
wc -l | tr -d ' '
```

```expect
3
```

```expect-not
Tips:
```

## 9. Count rows in a section

> Goal: Return a single integer row count for a selected table section.

### 9a. Count async methods

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

### 9b. Count requires one selected section

```bash
dotnet-inspect System.Text.Json --count
```

```expect-error
--count requires -S/--select to match exactly one section
```
