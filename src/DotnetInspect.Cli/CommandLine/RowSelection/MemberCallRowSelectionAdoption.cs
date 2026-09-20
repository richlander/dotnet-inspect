using System.CommandLine;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

internal static class MemberCallRowSelectionAdoption
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
        return select is [var section]
            && section.Equals(
                SectionNames.Calls,
                StringComparison.OrdinalIgnoreCase);
    }
}
