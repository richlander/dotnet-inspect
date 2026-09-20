using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
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
[JsonSerializable(typeof(AssemblyResolutionProvenance))]
[JsonSerializable(typeof(AssemblyResolutionProvenance.PackageAsset))]
[JsonSerializable(typeof(AssemblyResolutionProvenance.PlatformAsset))]
[JsonSerializable(typeof(AssemblyResolutionProvenance.ProjectAsset))]
[JsonSerializable(typeof(AssemblyResolutionProvenance.LocalAsset))]
[JsonSerializable(typeof(AssemblyResolutionProvenance.EmbeddedAsset))]
[JsonSerializable(typeof(AssemblyResolutionProvenance.DesignatedAsset))]
public partial class ImplementationDiffJsonContext : JsonSerializerContext;
