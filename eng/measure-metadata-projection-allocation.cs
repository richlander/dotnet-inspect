#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:property EnablePreviewFeatures=true
#:property NoWarn=CA2252

// Measures the allocation shape of MetadataTableProjector.Project so that any
// future decision to make the projection lazier is gated on numbers rather than
// on the structural intuition that eager ImmutableArray materialization "looks
// expensive" (issue #3341, gap 3).
//
// Reports two different quantities, because they answer different questions:
//
//   allocated — total churn while building the projection. A throughput and
//               GC-pressure signal.
//   retained  — the live set still reachable from the finished projection after
//               a forced collection. This is the number that decides whether a
//               browser tab can hold the result, which is the consumer #3341
//               exists to serve.
//
// Usage:
//   dotnet run eng/measure-metadata-projection-allocation.cs
//   dotnet run eng/measure-metadata-projection-allocation.cs -- <path-to-assembly>

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

const string PinnedVersion = "10.0.9";

string target = args.Length > 0 ? args[0] : ResolvePinnedCoreLib();

if (!File.Exists(target))
    throw new FileNotFoundException(
        $"Measurement target not found: {target}. Pass an assembly path as the first argument.",
        target);

// Resolves the pinned measurement input across platforms so the recorded numbers
// stay comparable. The version is pinned deliberately: a different CoreLib has a
// different row population and would not be a like-for-like baseline.
static string ResolvePinnedCoreLib()
{
    IEnumerable<string> roots = Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } configured
        ? [configured]
        : OperatingSystem.IsWindows()
            ? [@"C:\Program Files\dotnet"]
            : ["/usr/share/dotnet", "/usr/local/share/dotnet", "/usr/lib/dotnet"];

    foreach (string root in roots)
    {
        string candidate = Path.Combine(
            root, "shared", "Microsoft.NETCore.App", PinnedVersion, "System.Private.CoreLib.dll");

        if (File.Exists(candidate))
            return candidate;
    }

    throw new FileNotFoundException(
        $"Could not locate the pinned measurement input (Microsoft.NETCore.App {PinnedVersion}). " +
        "Install that runtime, set DOTNET_ROOT, or pass an assembly path as the first argument.");
}

byte[] image = File.ReadAllBytes(target);

Console.WriteLine($"target  : {target}");
Console.WriteLine($"size    : {image.Length:N0} bytes ({image.Length / 1024.0 / 1024.0:N1} MB)");
Console.WriteLine($"runtime : {Environment.Version}");
Console.WriteLine($"server GC: {System.Runtime.GCSettings.IsServerGC}");
Console.WriteLine();

Console.WriteLine($"{"scenario",-36} {"allocated",12} {"(bytes)",13} {"retained",12} {"(bytes)",13} {"rows",10} {"cells",12}");
Console.WriteLine(new string('-', 116));

// Control: what an open PEReader plus its MetadataReader costs on its own, with
// no projection at all. Every scenario below is measured with the reader still
// live, so this is the floor that must be subtracted before attributing retained
// bytes to the projection itself.
{
    var control = MeasureReaderOnly(image);
    Console.WriteLine(
        $"{"control: PEReader only (no rows)",-36} {Megabytes(control.Allocated)} {Exact(control.Allocated)} " +
        $"{Megabytes(control.Retained)} {Exact(control.Retained)} {"-",10} {"-",12}");
}

foreach (var (name, options) in Scenarios())
{
    var result = Measure(options, image);
    Console.WriteLine(
        $"{name,-36} {Megabytes(result.Allocated)} {Exact(result.Allocated)} " +
        $"{Megabytes(result.Retained)} {Exact(result.Retained)} " +
        $"{result.Rows,10:N0} {result.Cells,12:N0}");
}

Console.WriteLine();
ReportRetentionSplit(image);

Console.WriteLine();
ReportRetainedTextCost(image);

static IEnumerable<(string Name, MetadataProjectionOptions Options)> Scenarios() =>
[
    ("full (MaxRows = int.MaxValue)", new MetadataProjectionOptions { MaxRowsPerTable = int.MaxValue }),
    ("default (MaxRows = 4096)", new MetadataProjectionOptions()),
    ("window 1000", new MetadataProjectionOptions { MaxRowsPerTable = 1000 }),
    ("window 100", new MetadataProjectionOptions { MaxRowsPerTable = 100 }),
    ("window 100, MethodDef only", new MetadataProjectionOptions
    {
        MaxRowsPerTable = 100,
        Tables = [TableIndex.MethodDef],
    }),
    // Deep paging has to be measured inside a single large table. An all-tables
    // window at a high start row is not a deep-paging measurement: only the two
    // tables that actually reach that depth contribute rows, so it reports a
    // small number for the wrong reason.
    ("window 100 @ MethodDef row 40000", new MetadataProjectionOptions
    {
        MaxRowsPerTable = 100,
        StartRowId = 40000,
        Tables = [TableIndex.MethodDef],
    }),
];

