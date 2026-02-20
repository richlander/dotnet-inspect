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

## 1. View library metadata

> Goal: See assembly metadata including version, TFM, architecture, and signing info.

### 1a. Default verbosity

```prompt
Show me the metadata for the System.CommandLine library.
```

```bash
dotnet-inspect library --package System.CommandLine
```

```expect
# System.CommandLine.dll
## Library Info
| Property | Value |
Name
Version
Target Framework
Signed
Deterministic
```

### 1b. Quiet mode (summary only)

```bash
dotnet-inspect library --package System.CommandLine -v:q
```

```expect
# System.CommandLine.dll (net8.0)
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

## 2. SourceLink audit

> Goal: Verify all source files are accessible via SourceLink URLs.

### 2a. Run full audit

```bash
dotnet-inspect library --package System.CommandLine --source-link-audit
```

```expect
## Source Link Audit
| Property | Value |
Status
files accessible
Embedded
```

```expect-not
Tips:
```

### 2b. Audit with large file count

```bash
dotnet-inspect library Newtonsoft.Json --source-link-audit
```

```expect
## Source Link Audit
| Property | Value |
Status
files accessible
```

## 3. View symbols information

> Goal: See PDB format, location, and SourceLink status.

```prompt
Does System.CommandLine have SourceLink? What PDB format does it use?
```

```bash
dotnet-inspect library --package System.CommandLine -v:d -s Symbols
```

```expect
## Symbols
| Property | Value |
PDB Format
PDB Location
SourceLink
Builder
Publisher
```

```expect-not
## Library Info
Tips:
```

## 4. View library references

> Goal: See what assemblies this library references.

```bash
dotnet-inspect library --package System.CommandLine --references -n 40
```

```expect
## Library References
| Name | Version | Public Key Token |
System.Collections
System.Runtime
```

## 5. View dependency tree

> Goal: See full transitive dependency graph for a library.

```bash
dotnet-inspect library --package Microsoft.Extensions.AI --dependencies -n 25
```

```expect
# Microsoft.Extensions.AI.dll
├─ Microsoft.Extensions.AI.Abstractions
├─ Microsoft.Extensions.Caching.Abstractions
│  └─ Microsoft.Extensions.Primitives
```

## 6. List available sections

> Goal: Discover what sections are available for a library.

```bash
dotnet-inspect library --package System.CommandLine -s
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
dotnet-inspect library System.Text.Json -v:d -s Resources
```

```expect
## Resources
| Name | Visibility | Size |
SR.resources
```

## 8. View custom attributes

> Goal: See assembly-level attributes.

```bash
dotnet-inspect library --package System.CommandLine -v:d -s "Custom Attributes" -n 12
```

```expect
## Custom Attributes
| Name | Target | Value |
Extension
CLSCompliant
NeutralResourcesLanguage
```

```expect-not
Tips:
```

## 9. View extension methods

> Goal: See extension methods defined in the library.

```bash
dotnet-inspect library System.Text.Json -v:d -s "Extension Methods" -n 15
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
dotnet-inspect library --package System.CommandLine --tfm net8.0 -v:q
```

```expect
# System.CommandLine.dll (net8.0)
TFM: .NETCoreApp,Version=v8.0
```

### 10b. .NET Standard TFM

```bash
dotnet-inspect library --package Newtonsoft.Json --tfm netstandard2.0 -v:q
```

```expect
# Newtonsoft.Json.dll (netstandard2.0)
TFM: .NETStandard,Version=v2.0
```

## 11. View type forwarders

> Goal: See type forwarding declarations in the assembly.

```bash
dotnet-inspect library System.Text.Json -v:d -s "Type Forwarders"
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

## 12. View unsafe methods

> Goal: See methods with pointer signatures — useful for security audit and interop review.

```bash
dotnet-inspect library System.Security.Cryptography -s 'Unsafe Methods' -n 10
```

```expect
## Unsafe Methods
| Name | Declaring Type | Signature |
```

## 13. View P/Invoke methods

> Goal: See native interop methods declared via DllImport/LibraryImport.

```bash
dotnet-inspect library System.Security.Cryptography -s 'P/Invoke Methods' -n 10
```

```expect
## P/Invoke Methods
```

## 14. Extract embedded resources

> Goal: Extract embedded resources from an assembly to a directory on disk.

```bash
dotnet-inspect library System.Text.Json --extract-resources /tmp/stj-resources
```

```expect
SR.resources
```

```query
ls /tmp/stj-resources/ | head -5
```

## 15. JSON output for tooling

> Goal: Get library metadata in machine-readable JSON format.

```bash
dotnet-inspect library --package System.CommandLine --json -n 30
```

```expect
{
"file_name":
"pdb_format":
"has_source_link":
"is_deterministic":
```
