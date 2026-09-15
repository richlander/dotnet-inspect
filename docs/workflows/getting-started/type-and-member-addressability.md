---
id: type-and-member-addressability
description: Addressing types and members across platform and NuGet sources
commands: [type, member]
areas: [routing, resolution, type, member]
---

# Type and Member Addressability

> How to address types and members across platform and NuGet sources. The tool provides a progressive addressing model: start with an assembly, narrow to a type, then to a member. Fully qualified type names are also supported as a shortcut for platform types.

Ref: [PR #193](https://github.com/richlander/dotnet-inspect/pull/193) (type/member split), [PR #207](https://github.com/richlander/dotnet-inspect/pull/207) (qualified type names)

## Preconditions

Isolated session.

```bash
export DOTNET_INSPECT_ISOLATED=type-member-addr
```

```bash
dotnet-inspect cache clear
```

## List types in a library

> Goal: See all public types in a library, grouped by kind (classes, structs, enums).

### Platform library

```prompt
What types are in System.Text.Json?
```

```bash
dotnet-inspect type System.Text.Json -v:q
```

```expect
Library: System.Text.Json.dll
Source: Platform
```

```query
grep -Eq 'Types: [1-9][0-9]*' && echo type-count-positive
```

```expect
type-count-positive
```

### NuGet package

```prompt
What types are in System.CommandLine 2.0.3?
```

```bash
dotnet-inspect type System.CommandLine@2.0.3 -v:q
```

```expect
Library: System.CommandLine.dll
Source: NuGet
```

```query
grep -Eq 'Types: [1-9][0-9]*' && echo type-count-positive
```

```expect
type-count-positive
```

## Filter types by glob

> Goal: Narrow the type list to names matching a pattern.

```prompt
Find all types starting with "JsonS" in System.Text.Json.
```

```bash
dotnet-inspect type System.Text.Json "JsonS*" -v:q
```

```expect
System.Text.Json.JsonSerializer
System.Text.Json.JsonSerializerOptions
```

```query
awk -F'`' '/^\| `/ { name=$2; sub(/^.*\./, "", name); rows++; if (name !~ /^JsonS/) bad=1 } END { if (rows && !bad) print "jsons-filter-only" }'
```

```expect
jsons-filter-only
```

## Address a single type

> Goal: Get the type summary for a specific type by name.

### Assembly + type name

```prompt
Show me the JsonSerializer type.
```

```bash
dotnet-inspect type System.Text.Json JsonSerializer -v:q --markdown
```

```expect
# System.Text.Json.JsonSerializer
Kind:
Library:
System.Text.Json.dll
```

### Fully qualified name (bare)

```prompt
Show me JsonSerializer using its full name.
```

```bash
dotnet-inspect System.Text.Json.JsonSerializer -v:q --markdown
```

```expect
# System.Text.Json.JsonSerializer
Source: Platform
```

### Fully qualified name (`type` command)

```prompt
Look up the JsonSerializer type using the type command.
```

```bash
dotnet-inspect type System.Text.Json.JsonSerializer -v:q --markdown
```

```expect
# System.Text.Json.JsonSerializer
Source: Platform
```

## Qualified names with nested namespaces

> Goal: Types deeper than the assembly namespace resolve correctly.

```prompt
Show me the JsonConverter type in System.Text.Json.Serialization.
```

```bash
dotnet-inspect System.Text.Json.Serialization.JsonConverter -v:q --markdown
```

```expect
# System.Text.Json.Serialization.JsonConverter
Library:
System.Text.Json.dll
```

## Qualified names for ASP.NET Core

> Goal: `Microsoft.*` qualified names resolve against the ASP.NET Core shared framework.

```prompt
Tell me about the WebApplication class.
```

```bash
dotnet-inspect Microsoft.AspNetCore.Builder.WebApplication -v:q --markdown
```

```expect
# Microsoft.AspNetCore.Builder.WebApplication
Library:
Microsoft.AspNetCore.dll
Source: Platform
```

## Assembly names still resolve as assemblies

> Goal: A name that matches an actual assembly is not treated as a qualified type name.

```prompt
What is System.Text.Json?
```

```bash
dotnet-inspect System.Text.Json -v:q
```

```expect
# System.Text.Json.dll
```

```expect-not
Kind:
```

## View type shape

> Goal: See inheritance, interfaces, constructors, properties, and methods in a tree view.

```prompt
Show me the shape of JsonSerializer — inheritance and members.
```

```bash
dotnet-inspect type System.Text.Json JsonSerializer --shape
```

```expect
Inherits
Properties
Methods
```

## List members of a type

> Goal: See all members for a type with docs.

```prompt
What methods does JsonSerializer have?
```

```bash
dotnet-inspect member System.Text.Json JsonSerializer -v:q
```

```expect
# System.Text.Json.JsonSerializer
Kind: class
Properties:
Methods:
```

```query
grep -Eq 'Properties: [1-9][0-9]*' && echo properties-positive
grep -Eq 'Methods: [1-9][0-9]*' && echo methods-positive
```

```expect
properties-positive
methods-positive
```

## Address a specific member by name

> Goal: Filter members to a single name (all overloads).

```prompt
Show me all Deserialize overloads on JsonSerializer.
```

```bash
dotnet-inspect member System.Text.Json JsonSerializer Deserialize -v:m
```

```expect
# System.Text.Json.JsonSerializer
## Methods
| Name | Digest | Signature | Description |
| Deserialize |
```

```query
awk -F '|' '/^\| [A-Za-z]/ && $2 != " Name " { gsub(/^ +| +$/, "", $2); print $2 }' | sort -u | wc -l | tr -d ' '
```

```expect
1
```

## Member select mode

> Goal: `-S "Member Index"` renders the Member Index section with addressing tokens for detailed member pages.

```prompt
Show me the members of Command with selection tokens.
```

```bash
dotnet-inspect member System.CommandLine@2.0.3 Command -S "Member Index"
```

```expect
# System.CommandLine.Command
## Member Index
| Selector | Stable | Canonical Signature |
.ctor
SetAction
```

## Members from a NuGet package

> Goal: Address types from NuGet packages via the router.

```prompt
What members does the Command type in System.CommandLine have?
```

```bash
dotnet-inspect member System.CommandLine@2.0.3 Command -v:q
```

```expect
# System.CommandLine.Command
Kind: class
Source: NuGet
```
