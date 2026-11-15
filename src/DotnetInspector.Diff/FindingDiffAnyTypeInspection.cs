using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Diff;

/// <summary>
/// One Finding Diff Any Type-level request over a host-opened Library pair
/// and one exact escaped Type definition identity (finding-diff-any.md#type).
/// The route runs the comparison to completion; a host that steps it runs its
/// own steps and projects the result through
/// <see cref="FindingDiffAnyTypeInspection.Project"/>.
/// </summary>
public sealed record FindingDiffAnyTypeRequest(
    string Name,
    string BeforeVersion,
    string AfterVersion,
    DiffLibraryPair Pair,
    string Type);

/// <summary>What one Type-level comparison compared.</summary>
public sealed record FindingDiffAnyTypeComparison(
    string Name,
    string BeforeVersion,
    string AfterVersion,
    string Type);

public enum FindingDiffAnyTypeOutcomeKind
{
    /// <summary>The Type was compared; every Member and the residual have API and Body states.</summary>
    Compared,

    /// <summary>
    /// An endpoint image could not be opened or admitted, or the Type is
    /// absent from both or does not resolve uniquely in one.
    /// </summary>
    Rejected,
}

/// <summary>Which owner of the Type's facts a row reports.</summary>
public enum FindingDiffAnyTypeOwner
{
    /// <summary>One Member declared on either side.</summary>
    Member,

    /// <summary>
    /// The facts no Member owns, such as the Type's own declaration and its
    /// presence.
    /// </summary>
    Residual,
}

/// <summary>
/// One owner whose API or Body state is <c>Changed</c> or
/// <c>Indeterminate</c>. A Member row carries the canonical signature and
/// stable selector of the anchor it is reported by, the After-side anchor or
/// a removed Member's Before-side anchor, and each side's anchor fingerprint;
/// the residual row carries none of them.
/// </summary>
public sealed record FindingDiffAnyTypeRow(
    FindingDiffAnyTypeOwner Owner,
    string? Member,
    FindingDiffAnyState Api,
    FindingDiffAnyState Body,
    string? Selector = null,
    string? BeforeFingerprint = null,
    string? AfterFingerprint = null);

/// <summary>The Type's own states and its reported owners per axis state.</summary>
public sealed record FindingDiffAnyTypeCounts(
    FindingDiffAnyState Api,
    FindingDiffAnyState Body,
    int ReportedRows,
    int ApiChanged,
    int ApiIndeterminate,
    int BodyChanged,
    int BodyIndeterminate);

/// <summary>
/// The work the comparison performed: the Types each side read, which for a
/// Type-addressed query is the addressed Type alone, every Member it compared,
/// including the unreported <c>Unchanged</c> ones, the paired bodies and IL
/// bytes the Body walk read, and the single-Type API surfaces it built.
/// </summary>
public sealed record FindingDiffAnyTypeLevelReceipt(
    ImmutableArray<string> TypesBefore,
    ImmutableArray<string> TypesAfter,
    int ComparedMembers,
    int BodiesCompared,
    long IlBytesCompared,
    int ApiSurfacesBuilt);

/// <summary>The Table representation: the reported owners.</summary>
public sealed record FindingDiffAnyTypeTable(
    ImmutableArray<FindingDiffAnyTypeRow> Rows);

/// <summary>
/// The Document representation: what was compared, its outcome, the Type's
/// display name, the reported owners in comparison order with the residual
/// last, their counts, and the comparison's receipt.
/// </summary>
public sealed record FindingDiffAnyTypeDocument(
    FindingDiffAnyTypeComparison Comparison,
    FindingDiffAnyTypeOutcomeKind Outcome,
    string? FullName,
    ImmutableArray<FindingDiffAnyTypeRow> Rows,
    FindingDiffAnyTypeCounts? Counts,
    FindingDiffAnyTypeLevelReceipt? Receipt,
    string? Detail = null);

/// <summary>
/// Finding Diff Any's Type level as a section query: the reported Members and
/// residual of one Type of one Library pair, with typed Table and Document
/// representations. It pairs the addressed Type by its identity and reads no
/// other Type (diff-capabilities.md#demand-shaped).
/// </summary>
public static class FindingDiffAnyTypeInspection
{
    /// <summary>The owner-issued result contract of the Type-level document.</summary>
    public const string ResultContractIdentity = "finding-diff-any-members/v1";

