using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Research;

namespace DotnetInspector.ResearchSections;

public static class ImplementationDiffInspection
{
    public static InspectionEnvelope<ImplementationDiffDocument> Execute(
        ImplementationComparisonInput input)
        => new(
            ImplementationDiffDocumentQuery.Execute(input),
            new InspectionShare.NonProjectable(
                "comparison/endpoints",
                "Inspect Web cannot yet restore an ordered Implementation Diff endpoint pair."),
            []);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ImplementationDiffDocument))]
[JsonSerializable(typeof(InspectionEnvelope<ImplementationDiffDocument>))]
public partial class ImplementationDiffJsonContext : JsonSerializerContext;
