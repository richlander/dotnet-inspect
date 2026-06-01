# Platform Library Resolution

This document describes how dotnet-inspect resolves and uses platform libraries from the installed .NET SDK.

## Overview

The .NET SDK installs libraries in two distinct locations:

| Location | Path Pattern | Purpose | Debug Info |
| -------- | ------------ | ------- | ---------- |
| **Packs** (ref) | `packs/Microsoft.NETCore.App.Ref/{version}/ref/net{x}/` | Compilation reference assemblies | ❌ None |
| **Shared** (runtime) | `shared/Microsoft.NETCore.App/{version}/` | Runtime implementation assemblies | ✅ CodeView entries |

## When to Use Each

### Reference Assemblies (Packs)

**Use for:** API extraction, type enumeration, public surface analysis

Reference assemblies contain only public API metadata. They are:

- Smaller than runtime assemblies
- Contain all public types and signatures
- **Do not contain** implementation code, private members, or debug information
- Located in: `/usr/local/share/dotnet/packs/` (macOS/Linux) or `C:\Program Files\dotnet\packs\` (Windows)

Commands that use ref assemblies for primary data:

- `api` - Extracting public API surface
- `type` - Displaying type structure
- `find` - Searching for types
- `diff` - Comparing API surfaces

### Runtime Assemblies (Shared)

**Use for:** PDB/SourceLink resolution, full library inspection

Runtime assemblies are the actual implementation. They:

- Contain CodeView debug directory entries (GUID/age for symbol lookup)
- Are JIT-compiled or ReadyToRun
- Enable MSDL symbol server lookups
- Located in: `/usr/local/share/dotnet/shared/` (macOS/Linux) or `C:\Program Files\dotnet\shared\` (Windows)

Commands that use runtime assemblies:

- `library` - Full library inspection (always uses runtime for `--platform`)
- `audit` - library audit for platform-looking targets
- PDB/SourceLink resolution for `--docs`, `--samples`, or source URL extraction

## Hybrid Resolution Pattern

For commands that need both API information and source resolution (like `api` with SourceLink), we use a **hybrid pattern**:

1. **Resolve ref assembly** for API extraction (complete public surface)
2. **Resolve runtime assembly** for PDB lookup (has CodeView debug info)
3. Use ref assembly path for type/method discovery
4. Use runtime assembly path for symbol server queries

```text
┌─────────────────────────────────────────────────────────────────┐
│                        api --platform                           │
├─────────────────────────────────────────────────────────────────┤
│  1. Resolve ref assembly     →  Extract public types/methods    │
│  2. Resolve runtime assembly →  Query MSDL for PDB              │
│  3. Read PDB                 →  Extract SourceLink URLs         │
│  4. Combine                  →  Output API with source links    │
└─────────────────────────────────────────────────────────────────┘
```

### Why Ref Assemblies Have No Debug Info

Reference assemblies are **metadata-only** assemblies designed for compilation. The C# compiler only needs type signatures, not implementation. To minimize size and avoid confusing debuggers, ref assemblies:

- Have no method bodies (just `throw null`)
- Have no embedded PDBs
- Have no CodeView debug directory entries
- Cannot be used for symbol server lookups

### Why Runtime Assemblies Are Needed for PDB

The MSDL symbol server uses **CodeView debug information** embedded in the PE file to construct download URLs:

```text
https://msdl.microsoft.com/download/symbols/{pdbname}/{guid}FFFFFFFF/{pdbname}
```

The GUID comes from the CodeView entry in the debug directory. Without it, there's no way to query for matching symbols.

## Type Forwarders

Many runtime assemblies are **type-forwarding facades**. For example, runtime
`System.Collections.dll` does not define `List<T>` — it forwards the type to
`System.Private.CoreLib`, where the implementation (and PDB sequence points)
actually live.

| Assembly (runtime) | Type | Target |
| ------------------- | ---- | ------ |
| System.Collections | `List<T>`, `Dictionary<TKey,TValue>`, ... | System.Private.CoreLib |
| System.Runtime | `String`, `Int32`, `Task`, ... | System.Private.CoreLib |
| System.Net.Primitives | `IPAddress`, `IPEndPoint`, ... | System.Net.Primitives *(not forwarded)* |

Reference assemblies define all types as real type definitions (the compiler
needs them for compilation), so type forwarders are a **runtime-only** concern.

### Impact on Source Resolution

When the `source` command opens the runtime assembly's PDB, forwarded types
have no sequence points there — the PDB covers only the assembly's own code.
To find source links for a forwarded type, we must follow the forwarder to the
implementation assembly and open *its* PDB.

```text
source "List<T>" --platform System.Collections

