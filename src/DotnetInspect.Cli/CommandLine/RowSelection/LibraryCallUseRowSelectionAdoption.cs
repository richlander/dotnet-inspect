using System.CommandLine;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

internal static class LibraryCallUseRowSelectionAdoption
{
    public static bool IsActive(
        ParseResult parseResult,
        SharedOptions options)
    {
        if (options.IsDiscoveryMode(parseResult)
            || parseResult.GetResult(options.QueryHelp)
                is { Implicit: false }
            || options.ParseSelectDefault(parseResult))
        {
            return false;
        }

        if (parseResult.GetResult(options.Select) is not
            { Implicit: false })
        {
            return true;
        }

        return LibraryCallUseSections.TryGetSemanticRows(
            options.ParseSelect(parseResult),
            out _);
    }
}
