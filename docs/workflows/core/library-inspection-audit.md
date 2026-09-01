---
id: library-inspection-audit
description: Inspect .NET libraries for metadata, symbols, references, and SourceLink audit
commands: [library]
areas: [library, metadata, sourcelink, audit, dependencies, unsafe, pinvoke, resources]
---

# Library Inspection and Audit

> Inspect .NET library files to view metadata, symbols, references, dependencies, and perform SourceLink verification. The `library` command provides deep inspection of assembly metadata, PDB information, and provenance verification.

## Preconditions

Named isolated session ensures reproducible results (no shared state, no NuGet cache).

```bash
export DOTNET_INSPECT_ISOLATED=library-audit
```

```bash
dotnet-inspect cache clear
```

Prime the cache with test packages:

```bash
dotnet-inspect System.CommandLine@2.0.3 -v:q
```

```bash
dotnet-inspect Microsoft.Extensions.AI@9.9.1 -v:q
```

```bash
dotnet-inspect Newtonsoft.Json@13.0.3 -v:q
```

```bash
dotnet-inspect System.Drawing.Common@10.0.0 -v:q
```

## 1. View library metadata

> Goal: See assembly metadata including version, TFM, architecture, and signing info.

### 1a. Default verbosity

```prompt
Show me the metadata for the System.CommandLine library.
```

```bash
dotnet-inspect library --package System.CommandLine@2.0.3
```

```expect
# System.CommandLine.dll
## Library Info
| Field | Value |
Name
Version
Target Framework
Signed
Deterministic
```

### 1b. Quiet mode (summary only)

```bash
dotnet-inspect library --package System.CommandLine@2.0.3 -v:q
```

```expect
# System.CommandLine.dll
Name: System.CommandLine | Version: 2.0.3 | TFM: .NETCoreApp
```

```expect-not
## Library Info
```

### 1c. Platform library

```bash
dotnet-inspect library System.Text.Json -v:q
```

```expect
# System.Text.Json.dll
Source: Platform
```

Use `--version <runtime-version>` when you need a specific installed shared runtime version; dotnet-inspect searches runtime frameworks in priority order.

## 2. SourceLink audit

> Goal: Verify all source files are accessible via SourceLink URLs.

### 2a. Run source audit

```bash
dotnet-inspect library --package System.CommandLine@2.0.3 -S "Signals,SourceLink: Availability,SourceLink: Missing Files"
```

```expect
## SourceLink: Availability
| Field | Value |
Status
Source Files
available
Embedded
```

```expect-not
Tips:
```

### 2b. Audit with large file count

```bash
dotnet-inspect library --package Newtonsoft.Json@13.0.3 -S "Signals,SourceLink: Availability,SourceLink: Missing Files"
```

```expect
## SourceLink: Availability
| Field | Value |
Status
Source Files
available
```

## 3. View symbols information

> Goal: See PDB format, location, and SourceLink status.

```prompt
Does System.CommandLine have SourceLink? What PDB format does it use?
```

```bash
dotnet-inspect library --package System.CommandLine@2.0.3 -v:d -S Symbols
```

```expect
## Symbols
| Field | Value |
PDB Format
PDB Location
Source Link
Publisher
```

```expect-not
## Library Info
Tips:
```

## 4. View library references

> Goal: See what assemblies this library references.

```bash
dotnet-inspect library --package System.CommandLine@2.0.3 --references -n 40
```

```expect
## References
| Name | Version | Public Key Token |
System.Collections
System.Runtime
```

## 5. View dependency tree

> Goal: See full transitive dependency graph for a library.

```bash
dotnet-inspect library --package Microsoft.Extensions.AI@9.9.1 -S References --tree --depth 3
```

```expect
# Microsoft.Extensions.AI.dll
## References
Microsoft.Extensions.AI.Abstractions
Microsoft.Extensions.Caching.Abstractions
Microsoft.Extensions.Primitives
```

## 6. List available sections

> Goal: Discover what sections are available for a library.

```bash
dotnet-inspect library --package System.CommandLine@2.0.3 -D
```

```expect
Library Info
Symbols
Extension Methods
Resources
Custom Attributes
```

## 7. View embedded resources

> Goal: See resources embedded in the assembly.

```bash
dotnet-inspect library System.Text.Json -v:d -S Resources
```

```expect
## Resources
| Name | Visibility | Size |
SR.resources
```

## 8. View custom attributes

> Goal: See assembly-level attributes.

```bash
dotnet-inspect library --package System.CommandLine@2.0.3 -v:d -S "Custom Attributes" -n 12
```

```expect
## Custom Attributes
| Name | Target | Value |
AssemblyMetadata(IsTrimmable)
AssemblyMetadata(RepositoryUrl)
NeutralResourcesLanguage
```

```expect-not
Tips:
```

## 9. View extension methods

> Goal: See extension methods defined in the library.

```bash
dotnet-inspect library System.Text.Json -v:d -S "Extension Methods" -n 15
```

```expect
## Extension Methods
| Name | Kind | Extended Type | Class |
method
```

## 10. Select specific TFM

> Goal: Inspect a specific target framework when multiple are available.

### 10a. .NET 8 TFM

```bash
dotnet-inspect library --package System.CommandLine@2.0.3 --tfm net8.0 -v:q
```

```expect
# System.CommandLine.dll
TFM: .NETCoreApp,Version=v8.0
```

### 10b. .NET Standard TFM

```bash
dotnet-inspect library --package Newtonsoft.Json@13.0.3 --tfm netstandard2.0 -v:q
```

```expect
# Newtonsoft.Json.dll
TFM: .NETStandard,Version=v2.0
```

## 11. View type forwarders

> Goal: See type forwarding declarations in the assembly.

```bash
dotnet-inspect library System.Text.Json -v:d -S "Type Forwarders"
```

```expect
## Type Forwarders
| Type | Target Assembly |
IsExternalInit
System.Runtime
```

```expect-not
Tips:
```

## 12. View unsafe members

> Goal: See members with unsafe signatures or unsafe calls for security and interop review.

```bash
dotnet-inspect library System.Private.CoreLib -S 'Unsafe Members' -n 3
```

```expect
## Unsafe Members
| Member | Reason | Detail | Kind | IL | Token |
Unsafe call
```

## 13. View P/Invoke methods

> Goal: See native interop methods declared via DllImport/LibraryImport.

```bash
dotnet-inspect library --package System.Drawing.Common@10.0.0 -S 'P/Invoke Methods' -n 3
```

```expect
## P/Invoke Methods
| Name | Declaring Type | Module | Signature |
shell32.dll
```

## 14. Extract embedded resources

> Goal: Extract embedded resources from an assembly to a directory on disk.

```setup
rm -rf artifacts/workflows/library-inspection-resources
```

```bash
dotnet-inspect library --package System.CommandLine@2.0.3 \
  --extract-resources artifacts/workflows/library-inspection-resources
test -f artifacts/workflows/library-inspection-resources/System.CommandLine.Properties.Resources.resources
```

```expect
## Library Info
```

```expect-stderr
Extracted
.resources
```

## 15. JSON output for tooling

> Goal: Get library metadata in machine-readable JSON format.

```bash
dotnet-inspect library --package System.CommandLine@2.0.3 --json
```

```expect
{
  "file_name": "System.CommandLine.dll",
  "file_type": "dll",
  "pdb_format": "Portable",
  "pdb_location": "Standalone",
```
