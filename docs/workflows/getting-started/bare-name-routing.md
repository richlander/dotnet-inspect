---
id: bare-name-routing
description: How bare names route to platform vs NuGet based on assembly overlap
commands: [type, package]
areas: [routing, resolution, platform]
---

# Bare Name Routing

> Bare names (no `package` or `library` prefix) are routed based on whether the name overlaps with the .NET platform. Libraries like `System.Text.Json` that ship in both platform and NuGet default to **platform** as a bare name. The `package` command forces a NuGet lookup.

## Preconditions

Isolated session with cached packages. Offline mode ensures no unexpected network dependencies.

```bash
export DOTNET_INSPECT_ISOLATED=bare-name-routing
export DOTNET_INSPECT_OFFLINE=1
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
DOTNET_INSPECT_OFFLINE=0 dotnet-inspect System.CommandLine -v:q
```

## 1. Platform library as bare name

> Goal: A platform-overlapping name like `System.Text.Json` resolves to the platform assembly, not the NuGet package.

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

### 1b. Using `type`

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
dotnet-inspect package System.Text.Json -v:q
```

```expect
Source: NuGet
```

```query
grep -o 'Source: [A-Za-z]*'
```

### 2b. Using `type --package`

```bash
dotnet-inspect type --package System.Text.Json -v:q
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
dotnet-inspect System.CommandLine -v:q
```

```expect
Source: NuGet
```

```query
grep -o 'Source: [A-Za-z]*'
```

### 3b. Using `type`

```bash
dotnet-inspect type System.CommandLine -v:q
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

```bash
dotnet-inspect package System.CommandLine -v:q
```

```expect
Source: NuGet
```

```query
grep -o 'Source: [A-Za-z]*'
```

### 4b. Using `type --package`

```bash
dotnet-inspect type --package System.CommandLine -v:q
```

```expect
Source: NuGet
```

```query
grep -o 'Source: [A-Za-z]*'
```

## 5. Fully qualified type name as bare name

> Goal: A dotted name that doesn't match an assembly is treated as a fully qualified type name and routed to the platform type page.

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
