using DotnetInspector;
using DotnetInspector.Core;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Views;
using Markout;

// Parse --offline early (before command parsing) to configure HttpClientFactory
bool offline = args.Contains("--offline")
    || string.Equals(Environment.GetEnvironmentVariable("DOTNET_INSPECT_OFFLINE"), "1");
if (offline)
    args = args.Where(a => a != "--offline").ToArray();

// Parse --info early (before command parsing) to install counting writer
bool showInfo = args.Contains("--info")
    || string.Equals(Environment.GetEnvironmentVariable("DOTNET_INSPECT_INFO"), "1");
if (showInfo)
    args = args.Where(a => a != "--info").ToArray();

// Parse --isolated <name> and --no-nuget-cache early
string? sessionName = null;
var argList = new List<string>(args);
int isolatedIdx = argList.IndexOf("--isolated");
if (isolatedIdx >= 0 && isolatedIdx + 1 < argList.Count)
{
    sessionName = argList[isolatedIdx + 1];
    argList.RemoveAt(isolatedIdx + 1);
    argList.RemoveAt(isolatedIdx);
}
else if (isolatedIdx >= 0)
{
    argList.RemoveAt(isolatedIdx);
}
sessionName ??= Environment.GetEnvironmentVariable("DOTNET_INSPECT_ISOLATED");
if (string.IsNullOrWhiteSpace(sessionName))
    sessionName = null;
bool isolated = sessionName != null;
args = argList.ToArray();

bool noNuGetCache = args.Contains("--no-nuget-cache") || isolated;
if (args.Contains("--no-nuget-cache"))
    args = args.Where(a => a != "--no-nuget-cache").ToArray();

// Resolve cache base path: explicit env var > named session dir > default
string? cacheBasePath = Environment.GetEnvironmentVariable("DOTNET_INSPECT_CACHE_DIR");
if (isolated && cacheBasePath == null)
{
    cacheBasePath = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-{sessionName}");
}

// Initialize library configuration
DotnetInspector.Core.HttpClientFactory.Initialize(offline);
NuGetCache.Initialize("dotnet-inspect", basePath: cacheBasePath, skipNuGetCache: noNuGetCache);

// Start info tracking (installs counting writer on Console.Out)
if (showInfo)
{
    InfoTracker.Start();
    // Suppress tips when --info is active (show info instead)
    args = [.. args, "-T:q"];
}

#if DEBUG
// DEBUG-only: network guard is always on to catch unintended network access.
// Disabled for offline mode (OfflineHandler handles it) and detailed verbosity (legitimate need).
if (!offline)
    DotnetInspector.Core.HttpClientFactory.DenyNetwork();
#endif

// Handle --version explicitly to show short commit hash
if (args.Length == 1 && args[0] == "--version")
{
    Console.WriteLine(VersionInfo.Version);
    return 0;
}

// Handle --flavor to show build type (CoreCLR or NativeAOT)
if (args.Length == 1 && args[0] == "--flavor")
{
    Console.WriteLine(VersionInfo.FlavorVersion);
    return 0;
}

// Handle --release-notes to print release notes
if (args.Length == 1 && args[0] == "--release-notes")
{
    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
    using var stream = assembly.GetManifestResourceStream("dotnet-inspect.release-notes.md");
    if (stream != null)
    {
        using var reader = new StreamReader(stream);
        Console.WriteLine(reader.ReadToEnd());
    }
    return 0;
}

// Pre-process args for implicit package command (also expands -NN → -n NN)
args = CommandLineBuilder.PreprocessArgs(args);

// Install line-limiting writer when -NN shorthand was used (e.g. -30)
if (CommandLineBuilder.HeadLines is int headLines)
    Console.SetOut(new LineLimitingTextWriter(Console.Out, headLines));

// Create and invoke command
var rootCommand = CommandLineBuilder.CreateRootCommand();
var result = rootCommand.Parse(args);
int exitCode;
try
{
    exitCode = await result.InvokeAsync();
}
catch (OperationCanceledException)
{
    return 1;
}

// Write info metrics to stderr if --info was requested
if (showInfo)
{
    Console.Out.Flush();

    var elapsed = InfoTracker.Elapsed;
    var timeStr = elapsed.TotalSeconds >= 1
        ? $"{elapsed.TotalSeconds:F2}s"
        : $"{elapsed.TotalMilliseconds:F0}ms";

    var view = new InfoView
    {
        Output = CacheOutputFormatter.FormatSize(InfoTracker.CharsWritten),
        Time = timeStr,
        HTTP = InfoTracker.HttpRequests > 0
            ? $"{InfoTracker.HttpRequests} {(InfoTracker.HttpRequests == 1 ? "request" : "requests")}"
            : null,
        Cache = InfoTracker.CacheHits > 0 || InfoTracker.CacheMisses > 0
            ? $"{InfoTracker.CacheHits} {(InfoTracker.CacheHits == 1 ? "hit" : "hits")}, {InfoTracker.CacheMisses} {(InfoTracker.CacheMisses == 1 ? "miss" : "misses")}"
            : null
    };

    Console.Error.WriteLine();
    MarkoutSerializer.Serialize(view, Console.Error, InfoViewContext.Default);
}

return exitCode;
