using System.CommandLine;
using System.CommandLine.Parsing;

namespace DotnetInspect.Cli.CommandLine;

/// <summary>
/// Admits <c>--tfm all</c> only on the <c>package</c> command, as
/// docs/design/package-tfm-all-discovery.md owns. Every other command, including
/// <c>package</c> subcommands and non-package targets, fails before acquisition
/// rather than inspecting several frameworks or treating <c>all</c> as a framework.
/// </summary>
internal static class TfmAllAdmission
{
    private const string TfmOptionName = "--tfm";
    private const string PackageOptionName = "--package";

    public static bool TryGetError(ParseResult parseResult, out string? error)
    {
        CommandResult command = parseResult.CommandResult;
        // A subcommand may inherit its parent's --tfm,
        // so the value is read from the command and every ancestor.
        if (command.Command.Name == "router"
            || !IsAll(LastToken(command, TfmOptionName)))
        {
            error = null;
            return false;
        }

        if (command.Command.Name == "package"
            && command.Parent is CommandResult { Parent: null })
        {
            error = null;
            return false;
        }

        // Only one non-range package target names a single package to suggest.
        string? package = SingleToken(command, PackageOptionName);
        error = package is null || package.Contains("..", StringComparison.Ordinal)
            ? "--tfm all applies only to 'package' discovery."
            : $"--tfm all applies only to 'package' discovery. Use 'dotnet-inspect package {package} --tfm all'.";
        return true;
    }

    private static bool IsAll(string? value) =>
        string.Equals(value, "all", StringComparison.OrdinalIgnoreCase);

    private static string? LastToken(CommandResult command, string name) =>
        Find(command, name) is { Tokens.Count: > 0 } result
            ? result.Tokens[^1].Value
            : null;

    private static string? SingleToken(CommandResult command, string name) =>
        Find(command, name) is { Tokens.Count: 1 } result
            && !string.IsNullOrWhiteSpace(result.Tokens[0].Value)
                ? result.Tokens[0].Value
                : null;

    private static OptionResult? Find(CommandResult command, string name)
    {
        for (SymbolResult? scope = command; scope is not null; scope = scope.Parent)
        {
            if (scope is CommandResult current
                && current.Children
                    .OfType<OptionResult>()
                    .FirstOrDefault(option => option.Option.Name == name)
                    is { } result)
            {
                return result;
            }
        }

        return null;
    }
}
