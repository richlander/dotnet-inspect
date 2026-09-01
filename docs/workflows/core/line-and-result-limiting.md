---
id: line-and-result-limiting
description: Control output size with item, range, rendered-line, ranked, count, and shape projections
commands: [-n, --rows, --lines, --tail-lines, --head, --tail, --top, --versions, --count, --value, --urls, --paths, --json-array, --print, --row]
areas: [output, limiting, count, agents]
---

# Line, Result, and Row Counting

> Control how much output is returned. `-n N` keeps the first N declared items; add `--tail` for the last N. `--rows N..M`, `N+K`, and `N..` select stable absolute row addresses. Add `--lines` to make `-n` limit rendered lines instead, or use `--tail-lines` for the rendered suffix. `--top N` applies only to ranked sections. `--value`, `--urls`, and `--paths` project selected rows to scalar/URL/path payloads; `--json-array` makes projected rows one JSON document; `--row N` chooses one projected or printable row. `-n N` also limits version-list rows. `--count` reduces the full selected/filter cohort to a count and rejects item, range, ranked, exact-row, direction, and line gestures. These are essential for agents that need compact, predictable output.

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
dotnet-inspect System.CommandLine@2.0.3 -v:q
```

## 1. Limit output lines

> Goal: Truncate output to a fixed number of lines, regardless of content.

### 1a. Using `-n`

```prompt
Show me just the first 4 lines about System.Text.Json.
```

```bash
dotnet-inspect System.Text.Json -n 4 --lines
```

```expect
# System.Text.Json.dll
## Library Info
```

```expect-not
Tips:
```

```query
wc -l | tr -d ' '
```

```expect
4
```

### 1b. Using `-N` shorthand

```bash
dotnet-inspect System.Text.Json -6 --lines
```

```expect
# System.Text.Json.dll
## Library Info
```

```expect-not
Tips:
```

```query
wc -l | tr -d ' '
```

```expect
6
```

## 2. Limit declared items

> Goal: Keep only the first N declared items in each selected row set after
> filtering and ordering. Add `--tail` for the last N. Use `--rows` only for
> absolute row addresses.

```bash
dotnet-inspect System.Text.Json -S "Async*" -n 6
```

```expect
## Async Methods
| Name | Declaring Type | Kind | Signature |
ParseAsync
```

```query
grep '^|' | tail -n +3 | wc -l | tr -d ' '
```

```expect
6
```

## 3. Limit type results

> Goal: Return only the first N types from a type listing.

### 3a. Using `type -n N`

```prompt
Show me just 3 types from System.Text.Json.
```

```bash
dotnet-inspect type System.Text.Json -S Classes -n 3 --tips q
```

```expect
# System.Text.Json
```

```expect-not
Tips:
```

```query
grep -c '^| `'
```

```expect
3
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

### 5a. Using `find -n N`

```bash
dotnet-inspect find "Json*" -n 3 -v:q
```

```expect
# Find: Json*
JsonContent
```

```expect-not
Tips:
```

```query
grep '^|' | tail -n +3 | wc -l | tr -d ' '
```

```expect
3
```

## 6. Limit one member row set

> Goal: Return only the first N methods from the Methods row set.

### 6a. Using `member -n N`

```bash
dotnet-inspect member System.Text.Json JsonSerializer -S Methods -n 3 --tips q
```

```expect
# System.Text.Json.JsonSerializer
Deserialize
```

```expect-not
Tips:
```

```query
grep '^| Deserialize' | wc -l | tr -d ' '
```

```expect
3
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

### 8a. Using `--versions` with `-n N`

```bash
dotnet-inspect System.CommandLine --versions -n 3
```

```expect-not
Tips:
```

```query
wc -l | tr -d ' '
```

```expect
3
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

### 9b. Count requires one selected section

```bash
dotnet-inspect System.Text.Json --count
```

```expect-error
--count requires -S/--select to match at least one section.
```
