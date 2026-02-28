# Streaming Feasibility Report for dotnet-inspect

## Summary

dotnet-inspect can adopt streaming output today with minimal changes.
The byte-level zero-allocation model (IUtf8StreamingTableFormatter) is not
a natural fit because dotnet-inspect's data originates as managed strings
from metadata readers and XML/JSON parsers — not from raw byte streams.

## Current Architecture

```text
Command → Inspector → InspectionResult (model) → View (LINQ transforms) → Serialize() → string → stdout
```

Every step materializes fully before the next begins. `Serialize()` returns
a `string` containing the entire markdown document.

## What Streaming Means Here

There are three levels of streaming, each progressively harder:

### Level 1: String-Streaming Output (Easy — works today)

**Change:** Replace `context.Serialize(view)` (returns string) with
`context.Serialize(view, Console.Out, formatter, options)` (writes to TextWriter).

The Markout source generator already emits per-row `WriteTableRow()` calls
inside a `foreach` loop. When the `MarkoutWriter` is backed by a TextWriter
connected to stdout, rows flush incrementally rather than buffering into a
single string. The `IStreamingTableFormatter` interface (already implemented
by `MarkdownFormatter`) enables this path automatically.

**Impact:** Eliminates the final string allocation (can be 100KB+ for large
packages). Output begins appearing as soon as the first section is ready.

**Effort:** A few lines in `OutputFormatter.cs`. No model or view changes.

### Level 2: Incremental Section Output (Moderate)

**Change:** Restructure the pipeline so each section serializes as soon as
its data is available, rather than waiting for the full `InspectionResult`.

Today, `PackageInspector.InspectAsync()` returns a complete
`InspectionResult` with all sections populated. The view wrapper then
exposes each section as a `List<T>` property with LINQ transforms
(`OrderBy`, `SelectMany`, `ToList`).

To stream sections incrementally:

- Split inspection into phases (nuspec → deps → metadata → libraries)
- Output each section's view immediately after its data phase completes
- Use `MarkoutWriter` directly (not the source-gen context) for partial output

**Impact:** Output starts appearing during inspection, not after. Matters
most for `--verbosity detailed` where metadata fetching adds seconds.

**Effort:** Moderate refactor of PackageCommand and OutputFormatter.
The `Downloader<T>` already yields results in completion order, so library
inspection sections could stream as each assembly finishes.

### Level 3: Byte-Streaming (IUtf8StreamingTableFormatter) (Not Recommended)

**Why it doesn't fit:**

1. **Data originates as strings.** Assembly metadata comes from
   `MetadataReader` which returns `string`. NuSpec is XML (string).
   Package metadata is JSON deserialized to `string`. There is no raw
   byte pipeline to preserve.

2. **View transforms create strings.** Type signatures, method names,
   version ranges — all computed as `string` in the view layer. Converting
   to UTF-8 bytes would require encoding at the boundary, negating the
   zero-alloc benefit.

3. **Source generator emits string calls.** The generated serializer calls
   `WriteTableRow(ReadOnlySpan<string>)`. A parallel byte-based code
   generation path would be a major undertaking for no practical gain here.

4. **Volume doesn't justify it.** The largest dotnet-inspect output is
   perhaps 200KB of markdown (a deeply inspected large package). The
   SdkDownloads demo processes megabytes of JSON per section. The
   allocation pressure is qualitatively different.

The byte-streaming model excels when data arrives as UTF-8 bytes (JSON
from HTTP, binary protocols) and output goes to a byte stream — a
network-to-network or network-to-file pipeline. dotnet-inspect is a
metadata-to-terminal tool where strings are the natural representation.

## Recommendation

Adopt **Level 1** immediately — it's a one-line change in
`OutputFormatter.cs` that eliminates the output string allocation. Consider
**Level 2** if users report slow time-to-first-output on large packages.
Skip **Level 3**.

## Reference

The streaming model was prototyped in
[feature/streaming-sdk-demo](https://github.com/richlander/markout/tree/feature/streaming-sdk-demo)
with two comparison demos:

| Metric | String API | Byte-Streaming |
| --- | --- | --- |
| Render alloc (199 rows) | 54 KB (271 B/row) | 0 KB (0 B/row) |
| Total alloc | 540 KB | 432 KB |
| Wall-clock time | ~0.9 s | ~0.9 s |

The byte path achieves zero per-row allocation but wall-clock time is
identical because network I/O dominates. The same dynamic applies to
dotnet-inspect — rendering is not the bottleneck.
