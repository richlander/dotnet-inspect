# Streaming Feasibility Report for dotnet-inspect

## Summary

dotnet-inspect has two distinct data pipelines with different streaming
profiles. Assembly metadata originates as managed strings and fits the
string-streaming model. NuGet service data arrives as JSON over HTTP —
the same pattern where byte-streaming proved effective in the Markout
SdkDownloads prototype.

## Current Architecture

```text
Command → Inspector → InspectionResult (model) → View (LINQ transforms) → Serialize() → string → stdout
```

Every step materializes fully before the next begins. `Serialize()` returns
a `string` containing the entire markdown document.

## Data Pipeline Analysis

dotnet-inspect has two fundamentally different data sources:

### Pipeline A: Assembly Metadata (strings are natural)

- `MetadataReader` returns `string` for type names, method signatures
- NuSpec is XML parsed via `XDocument` into strings
- View transforms compute display strings (type signatures, version ranges)
- Source generator emits `WriteTableRow(ReadOnlySpan<string>)`

This pipeline's data originates as managed strings. Byte-streaming would
require encoding at the boundary, negating the zero-alloc benefit.

### Pipeline B: NuGet Service Data (bytes from the network)

Several paths fetch JSON over HTTP and iterate the results into table rows:

| Path | API | Pattern | Streaming Fit |
| --- | --- | --- | --- |
| **Vulnerability scan** | NuGet vuln index → pages | Paginated JSON, entry-by-entry iteration | Strong |
| **Search results** | NuGet search API | JSON array → table rows | Strong |
| **Version listing** | Flat-container index.json | JSON versions array → list | Moderate |
| **deps.json** | Local file | `File.ReadAllText` → `JsonDocument` | Moderate |
| **Package metadata** | Registration + search APIs | Multi-endpoint aggregation | Weak |

Today all of these use `GetStringAsync → JsonDocument.Parse`, which
materializes the entire HTTP response as a managed string, then allocates
a DOM tree. This is the same pattern the SdkDownloads prototype replaced
with `GetStreamAsync → Utf8JsonReader → ValueSpan`.

**Vulnerability scanning is the strongest candidate.** It fetches multiple
JSON pages sequentially, iterates entries one at a time, checks version
ranges, and accumulates matching results into a list. This could instead
stream each match directly through Markout as a table row — no `List<T>`
accumulation, no `JsonDocument` tree, no `GetString()` per field.

## Streaming Levels

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

**Applies to:** Both pipelines.

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

**Applies to:** Both pipelines.

### Level 3: Byte-Streaming NuGet Service Data (Targeted)

**Change:** For Pipeline B paths, replace:

```text
GetStringAsync → JsonDocument.Parse → iterate → GetString() per field → List<T> → Serialize
```

With:

```text
GetStreamAsync → Utf8JsonReader → ValueSpan (zero-alloc) → Markout WriteUtf8 → Stream
```

This is the same transformation proven in the SdkDownloads prototype.

**Best candidates:**

1. **Vulnerability scanning** (`PackageMetadataService.cs` lines 263–328):
   Fetches a vulnerability index, then iterates multiple JSON pages.
   Each vulnerability entry that matches the package version becomes a
   table row. Today accumulates into `List<PackageVulnerability>`. Could
   instead stream each match through `IUtf8StreamingTableFormatter` as
   it's found — no list, no DOM, no strings for advisory URLs/IDs.

2. **Search results** (`PackageSearchOutputFormatter.cs`): JSON array from
   search API, each result rendered as a table row. The formatter already
   iterates results one at a time — converting to `Utf8JsonReader` +
   byte-based Markout would eliminate string allocation for package IDs,
   versions, and descriptions.

3. **deps.json** (`DepsJsonParser.cs`): Currently `File.ReadAllText` +
   `JsonDocument.Parse`. Could use `FileStream` + `Utf8JsonReader` to
   avoid materializing the entire file as a string.

**Does not apply to:** Assembly metadata (Pipeline A), NuSpec XML parsing,
or package metadata aggregation (requires random access across multiple
API responses).

**Effort:** Per-path conversion similar to SdkDownloads. Each path needs
its own `Utf8JsonReader` state machine. The Markout byte API
(`IUtf8StreamingTableFormatter`) is already available.

## Recommendation

1. **Level 1 now** — one-line change, benefits everything.
2. **Level 3 for vulnerability scanning** — strongest ROI. Vulnerability
   pages can be large, the iteration pattern is a direct match for
   `Utf8JsonReader`, and the output is a simple table.
3. **Level 2 as needed** — if users report slow time-to-first-output.

## Reference

The streaming model was prototyped in
[feature/streaming-sdk-demo](https://github.com/richlander/markout/tree/feature/streaming-sdk-demo)
with two comparison demos:

| Metric | String API | Byte-Streaming |
| --- | --- | --- |
| Render alloc (199 rows) | 54 KB (271 B/row) | 0 KB (0 B/row) |
| Total alloc | 540 KB | 432 KB |
| Wall-clock time | ~0.9 s | ~0.9 s |

The byte path achieves zero per-row allocation. Wall-clock time is
identical at this scale because network I/O dominates, but the allocation
reduction matters for GC pressure in longer-running or higher-throughput
scenarios.
