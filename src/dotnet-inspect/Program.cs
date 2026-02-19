using DotnetInspector;
using DotnetInspector.Output;
using DotnetInspector.Packages;

// Parse --offline early (before command parsing) to configure HttpClientFactory
bool offline = args.Contains("--offline");
if (offline)
    args = args.Where(a => a != "--offline").ToArray();

// Initialize library configuration
DotnetInspector.Core.HttpClientFactory.Initialize(offline);
NuGetCache.Initialize("dotnet-inspect");

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
return await result.InvokeAsync();
