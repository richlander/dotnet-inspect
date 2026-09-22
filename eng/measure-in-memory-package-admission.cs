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
await using (var archive = File.OpenRead(args[0]))
    await store.CommitAsync(args[1], args[2], producer, archive);
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
for (int iteration = 0; iteration < 6; iteration++)
{
    long before = GC.GetAllocatedBytesForCurrentThread();
    long start = Stopwatch.GetTimestamp();
    PackagePayloadResult result = await PackagePayloadAcquisition.AcquireAsync(
        client, coordinate, store);
    TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    if (result is not PackagePayloadResult.Acquired acquired
        || acquired.Payload.Origin != PackagePayloadOrigin.Cache)
        throw new InvalidOperationException($"Expected cached acquisition: {result}");
    Console.WriteLine($"{(iteration == 0 ? "First validation" : "Repeated admission"),-20} "
        + $"{elapsed.TotalMilliseconds,10:F3} ms {allocated,12:N0} allocated bytes");
}
return 0;

sealed class NoNetwork : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The probe attempted network work.");
}
