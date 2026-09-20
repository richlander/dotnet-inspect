using System.CommandLine;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

internal static class LibraryEcosystemDependencyRowSelectionAdoption
{
    public static bool IsActive(
        ParseResult parseResult,
        SharedOptions options,
        Option<string?> tfmOption,
        IReadOnlyCollection<string>? effectiveSelection)
    {
        if (!CanSelectRows(parseResult, options, tfmOption))
            return false;

        IReadOnlyCollection<string>? sections =
            ResolveSections(effectiveSelection);
        return sections is { Count: 1 }
            && sections.Contains(SectionNames.EcosystemDependencies);
    }

    public static bool IsMultiSectionSelection(
        ParseResult parseResult,
        SharedOptions options,
        Option<string?> tfmOption,
        IReadOnlyCollection<string>? effectiveSelection)
    {
        if (!CanSelectRows(parseResult, options, tfmOption))
            return false;

        IReadOnlyCollection<string>? sections =
            ResolveSections(effectiveSelection);
        return sections is { Count: > 1 }
            && sections.Contains(SectionNames.EcosystemDependencies);
    }

    public static bool HasExplicitSelection(
        ParseResult parseResult,
        SharedOptions options) =>
        parseResult.GetResult(options.Limit) is { Implicit: false }
        || parseResult.GetResult(options.Rows) is { Implicit: false }
        || parseResult.GetResult(options.Head) is { Implicit: false }
        || parseResult.GetResult(options.Tail) is { Implicit: false }
        || parseResult.GetResult(options.Lines) is { Implicit: false }
        || parseResult.GetResult(options.TailLines) is { Implicit: false };

    private static bool CanSelectRows(
        ParseResult parseResult,
        SharedOptions options,
        Option<string?> tfmOption) =>
        !options.IsDiscoveryMode(parseResult)
        && parseResult.GetResult(options.QueryHelp)
            is not { Implicit: false }
        && !parseResult.GetValue(options.Print)
        && !parseResult.GetValue(options.Value)
        && !parseResult.GetValue(options.Urls)
        && !parseResult.GetValue(options.Paths)
        && !string.Equals(
            parseResult.GetValue(tfmOption),
            "all",
            StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyCollection<string>? ResolveSections(
        IReadOnlyCollection<string>? selection)
    {
        if (selection is not { Count: > 0 })
            return null;

        SectionCatalog<DotnetInspect.Cli.Models.LibraryInspection> sections =
            LibrarySections.CreateCatalog().Sections;
        var resolved =
            SelectResolver.ResolveSelectAsSections(
                [.. selection],
                sections.SelectableSectionNames,
                sections.InfoSectionNames,
                sections.SelectionCategoryMap,
                selectDefault: false);
        return resolved.HasError ? null : resolved.Sections;
    }
}