static (long Allocated, long Retained) MeasureReaderOnly(byte[] image)
{
    using (var warmup = new PEReader(new MemoryStream(image)))
    {
        _ = warmup.GetMetadataReader(MetadataReaderOptions.None);
    }

    long baseline = Quiesce();
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

    using var peReader = new PEReader(new MemoryStream(image));
    var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);

    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    long retained = Quiesce() - baseline;

    GC.KeepAlive(reader);
    return (allocated, retained);
}

static (long Allocated, long Retained, int Rows, long Cells) Measure(
    MetadataProjectionOptions options,
    byte[] image)
{
    // Warm up first so JIT and static initialization are not charged to the
    // measurement, which would otherwise dominate the smallest scenarios.
    using (var warmup = new PEReader(new MemoryStream(image)))
    {
        _ = MetadataTableProjector.Project(warmup, options);
    }

    long baseline = Quiesce();
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

    using var peReader = new PEReader(new MemoryStream(image));
    var projection = MetadataTableProjector.Project(peReader, options);

    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

    // The projection is still rooted here, so what survives this collection is
    // what a caller holding the projection actually pays for.
    long retained = Quiesce() - baseline;

    int rows = 0;
    long cells = 0;
    foreach (var table in projection.Tables)
    {
        rows += table.Rows.Length;
        cells += (long)table.Rows.Length * table.Columns.Length;
    }

    GC.KeepAlive(projection);
    return (allocated, retained, rows, cells);
}

// Attributes the retained graph to the eagerly formatted display text each cell
// carries, which is the only component large enough to be worth a design change.
//
// Counting is by object identity, not by property. String, UserString, and Guid
// cells pass the SAME string instance as both `Text` and `Preview`, so summing
// the two properties double-counts every one of them. Only Blob cells hold a
// distinct preview with a null `Text`.
static void ReportRetainedTextCost(byte[] image)
{
    using var peReader = new PEReader(new MemoryStream(image));
    var projection = MetadataTableProjector.Project(
        peReader,
        new MetadataProjectionOptions { MaxRowsPerTable = int.MaxValue });

    long scalarCount = 0, scalarChars = 0, scalarBytes = 0;
    long flagsCount = 0, flagsChars = 0, flagsBytes = 0;
    long handleCount = 0, handleChars = 0, handleBytes = 0;
    long heapCount = 0, heapChars = 0, heapBytes = 0;
    long sharedTextPreview = 0;
    long nilCount = 0;

    var distinctDisplay = new HashSet<string>(StringComparer.Ordinal);
    var seen = new HashSet<string>(StringIdentityComparer.Instance);

    // Charges each distinct string OBJECT once, however many properties expose
    // it, and sizes each one individually: an x64 System.String occupies
    // Align8(22 + 2 * Length). Summing chars first and rounding once at the end
    // would understate the total, because each object rounds separately.
    void Charge(string? s, ref long count, ref long chars, ref long bytes)
    {
        if (s is null || !seen.Add(s))
            return;
        count++;
        chars += s.Length;
        bytes += ((22 + (2L * s.Length)) + 7) / 8 * 8;
    }

    foreach (var table in projection.Tables)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                switch (cell)
                {
                    case MetadataValue.Scalar scalar:
                        Charge(scalar.Display, ref scalarCount, ref scalarChars, ref scalarBytes);
                        distinctDisplay.Add(scalar.Display);
                        break;
                    case MetadataValue.Flags flags:
                        Charge(flags.Decoded, ref flagsCount, ref flagsChars, ref flagsBytes);
                        distinctDisplay.Add(flags.Decoded);
                        break;
                    case MetadataValue.Handle handle:
                        Charge(handle.Reference.Display, ref handleCount, ref handleChars, ref handleBytes);
                        break;
                    case MetadataValue.HeapReference heap:
                        if (ReferenceEquals(heap.Text, heap.Preview))
                            sharedTextPreview++;
                        Charge(heap.Text, ref heapCount, ref heapChars, ref heapBytes);
                        Charge(heap.Preview, ref heapCount, ref heapChars, ref heapBytes);
                        break;
                    case MetadataValue.Nil:
                        nilCount++;
                        break;
                }
            }
        }
    }

    long total = scalarBytes + flagsBytes + handleBytes + heapBytes;

    Console.WriteLine("=== Retained text cost of the full projection ===");
    Console.WriteLine("(distinct string objects; a shared instance is charged once)");
    Console.WriteLine($"Scalar.Display  {scalarCount,10:N0} objects {scalarChars,12:N0} chars {Mb(scalarBytes)}");
    Console.WriteLine($"Flags.Decoded   {flagsCount,10:N0} objects {flagsChars,12:N0} chars {Mb(flagsBytes)}");
    Console.WriteLine($"Handle.Display  {handleCount,10:N0} objects {handleChars,12:N0} chars {Mb(handleBytes)}");
    Console.WriteLine($"Heap text       {heapCount,10:N0} objects {heapChars,12:N0} chars {Mb(heapBytes)}");
    Console.WriteLine($"{"total",-16}{"",10}         {"",12} {Mb(total)}");
    Console.WriteLine();
    Console.WriteLine($"Heap cells sharing one instance as Text and Preview : {sharedTextPreview:N0}");
    Console.WriteLine($"Nil cells (stateless, all identical)                : {nilCount:N0}");
    Console.WriteLine($"Scalar/Flags distinct by value                      : {distinctDisplay.Count:N0}");
    Console.WriteLine();
    Console.WriteLine("Caveat: identity counting charges a string the runtime had already");
    Console.WriteLine("cached (small integers, enum names) as if the projection owned it, and");
    Console.WriteLine("whether such a string is shared depends on what ran earlier in the");
    Console.WriteLine("process. Totals can therefore drift by a few dozen chars between runs.");

    GC.KeepAlive(projection);

    static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0,8:N1} MB ({bytes,13:N0} bytes)";
}

