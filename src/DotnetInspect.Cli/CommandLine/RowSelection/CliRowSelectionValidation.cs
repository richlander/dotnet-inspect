using DotnetInspect.Cli.Options;

namespace DotnetInspect.Cli.CommandLine;

internal static class CliRowSelectionValidation
{
    public static string? ValidateLineSelectionForOutput(
        bool isJsonDocument,
        CliRowSelectionLowering<string> lowering) =>
        lowering.LineIntent is not null
            && isJsonDocument
                ? "--lines and --tail-lines cannot be combined with JSON "
                    + "output; use semantic -n to select complete JSON rows."
                : null;
}
