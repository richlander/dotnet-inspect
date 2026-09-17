#if DEBUG
using DotnetInspector.Sections;

namespace DotnetInspector.Sections.Tests;

internal sealed class DebugEvidenceEnvelopeHostPattern(
    ExampleInspectionService service)
{
    public EvidenceInspectionEnvelope<
        ExampleInspectionContent,
        ExampleInspectionEvidence> Execute(
            ExampleInspectionRequest request) =>
        service.ExecuteWithEvidence(request);
}
#endif
