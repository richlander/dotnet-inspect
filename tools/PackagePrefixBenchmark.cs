#:project ../src/DotnetInspector.Queries/DotnetInspector.Queries.csproj
#:property EnablePreviewFeatures=true

using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using DotnetInspector.Queries;
using NuGetFetch;

if (args.Length < 3
    || args[0] is not ("search" or "profile"))
{
    Console.Error.WriteLine(
        "Usage: dotnet run tools/PackagePrefixBenchmark.cs --"
        + " <search|profile> <prefix> <comma-separated-takes> [trials]");
    return 1;
}

string mode = args[0];
string prefix = args[1];
int[] takes = args[2]
    .Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(int.Parse)
    .ToArray();
int trials = args.Length > 3 ? int.Parse(args[3]) : 1;

var countingHandler = new CountingHandler(
    new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        Credentials = null,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PreAuthenticate = false,
        UseCookies = false,
        UseProxy = false,
    });
var options = new NuGetFetchOptions
{
    RequestTimeout = TimeSpan.FromMinutes(1),
    OperationTimeout = TimeSpan.FromMinutes(30),
};
using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
    PackageSourceAssociation.Create(),
    countingHandler,
    options);

if (mode == "search")
{
    PackageSourceOperationResult<PackageSearchResult> warmup =
        await source.SearchByPrefixAsync(prefix, take: 10);
    _ = warmup.Value
        ?? throw new InvalidOperationException(warmup.Failure?.Message);

    Console.WriteLine(
        "timestampUtc\thost\tos\tarchitecture\tprocessorCount\tframework"
        + "\tmode\tprefix\ttake\ttrial\tresultCount\ttruncationReason"
        + "\trequests\telapsedMilliseconds\tcpuMilliseconds\tallocatedBytes"
        + "\tworkingSetDeltaBytes");
}
else
{
    Console.WriteLine(
        "timestampUtc\thost\tos\tarchitecture\tprocessorCount\tframework"
        + "\tmode\tprefix\ttake\ttrial\tcandidates\tmatches\tfailures"
        + "\ttruncationReason\trequests\telapsedMilliseconds"
        + "\tcpuMilliseconds\tallocatedBytes\tworkingSetDeltaBytes");
}

for (int trial = 1; trial <= trials; trial++)
{
    foreach (int take in takes)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int requestsBefore = countingHandler.RequestCount;
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        TimeSpan cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        long workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
        long started = Stopwatch.GetTimestamp();

        if (mode == "search")
        {
            PackageSourceOperationResult<PackageSearchResult> operation =
                await source.SearchByPrefixAsync(prefix, take);
            PackageSearchResult result = operation.Value
                ?? throw new InvalidOperationException(operation.Failure?.Message);
            WriteSearchResult(
                prefix,
                take,
                trial,
                result,
                Measure(
                    started,
                    requestsBefore,
                    allocatedBefore,
                    cpuBefore,
                    workingSetBefore,
                    countingHandler));
        }
        else
        {
            PackageProfileSummary? summary = null;
            await foreach (PackageProfileEvent profileEvent
                in PackageProfileQuery.ExecuteAsync(
                    source,
                    new PackagePrefixProfileRequest(prefix, take)))
            {
                if (profileEvent is PackageProfileEvent.Completed completed)
                    summary = completed.Value;
            }

            if (summary is null)
            {
                throw new InvalidOperationException(
                    "Profile query did not complete.");
            }

            WriteProfileResult(
                prefix,
                take,
                trial,
                summary,
                Measure(
                    started,
                    requestsBefore,
                    allocatedBefore,
                    cpuBefore,
                    workingSetBefore,
                    countingHandler));
        }
    }
}

return 0;

static Measurement Measure(
    long started,
    int requestsBefore,
    long allocatedBefore,
    TimeSpan cpuBefore,
    long workingSetBefore,
    CountingHandler countingHandler)
{
    var process = Process.GetCurrentProcess();
    return new(
        countingHandler.RequestCount - requestsBefore,
        Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
        GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
        process.WorkingSet64 - workingSetBefore);
}

static void WriteSearchResult(
    string prefix,
    int take,
    int trial,
    PackageSearchResult result,
    Measurement measurement) =>
    Console.WriteLine(string.Join('\t',
        CommonFields("search", prefix, take, trial),
        result.Matches.Count,
        result.TruncationReason,
        measurement.Requests,
        Math.Round(measurement.ElapsedMilliseconds, 1),
        Math.Round(measurement.CpuMilliseconds, 1),
        measurement.AllocatedBytes,
        measurement.WorkingSetDeltaBytes));

static void WriteProfileResult(
    string prefix,
    int take,
    int trial,
    PackageProfileSummary summary,
    Measurement measurement) =>
    Console.WriteLine(string.Join('\t',
        CommonFields("profile", prefix, take, trial),
        summary.Candidates,
        summary.Matches,
        summary.Failures,
        summary.TruncationReason,
        measurement.Requests,
        Math.Round(measurement.ElapsedMilliseconds, 1),
        Math.Round(measurement.CpuMilliseconds, 1),
        measurement.AllocatedBytes,
        measurement.WorkingSetDeltaBytes));

static string CommonFields(
    string mode,
    string prefix,
    int take,
    int trial) =>
    string.Join('\t',
        DateTimeOffset.UtcNow.ToString("O"),
        Environment.MachineName,
        RuntimeInformation.OSDescription,
        RuntimeInformation.OSArchitecture,
        Environment.ProcessorCount,
        RuntimeInformation.FrameworkDescription,
        mode,
        prefix,
        take,
        trial);

readonly record struct Measurement(
    int Requests,
    double ElapsedMilliseconds,
    double CpuMilliseconds,
    long AllocatedBytes,
    long WorkingSetDeltaBytes);

sealed class CountingHandler(HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    private int _requestCount;

    public int RequestCount => Volatile.Read(ref _requestCount);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        return base.SendAsync(request, cancellationToken);
    }
}
