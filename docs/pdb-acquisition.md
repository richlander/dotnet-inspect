# PDB Acquisition

This document describes how dotnet-inspect locates and downloads PDB (Program Database) files to enable SourceLink resolution and source code navigation.

## Overview

PDBs contain debug information that maps compiled code back to source. For SourceLink to work, we need access to a **Portable PDB** that contains the SourceLink JSON document.

## PDB Formats

### Portable PDB

- Cross-platform format introduced with .NET Core
- Magic header: `BSJB` (first 4 bytes)
- Contains SourceLink information
- Identified in PE files by CodeView entry with `MinorVersion == 0x504d` ("PM" for Portable Metadata)

> **Trivia**: BSJB are initials from the original CLR team: Brian, Susan, Jason, and Bill. Bill was the metadata developer—of course, management goes first and the developer goes last. This follows the tradition of `MZ` (Mark Zbikowski) in DOS/PE headers found in the same binaries.

### Windows PDB

- Legacy format, Windows-only tooling
- Magic header: `Microsoft C/C++ MSF 7.00`
- Cannot be read by System.Reflection.Metadata
- Still used for native image PDBs (`.ni.pdb`) in Windows R2R builds

## PDB Location Strategy

The tool searches for PDBs in this order:

### 1. Embedded PDB

Check if the assembly has an embedded PDB (stored inside the PE file itself). This is the most reliable option as no external lookup is needed.

### 2. Standalone PDB

Look for a `.pdb` file next to the assembly with the same base name. Common when debugging locally.

### 3. Symbol Package (.snupkg)

For NuGet packages, download the corresponding `.snupkg` from:

- `https://globalcdn.nuget.org/symbol-packages/{id}.{version}.snupkg`
- `https://api.nuget.org/v3-flatcontainer/{id}/{version}/{id}.{version}.snupkg`

### 4. Symbol Servers

Query symbol servers using the CodeView GUID and age:

- **NuGet**: `https://symbols.nuget.org/download/symbols/{pdbname}/{key}/{pdbname}`
- **MSDL**: `https://msdl.microsoft.com/download/symbols/{pdbname}/{key}/{pdbname}`

The symbol key format differs by PDB type:

- Portable PDB: `{GUID}FFFFFFFF`
- Windows PDB: `{GUID}{age:x}`

## CodeView Debug Directory

The PE file's debug directory contains CodeView entries that provide:

- **Path**: Original PDB filename (e.g., `System.Text.Json.pdb`)
- **GUID**: Unique identifier for this build
- **Age**: Build counter (always 1 for Portable PDBs)
- **MinorVersion**: `0x504d` indicates Portable PDB format

### Multiple CodeView Entries

**Important**: Some assemblies have multiple CodeView entries. Windows ReadyToRun (R2R) assemblies typically have two:

1. **Native Image PDB** (`.ni.pdb`) - Windows PDB format, different GUID
2. **Original PDB** - Portable PDB format, original GUID

We iterate through all CodeView entries and **prefer the Portable PDB entry** (identified by `MinorVersion == 0x504d`). This ensures we use the correct GUID when querying symbol servers.

Example from a Windows R2R build:

```text
CodeView Entry 1: System.Text.Json.ni.pdb (MinorVersion: 0x0000, Windows PDB)
CodeView Entry 2: System.Text.Json.pdb    (MinorVersion: 0x504d, Portable PDB) ← use this
```

## Microsoft vs Third-Party Assemblies

### Microsoft Platform Assemblies

- Built by Microsoft from dotnet/runtime
- Published to MSDL symbol server
- SourceLink URLs point to `raw.githubusercontent.com/dotnet/runtime/...`

### Distro Builds (Canonical, Red Hat, etc.)

- Rebuilt from source by Linux distributions
- SourceLink typically disabled during rebuild
- Same metadata (Company: "Microsoft Corporation") but no symbols on MSDL
- Detected by: symbols not found on any server

### Third-Party NuGet Packages

- May publish `.snupkg` to NuGet.org
- May publish to NuGet symbol server
- Quality varies by publisher

## Caching

Downloaded PDBs are cached locally to avoid repeated downloads:

- **Symbol packages**: `~/.dotnet-inspect/symbols/{package}/{version}/{filename}.pdb`
- **Symbol server**: `~/.dotnet-inspect/symbols/{pdbname}/{key}/{pdbname}`

## Error Handling

When PDB acquisition fails, we report the reason:

- **"Windows PDB"**: Found a PDB but it's Windows format (unreadable)
- **"no symbols"**: No PDB found on any server (distro build, private package, etc.)
- **"embedded"**: PDB is embedded in the assembly (success case)
- **"msdl.microsoft.com"**: Downloaded from Microsoft symbol server (success case)

## Related Resources

- [Portable PDB Specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md)
- [Symbol Server Protocol](https://github.com/dotnet/symstore)
- [SourceLink](https://github.com/dotnet/sourcelink)
