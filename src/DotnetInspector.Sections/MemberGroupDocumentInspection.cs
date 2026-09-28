using System.Text.Json.Serialization;

using DotnetInspector.Libraries;

namespace DotnetInspector.Sections;

/// <summary>
/// A resource-free document for one exact member group and its requested
/// overload population.
/// </summary>
public sealed record MemberGroupDocument(
    MemberGroupSubject Subject,
    MemberOverloadPopulationResult Overloads);

public sealed record MemberGroupDocumentInspectionRequest(
    LibraryReference Library,
    MemberOverloadPopulationInspectionPlan Plan);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(
    typeof(MemberGroupDocumentInspectionOutcome.Available),
    typeDiscriminator: "available")]
[JsonDerivedType(
    typeof(MemberGroupDocumentInspectionOutcome.Rejected),
    typeDiscriminator: "rejected")]
[JsonDerivedType(
    typeof(MemberGroupDocumentInspectionOutcome.Incomplete),
    typeDiscriminator: "incomplete")]
[JsonDerivedType(
    typeof(MemberGroupDocumentInspectionOutcome.Failed),
    typeDiscriminator: "failed")]
public abstract record MemberGroupDocumentInspectionOutcome
{
    private MemberGroupDocumentInspectionOutcome()
    {
    }

    public sealed record Available(MemberGroupDocument Document)
        : MemberGroupDocumentInspectionOutcome;

    public sealed record Rejected(
        MemberOverloadPopulationInspectionRejection Reason)
        : MemberGroupDocumentInspectionOutcome;

    public sealed record Incomplete(
        MemberOverloadPopulationBound Bound,
        long Limit,
        long Measured)
        : MemberGroupDocumentInspectionOutcome;

    public sealed record Failed(
        MemberOverloadPopulationInspectionFailure Reason)
        : MemberGroupDocumentInspectionOutcome;
}

/// <summary>
/// Composes the exact-overload population into one owner-issued member-group
/// document without reconstructing population semantics.
/// </summary>
public static class MemberGroupDocumentInspectionOperation
{
    public static InspectionEnvelope<MemberGroupDocumentInspectionOutcome> Execute(
        MemberGroupDocumentInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Library);
        ArgumentNullException.ThrowIfNull(request.Plan);
        ArgumentNullException.ThrowIfNull(lease);

        InspectionEnvelope<MemberOverloadPopulationInspectionOutcome> population =
            MemberOverloadPopulationInspectionOperation.Execute(
                new(request.Library, request.Plan),
                lease,
                cancellationToken);
        return new(
            Project(population.Content),
            population.Share,
            population.Diagnostics);
    }

    private static MemberGroupDocumentInspectionOutcome Project(
        MemberOverloadPopulationInspectionOutcome outcome) =>
        outcome switch
        {
            MemberOverloadPopulationInspectionOutcome.Available available =>
                new MemberGroupDocumentInspectionOutcome.Available(
                    new MemberGroupDocument(
                        available.Content.Subject,
                        available.Content.Overloads)),
            MemberOverloadPopulationInspectionOutcome.Rejected rejected =>
                new MemberGroupDocumentInspectionOutcome.Rejected(
                    rejected.Reason),
            MemberOverloadPopulationInspectionOutcome.Incomplete incomplete =>
                new MemberGroupDocumentInspectionOutcome.Incomplete(
                    incomplete.Bound,
                    incomplete.Limit,
                    incomplete.Measured),
            MemberOverloadPopulationInspectionOutcome.Failed failed =>
                new MemberGroupDocumentInspectionOutcome.Failed(
                    failed.Reason),
            _ => throw new InvalidOperationException(
                "Unknown member-overload population inspection outcome."),
        };
}
