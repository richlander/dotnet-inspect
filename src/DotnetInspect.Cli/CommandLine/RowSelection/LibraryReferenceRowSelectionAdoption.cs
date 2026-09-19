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
        Option<string?> tfmOption)
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

        string[]? select = options.ParseSelect(parseResult);
        if (select is [var section])
        {
            return section.Equals(
                SectionNames.References,
                StringComparison.OrdinalIgnoreCase);
        }

        return select is not { Length: > 0 }
            && parseResult.GetValue(referencesOption);
    }
}
