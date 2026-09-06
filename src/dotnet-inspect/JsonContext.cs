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
[JsonSerializable(typeof(List<VersionFeedJson>))]
[JsonSerializable(typeof(LibraryIntegrationSignalJson))]
[JsonSerializable(typeof(List<LibraryIntegrationSignalJson>))]
[JsonSerializable(typeof(LibraryInspectionFailureJson))]
[JsonSerializable(typeof(List<LibraryInspectionFailureJson>))]
[JsonSerializable(typeof(BodyShapeJsonMatch))]
[JsonSerializable(typeof(List<BodyShapeJsonMatch>))]
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
[JsonSerializable(typeof(PackageSourceLinkIssue))]
[JsonSerializable(typeof(List<PackageSourceLinkIssue>))]
[JsonSerializable(typeof(PackageSourceLinkFile))]
[JsonSerializable(typeof(List<PackageSourceLinkFile>))]
[JsonSerializable(typeof(PackageSourceAvailability))]
[JsonSerializable(typeof(PackageSourceIntegrity))]
[JsonSerializable(typeof(MemorySafetyRulesResult.Unavailable), TypeInfoPropertyName = "UnavailableMemorySafetyRules")]
[JsonSerializable(typeof(MemorySafetyMemberContractResult.Unavailable), TypeInfoPropertyName = "UnavailableMemorySafetyContract")]
public partial class JsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PackageInspectionJson))]
[JsonSerializable(typeof(PackageInspectionJson[]))]
internal partial class PackageInspectionJsonContext : JsonSerializerContext
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

/// <summary>
/// Serialization contract for <c>match --body --json</c>
/// (<see cref="MatchBodyDocument"/>): snake-case names, omitted nulls, and string enums
/// to match <see cref="ILInspector.Analysis.StructuralCloneComparisonDocumentJsonContext"/>'s
/// contract for the nested <see cref="ILInspector.Analysis.StructuralCloneComparisonDocument"/>.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MatchBodyDocument))]
internal partial class MatchBodyDocumentJsonContext : JsonSerializerContext
{
}

/// <inheritdoc cref="MatchBodyDocumentJsonContext"/>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MatchBodyDocument))]
internal partial class MatchBodyDocumentCompactJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Serialization contract for <c>match --similar --json</c>
/// (<see cref="MatchDiscoveryDocument"/>). Structured output retains every query-returned
/// candidate, outcome, blocker, limit, and receipt; <c>--top</c> bounds rendered text only.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MatchDiscoveryDocument))]
internal partial class MatchDiscoveryDocumentJsonContext : JsonSerializerContext
{
}

/// <inheritdoc cref="MatchDiscoveryDocumentJsonContext"/>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MatchDiscoveryDocument))]
internal partial class MatchDiscoveryDocumentCompactJsonContext : JsonSerializerContext
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
[JsonSerializable(typeof(PackageFileContentText))]
internal partial class PackageFileContentJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ApiSurface))]
[JsonSerializable(typeof(MemorySafetyRulesResult.Unavailable), TypeInfoPropertyName = "UnavailableMemorySafetyRules")]
[JsonSerializable(typeof(MemorySafetyMemberContractResult.Unavailable), TypeInfoPropertyName = "UnavailableMemorySafetyContract")]
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
[JsonSerializable(typeof(MemorySafetyRulesResult.Unavailable), TypeInfoPropertyName = "UnavailableMemorySafetyRules")]
[JsonSerializable(typeof(MemorySafetyMemberContractResult.Unavailable), TypeInfoPropertyName = "UnavailableMemorySafetyContract")]
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
[JsonSerializable(typeof(MemorySafetyRulesResult.Unavailable), TypeInfoPropertyName = "UnavailableMemorySafetyRules")]
[JsonSerializable(typeof(MemorySafetyMemberContractResult.Unavailable), TypeInfoPropertyName = "UnavailableMemorySafetyContract")]
public partial class ApiTypeCompactJsonContext : JsonSerializerContext
{
}

// The AnnotatedSourceDocument wire shape is owned by ILInspector.Decompiler
// (AnnotatedSourceDocumentJsonContext / AnnotatedSourceDocumentCompactJsonContext), because the
// document has more than one producer and one consumer contract.

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

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<TypeFindResult>))]
internal partial class TypeFindResultJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<TypeFindResult>))]
internal partial class TypeFindResultCompactJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<MemberFindResult>))]
internal partial class MemberFindResultJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<MemberFindResult>))]
internal partial class MemberFindResultCompactJsonContext : JsonSerializerContext { }

static class JsonOutputHelper
{
    public static void Write<T>(T data, JsonTypeInfo<T> indented, JsonTypeInfo<T> compact, bool useCompact)
    {
        var typeInfo = useCompact ? compact : indented;
        Console.WriteLine(JsonSerializer.Serialize(data, typeInfo));
    }
}
