using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "finding_kind")]
[JsonDerivedType(typeof(TypeFindResult), "type")]
[JsonDerivedType(typeof(MemberFindResult), "member")]
public abstract record ApiFindRow;

public sealed record ApiFindTable(
    IReadOnlyList<ApiFindRow> Rows,
    bool IsIncomplete);

public sealed record ApiFindDocument(
    IReadOnlyList<ApiFindRow> Rows,
    IReadOnlyList<string> UnmatchedPatterns,
    bool IsIncomplete);

public static class ApiFindInspection
{
    private static readonly InspectionShare Share =
        new InspectionShare.NonProjectable(
            "api-find/share",
            "API Find has no canonical Workspace Share projection.");

    public static InspectionEnvelope<int> Count(
        int count,
        bool isIncomplete,
        IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(count, Share, Diagnostics(isIncomplete, diagnostics));

    public static InspectionEnvelope<ApiFindTable> Table(
        IReadOnlyList<TypeFindResult> types,
        IReadOnlyList<MemberFindResult> members,
        bool isIncomplete,
        IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(new(Rows(types, members), isIncomplete),
            Share, Diagnostics(isIncomplete, diagnostics));

    public static InspectionEnvelope<ApiFindDocument> Document(
        IReadOnlyList<TypeFindResult> types,
        IReadOnlyList<MemberFindResult> members,
        IReadOnlyList<string> unmatchedPatterns,
        bool isIncomplete,
        IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(new(Rows(types, members), unmatchedPatterns, isIncomplete),
            Share, Diagnostics(isIncomplete, diagnostics));

    private static ApiFindRow[] Rows(
        IReadOnlyList<TypeFindResult> types,
        IReadOnlyList<MemberFindResult> members) =>
        [.. members, .. types.Where(static row =>
            row.Match != TypeFindMatchKind.NotFound)];

    private static IEnumerable<InspectionDiagnostic> Diagnostics(
        bool isIncomplete,
        IEnumerable<InspectionDiagnostic>? diagnostics)
    {
        foreach (InspectionDiagnostic diagnostic in diagnostics ?? [])
            yield return diagnostic;
        if (isIncomplete)
        {
            yield return new(
                "api-find/incomplete",
                InspectionDiagnosticSeverity.Error,
                "The Find answer is incomplete.");
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ApiFindDocument))]
public partial class ApiFindDocumentJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ApiFindDocument))]
public partial class ApiFindDocumentCompactJsonContext : JsonSerializerContext;
