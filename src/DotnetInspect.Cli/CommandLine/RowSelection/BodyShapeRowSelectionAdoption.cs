using System.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

/// <summary>
/// Semantic row adoption for an exact lone <c>Body Shapes</c> or
/// <c>Body Shape Summary</c> section on <c>library</c>, <c>type</c>, and
/// <c>member</c>: one occurrence or one summary group is one item for
/// <c>-n</c>, <c>--tail</c>, and <c>--rows</c>.
/// </summary>
internal static class BodyShapeRowSelectionAdoption
{
    public static bool IsActive(
        ParseResult parseResult,
        SharedOptions options,
        IReadOnlyCollection<string>? effectiveSelection = null)
    {
        if (options.IsDiscoveryMode(parseResult)
            || parseResult.GetResult(options.QueryHelp)
                is { Implicit: false }
            || parseResult.GetValue(options.Print)
            || parseResult.GetValue(options.Value)
            || parseResult.GetValue(options.Urls)
            || parseResult.GetValue(options.Paths))
        {
            return false;
        }

        IReadOnlyCollection<string> select =
            effectiveSelection ?? EffectiveSelection(parseResult, options);
        return select.Count == 1
            && IsBodyShapeSection(select.Single());
    }

    /// <summary>
    /// The library predicate: <c>--references</c>, a <c>-t</c> type filter,
    /// and <c>--tfm all</c> add sections or inspections, so they stay outside
    /// the declaration.
    /// </summary>
    public static bool IsActiveForLibrary(
        ParseResult parseResult,
        SharedOptions options,
        Option<bool> referencesOption,
        Option<string?> tfmOption,
        Option<string?> typeFilterOption,
        IReadOnlyCollection<string>? effectiveSelection = null)
    {
        if (effectiveSelection is null
            && (parseResult.GetValue(referencesOption)
                || !string.IsNullOrWhiteSpace(
                    parseResult.GetValue(typeFilterOption))))
        {
            return false;
        }

        return !string.Equals(
                parseResult.GetValue(tfmOption),
                "all",
                StringComparison.OrdinalIgnoreCase)
            && IsActive(parseResult, options, effectiveSelection);
    }

    private static bool IsBodyShapeSection(string section) =>
        section.Equals(
            SectionNames.BodyShapes,
            StringComparison.OrdinalIgnoreCase)
        || section.Equals(
            SectionNames.BodyShapeSummary,
            StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyCollection<string> EffectiveSelection(
        ParseResult parseResult,
        SharedOptions options)
    {
        string[]? select = options.ParseSelect(parseResult);
        if (select is { Length: > 0 })
            return select;

        return BodyKindQueryOptions.TryExtract(
                parseResult.GetValue(options.RowWhere) ?? [],
                out BodyKindQueryOptions query,
                out _,
                out _)
            && query.HasFilter
                ? [SectionNames.BodyShapes]
                : [];
    }
}