// Isolates what the PROJECTION retains from what SRM retains lazily. The
// projection is created and measured inside a scope, then becomes unreachable
// while the PEReader stays live; whatever survives is not the projection's.
// A WeakReference confirms the projection really was collected rather than
// merely appearing to be.
static void ReportRetentionSplit(byte[] image)
{
    using (var warmup = new PEReader(new MemoryStream(image)))
    {
        _ = MetadataTableProjector.Project(warmup, new MetadataProjectionOptions { MaxRowsPerTable = int.MaxValue });
    }

    long baseline = Quiesce();
    using var peReader = new PEReader(new MemoryStream(image));

    var (live, weak) = ProjectScoped(peReader, baseline);
    long afterDrop = Quiesce() - baseline;

    Console.WriteLine("=== Where the retained bytes actually live ===");
    Console.WriteLine($"projection reachable            : {Megabytes(live)}");
    Console.WriteLine($"projection unreachable, reader live : {Megabytes(afterDrop)}");
    Console.WriteLine($"attributable to the projection  : {Megabytes(live - afterDrop)}");
    Console.WriteLine($"projection survived the drop?   : {weak.IsAlive}");
    Console.WriteLine();
    Console.WriteLine("Not on the managed heap, so not counted above:");
    Console.WriteLine($"  input image bytes             : {Megabytes(image.LongLength)}");
    Console.WriteLine($"  SRM metadata block            : {Megabytes(peReader.GetMetadata().Length)}");

    GC.KeepAlive(peReader);

    static (long Retained, WeakReference Weak) ProjectScoped(PEReader peReader, long baseline)
    {
        var projection = MetadataTableProjector.Project(
            peReader,
            new MetadataProjectionOptions { MaxRowsPerTable = int.MaxValue });
        long retained = Quiesce() - baseline;
        var weak = new WeakReference(projection);
        GC.KeepAlive(projection);
        return (retained, weak);
    }
}

static long Quiesce()
{
    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
    return GC.GetTotalMemory(forceFullCollection: true);
}

static string Megabytes(long bytes) => $"{bytes / 1024.0 / 1024.0,9:N1} MB";

// Raw bytes alongside MB, so claims about stability and about two scenarios
// costing "the same" can be checked rather than taken on a rounded figure.
static string Exact(long bytes) => $"{bytes,13:N0}";

// Distinguishes string OBJECTS, not string values. Two equal-valued strings at
// different addresses each occupy memory, so a value comparer would undercount.
sealed class StringIdentityComparer : IEqualityComparer<string>
{
    public static readonly StringIdentityComparer Instance = new();

    public bool Equals(string? x, string? y) => ReferenceEquals(x, y);

    public int GetHashCode(string obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
