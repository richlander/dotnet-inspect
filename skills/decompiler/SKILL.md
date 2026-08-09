---
name: dotnet-inspect-decompiler
version: 0.1.0
description: Reconstruct a method or type as C# and IL — decompiled source, annotated source with hidden facts, raw IL, fidelity levels, and IL-offset lookup. Use --offline to prohibit package and PDB network acquisition.
---

# dotnet-inspect: decompiler and IL

Use this skill to understand how code actually works from the assembly you have.
The decompiler runs locally against the acquired assembly, and the IL and
annotated views can reveal more than the original source. Package and PDB
acquisition can use the network; add `--offline` to prohibit network access.
For authored original source, use the `sourcelink` skill and follow its
checksum-verification boundaries before treating fetched content as
authoritative.

```bash
dnx dotnet-inspect -y -- <command>
```

## Decompiled source and IL

A selected overload defaults to `Signature`; bare `-S` adds `Decompiled Source`.
Use `-S "Decompiled Source,Annotated Source,IL" --offline` for the full
zero-network evidence set:

- `Decompiled Source` — raised, lowered C# (readable best-effort); locals without
  PDB names use byte-preserving type/role-derived names by default.
- `Annotated Source` — C# with hidden-fact comments and interleaved IL.
- `IL` — raw IL, the highest-fidelity view.

Use `Annotated Source` or `IL` when exact opcodes, offsets, branches, tokens, or
calls matter. Use `--bare` for a whole-type listing.
`-S @Source` is broader and may fetch network `Original Source` content when
SourceLink is available; that network body is not checksum-verified by default.
`--project` reads existing restored assets; restore/build first if dependencies
changed.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json \
  Serialize:1 -S "Decompiled Source,Annotated Source,IL" --offline
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S "Annotated Source"
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Decompiled Source" --bare
dnx dotnet-inspect -y -- member Command --project ./src/App Add:1 -S "Decompiled Source,Annotated Source,IL"
```

### Readability and taste

`--readable-names` replaces compiler-style local names such as `V_0` where the
body provides a stable readable alternative. It is independent of C# taste
options. A tool-owned `.dotnet-inspectconfig`, discovered by walking up from the
working directory, selects configured spellings; `--taste` requests the full
supported taste set for one invocation. `Applied Taste` reports which choices
actually changed the rendered body.

```bash
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll \
  -S "Decompiled Source,Applied Taste" --taste --readable-names
```

### Focusing annotations

By default every hidden fact renders as a trailing `//` comment. A fact with a
long detail can push that comment far off the right edge. `--focus` promotes
matching facts to a `^^^^` underline beneath the statement, wrapped into a
readable block:

```bash
dnx dotnet-inspect -y -- member Cache --project ./src/App Pump:1 -S "Annotated Source" --focus allocation
```

The value matches an annotation category, an exact id, or a dotted-id prefix on
a segment boundary (`alloc` selects `alloc.box`, not `allocator.x`). It
**promotes, it never filters** — facts that do not match keep the trailing form,
so `--focus` narrows attention without hiding anything. A focus that matches
nothing says so and names the families the member does have.

## Fidelity model

The decompiler degrades honestly: IL with no faithful C# spelling renders as a
visible comment and lowers the result's fidelity level
(`Full` -> `Partial` -> `StructuredOnly` -> `IlOnly` -> `Failed`) instead of
emitting plausible-but-wrong source, with a stable `DEC####` diagnostic on every
degradation. `Decompiled Source` is lowered C#; raw/annotated `IL` is highest
fidelity.

If decompiled output looks wrong, capture `Decompiled Source`, `Annotated
Source`, `Original Source`, `Source Diff` (via the `sourcelink` skill), and `IL`
together; maintainers diagnose pipeline state with DecompilerHarness.

Select `Fidelity Causes` for the typed `DEC####` cause census behind that
fidelity grade. It distinguishes a Full method (complete, no causes), a method
without a body (absent), and a failed inspection.

```bash
dnx dotnet-inspect -y -- member MyType MyMethod:1 --library MyLib.dll -S "Fidelity Causes"
```

## Locate code by IL offset

```bash
dnx dotnet-inspect -y -- library Foo --il-offset 0x06000001+0x5
```

Use `library Foo --il-offset 0x06000001+0x5` (MethodDef token plus IL offset) to
compose its default source-location, member, instruction, exception, callsite,
and return-address sections. Allocation, safety, and cost are opt-in; request
them with `-S "Context: Allocation,Context: Safety,Context: Cost"`. Use
`--il-offsets coordinates.txt` for a sparse batch. For call edges (what a method
calls, who calls it), see the `relationships` skill.
