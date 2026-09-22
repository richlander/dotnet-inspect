using DotnetInspect.Cli.Commands;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using ILInspector.Research;

namespace DotnetInspect.Cli.Output;

internal static class ImplementationDiffOutput
{
    static readonly InspectionEnvelopeJsonContract<ImplementationDiffDocument>
        JsonContract = new(
            "implementation-diff",
            2,
            ImplementationDiffJsonContext.Default.ImplementationDiffDocument);

    internal static int Write(
        InspectionEnvelope<ImplementationDiffDocument> envelope,
        DiffOptions options)
    {
        foreach (InspectionDiagnostic diagnostic in envelope.Diagnostics)
            CommandError.WriteNote(diagnostic.Summary.ToString());

        return InspectionEnvelopeOutput.TryWrite(
            envelope,
            JsonContract,
            options.EnvelopeOutput,
            options.CompactJson)
            ? 0
            : 1;
    }
}
