using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class ApiCoordinateMatchOutput
{
    private static readonly InspectionEnvelopeJsonContract<ApiCoordinateMatchContent>
        JsonContract =
            new(
                "api-coordinate-match",
                2,
                ApiCoordinateMatchJsonContext.Default.ApiCoordinateMatchContent);

    internal static int Write(
        InspectionEnvelope<ApiCoordinateMatchContent> envelope,
        ApiCoordinateMatchOptionsParser.Success options)
    {
        foreach (InspectionDiagnostic diagnostic in envelope.Diagnostics)
            CommandError.WriteNote(diagnostic.Summary.ToString());

        bool wroteOutput;
        if (options.Envelope || options.Format == OutputFormat.Json)
        {
            wroteOutput = InspectionEnvelopeOutput.TryWrite(
                envelope,
                JsonContract,
                options.Envelope,
                options.CompactJson);
        }
        else
        {
            ApiCoordinateMatchPresentation.Render(
                envelope.Content,
                Console.Out,
                options.Format == OutputFormat.PlainText
                    ? new PlainTextFormatter()
                    : new MarkdownFormatter());
            wroteOutput = true;
        }

        int exitCode =
            wroteOutput
            && envelope.Content.Status is
                ApiCoordinateMatchStatus.Exact
                or ApiCoordinateMatchStatus.Absent
                ? 0
                : 1;
        if (options.ShareFormat is { } shareFormat
            && WorkspaceShareOutput.Write(
                envelope.PortableProjection,
                shareFormat) != 0)
        {
            exitCode = 1;
        }

        return exitCode;
    }
}
