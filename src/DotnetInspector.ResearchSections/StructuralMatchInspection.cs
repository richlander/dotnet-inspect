using System.Reflection.Metadata;

using DotnetInspector.Sections;

using ILInspector.Research;

namespace DotnetInspector.ResearchSections;

public static class StructuralMatchInspection
{
    public static InspectionEnvelope<ResearchMatchResult> Execute(
        string assemblyPath,
        MethodDefinitionHandle left,
        MethodDefinitionHandle right)
        => new(
            ResearchMatch.Compare(assemblyPath, left, right),
            new InspectionShare.NonProjectable(
                "structural-match/endpoints",
                "Inspect Web cannot yet restore an ordered same-image "
                    + "Structural Match endpoint pair."),
            []);
}
