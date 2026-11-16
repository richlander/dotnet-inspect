using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Diff;

/// <summary>
/// One Finding Diff Any Library-level request over a host-opened Library pair
/// (finding-diff-any.md#library). The route runs the comparison to completion;
/// a host that slices it runs its own steps and projects the result through
/// <see cref="FindingDiffAnyLibraryInspection.Project"/>.
/// </summary>
public sealed record FindingDiffAnyLibraryRequest(
    string Name,
    string BeforeVersion,
    string AfterVersion,
    DiffLibraryPair Pair,
    FindingDiffAnyAxes Axes = FindingDiffAnyAxes.ApiAndBody);

/// <summary>What one Library-level comparison compared.</summary>
public sealed record FindingDiffAnyLibraryComparison(
    string Name,
    string BeforeVersion,
    string AfterVersion,
    FindingDiffAnyAxes Axes);

public enum FindingDiffAnyLibraryOutcomeKind
{
    /// <summary>Both images were compared; every Type has API and Body states.</summary>
    Compared,

    /// <summary>An endpoint image could not be opened or admitted.</summary>
    Rejected,
}

/// <summary>
/// One Type whose API or Body state is <c>Changed</c> or
/// <c>Indeterminate</c>. <see cref="Type"/> is the display spelling and
/// <see cref="Identifier"/> the injective escaped definition name.
/// </summary>
public sealed record FindingDiffAnyLibraryRow(
    string Type,
    FindingDiffAnyState Api,
    FindingDiffAnyState Body,
    string Identifier);

/// <summary>The reported Types per axis state.</summary>
public sealed record FindingDiffAnyLibraryCounts(
    int ReportedTypes,
    int ApiChanged,
    int ApiIndeterminate,
    int BodyChanged,
    int BodyIndeterminate);

/// <summary>
/// The work the comparison performed: every Type it compared, including the
/// unreported <c>Unchanged</c> ones, the paired bodies and IL bytes the Body
/// walk read, and the per-Type API surfaces it built.
/// </summary>
public sealed record FindingDiffAnyLibraryReceipt(
    int ComparedTypes,
    int BodiesCompared,
    long IlBytesCompared,
    int ApiSurfacesBuilt);

/// <summary>The Table representation: the reported Types.</summary>
public sealed record FindingDiffAnyLibraryTable(
    ImmutableArray<FindingDiffAnyLibraryRow> Rows);

/// <summary>
/// The Document representation: what was compared, its outcome, the reported
/// Types in comparison order, their counts, and the comparison's receipt.
/// </summary>
public sealed record FindingDiffAnyLibraryDocument(
    FindingDiffAnyLibraryComparison Comparison,
    FindingDiffAnyLibraryOutcomeKind Outcome,
    ImmutableArray<FindingDiffAnyLibraryRow> Rows,
    FindingDiffAnyLibraryCounts? Counts,
    FindingDiffAnyLibraryReceipt? Receipt,
    string? Detail = null);

/// <summary>
/// Finding Diff Any's Library level as a section query: the reported Types of
/// one Library pair, with typed Table and Document representations.
/// </summary>
public static class FindingDiffAnyLibraryInspection
{
    /// <summary>The owner-issued result contract of the Library-level document.</summary>
    public const string ResultContractIdentity = "finding-diff-any-library/v1";

    public const string RejectedDiagnosticCode = "finding-diff-any.rejected";

    /// <summary>Runs the comparison to completion over the request's Library pair.</summary>
    public static InspectionEnvelope<FindingDiffAnyLibraryDocument> Execute(
        FindingDiffAnyLibraryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BeforeVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AfterVersion);
        ArgumentNullException.ThrowIfNull(request.Pair);
        if (!Enum.IsDefined(request.Axes))
            throw new ArgumentOutOfRangeException(nameof(request), "The Finding Diff Any axes are unsupported.");

