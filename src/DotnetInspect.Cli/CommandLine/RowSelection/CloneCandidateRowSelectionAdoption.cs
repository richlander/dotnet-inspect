using System.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

internal static class CloneCandidateRowSelectionAdoption
{
    public static bool IsActive(
        ParseResult parseResult,
        SharedOptions options)
    {
        if (options.IsDiscoveryMode(parseResult)
            || parseResult.GetResult(options.QueryHelp)
                is { Implicit: false })
        {
            return false;
        }

        string[]? select = options.ParseSelect(parseResult);
        if (select?.Contains(
                SectionNames.CloneCandidates,
                StringComparer.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (select is { Length: > 0 }
            || options.ParseSelectDefault(parseResult))
        {
            return false;
        }

        return CloneCandidateQueryOptions.TryExtract(
                parseResult.GetValue(options.RowWhere) ?? [],
                out CloneCandidateQueryOptions query,
                out _,
                out _)
            && query.HasPredicates;
    }
}