1. Ref: System.Collections.dll  →  Finds List`1 in type definitions (API surface)
2. Runtime: System.Collections.dll  →  PDB has no List`1 sequence points (forwarded)
3. Follow forwarder  →  System.Private.CoreLib.dll
4. Runtime: System.Private.CoreLib.dll  →  PDB has List`1 at List.cs:line 23
```

### Metadata Primitives

The forwarder-following capability lives in the metadata layer so any command
can use it:

- **`PdbContext.FindTypeForwarder(typeName)`** — returns the target assembly
  name if a type is forwarded, null otherwise.
- **`PdbContext.ResolveImplementationAssemblyPath(typeName)`** — follows the
  forwarder and returns the full path to the target DLL (looks in the same
  directory).
- **`SourceLinkService.OpenImplementation(typeName)`** — opens a new service
  on the implementation assembly, ready for PDB acquisition.

### Design Principle

`find` uses ref packs and reports the canonical assembly name (e.g.,
`System.Collections` for `List<T>`). This preserves the user-facing .NET API
surface model. Commands that need PDBs or method bodies (`source`, `api` with
`--docs`) follow forwarders transparently at the type-resolution level.
Mixing ref and runtime data within a single concern is avoided.

## Framework Mappings

| Short Name | Ref Pack | Runtime Shared |
| ---------- | -------- | -------------- |
| `runtime` | `Microsoft.NETCore.App.Ref` | `Microsoft.NETCore.App` |
| `aspnetcore` | `Microsoft.AspNetCore.App.Ref` | `Microsoft.AspNetCore.App` |
| `netstandard` | `NETStandard.Library.Ref` | *(none - ref only)* |

Note: `netstandard` has no runtime assemblies. It's a reference-only framework that type-forwards to runtime implementations.

## Command Behavior Summary

| Command | Primary Source | PDB Source | Notes |
| ------- | -------------- | ---------- | ----- |
| `api --platform` | Ref | Runtime | Hybrid: API from ref, PDB from runtime |
| `type --platform` | Ref | Runtime | Hybrid: structure from ref, source from runtime |
| `source --platform` | Ref | Runtime (+forwarders) | Follows type forwarders to implementation assembly PDB |
| `find --platform` | Ref | *(none)* | Ref only: no PDB needed for search |
| `diff --platform` | Ref | *(none)* | Ref only: comparing public API |
| `library --platform` | Runtime | Runtime | Runtime only: full inspection with debug info |

## Implementation

The `PlatformResolver` class handles both resolution modes:

```csharp
// Resolve for API extraction (ref assembly)
var (refPath, _, _, _) = PlatformResolver.ResolveAssembly(
    "System.Text.Json",
    frameworkSpec: null,
    packsDirectory: null,
    useRuntimeAssemblies: false);  // Uses packs/

// Resolve for PDB lookup (runtime assembly)
var (runtimePath, _, _, _) = PlatformResolver.ResolveAssembly(
    "System.Text.Json",
    frameworkSpec: null,
    packsDirectory: null,
    useRuntimeAssemblies: true);   // Uses shared/
```

## Version Resolution

Both ref and runtime assemblies support version specifiers:

```bash
# Latest version (default)
dotnet-inspect api --platform System.Text.Json

# Specific shared runtime version (library/audit)
dotnet-inspect library --platform System.Text.Json --version 9.0.12

# Optional framework family restriction
dotnet-inspect api --platform Microsoft.AspNetCore.Mvc --framework aspnetcore
```

The resolver:

1. Parses an optional framework family (`runtime`, `aspnetcore`, `netstandard`)
2. Lists installed versions (sorted descending)
3. Matches `--version` or uses latest
4. Searches runtime before aspnetcore when only `--version` is specified for runtime-library lookup
5. Returns the full path to the library

## Troubleshooting

### "No readable PDB found" Warning

This usually means the code is trying to use a **ref assembly** for PDB lookup. Check that:

1. Runtime assembly is being resolved for PDB operations
2. The assembly exists in `shared/` directory
3. MSDL symbol server is accessible

### "Library not found in framework"

The library may only exist in a specific framework:

- BCL types → `runtime`
- ASP.NET types → `aspnetcore`
- .NET Standard facades → `netstandard`

Use `--framework` only when you need to restrict lookup to a specific framework family.

### Version Mismatch

If ref and runtime versions differ, SourceLink URLs may not match the installed version. This is expected for SDK-bundled assemblies where patch versions can vary.
