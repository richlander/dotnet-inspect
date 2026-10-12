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
        if (command.Command.Name == "router"
            || !IsAll(OptionValue(command, TfmOptionName)))
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

        string? package = OptionValue(command, PackageOptionName);
        // A version range (diff) names no single package to suggest.
        error = string.IsNullOrWhiteSpace(package) || package.Contains("..", StringComparison.Ordinal)
            ? "--tfm all applies only to 'package' discovery."
            : $"--tfm all applies only to 'package' discovery. Use 'dotnet-inspect package {package} --tfm all'.";
        return true;
    }

    private static bool IsAll(string? value) =>
        string.Equals(value, "all", StringComparison.OrdinalIgnoreCase);

    private static string? OptionValue(CommandResult command, string name) =>
        command.Children
            .OfType<OptionResult>()
            .FirstOrDefault(option => option.Option.Name == name)
            is { Tokens.Count: > 0 } result
                ? result.Tokens[^1].Value
                : null;
}
