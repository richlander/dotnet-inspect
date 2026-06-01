using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Commands;
using DotnetInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Packages;
using DotnetInspector.Views;

namespace DotnetInspector;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InspectionResult))]
[JsonSerializable(typeof(LibraryInspection))]
[JsonSerializable(typeof(LibraryInspection[]))]
[JsonSerializable(typeof(AuditSignal))]
[JsonSerializable(typeof(List<AuditSignal>))]
[JsonSerializable(typeof(RidPackageReference))]
public partial class JsonContext : JsonSerializerContext
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
[JsonSerializable(typeof(List<ExtensionMethodResult>))]
internal partial class ExtensionsJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ExtensionMethodResult>))]
internal partial class ExtensionsCompactJsonContext : JsonSerializerContext { }

// Implements command JSON contexts
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ImplementerResult>))]
internal partial class ImplementsJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ImplementerResult>))]
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

// Source command JSON contexts
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SourceListResult))]
[JsonSerializable(typeof(SourceDetailResult))]
[JsonSerializable(typeof(ILOffsetResult))]
internal partial class SourceJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SourceListResult))]
[JsonSerializable(typeof(SourceDetailResult))]
[JsonSerializable(typeof(ILOffsetResult))]
internal partial class SourceCompactJsonContext : JsonSerializerContext { }

static class JsonOutputHelper
{
    public static void Write<T>(T data, JsonTypeInfo<T> indented, JsonTypeInfo<T> compact, bool useCompact)
    {
        var typeInfo = useCompact ? compact : indented;
        Console.WriteLine(JsonSerializer.Serialize(data, typeInfo));
    }
}
