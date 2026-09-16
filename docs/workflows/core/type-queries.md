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

```bash
dotnet-inspect System.Text.Json@10.0.0 -v:q
```

## 1. List types in a package

> Goal: See all public types in a package.

### 1a. Using `type` with package name

```prompt
What types are in the System.CommandLine package?
```

```bash
dotnet-inspect type System.CommandLine@2.0.3 -v:q --tips q
```

```expect
# System.CommandLine
Types:
Source: NuGet
```

```expect-not
Tips:
```

```query
grep -oE 'Types: [0-9]+'
grep -oE 'Source: [A-Za-z]+'
```

```expect
Types: 39
Source: NuGet
```

### 1b. Using `type` with table output

```bash
dotnet-inspect type System.CommandLine@2.0.3 --table -t 5 --no-headers --tips q
```

```expect-not
Tips:
Kind
```

```query
wc -l | tr -d ' '
```

```expect
5
```

### 1c. Table column structure

```bash
dotnet-inspect type System.Text.Json --table | head -1
```

```expect
Kind
Type
Members
```

```query
grep -o 'Kind\|Type\|Members' | wc -l | tr -d ' '
```

```expect
3
```

## 2. List types in a platform library

> Goal: See all public types in a platform library like System.Text.Json.

### 2a. Using `type` with platform library

```bash
dotnet-inspect type System.Text.Json -v:q --tips q
```

```expect
# System.Text.Json
Types:
Source: Platform
```

```expect-not
Tips:
```

```query
grep -Eq 'Types: [1-9][0-9]*' && echo type-count-positive
```

```expect
type-count-positive
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
dotnet-inspect type System.Text.Json JsonSerializer --markdown -v:q --tips q
```

```expect
# System.Text.Json.JsonSerializer
Kind: class
Source: Platform
Methods:
```

```expect-not
Tips:
```

```query
grep -o 'Kind: [a-z]*'
grep -Eq 'Methods: [1-9][0-9]*' && echo methods-positive
```

```expect
Kind: class
methods-positive
```

### 4b. Using fully qualified type name

```bash
dotnet-inspect System.Text.Json.JsonSerializer --markdown -v:q --tips q
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
dotnet-inspect type --package System.CommandLine@2.0.3 Command --markdown -v:q --tips q
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
Kind
Name
Return Type
Detail
```

```query
grep -o 'Kind\|Name\|Return Type\|Detail' | wc -l | tr -d ' '
```

## 5. View type with documentation

> Goal: See type description and member documentation at detailed verbosity.

### 5a. Detailed verbosity (with descriptions)

```bash
dotnet-inspect type --package System.CommandLine@2.0.3 Command --markdown -v:d -n 30 --tips q
```

```expect
# System.CommandLine.Command
Represents a specific action that the application performs.
| Name | Digest | Signature | Description |
Initializes a new instance
```

```expect-not
Tips:
```

## 6. View type shape

> Goal: See inheritance, interfaces, and member signatures in a tree view.

### 5a. Using `--shape` flag

```bash
dotnet-inspect type --package System.CommandLine@2.0.3 Command --shape --tips q
```

```expect
System.CommandLine.Command
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
dotnet-inspect type System.Text.Json JsonElement --shape --tips q
```

```expect
System.Text.Json.JsonElement
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
dotnet-inspect find "Command*" --package System.CommandLine@2.0.3 -v:q
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
What chat types are found across up to 500 Azure AI packages returned by prefix search?
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
grep -c '^| Chat' | awk '$1 > 0 { print "positive" }'
```

```expect
positive
```

## 8. Compare platform vs package types

> Goal: Understand when the same name resolves differently.

### 7a. Platform library (default for System.*)

```bash
dotnet-inspect type System.Text.Json JsonDocument --markdown -v:q --tips q
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
dotnet-inspect type --package System.Text.Json@10.0.0 JsonDocument --markdown -v:q --tips q
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
dotnet-inspect type --package System.Collections@4.3.0 'HashSet<T>' --markdown -v:q --tips q
```

```expect
# System.Collections.Generic.HashSet&lt;T&gt;
Type Parameters: T
```

### 9b. Using backtick notation

```bash
dotnet-inspect type --package Microsoft.Extensions.Options@10.0.2 'OptionsFactory`1' --markdown -v:q --tips q
```

```expect
# Microsoft.Extensions.Options.OptionsFactory&lt;TOptions&gt;
Type Parameters: TOptions
```

## 10. Type sections

> Goal: Discover and filter to specific sections of a type view.

### 10a. List available sections

```bash
dotnet-inspect type --package System.CommandLine@2.0.3 Command -D
```

```expect
Interfaces
Baseclass
Constructors
Properties
Method Groups
Methods
```

### 10b. Filter to specific sections

```bash
dotnet-inspect type --package System.CommandLine@2.0.3 Command -v:d -S Interfaces,Baseclass -n 15 --tips q
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
Deserialize
DeserializeAsync
```

```expect-not
Serialize (
```

```query
grep -E 'Deserialize(Async)? \([0-9]+ overloads\)'
```

### 11b. Limit member count

```bash
dotnet-inspect type --package System.CommandLine@2.0.3 Command -m 3 --tips q
```

```expect
Action
Aliases
```

```expect-not
Arguments
```

## 12. Source files

> Goal: View where source code for a type can be found.

```bash
dotnet-inspect type --package System.CommandLine@2.0.3 Command -v:d -S "Source Files" -n 10 --tips q
```

```expect
## Source Files
| Url |
raw.githubusercontent.com
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
System.Text.Json.Utf8JsonWriter
```

```query
awk 'NR == 1 { previous = $1; rows = 1; next } { if ($1 > previous) bad = 1; previous = $1; rows++ } END { if (rows == 5 && !bad) print "descending-five" }'
```

```expect
descending-five
```

## 14. Filter types with unsafe signatures

> Goal: The `--unsafe` flag filters to types that have members with pointer signatures.

```bash
dotnet-inspect type System.Runtime --unsafe -t 5 --table --no-headers
```

```expect
class
System.ArgIterator
```

```expect-not
SafeHandle
```

```query
wc -l | tr -d ' '
```

```expect
5
```

## 15. View type SourceLink files

> Goal: Select the current `Source Files` section for a pinned package type.

```bash
dotnet-inspect type --package System.CommandLine@2.0.3 Command -S "Source Files" --tips q
```

```expect
## Source Files
raw.githubusercontent.com
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