        var comparison = new FindingDiffAnyLibraryComparison(
            request.Name,
            request.BeforeVersion,
            request.AfterVersion,
            request.Axes);
        AssemblyContextFindingDiffAnyOutcome outcome =
            AssemblyContextFindingDiffAnyQuery.Execute(
                request.Pair.BeforeGroup,
                request.Pair.Before,
                request.Pair.AfterGroup,
                request.Pair.After,
                new FindingDiffAnyComparison(request.Axes),
                Timeout.InfiniteTimeSpan,
                cancellationToken);
        return outcome switch
        {
            AssemblyContextFindingDiffAnyOutcome.Compared compared =>
                Project(comparison, compared.Result),
            AssemblyContextFindingDiffAnyOutcome.Rejected rejected =>
                Rejected(comparison, rejected.Reason),
            _ => throw new InvalidOperationException(
                "An unsliced Finding Diff Any did not complete."),
        };
    }

    /// <summary>
    /// The document of one completed comparison: its Types with a
    /// <c>Changed</c> or <c>Indeterminate</c> axis, in comparison order.
    /// </summary>
    public static InspectionEnvelope<FindingDiffAnyLibraryDocument> Project(
        FindingDiffAnyLibraryComparison comparison,
        FindingDiffAnyResult result)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(result);
        ImmutableArray<FindingDiffAnyLibraryRow> rows =
        [
            .. result.Types
                .Where(IsReported)
                .Select(static type => new FindingDiffAnyLibraryRow(
                    type.FullName,
                    type.Api,
                    type.Body,
                    type.Identifier)),
        ];
        return new(
            new FindingDiffAnyLibraryDocument(
                comparison,
                FindingDiffAnyLibraryOutcomeKind.Compared,
                rows,
                new FindingDiffAnyLibraryCounts(
                    rows.Length,
                    rows.Count(static row => row.Api == FindingDiffAnyState.Changed),
                    rows.Count(static row => row.Api == FindingDiffAnyState.Indeterminate),
                    rows.Count(static row => row.Body == FindingDiffAnyState.Changed),
                    rows.Count(static row => row.Body == FindingDiffAnyState.Indeterminate)),
                new FindingDiffAnyLibraryReceipt(
                    result.Types.Length,
                    result.Receipt.BodiesCompared,
                    result.Receipt.IlBytesCompared,
                    result.Receipt.ApiSurfacesBuilt)),
            EndpointPairShare);
    }

    /// <summary>The Table representation of a document: its reported Types.</summary>
    public static InspectionEnvelope<FindingDiffAnyLibraryTable> Table(
        InspectionEnvelope<FindingDiffAnyLibraryDocument> document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(
            new FindingDiffAnyLibraryTable(document.Content.Rows),
            document.Share,
            document.Diagnostics);
    }

    /// <summary>
    /// Whether a Type is reported: an axis is <c>Changed</c> or
    /// <c>Indeterminate</c>. Absence means every compared axis is
    /// <c>Unchanged</c>.
    /// </summary>
    public static bool IsReported(FindingDiffAnyTypeState type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return IsReported(type.Api) || IsReported(type.Body);
    }

    static bool IsReported(FindingDiffAnyState state)
        => state is FindingDiffAnyState.Changed or FindingDiffAnyState.Indeterminate;

    static InspectionEnvelope<FindingDiffAnyLibraryDocument> Rejected(
        FindingDiffAnyLibraryComparison comparison,
        string reason)
        => new(
            new FindingDiffAnyLibraryDocument(
                comparison,
                FindingDiffAnyLibraryOutcomeKind.Rejected,
                [],
                Counts: null,
                Receipt: null,
                reason),
            EndpointPairShare,
            [
                new InspectionDiagnostic(
                    RejectedDiagnosticCode,
                    InspectionDiagnosticSeverity.Error,
                    reason),
            ]);

    static InspectionShare EndpointPairShare { get; } =
        new InspectionShare.NonProjectable(
            "comparison/endpoints",
            "No portable projection is available for an ordered Finding Diff Any endpoint pair.");
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(FindingDiffAnyLibraryDocument))]
[JsonSerializable(typeof(FindingDiffAnyLibraryTable))]
public partial class FindingDiffAnyLibraryJsonContext : JsonSerializerContext;
