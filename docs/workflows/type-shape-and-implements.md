---
id: type-shape-and-implements
description: Inspect type hierarchy — shape, inheritance walks, and implementer discovery
commands: [type, depends, implements]
areas: [types, shape, inheritance, interfaces, implements, depends]
---

# Type Shape and Hierarchy

> Understand a type's structure and its place in the type hierarchy. Three complementary views: `type --shape` shows the full type structure (inheritance, interfaces, members). `depends` walks the hierarchy **upward** — base classes and interfaces a type inherits. `implements` walks **downward** — finding all types that implement an interface or extend a base class.

## Preconditions

Isolated session with cached packages. Offline mode ensures no unexpected network dependencies.

```bash
export DOTNET_INSPECT_ISOLATED=type-shape-implements
export DOTNET_INSPECT_OFFLINE=1
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
DOTNET_INSPECT_OFFLINE=0 dotnet-inspect System.CommandLine@2.0.3 -v:q
```

## 1. View type shape

> Goal: See inheritance, interfaces, and all member signatures in a tree view.

### 1a. Class with inheritance and interfaces

```prompt
What does the Command type look like? Show its shape.
```

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

### 1b. Static class

```bash
dotnet-inspect type System.Text.Json JsonSerializer --shape -n 15
```

```expect
├─ Inherits
│  └─ System.Object
├─ Properties
└─ Methods
```

### 1c. Struct

```bash
dotnet-inspect type System.Text.Json JsonElement --shape -n 10
```

```expect
├─ Inherits
│  └─ System.ValueType
├─ Properties
└─ Methods
```

## 2. View shape for types with many interfaces

> Goal: Types like WebApplication implement many interfaces — shape shows them all.

```prompt
What interfaces does WebApplication implement?
```

```bash
dotnet-inspect type Microsoft.AspNetCore.Builder.WebApplication --shape -n 15
```

```expect
├─ Inherits
│  └─ System.Object
├─ Implements
│  ├─ Microsoft.Extensions.Hosting.IHost
│  ├─ System.IDisposable
│  ├─ Microsoft.AspNetCore.Builder.IApplicationBuilder
│  ├─ Microsoft.AspNetCore.Routing.IEndpointRouteBuilder
│  └─ System.IAsyncDisposable
├─ Properties
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
# Stream
├─ System.MarshalByRefObject
├─ System.IAsyncDisposable
└─ System.IDisposable
```

### 3b. Class with deep interface hierarchy

```prompt
What is the full type hierarchy for WebApplication?
```

```bash
dotnet-inspect depends WebApplication -v:q
```

```expect
# WebApplication
├─ Microsoft.Extensions.Hosting.IHost
│  └─ System.IDisposable
├─ Microsoft.AspNetCore.Builder.IApplicationBuilder
├─ Microsoft.AspNetCore.Routing.IEndpointRouteBuilder
└─ System.IAsyncDisposable
```

### 3c. NuGet package type

```bash
dotnet-inspect depends Command --package System.CommandLine -v:q
```

```expect
# Command
├─ System.CommandLine.Symbol
└─ System.Collections.IEnumerable
```

### 3d. Generic numeric type (deep hierarchy)

```prompt
What interfaces does Int128 implement?
```

```bash
dotnet-inspect depends Int128 -n 15
```

```expect
# Int128
├─ System.Numerics.IBinaryInteger<System.Int128>
│  ├─ System.Numerics.IBinaryNumber<TSelf>
│  └─ System.Numerics.IShiftOperators<TSelf, int, TSelf>
├─ System.Numerics.IMinMaxValue<System.Int128>
└─ System.Numerics.ISignedNumber<System.Int128>
```

### 3e. Interface hierarchy

```bash
dotnet-inspect depends IFloatingPointIeee754 -n 10
```

```expect
# IFloatingPointIeee754
├─ System.Numerics.IExponentialFunctions<TSelf>
│  └─ System.Numerics.IFloatingPointConstants<TSelf>
│     └─ System.Numerics.INumberBase<TSelf>
```

## 4. Find implementers of a base class

> Goal: Discover all types that extend a base class across the platform.

### 4a. Default scope

```prompt
What types extend Stream?
```

```bash
dotnet-inspect implements Stream -v:q
```

```expect
# Types Implementing Stream
extends
```

```query
grep -oE 'Matches: [0-9]+'
```

### 4b. Limited results

```bash
dotnet-inspect implements Stream -t 3 -v:q
```

```expect
# Types Implementing Stream
Matches: 3
extends
```

## 5. Find implementers of an interface

> Goal: Discover types that implement a specific interface.

### 5a. Interface with few implementers

```prompt
What types implement IHost?
```

```bash
dotnet-inspect implements IHost -v:q
```

```expect
# Types Implementing IHost
Matches: 1
Microsoft.AspNetCore.Builder.WebApplication | class | implements
```

### 5b. Interface with many implementers

```bash
dotnet-inspect implements IDisposable -v:q --platform -t 5
```

```expect
# Types Implementing IDisposable
Matches: 5
implements
```

## 6. Find implementers in a specific scope

> Goal: Narrow the search to platform assemblies or specific packages.

### 6a. Platform only

```bash
dotnet-inspect implements IJsonTypeInfoResolver --platform -v:q
```

```expect
# Types Implementing IJsonTypeInfoResolver
Matches: 2
System.Text.Json.Serialization.JsonSerializerContext | class | implements
System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver | class | implements
```

## 7. Oneline output for scripting

> Goal: Columnar output suitable for piping and filtering.

### 7a. With header

```bash
dotnet-inspect implements Stream --oneline -t 3
```

```expect
TYPE
KIND
RELATIONSHIP
LIBRARY
SOURCE
```

### 7b. Without header for piping

```bash
dotnet-inspect implements Stream --oneline --no-header -t 3
```

```expect
class
extends
```

```expect-not
TYPE
KIND
```

```query
wc -l | tr -d ' '
```

## 8. Shape → depends → implements workflow

> Goal: Start with `type --shape` to see structure, use `depends` to walk the hierarchy upward, then use `implements` to find sibling types.

### 8a. Discover interfaces via shape

```prompt
What interfaces does Command implement, and what other types implement those interfaces?
```

```bash
dotnet-inspect type --package System.CommandLine Command --shape -n 8
```

```expect
├─ Implements
│  └─ System.Collections.IEnumerable
```

### 8b. Walk hierarchy upward

```bash
dotnet-inspect depends Command --package System.CommandLine -v:q
```

```expect
# Command
├─ System.CommandLine.Symbol
└─ System.Collections.IEnumerable
```

### 8c. Find types implementing the same interface

```bash
dotnet-inspect implements IEnumerable -t 5 -v:q
```

```expect
# Types Implementing IEnumerable
Matches: 5
implements
```
