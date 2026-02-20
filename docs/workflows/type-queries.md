---
id: type-queries
description: Discover and inspect types in packages and platform libraries
commands: [type, find, --package-prefix]
areas: [types, discovery, inspection, package-prefix, unsafe, sourcelink, platform-version]
---

# Type Queries

> Find and inspect types across NuGet packages and platform libraries. These are core workflows for understanding APIs — listing types, filtering by pattern, and drilling into specific types.

## Preconditions

Isolated session with cached packages. Offline mode ensures no unexpected network dependencies.

```bash
export DOTNET_INSPECT_ISOLATED=type-queries
export DOTNET_INSPECT_OFFLINE=1
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
DOTNET_INSPECT_OFFLINE=0 dotnet-inspect System.CommandLine -v:q
```

## 1. List types in a package

> Goal: See all public types in a package.

### 1a. Using `type` with package name

```bash
dotnet-inspect type System.CommandLine -v:q
```

```expect
# System.CommandLine
Types: 39
Source: NuGet
```

```expect-not
Tips:
```

```query
grep -o 'Types: [0-9]*'
grep -o 'Source: [A-Za-z]*'
```

### 1b. Using `type` with oneline output

```bash
dotnet-inspect type System.CommandLine --oneline -t 5 --no-header
```

```expect
class
```

```expect-not
Tips:
KIND
```

```query
wc -l | tr -d ' '
```

### 1c. Oneline column structure

```bash
dotnet-inspect type System.Text.Json --oneline | head -1
```

```expect
KIND
TYPE
MEMBERS
```

```query
grep -o 'KIND\|TYPE\|MEMBERS' | wc -l | tr -d ' '
```

## 2. List types in a platform library

> Goal: See all public types in a platform library like System.Text.Json.

### 2a. Using `type` with platform library

```bash
dotnet-inspect type System.Text.Json -v:q
```

```expect
# System.Text.Json
Types: 80
Source: Platform
```

```expect-not
Tips:
```

```query
grep -o 'Types: [0-9]*'
grep -o 'Source: [A-Za-z]*'
```

## 3. Filter types by pattern

> Goal: Find types matching a glob pattern within a package.

### 3a. Using `-t` with glob pattern

```bash
dotnet-inspect type System.Text.Json -t "Json*" --oneline --no-header
```

```expect
JsonSerializer
JsonDocument
JsonElement
```

```expect-not
Tips:
```

```query
wc -l | tr -d ' '
```

### 3b. Filter to specific kind

```bash
dotnet-inspect type System.Text.Json -t "Json*" --oneline --no-header | grep '^enum'
```

```expect
JsonValueKind
JsonTokenType
```

```query
wc -l | tr -d ' '
```

## 4. Inspect a specific type

> Goal: Get details about a specific type by name.

### 4a. Using type name positional argument

```bash
dotnet-inspect type System.Text.Json JsonSerializer -v:q
```

```expect
# System.Text.Json.JsonSerializer
Kind: class
Source: Platform
Methods: 103
```

```expect-not
Tips:
```

```query
grep -o 'Kind: [a-z]*'
grep -o 'Methods: [0-9]*'
```

### 4b. Using fully qualified type name

```bash
dotnet-inspect System.Text.Json.JsonSerializer -v:q
```

```expect
# System.Text.Json.JsonSerializer
Kind: class
Source: Platform
```

```query
grep -o 'Kind: [a-z]*'
```

### 4c. Inspect Command type from System.CommandLine

```bash
dotnet-inspect type System.CommandLine Command -v:q
```

```expect
# System.CommandLine.Command
Kind: class
Source: NuGet
```

```expect-not
Tips:
```

```query
grep -o 'Kind: [a-z]*'
grep -o 'Source: [A-Za-z]*'
```

### 4d. Member oneline column structure

```bash
dotnet-inspect type System.Text.Json JsonSerializer --oneline | head -1
```

