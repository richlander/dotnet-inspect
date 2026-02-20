# Type and Member Addressability

> How to address types and members across platform and NuGet sources. The tool provides a progressive addressing model: start with an assembly, narrow to a type, then to a member. Fully qualified type names are also supported as a shortcut for platform types.

Ref: [PR #193](https://github.com/richlander/dotnet-inspect/pull/193) (type/member split), [PR #207](https://github.com/richlander/dotnet-inspect/pull/207) (qualified type names)

## Preconditions

Isolated session with cached packages. Offline mode ensures no unexpected network dependencies.

```bash
export DOTNET_INSPECT_ISOLATED=type-member-addr
export DOTNET_INSPECT_OFFLINE=1
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
DOTNET_INSPECT_OFFLINE=0 dotnet-inspect System.CommandLine -v:q
```

## List types in a library

> Goal: See all public types in a library, grouped by kind (classes, structs, enums).

### Platform library

```bash
dotnet-inspect type System.Text.Json -v:q
```

```expect
Types: 80
Source: Platform
```

### NuGet package

```bash
dotnet-inspect type --package System.CommandLine -v:q
```

```expect
Types: 39
Source: NuGet
```

## Filter types by glob

> Goal: Narrow the type list to names matching a pattern.

```bash
dotnet-inspect type System.Text.Json "Json*" -v:q
```

```expect
Types: 66
```

## Address a single type

> Goal: Get the type summary for a specific type by name.

### Assembly + type name

```bash
dotnet-inspect type System.Text.Json JsonSerializer -v:q
```

```expect
# System.Text.Json.JsonSerializer
Kind:
Library: System.Text.Json
```

### Fully qualified name (bare)

```bash
dotnet-inspect System.Text.Json.JsonSerializer -v:q
```

```expect
# System.Text.Json.JsonSerializer
Source: Platform
```

### Fully qualified name (`type` command)

```bash
dotnet-inspect type System.Text.Json.JsonSerializer -v:q
```

```expect
# System.Text.Json.JsonSerializer
Source: Platform
```

## Qualified names with nested namespaces

> Goal: Types deeper than the assembly namespace resolve correctly.

```bash
dotnet-inspect System.Text.Json.Serialization.JsonConverter -v:q
```

```expect
# System.Text.Json.Serialization.JsonConverter
Library: System.Text.Json
```

## Qualified names for ASP.NET Core

> Goal: `Microsoft.*` qualified names resolve against the ASP.NET Core shared framework.

```bash
dotnet-inspect Microsoft.AspNetCore.Builder.WebApplication -v:q
```

```expect
# Microsoft.AspNetCore.Builder.WebApplication
Library: Microsoft.AspNetCore
Source: Platform
```

## Assembly names still resolve as assemblies

> Goal: A name that matches an actual assembly is not treated as a qualified type name.

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

```bash
dotnet-inspect type System.Text.Json JsonSerializer --shape
```

```expect
├─ Inherits
├─ Properties
└─ Methods
```

## List members of a type

> Goal: See all members for a type with docs.

```bash
dotnet-inspect member System.Text.Json JsonSerializer -v:q
```

```expect
# System.Text.Json.JsonSerializer
Properties: 1
Methods: 103
```

## Address a specific member by name

> Goal: Filter members to a single name (all overloads).

```bash
dotnet-inspect member System.Text.Json JsonSerializer Deserialize -v:q
```

```expect
# System.Text.Json.JsonSerializer
Methods: 1
```

## Member select mode

> Goal: `--select` adds a Select column with addressing tokens for detailed member pages.

```bash
dotnet-inspect member --package System.CommandLine Command --select -v:q
```

```expect
# System.CommandLine.Command
```

## Members from a NuGet package

> Goal: Address types from NuGet packages using `--package`.

```bash
dotnet-inspect member --package System.CommandLine Command -v:q
```

```expect
# System.CommandLine.Command
Source: NuGet
```
