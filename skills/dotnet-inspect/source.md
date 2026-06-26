---
name: dotnet-inspect-source
version: 0.1.0
description: Decompiled C#, IL, and SourceLink-backed original source evidence for .NET members, types, and libraries.
---

# dotnet-inspect: source and decompiler

Use this skill when you need source-level evidence for a .NET API: raised C#,
exact IL, SourceLink-backed original source, or unsafe-operation audits.

```bash
dnx dotnet-inspect -y -- <command>
```

## Source evidence for a member

A selected overload defaults to `Signature`; bare `-S` adds `Decompiled Source`.
Use `-S "Source Locations"` for member file/line URLs without fetching bodies.
Use `-S @Source` for the full source-and-IL evidence set:

- `Decompiled Source` — raised, lowered C# (readable best-effort).
- `Annotated Source` — C# with hidden-fact comments and interleaved IL.
- `Original Source` — SourceLink-backed original source when available.
- `IL` — raw IL, the highest-fidelity view.

Use `Annotated Source` or `IL` when exact opcodes, offsets, branches, tokens, or
calls matter.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S @Source
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S "Annotated Source"
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Source Files"
dnx dotnet-inspect -y -- library System.Text.Json -S "Source Files"
```

## Fidelity model

The decompiler degrades honestly: IL with no faithful C# spelling renders as a
visible comment and lowers the result's fidelity level
(`Full` -> `Partial` -> `StructuredOnly` -> `IlOnly` -> `Failed`) instead of
emitting plausible-but-wrong source, with a stable `DEC####` diagnostic on every
degradation. `Original Source` is original source; `Decompiled Source` is lowered
C#; raw/annotated `IL` is highest fidelity.

If decompiled output looks wrong, capture `Decompiled Source`, `Annotated
Source`, `Original Source`, and `IL` together; maintainers diagnose pipeline
state with DecompilerHarness.

## SourceLink

PDBs carry SourceLink data; they are not SourceLink themselves. Use
`-S "Source Files"` for SourceLink type-to-URL rows. SourceLink URLs default to
raw/fetchable form; add `--blob` for browser URLs.

```bash
dnx dotnet-inspect -y -- library System.Text.Json -S "Source Files"
dnx dotnet-inspect -y -- member Type Method:1 -S "Source Locations" --bare
```

## Unsafe and IL audits

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S @Audit
dnx dotnet-inspect -y -- member Type Method:1 --library MyLib.dll -S "Unsafe Operations,IL"
dnx dotnet-inspect -y -- member Type Method:1 -S Facts --tsv
dnx dotnet-inspect -y -- library Foo --il-offset 0x06000001+0x5
```

Use `-S Calls` for direct call-site evidence, `-S Callers` for reverse edges
(widen with `--bin`, `--project`, or `--caller-package`), `-S "Unsafe
Operations"` for unsafe evidence, and `-S Facts --tsv` for structured hidden
facts. Use `library Foo --il-offset 0x06000001+0x5` (MethodDef token plus IL
offset) for crash diagnostics.
