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

// Parse --trace-mermaid early to subscribe before any request/cache/network breadcrumbs.
bool showTraceMermaid = args.Contains("--trace-mermaid")
    || string.Equals(Environment.GetEnvironmentVariable("DOTNET_INSPECT_TRACE_MERMAID"), "1");
if (showTraceMermaid)
    args = args.Where(a => a != "--trace-mermaid").ToArray();

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
// The IR invariant check is armed by default so any host that runs the
// decompiler pipeline validates it (#3267). The shipped tool is the one
// sanctioned opt-out: users are not developing the pipeline, so the per-pass
// tree walk is pure overhead on the decompile hot path. Setting
// DOTNET_INSPECT_IR_INVARIANTS=1 (or full) overrides this and arms the shipped
// tool for debugging.
ILInspector.Decompiler.Pipeline.IrInvariants.DisableForShippedTool();
// Wire the tool-tier SourceLink index cache into the engine's dependency-inversion seam.
ILInspector.Metadata.SourceLinkService.DefaultCache = DotnetInspector.Services.CoreSourceLinkIndexCache.Instance;

// Start info tracking (installs counting writer on Console.Out)
if (showInfo)
{
    InfoTracker.Start();
    // Suppress tips when --info is active (show info instead)
    args = [.. args, "-T:q"];
}

#if DEBUG
// DEBUG-only: log every managed HTTP request with its traffic kind to catch unintended network access.
// Disabled for offline mode (OfflineHandler handles it) and detailed verbosity (legitimate need).
if (!offline)
    DotnetInspector.Core.HttpClientFactory.EnableNetworkTrafficLogging();
#endif

using var traceMermaid = showTraceMermaid ? RequestMermaidDiagram.Start() : null;
using var requestScope = RequestTelemetry.Scope(string.Join(' ', args), "cli invocation");
if (showTraceMermaid)
    RequestTelemetry.Breadcrumb("request", string.Join(' ', args));

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
var argsBeforePreprocess = args;
args = CommandLineBuilder.PreprocessArgs(args);
if (showTraceMermaid && args.Length > 0 && argsBeforePreprocess.FirstOrDefault() != args[0])
    RequestTelemetry.Breadcrumb("preprocess", $"{string.Join(' ', argsBeforePreprocess)} -> {string.Join(' ', args)}");

// Install line-limiting writer when -NN shorthand was used (e.g. -30).
// With --rows, -n/-NN is interpreted by commands as per-table row limits. The
// head/tail conflict is validated at parse time by the command validator in
// SharedOptions.AddOutputOptionsTo against the real System.CommandLine parse (so it
// covers =-syntax and concatenated forms the arg-preprocessor token scan misses),
// which is why no --rows gate remains here.
var rowLimitMode = args.Any(a => a == "--rows" || a.StartsWith("--rows=", StringComparison.Ordinal));

if (CommandLineBuilder.TryGetStaleDirectionFlagError(args, out var staleDirectionError))
{
    Console.Error.WriteLine($"Error: {staleDirectionError}");
    return 1;
}

// Line/tail windows apply only outside --rows mode; in --rows mode the count is a
// per-table data-row window rendered by the commands, not an output-line window.
if (!rowLimitMode && CommandLineBuilder.HeadLines is int headLines)
    Console.SetOut(new LineLimitingTextWriter(Console.Out, headLines));

// Install tail writer when --tail N was used
TailLineLimitingTextWriter? tailWriter = null;
if (!rowLimitMode && CommandLineBuilder.TailLines is int tailLines)
{
    tailWriter = new TailLineLimitingTextWriter(Console.Out, tailLines);
    Console.SetOut(tailWriter);
}

// Create and invoke command
var rootCommand = CommandLineBuilder.CreateRootCommand();
var result = rootCommand.Parse(args);
if (result.Errors.Count > 0)
{
    foreach (var error in result.Errors)
        Console.Error.WriteLine(FormatParseError(error.Message));
    return 1;
}
int exitCode;
try
{
    exitCode = await CommandLineBuilder.InvokeAsync(result);
}
catch (RowWindowValidationException ex)
{
    // Defensive: the --rows head/tail window is rejected at parse time by the
    // command validator (SharedOptions.AddOutputOptionsTo), so this is not the
    // primary path. It converts any escaped validation throw into the clean CLI
    // error contract instead of an unhandled-exception stack trace.
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
catch (OperationCanceledException)
{
    return 1;
}

// Flush tail writer to emit only the last N lines
if (tailWriter != null)
    tailWriter.FlushTail();

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
            : null,
        Readme = InfoTracker.GetDetail("readme")
    };

    Console.Error.WriteLine();
    MarkoutSerializer.Serialize(view, Console.Error, InfoViewContext.Default);
}

static string FormatParseError(string message)
{
    if (message.StartsWith("Cannot parse argument '", StringComparison.Ordinal)
        && TryParseCannotParseArgument(message, out var value, out var option, out var type))
    {
        var expected = type switch
        {
            "System.Int32" or "System.Nullable`1[System.Int32]" => "an integer",
            _ => "a valid value",
        };
        return $"Error: Cannot parse value '{value}' for option '{option}' as {expected}.";
    }

    return message.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
        ? message
        : $"Error: {message}";
}

static bool TryParseCannotParseArgument(string message, out string value, out string option, out string type)
{
    value = "";
    option = "";
    type = "";
    const string prefix = "Cannot parse argument '";
    int valueStart = prefix.Length;
    int valueEnd = message.IndexOf('\'', valueStart);
    const string middle = " for option '";
    if (valueEnd < 0 || !message.AsSpan(valueEnd + 1).StartsWith(middle, StringComparison.Ordinal))
        return false;

    int optionStart = valueEnd + 1 + middle.Length;
    int optionEnd = message.IndexOf('\'', optionStart);
    const string typeMarker = " as expected type '";
    int typeStart = message.IndexOf(typeMarker, optionEnd + 1, StringComparison.Ordinal);
    if (optionEnd < 0 || typeStart < 0)
        return false;

    typeStart += typeMarker.Length;
    int typeEnd = message.IndexOf('\'', typeStart);
    if (typeEnd < 0)
        return false;

    value = message[valueStart..valueEnd];
    option = message[optionStart..optionEnd];
    type = message[typeStart..typeEnd];
    return true;
}

if (traceMermaid != null)
{
    Console.Error.WriteLine();
    traceMermaid.WriteTo(Console.Error);
}

var cacheMaintenance = CoreCache.CancelAndWaitForMaintenance(TimeSpan.FromMilliseconds(100));
if (cacheMaintenance.BytesFreed > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Removed {CacheOutputFormatter.FormatSize(cacheMaintenance.BytesFreed)} from obsolete cache entries.");
}

return exitCode;
