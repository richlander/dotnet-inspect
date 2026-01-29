# Request: Publish Portable PDBs with SourceLink for all packages

## Summary

All NuGet packages should publish **Portable PDBs** (not Windows PDBs) with **SourceLink** information, either embedded in the assembly or in a `.snupkg` symbol package. This enables cross-platform tooling and improves the developer experience across the .NET ecosystem.

## Background

There are two PDB formats in the .NET ecosystem:

| Format | Header | Cross-Platform | Tooling Support |
|--------|--------|----------------|-----------------|
| **Portable PDB** | `BSJB` | Yes - readable with `System.Reflection.Metadata` | Full managed code support |
| **Windows PDB** | `Microsoft C/C++ MSF 7.00` | No - requires native DiaSymReader (Windows-only COM component) | Requires native interop or Windows |

## The Problem

Currently, some Microsoft packages (including ASP.NET Core packages like `Microsoft.AspNetCore.Authentication.JwtBearer`) publish **Windows PDBs** to the Microsoft symbol server instead of Portable PDBs. This creates several problems:

### 1. Cross-platform tools cannot read the debug information

Tools written in managed .NET code using `System.Reflection.Metadata` cannot read Windows PDBs. The only way to read Windows PDBs from non-Windows platforms is to:

- Use native interop with DiaSymReader (Windows-only)
- Convert Windows PDBs to Portable PDBs using `Microsoft.DiaSymReader.Converter` (also requires Windows)

### 2. Even NuGet Package Explorer struggles with this

[NuGet Package Explorer](https://github.com/NuGetPackageExplorer/NuGetPackageExplorer) - the canonical tool for inspecting NuGet packages - has to jump through significant hoops to handle Windows PDBs:

```csharp
// From AssemblyDebugParser.cs
if (!PdbConverter.IsPortable(pdbStream))
{
    if (!AppCompat.IsSupported(RuntimeFeature.DiaSymReader))
        throw new PlatformNotSupportedException("Windows PDB cannot be processed on this platform.");

    // Full PDB - convert to Portable PDB in memory
    PdbConverter.Default.ConvertWindowsToPortable(peStream, pdbStream, _temporaryPdbStream);
}
```

And DiaSymReader is explicitly Windows-only:

```csharp
// From AppCompat.cs
RuntimeFeature.DiaSymReader => IsWindows,  // Only supported on Windows
```

This means the web version of NPE (nuget.info) running in WebAssembly cannot fully validate SourceLink for packages with Windows PDBs.

### 3. LLM-powered developer tools need SourceLink

Modern development increasingly involves LLM-powered tools that can:

- Navigate to source code for types and methods
- Read XML documentation comments from source files
- Understand implementation details for better code suggestions
- Provide contextual help based on actual library source code

**SourceLink is the key that makes this possible.** It maps PDB document paths to URLs in source control (GitHub, Azure DevOps, etc.), allowing tools to fetch the exact source code for a specific package version.

Without readable PDBs containing SourceLink:
- Tools cannot resolve source URLs
- XML doc comments in source files are inaccessible
- Developers lose the ability to "go to definition" in the actual source

### 4. Example: What cross-platform tools see today

Using [dotnet-inspect](https://github.com/richlander/dotnet-inspect), a cross-platform tool for inspecting .NET assemblies:

#### Embedded Portable PDB (Markout) - Works

```text
$ dotnet-inspect assembly --package Markout --audit

## Build Audit

| Check | Status |
|-------|--------|
| Deterministic | ✓ |
| Reproducible Flag | ✓ |
| SourceLink | ✓ |

## PDB

| Property | Value |
|----------|-------|
| Format | Portable |
| Location | Embedded |
| Path | Markout.pdb |
```

#### Symbol Package / snupkg (Newtonsoft.Json) - Works

```text
$ dotnet-inspect assembly --package Newtonsoft.Json --tfm net6.0 --audit

## Build Audit

| Check | Status |
|-------|--------|
| Deterministic | ✓ |
| Reproducible Flag | ✓ |
| SourceLink | ✓ |

## PDB

| Property | Value |
|----------|-------|
| Format | Portable |
| Location | Symbol Package |
| Path | /_/Src/Newtonsoft.Json/obj/Release/net6.0/Newtonsoft.Json.pdb |
```

#### Windows PDB on Symbol Server (Microsoft.AspNetCore.Authentication.JwtBearer) - Fails

```text
$ dotnet-inspect assembly --package Microsoft.AspNetCore.Authentication.JwtBearer --audit

## Build Audit

| Check | Status |
|-------|--------|
| Deterministic | ✓ |
| Reproducible Flag | ✓ |
| SourceLink | ✗ |

## PDB

| Property | Value |
|----------|-------|
| Format | Windows |
| Location | Unknown |
| Path | /_/src/aspnetcore/artifacts/obj/.../Microsoft.AspNetCore.Authentication.JwtBearer.pdb |

*Path is from the CodeView record in the assembly; actual PDB location is unknown.*

**Note:** Windows PDB format is not supported by this tool.
Only Portable PDBs (embedded or in .snupkg) can be read.
Consider asking the package maintainer to publish Portable PDBs.
```

The first two packages work perfectly - cross-platform tools can read their debug information and resolve source URLs. The third package publishes a Windows PDB to the Microsoft symbol server, making it inaccessible to managed code tools.

## The Solution

All packages should:

1. **Use Portable PDB format** - set `<DebugType>portable</DebugType>` or `<DebugType>embedded</DebugType>`
2. **Include SourceLink** - reference the appropriate SourceLink package (e.g., `Microsoft.SourceLink.GitHub`)
3. **Publish symbols** via one of:
   - Embedded PDBs (`<DebugType>embedded</DebugType>`) - simplest, symbols travel with the assembly
   - Symbol packages (`.snupkg`) uploaded to NuGet.org - keeps assembly size smaller

## Benefits

- **Cross-platform tooling**: Any tool on any platform can read debug information
- **LLM integration**: AI assistants can access source code and documentation
- **Better debugging**: Portable PDBs work consistently across all platforms
- **Smaller ecosystem friction**: No need for Windows-specific workarounds
- **Future-proof**: Portable PDB is the modern, supported format going forward

## References

- [Portable PDB Specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md)
- [SourceLink Documentation](https://github.com/dotnet/sourcelink)
- [NuGet Symbol Packages](https://docs.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg)
- [NuGet Package Explorer source showing Windows PDB limitations](https://github.com/NuGetPackageExplorer/NuGetPackageExplorer/blob/main/Core/AssemblyMetadata/AssemblyDebugParser.cs)
