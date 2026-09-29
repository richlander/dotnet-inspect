---
id: type-shape-and-relations
description: Inspect type hierarchy — shape, inheritance walks, and implementer discovery
commands: [type, depends]
areas: [types, shape, inheritance, interfaces, relations, depends]
---

# Type Shape and Hierarchy

> Understand a type's structure and its place in the type hierarchy. Three complementary views: `type --tree` shows the full type structure (inheritance, interfaces, members). `depends` walks the hierarchy **upward** — base classes and interfaces a type inherits. The `Implementers` and `Derived Types` sections walk **downward** through Subject Relations.

## Preconditions

Isolated session with cached packages.

```bash
export DOTNET_INSPECT_ISOLATED=type-shape-relations
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
dotnet-inspect package System.CommandLine@2.0.3 -v:q
```

## 1. View type shape

> Goal: See inheritance, interfaces, and all member signatures in a tree view.

### 1a. Class with inheritance and interfaces

```prompt
What does the Command type look like? Show its shape.
```

```bash
dotnet-inspect type --package System.CommandLine@2.0.3 Command \
  --tree -n 30 --lines --tips q
```

```expect
System.CommandLine.Command (System.CommandLine 2.0.3)
Inherits
System.CommandLine.Symbol
Implements
System.Collections.IEnumerable
Constructors
Properties
Methods
```

```expect-not
Tips:
```

### 1b. Static class

```bash
dotnet-inspect type System.Text.Json JsonSerializer \
  --tree -n 20 --lines --tips q
```

```expect
System.Text.Json.JsonSerializer
Inherits
System.Object
Properties
Methods
```

### 1c. Struct

```bash
dotnet-inspect type System.Text.Json JsonElement \
  --tree -n 20 --lines --tips q
```

```expect
System.Text.Json.JsonElement
Inherits
System.ValueType
Properties
Methods
```

## 2. View shape for types with many interfaces

> Goal: Types like WebApplication implement many interfaces — shape shows them all.

```prompt
What interfaces does WebApplication implement?
```

```bash
dotnet-inspect type Microsoft.AspNetCore.Builder.WebApplication \
  --tree -n 30 --lines --tips q
```

```expect
Microsoft.AspNetCore.Builder.WebApplication
Inherits
System.Object
Implements
Microsoft.Extensions.Hosting.IHost
System.IDisposable
Microsoft.AspNetCore.Builder.IApplicationBuilder
Microsoft.AspNetCore.Routing.IEndpointRouteBuilder
System.IAsyncDisposable
Properties
```

## 3. Walk hierarchy upward with depends

> Goal: See the full inheritance tree — base classes and all interfaces a type depends on.

### 3a. Simple class hierarchy

```prompt
What does Stream inherit from?
```

```bash
dotnet-inspect depends Stream -v:q
```

```expect
System.IO.Stream
System.MarshalByRefObject
System.IAsyncDisposable
System.IDisposable
```

### 3b. Class with deep interface hierarchy

```prompt
What is the full type hierarchy for WebApplication?
```

```bash
dotnet-inspect depends WebApplication -v:q -n 30
```

```expect
Microsoft.AspNetCore.Builder.WebApplication
Microsoft.Extensions.Hosting.IHost
System.IDisposable
Microsoft.AspNetCore.Builder.IApplicationBuilder
Microsoft.AspNetCore.Routing.IEndpointRouteBuilder
System.IAsyncDisposable
```

### 3c. NuGet package type

```bash
dotnet-inspect depends Command --package System.CommandLine@2.0.3 -v:q
```

```expect
System.CommandLine.Command
System.CommandLine.Symbol
System.Collections.IEnumerable
```

### 3d. Generic numeric type (deep hierarchy)

```prompt
What interfaces does Int128 implement?
```

```bash
dotnet-inspect depends Int128 -n 40
```

```expect
System.Int128
System.Numerics.IBinaryInteger<System.Int128>
System.Numerics.IBinaryNumber<TSelf>
System.Numerics.IShiftOperators<TSelf, int, TSelf>
System.Numerics.IMinMaxValue<System.Int128>
System.Numerics.ISignedNumber<System.Int128>
```

