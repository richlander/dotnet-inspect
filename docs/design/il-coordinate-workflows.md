# IL coordinate workflows

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
