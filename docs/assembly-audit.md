# Signals

The `Signals` section reports package and library observations. It is an evidence report, not a safety or trust verdict.

```bash
dotnet-inspect library System.Text.Json -S Signals
dotnet-inspect library System.Text.Json -S "Signals,SourceLink Availability,SourceLink Missing Files"
dotnet-inspect library System.Text.Json -S "SourceLink Integrity"
dotnet-inspect package System.Text.Json -S Signals
```

Cost is governed by verbosity (the cost ceiling) and explicit section selection. `library X -S Signals` reports metadata/provenance signals and acquires a missing library PDB to resolve SourceLink. The per-source-file reachability pass (the `SourceLink Availability` and `SourceLink Missing Files` sections, which issue one HTTP HEAD per tracked source URL) is selected explicitly via `-S`. It does not run in a plain `library X -v:d` flow, because its cost scales with source-file count. The exhaustive content check — downloading every tracked source file and comparing its hash to the PDB checksum — is the opt-in `SourceLink Integrity` section, selected explicitly via `library X -S "SourceLink Integrity"`; it never runs in a default flow and exits non-zero on a checksum mismatch. For packages, `Signals` is opt-in and includes registry-backed signals.

## Build Audit Fields

### Deterministic

Whether the library was built with deterministic compilation, meaning the same source produces identical binaries.

| Value | Meaning |
| ----- | ------- |
| ✓ | Deterministic build - reproducible output |
| ✗ | Non-deterministic - may vary between builds |

**Why it matters:** Deterministic builds enable binary verification and reproducible builds.

### Reproducible Flag

Whether the library has the reproducible build flag set in its PE header.

| Value | Meaning |
| ----- | ------- |
| ✓ | Reproducible flag is set |
| ✗ | Flag not set |

**Why it matters:** The reproducible flag indicates the build system intended for reproducibility.

### SourceLink

Whether SourceLink metadata is available, enabling navigation to exact source code.

| Value | Meaning |
| ----- | ------- |
| ✓ | SourceLink available - can navigate to source |
| ✗ (Windows PDB) | PDB found but in Windows format (not readable) |
| ✗ (no symbols) | No PDB found on symbol servers |
| ✗ | SourceLink not available for other reasons |

**Why it matters:** SourceLink connects compiled code to its exact source revision.

### Builder

For libraries branded as Microsoft (Company = "Microsoft Corporation"), indicates whether we could verify the build came from Microsoft.

| Value | Meaning |
| ----- | ------- |
| Microsoft | Verified via Microsoft symbol server (MSDL) |
| Unknown | Could not verify - symbols not found on Microsoft servers |
| *(not shown)* | Non-Microsoft library - see Company field instead |

**Why it matters:** Linux distributions rebuild .NET from source. These builds have Microsoft branding in metadata but aren't the official Microsoft binaries. The Builder field helps distinguish them.

**How verification works:**

- Microsoft publishes symbols to `msdl.microsoft.com`
- If we can download the PDB (matching the library's GUID) and extract SourceLink, it's verified
- If symbols aren't found, we can't verify who built it

## PDB Section Fields

### Format

The debug symbol format.

| Value | Meaning |
| ----- | ------- |
| Portable | Cross-platform Portable PDB (readable) |
| Windows | Legacy Windows PDB format (not supported) |
| Unknown | Could not determine format |

### Location

Where the PDB was found.

| Value | Meaning |
| ----- | ------- |
| Embedded | PDB embedded in the library itself |
| Standalone | PDB file next to the library |
| Symbol Package | Downloaded from symbol server or .snupkg |
| Unknown | PDB not found |

### Server

The symbol server that provided the PDB.

| Value | Meaning |
| ----- | ------- |
| msdl.microsoft.com | Microsoft symbol server |
| symbols.nuget.org | NuGet symbol server |
| nuget.org | NuGet symbol package (.snupkg) |
| *(not shown)* | Local or embedded PDB |

## Data Sources

dotnet-inspect pulls information from multiple sources:

| Data | Source |
| ---- | ------ |
| Library metadata | PE file headers and custom attributes |
| PDB / SourceLink | Embedded PDB, local .pdb files, symbol servers |
| Package metadata | NuGet.org API |
| Version verification | Microsoft symbol server (MSDL) |

## Common Scenarios

### Microsoft Official Build

```text
| SourceLink | ✓ |
| Builder | Microsoft |
| Server | msdl.microsoft.com |
```

Symbols downloaded from Microsoft, SourceLink verified.

### Distro Build (Ubuntu, Fedora, etc.)

```text
| SourceLink | ✗ (no symbols) |
| Builder | Unknown |
```

Library says "Microsoft Corporation" but symbols aren't on Microsoft servers. Rebuilt by distribution maintainers.

### NuGet Package

```text
| SourceLink | ✓ |
| Server | nuget.org |
```

Symbols from NuGet symbol package. Builder field not shown (not a Microsoft-branded library).

### Local/Development Build

```text
| SourceLink | ✗ |
| Location | Standalone |
```

PDB found locally but no SourceLink configured.
