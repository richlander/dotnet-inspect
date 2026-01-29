# Request: Publish Portable PDBs with SourceLink for all packages

## Summary

In the new AI-assisted development era, a major technique is to provide LLMs with the information they need _now_ to complete a task. LLMs are often writing code in terms of packages, which are common in all ecosystems. .NET is perhaps best off since it is trivial to generate high-value API signatures from assembly metadata without needing to download external docs. [Source Link](https://github.com/dotnet/sourcelink) provides access to package source via commit-specific repo links. Source Link can provide access to the implementation and to `///` doc comments that describe functionality in additional detail. The combination of metadata and API docs can drive significant understanding, particularly with new packages that are gauranteed to not be present in training data. This approach has been observed to be a powerful antidote to breaking changes, when LLMs act on pre-break training data.

Source Link information is stored in symbol files (PDBs). A major problem is that many (all?) Microsoft packages publish Windows-specific PDBs to the Microsoft symbol server making them in accessible to cross-platform tools. This means that LLM-targeted tools that rely on source-link information are likely to provide a second-tier experience to Microsoft packages. However, Microsoft packages tend to be quite popular.

The natural conclusion is that Microsoft NuGet packages should publish **Portable PDBs** (not Windows PDBs) with **SourceLink** information, either embedded in the assembly, or in a `.snupkg` symbol package. This enables cross-platform tooling and improves the developer experience across the .NET ecosystem.

The most popular package in our ecosystem [Newtonsoft.Json](https://www.nuget.org/packages/newtonsoft.json/) publish a `.snupkg` package for symbols. That's a fine option.

## Background

There are two PDB formats in the .NET ecosystem:

| Format | Header | Cross-Platform | Tooling Support |
|--------|--------|----------------|-----------------|
| **Portable PDB** | `BSJB` | Yes - readable with `System.Reflection.Metadata` | Full managed code support |
| **Windows PDB** | `Microsoft C/C++ MSF 7.00` | No - requires native DiaSymReader (Windows-only COM component) | Requires native interop or Windows |

There is a [pdb2pdb](https://github.com/dotnet/symreader-converter) tool that converts Windows PDBs to Portable PDBs. It only works on Windows and comes with a native depednency.

## The Problem

Currently, some Microsoft packages (including ASP.NET Core packages like `Microsoft.AspNetCore.Authentication.JwtBearer`) publish **Windows PDBs** to the Microsoft symbol server instead of Portable PDBs. This creates several problems:

### 1. Cross-platform tools cannot read the debug information

Tools written using `System.Reflection.Metadata` cannot read Windows PDBs. The only way to read Windows PDBs from non-Windows platforms is to:

- Use native interop with DiaSymReader (Windows-only)
- Convert Windows PDBs to Portable PDBs using `pdb2pdb` (Windows-only)
- Use the [`pdb`](https://github.com/getsentry/pdb) Rust crate, which can parse Windows PDBs cross-platform (read-only)

### 2. Even NuGet Package Explorer struggles with this

[NuGet Package Explorer](https://github.com/NuGetPackageExplorer/NuGetPackageExplorer) - the canonical tool for inspecting NuGet packages - has to jump through significant hoops to handle Windows PDBs:

```csharp
// From AssemblyDebugParser.cs
// https://github.com/NuGetPackageExplorer/NuGetPackageExplorer/blob/4b0af799dc27af3e608770b0e9e38dc04374ad26/Core/AssemblyMetadata/AssemblyDebugParser.cs#L26-L45
if (!PdbConverter.IsPortable(pdbStream))
{
    if (!AppCompat.IsSupported(RuntimeFeature.DiaSymReader))
        throw new PlatformNotSupportedException("Windows PDB cannot be processed on this platform.");

    // Full PDB. convert to ppdb in memory
    ...
    PdbConverter.Default.ConvertWindowsToPortable(peStream, pdbStream, _temporaryPdbStream);
}
```

And DiaSymReader is explicitly Windows-only:

```csharp
// From AppCompat.cs
// https://github.com/NuGetPackageExplorer/NuGetPackageExplorer/blob/4b0af799dc27af3e608770b0e9e38dc04374ad26/Core/Utility/AppCompat.cs#L33
RuntimeFeature.DiaSymReader => IsWindows,
```

This suggests the web version of NPE (nuget.info) running in WebAssembly cannot fully validate SourceLink for packages with Windows PDBs.

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
