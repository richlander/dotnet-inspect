#:project ../src/DotnetInspector.Packages/DotnetInspector.Packages.csproj

using System.Diagnostics;
using DotnetInspector.Packages;
using NuGetFetch;

// Non-CI probe: supply a real retained nupkg; never contacts the network.
// dotnet run eng/measure-in-memory-package-admission.cs -c Release -- ARCHIVE ID VERSION
if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: ARCHIVE PACKAGE_ID VERSION");
    return 2;
}

PackageSource source = PackageSource.NuGetOrg;
string producer = NuGetCache.GetSourceKey(source.Url);
var store = new InMemoryPackageStore();
long before = GC.GetAllocatedBytesForCurrentThread();
long start = Stopwatch.GetTimestamp();
IPackageContent committed;
await using (var archive = File.OpenRead(args[0]))
    committed = await store.CommitAsync(args[1], args[2], producer, archive);
TimeSpan commitElapsed = Stopwatch.GetElapsedTime(start);
long commitAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
using var client = new HttpClient(new NoNetwork());
PackageCoordinateResolution resolution =
    await PackageCoordinateResolver.ResolveAsync(
        client,
        new PackageCoordinate(args[1], args[2], "net11.0"),
        [source]);
ResolvedPackageCoordinate coordinate = resolution
    is PackageCoordinateResolution.Resolved resolved
        ? resolved.Coordinate
        : throw new InvalidOperationException(
            $"The exact coordinate could not be resolved: {resolution}");
Console.WriteLine($"Archive: {args[1]}@{args[2]} ({new FileInfo(args[0]).Length:N0} bytes)");
Console.WriteLine($"{"Checked commit",-20} "
    + $"{commitElapsed.TotalMilliseconds,10:F3} ms {commitAllocated,12:N0} allocated bytes");
for (int iteration = 0; iteration < 6; iteration++)
{
    before = GC.GetAllocatedBytesForCurrentThread();
    start = Stopwatch.GetTimestamp();
    PackagePayloadResult result = await PackagePayloadAcquisition.AcquireAsync(
        client, coordinate, store);
    TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    if (result is not PackagePayloadResult.Acquired acquired
        || acquired.Payload.Origin != PackagePayloadOrigin.Cache)
        throw new InvalidOperationException($"Expected cached acquisition: {result}");
    Console.WriteLine($"{"Cached acquisition",-20} "
        + $"{elapsed.TotalMilliseconds,10:F3} ms {allocated,12:N0} allocated bytes");
}

var manifest = (IPackageContentEntryManifest)committed;
PackageContentEntry[] assemblies = manifest.EnumerateEntriesWithLengths()
    .Where(entry => entry.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
    .ToArray();
if (assemblies.Length == 0)
    throw new InvalidOperationException("The archive contains no DLL entry.");
PackageContentEntry selected = assemblies.MaxBy(entry => entry.Length);
for (int iteration = 0; iteration < 2; iteration++)
{
    before = GC.GetAllocatedBytesForCurrentThread();
    start = Stopwatch.GetTimestamp();
    if (!committed.TryOpenEntry(
            selected.Path,
            selected.Length,
            out Stream? entry))
    {
        throw new InvalidOperationException(
            $"The selected entry is unavailable: {selected.Path}");
    }
    using (entry)
    {
    }
    TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"{"Checked entry",-20} "
        + $"{elapsed.TotalMilliseconds,10:F3} ms {allocated,12:N0} allocated bytes "
        + $"{selected.Length,12:N0} bytes {selected.Path}");
}
return 0;

sealed class NoNetwork : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The probe attempted network work.");
}
