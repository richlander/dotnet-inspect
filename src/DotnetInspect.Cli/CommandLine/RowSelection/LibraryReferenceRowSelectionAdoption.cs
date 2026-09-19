using System.CommandLine;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

internal static class LibraryReferenceRowSelectionAdoption
{
    public static bool IsActive(
        ParseResult parseResult,
        SharedOptions options,
        Option<bool> referencesOption,
        Option<string?> tfmOption,
        Option<string?> typeFilterOption,
        IReadOnlyCollection<string>? effectiveSelection = null)
    {
        if (options.IsDiscoveryMode(parseResult)
            || parseResult.GetResult(options.QueryHelp)
                is { Implicit: false }
            || parseResult.GetValue(options.Print)
            || parseResult.GetValue(options.Value)
            || parseResult.GetValue(options.Urls)
            || parseResult.GetValue(options.Paths)
            || string.Equals(
                parseResult.GetValue(tfmOption),
                "all",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        IReadOnlyCollection<string> select =
            effectiveSelection ?? EffectiveSelection(
                parseResult,
                options,
                referencesOption,
                typeFilterOption);
        return select.Count == 1
            && select.Single().Equals(
                SectionNames.References,
                StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<string> EffectiveSelection(
        ParseResult parseResult,
        SharedOptions options,
        Option<bool> referencesOption,
        Option<string?> typeFilterOption)
    {
        var select = options.ParseSelect(parseResult)?.ToList() ?? [];
        if (parseResult.GetValue(referencesOption)
            && !select.Contains(
                SectionNames.References,
                StringComparer.OrdinalIgnoreCase))
        {
            select.Add(SectionNames.References);
        }
        if (!string.IsNullOrWhiteSpace(
                parseResult.GetValue(typeFilterOption)))
        {
            select.Add(SectionNames.SourceFiles);
        }

        return select;
    }
}