### 3e. Interface hierarchy

```bash
dotnet-inspect depends IFloatingPointIeee754 -n 40
```

```expect
System.Numerics.IFloatingPointIeee754<T>
System.Numerics.IExponentialFunctions<TSelf>
System.Numerics.IFloatingPointConstants<TSelf>
System.Numerics.INumberBase<TSelf>
```

## 4. Find derived types of a base class

> Goal: Discover all types that extend a base class across the platform.

### 4a. Default scope

```prompt
What types extend Stream?
```

```bash
dotnet-inspect type System.IO.Stream --platform System.Private.CoreLib \
  -S "Derived Types" -v:q
```

```expect
# Relations for System.IO.Stream
## Derived Types
base type
```

### 4b. Limited results

```bash
dotnet-inspect type System.IO.Stream --platform System.Private.CoreLib \
  -S "Derived Types" -n 3 -v:q
```

```expect
# Relations for System.IO.Stream
## Derived Types
base type
```

```query
awk '/^\| `/ { count++ } END { if (count == 3) print "three-derived-types" }'
```

```expect
three-derived-types
```

## 5. Find implementers of an interface

> Goal: Discover types that implement a specific interface.

### 5a. Interface with few implementers

```prompt
What types implement IHost?
```

```bash
dotnet-inspect type Microsoft.Extensions.Hosting.IHost \
  --package Microsoft.Extensions.Hosting.Abstractions@10.0.0 \
  --tfm net10.0 -S Implementers -v:q
```

```expect
# Relations for Microsoft.Extensions.Hosting.IHost
## Implementers
interface
```

### 5b. Interface with many implementers

```bash
dotnet-inspect type System.IDisposable --platform System.Private.CoreLib \
  -S Implementers -v:q -n 5
```

```expect
# Relations for System.IDisposable
## Implementers
interface
```

```query
awk '/^\| `/ { count++ } END { if (count == 5) print "five-implementers" }'
```

```expect
five-implementers
```

## 6. Make the platform scope explicit

> Goal: Name the same platform framework set explicitly. Bare `--platform` is
> equivalent to the implicit default when used alone and composes with other
> explicit source selectors.

### 6a. Explicit platform scope

```bash
dotnet-inspect type \
  System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver \
  --platform System.Text.Json -S Implementers -v:q
```

```expect
# Relations for System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver
## Implementers
`System.Text.Json.Serialization.JsonSerializerContext`
`System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver`
```

## 7. Table output for scripting

> Goal: Columnar output suitable for piping and filtering.

### 7a. With header

```bash
dotnet-inspect type System.IO.Stream --platform System.Private.CoreLib \
  -S "Derived Types" --table -n 3
```

```expect
Type
Relationship
Library
Source
```

### 7b. Without header for piping

```bash
dotnet-inspect type System.IO.Stream --platform System.Private.CoreLib \
  -S "Derived Types" --table --no-headers -n 3
```

```expect
base type
```

```expect-not
Type
Kind
```

```query
wc -l | tr -d ' '
```

## 8. Shape → depends → Subject Relations workflow

> Goal: Start with `type --tree` to see structure, use `depends` to walk the hierarchy upward, then use `Implementers` to find sibling types.

### 8a. Discover interfaces via shape

```prompt
What interfaces does Command implement, and what other types implement those interfaces?
```

```bash
dotnet-inspect type --package System.CommandLine@2.0.3 Command \
  --tree -n 15 --lines --tips q
```

```expect
Implements
System.Collections.IEnumerable
```

### 8b. Walk hierarchy upward

```bash
dotnet-inspect depends Command --package System.CommandLine@2.0.3 -v:q
```

```expect
System.CommandLine.Command
System.CommandLine.Symbol
System.Collections.IEnumerable
```

### 8c. Find types implementing the same interface

```bash
dotnet-inspect type System.Collections.IEnumerable \
  --platform System.Private.CoreLib -S Implementers -n 5 -v:q
```

```expect
# Relations for System.Collections.IEnumerable
## Implementers
interface
```
