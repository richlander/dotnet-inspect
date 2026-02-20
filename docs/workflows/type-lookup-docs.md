---
id: type-lookup-docs
description: Look up types with documentation, shape, and inheritance
commands: [type]
areas: [types, documentation, shape, inheritance]
---

# Type Lookup with Documentation

> Discover and inspect types with their documentation, inheritance hierarchy, and member signatures. The `type` command shows type summaries by default; use `-v:d` for detailed documentation including member descriptions.

## Preconditions

Named isolated session ensures reproducible results (no shared state, no NuGet cache).

```bash
export DOTNET_INSPECT_ISOLATED=type-lookup
```

```bash
dotnet-inspect cache clear
```

Prime the cache with test packages:

```bash
dotnet-inspect System.CommandLine@2.0.3 -v:q
```

```bash
dotnet-inspect Microsoft.Extensions.Options@10.0.2 -v:q
```

```bash
dotnet-inspect System.Collections@4.3.0 -v:q
```

## 1. View type with documentation

> Goal: See type summary, description, and member overview.

### 1a. Default verbosity

```bash
dotnet-inspect type --package System.CommandLine Command
```

```expect
# System.CommandLine.Command
Kind: class
## Constructors
## Properties
## Methods
```

```query
grep -c '| ---- |'
```

### 1b. Detailed verbosity (with descriptions)

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

### 1c. Quiet mode (summary only)

```bash
dotnet-inspect type --package System.CommandLine Command -v:q
```

```expect
# System.CommandLine.Command
Kind: class
Properties: 8
Methods: 10
```

```expect-not
## Properties
```

## 2. View type shape (inheritance tree)

> Goal: See inheritance, interfaces, and all member signatures in a tree view.

### 2a. Class with inheritance

```bash
dotnet-inspect type --package System.CommandLine Command --shape
```

```expect
├─ Inherits
│  └─ System.CommandLine.Symbol
├─ Implements
│  └─ System.Collections.IEnumerable
├─ Constructors
├─ Properties
└─ Methods
```

```expect-not
Tips:
```

### 2b. Static class

```bash
dotnet-inspect type System.Text.Json JsonSerializer --shape -n 15
```

```expect
├─ Inherits
│  └─ System.Object
├─ Properties
└─ Methods
```

## 3. Fully qualified type names

> Goal: Look up types using their fully qualified name (namespace + type).

### 3a. Platform type via qualified name

```bash
dotnet-inspect System.Text.Json.JsonSerializer -v:q
```

```expect
# System.Text.Json.JsonSerializer
Kind: class
Source: Platform
```

### 3b. Nested namespace type

```bash
dotnet-inspect System.Text.Json.Serialization.JsonConverter -v:q
```

```expect
# System.Text.Json.Serialization.JsonConverter
Library: System.Text.Json
```

### 3c. ASP.NET Core type

```bash
dotnet-inspect Microsoft.AspNetCore.Builder.WebApplication -v:q
```

```expect
# Microsoft.AspNetCore.Builder.WebApplication
Source: Platform
```

## 4. List available sections

> Goal: Discover what sections are available for a type.

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

## 5. Filter to specific sections

> Goal: View only selected sections of a type.

### 5a. Single section

```bash
dotnet-inspect type --package System.CommandLine Command -v:d -s Interfaces
```

```expect
## Interfaces
| Interface |
System.Collections.IEnumerable
```

```expect-not
## Properties
## Methods
```

### 5b. Multiple sections

```bash
dotnet-inspect type --package System.CommandLine Command -v:d -s Interfaces,Baseclass -n 15
```

```expect
## Interfaces
## Baseclass
```

```expect-not
## Properties
```

## 6. Platform vs package resolution

> Goal: Understand when the same name resolves to platform or NuGet.

### 6a. Platform library (default for System.*)

```bash
dotnet-inspect type System.Text.Json JsonDocument -v:q
```

```expect
Source: Platform
```

### 6b. Force package resolution

```bash
dotnet-inspect type --package System.Text.Json JsonDocument -v:q
```

```expect
Source: NuGet
```

## 7. Generic types

> Goal: Look up generic types using angle bracket or backtick notation.

### 7a. Using quoted generic syntax

```bash
dotnet-inspect type --package System.Collections 'HashSet<T>' -v:q
```

```expect
# System.Collections.Generic.HashSet<T>
Type Parameters: T
```

### 7b. Using backtick notation

```bash
dotnet-inspect type --package Microsoft.Extensions.Options 'OptionsFactory`1' -v:q
```

```expect
# Microsoft.Extensions.Options.OptionsFactory<TOptions>
Type Parameters: TOptions
```

## 8. Filter types by pattern

> Goal: Find types matching a glob pattern within a package.

### 8a. List matching types

```bash
dotnet-inspect type System.Text.Json -t "Json*" --oneline --no-header -n 10
```

```expect
JsonSerializer
JsonDocument
JsonElement
```

### 8b. Default view with filter

```bash
dotnet-inspect type System.Text.Json -t "Json*" -n 15
```

```expect
# System.Text.Json
## Classes
| Type | Members |
JsonDocument
JsonSerializer
```

```query
grep -c 'Json'
```

## 9. View type with member filter

> Goal: Limit which members are shown in the type view.

### 9a. Filter members by name pattern

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

### 9b. Limit member count

```bash
dotnet-inspect type --package System.CommandLine Command -m 3
```

```expect
## Constructors
## Properties
and 10 more members
```

```expect-not
## Methods
```

## 10. Remote source information

> Goal: View where the source code for a type can be found.

```bash
dotnet-inspect type --package System.CommandLine Command -v:d -s "Remote Source" -n 10
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
