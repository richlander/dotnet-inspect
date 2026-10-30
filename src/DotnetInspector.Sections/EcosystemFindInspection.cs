using System.Text.Json.Serialization;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public sealed record EcosystemFindInspectionEvidence(
    EcosystemFindSearchEvidence Search,
    string? TargetFramework,
    string? TypeFilter,
    string AcquisitionReceiptsUnavailableReason);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(EcosystemFindInspectionEvidence))]
[JsonSerializable(typeof(TypeFindPattern.Glob), TypeInfoPropertyName = "TypeGlob")]
[JsonSerializable(typeof(MemberFindPattern.Glob), TypeInfoPropertyName = "MemberGlob")]
public partial class EcosystemFindInspectionEvidenceJsonContext : JsonSerializerContext;

/// <summary>Completes the staged Ecosystem operation without a host-specific scheduler.</summary>
public static class EcosystemFindInspection
{
    public static async ValueTask<InspectionEnvelope<EcosystemFindSearchSummary<T>>>
        ExecuteAsync<T>(
            EcosystemFindSearchSession<T> session,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Envelope(await session.ExecuteAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    public static async ValueTask<EvidenceInspectionEnvelope<
        EcosystemFindSearchSummary<T>, EcosystemFindInspectionEvidence>>
        ExecuteWithEvidenceAsync<T>(
            EcosystemFindSearchSession<T> session,
            string acquisitionReceiptsUnavailableReason,
            string? targetFramework = null,
            string? typeFilter = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(acquisitionReceiptsUnavailableReason);
        var (summary, evidence) = await session
            .ExecuteWithEvidenceAsync(cancellationToken).ConfigureAwait(false);
        return new(Envelope(summary),
            new(evidence, targetFramework, typeFilter, acquisitionReceiptsUnavailableReason));
    }

    private static InspectionEnvelope<EcosystemFindSearchSummary<T>> Envelope<T>(
        EcosystemFindSearchSummary<T> summary) =>
        new(summary,
            new InspectionShare.NonProjectable(
                "ecosystem-find/share",
                "Ecosystem Find has no canonical Workspace Share projection."),
            summary.Failures.Select(failure => new InspectionDiagnostic(
                $"ecosystem-find/{failure.Phase.ToLowerInvariant()}",
                InspectionDiagnosticSeverity.Error,
                failure.Message,
                failure.Source)));
}
