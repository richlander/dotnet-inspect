using DotnetInspector.Options;

namespace DotnetInspector.CommandLine;

internal static class CliRowSelectionValidation
{
    public static string? ValidateLineSelectionForOutput(
        OutputFormat format,
        CliRowSelectionLowering<string> lowering) =>
        lowering.LineIntent is not null
            && format == OutputFormat.Json
                ? "--lines and --tail-lines cannot be combined with JSON "
                    + "output; use semantic -n to select complete JSON rows."
                : null;
}