    /// <summary>Runs the comparison to completion over the request's Library pair and Type.</summary>
    public static async ValueTask<InspectionEnvelope<FindingDiffAnyTypeDocument>> ExecuteAsync(
        FindingDiffAnyTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BeforeVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AfterVersion);
        ArgumentNullException.ThrowIfNull(request.Pair);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Type);

        var comparison = new FindingDiffAnyTypeComparison(
            request.Name,
            request.BeforeVersion,
            request.AfterVersion,
            request.Type);
        AssemblyContextFindingDiffAnyTypeOutcome outcome =
            await AssemblyContextFindingDiffAnyTypeQuery.ExecuteAsync(
                request.Pair.BeforeGroup,
                request.Pair.Before,
                request.Pair.AfterGroup,
                request.Pair.After,
                request.Type,
                TimeSpan.MaxValue,
                cancellationToken).ConfigureAwait(false);
        return outcome switch
        {
            AssemblyContextFindingDiffAnyTypeOutcome.Compared compared =>
                Project(comparison, compared.Result),
            AssemblyContextFindingDiffAnyTypeOutcome.Rejected rejected =>
                Rejected(comparison, rejected.Reason),
            _ => throw new InvalidOperationException(
                "Unknown Finding Diff Any Type outcome."),
        };
    }

    /// <summary>
    /// The document of one completed comparison: its Members with a
    /// <c>Changed</c> or <c>Indeterminate</c> axis in comparison order, then
    /// the residual when it is reported.
    /// </summary>
    public static InspectionEnvelope<FindingDiffAnyTypeDocument> Project(
        FindingDiffAnyTypeComparison comparison,
        FindingDiffAnyTypeResult result)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(result);
        ImmutableArray<FindingDiffAnyTypeRow>.Builder rows =
            ImmutableArray.CreateBuilder<FindingDiffAnyTypeRow>();
        foreach (FindingDiffAnyTypeMember member in result.Members)
        {
            if (IsReported(member.Api) || IsReported(member.Body))
            {
                rows.Add(new FindingDiffAnyTypeRow(
                    FindingDiffAnyTypeOwner.Member,
                    member.Signature,
                    member.Api,
                    member.Body,
                    member.Selector,
                    member.BeforeFingerprint,
                    member.AfterFingerprint));
            }
        }
        if (IsReported(result.ResidualApi) || IsReported(result.ResidualBody))
        {
            rows.Add(new FindingDiffAnyTypeRow(
                FindingDiffAnyTypeOwner.Residual,
                Member: null,
                result.ResidualApi,
                result.ResidualBody));
        }
        ImmutableArray<FindingDiffAnyTypeRow> reported = rows.ToImmutable();
        return new(
            new FindingDiffAnyTypeDocument(
                comparison,
                FindingDiffAnyTypeOutcomeKind.Compared,
                result.FullName,
                reported,
                new FindingDiffAnyTypeCounts(
                    result.Api,
                    result.Body,
                    reported.Length,
                    reported.Count(static row => row.Api == FindingDiffAnyState.Changed),
                    reported.Count(static row => row.Api == FindingDiffAnyState.Indeterminate),
                    reported.Count(static row => row.Body == FindingDiffAnyState.Changed),
                    reported.Count(static row => row.Body == FindingDiffAnyState.Indeterminate)),
                new FindingDiffAnyTypeLevelReceipt(
                    result.Receipt.TypesBefore,
                    result.Receipt.TypesAfter,
                    result.Members.Length,
                    result.Receipt.BodiesCompared,
                    result.Receipt.IlBytesCompared,
                    result.Receipt.ApiSurfacesBuilt)),
            EndpointPairShare);
    }

    /// <summary>The Table representation of a document: its reported owners.</summary>
    public static InspectionEnvelope<FindingDiffAnyTypeTable> Table(
        InspectionEnvelope<FindingDiffAnyTypeDocument> document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(
            new FindingDiffAnyTypeTable(document.Content.Rows),
            document.Share,
            document.Diagnostics);
    }

    /// <summary>The document of a request the query rejected.</summary>
    public static InspectionEnvelope<FindingDiffAnyTypeDocument> Rejected(
        FindingDiffAnyTypeComparison comparison,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(
            new FindingDiffAnyTypeDocument(
                comparison,
                FindingDiffAnyTypeOutcomeKind.Rejected,
                FullName: null,
                [],
                Counts: null,
                Receipt: null,
                reason),
            EndpointPairShare,
            [
                new InspectionDiagnostic(
                    FindingDiffAnyLibraryInspection.RejectedDiagnosticCode,
                    InspectionDiagnosticSeverity.Error,
                    reason),
            ]);
    }

    static bool IsReported(FindingDiffAnyState state)
        => state is FindingDiffAnyState.Changed or FindingDiffAnyState.Indeterminate;

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
[JsonSerializable(typeof(FindingDiffAnyTypeDocument))]
[JsonSerializable(typeof(FindingDiffAnyTypeTable))]
public partial class FindingDiffAnyTypeJsonContext : JsonSerializerContext;
