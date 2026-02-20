using System.CommandLine;
using System.CommandLine.Help;
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
    /// Shows help action followed by command-specific tips.
    /// </summary>
    public static int ShowHelpWithTips(ParseResult parseResult, params string[] tips)
    {
        new HelpAction().Invoke(parseResult);
        Console.Error.WriteLine();
        Console.Error.WriteLine("Tips:");
        foreach (var tip in tips)
            Console.Error.WriteLine($"  {tip}");
        return 0;
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
        tips.Add(new(PackageCommand.Name, $"{packageName} --files", "list package files"));
        tips.Add(new(PackageCommand.Name, $"{packageName} --layout", "show file tree"));
        tips.Add(new(LlmsTxtCommand.Name, "", "complete usage examples"));

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
        tips.Add(new(LlmsTxtCommand.Name, "", "complete usage examples"));

        Hints.WriteTips(tipLevel, [.. tips]);
    }
}
