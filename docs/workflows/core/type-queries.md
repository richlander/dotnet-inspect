---
id: type-queries
description: Discover, inspect, and document types in packages and platform libraries
commands: [type, find, --package-prefix]
areas: [types, discovery, inspection, documentation, shape, generics, package-prefix, unsafe, sourcelink, platform-version]
---

# Type Queries

> Find and inspect types across NuGet packages and platform libraries. These are core workflows for understanding APIs — listing types, filtering by pattern, drilling into specific types, and viewing documentation.

## Preconditions

Isolated session with cached packages.

```bash
export DOTNET_INSPECT_ISOLATED=type-queries
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
dotnet-inspect System.CommandLine@2.0.3 -v:q
```

```bash
dotnet-inspect Microsoft.Extensions.Options@10.0.2 -v:q
```

```bash
dotnet-inspect System.Collections@4.3.0 -v:q
```

## 1. List types in a package

> Goal: See all public types in a package.

### 1a. Using `type` with package name

```prompt
What types are in the System.CommandLine package?
```

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

### 1b. Using `type` with table output

```bash
dotnet-inspect type System.CommandLine --table -t 5 --no-headers
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
dotnet-inspect type System.Text.Json --table | head -1
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

```prompt
Find all types starting with "Json" in System.Text.Json.
```

### 3a. Using `-t` with glob pattern

```bash
dotnet-inspect type System.Text.Json -t "Json*" --table --no-headers
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
dotnet-inspect type System.Text.Json -t "Json*" --table --no-headers | grep '^enum'
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

```prompt
Tell me about the JsonSerializer class.
```

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

### 4d. Member table column structure

```bash
dotnet-inspect type System.Text.Json JsonSerializer --table | head -1
```

```expect
KIND
NAME
SIGNATURE
```

```query
grep -o 'KIND\|NAME\|SIGNATURE' | wc -l | tr -d ' '
```

## 5. View type with documentation

> Goal: See type description and member documentation at detailed verbosity.

### 5a. Detailed verbosity (with descriptions)

```bash
dotnet-inspect type --package System.CommandLine Command -v:d -n 30
```

```expect
# System.CommandLine.Command
Represents a specific action that the application performs.
| Name | Signature | Description |
Initializes a new instance
```

```expect-not
Tips:
```

## 6. View type shape

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

## 7. Search for types across packages

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

## 8. Compare platform vs package types

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

## 9. Generic types

> Goal: Look up generic types using angle bracket or backtick notation.

### 9a. Using quoted generic syntax

```bash
dotnet-inspect type --package System.Collections 'HashSet<T>' -v:q
```

```expect
# System.Collections.Generic.HashSet<T>
Type Parameters: T
```

### 9b. Using backtick notation

```bash
dotnet-inspect type --package Microsoft.Extensions.Options 'OptionsFactory`1' -v:q
```

```expect
# Microsoft.Extensions.Options.OptionsFactory<TOptions>
Type Parameters: TOptions
```

## 10. Type sections

> Goal: Discover and filter to specific sections of a type view.

### 10a. List available sections

```bash
dotnet-inspect type --package System.CommandLine Command -s
```

```expect
Interfaces
Baseclass
Constructors
Properties
Methods
```

### 10b. Filter to specific sections

```bash
dotnet-inspect type --package System.CommandLine Command -v:d -S Interfaces,Baseclass -n 15
```

```expect
## Interfaces
## Baseclass
```

```expect-not
## Properties
```

## 11. View type with member filter

> Goal: Limit which members are shown in the type view.

### 11a. Filter members by name pattern

```bash
dotnet-inspect type System.Text.Json JsonSerializer -m 'Deseri*'
```

```expect
# System.Text.Json.JsonSerializer
## Methods
Deserialize
DeserializeAsync
```

```expect-not
Serialize
SerializeAsync
```

### 11b. Limit member count

```bash
dotnet-inspect type --package System.CommandLine Command -m 3
```

```expect
## Constructors
## Properties
more members
```

## 12. Remote source information

> Goal: View where source code for a type can be found.

```bash
dotnet-inspect type --package System.CommandLine Command -v:d -S "Remote Source" -n 10
```

```expect
## Remote Source
| File | Url |
Command.cs
github.com
```

```expect-not
## Properties
Tips:
```

## 13. List types with member counts

> Goal: See types ranked by member count for API surface overview.

### 8a. Using TSV with awk sort

```bash
dotnet-inspect type System.Text.Json --tsv --no-headers | awk -F '\t' '{print $NF, $2}' | sort -rn | head -5
```

```expect
Utf8JsonWriter
JsonSerializer
JsonNode
```

```query
head -3 | awk '{print $2}'
```

## 14. Filter types with unsafe signatures

> Goal: The `--unsafe` flag filters to types that have members with pointer signatures.

```bash
dotnet-inspect type System.Runtime --unsafe -t 5 --table --no-headers
```

```expect
class
System.Runtime.CompilerServices.Unsafe
```

```query
wc -l | tr -d ' '
```

## 15. Filter types with SourceLink

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

## 16. Platform library at specific runtime version

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
