using DotnetInspect.Cli.Options;

namespace DotnetInspect.Cli.CommandLine;

internal static class CliRowSelectionValidation
{
    public static string? ValidateLineSelectionForOutput(
        bool isJsonDocument,
        CliRowSelectionLowering<string> lowering) =>
        lowering.LineIntent is not null
            && isJsonDocument
                ? "Rendered-line selection cannot be combined with JSON output."
                : null;
}
