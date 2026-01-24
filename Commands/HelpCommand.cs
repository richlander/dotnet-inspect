namespace DotnetInspector.Commands;

/// <summary>
/// Displays help information.
/// </summary>
public class HelpCommand : ICommand
{
    private readonly string? _subcommand;

    public HelpCommand(string? subcommand = null)
    {
        _subcommand = subcommand;
    }

    public Task<int> ExecuteAsync(string[] args)
    {
        // Check if help is requested for a specific subcommand
        string? topic = _subcommand ?? (args.Length > 0 ? args[0] : null);

        if (topic == "package")
        {
            ShowPackageHelp();
        }
        else
        {
            ShowGeneralHelp();
        }

        return Task.FromResult(0);
    }

    private static void ShowGeneralHelp()
    {
        Console.WriteLine("Usage: dotnet-inspect [command] <target> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  package <name|path> [version]    Inspect a NuGet package (default)");
        Console.WriteLine("  help [command]                   Show help information");
        Console.WriteLine();
        Console.WriteLine("If no command is specified, 'package' is assumed.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet-inspect dotnet-ef                    Inspect latest dotnet-ef");
        Console.WriteLine("  dotnet-inspect dotnet-ef 9.0.0              Inspect specific version");
        Console.WriteLine("  dotnet-inspect package dotnet-ef --all      Include all inspection features");
        Console.WriteLine("  dotnet-inspect ./MyTool.1.0.0.nupkg         Inspect local package");
        Console.WriteLine();
        Console.WriteLine("Run 'dotnet-inspect help package' for package command options.");
    }

    private static void ShowPackageHelp()
    {
        Console.WriteLine("Usage: dotnet-inspect [package] <name|path> [version] [options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <name|path>    NuGet package name or path to .nupkg file");
        Console.WriteLine("  [version]      Package version (latest if omitted)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --audit        Include SourceLink and determinism audit");
        Console.WriteLine("  --api          Include public API surface extraction");
        Console.WriteLine("  --deps         Include dependency analysis");
        Console.WriteLine("  --all          Include all inspection features");
        Console.WriteLine("  --json         Output as JSON");
        Console.WriteLine("  --mdf          Output as Markdown Data Format (default)");
        Console.WriteLine("  --verbose, -v  Show progress messages on stderr");
        Console.WriteLine("  --help         Show this help message");
        Console.WriteLine();
        Console.WriteLine("Default behavior (no flags):");
        Console.WriteLine("  Outputs package metadata, tool info, and RIDs.");
        Console.WriteLine("  Audit and API surface require explicit flags.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet-inspect dotnet-ef");
        Console.WriteLine("  dotnet-inspect dotnet-ef 9.0.0 --audit");
        Console.WriteLine("  dotnet-inspect package dotnet-ef --all --json");
        Console.WriteLine("  dotnet-inspect ./MyTool.1.0.0.nupkg --api");
    }
}
