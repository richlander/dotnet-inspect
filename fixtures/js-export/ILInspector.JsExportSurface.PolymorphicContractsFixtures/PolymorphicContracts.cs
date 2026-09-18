using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.PolymorphicContractsFixtures;

public sealed record PackageSubject(string Id, string? Version);

public sealed record PackageDocumentation(string? Summary, int MemberCount);

public enum PackageSourceKind
{
    Package,
    Platform,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(PackageDocumentationOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(PackageDocumentationOutcome.Absent),
    "absent")]
public abstract record PackageDocumentationOutcome(
    PackageSubject Subject)
{
    public abstract string? Detail { get; }

    public sealed record Available(
        PackageSubject Subject,
        PackageDocumentation Documentation,
        [property: JsonPropertyName("source_kind")]
        PackageSourceKind SourceKind)
        : PackageDocumentationOutcome(Subject)
    {
        public override string Detail => "available";

        public JsonElement? Evidence { get; init; }
    }

    public sealed record Absent(
        PackageSubject Subject,
        string? Reason,
        [property: JsonIgnore(
            Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool SourcesTruncated)
        : PackageDocumentationOutcome(Subject)
    {
        public override string? Detail => null;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PackageDocumentationOutcome))]
public sealed partial class PolymorphicJsonContext
    : JsonSerializerContext;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(NumericOutcome.Value), 1)]
public abstract record NumericOutcome
{
    public sealed record Value(int Number) : NumericOutcome;
}
