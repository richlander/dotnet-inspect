using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// The structural rows and settlement of one completed semantic Find answer.
/// </summary>
public sealed record ApiFindSemanticTable(
    ImmutableArray<FindSemanticFinding> Rows,
    FindSemanticResultCompletion Completion,
    ImmutableArray<FindSemanticResultFailure> Failures,
    ImmutableArray<string> Unsearched)
{
    public bool IsIncomplete =>
        Completion is not FindSemanticResultCompletion.Exhausted;
}

/// <summary>The complete owner-issued semantic Find answer.</summary>
public sealed record ApiFindSemanticDocument(
    ImmutableArray<FindSemanticFinding> Findings,
    FindSemanticResultCompletion Completion,
    ImmutableArray<FindSemanticResultFailure> Failures,
    ImmutableArray<string> Unsearched)
{
    public bool IsIncomplete =>
        Completion is not FindSemanticResultCompletion.Exhausted;
}

/// <summary>
/// Projects one completed semantic Find answer to host-neutral structural or
/// document content without reselecting its findings.
/// </summary>
public static class ApiFindSemanticInspection
{
    private static readonly InspectionShare Share =
        new InspectionShare.NonProjectable(
            "api-find/semantic/share",
            "Semantic API Find has no canonical Workspace Share projection.");

    public static InspectionEnvelope<ApiFindSemanticTable> Table(
        FindSemanticResult result,
        IEnumerable<InspectionDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new(
            new(
                result.Findings,
                result.Completion,
                result.Failures,
                result.Unsearched),
            Share,
            diagnostics);
    }

    public static InspectionEnvelope<ApiFindSemanticDocument> Document(
        FindSemanticResult result,
        IEnumerable<InspectionDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new(
            new(
                result.Findings,
                result.Completion,
                result.Failures,
                result.Unsearched),
            Share,
            diagnostics);
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(InspectionEnvelope<ApiFindSemanticTable>))]
[JsonSerializable(typeof(InspectionEnvelope<ApiFindSemanticDocument>))]
public partial class ApiFindSemanticInspectionJsonContext
    : JsonSerializerContext;
