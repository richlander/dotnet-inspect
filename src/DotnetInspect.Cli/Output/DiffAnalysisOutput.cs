using DotnetInspect.Cli.Commands;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

internal static class DiffAnalysisOutput
{
    internal static readonly InspectionEnvelopeJsonContract<DiffAnalysisDocument>
        JsonContract = new(
            "diff-analysis",
            2,
            DiffAnalysisInspectionJsonContext.Default.DiffAnalysisDocument);

    internal static bool Write(
        InspectionEnvelope<DiffAnalysisDocument> inspection,
        DiffOptions options)
        => InspectionEnvelopeOutput.TryWrite(
            inspection,
            JsonContract,
            options.EnvelopeOutput,
            options.CompactJson);
}
