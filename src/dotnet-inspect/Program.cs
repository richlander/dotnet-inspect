using DotnetInspector;
using DotnetInspector.Packages;

// Initialize library configuration
NuGetCache.Initialize("dotnet-inspect");

// Handle --version explicitly to show short commit hash
if (args.Length == 1 && args[0] == "--version")
{
    Console.WriteLine(VersionInfo.Version);
    return 0;
}

// Pre-process args for implicit package command
args = CommandLineBuilder.PreprocessArgs(args);

// Create and invoke command
var rootCommand = CommandLineBuilder.CreateRootCommand();
var result = rootCommand.Parse(args);
return await result.InvokeAsync();
