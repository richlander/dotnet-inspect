# IL coordinate workflows

## Owner and boundaries

`ILInspector.Research` owns the IL-offset projection composition: it joins
Metadata, Instructions, SourceLink, and Analysis evidence for one selected
assembly coordinate. The typed request may consume an already-selected
`ResolvedAssemblyReference`; Research does not construct that descriptor,
redefine its identity or MVID validation, or redefine Analysis feature
semantics.

When a descriptor is present, Research snapshots its guarded content through
Metadata, requests Analysis evidence over those immutable bytes, and keys
Research's derived-index reuse by the descriptor's
`AssemblyAcquisitionRegistration`. It does not reopen the descriptor's path.
A request without a descriptor retains the existing path compatibility route.
`ProjectILOffset_DescriptorSemanticEvidenceDoesNotReopenPath` is the
non-vacuity gate for this handoff; the existing
`ProjectILOffset_CostContextUsesPhysicalAsyncBody` test gates descriptor-less
compatibility.

This contract does not own CLI source selection, assembly descriptor
construction, Analysis index construction policy, PDB acquisition, or
multi-assembly coordinate joins.

`library --il-offsets <file>` is a prototype for explaining sparse runtime
coordinates. It assumes another tool has already collected MethodDef token + IL
offset pairs and normalizes them into a simple text file:

```text
# label coordinate
profiler-sample 0x06000042+0x2F
debugger-frame 0x06000051+0x10
analyzer-hit 0x06000060+0x08
```

The command resolves each coordinate against one assembly and prints a compact
summary:

```bash
dotnet-inspect library My.dll --il-offsets coords.txt
```

```text
## IL Coordinates

Coordinate      Label            Member      IL Offset  Meaning         Evidence
0x06000042+0x2F profiler-sample  My.Type.M1  IL_002F    return address  call at IL_002A to M2
0x06000051+0x10 debugger-frame   My.Type.M2  IL_0010    allocation      array int[]
```

## Prototype producer workflows

These workflows are intentionally producer-agnostic. The skill-worthy part is
likely the collection and normalization step, not the `dotnet-inspect` query.

### Debugger / dump workflow

1. Use a debugger, SOS, or dump inspection tool to collect stack frames that
   include a method identity and IL offset.
2. Normalize frames to `0x06000000+0x0` coordinates in a text file.
3. Run `library --il-offsets` to explain return addresses, callsites, exception
   regions, and semantic context rows.
4. Malformed lines are kept as `error` rows so partially-clean artifacts can
   still produce a useful summary.

### Profiler / trace workflow

1. Use a profiler or EventPipe trace tool to identify hot methods and offsets.
2. Symbolize native/IP data to method token + IL offset when needed.
3. Run `library --il-offsets` with labels such as `hot-sample` or
   `alloc-sample` to summarize what each sparse coordinate represents.

### Analyzer / CI artifact workflow

1. A static analyzer or test harness emits method tokens and IL offsets for
   suspicious points.
2. The agent turns the artifact into the coordinate file format.
3. `library --il-offsets` produces the shared explanation table used in PR or
   issue triage.

## Deferrals

- This prototype does not parse debugger, dump, profiler, or trace formats
  directly.
- It does not join coordinates across multiple assemblies in one invocation.
- Those adapters belong in a future debugging skill once the highest-value
  producer workflow is clear.
