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

        switch (topic)
        {
            case "package":
                ShowPackageHelp();
                break;
            case "assembly":
                ShowAssemblyHelp();
                break;
            case "api":
                ShowApiHelp();
                break;
            default:
                ShowGeneralHelp();
                break;
        }

        return Task.FromResult(0);
    }

    private static void ShowGeneralHelp()
    {
        Console.WriteLine("Usage: dotnet-inspect [command] <target> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  package <name|path> [version]    Inspect a NuGet package (default)");
        Console.WriteLine("  assembly <path>                  Inspect a .NET assembly");
        Console.WriteLine("  api <path>                       Extract public API surface");
        Console.WriteLine("  llmstxt                          Show usage examples (run this first)");
        Console.WriteLine("  help [command]                   Show help information");
        Console.WriteLine();
        Console.WriteLine("If no command is specified, 'package' is assumed.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet-inspect dotnet-ef                    Inspect latest dotnet-ef");
        Console.WriteLine("  dotnet-inspect package foo.nupkg --files    List .dll files in package");
        Console.WriteLine("  dotnet-inspect api Foo.dll --package bar.nupkg");
        Console.WriteLine();
        Console.WriteLine("Run 'dotnet-inspect help <command>' for command-specific options.");
    }

    private static void ShowPackageHelp()
    {
        Console.WriteLine("Usage: dotnet-inspect [package] <name|path> [version] [options]");
        Console.WriteLine();
        Console.WriteLine("Inspect a NuGet package to view metadata, dependencies, and file structure.");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <name|path>    NuGet package name or path to .nupkg file");
        Console.WriteLine("  [version]      Package version (latest if omitted)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --files        List DLLs (from tools/ for tool packages, lib/ otherwise)");
        Console.WriteLine("  --all          With --files: list all files in entire package");
        Console.WriteLine("  --tree         With --files: display as tree view");
        Console.WriteLine("  --versions     List available versions from nuget.org");
        Console.WriteLine("  --preview      With --versions: include prerelease versions");
        Console.WriteLine("  -n <count>     Limit results (for --versions, --files)");
        Console.WriteLine("  --deps         Include dependency analysis");
        Console.WriteLine("  --json         Output as JSON");
        Console.WriteLine("  --markout      Output as Markout (default)");
        Console.WriteLine("  --verbose      Show progress messages on stderr");
        Console.WriteLine("  --help         Show this help message");
        Console.WriteLine();
        Console.WriteLine("Verbosity (controls section detail):");
        Console.WriteLine("  -v:q           Quiet - summary only");
        Console.WriteLine("  -v:m           Minimal - summary + compact metadata");
        Console.WriteLine("  -v:n           Normal - full sections (default)");
        Console.WriteLine("  -v:d           Detailed - all sections with full tables");
        Console.WriteLine();
        Console.WriteLine("Section Filtering (H2s):");
        Console.WriteLine("  -s:1,3         Include only sections 1 and 3");
        Console.WriteLine("  -x:4           Exclude section 4");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet-inspect dotnet-ef                    # Package metadata");
        Console.WriteLine("  dotnet-inspect dotnet-ef --files            # List DLLs");
        Console.WriteLine("  dotnet-inspect dotnet-ef --files --all      # List all files");
        Console.WriteLine("  dotnet-inspect dotnet-ef --versions         # List available versions");
        Console.WriteLine("  dotnet-inspect dotnet-ef --deps             # Include dependencies");
        Console.WriteLine("  dotnet-inspect ./MyTool.1.0.0.nupkg --files");
        Console.WriteLine();
        Console.WriteLine("To inspect assemblies within a package, use the 'assembly' command:");
        Console.WriteLine("  dotnet-inspect assembly Foo.dll --package bar.nupkg --api");
    }

    private static void ShowAssemblyHelp()
    {
        Console.WriteLine("Usage: dotnet-inspect assembly [path] [options]");
        Console.WriteLine();
        Console.WriteLine("Inspect a .NET assembly file.");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  [path]         Path to assembly (optional with --package, auto-detects first DLL)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --audit        Include SourceLink and determinism audit");
        Console.WriteLine("  --package <p>  Extract assembly from package (file, name, or name@version)");
        Console.WriteLine("  --json         Output as JSON");
        Console.WriteLine("  --markout      Output as Markout (default)");
        Console.WriteLine("  --verbose      Show progress messages on stderr");
        Console.WriteLine("  --help         Show this help message");
        Console.WriteLine();
        Console.WriteLine("Section Filtering (H2s):");
        Console.WriteLine("  -s:1,2         Include only sections 1 and 2");
        Console.WriteLine("  -x:3           Exclude section 3");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet-inspect assembly MyLib.dll           # Local file");
        Console.WriteLine("  dotnet-inspect assembly MyLib.dll --audit   # With SourceLink audit");
        Console.WriteLine("  dotnet-inspect assembly --package dotnetsay # Auto-detect DLL in package");
        Console.WriteLine("  dotnet-inspect assembly lib/net8.0/System.Text.Json.dll --package System.Text.Json");
    }

    private static void ShowApiHelp()
    {
        Console.WriteLine("Usage: dotnet-inspect api [type] [options]");
        Console.WriteLine();
        Console.WriteLine("Display the public API surface of an assembly or a specific type.");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  [type]         Optional type name (full or simple). If omitted, lists all types.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --package <p>  Extract from package (file, name, or name@version)");
        Console.WriteLine("  --assembly <a> Assembly path (local file, or relative path within package)");
        Console.WriteLine("  -m, --member   Filter to specific member(s) (multiple allowed, requires type)");
        Console.WriteLine("  -n <count>     Limit output to N types or N members");
        Console.WriteLine("  --source-url   Show SourceLink URL for the type (requires embedded PDB)");
        Console.WriteLine("  --docs         Fetch and display XML doc comments from source");
        Console.WriteLine("  --json         Output as JSON");
        Console.WriteLine("  --markout      Output as Markout (default)");
        Console.WriteLine("  --verbose      Show progress messages on stderr");
        Console.WriteLine("  --help         Show this help message");
        Console.WriteLine();
        Console.WriteLine("Verbosity (controls member detail level):");
        Console.WriteLine("  -v:q           Quiet - one line per member kind (names only)");
        Console.WriteLine("  -v:m           Minimal - grouped overloads with counts (default)");
        Console.WriteLine("  -v:n           Normal - full member table");
        Console.WriteLine("  -v:d           Detailed - full member table with limit support");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet-inspect api --assembly MyLib.dll                 # List all types");
        Console.WriteLine("  dotnet-inspect api --package System.Text.Json --assembly lib/net10.0/System.Text.Json.dll");
        Console.WriteLine("  dotnet-inspect api JsonSerializer --package System.Text.Json  # Search all assemblies");
        Console.WriteLine("  dotnet-inspect api JsonSerializer --package System.Text.Json -v:q  # Compact view");
        Console.WriteLine("  dotnet-inspect api JsonSerializer --package System.Text.Json -m Deserialize  # Filter to member");
        Console.WriteLine();
        Console.WriteLine("Source link and documentation:");
        Console.WriteLine("  dotnet-inspect api JsonSerializer --package System.Text.Json --source-url");
        Console.WriteLine("  dotnet-inspect api JsonSerializer --package System.Text.Json --docs");
        Console.WriteLine("  dotnet-inspect api JsonSerializer --package System.Text.Json --source-url --docs");
        Console.WriteLine();
        Console.WriteLine("Compare APIs between versions:");
        Console.WriteLine("  diff <(dotnet-inspect api JsonSerializer --package System.Text.Json@9.0.2) \\");
        Console.WriteLine("       <(dotnet-inspect api JsonSerializer --package System.Text.Json@10.0.2)");
    }
}
