# Accessing Platform Components

This document explains how to access .NET platform (SDK) libraries and compare them with NuGet package versions using dotnet-inspect.

## Overview

Some types exist in **two places**:

1. **NuGet packages** - Published to nuget.org, downloaded on demand
2. **Platform libraries** - Installed with the .NET SDK in `packs/` directory

For example, `JsonSerializer` ships in both:

- The `System.Text.Json` NuGet package
- The .NET runtime (as part of the SDK)

## Using `find` to Discover Types

The `find` command searches across both packages and platform libraries simultaneously:

```bash
# Search for JsonSerializer in both platform and package
dnx dotnet-inspect -y -- find JsonSerializer --framework runtime --package System.Text.Json
```

Output:

```text
# Find: JsonSerializer

**Matches:** 2

| Type | Namespace | Kind | Library | Source |
|------|-----------|------|----------|--------|
| JsonSerializer | System.Text.Json | class | System.Text.Json | System.Text.Json@10.0.2 |
| JsonSerializer | System.Text.Json | class | System.Text.Json | runtime@10.0.1 |
```

This shows the type exists in both locations with potentially different versions.

## Package vs Platform Access

### From NuGet Package (`--package`)

```bash
dnx dotnet-inspect -y -- api JsonSerializer --package System.Text.Json
```

**Advantages:**

- Has SourceLink and embedded PDBs
- Supports `--docs` to show XML documentation from source
- Supports `--samples` to find code sample references
- Can specify exact versions with `@version` syntax

### From Platform (`--platform`)

```bash
dnx dotnet-inspect -y -- api JsonSerializer --platform System.Text.Json
```

**Advantages:**

- No download required (uses local SDK)
- Faster for quick lookups
- Shows what's actually installed

**Current limitations:**

- Reference assemblies lack PDBs, so no SourceLink
- `--docs` and `--samples` require source access via SourceLink

## Documentation Access

### Package Source (Full Support)

When using `--package`, the tool can fetch documentation from source files via SourceLink:

```bash
dnx dotnet-inspect -y -- api JsonSerializer --package System.Text.Json --docs
```

This retrieves the `///` XML doc comments directly from the source repository.

### Platform Source (Automatic Fallback)

Platform libraries in the `packs/` directory include XML documentation files alongside the DLLs:

```text
/usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.1/ref/net10.0/
├── System.Text.Json.dll
├── System.Text.Json.xml    ← XML docs available here
└── ...
```

The tool automatically falls back to these XML files when SourceLink/MSDL isn't available:

```bash
# Automatic fallback to XML docs when MSDL symbols aren't available
dnx dotnet-inspect -y -- api JsonSerializer --platform System.Text.Json --docs
```

You can also force local XML docs with `--use-local-docs` for faster offline access:

```bash
# Use local XML docs directly (skip MSDL lookup)
dnx dotnet-inspect -y -- api JsonSerializer --platform System.Text.Json --use-local-docs
```

| Option | Behavior |
| ------ | -------- |
| `--docs` | Try MSDL/SourceLink first, fall back to XML |
| `--use-local-docs` | Use XML docs directly (faster, works offline, implies --docs) |

## Sample References

Sample references are extracted from `<code source="...">` and `<seealso href="...">` tags in XML doc comments:

```bash
dnx dotnet-inspect -y -- api JsonSerializer --package System.Text.Json --samples
```

This requires SourceLink access, so it only works with `--package`, not `--platform`.

## Comparing Package and Platform Versions

You can compare what's in your installed SDK vs what's on nuget.org:

```bash
# Check platform version
dnx dotnet-inspect -y -- find JsonSerializer --framework runtime

# Check latest package version
dnx dotnet-inspect -y -- package System.Text.Json --versions -n 1
```

The versions may differ - the SDK ships with a specific version, while nuget.org may have newer releases.

## Framework Selection

Platform libraries are organized by framework:

| Short Name | Framework Pack | Contents |
| ---------- | -------------- | -------- |
| `runtime` | Microsoft.NETCore.App.Ref | Core runtime (BCL) |
| `aspnetcore` | Microsoft.AspNetCore.App.Ref | ASP.NET Core |
| `netstandard` | NETStandard.Library.Ref | .NET Standard |

Use `--framework` with `find` or specify the framework with `--platform`:

```bash
# Search the runtime framework
dnx dotnet-inspect -y -- find "*Logger*" --framework runtime

# Search ASP.NET Core
dnx dotnet-inspect -y -- find "*Controller*" --framework aspnetcore
```

## When to Use Each

| Scenario | Use |
| -------- | --- |
| Quick API lookup | `--platform` |
| Need documentation | `--platform --docs` or `--package --docs` |
| Need source URLs | `--package` |
| Need code samples | `--package --samples` |
| Specific version | `--package Name@version` |
| Offline/no network | `--platform --use-local-docs` |
| Discover type location | `find --framework --package` |

## Platform Directory Structure

The tool locates platform libraries via the SDK installation:

```bash
# Find dotnet installation
which dotnet
# /usr/lib/dotnet/dotnet

# Packs directory contains reference assemblies
ls /usr/lib/dotnet/packs/
# Microsoft.NETCore.App.Ref/
# Microsoft.AspNetCore.App.Ref/
# ...

# Each pack has versioned ref assemblies
ls /usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.1/ref/net10.0/
# System.Text.Json.dll
# System.Text.Json.xml
# ...
```

The `platform` command lists all available frameworks and their installed versions:

```bash
dnx dotnet-inspect -y -- platform
```
