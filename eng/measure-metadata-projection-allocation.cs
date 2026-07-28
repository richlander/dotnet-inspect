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

Console.WriteLine($"{"scenario",-36} {"allocated",12} {"retained",12} {"rows",10} {"cells",12}");
Console.WriteLine(new string('-', 86));

// Control: what an open PEReader plus its MetadataReader costs on its own, with
// no projection at all. Every scenario below is measured with the reader still
// live, so this is the floor that must be subtracted before attributing retained
// bytes to the projection itself.
{
    var control = MeasureReaderOnly(image);
    Console.WriteLine(
        $"{"control: PEReader only (no rows)",-36} {Megabytes(control.Allocated)} " +
        $"{Megabytes(control.Retained)} {"-",10} {"-",12}");
}

foreach (var (name, options) in Scenarios())
{
    var result = Measure(options, image);
    Console.WriteLine(
        $"{name,-36} {Megabytes(result.Allocated)} {Megabytes(result.Retained)} " +
        $"{result.Rows,10:N0} {result.Cells,12:N0}");
}

Console.WriteLine();
ReportRetainedTextCost(image);

static IEnumerable<(string Name, MetadataProjectionOptions Options)> Scenarios() =>
[
    ("full (MaxRows = int.MaxValue)", new MetadataProjectionOptions { MaxRowsPerTable = int.MaxValue }),
    ("default (MaxRows = 4096)", new MetadataProjectionOptions()),
    ("window 1000", new MetadataProjectionOptions { MaxRowsPerTable = 1000 }),
    ("window 100", new MetadataProjectionOptions { MaxRowsPerTable = 100 }),
    ("window 100 @ row 40000", new MetadataProjectionOptions { MaxRowsPerTable = 100, StartRowId = 40000 }),
    ("window 100, MethodDef only", new MetadataProjectionOptions
    {
        MaxRowsPerTable = 100,
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
static void ReportRetainedTextCost(byte[] image)
{
    using var peReader = new PEReader(new MemoryStream(image));
    var projection = MetadataTableProjector.Project(
        peReader,
        new MetadataProjectionOptions { MaxRowsPerTable = int.MaxValue });

    long scalarCount = 0, scalarChars = 0;
    long flagsCount = 0, flagsChars = 0;
    long handleCount = 0, handleChars = 0;
    long heapTextChars = 0, heapPreviewChars = 0;
    long nilCount = 0;

    var distinctDisplay = new HashSet<string>(StringComparer.Ordinal);

    foreach (var table in projection.Tables)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                switch (cell)
                {
                    case MetadataValue.Scalar scalar:
                        scalarCount++;
                        scalarChars += scalar.Display.Length;
                        distinctDisplay.Add(scalar.Display);
                        break;
                    case MetadataValue.Flags flags:
                        flagsCount++;
                        flagsChars += flags.Decoded.Length;
                        distinctDisplay.Add(flags.Decoded);
                        break;
                    case MetadataValue.Handle handle:
                        handleCount++;
                        handleChars += handle.Reference.Display?.Length ?? 0;
                        break;
                    case MetadataValue.HeapReference heap:
                        heapTextChars += heap.Text?.Length ?? 0;
                        heapPreviewChars += heap.Preview.Length;
                        break;
                    case MetadataValue.Nil:
                        nilCount++;
                        break;
                }
            }
        }
    }

    Console.WriteLine("=== Retained text cost of the full projection ===");
    Console.WriteLine($"Scalar.Display  {scalarCount,10:N0} strings {scalarChars,12:N0} chars  ~{Estimate(scalarCount, scalarChars)}");
    Console.WriteLine($"Flags.Decoded   {flagsCount,10:N0} strings {flagsChars,12:N0} chars  ~{Estimate(flagsCount, flagsChars)}");
    Console.WriteLine($"Handle.Display  {handleCount,10:N0} strings {handleChars,12:N0} chars  ~{Estimate(handleCount, handleChars)}");
    Console.WriteLine($"HeapRef.Text    {"",10}         {heapTextChars,12:N0} chars  ~{Estimate(0, heapTextChars)}");
    Console.WriteLine($"HeapRef.Preview {"",10}         {heapPreviewChars,12:N0} chars  ~{Estimate(0, heapPreviewChars)}");
    Console.WriteLine();
    Console.WriteLine($"Nil cells (stateless, all identical) : {nilCount:N0}");
    Console.WriteLine($"Scalar/Flags strings                 : {scalarCount + flagsCount:N0}");
    Console.WriteLine($"  distinct                           : {distinctDisplay.Count:N0}");
    Console.WriteLine($"  duplication factor                 : {(scalarCount + flagsCount) / (double)Math.Max(1, distinctDisplay.Count):N1}x");

    GC.KeepAlive(projection);

    // A string costs roughly 22 bytes of object overhead plus 2 bytes per char.
    static string Estimate(long count, long chars)
        => $"{((count * 22) + (chars * 2)) / 1024.0 / 1024.0,6:N1} MB";
}

static long Quiesce()
{
    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
    return GC.GetTotalMemory(forceFullCollection: true);
}

static string Megabytes(long bytes) => $"{bytes / 1024.0 / 1024.0,9:N1} MB";
