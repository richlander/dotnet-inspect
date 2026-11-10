---
name: dotnet-inspect-decompiler
version: 0.1.0
description: Read .NET method and type implementations as authored-first source, decompiled C#, annotated source or IL; interpret reconstruction fidelity and locate exact code coordinates. Use --offline to prohibit network acquisition.
---

# dotnet-inspect: Read .NET Method Implementations as C# and IL

Use this skill to understand implementation from the assembly you have.
Run `dnx dotnet-inspect -y -- <command>`. Package/PDB acquisition can use the
network; `--offline` forbids it. Decompilation itself is local.

## Choose the evidence for the question

```bash
dnx dotnet-inspect -y -- type JsonSerializer \
  --package System.Text.Json -S Source
dnx dotnet-inspect -y -- member JsonSerializer \
  --platform System.Text.Json Serialize:1 \
  -S "Decompiled Source,Annotated Source,IL" --offline
```

| View | What it establishes |
| --- | --- |
| `Signature` | API declaration, not implementation; the default for one selected overload. |
| `Source` | Authored-first source with fallback and provider context retained. |
| `PDB Source` | Only checksum-matched source selected through Portable PDB evidence. |
| `Decompiled Source` | Locally reconstructed, lowered C#; not the authored source. |
| `Annotated Source` | Reconstructed C# with hidden facts and interleaved IL. |
| `IL` | Exact instructions, offsets, branches, tokens and calls. |

One selected source payload normally renders native source; add `--markdown`
for document framing. `@Source` is broader and can fetch PDB-mapped source.
Use `skill sourcelink` when source provenance or checksum boundaries matter.

Choose overloads with `Member Index`, then reuse `Name:N` or a stable
`Name~digest`. `--all` includes non-public and hidden targets. A restored
`--project` needs current assets and output.

## Follow deeper implementation evidence

```bash
dnx dotnet-inspect -y -- member --explain
dnx dotnet-inspect -y -- member JsonSerializer \
  --platform System.Text.Json Serialize:1 --explain
dnx dotnet-inspect -y -- member JsonSerializer \
  --platform System.Text.Json Serialize:1 -S IL -E .tips
```

`--explain` replaces ordinary stdout; Member `-E` preserves it and writes a
companion explanation to stderr. `-E .tips` selects contextual suggestions.
Use `-D` for available sections and `-Q` for query rules; a known source
command does not need an explanation detour.

| Question | Entrance |
| --- | --- |
| Which bodies contain a rendered C# syntax kind? | `library -Q "Body Shapes"` and `explain "C# body kinds"` |
| Which style choices changed the reconstruction? | `-S "Decompiled Source,Applied Taste" --taste` on an exact Member |
| How can I emphasize one annotation family? | `-S "Annotated Source" --focus allocation` on an exact Member |
| Why is reconstruction incomplete? | `-S "Fidelity Causes"` on an exact Member |
| Where is a profiler/crash IL coordinate? | `library address 0x06000001+0x5 --library Foo.dll` |

Read returned bindings and vocabularies before unfamiliar syntax queries.
Body Shapes locates rendered C# occurrences, not original file spans or IL
offsets; whole-Library decompilation may be expensive. Narrow the subject
before broad content searches.

`--focus` promotes matching annotations without filtering away other facts.
Style options change spelling, not a runtime-cost or source-provenance claim.
Load `skill relationships` for calls and `skill performance` for cost triage.

## Interpret fidelity honestly

Fidelity degrades visibly from `Full` through `Partial`, `StructuredOnly`,
`IlOnly` and `Failed`, with `DEC####` diagnostics. A plausible reconstruction
is not proof of faithful source semantics. Use Annotated Source or IL whenever
exact execution structure matters.

`Fidelity Causes` distinguishes complete/no-causes, absent body and failed
inspection. Preserve those outcomes instead of treating all empty results as
success. If C# looks wrong, retain Decompiled Source, Annotated Source, IL and
available PDB Source/Source Diff for comparison.

The coordinate entrance composes source-location, Member, instruction,
exception, callsite and return-address context. Allocation, safety and cost
are opt-in sections; discover them rather than assuming the default performs
all analyses. Keep the exact Library build, MethodDef token and IL offset with
every conclusion.
