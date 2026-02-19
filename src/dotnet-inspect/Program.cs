using DotnetInspector;
using DotnetInspector.Output;
using DotnetInspector.Packages;

// Parse --offline early (before command parsing) to configure HttpClientFactory
bool offline = args.Contains("--offline")
    || string.Equals(Environment.GetEnvironmentVariable("DOTNET_INSPECT_OFFLINE"), "1");
if (offline)
    args = args.Where(a => a != "--offline").ToArray();

// Parse --isolated and --no-nuget-cache early
bool isolated = args.Contains("--isolated")
    || string.Equals(Environment.GetEnvironmentVariable("DOTNET_INSPECT_ISOLATED"), "1");
if (isolated)
    args = args.Where(a => a != "--isolated").ToArray();

bool noNuGetCache = args.Contains("--no-nuget-cache") || isolated;
if (args.Contains("--no-nuget-cache"))
    args = args.Where(a => a != "--no-nuget-cache").ToArray();

// Resolve cache base path: explicit env var > isolated temp dir > default
string? cacheBasePath = Environment.GetEnvironmentVariable("DOTNET_INSPECT_CACHE_DIR");
string? isolatedTempDir = null;
if (isolated && cacheBasePath == null)
{
    isolatedTempDir = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-{Path.GetRandomFileName()}");
    cacheBasePath = isolatedTempDir;
}

// Initialize library configuration
DotnetInspector.Core.HttpClientFactory.Initialize(offline);
NuGetCache.Initialize("dotnet-inspect", basePath: cacheBasePath, skipNuGetCache: noNuGetCache);

// Handle --version explicitly to show short commit hash
if (args.Length == 1 && args[0] == "--version")
{
    Console.WriteLine(VersionInfo.Version);
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
var exitCode = await result.InvokeAsync();

// Clean up isolated temp dir
if (isolatedTempDir != null && Directory.Exists(isolatedTempDir))
{
    try { Directory.Delete(isolatedTempDir, recursive: true); } catch { }
}

return exitCode;
