using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using DotnetInspect.Cli.Options;

namespace DotnetInspect.Cli.CommandLine;

internal static class WorkspaceShareOption
{
    internal static Option<string?> Create(string description)
    {
        var option = new Option<string?>("--share")
        {
            Description = description,
            Arity = ArgumentArity.ZeroOrOne,
        };
        option.AcceptOnlyFromAmong(
            StringComparer.OrdinalIgnoreCase,
            "packet",
            "url");
        return option;
    }

    internal static WorkspaceShareFormat? Parse(
        ParseResult parseResult,
        Option<string?> option)
    {
        if (parseResult.GetResult(option) is null)
            return null;

        return parseResult.GetValue(option)?.ToLowerInvariant() switch
        {
            null => WorkspaceShareFormat.Url,
            "packet" => WorkspaceShareFormat.Packet,
            "url" => WorkspaceShareFormat.Url,
            _ => throw new UnreachableException(
                "System.CommandLine admitted an unsupported --share value."),
        };
    }
}
