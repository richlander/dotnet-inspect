---
name: dotnet-inspect-decompiler
version: 0.1.0
description: Reconstruct a method or type locally as C# and IL — decompiled source, annotated source with hidden facts, raw IL, fidelity levels, and IL-offset lookup. Always local, no network.
---

# dotnet-inspect: decompiler and IL

Use this skill to understand how code actually works from the assembly you have.
The decompiler is local and always available (no network), and the IL and
annotated views can reveal more than the original source. For the authoritative
original source as written by the author, use the `sourcelink` skill.

```bash
dnx dotnet-inspect -y -- <command>
```

## Decompiled source and IL

A selected overload defaults to `Signature`; bare `-S` adds `Decompiled Source`.
Use `-S @Source` for the full local evidence set (it also pulls `Original
Source` when SourceLink is available — see the `sourcelink` skill):

- `Decompiled Source` — raised, lowered C# (readable best-effort).
- `Annotated Source` — C# with hidden-fact comments and interleaved IL.
- `IL` — raw IL, the highest-fidelity view.

Use `Annotated Source` or `IL` when exact opcodes, offsets, branches, tokens, or
calls matter. Use `--bare` for a whole-type listing.
`--project` reads existing restored assets; restore/build first if dependencies
changed.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S @Source
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S "Annotated Source"
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Decompiled Source" --bare
dnx dotnet-inspect -y -- member Command --project ./src/App Add:1 -S @Source
```

## Fidelity model

The decompiler degrades honestly: IL with no faithful C# spelling renders as a
visible comment and lowers the result's fidelity level
(`Full` -> `Partial` -> `StructuredOnly` -> `IlOnly` -> `Failed`) instead of
emitting plausible-but-wrong source, with a stable `DEC####` diagnostic on every
degradation. `Decompiled Source` is lowered C#; raw/annotated `IL` is highest
fidelity.

If decompiled output looks wrong, capture `Decompiled Source`, `Annotated
Source`, `Original Source` (via the `sourcelink` skill), and `IL` together;
maintainers diagnose pipeline state with DecompilerHarness.

## Locate code by IL offset

```bash
dnx dotnet-inspect -y -- library Foo --il-offset 0x06000001+0x5
```

Use `library Foo --il-offset 0x06000001+0x5` (MethodDef token plus IL offset) to
map a crash location back to the method and source. For call edges (what a method
calls, who calls it), see the `relationships` skill.