```expect
KIND
NAME
RETURN TYPE
DETAIL
```

```query
grep -o 'KIND\|NAME\|RETURN TYPE\|DETAIL' | wc -l | tr -d ' '
```

## 5. View type shape

> Goal: See inheritance, interfaces, and member signatures in a tree view.

### 5a. Using `--shape` flag

```bash
dotnet-inspect type System.CommandLine Command --shape
```

```expect
# System.CommandLine.Command
Inherits
Symbol
Implements
IEnumerable
Constructors
Properties
Methods
```

```expect-not
Tips:
```

```query
grep -E '(Inherits|Implements|Properties|Methods)'
```

### 5b. Shape for a struct

```bash
dotnet-inspect type System.Text.Json JsonElement --shape
```

```expect
# System.Text.Json.JsonElement
Inherits
Properties
Methods
```

```expect-not
Tips:
```

```query
grep -E '(Inherits|Properties|Methods)'
```

## 6. Search for types across packages

> Goal: Find types by name pattern across multiple sources.

### 6a. Using `find` command

```bash
dotnet-inspect find "JsonSer*" -v:q
```

```expect
JsonSerializer
JsonSerializerOptions
```

```expect-not
Tips:
```

```query
grep -c 'JsonSer'
```

### 6b. Search with package filter

```bash
dotnet-inspect find "Command*" --package System.CommandLine -v:q
```

```expect
Command
CommandLineParser
CommandResult
```

```expect-not
Tips:
```

```query
grep -c 'Command'
```

### 6c. Search across package prefix

```prompt
What chat types exist across all Azure AI packages?
```

```bash
dotnet-inspect find "Chat*" --package-prefix Azure.AI -v:q
```

```expect
# Find: Chat*
## Results
ChatCitation
Azure.AI.OpenAI
```

```expect-not
Tips:
```

```query
grep -oE 'Matches: [0-9]+'
```

## 7. Compare platform vs package types

> Goal: Understand when the same name resolves differently.

### 7a. Platform library (default for System.*)

```bash
dotnet-inspect type System.Text.Json JsonDocument -v:q
```

```expect
Source: Platform
```

```expect-not
Tips:
```

```query
grep -o 'Source: [A-Za-z]*'
```

### 7b. Force package resolution

```bash
dotnet-inspect type --package System.Text.Json JsonDocument -v:q
```

```expect
Source: NuGet
```

```expect-not
Tips:
```

```query
grep -o 'Source: [A-Za-z]*'
```

## 8. List types with member counts

> Goal: See types ranked by member count for API surface overview.

### 8a. Using oneline with awk sort

```bash
dotnet-inspect type System.Text.Json --oneline --no-header | awk '{print $NF, $2}' | sort -rn | head -5
```

```expect
Utf8JsonWriter
JsonSerializer
JsonNode
```

```query
head -3 | awk '{print $2}'
```

## 9. Filter types with unsafe signatures

> Goal: The `--unsafe` flag filters to types that have members with pointer signatures.

```bash
dotnet-inspect type System.Runtime --unsafe -t 5 --oneline --no-header
```

```expect
class
System.Runtime.CompilerServices.Unsafe
```

```query
wc -l | tr -d ' '
```

## 10. Filter types with SourceLink

> Goal: The `--sourcelink-only` flag filters to types that have SourceLink resolution — useful for knowing which types have browsable source.

```bash
dotnet-inspect type --package System.CommandLine --sourcelink-only -v:q
```

```expect
# System.CommandLine
Types:
Source: NuGet
```

```query
grep -oE 'Types: [0-9]+'
```

## 11. Platform library at specific runtime version

> Goal: Use `--framework runtime@version` to inspect a platform library from a specific .NET version.

```bash
dotnet-inspect type --platform System.Text.Json --framework runtime@8.0.0 -v:q
```

```expect
# System.Text.Json
Types:
Version: 8.0.0
```

```query
grep -oE 'Types: [0-9]+'
```
