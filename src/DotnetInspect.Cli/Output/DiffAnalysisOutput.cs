using DotnetInspect.Cli.Commands;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

internal static class DiffAnalysisOutput
{
    private static readonly InspectionEnvelopeJsonContract<DiffAnalysisDocument>
        s_jsonContract = new(
            "diff-analysis",
            2,
            DiffAnalysisInspectionJsonContext.Default.DiffAnalysisDocument);

    internal static bool Write(
        InspectionEnvelope<DiffAnalysisDocument> inspection,
        DiffOptions options)
        => InspectionEnvelopeOutput.TryWrite(
            inspection,
            s_jsonContract,
            options.EnvelopeOutput,
            options.CompactJson);
}
