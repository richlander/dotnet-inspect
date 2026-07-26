using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ILInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Views;

namespace DotnetInspector;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InspectionResult))]
[JsonSerializable(typeof(InspectionResult[]))]
[JsonSerializable(typeof(LibraryInspection))]
[JsonSerializable(typeof(LibraryInspection[]))]
[JsonSerializable(typeof(PerformanceProjection))]
[JsonSerializable(typeof(AuditSignal))]
[JsonSerializable(typeof(List<AuditSignal>))]
[JsonSerializable(typeof(LibraryIntegrationSummaryJson))]
[JsonSerializable(typeof(List<LibraryIntegrationSummaryJson>))]
[JsonSerializable(typeof(LibraryIntegrationSignalJson))]
[JsonSerializable(typeof(List<LibraryIntegrationSignalJson>))]
[JsonSerializable(typeof(LibraryInspectionFailureJson))]
[JsonSerializable(typeof(List<LibraryInspectionFailureJson>))]
[JsonSerializable(typeof(LibraryResourceJson))]
[JsonSerializable(typeof(List<LibraryResourceJson>))]
[JsonSerializable(typeof(LibraryExtensionMethodJson))]
[JsonSerializable(typeof(List<LibraryExtensionMethodJson>))]
[JsonSerializable(typeof(LibraryCustomAttributeJson))]
[JsonSerializable(typeof(List<LibraryCustomAttributeJson>))]
[JsonSerializable(typeof(TypeForwarderInfo))]
[JsonSerializable(typeof(List<TypeForwarderInfo>))]
[JsonSerializable(typeof(UnionTypeInfo))]
[JsonSerializable(typeof(List<UnionTypeInfo>))]
[JsonSerializable(typeof(IntegrationOpportunityInfo))]
[JsonSerializable(typeof(List<IntegrationOpportunityInfo>))]
[JsonSerializable(typeof(SwitchInfo))]
[JsonSerializable(typeof(List<SwitchInfo>))]
[JsonSerializable(typeof(UnsafeMemberSummary))]
[JsonSerializable(typeof(List<UnsafeMemberSummary>))]
[JsonSerializable(typeof(OptimizationOpportunitySummary))]
[JsonSerializable(typeof(List<OptimizationOpportunitySummary>))]
[JsonSerializable(typeof(RidPackageReference))]
[JsonSerializable(typeof(SourceFileInfo))]
[JsonSerializable(typeof(List<SourceFileInfo>))]
[JsonSerializable(typeof(PackageSourceFileInfo))]
[JsonSerializable(typeof(List<PackageSourceFileInfo>))]
public partial class JsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DiffDocumentView))]
internal partial class DiffJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(PackageFileJsonRow))]
internal partial class PackageFileJsonRowContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(PackageFileMultiJsonRow))]
internal partial class PackageFileMultiJsonRowContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PackageFileContent))]
internal partial class PackageFileContentJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ApiSurface))]
public partial class ApiJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ApiType))]
[JsonSerializable(typeof(DocComment))]
[JsonSerializable(typeof(SampleReference))]
[JsonSerializable(typeof(List<SampleReference>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class ApiTypeJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(ApiType))]
[JsonSerializable(typeof(DocComment))]
[JsonSerializable(typeof(SampleReference))]
[JsonSerializable(typeof(List<SampleReference>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class ApiTypeCompactJsonContext : JsonSerializerContext
{
}

// Extensions command JSON contexts
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ExtensionMethodJsonResult>))]
internal partial class ExtensionsJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ExtensionMethodJsonResult>))]
internal partial class ExtensionsCompactJsonContext : JsonSerializerContext { }

// Implements command JSON contexts
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ImplementerJsonResult>))]
internal partial class ImplementsJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ImplementerJsonResult>))]
internal partial class ImplementsCompactJsonContext : JsonSerializerContext { }

// Depends command JSON contexts
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<TypeDependencyNode>))]
internal partial class DependsJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<TypeDependencyNode>))]
internal partial class DependsCompactJsonContext : JsonSerializerContext { }

// Package search JSONL context (one compact object per line)
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NuGetSearchResult))]
internal partial class PackageSearchJsonlContext : JsonSerializerContext { }

// TypeFindResult JSONL context (one compact object per line)
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TypeFindResult))]
internal partial class TypeFindResultJsonlContext : JsonSerializerContext { }

// MemberFindResult JSONL context (one compact object per line)
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MemberFindResult))]
internal partial class MemberFindResultJsonlContext : JsonSerializerContext { }

static class JsonOutputHelper
{
    public static void Write<T>(T data, JsonTypeInfo<T> indented, JsonTypeInfo<T> compact, bool useCompact)
    {
        var typeInfo = useCompact ? compact : indented;
        Console.WriteLine(JsonSerializer.Serialize(data, typeInfo));
    }
}
