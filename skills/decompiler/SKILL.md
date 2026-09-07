---
name: dotnet-inspect-decompiler
version: 0.1.0
description: Reconstruct a method or type as C# and IL — decompiled source, annotated source with hidden facts, raw IL, fidelity levels, and IL-offset lookup. Use --offline to prohibit package and PDB network acquisition.
---

# dotnet-inspect: decompiler and IL

Use this skill to understand how code actually works from the assembly you have.
The decompiler runs locally against the acquired assembly, and the IL and
annotated views can reveal facts absent from PDB-mapped source. Package and PDB
acquisition can use the network; add `--offline` to prohibit network access.
For PDB-mapped source, use the `sourcelink` skill and follow its checksum and
provenance boundaries before interpreting fetched content.

```bash
dnx dotnet-inspect -y -- <command>
```

## Decompiled source and IL

A selected overload and bare `-S` both render its bounded `Signature` overview.
Select `Decompiled Source` explicitly when implementation evidence is the
question. Use `-S "Decompiled Source,Annotated Source,IL" --offline` for the
full zero-network evidence set:

- `Decompiled Source` — raised, lowered C# (readable best-effort); locals without
  PDB names use byte-preserving type/role-derived names by default.
- `Annotated Source` — C# with hidden-fact comments and interleaved IL.
- `IL` — raw IL, the highest-fidelity view.

Use `Annotated Source` or `IL` when exact opcodes, offsets, branches, tokens, or
calls matter. Use `--bare` for a whole-type listing.
`-S @Source` is broader and may fetch network `PDB Source` content when
SourceLink is available; the fetch verifies the final redirect origin and PDB
checksum before returning the body.
`--project` reads existing restored assets; restore/build first if dependencies
changed.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json \
  Serialize:1 -S "Decompiled Source,Annotated Source,IL" --offline
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S "Annotated Source"
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Decompiled Source" --bare
dnx dotnet-inspect -y -- member Command --project ./src/App Add:1 -S "Decompiled Source,Annotated Source,IL"
```

## Search rendered body shapes

Use the library `Body Shapes` section when the question is "which public API
methods contain this C# syntax?" The query is exact and assembly-scoped:
`Kind` accepts a stable ID such as `ObjectCreationExpression`, `TryStatement`,
or `ElementAccessExpression`, not an IR class name or text pattern. Discover
the IDs instead of guessing them:

```bash
dnx dotnet-inspect -y -- vocabulary -S "C# Body Kinds"
dnx dotnet-inspect -y -- library MyLib.dll \
  --where "Kind=ObjectCreationExpression" --jsonl
dnx dotnet-inspect -y -- library System.Text.Json \
  --where "Kind=TryStatement" --columns "Member;Token;Match" --rows 10
dnx dotnet-inspect -y -- library MyLib.dll \
  --where "Kind=InvocationExpression" \
  --where "Finding=analysis.call-site" \
  --where "Shape=sync-call-in-async" --jsonl
dnx dotnet-inspect -y -- type JsonDocument \
  --platform System.Text.Json \
  --where "Kind=ObjectCreationExpression" --jsonl
dnx dotnet-inspect -y -- member JsonDocument RootElement:1 \
  --platform System.Text.Json \
  --where "Kind=ObjectCreationExpression" --jsonl
```

`Kind=...` auto-selects the explicit-only section when no `-S` selection is
present. Results include a round-tripping qualified member selector, MethodDef
token, one-based start/end range in the method's rendered C# body, and exact
selected text. These are not IL offsets or original source-file coordinates.
Bodies below `Full`
fidelity are skipped and reported; add `--verbose` for per-member detail.
At library scope, repeat `--where` with Performance Triage fields to AND those
predicates before decompilation. The query maps matching opportunities through
their typed source owner and searches only those MethodDef bodies; select a
Performance section separately for the canonical evidence receipt. Performance
`--top` and `--order-by` do not compose; use `--rows` to limit Body Shapes
output. Without narrowing, the search runs the decompiler for each API-surface
candidate body and may be expensive on a large library.

For a counted overview, select `-S "Body Shape Summary"` with the same Kind
predicate. Identical rendered Kind/Match values are grouped before row limits;
`--columns "Match;Count"` gives a compact table. The existing `Body Shapes`
section remains the locatable occurrence view. Column projection never groups
rows, and `--count` counts groups in the summary or occurrences in the detail
view after its row window. Both views are available at library, type, and
member scope.

Type scope requires one exact type and decompiles only its MethodDef and
accessor bodies. Member scope requires one exact member name or selector and
decompiles only that member's MethodDef body. Unambiguous methods and
single-accessor members are auto-selected; overloaded names require `Name:N`
or `Name~digest`. A property or event with multiple body accessors requires an
accessor selector; use `Name~digest:1`/`Name~digest:2` when the owner is
overloaded. Use `--all` to include non-public type members or select a
non-public member.

### Readability and taste

`--readable-names` replaces compiler-style local names such as `V_0` where the
body provides a stable readable alternative. It is independent of C# taste
options. A tool-owned `.dotnet-inspectconfig`, discovered by walking up from the
working directory, selects configured spellings; `--taste` requests the
oracle-endorsed taste set for one invocation. `Applied Taste` reports which
choices actually changed the rendered body.

Explicit local types are the default. Enable any independent `var` category
with `csharp_style_var_for_built_in_types = true`,
`csharp_style_var_when_type_is_apparent = true`, or
`csharp_style_var_elsewhere = true`. These byte-neutral opt-ins are not part of
`--taste`. Object creation is target-typed by default; set
`csharp_style_implicit_object_creation_when_type_is_apparent = false` to retain
the explicit constructed type. A `var` declaration keeps `new T(...)` because
`var x = new()` has no target type.

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
Source`, `PDB Source`, `Source Diff` (via the `sourcelink` skill), and `IL`
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
