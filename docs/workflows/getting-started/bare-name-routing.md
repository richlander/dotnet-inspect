---
id: bare-name-routing
description: How bare names route to platform vs NuGet based on assembly overlap
commands: [type, package]
areas: [routing, resolution, platform]
---

# Bare Name Routing

> Bare names (no `package` or `library` prefix) are routed based on whether the name overlaps with the .NET platform. Libraries like `System.Text.Json` that ship in both platform and NuGet default to **platform** as a bare name. The `package` command forces a NuGet lookup.

## Preconditions

Isolated session.

```bash
export DOTNET_INSPECT_ISOLATED=bare-name-routing
```

```bash
dotnet-inspect cache clear
```

## 1. Platform library as bare name

> Goal: A platform-overlapping name like `System.Text.Json` resolves to the platform assembly, not the NuGet package. Even with a version tag, the bare name routes to platform.

### 1a. Using bare name

```prompt
What library is System.Text.Json and where does it come from?
```

```bash
dotnet-inspect System.Text.Json -v:q
```

```expect
Source: Platform
```

```query
grep -o 'Source: [A-Za-z]*'
```

### 1b. Using bare name with version

```prompt
What is System.Text.Json 6.0.0?
```

```bash
dotnet-inspect System.Text.Json@6.0.0 -v:q
```

```expect
Source: Platform
```

```query
grep -o 'Source: [A-Za-z]*'
```

### 1c. Using `type`

```prompt
List the types in System.Text.Json.
```

```bash
dotnet-inspect type System.Text.Json -v:q
```

```expect
Source: Platform
Library: System.Text.Json.dll
```

```query
grep -o 'Source: [A-Za-z]*'
grep -o 'Library: [A-Za-z.]*'
grep -oE 'Types: [0-9]+'
grep -oE 'Methods: [0-9]+'
grep -oE 'Properties: [0-9]+'
```

## 2. Platform library forced to package

> Goal: The `package` command or `type --package` flag explicitly forces NuGet resolution, even for a platform name (if a package exists).

### 2a. Using `package`

```prompt
Show me the NuGet package for System.Text.Json, not the platform version.
```

```bash
dotnet-inspect package System.Text.Json@6.0.0 -v:q
```

```expect
Version: 6.0.0
Source: NuGet
```

```query
grep -o 'Source: [A-Za-z]*'
grep -oE 'Version: [0-9.]+'
```

### 2b. Using `type --package`

```prompt
List the types in the System.Text.Json 6.0.0 NuGet package.
```

```bash
dotnet-inspect type --package System.Text.Json@6.0.0 -v:q
```

```expect
Source: NuGet
```

```query
grep -o 'Source: [A-Za-z]*'
```

## 3. Non-platform package as bare name

> Goal: A name that only exists on NuGet (not in the platform) routes to the package automatically.

### 3a. Using bare name

```prompt
What is System.CommandLine?
```

```bash
dotnet-inspect System.CommandLine@2.0.3 -v:q
```

```expect
Version: 2.0.3
Source: NuGet
```

```query
grep -o 'Source: [A-Za-z]*'
grep -oE 'Version: [0-9.]+'
```

### 3b. Using `type`

```prompt
What types are in System.CommandLine 2.0.3?
```

```bash
dotnet-inspect type System.CommandLine@2.0.3 -v:q
```

```expect
Source: NuGet
```

```query
grep -o 'Source: [A-Za-z]*'
```

## 4. Non-platform package with explicit command

> Goal: The `package` command or `type --package` flag for a NuGet-only name produces the same result as the bare name.

### 4a. Using `package`

```prompt
Show me the System.CommandLine 2.0.3 package details.
```

```bash
dotnet-inspect package System.CommandLine@2.0.3 -v:q
```

```expect
Version: 2.0.3
Source: NuGet
```

```query
grep -o 'Source: [A-Za-z]*'
grep -oE 'Version: [0-9.]+'
```

### 4b. Using `type --package`

```prompt
List types in the System.CommandLine 2.0.3 package explicitly.
```

```bash
dotnet-inspect type --package System.CommandLine@2.0.3 -v:q
```

```expect
Source: NuGet
```

```query
grep -o 'Source: [A-Za-z]*'
```

## 5. Fully qualified type name as bare name

> Goal: A dotted name that doesn't match an assembly is treated as a fully qualified type name and routed to the platform type page.

```prompt
Tell me about System.Text.Json.JsonSerializer.
```

```bash
dotnet-inspect System.Text.Json.JsonSerializer -v:q
```

```expect
# System.Text.Json.JsonSerializer
Kind: class
Source: Platform
```

```query
grep '# System.Text.Json.JsonSerializer'
grep -o 'Kind: [a-z]*'
grep -o 'Source: [A-Za-z]*'
```

## 6. Fully qualified type name forced to package

> Goal: A fully qualified type name with `--package` resolves against the NuGet package instead of the platform.

```prompt
Show me JsonSerializer from the System.Text.Json 6.0.0 NuGet package.
```

```bash
dotnet-inspect type --package System.Text.Json@6.0.0 JsonSerializer -v:q
```

```expect
# System.Text.Json.JsonSerializer
Kind: class
Source: NuGet
```

```query
grep '# System.Text.Json.JsonSerializer'
grep -o 'Kind: [a-z]*'
grep -o 'Source: [A-Za-z]*'
```
