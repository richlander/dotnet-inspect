using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Services;

namespace DotnetInspect.Cli.Commands;

internal static class PackageApiAggregateExecution
{
    internal static bool TryGetFrameworks(
        ApiOptions options,
        ApiSourceResult source,
        out IReadOnlyList<string> frameworks)
    {
        frameworks = [];
        if (!string.Equals(
                options.Tfm,
                "all",
                StringComparison.OrdinalIgnoreCase)
            || source.PackageExtractPath is null
            || !Directory.Exists(source.SearchPath)
            )
        {
            return false;
        }

        frameworks =
            TfmSelector.GetPackageLibraryTfms(
                source.PackageExtractPath);
        return true;
    }

    internal static ApiSourceResult? ResolveFrameworkSource(
        ApiOptions options,
        ApiSourceResult source,
        string framework)
    {
        if (string.IsNullOrWhiteSpace(options.AssemblyPath)
            && !options.NamesakeLibrary)
        {
            return source with
            {
                SearchPath = source.PackageExtractPath!,
                SelectedTfm = framework,
            };
        }

        TfmSelector.PackageLibraryResolution selection =
            TfmSelector.SelectPackageLibrary(
                source.PackageExtractPath!,
                source.PackageName ?? "Package",
                options.AssemblyPath,
                framework);
        if (!selection.IsSelected)
        {
            string subject = options.NamesakeLibrary
                ? $"namesake Library for '{source.PackageName ?? "Package"}'"
                : $"Library '{options.AssemblyPath}'";
            CommandError.Write(
                $"{subject} was not found uniquely for TFM '{framework}'.");
            foreach (string candidate in selection.CandidatePaths)
            {
                CommandError.WriteLine(
                    $"  {Path.GetRelativePath(source.PackageExtractPath!, candidate)}");
            }
            return null;
        }

        return source with
        {
            SearchPath = selection.Paths[0],
            SelectedTfm = framework,
        };
    }

    internal static bool ValidateOutput(
        ApiOptions options)
    {
        if (options.Format == OutputFormat.Markdown
            && !options.IsRawOutput)
        {
            return true;
        }

        CommandError.Write(
            "--tfm all selects one package API aggregate per target framework "
            + "and supports Markdown document output. Select one --tfm for "
            + "row, JSON, count, print, or shape output.");
        return false;
    }

    internal static void WriteFrameworkHeading(
        ApiSourceResult source,
        string framework,
        bool first)
    {
        if (!first)
            Console.Out.WriteLine();

        string package =
            source.PackageName
            ?? source.ResolvedPackagePath
            ?? "Package";
        Console.Out.WriteLine(
            OutputFormatter.RenderMarkdownHeading(
                1,
                $"{package} ({framework})"));
    }
}
