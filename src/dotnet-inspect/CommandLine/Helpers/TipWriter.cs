using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Helper methods for writing command-specific tips to the console.
/// </summary>
public static class TipWriter
{
    /// <summary>
    /// Reports a missing required argument the Unix way: a concise error and the example
    /// tips on stderr, with a non-zero exit code. Help is reserved for explicit --help.
    /// </summary>
    public static int MissingArgumentWithTips(Command command, string message, params string[] tips)
    {
        Console.Error.WriteLine($"Error: {message}");
        Console.Error.WriteLine($"Run 'dotnet-inspect {command.Name} --help' for usage.");
        if (tips.Length > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Tips:");
            foreach (var tip in tips)
                Console.Error.WriteLine($"  {tip}");
        }
        return 1;
    }

    /// <summary>
    /// Writes package-related tips after successful package inspection.
    /// </summary>
    public static void WritePackageTips(string packageName, TipLevel tipLevel, Verbosity verbosity)
    {
        List<Tip> tips = [];

        if (verbosity < Verbosity.Detailed)
            tips.Add(new(PackageCommand.Name, $"{packageName} -v:d", "detailed metadata"));

        tips.Add(new("library", packageName, "inspect library"));
        tips.Add(new(TypeCommand.Name, $"--package {packageName}", "discover types in package"));
        tips.Add(new(FindCommand.Name, $"<pattern> --package {packageName}", "search for types"));
        tips.Add(new(DiffCommand.Name, $"--package {packageName}@<prev>..<cur>", "diff versions"));
        tips.Add(new(PackageCommand.Name, $"{packageName} --readme", "view README"));
        tips.Add(new(PackageCommand.Name, $"{packageName} --path /", "list package files"));
        tips.Add(new(PackageCommand.Name, $"{packageName} --layout", "show file tree"));

        Hints.WriteTips(tipLevel, [.. tips]);
    }

    /// <summary>
    /// Writes platform library-related tips after successful assembly inspection.
    /// </summary>
    public static void WritePlatformTips(string assemblyName, TipLevel tipLevel, Verbosity verbosity)
    {
        List<Tip> tips = [];

        if (verbosity < Verbosity.Detailed)
            tips.Add(new(assemblyName, "-v:d", "detailed metadata"));

        tips.Add(new(PackageCommand.Name, assemblyName, "inspect as NuGet package"));
        tips.Add(new(TypeCommand.Name, $"--platform {assemblyName}", "discover types"));
        tips.Add(new(FindCommand.Name, $"<pattern> --platform {assemblyName}", "search for types"));

        Hints.WriteTips(tipLevel, [.. tips]);
    }
}
